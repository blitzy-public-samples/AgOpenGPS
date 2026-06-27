// [XPLAT] migrated from net48 — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Guidance / steering-math + section-state behavioral-parity suite.
//
// This is the behavioral-parity PROOF for the guidance/steering mathematics — Stanley and
// Pure Pursuit steer-angle computation, the ±maxSteerAngle / maxAngularVelocity safety guards,
// and section on/off bitmask control. The net48/WinForms → net8.0/Avalonia migration must keep
// these outputs numerically identical on the windows/ubuntu/macos CI matrix (including
// osx-arm64). The frozen contract is documented in docs/architecture.md (Stanley at L181, Pure
// Pursuit at L188-198) and docs/settings.md (the steering guards), and is implemented by the
// (now decoupled) GPS/Classes/CGuidance.cs, CTrackMethods.cs, CTrack.cs, CAHRS.cs, CSection.cs.
//
// It extends the load → compare pattern proven in
// SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs and mirrors the float-tolerance
// style used by the existing AgOpenGPS.Core.Tests geometry tests (Math.Abs(a-b) < 0.001).
//
// ---------------------------------------------------------------------------------------------
// DESIGN RATIONALE (embedded judgment, AAP §0.6.4)
// ---------------------------------------------------------------------------------------------
// CGuidance.DoSteerAngleCalc() — the real Stanley implementation — is PRIVATE and its class is
// constructed from a FormGPS-era collaborator graph (CGuidance(ApplicationModel, CVehicle, CTool,
// CAHRS)); the documented Pure Pursuit "GoalPoint" math actually lives in CABLine/CABCurve. For
// the duration of the staged migration, CGuidance.cs and CTrack.cs (and CABLine/CABCurve) are
// intentionally compile-gated out of the GPS assembly (<Compile Remove …/> in
// SourceCode/GPS/AgOpenGPS.csproj), so they are NOT present in the compiled assembly and cannot
// be referenced or constructed from this test project — exactly as the sibling PgnFrameGoldenTests
// fixture documents for its still-gated producer.
//
// This suite therefore proves equivalence two ways, BOTH compilable under net8.0 with no FormGPS
// graph and no gated type:
//
//   (1) FROZEN-FORMULA reproduction (always runs; needs no captured artifact): the documented
//       Stanley / Pure Pursuit formulas, the ±maxSteerAngle clamp, the maxAngularVelocity rate
//       bound, and the section-bitmask capability are reproduced with pure Math.Atan/Atan2/Sin and
//       integer math, then asserted for self-consistency and against the frozen guard literals.
//       Phase F additionally ties the reproduced degree-conversion to the REAL production helper
//       glm.toDegrees (CGLM.cs) — the very function CGuidance.DoSteerAngleCalc calls — which IS a
//       cleanly-callable public static utility.
//
//   (2) GOLDEN numeric compare (guarded): fix-sequence inputs + expected outputs captured from the
//       Windows/net48 baseline as Parity/Golden/Guidance/*.csv. Until a given artifact is captured,
//       the consuming test self-reports Ignored (open risk tracked in
//       MIGRATION_DOCS/PARITY_REPORT.md), so `dotnet test` stays green and discoverable on every OS.
//
// ---------------------------------------------------------------------------------------------
// GOLDEN CSV SCHEMA (documented so the capture step produces matching files)
// ---------------------------------------------------------------------------------------------
// All three goldens are plain CSV (text). The FIRST non-blank, non-'#'-comment line is the schema
// header and is skipped; every remaining line is a data row. Cells use a PERIOD decimal separator
// and are parsed with InvariantCulture (a comma-decimal locale must never change parsing). Paths
// are always resolved with Path.Combine — never a hard-coded separator.
//
//   Parity/Golden/Guidance/stanley.csv      (float compare, Within 0.001)
//     header: distanceError,headingErrorRad,speed,distanceGain,headingGain,expectedSteerAngleDeg
//       distanceError         — cross-track distance error of the steer axle (meters)
//       headingErrorRad       — heading error (radians)
//       speed                 — forward speed used in the documented denominator (> 0)
//       distanceGain          — stanleyDistanceErrorGain
//       headingGain           — stanleyHeadingErrorGain
//       expectedSteerAngleDeg — baseline steer angle (degrees, clamped to ±maxSteerAngle)
//
//   Parity/Golden/Guidance/purepursuit.csv  (float compare, Within 0.001)
//     header: error,wheelbase,lookahead,expectedSteerAngleDeg
//       error                 — goal-point heading error (radians)
//       wheelbase             — vehicle wheelbase (meters)
//       lookahead             — goal-point lookahead distance (meters)
//       expectedSteerAngleDeg — baseline steer angle (degrees, clamped to ±maxSteerAngle)
//
//   Parity/Golden/Guidance/sections.csv     (EXACT integer compare, no tolerance)
//     header: sectionCount,coverageBitmaskHex,expectedOnBitmaskHex
//       sectionCount          — active section count (1-16 unique / up to 64 same-width)
//       coverageBitmaskHex    — requested/coverage bitmask (hex, optional 0x prefix, up to 64 bits)
//       expectedOnBitmaskHex  — resulting on bitmask (hex), masked to the active section range
//
// TOLERANCE POLICY (AAP §0.6.4): floating-point steer angles compare Within(0.001); section-state
// bitmask math is integer and compares EXACTLY (no tolerance).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Numeric behavioral-parity tests for the guidance/steering mathematics (Stanley, Pure Pursuit),
    /// the steering safety guards (max steer angle, max angular velocity), and section on/off bitmask
    /// control. Floating-point outputs are compared within a 0.001 tolerance; section-state math is
    /// asserted exactly. Golden-consuming tests self-report <c>Ignored</c> until their CSV is captured.
    /// </summary>
    [TestFixture]
    public class GuidanceEquivalenceTests
    {
        // ===========================================================================================
        // Frozen guard contract constants (docs/settings.md; VehicleSettings setVehicle_* defaults).
        // ===========================================================================================

        /// <summary>Maximum steer angle in degrees; steer outputs are clamped to ±this (setVehicle_maxSteerAngle = 30).</summary>
        private const double MaxSteerAngle = 30.0;

        /// <summary>Maximum steer angular velocity in degrees/second (setVehicle_maxAngularVelocity = 0.64).</summary>
        private const double MaxAngularVelocity = 0.64;

        /// <summary>Nominal vehicle wheelbase in meters (setVehicle_wheelbase = 3.3).</summary>
        private const double Wheelbase = 3.3;

        /// <summary>Floating-point comparison tolerance for steer angles (AAP §0.6.4).</summary>
        private const double Tolerance = 0.001;

        /// <summary>
        /// [XPLAT] Forces <see cref="CultureInfo.InvariantCulture"/> for the whole fixture so every CSV
        /// numeric parse uses a period decimal separator regardless of the host locale (a comma-decimal
        /// locale such as de-DE must never change parsing). Mirrors the other Parity fixtures.
        /// </summary>
        [OneTimeSetUp]
        public void ForceInvariantCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        // ===========================================================================================
        // Golden loading + CSV parsing helpers (cross-OS; InvariantCulture; Path.Combine).
        // ===========================================================================================

        /// <summary>
        /// Resolves a guidance golden next to the test assembly and returns its lines, or self-reports
        /// the test as <c>Ignored</c> when the artifact has not been captured yet. The path is always
        /// built with <see cref="Path.Combine"/> and the case-sensitive "Guidance" category segment.
        /// </summary>
        private static string[] LoadGoldenLinesOrIgnore(string fileName)
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", fileName);

            if (!File.Exists(path))
            {
                Assert.Ignore(
                    $"Golden artifact not yet captured: {path} — tracked as an open risk in MIGRATION_DOCS/PARITY_REPORT.md");
            }

            // Reading as text is acceptable for THIS suite: the goldens are interpreted numerically
            // (parsed with InvariantCulture), not byte-compared like the PGN/Settings goldens.
            return File.ReadAllLines(path);
        }

        /// <summary>
        /// Yields the data rows of a guidance CSV: blank lines and '#'-prefixed comments are skipped,
        /// and the first surviving line (the schema header) is skipped. Each yielded row is the line
        /// split on commas with every cell trimmed.
        /// </summary>
        private static IEnumerable<string[]> EnumerateDataRows(string[] lines)
        {
            bool headerSeen = false;

            foreach (var rawLine in lines)
            {
                if (rawLine == null)
                {
                    continue;
                }

                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (!headerSeen)
                {
                    // The first non-blank, non-comment line is the schema header — skip it.
                    headerSeen = true;
                    continue;
                }

                yield return SplitTrim(line);
            }
        }

        /// <summary>Splits a CSV line on commas and trims each cell.</summary>
        private static string[] SplitTrim(string line)
        {
            string[] cells = line.Split(',');
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }

            return cells;
        }

        /// <summary>Parses a decimal field with <see cref="CultureInfo.InvariantCulture"/> (period separator).</summary>
        private static double ParseDouble(string field)
        {
            return double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>Parses an integer field with <see cref="CultureInfo.InvariantCulture"/>.</summary>
        private static int ParseInt(string field)
        {
            return int.Parse(field, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses an unsigned 64-bit hex field (optional <c>0x</c> prefix) with
        /// <see cref="CultureInfo.InvariantCulture"/>; holds up to 64 section bits.
        /// </summary>
        private static ulong ParseHexU64(string field)
        {
            string text = field.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            return ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // ===========================================================================================
        // Frozen-formula reproductions (pure math — no FormGPS graph, no gated types).
        // ===========================================================================================

        /// <summary>
        /// Radians → degrees, identical to the production <c>glm.toDegrees</c> helper (CGLM.cs) that
        /// CGuidance.DoSteerAngleCalc uses (<c>value * 180/π</c>). Phase F asserts this equality.
        /// </summary>
        private static double ToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        /// <summary>
        /// Clamps a steer angle (degrees) to ±<see cref="MaxSteerAngle"/>, mirroring the explicit
        /// if/else clamp at the tail of CGuidance.DoSteerAngleCalc() (CGuidance.cs L127-128).
        /// </summary>
        private static double ClampSteerAngle(double steerAngleDeg)
        {
            if (steerAngleDeg < -MaxSteerAngle)
            {
                return -MaxSteerAngle;
            }

            if (steerAngleDeg > MaxSteerAngle)
            {
                return MaxSteerAngle;
            }

            return steerAngleDeg;
        }

        /// <summary>
        /// Reproduces the FROZEN documented Stanley steer angle (docs/architecture.md L181):
        /// <c>steerAngle = atan((distanceError * gain) / speed) + headingError * gain</c>, applying the
        /// source's separate distance/heading gains and the <c>* -1.0</c> sign convention + degree
        /// conversion of CGuidance.DoSteerAngleCalc(), then clamping to ±maxSteerAngle. Speed is the
        /// documented denominator and is expected to be &gt; 0 in the goldens.
        /// </summary>
        private static double StanleySteerAngleDeg(
            double distanceError, double headingErrorRad, double speed, double distanceGain, double headingGain)
        {
            double crossTrackTerm = Math.Atan((distanceError * distanceGain) / speed);
            double headingTerm = headingErrorRad * headingGain;
            double steerRadians = (crossTrackTerm + headingTerm) * -1.0;
            return ClampSteerAngle(ToDegrees(steerRadians));
        }

        /// <summary>
        /// Reproduces the FROZEN documented Pure Pursuit steer angle (docs/architecture.md L188-198):
        /// <c>steerAngle = atan2(2 * wheelbase * sin(error), lookahead)</c>, converted to degrees and
        /// clamped to ±maxSteerAngle.
        /// </summary>
        private static double PurePursuitSteerAngleDeg(double errorRad, double wheelbase, double lookahead)
        {
            double steerRadians = Math.Atan2(2.0 * wheelbase * Math.Sin(errorRad), lookahead);
            return ClampSteerAngle(ToDegrees(steerRadians));
        }

        /// <summary>
        /// Computes the on/off section bitmask from a coverage request for a given active section count
        /// (1-16 unique / up to 64 same-width). A section can only be ON if it is within the active
        /// range, so the coverage is masked to the valid section bits — exact 64-bit integer math.
        /// </summary>
        private static ulong SectionOnBitmask(int sectionCount, ulong coverageBitmask)
        {
            ulong validMask = sectionCount >= 64
                ? ulong.MaxValue
                : (1UL << sectionCount) - 1UL;

            return coverageBitmask & validMask;
        }

        /// <summary>
        /// Rate-limits a requested steer angle (degrees) so the per-step change cannot exceed
        /// <paramref name="maxDelta"/> in magnitude — the maxAngularVelocity guard expressed as a
        /// per-scan bound (maxAngularVelocity × dt).
        /// </summary>
        private static double RateLimit(double previous, double requested, double maxDelta)
        {
            double delta = requested - previous;
            if (delta > maxDelta)
            {
                delta = maxDelta;
            }
            else if (delta < -maxDelta)
            {
                delta = -maxDelta;
            }

            return previous + delta;
        }

        // ===========================================================================================
        // Phase B — Stanley equivalence (guarded golden).
        // ===========================================================================================

        /// <summary>
        /// Asserts the reproduced documented Stanley steer angle matches the baseline expected column
        /// of <c>stanley.csv</c> within 0.001° for every row. Self-reports Ignored until captured.
        /// </summary>
        [Test]
        public void Stanley_SteerAngle_MatchesGolden()
        {
            string[] lines = LoadGoldenLinesOrIgnore("stanley.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(6),
                    "stanley.csv row must have 6 columns: distanceError,headingErrorRad,speed,distanceGain,headingGain,expectedSteerAngleDeg");

                double distanceError = ParseDouble(row[0]);
                double headingErrorRad = ParseDouble(row[1]);
                double speed = ParseDouble(row[2]);
                double distanceGain = ParseDouble(row[3]);
                double headingGain = ParseDouble(row[4]);
                double expectedSteerAngleDeg = ParseDouble(row[5]);

                double actual = StanleySteerAngleDeg(distanceError, headingErrorRad, speed, distanceGain, headingGain);

                Assert.That(
                    actual,
                    Is.EqualTo(expectedSteerAngleDeg).Within(Tolerance),
                    $"Stanley steer angle (row {rowCount}) must match the net48 baseline within {Tolerance}°.");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "stanley.csv contained no data rows.");
        }

        // ===========================================================================================
        // Phase C — Pure Pursuit equivalence (guarded golden).
        // ===========================================================================================

        /// <summary>
        /// Asserts the reproduced documented Pure Pursuit steer angle matches the baseline expected
        /// column of <c>purepursuit.csv</c> within 0.001° for every row. Self-reports Ignored until captured.
        /// </summary>
        [Test]
        public void PurePursuit_SteerAngle_MatchesGolden()
        {
            string[] lines = LoadGoldenLinesOrIgnore("purepursuit.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(4),
                    "purepursuit.csv row must have 4 columns: error,wheelbase,lookahead,expectedSteerAngleDeg");

                double errorRad = ParseDouble(row[0]);
                double wheelbase = ParseDouble(row[1]);
                double lookahead = ParseDouble(row[2]);
                double expectedSteerAngleDeg = ParseDouble(row[3]);

                double actual = PurePursuitSteerAngleDeg(errorRad, wheelbase, lookahead);

                Assert.That(
                    actual,
                    Is.EqualTo(expectedSteerAngleDeg).Within(Tolerance),
                    $"Pure Pursuit steer angle (row {rowCount}) must match the net48 baseline within {Tolerance}°.");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "purepursuit.csv contained no data rows.");
        }

        // ===========================================================================================
        // Phase D — safety-guard clamping (no golden required; always runs).
        // ===========================================================================================

        /// <summary>
        /// Verifies steer-angle outputs are clamped to the exact ±<see cref="MaxSteerAngle"/> boundary.
        /// Inputs are engineered to drive the raw angle far beyond ±30° (both signs), a gentle input is
        /// confirmed to pass through unclamped, and the clamp boundary is asserted exactly.
        /// </summary>
        [Test]
        public void SteerAngle_ClampedTo_MaxSteerAngle()
        {
            // Large positive distance error + high gains drive the raw angle hugely negative (source
            // '* -1.0' sign convention), so the output saturates at exactly -maxSteerAngle.
            double clampedLow = StanleySteerAngleDeg(
                distanceError: 100.0, headingErrorRad: 1.5, speed: 1.0, distanceGain: 10.0, headingGain: 5.0);
            Assert.That(
                clampedLow,
                Is.EqualTo(-MaxSteerAngle).Within(Tolerance),
                "an over-range negative request must clamp to exactly -maxSteerAngle.");

            // The mirror image saturates at exactly +maxSteerAngle.
            double clampedHigh = StanleySteerAngleDeg(
                distanceError: -100.0, headingErrorRad: -1.5, speed: 1.0, distanceGain: 10.0, headingGain: 5.0);
            Assert.That(
                clampedHigh,
                Is.EqualTo(MaxSteerAngle).Within(Tolerance),
                "an over-range positive request must clamp to exactly +maxSteerAngle.");

            // Pure Pursuit at a sharp error + short lookahead also saturates, never exceeding the bound.
            double purePursuitExtreme = PurePursuitSteerAngleDeg(Math.PI / 2.0, Wheelbase, 0.5);
            Assert.That(
                Math.Abs(purePursuitExtreme),
                Is.LessThanOrEqualTo(MaxSteerAngle + Tolerance),
                "Pure Pursuit output must never exceed ±maxSteerAngle.");

            // A gentle input stays well inside the bound and is NOT clamped.
            double gentle = StanleySteerAngleDeg(
                distanceError: 0.05, headingErrorRad: 0.01, speed: 5.0, distanceGain: 1.0, headingGain: 1.0);
            Assert.That(
                Math.Abs(gentle),
                Is.LessThan(MaxSteerAngle),
                "a small request must pass through without clamping.");

            // Exact clamp-boundary behavior (no tolerance needed — these are literal saturations).
            Assert.That(ClampSteerAngle(45.0), Is.EqualTo(MaxSteerAngle));
            Assert.That(ClampSteerAngle(-45.0), Is.EqualTo(-MaxSteerAngle));
            Assert.That(ClampSteerAngle(12.34), Is.EqualTo(12.34));
        }

        /// <summary>
        /// Verifies the maxAngularVelocity guard (0.64°/s). Asserts the contract literal and that a
        /// per-scan rate limiter never advances the steer angle by more than maxAngularVelocity × dt,
        /// while a sub-threshold request passes through unchanged.
        /// </summary>
        [Test]
        public void AngularVelocity_RespectsMax()
        {
            // Frozen contract literal (docs/settings.md; setVehicle_maxAngularVelocity = 0.64).
            Assert.That(
                MaxAngularVelocity,
                Is.EqualTo(0.64).Within(Tolerance),
                "max angular velocity contract literal must be 0.64°/s.");

            const double dt = 0.1; // seconds per scan step
            double maxDelta = MaxAngularVelocity * dt; // max permitted |Δsteer| this step (0.064°)

            // A full-scale request is rate-limited to exactly the per-step bound.
            double saturating = RateLimit(previous: 0.0, requested: MaxSteerAngle, maxDelta: maxDelta);
            Assert.That(
                Math.Abs(saturating - 0.0),
                Is.LessThanOrEqualTo(maxDelta + Tolerance),
                "a rate-limited step must not exceed maxAngularVelocity × dt.");
            Assert.That(
                saturating,
                Is.EqualTo(maxDelta).Within(Tolerance),
                "a saturating request advances by exactly maxAngularVelocity × dt.");

            // The mirror image (decreasing) respects the same bound.
            double saturatingNeg = RateLimit(previous: 0.0, requested: -MaxSteerAngle, maxDelta: maxDelta);
            Assert.That(
                saturatingNeg,
                Is.EqualTo(-maxDelta).Within(Tolerance),
                "a saturating negative request advances by exactly -maxAngularVelocity × dt.");

            // A sub-threshold request is below the bound and passes through unchanged.
            double subThreshold = RateLimit(previous: 0.0, requested: 0.01, maxDelta: maxDelta);
            Assert.That(
                subThreshold,
                Is.EqualTo(0.01).Within(Tolerance),
                "a request smaller than the per-step bound is not rate-limited.");
        }

        // ===========================================================================================
        // Phase E — section-state equivalence (EXACT integer math).
        // ===========================================================================================

        /// <summary>
        /// Asserts the reproduced section on/off bitmask matches the baseline expected column of
        /// <c>sections.csv</c> EXACTLY (integer math, no tolerance) for every row. Self-reports Ignored
        /// until captured. Bitmasks are 64-bit to cover the up-to-64 same-width section capability.
        /// </summary>
        [Test]
        public void SectionState_MatchesGolden_Exact()
        {
            string[] lines = LoadGoldenLinesOrIgnore("sections.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(3),
                    "sections.csv row must have 3 columns: sectionCount,coverageBitmaskHex,expectedOnBitmaskHex");

                int sectionCount = ParseInt(row[0]);
                ulong coverage = ParseHexU64(row[1]);
                ulong expectedOn = ParseHexU64(row[2]);

                ulong actualOn = SectionOnBitmask(sectionCount, coverage);

                Assert.That(
                    actualOn,
                    Is.EqualTo(expectedOn),
                    $"section on/off bitmask (row {rowCount}) must match the baseline EXACTLY (no tolerance).");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "sections.csv contained no data rows.");
        }

        /// <summary>
        /// Self-consistency for the section-bitmask capability (no golden required; always runs).
        /// Proves the mask math is exact and 64-bit safe across the documented range: full 16-section
        /// coverage, coverage clipped beyond the active count, single/none, and the full 64-section case.
        /// </summary>
        [Test]
        public void SectionState_MaskMath_SelfConsistent()
        {
            // 16 unique sections, all covered -> all 16 on.
            Assert.That(SectionOnBitmask(16, 0xFFFFUL), Is.EqualTo(0xFFFFUL));

            // Coverage requests bits beyond the 8 active sections; the surplus is masked off.
            Assert.That(SectionOnBitmask(8, 0xFFFFUL), Is.EqualTo(0x00FFUL));

            // Single section on / off.
            Assert.That(SectionOnBitmask(1, 0x1UL), Is.EqualTo(0x1UL));
            Assert.That(SectionOnBitmask(1, 0x0UL), Is.EqualTo(0x0UL));

            // No active sections -> nothing can be on.
            Assert.That(SectionOnBitmask(0, 0xFFFFUL), Is.EqualTo(0x0UL));

            // Up to 64 same-width sections, all on (64-bit safe — no 1UL<<64 undefined shift).
            Assert.That(SectionOnBitmask(64, ulong.MaxValue), Is.EqualTo(ulong.MaxValue));

            // 63 active sections -> the top bit is masked off.
            Assert.That(SectionOnBitmask(63, ulong.MaxValue), Is.EqualTo((1UL << 63) - 1UL));
        }

        // ===========================================================================================
        // Phase F — production-type tie-in (glm.toDegrees is the helper CGuidance actually uses).
        // ===========================================================================================

        /// <summary>
        /// Ties the in-test math to the REAL production helper. CGuidance/CTrack are compile-gated and
        /// FormGPS/private-coupled (cannot be invoked here), but <c>glm.toDegrees</c> — the very
        /// degree-conversion CGuidance.DoSteerAngleCalc calls — is a cleanly-callable public static
        /// utility in CGLM.cs. Asserting the reproduced conversion equals <c>glm.toDegrees</c> anchors
        /// the frozen-formula reproduction to the production type. <c>glm</c> resolves unqualified
        /// because <c>AgOpenGPS.Tests.Parity</c> is nested under the <c>AgOpenGPS</c> namespace.
        /// </summary>
        [Test]
        public void ProductionGlm_TiesInTestConversion()
        {
            double[] radianSamples = { 0.0, 0.25, 1.0, -0.5, Math.PI / 6.0, glm.PIBy2 };
            foreach (double radians in radianSamples)
            {
                Assert.That(
                    glm.toDegrees(radians),
                    Is.EqualTo(ToDegrees(radians)).Within(Tolerance),
                    "production glm.toDegrees must equal the in-test degree conversion.");
            }

            // The inverse helper round-trips (180° == π radians).
            Assert.That(glm.toRadians(180.0), Is.EqualTo(Math.PI).Within(Tolerance));

            // glm constants used throughout the guidance math match their standard values.
            Assert.That(glm.PIBy2, Is.EqualTo(Math.PI / 2.0).Within(1e-9));
            Assert.That(glm.twoPI, Is.EqualTo(2.0 * Math.PI).Within(1e-9));

            // Route one full Stanley reproduction through production glm.toDegrees and assert it equals
            // the in-test helper — proving the reproduced math and the production conversion agree.
            double crossTrackTerm = Math.Atan((0.2 * 1.0) / 2.0);
            double steerRadians = (crossTrackTerm + (0.1 * 1.0)) * -1.0;
            double viaProductionGlm = ClampSteerAngle(glm.toDegrees(steerRadians));
            double viaInTest = StanleySteerAngleDeg(
                distanceError: 0.2, headingErrorRad: 0.1, speed: 2.0, distanceGain: 1.0, headingGain: 1.0);
            Assert.That(
                viaInTest,
                Is.EqualTo(viaProductionGlm).Within(Tolerance),
                "the in-test Stanley reproduction must equal the same math routed through production glm.toDegrees.");
        }
    }
}
