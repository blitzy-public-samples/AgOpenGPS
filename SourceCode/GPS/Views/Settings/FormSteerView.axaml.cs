// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormSteer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the steer-settings configuration dialog (the LAYOUT half of the
// code-behind-driven pair; the markup carries ~194 named controls and no bindings or declared
// handlers).
//
// Parity notes vs the WinForms original (FormSteer):
//   * The markup is an imperative, code-behind-driven shell: no x:DataType, no DataContext, no
//     {Binding} and no markup-declared Click handlers — every control is reached by x:Name, exactly
//     like the WinForms form. This partial class is the Avalonia adapter required so the x:Class
//     view resolves and InitializeComponent runs.
//   * In WinForms this form was the most tightly coupled to the FormGPS "mf" god-object and to the
//     PGN transport: it encoded/decoded the steer-configuration PGN frames (which must remain
//     byte-for-byte identical) and read/wrote dozens of steer settings, localised ~80 labels via
//     gStr, and progressively expanded its own window as sections were revealed. That steer-settings
//     wiring, the PGN encode/decode, the gStr localisation and the window-expansion choreography are
//     part of the guidance/transport pipeline being decoupled into injectable services and a
//     view-model; per AAP §0.3.2 ("host supplies data") they are owned by the application host /
//     view-model rather than reached from this view. They are intentionally NOT reproduced here so
//     that no call is fabricated against services that are still being extracted; this checkpoint
//     delivers the faithful Avalonia layout shell plus the code-behind partial that hosts it.
//   * No behaviour is lost relative to the current checkpoint: the WinForms form's live behaviour is
//     re-attached when the steer view-model / PGN services are connected, against the same named
//     controls this shell exposes.

using Avalonia.Controls;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormSteerView : Window
    {
        public FormSteerView()
        {
            InitializeComponent();
        }
    }
}
