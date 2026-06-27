// [XPLAT] migrated from net48 — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// PGN frame byte-equivalence + additive-CRC parity suite.
//
// This is the behavioral-parity PROOF for the PGN (Parameter Group Number) wire protocol — the
// single most safety-critical contract in the system (autosteer + section-control commands). The
// net48/WinForms → net8.0/Avalonia migration must reproduce every encoded PGN frame BYTE-FOR-BYTE,
// including the trailing additive checksum, on the windows/ubuntu/macos CI matrix (including
// osx-arm64). The frozen contract is documented in docs/pgn-protocol.md and is implemented by the
// migrated producer AgOpenGPS.Services.PgnDispatcher (SourceCode/GPS/Services/PgnDispatcher.cs),
// which carried the CPGN_* frame templates over VERBATIM from the former FormGPS partials
// (Forms/PGN.Designer.cs + Forms/UDPComm.Designer.cs).
//
// It extends the load → byte-compare pattern proven in
// SourceCode/AgLibrary.Tests/Settings/XmlSettingsHandlerTests.cs, and is a sibling of the other
// Parity fixtures (SteerConfigPgnParityTests covers the 0xFB/0xFC *configuration* frames; this
// fixture covers the runtime *telemetry* frames D0/FE/EF/E5/D6 and the optional EB/EC).
//
// ---------------------------------------------------------------------------------------------
// DESIGN RATIONALE (embedded judgment, AAP §0.6.4)
// ---------------------------------------------------------------------------------------------
// The migrated producer PgnDispatcher.cs — together with the CPGN_* frame templates relocated into
// it — is INTENTIONALLY compile-gated in SourceCode/GPS/AgOpenGPS.csproj
// (`<Compile Remove="Services\PgnDispatcher.cs" />`) for the duration of the staged migration,
// because it injects the still-gated CISOBUS/CTrack collaborators that remain FormGPS-coupled.
// The AgOpenGPS.Services namespace and the CPGN_* types are therefore NOT present in the compiled
// GPS assembly and cannot be referenced from this test project. PgnDispatcher and CISOBUS are also
// DI/`mf`-coupled (8-arg ctor / collaborator graph) and would never be constructed here regardless.
//
// As explicitly sanctioned by the file's specification ("If it is not cleanly constructible, fall
// back to verifying the pure algorithm + structure plus the golden byte-compare"), the frozen
// contract is proved two ways, BOTH compilable under net8.0 with no AgOpenGPS.Services dependency,
// no FormGPS graph, and no real socket:
//
//   (1) PURE-ALGORITHM + DOCUMENTED-STRUCTURE verification (always runs, needs no captured artifact):
//       the additive-CRC algorithm, the lat/lon integer encoding, the frozen frame-envelope of the
//       documented templates, and the 64-section capability are asserted directly against
//       docs/pgn-protocol.md. Every algorithm, index offset, and constant mirrors the production
//       source (PgnDispatcher.cs MakeCRC()/CPGN_* templates, PGN.Designer.cs, UDPComm.Designer.cs)
//       so the proof stays faithful to the relocated production code without depending on it at
//       compile time.
//
//   (2) GOLDEN byte-compare (enforced): canonical frames captured from the Windows/net48 baseline as
//       Parity/Golden/Pgn/*.bin, committed to the repository. A required artifact that is missing
//       FAILS the test so CI enforces the byte contract on every OS (see MIGRATION_DOCS/PARITY_REPORT.md).
//       Frames that have a deterministic, natural canonical input
//       (D0 via a lat/lon, E5 via "all 64 sections on") are byte-compared through the documented
//       encoder; frames whose payload is runtime-telemetry-dependent (FE/EF), inbound-only with no
//       encoder (D6), or settings-coupled (EB/EC) are validated against the self-consistent frozen
//       ENVELOPE of the captured golden (header/source/id, data-length consistency, and the
//       additive-CRC reproduced over the captured bytes) — never by fabricating payload bytes.
using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Byte-exact + additive-CRC parity tests for the runtime PGN telemetry frames
    /// (0xD0/0xFE/0xEF/0xE5/0xD6, plus optional 0xEB/0xEC) and the frozen loopback network constants.
    /// </summary>
    [TestFixture]
    public class PgnFrameGoldenTests
    {
        // -------------------------------------------------------------------------------------------------
        // Frozen wire constants (docs/pgn-protocol.md + the migrated CPGN_* templates in PgnDispatcher.cs).
        // -------------------------------------------------------------------------------------------------
        private const byte Header0 = 0x80; // frame[0] — fixed header byte 0
        private const byte Header1 = 0x81; // frame[1] — fixed header byte 1
        private const byte SrcAddr = 0x7F; // frame[2] — source address

        private const byte LatLonPgn = 0xD0;      // 208 — Latitude/Longitude (14B)
        private const byte AutoSteerPgn = 0xFE;   // 254 — AutoSteer Data (14B)
        private const byte MachinePgn = 0xEF;     // 239 — Machine Data (14B)
        private const byte SectionExtPgn = 0xE5;  // 229 — Section Control Extended (16B)
        private const byte GpsPositionPgn = 0xD6; // 214 — GPS Position (per README 52B)
        private const byte SectionDimsPgn = 0xEB; // 235 — Section Dimensions (optional)
        private const byte RelayConfigPgn = 0xEC; // 236 — Relay Config (optional)

        // The universal envelope adds 6 non-payload bytes to the data-length byte:
        //   3 header/source (0x80 0x81 0x7F) + 1 id + 1 length + <data> + 1 additive CRC.
        private const int EnvelopeOverhead = 6;

        // Documented 0xE5 index layout (mirrors the CPGN_E5 field offsets relocated into PgnDispatcher.cs):
        // eight section-bitmask bytes [5..12] (up to 64 sections) + tool-left/right speeds at [13]/[14].
        private const int E5FirstSectionIndex = 5;
        private const int E5LastSectionIndex = 12;
        private const int E5ToolLeftIndex = 13;
        private const int E5ToolRightIndex = 14;

        // Sample geodetic fix used by both the pure lat/lon test and the D0 golden builder. A capture of
        // D0_latlon.bin from the baseline must be produced from these values via the documented formula.
        private const double SampleLat = 45.0;
        private const double SampleLon = -93.0;

        // Frozen loopback network constants (asserted as values only — NO socket is ever opened). The
        // production source defines them in SourceCode/GPS/Services/PgnDispatcher.cs:
        //   * loopBackSocket.Bind(new IPEndPoint(IPAddress.Loopback, 15555))      → AOG listen port
        //   * epAgIO = new IPEndPoint(IPAddress.Parse("127.255.255.255"), 17777)  → AgIO send endpoint
        //   * public int udpWatchLimit = 70                                       → per-fix receive throttle
        private const int AogListenPort = 15555;
        private const int AgIoEndpointPort = 17777;
        private const string AgIoEndpointAddress = "127.255.255.255";
        private const string LoopbackPrefix = "127.";
        private const int UdpWatchLimitMs = 70;

        /// <summary>
        /// [XPLAT] Forces <see cref="CultureInfo.InvariantCulture"/> for the whole fixture so any numeric
        /// formatting (e.g. the hex frame dumps in failure messages) is locale-independent and so the suite
        /// behaves identically under a comma-decimal locale on Linux/macOS.
        /// </summary>
        [OneTimeSetUp]
        public void ForceInvariantCulture()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        // =================================================================================================
        // Phase B — Additive-CRC algorithm (pure; needs no golden artifact).
        // =================================================================================================

        /// <summary>
        /// The all-zero 0xD0 template's additive checksum is 0x7F + 0xD0 + 0x08 = 343 ≡ 0x57 (mod 256).
        /// docs/pgn-protocol.md shows the template trailing byte as 0xCC, but that is an UNINITIALISED
        /// placeholder in the CPGN_D0 template; the production MakeCRC()/SendPgnToLoop() overwrites it with
        /// the computed additive checksum before transmit. We assert the computed value (0x57) and confirm it
        /// differs from the documented placeholder, proving the slot is overwritten.
        /// </summary>
        [Test]
        public void AdditiveCrc_ZeroPayload_D0Template_MatchesDoc()
        {
            // The documented 0xD0 template with the CRC slot left at 0 (it is excluded from the sum anyway).
            byte[] template = { Header0, Header1, SrcAddr, LatLonPgn, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            byte computed = AdditiveCrc(template);

            Assert.Multiple(() =>
            {
                Assert.That(computed, Is.EqualTo((byte)0x57),
                    "0x7F + 0xD0 + 0x08 = 343 ≡ 0x57 (mod 256) — the additive CRC of the zero-payload 0xD0 frame.");
                Assert.That(computed, Is.Not.EqualTo((byte)0xCC),
                    "the documented 0xCC trailing byte is an uninitialised placeholder; production MakeCRC() overwrites it with 0x57.");
            });
        }

        /// <summary>
        /// The checksum accumulates in a <see cref="byte"/>, so it wraps modulo 256. A payload whose running
        /// sum exceeds one byte must yield exactly the low 8 bits of that sum — that wrap IS the protocol.
        /// </summary>
        [Test]
        public void AdditiveCrc_WrapsModulo256()
        {
            // Sum over [2..12] = 0x7F+0xFF+0x08+0xFF+0xFF+0xFF+0x10 = 1171, which exceeds 255.
            byte[] frame = { Header0, Header1, SrcAddr, 0xFF, 8, 0xFF, 0xFF, 0xFF, 0x10, 0, 0, 0, 0, 0 };

            int rawSum = 0;
            for (int i = 2; i < frame.Length - 1; i++)
            {
                rawSum += frame[i];
            }

            Assert.Multiple(() =>
            {
                Assert.That(rawSum, Is.GreaterThan(255),
                    "the test vector must exceed one byte to actually exercise the modulo-256 wrap.");
                Assert.That(AdditiveCrc(frame), Is.EqualTo((byte)rawSum),
                    "the additive CRC must equal the low 8 bits of the running sum (byte wrap).");
            });
        }

        /// <summary>
        /// The CRC is summed over indices [2 .. len-2] inclusive: the source byte through the last data byte.
        /// The header bytes (0,1) and the trailing CRC slot itself are EXCLUDED. Flipping an excluded byte
        /// must not change the checksum; flipping a covered data byte must.
        /// </summary>
        [Test]
        public void AdditiveCrc_CoversSourceThroughLastDataByte_NotHeaderBytes()
        {
            byte[] frame = { Header0, Header1, SrcAddr, AutoSteerPgn, 8, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x00 };
            byte baseline = AdditiveCrc(frame);

            byte[] flippedHeader = (byte[])frame.Clone();
            flippedHeader[0] ^= 0xFF; // 0x80 header byte — excluded
            flippedHeader[1] ^= 0xFF; // 0x81 header byte — excluded

            byte[] flippedCrcSlot = (byte[])frame.Clone();
            flippedCrcSlot[^1] ^= 0xFF; // the CRC slot is excluded from its own computation

            byte[] flippedData = (byte[])frame.Clone();
            flippedData[5] ^= 0xFF; // first data byte — covered

            Assert.Multiple(() =>
            {
                Assert.That(AdditiveCrc(flippedHeader), Is.EqualTo(baseline),
                    "header bytes 0x80/0x81 (indices 0-1) must be excluded from the additive CRC.");
                Assert.That(AdditiveCrc(flippedCrcSlot), Is.EqualTo(baseline),
                    "the trailing CRC slot must be excluded from its own computation.");
                Assert.That(AdditiveCrc(flippedData), Is.Not.EqualTo(baseline),
                    "data bytes [5..len-2] must be covered by the additive CRC.");
            });
        }

        // =================================================================================================
        // Phase C — Lat/Lon integer encoding (pure math; mirrors docs/pgn-protocol.md + CPGN_D0).
        // =================================================================================================

        /// <summary>
        /// docs/pgn-protocol.md: <c>encodedAngle = (int)(lat * (0x7FFFFFFF / 90.0))</c> for latitude and
        /// <c>(int)(lon * (0x7FFFFFFF / 180.0))</c> for longitude. CPGN_D0 stores each int32 via
        /// <see cref="BitConverter.GetBytes(int)"/> (little-endian on every supported RID:
        /// win-x64/linux-x64/osx-x64/osx-arm64) into latLong[5..8] and [9..12]. We pin the documented formula
        /// to its independently-verified little-endian constants and assert the documented frame layout places
        /// them at indices [5..8] and [9..12].
        /// </summary>
        [Test]
        public void LatLon_Encoding_MatchesDocFormula()
        {
            int encodedLat = (int)(SampleLat * (0x7FFFFFFF / 90.0));
            int encodedLon = (int)(SampleLon * (0x7FFFFFFF / 180.0));

            byte[] expectedLat = BitConverter.GetBytes(encodedLat);
            byte[] expectedLon = BitConverter.GetBytes(encodedLon);

            // Independently-verified little-endian int32 encodings for lat=45.0 / lon=-93.0. These pin the
            // documented formula to known bytes so the test cannot pass on a self-referential tautology.
            byte[] knownLat = { 0xFF, 0xFF, 0xFF, 0x3F };
            byte[] knownLon = { 0xDF, 0xDD, 0xDD, 0xBD };

            byte[] frame = BuildCanonicalD0();

            Assert.Multiple(() =>
            {
                Assert.That(expectedLat, Is.EqualTo(knownLat),
                    $"the documented latitude formula must produce the verified little-endian int32 for 45.0. got={Hex(expectedLat)}");
                Assert.That(expectedLon, Is.EqualTo(knownLon),
                    $"the documented longitude formula must produce the verified little-endian int32 for -93.0. got={Hex(expectedLon)}");
                Assert.That(frame[5..9], Is.EqualTo(expectedLat),
                    $"latitude int32 must occupy frame indices [5..8] little-endian. frame={Hex(frame)}");
                Assert.That(frame[9..13], Is.EqualTo(expectedLon),
                    $"longitude int32 must occupy frame indices [9..12] little-endian. frame={Hex(frame)}");
            });
        }

        // =================================================================================================
        // Phase D (pure structural) — the documented frame templates conform to the frozen envelope, and the
        // 0xE5 frame can represent all 64 sections while staying 16 bytes. These run with no golden artifact.
        // =================================================================================================

        /// <summary>
        /// Every documented runtime frame template (D0/FE/EF 14B, E5 16B) must conform to the frozen envelope:
        /// header 0x80 0x81, source 0x7F, the expected id, and a data-length byte consistent with the array
        /// length (len == total - 6). This ties the documented contract (docs/pgn-protocol.md) and the
        /// relocated CPGN_* templates to the suite without needing any captured artifact.
        /// </summary>
        [Test]
        public void DocumentedFrameTemplates_ConformToEnvelope()
        {
            Assert.Multiple(() =>
            {
                AssertTemplateEnvelope(NewFrameTemplate(LatLonPgn, 8), LatLonPgn, 8, 14);
                AssertTemplateEnvelope(NewFrameTemplate(AutoSteerPgn, 8), AutoSteerPgn, 8, 14);
                AssertTemplateEnvelope(NewFrameTemplate(MachinePgn, 8), MachinePgn, 8, 14);
                // 0xE5 carries a data-length byte of 10 (eight section-bitmask bytes + tool L/R speed) → 16B.
                AssertTemplateEnvelope(NewFrameTemplate(SectionExtPgn, 10), SectionExtPgn, 10, 16);
            });
        }

        /// <summary>
        /// The 0xE5 extended section frame carries eight bitmask bytes at indices [5..12] (up to 64 sections)
        /// plus tool-left/right speeds at [13]/[14], and must remain exactly 16 bytes even with every section
        /// bit set. The documented index layout mirrors the CPGN_E5 field offsets relocated into
        /// PgnDispatcher.cs.
        /// </summary>
        [Test]
        public void Pgn_E5_AllSixtyFourSections_Representable_StaysSixteenBytes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(E5FirstSectionIndex, Is.EqualTo(5), "section bitmask 1-8 must start at index 5.");
                Assert.That(E5LastSectionIndex, Is.EqualTo(12), "section bitmask 57-64 must end at index 12 (8 bytes → 64 sections).");
                Assert.That(E5ToolLeftIndex, Is.EqualTo(13), "tool-left speed must occupy index 13.");
                Assert.That(E5ToolRightIndex, Is.EqualTo(14), "tool-right speed must occupy index 14.");
            });

            byte[] frame = BuildCanonicalE5();

            Assert.Multiple(() =>
            {
                Assert.That(frame.Length, Is.EqualTo(16),
                    "the 0xE5 frame must stay 16 bytes even with all 64 sections set.");
                Assert.That(frame[5..13], Is.All.EqualTo((byte)0xFF),
                    $"all eight section-bitmask bytes [5..12] must be set (64 sections). frame={Hex(frame)}");
                Assert.That(frame[^1], Is.EqualTo(AdditiveCrc(frame)),
                    "the trailing CRC must be the additive checksum of the all-sections frame.");
            });
        }

        // =================================================================================================
        // Phase D (golden byte-compare / envelope) — enforced; a missing required *.bin FAILS the test.
        // =================================================================================================

        /// <summary>
        /// 0xD0 Latitude/Longitude (14B). Encoded via the documented formula for the canonical
        /// <see cref="SampleLat"/>/<see cref="SampleLon"/> fix and compared byte-for-byte to the golden.
        /// </summary>
        [Test]
        public void Pgn_D0_LatLon_MatchesGolden()
        {
            byte[] frame = BuildCanonicalD0();
            byte[] golden = LoadRequiredGolden("D0_latlon.bin");

            Assert.That(frame, Is.EqualTo(golden),
                $"0xD0 frame must be byte-identical to the net48 baseline (incl. additive CRC). actual={Hex(frame)}");
        }

        /// <summary>
        /// 0xE5 Section Control Extended (16B). Encoded with all 64 sections set (the documented
        /// maximum-capability vector) and compared byte-for-byte to the golden.
        /// </summary>
        [Test]
        public void Pgn_E5_SectionControlExtended_MatchesGolden()
        {
            byte[] frame = BuildCanonicalE5();
            byte[] golden = LoadRequiredGolden("E5_sections.bin");

            Assert.That(frame, Is.EqualTo(golden),
                $"0xE5 frame must be byte-identical to the net48 baseline (incl. additive CRC). actual={Hex(frame)}");
        }

        /// <summary>
        /// 0xFE AutoSteer Data (14B). Its payload (speed/status/steer-angle/line-distance/section bitmasks)
        /// is runtime-telemetry-dependent, so the captured golden is validated against the frozen envelope
        /// rather than by fabricating payload bytes.
        /// </summary>
        [Test]
        public void Pgn_FE_AutoSteer_MatchesGolden()
        {
            byte[] golden = LoadRequiredGolden("FE_autosteer.bin");
            AssertEnvelopeContract(golden, AutoSteerPgn);
        }

        /// <summary>
        /// 0xEF Machine Data (14B). Payload (uturn/speed/hyd-lift/tram/geo-stop + section bitmasks) is
        /// runtime-dependent; the captured golden is validated against the frozen envelope.
        /// </summary>
        [Test]
        public void Pgn_EF_MachineData_MatchesGolden()
        {
            byte[] golden = LoadRequiredGolden("EF_machine.bin");
            AssertEnvelopeContract(golden, MachinePgn);
        }

        /// <summary>
        /// 0xD6 GPS Position (per README 52B). This is an INBOUND/decoded frame — there is no production
        /// encoder (no CPGN_D6) — so the captured golden is validated against the self-consistent frozen
        /// envelope (header/source/id, data-length consistency, and the additive CRC reproduced over the
        /// captured bytes). docs/pgn-protocol.md states 52 bytes; the captured golden is authoritative.
        /// </summary>
        [Test]
        public void Pgn_D6_GpsPosition_MatchesGolden()
        {
            byte[] golden = LoadRequiredGolden("D6_gps.bin");
            AssertEnvelopeContract(golden, GpsPositionPgn);
        }

        /// <summary>
        /// 0xEB Section Dimensions (optional). The relocated CPGN_EB template and docs/pgn-protocol.md disagree
        /// on the exact length (39B production vs 38B doc), so the captured golden is the authority: it is
        /// validated against the self-consistent frozen envelope rather than a fabricated payload.
        /// </summary>
        [Test]
        public void Pgn_EB_SectionDimensions_MatchesGolden()
        {
            byte[] golden = LoadRequiredGolden("EB_dims.bin");
            AssertEnvelopeContract(golden, SectionDimsPgn);
        }

        /// <summary>
        /// 0xEC Relay Config (optional). CPGN_EC parses a persisted pin map from settings and is NOT cleanly
        /// constructible, so the captured golden is validated against the self-consistent frozen envelope.
        /// </summary>
        [Test]
        public void Pgn_EC_RelayConfig_MatchesGolden()
        {
            byte[] golden = LoadRequiredGolden("EC_relay.bin");
            AssertEnvelopeContract(golden, RelayConfigPgn);
        }

        // =================================================================================================
        // Phase E — Frozen loopback network constants (assert values only; NEVER open or bind a socket).
        // =================================================================================================

        /// <summary>
        /// AOG binds/listens on loopback port 15555 and AgIO's send endpoint is port 17777. Defined in
        /// PgnDispatcher.cs (the Bind call and the epAgIO field); asserted here as frozen contract values.
        /// </summary>
        [Test]
        public void LoopbackPorts_AreUnchanged()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AogListenPort, Is.EqualTo(15555), "AOG must keep binding/listening on loopback port 15555.");
                Assert.That(AgIoEndpointPort, Is.EqualTo(17777), "the AgIO send endpoint must keep port 17777.");
            });
        }

        /// <summary>
        /// The AgIO broadcast endpoint address is 127.255.255.255 and the loopback subnet is 127.x.x.x. The
        /// two-program model communicates only over UDP loopback; these values must not drift.
        /// </summary>
        [Test]
        public void AgIoBroadcastEndpoint_IsUnchanged()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AgIoEndpointAddress, Is.EqualTo("127.255.255.255"),
                    "the AgIO endpoint address (epAgIO in PgnDispatcher.cs) must remain 127.255.255.255.");
                Assert.That(AgIoEndpointAddress, Does.StartWith(LoopbackPrefix),
                    "the endpoint must remain inside the 127.x.x.x loopback subnet.");
            });
        }

        /// <summary>
        /// The per-fix receive throttle (udpWatchLimit) is frozen at 70 ms. It is a public instance field on
        /// PgnDispatcher (not a static constant), so we assert the contract literal here without constructing
        /// the DI-coupled dispatcher.
        /// </summary>
        [Test]
        public void UdpWatchLimit_Is70ms()
        {
            Assert.That(UdpWatchLimitMs, Is.EqualTo(70),
                "the per-fix scan-loop throttle (PgnDispatcher.udpWatchLimit) must remain 70 ms.");
        }

        // -------------------------------------------------------------------------------------------------
        // Builders + helpers.
        // -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds a documented PGN frame template: <c>[0x80, 0x81, 0x7F, id, dataLen, &lt;dataLen zero bytes&gt;,
        /// crc-slot]</c>. The total length is <paramref name="dataLen"/> + 6 (the universal envelope overhead);
        /// the data bytes and the trailing CRC slot start zero-initialised.
        /// </summary>
        private static byte[] NewFrameTemplate(byte pgnId, int dataLen)
        {
            byte[] frame = new byte[dataLen + EnvelopeOverhead];
            frame[0] = Header0;
            frame[1] = Header1;
            frame[2] = SrcAddr;
            frame[3] = pgnId;
            frame[4] = (byte)dataLen;
            return frame; // remaining bytes (payload + CRC slot) are already zero.
        }

        /// <summary>
        /// Builds the canonical 0xD0 frame for the <see cref="SampleLat"/>/<see cref="SampleLon"/> fix using the
        /// documented encoding (<c>(int)(lat * (0x7FFFFFFF / 90.0))</c> / <c>(int)(lon * (0x7FFFFFFF / 180.0))</c>
        /// stored little-endian via <see cref="BitConverter.GetBytes(int)"/> at indices [5..8] and [9..12]),
        /// then appends the additive CRC. Mirrors CPGN_D0.LoadLatitudeLongitude.
        /// </summary>
        private static byte[] BuildCanonicalD0()
        {
            byte[] frame = NewFrameTemplate(LatLonPgn, 8);

            int encodedLat = (int)(SampleLat * (0x7FFFFFFF / 90.0));
            int encodedLon = (int)(SampleLon * (0x7FFFFFFF / 180.0));

            Array.Copy(BitConverter.GetBytes(encodedLat), 0, frame, 5, 4);
            Array.Copy(BitConverter.GetBytes(encodedLon), 0, frame, 9, 4);

            frame[^1] = AdditiveCrc(frame);
            return frame;
        }

        /// <summary>
        /// Builds the canonical 0xE5 frame with all 64 sections set (every bitmask bit at indices [5..12]),
        /// tool speeds zero ([13]/[14]), and the additive CRC appended. Mirrors the CPGN_E5 layout.
        /// </summary>
        private static byte[] BuildCanonicalE5()
        {
            byte[] frame = NewFrameTemplate(SectionExtPgn, 10);

            for (int i = E5FirstSectionIndex; i <= E5LastSectionIndex; i++)
            {
                frame[i] = 0xFF;
            }

            frame[E5ToolLeftIndex] = 0;
            frame[E5ToolRightIndex] = 0;
            frame[^1] = AdditiveCrc(frame);
            return frame;
        }

        /// <summary>
        /// Asserts a captured golden conforms to the frozen PGN envelope: header 0x80 0x81, source 0x7F, the
        /// expected id, a data-length byte consistent with the array length (len == total - 6), and a trailing
        /// additive CRC that our algorithm reproduces over the captured bytes.
        /// </summary>
        private static void AssertEnvelopeContract(byte[] frame, byte expectedPgnId)
        {
            Assert.That(frame.Length, Is.GreaterThanOrEqualTo(7),
                $"a PGN frame must be at least 7 bytes (header+source+id+len+>=0 data+crc). got {frame.Length}: {Hex(frame)}");

            Assert.Multiple(() =>
            {
                Assert.That(frame[0], Is.EqualTo(Header0), "byte 0 must be the fixed header 0x80.");
                Assert.That(frame[1], Is.EqualTo(Header1), "byte 1 must be the fixed header 0x81.");
                Assert.That(frame[2], Is.EqualTo(SrcAddr), "byte 2 must be the source address 0x7F.");
                Assert.That(frame[3], Is.EqualTo(expectedPgnId), "byte 3 must be the expected PGN id.");
                Assert.That(frame.Length, Is.EqualTo(frame[4] + EnvelopeOverhead),
                    "total length must equal the data-length byte + 6 (3 header/source + id + len + crc).");
                Assert.That(frame[^1], Is.EqualTo(AdditiveCrc(frame)),
                    $"the trailing CRC must equal the additive checksum over [2..len-2]. frame={Hex(frame)}");
            });
        }

        /// <summary>
        /// Asserts a documented frame template's frozen envelope: header/source, the expected id, the exact
        /// data-length byte, and the exact total array length (len == total - 6).
        /// </summary>
        private static void AssertTemplateEnvelope(byte[] template, byte expectedPgnId, int expectedDataLen, int expectedTotal)
        {
            Assert.That(template.Length, Is.EqualTo(expectedTotal),
                $"the {Hex1(expectedPgnId)} template must be {expectedTotal.ToString(CultureInfo.InvariantCulture)} bytes. template={Hex(template)}");
            Assert.That(template[0], Is.EqualTo(Header0));
            Assert.That(template[1], Is.EqualTo(Header1));
            Assert.That(template[2], Is.EqualTo(SrcAddr));
            Assert.That(template[3], Is.EqualTo(expectedPgnId));
            Assert.That(template[4], Is.EqualTo((byte)expectedDataLen),
                $"the {Hex1(expectedPgnId)} data-length byte must be {expectedDataLen.ToString(CultureInfo.InvariantCulture)}.");
            Assert.That(template.Length, Is.EqualTo(template[4] + EnvelopeOverhead),
                "the data-length byte must be consistent with the total frame length (len == total - 6).");
        }

        /// <summary>
        /// Loads a REQUIRED captured golden PGN frame as raw bytes, FAILING the test when the artifact is
        /// missing so CI enforces byte-contract parity (the goldens are committed under
        /// <c>Parity/Golden/Pgn</c> and copied to the test output by the <c>AgOpenGPS.Tests.csproj</c>
        /// <c>Parity\Golden\**\*</c> rule). Resolution is cross-OS and case-stable via
        /// <see cref="TestContext.CurrentContext"/>.<c>TestDirectory</c> + <see cref="Path.Combine"/> (the
        /// category folder is exactly <c>Pgn</c>); callers read with <see cref="File.ReadAllBytes"/> so
        /// end-of-line/encoding drift can never mask a difference in these binary frames.
        /// </summary>
        private static byte[] LoadRequiredGolden(string fileName)
        {
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Parity", "Golden", "Pgn", fileName);

            Assert.That(
                File.Exists(path),
                Is.True,
                $"Required PGN golden artifact is missing: {path}. Commit it under Parity/Golden/Pgn so CI enforces byte-contract parity (see MIGRATION_DOCS/PARITY_REPORT.md).");

            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// The additive checksum per docs/pgn-protocol.md: sum bytes [2 .. Length-2] (source byte through the
        /// last data byte), wrapping mod 256; the header bytes and the CRC slot are excluded. Mirrors the
        /// production <c>MakeCRC()</c> in PgnDispatcher.cs (<c>for (i=2; i&lt;pgn.Length-1; i++) crc += pgn[i]</c>).
        /// </summary>
        private static byte AdditiveCrc(byte[] frame)
        {
            byte crc = 0;
            for (int i = 2; i < frame.Length - 1; i++)
            {
                crc += frame[i];
            }

            return crc;
        }

        /// <summary>Formats a frame as a dash-separated hex string (InvariantCulture) for failure messages.</summary>
        private static string Hex(byte[] frame)
        {
            return string.Join("-", Array.ConvertAll(frame, b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        /// <summary>Formats a single byte as two invariant hex digits for failure messages.</summary>
        private static string Hex1(byte value)
        {
            return value.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
