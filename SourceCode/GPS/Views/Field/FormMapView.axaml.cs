// [XPLAT] migrated from net48/WinForms (Forms/Field/FormMap.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the background-map (online imagery) configuration dialog — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormMap</c> (FormMap.cs + FormMap.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Fetching and displaying online map tiles is an optional, feature-gated capability (GMap on Windows) configured against the FormGPS map model. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormMapView : Window
    {
        public FormMapView()
        {
            InitializeComponent();
        }
    }
}
