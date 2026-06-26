// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormConfig — folds ConfigMenu/Vehicle/Tool/Data/Module/Help partials) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the AgOpenGPS (GPS program) Configuration dialog. This is the companion
// partial class referenced by FormConfigView.axaml's x:Class
// (AgOpenGPS.Views.Settings.FormConfigView); without it the compiled-XAML / x:Name source generator
// cannot emit the typed control fields and the view type never exists in the assembly (Avalonia
// AXN0001), which fails the whole GPS build.
//
// Parity notes vs the WinForms original (FormConfig) — identical approach to the sibling
// FormSteerWizView.axaml.cs already delivered at this checkpoint:
//   * The markup is an imperative, code-behind-driven shell: NO x:DataType, NO DataContext, NO
//     {Binding} and NO markup-declared event handlers — every control is reached by x:Name, exactly
//     like the WinForms designer. This partial class is the Avalonia adapter required so the x:Class
//     view resolves and InitializeComponent runs.
//   * In WinForms FormConfig walked the operator through vehicle/tool/data/module configuration with
//     Enter/Leave-driven settings load-save against the split settings XML (the schema of which is a
//     behaviour-frozen contract per AAP §0.2.2). That settings load-save choreography is part of the
//     settings/guidance pipeline being decoupled into the Core view-models and injectable services
//     (AAP §0.3.2); it is owned by the application host / view-model rather than reached from this
//     view, so it is intentionally NOT reproduced here. No call is fabricated against services that
//     are still being extracted; this checkpoint delivers the faithful Avalonia layout shell plus the
//     code-behind partial that hosts it.
//   * No behaviour is lost relative to the current checkpoint: the WinForms dialog's live behaviour is
//     re-attached when the settings view-model / services are connected, against the same named
//     controls this shell exposes (tab1, the sidebar panels, and the per-section controls declared in
//     FormConfigView.axaml).

using Avalonia.Controls;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormConfigView : Window
    {
        public FormConfigView()
        {
            InitializeComponent();
        }
    }
}
