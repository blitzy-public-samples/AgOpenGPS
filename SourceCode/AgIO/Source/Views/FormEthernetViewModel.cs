// [XPLAT] migrated from net48/WinForms FormEthernet.cs + FormEthernet.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.ViewModels;
using AgIO.Services;
using AgIO.Properties;

namespace AgIO.Views
{
    /// <summary>
    /// MVVM view-model backing <c>FormEthernet.axaml</c>, the AgIO <b>Ethernet</b> configuration
    /// dialog. It sets the four bytes of the UDP <em>loopback</em> address that AgIO uses to reach
    /// AgOpenGPS over the local loopback fabric (the dotted-quad assembled at port 15555), plus the
    /// master "UDP on" flag. It replaces the WinForms <c>FormEthernet</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormEthernet.cs</c> + <c>FormEthernet.designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] <b>God-object removal.</b> The WinForms original held a <c>FormLoop mf</c> back-reference
    /// and called <c>mf.YesMessageBox(...)</c> to confirm before restarting. That back-reference is gone:
    /// the loopback subnet bytes belong to the UDP transport layer, so this view-model takes the
    /// <see cref="CommCoordinatorService"/> (whose <see cref="CommCoordinatorService.Udp"/> transport owns
    /// those bytes) by constructor injection instead of reaching back through the form. The transport reads
    /// the bytes from <see cref="Settings.Default"/> into its (read-only) AgOpenGPS loopback endpoint at
    /// construction time; because that endpoint is fixed for the lifetime of the transport, the values
    /// saved here are adopted when the process is rebuilt by the cross-platform restart raised through
    /// <see cref="RequestRestart"/> — exactly mirroring the WinForms <c>Program.Restart()</c> contract.
    /// </para>
    /// <para>
    /// <b>Behavior parity.</b> Construction seeds <see cref="IsUdpOn"/> from
    /// <c>Settings.Default.setUDP_isOn</c> and <see cref="LoopOne"/>–<see cref="LoopFour"/> from the
    /// <c>eth_loopOne</c>–<c>eth_loopFour</c> byte keys, exactly as <c>FormUDp_Load</c> seeded the check box
    /// and the four <c>NumericUpDown</c> controls. <see cref="SaveCommand"/> (the former <c>btnSerialCancel</c>
    /// OK button) writes each byte back with the same explicit <c>(byte)</c> cast the original used on
    /// <c>nud.Value</c>, writes the flag, persists with <c>Settings.Default.Save()</c>, then raises
    /// <see cref="RequestConfirm"/> (replacing <c>mf.YesMessageBox</c> / <c>FormYes</c>),
    /// <see cref="RequestRestart"/> (replacing <c>Program.Restart()</c>), and <see cref="RequestClose"/>
    /// (replacing <c>Close()</c>), in that order. <see cref="CancelCommand"/> dismisses the dialog without
    /// persisting. The on-screen numeric keypad (the former <c>NumericUpDown.ShowKeypad(this)</c> →
    /// Avalonia <c>FormNumeric</c>) is wired in the view's code-behind and writes straight to the
    /// <see cref="LoopOne"/>–<see cref="LoopFour"/> properties, so it needs no view-model surface here.
    /// </para>
    /// <para>
    /// The settings keys (<c>eth_loopOne</c>, <c>eth_loopTwo</c>, <c>eth_loopThree</c>, <c>eth_loopFour</c>,
    /// <c>setUDP_isOn</c>) and the <c>(byte)</c> cast are preserved verbatim; these are configuration bytes
    /// consumed by the UDP service and do not alter any frozen PGN framing.
    /// </para>
    /// </remarks>
    public class FormEthernetViewModel : ViewModel
    {
        // [XPLAT] The comms-hub aggregate that owns the UDP transport (_comm.Udp) which consumes the
        // loopback subnet bytes edited here. Injected to replace the WinForms `FormLoop mf` god-object:
        // rather than reaching back through the form, this view-model persists the bytes to
        // Settings.Default and the transport adopts them when it is rebuilt on the restart raised by
        // SaveCommand. Held for the lifetime of the dialog as the explicit, fail-fast dependency that
        // ties this configuration surface to the UDP layer it configures.
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Backing fields seeded from Settings.Default in the constructor and flushed back by
        // OnSave. The Cancel path leaves Settings.Default untouched. The loop bytes are held as int so
        // they bind cleanly to the numeric editors / on-screen keypad; the (byte) narrowing happens only
        // on save, exactly as the WinForms original cast nud.Value to byte.
        private bool _isUdpOn;
        private int _loopOne;
        private int _loopTwo;
        private int _loopThree;
        private int _loopFour;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormEthernetViewModel"/> class, capturing the
        /// injected <see cref="CommCoordinatorService"/>, seeding the editable state from the persisted
        /// Ethernet settings, and wiring the Save/Cancel commands. Mirrors the WinForms constructor +
        /// <c>FormUDp_Load</c>, which seeded the same five settings keys into its controls.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub aggregate whose UDP transport consumes the loopback subnet bytes configured
        /// by this dialog. Required; the dialog is meaningless without the comm layer it configures.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="comm"/> is <c>null</c>.</exception>
        public FormEthernetViewModel(CommCoordinatorService comm)
        {
            // [XPLAT] Fail fast on a missing dependency (matches the RelayCommand argument-validation
            // convention used elsewhere in AgOpenGPS.Core.ViewModels).
            _comm = comm ?? throw new ArgumentNullException(nameof(comm));

            // [XPLAT] Parity: seed the editable state from the persisted settings, exactly as FormUDp_Load
            // set cboxIsUDPOn.Checked and the four NumericUpDown values from these same keys. The byte
            // settings widen implicitly to the int backing fields (no parse, no culture dependency).
            _isUdpOn = Settings.Default.setUDP_isOn;
            _loopOne = Settings.Default.eth_loopOne;
            _loopTwo = Settings.Default.eth_loopTwo;
            _loopThree = Settings.Default.eth_loopThree;
            _loopFour = Settings.Default.eth_loopFour;

            // [XPLAT] btnSerialCancel (OK) -> SaveCommand (persist + confirm + restart + close);
            // an explicit Cancel path dismisses without persisting.
            SaveCommand = new RelayCommand(OnSave);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets a value indicating whether UDP networking is enabled. Two-way bound to the former
        /// <c>cboxIsUDPOn</c> check box and persisted to <c>Settings.Default.setUDP_isOn</c> on save. The
        /// equality guard suppresses redundant change notifications.
        /// </summary>
        public bool IsUdpOn
        {
            get => _isUdpOn;
            set
            {
                if (_isUdpOn != value)
                {
                    _isUdpOn = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the first octet of the UDP loopback address (the former <c>nudFirstIP</c>). Two-way
        /// bound to a numeric editor / the on-screen keypad and persisted to
        /// <c>Settings.Default.eth_loopOne</c> as a <see cref="byte"/> on save. The value is defensively
        /// clamped to the 0–255 byte range so the save-time <c>(byte)</c> cast is always lossless.
        /// </summary>
        public int LoopOne
        {
            get => _loopOne;
            set
            {
                int clamped = ClampToByteRange(value);
                if (_loopOne != clamped)
                {
                    _loopOne = clamped;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the second octet of the UDP loopback address (the former <c>nudSecndIP</c>). Two-way
        /// bound to a numeric editor / the on-screen keypad and persisted to
        /// <c>Settings.Default.eth_loopTwo</c> as a <see cref="byte"/> on save. The value is defensively
        /// clamped to the 0–255 byte range so the save-time <c>(byte)</c> cast is always lossless.
        /// </summary>
        public int LoopTwo
        {
            get => _loopTwo;
            set
            {
                int clamped = ClampToByteRange(value);
                if (_loopTwo != clamped)
                {
                    _loopTwo = clamped;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the third octet of the UDP loopback address (the former <c>nudThirdIP</c>). Two-way
        /// bound to a numeric editor / the on-screen keypad and persisted to
        /// <c>Settings.Default.eth_loopThree</c> as a <see cref="byte"/> on save. The value is defensively
        /// clamped to the 0–255 byte range so the save-time <c>(byte)</c> cast is always lossless.
        /// </summary>
        public int LoopThree
        {
            get => _loopThree;
            set
            {
                int clamped = ClampToByteRange(value);
                if (_loopThree != clamped)
                {
                    _loopThree = clamped;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the fourth octet of the UDP loopback address (the former <c>nudFourthIP</c>). Two-way
        /// bound to a numeric editor / the on-screen keypad and persisted to
        /// <c>Settings.Default.eth_loopFour</c> as a <see cref="byte"/> on save. The value is defensively
        /// clamped to the 0–255 byte range so the save-time <c>(byte)</c> cast is always lossless.
        /// </summary>
        public int LoopFour
        {
            get => _loopFour;
            set
            {
                int clamped = ClampToByteRange(value);
                if (_loopFour != clamped)
                {
                    _loopFour = clamped;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that persists the loopback bytes and the UDP-on flag to
        /// <see cref="Settings.Default"/>, then requests confirmation, a cross-platform restart, and dialog
        /// close. Parity with the WinForms <c>btnSerialCancel_Click</c> handler.
        /// </summary>
        public RelayCommand SaveCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without persisting any change by raising
        /// <see cref="RequestClose"/>.
        /// </summary>
        public RelayCommand CancelCommand { get; }

        /// <summary>
        /// Raised when the operator confirms the save and must be informed that AgIO will restart. The
        /// string payload is the confirmation message. The hosting Avalonia view subscribes and shows the
        /// <c>FormYes</c> confirmation dialog, replacing the WinForms <c>mf.YesMessageBox(...)</c> call.
        /// Initialized to a no-op delegate so it is always safe to raise without a null check (nullable
        /// reference types are disabled project-wide).
        /// </summary>
        public event Action<string> RequestConfirm = delegate { };

        /// <summary>
        /// [XPLAT] Raised to request a cross-platform application restart so the UDP transport rebinds with
        /// the freshly-saved loopback subnet bytes. Replaces the WinForms <c>Program.Restart()</c> call;
        /// the host performs the actual restart. Initialized to a no-op delegate so it is always safe to
        /// raise.
        /// </summary>
        public event Action RequestRestart = delegate { };

        /// <summary>
        /// Raised when the dialog should close. The hosting Avalonia <c>Window</c> subscribes and closes
        /// itself, replacing the WinForms <c>Close()</c>. Initialized to a no-op delegate so it is always
        /// safe to raise without a null check.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Persists the four loopback octets (with the original explicit <c>(byte)</c> cast) and the UDP-on
        /// flag to <see cref="Settings.Default"/>, saves, then requests confirmation, restart, and close —
        /// reproducing the WinForms <c>btnSerialCancel_Click</c> save-confirm-restart-close sequence.
        /// </summary>
        private void OnSave()
        {
            // [XPLAT] Flush the edited state back to the persisted settings using the EXACT five keys and
            // the same (byte) narrowing the WinForms original applied to nud.Value. The backing fields are
            // already clamped to 0–255 by their setters, so every cast is lossless.
            Settings.Default.eth_loopOne = (byte)LoopOne;
            Settings.Default.eth_loopTwo = (byte)LoopTwo;
            Settings.Default.eth_loopThree = (byte)LoopThree;
            Settings.Default.eth_loopFour = (byte)LoopFour;
            Settings.Default.setUDP_isOn = IsUdpOn;
            Settings.Default.Save();

            // [XPLAT] mf.YesMessageBox(...) -> RequestConfirm (FormYes); Program.Restart() -> RequestRestart
            // (cross-platform restart performed by the host, which rebuilds _comm.Udp with the saved bytes);
            // Close() -> RequestClose. Order preserved from the WinForms handler.
            RequestConfirm("AgIO will Restart to Enable UDP Networking Features");
            RequestRestart();
            RequestClose();
        }

        /// <summary>
        /// Requests the dialog close without persisting any change, leaving <see cref="Settings.Default"/>
        /// untouched.
        /// </summary>
        private void OnCancel()
        {
            RequestClose();
        }

        /// <summary>
        /// Clamps an octet value to the inclusive 0–255 <see cref="byte"/> range so that the save-time
        /// <c>(byte)</c> cast can never wrap. The bound numeric editor and on-screen keypad already enforce
        /// 0–255 (the WinForms <c>NumericUpDown.Maximum</c> was 255); this is defensive depth.
        /// </summary>
        /// <param name="value">The candidate octet value.</param>
        /// <returns>The value constrained to the 0–255 range.</returns>
        private static int ClampToByteRange(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }
    }
}
