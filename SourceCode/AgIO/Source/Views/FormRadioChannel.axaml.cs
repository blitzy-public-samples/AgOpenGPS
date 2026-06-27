// [XPLAT] migrated from net48/WinForms FormRadioChannel.cs + FormRadioChannel.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AgIO.Controls;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Radio Channel" add/edit dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormRadioChannel : Form</c>. This is the code-behind half of
    /// <c>FormRadioChannel.axaml</c>; together they form a single view bound to a
    /// <see cref="FormRadioChannelViewModel"/> that exposes the channel's Id / Name / Frequency and
    /// Latitude / Longitude. It is opened by the migrated <c>FormRadio</c> to add a new channel or edit
    /// an existing one and returns the edited <see cref="CRadioChannel"/> as the dialog result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal (constructor).</b> The WinForms dialog took a direct <c>FormLoop</c> back
    /// reference (<c>mf</c>) and carried a mutable <c>Channel</c> property. Both couplings are gone: this
    /// view is constructed from just the <see cref="CRadioChannel"/> to edit
    /// (<see cref="FormRadioChannel(CRadioChannel)"/>), builds its own
    /// <see cref="FormRadioChannelViewModel"/> from that channel, and assigns it as the
    /// <see cref="StyledElement.DataContext"/>. The opener (<c>FormRadio</c>) uses
    /// <c>await new FormRadioChannel(channel).ShowDialog&lt;CRadioChannel&gt;(owner)</c> and applies the
    /// returned record.
    /// </para>
    /// <para>
    /// <b>Dialog result (accept vs cancel).</b> The OK / Cancel buttons in the markup bind to the
    /// view-model's <c>OkCommand</c> / <c>CancelCommand</c>, but a view-model must not close a window or
    /// pop a toast — those commands only raise intent EVENTS. This code-behind therefore bridges the three
    /// view-model events to the window-level actions the WinForms form performed inline:
    /// <list type="bullet">
    ///   <item><description><see cref="FormRadioChannelViewModel.RequestClose"/> (raised only when the
    ///   edits validate) carries the composed <see cref="CRadioChannel"/> → <c>Close(channel)</c>, so the
    ///   host's <c>ShowDialog&lt;CRadioChannel&gt;</c> resolves to that record (the WinForms
    ///   <c>DialogResult.OK</c> plus the edited value).</description></item>
    ///   <item><description><see cref="FormRadioChannelViewModel.RequestCancel"/> → <c>Close()</c> with
    ///   NO result, so <c>ShowDialog&lt;CRadioChannel&gt;</c> resolves to <see langword="null"/> (the
    ///   default for a reference type). Returning <see langword="null"/> — rather than the original
    ///   channel — is required by the opener's contract: <c>FormRadioViewModel.ApplyEditedChannel</c>
    ///   treats a <see langword="null"/> result as "cancelled / no-op" and APPLIES any non-null result, so
    ///   closing with the original channel would erroneously append an empty record when the operator
    ///   cancels an "add". This is the faithful equivalent of the WinForms <c>DialogResult.Cancel</c>
    ///   (the opener's former <c>if (DialogResult.OK)</c> gate).</description></item>
    ///   <item><description><see cref="FormRadioChannelViewModel.RequestTimedMessage"/> (duration, title,
    ///   message) → shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog. It
    ///   is shown non-modally (<c>Show(this)</c>) so the editor STAYS OPEN for correction — the parity
    ///   equivalent of the WinForms validation popup combined with <c>DialogResult.None</c>.</description></item>
    /// </list>
    /// The bridges are named methods (not lambdas) so they can be detached in <see cref="OnClosed"/>.
    /// </para>
    /// <para>
    /// <b>On-screen keyboard (Phase 2 parity).</b> The WinForms <c>tbox_Click</c> handler opened the
    /// touch keyboard whenever <c>FormLoop.isKeyboardOn</c> was set. That handler was wired in the
    /// designer to exactly four text boxes — <c>tbId</c>, <c>tbName</c>, <c>tbFrequency</c> (migrated
    /// <c>tbFreq</c>) and <c>tbLat</c>; the longitude field <c>tbLon</c> had no <c>Click</c> handler.
    /// To preserve behaviour exactly, <see cref="WireOnScreenKeyboard"/> attaches the keyboard to those
    /// same four fields ONLY (see <see cref="OnTextBoxPointerPressed"/>), leaving <c>tbLon</c> unwired.
    /// The WinForms <c>TextBox.Click</c> becomes Avalonia <see cref="InputElement.PointerPressed"/> (the
    /// codebase-standard analogue), the gate becomes <see cref="FormRadioChannelViewModel.IsKeyboardOn"/>,
    /// and the keyboard itself is presented by the converted
    /// <see cref="AgIO.Controls.TextBoxExtensions.ShowKeyboard(TextBox, Window)"/> helper, which writes the
    /// typed text back to the <see cref="TextBox.Text"/> — flowing through the markup's two-way binding
    /// into the bound view-model property.
    /// </para>
    /// <para>
    /// Follows the established AgIO view convention (<c>FormYes</c>, <c>FormPGN</c>, <c>FormSource</c>,
    /// <c>FormKeyboard</c>, <c>FormTimedMessage</c>): a parameterless constructor for the XAML loader /
    /// previewer, an explicit <see cref="AvaloniaXamlLoader"/>-based <see cref="InitializeComponent"/>,
    /// and named controls resolved at runtime via <see cref="NameScopeExtensions.FindControl{T}"/>.
    /// No WinForms, WPF or <c>System.Drawing</c> types are referenced; nullable reference types are
    /// disabled project-wide, so no reference type is annotated with <c>?</c>.
    /// </para>
    /// </remarks>
    public partial class FormRadioChannel : Window
    {
        /// <summary>
        /// [XPLAT] The view-model built from the channel passed to
        /// <see cref="FormRadioChannel(CRadioChannel)"/>. Held so its intent events can be detached in
        /// <see cref="OnClosed"/>. Left <see langword="null"/> on the parameterless (designer / loader)
        /// path; every member that touches it guards against that, so the design-time previewer is safe.
        /// </summary>
        private readonly FormRadioChannelViewModel _viewModel;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer (and to keep the compiled-XAML resource reachable so the Release build — which sets
        /// <c>TreatWarningsAsErrors</c> — does not fail on <c>AVLN3001</c>). Application code always
        /// constructs the dialog through <see cref="FormRadioChannel(CRadioChannel)"/>; this mirrors the
        /// dual-constructor convention of the sibling AgIO views.
        /// </summary>
        public FormRadioChannel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog for the supplied <paramref name="channel"/>: builds the backing
        /// <see cref="FormRadioChannelViewModel"/>, assigns it as the <see cref="StyledElement.DataContext"/>,
        /// bridges the view-model's close / cancel / timed-message intent events to the corresponding
        /// window actions, and wires the on-screen keyboard. Parity with the WinForms
        /// <c>FormRadioChannel(FormLoop)</c> constructor, minus the <c>FormLoop</c> god-object parameter.
        /// </summary>
        /// <param name="channel">
        /// The channel to add or edit. A <see langword="null"/> argument is tolerated — the view-model
        /// treats it as a new, empty channel — so the dialog can never fault on construction (nullable
        /// reference types are disabled project-wide).
        /// </param>
        public FormRadioChannel(CRadioChannel channel)
        {
            InitializeComponent();

            // [XPLAT] Build the VM from the channel (the WinForms FormRadioChannel_Load seeding now lives
            // in the VM constructor) and bind it. The .axaml declares x:DataType=FormRadioChannelViewModel,
            // so the compiled bindings resolve against exactly this DataContext.
            _viewModel = new FormRadioChannelViewModel(channel);
            DataContext = _viewModel;

            // [XPLAT] OkCommand / CancelCommand (bound in the .axaml) only raise these intent events; the
            // window performs the actual close / result / toast that the WinForms FormRadioChannel did
            // inline. A delegate { } initializer on each event in the VM guarantees they are non-null.
            _viewModel.RequestClose += OnRequestClose;
            _viewModel.RequestCancel += OnRequestCancel;
            _viewModel.RequestTimedMessage += OnRequestTimedMessage;

            // [XPLAT] WinForms tbox_Click -> PointerPressed on the same four fields (see WireOnScreenKeyboard).
            WireOnScreenKeyboard();
        }

        /// <summary>
        /// [XPLAT] Accept path: close the dialog returning the edited record so the opener's
        /// <c>ShowDialog&lt;CRadioChannel&gt;</c> resolves to it — the cross-platform equivalent of the
        /// WinForms <c>DialogResult.OK</c> plus the populated <c>Channel</c>. Raised by the view-model only
        /// after every validation guard passes.
        /// </summary>
        /// <param name="channel">The validated, composed channel to return to the opener.</param>
        private void OnRequestClose(CRadioChannel channel)
        {
            Close(channel);
        }

        /// <summary>
        /// [XPLAT] Cancel path: close with NO result so <c>ShowDialog&lt;CRadioChannel&gt;</c> resolves to
        /// <see langword="null"/>. Returning <see langword="null"/> (NOT the original channel) is mandated
        /// by the opener's <c>FormRadioViewModel.ApplyEditedChannel</c>, which treats <see langword="null"/>
        /// as "cancelled / no-op" and applies any non-null result — so this is the faithful equivalent of
        /// the WinForms <c>DialogResult.Cancel</c>.
        /// </summary>
        private void OnRequestCancel()
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Validation-feedback path: show the auto-closing AgIO timed-message toast over this
        /// dialog. It is shown non-modally with <c>Show(this)</c> so the editor stays open for the operator
        /// to correct the input — reproducing the WinForms <c>TimedMessageBox</c> + <c>DialogResult.None</c>
        /// behaviour. Duration, title and message are passed through unchanged.
        /// </summary>
        /// <param name="timeInMsec">How long the toast stays visible, in milliseconds.</param>
        /// <param name="title">The toast title (e.g. "Invalid Id").</param>
        /// <param name="message">The toast message body.</param>
        private void OnRequestTimedMessage(int timeInMsec, string title, string message)
        {
            FormTimedMessage toast = new FormTimedMessage(timeInMsec, title, message);
            toast.Show(this);
        }

        /// <summary>
        /// [XPLAT] Attaches the on-screen touch keyboard to the text fields that carried the WinForms
        /// <c>tbox_Click</c> handler. The designer wired <c>tbox_Click</c> to exactly <c>tbId</c>,
        /// <c>tbName</c>, <c>tbFrequency</c> (migrated <c>tbFreq</c>) and <c>tbLat</c>; the longitude field
        /// <c>tbLon</c> had no <c>Click</c> handler, so it is intentionally NOT wired here — preserving the
        /// original behaviour exactly. Controls are resolved by their <c>x:Name</c> through
        /// <see cref="NameScopeExtensions.FindControl{T}"/> because this view loads its XAML with the
        /// explicit <see cref="AvaloniaXamlLoader"/> call (so the name-generator's field-assigning overload
        /// never runs); each lookup is null-guarded to keep the design-time previewer safe.
        /// </summary>
        private void WireOnScreenKeyboard()
        {
            HookKeyboard("tbId");
            HookKeyboard("tbName");
            HookKeyboard("tbFreq");
            HookKeyboard("tbLat");
            // [XPLAT] tbLon deliberately omitted — the WinForms designer attached no Click handler to it.
        }

        /// <summary>
        /// [XPLAT] Resolves a text box by its <c>x:Name</c> and, when present, subscribes
        /// <see cref="OnTextBoxPointerPressed"/> to its <see cref="InputElement.PointerPressed"/> event
        /// (the Avalonia analogue of the WinForms <c>TextBox.Click</c>). The text boxes live for the
        /// lifetime of this window, so the subscription needs no explicit teardown.
        /// </summary>
        /// <param name="fieldName">The <c>x:Name</c> of the text box to wire.</param>
        private void HookKeyboard(string fieldName)
        {
            TextBox textBox = this.FindControl<TextBox>(fieldName);
            if (textBox != null)
            {
                textBox.PointerPressed += OnTextBoxPointerPressed;
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>tbox_Click</c> handler. When the
        /// view-model's <see cref="FormRadioChannelViewModel.IsKeyboardOn"/> flag is set (the former
        /// <c>FormLoop.isKeyboardOn</c> gate), it presents the on-screen keyboard for the tapped text box
        /// through the converted <see cref="AgIO.Controls.TextBoxExtensions.ShowKeyboard(TextBox, Window)"/>
        /// helper. That helper opens the modal <see cref="FormKeyboard"/>, awaits its result and writes the
        /// typed text back to <see cref="TextBox.Text"/>, which flows through the markup's two-way binding
        /// into the bound view-model property. <c>async void</c> is the standard signature for an awaited
        /// event handler (and matches the sibling views).
        /// </summary>
        /// <param name="sender">The text box that was pressed.</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnTextBoxPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsKeyboardOn && sender is TextBox textBox)
            {
                await textBox.ShowKeyboard(this);
            }
        }

        /// <summary>
        /// [XPLAT] Detaches the view-model intent events when the dialog closes so the view-model never
        /// keeps the closed window alive. The <see cref="InputElement.PointerPressed"/> subscriptions on
        /// the child text boxes are intentionally left in place — those controls share this window's
        /// lifetime and are collected with it.
        /// </summary>
        /// <param name="e">The close-event payload passed to the base implementation.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.RequestCancel -= OnRequestCancel;
                _viewModel.RequestTimedMessage -= OnRequestTimedMessage;
            }

            base.OnClosed(e);
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
