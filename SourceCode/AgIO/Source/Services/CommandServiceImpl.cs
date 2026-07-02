using System;
using System.Text;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Ipc;
using Grpc.Core;

namespace AgIO
{
    /// <summary>
    /// Unary gRPC service carrying AgOpenGPS -> AgIO outbound commands and
    /// configuration. Each handler rebuilds the legacy byte PGN frame
    /// [0x80, 0x81, 0x7F, PGN, Length, Data..., CRC] that the hardware firmware
    /// still requires, computes the additive-byte CRC, and forwards it to the
    /// hardware bridge. This replaces the fire-and-forget UDP send with a unary
    /// RPC that returns a delivery acknowledgement.
    /// </summary>
    public class CommandServiceImpl : CommandService.CommandServiceBase
    {
        private readonly FormLoop formLoop;

        public CommandServiceImpl(FormLoop formLoop)
        {
            this.formLoop = formLoop;
        }

        public override Task<CommandAck> SendAutoSteerData(AutoSteerDataMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)(request.Speed & 0xFF);
            frame[6] = (byte)((request.Speed >> 8) & 0xFF);
            frame[7] = (byte)request.Status;
            frame[8] = (byte)(request.CommandedSteerAngle & 0xFF);
            frame[9] = (byte)((request.CommandedSteerAngle >> 8) & 0xFF);
            frame[10] = (byte)request.LineDistance;
            frame[11] = (byte)request.SectionControl18;
            frame[12] = (byte)request.SectionControl916;
            return Forward(nameof(SendAutoSteerData), frame);
        }

        public override Task<CommandAck> SendAutoSteerSettings(AutoSteerSettingsMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFC, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.GainProportionalKp;
            frame[6] = (byte)request.HighPwm;
            frame[7] = (byte)request.LowPwm;
            frame[8] = (byte)request.MinPwm;
            frame[9] = (byte)request.CountsPerDegree;
            frame[10] = (byte)(request.WasOffset & 0xFF);
            frame[11] = (byte)((request.WasOffset >> 8) & 0xFF);
            frame[12] = (byte)request.Ackerman;
            return Forward(nameof(SendAutoSteerSettings), frame);
        }

        public override Task<CommandAck> SendAutoSteerConfig(AutoSteerConfigMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xFB, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.Set0;
            frame[6] = (byte)request.MaxPulse;
            frame[7] = (byte)request.MinSpeed;
            frame[8] = (byte)request.AckermanFix;
            frame[9] = (byte)request.AngularVelocity;
            return Forward(nameof(SendAutoSteerConfig), frame);
        }

        public override Task<CommandAck> SendMachineData(MachineDataMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xEF, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.UTurn;
            frame[6] = (byte)request.Speed;
            frame[7] = (byte)request.HydLift;
            frame[8] = (byte)request.Tram;
            frame[9] = (byte)request.GeoStop;
            frame[11] = (byte)request.SectionControl18;
            frame[12] = (byte)request.SectionControl916;
            return Forward(nameof(SendMachineData), frame);
        }

        public override Task<CommandAck> SendMachineConfig(MachineConfigMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xEE, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.RaiseTime;
            frame[6] = (byte)request.LowerTime;
            frame[7] = (byte)request.EnableHyd;
            frame[8] = (byte)request.Set0;
            frame[9] = (byte)request.User1;
            frame[10] = (byte)request.User2;
            frame[11] = (byte)request.User3;
            frame[12] = (byte)request.User4;
            return Forward(nameof(SendMachineConfig), frame);
        }

        public override Task<CommandAck> SendRelayConfig(RelayConfigMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[30];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xEC;
            frame[4] = 24;
            for (int i = 0; i < request.PinConfig.Count && i < 24; i++)
            {
                frame[5 + i] = (byte)request.PinConfig[i];
            }
            return Forward(nameof(SendRelayConfig), frame);
        }

        public override Task<CommandAck> SendSectionDimensions(SectionDimensionsMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[39];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xEB;
            frame[4] = 33;
            for (int i = 0; i < request.SectionWidths.Count && i < 16; i++)
            {
                uint width = request.SectionWidths[i];
                frame[5 + (i * 2)] = (byte)(width & 0xFF);
                frame[6 + (i * 2)] = (byte)((width >> 8) & 0xFF);
            }
            frame[37] = (byte)request.NumSections;
            return Forward(nameof(SendSectionDimensions), frame);
        }

