// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// GuidanceComposition is the SINGLE SOURCE OF TRUTH for wiring the cyclic guidance peer references
// that the guidance C-classes cannot receive at construction time (they form mutually-dependent
// cycles: CABLine ↔ CTrack/CYouTurn/CGuidance, CGuidance ↔ CABCurve/CABLine, CYouTurn ↔
// CTrack/CABCurve/CABLine, …). In the net48/WinForms build these references resolved implicitly
// through the shared FormGPS (`mf`) god-object; the migration removed that back-reference, so the
// peers must be wired explicitly post-construction through the public Set*References seams each class
// exposes.
//
// WHY THIS EXISTS (QA Issue 9 / PARITY_REPORT.md Open Risk #8)
// --------------------------------------------------------------------------------------------------
// The composition root (SourceCode/GPS/App.axaml.cs, step 9) previously constructed the guidance
// objects (CABLine/CABCurve/CContour/CYouTurn/CTrack/CRecordedPath) but NEVER constructed a live
// CGuidance and NEVER called any Set*References seam. Because CABLine.GetCurrentABLine() calls
// gyd.StanleyGuidanceABLine(...) and CABCurve.GetCurrentCurveLine() calls gyd.StanleyGuidanceCurve(...)
// — both reached from PositionService on every fix while AutoSteer is engaged on a track — a live
// guidance flow would dereference a null `gyd` and throw NullReferenceException. The behavioural-parity
// fixture (AgOpenGPS.Tests/Parity/ParityGraphFixture) proved the math is correct, but it wired the
// peers itself (and only five of the seven seams), so the production composition root and the test
// graph could drift. Centralising ALL SEVEN seams here — and having BOTH the composition root and the
// parity fixture call this one method — makes the wiring provably identical ("wired the same way the
// parity graph fixtures do", per the finding's expected outcome) and closes the gap at its root cause.
//
// The seven seams (every late-wired guidance peer reference in the domain):
//   1. CABLine.SetGuidanceReferences(track, youTurn, boundary, guidance)
//   2. CABCurve.SetGuidanceReferences(track, abLine, youTurn, boundary, guidance)
//   3. CContour.SetGuidanceReferences(youTurn, guidance)
//   4. CTrack.SetGuidanceReferences(curve, abLine, youTurn)
//   5. CYouTurn.SetGuidanceReferences(track, curve, abLine, boundary)
//   6. CGuidance.SetGuidanceReferences(curve, abLine)
//   7. CRecordedPath.SetReferences(vehicle, youTurn)
//
// Behaviour is FROZEN (AAP §0.2.2, §0.7.1): this only relocates/centralises wiring that the net48
// build performed implicitly; it changes no guidance output. The guidance C-classes live in the
// enclosing `AgOpenGPS` namespace and resolve here through namespace nesting (matching the sibling
// extracted services PositionService/SectionService), so no extra `using` is required for them.
using System;

namespace AgOpenGPS.Services
{
    /// <summary>
    /// [XPLAT] Centralised, fail-fast wiring of the post-construction guidance peer references shared by
    /// the production composition root (App.axaml.cs) and the behavioural-parity fixture. See the file
    /// header for the rationale (QA Issue 9 / PARITY_REPORT Open Risk #8).
    /// </summary>
    public static class GuidanceComposition
    {
        /// <summary>
        /// Wires every cyclic guidance peer reference through the public <c>Set*References</c> seams the
        /// guidance C-classes expose. All collaborators are REQUIRED for a complete guidance graph, so a
        /// null argument is a composition-root defect and throws <see cref="ArgumentNullException"/>
        /// immediately (fail fast at startup) rather than surfacing later as a
        /// <see cref="NullReferenceException"/> on a live AutoSteer fix — the exact failure mode QA Issue 9
        /// flagged. The seam calls only store references (no guidance computation occurs here), so the
        /// guidance outputs remain byte-for-byte frozen (AAP §0.2.2 / §0.7.1).
        /// </summary>
        /// <param name="abLine">The AB-line guidance model (Pure Pursuit / Stanley AB line).</param>
        /// <param name="curve">The AB-curve guidance model.</param>
        /// <param name="contour">The contour guidance model.</param>
        /// <param name="track">The track manager (AB/curve/youturn selection).</param>
        /// <param name="youTurn">The U-turn / headland-turn model.</param>
        /// <param name="recordedPath">The recorded-path playback model.</param>
        /// <param name="guidance">The Stanley steering-angle calculator (CGuidance).</param>
        /// <param name="boundary">The field boundary (used by AB/curve/youturn boundary checks).</param>
        /// <param name="vehicle">The vehicle model (recorded-path peer).</param>
        /// <exception cref="ArgumentNullException">Thrown when any collaborator is <see langword="null"/>.</exception>
        public static void WireGuidanceReferences(
            CABLine abLine,
            CABCurve curve,
            CContour contour,
            CTrack track,
            CYouTurn youTurn,
            CRecordedPath recordedPath,
            CGuidance guidance,
            CBoundary boundary,
            CVehicle vehicle)
        {
            // Fail closed/fast: an incomplete guidance graph must never reach a live steering fix.
            if (abLine == null) throw new ArgumentNullException(nameof(abLine));
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (contour == null) throw new ArgumentNullException(nameof(contour));
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (youTurn == null) throw new ArgumentNullException(nameof(youTurn));
            if (recordedPath == null) throw new ArgumentNullException(nameof(recordedPath));
            if (guidance == null) throw new ArgumentNullException(nameof(guidance));
            if (boundary == null) throw new ArgumentNullException(nameof(boundary));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));

            // 1. AB line ← track / youturn / boundary / guidance (drives GetCurrentABLine pure-pursuit + Stanley).
            abLine.SetGuidanceReferences(track, youTurn, boundary, guidance);

            // 2. AB curve ← track / ab line / youturn / boundary / guidance (drives GetCurrentCurveLine).
            curve.SetGuidanceReferences(track, abLine, youTurn, boundary, guidance);

            // 3. Contour ← youturn / guidance.
            contour.SetGuidanceReferences(youTurn, guidance);

            // 4. Track ← curve / ab line / youturn (track selection across guidance modes).
            track.SetGuidanceReferences(curve, abLine, youTurn);

            // 5. YouTurn ← track / curve / ab line / boundary (U-turn geometry peers).
            youTurn.SetGuidanceReferences(track, curve, abLine, boundary);

            // 6. Guidance ← curve / ab line (Stanley DoSteerAngleCalc reads the active line from these).
            guidance.SetGuidanceReferences(curve, abLine);

            // 7. Recorded path ← vehicle / youturn (note the seam name is SetReferences, not SetGuidanceReferences).
            recordedPath.SetReferences(vehicle, youTurn);
        }
    }
}
