// [XPLAT] migrated from net48/WinForms FormUDP.cs + FormUDP.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormUDP.axaml</c> — the AgIO <b>Ethernet / UDP module-scan</b>
    /// dialog — reimplementing the deleted WinForms <c>FormUDP : Form</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormUDP.cs</c> + <c>FormUDP.Designer.cs</c>) as an Avalonia
    /// <see cref="Window"/> bound to a <see cref="FormUDPViewModel"/>. The dialog discovers AgOpenGPS
    /// hardware modules (Steer / Machine / GPS / IMU) on the local network and pushes a new IP subnet to
    /// them, using the frozen PGN 202 (scan) and PGN 201 (set-subnet) loopback frames owned by the
    /// view-model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal (the heart of this migration).</b> The WinForms original took a
    /// <c>Form callingForm</c> back-reference (<c>mf</c>) and reached through it for every piece of shared
    /// state and for its dialog actions. That back-reference is gone: this view is constructed from the
    /// <see cref="CommCoordinatorService"/> (the AAP §0.3.2 comms-hub facade that owns the UDP transport,
    /// the scan-reply state, and the live per-module status), which it forwards to the
    /// <see cref="FormUDPViewModel"/> by constructor injection. This satisfies the opener contract
    /// <c>await new FormUDP(_vm.Comm).ShowDialog(this)</c> used by <c>MainWindow.axaml.cs</c>, where the
    /// comm coordinator is surfaced by <c>MainWindowViewModel.Comm</c>.
    /// </para>
    /// <para>
    /// <b>Intent-event bridging (parity with the WinForms click handlers).</b> A view-model must not open a
    /// window, pop a dialog, restart the process, or close itself, so <see cref="FormUDPViewModel"/>
    /// surfaces those concerns as events this code-behind bridges to the window-level actions the WinForms
    /// form performed inline:
    /// <list type="bullet">
    ///   <item><description><see cref="FormUDPViewModel.RequestOpenMonitor"/> → shows the migrated Avalonia
    ///   <see cref="FormUDPMonitor"/> non-modally with this window as owner, replacing the WinForms
    ///   <c>btnSerialMonitor_Click</c> → <c>mf.ShowUDPMonitor()</c>. The same injected
    ///   <see cref="CommCoordinatorService"/> is handed to the monitor so it mirrors the live loopback
    ///   traffic.</description></item>
    ///   <item><description><see cref="FormUDPViewModel.RequestConfirm"/> (carrying the confirmation
    ///   message) → shows the migrated Avalonia <see cref="FormYes"/> acknowledge prompt
    ///   (<c>showCancel: false</c>), replacing the WinForms <c>mf.YesMessageBox(...)</c> shown before the
    ///   UDP-off restart.</description></item>
    ///   <item><description><see cref="FormUDPViewModel.RequestRestart"/> → invokes the cross-platform
    ///   <see cref="Program.Restart"/> helper (the AgIO entry-point relaunch that releases the
    ///   single-instance guard, starts a fresh process, and shuts the current one down), replacing the
    ///   WinForms <c>Program.Restart()</c> — never the WinForms-only <c>Application.Restart()</c>.</description></item>
    ///   <item><description><see cref="FormUDPViewModel.RequestClose"/> → <see cref="Window.Close()"/>,
    ///   replacing the WinForms <c>btnSerialCancel_Click</c> <c>Close()</c>.</description></item>
    /// </list>
    /// The view-model initializes these events to no-op delegates, so they are always safe to raise. Because
    /// this window owns its view-model (and nothing else references it), the lambda subscriptions form a
    /// self-contained reference cycle collected with the window — no explicit unsubscribe is required
    /// (mirrors the sibling <c>FormEthernet</c> / <c>FormUDPMonitor</c> convention).
    /// </para>
    /// <para>
    /// <b>On-screen numeric keypad (Phase 2 parity).</b> The WinForms designer wired each of the three
    /// subnet-octet editors' <c>Click</c> event to <c>nudFirstIP_Click</c>, which opened an on-screen
    /// keypad (<c>NumericUpDown.ShowKeypad</c>) and wrote the entered value back into the octet. In the
    /// Avalonia view the octets are rendered as tappable <see cref="Button"/>s bound to the view-model's
    /// <c>EditIpFirst</c>/<c>EditIpSecond</c>/<c>EditIpThird</c> commands (not <c>NumericUpDown</c>
    /// controls), so the keypad is supplied here through the view-model's
    /// <see cref="FormUDPViewModel.RequestKeypad"/> callback rather than the
    /// <c>NumericUpDownExtensions.ShowKeypad</c> helper used by <c>FormEthernet</c>. The callback opens the
    /// migrated Avalonia <see cref="FormNumeric"/> touch keypad (<c>ShowDialog&lt;double?&gt;</c>, returning
    /// the entered value on accept or <see langword="null"/> on cancel); the view-model clamps the result to
    /// the valid <c>0</c>–<c>255</c> octet range and writes it back into the bound <c>IpFirst</c>/
    /// <c>IpSecond</c>/<c>IpThird</c> property — exact behavioural parity with the WinForms keypad.
    /// </para>
    /// <para>
    /// <b>Scan-timer teardown.</b> <see cref="OnClosing"/> calls the view-model's idempotent
    /// <see cref="FormUDPViewModel.Cleanup"/> so the 500&#160;ms display/scan timer is stopped and detached
    /// on every close path (the OK / Cancel buttons and the window chrome alike); otherwise it would keep
    /// firing against a discarded view-model. This is the parity counterpart of the WinForms timer being
    /// disposed with the form.
    /// </para>
    /// <para>
    /// Follows the established AgIO view convention (<c>FormEthernet</c>, <c>FormUDPMonitor</c>,
    /// <c>FormYes</c>, <c>FormNumeric</c>): a parameterless constructor for the XAML loader / previewer plus
    /// an application constructor, and an explicit <see cref="AvaloniaXamlLoader"/>-based
    /// <see cref="InitializeComponent"/>. No WinForms, WPF or <c>System.Drawing</c> types are referenced;
    /// nullable reference types are disabled project-wide, so no reference type is annotated with <c>?</c>
    /// (the <c>double?</c> keypad result is a nullable value type, which is unaffected).
    /// </para>
    /// </remarks>
    public partial class FormUDP : Window
    {
        /// <summary>
        /// [XPLAT] The view-model built from the injected <see cref="CommCoordinatorService"/> in
        /// <see cref="FormUDP(CommCoordinatorService)"/>; it is also assigned as this window's
        /// <see cref="Avalonia.StyledElement.DataContext"/>. Left at its default (<see langword="null"/>) on
        /// the parameterless loader / previewer path — that path never touches it, and
        /// <see cref="OnClosing"/> null-guards it (an explicit check, since nullable reference types are
        /// disabled project-wide).
        /// </summary>
        private readonly FormUDPViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error). Application code constructs
        /// the dialog through <see cref="FormUDP(CommCoordinatorService)"/>; this mirrors the
        /// dual-constructor convention of the sibling AgIO views.
        /// </summary>
        public FormUDP()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the UDP module-scan dialog: builds the backing <see cref="FormUDPViewModel"/> from the
        /// injected comm coordinator, binds it, bridges the view-model's monitor-open / confirm / restart /
        /// close intent events to the corresponding window actions, and supplies the on-screen numeric
        /// keypad used to edit the subnet octets. The cross-platform replacement for the WinForms
        /// <c>FormUDP(Form callingForm)</c> constructor, with the <c>FormLoop</c> god-object parameter
        /// dropped in favour of the explicit <see cref="CommCoordinatorService"/> dependency.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub aggregate whose UDP transport carries the module-scan / set-subnet frames and
        /// the live module status this dialog displays. Forwarded to the <see cref="FormUDPViewModel"/>,
        /// which validates it (the dialog is meaningless without the comm layer it scans), and re-used to
        /// construct the <see cref="FormUDPMonitor"/> so the monitor mirrors the same transport.
        /// </param>
        public FormUDP(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the VM from the injected comm coordinator (the WinForms FormUDp_Load seeding now
            // lives in the VM constructor, which also starts the 500 ms display/scan timer) and bind it. The
            // .axaml declares x:DataType=FormUDPViewModel, so the compiled bindings resolve against exactly
            // this DataContext.
            _vm = new FormUDPViewModel(comm);
            DataContext = _vm;

            // [XPLAT] btnSerialMonitor_Click parity (mf.ShowUDPMonitor): open the migrated FormUDPMonitor
            // non-modally with this window as owner, mirroring the live UDP traffic through the same comm
            // coordinator this dialog uses.
            _vm.RequestOpenMonitor += () => new FormUDPMonitor(comm).Show(this);

            // [XPLAT] mf.YesMessageBox(...) parity: show the FormYes acknowledge prompt (showCancel: false)
            // before the UDP-off restart. async void is the standard signature for an awaited event handler
            // and matches the sibling AgIO views.
            _vm.RequestConfirm += async msg => await new FormYes(msg, false).ShowDialog<bool>(this);

            // [XPLAT] btnUDPOff_Click parity (Program.Restart): the cross-platform AgIO entry-point restart
            // helper, NOT the WinForms-only Application.Restart().
            _vm.RequestRestart += () => Program.Restart();

            // [XPLAT] btnSerialCancel_Click parity: close the dialog.
            _vm.RequestClose += () => Close();

            // [XPLAT] nudFirstIP_Click parity (NumericUpDown.ShowKeypad): supply the touch keypad the
            // view-model invokes when an octet button is tapped. The callback opens the migrated FormNumeric
            // (ShowDialog<double?> -> value on accept, null on cancel); the view-model clamps the result to
            // 0..255 and writes it back into IpFirst/IpSecond/IpThird. Assigned (not +=) because RequestKeypad
            // is a single host-supplied Func, defaulted by the VM to a no-op that returns null.
            _vm.RequestKeypad = (min, max, current) => new FormNumeric(min, max, current).ShowDialog<double?>(this);
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        /// <summary>
        /// [XPLAT] Window close handler — the parity counterpart of the WinForms timer being disposed with
        /// the form. Calls the view-model's idempotent <see cref="FormUDPViewModel.Cleanup"/> so the
        /// 500&#160;ms display/scan timer is stopped and detached on <i>every</i> close path (the OK / Cancel
        /// buttons, which raise <see cref="FormUDPViewModel.RequestClose"/>, and the title-bar close alike),
        /// then defers to the base implementation. The <see langword="null"/> check guards the parameterless
        /// loader / previewer construction path, where the view-model was never created (an explicit check
        /// rather than the null-conditional operator, since nullable reference types are disabled
        /// project-wide).
        /// </summary>
        /// <param name="e">The window-closing event data, forwarded unchanged to the base implementation.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_vm != null)
            {
                _vm.Cleanup();
            }

            base.OnClosing(e);
        }
    }
}
