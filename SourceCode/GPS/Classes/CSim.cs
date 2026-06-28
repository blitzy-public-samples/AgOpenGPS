// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using System;

namespace AgOpenGPS
{
    public class CSim
    {
        // [XPLAT] Decoupled from the WinForms FormGPS host god-object (the former
        // `private readonly FormGPS mf;`). The collaborators this simulator drives are now
        // constructor-injected and used live-by-reference, exactly mirroring the established
        // CNMEA(ApplicationModel, ...) / CModuleComm(ApplicationModel, ...) / CSmartWAS(ApplicationModel)
        // decoupling, so the simulator runs unchanged on Windows, Linux and macOS:
        //   _appModel - shared AgOpenGPS.Core runtime model. Supplies the WGS84->local-plane projection
        //               (LocalPlane), the canonical current lat/lon (CurrentLatLon) and the NMEA-sentence
        //               watchdog counter (sentenceCounter) that previously lived on FormGPS. Same state,
        //               same scaling — the generated fix sequence is preserved byte-for-byte.
        //   _ahrs     - injected AHRS sensor state (was mf.ahrs); the simulated IMU heading is written here.
        //   _mc       - injected module-comm state (was mf.mc); the simulated actual steer angle is written here.
        //   _pn       - injected NMEA/position fix sink (was mf.pn). Wired late via SetNmea(...) because
        //               CNMEA's constructor takes this CSim (CNMEA(ApplicationModel, CSim, WorldGrid)),
        //               so CSim is created first and receives its CNMEA afterwards — this breaks the
        //               CNMEA<->CSim constructor cycle without introducing any new abstraction.
        // No FormGPS reference remains. The simulated-fix kinematics, constants and call cadence are
        // UNCHANGED (frozen, deterministic). See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly CAHRS _ahrs;
        private readonly CModuleComm _mc;
        private CNMEA _pn;

        #region properties sim

        public Wgs84 CurrentLatLon { get; set; }

        public double headingTrue, stepDistance = 0.0, steerAngle, steerangleAve = 0.0;
        public double steerAngleScrollBar = 0;

        public bool isAccelForward, isAccelBack;

        // [XPLAT] Simulator run-state flag, the portable replacement for the old WinForms
        // `FormGPS.timerSim.Enabled` gate (this is NOT a WinForms Timer). It is read by
        // CNMEA.DefineLocalPlane to decide whether to re-seat the simulator origin while simulating;
        // the gate — and everything it guards — is behaviorally unchanged.
        public bool IsActive { get; set; }

        #endregion properties sim

        // [XPLAT] Raised once per simulated tick, after the simulated fix has been written into the
        // injected NMEA / AHRS / model state, to drive the fix-processing scan loop. This replaces the
        // former direct `mf.UpdateFixPosition()` call: the extracted PositionService subscribes to this
        // event so the receive->fuse->steer->section path is triggered with the exact same cadence as
        // before. A plain CLR event keeps the simulator free of any FormGPS / PositionService
        // compile-time coupling and introduces no DI container or new abstraction.
        public event Action FixGenerated;

        // [XPLAT] Inject the shared model + collaborators instead of the WinForms host form
        // (was `CSim(FormGPS _f)`). The CNMEA fix sink is supplied later via SetNmea(...) to break the
        // CNMEA<->CSim constructor cycle. The initial simulated position is read from settings exactly
        // as before.
        public CSim(ApplicationModel appModel, CAHRS ahrs, CModuleComm mc)
        {
            _appModel = appModel;
            _ahrs = ahrs;
            _mc = mc;
            CurrentLatLon = new Wgs84(
                Properties.Settings.Default.setGPS_SimLatitude,
                Properties.Settings.Default.setGPS_SimLongitude);
        }

        // [XPLAT] Wire the CNMEA fix sink after construction. CNMEA's constructor takes this CSim, so
        // the composition root creates CSim first, then the CNMEA, then calls SetNmea(pn) here — which
        // breaks the otherwise-circular CNMEA<->CSim construction without any new abstraction.
        public void SetNmea(CNMEA pn)
        {
            _pn = pn;
        }

        public void DoSimTick(double _st)
        {
            steerAngle = _st;

            double diff = Math.Abs(steerAngle - steerangleAve);

            if (diff > 11)
            {
                if (steerangleAve >= steerAngle)
                {
                    steerangleAve -= 6;
                }
                else steerangleAve += 6;
            }
            else if (diff > 5)
            {
                if (steerangleAve >= steerAngle)
                {
                    steerangleAve -= 2;
                }
                else steerangleAve += 2;
            }
            else if (diff > 1)
            {
                if (steerangleAve >= steerAngle)
                {
                    steerangleAve -= 0.5;
                }
                else steerangleAve += 0.5;
            }
            else
            {
                steerangleAve = steerAngle;
            }

            // [XPLAT] mf.mc -> injected _mc (module-comm state); value/scaling unchanged.
            _mc.actualSteerAngleDegrees = steerangleAve;

            double temp = stepDistance * Math.Tan(steerangleAve * 0.0165329252) / 2;
            headingTrue += temp;
            if (headingTrue > glm.twoPI) headingTrue -= glm.twoPI;
            if (headingTrue < 0) headingTrue += glm.twoPI;

            // [XPLAT] mf.pn -> injected _pn (NMEA fix state); computation unchanged.
            _pn.vtgSpeed = Math.Abs(Math.Round(4 * stepDistance * 10, 2));
            _pn.AverageTheSpeed();

            //Calculate the next Lat Long based on heading and distance
            CurrentLatLon = CurrentLatLon.CalculateNewPostionFromBearingDistance(headingTrue, stepDistance);

            // [XPLAT] mf.AppModel -> injected _appModel (shared Core model); projection unchanged.
            GeoCoord fixCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(CurrentLatLon);
            _pn.fix.northing = fixCoord.Northing;
            _pn.fix.easting = fixCoord.Easting;
            _pn.headingTrue = _pn.headingTrueDual = glm.toDegrees(headingTrue);
            // [XPLAT] mf.ahrs -> injected _ahrs (IMU state); value unchanged.
            _ahrs.imuHeading = _pn.headingTrue;
            if (_ahrs.imuHeading >= 360) _ahrs.imuHeading -= 360;

            _appModel.CurrentLatLon = CurrentLatLon;

            _pn.hdop = 0.7;

            _pn.altitude = SimulateAltitude(_appModel.CurrentLatLon);

            _pn.satellitesTracked = 12;

            // [XPLAT] mf.sentenceCounter -> _appModel.sentenceCounter (relocated to the shared Core
            // model); the simulator resets the stale-fix watchdog every tick exactly as before.
            _appModel.sentenceCounter = 0;

            // [XPLAT] mf.UpdateFixPosition() -> FixGenerated event; PositionService subscribes and runs
            // the fix-processing scan loop, preserving the original per-tick call cadence.
            FixGenerated?.Invoke();

            if (isAccelForward)
            {
                isAccelBack = false;
                stepDistance += 0.02;
                if (stepDistance > 0.12) isAccelForward = false;
            }
            if (isAccelBack)
            {
                isAccelForward = false;
                stepDistance -= 0.01;
                if (stepDistance < -0.06) isAccelBack = false;
            }
        }

        private double SimulateAltitude(Wgs84 latLon)
        {
            double temp = Math.Abs(latLon.Latitude * 100);
            temp -= ((int)(temp));
            temp *= 100;
            double altitude = temp + 200;

            temp = Math.Abs(latLon.Longitude * 100);
            temp -= ((int)(temp));
            temp *= 100;
            altitude += temp;
            return altitude;
        }

    }
}
