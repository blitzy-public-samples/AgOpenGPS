using System.Collections.Generic;
using AgOpenGPS.Ipc;
using Google.Protobuf;
using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    /// <summary>
    /// Proto round-trip fixture (Test Area 1 of the IPC test mandate). For each of the
    /// 23 PGN message types it builds a <c>PgnEnvelope</c> with
    /// <c>SchemaVersion = IpcConstants.SchemaVersion</c>, serializes it via
    /// <c>ToByteArray()</c>, re-parses it via <c>PgnEnvelope.Parser.ParseFrom(bytes)</c>,
    /// and asserts the correct payload oneof case plus whole-message (field-by-field)
    /// equality. Together these prove the 23-message catalog survives the proto3
    /// serialization boundary with zero payload-semantic loss.
    /// </summary>
    public class ProtoRoundTripIpcTests
    {
        /// <summary>
        /// Yields exactly one <see cref="TestCaseData"/> per PGN message type (23 total),
        /// each carrying a pre-built envelope and its expected payload oneof case. Every
        /// envelope stamps the shared schema version and populates the available payload
        /// fields with non-default values so the equality assertion is meaningful.
        /// </summary>
        private static IEnumerable<TestCaseData> EnvelopeCases()
        {
            // 01) PGN 0xD6 - GPS Position (AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    GpsPosition = new GpsPositionMsg { Latitude = 12.3456, Longitude = -98.7654, Satellites = 12, FixQuality = 4 }
                },
                PgnEnvelope.PayloadOneofCase.GpsPosition)
                .SetName("RoundTrip_01_GpsPosition_D6");

            // 02) PGN 0xD3 - External IMU (AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    ExternalImu = new ExternalImuMsg { Heading = 1800, Roll = 50, AngularVelocity = -30 }
                },
                PgnEnvelope.PayloadOneofCase.ExternalImu)
                .SetName("RoundTrip_02_ExternalImu_D3");

            // 03) PGN 0xD4 - IMU Disconnect (AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    ImuDisconnect = new ImuDisconnectMsg { IsDisconnected = true, RawValue = 1 }
                },
                PgnEnvelope.PayloadOneofCase.ImuDisconnect)
                .SetName("RoundTrip_03_ImuDisconnect_D4");

            // 04) PGN 0xFD - Steer Module Response (AgIO -> AOG)
            // Heading/Roll carry raw values with the typed validity flags set true (i.e. NOT the
            // legacy 9999/8888 "N/A" sentinels), proving the bool+value sentinel fields round-trip.
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    SteerModuleResponse = new SteerModuleResponseMsg
                    {
                        ActualSteerAngle = -125,
                        Heading = 1234,
                        Roll = -567,
                        SwitchStatus = 3,
                        Pwm = 180,
                        HeadingValid = true,
                        RollValid = true
                    }
                },
                PgnEnvelope.PayloadOneofCase.SteerModuleResponse)
                .SetName("RoundTrip_04_SteerModuleResponse_FD");

            // 05) PGN 0xFA - Sensor Data (AgIO -> AOG); single payload field
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    SensorData = new SensorDataMsg { SensorData = 42 }
                },
                PgnEnvelope.PayloadOneofCase.SensorData)
                .SetName("RoundTrip_05_SensorData_FA");

            // 06) PGN 0xEA - Remote Switches (AgIO -> AOG); single payload field
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    RemoteSwitch = new RemoteSwitchMsg { SwitchData = ByteString.CopyFrom(new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 }) }
                },
                PgnEnvelope.PayloadOneofCase.RemoteSwitch)
                .SetName("RoundTrip_06_RemoteSwitch_EA");

            // 07) PGN 0xDD - Display Message (AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    DisplayHardware = new DisplayHardwareMsg { DisplayTime = 5, Color = 1, Message = "Hello" }
                },
                PgnEnvelope.PayloadOneofCase.DisplayHardware)
                .SetName("RoundTrip_07_DisplayHardware_DD");

            // 08) PGN 0xDE - Remote Commands (AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    RemoteCommand = new RemoteCommandMsg { Mask = 1, Command = 2 }
                },
                PgnEnvelope.PayloadOneofCase.RemoteCommand)
                .SetName("RoundTrip_08_RemoteCommand_DE");

            // 09) PGN 0xF0 - ISOBUS Heartbeat (TC -> AgIO -> AOG)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    IsoBusHeartbeat = new IsoBusHeartbeatMsg { Status = 5, NumSections = 4, SectionStates = ByteString.CopyFrom(new byte[] { 0x0F }) }
                },
                PgnEnvelope.PayloadOneofCase.IsoBusHeartbeat)
                .SetName("RoundTrip_09_IsoBusHeartbeat_F0");

            // 10) PGN 0xFE - AutoSteer Data (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    AutoSteerData = new AutoSteerDataMsg { Speed = 1234, Status = 1, CommandedSteerAngle = -250, LineDistance = 10 }
                },
                PgnEnvelope.PayloadOneofCase.AutoSteerData)
                .SetName("RoundTrip_10_AutoSteerData_FE");

            // 11) PGN 0xFC - AutoSteer Settings (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    AutoSteerSettings = new AutoSteerSettingsMsg { GainProportionalKp = 100, HighPwm = 200, WasOffset = 15 }
                },
                PgnEnvelope.PayloadOneofCase.AutoSteerSettings)
                .SetName("RoundTrip_11_AutoSteerSettings_FC");

            // 12) PGN 0xFB - AutoSteer Config (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    AutoSteerConfig = new AutoSteerConfigMsg { Set0 = 1, MaxPulse = 5, AngularVelocity = 90 }
                },
                PgnEnvelope.PayloadOneofCase.AutoSteerConfig)
                .SetName("RoundTrip_12_AutoSteerConfig_FB");

            // 13) PGN 0xEF - Machine Data (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    MachineData = new MachineDataMsg { UTurn = 1, Speed = 55, Tram = 3 }
                },
                PgnEnvelope.PayloadOneofCase.MachineData)
                .SetName("RoundTrip_13_MachineData_EF");

            // 14) PGN 0xEE - Machine Config (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    MachineConfig = new MachineConfigMsg { RaiseTime = 3, LowerTime = 2, EnableHyd = 1 }
                },
                PgnEnvelope.PayloadOneofCase.MachineConfig)
                .SetName("RoundTrip_14_MachineConfig_EE");

            // 15) PGN 0xEC - Relay Config (AOG -> AgIO); repeated pin map
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    RelayConfig = new RelayConfigMsg { PinConfig = { 1u, 2u, 3u, 0u } }
                },
                PgnEnvelope.PayloadOneofCase.RelayConfig)
                .SetName("RoundTrip_15_RelayConfig_EC");

            // 16) PGN 0xEB - Section Dimensions (AOG -> AgIO); repeated widths
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    SectionDimensions = new SectionDimensionsMsg { SectionWidths = { 100u, 200u, 300u }, NumSections = 3 }
                },
                PgnEnvelope.PayloadOneofCase.SectionDimensions)
                .SetName("RoundTrip_16_SectionDimensions_EB");

            // 17) PGN 0xE5 - Extended Section Control (AOG -> AgIO); 64-section bitmask
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    ExtendedSectionControl = new ExtendedSectionControlMsg { Sections = 0xFFFFFFFFUL, ToolLeftSpeed = 10, ToolRightSpeed = 12 }
                },
                PgnEnvelope.PayloadOneofCase.ExtendedSectionControl)
                .SetName("RoundTrip_17_ExtendedSectionControl_E5");

            // 18) PGN 0xE4 - Rate Control (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    RateControl = new RateControlMsg { Rate0 = 1, Rate1 = 2, Rate2 = 3 }
                },
                PgnEnvelope.PayloadOneofCase.RateControl)
                .SetName("RoundTrip_18_RateControl_E4");

            // 19) PGN 0xF1 - Section Control Enable (AOG -> TC via AgIO); single field
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    SectionControlEnable = new SectionControlEnableMsg { Enabled = true }
                },
                PgnEnvelope.PayloadOneofCase.SectionControlEnable)
                .SetName("RoundTrip_19_SectionControlEnable_F1");

            // 20) PGN 0xF2 - Process Data (AOG -> TC via AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    ProcessData = new ProcessDataMsg { Identifier = 513, Value = -1234 }
                },
                PgnEnvelope.PayloadOneofCase.ProcessData)
                .SetName("RoundTrip_20_ProcessData_F2");

            // 21) PGN 0xF3 - Field Name (AOG -> TC via AgIO); single field
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    FieldName = new FieldNameMsg { Name = "North Field" }
                },
                PgnEnvelope.PayloadOneofCase.FieldName)
                .SetName("RoundTrip_21_FieldName_F3");

            // 22) PGN 0xD0 - Latitude/Longitude (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    LatLon = new LatLonMsg { LatitudeEncoded = 123456, LongitudeEncoded = -654321 }
                },
                PgnEnvelope.PayloadOneofCase.LatLon)
                .SetName("RoundTrip_22_LatLon_D0");

            // 23) PGN 0x64 - Corrected Position (AOG -> AgIO)
            yield return new TestCaseData(
                new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    CorrectedPosition = new CorrectedPositionMsg { Longitude = -98.76, Latitude = 12.34, Fix2FixHeading = 45.6 }
                },
                PgnEnvelope.PayloadOneofCase.CorrectedPosition)
                .SetName("RoundTrip_23_CorrectedPosition_64");
        }

        /// <summary>
        /// Serializes and re-parses each envelope, asserting the payload oneof case, the
        /// schema version, and whole-message structural equality are all preserved.
        /// </summary>
        [TestCaseSource(nameof(EnvelopeCases))]
        public void PgnEnvelope_RoundTrips_PreservingPayload(PgnEnvelope original, PgnEnvelope.PayloadOneofCase expectedCase)
        {
            // Arrange
            // The envelope under test is supplied by EnvelopeCases().

            // Act
            byte[] bytes = original.ToByteArray();
            PgnEnvelope parsed = PgnEnvelope.Parser.ParseFrom(bytes);

            // Assert
            Assert.That(parsed.PayloadCase, Is.EqualTo(expectedCase));
            Assert.That(parsed.SchemaVersion, Is.EqualTo(IpcConstants.SchemaVersion));
            Assert.That(parsed, Is.EqualTo(original));
        }

        /// <summary>
        /// Pins the initial schema version to 1, matching docs/proto/SCHEMA_VERSIONING.md
        /// and the value every producer stamps onto <c>PgnEnvelope.SchemaVersion</c>.
        /// </summary>
        [Test]
        public void SchemaVersion_IsOne()
        {
            // Arrange / Act / Assert
            Assert.That(IpcConstants.SchemaVersion, Is.EqualTo(1u));
        }
    }
}
