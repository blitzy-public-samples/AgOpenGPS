using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using System.IO;

namespace AgOpenGPS.Core
{
    public class ApplicationModel
    {

        private IApplicationPresenter _applicationPresenter;

        public ApplicationModel(DirectoryInfo baseDirectory)
        {
            FieldsDirectory = baseDirectory.CreateSubdirectory("Fields");
            VehiclesDirectory = baseDirectory.CreateSubdirectory("Vehicles");
            SharedFieldProperties = new SharedFieldProperties();

            Fields = new Fields(FieldsDirectory);
        }

        public void SetPresenter(IApplicationPresenter applicationPresenter)
        {
            _applicationPresenter = applicationPresenter;
        }

        public DirectoryInfo FieldsDirectory { get; }
        public DirectoryInfo VehiclesDirectory { get; }

        public SharedFieldProperties SharedFieldProperties { get; }
        public Fields Fields { get; }

        public Wgs84 CurrentLatLon { get; set; }
        public GeoDir FixHeading { get; set; }

        public LocalPlane LocalPlane { get; set; }

        // [XPLAT] Autosteer/guidance runtime state relocated here from the WinForms host form
        // (FormGPS) so portable, cross-platform domain code (e.g. CSmartWAS) can read and write it
        // live-by-reference without any WinForms coupling. Names, types and scaling are preserved
        // exactly from the originals so guidance/autosteer behavior stays byte-for-byte identical:
        //   isBtnAutoSteerOn        - autosteer-engaged flag (was FormGPS.isBtnAutoSteerOn, bool).
        //   avgSpeed                - smoothed vehicle speed in km/h (was FormGPS.avgSpeed, double).
        //   guidanceLineDistanceOff - signed cross-track distance in mm, rounded to a short
        //                             (was FormGPS.guidanceLineDistanceOff, short).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isBtnAutoSteerOn;
        public double avgSpeed;
        public short guidanceLineDistanceOff;
    }
}
