using System;
using Avalonia.Controls;
using Avalonia.Threading;

// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md

namespace ModSim.Views
{
    /// <summary>
    /// Auto-closing "toast" popup that shows a bold title and a message for a caller-supplied
    /// duration and then closes itself. This is the Avalonia <see cref="Window"/> reimplementation
    /// of the deleted WinForms <c>FormTimedMessage : Form</c>
    /// (<c>Forms/FormTimedMessage.cs</c> + <c>FormTimedMessage.Designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour is ported 1:1 from the WinForms original: the constructor assigns the
    /// title/message, widens the window proportionally to the message length
    /// (<c>Width = messageStr.Length * 15 + 120</c>), and starts a one-shot timer that closes the
    /// window once the requested delay elapses. The WinForms <c>System.Windows.Forms.Timer</c> is
    /// replaced by an Avalonia <see cref="DispatcherTimer"/>; Avalonia owns the window lifetime on
    /// <see cref="Window.Close()"/>, so (unlike the WinForms version) no explicit <c>Dispose()</c>
    /// call is required. ModSim is a standalone project — this view references no other project and
    /// uses no MVVM, exactly like the rest of the ModSim re-platform.
    /// </remarks>
    public partial class FormTimedMessageView : Window
    {
        // [XPLAT] one-shot auto-close timer; replaces the WinForms designer's System.Windows.Forms.Timer.
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Parameterless constructor required by the Avalonia XAML runtime loader and the design-time
        /// previewer. Because this view carries an <c>x:Class</c>, a public parameterless constructor
        /// must exist (otherwise the build emits AVLN3001); the real popup is always created through
        /// the three-argument constructor by <c>MainSimView.TimedMessageBox</c>.
        /// </summary>
        public FormTimedMessageView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup, assigns the title and message, sizes the window to the message length,
        /// and starts the auto-close timer — mirroring the WinForms
        /// <c>FormTimedMessage(int timeInMsec, string titleStr, string messageStr)</c> constructor.
        /// </summary>
        /// <param name="timeInMsec">Delay, in milliseconds, before the popup auto-closes.</param>
        /// <param name="titleStr">The bold title text shown at the top of the popup.</param>
        /// <param name="messageStr">The message text shown below the title.</param>
        public FormTimedMessageView(int timeInMsec, string titleStr, string messageStr)
            : this()
        {
            lblTitle.Text = titleStr;
            lblMessage2.Text = messageStr;

            // [XPLAT] preserve the original proportional sizing formula verbatim.
            Width = messageStr.Length * 15 + 120;

            // [XPLAT] WinForms Timer.Interval + Tick -> Avalonia DispatcherTimer (stopped on first tick).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeInMsec) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // [XPLAT] faithful equivalent of the source timer1_Tick: stop the one-shot timer and close.
        // Avalonia manages the window lifetime on Close(), so no explicit Dispose() is performed.
        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            Close();
        }
    }
}
