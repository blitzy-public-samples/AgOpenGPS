// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using Avalonia.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the configuration-summary panel — the behaviour half of
    /// <c>ConfigSummaryControl.axaml</c>. This is a 1:1 behavioural-parity reimplementation of the
    /// WinForms <c>Forms/Config/ConfigSummaryControl</c> (ConfigSummaryControl.cs +
    /// ConfigSummaryControl.Designer.cs): a read-only surface that summarizes the active vehicle and
    /// tool profile values. Only the UI framework underneath it changes (AAP §0.3.3, §0.7.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Display-only and imperative (no view-model, no data binding). The constructor assigns the
    /// localized caption text from <see cref="gStr"/>; <see cref="UpdateSummary(bool, int)"/> refreshes
    /// the value labels from the cross-platform <see cref="VehicleSettings"/> / <see cref="ToolSettings"/>
    /// / <see cref="Settings"/> stores plus the active profile names on <see cref="RegistrySettings"/>;
    /// and <see cref="SetSummaryWidth(string)"/> sets the pre-formatted working-width readout. Each label
    /// is reached through its generated typed field (declared with the matching <c>x:Name</c> in the
    /// paired XAML).
    /// </para>
    /// <para>
    /// [XPLAT] The WinForms method was <c>UpdateSummary(FormGPS mf)</c> and reached back into the
    /// <c>FormGPS</c> (<c>mf</c>) god-object for exactly two primitives — <c>mf.isMetric</c> and
    /// <c>mf.tool.numOfSections</c>. Those are now injected as plain parameters, removing the last
    /// dependency on the WinForms shell (AAP §0.1.2 Extract/Move). Every numeric <c>ToString</c>
    /// conversion uses <see cref="CultureInfo.InvariantCulture"/> so the displayed values never drift
    /// with the host OS locale (AAP §0.6.5 — the highest data-integrity rule of the migration). The
    /// frozen <see cref="Distance"/> formatters in <c>AgOpenGPS.Core.Models</c> handle their own
    /// unit formatting and are used unchanged.
    /// </para>
    /// </remarks>
    public partial class ConfigSummaryControl : UserControl
    {
        /// <summary>
        /// Initializes the control and assigns the static, localized caption labels from
        /// <see cref="gStr"/>. Parity with the WinForms constructor: the profile-menu hint carries no
        /// trailing colon, while every other caption appends <c>":"</c>.
        /// </summary>
        public ConfigSummaryControl()
        {
            InitializeComponent();

            labelProfileMenuHint.Text = gStr.gsProfileMenuHint;
            labelUnits.Text = gStr.gsUnits + ":";
            labelWidth.Text = gStr.gsWidth + ":";
            labelSections.Text = gStr.gsSections + ":";
            labelOffset.Text = gStr.gsOffset + ":";
            labelOverlap.Text = gStr.gsOverlap + ":";
            labelLookAhead.Text = gStr.gsLookAhead + ":";
            labelNudge.Text = gStr.gsNudge + ":";
            labelTramW.Text = gStr.gsTramWidth + ":";
            labelWheelBase.Text = gStr.gsWheelbase + ":";
            labelVehicleType.Text = gStr.gsVehiclegroupbox + ":";
            labelAntPivot.Text = gStr.gsPivot + ":";
            labelAntOffset.Text = gStr.gsAntennaOffset + ":";
            labelHitch.Text = gStr.gsHitchLength + ":";
        }

        /// <summary>
        /// Refreshes the vehicle- and tool-summary value labels from the current settings.
        /// </summary>
        /// <param name="isMetric">
        /// Whether distances are shown in metric units. [XPLAT] replaces the former <c>mf.isMetric</c>;
        /// forwarded to the frozen <see cref="Distance"/> formatters and used for the Units readout.
        /// </param>
        /// <param name="numOfSections">
        /// The active number of tool sections. [XPLAT] replaces the former <c>mf.tool.numOfSections</c>.
        /// </param>
        public void UpdateSummary(bool isMetric, int numOfSections) // [XPLAT] decoupled from FormGPS mf
        {
            var vs = VehicleSettings.Default;
            var ts = ToolSettings.Default;

            // Vehicle panel
            lblSummaryVehicleName.Text = RegistrySettings.vehicleProfileName;
            lblSumVehicleType.Text = vs.setVehicle_vehicleType == 0 ? "Tractor"
                : vs.setVehicle_vehicleType == 1 ? "Harvester"
                : "Articulated";
            lblSumWheelbase.Text = Distance.SmallDistanceString(isMetric, vs.setVehicle_wheelbase);
            lblAntPivot.Text = Distance.SmallDistanceString(isMetric, vs.setVehicle_antennaPivot);
            lblAntOffset.Text = Distance.SmallDistanceString(isMetric, vs.setVehicle_antennaOffset);
            lblHitch.Text = Distance.SmallDistanceString(isMetric, ts.setVehicle_hitchLength);

            // Tool panel
            lblSummaryToolName.Text = RegistrySettings.toolProfileName;
            lblSumNumSections.Text = numOfSections.ToString(CultureInfo.InvariantCulture); // [XPLAT] InvariantCulture
            lblToolOffset.Text = Distance.SmallDistanceString(isMetric, ts.setVehicle_toolOffset);
            lblOverlap.Text = Distance.SmallDistanceString(isMetric, ts.setVehicle_toolOverlap);
            lblLookahead.Text = ts.setVehicle_toolLookAheadOn.ToString(CultureInfo.InvariantCulture) + " sec"; // [XPLAT] InvariantCulture
            lblNudgeDistance.Text = Distance.VerySmallDistanceString(isMetric, 0.01 * ts.setAS_snapDistance);
            // [XPLAT] Fully qualify Settings: the enclosing AgOpenGPS.Views.Settings namespace would
            // otherwise shadow the AgOpenGPS.Properties.Settings type for the simple name "Settings".
            lblTramWidth.Text = Distance.MediumDistanceString(isMetric, AgOpenGPS.Properties.Settings.Default.setTram_tramWidth);

            // Outside the panels
            lblUnits.Text = isMetric ? "Metric" : "Imperial";
        }

        /// <summary>
        /// Sets the pre-formatted working-width readout. Parity with the WinForms
        /// <c>SetSummaryWidth(string)</c>: the host supplies the already-formatted width string (the
        /// summary control performs no width math of its own).
        /// </summary>
        /// <param name="widthText">The pre-formatted working-width text to display.</param>
        public void SetSummaryWidth(string widthText)
        {
            lblSummaryWidth.Text = widthText;
        }
    }
}
