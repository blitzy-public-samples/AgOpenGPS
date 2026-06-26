// [XPLAT] migrated from net48/WinForms (Forms/Inputs/FormKeyboard.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the on-screen keyboard text-entry dialog, hosting the migrated
    /// <c>Keypad.Keyboard</c> UserControl. The .axaml declares no event handlers, so this code-behind
    /// is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// The <c>keyboard1.ButtonPressed</c> subscription and the OK/Cancel dialog-result transitions are
    /// host-owned and wired where the dialog is shown (mirroring the WinForms entry-text behaviour);
    /// no fabricated behaviour is introduced here. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormKeyboard : Window
    {
        public FormKeyboard()
        {
            InitializeComponent();
        }
    }
}
