// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormGraphXTE.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the cross-track-error (XTE) / PWM live graph dialog.
//
// Parity notes vs the WinForms original (FormGraphXTE):
//   * The WinForms form hosted a System.Windows.Forms.DataVisualization Chart with two series
//     (cross-track error and PWM) and three gain buttons that rescaled the Y axis.  That
//     Windows-only charting control is replaced by the portable <local:XteChartControl> declared
//     in the markup (x:Name="unoChart"); this code-behind is the thin Avalonia adapter that wires
//     the three gain buttons to the chart's gain operations and mirrors the chart's current Y span
//     into the lblMax / lblMin read-outs, exactly as the WinForms axis labels did.
//   * Live sample feeding (the per-fix cross-track-error / PWM stream that the WinForms form pulled
//     from the FormGPS "mf" god-object) is intentionally NOT wired here.  The real-time guidance
//     pipeline is being decoupled into injectable services in a later checkpoint, so the host
//     supplies samples through the AddSample passthrough below rather than this view reaching back
//     into FormGPS.  Until the host feeds data the chart simply renders an empty baseline — the same
//     state the WinForms chart showed before the first fix arrived.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormGraphXTEView : Window
    {
        public FormGraphXTEView()
        {
            InitializeComponent();
            UpdateAxisLabels();
        }

        // Gain-up: tighten the Y span so the trace fills more of the plot (mirrors the WinForms
        // "+" button that halved the chart axis maximum).
        private void OnGainUpClick(object sender, RoutedEventArgs e)
        {
            unoChart.GainUp();
            UpdateAxisLabels();
        }

        // Gain-down: widen the Y span (mirrors the WinForms "-" button that doubled the axis maximum).
        private void OnGainDownClick(object sender, RoutedEventArgs e)
        {
            unoChart.GainDown();
            UpdateAxisLabels();
        }

        // Gain-auto: fit the Y span to the largest sample currently buffered (mirrors the WinForms
        // "Auto" button that auto-ranged the axis).
        private void OnGainAutoClick(object sender, RoutedEventArgs e)
        {
            unoChart.GainAuto();
            UpdateAxisLabels();
        }

        // Mirror the chart's symmetric Y span into the top/bottom axis read-outs.  The WinForms form
        // showed the axis maximum/minimum next to the plot; XteChartControl exposes the same values
        // through YMax / YMin so the labels stay in lock-step with the gain buttons.
        private void UpdateAxisLabels()
        {
            lblMax.Text = unoChart.YMax.ToString("0");
            lblMin.Text = unoChart.YMin.ToString("0");
        }

        /// <summary>
        /// Host entry point for the decoupled guidance pipeline: push one cross-track-error / PWM
        /// sample pair into the chart and refresh the steer-angle / PWM read-outs.  This replaces the
        /// WinForms form's direct reads of the FormGPS "mf" reference; the live data source is wired
        /// by the application host when the guidance services are connected.
        /// </summary>
        /// <param name="crossTrackError">Cross-track error sample (same units the WinForms chart plotted).</param>
        /// <param name="pwm">PWM sample.</param>
        /// <param name="steerAngle">Steer-angle read-out value, in degrees.</param>
        public void AddSample(double crossTrackError, double pwm, double steerAngle)
        {
            unoChart.AddSample(crossTrackError, pwm);
            lblSteerAng.Text = steerAngle.ToString("0.0");
            lblPWM.Text = pwm.ToString("0");
        }

        /// <summary>Clear the buffered trace (used when the host restarts a session).</summary>
        public void ClearChart()
        {
            unoChart.Clear();
        }
    }
}
