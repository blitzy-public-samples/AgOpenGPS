using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using AgLibrary.Logging;
// IPC-REFACTOR: generated proto/gRPC contract types (PgnEnvelope, IpcConstants, the 23 message types).
using AgOpenGPS.Ipc;
// IPC-REFACTOR: Google.Protobuf.ByteString for RemoteSwitchMsg.SwitchData and IsoBusHeartbeatMsg.SectionStates.
using Google.Protobuf;

namespace AgIO
{
    public class CTraffic
    {     
        public int cntrGPSIn = 0;
        public int cntrGPSInBytes = 0;
        public int cntrGPSOut = 0;

        public uint helloFromMachine = 99, helloFromAutoSteer = 99, helloFromIMU = 99;
    }

    public class CScanReply
    {
        public string steerIP =   "";
        public string machineIP = "";
        public string GPS_IP =    "";
        public string IMU_IP =    "";
        public string subnetStr = "";

        public byte[] subnet = { 0, 0, 0 };

        public bool isNewSteer, isNewMachine, isNewGPS, isNewIMU;

        public bool isNewData = false;
    }

    public partial class FormLoop
    {
        // IPC-REFACTOR: loopBackSocket removed - the loopback UDP socket is replaced by the gRPC host started in FormLoop.cs.
        // IPC-REFACTOR: endPointLoopBack removed - it was only used by the removed loopback receive path.

        // UDP Socket
        public Socket UDPSocket;
        private EndPoint endPointUDP = new IPEndPoint(IPAddress.Any, 0);
        
        public bool isUDPNetworkConnected;

        //2 endpoints for local and 2 udp

        // IPC-REFACTOR: epAgOpen (127.x loopback ":15555" AOG send target) removed - the gRPC TelemetryService server-side stream replaces the loopback send target.

        public IPEndPoint epModule = new IPEndPoint(IPAddress.Parse(
                Properties.Settings.Default.etIP_SubnetOne.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetTwo.ToString() + "." +
                Properties.Settings.Default.etIP_SubnetThree.ToString() + ".255"), 8888);
        private IPEndPoint epNtrip;

        public IPEndPoint epModuleSet = new IPEndPoint(IPAddress.Parse("255.255.255.255"), 8888);
        public byte[] ipAutoSet = { 192, 168, 5 };

        //class for counting bytes
        public CTraffic traffic = new CTraffic();
        public CScanReply scanReply = new CScanReply();

        //scan results placed here
        public string scanReturn = "Scanning...";
        
        // Data stream
        private byte[] buffer = new byte[1024];

        //used to send communication check pgn= C8 or 200
        private byte[] helloFromAgIO = { 0x80, 0x81, 0x7F, 200, 3, 56, 0, 0, 0x47 };

