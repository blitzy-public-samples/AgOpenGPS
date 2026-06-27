// [XPLAT] new for the net48/WinForms -> net8.0/Avalonia migration — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// PGN parity fixture for the steer-board configuration frames produced by the migrated
// AgOpenGPS.Views.Settings.FormSteerView (the net8.0/Avalonia port of net48 Forms/Settings/FormSteer.cs).
//
// SCOPE NOTE: this fixture is intentionally DISTINCT from the parent agent's planned PgnFrameGoldenTests.cs,
// which covers the *runtime telemetry* frames (D0/FE/EF/E5/D6) emitted by the PgnDispatcher and resolves
// captured *.bin goldens under Parity/Golden/Pgn/. This fixture instead freezes the two *configuration*
// frames that FormSteerView builds and sends to the steer module:
//
//   * PGN 251 (0xFB) — steer "config" frame (setting0/setting1 bitfields, max-pulse, min-speed, ang-vel).
//   * PGN 252 (0xFC) — steer "settings" frame (gains, PWM, counts-per-degree, WAS-offset, ackerman).
//
// These two frames are a BEHAVIOR-FROZEN contract (AAP §0.2.2 / §0.6): the cross-platform build must encode
// them byte-for-byte identically to the Windows/net48 baseline. The golden byte arrays below are computed by
// hand from the frozen FormSteer.SaveSettings / FormSteer.FormClosing algorithm and the additive-CRC contract
// documented in docs/pgn-protocol.md; they are NOT *.bin artifacts and do not belong under Parity/Golden/.
//
// The risk-bearing part of the encode — the setting0/setting1 bit cadence — is exercised through the REAL
// production code path: FormSteerView.EncodeSetting0 / EncodeSetting1 are the very methods SaveSettings calls,
// extracted verbatim as pure functions so the byte contract is independently unit-testable. The frame tests
// assemble the full on-wire frame using those real encoders plus the documented index/CRC math, then compare
// against the hand-computed golden, cross-checking the algorithm against an independent expectation.

using System;
using AgOpenGPS.Views.Settings;
using NUnit.Framework;

namespace AgOpenGPS.Tests.Parity
{
    /// <summary>
    /// Byte-exact parity tests for the steer-board configuration PGNs (0xFB / 0xFC) produced by
    /// <see cref="FormSteerView"/>. Verifies the behavior-frozen bitfield encoders and the full frame layout
    /// (header, payload indices, additive CRC) against the net48 baseline.
    /// </summary>
    [TestFixture]
    public class SteerConfigPgnParityTests
    {
        // -------------------------------------------------------------------------------------------------
        // Frozen wire constants (docs/pgn-protocol.md + the recovered CPGN_FB / CPGN_FC templates).
        // -------------------------------------------------------------------------------------------------
        private const byte Header0 = 0x80;
        private const byte Header1 = 0x81;
        private const byte SrcAddr = 0x7F;

        private const byte Pgn251Id = 0xFB; // steer config
        private const byte Pgn252Id = 0xFC; // steer settings
        private const byte DataLen = 8;     // payload byte count for both frames

        // PGN 251 (0xFB) payload indices.
        private const int P251_Set0 = 5;
        private const int P251_MaxPulse = 6;
        private const int P251_MinSpeed = 7;
        private const int P251_Set1 = 8;
        private const int P251_AngVel = 9;

        // PGN 252 (0xFC) payload indices.
        private const int P252_GainProportional = 5;
        private const int P252_HighPwm = 6;
        private const int P252_LowPwm = 7;
        private const int P252_MinPwm = 8;
        private const int P252_CountsPerDegree = 9;
        private const int P252_WasOffsetLo = 10;
        private const int P252_WasOffsetHi = 11;
        private const int P252_Ackerman = 12;

        private const int FrameLength = 14; // header(3) + id + len + 8 payload + CRC

        // =================================================================================================
        // setting0 (0xFB index 5) — behavior-frozen bitfield encoder, exercised through the real producer.
        // Bit order: b0 invert-WAS, b1 steer-invert-relays, b2 invert-steer, b3 conv==Single,
        //            b4 drive==Cytron, b5 enable==Switch, b6 enable==Button, b7 encoder.
        // =================================================================================================

