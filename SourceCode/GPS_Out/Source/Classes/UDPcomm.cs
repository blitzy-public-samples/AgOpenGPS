using System;
using System.Net; // IPAddress — retained for NetworkEP / SetEP / SetSourceIP (endpoint config surface kept for Form1 API compatibility).
// IPC-REFACTOR: the gRPC loopback channel build + connect retry/backoff now live in the shared AgOpenGPS.Ipc helpers
// (IpcChannelFactory + IpcTelemetrySubscriber), so the net48-incompatible transport usings (System.IO, System.IO.Pipes,
// System.Net.Http, System.Net.Sockets, System.Runtime.InteropServices) and Google.Protobuf.WellKnownTypes / Grpc.Core
// are no longer referenced in this file and have been removed.
using System.Threading;
using System.Threading.Tasks;
using AgOpenGPS.Ipc;
using Grpc.Net.Client;

namespace GPS_Out
{
    public class UDPComm
    {
        private readonly frmStart mf;

        // IPC-REFACTOR: 1024-byte UDP receive buffer removed — gRPC HTTP/2 framing owns buffering now.
        private string cConnectionName;
        private bool cIsUDPSendConnected;
        private string cLog;
        private IPAddress cNetworkEP;
        private int cReceivePort;   // IPC-REFACTOR: retained for API compatibility with Form1 (no longer used for UDP binding).
        private int cSendFromPort;  // IPC-REFACTOR: retained for API compatibility with Form1 (no longer used for UDP binding).
        private int cSendToPort;    // IPC-REFACTOR: retained for API compatibility with Form1 (no longer used for UDP binding).
        private IPAddress cSourceIP; // IPC-REFACTOR: still assigned by SetSourceIP; no longer used for UDP binding.
        private string cSubNet;

        // IPC-REFACTOR: gRPC channel + cancellation for the background StreamTelemetry subscriber (replaces recvSocket/sendSocket).
        private GrpcChannel _channel;
        private CancellationTokenSource _cts;

        // IPC-REFACTOR (QA F1 Issue 2): the single telemetry payload this comm instance owns. The
        // TelemetryService fan-out delivers EVERY envelope to EVERY subscriber, whereas each legacy
        // per-port UDP listener saw only its own SubPGN. Restricting each of the two instances
        // (AGIOcomm / AOGcomm) to its owned payload restores the legacy single-parse-per-frame behaviour,
        // so a frame is no longer parsed once per instance, while preserving the two-instance model
        // (both instances still subscribe and hold their own channel + connection flag).
        private readonly PgnEnvelope.PayloadOneofCase _observedPayload;

        public UDPComm(frmStart CallingForm, int ReceivePort, int SendToPort, int SendFromPort,
            string ConnectionName, string SourceIPaddress, string DestinationEndPoint = "")
        {
            mf = CallingForm;
            cReceivePort = ReceivePort;
            cSendToPort = SendToPort;
            cSendFromPort = SendFromPort;
            cConnectionName = ConnectionName;
            // IPC-REFACTOR (QA F1 Issue 2): map this instance to the one payload its legacy UDP port received —
            // AGIOcomm observes GpsPosition (-> AGIOdata / PGN54908); AOGcomm observes CorrectedPosition
            // (-> AOGdata / PGN100). This is a bijection over the two observed payloads, so each frame the
            // fan-out broadcasts is parsed by exactly one instance (no double-parse) and both payloads stay covered.
            _observedPayload = string.Equals(ConnectionName, "AGIO", StringComparison.Ordinal)
                ? PgnEnvelope.PayloadOneofCase.GpsPosition
                : PgnEnvelope.PayloadOneofCase.CorrectedPosition;
            SetEP(DestinationEndPoint);
            SetSourceIP(SourceIPaddress);
        }

        // IPC-REFACTOR: HandleDataDelegate field and its HandleDataDelegateObj delegate type removed — the stream-reader task calls HandleData directly (no UI marshaling needed).

        public bool IsUDPSendConnected { get => cIsUDPSendConnected; set => cIsUDPSendConnected = value; }

        public string NetworkEP
        {
            get { return cNetworkEP.ToString(); }
            set
            {
                string[] data;
                if (IPAddress.TryParse(value, out IPAddress IP))
                {
                    data = value.Split('.');
                    cNetworkEP = IPAddress.Parse(data[0] + "." + data[1] + "." + data[2] + ".255");
                    Properties.Settings.Default["EndPoint" + cConnectionName] = cNetworkEP.ToString();
                    cSubNet = data[0].ToString() + "." + data[1].ToString() + "." + data[2].ToString();
                }
            }
        }

