// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the AutoSteer Configuration dialog. This is a faithful, 1:1
// BEHAVIOR-FROZEN reimplementation of the deleted WinForms Forms/Settings/FormSteer.cs (1363 lines):
// the steer-configuration PGN 251/252 encode/decode, the reset-to-defaults constants, and the
// per-tick PGN resend throttle are reproduced byte-for-byte (AAP §0.2.2 behavior-frozen contract;
// §0.6 parity). The companion markup FormSteerView.axaml declares only the ~194 named controls
// (no bindings, no markup-declared handlers); this partial reaches them by x:Name exactly as the
// WinForms designer + handlers did, and wires every event in code (see WireEvents()).
//
// The WinForms shell reached the rest of the program through a single FormGPS "mf" god-object. Per
// the migration's decoupling mandate (AAP §0.3.2 / §0.6.1) that back-reference is replaced by a
// small set of constructor-injected collaborator interfaces (vehicle / steerCfg / telemetry /
// smartWAS) plus the owning Window. The interfaces are declared locally with steer-settings-unique
// names so they never collide with the differently-shaped ISteerTelemetry already declared by
// FormGraphSteerView nor the ISteerWiz* family declared by FormSteerWizView in this same namespace;
// the concrete implementations are owned by the application host / extracted services. A
// parameterless constructor (required by Avalonia's compiled-XAML loader) chains to the injected one
// with inert Null collaborators so the view still loads in the designer/previewer.
//
// [XPLAT] Culture parity (AAP §0.6.5 — highest data-integrity rule): EVERY double/int ToString and
// Parse is pinned to CultureInfo.InvariantCulture (exposed here as CI) so a comma-decimal locale on
// Linux/macOS cannot corrupt the PGN-derived text, the calibration read-outs, or the persisted
// settings.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AgLibrary.Logging;
using AgOpenGPS;                    // vec3, glm (GPS/Classes) — geometry helpers used by the SA loop
using AgOpenGPS.Core.Models;        // VehicleConfig, Speed
using AgOpenGPS.Core.Translations;  // gStr
using AgOpenGPS.Helpers;            // ScreenHelper (on-screen recovery)
using AgOpenGPS.Properties;         // VehicleSettings, ToolSettings
using AgOpenGPS.Views;              // FormNumeric, FormDialogView, FormTimedMessageView, DialogSeverity

namespace AgOpenGPS.Views.Settings
{
    // ===================================================================================================
    // Collaborator interfaces (steer-settings-local). These replace the WinForms FormGPS "mf"
    // back-reference. Steer-settings-unique names avoid colliding with FormGraphSteerView's
    // ISteerTelemetry and FormSteerWizView's ISteerWiz* family (all in this namespace). The Services
    // agent / application host owns the concrete implementations.
    // ===================================================================================================

    /// <summary>[XPLAT] Mutable vehicle guidance parameters the steer dialog reads/writes (was
    /// <c>mf.vehicle</c>, a Core <c>CVehicle</c>). <see cref="VehicleConfig"/> is the real Core geometry
    /// object used by the steer-angle / diameter calculation.</summary>
    public interface ISteerSettingsVehicle
    {
        double maxSteerAngle { get; set; }
        double maxSteerSpeed { get; set; }
        double minSteerSpeed { get; set; }
        double functionSpeedLimit { get; set; }
        double driveFreeSteerAngle { get; set; }
        double stanleyDistanceErrorGain { get; set; }
        double stanleyHeadingErrorGain { get; set; }
        double stanleyIntegralGainAB { get; set; }
        double purePursuitIntegralGain { get; set; }
        double goalPointLookAheadHold { get; set; }
        double goalPointLookAheadMult { get; set; }
        double goalPointAcquireFactor { get; set; }
        double uturnCompensation { get; set; }
        double modeXTE { get; set; }
        int modeTime { get; set; }
        int deadZoneHeading { get; set; }
        int deadZoneDelay { get; set; }
        bool isInFreeDriveMode { get; set; }
        double goalDistance { get; }
        VehicleConfig VehicleConfig { get; }
    }

    /// <summary>[XPLAT] Steer-config PGN 0xFC (252) frame + named byte indices. Mirrors the Core
    /// CPGN_FC object the WinForms form reached as <c>mf.p_252</c> (<c>.pgn</c> + index fields).</summary>
    public interface ISteerSettingsPgn252
    {
        byte[] Pgn { get; }
        int GainProportional { get; }
        int HighPWM { get; }
        int LowPWM { get; }
        int MinPWM { get; }
        int CountsPerDegree { get; }
        int WasOffsetLo { get; }
        int WasOffsetHi { get; }
        int Ackerman { get; }
    }

    /// <summary>[XPLAT] Steer-config PGN 0xFB (251) frame + named byte indices. Mirrors the Core
    /// CPGN_FB object the WinForms form reached as <c>mf.p_251</c>. Includes <c>AngVel</c> (index 9),
    /// which FormSteer writes (the wizard did not).</summary>
    public interface ISteerSettingsPgn251
    {
        byte[] Pgn { get; }
        int Set0 { get; }
        int MaxPulse { get; }
        int MinSpeed { get; }
        int Set1 { get; }
        int AngVel { get; }
    }

    /// <summary>[XPLAT] Steer-configuration transport + the misc guidance/AB-line state the form pushes
    /// to. Replaces <c>mf.p_252</c>/<c>mf.p_251</c>/<c>mf.SendPgnToLoop</c>, plus
    /// <c>mf.gyd.sideHillCompFactor</c>, <c>mf.ABLine.lineWidth</c>/<c>snapDistance</c>, and the
    /// reset-time <c>mf.vehicle = new CVehicle(mf)</c> (exposed as <see cref="ResetVehicle"/>).</summary>
    public interface ISteerSettingsConfigService
    {
        ISteerSettingsPgn252 P252 { get; }
        ISteerSettingsPgn251 P251 { get; }
        void SendPgnToLoop(byte[] pgn);
        double SideHillCompFactor { get; set; }
        int LineWidth { get; set; }
        double SnapDistance { get; set; }
        ISteerSettingsVehicle ResetVehicle();
    }

    /// <summary>[XPLAT] Live steer/telemetry read-outs plus the scalar program flags the dialog reads
    /// and toggles (was <c>mf.mc.*</c> and assorted <c>mf</c> members). The telemetry getters are
    /// snapshots driven by the receive pipeline; the flag setters write the same shared program state.</summary>
    public interface ISteerSettingsTelemetry
    {
        double actualSteerAngleDegrees { get; }
        int pwmDisplay { get; }
        int sensorData { get; set; }
        string SetSteerAngle { get; }
        double guidanceLineSteerAngle { get; }
        double actAngVel { get; }
        double setAngVel { get; }
        vec3 pivotAxlePos { get; }
        double cm2CmOrIn { get; }
        double inOrCm2Cm { get; }
        double lightbarCmPerPixel { get; set; }
        double guidanceLookAheadTime { get; set; }
        string unitsInCm { get; }
        bool isStanleyUsed { get; set; }
        bool isSteerInReverse { get; set; }
        bool isLightbarOn { get; set; }
        bool isLightBarNotSteerBar { get; set; }
        bool isMetric { get; }
        bool isEasyDriveMode { get; }
    }

    /// <summary>[XPLAT] Smart-WAS auto-calibration collector the WAS-zero panel drives (was
    /// <c>mf.smartWAS</c>).</summary>
    public interface ISteerSettingsSmartWAS
    {
        void Reset();
        void Start();
        void Stop();
        void ApplyOffsetCorrection(double recommendedOffset);
        bool HasValidCalibration { get; }
        bool IsCollecting { get; }
        double RecommendedOffset { get; }
        double Confidence { get; }
        int SampleCount { get; }
    }

    // --- Inert Null implementations (designer/previewer path only; never wired to real hardware) -------

    internal sealed class NullSteerSettingsPgn252 : ISteerSettingsPgn252
    {
        // [XPLAT] CPGN_FC (252) layout — header 0x80 0x81 0x7F 0xFC, length 8, trailing CRC slot.
        public byte[] Pgn { get; } = new byte[] { 0x80, 0x81, 0x7f, 0xFC, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int GainProportional => 5;
        public int HighPWM => 6;
        public int LowPWM => 7;
        public int MinPWM => 8;
        public int CountsPerDegree => 9;
        public int WasOffsetLo => 10;
        public int WasOffsetHi => 11;
        public int Ackerman => 12;
    }

    internal sealed class NullSteerSettingsPgn251 : ISteerSettingsPgn251
    {
        // [XPLAT] CPGN_FB (251) layout — header 0x80 0x81 0x7F 0xFB, length 8, trailing CRC slot.
        public byte[] Pgn { get; } = new byte[] { 0x80, 0x81, 0x7f, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int Set0 => 5;
        public int MaxPulse => 6;
        public int MinSpeed => 7;
        public int Set1 => 8;
        public int AngVel => 9;
    }

    internal sealed class NullSteerSettingsVehicle : ISteerSettingsVehicle
    {
        public double maxSteerAngle { get; set; }
        public double maxSteerSpeed { get; set; }
        public double minSteerSpeed { get; set; }
        public double functionSpeedLimit { get; set; }
        public double driveFreeSteerAngle { get; set; }
        public double stanleyDistanceErrorGain { get; set; }
        public double stanleyHeadingErrorGain { get; set; }
        public double stanleyIntegralGainAB { get; set; }
        public double purePursuitIntegralGain { get; set; }
        public double goalPointLookAheadHold { get; set; }
        public double goalPointLookAheadMult { get; set; }
        public double goalPointAcquireFactor { get; set; }
        public double uturnCompensation { get; set; }
        public double modeXTE { get; set; }
        public int modeTime { get; set; }
        public int deadZoneHeading { get; set; }
        public int deadZoneDelay { get; set; }
        public bool isInFreeDriveMode { get; set; }
        public double goalDistance => 0;
        // VehicleConfig is a Core value object with a parameterless ctor; safe to allocate for previews.
        public VehicleConfig VehicleConfig { get; } = new VehicleConfig();
    }

    internal sealed class NullSteerSettingsConfigService : ISteerSettingsConfigService
    {
        public ISteerSettingsPgn252 P252 { get; } = new NullSteerSettingsPgn252();
        public ISteerSettingsPgn251 P251 { get; } = new NullSteerSettingsPgn251();
        public void SendPgnToLoop(byte[] pgn) { /* no-op: no loopback in the designer/previewer path */ }
        public double SideHillCompFactor { get; set; }
        public int LineWidth { get; set; }
        public double SnapDistance { get; set; }
        public ISteerSettingsVehicle ResetVehicle() => new NullSteerSettingsVehicle();
    }

    internal sealed class NullSteerSettingsTelemetry : ISteerSettingsTelemetry
    {
        public double actualSteerAngleDegrees => 0;
        public int pwmDisplay => 0;
        public int sensorData { get; set; } = -1;
        public string SetSteerAngle => "0";
        public double guidanceLineSteerAngle => 0;
        public double actAngVel => 0;
        public double setAngVel => 0;
        public vec3 pivotAxlePos => new vec3(0, 0, 0);
        public double cm2CmOrIn => 1;
        public double inOrCm2Cm => 1;
        public double lightbarCmPerPixel { get; set; }
        public double guidanceLookAheadTime { get; set; }
        public string unitsInCm => "cm";
        public bool isStanleyUsed { get; set; }
        public bool isSteerInReverse { get; set; }
        public bool isLightbarOn { get; set; }
        public bool isLightBarNotSteerBar { get; set; }
        public bool isMetric => true;
        public bool isEasyDriveMode => false;
    }

    internal sealed class NullSteerSettingsSmartWAS : ISteerSettingsSmartWAS
    {
        public void Reset() { }
        public void Start() { }
        public void Stop() { }
        public void ApplyOffsetCorrection(double recommendedOffset) { }
        public bool HasValidCalibration => false;
        public bool IsCollecting => false;
        public double RecommendedOffset => 0;
        public double Confidence => 0;
        public int SampleCount => 0;
    }

    // ===================================================================================================
    // The AutoSteer Configuration dialog.
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Avalonia code-behind for the AutoSteer Configuration dialog — a behavior-frozen 1:1 port
    /// of WinForms <c>FormSteer</c>. Reaches its ~194 controls by x:Name and wires every event in code.
    /// </summary>
    public partial class FormSteerView : Window
    {
        // [XPLAT] InvariantCulture for every numeric ToString/Parse (AAP §0.6.5).
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // Injected collaborators (replace the WinForms FormGPS "mf" god-object).
        private ISteerSettingsVehicle vehicle;
        private readonly ISteerSettingsConfigService steerCfg;
        private readonly ISteerSettingsTelemetry tel;
        private readonly ISteerSettingsSmartWAS smartWAS;
        private readonly Window owner;