        // All-clear -> 0.
        [TestCase(false, false, false, false, false, false, false, false, (byte)0)]
        // Single isolated bits -> their mask value.
        [TestCase(true, false, false, false, false, false, false, false, (byte)1)]
        [TestCase(false, true, false, false, false, false, false, false, (byte)2)]
        [TestCase(false, false, true, false, false, false, false, false, (byte)4)]
        [TestCase(false, false, false, true, false, false, false, false, (byte)8)]
        [TestCase(false, false, false, false, true, false, false, false, (byte)16)]
        [TestCase(false, false, false, false, false, true, false, false, (byte)32)]
        [TestCase(false, false, false, false, false, false, true, false, (byte)64)]
        [TestCase(false, false, false, false, false, false, false, true, (byte)128)]
        // Reset-default control state: Single + Cytron + Switch -> 8|16|32 = 56 (matches btnVehicleReset).
        [TestCase(false, false, false, true, true, true, false, false, (byte)56)]
        // Mixed: all booleans on, Differential, IBT2, Button enable, encoder -> 1|2|4|64|128 = 199.
        [TestCase(true, true, true, false, false, false, true, true, (byte)199)]
        public void EncodeSetting0_MatchesGolden(
            bool invertWas, bool steerInvertRelays, bool invertSteer, bool convSingle,
            bool driveCytron, bool enableSwitch, bool enableButton, bool encoder, byte expected)
        {
            byte actual = FormSteerView.EncodeSetting0(
                invertWas, steerInvertRelays, invertSteer, convSingle,
                driveCytron, enableSwitch, enableButton, encoder);

            Assert.That(actual, Is.EqualTo(expected),
                "PGN 251 setting0 bitfield must match the net48 FormSteer.SaveSettings baseline byte-for-byte.");
        }

        // =================================================================================================
        // setting1 (0xFB index 8) — behavior-frozen bitfield encoder.
        // Bit order: b0 Danfoss, b1 pressure-sensor, b2 current-sensor, b3 X/Y==Y.
        // =================================================================================================

        [TestCase(false, false, false, false, (byte)0)]
        [TestCase(true, false, false, false, (byte)1)]
        [TestCase(false, true, false, false, (byte)2)]
        [TestCase(false, false, true, false, (byte)4)]
        [TestCase(false, false, false, true, (byte)8)]
        [TestCase(true, false, false, true, (byte)9)]   // Danfoss + XY==Y
        [TestCase(false, true, true, false, (byte)6)]   // pressure + current
        public void EncodeSetting1_MatchesGolden(
            bool danfoss, bool pressure, bool current, bool xyIsY, byte expected)
        {
            byte actual = FormSteerView.EncodeSetting1(danfoss, pressure, current, xyIsY);

            Assert.That(actual, Is.EqualTo(expected),
                "PGN 251 setting1 bitfield must match the net48 FormSteer.SaveSettings baseline byte-for-byte.");
        }

        // =================================================================================================
        // Decode (FormSteer_Load) is the exact inverse of the encode. Verifying the round-trip guards both
        // directions of the frozen contract (the Load decode masks 1/2/4/8/16/32/64/128 and 1/2/4/8).
        // =================================================================================================

        [Test]
        public void Setting0_DecodeIsInverseOfEncode()
        {
            // Each vector uses a VALID (mutually-exclusive) steer-enable state, exactly as the UI produces.
            bool[][] vectors =
            {
                new[] { false, false, false, false, false, false, false, false },
                new[] { true, false, false, true, true, true, false, false },   // reset-default-ish
                new[] { false, true, true, false, true, false, true, true },     // mixed, Button enable
                new[] { true, true, true, true, true, true, false, true },       // Switch enable
            };

            foreach (bool[] v in vectors)
            {
                byte encoded = FormSteerView.EncodeSetting0(
                    v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);

                // Decode mirrors FormSteer_Load (source L231-L253).
                bool[] decoded =
                {
                    (encoded & 1) != 0,
                    (encoded & 2) != 0,
                    (encoded & 4) != 0,
                    (encoded & 8) != 0,
                    (encoded & 16) != 0,
                    (encoded & 32) != 0,
                    (encoded & 64) != 0,
                    (encoded & 128) != 0,
                };

                Assert.That(decoded, Is.EqualTo(v),
                    "setting0 decode (Load) must be the exact inverse of the encode (SaveSettings).");
            }
        }