        public void StartUDPServer()
        {
            try
            {
                // IPC-REFACTOR: UDP loopback receive socket + BeginReceiveFrom replaced by a background TelemetryService.StreamTelemetry gRPC subscriber.
                _cts = new CancellationTokenSource();
                Task.Factory.StartNew(() => SubscribeLoopAsync(_cts.Token),
                    _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception e)
            {
                mf.Tls.WriteErrorLog("UDPcomm/StartUDPServer: \n" + e.Message);
            }
        }

        // IPC-REFACTOR: cancellation-based teardown replaces implicit socket disposal (additive public method; not part of the legacy contract).
        public void Stop()
        {
            _cts?.Cancel();
            _channel?.Dispose();
            _channel = null;
        }

        private void AddToLog(string NewData)
        {
            cLog += DateTime.Now.Second.ToString() + "  " + NewData + Environment.NewLine;
            if (cLog.Length > 100000)
            {
                cLog = cLog.Substring(cLog.Length - 98000, 98000);
            }
            cLog = cLog.Replace("\0", string.Empty);
        }

        // IPC-REFACTOR: connect + subscribe delegated to the shared AgOpenGPS.Ipc.IpcTelemetrySubscriber. This removes
        // the net48-incompatible SocketsHttpHandler channel build (now guarded in IpcChannelFactory) and the C# 7.3-
        // incompatible async-stream (await foreach / ReadAllAsync) loop, and makes the canonical 5-attempt /
        // 500-1000-2000-4000-8000 ms (total 15500 ms) connect retry-backoff identical across every IPC client. The
        // endpoint is derived from IpcConstants.AgIoSocketPath inside the shared factory (no hard-coded pipe name here).
        // IsUDPSendConnected still means "IPC connected": set true when the stream opens, false on any disconnect.
        private Task SubscribeLoopAsync(CancellationToken token)
        {
            return IpcTelemetrySubscriber.RunAsync(
                onChannel: channel => _channel = channel,
                onConnected: () => IsUDPSendConnected = true,
                onEnvelope: HandleData,
                onError: ex => mf.Tls.WriteErrorLog("UDPcomm/SubscribeLoop: " + ex.Message),
                token: token,
                onDisconnected: () => IsUDPSendConnected = false);
        }

        // IPC-REFACTOR: byte-keyed switch(PGN)/switch(SubPGN) dispatch replaced by typed switch(envelope.PayloadCase); observes exactly the two payloads the legacy listener handled.
        private void HandleData(PgnEnvelope envelope)
        {
            try
            {
                // IPC-REFACTOR (QA F1 Issue 2): observe only this instance's owned payload. The TelemetryService
                // fan-out broadcasts every envelope to every subscriber, so without this guard BOTH the AGIOcomm
                // and AOGcomm instances would parse BOTH payloads (each frame handled twice). Gating to the owned
                // payload restores the legacy per-port single-parse semantics; any other payload (including
                // PayloadOneofCase.None) is ignored here exactly as the legacy per-port listener ignored it.
                if (envelope.PayloadCase != _observedPayload)
                {
                    return;
                }

                switch (envelope.PayloadCase)
                {
                    case PgnEnvelope.PayloadOneofCase.GpsPosition:       // IPC-REFACTOR: was SubPGN 54908 / 0xD67C -> mf.AGIOdata.ParseByteData(Data).
                        AddToLog("< " + envelope.PayloadCase);
                        mf.AGIOdata.ParseMessage(envelope.GpsPosition);
                        break;

                    case PgnEnvelope.PayloadOneofCase.CorrectedPosition: // IPC-REFACTOR: was SubPGN 25727 / 0x647F -> mf.AOGdata.ParseByteData(Data).
                        AddToLog("< " + envelope.PayloadCase);
                        mf.AOGdata.ParseMessage(envelope.CorrectedPosition);
                        break;

                    default:
                        break; // IPC-REFACTOR: ignore all other payloads (parity with the legacy two-SubPGN behavior); tolerate PayloadOneofCase.None gracefully.
                }
            }
            catch (Exception ex)
            {
                mf.Tls.WriteErrorLog("UDPcomm/HandleData " + ex.Message);
            }
        }

        private void SetEP(string DestinationEndPoint)
        {
            try
            {
                if (IPAddress.TryParse(DestinationEndPoint, out _))
                {
                    NetworkEP = DestinationEndPoint;
                }
                else
                {
                    string EP = mf.Tls.LoadProperty("EndPoint_" + cConnectionName);
                    if (IPAddress.TryParse(EP, out _))
                    {
                        NetworkEP = EP;
                    }
                    else
                    {
                        NetworkEP = "192.168.1.255";
                    }
                }
            }
            catch (Exception ex)
            {
                mf.Tls.WriteErrorLog("UDPcomm/SetEP " + ex.Message);
            }
        }

        private void SetSourceIP(string Source)
        {
            try
            {
                if (IPAddress.TryParse(Source, out IPAddress tmp))
                {
                    cSourceIP = tmp;
                }
                else
                {
                    cSourceIP = IPAddress.Any;
                }
            }
            catch (Exception ex)
            {
                mf.Tls.WriteErrorLog("UDPcomm/SetSourceEP " + ex.Message);
            }
        }
    }
}