        // [XPLAT] QA Issue 3 — production launcher for the Steer/WAS calibration wizard, injected by the
        // composition root (App.axaml.cs) so the "Wizard" button opens the REAL-adapter FormSteerWizView via
        // the same ShowEditorDialog path as the MainView entry point. Null only on the designer/previewer
        // (parameterless) path, where btnSteerWizard_Click falls back to the inert preview wizard.
        private readonly Action openSteerWizard;

        // State fields — mirror the WinForms originals exactly (FormSteer L19-L23).
        private bool toSend = false, isSA = false;
        private int counter = 0, secondCntr = 0, cntr;
        private vec3 startFix;
        private double diameter, steerAngleRight, dist;
        private int windowSizeState = 0;

        // [XPLAT] In WinForms the combos were wired to .Click and the sensor bar to .Scroll, so
        // assigning their value programmatically during load did NOT raise the "settings changed"
        // alert. Avalonia raises SelectionChanged/ValueChanged on programmatic changes, so the bulk
        // load is bracketed by this guard and EnableAlert_Click / hsbarSensor_Scroll no-op while it holds.
        private bool isLoading;

        private readonly DispatcherTimer timer1;

        // [XPLAT] WinForms NumericUpDown ("nud*") controls have no Avalonia equivalent; the markup uses
        // Buttons that open a numeric keypad (FormNumeric). Each nud's live value + edit range + decimal
        // places are tracked here, and the button Content shows the formatted value.
        private double nudMaxCountsValue, nudMinSteerSpeedValue, nudMaxSteerSpeedValue, nudGuidanceSpeedLimitValue;
        private double nudSnapDistanceValue, nudGuidanceLookAheadValue, nudLineWidthValue, nudcmPerPixelValue;
        private double nudDeadZoneHeadingValue, nudDeadZoneDelayValue;

        // nudSnapDistance edit range is derived in the ctor (imperial /2.54 conversion, reproduced verbatim).
        private decimal nudSnapDistanceMin = 0M, nudSnapDistanceMax = 1000M;
        private int nudSnapDistanceDecimals = 0;

        /// <summary>[XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader; chains
        /// to the injected constructor with inert Null collaborators (designer/previewer path).</summary>
        public FormSteerView()
            : this(new NullSteerSettingsVehicle(), new NullSteerSettingsConfigService(),
                   new NullSteerSettingsTelemetry(), new NullSteerSettingsSmartWAS(), null)
        {
        }

        /// <summary>
        /// [XPLAT] Primary constructor. Parameter names mirror the original form's collaborators
        /// (vehicle / steerCfg / tel / smartWAS) plus the owning window for dialog parenting.
        /// Faithful port of the WinForms <c>FormSteer(Form callingForm)</c> ctor (source L26-L107).
        /// </summary>
        public FormSteerView(
            ISteerSettingsVehicle vehicle,
            ISteerSettingsConfigService steerCfg,
            ISteerSettingsTelemetry tel,
            ISteerSettingsSmartWAS smartWAS,
            Window owner,
            Action openSteerWizard = null)
        {
            this.vehicle = vehicle ?? new NullSteerSettingsVehicle();
            this.steerCfg = steerCfg ?? new NullSteerSettingsConfigService();
            this.tel = tel ?? new NullSteerSettingsTelemetry();
            this.smartWAS = smartWAS ?? new NullSteerSettingsSmartWAS();
            this.owner = owner;
            // [XPLAT] QA Issue 3 — the real-adapter wizard launcher (null on the designer/previewer path).
            this.openSteerWizard = openSteerWizard;

            InitializeComponent();

            // [XPLAT] The WinForms ctor disabled the embedded textbox of each NumericUpDown
            // (nud*.Controls[0].Enabled = false). Avalonia's keypad buttons have no embedded textbox,
            // so those lines are intentionally dropped.

            // Imperial conversion of the snap-distance edit range, reproduced VERBATIM including the
            // original's repeated division (the WinForms code divides Maximum/Minimum by 2.54 twice;
            // the second pass is a latent quirk that is preserved for byte-faithful range parity).
            nudSnapDistanceMax = Math.Round(nudSnapDistanceMax / 2.54M);
            nudSnapDistanceMin = Math.Round(nudSnapDistanceMin / 2.54M);
            nudSnapDistanceMin = Math.Round(nudSnapDistanceMin / 2.54M);
            nudSnapDistanceMax = Math.Round(nudSnapDistanceMax / 2.54M);

            // translate (source L46-L73)
            Title = gStr.gsAutoSteerConfiguration;
            labelFast.Text = gStr.gsFast;
            labelSteerResponse.Text = gStr.gsSteerResponse;
            labelSlow.Text = gStr.gsSlow;
            labelIntegralPP.Text = gStr.gsIntegral;
            labelIntegralInfo.Text = gStr.gsIntegralInfo;
            labelSteerAngle.Text = gStr.gsSteerAngle;
            labelDiameter.Text = gStr.gsDiameter;
            labelDistance.Text = gStr.gsAgressiveness;
            labelHeading.Text = gStr.gsOvershootReduction;
            labelIntergralStanley.Text = gStr.gsIntegral;
            labelProportionalGain.Text = gStr.gsProportionalGain;
            labelMaxLimit.Text = gStr.gsMaxLimit;
            labelMinToMove.Text = gStr.gsMinToMove;
            labelWasZero.Text = gStr.gsWasZero;
            labelCountsPerDegree.Text = gStr.gsCountsPerDegree;
            labelAckermann.Text = gStr.gsAckermann;
            labelMaxSteerAngle.Text = gStr.gsMaxSteerAngle;
            labelDeadzone.Text = gStr.gsDeadzone;
            labelHeadingDegree.Text = gStr.gsHeading;
            labelOnDelay.Text = gStr.gsOnDelay;
            labelSpeedFactor.Text = gStr.gsSpeedFactor;
            labelAcquireFactor.Text = gStr.gsAcquireFactor;
            labelAcquireDescription.Text = $"{gStr.gsAcquire} = {gStr.gsFactor} * {gStr.gsHold}";
            labelDist.Text = gStr.gsDistance;
            labelAcquire2.Text = gStr.gsAcquire;
            labelHold.Text = gStr.gsHold;

            // translate pop-out (source L75-L103)
            labelEncoder.Text = gStr.gsTurnSensor;
            labelTurnSensor.Text = gStr.gsTurnSensor;
            labelPressureTurnSensor.Text = gStr.gsPressureTurnSensor;
            labelCurrentTurnSensor.Text = gStr.gsCurrentTurnSensor;
            labelInvertWas.Text = gStr.gsInvertWas;
            labelInvertMotor.Text = gStr.gsInvertMotor;
            labelInvertRelays.Text = gStr.gsInvertRelays;
            labelMotorDriver.Text = gStr.gsMotorDriver;
            labelADConverter.Text = gStr.gsADConverter;
            labelIMUAxis.Text = gStr.gsIMUAxis;
            labelSteerEnable.Text = gStr.gsSteerEnable;
            labelSteerDescription.Text = $"{gStr.gsButton} - {gStr.gsButtonDescription}\n{gStr.gsSwitch} - {gStr.gsSwitchDescription}";
            labelUturnCompensation.Text = gStr.gsUturnCompensation;
            labelSideHill.Text = gStr.gsSideHillComp;
            labelSteerInReverse.Text = gStr.gsSteerInReverse;
            labelManualTurns.Text = gStr.gsManualTurns;
            labelMinSpeed.Text = gStr.gsMinSpeed;
            labelMaxSpeed.Text = gStr.gsMaxSpeed;
            labelLineWidth.Text = gStr.gsLineWidth;
            labelNudgeDistance.Text = gStr.gsNudgeDistance;
            labelNextGuidanceLine.Text = gStr.gsNextGuidanceLine;
            labelCmPix.Text = $"{gStr.gsCm} -> {gStr.gsPixel}";
            labelOnOff.Text = $"{gStr.gsOn}/{gStr.gsOff}";
            labelLightbar.Text = gStr.gsLightbar;
            labelSteerBar.Text = gStr.gsSteerBar;
            labelWizard.Text = gStr.gsWizard;
            labelReset.Text = gStr.gsReset;
            labelSendAndSave.Text = gStr.gsSendAndSave;

            // Initial compact size (source L105-L106).
            Width = 388;
            Height = 490;

            // [XPLAT] timer1.Interval = 250 (designer).
            timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer1.Tick += Timer1_Tick;

            WireEvents();
        }

        /// <summary>
        /// [XPLAT] Subscribes every control event in code, reproducing the WinForms designer's event
        /// wiring exactly (the partner .axaml declares no handlers). Grouped to match the original
        /// handler sharing (EnableAlert_Click and expandWindow_Click are each shared by many controls).
        /// </summary>
        private void WireEvents()
        {
            // Action buttons (.Click).
            btnClose.Click += btnClose_Click;
            btnExpand.Click += expandWindow_Click;
            btnFreeDrive.Click += btnFreeDrive_Click;
            btnFreeDriveZero.Click += btnFreeDriveZero_Click;
            btnSendSteerConfigPGN.Click += btnSendSteerConfigPGN_Click;
            btnSmartZeroWAS.Click += btnSmartZeroWAS_Click;
            btnStanleyPure.Click += btnStanleyPure_Click;
            btnStartSA.Click += btnStartSA_Click;
            btnSteerWizard.Click += btnSteerWizard_Click;
            btnZeroWAS.Click += btnZeroWAS_Click;
            button2.Click += btnVehicleReset_Click;

            // NumericUpDown keypad buttons (.Click).
            nudDeadZoneDelay.Click += nudDeadZoneDelay_Click;
            nudDeadZoneHeading.Click += nudDeadZoneHeading_Click;
            nudGuidanceLookAhead.Click += nudGuidanceLookAhead_Click;
            nudGuidanceSpeedLimit.Click += nudGuidanceSpeedLimit_Click;
            nudLineWidth.Click += nudLineWidth_Click;
            nudMaxCounts.Click += nudMaxCounts_Click;
            nudMaxSteerSpeed.Click += nudMaxSteerSpeed_Click;
            nudMinSteerSpeed.Click += nudMinSteerSpeed_Click;
            nudSnapDistance.Click += nudSnapDistance_Click;
            nudcmPerPixel.Click += nudcmPerPixel_Click;

            // Free-drive nudge RepeatButtons (WinForms MouseDown -> Avalonia PointerPressed).
            btnSteerAngleUp.PointerPressed += btnSteerAngleUp_MouseDown;
            btnSteerAngleDown.PointerPressed += btnSteerAngleDown_MouseDown;

            // ComboBoxes (WinForms .Click -> Avalonia .SelectionChanged). They share EnableAlert_Click;
            // a lambda forwards the SelectionChangedEventArgs to the shared RoutedEventArgs handler.
            cboxConv.SelectionChanged += (s, e) => EnableAlert_Click(s, e);
            cboxMotorDrive.SelectionChanged += (s, e) => EnableAlert_Click(s, e);
            cboxSteerEnable.SelectionChanged += (s, e) => EnableAlert_Click(s, e);
            cboxXY.SelectionChanged += (s, e) => EnableAlert_Click(s, e);

            // CheckBoxes (.Click).
            cboxCurrentSensor.Click += EnableAlert_Click;
            cboxDanfoss.Click += EnableAlert_Click;
            cboxEncoder.Click += EnableAlert_Click;
            cboxPressureSensor.Click += EnableAlert_Click;
            chkInvertSteer.Click += EnableAlert_Click;
            chkInvertWAS.Click += EnableAlert_Click;
            chkSteerInvertRelays.Click += EnableAlert_Click;
            cboxSteerInReverse.Click += cboxSteerInReverse_Click;
            chkDisplayLightbar.Click += chkDisplayLightbar_Click;

            // Sliders (.ValueChanged). NOTE: hsbarWasOffset is wired to the SensorZero handler exactly as
            // the WinForms designer did; hsbarSensor uses the (renamed) Scroll handler.
            hsbarAckerman.ValueChanged += hsbarAckerman_ValueChanged;
            hsbarAcquireFactor.ValueChanged += hsbarAcquireFactor_ValueChanged;
            hsbarCountsPerDegree.ValueChanged += hsbarCountsPerDegree_ValueChanged;
            hsbarHeadingErrorGain.ValueChanged += hsbarHeadingErrorGain_ValueChanged;
            hsbarHighSteerPWM.ValueChanged += hsbarHighSteerPWM_ValueChanged;
            hsbarHoldLookAhead.ValueChanged += hsbarHoldLookAhead_ValueChanged;
            hsbarIntegral.ValueChanged += hsbarIntegral_ValueChanged;
            hsbarIntegralPurePursuit.ValueChanged += hsbarIntegralPurePursuit_ValueChanged;
            hsbarLookAheadMult.ValueChanged += hsbarLookAheadMult_ValueChanged;
            hsbarMaxSteerAngle.ValueChanged += hsbarMaxSteerAngle_ValueChanged;
            hsbarMinPWM.ValueChanged += hsbarMinPWM_ValueChanged;
            hsbarProportionalGain.ValueChanged += hsbarProportionalGain_ValueChanged;
            hsbarSensor.ValueChanged += hsbarSensor_Scroll;
            hsbarSideHillComp.ValueChanged += hsbarSideHillComp_ValueChanged;
            hsbarStanleyGain.ValueChanged += hsbarStanleyGain_ValueChanged;
            hsbarUTurnCompensation.ValueChanged += hsbarUTurnCompensation_ValueChanged;
            hsbarWasOffset.ValueChanged += hsbarSteerAngleSensorZero_ValueChanged;

            // Lightbar / steerbar radio buttons (.Click — not raised by programmatic IsChecked changes).
            rbtnLightBar.Click += rbtnLightBar_Click;
            rbtnSteerBar.Click += rbtnSteerBar_Click;

            // Labels that act as a hit-target to expand/collapse the window (WinForms .Click ->
            // Avalonia PointerPressed on the TextBlocks).
            label11.PointerPressed += expandWindow_PointerPressed;
            label12.PointerPressed += expandWindow_PointerPressed;
            label13.PointerPressed += expandWindow_PointerPressed;
            lblError.PointerPressed += expandWindow_PointerPressed;
            lblSteerAngle.PointerPressed += expandWindow_PointerPressed;
            lblSteerAngleActual.PointerPressed += expandWindow_PointerPressed;

            // Tab Enter/Leave (WinForms TabPage.Enter/.Leave -> Avalonia TabControl.SelectionChanged,
            // dispatched per added/removed item for the three tracked tabs).
            tabControl1.SelectionChanged += TabControl_SelectionChanged;
            tabSteerSettings.SelectionChanged += TabControl_SelectionChanged;
        }

