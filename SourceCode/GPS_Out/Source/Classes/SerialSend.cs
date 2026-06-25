// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO.Ports;
using Avalonia.Threading;   // [XPLAT] DispatcherTimer replaces System.Windows.Forms.Timer
using GPS_Out.Views;        // [XPLAT] back-reference retyped frmStart -> Avalonia MainWindow

namespace GPS_Out
{
    public class SerialSend
    {
        private readonly MainWindow mf;
        private bool cWriteTimeOut = false;
        private SerialPort Sport;
        // [XPLAT] WinForms System.Windows.Forms.Timer -> Avalonia DispatcherTimer (UI-thread, 1 s
        // watchdog). Interval is a TimeSpan; Start()/Stop() replace the Enabled flag.
        private readonly DispatcherTimer Timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        private int WriteErrorCount;

        public SerialSend(MainWindow CalledFrom)
        {
            this.mf = CalledFrom;
            Sport = new SerialPort(Properties.Settings.Default.Port, Properties.Settings.Default.Baud);
            Sport.WriteTimeout = 500;
            Sport.Parity = Parity.None;
            Sport.DataBits = 8;
            Sport.StopBits = StopBits.One;
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
                mf.Tls.WriteErrorLog("SerialSend/CloseRCport: " + ex.Message);
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
                mf.Tls.WriteErrorLog("SerialSend/OpenRCport: " + ex.Message);
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
                    mf.Tls.WriteErrorLog("SerialSend/SendStringData: " + ex.Message);
                }
            }
        }

        private void CheckConnection(object myObject, EventArgs myEventArgs)
        {
            if (cWriteTimeOut)
            {
                if (++WriteErrorCount > 2)
                {
                    mf.Tls.ShowHelp(Sport.PortName + " is not sending correctly. It will be closed.", "Serial Port", 5000, true, false, true);
                    Close();
                    // [XPLAT] refresh the port indicator via the internal ISerialStatusSink contract
                    // (MainWindow implements it) instead of the old direct mf.SetPortButtons1() call.
                    mf.OnPortStateChanged();
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
            // [XPLAT] route OS port enumeration through the internal SerialPortHelper (wraps the
            // cross-platform SerialPort.GetPortNames(): COMx / /dev/ttyUSB* / /dev/cu.*).
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