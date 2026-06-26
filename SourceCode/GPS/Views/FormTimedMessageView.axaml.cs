// [XPLAT] migrated from net48/WinForms (Forms/FormTimedMessage.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Self-dismissing transient notification (title + message).
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormTimedMessage</c> (FormTimedMessage.cs
    /// + .Designer.cs). The WinForms form set a title (<c>lblMessage</c>) and a message
    /// (<c>lblMessage2</c>), auto-sized to its content, anchored at screen (20,20), and closed itself
    /// after <c>timeInMsec</c> via a WinForms <c>Timer</c>.
    ///
    /// Avalonia equivalents: the WinForms <c>Timer</c> becomes a <see cref="DispatcherTimer"/> (UI
    /// thread), and the manual <c>ClientSize</c> calculation from <c>PreferredWidth/Height</c> becomes
    /// <c>SizeToContent="WidthAndHeight"</c> declared in the XAML, which yields the same padded
    /// auto-fit. The (20,20) placement maps to <see cref="WindowBase.Position"/>.
    ///
    /// Null-safety: the label assignments use <c>?? string.Empty</c>. This deliberately mirrors the
    /// fix applied to the ModSim timed-message view — a null title/message must never throw — and is
    /// the safe default for a TextBlock either way.
    /// </summary>
    public partial class FormTimedMessageView : Window
    {
        private DispatcherTimer _timer;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / design preview.
        /// </summary>
        public FormTimedMessageView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms
        /// <c>FormTimedMessage(int timeInMsec, string titleString, string messageString)</c> ctor:
        /// assigns the title/message, positions the window at (20,20), and starts the auto-close timer.
        /// </summary>
        public FormTimedMessageView(int timeInMsec, string titleString, string messageString)
            : this()
        {
            // [XPLAT] Null-safe text assignment (parity with the ModSim null-safety fix).
            lblMessage.Text = titleString ?? string.Empty;
            lblMessage2.Text = messageString ?? string.Empty;

            // [XPLAT] WinForms: this.Left = 20; this.Top = 20;
            Position = new PixelPoint(20, 20);

            // [XPLAT] WinForms timer1.Interval = timeInMsec; auto-close on tick.
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(timeInMsec > 0 ? timeInMsec : 1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // [XPLAT] Parity with FormTimedMessage.timer1_Tick: stop the timer and close once elapsed.
        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            Close();
        }

        // [XPLAT] Defensive: ensure the timer is stopped if the window is closed before the tick.
        protected override void OnClosed(EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }

            base.OnClosed(e);
        }
    }
}
