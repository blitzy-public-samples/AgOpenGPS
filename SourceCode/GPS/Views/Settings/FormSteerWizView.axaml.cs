// [XPLAT] migrated from net48/WinForms FormSteerWiz — see TRANSITION_MAP.md
//
// Avalonia code-behind for the AutoSteer Configuration WIZARD dialog. This is a faithful, 1:1
// BEHAVIOR-FROZEN reimplementation of the deleted WinForms Forms/Settings/FormSteerWiz.cs (1324 lines):
// the PGN 251/252 bitfield encoding and every numeric computation are reproduced byte-for-byte
// (AAP §0.2.2 / §0.7.1). The companion markup FormSteerWizView.axaml declares only the ~300 named
// controls (no bindings, no markup-declared handlers); this partial reaches them by x:Name exactly as
// the WinForms designer + handlers did, and wires every event in code (see WireEvents()).
//
// The WinForms shell reached the rest of the program through a single FormGPS "mf" god-object. Per the
// migration's decoupling mandate (AAP §0.3.2 / §0.6.1) that back-reference is replaced by a small set of
// constructor-injected collaborator interfaces (vehicle / steerConfig / telemetry / ahrs / gyd) plus the
// owning Window. The interfaces are declared locally with wizard-unique names so they never collide with
// the differently-shaped ISteerTelemetry already declared by FormGraphSteerView in this namespace; the
// concrete implementations are owned by the application host / extracted services. A parameterless
// constructor (required by Avalonia's compiled-XAML loader) chains to the injected one with inert Null
// collaborators so the view still loads in the designer/previewer.
//
// [XPLAT] Culture parity (AAP §0.6.5 — highest data-integrity rule): EVERY double/int ToString and Parse
// is pinned to CultureInfo.InvariantCulture (exposed here as CI) so a comma-decimal locale on Linux/macOS
// cannot corrupt the PGN-derived text, the calibration read-outs, or the persisted settings.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AgOpenGPS.Core.Models;        // VehicleConfig
using AgOpenGPS.Core.Translations;  // gStr
using AgOpenGPS.Helpers;            // ScreenHelper (on-screen recovery)
using AgOpenGPS.Properties;         // VehicleSettings, ToolSettings

namespace AgOpenGPS.Views.Settings
{
    // ===================================================================================================
    // Collaborator interfaces (wizard-local). These replace the WinForms FormGPS "mf" back-reference.
    // Wizard-unique names avoid colliding with FormGraphSteerView's ISteerTelemetry (same namespace,
    // incompatible contract). The Services agent owns the concrete implementations.
    // ===================================================================================================

    /// <summary>[XPLAT] Steer-config PGN 0xFC (252) frame + named byte indices. Mirrors the Core CPGN_FC
    /// object the WinForms wizard reached as <c>mf.p_252</c> (<c>.pgn</c> + index fields).</summary>
    public interface ISteerWizPgn252
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

    /// <summary>[XPLAT] Steer-config PGN 0xFB (251) frame + named byte indices. Mirrors the Core CPGN_FB
    /// object the WinForms wizard reached as <c>mf.p_251</c>.</summary>
    public interface ISteerWizPgn251
    {
        byte[] Pgn { get; }
        int Set0 { get; }
        int Set1 { get; }
        int MaxPulse { get; }
        int MinSpeed { get; }
    }

    /// <summary>[XPLAT] Steer-configuration transport: the two config PGN frames plus the loopback send.
    /// Replaces <c>mf.p_252</c>/<c>mf.p_251</c>/<c>mf.SendPgnToLoop</c>.</summary>
    public interface ISteerWizConfigService
    {
        ISteerWizPgn252 P252 { get; }
        ISteerWizPgn251 P251 { get; }
        void SendPgnToLoop(byte[] pgn);
    }

    /// <summary>[XPLAT] Mutable vehicle guidance parameters the wizard reads/writes (was <c>mf.vehicle</c>).
    /// <see cref="VehicleConfig"/> is the real Core geometry object; <see cref="HalfWheelTrack"/> folds in
    /// the former <c>mf.tram.halfWheelTrack</c> assignment from the vehicle-track keypad handler.</summary>
    public interface ISteerWizVehicle
    {
        double maxSteerAngle { get; set; }
        bool isInFreeDriveMode { get; set; }
        double driveFreeSteerAngle { get; set; }
        double stanleyDistanceErrorGain { get; set; }
        double stanleyHeadingErrorGain { get; set; }
        double stanleyIntegralGainAB { get; set; }
        double purePursuitIntegralGain { get; set; }
        double goalPointLookAheadMult { get; set; }
        VehicleConfig VehicleConfig { get; }
        double HalfWheelTrack { get; set; }
    }

    /// <summary>[XPLAT] Live steer/telemetry read-outs (was <c>mf.mc.*</c> + assorted <c>mf</c> members).
    /// These are read-only snapshots driven by the receive pipeline.</summary>
    public interface ISteerWizTelemetry
    {
        double actualSteerAngleDegrees { get; }
        int pwmDisplay { get; }
        int sensorData { get; }
        bool steerSwitchHigh { get; }
        bool isBtnAutoSteerOn { get; }
        int steerModuleConnectedCounter { get; }
        vec3 pivotAxlePos { get; }
        string SetSteerAngle { get; }
        string Heading { get; }
        string RollInDegrees { get; }
        double guidanceLineSteerAngle { get; }
        double m2InchOrCm { get; }
        double inchOrCm2m { get; }
    }

    /// <summary>[XPLAT] AHRS roll state the roll-zero / invert-roll tabs read and write (was <c>mf.ahrs</c>).</summary>
    public interface ISteerWizAhrs
    {
        double imuRoll { get; set; }
        double rollZero { get; set; }
        bool isRollInvert { get; set; }
    }

    /// <summary>[XPLAT] Guidance state for the side-hill compensation factor (was <c>mf.gyd</c>).</summary>
    public interface ISteerWizGuidance
    {
        double sideHillCompFactor { get; set; }
    }

    // --- Inert Null implementations (designer/previewer path only; never wired to real hardware) -------

    internal sealed class NullSteerWizPgn252 : ISteerWizPgn252
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

    internal sealed class NullSteerWizPgn251 : ISteerWizPgn251
    {
        // [XPLAT] CPGN_FB (251) layout — header 0x80 0x81 0x7F 0xFB, length 8, trailing CRC slot.
        public byte[] Pgn { get; } = new byte[] { 0x80, 0x81, 0x7f, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int Set0 => 5;
        public int MaxPulse => 6;
        public int MinSpeed => 7;
        public int Set1 => 8;
    }

    internal sealed class NullSteerWizConfigService : ISteerWizConfigService
    {
        public ISteerWizPgn252 P252 { get; } = new NullSteerWizPgn252();
        public ISteerWizPgn251 P251 { get; } = new NullSteerWizPgn251();
        public void SendPgnToLoop(byte[] pgn) { /* no-op: no loopback in the designer/previewer path */ }
    }

    internal sealed class NullSteerWizVehicle : ISteerWizVehicle
    {
        public double maxSteerAngle { get; set; }
        public bool isInFreeDriveMode { get; set; }
        public double driveFreeSteerAngle { get; set; }
        public double stanleyDistanceErrorGain { get; set; }
        public double stanleyHeadingErrorGain { get; set; }
        public double stanleyIntegralGainAB { get; set; }
        public double purePursuitIntegralGain { get; set; }
        public double goalPointLookAheadMult { get; set; }
        public VehicleConfig VehicleConfig { get; } = new VehicleConfig();
        public double HalfWheelTrack { get; set; }
    }

    internal sealed class NullSteerWizTelemetry : ISteerWizTelemetry
    {
        public double actualSteerAngleDegrees => 0;
        public int pwmDisplay => 0;
        public int sensorData => -1;
        public bool steerSwitchHigh => false;
        public bool isBtnAutoSteerOn => false;
        public int steerModuleConnectedCounter => 0;
        public vec3 pivotAxlePos => new vec3();
        public string SetSteerAngle => "0";
        public string Heading => "0";
        public string RollInDegrees => "0";
        public double guidanceLineSteerAngle => 0;
        public double m2InchOrCm => 1;
        public double inchOrCm2m => 1;
    }

    internal sealed class NullSteerWizAhrs : ISteerWizAhrs
    {
        public double imuRoll { get; set; }
        public double rollZero { get; set; }
        public bool isRollInvert { get; set; }
    }

    internal sealed class NullSteerWizGuidance : ISteerWizGuidance
    {
        public double sideHillCompFactor { get; set; }
    }

    // ===================================================================================================
    // The wizard view.
    // ===================================================================================================

    public partial class FormSteerWizView : Window
    {
        // [XPLAT] AAP §0.6.5 — invariant culture for ALL numeric ToString/Parse (parity + cross-platform).
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // --- Injected collaborators (replace the WinForms FormGPS "mf" back-reference) ------------------
        private readonly ISteerWizVehicle vehicle;
        private readonly ISteerWizConfigService steerConfig;
        private readonly ISteerWizTelemetry telemetry;
        private readonly ISteerWizAhrs ahrs;
        private readonly ISteerWizGuidance gyd;
        private readonly Window owner;

