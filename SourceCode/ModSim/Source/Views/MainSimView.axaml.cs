// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// ModSim main window (Avalonia). 1:1 behavioural port of the deleted WinForms FormSim, whose
// logic lived across three partial files:
//   Forms/FormSim.cs            -> lifecycle (Load/FormClosing) + local-IP enumeration
//   Forms/Controls.Designer.cs  -> UI event handlers + the 100 ms GPS-simulator tick + NMEA builders
//   Forms/UDP.designer.cs       -> UDP socket plumbing + AgIO PGN parsing
// To keep each concern readable the port is likewise split into three partials of the single
// "partial class MainSimView : Window":
//   MainSimView.axaml.cs  (this file) -> lifecycle, UI handlers, GPS-sim tick, sim state
//   MainSimView.Nmea.cs               -> NMEA sentence builders + checksum + position math
//   MainSimView.Udp.cs                -> sockets, send/receive, PGN parsing, swap-bit table
//
// STANDALONE: no ProjectReference, no AgOpenGPS.Core, pure code-behind against the generated
// x:Name fields (the Avalonia source generator emits InitializeComponent() + the typed fields).
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ModSim.Properties;

namespace ModSim.Views
{
    /// <summary>
    /// The ModSim standalone module-simulator main window. Drives a 100 ms GPS simulation loop that
    /// emits the user-selected NMEA sentences over UDP loopback to AgIO, and decodes the AutoSteer /
    /// Machine / IMU PGNs received back, reproducing the original WinForms <c>FormSim</c> exactly.
    /// </summary>
    public partial class MainSimView : Window
    {
        // [XPLAT] The GPS-simulator state fields (TimeNow, latitude/longitude, speed, heading,
        // roll, the eight NMEA StringBuilders, etc.) and the OnSimTimerTick heartbeat live in the
        // MainSimView.Nmea.cs partial of this same class; the UI handlers below assign them and the
        // tick consumes them. Declared there exactly once to keep this file focused on lifecycle + UI.

        // [XPLAT] WinForms Timer (default 100 ms, Enabled=true) -> Avalonia DispatcherTimer.
        private DispatcherTimer simTimer;

        // [XPLAT] Avalonia Slider.ValueChanged fires on *programmatic* value changes too, whereas the
        // WinForms TrackBar.Scroll event fired only on genuine user interaction. These two guards
        // reproduce the original semantics: _isLoaded suppresses the spurious events raised while the
        // XAML is still initialising, and _suppressSliderEvents brackets every programmatic Value
        // assignment we make ourselves (the "click-to-zero" handlers and the guided tick branch).
        private bool _isLoaded;
        private bool _suppressSliderEvents;

        // [XPLAT] The WinForms code compared btnSteerButtonRemote.BackColor == Color.Green to decide
        // whether to reset it to white on the next PGN 254. Comparing brush instances is brittle, so
        // the "is currently green" state is tracked with an explicit flag instead.
        private bool _steerButtonRemoteGreen;

        /// <summary>Initialises the window and wires the WinForms Load/FormClosing equivalents.</summary>
        public MainSimView()
        {
            InitializeComponent();

            // [XPLAT] WinForms this.Load / this.FormClosing -> Avalonia Loaded / Closing.
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        // [XPLAT] WinForms FormSim_Load: restore persisted settings, populate the readouts, open the
        // UDP socket, then start the simulation timer.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            cboxGGA.IsChecked = Settings.Default.isGGA;
            cboxVTG.IsChecked = Settings.Default.isVTG;
            cboxAVR.IsChecked = Settings.Default.isAVR;
            cboxHDT.IsChecked = Settings.Default.isHDT;
            cboxRMC.IsChecked = Settings.Default.isRMC;
            cboxOGI.IsChecked = Settings.Default.isOGI;
            cboxNDA.IsChecked = Settings.Default.isNDA;
            cboxKSXT.IsChecked = Settings.Default.isKSXT;

            latitude = Settings.Default.setGPS_SimLatitude;
            nudLat.Value = (decimal)Settings.Default.setGPS_SimLatitude;
            longitude = Settings.Default.setGPS_SimLongitude;
            nudLon.Value = (decimal)Settings.Default.setGPS_SimLongitude;

            lblIPSet1.Text = Settings.Default.etIP_SubnetOne.ToString(CultureInfo.InvariantCulture);
            lblIPSet2.Text = Settings.Default.etIP_SubnetTwo.ToString(CultureInfo.InvariantCulture);
            lblIPSet3.Text = Settings.Default.etIP_SubnetThree.ToString(CultureInfo.InvariantCulture);

            lblScanReply.Text = "No";

            LoadUDPNetwork();

            // [XPLAT] Start the GPS-sim loop (WinForms simTimer.Enabled = true, default Interval 100 ms).
            simTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            simTimer.Tick += OnSimTimerTick;
            simTimer.Start();

            // Genuine user slider interaction is enabled only after the initial layout has settled.
            _isLoaded = true;
        }

        // [XPLAT] WinForms FormSim_FormClosing: persist the sentence selections and tear the socket down.
        private void OnClosing(object sender, WindowClosingEventArgs e)
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

            simTimer?.Stop();

