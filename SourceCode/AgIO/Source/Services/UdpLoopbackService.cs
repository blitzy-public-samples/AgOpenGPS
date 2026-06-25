// [XPLAT] migrated from net48/WinForms Forms/UDP.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgLibrary.Logging;
using Avalonia.Threading;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Byte counters for the comm hub, extracted unchanged from the WinForms
    /// <c>FormLoop</c> partial (<c>UDP.designer.cs</c>). Behaviour frozen.
    /// </summary>
    public class CTraffic
    {
        public int cntrGPSIn = 0;
        public int cntrGPSInBytes = 0;
        public int cntrGPSOut = 0;

        public uint helloFromMachine = 99, helloFromAutoSteer = 99, helloFromIMU = 99;
    }

    /// <summary>
    /// [XPLAT] Module scan-reply state, extracted unchanged from the WinForms <c>FormLoop</c>
    /// partial (<c>UDP.designer.cs</c>). Populated from inbound PGN 203 scan replies; read by the
    /// shell to display discovered Steer/Machine/IMU/GPS module IP addresses. Behaviour frozen.
    /// </summary>
    public class CScanReply
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
    /// (behaviour frozen) from the former WinForms <c>FormLoop</c> partial class
    /// (<c>SourceCode/AgIO/Source/Forms/UDP.designer.cs</c>). This is the core of the AgIO comm hub:
    /// it bridges AgOpenGPS (over the loopback subnet) and the steering/machine/IMU/GPS modules
    /// (over the UDP network and serial), forwarding raw PGN frames in both directions.
    /// </summary>
    /// <remarks>
    /// The byte-for-byte protocol contract is preserved exactly (AAP R2): the loopback socket binds
    /// to <c>127.0.0.1:17777</c> (loopback-only enforcement, AAP R11), AgOpenGPS is addressed on
    /// port <c>15555</c>, the module UDP socket binds to <c>0.0.0.0:9999</c> and modules are
    /// addressed on the subnet broadcast at port <c>8888</c>. PGN frames begin with the AOG header
    /// <c>0x80 0x81</c> and the loopback/UDP routing switch on <c>data[3]</c> is unchanged. The only
    /// behavioural change versus the WinForms original is the removal of direct UI mutation
    /// (status labels / button colours): inbound packets are still marshalled onto the UI thread via
    /// <see cref="Dispatcher"/> (replacing the WinForms <c>BeginInvoke</c>) so the shared
    /// <see cref="NmeaService"/> and <see cref="ScanReply"/> state stays single-threaded, but status
    /// is now surfaced through <see cref="StatusChanged"/> for the Avalonia shell to bind.
    /// </remarks>
    public sealed class UdpLoopbackService
    {
        /// <summary>
        /// [XPLAT] Peer reference to the NMEA parse/build service. Wired AFTER construction by the
        /// composition root to break the construction cycle (each service needs the other).
        /// </summary>
        internal NmeaService Nmea { get; set; }

        /// <summary>
        /// [XPLAT] Peer reference to the serial transport service used to relay AgOpenGPS PGN frames
        /// to serial-connected steer/machine modules. Wired AFTER construction by the composition
        /// root; guarded with the null-conditional operator at every call site.
        /// </summary>
        internal SerialCommService Serial { get; set; }

        // loopback Socket
        private Socket loopBackSocket;
        private EndPoint endPointLoopBack = new IPEndPoint(IPAddress.Loopback, 0);

        // UDP Socket
        public Socket UDPSocket;
        private EndPoint endPointUDP = new IPEndPoint(IPAddress.Any, 0);

        public bool isUDPNetworkConnected;

        // 2 endpoints for local loopback (AgOpenGPS) and the module UDP broadcast.
        // [XPLAT] Settings byte->string uses InvariantCulture so a comma-decimal locale on
        // Linux/macOS can never corrupt the dotted-quad address (AAP R2 culture safety).
        private readonly IPEndPoint epAgOpen = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.eth_loopOne.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.eth_loopTwo.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.eth_loopThree.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.eth_loopFour.ToString(CultureInfo.InvariantCulture)), 15555);

        private readonly IPEndPoint epModule = new IPEndPoint(IPAddress.Parse(
            Properties.Settings.Default.etIP_SubnetOne.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.etIP_SubnetTwo.ToString(CultureInfo.InvariantCulture) + "." +
            Properties.Settings.Default.etIP_SubnetThree.ToString(CultureInfo.InvariantCulture) + ".255"), 8888);

        /// <summary>
        /// [XPLAT] The module subnet-broadcast endpoint (port 8888). Exposed so the NMEA service can
        /// optionally forward the assembled 0xD6 GPS frame to the modules (the former
        /// <c>SendUDPMessage(nmeaPGN, epModule)</c>).
        /// </summary>
        public IPEndPoint EpModule => epModule;

        /// <summary>[XPLAT] NTRIP endpoint, assigned by the NTRIP service when active.</summary>
        public IPEndPoint epNtrip;

        public IPEndPoint epModuleSet = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 8888);
        public byte[] ipAutoSet = { 192, 168, 5 };

        // class for counting bytes
        public CTraffic traffic = new CTraffic();
        public CScanReply scanReply = new CScanReply();

        /// <summary>Exposes the byte counters for the shell to display.</summary>
        public CTraffic Traffic => traffic;

        /// <summary>Exposes the module scan-reply state for the shell to display.</summary>
        public CScanReply ScanReply => scanReply;

        // scan results placed here
        public string scanReturn = "Scanning...";

        // Data stream
        private readonly byte[] buffer = new byte[1024];

        // used to send communication check pgn= C8 or 200
        private readonly byte[] helloFromAgIO = { 0x80, 0x81, 0x7F, 200, 3, 56, 0, 0, 0x47 };

        public IPAddress ipCurrent;

        /// <summary>
        /// [XPLAT] Raised when transport status changes (connect/disconnect/error). Replaces the
        /// WinForms direct label/button-colour mutation. The string carries the local IP list on
        /// success or an error message on failure, for the shell to surface.
        /// </summary>
        public event EventHandler<UdpStatusEventArgs> StatusChanged;

        private void RaiseStatus(bool connected, string message)
        {
            StatusChanged?.Invoke(this, new UdpStatusEventArgs(connected, message));
        }

        /// <summary>
        /// Initialise the module UDP network socket (bind 0.0.0.0:9999, broadcast enabled) and begin
        /// the asynchronous receive loop. Mirrors the WinForms <c>LoadUDPNetwork()</c> exactly, minus
        /// the direct UI updates which are now surfaced via <see cref="StatusChanged"/>.
        /// </summary>
        public void LoadUDPNetwork()
        {
            helloFromAgIO[5] = 56;

            var localIps = new StringBuilder();
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

                Log.EventWriter("UDP Network is connected: " + epModule.ToString());
                RaiseStatus(true, localIps.ToString());
            }
            catch (Exception e)
            {
                Log.EventWriter("Catch -> Load UDP Server" + e);
                RaiseStatus(false, "Serious Network Connection Error: " + e.Message);
            }
        }

        /// <summary>
        /// Initialise the loopback socket (bind 127.0.0.1:17777) and begin the asynchronous receive
        /// loop that carries AgOpenGPS traffic. Mirrors the WinForms <c>LoadLoopback()</c> exactly.
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
                Log.EventWriter("Catch - > Load UDP Loopback Failed: " + ex.Message);
                RaiseStatus(false, "Loopback Server load error: " + ex.Message);
            }
        }

        /// <summary>
        /// Releases both sockets. Mirrors the implicit WinForms form-close socket teardown so the
        /// loopback/UDP ports are freed on shutdown and restart.
        /// </summary>
        public void Stop()
        {
            try { loopBackSocket?.Close(); } catch { /* best-effort teardown */ }
            try { UDPSocket?.Close(); } catch { /* best-effort teardown */ }
            isUDPNetworkConnected = false;
        }

        /// <summary>
        /// [XPLAT] Broadcasts the AgIO "hello" frame (PGN 200) to the module subnet endpoint, exactly as
        /// the WinForms <c>FormLoop.TwoSecondLoop()</c> did with <c>SendUDPMessage(helloFromAgIO, epModule)</c>.
        /// The hello frame is kept private to this service; the shell's scan loop calls this method on its
        /// two-second cadence so modules know AgIO is alive. No-op until the UDP network is loaded.
        /// </summary>
        public void SendHelloToModules()
        {
            SendUDPMessage(helloFromAgIO, epModule);
        }

        #region Send LoopBack

        /// <summary>
        /// Sends an assembled frame to AgOpenGPS on the loopback subnet (port 15555). This is the
        /// single outward call the NMEA service routes its 0xD6 GPS frame through.
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
                    loopBackSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, endPoint,
                        new AsyncCallback(SendDataLoopAsync), null);
                }
            }
            catch
            {
                // best-effort send; transport errors are non-fatal to the comm loop (parity)
            }
        }

        private void SendDataLoopAsync(IAsyncResult asyncResult)
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

                // [XPLAT] marshal processing onto the UI thread (replaces WinForms BeginInvoke) so the
                // shared NmeaService/scanReply state remains single-threaded exactly as before.
                Dispatcher.UIThread.Post(() => ReceiveFromLoopBack(localMsg));
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
                    // best-effort send; parity with the original silent catch
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

                // [XPLAT] marshal processing onto the UI thread (replaces WinForms BeginInvoke).
                Dispatcher.UIThread.Post(() => ReceiveFromUDP(localMsg));
            }
            catch
            {
            }
        }

        private void ReceiveFromUDP(byte[] data)
        {
            try
            {
                if (data[0] == 0x80 && data[1] == 0x81)
                {
                    // module return via udp sent to AOG
                    SendToLoopBackMessageAOG(data);

                    // check for Scan and Hello
                    if (data[3] == 126 && data.Length == 11)
                    {
                        traffic.helloFromAutoSteer = 0;
                    }
                    else if (data[3] == 123 && data.Length == 11)
                    {
                        traffic.helloFromMachine = 0;
                    }
                    else if (data[3] == 121 && data.Length == 11)
                    {
                        traffic.helloFromIMU = 0;
                    }

                    // scan Reply
                    else if (data[3] == 203 && data.Length == 13)
                    {
                        if (data[2] == 126) // steer module
                        {
                            scanReply.steerIP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewSteer = true;
                        }
                        else if (data[2] == 123) // machine module
                        {
                            scanReply.machineIP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewMachine = true;
                        }
                        else if (data[2] == 121) // IMU Module
                        {
                            scanReply.IMU_IP = data[5].ToString(CultureInfo.InvariantCulture) + "." + data[6].ToString(CultureInfo.InvariantCulture) + "." + data[7].ToString(CultureInfo.InvariantCulture) + "." + data[8].ToString(CultureInfo.InvariantCulture);

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString(CultureInfo.InvariantCulture) + "." + data[10].ToString(CultureInfo.InvariantCulture) + "." + data[11].ToString(CultureInfo.InvariantCulture);

                            scanReply.isNewData = true;
                            scanReply.isNewIMU = true;
                        }
                        else if (data[2] == 120) // GPS module
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
                } // end of pgns

                else if (data[0] == 36 && (data[1] == 71 || data[1] == 80 || data[1] == 75))
                {
                    traffic.cntrGPSOut += data.Length;
                    if (Nmea != null)
                    {
                        Nmea.rawBuffer += Encoding.ASCII.GetString(data);
                        Nmea.ParseNMEA(ref Nmea.rawBuffer);
                    }
                }
            }
            catch
            {
            }
        }

        #endregion
    }

    /// <summary>
    /// [XPLAT] Status payload raised by <see cref="UdpLoopbackService.StatusChanged"/>, replacing the
    /// WinForms direct label/button-colour mutation.
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