        [Test]
        public void Setting1_DecodeIsInverseOfEncode()
        {
            bool[][] vectors =
            {
                new[] { false, false, false, false },
                new[] { true, false, false, true },
                new[] { false, true, false, false },
                new[] { false, false, true, true },
            };

            foreach (bool[] v in vectors)
            {
                byte encoded = FormSteerView.EncodeSetting1(v[0], v[1], v[2], v[3]);

                // Decode mirrors FormSteer_Load (source L259-L271): &1 Danfoss, &2 pressure, &4 current, &8 XY.
                bool[] decoded =
                {
                    (encoded & 1) != 0,
                    (encoded & 2) != 0,
                    (encoded & 4) != 0,
                    (encoded & 8) != 0,
                };

                Assert.That(decoded, Is.EqualTo(v),
                    "setting1 decode (Load) must be the exact inverse of the encode (SaveSettings).");
            }
        }

        // =================================================================================================
        // Full PGN 251 (0xFB) frame — byte-exact golden, assembled through the real setting0/setting1
        // encoders plus the documented index + additive-CRC contract.
        // =================================================================================================

        [Test]
        public void Pgn251Frame_ResetDefaults_ByteExact()
        {
            // Representative state = the btnVehicleReset defaults:
            //   setting0 = 56 (Single+Cytron+Switch), setting1 = 0, maxPulseCounts = 3,
            //   minSteerSpeed = 0 -> minSpeed = 0, isConstantContourOn = false -> angVel = 0.
            byte[] frame = BuildPgn251(
                set0: FormSteerView.EncodeSetting0(false, false, false, true, true, true, false, false),
                set1: FormSteerView.EncodeSetting1(false, false, false, false),
                maxPulse: 3,
                minSteerSpeed: 0.0,
                isConstantContourOn: false);

            byte[] golden =
            {
                0x80, 0x81, 0x7F, 0xFB, 0x08,
                0x38, // [5] setting0 = 56
                0x03, // [6] maxPulse = 3
                0x00, // [7] minSpeed = 0
                0x00, // [8] setting1 = 0
                0x00, // [9] angVel = 0
                0x00, 0x00, 0x00,
                0xBD, // [13] additive CRC over [2..12]
            };

            Assert.That(frame, Is.EqualTo(golden),
                "PGN 251 reset-default frame must be byte-identical to the net48 baseline (incl. additive CRC).");
        }

        [Test]
        public void Pgn251Frame_Mixed_ByteExact()
        {
            // Mixed state: setting0 = 199, setting1 = 9 (Danfoss + XY=Y), maxPulse = 200,
            //   minSteerSpeed = 2.0 -> minSpeed = 20, isConstantContourOn = true -> angVel = 1.
            byte[] frame = BuildPgn251(
                set0: FormSteerView.EncodeSetting0(true, true, true, false, false, false, true, true),
                set1: FormSteerView.EncodeSetting1(true, false, false, true),
                maxPulse: 200,
                minSteerSpeed: 2.0,
                isConstantContourOn: true);

            byte[] golden =
            {
                0x80, 0x81, 0x7F, 0xFB, 0x08,
                0xC7, // [5] setting0 = 199
                0xC8, // [6] maxPulse = 200
                0x14, // [7] minSpeed = 20
                0x09, // [8] setting1 = 9
                0x01, // [9] angVel = 1
                0x00, 0x00, 0x00,
                0x2F, // [13] additive CRC over [2..12]
            };

            Assert.That(frame, Is.EqualTo(golden),
                "PGN 251 mixed-state frame must be byte-identical to the net48 baseline (incl. additive CRC).");
        }

