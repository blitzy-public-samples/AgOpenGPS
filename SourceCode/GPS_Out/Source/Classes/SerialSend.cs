// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO.Ports;
using Avalonia.Threading;          // [XPLAT] DispatcherTimer (cross-platform UI-thread watchdog)

namespace GPS_Out
{
    public class SerialSend
    {
        // [XPLAT] decoupled from the deleted WinForms host form (the former view back-reference is
        // gone). SerialSend now depends only on the local clsTools helper and the local
        // ISerialStatusSink contract — never on the Avalonia Views layer — so the standalone GPS_Out
        // serial logic stays UI-framework agnostic. The host window injects itself as the
        // ISerialStatusSink implementation (see TRANSITION_MAP.md for the old -> new wiring).
        private readonly clsTools tools;
        private readonly ISerialStatusSink statusSink;
        private bool cWriteTimeOut = false;
        private SerialPort Sport;
        // [XPLAT] the WinForms UI timer is replaced by an Avalonia DispatcherTimer. The original UI
        // timer fired Tick on the UI thread; DispatcherTimer preserves that exact threading so the
        // watchdog can open the Avalonia HelpWindow (tools.ShowHelp) and refresh the port indicator
        // without marshaling. Interval is a TimeSpan; Start()/Stop() replace the Enabled flag. This
        // 1 Hz watchdog is unrelated to the real-time receive path, so no latency is added.
        private readonly DispatcherTimer Timer1 = new DispatcherTimer();
        private int WriteErrorCount;

        // [XPLAT] internal (not public): the ISerialStatusSink parameter type is internal, so a public
        // constructor would raise CS0051 (inconsistent accessibility). Mirrors the same pattern used by
        // clsTools.LoadFormData/SaveFormData. Only the host window (same assembly) constructs this, as
        // new SerialSend(tools, statusSink). The class itself remains public.
        internal SerialSend(clsTools Tools, ISerialStatusSink StatusSink)
        {
            this.tools = Tools;
            this.statusSink = StatusSink;
            Sport = new SerialPort(Properties.Settings.Default.Port, Properties.Settings.Default.Baud);
            Sport.WriteTimeout = 500;
            Sport.Parity = Parity.None;
            Sport.DataBits = 8;
            Sport.StopBits = StopBits.One;
            Timer1.Interval = TimeSpan.FromMilliseconds(1000);
            Timer1.Tick += CheckConnection;

            if (Properties.Settings.Default.AutoConnect && Properties.Settings.Default.SerialSuccessful) Open();
        }

        public int Baud
        {
            get { return Sport.BaudRate; }
            set
            {
                if (!Sport.IsOpen && value > 0 && value < 115201)
                {
                    Sport.BaudRate = value;
                    Properties.Settings.Default.Baud = Sport.BaudRate;
                }
            }
        }

        public string PortNm
        {
            get { return Sport.PortName; }
            set
            {
                if (!Sport.IsOpen && value != "")
                {
                    Sport.PortName = value;
                    Properties.Settings.Default.Port = Sport.PortName;
                }
            }
        }

        public void Close()
        {
            try
            {
                Timer1.Stop();
                if (Sport.IsOpen)
                {
                    Sport.Close();
                    Sport.Dispose();
                }
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog("SerialSend/CloseRCport: " + ex.Message);
            }
        }

        public bool IsOpen()
        {
            return Sport.IsOpen;
        }

        public bool Open()
        {
            bool Result = false;
            try
            {
                if (SerialPortExists(Sport.PortName))
                {
                    if (!Sport.IsOpen) Sport.Open();

                    if (Sport.IsOpen)
                    {
                        Sport.DiscardOutBuffer();
                        WriteErrorCount = 0;
                        Timer1.Start();
                        Result = true;
                    }
                }
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog("SerialSend/OpenRCport: " + ex.Message);
            }
            Properties.Settings.Default.SerialSuccessful = Result;
            return Result;
        }

        public void SendStringData(String data)
        {
            if (Sport.IsOpen)
            {
                try
                {
                    Sport.WriteLine(data + "\r\n");
                    cWriteTimeOut = false;
                }
                catch (Exception ex)
                {
                    if (ex is TimeoutException) cWriteTimeOut = true;
                    tools.WriteErrorLog("SerialSend/SendStringData: " + ex.Message);
                }
            }
        }

        private void CheckConnection(object myObject, EventArgs myEventArgs)
        {
            if (cWriteTimeOut)
            {
                if (++WriteErrorCount > 2)
                {
                    tools.ShowHelp(Sport.PortName + " is not sending correctly. It will be closed.", "Serial Port", 5000, true, false, true);
                    Close();
                    // [XPLAT] refresh the port indicator via the local ISerialStatusSink contract
                    // (the host window implements it), replacing the old direct view-callback.
                    statusSink.OnPortStateChanged();
                }
            }
            else
            {
                WriteErrorCount = 0;
            }
        }

        private bool SerialPortExists(string Name)
        {
            bool Result = false;
            // [XPLAT] route OS port enumeration through the local SerialPortHelper, which wraps the
            // cross-platform SerialPort.GetPortNames() (COMx on Windows; /dev/ttyUSB*, /dev/ttyACM* on
            // Linux; /dev/cu.* on macOS). The s == Name match loop is unchanged.
            foreach (string s in SerialPortHelper.GetPortNames())
            {
                if (s == Name)
                {
                    Result = true;
                    break;
                }
            }
            return Result;
        }
    }
}
