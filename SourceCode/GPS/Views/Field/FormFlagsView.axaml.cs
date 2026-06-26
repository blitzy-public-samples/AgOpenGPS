// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFlags.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the field flag manager (lists placed flags and edits the selected flag's note / colour) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormFlags</c> (FormFlags.cs + FormFlags.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Enumerating, selecting, deleting and renaming flags operate on the live flag collection (mf.flagPts) and the OpenGL view. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormFlagsView : Window
    {
        public FormFlagsView()
        {
            InitializeComponent();
        }
    }
}