        // ===============================================================================================
        // Lifecycle: OnOpened (WinForms FormSteer_Load) and OnClosing (WinForms FormSteer_FormClosing).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>Load</c> handler -> Avalonia <c>OnOpened</c>.</summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadSettingsToUi();
            FireInitialTabEnter();
            timer1.Start();
        }

        /// <summary>
        /// [XPLAT] Faithful port of WinForms <c>FormSteer_Load</c> (source L109-L336). Reproduces the
        /// original's slider handler detach/reattach choreography exactly: only the six PGN sliders
        /// (WAS-offset, counts-per-degree, ackerman, min-PWM, P-gain, high-PWM) are detached while their
        /// value is assigned; the remaining sliders stay attached so their ValueChanged handlers run and
        /// push the loaded values into <c>vehicle.*</c> (the original's intentional Int16-quantised
        /// round-trip). The bulk load is bracketed by <see cref="isLoading"/> so the combo/sensor "alert"
        /// handlers no-op (WinForms used .Click/.Scroll, which programmatic changes never raised).
        /// </summary>
        private void LoadSettingsToUi()
        {
            isLoading = true;

            vehicle.goalPointLookAheadHold = ToolSettings.Default.setVehicle_goalPointLookAheadHold;
            cboxSteerInReverse.IsChecked = VehicleSettings.Default.setAS_isSteerInReverse;

            SetButtonImage(btnStanleyPure, tel.isStanleyUsed ? "ModeStanley.png" : "ModePurePursuit.png");

            // Mode tabs: Stanley hides PP/PPAdv, Pure Pursuit hides Stan (matches WinForms ItemSize too).
            if (tel.isStanleyUsed)
            {
                tabControl1.Items.Remove(tabPP);
                tabControl1.Items.Remove(tabPPAdv);
                SetTabHeaderSize(tabControl1, 105, 48);
            }
            else
            {
                tabControl1.Items.Remove(tabStan);
                SetTabHeaderSize(tabControl1, 89, 48);
            }

            // Restore window location (WinForms Point -> Avalonia PixelPoint).
            var loc = AgOpenGPS.Properties.Settings.Default.setWindow_steerSettingsLocation;
            Position = new PixelPoint(loc.X, loc.Y);

            // WAS-zero + counts-per-degree (handlers detached during assignment).
            hsbarWasOffset.ValueChanged -= hsbarSteerAngleSensorZero_ValueChanged;
            hsbarCountsPerDegree.ValueChanged -= hsbarCountsPerDegree_ValueChanged;

            hsbarWasOffset.Value = VehicleSettings.Default.setAS_wasOffset;
            hsbarCountsPerDegree.Value = VehicleSettings.Default.setAS_countsPerDegree;

            lblCountsPerDegree.Text = ((int)hsbarCountsPerDegree.Value).ToString(CI);
            lblSteerAngleSensorZero.Text = (hsbarWasOffset.Value / (double)hsbarCountsPerDegree.Value).ToString("N2", CI);

            hsbarWasOffset.ValueChanged += hsbarSteerAngleSensorZero_ValueChanged;
            hsbarCountsPerDegree.ValueChanged += hsbarCountsPerDegree_ValueChanged;

            // Ackerman (handler detached during assignment).
            hsbarAckerman.ValueChanged -= hsbarAckerman_ValueChanged;
            hsbarAckerman.Value = VehicleSettings.Default.setAS_ackerman;
            lblAckerman.Text = ((int)hsbarAckerman.Value).ToString(CI);
            hsbarAckerman.ValueChanged += hsbarAckerman_ValueChanged;

            // Min PWM + P gain (handlers detached during assignment).
            hsbarMinPWM.ValueChanged -= hsbarMinPWM_ValueChanged;
            hsbarProportionalGain.ValueChanged -= hsbarProportionalGain_ValueChanged;

            hsbarMinPWM.Value = VehicleSettings.Default.setAS_minSteerPWM;
            lblMinPWM.Text = ((int)hsbarMinPWM.Value).ToString(CI);

            hsbarProportionalGain.Value = VehicleSettings.Default.setAS_Kp;
            lblProportionalGain.Text = ((int)hsbarProportionalGain.Value).ToString(CI);

            hsbarMinPWM.ValueChanged += hsbarMinPWM_ValueChanged;
            hsbarProportionalGain.ValueChanged += hsbarProportionalGain_ValueChanged;

            // High steer PWM (handler detached during assignment).
            hsbarHighSteerPWM.ValueChanged -= hsbarHighSteerPWM_ValueChanged;
            hsbarHighSteerPWM.Value = VehicleSettings.Default.setAS_highSteerPWM;
            lblHighSteerPWM.Text = ((int)hsbarHighSteerPWM.Value).ToString(CI);
            hsbarHighSteerPWM.ValueChanged += hsbarHighSteerPWM_ValueChanged;

            // Max steer angle (handler ATTACHED -> fires, sets vehicle.maxSteerAngle).
            hsbarMaxSteerAngle.Value = (short)VehicleSettings.Default.setVehicle_maxSteerAngle;
            lblMaxSteerAngle.Text = ((int)hsbarMaxSteerAngle.Value).ToString(CI);

            // Stanley distance gain (handler ATTACHED).
            vehicle.stanleyDistanceErrorGain = ToolSettings.Default.stanleyDistanceErrorGain;
            hsbarStanleyGain.Value = (short)(vehicle.stanleyDistanceErrorGain * 10);
            lblStanleyGain.Text = vehicle.stanleyDistanceErrorGain.ToString(CI);

            // Stanley heading gain (handler ATTACHED).
            vehicle.stanleyHeadingErrorGain = ToolSettings.Default.stanleyHeadingErrorGain;
            hsbarHeadingErrorGain.Value = (short)(vehicle.stanleyHeadingErrorGain * 10);
            lblHeadingErrorGain.Text = vehicle.stanleyHeadingErrorGain.ToString(CI);

            // Stanley integral (handler ATTACHED).
            vehicle.stanleyIntegralGainAB = ToolSettings.Default.stanleyIntegralGainAB;
            hsbarIntegral.Value = (int)(ToolSettings.Default.stanleyIntegralGainAB * 100);
            lblIntegralPercent.Text = ((int)(vehicle.stanleyIntegralGainAB * 100)).ToString(CI);

            // Pure Pursuit integral (handler ATTACHED).
            vehicle.purePursuitIntegralGain = ToolSettings.Default.purePursuitIntegralGainAB;
            hsbarIntegralPurePursuit.Value = (int)(ToolSettings.Default.purePursuitIntegralGainAB * 100);
            lblPureIntegral.Text = ((int)(vehicle.purePursuitIntegralGain * 100)).ToString(CI);

            // Side-hill compensation (handler ATTACHED -> sets gyd factor + lblSideHillComp).
            steerCfg.SideHillCompFactor = VehicleSettings.Default.setAS_sideHillComp;
            hsbarSideHillComp.Value = (int)(VehicleSettings.Default.setAS_sideHillComp * 100);

            // Pure Pursuit hold look-ahead (handler ATTACHED).
            vehicle.goalPointLookAheadHold = ToolSettings.Default.setVehicle_goalPointLookAheadHold;
            hsbarHoldLookAhead.Value = (short)(vehicle.goalPointLookAheadHold * 10);
            lblHoldLookAhead.Text = vehicle.goalPointLookAheadHold.ToString(CI);

            // Look-ahead multiplier (handler ATTACHED).
            hsbarLookAheadMult.Value = (short)(ToolSettings.Default.setVehicle_goalPointLookAheadMult * 10);
            lblLookAheadMult.Text = vehicle.goalPointLookAheadMult.ToString(CI);

            // Acquire factor (handler ATTACHED).
            hsbarAcquireFactor.Value = (int)(ToolSettings.Default.setVehicle_goalPointAcquireFactor * 100);
            lblAcquireFactor.Text = vehicle.goalPointAcquireFactor.ToString(CI);

            lblAcquirePP.Text = (vehicle.goalPointLookAheadHold * vehicle.goalPointAcquireFactor).ToString("N1", CI);

            // U-turn compensation (handler ATTACHED).
            hsbarUTurnCompensation.Value = (short)(AgOpenGPS.Properties.Settings.Default.setAS_uTurnCompensation * 10);
            lblUTurnCompensation.Text = ((int)(hsbarUTurnCompensation.Value - 10)).ToString(CI);

            // Make sure free drive is off.
            SetButtonImage(btnFreeDrive, "SteerDriveOff.png");
            vehicle.isInFreeDriveMode = false;
            btnSteerAngleDown.IsEnabled = false;
            btnSteerAngleUp.IsEnabled = false;
            vehicle.driveFreeSteerAngle = 0;

            nudDeadZoneHeadingValue = (double)ToolSettings.Default.setAS_deadZoneHeading / 100;
            SetNud(nudDeadZoneHeading, nudDeadZoneHeadingValue, 1);
            nudDeadZoneDelayValue = vehicle.deadZoneDelay;
            SetNud(nudDeadZoneDelay, nudDeadZoneDelayValue, 0);

            toSend = false;

            // --- PGN setting0 DECODE (source L231-L253) — inverse of the SaveSettings bitfield builder. ---
            int sett = VehicleSettings.Default.setArdSteer_setting0;

            chkInvertWAS.IsChecked = (sett & 1) != 0;
            chkSteerInvertRelays.IsChecked = (sett & 2) != 0;
            chkInvertSteer.IsChecked = (sett & 4) != 0;
            SetCombo(cboxConv, (sett & 8) == 0 ? "Differential" : "Single");
            SetCombo(cboxMotorDrive, (sett & 16) == 0 ? "IBT2" : "Cytron");

            if ((sett & 32) == 32) SetCombo(cboxSteerEnable, "Switch");
            else if ((sett & 64) == 64) SetCombo(cboxSteerEnable, "Button");
            else SetCombo(cboxSteerEnable, "None");

            cboxEncoder.IsChecked = (sett & 128) != 0;

            nudMaxCountsValue = VehicleSettings.Default.setArdSteer_maxPulseCounts;
            SetNud(nudMaxCounts, nudMaxCountsValue, 0);
            hsbarSensor.Value = (int)VehicleSettings.Default.setArdSteer_maxPulseCounts;
            lblhsbarSensor.Text = ((int)((double)hsbarSensor.Value * 0.3921568627)).ToString(CI) + "%";

            // --- PGN setting1 DECODE (source L259-L271). ---
            sett = VehicleSettings.Default.setArdSteer_setting1;

            cboxDanfoss.IsChecked = (sett & 1) != 0;
            SetCombo(cboxXY, (sett & 8) == 0 ? "X" : "Y");
            cboxPressureSensor.IsChecked = (sett & 2) != 0;
            cboxCurrentSensor.IsChecked = (sett & 4) != 0;

            // --- Sensor-type mutual exclusion + visibility (source L273-L322). ---
            if (IsChk(cboxEncoder))
            {
                cboxPressureSensor.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                labelTurnSensor.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = true;
                pbarSensor.IsVisible = false;
                hsbarSensor.IsVisible = false;
                lblhsbarSensor.IsVisible = false;
                labelTurnSensor.Text = gStr.gsEncoderCounts;
            }
            else if (IsChk(cboxPressureSensor))
            {
                cboxEncoder.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                labelTurnSensor.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = true;
                hsbarSensor.IsVisible = true;
                lblhsbarSensor.IsVisible = true;
                labelTurnSensor.Text = "Off at %";
            }
            else if (IsChk(cboxCurrentSensor))
            {
                cboxPressureSensor.IsChecked = false;
                cboxEncoder.IsChecked = false;
                labelTurnSensor.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = true;
                hsbarSensor.IsVisible = true;
                lblhsbarSensor.IsVisible = true;
                labelTurnSensor.Text = "Off at %";
            }
            else
            {
                cboxPressureSensor.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                cboxEncoder.IsChecked = false;
                labelTurnSensor.IsVisible = false;
                lblPercentFS.IsVisible = false;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = false;
                hsbarSensor.IsVisible = false;
                lblhsbarSensor.IsVisible = false;
            }

            // Off-screen recovery (WinForms checked Bounds; ScreenHelper accepts the Window directly).
            if (!ScreenHelper.IsOnScreen(this))
            {
                Position = new PixelPoint(0, 0);
            }

            if (tel.isLightBarNotSteerBar) rbtnLightBar.IsChecked = true;
            else rbtnSteerBar.IsChecked = true;

            // Start Smart WAS Calibration data collection.
            smartWAS.Reset();
            smartWAS.Start();

            isLoading = false;
        }