        // --- State (mirrors FormSteerWiz fields, source L17-L21) ----------------------------------------
        private bool toSend252 = false, toSend251 = false, isSARight = false, isSALeft = false;
        private int counter252 = 0, counter251 = 0, cntr;
        private vec3 startFix;
        private double diameter, steerAngleRight, steerAngleLeft, dist, startAngleLeft;
        private bool isWizardStarted = false;

        // [XPLAT] DispatcherTimer replaces the WinForms System.Windows.Forms.Timer (intervals from the
        // designer: timer1 = 250 ms, sideBarTimer = 1000 ms). Started in OnOpened, stopped in OnClosing.
        private DispatcherTimer timer1;
        private DispatcherTimer sideBarTimer;

        // [XPLAT] guards the bulk slider assignment during the Load port so ValueChanged handlers no-op
        // (the WinForms handlers fired during Load too, but were inert because isWizardStarted was false;
        // suppressing them here also avoids the transient min->low / low<->high coupling side effects).
        private bool isLoading = false;

        // [XPLAT] CheckSteerSwitch() in WinForms tested btnSteerStatus.BackColor == Color.Yellow. Reading a
        // brush back is awkward in Avalonia, so the steer-status tick records the "yellow" state here and
        // CheckSteerSwitch() returns it — preserving the original semantics exactly.
        private bool steerSwitchIsYellow = false;

        // [XPLAT] the WinForms nud* controls were NumericUpDowns with a .Value; their Avalonia counterparts
        // are display Buttons, so their numeric value is held here and the Button.Content shows it.
        private double nudWheelbaseValue, nudVehicleTrackValue, nudAntennaPivotValue, nudAntennaHeightValue, nudAntennaOffsetValue;
        private int nudMaxCountsValue;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / previewer. Chains
        /// to the injected constructor with inert Null collaborators and no owner so the view still loads.
        /// </summary>
        public FormSteerWizView()
            : this(new NullSteerWizVehicle(), new NullSteerWizConfigService(), new NullSteerWizTelemetry(),
                   new NullSteerWizAhrs(), new NullSteerWizGuidance(), null)
        {
        }

        /// <summary>
        /// [XPLAT] Primary constructor. Parameter names mirror the original wizard's collaborators
        /// (vehicle / steerConfig / telemetry / ahrs / gyd) plus the owning window for dialog parenting.
        /// </summary>
        public FormSteerWizView(
            ISteerWizVehicle vehicle,
            ISteerWizConfigService steerConfig,
            ISteerWizTelemetry telemetry,
            ISteerWizAhrs ahrs,
            ISteerWizGuidance gyd,
            Window owner)
        {
            this.vehicle = vehicle ?? new NullSteerWizVehicle();
            this.steerConfig = steerConfig ?? new NullSteerWizConfigService();
            this.telemetry = telemetry ?? new NullSteerWizTelemetry();
            this.ahrs = ahrs ?? new NullSteerWizAhrs();
            this.gyd = gyd ?? new NullSteerWizGuidance();
            this.owner = owner;

            InitializeComponent();

            // Localised labels + window title (FormSteerWiz ctor, source L36-L38).
            label3.Text = gStr.gsAgressiveness;
            label5.Text = gStr.gsOvershootReduction;
            Title = gStr.gsAutoSteerConfiguration;

            // [XPLAT] timers (designer: timer1.Interval = 250, sideBarTimer.Interval = 1000).
            timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer1.Tick += Timer1_Tick;
            sideBarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            sideBarTimer.Tick += SideBarTimer_Tick;

            WireEvents();
        }

        /// <summary>
        /// [XPLAT] Subscribes every control event in code, reproducing the WinForms designer's event wiring
        /// exactly (the partner .axaml declares no handlers). Grouped to match the original handler sharing.
        /// </summary>
        private void WireEvents()
        {
            // Sliders (.ValueChanged). NOTE: hsbarWasOffset is wired to the SensorZero handler, exactly as
            // the WinForms designer did; the hidden hsbarSteerAngleSensorZero slider has no handler.
            hsbarMinPWM.ValueChanged += HsbarMinPWM_ValueChanged;
            hsbarProportionalGain.ValueChanged += HsbarProportionalGain_ValueChanged;
            hsbarLowSteerPWM.ValueChanged += HsbarLowSteerPWM_ValueChanged;
            hsbarHighSteerPWM.ValueChanged += HsbarHighSteerPWM_ValueChanged;
            hsbarAckerman.ValueChanged += HsbarAckerman_ValueChanged;
            hsbarMaxSteerAngle.ValueChanged += HsbarMaxSteerAngle_ValueChanged;
            hsbarCountsPerDegree.ValueChanged += HsbarCountsPerDegree_ValueChanged;
            hsbarWasOffset.ValueChanged += HsbarSteerAngleSensorZero_ValueChanged;
            hsbarStanleyGain.ValueChanged += HsbarStanleyGain_ValueChanged;
            hsbarHeadingErrorGain.ValueChanged += HsbarHeadingErrorGain_ValueChanged;
            hsbarIntegral.ValueChanged += HsbarIntegral_ValueChanged;
            hsbarIntegralPurePursuit.ValueChanged += HsbarIntegralPurePursuit_ValueChanged;
            hsbarSideHillComp.ValueChanged += HsbarSideHillComp_ValueChanged;
            hsbarLookAheadMult.ValueChanged += HsbarLookAheadMult_ValueChanged;
            hsbarSensor.ValueChanged += HsbarSensor_Scroll;

            // ComboBoxes (.SelectionChanged -> WinForms .SelectedIndexChanged).
            cboxMotorDrive.SelectionChanged += Combo251_SelectionChanged;
            cboxSteerEnable.SelectionChanged += Combo251_SelectionChanged;
            cboxConv.SelectionChanged += Combo251_SelectionChanged;

            // CheckBoxes. chk* + cboxDanfoss used WinForms CheckedChanged -> Avalonia IsCheckedChanged.
            chkInvertWAS.IsCheckedChanged += Toggle251;
            chkInvertSteer.IsCheckedChanged += Toggle251;
            chkSteerInvertRelays.IsCheckedChanged += Toggle251;
            cboxDanfoss.IsCheckedChanged += Toggle251;
            cboxDataInvertRoll.Click += CboxDataInvertRoll_Click;
            // Sensor trio used WinForms Click -> mutual-exclusion handler.
            cboxEncoder.Click += CboxCancelGuidance_Click;
            cboxPressureSensor.Click += CboxCancelGuidance_Click;
            cboxCurrentSensor.Click += CboxCancelGuidance_Click;

            // Shared "advance one tab" (btnOkNext_Click). Plain btnOkNext was intentionally not wired.
            foreach (var b in new Button[]
            {
                btnOkNext_LoadDefault, button2, button8, button10, button14, button12, btnOkNext_ButtonSwitch,
                btnOkNext_A2D, btnOkNext_MotorDriver, btnOkNext_InvertRelays, btnOkNext_Danfoss, button18,
                button19, btnOkWAS, btnOkNext_WAS_Zero, btnOkNext_MotorDirection, btnOkNext_CountsPerDeg,
                button17, btnOkNextMaxSteerAngle, btnOkNext_PanicStop, btnOK_Next, btnNext_PGain
            })
            {
                b.Click += BtnOkNext_Click;
            }

            // Shared "go back" (btnPrev_Click).
            foreach (var b in new Button[]
            {
                button1, button7, button9, button13, button11, button15, btnPrev_A2D, btnPrev_MotorDriver,
                btnPrev_InvertRelays, btnPrev_Danfoss, button4, button3, btnPrev_InvertWAS, button6,
                btnPrev_MotorDirection, btnPrev_CountsPerDegree, button5, button16, btnPrev_MaxSteerAngle,
                btnPrev_CancelGuidance, btnPrev_Panic, btnPrev_Gain, btnPrev_PGain, btnPrev_End
            })
            {
                b.Click += BtnPrev_Click;
            }

            // Exit / close-all.
            btnExit.Click += BtnExit_Click;
            btnCloseAll.Click += BtnExit_Click;

            // Specific button handlers.
            btnStartWizard.Click += BtnStartWizard_Click;
            btnLoadDefaults.Click += BtnLoadDefaults_Click;
            btnStopWizard.Click += BtnStopWizard_Click;
            btnRestartWizard.Click += BtnRestartWizard_Click;
            btnRemoveWasOffset.Click += BtnRemoveWasOffset_Click;
            btnZeroWAS.Click += BtnZeroWAS_Click;
            btnStartSA.Click += BtnStartSA_Click;
            btnStartSA_Left.Click += BtnStartSA_Left_Click;
            btnAckReset.Click += BtnAckReset_Click;
            btnOkSetMaximumSteerAngle.Click += BtnOkSetMaximumSteerAngle_Click;
            btnSkipCPD_Setup.Click += BtnSkipCPD_Setup_Click;
            btnOKNext_CPDSetup.Click += BtnOKNext_CPDSetup_Click;
            btnOkNextCancelGuidance.Click += BtnOkNextCancelGuidance_Click;
            btnMinGainLeft.Click += BtnMinGainLeft_Click;
            btnMinGainRight.Click += BtnMinGainRight_Click;
            btnZeroMinMovementSetting.Click += BtnZeroMinMovementSetting_Click;
            btnLeftPGain.Click += BtnLeftPGain_Click;
            btnRightPGain.Click += BtnRightPGain_Click;
            btnZeroPGain.Click += BtnZeroPGain_Click;
            btnZeroRoll.Click += BtnZeroRoll_Click;
            btnRemoveZeroOffset.Click += BtnRemoveZeroOffset_Click;
            btnFreeDrive.Click += BtnFreeDrive_Click;
            btnFreeDriveZero.Click += BtnFreeDriveZero_Click;
            btnSteerLeft.Click += BtnSteerLeft_Click;
            btnSteerRight.Click += BtnSteerRight_Click;

            // NUD keypad buttons.
            nudWheelbase.Click += NudWheelbase_Click;
            nudVehicleTrack.Click += NudVehicleTrack_Click;
            nudAntennaPivot.Click += NudAntennaPivot_Click;
            nudAntennaHeight.Click += NudAntennaHeight_Click;
            nudAntennaOffset.Click += NudAntennaOffset_Click;
            nudMaxCounts.Click += NudMaxCounts_Click;

            // Free-drive nudge RepeatButtons (WinForms MouseDown -> Avalonia PointerPressed).
            btnSteerAngleUp.PointerPressed += BtnSteerAngleUp_PointerPressed;
            btnSteerAngleDown.PointerPressed += BtnSteerAngleDown_PointerPressed;

            // Hidden-header tab navigation: a single SelectionChanged reproduces the per-tab Enter/Leave
            // FreeDrive choreography (tabMotorDirection, tab_MinimumGain, tabPGain).
            tabWiz.SelectionChanged += TabWiz_SelectionChanged;
        }

