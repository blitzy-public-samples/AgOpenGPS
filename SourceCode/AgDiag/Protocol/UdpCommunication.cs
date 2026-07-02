using System;
// IPC-REFACTOR: the gRPC loopback channel build + connect retry/backoff now live in the shared AgOpenGPS.Ipc helpers
// (IpcChannelFactory + IpcTelemetrySubscriber), so the net48-incompatible transport usings (System.IO, System.IO.Pipes,
// System.Net.Http, System.Net.Sockets, System.Runtime.InteropServices) and Google.Protobuf.WellKnownTypes / Grpc.Core
// are no longer referenced in this file and have been removed.
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgOpenGPS.Ipc;
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
            // IPC-REFACTOR: recreate the CancellationTokenSource when a prior CloseLoopback disposed+nulled it, so a
            // LoadLoopback -> CloseLoopback -> LoadLoopback cycle remains safe (no use of a disposed CTS).
            if (_cancellationTokenSource == null)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }
            var cancellationToken = _cancellationTokenSource.Token;
            Task.Factory.StartNew(() => SubscribeLoopAsync(cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void CloseLoopback()
        {
            // IPC-REFACTOR: cancel, then dispose AND null the CancellationTokenSource (previously it was cancelled but
            // never disposed/nulled, leaking the CTS). LoadLoopback recreates it when null, so repeated Load/Close
            // cycles remain safe. Guarded so CloseLoopback is idempotent.
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            // IPC-REFACTOR: also dispose the gRPC channel on shutdown (replaces the UdpClient using-block disposal).
            _channel?.Dispose();
            _channel = null;
        }

        // IPC-REFACTOR: connect + subscribe delegated to the shared AgOpenGPS.Ipc.IpcTelemetrySubscriber. This removes
        // the net48-incompatible SocketsHttpHandler channel build (now guarded in IpcChannelFactory) and the C# 7.3-
        // incompatible async-stream (await foreach / ReadAllAsync) loop, and makes the canonical 5-attempt /
        // 500-1000-2000-4000-8000 ms (total 15500 ms) connect retry-backoff identical across every IPC client. The
        // endpoint is derived from IpcConstants.AgIoSocketPath inside the shared factory (no hard-coded pipe name here).
        private Task SubscribeLoopAsync(CancellationToken token)
        {
            return IpcTelemetrySubscriber.RunAsync(
                onChannel: channel => _channel = channel,
                onConnected: null,
                onEnvelope: Dispatch,
                onError: ex => MessageBox.Show(
                    "IPC Error: " + ex.Message, "AgIO IPC", MessageBoxButtons.OK, MessageBoxIcon.Error),
                token: token);
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
