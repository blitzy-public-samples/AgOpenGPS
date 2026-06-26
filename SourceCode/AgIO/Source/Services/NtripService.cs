// [XPLAT] migrated from net48/WinForms Forms/NTRIPComm.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
//Please, if you use this, share the improvements
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Plain, injectable NTRIP / radio / serial-pass correction-source service extracted 1:1
    /// (behaviour frozen) from the former WinForms <c>FormLoop</c> partial class
    /// (<c>SourceCode/AgIO/Source/Forms/NTRIPComm.Designer.cs</c>). Connects to an NTRIP caster over TCP
    /// (or a radio / serial-pass source), authorizes, receives the RTCM correction stream, and forwards
    /// it to the GPS receiver (serial) and/or to AgOpenGPS over the UDP fabric.
    /// </summary>
    /// <remarks>
    /// The correction transport, authorization handshake, GGA construction and NTRIP metering are
    /// preserved exactly (AAP §0.2.2 / §0.7.1): NMEA/GGA numeric formatting uses
    /// <see cref="CultureInfo.InvariantCulture"/> throughout and the NTRIP UDP forward still targets the
    /// shared <c>epNtrip</c> endpoint. The only structural transforms versus the WinForms original are:
    /// (a) the two WinForms <c>System.Windows.Forms.Timer</c> instances (the GGA send timer and the
    /// 50&#160;ms NTRIP meter) become cross-platform <see cref="System.Timers.Timer"/> instances with the
    /// identical interval/enable semantics — their <c>Elapsed</c> handlers run on a thread-pool thread,
    /// adding no latency to the correction path (AAP §0.6.1); (b) the WinForms <c>BeginInvoke</c> UI-thread
    /// marshalling in the two receive callbacks becomes a <b>direct</b> call to <see cref="OnAddMessage"/>
    /// on the socket/serial-callback thread; (c) the former status-label mutations are surfaced through the
    /// <see cref="StatusChanged"/> event for the Avalonia view-model to bind, and the WinForms
    /// <c>TimedMessageBox</c> is routed through the optional injected <see cref="IErrorPresenter"/>
    /// (and the <see cref="MessageRequested"/> event for shells that bind events instead). Because the
    /// receive callbacks and the meter timer can now touch the shared RTCM queue from different threads
    /// (the WinForms original serialised everything on the UI thread), access to that queue is guarded by
    /// a private lock so the byte stream can never be corrupted — the bytes forwarded are unchanged.
    /// </remarks>
    public sealed class NtripService : IDisposable
    {
        // [XPLAT] Optional error presenter that replaces the WinForms TimedMessageBox (AAP transform).
        // Null when the composition root prefers to bind the MessageRequested event instead; every use is
        // null-conditional so an absent presenter is simply a no-op.
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] Guards the shared RTCM byte queue (rawTrip) against concurrent access now that the
        // socket/serial receive callbacks (OnAddMessage) and the meter timer (ntripMeterTimer_Tick) run on
        // separate thread-pool threads instead of the single WinForms UI thread. Restores the original's
        // race-free serialisation without changing the forwarded bytes.
        private readonly object _tripLock = new object();

        // [XPLAT] Idempotency guard for IDisposable and a fast-exit for timer Elapsed handlers that may
        // race with shutdown.
        private bool _disposed;
        // [XPLAT] Peer services wired AFTER construction by the composition root (cycle break).
        internal UdpLoopbackService Udp { get; set; }
        internal SerialCommService Serial { get; set; }
        internal NmeaService Nmea { get; set; }

        //for the NTRIP CLient counting
        private int ntripCounter = 10;

        private Socket clientSocket;                      // Server connection
        private readonly byte[] casterRecBuffer = new byte[2800];    // Recieved data buffer

        //Send GGA back timer ([XPLAT] System.Windows.Forms.Timer -> cross-platform System.Timers.Timer)
        private Timer tmr;

        //NTRIP metering timer (50 ms) ([XPLAT] System.Windows.Forms.Timer -> cross-platform System.Timers.Timer)
        private readonly Timer ntripMeterTimer;

        private string mount;
        private string username;
        private string password;

        public string broadCasterIP;
        private int broadCasterPort;

        private int sendGGAInterval = 0;

        public uint tripBytes = 0;
        private int toUDP_Port = 0;
        private int NTRIP_Watchdog = 100;

        public bool isNTRIP_RequiredOn = false;
        public bool isNTRIP_Connected = false;
        public bool isNTRIP_Starting = false;
        public bool isNTRIP_Connecting = false;
        public bool isNTRIP_Sending = false;
        public bool isRunGGAInterval = false;

        public bool isRadio_RequiredOn = false;
        public bool isSerialPass_RequiredOn = false;
        internal SerialPort spRadio = new SerialPort("Radio", 9600, Parity.None, 8, StopBits.One);

        // [XPLAT] Forwarding routing flags (formerly FormLoop fields). Defaults match FormLoop.cs.
        public bool isSendToSerial = true;
        public bool isSendToUDP = false;

        // [XPLAT] Advanced-view + focus-skip gating (formerly FormLoop fields). The shell can toggle these.
        public bool isViewAdvanced = false;
        public int focusSkipCounter = 310;
        public int packetSizeNTRIP;

        private readonly List<int> rList = new List<int>();
        private readonly List<int> aList = new List<int>();

        //NTRIP metering
        private readonly Queue<byte> rawTrip = new Queue<byte>();

        /// <summary>
        /// [XPLAT] Raised with the human-readable NTRIP status strings (watch state, byte counters, mount,
        /// caster IP, bytes-to-GPS) that the WinForms code wrote directly onto status labels.
        /// </summary>
        public event EventHandler<NtripStatusEventArgs> StatusChanged;

        /// <summary>
        /// [XPLAT] Raised in place of the WinForms <c>TimedMessageBox</c> so the Avalonia shell can show a
        /// transient message (timeout in ms, title, body).
        /// </summary>
        public event EventHandler<NtripMessageEventArgs> MessageRequested;

        public NtripService(IErrorPresenter errorPresenter = null)
        {
            _errorPresenter = errorPresenter;

            // [XPLAT] 50 ms NTRIP meter timer (was FormLoop.Designer.cs ntripMeterTimer, Interval = 50).
            // System.Timers.Timer repeats (AutoReset = true) like the original WinForms timer; its Elapsed
            // handler drains the RTCM queue on a thread-pool thread (queue access is lock-guarded).
            ntripMeterTimer = new Timer(50) { AutoReset = true };
            ntripMeterTimer.Elapsed += ntripMeterTimer_Tick;
        }

        // [XPLAT] Replaces the WinForms TimedMessageBox: prefer the injected IErrorPresenter, and also raise
        // the MessageRequested event for shells that bind events instead. _errorPresenter is null-conditional,
        // so a composition root that wires only the event sees a single notification (no double display).
        private void RaiseMessage(int timeout, string title, string message)
        {
            _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(timeout), title, message);
            MessageRequested?.Invoke(this, new NtripMessageEventArgs(timeout, title, message));
        }

        //set up connection to Caster
        public void DoNTRIPSecondRoutine()
        {
            //count up the ntrip clock only if everything is alive
            if (isNTRIP_RequiredOn || isRadio_RequiredOn || isSerialPass_RequiredOn)
            {
                IncrementNTRIPWatchDog();
            }

            //Have we NTRIP connection
            if (isNTRIP_RequiredOn && !isNTRIP_Connected && !isNTRIP_Connecting)
            {
                if (!isNTRIP_Starting && ntripCounter > 20)
                {
                    StartNTRIP();
                }
            }

            if ((isRadio_RequiredOn || isSerialPass_RequiredOn) && !isNTRIP_Connected && !isNTRIP_Connecting)
            {
                if (!isNTRIP_Starting)
                {
                    StartNTRIP();
                }
            }

            if (isNTRIP_Connecting)
            {
                if (ntripCounter > 29)
                {
                    RaiseMessage(1500, "Connection Problem", "Not Connecting To Caster");
                    ReconnectRequest();
                }
                if (clientSocket != null && clientSocket.Connected)
                {
                    SendAuthorization();
                }
            }

            // [XPLAT] Status strings that were written to lblNTRIPBytes / btnStartStopNtrip / lblWatch are
            // now surfaced via the StatusChanged event. The one behavioural side effect in this block
            // (clearing isNTRIP_Sending after a GGA send) is preserved.
            if (isNTRIP_RequiredOn || isRadio_RequiredOn)
            {
                string bytesText = ((tripBytes >> 10)).ToString("###,###,### kb", CultureInfo.InvariantCulture);
                string countdown;
                if (ntripCounter > 59) countdown = (ntripCounter >> 6) + " Min";
                else if (ntripCounter < 60 && ntripCounter > 25) countdown = ntripCounter + " Secs";
                else countdown = "In " + (Math.Abs(ntripCounter - 25)) + " secs";

                string watch;
                if (isNTRIP_Connecting)
                {
                    watch = "Authourizing";
                }
                else
                {
                    if (isNTRIP_RequiredOn && NTRIP_Watchdog > 10)
                    {
                        watch = "Waiting";
                    }
                    else
                    {
                        watch = "Listening";
                        if (isNTRIP_RequiredOn) watch += " NTRIP";
                        else if (isRadio_RequiredOn) watch += " Radio";
                    }
                }

                if (sendGGAInterval > 0 && isNTRIP_Sending)
                {
                    watch = "Send GGA";
                    isNTRIP_Sending = false;
                }

                RaiseStatus(watch: watch, bytes: bytesText, countdown: countdown);
            }
            else if (isSerialPass_RequiredOn)
            {
                string bytesText = ((tripBytes >> 10)).ToString("###,###,### kb", CultureInfo.InvariantCulture);
                string countdown;
                if (ntripCounter > 59) countdown = (ntripCounter >> 6) + " Min";
                else if (ntripCounter < 60 && ntripCounter > 22) countdown = ntripCounter + " Secs";
                else countdown = "In " + (Math.Abs(ntripCounter - 22)) + " secs";

                RaiseStatus(bytes: bytesText, countdown: countdown);
            }
        }

        public void ConfigureNTRIP()
        {
            aList.Clear();
            rList.Clear();

            //start NTRIP if required
            isNTRIP_RequiredOn = Properties.Settings.Default.setNTRIP_isOn;
            isRadio_RequiredOn = Properties.Settings.Default.setRadio_isOn;
            isSerialPass_RequiredOn = Properties.Settings.Default.setPass_isOn;

            if (isRadio_RequiredOn || isSerialPass_RequiredOn)
            {
                // Immediatly connect radio
                ntripCounter = 20;
            }

            RaiseStatus(watch: "Wait GPS", visible: (isNTRIP_RequiredOn || isRadio_RequiredOn || isSerialPass_RequiredOn));

            //update Caster IP from URL, just use the old one if can't find
            if (isNTRIP_RequiredOn)
            {
                ResolveCasterIP();
            }
        }

        /// <summary>
        /// [XPLAT] Resolves the NTRIP caster hostname (<c>setNTRIP_casterURL</c>) to an IPv4 address,
        /// persisting it to <c>setNTRIP_casterIP</c>. Extracted verbatim (behaviour frozen) from the former
        /// WinForms <c>FormLoop</c> Load handler so the caster IP is resolved before
        /// <see cref="StartNTRIP"/> runs. On failure it falls back to the last-known stored IP and requests
        /// a reconnect, exactly as the original did.
        /// </summary>
        public void ResolveCasterIP()
        {
            //broadCasterIP = Properties.Settings.Default.setNTRIP_casterIP; //Select correct Address
            broadCasterIP = null;
            string actualIP = Properties.Settings.Default.setNTRIP_casterURL.Trim();

            try
            {
                IPAddress[] addresslist = Dns.GetHostAddresses(actualIP);
                foreach (IPAddress address in addresslist)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        broadCasterIP = address.ToString().Trim();
                        Properties.Settings.Default.setNTRIP_casterIP = broadCasterIP;
                        Properties.Settings.Default.Save();
                        break;
                    }
                }

                if (broadCasterIP == null) throw new NullReferenceException();
            }
            catch (Exception ex)
            {
                Log.EventWriter(ex.ToString());
                RaiseMessage(1500, "URL Not Located, Network Down?", "Cannot Find: " + Properties.Settings.Default.setNTRIP_casterURL);
                //if we had a timer already, kill it
                tmr?.Stop();

                //use last known
                broadCasterIP = Properties.Settings.Default.setNTRIP_casterIP; //Select correct Address

                // Close the socket if it is still open
                if (clientSocket != null && clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                    System.Threading.Thread.Sleep(100);
                    clientSocket.Close();
                }

                //TimedMessageBox(2000, "NTRIP Not Connected", " Reconnect Request");
                ntripCounter = 15;
                isNTRIP_Connected = false;
                isNTRIP_Starting = false;
                isNTRIP_Connecting = false;
            }
        }

        public void StartNTRIP()
        {
            if (isNTRIP_RequiredOn)
            {
                // [XPLAT] broadCasterIP is resolved from the caster URL in ResolveCasterIP() (formerly in
                // the FormLoop Load handler) and is already populated by ConfigureNTRIP() at this point.
                broadCasterPort = Properties.Settings.Default.setNTRIP_casterPort; //Select correct port (usually 80 or 2101)
                mount = Properties.Settings.Default.setNTRIP_mount; //Insert the correct mount
                username = Properties.Settings.Default.setNTRIP_userName; //Insert your username!
                password = Properties.Settings.Default.setNTRIP_userPassword; //Insert your password!
                toUDP_Port = Properties.Settings.Default.setNTRIP_sendToUDPPort; //send rtcm to which udp port
                sendGGAInterval = Properties.Settings.Default.setNTRIP_sendGGAInterval; //how often to send fixes
                packetSizeNTRIP = Properties.Settings.Default.setNTRIP_packetSize;

                //if we had a timer already, kill it ([XPLAT] Dispose + null the old System.Timers.Timer
                //before recreating so the underlying timer is released and the later null-checks are correct)
                tmr?.Dispose();
                tmr = null;

                //create new timer at fast rate to start
                if (sendGGAInterval > 0)
                {
                    tmr = new Timer(5000) { AutoReset = true };
                    tmr.Elapsed += NTRIPtick;
                }

                try
                {
                    // Close the socket if it is still open
                    if (clientSocket != null && clientSocket.Connected)
                    {
                        clientSocket.Shutdown(SocketShutdown.Both);
                        System.Threading.Thread.Sleep(100);
                        clientSocket.Close();
                    }

                    //NTRIP endpoint
                    if (Udp != null)
                    {
                        Udp.epNtrip = new IPEndPoint(IPAddress.Parse(
                            Properties.Settings.Default.etIP_SubnetOne.ToString(CultureInfo.InvariantCulture) + "." +
                            Properties.Settings.Default.etIP_SubnetTwo.ToString(CultureInfo.InvariantCulture) + "." +
                            Properties.Settings.Default.etIP_SubnetThree.ToString(CultureInfo.InvariantCulture) + ".255"), toUDP_Port);
                    }

                    // Create the socket object
                    clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true,
                        // Connect to server non-Blocking method
                        Blocking = false
                    };
                    clientSocket.BeginConnect(new IPEndPoint(IPAddress.Parse(broadCasterIP), broadCasterPort), new AsyncCallback(OnConnect), null);

                    Log.EventWriter("NTRIP - IP: " + broadCasterIP.ToString() + ":" + broadCasterPort.ToString()
                        + " To Port: " + toUDP_Port.ToString() + " Mount: " + mount);
                }
                catch (Exception ex)
                {
                    ReconnectRequest();
                    Log.EventWriter("Catch - > NTRIP Reconnect Request: " + ex.ToString());

                    return;
                }

                isNTRIP_Connecting = true;
                RaiseStatus(ip: broadCasterIP, mount: mount);
            }
            else if (isRadio_RequiredOn)
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.setPort_portNameRadio))
                {
                    // Disconnect when already connected
                    if (spRadio != null)
                    {
                        spRadio.Close();
                        spRadio.Dispose();
                    }

                    // Setup and open serial port
                    spRadio = new SerialPort(Properties.Settings.Default.setPort_portNameRadio)
                    {
                        BaudRate = int.Parse(Properties.Settings.Default.setPort_baudRateRadio, CultureInfo.InvariantCulture)
                    };
                    spRadio.DataReceived += NtripPort_DataReceived;
                    isNTRIP_Connecting = false;
                    isNTRIP_Connected = true;

                    try
                    {
                        spRadio.Open();
                    }
                    catch (Exception ex)
                    {
                        isNTRIP_Connecting = false;
                        isNTRIP_Connected = false;
                        isRadio_RequiredOn = false;
                        Log.EventWriter("Catch - > Error connecting to radio" + ex.ToString());

                        RaiseMessage(2000, "Error connecting to radio", $"{ex.Message}");
                    }
                }
            }
            else if (isSerialPass_RequiredOn)
            {
                toUDP_Port = Properties.Settings.Default.setNTRIP_sendToUDPPort; //send rtcm to which udp port
                if (Udp != null)
                {
                    Udp.epNtrip = new IPEndPoint(IPAddress.Parse(
                        Properties.Settings.Default.etIP_SubnetOne.ToString(CultureInfo.InvariantCulture) + "." +
                        Properties.Settings.Default.etIP_SubnetTwo.ToString(CultureInfo.InvariantCulture) + "." +
                        Properties.Settings.Default.etIP_SubnetThree.ToString(CultureInfo.InvariantCulture) + ".255"), toUDP_Port);
                }

                if (!string.IsNullOrEmpty(Properties.Settings.Default.setPort_portNameRadio))
                {
                    // Disconnect when already connected
                    if (spRadio != null)
                    {
                        spRadio.Close();
                        spRadio.Dispose();
                    }

                    // Setup and open serial port
                    spRadio = new SerialPort(Properties.Settings.Default.setPort_portNameRadio)
                    {
                        BaudRate = int.Parse(Properties.Settings.Default.setPort_baudRateRadio, CultureInfo.InvariantCulture)
                    };
                    spRadio.DataReceived += NtripPort_DataReceived;
                    isNTRIP_Connecting = false;
                    isNTRIP_Connected = true;
                    RaiseStatus(watch: "RTCM Serial");

                    try
                    {
                        spRadio.Open();
                    }
                    catch (Exception ex)
                    {
                        isNTRIP_Connecting = false;
                        isNTRIP_Connected = false;
                        isSerialPass_RequiredOn = false;
                        Log.EventWriter("Catch - > Serial Pass Radio: " + ex.ToString());

                        RaiseMessage(2000, "Error connecting to Serial Pass", $"{ex.Message}");
                    }
                }
            }
        }

        private void ReconnectRequest()
        {
            ntripCounter = 15;
            isNTRIP_Connected = false;
            isNTRIP_Starting = false;
            isNTRIP_Connecting = false;

            //if we had a timer already, kill it
            tmr?.Stop();
        }

        private void IncrementNTRIPWatchDog()
        {
            //increment once every second
            ntripCounter++;

            //Thinks is connected but not receiving anything
            if (NTRIP_Watchdog++ > 30 && isNTRIP_Connected)
                ReconnectRequest();

            //Once all connected set the timer GGA to NTRIP Settings ([XPLAT] System.Timers.Timer.Interval is
            //milliseconds as a double — same numeric value as the original WinForms Timer.Interval (int ms))
            if (sendGGAInterval > 0 && ntripCounter == 40 && tmr != null) tmr.Interval = sendGGAInterval * 1000;
        }

        private void SendAuthorization()
        {
            // Check we are connected
            if (clientSocket == null || !clientSocket.Connected)
            {
                ReconnectRequest();
                return;
            }

            // Read the message from settings and send it
            try
            {
                if (!Properties.Settings.Default.setNTRIP_isTCP)
                {
                    //encode user and password
                    string auth = ToBase64(username + ":" + password);

                    //grab location sentence ([XPLAT] BuildGGA still populates sbGGA exactly as before; the
                    //write-only GGASentence capture was removed as dead code so the build stays analyzer-clean
                    //under Release TreatWarningsAsErrors — the position line is not appended to the request)
                    BuildGGA();

                    string htt;
                    if (Properties.Settings.Default.setNTRIP_isHTTP10) htt = "1.0";
                    else htt = "1.1";

                    //Build authorization string
                    string str = "GET /" + mount + " HTTP/" + htt + "\r\n";
                    str += "User-Agent: NTRIP AgOpenGPSClient/6.4\r\n";
                    str += "Authorization: Basic " + auth + "\r\n"; //This line can be removed if no authorization is needed
                                                                    //str += sbGGA.ToString(); //this line can be removed if no position feedback is needed
                    str += "Accept: */*\r\nConnection: close\r\n";
                    str += "\r\n";

                    // Convert to byte array and send.
                    Byte[] byteDateLine = Encoding.ASCII.GetBytes(str.ToCharArray());
                    clientSocket.Send(byteDateLine, byteDateLine.Length, 0);

                    //enable to periodically send GGA sentence to server.
                    if (sendGGAInterval > 0) tmr?.Start();
                }
                //say its connected
                isNTRIP_Connected = true;
                isNTRIP_Starting = false;
                isNTRIP_Connecting = false;
            }
            catch (Exception ex)
            {
                ReconnectRequest();
                Log.EventWriter("Catch - > NTRIP Send Authourization: " + ex.ToString());
            }
        }

        public void OnAddMessage(byte[] data)
        {
            // [XPLAT] Called directly on the socket/serial receive-callback thread (the WinForms BeginInvoke
            // UI-thread marshal was removed — no UI here). Bail out if the service was disposed mid-flight.
            if (_disposed) return;

            //update gui with stats
            tripBytes += (uint)data.Length;

            if (isViewAdvanced && isNTRIP_RequiredOn)
            {
                int mess = 0;

                try
                {
                    for (int i = 0; i < data.Length - 5; i++)
                    {
                        if (data[i] == 211 && (data[i + 1] >> 2) == 0)
                        {
                            mess = ((data[i + 3] << 4) + (data[i + 4] >> 4));
                            if (mess > 1000 && mess < 1231)
                            {
                                rList.Add(mess);
                                i += (data[i + 1] << 6) + (data[i + 2]) + 5;
                                if (data[i + 1] != 211)
                                {
                                    //rList.Clear();
                                    //break;
                                }
                            }
                            else
                            {
                                rList.Clear();
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    //MessageBox.Show("Error");
                }
            }

            //reset watchdog since we have updated data
            NTRIP_Watchdog = 0;

            if (isNTRIP_RequiredOn)
            {
                //move the ntrip stream to queue ([XPLAT] lock-guarded — the meter timer now drains rawTrip
                //on a separate thread-pool thread, where the WinForms original serialised on the UI thread)
                lock (_tripLock)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        rawTrip.Enqueue(data[i]);
                    }
                }

                ntripMeterTimer.Start();
            }
            else
            {
                RaiseStatus(toGPS: data.Length.ToString(CultureInfo.InvariantCulture));
                //send it
                SendNTRIP(data);
            }
        }

        private void ntripMeterTimer_Tick(object sender, ElapsedEventArgs e)
        {
            // [XPLAT] Elapsed fires on a thread-pool thread (System.Timers.Timer); bail out if the service
            // was disposed during shutdown so a late tick cannot touch a closed socket/queue.
            if (_disposed) return;

            byte[] trip;
            bool drained;

            // [XPLAT] All rawTrip access is taken under the lock (enqueue happens on the receive thread):
            // the dequeue, the "done?" check and the overflow clear are atomic so the byte stream can never
            // be corrupted. The socket/serial send runs OUTSIDE the lock so I/O is never held under it.
            lock (_tripLock)
            {
                //we really should get here, but have to check
                if (rawTrip.Count == 0) return;

                //how many bytes in the Queue
                int cnt = rawTrip.Count;

                //how many sends have occured
                if (Udp != null) Udp.traffic.cntrGPSIn++;

                //128 bytes chunks max
                if (cnt > packetSizeNTRIP) cnt = packetSizeNTRIP;

                //new data array to send
                trip = new byte[cnt];

                if (Udp != null) Udp.traffic.cntrGPSInBytes += cnt;

                //dequeue into the array
                for (int i = 0; i < cnt; i++) trip[i] = rawTrip.Dequeue();

                //Are we done?
                drained = rawTrip.Count == 0;

                //Can't keep up as internet dumped a shit load so clear
                if (rawTrip.Count > 10000) rawTrip.Clear();
            }

            //send it
            SendNTRIP(trip);

            //Are we done?
            if (drained)
            {
                ntripMeterTimer.Stop();

                if (focusSkipCounter != 0 && Udp != null)
                {
                    RaiseStatus(toGPS: Udp.traffic.cntrGPSInBytes == 0 ? "---" : (Udp.traffic.cntrGPSInBytes).ToString(CultureInfo.InvariantCulture));
                    Udp.traffic.cntrGPSInBytes = 0;
                }
            }
        }

        public void SendNTRIP(byte[] data)
        {
            //serial send out GPS port
            if (isSendToSerial)
            {
                Serial?.SendGPSPort(data);
            }

            //send out UDP Port
            if (isSendToUDP && Udp != null)
            {
                Udp.SendUDPMessage(data, Udp.epNtrip);
            }
        }

        public void SendGGA()
        {
            //timer may have brought us here so return if not connected
            if (!isNTRIP_Connected)
                return;
            // Check we are connected
            if (clientSocket == null || !clientSocket.Connected)
            {
                ReconnectRequest();
                return;
            }

            // Read the message from the text box and send it
            try
            {
                isNTRIP_Sending = true;
                BuildGGA();
                string str = sbGGA.ToString();

                Byte[] byteDateLine = Encoding.ASCII.GetBytes(str.ToCharArray());
                clientSocket.Send(byteDateLine, byteDateLine.Length, 0);
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Send GGA" + ex.ToString());

                ReconnectRequest();
            }
        }

        private void NTRIPtick(object sender, ElapsedEventArgs e)
        {
            // [XPLAT] Elapsed fires on a thread-pool thread (System.Timers.Timer); skip if disposed.
            if (_disposed) return;
            SendGGA();
        }

        public void OnConnect(IAsyncResult ar)
        {
            // Check if we were sucessfull
            try
            {
                if (clientSocket.Connected)
                    clientSocket.BeginReceive(casterRecBuffer, 0, casterRecBuffer.Length, SocketFlags.None, new AsyncCallback(OnRecievedData), null);
            }
            catch (Exception)
            {
                //MessageBox.Show(ex.Message, "Unusual error during Connect!");
            }
        }

        public void OnRecievedData(IAsyncResult ar)
        {
            // Check if we got any data
            try
            {
                int nBytesRec = clientSocket.EndReceive(ar);
                if (nBytesRec > 0)
                {
                    byte[] localMsg = new byte[nBytesRec];
                    Array.Copy(casterRecBuffer, localMsg, nBytesRec);

                    // [XPLAT] WinForms BeginInvoke UI-thread marshal removed — process on the socket-callback
                    // thread directly (no UI; preserves real-time correction throughput with no added latency).
                    OnAddMessage(localMsg);
                    clientSocket.BeginReceive(casterRecBuffer, 0, casterRecBuffer.Length, SocketFlags.None, new AsyncCallback(OnRecievedData), null);
                }
                else
                {
                    // If no data was recieved then the connection is probably dead
                    Console.WriteLine("Client {0}, disconnected", clientSocket.RemoteEndPoint);
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
            }
            catch (Exception)
            {
                //MessageBox.Show( this, ex.Message, "Unusual error druing Recieve!" );
            }
        }

        private void NtripPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Check if we got any data
            try
            {
                SerialPort comport = (SerialPort)sender;
                if (comport.BytesToRead < 32)
                    return;

                int nBytesRec = comport.BytesToRead;

                if (nBytesRec > 0)
                {
                    byte[] localMsg = new byte[nBytesRec];
                    comport.Read(localMsg, 0, nBytesRec);

                    // [XPLAT] WinForms BeginInvoke UI-thread marshal removed — process on the serial
                    // DataReceived callback thread directly (no UI; no added latency on the correction path).
                    OnAddMessage(localMsg);
                }
                else
                {
                    // If no data was recieved then the connection is probably dead
                    // TODO: What can we do?
                }
            }
            catch (Exception)
            {
                //MessageBox.Show( this, ex.Message, "Unusual error druing Recieve!" );
            }
        }

        private string ToBase64(string str)
        {
            Encoding asciiEncoding = Encoding.ASCII;
            byte[] byteArray = new byte[asciiEncoding.GetByteCount(str)];
            byteArray = asciiEncoding.GetBytes(str);
            return Convert.ToBase64String(byteArray, 0, byteArray.Length);
        }

        public void ShutDownNTRIP()
        {
            if (clientSocket != null && clientSocket.Connected)
            {
                //shut it down
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                System.Threading.Thread.Sleep(500);

                //start it up again
                ReconnectRequest();

                //Also stop the requests now
                isNTRIP_RequiredOn = false;
            }
            else if (spRadio != null)
            {
                spRadio.Close();
                spRadio.Dispose();
                spRadio = null;

                ReconnectRequest();

                //Also stop the requests now
                isRadio_RequiredOn = false;
            }
        }

        public void SettingsShutDownNTRIP()
        {
            if (clientSocket != null && clientSocket.Connected)
            {
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                System.Threading.Thread.Sleep(500);
                ReconnectRequest();
            }

            if (spRadio != null && spRadio.IsOpen)
            {
                spRadio.Close();
                spRadio.Dispose();
                spRadio = null;
                ReconnectRequest();
            }
        }

        //calculate the NMEA checksum to stuff at the end
        public string CalculateChecksum(string Sentence)
        {
            int sum = 0, inx;
            char[] sentence_chars = Sentence.ToCharArray();
            char tmp;

            // All character xor:ed results in the trailing hex checksum
            // The checksum calc starts after '$' and ends before '*'
            for (inx = 1; ; inx++)
            {
                tmp = sentence_chars[inx];

                // Indicates end of data and start of checksum
                if (tmp == '*')
                    break;
                sum ^= tmp;    // Build checksum
            }

            // Calculated checksum converted to a 2 digit hex string
            return String.Format(CultureInfo.InvariantCulture, "{0:X2}", sum);
        }

        private readonly StringBuilder sbGGA = new StringBuilder();

        private void BuildGGA()
        {
            double latitude = 0;
            double longitude = 0;

            if (Properties.Settings.Default.setNTRIP_isGGAManual)
            {
                latitude = Properties.Settings.Default.setNTRIP_manualLat;
                longitude = Properties.Settings.Default.setNTRIP_manualLon;
            }
            else
            {
                latitude = Nmea?.latitude ?? 0;
                longitude = Nmea?.longitude ?? 0;
            }

            //convert to DMS from Degrees
            double latMinu = latitude;
            double longMinu = longitude;

            double latDeg = (int)latitude;
            double longDeg = (int)longitude;

            latMinu -= latDeg;
            longMinu -= longDeg;

            latMinu = Math.Round(latMinu * 60.0, 7);
            longMinu = Math.Round(longMinu * 60.0, 7);

            latDeg *= 100.0;
            longDeg *= 100.0;

            double latNMEA = latMinu + latDeg;
            double longNMEA = longMinu + longDeg;

            char NS = 'W';
            char EW = 'N';
            if (latitude >= 0) NS = 'N';
            else NS = 'S';
            if (longitude >= 0) EW = 'E';
            else EW = 'W';

            byte fixQualityData = Nmea?.fixQualityData ?? 0;
            ushort satellitesData = Nmea?.satellitesData ?? 0;
            float hdopData = Nmea?.hdopData ?? 0;
            float altitudeData = Nmea?.altitudeData ?? 0;
            float ageData = Nmea?.ageData ?? 0;

            sbGGA.Clear();
            sbGGA.Append("$GPGGA,");
            sbGGA.Append(DateTime.Now.ToString("HHmmss.00,", CultureInfo.InvariantCulture));
            sbGGA.Append(Math.Abs(latNMEA).ToString("0000.000", CultureInfo.InvariantCulture)).Append(',').Append(NS).Append(',');
            sbGGA.Append(Math.Abs(longNMEA).ToString("00000.000", CultureInfo.InvariantCulture)).Append(',').Append(EW);
            sbGGA.Append(',').Append(fixQualityData.ToString(CultureInfo.InvariantCulture)).Append(',');
            sbGGA.Append(satellitesData.ToString(CultureInfo.InvariantCulture)).Append(',');

            if (hdopData > 0) sbGGA.Append(hdopData.ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            else sbGGA.Append("1,");

            sbGGA.Append(altitudeData.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sbGGA.Append("M,");
            sbGGA.Append("46.4,M,");  //udulation
            sbGGA.Append(ageData.ToString("0.#", CultureInfo.InvariantCulture)).Append(','); //age
            sbGGA.Append("0*");

            sbGGA.Append(CalculateChecksum(sbGGA.ToString()));
            sbGGA.Append("\r\n");
            /*
        $GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,5,0*47
           0     1      2      3    4      5 6  7  8   9    10 11  12 13  14
                Time      Lat       Lon     FixSatsOP Alt */
        }

        private void RaiseStatus(string watch = null, string bytes = null, string countdown = null,
            string ip = null, string mount = null, string toGPS = null, bool? visible = null)
        {
            StatusChanged?.Invoke(this, new NtripStatusEventArgs(watch, bytes, countdown, ip, mount, toGPS, visible));
        }

        /// <summary>
        /// [XPLAT] Releases the TCP caster socket, the radio / serial-pass <see cref="SerialPort"/> and both
        /// <see cref="System.Timers.Timer"/> instances. Best-effort and idempotent: the re-entrancy guard
        /// makes repeated calls safe and each step is wrapped in try/catch so one failure cannot block the
        /// rest of the teardown. The timers are stopped before the socket is closed so a late Elapsed tick
        /// cannot touch a half-closed connection. The runtime <see cref="ShutDownNTRIP"/> /
        /// <see cref="SettingsShutDownNTRIP"/> stop/restart paths remain available and are unaffected.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // stop the GGA send timer first so NTRIPtick cannot fire mid-teardown
            try
            {
                if (tmr != null)
                {
                    tmr.Stop();
                    tmr.Dispose();
                    tmr = null;
                }
            }
            catch (Exception ex) { Log.EventWriter("Catch - > NTRIP Dispose tmr: " + ex.Message); }

            // stop the NTRIP meter timer
            try
            {
                ntripMeterTimer.Stop();
                ntripMeterTimer.Dispose();
            }
            catch (Exception ex) { Log.EventWriter("Catch - > NTRIP Dispose meter timer: " + ex.Message); }

            // close the caster TCP socket
            try
            {
                if (clientSocket != null)
                {
                    if (clientSocket.Connected) clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                    clientSocket.Dispose();
                    clientSocket = null;
                }
            }
            catch (Exception ex) { Log.EventWriter("Catch - > NTRIP Dispose socket: " + ex.Message); }

            // close the radio / serial-pass port
            try
            {
                if (spRadio != null)
                {
                    spRadio.Close();
                    spRadio.Dispose();
                    spRadio = null;
                }
            }
            catch (Exception ex) { Log.EventWriter("Catch - > NTRIP Dispose radio: " + ex.Message); }
        }
    }

    /// <summary>
    /// [XPLAT] Status payload raised by <see cref="NtripService.StatusChanged"/>. Each property is null
    /// when that particular field was not updated, mirroring the piecemeal label writes in the WinForms code.
    /// </summary>
    public sealed class NtripStatusEventArgs : EventArgs
    {
        public NtripStatusEventArgs(string watch, string bytes, string countdown, string ip, string mount, string toGPS, bool? panelVisible)
        {
            Watch = watch;
            Bytes = bytes;
            Countdown = countdown;
            Ip = ip;
            Mount = mount;
            ToGPS = toGPS;
            PanelVisible = panelVisible;
        }

        public string Watch { get; }
        public string Bytes { get; }
        public string Countdown { get; }
        public string Ip { get; }
        public string Mount { get; }
        public string ToGPS { get; }
        public bool? PanelVisible { get; }
    }

    /// <summary>
    /// [XPLAT] Transient-message payload raised by <see cref="NtripService.MessageRequested"/>, replacing
    /// the WinForms <c>TimedMessageBox</c>.
    /// </summary>
    public sealed class NtripMessageEventArgs : EventArgs
    {
        public NtripMessageEventArgs(int timeoutMs, string title, string message)
        {
            TimeoutMs = timeoutMs;
            Title = title;
            Message = message;
        }

        public int TimeoutMs { get; }
        public string Title { get; }
        public string Message { get; }
    }
}
