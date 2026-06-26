// [XPLAT] migrated from net48/WinForms FormNtrip.cs + FormNtrip.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Input;
using AgIO.Properties;
using AgIO.Services;
using AgLibrary.Logging;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] View-model backing the Avalonia <c>FormNtrip</c> view — the NTRIP caster
    /// configuration dialog that selects and configures the RTK correction source. It replaces the
    /// WinForms <c>FormNtrip</c> (which held a direct <c>FormLoop mf</c> back-reference) and is a
    /// Tier-3 communication-configuration dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Frozen-contract discipline (AAP §0.2.2 / §0.7.1).</b> The NTRIP/PGN behaviour is
    /// behaviour-frozen: this view-model only reads and writes the <c>Settings.Default.setNTRIP_*</c>
    /// keys and triggers a reconfigure hook (<see cref="NtripService.ConfigureNTRIP"/>). The actual
    /// NTRIP TCP/HTTP correction stream, GGA-sentence emission, ports and PGN format all live in the
    /// behaviour-frozen <see cref="NtripService"/> and are never reimplemented here. The one piece of
    /// network code retained in the view-model is the caster <em>source-table</em> fetch (the original
    /// <c>GetData()</c> handler), because no service helper exists for it; it is ported verbatim onto
    /// cross-platform <see cref="System.Net.Sockets"/> and emits the exact same five-field records the
    /// original produced.
    /// </para>
    /// <para>
    /// <b>God-object removal.</b> Every former <c>mf.*</c> access is replaced by the injected
    /// <see cref="CommCoordinatorService"/> facade: <c>mf.packetSizeNTRIP</c> →
    /// <c>_comm.Ntrip.packetSizeNTRIP</c>, <c>mf.broadCasterIP</c> → <c>_comm.Ntrip.broadCasterIP</c>,
    /// <c>mf.isRadio_RequiredOn</c>/<c>mf.isSerialPass_RequiredOn</c> →
    /// <c>_comm.Ntrip.isRadio_RequiredOn</c>/<c>isSerialPass_RequiredOn</c>,
    /// <c>mf.isSendToSerial</c>/<c>mf.isSendToUDP</c> → <c>_comm.Ntrip.isSendToSerial</c>/<c>isSendToUDP</c>,
    /// <c>mf.latitude</c>/<c>mf.longitude</c> → <c>_comm.Nmea.latitude</c>/<c>longitude</c>, and
    /// <c>mf.ConfigureNTRIP()</c> → <c>_comm.Ntrip.ConfigureNTRIP()</c>. The WinForms
    /// <c>YesMessageBox</c>/<c>TimedMessageBox</c> popups, the <c>FormSource</c> mountpoint picker, the
    /// on-screen <c>FormKeyboard</c>, and the <c>Program.Restart()</c> relaunch are all surfaced as
    /// intent events (<see cref="RequestConfirm"/>, <see cref="RequestTimedMessage"/>,
    /// <see cref="RequestPickSource"/>, <see cref="RequestRestart"/>, <see cref="RequestClose"/>) so the
    /// view owns every window/dialog/lifecycle concern and this view-model stays unit-testable.
    /// </para>
    /// <para>
    /// <b>Culture safety (AAP §0.6.5).</b> Every numeric <c>ToString</c>/parse uses
    /// <see cref="CultureInfo.InvariantCulture"/>. The original used culture-default formatting for the
    /// current-fix latitude/longitude text and <c>Convert.ToInt32</c> for the packet size; both are
    /// hardened here so a locale with a comma decimal separator can never corrupt the persisted values
    /// on Linux/macOS.
    /// </para>
    /// </remarks>
    public class FormNtripViewModel : ViewModel
    {
        // [XPLAT] Injected comms facade replacing the WinForms FormLoop "mf" back-reference. It owns the
        // behaviour-frozen NtripService (correction stream + reconfigure hook) and NmeaService (live fix).
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Mirrors the WinForms "ntripStatusChanged" flag. Set true whenever the Serial/UDP routing
        // toggles change (the only edits the original treated as requiring a full restart on save). Left
        // false by the constructor's direct field loads so opening the dialog never counts as a change.
        private bool _ntripStatusChanged;

        // [XPLAT] Re-entrancy guard for the source-table fetch, reproducing the original's
        // "btnGetSourceTable.Enabled = false/true" gating so a second fetch cannot start while one runs.
        private bool _isGettingSourceTable;

        // Backing field kept as a RelayCommand (not just ICommand) so the fetch's CanExecute can be
        // re-queried via RaiseCanExecuteChanged when the busy guard flips.
        private readonly RelayCommand _getSourceTableCommand;

        // Property backing fields (one per bound control on the dialog).
        private bool _isNtripOn;
        private bool _sendToSerial;
        private bool _sendToUdp;
        private int _sendToUdpPort;
        private string _casterUrl;
        private string _casterIp;
        private int _casterPort;
        private string _userName;
        private string _userPassword;
        private string _mount;
        private int _ggaInterval;
        private double _manualLat;
        private double _manualLon;
        private string _currentLatText;
        private string _currentLonText;
        private bool _isTcp;
        private bool _isGgaManual;
        private bool _isHttp10;
        private string _packetSizeText;
        private string _hostName;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormNtripViewModel"/> class, seeding every bound
        /// property from the persisted <c>setNTRIP_*</c> settings exactly as the WinForms
        /// <c>FormNtrip_Load</c> handler did. The fields are assigned directly (not through the public
        /// setters) so the load does not raise the routing-changed flag or trigger the Serial/UDP
        /// mutual-exclusion side effects.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms facade. Provides the behaviour-frozen <see cref="NtripService"/> (settings
        /// targets + reconfigure hook) and the <see cref="NmeaService"/> live fix used to seed the manual
        /// position. Must not be <see langword="null"/>.
        /// </param>
        public FormNtripViewModel(CommCoordinatorService comm)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;

            Settings settings = Settings.Default;

            // Load all setNTRIP_* keys into the backing fields directly (no setter side effects during
            // load), mirroring FormNtrip_Load which set control values without firing Click handlers.
            _isNtripOn = settings.setNTRIP_isOn;
            _sendToSerial = settings.setNTRIP_sendToSerial;
            _sendToUdp = settings.setNTRIP_sendToUDP;
            _sendToUdpPort = settings.setNTRIP_sendToUDPPort;
            _casterUrl = settings.setNTRIP_casterURL;
            _casterIp = settings.setNTRIP_casterIP;
            _casterPort = settings.setNTRIP_casterPort;
            _userName = settings.setNTRIP_userName;
            _userPassword = settings.setNTRIP_userPassword;
            _mount = settings.setNTRIP_mount;
            _ggaInterval = settings.setNTRIP_sendGGAInterval;
            _manualLat = settings.setNTRIP_manualLat;
            _manualLon = settings.setNTRIP_manualLon;
            _isTcp = settings.setNTRIP_isTCP;
            _isGgaManual = settings.setNTRIP_isGGAManual;
            _isHttp10 = settings.setNTRIP_isHTTP10;

            // Packet size text seeded from the live service value (the original used mf.packetSizeNTRIP),
            // formatted invariantly so the round-trip with the invariant-culture parse in OnSave is stable.
            _packetSizeText = _comm.Ntrip.packetSizeNTRIP.ToString(CultureInfo.InvariantCulture);

            // Current-fix display seeded from the persisted manual fix (FormNtrip_Load L86-87), invariantly
            // formatted to avoid comma-decimal corruption on non-en locales.
            _currentLatText = settings.setNTRIP_manualLat.ToString(CultureInfo.InvariantCulture);
            _currentLonText = settings.setNTRIP_manualLon.ToString(CultureInfo.InvariantCulture);

            // Host name + local IPv4 list (FormNtrip_Load L63-67 + GetIP4AddressList), guarded so a DNS
            // failure on a headless/offline machine cannot fault the dialog.
            _hostName = SafeGetHostName();
            LocalIpList = new ObservableCollection<string>();
            BuildLocalIpList();

            // Commands. The source-table fetch keeps its RelayCommand reference so the busy guard can be
            // surfaced through CanExecute.
            SaveCommand = new RelayCommand(OnSave);
            ToggleNtripOnCommand = new RelayCommand(OnToggleNtripOn);
            FindIpCommand = new RelayCommand(OnFindIp);
            _getSourceTableCommand = new RelayCommand(OnGetSourceTable, CanGetSourceTable);
            GetSourceTableCommand = _getSourceTableCommand;
            UseCurrentFixCommand = new RelayCommand(OnUseCurrentFix);
            CancelCommand = new RelayCommand(OnCancel);
        }

        // ---------------------------------------------------------------------------------------------
        // Bound properties (one per dialog control). Reference-type setters compare with != before
        // raising change notification; nullable reference types are disabled project-wide so no '?' is
        // used on any reference type.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets or sets a value indicating whether the NTRIP client is enabled
        /// (former <c>cboxIsNTRIPOn.Checked</c>; persisted as <c>setNTRIP_isOn</c>). Toggling this in the
        /// view should also invoke <see cref="ToggleNtripOnCommand"/> to reproduce the original
        /// immediate save-and-restart behaviour.
        /// </summary>
        public bool IsNtripOn
        {
            get { return _isNtripOn; }
            set
            {
                if (value != _isNtripOn)
                {
                    _isNtripOn = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether received corrections are forwarded to the GPS over the
        /// serial port (former <c>cboxToSerial.Checked</c>; persisted as <c>setNTRIP_sendToSerial</c>).
        /// Mutually exclusive with <see cref="SendToUdp"/>: enabling it disables UDP forwarding, and any
        /// genuine change marks the routing as changed (the original <c>ntripStatusChanged</c> flag),
        /// which forces a restart on save.
        /// </summary>
        public bool SendToSerial
        {
            get { return _sendToSerial; }
            set
            {
                if (value != _sendToSerial)
                {
                    _sendToSerial = value;
                    NotifyPropertyChanged();
                    _ntripStatusChanged = true;
                    if (value && _sendToUdp)
                    {
                        SendToUdp = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether received corrections are forwarded over UDP
        /// (former <c>cboxToUDP.Checked</c>; persisted as <c>setNTRIP_sendToUDP</c>). Mutually exclusive
        /// with <see cref="SendToSerial"/>; see that property for the shared routing-changed semantics.
        /// </summary>
        public bool SendToUdp
        {
            get { return _sendToUdp; }
            set
            {
                if (value != _sendToUdp)
                {
                    _sendToUdp = value;
                    NotifyPropertyChanged();
                    _ntripStatusChanged = true;
                    if (value && _sendToSerial)
                    {
                        SendToSerial = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the UDP port corrections are forwarded to when <see cref="SendToUdp"/> is enabled
        /// (former <c>nudSendToUDPPort.Value</c>; persisted as <c>setNTRIP_sendToUDPPort</c>).
        /// </summary>
        public int SendToUdpPort
        {
            get { return _sendToUdpPort; }
            set
            {
                if (value != _sendToUdpPort)
                {
                    _sendToUdpPort = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the broadcaster URL or IP entered by the operator (former
        /// <c>tboxEnterURL.Text</c>; persisted as <c>setNTRIP_casterURL</c>). Resolved to an IP by
        /// <see cref="FindIpCommand"/>.
        /// </summary>
        public string CasterUrl
        {
            get { return _casterUrl; }
            set
            {
                if (value != _casterUrl)
                {
                    _casterUrl = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the resolved caster IPv4 address (former <c>tboxCasterIP.Text</c>; persisted as
        /// <c>setNTRIP_casterIP</c>). It was read-only in the WinForms dialog — set programmatically by
        /// <see cref="FindIpCommand"/> or the source-table picker — and is validated by
        /// <see cref="ValidateCasterIp"/>.
        /// </summary>
        public string CasterIp
        {
            get { return _casterIp; }
            set
            {
                if (value != _casterIp)
                {
                    _casterIp = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the caster TCP port (former <c>nudCasterPort.Value</c>; persisted as
        /// <c>setNTRIP_casterPort</c>).
        /// </summary>
        public int CasterPort
        {
            get { return _casterPort; }
            set
            {
                if (value != _casterPort)
                {
                    _casterPort = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the NTRIP user name (former <c>tboxUserName.Text</c>; persisted as
        /// <c>setNTRIP_userName</c>).
        /// </summary>
        public string UserName
        {
            get { return _userName; }
            set
            {
                if (value != _userName)
                {
                    _userName = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the NTRIP password (former <c>tboxUserPassword.Text</c>; persisted as
        /// <c>setNTRIP_userPassword</c>). Password masking/reveal is a pure view concern.
        /// </summary>
        public string UserPassword
        {
            get { return _userPassword; }
            set
            {
                if (value != _userPassword)
                {
                    _userPassword = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected mountpoint (former <c>tboxMount.Text</c>; persisted as
        /// <c>setNTRIP_mount</c>). The view assigns this from the value returned by the source-table
        /// picker raised through <see cref="RequestPickSource"/>.
        /// </summary>
        public string Mount
        {
            get { return _mount; }
            set
            {
                if (value != _mount)
                {
                    _mount = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the interval, in seconds, at which a GGA sentence is sent back to the caster
        /// (former <c>nudGGAInterval.Value</c>; persisted as <c>setNTRIP_sendGGAInterval</c>; zero
        /// disables the periodic send).
        /// </summary>
        public int GgaInterval
        {
            get { return _ggaInterval; }
            set
            {
                if (value != _ggaInterval)
                {
                    _ggaInterval = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the manual-fix latitude in decimal degrees (former <c>nudLatitude.Value</c>;
        /// persisted as <c>setNTRIP_manualLat</c>). Seeded from the live fix by
        /// <see cref="UseCurrentFixCommand"/>.
        /// </summary>
        public double ManualLat
        {
            get { return _manualLat; }
            set
            {
                if (value != _manualLat)
                {
                    _manualLat = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the manual-fix longitude in decimal degrees (former <c>nudLongitude.Value</c>;
        /// persisted as <c>setNTRIP_manualLon</c>). Seeded from the live fix by
        /// <see cref="UseCurrentFixCommand"/>.
        /// </summary>
        public double ManualLon
        {
            get { return _manualLon; }
            set
            {
                if (value != _manualLon)
                {
                    _manualLon = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the read-only display of the current live latitude (former
        /// <c>tboxCurrentLat.Text</c>). Refreshed by <see cref="UpdateCurrentFix"/> on the view's timer;
        /// always invariant-culture formatted.
        /// </summary>
        public string CurrentLatText
        {
            get { return _currentLatText; }
            set
            {
                if (value != _currentLatText)
                {
                    _currentLatText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the read-only display of the current live longitude (former
        /// <c>tboxCurrentLon.Text</c>). See <see cref="CurrentLatText"/>.
        /// </summary>
        public string CurrentLonText
        {
            get { return _currentLonText; }
            set
            {
                if (value != _currentLonText)
                {
                    _currentLonText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the caster connection uses raw TCP rather than HTTP
        /// (former <c>checkBoxusetcp.Checked</c>; persisted as <c>setNTRIP_isTCP</c>).
        /// </summary>
        public bool IsTcp
        {
            get { return _isTcp; }
            set
            {
                if (value != _isTcp)
                {
                    _isTcp = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the manual fix (rather than the live GPS fix) is used
        /// for the GGA position sent to the caster (former <c>cboxGGAManual</c> "Use Manual Fix" vs
        /// "Use GPS Fix"; persisted as <c>setNTRIP_isGGAManual</c>).
        /// </summary>
        public bool IsGgaManual
        {
            get { return _isGgaManual; }
            set
            {
                if (value != _isGgaManual)
                {
                    _isGgaManual = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether HTTP/1.0 (rather than HTTP/1.1) is used for the caster
        /// request (former <c>cboxHTTP</c> "1.0" vs "1.1"; persisted as <c>setNTRIP_isHTTP10</c>).
        /// </summary>
        public bool IsHttp10
        {
            get { return _isHttp10; }
            set
            {
                if (value != _isHttp10)
                {
                    _isHttp10 = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the RTCM forward packet-size text (former <c>comboboxPacketSize.Text</c>;
        /// persisted as <c>setNTRIP_packetSize</c>). Parsed invariantly in <see cref="OnSave"/>.
        /// </summary>
        public string PacketSizeText
        {
            get { return _packetSizeText; }
            set
            {
                if (value != _packetSizeText)
                {
                    _packetSizeText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the local machine host name (former <c>tboxHostName.Text</c>, read-only). Resolved once
        /// in the constructor.
        /// </summary>
        public string HostName
        {
            get { return _hostName; }
            set
            {
                if (value != _hostName)
                {
                    _hostName = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the list of local IPv4 addresses shown to the operator (former <c>listboxIP</c>). Populated
        /// from the host's address list in the constructor; an <see cref="ObservableCollection{T}"/> so the
        /// bound list refreshes if it is ever rebuilt.
        /// </summary>
        public ObservableCollection<string> LocalIpList { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the touch on-screen keyboard is offered when a text
        /// field is tapped. Defaults to <see langword="true"/>, matching the AgIO <c>FormLoop</c> original
        /// which hard-coded <c>isKeyboardOn = true</c>. The view reads this flag before opening the
        /// keyboard helper; this view-model never opens a window itself.
        /// </summary>
        public bool IsKeyboardOn { get; set; } = true;

        // ---------------------------------------------------------------------------------------------
        // Commands. Exposed as ICommand for binding; the source-table fetch is also held as a concrete
        // RelayCommand so its CanExecute can be re-queried while the fetch is in progress.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the command that persists every NTRIP setting and either reconfigures the service in place
        /// or requests a restart (parity with the WinForms <c>btnSerialOK_Click</c>). Bound to the OK button.
        /// </summary>
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the command that reproduces the WinForms <c>cboxIsNTRIPOn_Click</c> behaviour: it persists
        /// the enabled state, clears the conflicting radio/serial-pass sources, and requests a restart.
        /// The view binds this to the NTRIP-on checkbox's click so toggling NTRIP restarts AgIO exactly as
        /// the original did.
        /// </summary>
        public ICommand ToggleNtripOnCommand { get; }

        /// <summary>
        /// Gets the command that resolves <see cref="CasterUrl"/> to an IPv4 address via DNS and stores it
        /// in <see cref="CasterIp"/> (parity with the WinForms <c>btnGetIP_Click</c>). Bound to the
        /// confirm-IP button.
        /// </summary>
        public ICommand FindIpCommand { get; }

        /// <summary>
        /// Gets the command that fetches the caster source-table and raises <see cref="RequestPickSource"/>
        /// so the view can show the mountpoint picker (parity with the WinForms <c>btnGetSourceTable_Click</c>
        /// + <c>GetData()</c>). Disabled while a fetch is in progress.
        /// </summary>
        public ICommand GetSourceTableCommand { get; }

        /// <summary>
        /// Gets the command that copies the current live fix into the manual latitude/longitude fields
        /// (parity with the WinForms <c>btnSetManualPosition_Click</c>). Bound to the "send to manual fix"
        /// button.
        /// </summary>
        public ICommand UseCurrentFixCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without saving by raising <see cref="RequestClose"/>.
        /// Bound to the Cancel button.
        /// </summary>
        public ICommand CancelCommand { get; }

        // ---------------------------------------------------------------------------------------------
        // Intent events (replace the WinForms message boxes, dialog launches and restart). All are
        // initialized to a no-op delegate so they are always safe to invoke (nullable refs disabled).
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Raised to ask the view to show a modal confirmation message — the MVVM replacement for the
        /// WinForms <c>FormLoop.YesMessageBox(message)</c>. The argument is the message body.
        /// </summary>
        public event Action<string> RequestConfirm = delegate { };

        /// <summary>
        /// Raised to ask the view to show a transient, self-dismissing notification — the MVVM replacement
        /// for the WinForms <c>FormLoop.TimedMessageBox(milliseconds, title, message)</c>. The arguments are
        /// the display duration in milliseconds, the title and the message body.
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Raised to ask the view to open the source-table (mountpoint) picker. The arguments are the parsed
        /// caster records (each a comma-joined <c>mount,lat,lon,format,network</c> row), the current fix
        /// latitude and longitude, and the caster monitor URL. The view opens the picker, and on a
        /// confirmed choice assigns the chosen mountpoint back to <see cref="Mount"/>.
        /// </summary>
        public event Action<List<string>, double, double, string> RequestPickSource = delegate { };

        /// <summary>
        /// Raised to ask the host to perform the cross-platform restart (the <c>Program.Restart()</c>
        /// equivalent). The view-model never restarts the process itself, keeping it unit-testable.
        /// </summary>
        public event Action RequestRestart = delegate { };

        /// <summary>
        /// Raised to ask the host to close the dialog. Used by the in-place save path and by Cancel.
        /// </summary>
        public event Action RequestClose = delegate { };

        // ---------------------------------------------------------------------------------------------
        // Command handlers and helpers.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Persists every NTRIP setting from the bound properties, then either reconfigures the service in
        /// place or requests a restart, reproducing the WinForms <c>btnSerialOK_Click</c> handler.
        /// </summary>
        /// <remarks>
        /// The either/or restart branch is preserved exactly: when no routing toggle changed
        /// (<see cref="_ntripStatusChanged"/> is false) the service is reconfigured in place and the dialog
        /// closes; when a routing toggle changed, a full restart is requested instead. Mutual exclusion
        /// (enabling NTRIP clears the radio and serial-pass sources) and the radio-conflict guard are kept
        /// verbatim, and the packet size is parsed with <see cref="CultureInfo.InvariantCulture"/>.
        /// </remarks>
        private void OnSave()
        {
            Settings settings = Settings.Default;

            // The WinForms caster-IP box was read-only and validated on blur; validate defensively here so
            // an invalid value can never be persisted.
            ValidateCasterIp();

            settings.setNTRIP_casterIP = CasterIp;
            settings.setNTRIP_casterPort = CasterPort;
            settings.setNTRIP_sendToUDPPort = SendToUdpPort;

            settings.setNTRIP_isOn = IsNtripOn;

            // Mutual exclusion: enabling NTRIP forces the radio and serial-pass sources off, both in the
            // persisted settings and on the live service (the original cleared mf.isRadio_RequiredOn /
            // mf.isSerialPass_RequiredOn).
            if (IsNtripOn)
            {
                settings.setRadio_isOn = false;
                settings.setPass_isOn = false;
                _comm.Ntrip.isRadio_RequiredOn = false;
                _comm.Ntrip.isSerialPass_RequiredOn = false;
            }

            settings.setNTRIP_userName = UserName;
            settings.setNTRIP_userPassword = UserPassword;
            settings.setNTRIP_mount = Mount;

            settings.setNTRIP_sendGGAInterval = GgaInterval;
            settings.setNTRIP_manualLat = ManualLat;
            settings.setNTRIP_manualLon = ManualLon;

            settings.setNTRIP_casterURL = CasterUrl;
            settings.setNTRIP_isGGAManual = IsGgaManual;
            settings.setNTRIP_isHTTP10 = IsHttp10;
            settings.setNTRIP_isTCP = IsTcp;

            settings.setNTRIP_sendToSerial = SendToSerial;
            settings.setNTRIP_sendToUDP = SendToUdp;

            // Mirror the routing flags and the resolved caster IP onto the live service (mf.isSendToSerial /
            // mf.isSendToUDP / mf.broadCasterIP).
            _comm.Ntrip.isSendToSerial = SendToSerial;
            _comm.Ntrip.isSendToUDP = SendToUdp;
            _comm.Ntrip.broadCasterIP = CasterIp;

            // [XPLAT] Packet size: invariant-culture parse replacing the original locale-sensitive
            // Convert.ToInt32(comboboxPacketSize.Text). A non-numeric entry falls back to the current
            // service value so a stray value can never throw out of the save handler.
            int packetSize;
            if (!int.TryParse(PacketSizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out packetSize))
            {
                packetSize = _comm.Ntrip.packetSizeNTRIP;
            }

            _comm.Ntrip.packetSizeNTRIP = packetSize;
            settings.setNTRIP_packetSize = packetSize;

            // Radio-conflict guard (btnSerialOK_Click): NTRIP and radio cannot both be on.
            if (settings.setNTRIP_isOn && settings.setRadio_isOn)
            {
                RequestTimedMessage(2000, "Radio also enabled", "Disable the Radio NTRIP");
                settings.setRadio_isOn = false;
            }

            settings.Save();

            // Either/or branch from btnSerialOK_Click: a routing change requires a full restart; otherwise
            // reconfigure NTRIP in place and close. ConfigureNTRIP runs before RequestClose so the
            // reconfigure cannot be skipped by an immediate view teardown.
            if (!_ntripStatusChanged)
            {
                _comm.Ntrip.ConfigureNTRIP();
                RequestClose();
            }
            else
            {
                Log.EventWriter("Program Reset: Button Ok on Ntrip Form");
                RequestConfirm("Restart of AgIO is Required - Restarting");
                RequestRestart();
            }
        }

        /// <summary>
        /// Reproduces the WinForms <c>cboxIsNTRIPOn_Click</c> handler: persists the enabled state, clears
        /// the conflicting radio/serial-pass sources when enabling, then confirms and requests a restart
        /// (selecting the NTRIP feature always required a restart).
        /// </summary>
        private void OnToggleNtripOn()
        {
            Settings settings = Settings.Default;
            settings.setNTRIP_isOn = IsNtripOn;

            if (IsNtripOn)
            {
                settings.setRadio_isOn = false;
                settings.setPass_isOn = false;
                _comm.Ntrip.isRadio_RequiredOn = false;
                _comm.Ntrip.isSerialPass_RequiredOn = false;
                Log.EventWriter("NTRIP Turned on");
            }
            else
            {
                Log.EventWriter("NTRIP Turned off");
            }

            settings.Save();

            RequestConfirm("Restart of AgIO is Required - Restarting");
            Log.EventWriter("Program Reset: Selecting NTRIP Feature");
            RequestRestart();
        }

        /// <summary>
        /// Resolves <see cref="CasterUrl"/> to an IPv4 address via DNS and stores it in
        /// <see cref="CasterIp"/> (and on the live service), reproducing the WinForms <c>btnGetIP_Click</c>
        /// handler. <see cref="Dns.GetHostAddresses(string)"/> is cross-platform. On success a confirmation
        /// is shown; any failure raises a confirm prompt and is logged.
        /// </summary>
        private void OnFindIp()
        {
            string actualIp = _casterUrl == null ? string.Empty : _casterUrl.Trim();

            try
            {
                IPAddress[] addressList = Dns.GetHostAddresses(actualIp);
                if (addressList != null)
                {
                    CasterIp = string.Empty;

                    foreach (IPAddress addr in addressList)
                    {
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string resolved = addr.ToString().Trim();
                            CasterIp = resolved;
                            _comm.Ntrip.broadCasterIP = resolved;
                            Settings.Default.setNTRIP_casterIP = resolved;
                            Settings.Default.Save();
                            break;
                        }
                    }

                    RequestTimedMessage(2500, "IP Located", "Verified: " + actualIp);
                }
                else
                {
                    RequestConfirm("Can't Find: " + actualIp);
                    Log.EventWriter("Can't Find Caster IP");
                }
            }
            catch (Exception ex)
            {
                RequestConfirm("Can't Find: " + actualIp);
                Log.EventWriter("Catch -> Can't Find Caster IP" + ex.ToString());
            }
        }

        // CanExecute for the source-table fetch: blocked while a fetch is already running (parity with the
        // WinForms "btnGetSourceTable.Enabled = false" gating).
        private bool CanGetSourceTable()
        {
            return !_isGettingSourceTable;
        }

        /// <summary>
        /// Fetches the caster source-table over a cross-platform TCP socket and raises
        /// <see cref="RequestPickSource"/> with the parsed mountpoint records, reproducing the WinForms
        /// <c>btnGetSourceTable_Click</c> + <c>GetData()</c> handler. The NTRIP correction stream is not
        /// touched; this is only the one-shot HTTP source-table request the original performed inline.
        /// </summary>
        /// <remarks>
        /// The socket request, the 200&#160;ms settle delay, and the <c>STR;</c> record parsing are ported
        /// verbatim, so the five-field records (<c>mount,lat,lon,format,network</c>) are byte-identical to
        /// the WinForms build and remain compatible with the source-table picker. The socket is disposed via
        /// a <c>using</c> block (the original leaked it). The monitor URL's port is formatted with
        /// <see cref="CultureInfo.InvariantCulture"/>.
        /// </remarks>
        private void OnGetSourceTable()
        {
            _isGettingSourceTable = true;
            _getSourceTableCommand.RaiseCanExecuteChanged();

            try
            {
                List<string> dataList = new List<string>();
                bool fetchOk = true;

                try
                {
                    IPAddress casterIp = IPAddress.Parse(CasterIp.Trim());
                    int casterPort = CasterPort;

                    using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = true })
                    {
                        socket.Connect(new IPEndPoint(casterIp, casterPort));

                        string msg = "GET / HTTP/1.0\r\n" + "User-Agent: NTRIP iter.dk\r\n" +
                            "Accept: */*\r\nConnection: close\r\n" + "\r\n";

                        byte[] data = Encoding.ASCII.GetBytes(msg);
                        socket.Send(data);

                        int bytes = 0;
                        byte[] bytesReceived = new byte[1024];
                        string page = string.Empty;
                        Thread.Sleep(200);

                        do
                        {
                            bytes = socket.Receive(bytesReceived, bytesReceived.Length, SocketFlags.None);
                            page += Encoding.ASCII.GetString(bytesReceived, 0, bytes);
                        }
                        while (bytes > 0);

                        if (page.Length > 0)
                        {
                            string[] words = page.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                            for (int i = 0; i < words.Length; i++)
                            {
                                string[] words2 = words[i].Split(';');

                                if (words2[0] == "STR")
                                {
                                    dataList.Add(words2[1].Trim() + "," + words2[9] + "," + words2[10]
                                        + "," + words2[3].Trim() + "," + words2[6].Trim());
                                }
                            }
                        }
                    }
                }
                catch (SocketException ex)
                {
                    RequestTimedMessage(2000, "Socket Exception", "Invalid IP:Port");
                    Log.EventWriter("Catch -> Socket Exception, Invalid IP:Port" + ex.ToString());
                    fetchOk = false;
                }
                catch (Exception ex)
                {
                    RequestTimedMessage(2000, "Exception", "Get Source Table Error");
                    Log.EventWriter("Catch - > Get Source Table Error" + ex.ToString());
                    fetchOk = false;
                }

                if (!fetchOk)
                {
                    return;
                }

                if (dataList.Count > 0)
                {
                    string syte = "http://monitor.use-snip.com/?hostUrl=" + CasterIp + "&port=" +
                        CasterPort.ToString(CultureInfo.InvariantCulture);
                    RequestPickSource(dataList, _comm.Nmea.latitude, _comm.Nmea.longitude, syte);
                }
                else
                {
                    RequestTimedMessage(2000, "Error", "No Source Data");
                }
            }
            finally
            {
                _isGettingSourceTable = false;
                _getSourceTableCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Copies the current live fix into the manual latitude/longitude fields, reproducing the WinForms
        /// <c>btnSetManualPosition_Click</c> handler.
        /// </summary>
        private void OnUseCurrentFix()
        {
            ManualLat = _comm.Nmea.latitude;
            ManualLon = _comm.Nmea.longitude;
        }

        /// <summary>
        /// Dismisses the dialog without saving by raising <see cref="RequestClose"/>.
        /// </summary>
        private void OnCancel()
        {
            RequestClose();
        }

        /// <summary>
        /// Refreshes <see cref="CurrentLatText"/>/<see cref="CurrentLonText"/> from the live fix, reproducing
        /// the WinForms <c>timer1_Tick</c> handler. The view calls this on its display timer. Both values are
        /// formatted with <see cref="CultureInfo.InvariantCulture"/> (the original used culture-default
        /// formatting, a data-integrity hazard on comma-decimal locales).
        /// </summary>
        public void UpdateCurrentFix()
        {
            CurrentLatText = _comm.Nmea.latitude.ToString(CultureInfo.InvariantCulture);
            CurrentLonText = _comm.Nmea.longitude.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Validates an IPv4 address (or a COM-port name), ported as-is from the WinForms
        /// <c>CheckIPValid</c> helper: a value containing "COM" is accepted, otherwise the value must be
        /// four dot-separated octets each 1–3 digits in the range 0–255.
        /// </summary>
        /// <param name="strIP">The candidate address string.</param>
        /// <returns><see langword="true"/> when the value is a valid IPv4 address or a COM-port name.</returns>
        public bool CheckIPValid(string strIP)
        {
            // Guard a null value defensively (the original would have thrown on Contains/Split).
            if (strIP == null)
            {
                return false;
            }

            // Return true for COM Port.
            if (strIP.Contains("COM"))
            {
                return true;
            }

            // Split string by ".", check that array length is 4.
            string[] arrOctets = strIP.Split('.');

            // At least 4 groups in the IP.
            if (arrOctets.Length != 4)
            {
                return false;
            }

            // Check each substring: the int value must be < 255 and its length must not be > 3.
            const short MAXVALUE = 255;
            foreach (string strOctet in arrOctets)
            {
                // Check at least 1 digit but not more than 3.
                if (strOctet.Length > 3 || strOctet.Length == 0)
                {
                    return false;
                }

                // Make sure all digits (invariant-culture parse so locale can never change acceptance).
                if (!int.TryParse(strOctet, NumberStyles.Integer, CultureInfo.InvariantCulture, out int temp2))
                {
                    return false;
                }

                // Make sure not more than 255.
                int temp = int.Parse(strOctet, NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (temp > MAXVALUE || temp < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates <see cref="CasterIp"/> and, when it is not a valid address, resets it to the local
        /// default and notifies the operator, reproducing the WinForms <c>tboxCasterIP_Validating</c>
        /// handler.
        /// </summary>
        public void ValidateCasterIp()
        {
            if (!CheckIPValid(CasterIp))
            {
                CasterIp = "127.0.0.1";
                RequestTimedMessage(2000, "Invalid IP Address", "Set to Default Local 127.0.0.1");
            }
        }

        // Populates LocalIpList with the host's IPv4 addresses (WinForms GetIP4AddressList). Guarded so a
        // DNS failure cannot fault construction on a headless/offline machine.
        private void BuildLocalIpList()
        {
            LocalIpList.Clear();

            try
            {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        LocalIpList.Add(ip.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch -> NTRIP GetIP4AddressList: " + ex.Message);
            }
        }

        // Resolves the host name (WinForms Dns.GetHostName), guarded so a DNS failure cannot fault the
        // dialog; an empty string is shown instead.
        private static string SafeGetHostName()
        {
            try
            {
                return Dns.GetHostName();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch -> NTRIP GetHostName: " + ex.Message);
                return string.Empty;
            }
        }
    }
}
