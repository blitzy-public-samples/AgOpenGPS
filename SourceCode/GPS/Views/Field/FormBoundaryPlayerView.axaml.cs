// [XPLAT] migrated from net48/WinForms (Forms/Field/FormBoundaryPlayer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the boundary recording player (replays a recorded path to lay down a boundary) — a 1:1 visual-parity reimplementation of the WinForms
    /// <c>FormBoundaryPlayer</c> (FormBoundaryPlayer.cs + FormBoundaryPlayer.Designer.cs). Imperative dialog with NO DataContext,
    /// x:DataType or MVVM bindings; controls are addressed by x:Name. The .axaml declares no event
    /// handlers, so this code-behind is the thin Avalonia adapter required for XAML compilation.
    /// </summary>
    /// <remarks>
    /// Driving the recorded path and recording the boundary drive the live position / boundary model. That behaviour depends on the FormGPS god-object / services that are not projected
    /// at this checkpoint, so it is host-owned — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated
    /// calls to not-yet-existing services are made here.
    /// </remarks>
    public partial class FormBoundaryPlayerView : Window
    {
        public FormBoundaryPlayerView()
        {
            InitializeComponent();
        }
    }
}
