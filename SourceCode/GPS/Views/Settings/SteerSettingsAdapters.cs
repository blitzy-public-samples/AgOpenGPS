// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Real (non-Null) collaborator adapters for FormSteerView (the AutoSteer Configuration dialog). The
// WinForms FormSteer reached the running domain objects through the FormGPS "mf" back-reference; Avalonia
// removes it, so the dialog depends only on the wizard-local ISteerSettings* interfaces declared in
// FormSteerView.axaml.cs. These adapters wire those interfaces onto the real composition-root domain
// objects, replacing the inert Null* defaults so the dialog is genuinely functional (live gains/telemetry
// + config writes that emit the real 0xFC/0xFB steer-config PGN frames).
//
// Behaviour is frozen: every member delegates verbatim to the same domain field the WinForms dialog wrote.
// Intentional, documented substitutions:
//  - actAngVel / setAngVel return 0: the angular-velocity computation is dormant (commented out) in the
//    migrated PositionService (UpdateFixPosition, ~L1156-1164), so 0 matches the current migrated build.
//  - cm2CmOrIn / inOrCm2Cm are computed from the metric flag (the migrated RenderCoordinator exposes only
//    M2InchOrCm/UnitsInCmNS); the metric<->imperial cm conversion factors are exact (1 vs 1/2.54, 1 vs 2.54).
//  - ResetVehicle() reloads the LIVE CVehicle in-place via CVehicle.LoadSettings() (after the dialog re-saves
//    default settings) so a defaults-reset propagates app-wide, restoring the WinForms behaviour where reset
//    replaced the whole CVehicle object.

