// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the live cross-track-error (XTE) / heading-error (HE) chart dialog.
// 1:1 parity reimplementation of the deleted WinForms Forms/Settings/FormGraphXTE
// (FormGraphXTE.cs + FormGraphXTE.Designer.cs).
//
// WHY THIS IS NOT A STRAIGHT PORT
//   The WinForms form hosted a System.Windows.Forms.DataVisualization.Charting.Chart ("unoChart")
//   with two FastLine series ("S" = heading error, "PWM" = cross-track error) and three gain
//   buttons that rescaled the chart's Y axis. DataVisualization is Windows-only and is removed by
//   the migration (AAP §0.5). It is reimplemented here by the lightweight, dependency-free
//   <see cref="XteChartControl"/> declared below — the control the partner markup
//   (FormGraphXTEView.axaml) references as <local:XteChartControl x:Name="unoChart" /> and which the
//   markup's own contract comment states is "defined by the partner code-behind". No new NuGet
//   dependency, and no System.Windows.Forms / DataVisualization / OpenTK types are referenced.
//
// DEPENDENCY INJECTION (replaces the WinForms "mf" FormGPS god-object)
//   The WinForms form read mf.vehicle.modeActualXTE, mf.vehicle.modeActualHeadingError and
//   mf.gpsHz directly off FormGPS. Per the AAP guidance/scan-loop decoupling (§0.6.1) this view
//   takes NO FormGPS reference; instead the composition root injects an <see cref="IXteTelemetry"/>
//   snapshot exposing exactly those three values. This keeps the dialog testable and free of the
//   central FormGPS back-reference while preserving behaviour exactly.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AgOpenGPS.Helpers;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Minimal telemetry contract consumed by <see cref="FormGraphXTEView"/> each timer tick,
    /// replacing the WinForms form's direct reads of the FormGPS "mf" reference
    /// (<c>mf.vehicle.modeActualXTE</c>, <c>mf.vehicle.modeActualHeadingError</c>, <c>mf.gpsHz</c>).
    /// The running guidance pipeline (or an adapter over the live <c>CVehicle</c>) implements this at
    /// the composition root; defining it here keeps the view decoupled from FormGPS and unit-testable.
    /// </summary>
    public interface IXteTelemetry
    {
        /// <summary>Current cross-track error, in metres (plotted as <c>value * 100</c> centimetres).</summary>
        double modeActualXTE { get; }

        /// <summary>Current heading error, in degrees (plotted rounded to one decimal place).</summary>
        double modeActualHeadingError { get; }

        /// <summary>GPS update rate in hertz; drives the redraw timer interval <c>(1 / gpsHz) * 1000</c> ms.</summary>
        double gpsHz { get; }
    }

    /// <summary>
    /// [XPLAT] Live XTE / heading-error chart window. Faithful Avalonia reimplementation of the
    /// WinForms <c>FormGraphXTE</c>: a black plot of two rolling series with a ±Y gain control
    /// ("+", "A", "-") and live numeric read-outs for heading error and XTE.
    /// </summary>
    public partial class FormGraphXTEView : Window
    {
        // ----- chart data state (mirrors the WinForms FormGraphXTE fields) -----

        /// <summary>Last formatted heading-error read-out (series "S"); design-time seed "0".</summary>
        private string dataSteerAngle = "0";

        /// <summary>Last formatted cross-track-error read-out in cm (series "PWM"); design-time seed "0".</summary>
        private string dataPWM = "0";

        /// <summary>True while the Y axis is auto-ranging; flipped to fixed ±80 on first gain interaction.</summary>
        private bool isAuto = true;

        // [XPLAT] Replaces the WinForms System.Windows.Forms.Timer "timer1" with a UI-thread DispatcherTimer.
        private DispatcherTimer timer1;

        // [XPLAT] Injected telemetry snapshot (the WinForms "mf"); null only for the design-time/preview ctor.
        private readonly IXteTelemetry _tel;

        // [XPLAT] Guards the one-shot "Load" work (WinForms Load fires once; Avalonia OnOpened may re-fire).
        private bool _loaded;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / design-time preview.
        /// Performs the WinForms <c>FormGraphXTE</c> constructor parity: load the markup and set the
        /// hard-coded legend literals and window title (literals, NOT gStr — matching the WinForms ctor).
        /// </summary>
        public FormGraphXTEView()
        {
            InitializeComponent();

            // [XPLAT] WinForms ctor: this.label5.Text = "HE"; this.label1.Text = "XTE"; this.Text = "XTE Chart";
            label5.Text = "HE";
            label1.Text = "XTE";
            Title = "XTE Chart";
        }

        /// <summary>
        /// [XPLAT] Production constructor. Parity with the WinForms <c>FormGraphXTE(Form callingForm)</c>
        /// ctor, except the FormGPS "mf" reference is replaced by an injected <see cref="IXteTelemetry"/>
        /// snapshot supplied by the composition root.
        /// </summary>
        /// <param name="telemetry">Live XTE / heading-error / gpsHz source (must not be null).</param>
        public FormGraphXTEView(IXteTelemetry telemetry)
            : this()
        {
            _tel = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormSteerGraph_Load</c> parity. Avalonia raises <c>Opened</c> once the
        /// window is shown (the cross-platform analogue of the WinForms Load event), so the one-time
        /// initialisation lives here: start the redraw timer at the GPS rate, seed the fixed ±80 axis
        /// and its read-out labels, and recover the window if its persisted position is off-screen.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_loaded)
            {
                return;
            }
            _loaded = true;

            // [XPLAT] WinForms: unoChart.ChartAreas[0].AxisY.Minimum = -80 / Maximum = 80; then the labels
            // were set from ((int)Maximum)/((int)Minimum) + " cm" and isAuto = false.
            unoChart.AxisYMin = -80;
            unoChart.AxisYMax = 80;
            lblMax.Text = ((int)unoChart.AxisYMax).ToString(CultureInfo.InvariantCulture) + " cm";
            lblMin.Text = ((int)unoChart.AxisYMin).ToString(CultureInfo.InvariantCulture) + " cm";
            isAuto = false;
            unoChart.InvalidateVisual();

            // [XPLAT] Off-screen recovery. WinForms: if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }
            // ScreenHelper now resolves the monitor topology from the window's own Avalonia Screens collection.
            if (!ScreenHelper.IsOnScreen(this))
            {
                Position = new PixelPoint(0, 0);
            }

            // [XPLAT] WinForms: timer1.Interval = (int)((1 / mf.gpsHz) * 1000); (the System.Windows.Forms.Timer
            // was Enabled in the designer). The DispatcherTimer is only armed when telemetry is present;
            // the interval is clamped to >= 1 ms (the WinForms Timer.Interval likewise rejected 0).
            if (_tel != null)
            {
                double hz = _tel.gpsHz;
                int interval = hz > 0 ? (int)((1.0 / hz) * 1000.0) : 200;
                if (interval < 1)
                {
                    interval = 1;
                }

                timer1 = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(interval)
                };
                timer1.Tick += OnTimerTick;
                timer1.Start();
            }
        }

        /// <summary>
        /// [XPLAT] Stop the redraw timer when the window closes (the agent_prompt's "OnClosing → timer1?.Stop()";
        /// implemented via the repo's standard <see cref="Window.OnClosed"/> timer-cleanup convention).
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            if (timer1 != null)
            {
                timer1.Stop();
                timer1.Tick -= OnTimerTick;
                timer1 = null;
            }

            base.OnClosed(e);
        }

        // [XPLAT] WinForms timer1_Tick: redraw on every timer tick.
        private void OnTimerTick(object sender, EventArgs e)
        {
            DrawChart();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>DrawChart()</c> parity (rolling window of 120 points). Reads the live XTE /
        /// heading-error sample from the injected telemetry, formats the read-outs with
        /// <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5 numeric-culture rule), appends the pair to
        /// the chart's two series and trims each to the most recent 120 samples, then repaints.
        /// </summary>
        private void DrawChart()
        {
            if (_tel == null)
            {
                return;
            }

            // WinForms: dataPWM = ((int)(mf.vehicle.modeActualXTE * 100)).ToString(InvariantCulture);
            int xteCm = (int)(_tel.modeActualXTE * 100);
            // WinForms: dataSteerAngle = (Math.Round(mf.vehicle.modeActualHeadingError, 1)).ToString(InvariantCulture);
            double headingError = Math.Round(_tel.modeActualHeadingError, 1);

            dataPWM = xteCm.ToString(CultureInfo.InvariantCulture);
            dataSteerAngle = headingError.ToString(CultureInfo.InvariantCulture);

            // WinForms: lblSteerAng.Text = dataSteerAngle + "\u00B0"; lblPWM.Text = dataPWM + " cm";
            lblSteerAng.Text = dataSteerAngle + "\u00B0";
            lblPWM.Text = dataPWM + " cm";

            // WinForms appended dataSteerAngle to series "S" and dataPWM to series "PWM", then trimmed each
            // series to the most recent 120 points. XteChartControl.AddSample performs the same append + trim.
            unoChart.AddSample(headingError, xteCm);
            unoChart.InvalidateVisual();
        }

        // ----- Y-gain buttons (wired from the markup via Click="..."). Exact WinForms arithmetic. -----

        /// <summary>
        /// [XPLAT] WinForms <c>btnGainUp_Click</c>: increase the Y scale. From auto, snap to fixed ±80;
        /// otherwise double the span (floored to whole numbers) up to a ±5120 cap.
        /// </summary>
        private void OnGainUpClick(object sender, RoutedEventArgs e)
        {
            if (isAuto)
            {
                unoChart.AxisYMin = -80;
                unoChart.AxisYMax = 80;
                UpdateAxisLabels();
                isAuto = false;
                unoChart.InvalidateVisual();
                return;
            }

            if (unoChart.AxisYMax >= 5120)
            {
                unoChart.AxisYMin = -5120;
                unoChart.AxisYMax = 5120;
                UpdateAxisLabels();
                unoChart.InvalidateVisual();
                return;
            }

            unoChart.AxisYMin *= 2;
            unoChart.AxisYMax *= 2;
            unoChart.AxisYMin = (int)unoChart.AxisYMin;
            unoChart.AxisYMax = (int)unoChart.AxisYMax;
            UpdateAxisLabels();
            unoChart.InvalidateVisual();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnGainAuto_Click</c>: switch the Y axis to auto-range. The chart treats
        /// NaN bounds as "fit to data"; the labels show "Auto" / "" as in WinForms.
        /// </summary>
        private void OnGainAutoClick(object sender, RoutedEventArgs e)
        {
            unoChart.AxisYMax = double.NaN;
            unoChart.AxisYMin = double.NaN;
            lblMax.Text = "Auto";
            lblMin.Text = "";
            isAuto = true;
            unoChart.InvalidateVisual();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnGainDown_Click</c>: decrease the Y scale. From auto, snap to fixed ±80;
        /// otherwise halve the span (floored to whole numbers) down to a ±10 floor.
        /// </summary>
        private void OnGainDownClick(object sender, RoutedEventArgs e)
        {
            if (isAuto)
            {
                unoChart.AxisYMin = -80;
                unoChart.AxisYMax = 80;
                UpdateAxisLabels();
                isAuto = false;
                unoChart.InvalidateVisual();
                return;
            }

            if (unoChart.AxisYMin >= -19.999999)
            {
                unoChart.AxisYMin = -10;
                unoChart.AxisYMax = 10;
                UpdateAxisLabels();
                unoChart.InvalidateVisual();
                return;
            }

            unoChart.AxisYMin *= 0.5;
            unoChart.AxisYMax *= 0.5;
            unoChart.AxisYMin = (int)unoChart.AxisYMin;
            unoChart.AxisYMax = (int)unoChart.AxisYMax;
            UpdateAxisLabels();
            unoChart.InvalidateVisual();
        }

        /// <summary>
        /// [XPLAT] Mirror the chart's current symmetric Y bounds into the max/min read-out labels with the
        /// " cm" suffix, formatted with <see cref="CultureInfo.InvariantCulture"/>. Every gain path leaves
        /// the bounds at whole numbers, so this matches the WinForms <c>(Maximum/Minimum).ToString() + " cm"</c>.
        /// </summary>
        private void UpdateAxisLabels()
        {
            lblMax.Text = unoChart.AxisYMax.ToString(CultureInfo.InvariantCulture) + " cm";
            lblMin.Text = unoChart.AxisYMin.ToString(CultureInfo.InvariantCulture) + " cm";
        }
    }

    /// <summary>
    /// [XPLAT] Dependency-free cross-platform replacement for the Windows-only
    /// <c>System.Windows.Forms.DataVisualization</c> chart ("unoChart") that the WinForms
    /// <c>FormGraphXTE</c> hosted. Defined in the partner code-behind exactly as the
    /// FormGraphXTEView.axaml contract comment specifies, and referenced from that markup as
    /// <c>&lt;local:XteChartControl x:Name="unoChart" /&gt;</c>.
    ///
    /// <para>Keeps two rolling buffers, each capped at <see cref="Capacity"/> = 120 (the WinForms scroll
    /// window): series <b>"S"</b> (heading error, OrangeRed) and <b>"PWM"</b> (cross-track error, Lime),
    /// drawn as polylines over DimGray gridlines on a black background — the exact WinForms series names,
    /// colours and back-colour from FormGraphXTE.Designer.cs.</para>
    ///
    /// <para>The dialog's three gain buttons drive the settable <see cref="AxisYMin"/> / <see cref="AxisYMax"/>
    /// bounds; assigning <see cref="double.NaN"/> to either selects auto-range (fit to the buffered data),
    /// reproducing the WinForms <c>RecalculateAxesScale()</c> behaviour.</para>
    /// </summary>
    public class XteChartControl : Control
    {
        /// <summary>Maximum retained samples per series (the WinForms 120-point scroll window).</summary>
        public const int Capacity = 120;

        // Series "S" = heading error (OrangeRed); series "PWM" = cross-track error (Lime).
        private readonly List<double> _s = new List<double>(Capacity);
        private readonly List<double> _pwm = new List<double>(Capacity);

        // Pens: series BorderWidth was 2 in FormGraphXTE.Designer.cs; gridlines use the chart's DimGray.
        private static readonly IPen PenS = new Pen(Brushes.OrangeRed, 2);
        private static readonly IPen PenPwm = new Pen(Brushes.Lime, 2);
        private static readonly IPen PenGrid = new Pen(Brushes.DimGray, 1);

        /// <summary>[XPLAT] Upper Y bound (WinForms AxisY.Maximum). Default +80; NaN selects auto-range.</summary>
        public double AxisYMax { get; set; } = 80;

        /// <summary>[XPLAT] Lower Y bound (WinForms AxisY.Minimum). Default -80; NaN selects auto-range.</summary>
        public double AxisYMin { get; set; } = -80;

        /// <summary>[XPLAT] Count of buffered "S" (heading-error) samples — exposed for parity tests.</summary>
        public int SeriesSCount => _s.Count;

        /// <summary>[XPLAT] Count of buffered "PWM" (cross-track-error) samples — exposed for parity tests.</summary>
        public int SeriesPwmCount => _pwm.Count;

        /// <summary>
        /// [XPLAT] Append one (heading-error, cross-track-error) sample pair, trimming each series to the
        /// most recent <see cref="Capacity"/> points (the WinForms <c>while (Points.Count &gt; 120) RemoveAt(0)</c>).
        /// Repaint is requested by the caller via <see cref="Visual.InvalidateVisual"/>, matching the
        /// WinForms code path.
        /// </summary>
        /// <param name="s">Heading-error sample for series "S".</param>
        /// <param name="pwm">Cross-track-error sample (centimetres) for series "PWM".</param>
        public void AddSample(double s, double pwm)
        {
            Push(_s, s);
            Push(_pwm, pwm);
        }

        /// <summary>[XPLAT] Drop all buffered samples (e.g. when the dialog is reopened).</summary>
        public void Clear()
        {
            _s.Clear();
            _pwm.Clear();
        }

        private static void Push(List<double> buf, double v)
        {
            buf.Add(v);
            while (buf.Count > Capacity)
            {
                buf.RemoveAt(0);
            }
        }

        /// <summary>
        /// [XPLAT] Render the two series over DimGray gridlines on the black plot background. The visible Y
        /// range is the fixed [<see cref="AxisYMin"/>, <see cref="AxisYMax"/>] span, or — when either bound
        /// is NaN — auto-fit to the buffered samples (WinForms auto axis). Out-of-range points are clamped so
        /// spikes stay on-canvas.
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

            // DimGray major gridlines (top, middle, bottom) — the chart's MajorGrid.LineColor = DimGray.
            double mid = bounds.Height / 2.0;
            context.DrawLine(PenGrid, new Point(0, 0), new Point(bounds.Width, 0));
            context.DrawLine(PenGrid, new Point(0, mid), new Point(bounds.Width, mid));
            context.DrawLine(PenGrid, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

            DrawSeries(context, _s, PenS, bounds, min, max);
            DrawSeries(context, _pwm, PenPwm, bounds, min, max);
        }

        /// <summary>
        /// Compute the effective [min, max] Y range: the fixed axis bounds, or an auto-fit to the buffered
        /// data when either bound is NaN. Degenerate (flat / empty) ranges are padded so mapping never
        /// divides by zero.
        /// </summary>
        private void ResolveRange(out double min, out double max)
        {
            min = AxisYMin;
            max = AxisYMax;

            if (double.IsNaN(min) || double.IsNaN(max))
            {
                min = double.MaxValue;
                max = double.MinValue;
                ExpandRange(_s, ref min, ref max);
                ExpandRange(_pwm, ref min, ref max);
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

        private static void ExpandRange(List<double> buf, ref double min, ref double max)
        {
            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i] < min) min = buf[i];
                if (buf[i] > max) max = buf[i];
            }
        }

        /// <summary>
        /// Stroke <paramref name="buf"/> as a straight polyline (WinForms <c>SeriesChartType.FastLine</c>),
        /// mapping each value from [min, max] onto [height, 0] and clamping out-of-range points to the canvas.
        /// </summary>
        private static void DrawSeries(DrawingContext ctx, List<double> buf, IPen pen, Rect bounds, double min, double max)
        {
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
}
