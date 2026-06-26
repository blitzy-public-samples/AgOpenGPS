// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//Please, if you use this, share the improvements

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Platform;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Plain, injectable serial-port communication service extracted 1:1 (behaviour frozen,
    /// byte-for-byte) from the former WinForms <c>FormLoop</c> partial class
    /// (<c>SourceCode/AgIO/Source/Forms/SerialComm.Designer.cs</c>). Owns the six serial ports of the
    /// AgIO comm hub — GPS, GPS2, RTCM, IMU, steer module and machine module — relaying AgOpenGPS PGN
    /// frames out to the serial steer/machine/IMU modules, parsing the inbound PGN frames the modules
    /// return and forwarding them to AgOpenGPS over the loopback, and feeding raw NMEA from the serial
    /// GPS receiver into the <see cref="NmeaService"/>.
    /// </summary>
    /// <remarks>
    /// The serial byte contract is preserved exactly (AAP §0.2.2 / §0.7.1): baud rates, parity
    /// (<c>Parity.None, 8, StopBits.One</c>), the <c>Thread.Sleep</c> module-boot delays, the 22-byte
    /// PGN parse state machines (header <c>0x80 0x81</c>, source-address window 121-127, length byte,
    /// additive checksum over bytes <c>[2..length)</c>) and the <c>0x7C</c>-header IMU-disconnect frame
    /// are all verbatim. <c>System.IO.Ports</c> is kept because it is cross-platform (Windows
    /// <c>COMx</c>, Linux <c>/dev/ttyUSB*</c>/<c>/dev/ttyACM*</c>, macOS <c>/dev/cu.*</c>); the only
    /// abstracted seam is port-<i>name</i> discovery, surfaced through <see cref="GetAvailablePortNames"/>
    /// which routes to <see cref="IPlatformServices.GetSerialPortNames"/>.
    /// </remarks>
    /// <remarks>
    /// The migration transforms versus the WinForms original are limited to: the <c>FormLoop</c>
    /// god-object back-references become the after-construction-wired <see cref="Nmea"/> and
    /// <see cref="Udp"/> peer properties; the WinForms <c>BeginInvoke</c> UI-thread marshal in the receive
    /// callbacks becomes a <b>direct call</b> on the serial-callback thread (no UI, preserving timing with
    /// no added latency — AAP §0.6.1); and the <c>MessageBox.Show</c> popups are routed through the
    /// optional <see cref="IErrorPresenter"/> while the status-label mutations are dropped (they become
    /// Avalonia view-model bindings). Everything on the serial byte path is unchanged.
    /// </remarks>
    public sealed class SerialCommService : IDisposable
    {
        // [XPLAT] Port-name enumeration seam only (COMx vs /dev/ttyUSB*); the serial byte I/O stays on the
        // cross-platform System.IO.Ports package and is never routed through this abstraction.
        private readonly IPlatformServices _platform;

        // [XPLAT] Optional presenter that replaces the WinForms MessageBox.Show error popups. Null-guarded
        // at every call site with ?.; when null the Log.EventWriter entries preserve the diagnostics.
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] Guards Dispose() so closing the six ports on shutdown is idempotent.
        private bool _disposed;

        /// <summary>
        /// [XPLAT] Peer reference to the NMEA parse service. Wired AFTER construction by the composition
        /// root to break the construction cycle; the GPS receive path uses it to append raw bytes and
        /// drain the parser.
        /// </summary>
        internal NmeaService Nmea { get; set; }

        /// <summary>
        /// [XPLAT] Peer reference to the UDP loopback transport. Wired AFTER construction by the
        /// composition root to break the construction cycle; module frames are forwarded to AgOpenGPS
        /// through it and the byte counters live on its <c>traffic</c>. Null-guarded with ?. on the
        /// forward call so a briefly-unwired hub never throws.
        /// </summary>
        internal UdpLoopbackService Udp { get; set; }

        /// <summary>
        /// [XPLAT] Creates the serial service with the cross-platform port-name provider and an optional
        /// error presenter (replacing the WinForms <c>MessageBox.Show</c>). The <see cref="Nmea"/> and
        /// <see cref="Udp"/> peers are wired separately after construction.
        /// </summary>
        /// <param name="platform">Cross-platform services used for serial port-name enumeration.</param>
        /// <param name="errorPresenter">Optional presenter for surfacing connection errors; may be null.</param>
        public SerialCommService(IPlatformServices platform, IErrorPresenter errorPresenter = null)
        {
            _platform = platform;
            _errorPresenter = errorPresenter;
        }

        //B5,62,7F,PGN_ID,Length
        private int totalHeaderByteCount = 5;

        public static string portNameGPS = "***";
        public static int baudRateGPS = 4800;

        public static string portNameGPS2 = "***";
        public static int baudRateGPS2 = 4800;

        public static string portNameRtcm = "***";
        public static int baudRateRtcm = 4800;

        public static string portNameIMU = "***";
        public static int baudRateIMU = 38400;

        public static string portNameSteerModule = "***";
        public static int baudRateSteerModule = 38400;

        public static string portNameMachineModule = "***";
        public static int baudRateMachineModule = 38400;

        //used to decide to autoconnect section arduino this run
        public string recvGPSSentence = "GPS";
        public string recvGPS2Sentence = "GPS2";
        public string recvIMUSentence = "IMU";
        public string recvSteerModuleSentence = "Module 1";
        public string recvMachineModuleSentence = "Module 2";

        public bool isGPSCommOpen = false;

        public byte checksumSent = 0;
        public byte checksumRecd = 0;

        //used to decide to autoconnect autosteer arduino this run
        public bool wasGPSConnectedLastRun = false;
        public bool wasMachineModuleConnectedLastRun = false;
        public bool wasSteerModuleConnectedLastRun = false;
        public bool wasIMUConnectedLastRun = false;
        public bool wasRtcmConnectedLastRun = false;

        //serial port gps is connected to
        public SerialPort spGPS = new SerialPort(portNameGPS, baudRateGPS, Parity.None, 8, StopBits.One);

        //serial port gps2 is connected to
        public SerialPort spGPS2 = new SerialPort(portNameGPS2, baudRateGPS2, Parity.None, 8, StopBits.One);

        //serial port gps is connected to
        public SerialPort spRtcm = new SerialPort(portNameRtcm, baudRateRtcm, Parity.None, 8, StopBits.One);

        //serial port Arduino is connected to
        public SerialPort spIMU = new SerialPort(portNameIMU, baudRateIMU, Parity.None, 8, StopBits.One);

        //serial port Arduino is connected to
        public SerialPort spSteerModule = new SerialPort(portNameSteerModule, baudRateSteerModule, Parity.None, 8, StopBits.One);

        //serial port Arduino is connected to
        public SerialPort spMachineModule = new SerialPort(portNameMachineModule, baudRateMachineModule, Parity.None, 8, StopBits.One);

        //lists for parsing incoming bytes
        private byte[] pgnSteerModule = new byte[22];
        private byte[] pgnMachineModule = new byte[22];
        private byte[] pgnIMU = new byte[22];

        /// <summary>
        /// [XPLAT] Cross-platform serial port-name enumeration routed through
        /// <see cref="IPlatformServices.GetSerialPortNames"/> (the abstracted seam) so the config UI gets
        /// the correct per-OS names — <c>COMx</c> on Windows, <c>/dev/ttyUSB*</c>/<c>/dev/ttyACM*</c> on
        /// Linux, <c>/dev/cu.*</c> on macOS — instead of the Windows-leaning <c>SerialPort.GetPortNames()</c>.
        /// Returns an empty sequence when no platform provider has been wired so the UI degrades gracefully
        /// rather than throwing.
        /// </summary>
        public IEnumerable<string> GetAvailablePortNames() =>
            _platform?.GetSerialPortNames() ?? Array.Empty<string>();

        #region IMUSerialPort //--------------------------------------------------------------------
        private void ReceiveIMUPort(byte[] Data)
        {
            // [XPLAT] The Udp peer is wired AFTER construction (composition root, to break the
            // construction cycle), and this runs on the serial DataReceived callback thread — which can
            // fire before wiring or during teardown. Snapshot the peer once (so the null check and the
            // dereference see the same instance) and drop the frame safely if it or its traffic counter
            // is not yet wired. Mirrors the WinForms path, which only relayed once FormLoop had wired it.
            var udp = Udp;
            if (udp?.traffic == null)
            {
                return;
            }

            udp.SendToLoopBackMessageAOG(Data);
            udp.traffic.helloFromIMU = 0;
        }

        //Send machine info out to machine board
        public void SendIMUPort(byte[] items, int numItems)
        {
            //Tell Arduino to turn section on or off accordingly
            if (spIMU.IsOpen)
            {
                try
                {
                    spIMU.Write(items, 0, numItems);
                }
                catch (Exception)
                {
                    CloseIMUPort();
                }
            }
        }

        //open the Arduino serial port
        public void OpenIMUPort()
        {
            if (!spIMU.IsOpen)
            {
                spIMU.PortName = portNameIMU;
                spIMU.BaudRate = baudRateIMU;
                spIMU.DataReceived += sp_DataReceivedIMU;
                spIMU.DtrEnable = true;
                spIMU.RtsEnable = true;
            }

            try { spIMU.Open(); }
            catch (Exception ex)
            {
                Log.EventWriter("No Arduino Port, IMU Port Exc: " + ex.ToString());

                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), "No Arduino Port Active",
                    ex.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix");

                Properties.Settings.Default.setPort_wasIMUConnected = false;
                Properties.Settings.Default.Save();
                wasIMUConnectedLastRun = false;
            }

            if (spIMU.IsOpen)
            {
                //short delay for the use of mega2560, it is working in debugmode with breakpoint
                System.Threading.Thread.Sleep(500); // 500 was not enough

                spIMU.DiscardOutBuffer();
                spIMU.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameIMU = portNameIMU;
                Properties.Settings.Default.setPort_wasIMUConnected = true;
                Properties.Settings.Default.Save();
                wasIMUConnectedLastRun = true;
            }
        }

        //close the machine port
        public void CloseIMUPort()
        {
            if (spIMU.IsOpen)
            {
                spIMU.DataReceived -= sp_DataReceivedIMU;
                try
                {
                    spIMU.Close();
                    byte[] imuClose = new byte[] { 0x80, 0x81, 0x7C, 0xD4, 2, 1, 0, 0xCC };

                    //tell AOG IMU is disconnected
                    Udp?.SendToLoopBackMessageAOG(imuClose);
                }
                catch (Exception e)
                {
                    Log.EventWriter("Closing Machine Serial Port" + e.ToString());
                    _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                        "Connection already terminated??", e.Message);
                }

                Properties.Settings.Default.setPort_wasIMUConnected = false;
                Properties.Settings.Default.Save();

                spIMU.Dispose();
                wasIMUConnectedLastRun = false;
            }
            else
            {
                byte[] imuClose = new byte[] { 0x80, 0x81, 0x7C, 0xD4, 2, 1, 0, 0xCC };

                //tell AOG IMU is disconnected
                Udp?.SendToLoopBackMessageAOG(imuClose);
                wasIMUConnectedLastRun = false;
            }

            wasIMUConnectedLastRun = false;
        }

        private void sp_DataReceivedIMU(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spIMU.IsOpen)
            {
                byte[] ByteList;
                ByteList = pgnIMU;

                try
                {
                    if (spIMU.BytesToRead > 100)
                    {
                        spIMU.DiscardInBuffer();
                        return;
                    }

                    byte a;

                    int aas = spIMU.BytesToRead;

                    for (int i = 0; i < aas; i++)
                    {
                        a = (byte)spIMU.ReadByte();

                        switch (ByteList[21])
                        {
                            case 0: //find 0x80
                                {
                                    if (a == 128) ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 1: //find 0x81
                                {
                                    if (a == 129) ByteList[ByteList[21]++] = a;
                                    else
                                    {
                                        if (a == 181)
                                        {
                                            ByteList[21] = 0;
                                            ByteList[ByteList[21]++] = a;
                                        }
                                        else ByteList[21] = 0;
                                    }
                                    break;
                                }

                            case 2: //Source Address (7F)
                                {
                                    if (a < 128 && a > 120)
                                        ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 3: //PGN ID
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            case 4: //Num of data bytes
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            default: //Data load and Checksum
                                {
                                    if (ByteList[21] > 4)
                                    {
                                        int length = ByteList[4] + totalHeaderByteCount;
                                        if ((ByteList[21]) < length)
                                        {
                                            ByteList[ByteList[21]++] = a;
                                            break;
                                        }
                                        else
                                        {
                                            //crc
                                            int CK_A = 0;
                                            for (int j = 2; j < length; j++)
                                            {
                                                CK_A = CK_A + ByteList[j];
                                            }

                                            //if checksum matches finish and update main thread
                                            if (a == (byte)(CK_A))
                                            {
                                                length++;
                                                ByteList[ByteList[21]++] = (byte)CK_A;
                                                ReceiveIMUPort(ByteList.Take(length).ToArray());
                                            }

                                            //clear out the current pgn
                                            ByteList[21] = 0;
                                            return;
                                        }
                                    }

                                    break;
                                }
                        }
                    }
                }
                catch
                {
                    ByteList[21] = 0;
                }
            }
        }
        #endregion ----------------------------------------------------------------

        #region SteerModuleSerialPort //--------------------------------------------------------------------
        private void ReceiveSteerModulePort(byte[] Data)
        {
            // [XPLAT] Snapshot the late-wired Udp peer once and drop the frame safely if it (or its
            // traffic counter) is not yet wired — the serial callback thread can fire before the
            // composition root wires Udp or during teardown. See ReceiveIMUPort for the full rationale.
            var udp = Udp;
            if (udp?.traffic == null)
            {
                return;
            }

            udp.SendToLoopBackMessageAOG(Data);
            udp.traffic.helloFromAutoSteer = 0;
        }

        //Send machine info out to machine board
        public void SendSteerModulePort(byte[] items, int numItems)
        {
            //Tell Arduino to turn section on or off accordingly
            if (spSteerModule.IsOpen)
            {
                try
                {
                    spSteerModule.Write(items, 0, numItems);
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Catch - > Serial Steer module disconnect: " + ex.ToString());
                    CloseSteerModulePort();
                }
            }
        }

        //open the Arduino serial port
        public void OpenSteerModulePort()
        {
            if (!spSteerModule.IsOpen)
            {
                spSteerModule.PortName = portNameSteerModule;
                spSteerModule.BaudRate = baudRateSteerModule;
                spSteerModule.DataReceived += sp_DataReceivedSteerModule;
                spSteerModule.DtrEnable = true;
                spSteerModule.RtsEnable = true;
            }

            try
            {
                spSteerModule.Open();
                //short delay for the use of mega2560, it is working in debugmode with breakpoint
                System.Threading.Thread.Sleep(1000); // 500 was not enough
            }
            catch (Exception e)
            {
                Log.EventWriter("Opening Machine Port" + e.ToString());

                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), "No Arduino Port Active",
                    e.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix");

                Properties.Settings.Default.setPort_wasSteerModuleConnected = false;
                Properties.Settings.Default.Save();
            }

            if (spSteerModule.IsOpen)
            {
                spSteerModule.DiscardOutBuffer();
                spSteerModule.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameSteer = portNameSteerModule;
                Properties.Settings.Default.setPort_wasSteerModuleConnected = true;
                Properties.Settings.Default.Save();

                wasSteerModuleConnectedLastRun = true;
            }
        }

        //close the machine port
        public void CloseSteerModulePort()
        {
            if (spSteerModule.IsOpen)
            {
                spSteerModule.DataReceived -= sp_DataReceivedSteerModule;
                try { spSteerModule.Close(); }
                catch (Exception e)
                {
                    Log.EventWriter("Closing Machine Serial Port" + e.ToString());
                    _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                        "Connection already terminated??", e.Message);
                }

                Properties.Settings.Default.setPort_wasSteerModuleConnected = false;
                Properties.Settings.Default.Save();

                spSteerModule.Dispose();
            }

            wasSteerModuleConnectedLastRun = false;
        }

        private void sp_DataReceivedSteerModule(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spSteerModule.IsOpen)
            {
                byte[] ByteList;
                ByteList = pgnSteerModule;

                try
                {
                    if (spSteerModule.BytesToRead > 100)
                    {
                        spSteerModule.DiscardInBuffer();
                        return;
                    }

                    byte a;

                    int aas = spSteerModule.BytesToRead;

                    for (int i = 0; i < aas; i++)
                    {
                        a = (byte)spSteerModule.ReadByte();

                        switch (ByteList[21])
                        {
                            case 0: //find 0x80
                                {
                                    if (a == 128) ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 1: //find 0x81
                                {
                                    if (a == 129) ByteList[ByteList[21]++] = a;
                                    else
                                    {
                                        if (a == 181)
                                        {
                                            ByteList[21] = 0;
                                            ByteList[ByteList[21]++] = a;
                                        }
                                        else ByteList[21] = 0;
                                    }
                                    break;
                                }

                            case 2: //Source Address (7F)
                                {
                                    if (a < 128 && a > 120)
                                        ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 3: //PGN ID
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            case 4: //Num of data bytes
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            default: //Data load and Checksum
                                {
                                    if (ByteList[21] > 4)
                                    {
                                        int length = ByteList[4] + totalHeaderByteCount;
                                        if ((ByteList[21]) < length)
                                        {
                                            ByteList[ByteList[21]++] = a;
                                            break;
                                        }
                                        else
                                        {
                                            //crc
                                            int CK_A = 0;
                                            for (int j = 2; j < length; j++)
                                            {
                                                CK_A = CK_A + ByteList[j];
                                            }

                                            //if checksum matches finish and update main thread
                                            if (a == (byte)(CK_A))
                                            {
                                                length++;
                                                ByteList[ByteList[21]++] = (byte)CK_A;
                                                ReceiveSteerModulePort(ByteList.Take(length).ToArray());
                                            }

                                            //clear out the current pgn
                                            ByteList[21] = 0;
                                            return;
                                        }
                                    }

                                    break;
                                }
                        }
                    }
                }
                catch (Exception)
                {
                    ByteList[21] = 0;
                }
            }
        }
        #endregion ----------------------------------------------------------------

        #region MachineModuleSerialPort // Machine Port ------------------------------------------------

        private void ReceiveMachineModulePort(byte[] Data)
        {
            try
            {
                // [XPLAT] Snapshot the late-wired Udp peer once and drop the frame safely if it (or its
                // traffic counter) is not yet wired. The enclosing try/catch is retained for the serial
                // byte path, but the guard means an unwired peer no longer raises a logged NRE every tick.
                var udp = Udp;
                if (udp?.traffic == null)
                {
                    return;
                }

                udp.SendToLoopBackMessageAOG(Data);
                udp.traffic.helloFromMachine = 0;
            }
            catch (Exception e)
            {
                Log.EventWriter("Machine Module Send Exc: " + e.ToString());
            }
        }

        //Send machine info out to machine board
        public void SendMachineModulePort(byte[] items, int numItems)
        {
            if (spMachineModule.IsOpen)
            {
                try
                {
                    spMachineModule.Write(items, 0, numItems);
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Catch - > Serial Machine module disconnect: " + ex.ToString());
                    CloseMachineModulePort();
                }
            }
        }

        //open the Arduino serial port
        public void OpenMachineModulePort()
        {
            if (!spMachineModule.IsOpen)
            {
                spMachineModule.PortName = portNameMachineModule;
                spMachineModule.BaudRate = baudRateMachineModule;
                spMachineModule.DataReceived += sp_DataReceivedMachineModule;
                spMachineModule.DtrEnable = true;
                spMachineModule.RtsEnable = true;
            }

            try
            {
                spMachineModule.Open();
                //short delay for the use of mega2560, it is working in debugmode with breakpoint
                System.Threading.Thread.Sleep(1000); // 500 was not enough
            }
            catch (Exception e)
            {
                Log.EventWriter("Opening Machine Port: " + e.ToString());

                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), "No Arduino Port Active",
                    e.Message + "\n\r" + "\n\r" + "Go to Settings -> COM Ports to Fix");

                Properties.Settings.Default.setPort_wasMachineModuleConnected = false;
                Properties.Settings.Default.Save();
            }

            if (spMachineModule.IsOpen)
            {
                spMachineModule.DiscardOutBuffer();
                spMachineModule.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameMachine = portNameMachineModule;
                Properties.Settings.Default.setPort_wasMachineModuleConnected = true;
                Properties.Settings.Default.Save();

                wasMachineModuleConnectedLastRun = true;
            }
        }

        //close the machine port
        public void CloseMachineModulePort()
        {
            if (spMachineModule.IsOpen)
            {
                spMachineModule.DataReceived -= sp_DataReceivedMachineModule;
                try { spMachineModule.Close(); }
                catch (Exception e)
                {
                    Log.EventWriter("Closing Machine Serial Port: " + e.ToString());
                    _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                        "Connection already terminated??", e.Message);
                }

                Properties.Settings.Default.setPort_wasMachineModuleConnected = false;
                Properties.Settings.Default.Save();

                spMachineModule.Dispose();
            }

            wasMachineModuleConnectedLastRun = false;
        }

        private void sp_DataReceivedMachineModule(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spMachineModule.IsOpen)
            {
                byte[] ByteList;
                ByteList = pgnMachineModule;

                try
                {
                    if (spMachineModule.BytesToRead > 100)
                    {
                        spMachineModule.DiscardInBuffer();
                        return;
                    }

                    byte a;

                    int aas = spMachineModule.BytesToRead;

                    for (int i = 0; i < aas; i++)
                    {
                        a = (byte)spMachineModule.ReadByte();

                        switch (ByteList[21])
                        {
                            case 0: //find 0x80
                                {
                                    if (a == 128) ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 1: //find 0x81
                                {
                                    if (a == 129) ByteList[ByteList[21]++] = a;
                                    else
                                    {
                                        if (a == 181)
                                        {
                                            ByteList[21] = 0;
                                            ByteList[ByteList[21]++] = a;
                                        }
                                        else ByteList[21] = 0;
                                    }
                                    break;
                                }

                            case 2: //Source Address (7F)
                                {
                                    if (a < 128 && a > 120)
                                        ByteList[ByteList[21]++] = a;
                                    else ByteList[21] = 0;
                                    break;
                                }

                            case 3: //PGN ID
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            case 4: //Num of data bytes
                                {
                                    ByteList[ByteList[21]++] = a;
                                    break;
                                }

                            default: //Data load and Checksum
                                {
                                    if (ByteList[21] > 4)
                                    {
                                        int length = ByteList[4] + totalHeaderByteCount;
                                        if ((ByteList[21]) < length)
                                        {
                                            ByteList[ByteList[21]++] = a;
                                            break;
                                        }
                                        else
                                        {
                                            //crc
                                            int CK_A = 0;
                                            for (int j = 2; j < length; j++)
                                            {
                                                CK_A = CK_A + ByteList[j];
                                            }

                                            //if checksum matches finish and update main thread
                                            if (a == (byte)(CK_A))
                                            {
                                                ByteList[ByteList[21]++] = (byte)CK_A;
                                                length++;
                                                ReceiveMachineModulePort(ByteList.Take(length).ToArray());
                                            }

                                            //clear out the current pgn
                                            ByteList[21] = 0;
                                            return;
                                        }
                                    }

                                    break;
                                }
                        }
                    }
                }
                catch (Exception)
                {
                    ByteList[21] = 0;
                }
            }
        }
        #endregion --------------------------------------------------------------------

        #region GPS SerialPort --------------------------------------------------------------------------

        public void SendGPSPort(byte[] data)
        {
            try
            {
                if (spRtcm.IsOpen)
                {
                    spRtcm.Write(data, 0, data.Length);
                }
                else if (spGPS.IsOpen)
                {
                    spGPS.Write(data, 0, data.Length);
                }
            }
            catch (Exception e)
            {
                Log.EventWriter("Opening RTCM Port: " + e.ToString());
            }
        }

        public void OpenGPSPort()
        {
            if (spGPS.IsOpen)
            {
                //close it first
                CloseGPSPort();
            }

            if (!spGPS.IsOpen)
            {
                spGPS.PortName = portNameGPS;
                spGPS.BaudRate = baudRateGPS;
                spGPS.DataReceived += sp_DataReceivedGPS;
                spGPS.WriteTimeout = 1000;
            }

            try { spGPS.Open(); }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Serial GPS Open Fail: " + ex.ToString());
            }

            if (spGPS.IsOpen)
            {
                //discard any stuff in the buffers
                spGPS.DiscardOutBuffer();
                spGPS.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameGPS = portNameGPS;
                Properties.Settings.Default.setPort_baudRateGPS = baudRateGPS;
                Properties.Settings.Default.setPort_wasGPSConnected = true;
                Properties.Settings.Default.Save();
                wasGPSConnectedLastRun = true;
            }
        }

        public void CloseGPSPort()
        {
            try { spGPS.Close(); }
            catch (Exception e)
            {
                Log.EventWriter("Closing GPS Port" + e.ToString());
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                    "Connection already terminated?", e.Message);
            }

            spGPS.Dispose();
            wasGPSConnectedLastRun = false;
        }

        //called by the GPS delegate every time a chunk is rec'd
        private void ReceiveGPSPort(string sentence)
        {
            // [XPLAT] This GPS receive path dereferences BOTH late-wired peers — Nmea (raw-buffer append
            // + parse) and Udp.traffic (out-byte counter). Both are wired after construction and this runs
            // on the serial callback thread, so snapshot each once and drop the sentence safely if either
            // is not yet wired or has been torn down (the caller sp_DataReceivedGPS swallows exceptions,
            // so an unguarded NRE here would silently lose the GPS stream). Snapshotting also keeps the
            // ref-parse operating on the same Nmea instance the guard validated.
            var nmea = Nmea;
            var udp = Udp;
            if (nmea == null || udp?.traffic == null)
            {
                return;
            }

            nmea.rawBuffer += sentence;
            nmea.ParseNMEA(ref nmea.rawBuffer);

            udp.traffic.cntrGPSOut += sentence.Length;
            if (isGPSCommOpen) recvGPSSentence = sentence;
        }

        //serial port receive in its own thread
        private void sp_DataReceivedGPS(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spGPS.IsOpen)
            {
                try
                {
                    string sentence = spGPS.ReadExisting();
                    ReceiveGPSPort(sentence);
                }
                catch (Exception)
                {
                }
            }
        }
        #endregion SerialPortGPS

        #region GPS2 SerialPort //--------------------------------------------------------------------------

        //called by the GPS2 delegate every time a chunk is rec'd
        private void ReceiveGPS2Port(string sentence)
        {
            recvGPS2Sentence = sentence;
        }

        public void SendGPS2Port(byte[] data)
        {
            try
            {
                if (spGPS2.IsOpen)
                {
                    spGPS2.Write(data, 0, data.Length);
                }
            }
            catch (Exception)
            {
            }
        }

        public void OpenGPS2Port()
        {
            //close it first
            CloseGPS2Port();

            if (!spGPS2.IsOpen)
            {
                spGPS2.PortName = portNameGPS2;
                spGPS2.BaudRate = baudRateGPS2;
                spGPS2.DataReceived += sp_DataReceivedGPS2;
                spGPS2.WriteTimeout = 1000;
            }

            try { spGPS2.Open(); }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Serial GPS Open Fail: " + ex.ToString());
            }

            if (spGPS2.IsOpen)
            {
                //discard any stuff in the buffers
                spGPS2.DiscardOutBuffer();
                spGPS2.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameGPS2 = portNameGPS2;
                Properties.Settings.Default.setPort_baudRateGPS2 = baudRateGPS2;
                Properties.Settings.Default.Save();
            }
        }

        public void CloseGPS2Port()
        {
            spGPS2.DataReceived -= sp_DataReceivedGPS2;
            try { spGPS2.Close(); }
            catch (Exception e)
            {
                Log.EventWriter("Closing GPS2 Port" + e.ToString());
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                    "Connection already terminated?", e.Message);
            }

            spGPS2.Dispose();
        }

        //serial port receive in its own thread
        private void sp_DataReceivedGPS2(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            if (spGPS2.IsOpen)
            {
                try
                {
                    string sentence = spGPS2.ReadLine();
                    ReceiveGPS2Port(sentence);
                }
                catch (Exception)
                {
                }
            }
        }
        #endregion //--------------------------------------------------------

        #region RTCM SerialPort //--------------------------------------------------------------------------

        public void OpenRtcmPort()
        {
            if (spRtcm.IsOpen)
            {
                //close it first
                CloseRtcmPort();
            }

            if (!spRtcm.IsOpen)
            {
                spRtcm.PortName = portNameRtcm;
                spRtcm.BaudRate = baudRateRtcm;
                spRtcm.WriteTimeout = 1000;
            }

            try { spRtcm.Open(); }
            catch (Exception ex)
            {
                Log.EventWriter("Catch - > Serial RTCM Open Fail: " + ex.ToString());
            }

            if (spRtcm.IsOpen)
            {
                //discard any stuff in the buffers
                spRtcm.DiscardOutBuffer();
                spRtcm.DiscardInBuffer();

                Properties.Settings.Default.setPort_portNameRtcm = portNameRtcm;
                Properties.Settings.Default.setPort_baudRateRtcm = baudRateRtcm;
                Properties.Settings.Default.setPort_wasRtcmConnected = true;
                Properties.Settings.Default.Save();
                wasRtcmConnectedLastRun = true;
            }
        }

        public void CloseRtcmPort()
        {
            try { spRtcm.Close(); }
            catch (Exception e)
            {
                Log.EventWriter("Closing RTCM Port" + e.ToString());
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2),
                    "Connection already terminated?", e.Message);
            }

            wasRtcmConnectedLastRun = false;
        }
        #endregion //--------------------------------------------------------

        /// <summary>
        /// [XPLAT] Releases the six serial-port handles on shutdown/restart. Best-effort and idempotent:
        /// each port is unsubscribed, closed and disposed inside its own try/catch so a single faulted
        /// COM/tty handle never blocks the rest of the teardown. Unlike the per-port <c>Close*Port</c>
        /// methods this is a lean resource release — it deliberately does not persist settings, so the
        /// connection flags the shell saves just before shutdown survive for next-run auto-reconnect.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            DisposePort(spGPS, sp_DataReceivedGPS);
            DisposePort(spGPS2, sp_DataReceivedGPS2);
            DisposePort(spRtcm, null);
            DisposePort(spIMU, sp_DataReceivedIMU);
            DisposePort(spSteerModule, sp_DataReceivedSteerModule);
            DisposePort(spMachineModule, sp_DataReceivedMachineModule);
        }

        // [XPLAT] Best-effort teardown of a single serial port: unsubscribe the receive handler (if any),
        // close and dispose, swallowing any exception so the remaining ports still get released.
        private static void DisposePort(SerialPort port, SerialDataReceivedEventHandler handler)
        {
            if (port == null)
            {
                return;
            }

            try
            {
                if (handler != null)
                {
                    port.DataReceived -= handler;
                }

                if (port.IsOpen)
                {
                    port.Close();
                }

                port.Dispose();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Serial port dispose error: " + ex.ToString());
            }
        }
    }
}
