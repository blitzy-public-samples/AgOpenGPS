// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Extract-Class move: the plain track-line data model (TrackMode + CTrk) is lifted out of CTrack.cs
// into its own file. CTrack.cs holds the FormGPS-coupled track *manager* (it carries a
// `private readonly FormGPS mf;` back-reference and is therefore deferred behind the Checkpoint-6
// source gate in AgOpenGPS.csproj until the guidance pipeline is decoupled, AAP §0.6.1). These two
// types, by contrast, are framework-agnostic value/model types with no FormGPS, WinForms, Accord or
// System.Drawing dependency, and they are consumed by already-migrated cross-platform code — the
// track file I/O (IO/TrackFiles.cs, an R2 behaviour-frozen contract), the ISOXML import helpers
// (IsoXmlParserHelpers/TrackCopier/BoundaryBuilder) and the Avalonia track Views (FormCopyTracksView,
// FormQuickABView). Moving them here keeps that cross-platform code building while the manager stays
// gated. The type bodies are copied verbatim from CTrack.cs — behaviour frozen, no logic change.
using System.Collections.Generic;

namespace AgOpenGPS
{
    public enum TrackMode { None = 0, AB = 2, Curve = 4, bndTrackOuter = 8, bndTrackInner = 16, bndCurve = 32, waterPivot = 64 };//, Heading, Circle, Spiral

    public class CTrk
    {
        public List<vec3> curvePts = new List<vec3>();
        public double heading;
        public string name;
        public bool isVisible;
        public vec2 ptA;
        public vec2 ptB;
        public vec2 endPtA;
        public vec2 endPtB;
        public TrackMode mode;
        public double nudgeDistance;
        public HashSet<int> workedTracks = new HashSet<int>();

        public CTrk()
        {
            curvePts = new List<vec3>();
            heading = 3;
            name = "New Track";
            isVisible = true;
            ptA = new vec2();
            ptB = new vec2();
            endPtA = new vec2();
            endPtB = new vec2();
            mode = TrackMode.None;
            nudgeDistance = 0;
        }

        public CTrk(CTrk _trk)
        {
            curvePts = new List<vec3>(_trk.curvePts);
            heading = _trk.heading;
            name = _trk.name;
            isVisible = _trk.isVisible;
            ptA = _trk.ptA;
            ptB = _trk.ptB;
            endPtA = new vec2();
            endPtB = new vec2();
            mode = _trk.mode;
            nudgeDistance = _trk.nudgeDistance;
        }
    }
}
