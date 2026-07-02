using System;
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

        // IPC-REFACTOR: the legacy hardware byte-frame construction for every Send* handler is now delegated to
        // AgOpenGPS.Ipc.CommandFrameBuilder. The frame byte layouts are unchanged (copied verbatim); extracting
        // them into the shared contract assembly lets the net48 test project — which cannot reference this WinExe —
        // unit-test the exact PGN byte, length byte, payload offsets, and additive CRC directly. This closes the
        // coverage gap that let the PGN 0x64 corrected-position byte-contract regression slip through.
        public override Task<CommandAck> SendAutoSteerData(AutoSteerDataMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildAutoSteerData(request);
            return Forward(nameof(SendAutoSteerData), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, AutoSteerData = request });
        }

        public override Task<CommandAck> SendAutoSteerSettings(AutoSteerSettingsMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildAutoSteerSettings(request);
            return Forward(nameof(SendAutoSteerSettings), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, AutoSteerSettings = request });
        }

        public override Task<CommandAck> SendAutoSteerConfig(AutoSteerConfigMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildAutoSteerConfig(request);
            return Forward(nameof(SendAutoSteerConfig), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, AutoSteerConfig = request });
        }

        public override Task<CommandAck> SendMachineData(MachineDataMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildMachineData(request);
            return Forward(nameof(SendMachineData), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, MachineData = request });
        }

        public override Task<CommandAck> SendMachineConfig(MachineConfigMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildMachineConfig(request);
            return Forward(nameof(SendMachineConfig), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, MachineConfig = request });
        }

        public override Task<CommandAck> SendRelayConfig(RelayConfigMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildRelayConfig(request);
            return Forward(nameof(SendRelayConfig), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, RelayConfig = request });
        }

        public override Task<CommandAck> SendSectionDimensions(SectionDimensionsMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildSectionDimensions(request);
            return Forward(nameof(SendSectionDimensions), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, SectionDimensions = request });
        }

        public override Task<CommandAck> SendExtendedSectionControl(ExtendedSectionControlMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildExtendedSectionControl(request);
            return Forward(nameof(SendExtendedSectionControl), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, ExtendedSectionControl = request });
        }

        public override Task<CommandAck> SendRateControl(RateControlMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildRateControl(request);
            return Forward(nameof(SendRateControl), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, RateControl = request });
        }

        public override Task<CommandAck> SendSectionControlEnable(SectionControlEnableMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildSectionControlEnable(request);
            return Forward(nameof(SendSectionControlEnable), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, SectionControlEnable = request });
        }

        public override Task<CommandAck> SendProcessData(ProcessDataMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildProcessData(request);
            return Forward(nameof(SendProcessData), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, ProcessData = request });
        }

        public override Task<CommandAck> SendFieldName(FieldNameMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildFieldName(request);
            return Forward(nameof(SendFieldName), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, FieldName = request });
        }

        public override Task<CommandAck> SendLatLon(LatLonMsg request, ServerCallContext context)
        {
            byte[] frame = CommandFrameBuilder.BuildLatLon(request);
            return Forward(nameof(SendLatLon), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, LatLon = request });
        }

        public override Task<CommandAck> SendCorrectedPosition(CorrectedPositionMsg request, ServerCallContext context)
        {
            // IPC-REFACTOR: PGN 0x64 corrected-position frame is ALWAYS a 30-byte / 24-data-byte frame with the
            // fix2fix heading written at offset 21-28, exactly as the legacy producer (GPS Position.designer.cs)
            // emitted it — including the sentinel heading value 1000 (invalid heading). An earlier revision emitted
            // a shorter 16-data-byte frame and omitted the heading when it equalled the sentinel, which changed the
            // firmware-facing byte contract; that regression is fixed by delegating to CommandFrameBuilder, which
            // always writes all 24 data bytes. See CommandFrameBuilder.BuildCorrectedPosition.
            byte[] frame = CommandFrameBuilder.BuildCorrectedPosition(request);
            return Forward(nameof(SendCorrectedPosition), frame,
                new PgnEnvelope { SchemaVersion = IpcConstants.SchemaVersion, CorrectedPosition = request });
        }

        /// <summary>
        /// Optional injection RPC (AAP 0.6.3). Lets ModSim act as a client that injects
        /// simulated inbound frames which the AgIO bridge fans out to all telemetry
        /// subscribers via the shared static registry.
        /// </summary>
        public override Task<CommandAck> InjectTelemetry(PgnEnvelope request, ServerCallContext context)
        {
            // IPC-REFACTOR: the accept/reject decision is delegated to the shared, unit-testable
            // AgOpenGPS.Ipc.TelemetryInjectionValidator so the injection contract can be exercised from
            // the net48 test project (which cannot reference this WinExe). A malformed or default-version
            // local injection must never be broadcast to every subscriber.
            if (!TelemetryInjectionValidator.TryValidate(request, out string rejectReason))
            {
                return Task.FromResult(new CommandAck { Received = false, ErrorMessage = rejectReason });
            }

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
        /// Applies the additive-byte CRC, forwards the frame to the hardware bridge, then
        /// fans the typed <paramref name="envelope"/> out to every telemetry subscriber, and
        /// returns an acknowledgement. Any failure is logged and returned as a negative
        /// acknowledgement rather than thrown back to the gRPC caller.
        ///
        /// <para>
        /// The <see cref="TelemetryServiceImpl.Broadcast(PgnEnvelope)"/> call preserves the
        /// legacy UDP loopback broadcast semantics: AOG-originated command / configuration /
        /// corrected-position frames were previously observed by every loopback listener
        /// (AgDiag, GPS_Out, ModSim). Rebuilding the hardware byte frame and computing its CRC
        /// keeps the firmware-facing contract intact; the additional broadcast re-establishes
        /// the observer parity that the fire-and-forget UDP path provided.
        /// </para>
        /// </summary>
        private Task<CommandAck> Forward(string handlerName, byte[] frame, PgnEnvelope envelope)
        {
            try
            {
                // IPC-REFACTOR: The legacy additive-byte CRC (bytes 2..N-2 -> final byte) is computed by the
                // shared CommandFrameBuilder so the firmware-facing byte contract is defined in exactly one place
                // and is unit-testable from the net48 test project without referencing this WinForms host.
                CommandFrameBuilder.ApplyCrc(frame);
                formLoop.ForwardCommandToHardware(frame);

                // Re-establish UDP-broadcast parity: every AOG-originated command reaches all
                // telemetry subscribers, exactly as the legacy loopback broadcast did.
                TelemetryServiceImpl.Broadcast(envelope);

                return Task.FromResult(new CommandAck { Received = true });
            }
            catch (Exception ex)
            {
                Log.EventWriter("CommandServiceImpl." + handlerName + ": " + ex.Message);
                return Task.FromResult(new CommandAck { Received = false, ErrorMessage = ex.Message });
            }
        }
    }
}
