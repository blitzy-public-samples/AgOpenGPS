// [XPLAT] migrated from net48/WinForms FormCommSetGPS.cs + FormCommSetGPS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Connect GPS" / GPS-comm-settings dialog — the largest AgIO configuration dialog —
    /// reimplemented as an Avalonia <see cref="Window"/> replacing the deleted WinForms
    /// <c>FormCommSetGPS : Form</c>. This is the code-behind half of <c>FormCommSetGPS.axaml</c>;
    /// together they form a single view bound to a <see cref="FormCommSetGPSViewModel"/> that
    /// configures and opens/closes AgIO's six serial ports (GPS, GPS2, RTCM, IMU, steer module,
    /// machine module) and surfaces the live receive traffic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It follows the same convention as the sibling Avalonia views in this folder
    /// (<c>FormRadioChannel</c>, <c>FormSource</c>, <c>FormYes</c>, <c>FormTimedMessage</c>): a
    /// parameterless constructor for the XAML loader / previewer plus a view-model overload that
    /// assigns <see cref="StyledElement.DataContext"/>.
    /// </para>
    /// <para>
    /// [XPLAT] The OK button binds to the view-model's <c>OkCommand</c>, but a command can neither close
    /// a window nor pop a toast — those are window-level actions. This code-behind therefore subscribes
    /// the view-model's intent events and performs the actions the WinForms <c>FormCommSetGPS</c> did
    /// directly:
    /// <list type="bullet">
    ///   <item><see cref="FormCommSetGPSViewModel.RequestClose"/> → <c>Close()</c>, reproducing the
    ///   WinForms <c>btnSerialOK_Click</c> → <c>Close()</c>.</item>
    ///   <item><see cref="FormCommSetGPSViewModel.RequestTimedMessage"/> (duration, title, message) →
    ///   shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog, replacing the
    ///   WinForms <c>MessageBox.Show("Unable to connect to Port")</c> popups while the dialog stays open.</item>
    /// </list>
    /// </para>
    /// <para>
    /// [XPLAT] The view-model owns a 500 ms display-refresh timer (the WinForms <c>timer1</c>). The
    /// window stops it in <see cref="OnClosed"/> via <see cref="FormCommSetGPSViewModel.Stop"/> so the
    /// timer never ticks against a closed dialog; <see cref="FormCommSetGPSViewModel.Stop"/> deliberately
    /// does NOT close any serial port (the ports outlive this dialog — they are owned by the shared
    /// <c>SerialCommService</c>). The event handlers are named methods (not lambdas) so they can be
    /// unsubscribed in <see cref="OnClosed"/>, preventing the view-model from holding the closed window
    /// alive.
    /// </para>
    /// </remarks>
    public partial class FormCommSetGPS : Window
    {
        // [XPLAT] Held so the view-model's intent events can be unsubscribed (and its timer stopped) in OnClosed.
        private FormCommSetGPSViewModel _viewModel;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormCommSetGPS()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the
        /// view-model's close / timed-message intent events to the corresponding window actions.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the six serial-port rows and live traffic.</param>
        public FormCommSetGPS(FormCommSetGPSViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // [XPLAT] OkCommand (bound in the .axaml) only raises RequestClose; the window performs the
            // actual close + toast that the WinForms FormCommSetGPS did inline.
            if (_viewModel != null)
            {
                _viewModel.RequestClose += OnRequestClose;
                _viewModel.RequestTimedMessage += OnRequestTimedMessage;
            }
        }

        // [XPLAT] WinForms btnSerialOK_Click -> Close(). The view-model exposes no dialog result, so the
        // window simply closes (the port configuration was already applied live as the operator changed it).
        private void OnRequestClose() => Close();

        // [XPLAT] WinForms "Unable to connect to Port" MessageBox -> the auto-closing AgIO timed-message
        // toast shown over this dialog (non-modal, so the dialog stays open for another attempt), mirroring
        // the original behaviour. FormTimedMessage exposes the (milliseconds, title, message) constructor.
        private void OnRequestTimedMessage(int timeInMsec, string title, string message)
        {
            FormTimedMessage toast = new FormTimedMessage(timeInMsec, title, message);
            toast.Show(this);
        }

        /// <summary>
        /// [XPLAT] Stop the view-model's 500 ms display timer and unsubscribe its intent events when the
        /// dialog closes, so the timer never ticks against a closed window and the view-model never keeps
        /// the closed window alive. Stopping the timer does NOT close any serial port (the shared ports
        /// outlive this dialog).
        /// </summary>
        /// <param name="e">The close-event payload.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.Stop();
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.RequestTimedMessage -= OnRequestTimedMessage;
                _viewModel = null;
            }

            base.OnClosed(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
