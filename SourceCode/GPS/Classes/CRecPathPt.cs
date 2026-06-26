// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Extract-Class move: the plain recorded-path point model (CRecPathPt) is lifted out of
// CRecordedPath.cs into its own file. CRecordedPath.cs holds the FormGPS-coupled recorded-path
// *manager* (it carries a `private readonly FormGPS mf;` back-reference and is therefore deferred
// behind the Checkpoint-6 source gate in AgOpenGPS.csproj until the guidance pipeline is decoupled,
// AAP §0.6.1). CRecPathPt, by contrast, is a framework-agnostic value model with no FormGPS,
// WinForms, Accord or System.Drawing dependency, and it is consumed by already-migrated cross-platform
// code — the recorded-path file I/O (IO/RecPathFiles.cs, an R2 behaviour-frozen contract). Moving it
// here keeps that code building while the manager stays gated. The body is copied verbatim from
// CRecordedPath.cs — behaviour frozen, no logic change.
using AgOpenGPS.Core.Models;

namespace AgOpenGPS
{
    public class CRecPathPt
    {
        public double easting { get; set; }
        public double northing { get; set; }
        public double heading { get; set; }
        public double speed { get; set; }
        public bool autoBtnState { get; set; }

        //constructor
        public CRecPathPt(double _easting, double _northing, double _heading, double _speed,
                            bool _autoBtnState)
        {
            easting = _easting;
            northing = _northing;
            heading = _heading;
            speed = _speed;
            autoBtnState = _autoBtnState;
        }

        public GeoCoord AsGeoCoord => new GeoCoord(northing, easting);
    }
}
