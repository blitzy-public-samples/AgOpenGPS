// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using AgIO.Views;

namespace AgIO.Controls
{
    public static class TextBoxExtensions
    {
        public static async Task ShowKeyboard(this TextBox textBox, Window owner)
        {
            // [XPLAT] Capture the control's existing background and restore it in a finally block, rather
            // than unconditionally resetting to a hardcoded brush. This guarantees the active-edit colour
            // is cleared even if ShowDialog throws (previously the box stayed red on an exception) and
            // preserves the control's real themed/resting brush instead of clobbering it with a literal.
            var originalBackground = textBox.Background;
            textBox.Background = Brushes.Red;
            try
            {
                var dialog = new FormKeyboard(textBox.Text ?? string.Empty);

                string result = await dialog.ShowDialog<string>(owner);
                if (result != null)
                {
                    textBox.Text = result;
                }
            }
            finally
            {
                textBox.Background = originalBackground;
            }
        }
    }
}
