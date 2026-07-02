using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AgOpenGPS.Helpers;
// IPC-REFACTOR: gRPC client transport (replaces System.Net UDP loopback). The loopback channel build and the
// connect retry/backoff now live in the shared AgOpenGPS.Ipc helpers (IpcChannelFactory + IpcTelemetrySubscriber),
// so the net48-incompatible SocketsHttpHandler / named-pipe / Unix-domain-socket types are no longer referenced
// here and their usings (System.Net.Http, System.IO.Pipes, System.Runtime.InteropServices, Google.Protobuf.
// WellKnownTypes) are removed. The single retained socket field (loopBackSocket, kept null for shutdown-path
// compatibility) is fully-qualified below so this file stays warning-clean (no unused usings).
using AgOpenGPS.Ipc;                 // PgnEnvelope, IpcConstants, IpcTelemetrySubscriber, all *Msg types, Command client, CommandAck
using Grpc.Core;                     // RpcException, AsyncUnaryCall<T> (SendCommandAckAsync helper)
using Grpc.Net.Client;               // GrpcChannel (retained channel field type / RunAsync onChannel callback)
using System.Threading;              // CancellationTokenSource
using System.Threading.Tasks;        // Task (subscribe task + fire-and-forget ack)

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        // - App Sockets  -----------------------------------------------------
        // IPC-REFACTOR: loopBackSocket retained (null) for FormGPS shutdown-path compatibility; UDP socket eliminated.
        // Fully-qualified type so the file no longer needs `using System.Net.Sockets;`.
        private System.Net.Sockets.Socket loopBackSocket;

        // IPC-REFACTOR: UDP broadcast to 127.255.255.255:17777 replaced by TelemetryService gRPC server-side stream.
        // IPC-REFACTOR: UDP loopback endpoint/receive buffer removed with the socket transport.
        // (removed fields: epAgIO [127.255.255.255:17777], endPointLoopBack, loopBuffer[1024])

        // Status delegate
        public int missedSentenceCount = 0;
        public int udpWatchLimit = 70;

        // IPC-REFACTOR: udpWatch retained for FormGPS compatibility (FormGPS.cs calls udpWatch.Start()); throttle now uses _lastFixAt delta.
        private readonly Stopwatch udpWatch = new Stopwatch();

        // IPC-REFACTOR: gRPC channel/client + stream-side throttle state (no default initializers; each assigned via a statement and read).
        private DateTime _lastFixAt;
        private GrpcChannel _ipcChannel;
        private CommandService.CommandServiceClient _commandClient;
        private CancellationTokenSource _ipcCts;

        // IPC-REFACTOR: inbound dispatch now switches on the strongly-typed PgnEnvelope.PayloadOneofCase produced by the
        // proto3 oneof (was `switch (data[3])` over the raw PGN byte). Fed by the TelemetryService stream reader
        // (ConnectAndSubscribeAsync) via BeginInvoke, replacing the removed UDP ReceiveAppData callback. Each case below
        // preserves the exact downstream domain logic, sentinels and scaling of the legacy byte-offset parsing; only the
        // field SOURCE changes (byte offset -> typed property). The // PGN 0xNN comments are retained for byte-table traceability.
        private void ReceiveFromAgIO(PgnEnvelope envelope)
        {
            // IPC-REFACTOR: CRC check removed — HTTP/2 frame integrity provides equivalent byte-level guarantees.
            switch (envelope.PayloadCase)
            {
                case PgnEnvelope.PayloadOneofCase.GpsPosition: // PGN 0xD6
                    {
                        // IPC-REFACTOR: 70ms udpWatchLimit GPS-fix throttle migrated from udpWatch.Elapsed to _lastFixAt DateTime delta; interval preserved.
                        if ((DateTime.Now - _lastFixAt).TotalMilliseconds < udpWatchLimit)
                        {
                            missedSentenceCount++;
                            return;
                        }
                        _lastFixAt = DateTime.Now;

                        double Lon = envelope.GpsPosition.Longitude;
                        double Lat = envelope.GpsPosition.Latitude;

                        if (Lon != double.MaxValue && Lat != double.MaxValue)
                        {
                            if (timerSim.Enabled) DisableSim();

                            AppModel.CurrentLatLon = new Wgs84(Lat, Lon);

                            GeoCoord fixCoord = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(AppModel.CurrentLatLon);
                            pn.fix.northing = fixCoord.Northing;
                            pn.fix.easting = fixCoord.Easting;

                            //From dual antenna heading sentences
                            float temp = envelope.GpsPosition.HeadingDual;
                            if (temp != float.MaxValue)
                            {
                                pn.headingTrueDual = temp + pn.headingTrueDualOffset;
                                if (pn.headingTrueDual >= 360) pn.headingTrueDual -= 360;
                                else if (pn.headingTrueDual < 0) pn.headingTrueDual += 360;

                                if (ahrs.isDualAsIMU) ahrs.imuHeading = pn.headingTrueDual;
                            }

                            //from single antenna sentences (VTG,RMC)
                            pn.headingTrue = envelope.GpsPosition.HeadingTrue;

                            //always save the speed.
                            temp = envelope.GpsPosition.Speed;
                            if (temp != float.MaxValue)
                            {
                                pn.vtgSpeed = temp;
                            }

                            //roll in degrees
                            temp = envelope.GpsPosition.Roll;
                            if (temp != float.MaxValue)
                            {
                                if (ahrs.isRollInvert) temp *= -1;
                                ahrs.imuRoll = temp - ahrs.rollZero;
                            }
                            if (temp == float.MinValue)
                                ahrs.imuRoll = 0;

                            //altitude in meters
                            temp = envelope.GpsPosition.Altitude;
                            if (temp != float.MaxValue)
                                pn.altitude = temp;

                            uint sats = envelope.GpsPosition.Satellites;
                            if (sats != ushort.MaxValue)
                                pn.satellitesTracked = (int)sats;

                            uint fix = envelope.GpsPosition.FixQuality;
                            if (fix != byte.MaxValue)
                                pn.fixQuality = (int)fix;

                            uint hdop = envelope.GpsPosition.Hdop;
                            if (hdop != ushort.MaxValue)
                                pn.hdop = hdop * 0.01;

                            uint age = envelope.GpsPosition.Age;
                            if (age != ushort.MaxValue)
                                pn.age = age * 0.01;

                            uint imuHead = envelope.GpsPosition.ImuHeading;
                            if (imuHead != ushort.MaxValue)
                            {
                                ahrs.imuHeading = imuHead;
                                ahrs.imuHeading *= 0.1;
                            }

                            int imuRol = envelope.GpsPosition.ImuRoll;
                            if (imuRol != short.MaxValue)
                            {
                                double rollK = imuRol;
                                if (ahrs.isRollInvert) rollK *= -0.1;
                                else rollK *= 0.1;
                                rollK -= ahrs.rollZero;
                                ahrs.imuRoll = ahrs.imuRoll * ahrs.rollFilter + rollK * (1 - ahrs.rollFilter);
                            }

                            int imuPich = envelope.GpsPosition.ImuPitch;
                            if (imuPich != short.MaxValue)
                            {
                                ahrs.imuPitch = imuPich;
                            }

                            int imuYaw = envelope.GpsPosition.ImuYawRate;
                            if (imuYaw != short.MaxValue)
                            {
                                ahrs.imuYawRate = imuYaw;
                            }

                            sentenceCounter = 0;

                            UpdateFixPosition();
                        }
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.ExternalImu: // PGN 0xD3 external IMU
                    {
                        if (ahrs.imuRoll > 25 || ahrs.imuRoll < -25) ahrs.imuRoll = 0;
                        //Heading
                        ahrs.imuHeading = envelope.ExternalImu.Heading;
                        ahrs.imuHeading *= 0.1;

                        //Roll
                        double rollK = envelope.ExternalImu.Roll;

                        if (ahrs.isRollInvert) rollK *= -0.1;
                        else rollK *= 0.1;
                        rollK -= ahrs.rollZero;
                        ahrs.imuRoll = ahrs.imuRoll * ahrs.rollFilter + rollK * (1 - ahrs.rollFilter);

                        //Angular velocity
                        ahrs.angVel = (short)envelope.ExternalImu.AngularVelocity;
                        ahrs.angVel /= -2;
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.ImuDisconnect: // PGN 0xD4 imu disconnect pgn
                    {
                        if (envelope.ImuDisconnect.IsDisconnected)
                        {
                            ahrs.imuHeading = 99999;

                            ahrs.imuRoll = 88888;

                            ahrs.angVel = 0;
                        }
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.SteerModuleResponse: // PGN 0xFD return from autosteer module
                    {
                        //Steer angle actual
                        mc.actualSteerAngleChart = envelope.SteerModuleResponse.ActualSteerAngle;
                        mc.actualSteerAngleDegrees = (double)mc.actualSteerAngleChart * 0.01;

                        //Heading — HeadingValid == false reproduces the legacy 9999 "N/A" sentinel (producer sets HeadingValid = raw != 9999)
                        if (envelope.SteerModuleResponse.HeadingValid)
                        {
                            ahrs.imuHeading = envelope.SteerModuleResponse.Heading * 0.1;
                        }

                        //Roll — RollValid == false reproduces the legacy 8888 "N/A" sentinel (producer sets RollValid = raw != 8888)
                        double rollK = envelope.SteerModuleResponse.Roll;
                        if (envelope.SteerModuleResponse.RollValid)
                        {
                            if (ahrs.isRollInvert) rollK *= -0.1;
                            else rollK *= 0.1;
                            rollK -= ahrs.rollZero;
                            ahrs.imuRoll = ahrs.imuRoll * ahrs.rollFilter + rollK * (1 - ahrs.rollFilter);
                        }
                        //else ahrs.imuRoll = 88888;

                        //switch status
                        mc.workSwitchHigh = (envelope.SteerModuleResponse.SwitchStatus & 1) == 1;
                        mc.steerSwitchHigh = (envelope.SteerModuleResponse.SwitchStatus & 2) == 2;

                        //the pink steer dot reset
                        steerModuleConnectedCounter = 0;

                        //Actual PWM
                        mc.pwmDisplay = (int)envelope.SteerModuleResponse.Pwm;
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.IsoBusHeartbeat: // PGN 0xF0 ISOBUS heartbeat
                    {
                        // IPC-REFACTOR: reconstruct the exact byte[] layout CISOBUS.DeserializeHeartbeat consumes
                        // ([0]=status, [1]=numSections, [2..]=section-state bitmask) from the typed message. CISOBUS is
                        // immutable/out-of-scope; its 1-second heartbeat liveness semantics are unchanged.
                        byte[] sectionStates = envelope.IsoBusHeartbeat.SectionStates.ToByteArray();
                        byte[] pgnData = new byte[2 + sectionStates.Length];
                        pgnData[0] = (byte)envelope.IsoBusHeartbeat.Status;
                        pgnData[1] = (byte)envelope.IsoBusHeartbeat.NumSections;
                        if (sectionStates.Length > 0)
                            Array.Copy(sectionStates, 0, pgnData, 2, sectionStates.Length);
                        isobus.DeserializeHeartbeat(pgnData);
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.SensorData: // PGN 0xFA
                    {
                        mc.sensorData = (byte)envelope.SensorData.SensorData;
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.DisplayHardware: // PGN 0xDD
                    {
                        //{ 0x80, 0x81, 0x7f, 221, number bytes, seconds to display, mystery byte, 98,99,100,101, CRC };
                        if (isHardwareMessages)
                        {
                            lblHardwareMessage.Text = envelope.DisplayHardware.Message;
                            lblHardwareMessage.Visible = true;
                            hardwareLineCounter = (int)envelope.DisplayHardware.DisplayTime * 10;

                            Log.EventWriter(lblHardwareMessage.Text);

                            //color based on byte 6
                            lblHardwareMessage.BackColor = envelope.DisplayHardware.Color == 0 ? Color.Salmon : Color.Bisque;
                            lblHardwareMessage.ForeColor = Color.Black;
                        }
                        else
                        {
                            lblHardwareMessage.Visible = false;
                            hardwareLineCounter = 0;
                        }
                    }
                    break;

                case PgnEnvelope.PayloadOneofCase.RemoteCommand: // PGN 0xDE
                    {
                        //{ 0x80, 0x81, 0x7f, 222, number bytes, mask, command CRC };
                        if (((envelope.RemoteCommand.Mask & 1) == 1)) //mask bit #0 set and command bit #0 nudge line to the 0 = left 1 = right
                        {
                            double dist = Properties.ToolSettings.Default.setAS_snapDistance * 0.01;
                            if ((envelope.RemoteCommand.Command & 1) != 1) { trk.NudgeTrack(-dist); }
                            if ((envelope.RemoteCommand.Command & 1) == 1) { trk.NudgeTrack(dist); }
                        }
                        if (((envelope.RemoteCommand.Mask & 2) == 2)) //mask bit #1 set and command bit #0 cycle line to the 0 = left 1 = right
                        {
                            if ((envelope.RemoteCommand.Command & 1) != 1) { btnCycleLines.PerformClick(); }
                            if ((envelope.RemoteCommand.Command & 1) == 1) { btnCycleLinesBk.PerformClick(); }
                        }
                    }
                    break;

                #region Remote Switches
                case PgnEnvelope.PayloadOneofCase.RemoteSwitch: // PGN 0xEA MTZ8302 Feb 2020
                    {
                        // IPC-REFACTOR: copy the 8 section-switch bytes into mc.ss at offset 1 (was Buffer.BlockCopy(data,5,mc.ss,1,8)).
                        envelope.RemoteSwitch.SwitchData.CopyTo(mc.ss, 1);

                        DoRemoteSwitches();
                    }
                    break;
                #endregion

                default: // PayloadOneofCase.None or an outbound/unknown payload — no-op (never throw)
                    break;
            }
        }

        //start the gRPC IPC client (formerly the UDP loopback server)
        public void StartLoopbackServer()
        {
            // IPC-REFACTOR: UDP loopback bind(127.0.0.1:15555)+BeginReceiveFrom replaced by gRPC channel + TelemetryService server-stream subscription.
            try
            {
                // IPC-REFACTOR: loopBackSocket intentionally left null in gRPC mode. FormGPS.cs null-checks it
                // before Shutdown()/Close(), so null is a safe no-op there. Assigned via a STATEMENT (not a field
                // initializer) so the field is definitely-assigned (no CS0649) without tripping CA1805.
                loopBackSocket = null;

                _ipcCts = new CancellationTokenSource();

                // IPC-REFACTOR: connect + subscribe off the UI thread (StartLoopbackServer runs on the UI thread and
                // the retry backoff can sum to ~15.5 s). ConnectAndSubscribeAsync owns its own try/catch, so this
                // discard fire-and-forget never leaves an unobserved task exception.
                _ = ConnectAndSubscribeAsync();

                Log.EventWriter("gRPC IPC client starting: " + IpcConstants.AgIoSocketPath);
            }
            catch (Exception ex)
            {
                FormDialog.Show(
                    "UDP Server",
                    "Load Error: " + ex.Message,
                    DialogSeverity.Error);
                Log.EventWriter("Catch -> Load gRPC IPC Error: " + ex.ToString());
            }
        }

        // IPC-REFACTOR: connect + subscribe delegated to the shared AgOpenGPS.Ipc.IpcTelemetrySubscriber so the
        // net48-incompatible SocketsHttpHandler channel build (now guarded in IpcChannelFactory) is no longer
        // referenced here, and the canonical 5-attempt / 500-1000-2000-4000-8000 ms (total 15500 ms) retry-backoff
        // schedule is identical across every IPC client. The local CreateIpcChannel/IsTransientConnect helpers and the
        // hand-rolled retry loop are removed in favour of the shared helper. Returns the subscriber Task directly; the
        // fire-and-forget discard in StartLoopbackServer still observes no unhandled exception because RunAsync owns
        // its own try/catch on every path.
        private Task ConnectAndSubscribeAsync()
        {
            return IpcTelemetrySubscriber.RunAsync(
                onChannel: channel =>
                {
                    _ipcChannel = channel;
                    _commandClient = new CommandService.CommandServiceClient(channel);
                },
                onConnected: () => Log.EventWriter("gRPC telemetry stream connected: " + IpcConstants.AgIoSocketPath),
                onEnvelope: envelope =>
                {
                    // IPC-REFACTOR: marshal each envelope to the UI thread exactly as legacy ReceiveAppData did
                    // (only the SOURCE changed: UDP datagram -> telemetry stream item).
                    try { BeginInvoke((MethodInvoker)(() => ReceiveFromAgIO(envelope))); }
                    catch { /* handle not yet created / form disposing; ignore - parity with legacy empty catch */ }
                },
                onError: ex =>
                {
                    // IPC-REFACTOR: UDP bind-failure dialog replaced by gRPC connect retry/backoff (5x, 500ms base x2)
                    // -> existing FormDialog + Log.EventWriter on exhaustion, marshalled to the UI thread.
                    try
                    {
                        BeginInvoke((MethodInvoker)(() => FormDialog.Show(
                            "UDP Server",
                            "gRPC IPC connect failed (5 attempts) to " + IpcConstants.AgIoSocketPath,
                            DialogSeverity.Error)));
                    }
                    catch { /* form may be closing; ignore */ }
                    Log.EventWriter("Catch -> gRPC IPC connect exhausted after 5 attempts to "
                        + IpcConstants.AgIoSocketPath + ": " + ex.Message);
                },
                token: _ipcCts.Token);
        }

        // IPC-REFACTOR: gRPC-mode teardown counterpart to StartLoopbackServer. FormGPS.FinishShutdown previously only
        // closed the legacy loopBackSocket, which is intentionally null in gRPC mode, so the TelemetryService stream
        // subscription (the IpcTelemetrySubscriber background reader), its GrpcChannel, and the CancellationTokenSource
        // were never cancelled or disposed on form close — leaving the subscription/channel running during shutdown.
        // Cancel first (this signals the shared reader loop to stop), then dispose the channel and the CTS. The method
        // is fully null-guarded and safe to call more than once and when the IPC client was never started.
        public void StopLoopbackServer()
        {
            // Signal the background subscriber (started with _ipcCts.Token) to stop. Guard against a CTS that has
            // already been disposed by a prior StopLoopbackServer call.
            try
            {
                _ipcCts?.Cancel();
            }
            catch (ObjectDisposedException) { /* already disposed by a previous shutdown; nothing to cancel */ }

            // Capture-then-null-then-dispose so a concurrent onChannel assignment or a repeat call cannot double-dispose.
            CancellationTokenSource cts = _ipcCts;
            _ipcCts = null;
            cts?.Dispose();

            GrpcChannel channel = _ipcChannel;
            _ipcChannel = null;
            _commandClient = null;
            channel?.Dispose();
        }

        private void DisableSim()
        {
            isFirstFixPositionSet = false;
            isGPSPositionInitialized = false;
            isFirstHeadingSet = false;
            startCounter = 0;
            panelSim.Visible = false;
            timerSim.Enabled = false;
            simulatorOnToolStripMenuItem.Checked = false;
            Properties.Settings.Default.setMenu_isSimulatorOn = simulatorOnToolStripMenuItem.Checked;
            Properties.Settings.Default.Save();
            return;
        }

        // IPC-REFACTOR: UDP async receive callback (EndReceiveFrom + BeginReceiveFrom re-arm + BeginInvoke -> ReceiveFromAgIO)
        // removed; inbound frames now arrive on the TelemetryService server-stream reader (ConnectAndSubscribeAsync MoveNext loop),
        // which marshals each PgnEnvelope to the UI thread via the same BeginInvoke((MethodInvoker)(() => ReceiveFromAgIO(...))) pattern.

        // IPC-REFACTOR: fire-and-forget UDP send + CRC replaced by typed CommandService.Send* unary RPC (byteData[3] dispatch); ack logged, non-blocking.
        public void SendPgnToLoop(byte[] byteData)
        {
            // IPC-REFACTOR: guard fixed for gRPC mode. The legacy guard `loopBackSocket != null` can never pass now
            // (loopBackSocket stays null in gRPC mode), which would silently drop EVERY outbound PGN. Require a live
            // command client and at least 4 bytes so the byteData[3] PGN dispatch key can be read.
            if (_commandClient == null || byteData == null || byteData.Length <= 3) return;

            // IPC-REFACTOR: CRC check removed — HTTP/2 frame integrity provides equivalent byte-level guarantees.
            // Each case builds the typed message from the legacy byte[] via OutboundPgn.Build* (PGN.Designer.cs) and
            // issues the matching unary CommandService RPC. `_ = SendCommandAckAsync(...)` is an intentional fire-and-forget
            // discard that preserves the legacy non-blocking UDP send; the whole switch is wrapped so it never throws to
            // callers (parity with the legacy empty catch — includes the immutable CISOBUS.cs call sites).
            try
            {
                switch (byteData[3])
                {
                    case 0xFE: // PGN 254 AutoSteer data
                        _ = SendCommandAckAsync(_commandClient.SendAutoSteerDataAsync(OutboundPgn.BuildAutoSteerData(byteData)));
                        break;
                    case 0xFC: // PGN 252 AutoSteer settings
                        _ = SendCommandAckAsync(_commandClient.SendAutoSteerSettingsAsync(OutboundPgn.BuildAutoSteerSettings(byteData)));
                        break;
                    case 0xFB: // PGN 251 AutoSteer config
                        _ = SendCommandAckAsync(_commandClient.SendAutoSteerConfigAsync(OutboundPgn.BuildAutoSteerConfig(byteData)));
                        break;
                    case 0xEF: // PGN 239 Machine data
                        _ = SendCommandAckAsync(_commandClient.SendMachineDataAsync(OutboundPgn.BuildMachineData(byteData)));
                        break;
                    case 0xEE: // PGN 238 Machine config
                        _ = SendCommandAckAsync(_commandClient.SendMachineConfigAsync(OutboundPgn.BuildMachineConfig(byteData)));
                        break;
                    case 0xEC: // PGN 236 Relay config
                        _ = SendCommandAckAsync(_commandClient.SendRelayConfigAsync(OutboundPgn.BuildRelayConfig(byteData)));
                        break;
                    case 0xEB: // PGN 235 Section dimensions
                        _ = SendCommandAckAsync(_commandClient.SendSectionDimensionsAsync(OutboundPgn.BuildSectionDimensions(byteData)));
                        break;
                    case 0xE5: // PGN 229 Extended section control
                        _ = SendCommandAckAsync(_commandClient.SendExtendedSectionControlAsync(OutboundPgn.BuildExtendedSectionControl(byteData)));
                        break;
                    case 0xE4: // PGN 228 Rate control
                        _ = SendCommandAckAsync(_commandClient.SendRateControlAsync(OutboundPgn.BuildRateControl(byteData)));
                        break;
                    case 0xF1: // PGN 241 Section control enable
                        _ = SendCommandAckAsync(_commandClient.SendSectionControlEnableAsync(OutboundPgn.BuildSectionControlEnable(byteData)));
                        break;
                    case 0xF2: // PGN 242 Process data
                        _ = SendCommandAckAsync(_commandClient.SendProcessDataAsync(OutboundPgn.BuildProcessData(byteData)));
                        break;
                    case 0xF3: // PGN 243 Field name
                        _ = SendCommandAckAsync(_commandClient.SendFieldNameAsync(OutboundPgn.BuildFieldName(byteData)));
                        break;
                    case 0xD0: // PGN 208 Lat/Lon
                        _ = SendCommandAckAsync(_commandClient.SendLatLonAsync(OutboundPgn.BuildLatLon(byteData)));
                        break;
                    case 0x64: // PGN 100 Corrected position
                        _ = SendCommandAckAsync(_commandClient.SendCorrectedPositionAsync(OutboundPgn.BuildCorrectedPosition(byteData)));
                        break;
                    default:
                        // Unknown/unsupported outbound PGN — no-op (only known PGNs are emitted by callers).
                        break;
                }
            }
            catch (Exception ex)
            {
                // IPC-REFACTOR: swallow like the legacy empty catch — never throw to callers (incl. immutable CISOBUS.cs).
                Log.EventWriter("SendPgnToLoop dispatch error: " + ex.Message);
            }
        }

        // IPC-REFACTOR: awaits the unary CommandService ack off the caller's thread and logs a NAK/failure without ever
        // throwing (backs the fire-and-forget discard at each SendPgnToLoop call site, preserving the legacy non-blocking
        // UDP send). Disposes the call. Static because it only touches Log.EventWriter (no instance state) — satisfies CA1822.
        private static async Task SendCommandAckAsync(AsyncUnaryCall<CommandAck> call)
        {
            try
            {
                using (call)
                {
                    CommandAck ack = await call.ResponseAsync.ConfigureAwait(false);
                    if (ack != null && !ack.Received)
                        Log.EventWriter("CommandService NAK: " + ack.ErrorMessage);
                }
            }
            catch (RpcException ex)
            {
                Log.EventWriter("CommandService send failed: " + ex.Status.Detail);
            }
            catch (Exception ex)
            {
                Log.EventWriter("CommandService send error: " + ex.Message);
            }
        }

        // IPC-REFACTOR: UDP BeginSendTo completion callback (SendAsyncLoopData -> EndSend) removed. It had zero external
        // referencers; the outbound path is now the unary CommandService RPC whose ack is awaited by SendCommandAckAsync.

        //for moving and sizing borderless window
        protected override void WndProc(ref Message m)
        {
            const int RESIZE_HANDLE_SIZE = 7;

            switch (m.Msg)
            {
                case 0x0084/*NCHITTEST*/ :
                    base.WndProc(ref m);
                    if (!isKioskMode)
                    {
                        if ((int)m.Result == 0x01/*HTCLIENT*/)
                        {
                            Point screenPoint = new Point(m.LParam.ToInt32());
                            Point clientPoint = this.PointToClient(screenPoint);
                            if (clientPoint.Y <= RESIZE_HANDLE_SIZE)
                            {
                                if (clientPoint.X <= RESIZE_HANDLE_SIZE)
                                    m.Result = (IntPtr)13/*HTTOPLEFT*/ ;
                                else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
                                    m.Result = (IntPtr)12/*HTTOP*/ ;
                                else
                                    m.Result = (IntPtr)14/*HTTOPRIGHT*/ ;
                            }
                            else if (clientPoint.Y <= (Size.Height - RESIZE_HANDLE_SIZE))
                            {
                                if (clientPoint.X <= RESIZE_HANDLE_SIZE)
                                    m.Result = (IntPtr)10/*HTLEFT*/ ;
                                else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
                                    m.Result = (IntPtr)2/*HTCAPTION*/ ;
                                else
                                    m.Result = (IntPtr)11/*HTRIGHT*/ ;
                            }
                            else
                            {
                                if (clientPoint.X <= RESIZE_HANDLE_SIZE)
                                    m.Result = (IntPtr)16/*HTBOTTOMLEFT*/ ;
                                else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
                                    m.Result = (IntPtr)15/*HTBOTTOM*/ ;
                                else
                                    m.Result = (IntPtr)17/*HTBOTTOMRIGHT*/ ;
                            }
                        }
                    }
                    return;
            }
            base.WndProc(ref m);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                //drop shadow
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000; // <--- use 0x20000
                return cp;
            }
        }

        #region keystrokes

        private HotkeyMessageFilter _hotkeyFilter;
        private bool _uiReady = false; // becomes true once FormGPS UI is fully shown/ready

        /// <summary>
        /// Called by the app-wide message filter to forward keystrokes to the existing mapping logic.
        /// We strip modifiers to keep your (char)keyData comparisons working, and ignore keys until UI is ready.
        /// </summary>
        public bool HandleAppWideKey(Keys key, Keys mods)
        {
            if (!_uiReady) return false; // ignore while Terms&Conditions or before FormGPS is ready

            // Use only the key code (drop modifiers) so your mappings still match
            var keyData = (key & Keys.KeyCode);

            // ProcessCmdKey reads only keyData; the Message payload is irrelevant here
            var msg = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            return ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Register the message filter once when the handle exists. Keep it disabled until the UI is ready.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_hotkeyFilter == null)
            {
                _hotkeyFilter = new HotkeyMessageFilter(this)
                {
                    // Keep the filter disabled until FormGPS is fully shown,
                    // to avoid calling ProcessCmdKey before controls are initialized.
                    Enabled = false
                };
                Application.AddMessageFilter(_hotkeyFilter);
            }
        }

        /// <summary>
        /// Mark UI as ready and enable the filter once the form is shown (after Load/InitializeComponent).
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _uiReady = true;
            if (_hotkeyFilter != null) _hotkeyFilter.Enabled = true;
        }

        /// <summary>
        /// Clean up the message filter when closing the form.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hotkeyFilter != null)
            {
                Application.RemoveMessageFilter(_hotkeyFilter);
                _hotkeyFilter.Dispose();
                _hotkeyFilter = null;
            }
            base.OnFormClosed(e);
        }

        /// <summary>
        /// Existing key mapping logic. Now guarded so it only runs when UI is ready and hotkeys exist.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Guard clauses: do not handle until initialized / prevent NREs during early dialogs
            if (!_uiReady || hotkeys == null || hotkeys.Length < 19)
                return base.ProcessCmdKey(ref msg, keyData);

            if ((char)keyData == hotkeys[0]) // autosteer button on/off
            {
                btnAutoSteer.PerformClick();
                if (!isBtnAutoSteerOn) TimedMessageBox(2000, gStr.gsGuidanceStopped, "Hotkey Triggered");
                return true;
            }

            if ((char)keyData == hotkeys[1]) // cycle lines
            {
                btnCycleLines.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[2]) // save & close field
            {
                _ = FileSaveEverythingBeforeClosingField();
                return true;
            }

            if ((char)keyData == hotkeys[3]) // new flag
            {
                btnFlag.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[4]) // section master manual
            {
                btnSectionMasterManual.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[5]) // section master auto
            {
                btnSectionMasterAuto.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[6]) // snap to pivot
            {
                trk.SnapToPivot();
                return true;
            }

            if ((char)keyData == hotkeys[7]) // nudge track left
            {
                if (trk.idx > -1)
                    trk.NudgeTrack((double)Properties.ToolSettings.Default.setAS_snapDistance * -0.01);
                return true;
            }

            if ((char)keyData == hotkeys[8]) // nudge track right
            {
                if (trk.idx > -1)
                    trk.NudgeTrack((double)Properties.ToolSettings.Default.setAS_snapDistance * 0.01);
                return true;
            }

            if ((char)keyData == hotkeys[9]) // vehicle settings
            {
                toolStripConfig.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[10]) // steer wizard
            {
                Form fcs = Application.OpenForms["FormSteer"];
                if (fcs != null) { fcs.Focus(); fcs.Close(); }

                Form fc = Application.OpenForms["FormSteerWiz"];
                if (fc != null) { fc.Focus(); return true; }

                Form form = new FormSteerWiz(this);
                form.Show(this);
            }

            if ((char)keyData == hotkeys[11]) // section/zone 1
            {
                if (tool.isSectionsNotZones) btnSection1Man.PerformClick();
                else btnZone1.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[12]) // section/zone 2
            {
                if (tool.isSectionsNotZones) btnSection2Man.PerformClick();
                else btnZone2.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[13]) // section/zone 3
            {
                if (tool.isSectionsNotZones) btnSection3Man.PerformClick();
                else btnZone3.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[14]) // section/zone 4
            {
                if (tool.isSectionsNotZones) btnSection4Man.PerformClick();
                else btnZone4.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[15]) // section/zone 5
            {
                if (tool.isSectionsNotZones) btnSection5Man.PerformClick();
                else btnZone5.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[16]) // section/zone 6
            {
                if (tool.isSectionsNotZones) btnSection6Man.PerformClick();
                else btnZone6.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[17]) // section/zone 7
            {
                if (tool.isSectionsNotZones) btnSection7Man.PerformClick();
                else btnZone7.PerformClick();
                return true;
            }

            if ((char)keyData == hotkeys[18]) // section/zone 8
            {
                if (tool.isSectionsNotZones) btnSection8Man.PerformClick();
                else btnZone8.PerformClick();
                return true;
            }

            //////////////////////////////////////////////

            if (keyData == Keys.NumPad1) // section master auto
            {
                btnSectionMasterAuto.PerformClick();
                return true;
            }

            if (keyData == Keys.NumPad0) // section master manual
            {
                btnSectionMasterManual.PerformClick();
                return true;
            }

            if (keyData == Keys.F11) // fullscreen
            {
                btnMaximizeMainForm.PerformClick();
                return true;
            }

            // reset sim
            if (keyData == Keys.R)
            {
                btnResetSim.PerformClick();
                return true;
            }

            // U-Turn
            if (keyData == Keys.U)
            {
                sim.headingTrue += Math.PI;
                ABLine.isABValid = false;
                curve.isCurveValid = false;
                if (isBtnAutoSteerOn) btnAutoYouTurn.PerformClick();
            }

            // speed up
            if (keyData == Keys.Up)
            {
                if (sim.stepDistance < 0.4 && sim.stepDistance > -0.36) sim.stepDistance += 0.01;
                else sim.stepDistance += 0.04;
                if (sim.stepDistance > 4) sim.stepDistance = 4;
                return true;
            }

            // slow down
            if (keyData == Keys.Down)
            {
                if (sim.stepDistance < 0.2 && sim.stepDistance > -0.04) sim.stepDistance -= 0.01;
                else sim.stepDistance -= 0.04;
                if (sim.stepDistance < -0.35) sim.stepDistance = -0.35;
                return true;
            }

            // stop
            if (keyData == Keys.OemPeriod)
            {
                sim.stepDistance = 0;
                return true;
            }

            // turn right
            if (keyData == Keys.Right)
            {
                sim.steerAngle += 1.0;
                if (sim.steerAngle > 40) sim.steerAngle = 40;
                if (sim.steerAngle < -40) sim.steerAngle = -40;
                sim.steerAngleScrollBar = sim.steerAngle;
                btnResetSteerAngle.Text = sim.steerAngle.ToString();
                hsbarSteerAngle.Value = (int)(10 * sim.steerAngle) + 400;
                return true;
            }

            // turn left
            if (keyData == Keys.Left)
            {
                sim.steerAngle -= 1.0;
                if (sim.steerAngle > 40) sim.steerAngle = 40;
                if (sim.steerAngle < -40) sim.steerAngle = -40;
                sim.steerAngleScrollBar = sim.steerAngle;
                btnResetSteerAngle.Text = sim.steerAngle.ToString();
                hsbarSteerAngle.Value = (int)(10 * sim.steerAngle) + 400;
                return true;
            }

            // zero steering
            if (keyData == Keys.OemQuestion)
            {
                sim.steerAngle = 0.0;
                sim.steerAngleScrollBar = sim.steerAngle;
                btnResetSteerAngle.Text = sim.steerAngle.ToString();
                hsbarSteerAngle.Value = (int)(10 * sim.steerAngle) + 400;
                return true;
            }

            if (keyData == Keys.OemOpenBrackets)
            {
                sim.stepDistance = 0;
                sim.isAccelBack = true;
            }

            if (keyData == Keys.OemCloseBrackets)
            {
                sim.stepDistance = 0;
                sim.isAccelForward = true;
            }

            if (keyData == Keys.OemQuotes)
            {
                sim.stepDistance = 0;
                return true;
            }

            if (keyData == Keys.F6) // toggle fast/normal sim
            {
                if (timerSim.Enabled)
                {
                    if (timerSim.Interval < 20) timerSim.Interval = 93;
                    else timerSim.Interval = 15;
                }
                return true;
            }

            // Fallback: let base handle anything else
            return base.ProcessCmdKey(ref msg, keyData);
        }
        #endregion


        #region Gesture

        // Private variables used to maintain the state of gestures
        ////private DrawingObject _dwo = new DrawingObject();
        //private Point _ptFirst = new Point();

        //private Point _ptSecond = new Point();
        //private int _iArguments = 0;

        //// One of the fields in GESTUREINFO structure is type of Int64 (8 bytes).
        //// The relevant gesture information is stored in lower 4 bytes. This
        //// bit mask is used to get 4 lower bytes from this argument.
        //private const Int64 ULL_ARGUMENTS_BIT_MASK = 0x00000000FFFFFFFF;

        ////-----------------------------------------------------------------------
        //// Multitouch/Touch glue (from winuser.h file)
        //// Since the managed layer between C# and WinAPI functions does not
        //// exist at the moment for multi-touch related functions this part of
        //// code is required to replicate definitions from winuser.h file.
        ////-----------------------------------------------------------------------
        //// Touch event window message constants [winuser.h]
        //private const int WM_GESTURENOTIFY = 0x011A;

        //private const int WM_GESTURE = 0x0119;

        //private const int GC_ALLGESTURES = 0x00000001;

        //// Gesture IDs
        //private const int GID_BEGIN = 1;

        //private const int GID_END = 2;
        //private const int GID_ZOOM = 3;
        //private const int GID_PAN = 4;
        //private const int GID_ROTATE = 5;
        //private const int GID_TWOFINGERTAP = 6;


        //private const int GID_PRESSANDTAP = 7;

        //// Gesture flags - GESTUREINFO.dwFlags
        //private const int GF_BEGIN = 0x00000001;

        //private const int GF_INERTIA = 0x00000002;
        //private const int GF_END = 0x00000004;

        ////
        //// Gesture configuration structure
        ////   - Used in SetGestureConfig and GetGestureConfig
        ////   - Note that any setting not included in either GESTURECONFIG.dwWant
        ////     or GESTURECONFIG.dwBlock will use the parent window's preferences
        ////     or system defaults.
        ////
        //// Touch API defined structures [winuser.h]
        //[StructLayout(LayoutKind.Sequential)]
        //private struct GESTURECONFIG
        //{
        //    public int dwID;    // gesture ID
        //    public int dwWant;  // settings related to gesture ID that are to be

        //    // turned on
        //    public int dwBlock; // settings related to gesture ID that are to be

        //    // turned off
        //}

        //[StructLayout(LayoutKind.Sequential)]
        //private struct POINTS
        //{
        //    public short x;
        //    public short y;
        //}

        ////
        //// Gesture information structure
        ////   - Pass the HGESTUREINFO received in the WM_GESTURE message lParam
        ////     into the GetGestureInfo function to retrieve this information.
        ////   - If cbExtraArgs is non-zero, pass the HGESTUREINFO received in
        ////     the WM_GESTURE message lParam into the GetGestureExtraArgs
        ////     function to retrieve extended argument information.
        ////
        //[StructLayout(LayoutKind.Sequential)]
        //private struct GESTUREINFO
        //{
        //    public int cbSize;           // size, in bytes, of this structure

        //    // (including variable length Args
        //    // field)
        //    public int dwFlags;          // see GF_* flags

        //    public int dwID;             // gesture ID, see GID_* defines
        //    public IntPtr hwndTarget;    // handle to window targeted by this

        //    // gesture
        //    [MarshalAs(UnmanagedType.Struct)]
        //    internal POINTS ptsLocation; // current location of this gesture

        //    public int dwInstanceID;     // internally used
        //    public int dwSequenceID;     // internally used
        //    public Int64 ullArguments;   // arguments for gestures whose

        //    // arguments fit in 8 BYTES
        //    public int cbExtraArgs;      // size, in bytes, of extra arguments,

        //    // if any, that accompany this gesture
        //}

        //// Currently touch/multitouch access is done through unmanaged code
        //// We must p/invoke into user32 [winuser.h]
        //[DllImport("user32")]
        //[return: MarshalAs(UnmanagedType.Bool)]
        //private static extern bool SetGestureConfig(IntPtr hWnd, int dwReserved, int cIDs, ref GESTURECONFIG pGestureConfig, int cbSize);

        //[DllImport("user32")]
        //[return: MarshalAs(UnmanagedType.Bool)]
        //private static extern bool GetGestureInfo(IntPtr hGestureInfo, ref GESTUREINFO pGestureInfo);

        //// size of GESTURECONFIG structure
        //private int _gestureConfigSize;

        //// size of GESTUREINFO structure
        //private int _gestureInfoSize;

        //[SecurityPermission(SecurityAction.Demand)]
        //private void SetupStructSizes()
        //{
        //    // Both GetGestureCommandInfo and GetTouchInputInfo need to be
        //    // passed the size of the structure they will be filling
        //    // we get the sizes upfront so they can be used later.
        //    _gestureConfigSize = Marshal.SizeOf(new GESTURECONFIG());
        //    _gestureInfoSize = Marshal.SizeOf(new GESTUREINFO());
        //}

        ////-------------------------------------------------------------
        //// Since there is no managed layer at the moment that supports
        //// event handlers for WM_GESTURENOTIFY and WM_GESTURE
        //// messages we have to override WndProc function
        ////
        //// in
        ////   m - Message object
        ////-------------------------------------------------------------

        //// Drag form without border definitions
        //private const int WM_NCHITTEST = 0x84;
        ////private const int HT_CAPTION = 0x2;

        //[PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        //protected override void WndProc(ref Message m)
        //{
        //    bool handled = false;
        //    const int RESIZE_HANDLE_SIZE = 10;

        //    switch (m.Msg)
        //    {
        //        //case WM_GESTURENOTIFY:
        //        //    {
        //        //        // This is the right place to define the list of gestures
        //        //        // that this application will support. By populating
        //        //        // GESTURECONFIG structure and calling SetGestureConfig
        //        //        // function. We can choose gestures that we want to
        //        //        // handle in our application. In this app we decide to
        //        //        // handle all gestures.
        //        //        GESTURECONFIG gc = new GESTURECONFIG
        //        //        {
        //        //            dwID = 0,                // gesture ID
        //        //            dwWant = GC_ALLGESTURES, // settings related to gesture
        //        //                                     // ID that are to be turned on
        //        //            dwBlock = 0 // settings related to gesture ID that are
        //        //        };
        //        //        // to be

        //        //        // We must p/invoke into user32 [winuser.h]
        //        //        bool bResult = SetGestureConfig(
        //        //            Handle, // window for which configuration is specified
        //        //            0,      // reserved, must be 0
        //        //            1,      // countExit of GESTURECONFIG structures
        //        //            ref gc, // array of GESTURECONFIG structures, dwIDs
        //        //                    // will be processed in the order specified
        //        //                    // and repeated occurances will overwrite
        //        //                    // previous ones
        //        //            _gestureConfigSize // sizeof(GESTURECONFIG)
        //        //        );

        //        //        if (!bResult)
        //        //        {
        //        //            throw new Exception("Error in execution of SetGestureConfig");
        //        //        }
        //        //    }
        //        //    handled = true;
        //        //    break;

        //        //case WM_GESTURE:
        //        //    // The gesture processing code is implemented in
        //        //    // the DecodeGesture method
        //        //    handled = DecodeGesture(ref m);
        //        //    break;

        //        case WM_NCHITTEST:

        //            base.WndProc(ref m);

        //            if ((int)m.Result == 0x01/*HTCLIENT*/)
        //            {
        //                Point screenPoint = new Point(m.LParam.ToInt32());
        //                Point clientPoint = this.PointToClient(screenPoint);
        //                if (clientPoint.Y <= RESIZE_HANDLE_SIZE)
        //                {
        //                    if (clientPoint.X <= RESIZE_HANDLE_SIZE)
        //                        m.Result = (IntPtr)13/*HTTOPLEFT*/ ;
        //                    else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
        //                        m.Result = (IntPtr)12/*HTTOP*/ ;
        //                    else
        //                        m.Result = (IntPtr)14/*HTTOPRIGHT*/ ;
        //                }
        //                else if (clientPoint.Y <= (Size.Height - RESIZE_HANDLE_SIZE))
        //                {
        //                    if (clientPoint.X <= RESIZE_HANDLE_SIZE)
        //                        m.Result = (IntPtr)10/*HTLEFT*/ ;
        //                    else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
        //                        m.Result = (IntPtr)2/*HTCAPTION*/ ;
        //                    else
        //                        m.Result = (IntPtr)11/*HTRIGHT*/ ;
        //                }
        //                else
        //                {
        //                    if (clientPoint.X <= RESIZE_HANDLE_SIZE)
        //                        m.Result = (IntPtr)16/*HTBOTTOMLEFT*/ ;
        //                    else if (clientPoint.X < (Size.Width - RESIZE_HANDLE_SIZE))
        //                        m.Result = (IntPtr)15/*HTBOTTOM*/ ;
        //                    else
        //                        m.Result = (IntPtr)17/*HTBOTTOMRIGHT*/ ;
        //                }
        //                return;

        //            }

        //            handled = false;
        //            //base.WndProc(ref m);

        //            // For window move
        //            //m.Result = (IntPtr)(HT_CAPTION);

        //            //return;

        //            break;

        //        default:
        //            handled = false;
        //            break;
        //    }

        //    //// Filter message back up to parents.
        //    //base.WndProc(ref m);

        //    //if (handled)
        //    //{
        //    //    // Acknowledge event if handled.
        //    //    try
        //    //    {
        //    //        m.Result = new System.IntPtr(1);
        //    //    }
        //    //    catch (Exception)
        //    //    {
        //    //    }
        //    //}
        //}

        //// Taken from GCI_ROTATE_ANGLE_FROM_ARGUMENT.
        //// Converts from "binary radians" to traditional radians.
        //static protected double ArgToRadians(Int64 arg)
        //{
        //    return (arg / 65535.0 * 4.0 * 3.14159265) - (2.0 * 3.14159265);
        //}

        //// Handler of gestures
        ////in:
        ////  m - Message object
        //private bool DecodeGesture(ref Message m)
        //{
        //    GESTUREINFO gi;

        //    try
        //    {
        //        gi = new GESTUREINFO();
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    gi.cbSize = _gestureInfoSize;

        //    // Load the gesture information.
        //    // We must p/invoke into user32 [winuser.h]
        //    if (!GetGestureInfo(m.LParam, ref gi))
        //    {
        //        return false;
        //    }

        //    switch (gi.dwID)
        //    {
        //        case GID_BEGIN:
        //        case GID_END:
        //            break;

        //        case GID_ZOOM:
        //            switch (gi.dwFlags)
        //            {
        //                case GF_BEGIN:
        //                    _iArguments = (int)(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK);
        //                    _ptFirst.X = gi.ptsLocation.x;
        //                    _ptFirst.Y = gi.ptsLocation.y;
        //                    _ptFirst = PointToClient(_ptFirst);
        //                    break;

        //                default:
        //                    // We read here the second point of the gesture. This
        //                    // is middle point between fingers in this new
        //                    // position.
        //                    _ptSecond.X = gi.ptsLocation.x;
        //                    _ptSecond.Y = gi.ptsLocation.y;
        //                    _ptSecond = PointToClient(_ptSecond);
        //                    {
        //                        // The zoom factor is the ratio of the new
        //                        // and the old distance. The new distance
        //                        // between two fingers is stored in
        //                        // gi.ullArguments (lower 4 bytes) and the old
        //                        // distance is stored in _iArguments.
        //                        double k = (double)(_iArguments)
        //                                    / (double)(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK);
        //                        //lblX.Text = k.ToString();
        //                        camera.zoomValue *= k;
        //                        if (camera.zoomValue < 6.0) camera.zoomValue = 6;
        //                        camera.camSetDistance = camera.zoomValue * camera.zoomValue * -1;
        //                        SetZoom();
        //                    }

        //                    // Now we have to store new information as a starting
        //                    // information for the next step in this gesture.
        //                    _ptFirst = _ptSecond;
        //                    _iArguments = (int)(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK);
        //                    break;
        //            }
        //            break;

        //        //case GID_PAN:
        //        //    switch (gi.dwFlags)
        //        //    {
        //        //        case GF_BEGIN:
        //        //            _ptFirst.X = gi.ptsLocation.x;
        //        //            _ptFirst.Y = gi.ptsLocation.y;
        //        //            _ptFirst = PointToClient(_ptFirst);
        //        //            break;

        //        //        default:
        //        //            // We read the second point of this gesture. It is a
        //        //            // middle point between fingers in this new position
        //        //            _ptSecond.X = gi.ptsLocation.x;
        //        //            _ptSecond.Y = gi.ptsLocation.y;
        //        //            _ptSecond = PointToClient(_ptSecond);

        //        //            // We apply move operation of the object
        //        //            _dwo.Move(_ptSecond.X - _ptFirst.X, _ptSecond.Y - _ptFirst.Y);

        //        //            Invalidate();

        //        //            // We have to copy second point into first one to
        //        //            // prepare for the next step of this gesture.
        //        //            _ptFirst = _ptSecond;
        //        //            break;
        //        //    }
        //        //    break;

        //        case GID_ROTATE:
        //            switch (gi.dwFlags)
        //            {
        //                case GF_BEGIN:
        //                    _iArguments = 32768;
        //                    break;

        //                default:
        //                    // Gesture handler returns cumulative rotation angle. However we
        //                    // have to pass the delta angle to our function responsible
        //                    // to process the rotation gesture.
        //                    double k = ((int)(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK) - _iArguments) * 0.01;
        //                    camera.camPitch -= k;
        //                    if (camera.camPitch < -74) camera.camPitch = -74;
        //                    if (camera.camPitch > 0) camera.camPitch = 0;
        //                    _iArguments = (int)(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK);
        //                    break;
        //            }
        //            break;

        //            //case GID_TWOFINGERTAP:
        //            //    // Toggle drawing of diagonals
        //            //    _dwo.ToggleDrawDiagonals();
        //            //    Invalidate();
        //            //    break;

        //            //case GID_PRESSANDTAP:
        //            //    if (gi.dwFlags == GF_BEGIN)
        //            //    {
        //            //        // Shift drawing color
        //            //        _dwo.ShiftColor();
        //            //        Invalidate();
        //            //    }
        //            //    break;
        //    }

        //    return true;
        //}

        #endregion Gesture

    }
}
