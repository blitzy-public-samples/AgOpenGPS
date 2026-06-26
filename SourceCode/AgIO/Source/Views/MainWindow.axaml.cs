// [XPLAT] migrated from net48/WinForms Forms/FormLoop (shell + composition + scan loop) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgIO.Properties;
using AgIO.Services;
using AgLibrary.Logging;
using AgLibrary.Settings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] AgIO main window — the cross-platform comms-hub shell that replaces the deleted WinForms
    /// <c>FormLoop</c>. This window is the composition root for the AgIO program: it instantiates and wires
    /// the extracted transport services (UDP loopback/network, serial, NTRIP, NMEA), starts them from the
    /// persisted settings, runs the one-second scan loop, and surfaces live module/UDP/NTRIP/GPS status.
    /// </summary>
    /// <remarks>
    /// Behaviour is ported 1:1 from <c>FormLoop</c>:
    /// <list type="bullet">
    /// <item><description>The constructor wires the four services in a cycle-breaking order (UDP first,
    /// then NMEA which requires UDP, then serial and NTRIP whose peers are assigned after construction),
    /// exactly the collaborator graph the WinForms partials shared through the form instance.</description></item>
    /// <item><description><see cref="OnOpened"/> reproduces <c>FormLoop_Load</c>: optionally start the UDP
    /// network, always start loopback, open the serial ports that were connected last run, configure NTRIP,
    /// list serial ports, optionally auto-run GPS_Out, then enable the scan loop.</description></item>
    /// <item><description><see cref="ScanTimerTick"/> reproduces <c>oneSecondLoopTimer_Tick</c> with the
    /// two-/ten-/180-second sub-loops (hello broadcast, module-alarm colours, traffic counters, port
    /// reconnect checks, IMU-disconnect notification to AgOpenGPS).</description></item>
    /// <item><description><see cref="OnClosing"/> reproduces <c>FormLoop_FormClosing</c>: persist the
    /// was-connected flags, save settings (only on a clean profile load), shut down NTRIP, close serial
    /// ports, stop the sockets, and close GPS_Out.</description></item>
    /// </list>
    /// The Windows-only conveniences from <c>FormLoop</c> (focus/minimize tracking, the
    /// <c>SetForegroundWindow</c>/<c>keybd_event</c> P/Invoke "show on warning", the splash picture box, the
    /// ISOBUS task-controller hack, and the advanced-view slide panel) are intentionally not reproduced:
    /// they are non-portable UI niceties whose absence never affects the comms hub, consistent with the
    /// AAP graceful-degradation rule. The full configuration-dialog surface is reimplemented in a later UI
    /// checkpoint (tracked in MIGRATION_DOCS/TRANSITION_MAP.md).
    /// </remarks>
    public partial class MainWindow : Window
    {
        // ---- Comms-hub services (composition root) -------------------------------------------------
        private readonly UdpLoopbackService _udp;
        private readonly NmeaService _nmea;
        private readonly SerialCommService _serial;
        private readonly NtripService _ntrip;

        // ---- Scan loop ------------------------------------------------------------------------------
        private readonly DispatcherTimer _scanTimer;
        private double _secondsSinceStart;
        private double _twoSecondTimer;
        private double _tenSecondTimer;
        private double _threeMinuteTimer;

        // ---- Module enable + hello state (mirrors the FormLoop fields) ------------------------------
        private bool _isConnectedIMU;
        private bool _isConnectedSteer;
        private bool _isConnectedMachine;
        private bool _lastHelloGPS;
        private bool _lastHelloAutoSteer;
        private bool _lastHelloMachine;
        private bool _lastHelloIMU;
        private bool _ntripToggle;

        /// <summary>
        /// [XPLAT] Composition root. Builds and wires the comms-hub services, subscribes to their status
        /// events, and arms (but does not start) the one-second scan loop. The window lifecycle events
        /// drive startup (<see cref="OnOpened"/>) and shutdown (<see cref="OnClosing"/>).
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // [XPLAT] Wire the service graph in a cycle-breaking order. NmeaService is the only service
            // requiring a constructor dependency (the UDP transport, which it must never null-deref); every
            // other peer link is assigned after construction, reproducing the collaborator graph the
            // WinForms FormLoop partials shared implicitly through the form instance.
            _udp = new UdpLoopbackService();
            _nmea = new NmeaService(_udp);
            _udp.Nmea = _nmea;

            // [XPLAT] Inject the cross-platform port-name provider and the optional error presenter
            // (replacing the WinForms MessageBox.Show), supplied by the Avalonia bootstrap. Both may be
            // null until the platform layer is wired; SerialCommService null-guards them internally.
            _serial = new SerialCommService(RegistrySettings.PlatformServices, RegistrySettings.ErrorPresenter);
            _serial.Udp = _udp;
            _serial.Nmea = _nmea;
            _udp.Serial = _serial;

            _ntrip = new NtripService();
            _ntrip.Udp = _udp;
            _ntrip.Serial = _serial;
            _ntrip.Nmea = _nmea;

            // [XPLAT] Surface transport status through the services' events instead of the former direct
            // label/colour mutation inside the comm partials.
            _udp.StatusChanged += Udp_StatusChanged;
            _ntrip.StatusChanged += Ntrip_StatusChanged;
            _ntrip.MessageRequested += Ntrip_MessageRequested;

            _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _scanTimer.Tick += ScanTimerTick;

            Opened += OnOpened;
            Closing += OnClosing;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ===========================================================================================
        //  Startup — mirrors FormLoop_Load (comms-hub start only)
        // ===========================================================================================
        private void OnOpened(object sender, EventArgs e)
        {
            Settings s = Settings.Default;

            // Profile load warnings (FormLoop_Load TimedMessageBox calls).
            if (RegistrySettings.profileLoadResult == LoadResult.Failed)
            {
                ShowTimedMessage(10000, "Profile Load Warning",
                    "AgIO profile could not be fully loaded (file may be corrupt). " +
                    "Current session is using defaults for missing/invalid values.");
            }
            if (RegistrySettings.profileLoadedFromBackup)
            {
                ShowTimedMessage(10000, "Profile Recovery",
                    "AgIO profile was recovered from .last backup because the main file was unreadable.");
            }

            if (s.setDisplay_StartMinimized)
            {
                WindowState = WindowState.Minimized;
            }

            // UDP network (modules) — optional; loopback (AgOpenGPS) — always.
            if (s.setUDP_isOn)
            {
                _udp.LoadUDPNetwork();
                Log.EventWriter("UDP Network Is On");
            }
            else
            {
                lblIP.Text = "Off";
            }

            _udp.LoadLoopback();

            _nmea.isSendNMEAToUDP = s.setUDP_isSendNMEAToUDP;
            _ntrip.packetSizeNTRIP = s.setNTRIP_packetSize;
            _ntrip.isSendToSerial = s.setNTRIP_sendToSerial;
            _ntrip.isSendToUDP = s.setNTRIP_sendToUDP;

            // Open the serial ports that were connected on the previous run (port names/baud from settings).
            SerialCommService.baudRateGPS = s.setPort_baudRateGPS;
            SerialCommService.portNameGPS = s.setPort_portNameGPS;
            _serial.wasGPSConnectedLastRun = s.setPort_wasGPSConnected;
            if (_serial.wasGPSConnectedLastRun)
            {
                _serial.OpenGPSPort();
                if (_serial.spGPS.IsOpen) lblGPS1Comm.Text = SerialCommService.portNameGPS;
            }

            SerialCommService.baudRateRtcm = s.setPort_baudRateRtcm;
            SerialCommService.portNameRtcm = s.setPort_portNameRtcm;
            _serial.wasRtcmConnectedLastRun = s.setPort_wasRtcmConnected;
            if (_serial.wasRtcmConnectedLastRun)
            {
                _serial.OpenRtcmPort();
            }

            SerialCommService.portNameIMU = s.setPort_portNameIMU;
            _serial.wasIMUConnectedLastRun = s.setPort_wasIMUConnected;
            if (_serial.wasIMUConnectedLastRun)
            {
                _serial.OpenIMUPort();
                if (_serial.spIMU.IsOpen) lblIMUComm.Text = SerialCommService.portNameIMU;
            }

            SerialCommService.portNameSteerModule = s.setPort_portNameSteer;
            _serial.wasSteerModuleConnectedLastRun = s.setPort_wasSteerModuleConnected;
            if (_serial.wasSteerModuleConnectedLastRun)
            {
                _serial.OpenSteerModulePort();
                if (_serial.spSteerModule.IsOpen) lblMod1Comm.Text = SerialCommService.portNameSteerModule;
            }

            SerialCommService.portNameMachineModule = s.setPort_portNameMachine;
            _serial.wasMachineModuleConnectedLastRun = s.setPort_wasMachineModuleConnected;
            if (_serial.wasMachineModuleConnectedLastRun)
            {
                _serial.OpenMachineModulePort();
                if (_serial.spMachineModule.IsOpen) lblMod2Comm.Text = SerialCommService.portNameMachineModule;
            }

            _ntrip.ConfigureNTRIP();

            // Module enable flags (drive the status-indicator visibility).
            _isConnectedIMU = s.setMod_isIMUConnected;
            _isConnectedSteer = s.setMod_isSteerConnected;
            _isConnectedMachine = s.setMod_isMachineConnected;
            SetModulesOnOff();

            // Serial ports list.
            RescanPorts();

            // Title with profile.
            Title = "AgIO  v" + Program.Version + " Profile: " + RegistrySettings.profileName;

            // Auto-run GPS_Out if configured.
            if (s.setDisplay_isAutoRunGPS_Out)
            {
                StartGPS_Out();
                Log.EventWriter("Run GPS_Out");
            }

            _scanTimer.Start();
        }

        // ===========================================================================================
        //  Scan loop — mirrors oneSecondLoopTimer_Tick and its sub-loops
        // ===========================================================================================
        private void ScanTimerTick(object sender, EventArgs e)
        {
            _secondsSinceStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;

            // All NTRIP per-second work (connect/reconnect/watchdog) lives in the service.
            _ntrip.DoNTRIPSecondRoutine();

            if ((_secondsSinceStart - _twoSecondTimer) > 2)
            {
                TwoSecondLoop();
                _twoSecondTimer = _secondsSinceStart;
            }

            if ((_secondsSinceStart - _tenSecondTimer) > 9.5)
            {
                TenSecondLoop();
                _tenSecondTimer = _secondsSinceStart;
            }

            if ((_secondsSinceStart - _threeMinuteTimer) > 180)
            {
                ThreeMinuteLoop();
                _threeMinuteTimer = _secondsSinceStart;
            }

            // NTRIP byte-activity toggle colour (FormLoop lblNTRIPBytes blink).
            if (_ntrip.isNTRIP_RequiredOn)
            {
                _ntripToggle = !_ntripToggle;
                lblNTRIPBytes.Background = _ntripToggle
                    ? GetBrush("AogNtripActiveBrush")
                    : GetBrush("AogNtripIdleBrush");
            }
            else
            {
                lblNTRIPBytes.Background = Brushes.Transparent;
            }
        }

        private void TwoSecondLoop()
        {
            DoHelloAlarmLogic();
            DoTraffic();

            // Send a hello to modules (encapsulated in the UDP service so the hello frame stays private).
            _udp.SendHelloToModules();
        }

        private void TenSecondLoop()
        {
            // Update the local IP list (FormLoop lblIP refresh).
            var ipText = new StringBuilder();
            try
            {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipText.Append(ip.ToString()).Append("\r\n");
                    }
                }
                if (Settings.Default.setUDP_isOn)
                {
                    lblIP.Text = ipText.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("IP enumeration error: " + ex.Message);
            }

            // Serial reconnect / stale-port checks (FormLoop "Serial update" region).
            if (_serial.wasIMUConnectedLastRun && !_serial.spIMU.IsOpen)
            {
                // Tell AgOpenGPS the IMU is disconnected (byte-exact frame from FormLoop.TenSecondLoop).
                byte[] imuClose = { 0x80, 0x81, 0x7C, 0xD4, 2, 1, 0, 83 };
                _udp.SendToLoopBackMessageAOG(imuClose);
                _serial.wasIMUConnectedLastRun = false;
                lblIMUComm.Text = "";
            }

            if (_serial.wasGPSConnectedLastRun && !_serial.spGPS.IsOpen)
            {
                _serial.wasGPSConnectedLastRun = false;
                lblGPS1Comm.Text = "";
            }

            if (_serial.wasSteerModuleConnectedLastRun && !_serial.spSteerModule.IsOpen)
            {
                _serial.wasSteerModuleConnectedLastRun = false;
                lblMod1Comm.Text = "";
            }

            if (_serial.wasMachineModuleConnectedLastRun && !_serial.spMachineModule.IsOpen)
            {
                _serial.wasMachineModuleConnectedLastRun = false;
                lblMod2Comm.Text = "";
            }
        }

        private void ThreeMinuteLoop()
        {
            // [XPLAT] FormLoop's three-minute loop only collapsed the advanced-view slide panel, which is a
            // UI nicety not reproduced in the CP2 status shell; intentionally a no-op for the comms hub.
        }

        // ===========================================================================================
        //  Status display — mirrors DoHelloAlarmLogic / DoTraffic / SetModulesOnOff
        // ===========================================================================================
        private void DoHelloAlarmLogic()
        {
            CTraffic traffic = _udp.traffic;
            bool currentHello;

            if (_isConnectedMachine)
            {
                currentHello = traffic.helloFromMachine < 3;
                if (currentHello != _lastHelloMachine)
                {
                    borderMachine.Background = currentHello ? GetBrush("AogModuleOkBrush") : GetBrush("AogModuleAlarmBrush");
                    _lastHelloMachine = currentHello;
                }
            }

            if (_isConnectedSteer)
            {
                currentHello = traffic.helloFromAutoSteer < 3;
                if (currentHello != _lastHelloAutoSteer)
                {
                    borderSteer.Background = currentHello ? GetBrush("AogModuleOkBrush") : GetBrush("AogModuleAlarmBrush");
                    _lastHelloAutoSteer = currentHello;
                }
            }

            if (_isConnectedIMU)
            {
                currentHello = traffic.helloFromIMU < 3;
                if (currentHello != _lastHelloIMU)
                {
                    borderIMU.Background = currentHello ? GetBrush("AogModuleOkBrush") : GetBrush("AogModuleAlarmBrush");
                    _lastHelloIMU = currentHello;
                }
            }

            currentHello = traffic.cntrGPSOut != 0;
            if (currentHello != _lastHelloGPS)
            {
                borderGPS.Background = currentHello ? GetBrush("AogModuleOkBrush") : GetBrush("AogModuleAlarmBrush");
                _lastHelloGPS = currentHello;
            }
        }

        private void DoTraffic()
        {
            CTraffic traffic = _udp.traffic;
            traffic.helloFromMachine++;
            traffic.helloFromAutoSteer++;
            traffic.helloFromIMU++;

            lblFromGPS.Text = traffic.cntrGPSOut == 0
                ? "---"
                : (traffic.cntrGPSOut >> 1).ToString(CultureInfo.InvariantCulture);

            // reset the GPS byte counter every cycle (FormLoop DoTraffic)
            traffic.cntrGPSOut = 0;

            lblCurrentLon.Text = _nmea.longitude.ToString("N7", CultureInfo.InvariantCulture);
            lblCurrentLat.Text = _nmea.latitude.ToString("N7", CultureInfo.InvariantCulture);
        }

        private void SetModulesOnOff()
        {
            // Module status indicators are shown only for enabled modules (FormLoop SetModulesOnOff).
            borderIMU.IsVisible = _isConnectedIMU;
            borderSteer.IsVisible = _isConnectedSteer;
            borderMachine.IsVisible = _isConnectedMachine;

            Settings s = Settings.Default;
            if (s.setMod_isIMUConnected != _isConnectedIMU ||
                s.setMod_isSteerConnected != _isConnectedSteer ||
                s.setMod_isMachineConnected != _isConnectedMachine)
            {
                s.setMod_isIMUConnected = _isConnectedIMU;
                s.setMod_isSteerConnected = _isConnectedSteer;
                s.setMod_isMachineConnected = _isConnectedMachine;
                s.Save();
            }
        }

        private void RescanPorts()
        {
            // [XPLAT] Port-name discovery now goes through the instance method, which routes to
            // IPlatformServices.GetSerialPortNames() (COMx / /dev/ttyUSB* / /dev/cu.*) instead of the
            // Windows-leaning static SerialPort.GetPortNames(). It never returns null.
            string[] ports = System.Linq.Enumerable.ToArray(_serial.GetAvailablePortNames());
            lblSerialPorts.Text = (ports == null || ports.Length == 0)
                ? "None"
                : string.Join("\r\n", ports);
        }

        // ===========================================================================================
        //  Service status event handlers (marshalled to the UI thread)
        // ===========================================================================================
        private void Udp_StatusChanged(object sender, UdpStatusEventArgs e)
        {
            RunOnUi(() =>
            {
                if (e.Connected && !string.IsNullOrEmpty(e.Message))
                {
                    lblIP.Text = e.Message;
                }
                else if (!e.Connected)
                {
                    ShowTimedMessage(2000, "AgIO Network", e.Message);
                }
            });
        }

        private void Ntrip_StatusChanged(object sender, NtripStatusEventArgs e)
        {
            RunOnUi(() =>
            {
                if (e.Bytes != null) lblNTRIPBytesText.Text = e.Bytes;
                if (e.Watch != null) lblNTRIPMessages.Text = e.Watch;
            });
        }

        private void Ntrip_MessageRequested(object sender, NtripMessageEventArgs e)
        {
            RunOnUi(() => ShowTimedMessage(e.TimeoutMs, e.Title, e.Message));
        }

        // ===========================================================================================
        //  Dialog access + GPS_Out auto-launch + helpers
        // ===========================================================================================
        private async void OnShowPgnGuideClick(object sender, RoutedEventArgs e)
        {
            var dialog = new FormPGNView(new FormPGNViewModel());
            await dialog.ShowDialog(this);
        }

        /// <summary>
        /// [XPLAT] Auto-launches the GPS_Out helper program if it is not already running, ported from the
        /// WinForms <c>Controls.Designer.cs StartGPS_Out()</c>. <c>Application.StartupPath</c> is replaced by
        /// the cross-platform <see cref="AppContext.BaseDirectory"/>; a missing executable degrades
        /// gracefully (logged + a transient message) exactly as the original did.
        /// </summary>
        private void StartGPS_Out()
        {
            if (Process.GetProcessesByName("GPS_Out").Length != 0)
            {
                return;
            }

            string strPath = Path.Combine(AppContext.BaseDirectory, "GPS_Out.exe");
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = strPath,
                    WorkingDirectory = Path.GetDirectoryName(strPath),
                    UseShellExecute = false,
                };
                Process.Start(processInfo);
            }
            catch
            {
                ShowTimedMessage(2000, "No File Found", "Can't Find GPS_Out");
                Log.EventWriter("No File Found, Can't Find GPS_Out");
            }
        }

        // [XPLAT] Transient auto-dismissing message, replacing the WinForms TimedMessageBox/Form.
        private void ShowTimedMessage(int timeoutMs, string title, string message)
        {
            var view = new FormTimedMessageView(new FormTimedMessageViewModel(title, message), timeoutMs);
            view.Show(this);
        }

        // [XPLAT] Resolve a named brush from the application/window resources, with a safe fallback.
        private IBrush GetBrush(string key)
        {
            if (this.TryFindResource(key, out object resource) && resource is IBrush brush)
            {
                return brush;
            }
            return Brushes.Transparent;
        }

        // [XPLAT] Marshal an action onto the Avalonia UI thread (service events may fire on socket/serial threads).
        private static void RunOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }

        // ===========================================================================================
        //  Shutdown — mirrors FormLoop_FormClosing
        // ===========================================================================================
        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            _scanTimer.Stop();

            Settings s = Settings.Default;
            s.setPort_wasGPSConnected = _serial.wasGPSConnectedLastRun;
            s.setPort_wasIMUConnected = _serial.wasIMUConnectedLastRun;
            s.setPort_wasSteerModuleConnected = _serial.wasSteerModuleConnectedLastRun;
            s.setPort_wasMachineModuleConnected = _serial.wasMachineModuleConnectedLastRun;
            s.setPort_wasRtcmConnected = _serial.wasRtcmConnectedLastRun;

            if (RegistrySettings.profileLoadResult == LoadResult.Ok)
            {
                s.Save();
            }
            else
            {
                Log.EventWriter($"AgIO profile save skipped: profile load state is {RegistrySettings.profileLoadResult}");
            }

            try { _ntrip.ShutDownNTRIP(); } catch (Exception ex) { Log.EventWriter("NTRIP shutdown error: " + ex.Message); }
            try { _serial.Dispose(); } catch (Exception ex) { Log.EventWriter("Serial close error: " + ex.Message); }
            _udp.Stop();

            // Close the GPS_Out helper if AgIO launched it (FormLoop_FormClosing behaviour).
            try
            {
                Process[] processName = Process.GetProcessesByName("GPS_Out");
                if (processName.Length != 0)
                {
                    processName[0].CloseMainWindow();
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("GPS_Out close error: " + ex.Message);
            }

            Log.EventWriter("Program Exit: " +
                DateTime.Now.ToString("f", CultureInfo.InvariantCulture) + "\n\r");

            Log.FileSaveSystemEvents();
        }
    }
}
