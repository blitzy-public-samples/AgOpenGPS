using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
// IPC-REFACTOR: System.Net.Sockets retained ONLY for the Unix-domain-socket connect callback (legacy UDP receive/send sockets removed).
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
// IPC-REFACTOR: gRPC client + proto contract usings replace the UDP transport.
using AgOpenGPS.Ipc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
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

        public UDPComm(frmStart CallingForm, int ReceivePort, int SendToPort, int SendFromPort,
            string ConnectionName, string SourceIPaddress, string DestinationEndPoint = "")
        {
            mf = CallingForm;
            cReceivePort = ReceivePort;
            cSendToPort = SendToPort;
            cSendFromPort = SendFromPort;
            cConnectionName = ConnectionName;
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

        // IPC-REFACTOR: GrpcChannel over the loopback UDS/named-pipe at IpcConstants.AgIoSocketPath; OS-branched connect callback. Loopback-only, no TLS, no auth (parity with the legacy UDP loopback posture).
        // FEASIBILITY DEVIATION (AAP 0.1.3/0.6.4): SocketsHttpHandler.ConnectCallback / UnixDomainSocketEndPoint target the .NET 6+ host the prompt designates; GPS_Out stays net48 pending the AgIO host retarget (do not work around).
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

        // IPC-REFACTOR: replaces ReceiveData/BeginReceiveFrom with a resilient StreamTelemetry subscriber; handles the AgIO auto-start race via connect retry/backoff.
        private async Task SubscribeLoopAsync(CancellationToken token)
        {
            // IPC-REFACTOR: bind-failure log replaced by connect retry/backoff (5 attempts, 500..8000 ms) before surfacing via mf.Tls.WriteErrorLog.
            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts && !token.IsCancellationRequested; attempt++)
            {
                try
                {
                    _channel = CreateChannel();
                    var client = new TelemetryService.TelemetryServiceClient(_channel);
                    using (var call = client.StreamTelemetry(new Empty(), cancellationToken: token))
                    {
                        IsUDPSendConnected = true; // IPC-REFACTOR: flag now means "IPC connected".

                        // IPC-REFACTOR: gRPC server-stream read replaces the UDP BeginReceiveFrom/ReceiveData callback loop; typed envelopes dispatched below.
                        await foreach (var envelope in call.ResponseStream.ReadAllAsync(token).ConfigureAwait(false))
                        {
                            // IPC-REFACTOR: direct call from the stream-reader task; typed-ingest (PGN54908/PGN100.ParseMessage) sets fields + timestamp only (no UI), so mf.Invoke UI marshaling is unnecessary.
                            HandleData(envelope);
                        }
                    }

                    return; // IPC-REFACTOR: stream completed (server closed) — normal termination.
                }
                catch (OperationCanceledException)
                {
                    return; // IPC-REFACTOR: cancellation is normal shutdown.
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && token.IsCancellationRequested)
                {
                    return; // IPC-REFACTOR: stream cancelled by our own Stop()/teardown.
                }
                catch (Exception ex) when (ex is RpcException || ex is SocketException || ex is IOException)
                {
                    // IPC-REFACTOR: transient connect/stream failure (e.g. server not yet listening during AgIO auto-start) — back off and retry.
                    IsUDPSendConnected = false;
                    _channel?.Dispose();
                    _channel = null;

                    if (attempt >= maxAttempts)
                    {
                        mf.Tls.WriteErrorLog("UDPcomm/SubscribeLoop: " + ex.Message); // IPC-REFACTOR: preserved bind-failure UX via the existing error log (was the legacy StartUDPServer catch).
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

        // IPC-REFACTOR: byte-keyed switch(PGN)/switch(SubPGN) dispatch replaced by typed switch(envelope.PayloadCase); observes exactly the two payloads the legacy listener handled.
        private void HandleData(PgnEnvelope envelope)
        {
            try
            {
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
