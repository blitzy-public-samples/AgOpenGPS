// [XPLAT] new for the net48/WinForms → net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// SECTION-CONTROL behavioral-parity suite (QA F5 findings M1-sections, M6-sections, M4).
//
// WHY THIS EXISTS
// --------------------------------------------------------------------------------------------------
// QA F5 found that the committed parity suite never exercised the production section-control path: it
// reproduced a generic `coverage & validMask` bitmask in-test (M1) against a formula-derived golden (M6),
// and it had NO test at all for the `isJobStarted` safety gate (M4). The QA report further claimed the
// section assembly was "impractical to isolate" (8-collaborator render-coupled graph). That claim is FALSE
// for the bit-packing/gate logic under test: SectionService is constructible from the same dependency graph
// the composition root builds in SourceCode/GPS/App.axaml.cs (step 10) — see ParityGraphFixture.BuildWithSections.
//
// WHAT THIS SUITE CERTIFIES (by invoking REAL production code, not a reproduction):
//   * SectionService.BuildMachineByte() — the dual-mode (unique-section / same-width-zone) bit packing that
//     fills the p_254 (0xFE), p_239 (0xEF) and p_229 (0xE5) PGN byte buffers. Asserted EXACTLY against
//     production-captured goldens (Parity/Golden/Guidance/sections_machinebyte.csv). Integer compare, no
//     tolerance. (resolves M1-sections + M6-sections)
//   * SectionService.DoRemoteSwitches() — the `if (appModel.isJobStarted)` remote-switch safety gate. A
//     remote section-ON request must NOT activate a section when no job/field is started, and MUST activate
//     it once a job is started. Asserted by driving the REAL gate both ways. (resolves M4)
//
// Behavior is FROZEN: BuildMachineByte / DoRemoteSwitches are byte-identical to the net48 baseline 860eb9fd
// by source-diff (only mf.* -> injected collaborators and pgn.* -> injected PGN instances changed). These
// tests are the automated regression gate that proves that freeze going forward.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using AgOpenGPS.Core;        // btnStates
using AgOpenGPS.Tests.Parity;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Production-invoking parity tests for the section-control domain: <c>SectionService.BuildMachineByte</c>
    /// PGN packing (both modes) and the <c>DoRemoteSwitches</c> <c>isJobStarted</c> safety gate. All inputs are
    /// fully specified and identical to the capture run that produced the committed goldens.
    /// </summary>
    [TestFixture]
    public class SectionControlParityTests
    {
        // ===========================================================================================
        // Golden loading (same convention as GuidanceEquivalenceTests: '#'-comments + first non-comment
        // schema line skipped; cross-OS Path.Combine; case-sensitive "Guidance" segment).
        // ===========================================================================================

        /// <summary>
        /// Loads the production-captured section golden into a map keyed by "scenario|field" (e.g.
        /// "sections|p254_sc1to8" -> 85). FAILS the test when the artifact is missing so CI enforces parity.
        /// </summary>
        private static Dictionary<string, int> LoadSectionGolden()
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Guidance", "sections_machinebyte.csv");

            Assert.That(
                File.Exists(path),
                Is.True,
                $"Required section golden artifact is missing: {path}. Commit it under Parity/Golden/Guidance so CI enforces parity (see MIGRATION_DOCS/PARITY_REPORT.md).");

            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            bool headerSeen = false;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                if (rawLine == null) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!headerSeen) { headerSeen = true; continue; }  // skip schema header

                string[] cells = line.Split(',');
                Assert.That(cells.Length, Is.EqualTo(3),
                    "sections_machinebyte.csv row must have 3 columns: scenario,field,expectedByte");

                string key = cells[0].Trim() + "|" + cells[1].Trim();
                int expected = int.Parse(cells[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                map[key] = expected;
            }

            Assert.That(map.Count, Is.GreaterThan(0), "sections_machinebyte.csv contained no data rows.");
            return map;
        }

        /// <summary>Sets <c>section[baseIdx + j].isSectionOn</c> for the 8 bits of <paramref name="mask"/>.</summary>
        private static void SetGroup(ParityGraphFixture.Graph g, int baseIdx, int mask)
        {
            for (int j = 0; j < 8; j++)
            {
                g.Section[baseIdx + j].isSectionOn = (mask & (1 << j)) != 0;
            }
        }

        // ===========================================================================================
        // M1-sections + M6-sections: production BuildMachineByte (unique-section mode) byte-for-byte.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="AgOpenGPS.Services.SectionService.BuildMachineByte"/> in unique-section
        /// mode (<c>tool.isSectionsNotZones = true</c>) over the captured input pattern and asserts every packed
        /// p_254 / p_239 / p_229 byte matches the production-captured golden EXACTLY (integer compare).
        /// </summary>
        [Test]
        public void BuildMachineByte_SectionsMode_MatchesGolden()
        {
            var golden = LoadSectionGolden();
            var g = ParityGraphFixture.BuildWithSections();

            g.Tool.isSectionsNotZones = true;
            SetGroup(g, 0, 0x55);   // sections 0,2,4,6 ON
            SetGroup(g, 8, 0x03);   // sections 8,9 ON
            g.Tool.farLeftSpeed = 5.0;
            g.Tool.farRightSpeed = 6.0;
            g.AppModel.avgSpeed = 4.0;

            g.Sections.BuildMachineByte();

            Assert.Multiple(() =>
            {
                Assert.That(g.Pgn.p_254.pgn[g.Pgn.p_254.sc1to8], Is.EqualTo(golden["sections|p254_sc1to8"]), "p_254.sc1to8");
                Assert.That(g.Pgn.p_254.pgn[g.Pgn.p_254.sc9to16], Is.EqualTo(golden["sections|p254_sc9to16"]), "p_254.sc9to16");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.sc1to8], Is.EqualTo(golden["sections|p239_sc1to8"]), "p_239.sc1to8");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.sc9to16], Is.EqualTo(golden["sections|p239_sc9to16"]), "p_239.sc9to16");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.speed], Is.EqualTo(golden["sections|p239_speed"]), "p_239.speed");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.tram], Is.EqualTo(golden["sections|p239_tram"]), "p_239.tram");
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.sc1to8], Is.EqualTo(golden["sections|p229_sc1to8"]), "p_229.sc1to8");
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.sc9to16], Is.EqualTo(golden["sections|p229_sc9to16"]), "p_229.sc9to16");
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.toolLSpeed], Is.EqualTo(golden["sections|p229_toolL"]), "p_229.toolLSpeed");
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.toolRSpeed], Is.EqualTo(golden["sections|p229_toolR"]), "p_229.toolRSpeed");
            });
        }

        // ===========================================================================================
        // M1-sections + M6-sections: production BuildMachineByte (same-width zone mode) byte-for-byte.
        // ===========================================================================================

        /// <summary>
        /// Drives the REAL <see cref="AgOpenGPS.Services.SectionService.BuildMachineByte"/> in same-width zone
        /// mode (<c>tool.isSectionsNotZones = false</c>, up to 64 sections packed into p_229 bytes 5..12) over
        /// the captured input pattern and asserts every packed byte matches the production-captured golden.
        /// </summary>
        [Test]
        public void BuildMachineByte_ZonesMode_MatchesGolden()
        {
            var golden = LoadSectionGolden();
            var g = ParityGraphFixture.BuildWithSections();

            g.Tool.isSectionsNotZones = false;
            SetGroup(g, 0, 0x03);    // p_229 byte5: sections 0,1 ON
            SetGroup(g, 16, 0x07);   // p_229 byte7: sections 16,17,18 ON
            g.Tool.farLeftSpeed = 5.0;
            g.Tool.farRightSpeed = 6.0;
            g.AppModel.avgSpeed = 4.0;

            g.Sections.BuildMachineByte();

            Assert.Multiple(() =>
            {
                for (int b = 5; b <= 12; b++)
                {
                    Assert.That(g.Pgn.p_229.pgn[b], Is.EqualTo(golden["zones|p229_b" + b.ToString(CultureInfo.InvariantCulture)]), $"p_229.pgn[{b}]");
                }
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.toolLSpeed], Is.EqualTo(golden["zones|p229_toolL"]), "p_229.toolLSpeed");
                Assert.That(g.Pgn.p_229.pgn[g.Pgn.p_229.toolRSpeed], Is.EqualTo(golden["zones|p229_toolR"]), "p_229.toolRSpeed");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.sc1to8], Is.EqualTo(golden["zones|p239_sc1to8"]), "p_239.sc1to8 mirror");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.sc9to16], Is.EqualTo(golden["zones|p239_sc9to16"]), "p_239.sc9to16 mirror");
                Assert.That(g.Pgn.p_254.pgn[g.Pgn.p_254.sc1to8], Is.EqualTo(golden["zones|p254_sc1to8"]), "p_254.sc1to8 mirror");
                Assert.That(g.Pgn.p_254.pgn[g.Pgn.p_254.sc9to16], Is.EqualTo(golden["zones|p254_sc9to16"]), "p_254.sc9to16 mirror");
                Assert.That(g.Pgn.p_239.pgn[g.Pgn.p_239.speed], Is.EqualTo(golden["zones|p239_speed"]), "p_239.speed");
            });
        }

        // ===========================================================================================
        // M4: production DoRemoteSwitches `isJobStarted` safety gate.
        // ===========================================================================================

        /// <summary>
        /// SAFETY (M4): drives the REAL <see cref="AgOpenGPS.Services.SectionService.DoRemoteSwitches"/> with an
        /// identical remote section-ON request (button hardware: <c>mc.ss[swMain]=0</c>, <c>mc.ss[swOnGr0]=0x01</c>)
        /// under both job states. With <c>appModel.isJobStarted = false</c> the gate MUST block activation
        /// (section 0 stays Off); with <c>isJobStarted = true</c> the same request MUST activate section 0 (On).
        /// Expected end-states are read from the production-captured golden (Off=0, On=2).
        /// </summary>
        [Test]
        public void IsJobStarted_Gate_BlocksRemoteSwitchActivation()
        {
            var golden = LoadSectionGolden();

            // isJobStarted = FALSE -> gate blocks; section 0 must remain unchanged (Off).
            var gf = ParityGraphFixture.BuildWithSections();
            gf.Tool.isSectionsNotZones = true;
            gf.Tool.numOfSections = 8;
            gf.Mc.ss[gf.Mc.swMain] = 0;        // bit2 clear -> button hardware; equals ssP -> master block skipped
            gf.Mc.ss[gf.Mc.swOnGr0] = 0x01;    // remote request: section 0 ON
            gf.Section[0].sectionBtnState = btnStates.Off;
            gf.AppModel.isJobStarted = false;

            gf.Sections.DoRemoteSwitches();

            Assert.That((int)gf.Section[0].sectionBtnState, Is.EqualTo(golden["gate|false_section0"]),
                "isJobStarted=false MUST NOT activate sections via remote switches (safety gate).");
            Assert.That(gf.Section[0].sectionBtnState, Is.EqualTo(btnStates.Off),
                "section 0 must stay Off when no job is started.");

            // isJobStarted = TRUE -> same request now activates section 0 (Off -> Auto -> On).
            var gt = ParityGraphFixture.BuildWithSections();
            gt.Tool.isSectionsNotZones = true;
            gt.Tool.numOfSections = 8;
            gt.Mc.ss[gt.Mc.swMain] = 0;
            gt.Mc.ss[gt.Mc.swOnGr0] = 0x01;
            gt.Section[0].sectionBtnState = btnStates.Off;
            gt.AppModel.isJobStarted = true;

            gt.Sections.DoRemoteSwitches();

            Assert.That((int)gt.Section[0].sectionBtnState, Is.EqualTo(golden["gate|true_section0"]),
                "isJobStarted=true MUST honor the remote section-ON request.");
            Assert.That(gt.Section[0].sectionBtnState, Is.Not.EqualTo(btnStates.Off),
                "section 0 must activate once a job is started.");
        }
    }
}
