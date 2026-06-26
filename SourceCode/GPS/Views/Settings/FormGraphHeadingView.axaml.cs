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
//     partner markup (FormGraphHeadingView.axaml) re-hosts the two charts as plain <Border> plot
//     surfaces and the read-outs as <TextBlock>s; this code-behind keeps the four rolling
//     point-buffers and the sample timer that drove the original charts. The actual polyline
//     pixel-paint onto the two surfaces is the cross-platform charting work tracked in
//     MIGRATION_DOCS/TRANSITION_MAP.md (CP4/CP9) — the same deferred-render posture as the sibling
//     FormGraphSteerView; the three read-outs refresh live on every tick exactly as before.
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
