// [XPLAT] migrated from net48/WinForms Controls/TextBoxExtensions.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using AgIO.Views;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgIO.Controls
{
    /// <summary>
    /// [XPLAT] Avalonia replacement for the WinForms on-screen-keyboard helper. The original
    /// extension instantiated the deleted WinForms <c>FormKeyboard</c> via
    /// <c>form.ShowDialog(owner)</c> and copied <c>FormKeyboard.ReturnString</c> back into the
    /// <see cref="TextBox"/>. This rewrite drives the new MVVM keyboard flow
    /// (<see cref="FormKeyboardViewModel"/> + <see cref="FormKeyboardView"/>) and removes all
    /// <c>System.Windows.Forms</c>/<c>System.Drawing</c> coupling so the comm-hub project builds
    /// cross-platform.
    /// </summary>
    public static class TextBoxExtensions
    {
        /// <summary>
        /// Opens the touch on-screen keyboard dialog seeded with the text box's current text and,
        /// when the operator confirms, writes the edited string back into the text box. Mirrors the
        /// WinForms <c>ShowKeyboard</c> behaviour (red editing highlight while the keypad is open,
        /// commit on OK, discard on Cancel) using the asynchronous Avalonia dialog model.
        /// </summary>
        /// <param name="textBox">The Avalonia text box to edit.</param>
        /// <param name="owner">The window that owns the modal keyboard dialog.</param>
        /// <returns>A task that completes once the modal dialog has closed.</returns>
        public static async Task ShowKeyboardAsync(this TextBox textBox, Window owner)
        {
            if (textBox == null)
            {
                return;
            }

            // [XPLAT] Visual editing feedback: flash red while the dialog is open, then restore the
            // control's previous background. This is the theme-safe Avalonia equivalent of the WinForms
            // `BackColor = Color.Red ... BackColor = Color.AliceBlue` round-trip (capturing and restoring
            // the existing brush avoids fighting the Fluent theme with a hard-coded colour).
            IBrush previousBackground = textBox.Background;
            textBox.Background = Brushes.Red;
            try
            {
                var viewModel = new FormKeyboardViewModel(textBox.Text);
                var dialog = new FormKeyboardView(viewModel);

                // [XPLAT] WinForms `if (form.ShowDialog(owner) == DialogResult.OK)` -> async ShowDialog<bool>.
                bool accepted = await dialog.ShowDialog<bool>(owner);
                if (accepted)
                {
                    textBox.Text = dialog.Result;
                }
            }
            finally
            {
                textBox.Background = previousBackground;
            }
        }
    }
}
