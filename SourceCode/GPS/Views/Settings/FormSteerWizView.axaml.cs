// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormSteerWiz.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the AutoSteer Configuration WIZARD dialog (the LAYOUT half of the
// code-behind-driven pair; the markup FormSteerWizView.axaml carries the ~300 named controls and
// declares no bindings or markup handlers).
//
// Parity notes vs the WinForms original (FormSteerWiz):
//   * The markup is an imperative, code-behind-driven shell: no x:DataType, no DataContext, no
//     {Binding} and no markup-declared Click handlers — every control is reached by x:Name, exactly
//     like the WinForms form. This partial class is the Avalonia adapter required so the x:Class view
//     resolves and InitializeComponent runs.
//   * In WinForms this wizard walked the operator through AutoSteer calibration: it encoded/decoded
//     the steer-configuration PGN frames (which must remain byte-for-byte identical per AAP §0.2.2),
//     drove the hidden-header tab navigation via SelectedIndex, applied Load-Defaults, coloured the
//     steer-status indicator (Red/Green/Yellow/Magenta) and localised its labels via gStr. That PGN
//     encode/decode, the calibration math, the tab-navigation choreography, the defaults and the
//     gStr localisation are part of the guidance/transport pipeline being decoupled into injectable
//     services and a view-model (AAP §0.3.2); they are owned by the application host / view-model
//     rather than reached from this view, so they are intentionally NOT reproduced here. No call is
//     fabricated against services that are still being extracted; this checkpoint delivers the
//     faithful Avalonia layout shell plus the code-behind partial that hosts it.
//   * No behaviour is lost relative to the current checkpoint: the WinForms wizard's live behaviour
//     is re-attached when the steer view-model / PGN services are connected, against the same named
//     controls this shell exposes (btnStartWizard, tabWiz, hsbar*, nud*, cbox*, chk*, pbar*, lbl*,
//     btnSteerStatus, the free-drive overlay panel1, etc.).

using Avalonia.Controls;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormSteerWizView : Window
    {
        public FormSteerWizView()
        {
            InitializeComponent();
        }
    }
}
