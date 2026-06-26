// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormGraphHeading.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the heading / IMU diagnostic graph dialog.
//
// Parity notes vs the WinForms original (FormGraphHeading):
//   * The WinForms form hosted two System.Windows.Forms.DataVisualization charts (a heading-source
//     comparison and a roll trace) plus three live read-outs.  The two Windows-only charts are
//     represented in the markup by the <Border x:Name="unoChart"> / <Border x:Name="rollChart">
//     plot surfaces; rendering the live polylines onto those surfaces is part of the cross-platform
//     charting work being completed in a later checkpoint and is intentionally deferred here.
//   * This view declares no Click handlers (it is read-only), so the code-behind is the thin
//     Avalonia adapter required to satisfy the x:Class partial: it calls InitializeComponent and
//     exposes a host passthrough for the three read-outs.  The WinForms form pulled those values
//     from the FormGPS "mf" god-object on a timer; with the guidance pipeline being decoupled into
//     injectable services, the host feeds them through UpdateReadouts instead of this view reaching
//     back into FormGPS.

using Avalonia.Controls;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormGraphHeadingView : Window
    {
        public FormGraphHeadingView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Host entry point for the decoupled guidance pipeline: refresh the steer-angle, PWM and
        /// heading-difference read-outs.  Replaces the WinForms form's timer-driven reads of the
        /// FormGPS "mf" reference; the live data source is wired by the application host when the
        /// guidance services connect.
        /// </summary>
        /// <param name="steerAngle">Steer-angle read-out value, in degrees.</param>
        /// <param name="pwm">PWM read-out value.</param>
        /// <param name="headingDifference">Heading-difference read-out value, in degrees.</param>
        public void UpdateReadouts(double steerAngle, double pwm, double headingDifference)
        {
            lblSteerAng.Text = steerAngle.ToString("0.0");
            lblPWM.Text = pwm.ToString("0");
            lblDiff.Text = headingDifference.ToString("0.0");
        }
    }
}
