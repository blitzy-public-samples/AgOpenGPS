// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Real (non-Null) collaborator adapters for FormSteerWizView. The WinForms steer wizard reached the
// running domain objects through the FormGPS "mf" back-reference (mf.vehicle, mf.p_252/p_251,
// mf.SendPgnToLoop, mf.mc, mf.ahrs, mf.gyd, mf.AppModel). Avalonia removes that back-reference, so the
// wizard view depends only on the small wizard-local interfaces declared in FormSteerWizView.axaml.cs
// (ISteerWizVehicle / ISteerWizConfigService / ISteerWizTelemetry / ISteerWizAhrs / ISteerWizGuidance).
//
// These adapters are the production wiring of those interfaces onto the real composition-root domain
// objects, replacing the inert Null* defaults so the calibration workflow is genuinely functional
// (live telemetry read-outs + config writes that emit the real 0xFC/0xFB steer-config PGN frames).
//
// Behaviour is frozen: every member delegates verbatim to the same domain field/property the WinForms
// wizard mutated. The only intentional substitution is ISteerWizGuidance.sideHillCompFactor, which is
// backed by the persistent VehicleSettings.setAS_sideHillComp store (the same store FormSteerView writes
// and CGuidance loads at construction) because the migrated composition root holds no live CGuidance
// instance — see MIGRATION_DOCS/PARITY_REPORT.md (open risk: guidance cyclic-peer wiring).

using System;
using System.Globalization;
using AgOpenGPS.Core;            // ApplicationModel
using AgOpenGPS.Core.Models;     // VehicleConfig
using AgOpenGPS.Properties;      // VehicleSettings
using AgOpenGPS.Services;        // RenderCoordinator, PgnDispatcher, CPGN_FC, CPGN_FB

namespace AgOpenGPS.Views.Settings
{
    /// <summary>[XPLAT] Wraps the live Core <see cref="CPGN_FC"/> (PGN 0xFC / 252) — was <c>mf.p_252</c>.
    /// Exposes the mutable frame bytes plus the named byte-index positions the wizard writes into.</summary>
    internal sealed class SteerWizPgn252Adapter : ISteerWizPgn252
    {
        private readonly CPGN_FC _fc;
        public SteerWizPgn252Adapter(CPGN_FC fc) => _fc = fc ?? throw new ArgumentNullException(nameof(fc));

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

    /// <summary>[XPLAT] Wraps the live Core <see cref="CPGN_FB"/> (PGN 0xFB / 251) — was <c>mf.p_251</c>.</summary>
    internal sealed class SteerWizPgn251Adapter : ISteerWizPgn251
    {
        private readonly CPGN_FB _fb;
        public SteerWizPgn251Adapter(CPGN_FB fb) => _fb = fb ?? throw new ArgumentNullException(nameof(fb));

        public byte[] Pgn => _fb.pgn;
        public int Set0 => _fb.set0;
        public int Set1 => _fb.set1;
        public int MaxPulse => _fb.maxPulse;
        public int MinSpeed => _fb.minSpeed;
    }

    /// <summary>[XPLAT] Steer-config transport over the real <see cref="PgnDispatcher"/>: the two config
    /// frames plus the loopback send — was <c>mf.p_252</c>/<c>mf.p_251</c>/<c>mf.SendPgnToLoop</c>.</summary>
    internal sealed class SteerWizConfigServiceAdapter : ISteerWizConfigService
    {
        private readonly PgnDispatcher _pgn;
        public SteerWizConfigServiceAdapter(PgnDispatcher pgn)
        {
            _pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
            P252 = new SteerWizPgn252Adapter(_pgn.p_252);
            P251 = new SteerWizPgn251Adapter(_pgn.p_251);
        }

        public ISteerWizPgn252 P252 { get; }
        public ISteerWizPgn251 P251 { get; }

        // [XPLAT] was mf.SendPgnToLoop(byteData) — emits the additive-CRC frame on the loopback (parity contract).
        public void SendPgnToLoop(byte[] pgn) => _pgn.SendPgnToLoop(pgn);
    }

    /// <summary>[XPLAT] Mutable vehicle guidance parameters — was <c>mf.vehicle</c>. <see cref="HalfWheelTrack"/>
    /// folds in the former <c>mf.tram.halfWheelTrack</c> assignment from the vehicle-track keypad handler.</summary>
    internal sealed class SteerWizVehicleAdapter : ISteerWizVehicle
    {
        private readonly CVehicle _vehicle;
        private readonly CTram _tram;
        public SteerWizVehicleAdapter(CVehicle vehicle, CTram tram)
        {
            _vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            _tram = tram ?? throw new ArgumentNullException(nameof(tram));
        }