        // ===============================================================================================
        // Lifecycle — Load (OnOpened) and Close (OnClosing).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Window.Opened is the Avalonia analogue of the WinForms <c>Load</c> event: the named
        /// controls are realised by now. Runs the settings-to-UI port and then starts the two timers.
        /// (In the designer/previewer the window is never shown, so OnOpened — and therefore the Null
        /// collaborators — are never exercised.)
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadSettingsToUi();
            timer1.Start();
            sideBarTimer.Start();
        }

        /// <summary>
        /// [XPLAT] Faithful port of WinForms <c>FormSteer_Load</c> (source L43-L219). The bulk slider
        /// assignment is bracketed by <see cref="isLoading"/> so the ValueChanged handlers no-op during
        /// load; every label the suppressed handlers would have set is set explicitly here (including
        /// <c>lblSideHillComp</c>, which the WinForms code set only via its handler).
        /// </summary>
        private void LoadSettingsToUi()
        {
            isLoading = true;

            // WinForms hid the tab headers here (Appearance/ItemSize/SizeMode); that styling lives in the
            // XAML. pbarProgress spans 0..(tab count - 1) (WinForms used tabWiz.TabCount - 1).
            pbarProgress.Maximum = tabWiz.ItemCount - 1;

            hsbarWasOffset.Value = VehicleSettings.Default.setAS_wasOffset;
            hsbarCountsPerDegree.Value = VehicleSettings.Default.setAS_countsPerDegree;

            lblCountsPerDegree.Text = ((int)hsbarCountsPerDegree.Value).ToString(CI);
            lblSteerAngleSensorZero.Text =
                (hsbarWasOffset.Value / (double)((int)hsbarCountsPerDegree.Value)).ToString("N2", CI);

            hsbarAckerman.Value = VehicleSettings.Default.setAS_ackerman;
            lblAckerman.Text = ((int)hsbarAckerman.Value).ToString(CI);

            // min pwm, kP
            hsbarMinPWM.Value = VehicleSettings.Default.setAS_minSteerPWM;
            lblMinPWM.Text = ((int)hsbarMinPWM.Value).ToString(CI);

            hsbarProportionalGain.Value = VehicleSettings.Default.setAS_Kp;
            lblProportionalGain.Text = ((int)hsbarProportionalGain.Value).ToString(CI);

            // low steer, high steer
            hsbarLowSteerPWM.Value = VehicleSettings.Default.setAS_lowSteerPWM;
            lblLowSteerPWM.Text = ((int)hsbarLowSteerPWM.Value).ToString(CI);

            hsbarHighSteerPWM.Value = VehicleSettings.Default.setAS_highSteerPWM;
            lblHighSteerPWM.Text = ((int)hsbarHighSteerPWM.Value).ToString(CI);

            hsbarMaxSteerAngle.Value = (short)VehicleSettings.Default.setVehicle_maxSteerAngle;
            lblMaxSteerAngle.Text = ((int)hsbarMaxSteerAngle.Value).ToString(CI);

            vehicle.stanleyDistanceErrorGain = ToolSettings.Default.stanleyDistanceErrorGain;
            hsbarStanleyGain.Value = (short)(vehicle.stanleyDistanceErrorGain * 10);
            lblStanleyGain.Text = vehicle.stanleyDistanceErrorGain.ToString(CI);

            vehicle.stanleyHeadingErrorGain = ToolSettings.Default.stanleyHeadingErrorGain;
            hsbarHeadingErrorGain.Value = (short)(vehicle.stanleyHeadingErrorGain * 10);
            lblHeadingErrorGain.Text = vehicle.stanleyHeadingErrorGain.ToString(CI);

            hsbarIntegral.Value = (int)(ToolSettings.Default.stanleyIntegralGainAB * 100);
            lblIntegralPercent.Text = ((int)(vehicle.stanleyIntegralGainAB * 100)).ToString(CI);

            hsbarIntegralPurePursuit.Value = (int)(ToolSettings.Default.purePursuitIntegralGainAB * 100);
            lblPureIntegral.Text = ((int)(vehicle.purePursuitIntegralGain * 100)).ToString(CI);

            // [XPLAT] lblSideHillComp is set explicitly (in WinForms its hsbarSideHillComp.Value assignment
            // fired the handler that set the label; here the handler is suppressed during load).
            int shc = (int)(VehicleSettings.Default.setAS_sideHillComp * 100);
            hsbarSideHillComp.Value = shc;
            lblSideHillComp.Text = (shc * 0.01).ToString("N2", CI) + "\u00B0";
            gyd.sideHillCompFactor = VehicleSettings.Default.setAS_sideHillComp;

            vehicle.goalPointLookAheadMult = ToolSettings.Default.setVehicle_goalPointLookAheadMult;
            hsbarLookAheadMult.Value = (short)(vehicle.goalPointLookAheadMult * 10);
            lblLookAheadMult.Text = vehicle.goalPointLookAheadMult.ToString(CI);

            // Antenna / wheelbase / track display nuds (Buttons): value = setting * m2InchOrCm.
            nudAntennaPivotValue = (int)(VehicleSettings.Default.setVehicle_antennaPivot * telemetry.m2InchOrCm);
            nudAntennaPivot.Content = NudText(nudAntennaPivotValue);
            nudAntennaHeightValue = (int)(VehicleSettings.Default.setVehicle_antennaHeight * telemetry.m2InchOrCm);
            nudAntennaHeight.Content = NudText(nudAntennaHeightValue);
            nudAntennaOffsetValue = (int)(VehicleSettings.Default.setVehicle_antennaOffset * telemetry.m2InchOrCm);
            nudAntennaOffset.Content = NudText(nudAntennaOffsetValue);
            nudWheelbaseValue = (int)(Math.Abs(VehicleSettings.Default.setVehicle_wheelbase) * telemetry.m2InchOrCm);
            nudWheelbase.Content = NudText(nudWheelbaseValue);
            nudVehicleTrackValue = (int)(Math.Abs(VehicleSettings.Default.setVehicle_trackWidth) * telemetry.m2InchOrCm);
            nudVehicleTrack.Content = NudText(nudVehicleTrackValue);

            cboxDataInvertRoll.IsChecked = VehicleSettings.Default.setIMU_invertRoll;
            ahrs.isRollInvert = VehicleSettings.Default.setIMU_invertRoll;

            lblRollZeroOffset.Text = ((double)VehicleSettings.Default.setIMU_rollZero).ToString("N2", CI);
            ahrs.rollZero = VehicleSettings.Default.setIMU_rollZero;
            lblRollZeroOffset.Text = "0.00";   // WinForms sets the label twice; the final "0.00" wins.

            // make sure free drive is off
            SetButtonImage(btnFreeDrive, "SteerDriveOff.png");
            vehicle.isInFreeDriveMode = false;
            btnSteerAngleDown.IsEnabled = false;
            btnSteerAngleUp.IsEnabled = false;
            btnFreeDriveZero.IsEnabled = false;
            vehicle.driveFreeSteerAngle = 0;

            toSend252 = false;
            toSend251 = false;

            // setting0 decode (source L123-L146).
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
            nudMaxCounts.Content = nudMaxCountsValue.ToString(CI);
            hsbarSensor.Value = (int)VehicleSettings.Default.setArdSteer_maxPulseCounts;
            lblhsbarSensor.Text = ((int)((double)hsbarSensor.Value * 0.3921568627)).ToString(CI) + "%";

            // setting1 decode (source L151-L160).
            sett = VehicleSettings.Default.setArdSteer_setting1;
            cboxDanfoss.IsChecked = (sett & 1) != 0;
            cboxPressureSensor.IsChecked = (sett & 2) != 0;
            cboxCurrentSensor.IsChecked = (sett & 4) != 0;

            // Sensor mutual-exclusion + visibility (source L162-L212), including the final else's return.
            if (cboxEncoder.IsChecked == true)
            {
                cboxPressureSensor.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                label61.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = true;
                pbarSensor.IsVisible = false;
                hsbarSensor.IsVisible = false;
                lblhsbarSensor.IsVisible = false;
                label61.Text = gStr.gsEncoderCounts;
            }
            else if (cboxPressureSensor.IsChecked == true)
            {
                cboxEncoder.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                label61.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = true;
                hsbarSensor.IsVisible = true;
                lblhsbarSensor.IsVisible = true;
                label61.Text = "Off at %";
            }
            else if (cboxCurrentSensor.IsChecked == true)
            {
                cboxPressureSensor.IsChecked = false;
                cboxEncoder.IsChecked = false;
                label61.IsVisible = true;
                lblPercentFS.IsVisible = true;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = true;
                hsbarSensor.IsVisible = true;
                lblhsbarSensor.IsVisible = true;
                label61.Text = "Off at %";
            }
            else
            {
                cboxPressureSensor.IsChecked = false;
                cboxCurrentSensor.IsChecked = false;
                cboxEncoder.IsChecked = false;
                label61.IsVisible = false;
                lblPercentFS.IsVisible = false;
                nudMaxCounts.IsVisible = false;
                pbarSensor.IsVisible = false;
                hsbarSensor.IsVisible = false;
                lblhsbarSensor.IsVisible = false;
                isLoading = false;   // [XPLAT] release the guard before the early return (parity).
                return;
            }

            // [XPLAT] on-screen recovery (WinForms: if (!ScreenHelper.IsOnScreen(Bounds)) { Top=0; Left=0; }).
            if (!ScreenHelper.IsOnScreen(this))
            {
                Position = new PixelPoint(0, 0);
            }

            isLoading = false;
        }

