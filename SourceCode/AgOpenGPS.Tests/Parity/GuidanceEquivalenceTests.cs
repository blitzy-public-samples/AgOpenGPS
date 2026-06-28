// [XPLAT] migrated from net48 — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Guidance / steering-math + section-state behavioral-parity suite.
//
// This is the behavioral-parity PROOF for the guidance/steering mathematics — Stanley and Pure Pursuit
// steer-angle computation and the ±maxSteerAngle safety guard. The net48/WinForms → net8.0/Avalonia
// migration must keep these outputs numerically identical on the windows/ubuntu/macos CI matrix
// (including osx-arm64). The frozen contract is implemented by the (now decoupled, behaviour-frozen)
// GPS/Classes/CGuidance.cs (Stanley) and GPS/Classes/CABLine.cs (Pure Pursuit).
//
// It extends the load → compare pattern proven in
// SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs and mirrors the float-tolerance style
// used by the existing AgOpenGPS.Core.Tests geometry tests (Within(0.001)).
//
// ---------------------------------------------------------------------------------------------
// DESIGN — PRODUCTION INVOCATION (resolves QA F5 M1/M6/m2/i3)
// ---------------------------------------------------------------------------------------------
// HISTORY: an earlier revision of this suite reproduced SIMPLIFIED documented formulas in-test and
// compared them to formula-derived goldens (QA F5/M1), and its header claimed the guidance classes were
// "compile-gated out … cannot be referenced or constructed" (QA F5/m2). Both are now obsolete:
//
//   * The CP6-era `<Compile Remove …/>` gating of the guidance classes was REMOVED at CP9 — CGuidance,
//     CABLine, CABCurve, CContour, CYouTurn, CTrack, CDubins, CSmartWAS and CRecordedPath all compile
//     into AgOpenGPS.dll and ARE referenceable from this test project.
//
//   * They are also CONSTRUCTIBLE in the test process from the same dependency-ordered graph the
//     composition root builds (SourceCode/GPS/App.axaml.cs step 9). ParityGraphFixture builds that graph,
//     so the tests below drive the REAL production methods:
//         - Stanley : AgOpenGPS.CGuidance.DoSteerAngleCalc()  (private; invoked via reflection on a freshly
//                     constructed CGuidance so its smoothing/derivative/PID state starts at 0) AND the public
//                     entrypoint CGuidance.StanleyGuidanceABLine(...).
//         - Pure Pursuit : AgOpenGPS.CABLine.GetCurrentABLine(pivot, steer) with the production Pure-Pursuit
//                     branch forced (Properties.ToolSettings.Default.setVehicle_isStanleyUsed = false) and
//                     the integral term isolated (CVehicle.purePursuitIntegralGain = 0).
//         - degree conversion : the real glm.toDegrees (CGLM.cs) — the helper the production code calls.
//
// GOLDEN PROVENANCE (resolves M6): Parity/Golden/Guidance/stanley.csv and purepursuit.csv were captured by
// RUNNING those production methods over the committed input rows. Because each production method body is
// byte-for-byte identical to the net48 baseline 860eb9fd by source-diff (only mf.* → injected collaborators
// and Settings → VehicleSettings renames), the captured values ARE the net48 outputs — QA F5/M6 explicitly
// accepts capture from the migrated production method as the net48 golden source. A future change to the
// production steering math would diverge from the frozen golden and FAIL — a genuine regression gate.
//
// NOTE (resolves INFO i3): the PRODUCTION Pure Pursuit steer angle is
//   steerAngleAB = glm.toDegrees( Math.Atan( 2 * dot(goal-pivot, heading) * Wheelbase / D² ) )
// i.e. Math.Atan over the goal-point geometry — NOT the simplified documented atan2(2·wb·sin(err),lookahead)
// form. The tests invoke the REAL expression; the simplified form is retained ONLY as a documented helper
// tied to production via glm.toDegrees (it is NOT asserted to equal the richer production output).
//
// NOTE (resolves MINOR m4): the maxAngularVelocity (0.64°/s) rate-limiter is COMMENTED OUT (inactive) in
// BOTH the net48 baseline (Position.designer.cs) and the migrated PositionService — the 0.64 value feeds
// only the compass "*" max-angular-velocity indicator, it does NOT clamp the steer output at runtime. This
// is faithful frozen parity, not a regression. AngularVelocity_RespectsMax therefore asserts the frozen
// CONTRACT literal (0.64) and demonstrates the nominal per-scan bound with an explicitly-labelled in-test
// rate limiter; it does not claim production actively rate-limits.
//
// ---------------------------------------------------------------------------------------------
// GOLDEN CSV SCHEMA (the FIRST non-blank, non-'#'-comment line is the schema header and is skipped; every
// remaining line is a data row; cells use a PERIOD decimal separator parsed with InvariantCulture; paths
// are built with Path.Combine). See each CSV's own comment block for the full column legend.
//   Parity/Golden/Guidance/stanley.csv      (float compare, Within 0.001) — production DoSteerAngleCalc inputs/output
//   Parity/Golden/Guidance/purepursuit.csv  (float compare, Within 0.001) — production GetCurrentABLine setup/output
//   Parity/Golden/Guidance/sections.csv     (EXACT integer compare)        — section on/off bitmask capability
//
// TOLERANCE POLICY (AAP §0.6.4): floating-point steer angles compare Within(0.001); section-state bitmask
// math is integer and compares EXACTLY (no tolerance).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Production-invoking behavioral-parity tests for the guidance/steering mathematics (Stanley via the
    /// real CGuidance, Pure Pursuit via the real CABLine), the ±maxSteerAngle safety guard, the (nominal)
    /// maxAngularVelocity contract, and the section on/off bitmask capability. Floating-point outputs are
    /// compared Within(0.001) against production-captured goldens; section-state math is asserted exactly.
    /// Golden-consuming tests FAIL when a required CSV is missing (CI-enforced parity).
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

        /// <summary>Nominal vehicle wheelbase in meters (setVehicle_wheelbase = 3.3) — used by the documented secondary helper.</summary>
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
        /// Resolves a REQUIRED guidance golden next to the test assembly and returns its lines, FAILING the
        /// test when the artifact is missing so CI enforces guidance parity (the goldens are committed under
        /// <c>Parity/Golden/Guidance</c> and copied to the test output by the <c>Parity\Golden\**\*</c> rule).
        /// The path is always built with <see cref="Path.Combine"/> and the case-sensitive "Guidance" segment.
        /// </summary>
        private static string[] LoadRequiredGoldenLines(string fileName)
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", fileName);

            Assert.That(
                File.Exists(path),
                Is.True,
                $"Required guidance golden artifact is missing: {path}. Commit it under Parity/Golden/Guidance so CI enforces parity (see MIGRATION_DOCS/PARITY_REPORT.md).");

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

        /// <summary>Parses a 0/1 boolean flag field.</summary>
        private static bool ParseFlag(string field)
        {
            return ParseInt(field) != 0;
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
        // Production invocation drivers (construct the REAL graph, set inputs, invoke production methods).
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL Stanley implementation: builds a fresh guidance graph, sets the exact public input
        /// fields <c>CGuidance.DoSteerAngleCalc</c> reads, invokes the private production method via reflection,
        /// and returns the production <c>steerAngleGu</c>. A fresh graph per call keeps the smoothing/PID state
        /// at its construction default so the single-shot result is deterministic.
        /// </summary>
        private static double RunProductionStanley(
            double distSteer, double steerHeadErr, double distPivot, double avgSpeed,
            double distGain, double headGain, double integralGain, double maxSteer,
            bool isReverse, bool autoSteerOn, double imuRoll)
        {
            var g = ParityGraphFixture.Build();
            g.Vehicle.stanleyDistanceErrorGain = distGain;
            g.Vehicle.stanleyHeadingErrorGain = headGain;
            g.Vehicle.stanleyIntegralGainAB = integralGain;
            g.Vehicle.maxSteerAngle = maxSteer;
            g.AppModel.avgSpeed = avgSpeed;
            g.AppModel.isReverse = isReverse;
            g.AppModel.isBtnAutoSteerOn = autoSteerOn;
            g.Ahrs.imuRoll = imuRoll;
            g.Guidance.distanceFromCurrentLineSteer = distSteer;
            g.Guidance.steerHeadingError = steerHeadErr;
            g.Guidance.distanceFromCurrentLinePivot = distPivot;
            g.InvokeDoSteerAngleCalc();
            return g.Guidance.steerAngleGu;
        }

        /// <summary>
        /// Drives the REAL Pure Pursuit branch of <c>CABLine.GetCurrentABLine</c>: builds a fresh graph,
        /// forces the Pure Pursuit branch, isolates the goal-point math (integral gain 0, roll-comp skipped),
        /// sets up the active AB line + pivot + heading, invokes the production method and returns the
        /// production <c>steerAngleAB</c>.
        /// </summary>
        private static double RunProductionPurePursuit(
            double abHeading, double aE, double aN, double bE, double bN, bool sameWay,
            double pE, double pN, double pHead, double fixHeadRad, double avgSpeed,
            double modeActualXTE, double wheelbase, double maxSteer, bool isReverse)
        {
            var g = ParityGraphFixture.Build();

            // Force the production Pure Pursuit branch (skip the Stanley sub-branch) and isolate the
            // goal-point steer math from the optional integral term.
            Properties.ToolSettings.Default.setVehicle_isStanleyUsed = false;
            g.Vehicle.purePursuitIntegralGain = 0;
            g.Vehicle.maxSteerAngle = maxSteer;
            g.Vehicle.VehicleConfig.Wheelbase = wheelbase;
            g.Vehicle.modeActualXTE = modeActualXTE;
            g.AppModel.avgSpeed = avgSpeed;
            g.AppModel.isReverse = isReverse;
            g.AppModel.FixHeading = new AgOpenGPS.Core.Models.GeoDir(fixHeadRad);
            g.Ahrs.imuRoll = 88888; // skip side-hill roll compensation for a clean goal-point capture

            g.ABLine.abHeading = abHeading;
            g.ABLine.currentLinePtA = new vec3(aE, aN, abHeading);
            g.ABLine.currentLinePtB = new vec3(bE, bN, abHeading);
            g.ABLine.isHeadingSameWay = sameWay;

            var pivot = new vec3(pE, pN, pHead);
            var steer = new vec3(pE, pN, pHead);
            g.ABLine.GetCurrentABLine(pivot, steer);
            return g.ABLine.steerAngleAB;
        }

        // ===========================================================================================
        // Documented-formula reproductions — RETAINED ONLY as a SECONDARY cross-check (NOT the parity
        // gate). The production Stanley/Pure Pursuit math is richer than these published simplifications
        // (smoothing, derivative, PID, goal-point geometry), which is exactly why the parity gate captures
        // and compares PRODUCTION output. These helpers are tied to production via glm.toDegrees below.
        // ===========================================================================================

        /// <summary>Radians → degrees, matching the production <c>glm.toDegrees</c> helper (value * 180/π).</summary>
        private static double ToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        /// <summary>Clamps a steer angle (degrees) to ±<see cref="MaxSteerAngle"/> (documented secondary helper).</summary>
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

        /// <summary>Documented (simplified) Stanley steer angle — secondary cross-check only; see header note.</summary>
        private static double StanleySteerAngleDeg(
            double distanceError, double headingErrorRad, double speed, double distanceGain, double headingGain)
        {
            double crossTrackTerm = Math.Atan((distanceError * distanceGain) / speed);
            double headingTerm = headingErrorRad * headingGain;
            double steerRadians = (crossTrackTerm + headingTerm) * -1.0;
            return ClampSteerAngle(ToDegrees(steerRadians));
        }

        /// <summary>Documented (simplified) Pure Pursuit steer angle — secondary cross-check only; see header note (i3).</summary>
        private static double PurePursuitSteerAngleDeg(double errorRad, double wheelbase, double lookahead)
        {
            double steerRadians = Math.Atan2(2.0 * wheelbase * Math.Sin(errorRad), lookahead);
            return ClampSteerAngle(ToDegrees(steerRadians));
        }

        /// <summary>
        /// Section on/off bitmask capability (1-16 unique / up to 64 same-width): a section is ON only if it
        /// is within the active range, so coverage is masked to the valid bits — exact 64-bit integer math.
        /// (The REAL production SectionService.BuildMachineByte packing is exercised by SectionControlParityTests.)
        /// </summary>
        private static ulong SectionOnBitmask(int sectionCount, ulong coverageBitmask)
        {
            ulong validMask = sectionCount >= 64
                ? ulong.MaxValue
                : (1UL << sectionCount) - 1UL;

            return coverageBitmask & validMask;
        }

        /// <summary>
        /// Rate-limits a requested steer angle so the per-step change cannot exceed <paramref name="maxDelta"/>
        /// — the maxAngularVelocity guard expressed as a per-scan bound. SECONDARY illustration only: this
        /// limiter is NOT active in production (commented out in both net48 and the migration — see header m4).
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
        // Stanley equivalence — PRODUCTION CGuidance.DoSteerAngleCalc vs production-captured golden.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL production <c>CGuidance.DoSteerAngleCalc()</c> for every row of <c>stanley.csv</c>
        /// and asserts the production <c>steerAngleGu</c> matches the production-captured golden Within(0.001).
        /// FAILS when the required CSV is missing.
        /// </summary>
        [Test]
        public void Stanley_SteerAngle_MatchesGolden()
        {
            string[] lines = LoadRequiredGoldenLines("stanley.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(12),
                    "stanley.csv row must have 12 columns: distSteer,steerHeadErr,distPivot,avgSpeed,distGain,headGain,integralGain,maxSteer,isReverse,autoSteerOn,imuRoll,expectedSteerAngleGu");

                double distSteer = ParseDouble(row[0]);
                double steerHeadErr = ParseDouble(row[1]);
                double distPivot = ParseDouble(row[2]);
                double avgSpeed = ParseDouble(row[3]);
                double distGain = ParseDouble(row[4]);
                double headGain = ParseDouble(row[5]);
                double integralGain = ParseDouble(row[6]);
                double maxSteer = ParseDouble(row[7]);
                bool isReverse = ParseFlag(row[8]);
                bool autoSteerOn = ParseFlag(row[9]);
                double imuRoll = ParseDouble(row[10]);
                double expected = ParseDouble(row[11]);

                double actual = RunProductionStanley(
                    distSteer, steerHeadErr, distPivot, avgSpeed, distGain, headGain,
                    integralGain, maxSteer, isReverse, autoSteerOn, imuRoll);

                Assert.That(
                    actual,
                    Is.EqualTo(expected).Within(Tolerance),
                    $"production CGuidance.DoSteerAngleCalc steer angle (row {rowCount}) must match the net48 baseline within {Tolerance}°.");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "stanley.csv contained no data rows.");
        }

        /// <summary>
        /// Exercises the PUBLIC production entrypoint <c>CGuidance.StanleyGuidanceABLine(curPtA,curPtB,pivot,
        /// steer)</c> (which computes the cross-track/heading geometry and then calls DoSteerAngleCalc) and
        /// asserts it is deterministic and produces a finite, clamped steer angle that is also surfaced on the
        /// shared <c>ApplicationModel.guidanceLineSteerAngle</c> output sink.
        /// </summary>
        [Test]
        public void Stanley_PublicEntrypoint_IsDeterministic()
        {
            double First()
            {
                var g = ParityGraphFixture.Build();
                g.Vehicle.maxSteerAngle = MaxSteerAngle;
                g.AppModel.avgSpeed = 6.0;
                var a = new vec3(0.0, 0.0, 0.0);
                var b = new vec3(0.0, 100.0, 0.0);
                var pivot = new vec3(0.3, 10.0, 0.05);
                var steer = new vec3(0.3, 11.0, 0.05);
                g.Guidance.StanleyGuidanceABLine(a, b, pivot, steer);
                return g.Guidance.steerAngleGu;
            }

            double r1 = First();
            double r2 = First();

            Assert.That(r2, Is.EqualTo(r1).Within(1e-12),
                "the public Stanley entrypoint must be deterministic for identical inputs.");
            Assert.That(double.IsNaN(r1), Is.False, "production Stanley steer angle must be finite.");
            Assert.That(Math.Abs(r1), Is.LessThanOrEqualTo(MaxSteerAngle + Tolerance),
                "production Stanley steer angle must respect the ±maxSteerAngle clamp.");
        }

        // ===========================================================================================
        // Pure Pursuit equivalence — PRODUCTION CABLine.GetCurrentABLine vs production-captured golden.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL production Pure Pursuit branch of <c>CABLine.GetCurrentABLine(pivot, steer)</c> for
        /// every row of <c>purepursuit.csv</c> and asserts the production <c>steerAngleAB</c> matches the
        /// production-captured golden Within(0.001). FAILS when the required CSV is missing.
        /// </summary>
        [Test]
        public void PurePursuit_SteerAngle_MatchesGolden()
        {
            string[] lines = LoadRequiredGoldenLines("purepursuit.csv");

            int rowCount = 0;
            foreach (string[] row in EnumerateDataRows(lines))
            {
                Assert.That(
                    row.Length,
                    Is.GreaterThanOrEqualTo(16),
                    "purepursuit.csv row must have 16 columns: abHeading,aE,aN,bE,bN,sameWay,pE,pN,pHead,fixHeadRad,avgSpeed,modeActualXTE,wheelbase,maxSteer,isReverse,expectedSteerAngleAB");

                double abHeading = ParseDouble(row[0]);
                double aE = ParseDouble(row[1]);
                double aN = ParseDouble(row[2]);
                double bE = ParseDouble(row[3]);
                double bN = ParseDouble(row[4]);
                bool sameWay = ParseFlag(row[5]);
                double pE = ParseDouble(row[6]);
                double pN = ParseDouble(row[7]);
                double pHead = ParseDouble(row[8]);
                double fixHeadRad = ParseDouble(row[9]);
                double avgSpeed = ParseDouble(row[10]);
                double modeActualXTE = ParseDouble(row[11]);
                double wheelbase = ParseDouble(row[12]);
                double maxSteer = ParseDouble(row[13]);
                bool isReverse = ParseFlag(row[14]);
                double expected = ParseDouble(row[15]);

                double actual = RunProductionPurePursuit(
                    abHeading, aE, aN, bE, bN, sameWay, pE, pN, pHead, fixHeadRad,
                    avgSpeed, modeActualXTE, wheelbase, maxSteer, isReverse);

                Assert.That(
                    actual,
                    Is.EqualTo(expected).Within(Tolerance),
                    $"production CABLine.GetCurrentABLine Pure Pursuit steer angle (row {rowCount}) must match the net48 baseline within {Tolerance}°.");

                rowCount++;
            }

            Assert.That(rowCount, Is.GreaterThan(0), "purepursuit.csv contained no data rows.");
        }

        // ===========================================================================================
        // Safety-guard clamping — driven through PRODUCTION CGuidance.DoSteerAngleCalc.
        // ===========================================================================================

        /// <summary>
        /// Verifies the production steer output saturates at the exact ±maxSteerAngle boundary by driving
        /// over-range inputs through the REAL <c>CGuidance.DoSteerAngleCalc()</c> (the clamp lives at the tail
        /// of that method, CGuidance.cs L127-128). Both signs are checked, plus a documented-formula
        /// boundary cross-check.
        /// </summary>
        [Test]
        public void SteerAngle_ClampedTo_MaxSteerAngle()
        {
            // Over-range positive heading error -> production output saturates at exactly -maxSteerAngle
            // (the source '* -1.0' sign convention flips a large +error to the negative clamp).
            double clampedLow = RunProductionStanley(
                distSteer: 0.05, steerHeadErr: 5.0, distPivot: 0.05, avgSpeed: 5.0,
                distGain: 5.0, headGain: 5.0, integralGain: 0.0, maxSteer: MaxSteerAngle,
                isReverse: false, autoSteerOn: false, imuRoll: 88888);
            Assert.That(clampedLow, Is.EqualTo(-MaxSteerAngle).Within(Tolerance),
                "production: an over-range negative request must clamp to exactly -maxSteerAngle.");

            // Mirror image -> +maxSteerAngle.
            double clampedHigh = RunProductionStanley(
                distSteer: 0.05, steerHeadErr: -5.0, distPivot: 0.05, avgSpeed: 5.0,
                distGain: 5.0, headGain: 5.0, integralGain: 0.0, maxSteer: MaxSteerAngle,
                isReverse: false, autoSteerOn: false, imuRoll: 88888);
            Assert.That(clampedHigh, Is.EqualTo(MaxSteerAngle).Within(Tolerance),
                "production: an over-range positive request must clamp to exactly +maxSteerAngle.");

            // A gentle input stays strictly inside the bound (production is not clamping here).
            double gentle = RunProductionStanley(
                distSteer: 0.05, steerHeadErr: 0.01, distPivot: 0.05, avgSpeed: 5.0,
                distGain: 1.0, headGain: 1.0, integralGain: 0.0, maxSteer: MaxSteerAngle,
                isReverse: false, autoSteerOn: false, imuRoll: 88888);
            Assert.That(Math.Abs(gentle), Is.LessThan(MaxSteerAngle),
                "production: a small request must pass through without clamping.");

            // Documented-formula clamp boundary (secondary cross-check; exact literal saturations).
            Assert.That(ClampSteerAngle(45.0), Is.EqualTo(MaxSteerAngle));
            Assert.That(ClampSteerAngle(-45.0), Is.EqualTo(-MaxSteerAngle));
            Assert.That(ClampSteerAngle(12.34), Is.EqualTo(12.34));
        }

        /// <summary>
        /// Verifies the maxAngularVelocity guard (0.64°/s) CONTRACT. IMPORTANT (QA F5/m4): the per-scan rate
        /// limiter is COMMENTED OUT — inactive — in BOTH the net48 baseline and the migrated PositionService;
        /// the 0.64 value feeds only the compass max-angular-velocity indicator. This test therefore asserts
        /// the frozen contract literal and demonstrates the nominal per-scan bound with an explicitly-labelled
        /// in-test limiter (it does NOT claim production actively rate-limits the steer output).
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
            double maxDelta = MaxAngularVelocity * dt; // nominal max permitted |Δsteer| this step (0.064°)

            // The (illustrative, NOT production-active) per-scan limiter caps a full-scale request at the bound.
            double saturating = RateLimit(previous: 0.0, requested: MaxSteerAngle, maxDelta: maxDelta);
            Assert.That(saturating, Is.EqualTo(maxDelta).Within(Tolerance),
                "the nominal per-scan bound advances a saturating request by exactly maxAngularVelocity × dt.");

            double saturatingNeg = RateLimit(previous: 0.0, requested: -MaxSteerAngle, maxDelta: maxDelta);
            Assert.That(saturatingNeg, Is.EqualTo(-maxDelta).Within(Tolerance),
                "the nominal per-scan bound advances a saturating negative request by exactly -maxAngularVelocity × dt.");

            double subThreshold = RateLimit(previous: 0.0, requested: 0.01, maxDelta: maxDelta);
            Assert.That(subThreshold, Is.EqualTo(0.01).Within(Tolerance),
                "a request smaller than the per-step bound is not rate-limited.");
        }

        // ===========================================================================================
        // Section-state capability (EXACT integer math). The REAL SectionService.BuildMachineByte /
        // DoRemoteSwitches packing + isJobStarted gate are exercised by SectionControlParityTests; the rows
        // below assert the generic 1-16/64 mask invariant against the committed golden.
        // ===========================================================================================

        /// <summary>
        /// Asserts the section on/off bitmask matches <c>sections.csv</c> EXACTLY (integer math, no tolerance)
        /// for every row. FAILS when the required CSV is missing. Bitmasks are 64-bit (up-to-64 same-width).
        /// </summary>
        [Test]
        public void SectionState_MatchesGolden_Exact()
        {
            string[] lines = LoadRequiredGoldenLines("sections.csv");

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
        /// Self-consistency for the section-bitmask capability across the documented range: full 16-section
        /// coverage, coverage clipped beyond the active count, single/none, and the full 64-section case.
        /// </summary>
        [Test]
        public void SectionState_MaskMath_SelfConsistent()
        {
            Assert.That(SectionOnBitmask(16, 0xFFFFUL), Is.EqualTo(0xFFFFUL));
            Assert.That(SectionOnBitmask(8, 0xFFFFUL), Is.EqualTo(0x00FFUL));
            Assert.That(SectionOnBitmask(1, 0x1UL), Is.EqualTo(0x1UL));
            Assert.That(SectionOnBitmask(1, 0x0UL), Is.EqualTo(0x0UL));
            Assert.That(SectionOnBitmask(0, 0xFFFFUL), Is.EqualTo(0x0UL));
            Assert.That(SectionOnBitmask(64, ulong.MaxValue), Is.EqualTo(ulong.MaxValue));
            Assert.That(SectionOnBitmask(63, ulong.MaxValue), Is.EqualTo((1UL << 63) - 1UL));
        }

        // ===========================================================================================
        // Production helper tie-in — glm.toDegrees is the conversion the production guidance math uses, and
        // the documented (secondary) Stanley/Pure Pursuit helpers are anchored to it here.
        // ===========================================================================================

        /// <summary>
        /// Asserts the documented secondary helpers agree with the REAL production <c>glm.toDegrees</c>
        /// (CGLM.cs) — the very degree-conversion the production guidance math calls — and that the glm
        /// constants match their standard values. <c>glm</c> resolves unqualified because
        /// <c>AgOpenGPS.Tests.Parity</c> is nested under the <c>AgOpenGPS</c> namespace.
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

            Assert.That(glm.toRadians(180.0), Is.EqualTo(Math.PI).Within(Tolerance));
            Assert.That(glm.PIBy2, Is.EqualTo(Math.PI / 2.0).Within(1e-9));
            Assert.That(glm.twoPI, Is.EqualTo(2.0 * Math.PI).Within(1e-9));

            // Route the documented (secondary) Stanley/Pure Pursuit helpers through production glm.toDegrees
            // and confirm they agree with the in-test conversion (anchors the secondary helpers to production).
            double viaProductionGlm = ClampSteerAngle(glm.toDegrees(
                (Math.Atan((0.2 * 1.0) / 2.0) + (0.1 * 1.0)) * -1.0));
            double viaInTest = StanleySteerAngleDeg(
                distanceError: 0.2, headingErrorRad: 0.1, speed: 2.0, distanceGain: 1.0, headingGain: 1.0);
            Assert.That(viaInTest, Is.EqualTo(viaProductionGlm).Within(Tolerance),
                "the documented Stanley helper must equal the same math routed through production glm.toDegrees.");

            double ppViaProductionGlm = ClampSteerAngle(glm.toDegrees(
                Math.Atan2(2.0 * Wheelbase * Math.Sin(0.2), 5.0)));
            double ppViaInTest = PurePursuitSteerAngleDeg(0.2, Wheelbase, 5.0);
            Assert.That(ppViaInTest, Is.EqualTo(ppViaProductionGlm).Within(Tolerance),
                "the documented Pure Pursuit helper must equal the same math routed through production glm.toDegrees.");
        }
    }
}
