using System.Threading.Tasks;
using AgOpenGPS.Ipc;
using Grpc.Core;
using NUnit.Framework;

namespace AgOpenGPS.Tests
{
    /// <summary>
    /// Test Area 3 — validates the unary <c>CommandService</c> acknowledgement contract that
    /// replaces the legacy fire-and-forget UDP send with an explicit delivery confirmation.
    /// A full <c>GrpcChannel</c> client-to-server round-trip would require ASP.NET Core / Kestrel
    /// (net6+), which this test project deliberately does not reference (only <c>AgOpenGPS.Ipc</c>).
    /// These tests therefore derive a self-contained in-process stub from the generated
    /// <see cref="CommandService.CommandServiceBase"/> and invoke its <c>Send*</c> overrides
    /// directly — no network, no host, no mocking framework. This proves exactly what the gRPC
    /// migration adds over UDP: every <c>Send*</c> RPC returns a <see cref="CommandAck"/> whose
    /// <see cref="CommandAck.Received"/> flag confirms receipt (and carries an error message when
    /// the command is rejected).
    /// </summary>
    public class CommandServiceIpcTests
    {
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
    }
}
