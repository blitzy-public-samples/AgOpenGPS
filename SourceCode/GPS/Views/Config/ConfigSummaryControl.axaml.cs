// [XPLAT] migrated from net48/WinForms (Forms/Settings/ConfigSummaryControl.{cs,Designer.cs}) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the configuration summary panel. Avalonia reimplementation of the WinForms
    /// <c>ConfigSummaryControl : UserControl</c> hosted inside the configuration dialog. The control is a
    /// read-only summary surface whose label values are populated by the host
    /// (<c>ConfigView</c>) from the extracted configuration services that replace the former
    /// <c>FormGPS</c> (<c>mf</c>) god-object referenced by the WinForms <c>UpdateSummary(mf)</c>; that
    /// data-population step is tracked in MIGRATION_DOCS/TRANSITION_MAP.md and is intentionally not
    /// fabricated here against state that does not yet exist. This partial class therefore provides the
    /// constructor and <see cref="InitializeComponent"/> wiring required for the control to load.
    /// </summary>
    public partial class ConfigSummaryControl : UserControl
    {
        public ConfigSummaryControl()
        {
            InitializeComponent();
        }
    }
}