        public IPAddress ipCurrent;
        //initialize loopback and udp network
        public void LoadUDPNetwork()
        {
            helloFromAgIO[5] = 56;

            lblIP.Text = "";
            try //udp network
            {
                foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (IPA.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string  data = IPA.ToString();
                        lblIP.Text += IPA.ToString().Trim() + "\r\n";
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

                btnUDP.BackColor = Color.LimeGreen;

                //if (!isFound)
                //{
                //    MessageBox.Show("Network Address of Modules -> " + Properties.Settings.Default.setIP_localAOG+"[2 - 254] May not exist. \r\n"
                //    + "Are you sure ethernet is connected?\r\n" + "Go to UDP Settings to fix.\r\n\r\n", "Network Connection Error",
                //    MessageBoxButtons.OK, MessageBoxIcon.Error);
                //    //btnUDP.BackColor = Color.Red;
                //    lblIP.Text = "Not Connected";
                //}
            }
            catch (Exception e)
            {
                Log.EventWriter("Catch -> Load UDP Server" + e);
                MessageBox.Show(e.Message, "Serious Network Connection Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnUDP.BackColor = Color.Red;
                lblIP.Text = "Error";
            }
        }

        // IPC-REFACTOR: LoadLoopback() removed - it bound the loopback UDP socket to 127.0.0.1:17777 and started
        // the loopback receive loop. The gRPC host started in FormLoop.cs (Kestrel UnixDomainSockets / Windows
        // named pipe, MapGrpcService Telemetry + Command) now fulfils that role.

        #region Telemetry Producer (gRPC fan-out)

        // IPC-REFACTOR: UDP broadcast to 127.255.255.255:17777 replaced by TelemetryService gRPC server-side stream.
        // This method is the hardware -> AOG telemetry PRODUCER: it parses each inbound legacy PGN byte frame
        // (header 0x80,0x81,src,PGN,Len followed by the payload data bytes) into the matching typed PgnEnvelope
        // payload and fans it out to every subscribed gRPC client via TelemetryServiceImpl.Broadcast. The name and
        // byte[] signature are preserved because NMEA.Designer.cs, FormLoop.cs, SerialComm.Designer.cs and
        // ReceiveFromUDP all still call it.
        private void SendToLoopBackMessageAOG(byte[] byteData)
        {
            // Guard the legacy frame header; anything that is not a well-formed PGN frame is ignored.
            if (byteData == null || byteData.Length < 5 || byteData[0] != 0x80 || byteData[1] != 0x81)
            {
                return;
            }

            // Build the typed envelope only for the mapped inbound PGNs. Unmapped PGNs leave env null and are
            // silently skipped (see the default case). Every array read below is length-guarded so a short or
            // malformed frame never throws - it is simply not fanned out (parity with best-effort UDP).
            PgnEnvelope env = null;

            switch (byteData[3])
            {
                case 0xD6: // 214 GPS Position -> GpsPositionMsg (52-byte payload; last field at bytes 54-55)
                    if (byteData.Length >= 56)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            GpsPosition = new GpsPositionMsg
                            {
                                Longitude = BitConverter.ToDouble(byteData, 5),
                                Latitude = BitConverter.ToDouble(byteData, 13),
                                HeadingDual = BitConverter.ToSingle(byteData, 21),
                                HeadingTrue = BitConverter.ToSingle(byteData, 25),
                                Speed = BitConverter.ToSingle(byteData, 29),
                                Roll = BitConverter.ToSingle(byteData, 33),
                                Altitude = BitConverter.ToSingle(byteData, 37),
                                Satellites = BitConverter.ToUInt16(byteData, 41),
                                FixQuality = byteData[43],
                                Hdop = BitConverter.ToUInt16(byteData, 44),
                                Age = BitConverter.ToUInt16(byteData, 46),
                                ImuHeading = BitConverter.ToUInt16(byteData, 48),
                                ImuRoll = BitConverter.ToInt16(byteData, 50),
                                ImuPitch = BitConverter.ToInt16(byteData, 52),
                                ImuYawRate = BitConverter.ToInt16(byteData, 54),
                            },
                        };
                    }
                    break;

                case 0xD3: // 211 External IMU -> ExternalImuMsg
                    if (byteData.Length >= 11)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            ExternalImu = new ExternalImuMsg
                            {
                                Heading = BitConverter.ToInt16(byteData, 5),
                                Roll = BitConverter.ToInt16(byteData, 7),
                                AngularVelocity = BitConverter.ToInt16(byteData, 9),
                            },
                        };
                    }
                    break;