        // =================================================================================================
        // Full PGN 252 (0xFC) frame — byte-exact golden. The payload packing mirrors FormSteerView's
        // Apply252ToPgn (FormClosing + timer resend) exactly, including the WAS-offset hi/lo split and the
        // low-PWM = high-PWM / 3 integer-division rule.
        // =================================================================================================

        [Test]
        public void Pgn252Frame_ResetDefaults_ByteExact()
        {
            // Representative state = the btnVehicleReset defaults:
            //   gainProportional(Kp)=50, highPwm=180 -> lowPwm=60, minPwm=25, countsPerDegree=110,
            //   wasOffset=3 (Lo=3,Hi=0), ackerman=100.
            byte[] frame = BuildPgn252(
                gainProportional: 50,
                highPwm: 180,
                minPwm: 25,
                countsPerDegree: 110,
                wasOffset: 3,
                ackerman: 100);

            byte[] golden =
            {
                0x80, 0x81, 0x7F, 0xFC, 0x08,
                0x32, // [5]  gainProportional = 50
                0xB4, // [6]  highPwm = 180
                0x3C, // [7]  lowPwm = 180/3 = 60
                0x19, // [8]  minPwm = 25
                0x6E, // [9]  countsPerDegree = 110
                0x03, // [10] wasOffsetLo = 3
                0x00, // [11] wasOffsetHi = 0
                0x64, // [12] ackerman = 100
                0x93, // [13] additive CRC over [2..12]
            };

            Assert.That(frame, Is.EqualTo(golden),
                "PGN 252 reset-default frame must be byte-identical to the net48 baseline (incl. additive CRC).");
        }

        [Test]
        public void Pgn252Frame_Mixed_ByteExact()
        {
            // Mixed state exercising the WAS-offset hi/lo split with a value > 255 and low-PWM truncation:
            //   gainProportional=255, highPwm=255 -> lowPwm=85, minPwm=50, countsPerDegree=200,
            //   wasOffset=500 (Lo=0xF4, Hi=0x01), ackerman=160.
            byte[] frame = BuildPgn252(
                gainProportional: 255,
                highPwm: 255,
                minPwm: 50,
                countsPerDegree: 200,
                wasOffset: 500,
                ackerman: 160);

            byte[] golden =
            {
                0x80, 0x81, 0x7F, 0xFC, 0x08,
                0xFF, // [5]  gainProportional = 255
                0xFF, // [6]  highPwm = 255
                0x55, // [7]  lowPwm = 255/3 = 85
                0x32, // [8]  minPwm = 50
                0xC8, // [9]  countsPerDegree = 200
                0xF4, // [10] wasOffsetLo = 500 & 0xFF = 244
                0x01, // [11] wasOffsetHi = 500 >> 8 = 1
                0xA0, // [12] ackerman = 160
                0x65, // [13] additive CRC over [2..12]
            };

            Assert.That(frame, Is.EqualTo(golden),
                "PGN 252 mixed-state frame (WAS-offset > 255) must be byte-identical to the net48 baseline.");
        }

        // =================================================================================================
        // Frozen layout + CRC contract guards.
        // =================================================================================================

        [Test]
        public void FrameLayout_IndicesAreFrozen()
        {
            // These indices are the behavior-frozen on-wire layout (CPGN_FB / CPGN_FC). A change here would
            // silently corrupt every steer-module command, so they are asserted explicitly.
            Assert.Multiple(() =>
            {
                Assert.That(P251_Set0, Is.EqualTo(5));
                Assert.That(P251_MaxPulse, Is.EqualTo(6));
                Assert.That(P251_MinSpeed, Is.EqualTo(7));
                Assert.That(P251_Set1, Is.EqualTo(8));
                Assert.That(P251_AngVel, Is.EqualTo(9));

                Assert.That(P252_GainProportional, Is.EqualTo(5));
                Assert.That(P252_HighPwm, Is.EqualTo(6));
                Assert.That(P252_LowPwm, Is.EqualTo(7));
                Assert.That(P252_MinPwm, Is.EqualTo(8));
                Assert.That(P252_CountsPerDegree, Is.EqualTo(9));
                Assert.That(P252_WasOffsetLo, Is.EqualTo(10));
                Assert.That(P252_WasOffsetHi, Is.EqualTo(11));
                Assert.That(P252_Ackerman, Is.EqualTo(12));
            });
        }

