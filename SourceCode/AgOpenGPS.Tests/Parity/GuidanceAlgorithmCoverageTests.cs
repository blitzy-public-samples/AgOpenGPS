// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// GUIDANCE-ALGORITHM coverage parity suite (QA F5 findings M2 + M5).
//
// WHY THIS EXISTS
// --------------------------------------------------------------------------------------------------
// QA F5 found that several checkpoint-required algorithms had ZERO committed runtime coverage:
//   * M2 — CDubins path generation had no committed test at all.
//   * M5 — CYouTurn / CABCurve / CContour / CRecordedPath / CSmartWAS were never constructed or invoked by
//          any test (Pure-Pursuit/CABLine is covered by GuidanceEquivalenceTests; Stanley/CGuidance too).
// The QA report established behavioral parity for these by source-diff against net48 860eb9fd (decoupling
// only — no math edits) but had no repeatable CI gate. This suite closes that gap by INVOKING the REAL
// production methods (constructed via ParityGraphFixture — see that file for why "impractical to isolate"
// is false) over deterministic inputs and asserting production-captured goldens
// (Parity/Golden/Guidance/algorithms.csv). Counts/states are compared EXACTLY; distances, path lengths and
// steer angles use Within(0.001) per AAP §0.6.4 (and cross-OS float-determinism guard, AAP §0.6.5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using AgOpenGPS.Core.Models;   // GeoCoord, GeoDir
using AgOpenGPS.Tests.Parity;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Production-invoking coverage tests for the guidance algorithms QA F5 flagged as uncovered: Dubins (M2)
    /// and CSmartWAS / CContour / CABCurve / CRecordedPath / CYouTurn (M5). Each test drives a REAL production
    /// method over the exact input that produced the committed golden.
    /// </summary>
    [TestFixture]
    public class GuidanceAlgorithmCoverageTests
    {
        private const double Tolerance = 0.001;   // AAP §0.6.4 steer/length tolerance

        // ===========================================================================================
        // Golden loading (same convention as the other parity suites).
        // ===========================================================================================

        /// <summary>Loads algorithms.csv into a map keyed "algorithm|metric" (e.g. "dubins|pose1_len").</summary>
        private static Dictionary<string, double> LoadGolden()
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", "algorithms.csv");

            Assert.That(File.Exists(path), Is.True,
                $"Required algorithm golden artifact is missing: {path}. Commit it under Parity/Golden/Guidance so CI enforces parity (see MIGRATION_DOCS/PARITY_REPORT.md).");

            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            bool headerSeen = false;
            foreach (var rawLine in File.ReadAllLines(path))
            {
                if (rawLine == null) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!headerSeen) { headerSeen = true; continue; }

                string[] c = line.Split(',');
                Assert.That(c.Length, Is.EqualTo(3), "algorithms.csv row must have 3 columns: algorithm,metric,value");
                map[c[0].Trim() + "|" + c[1].Trim()] = double.Parse(c[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            Assert.That(map.Count, Is.GreaterThan(0), "algorithms.csv contained no data rows.");
            return map;
        }

        /// <summary>Summed Euclidean length of a vec3 polyline.</summary>
        private static double PathLength(List<vec3> p)
        {
            double len = 0;
            for (int i = 1; i < p.Count; i++)
            {
                double dx = p[i].easting - p[i - 1].easting;
                double dz = p[i].northing - p[i - 1].northing;
                len += Math.Sqrt((dx * dx) + (dz * dz));
            }
            return len;
        }

        // ===========================================================================================
        // M2: CDubins.GenerateDubins — path generation + determinism.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CDubins.GenerateDubins"/> for three known poses (straight, lateral-shift,
        /// U-turn) and asserts the production path point count (exact) and length (Within 0.001) match the
        /// captured golden. Also re-invokes the straight pose and asserts an IDENTICAL count+length to prove
        /// determinism. The straight pose reproduces the QA-observed ≈30.0 m euclidean result. (resolves M2)
        /// </summary>
        [Test]
        public void Dubins_GenerateDubins_MatchesGolden_AndIsDeterministic()
        {
            var golden = LoadGolden();
            CDubins.turningRadius = 8.1;
            var dub = new CDubins();

            var p1 = dub.GenerateDubins(new vec3(0, 0, 0), new vec3(0, 30, 0));
            int c1 = p1.Count; double l1 = PathLength(p1);

            var p1b = dub.GenerateDubins(new vec3(0, 0, 0), new vec3(0, 30, 0));   // determinism re-run
            int c1b = p1b.Count; double l1b = PathLength(p1b);

            var p2 = dub.GenerateDubins(new vec3(0, 0, 0), new vec3(10, 30, 0));
            int c2 = p2.Count; double l2 = PathLength(p2);

            var p3 = dub.GenerateDubins(new vec3(0, 0, 0), new vec3(5, 0, Math.PI));
            int c3 = p3.Count; double l3 = PathLength(p3);

            Assert.Multiple(() =>
            {
                Assert.That(c1, Is.EqualTo((int)golden["dubins|pose1_count"]), "pose1 count");
                Assert.That(l1, Is.EqualTo(golden["dubins|pose1_len"]).Within(Tolerance), "pose1 length (≈30 m straight)");
                Assert.That(c2, Is.EqualTo((int)golden["dubins|pose2_count"]), "pose2 count");
                Assert.That(l2, Is.EqualTo(golden["dubins|pose2_len"]).Within(Tolerance), "pose2 length");
                Assert.That(c3, Is.EqualTo((int)golden["dubins|pose3_count"]), "pose3 count");
                Assert.That(l3, Is.EqualTo(golden["dubins|pose3_len"]).Within(Tolerance), "pose3 length (U-turn)");

                // Determinism: identical inputs -> identical production output.
                Assert.That(c1b, Is.EqualTo(c1), "Dubins must be deterministic (count)");
                Assert.That(l1b, Is.EqualTo(l1).Within(1e-9), "Dubins must be deterministic (length)");
            });
        }

        // ===========================================================================================
        // M5: CSmartWAS statistics over a deterministic sample sequence.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CSmartWAS"/>: starts collection, feeds 250 deterministic samples through
        /// the production gate (autosteer on, speed/dist within limits) and asserts the production Mean / Median
        /// / StdDev / RecommendedOffset / SampleCount match the captured golden. (resolves M5: CSmartWAS)
        /// </summary>
        [Test]
        public void SmartWas_Statistics_MatchGolden()
        {
            var golden = LoadGolden();
            var g = ParityGraphFixture.Build();

            g.SmartWas.Start();
            g.AppModel.isBtnAutoSteerOn = true;
            g.AppModel.avgSpeed = 5.0;
            g.AppModel.guidanceLineDistanceOff = 0;

            for (int i = 0; i < 250; i++)
            {
                double angle = (((i * 37) % 101) - 50) / 10.0;   // deterministic, range [-5.0, 5.0]
                g.SmartWas.AddSample(angle);
            }

            Assert.Multiple(() =>
            {
                Assert.That(g.SmartWas.SampleCount, Is.EqualTo((int)golden["smartwas|sampleCount"]), "sample count");
                Assert.That(g.SmartWas.Mean, Is.EqualTo(golden["smartwas|mean"]).Within(Tolerance), "mean");
                Assert.That(g.SmartWas.Median, Is.EqualTo(golden["smartwas|median"]).Within(Tolerance), "median");
                Assert.That(g.SmartWas.StdDev, Is.EqualTo(golden["smartwas|stddev"]).Within(Tolerance), "stddev");
                Assert.That(g.SmartWas.RecommendedOffset, Is.EqualTo(golden["smartwas|recOffset"]).Within(Tolerance), "recommended offset");
            });
        }

        // ===========================================================================================
        // M5: CContour.DistanceFromContourLine steer-angle.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CContour.DistanceFromContourLine"/> (Stanley branch) over a straight
        /// contour strip and asserts the production <c>steerAngleCT</c> and cross-track distance match the
        /// captured golden. (resolves M5: CContour)
        /// </summary>
        [Test]
        public void Contour_DistanceFromContourLine_SteerAngle_MatchesGolden()
        {
            var golden = LoadGolden();
            bool prev = Properties.ToolSettings.Default.setVehicle_isStanleyUsed;
            try
            {
                Properties.ToolSettings.Default.setVehicle_isStanleyUsed = true;
                var g = ParityGraphFixture.Build();
                g.Vehicle.stanleyDistanceErrorGain = 1.0;
                g.Vehicle.stanleyHeadingErrorGain = 1.0;
                g.Vehicle.maxSteerAngle = 30;
                g.AppModel.avgSpeed = 5.0;
                g.AppModel.isReverse = false;

                for (int i = 0; i < 12; i++)
                {
                    g.Contour.ctList.Add(new vec3(0, i * 2.0, 0));   // north line at easting 0
                }

                g.Contour.DistanceFromContourLine(new vec3(0.3, 10, 0), new vec3(0.3, 10, 0));

                Assert.Multiple(() =>
                {
                    Assert.That(g.Contour.steerAngleCT, Is.EqualTo(golden["contour|steerAngleCT"]).Within(Tolerance), "steerAngleCT");
                    Assert.That(g.Contour.distanceFromCurrentLinePivot, Is.EqualTo(golden["contour|distPivot"]).Within(Tolerance), "distanceFromCurrentLinePivot");
                });
            }
            finally { Properties.ToolSettings.Default.setVehicle_isStanleyUsed = prev; }
        }

        // ===========================================================================================
        // M5: CABCurve.BuildNewOffsetList curve-offset geometry.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CABCurve.BuildNewOffsetList"/> (Curve mode) to offset a north reference
        /// curve by 2 m and asserts the production offset-point count (exact) and first/last coordinates match
        /// the captured golden. (resolves M5: CABCurve)
        /// </summary>
        [Test]
        public void AbCurve_BuildNewOffsetList_MatchesGolden()
        {
            var golden = LoadGolden();
            var g = ParityGraphFixture.Build();

            var track = new CTrk { mode = TrackMode.Curve };
            for (int i = 0; i < 20; i++)
            {
                track.curvePts.Add(new vec3(0, i * 1.0, 0));   // north curve at easting 0
            }

            var offset = g.Curve.BuildNewOffsetList(2.0, track);

            Assert.That(offset.Count, Is.EqualTo((int)golden["abcurve|offset_count"]), "offset point count");
            Assert.That(offset.Count, Is.GreaterThan(0), "offset list must not be empty");
            Assert.Multiple(() =>
            {
                Assert.That(offset[0].easting, Is.EqualTo(golden["abcurve|first_e"]).Within(Tolerance), "first easting");
                Assert.That(offset[0].northing, Is.EqualTo(golden["abcurve|first_n"]).Within(Tolerance), "first northing");
                Assert.That(offset[offset.Count - 1].easting, Is.EqualTo(golden["abcurve|last_e"]).Within(Tolerance), "last easting");
                Assert.That(offset[offset.Count - 1].northing, Is.EqualTo(golden["abcurve|last_n"]).Within(Tolerance), "last northing");
            });
        }

        // ===========================================================================================
        // M5: CRecordedPath.StartDrivingRecordedPath (recorded-path + Dubins approach).
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CRecordedPath.StartDrivingRecordedPath"/>: with an 8-point recorded path
        /// and the vehicle at the origin, the method must build a valid Dubins approach path and start driving.
        /// Asserts the production start flag, shuttle-list length (exact) and current index match the golden.
        /// (resolves M5: CRecordedPath)
        /// </summary>
        [Test]
        public void RecordedPath_StartDriving_MatchesGolden()
        {
            var golden = LoadGolden();
            CDubins.turningRadius = 8.1;
            var g = ParityGraphFixture.Build();

            g.RecPath.SetReferences(g.Vehicle, g.YouTurn);
            g.YouTurn.youTurnRadius = 8.1;
            g.AppModel.PivotAxlePos = new GeoCoord(0, 0);   // (northing, easting)
            g.AppModel.FixHeading = new GeoDir(0);
            for (int i = 0; i < 8; i++)
            {
                g.RecPath.recList.Add(new CRecPathPt(0, 20 + (i * 1.0), 0, 5.0, false));
            }
            g.RecPath.resumeState = 0;

            bool started = g.RecPath.StartDrivingRecordedPath();

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.True, "recorded-path drive must start with a valid Dubins approach");
                Assert.That(g.RecPath.isDrivingRecordedPath, Is.True, "isDrivingRecordedPath flag");
                Assert.That(g.RecPath.shuttleListCount, Is.EqualTo((int)golden["recpath|shuttleListCount"]), "Dubins approach path length");
                Assert.That(g.RecPath.currentPositonIndex, Is.EqualTo((int)golden["recpath|currentPositonIndex"]), "current position index");
            });
        }

        // ===========================================================================================
        // M5: CYouTurn.DistanceFromYouTurnLine steer-angle.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="CYouTurn.DistanceFromYouTurnLine"/> (Stanley branch) over a straight
        /// you-turn path and asserts the production <c>steerAngleYT</c> and cross-track distance match the
        /// captured golden. (resolves M5: CYouTurn)
        /// </summary>
        [Test]
        public void YouTurn_DistanceFromYouTurnLine_SteerAngle_MatchesGolden()
        {
            var golden = LoadGolden();
            bool prev = Properties.ToolSettings.Default.setVehicle_isStanleyUsed;
            try
            {
                Properties.ToolSettings.Default.setVehicle_isStanleyUsed = true;
                var g = ParityGraphFixture.Build();
                g.Vehicle.stanleyDistanceErrorGain = 1.0;
                g.Vehicle.stanleyHeadingErrorGain = 1.0;
                g.Vehicle.maxSteerAngle = 30;
                g.AppModel.avgSpeed = 5.0;
                g.AppModel.isReverse = false;

                for (int i = 0; i <= 20; i++)
                {
                    g.YouTurn.ytList.Add(new vec3(0, i * 1.0, 0));   // north path at easting 0
                }
                g.AppModel.SteerAxlePos = new GeoCoord(4, 0.2);      // (northing=4, easting=0.2) near ytList[4]
                g.AppModel.FixHeading = new GeoDir(0);

                bool ret = g.YouTurn.DistanceFromYouTurnLine();

                Assert.Multiple(() =>
                {
                    Assert.That(ret, Is.True, "DistanceFromYouTurnLine should track the path (not complete) for this pose");
                    Assert.That(g.YouTurn.steerAngleYT, Is.EqualTo(golden["youturn|steerAngleYT"]).Within(Tolerance), "steerAngleYT");
                    Assert.That(g.YouTurn.distanceFromCurrentLine, Is.EqualTo(golden["youturn|distFromLine"]).Within(Tolerance), "distanceFromCurrentLine");
                });
            }
            finally { Properties.ToolSettings.Default.setVehicle_isStanleyUsed = prev; }
        }
    }
}
