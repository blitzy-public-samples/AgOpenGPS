// [XPLAT] migrated from net48/WinForms FormTimedMessage.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Auto-dismissing "toast" notification window — a 1:1 parity reimplementation of the
    /// deleted WinForms <c>FormTimedMessage : Form</c> (<c>Forms/FormTimedMessage.cs</c> +
    /// <c>FormTimedMessage.Designer.cs</c>), rebuilt as an Avalonia <see cref="Window"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window shows a bold title over a message line — bound, through the
    /// <see cref="FormTimedMessageViewModel"/> set as its <c>DataContext</c>, to the
    /// paired <c>FormTimedMessage.axaml</c> view's <c>TitleText</c> and <c>MessageText</c> — and closes
    /// itself after a caller-supplied duration. It is shown non-modally, fire-and-forget, e.g.
    /// <c>new FormTimedMessage(ms, title, msg).Show(ownerWindow);</c>, and is the view raised by a
    /// concrete <c>IErrorPresenter.PresentTimedMessage(...)</c> implementation (the replacement for the
    /// former WinForms <c>TimedMessageBox(msec, title, message)</c> helper).
    /// </para>
    /// <para>
    /// Behaviour is ported 1:1 from the WinForms original: the single-shot
    /// <c>System.Windows.Forms.Timer</c> whose <c>Tick</c> disabled the timer and called <c>Close()</c>
    /// becomes a <see cref="DispatcherTimer"/> that, on its first tick, stops and detaches itself — so it
    /// neither fires again nor keeps this window alive — and then closes the popup. The WinForms
    /// width-grow heuristic (<c>Width = message.Length * 15 + 120</c>) is no longer computed here: the
    /// paired <c>FormTimedMessage.axaml</c> sizes to its content (<c>SizeToContent="WidthAndHeight"</c>)
    /// with a bounded <c>MaxWidth</c>, so an unusually long message wraps instead of producing a
    /// runaway-wide window.
    /// </para>
    /// </remarks>
    public partial class FormTimedMessage : Window
    {
        /// <summary>
        /// One-shot auto-close timer. Replaces the WinForms <c>System.Windows.Forms.Timer</c>: it is
        /// started in the constructor and stopped/detached on its first
        /// <see cref="DispatcherTimer.Tick"/>.
        /// </summary>
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Parameterless constructor required by the Avalonia runtime XAML loader / previewer (a public
        /// parameterless constructor is what the URI-based loader uses to instantiate the type; without
        /// one the compiler emits AVLN3001 and the paired <c>FormTimedMessage.axaml</c> resource is not
        /// reachable through that path). It only loads the XAML; real usage always goes through
        /// <see cref="FormTimedMessage(int, string, string)"/> (or the <see cref="TimeSpan"/> overload),
        /// which additionally binds the view-model and starts the auto-close timer. Matches the
        /// convention of the sibling AgIO views (<c>FormYes</c>, <c>FormPGN</c>, <c>MainWindow</c>).
        /// </summary>
        public FormTimedMessage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the popup, binds it to a freshly created <see cref="FormTimedMessageViewModel"/>,
        /// and starts the one-shot auto-close timer — mirroring the WinForms
        /// <c>FormTimedMessage(int timeInMsec, string titleStr, string messageStr)</c> constructor.
        /// </summary>
        /// <param name="milliseconds">
        /// Time, in milliseconds, the popup stays visible before it auto-closes (the former
        /// <c>timer1.Interval</c>).
        /// </param>
        /// <param name="titleStr">The bold title shown at the top (the former <c>lblTitle.Text</c>).</param>
        /// <param name="messageStr">
        /// The message body shown beneath the title (the former <c>lblMessage2.Text</c>).
        /// </param>
        public FormTimedMessage(int milliseconds, string titleStr, string messageStr)
        {
            InitializeComponent();

            // [XPLAT] WinForms lblTitle/lblMessage2 text -> data-bound view-model (FormTimedMessage.axaml).
            DataContext = new FormTimedMessageViewModel(titleStr, messageStr);

            // [XPLAT] one-shot auto-close timer (WinForms Timer.Interval + Tick -> DispatcherTimer).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        /// <summary>
        /// Convenience overload accepting a <see cref="TimeSpan"/> duration, so an
        /// <c>IErrorPresenter.PresentTimedMessage(TimeSpan, ...)</c> implementation can construct the
        /// popup directly. Forwards to <see cref="FormTimedMessage(int, string, string)"/>.
        /// </summary>
        /// <param name="duration">How long the popup stays visible before it auto-closes.</param>
        /// <param name="titleStr">The bold title shown at the top.</param>
        /// <param name="messageStr">The message body shown beneath the title.</param>
        public FormTimedMessage(TimeSpan duration, string titleStr, string messageStr)
            : this((int)duration.TotalMilliseconds, titleStr, messageStr)
        {
        }

        /// <summary>
        /// Loads the compiled XAML for this window. The explicit <see cref="AvaloniaXamlLoader"/> call
        /// matches the convention used by the sibling AgIO views (e.g. <c>FormYes</c>, <c>MainWindow</c>).
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// First (and only) timer tick: stop and detach the timer so it neither fires again nor keeps this
        /// window alive, then close the popup — the Avalonia equivalent of the WinForms
        /// <c>timer1_Tick</c> handler.
        /// </summary>
        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            Close();
        }
    }
}
