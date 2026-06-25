// [XPLAT] migrated from net48/WinForms Controls/NumericUpDownExtensions.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using AgIO.Views;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgIO.Controls
{
    /// <summary>
    /// [XPLAT] Avalonia replacement for the WinForms numeric-keypad helper. The original extension
    /// instantiated the deleted WinForms <c>FormNumeric</c> via <c>form.ShowDialog(owner)</c> and copied
    /// <c>FormNumeric.ReturnValue</c> back into the <see cref="NumericUpDown"/>. This rewrite drives the
    /// new MVVM numeric flow (<see cref="FormNumericViewModel"/> + <see cref="FormNumericView"/>) — which
    /// now includes the touch increment/decrement repeat buttons — and removes all
    /// <c>System.Windows.Forms</c>/<c>System.Drawing</c> coupling so the comm-hub project builds
    /// cross-platform.
    /// </summary>
    public static class NumericUpDownExtensions
    {
        /// <summary>
        /// Opens the touch numeric-keypad dialog seeded with the control's current value (and its
        /// minimum/maximum bounds) and, when the operator confirms a valid value, writes it back into the
        /// <see cref="NumericUpDown"/>. Mirrors the WinForms <c>ShowKeypad</c> behaviour (red editing
        /// highlight while the keypad is open, commit on OK, discard on Cancel) using the asynchronous
        /// Avalonia dialog model.
        /// </summary>
        /// <param name="numericUpDown">The Avalonia numeric control to edit.</param>
        /// <param name="owner">The window that owns the modal keypad dialog.</param>
        /// <returns>A task that completes once the modal dialog has closed.</returns>
        public static async Task ShowKeypadAsync(this NumericUpDown numericUpDown, Window owner)
        {
            if (numericUpDown == null)
            {
                return;
            }

            // [XPLAT] Visual editing feedback: flash red while the dialog is open, then restore the
            // control's previous background — the theme-safe Avalonia equivalent of the WinForms
            // `BackColor = Color.Red ... BackColor = Color.AliceBlue` round-trip.
            IBrush previousBackground = numericUpDown.Background;
            numericUpDown.Background = Brushes.Red;
            try
            {
                // [XPLAT] Avalonia NumericUpDown exposes Minimum/Maximum as decimal and Value as decimal?;
                // convert to the double contract used by the keypad view-model (a null Value -> 0).
                double minimum = (double)numericUpDown.Minimum;
                double maximum = (double)numericUpDown.Maximum;
                double currentValue = (double)(numericUpDown.Value ?? 0m);

                var viewModel = new FormNumericViewModel(minimum, maximum, currentValue);
                var dialog = new FormNumericView(viewModel);

                // [XPLAT] WinForms `if (form.ShowDialog(owner) == DialogResult.OK)` -> async ShowDialog<bool>.
                bool accepted = await dialog.ShowDialog<bool>(owner);
                if (accepted)
                {
                    numericUpDown.Value = (decimal)dialog.ResultValue;
                }
            }
            finally
            {
                numericUpDown.Background = previousBackground;
            }
        }
    }
}
