// [XPLAT] migrated from net48/WinForms frmHelp.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace GPS_Out.Views
{
    /// <summary>
    /// Auto-dismissing, click-to-close help/notification popup, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>frmHelp : Form</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour ported 1:1 from <c>frmHelp</c>: the constructor assigns the title/message,
    /// sizes the window height proportionally to the message length
    /// (<c>20 + (len / 34) * 40</c>, clamped to [150, 500]), paints the DayColour background, and
    /// starts a one-shot auto-close timer. Clicking the surface closes the popup immediately
    /// (<see cref="OnSurfacePressed"/>). The WinForms <c>System.Windows.Forms.Timer</c> becomes a
    /// <see cref="DispatcherTimer"/>; the saved-geometry persistence of the transient popup is dropped.
    /// </remarks>
    public partial class HelpWindow : Window
    {
        private readonly DispatcherTimer _timer;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public HelpWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup, assigns the title/message, sizes it to the message, paints the
        /// DayColour background and starts the auto-close timer — mirroring the WinForms
        /// <c>frmHelp(frmStart, string, string, int)</c> constructor (the calling-form back-reference
        /// is no longer needed).
        /// </summary>
        /// <param name="message">The help/notification text.</param>
        /// <param name="title">The window title (defaults to "Help").</param>
        /// <param name="timeInMsec">Time, in milliseconds, before the popup auto-closes.</param>
        public HelpWindow(string message, string title = "Help", int timeInMsec = 30000)
            : this()
        {
            Title = title;
            label1.Text = message;

            // [XPLAT] dynamic height ported verbatim from frmHelp: 20 + (len / 34) * 40, clamped [150, 500].
            int len = message?.Length ?? 0;
            int ht = 20 + ((len / 34) * 40);
            if (ht < 150) ht = 150;
            else if (ht > 500) ht = 500;
            Height = ht;

            // [XPLAT] DayColour background parity (frmHelp set this.BackColor = Settings.DayColour).
            // App.axaml.cs parses the persisted "R, G, B" string into the DayBrush application resource.
            if (Application.Current is { } app &&
                app.TryGetResource("DayBrush", null, out object res) && res is IBrush brush)
            {
                Background = brush;
            }

            // [XPLAT] one-shot auto-close timer (WinForms Timer.Interval + Tick -> DispatcherTimer).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeInMsec) };
            _timer.Tick += Timer1_Tick;
            _timer.Start();
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= Timer1_Tick;
            Close();
        }

        /// <summary>Click anywhere on the popup surface closes it immediately (parity with frmHelp.panel1_Click).</summary>
        private void OnSurfacePressed(object sender, PointerPressedEventArgs e)
        {
            Close();
        }
    }
}
