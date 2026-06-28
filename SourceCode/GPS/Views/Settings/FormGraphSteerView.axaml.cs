// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] AgOpenGPS (GPS program) — live AutoSteer steer-angle chart (Avalonia code-behind).
//
// 1:1 behavioural PARITY reimplementation of the WinForms code-behind
// Forms/Settings/FormGraphSteer.cs (157 lines) + FormGraphSteer.Designer.cs, per AAP §0.3.3
// (parity reimplementation, never a redesign). It plots two real-time rolling series — the
// ACTUAL steer angle (series "S", OrangeRed) and the SETPOINT/guidance steer angle (series
// "PWM", Lime) — capped at the most recent 30 samples, with a left-hand Y-gain (zoom) control
// column and a colour-coded bottom legend.
//
// CROSS-PLATFORM SUBSTITUTIONS (all tagged // [XPLAT] at their use site):
//   * Windows-only System.Windows.Forms.DataVisualization.Charting.Chart -> a plain black
//     Avalonia Canvas named `unoChart` (declared in FormGraphSteerView.axaml). This code-behind
//     renders into it: two Polyline shapes for the series plus DimGray Y-axis gridlines. No new
//     NuGet package is introduced (AAP §0.5 — charting replaced with custom Avalonia drawing).
//   * Windows-only System.Windows.Forms.Timer -> Avalonia DispatcherTimer (Avalonia.Threading),
//     stopped explicitly in OnClosed (the WinForms forms-timer was disposed with the form).
//   * Helpers.ScreenHelper.IsOnScreen(Bounds) -> Avalonia Screens clamp in OnOpened.
//   * The FormGPS `mf` back-reference is removed: telemetry is injected via ISteerTelemetry
//     (Dependency Inversion, AAP §0.3.2) instead of reaching back into the UI shell.
//
// All numeric Parse/ToString use CultureInfo.InvariantCulture (AAP §0.6.5 — a comma decimal
// separator under a non-US locale must never corrupt the plotted values or the axis labels).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// Live AutoSteer steer-angle chart window. Reproduces the WinForms <c>FormGraphSteer</c>
    /// behaviour exactly: a <see cref="DispatcherTimer"/> ticks at <c>(int)((1 / gpsHz) * 1000)</c>
    /// milliseconds and, on each tick, samples the injected <see cref="ISteerTelemetry"/>, appends
    /// the ACTUAL and SETPOINT steer angles to the two rolling 30-sample series, and redraws the
    /// chart. The three gain buttons rescale the Y axis with the identical arithmetic of the
    /// WinForms original.
    /// </summary>
    public partial class FormGraphSteerView : Window
    {
        // Injected telemetry source (replaces the WinForms `mf` FormGPS back-reference).
        private readonly ISteerTelemetry _tel;

        // ----- chart data state (mirrors the WinForms fields exactly) -----

        // word 0 - steer angle (series "S", ACTUAL). Holds the InvariantCulture string of the
        // last sampled value, exactly as WinForms stored it before AddXY parsed it.
        private string dataSteerAngle = "0";

        // word 1 - pwm/setpoint (series "PWM", SETPOINT).
        private string dataPWM = "-1";

        // Y-axis mode flag: true while the axis auto-scales to the data (WinForms AxisY = NaN).
        private bool isAuto = false;

        // [XPLAT] Avalonia DispatcherTimer replacing the WinForms System.Windows.Forms.Timer.
        private DispatcherTimer timer1;

        // Rolling sample buffers feeding the two Polyline series rendered into `unoChart`.
        // Each Point is (X = WinForms-style auto-incrementing index, Y = numeric value).
        //   seriesActual   == WinForms unoChart.Series["S"]   (ACTUAL  steer angle, OrangeRed)
        //   seriesSetpoint == WinForms unoChart.Series["PWM"] (SETPOINT steer angle, Lime)
        private readonly List<Point> seriesActual = new List<Point>();
        private readonly List<Point> seriesSetpoint = new List<Point>();

        // Y-axis bounds == WinForms unoChart.ChartAreas[0].AxisY.Minimum / .Maximum.
        // double.NaN means "auto" (the chart computes the range from the data).
        private double axisYMin = double.NaN;
        private double axisYMax = double.NaN;

        // Series line colours / weights mirror FormGraphSteer.Designer.cs (BorderWidth 2):
        //   series5 "S"   -> Color.OrangeRed   series6 "PWM" -> Color.Lime
        private const double SeriesThickness = 2.0;
        private const double GridThickness = 1.0;

        /// <summary>
        /// Parameterless constructor required by the Avalonia compiled-XAML loader (its absence
        /// raises AVLN3001, which Release treats as an error). Chains to the dependency-injection
        /// constructor with an inert <see cref="NullSteerTelemetry"/> so the designer/loader path
        /// never divides by a zero <c>gpsHz</c> and the window remains self-contained.
        /// </summary>
        public FormGraphSteerView()
            : this(new NullSteerTelemetry())
        {
        }

        /// <summary>
        /// Creates the chart window bound to a telemetry source. Mirrors the WinForms
        /// <c>FormGraphSteer(Form callingForm)</c> constructor: loads the XAML, then sets the
        /// localized legend captions and the window title from <see cref="gStr"/>.
        /// </summary>
        /// <param name="telemetry">
        /// Steer-angle telemetry snapshot supplied by the composition root (replaces the WinForms
        /// <c>mf</c> FormGPS reference). When <c>null</c>, an inert <see cref="NullSteerTelemetry"/>
        /// is substituted so the view never throws.
        /// </param>
        public FormGraphSteerView(ISteerTelemetry telemetry)
        {
            _tel = telemetry ?? new NullSteerTelemetry();

            InitializeComponent();

            // Parity with WinForms ctor: localize the two legend captions and the window title.
            label5.Text = gStr.gsSetPoint;
            label1.Text = gStr.gsActual;
            Title = gStr.gsSteerChart;

            // The .axaml declares no Click= handlers (decoupled markup), so wire the WinForms
            // click handlers to the named gain buttons here.
            btnGainUp.Click += btnGainUp_Click;
            btnGainAuto.Click += btnGainAuto_Click;
            btnGainDown.Click += btnGainDown_Click;
        }

        /// <summary>
        /// Window-opened handler — parity with the WinForms <c>FormSteerGraph_Load</c>: starts the
        /// sampling timer at the gps-rate interval, initializes the Y axis to auto, and clamps the
        /// window on-screen.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // [XPLAT] WinForms: timer1.Interval = (int)((1 / mf.gpsHz) * 1000);
            // Guarded against a zero/invalid gpsHz (which would yield a non-finite or negative
            // interval and throw); falls back to the designer default of 200 ms.
            timer1 = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ComputeIntervalMs(_tel.gpsHz)),
            };
            timer1.Tick += OnTimerTick;
            timer1.Start();

            // WinForms: AxisY Max/Min = NaN; RecalculateAxesScale(); ResetAutoValues();
            //           lblMax.Text = "Auto"; lblMin.Text = ""; isAuto = true;
            axisYMax = double.NaN;
            axisYMin = double.NaN;
            lblMax.Text = "Auto";
            lblMin.Text = string.Empty;
            isAuto = true;

            // [XPLAT] WinForms: if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }
            ClampToScreen();

            RedrawChart();
        }

        /// <summary>
        /// [XPLAT] Stops the sampling timer when the window closes. The WinForms forms-timer was
        /// disposed automatically with the form; an Avalonia <see cref="DispatcherTimer"/> keeps
        /// firing (and holding this window alive) unless stopped explicitly.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            timer1?.Stop();
            base.OnClosed(e);
        }

        /// <summary>Timer tick — redraws the chart once per interval (WinForms <c>timer1_Tick</c>).</summary>
        private void OnTimerTick(object sender, EventArgs e)
        {
            DrawChart();
        }

        /// <summary>
        /// Samples the telemetry and appends one point to each rolling series, trimming both to the
        /// most recent 30 samples, then redraws. Byte-for-byte parity with the WinForms
        /// <c>DrawChart()</c> (series names, InvariantCulture formatting, <c>nextX</c> auto-increment,
        /// the 30-point window, and the readout labels).
        /// </summary>
        private void DrawChart()
        {
            // word 0 - steerangle, 1 - pwmDisplay  (WinForms read mf.mc.actualSteerAngleChart /
            // mf.guidanceLineSteerAngle and the pre-formatted mf.ActualSteerAngle / mf.SetSteerAngle).
            dataSteerAngle = _tel.actualSteerAngleChart.ToString(CultureInfo.InvariantCulture);
            dataPWM = _tel.guidanceLineSteerAngle.ToString(CultureInfo.InvariantCulture);

            lblSteerAng.Text = _tel.ActualSteerAngle;
            lblPWM.Text = _tel.SetSteerAngle;

            // WinForms: nextX / nextX5 default to 1, or last XValue + 1 when the series has points.
            double nextX = 1;
            double nextX5 = 1;

            if (seriesActual.Count > 0)
            {
                nextX = seriesActual[seriesActual.Count - 1].X + 1;
            }

            if (seriesSetpoint.Count > 0)
            {
                nextX5 = seriesSetpoint[seriesSetpoint.Count - 1].X + 1;
            }

            // WinForms: unoChart.Series["S"].Points.AddXY(nextX, dataSteerAngle);
            //           unoChart.Series["PWM"].Points.AddXY(nextX5, dataPWM);
            // The Chart parsed the string operands; we parse them with InvariantCulture here.
            seriesActual.Add(new Point(nextX, double.Parse(dataSteerAngle, CultureInfo.InvariantCulture)));
            seriesSetpoint.Add(new Point(nextX5, double.Parse(dataPWM, CultureInfo.InvariantCulture)));

            // WinForms: while (Points.Count > 30) Points.RemoveAt(0);  (rolling 30-sample window)
            while (seriesActual.Count > 30)
            {
                seriesActual.RemoveAt(0);
            }

            while (seriesSetpoint.Count > 30)
            {
                seriesSetpoint.RemoveAt(0);
            }

            // WinForms: unoChart.ResetAutoValues();  -> rebuild the Avalonia polylines + invalidate.
            RedrawChart();
        }

        /// <summary>
        /// Gain "+" button — parity with WinForms <c>btnGainUp_Click</c>. From auto it snaps to a
        /// fixed [-1000, 1000] range; otherwise it widens the current range by 1.5x (truncated to
        /// int). Labels show the range scaled by 0.01 and truncated to int.
        /// </summary>
        private void btnGainUp_Click(object sender, RoutedEventArgs e)
        {
            if (isAuto)
            {
                axisYMin = -1000;
                axisYMax = 1000;
                RedrawChart();
                lblMax.Text = ((int)(axisYMax * 0.01)).ToString(CultureInfo.InvariantCulture);
                lblMin.Text = ((int)(axisYMin * 0.01)).ToString(CultureInfo.InvariantCulture);
                isAuto = false;
                return;
            }

            axisYMin *= 1.5;
            axisYMax *= 1.5;
            axisYMin = (int)axisYMin;
            axisYMax = (int)axisYMax;
            RedrawChart();
            lblMax.Text = ((int)(axisYMax * 0.01)).ToString(CultureInfo.InvariantCulture);
            lblMin.Text = ((int)(axisYMin * 0.01)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gain "A" (auto) button — parity with WinForms <c>btnGainAuto_Click</c>: returns the Y
        /// axis to auto-scale (NaN bounds) and resets the labels to "Auto" / "".
        /// </summary>
        private void btnGainAuto_Click(object sender, RoutedEventArgs e)
        {
            axisYMax = double.NaN;
            axisYMin = double.NaN;
            RedrawChart();
            lblMax.Text = "Auto";
            lblMin.Text = string.Empty;
            isAuto = true;
        }

        /// <summary>
        /// Gain "-" button — parity with WinForms <c>btnGainDown_Click</c>. From auto it snaps to
        /// [-1000, 1000]; once the minimum has reached -200 it clamps to [-200, 200]; otherwise it
        /// narrows the current range by 0.66666x (truncated to int).
        /// </summary>
        private void btnGainDown_Click(object sender, RoutedEventArgs e)
        {
            if (isAuto)
            {
                axisYMin = -1000;
                axisYMax = 1000;
                RedrawChart();
                lblMax.Text = ((int)(axisYMax * 0.01)).ToString(CultureInfo.InvariantCulture);
                lblMin.Text = ((int)(axisYMin * 0.01)).ToString(CultureInfo.InvariantCulture);
                isAuto = false;
                return;
            }

            if (axisYMin >= -200)
            {
                axisYMin = -200;
                axisYMax = 200;
                RedrawChart();
                lblMax.Text = ((int)(axisYMax * 0.01)).ToString(CultureInfo.InvariantCulture);
                lblMin.Text = ((int)(axisYMin * 0.01)).ToString(CultureInfo.InvariantCulture);
                return;
            }

            axisYMin *= 0.66666;
            axisYMax *= 0.66666;
            axisYMin = (int)axisYMin;
            axisYMax = (int)axisYMax;
            RedrawChart();
            lblMax.Text = ((int)(axisYMax * 0.01)).ToString(CultureInfo.InvariantCulture);
            lblMin.Text = ((int)(axisYMin * 0.01)).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] Rebuilds the chart content inside the <c>unoChart</c> Canvas: clears it, draws the
        /// DimGray Y-axis gridlines (min/max boundaries and the zero reference when in range), then
        /// draws the ACTUAL (OrangeRed) and SETPOINT (Lime) polylines. This is the cross-platform
        /// equivalent of the WinForms <c>unoChart.ResetAutoValues()</c> + repaint — a plain
        /// <see cref="Canvas"/> does not reposition its children on <c>InvalidateVisual()</c> alone.
        /// </summary>
        private void RedrawChart()
        {
            if (unoChart == null)
            {
                return;
            }

            unoChart.Children.Clear();

            // Resolve the plot rectangle. Before the first layout pass Bounds can be zero, so fall
            // back to the Canvas's declared Width/Height (481 x 274 from the .axaml).
            double w = unoChart.Bounds.Width > 0 ? unoChart.Bounds.Width : unoChart.Width;
            double h = unoChart.Bounds.Height > 0 ? unoChart.Bounds.Height : unoChart.Height;
            if (double.IsNaN(w) || w <= 0)
            {
                w = 481;
            }

            if (double.IsNaN(h) || h <= 0)
            {
                h = 274;
            }

            // Resolve the Y range: explicit bounds when set, otherwise auto from the data.
            double yMin;
            double yMax;
            if (double.IsNaN(axisYMin) || double.IsNaN(axisYMax))
            {
                ComputeAutoRange(out yMin, out yMax);
            }
            else
            {
                yMin = axisYMin;
                yMax = axisYMax;
            }

            double range = yMax - yMin;
            if (range <= 0)
            {
                range = 1;
            }

            // Y-axis gridlines (DimGray, mirroring chartArea.AxisY.MajorGrid.LineColor = DimGray).
            AddGridLine(0, w);
            AddGridLine(h, w);
            if (yMin < 0 && yMax > 0)
            {
                double zeroPix = h - (((0 - yMin) / range) * h);
                AddGridLine(zeroPix, w);
            }

            // Series polylines. series "S" = ACTUAL (OrangeRed); series "PWM" = SETPOINT (Lime).
            AddSeriesPolyline(seriesActual, w, h, yMin, range, Brushes.OrangeRed);
            AddSeriesPolyline(seriesSetpoint, w, h, yMin, range, Brushes.Lime);

            unoChart.InvalidateVisual();
        }

        /// <summary>Adds a single horizontal DimGray gridline spanning the plot width.</summary>
        private void AddGridLine(double yPix, double width)
        {
            var line = new Line
            {
                StartPoint = new Point(0, yPix),
                EndPoint = new Point(width, yPix),
                Stroke = Brushes.DimGray,
                StrokeThickness = GridThickness,
            };
            unoChart.Children.Add(line);
        }

        /// <summary>
        /// Maps a sample buffer to pixel space and adds it as a <see cref="Polyline"/>. X is mapped
        /// by index across the full width (the samples are evenly spaced, matching the WinForms
        /// auto-scaled X over the rolling window); Y is normalized into the plot rectangle and
        /// inverted (Canvas Y grows downward).
        /// </summary>
        private void AddSeriesPolyline(List<Point> data, double width, double height, double yMin, double range, IBrush stroke)
        {
            int n = data.Count;
            if (n < 2)
            {
                return;
            }

            var pts = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double xPix = (i / (double)(n - 1)) * width;
                double yNorm = (data[i].Y - yMin) / range;
                double yPix = height - (yNorm * height);
                pts.Add(new Point(xPix, yPix));
            }

            var polyline = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = SeriesThickness,
                Points = pts,
            };
            unoChart.Children.Add(polyline);
        }

        /// <summary>
        /// Computes an auto Y range spanning both series (WinForms AxisY auto-scale). Falls back to
        /// [-1, 1] when no data exists and pads a degenerate (flat) range so the line stays visible.
        /// </summary>
        private void ComputeAutoRange(out double yMin, out double yMax)
        {
            bool any = false;
            double lo = double.MaxValue;
            double hi = double.MinValue;

            foreach (Point p in seriesActual)
            {
                if (p.Y < lo)
                {
                    lo = p.Y;
                }

                if (p.Y > hi)
                {
                    hi = p.Y;
                }

                any = true;
            }

            foreach (Point p in seriesSetpoint)
            {
                if (p.Y < lo)
                {
                    lo = p.Y;
                }

                if (p.Y > hi)
                {
                    hi = p.Y;
                }

                any = true;
            }

            if (!any)
            {
                yMin = -1;
                yMax = 1;
                return;
            }

            if (hi - lo < 1e-9)
            {
                lo -= 1;
                hi += 1;
            }

            yMin = lo;
            yMax = hi;
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for <c>ScreenHelper.IsOnScreen(Bounds)</c>: if the
        /// window's position is not contained by any connected screen, move it to the top-left
        /// origin (mirroring the WinForms <c>Top = 0; Left = 0;</c> fallback). Best-effort — does
        /// nothing when screen information is unavailable (e.g. a headless host).
        /// </summary>
        private void ClampToScreen()
        {
            var screens = Screens;
            if (screens == null)
            {
                return;
            }

            PixelPoint pos = Position;
            bool onScreen = false;
            foreach (var sc in screens.All)
            {
                if (sc.Bounds.Contains(pos))
                {
                    onScreen = true;
                    break;
                }
            }

            if (!onScreen)
            {
                Position = new PixelPoint(0, 0);
            }
        }

        /// <summary>
        /// Computes the timer interval in milliseconds from the gps rate, reproducing the WinForms
        /// expression <c>(int)((1 / gpsHz) * 1000)</c> for any finite, positive rate. Guards an
        /// invalid rate (zero, negative, NaN, or infinite) by returning the WinForms designer
        /// default of 200 ms, and caps the interval at 60 s.
        /// </summary>
        private static int ComputeIntervalMs(double gpsHz)
        {
            if (gpsHz <= 0 || double.IsNaN(gpsHz) || double.IsInfinity(gpsHz))
            {
                return 200;
            }

            double ms = (1 / gpsHz) * 1000;
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 1)
            {
                return 200;
            }

            if (ms > 60000)
            {
                return 60000;
            }

            return (int)ms;
        }
    }

    /// <summary>
    /// Steer-angle telemetry contract consumed by <see cref="FormGraphSteerView"/>. Replaces the
    /// WinForms <c>mf</c> (FormGPS) back-reference with an injected snapshot, exposing exactly the
    /// members the chart reads. The composition root (or a future Services-layer guidance object)
    /// supplies the implementation; member names mirror the original FormGPS fields/properties.
    /// </summary>
    public interface ISteerTelemetry
    {
        /// <summary>Actual steer angle plotted as series "S" (WinForms <c>mc.actualSteerAngleChart</c>).</summary>
        double actualSteerAngleChart { get; }

        /// <summary>Guidance/setpoint steer angle plotted as series "PWM" (WinForms <c>guidanceLineSteerAngle</c>).</summary>
        double guidanceLineSteerAngle { get; }

        /// <summary>Pre-formatted actual steer-angle readout for the OrangeRed label (WinForms <c>ActualSteerAngle</c>).</summary>
        string ActualSteerAngle { get; }

        /// <summary>Pre-formatted setpoint steer-angle readout for the Lime label (WinForms <c>SetSteerAngle</c>).</summary>
        string SetSteerAngle { get; }

        /// <summary>GPS fix rate in Hz; drives the redraw timer interval (WinForms <c>gpsHz</c>).</summary>
        double gpsHz { get; }
    }

    /// <summary>
    /// Inert <see cref="ISteerTelemetry"/> used by the parameterless (designer/loader) constructor.
    /// Reports a benign 10 Hz rate so the timer interval resolves to 100 ms (never a divide-by-zero)
    /// and flat zero readings so the chart renders without a live telemetry source.
    /// </summary>
    internal sealed class NullSteerTelemetry : ISteerTelemetry
    {
        /// <inheritdoc />
        public double actualSteerAngleChart => 0.0;

        /// <inheritdoc />
        public double guidanceLineSteerAngle => 0.0;

        /// <inheritdoc />
        public string ActualSteerAngle => "0";

        /// <inheritdoc />
        public string SetSteerAngle => "0";

        /// <inheritdoc />
        public double gpsHz => 10.0;
    }
}
