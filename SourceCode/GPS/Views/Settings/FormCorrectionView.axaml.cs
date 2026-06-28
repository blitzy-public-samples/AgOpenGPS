// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Roll Correction Chart" diagnostic dialog — a 1:1 parity
// reimplementation of the WinForms Forms/Settings/FormCorrection.cs (144 lines) + its
// FormCorrection.Designer.cs. The screen plots three live rolling traces (roll-correction
// distance, corrected easting, uncorrected easting) and shows their numeric read-outs along the
// bottom of the window.
//
// PARITY MAPPING (WinForms FormCorrection -> this view):
//   * WinForms held a back-reference to the FormGPS "mf" god-object and read live values from it
//     (mf.correctionDistanceGraph, mf.pn.fix.easting, mf.uncorrectedEastingGraph, mf.RollInDegrees,
//     mf.gpsHz). Per AAP §0.6.1 the guidance pipeline is being decoupled from the UI shell, so this
//     view depends only on the small read-only <see cref="ICorrectionTelemetry"/> seam (supplied by
//     the composition root) instead of reaching back into FormGPS. The constructor stores it in
//     "_tel", exactly mirroring the original "mf = callingForm as FormGPS;" assignment.
//   * The Windows-only System.Windows.Forms.DataVisualization Chart ("rollChart") was dropped during
//     the migration (AAP §0.5); it is replaced by the portable <see cref="RollChart"/> control
//     declared in the markup, which buffers the most recent 50 samples per series (matching the
//     WinForms "while (Points.Count > 50) RemoveAt(0)" trim) and step-line-renders them. Its
//     AddSample(ro, ze, oe) is the cross-platform equivalent of AddXY(...) + trim-to-50 +
//     ResetAutoValues().
//   * The WinForms System.Windows.Forms.Timer (created in the designer with Enabled=true, interval
//     set in FormSteerGraph_Load) becomes an Avalonia DispatcherTimer configured and started in
//     OnLoaded and stopped in OnClosing.
//   * The two button handler names (btnScroll_Click_1, btnPoleOrMoving_Click) are preserved verbatim
//     so the markup's Click="..." attributes resolve, and their behaviour is identical: btnScroll
//     freezes/resumes scrolling (isScroll) and btnPoleOrMoving toggles pole/moving correction
//     (isPole), flipping the caption between "Pole" and "Moving" (WinForms Button.Text -> Avalonia
//     Button.Content).
//
// CROSS-CUTTING: every ToString/Parse uses CultureInfo.InvariantCulture so the read-outs and the
// plotted values are locale-independent across Windows / Linux / macOS (AAP §0.6.5 culture risk);
// no System.Windows.Forms and no DataVisualization types are referenced.
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Roll Correction Chart dialog. Faithful Avalonia re-host of the WinForms
    /// <c>FormCorrection</c>; drives the named markup controls imperatively from a per-fix timer,
    /// reading live values from an injected <see cref="ICorrectionTelemetry"/> snapshot.
    /// </summary>
    public partial class FormCorrectionView : Window
    {
        /// <summary>
        /// [XPLAT] Read-only telemetry seam replacing the WinForms <c>FormGPS mf</c> back-reference.
        /// Supplied by the composition root; never null after construction.
        /// </summary>
        private readonly ICorrectionTelemetry _tel;

        // ---- Live state (names + defaults mirror FormCorrection.cs exactly) -------------------------

        /// <summary>Pole-mode (true) vs moving-mode (false) correction. Matches FormCorrection.isPole.</summary>
        private bool isPole = true;

        // Per-series formatted sample buffers between computation and plotting (FormCorrection held the
        // same three string fields with these exact initial values).
        private string roll = "0.1";
        private string east = "0";
        private string ost = "0";

        /// <summary>True while the chart is live-scrolling; when false the traces freeze. Matches FormCorrection.isScroll.</summary>
        private bool isScroll = true;

        /// <summary>
        /// [XPLAT] Per-fix redraw timer (Avalonia DispatcherTimer replaces the WinForms designer
        /// <c>System.Windows.Forms.Timer</c>). Created and started in <see cref="OnLoaded"/>, stopped
        /// in <see cref="OnClosing"/>.
        /// </summary>
        private DispatcherTimer timer1;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader. Without it the
        /// Avalonia XAML compiler raises AVLN3001 ("XAML resource ... won't be reachable via runtime
        /// loader, as no public constructor was found"), which the Release zero-warning gate forbids
        /// (see MIGRATION_DOCS/CHANGELOG.md, F1-001). It is the single construction chokepoint: the
        /// parity constructor below chains to it with <c>: this()</c> so <c>InitializeComponent()</c>
        /// runs exactly once.
        /// </summary>
        public FormCorrectionView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to a telemetry snapshot. Mirrors the WinForms
        /// <c>FormCorrection(Form callingForm)</c> constructor, which assigned the caller and then
        /// called <c>InitializeComponent()</c> (now reached via the <c>: this()</c> chain).
        /// </summary>
        /// <param name="telemetry">Live correction/easting/roll source (the composition root's adapter).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="telemetry"/> is null.</exception>
        public FormCorrectionView(ICorrectionTelemetry telemetry)
            : this()
        {
            // [XPLAT] replaces "mf = callingForm as FormGPS;" — the DI seam is required, so guard it.
            _tel = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// [XPLAT] Port of <c>FormSteerGraph_Load</c>: configure and start the redraw timer. The
        /// WinForms form set <c>timer1.Interval = (int)((1 / mf.gpsHz) * 1000)</c> on a designer timer
        /// whose <c>Enabled = true</c> auto-started it; here the DispatcherTimer is created once and
        /// started on load.
        /// </summary>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (timer1 == null)
            {
                // WinForms: timer1.Interval = (int)((1 / gpsHz) * 1000). For any valid (finite, positive)
                // gpsHz the millisecond value below is byte-identical to the WinForms result.
                //
                // [XPLAT] Degenerate-input guard. gpsHz is always a positive GPS update rate in normal
                // operation, but a zero/negative/NaN/Infinity value must not crash startup (AAP graceful
                // degradation). Note a runtime divergence the parity test surfaced: net48 cast an
                // out-of-range double to int.MinValue, so 1/0 -> +Infinity was caught by a "< 1" clamp;
                // .NET 8/9 uses saturating conversion, so (int)(+Infinity) is int.MaxValue and would slip
                // past that clamp, yielding a multi-day interval that silently never ticks. Validate the
                // double *before* the cast and fall back to a safe cadence (the WinForms Timer default of
                // 100 ms) when gpsHz is not usable.
                double hz = _tel.gpsHz;
                int intervalMs;
                if (double.IsNaN(hz) || double.IsInfinity(hz) || hz <= 0.0)
                {
                    intervalMs = 100;
                }
                else
                {
                    double ms = (1.0 / hz) * 1000.0;
                    if (double.IsNaN(ms) || ms < 1.0)
                    {
                        intervalMs = 1;
                    }
                    else if (ms > int.MaxValue)
                    {
                        intervalMs = int.MaxValue;
                    }
                    else
                    {
                        intervalMs = (int)ms;
                    }
                }

                timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
                timer1.Tick += timer1_Tick;
            }

            timer1.Start();
        }

        /// <summary>
        /// [XPLAT] Stop the redraw timer when the window closes. WinForms disposed the designer timer
        /// with the form; the DispatcherTimer must be stopped explicitly so it stops firing.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            timer1?.Stop();
            base.OnClosing(e);
        }

        /// <summary>Timer tick -> redraw. Preserved verbatim from FormCorrection.timer1_Tick.</summary>
        private void timer1_Tick(object sender, EventArgs e)
        {
            DrawChart();
        }

        /// <summary>
        /// [XPLAT] Port of <c>FormCorrection.DrawChart()</c>. Recomputes the ×20-scaled per-series
        /// sample strings and the bottom read-out band, then — while scrolling — pushes one sample per
        /// series into the chart. All formatting/parsing uses <see cref="CultureInfo.InvariantCulture"/>
        /// for cross-platform locale-independence.
        /// </summary>
        private void DrawChart()
        {
            // Per-series sample values, scaled ×20 for visibility, formatted "N2" exactly as WinForms.
            roll = (_tel.correctionDistanceGraph * 20).ToString("N2", CultureInfo.InvariantCulture);
            east = (_tel.FixEasting * 20).ToString("N2", CultureInfo.InvariantCulture);
            ost = (_tel.uncorrectedEastingGraph * 20).ToString("N2", CultureInfo.InvariantCulture);

            // Moving mode plots the combined correction + uncorrected easting on the "Ro" trace.
            if (!isPole)
            {
                roll = ((_tel.correctionDistanceGraph + _tel.uncorrectedEastingGraph) * 20).ToString("N2", CultureInfo.InvariantCulture);
            }

            // Live numeric read-outs (updated every tick, regardless of the scroll/freeze state — the
            // WinForms label updates sat outside the "if (isScroll)" block).
            lblCorrectionDistance.Text = _tel.correctionDistanceGraph.ToString("N2", CultureInfo.InvariantCulture);
            lblEast.Text = _tel.FixEasting.ToString("N2", CultureInfo.InvariantCulture);
            lblOst.Text = _tel.uncorrectedEastingGraph.ToString("N2", CultureInfo.InvariantCulture);
            lblRollDegrees.Text = _tel.RollInDegrees;
            lblEastOnGraph.Text = ((int)(_tel.FixEasting * 100)).ToString(CultureInfo.InvariantCulture);

            if (isScroll)
            {
                // Parse the "N2"-formatted strings back to double before plotting, reproducing the
                // WinForms round-trip (the chart's AddXY received the formatted strings and parsed
                // them, so the plotted points carried the same 2-decimal rounding). RollChart.AddSample
                // appends to series "Ro"/"Ze"/"Oe", trims each buffer to the most recent 50
                // (RollChart.Capacity) and calls InvalidateVisual() — the cross-platform equivalent of
                // AddXY(...) + trim-to-50 + ResetAutoValues(). When isScroll is false the traces freeze
                // (no append), exactly as in the original.
                double rollPlot = double.Parse(roll, CultureInfo.InvariantCulture);
                double eastPlot = double.Parse(east, CultureInfo.InvariantCulture);
                double ostPlot = double.Parse(ost, CultureInfo.InvariantCulture);

                rollChart.AddSample(rollPlot, eastPlot, ostPlot);
            }
        }

        /// <summary>
        /// btnScroll handler — freeze/resume the traces. Name and behaviour preserved verbatim from
        /// <c>FormCorrection.btnScroll_Click_1</c>.
        /// </summary>
        private void btnScroll_Click_1(object sender, RoutedEventArgs e)
        {
            isScroll = !isScroll;
        }

        /// <summary>
        /// btnPoleOrMoving handler — toggle pole vs. moving correction and flip the caption. Mirrors
        /// <c>FormCorrection.btnPoleOrMoving_Click</c>; WinForms set Button.Text, the Avalonia
        /// equivalent is Button.Content.
        /// </summary>
        private void btnPoleOrMoving_Click(object sender, RoutedEventArgs e)
        {
            isPole = !isPole;
            btnPoleOrMoving.Content = isPole ? "Pole" : "Moving";
        }
    }

    /// <summary>
    /// [XPLAT] Minimal read-only telemetry seam consumed by <see cref="FormCorrectionView"/>, replacing
    /// the WinForms direct reads off the <c>FormGPS</c> "mf" god-object (AAP §0.6.1 decoupling). The
    /// composition root provides an implementation that snapshots the live guidance/AHRS values; the
    /// member names mirror the original <c>FormGPS</c> fields so the parity port reads identically:
    /// <list type="bullet">
    /// <item><description><see cref="correctionDistanceGraph"/> = <c>mf.correctionDistanceGraph</c>.</description></item>
    /// <item><description><see cref="FixEasting"/> = <c>mf.pn.fix.easting</c>.</description></item>
    /// <item><description><see cref="uncorrectedEastingGraph"/> = <c>mf.uncorrectedEastingGraph</c>.</description></item>
    /// <item><description><see cref="RollInDegrees"/> = <c>mf.RollInDegrees</c> (pre-formatted string).</description></item>
    /// <item><description><see cref="gpsHz"/> = <c>mf.gpsHz</c> (fix rate, Hz; drives the redraw interval).</description></item>
    /// </list>
    /// </summary>
    public interface ICorrectionTelemetry
    {
        /// <summary>Roll-correction distance (the "Ro" trace source / lblCorrectionDistance read-out).</summary>
        double correctionDistanceGraph { get; }

        /// <summary>Corrected easting — <c>pn.fix.easting</c> (the "Ze" trace source / lblEast read-out).</summary>
        double FixEasting { get; }

        /// <summary>Uncorrected easting (the "Oe" trace source / lblOst read-out).</summary>
        double uncorrectedEastingGraph { get; }

        /// <summary>Pre-formatted roll-in-degrees string shown in lblRollDegrees (formatted by the source).</summary>
        string RollInDegrees { get; }

        /// <summary>GPS fix rate in Hz; the redraw timer interval is <c>(1 / gpsHz) * 1000</c> ms.</summary>
        double gpsHz { get; }
    }
}
