// [XPLAT] migrated from net48/WinForms (Forms/Inputs/FormNumeric.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the numeric-entry dialog, hosting the migrated <c>Keypad.NumKeypad</c>
    /// UserControl with min/max readouts and up/down nudge buttons. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// The <c>keypad1.ButtonPressed</c> subscription and the <c>btnDistanceUp</c>/<c>btnDistanceDn</c>
    /// nudge handling are host-owned and wired where the dialog is shown (mirroring the WinForms
    /// numeric-entry behaviour); no fabricated behaviour is introduced here. See
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormNumeric : Window
    {
        public FormNumeric()
        {
            InitializeComponent();
        }
    }
}
