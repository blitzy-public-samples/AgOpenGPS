// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// ModSim standalone module-simulator — main window (Avalonia). This is the primary code-behind
// partial of the single "partial class MainSimView : Window"; it is a 1:1 behavioural port of the
// deleted WinForms FormSim, whose logic lived across three source files:
//   Forms/FormSim.cs            -> window lifecycle (Load / FormClosing) + local-IP enumeration
//   Forms/Controls.Designer.cs  -> the interactive UI handlers + the two message-box helpers
//   Forms/UDP.designer.cs       -> UDP sockets + AgIO PGN parsing
// The port keeps the same separation across three partials of one class:
//   MainSimView.axaml.cs (this file) -> ctor, OnOpened/OnClosing lifecycle, UI handlers, dialogs
//   MainSimView.Nmea.cs               -> GPS-simulator engine, the ~100 ms tick, NMEA builders
//   MainSimView.Udp.cs                -> sockets, send/receive, PGN parsing
// Because the three files form one class they freely reference each other's members: this file
// uses latitude/longitude/steerAngle/roll/rollIMU and OnSimTimerTick declared in Nmea.cs, and
// UDPSocket/steerSwitch/workSwitch/steerAngleActual/LoadUDPNetwork plus the _steerButtonRemoteGreen
// flag declared in Udp.cs.
//
// STANDALONE: no ProjectReference, no AgOpenGPS.Core, no IPlatformServices, no MVVM — pure
// code-behind against the typed x:Name fields emitted by the Avalonia XAML source generator.
using System;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ModSim.Properties;

namespace ModSim.Views;

/// <summary>
/// The ModSim standalone module-simulator main window. Drives a ~100 ms GPS-simulation loop that
/// emits the user-selected NMEA sentences over UDP loopback to AgIO and decodes the AutoSteer /
/// Machine / IMU PGNs received back, reproducing the original WinForms <c>FormSim</c> exactly.
/// </summary>
public partial class MainSimView : Window
{
    // [XPLAT] WinForms simTimer (designer Enabled=true with no explicit Interval -> the WinForms
    // Timer default of 100 ms) -> Avalonia DispatcherTimer. Created and wired here in the
    // constructor and started from OnOpened, so the heartbeat begins only once the window is shown
    // (mirroring the WinForms pump, which ticked once the form became visible). The Tick target,
    // OnSimTimerTick, lives in the MainSimView.Nmea.cs partial of this same class.
    private readonly DispatcherTimer simTimer;

    // [XPLAT] Avalonia Slider.ValueChanged fires for *programmatic* Value writes too, whereas the
    // WinForms TrackBar.Scroll event fired only on genuine user interaction. Every programmatic
    // Value assignment we make (the click-to-zero handlers here and the guided branch in the sim
    // tick) is bracketed by this guard so it does not re-enter the ValueChanged handlers. Declared
    // here and also read by MainSimView.Nmea.cs.
    private bool _suppressSliderEvents;

    // [XPLAT] The WinForms code compared btnSteerButtonRemote.BackColor == Color.Green to decide
    // whether to reset the button to white on the next PGN 254. Comparing brush instances is
    // unreliable, so the "is currently green" state is tracked with this explicit flag instead;
    // MainSimView.Udp.cs reads and clears it in the PGN-254 handler.
    private bool _steerButtonRemoteGreen;

    /// <summary>
    /// Initialises the window: loads the compiled XAML, applies a best-effort window icon, and
    /// wires (but does not start) the GPS-simulation heartbeat timer. A public parameterless
    /// constructor is required because <c>App.axaml.cs</c> creates the window via <c>new MainSimView()</c>.
    /// </summary>
    public MainSimView()
    {
        InitializeComponent();

        // [XPLAT] Best-effort window icon. Graceful by design: if ModSim_ico.ico cannot be loaded
        // (e.g. not packaged as an AvaloniaResource, or undecodable on the host) startup must not
        // break — the AAP mandates graceful degradation for non-portable optional features.
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ModSim/ModSim_ico.ico")));
        }
        catch
        {
            // Icon is optional; never break startup.
        }