        /// <summary>
        /// [XPLAT] Faithful port of WinForms <c>FormSteer_FormClosing</c> (source L338-L384). Persists
        /// the guidance gains and the PGN 252 (0xFC) bytes, then saves all three settings stores unless
        /// Easy-Drive mode is active. BEHAVIOR-FROZEN: the byte packing in <see cref="Apply252ToPgn"/> is
        /// identical to the net48 baseline.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Stop Smart WAS Calibration data collection.
            smartWAS.Stop();

            vehicle.isInFreeDriveMode = false;

            ToolSettings.Default.setVehicle_goalPointLookAheadHold = vehicle.goalPointLookAheadHold;
            ToolSettings.Default.setVehicle_goalPointLookAheadMult = vehicle.goalPointLookAheadMult;
            ToolSettings.Default.setVehicle_goalPointAcquireFactor = vehicle.goalPointAcquireFactor;

            ToolSettings.Default.stanleyHeadingErrorGain = vehicle.stanleyHeadingErrorGain;
            ToolSettings.Default.stanleyDistanceErrorGain = vehicle.stanleyDistanceErrorGain;
            ToolSettings.Default.stanleyIntegralGainAB = vehicle.stanleyIntegralGainAB;
            ToolSettings.Default.purePursuitIntegralGainAB = vehicle.purePursuitIntegralGain;
            VehicleSettings.Default.setVehicle_maxSteerAngle = vehicle.maxSteerAngle;

            // PGN 252 encode: persist the settings then fill the frame (byte-frozen, identical values).
            VehicleSettings.Default.setAS_countsPerDegree = unchecked((byte)(int)hsbarCountsPerDegree.Value);
            VehicleSettings.Default.setAS_ackerman = unchecked((byte)(int)hsbarAckerman.Value);
            VehicleSettings.Default.setAS_wasOffset = (int)hsbarWasOffset.Value;
            VehicleSettings.Default.setAS_highSteerPWM = unchecked((byte)(int)hsbarHighSteerPWM.Value);
            VehicleSettings.Default.setAS_lowSteerPWM = unchecked((byte)((int)hsbarHighSteerPWM.Value / 3));
            VehicleSettings.Default.setAS_Kp = unchecked((byte)(int)hsbarProportionalGain.Value);
            VehicleSettings.Default.setAS_minSteerPWM = unchecked((byte)(int)hsbarMinPWM.Value);
            Apply252ToPgn();

            ToolSettings.Default.setAS_deadZoneHeading = vehicle.deadZoneHeading;
            ToolSettings.Default.setAS_deadZoneDelay = vehicle.deadZoneDelay;

            VehicleSettings.Default.setAS_ModeXTE = vehicle.modeXTE;
            VehicleSettings.Default.setAS_ModeTime = vehicle.modeTime;

            // Window location (Avalonia PixelPoint -> WinForms Point in the settings store).
            var pos = Position;
            AgOpenGPS.Properties.Settings.Default.setWindow_steerSettingsLocation = new System.Drawing.Point(pos.X, pos.Y);

            AgOpenGPS.Properties.Settings.Default.setAS_uTurnCompensation = vehicle.uturnCompensation;

            // Do not persist settings in Easy Drive mode.
            if (!tel.isEasyDriveMode)
            {
                VehicleSettings.Default.Save();
                ToolSettings.Default.Save();
                AgOpenGPS.Properties.Settings.Default.Save();
            }

            // [XPLAT] Avalonia timers are not disposed with the window — stop explicitly.
            timer1.Stop();

            base.OnClosing(e);
        }

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN: PGN 252 (0xFC) byte packing identical to net48 <c>FormSteer</c>
        /// (the chained <c>p_252.pgn[...] = unchecked((byte)...)</c> writes in both FormClosing and the
        /// timer resend). Used by <see cref="OnClosing"/> and <see cref="Timer1_Tick"/> so the encode is
        /// written once. Slider values are bridged double->int before the byte cast / right-shift, exactly
        /// reproducing the original int arithmetic.
        /// </summary>
        private void Apply252ToPgn()
        {
            var p = steerCfg.P252;
            p.Pgn[p.CountsPerDegree] = unchecked((byte)(int)hsbarCountsPerDegree.Value);
            p.Pgn[p.Ackerman] = unchecked((byte)(int)hsbarAckerman.Value);
            p.Pgn[p.WasOffsetHi] = unchecked((byte)((int)hsbarWasOffset.Value >> 8));
            p.Pgn[p.WasOffsetLo] = unchecked((byte)(int)hsbarWasOffset.Value);
            p.Pgn[p.HighPWM] = unchecked((byte)(int)hsbarHighSteerPWM.Value);
            p.Pgn[p.LowPWM] = unchecked((byte)((int)hsbarHighSteerPWM.Value / 3));
            p.Pgn[p.GainProportional] = unchecked((byte)(int)hsbarProportionalGain.Value);
            p.Pgn[p.MinPWM] = unchecked((byte)(int)hsbarMinPWM.Value);
        }

        // ===============================================================================================
        // Per-tick telemetry + PGN 252 resend (WinForms Timer1_Tick, source L386-L496).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Faithful port of WinForms <c>Timer1_Tick</c>. Drives the steer-angle diameter
        /// measurement, the live steer/PWM/angular-velocity read-outs, and the throttled PGN 252 resend.
        /// BEHAVIOR-FROZEN: the <c>counter &gt; 4</c> resend gate (the resend cadence) is preserved
        /// exactly, and the resend reuses <see cref="Apply252ToPgn"/> for byte-identical framing.
        /// </summary>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (isSA)
            {
                dist = glm.Distance(startFix, tel.pivotAxlePos);
                cntr++;
                if (dist > diameter)
                {
                    diameter = dist;
                    cntr = 0;
                }
                lblDiameter.Text = diameter.ToString("N2", CI) + " m";

                if (cntr > 9)
                {
                    steerAngleRight = Math.Atan(vehicle.VehicleConfig.Wheelbase / ((diameter - vehicle.VehicleConfig.TrackWidth * 0.5) / 2));
                    steerAngleRight = glm.toDegrees(steerAngleRight);

                    lblCalcSteerAngleInner.Text = steerAngleRight.ToString("N1", CI) + "\u00B0";
                    lblDiameter.Text = diameter.ToString("N2", CI) + " m";
                    SetButtonImage(btnStartSA, "BoundaryRecord.png");
                    isSA = false;
                }
            }

            double actAng = tel.actualSteerAngleDegrees * 5;
            if (actAng > 0)
            {
                if (actAng > 49) actAng = 49;
                pbarRight.Value = (int)actAng;
                pbarLeft.Value = 0;
            }
            else
            {
                if (actAng < -49) actAng = -49;
                pbarRight.Value = 0;
                pbarLeft.Value = (int)-actAng;
            }

            lblSteerAngle.Text = tel.SetSteerAngle;
            lblSteerAngleActual.Text = tel.actualSteerAngleDegrees.ToString("N1", CI) + "\u00B0";
            lblActualSteerAngleUpper.Text = lblSteerAngleActual.Text;
            double err = (tel.actualSteerAngleDegrees - tel.guidanceLineSteerAngle * 0.01);
            lblError.Text = Math.Abs(err).ToString("N1", CI) + "\u00B0";
            if (err > 0) lblError.Foreground = Brushes.Red;
            else lblError.Foreground = Brushes.DarkGreen;

            lblAV_Act.Text = tel.actAngVel.ToString("N1", CI);
            lblAV_Set.Text = tel.setAngVel.ToString("N1", CI);

            lblPWMDisplay.Text = tel.pwmDisplay.ToString(CI);

            counter++;

            if (toSend && counter > 4)
            {
                Apply252ToPgn();
                steerCfg.SendPgnToLoop(steerCfg.P252.Pgn);
                toSend = false;
                counter = 0;
            }

            if (secondCntr++ > 2)
            {
                secondCntr = 0;

                if (tabControl1.SelectedItem == tabPPAdv)
                {
                    lblHoldAdv.Text = vehicle.goalPointLookAheadHold.ToString("N1", CI);
                    lblAcqAdv.Text = (vehicle.goalPointLookAheadHold * vehicle.goalPointAcquireFactor).ToString("N1", CI);
                    lblDistanceAdv.Text = vehicle.goalDistance.ToString("N1", CI);
                    lblAcquirePP.Text = lblAcqAdv.Text;
                }
            }

            if (tel.sensorData != -1)
            {
                if (tel.sensorData < 0 || tel.sensorData > 255) tel.sensorData = 0;
                pbarSensor.Value = tel.sensorData;
                if (nudMaxCounts.IsVisible == false)
                    lblPercentFS.Text = ((int)((double)tel.sensorData * 0.3921568627)).ToString(CI) + "%";
                else
                    lblPercentFS.Text = tel.sensorData.ToString(CI);
            }

            // Update Smart WAS Calibration status.
            UpdateSmartWASStatus();
        }

