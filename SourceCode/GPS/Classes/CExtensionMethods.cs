// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// De-Windowsed extension helpers. The WinForms-only members that previously lived here are removed
// by the migration (AAP §0.2.1, §0.5):
//   * NudlessNumericUpDown : System.Windows.Forms.NumericUpDown — a custom spinner control; the
//     Avalonia views reimplement numeric entry directly (e.g. FormSimCoordsView), so the WinForms
//     control has no cross-platform consumer.
//   * SetProgressNoAnimation(this System.Windows.Forms.ProgressBar) — a WinForms Aero-animation
//     work-around with no consumers once the WinForms surface is removed.
// Only the framework-agnostic colour helper is retained: System.Drawing.Color lives in the in-box
// System.Drawing.Primitives assembly on net8.0, so it carries across all platforms unchanged.
using System.Drawing;

namespace AgOpenGPS
{
    public static class CExtensionMethods
    {
        /// <summary>
        /// Clamps each RGB channel that is fully saturated (255) down to 254, leaving alpha untouched.
        /// Used so a colour never serialises/round-trips as pure 255 in a channel where the legacy
        /// format treated 255 as a sentinel. Cross-platform: <see cref="Color"/> is in the in-box
        /// System.Drawing.Primitives assembly.
        /// </summary>
        public static Color CheckColorFor255(this Color color)
        {
            var currentR = color.R;
            var currentG = color.G;
            var currentB = color.B;

            if (currentR == 255) currentR = 254;
            if (currentG == 255) currentG = 254;
            if (currentB == 255) currentB = 254;

            return Color.FromArgb(color.A, currentR, currentG, currentB);
        }
    }
}
