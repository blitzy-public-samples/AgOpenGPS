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
            textBox.Background = Brushes.Red;

            var dialog = new FormKeyboard(textBox.Text ?? string.Empty);

            string result = await dialog.ShowDialog<string>(owner);
            if (result != null)
            {
                textBox.Text = result;
            }

            textBox.Background = Brushes.AliceBlue;
        }
    }
}
