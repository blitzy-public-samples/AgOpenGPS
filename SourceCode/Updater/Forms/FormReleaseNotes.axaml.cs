// [XPLAT] migrated from net48/WinForms (Forms/FormReleaseNotes.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Dialog for displaying release notes. Avalonia reimplementation of the WinForms
    /// <c>FormReleaseNotes</c>; the static helper now awaits a modal <c>ShowDialog</c> instead of
    /// the blocking WinForms <c>ShowDialog</c>.
    /// </summary>
    public partial class FormReleaseNotes : Window
    {
        public FormReleaseNotes()
        {
            // [XPLAT] Pattern B: InitializeComponent + typed x:Name fields generated from the XAML.
            InitializeComponent();
        }

        /// <summary>
        /// Shows release notes in a modal dialog over <paramref name="parent"/>.
        /// </summary>
        public static async Task ShowReleaseNotes(Window parent, string title, string version, string releaseNotes)
        {
            var dialog = new FormReleaseNotes
            {
                Title = title
            };
            dialog.lblTitle.Text = $"Release Notes - {version}";
            dialog.txtNotes.Text = !string.IsNullOrEmpty(releaseNotes) ? releaseNotes : "No release notes available.";

            if (parent != null)
            {
                await dialog.ShowDialog(parent);
            }
            else
            {
                dialog.Show();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>[XPLAT] Esc closes the dialog (WinForms key-handling parity).</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
