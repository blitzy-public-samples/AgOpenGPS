// [XPLAT] migrated from net48/WinForms (Forms/Settings/ConfigVehicleControl.{cs,Designer.cs}) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the vehicle-type / brand chooser panel. Avalonia reimplementation of the
    /// WinForms <c>ConfigVehicleControl : UserControl</c> hosted inside the configuration dialog. The
    /// vehicle-type radios (<c>rbtnHarvester</c>/<c>rbtnTractor</c>/<c>rbtnArticulated</c>) and the empty
    /// brand panels (<c>panelHarvesterBrands</c>/<c>panelTractorBrands</c>/<c>panelArticulatedBrands</c>)
    /// are populated and toggled by the host (<c>ConfigView</c>) via the extracted configuration services
    /// that replace the former <c>FormGPS</c> (<c>mf</c>) god-object referenced by the WinForms
    /// <c>Initialize(mf)</c>; that data-population step is tracked in MIGRATION_DOCS/TRANSITION_MAP.md and
    /// is intentionally not fabricated here against state that does not yet exist. This partial class
    /// therefore provides the constructor and <see cref="InitializeComponent"/> wiring required for the
    /// control to load.
    /// </summary>
    public partial class ConfigVehicleControl : UserControl
    {
        public ConfigVehicleControl()
        {
            InitializeComponent();
        }
    }
}
