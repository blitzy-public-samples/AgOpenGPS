// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using AgIO.Views;

namespace AgIO.Controls
{
    /// <summary>
    /// [XPLAT] Avalonia replacement for the WinForms numeric-keypad helper. The original WinForms
    /// extension painted the control red, opened a modal numeric entry form, and copied the accepted
    /// value back into the <see cref="NumericUpDown"/>. This rewrite opens the migrated Avalonia
    /// <see cref="FormNumeric"/> window via the asynchronous <c>ShowDialog&lt;double?&gt;</c> dialog
    /// model and drops the Windows-only UI coupling so the comm-hub project builds cross-platform.
    /// </summary>
    public static class NumericUpDownExtensions
    {
        /// <summary>
        /// Opens the touch numeric keypad seeded with the control's current value and its inclusive
        /// minimum/maximum bounds, then writes the entered value back into the
        /// <see cref="NumericUpDown"/> only when the operator confirms a valid entry. Preserves the
        /// WinForms <c>ShowKeypad</c> behaviour exactly: a red active-edit highlight while the keypad is
        /// open, commit on accept, and no change on cancel.
        /// </summary>
        /// <param name="numericUpDown">The Avalonia numeric control being edited.</param>
        /// <param name="owner">The window that owns the modal keypad dialog.</param>
        /// <returns>A task that completes once the modal keypad dialog has closed.</returns>
        public static async Task ShowKeypad(this NumericUpDown numericUpDown, Window owner)
        {
            // [XPLAT] Active-edit cue: paint the control red while the keypad is open.
            numericUpDown.Background = Brushes.Red;

            // [XPLAT] Avalonia NumericUpDown exposes Minimum/Maximum as decimal and Value as decimal?; cast
            // to the double contract of FormNumeric (a null Value is seeded as 0 so the cast cannot throw).
            var dialog = new FormNumeric(
                (double)numericUpDown.Minimum,
                (double)numericUpDown.Maximum,
                (double)(numericUpDown.Value ?? 0m));

            // [XPLAT] Async modal: FormNumeric returns the validated value (HasValue) on accept and null on
            // cancel/dismiss, so the value is committed only on accept — preserving the original accept-only gate.
            double? result = await dialog.ShowDialog<double?>(owner);
            if (result.HasValue)
            {
                numericUpDown.Value = (decimal)result.Value;
            }

            // [XPLAT] Restore the resting appearance once the keypad has closed.
            numericUpDown.Background = Brushes.AliceBlue;
        }
    }
}
