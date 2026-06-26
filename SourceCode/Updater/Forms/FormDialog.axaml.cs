// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Reusable, custom-styled modal dialog for the AgOpenGPS Updater. It reproduces the four
    /// presentation kinds of the original Windows Forms <c>FormDialog</c> — confirm, info, error and
    /// success — as an Avalonia <see cref="Window"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Avalonia code-behind half of the dialog; its markup partner is <c>FormDialog.axaml</c>.
    /// It is a 1:1 behavioral reimplementation of the WinForms <c>FormDialog</c> (<c>Forms/FormDialog.cs</c>):
    /// the per-kind glyph (<c>?</c>/<c>i</c>/<c>!</c>/<c>✓</c>), the icon-chip background and the OK-button
    /// background are switched here in code-behind, while the markup supplies the default (confirm)
    /// appearance.
    /// </para>
    /// <para>
    /// The WinForms helpers blocked on a synchronous <c>ShowDialog</c> and returned a
    /// <c>DialogResult</c>; Avalonia modals are asynchronous, so the helpers here are awaitable. The
    /// confirmation helper yields a <see cref="Task"/> of <see langword="bool"/> that is
    /// <see langword="true"/> only when the affirmative (OK) button was pressed, while the single-button
    /// helpers yield a non-generic <see cref="Task"/>. Closing via Cancel or the title bar yields
    /// <see langword="false"/>, matching the original <c>DialogResult.Cancel</c> default.
    /// </para>
    /// <para>
    /// All palette colors are resolved by key from the application-level resources declared in
    /// <c>App.axaml</c>, keeping that markup the single source of truth for the updater color scheme.
    /// </para>
    /// </remarks>
    public partial class FormDialog : Window
    {
        /// <summary>
        /// Initializes a new <see cref="FormDialog"/> instance and loads its compiled XAML, which wires
        /// up the named controls (BtnOK, BtnCancel, PanelIcon, LblIcon, LblMessage) used by the helpers.
        /// </summary>
        public FormDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows a two-button confirmation dialog modally over <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">The owning window the dialog is centered over and blocks.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="message">The body message; long text wraps within the fixed-width dialog.</param>
        /// <param name="okText">The affirmative button caption. Defaults to <c>"Yes"</c>.</param>
        /// <param name="cancelText">The dismissive button caption. Defaults to <c>"No"</c>.</param>
        /// <returns>
        /// A task that yields <see langword="true"/> when the affirmative button was pressed; otherwise
        /// <see langword="false"/> (Cancel or title-bar close).
        /// </returns>
        public static async Task<bool> ShowConfirm(Window owner, string title, string message,
                                                   string okText = "Yes", string cancelText = "No")
        {
            var dialog = new FormDialog { Title = title };
            dialog.LblMessage.Text = message;
            dialog.BtnOK.Content = okText;
            dialog.BtnCancel.Content = cancelText;
            dialog.BtnCancel.IsVisible = true;
            dialog.LblIcon.Text = "?";
            dialog.PanelIcon.Background = GetBrush("AccentBrush");

            // BtnOK keeps its XAML-default green (SuccessBrush); both buttons remain visible.
            return await ShowAsync(dialog, owner);
        }

        /// <summary>
        /// Shows a single-button information dialog modally over <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">The owning window the dialog is centered over and blocks.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="message">The body message; long text wraps within the fixed-width dialog.</param>
        public static async Task ShowInfo(Window owner, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.LblMessage.Text = message;
            dialog.BtnOK.Content = "OK";
            dialog.BtnCancel.IsVisible = false;
            dialog.LblIcon.Text = "i";
            dialog.PanelIcon.Background = GetBrush("AccentBrush");

            // BtnOK keeps its XAML-default green (SuccessBrush).
            await ShowAsync(dialog, owner);
        }

        /// <summary>
        /// Shows a single-button error dialog (red accents) modally over <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">The owning window the dialog is centered over and blocks.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="message">The body message; long text wraps within the fixed-width dialog.</param>
        public static async Task ShowError(Window owner, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.LblMessage.Text = message;
            dialog.BtnOK.Content = "OK";
            dialog.BtnCancel.IsVisible = false;
            dialog.LblIcon.Text = "!";
            dialog.PanelIcon.Background = GetBrush("ErrorBrush");
            dialog.BtnOK.Background = GetBrush("ErrorBrush");

            await ShowAsync(dialog, owner);
        }

        /// <summary>
        /// Shows a single-button success dialog (green accents) modally over <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">The owning window the dialog is centered over and blocks.</param>
        /// <param name="title">The dialog window title.</param>
        /// <param name="message">The body message; long text wraps within the fixed-width dialog.</param>
        public static async Task ShowSuccess(Window owner, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.LblMessage.Text = message;
            dialog.BtnOK.Content = "OK";
            dialog.BtnCancel.IsVisible = false;
            dialog.LblIcon.Text = "\u2713"; // ✓ check mark
            dialog.PanelIcon.Background = GetBrush("SuccessBrush");
            dialog.BtnOK.Background = GetBrush("SuccessBrush");

            await ShowAsync(dialog, owner);
        }

        /// <summary>
        /// Awaits <paramref name="dialog"/> as a modal owned by <paramref name="owner"/> and reports
        /// whether the affirmative button was pressed.
        /// </summary>
        /// <param name="dialog">The configured dialog instance to display.</param>
        /// <param name="owner">The owning window. When supplied, the dialog is shown modally.</param>
        /// <returns>
        /// <see langword="true"/> if OK was pressed; otherwise <see langword="false"/>. When no owner is
        /// available (not expected in practice) the dialog is shown modelessly and
        /// <see langword="false"/> is returned, since a result cannot be awaited without an owner.
        /// </returns>
        private static async Task<bool> ShowAsync(FormDialog dialog, Window owner)
        {
            if (owner != null)
            {
                return await dialog.ShowDialog<bool>(owner);
            }

            dialog.Show();
            return false;
        }

        /// <summary>
        /// Resolves a named palette brush (for example <c>"AccentBrush"</c>, <c>"ErrorBrush"</c> or
        /// <c>"SuccessBrush"</c>) from the application-level resources declared in <c>App.axaml</c>, so the
        /// updater color scheme stays single-sourced in markup rather than duplicated in code-behind.
        /// </summary>
        /// <param name="key">The resource key of the brush to resolve.</param>
        /// <returns>
        /// The resolved <see cref="IBrush"/>, or <see cref="Brushes.Transparent"/> if the application or
        /// resource is unavailable (for example at design time).
        /// </returns>
        private static IBrush GetBrush(string key)
        {
            if (Application.Current is { } app &&
                app.TryGetResource(key, app.ActualThemeVariant, out var value) &&
                value is IBrush brush)
            {
                return brush;
            }

            return Brushes.Transparent;
        }

        /// <summary>Closes the dialog with an affirmative result.</summary>
        private void BtnOK_Click(object sender, RoutedEventArgs e) => Close(true);

        /// <summary>Closes the dialog with a dismissive result.</summary>
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close(false);
    }
}
