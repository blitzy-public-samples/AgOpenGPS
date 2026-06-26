// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormGraphSteer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the steer-angle / PWM live graph dialog.
//
// Parity notes vs the WinForms original (FormGraphSteer):
//   * The WinForms form hosted a System.Windows.Forms.DataVisualization Chart and three gain
//     buttons that the form attached event handlers to in code (the markup declared no Click
//     attributes).  This code-behind preserves that imperative wiring exactly: the three gain
//     buttons (btnGainUp / btnGainAuto / btnGainDown) are subscribed here in the constructor rather
//     than from the markup, so the XAML never names a handler the code-behind might not define.
//   * Gain changes adjust a symmetric Y span and mirror it into the lblMax / lblMin axis read-outs,
//     reproducing the WinForms behaviour of rescaling the chart's Y axis.  The plot surface itself
//     is the <Canvas x:Name="unoChart"> declared in the markup; rendering the live steer-angle /
//     PWM polylines onto that canvas is part of the cross-platform charting work being completed in
//     a later checkpoint, so it is intentionally deferred here — the host feeds read-out values and
//     samples through the public passthrough below instead of this view reaching into FormGPS.

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormGraphSteerView : Window
    {
        // Symmetric Y span (degrees / PWM units) — the same gain model the WinForms axis used.
        private const double MinSpan = 1.0;
        private const double MaxSpan = 1000.0;
        private const double DefaultSpan = 50.0;

        private double _span = DefaultSpan;

        public FormGraphSteerView()
        {
            InitializeComponent();

            // Imperative handler wiring (parity with the WinForms form, whose markup declared no
            // Click attributes on the gain buttons).
            btnGainUp.Click += OnGainUpClick;
            btnGainAuto.Click += OnGainAutoClick;
            btnGainDown.Click += OnGainDownClick;

            UpdateAxisLabels();
        }

        // Gain-up: tighten the span so the trace fills more of the plot.
        private void OnGainUpClick(object sender, RoutedEventArgs e)
        {
            _span = Math.Max(MinSpan, _span / 2.0);
            UpdateAxisLabels();
        }

        // Gain-down: widen the span.
        private void OnGainDownClick(object sender, RoutedEventArgs e)
        {
            _span = Math.Min(MaxSpan, _span * 2.0);
            UpdateAxisLabels();
        }

        // Gain-auto: return to the default span (the WinForms "Auto" button auto-ranged the axis;
        // with no buffered series yet the faithful no-data behaviour is to reset to the default span).
        private void OnGainAutoClick(object sender, RoutedEventArgs e)
        {
            _span = DefaultSpan;
            UpdateAxisLabels();
        }

        private void UpdateAxisLabels()
        {
            lblMax.Text = _span.ToString("0");
            lblMin.Text = (-_span).ToString("0");
        }

        /// <summary>
        /// Host entry point for the decoupled guidance pipeline: refresh the steer-angle / PWM
        /// read-outs.  Replaces the WinForms form's direct reads of the FormGPS "mf" reference; the
        /// live data source is wired by the application host when the guidance services connect.
        /// </summary>
        /// <param name="steerAngle">Steer-angle read-out value, in degrees.</param>
        /// <param name="pwm">PWM read-out value.</param>
        public void UpdateReadouts(double steerAngle, double pwm)
        {
            lblSteerAng.Text = steerAngle.ToString("0.0");
            lblPWM.Text = pwm.ToString("0");
        }
    }
}
