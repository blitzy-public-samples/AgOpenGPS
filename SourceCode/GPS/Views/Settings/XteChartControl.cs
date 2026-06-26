// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormGraphXTE — DataVisualization unoChart) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Cross-platform replacement for the Windows-only
    /// <c>System.Windows.Forms.DataVisualization</c> <c>unoChart</c> hosted by the WinForms
    /// <c>FormGraphXTE</c> cross-track-error / heading-error chart. DataVisualization is Windows-only
    /// and was removed by the migration (AAP §0.5); this control reproduces the two rolling series with
    /// plain Avalonia drawing — no new NuGet dependency and no Windows-only types.
    ///
    /// <para>It keeps two rolling buffers (each capped at <see cref="Capacity"/>) matching the WinForms
    /// series and colours: <b>S</b> (heading error, legend "HE") = OrangeRed, and <b>PWM</b> (XTE,
    /// legend "XTE") = Lime, drawn over DimGray axes on a black background.</para>
    ///
    /// <para>The three on-dialog gain buttons drive <see cref="GainUp"/>, <see cref="GainDown"/> and
    /// <see cref="GainAuto"/>; the resulting symmetric Y span is published via <see cref="YMax"/> /
    /// <see cref="YMin"/> for the dialog's max/min readout labels.</para>
    ///
    /// <para><b>Data feed is host-owned (deferred):</b> the running <c>FormGPS</c> guidance loop pushes
    /// samples through <see cref="AddSample"/>; this control owns only buffering, gain and rendering and
    /// takes no <c>FormGPS</c> reference.</para>
    /// </summary>
    public class XteChartControl : Control
    {
        /// <summary>Maximum retained samples per series.</summary>
        public const int Capacity = 100;

        private const double MinSpan = 1.0;     // tightest zoom (±1)
        private const double MaxSpan = 1000.0;  // widest zoom (±1000)
        private const double DefaultSpan = 50.0;

        private readonly List<double> _s = new List<double>(Capacity);
        private readonly List<double> _pwm = new List<double>(Capacity);

        // Symmetric span: the chart shows -_span .. +_span on the Y axis.
        private double _span = DefaultSpan;
        private bool _auto;

        private static readonly Pen PenS = new Pen(Brushes.OrangeRed, 1);
        private static readonly Pen PenPwm = new Pen(Brushes.Lime, 1);
        private static readonly Pen PenAxis = new Pen(Brushes.DimGray, 1);

        /// <summary>[XPLAT] Upper Y bound currently displayed (for the dialog's max readout).</summary>
        public double YMax => _span;

        /// <summary>[XPLAT] Lower Y bound currently displayed (for the dialog's min readout).</summary>
        public double YMin => -_span;

        /// <summary>[XPLAT] Append one (heading-error, XTE) sample pair and repaint.</summary>
        public void AddSample(double s, double pwm)
        {
            Push(_s, s);
            Push(_pwm, pwm);
            if (_auto)
            {
                RecomputeAutoSpan();
            }
            InvalidateVisual();
        }

        /// <summary>[XPLAT] Drop all buffered samples and repaint.</summary>
        public void Clear()
        {
            _s.Clear();
            _pwm.Clear();
            InvalidateVisual();
        }

        /// <summary>[XPLAT] Zoom in (halve the Y span), clamped at <see cref="MinSpan"/>. Cancels auto.</summary>
        public void GainUp()
        {
            _auto = false;
            _span = Math.Max(MinSpan, _span / 2.0);
            InvalidateVisual();
        }

        /// <summary>[XPLAT] Zoom out (double the Y span), clamped at <see cref="MaxSpan"/>. Cancels auto.</summary>
        public void GainDown()
        {
            _auto = false;
            _span = Math.Min(MaxSpan, _span * 2.0);
            InvalidateVisual();
        }

        /// <summary>[XPLAT] Auto-fit the Y span to the buffered samples and keep auto-fitting.</summary>
        public void GainAuto()
        {
            _auto = true;
            RecomputeAutoSpan();
            InvalidateVisual();
        }

        private void RecomputeAutoSpan()
        {
            double peak = 0;
            for (int i = 0; i < _s.Count; i++) peak = Math.Max(peak, Math.Abs(_s[i]));
            for (int i = 0; i < _pwm.Count; i++) peak = Math.Max(peak, Math.Abs(_pwm[i]));
            if (peak < MinSpan) peak = MinSpan;
            _span = Math.Min(MaxSpan, peak);
        }

        private static void Push(List<double> buf, double v)
        {
            buf.Add(v);
            if (buf.Count > Capacity)
            {
                buf.RemoveAt(0);
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            context.FillRectangle(Brushes.Black, bounds);

            // Zero baseline + top/bottom guide lines (DimGray axes, parity with the chart gridlines).
            double mid = bounds.Height / 2.0;
            context.DrawLine(PenAxis, new Point(0, mid), new Point(bounds.Width, mid));
            context.DrawLine(PenAxis, new Point(0, 0), new Point(bounds.Width, 0));
            context.DrawLine(PenAxis, new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height));

            DrawSeries(context, _s, PenS, bounds);
            DrawSeries(context, _pwm, PenPwm, bounds);
        }

        private void DrawSeries(DrawingContext ctx, List<double> buf, Pen pen, Rect bounds)
        {
            int n = buf.Count;
            if (n < 2)
            {
                return;
            }

            double w = bounds.Width;
            double h = bounds.Height;
            double full = 2.0 * _span;

            Point prev = default;
            bool have = false;
            for (int i = 0; i < n; i++)
            {
                double x = w * i / (n - 1);
                // Map [-span, +span] -> [h, 0]; clamp so out-of-range spikes stay on-canvas.
                double norm = (buf[i] + _span) / full;
                if (norm < 0) norm = 0;
                else if (norm > 1) norm = 1;
                double y = h - norm * h;
                var cur = new Point(x, y);
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
