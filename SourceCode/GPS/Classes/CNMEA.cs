// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using System.Globalization;

namespace AgOpenGPS
{
    public class CNMEA
    {
        //our current fix
        public vec2 fix = new vec2(0, 0);

        //other GIS Info
        public double altitude, speed, vtgSpeed = float.MaxValue;

        public double headingTrueDual, headingTrue, hdop, age, headingTrueDualOffset;

        public int fixQuality, ageAlarm;
        public int satellitesTracked;

        // [XPLAT] Decoupled from the WinForms host-form god-object (the former `private readonly FormGPS mf;`).
        // The collaborators this fix/heading/speed state-holder needs are now constructor-injected and read /
        // written live-by-reference, exactly mirroring the established CSmartWAS(ApplicationModel) decoupling:
        //   _appModel  - shared AgOpenGPS.Core runtime model. Supplies the WGS84->local-plane projection
        //                (LocalPlane), the current lat/lon (CurrentLatLon), the shared field properties and
        //                the smoothed vehicle speed (avgSpeed) that previously lived on FormGPS. Same state,
        //                same scaling — guidance/position parity is preserved byte-for-byte.
        //   _sim       - the simulator service (was FormGPS.sim). Its CurrentLatLon is updated when the
        //                origin is (re)defined while simulating, and its IsActive flag is the portable
        //                replacement for the old WinForms `FormGPS.timerSim.Enabled` run-state gate
        //                (NOT a WinForms Timer).
        //   _worldGrid - the world grid (was FormGPS.worldGrid), re-centred around the vehicle as it moves.
        // No FormGPS reference remains, so this class compiles and runs unchanged on Windows, Linux and macOS.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        private readonly ApplicationModel _appModel;
        private readonly CSim _sim;
        private readonly WorldGrid _worldGrid;

        // [XPLAT] Inject the shared model + collaborators instead of the WinForms host form (was CNMEA(FormGPS f)).
        public CNMEA(ApplicationModel appModel, CSim sim, WorldGrid worldGrid)
        {
            //constructor, grab the shared model + collaborators
            _appModel = appModel;
            _sim = sim;
            _worldGrid = worldGrid;

            _appModel.LocalPlane = new LocalPlane(new Wgs84(0, 0), _appModel.SharedFieldProperties);
            ageAlarm = Properties.Settings.Default.setGPS_ageAlarm;
        }

        public void AverageTheSpeed()
        {
            //average the speed
            //if (speed > 70) speed = 70;
            // [XPLAT] avgSpeed relocated from FormGPS to the shared ApplicationModel; identical scaling/behavior.
            _appModel.avgSpeed = (_appModel.avgSpeed * 0.75) + (speed * 0.25);
        }

        public void DefineLocalPlane(Wgs84 origin, bool setSim)
        {
            _appModel.LocalPlane = new LocalPlane(origin, _appModel.SharedFieldProperties);
            // [XPLAT] `mf.timerSim.Enabled` -> `_sim.IsActive`: the simulator run-state now comes from the
            // injected sim service rather than a WinForms Timer. The gate (and everything it guards) is unchanged.
            if (setSim && _sim.IsActive)
            {
                _appModel.CurrentLatLon = origin;
                _sim.CurrentLatLon = origin;

                Properties.Settings.Default.setGPS_SimLatitude = _appModel.LocalPlane.Origin.Latitude;
                Properties.Settings.Default.setGPS_SimLongitude = _appModel.LocalPlane.Origin.Longitude;
                Properties.Settings.Default.Save();
            }
            GeoCoord geoCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(_appModel.CurrentLatLon);
            _worldGrid.checkZoomWorldGrid(geoCoord);
        }

    }
}
