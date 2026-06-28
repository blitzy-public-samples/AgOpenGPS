// [XPLAT] migrated from net48/WinForms Forms/UDP.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgLibrary.Logging;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Byte counters for the comm hub, folded in unchanged from the WinForms
    /// <c>FormLoop</c> partial (<c>SourceCode/AgIO/Source/Forms/UDP.designer.cs</c> L12–L19).
    /// Behaviour frozen; declared here (not in a separate file) to keep the new surface area minimal
    /// exactly as it was declared alongside <c>FormLoop</c> in the original source (AAP §0.7.1).
    /// </summary>
    public sealed class CTraffic
    {
        public int cntrGPSIn = 0;
        public int cntrGPSInBytes = 0;
        public int cntrGPSOut = 0;

        public uint helloFromMachine = 99, helloFromAutoSteer = 99, helloFromIMU = 99;
    }

    /// <summary>
    /// [XPLAT] Module scan-reply state, folded in unchanged from the WinForms <c>FormLoop</c>
    /// partial (<c>UDP.designer.cs</c> L21–L34). Populated from inbound PGN 203 scan replies; read by
    /// the shell/view-model to display discovered Steer/Machine/IMU/GPS module IP addresses.
    /// Behaviour frozen.
    /// </summary>
    public sealed class CScanReply
    {
        public string steerIP = "";
        public string machineIP = "";
        public string GPS_IP = "";
        public string IMU_IP = "";
        public string subnetStr = "";

        public byte[] subnet = { 0, 0, 0 };

        public bool isNewSteer, isNewMachine, isNewGPS, isNewIMU;

        public bool isNewData = false;
    }

    /// <summary>
    /// [XPLAT] Plain, injectable UDP loopback + module-broadcast transport service, extracted 1:1
    /// (behaviour frozen, byte-for-byte) from the former WinForms <c>FormLoop</c> partial class
    /// (<c>SourceCode/AgIO/Source/Forms/UDP.designer.cs</c>). This is the contract-critical core of the
    /// AgIO comm hub: it bridges AgOpenGPS (over the loopback socket) and the steering/machine/IMU/GPS
    /// modules (over the UDP network and serial), forwarding raw PGN frames in both directions.
    /// </summary>
    /// <remarks>
    /// The byte-for-byte protocol contract is preserved exactly (AAP §0.2.2 / §0.7.1): the loopback
    /// socket binds to <c>127.0.0.1:17777</c>, AgOpenGPS is addressed on port <c>15555</c>, the module
    /// UDP socket binds to <c>0.0.0.0:9999</c> and modules are addressed on the subnet broadcast at
    /// port <c>8888</c>. PGN frames begin with the AOG header <c>0x80 0x81</c> and the loopback/UDP
    /// routing switch on <c>data[3]</c> is unchanged. The only transforms versus the WinForms original
    /// are: (a) the WinForms <c>BeginInvoke</c> UI-thread marshal in the two receive callbacks is
    /// replaced by a <b>direct call</b> on the socket-callback thread — the receive path no longer
    /// touches any UI, so this preserves timing with no added latency (AAP §0.6.1); (b) the former
    /// status <c>Label</c>/button-colour mutations are surfaced through <see cref="StatusChanged"/> and
    /// public properties for the Avalonia view-model to bind; (c) the <c>FormLoop</c> <c>mf</c> peer
    /// references become the constructor-free, after-construction-wired <see cref="Nmea"/> and
    /// <see cref="Serial"/> peer properties; (d) the <c>MessageBox</c> error is routed through
    /// <see cref="StatusChanged"/>. Everything on the byte/socket path is verbatim.
    /// </remarks>
    public sealed class UdpLoopbackService : IDisposable
    {
        /// <summary>
        /// [XPLAT] Peer reference to the NMEA parse/build service. Wired AFTER construction by the
        /// composition root to break the construction cycle (each service needs the other). Guarded
        /// with the null-conditional operator at every call site (peers may be briefly null during
        /// bootstrap).
        /// </summary>
        internal NmeaService Nmea { get; set; }

        /// <summary>
        /// [XPLAT] Peer reference to the serial transport service used to relay AgOpenGPS PGN frames to
        /// serial-connected steer/machine modules. Wired AFTER construction by the composition root;
        /// guarded with the null-conditional operator at every call site.
        /// </summary>
        internal SerialCommService Serial { get; set; }

        // loopback Socket
        private Socket loopBackSocket;
        private EndPoint endPointLoopBack = new IPEndPoint(IPAddress.Loopback, 0);

        // UDP Socket
        public Socket UDPSocket;
        private EndPoint endPointUDP = new IPEndPoint(IPAddress.Any, 0);

        public bool isUDPNetworkConnected;

        // 2 endpoints for the local loopback (AgOpenGPS) and the module UDP broadcast.
        // [XPLAT] Loopback peer resolved to the UNICAST loopback host 127.0.0.1 instead of the 127/8
        // directed broadcast 127.255.255.255 that the eth_loop settings default to. On Windows a datagram
        // addressed to 127.255.255.255 is delivered to a socket bound to the specific address 127.0.0.1,
        // but Linux and macOS do NOT deliver a 127/8 directed broadcast to a specifically-bound loopback
        // socket, so the frozen WinForms idiom silently failed to reach the GPS peer cross-platform
        // (QA F4-C1). The loopback fabric is always a same-machine two-program model, so unicast 127.0.0.1
        // is the loopback-faithful send target on every OS and reaches GPS's receiver bound to
        // 127.0.0.1:15555. The eth_loop settings schema (eth_loopOne..eth_loopFour) and the frozen port
        // 15555 are preserved — the Ethernet config dialog (FormEthernetViewModel) still reads/persists
        // those keys. The module endpoint below retains the InvariantCulture dotted-quad conversion and the
        // ".255" subnet-broadcast suffix (those reach real hardware on the LAN, not loopback). — see
        // TRANSITION_MAP.md
        private readonly IPEndPoint epAgOpen = new IPEndPoint(IPAddress.Loopback, 15555);

        // [XPLAT] Not readonly: the UDP dialog (FormUDPViewModel.SendSubnet) rebuilds this endpoint to the
        // new ".255" broadcast address when the operator changes the module subnet, matching the original
        // WinForms FormUDP which reassigned mf.epModule. Without a setter, AgIO would keep broadcasting to
        // the previous subnet until the next restart (a real regression).
        private IPEndPoint epModule = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.etIP_SubnetOne.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.etIP_SubnetTwo.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.etIP_SubnetThree.ToString(CultureInfo.InvariantCulture) + ".255"), 8888);

        /// <summary>
        /// [XPLAT] The module subnet-broadcast endpoint (port 8888). Exposed so the NMEA service can
        /// optionally forward the assembled PGN 0xD6 GPS frame to the modules (the former
        /// <c>SendUDPMessage(nmeaPGN, epModule)</c>), and so the UDP dialog can repoint it to a newly
        /// configured subnet's broadcast address (parity with the original <c>mf.epModule</c> reassignment).
        /// </summary>
        public IPEndPoint EpModule
        {
            get { return epModule; }
            set { epModule = value; }
        }

        /// <summary>
        /// [XPLAT] NTRIP endpoint, assigned by <c>NtripService</c> when an NTRIP source is active. The
        /// NTRIP service forwards RTCM corrections via <c>SendUDPMessage(rtcm, epNtrip)</c>. Kept as a
        /// public field (matching the original <c>FormLoop</c> field) so the peer can set it directly.
        /// </summary>
        public IPEndPoint epNtrip;

        public IPEndPoint epModuleSet = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 8888);
        public byte[] ipAutoSet = { 192, 168, 5 };

        // class for counting bytes
        public CTraffic traffic = new CTraffic();
        public CScanReply scanReply = new CScanReply();

        /// <summary>Exposes the byte counters for the shell/view-model to display.</summary>
        public CTraffic Traffic => traffic;

        /// <summary>Exposes the module scan-reply state for the shell/view-model to display.</summary>
        public CScanReply ScanReply => scanReply;

        // scan results placed here
        public string scanReturn = "Scanning...";

        // Data stream
        private readonly byte[] buffer = new byte[1024];

        // used to send communication check pgn= C8 or 200 — FROZEN exact bytes
        private readonly byte[] helloFromAgIO = { 0x80, 0x81, 0x7F, 200, 3, 56, 0, 0, 0x47 };

        public IPAddress ipCurrent;

        // [XPLAT] Monitor/log flags formerly declared on FormLoop; kept as public state so the
        // coordinator/view-model can drive them (the original UI label/button mutations are dropped and
        // become bindings/events). The UDP monitor log buffer and the ping baseline are retained so the
        // diagnostics view keeps full parity.
        public bool isUDPMonitorOn;
        public bool isNTRIPLogOn;
        public bool isGPSLogOn;
        public bool isViewAdvanced;
        public StringBuilder logUDPSentence = new StringBuilder();
        public double pingSecondsStart;

        // [XPLAT] The advanced-view diagnostic values formerly written straight into WinForms Labels
        // (lblPing / lblSteerAngle / lblWASCounts / lblSwitchStatus / lblWorkSwitchStatus /
        // lblPingMachine / lbl1To8 / lbl9To16). They are now computed with the identical formatting and
        // exposed as public properties for the Avalonia view-model to bind. They carry display strings
        // only and never affect the PGN/byte path.
        public string Ping { get; private set; } = "";
        public string SteerAngle { get; private set; } = "";
        public string WasCounts { get; private set; } = "";
        public string SwitchStatus { get; private set; } = "";
        public string WorkSwitchStatus { get; private set; } = "";
        public string PingMachine { get; private set; } = "";
        public string Sections1To8 { get; private set; } = "";
        public string Sections9To16 { get; private set; } = "";

        // [XPLAT] Cache the process start time once (disposing the transient Process handle) so the
        // advanced-view ping uses the identical formula as the WinForms original — the value is constant
        // for the lifetime of the process — without leaking a Process handle on every inbound hello frame.
        private static readonly DateTime ProcessStartTime = GetProcessStartTime();

        private static DateTime GetProcessStartTime()
        {
            using (Process p = Process.GetCurrentProcess())
            {
                return p.StartTime;
            }
        }

        /// <summary>
        /// [XPLAT] Raised when transport status changes (connect / load error). Replaces the WinForms
        /// direct status-label and button-colour mutation. The payload carries the local IP list on a
        /// successful UDP load, or an error description on failure, for the Avalonia shell to surface.
        /// </summary>
        public event EventHandler<UdpStatusEventArgs> StatusChanged;

        private void RaiseStatus(bool connected, string message)
        {
            StatusChanged?.Invoke(this, new UdpStatusEventArgs(connected, message));
        }

        /// <summary>
        /// Initialise the module UDP network socket (bind <c>0.0.0.0:9999</c>, broadcast enabled) and
        /// begin the asynchronous receive loop. Mirrors the WinForms <c>LoadUDPNetwork()</c> exactly,
        /// minus the direct UI updates which are now surfaced via <see cref="StatusChanged"/>.
        /// </summary>
        public void LoadUDPNetwork()
        {
            helloFromAgIO[5] = 56;

            // [XPLAT] collect the host's IPv4 addresses for the shell to display (replaces lblIP.Text)
            StringBuilder localIps = new StringBuilder();
            try // udp network
            {
                foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (IPA.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIps.Append(IPA.ToString().Trim()).Append("\r\n");
                    }
                }

                // Initialise the socket
                UDPSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                UDPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                UDPSocket.Bind(new IPEndPoint(IPAddress.Any, 9999));
                UDPSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointUDP,
                    new AsyncCallback(ReceiveDataUDPAsync), null);

                isUDPNetworkConnected = true;

                if (isUDPNetworkConnected)
                {
                    Log.EventWriter("UDP Network is connected: " + epModule.ToString());
                }
                else
                {
                    Log.EventWriter("UDP Network Failed to Connect");
                }

                RaiseStatus(true, localIps.ToString());
            }
            catch (Exception e)
            {
                Log.EventWriter("Catch -> Load UDP Server" + e);
                RaiseStatus(false, "Serious Network Connection Error: " + e.Message);
            }
        }

        /// <summary>
        /// Initialise the loopback socket (bind <c>127.0.0.1:17777</c>) and begin the asynchronous
        /// receive loop that carries AgOpenGPS traffic. Mirrors the WinForms <c>LoadLoopback()</c>
        /// exactly. The loopback bind to port 17777 is FROZEN.
        /// </summary>
        public void LoadLoopback()
        {
            try // loopback
            {
                loopBackSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                loopBackSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                loopBackSocket.Bind(new IPEndPoint(IPAddress.Loopback, 17777));
                loopBackSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointLoopBack,
                    new AsyncCallback(ReceiveDataLoopAsync), null);
                Log.EventWriter("Loopback is Connected: " + IPAddress.Loopback.ToString() + ":17777");
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Load UDP Loopback Failed: " + ex.ToString());
                RaiseStatus(false, "Loopback Server load error: " + ex.Message);
            }
        }

        /// <summary>
        /// [XPLAT] Broadcasts the AgIO "hello" frame (PGN 200) to the module subnet endpoint, as the
        /// WinForms scan loop did with <c>SendUDPMessage(helloFromAgIO, epModule)</c>. The shell's scan
        /// timer calls this on its cadence so modules know AgIO is alive. No-op until the UDP network is
        /// loaded (the send is gated by <see cref="isUDPNetworkConnected"/>).
        /// </summary>
        public void SendHelloToModules()
        {
            SendUDPMessage(helloFromAgIO, epModule);
        }

        #region Send LoopBack

        /// <summary>
        /// Sends an assembled frame to AgOpenGPS on the loopback subnet (port 15555). This is the single
        /// outward call the peer services route their frames through (e.g. the NMEA 0xD6 GPS frame).
        /// </summary>
        public void SendToLoopBackMessageAOG(byte[] byteData)
        {
            SendDataToLoopBack(byteData, epAgOpen);
        }

        private void SendDataToLoopBack(byte[] byteData, IPEndPoint endPoint)
        {
            try
            {
                if (byteData.Length != 0 && loopBackSocket != null)
                {
                    // Send packet to AgOpenGPS
                    loopBackSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, endPoint,
                        new AsyncCallback(SendDataLoopAsync), null);
                }
            }
            catch
            {
            }
        }

        public void SendDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                loopBackSocket.EndSend(asyncResult);
            }
            catch
            {
            }
        }

        #endregion

        #region Receive LoopBack

        private void ReceiveFromLoopBack(byte[] data)
        {
            // Send out to udp network
            SendUDPMessage(data, epModule);

            // [XPLAT] SEC-2 (CWE-20): a valid PGN frame is at least [0x80][0x81][src][pgn]..., so guard the PGN
            // header reads below against null or short datagrams. The receive callback hands us a right-sized
            // array (new byte[msgLen] + Array.Copy), so a runt packet would otherwise throw IndexOutOfRange on
            // data[0]/data[1]/data[3]. Forwarding above is unchanged; only the header parse is gated.
            if (data == null || data.Length < 4)
                return;

            if (data[0] == 0x80 && data[1] == 0x81)
            {
                switch (data[3])
                {
                    case 0xFE: // 254 AutoSteer Data
                        {
                            Serial?.SendSteerModulePort(data, data.Length);
                            Serial?.SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xEF: // 239 machine pgn
                        {
                            Serial?.SendMachineModulePort(data, data.Length);
                            Serial?.SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xE5: // 229 Symmetric Sections - Zones
                        {
                            Serial?.SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xFC: // 252 steer settings
                        {
                            Serial?.SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFB: // 251 steer config
                        {
                            Serial?.SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xEE: // 238 machine config
                        {
                            Serial?.SendMachineModulePort(data, data.Length);
                            Serial?.SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xEC: // 236 machine config
                        {
                            Serial?.SendMachineModulePort(data, data.Length);
                            Serial?.SendSteerModulePort(data, data.Length);
                            break;
                        }
                }
            }
        }

        private void ReceiveDataLoopAsync(IAsyncResult asyncResult)
        {
            try
            {
                // Receive all data
                int msgLen = loopBackSocket.EndReceiveFrom(asyncResult, ref endPointLoopBack);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(buffer, localMsg, msgLen);

                // Listen for more connections again...
                loopBackSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointLoopBack,
                    new AsyncCallback(ReceiveDataLoopAsync), null);

                // [XPLAT] Direct call on the socket-callback thread (replaces the WinForms
                // BeginInvoke UI-thread marshal). The dispatch logic touches no UI, so the direct call
                // preserves real-time timing with NO added latency (AAP §0.6.1 / §0.7.1).
                ReceiveFromLoopBack(localMsg);
            }
            catch
            {
            }
        }

        #endregion

        #region Send UDP

        public void SendUDPMessage(byte[] byteData, IPEndPoint endPoint)
        {
            if (isUDPNetworkConnected)
            {
                if (isUDPMonitorOn)
                {
                    if (epNtrip != null && endPoint.Port == epNtrip.Port)
                    {
                        if (isNTRIPLogOn)
                        {
                            logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t", CultureInfo.InvariantCulture)
                                + endPoint.ToString() + "\t" + " > NTRIP\r\n");
                        }
                    }
                    else
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t", CultureInfo.InvariantCulture)
                            + endPoint.ToString() + "\t" + " > " + byteData[3].ToString(CultureInfo.InvariantCulture) + "\r\n");
                    }
                }

                try
                {
                    // Send packet to the module subnet
                    if (byteData.Length != 0)
                    {
                        UDPSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None,
                           endPoint, new AsyncCallback(SendDataUDPAsync), null);
                    }
                }
                catch (Exception)
                {
                    // best-effort send; parity with the original silent catch (transport errors are
                    // non-fatal to the comm loop)
                }
            }
        }

        private void SendDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                UDPSocket.EndSend(asyncResult);
            }
            catch
            {
            }
        }

        #endregion

        #region Receive UDP

        private void ReceiveDataUDPAsync(IAsyncResult asyncResult)
        {
            try
            {
                // Receive all data
                int msgLen = UDPSocket.EndReceiveFrom(asyncResult, ref endPointUDP);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(buffer, localMsg, msgLen);

                // Listen for more connections again...
                UDPSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPointUDP,
                    new AsyncCallback(ReceiveDataUDPAsync), null);

                // [XPLAT] Direct call (replaces the WinForms BeginInvoke UI-thread marshal); no UI is
                // touched, so timing is preserved with no added latency (AAP §0.6.1 / §0.7.1).
                ReceiveFromUDP(localMsg);
            }
            catch
            {
            }
        }

        private void ReceiveFromUDP(byte[] data)
        {
            try
            {
                // [XPLAT] SEC-3 (CWE-20): drop null/short datagrams before reading the PGN header. The deeper
                // byte reads in each branch are already gated by exact-length checks (data.Length == 11/13),
                // but the unconditional data[0]/data[1]/data[3] reads need this minimum-length guard so a runt
                // packet cannot throw IndexOutOfRange (silently swallowed by the catch) — it is dropped cleanly.
                if (data == null || data.Length < 4)
                    return;

                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    // module return via udp sent to AOG
                    SendToLoopBackMessageAOG(data);

                    // check for Scan and Hello
                    if (data[3] == 126 && data.Length == 11)
                    {
                        traffic.helloFromAutoSteer = 0;
                        if (isViewAdvanced)
                        {
                            Ping = (((DateTime.Now - ProcessStartTime).TotalSeconds - pingSecondsStart) * 1000)
                                .ToString("N0", CultureInfo.InvariantCulture);
                            double actualSteerAngle = (Int16)((data[6] << 8) + data[5]);
                            SteerAngle = (actualSteerAngle * 0.01).ToString("N1", CultureInfo.InvariantCulture);
                            WasCounts = ((Int16)((data[8] << 8) + data[7])).ToString(CultureInfo.InvariantCulture);

                            SwitchStatus = ((data[9] & 2) == 2).ToString();
                            WorkSwitchStatus = ((data[9] & 1) == 1).ToString();
                        }
                    }

                    else if (data[3] == 123 && data.Length == 11)
                    {
                        traffic.helloFromMachine = 0;

                        if (isViewAdvanced)
                        {
                            PingMachine = (((DateTime.Now - ProcessStartTime).TotalSeconds - pingSecondsStart) * 1000)
                                .ToString("N0", CultureInfo.InvariantCulture);
                            Sections1To8 = Convert.ToString(data[5], 2).PadLeft(8, '0');
                            Sections9To16 = Convert.ToString(data[6], 2).PadLeft(8, '0');
                        }
                    }

                    else if (data[3] == 121 && data.Length == 11)
                        traffic.helloFromIMU = 0;

                    // scan Reply
                    else if (data[3] == 203 && data.Length == 13) //
                    {
                        if (data[2] == 126)  // steer module
                        {
                            scanReply.steerIP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewSteer = true;
                        }
                        //
                        else if (data[2] == 123)   // machine module
                        {
                            scanReply.machineIP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewMachine = true;
                        }
                        else if (data[2] == 121)   // IMU Module
                        {
                            scanReply.IMU_IP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewIMU = true;
                        }

                        else if (data[2] == 120)    // GPS module
                        {
                            scanReply.GPS_IP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewGPS = true;
                        }
                    }

                    if (isUDPMonitorOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t", CultureInfo.InvariantCulture)
                            + endPointUDP.ToString() + "\t" + " < " + data[3].ToString(CultureInfo.InvariantCulture) + "\r\n");
                    }
                } // end of pgns

                else if (data[0] == 36 && (data[1] == 71 || data[1] == 80 || data[1] == 75))
                {
                    traffic.cntrGPSOut += data.Length;

                    // [XPLAT] route the raw "$" sentence into the NMEA peer (mf.rawBuffer -> Nmea.rawBuffer);
                    // rawBuffer is public and ParseNMEA takes it by ref, exactly as the original did.
                    if (Nmea != null)
                    {
                        Nmea.rawBuffer += Encoding.ASCII.GetString(data);
                        Nmea.ParseNMEA(ref Nmea.rawBuffer);
                    }

                    if (isUDPMonitorOn && isGPSLogOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t", CultureInfo.InvariantCulture)
                            + Encoding.ASCII.GetString(data));
                    }
                }
            }
            catch
            {
            }
        }

        #endregion

        /// <summary>
        /// Releases both sockets so the loopback/UDP ports (17777 / 9999, and implicitly 15555 on the
        /// loopback subnet) are freed on shutdown and restart. Idempotent and best-effort: socket
        /// teardown races are swallowed exactly as the original form-close path tolerated them.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try { loopBackSocket?.Close(); loopBackSocket?.Dispose(); } catch { /* best-effort teardown */ }
            try { UDPSocket?.Close(); UDPSocket?.Dispose(); } catch { /* best-effort teardown */ }

            isUDPNetworkConnected = false;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// [XPLAT] Explicit stop entry point kept for the composition root, which calls <c>Stop()</c> on
        /// window close. Delegates to <see cref="Dispose"/> so socket teardown happens exactly once.
        /// </summary>
        public void Stop()
        {
            Dispose();
        }

        private bool _disposed;
    }

    /// <summary>
    /// [XPLAT] Status payload raised by <see cref="UdpLoopbackService.StatusChanged"/>, replacing the
    /// WinForms direct status-label / button-colour mutation.
    /// </summary>
    public sealed class UdpStatusEventArgs : EventArgs
    {
        public UdpStatusEventArgs(bool connected, string message)
        {
            Connected = connected;
            Message = message;
        }

        /// <summary>True when the UDP module network is connected.</summary>
        public bool Connected { get; }

        /// <summary>Local IP list on success, or an error description on failure.</summary>
        public string Message { get; }
    }
}
