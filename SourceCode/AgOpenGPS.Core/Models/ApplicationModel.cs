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
        //   guidanceLineSteerAngle  - commanded steer angle in 0.01-degree units (was
        //                             FormGPS.guidanceLineSteerAngle, short). Paired with
        //                             guidanceLineDistanceOff and relocated together so the
        //                             cross-platform guidance classes (CABLine/CABCurve/CGuidance/
        //                             CYouTurn) and the PGN autosteer transmitter share one canonical
        //                             autosteer output with no WinForms coupling. Scaling (degrees*100)
        //                             is preserved so the wire value stays byte-for-byte identical.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isBtnAutoSteerOn;
        public double avgSpeed;
        public short guidanceLineDistanceOff;
        public short guidanceLineSteerAngle;

        // [XPLAT] Monotonic elapsed-seconds clock relocated here from the WinForms host form (FormGPS,
        // GUI.Designer.cs `public double secondsSinceStart`) so the portable, cross-platform guidance
        // classes (e.g. CABLine/CABCurve AB-line refresh throttling) and the scan-loop read the SAME
        // canonical time base live-by-reference with no WinForms coupling. The type (double) and meaning
        // (seconds since program start, advanced by the fix/timer pipeline) are preserved exactly, so the
        // 0.66 s AB-line rebuild debounce and any other time-gated guidance behavior stay identical.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public double secondsSinceStart;

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

        // [XPLAT] GPS-data window "sentences on" flag relocated here from the WinForms host form
        // (FormGPS) so the portable, cross-platform UI (the Avalonia FormGPSDataView live-telemetry
        // read-out) clears the SAME canonical flag the original form did, without any WinForms coupling.
        // The name, type and default are preserved exactly from the original
        // (FormGPS: `public bool isGPSSentencesOn = false;`) so behavior stays identical: the original
        // FormGPSData_FormClosing handler set mf.isGPSSentencesOn = false on close, and the Avalonia
        // view reproduces that in its OnClosed override (FormGPSDataView.axaml.cs).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isGPSSentencesOn;

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

        // [XPLAT] Heading-acquisition runtime state relocated here from the WinForms host form
        // (FormGPS, Position.designer.cs) so the portable, cross-platform fix/position pipeline writes,
        // and the cross-platform renderer (CVehicle.DrawVehicle) reads, the SAME canonical state
        // live-by-reference with no WinForms coupling. Names, types and defaults are preserved exactly
        // from the originals so behavior stays byte-for-byte identical:
        //   isFirstHeadingSet  - true once the first valid GPS/IMU heading has been established
        //                        (was FormGPS.isFirstHeadingSet, bool, default false). Gates drawing of
        //                        the rigid hitch, the antenna dot and the "no-heading" question mark.
        //   headingFromSource  - identifies the heading source string (e.g. "Dual", "Fix", "VTG", "GPS")
        //                        (was FormGPS.headingFromSource, string, default null). When it equals
        //                        "Dual" the dual-antenna source already supplies heading, so the
        //                        "no-heading" question mark is suppressed.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isFirstHeadingSet;
        public string headingFromSource;

        // [XPLAT] Tool/tank hitch headings and the field-job gate relocated here from the WinForms host
        // form (FormGPS) so the portable, cross-platform fix/position pipeline writes them, and the
        // cross-platform tool renderer (CTool.DrawTool) reads them, live-by-reference with no WinForms
        // coupling. Only the heading scalars (not the full vec3 tool/tank positions, which remain in the
        // GPS layer because vec3 is a GPS type) are relocated, exactly as needed by the draw, and the
        // job gate that the section/coverage logic already keys on. Names, types and defaults are
        // preserved so behavior stays byte-for-byte identical:
        //   ToolPivotHeading - tool-pivot heading in radians (was FormGPS.toolPivotPos.heading, double).
        //                      Stored as the portable GeoDir (its AngleInRadians is what the draw rotates
        //                      by); GeoDir's [0, 2pi) normalization is rotation-neutral for GL.Rotate, so
        //                      the rendered tool orientation is unchanged. Mirrors the existing
        //                      FixHeading GeoDir property.
        //   TankHeading      - towed-tank (TBT) heading in radians (was FormGPS.tankPos.heading, double),
        //                      stored as GeoDir for the same reason; only read when isToolTBT &&
        //                      isToolTrailing, identical to the original.
        //   isJobStarted     - field-job gate (was FormGPS.isJobStarted, bool, default false). When true
        //                      the tool draw renders the look-ahead section lines; gating is unchanged.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public GeoDir ToolPivotHeading { get; set; }
        public GeoDir TankHeading { get; set; }
        public bool isJobStarted;

        // [XPLAT] Vehicle steer-axle position relocated here from the WinForms host form (FormGPS,
        // Position.designer.cs) so the portable, cross-platform guidance code — the extracted track
        // manager (CTrack.FindClosestRefTrack) and, as they are decoupled, the AB-line/curve guidance
        // classes — reads the SAME canonical steer-axle position live-by-reference with no WinForms
        // coupling. It is stored as the portable Core GeoCoord (Easting/Northing) rather than the
        // GPS-layer vec3, exactly as the tool/tank headings above are stored as GeoDir, because vec3 is a
        // GPS type that must not leak into Core. Only the easting/northing scalars that the guidance
        // distance math reads are needed, and their numeric values are preserved exactly, so track
        // selection stays byte-for-byte identical to the original. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public GeoCoord SteerAxlePos { get; set; }

        // [XPLAT] Vehicle pivot-axle position relocated here from the WinForms host form (FormGPS,
        // Position.designer.cs `public vec3 pivotAxlePos`) so the portable, cross-platform boundary
        // controller (the CBoundary/CFence partial — DrawFenceLines, which draws the in-progress
        // recorded boundary relative to the live pivot) reads the SAME canonical pivot-axle point
        // live-by-reference with no WinForms coupling. It is stored as the portable Core GeoCoord
        // (Easting/Northing) rather than the GPS-layer vec3 — exactly as SteerAxlePos above — because
        // vec3 is a GPS type that must not leak into Core; the heading half of the old vec3 is the fix
        // heading, already exposed as FixHeading (the original set pivotAxlePos.heading = fixHeading, so
        // the [0, 2pi) GeoDir normalization is rotation-neutral for the Sin/Cos draw math). The
        // fix/position pipeline writes it (mirroring SteerAxlePos), so the easting/northing values stay
        // identical per fix and the rendered boundary geometry is unchanged (render parity).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public GeoCoord PivotAxlePos { get; set; }

        // [XPLAT] Guidance look-ahead reference position relocated here from the WinForms host form
        // (FormGPS, Position.designer.cs `public vec2 guidanceLookPos`) so the portable, cross-platform
        // AB-line and curve guidance classes (CABLine/CABCurve) read the SAME canonical look-ahead
        // reference point live-by-reference with no WinForms coupling. It is stored as the portable Core
        // GeoCoord (Easting/Northing) rather than the GPS-layer vec2 — exactly as SteerAxlePos /
        // ToolPivotPosition above — because vec2 is a GPS type that must not leak into Core. The
        // fix/position pipeline writes it, so the easting/northing values the reference-line distance math
        // reads are preserved exactly and the "which AB line is the vehicle on" selection stays
        // byte-for-byte identical. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public GeoCoord GuidanceLookPos { get; set; }

        // [XPLAT] Tool-pivot position relocated here from the WinForms host form (FormGPS,
        // Position.designer.cs: `public vec3 toolPivotPos`) so the portable, cross-platform headland
        // controller (the CBoundary/CHead partial — CheckHeadlandProximity and WhereAreToolLookOnPoints)
        // reads the SAME canonical tool-pivot point live-by-reference with no WinForms coupling. It is
        // stored as the portable Core GeoCoord (Easting/Northing) rather than the GPS-layer vec3 —
        // exactly as SteerAxlePos above — because vec3 is a GPS type that must not leak into Core; the
        // heading half of the old vec3 is already exposed as ToolPivotHeading. The fix/position pipeline
        // writes it, so the values stay identical per fix and the headland geometry/gating is unchanged
        // (the CHead consumer recomposes the original vec3 from this position plus ToolPivotHeading).
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public GeoCoord ToolPivotPosition { get; set; }

        // [XPLAT] Per-fix scan-loop flags relocated here from the WinForms host form (FormGPS:
        // Position.designer.cs `isReverse`, GUI.Designer.cs `isHeadlandDistanceOn`) so the portable,
        // cross-platform headland controller (CHead) reads them live-by-reference with no WinForms
        // coupling. Names, types and the false default are preserved exactly from the originals so the
        // hydraulic-lift gating (only acts when moving forward: !isReverse) and the headland-distance
        // proximity-alarm gating (isHeadlandDistanceOn) stay behavior-identical. The fix/position
        // pipeline and the settings loader write them. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public bool isReverse;
        public bool isHeadlandDistanceOn;

        // [XPLAT] Machine-data PGN 0xEF (239) frame buffer relocated here from the deleted WinForms host
        // (FormGPS, PGN.Designer.cs: `public CPGN_EF p_239`) so the portable, cross-platform headland
        // controller (CHead.SetHydPosition) writes the hydraulic-lift command byte into this live buffer
        // and the cross-platform PGN dispatcher transmits it — exactly as before, with no WinForms
        // coupling. The frame bytes (the 0x80 0x81 0x7F header, the 0xEF id, the length and trailing
        // 0xCC) and the hydLift byte index (7) are preserved verbatim from CPGN_EF so the machine-byte
        // semantics stay byte-for-byte identical (the AgIO loopback dispatcher and the ModSim simulator
        // both read the hydLift state from byte index 7 of this frame). The byte[]-plus-named-index shape
        // mirrors the existing PGN-buffer convention in CModuleComm (e.g. `ss` + `swHeader`/`swMain`...),
        // introducing no new abstraction. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public byte[] machinePgnEF = new byte[] { 0x80, 0x81, 0x7f, 0xEF, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0xCC };
        public int machinePgnEFHydLift = 7;

        // [XPLAT] PGN 0xEF (239) "uturn" command byte index relocated here from the deleted WinForms host
        // (FormGPS, PGN.Designer.cs: `public CPGN_EF p_239` whose `uturn = 5`) so the portable,
        // cross-platform U-turn generator (CYouTurn.ResetYouTurn / ResetCreatedYouTurn) can clear the
        // U-turn-active byte of the same canonical machine PGN frame above, with no WinForms coupling —
        // mirroring the machinePgnEFHydLift index used by CHead. The byte index (5) is preserved verbatim
        // from CPGN_EF so the machine-byte semantics stay byte-for-byte identical.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public int machinePgnEFUturn = 5;

        // [XPLAT] Section/applied-coverage day colour relocated here from the deleted WinForms host form
        // (FormGPS, GUI.Designer.cs: `public Color sectionColorDay;`, loaded at startup from
        // Properties.Settings.Default.setDisplay_colorSectionsDay.CheckColorFor255()) so the portable,
        // cross-platform coverage builder (CPatches, which writes this colour's R/G/B bytes into the first
        // vertex of every applied-area patch triangle-strip) and the Avalonia "Color Set" dialog
        // (FormColorView.IColorSettingsState.SectionColorDay) read and write the SAME canonical colour
        // live-by-reference, with no WinForms coupling. The type (System.Drawing.Color — already used by the
        // Core ColorRgba conversions, so no new dependency) and the value are preserved exactly so the
        // rendered/round-tripped coverage colour stays byte-for-byte identical (render + FieldRoundTripTests
        // parity). The default Color.FromArgb(27, 151, 160) mirrors the settings default (Settings.cs) and the
        // FormColor reset, providing a safe pre-load fallback; the settings loader overwrites it at startup
        // exactly as the original did. See MIGRATION_DOCS/TRANSITION_MAP.md.
        public System.Drawing.Color SectionColorDay { get; set; } = System.Drawing.Color.FromArgb(27, 151, 160);

        // [XPLAT] Applied-patch tally relocated here from the deleted WinForms host form (FormGPS,
        // Position.designer.cs: `public int patchCounter = 0;`, reset to 0 on new/closed field) so the
        // portable, cross-platform coverage builder (CPatches.TurnMappingOn increments it per applied
        // patch) and the field life-cycle logic read and write the SAME canonical counter
        // live-by-reference, with no WinForms coupling — mirroring the sentenceCounter relocation above.
        // The type (int) and default (0) are preserved exactly so the diagnostic tally stays identical.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public int patchCounter;

        // [XPLAT] U-turn sequencing counter relocated here from the deleted WinForms host form (FormGPS,
        // GUI.Designer.cs: `public int makeUTurnCounter = 0;`, advanced by the per-fix GUI/scan timer and
        // read/reset by the U-turn generator) so the portable, cross-platform U-turn generator (CYouTurn)
        // and the fix/scan-loop pipeline read and write the SAME canonical counter live-by-reference, with
        // no WinForms coupling — mirroring the sentenceCounter/patchCounter relocations above. The type
        // (int) and default (0) are preserved exactly so the U-turn trigger sequencing stays identical.
        // See MIGRATION_DOCS/TRANSITION_MAP.md.
        public int makeUTurnCounter;

        // [XPLAT] Live pivot-to-turn-line distance relocated here from the deleted WinForms host form
        // (FormGPS, Position.designer.cs: `public double distancePivotToTurnLine = -2222;`) so the portable,
        // cross-platform U-turn generator (CYouTurn) writes it and the fix/scan-loop pipeline (turn
        // triggering), the status read-outs and the renderer read the SAME canonical distance
        // live-by-reference, with no WinForms coupling. The type (double) and — critically — the sentinel
        // default (-2222, the "no valid distance yet" marker that the > 0 display/trigger guards key on)
        // are preserved exactly so the turn-trigger distances and read-outs stay byte-for-byte identical
        // (turn parity). See MIGRATION_DOCS/TRANSITION_MAP.md.
        public double distancePivotToTurnLine = -2222;
    }

    // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
    // Section-master / autosteer on-screen button tri-state. Moved out of the deleted WinForms
    // FormGPS partial (Sections.Designer.cs) into the portable Core so the shared ApplicationModel
    // and the cross-platform GPS classes (CModuleComm, CSection, CYouTurn, CRecordedPath, ...)
    // reference one canonical definition. Member names, values and order are unchanged: Off, Auto, On.
    public enum btnStates { Off, Auto, On }
}
