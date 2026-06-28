// [XPLAT] migrated from net48/WinForms FormUDP.cs + FormUDP.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using AgIO.Properties;
using AgIO.Services;
using AgLibrary.Logging;
using AgOpenGPS.Core.ViewModels;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] View-model backing the Avalonia UDP dialog (replaces the WinForms <c>FormUDP</c>). It
    /// discovers AgOpenGPS hardware modules (Steer / Machine / GPS / IMU) on the local network by
    /// broadcasting the frozen <b>PGN 202</b> module-scan request, displays the modules' reported IP
    /// addresses and subnet, and pushes a new IP subnet to the modules with the frozen <b>PGN 201</b>
    /// set-subnet frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The original dialog reached back into the <c>FormLoop mf</c> instance
    /// for every piece of shared state. Those references are replaced by the injected
    /// <see cref="CommCoordinatorService"/>: scan-reply state comes from <c>_comm.Udp.ScanReply</c>, the
    /// scan/subnet broadcast endpoint from <c>_comm.Udp.epModuleSet</c>, the live module data endpoint
    /// from <c>_comm.Udp.EpModule</c>, and the per-module "online" status from the coordinator's
    /// <see cref="CommCoordinatorService.IsSteerConnected"/>/<c>IsMachineConnected</c>/<c>IsGpsConnected</c>/
    /// <c>IsImuConnected</c> flags (the WinForms code read these from button back-colours, a UI-as-state
    /// hack that is removed here). Dialog actions that the original performed directly — opening the UDP
    /// monitor, showing the restart confirmation, restarting the program, and closing — are surfaced as
    /// the <see cref="RequestOpenMonitor"/>, <see cref="RequestConfirm"/>, <see cref="RequestRestart"/>
    /// and <see cref="RequestClose"/> events for the hosting view to handle.
    /// </para>
    /// <para>
    /// <b>Frozen protocol contract (AAP §0.2.2 / §0.7.1, docs/pgn-protocol.md).</b> The
    /// <see cref="sendIPToModules"/> (PGN 201) and the local <c>scanModules</c> (PGN 202) byte arrays are
    /// preserved byte-for-byte, as are the discovery broadcast endpoint (255.255.255.255:8888) and the
    /// NIC bind port (9999). This dialog only <i>displays</i> scan results and <i>sends</i> these exact
    /// frames; it must never alter them.
    /// </para>
    /// <para>
    /// <b>Cross-platform.</b> NIC enumeration and the UDP socket code are already portable and kept
    /// verbatim. The only Windows-only action — launching the <c>ncpa.cpl</c> Network Connections
    /// applet — is runtime-guarded with <see cref="OperatingSystem.IsWindows"/> and the button is hidden
    /// off-Windows via <see cref="IsWindows"/>. Every numeric/IP-octet <c>ToString</c>/parse uses
    /// <see cref="CultureInfo.InvariantCulture"/> so a comma-decimal locale can never corrupt an address
    /// (AAP §0.6.5).
    /// </para>
    /// </remarks>
    public class FormUDPViewModel : ViewModel
    {
        // ===========================================================================================
        //  Constants — App.axaml status-brush keys and the exact parity colours used as a fallback.
        // ===========================================================================================

        // [XPLAT] Shared status-brush resource keys defined once in AgIO App.axaml (the stable key
        // contract). Resolved at construction so the module indicators bind to the central definition.
        private const string ModuleOkBrushKey = "AogModuleOkBrush";
        private const string ModuleAlarmBrushKey = "AogModuleAlarmBrush";

        // [XPLAT] Exact GDI parity colours (the WinForms original used System.Drawing.Color). Used as a
        // safe fallback when the App.axaml resource cannot be resolved (e.g. a headless/test context),
        // and directly for the network-help match/mismatch indicator, which has no App.axaml resource.
        //   LimeGreen  #FF32CD32 — a module is talking / the PC adapter is on the module subnet.
        //   Red        #FFFF0000 — a module is silent / absent.
        //   LightGreen #FF90EE90 — the PC has an adapter on the current module subnet.
        //   Salmon     #FFFA8072 — no adapter matches the current module subnet.
        private static readonly Color LimeGreen = Color.FromArgb(0xFF, 0x32, 0xCD, 0x32);
        private static readonly Color Red = Color.FromArgb(0xFF, 0xFF, 0x00, 0x00);
        private static readonly Color LightGreen = Color.FromArgb(0xFF, 0x90, 0xEE, 0x90);
        private static readonly Color Salmon = Color.FromArgb(0xFF, 0xFA, 0x80, 0x72);

        // ===========================================================================================
        //  Collaborators and transport state.
        // ===========================================================================================

        // [XPLAT] The injected comms-hub facade replacing the WinForms FormLoop "mf" back-reference.
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Display/scan-cadence timer reproducing the WinForms timer1 (500 ms). It updates the
        // displayed scan results and re-runs the network scan on the original tick cadence. It drives no
        // part of the comm/byte path, so it adds no latency (AAP §0.6.1).
        private readonly DispatcherTimer _timer;
        private int _tickCounter;

        // [XPLAT] FROZEN PGN 201 set-subnet frame — byte-for-byte (docs/pgn-protocol.md). Bytes [7],[8],[9]
        // are overwritten with the new subnet octets immediately before the frame is broadcast; every
        // other byte (header 0x80 0x81 0x7F, id 201, length, source/dest 201, and the 0x47 trailer) is
        // immutable. DO NOT reformat or change any value.
        private readonly byte[] sendIPToModules = { 0x80, 0x81, 0x7F, 201, 5, 201, 201, 192, 168, 5, 0x47 };

        // The PC's current module subnet (first three octets), used to test whether any local adapter is
        // on the module subnet. Seeded from settings in the constructor (parity with the original field
        // initialised to { 192, 168, 5 }).
        private readonly byte[] _ipCurrent = { 192, 168, 5 };

        // The pending (edited) subnet octets that the set-subnet frame will carry. Kept in lock-step with
        // the IpFirst/IpSecond/IpThird display properties.
        private readonly byte[] _ipNew = { 192, 168, 5 };

        // Cached status-brush instances (resolved once) so the timer can flip indicators without
        // re-resolving resources every tick.
        private readonly IBrush _moduleOkBrush;
        private readonly IBrush _moduleAlarmBrush;
        private readonly IBrush _subnetMatchBrush;
        private readonly IBrush _subnetMismatchBrush;

        // Backing fields for the bound display properties.
        private string _steerIpText = string.Empty;
        private string _machineIpText = string.Empty;
        private string _gpsIpText = string.Empty;
        private string _imuIpText = string.Empty;
        private string _newSubnetText = string.Empty;
        private string _hostName = string.Empty;
        private string _networkHelpText = string.Empty;
        private string _netsText = string.Empty;
        private string _subTimerText = "-";
        private string _filterUpText = "Up";

        private byte _ipFirst = 192;
        private byte _ipSecond = 168;
        private byte _ipThird = 5;

        private bool _canAutoSet;
        private bool _isSendIndicatorVisible;
        private bool _isNoAdapterVisible;
        private bool _filterUpOnly = true;

        private IBrush _steerBrush;
        private IBrush _machineBrush;
        private IBrush _gpsBrush;
        private IBrush _imuBrush;
        private IBrush _networkHelpBrush;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormUDPViewModel"/> class, reproducing the
        /// WinForms <c>FormUDP</c> constructor plus <c>FormUDp_Load</c>: it resets the coordinator's
        /// auto-set markers, reads the host name, seeds the subnet octets from settings, performs the
        /// initial network scan, and starts the 500 ms display/scan timer.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub facade that owns the UDP transport and live module status. Must not be
        /// <c>null</c>.
        /// </param>
        public FormUDPViewModel(CommCoordinatorService comm)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;

            // Resolve the shared status brushes from App.axaml (single source of truth) with a safe,
            // parity-coloured fallback. The match/mismatch brushes have no App.axaml resource, so they
            // are constructed directly from the exact GDI colours the WinForms original used.
            _moduleOkBrush = ResolveBrush(ModuleOkBrushKey, LimeGreen);
            _moduleAlarmBrush = ResolveBrush(ModuleAlarmBrushKey, Red);
            _subnetMatchBrush = new SolidColorBrush(LightGreen);
            _subnetMismatchBrush = new SolidColorBrush(Salmon);

            // Modules start in the "not yet heard from" (alarm) state until the first status tick; the
            // network-help indicator starts in the mismatch state until the first scan proves otherwise.
            _steerBrush = _moduleAlarmBrush;
            _machineBrush = _moduleAlarmBrush;
            _gpsBrush = _moduleAlarmBrush;
            _imuBrush = _moduleAlarmBrush;
            _networkHelpBrush = _subnetMismatchBrush;

            // Commands. SendSubnet mirrors the always-enabled WinForms button; AutoSet is gated on a
            // fresh scan reply exactly as the original toggled btnAutoSet.Enabled.
            ScanCommand = new RelayCommand(ScanNetwork);
            SendSubnetCommand = new RelayCommand(SendSubnet);
            AutoSetCommand = new RelayCommand(AutoSet, () => CanAutoSet);
            OpenNetworkCplCommand = new RelayCommand(OpenNetworkControlPanel, () => IsWindows);
            OpenMonitorCommand = new RelayCommand(() => RequestOpenMonitor());
            UdpOffCommand = new RelayCommand(TurnUdpOff);
            OkCommand = new RelayCommand(() => RequestClose());
            CancelCommand = new RelayCommand(() => RequestClose());
            ToggleFilterUpCommand = new RelayCommand(ToggleFilterUp);
            EditIpFirstCommand = new RelayCommand(() => BeginEditOctet(0));
            EditIpSecondCommand = new RelayCommand(() => BeginEditOctet(1));
            EditIpThirdCommand = new RelayCommand(() => BeginEditOctet(2));

            // Parity with FormUDp_Load: clear the coordinator's auto-set markers (99 = "none").
            ResetAutoSetMarkers();

            // Host name shown at the top of the dialog.
            HostName = Dns.GetHostName();

            // Seed the working/current subnet copies and the bound octets from persisted settings. The
            // display octets are assigned through their backing fields so seeding does not mark the
            // subnet "dirty" (the send indicator stays hidden until the operator actually edits a value).
            byte one = Settings.Default.etIP_SubnetOne;
            byte two = Settings.Default.etIP_SubnetTwo;
            byte three = Settings.Default.etIP_SubnetThree;

            _ipNew[0] = _ipCurrent[0] = one;
            _ipNew[1] = _ipCurrent[1] = two;
            _ipNew[2] = _ipCurrent[2] = three;

            _ipFirst = one;
            _ipSecond = two;
            _ipThird = three;

            NetworkHelpText = FormatSubnetSpaced(one, two, three);

            // Initial scan (parity with FormUDp_Load) then start the recurring 500 ms display/scan timer.
            ScanNetwork();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // ===========================================================================================
        //  Bound display properties.
        // ===========================================================================================

        /// <summary>Gets the discovered AutoSteer module IP address (empty until a scan reply arrives).</summary>
        public string SteerIpText
        {
            get { return _steerIpText; }
            private set { if (_steerIpText != value) { _steerIpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the discovered Machine module IP address (empty until a scan reply arrives).</summary>
        public string MachineIpText
        {
            get { return _machineIpText; }
            private set { if (_machineIpText != value) { _machineIpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the discovered GPS module IP address (empty until a scan reply arrives).</summary>
        public string GpsIpText
        {
            get { return _gpsIpText; }
            private set { if (_gpsIpText != value) { _gpsIpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the discovered IMU module IP address (empty until a scan reply arrives).</summary>
        public string ImuIpText
        {
            get { return _imuIpText; }
            private set { if (_imuIpText != value) { _imuIpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the subnet a module last reported in its scan reply (the "new subnet" readout).</summary>
        public string NewSubnetText
        {
            get { return _newSubnetText; }
            private set { if (_newSubnetText != value) { _newSubnetText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the local machine's host name, shown at the top of the dialog.</summary>
        public string HostName
        {
            get { return _hostName; }
            private set { if (_hostName != value) { _hostName = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the currently-saved module subnet, formatted "a . b . c" for the large readout.</summary>
        public string NetworkHelpText
        {
            get { return _networkHelpText; }
            private set { if (_networkHelpText != value) { _networkHelpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the multi-line list of local network interfaces produced by the scan.</summary>
        public string NetsText
        {
            get { return _netsText; }
            private set { if (_netsText != value) { _netsText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the scan-cadence indicator text ("Scanning" on a scan tick, otherwise "-").</summary>
        public string SubTimerText
        {
            get { return _subTimerText; }
            private set { if (_subTimerText != value) { _subTimerText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>
        /// Gets the caption for the interface filter toggle ("Up" when only operational adapters are
        /// listed, "Up + Down" when all adapters are listed), mirroring the original <c>cboxUp</c>.
        /// </summary>
        public string FilterUpText
        {
            get { return _filterUpText; }
            private set { if (_filterUpText != value) { _filterUpText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>
        /// Gets or sets the first octet of the new subnet. Setting it keeps the pending set-subnet frame
        /// in sync and reveals the unsaved-changes indicator.
        /// </summary>
        public byte IpFirst
        {
            get { return _ipFirst; }
            set
            {
                if (_ipFirst != value)
                {
                    _ipFirst = value;
                    _ipNew[0] = value;
                    IsSendIndicatorVisible = true;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the second octet of the new subnet. See <see cref="IpFirst"/>.</summary>
        public byte IpSecond
        {
            get { return _ipSecond; }
            set
            {
                if (_ipSecond != value)
                {
                    _ipSecond = value;
                    _ipNew[1] = value;
                    IsSendIndicatorVisible = true;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the third octet of the new subnet. See <see cref="IpFirst"/>.</summary>
        public byte IpThird
        {
            get { return _ipThird; }
            set
            {
                if (_ipThird != value)
                {
                    _ipThird = value;
                    _ipNew[2] = value;
                    IsSendIndicatorVisible = true;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the auto-set action is available (a module scan reply with a
        /// usable subnet has been received). Toggling it re-queries <see cref="AutoSetCommand"/>.
        /// </summary>
        public bool CanAutoSet
        {
            get { return _canAutoSet; }
            private set
            {
                if (_canAutoSet != value)
                {
                    _canAutoSet = value;
                    NotifyPropertyChanged();
                    AutoSetCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the "unsaved subnet change" send indicator is shown (the
        /// original <c>pboxSendSteer</c>). Set when the operator edits or auto-sets an octet, cleared
        /// once the subnet is broadcast.
        /// </summary>
        public bool IsSendIndicatorVisible
        {
            get { return _isSendIndicatorVisible; }
            private set { if (_isSendIndicatorVisible != value) { _isSendIndicatorVisible = value; NotifyPropertyChanged(); } }
        }

        /// <summary>
        /// Gets a value indicating whether the "no adapter on this subnet" warning is shown (the original
        /// <c>lblNoAdapter</c>). Driven by the scan's subnet-match test.
        /// </summary>
        public bool IsNoAdapterVisible
        {
            get { return _isNoAdapterVisible; }
            private set { if (_isNoAdapterVisible != value) { _isNoAdapterVisible = value; NotifyPropertyChanged(); } }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the scan lists only operational ("Up") interfaces. When
        /// <c>false</c>, all interfaces are listed. Setting it also updates <see cref="FilterUpText"/>.
        /// </summary>
        public bool FilterUpOnly
        {
            get { return _filterUpOnly; }
            set
            {
                if (_filterUpOnly != value)
                {
                    _filterUpOnly = value;
                    FilterUpText = value ? "Up" : "Up + Down";
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host operating system is Windows. The view binds this to
        /// the visibility of the "open Network Connections" (<c>ncpa.cpl</c>) button, which is a
        /// Windows-only shortcut.
        /// </summary>
        public bool IsWindows
        {
            get { return OperatingSystem.IsWindows(); }
        }

        /// <summary>Gets the status brush for the AutoSteer module indicator (good/alarm).</summary>
        public IBrush SteerBrush
        {
            get { return _steerBrush; }
            private set { if (!ReferenceEquals(_steerBrush, value)) { _steerBrush = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the status brush for the Machine module indicator (good/alarm).</summary>
        public IBrush MachineBrush
        {
            get { return _machineBrush; }
            private set { if (!ReferenceEquals(_machineBrush, value)) { _machineBrush = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the status brush for the GPS module indicator (good/alarm).</summary>
        public IBrush GpsBrush
        {
            get { return _gpsBrush; }
            private set { if (!ReferenceEquals(_gpsBrush, value)) { _gpsBrush = value; NotifyPropertyChanged(); } }
        }

        /// <summary>Gets the status brush for the IMU module indicator (good/alarm).</summary>
        public IBrush ImuBrush
        {
            get { return _imuBrush; }
            private set { if (!ReferenceEquals(_imuBrush, value)) { _imuBrush = value; NotifyPropertyChanged(); } }
        }

        /// <summary>
        /// Gets the background brush for the large subnet readout: light green when a local adapter is on
        /// the current module subnet, salmon when none is (parity with the original <c>lblNetworkHelp</c>
        /// back-colour).
        /// </summary>
        public IBrush NetworkHelpBrush
        {
            get { return _networkHelpBrush; }
            private set { if (!ReferenceEquals(_networkHelpBrush, value)) { _networkHelpBrush = value; NotifyPropertyChanged(); } }
        }

        // ===========================================================================================
        //  Commands.
        // ===========================================================================================

        /// <summary>Gets the command that performs an immediate network scan (PGN 202 broadcast).</summary>
        public ICommand ScanCommand { get; }

        /// <summary>Gets the command that broadcasts the new subnet to the modules (PGN 201).</summary>
        public ICommand SendSubnetCommand { get; }

        /// <summary>
        /// Gets the command that copies the most recently reported module subnet into the editable
        /// octets. Enabled only while <see cref="CanAutoSet"/> is <c>true</c>.
        /// </summary>
        public RelayCommand AutoSetCommand { get; }

        /// <summary>
        /// Gets the command that opens the Windows Network Connections applet (<c>ncpa.cpl</c>). Enabled
        /// only on Windows; the button is also hidden off-Windows via <see cref="IsWindows"/>.
        /// </summary>
        public ICommand OpenNetworkCplCommand { get; }

        /// <summary>Gets the command that requests the host open the UDP monitor dialog.</summary>
        public ICommand OpenMonitorCommand { get; }

        /// <summary>
        /// Gets the command that disables UDP networking in settings and requests a program restart.
        /// </summary>
        public ICommand UdpOffCommand { get; }

        /// <summary>Gets the command that closes the dialog (confirm/back).</summary>
        public ICommand OkCommand { get; }

        /// <summary>Gets the command that closes the dialog (cancel/back).</summary>
        public ICommand CancelCommand { get; }

        /// <summary>Gets the command that toggles the interface filter between "Up" and "Up + Down".</summary>
        public ICommand ToggleFilterUpCommand { get; }

        /// <summary>Gets the command that opens the numeric keypad to edit the first subnet octet.</summary>
        public ICommand EditIpFirstCommand { get; }

        /// <summary>Gets the command that opens the numeric keypad to edit the second subnet octet.</summary>
        public ICommand EditIpSecondCommand { get; }

        /// <summary>Gets the command that opens the numeric keypad to edit the third subnet octet.</summary>
        public ICommand EditIpThirdCommand { get; }

        // ===========================================================================================
        //  Events / host interactions (all non-null so they are always safe to invoke).
        // ===========================================================================================

        /// <summary>
        /// Raised to ask the host to show an informational confirmation (the original
        /// <c>YesMessageBox</c>) before a restart. The argument is the message to display.
        /// </summary>
        public event Action<string> RequestConfirm = delegate { };

        /// <summary>Raised to ask the host to open the UDP monitor dialog (<c>FormUDPMonitor</c>).</summary>
        public event Action RequestOpenMonitor = delegate { };

        /// <summary>Raised to ask the host to perform a cross-platform program restart.</summary>
        public event Action RequestRestart = delegate { };

        /// <summary>Raised to ask the host to close the dialog.</summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// The host-supplied numeric keypad interaction. Given <c>(min, max, current)</c> it returns the
        /// entered value, or <c>null</c> if the operator cancelled. Replaces the WinForms
        /// <c>NumericUpDown.ShowKeypad</c> with an Avalonia <c>FormNumeric</c> shown via
        /// <c>ShowDialog&lt;double?&gt;</c>. Defaults to a no-op that returns <c>null</c> so the
        /// view-model is safe to use before the host wires it.
        /// </summary>
        public Func<double, double, double, Task<double?>> RequestKeypad { get; set; }
            = (min, max, current) => Task.FromResult<double?>(null);

        // ===========================================================================================
        //  Lifecycle.
        // ===========================================================================================

        /// <summary>
        /// Stops and detaches the display/scan timer. The hosting view must call this when the dialog is
        /// closed so the timer does not keep firing against a discarded view-model.
        /// </summary>
        public void Cleanup()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
            }
        }

        // ===========================================================================================
        //  Timer — display/scan cadence (reproduces WinForms timer1_Tick, 500 ms).
        // ===========================================================================================

        // [XPLAT] Reproduces FormUDP.timer1_Tick byte-for-byte in behaviour, reading the migrated scan
        // state from _comm.Udp.ScanReply and the live module status from the coordinator's connection
        // flags instead of the FormLoop "mf" back-reference and button back-colours. This timer is purely
        // a display/scan-cadence driver; it never touches the comm/byte path, so it adds no latency.
        private void OnTimerTick(object sender, EventArgs e)
        {
            UdpLoopbackService udp = _comm.Udp;
            if (udp == null)
            {
                return;
            }

            CScanReply reply = udp.ScanReply;
            if (reply == null)
            {
                return;
            }

            // Auto-set availability tracks whether a usable module scan reply has arrived. When none has,
            // the coordinator's auto-set markers are reset to the "none" sentinel (99) exactly as the
            // original cleared mf.ipAutoSet and disabled btnAutoSet.
            if (!reply.isNewData)
            {
                ResetAutoSetMarkers();
                CanAutoSet = false;
            }
            else
            {
                CanAutoSet = true;
            }

            // Latch each newly-reported module address into its readout and clear the "new" flag, so the
            // value persists between scans (parity with the original per-module isNew* handling).
            if (reply.isNewSteer)
            {
                SteerIpText = reply.steerIP ?? string.Empty;
                reply.isNewSteer = false;
                NewSubnetText = reply.subnetStr ?? string.Empty;
            }

            if (reply.isNewMachine)
            {
                MachineIpText = reply.machineIP ?? string.Empty;
                reply.isNewMachine = false;
                NewSubnetText = reply.subnetStr ?? string.Empty;
            }

            if (reply.isNewIMU)
            {
                ImuIpText = reply.IMU_IP ?? string.Empty;
                reply.isNewIMU = false;
                NewSubnetText = reply.subnetStr ?? string.Empty;
            }

            if (reply.isNewGPS)
            {
                GpsIpText = reply.GPS_IP ?? string.Empty;
                reply.isNewGPS = false;
                NewSubnetText = reply.subnetStr ?? string.Empty;
            }

            // On the original's 5th tick, refresh the four module indicators from the coordinator's live
            // connection status (which replaces the WinForms btnX.BackColor == LimeGreen state hack).
            if (_tickCounter == 4)
            {
                SteerBrush = _comm.IsSteerConnected ? _moduleOkBrush : _moduleAlarmBrush;
                MachineBrush = _comm.IsMachineConnected ? _moduleOkBrush : _moduleAlarmBrush;
                GpsBrush = _comm.IsGpsConnected ? _moduleOkBrush : _moduleAlarmBrush;
                ImuBrush = _comm.IsImuConnected ? _moduleOkBrush : _moduleAlarmBrush;
            }

            // Re-scan every sixth tick (~3 s), mirroring the original cadence and the "Scanning"/"-"
            // indicator toggle.
            if (_tickCounter > 5)
            {
                ScanNetwork();
                _tickCounter = 0;
                SubTimerText = "Scanning";
            }
            else
            {
                SubTimerText = "-";
            }

            _tickCounter++;
        }

        // ===========================================================================================
        //  Network scan and subnet push (frozen PGN frames).
        // ===========================================================================================

        // [XPLAT] Reproduces FormUDP.ScanNetwork. Enumerates the local NICs (cross-platform), builds the
        // diagnostic interface list, tests whether any local adapter is on the current module subnet, and
        // broadcasts the FROZEN PGN 202 module-scan request out of every operational IPv4 interface bound
        // to port 9999. The scanModules byte array, the bind port (9999), and the broadcast endpoint
        // (_comm.Udp.epModuleSet = 255.255.255.255:8888) are byte-/port-exact protocol and must not change.
        private void ScanNetwork()
        {
            UdpLoopbackService udp = _comm.Udp;
            if (udp == null)
            {
                return;
            }

            // Clear the readouts at the start of a scan (parity with the original blanking the labels and
            // the interface text box) so stale values do not linger if a module drops off.
            SteerIpText = string.Empty;
            MachineIpText = string.Empty;
            GpsIpText = string.Empty;
            ImuIpText = string.Empty;
            NewSubnetText = string.Empty;

            if (udp.ScanReply != null)
            {
                udp.ScanReply.isNewData = false;
            }

            bool isSubnetMatchCard = false;

            // [XPLAT] FROZEN PGN 202 module-scan request — byte-for-byte (docs/pgn-protocol.md). Header
            // 0x80 0x81 0x7F, id 202, length 3, source/dest 202, payload 5, 0x47 trailer. DO NOT change.
            byte[] scanModules = { 0x80, 0x81, 0x7F, 202, 3, 202, 202, 5, 0x47 };

            StringBuilder nets = new StringBuilder();

            // Send the scan request out of each installed network interface.
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                {
                    // Only IPv4 and not loopback addresses (these carry a subnet mask).
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address))
                    {
                        continue;
                    }

                    try
                    {
                        // Build the diagnostic interface listing, honouring the "Up only" filter.
                        if ((FilterUpOnly && nic.OperationalStatus == OperationalStatus.Up) || !FilterUpOnly)
                        {
                            // [XPLAT] IPv4Mask can be null on some non-Windows adapters; guard the
                            // diagnostic text (display-only — not a frozen contract) to avoid a spurious
                            // NullReferenceException on Linux/macOS.
                            string mask = info.IPv4Mask != null ? info.IPv4Mask.ToString() : string.Empty;

                            nets.Append(info.Address.ToString());
                            nets.Append("  - ");
                            nets.Append(nic.OperationalStatus.ToString());
                            nets.Append("\r\n");

                            nets.Append(mask);
                            nets.Append("  ");
                            nets.Append(nic.Name);
                            nets.Append("\r\n");

                            // [XPLAT] The per-interface packet counters (IPInterfaceStatistics
                            // NonUnicast/Unicast packet totals) are unsupported on Linux and throw
                            // PlatformNotSupportedException there (CA1416). They are purely a Windows
                            // diagnostic readout — not part of any frozen contract — so they are gathered
                            // only on Windows; off-Windows the address/mask/name/status lines still render.
                            if (OperatingSystem.IsWindows())
                            {
                                IPInterfaceStatistics properties = nic.GetIPStatistics();
                                long sent = properties.NonUnicastPacketsSent + properties.UnicastPacketsSent;
                                long received = properties.NonUnicastPacketsReceived + properties.UnicastPacketsReceived;

                                nets.Append("->");
                                nets.Append(sent.ToString(CultureInfo.InvariantCulture));
                                nets.Append("  <-");
                                nets.Append(received.ToString(CultureInfo.InvariantCulture));
                                nets.Append("\r\n");
                            }

                            nets.Append("\r\n");
                        }

                        if (nic.OperationalStatus == OperationalStatus.Up && info.IPv4Mask != null)
                        {
                            byte[] data = info.Address.GetAddressBytes();
                            if (data[0] == _ipCurrent[0] && data[1] == _ipCurrent[1] && data[2] == _ipCurrent[2])
                            {
                                isSubnetMatchCard = true;
                            }

                            // Send the scan request out of this interface.
                            Socket scanSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                            scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                            scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

                            try
                            {
                                scanSocket.Bind(new IPEndPoint(info.Address, 9999));
                                scanSocket.SendTo(scanModules, 0, scanModules.Length, SocketFlags.None, udp.epModuleSet);
                            }
                            catch (Exception ex)
                            {
                                Log.EventWriter("Catch - > Socket Bind Error Scan UDP" + ex.ToString());
                            }

                            scanSocket.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.EventWriter("Catch - > Nic Loop exception in Scan" + ex.ToString());
                    }
                }
            }

            NetsText = nets.ToString();

            if (isSubnetMatchCard)
            {
                NetworkHelpBrush = _subnetMatchBrush;
                IsNoAdapterVisible = false;
            }
            else
            {
                NetworkHelpBrush = _subnetMismatchBrush;
                IsNoAdapterVisible = true;
            }
        }

        // [XPLAT] Reproduces FormUDP.btnSendSubnet_Click. Writes the edited subnet octets into the FROZEN
        // PGN 201 set-subnet frame, broadcasts it out of every operational IPv4 interface, persists the
        // new subnet to settings, and rebuilds the module data endpoint (_comm.Udp.EpModule) to the new
        // ".255" broadcast address so subsequent module traffic targets the new subnet without a restart.
        private void SendSubnet()
        {
            UdpLoopbackService udp = _comm.Udp;
            if (udp == null)
            {
                return;
            }

            // Stamp the pending subnet octets into the frozen frame (bytes [7],[8],[9]); all other bytes
            // are immutable protocol.
            sendIPToModules[7] = _ipNew[0];
            sendIPToModules[8] = _ipNew[1];
            sendIPToModules[9] = _ipNew[2];

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4) || nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(info.Address) ||
                        info.IPv4Mask == null)
                    {
                        continue;
                    }

                    try
                    {
                        Socket scanSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

                        try
                        {
                            scanSocket.Bind(new IPEndPoint(info.Address, 9999));
                            scanSocket.SendTo(sendIPToModules, 0, sendIPToModules.Length, SocketFlags.None, udp.epModuleSet);
                        }
                        catch (Exception ex)
                        {
                            Log.EventWriter("Catch - > Send Subnet Bind and Send: " + ex.ToString());
                        }

                        scanSocket.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.EventWriter("Catch - > Nic Loop Send Subnet: " + ex.ToString());
                    }
                }
            }

            // Persist the new subnet and promote it to "current".
            byte one = _ipNew[0];
            byte two = _ipNew[1];
            byte three = _ipNew[2];

            Settings.Default.etIP_SubnetOne = _ipCurrent[0] = one;
            Settings.Default.etIP_SubnetTwo = _ipCurrent[1] = two;
            Settings.Default.etIP_SubnetThree = _ipCurrent[2] = three;
            Settings.Default.Save();

            // [XPLAT] Rebuild the module data endpoint to the new subnet's broadcast address (parity with
            // the original mf.epModule assignment). InvariantCulture guarantees a dotted-decimal address
            // regardless of the host locale's decimal separator (AAP §0.6.5).
            udp.EpModule = new IPEndPoint(IPAddress.Parse(FormatSubnetDotted(one, two, three) + ".255"), 8888);

            NetworkHelpText = FormatSubnetSpaced(one, two, three);
            IsSendIndicatorVisible = false;

            Log.EventWriter("Subnet Uploaded: " + NetworkHelpText);
        }

        // ===========================================================================================
        //  Command handlers.
        // ===========================================================================================

        // [XPLAT] Reproduces FormUDP.btnAutoSet_Click: copies the module-reported subnet into the editable
        // octets and reveals the unsaved-change indicator.
        private void AutoSet()
        {
            UdpLoopbackService udp = _comm.Udp;
            if (udp == null || udp.ScanReply == null || udp.ScanReply.subnet == null || udp.ScanReply.subnet.Length < 3)
            {
                return;
            }

            byte[] subnet = udp.ScanReply.subnet;
            IpFirst = subnet[0];
            IpSecond = subnet[1];
            IpThird = subnet[2];

            // Always show the send indicator even when the octets did not change (parity with the original
            // unconditionally setting pboxSendSteer.Visible = true).
            IsSendIndicatorVisible = true;
        }

        // [XPLAT] Reproduces FormUDP.btnUDPOff_Click without WinForms. Disables the UDP features in
        // settings, asks the host to confirm, then requests a cross-platform restart and dialog close.
        private void TurnUdpOff()
        {
            Settings.Default.setUDP_isOn = false;
            Settings.Default.setUDP_isSendNMEAToUDP = false;
            Settings.Default.Save();

            RequestConfirm("AgIO will Restart to Disable UDP Networking Features");
            Log.EventWriter("Program Reset: Turning UDP OFF");

            RequestRestart();
            RequestClose();
        }

        // [XPLAT] Reproduces FormUDP.btnNetworkCPL_Click. The Windows Network Connections applet
        // (ncpa.cpl) is Windows-only, so the launch is runtime-guarded and the button is hidden off
        // Windows via the IsWindows property. UseShellExecute is required because modern .NET no longer
        // shell-launches .cpl files implicitly.
        private void OpenNetworkControlPanel()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Open Network Connections (ncpa.cpl): " + ex.ToString());
            }
        }

        // Reproduces FormUDP.cboxUp_Click: toggle the interface filter (the property setter updates the
        // caption text).
        private void ToggleFilterUp()
        {
            FilterUpOnly = !FilterUpOnly;
        }

        // ===========================================================================================
        //  Keypad-driven octet editing (replaces NumericUpDown.ShowKeypad).
        // ===========================================================================================

        // [XPLAT] Reproduces FormUDP.nudFirstIP_Click. The WinForms code opened an on-screen keypad on the
        // tapped NumericUpDown; here the host shows the Avalonia FormNumeric keypad via the RequestKeypad
        // callback. The fire-and-forget Task is intentional: a RelayCommand handler is synchronous.
        private void BeginEditOctet(int index)
        {
            _ = EditOctetAsync(index);
        }

        private async Task EditOctetAsync(int index)
        {
            double current;
            switch (index)
            {
                case 0:
                    current = _ipNew[0];
                    break;
                case 1:
                    current = _ipNew[1];
                    break;
                default:
                    current = _ipNew[2];
                    break;
            }

            // Subnet octets are a single byte; constrain the keypad to the valid 0..255 range.
            double? entered = await RequestKeypad(0, 255, current).ConfigureAwait(true);
            if (!entered.HasValue)
            {
                return;
            }

            double clamped = entered.Value;
            if (clamped < 0)
            {
                clamped = 0;
            }
            else if (clamped > 255)
            {
                clamped = 255;
            }

            byte octet = (byte)clamped;
            switch (index)
            {
                case 0:
                    IpFirst = octet;
                    break;
                case 1:
                    IpSecond = octet;
                    break;
                default:
                    IpThird = octet;
                    break;
            }

            // Mirror the original which always revealed the send indicator after an octet edit, even when
            // the entered value equalled the previous one.
            IsSendIndicatorVisible = true;
        }

        // ===========================================================================================
        //  Helpers.
        // ===========================================================================================

        // [XPLAT] Reproduces the FormUDp_Load auto-set reset: clears the coordinator's auto-set markers to
        // the "none" sentinel (99) so a stale module subnet is never auto-applied.
        private void ResetAutoSetMarkers()
        {
            UdpLoopbackService udp = _comm.Udp;
            if (udp == null || udp.ipAutoSet == null || udp.ipAutoSet.Length < 3)
            {
                return;
            }

            udp.ipAutoSet[0] = 99;
            udp.ipAutoSet[1] = 99;
            udp.ipAutoSet[2] = 99;
        }

        // Resolves an App.axaml brush resource by key, falling back to a brush built from the exact GDI
        // parity colour when no Avalonia application/resource is available (e.g. a headless test).
        private static IBrush ResolveBrush(string key, Color fallback)
        {
            Application app = Application.Current;
            if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out object value) && value is IBrush brush)
            {
                return brush;
            }

            return new SolidColorBrush(fallback);
        }

        // Formats the three subnet octets as the spaced "a . b . c" readout, using InvariantCulture so the
        // host locale can never inject a comma decimal separator.
        private static string FormatSubnetSpaced(byte one, byte two, byte three)
        {
            return one.ToString(CultureInfo.InvariantCulture) + " . " +
                   two.ToString(CultureInfo.InvariantCulture) + " . " +
                   three.ToString(CultureInfo.InvariantCulture);
        }

        // Formats the three subnet octets as a dotted "a.b.c" prefix for IPAddress.Parse, using
        // InvariantCulture for the same locale-safety reason.
        private static string FormatSubnetDotted(byte one, byte two, byte three)
        {
            return one.ToString(CultureInfo.InvariantCulture) + "." +
                   two.ToString(CultureInfo.InvariantCulture) + "." +
                   three.ToString(CultureInfo.InvariantCulture);
        }
    }
}