            if (UDPSocket != null)
            {
                try
                {
                    UDPSocket.Shutdown(SocketShutdown.Both);
                }
                finally { UDPSocket.Close(); }
            }
        }

        // [XPLAT] WinForms lblIP_Click: enumerate this host's IPv4 addresses into the label.
        // The original was a Click handler; here it is the TextBlock's Tapped handler.
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

        // ----- UI event handlers (ported from Controls.Designer.cs) ------------------------------

        // [XPLAT] btnSave_Click: persist the simulated start coordinate.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.setGPS_SimLatitude = (double)(nudLat.Value ?? 0);
            Settings.Default.setGPS_SimLongitude = (double)(nudLon.Value ?? 0);
            Settings.Default.Save();
            latitude = Settings.Default.setGPS_SimLatitude;
            longitude = Settings.Default.setGPS_SimLongitude;
        }

        // [XPLAT] lblWAS_Click: zero the simulated steer angle.
        private void OnWasLabelClick(object sender, TappedEventArgs e)
        {
            _suppressSliderEvents = true;
            tbarSteerAngleWAS.Value = 0;
            _suppressSliderEvents = false;
            steerAngle = 0;
            lblWAS.Text = "Steer: 0.0°";
        }

        // [XPLAT] lblKmh_Click: zero the simulated speed.
        private void OnKmhLabelClick(object sender, TappedEventArgs e)
        {
            _suppressSliderEvents = true;
            tbarSpeed.Value = 0;
            _suppressSliderEvents = false;
            lblKmh.Text = "Kmh: 0.0";
            mSec.Text = "M/Sec: 0.0";
        }

        // [XPLAT] tbarSteerAngleWAS_Scroll: user dragged the steer-angle slider.
        private void OnSteerAngleChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_isLoaded || _suppressSliderEvents) return;

            steerAngleActual = (int)tbarSteerAngleWAS.Value * 0.01;
            lblWAS.Text = "Steer: " + steerAngleActual.ToString("N2") + "°";
        }

        // [XPLAT] tbarSpeed_Scroll: user dragged the speed slider.
        private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_isLoaded || _suppressSliderEvents) return;

            if ((int)tbarSpeed.Value < 0) lblKmh.Background = Brushes.Salmon;
            else lblKmh.Background = Brushes.LightGreen;

            lblKmh.Text = "Kmh: " + ((int)tbarSpeed.Value * 0.1).ToString("N1");
            mSec.Text = "M/Sec: " + ((int)tbarSpeed.Value * 0.027777777777).ToString("N1");
        }

        // [XPLAT] tbarRoll_Scroll: user dragged the roll slider.
        private void OnRollChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_isLoaded || _suppressSliderEvents) return;

            roll = (int)tbarRoll.Value * 0.1;
            rollIMU = (int)(roll * 10);
            lblRoll.Text = "Roll: " + roll.ToString("N2") + "°";
        }

        // [XPLAT] lblRoll_Click: zero the simulated roll.
        private void OnRollLabelClick(object sender, TappedEventArgs e)
        {
            roll = 0;
            rollIMU = 0;
            lblRoll.Text = "Roll: 0°";
            _suppressSliderEvents = true;
            tbarRoll.Value = 0;
            _suppressSliderEvents = false;
        }

        // [XPLAT] btnSteerButtonRemote_Click: momentary steer-button press.
        private void OnSteerButtonRemoteClick(object sender, RoutedEventArgs e)
        {
            if (steerSwitch > 0) steerSwitch = 0;
            else steerSwitch = 1;
            btnSteerButtonRemote.Background = Brushes.Green;
            _steerButtonRemoteGreen = true;
        }

        // [XPLAT] cboxSteerSwitchRemote_Click: latching steer switch (now an Avalonia ToggleButton).
        private void OnSteerSwitchRemoteClick(object sender, RoutedEventArgs e)
        {
            if (cboxSteerSwitchRemote.IsChecked == true) steerSwitch = 0;
            else steerSwitch = 1;
        }

        // [XPLAT] cboxWorkSwitch_Click: latching work switch (now an Avalonia ToggleButton).
        private void OnWorkSwitchClick(object sender, RoutedEventArgs e)
        {
            if (cboxWorkSwitch.IsChecked == true) workSwitch = 0;
            else workSwitch = 1;
        }

        // [XPLAT] M10 — keyboard-accessible equivalent for the tap-only readout labels. Activating a
        // focused label with Enter or Space invokes the same action as a pointer tap, so the controls
        // are operable without a touchscreen / mouse and are exposed to assistive technology.
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

        // ----- Modal helpers (ported from Controls.Designer.cs) ----------------------------------

        // [XPLAT] WinForms TimedMessageBox -> show the auto-closing Avalonia toast window.
        public void TimedMessageBox(int timeout, string title, string message)
        {
            var form = new FormTimedMessageView(timeout, title, message);
            form.Show();
        }

        // [XPLAT] WinForms YesMessageBox (modal ShowDialog) -> async ShowDialog on the Avalonia window.
        public System.Threading.Tasks.Task YesMessageBox(string s1)
        {
            var form = new FormYesView(s1);
            return form.ShowDialog(this);
        }

    }
}
