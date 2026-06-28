// [XPLAT] migrated from net48/WinForms FormSerialPass.cs + FormSerialPass.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgIO.Controls;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormSerialPass.axaml</c> — the AgIO <b>Serial Pass-Through</b>
    /// configuration dialog — reimplementing the deleted WinForms <c>FormSerialPass : Form</c>
    /// (<c>Forms/FormSerialPass.cs</c> + <c>Forms/FormSerialPass.designer.cs</c>) as an Avalonia
    /// <see cref="Window"/> bound to a <see cref="FormSerialPassViewModel"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin orchestration only.</b> All behaviour — the NTRIP/Radio three-way mutual exclusion, the
    /// To-Serial/To-UDP mutual exclusion, culture-safe baud parsing, the shared-radio-port open/close,
    /// settings persistence and the cross-platform restart — lives in the bound
    /// <see cref="FormSerialPassViewModel"/> (set as the <see cref="Avalonia.StyledElement.DataContext"/>).
    /// The paired markup drives every affordance through compiled command bindings
    /// (<c>x:DataType="vm:FormSerialPassViewModel"</c>), so this code-behind declares no <c>Click</c>
    /// handlers. Its only jobs are: construct and bind the view-model from the injected comm coordinator,
    /// bridge the view-model's window-level intent events to the actual window actions, and wire the
    /// touch numeric keypad onto the UDP-port editor.
    /// </para>
    /// <para>
    /// <b>God-object removal.</b> The WinForms dialog took a direct <c>FormLoop mf</c> back-reference and
    /// reached through it for the shared radio serial port and the routing flags. That coupling is gone:
    /// the dialog is constructed from an injected <see cref="CommCoordinatorService"/>
    /// (<see cref="FormSerialPass(CommCoordinatorService)"/>), which it forwards to the view-model — the
    /// migrated peer that genuinely owns those members. The opener (<c>MainWindow.axaml.cs</c>) uses
    /// <c>await new FormSerialPass(_vm.Comm).ShowDialog(this)</c>.
    /// </para>
    /// <para>
    /// <b>Window-level intent events.</b> A view-model must not open or close a window or pop a toast, so
    /// <see cref="FormSerialPassViewModel"/> raises three intent events that this window services:
    /// <list type="bullet">
    ///   <item><description><see cref="FormSerialPassViewModel.RequestTimedMessage"/> (duration, title,
    ///   message) → shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast non-modally over this
    ///   dialog (the former <c>mf.TimedMessageBox(...)</c>), so the dialog stays open for correction.</description></item>
    ///   <item><description><see cref="FormSerialPassViewModel.RequestRestart"/> → performs the
    ///   cross-platform program restart through the AgIO <see cref="Program.Restart"/> helper (the former
    ///   <c>Program.Restart()</c>); the helper relaunches the executable and shuts the lifetime down on every OS.</description></item>
    ///   <item><description><see cref="FormSerialPassViewModel.RequestClose"/> → closes this window (the
    ///   former <c>Close()</c>).</description></item>
    /// </list>
    /// The bridges are named methods (not lambdas) so they can be detached in <see cref="OnClosed"/>,
    /// guaranteeing the view-model never keeps the closed window alive.
    /// </para>
    /// <para>
    /// <b>On-screen numeric keypad (Phase 2 parity).</b> The UDP-port editor (<c>nudSendToUDPPort</c>) is
    /// an Avalonia <see cref="NumericUpDown"/> precisely so the converted
    /// <see cref="AgIO.Controls.NumericUpDownExtensions.ShowKeypad(NumericUpDown, Window)"/> touch helper
    /// can read its <c>Minimum</c>/<c>Maximum</c>/<c>Value</c> and write the chosen value back. Tapping the
    /// editor opens the modal <see cref="FormNumeric"/> keypad; on accept the helper assigns
    /// <see cref="NumericUpDown.Value"/>, which flows through the markup's two-way binding into
    /// <see cref="FormSerialPassViewModel.SendToUdpPort"/>. The control is resolved by its <c>x:Name</c>
    /// (this view loads its XAML through the explicit <see cref="AvaloniaXamlLoader"/> call, so the
    /// name-generator's field-assigning path never runs) and the tap is captured with a tunnelling
    /// <see cref="InputElement.PointerPressed"/> handler registered with <c>handledEventsToo</c>, because
    /// the editor's inner text box swallows the pointer press otherwise — the same wiring the sibling
    /// GPS <c>FormNumeric</c>/<c>FormInputDialogView</c> views use.
    /// </para>
    /// <para>
    /// Follows the established AgIO view convention (<c>FormYes</c>, <c>FormPGN</c>, <c>FormSource</c>,
    /// <c>FormRadioChannel</c>, <c>FormTimedMessage</c>): a parameterless constructor for the XAML loader /
    /// previewer plus a data-supplying overload, with an explicit <see cref="AvaloniaXamlLoader"/>-based
    /// <see cref="InitializeComponent"/>. No WinForms, WPF or <c>System.Drawing</c> types are referenced;
    /// nullable reference types are disabled project-wide, so no reference type is annotated with <c>?</c>.
    /// </para>
    /// </remarks>
    public partial class FormSerialPass : Window
    {
        /// <summary>
        /// [XPLAT] The view-model built from the comm coordinator passed to
        /// <see cref="FormSerialPass(CommCoordinatorService)"/>. Held so its intent events can be detached
        /// in <see cref="OnClosed"/>. Left <see langword="null"/> on the parameterless (designer / loader)
        /// path; every member that touches it guards against that, so the design-time previewer is safe.
        /// </summary>
        private readonly FormSerialPassViewModel _vm;

        /// <summary>
        /// [XPLAT] Re-entrancy guard for the UDP-port keypad. Set while the modal
        /// <see cref="FormNumeric"/> keypad is open so a rapid second tap on the editor cannot stack a
        /// second keypad on top of the first.
        /// </summary>
        private bool _isKeypadOpen;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error). Application code always
        /// constructs the dialog through <see cref="FormSerialPass(CommCoordinatorService)"/>; this mirrors
        /// the dual-constructor convention of the sibling AgIO views.
        /// </summary>
        public FormSerialPass()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog from the injected comm coordinator: builds the backing
        /// <see cref="FormSerialPassViewModel"/>, assigns it as the
        /// <see cref="Avalonia.StyledElement.DataContext"/>, bridges the view-model's timed-message /
        /// restart / close intent events to the corresponding window actions, and wires the UDP-port touch
        /// keypad. This is the cross-platform replacement for the WinForms
        /// <c>FormSerialPass(Form callingForm)</c> constructor, with the <c>FormLoop</c> god-object
        /// parameter replaced by the genuinely-owning <see cref="CommCoordinatorService"/>.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms-hub facade that owns the shared radio serial port, the NTRIP/Radio/routing flags,
        /// and the cross-platform serial port-name enumeration. Forwarded to the view-model, which guards it
        /// against <see langword="null"/>.
        /// </param>
        public FormSerialPass(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the VM from the injected comm coordinator (the WinForms FormSerialPass_Load
            // seeding now lives in the VM constructor) and bind it. The .axaml declares
            // x:DataType="vm:FormSerialPassViewModel", so the compiled bindings resolve against this DataContext.
            _vm = new FormSerialPassViewModel(comm);
            DataContext = _vm;

            // [XPLAT] The bound OkCommand/CancelCommand/Open/Close/Rescan commands only raise these intent
            // events; the window performs the actual toast / restart / close that the WinForms FormSerialPass
            // did inline. Each event is initialised to a no-op delegate in the VM, so they are never null.
            _vm.RequestTimedMessage += OnRequestTimedMessage;
            _vm.RequestRestart += OnRequestRestart;
            _vm.RequestClose += OnRequestClose;

            // [XPLAT] WinForms tap-to-edit on the UDP-port NumericUpDown -> open the FormNumeric touch keypad.
            WireUdpPortKeypad();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        /// <summary>
        /// [XPLAT] Timed-message bridge: shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast
        /// over this dialog — the cross-platform replacement for the WinForms
        /// <c>mf.TimedMessageBox(milliseconds, title, message)</c>. It is shown non-modally
        /// (<see cref="Window.Show(Window)"/>) so the dialog stays open for the operator to correct input
        /// (e.g. after a failed serial-port open). Duration, title and message are passed through unchanged.
        /// </summary>
        /// <param name="milliseconds">How long the toast stays visible, in milliseconds.</param>
        /// <param name="title">The toast title (e.g. "Error opening port").</param>
        /// <param name="message">The toast message body (e.g. the exception message).</param>
        private void OnRequestTimedMessage(int milliseconds, string title, string message)
        {
            FormTimedMessage toast = new FormTimedMessage(milliseconds, title, message);
            toast.Show(this);
        }

        /// <summary>
        /// [XPLAT] Restart bridge: performs the cross-platform program restart through the AgIO
        /// <see cref="Program.Restart"/> helper — the replacement for the WinForms <c>Program.Restart()</c>.
        /// The helper releases the single-instance guard, relaunches the current executable, and shuts the
        /// classic-desktop lifetime down on every OS, so the dialog needs no OS-specific code of its own.
        /// </summary>
        private void OnRequestRestart()
        {
            Program.Restart();
        }

        /// <summary>
        /// [XPLAT] Close bridge: closes this dialog — the replacement for the WinForms <c>Close()</c> the
        /// OK / Cancel handlers performed inline.
        /// </summary>
        private void OnRequestClose()
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Wires the touch numeric keypad onto the UDP-port editor. Resolves the
        /// <c>nudSendToUDPPort</c> <see cref="NumericUpDown"/> by its <c>x:Name</c> and subscribes
        /// <see cref="OnUdpPortPointerPressed"/> to its <see cref="InputElement.PointerPressed"/>. The
        /// handler is registered with <see cref="RoutingStrategies.Tunnel"/> and <c>handledEventsToo</c>
        /// because the editor's inner text box marks the pointer press handled (for caret placement)
        /// before it would bubble, so a plain subscription would never fire — the same wiring the sibling
        /// GPS <c>FormNumeric</c>/<c>FormInputDialogView</c> views use. The lookup is null-guarded so the
        /// design-time previewer (and any future markup change that renames the control) stays safe; the
        /// editor lives for the lifetime of this window, so the subscription needs no explicit teardown.
        /// </summary>
        private void WireUdpPortKeypad()
        {
            NumericUpDown udpPort = this.FindControl<NumericUpDown>("nudSendToUDPPort");
            if (udpPort != null)
            {
                udpPort.AddHandler(
                    InputElement.PointerPressedEvent,
                    OnUdpPortPointerPressed,
                    RoutingStrategies.Tunnel,
                    handledEventsToo: true);
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform tap-to-edit handler for the UDP-port editor. Opens the modal
        /// <see cref="FormNumeric"/> touch keypad through the converted
        /// <see cref="AgIO.Controls.NumericUpDownExtensions.ShowKeypad(NumericUpDown, Window)"/> helper,
        /// which seeds the keypad with the editor's current value and inclusive bounds and — on accept —
        /// writes the entered value back to <see cref="NumericUpDown.Value"/>. That assignment flows through
        /// the markup's two-way binding into <see cref="FormSerialPassViewModel.SendToUdpPort"/>. The
        /// <see cref="_isKeypadOpen"/> guard prevents a second keypad from stacking on a rapid re-tap.
        /// <c>async void</c> is the standard signature for an awaited routed-event handler (and matches the
        /// sibling views).
        /// </summary>
        /// <param name="sender">The UDP-port <see cref="NumericUpDown"/> the handler was attached to.</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnUdpPortPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isKeypadOpen)
            {
                return;
            }

            if (sender is NumericUpDown udpPort)
            {
                _isKeypadOpen = true;
                try
                {
                    await udpPort.ShowKeypad(this);
                }
                finally
                {
                    _isKeypadOpen = false;
                }
            }
        }

        /// <summary>
        /// [XPLAT] Detaches the view-model intent events when the dialog closes so the view-model never
        /// keeps the closed window alive. The <see cref="InputElement.PointerPressed"/> subscription on the
        /// UDP-port editor is intentionally left in place — that control shares this window's lifetime and
        /// is collected with it.
        /// </summary>
        /// <param name="e">The close-event payload passed to the base implementation.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_vm != null)
            {
                _vm.RequestTimedMessage -= OnRequestTimedMessage;
                _vm.RequestRestart -= OnRequestRestart;
                _vm.RequestClose -= OnRequestClose;
            }

            base.OnClosed(e);
        }
    }
}
