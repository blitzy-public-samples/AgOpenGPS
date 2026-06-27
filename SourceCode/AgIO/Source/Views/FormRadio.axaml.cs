// [XPLAT] migrated from net48/WinForms FormRadio.cs + FormRadio.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AgIO.Services;
using AgIO;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Radio Settings" configuration dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormRadio : Form</c>
    /// (<c>SourceCode/AgIO/Source/Forms/FormRadio.cs</c> + <c>FormRadio.Designer.cs</c>). This is the
    /// code-behind half of <c>FormRadio.axaml</c>; together they form a single view bound to a
    /// <see cref="FormRadioViewModel"/> that configures the serial-radio RTK-correction source and manages
    /// the operator's list of radio channels. It follows the convention of the sibling AgIO views
    /// (<c>FormSource</c>, <c>FormRadioChannel</c>, <c>FormISOBUS</c>): a parameterless constructor for the
    /// XAML loader plus an application constructor that builds and binds the view-model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The WinForms dialog took a direct <c>FormLoop mf</c> back-reference and
    /// reached through it for the shared radio serial port, the current position, the NTRIP reconfigure,
    /// the timed-message popup and the on-screen-keyboard flag. That coupling is gone: this view is
    /// constructed from the injected <see cref="CommCoordinatorService"/> — the AgIO comms aggregate the
    /// composition root owns — and builds its <see cref="FormRadioViewModel"/> from the three comm services
    /// that aggregate exposes (<see cref="CommCoordinatorService.Nmea"/>,
    /// <see cref="CommCoordinatorService.Serial"/> and <see cref="CommCoordinatorService.Ntrip"/>). The
    /// opener is <c>await new FormRadio(viewModel.Comm).ShowDialog(this)</c> in <c>MainWindow.axaml.cs</c>,
    /// guarded by <see cref="CommCoordinatorService.CanOpenRadioSettings"/>.
    /// </para>
    /// <para>
    /// <b>Intent events.</b> The markup's buttons bind to the view-model's commands, but a view-model must
    /// not close a window, pop a toast, or open another dialog — those commands only raise intent EVENTS,
    /// which this code-behind bridges to the window-level actions the WinForms <c>FormRadio</c> performed
    /// inline:
    /// <list type="bullet">
    ///   <item><description><see cref="FormRadioViewModel.RequestTimedMessage"/> (duration, title,
    ///   message) → shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog (the
    ///   former <c>mf.TimedMessageBox</c>), shown non-modally so the dialog stays open — preserving the
    ///   WinForms "No channel" / "NTRIP also enabled" popups and the stay-open-on-invalid
    ///   behaviour.</description></item>
    ///   <item><description><see cref="FormRadioViewModel.RequestEditChannel"/> (carrying the channel to
    ///   add or edit) → opens the <see cref="FormRadioChannel"/> editor with
    ///   <c>ShowDialog&lt;CRadioChannel&gt;</c> and feeds the result back through
    ///   <see cref="FormRadioViewModel.ApplyEditedChannel"/>, reproducing the WinForms
    ///   <c>btnAddChannel_Click</c> / <c>btnEditChannel_Click</c> editor round-trip (a cancelled editor
    ///   yields <see langword="null"/>, which the guarded apply below skips).</description></item>
    ///   <item><description><see cref="FormRadioViewModel.RequestClose"/> → <c>Close()</c>, so the host's
    ///   <c>ShowDialog</c> resolves (the WinForms <c>Close()</c> after a successful OK, and the
    ///   <c>DialogResult.Cancel</c> path).</description></item>
    /// </list>
    /// The window constructs and solely owns the view-model (it is both the <c>_vm</c> field and the
    /// window's <c>DataContext</c>); the two share a lifetime and are collected together when the dialog
    /// closes, so the lambda subscriptions need no explicit unsubscribe — matching the sibling
    /// <c>FormSource</c> / <c>FormISOBUS</c> convention.
    /// </para>
    /// <para>
    /// <b>On-screen keyboard (Phase 2 parity).</b> The migrated <c>FormRadio.axaml</c> exposes no free-text
    /// <c>TextBox</c>: the port and baud selectors are non-editable drop-down <c>ComboBox</c>es and the
    /// WinForms raw command/response console (<c>tbCommand</c>/<c>tbResponse</c> — the only controls the
    /// original wired to <c>tbox_Click</c> → <c>ShowKeyboard</c>) was intentionally dropped from the
    /// migrated surface. There is therefore no field here for the touch keyboard to attach to; the
    /// on-screen-keyboard entry for radio-channel fields lives entirely in the <see cref="FormRadioChannel"/>
    /// editor, which wires it itself. No keyboard plumbing is added to this dialog — preserving behaviour
    /// exactly.
    /// </para>
    /// <para>
    /// No WinForms, WPF or <c>System.Drawing</c> types are referenced; nullable reference types are
    /// disabled project-wide, so no reference type is annotated with <c>?</c> and the
    /// <c>ShowDialog&lt;CRadioChannel&gt;</c> result is a plain <see cref="CRadioChannel"/> (null on
    /// cancel, checked at runtime).
    /// </para>
    /// </remarks>
    public partial class FormRadio : Window
    {
        /// <summary>
        /// [XPLAT] The view-model backing this dialog, built from the injected comms aggregate in
        /// <see cref="FormRadio(CommCoordinatorService)"/> and assigned as the window's <c>DataContext</c>.
        /// Left <see langword="null"/> on the parameterless (loader / previewer) path, which never touches
        /// it.
        /// </summary>
        private readonly FormRadioViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable — without it the build emits
        /// warning <c>AVLN3001</c>, which the Release configuration (<c>TreatWarningsAsErrors</c>) promotes
        /// to an error. Application code always constructs the dialog through
        /// <see cref="FormRadio(CommCoordinatorService)"/>; this mirrors the dual-constructor convention of
        /// the sibling AgIO views.
        /// </summary>
        public FormRadio()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog from the AgIO comms aggregate: builds the backing
        /// <see cref="FormRadioViewModel"/> from the aggregate's NMEA, serial and NTRIP services, binds it,
        /// and wires the view-model's timed-message / edit-channel / close intent events to the
        /// corresponding window actions. Parity with the WinForms <c>FormRadio(Form callingForm)</c>
        /// constructor, minus the <c>FormLoop</c> god-object parameter.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate the composition root owns. The view-model is built from its
        /// <see cref="CommCoordinatorService.Nmea"/>, <see cref="CommCoordinatorService.Serial"/> and
        /// <see cref="CommCoordinatorService.Ntrip"/> services (the view-model validates each is non-null),
        /// replacing the former <c>mf</c> back-reference.
        /// </param>
        public FormRadio(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] The agent-prompt's `new FormRadioViewModel(comm)` shorthand resolves to the
            // view-model's actual single constructor, which takes the three comm services the dialog needs:
            // the NMEA service (current position for the channel-distance column), the serial service
            // (cross-platform port-name enumeration) and the NTRIP service (the shared radio serial port
            // plus the NTRIP reconfigure / radio-vs-NTRIP mutual exclusion). CommCoordinatorService exposes
            // exactly those three; the view-model fail-fasts if any is null.
            _vm = new FormRadioViewModel(comm.Nmea, comm.Serial, comm.Ntrip);
            DataContext = _vm;

            // [XPLAT] mf.TimedMessageBox(ms, title, message) -> show the auto-closing AgIO timed-message
            // toast over this dialog, non-modally (Show) so the dialog stays open for correction.
            _vm.RequestTimedMessage += (milliseconds, title, message) =>
                new FormTimedMessage(milliseconds, title, message).Show(this);

            // [XPLAT] btnAddChannel_Click / btnEditChannel_Click -> open the FormRadioChannel editor for the
            // supplied channel and feed the result back to the view-model. `async` here is the idiomatic
            // Avalonia pattern for an event handler that must await a modal child dialog;
            // ShowDialog<CRadioChannel> yields the edited record on accept or null on cancel, and the guard
            // mirrors the original `if (DialogResult.OK)` gate (ApplyEditedChannel is also null-safe).
            _vm.RequestEditChannel += async channel =>
            {
                CRadioChannel result = await new FormRadioChannel(channel).ShowDialog<CRadioChannel>(this);
                if (result != null)
                {
                    _vm.ApplyEditedChannel(result);
                }
            };

            // [XPLAT] WinForms Close() after a successful OK (and the DialogResult.Cancel path) -> close the
            // window; the view-model has already persisted the radio settings and channel list on the OK
            // path before raising this event.
            _vm.RequestClose += () => Close();
        }

        /// <summary>
        /// [XPLAT] Loads the compiled XAML for this window. Declared explicitly (rather than relying on a
        /// generated method) to match the convention used by every sibling AgIO Avalonia view.
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
