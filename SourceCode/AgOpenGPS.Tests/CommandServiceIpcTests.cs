using System;
using System.Text;
using System.Threading.Tasks;
using AgOpenGPS.Ipc;
using Grpc.Core;
using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    /// <summary>
    /// Test Area 3 — validates the unary <c>CommandService</c> contract that replaces the legacy
    /// fire-and-forget UDP send with an explicit delivery confirmation, AND the firmware-facing byte
    /// frames that AgIO forwards to the hardware for every command.
    ///
    /// <para>
    /// A full <c>GrpcChannel</c> client-to-server round-trip would require ASP.NET Core / Kestrel
    /// (net6+), which this test project deliberately does not reference (only <c>AgOpenGPS.Ipc</c>).
    /// Two complementary approaches are therefore used:
    /// </para>
    /// <list type="number">
    ///   <item>Acknowledgement contract — a self-contained in-process stub derived from the generated
    ///   <see cref="CommandService.CommandServiceBase"/> is invoked directly, proving every <c>Send*</c>
    ///   RPC returns a <see cref="CommandAck"/> (and carries an error message on rejection).</item>
    ///   <item>Byte contract — the hardware frame construction and additive CRC were extracted out of the
    ///   AgIO WinExe into the shared, referenceable <see cref="CommandFrameBuilder"/>; these tests assert
    ///   the exact PGN byte, length byte, payload offsets, and additive CRC for all 14 command frames.
    ///   This closes the coverage gap that previously let the PGN 0x64 corrected-position byte-contract
    ///   regression (a shortened frame for the sentinel heading) slip through undetected.</item>
    /// </list>
    /// </summary>
    public class CommandServiceIpcTests
    {
        // ---------------------------------------------------------------------------------------------
        // Acknowledgement-contract tests (in-process stub; preserved from the original fixture).
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// In-process <see cref="CommandService.CommandServiceBase"/> whose overrides acknowledge
        /// every command positively, representing the AgIO server "happy path". The
        /// <see cref="ServerCallContext"/> argument is unused because the RPC is invoked directly
        /// rather than through a real gRPC channel.
        /// </summary>
        private sealed class InProcessCommandService : CommandService.CommandServiceBase
        {
            public override Task<CommandAck> SendAutoSteerData(AutoSteerDataMsg request, ServerCallContext context)
            {
                return Task.FromResult(new CommandAck { Received = true });
            }

            public override Task<CommandAck> SendMachineData(MachineDataMsg request, ServerCallContext context)
            {
                return Task.FromResult(new CommandAck { Received = true });
            }

            public override Task<CommandAck> SendProcessData(ProcessDataMsg request, ServerCallContext context)
            {
                return Task.FromResult(new CommandAck { Received = true });
            }

            public override Task<CommandAck> SendFieldName(FieldNameMsg request, ServerCallContext context)
            {
                return Task.FromResult(new CommandAck { Received = true });
            }
        }

        /// <summary>
        /// In-process <see cref="CommandService.CommandServiceBase"/> that rejects the command,
        /// demonstrating that the acknowledgement carries the failure detail the fire-and-forget
        /// UDP path could never surface.
        /// </summary>
        private sealed class RejectingCommandService : CommandService.CommandServiceBase
        {
            public override Task<CommandAck> SendAutoSteerData(AutoSteerDataMsg request, ServerCallContext context)
            {
                return Task.FromResult(new CommandAck { Received = false, ErrorMessage = "rejected" });
            }
        }

        [Test]
        public async Task SendAutoSteerData_ReturnsAck_Received()
        {
            // Arrange
            var service = new InProcessCommandService();
            var request = new AutoSteerDataMsg { Speed = 1234, Status = 1, CommandedSteerAngle = -250 };

            // Act
            CommandAck ack = await service.SendAutoSteerData(request, null);

            // Assert
            Assert.That(ack, Is.Not.Null);
            Assert.That(ack.Received, Is.True);
            Assert.That(ack.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task SendMachineData_ReturnsAck_Received()
        {
            // Arrange
            var service = new InProcessCommandService();
            var request = new MachineDataMsg { Speed = 55, UTurn = 1, HydLift = 2, Tram = 3, GeoStop = 0 };

            // Act
            CommandAck ack = await service.SendMachineData(request, null);

            // Assert
            Assert.That(ack, Is.Not.Null);
            Assert.That(ack.Received, Is.True);
            Assert.That(ack.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task SendProcessData_ReturnsAck_Received()
        {
            // Arrange
            var service = new InProcessCommandService();
            var request = new ProcessDataMsg { Identifier = 513, Value = -1200 };

            // Act
            CommandAck ack = await service.SendProcessData(request, null);

            // Assert
            Assert.That(ack, Is.Not.Null);
            Assert.That(ack.Received, Is.True);
            Assert.That(ack.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task SendFieldName_ReturnsAck_Received()
        {
            // Arrange
            var service = new InProcessCommandService();
            var request = new FieldNameMsg { Name = "North Paddock" };

            // Act
            CommandAck ack = await service.SendFieldName(request, null);

            // Assert
            Assert.That(ack, Is.Not.Null);
            Assert.That(ack.Received, Is.True);
            Assert.That(ack.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task SendAutoSteerData_WhenRejected_ReturnsAck_WithError()
        {
            // Arrange
            var service = new RejectingCommandService();
            var request = new AutoSteerDataMsg { Speed = 0, Status = 0, CommandedSteerAngle = 0 };

            // Act
            CommandAck ack = await service.SendAutoSteerData(request, null);

            // Assert
            Assert.That(ack, Is.Not.Null);
            Assert.That(ack.Received, Is.False);
            Assert.That(ack.ErrorMessage, Is.EqualTo("rejected"));
        }

        // ---------------------------------------------------------------------------------------------
        // Byte-contract test helpers.
        //
        // Every AgIO Send* handler delegates its hardware frame construction to CommandFrameBuilder and
        // its CRC to CommandFrameBuilder.ApplyCrc (invoked centrally in CommandServiceImpl.Forward).
        // Asserting the builder output is therefore an exact assertion of the firmware-facing byte
        // contract that leaves AgIO. The legacy frame is [0x80, 0x81, 0x7F, PGN, Length, Data..., CRC].
        // ---------------------------------------------------------------------------------------------

        /// <summary>Asserts the fixed frame header, the PGN byte, the length byte, and the total frame size.</summary>
        private static void AssertFrameHeader(byte[] frame, byte expectedPgn, int expectedDataLength)
        {
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame.Length, Is.EqualTo(5 + expectedDataLength + 1),
                "frame length must be 5 header/length bytes + data bytes + 1 CRC byte");
            Assert.That(frame[0], Is.EqualTo((byte)0x80), "header byte 0");
            Assert.That(frame[1], Is.EqualTo((byte)0x81), "header byte 1");
            Assert.That(frame[2], Is.EqualTo((byte)0x7F), "header byte 2");
            Assert.That(frame[3], Is.EqualTo(expectedPgn), "PGN byte");
            Assert.That(frame[4], Is.EqualTo((byte)expectedDataLength), "length byte");
        }

        /// <summary>
        /// Independently re-implements the legacy additive CRC (sum of bytes 2..N-2), applies the
        /// production CRC via <see cref="CommandFrameBuilder.ApplyCrc(byte[])"/>, and asserts the final
        /// byte matches. This guards both the CRC formula and the byte range it covers.
        /// </summary>
        private static void AssertAdditiveCrc(byte[] frame)
        {
            byte expected = 0;
            for (int i = 2; i < frame.Length - 1; i++)
            {
                expected += frame[i];
            }
            CommandFrameBuilder.ApplyCrc(frame);
            Assert.That(frame[frame.Length - 1], Is.EqualTo(expected), "additive CRC in final byte");
        }

        /// <summary>Returns a copy of <paramref name="length"/> bytes from <paramref name="source"/> starting at <paramref name="start"/>.</summary>
        private static byte[] Slice(byte[] source, int start, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(source, start, result, 0, length);
            return result;
        }

        // ---------------------------------------------------------------------------------------------
        // Byte-contract tests — all 14 command frames.
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void BuildAutoSteerData_ProducesPgn0xFE_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new AutoSteerDataMsg
            {
                Speed = 1234, // 0x04D2 -> lo 0xD2 @5, hi 0x04 @6
                Status = 2,
                SectionControl18 = 0xAB,
                SectionControl916 = 0xCD
            };
            byte[] frame = CommandFrameBuilder.BuildAutoSteerData(request);

            // Assert
            AssertFrameHeader(frame, 0xFE, 8);
            Assert.That(frame[5], Is.EqualTo((byte)0xD2));
            Assert.That(frame[6], Is.EqualTo((byte)0x04));
            Assert.That(frame[7], Is.EqualTo((byte)2));
            Assert.That(frame[11], Is.EqualTo((byte)0xAB));
            Assert.That(frame[12], Is.EqualTo((byte)0xCD));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildAutoSteerSettings_ProducesPgn0xFC_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new AutoSteerSettingsMsg
            {
                GainProportionalKp = 10,
                HighPwm = 200,
                WasOffset = 1234, // 0x04D2 -> lo 0xD2 @10, hi 0x04 @11
                Ackerman = 50
            };
            byte[] frame = CommandFrameBuilder.BuildAutoSteerSettings(request);

            // Assert
            AssertFrameHeader(frame, 0xFC, 8);
            Assert.That(frame[5], Is.EqualTo((byte)10));
            Assert.That(frame[6], Is.EqualTo((byte)200));
            Assert.That(frame[10], Is.EqualTo((byte)0xD2));
            Assert.That(frame[11], Is.EqualTo((byte)0x04));
            Assert.That(frame[12], Is.EqualTo((byte)50));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildAutoSteerConfig_ProducesPgn0xFB_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new AutoSteerConfigMsg
            {
                Set0 = 1,
                MaxPulse = 99,
                AngularVelocity = 42
            };
            byte[] frame = CommandFrameBuilder.BuildAutoSteerConfig(request);

            // Assert
            AssertFrameHeader(frame, 0xFB, 8);
            Assert.That(frame[5], Is.EqualTo((byte)1));
            Assert.That(frame[6], Is.EqualTo((byte)99));
            Assert.That(frame[9], Is.EqualTo((byte)42));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildMachineData_ProducesPgn0xEF_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new MachineDataMsg
            {
                UTurn = 1,
                Speed = 55,
                SectionControl18 = 0x0F,
                SectionControl916 = 0xF0
            };
            byte[] frame = CommandFrameBuilder.BuildMachineData(request);

            // Assert
            AssertFrameHeader(frame, 0xEF, 8);
            Assert.That(frame[5], Is.EqualTo((byte)1));
            Assert.That(frame[6], Is.EqualTo((byte)55));
            // byte 10 is intentionally skipped by the legacy layout and stays zero.
            Assert.That(frame[10], Is.EqualTo((byte)0));
            Assert.That(frame[11], Is.EqualTo((byte)0x0F));
            Assert.That(frame[12], Is.EqualTo((byte)0xF0));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildMachineConfig_ProducesPgn0xEE_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new MachineConfigMsg
            {
                RaiseTime = 20,
                LowerTime = 30,
                User4 = 9
            };
            byte[] frame = CommandFrameBuilder.BuildMachineConfig(request);

            // Assert
            AssertFrameHeader(frame, 0xEE, 8);
            Assert.That(frame[5], Is.EqualTo((byte)20));
            Assert.That(frame[6], Is.EqualTo((byte)30));
            Assert.That(frame[12], Is.EqualTo((byte)9));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildRelayConfig_ProducesPgn0xEC_30ByteFrame_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new RelayConfigMsg { PinConfig = { 11u, 22u, 33u } };
            byte[] frame = CommandFrameBuilder.BuildRelayConfig(request);

            // Assert — 30-byte frame, 24 data bytes (pin map)
            AssertFrameHeader(frame, 0xEC, 24);
            Assert.That(frame.Length, Is.EqualTo(30));
            Assert.That(frame[5], Is.EqualTo((byte)11));
            Assert.That(frame[6], Is.EqualTo((byte)22));
            Assert.That(frame[7], Is.EqualTo((byte)33));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildSectionDimensions_ProducesPgn0xEB_39ByteFrame_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new SectionDimensionsMsg
            {
                SectionWidths = { 300u, 150u }, // 300 = 0x012C, 150 = 0x0096
                NumSections = 2
            };
            byte[] frame = CommandFrameBuilder.BuildSectionDimensions(request);

            // Assert — 39-byte frame, 33 data bytes
            AssertFrameHeader(frame, 0xEB, 33);
            Assert.That(frame.Length, Is.EqualTo(39));
            Assert.That(frame[5], Is.EqualTo((byte)0x2C)); // width0 lo
            Assert.That(frame[6], Is.EqualTo((byte)0x01)); // width0 hi
            Assert.That(frame[7], Is.EqualTo((byte)0x96)); // width1 lo
            Assert.That(frame[8], Is.EqualTo((byte)0x00)); // width1 hi
            Assert.That(frame[37], Is.EqualTo((byte)2));    // num sections
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildExtendedSectionControl_ProducesPgn0xE5_WithSectionsAndCrc()
        {
            // Arrange / Act
            ulong sections = 0x1122334455667788UL;
            var request = new ExtendedSectionControlMsg
            {
                Sections = sections,
                ToolLeftSpeed = 3,
                ToolRightSpeed = 4
            };
            byte[] frame = CommandFrameBuilder.BuildExtendedSectionControl(request);

            // Assert — 16-byte frame, 10 data bytes (8 section bytes + 2 tool speeds)
            AssertFrameHeader(frame, 0xE5, 10);
            Assert.That(Slice(frame, 5, 8), Is.EqualTo(BitConverter.GetBytes(sections)));
            Assert.That(frame[13], Is.EqualTo((byte)3));
            Assert.That(frame[14], Is.EqualTo((byte)4));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildRateControl_ProducesPgn0xE4_WithOffsetsAndCrc()
        {
            // Arrange / Act
            var request = new RateControlMsg { Rate0 = 5, Rate1 = 6, Rate2 = 7 };
            byte[] frame = CommandFrameBuilder.BuildRateControl(request);

            // Assert
            AssertFrameHeader(frame, 0xE4, 8);
            Assert.That(frame[5], Is.EqualTo((byte)5));
            Assert.That(frame[6], Is.EqualTo((byte)6));
            Assert.That(frame[7], Is.EqualTo((byte)7));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildSectionControlEnable_ProducesPgn0xF1_ForEnabledAndDisabled()
        {
            // Enabled
            byte[] enabled = CommandFrameBuilder.BuildSectionControlEnable(new SectionControlEnableMsg { Enabled = true });
            AssertFrameHeader(enabled, 0xF1, 1);
            Assert.That(enabled.Length, Is.EqualTo(7));
            Assert.That(enabled[5], Is.EqualTo((byte)1));
            AssertAdditiveCrc(enabled);

            // Disabled
            byte[] disabled = CommandFrameBuilder.BuildSectionControlEnable(new SectionControlEnableMsg { Enabled = false });
            AssertFrameHeader(disabled, 0xF1, 1);
            Assert.That(disabled[5], Is.EqualTo((byte)0));
            AssertAdditiveCrc(disabled);
        }

        [Test]
        public void BuildProcessData_ProducesPgn0xF2_WithIdentifierValueAndCrc()
        {
            // Arrange / Act
            int value = -1200;
            var request = new ProcessDataMsg { Identifier = 513, Value = value }; // 513 = 0x0201
            byte[] frame = CommandFrameBuilder.BuildProcessData(request);

            // Assert — 12-byte frame, 6 data bytes (2 id + 4 value)
            AssertFrameHeader(frame, 0xF2, 6);
            Assert.That(frame[5], Is.EqualTo((byte)0x01)); // id lo
            Assert.That(frame[6], Is.EqualTo((byte)0x02)); // id hi
            Assert.That(Slice(frame, 7, 4), Is.EqualTo(BitConverter.GetBytes(value)));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildFieldName_ProducesPgn0xF3_WithUtf8PayloadAndCrc()
        {
            // Arrange / Act
            const string name = "North Paddock";
            byte[] expectedName = Encoding.UTF8.GetBytes(name);
            byte[] frame = CommandFrameBuilder.BuildFieldName(new FieldNameMsg { Name = name });

            // Assert
            AssertFrameHeader(frame, 0xF3, expectedName.Length);
            Assert.That(Slice(frame, 5, expectedName.Length), Is.EqualTo(expectedName));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildFieldName_EmptyName_ProducesFieldClosedFrame()
        {
            // Arrange / Act — empty name signals "field closed"; length byte 0, 6-byte frame.
            byte[] frame = CommandFrameBuilder.BuildFieldName(new FieldNameMsg { Name = string.Empty });

            // Assert
            AssertFrameHeader(frame, 0xF3, 0);
            Assert.That(frame.Length, Is.EqualTo(6));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildFieldName_OverLongName_ClampsTo248Bytes()
        {
            // Arrange / Act — names longer than 248 bytes are clamped (ISOBUS field-name limit).
            var request = new FieldNameMsg { Name = new string('A', 300) };
            byte[] frame = CommandFrameBuilder.BuildFieldName(request);

            // Assert
            AssertFrameHeader(frame, 0xF3, 248);
            Assert.That(frame.Length, Is.EqualTo(5 + 248 + 1));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildLatLon_ProducesPgn0xD0_WithEncodedOffsetsAndCrc()
        {
            // Arrange / Act — latitude at offset 5-8, longitude at offset 9-12.
            int lat = 1000000;
            int lon = -2000000;
            var request = new LatLonMsg { LatitudeEncoded = lat, LongitudeEncoded = lon };
            byte[] frame = CommandFrameBuilder.BuildLatLon(request);

            // Assert
            AssertFrameHeader(frame, 0xD0, 8);
            Assert.That(Slice(frame, 5, 4), Is.EqualTo(BitConverter.GetBytes(lat)));
            Assert.That(Slice(frame, 9, 4), Is.EqualTo(BitConverter.GetBytes(lon)));
            AssertAdditiveCrc(frame);
        }

        // ---------------------------------------------------------------------------------------------
        // PGN 0x64 corrected-position byte-contract regression tests (the critical finding).
        //
        // The authoritative legacy producer (GPS Position.designer.cs) ALWAYS emits a 30-byte frame with
        // 24 data bytes: longitude @5-12, latitude @13-20, fix2fix heading @21-28 — including the sentinel
        // heading value 1000 (invalid heading). An earlier revision emitted a shorter 16-data-byte frame
        // and dropped the heading for the sentinel case, changing the firmware-facing byte contract.
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void BuildCorrectedPosition_WithValidHeading_Emits30ByteFrameWithHeadingAndCrc()
        {
            // Arrange / Act
            double lon = 12.3456789;
            double lat = 56.7890123;
            double heading = 90.5;
            var request = new CorrectedPositionMsg { Longitude = lon, Latitude = lat, Fix2FixHeading = heading };
            byte[] frame = CommandFrameBuilder.BuildCorrectedPosition(request);

            // Assert — always 30-byte / 24-data-byte frame
            AssertFrameHeader(frame, 0x64, 24);
            Assert.That(frame.Length, Is.EqualTo(30));
            Assert.That(Slice(frame, 5, 8), Is.EqualTo(BitConverter.GetBytes(lon)));
            Assert.That(Slice(frame, 13, 8), Is.EqualTo(BitConverter.GetBytes(lat)));
            Assert.That(Slice(frame, 21, 8), Is.EqualTo(BitConverter.GetBytes(heading)));
            AssertAdditiveCrc(frame);
        }

        [Test]
        public void BuildCorrectedPosition_WithSentinelHeading_StillEmitsFull24BytePayload()
        {
            // Arrange / Act — the regression case: sentinel heading 1000 (invalid) must NOT shorten the frame.
            double lon = -1.5;
            double lat = 2.5;
            const double sentinel = 1000.0;
            var request = new CorrectedPositionMsg { Longitude = lon, Latitude = lat, Fix2FixHeading = sentinel };
            byte[] frame = CommandFrameBuilder.BuildCorrectedPosition(request);

            // Assert — length byte is still 24, frame is still 30 bytes, and the sentinel heading is
            // still written verbatim at offset 21-28 (this is the exact byte-contract regression guard).
            AssertFrameHeader(frame, 0x64, 24);
            Assert.That(frame.Length, Is.EqualTo(30));
            Assert.That(Slice(frame, 5, 8), Is.EqualTo(BitConverter.GetBytes(lon)));
            Assert.That(Slice(frame, 13, 8), Is.EqualTo(BitConverter.GetBytes(lat)));
            Assert.That(Slice(frame, 21, 8), Is.EqualTo(BitConverter.GetBytes(sentinel)),
                "sentinel heading 1000 must be preserved at offset 21-28, not dropped");
            AssertAdditiveCrc(frame);
        }

        // ---------------------------------------------------------------------------------------------
        // InjectTelemetry validation contract (AAP 0.6.3). The accept/reject predicate is extracted into
        // AgOpenGPS.Ipc.TelemetryInjectionValidator so it is testable from this net48 project; the AgIO
        // CommandServiceImpl.InjectTelemetry delegates to it before fanning out to subscribers.
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void InjectTelemetry_Validator_AcceptsWellFormedEnvelope()
        {
            // Arrange
            var envelope = new PgnEnvelope
            {
                SchemaVersion = IpcConstants.SchemaVersion,
                GpsPosition = new GpsPositionMsg { Latitude = 1.0, Longitude = 2.0 }
            };

            // Act
            bool valid = TelemetryInjectionValidator.TryValidate(envelope, out string error);

            // Assert
            Assert.That(valid, Is.True);
            Assert.That(error, Is.Empty);
        }

        [Test]
        public void InjectTelemetry_Validator_RejectsNullEnvelope()
        {
            // Act
            bool valid = TelemetryInjectionValidator.TryValidate(null, out string error);

            // Assert
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("null"));
        }

        [Test]
        public void InjectTelemetry_Validator_RejectsUnsupportedSchemaVersion()
        {
            // Arrange — a version the server was not built against.
            var envelope = new PgnEnvelope
            {
                SchemaVersion = IpcConstants.SchemaVersion + 99,
                GpsPosition = new GpsPositionMsg { Latitude = 1.0, Longitude = 2.0 }
            };

            // Act
            bool valid = TelemetryInjectionValidator.TryValidate(envelope, out string error);

            // Assert
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("schema version"));
        }

        [Test]
        public void InjectTelemetry_Validator_RejectsEmptyPayload()
        {
            // Arrange — correct version but no oneof payload set.
            var envelope = new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion };

            // Act
            bool valid = TelemetryInjectionValidator.TryValidate(envelope, out string error);

            // Assert
            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("payload"));
            Assert.That(envelope.PayloadCase, Is.EqualTo(PgnEnvelope.PayloadOneofCase.None));
        }
    }
}
