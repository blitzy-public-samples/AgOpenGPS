// [XPLAT] migrated from net48/WinForms Forms/FormRadioChannel.cs + FormRadioChannel.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Radio Channel" editor dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormRadioChannel : Form</c>. This is the code-behind half
    /// of <c>FormRadioChannel.axaml</c>; together they form a single view bound to a
    /// <see cref="FormRadioChannelViewModel"/> that exposes the radio channel's id/name/frequency
    /// and latitude/longitude. It follows the same convention as the sibling Avalonia views in this
    /// folder (<c>FormPGN</c>, <c>FormYes</c>, <c>FormTimedMessage</c>): a parameterless constructor
    /// for the XAML loader plus a view-model overload that assigns
    /// <see cref="StyledElement.DataContext"/>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The OK/Cancel buttons bind to the view-model's <c>OkCommand</c>/<c>CancelCommand</c>, but
    /// those commands only raise intent EVENTS — a view-model cannot close a window or pop a toast. This
    /// code-behind therefore subscribes the three view-model events and performs the window-level actions
    /// the WinForms <c>FormRadioChannel</c> did directly:
    /// <list type="bullet">
    ///   <item><see cref="FormRadioChannelViewModel.RequestClose"/> (carries the edited
    ///   <see cref="CRadioChannel"/>) → <c>Close(channel)</c>, so the host's
    ///   <c>ShowDialog&lt;CRadioChannel&gt;</c> returns the record (WinForms <c>DialogResult.OK</c> + value).</item>
    ///   <item><see cref="FormRadioChannelViewModel.RequestCancel"/> → <c>Close()</c> with no result
    ///   (WinForms <c>DialogResult.Cancel</c>).</item>
    ///   <item><see cref="FormRadioChannelViewModel.RequestTimedMessage"/> (duration, title, message) →
    ///   shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog on a validation
    ///   failure, while the editor stays open (parity with the WinForms validation popup).</item>
    /// </list>
    /// The handlers are named methods (not lambdas) so they can be unsubscribed in
    /// <see cref="OnClosed"/>, preventing the view-model from holding the closed window alive.
    /// </remarks>
    public partial class FormRadioChannel : Window
    {
        // [XPLAT] Held so the view-model's intent events can be unsubscribed in OnClosed.
        private FormRadioChannelViewModel _viewModel;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormRadioChannel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the
        /// view-model's close / cancel / timed-message intent events to the corresponding window actions.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the radio-channel fields.</param>
        public FormRadioChannel(FormRadioChannelViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // [XPLAT] OkCommand/CancelCommand (bound in the .axaml) only raise these events; the window
            // performs the actual close / result / toast that the WinForms FormRadioChannel did inline.
            if (_viewModel != null)
            {
                _viewModel.RequestClose += OnRequestClose;
                _viewModel.RequestCancel += OnRequestCancel;
                _viewModel.RequestTimedMessage += OnRequestTimedMessage;
            }
        }

        // [XPLAT] WinForms accepted edit: DialogResult.OK + return the edited record. Closing with the
        // CRadioChannel makes the host's ShowDialog<CRadioChannel> resolve to that value.
        private void OnRequestClose(CRadioChannel channel) => Close(channel);

        // [XPLAT] WinForms cancel: DialogResult.Cancel. Close with no result (null).
        private void OnRequestCancel() => Close();

        // [XPLAT] WinForms validation popup: show the auto-closing AgIO timed-message toast over this
        // dialog (non-modal, so the editor stays open for correction), mirroring the original behaviour.
        private void OnRequestTimedMessage(int timeInMsec, string title, string message)
        {
            // [XPLAT] FormTimedMessage now exposes the (milliseconds, title, message) constructor directly
            // (the FormTimedMessageViewModel-based constructor was retired in the toast reimplementation);
            // behaviour is unchanged — same duration/title/message shown over this dialog.
            FormTimedMessage toast = new FormTimedMessage(timeInMsec, title, message);
            toast.Show(this);
        }

        /// <summary>
        /// [XPLAT] Unsubscribe the view-model events when the dialog closes so the view-model never keeps
        /// the closed window alive.
        /// </summary>
        /// <param name="e">The close-event payload.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.RequestCancel -= OnRequestCancel;
                _viewModel.RequestTimedMessage -= OnRequestTimedMessage;
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
