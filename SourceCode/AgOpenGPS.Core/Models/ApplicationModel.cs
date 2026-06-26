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

        // [XPLAT] Section-master button tri-states relocated here from the WinForms host form
        // (FormGPS, Sections.Designer.cs) so portable cross-platform domain code — the CModuleComm
        // work/steer-switch bridge and the section/guidance logic — reads and writes the SAME state
        // the Avalonia view-models bind to, with no WinForms coupling. The type (btnStates) and the
        // defaults (btnStates.Off) are preserved exactly from the originals so section-control
        // activation stays behavior-identical. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public btnStates manualBtnState = btnStates.Off;
        public btnStates autoBtnState = btnStates.Off;

        // [XPLAT] Keep-offsets flag relocated here from the WinForms host form (FormGPS) so the
        // portable, cross-platform UI (the Avalonia FormShiftPosView drift-compensation editor) and
        // the field life-cycle logic read and write the SAME canonical flag without any WinForms
        // coupling. The name, type and default are preserved exactly from the original
        // (FormGPS: `public bool isKeepOffsetsOn = false;`) so behavior stays identical: when a field
        // is closed and isKeepOffsetsOn is false, SharedFieldProperties.DriftCompensation is reset to
        // GeoDelta(0,0); when true, the operator-entered drift offset is preserved across field close.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isKeepOffsetsOn;

        // [XPLAT] NMEA-sentence / fix-cadence watchdog counter relocated here from the WinForms host
        // form (FormGPS, GUI.Designer.cs) so the portable, cross-platform fix pipeline — the GPS
        // simulator (CSim), the extracted position/scan-loop service and the comm/NMEA decoders — all
        // read and write the SAME canonical counter the Avalonia status read-outs bind to, with no
        // WinForms coupling. The type (uint) and default (0) are preserved exactly from the original
        // (FormGPS: `public uint sentenceCounter = 0;`) so the stale-fix / "GPS data lost" watchdog
        // behavior stays byte-for-byte identical: it is incremented as sentences arrive and reset to 0
        // once a fix is applied (e.g. the simulator resets it every DoSimTick).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public uint sentenceCounter;
    }

    // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
    // Section-master / autosteer on-screen button tri-state. Moved out of the deleted WinForms
    // FormGPS partial (Sections.Designer.cs) into the portable Core so the shared ApplicationModel
    // and the cross-platform GPS classes (CModuleComm, CSection, CYouTurn, CRecordedPath, ...)
    // reference one canonical definition. Member names, values and order are unchanged: Off, Auto, On.
    public enum btnStates { Off, Auto, On }
}