        /// <summary>
        /// [XPLAT] Port of WinForms <c>FormSteer_FormClosing</c> (source L221-L254). Persists the gains,
        /// re-encodes PGN 252, saves all three settings stores, and — unlike WinForms, whose timer was
        /// disposed with the form — explicitly stops the two DispatcherTimers.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            vehicle.isInFreeDriveMode = false;

            ToolSettings.Default.stanleyHeadingErrorGain = vehicle.stanleyHeadingErrorGain;
            ToolSettings.Default.stanleyDistanceErrorGain = vehicle.stanleyDistanceErrorGain;
            ToolSettings.Default.stanleyIntegralGainAB = vehicle.stanleyIntegralGainAB;

            ToolSettings.Default.purePursuitIntegralGainAB = vehicle.purePursuitIntegralGain;
            ToolSettings.Default.setVehicle_goalPointLookAheadMult = vehicle.goalPointLookAheadMult;

            VehicleSettings.Default.setVehicle_maxSteerAngle = vehicle.maxSteerAngle;

            Apply252ToPgn();

            hsbarSideHillComp.Value = (int)(VehicleSettings.Default.setAS_sideHillComp * 100);

            VehicleSettings.Default.setIMU_invertRoll = ahrs.isRollInvert;

            // save current vehicle and tool (Settings is fully-qualified to avoid the namespace clash with
            // the enclosing AgOpenGPS.Views.Settings namespace).
            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
            AgOpenGPS.Properties.Settings.Default.Save();

            // [XPLAT] Avalonia timers are not disposed with the window — stop them explicitly.
            timer1.Stop();
            sideBarTimer.Stop();

            base.OnClosing(e);
        }

