// [XPLAT] migrated from net48/WinForms Forms/FormTimedMessage.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ModSim.Views
{
    /// <summary>
    /// Auto-closing "toast" popup showing a bold title and a message for a caller-supplied duration,
    /// reimplemented as an Avalonia <see cref="Window"/> replacing the deleted WinForms
    /// <c>FormTimedMessage : Form</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour ported 1:1: the constructor assigns the title/message and widens the window
    /// proportionally to the message length (<c>Width = message.Length * 15 + 120</c>), and a one-shot
    /// timer closes the window after <c>timeInMsec</c>. The WinForms <c>System.Windows.Forms.Timer</c>
    /// becomes a <see cref="DispatcherTimer"/>.
    /// </remarks>
    public partial class FormTimedMessageView : Window
    {
        private readonly DispatcherTimer _timer;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormTimedMessageView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup, assigns the title/message, sizes it to the message, and starts the
        /// auto-close timer — mirroring the WinForms <c>FormTimedMessage(int, string, string)</c> ctor.
        /// </summary>
        /// <param name="timeInMsec">Time, in milliseconds, before the popup auto-closes.</param>
        /// <param name="titleStr">The bold title text.</param>
        /// <param name="messageStr">The message text.</param>
        public FormTimedMessageView(int timeInMsec, string titleStr, string messageStr)
            : this()
        {
            lblTitle.Text = titleStr;
            lblMessage2.Text = messageStr;

            // [XPLAT] preserve the original proportional sizing: Width = len * 15 + 120.
            int messWidth = messageStr?.Length ?? 0;
            Width = (messWidth * 15) + 120;

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
    }
}