        public override Task<CommandAck> SendExtendedSectionControl(ExtendedSectionControlMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xE5, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] sections = BitConverter.GetBytes(request.Sections);
            Array.Copy(sections, 0, frame, 5, 8);
            frame[13] = (byte)request.ToolLeftSpeed;
            frame[14] = (byte)request.ToolRightSpeed;
            return Forward(nameof(SendExtendedSectionControl), frame);
        }

        public override Task<CommandAck> SendRateControl(RateControlMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xE4, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)request.Rate0;
            frame[6] = (byte)request.Rate1;
            frame[7] = (byte)request.Rate2;
            return Forward(nameof(SendRateControl), frame);
        }

        public override Task<CommandAck> SendSectionControlEnable(SectionControlEnableMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xF1, 1, 0, 0 };
            frame[5] = (byte)(request.Enabled ? 1 : 0);
            return Forward(nameof(SendSectionControlEnable), frame);
        }

        public override Task<CommandAck> SendProcessData(ProcessDataMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xF2, 6, 0, 0, 0, 0, 0, 0, 0 };
            frame[5] = (byte)(request.Identifier & 0xFF);
            frame[6] = (byte)((request.Identifier >> 8) & 0xFF);
            byte[] value = BitConverter.GetBytes(request.Value);
            Array.Copy(value, 0, frame, 7, 4);
            return Forward(nameof(SendProcessData), frame);
        }

        public override Task<CommandAck> SendFieldName(FieldNameMsg request, ServerCallContext context)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(request.Name);
            if (nameBytes.Length > 248)
            {
                byte[] clamped = new byte[248];
                Array.Copy(nameBytes, clamped, 248);
                nameBytes = clamped;
            }
            byte[] frame = new byte[5 + nameBytes.Length + 1];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0xF3;
            frame[4] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, frame, 5, nameBytes.Length);
            return Forward(nameof(SendFieldName), frame);
        }

        public override Task<CommandAck> SendLatLon(LatLonMsg request, ServerCallContext context)
        {
            byte[] frame = new byte[] { 0x80, 0x81, 0x7F, 0xD0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            byte[] latitude = BitConverter.GetBytes(request.LatitudeEncoded);
            Array.Copy(latitude, 0, frame, 5, 4);
            byte[] longitude = BitConverter.GetBytes(request.LongitudeEncoded);
            Array.Copy(longitude, 0, frame, 9, 4);
            return Forward(nameof(SendLatLon), frame);
        }

        public override Task<CommandAck> SendCorrectedPosition(CorrectedPositionMsg request, ServerCallContext context)
        {
            // PGN 0x64 is not documented in docs/pgn-protocol.md byte tables; the layout is
            // derived from the proto CorrectedPositionMsg. The standard layout carries two
            // doubles (longitude, latitude); the extended layout adds fix2fix_heading when it
            // is valid (the sentinel value 1000 marks an invalid heading).
            bool hasHeading = request.Fix2FixHeading != 1000.0;
            int dataLength = hasHeading ? 24 : 16;
            byte[] frame = new byte[5 + dataLength + 1];
            frame[0] = 0x80;
            frame[1] = 0x81;
            frame[2] = 0x7F;
            frame[3] = 0x64;
            frame[4] = (byte)dataLength;
            byte[] longitude = BitConverter.GetBytes(request.Longitude);
            Array.Copy(longitude, 0, frame, 5, 8);
            byte[] latitude = BitConverter.GetBytes(request.Latitude);
            Array.Copy(latitude, 0, frame, 13, 8);
            if (hasHeading)
            {
                byte[] heading = BitConverter.GetBytes(request.Fix2FixHeading);
                Array.Copy(heading, 0, frame, 21, 8);
            }
            return Forward(nameof(SendCorrectedPosition), frame);
        }

        /// <summary>
        /// Optional injection RPC (AAP 0.6.3). Lets ModSim act as a client that injects
        /// simulated inbound frames which the AgIO bridge fans out to all telemetry
        /// subscribers via the shared static registry.
        /// </summary>
        public override Task<CommandAck> InjectTelemetry(PgnEnvelope request, ServerCallContext context)
        {
            try
            {
                TelemetryServiceImpl.Broadcast(request);
                return Task.FromResult(new CommandAck { Received = true });
            }
            catch (Exception ex)
            {
                Log.EventWriter("CommandServiceImpl.InjectTelemetry: " + ex.Message);
                return Task.FromResult(new CommandAck { Received = false, ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Applies the additive-byte CRC, forwards the frame to the hardware bridge, and
        /// returns an acknowledgement. Any failure is logged and returned as a negative
        /// acknowledgement rather than thrown back to the gRPC caller.
        /// </summary>
        private Task<CommandAck> Forward(string handlerName, byte[] frame)
        {
            try
            {
                ApplyCrc(frame);
                formLoop.ForwardCommandToHardware(frame);
                return Task.FromResult(new CommandAck { Received = true });
            }
            catch (Exception ex)
            {
                Log.EventWriter("CommandServiceImpl." + handlerName + ": " + ex.Message);
                return Task.FromResult(new CommandAck { Received = false, ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Computes the legacy additive-byte CRC over bytes 2..N-2 and stores it in the
        /// final byte. The hardware firmware still requires this CRC.
        /// </summary>
        private static void ApplyCrc(byte[] frame)
        {
            byte crc = 0;
            for (int i = 2; i < frame.Length - 1; i++)
            {
                crc += frame[i];
            }
            frame[frame.Length - 1] = crc;
        }
    }
}