        // ===============================================================================================
        // Extracted, BEHAVIOR-FROZEN PGN 251 (0xFB) bitfield encoders.
        // These are pure functions (no Avalonia/control dependency) carrying the exact net48 bit cadence so
        // the byte-for-byte contract is independently unit-testable from AgOpenGPS.Tests/Parity. The math is
        // verbatim from FormSteer.SaveSettings — DO NOT "simplify": the running production path
        // (SaveSettings) and the parity test both call through here, so any drift is caught immediately.
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN steer-board <c>setting0</c> byte encoder (PGN 251 / 0xFB, index 5),
        /// extracted verbatim from net48 <c>FormSteer.SaveSettings</c>. Reproduces the
        /// <c>set = 1; reset = 2046;</c> seed and the per-bit <c>set &lt;&lt;= 1; reset &lt;&lt;= 1; reset += 1;</c>
        /// cadence exactly. Bit order: b0 invert-WAS, b1 steer-invert-relays, b2 invert-steer, b3 conv==Single,
        /// b4 motor-drive==Cytron, b5 steer-enable==Switch, b6 steer-enable==Button, b7 encoder.
        /// </summary>
        public static byte EncodeSetting0(
            bool invertWas,
            bool steerInvertRelays,
            bool invertSteer,
            bool convSingle,
            bool driveCytron,
            bool enableSwitch,
            bool enableButton,
            bool encoder)
        {
            int set = 1;
            int reset = 2046;
            int sett = 0;

            if (invertWas) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (steerInvertRelays) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (invertSteer) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (convSingle) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (driveCytron) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (enableSwitch) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (enableButton) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (encoder) sett |= set;
            else sett &= reset;

            return (byte)sett;
        }

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN steer-board <c>setting1</c> byte encoder (PGN 251 / 0xFB, index 8),
        /// extracted verbatim from net48 <c>FormSteer.SaveSettings</c>. Bit order: b0 Danfoss,
        /// b1 pressure-sensor, b2 current-sensor, b3 X/Y==Y.
        /// </summary>
        public static byte EncodeSetting1(
            bool danfoss,
            bool pressure,
            bool current,
            bool xyIsY)
        {
            int set = 1;
            int reset = 2046;
            int sett = 0;

            if (danfoss) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (pressure) sett |= set;
            else sett &= reset;

            //bit 2
            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (current) sett |= set;
            else sett &= reset;

            //bit 3
            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (xyIsY) sett |= set;
            else sett &= reset;

            return (byte)sett;
        }

        // ===============================================================================================
        // SaveSettings — PGN 251 (0xFB) bitfield encode (WinForms SaveSettings, source L1086-L1195).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN: the steer-board configuration PGN 251 (0xFB) bitfield encode,
        /// reproduced byte-for-byte from net48 <c>FormSteer.SaveSettings</c>. The <c>set</c>/<c>reset</c>
        /// shift cadence (<c>set = 1; reset = 2046;</c> then per bit <c>set &lt;&lt;= 1; reset &lt;&lt;= 1;
        /// reset += 1;</c>) and the exact bit order are preserved verbatim.
        /// </summary>
        private void SaveSettings()
        {
            // [XPLAT] BEHAVIOR-FROZEN: the setting0 bitfield is delegated to the extracted, independently
            // unit-tested EncodeSetting0 (byte-identical bit cadence — see
            // AgOpenGPS.Tests/Parity/SteerConfigPgnParityTests.cs). cboxSteerEnable is read twice (Switch
            // then Button), in that order, exactly as the net48 original, preserving evaluation semantics.
            VehicleSettings.Default.setArdSteer_setting0 = EncodeSetting0(
                IsChk(chkInvertWAS),
                IsChk(chkSteerInvertRelays),
                IsChk(chkInvertSteer),
                GetCombo(cboxConv) == "Single",
                GetCombo(cboxMotorDrive) == "Cytron",
                GetCombo(cboxSteerEnable) == "Switch",
                GetCombo(cboxSteerEnable) == "Button",
                IsChk(cboxEncoder));
            VehicleSettings.Default.setArdMac_isDanfoss = IsChk(cboxDanfoss);

            if (IsChk(cboxCurrentSensor) || IsChk(cboxPressureSensor))
            {
                VehicleSettings.Default.setArdSteer_maxPulseCounts = (byte)(int)hsbarSensor.Value;
            }
            else
            {
                VehicleSettings.Default.setArdSteer_maxPulseCounts = (byte)(int)nudMaxCountsValue;
            }

            // Settings1 — [XPLAT] BEHAVIOR-FROZEN: delegated to the extracted, unit-tested EncodeSetting1
            // (byte-identical bit cadence — see AgOpenGPS.Tests/Parity/SteerConfigPgnParityTests.cs).
            VehicleSettings.Default.setArdSteer_setting1 = EncodeSetting1(
                IsChk(cboxDanfoss),
                IsChk(cboxPressureSensor),
                IsChk(cboxCurrentSensor),
                GetCombo(cboxXY) == "Y");

            if (!tel.isEasyDriveMode) VehicleSettings.Default.Save();

            var p = steerCfg.P251;
            p.Pgn[p.Set0] = VehicleSettings.Default.setArdSteer_setting0;
            p.Pgn[p.Set1] = VehicleSettings.Default.setArdSteer_setting1;
            p.Pgn[p.MaxPulse] = VehicleSettings.Default.setArdSteer_maxPulseCounts;
            p.Pgn[p.MinSpeed] = unchecked((byte)(VehicleSettings.Default.setAS_minSteerSpeed * 10));

            if (AgOpenGPS.Properties.Settings.Default.setAS_isConstantContourOn)
                p.Pgn[p.AngVel] = 1;
            else p.Pgn[p.AngVel] = 0;

            pboxSendSteer.IsVisible = false;
        }

        /// <summary>[XPLAT] WinForms <c>btnSendSteerConfigPGN_Click</c> (source L1180-L1188): save, then
        /// send the freshly-built PGN 251 frame over the loopback, re-enable Close, and confirm.</summary>
        private void btnSendSteerConfigPGN_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            steerCfg.SendPgnToLoop(steerCfg.P251.Pgn);
            pboxSendSteer.IsVisible = false;
            btnClose.IsEnabled = true;
            Log.EventWriter("Steer Form, Send and Save Pressed");

