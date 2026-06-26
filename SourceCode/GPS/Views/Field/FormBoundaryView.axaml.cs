// [XPLAT] migrated from net48/WinForms (Forms/Field/FormBoundary.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the boundary manager (lists the field's boundaries and lets the operator add / delete / toggle them) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormBoundary</c> (FormBoundary.cs + FormBoundary.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Enumerating and mutating the boundary list operate on the live boundary model (mf.bnd.bndList). That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormBoundaryView : Window
    {
        public FormBoundaryView()
        {
            InitializeComponent();
        }
    }
}
