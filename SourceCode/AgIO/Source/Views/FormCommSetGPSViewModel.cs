// [XPLAT] migrated from net48/WinForms FormCommSetGPS.cs + FormCommSetGPS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using AgIO.Services;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Per-port sub-view-model for a single serial port row of the
    /// <c>FormCommSetGPS</c> dialog. The original WinForms dialog repeated the same block of
    /// controls (a baud combo, a port combo, an Open button, a Close button and the
    /// "current baud / current port" labels) once for each of the six AgIO serial ports, with
    /// the only differences being which <see cref="SerialCommService"/> port and which
    /// <c>Open*/Close*Port</c> methods the buttons drove. This class captures that single
    /// repeated block once (AAP §0.3.2 "Extract Class") so the parent
    /// <see cref="FormCommSetGPSViewModel"/> can compose six instances instead of duplicating
    /// the open/close/apply logic six times. Every port's exact open/close semantics and the
    /// culture-invariant baud parsing are preserved (AAP §0.7.1 / §0.6.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a single capability flag.</b> The six ports fall into exactly two families whose
    /// behaviour differs along three axes that happen to move together, so a single
    /// <see cref="ShowBaud"/> capability flag fully describes a port:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>GPS / GPS2 / RTCM</b> (<c>ShowBaud == true</c>) expose a baud combo, surface an
    /// "Unable to connect to Port" message when an open attempt fails, and refresh their
    /// "current port" label only when a connection is (re)opened — exactly as the WinForms
    /// <c>cboxBaud*_SelectedIndexChanged</c>/<c>btnOpen*_Click</c> handlers did.
    /// </description></item>
    /// <item><description>
    /// <b>IMU / SteerModule / MachineModule</b> (<c>ShowBaud == false</c>) have a fixed baud
    /// (no baud combo), stay silent on a failed open, and update their "current port" label the
    /// instant a port name is chosen — exactly as the WinForms
    /// <c>cbox*_SelectedIndexChanged</c> handlers did (<c>lblCurrent*.Text = cbox*.Text</c>).
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>God-object removal.</b> The WinForms block reached the form's <c>mf.sp&lt;Port&gt;</c>
    /// serial ports and <c>mf.Open*/Close*Port()</c> methods directly. Those references are
    /// replaced by injected delegates that the parent binds to the shared
    /// <see cref="SerialCommService"/> the <see cref="CommCoordinatorService"/> owns; this row
    /// never holds a back-reference to the form (AAP §0.1.1).
    /// </para>
    /// <para>
    /// <b>Ownership.</b> This row only <i>commands</i> the shared serial port — it does not own
    /// it. Dismissing the dialog therefore never closes a port (see
    /// <see cref="FormCommSetGPSViewModel.Stop"/>).
    /// </para>
    /// </remarks>
    public sealed class SerialPortItemViewModel : ViewModel
    {
        // [XPLAT] Applies the chosen port NAME to both the live SerialPort instance and the static
        // SerialCommService field the Open*Port methods read. Supplied by the parent so this row
        // does not reference SerialCommService directly. Never null.
        private readonly Action<string> _applyPortName;

        // [XPLAT] Applies the chosen baud rate (instance + static). Null for the port-only module
        // ports (IMU/Steer/Machine), whose baud is fixed; guarded with ?. at the call site.
        private readonly Action<int> _applyBaudRate;

        // [XPLAT] Opens / closes the underlying shared serial port. These wrap the
        // SerialCommService.Open*Port()/Close*Port() methods, which catch their own exceptions and
        // never throw, so success is determined by re-reading _isPortOpen afterwards.
        private readonly Action _openPort;
        private readonly Action _closePort;

        // [XPLAT] Live reads of the shared serial port's state used to refresh this row after an
        // open/close and to seed the initial display.
        private readonly Func<bool> _isPortOpen;
        private readonly Func<string> _currentPortName;
        private readonly Func<int> _currentBaudRate;

        // [XPLAT] Surfaces the former MessageBox.Show popups through the parent's RequestTimedMessage
        // event (which the view routes to the IErrorPresenter / FormTimedMessage). Never null.
        private readonly Action<int, string, string> _raiseTimedMessage;

        // [XPLAT] True only for the GPS-family ports (GPS/GPS2/RTCM): they show a connect-failure
        // message and refresh their current-port label on open rather than on selection. Derived
        // from the single ShowBaud capability flag.
        private readonly bool _showConnectFailMessage;
        private readonly bool _updateLabelOnSelect;

        private string _selectedPortName = string.Empty;
        private string _selectedBaud = string.Empty;
        private bool _isOpen;
        private string _currentPortLabel = string.Empty;
        private string _currentBaudLabel = string.Empty;

        /// <summary>
        /// [XPLAT] Initializes a serial-port row. The capability flag <paramref name="hasBaudSelection"/>
        /// distinguishes the GPS-family ports (baud combo + connect-fail message + label-on-open) from
        /// the module ports (fixed baud + silent + label-on-select); see the class remarks.
        /// </summary>
        /// <param name="displayName">Human-readable group title (e.g. "GPS", "RTCM", "IMU").</param>
        /// <param name="hasBaudSelection">
        /// <c>true</c> for GPS/GPS2/RTCM (a baud combo is shown and a failed open is reported);
        /// <c>false</c> for IMU/SteerModule/MachineModule (fixed baud, silent on failure, current-port
        /// label refreshed the moment a port name is chosen).
        /// </param>
        /// <param name="availablePorts">The shared, rescannable collection of discovered port names.</param>
        /// <param name="baudRates">The selectable baud rates for this port (empty for module ports).</param>
        /// <param name="applyPortName">Applies the chosen port name to the instance + static fields.</param>
        /// <param name="applyBaudRate">Applies the chosen baud (instance + static); may be <c>null</c>.</param>
        /// <param name="openPort">Opens the underlying shared serial port.</param>
        /// <param name="closePort">Closes the underlying shared serial port.</param>
        /// <param name="isPortOpen">Reads whether the underlying shared serial port is open.</param>
        /// <param name="currentPortName">Reads the underlying port's current name.</param>
        /// <param name="currentBaudRate">Reads the underlying port's current baud rate.</param>
        /// <param name="raiseTimedMessage">Surfaces a transient (duration-ms, title, message) notice.</param>
        public SerialPortItemViewModel(
            string displayName,
            bool hasBaudSelection,
            ObservableCollection<string> availablePorts,
            ObservableCollection<string> baudRates,
            Action<string> applyPortName,
            Action<int> applyBaudRate,
            Action openPort,
            Action closePort,
            Func<bool> isPortOpen,
            Func<string> currentPortName,
            Func<int> currentBaudRate,
            Action<int, string, string> raiseTimedMessage)
        {
            if (applyPortName == null) throw new ArgumentNullException(nameof(applyPortName));
            if (openPort == null) throw new ArgumentNullException(nameof(openPort));
            if (closePort == null) throw new ArgumentNullException(nameof(closePort));
            if (isPortOpen == null) throw new ArgumentNullException(nameof(isPortOpen));
            if (currentPortName == null) throw new ArgumentNullException(nameof(currentPortName));
            if (currentBaudRate == null) throw new ArgumentNullException(nameof(currentBaudRate));
            if (raiseTimedMessage == null) throw new ArgumentNullException(nameof(raiseTimedMessage));

            DisplayName = displayName ?? string.Empty;
            ShowBaud = hasBaudSelection;
            _showConnectFailMessage = hasBaudSelection;
            _updateLabelOnSelect = !hasBaudSelection;

            AvailablePorts = availablePorts ?? new ObservableCollection<string>();
            BaudRates = baudRates ?? new ObservableCollection<string>();

            _applyPortName = applyPortName;
            _applyBaudRate = applyBaudRate;
            _openPort = openPort;
            _closePort = closePort;
            _isPortOpen = isPortOpen;
            _currentPortName = currentPortName;
            _currentBaudRate = currentBaudRate;
            _raiseTimedMessage = raiseTimedMessage;

            OpenCommand = new RelayCommand(OnOpen, () => !IsOpen);
            CloseCommand = new RelayCommand(OnClose, () => IsOpen);

            // Seed the initial display from the live serial port exactly as the WinForms
            // FormCommSet_Load handler did (current labels from the instance's PortName/BaudRate).
            // Backing fields are assigned directly so seeding never triggers an apply-on-select.
            _isOpen = _isPortOpen();
            _currentPortLabel = Safe(_currentPortName());
            _currentBaudLabel = _currentBaudRate().ToString(CultureInfo.InvariantCulture);

            string seededPort = Safe(_currentPortName());
            if (AvailablePorts.Contains(seededPort))
            {
                _selectedPortName = seededPort;
            }

            if (ShowBaud)
            {
                _selectedBaud = _currentBaudRate().ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Gets the human-readable group title for this serial port row.</summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets a value indicating whether this port exposes a selectable baud rate. <c>true</c> for
        /// GPS/GPS2/RTCM; <c>false</c> for the fixed-baud module ports (IMU/Steer/Machine). The view
        /// binds the baud combo's visibility to this flag.
        /// </summary>
        public bool ShowBaud { get; }

        /// <summary>Gets the shared, rescannable collection of discovered serial port names.</summary>
        public ObservableCollection<string> AvailablePorts { get; }

        /// <summary>Gets the selectable baud rates for this port (empty for module ports).</summary>
        public ObservableCollection<string> BaudRates { get; }

        /// <summary>
        /// Gets the command that opens this serial port. Enabled only while the port is closed,
        /// reproducing the WinForms Open button's enabled state.
        /// </summary>
        public ICommand OpenCommand { get; }

        /// <summary>
        /// Gets the command that closes this serial port. Enabled only while the port is open,
        /// reproducing the WinForms Close button's enabled state.
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Gets or sets the port name currently chosen in the combo. Setting it to a new, non-empty
        /// value applies the name to the live serial port and the static field the open routine reads
        /// (mirroring the WinForms <c>cbox*Port_SelectedIndexChanged</c> handler). For the module
        /// ports the current-port label is refreshed immediately, exactly as the original did.
        /// </summary>
        public string SelectedPortName
        {
            get { return _selectedPortName; }
            set
            {
                string newValue = value ?? string.Empty;
                if (newValue == _selectedPortName)
                {
                    return;
                }

                _selectedPortName = newValue;
                NotifyPropertyChanged();

                if (newValue.Length == 0)
                {
                    return;
                }

                // [XPLAT] mf.sp<Port>.PortName = cbox.Text; FormLoop.portName<Port> = cbox.Text;
                _applyPortName(newValue);

                if (_updateLabelOnSelect)
                {
                    // [XPLAT] lblCurrent<Port>.Text = cbox.Text; (module ports update on select).
                    CurrentPortLabel = newValue;
                }
            }
        }

        /// <summary>
        /// Gets or sets the baud rate currently chosen in the combo (as text). Setting it to a new,
        /// non-empty value parses it with <see cref="CultureInfo.InvariantCulture"/> — replacing the
        /// WinForms <c>Convert.ToInt32(cbox.Text)</c> — and applies it to the live serial port and the
        /// static field the open routine reads. No-op for the fixed-baud module ports.
        /// </summary>
        public string SelectedBaud
        {
            get { return _selectedBaud; }
            set
            {
                string newValue = value ?? string.Empty;
                if (newValue == _selectedBaud)
                {
                    return;
                }

                _selectedBaud = newValue;
                NotifyPropertyChanged();

                if (!ShowBaud || newValue.Length == 0)
                {
                    return;
                }

                // [XPLAT] Convert.ToInt32(cboxBaud.Text) -> int.Parse(baudText, InvariantCulture).
                // TryParse guards against a transient empty/partial binding value without throwing on
                // the UI thread; the combo only ever offers valid numeric baud strings.
                int baud;
                if (int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out baud))
                {
                    if (_applyBaudRate != null)
                    {
                        _applyBaudRate(baud);
                    }
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the underlying shared serial port is currently open.
        /// Updated after every open/close. Setting it also refreshes <see cref="CanEditSettings"/>
        /// and re-evaluates the Open/Close command availability (the WinForms button enabled states).
        /// </summary>
        public bool IsOpen
        {
            get { return _isOpen; }
            private set
            {
                if (value == _isOpen)
                {
                    return;
                }

                _isOpen = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CanEditSettings));
                ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CloseCommand).RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Gets a value indicating whether the baud/port combos may be edited. <c>true</c> while the
        /// port is closed, <c>false</c> while it is open — reproducing the WinForms behaviour of
        /// disabling the combos for an open port.
        /// </summary>
        public bool CanEditSettings
        {
            get { return !_isOpen; }
        }

        /// <summary>
        /// Gets the "current port" label for this row (the WinForms <c>lblCurrent*Port</c>). Seeded
        /// from the live serial port and refreshed on open (all ports) and on selection (module ports).
        /// </summary>
        public string CurrentPortLabel
        {
            get { return _currentPortLabel; }
            private set
            {
                string newValue = Safe(value);
                if (newValue == _currentPortLabel)
                {
                    return;
                }

                _currentPortLabel = newValue;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CurrentLabel));
            }
        }

        /// <summary>
        /// Gets the "current baud" label for this row (the WinForms <c>lblCurrent*Baud</c>). Seeded
        /// from the live serial port and refreshed on open. For the fixed-baud module ports this is
        /// the constant module baud and the view typically hides it (see <see cref="ShowBaud"/>).
        /// </summary>
        public string CurrentBaudLabel
        {
            get { return _currentBaudLabel; }
            private set
            {
                string newValue = Safe(value);
                if (newValue == _currentBaudLabel)
                {
                    return;
                }

                _currentBaudLabel = newValue;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CurrentLabel));
            }
        }

        /// <summary>
        /// Gets a single combined "current configuration" string for views that prefer one label per
        /// row over the original two-label layout. For baud ports it is "<c>baud  port</c>"; for the
        /// fixed-baud module ports it is just the port name.
        /// </summary>
        public string CurrentLabel
        {
            get
            {
                if (ShowBaud)
                {
                    return string.Concat(_currentBaudLabel, "  ", _currentPortLabel);
                }

                return _currentPortLabel;
            }
        }

        /// <summary>
        /// [XPLAT] Opens the shared serial port and refreshes this row's state. The wrapped
        /// <c>SerialCommService.Open*Port()</c> catches its own exceptions, so a failure surfaces as
        /// the port simply not being open afterwards; the belt-and-braces try/catch covers any
        /// unexpected throw from applying settings. When the open fails the GPS-family ports show the
        /// original "Unable to connect to Port" message (the module ports stay silent, exactly as the
        /// WinForms dialog did).
        /// </summary>
        private void OnOpen()
        {
            try
            {
                _openPort();
            }
            catch (Exception ex)
            {
                _raiseTimedMessage(2000, "Connection Error", ex.Message);
            }

            RefreshState();

            if (!IsOpen && _showConnectFailMessage)
            {
                // [XPLAT] MessageBox.Show("Unable to connect to Port") -> RequestTimedMessage.
                _raiseTimedMessage(2000, "No Connection", "Unable to connect to Port");
            }
        }

        /// <summary>
        /// [XPLAT] Closes the shared serial port and refreshes this row's state. The wrapped
        /// <c>SerialCommService.Close*Port()</c> handles its own exceptions.
        /// </summary>
        private void OnClose()
        {
            _closePort();
            RefreshState();
        }

        /// <summary>
        /// Re-reads the live serial port and refreshes <see cref="IsOpen"/> and the current labels.
        /// On a successful open the current-port and current-baud labels are refreshed for every port
        /// (matching the WinForms <c>btnOpen*_Click</c> handlers, which set <c>lblCurrent*</c> when the
        /// port opened).
        /// </summary>
        private void RefreshState()
        {
            IsOpen = _isPortOpen();

            if (IsOpen)
            {
                CurrentPortLabel = Safe(_currentPortName());
                CurrentBaudLabel = _currentBaudRate().ToString(CultureInfo.InvariantCulture);
            }
        }

        // [XPLAT] Never surface a null reference as visible "null" text (UI defensive pattern); a
        // null port name degrades to an empty string. Nullable reference types are disabled
        // project-wide, so the ?? guard (not a nullable annotation) is the portable form.
        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }
    }

    /// <summary>
    /// [XPLAT] View-model backing the cross-platform <c>FormCommSetGPS</c> dialog — the largest AgIO
    /// configuration dialog. It configures and opens/closes AgIO's six serial ports (GPS, GPS2, RTCM,
    /// IMU, steer module, machine module) and shows the live receive traffic. It replaces the WinForms
    /// <c>FormCommSetGPS</c> (which took a <c>FormLoop mf</c> back-reference and drove the form's serial
    /// ports directly).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal (AAP §0.1.1).</b> Every former <c>mf.*</c> reference is replaced by the
    /// injected <see cref="CommCoordinatorService"/>: the six serial ports and their
    /// <c>Open*/Close*Port</c> methods are reached through <c>comm.Serial</c> (the migrated
    /// <see cref="SerialCommService"/> that now owns all six ports), the live receive buffers through
    /// <c>comm.Serial.recv*Sentence</c>, and the GPS-in traffic counter through
    /// <c>comm.Udp.Traffic.cntrGPSIn</c>. This dialog only <i>commands</i> the shared service; it never
    /// owns the ports (see <see cref="Stop"/>).
    /// </para>
    /// <para>
    /// <b>Repetition factored out.</b> The six near-identical port blocks are each represented by a
    /// <see cref="SerialPortItemViewModel"/>, built in the constructor by <c>CreatePortItem</c> with
    /// closures that bind the row to the specific shared port and the specific static port/baud fields
    /// the open routine reads. GPS/GPS2/RTCM are baud-selectable; IMU/Steer/Machine are fixed-baud
    /// (port-only), exactly as the WinForms dialog laid them out.
    /// </para>
    /// <para>
    /// <b>Culture safety (AAP §0.6.5).</b> Every numeric format/parse — the baud parsing in each row and
    /// the GPS-in counter rendered by <see cref="FromGpsText"/> — uses
    /// <see cref="CultureInfo.InvariantCulture"/>, so a locale with a comma decimal separator can never
    /// corrupt a baud rate or a counter display.
    /// </para>
    /// <para>
    /// <b>Messages and dismissal.</b> The former <c>MessageBox.Show</c> popups are raised through
    /// <see cref="RequestTimedMessage"/> (the view routes it to the <c>IErrorPresenter</c> /
    /// <c>FormTimedMessage</c>), and the OK/close buttons raise <see cref="RequestClose"/> — keeping the
    /// view-model free of any WinForms or windowing dependency.
    /// </para>
    /// </remarks>
    public class FormCommSetGPSViewModel : ViewModel
    {
        // [XPLAT] The injected comms aggregate that owns the shared serial/UDP services. Replaces the
        // WinForms FormLoop "mf" back-reference. Never null (guarded in the constructor).
        private readonly CommCoordinatorService _comm;

        // [XPLAT] 500 ms display-refresh timer reproducing the WinForms timer1 (Interval = 500). It is
        // display-only: it reads the receive buffers, the bottom-status port labels and the GPS-in
        // counter, and adds no latency to the comm path (AAP §0.6.1).
        private readonly DispatcherTimer _refreshTimer;

        private string _recvText = string.Empty;
        private string _recvText2 = string.Empty;
        private string _steerPortLabel = string.Empty;
        private string _gpsPortLabel = string.Empty;
        private string _imuPortLabel = string.Empty;
        private string _machinePortLabel = string.Empty;
        private string _fromGpsText = "--";

        /// <summary>
        /// [XPLAT] Initializes the dialog view-model from the shared comms aggregate, builds the six
        /// per-port rows, populates the available port names and baud lists, seeds the live display, and
        /// starts the 500 ms refresh timer.
        /// </summary>
        /// <param name="comm">
        /// The comms aggregate that owns the shared serial ports and UDP transport. Must not be
        /// <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="comm"/> is <c>null</c>.</exception>
        public FormCommSetGPSViewModel(CommCoordinatorService comm)
        {
            if (comm == null) throw new ArgumentNullException(nameof(comm));

            _comm = comm;

            // [XPLAT] Available port NAMES come through SerialCommService.GetAvailablePortNames(), which
            // routes to IPlatformServices.GetSerialPortNames() (COMx on Windows; /dev/ttyUSB*,
            // /dev/ttyACM* on Linux; /dev/cu.* on macOS) instead of the Windows-leaning
            // SerialPort.GetPortNames(). One shared collection backs every port combo, so a rescan
            // updates them all at once.
            AvailablePorts = new ObservableCollection<string>();
            PopulatePorts();

            // [XPLAT] Original baud lists preserved verbatim from the WinForms designer. GPS and GPS2
            // share one list; RTCM offers the higher 128000/256000 rates as well.
            GpsBaudRates = new ObservableCollection<string>(
                new[] { "4800", "9600", "19200", "38400", "57600", "115200", "460800" });
            RtcmBaudRates = new ObservableCollection<string>(
                new[] { "4800", "9600", "19200", "38400", "57600", "115200", "128000", "256000" });

            // [XPLAT] The module ports (IMU/Steer/Machine) have no baud combo; an empty list backs them.
            ObservableCollection<string> noBaud = new ObservableCollection<string>();

            SerialCommService serial = _comm.Serial;

            // ---- The six port rows (AAP §0.3.2 Extract Class) ------------------------------------
            // Each closure binds the row to one shared SerialPort and the matching static port/baud
            // fields the Open*Port routines read. The instance property is set as well so the live port
            // reflects the choice immediately, exactly as the WinForms SelectedIndexChanged handlers did
            // (which set both mf.sp<Port>.X and the FormLoop.<field>).
            GpsPort = CreatePortItem(
                "GPS", true, GpsBaudRates,
                portName => { serial.spGPS.PortName = portName; SerialCommService.portNameGPS = portName; },
                baud => { serial.spGPS.BaudRate = baud; SerialCommService.baudRateGPS = baud; },
                serial.OpenGPSPort, serial.CloseGPSPort,
                () => serial.spGPS.IsOpen, () => serial.spGPS.PortName, () => serial.spGPS.BaudRate);

            Gps2Port = CreatePortItem(
                "GPS2", true, GpsBaudRates,
                portName => { serial.spGPS2.PortName = portName; SerialCommService.portNameGPS2 = portName; },
                baud => { serial.spGPS2.BaudRate = baud; SerialCommService.baudRateGPS2 = baud; },
                serial.OpenGPS2Port, serial.CloseGPS2Port,
                () => serial.spGPS2.IsOpen, () => serial.spGPS2.PortName, () => serial.spGPS2.BaudRate);

            RtcmPort = CreatePortItem(
                "RTCM", true, RtcmBaudRates,
                portName => { serial.spRtcm.PortName = portName; SerialCommService.portNameRtcm = portName; },
                baud => { serial.spRtcm.BaudRate = baud; SerialCommService.baudRateRtcm = baud; },
                serial.OpenRtcmPort, serial.CloseRtcmPort,
                () => serial.spRtcm.IsOpen, () => serial.spRtcm.PortName, () => serial.spRtcm.BaudRate);

            ImuPort = CreatePortItem(
                "IMU", false, noBaud,
                portName => { serial.spIMU.PortName = portName; SerialCommService.portNameIMU = portName; },
                null,
                serial.OpenIMUPort, serial.CloseIMUPort,
                () => serial.spIMU.IsOpen, () => serial.spIMU.PortName, () => serial.spIMU.BaudRate);

            SteerPort = CreatePortItem(
                "Steer Module", false, noBaud,
                portName => { serial.spSteerModule.PortName = portName; SerialCommService.portNameSteerModule = portName; },
                null,
                serial.OpenSteerModulePort, serial.CloseSteerModulePort,
                () => serial.spSteerModule.IsOpen, () => serial.spSteerModule.PortName, () => serial.spSteerModule.BaudRate);

            MachinePort = CreatePortItem(
                "Machine Module", false, noBaud,
                portName => { serial.spMachineModule.PortName = portName; SerialCommService.portNameMachineModule = portName; },
                null,
                serial.OpenMachineModulePort, serial.CloseMachineModulePort,
                () => serial.spMachineModule.IsOpen, () => serial.spMachineModule.PortName, () => serial.spMachineModule.BaudRate);

            RescanCommand = new RelayCommand(OnRescan);
            OkCommand = new RelayCommand(OnClose);
            CloseCommand = new RelayCommand(OnClose);

            // Seed the live-display fields once so the dialog shows current values before the first tick.
            RefreshLiveData();

            // [XPLAT] timer1 (Interval = 500) -> Avalonia DispatcherTimer; runs on the UI thread, so the
            // bound display properties update safely. Started here (the dialog timer was always enabled);
            // the view calls Stop() on close.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += OnRefreshTick;
            _refreshTimer.Start();
        }

        /// <summary>Gets the GPS serial-port row (baud-selectable).</summary>
        public SerialPortItemViewModel GpsPort { get; }

        /// <summary>Gets the second-GPS serial-port row (baud-selectable).</summary>
        public SerialPortItemViewModel Gps2Port { get; }

        /// <summary>Gets the RTCM serial-port row (baud-selectable, with the higher RTCM baud list).</summary>
        public SerialPortItemViewModel RtcmPort { get; }

        /// <summary>Gets the IMU serial-port row (fixed baud, port-only).</summary>
        public SerialPortItemViewModel ImuPort { get; }

        /// <summary>Gets the steer-module serial-port row (fixed baud, port-only).</summary>
        public SerialPortItemViewModel SteerPort { get; }

        /// <summary>Gets the machine-module serial-port row (fixed baud, port-only).</summary>
        public SerialPortItemViewModel MachinePort { get; }

        /// <summary>
        /// Gets the shared collection of discovered serial port names, bound by every port combo and
        /// refreshed by <see cref="RescanCommand"/>.
        /// </summary>
        public ObservableCollection<string> AvailablePorts { get; }

        /// <summary>Gets the selectable baud rates for the GPS and GPS2 ports.</summary>
        public ObservableCollection<string> GpsBaudRates { get; }

        /// <summary>Gets the selectable baud rates for the RTCM port (includes 128000 / 256000).</summary>
        public ObservableCollection<string> RtcmBaudRates { get; }

        /// <summary>
        /// Gets the command that re-enumerates the available serial ports into
        /// <see cref="AvailablePorts"/>, reproducing the WinForms "rescan" button.
        /// </summary>
        public ICommand RescanCommand { get; }

        /// <summary>Gets the command that confirms and dismisses the dialog (the WinForms OK button).</summary>
        public ICommand OkCommand { get; }

        /// <summary>Gets the command that dismisses the dialog.</summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// [XPLAT] Raised when the dialog should close. The hosting window subscribes and closes itself.
        /// Initialized to a no-op delegate so it is always safe to invoke. Replaces the WinForms
        /// <c>Close()</c> call.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// [XPLAT] Raised to surface a transient message (duration in milliseconds, title, body),
        /// replacing the WinForms <c>MessageBox.Show</c> popups. The hosting window routes it to the
        /// <c>IErrorPresenter</c> / <c>FormTimedMessage</c>. Initialized to a no-op delegate so it is
        /// always safe to invoke.
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Gets the latest raw GPS sentence received on the primary GPS port (the WinForms
        /// <c>textBoxRcv</c>). Refreshed by the display timer.
        /// </summary>
        public string RecvText
        {
            get { return _recvText; }
            private set { SetField(ref _recvText, value); }
        }

        /// <summary>
        /// Gets the latest raw GPS sentence received on the secondary GPS port (the WinForms
        /// <c>textBoxRcv2</c>). Refreshed by the display timer.
        /// </summary>
        public string RecvText2
        {
            get { return _recvText2; }
            private set { SetField(ref _recvText2, value); }
        }

        /// <summary>Gets the steer-module port name shown in the status area (the WinForms <c>lblSteer</c>).</summary>
        public string SteerPortLabel
        {
            get { return _steerPortLabel; }
            private set { SetField(ref _steerPortLabel, value); }
        }

        /// <summary>Gets the GPS port name shown in the status area (the WinForms <c>lblGPS</c>).</summary>
        public string GpsPortLabel
        {
            get { return _gpsPortLabel; }
            private set { SetField(ref _gpsPortLabel, value); }
        }

        /// <summary>Gets the IMU port name shown in the status area (the WinForms <c>lblIMU</c>).</summary>
        public string ImuPortLabel
        {
            get { return _imuPortLabel; }
            private set { SetField(ref _imuPortLabel, value); }
        }

        /// <summary>Gets the machine-module port name shown in the status area (the WinForms <c>lblMachine</c>).</summary>
        public string MachinePortLabel
        {
            get { return _machinePortLabel; }
            private set { SetField(ref _machinePortLabel, value); }
        }

        /// <summary>
        /// Gets the GPS-in traffic counter rendered for display (the WinForms <c>lblFromGPS</c>): the
        /// literal "--" when no GPS frames have been counted, otherwise the count formatted with
        /// <see cref="CultureInfo.InvariantCulture"/>.
        /// </summary>
        public string FromGpsText
        {
            get { return _fromGpsText; }
            private set { SetField(ref _fromGpsText, value); }
        }

        /// <summary>
        /// [XPLAT] Stops the display-refresh timer. The view calls this when the dialog closes. It
        /// deliberately does NOT close any of the six serial ports: they are owned by the shared
        /// <see cref="SerialCommService"/> and must persist beyond this dialog (AAP key insight).
        /// </summary>
        public void Stop()
        {
            _refreshTimer.Stop();
        }

        // [XPLAT] Builds one serial-port row, wiring the shared-service closures and the parent's
        // timed-message relay. Centralizing construction keeps the six rows DRY while leaving each
        // port's exact open/close semantics intact.
        private SerialPortItemViewModel CreatePortItem(
            string displayName,
            bool hasBaudSelection,
            ObservableCollection<string> baudRates,
            Action<string> applyPortName,
            Action<int> applyBaudRate,
            Action openPort,
            Action closePort,
            Func<bool> isPortOpen,
            Func<string> currentPortName,
            Func<int> currentBaudRate)
        {
            return new SerialPortItemViewModel(
                displayName,
                hasBaudSelection,
                AvailablePorts,
                baudRates,
                applyPortName,
                applyBaudRate,
                openPort,
                closePort,
                isPortOpen,
                currentPortName,
                currentBaudRate,
                RaiseTimedMessage);
        }

        // [XPLAT] Clears and re-populates the shared port-name collection from the cross-platform
        // enumeration. Used at construction and by the rescan command.
        private void PopulatePorts()
        {
            AvailablePorts.Clear();
            foreach (string portName in _comm.Serial.GetAvailablePortNames())
            {
                if (!string.IsNullOrEmpty(portName))
                {
                    AvailablePorts.Add(portName);
                }
            }
        }

        // [XPLAT] btnRescan_Click: re-enumerate the serial ports into the shared collection so every
        // combo refreshes at once.
        private void OnRescan()
        {
            PopulatePorts();
        }

        // [XPLAT] btnSerialOK_Click -> Close(): dismiss the dialog via the RequestClose event.
        private void OnClose()
        {
            RequestClose();
        }

        // [XPLAT] Relays a per-port timed message up to the RequestTimedMessage event. Passed to each
        // SerialPortItemViewModel so the rows never reference the event (or a presenter) directly.
        private void RaiseTimedMessage(int durationMs, string title, string message)
        {
            RequestTimedMessage(durationMs, title, message);
        }

        // [XPLAT] DispatcherTimer tick: refresh the display-only fields. Mirrors timer1_Tick.
        private void OnRefreshTick(object sender, EventArgs e)
        {
            RefreshLiveData();
        }

        // [XPLAT] timer1_Tick body: copy the live receive buffers, the bottom-status port labels and the
        // GPS-in counter into the bound properties. Read-only with respect to the comm path — it never
        // touches the serial framing or the open/close state (parity with the WinForms timer, which only
        // updated labels and never changed button/combo enabled state).
        private void RefreshLiveData()
        {
            SerialCommService serial = _comm.Serial;

            RecvText = Safe(serial.recvGPSSentence);
            RecvText2 = Safe(serial.recvGPS2Sentence);
            SteerPortLabel = Safe(serial.spSteerModule.PortName);
            GpsPortLabel = Safe(serial.spGPS.PortName);
            ImuPortLabel = Safe(serial.spIMU.PortName);
            MachinePortLabel = Safe(serial.spMachineModule.PortName);

            // [XPLAT] lblFromGPS.Text = traffic.cntrGPSIn == 0 ? "--" : cntrGPSIn.ToString();
            // ToString gains InvariantCulture so the counter never localizes its digit grouping.
            int gpsIn = _comm.Udp.Traffic.cntrGPSIn;
            FromGpsText = gpsIn == 0 ? "--" : gpsIn.ToString(CultureInfo.InvariantCulture);
        }

        // [XPLAT] Property-setter helper: assigns and raises PropertyChanged only on a real change,
        // coalescing null to empty so the UI never shows literal "null" text.
        private void SetField(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            string newValue = value ?? string.Empty;
            if (newValue == field)
            {
                return;
            }

            field = newValue;
            NotifyPropertyChanged(name);
        }

        // [XPLAT] Local null-coalescing guard mirroring SerialPortItemViewModel.Safe (UI defensive
        // pattern; nullable reference types are disabled project-wide).
        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }
    }
}
