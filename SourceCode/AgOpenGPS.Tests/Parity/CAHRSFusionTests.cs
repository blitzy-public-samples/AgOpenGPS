// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// CAHRS IMU/GPS heading-fusion behavioral-parity suite (QA F5 finding M3).
//
// WHAT THIS PROVES, AND WHY IT IS DIFFERENT FROM A FORMULA REPRODUCTION
// --------------------------------------------------------------------------------------------------
// The heading/roll fusion that turns a raw IMU heading + the antenna-corrected GPS heading into the
// fused vehicle heading used to be inline inside the FormGPS-coupled scan loop
// (PositionService.UpdateFixPosition "#region IMU Fusion", itself extracted from net48
// Position.designer.cs). QA F5/M3 flagged that it had NO test and could not be exercised in isolation.
//
// The migration extracted that arithmetic VERBATIM into the pure, public static seam
// AgOpenGPS.CAHRS.FuseImuGpsHeading(...) (a decoupling-only move — AAP §0.7.1: "logic may move for
// decoupling but outputs may not change"). This fixture therefore invokes the REAL production method
// (not a re-implemented formula) over a representative IMU+GPS sequence and asserts the fused heading
// and the advanced offset reproduce the frozen, production-captured golden Within(0.001).
//
// GOLDEN PROVENANCE (resolves M6 for this path): Parity/Golden/Guidance/cahrs_fusion.csv and
// cahrs_fusion_iter.csv were captured by RUNNING this same production method; because the method body
// is byte-for-byte identical to the net48 baseline 860eb9fd by source-diff, the captured values ARE the
// net48 outputs (QA M6 explicitly accepts capture from the migrated production method). A future change
// to the fusion arithmetic would diverge from the frozen golden and FAIL — a genuine regression gate.
//
// `CAHRS` and `glm` resolve unqualified because AgOpenGPS.Tests.Parity is nested under the AgOpenGPS
// namespace (same as the existing GuidanceEquivalenceTests glm tie-in).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Production-invoking parity tests for <see cref="CAHRS.FuseImuGpsHeading"/> — the extracted IMU/GPS
    /// heading-fusion seam. Floating-point outputs are compared within 0.001 rad against the frozen,
    /// production-captured goldens; a missing golden FAILS the test so CI enforces fusion parity on every OS.
    /// </summary>
    [TestFixture]
    public class CAHRSFusionTests
    {
        /// <summary>Floating-point comparison tolerance for fused headings/offsets (radians), per AAP §0.6.4.</summary>
        private const double Tolerance = 0.001;

        /// <summary>
        /// [XPLAT] Forces InvariantCulture for the whole fixture so every CSV numeric parse uses a period
        /// decimal separator regardless of host locale (a comma-decimal locale must never change parsing).
        /// </summary>
        [OneTimeSetUp]
        public void ForceInvariantCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        // ---- Golden loading helpers (cross-OS; InvariantCulture; Path.Combine; case-sensitive "Guidance") ----

        private static string[] LoadRequiredGoldenLines(string fileName)
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", fileName);

            Assert.That(
                File.Exists(path),
                Is.True,
                $"Required CAHRS-fusion golden artifact is missing: {path}. Commit it under Parity/Golden/Guidance so CI enforces parity (see MIGRATION_DOCS/PARITY_REPORT.md).");

            return File.ReadAllLines(path);
        }

        private static IEnumerable<string[]> EnumerateDataRows(string[] lines)
        {
            bool headerSeen = false;
            foreach (var rawLine in lines)
            {
                if (rawLine == null) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!headerSeen)
                {
                    headerSeen = true; // first non-blank, non-comment line is the schema header — skip it
                    continue;
                }

                string[] cells = line.Split(',');
                for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
                yield return cells;
            }
        }

        private static double ParseDouble(string field)
            => double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static int ParseInt(string field)
            => int.Parse(field, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static bool ParseBool(string field)
            => bool.Parse(field.Trim());

        // ===========================================================================================
        // Single-shot fusion parity — REAL production CAHRS.FuseImuGpsHeading vs production-captured golden.
        // ===========================================================================================

        /// <summary>
        /// For every row of <c>cahrs_fusion.csv</c>, invokes the production
        /// <see cref="CAHRS.FuseImuGpsHeading"/> with a fresh offset and asserts the returned fused heading
        /// AND the advanced offset match the frozen net48-equivalent golden within 0.001 rad.
        /// </summary>
        [Test]
        public void Fusion_MatchesGolden_SingleShot()
        {
            string[] lines = LoadRequiredGoldenLines("cahrs_fusion.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(7),
                    "cahrs_fusion.csv row must have 7 columns: imuHeadingDeg,gpsHeadingRad,fusionWeight,isReverseWithIMU,initialOffset,expectedFusedHeadingRad,expectedFinalOffsetRad");

                double imuHeadingDeg = ParseDouble(row[0]);
                double gpsHeadingRad = ParseDouble(row[1]);
                double fusionWeight = ParseDouble(row[2]);
                bool isReverseWithIMU = ParseBool(row[3]);
                double initialOffset = ParseDouble(row[4]);
                double expectedFused = ParseDouble(row[5]);
                double expectedOffset = ParseDouble(row[6]);

                // Invoke the REAL production fusion seam (offset is advanced in place).
                double offset = initialOffset;
                double fused = CAHRS.FuseImuGpsHeading(
                    imuHeadingDeg, gpsHeadingRad, fusionWeight, isReverseWithIMU, ref offset);

                Assert.That(
                    fused,
                    Is.EqualTo(expectedFused).Within(Tolerance),
                    $"production fused heading (row {rowCount}) must match the net48 baseline within {Tolerance} rad.");
                Assert.That(
                    offset,
                    Is.EqualTo(expectedOffset).Within(Tolerance),
                    $"production advanced offset (row {rowCount}) must match the net48 baseline within {Tolerance} rad.");

                // Sanity: the fused heading and offset are always normalized to [0, 2π).
                Assert.That(fused, Is.GreaterThanOrEqualTo(-Tolerance).And.LessThanOrEqualTo(glm.twoPI + Tolerance),
                    $"fused heading (row {rowCount}) must be normalized to [0, 2π).");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "cahrs_fusion.csv contained no data rows.");
        }

        // ===========================================================================================
        // Iterative convergence — the persistent offset carries across calls; fused heading tracks GPS.
        // ===========================================================================================

        /// <summary>
        /// Drives the production fusion repeatedly with a FIXED input (imu=100°, gps=90°, weight=0.06,
        /// forward) so the running offset persists across calls, and asserts each step reproduces the
        /// production-captured convergence golden. This exercises the loop behavior, not a single formula.
        /// </summary>
        [Test]
        public void Fusion_Iterative_MatchesGolden()
        {
            string[] lines = LoadRequiredGoldenLines("cahrs_fusion_iter.csv");

            const double imuHeadingDeg = 100.0;
            double gpsHeadingRad = glm.toRadians(90.0);
            const double fusionWeight = 0.06;

            double offset = 0.0;
            int expectedStep = 1;
            int rowCount = 0;

            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(row.Length, Is.GreaterThanOrEqualTo(3),
                    "cahrs_fusion_iter.csv row must have 3 columns: step,expectedFusedHeadingRad,expectedFinalOffsetRad");

                int step = ParseInt(row[0]);
                double expectedFused = ParseDouble(row[1]);
                double expectedOffset = ParseDouble(row[2]);

                Assert.That(step, Is.EqualTo(expectedStep), "iterative golden steps must be contiguous from 1.");

                double fused = CAHRS.FuseImuGpsHeading(
                    imuHeadingDeg, gpsHeadingRad, fusionWeight, false, ref offset);

                Assert.That(fused, Is.EqualTo(expectedFused).Within(Tolerance),
                    $"production fused heading at step {step} must match the captured convergence golden.");
                Assert.That(offset, Is.EqualTo(expectedOffset).Within(Tolerance),
                    $"production offset at step {step} must match the captured convergence golden.");

                expectedStep++;
                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "cahrs_fusion_iter.csv contained no data rows.");

            // The fused heading must monotonically approach the GPS heading (1.5708 rad) — the whole point
            // of fusion. After 5 steps it is strictly closer to the GPS heading than after 1 step.
            double offsetA = 0.0, offsetB = 0.0;
            double firstFused = CAHRS.FuseImuGpsHeading(imuHeadingDeg, gpsHeadingRad, fusionWeight, false, ref offsetA);
            double fifthFused = firstFused;
            for (int i = 0; i < 4; i++)
                fifthFused = CAHRS.FuseImuGpsHeading(imuHeadingDeg, gpsHeadingRad, fusionWeight, false, ref offsetB);
            Assert.That(
                Math.Abs(fifthFused - gpsHeadingRad),
                Is.LessThan(Math.Abs(firstFused - gpsHeadingRad)),
                "the fused heading must converge toward the GPS heading as the offset advances.");
        }

        // ===========================================================================================
        // Determinism + reverse-weight behavioral properties (no golden needed; always run).
        // ===========================================================================================

        /// <summary>
        /// The production fusion is pure and deterministic: the same inputs and starting offset produce a
        /// BIT-EXACT identical result on repeated invocation (cross-platform float determinism, AAP §0.6.5).
        /// </summary>
        [Test]
        public void Fusion_IsDeterministic()
        {
            double offset1 = 0.0, offset2 = 0.0;
            double f1 = CAHRS.FuseImuGpsHeading(123.456, glm.toRadians(120.0), 0.06, false, ref offset1);
            double f2 = CAHRS.FuseImuGpsHeading(123.456, glm.toRadians(120.0), 0.06, false, ref offset2);

            Assert.That(f2, Is.EqualTo(f1), "fused heading must be bit-exact deterministic.");
            Assert.That(offset2, Is.EqualTo(offset1), "advanced offset must be bit-exact deterministic.");
        }

        /// <summary>
        /// FROZEN reverse behavior: when reversing with the IMU, the offset advances with the fixed 0.02
        /// weight instead of the (larger) forward fusion weight, so for the same heading delta the reverse
        /// offset step is strictly smaller in magnitude than the forward step. Verifies the production
        /// branch selection, exactly as net48.
        /// </summary>
        [Test]
        public void Fusion_ReverseUsesLowerWeight()
        {
            const double imuDeg = 95.0;                 // 5° ahead of GPS
            double gpsRad = glm.toRadians(90.0);
            const double forwardWeight = 0.06;          // > 0.02 reverse weight

            double fwdOffset = 0.0;
            CAHRS.FuseImuGpsHeading(imuDeg, gpsRad, forwardWeight, false, ref fwdOffset);

            double revOffset = 0.0;
            CAHRS.FuseImuGpsHeading(imuDeg, gpsRad, forwardWeight, true, ref revOffset);

            // Both wrap into [0, 2π); compare the signed step as the shortest angular distance from 0.
            double fwdStep = Math.Min(fwdOffset, glm.twoPI - fwdOffset);
            double revStep = Math.Min(revOffset, glm.twoPI - revOffset);

            Assert.That(
                revStep,
                Is.LessThan(fwdStep),
                "the reverse path (fixed 0.02 weight) must advance the offset less than the forward path for the same delta.");
        }
    }
}
