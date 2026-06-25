// [XPLAT] migrated from net48/WinForms Forms/FormLoop.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AgDiag.Protocol;

namespace AgDiag
{
    /// <summary>
    /// AgDiag main diagnostics window. Passive viewer that listens on the AgOpenGPS UDP loopback and
    /// renders the decoded autosteer/machine PGNs. Reimplemented as an Avalonia <see cref="Window"/>,
    /// replacing the deleted WinForms <c>FormLoop : Form</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour is ported verbatim from the WinForms original: a 250 ms timer refreshes every
    /// readout, section/switch indicators recolour (sections Green=on/Red=off; Steer/Work switches are
    /// inverted Red=on/Green=off, exactly as the source), and the UDP loopback starts on window load and
    /// stops on close. WinForms <c>BeginInvoke</c> marshalling becomes <see cref="Dispatcher.UIThread"/>
    /// posts. M3: UDP receive errors raised by <see cref="UdpCommunication.ErrorOccurred"/> are shown in a
    /// visible label rather than being silently logged.
    /// </remarks>
    public partial class FormLoop : Window
    {
        private static readonly IBrush GreenBrush = Brushes.Green;
        private static readonly IBrush RedBrush = Brushes.Red;

        private readonly Pgns _pgns;
        private readonly UdpCommunication _udpCommunication;
        private readonly DispatcherTimer _timer;

