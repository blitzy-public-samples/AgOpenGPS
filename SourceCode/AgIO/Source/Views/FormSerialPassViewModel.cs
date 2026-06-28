// [XPLAT] migrated from net48/WinForms FormSerialPass.cs + FormSerialPass.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Windows.Input;
using AgIO.Properties;
using AgIO.Services;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing <c>FormSerialPassView.axaml</c>, the AgIO <b>Serial Pass-Through</b>
    /// configuration dialog. It routes the inbound RTCM / correction stream to a serial radio port
    /// and/or out over UDP, owns the operator's pass-through / send-target / radio-port choices, and
    /// triggers a cross-platform program restart when the operator accepts the changes. It replaces the
    /// WinForms <c>FormSerialPass</c> (<c>../Forms/FormSerialPass.cs</c> + <c>.designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The WinForms dialog held a direct <c>FormLoop mf</c> back-reference and
    /// reached through it for the shared radio serial port (<c>mf.spRadio</c>) and the routing flags
    /// (<c>mf.isNTRIP_RequiredOn</c> / <c>isRadio_RequiredOn</c> / <c>isSendToSerial</c> /
    /// <c>isSendToUDP</c>). That coupling is removed: the dialog now takes an injected
    /// <see cref="CommCoordinatorService"/> and operates on the migrated peer that genuinely owns those
    /// members — <see cref="CommCoordinatorService.Ntrip"/> (the <c>NtripService</c>, which owns the
    /// shared radio <see cref="SerialPort"/>) and <see cref="CommCoordinatorService.Serial"/> (the
    /// <c>SerialCommService</c>, whose <c>GetAvailablePortNames()</c> is the cross-platform
    /// port-enumeration seam). The radio port is the SAME port used by the Radio dialog, so it must be
    /// owned by the comm coordinator rather than this dialog.
    /// </para>
    /// <para>
    /// <b>Three-way mutual exclusion (the defining behaviour).</b> Accepting the dialog with pass-through
    /// enabled turns NTRIP and Radio off — both the persisted <c>setNTRIP_isOn</c>/<c>setRadio_isOn</c>
    /// settings and the live <c>NtripService.isNTRIP_RequiredOn</c>/<c>isRadio_RequiredOn</c> flags —
    /// exactly as the WinForms OK handler did, so a correction stream can never be double-routed. The two
    /// "send NTRIP to GPS using" targets (<see cref="SendToSerial"/> and <see cref="SendToUdp"/>) are
    /// themselves mutually exclusive: enabling one disables the other, reproducing the WinForms
    /// <c>cboxToSerial</c>/<c>cboxToUDP</c> click handlers.
    /// </para>
    /// <para>
    /// <b>Serial framing frozen.</b> The cross-platform <see cref="SerialPort"/> type is kept verbatim;
    /// only port-<i>name</i> enumeration is abstracted (it flows through
    /// <c>SerialCommService.GetAvailablePortNames()</c> → <c>IPlatformServices.GetSerialPortNames()</c>).
    /// The baud rate is parsed with <see cref="CultureInfo.InvariantCulture"/> so a locale with a
    /// non-period number format can never change which baud strings are accepted (AAP §0.6.5).
    /// </para>
    /// <para>
    /// <b>View concerns delegated.</b> The numeric UDP-port entry (the former <c>nudSendToUDPPort</c>) is
    /// edited through the Avalonia <c>FormNumeric</c> touch keypad (<c>ShowDialog&lt;double?&gt;</c>) by
    /// the view, which writes the chosen value back into <see cref="SendToUdpPort"/>; this view-model
    /// never opens a window. The WinForms <c>mf.TimedMessageBox(...)</c> popups become the
    /// <see cref="RequestTimedMessage"/> event, and <c>Program.Restart()</c> becomes the
    /// <see cref="RequestRestart"/> event so the host performs the cross-platform restart. Nullable
    /// reference types are disabled project-wide, so every event is initialized to a no-op delegate and
    /// no reference type carries a <c>?</c> annotation.
    /// </para>
    /// </remarks>
    public class FormSerialPassViewModel : ViewModel
    {
        // [XPLAT] The comms-hub facade (replaces the WinForms FormLoop god-object). Owns the shared radio
        // serial port (via Ntrip), the routing flags, and the cross-platform port-name enumeration (via
        // Serial). Never null after construction (the constructor guards the argument).
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Concrete RelayCommand references for the open/close pair so their CanExecute can be
        // re-queried when IsRadioPortOpen changes (reproducing the WinForms button Enabled toggling). The
        // public surface exposes them as ICommand (sibling-VM convention).
        private readonly RelayCommand _openSerialCommand;
        private readonly RelayCommand _closeSerialCommand;

        private bool _isPassOn;
        private bool _sendToSerial;
        private bool _sendToUdp;
        private int _sendToUdpPort;
        private string _selectedPort;
        private string _selectedBaud;
        private bool _isRadioPortOpen;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormSerialPassViewModel"/> class, seeding every
        /// bound field from persisted settings exactly as the WinForms <c>FormSerialPass_Load</c> handler
        /// did, populating the radio-port list from the cross-platform port enumerator, and reflecting the
        /// shared radio port's current open/closed state.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub facade. It supplies the shared radio serial port (through its
        /// <c>Ntrip</c> service), the NTRIP/Radio/routing flags, and the cross-platform serial
        /// port-name enumeration (through its <c>Serial</c> service). Required.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="comm"/> is <see langword="null"/>; the dialog cannot operate the
        /// shared radio port or persist routing choices without it.
        /// </exception>
        public FormSerialPassViewModel(CommCoordinatorService comm)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;

            // Seed the editable fields directly (mirrors FormSerialPass_Load, which set the checkboxes
            // programmatically and therefore did NOT fire the mutual-exclusion click handlers). Assigning
            // the backing fields here — rather than the property setters — preserves that exactly: both
            // send targets can legitimately load as enabled (their persisted defaults are both true),
            // and the SendToSerial/SendToUdp mutual exclusion only engages on a subsequent operator edit.
            _isPassOn = Settings.Default.setPass_isOn;
            _sendToSerial = Settings.Default.setNTRIP_sendToSerial;
            _sendToUdp = Settings.Default.setNTRIP_sendToUDP;
            _sendToUdpPort = Settings.Default.setNTRIP_sendToUDPPort;

            // [XPLAT] Radio-port list via the abstracted enumeration seam (COMx on Windows;
            // /dev/ttyUSB*, /dev/ttyACM* on Linux; /dev/cu.* on macOS) instead of the Windows-leaning
            // SerialPort.GetPortNames() the WinForms Load handler used.
            AvailablePorts = new ObservableCollection<string>(_comm.Serial.GetAvailablePortNames());

            // [XPLAT] Parity for the WinForms cboxBaud fixed item list. Kept verbatim so the dialog offers
            // the identical baud choices; the view binds this to the baud combo's items.
            AvailableBauds = new ObservableCollection<string>(new[]
            {
                "4800", "9600", "19200", "38400", "57600", "115200", "128000", "256000"
            });

            // Saved radio port name + baud (the former cboxRadioPort.Text / cboxBaud.Text seeds).
            _selectedPort = Settings.Default.setPort_portNameRadio;
            _selectedBaud = Settings.Default.setPort_baudRateRadio;

            // Build commands. The open/close pair carries CanExecute predicates that mirror the WinForms
            // btnOpenSerial/btnCloseSerial Enabled toggling; they are re-queried from the IsRadioPortOpen
            // setter. Commands are created before RefreshRadioPortState so that the first state refresh can
            // safely re-query them.
            _openSerialCommand = new RelayCommand(OnOpenSerial, () => !IsRadioPortOpen);
            _closeSerialCommand = new RelayCommand(OnCloseSerial, () => IsRadioPortOpen);
            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
            RescanCommand = new RelayCommand(OnRescan);

            // Reflect the shared radio port's actual open/closed state (the former Load handler enabled or
            // disabled the open/close buttons and the port/baud combos from mf.spRadio.IsOpen).
            RefreshRadioPortState();
        }

        /// <summary>
        /// Gets or sets a value indicating whether serial pass-through is enabled (the former
        /// <c>cboxSerialPassOn</c>). Toggling it has no immediate side effect, matching the WinForms
        /// dialog; the mutual exclusion with NTRIP and Radio is applied only when the operator accepts the
        /// dialog through <see cref="OkCommand"/>.
        /// </summary>
        public bool IsPassOn
        {
            get { return _isPassOn; }
            set
            {
                if (value != _isPassOn)
                {
                    _isPassOn = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the NTRIP stream is sent to the GPS receiver over the
        /// serial RTCM port (the former <c>cboxToSerial</c>). Mutually exclusive with
        /// <see cref="SendToUdp"/>: enabling this disables that, reproducing the WinForms
        /// <c>cboxToSerial</c> click handler.
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

                    // Mutual exclusion: checking "to serial" unchecks "to UDP". The SendToUdp setter's own
                    // value-changed guard stops the cascade (it only re-enters when actually changing), so
                    // there is no infinite recursion.
                    if (value)
                    {
                        SendToUdp = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the NTRIP stream is sent to AgOpenGPS over UDP (the
        /// former <c>cboxToUDP</c>). Mutually exclusive with <see cref="SendToSerial"/>: enabling this
        /// disables that, reproducing the WinForms <c>cboxToUDP</c> click handler.
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

                    // Mutual exclusion: checking "to UDP" unchecks "to serial".
                    if (value)
                    {
                        SendToSerial = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the UDP port the NTRIP stream is forwarded to (the former
        /// <c>nudSendToUDPPort.Value</c>, persisted as <c>setNTRIP_sendToUDPPort</c>). The value is edited
        /// by the view through the Avalonia <c>FormNumeric</c> touch keypad (<c>ShowDialog&lt;double?&gt;</c>),
        /// which casts the chosen number to <see cref="int"/> and assigns it here; this view-model never
        /// opens a window itself.
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
        /// Gets the list of serial port names offered for the radio (the former <c>cboxRadioPort</c>
        /// items). Populated from the cross-platform enumeration seam and refreshed by
        /// <see cref="RescanCommand"/>.
        /// </summary>
        public ObservableCollection<string> AvailablePorts { get; }

        /// <summary>
        /// Gets the list of baud rates offered for the radio (the former fixed <c>cboxBaud</c> items).
        /// </summary>
        public ObservableCollection<string> AvailableBauds { get; }

        /// <summary>
        /// Gets or sets the selected radio serial port name (the former <c>cboxRadioPort.Text</c>),
        /// persisted as <c>setPort_portNameRadio</c> when the dialog is accepted.
        /// </summary>
        public string SelectedPort
        {
            get { return _selectedPort; }
            set
            {
                if (value != _selectedPort)
                {
                    _selectedPort = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected radio baud rate as a string (the former <c>cboxBaud.Text</c>),
        /// persisted as <c>setPort_baudRateRadio</c> when the dialog is accepted and parsed with
        /// <see cref="CultureInfo.InvariantCulture"/> when the port is opened.
        /// </summary>
        public string SelectedBaud
        {
            get { return _selectedBaud; }
            set
            {
                if (value != _selectedBaud)
                {
                    _selectedBaud = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the shared radio serial port is currently open (reflects
        /// <c>NtripService.spRadio.IsOpen</c>). The view binds button enablement and the port/baud combo
        /// enablement to this — open the port when it is closed, close it when it is open — reproducing the
        /// WinForms <c>btnOpenSerial</c>/<c>btnCloseSerial</c> Enabled toggling. Updated only by the
        /// view-model, so the setter is private.
        /// </summary>
        public bool IsRadioPortOpen
        {
            get { return _isRadioPortOpen; }
            private set
            {
                if (value != _isRadioPortOpen)
                {
                    _isRadioPortOpen = value;
                    NotifyPropertyChanged();

                    // Re-query the open/close commands so their bound buttons enable/disable in step with
                    // the port state (null-conditional purely defensive — both are assigned in the ctor
                    // before this setter can run).
                    _openSerialCommand?.RaiseCanExecuteChanged();
                    _closeSerialCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that accepts the dialog (the former <c>btnSerialOK</c>): it applies the
        /// NTRIP/Radio mutual exclusion, persists every setting, then asks the host to restart and close.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command that opens the shared radio serial port with the selected port and baud (the
        /// former <c>btnOpenSerial</c>). It can execute only while the port is closed.
        /// </summary>
        public ICommand OpenSerialCommand
        {
            get { return _openSerialCommand; }
        }

        /// <summary>
        /// Gets the command that closes the shared radio serial port (the former <c>btnCloseSerial</c>).
        /// It can execute only while the port is open.
        /// </summary>
        public ICommand CloseSerialCommand
        {
            get { return _closeSerialCommand; }
        }

        /// <summary>
        /// Gets the command that re-enumerates the available serial ports into <see cref="AvailablePorts"/>
        /// (the former <c>btnRescan</c>).
        /// </summary>
        public ICommand RescanCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without saving (the former <c>btnSerialCancel</c>,
        /// which carried <c>DialogResult.Cancel</c>). It simply raises <see cref="RequestClose"/>.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Raised to ask the view to show a transient, self-dismissing notification — the MVVM replacement
        /// for the WinForms <c>FormLoop.TimedMessageBox(milliseconds, title, message)</c>. The arguments
        /// are the display duration in milliseconds, the title, and the message body. Initialized to a
        /// no-op delegate so it is always safe to invoke (nullable reference types are disabled).
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// [XPLAT] Raised to ask the host to perform a cross-platform program restart — the replacement for
        /// the WinForms <c>Program.Restart()</c>. The host owns the restart mechanism (it differs per OS),
        /// so the view-model only signals intent. Initialized to a no-op delegate so it is always safe to
        /// invoke.
        /// </summary>
        public event Action RequestRestart = delegate { };

        /// <summary>
        /// Raised to ask the host to close the dialog — the replacement for the WinForms <c>Close()</c>.
        /// Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Accepts the dialog, reproducing the WinForms <c>btnSerialOK_Click</c> handler one-for-one:
        /// persist the pass-through flag; when pass-through is on, turn NTRIP and Radio off in both the
        /// settings and the live comm flags (the three-way mutual exclusion); persist the UDP port and the
        /// two send targets and mirror them onto the live comm flags; persist the radio port and baud;
        /// save; then ask the host to restart and close.
        /// </summary>
        private void OnOk()
        {
            Settings.Default.setPass_isOn = IsPassOn;

            // [XPLAT] Mutual exclusion (frozen): enabling pass-through forces NTRIP and Radio off, both in
            // the persisted settings AND on the live NtripService flags the WinForms code reached through
            // mf. This guarantees the correction stream is never double-routed.
            if (IsPassOn)
            {
                Settings.Default.setNTRIP_isOn = false;
                _comm.Ntrip.isNTRIP_RequiredOn = false;
                Settings.Default.setRadio_isOn = false;
                _comm.Ntrip.isRadio_RequiredOn = false;
            }

            Settings.Default.setNTRIP_sendToUDPPort = SendToUdpPort;

            Settings.Default.setNTRIP_sendToSerial = SendToSerial;
            Settings.Default.setNTRIP_sendToUDP = SendToUdp;

            // [XPLAT] mf.isSendToSerial / mf.isSendToUDP -> the live NtripService routing flags.
            _comm.Ntrip.isSendToSerial = SendToSerial;
            _comm.Ntrip.isSendToUDP = SendToUdp;

            Settings.Default.setPort_portNameRadio = SelectedPort;
            Settings.Default.setPort_baudRateRadio = SelectedBaud;

            Settings.Default.Save();

            // [XPLAT] Program.Restart() -> host performs the cross-platform restart; then close.
            RequestRestart();
            RequestClose();
        }

        /// <summary>
        /// Opens the shared radio serial port, reproducing the WinForms <c>btnOpenSerial_Click</c> handler.
        /// Any previously open radio port is closed and disposed first, then a fresh
        /// <see cref="SerialPort"/> is created on the selected port at the selected baud and opened. The
        /// baud is parsed with <see cref="CultureInfo.InvariantCulture"/>. The port construction, baud
        /// parse, and open are all guarded so any failure (invalid baud, missing port, or open error)
        /// surfaces the original timed message rather than faulting; the actual port state is then
        /// reflected back into <see cref="IsRadioPortOpen"/>.
        /// </summary>
        private void OnOpenSerial()
        {
            // Close and release any port that is currently open before re-opening (WinForms parity).
            if (_comm.Ntrip.spRadio != null && _comm.Ntrip.spRadio.IsOpen)
            {
                _comm.Ntrip.spRadio.Close();
                _comm.Ntrip.spRadio.Dispose();
                _comm.Ntrip.spRadio = null;
            }

            try
            {
                // [XPLAT] Serial framing FROZEN: System.IO.Ports.SerialPort kept verbatim. Baud parsed with
                // InvariantCulture so the accepted baud strings never change with the OS locale.
                int baud = int.Parse(SelectedBaud, CultureInfo.InvariantCulture);
                _comm.Ntrip.spRadio = new SerialPort(SelectedPort, baud);
                _comm.Ntrip.spRadio.Open();
            }
            catch (Exception ex)
            {
                // [XPLAT] mf.TimedMessageBox(3000, "Error opening port", ex.Message) -> RequestTimedMessage.
                // The original title and the exception message are preserved so the operator still sees why
                // the port could not be opened.
                RequestTimedMessage(3000, "Error opening port", ex.Message);
            }

            RefreshRadioPortState();
        }

        /// <summary>
        /// Closes the shared radio serial port, reproducing the WinForms <c>btnCloseSerial_Click</c>
        /// handler: when the port is open it is closed, disposed and released, then the actual state is
        /// reflected back into <see cref="IsRadioPortOpen"/>.
        /// </summary>
        private void OnCloseSerial()
        {
            if (_comm.Ntrip.spRadio != null && _comm.Ntrip.spRadio.IsOpen)
            {
                _comm.Ntrip.spRadio.Close();
                _comm.Ntrip.spRadio.Dispose();
                _comm.Ntrip.spRadio = null;
            }

            RefreshRadioPortState();
        }

        /// <summary>
        /// Re-enumerates the available serial ports into <see cref="AvailablePorts"/>, reproducing the
        /// WinForms <c>btnRescan_Click</c> handler. The list is cleared and repopulated from the
        /// cross-platform port-name enumeration seam so the bound combo updates in place.
        /// </summary>
        private void OnRescan()
        {
            AvailablePorts.Clear();
            foreach (string portName in _comm.Serial.GetAvailablePortNames())
            {
                AvailablePorts.Add(portName);
            }
        }

        /// <summary>
        /// Dismisses the dialog without saving (the former <c>btnSerialCancel</c> / <c>DialogResult.Cancel</c>)
        /// by raising <see cref="RequestClose"/>.
        /// </summary>
        private void OnCancel()
        {
            RequestClose();
        }

        /// <summary>
        /// Reflects the shared radio serial port's actual open/closed state into
        /// <see cref="IsRadioPortOpen"/>. Mirrors the WinForms Load / open / close handlers, which enabled
        /// or disabled the buttons and the port/baud combos from <c>mf.spRadio.IsOpen</c>.
        /// </summary>
        private void RefreshRadioPortState()
        {
            IsRadioPortOpen = _comm.Ntrip.spRadio != null && _comm.Ntrip.spRadio.IsOpen;
        }
    }
}
