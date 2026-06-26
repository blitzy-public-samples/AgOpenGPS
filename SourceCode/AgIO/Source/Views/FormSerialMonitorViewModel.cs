// [XPLAT] migrated from net48/WinForms FormSerialMonitor.cs + FormSerialMonitor.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows.Input;
using Avalonia.Threading;
using AgIO.Services;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] View-model backing the Avalonia <c>Serial Monitor</c> dialog — a 1:1 parity
    /// reimplementation of the WinForms <c>FormSerialMonitor</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormSerialMonitor.cs</c> + <c>FormSerialMonitor.designer.cs</c>).
    /// The monitor is a standalone serial-port sniffer: it owns its OWN throwaway
    /// <see cref="SerialPort"/> (independent of AgIO's six comm-hub ports), reads the incoming
    /// bytes and appends them to a read-only display buffer, and offers an optional "log" mode
    /// that mirrors the AgIO NMEA log buffer into the same display via a UI timer, plus a
    /// save-to-file action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Frozen byte I/O.</b> The serial framing is preserved byte-for-byte from the WinForms
    /// original (AAP §0.2.2 / §0.7.1): the port is constructed with
    /// <c>(portName, baudRate, Parity.None, 8, StopBits.One)</c>, <see cref="OpenPort"/> sets
    /// <c>DtrEnable</c>/<c>RtsEnable</c>, opens, sleeps 500&#160;ms (the documented mega2560 boot
    /// delay) and discards the in/out buffers, and <see cref="OnDataReceived"/> reads with
    /// <c>ReadExisting()</c>. <see cref="System.IO.Ports"/> is kept because it is cross-platform
    /// (Windows <c>COMx</c>, Linux <c>/dev/ttyUSB*</c>/<c>/dev/ttyACM*</c>, macOS <c>/dev/cu.*</c>);
    /// only port-<i>name</i> enumeration is abstracted.
    /// </para>
    /// <para>
    /// <b>Migration transforms.</b> The WinForms <c>FormLoop mf</c> god-object back-reference is
    /// replaced by a constructor-injected <see cref="CommCoordinatorService"/>; the
    /// <c>isLogMonitorOn</c>/<c>logMonitorSentence</c> log-mirror state it formerly read off the
    /// window now lives on the migrated AgIO state (<see cref="CommCoordinatorService.Nmea"/>) and
    /// is read/written through it. The WPF/WinForms <c>System.Windows.Threading.Dispatcher</c>
    /// UI-thread marshal becomes Avalonia's <see cref="Dispatcher.UIThread"/>; the WinForms
    /// <c>SerialPort.GetPortNames()</c> port discovery becomes
    /// <see cref="SerialCommService.GetAvailablePortNames"/> (which routes to
    /// <c>IPlatformServices.GetSerialPortNames()</c>); and the <c>MessageBox.Show</c>/
    /// <c>TimedMessageBox</c> popups become the <see cref="RequestTimedMessage"/> event the hosting
    /// view turns into an AgIO timed-message toast. The window-close is surfaced through
    /// <see cref="RequestClose"/>, and the WinForms <c>FormClosing</c> teardown becomes
    /// <see cref="Cleanup"/>, which the hosting window calls on close.
    /// </para>
    /// <para>
    /// <b>Culture safety.</b> The baud-rate text is parsed with
    /// <see cref="CultureInfo.InvariantCulture"/> (the WinForms <c>Convert.ToInt32</c> replacement),
    /// so a locale whose decimal/grouping conventions differ can never misread the value
    /// (AAP §0.6.5). The log-file path is composed with <see cref="Path.Combine"/> rather than a
    /// hard-coded separator, preserving the original current-working-directory location on every OS.
    /// </para>
    /// </remarks>
    public class FormSerialMonitorViewModel : ViewModel
    {
        // --------------------------------------------------------------------------------------
        // Preserved statics (FormSerialMonitor.cs L16-L17). These persist the last-used port name
        // and baud rate across dialog instances exactly as the WinForms statics did; the instance
        // SerialPort below is constructed from them, matching the original field initializer.
        // --------------------------------------------------------------------------------------

        /// <summary>[XPLAT] Last-used serial port name; default sentinel <c>"***"</c> (no port chosen).</summary>
        public static string portName = "***";

        /// <summary>[XPLAT] Last-used baud rate; default <c>115200</c> (Teensy default per the dialog title).</summary>
        public static int baudRate = 115200;

        // [XPLAT] The monitor's OWN throwaway serial port (NOT one of the comm hub's six ports).
        // Constructed with the exact frozen framing (8 data bits, no parity, one stop bit) from the
        // WinForms original (FormSerialMonitor.cs L19). The static portName/baudRate are initialized
        // before this instance initializer runs, so this mirrors the original construction exactly.
        private SerialPort _sp = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);

        // [XPLAT] Injected comms aggregate replacing the WinForms `FormLoop mf` back-reference. Read
        // for the cross-platform port-name enumeration (_comm.Serial) and the log-mirror state
        // (_comm.Nmea.isLogMonitorOn / .logMonitorSentence). Never null (constructor-guarded).
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Avalonia UI timer replacing the WinForms `timer1` (Interval 333 ms). When log mode
        // is on, each tick drains the mirrored NMEA log buffer into the display.
        private readonly DispatcherTimer _logTimer;

        // [XPLAT] Backs IsLogOn (the WinForms `logOn` field). When true the log timer is running and
        // _comm.Nmea.isLogMonitorOn is set so the NMEA path mirrors sentences into logMonitorSentence.
        private bool _logOn;

        // Property backing fields (nullable reference types are disabled project-wide, so reference
        // fields are plain non-annotated types; strings default to string.Empty where they are shown).
        private string _selectedPort = string.Empty;
        private string _selectedBaud = string.Empty;
        private string _receivedText = string.Empty;
        private string _currentPort = string.Empty;
        private string _currentBaud = string.Empty;
        private bool _isPortOpen;

        // Guards Cleanup() so the FormClosing teardown is idempotent (the hosting window may invoke it
        // more than once across its close sequence).
        private bool _isCleanedUp;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormSerialMonitorViewModel"/> class.
        /// </summary>
        /// <param name="comm">
        /// The injected AgIO comms aggregate. Supplies cross-platform serial port-name enumeration
        /// (<see cref="CommCoordinatorService.Serial"/>) and owns the log-mirror state read/written
        /// by the log mode (<see cref="CommCoordinatorService.Nmea"/>). Must not be <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="comm"/> is <c>null</c>.</exception>
        public FormSerialMonitorViewModel(CommCoordinatorService comm)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;

            // The receive box starts empty (WinForms textBoxRcv default).
            ReceivedText = string.Empty;

            // Populate the available serial ports from the cross-platform enumeration seam
            // (replaces the WinForms FormUDp_Load loop over SerialPort.GetPortNames()).
            AvailablePorts = new ObservableCollection<string>();
            PopulatePorts();

            // The fixed baud-rate choices reproduced verbatim from the WinForms cboxBaud item list
            // (FormSerialMonitor.designer.cs L84-L91), order preserved.
            AvailableBaudRates = new[]
            {
                "4800", "9600", "19200", "38400", "57600", "115200", "460800"
            };

            // Default the selected baud to the persisted static value (the WinForms default 115200);
            // no port is pre-selected, matching the original (the combo was populated but unselected).
            _selectedBaud = baudRate.ToString(CultureInfo.InvariantCulture);

            // [XPLAT] WinForms timer1 (Interval = 333) -> Avalonia DispatcherTimer. Armed here but only
            // started when log mode is toggled on (parity with timer1.Enabled).
            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(333) };
            _logTimer.Tick += OnLogTimerTick;

            // Command wiring (replaces the WinForms button Click handlers).
            OpenCommand = new RelayCommand(OpenPort);
            CloseCommand = new RelayCommand(ClosePort);
            LogCommand = new RelayCommand(ToggleLog);
            SaveFileCommand = new RelayCommand(SaveLogFile);
            ClearCommand = new RelayCommand(ClearReceived);
            RescanCommand = new RelayCommand(PopulatePorts);
            AcceptCommand = new RelayCommand(OnAccept);
        }

        // ==========================================================================================
        // Bound properties
        // ==========================================================================================

        /// <summary>
        /// Gets the available serial port names shown in the port chooser. Populated from the
        /// cross-platform enumeration seam (<see cref="SerialCommService.GetAvailablePortNames"/> →
        /// <c>IPlatformServices.GetSerialPortNames()</c>), so it carries <c>COMx</c> on Windows and
        /// <c>/dev/tty*</c>/<c>/dev/cu.*</c> on Linux/macOS. Replaces the WinForms <c>cboxPort</c> items.
        /// </summary>
        public ObservableCollection<string> AvailablePorts { get; }

        /// <summary>
        /// Gets the fixed baud-rate choices shown in the baud chooser, reproduced verbatim from the
        /// WinForms <c>cboxBaud</c> item list. Replaces the designer-authored combo items.
        /// </summary>
        public IReadOnlyList<string> AvailableBaudRates { get; }

        /// <summary>
        /// Gets or sets the currently selected serial port name. Setting it mirrors the WinForms
        /// <c>cboxPort_SelectedIndexChanged</c> handler: the persisted <see cref="portName"/> static is
        /// updated, and the live port's <see cref="SerialPort.PortName"/> is updated as well while the
        /// port is closed (it cannot be changed on an open port). Empty/blank selections are ignored.
        /// </summary>
        public string SelectedPort
        {
            get { return _selectedPort; }
            set
            {
                if (value == _selectedPort)
                {
                    return;
                }

                _selectedPort = value;

                // Mirror FormSerialMonitor.cs L127-L128 (sp.PortName = cboxPort.Text; portName = cboxPort.Text)
                // but guard against null/empty (SerialPort.PortName rejects them) and against changing the
                // name while open (the WinForms combo was disabled when open, so it never fired then).
                if (!string.IsNullOrEmpty(value))
                {
                    portName = value;
                    if (!_sp.IsOpen)
                    {
                        _sp.PortName = value;
                    }
                }

                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the currently selected baud-rate text. Setting it mirrors the WinForms
        /// <c>cboxBaud_SelectedIndexChanged</c> handler, parsing the text with
        /// <see cref="CultureInfo.InvariantCulture"/> (replacing <c>Convert.ToInt32</c>) into the
        /// persisted <see cref="baudRate"/> static, and updating the live port's
        /// <see cref="SerialPort.BaudRate"/> while the port is closed.
        /// </summary>
        public string SelectedBaud
        {
            get { return _selectedBaud; }
            set
            {
                if (value == _selectedBaud)
                {
                    return;
                }

                _selectedBaud = value;

                // [XPLAT] Convert.ToInt32(cboxBaud.Text) -> int.Parse(..., InvariantCulture). The choices
                // are a fixed integer list so this never throws in practice; the non-empty guard avoids a
                // parse on the transient empty value a binding can push during initialization.
                if (!string.IsNullOrEmpty(value))
                {
                    int parsed = int.Parse(value, CultureInfo.InvariantCulture);
                    baudRate = parsed;
                    if (!_sp.IsOpen)
                    {
                        _sp.BaudRate = parsed;
                    }
                }

                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets the accumulated received text shown in the read-only display box (the WinForms
        /// <c>textBoxRcv</c>). Appended to by <see cref="AppendReceived"/> as serial data and mirrored
        /// log lines arrive, and reset by <see cref="ClearCommand"/>.
        /// </summary>
        public string ReceivedText
        {
            get { return _receivedText; }
            private set
            {
                if (value == _receivedText)
                {
                    return;
                }

                // Never surface a null as visible "null" text; coalesce to empty (UI8 defensive pattern).
                _receivedText = value ?? string.Empty;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether log mode is active. Mirrors the WinForms
        /// <c>btnLog_Click</c> toggle: enabling it sets <c>_comm.Nmea.isLogMonitorOn</c> and starts the
        /// log timer; disabling it clears that flag and stops the timer. The view binds the log button's
        /// highlight (green when on) to this value.
        /// </summary>
        public bool IsLogOn
        {
            get { return _logOn; }
            set
            {
                if (value == _logOn)
                {
                    return;
                }

                _logOn = value;

                if (_logOn)
                {
                    // [XPLAT] mf.isLogMonitorOn = true; timer1.Enabled = true  (log-mirror state on _comm.Nmea)
                    _comm.Nmea.isLogMonitorOn = true;
                    _logTimer.Start();
                }
                else
                {
                    // [XPLAT] mf.isLogMonitorOn = false; timer1.Enabled = false
                    _comm.Nmea.isLogMonitorOn = false;
                    _logTimer.Stop();
                }

                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets a value indicating whether the serial port is currently open. Drives the enabled state
        /// of the open/close buttons and (via <see cref="CanEditConnection"/>) the port/baud choosers,
        /// reproducing the WinForms enable/disable logic in <c>btnOpenSerial_Click</c>/<c>btnCloseSerial_Click</c>.
        /// </summary>
        public bool IsPortOpen
        {
            get { return _isPortOpen; }
            private set
            {
                if (value == _isPortOpen)
                {
                    return;
                }

                _isPortOpen = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CanEditConnection));
            }
        }

        /// <summary>
        /// Gets a value indicating whether the port and baud choosers may be edited (i.e. the port is
        /// closed). The WinForms dialog disabled <c>cboxPort</c>/<c>cboxBaud</c> while connected.
        /// </summary>
        public bool CanEditConnection
        {
            get { return !_isPortOpen; }
        }

        /// <summary>
        /// Gets the port name of the live connection, shown once connected (the WinForms
        /// <c>lblCurrentPort</c>). Empty until the port opens.
        /// </summary>
        public string CurrentPort
        {
            get { return _currentPort; }
            private set
            {
                if (value == _currentPort)
                {
                    return;
                }

                _currentPort = value ?? string.Empty;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets the baud rate of the live connection, shown once connected (the WinForms
        /// <c>lblCurrentBaud</c>). Empty until the port opens.
        /// </summary>
        public string CurrentBaud
        {
            get { return _currentBaud; }
            private set
            {
                if (value == _currentBaud)
                {
                    return;
                }

                _currentBaud = value ?? string.Empty;
                NotifyPropertyChanged();
            }
        }

        // ==========================================================================================
        // Commands (replace the WinForms button Click handlers)
        // ==========================================================================================

        /// <summary>Gets the command that opens the serial port (the WinForms <c>btnOpenSerial</c>).</summary>
        public ICommand OpenCommand { get; }

        /// <summary>Gets the command that closes the serial port (the WinForms <c>btnCloseSerial</c>).</summary>
        public ICommand CloseCommand { get; }

        /// <summary>Gets the command that toggles log mode (the WinForms <c>btnLog</c>).</summary>
        public ICommand LogCommand { get; }

        /// <summary>Gets the command that saves the received text to a log file (the WinForms <c>btnFileSave</c>).</summary>
        public ICommand SaveFileCommand { get; }

        /// <summary>Gets the command that clears the received-text display (the WinForms <c>btnClear</c>).</summary>
        public ICommand ClearCommand { get; }

        /// <summary>Gets the command that rescans the available serial ports (the WinForms <c>btnRescan</c>).</summary>
        public ICommand RescanCommand { get; }

        /// <summary>
        /// Gets the command that accepts/closes the dialog (the WinForms <c>btnSerialCancel</c>): it turns
        /// off log mode and raises <see cref="RequestClose"/> so the hosting window closes.
        /// </summary>
        public ICommand AcceptCommand { get; }

        // ==========================================================================================
        // Events (MVVM replacements for the WinForms popups / window close)
        // ==========================================================================================

        /// <summary>
        /// Raised to ask the hosting view to show a transient, self-dismissing notification — the MVVM
        /// replacement for the WinForms <c>MessageBox.Show</c>/<c>FormLoop.TimedMessageBox</c> popups.
        /// Arguments are the display duration in milliseconds, the title, and the message body.
        /// Initialized to a no-op delegate so it is always safe to invoke (nullable refs are disabled).
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Raised to ask the hosting window to close the dialog (the WinForms <c>Close()</c> call in
        /// <c>btnSerialCancel_Click</c>). Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action RequestClose = delegate { };

        // ==========================================================================================
        // Port enumeration
        // ==========================================================================================

        /// <summary>
        /// Refreshes <see cref="AvailablePorts"/> from the cross-platform port-name enumeration seam.
        /// Replaces the WinForms <c>FormUDp_Load</c>/<c>btnRescan_Click</c> loops over
        /// <c>SerialPort.GetPortNames()</c>; <see cref="SerialCommService.GetAvailablePortNames"/> routes
        /// to <c>IPlatformServices.GetSerialPortNames()</c> and never returns <c>null</c>.
        /// </summary>
        private void PopulatePorts()
        {
            AvailablePorts.Clear();
            foreach (string s in _comm.Serial.GetAvailablePortNames())
            {
                AvailablePorts.Add(s);
            }
        }

        // ==========================================================================================
        // Serial port logic (frozen framing — ported byte-for-byte from FormSerialMonitor.cs)
        // ==========================================================================================

        /// <summary>
        /// Opens the monitor's serial port. Ported verbatim from <c>OpenPort()</c>
        /// (FormSerialMonitor.cs L49-L74): on a closed port it applies the selected name/baud, attaches
        /// the receive handler and asserts DTR/RTS; it then opens, and on success waits the documented
        /// 500&#160;ms module-boot delay before discarding the in/out buffers. The only migration change
        /// is that an open failure is surfaced through <see cref="RequestTimedMessage"/> (replacing
        /// <c>MessageBox.Show("Unable to connect to Port")</c>) once the bound connection state is
        /// refreshed.
        /// </summary>
        public void OpenPort()
        {
            if (!_sp.IsOpen)
            {
                _sp.PortName = portName;
                _sp.BaudRate = baudRate;
                _sp.DataReceived += OnDataReceived;
                _sp.DtrEnable = true;
                _sp.RtsEnable = true;
            }

            try
            {
                _sp.Open();
            }
            catch (Exception)
            {
                // [XPLAT] WinForms logged via Log.EventWriter and left the port closed; the closed state
                // is surfaced to the operator below through RequestTimedMessage, matching the original UX.
            }

            if (_sp.IsOpen)
            {
                // short delay for the use of mega2560, it is working in debugmode with breakpoint
                System.Threading.Thread.Sleep(500); // 500 was not enough

                _sp.DiscardOutBuffer();
                _sp.DiscardInBuffer();
            }

            // Refresh the bound connection state (WinForms enabled-state + label updates), then report a
            // failed open exactly as the original MessageBox did.
            UpdateConnectionState();

            if (!_sp.IsOpen)
            {
                RequestTimedMessage(2000, "Unable to connect to Port", string.Empty);
            }
        }

        /// <summary>
        /// Closes the monitor's serial port. Ported from <c>ClosePort()</c> (FormSerialMonitor.cs
        /// L77-L94): the receive handler is detached and the port closed, with a close failure surfaced
        /// through <see cref="RequestTimedMessage"/> (replacing the WinForms
        /// <c>MessageBox.Show(e.Message, "Connection already terminated??")</c>). Final disposal is
        /// deferred to <see cref="Cleanup"/> so the port can be reopened within the same dialog session;
        /// the byte I/O path is unaffected by this timing.
        /// </summary>
        public void ClosePort()
        {
            if (_sp.IsOpen)
            {
                _sp.DataReceived -= OnDataReceived;
                try
                {
                    _sp.Close();
                }
                catch (Exception ex)
                {
                    RequestTimedMessage(2000, "Connection already terminated??", ex.Message);
                }
            }

            UpdateConnectionState();
        }

        /// <summary>
        /// Serial <see cref="SerialPort.DataReceived"/> handler. Ported from <c>sp_DataReceived</c>
        /// (FormSerialMonitor.cs L96-L110): it reads the available text with <c>ReadExisting()</c> and
        /// appends it to the display. The WPF <c>Dispatcher.BeginInvoke</c> marshal becomes the
        /// Avalonia UI-thread marshal performed inside <see cref="AppendReceived"/>. Read failures are
        /// swallowed exactly as the original did, so a transient serial glitch never tears down the view.
        /// </summary>
        /// <param name="sender">The serial port raising the event (unused).</param>
        /// <param name="e">The receive event payload (unused; the buffer is drained with ReadExisting).</param>
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_sp.IsOpen)
            {
                try
                {
                    string sentence = _sp.ReadExisting();
                    AppendReceived(sentence);
                }
                catch (Exception)
                {
                    // [XPLAT] swallow exactly as the WinForms sp_DataReceived catch did (a torn-down or
                    // mid-close port can raise here; the empty catch keeps the monitor resilient).
                }
            }
        }

        /// <summary>
        /// Appends text to <see cref="ReceivedText"/>, marshalling onto the Avalonia UI thread when called
        /// from a non-UI thread (the serial <see cref="SerialPort.DataReceived"/> callback). Replaces the
        /// WinForms <c>ReceivePort</c>/<c>_dispatcher.BeginInvoke(...)</c> pair. When already on the UI
        /// thread (the log-timer tick), the append is applied directly.
        /// </summary>
        /// <param name="s">The text to append; a <c>null</c> value is treated as empty.</param>
        public void AppendReceived(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                ReceivedText = _receivedText + s;
            }
            else
            {
                Dispatcher.UIThread.Post(() => ReceivedText = _receivedText + s);
            }
        }

        // ==========================================================================================
        // Command implementations
        // ==========================================================================================

        // Toggles log mode (the WinForms btnLog_Click `logOn = !logOn`). The IsLogOn setter performs the
        // _comm.Nmea.isLogMonitorOn write and the timer start/stop.
        private void ToggleLog()
        {
            IsLogOn = !IsLogOn;
        }

        // Clears the received-text display (the WinForms btnClear_Click `textBoxRcv.Text = ""`).
        private void ClearReceived()
        {
            ReceivedText = string.Empty;
        }

        /// <summary>
        /// Log-timer tick. Ported from <c>timer1_Tick</c> (FormSerialMonitor.cs L205-L209): it appends the
        /// mirrored AgIO NMEA log buffer (now <c>_comm.Nmea.logMonitorSentence</c>) into the display and
        /// then clears that buffer. Runs on the Avalonia UI thread, so the append is applied directly.
        /// </summary>
        /// <param name="sender">The timer raising the tick (unused).</param>
        /// <param name="e">The tick payload (unused).</param>
        private void OnLogTimerTick(object sender, EventArgs e)
        {
            AppendReceived(_comm.Nmea.logMonitorSentence.ToString());
            _comm.Nmea.logMonitorSentence.Clear();
        }

        /// <summary>
        /// Writes the received text to a log file. Ported from <c>btnFileSave_Click</c>
        /// (FormSerialMonitor.cs L211-L219): the file is overwritten (not appended), keeping the original
        /// name <c>zAgIO_SerialMon_log.txt</c> in the current working directory — composed with
        /// <see cref="Path.Combine"/> for cross-platform correctness — and a confirmation toast is raised
        /// through <see cref="RequestTimedMessage"/> (replacing <c>mf.TimedMessageBox</c>).
        /// </summary>
        public void SaveLogFile()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "zAgIO_SerialMon_log.txt");

            using (StreamWriter writer = new StreamWriter(path, false))
            {
                writer.Write(ReceivedText);
            }

            RequestTimedMessage(2000, "File Saved", "To zAgIO_SerialMon_Log.Txt");
        }

        // Accepts/closes the dialog (the WinForms btnSerialCancel_Click): turn off log mirroring, then ask
        // the host to close. Cleanup() (invoked by the window on close) repeats the isLogMonitorOn reset
        // idempotently, so both close paths converge on the same teardown.
        private void OnAccept()
        {
            _comm.Nmea.isLogMonitorOn = false;
            RequestClose();
        }

        /// <summary>
        /// Refreshes the bound connection state from the live port. Reproduces the WinForms enabled-state
        /// and label logic: <see cref="IsPortOpen"/> (and thus <see cref="CanEditConnection"/>) tracks the
        /// port, and on a successful open the <see cref="CurrentPort"/>/<see cref="CurrentBaud"/> labels
        /// are set from the live port settings.
        /// </summary>
        private void UpdateConnectionState()
        {
            IsPortOpen = _sp.IsOpen;

            if (_sp.IsOpen)
            {
                CurrentPort = _sp.PortName;
                CurrentBaud = _sp.BaudRate.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Tears the monitor down when its hosting window closes. Reproduces the WinForms
        /// <c>FormSerialMonitor_FormClosing</c> handler (which reset <c>mf.isLogMonitorOn = false</c>) plus
        /// the resource cleanup the dialog needed: the log timer is stopped and detached, the serial port's
        /// receive handler is detached, the port is closed (if open) and disposed, and the AgIO log-mirror
        /// flag is cleared. Idempotent — safe to call more than once across the window close sequence.
        /// </summary>
        public void Cleanup()
        {
            if (_isCleanedUp)
            {
                return;
            }

            _isCleanedUp = true;

            if (_logTimer != null)
            {
                _logTimer.Stop();
                _logTimer.Tick -= OnLogTimerTick;
            }

            if (_sp != null)
            {
                _sp.DataReceived -= OnDataReceived;
                try
                {
                    if (_sp.IsOpen)
                    {
                        _sp.Close();
                    }
                }
                catch (Exception)
                {
                    // [XPLAT] a port torn down out from under us can raise on Close; ignore on teardown.
                }

                _sp.Dispose();
            }

            // [XPLAT] FormSerialMonitor_FormClosing: mf.isLogMonitorOn = false  (now on the migrated state).
            _comm.Nmea.isLogMonitorOn = false;
            _logOn = false;
        }


    }
}
