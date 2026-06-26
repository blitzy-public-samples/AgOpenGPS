// [XPLAT] migrated from net48/WinForms (Forms/FormYes.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the large "OK" acknowledgment message box. Faithful Avalonia
    /// reimplementation of the WinForms <c>FormYes</c> (Forms/FormYes.cs): a single bold, centered
    /// message above one OK image button.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (no view-model). The WinForms <c>FormYes(string messageStr)</c> constructor only
    /// assigned <c>lblMessage2.Text = messageStr</c> (the window is a fixed 881 x 459, so there is no
    /// proportional resize as in the standalone ModSim sibling). The OK button returns
    /// <see langword="true"/> through <c>ShowDialog&lt;bool&gt;</c>, the cross-platform replacement for
    /// the WinForms <c>DialogResult.OK</c>.
    /// </remarks>
    public partial class FormYesView : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (keeps the
        /// avares:// resource reachable and avoids AVLN3001, which Release treats as an error). Production
        /// code constructs the dialog via <see cref="FormYesView(string)"/>.
        /// </summary>
        public FormYesView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Mirrors the WinForms <c>FormYes(string messageStr)</c> constructor.
        /// </summary>
        /// <param name="messageStr">The acknowledgment message to display.</param>
        public FormYesView(string messageStr)
        {
            InitializeComponent();

            // [XPLAT] verbatim from WinForms FormYes(string). TextBlock.Text accepts null, so no width
            // calculation and no null guard are required (unlike the ModSim FormYes proportional sizing).
            lblMessage2.Text = messageStr;
        }

        /// <summary>
        /// [XPLAT] btnSerialOK click -> Close(true). Cross-platform replacement for the WinForms
        /// <c>btnSerialOK.DialogResult = DialogResult.OK</c>, surfaced via <c>ShowDialog&lt;bool&gt;</c>.
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
