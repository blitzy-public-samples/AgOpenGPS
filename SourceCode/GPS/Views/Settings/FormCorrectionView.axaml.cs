// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormCorrection.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the Roll Correction Chart diagnostic dialog.
//
// Parity notes vs the WinForms original (FormCorrection):
//   * The WinForms form hosted a Windows-only DataVisualization chart with three rolling traces
//     (roll-correction distance, corrected easting, uncorrected easting) plus live numeric
//     read-outs.  The chart is reimplemented by the portable <local:RollChart x:Name="rollChart">
//     declared in the markup (a Control that buffers the most recent 50 samples per series and
//     paints step-line polylines in Render); this code-behind is the thin adapter that owns the
//     two toggle buttons and forwards samples / read-out values to the named controls.
//   * The two declared handler names are preserved verbatim from the WinForms source
//     (btnScroll_Click_1, btnPoleOrMoving_Click) and reproduce its exact behaviour: btnScroll
//     freezes / resumes scrolling (isScroll), and btnPoleOrMoving toggles pole vs. moving correction
//     (isPole) while flipping the button caption between "Pole" and "Moving".
//   * The WinForms form ran a timer (1000 / gpsHz) that read the live correction/easting/roll values
//     from the FormGPS "mf" god-object and, while scrolling, pushed one ×20-scaled sample per series
//     into the chart.  With the guidance pipeline being decoupled into injectable services, the host
//     drives that loop and feeds this view through AddSample / UpdateReadouts below instead of this
//     view reaching back into FormGPS.  The scroll/pole state that gates and shapes those pushes is
//     owned here, exactly as in the original.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormCorrectionView : Window
    {
        // Per-series push scale used by the WinForms DrawChart (each sample was multiplied by 20
        // before being added to the chart series); preserved for visual parity.
        private const double SampleScale = 20.0;

        // isScroll / isPole defaults match FormCorrection.cs (both true at construction).
        private bool _isScroll = true;
        private bool _isPole = true;

        public FormCorrectionView()
        {
            InitializeComponent();

            // Caption matches the initial isPole == true state (markup also seeds Content="Pole").
            btnPoleOrMoving.Content = _isPole ? "Pole" : "Moving";
        }

        /// <summary>True while the chart is live-scrolling; the host should only push samples when set.</summary>
        public bool IsScrolling => _isScroll;

        /// <summary>True for pole-mode correction, false for moving-mode (matches FormCorrection.isPole).</summary>
        public bool IsPole => _isPole;

        // btnScroll: freeze / resume scrolling (WinForms: isScroll = !isScroll). Handler name preserved.
        private void btnScroll_Click_1(object sender, RoutedEventArgs e)
        {
            _isScroll = !_isScroll;
        }

        // btnPoleOrMoving: toggle pole vs moving correction and flip the caption (WinForms used
        // Button.Text; the Avalonia equivalent is Button.Content). Handler name preserved.
        private void btnPoleOrMoving_Click(object sender, RoutedEventArgs e)
        {
            _isPole = !_isPole;
            btnPoleOrMoving.Content = _isPole ? "Pole" : "Moving";
        }

        /// <summary>
        /// Host entry point: push one sample set into the chart (only honoured while scrolling).
        /// Mirrors the WinForms DrawChart, which multiplied each series value by 20 before adding it.
        /// </summary>
        /// <param name="rollCorrection">Roll-correction distance series ("Ro").</param>
        /// <param name="correctedEasting">Corrected-easting series ("Ze").</param>
        /// <param name="uncorrectedEasting">Uncorrected-easting series ("Oe").</param>
        public void AddSample(double rollCorrection, double correctedEasting, double uncorrectedEasting)
        {
            if (!_isScroll) return;
            rollChart.AddSample(rollCorrection * SampleScale, correctedEasting * SampleScale, uncorrectedEasting * SampleScale);
        }

        /// <summary>
        /// Host entry point: refresh the bottom read-out band.  Numeric values are formatted with
        /// InvariantCulture and the "N2" pattern, identical to the WinForms read-outs, so the display
        /// is locale-independent across Windows / Linux / macOS.
        /// </summary>
        /// <param name="correctionDistance">lblCorrectionDistance value.</param>
        /// <param name="easting">lblEast value (fix easting).</param>
        /// <param name="uncorrectedEasting">lblOst value.</param>
        /// <param name="rollDegrees">lblRollDegrees value (already-formatted roll string, as in the original).</param>
        public void UpdateReadouts(double correctionDistance, double easting, double uncorrectedEasting, string rollDegrees)
        {
            lblCorrectionDistance.Text = correctionDistance.ToString("N2", CultureInfo.InvariantCulture);
            lblEast.Text = easting.ToString("N2", CultureInfo.InvariantCulture);
            lblOst.Text = uncorrectedEasting.ToString("N2", CultureInfo.InvariantCulture);
            lblRollDegrees.Text = rollDegrees;
            lblEastOnGraph.Text = ((int)(easting * 100)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Clear the buffered traces (used when the host restarts a session).</summary>
        public void ClearChart()
        {
            rollChart.Clear();
        }
    }
}
