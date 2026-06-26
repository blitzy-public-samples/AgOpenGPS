// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgDiag.Protocol;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgDiag;

/// <summary>
/// AgDiag main diagnostics window: a passive viewer that listens on the AgOpenGPS UDP loopback and
/// renders the decoded AutoSteer/Machine PGNs. Reimplemented as an Avalonia <see cref="Window"/>,
/// replacing the original Windows Forms <c>FormLoop : Form</c>.
/// </summary>
/// <remarks>
/// [XPLAT] Behaviour is a 1:1 port of the WinForms original (AAP §0.3.3): a 250 ms timer refreshes
/// every readout; the eight section indicators colour Green when on and Red when off, while the
/// Steer and Work switch indicators are intentionally inverted (Red when on, Green when off),
/// exactly as the source. The UDP loopback starts when the window opens and stops when it closes.
/// WinForms <c>BeginInvoke</c> marshalling is replaced with <see cref="Dispatcher"/> UI-thread
/// posts, and every numeric readout is formatted with <see cref="CultureInfo.InvariantCulture"/>
/// (AAP §0.6.5) so the displayed digits never vary by locale. This is the code-behind half of the
/// partial class; the matching layout lives in FormLoop.axaml, whose Avalonia source generator
/// supplies <c>InitializeComponent</c> and the strongly-typed <c>x:Name</c> control fields.
/// </remarks>
public partial class FormLoop : Window
{
    private readonly Pgns _pgns;
    private readonly UdpCommunication _udpCommunication;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormLoop"/> class, wiring the UDP loopback
    /// callbacks and the 250 ms diagnostic refresh timer.
    /// </summary>
    public FormLoop()
    {
        InitializeComponent();

        _pgns = new Pgns();
        _udpCommunication = new UdpCommunication(_pgns);

        // [XPLAT] WinForms BeginInvoke((MethodInvoker)(...)) -> Dispatcher.UIThread.Post(...).
        _udpCommunication.DefaultSendsUpdated += (sender, e) =>
            Dispatcher.UIThread.Post(() => UpdateDefaultSends(e));

        // [XPLAT] WinForms System.Windows.Forms.Timer (Interval = 250) -> Avalonia DispatcherTimer.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += timer1_Tick;
    }

    /// <summary>
    /// [XPLAT] Replaces the WinForms <c>FormLoop_Load</c> handler: starts the refresh timer and
    /// opens the UDP loopback once the window has been shown.
    /// </summary>
    /// <param name="e">The event data for the window-opened notification.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _timer.Start();
        _udpCommunication.LoadLoopback();
    }

    /// <summary>
    /// [XPLAT] Replaces the WinForms <c>FormLoop_FormClosing</c> handler: closes the UDP loopback
    /// and stops the refresh timer as the window closes.
    /// </summary>
    /// <param name="e">The event data for the window-closing notification.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _udpCommunication.CloseLoopback();
        _timer.Stop();
        base.OnClosing(e);
    }

    // [XPLAT] Verbatim port of the WinForms timer1_Tick readout refresh. Numeric values use
    // CultureInfo.InvariantCulture (AAP §0.6.5); ToHexString() returns a culture-independent string.
    private void timer1_Tick(object sender, EventArgs e)
    {
        lblSection1.Background = _pgns.asData.IsSectionOn(1) ? Brushes.Green : Brushes.Red;
        lblSection2.Background = _pgns.asData.IsSectionOn(2) ? Brushes.Green : Brushes.Red;
        lblSection3.Background = _pgns.asData.IsSectionOn(3) ? Brushes.Green : Brushes.Red;
        lblSection4.Background = _pgns.asData.IsSectionOn(4) ? Brushes.Green : Brushes.Red;

        lblSection5.Background = _pgns.asData.IsSectionOn(5) ? Brushes.Green : Brushes.Red;
        lblSection6.Background = _pgns.asData.IsSectionOn(6) ? Brushes.Green : Brushes.Red;
        lblSection7.Background = _pgns.asData.IsSectionOn(7) ? Brushes.Green : Brushes.Red;
        lblSection8.Background = _pgns.asData.IsSectionOn(8) ? Brushes.Green : Brushes.Red;

        lblSpeed.Text = _pgns.asData.Speed.ToString(CultureInfo.InvariantCulture);
        lblSetSteerAngle.Text = _pgns.asData.SteerAngle.ToString(CultureInfo.InvariantCulture);
        lblStatus.Text = _pgns.asData.Status.ToString(CultureInfo.InvariantCulture);

        lblSteerDataPGN.Text = _pgns.asData.ToHexString();

        // from autosteer module
        lblSteerAngleActual.Text = _pgns.asModule.ActualSteerAngle.ToString(CultureInfo.InvariantCulture);
        lblHeading.Text = _pgns.asModule.Heading.ToString(CultureInfo.InvariantCulture);
        lblRoll.Text = _pgns.asModule.Roll.ToString(CultureInfo.InvariantCulture);
        lblPWM.Text = _pgns.asModule.PWM.ToString(CultureInfo.InvariantCulture);

        // [XPLAT] Switch indicators are intentionally inverted (on = Red), matching the source.
        lblWorkSwitch.Background = _pgns.asModule.IsWorkSwitchOn ? Brushes.Red : Brushes.Green;
        lblSteerSwitch.Background = _pgns.asModule.IsSteerSwitchOn ? Brushes.Red : Brushes.Green;

        lblPGNFromAutosteerModule.Text = _pgns.asModule.ToHexString();

        // autosteer settings
        lblPGNSteerSettings.Text = _pgns.asSet.ToHexString();

        lblP.Text = _pgns.asSet.GainProportional.ToString(CultureInfo.InvariantCulture);
        lblHiPWM.Text = _pgns.asSet.HighPWM.ToString(CultureInfo.InvariantCulture);
        lblLoPWM.Text = _pgns.asSet.LowPWM.ToString(CultureInfo.InvariantCulture);
        lblMinPWM.Text = _pgns.asSet.MinPWM.ToString(CultureInfo.InvariantCulture);
        lblCPD.Text = _pgns.asSet.CountsPerDegree.ToString(CultureInfo.InvariantCulture);
        lblAckerman.Text = _pgns.asSet.Ackerman.ToString(CultureInfo.InvariantCulture);
        lblOffset.Text = _pgns.asSet.SteerOffset.ToString(CultureInfo.InvariantCulture);

        // autosteer config bytes
        lblPGNAutoSteerConfig.Text = _pgns.asConfig.ToHexString();

        lblSet0.Text = _pgns.asConfig.Set0.ToString(CultureInfo.InvariantCulture);
        lblPulseCount.Text = _pgns.asConfig.MaxPulse.ToString(CultureInfo.InvariantCulture);
        lblMinSpeed.Text = _pgns.asConfig.MinSpeed.ToString(CultureInfo.InvariantCulture);

        // machine bytes
        lblPNGMachine.Text = _pgns.maData.ToHexString();

        TreeLbl.Text = _pgns.maData.Speed.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Updates the NTRIP UDP "default sends" byte-count readout. Invoked on the UI thread in
    /// response to <see cref="UdpCommunication.DefaultSendsUpdated"/>.
    /// </summary>
    /// <param name="defaultSends">The cumulative byte count reported by the loopback receiver.</param>
    private void UpdateDefaultSends(int defaultSends)
    {
        lblDefaultSends.Text = defaultSends.ToString(CultureInfo.InvariantCulture);
    }
}
