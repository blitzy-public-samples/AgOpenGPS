// [XPLAT] migrated from net48/WinForms Forms/SerialComm.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
//Please, if you use this, share the improvements
using System;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using AgLibrary.Logging;
using Avalonia.Threading;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Plain, injectable serial-transport service extracted 1:1 (behaviour frozen) from the
    /// former WinForms <c>FormLoop</c> partial class (<c>SourceCode/AgIO/Source/Forms/SerialComm.Designer.cs</c>).
    /// Owns the six serial ports of the comm hub (GPS, GPS2, RTCM, IMU, steer module, machine module),
    /// relays AgOpenGPS PGN frames out to the serial steer/machine/IMU modules, parses the inbound PGN
    /// frames returned by those modules and forwards them to AgOpenGPS over the loopback, and feeds raw
    /// NMEA from the serial GPS receiver into the <see cref="NmeaService"/>.
    /// </summary>
    /// <remarks>
    /// The serial protocol is preserved byte-for-byte (AAP R2): the inbound PGN state machine
    /// (header <c>0x80 0x81</c>, source-address window 121-127, length byte, additive checksum over
    /// bytes <c>[2..length)</c>) is unchanged, as are the per-module forward targets. <c>System.IO.Ports</c>
    /// is cross-platform (Windows <c>COMx</c>, Linux <c>/dev/ttyUSB*</c>/<c>/dev/ttyACM*</c>,
    /// macOS <c>/dev/cu.*</c>); only port-NAME discovery is surfaced through
    /// <see cref="GetAvailablePortNames"/>. The WinForms direct UI mutation (status labels,
    /// <c>MessageBox.Show</c>) is replaced by the <see cref="PortStatusChanged"/> event and event-log
    /// entries, and the WinForms <c>BeginInvoke</c> marshalling is replaced by <see cref="Dispatcher"/>.
    /// </remarks>
    public sealed class SerialCommService
    {
        // [XPLAT] Peer services wired AFTER construction by the composition root (cycle break).
        internal UdpLoopbackService Udp { get; set; }
        internal NmeaService Nmea { get; set; }

        //B5,62,7F,PGN_ID,Length
        private const int totalHeaderByteCount = 5;

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

        //serial port rtcm is connected to
        public SerialPort spRtcm = new SerialPort(portNameRtcm, baudRateRtcm, Parity.None, 8, StopBits.One);

        //serial port IMU is connected to
        public SerialPort spIMU = new SerialPort(portNameIMU, baudRateIMU, Parity.None, 8, StopBits.One);

        //serial port steer module is connected to
        public SerialPort spSteerModule = new SerialPort(portNameSteerModule, baudRateSteerModule, Parity.None, 8, StopBits.One);

        //serial port machine module is connected to
        public SerialPort spMachineModule = new SerialPort(portNameMachineModule, baudRateMachineModule, Parity.None, 8, StopBits.One);

        //lists for parsing incoming bytes
        private readonly byte[] pgnSteerModule = new byte[22];
        private readonly byte[] pgnMachineModule = new byte[22];
        private readonly byte[] pgnIMU = new byte[22];

        /// <summary>
        /// [XPLAT] Raised when a serial port opens, closes or errors. Replaces the WinForms direct
        /// status-label mutation (<c>lblGPS1Comm</c>/<c>lblMod1Comm</c>/<c>lblIMUComm</c>) and the
        /// <c>MessageBox.Show</c> error popups, so the Avalonia shell can bind to it instead.
        /// </summary>
        public event EventHandler<SerialPortStatusEventArgs> PortStatusChanged;

        private void RaiseStatus(string role, string status) =>
            PortStatusChanged?.Invoke(this, new SerialPortStatusEventArgs(role, status));

        /// <summary>
        /// [XPLAT] Cross-platform serial port-name enumeration. <c>System.IO.Ports.SerialPort.GetPortNames()</c>
        /// returns <c>COMx</c> on Windows and <c>/dev/tty*</c> / <c>/dev/cu.*</c> on Linux/macOS, so this is
        /// the single port-discovery seam the config UI uses (the WinForms <c>COMx</c>-only enumeration is
        /// the only part of the serial stack that needed abstracting).
        /// </summary>
        public static string[] GetAvailablePortNames() => SerialPort.GetPortNames();

        /// <summary>
        /// [XPLAT] Shared inbound PGN byte state machine, identical for the IMU, steer and machine
        /// module ports (the three WinForms <c>sp_DataReceived*</c> handlers were byte-identical). Reads
        /// the available bytes, frames a PGN (header 0x80 0x81, source 121-127, length, additive CRC over
        /// bytes [2..length)) into <paramref name="byteList"/>, and on a valid checksum marshals the
        /// completed frame to <paramref name="onComplete"/> on the UI thread. Behaviour frozen.
        /// </summary>
        private void ProcessModuleBytes(SerialPort sp, byte[] byteList, Action<byte[]> onComplete)
        {
            if (!sp.IsOpen)
            {
                return;
            }

            try
            {
                if (sp.BytesToRead > 100)
                {
                    sp.DiscardInBuffer();
                    return;
                }

                int aas = sp.BytesToRead;

                for (int i = 0; i < aas; i++)
                {
                    byte a = (byte)sp.ReadByte();

                    switch (byteList[21])
                    {
                        case 0: //find 0x80
                            {
                                if (a == 128) byteList[byteList[21]++] = a;
                                else byteList[21] = 0;
                                break;
                            }

                        case 1: //find 0x81
                            {
                                if (a == 129) byteList[byteList[21]++] = a;
                                else
                                {
                                    if (a == 181)
                                    {
                                        byteList[21] = 0;
                                        byteList[byteList[21]++] = a;
                                    }
                                    else byteList[21] = 0;
                                }
                                break;
                            }

                        case 2: //Source Address (7F)
                            {
                                if (a < 128 && a > 120)
                                    byteList[byteList[21]++] = a;
                                else byteList[21] = 0;
                                break;
                            }

                        case 3: //PGN ID
                            {
                                byteList[byteList[21]++] = a;
                                break;
                            }

                        case 4: //Num of data bytes
                            {
                                byteList[byteList[21]++] = a;
                                break;
                            }

                        default: //Data load and Checksum
                            {
                                if (byteList[21] > 4)
                                {
                                    int length = byteList[4] + totalHeaderByteCount;
                                    if ((byteList[21]) < length)
                                    {
                                        byteList[byteList[21]++] = a;
                                        break;
                                    }
                                    else
                                    {
                                        //crc
                                        int CK_A = 0;
                                        for (int j = 2; j < length; j++)
                                        {
                                            CK_A += byteList[j];
                                        }

                                        //if checksum matches finish and update main thread
                                        if (a == (byte)(CK_A))
                                        {
                                            length++;
                                            byteList[byteList[21]++] = (byte)CK_A;
                                            byte[] frame = byteList.Take(length).ToArray();
                                            Dispatcher.UIThread.Post(() => onComplete(frame));
                                        }

                                        //clear out the current pgn
                                        byteList[21] = 0;
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
                byteList[21] = 0;
            }
        }

        #region IMU SerialPort //--------------------------------------------------------------------
        private void ReceiveIMUPort(byte[] Data)
        {
            Udp?.SendToLoopBackMessageAOG(Data);
            if (Udp != null) Udp.traffic.helloFromIMU = 0;
        }

        //Send IMU info out to IMU board
        public void SendIMUPort(byte[] items, int numItems)
        {
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

        //open the IMU serial port
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
                Log.EventWriter("No Arduino Port, IMU Port Exc: " + ex.Message);
                RaiseStatus("IMU", "Error: " + ex.Message);

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
                RaiseStatus("IMU", portNameIMU);
            }
        }

        //close the IMU port
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
                    Log.EventWriter("Closing IMU Serial Port" + e.Message);
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
            RaiseStatus("IMU", "---");
        }

        private void sp_DataReceivedIMU(object sender, SerialDataReceivedEventArgs e)
        {
            ProcessModuleBytes(spIMU, pgnIMU, ReceiveIMUPort);
        }
        #endregion ----------------------------------------------------------------

        #region SteerModule SerialPort //--------------------------------------------------------------------
        private void ReceiveSteerModulePort(byte[] Data)
        {
            Udp?.SendToLoopBackMessageAOG(Data);
            if (Udp != null) Udp.traffic.helloFromAutoSteer = 0;
        }

        //Send steer info out to steer board
        public void SendSteerModulePort(byte[] items, int numItems)
        {
            if (spSteerModule.IsOpen)
            {
                try
                {
                    spSteerModule.Write(items, 0, numItems);
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Catch - > Serial Steer module disconnect: " + ex.Message);
                    CloseSteerModulePort();
                }
            }
        }

        //open the steer module serial port
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
                Log.EventWriter("Opening Steer Module Port" + e.Message);
                RaiseStatus("Steer", "Error: " + e.Message);

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
                RaiseStatus("Steer", portNameSteerModule);
            }
        }

        //close the steer module port
        public void CloseSteerModulePort()
        {
            if (spSteerModule.IsOpen)
            {
                spSteerModule.DataReceived -= sp_DataReceivedSteerModule;
                try { spSteerModule.Close(); }
                catch (Exception e)
                {
                    Log.EventWriter("Closing Steer Serial Port" + e.Message);
                }

                Properties.Settings.Default.setPort_wasSteerModuleConnected = false;
                Properties.Settings.Default.Save();

                spSteerModule.Dispose();
            }

            wasSteerModuleConnectedLastRun = false;
            RaiseStatus("Steer", "---");
        }

        private void sp_DataReceivedSteerModule(object sender, SerialDataReceivedEventArgs e)
        {
            ProcessModuleBytes(spSteerModule, pgnSteerModule, ReceiveSteerModulePort);
        }
        #endregion ----------------------------------------------------------------

        #region MachineModule SerialPort // Machine Port ------------------------------------------------

        private void ReceiveMachineModulePort(byte[] Data)
        {
            try
            {
                Udp?.SendToLoopBackMessageAOG(Data);
                if (Udp != null) Udp.traffic.helloFromMachine = 0;
            }
            catch (Exception e)
            {
                Log.EventWriter("Machine Module Send Exc: " + e.Message);
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
                    Log.EventWriter("Catch - > Serial Machine module disconnect: " + ex.Message);
                    CloseMachineModulePort();
                }
            }
        }

        //open the machine module serial port
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
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception e)
            {
                Log.EventWriter("Opening Machine Module Port" + e.Message);
                RaiseStatus("Machine", "Error: " + e.Message);

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
                RaiseStatus("Machine", portNameMachineModule);
            }
        }

        //close the machine module port
        public void CloseMachineModulePort()
        {
            if (spMachineModule.IsOpen)
            {
                spMachineModule.DataReceived -= sp_DataReceivedMachineModule;
                try { spMachineModule.Close(); }
                catch (Exception e)
                {
                    Log.EventWriter("Closing Machine Serial Port" + e.Message);
                }

                Properties.Settings.Default.setPort_wasMachineModuleConnected = false;
                Properties.Settings.Default.Save();

                spMachineModule.Dispose();
            }

            wasMachineModuleConnectedLastRun = false;
            RaiseStatus("Machine", "---");
        }

        private void sp_DataReceivedMachineModule(object sender, SerialDataReceivedEventArgs e)
        {
            ProcessModuleBytes(spMachineModule, pgnMachineModule, ReceiveMachineModulePort);
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
                Log.EventWriter("Opening RTCM Port: " + e.Message);
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
                Log.EventWriter("Catch - > Serial GPS Open Fail: " + ex.Message);
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
                RaiseStatus("GPS", portNameGPS);
                wasGPSConnectedLastRun = true;
            }
        }

        public void CloseGPSPort()
        {
            try { spGPS.Close(); }
            catch (Exception e)
            {
                Log.EventWriter("Closing GPS Port" + e.Message);
            }

            spGPS.Dispose();
            RaiseStatus("GPS", "---");
            wasGPSConnectedLastRun = false;
        }

        //called by the GPS delegate every time a chunk is rec'd
        private void ReceiveGPSPort(string sentence)
        {
            if (Nmea != null)
            {
                Nmea.rawBuffer += sentence;
                Nmea.ParseNMEA(ref Nmea.rawBuffer);
            }

            if (Udp != null) Udp.traffic.cntrGPSOut += sentence.Length;
            if (isGPSCommOpen) recvGPSSentence = sentence;
        }

        //serial port receive in its own thread
        private void sp_DataReceivedGPS(object sender, SerialDataReceivedEventArgs e)
        {
            if (spGPS.IsOpen)
            {
                try
                {
                    string sentence = spGPS.ReadExisting();
                    Dispatcher.UIThread.Post(() => ReceiveGPSPort(sentence));
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
                Log.EventWriter("Catch - > Serial GPS2 Open Fail: " + ex.Message);
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
                Log.EventWriter("Closing GPS2 Port" + e.Message);
            }

            spGPS2.Dispose();
        }

        //serial port receive in its own thread
        private void sp_DataReceivedGPS2(object sender, SerialDataReceivedEventArgs e)
        {
            if (spGPS2.IsOpen)
            {
                try
                {
                    string sentence = spGPS2.ReadLine();
                    Dispatcher.UIThread.Post(() => ReceiveGPS2Port(sentence));
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
                Log.EventWriter("Catch - > Serial RTCM Open Fail: " + ex.Message);
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
                Log.EventWriter("Closing RTCM Port" + e.Message);
            }

            wasRtcmConnectedLastRun = false;
        }
        #endregion //--------------------------------------------------------

        /// <summary>Closes every open serial port (shutdown / restart teardown).</summary>
        public void CloseAll()
        {
            CloseGPSPort();
            CloseGPS2Port();
            CloseRtcmPort();
            CloseIMUPort();
            CloseSteerModulePort();
            CloseMachineModulePort();
        }
    }

    /// <summary>
    /// [XPLAT] Status payload raised by <see cref="SerialCommService.PortStatusChanged"/>, replacing the
    /// WinForms direct status-label mutation and error <c>MessageBox</c>es.
    /// </summary>
    public sealed class SerialPortStatusEventArgs : EventArgs
    {
        public SerialPortStatusEventArgs(string role, string status)
        {
            Role = role;
            Status = status;
        }

        /// <summary>Logical port role: GPS, IMU, Steer or Machine.</summary>
        public string Role { get; }

        /// <summary>Port name when connected, "---" when closed, or an error description.</summary>
        public string Status { get; }
    }
}
