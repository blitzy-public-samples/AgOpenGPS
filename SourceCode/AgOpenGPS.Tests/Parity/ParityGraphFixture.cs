// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Shared production-graph builder for the guidance/steering behavioral-parity suites (QA F5 findings
// M1/M2/M5/M6).
//
// WHY THIS EXISTS
// --------------------------------------------------------------------------------------------------
// QA F5 found that the original parity suite never invoked production code — it reproduced simplified
// documented formulas in-test (M1) and compared them to formula-derived goldens (M6). The QA report
// further asserted the production classes were "impractical to isolate". That assertion is FALSE: every
// in-scope guidance class (CGuidance, CABLine, CABCurve, CContour, CYouTurn, CTrack, CDubins, CSmartWAS,
// CRecordedPath) is constructible from the SAME dependency-ordered graph the composition root builds in
// SourceCode/GPS/App.axaml.cs (step 9), minus the Avalonia/OpenGL/render objects the guidance math never
// touches. This fixture builds exactly that graph so the parity tests can drive the REAL production
// methods and compare their output to production-captured goldens — turning the suite into a genuine
// regression gate for the safety-critical steering domain.
//
// The construction order and constructor argument lists mirror App.axaml.cs verbatim (the domain classes
// are behaviour-frozen; only their construction location moved in the migration). The two cyclic
// guidance peers that App wires post-construction (CABLine ↔ CTrack/CYouTurn/CBoundary/CGuidance and
// CGuidance ↔ CABCurve/CABLine) are wired here through the public Set*References seams the classes expose,
// so the production GetCurrentABLine path can be exercised without a null peer.
//
// Avalonia is intentionally NOT referenced: IErrorPresenter and ICommand are satisfied by tiny no-op test
// stubs (CBoundary/CABCurve only store the references; they are not invoked on the guidance hot path).
using System;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using AgOpenGPS.Classes;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Services;   // [XPLAT] PgnDispatcher + SectionService (extracted from FormGPS partials) — section-parity (M1/M4/M6)

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Builds the behaviour-frozen guidance domain graph (the App.axaml.cs step-9 subset that the steering
    /// math depends on) so parity tests can invoke REAL production methods. Construction mirrors the
    /// composition root; no Avalonia/GL objects are created.
    /// </summary>
    internal static class ParityGraphFixture
    {
        /// <summary>No-op <see cref="IErrorPresenter"/> — CBoundary/CABCurve only store it; never called on the guidance path.</summary>
        internal sealed class TestErrorPresenter : IErrorPresenter
        {
            public void PresentTimedMessage(TimeSpan timeSpan, string titleString, string messageString) { }
        }

        /// <summary>No-op <see cref="ICommand"/> — satisfies CModuleComm/CABCurve command parameters in the test graph.</summary>
        internal sealed class NoopCommand : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) { }
        }

        /// <summary>
        /// Holds every constructed domain object plus the reflected (private) <c>CGuidance.DoSteerAngleCalc</c>
        /// method so tests can drive the real Stanley pipeline. All fields are the live production instances.
        /// </summary>
        internal sealed class Graph
        {
            public ApplicationModel AppModel;
            public CAHRS Ahrs;
            public CSound Sounds;
            public Camera Camera;
            public CModuleComm Mc;
            public CSim Sim;
            public CNMEA Pn;
            public CVehicle Vehicle;
            public CSection[] Section;
            public CTool Tool;
            public CBoundary Bnd;
            public CTram Tram;
            public CABLine ABLine;
            public CABCurve Curve;
            public CContour Contour;
            public CYouTurn YouTurn;
            public CTrack Track;
            public CRecordedPath RecPath;
            public CSmartWAS SmartWas;
            public CDubins Dubins;
            public CGuidance Guidance;

            // [XPLAT] Section-control collaborators (built only by BuildWithSections, used by
            // SectionControlParityTests). PgnDispatcher owns the p_254/p_239/p_229 PGN instances whose
            // bytes BuildMachineByte packs; CISOBUS is a PgnDispatcher ctor dependency.
            public CISOBUS Isobus;
            public PgnDispatcher Pgn;
            public SectionService Sections;

            private static readonly MethodInfo DoSteerAngleCalcMethod =
                typeof(CGuidance).GetMethod("DoSteerAngleCalc", BindingFlags.NonPublic | BindingFlags.Instance);

            /// <summary>Invokes the REAL (private) <c>CGuidance.DoSteerAngleCalc()</c> on the wired guidance instance.</summary>
            public void InvokeDoSteerAngleCalc()
            {
                if (DoSteerAngleCalcMethod == null)
                {
                    throw new InvalidOperationException(
                        "CGuidance.DoSteerAngleCalc could not be located via reflection — the production Stanley method changed shape.");
                }

                DoSteerAngleCalcMethod.Invoke(Guidance, null);
            }
        }

        /// <summary>
        /// Constructs a fresh, fully-wired guidance graph rooted at a unique temp base directory. Each call
        /// returns independent instances (so stateful guidance fields — smoothing/PID accumulators — start at
        /// their construction defaults, giving deterministic single-shot invocations).
        /// </summary>
        public static Graph Build()
        {
            var g = new Graph();

            var tempDir = new DirectoryInfo(
                Path.Combine(Path.GetTempPath(), "aog_parity_" + Guid.NewGuid().ToString("N")));
            tempDir.Create();

            g.AppModel = new ApplicationModel(tempDir);
            g.Ahrs = new CAHRS();
            g.Sounds = new CSound();
            g.Camera = new Camera(-65.0, 15.0);

            var vehicleTextures = new VehicleTextures();
            var screenTextures = new ScreenTextures();
            ICommand noop = new NoopCommand();

            g.Mc = new CModuleComm(g.AppModel, g.Ahrs, noop, noop, noop);
            g.Sim = new CSim(g.AppModel, g.Ahrs, g.Mc);
            var worldGrid = new WorldGrid(null, 0, 0);
            g.Pn = new CNMEA(g.AppModel, g.Sim, worldGrid);
            g.Vehicle = new CVehicle(g.AppModel, vehicleTextures, screenTextures, g.Camera, g.Mc, g.Sim);

            g.Section = new CSection[64];
            for (int i = 0; i < g.Section.Length; i++)
            {
                g.Section[i] = new CSection();
            }

            g.Tool = new CTool(g.AppModel, g.Section, g.Vehicle, g.Camera, vehicleTextures, g.Sim, g.Mc);
            IErrorPresenter ep = new TestErrorPresenter();
            g.Bnd = new CBoundary(g.AppModel, g.Section, g.Tool, g.Mc, g.Sounds, g.Vehicle, ep);
            g.Tram = new CTram(g.Bnd, g.Tool, g.Camera);
            g.ABLine = new CABLine(g.AppModel, g.Camera, g.Vehicle, g.Tool, g.Tram, g.Ahrs, g.Mc);
            g.Curve = new CABCurve(g.AppModel, g.Camera, g.Vehicle, g.Tool, g.Tram, g.Ahrs, g.Mc, ep, noop);
            g.Contour = new CContour(g.AppModel, g.Vehicle, g.Tool, g.Pn, g.Ahrs, g.ABLine);
            g.YouTurn = new CYouTurn(g.AppModel, g.Vehicle, g.Tool, g.Mc, g.Sounds);
            g.Track = new CTrack(g.Tool, g.AppModel);
            g.RecPath = new CRecordedPath(g.AppModel, g.Sim);
            g.SmartWas = new CSmartWAS(g.AppModel);
            g.Dubins = new CDubins();
            g.Guidance = new CGuidance(g.AppModel, g.Vehicle, g.Tool, g.Ahrs);

            // Wire the cyclic guidance peers through the SAME production helper the composition root uses
            // (GuidanceComposition.WireGuidanceReferences), so the parity graph and SourceCode/GPS/App.axaml.cs
            // wire all SEVEN seams IDENTICALLY — a single source of truth (QA Issue 9 / PARITY_REPORT Open Risk
            // #8). This both removes the prior drift (the fixture used to wire only five seams inline) and lets
            // GuidanceCompositionTests assert the production wiring against the live graph. Required so the
            // production CABLine.GetCurrentABLine pure-pursuit path can be driven without a null youturn/track/
            // guidance peer.
            GuidanceComposition.WireGuidanceReferences(
                g.ABLine, g.Curve, g.Contour, g.Track, g.YouTurn, g.RecPath, g.Guidance, g.Bnd, g.Vehicle);

            return g;
        }

        /// <summary>
        /// Builds the full guidance graph (via <see cref="Build"/>) and additionally constructs the REAL
        /// section-control collaborators — <see cref="CISOBUS"/>, <see cref="PgnDispatcher"/> (which owns the
        /// p_254/p_239/p_229 PGN byte buffers) and <see cref="SectionService"/> — wired exactly as the
        /// composition root does in App.axaml.cs (step 10). The two PgnDispatcher delegate parameters are
        /// satisfied by no-op test sinks (<c>postToUi</c> runs the action inline; <c>reportError</c> is a
        /// no-op), and the two SectionService path-count accessors return 0 (no track selected in the test),
        /// none of which are exercised on the BuildMachineByte / DoRemoteSwitches paths under test.
        /// </summary>
        public static Graph BuildWithSections()
        {
            var g = Build();

            // CISOBUS sendPgnToLoop sink is never triggered by BuildMachineByte/DoRemoteSwitches; no-op.
            g.Isobus = new CISOBUS(g.AppModel, _ => { });

            // postToUi runs the marshalled action inline (single-threaded test); reportError is a no-op sink.
            g.Pgn = new PgnDispatcher(
                g.Ahrs, g.Mc, g.Pn, g.Isobus, g.Track, g.AppModel,
                action => action(), _ => { });

            // getABLineHowManyPathsAway / getCurveHowManyPathsAway: only read by MarkAsWorkedTrack (master
            // toggle with a selected track) — not on the tested paths. Return 0.
            g.Sections = new SectionService(
                g.Section, g.Tool, g.Tram, g.Mc, g.Sounds, g.Track, g.Pgn, g.AppModel,
                () => 0, () => 0);

            return g;
        }
    }
}
