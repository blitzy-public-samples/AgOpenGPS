// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GPS_Out.PGNs;

namespace GPS_Out.Views
{
    /// <summary>
    /// Avalonia 1:1 parity reimplementation of the deleted WinForms <c>frmStart</c> main window:
    /// the GPS_Out NMEA bridge that receives PGN data over UDP loopback from AgIO/AgOpenGPS and
    /// emits NMEA sentences (GGA/VTG/RMC/ZDA/GSA) on a serial port.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour is preserved verbatim from <c>frmStart</c>; only the host APIs change:
    /// <list type="bullet">
    /// <item>The <c>frmStart</c> back-reference held by the helper classes (clsTools, UDPComm,
    /// SerialSend, the PGN encoders) is retyped to this window; this class exposes the same public
    /// surface they consumed (<see cref="Tls"/>, <see cref="AGIOdata"/>, <see cref="AOGdata"/>,
    /// <see cref="CheckSum"/>, <see cref="Heading"/>, <see cref="OnPortStateChanged"/>).</item>
    /// <item>WinForms <c>System.Windows.Forms.Timer</c>s become <see cref="DispatcherTimer"/>s.</item>
    /// <item>WinForms <c>CheckBox.Checked</c>/<c>RadioButton.Checked</c> become Avalonia
    /// <c>ToggleButton</c>/<c>RadioButton.IsChecked</c>; <c>Control.BackColor</c> becomes a brush
    /// <c>Background</c>; <c>PictureBox.Image</c> becomes <c>Image.Source</c> from an avares bitmap.</item>
    /// <item>Window geometry persistence flows through <see cref="IWindowState"/>; serial-port
    /// indicator refresh flows through <see cref="ISerialStatusSink"/>.</item>
    /// <item>Combo/checkbox handlers are gated by <see cref="_isLoaded"/> because Avalonia raises
    /// <c>SelectionChanged</c>/<c>IsCheckedChanged</c> on programmatic assignment; the load-time
    /// side effects WinForms applied through those handlers are applied explicitly in
    /// <see cref="OnLoaded"/>.</item>
    /// </list>
    /// </remarks>
    public partial class MainWindow : Window, INmeaHost, ISerialStatusSink, IWindowState
    {
        // ---- Public surface consumed by the ported helper classes (was frmStart's public fields) ----
        public UDPComm AGIOcomm;
        public PGN54908 AGIOdata;
        public UDPComm AOGcomm;
        public PGN100 AOGdata;
        public PGN_GGA GGA;
        public string GGAsentence = "";
        public PGN_GSA GSA;
        public string GSAsentence = "";
        public PGN_RMC RMC;
        public string RMCsentence = "";
        public SerialSend SER;
        public clsTools Tls;
        public PGN_VTG VTG;
        public string VTGsentence = "";
        public PGN_ZDA ZDA;
        public string ZDAsentence = "";

        // [XPLAT] INmeaHost provider wiring — the decoupled PGN encoder/parser classes hold their host
        // back-reference as the INmeaHost abstraction (see PGNs/INmeaHost.cs) rather than this concrete
        // window, so the byte-frozen protocol logic no longer depends on any UI type. These explicit
        // interface members surface the existing public fields as the read-only INmeaHost properties
        // without altering field semantics or any existing access; CheckSum(string) and Heading()
        // satisfy the interface implicitly via their public methods below.
        clsTools INmeaHost.Tls => Tls;
        PGN100 INmeaHost.AOGdata => AOGdata;
        PGN54908 INmeaHost.AGIOdata => AGIOdata;

        // ---- Private state (matches frmStart) ----
        private string HeadingType = "";

        // [XPLAT] WinForms Color.Orange BackColor -> Avalonia brush applied to the quality readouts.
        private readonly IBrush SimColor = Brushes.Orange;
        private int Watchdog;

        // [XPLAT] guard: Avalonia raises SelectionChanged / IsCheckedChanged on programmatic sets
        // (WinForms did too for some, but the construction order differs); stays false until OnLoaded
        // finishes wiring state so no handler fires before the helpers/timers exist.
        private bool _isLoaded;
        private bool _hasInitialized;

