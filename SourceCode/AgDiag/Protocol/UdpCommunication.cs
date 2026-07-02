using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
// IPC-REFACTOR: System.Net.Sockets retained ONLY for the Unix-domain-socket connect callback (legacy UdpClient transport removed).
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
// IPC-REFACTOR: gRPC client + proto contract usings replace the UDP transport.
using AgOpenGPS.Ipc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

namespace AgDiag.Protocol
{
    public class UdpCommunication
    {
        private readonly Pgns _pgns;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private int cntr;

        // IPC-REFACTOR: gRPC channel to the AgIO TelemetryService (disposed on CloseLoopback; replaces the legacy UdpClient using-block).
        private GrpcChannel _channel;

        public UdpCommunication(Pgns pgns)
        {
            _pgns = pgns;
        }

        public event EventHandler<int> DefaultSendsUpdated;

        public void LoadLoopback()
        {
            // IPC-REFACTOR: UDP loopback listener replaced by TelemetryService.StreamTelemetry subscription (same LongRunning background-task model).
            var cancellationToken = _cancellationTokenSource.Token;
            Task.Factory.StartNew(() => SubscribeLoopAsync(cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void CloseLoopback()
        {
            _cancellationTokenSource.Cancel();
            // IPC-REFACTOR: also dispose the gRPC channel on shutdown (replaces the UdpClient using-block disposal).
            _channel?.Dispose();
            _channel = null;
        }

        // IPC-REFACTOR: GrpcChannel over the loopback UDS/named-pipe at IpcConstants.AgIoSocketPath; OS-branched connect callback. Loopback-only, no TLS, no auth (parity with the legacy UDP loopback posture).
        // FEASIBILITY DEVIATION (AAP 0.1.3/0.6.4): SocketsHttpHandler.ConnectCallback / UnixDomainSocketEndPoint target the .NET 6+ host the prompt designates; AgDiag stays net48 pending the AgIO host retarget (do not work around).
        private static GrpcChannel CreateChannel()
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // IPC-REFACTOR: Windows loopback transport = named pipe (matches the tail of IpcConstants.AgIoSocketPath and the AgIO server pipe).
                        var pipe = new NamedPipeClientStream(".", "agopengps_ipc", PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                        return pipe;
                    }

                    // IPC-REFACTOR: Linux/macOS loopback transport = Unix domain socket at IpcConstants.AgIoSocketPath.
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(IpcConstants.AgIoSocketPath)).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };

            return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        }

        // IPC-REFACTOR: replaces the legacy ReceiveLoopAsync (UdpClient 17777 + ReceiveAsync) with a resilient StreamTelemetry subscriber; handles the AgIO auto-start race via connect retry/backoff.
        private async Task SubscribeLoopAsync(CancellationToken token)
        {
            // IPC-REFACTOR: bind-failure MessageBox replaced by connect retry/backoff (5 attempts, 500..8000 ms) before surfacing the same MessageBox error.
            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts && !token.IsCancellationRequested; attempt++)
            {
                try
                {
                    _channel = CreateChannel();
                    var client = new TelemetryService.TelemetryServiceClient(_channel);

                    using (var call = client.StreamTelemetry(new Empty(), cancellationToken: token))
                    {
                        // IPC-REFACTOR: gRPC server-stream read replaces UdpClient.ReceiveAsync(); typed envelopes dispatched below.
                        await foreach (var envelope in call.ResponseStream.ReadAllAsync(token).ConfigureAwait(false))
                        {
                            Dispatch(envelope);
                        }
                    }

                    return; // IPC-REFACTOR: stream completed (server closed) — normal termination.
                }
                catch (OperationCanceledException)
                {
                    return; // IPC-REFACTOR: normal shutdown via CloseLoopback — not an error.
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && token.IsCancellationRequested)
                {
                    return; // IPC-REFACTOR: cancellation surfaced as RpcException — not an error.
                }
                catch (Exception ex) when (ex is RpcException || ex is SocketException || ex is IOException)
                {
                    // IPC-REFACTOR: connect/stream failure (incl. AgIO not-yet-listening during auto-start) — back off and retry, or surface after exhaustion.
                    _channel?.Dispose();
                    _channel = null;

                    if (attempt >= maxAttempts)
                    {
                        MessageBox.Show("IPC Error: " + ex.Message, "AgIO IPC", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    try
                    {
                        await Task.Delay(500 << (attempt - 1), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // IPC-REFACTOR: cancelled while backing off — normal shutdown.
                    }
                }
            }
        }

        private void Dispatch(PgnEnvelope envelope)
        {
            // IPC-REFACTOR: switch(data[3]) selector-byte dispatch replaced by typed PgnEnvelope.PayloadCase dispatch (same 5 observed PGNs; unknown -> default counter).
            switch (envelope.PayloadCase)
            {
                case PgnEnvelope.PayloadOneofCase.SteerModuleResponse: // was 253 / 0xFD
                    _pgns.asModule.SetFromMessage(envelope.SteerModuleResponse);
                    break;
                case PgnEnvelope.PayloadOneofCase.AutoSteerData:       // was 254 / 0xFE
                    _pgns.asData.SetFromMessage(envelope.AutoSteerData);
                    break;
                case PgnEnvelope.PayloadOneofCase.AutoSteerSettings:   // was 252 / 0xFC
                    _pgns.asSet.SetFromMessage(envelope.AutoSteerSettings);
                    break;
                case PgnEnvelope.PayloadOneofCase.AutoSteerConfig:     // was 251 / 0xFB
                    _pgns.asConfig.SetFromMessage(envelope.AutoSteerConfig);
                    break;
                case PgnEnvelope.PayloadOneofCase.MachineData:         // was 239 / 0xEF
                    _pgns.maData.SetFromMessage(envelope.MachineData);
                    break;
                default: // unknown / None / MachineConfig / GPS / IMU / etc.
                    cntr += envelope.CalculateSize();
                    DefaultSendsUpdated?.Invoke(this, cntr);
                    break;
            }
        }
    }
}
