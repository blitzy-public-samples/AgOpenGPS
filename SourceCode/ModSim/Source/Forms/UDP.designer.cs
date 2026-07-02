using System;
using System.Drawing;
// IPC-REFACTOR: UDP socket usings replaced by gRPC client + Protobuf usings.
// Removed: System.Net + System.Net.Sockets (UDP loopback transport) and System.Text
// (Encoding.ASCII no longer used after SendUDPMessage was re-routed to typed injection).
// The few socket types still required by the Unix-domain-socket connect callback are
// fully-qualified as System.Net.Sockets.* (CreateIpcChannel / IsTransientConnect) so this
// file stays warning-clean (no unused usings / IDE0005) under the Release
// TreatWarningsAsErrors gate.
// net48 DEVIATION (Authority-Hierarchy Case 3 — surfaced, NOT resolved): SocketsHttpHandler
// and its ConnectCallback are .NET Core 2.1+ / .NET 6+ only and do NOT exist on net48. ModSim
// currently inherits net48 while AgOpenGPS.Ipc multi-targets net8.0/netstandard2.0; the ModSim
// host retarget to net6.0+ is the gating prerequisite for an actual build. This client is
// implemented exactly as the .NET 6+ design the prompt designates (mirrors the GPS sibling).
using System.Windows.Forms;
using AgOpenGPS.Ipc;                    // IpcConstants, PgnEnvelope, TelemetryService, CommandService, all *Msg types
using Google.Protobuf.WellKnownTypes;   // Empty (StreamTelemetry request)
using Grpc.Core;                        // RpcException, StatusCode, AsyncServerStreamingCall<T>, MoveNext
using Grpc.Net.Client;                  // GrpcChannel, GrpcChannelOptions
using System.IO;                        // IOException (transient-connect classification)
using System.IO.Pipes;                  // NamedPipeClientStream, PipeDirection, PipeOptions (Windows transport)
using System.Net.Http;                  // SocketsHttpHandler (gRPC HTTP/2 over UDS / named pipe)
using System.Runtime.InteropServices;   // RuntimeInformation, OSPlatform (OS-branched endpoint)
using System.Threading;                 // CancellationTokenSource, CancellationToken
using System.Threading.Tasks;           // Task (async connect/subscribe + fire-and-forget inject)
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace ModSim
{
    public partial class FormSim
    {
        // IPC-REFACTOR: UDP socket/endpoint/discovery-buffer fields removed (removed-symbol cascade:
        // UDPSocket, endPointUDP, epAgIO [:9999], buffer[1024], helloFromAgIO, ipCurrent). AgIO's
        // hardware-facing UDP discovery path is unchanged (AAP §0.1.1); only ModSim's software transport
        // migrates to a gRPC client.
        // IPC-REFACTOR: isUDPNetworkConnected renamed to isIpcConnected; gates outbound injection.
        private bool isIpcConnected;

        // IPC-REFACTOR: UDP socket fields replaced by a gRPC channel, the Telemetry (observe) and Command
        // (inject) service clients, and a subscription CancellationTokenSource. Also referenced by the
        // FormSim.cs FormClosing teardown (same partial class, so private is fine).
        private GrpcChannel _channel;
        private TelemetryService.TelemetryServiceClient _telemetryClient;
        private CommandService.CommandServiceClient _commandClient;
        private CancellationTokenSource _cts;

        //initialize udp network
        // IPC-REFACTOR: UDP socket bind(:8888) + BeginReceiveFrom replaced by a gRPC client channel and a
        // background StreamTelemetry subscription. Method name preserved (called from FormSim.cs L44).
        public void LoadUDPNetwork()
        {
            // IPC-REFACTOR: host-IP enumeration (Dns/AddressFamily) removed; surface the connection status
            // instead. lblIP status behavior kept equivalent (connection state shown to the user).
            lblIP.Text = "Connecting…";

            _cts = new CancellationTokenSource();

            // Fire-and-forget: ConnectAndSubscribeAsync owns its own try/catch on every path and never
            // throws unobserved (the retry backoff can sum to ~15.5 s, so it must run off the UI thread).
            _ = ConnectAndSubscribeAsync(_cts.Token);
        }

        // IPC-REFACTOR: builds the loopback-only gRPC channel (no TLS, no auth — security parity with the
        // legacy UDP loopback posture). "http://localhost" is a placeholder plaintext HTTP/2 authority; the
        // ACTUAL endpoint is dialed by the SocketsHttpHandler.ConnectCallback to IpcConstants.AgIoSocketPath
        // (Windows named pipe / Unix domain socket). Static: no instance state. Mirrors the GPS sibling.
        private static GrpcChannel CreateIpcChannel()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Windows loopback transport: named pipe. Strip the @"\\.\pipe\" prefix to get the name.
                        string socketPath = IpcConstants.AgIoSocketPath;
                        string pipeName = socketPath.StartsWith(@"\\.\pipe\", StringComparison.Ordinal)
                            ? socketPath.Substring(@"\\.\pipe\".Length)
                            : socketPath;
                        NamedPipeClientStream pipe = new NamedPipeClientStream(
                            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                        return pipe;
                    }

                    // Unix/macOS loopback transport: Unix domain socket. Socket types are fully-qualified so
                    // this file no longer needs `using System.Net.Sockets;`.
                    System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.Unix,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Unspecified);
                    await socket.ConnectAsync(
                        new System.Net.Sockets.UnixDomainSocketEndPoint(IpcConstants.AgIoSocketPath),
                        cancellationToken).ConfigureAwait(false);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
            };

            return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
        }

        // IPC-REFACTOR: UDP receive socket + switch(data[3]) replaced by a TelemetryService.StreamTelemetry
        // subscription with typed PayloadCase dispatch.
        // IPC-REFACTOR: bind-failure MessageBox replaced by connect retry/backoff (5x: 500/1000/2000/4000/8000 ms)
        // before surfacing the SAME error dialog. AgIO is auto-started by AOG, so a startup race (server not yet
        // listening) is expected and retried. Mirrors the GPS sibling; the error path uses MessageBox because
        // ModSim has no FormDialog/Log.EventWriter. This task owns its try/catch on every path (never unobserved).
        private async Task ConnectAndSubscribeAsync(CancellationToken token)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int delayMs = 500 * (1 << attempt); // 500 / 1000 / 2000 / 4000 / 8000 ms
                try
                {
                    // Dispose any channel left over from a prior failed attempt before recreating.
                    _channel?.Dispose();
                    _channel = CreateIpcChannel();
                    _telemetryClient = new TelemetryService.TelemetryServiceClient(_channel);
                    _commandClient = new CommandService.CommandServiceClient(_channel);

                    // MoveNext loop (more portable than ReadAllAsync — no IAsyncEnumerable dependency).
                    using (var call = _telemetryClient.StreamTelemetry(new Empty(), cancellationToken: token))
                    {
                        isIpcConnected = true;
                        try { BeginInvoke((MethodInvoker)(() => lblIP.Text = "Connected")); }
                        catch { /* handle not yet created / form disposing; ignore */ }

                        while (await call.ResponseStream.MoveNext(token).ConfigureAwait(false))
                        {
                            PgnEnvelope envelope = call.ResponseStream.Current;

                            // Marshal each envelope to the UI thread exactly as the legacy UDP callback did
                            // (only the SOURCE changed: UDP datagram -> telemetry stream item).
                            try { BeginInvoke((MethodInvoker)(() => ReceiveFromUDP(envelope))); }
                            catch { /* form disposing; ignore — parity with the legacy empty catch */ }
                        }
                    }

                    // Stream ended (server closed it cleanly) — graceful stop, mirrors GPS.
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Cooperative shutdown requested; no error dialog on cancellation.
                    return;
                }
                catch (Exception ex) when (attempt < 4 && IsTransientConnect(ex))
                {
                    isIpcConnected = false;
                    try { await Task.Delay(delayMs, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
                catch (Exception ex)
                {
                    isIpcConnected = false;

                    // Cancellation surfacing as a non-OperationCanceled exception (e.g. RpcException/Cancelled)
                    // during shutdown is not an error — exit quietly.
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    // IPC-REFACTOR: preserve ModSim's legacy MessageBox error path (no FormDialog/Log.EventWriter
                    // exists here). Reached only on a non-transient failure or after the 5th attempt failed.
                    try
                    {
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            MessageBox.Show(ex.Message, "Serious Network Connection Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            lblIP.Text = "Error";
                        }));
                    }
                    catch { /* form may be closing; ignore */ }
                    return;
                }
            }
        }

        // IPC-REFACTOR: a connect/stream failure is transient (retryable) when the server is not yet listening
        // — AgIO is auto-started by AOG so a startup race is expected. Static: no instance state.
        private static bool IsTransientConnect(Exception ex)
        {
            return (ex is RpcException rpc && rpc.StatusCode == StatusCode.Unavailable)
                || ex is System.Net.Sockets.SocketException
                || ex is IOException;
        }

        #region Send UDP

        // IPC-REFACTOR: fire-and-forget UDP send (BeginSendTo) replaced by CommandService.InjectTelemetry.
        // The byte[] overload now parses the one frame ModSim still produces (the PGN_253 steer reply built in
        // case 254) into a typed SteerModuleResponseMsg. Signature preserved (called via SendUDPMessage(PGN_253)).
        public void SendUDPMessage(byte[] byteData)
        {
            // Guard covers every index accessed below (up to byteData[12]); the PGN_253 steer reply is 14 bytes.
            if (byteData == null || byteData.Length < 13)
            {
                return;
            }

            // The steer reply is the only byte[] frame produced after the discovery cases were removed.
            if (byteData[3] == 253)
            {
                SteerModuleResponseMsg msg = new SteerModuleResponseMsg
                {
                    ActualSteerAngle = (short)(byteData[5] | (byteData[6] << 8)),
                    Heading = 9999,   // legacy "N/A" sentinel; HeadingValid stays false (proto rule: raw == 9999)
                    Roll = 8888,      // legacy "N/A" sentinel; RollValid stays false (proto rule: raw == 8888)
                    SwitchStatus = byteData[11],
                    Pwm = byteData[12]
                };

                InjectEnvelope(new PgnEnvelope
                {
                    SchemaVersion = IpcConstants.SchemaVersion,
                    SteerModuleResponse = msg
                });
            }
            // else: no other byte[] frame is produced after discovery-case removal; ignore unknown frames.
        }

        // IPC-REFACTOR: NMEA text has no PGN-catalog equivalent; inject the simulator's already-computed GPS
        // state as a typed GpsPositionMsg so downstream observers still receive equivalent telemetry (do NOT
        // silently drop the simulator's GPS emission). Signature preserved (Controls.Designer.cs calls it once
        // per enabled NMEA sentence, up to 8x/tick, with identical field values — injecting up to 8
        // GpsPositionMsg/tick is acceptable because telemetry is self-superseding). The simulator fields live in
        // Controls.Designer.cs but are members of the SAME partial class FormSim, so they are accessible here.
        public void SendUDPMessage(string message)
        {
            GpsPositionMsg msg = new GpsPositionMsg
            {
                Latitude = latitude,
                Longitude = longitude,
                HeadingTrue = (float)degrees,            // degrees == ToDegrees * headingTrue
                Speed = (float)speed,
                Roll = (float)roll,
                Altitude = (float)altitude,
                Satellites = (uint)sats,                 // simulator constant (12)
                FixQuality = (uint)fixQuality,           // simulator constant (8)
                Hdop = (uint)Math.Round(HDOP * 10.0),    // TODO: confirm HDOP scaling vs the 0xD6 byte table
                ImuHeading = (uint)headingIMU,
                ImuRoll = rollIMU
                // TODO: confirm GpsPositionMsg field mapping vs NMEA sentence content (HeadingDual/Age/ImuPitch/
                // ImuYawRate left default; the `message` NMEA string is intentionally unused after the re-route).
            };

            InjectEnvelope(new PgnEnvelope
            {
                SchemaVersion = IpcConstants.SchemaVersion,
                GpsPosition = msg
            });
        }

        // IPC-REFACTOR: fire-and-forget UDP send replaced by CommandService.InjectTelemetry(PgnEnvelope).
        // A synchronous unary RPC on the UI/sim thread would block, so injection is async fire-and-forget with
        // swallowed errors (mirrors the legacy empty-catch UDP send). Gated on a live connection + client.
        private void InjectEnvelope(PgnEnvelope envelope)
        {
            if (!isIpcConnected || _commandClient == null)
            {
                return;
            }

            _ = InjectEnvelopeAsync(envelope);
        }

        private async Task InjectEnvelopeAsync(PgnEnvelope envelope)
        {
            try
            {
                await _commandClient.InjectTelemetryAsync(envelope, cancellationToken: _cts.Token).ConfigureAwait(false);
            }
            catch (RpcException) { /* swallow — the legacy UDP send swallowed errors too */ }
            catch (OperationCanceledException) { /* shutdown requested */ }
        }

        // IPC-REFACTOR: UDP async send callback (SendDataUDPAsync) removed; injection now uses
        // CommandService.InjectTelemetryAsync (see InjectEnvelopeAsync above).

        #endregion

        #region Receive UDP

        // IPC-REFACTOR: UDP async receive callback (ReceiveDataUDPAsync) removed; inbound frames now flow from
        // the TelemetryService.StreamTelemetry subscription (the ConnectAndSubscribeAsync MoveNext loop) into
        // ReceiveFromUDP(PgnEnvelope) via BeginInvoke.

        static byte [] PGN_253 = { 128, 129, 126, 253, 8, 0, 0, 0, 0, 0, 0, 0, 0, 12 };
        int PGN_253_Size = PGN_253.Length - 1;

        // IPC-REFACTOR: UDP discovery hello frames (helloFromAutoSteer / helloFromMachine / helloFromIMU)
        // removed together with the discovery cases (200/201/202) that were their only users.

        //settings pgn
        static byte[] PGN_237 = { 0x80, 0x81, 0x7f, 237, 8, 1, 2, 3, 4, 0, 0, 0, 0, 0xCC };
        int PGN_237_Size = PGN_237.Length - 1;


        //Relays
        //bool isRelayActiveHigh = true;
        byte relay = 0, relayHi = 0, uTurn = 0;
        byte xte = 0;

        //Switches
        int remoteSwitch = 1, workSwitch = 1, steerSwitch = 1, switchByte = 0;

        //On Off
        byte guidanceStatus = 0;
        byte prevGuidanceStatus = 0;
        bool guidanceStatusChanged = false;

        //speed sent as *10
        double gpsSpeed = 0, gpsSpeedMM = 0;

        //steering variables
        double steerAngleActual = 0;
        double steerAngleSetPoint = 0; //the desired angle from AgOpen
        //int steeringPosition = 0; //from steering sensor
        //double steerAngleError = 0; //setpoint - actual

        //Machine module
        int hydLift = 0;
        int tramline = 0;

        int relayLoM = 0;
        int relayHiM = 0;

        // IPC-REFACTOR: typed PgnEnvelope replaces the raw byte[] frame; dispatch on PayloadCase instead of
        // data[3]. Only the data SOURCE changes (byte offset -> typed proto property); every lbl*/state
        // assignment below is preserved verbatim so the simulator UI shows identical values. ReceiveFromUDP is
        // internal-only, so this signature change is safe (fed by the StreamTelemetry reader via BeginInvoke).
        private void ReceiveFromUDP(PgnEnvelope envelope)
        {
            try
            {
                // IPC-REFACTOR: the data[0..2]==0x80,0x81,0x7F header check is dropped — the typed stream carries
                // no framing bytes. The outer try/catch shape is retained (the empty catch is pre-existing).
                switch (envelope.PayloadCase)
                {
                    case PgnEnvelope.PayloadOneofCase.AutoSteerData: // PGN 0xFE (254)
                        {
                            AutoSteerDataMsg msg = envelope.AutoSteerData;

                            gpsSpeed = ((double)msg.Speed) * 0.1;

                            prevGuidanceStatus = guidanceStatus;

                            guidanceStatus = (byte)msg.Status;
                            guidanceStatusChanged = (guidanceStatus != prevGuidanceStatus);

                            lblGuidanceStatus.Text = guidanceStatus.ToString();
                            lblSteerSwitchStatus.Text = steerSwitch.ToString();

                            //if (steerConfig.SteerButton == 1)
                            //{
                            //    if (guidanceStatus == 1) steerSwitch = 0;
                            //}

                            //Bit 8,9    set point steer angle * 100 is sent
                            steerAngleSetPoint = (float)msg.CommandedSteerAngle * 0.01; //high low bytes

                            //Bit 10 Tram
                            xte = (byte)msg.LineDistance;

                            //Bit 11
                            relay = (byte)msg.SectionControl18;

                            //Bit 12
                            relayHi = (byte)msg.SectionControl916;

                            byte swap = swapBits[relay];
                            lbl1To8.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[relayHi];
                            lbl9To16.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            //----------------------------------------------------------------------------
                            //Serial Send to agopenGPS

                            int sa = (int)(steerAngleActual * 100);

                            PGN_253[5] = unchecked((byte)((int)(sa)));
                            PGN_253[6] = unchecked((byte)((int)(sa) >> 8));

                            //heading
                            PGN_253[7] = unchecked((byte)((int)(9999)));
                            PGN_253[8] = unchecked((byte)((int)(9999) >> 8));

                            //roll
                            PGN_253[9] = unchecked((byte)((int)(8888)));
                            PGN_253[10] = unchecked((byte)((int)(8888) >> 8));

                            switchByte = 0;
                            switchByte |= ((int)remoteSwitch << 2); //put remote in bit 2
                            switchByte |= (steerSwitch << 1);   //put steerSwitch status in bit 1 position
                            switchByte |= workSwitch;

                            PGN_253[11] = (byte)switchByte;
                            PGN_253[12] = 44;  //(uint8_t)pwmDisplay;

                            // IPC-REFACTOR: CRC check removed — HTTP/2 frame integrity provides equivalent byte-level guarantees.

                            SendUDPMessage(PGN_253);

                            if (btnSteerButtonRemote.BackColor == Color.Green)
                            {
                                btnSteerButtonRemote.BackColor = Color.White;
                            }

                            break;
                        }

                    case PgnEnvelope.PayloadOneofCase.AutoSteerSettings: // PGN 0xFC (252)
                        {
                            AutoSteerSettingsMsg msg = envelope.AutoSteerSettings;

                            //PID values
                            steerSettings.Kp = (byte)msg.GainProportionalKp;   // read Kp from AgOpenGPS
                            lblKp.Text = steerSettings.Kp.ToString();

                            steerSettings.highPWM = (byte)msg.HighPwm; // read high pwm
                            lblHighPWM.Text = steerSettings.highPWM.ToString();

                            steerSettings.lowPWM = (byte)msg.LowPwm;   // read lowPWM from AgOpenGPS

                            steerSettings.minPWM = (byte)msg.MinPwm; //read the minimum amount of PWM for instant on\
                            lblMinPWM.Text = steerSettings.minPWM.ToString();

                            float temp = steerSettings.minPWM;
                            temp *= 1.2f;
                            steerSettings.lowPWM = (byte)temp;
                            lblLowPWM.Text = steerSettings.lowPWM.ToString();

                            steerSettings.steerSensorCounts = (byte)msg.CountsPerDegree; //sent as setting displayed in AOG
                            lblWAS_Counts.Text = steerSettings.steerSensorCounts.ToString();

                            // IPC-REFACTOR: was_offset is a single combined value in proto (legacy read lo @data[10] then |= hi @data[11]<<8).
                            steerSettings.wasOffset = (int)msg.WasOffset;  //read was zero offset
                            lblWAS_Offset.Text = steerSettings.wasOffset.ToString();

                            steerSettings.AckermanFix = (float)msg.Ackerman * 0.01;
                            lblAckerman.Text = (steerSettings.AckermanFix * 100).ToString("N0") + "%";

                            break;
                        }

                    case PgnEnvelope.PayloadOneofCase.AutoSteerConfig: // PGN 0xFB (251)
                        {
                            AutoSteerConfigMsg msg = envelope.AutoSteerConfig;

                            int sett = (int)msg.Set0; //setting0 (byte 5)

                            if ((sett & (1 << 0)) != 0) steerConfig.InvertWAS = 1; else steerConfig.InvertWAS = 0;
                            lblInvertWAS.Text = steerConfig.InvertWAS.ToString();

                            if ((sett & (1 << 1)) != 0) steerConfig.IsRelayActiveHigh = 1; else steerConfig.IsRelayActiveHigh = 0;
                            lblRelayActHigh.Text = steerConfig.IsRelayActiveHigh.ToString();

                            if ((sett & (1 << 2)) != 0) steerConfig.MotorDriveDirection = 1; else steerConfig.MotorDriveDirection = 0;
                            lblMotorDirection.Text = steerConfig.MotorDriveDirection.ToString();

                            if ((sett & (1 << 3)) != 0) steerConfig.SingleInputWAS = 1; else steerConfig.SingleInputWAS = 0;
                            lblSingleInputWAS.Text = steerConfig.SingleInputWAS.ToString();

                            if ((sett & (1 << 4)) != 0) steerConfig.CytronDriver = 1; else steerConfig.CytronDriver = 0;
                            lblCytron.Text = steerConfig.CytronDriver.ToString();

                            if ((sett & (1 << 5)) != 0) steerConfig.SteerSwitch = 1; else steerConfig.SteerSwitch = 0;
                            lblSteerSw.Text = steerConfig.SteerSwitch.ToString();

                            if ((sett & (1 << 6)) != 0) steerConfig.SteerButton = 1; else steerConfig.SteerButton = 0;
                            lblSteerBtn.Text = steerConfig.SteerButton.ToString();

                            if ((sett & (1 << 7)) != 0) steerConfig.ShaftEncoder = 1; else steerConfig.ShaftEncoder = 0;
                            lblShaftEnc.Text = steerConfig.ShaftEncoder.ToString();

                            steerConfig.PulseCountMax = (byte)msg.MaxPulse;
                            lblPulseCounts.Text = steerConfig.PulseCountMax.ToString();

                            //was speed
                            //data[7];

                            // TODO: proto field 'ackerman_fix' occupies AutoSteerConfig byte 8, which ModSim decodes as setting1 (Danfoss/pressure/current/Y-axis bits); confirm semantic alignment against docs/pgn-protocol.md byte tables.
                            sett = (int)msg.AckermanFix; //setting1 - Danfoss valve etc (byte 8)

                            if ((sett & (1 << 0)) != 0) steerConfig.IsDanfoss = 1; else steerConfig.IsDanfoss = 0;
                            lblDanfoss.Text = steerConfig.IsDanfoss.ToString();

                            if ((sett & (1 << 1)) != 0) steerConfig.PressureSensor = 1; else steerConfig.PressureSensor = 0;
                            lblPressure.Text = steerConfig.PressureSensor.ToString();

                            if ((sett & (1 << 2)) != 0) steerConfig.CurrentSensor = 1; else steerConfig.CurrentSensor = 0;
                            lblCurrent.Text = steerConfig.CurrentSensor.ToString();

                            if ((sett & (1 << 3)) != 0) steerConfig.IsUseY_Axis = 1; else steerConfig.IsUseY_Axis = 0;
                            lblUseY_Axis.Text = steerConfig.IsUseY_Axis.ToString();
                            break;
                        }

                    case PgnEnvelope.PayloadOneofCase.MachineData: // PGN 0xEF (239) machine data
                        {
                            MachineDataMsg msg = envelope.MachineData;

                            uTurn = (byte)msg.UTurn;
                            lblUTurn.Text=uTurn.ToString();

                            gpsSpeedMM = (double)msg.Speed;//actual speed times 4, single uint8_t
                            gpsSpeedMM *= 0.1;
                            lblGPSSpeedMM.Text = gpsSpeedMM.ToString("N1");

                            hydLift = (int)msg.HydLift;
                            lblHydLift.Text = hydLift.ToString();

                            tramline = (int)msg.Tram;  //bit 0 is right bit 1 is left
                            lblTram.Text = tramline.ToString();

                            relayLoM = (int)msg.SectionControl18;          // read relay control from AgOpenGPS
                            relayHiM = (int)msg.SectionControl916;

                            byte swap = swapBits[(byte)relayLoM];
                            lbl1To8M.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[(byte)relayHiM];
                            lbl9To16M.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            break;
                        }

                    case PgnEnvelope.PayloadOneofCase.ExtendedSectionControl: // PGN 0xE5 (229)
                        {
                            ExtendedSectionControlMsg msg = envelope.ExtendedSectionControl;

                            // IPC-REFACTOR: legacy read 8 individual bytes data[5..12]; proto packs them as a single
                            // uint64 (8 bitmask bytes, LSB-first). Unpack LSB-first to reproduce bytes 5..12 exactly.
                            // TODO: confirm ExtendedSectionControl section byte order matches the AOG producer packing in SourceCode/GPS/Forms/PGN.Designer.cs.
                            ulong sections = msg.Sections;
                            byte b5 = (byte)(sections & 0xFF);
                            byte b6 = (byte)((sections >> 8) & 0xFF);
                            byte b7 = (byte)((sections >> 16) & 0xFF);
                            byte b8 = (byte)((sections >> 24) & 0xFF);
                            byte b9 = (byte)((sections >> 32) & 0xFF);
                            byte b10 = (byte)((sections >> 40) & 0xFF);
                            byte b11 = (byte)((sections >> 48) & 0xFF);
                            byte b12 = (byte)((sections >> 56) & 0xFF);

                            byte swap = swapBits[b5];
                            lblZone1.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b6];
                            lblZone2.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b7];
                            lblZone3.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b8];
                            lblZone4.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b9];
                            lblZone5.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b10];
                            lblZone6.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b11];
                            lblZone7.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            swap = swapBits[b12];
                            lblZone8.Text = Convert.ToString(swap, 2).PadLeft(8, '0');

                            break;
                        }

                    case PgnEnvelope.PayloadOneofCase.MachineConfig: // PGN 0xEE (238)
                        {
                            MachineConfigMsg msg = envelope.MachineConfig;

                            aogConfig.raiseTime = (byte)msg.RaiseTime;
                            lblRaiseTime.Text = aogConfig.raiseTime.ToString();

                            aogConfig.lowerTime = (byte)msg.LowerTime;
                            lblLowerTime.Text = aogConfig.lowerTime.ToString();

                            aogConfig.enableToolLift = (byte)msg.EnableHyd;
                            lblLiftEnable.Text = aogConfig.enableToolLift.ToString();

                            //set1
                            int sett = (int)msg.Set0;  //setting0
                            if ((sett & (1 << 0)) != 0)
                                aogConfig.isRelayActiveHigh = 1; else aogConfig.isRelayActiveHigh = 0;
                            lblRelayActiveHigh.Text = aogConfig.isRelayActiveHigh.ToString();

                            aogConfig.user1 = (byte)msg.User1;
                            lblUser1.Text = msg.User1.ToString();

                            aogConfig.user2 = (byte)msg.User2;
                            lblUser2.Text = msg.User2.ToString();

                            aogConfig.user3 = (byte)msg.User3;
                            lblUser3.Text = msg.User3.ToString();

                            aogConfig.user4 = (byte)msg.User4;
                            lblUser4.Text = msg.User4.ToString();

                            break;
                        }

                    // IPC-REFACTOR: UDP module-discovery (hello / subnet-scan / scan-reply) cases 200/201/202 and the
                    // trailing default are not applicable to a gRPC client; AgIO's hardware-UDP discovery path is
                    // unchanged (AAP §0.1.1). The default case (legacy commented-out SendToLoopBackMessageAOG echo)
                    // is dropped with them.
                    // IPC-REFACTOR: subnet persistence (Properties.Settings.Default.etIP_Subnet*) was only reachable
                    // from the removed UDP discovery path (case 201); the subnet UI is now inert (the settings keys
                    // are retained, out of scope). The TimedMessageBox/YesMessageBox helpers remain defined (and now
                    // uncalled) in the out-of-scope Controls.Designer.cs, which stays byte-for-byte unchanged.
                }
            }
            catch
            {

            }
        }

        #endregion

        static byte [] swapBits = {
        0x00, 0x80, 0x40, 0xc0, 0x20, 0xa0, 0x60, 0xe0,
        0x10, 0x90, 0x50, 0xd0, 0x30, 0xb0, 0x70, 0xf0,
        0x08, 0x88, 0x48, 0xc8, 0x28, 0xa8, 0x68, 0xe8,
        0x18, 0x98, 0x58, 0xd8, 0x38, 0xb8, 0x78, 0xf8,
        0x04, 0x84, 0x44, 0xc4, 0x24, 0xa4, 0x64, 0xe4,
        0x14, 0x94, 0x54, 0xd4, 0x34, 0xb4, 0x74, 0xf4,
        0x0c, 0x8c, 0x4c, 0xcc, 0x2c, 0xac, 0x6c, 0xec,
        0x1c, 0x9c, 0x5c, 0xdc, 0x3c, 0xbc, 0x7c, 0xfc,
        0x02, 0x82, 0x42, 0xc2, 0x22, 0xa2, 0x62, 0xe2,
        0x12, 0x92, 0x52, 0xd2, 0x32, 0xb2, 0x72, 0xf2,
        0x0a, 0x8a, 0x4a, 0xca, 0x2a, 0xaa, 0x6a, 0xea,
        0x1a, 0x9a, 0x5a, 0xda, 0x3a, 0xba, 0x7a, 0xfa,
        0x06, 0x86, 0x46, 0xc6, 0x26, 0xa6, 0x66, 0xe6,
        0x16, 0x96, 0x56, 0xd6, 0x36, 0xb6, 0x76, 0xf6,
        0x0e, 0x8e, 0x4e, 0xce, 0x2e, 0xae, 0x6e, 0xee,
        0x1e, 0x9e, 0x5e, 0xde, 0x3e, 0xbe, 0x7e, 0xfe,
        0x01, 0x81, 0x41, 0xc1, 0x21, 0xa1, 0x61, 0xe1,
        0x11, 0x91, 0x51, 0xd1, 0x31, 0xb1, 0x71, 0xf1,
        0x09, 0x89, 0x49, 0xc9, 0x29, 0xa9, 0x69, 0xe9,
        0x19, 0x99, 0x59, 0xd9, 0x39, 0xb9, 0x79, 0xf9,
        0x05, 0x85, 0x45, 0xc5, 0x25, 0xa5, 0x65, 0xe5,
        0x15, 0x95, 0x55, 0xd5, 0x35, 0xb5, 0x75, 0xf5,
        0x0d, 0x8d, 0x4d, 0xcd, 0x2d, 0xad, 0x6d, 0xed,
        0x1d, 0x9d, 0x5d, 0xdd, 0x3d, 0xbd, 0x7d, 0xfd,
        0x03, 0x83, 0x43, 0xc3, 0x23, 0xa3, 0x63, 0xe3,
        0x13, 0x93, 0x53, 0xd3, 0x33, 0xb3, 0x73, 0xf3,
        0x0b, 0x8b, 0x4b, 0xcb, 0x2b, 0xab, 0x6b, 0xeb,
        0x1b, 0x9b, 0x5b, 0xdb, 0x3b, 0xbb, 0x7b, 0xfb,
        0x07, 0x87, 0x47, 0xc7, 0x27, 0xa7, 0x67, 0xe7,
        0x17, 0x97, 0x57, 0xd7, 0x37, 0xb7, 0x77, 0xf7,
        0x0f, 0x8f, 0x4f, 0xcf, 0x2f, 0xaf, 0x6f, 0xef,
        0x1f, 0x9f, 0x5f, 0xdf, 0x3f, 0xbf, 0x7f, 0xff,  };

    }



    public static class steerConfig
    {            
        public static byte InvertWAS = 0;
        public static byte IsRelayActiveHigh = 0; //if zero, active low (default)
        public static byte MotorDriveDirection = 0;
        public static byte SingleInputWAS = 1;
        public static byte CytronDriver = 1;
        public static byte SteerSwitch = 0;  //1 if switch selected
        public static byte SteerButton = 0;  //1 if button selected
        public static byte ShaftEncoder = 0;
        public static byte PressureSensor = 0;
        public static byte CurrentSensor = 0;
        public static byte PulseCountMax = 5;
        public static byte IsDanfoss = 0;
        public static byte IsUseY_Axis = 0;
    }

    public static class steerSettings
    {
        public static byte Kp = 120;  //proportional gain
        public static byte lowPWM = 30;  //band of no action
        public static int wasOffset = 0;
        public static byte minPWM = 25;
        public static byte highPWM = 160;//max PWM value
        public static double steerSensorCounts = 30;
        public static double AckermanFix = 1;     //sent as percent
    }

    //relay module config
    public static class aogConfig
    {
        public static byte raiseTime = 2;
        public static byte lowerTime = 4;
        public static byte enableToolLift = 0;
        public static byte isRelayActiveHigh = 0; //if zero, active low (default)
        public static byte user1 = 0; 
        public static byte user2 = 0;
        public static byte user3 = 0;
        public static byte user4 = 0;
    }
}