        // [XPLAT] send-pump re-entrancy guard. Replaces the WinForms BackgroundWorker (which the
        // migration spec forbids): frmStart.Send() called backgroundWorker1.RunWorkerAsync() only when
        // !IsBusy. SerialSend.SendStringData is synchronous and swallows its own port exceptions, so the
        // synchronous Send() below uses this flag for the identical "skip if already sending" semantics
        // while preserving the exact send order and clear-after-send — no worker thread is needed.
        private bool _sending;

        // [XPLAT] WinForms designer Timers -> DispatcherTimers. Intervals reproduce the designer values
        // (tmrGGA 5000, tmrGSA 1000, tmrDisplay 500, tmrMinimize 120000); the rate timers' intervals are
        // (re)assigned from the rate combos exactly as in frmStart.
        private readonly DispatcherTimer tmrGGA = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
        private readonly DispatcherTimer tmrVTG = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        private readonly DispatcherTimer tmrRMC = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        private readonly DispatcherTimer tmrZDA = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        private readonly DispatcherTimer tmrGSA = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        private readonly DispatcherTimer tmrDisplay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer tmrMinimize = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120000) };

        // [XPLAT] port-state indicator images (were Properties.Resources.On/.Off PictureBox images);
        // loaded from the AvaloniaResource PNGs that replaced the WinForms .resx image pipeline.
        private static readonly Bitmap OnImage = LoadAsset("On.png");
        private static readonly Bitmap OffImage = LoadAsset("Off.png");

        public MainWindow()
        {
            InitializeComponent();

            // [XPLAT] frmStart carried the satellite icon; set the Avalonia titlebar icon from the
            // retained satellite.ico (declared as <ApplicationIcon>). Guarded because the .ico is only
            // guaranteed to be embedded in the EXE header — if it is not also packed as an
            // AvaloniaResource the avares load throws, and a missing window icon must never block startup.
            try
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GPS_Out/satellite.ico")));
            }
            catch (Exception)
            {
                // non-fatal: window simply uses the default icon when satellite.ico is not an avares asset.
            }

            // Construction order mirrors frmStart exactly (Tls first so the comm/PGN classes can use it).
            // [XPLAT] clsTools is now parameterless (the frmStart back-reference was removed; the help
            // window resolves its owner from the Avalonia application lifetime). See clsTools.ShowHelp.
            Tls = new clsTools();
            // [XPLAT] inject clsTools + the two sub-PGN route delegates (replaces the former view
            // back-reference). Both comm instances receive the SAME pair so HandleData routes by
            // sub-PGN (54908 -> AGIOdata, 25727 -> AOGdata) regardless of which socket received the
            // datagram. The lambdas capture `this` and read the fields at invoke-time (after
            // StartUDPServer), so construction order vs AGIOdata/AOGdata below is not a hazard.
            AGIOcomm = new UDPComm(Tls, 15555, 8000, 7120, "AGIO", "127.103.104.105",
                d => AGIOdata.ParseByteData(d), d => AOGdata.ParseByteData(d), "127.255.255.255");
            AOGcomm = new UDPComm(Tls, 17777, 8500, 9010, "AOG", "127.100.101.102",
                d => AGIOdata.ParseByteData(d), d => AOGdata.ParseByteData(d), "127.255.255.255");
            AGIOdata = new PGN54908(this);
            GGA = new PGN_GGA(this);
            VTG = new PGN_VTG(this);
            // [XPLAT] SerialSend is decoupled from the view: it takes the clsTools helper and the
            // ISerialStatusSink contract (this window) instead of a window back-reference.
            SER = new SerialSend(Tls, this);
            RMC = new PGN_RMC(this);
            AOGdata = new PGN100(this);
            ZDA = new PGN_ZDA(this);
            GSA = new PGN_GSA(this);

            tmrGGA.Tick += tmrGGA_Tick;
            tmrVTG.Tick += tmrVTG_Tick;
            tmrRMC.Tick += tmrRMC_Tick;
            tmrZDA.Tick += tmrZDA_Tick;
            tmrGSA.Tick += tmrGSA_Tick;
            tmrDisplay.Tick += tmrDisplay_Tick;
            tmrMinimize.Tick += tmrMinimize_Tick;
        }

        // ====================================================================================
        //  Lifecycle
        // ====================================================================================

        /// <summary>[XPLAT] frmStart_Load port. Loads geometry/settings, starts the UDP servers and
        /// the display timer, and applies the persisted control state and DayColour palette.</summary>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            if (_hasInitialized)
            {
                return;
            }
            _hasInitialized = true;

            Tls.LoadFormData(this);
            AGIOcomm.StartUDPServer();
            AOGcomm.StartUDPServer();

            // Populate the combo items that were designer-defined in WinForms.
            foreach (string b in new[] { "4800", "9600", "19200", "38400", "57600", "115200" })
            {
                cboBaud1.Items.Add(b);
            }
            foreach (string r in new[] { "0", "1", "5", "10" })
            {
                cboGGA.Items.Add(r);
                cboVTG.Items.Add(r);
                cboRMC.Items.Add(r);
                cboZDA.Items.Add(r);
            }
            foreach (string p in new[] { "4", "5", "6", "7" })
            {
                cboPrecision.Items.Add(p);
            }

            LoadRCbox();
            ApplyDayColour();

            // Persisted control state (handlers are still gated, so set the values directly here).
            ckAutoHide.IsChecked = Properties.Settings.Default.AutoHide;
            ckAutoConnect.IsChecked = Properties.Settings.Default.AutoConnect;
            ckRoll.IsChecked = Properties.Settings.Default.UseRollCorrected;
            rbGN.IsChecked = Properties.Settings.Default.SentenceStart == "$GN";
            ckGSA.IsChecked = Properties.Settings.Default.SendGSA;

            SafeSelect(cboGGA, Properties.Settings.Default.GGA);
            SafeSelect(cboVTG, Properties.Settings.Default.VTG);
            SafeSelect(cboRMC, Properties.Settings.Default.RMC);
            SafeSelect(cboZDA, Properties.Settings.Default.ZDA);
            SafeSelect(cboPrecision, Properties.Settings.Default.SentencePrecisionIndex);
            ckSimulate.IsChecked = Properties.Settings.Default.Simulate;

            // Enable handler side effects from this point on.
            _isLoaded = true;

            // Apply the load-time side effects WinForms produced via the change handlers.
            ApplyRate(cboGGA, tmrGGA);
            ApplyRate(cboVTG, tmrVTG);
            ApplyRate(cboRMC, tmrRMC);
            ApplyRate(cboZDA, tmrZDA);
            if (Properties.Settings.Default.SendGSA)
            {
                tmrGSA.Start();
            }
            ApplyPrecision();

            tmrDisplay.Start();             // tmrDisplay.Enabled = true (designer default)
            if (ckAutoHide.IsChecked == true)
            {
                tmrMinimize.Start();        // tmrMinimize.Enabled tracked AutoHide via ckAutoHide handler
                WindowState = WindowState.Minimized;
            }
        }

        /// <summary>[XPLAT] frmStart_FormClosed port: persist geometry + settings and close the serial port.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            Tls.SaveFormData(this);
            Properties.Settings.Default.Save();
            SER.Close();
            base.OnClosing(e);
        }

        /// <summary>[XPLAT] frmStart_Resize port: pause the auto-minimize timer while minimized.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (!_isLoaded || change.Property != WindowStateProperty)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                tmrMinimize.Stop();
            }
            else if (ckAutoHide.IsChecked == true)
            {
                tmrMinimize.Start();
            }
            else
            {
                tmrMinimize.Stop();
            }
        }

        // ====================================================================================
        //  Public surface consumed by the helper classes (unchanged behaviour from frmStart)
        // ====================================================================================

        /// <summary>XOR checksum between '$' and '*' — used by every PGN sentence encoder.</summary>
        public int CheckSum(string Data)
        {
            int CK = 0;
            int End = Data.IndexOf("*", StringComparison.Ordinal);
            char[] buf = Data.ToCharArray();
            if (buf[0] == '$' && End > -1)
            {
                for (int i = 1; i < End; i++)
                {
                    CK ^= buf[i];
                }
            }
            return CK;
        }

        /// <summary>Maps an NMEA fix-quality byte to its display string.</summary>
        public string FixQuality(byte Qu)
        {
            string Result = "";
            switch (Qu)
            {
                case 1: Result = "GPS 1"; break;
                case 2: Result = "DGPS"; break;
                case 3: Result = "PPS"; break;
                case 4: Result = "RTK fix"; break;
                case 5: Result = "Float"; break;
                case 6: Result = "Estimate"; break;
                case 7: Result = "Man IP"; break;
                case 8: Result = "Sim"; break;
            }
            return Result;
        }

        /// <summary>Selects the best available heading source (Dual/Fix2Fix/True/IMU) and records its type.</summary>
        public double Heading()
        {
            double Result = 0;
            HeadingType = "";

            if (AGIOdata.HeadingDual < 361)
            {
                Result = AGIOdata.HeadingDual;
                HeadingType = "D";
            }
            else if (AOGdata.Fix2FixHeading < 361)
            {
                Result = AOGdata.Fix2FixHeading;
                HeadingType = "F";
            }
            else if (AGIOdata.TrueHeading < 361)
            {
                Result = AGIOdata.TrueHeading;
                HeadingType = "T";
            }
            else if (AGIOdata.IMUheading < 361)
            {
                Result = AGIOdata.IMUheading;
                HeadingType = "I";
            }

            return Result;
        }

        /// <summary>Synchronises the port/baud combos and connect button/indicator with the serial state.</summary>
        public void SetPortButtons1()
        {
            cboPort1.SelectedIndex = IndexOfItem(cboPort1, SER.PortNm);
            cboBaud1.SelectedIndex = IndexOfItem(cboBaud1, SER.Baud.ToString(CultureInfo.InvariantCulture));

            if (SER.IsOpen())
            {
                cboBaud1.IsEnabled = false;
                cboPort1.IsEnabled = false;
                btnConnect1.Content = "Disconnect";
                PortIndicator1.Source = OnImage;
            }
            else
            {
                cboBaud1.IsEnabled = true;
                cboPort1.IsEnabled = true;
                btnConnect1.Content = "Connect";
                PortIndicator1.Source = OffImage;
            }
        }

        /// <summary>[XPLAT] <see cref="ISerialStatusSink"/>: refresh the port indicator (was mf.SetPortButtons1()).
        /// SerialSend's watchdog (a DispatcherTimer) already ticks on the UI thread, but the marshal guard
        /// keeps this safe if any caller raises it off-thread — Avalonia controls are UI-thread-affine.</summary>
        public void OnPortStateChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(OnPortStateChanged);
                return;
            }
            SetPortButtons1();
        }

        // ---- IWindowState (explicit; preserves the WinForms Form.Name="frmStart" settings keys) ----
        string IWindowState.Name => "frmStart";
        PixelPoint IWindowState.Position
        {
            get => Position;
            set => Position = value;
        }
        Size IWindowState.ClientSize => ClientSize;

        // ====================================================================================
        //  Private helpers
        // ====================================================================================

        private void LoadRCbox()
        {
            cboPort1.Items.Clear();
            foreach (string s in SerialPortHelper.GetPortNames())
            {
                cboPort1.Items.Add(s);
            }
            SetPortButtons1();
        }

        /// <summary>[XPLAT] frmStart_Load applied the persisted DayColour to four BackColor surfaces
        /// (this.BackColor, tabPage1/2.BackColor, PortIndicator1.BackColor). Here the DayColour setting —
        /// migrated from a WinForms System.Drawing.Color to a cross-platform "R, G, B" string — is parsed
        /// locally (see <see cref="ParseDayColour"/>) and applied as an Avalonia brush to the equivalent
        /// surfaces. Parsing is self-contained (no dependency on App resources) and never crashes startup.</summary>
        private void ApplyDayColour()
        {
            IBrush dayBrush = ParseDayColour();
            Background = dayBrush;
            tab1Canvas.Background = dayBrush;
            tab2Canvas.Background = dayBrush;
            PortIndicator1Bg.Background = dayBrush;
        }

        /// <summary>[XPLAT] Parses the persisted <c>Settings.DayColour</c> "R, G, B" string into an
        /// Avalonia brush using <see cref="CultureInfo.InvariantCulture"/> (so the saved value round-trips
        /// identically across locales — the cross-platform numeric-I/O rule). On any parse failure the
        /// documented default (210, 220, 230) is returned and the error is logged via the shared tools
        /// logger, so a malformed setting degrades gracefully instead of interrupting startup.</summary>
        private IBrush ParseDayColour()
        {
            try
            {
                string raw = Properties.Settings.Default.DayColour;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    string[] parts = raw.Split(',');
                    if (parts.Length == 3)
                    {
                        byte r = byte.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                        byte g = byte.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                        byte b = byte.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                        return new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                }
            }
            catch (Exception ex)
            {
                Tls.WriteErrorLog("GPS_Out: failed to parse Settings.DayColour - using default. " + ex.Message);
            }
            return new SolidColorBrush(Color.FromRgb(210, 220, 230));
        }

        /// <summary>Enables/disables a sentence timer from its rate combo (was the cbo*_SelectedIndexChanged body).</summary>
        private static void ApplyRate(ComboBox cbo, DispatcherTimer tmr)
        {
            string t = cbo.SelectedItem as string;
            if (string.IsNullOrEmpty(t) || t == "0")
            {
                tmr.Stop();
            }
            else
            {
                tmr.Interval = TimeSpan.FromMilliseconds(1000 / Convert.ToInt16(t, CultureInfo.InvariantCulture));
                tmr.Start();
            }
        }

        /// <summary>Derives the sentence/display precision formats from the precision combo (was cbPrecision body).</summary>
        private void ApplyPrecision()
        {
            string t = cboPrecision.SelectedItem as string;
            if (string.IsNullOrEmpty(t))
            {
                return;
            }

            int precision = Convert.ToInt32(t, CultureInfo.InvariantCulture);
            string NewFormat = ".";
            for (int i = 0; i < precision; i++)
            {
                NewFormat += "0";
            }

            Properties.Settings.Default.SentencePrecisionFormat = "00" + NewFormat;
            Properties.Settings.Default.DisplayPrecisionFormat = "0" + NewFormat;
        }

        /// <summary>[XPLAT] replacement for ComboBox.FindStringExact (no Avalonia equivalent).</summary>
        private static int IndexOfItem(ComboBox cbo, string value)
        {
            if (value == null)
            {
                return -1;
            }
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (string.Equals(cbo.Items[i] as string, value, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Range-checked SelectedIndex assignment (settings values are always valid in practice).</summary>
        private static void SafeSelect(ComboBox cbo, int index)
        {
            cbo.SelectedIndex = (index >= 0 && index < cbo.Items.Count) ? index : -1;
        }

        /// <summary>[XPLAT] Clipboard.SetText -> TopLevel.Clipboard.SetTextAsync (fire-and-forget).</summary>
        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            TopLevel top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null)
            {
                _ = top.Clipboard.SetTextAsync(text);
            }
        }

        private static Bitmap LoadAsset(string name)
            => new Bitmap(AssetLoader.Open(new Uri("avares://GPS_Out/Resources/" + name)));

        /// <summary>[XPLAT] frmStart.Send() port. WinForms kicked backgroundWorker1.RunWorkerAsync()
        /// only when !IsBusy; BackgroundWorker is forbidden by the migration spec, so this sends
        /// synchronously under <see cref="_sending"/>. The send order (GGA, VTG, RMC, ZDA, GSA) and the
        /// clear-all-five-after-send are preserved byte-for-byte from the former DoWork /
        /// RunWorkerCompleted bodies. SerialSend.SendStringData is synchronous and swallows its own port
        /// exceptions, so running it inline on the dispatcher adds no latency on the receive→send path.</summary>
        private void Send()
        {
            if (_sending)
            {
                return;
            }
            _sending = true;
            try
            {
                if (GGAsentence != "") SER.SendStringData(GGAsentence);
                if (VTGsentence != "") SER.SendStringData(VTGsentence);
                if (RMCsentence != "") SER.SendStringData(RMCsentence);
                if (ZDAsentence != "") SER.SendStringData(ZDAsentence);
                if (GSAsentence != "") SER.SendStringData(GSAsentence);
            }
            finally
            {
                GGAsentence = "";
                VTGsentence = "";
                RMCsentence = "";
                ZDAsentence = "";
                GSAsentence = "";
                _sending = false;
            }
        }

        private void UpdateForm()
        {
            if (WindowState == WindowState.Minimized)
            {
                return;
            }

            string disp = Properties.Settings.Default.DisplayPrecisionFormat;
            if (AOGdata.Connected() && (AGIOdata.Connected() || Properties.Settings.Default.Simulate))
            {
                lbLon.Text = AOGdata.Longitude.ToString(disp, CultureInfo.InvariantCulture);
                lbLat.Text = AOGdata.Latitude.ToString(disp, CultureInfo.InvariantCulture);
            }
            else
            {
                lbLon.Text = AGIOdata.Longitude.ToString(disp, CultureInfo.InvariantCulture);
                lbLat.Text = AGIOdata.Latitude.ToString(disp, CultureInfo.InvariantCulture);
            }

            lbSpeed.Text = AGIOdata.Speed.ToString("N1", CultureInfo.InvariantCulture);
            lbQuality.Text = FixQuality(AGIOdata.FixQuality);
            lbHDOP.Text = AGIOdata.HDOP.ToString("N2", CultureInfo.InvariantCulture);
            lbSats.Text = AGIOdata.Satellites.ToString(CultureInfo.InvariantCulture);
            lbElev.Text = AGIOdata.Altitude.ToString("N2", CultureInfo.InvariantCulture);
            lbAge.Text = AGIOdata.Age.ToString("N1", CultureInfo.InvariantCulture);

            ckRoll.IsEnabled = AOGdata.Connected();

            if (AGIOdata.Connected())
            {
                lbQuality.Background = Brushes.Transparent;
                label4.Background = Brushes.Transparent;
                lbSim.IsVisible = false;
                Title = "GPS_Out [" + Tls.AppVersion() + "]   " + HeadingType;
            }
            else if (Properties.Settings.Default.Simulate)
            {
                lbQuality.Background = SimColor;
                label4.Background = SimColor;
                lbSim.IsVisible = true;
                lbSim.Background = SimColor;
                Title = "GPS_Out [" + Tls.AppVersion() + "]  Simulated Data   " + HeadingType;
            }
            else
            {
                lbQuality.Background = Brushes.Transparent;
                label4.Background = Brushes.Transparent;
                lbSim.IsVisible = false;
                Title = "GPS_Out [" + Tls.AppVersion() + "]   NO DATA";
            }
        }

        // ====================================================================================
        //  Timer ticks
        // ====================================================================================

        private void tmrDisplay_Tick(object sender, EventArgs e) => UpdateForm();

        private void tmrGGA_Tick(object sender, EventArgs e)
        {
            if (GGAsentence == "")
            {
                Watchdog = 0;
                if (AGIOdata.Connected() || Properties.Settings.Default.Simulate)
                {
                    GGAsentence = GGA.Build();
                    Send();
                }
            }
            else
            {
                Watchdog++;
                // [XPLAT] frmStart cancelled the BackgroundWorker after 10 stalled ticks; with the
                // synchronous send pump the equivalent is clearing the send-pump re-entrancy guard.
                if (Watchdog > 10)
                {
                    _sending = false;
                }
            }
        }

        private void tmrGSA_Tick(object sender, EventArgs e)
        {
            if (GSAsentence == "")
            {
                Watchdog = 0;
                if (AGIOdata.Connected() || Properties.Settings.Default.Simulate) GSAsentence = GSA.Build();
            }
            else
            {
                Watchdog++;
                // [XPLAT] frmStart cancelled the BackgroundWorker after 10 stalled ticks; with the
                // synchronous send pump the equivalent is clearing the send-pump re-entrancy guard.
                if (Watchdog > 10)
                {
                    _sending = false;
                }
            }
        }

        private void tmrMinimize_Tick(object sender, EventArgs e) => WindowState = WindowState.Minimized;

        private void tmrRMC_Tick(object sender, EventArgs e)
        {
            if (RMCsentence == "")
            {
                Watchdog = 0;
                if (AGIOdata.Connected() || Properties.Settings.Default.Simulate) RMCsentence = RMC.Build();
            }
            else
            {
                Watchdog++;
                // [XPLAT] frmStart cancelled the BackgroundWorker after 10 stalled ticks; with the
                // synchronous send pump the equivalent is clearing the send-pump re-entrancy guard.
                if (Watchdog > 10)
                {
                    _sending = false;
                }
            }
        }

        private void tmrVTG_Tick(object sender, EventArgs e)
        {
            if (VTGsentence == "")
            {
                Watchdog = 0;
                if (AGIOdata.Connected() || Properties.Settings.Default.Simulate) VTGsentence = VTG.Build();
            }
            else
            {
                Watchdog++;
                // [XPLAT] frmStart cancelled the BackgroundWorker after 10 stalled ticks; with the
                // synchronous send pump the equivalent is clearing the send-pump re-entrancy guard.
                if (Watchdog > 10)
                {
                    _sending = false;
                }
            }
        }

        private void tmrZDA_Tick(object sender, EventArgs e)
        {
            if (ZDAsentence == "")
            {
                Watchdog = 0;
                if (AGIOdata.Connected() || Properties.Settings.Default.Simulate) ZDAsentence = ZDA.Build();
            }
            else
            {
                Watchdog++;
                // [XPLAT] frmStart cancelled the BackgroundWorker after 10 stalled ticks; with the
                // synchronous send pump the equivalent is clearing the send-pump re-entrancy guard.
                if (Watchdog > 10)
                {
                    _sending = false;
                }
            }
        }

        // ====================================================================================
        //  Event handlers (wired by name from MainWindow.axaml)
        // ====================================================================================

        private void CboPort1_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SER.PortNm = cboPort1.SelectedItem as string ?? "";
        }

        private void CboBaud1_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            string t = cboBaud1.SelectedItem as string;
            if (!string.IsNullOrEmpty(t)) SER.Baud = Convert.ToInt32(t, CultureInfo.InvariantCulture);
        }

        private void BtnConnect1_Click(object sender, RoutedEventArgs e)
        {
            if ((btnConnect1.Content as string) == "Connect") SER.Open();
            else SER.Close();
            SetPortButtons1();
        }

        private void BtnRescan_Click(object sender, RoutedEventArgs e) => LoadRCbox();

        private void BtnGGA_Click(object sender, RoutedEventArgs e)
        {
            tbGGA.Text = (AGIOdata.Connected() || Properties.Settings.Default.Simulate) ? GGA.Sentence : "";
            if (tbGGA.Text != "") CopyToClipboard(tbGGA.Text);
        }

        private void BtnVTG_Click(object sender, RoutedEventArgs e)
        {
            tbVTG.Text = (AGIOdata.Connected() || Properties.Settings.Default.Simulate) ? VTG.Sentence : "";
            if (tbVTG.Text != "") CopyToClipboard(tbVTG.Text);
        }

        private void BtnRMC_Click(object sender, RoutedEventArgs e)
        {
            tbRMC.Text = (AGIOdata.Connected() || Properties.Settings.Default.Simulate) ? RMC.Sentence : "";
            if (tbRMC.Text != "") CopyToClipboard(tbRMC.Text);
        }

        private void BtnZDA_Click(object sender, RoutedEventArgs e)
        {
            tbZDA.Text = (AGIOdata.Connected() || Properties.Settings.Default.Simulate) ? ZDA.Sentence : "";
            if (tbZDA.Text != "") CopyToClipboard(tbZDA.Text);
        }

        private void CboGGA_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyRate(cboGGA, tmrGGA);
            Properties.Settings.Default.GGA = cboGGA.SelectedIndex;
        }

        private void CboVTG_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyRate(cboVTG, tmrVTG);
            Properties.Settings.Default.VTG = cboVTG.SelectedIndex;
        }

        private void CboRMC_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyRate(cboRMC, tmrRMC);
            Properties.Settings.Default.RMC = cboRMC.SelectedIndex;
        }

        private void CboZDA_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyRate(cboZDA, tmrZDA);
            Properties.Settings.Default.ZDA = cboZDA.SelectedIndex;
        }

        private void CboPrecision_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            Properties.Settings.Default.SentencePrecisionIndex = cboPrecision.SelectedIndex;
            ApplyPrecision();
            tbGGA.Text = "";
            tbVTG.Text = "";
            tbRMC.Text = "";
            tbZDA.Text = "";
        }

        private void CkAutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Properties.Settings.Default.AutoConnect = ckAutoConnect.IsChecked == true;
        }

        private void CkAutoHide_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool on = ckAutoHide.IsChecked == true;
            if (on)
            {
                tmrMinimize.Start();
                WindowState = WindowState.Minimized;
            }
            else
            {
                tmrMinimize.Stop();
            }
            Properties.Settings.Default.AutoHide = on;
        }

        private void CkGSA_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool on = ckGSA.IsChecked == true;
            Properties.Settings.Default.SendGSA = on;
            if (on) tmrGSA.Start();
            else tmrGSA.Stop();
        }

        private void CkRoll_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Properties.Settings.Default.UseRollCorrected = ckRoll.IsChecked == true;
        }

        private void CkSimulate_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            bool on = ckSimulate.IsChecked == true;
            Properties.Settings.Default.Simulate = on;
            if (!on)
            {
                tbGGA.Text = "";
                tbVTG.Text = "";
                tbRMC.Text = "";
                tbZDA.Text = "";
            }
        }

        private void RbGP_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            Properties.Settings.Default.SentenceStart = (rbGP.IsChecked == true) ? "$GP" : "$GN";
        }
    }
}