        public double maxSteerAngle { get => _vehicle.maxSteerAngle; set => _vehicle.maxSteerAngle = value; }
        public bool isInFreeDriveMode { get => _vehicle.isInFreeDriveMode; set => _vehicle.isInFreeDriveMode = value; }
        public double driveFreeSteerAngle { get => _vehicle.driveFreeSteerAngle; set => _vehicle.driveFreeSteerAngle = value; }
        public double stanleyDistanceErrorGain { get => _vehicle.stanleyDistanceErrorGain; set => _vehicle.stanleyDistanceErrorGain = value; }
        public double stanleyHeadingErrorGain { get => _vehicle.stanleyHeadingErrorGain; set => _vehicle.stanleyHeadingErrorGain = value; }
        public double stanleyIntegralGainAB { get => _vehicle.stanleyIntegralGainAB; set => _vehicle.stanleyIntegralGainAB = value; }
        public double purePursuitIntegralGain { get => _vehicle.purePursuitIntegralGain; set => _vehicle.purePursuitIntegralGain = value; }
        public double goalPointLookAheadMult { get => _vehicle.goalPointLookAheadMult; set => _vehicle.goalPointLookAheadMult = value; }
        public VehicleConfig VehicleConfig => _vehicle.VehicleConfig;
        public double HalfWheelTrack { get => _tram.halfWheelTrack; set => _tram.halfWheelTrack = value; }
    }

    /// <summary>[XPLAT] Live steer/telemetry read-outs — was <c>mf.mc.*</c> + assorted <c>mf</c> members.
    /// Read-only snapshots driven by the receive pipeline; string members reproduce the original FormGPS
    /// property formatting (GUI.Designer.cs SetSteerAngle/Heading/RollInDegrees).</summary>
    internal sealed class SteerWizTelemetryAdapter : ISteerWizTelemetry
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private readonly CModuleComm _mc;
        private readonly ApplicationModel _appModel;
        private readonly RenderCoordinator _render;
        private readonly PgnDispatcher _pgn;
        private readonly CAHRS _ahrs;

        public SteerWizTelemetryAdapter(CModuleComm mc, ApplicationModel appModel, RenderCoordinator render, PgnDispatcher pgn, CAHRS ahrs)
        {
            _mc = mc ?? throw new ArgumentNullException(nameof(mc));
            _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
            _ahrs = ahrs ?? throw new ArgumentNullException(nameof(ahrs));
        }

        public double actualSteerAngleDegrees => _mc.actualSteerAngleDegrees;
        public int pwmDisplay => _mc.pwmDisplay;
        public int sensorData => _mc.sensorData;
        public bool steerSwitchHigh => _mc.steerSwitchHigh;
        public bool isBtnAutoSteerOn => _appModel.isBtnAutoSteerOn;
        public int steerModuleConnectedCounter => _pgn.steerModuleConnectedCounter;
        public vec3 pivotAxlePos => _render.PivotAxlePos;

        // [XPLAT] was FormGPS.SetSteerAngle (GUI.Designer.cs L1544): ((double)guidanceLineSteerAngle * 0.01).ToString("N1").
        public string SetSteerAngle => ((double)_appModel.guidanceLineSteerAngle * 0.01).ToString("N1", CI);

        // [XPLAT] was FormGPS.Heading (GUI.Designer.cs L1508): AppModel.FixHeading.HeadingString().
        public string Heading => _appModel.FixHeading.HeadingString();

        // [XPLAT] was FormGPS.RollInDegrees (GUI.Designer.cs L1535): "-" sentinel at 88888, else rounded roll + degree sign.
        public string RollInDegrees => _ahrs.imuRoll != 88888
            ? Math.Round(_ahrs.imuRoll, 1).ToString(CI) + "\u00B0"
            : "-";

        public double guidanceLineSteerAngle => _appModel.guidanceLineSteerAngle;
        public double m2InchOrCm => _render.M2InchOrCm;

        // [XPLAT] inchOrCm2m is the exact inverse of m2InchOrCm (metric 0.01<->100, imperial in2m<->m2in;
        // GUI.Designer.cs L474-490). Computed from the live unit factor; guarded against divide-by-zero.
        public double inchOrCm2m => _render.M2InchOrCm != 0 ? 1.0 / _render.M2InchOrCm : 0.01;
    }

    /// <summary>[XPLAT] AHRS roll state the roll-zero / invert-roll tabs read and write — was <c>mf.ahrs</c>.</summary>
    internal sealed class SteerWizAhrsAdapter : ISteerWizAhrs
    {
        private readonly CAHRS _ahrs;
        public SteerWizAhrsAdapter(CAHRS ahrs) => _ahrs = ahrs ?? throw new ArgumentNullException(nameof(ahrs));

        public double imuRoll { get => _ahrs.imuRoll; set => _ahrs.imuRoll = value; }
        public double rollZero { get => _ahrs.rollZero; set => _ahrs.rollZero = value; }
        public bool isRollInvert { get => _ahrs.isRollInvert; set => _ahrs.isRollInvert = value; }
    }

    /// <summary>[XPLAT] Side-hill compensation factor — was <c>mf.gyd.sideHillCompFactor</c>. Backed by the
    /// persistent <see cref="VehicleSettings.setAS_sideHillComp"/> store (the same store FormSteerView writes
    /// via steerCfg.SideHillCompFactor and CGuidance loads at construction). The migrated composition root
    /// holds no live CGuidance instance, so this persistent backing keeps the wizard read/write faithful and
    /// NRE-safe; the value is consumed by guidance once a CGuidance is constructed/reloaded.</summary>
    internal sealed class SteerWizGuidanceAdapter : ISteerWizGuidance
    {
        public double sideHillCompFactor
        {
            get => VehicleSettings.Default.setAS_sideHillComp;
            set => VehicleSettings.Default.setAS_sideHillComp = value;
        }
    }
}
