// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Net;
using System.Net.Sockets;
using System.Globalization;   // [XPLAT] InvariantCulture for culture-independent log text on all OS locales
using Avalonia.Threading;     // [XPLAT] Dispatcher.UIThread.Post replaces WinForms Control.Invoke for cross-thread UI marshalling

namespace GPS_Out
{
    public class UDPComm
    {
        // [XPLAT] the former view back-reference (god-object) was removed; collaborators are now
        // constructor-injected so this transport no longer depends on the Avalonia view layer.
        private readonly clsTools tools;
        private readonly Action<byte[]> routeAgio;
        private readonly Action<byte[]> routeAog;
        private byte[] buffer = new byte[1024];
        private string cConnectionName;
        private bool cIsUDPSendConnected;
        private string cLog;
        private IPAddress cNetworkEP;
        private int cReceivePort;   // local ports must be unique for each app on same pc and each class instance
        private int cSendFromPort;
        private int cSendToPort;
        private IPAddress cSourceIP;
        private string cSubNet;
        private Socket recvSocket;
        private Socket sendSocket;

        public UDPComm(clsTools Tools, int ReceivePort, int SendToPort, int SendFromPort,
            string ConnectionName, string SourceIPaddress,
            Action<byte[]> RouteAgio, Action<byte[]> RouteAog, string DestinationEndPoint = "")
        {
            // [XPLAT] assign injected collaborators BEFORE SetEP/SetSourceIP: those run during
            // construction and use `tools` for property load and error logging.
            tools = Tools;
            cReceivePort = ReceivePort;
            cSendToPort = SendToPort;
            cSendFromPort = SendFromPort;
            cConnectionName = ConnectionName;
            routeAgio = RouteAgio;
            routeAog = RouteAog;
            SetEP(DestinationEndPoint);
            SetSourceIP(SourceIPaddress);
        }

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
                // initialize the receive socket
                recvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                recvSocket.Bind(new IPEndPoint(cSourceIP, cReceivePort));

                // initialize the send socket
                sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                // Initialise the IPEndPoint for the server to send on port
                IPEndPoint server = new IPEndPoint(IPAddress.Any, cSendFromPort);
                sendSocket.Bind(server);

                // Initialise the IPEndPoint for the client - async listner client only!
                EndPoint client = new IPEndPoint(IPAddress.Any, 0);

                // Start listening for incoming data
                recvSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref client, new AsyncCallback(ReceiveData), recvSocket);
                IsUDPSendConnected = true;
            }
            catch (Exception e)
            {
                tools.WriteErrorLog("UDPcomm/StartUDPServer: \n" + e.Message);
            }
        }

        private void AddToLog(string NewData)
        {
            cLog += DateTime.Now.Second.ToString(CultureInfo.InvariantCulture) + "  " + NewData + Environment.NewLine;
            if (cLog.Length > 100000)
            {
                cLog = cLog.Substring(cLog.Length - 98000, 98000);
            }
            cLog = cLog.Replace("\0", string.Empty);
        }

        private void HandleData(int Port, byte[] Data)
        {
            try
            {
                if (Data.Length > 1)
                {
                    int PGN = Data[1] << 8 | Data[0];
                    AddToLog("< " + PGN.ToString(CultureInfo.InvariantCulture));

                    switch (PGN)
                    {
                        case 33152: // AOG, 0x8180
                            int SubPGN = Data[3] << 8 | Data[2];
                            switch (SubPGN)
                            {
                                case 54908: // 0xD67C, AGIO NEMA translation
                                    routeAgio?.Invoke(Data);
                                    break;

                                case 25727: // 0x647F, AOG roll corrected lat,lon
                                    routeAog?.Invoke(Data);
                                    break;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog("UDPcomm/HandleData " + ex.Message);
            }
        }

        private void ReceiveData(IAsyncResult asyncResult)
        {
            try
            {
                // Initialise the IPEndPoint for the client
                EndPoint epSender = new IPEndPoint(IPAddress.Any, 0);

                // Receive all data
                int msgLen = recvSocket.EndReceiveFrom(asyncResult, ref epSender);

                byte[] localMsg = new byte[msgLen];
                Array.Copy(buffer, localMsg, msgLen);

                // Listen for more connections again...
                recvSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref epSender, new AsyncCallback(ReceiveData), epSender);

                int port = ((IPEndPoint)epSender).Port;
                // [XPLAT] marshal the parsed datagram onto the Avalonia UI thread (was WinForms
                // Control.Invoke). Preserves the original receive->UI-thread handoff semantics so
                // PGN parsing that mutates UI-read state stays single-threaded.
                Dispatcher.UIThread.Post(() => HandleData(port, localMsg));
            }
            catch (ObjectDisposedException)
            {
                // do nothing
            }
            catch (Exception ex)
            {
                //tools.ShowHelp("ReceiveData Error \n" + e.Message, "Comm", 3000, true);
                tools.WriteErrorLog("UDPcomm/ReceiveData " + ex.Message);
            }
        }

        private void SendData(IAsyncResult asyncResult)
        {
            try
            {
                sendSocket.EndSend(asyncResult);
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog(" UDP Send Data" + ex.ToString());
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
                    string EP = tools.LoadProperty("EndPoint_" + cConnectionName);
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
                tools.WriteErrorLog("UDPcomm/SetEP " + ex.Message);
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
                tools.WriteErrorLog("UDPcomm/SetSourceEP " + ex.Message);
            }
        }
    }
}