        // ===============================================================================================
        // PGN encode helpers (BEHAVIOR-FROZEN — byte layouts identical to net48 FormSteerWiz).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN: PGN 252 (0xFC) byte packing identical to net48 FormSteerWiz
        /// (source L234-L244 / L363-L373). Each statement assigns BOTH the persisted setting and the PGN
        /// byte. Avalonia's Slider.Value is a double, so an explicit <c>(int)</c> bridge precedes the
        /// verbatim <c>unchecked((byte)...)</c> casts and the <c>&gt;&gt;</c> shift.
        /// </summary>
        private void Apply252ToPgn()
        {
            VehicleSettings.Default.setAS_countsPerDegree =
                steerConfig.P252.Pgn[steerConfig.P252.CountsPerDegree] = unchecked((byte)(int)hsbarCountsPerDegree.Value);
            VehicleSettings.Default.setAS_ackerman =
                steerConfig.P252.Pgn[steerConfig.P252.Ackerman] = unchecked((byte)(int)hsbarAckerman.Value);

            VehicleSettings.Default.setAS_wasOffset = (int)hsbarWasOffset.Value;
            steerConfig.P252.Pgn[steerConfig.P252.WasOffsetHi] = unchecked((byte)((int)hsbarWasOffset.Value >> 8));
            steerConfig.P252.Pgn[steerConfig.P252.WasOffsetLo] = unchecked((byte)(int)hsbarWasOffset.Value);

            VehicleSettings.Default.setAS_highSteerPWM =
                steerConfig.P252.Pgn[steerConfig.P252.HighPWM] = unchecked((byte)(int)hsbarHighSteerPWM.Value);
            VehicleSettings.Default.setAS_lowSteerPWM =
                steerConfig.P252.Pgn[steerConfig.P252.LowPWM] = unchecked((byte)(int)hsbarLowSteerPWM.Value);
            VehicleSettings.Default.setAS_Kp =
                steerConfig.P252.Pgn[steerConfig.P252.GainProportional] = unchecked((byte)(int)hsbarProportionalGain.Value);
            VehicleSettings.Default.setAS_minSteerPWM =
                steerConfig.P252.Pgn[steerConfig.P252.MinPWM] = unchecked((byte)(int)hsbarMinPWM.Value);
        }

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN: PGN 251 (0xFB) bitfield layout identical to net48 FormSteerWiz
        /// (source L384-L478). The <c>set</c>/<c>reset</c>/<c>sett</c> cadence is reproduced verbatim.
        /// Includes the in-block <c>Save()</c> and the four <c>p_251</c> byte assignments; the caller
        /// performs the loopback send.
        /// </summary>
        private void Apply251ToPgn()
        {
            int set = 1;
            int reset = 2046;
            int sett = 0;

            if (chkInvertWAS.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (chkSteerInvertRelays.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (chkInvertSteer.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (ConvText() == "Single") sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (MotorDriveText() == "Cytron") sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (SteerEnableText() == "Switch") sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (SteerEnableText() == "Button") sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (cboxEncoder.IsChecked == true) sett |= set;
            else sett &= reset;

            VehicleSettings.Default.setArdSteer_setting0 = (byte)sett;
            VehicleSettings.Default.setArdMac_isDanfoss = cboxDanfoss.IsChecked == true;

            if (cboxCurrentSensor.IsChecked == true || cboxPressureSensor.IsChecked == true)
            {
                VehicleSettings.Default.setArdSteer_maxPulseCounts = (byte)(int)hsbarSensor.Value;
            }
            else
            {
                VehicleSettings.Default.setArdSteer_maxPulseCounts = (byte)nudMaxCountsValue;
            }

            // Settings1
            set = 1;
            reset = 2046;
            sett = 0;

            if (cboxDanfoss.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (cboxPressureSensor.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (cboxCurrentSensor.IsChecked == true) sett |= set;
            else sett &= reset;

            VehicleSettings.Default.setArdSteer_setting1 = (byte)sett;

            VehicleSettings.Default.Save();

            steerConfig.P251.Pgn[steerConfig.P251.Set0] = VehicleSettings.Default.setArdSteer_setting0;
            steerConfig.P251.Pgn[steerConfig.P251.Set1] = VehicleSettings.Default.setArdSteer_setting1;
            steerConfig.P251.Pgn[steerConfig.P251.MaxPulse] = VehicleSettings.Default.setArdSteer_maxPulseCounts;
            steerConfig.P251.Pgn[steerConfig.P251.MinSpeed] =
                unchecked((byte)(VehicleSettings.Default.setAS_minSteerSpeed * 10)); // 0.5 kmh
        }

        /// <summary>[XPLAT] Integer display text for the keypad (nud) buttons, invariant-culture.</summary>
        private static string NudText(double value) => ((int)value).ToString(CI);

        // ===============================================================================================
        // Timers — main telemetry tick (250 ms) and side-bar tick (1000 ms).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Port of WinForms <c>Timer1_Tick</c> (source L256-L509): the steer-angle calibration
        /// capture, the live steer/error/PWM readouts, the throttled PGN 252/251 re-sends, the sensor bar,
        /// and the steer-status indicator colour. <see cref="CExtensionMethods"/>.SetProgressNoAnimation is
        /// replaced by a direct <c>.Value</c> assignment (Avalonia ProgressBar has no animation to defeat).
        /// </summary>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (isSARight)
            {
                dist = glm.Distance(startFix, telemetry.pivotAxlePos);
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
                    isSARight = false;

                    try
                    {
                        //force cpd a bit on the low side
                        double cpd = (telemetry.actualSteerAngleDegrees / steerAngleRight * hsbarCountsPerDegree.Value);
                        cpd *= 0.9;
                        hsbarCountsPerDegree.Value = (int)cpd;
                        lblCPDError.Text = "CPD set to: " + ((int)hsbarCountsPerDegree.Value).ToString(CI);
                    }
                    catch (Exception)
                    {
                        hsbarCountsPerDegree.Value = 100;
                        lblCPDError.Text = "Error, CPD set to 100";
                    }
                }
            }

            if (isSALeft)
            {
                dist = glm.Distance(startFix, telemetry.pivotAxlePos);
                cntr++;
                if (dist > diameter)
                {
                    diameter = dist;
                    cntr = 0;
                }
                lblDiameterLeft.Text = diameter.ToString("N2", CI) + " m";

                if (cntr > 9)
                {
                    steerAngleLeft = Math.Atan(vehicle.VehicleConfig.Wheelbase / ((diameter - vehicle.VehicleConfig.TrackWidth * 0.5) / 2));
                    steerAngleLeft = glm.toDegrees(steerAngleLeft);

                    lblCalcSteerAngleLeft.Text = steerAngleLeft.ToString("N1", CI) + "\u00B0";
                    lblDiameterLeft.Text = diameter.ToString("N2", CI) + " m";
                    SetButtonImage(btnStartSA_Left, "BoundaryRecord.png");
                    isSALeft = false;

                    try
                    {
                        hsbarAckerman.Value = (int)((steerAngleLeft / Math.Abs(startAngleLeft)) * 100);
                        lblAckermannError.Text = "Ackermann Set to: " + ((int)hsbarAckerman.Value).ToString(CI);
                    }
                    catch (Exception)
                    {
                        hsbarAckerman.Value = 100;
                        lblAckermannError.Text = "Error, Ackermann set to 100";
                    }
                }
            }

            //steering bar
            double actAng = telemetry.actualSteerAngleDegrees;
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

            //wizard progress bar
            pbarProgress.Value = tabWiz.SelectedIndex;

            lblSteerAngle.Text = telemetry.SetSteerAngle;
            lblSteerAngleActual.Text = telemetry.actualSteerAngleDegrees.ToString("N1", CI) + "\u00B0";
            double err = (telemetry.actualSteerAngleDegrees - telemetry.guidanceLineSteerAngle * 0.01);
            lblError.Text = Math.Abs(err).ToString("N1", CI) + "\u00B0";
            lblError.Foreground = err > 0 ? Brushes.Red : Brushes.DarkGreen;

            lblPWMDisplay.Text = telemetry.pwmDisplay.ToString(CI);
            counter252++;
            counter251++;

            if (toSend252 && counter252 > 3)
            {
                // [XPLAT] BEHAVIOR-FROZEN: PGN 252 (0xFC) byte layout identical to net48 FormSteerWiz.
                Apply252ToPgn();

                VehicleSettings.Default.Save();

                steerConfig.SendPgnToLoop(steerConfig.P252.Pgn);
                toSend252 = false;
                counter252 = 0;
            }
            //*************************************************************
            else if (toSend251 && counter251 > 3)
            {
                // [XPLAT] BEHAVIOR-FROZEN: PGN 251 (0xFB) bitfield layout identical to net48 FormSteerWiz.
                // Apply251ToPgn() performs the verbatim setting0/setting1 encode, the in-block Save(), and
                // the four p_251 byte assignments; the loopback send happens here exactly as in WinForms.
                Apply251ToPgn();

                steerConfig.SendPgnToLoop(steerConfig.P251.Pgn);

                toSend251 = false;
                counter251 = 0;
            }

            if (hsbarMinPWM.Value > hsbarLowSteerPWM.Value) lblMinPWM.Foreground = Brushes.OrangeRed;
            else lblMinPWM.Foreground = Brushes.Black; // WinForms SystemColors.ControlText == default TextBlock Black

            if (telemetry.sensorData != -1)
            {
                int sd = telemetry.sensorData;
                if (sd < 0 || sd > 255) sd = 0; // [XPLAT] local clamp (WinForms mutated mf.mc.sensorData)
                pbarSensor.Value = sd;
                lblPercentFS.Text = ((int)((double)sd * 0.3921568627)).ToString(CI) + "%";
            }

            // Emulate the OGL Steer circle
            if (telemetry.steerSwitchHigh)
            {
                btnSteerStatus.Background = Brushes.Red;
                steerSwitchIsYellow = false;
            }
            else if (telemetry.isBtnAutoSteerOn)
            {
                btnSteerStatus.Background = Brushes.Green;
                steerSwitchIsYellow = false;
            }
            else
            {
                btnSteerStatus.Background = Brushes.Yellow;
                steerSwitchIsYellow = true;
            }

            //we have lost connection to steer module
            if (telemetry.steerModuleConnectedCounter > 30)
            {
                btnSteerStatus.Background = Brushes.Magenta;
                steerSwitchIsYellow = false; // [XPLAT] keep CheckSteerSwitch() in sync with the final colour
            }
        }

        /// <summary>
        /// [XPLAT] Port of WinForms <c>sideBarTimer_Tick</c> (source L511-L544). WinForms keyed off
        /// <c>tabWiz.SelectedTab.Name</c>; Avalonia exposes the active page via <c>SelectedItem</c> cast to
        /// <see cref="TabItem"/> whose <c>Name</c> carries the x:Name.
        /// </summary>
        private void SideBarTimer_Tick(object sender, EventArgs e)
        {
            string tabName = (tabWiz.SelectedItem as TabItem)?.Name ?? string.Empty;

            //roll zero
            if (tabName == "tabWAS_Zero") lblCurrentHeading.Text = telemetry.Heading;

            //ackermann
            else if (tabName == "tabAckCPD")
            {
                if ((int)hsbarAckerman.Value != 100)
                {
                    btnStartSA_Left.IsEnabled = false;
                }
                else
                {
                    btnStartSA_Left.IsEnabled = true;
                }
            }

            //roll invert
            else if (tabName == "tabRollInv")
            {
                lblRoll.Text = telemetry.RollInDegrees;
            }

            //roll zero
            else if (tabName == "tabRollZero")
            {
                lblRoll2.Text = telemetry.RollInDegrees;
            }

            lblBarAck.Text = ((int)hsbarAckerman.Value).ToString(CI);
            lblBarWasOffset.Text = ((int)hsbarWasOffset.Value).ToString(CI);
            lblBarCPD.Text = ((int)hsbarCountsPerDegree.Value).ToString(CI);
        }

        // ===============================================================================================
        // ComboBox text accessors (WinForms ComboBox.Text -> Avalonia selected ComboBoxItem.Content).
        // ===============================================================================================

        /// <summary>[XPLAT] Selected item's display text for a string-item ComboBox ("" when none).</summary>
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

        private string ConvText() => GetCombo(cboxConv);
        private string MotorDriveText() => GetCombo(cboxMotorDrive);
        private string SteerEnableText() => GetCombo(cboxSteerEnable);

        // ===============================================================================================
        // Asset, dialog and steer-switch helpers.
        // ===============================================================================================

        /// <summary>[XPLAT] WinForms <c>Properties.Resources.*</c> bitmap -> Avalonia avares image.</summary>
        private static Bitmap LoadBitmap(string fileName) =>
            new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));

        /// <summary>
        /// [XPLAT] WinForms <c>button.Image = Properties.Resources.X</c> -> set the content control's
        /// Content to an <see cref="Image"/>. Swallows asset-load failures (headless/preview) so the
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

        /// <summary>
        /// [XPLAT] WinForms <c>mf.TimedMessageBox</c> -> non-modal <see cref="FormTimedMessageView"/>.
        /// Always shown via Show (never ShowDialog) so it auto-closes without blocking the wizard.
        /// </summary>
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

        /// <summary>
        /// [XPLAT] WinForms tested <c>btnSteerStatus.BackColor == Color.Yellow</c>; the indicator colour is
        /// recomputed every <see cref="Timer1_Tick"/> and mirrored into <see cref="steerSwitchIsYellow"/>.
        /// </summary>
        private bool CheckSteerSwitch() => steerSwitchIsYellow;

        // ===============================================================================================
        // ButtonControl region — defaults & wizard start/stop/exit (source L546-L627).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Port of WinForms <c>btnLoadDefaults_Click</c> (source L548-L605). Restores the EXACT
        /// wizard defaults (distinct from FormSteer's reset), primes both PGN re-send latches, saves, then
        /// re-runs the load to push the defaults back into the UI.
        /// </summary>
        private void BtnLoadDefaults_Click(object sender, RoutedEventArgs e)
        {
            TimedMessageBox(2000, "Reset To Default", "Values Set to Inital Default");
            VehicleSettings.Default.setVehicle_maxSteerAngle = vehicle.maxSteerAngle = 45;

            VehicleSettings.Default.setAS_countsPerDegree = 100;

            VehicleSettings.Default.setAS_ackerman = 100;

            VehicleSettings.Default.setAS_wasOffset = 0;

            VehicleSettings.Default.setAS_highSteerPWM = 150;
            VehicleSettings.Default.setAS_lowSteerPWM = 30;
            VehicleSettings.Default.setAS_Kp = 120;
            VehicleSettings.Default.setAS_minSteerPWM = 25;

            VehicleSettings.Default.setArdSteer_setting0 = 56;
            VehicleSettings.Default.setArdSteer_setting1 = 0;
            VehicleSettings.Default.setArdMac_isDanfoss = false;

            VehicleSettings.Default.setArdSteer_maxPulseCounts = 0;

            ToolSettings.Default.setVehicle_goalPointLookAheadMult = 1;

            ToolSettings.Default.stanleyHeadingErrorGain = 1;
            ToolSettings.Default.stanleyDistanceErrorGain = 1;
            ToolSettings.Default.stanleyIntegralGainAB = 0;

            ToolSettings.Default.purePursuitIntegralGainAB = 0;

            VehicleSettings.Default.setAS_sideHillComp = 0;

            VehicleSettings.Default.setVehicle_wheelbase = 2.8;

            VehicleSettings.Default.setVehicle_trackWidth = 1.9;

            VehicleSettings.Default.setVehicle_antennaPivot = 0.1;

            VehicleSettings.Default.setVehicle_antennaHeight = 3;

            VehicleSettings.Default.setVehicle_antennaOffset = 0;

            VehicleSettings.Default.setIMU_invertRoll = false;

            VehicleSettings.Default.setIMU_rollZero = ahrs.rollZero;

            toSend252 = true;
            counter252 = 3;
            toSend251 = true;
            counter251 = 2;

            //save current vehicle and tool
            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();

            LoadSettingsToUi();
        }

        private void BtnStartWizard_Click(object sender, RoutedEventArgs e)
        {
            panel1.IsVisible = false;
            isWizardStarted = true;
            tabWiz.SelectedIndex++;
        }

        private void BtnStopWizard_Click(object sender, RoutedEventArgs e)
        {
            isWizardStarted = false;
            FreeDrive(false);
            tabWiz.SelectedIndex = 0;
            panel1.IsVisible = true;
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ===============================================================================================
        // Wizard region — sensor mutual-exclusion, change latches, and tab navigation (source L629-L878).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Port of WinForms <c>cboxCancelGuidance_Click</c> (source L706-L776): the encoder /
        /// pressure-sensor / current-sensor mutual-exclusion and the dependent control visibility. Wired to
        /// all three sensor check boxes; <c>sender</c> identifies which one toggled.
        /// </summary>
        private void CboxCancelGuidance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox)
            {
                if (checkbox.Name == "cboxEncoder" || checkbox.Name == "cboxPressureSensor"
                    || checkbox.Name == "cboxCurrentSensor")
                {
                    if (checkbox.IsChecked != true)
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        cboxEncoder.IsChecked = false;
                        label61.IsVisible = false;
                        lblPercentFS.IsVisible = false;
                        nudMaxCounts.IsVisible = false;
                        pbarSensor.IsVisible = false;
                        hsbarSensor.IsVisible = false;
                        lblhsbarSensor.IsVisible = false;
                        if (isWizardStarted)
                        {
                            toSend251 = true;
                            counter251 = 0;
                        }
                        return;
                    }

                    if (checkbox == cboxPressureSensor)
                    {
                        cboxEncoder.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        label61.IsVisible = true;
                        lblPercentFS.IsVisible = true;
                        nudMaxCounts.IsVisible = false;
                        pbarSensor.IsVisible = true;
                        label61.Text = "Off at %";
                        hsbarSensor.IsVisible = true;
                        lblhsbarSensor.IsVisible = true;
                    }
                    else if (checkbox == cboxCurrentSensor)
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxEncoder.IsChecked = false;
                        label61.IsVisible = true;
                        lblPercentFS.IsVisible = true;
                        nudMaxCounts.IsVisible = false;
                        hsbarSensor.IsVisible = true;
                        pbarSensor.IsVisible = true;
                        label61.Text = "Off at %";
                        lblhsbarSensor.IsVisible = true;
                    }
                    else if (checkbox == cboxEncoder)
                    {
                        cboxPressureSensor.IsChecked = false;
                        cboxCurrentSensor.IsChecked = false;
                        label61.IsVisible = true;
                        lblPercentFS.IsVisible = false;
                        nudMaxCounts.IsVisible = true;
                        pbarSensor.IsVisible = false;
                        hsbarSensor.IsVisible = false;
                        lblhsbarSensor.IsVisible = false;
                        label61.Text = gStr.gsEncoderCounts;
                    }
                }
            }

            if (isWizardStarted)
            {
                toSend251 = true;
                counter251 = 0;
            }
        }

        /// <summary>
        /// [XPLAT] Consolidated PGN-251 dirty latch for the WinForms CheckedChanged handlers
        /// (chkInvertWAS / chkInvertSteer / chkSteerInvertRelays / cboxDanfoss — source L661-L704).
        /// </summary>
        private void Toggle251(object sender, RoutedEventArgs e)
        {
            if (isWizardStarted)
            {
                toSend251 = true;
                counter251 = 0;
            }
        }

        /// <summary>
        /// [XPLAT] Consolidated PGN-251 dirty latch for the WinForms SelectedIndexChanged handlers
        /// (cboxMotorDrive / cboxSteerEnable / cboxConv — source L643-L686).
        /// </summary>
        private void Combo251_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoading) return;
            if (isWizardStarted)
            {
                toSend251 = true;
                counter251 = 0;
            }
        }

        private void BtnOkNextCancelGuidance_Click(object sender, RoutedEventArgs e)
        {
            tabWiz.SelectedIndex = (tabWiz.SelectedIndex + 1 < tabWiz.ItemCount) ?
                tabWiz.SelectedIndex + 1 : tabWiz.SelectedIndex;
        }

        private void BtnOkNext_Click(object sender, RoutedEventArgs e)
        {
            tabWiz.SelectedIndex = (tabWiz.SelectedIndex + 1 < tabWiz.ItemCount) ?
                 tabWiz.SelectedIndex + 1 : tabWiz.SelectedIndex;
            lblCPDError.Text = "...";
            lblAckermannError.Text = "...";
            lblStartAngleLeft.Text = "...";
            lblRightStartAngle.Text = "...";
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if ((tabWiz.SelectedItem as TabItem)?.Name == "tabMaxSteerAngle")
            {
                tabWiz.SelectedIndex = (tabWiz.SelectedIndex - 3 < tabWiz.ItemCount) ?
                                 tabWiz.SelectedIndex - 3 : tabWiz.SelectedIndex;
            }
            else
            {
                tabWiz.SelectedIndex = (tabWiz.SelectedIndex - 1 < tabWiz.ItemCount) ?
                                 tabWiz.SelectedIndex - 1 : tabWiz.SelectedIndex;
            }

            lblCPDError.Text = "...";
            lblAckermannError.Text = "...";
            lblStartAngleLeft.Text = "...";
            lblRightStartAngle.Text = "...";
        }

        private void BtnSkipCPD_Setup_Click(object sender, RoutedEventArgs e)
        {
            tabWiz.SelectedIndex = (tabWiz.SelectedIndex + 3 < tabWiz.ItemCount) ?
                tabWiz.SelectedIndex + 3 : tabWiz.SelectedIndex;
        }

        private void BtnOKNext_CPDSetup_Click(object sender, RoutedEventArgs e)
        {
            TimedMessageBox(3000, "Defaults Set", "CPD and Ackermann set to defaults");
            hsbarCountsPerDegree.Value = 100;
            hsbarAckerman.Value = 100;

            tabWiz.SelectedIndex = (tabWiz.SelectedIndex + 1 < tabWiz.ItemCount) ?
             tabWiz.SelectedIndex + 1 : tabWiz.SelectedIndex;
        }

        private void BtnRemoveWasOffset_Click(object sender, RoutedEventArgs e)
        {
            hsbarWasOffset.Value = 0;
        }

        private void BtnRestartWizard_Click(object sender, RoutedEventArgs e)
        {
            isWizardStarted = false;
            FreeDrive(false);
            tabWiz.SelectedIndex = 0;
        }

        /// <summary>
        /// [XPLAT] FreeDrive toggle (source L862-L876). Both WinForms branches are identical apart from the
        /// flag, so the value is taken straight from <paramref name="isOn"/>.
        /// </summary>
        private void FreeDrive(bool isOn)
        {
            vehicle.isInFreeDriveMode = isOn;
            vehicle.driveFreeSteerAngle = 0;
            lblSteerAngle.Text = "0";
        }

        /// <summary>
        /// [XPLAT] Single SelectionChanged consolidating the WinForms per-TabPage Enter/Leave handlers
        /// (tabMotorDirection L852-L860, tab_MinimumGain L1128-L1139, tabPGain L1141-L1149). WinForms fired
        /// the outgoing tab's Leave before the incoming tab's Enter, so removed items are processed first.
        /// </summary>
        private void TabWiz_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoading) return;

            foreach (var removed in e.RemovedItems)
            {
                if (removed is TabItem leftTab) OnTabLeave(leftTab.Name);
            }
            foreach (var added in e.AddedItems)
            {
                if (added is TabItem enterTab) OnTabEnter(enterTab.Name);
            }
        }

        private void OnTabEnter(string tabName)
        {
            switch (tabName)
            {
                case "tabMotorDirection":
                    FreeDrive(true);
                    break;
                case "tab_MinimumGain":
                    hsbarProportionalGain.Value = 1;
                    FreeDrive(true);
                    TimedMessageBox(2000, "P Gain Change", "Proportional Gain set to 1");
                    break;
                case "tabPGain":
                    FreeDrive(true);
                    break;
            }
        }

        private void OnTabLeave(string tabName)
        {
            switch (tabName)
            {
                case "tabMotorDirection":
                    FreeDrive(false);
                    break;
                case "tab_MinimumGain":
                    FreeDrive(false);
                    hsbarProportionalGain.Value = 40;
                    break;
                case "tabPGain":
                    FreeDrive(false);
                    break;
            }
        }

        // ===============================================================================================
        // Gain region — PWM / proportional-gain sliders (source L880-L925).
        // ===============================================================================================

        private void HsbarMinPWM_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblMinPWM.Text = unchecked((byte)(int)hsbarMinPWM.Value).ToString(CI);
            hsbarLowSteerPWM.Value = hsbarMinPWM.Value + 2;
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        private void HsbarProportionalGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblProportionalGain.Text = unchecked((byte)(int)hsbarProportionalGain.Value).ToString(CI);
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        private void HsbarLowSteerPWM_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            if (hsbarLowSteerPWM.Value > hsbarHighSteerPWM.Value) hsbarHighSteerPWM.Value = hsbarLowSteerPWM.Value;
            lblLowSteerPWM.Text = unchecked((byte)(int)hsbarLowSteerPWM.Value).ToString(CI);
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        private void HsbarHighSteerPWM_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            if (hsbarLowSteerPWM.Value > hsbarHighSteerPWM.Value) hsbarLowSteerPWM.Value = hsbarHighSteerPWM.Value;
            lblHighSteerPWM.Text = unchecked((byte)(int)hsbarHighSteerPWM.Value).ToString(CI);
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        // ===============================================================================================
        // WAS region — ackerman / max-steer-angle / counts-per-degree / sensor-zero (source L927-L965).
        // ===============================================================================================

        private void HsbarAckerman_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblAckerman.Text = unchecked((byte)(int)hsbarAckerman.Value).ToString(CI);

            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        private void HsbarMaxSteerAngle_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.maxSteerAngle = hsbarMaxSteerAngle.Value;
            lblMaxSteerAngle.Text = ((int)hsbarMaxSteerAngle.Value).ToString(CI);
        }

        private void HsbarCountsPerDegree_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblCountsPerDegree.Text = unchecked((byte)(int)hsbarCountsPerDegree.Value).ToString(CI);
            lblSteerAngleSensorZero.Text =
                (hsbarWasOffset.Value / (double)((int)hsbarCountsPerDegree.Value)).ToString("N2", CI);
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        /// <summary>
        /// [XPLAT] Wired (per the WinForms designer) to the WAS-offset slider — recomputes the displayed
        /// sensor-zero ratio. (The hidden hsbarSteerAngleSensorZero slider itself has no handler.)
        /// </summary>
        private void HsbarSteerAngleSensorZero_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblSteerAngleSensorZero.Text =
                (hsbarWasOffset.Value / (double)((int)hsbarCountsPerDegree.Value)).ToString("N2", CI);
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        // ===============================================================================================
        // Stanley region (source L1037-L1055).
        // ===============================================================================================

        private void HsbarStanleyGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.stanleyDistanceErrorGain = hsbarStanleyGain.Value * 0.1;
            lblStanleyGain.Text = vehicle.stanleyDistanceErrorGain.ToString(CI);
        }

        private void HsbarHeadingErrorGain_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.stanleyHeadingErrorGain = hsbarHeadingErrorGain.Value * 0.1;
            lblHeadingErrorGain.Text = vehicle.stanleyHeadingErrorGain.ToString(CI);
        }

        private void HsbarIntegral_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.stanleyIntegralGainAB = hsbarIntegral.Value * 0.01;
            lblIntegralPercent.Text = ((int)hsbarIntegral.Value).ToString(CI);
        }

        // ===============================================================================================
        // Pure-pursuit region (source L1059-L1098).
        // ===============================================================================================

        private void HsbarIntegralPurePursuit_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.purePursuitIntegralGain = hsbarIntegralPurePursuit.Value * 0.01;
            lblPureIntegral.Text = ((int)hsbarIntegralPurePursuit.Value).ToString(CI);
        }

        private void HsbarSideHillComp_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            double deg = hsbarSideHillComp.Value;
            deg *= 0.01;
            lblSideHillComp.Text = (deg.ToString("N2", CI) + "\u00B0");
            VehicleSettings.Default.setAS_sideHillComp = deg;
            gyd.sideHillCompFactor = deg;
        }

        private void HsbarLookAheadMult_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            vehicle.goalPointLookAheadMult = hsbarLookAheadMult.Value * 0.1;
            lblLookAheadMult.Text = vehicle.goalPointLookAheadMult.ToString(CI);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>hsbarSensor_Scroll</c> (source L785-L793). The WinForms .Scroll event maps to
        /// Avalonia's RangeBase.ValueChanged; the percent-of-full-scale readout and the PGN-251 latch are
        /// preserved.
        /// </summary>
        private void HsbarSensor_Scroll(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoading) return;
            lblhsbarSensor.Text = ((int)((double)hsbarSensor.Value * 0.3921568627)).ToString(CI) + "%";
            if (isWizardStarted)
            {
                toSend251 = true;
                counter251 = 0;
            }
        }

        // ===============================================================================================
        // WAS zero & steer-angle calibration buttons (source L967-L1033, L1083-L1092).
        // ===============================================================================================

        /// <summary>
        /// [XPLAT] Port of WinForms <c>btnZeroWAS_Click</c> (source L967-L983). WinForms <c>FormDialog.Show</c>
        /// (modal) becomes <c>await FormDialogView.ShowAsync</c>, so this is an async-void event handler; the
        /// PGN-252 latch still runs after the dialog (matching WinForms' post-dialog ordering).
        /// </summary>
        private async void BtnZeroWAS_Click(object sender, RoutedEventArgs e)
        {
            int offset = (int)(hsbarCountsPerDegree.Value * -telemetry.actualSteerAngleDegrees + hsbarWasOffset.Value);
            if (Math.Abs(offset) > 3900)
            {
                await FormDialogView.ShowAsync("Exceeded Range", "Excessive Steer Angle - Cannot Zero", DialogSeverity.Error, owner);
            }
            else
            {
                hsbarWasOffset.Value += (int)(hsbarCountsPerDegree.Value * -telemetry.actualSteerAngleDegrees);
            }
            if (isWizardStarted)
            {
                toSend252 = true;
                counter252 = 0;
            }
        }

        private void BtnStartSA_Click(object sender, RoutedEventArgs e)
        {
            if (!isSARight)
            {
                isSARight = true;
                startFix = telemetry.pivotAxlePos;
                dist = 0;
                diameter = 0;
                cntr = 0;
                SetButtonImage(btnStartSA, "boundaryStop.png");
                lblDiameter.Text = "0";
                lblCalcSteerAngleInner.Text = "Drive Steady";
                lblRightStartAngle.Text = telemetry.actualSteerAngleDegrees.ToString("N1", CI);
            }
            else
            {
                isSARight = false;
                lblCalcSteerAngleInner.Text = "0.0" + "\u00B0";
                SetButtonImage(btnStartSA, "BoundaryRecord.png");
                lblRightStartAngle.Text = "..." + "\u00B0";
            }

            lblCPDError.Text = "...";
        }

        private void BtnStartSA_Left_Click(object sender, RoutedEventArgs e)
        {
            if (!isSALeft)
            {
                isSALeft = true;
                startFix = telemetry.pivotAxlePos;
                startAngleLeft = telemetry.actualSteerAngleDegrees;
                dist = 0;
                diameter = 0;
                cntr = 0;
                SetButtonImage(btnStartSA_Left, "boundaryStop.png");
                lblDiameterLeft.Text = "0";
                lblCalcSteerAngleLeft.Text = "Drive Steady";
                lblStartAngleLeft.Text = startAngleLeft.ToString("N1", CI) + "\u00B0";
            }
            else
            {
                isSALeft = false;
                lblCalcSteerAngleLeft.Text = "..." + "\u00B0";
                SetButtonImage(btnStartSA_Left, "BoundaryRecord.png");
            }

            lblAckermannError.Text = "...";
        }

        private async void BtnOkSetMaximumSteerAngle_Click(object sender, RoutedEventArgs e)
        {
            if (Math.Abs((int)telemetry.actualSteerAngleDegrees) < 5)
            {
                await FormDialogView.ShowAsync("Steer Angle Too Low", "Must be Greater than 5 degrees", DialogSeverity.Error, owner);
                return;
            }

            hsbarMaxSteerAngle.Value = Math.Abs((int)(telemetry.actualSteerAngleDegrees));
        }

        private void BtnAckReset_Click(object sender, RoutedEventArgs e)
        {
            hsbarAckerman.Value = 100;
        }

        // ===============================================================================================
        // MinMovement & P-Gain free-steer nudge buttons (source L1104-L1168).
        // ===============================================================================================

        private async void BtnMinGainLeft_Click(object sender, RoutedEventArgs e)
        {
            if (CheckSteerSwitch())
                vehicle.driveFreeSteerAngle -= 2;
            else
                await FormDialogView.ShowAsync("Steering Disabled", "Enable Steer Switch", DialogSeverity.Error, owner);
        }

        private async void BtnMinGainRight_Click(object sender, RoutedEventArgs e)
        {
            if (CheckSteerSwitch())
                vehicle.driveFreeSteerAngle += 2;
            else
                await FormDialogView.ShowAsync("Steering Disabled", "Enable Steer Switch", DialogSeverity.Error, owner);
        }

        private async void BtnZeroMinMovementSetting_Click(object sender, RoutedEventArgs e)
        {
            if (CheckSteerSwitch())
                vehicle.driveFreeSteerAngle = 0;
            else
                await FormDialogView.ShowAsync("Steering Disabled", "Enable Steer Switch", DialogSeverity.Error, owner);
        }

        private void BtnZeroPGain_Click(object sender, RoutedEventArgs e)
        {
            if (vehicle.driveFreeSteerAngle == 0)
                vehicle.driveFreeSteerAngle = 5;
            else vehicle.driveFreeSteerAngle = 0;
        }

        private void BtnLeftPGain_Click(object sender, RoutedEventArgs e)
        {
            vehicle.driveFreeSteerAngle--;
            if (vehicle.driveFreeSteerAngle < -40) vehicle.driveFreeSteerAngle = -40;
        }

        private void BtnRightPGain_Click(object sender, RoutedEventArgs e)
        {
            vehicle.driveFreeSteerAngle++;
            if (vehicle.driveFreeSteerAngle > 40) vehicle.driveFreeSteerAngle = 40;
        }

        // ===============================================================================================
        // Numeric-entry (nud) keypad buttons (source L1170-L1253, L631-L641).
        // Each opens a FormNumeric keypad; on accept, the displayed value, the persisted setting, and the
        // Core VehicleConfig property are updated (units converted via inchOrCm2m), then saved.
        // ===============================================================================================

        private async void NudWheelbase_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 999, nudWheelbaseValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudWheelbaseValue = f.ReturnValue;
                nudWheelbase.Content = NudText(nudWheelbaseValue);
                VehicleSettings.Default.setVehicle_wheelbase = nudWheelbaseValue * telemetry.inchOrCm2m;
                vehicle.VehicleConfig.Wheelbase = VehicleSettings.Default.setVehicle_wheelbase;
                VehicleSettings.Default.Save();
            }
        }

        private async void NudVehicleTrack_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 999, nudVehicleTrackValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudVehicleTrackValue = f.ReturnValue;
                nudVehicleTrack.Content = NudText(nudVehicleTrackValue);
                VehicleSettings.Default.setVehicle_trackWidth = nudVehicleTrackValue * telemetry.inchOrCm2m;
                vehicle.VehicleConfig.TrackWidth = VehicleSettings.Default.setVehicle_trackWidth;
                vehicle.HalfWheelTrack = vehicle.VehicleConfig.TrackWidth * 0.5; // mf.tram.halfWheelTrack
                VehicleSettings.Default.Save();
            }
        }

        private async void NudAntennaPivot_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(-999, 999, nudAntennaPivotValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudAntennaPivotValue = f.ReturnValue;
                nudAntennaPivot.Content = NudText(nudAntennaPivotValue);
                VehicleSettings.Default.setVehicle_antennaPivot = nudAntennaPivotValue * telemetry.inchOrCm2m;
                vehicle.VehicleConfig.AntennaPivot = VehicleSettings.Default.setVehicle_antennaPivot;
                VehicleSettings.Default.Save();
            }
        }

        private async void NudAntennaHeight_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 999, nudAntennaHeightValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudAntennaHeightValue = f.ReturnValue;
                nudAntennaHeight.Content = NudText(nudAntennaHeightValue);
                VehicleSettings.Default.setVehicle_antennaHeight = nudAntennaHeightValue * telemetry.inchOrCm2m;
                vehicle.VehicleConfig.AntennaHeight = VehicleSettings.Default.setVehicle_antennaHeight;
                VehicleSettings.Default.Save();
            }
        }

        private async void NudAntennaOffset_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(-999, 999, nudAntennaOffsetValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudAntennaOffsetValue = f.ReturnValue;
                nudAntennaOffset.Content = NudText(nudAntennaOffsetValue);
                VehicleSettings.Default.setVehicle_antennaOffset = nudAntennaOffsetValue * telemetry.inchOrCm2m;
                vehicle.VehicleConfig.AntennaOffset = VehicleSettings.Default.setVehicle_antennaOffset;
                VehicleSettings.Default.Save();
            }
        }

        /// <summary>
        /// [XPLAT] Port of WinForms <c>nudMaxCounts_Click</c> (source L631-L641): the keypad updates the
        /// displayed max-pulse count; only the PGN-251 latch is touched (no VehicleConfig write here).
        /// </summary>
        private async void NudMaxCounts_Click(object sender, RoutedEventArgs e)
        {
            var f = new FormNumeric(0, 255, nudMaxCountsValue);
            if (await f.ShowDialog<bool>(this))
            {
                nudMaxCountsValue = (int)f.ReturnValue;
                nudMaxCounts.Content = nudMaxCountsValue.ToString(CI);
                if (isWizardStarted)
                {
                    toSend251 = true;
                    counter251 = 0;
                }
            }
        }

        // ===============================================================================================
        // Roll invert / zero (source L1206-L1233).
        // ===============================================================================================

        private void CboxDataInvertRoll_Click(object sender, RoutedEventArgs e)
        {
            VehicleSettings.Default.setIMU_invertRoll = cboxDataInvertRoll.IsChecked == true;
            ahrs.isRollInvert = VehicleSettings.Default.setIMU_invertRoll;
        }

        private void BtnZeroRoll_Click(object sender, RoutedEventArgs e)
        {
            if (ahrs.imuRoll != 88888)
            {
                ahrs.imuRoll += ahrs.rollZero;
                ahrs.rollZero = ahrs.imuRoll;
                lblRollZeroOffset.Text = (ahrs.rollZero).ToString("N2", CI);
            }
            else
            {
                lblRollZeroOffset.Text = "***";
            }

            VehicleSettings.Default.setIMU_rollZero = ahrs.rollZero;
        }

        private void BtnRemoveZeroOffset_Click(object sender, RoutedEventArgs e)
        {
            ahrs.rollZero = 0;
            lblRollZeroOffset.Text = "0.00";
            VehicleSettings.Default.setIMU_rollZero = ahrs.rollZero;
        }

        // ===============================================================================================
        // Free Drive region (source L1264-L1321).
        // ===============================================================================================

        private void BtnFreeDrive_Click(object sender, RoutedEventArgs e)
        {
            if (vehicle.isInFreeDriveMode)
            {
                //turn OFF free drive mode
                SetButtonImage(btnFreeDrive, "SteerDriveOff.png");
                btnFreeDrive.Background = new SolidColorBrush(Color.FromArgb(255, 50, 50, 70));
                vehicle.isInFreeDriveMode = false;
                btnSteerAngleDown.IsEnabled = false;
                btnSteerAngleUp.IsEnabled = false;
                btnFreeDriveZero.IsEnabled = false;
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
                btnFreeDriveZero.IsEnabled = true;
                vehicle.driveFreeSteerAngle = 0;
                lblSteerAngle.Text = "0";
            }
        }

        private void BtnSteerLeft_Click(object sender, RoutedEventArgs e)
        {
            vehicle.driveFreeSteerAngle--;
            if (vehicle.driveFreeSteerAngle < -40) vehicle.driveFreeSteerAngle = -40;
        }

        private void BtnSteerRight_Click(object sender, RoutedEventArgs e)
        {
            vehicle.driveFreeSteerAngle++;
            if (vehicle.driveFreeSteerAngle > 40) vehicle.driveFreeSteerAngle = 40;
        }

        private void BtnFreeDriveZero_Click(object sender, RoutedEventArgs e)
        {
            if (vehicle.driveFreeSteerAngle == 0)
                vehicle.driveFreeSteerAngle = 5;
            else vehicle.driveFreeSteerAngle = 0;
        }

        private void BtnSteerAngleUp_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            vehicle.driveFreeSteerAngle++;
            if (vehicle.driveFreeSteerAngle > 40) vehicle.driveFreeSteerAngle = 40;
        }

        private void BtnSteerAngleDown_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            vehicle.driveFreeSteerAngle--;
            if (vehicle.driveFreeSteerAngle < -40) vehicle.driveFreeSteerAngle = -40;
        }
    }
}
