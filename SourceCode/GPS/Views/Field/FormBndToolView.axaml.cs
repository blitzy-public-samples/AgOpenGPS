// [XPLAT] migrated from net48/WinForms (Forms/Field/FormBndTool.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the boundary-creation tool (record / drive / drag a new field boundary and control recording options) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormBndTool</c> (FormBndTool.cs + FormBndTool.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Capturing boundary points and committing the new boundary drive the live boundary / position model (mf.bnd, mf.gyd, mf.pn). That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormBndToolView : Window
    {
        public FormBndToolView()
        {
            InitializeComponent();
        }
    }
}
