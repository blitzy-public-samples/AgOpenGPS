// [XPLAT] migrated from net48/WinForms (Forms/FormYes.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the full-screen "OK" acknowledgment message box — a faithful Avalonia
    /// reimplementation of the WinForms <c>FormYes</c> (Forms/FormYes.cs + FormYes.designer.cs): a
    /// single large, bold, centered message on a white panel with one OK image button anchored
    /// bottom-right inside a fixed 881 x 459 borderless pink window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an imperative dialog (no view-model and no data binding): the only logic in the original
    /// WinForms <c>FormYes(string messageStr)</c> constructor was <c>lblMessage2.Text = messageStr</c>,
    /// reproduced here by addressing the named <see cref="TextBlock"/> <c>lblMessage2</c> declared in
    /// <c>FormYesView.axaml</c> directly.
    /// </para>
    /// <para>
    /// The dialog exposes a single OK button (<c>btnSerialOK</c>) that carried <c>DialogResult.OK</c> in
    /// WinForms; here its click closes the window returning <see langword="true"/>, so a modal caller
    /// using <c>ShowDialog&lt;bool&gt;</c> observes the acknowledgment. There is intentionally no
    /// Yes/No/Cancel button — this is purely an acknowledgment box. Callers that need a yes/no question
    /// use <c>FormDialogView.ShowQuestionAsync</c> instead.
    /// </para>
    /// </remarks>
    public partial class FormYesView : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, both of which instantiate the view without arguments. Application code constructs
        /// the dialog through <see cref="FormYesView(string)"/>.
        /// </summary>
        public FormYesView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormYes(string messageStr)</c> constructor: loads the
        /// view and shows the supplied message.
        /// </summary>
        /// <param name="messageStr">The acknowledgment message shown in <c>lblMessage2</c>.</param>
        public FormYesView(string messageStr)
        {
            InitializeComponent();

            // [XPLAT] Verbatim from WinForms FormYes(string messageStr) — the sole statement in the
            // original constructor. Avalonia's TextBlock.Text accepts null without throwing, so the
            // assignment is carried across exactly, with no added null guard.
            lblMessage2.Text = messageStr;
        }

        /// <summary>
        /// [XPLAT] OK button handler — the cross-platform replacement for the WinForms
        /// <c>btnSerialOK.DialogResult = DialogResult.OK</c>. Closes the dialog returning
        /// <see langword="true"/> to a <c>ShowDialog&lt;bool&gt;</c> caller. Wired from
        /// <c>FormYesView.axaml</c> via <c>Click="OnOkClick"</c> (so it must not also be subscribed in
        /// code, which would fire twice).
        /// </summary>
        /// <param name="sender">The OK button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
