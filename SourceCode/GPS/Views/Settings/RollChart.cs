// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormCorrection — DataVisualization rollChart) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Lightweight cross-platform replacement for the Windows-only
    /// <c>System.Windows.Forms.DataVisualization</c> <c>rollChart</c> used by the WinForms
    /// <c>FormCorrection</c> roll/antenna-sway diagnostic. The original Windows-only charting control
    /// was dropped during the migration (AAP §0.5); this control reproduces its three step-line series
    /// with plain Avalonia drawing — no new NuGet dependency and no Windows-only types.
    ///
    /// <para>It holds three rolling buffers (each capped at the most recent <see cref="Capacity"/>
    /// samples), matching the WinForms series names and colours exactly:
    /// <list type="bullet">
    /// <item><description><b>Ro</b> — roll / correction distance — Red (stroke width 2).</description></item>
    /// <item><description><b>Ze</b> — corrected easting — Lime.</description></item>
    /// <item><description><b>Oe</b> — uncorrected easting — Cyan.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Data feed is host-owned (deferred):</b> the live sample source is the running
    /// <c>FormGPS</c> roll pipeline. This control exposes <see cref="AddSample"/> / <see cref="Clear"/>
    /// so the host pushes samples each GPS tick; the control owns only the buffering, auto-scaling and
    /// rendering. No <c>FormGPS</c> reference is taken here.</para>
    /// </summary>
    public class RollChart : Control
    {
        /// <summary>Maximum retained samples per series (WinForms kept the last 50).</summary>
        public const int Capacity = 50;

        private readonly List<double> _ro = new List<double>(Capacity);
        private readonly List<double> _ze = new List<double>(Capacity);
        private readonly List<double> _oe = new List<double>(Capacity);

        // Per-series pens, colours copied verbatim from FormCorrection.Designer.cs.
        private static readonly Pen PenRo = new Pen(Brushes.Red, 2);
        private static readonly Pen PenZe = new Pen(Brushes.Lime, 1);
        private static readonly Pen PenOe = new Pen(Brushes.Cyan, 1);

        /// <summary>
        /// [XPLAT] Append one sample to each series (roll correction, corrected easting, uncorrected
        /// easting) and request a repaint. Oldest samples beyond <see cref="Capacity"/> are dropped.
        /// </summary>
        public void AddSample(double ro, double ze, double oe)
        {
            Push(_ro, ro);
            Push(_ze, ze);
            Push(_oe, oe);
            InvalidateVisual();
        }

        /// <summary>[XPLAT] Drop all buffered samples (e.g. when the dialog reopens) and repaint.</summary>
        public void Clear()
        {
            _ro.Clear();
            _ze.Clear();
            _oe.Clear();
            InvalidateVisual();
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

            // The surrounding Border supplies the black backdrop; fill defensively in case this control
            // is used standalone so the plot area is always black (WinForms ChartArea.BackColor = Black).
            context.FillRectangle(Brushes.Black, bounds);

            // Auto-scale every series together to the control bounds (parity with the WinForms auto axis).
            double min = double.MaxValue, max = double.MinValue;
            ExpandRange(_ro, ref min, ref max);
            ExpandRange(_ze, ref min, ref max);
            ExpandRange(_oe, ref min, ref max);
            if (min > max)
            {
                return; // no samples yet
            }
            if (Math.Abs(max - min) < 1e-9)
            {
                // Flat data — pad so the line sits mid-height instead of dividing by zero.
                min -= 1;
                max += 1;
            }

            DrawStepLine(context, _oe, PenOe, bounds, min, max);
            DrawStepLine(context, _ze, PenZe, bounds, min, max);
            DrawStepLine(context, _ro, PenRo, bounds, min, max);
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
        /// Stroke <paramref name="buf"/> as a step-line (value held constant until the next sample),
        /// matching the WinForms <c>SeriesChartType.StepLine</c>.
        /// </summary>
        private static void DrawStepLine(DrawingContext ctx, List<double> buf, Pen pen, Rect bounds, double min, double max)
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
                double y = h - ((buf[i] - min) / range) * h;
                var cur = new Point(x, y);
                if (have)
                {
                    // horizontal segment at the previous level, then vertical to the new level (step).
                    var corner = new Point(cur.X, prev.Y);
                    ctx.DrawLine(pen, prev, corner);
                    ctx.DrawLine(pen, corner, cur);
                }
                prev = cur;
                have = true;
            }
        }
    }
}