        // [XPLAT] WinForms simTimer (Enabled=true, default 100 ms) -> Avalonia DispatcherTimer.
        // Started from OnOpened; OnSimTimerTick is defined in MainSimView.Nmea.cs.
        simTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        simTimer.Tick += OnSimTimerTick;
    }

    /// <summary>
    /// Ports the WinForms <c>FormSim_Load</c>: restores the persisted sentence selections and the
    /// simulated start coordinate, populates the read-outs, opens the UDP socket, and starts the
    /// simulation heartbeat. Runs once when the window is first shown.
    /// </summary>
    /// <param name="e">The opened-event payload, forwarded to the base implementation.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        cboxGGA.IsChecked = Settings.Default.isGGA;
        cboxVTG.IsChecked = Settings.Default.isVTG;
        cboxAVR.IsChecked = Settings.Default.isAVR;
        cboxHDT.IsChecked = Settings.Default.isHDT;
        cboxRMC.IsChecked = Settings.Default.isRMC;
        cboxOGI.IsChecked = Settings.Default.isOGI;
        cboxNDA.IsChecked = Settings.Default.isNDA;
        cboxKSXT.IsChecked = Settings.Default.isKSXT;

        // latitude / longitude are declared in MainSimView.Nmea.cs.
        latitude = Settings.Default.setGPS_SimLatitude;
        nudLat.Value = (decimal)Settings.Default.setGPS_SimLatitude;
        longitude = Settings.Default.setGPS_SimLongitude;
        nudLon.Value = (decimal)Settings.Default.setGPS_SimLongitude;

        // [XPLAT] Display text ported verbatim: the subnet octets are bytes and the source used a
        // plain ToString(); no CultureInfo is added (integers format identically and adding one
        // would be an unmandated behaviour change).
        lblIPSet1.Text = Settings.Default.etIP_SubnetOne.ToString();
        lblIPSet2.Text = Settings.Default.etIP_SubnetTwo.ToString();
        lblIPSet3.Text = Settings.Default.etIP_SubnetThree.ToString();

        lblScanReply.Text = "No";

        // LoadUDPNetwork is defined in MainSimView.Udp.cs.
        LoadUDPNetwork();

        // Begin the ~100 ms sim heartbeat after the window is shown (WinForms simTimer.Enabled=true).
        simTimer.Start();
    }

    /// <summary>
    /// Ports the WinForms <c>FormSim_FormClosing</c>: persists the sentence selections, stops the
    /// simulation timer, and tears the UDP socket down before the window closes.
    /// </summary>
    /// <param name="e">The closing-event payload, forwarded to the base implementation.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Settings.Default.isGGA = cboxGGA.IsChecked == true;
        Settings.Default.isVTG = cboxVTG.IsChecked == true;
        Settings.Default.isAVR = cboxAVR.IsChecked == true;
        Settings.Default.isHDT = cboxHDT.IsChecked == true;
        Settings.Default.isRMC = cboxRMC.IsChecked == true;
        Settings.Default.isOGI = cboxOGI.IsChecked == true;
        Settings.Default.isNDA = cboxNDA.IsChecked == true;
        Settings.Default.isKSXT = cboxKSXT.IsChecked == true;
        Settings.Default.Save();

        simTimer.Stop();

        // UDPSocket is declared in MainSimView.Udp.cs.
        if (UDPSocket != null)
        {
            try
            {
                UDPSocket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
                // [XPLAT] The simulator's UDPSocket is a connectionless datagram socket (SocketType.Dgram)
                // that is bound but never Connect()ed. Calling Shutdown() on it throws ENOTCONN on
                // Linux/macOS (and WSAENOTCONN on Windows). The WinForms original ran Windows-only and
                // never surfaced this, so swallow the expected socket error to keep window close graceful
                // on every platform (AAP graceful-degradation). Close() in the finally still releases it.
            }
            finally
            {
                UDPSocket.Close();
            }
        }

        base.OnClosing(e);
    }

    // ----- IP read-out (ported from FormSim.lblIP_Click) ----------------------------------------

    /// <summary>
    /// Ports the WinForms <c>lblIP_Click</c>: lists this host's IPv4 addresses into the IP label.
    /// Wired as the label's <c>Tapped</c> handler in the XAML.
    /// </summary>
    private void OnIpLabelClick(object sender, TappedEventArgs e)
    {
        lblIP.Text = "";
        foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (IPA.AddressFamily == AddressFamily.InterNetwork)
            {
                lblIP.Text += IPA.ToString() + "\r\n";
            }
        }
    }

    // ----- Interactive UI handlers (ported from Controls.Designer.cs lines 10-79) ----------------

    /// <summary>Ports <c>btnSave_Click</c>: persists the simulated start coordinate.</summary>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Settings.Default.setGPS_SimLatitude = (double)(nudLat.Value ?? 0m);
        Settings.Default.setGPS_SimLongitude = (double)(nudLon.Value ?? 0m);
        Settings.Default.Save();
        latitude = Settings.Default.setGPS_SimLatitude;
        longitude = Settings.Default.setGPS_SimLongitude;
    }

    /// <summary>Ports <c>lblWAS_Click</c>: zeroes the simulated steer angle.</summary>
    private void OnWasLabelClick(object sender, TappedEventArgs e)
    {
        _suppressSliderEvents = true;
        tbarSteerAngleWAS.Value = 0;
        _suppressSliderEvents = false;
        steerAngle = 0;
        lblWAS.Text = "Steer: 0.0°";
    }

    /// <summary>Ports <c>lblKmh_Click</c>: zeroes the simulated speed.</summary>
    private void OnKmhLabelClick(object sender, TappedEventArgs e)
    {
        _suppressSliderEvents = true;
        tbarSpeed.Value = 0;
        _suppressSliderEvents = false;
        lblKmh.Text = "Kmh: 0.0";
        mSec.Text = "M/Sec: 0.0";
    }

    /// <summary>Ports <c>lblRoll_Click</c>: zeroes the simulated roll.</summary>
    private void OnRollLabelClick(object sender, TappedEventArgs e)
    {
        roll = 0;
        rollIMU = 0;
        lblRoll.Text = "Roll: 0°";
        _suppressSliderEvents = true;
        tbarRoll.Value = 0;
        _suppressSliderEvents = false;
    }

    /// <summary>
    /// Ports <c>tbarSteerAngleWAS_Scroll</c>: the user dragged the steer-angle slider. The Slider
    /// value is read back as <c>int</c> to preserve the original integer TrackBar maths.
    /// </summary>
    private void OnSteerAngleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // WinForms Scroll did not fire on programmatic writes; mirror that here.
        if (_suppressSliderEvents) return;

        // steerAngleActual is declared in MainSimView.Udp.cs.
        steerAngleActual = (int)tbarSteerAngleWAS.Value * 0.01;
        lblWAS.Text = "Steer: " + steerAngleActual.ToString("N2") + "°";
    }

    /// <summary>Ports <c>tbarSpeed_Scroll</c>: the user dragged the speed slider.</summary>
    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSliderEvents) return;

        int v = (int)tbarSpeed.Value;
        if (v < 0) lblKmh.Background = Brushes.Salmon;
        else lblKmh.Background = Brushes.LightGreen;

        lblKmh.Text = "Kmh: " + (v * 0.1).ToString("N1");
        mSec.Text = "M/Sec: " + (v * 0.027777777777).ToString("N1");
    }

    /// <summary>Ports <c>tbarRoll_Scroll</c>: the user dragged the roll slider.</summary>
    private void OnRollChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSliderEvents) return;

        // roll / rollIMU are declared in MainSimView.Nmea.cs.
        roll = (int)tbarRoll.Value * 0.1;
        rollIMU = (int)(roll * 10);
        lblRoll.Text = "Roll: " + roll.ToString("N2") + "°";
    }

    /// <summary>Ports <c>btnSteerButtonRemote_Click</c>: momentary steer-button press.</summary>
    private void OnSteerButtonRemoteClick(object sender, RoutedEventArgs e)
    {
        // steerSwitch is declared in MainSimView.Udp.cs.
        if (steerSwitch > 0) steerSwitch = 0;
        else steerSwitch = 1;

        btnSteerButtonRemote.Background = Brushes.Green;
        _steerButtonRemoteGreen = true;
    }

    /// <summary>Ports <c>cboxSteerSwitchRemote_Click</c>: latching steer switch (Avalonia ToggleButton).</summary>
    private void OnSteerSwitchRemoteClick(object sender, RoutedEventArgs e)
    {
        if (cboxSteerSwitchRemote.IsChecked == true) steerSwitch = 0;
        else steerSwitch = 1;
    }

    /// <summary>Ports <c>cboxWorkSwitch_Click</c>: latching work switch (Avalonia ToggleButton).</summary>
    private void OnWorkSwitchClick(object sender, RoutedEventArgs e)
    {
        // workSwitch is declared in MainSimView.Udp.cs.
        if (cboxWorkSwitch.IsChecked == true) workSwitch = 0;
        else workSwitch = 1;
    }

    // [XPLAT] M10 — keyboard-accessible equivalent for the tap-only read-out labels. Activating a
    // focused label with Enter or Space invokes the same action as a pointer tap, so the controls
    // are operable without a touchscreen / mouse and are exposed to assistive technology. Wired as
    // the KeyDown handler on lblIP / lblWAS / lblKmh / lblRoll in the XAML.
    private void OnClickableKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space) return;

        if (ReferenceEquals(sender, lblIP)) OnIpLabelClick(sender, null);
        else if (ReferenceEquals(sender, lblWAS)) OnWasLabelClick(sender, null);
        else if (ReferenceEquals(sender, lblKmh)) OnKmhLabelClick(sender, null);
        else if (ReferenceEquals(sender, lblRoll)) OnRollLabelClick(sender, null);
        else return;

        e.Handled = true;
    }

    // ----- Dialog helpers (ported from Controls.Designer.cs lines 81-91) -------------------------

    /// <summary>
    /// Ports the WinForms <c>TimedMessageBox</c>: shows the auto-closing Avalonia toast popup.
    /// Fire-and-forget — the popup closes itself after the supplied timeout. Called from
    /// MainSimView.Udp.cs.
    /// </summary>
    private void TimedMessageBox(int timeout, string title, string message)
    {
        var form = new FormTimedMessageView(timeout, title, message);
        form.Show();
    }

    /// <summary>
    /// Ports the WinForms <c>YesMessageBox</c>: shows the modal acknowledge-only dialog. The
    /// <c>bool</c> dialog result is intentionally ignored (the original used a modal ShowDialog for
    /// acknowledgement only). Awaited by MainSimView.Udp.cs before the cross-platform restart.
    /// </summary>
    private async System.Threading.Tasks.Task YesMessageBox(string s1)
    {
        var form = new FormYesView(s1);
        await form.ShowDialog<bool>(this);
    }
}
