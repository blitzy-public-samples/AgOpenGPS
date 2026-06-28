// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// GuidanceCompositionTests closes QA Issue 9 / PARITY_REPORT.md Open Risk #8 ("live SetGuidanceReferences /
// CGuidance peer-wiring risk"). The behavioural-parity suites already prove the guidance MATH is correct;
// this fixture proves the COMPOSITION is correct — that the production composition root constructs a live
// CGuidance and wires every cyclic guidance peer, so a live AutoSteer fix can never dereference a null
// guidance peer (the NullReferenceException the finding flagged on CABLine.GetCurrentABLine /
// CABCurve.GetCurrentCurveLine, reached from PositionService on every fix while AutoSteer is engaged).
//
// The production composition root (SourceCode/GPS/App.axaml.cs, step 9) and ParityGraphFixture BOTH wire the
// peers through the SAME helper, AgOpenGPS.Services.GuidanceComposition.WireGuidanceReferences. Because
// ParityGraphFixture.Build() now delegates to that exact helper, asserting the wiring on the fixture graph
// asserts the production wiring contract itself (single source of truth). Each peer is verified by reflecting
// the private field the corresponding Set*References seam assigns and asserting it is non-null.
using System;
using System.Reflection;
using AgOpenGPS.Services;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Verifies the guidance composition contract: a live <c>CGuidance</c> is constructed and every late-wired
    /// guidance peer reference is non-null after the shared
    /// <see cref="GuidanceComposition.WireGuidanceReferences"/> runs, and that the helper fails fast on a null
    /// collaborator. (QA Issue 9 / PARITY_REPORT Open Risk #8.)
    /// </summary>
    [TestFixture]
    public class GuidanceCompositionTests
    {
        /// <summary>
        /// Reads a private (or public) instance field by name and asserts the field exists (the seam shape is
        /// unchanged) and its value is non-null (the peer was actually wired by composition).
        /// </summary>
        private static void AssertPeerWired(object owner, string fieldName)
        {
            Assert.That(owner, Is.Not.Null, "owner object was not constructed");

            FieldInfo fi = owner.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(fi, Is.Not.Null,
                $"{owner.GetType().Name}.{fieldName} field not found — the Set*References seam shape changed.");
            Assert.That(fi.GetValue(owner), Is.Not.Null,
                $"{owner.GetType().Name}.{fieldName} guidance peer is null — composition did not wire it (QA Issue 9).");
        }

        /// <summary>
        /// The composition root must construct a LIVE CGuidance (the net48 build resolved it implicitly through
        /// FormGPS; the migrated graph must build it explicitly). A null here is the root cause of the Open Risk
        /// #8 NRE.
        /// </summary>
        [Test]
        public void CompositionRoot_ConstructsLiveGuidance()
        {
            var g = ParityGraphFixture.Build();

            Assert.That(g.Guidance, Is.Not.Null, "Composition root did not construct a live CGuidance (QA Issue 9).");
            Assert.That(g.Guidance, Is.TypeOf<CGuidance>());
        }

        /// <summary>
        /// Every cyclic guidance peer wired by the seven Set*References seams must be non-null after composition.
        /// This is the direct proof that a live guidance/steering path will not NRE on a null peer. The reflected
        /// field names match the assignments in each seam body (CABLine: trk/yt/bnd/gyd; CABCurve:
        /// trk/ABLine/yt/bnd/gyd; CContour: yt/gyd; CTrack: curve/ABLine/yt; CYouTurn: trk/curve/ABLine/bnd;
        /// CGuidance: curve/ABLine; CRecordedPath: vehicle/yt).
        /// </summary>
        [Test]
        public void WireGuidanceReferences_WiresEveryGuidancePeer_NoNullPeer()
        {
            var g = ParityGraphFixture.Build();

            // 1. CABLine ← trk / yt / bnd / gyd
            AssertPeerWired(g.ABLine, "trk");
            AssertPeerWired(g.ABLine, "yt");
            AssertPeerWired(g.ABLine, "bnd");
            AssertPeerWired(g.ABLine, "gyd");

            // 2. CABCurve ← trk / ABLine / yt / bnd / gyd
            AssertPeerWired(g.Curve, "trk");
            AssertPeerWired(g.Curve, "ABLine");
            AssertPeerWired(g.Curve, "yt");
            AssertPeerWired(g.Curve, "bnd");
            AssertPeerWired(g.Curve, "gyd");

            // 3. CContour ← yt / gyd
            AssertPeerWired(g.Contour, "yt");
            AssertPeerWired(g.Contour, "gyd");

            // 4. CTrack ← curve / ABLine / yt
            AssertPeerWired(g.Track, "curve");
            AssertPeerWired(g.Track, "ABLine");
            AssertPeerWired(g.Track, "yt");

            // 5. CYouTurn ← trk / curve / ABLine / bnd
            AssertPeerWired(g.YouTurn, "trk");
            AssertPeerWired(g.YouTurn, "curve");
            AssertPeerWired(g.YouTurn, "ABLine");
            AssertPeerWired(g.YouTurn, "bnd");

            // 6. CGuidance ← curve / ABLine
            AssertPeerWired(g.Guidance, "curve");
            AssertPeerWired(g.Guidance, "ABLine");

            // 7. CRecordedPath ← vehicle / yt
            AssertPeerWired(g.RecPath, "vehicle");
            AssertPeerWired(g.RecPath, "yt");
        }

        /// <summary>
        /// The wired graph must be able to drive the REAL (private) <c>CGuidance.DoSteerAngleCalc()</c> without a
        /// null-peer dereference — the exact runtime path QA Issue 9 flagged. (Production-captured numeric parity
        /// is asserted separately by GuidanceEquivalenceTests; here we only assert composition does not NRE.)
        /// </summary>
        [Test]
        public void WiredGraph_CanInvokeRealDoSteerAngleCalc_WithoutNullPeer()
        {
            var g = ParityGraphFixture.Build();

            Assert.That(() => g.InvokeDoSteerAngleCalc(), Throws.Nothing,
                "Real CGuidance.DoSteerAngleCalc threw on the composed graph — a guidance peer was not wired (QA Issue 9).");
        }

        /// <summary>
        /// The composition helper must FAIL FAST (ArgumentNullException) when handed a null collaborator, so an
        /// incomplete guidance graph is rejected at startup rather than surfacing as a later NRE on a live fix.
        /// </summary>
        [Test]
        public void WireGuidanceReferences_NullCollaborator_ThrowsArgumentNullException()
        {
            Assert.That(
                () => GuidanceComposition.WireGuidanceReferences(null, null, null, null, null, null, null, null, null),
                Throws.TypeOf<ArgumentNullException>(),
                "WireGuidanceReferences must reject a null collaborator (fail fast at the composition root).");
        }
    }
}