                case 0xD4: // 212 IMU Disconnect -> ImuDisconnectMsg
                    if (byteData.Length >= 6)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            ImuDisconnect = new ImuDisconnectMsg
                            {
                                IsDisconnected = byteData[5] == 1,
                                RawValue = byteData[5],
                            },
                        };
                    }
                    break;

                case 0xFD: // 253 Steer Module Response -> SteerModuleResponseMsg
                    if (byteData.Length >= 13)
                    {
                        // Preserve the legacy N/A sentinels as typed validity flags (proto producer contract):
                        // heading 9999 and roll 8888 mean "not available"; consumers check the flag, not the magic value.
                        short steerHeadingRaw = BitConverter.ToInt16(byteData, 7);
                        short steerRollRaw = BitConverter.ToInt16(byteData, 9);
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            SteerModuleResponse = new SteerModuleResponseMsg
                            {
                                ActualSteerAngle = BitConverter.ToInt16(byteData, 5),
                                Heading = steerHeadingRaw,
                                Roll = steerRollRaw,
                                SwitchStatus = byteData[11],
                                Pwm = byteData[12],
                                HeadingValid = steerHeadingRaw != 9999,
                                RollValid = steerRollRaw != 8888,
                            },
                        };
                    }
                    break;

                case 0xFA: // 250 Sensor Data -> SensorDataMsg
                    if (byteData.Length >= 6)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            SensorData = new SensorDataMsg
                            {
                                SensorData = byteData[5],
                            },
                        };
                    }
                    break;

                case 0xEA: // 234 Remote Switches -> RemoteSwitchMsg (byte[8] at offset 5-12)
                    if (byteData.Length >= 13)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            RemoteSwitch = new RemoteSwitchMsg
                            {
                                SwitchData = ByteString.CopyFrom(byteData, 5, 8),
                            },
                        };
                    }
                    break;

                case 0xDD: // 221 Display Message -> DisplayHardwareMsg (variable length; Len at byte 4)
                    if (byteData.Length >= 7)
                    {
                        int displayLen = byteData[4];
                        string displayText = string.Empty;
                        // The message payload is (Len - 2) bytes at offset 7 (Len counts DisplayTime + Color + text).
                        if (displayLen >= 2 && 7 + (displayLen - 2) <= byteData.Length)
                        {
                            displayText = Encoding.UTF8.GetString(byteData, 7, displayLen - 2);
                        }
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            DisplayHardware = new DisplayHardwareMsg
                            {
                                DisplayTime = byteData[5],
                                Color = byteData[6],
                                Message = displayText,
                            },
                        };
                    }
                    break;

                case 0xDE: // 222 Remote Commands -> RemoteCommandMsg
                    if (byteData.Length >= 7)
                    {
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            RemoteCommand = new RemoteCommandMsg
                            {
                                Mask = byteData[5],
                                Command = byteData[6],
                            },
                        };
                    }
                    break;

                case 0xF0: // 240 ISOBUS Heartbeat -> IsoBusHeartbeatMsg (variable length; Status + NumSections + per-section bitmask)
                    if (byteData.Length >= 7)
                    {
                        // Section-state byte count = Len - 2 (Len counts Status + NumSections + the bitmask bytes),
                        // clamped to the bytes actually present so a short frame never over-reads.
                        int sectionCount = Math.Max(0, byteData[4] - 2);
                        sectionCount = Math.Min(sectionCount, byteData.Length - 7);
                        env = new PgnEnvelope
                        {
                            SchemaVersion = IpcConstants.SchemaVersion,
                            IsoBusHeartbeat = new IsoBusHeartbeatMsg
                            {
                                Status = byteData[5],
                                NumSections = byteData[6],
                                SectionStates = ByteString.CopyFrom(byteData, 7, sectionCount),
                            },
                        };
                    }
                    break;

                default:
                    // IPC-REFACTOR: unmapped PGNs (hardware hello/scan 126/123/121/203, PGN 200, etc.) are not part of
                    // the 23-message catalog and are not fanned out to AOG; AgIO-internal handling stays in ReceiveFromUDP.
                    break;
            }

            if (env != null)
            {
                // TelemetryServiceImpl.Broadcast is thread-safe and non-blocking (per-subscriber TryWrite), so it adds
                // no latency to the hardware ingestion thread and needs no lock/BeginInvoke here.
                TelemetryServiceImpl.Broadcast(env);
            }
        }

        // IPC-REFACTOR: SendDataToLoopBack removed - the loopback send helper (loopBackSocket.BeginSendTo) is no longer used.
        // IPC-REFACTOR: SendDataLoopAsync removed - the loopback send-completion callback is no longer used.

        #endregion

        #region Command Sink (gRPC -> hardware)

        // IPC-REFACTOR: former loopback receive handler is now invoked by CommandServiceImpl (gRPC) instead of the UDP loopback socket.
        // Renamed private ReceiveFromLoopBack -> public ForwardCommandToHardware and widened to public so CommandServiceImpl
        // (in Services/) can drive it from a gRPC server thread. The serial/UDP hardware forwarding dispatch below is
        // preserved byte-for-byte (same first send + same switch cases in the same order) - the on-the-wire hardware contract.
        public void ForwardCommandToHardware(byte[] data)
        {
            //Send out to udp network
            SendUDPMessage(data, epModule);

            if (data[0] == 0x80 && data[1] == 0x81)
            {
                switch (data[3])
                {
                    case 0xFE: //254 AutoSteer Data
                        {
                            //serList.AddRange(data);
                            SendSteerModulePort(data, data.Length);
                            SendMachineModulePort(data, data.Length);
                            break;
                        }
                    case 0xEF: //239 machine pgn
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xE5: //229 Symmetric Sections - Zones
                        {
                            SendMachineModulePort(data, data.Length);
                            //SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFC: //252 steer settings
                        {
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                    case 0xFB: //251 steer config
                        {
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEE: //238 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;                        }

                    case 0xEC: //236 machine config
                        {
                            SendMachineModulePort(data, data.Length);
                            SendSteerModulePort(data, data.Length);
                            break;
                        }
                }
            }                            
        }

        // IPC-REFACTOR: ReceiveDataLoopAsync removed - the loopback receive callback (loopBackSocket.EndReceiveFrom /
        // BeginReceiveFrom on the 127.0.0.1:17777 socket) is gone. Its BeginInvoke was the only caller of the former
        // ReceiveFromLoopBack, which is now the public ForwardCommandToHardware driven by CommandServiceImpl over gRPC.

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
                            logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPoint.ToString() + "\t" + " > NTRIP\r\n");
                    }
                    else
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPoint.ToString() + "\t" + " > " + byteData[3].ToString() + "\r\n");
                    }
                }

                try
                {
                    // Send packet to the zero
                    if (byteData.Length != 0)
                    {
                        UDPSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None,
                           endPoint, new AsyncCallback(SendDataUDPAsync), null);
                    }
                }
                catch (Exception)
                {
                    //WriteErrorLog("Sending UDP Message" + e.ToString());
                    //MessageBox.Show("Send Error: " + e.Message, "UDP Client", MessageBoxButtons.OK,
                    //MessageBoxIcon.Error);
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

                BeginInvoke((MethodInvoker)(() => ReceiveFromUDP(localMsg)));

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
                    //module return via udp sent to AOG
                    SendToLoopBackMessageAOG(data);

                    //check for Scan and Hello
                    if (data[3] == 126 && data.Length == 11)
                    {

                        traffic.helloFromAutoSteer = 0;
                        if (isViewAdvanced)
                        {
                            lblPing.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0");
                            double actualSteerAngle = (Int16)((data[6] << 8) + data[5]);
                            lblSteerAngle.Text = (actualSteerAngle * 0.01).ToString("N1");
                            lblWASCounts.Text = ((Int16)((data[8] << 8) + data[7])).ToString();

                            lblSwitchStatus.Text = ((data[9] & 2) == 2).ToString();
                            lblWorkSwitchStatus.Text = ((data[9] & 1) == 1).ToString();
                        }
                    }

                    else if (data[3] == 123 && data.Length == 11)
                    {

                        traffic.helloFromMachine = 0;

                        if (isViewAdvanced)
                        {
                            lblPingMachine.Text = (((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds - pingSecondsStart) * 1000).ToString("N0");
                            lbl1To8.Text = Convert.ToString(data[5], 2).PadLeft(8, '0');
                            lbl9To16.Text = Convert.ToString(data[6], 2).PadLeft(8, '0');
                        }
                    }

                    else if (data[3] == 121 && data.Length == 11)
                        traffic.helloFromIMU = 0;

                    //scan Reply
                    else if (data[3] == 203 && data.Length == 13) //
                    {
                        if (data[2] == 126)  //steer module
                        {
                            scanReply.steerIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewSteer = true;
                        }
                        //
                        else if (data[2] == 123)   //machine module
                        {
                            scanReply.machineIP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewMachine = true;

                        }
                        else if (data[2] == 121)   //IMU Module
                        {
                            scanReply.IMU_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewIMU = true;
                        }

                        else if (data[2] == 120)    //GPS module
                        {
                            scanReply.GPS_IP = data[5].ToString() + "." + data[6].ToString() + "." + data[7].ToString() + "." + data[8].ToString();

                            scanReply.subnet[0] = data[09];
                            scanReply.subnet[1] = data[10];
                            scanReply.subnet[2] = data[11];

                            scanReply.subnetStr = data[9].ToString() + "." + data[10].ToString() + "." + data[11].ToString();

                            scanReply.isNewData = true;
                            scanReply.isNewGPS = true;
                        }
                    }

                    if (isUDPMonitorOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + endPointUDP.ToString() + "\t" + " < " + data[3].ToString() + "\r\n");
                    }

                } // end of pgns

                else if (data[0] == 36 && (data[1] == 71 || data[1] == 80 || data[1] == 75))
                {
                    traffic.cntrGPSOut += data.Length;
                    rawBuffer += Encoding.ASCII.GetString(data);
                    ParseNMEA(ref rawBuffer);

                    if (isUDPMonitorOn && isGPSLogOn)
                    {
                        logUDPSentence.Append(DateTime.Now.ToString("ss.fff\t") + System.Text.Encoding.ASCII.GetString(data));
                    }
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}