using System;
using System.Globalization;
using AgOpenGPS.Core;            // ApplicationModel
using AgOpenGPS.Core.Models;     // VehicleConfig
using AgOpenGPS.Properties;      // VehicleSettings, ToolSettings
using AgOpenGPS.Services;        // RenderCoordinator, PgnDispatcher, PositionService, CPGN_FC, CPGN_FB
// [XPLAT] Alias the Properties.Settings store: inside namespace AgOpenGPS.Views.Settings the bare token
// 'Settings' binds to the enclosing namespace segment, not the settings class, so qualify it explicitly.
using AppSettings = AgOpenGPS.Properties.Settings;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>[XPLAT] Mutable vehicle guidance parameters — was <c>mf.vehicle</c>. Direct field passthrough.</summary>
    internal sealed class SteerSettingsVehicleAdapter : ISteerSettingsVehicle
    {
        private readonly CVehicle _v;
        public SteerSettingsVehicleAdapter(CVehicle vehicle) => _v = vehicle ?? throw new ArgumentNullException(nameof(vehicle));

        public double maxSteerAngle { get => _v.maxSteerAngle; set => _v.maxSteerAngle = value; }
        public double maxSteerSpeed { get => _v.maxSteerSpeed; set => _v.maxSteerSpeed = value; }
        public double minSteerSpeed { get => _v.minSteerSpeed; set => _v.minSteerSpeed = value; }
        public double functionSpeedLimit { get => _v.functionSpeedLimit; set => _v.functionSpeedLimit = value; }
        public double driveFreeSteerAngle { get => _v.driveFreeSteerAngle; set => _v.driveFreeSteerAngle = value; }
        public double stanleyDistanceErrorGain { get => _v.stanleyDistanceErrorGain; set => _v.stanleyDistanceErrorGain = value; }
        public double stanleyHeadingErrorGain { get => _v.stanleyHeadingErrorGain; set => _v.stanleyHeadingErrorGain = value; }
        public double stanleyIntegralGainAB { get => _v.stanleyIntegralGainAB; set => _v.stanleyIntegralGainAB = value; }
        public double purePursuitIntegralGain { get => _v.purePursuitIntegralGain; set => _v.purePursuitIntegralGain = value; }
        public double goalPointLookAheadHold { get => _v.goalPointLookAheadHold; set => _v.goalPointLookAheadHold = value; }
        public double goalPointLookAheadMult { get => _v.goalPointLookAheadMult; set => _v.goalPointLookAheadMult = value; }
        public double goalPointAcquireFactor { get => _v.goalPointAcquireFactor; set => _v.goalPointAcquireFactor = value; }
        public double uturnCompensation { get => _v.uturnCompensation; set => _v.uturnCompensation = value; }
        public double modeXTE { get => _v.modeXTE; set => _v.modeXTE = value; }
        public int modeTime { get => _v.modeTime; set => _v.modeTime = value; }
        public int deadZoneHeading { get => _v.deadZoneHeading; set => _v.deadZoneHeading = value; }
        public int deadZoneDelay { get => _v.deadZoneDelay; set => _v.deadZoneDelay = value; }
        public bool isInFreeDriveMode { get => _v.isInFreeDriveMode; set => _v.isInFreeDriveMode = value; }
        public double goalDistance => _v.goalDistance;
        public VehicleConfig VehicleConfig => _v.VehicleConfig;
    }

    /// <summary>[XPLAT] Steer-config PGN 0xFC (252) — was <c>mf.p_252</c>.</summary>
    internal sealed class SteerSettingsPgn252Adapter : ISteerSettingsPgn252
    {
        private readonly CPGN_FC _fc;
        public SteerSettingsPgn252Adapter(CPGN_FC fc) => _fc = fc ?? throw new ArgumentNullException(nameof(fc));

        public byte[] Pgn => _fc.pgn;
        public int GainProportional => _fc.gainProportional;
        public int HighPWM => _fc.highPWM;
        public int LowPWM => _fc.lowPWM;
        public int MinPWM => _fc.minPWM;
        public int CountsPerDegree => _fc.countsPerDegree;
        public int WasOffsetLo => _fc.wasOffsetLo;
        public int WasOffsetHi => _fc.wasOffsetHi;
        public int Ackerman => _fc.ackerman;
    }

    /// <summary>[XPLAT] Steer-config PGN 0xFB (251) — was <c>mf.p_251</c>. Includes AngVel (index 9), which
    /// FormSteer writes (the wizard did not).</summary>
    internal sealed class SteerSettingsPgn251Adapter : ISteerSettingsPgn251
    {
        private readonly CPGN_FB _fb;
        public SteerSettingsPgn251Adapter(CPGN_FB fb) => _fb = fb ?? throw new ArgumentNullException(nameof(fb));

        public byte[] Pgn => _fb.pgn;
        public int Set0 => _fb.set0;
        public int MaxPulse => _fb.maxPulse;
        public int MinSpeed => _fb.minSpeed;
        public int Set1 => _fb.set1;
        public int AngVel => _fb.angVel;
    }

    /// <summary>[XPLAT] Steer-configuration transport + the misc guidance/AB-line state the dialog pushes to —
    /// was <c>mf.p_252</c>/<c>mf.p_251</c>/<c>mf.SendPgnToLoop</c> + <c>mf.gyd.sideHillCompFactor</c>,
    /// <c>mf.ABLine.lineWidth</c>/<c>snapDistance</c>, and the reset-time <c>mf.vehicle = new CVehicle(mf)</c>.</summary>
    internal sealed class SteerSettingsConfigServiceAdapter : ISteerSettingsConfigService
    {
        private readonly PgnDispatcher _pgn;
        private readonly CABLine _abLine;
        private readonly CVehicle _vehicle;

        public SteerSettingsConfigServiceAdapter(PgnDispatcher pgn, CABLine abLine, CVehicle vehicle)
        {
            _pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
            _abLine = abLine ?? throw new ArgumentNullException(nameof(abLine));
            _vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            P252 = new SteerSettingsPgn252Adapter(_pgn.p_252);
            P251 = new SteerSettingsPgn251Adapter(_pgn.p_251);
        }

        public ISteerSettingsPgn252 P252 { get; }
        public ISteerSettingsPgn251 P251 { get; }

        public void SendPgnToLoop(byte[] pgn) => _pgn.SendPgnToLoop(pgn);

        public double SideHillCompFactor
        {
            get => VehicleSettings.Default.setAS_sideHillComp;
            set => VehicleSettings.Default.setAS_sideHillComp = value;
        }

        public int LineWidth { get => _abLine.lineWidth; set => _abLine.lineWidth = value; }
        public double SnapDistance { get => _abLine.snapDistance; set => _abLine.snapDistance = value; }

        // [XPLAT] was mf.vehicle = new CVehicle(mf): reload the LIVE vehicle from the just-saved default
        // settings (in-place, so all app-wide holders observe the reset) and return a fresh adapter over it.
        public ISteerSettingsVehicle ResetVehicle()
        {
            _vehicle.LoadSettings();
            return new SteerSettingsVehicleAdapter(_vehicle);
        }
    }

    /// <summary>[XPLAT] Live steer/telemetry read-outs + scalar program flags — was <c>mf.mc.*</c> and assorted
    /// <c>mf</c> members. Getters are receive-pipeline snapshots; flag setters write the same shared state.</summary>
    internal sealed class SteerSettingsTelemetryAdapter : ISteerSettingsTelemetry
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private readonly CModuleComm _mc;
        private readonly ApplicationModel _appModel;
        private readonly RenderCoordinator _render;
        private readonly PositionService _position;

        public SteerSettingsTelemetryAdapter(CModuleComm mc, ApplicationModel appModel, RenderCoordinator render, PositionService position)
        {
            _mc = mc ?? throw new ArgumentNullException(nameof(mc));
            _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _position = position ?? throw new ArgumentNullException(nameof(position));
        }

        public double actualSteerAngleDegrees => _mc.actualSteerAngleDegrees;
        public int pwmDisplay => _mc.pwmDisplay;
        public int sensorData { get => _mc.sensorData; set => _mc.sensorData = value; }

        // [XPLAT] was FormGPS.SetSteerAngle (GUI.Designer.cs L1544).
        public string SetSteerAngle => ((double)_appModel.guidanceLineSteerAngle * 0.01).ToString("N1", CI);
        public double guidanceLineSteerAngle => _appModel.guidanceLineSteerAngle;

        // [XPLAT] Angular-velocity read-outs are dormant in the migrated PositionService (computation commented
        // out, ~L1156-1164); 0 matches the current migrated build's display.
        public double actAngVel => 0;
        public double setAngVel => 0;

        public vec3 pivotAxlePos => _render.PivotAxlePos;

        // [XPLAT] cm <-> (cm or inch) conversion factors (was mf.cm2CmOrIn / mf.inOrCm2Cm). Metric: identity;
        // imperial: 1 inch = 2.54 cm. Derived from the live metric flag.
        public double cm2CmOrIn => _render.IsMetric ? 1.0 : 1.0 / 2.54;
        public double inOrCm2Cm => _render.IsMetric ? 1.0 : 2.54;

        // [XPLAT] lightbar pixel scale persists in Settings (int store); exposed as double per the interface.
        public double lightbarCmPerPixel
        {
            get => AppSettings.Default.setDisplay_lightbarCmPerPixel;
            set => AppSettings.Default.setDisplay_lightbarCmPerPixel = (int)value;
        }

        public double guidanceLookAheadTime { get => _position.guidanceLookAheadTime; set => _position.guidanceLookAheadTime = value; }
        public string unitsInCm => _render.UnitsInCmNS;

        public bool isStanleyUsed { get => ToolSettings.Default.setVehicle_isStanleyUsed; set => ToolSettings.Default.setVehicle_isStanleyUsed = value; }
        public bool isSteerInReverse { get => VehicleSettings.Default.setAS_isSteerInReverse; set => VehicleSettings.Default.setAS_isSteerInReverse = value; }
        public bool isLightbarOn { get => AppSettings.Default.setMenu_isLightbarOn; set => AppSettings.Default.setMenu_isLightbarOn = value; }
        public bool isLightBarNotSteerBar { get => AppSettings.Default.setMenu_isLightbarNotSteerBar; set => AppSettings.Default.setMenu_isLightbarNotSteerBar = value; }
        public bool isMetric => _render.IsMetric;

        // [XPLAT] Easy-drive mode is feature-gated off in this migration (parity with App.axaml.cs composition).
        public bool isEasyDriveMode => false;
    }

    /// <summary>[XPLAT] Smart-WAS auto-calibration collector the WAS-zero panel drives — was <c>mf.smartWAS</c>.</summary>
    internal sealed class SteerSettingsSmartWASAdapter : ISteerSettingsSmartWAS
    {
        private readonly CSmartWAS _smartWAS;
        public SteerSettingsSmartWASAdapter(CSmartWAS smartWAS) => _smartWAS = smartWAS ?? throw new ArgumentNullException(nameof(smartWAS));

        public void Reset() => _smartWAS.Reset();
        public void Start() => _smartWAS.Start();
        public void Stop() => _smartWAS.Stop();
        public void ApplyOffsetCorrection(double recommendedOffset) => _smartWAS.ApplyOffsetCorrection(recommendedOffset);
        public bool HasValidCalibration => _smartWAS.HasValidCalibration;
        public bool IsCollecting => _smartWAS.IsCollecting;
        public double RecommendedOffset => _smartWAS.RecommendedOffset;
        public double Confidence => _smartWAS.Confidence;
        public int SampleCount => _smartWAS.SampleCount;
    }
}