        // Cached control references (resolved once after XAML load; updated every 250 ms tick).
        private Border[] _sections;
        private Border _steerSwitch;
        private Border _workSwitch;
        private TextBlock _setSteerAngle, _speed, _status, _steerDataPGN;
        private TextBlock _steerAngleActual, _heading, _roll, _pwm, _pgnFromModule;
        private TextBlock _pgnSteerSettings, _p, _hiPWM, _loPWM, _minPWM, _cpd, _ackerman, _offset;
        private TextBlock _pgnAutoSteerConfig, _set0, _pulseCount, _minSpeed;
        private TextBlock _pngMachine, _treeLbl, _defaultSends, _udpError;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormLoop"/> class, wires the UDP loopback and the
        /// 250 ms refresh timer, and resolves the readout controls.
        /// </summary>
        public FormLoop()
        {
            _pgns = new Pgns();
            _udpCommunication = new UdpCommunication(_pgns);

            // [XPLAT] Marshal background-thread callbacks onto the UI thread (was WinForms BeginInvoke).
            _udpCommunication.DefaultSendsUpdated += (s, e) =>
                Dispatcher.UIThread.Post(() => UpdateDefaultSends(e));
            _udpCommunication.ErrorOccurred += (s, message) =>
                Dispatcher.UIThread.Post(() => ShowUdpError(message));

            InitializeComponent();
            ResolveControls();

            // [XPLAT] WinForms System.Windows.Forms.Timer(Interval=250) -> Avalonia DispatcherTimer.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer1_Tick;

            Loaded += FormLoop_Loaded;
            Closing += FormLoop_Closing;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ResolveControls()
        {
            _sections = new Border[9]; // 1-based; index 0 unused
            for (int i = 1; i <= 8; i++)
            {
                _sections[i] = this.FindControl<Border>("lblSection" + i);
            }

            _steerSwitch = this.FindControl<Border>("lblSteerSwitch");
            _workSwitch = this.FindControl<Border>("lblWorkSwitch");

            _setSteerAngle = this.FindControl<TextBlock>("lblSetSteerAngle");
            _speed = this.FindControl<TextBlock>("lblSpeed");
            _status = this.FindControl<TextBlock>("lblStatus");
            _steerDataPGN = this.FindControl<TextBlock>("lblSteerDataPGN");

            _steerAngleActual = this.FindControl<TextBlock>("lblSteerAngleActual");
            _heading = this.FindControl<TextBlock>("lblHeading");
            _roll = this.FindControl<TextBlock>("lblRoll");
            _pwm = this.FindControl<TextBlock>("lblPWM");
            _pgnFromModule = this.FindControl<TextBlock>("lblPGNFromAutosteerModule");

            _pgnSteerSettings = this.FindControl<TextBlock>("lblPGNSteerSettings");
            _p = this.FindControl<TextBlock>("lblP");
            _hiPWM = this.FindControl<TextBlock>("lblHiPWM");
            _loPWM = this.FindControl<TextBlock>("lblLoPWM");
            _minPWM = this.FindControl<TextBlock>("lblMinPWM");
            _cpd = this.FindControl<TextBlock>("lblCPD");
            _ackerman = this.FindControl<TextBlock>("lblAckerman");
            _offset = this.FindControl<TextBlock>("lblOffset");

            _pgnAutoSteerConfig = this.FindControl<TextBlock>("lblPGNAutoSteerConfig");
            _set0 = this.FindControl<TextBlock>("lblSet0");
            _pulseCount = this.FindControl<TextBlock>("lblPulseCount");
            _minSpeed = this.FindControl<TextBlock>("lblMinSpeed");

            _pngMachine = this.FindControl<TextBlock>("lblPNGMachine");
            _treeLbl = this.FindControl<TextBlock>("TreeLbl");
            _defaultSends = this.FindControl<TextBlock>("lblDefaultSends");
            _udpError = this.FindControl<TextBlock>("lblUdpError");
        }

        // [XPLAT] Verbatim port of the WinForms timer1_Tick readout refresh.
        private void Timer1_Tick(object sender, EventArgs e)
        {
            for (int i = 1; i <= 8; i++)
            {
                _sections[i].Background = _pgns.asData.IsSectionOn(i) ? GreenBrush : RedBrush;
            }

            _speed.Text = _pgns.asData.Speed.ToString();
            _setSteerAngle.Text = _pgns.asData.SteerAngle.ToString();
            _status.Text = _pgns.asData.Status.ToString();

            _steerDataPGN.Text = _pgns.asData.ToHexString();

            // from autosteer module
            _steerAngleActual.Text = _pgns.asModule.ActualSteerAngle.ToString();
            _heading.Text = _pgns.asModule.Heading.ToString();
            _roll.Text = _pgns.asModule.Roll.ToString();
            _pwm.Text = _pgns.asModule.PWM.ToString();

            // [XPLAT] Switch indicators are intentionally inverted (on = Red), matching the source.
            _workSwitch.Background = _pgns.asModule.IsWorkSwitchOn ? RedBrush : GreenBrush;
            _steerSwitch.Background = _pgns.asModule.IsSteerSwitchOn ? RedBrush : GreenBrush;

            _pgnFromModule.Text = _pgns.asModule.ToHexString();

            // autosteer settings
            _pgnSteerSettings.Text = _pgns.asSet.ToHexString();

            _p.Text = _pgns.asSet.GainProportional.ToString();
            _hiPWM.Text = _pgns.asSet.HighPWM.ToString();
            _loPWM.Text = _pgns.asSet.LowPWM.ToString();
            _minPWM.Text = _pgns.asSet.MinPWM.ToString();
            _cpd.Text = _pgns.asSet.CountsPerDegree.ToString();
            _ackerman.Text = _pgns.asSet.Ackerman.ToString();
            _offset.Text = _pgns.asSet.SteerOffset.ToString();

            // autosteer config bytes
            _pgnAutoSteerConfig.Text = _pgns.asConfig.ToHexString();

            _set0.Text = _pgns.asConfig.Set0.ToString();
            _pulseCount.Text = _pgns.asConfig.MaxPulse.ToString();
            _minSpeed.Text = _pgns.asConfig.MinSpeed.ToString();

            // machine bytes
            _pngMachine.Text = _pgns.maData.ToHexString();

            _treeLbl.Text = _pgns.maData.Speed.ToString();
        }

        private void UpdateDefaultSends(int defaultSends)
        {
            _defaultSends.Text = defaultSends.ToString();
        }

        // [XPLAT] M3: render UDP/loopback receive failures in a visible label for the operator.
        private void ShowUdpError(string message)
        {
            _udpError.Text = message;
        }

        private void FormLoop_Loaded(object sender, RoutedEventArgs e)
        {
            _timer.Start();
            _udpCommunication.LoadLoopback();
        }

        private void FormLoop_Closing(object sender, WindowClosingEventArgs e)
        {
            _timer.Stop();
            _udpCommunication.CloseLoopback();
        }
    }
}
