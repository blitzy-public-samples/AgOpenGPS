// [XPLAT] migrated from net48/WinForms Forms/FormTimedMessage.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Auto-closing "toast" popup showing a bold title and a message for a caller-supplied
    /// duration, reimplemented as an Avalonia <see cref="Window"/> replacing the deleted WinForms
    /// <c>FormTimedMessage : Form</c>. It is bound to a <see cref="FormTimedMessageViewModel"/>.
    /// </summary>
    /// <remarks>
    /// Behaviour ported 1:1: a one-shot timer closes the window after <c>timeInMsec</c>. The WinForms
    /// <c>System.Windows.Forms.Timer</c> becomes a <see cref="DispatcherTimer"/>. Unlike the WinForms
    /// original, the borderless popup sizes to its content (the view's <c>SizeToContent</c> with a
    /// bounded <c>MaxWidth</c>), so no explicit width is computed here.
    /// </remarks>
    public partial class FormTimedMessage : Window
    {
        private DispatcherTimer _timer;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormTimedMessage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup bound to <paramref name="viewModel"/> and starts the auto-close timer —
        /// mirroring the WinForms <c>FormTimedMessage(int, string, string)</c> ctor.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the title and message.</param>
        /// <param name="timeInMsec">Time, in milliseconds, before the popup auto-closes.</param>
        public FormTimedMessage(FormTimedMessageViewModel viewModel, int timeInMsec)
            : this()
        {
            DataContext = viewModel;

            // [XPLAT] one-shot auto-close timer (WinForms Timer.Interval + Tick -> DispatcherTimer).
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeInMsec) };
            _timer.Tick += Timer1_Tick;
            _timer.Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            _timer.Tick -= Timer1_Tick;
            Close();
        }
    }
}