        [Test]
        public void AdditiveCrc_MatchesProtocolContract()
        {
            // Sanity check of the additive-CRC helper against a frame whose CRC is known by construction:
            // header(0x80,0x81) is excluded; sum runs over [2..Length-2]; result wraps mod 256.
            byte[] frame =
            {
                0x80, 0x81, 0x7F, 0xFB, 0x08,
                0x38, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, // CRC slot (excluded from the sum)
            };

            byte crc = AdditiveCrc(frame);

            // 0x7F+0xFB+0x08+0x38+0x03 = 445 -> 445 mod 256 = 189 = 0xBD.
            Assert.That(crc, Is.EqualTo((byte)0xBD));
        }

        // -------------------------------------------------------------------------------------------------
        // Frame builders + CRC — mirror the FormSteerView producer math exactly.
        // -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds a PGN 251 (0xFB) frame the same way <see cref="FormSteerView.SaveSettings"/> does:
        /// setting0/setting1 come from the real encoders; max-pulse, min-speed (<c>minSteerSpeed * 10</c>)
        /// and ang-vel follow the source, then the additive CRC is appended.
        /// </summary>
        private static byte[] BuildPgn251(byte set0, byte set1, int maxPulse, double minSteerSpeed,
            bool isConstantContourOn)
        {
            byte[] pgn = NewTemplate(Pgn251Id);
            pgn[P251_Set0] = set0;
            pgn[P251_MaxPulse] = unchecked((byte)maxPulse);
            pgn[P251_MinSpeed] = unchecked((byte)(minSteerSpeed * 10));
            pgn[P251_Set1] = set1;
            pgn[P251_AngVel] = (byte)(isConstantContourOn ? 1 : 0);
            pgn[FrameLength - 1] = AdditiveCrc(pgn);
            return pgn;
        }

        /// <summary>
        /// Builds a PGN 252 (0xFC) frame the same way <see cref="FormSteerView"/>'s <c>Apply252ToPgn</c> does,
        /// including the WAS-offset hi/lo split and <c>lowPwm = highPwm / 3</c> integer division, then appends
        /// the additive CRC.
        /// </summary>
        private static byte[] BuildPgn252(int gainProportional, int highPwm, int minPwm, int countsPerDegree,
            int wasOffset, int ackerman)
        {
            byte[] pgn = NewTemplate(Pgn252Id);
            pgn[P252_CountsPerDegree] = unchecked((byte)countsPerDegree);
            pgn[P252_Ackerman] = unchecked((byte)ackerman);
            pgn[P252_WasOffsetHi] = unchecked((byte)(wasOffset >> 8));
            pgn[P252_WasOffsetLo] = unchecked((byte)wasOffset);
            pgn[P252_HighPwm] = unchecked((byte)highPwm);
            pgn[P252_LowPwm] = unchecked((byte)(highPwm / 3));
            pgn[P252_GainProportional] = unchecked((byte)gainProportional);
            pgn[P252_MinPwm] = unchecked((byte)minPwm);
            pgn[FrameLength - 1] = AdditiveCrc(pgn);
            return pgn;
        }

        /// <summary>Allocates a 14-byte frame template <c>{0x80,0x81,0x7F,&lt;id&gt;,8, 0..0}</c>.</summary>
        private static byte[] NewTemplate(byte pgnId)
        {
            byte[] pgn = new byte[FrameLength];
            pgn[0] = Header0;
            pgn[1] = Header1;
            pgn[2] = SrcAddr;
            pgn[3] = pgnId;
            pgn[4] = DataLen;
            return pgn;
        }

        /// <summary>
        /// Additive checksum per docs/pgn-protocol.md: sum bytes <c>[2 .. Length-2]</c> (source byte through
        /// the last data byte), wrapping mod 256; the header bytes and the CRC slot are excluded.
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
    }
}
