// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.ViewModels;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the main configuration dialog. Avalonia cross-platform reimplementation
    /// of the WinForms <c>FormConfig</c> (Forms/Settings/FormConfig.cs + FormConfig.Designer.cs). The
    /// view-model (<see cref="AgOpenGPS.Core.ViewModels.ConfigViewModel"/>) is supplied by the sibling
    /// <c>AvaloniaConfigMenuPanelPresenter</c> via <c>new ConfigView { DataContext = configViewModel }</c>,
    /// so this class exposes only a parameterless constructor and the thin Avalonia adapter behaviour:
    /// it seeds the two hosted configuration UserControls from the cross-platform settings stores on
    /// open, and closes on the OK button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hosted <c>configVehicleControl</c> (<see cref="ConfigVehicleControl"/>) and
    /// <c>configSummaryControl</c> (<see cref="ConfigSummaryControl"/>) x:Names declared in
    /// <c>ConfigView.axaml</c> are a hard contract; this shell drives them imperatively by name through
    /// their public APIs (<see cref="ConfigVehicleControl.Initialize(VehicleConfig)"/> and
    /// <see cref="ConfigSummaryControl.UpdateSummary(bool, int)"/>), reproducing the data the legacy
    /// <c>FormConfig</c> pushed into those pages — without any reference to the removed WinForms
    /// <c>FormGPS</c> (<c>mf</c>) god-object.
    /// </para>
    /// <para>
    /// The OK button is wired in the markup via <c>Click="OnOkClick"</c> (the established Click-handler
    /// convention used across this migration — e.g. the sibling <c>ConfigMenuView</c> and
    /// <c>FormInputDialogView</c>), so the close path is the code-behind <see cref="OnOkClick"/> handler
    /// rather than a binding; <c>ConfigViewModel</c> exposes no command. The presenter's
    /// <c>CloseConfigDialog()</c> also calls <see cref="Window.Close()"/>, giving a single consistent
    /// close path. The <c>DataContext</c> is intentionally never set or replaced here — the presenter
    /// owns it.
    /// </para>
    /// </remarks>
    public partial class ConfigView : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by the presenter
        /// (<c>new ConfigView { DataContext = configViewModel }</c>) and by Avalonia's XAML loader.
        /// Only realises the compiled XAML; the <c>DataContext</c> is supplied by the caller afterwards.
        /// </summary>
        public ConfigView()
        {
            InitializeComponent();   // [XPLAT] Avalonia XAML-compiled
        }

        /// <summary>
        /// [XPLAT] Avalonia equivalent of the WinForms <c>Form.Load</c> event (mirrors the sibling
        /// <c>ConfigMenuView.OnOpened</c>): by the time it runs the named controls are realised and the
        /// presenter-supplied <see cref="StyledElement.DataContext"/> is set, so the hosted pages can be
        /// seeded from the current settings.
        /// </summary>
        /// <param name="e">The event data forwarded to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            SeedFromSettings();
        }

        /// <summary>
        /// [XPLAT] Pushes the current cross-platform settings into the two hosted configuration
        /// UserControls, reproducing what the WinForms <c>FormConfig</c> did when it showed the vehicle
        /// and summary pages — but reading the migrated <c>VehicleSettings</c>/<c>ToolSettings</c>/
        /// <c>Settings</c> stores directly instead of the former <c>FormGPS</c> (<c>mf</c>) state.
        /// </summary>
        private void SeedFromSettings()
        {
            // [XPLAT] Build the vehicle model from settings and seed the ported vehicle UserControl.
            // AgOpenGPS.Properties.Settings is fully qualified on purpose: the simple name "Settings" is
            // shadowed by the AgOpenGPS.Views.Settings namespace from this AgOpenGPS.Views.Config context
            // (same fully-qualification the sibling ConfigVehicleControl uses). VehicleSettings/ToolSettings
            // have no such namespace collision, so their short names resolve via using AgOpenGPS.Properties.
            // int vehicle type (0/1/2) -> VehicleType enum; vehicle-image flag (bool); opacity int
            // percent -> 0..1 double; and System.Drawing.Color -> ColorRgba via ColorRgba's explicit
            // operator (the same conversion the sibling ConfigVehicleControl uses for the reverse).
            var vehicleConfig = new VehicleConfig
            {
                Type = (VehicleType)VehicleSettings.Default.setVehicle_vehicleType,
                IsImage = AgOpenGPS.Properties.Settings.Default.setDisplay_isVehicleImage,
                Opacity = AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity / 100.0,
                Color = (ColorRgba)AgOpenGPS.Properties.Settings.Default.setDisplay_colorVehicle,
            };
            configVehicleControl.Initialize(vehicleConfig);

            // [XPLAT] Section count mirrors the legacy mf.tool.numOfSections selector: unique sections when
            // sections-not-zones is active, otherwise the same-width multi-section count.
            int numOfSections = ToolSettings.Default.setTool_isSectionsNotZones
                ? ToolSettings.Default.setVehicle_numSections
                : ToolSettings.Default.setTool_numSectionsMulti;

            // [XPLAT] isMetric comes from the bound view-model (DayNightAndUnitsViewModel.IsMetric);
            // default to metric when no view-model is attached.
            bool isMetric = (DataContext as ConfigViewModel)?.IsMetric ?? true;

            configSummaryControl.UpdateSummary(isMetric, numOfSections);
        }

        /// <summary>
        /// [XPLAT] Closes the configuration dialog. Wired from the markup via <c>Click="OnOkClick"</c> on
        /// <c>btnOK</c>; the <c>IsDefault</c>/<c>IsCancel</c> attributes additionally map Enter/Escape to
        /// this path for keyboard operability on the chromeless (SystemDecorations="None") dialog. Mirrors
        /// the WinForms OK button that closed the form.
        /// </summary>
        /// <param name="sender">The OK button raising the event (unused).</param>
        /// <param name="e">The routed-event data (unused).</param>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
