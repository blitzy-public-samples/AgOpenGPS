// [XPLAT] migrated from net48/WinForms Forms/FormRadio.cs + FormRadio.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Radio Settings" configuration dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormRadio : Form</c>. This is the code-behind half of
    /// <c>FormRadio.axaml</c>; together they form a single view bound to a <see cref="FormRadioViewModel"/>
    /// that configures the serial radio RTK-correction source and manages the operator's radio channels.
    /// It follows the same convention as the sibling Avalonia views in this folder
    /// (<c>FormSource</c>, <c>FormRadioChannel</c>, <c>FormPGN</c>): a parameterless constructor for the
    /// XAML loader plus a view-model overload that assigns <see cref="StyledElement.DataContext"/>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The buttons bind to the view-model's commands, but those commands only raise intent
    /// EVENTS — a view-model cannot close a window, pop a toast, or open another dialog. This code-behind
    /// therefore subscribes the three view-model events and performs the window-level actions the WinForms
    /// <c>FormRadio</c> did directly:
    /// <list type="bullet">
    ///   <item><see cref="FormRadioViewModel.RequestClose"/> → <c>Close()</c>, so the host's
    ///   <c>ShowDialog</c> resolves (WinForms <c>Close()</c> after a successful OK, and the
    ///   <c>DialogResult.Cancel</c> path).</item>
    ///   <item><see cref="FormRadioViewModel.RequestTimedMessage"/> (duration, title, message) → shows the
    ///   auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog (the former
    ///   <c>mf.TimedMessageBox</c>), while the dialog stays open — preserving the WinForms
    ///   "no channel selected" / "NTRIP also enabled" popups and the stay-open-on-invalid behaviour.</item>
    ///   <item><see cref="FormRadioViewModel.RequestEditChannel"/> (carries the channel to add/edit) →
    ///   opens the <see cref="FormRadioChannel"/> editor via <c>ShowDialog&lt;CRadioChannel&gt;</c> and
    ///   feeds its result back through <see cref="FormRadioViewModel.ApplyEditedChannel"/>, reproducing the
    ///   WinForms <c>btnAddChannel_Click</c> / <c>btnEditChannel_Click</c> editor round-trip (a cancelled
    ///   editor yields <see langword="null"/>, which <c>ApplyEditedChannel</c> treats as a no-op).</item>
    /// </list>
    /// The handlers are named methods (not lambdas) so they can be unsubscribed in <see cref="OnClosed"/>,
    /// preventing the view-model from holding the closed window alive.
    /// </remarks>
    public partial class FormRadio : Window
    {
        // [XPLAT] Held so the view-model's intent events can be unsubscribed in OnClosed.
        private FormRadioViewModel _viewModel;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormRadio()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the view-model's
        /// close / timed-message / edit-channel intent events to the corresponding window actions.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the radio configuration and channel list.</param>
        public FormRadio(FormRadioViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // [XPLAT] The bound commands raise these events; the window performs the actual close / toast /
            // editor-launch that the WinForms FormRadio did inline.
            if (_viewModel != null)
            {
                _viewModel.RequestClose += OnRequestClose;
                _viewModel.RequestTimedMessage += OnRequestTimedMessage;
                _viewModel.RequestEditChannel += OnRequestEditChannel;
            }
        }

        // [XPLAT] WinForms Close() (after a successful OK) and the DialogResult.Cancel path both collapse to
        // closing the window; the view-model has already persisted settings on the OK path.
        private void OnRequestClose() => Close();

        // [XPLAT] WinForms mf.TimedMessageBox: show the auto-closing AgIO timed-message toast over this
        // dialog (non-modal, so the dialog stays open for correction), mirroring the original behaviour.
        private void OnRequestTimedMessage(int timeInMsec, string title, string message)
        {
            FormTimedMessage toast = new FormTimedMessage(timeInMsec, title, message);
            toast.Show(this);
        }

        // [XPLAT] WinForms btnAddChannel_Click / btnEditChannel_Click: open the FormRadioChannel editor for
        // the supplied channel and feed the result back to the view-model. async void is the idiomatic
        // Avalonia pattern for an event handler that must await a modal child dialog; ShowDialog<CRadioChannel>
        // yields the edited record on accept or null on cancel (ApplyEditedChannel handles both).
        private async void OnRequestEditChannel(CRadioChannel channel)
        {
            // [XPLAT] FormRadioChannel takes the CRadioChannel directly and builds its own
            // FormRadioChannelViewModel internally (its documented single-arg contract), then returns the
            // edited record via ShowDialog<CRadioChannel> (null on cancel; ApplyEditedChannel handles both).
            FormRadioChannel editor = new FormRadioChannel(channel);

            CRadioChannel result = await editor.ShowDialog<CRadioChannel>(this);

            if (_viewModel != null)
            {
                _viewModel.ApplyEditedChannel(result);
            }
        }

        /// <summary>
        /// [XPLAT] Unsubscribe the view-model events when the dialog closes so the view-model never keeps the
        /// closed window alive.
        /// </summary>
        /// <param name="e">The close-event payload.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.RequestTimedMessage -= OnRequestTimedMessage;
                _viewModel.RequestEditChannel -= OnRequestEditChannel;
                _viewModel = null;
            }

            base.OnClosed(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