            TimedMessageBox(2000, gStr.gsAutoSteerPort, "Settings Sent To Steer Module");
        }

        /// <summary>[XPLAT] WinForms <c>btnClose_Click</c>.</summary>
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===============================================================================================
        // btnVehicleReset_Click — reset this page to factory defaults (source L1207-L1287).
        // The default constants are BEHAVIOR-FROZEN: they define the PGN bytes the reset will resend.
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] WinForms <c>btnVehicleReset_Click</c>. Reproduces the exact default constants, then
        /// rebuilds the vehicle through <see cref="ISteerSettingsConfigService.ResetVehicle"/> (replacing
        /// <c>mf.vehicle = new CVehicle(mf)</c>), re-runs the load logic, primes the resend
        /// (<c>toSend = true; counter = 6;</c>), and performs the tab-reselect dance. The WinForms modal
        /// <c>FormDialog.ShowQuestion</c> becomes the async <see cref="FormDialogView.ShowQuestionAsync"/>.
        /// </summary>
        private async void btnVehicleReset_Click(object sender, RoutedEventArgs e)
        {
            bool result = await FormDialogView.ShowQuestionAsync(
                this,
                "Reset This Page to Defaults",
                "Are you Sure");

            if (result)
            {
                Log.EventWriter("Steer Form - Steer Settings Set to Default");

                TimedMessageBox(2000, "Reset To Default", "Values Set to Inital Default");
                VehicleSettings.Default.setVehicle_maxSteerAngle = vehicle.maxSteerAngle
                    = 45;

                VehicleSettings.Default.setAS_countsPerDegree = 110;

                VehicleSettings.Default.setAS_ackerman = 100;

                VehicleSettings.Default.setAS_wasOffset = 3;

                VehicleSettings.Default.setAS_highSteerPWM = 180;
                VehicleSettings.Default.setAS_Kp = 50;
                VehicleSettings.Default.setAS_minSteerPWM = 25;

                VehicleSettings.Default.setArdSteer_setting0 = 56;
                VehicleSettings.Default.setArdSteer_setting1 = 0;
                VehicleSettings.Default.setArdMac_isDanfoss = false;

                VehicleSettings.Default.setArdSteer_maxPulseCounts = 3;

                ToolSettings.Default.setVehicle_goalPointAcquireFactor = 0.85;
                ToolSettings.Default.setVehicle_goalPointLookAheadHold = 3;
                ToolSettings.Default.setVehicle_goalPointLookAheadMult = 1.5;

                ToolSettings.Default.stanleyHeadingErrorGain = 1;
                ToolSettings.Default.stanleyDistanceErrorGain = 1;
                ToolSettings.Default.stanleyIntegralGainAB = 0;

                ToolSettings.Default.purePursuitIntegralGainAB = 0;

                VehicleSettings.Default.setAS_sideHillComp = 0;

                AgOpenGPS.Properties.Settings.Default.setAS_uTurnCompensation = 1;

                VehicleSettings.Default.setIMU_invertRoll = false;

                VehicleSettings.Default.setIMU_rollZero = 0;

                VehicleSettings.Default.setAS_minSteerSpeed = 0;
                VehicleSettings.Default.setAS_maxSteerSpeed = 15;
                VehicleSettings.Default.setAS_functionSpeedLimit = 12;
                AgOpenGPS.Properties.Settings.Default.setDisplay_lightbarCmPerPixel = 5;
                AgOpenGPS.Properties.Settings.Default.setDisplay_lineWidth = 2;
                ToolSettings.Default.setAS_snapDistance = 20;
                AgOpenGPS.Properties.Settings.Default.setAS_guidanceLookAheadTime = 1.5;
                AgOpenGPS.Properties.Settings.Default.setAS_uTurnCompensation = 1;

                ToolSettings.Default.setVehicle_isStanleyUsed = false;
                tel.isStanleyUsed = false;

                VehicleSettings.Default.setAS_isSteerInReverse = false;
                tel.isSteerInReverse = false;

                //save current vehicle and tool
                VehicleSettings.Default.Save();
                ToolSettings.Default.Save();

                vehicle = steerCfg.ResetVehicle();

                LoadSettingsToUi();

                toSend = true; counter = 6;

                pboxSendSteer.IsVisible = true;

                tabControl1.SelectedIndex = 1;
                tabControl1.SelectedIndex = 0;
                tabSteerSettings.SelectedIndex = 1;
                tabSteerSettings.SelectedIndex = 0;
            }
        }

        // ===============================================================================================
        // "Dirty" / alert handlers + sensor mutual-exclusion (source L500-L584).
        // [XPLAT] All slider reads use (int)Value because WinForms HScrollBar.Value was an int; the
        // Avalonia Slider exposes a double. Casting preserves the exact integer the WinForms code saw,
        // which keeps the PGN bytes (built from these same controls) byte-for-byte identical.
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] WinForms <c>EnableAlert_Click</c> (source L500-L562). Marks the page dirty (show the
        /// "send" prompt, disable Close) and enforces the encoder/pressure/current sensor mutual exclusion.
        /// Shared by the steer-config combo boxes (via a forwarding lambda) and check boxes. Guarded by
        /// <see cref="isLoading"/> because Avalonia raises <c>SelectionChanged</c> on programmatic combo
        /// assignment during load, whereas the WinForms <c>.Click</c> wiring did not.
        /// </summary>
        private void EnableAlert_Click(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;

            pboxSendSteer.IsVisible = true;
            btnClose.IsEnabled = false;

            if (sender is CheckBox checkbox)
            {
                // [XPLAT] reference comparison replaces the WinForms checkbox.Name string test.
                if (checkbox == cboxEncoder || checkbox == cboxPressureSensor
                    || checkbox == cboxCurrentSensor)
                {
                    if (!IsChk(checkbox))
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        cboxEncoder.IsChecked = false;
                        labelTurnSensor.IsVisible = false;
                        lblPercentFS.IsVisible = false;
                        nudMaxCounts.IsVisible = false;
                        pbarSensor.IsVisible = false;
                        hsbarSensor.IsVisible = false;
                        lblhsbarSensor.IsVisible = false;
                        return;
                    }

                    if (checkbox == cboxPressureSensor)
                    {
                        cboxEncoder.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        labelTurnSensor.IsVisible = true;
                        lblPercentFS.IsVisible = true;
                        nudMaxCounts.IsVisible = false;
                        pbarSensor.IsVisible = true;
                        labelTurnSensor.Text = "Off at %";
                        hsbarSensor.IsVisible = true;
                        lblhsbarSensor.IsVisible = true;
                    }
                    else if (checkbox == cboxCurrentSensor)
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxEncoder.IsChecked = false;
                        labelTurnSensor.IsVisible = true;
                        lblPercentFS.IsVisible = true;
                        nudMaxCounts.IsVisible = false;
                        hsbarSensor.IsVisible = true;
                        pbarSensor.IsVisible = true;
                        labelTurnSensor.Text = "Off at %";
                        lblhsbarSensor.IsVisible = true;
                    }
                    else if (checkbox == cboxEncoder)
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        labelTurnSensor.IsVisible = true;
                        lblPercentFS.IsVisible = false;
                        nudMaxCounts.IsVisible = true;
                        pbarSensor.IsVisible = false;
                        hsbarSensor.IsVisible = false;
                        lblhsbarSensor.IsVisible = false;
                        labelTurnSensor.Text = gStr.gsEncoderCounts;
                    }
                }
            }
        }

        /// <summary>[XPLAT] WinForms <c>hsbarSensor_Scroll</c> (source L574-L579).</summary>
        private void hsbarSensor_Scroll(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;

            pboxSendSteer.IsVisible = true;
            btnClose.IsEnabled = false;
            lblhsbarSensor.Text = ((int)((double)(int)hsbarSensor.Value * 0.3921568627)).ToString(CI) + "%";
        }

        /// <summary>[XPLAT] WinForms <c>hsbarUTurnCompensation_ValueChanged</c> (source L606-L610).</summary>
        private void hsbarUTurnCompensation_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.uturnCompensation = (int)hsbarUTurnCompensation.Value * 0.1;
            lblUTurnCompensation.Text = ((int)hsbarUTurnCompensation.Value - 10).ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>cboxSteerInReverse_Click</c> (source L612-L616).</summary>
        private void cboxSteerInReverse_Click(object sender, RoutedEventArgs e)
        {
            VehicleSettings.Default.setAS_isSteerInReverse = IsChk(cboxSteerInReverse);
            tel.isSteerInReverse = IsChk(cboxSteerInReverse);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarSideHillComp_ValueChanged</c> (source L618-L625).
        /// <c>mf.gyd.sideHillCompFactor</c> is routed through
        /// <see cref="ISteerSettingsConfigService.SideHillCompFactor"/>.</summary>
        private void hsbarSideHillComp_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            double deg = (int)hsbarSideHillComp.Value;
            deg *= 0.01;
            lblSideHillComp.Text = (deg.ToString("N2", CI) + "\u00B0");
            VehicleSettings.Default.setAS_sideHillComp = deg;
            steerCfg.SideHillCompFactor = deg;
        }

        /// <summary>[XPLAT] WinForms <c>rbtnLightBar_Click</c> (source L808-L813).</summary>
        private void rbtnLightBar_Click(object sender, RoutedEventArgs e)
        {
            tel.isLightBarNotSteerBar = true;
            AgOpenGPS.Properties.Settings.Default.setMenu_isLightbarNotSteerBar = tel.isLightBarNotSteerBar;
            if (!tel.isEasyDriveMode) AgOpenGPS.Properties.Settings.Default.Save();
        }

        /// <summary>[XPLAT] WinForms <c>rbtnSteerBar_Click</c> (source L815-L820).</summary>
        private void rbtnSteerBar_Click(object sender, RoutedEventArgs e)
        {
            tel.isLightBarNotSteerBar = false;
            AgOpenGPS.Properties.Settings.Default.setMenu_isLightbarNotSteerBar = tel.isLightBarNotSteerBar;
            if (!tel.isEasyDriveMode) AgOpenGPS.Properties.Settings.Default.Save();
        }

        /// <summary>[XPLAT] WinForms <c>chkDisplayLightbar_Click</c> (source L822-L831). The WinForms
        /// <c>chkDisplayLightbar.Image</c> swap becomes a <see cref="SetButtonImage"/> call on the
        /// image-toggle CheckBox.</summary>
        private void chkDisplayLightbar_Click(object sender, RoutedEventArgs e)
        {
            if (IsChk(chkDisplayLightbar)) { SetButtonImage(chkDisplayLightbar, "SwitchOn.png"); }
            else { SetButtonImage(chkDisplayLightbar, "SwitchOff.png"); }

            AgOpenGPS.Properties.Settings.Default.setMenu_isLightbarOn = IsChk(chkDisplayLightbar);
            if (!tel.isEasyDriveMode) AgOpenGPS.Properties.Settings.Default.Save();
            tel.isLightbarOn = IsChk(chkDisplayLightbar);
        }

        // ===============================================================================================
        // Gain tab sliders (source L838-L858) — PWM/proportional. Each sets toSend/counter so the timer
        // re-encodes and resends p_252 (see Timer1_Tick).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>hsbarMinPWM_ValueChanged</c>.</summary>
        private void hsbarMinPWM_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblMinPWM.Text = unchecked((byte)(int)hsbarMinPWM.Value).ToString(CI);
            toSend = true;
            counter = 0;
        }

        /// <summary>[XPLAT] WinForms <c>hsbarProportionalGain_ValueChanged</c>.</summary>
        private void hsbarProportionalGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblProportionalGain.Text = unchecked((byte)(int)hsbarProportionalGain.Value).ToString(CI);
            toSend = true;
            counter = 0;
        }

        /// <summary>[XPLAT] WinForms <c>hsbarHighSteerPWM_ValueChanged</c>.</summary>
        private void hsbarHighSteerPWM_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblHighSteerPWM.Text = unchecked((byte)(int)hsbarHighSteerPWM.Value).ToString(CI);
            toSend = true;
            counter = 0;
        }

        // ===============================================================================================
        // Steer tab sliders (source L873-L899).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>hsbarAckerman_ValueChanged</c>.</summary>
        private void hsbarAckerman_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblAckerman.Text = unchecked((byte)(int)hsbarAckerman.Value).ToString(CI);
            toSend = true;
            counter = 0;
        }

        /// <summary>[XPLAT] WinForms <c>hsbarMaxSteerAngle_ValueChanged</c>.</summary>
        private void hsbarMaxSteerAngle_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.maxSteerAngle = (int)hsbarMaxSteerAngle.Value;
            lblMaxSteerAngle.Text = ((int)hsbarMaxSteerAngle.Value).ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarCountsPerDegree_ValueChanged</c>.</summary>
        private void hsbarCountsPerDegree_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblCountsPerDegree.Text = unchecked((byte)(int)hsbarCountsPerDegree.Value).ToString(CI);
            lblSteerAngleSensorZero.Text =
                ((int)hsbarWasOffset.Value / (double)(int)hsbarCountsPerDegree.Value).ToString("N2", CI);
            toSend = true;
            counter = 0;
        }

        /// <summary>[XPLAT] WinForms <c>hsbarSteerAngleSensorZero_ValueChanged</c> (wired to
        /// <c>hsbarWasOffset</c> exactly as the WinForms designer did).</summary>
        private void hsbarSteerAngleSensorZero_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblSteerAngleSensorZero.Text =
                ((int)hsbarWasOffset.Value / (double)(int)hsbarCountsPerDegree.Value).ToString("N2", CI);
            toSend = true;
            counter = 0;
        }

        // ===============================================================================================
        // Stanley tab sliders (source L942-L959).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>hsbarStanleyGain_ValueChanged</c>.</summary>
        private void hsbarStanleyGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.stanleyDistanceErrorGain = (int)hsbarStanleyGain.Value * 0.1;
            lblStanleyGain.Text = vehicle.stanleyDistanceErrorGain.ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarHeadingErrorGain_ValueChanged</c>.</summary>
        private void hsbarHeadingErrorGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.stanleyHeadingErrorGain = (int)hsbarHeadingErrorGain.Value * 0.1;
            lblHeadingErrorGain.Text = vehicle.stanleyHeadingErrorGain.ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarIntegral_ValueChanged</c>.</summary>
        private void hsbarIntegral_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.stanleyIntegralGainAB = (int)hsbarIntegral.Value * 0.01;
            lblIntegralPercent.Text = ((int)hsbarIntegral.Value).ToString(CI);
        }

        // ===============================================================================================
        // Pure Pursuit tab sliders (source L964-L987).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>hsbarHoldLookAhead_ValueChanged</c>.</summary>
        private void hsbarHoldLookAhead_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.goalPointLookAheadHold = (int)hsbarHoldLookAhead.Value * 0.1;
            lblHoldLookAhead.Text = vehicle.goalPointLookAheadHold.ToString(CI);
            lblAcquirePP.Text =
                (vehicle.goalPointLookAheadHold * vehicle.goalPointAcquireFactor).ToString("N1", CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarIntegralPurePursuit_ValueChanged</c>.</summary>
        private void hsbarIntegralPurePursuit_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.purePursuitIntegralGain = (int)hsbarIntegralPurePursuit.Value * 0.01;
            lblPureIntegral.Text = ((int)hsbarIntegralPurePursuit.Value).ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarLookAheadMult_ValueChanged</c>.</summary>
        private void hsbarLookAheadMult_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.goalPointLookAheadMult = (int)hsbarLookAheadMult.Value * 0.1;
            lblLookAheadMult.Text = vehicle.goalPointLookAheadMult.ToString(CI);
        }

        /// <summary>[XPLAT] WinForms <c>hsbarAcquireFactor_ValueChanged</c>.</summary>
        private void hsbarAcquireFactor_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            vehicle.goalPointAcquireFactor = (int)hsbarAcquireFactor.Value * 0.01;
            lblAcquireFactor.Text = vehicle.goalPointAcquireFactor.ToString(CI);
        }

        // ===============================================================================================
        // NumericUpDown keypad handlers. [XPLAT] WinForms used NudlessNumericUpDown.ShowKeypad(this); the
        // Avalonia nud is a plain Button whose Content shows the value, edited through the modal
        // FormNumeric(min, max, current) dialog (ReturnValue, true == OK). The exact Min/Max/DecimalPlaces
        // are carried over verbatim from the WinForms designer so the keypad clamps identically.
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>nudMaxCounts_Click</c> (source L564-L572). On OK, just flag dirty;
        /// the value feeds <see cref="SaveSettings"/> as the encoder max-pulse count.</summary>
        private async void nudMaxCounts_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 255, nudMaxCountsValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudMaxCountsValue = f.ReturnValue;
                SetNud(nudMaxCounts, nudMaxCountsValue, 0);
                pboxSendSteer.IsVisible = true;
                btnClose.IsEnabled = false;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudMinSteerSpeed_Click</c> (source L705-L713).</summary>
        private async void nudMinSteerSpeed_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 10, nudMinSteerSpeedValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudMinSteerSpeedValue = f.ReturnValue;
                SetNud(nudMinSteerSpeed, nudMinSteerSpeedValue, 1);
                VehicleSettings.Default.setAS_minSteerSpeed = nudMinSteerSpeedValue;
                if (!tel.isMetric)
                    VehicleSettings.Default.setAS_minSteerSpeed =
                        Speed.MphToKmh(VehicleSettings.Default.setAS_minSteerSpeed);
                vehicle.minSteerSpeed = VehicleSettings.Default.setAS_minSteerSpeed;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudMaxSteerSpeed_Click</c> (source L715-L723).</summary>
        private async void nudMaxSteerSpeed_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 50, nudMaxSteerSpeedValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudMaxSteerSpeedValue = f.ReturnValue;
                SetNud(nudMaxSteerSpeed, nudMaxSteerSpeedValue, 0);
                VehicleSettings.Default.setAS_maxSteerSpeed = nudMaxSteerSpeedValue;
                if (!tel.isMetric)
                    VehicleSettings.Default.setAS_maxSteerSpeed =
                        Speed.MphToKmh(VehicleSettings.Default.setAS_maxSteerSpeed);
                vehicle.maxSteerSpeed = VehicleSettings.Default.setAS_maxSteerSpeed;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudGuidanceSpeedLimit_Click</c> (source L725-L733).</summary>
        private async void nudGuidanceSpeedLimit_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 20, nudGuidanceSpeedLimitValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudGuidanceSpeedLimitValue = f.ReturnValue;
                SetNud(nudGuidanceSpeedLimit, nudGuidanceSpeedLimitValue, 0);
                VehicleSettings.Default.setAS_functionSpeedLimit = nudGuidanceSpeedLimitValue;
                if (!tel.isMetric)
                    VehicleSettings.Default.setAS_functionSpeedLimit =
                        Speed.MphToKmh(VehicleSettings.Default.setAS_functionSpeedLimit);
                vehicle.functionSpeedLimit = VehicleSettings.Default.setAS_functionSpeedLimit;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudcmPerPixel_Click</c> (source L772-L779).</summary>
        private async void nudcmPerPixel_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(2, 100, nudcmPerPixelValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudcmPerPixelValue = f.ReturnValue;
                SetNud(nudcmPerPixel, nudcmPerPixelValue, 0);
                AgOpenGPS.Properties.Settings.Default.setDisplay_lightbarCmPerPixel = (int)nudcmPerPixelValue;
                tel.lightbarCmPerPixel = AgOpenGPS.Properties.Settings.Default.setDisplay_lightbarCmPerPixel;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudLineWidth_Click</c> (source L781-L788). <c>mf.ABLine.lineWidth</c>
        /// is routed through <see cref="ISteerSettingsConfigService.LineWidth"/>.</summary>
        private async void nudLineWidth_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(1, 8, nudLineWidthValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudLineWidthValue = f.ReturnValue;
                SetNud(nudLineWidth, nudLineWidthValue, 0);
                AgOpenGPS.Properties.Settings.Default.setDisplay_lineWidth = (int)nudLineWidthValue;
                steerCfg.LineWidth = AgOpenGPS.Properties.Settings.Default.setDisplay_lineWidth;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudSnapDistance_Click</c> (source L790-L797). <c>mf.ABLine.snapDistance</c>
        /// is routed through <see cref="ISteerSettingsConfigService.SnapDistance"/>.</summary>
        private async void nudSnapDistance_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric((double)nudSnapDistanceMin, (double)nudSnapDistanceMax, nudSnapDistanceValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudSnapDistanceValue = f.ReturnValue;
                SetNud(nudSnapDistance, nudSnapDistanceValue, nudSnapDistanceDecimals);
                ToolSettings.Default.setAS_snapDistance = nudSnapDistanceValue * tel.inOrCm2Cm;
                steerCfg.SnapDistance = ToolSettings.Default.setAS_snapDistance;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudGuidanceLookAhead_Click</c> (source L799-L806).</summary>
        private async void nudGuidanceLookAhead_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0.1, 10, nudGuidanceLookAheadValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudGuidanceLookAheadValue = f.ReturnValue;
                SetNud(nudGuidanceLookAhead, nudGuidanceLookAheadValue, 1);
                AgOpenGPS.Properties.Settings.Default.setAS_guidanceLookAheadTime = nudGuidanceLookAheadValue;
                tel.guidanceLookAheadTime = AgOpenGPS.Properties.Settings.Default.setAS_guidanceLookAheadTime;
            }
        }

        /// <summary>[XPLAT] WinForms <c>nudDeadZoneHeading_Click</c> (source L989-L993). The vehicle field is
        /// written unconditionally (after the dialog), exactly as the WinForms handler did.</summary>
        private async void nudDeadZoneHeading_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0.1, 5, nudDeadZoneHeadingValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudDeadZoneHeadingValue = f.ReturnValue;
                SetNud(nudDeadZoneHeading, nudDeadZoneHeadingValue, 1);
            }
            vehicle.deadZoneHeading = (int)(nudDeadZoneHeadingValue * 100);
        }

        /// <summary>[XPLAT] WinForms <c>nudDeadZoneDelay_Click</c> (source L995-L999). The vehicle field is
        /// written unconditionally (after the dialog), exactly as the WinForms handler did.</summary>
        private async void nudDeadZoneDelay_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(1, 10, nudDeadZoneDelayValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudDeadZoneDelayValue = f.ReturnValue;
                SetNud(nudDeadZoneDelay, nudDeadZoneDelayValue, 0);
            }
            vehicle.deadZoneDelay = (int)(nudDeadZoneDelayValue);
        }

        // ===============================================================================================
        // Tab Enter/Leave. [XPLAT] WinForms wired these to TabPage.Enter/.Leave; Avalonia dispatches them
        // from TabControl.SelectionChanged for the three tracked tabs (all children of tabSteerSettings).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>tabSettings_Enter</c> (source L586-L597).</summary>
        private void tabSettings_Enter(object sender, EventArgs e)
        {
            cboxSteerInReverse.IsChecked = VehicleSettings.Default.setAS_isSteerInReverse;

            if (tel.isStanleyUsed)
            {
                SetButtonImage(btnStanleyPure, "ModeStanley.png");
            }
            else
            {
                SetButtonImage(btnStanleyPure, "ModePurePursuit.png");
            }
        }

        /// <summary>[XPLAT] WinForms <c>tabSettings_Leave</c> (source L600-L604).</summary>
        private void tabSettings_Leave(object sender, EventArgs e)
        {
            VehicleSettings.Default.setAS_isSteerInReverse = IsChk(cboxSteerInReverse);
            if (!tel.isEasyDriveMode) VehicleSettings.Default.Save();
        }

        /// <summary>[XPLAT] WinForms <c>tabAlarm_Enter</c> (source L679-L697). Imperial conversion via
        /// <see cref="Speed.KmhToMph"/>; the nud display decimals match the WinForms designer.</summary>
        private void tabAlarm_Enter(object sender, EventArgs e)
        {
            if (tel.isMetric)
            {
                nudMaxSteerSpeedValue = VehicleSettings.Default.setAS_maxSteerSpeed;
                nudMinSteerSpeedValue = VehicleSettings.Default.setAS_minSteerSpeed;
                nudGuidanceSpeedLimitValue = VehicleSettings.Default.setAS_functionSpeedLimit;
                label160.Text = label163.Text = label166.Text = "kmh";
            }
            else
            {
                nudMaxSteerSpeedValue = Speed.KmhToMph(VehicleSettings.Default.setAS_maxSteerSpeed);
                nudMinSteerSpeedValue = Speed.KmhToMph(VehicleSettings.Default.setAS_minSteerSpeed);
                nudGuidanceSpeedLimitValue = Speed.KmhToMph(VehicleSettings.Default.setAS_functionSpeedLimit);
                label160.Text = label163.Text = label166.Text = "mph";
            }

            SetNud(nudMaxSteerSpeed, nudMaxSteerSpeedValue, 0);
            SetNud(nudMinSteerSpeed, nudMinSteerSpeedValue, 1);
            SetNud(nudGuidanceSpeedLimit, nudGuidanceSpeedLimitValue, 0);

            label20.Text = tel.unitsInCm;
        }

        /// <summary>[XPLAT] WinForms <c>tabAlarm_Leave</c> (source L699-L702).</summary>
        private void tabAlarm_Leave(object sender, EventArgs e)
        {
            if (!tel.isEasyDriveMode) AgOpenGPS.Properties.Settings.Default.Save();
        }

        /// <summary>[XPLAT] WinForms <c>tabOnTheLine_Enter</c> (source L740-L765).</summary>
        private void tabOnTheLine_Enter(object sender, EventArgs e)
        {
            chkDisplayLightbar.IsChecked = tel.isLightbarOn;
            if (IsChk(chkDisplayLightbar)) { SetButtonImage(chkDisplayLightbar, "SwitchOn.png"); }
            else { SetButtonImage(chkDisplayLightbar, "SwitchOff.png"); }

            if (tel.isMetric)
            {
                nudSnapDistanceDecimals = 0;
                nudSnapDistanceValue = (int)((double)ToolSettings.Default.setAS_snapDistance * tel.cm2CmOrIn);
            }
            else
            {
                nudSnapDistanceDecimals = 1;
                nudSnapDistanceValue = Math.Round(
                    ((double)ToolSettings.Default.setAS_snapDistance * tel.cm2CmOrIn), 1, MidpointRounding.AwayFromZero);
            }
            SetNud(nudSnapDistance, nudSnapDistanceValue, nudSnapDistanceDecimals);

            nudGuidanceLookAheadValue = AgOpenGPS.Properties.Settings.Default.setAS_guidanceLookAheadTime;
            SetNud(nudGuidanceLookAhead, nudGuidanceLookAheadValue, 1);

            nudLineWidthValue = AgOpenGPS.Properties.Settings.Default.setDisplay_lineWidth;
            SetNud(nudLineWidth, nudLineWidthValue, 0);

            nudcmPerPixelValue = AgOpenGPS.Properties.Settings.Default.setDisplay_lightbarCmPerPixel;
            SetNud(nudcmPerPixel, nudcmPerPixelValue, 0);

            label20.Text = tel.unitsInCm;
            label43.Text = tel.unitsInCm;
        }

        /// <summary>[XPLAT] WinForms <c>tabOnTheLine_Leave</c> (source L767-L770).</summary>
        private void tabOnTheLine_Leave(object sender, EventArgs e)
        {
            if (!tel.isEasyDriveMode) AgOpenGPS.Properties.Settings.Default.Save();
        }

        /// <summary>
        /// [XPLAT] Dispatches the WinForms TabPage Enter/Leave semantics from Avalonia's
        /// <c>SelectionChanged</c>: removed items fire <c>*_Leave</c>, added items fire <c>*_Enter</c>,
        /// but only for the three tracked tabs. Other tab changes (e.g. the Stanley/Pure tabs on
        /// <c>tabControl1</c>) have no Enter/Leave logic and are ignored, exactly as in WinForms.
        /// </summary>
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.RemovedItems)
            {
                if (item == tabSettings) tabSettings_Leave(sender, e);
                else if (item == tabAlarm) tabAlarm_Leave(sender, e);
                else if (item == tabOnTheLine) tabOnTheLine_Leave(sender, e);
            }

            foreach (var item in e.AddedItems)
            {
                if (item == tabSettings) tabSettings_Enter(sender, e);
                else if (item == tabAlarm) tabAlarm_Enter(sender, e);
                else if (item == tabOnTheLine) tabOnTheLine_Enter(sender, e);
            }
        }

        /// <summary>
        /// [XPLAT] WinForms raised the initially-selected TabPage's <c>Enter</c> on load. Avalonia does not
        /// guarantee a <c>SelectionChanged</c> for the default selection, so this fires <c>*_Enter</c> once
        /// for whichever tracked tab is selected when the window opens. With the default selection
        /// (tabSensors / tabPP) this is a no-op, mirroring the WinForms baseline.
        /// </summary>
        private void FireInitialTabEnter()
        {
            object selected = tabSteerSettings.SelectedItem;
            if (selected == tabSettings) tabSettings_Enter(this, EventArgs.Empty);
            else if (selected == tabAlarm) tabAlarm_Enter(this, EventArgs.Empty);
            else if (selected == tabOnTheLine) tabOnTheLine_Enter(this, EventArgs.Empty);
        }

        // ===============================================================================================
        // Stanley / Pure Pursuit mode toggle + the tab add/remove dance (source L627-L677).
        // [XPLAT] WinForms TabControl.TabPages.Remove/Add -> Avalonia TabControl.Items.Remove/Add; the
        // WinForms ItemSize is approximated by SetTabHeaderSize. The tab ORDER is preserved exactly.
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>btnStanleyPure_Click</c>.</summary>
        private void btnStanleyPure_Click(object sender, RoutedEventArgs e)
        {
            tel.isStanleyUsed = !tel.isStanleyUsed;

            if (tel.isStanleyUsed)
            {
                SetButtonImage(btnStanleyPure, "ModeStanley.png");
                Log.EventWriter("Stanley Steer Mode Selectede");
            }
            else
            {
                SetButtonImage(btnStanleyPure, "ModePurePursuit.png");
                Log.EventWriter("Pure Pursuit Steer Mode Selected");
            }

            tabControl1.Items.Remove(tabPP);
            tabControl1.Items.Remove(tabPPAdv);
            tabControl1.Items.Remove(tabGain);
            tabControl1.Items.Remove(tabSteer);
            tabControl1.Items.Remove(tabStan);

            ToolSettings.Default.setVehicle_isStanleyUsed = tel.isStanleyUsed;
            if (!tel.isEasyDriveMode)
            {
                VehicleSettings.Default.Save();
                ToolSettings.Default.Save();
            }

            if (tel.isStanleyUsed)
            {
                SetTabHeaderSize(tabControl1, 105, 48);
                tabControl1.Items.Add(tabStan);
                tabControl1.Items.Add(tabGain);
                tabControl1.Items.Add(tabSteer);
            }
            else
            {
                tabControl1.Items.Add(tabPP);
                tabControl1.Items.Add(tabGain);
                tabControl1.Items.Add(tabSteer);
                tabControl1.Items.Add(tabPPAdv);

                SetTabHeaderSize(tabControl1, 89, 48);
            }
        }

        // ===============================================================================================
        // Window expand/collapse (source L1001-L1018).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>expandWindow_Click</c>. The WinForms 392x492 / 918x673 sizes are
        /// applied to the Avalonia <see cref="Window.Width"/>/<see cref="Window.Height"/>.</summary>
        private void expandWindow_Click(object sender, RoutedEventArgs e)
        {
            if (windowSizeState++ > 0) windowSizeState = 0;
            if (windowSizeState == 1)
            {
                Width = 918;
                Height = 673;
                SetButtonImage(btnExpand, "ArrowLeft.png");
            }
            else if (windowSizeState == 0)
            {
                Width = 392;
                Height = 492;
                SetButtonImage(btnExpand, "ArrowRight.png");
            }
        }

        /// <summary>[XPLAT] The status TextBlocks act as a hit-target for expand/collapse. WinForms wired
        /// their <c>.Click</c>; Avalonia uses <c>PointerPressed</c>, forwarded to the shared toggle.</summary>
        private void expandWindow_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            expandWindow_Click(sender, e);
        }

        // ===============================================================================================
        // Free-drive controls (source L1021-L1068).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>btnFreeDrive_Click</c>. <c>BackColor</c> becomes
        /// <c>Background</c>; <c>Color.FromArgb(50,50,70)</c> maps to an opaque Avalonia
        /// <see cref="SolidColorBrush"/>, and <c>Color.LightGreen</c> to <see cref="Brushes.LightGreen"/>.</summary>
        private void btnFreeDrive_Click(object sender, RoutedEventArgs e)
        {
            if (vehicle.isInFreeDriveMode)
            {
                //turn OFF free drive mode
                SetButtonImage(btnFreeDrive, "SteerDriveOff.png");
                btnFreeDrive.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 70));
                vehicle.isInFreeDriveMode = false;
                btnSteerAngleDown.IsEnabled = false;
                btnSteerAngleUp.IsEnabled = false;
                vehicle.driveFreeSteerAngle = 0;
            }
            else
            {
                //turn ON free drive mode
                SetButtonImage(btnFreeDrive, "SteerDriveOn.png");
                btnFreeDrive.Background = Brushes.LightGreen;
                vehicle.isInFreeDriveMode = true;
                btnSteerAngleDown.IsEnabled = true;
                btnSteerAngleUp.IsEnabled = true;
                vehicle.driveFreeSteerAngle = 0;
                lblSteerAngle.Text = "0";
            }
        }

        /// <summary>[XPLAT] WinForms <c>btnFreeDriveZero_Click</c>.</summary>
        private void btnFreeDriveZero_Click(object sender, RoutedEventArgs e)
        {
            if (vehicle.driveFreeSteerAngle == 0)
                vehicle.driveFreeSteerAngle = 5;
            else vehicle.driveFreeSteerAngle = 0;
        }

        /// <summary>[XPLAT] WinForms <c>btnSteerAngleUp_MouseDown</c> (WinForms MouseDown -> Avalonia
        /// PointerPressed).</summary>
        private void btnSteerAngleUp_MouseDown(object sender, PointerPressedEventArgs e)
        {
            vehicle.driveFreeSteerAngle++;
            if (vehicle.driveFreeSteerAngle > 40) vehicle.driveFreeSteerAngle = 40;
        }

        /// <summary>[XPLAT] WinForms <c>btnSteerAngleDown_MouseDown</c> (WinForms MouseDown -> Avalonia
        /// PointerPressed).</summary>
        private void btnSteerAngleDown_MouseDown(object sender, PointerPressedEventArgs e)
        {
            vehicle.driveFreeSteerAngle--;
            if (vehicle.driveFreeSteerAngle < -40) vehicle.driveFreeSteerAngle = -40;
        }

        // ===============================================================================================
        // Steer-angle (turning-diameter) measurement toggle (source L915-L936).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>btnStartSA_Click</c>. Begins/ends the diameter measurement consumed
        /// by <see cref="Timer1_Tick"/>; <c>mf.pivotAxlePos</c> is read from the telemetry collaborator.</summary>
        private void btnStartSA_Click(object sender, RoutedEventArgs e)
        {
            if (!isSA)
            {
                isSA = true;
                startFix = tel.pivotAxlePos;
                dist = 0;
                diameter = 0;
                cntr = 0;
                SetButtonImage(btnStartSA, "boundaryStop.png");
                lblDiameter.Text = "0";
                lblCalcSteerAngleInner.Text = "Drive Steady";
            }
            else
            {
                isSA = false;
                lblCalcSteerAngleInner.Text = "0.0" + "\u00B0";
                SetButtonImage(btnStartSA, "BoundaryRecord.png");
            }
        }

        // ===============================================================================================
        // WAS zeroing (manual + Smart WAS) and the steer wizard launch (source L901-L913, L1294-L1361,
        // L1200-L1205).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>btnZeroWAS_Click</c> (source L901-L913). <c>mf.mc.actualSteerAngleDegrees</c>
        /// is read from telemetry; the WinForms modal <c>FormDialog.Show</c> becomes the async
        /// <see cref="FormDialogView.ShowAsync"/>.</summary>
        private async void btnZeroWAS_Click(object sender, RoutedEventArgs e)
        {
            int offset = (int)((int)hsbarCountsPerDegree.Value * -tel.actualSteerAngleDegrees + (int)hsbarWasOffset.Value);
            if (Math.Abs(offset) > 3900)
            {
                await FormDialogView.ShowAsync(this, "Exceeded Range",
                    "Excessive Steer Angle - Cannot Zero", DialogSeverity.Error);
                Log.EventWriter("Excessive Steer Angle, No Zero " + offset);
            }
            else
            {
                hsbarWasOffset.Value += (int)((int)hsbarCountsPerDegree.Value * -tel.actualSteerAngleDegrees);
            }
        }

        /// <summary>[XPLAT] WinForms <c>btnSmartZeroWAS_Click</c> (source L1294-L1325). The ±50-count safety
        /// guard, the offset application, and the resend prime (<c>toSend = true; counter = 6;</c>) are
        /// preserved exactly. Numeric formatting uses <see cref="CultureInfo.InvariantCulture"/>.</summary>
        private void btnSmartZeroWAS_Click(object sender, RoutedEventArgs e)
        {
            if (!smartWAS.HasValidCalibration)
            {
                return;
            }

            double recommendedOffset = smartWAS.RecommendedOffset;
            int offsetCounts = (int)Math.Round(recommendedOffset * (int)hsbarCountsPerDegree.Value);

            // Safety check - limit maximum offset
            if (Math.Abs(offsetCounts) > 50)
            {
                TimedMessageBox(3000, "Exceeded Range", "Offset too large - cannot apply");
                Log.EventWriter($"Smart WAS: Offset {offsetCounts} too large, rejected");
                return;
            }

            // Apply the offset
            hsbarWasOffset.Value += offsetCounts;

            // Apply correction to collected data to prevent double-correction
            smartWAS.ApplyOffsetCorrection(recommendedOffset);

            // Show confirmation
            TimedMessageBox(2500, "Smart WAS Zero",
                $"Applied {recommendedOffset.ToString("F2", CI)}\u00B0 ({offsetCounts} counts)");

            Log.EventWriter($"Smart WAS: Applied {recommendedOffset.ToString("F2", CI)}\u00B0 ({offsetCounts} counts)");

            toSend = true;
            counter = 6;
        }

        /// <summary>[XPLAT] WinForms <c>UpdateSmartWASStatus</c> (source L1331-L1361). <c>Label.ForeColor</c>
        /// maps to <c>Foreground</c> brushes; <c>Button.Text</c> maps to <c>Content</c>.</summary>
        private void UpdateSmartWASStatus()
        {
            lblSmartCalSamples.Text = "Samples: " + smartWAS.SampleCount.ToString(CI);
            lblSmartCalConfidence.Text = "Confidence: " + smartWAS.Confidence.ToString("F0", CI) + "%";

            if (smartWAS.IsCollecting)
            {
                if (smartWAS.HasValidCalibration)
                {
                    lblSmartCalStatus.Text = "Ready to Apply";
                    lblSmartCalStatus.Foreground = Brushes.Green;
                    btnSmartZeroWAS.Content = smartWAS.RecommendedOffset.ToString("+0.0;-0.0", CI) + "\u00B0";
                    btnSmartZeroWAS.IsEnabled = true;
                }
                else
                {
                    lblSmartCalStatus.Text = "Collecting...";
                    lblSmartCalStatus.Foreground = Brushes.Orange;
                    btnSmartZeroWAS.Content = "";
                    btnSmartZeroWAS.IsEnabled = false;
                }
            }
            else
            {
                lblSmartCalStatus.Text = "Stopped";
                lblSmartCalStatus.Foreground = Brushes.Gray;
                btnSmartZeroWAS.Content = "";
                btnSmartZeroWAS.IsEnabled = false;
            }
        }

        /// <summary>[XPLAT] WinForms <c>btnSteerWizard_Click</c> (source L1200-L1205): close this window and
        /// open the steer wizard. <c>new FormSteerWiz(mf).Show(mf)</c> becomes the injected production launcher
        /// (<see cref="openSteerWizard"/>) so the wizard is built with the SAME real ISteerWiz* adapters as the
        /// MainView entry point (QA Issue 3 — no inert Null-adapter wizard on a production path). The launch is
        /// deferred to this dialog's <see cref="Window.Closed"/> event so the launcher's modal
        /// <c>ShowDialog(mainView)</c> does not nest inside this closing modal. Only the designer/previewer
        /// (parameterless) path — where no launcher is injected — falls back to the inert preview wizard.</summary>
        private void btnSteerWizard_Click(object sender, RoutedEventArgs e)
        {
            if (openSteerWizard != null)
            {
                // Production path: defer until this dialog has fully closed, then open the real-adapter wizard.
                void ReopenAsWizard(object s, EventArgs ev)
                {
                    Closed -= ReopenAsWizard;
                    openSteerWizard();
                }

                Closed += ReopenAsWizard;
                Close();
                return;
            }

            // Designer/previewer fallback only (no injected launcher): preserve the original inert behavior.
            Close();
            var wiz = new FormSteerWizView();
            if (owner != null) wiz.Show(owner);
            else wiz.Show();
        }

        // ===============================================================================================
        // Helpers — [XPLAT] WinForms-to-Avalonia shims (mirror the FormSteerWizView code-behind exactly).
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>ComboBox.Text</c> getter over the selected ComboBoxItem content.</summary>
        private static string GetCombo(ComboBox cb) =>
            (cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

        /// <summary>[XPLAT] Select the ComboBoxItem whose Content matches <paramref name="value"/>.</summary>
        private static void SetCombo(ComboBox cb, string value)
        {
            foreach (var item in cb.Items)
            {
                if (item is ComboBoxItem cbi && (cbi.Content?.ToString() ?? string.Empty) == value)
                {
                    cb.SelectedItem = cbi;
                    return;
                }
            }
        }

        /// <summary>[XPLAT] WinForms <c>ToggleButton.Checked</c> -> Avalonia nullable <c>IsChecked</c>.</summary>
        private static bool IsChk(ToggleButton tb) => tb.IsChecked == true;

        /// <summary>[XPLAT] WinForms <c>NumericUpDown.Value</c>/<c>DecimalPlaces</c> display -> the nud
        /// Button's <c>Content</c>, formatted with the given decimals in invariant culture.</summary>
        private void SetNud(Button nud, double value, int decimals)
        {
            nud.Content = value.ToString("F" + decimals.ToString(CI), CI);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>TabControl.ItemSize</c> has no direct Avalonia equivalent (tab headers size
        /// to their content and the active theme). Best-effort: nudge each TabItem's minimum width toward
        /// the WinForms item width. Purely cosmetic — it never affects the behaviour-frozen PGN logic.
        /// </summary>
        private static void SetTabHeaderSize(TabControl tc, int width, int height)
        {
            foreach (var item in tc.Items)
            {
                if (item is TabItem ti) ti.MinWidth = width;
            }
        }

        /// <summary>[XPLAT] Load a packaged button image from <c>avares://AgOpenGPS/btnImages/</c>.</summary>
        private static Bitmap LoadBitmap(string fileName) =>
            new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));

        /// <summary>
        /// [XPLAT] WinForms <c>button.Image = Properties.Resources.X</c> -> set the content control's
        /// Content to an <see cref="Image"/>. Swallows asset-load failures (headless/preview host) so the
        /// behaviour-frozen logic is never blocked by a missing texture.
        /// </summary>
        private void SetButtonImage(ContentControl button, string fileName)
        {
            try
            {
                button.Content = new Image { Source = LoadBitmap(fileName) };
            }
            catch (Exception)
            {
                // asset unavailable (e.g. previewer / headless test host) — non-fatal.
            }
        }

        /// <summary>[XPLAT] WinForms <c>mf.TimedMessageBox(ms, title, msg)</c> -> a non-modal,
        /// self-closing <see cref="FormTimedMessageView"/>.</summary>
        private void TimedMessageBox(int timeMs, string title, string message)
        {
            try
            {
                var f = new FormTimedMessageView(timeMs, title, message);
                if (owner != null) f.Show(owner);
                else f.Show();
            }
            catch (Exception)
            {
                // headless/preview host without a visual root — non-fatal.
            }
        }
    }
}
