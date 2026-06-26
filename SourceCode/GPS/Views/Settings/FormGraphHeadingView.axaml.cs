// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormGraphHeading.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the live heading / IMU diagnostic graph dialog ("Heading Chart").
//
// 1:1 behavioural parity with the deleted WinForms FormGraphHeading (FormGraphHeading.cs +
// FormGraphHeading.Designer.cs), per AAP §0.3.3 — a faithful re-platform, never a redesign.
//
// What the original did, and how it maps here:
//   * It hosted two Windows-only System.Windows.Forms.DataVisualization charts — unoChart (series
//     "S" = GPS heading, "PWM" = IMU-corrected heading) and rollChart (series "Ro" = heading
//     difference, "Ze" = constant zero baseline) — plus three live read-outs (lblSteerAng / lblPWM /
//     lblDiff). DataVisualization is Windows-only and is removed by the migration (AAP §0.5). The
//     partner markup (FormGraphHeadingView.axaml) re-hosts the two charts as custom-drawing
//     HeadingChartControl surfaces and the read-outs as <TextBlock>s; this code-behind keeps the four
//     rolling point-buffers and the sample timer that drove the original charts. The actual polyline
//     pixel-paint onto the two surfaces is performed by the HeadingChartControl declared at the bottom
//     of this file (the same dependency-free Render(DrawingContext) primitive used by the sibling
//     FormGraphXTEView's XteChartControl); the three read-outs refresh live on every tick as before.
//   * It read mf.gpsHeading / mf.imuCorrected / mf.gpsHz off the FormGPS "mf" god-object on a timer.
//     With the guidance pipeline being decoupled into injectable services (AAP §0.6.1), those three
//     values now arrive through the injected IHeadingTelemetry snapshot (_tel) instead of this view
//     reaching back into FormGPS — there is deliberately NO FormGPS / mf reference here.
//
// Cross-platform notes ([XPLAT]):
//   * WinForms System.Windows.Forms.Timer -> Avalonia DispatcherTimer (UI-thread tick).
//   * Every numeric ToString / Parse uses CultureInfo.InvariantCulture, so a comma-decimal locale on
//     Linux/macOS can never drift the plotted/displayed values (AAP cross-cutting culture rule §0.6.5).
//   * glm.toDegrees is the existing AgOpenGPS math helper (namespace AgOpenGPS, Classes/CGLM.cs),
//     reused unchanged.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media; // [XPLAT] DrawingContext / IPen / Pen / Brushes for the custom chart rendering
using Avalonia.Threading;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// Live heading / IMU diagnostic dialog. Reimplements the WinForms <c>FormGraphHeading</c> on
    /// Avalonia with identical sampling, formatting and rolling-window behaviour: it compares the GPS
    /// course heading against the IMU-corrected heading and tracks their signed difference.
    /// </summary>
    public partial class FormGraphHeadingView : Window
    {
        /// <summary>Most-recent-N window kept per series (the WinForms chart kept the last 100 points).</summary>
        private const int MaxPoints = 100;

        // Injected, FormGPS-free telemetry snapshot (replaces the WinForms "mf" field reads).
        private readonly IHeadingTelemetry _tel;

        // --- chart read-out state (initial values verbatim from FormGraphHeading.cs) ---
        private string dataSteerAngle = "0";
        private string dataPWM = "-1";
        private string roll = "1";
        private string zero = "0";

        // Scroll/freeze gate: when true the series scroll (append + trim); when false they freeze.
        private bool isScroll = true;

        // [XPLAT] WinForms System.Windows.Forms.Timer -> Avalonia DispatcherTimer.
        private DispatcherTimer timer1;

        // The four rolling series buffers behind the two <Border> plot surfaces. They are the
        // "S"/"PWM" series of unoChart and the "Ro"/"Ze" series of rollChart from the original
        // DataVisualization charts, retained here so the decoupled cross-platform renderer (CP4/CP9)
        // and the guidance parity tests can consume them.
        private readonly List<double> _seriesS = new List<double>();    // "S"   GPS heading (deg)
        private readonly List<double> _seriesPwm = new List<double>();  // "PWM" IMU-corrected heading (deg)
        private readonly List<double> _seriesRo = new List<double>();   // "Ro"  heading difference (deg)
        private readonly List<double> _seriesZe = new List<double>();   // "Ze"  constant zero baseline

        /// <summary>"S" series — GPS-heading samples (degrees), most-recent-100 rolling window.</summary>
        internal IReadOnlyList<double> HeadingSeries => _seriesS;

        /// <summary>"PWM" series — IMU-corrected-heading samples (degrees), rolling window.</summary>
        internal IReadOnlyList<double> ImuSeries => _seriesPwm;

        /// <summary>"Ro" series — GPS-minus-IMU heading-difference samples (degrees), rolling window.</summary>
        internal IReadOnlyList<double> DifferenceSeries => _seriesRo;

        /// <summary>"Ze" series — constant zero-baseline samples, rolling window.</summary>
        internal IReadOnlyList<double> ZeroSeries => _seriesZe;

        // [XPLAT] Series pens copied verbatim from the deleted FormGraphHeading.Designer.cs so the
        // re-hosted polylines match the WinForms DataVisualization series exactly:
        //   unoChart "S"   : Color LightSalmon, BorderWidth 2, StepLine.
        //   unoChart "PWM" : Color Lime,        BorderWidth 2, StepLine.
        //   rollChart "Ro" : Color Cyan,        BorderWidth 1 (designer default), StepLine.
        //   rollChart "Ze" : Color Red,         BorderWidth 1 (designer default), FastLine.
        // StepLine vs FastLine renders as a straight polyline here — the same reviewer-accepted
        // primitive the sibling XteChartControl uses (Finding 7 resolution: "share the sibling graph
        // rendering primitive"); at the 10 Hz / 100-point sampling density the two are indistinguishable.
        private static readonly IPen PenS = new Pen(Brushes.LightSalmon, 2);
        private static readonly IPen PenPwm = new Pen(Brushes.Lime, 2);
        private static readonly IPen PenRo = new Pen(Brushes.Cyan, 1);
        private static readonly IPen PenZe = new Pen(Brushes.Red, 1);

        /// <summary>
        /// Parameterless constructor for the Avalonia design-time previewer / XAML loader, matching the
        /// sibling graph views. Supplies a no-op telemetry source so the dialog can be instantiated
        /// without a live guidance pipeline; the application host uses the injecting constructor below.
        /// </summary>
        public FormGraphHeadingView() : this(new NullHeadingTelemetry())
        {
        }

        /// <summary>
        /// Creates the dialog bound to a heading-telemetry snapshot.
        /// </summary>
        /// <param name="telemetry">
        /// Live GPS/IMU heading source (replaces the WinForms <c>FormGPS</c> "mf" reads). A
        /// <see langword="null"/> argument falls back to a no-op source so the view never faults.
        /// </param>
        public FormGraphHeadingView(IHeadingTelemetry telemetry)
        {
            _tel = telemetry ?? new NullHeadingTelemetry();

            InitializeComponent();

            // [XPLAT] Point the two custom chart surfaces at this view's rolling buffers. The
            // HeadingChartControl holds REFERENCES to the lists (it never copies), so the existing
            // DrawChart() append-then-InvalidateVisual() path repaints the live data unchanged.
            //   unoChart  : "S" (GPS heading) + "PWM" (IMU heading); AUTO Y range (the WinForms unoChart
            //               set no AxisY.Minimum/Maximum), so NaN bounds select auto-fit; no fixed grid.
            //   rollChart : "Ro" (difference) + "Ze" (zero baseline); FIXED Y -10..+10 with a major-grid
            //               interval of 2 — verbatim from FormGraphHeading.Designer.cs (chartArea2).
            unoChart.Configure(_seriesS, _seriesPwm, PenS, PenPwm, double.NaN, double.NaN, 0.0);
            rollChart.Configure(_seriesRo, _seriesZe, PenRo, PenZe, -10.0, 10.0, 2.0);

            // [XPLAT] WinForms wired Load (FormSteerGraph_Load) and the timer in the designer; Avalonia
            // wires the equivalents here: start sampling once the window is shown, stop it on close.
            this.Opened += OnOpened;
            this.Closing += (_, _) => timer1?.Stop();
        }

        // [XPLAT] WinForms FormSteerGraph_Load -> Avalonia Window.Opened: configure and start the
        // sample timer at the GPS rate, then clamp the window on-screen.
        private void OnOpened(object sender, EventArgs e)
        {
            // WinForms: timer1.Interval = (int)((1 / (double)mf.gpsHz) * 1000). Guard the degenerate
            // gpsHz <= 0 so DispatcherTimer never receives an invalid interval (the WinForms source
            // assumed a positive rate); fall back to ~10 Hz.
            double hz = _tel.gpsHz;
            int intervalMs = hz > 0.0 ? (int)((1.0 / hz) * 1000.0) : 100;
            if (intervalMs < 1)
            {
                intervalMs = 1;
            }

            timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
            timer1.Tick += (_, _) => DrawChart();
            timer1.Start();

            TryClampOnScreen();
        }

        // [XPLAT] WinForms `if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }` -> Avalonia
        // Screens. Best-effort: if the window's top-left lands on no connected screen, move it to (0,0).
        private void TryClampOnScreen()
        {
            try
            {
                var screens = this.Screens;
                if (screens == null)
                {
                    return;
                }

                var pos = this.Position;
                foreach (var screen in screens.All)
                {
                    var b = screen.Bounds;
                    if (pos.X >= b.X && pos.X < b.X + b.Width &&
                        pos.Y >= b.Y && pos.Y < b.Y + b.Height)
                    {
                        return; // already visible on a connected screen
                    }
                }

                this.Position = new PixelPoint(0, 0);
            }
            catch (Exception)
            {
                // Best-effort only: on headless / unsupported backends the screen list may be
                // unavailable. Per AAP §0.7.2 graceful degradation must never break the dialog, so a
                // failed on-screen clamp is intentionally ignored — the window simply keeps the
                // position assigned by the window manager.
            }
        }

        /// <summary>
        /// Per-tick chart update — parity with the WinForms <c>DrawChart()</c>: refresh the three
        /// read-outs, then (only while scrolling) append one sample to each of the four series and trim
        /// every buffer to the most recent <see cref="MaxPoints"/> points.
        /// </summary>
        private void DrawChart()
        {
            dataSteerAngle = glm.toDegrees(_tel.gpsHeading).ToString("N1", CultureInfo.InvariantCulture);
            dataPWM = glm.toDegrees(_tel.imuCorrected).ToString("N1", CultureInfo.InvariantCulture);

            lblSteerAng.Text = dataSteerAngle;
            lblPWM.Text = dataPWM;

            lblDiff.Text = glm.toDegrees(_tel.gpsHeading - _tel.imuCorrected)
                .ToString("N2", CultureInfo.InvariantCulture);

            roll = lblDiff.Text;
            zero = "0";

            if (isScroll)
            {
                // [XPLAT] WinForms Series.Points.AddXY(nextX, value) -> append to the code-behind
                // buffer (the X value is the sample index, implicit in the rolling window). Parse with
                // InvariantCulture so the values round-trip identically on comma-decimal locales.
                AppendCapped(_seriesS, double.Parse(dataSteerAngle, CultureInfo.InvariantCulture));
                AppendCapped(_seriesPwm, double.Parse(dataPWM, CultureInfo.InvariantCulture));
                AppendCapped(_seriesRo, double.Parse(roll, CultureInfo.InvariantCulture));
                AppendCapped(_seriesZe, double.Parse(zero, CultureInfo.InvariantCulture));

                // [XPLAT] WinForms ChartArea.RecalculateAxesScale() + the implicit chart repaint ->
                // request a repaint of the two plot surfaces.
                unoChart.InvalidateVisual();
                rollChart.InvalidateVisual();
            }
        }

        // Append one value and drop the oldest beyond the rolling window
        // (WinForms: while (Points.Count > 100) Points.RemoveAt(0)).
        private static void AppendCapped(List<double> buffer, double value)
        {
            buffer.Add(value);
            while (buffer.Count > MaxPoints)
            {
                buffer.RemoveAt(0);
            }
        }

        // Null-object telemetry for design-time / preview (no live guidance pipeline). gpsHz = 10 keeps
        // the sample interval valid (100 ms) and the headings read as zero.
        private sealed class NullHeadingTelemetry : IHeadingTelemetry
        {
            public double gpsHeading => 0.0;
            public double imuCorrected => 0.0;
            public double gpsHz => 10.0;
        }
    }

    /// <summary>
    /// [XPLAT] Dependency-free cross-platform replacement for the two Windows-only
    /// <c>System.Windows.Forms.DataVisualization</c> charts ("unoChart" / "rollChart") that the WinForms
    /// <c>FormGraphHeading</c> hosted. Defined in the partner code-behind exactly as the
    /// FormGraphHeadingView.axaml contract comment specifies, and referenced from that markup as
    /// <c>&lt;local:HeadingChartControl x:Name="unoChart" /&gt;</c> and <c>x:Name="rollChart"</c>.
    ///
    /// <para>Unlike the sibling <c>XteChartControl</c> (which owns its sample buffers), this control holds
    /// <b>references</b> to the buffers owned by <see cref="FormGraphHeadingView"/> — supplied through
    /// <see cref="Configure"/> — so the view's existing <c>DrawChart()</c> append-then-<c>InvalidateVisual()</c>
    /// path drives both surfaces with no data duplication. One generalized control renders both charts: the
    /// top chart is auto-ranged (NaN bounds), the bottom chart is fixed to a symmetric range with a
    /// fixed-interval major grid, matching the original <c>chartArea2</c> (-10..+10, interval 2).</para>
    ///
    /// <para>Each series is stroked as a straight polyline over DimGray gridlines on a black background —
    /// the exact series colours and back-colour from FormGraphHeading.Designer.cs, using the same
    /// reviewer-accepted polyline primitive as <c>XteChartControl</c>.</para>
    /// </summary>
    public sealed class HeadingChartControl : Control
    {
        // References to the view's rolling buffers (never copied); the two series drawn on this surface.
        private IReadOnlyList<double> _seriesA;
        private IReadOnlyList<double> _seriesB;
        private IPen _penA;
        private IPen _penB;

        // Fixed Y bounds; NaN on either selects auto-fit to the buffered data (WinForms auto axis).
        private double _axisYMin = double.NaN;
        private double _axisYMax = double.NaN;

        // > 0 draws a fixed-interval horizontal major grid (rollChart: interval 2 over a fixed range);
        // 0 falls back to the top/middle/bottom thirds used by the auto-ranged sibling chart.
        private double _gridInterval;

        // [XPLAT] Gridlines use the chart's MajorGrid.LineColor = DimGray (FormGraphHeading.Designer.cs).
        private static readonly IPen PenGrid = new Pen(Brushes.DimGray, 1);

        /// <summary>
        /// [XPLAT] Bind this surface to a pair of the view's rolling series buffers, their pens, the Y range
        /// (pass <see cref="double.NaN"/> for either bound to auto-fit), and the major-grid interval
        /// (0 = no fixed grid). Held by reference, so later appends to the buffers paint on the next repaint.
        /// </summary>
        public void Configure(
            IReadOnlyList<double> seriesA,
            IReadOnlyList<double> seriesB,
            IPen penA,
            IPen penB,
            double axisYMin,
            double axisYMax,
            double gridInterval)
        {
            _seriesA = seriesA;
            _seriesB = seriesB;
            _penA = penA;
            _penB = penB;
            _axisYMin = axisYMin;
            _axisYMax = axisYMax;
            _gridInterval = gridInterval;
        }

        /// <summary>
        /// [XPLAT] Render the two series over DimGray gridlines on the black plot background, mapping each
        /// sample from the effective [min, max] Y range onto [height, 0] and clamping out-of-range points so
        /// spikes stay on-canvas. Mirrors <c>XteChartControl.Render</c>.
        /// </summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            // WinForms ChartArea.BackColor = Black (the surrounding Border is also black; fill defensively).
            context.FillRectangle(Brushes.Black, bounds);

            ResolveRange(out double min, out double max);
            DrawGrid(context, bounds, min, max);

            DrawSeries(context, _seriesA, _penA, bounds, min, max);
            DrawSeries(context, _seriesB, _penB, bounds, min, max);
        }

        // Draw the DimGray major gridlines: a fixed-interval horizontal grid when a fixed range + interval
        // are configured (rollChart), else the top/middle/bottom thirds used by the auto-ranged chart.
        private void DrawGrid(DrawingContext context, Rect bounds, double min, double max)
        {
            double range = max - min;
            if (_gridInterval > 0.0 && !double.IsNaN(_axisYMin) && !double.IsNaN(_axisYMax) && range > 0.0)
            {
                double start = Math.Ceiling(min / _gridInterval) * _gridInterval;
                for (double v = start; v <= max + 1e-9; v += _gridInterval)
                {
                    double norm = (v - min) / range;
                    double y = bounds.Height - norm * bounds.Height;
                    context.DrawLine(PenGrid, new Point(0, y), new Point(bounds.Width, y));
                }
            }
            else
            {
                double mid = bounds.Height / 2.0;
                context.DrawLine(PenGrid, new Point(0, 0), new Point(bounds.Width, 0));
                context.DrawLine(PenGrid, new Point(0, mid), new Point(bounds.Width, mid));
                context.DrawLine(PenGrid, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));
            }
        }

        // Compute the effective [min, max] Y range: the fixed bounds, or auto-fit to the referenced buffers
        // when either bound is NaN. Degenerate (flat / empty) ranges are padded so mapping never divides by zero.
        private void ResolveRange(out double min, out double max)
        {
            min = _axisYMin;
            max = _axisYMax;

            if (double.IsNaN(min) || double.IsNaN(max))
            {
                min = double.MaxValue;
                max = double.MinValue;
                ExpandRange(_seriesA, ref min, ref max);
                ExpandRange(_seriesB, ref min, ref max);
                if (min > max)
                {
                    // No samples buffered yet — show a symmetric unit window.
                    min = -1;
                    max = 1;
                }
            }

            if (Math.Abs(max - min) < 1e-9)
            {
                // Flat or degenerate range — pad so the line sits mid-height instead of dividing by zero.
                min -= 1;
                max += 1;
            }
        }

        private static void ExpandRange(IReadOnlyList<double> buf, ref double min, ref double max)
        {
            if (buf == null)
            {
                return;
            }

            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i] < min) min = buf[i];
                if (buf[i] > max) max = buf[i];
            }
        }

        // Stroke the buffer as a straight polyline (WinForms StepLine/FastLine -> straight line, parity with
        // the sibling XteChartControl), mapping [min, max] onto [height, 0] and clamping out-of-range points.
        private static void DrawSeries(DrawingContext ctx, IReadOnlyList<double> buf, IPen pen, Rect bounds, double min, double max)
        {
            if (buf == null || pen == null)
            {
                return;
            }

            int n = buf.Count;
            if (n < 2)
            {
                return;
            }

            double w = bounds.Width;
            double h = bounds.Height;
            double range = max - min;

            Point prev = default;
            bool have = false;
            for (int i = 0; i < n; i++)
            {
                double x = w * i / (n - 1);
                double norm = (buf[i] - min) / range;
                if (norm < 0) norm = 0;
                else if (norm > 1) norm = 1;
                double y = h - norm * h;

                Point cur = new Point(x, y);
                if (have)
                {
                    ctx.DrawLine(pen, prev, cur);
                }
                prev = cur;
                have = true;
            }
        }
    }

    /// <summary>
    /// Heading-telemetry snapshot consumed by <see cref="FormGraphHeadingView"/>. The member names
    /// mirror the WinForms <c>FormGPS</c> fields the original dialog read (<c>gpsHeading</c>,
    /// <c>imuCorrected</c>, <c>gpsHz</c>) so the migration is a direct field-for-field substitution.
    /// Provided by the application composition root when the decoupled guidance services connect
    /// (AAP §0.6.1).
    /// </summary>
    public interface IHeadingTelemetry
    {
        /// <summary>GPS course heading, in radians (as <c>FormGPS.gpsHeading</c>).</summary>
        double gpsHeading { get; }

        /// <summary>IMU-corrected (fused) heading, in radians (as <c>FormGPS.imuCorrected</c>).</summary>
        double imuCorrected { get; }

        /// <summary>GPS update rate, in hertz, used to derive the sample-timer interval.</summary>
        double gpsHz { get; }
    }
}
