// [XPLAT] migrated from net48/WinForms Forms/FormTimedMessage.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Auto-closing "toast" popup showing a bold title and a message for a caller-supplied
    /// duration, reimplemented as an Avalonia <see cref="Window"/> replacing the deleted WinForms
    /// <c>FormTimedMessage : Form</c>. It is bound to a <see cref="FormTimedMessageViewModel"/>.
    /// </summary>
    /// <remarks>
    /// Behaviour ported 1:1: the message width grows proportionally to the message length
    /// (<c>Width = message.Length * 15 + 120</c>), and a one-shot timer closes the window after
    /// <c>timeInMsec</c>. The WinForms <c>System.Windows.Forms.Timer</c> becomes a
    /// <see cref="DispatcherTimer"/>.
    /// </remarks>
    public partial class FormTimedMessageView : Window
    {
        private DispatcherTimer _timer;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormTimedMessageView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup bound to <paramref name="viewModel"/>, sizes it to the message, and starts
        /// the auto-close timer — mirroring the WinForms <c>FormTimedMessage(int, string, string)</c> ctor.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the title and message.</param>
        /// <param name="timeInMsec">Time, in milliseconds, before the popup auto-closes.</param>
        public FormTimedMessageView(FormTimedMessageViewModel viewModel, int timeInMsec)
            : this()
        {
            DataContext = viewModel;

            // [XPLAT] preserve the original proportional sizing: Width = len * 15 + 120.
            int messWidth = viewModel?.MessageText?.Length ?? 0;
            Width = (messWidth * 15) + 120;

            // [XPLAT] one-shot auto-close timer (WinForms Timer.Interval + Tick -> DispatcherTimer).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeInMsec) };
            _timer.Tick += Timer1_Tick;
            _timer.Start();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        private void Timer1_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= Timer1_Tick;
            Close();
        }
    }
}
