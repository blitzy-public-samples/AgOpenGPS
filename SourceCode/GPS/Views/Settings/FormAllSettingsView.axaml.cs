// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Read-only "All Settings" diagnostic viewer. 1:1 parity reimplementation of the
// former WinForms Forms/Settings/FormAllSettings.cs (523 lines) on Avalonia.
//
// The view lists every Vehicle / Tool / Environment setting in three columns each,
// highlighting rows whose active-profile value differs from the fresh default, plus a
// live "System / GPS" telemetry tab refreshed once per second. It can export every tab
// to a tab-separated CSV, copy a composite screenshot to the clipboard, or save that
// composite as a PNG. All Windows-only surface (System.Windows.Forms DataGridView,
// System.Drawing bitmap capture, Clipboard.SetImage, explorer.exe) is reimplemented with
// cross-platform Avalonia primitives (ItemsControl + ObservableCollection, RenderTargetBitmap
// + DrawingContext, IClipboard, and an xdg-open/open/Process file-manager launcher).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Properties;
using AgOpenGPS.Views;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// Read-only snapshot of the live position / AHRS / communication telemetry that the
    /// "System / GPS" tab renders. The former WinForms screen read these directly off the
    /// <c>FormGPS</c> god-object (<c>mf</c>); after the migration that coupling is replaced
    /// with this small injectable seam supplied by the composition root.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The agent action plan specifies a constructor accepting an
    /// <c>ISystemTelemetry</c>. No such type exists anywhere in the repository yet (the
    /// scan-loop / position-service extraction is a later migration step and
    /// <c>FormGPS</c> has already been removed from disk), so — per the strict dependency
    /// rule — the contract is defined locally here. When no telemetry source is wired
    /// (the only situation that exists at this checkpoint), the System tab degrades
    /// gracefully to "--" placeholders rather than fabricating values, mirroring the
    /// sibling <c>FormGPSDataView</c> precedent.
    /// </remarks>
    public interface ISystemTelemetry
    {
        /// <summary>Most recent main-loop frame time, in milliseconds.</summary>
        double FrameTime { get; }

        /// <summary>Duration of the last GPS fix slice, in seconds (its reciprocal is the rate).</summary>
        double TimeSliceOfLastFix { get; }

        /// <summary>Observed GPS update rate, in hertz.</summary>
        double GpsHz { get; }

        /// <summary>Human-readable fix-quality descriptor (e.g. "RTK Fix").</summary>
        string FixQuality { get; }

        /// <summary>Number of satellites currently tracked.</summary>
        int SatsTracked { get; }

        /// <summary>Horizontal dilution of precision.</summary>
        double Hdop { get; }

        /// <summary>True when the UI is displaying metric units.</summary>
        bool IsMetric { get; }

        /// <summary>Antenna altitude in metres.</summary>
        double Altitude { get; }

        /// <summary>Antenna altitude in feet.</summary>
        double AltitudeFeet { get; }

        /// <summary>Current fix easting in the local plane, in metres.</summary>
        double FixEasting { get; }

        /// <summary>Current fix northing in the local plane, in metres.</summary>
        double FixNorthing { get; }

        /// <summary>Count of UDP sentences dropped since startup.</summary>
        int MissedSentenceCount { get; }

        /// <summary>IMU (gyro) heading, in degrees.</summary>
        double GyroInDegrees { get; }

        /// <summary>Fix-to-fix (GPS-derived) heading, in degrees.</summary>
        double GpsHeading { get; }

        /// <summary>Fused heading, in radians (multiply by 180/pi for degrees).</summary>
        double FixHeading { get; }

        /// <summary>IMU yaw rate (angular velocity), in degrees per second.</summary>
        double ImuYawRate { get; }
    }

    /// <summary>
    /// Immutable row model bound by the XAML templates (replaces the WinForms
    /// <c>DataGridView</c> rows). Header rows carry only <see cref="Setting"/> with
    /// <see cref="IsHeader"/> set; data rows carry the active <see cref="Profile"/> value
    /// and the fresh <see cref="Default"/> value, with <see cref="IsChanged"/> flagged when
    /// the two differ. Properties are public so the reflection-based XAML bindings
    /// (<c>x:CompileBindings="False"</c>) can read them.
    /// </summary>
    public sealed class SettingRow
    {
        /// <summary>Setting label, or the section title for header rows.</summary>
        public string Setting { get; init; } = "";

        /// <summary>Active profile value, formatted with the invariant culture.</summary>
        public string Profile { get; init; } = "";

        /// <summary>Fresh default value, formatted with the invariant culture.</summary>
        public string Default { get; init; } = "";

        /// <summary>True for a section-header row (spans all columns, no values).</summary>
        public bool IsHeader { get; init; }

        /// <summary>True when <see cref="Profile"/> differs from <see cref="Default"/>.</summary>
        public bool IsChanged { get; init; }
    }

    /// <summary>
    /// Avalonia window presenting the read-only "All Settings" overview. See the file
    /// header for the full behavioural contract; this is a faithful port of the WinForms
    /// <c>FormAllSettings</c>.
    /// </summary>
    public partial class FormAllSettingsView : Window
    {
        // ── Defaults: fresh instances supplying the "Default" column for changed-detection ──
        private static readonly VehicleSettings vsDefault = new VehicleSettings();
        private static readonly ToolSettings tsDefault = new ToolSettings();
        // Fully qualified: this file lives in namespace AgOpenGPS.Views.Settings, whose trailing
        // segment "Settings" would otherwise shadow the AgOpenGPS.Properties.Settings type.
        private static readonly AgOpenGPS.Properties.Settings esDefault = new AgOpenGPS.Properties.Settings();

        // ── Live telemetry seam (null when no source is wired — graceful "--" fallback) ──
        private readonly ISystemTelemetry _tel;

        // ── One-second refresh of the System tab (parity with WinForms timer1, Interval=1000) ──
        private DispatcherTimer _timer;

        // ── Bound collections: three columns per settings tab, plus the two-column System tab ──
        private readonly ObservableCollection<SettingRow> _vehicleL = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _vehicleM = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _vehicleR = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _toolL = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _toolM = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _toolR = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _environmentL = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _environmentM = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _environmentR = new ObservableCollection<SettingRow>();
        private readonly ObservableCollection<SettingRow> _systemRows = new ObservableCollection<SettingRow>();

        /// <summary>
        /// Parameterless constructor required by the Avalonia runtime XAML loader. Wires the
        /// window with no live telemetry source (System tab shows "--").
        /// </summary>
        public FormAllSettingsView() : this(null)
        {
        }

        /// <summary>
        /// Primary constructor. <paramref name="telemetry"/> supplies the live System-tab
        /// readouts; pass <c>null</c> until the position/AHRS services are extracted, in which
        /// case the System tab degrades gracefully.
        /// </summary>
        /// <param name="telemetry">Read-only telemetry snapshot, or <c>null</c>.</param>
        public FormAllSettingsView(ISystemTelemetry telemetry)
        {
            _tel = telemetry;

            InitializeComponent();

            // Bind each named ItemsControl to its backing collection (ItemsSource is not set in markup).
            dgvVehicleL.ItemsSource = _vehicleL;
            dgvVehicleM.ItemsSource = _vehicleM;
            dgvVehicleR.ItemsSource = _vehicleR;
            dgvToolL.ItemsSource = _toolL;
            dgvToolM.ItemsSource = _toolM;
            dgvToolR.ItemsSource = _toolR;
            dgvEnvironmentL.ItemsSource = _environmentL;
            dgvEnvironmentM.ItemsSource = _environmentM;
            dgvEnvironmentR.ItemsSource = _environmentR;
            dgvSystem.ItemsSource = _systemRows;

            // Buttons expose only x:Name in markup — wire the handlers imperatively.
            btnExportCSV.Click += BtnExportCSV_Click;
            btnCreatePNG.Click += BtnCreatePNG_Click;
            btnScreenShot.Click += BtnScreenShot_Click;
            btnClose.Click += BtnClose_Click;

            PopulateAllSettings();
            UpdateHeader();
            PopulateSystem();

            // [XPLAT] DispatcherTimer replaces the WinForms System.Windows.Forms.Timer.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        // ── Header labels ──────────────────────────────────────────────────────

        /// <summary>Refreshes the three profile-name labels above the tabs.</summary>
        private void UpdateHeader()
        {
            lblVehicleName.Text = "Vehicle: " + RegistrySettings.vehicleProfileName;
            lblToolName.Text = "Tool: " + RegistrySettings.toolProfileName;
            lblEnvironmentName.Text = "Environment: " + (RegistrySettings.environmentFileName ?? "Default");
        }

        // ── Populate orchestration ─────────────────────────────────────────────

        /// <summary>Populates the Vehicle, Tool and Environment tabs from the active profiles.</summary>
        private void PopulateAllSettings()
        {
            PopulateVehicle(_vehicleL, _vehicleM, _vehicleR, VehicleSettings.Default);
            PopulateTool(_toolL, _toolM, _toolR, ToolSettings.Default);
            PopulateEnvironment(_environmentL, _environmentM, _environmentR, AgOpenGPS.Properties.Settings.Default);
        }

        // ── Row helpers ────────────────────────────────────────────────────────

        /// <summary>Appends a section-header row (spans all columns).</summary>
        private static void AddHeader(ObservableCollection<SettingRow> list, string title)
        {
            list.Add(new SettingRow { Setting = title, Profile = "", Default = "", IsHeader = true });
        }

        /// <summary>
        /// Appends a three-column data row. <paramref name="def"/> is the fresh default,
        /// <paramref name="current"/> the active profile value; the row is flagged changed
        /// when their invariant-culture string forms differ (matching the WinForms compare).
        /// </summary>
        private static void AddRow(ObservableCollection<SettingRow> list, string name, object def, object current)
        {
            // Order mirrors the grid: Setting | Profile | Default.
            string ds = InvariantString(def);
            string cs = InvariantString(current);
            list.Add(new SettingRow
            {
                Setting = name,
                Profile = cs,
                Default = ds,
                IsChanged = ds != cs
            });
        }

        /// <summary>Appends a two-column System row (label + value).</summary>
        private static void AddSysRow(ObservableCollection<SettingRow> list, string name, object value)
        {
            list.Add(new SettingRow { Setting = name, Profile = InvariantString(value) });
        }

        /// <summary>
        /// [XPLAT] Formats a value with the invariant culture so that field files, CSV exports
        /// and changed-detection are identical regardless of the host OS locale (a comma decimal
        /// separator would otherwise corrupt parity — see AAP §0.6.5). Numerics and enums route
        /// through <see cref="IFormattable"/>; strings and booleans use their culture-independent
        /// <c>ToString()</c>.
        /// </summary>
        private static string InvariantString(object v)
        {
            if (v == null) return "";
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString() ?? "";
        }

        // ── Vehicle: L = Steer + Arduino, M = Geometry, R = GPS + IMU + Brand ──

        private static void PopulateVehicle(ObservableCollection<SettingRow> left, ObservableCollection<SettingRow> mid, ObservableCollection<SettingRow> right, VehicleSettings vs)
        {
            left.Clear();
            AddHeader(left, "── Steer");
            AddRow(left, "Max Steer Angle", vsDefault.setVehicle_maxSteerAngle, vs.setVehicle_maxSteerAngle);
            AddRow(left, "Max Angular Vel.", vsDefault.setVehicle_maxAngularVelocity, vs.setVehicle_maxAngularVelocity);
            AddRow(left, "Panic Stop Speed", vsDefault.setVehicle_panicStopSpeed, vs.setVehicle_panicStopSpeed);
            AddRow(left, "Steer In Reverse", vsDefault.setAS_isSteerInReverse, vs.setAS_isSteerInReverse);
            AddRow(left, "Side Hill Comp", vsDefault.setAS_sideHillComp, vs.setAS_sideHillComp);

            AddHeader(left, "── Arduino Steer");
            AddRow(left, "Counts/Degree", vsDefault.setAS_countsPerDegree, vs.setAS_countsPerDegree);
            AddRow(left, "Ackerman", vsDefault.setAS_ackerman, vs.setAS_ackerman);
            AddRow(left, "WAS Offset", vsDefault.setAS_wasOffset, vs.setAS_wasOffset);
            AddRow(left, "High Steer PWM", vsDefault.setAS_highSteerPWM, vs.setAS_highSteerPWM);
            AddRow(left, "Low Steer PWM", vsDefault.setAS_lowSteerPWM, vs.setAS_lowSteerPWM);
            AddRow(left, "Min Steer PWM", vsDefault.setAS_minSteerPWM, vs.setAS_minSteerPWM);
            AddRow(left, "Kp (Proportional)", vsDefault.setAS_Kp, vs.setAS_Kp);
            AddRow(left, "Min Steer Speed", vsDefault.setAS_minSteerSpeed, vs.setAS_minSteerSpeed);
            AddRow(left, "Max Steer Speed", vsDefault.setAS_maxSteerSpeed, vs.setAS_maxSteerSpeed);
            AddRow(left, "Func Speed Limit", vsDefault.setAS_functionSpeedLimit, vs.setAS_functionSpeedLimit);

            mid.Clear();
            AddHeader(mid, "── Geometry");
            AddRow(mid, "Vehicle Type", vsDefault.setVehicle_vehicleType, vs.setVehicle_vehicleType);
            AddRow(mid, "Wheelbase", vsDefault.setVehicle_wheelbase, vs.setVehicle_wheelbase);
            AddRow(mid, "Track Width", vsDefault.setVehicle_trackWidth, vs.setVehicle_trackWidth);
            AddRow(mid, "Antenna Pivot", vsDefault.setVehicle_antennaPivot, vs.setVehicle_antennaPivot);
            AddRow(mid, "Antenna Offset", vsDefault.setVehicle_antennaOffset, vs.setVehicle_antennaOffset);
            AddRow(mid, "Antenna Height", vsDefault.setVehicle_antennaHeight, vs.setVehicle_antennaHeight);

            right.Clear();
            AddHeader(right, "── GPS");
            AddRow(right, "Heading Source", vsDefault.setGPS_headingFromWhichSource, vs.setGPS_headingFromWhichSource);
            AddRow(right, "Dual Hdg Offset", vsDefault.setGPS_dualHeadingOffset, vs.setGPS_dualHeadingOffset);
            AddRow(right, "Dual Rev. Dist.", vsDefault.setGPS_dualReverseDetectionDistance, vs.setGPS_dualReverseDetectionDistance);
            AddRow(right, "Min Step Limit", vsDefault.setGPS_minimumStepLimit, vs.setGPS_minimumStepLimit);

            AddHeader(right, "── IMU");
            AddRow(right, "Roll Zero", vsDefault.setIMU_rollZero, vs.setIMU_rollZero);
            AddRow(right, "Roll Filter", vsDefault.setIMU_rollFilter, vs.setIMU_rollFilter);
            AddRow(right, "Fusion Weight", vsDefault.setIMU_fusionWeight2, vs.setIMU_fusionWeight2);
            AddRow(right, "Invert Roll", vsDefault.setIMU_invertRoll, vs.setIMU_invertRoll);
            AddRow(right, "Dual As IMU", vsDefault.setIMU_isDualAsIMU, vs.setIMU_isDualAsIMU);

            AddHeader(right, "── Brand");
            AddRow(right, "Tractor Brand", vsDefault.setBrand_TBrand, vs.setBrand_TBrand);
            AddRow(right, "Harvester Brand", vsDefault.setBrand_HBrand, vs.setBrand_HBrand);
            AddRow(right, "Articulated Brand", vsDefault.setBrand_WDBrand, vs.setBrand_WDBrand);
        }

        // ── Tool: L = Dimensions + Type, M = Sections + Lookahead + Guidance, R = Work Switch ──

        private static void PopulateTool(ObservableCollection<SettingRow> left, ObservableCollection<SettingRow> mid, ObservableCollection<SettingRow> right, ToolSettings ts)
        {
            left.Clear();
            AddHeader(left, "── Dimensions");
            AddRow(left, "Tool Width", tsDefault.setVehicle_toolWidth, ts.setVehicle_toolWidth);
            AddRow(left, "Tool Overlap", tsDefault.setVehicle_toolOverlap, ts.setVehicle_toolOverlap);
            AddRow(left, "Tool Offset", tsDefault.setVehicle_toolOffset, ts.setVehicle_toolOffset);
            AddRow(left, "Num Sections", tsDefault.setVehicle_numSections, ts.setVehicle_numSections);
            AddRow(left, "Trailing Hitch", tsDefault.setVehicle_toolTrailingHitchLength, ts.setVehicle_toolTrailingHitchLength);
            AddRow(left, "Tank Trailing", tsDefault.setVehicle_tankTrailingHitchLength, ts.setVehicle_tankTrailingHitchLength);
            AddRow(left, "Trailing To Pivot", tsDefault.setTool_trailingToolToPivotLength, ts.setTool_trailingToolToPivotLength);
            // [parity quirk] WinForms adds this row to MID here, then clears MID below, so it never
            // appears in the rendered grid. Reproduced verbatim (statement order preserved) for fidelity.
            AddRow(mid, "Hitch Length", tsDefault.setVehicle_hitchLength, ts.setVehicle_hitchLength);


            AddHeader(left, "── Tool Type");
            AddRow(left, "Is Trailing", tsDefault.setTool_isToolTrailing, ts.setTool_isToolTrailing);
            AddRow(left, "Is Rear Fixed", tsDefault.setTool_isToolRearFixed, ts.setTool_isToolRearFixed);
            AddRow(left, "Is TBT", tsDefault.setTool_isToolTBT, ts.setTool_isToolTBT);
            AddRow(left, "Is Front", tsDefault.setTool_isToolFront, ts.setTool_isToolFront);

            mid.Clear(); // [parity quirk] wipes the "Hitch Length" row added above
            AddHeader(mid, "── Sections");
            AddRow(mid, "Sections Not Zones", tsDefault.setTool_isSectionsNotZones, ts.setTool_isSectionsNotZones);
            AddRow(mid, "Off When Out", tsDefault.setTool_isSectionOffWhenOut, ts.setTool_isSectionOffWhenOut);
            AddRow(mid, "Fast Section", tsDefault.setSection_isFast, ts.setSection_isFast);

            AddHeader(mid, "── Look Ahead / Timing");
            AddRow(mid, "Look Ahead On", tsDefault.setVehicle_toolLookAheadOn, ts.setVehicle_toolLookAheadOn);
            AddRow(mid, "Look Ahead Off", tsDefault.setVehicle_toolLookAheadOff, ts.setVehicle_toolLookAheadOff);
            AddRow(mid, "Tool Off Delay", tsDefault.setVehicle_toolOffDelay, ts.setVehicle_toolOffDelay);
            AddRow(mid, "Hydraulic Lift LA", tsDefault.setVehicle_hydraulicLiftLookAhead, ts.setVehicle_hydraulicLiftLookAhead);
            AddRow(mid, "Headland Sect. Ctrl", tsDefault.setHeadland_isSectionControlled, ts.setHeadland_isSectionControlled);

            AddHeader(mid, "── Guidance (per Tool)");
            AddRow(mid, "Snap Distance", tsDefault.setAS_snapDistance, ts.setAS_snapDistance);
            AddRow(mid, "Snap Distance Ref", tsDefault.setAS_snapDistanceRef, ts.setAS_snapDistanceRef);
            AddRow(mid, "DeadZone Dist", tsDefault.setAS_deadZoneDistance, ts.setAS_deadZoneDistance);
            AddRow(mid, "DeadZone Hdg", tsDefault.setAS_deadZoneHeading, ts.setAS_deadZoneHeading);
            AddRow(mid, "DeadZone Delay", tsDefault.setAS_deadZoneDelay, ts.setAS_deadZoneDelay);
            AddRow(mid, "Stanley Used", tsDefault.setVehicle_isStanleyUsed, ts.setVehicle_isStanleyUsed);
            AddRow(mid, "Stanley Dist. Gain", tsDefault.stanleyDistanceErrorGain, ts.stanleyDistanceErrorGain);
            AddRow(mid, "Stanley Hdg. Gain", tsDefault.stanleyHeadingErrorGain, ts.stanleyHeadingErrorGain);
            AddRow(mid, "Stanley Integral", tsDefault.stanleyIntegralGainAB, ts.stanleyIntegralGainAB);
            AddRow(mid, "PurePursuit Intgrl", tsDefault.purePursuitIntegralGainAB, ts.purePursuitIntegralGainAB);
            AddRow(mid, "Goal Pt Acq Factor", tsDefault.setVehicle_goalPointAcquireFactor, ts.setVehicle_goalPointAcquireFactor);
            AddRow(mid, "Goal Pt Hold", tsDefault.setVehicle_goalPointLookAheadHold, ts.setVehicle_goalPointLookAheadHold);
            AddRow(mid, "Goal Pt Mult", tsDefault.setVehicle_goalPointLookAheadMult, ts.setVehicle_goalPointLookAheadMult);
            AddRow(mid, "Slow Speed Cutoff", tsDefault.setVehicle_slowSpeedCutoff, ts.setVehicle_slowSpeedCutoff);

            right.Clear();
            AddHeader(right, "── Work Switch");
            AddRow(right, "Steer Work Switch", tsDefault.setF_isSteerWorkSwitchEnabled, ts.setF_isSteerWorkSwitchEnabled);
            AddRow(right, "Min Coverage", tsDefault.setVehicle_minCoverage, ts.setVehicle_minCoverage);
        }

        // ── Environment: L = Display/Menu, M = AutoSteer + UTurn + GPS, R = Sound + Global + Work Switch ──

        private static void PopulateEnvironment(ObservableCollection<SettingRow> left, ObservableCollection<SettingRow> mid, ObservableCollection<SettingRow> right, AgOpenGPS.Properties.Settings es)
        {
            left.Clear();
            AddHeader(left, "── Display / Menu");
            AddRow(left, "Metric", esDefault.setMenu_isMetric, es.setMenu_isMetric);
            AddRow(left, "Grid On", esDefault.setMenu_isGridOn, es.setMenu_isGridOn);
            AddRow(left, "Lightbar On", esDefault.setMenu_isLightbarOn, es.setMenu_isLightbarOn);
            AddRow(left, "Side Guide Lines", esDefault.setMenu_isSideGuideLines, es.setMenu_isSideGuideLines);
            AddRow(left, "Pure On", esDefault.setMenu_isPureOn, es.setMenu_isPureOn);
            AddRow(left, "Simulator On", esDefault.setMenu_isSimulatorOn, es.setMenu_isSimulatorOn);
            AddRow(left, "Speedo On", esDefault.setMenu_isSpeedoOn, es.setMenu_isSpeedoOn);
            AddRow(left, "Lightbar Not SteerBar", esDefault.setMenu_isLightbarNotSteerBar, es.setMenu_isLightbarNotSteerBar);
            AddRow(left, "Day Mode", esDefault.setDisplay_isDayMode, es.setDisplay_isDayMode);
            AddRow(left, "Start Fullscreen", esDefault.setDisplay_isStartFullScreen, es.setDisplay_isStartFullScreen);
            AddRow(left, "Keyboard On", esDefault.setDisplay_isKeyboardOn, es.setDisplay_isKeyboardOn);
            AddRow(left, "Vehicle Image", esDefault.setDisplay_isVehicleImage, es.setDisplay_isVehicleImage);
            AddRow(left, "Texture On", esDefault.setDisplay_isTextureOn, es.setDisplay_isTextureOn);
            AddRow(left, "Brightness On", esDefault.setDisplay_isBrightnessOn, es.setDisplay_isBrightnessOn);
            AddRow(left, "Log Elevation", esDefault.setDisplay_isLogElevation, es.setDisplay_isLogElevation);
            AddRow(left, "Svenn Arrow", esDefault.setDisplay_isSvennArrowOn, es.setDisplay_isSvennArrowOn);
            AddRow(left, "Section Lines", esDefault.setDisplay_isSectionLinesOn, es.setDisplay_isSectionLinesOn);
            AddRow(left, "Line Smooth", esDefault.setDisplay_isLineSmooth, es.setDisplay_isLineSmooth);
            AddRow(left, "Hardware Messages", esDefault.setDisplay_isHardwareMessages, es.setDisplay_isHardwareMessages);
            AddRow(left, "Kiosk Mode", esDefault.setWindow_isKioskMode, es.setWindow_isKioskMode);
            AddRow(left, "Shutdown Computer", esDefault.setWindow_isShutdownComputer, es.setWindow_isShutdownComputer);
            AddRow(left, "Shutdown No Power", esDefault.setDisplay_isShutdownWhenNoPower, es.setDisplay_isShutdownWhenNoPower);
            AddRow(left, "Auto Start AgIO", esDefault.setDisplay_isAutoStartAgIO, es.setDisplay_isAutoStartAgIO);
            AddRow(left, "Auto Off AgIO", esDefault.setDisplay_isAutoOffAgIO, es.setDisplay_isAutoOffAgIO);
            AddRow(left, "Lightbar cm/px", esDefault.setDisplay_lightbarCmPerPixel, es.setDisplay_lightbarCmPerPixel);
            AddRow(left, "Line Width", esDefault.setDisplay_lineWidth, es.setDisplay_lineWidth);
            AddRow(left, "Brightness", esDefault.setDisplay_brightness, es.setDisplay_brightness);
            AddRow(left, "Cam Zoom", esDefault.setDisplay_camZoom, es.setDisplay_camZoom);
            AddRow(left, "Cam Pitch", esDefault.setDisplay_camPitch, es.setDisplay_camPitch);

            mid.Clear();
            AddHeader(mid, "── AutoSteer");
            AddRow(mid, "Guidance Lookahead", esDefault.setAS_guidanceLookAheadTime, es.setAS_guidanceLookAheadTime);
            AddRow(mid, "AutoSteer Auto On", esDefault.setAS_isAutoSteerAutoOn, es.setAS_isAutoSteerAutoOn);
            AddRow(mid, "Constant Contour", esDefault.setAS_isConstantContourOn, es.setAS_isConstantContourOn);

            AddHeader(mid, "── U-Turn");
            AddRow(mid, "Turn Radius", esDefault.set_youTurnRadius, es.set_youTurnRadius);
            AddRow(mid, "Extension Length", esDefault.set_youTurnExtensionLength, es.set_youTurnExtensionLength);
            AddRow(mid, "Dist From Boundary", esDefault.set_youTurnDistanceFromBoundary, es.set_youTurnDistanceFromBoundary);
            AddRow(mid, "Skip Width", esDefault.set_youSkipWidth, es.set_youSkipWidth);
            AddRow(mid, "U-Turn Style", esDefault.set_uTurnStyle, es.set_uTurnStyle);
            AddRow(mid, "U-Turn Smoothing", esDefault.setAS_uTurnSmoothing, es.setAS_uTurnSmoothing);
            AddRow(mid, "U-Turn Compensation", esDefault.setAS_uTurnCompensation, es.setAS_uTurnCompensation);
            AddRow(mid, "Num Guide Lines", esDefault.setAS_numGuideLines, es.setAS_numGuideLines);

            AddHeader(mid, "── GPS");
            AddRow(mid, "GPS Age Alarm", esDefault.setGPS_ageAlarm, es.setGPS_ageAlarm);
            AddRow(mid, "Is RTK", esDefault.setGPS_isRTK, es.setGPS_isRTK);
            AddRow(mid, "RTK Kill AutoSteer", esDefault.setGPS_isRTK_KillAutoSteer, es.setGPS_isRTK_KillAutoSteer);
            AddRow(mid, "Jump Fix Alarm", esDefault.setGPS_jumpFixAlarmDistance, es.setGPS_jumpFixAlarmDistance);
            AddRow(mid, "Sim Latitude", esDefault.setGPS_SimLatitude, es.setGPS_SimLatitude);
            AddRow(mid, "Sim Longitude", esDefault.setGPS_SimLongitude, es.setGPS_SimLongitude);
            AddRow(mid, "UDP Watch ms", esDefault.SetGPS_udpWatchMsec, es.SetGPS_udpWatchMsec);

            right.Clear();
            AddHeader(right, "── Sound");
            AddRow(right, "U-Turn Sound", esDefault.setSound_isUturnOn, es.setSound_isUturnOn);
            AddRow(right, "Hyd Lift Sound", esDefault.setSound_isHydLiftOn, es.setSound_isHydLiftOn);
            AddRow(right, "AutoSteer Sound", esDefault.setSound_isAutoSteerOn, es.setSound_isAutoSteerOn);
            AddRow(right, "Sections Sound", esDefault.setSound_isSectionsOn, es.setSound_isSectionsOn);

            AddHeader(right, "── Tram");
            AddRow(right, "Tram Width", esDefault.setTram_tramWidth, es.setTram_tramWidth);
            AddRow(right, "Tram Passes", esDefault.setTram_passes, es.setTram_passes);
            AddRow(right, "Tram Alpha", esDefault.setTram_alpha, es.setTram_alpha);

            AddHeader(right, "── IMU / Global");
            AddRow(right, "Reverse On", esDefault.setIMU_isReverseOn, es.setIMU_isReverseOn);
            AddRow(right, "AutoSwitch Dual Fix", esDefault.setAutoSwitchDualFixOn, es.setAutoSwitchDualFixOn);
            AddRow(right, "AutoSwitch Speed", esDefault.setAutoSwitchDualFixSpeed, es.setAutoSwitchDualFixSpeed);
            AddRow(right, "Draw Pivot", esDefault.setBnd_isDrawPivot, es.setBnd_isDrawPivot);
            AddRow(right, "Headland Dist On", esDefault.isHeadlandDistanceOn, es.isHeadlandDistanceOn);
            AddRow(right, "Bnd Tool Spacing", esDefault.bndToolSpacing, es.bndToolSpacing);
            AddRow(right, "Bnd Tool Smooth", esDefault.bndToolSmooth, es.bndToolSmooth);

            AddHeader(right, "── Work Switch");
            AddRow(right, "Work Switch", esDefault.setF_isWorkSwitchEnabled, es.setF_isWorkSwitchEnabled);
            AddRow(right, "Active Low", esDefault.setF_isWorkSwitchActiveLow, es.setF_isWorkSwitchActiveLow);
            AddRow(right, "Manual Sections", esDefault.setF_isWorkSwitchManualSections, es.setF_isWorkSwitchManualSections);
            AddRow(right, "Remote Work Sys", esDefault.setF_isRemoteWorkSystemOn, es.setF_isRemoteWorkSystemOn);
            AddRow(right, "Steer WS Manual", esDefault.setF_isSteerWorkSwitchManualSections, es.setF_isSteerWorkSwitchManualSections);
            AddRow(right, "Min Hdg Step Dist", esDefault.setF_minHeadingStepDistance, es.setF_minHeadingStepDistance);

            AddHeader(right, "── AgShare");
            AddRow(right, "AgShare Enabled", esDefault.AgShareEnabled, es.AgShareEnabled);
            AddRow(right, "Upload Active", esDefault.AgShareUploadActive, es.AgShareUploadActive);
            AddRow(right, "Public Field", esDefault.PublicField, es.PublicField);

            AddHeader(right, "── Other");
            AddRow(right, "Culture", RegistrySettings.culture, RegistrySettings.culture);
        }

        // ── System / GPS: live telemetry, refreshed once per second ────────────

        private void PopulateSystem()
        {
            _systemRows.Clear();

            // [XPLAT] Live readouts come from the injected telemetry seam. When no source is
            // wired (the situation at this checkpoint), every value degrades to "--" rather than
            // fabricating data — mirroring the sibling FormGPSDataView precedent.
            bool live = _tel != null;
            CultureInfo inv = CultureInfo.InvariantCulture;

            AddHeader(_systemRows, "── GPS / Fix");
            AddSysRow(_systemRows, "Frame Time (ms)", live ? _tel.FrameTime.ToString("N1", inv) : "--");
            AddSysRow(_systemRows, "Time Slice", live ? (1 / _tel.TimeSliceOfLastFix).ToString("N3", inv) : "--");
            AddSysRow(_systemRows, "GPS Hz", live ? _tel.GpsHz.ToString("N1", inv) : "--");
            AddSysRow(_systemRows, "Fix Quality", live ? (object)_tel.FixQuality : "--");
            AddSysRow(_systemRows, "Sats Tracked", live ? InvariantString(_tel.SatsTracked) : "--");
            AddSysRow(_systemRows, "HDOP", live ? InvariantString(_tel.Hdop) : "--");
            AddSysRow(_systemRows, "Altitude", live ? InvariantString(_tel.IsMetric ? _tel.Altitude : _tel.AltitudeFeet) : "--");
            AddSysRow(_systemRows, "Easting", live ? Math.Round(_tel.FixEasting, 2).ToString(inv) : "--");
            AddSysRow(_systemRows, "Northing", live ? Math.Round(_tel.FixNorthing, 2).ToString(inv) : "--");
            AddSysRow(_systemRows, "Missed UDP Sentences", live ? _tel.MissedSentenceCount.ToString(inv) : "--");

            AddHeader(_systemRows, "── Heading");
            AddSysRow(_systemRows, "IMU Heading", live ? InvariantString(_tel.GyroInDegrees) : "--");
            AddSysRow(_systemRows, "Fix-to-Fix Heading", live ? InvariantString(_tel.GpsHeading) : "--");
            AddSysRow(_systemRows, "Fused Heading (deg)", live ? (_tel.FixHeading * 57.2957795).ToString("N1", inv) : "--");
            AddSysRow(_systemRows, "Angular Velocity", live ? _tel.ImuYawRate.ToString("N2", inv) : "--");

            AddHeader(_systemRows, "── Application");
            AddSysRow(_systemRows, "Version", Program.SemVer);
            // [XPLAT] Path.Combine replaces the hard-coded "\\" so the separator is correct on every OS.
            AddSysRow(_systemRows, "Vehicle File", Path.Combine(RegistrySettings.vehiclesDirectory ?? "", RegistrySettings.vehicleProfileName + ".xml"));
            AddSysRow(_systemRows, "Tool File", Path.Combine(RegistrySettings.toolsDirectory ?? "", RegistrySettings.toolProfileName + ".xml"));
        }

        /// <summary>Timer tick — refreshes the header labels and the live System tab only.</summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateHeader();
            PopulateSystem();
        }


        // ── CSV Export (tab-separated, parity with WinForms) ───────────────────

        private void ExportToCSV(string path)
        {
            // [XPLAT] Tab-separated so the file opens correctly under every Excel locale.
            using (var sw = new StreamWriter(path, false, Encoding.UTF8))
            {
                WriteSettingsTabToCSV(sw, "VEHICLE", true, _vehicleL, _vehicleM, _vehicleR);
                WriteSettingsTabToCSV(sw, "TOOL", true, _toolL, _toolM, _toolR);
                WriteSettingsTabToCSV(sw, "ENVIRONMENT", true, _environmentL, _environmentM, _environmentR);
                WriteSettingsTabToCSV(sw, "SYSTEM / GPS", false, _systemRows);
            }
        }

        private static void WriteSettingsTabToCSV(StreamWriter sw, string tabName, bool threeColumn, params ObservableCollection<SettingRow>[] grids)
        {
            sw.WriteLine($"=== {tabName} ===");
            sw.WriteLine();

            bool firstSection = true;
            foreach (var grid in grids)
            {
                foreach (var row in grid)
                {
                    string setting = row.Setting ?? "";

                    if (setting.StartsWith("──", StringComparison.Ordinal))
                    {
                        if (!firstSection) sw.WriteLine();
                        firstSection = false;
                        string sectionName = setting.Replace("──", "").Trim();
                        sw.WriteLine($"--- {sectionName} ---");
                        if (threeColumn)
                            sw.WriteLine("Setting\tProfile Value\tDefault");
                        else
                            sw.WriteLine("Setting\tValue");
                    }
                    else if (!string.IsNullOrWhiteSpace(setting))
                    {
                        if (threeColumn)
                        {
                            string profile = row.Profile ?? "";
                            string def = row.Default ?? "";
                            sw.WriteLine($"{CsvEscape(setting)}\t{CsvEscape(profile)}\t{CsvEscape(def)}");
                        }
                        else
                        {
                            string val = row.Profile ?? "";
                            sw.WriteLine($"{CsvEscape(setting)}\t{CsvEscape(val)}");
                        }
                    }
                }
            }

            sw.WriteLine();
            sw.WriteLine();
        }

        private static string CsvEscape(string s)
        {
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // ── Screenshot: render all three settings tabs and stack them vertically ──

        /// <summary>
        /// [XPLAT] Cross-platform reimplementation of the WinForms <c>CaptureTabsBitmap</c>
        /// (which relied on <c>Control.DrawToBitmap</c> + System.Drawing). Draws the Vehicle,
        /// Tool and Environment tabs directly onto a single <see cref="RenderTargetBitmap"/>
        /// via a <see cref="DrawingContext"/>: each tab gets a SteelBlue header band with a
        /// white bold label, followed by its three columns of rows (header / changed / normal
        /// backgrounds matching the on-screen styling). Drawing from the bound data — rather
        /// than capturing realized controls — sidesteps the TabControl single-presenter model
        /// so every tab is captured regardless of which one is selected.
        /// </summary>
        private RenderTargetBitmap CaptureTabsBitmap()
        {
            const double colBlockW = 380, setW = 150, profW = 100, rowH = 22, bandH = 30;
            const double totalW = colBlockW * 3;

            var tabs = new (string Label, ObservableCollection<SettingRow> L, ObservableCollection<SettingRow> M, ObservableCollection<SettingRow> R)[]
            {
                ("Vehicle", _vehicleL, _vehicleM, _vehicleR),
                ("Tool", _toolL, _toolM, _toolR),
                ("Environment", _environmentL, _environmentM, _environmentR),
            };

            double totalH = 0;
            foreach (var tab in tabs)
            {
                int maxRows = Math.Max(tab.L.Count, Math.Max(tab.M.Count, tab.R.Count));
                totalH += bandH + maxRows * rowH;
            }
            if (totalH < 1) totalH = 1;

            var headerTypeface = new Typeface(new FontFamily("Tahoma"), FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);
            var cellTypeface = new Typeface(new FontFamily("Tahoma"), FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

            var headerBg = new SolidColorBrush(Color.FromArgb(255, 200, 220, 240)); // #FFC8DCF0 section header
            var changedBg = new SolidColorBrush(Colors.LightYellow);
            var normalBg = new SolidColorBrush(Colors.WhiteSmoke);
            var bandBg = new SolidColorBrush(Colors.SteelBlue);

            var rtb = new RenderTargetBitmap(new PixelSize((int)totalW, (int)Math.Ceiling(totalH)), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.White, new Rect(0, 0, totalW, totalH), 0);

                double y = 0;
                foreach (var tab in tabs)
                {
                    ctx.FillRectangle(bandBg, new Rect(0, y, totalW, bandH), 0);
                    ctx.DrawText(
                        new FormattedText(tab.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, headerTypeface, 16, Brushes.White),
                        new Point(10, y + 5));
                    y += bandH;

                    int maxRows = Math.Max(tab.L.Count, Math.Max(tab.M.Count, tab.R.Count));
                    var cols = new[] { tab.L, tab.M, tab.R };
                    for (int c = 0; c < cols.Length; c++)
                    {
                        double x = c * colBlockW;
                        double rowY = y;
                        foreach (var row in cols[c])
                        {
                            IBrush bg = row.IsHeader ? headerBg : (row.IsChanged ? changedBg : normalBg);
                            ctx.FillRectangle(bg, new Rect(x, rowY, colBlockW, rowH), 0);

                            if (row.IsHeader)
                            {
                                ctx.DrawText(
                                    new FormattedText(row.Setting ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, headerTypeface, 13.333, Brushes.DarkBlue),
                                    new Point(x + 4, rowY + 3));
                            }
                            else
                            {
                                ctx.DrawText(
                                    new FormattedText(row.Setting ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, cellTypeface, 13.333, Brushes.Black),
                                    new Point(x + 4, rowY + 3));
                                ctx.DrawText(
                                    new FormattedText(row.Profile ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, cellTypeface, 13.333, Brushes.Black),
                                    new Point(x + setW + 4, rowY + 3));
                                ctx.DrawText(
                                    new FormattedText(row.Default ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, cellTypeface, 13.333, Brushes.Black),
                                    new Point(x + setW + profW + 4, rowY + 3));
                            }
                            rowY += rowH;
                        }
                    }
                    y += maxRows * rowH;
                }
            }
            return rtb;
        }

        /// <summary>
        /// [XPLAT] Best-effort copy of the composite PNG to the system clipboard. Image
        /// clipboard support varies per OS; any failure is swallowed and reported via the
        /// return value so the caller can fall back to writing the PNG to disk.
        /// </summary>
        // [XPLAT] Best-effort image-to-clipboard. Image clipboard support varies per OS; on
        // unsupported platforms this returns false so the caller can fall back to writing a PNG.
        // Uses the non-obsolete ClipboardExtensions.SetBitmapAsync (Avalonia.Input.Platform);
        // RenderTargetBitmap derives from Bitmap so it is passed directly.
        private async Task<bool> TrySetClipboardImageAsync(Bitmap bm)
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return false;

                await clipboard.SetBitmapAsync(bm);
                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormAllSettingsView clipboard image unsupported: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// [XPLAT] Opens a folder in the host file manager, replacing the WinForms
        /// <c>Process.Start("explorer.exe", dir)</c>. Uses the shell on Windows, and the
        /// <c>open</c> / <c>xdg-open</c> launchers on macOS / Linux. Failures degrade
        /// gracefully (logged, never thrown) per AAP §0.7.2.
        /// </summary>
        private static void OpenFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start(new ProcessStartInfo { FileName = "open", Arguments = "\"" + dir + "\"", UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = "\"" + dir + "\"", UseShellExecute = false });
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormAllSettingsView open folder failed: " + ex.Message);
            }
        }

        // ── Buttons ────────────────────────────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(RegistrySettings.baseDirectory ?? "", "AllSettings.csv");
                ExportToCSV(path);
                OpenFolder(RegistrySettings.baseDirectory);
                Log.EventWriter("View All Settings to CSV");
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormAllSettingsView CSV export failed: " + ex.Message);
            }
        }

        private void BtnCreatePNG_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(RegistrySettings.baseDirectory ?? "", "AllSet.PNG");
                using (var bm = CaptureTabsBitmap())
                using (var fs = File.Create(path))
                {
                    bm.Save(fs);
                }
                OpenFolder(RegistrySettings.baseDirectory);
                Log.EventWriter("View All Settings to PNG");
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormAllSettingsView PNG export failed: " + ex.Message);
            }
            Close();
        }

        private async void BtnScreenShot_Click(object sender, RoutedEventArgs e)
        {
            bool clipboardOk = false;
            // [XPLAT] The bitmap must stay alive for the duration of the async clipboard call
            // (and the fallback Save), so it is NOT wrapped in a 'using' before the await —
            // it is disposed in the finally block once all consumers are done.
            RenderTargetBitmap bm = null;
            try
            {
                bm = CaptureTabsBitmap();

                clipboardOk = await TrySetClipboardImageAsync(bm);
                if (!clipboardOk)
                {
                    // [XPLAT] fallback when image clipboard is unsupported: persist the PNG so
                    // the user still gets the artifact.
                    string path = Path.Combine(RegistrySettings.baseDirectory ?? "", "AllSet.PNG");
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    {
                        bm.Save(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormAllSettingsView screenshot failed: " + ex.Message);
            }
            finally
            {
                bm?.Dispose();
            }

            FormDialogView.Owner = this;
            if (clipboardOk)
                await FormDialogView.ShowAsync("Captured", "Copied to Clipboard, Paste (CTRL-V) in Telegram", DialogSeverity.Info);
            else
                await FormDialogView.ShowAsync("Captured", "Clipboard image not supported here — saved AllSet.PNG to the field storage folder instead.", DialogSeverity.Info);

            Log.EventWriter("View All Settings to Clipboard");
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>[XPLAT] Stop the one-second live-refresh timer when the window closes.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _timer?.Stop();
            base.OnClosing(e);
        }
    }
}

