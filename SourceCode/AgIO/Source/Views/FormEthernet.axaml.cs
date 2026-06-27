// [XPLAT] migrated from net48/WinForms FormEthernet.cs + FormEthernet.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AgIO.Controls;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormEthernet.axaml</c> — the AgIO <b>Ethernet</b>
    /// loopback-config dialog — reimplementing the deleted WinForms <c>FormEthernet : Form</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormEthernet.cs</c> + <c>FormEthernet.designer.cs</c>) as an
    /// Avalonia <see cref="Window"/> bound to a <see cref="FormEthernetViewModel"/>. The dialog sets the
    /// four bytes of the UDP loopback address AgIO uses to reach AgOpenGPS over the local loopback fabric,
    /// plus the master "UDP on" flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal (the heart of this migration).</b> The WinForms original took a
    /// <c>Form callingForm</c> back-reference (<c>mf</c>) and reached through it to pop the restart
    /// confirmation (<c>mf.YesMessageBox(...)</c>). That back-reference is gone: the loopback subnet bytes
    /// belong to the UDP transport layer, so this view is constructed from the
    /// <see cref="CommCoordinatorService"/> (whose <see cref="CommCoordinatorService.Udp"/> transport owns
    /// those bytes), which it forwards to the <see cref="FormEthernetViewModel"/> by constructor injection.
    /// This satisfies the opener contract <c>await new FormEthernet(_vm.Comm).ShowDialog(this)</c> used by
    /// <c>MainWindow.axaml.cs</c> (the comm coordinator is surfaced by
    /// <c>MainWindowViewModel.Comm</c>).
    /// </para>
    /// <para>
    /// <b>Intent-event bridging (parity with the WinForms <c>btnSerialCancel_Click</c> handler).</b> A
    /// view-model must not close a window, pop a dialog, or restart the process, so
    /// <see cref="FormEthernetViewModel"/> raises three intent EVENTS that this code-behind bridges to the
    /// window-level actions the WinForms form performed inline:
    /// <list type="bullet">
    ///   <item><description><see cref="FormEthernetViewModel.RequestConfirm"/> (carrying the confirmation
    ///   message) → shows the migrated Avalonia <see cref="FormYes"/> acknowledge prompt
    ///   (<c>showCancel: false</c>), replacing the WinForms <c>mf.YesMessageBox(...)</c>.</description></item>
    ///   <item><description><see cref="FormEthernetViewModel.RequestRestart"/> → invokes the cross-platform
    ///   <see cref="Program.Restart"/> helper (the AgIO entry-point relaunch that releases the
    ///   single-instance guard, starts a fresh process, and shuts the current one down), replacing the
    ///   WinForms <c>Program.Restart()</c>.</description></item>
    ///   <item><description><see cref="FormEthernetViewModel.RequestClose"/> → <see cref="Window.Close()"/>,
    ///   replacing the WinForms <c>Close()</c>.</description></item>
    /// </list>
    /// The view-model raises them in the original order (confirm → restart → close) inside its
    /// <c>SaveCommand</c>; the events are initialized to no-op delegates in the view-model so they are
    /// always safe to raise. Because this window OWNS its view-model (and nothing else references it), the
    /// lambda subscriptions form a self-contained reference cycle that is collected with the window — no
    /// explicit unsubscribe is required (mirrors the sibling <c>FormSource</c> convention).
    /// </para>
    /// <para>
    /// <b>On-screen numeric keypad (Phase 2 parity).</b> The WinForms designer wired every one of the four
    /// octet editors' <c>Click</c> event to <c>nudFirstIP_Click</c>, which called
    /// <c>NumericUpDown.ShowKeypad(this)</c>. To preserve that behaviour, <see cref="WireKeypad"/> attaches
    /// <see cref="OnOctetPointerPressed"/> to the <see cref="InputElement.PointerPressed"/> event (the
    /// codebase-standard Avalonia analogue of the WinForms <c>Click</c>) of those same four editors, found
    /// by their <c>x:Name</c> via <see cref="NameScopeExtensions.FindControl{T}"/>. The handler presents
    /// the migrated Avalonia <see cref="FormNumeric"/> through the converted
    /// <see cref="AgIO.Controls.NumericUpDownExtensions.ShowKeypad(NumericUpDown, Window)"/> helper, which
    /// writes the entered value back into the editor's <see cref="NumericUpDown.Value"/> — flowing through
    /// the markup's two-way binding into the bound <c>LoopOne</c>–<c>LoopFour</c> view-model properties.
    /// The runtime <c>FindControl</c> lookup (rather than a generated typed field) is required because this
    /// view loads its XAML with an explicit <see cref="AvaloniaXamlLoader"/> call, so the name generator's
    /// field-assigning path never runs; each lookup is null-guarded so the design-time previewer stays safe.
    /// </para>
    /// <para>
    /// Follows the established AgIO view convention (<c>FormYes</c>, <c>FormNumeric</c>, <c>FormSource</c>,
    /// <c>FormRadioChannel</c>): a parameterless constructor for the XAML loader / previewer plus an
    /// application constructor, and an explicit <see cref="AvaloniaXamlLoader"/>-based
    /// <see cref="InitializeComponent"/>. No WinForms, WPF or <c>System.Drawing</c> types are referenced;
    /// nullable reference types are disabled project-wide, so no reference type is annotated with <c>?</c>.
    /// </para>
    /// </remarks>
    public partial class FormEthernet : Window
    {
        /// <summary>
        /// [XPLAT] The view-model built from the injected <see cref="CommCoordinatorService"/> in
        /// <see cref="FormEthernet(CommCoordinatorService)"/>; it is also assigned as this window's
        /// <see cref="StyledElement.DataContext"/>. Left at its default on the parameterless
        /// (loader / previewer) path — that path never touches it, and nullable reference types are
        /// disabled project-wide.
        /// </summary>
        private readonly FormEthernetViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error). Application code constructs
        /// the dialog through <see cref="FormEthernet(CommCoordinatorService)"/>; this mirrors the
        /// dual-constructor convention of the sibling AgIO views.
        /// </summary>
        public FormEthernet()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the Ethernet loopback-config dialog: builds the backing
        /// <see cref="FormEthernetViewModel"/> from the injected comm coordinator, binds it, bridges the
        /// view-model's confirm / restart / close intent events to the corresponding window actions, and
        /// wires the on-screen numeric keypad to the four octet editors. The cross-platform replacement for
        /// the WinForms <c>FormEthernet(Form callingForm)</c> constructor, with the <c>FormLoop</c>
        /// god-object parameter dropped in favour of the explicit <see cref="CommCoordinatorService"/>
        /// dependency.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub aggregate whose UDP transport consumes the loopback subnet bytes configured by
        /// this dialog. Forwarded to <see cref="FormEthernetViewModel"/>, which validates it (the dialog is
        /// meaningless without the comm layer it configures).
        /// </param>
        public FormEthernet(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the VM from the injected comm coordinator (the WinForms FormUDp_Load seeding now
            // lives in the VM constructor) and bind it. The .axaml declares x:DataType=FormEthernetViewModel,
            // so the compiled bindings resolve against exactly this DataContext.
            _vm = new FormEthernetViewModel(comm);
            DataContext = _vm;

            // [XPLAT] SaveCommand (bound in the .axaml) only raises these intent events; the window performs
            // the actual confirm / restart / close that the WinForms btnSerialCancel_Click did inline:
            //   mf.YesMessageBox(...) -> FormYes acknowledge prompt (showCancel: false);
            //   Program.Restart()     -> the cross-platform AgIO entry-point restart helper;
            //   Close()               -> Window.Close().
            // The VM owns these events and shares this window's lifetime, so the lambdas need no unsubscribe.
            _vm.RequestConfirm += async msg => await new FormYes(msg, false).ShowDialog<bool>(this);
            _vm.RequestRestart += () => Program.Restart();
            _vm.RequestClose += () => Close();

            // [XPLAT] WinForms nudFirstIP_Click (wired to all four octets) -> PointerPressed -> ShowKeypad.
            WireKeypad();
        }

        /// <summary>
        /// [XPLAT] Loads the compiled XAML for this window. Declared explicitly (rather than relying on a
        /// generated method) to match the convention used by every sibling AgIO Avalonia view.
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// [XPLAT] Attaches the on-screen numeric keypad to the four loopback-octet editors. The WinForms
        /// designer wired the <c>Click</c> event of <c>nudFirstIP</c>, <c>nudSecndIP</c>, <c>nudThirdIP</c>
        /// and <c>nudFourthIP</c> to the single <c>nudFirstIP_Click</c> handler; this attaches the Avalonia
        /// equivalent to the same four editors, resolved by their <c>x:Name</c>.
        /// </summary>
        private void WireKeypad()
        {
            HookKeypad("nudFirstIP");
            HookKeypad("nudSecndIP");
            HookKeypad("nudThirdIP");
            HookKeypad("nudFourthIP");
        }

        /// <summary>
        /// [XPLAT] Resolves an octet editor by its <c>x:Name</c> and, when present, subscribes
        /// <see cref="OnOctetPointerPressed"/> to its <see cref="InputElement.PointerPressed"/> event (the
        /// Avalonia analogue of the WinForms <c>NumericUpDown.Click</c>). The editors live for the lifetime
        /// of this window, so the subscription needs no explicit teardown; the null guard keeps the
        /// design-time previewer safe.
        /// </summary>
        /// <param name="name">The <c>x:Name</c> of the octet editor to wire.</param>
        private void HookKeypad(string name)
        {
            NumericUpDown nud = this.FindControl<NumericUpDown>(name);
            if (nud != null)
            {
                nud.PointerPressed += OnOctetPointerPressed;
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>nudFirstIP_Click</c> handler. Presents the
        /// migrated Avalonia <see cref="FormNumeric"/> touch keypad for the tapped octet editor through the
        /// converted <see cref="AgIO.Controls.NumericUpDownExtensions.ShowKeypad(NumericUpDown, Window)"/>
        /// helper, which opens the modal <see cref="FormNumeric"/> (<c>ShowDialog&lt;double?&gt;</c>), awaits
        /// the result, and writes the accepted value back into <see cref="NumericUpDown.Value"/> — flowing
        /// through the markup's two-way binding into the bound <c>LoopOne</c>–<c>LoopFour</c> property.
        /// <c>async void</c> is the standard signature for an awaited event handler (and matches the sibling
        /// views).
        /// </summary>
        /// <param name="sender">The octet editor that was pressed.</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnOctetPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is NumericUpDown nud)
            {
                await nud.ShowKeypad(this);
            }
        }
    }
}
