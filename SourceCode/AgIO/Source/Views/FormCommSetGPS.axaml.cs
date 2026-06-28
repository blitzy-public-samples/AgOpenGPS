// [XPLAT] migrated from net48/WinForms FormCommSetGPS.cs + FormCommSetGPS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Connect GPS" / GPS-comm-settings dialog — the largest AgIO configuration dialog —
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormCommSetGPS : Form</c> (<c>Forms/FormCommSetGPS.cs</c> + <c>Forms/FormCommSetGPS.Designer.cs</c>).
    /// This is the code-behind half of <c>FormCommSetGPS.axaml</c>; together they form a single view bound
    /// to a <see cref="FormCommSetGPSViewModel"/> that configures and opens/closes AgIO's six serial ports
    /// (GPS, GPS2, RTCM, IMU, steer module, machine module) and surfaces the live receive traffic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> Every behaviour — port-name enumeration, the per-port open/close semantics,
    /// the culture-invariant baud parsing and the 500 ms live-readout refresh — lives in the bound
    /// <see cref="FormCommSetGPSViewModel"/> (set as the <see cref="StyledElement.DataContext"/>). The paired
    /// <c>FormCommSetGPS.axaml</c> drives every affordance through compiled command bindings
    /// (<c>x:DataType="vm:FormCommSetGPSViewModel"</c>). This dialog takes a
    /// <see cref="CommCoordinatorService"/> — the shared comms aggregate that owns the six serial ports —
    /// and builds the view-model from it, replacing the WinForms <c>FormLoop</c>/<c>mf</c> god-object
    /// back-reference (AAP §0.1.1). A typical caller is
    /// <c>await new FormCommSetGPS(mainVm.Comm).ShowDialog(this)</c>.
    /// </para>
    /// <para>
    /// <b>Intent events the window performs.</b> The bound <c>OkCommand</c> only <i>raises an event</i> — a
    /// view-model can neither close a window nor pop a toast — so this code-behind subscribes the view-model's
    /// intent events and performs the window-level actions the WinForms <c>FormCommSetGPS</c> did inline:
    /// <list type="bullet">
    ///   <item><description>
    ///   <see cref="FormCommSetGPSViewModel.RequestClose"/> → <c>Close()</c>, reproducing the WinForms
    ///   <c>btnSerialOK_Click</c> → <c>Close()</c>. No dialog result is surfaced: the port configuration is
    ///   already applied live as the operator changes it.
    ///   </description></item>
    ///   <item><description>
    ///   <see cref="FormCommSetGPSViewModel.RequestTimedMessage"/> (duration, title, message) → shows the
    ///   auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog, replacing the WinForms
    ///   <c>MessageBox.Show("Unable to connect to Port")</c> popups while the dialog stays open for another
    ///   attempt.
    ///   </description></item>
    /// </list>
    /// The handlers are named methods (not lambdas) so they can be unsubscribed when the dialog closes.
    /// </para>
    /// <para>
    /// <b>Key correctness point — ports outlive the dialog.</b> The view-model owns a 500 ms display-refresh
    /// timer (the WinForms <c>timer1</c>). <see cref="OnClosing"/> stops it via
    /// <see cref="FormCommSetGPSViewModel.Stop"/> so the timer never ticks against a closing dialog;
    /// <see cref="FormCommSetGPSViewModel.Stop"/> deliberately does <b>not</b> close any of the six serial
    /// ports — they are owned by the shared <c>SerialCommService</c> the <see cref="CommCoordinatorService"/>
    /// owns and must stay open after this dialog is dismissed (AAP key insight).
    /// </para>
    /// <para>
    /// <b>Conventions.</b> The shape here — a parameterless constructor for the Avalonia XAML loader plus a
    /// dependency-supplying overload that builds the view-model and binds it, with an explicit
    /// <see cref="AvaloniaXamlLoader"/> call — mirrors the sibling AgIO views (<c>FormISOBUS</c>,
    /// <c>FormSource</c>, <c>FormYes</c>, <c>FormRadioChannel</c>). Nullable reference types are disabled
    /// project-wide, so this file uses no nullable annotations, and it references no
    /// WinForms/WPF/System.Drawing types.
    /// </para>
    /// </remarks>
    public partial class FormCommSetGPS : Window
    {
        // [XPLAT] The view-model backing this dialog. Built in the CommCoordinatorService overload from the
        // shared comms aggregate and retained for the lifetime of the dialog (it is both this field and the
        // window's DataContext). Replaces the WinForms FormLoop "mf" back-reference. The parameterless loader
        // constructor leaves it at its default (null) — that path never touches it, and OnClosing null-guards.
        private readonly FormCommSetGPSViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error via <c>TreatWarningsAsErrors</c>).
        /// Application code constructs the dialog through <see cref="FormCommSetGPS(CommCoordinatorService)"/>.
        /// </summary>
        public FormCommSetGPS()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Creates the GPS-comm-settings dialog, builds its <see cref="FormCommSetGPSViewModel"/> from
        /// the supplied comms aggregate, binds it, and wires the view-model's close / timed-message intent
        /// events to the corresponding window actions — the cross-platform replacement for the WinForms
        /// <c>FormCommSetGPS(Form callingForm)</c> constructor, with the <c>FormLoop</c>/<c>mf</c> god-object
        /// parameter dropped in favour of the injected service.
        /// </summary>
        /// <param name="comm">
        /// The shared comms aggregate that owns the six serial ports and the UDP transport. The view-model
        /// commands these ports through it; the dialog never owns or closes them. Must not be <c>null</c>
        /// (<see cref="FormCommSetGPSViewModel"/> throws <see cref="System.ArgumentNullException"/> otherwise).
        /// </param>
        public FormCommSetGPS(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the view-model from the shared comms aggregate, then bind it. The VM owns all
            // logic (port enumeration, per-port open/close, baud parsing, the live readouts); this window
            // only hosts it and performs the window-level actions its intent events request.
            _vm = new FormCommSetGPSViewModel(comm);
            DataContext = _vm;

            // [XPLAT] OkCommand (bound in the .axaml) only raises RequestClose, and a failed port-open raises
            // RequestTimedMessage; the window performs the actual close + toast the WinForms FormCommSetGPS
            // did inline. Named handlers so they can be detached in OnClosing.
            _vm.RequestClose += OnRequestClose;
            _vm.RequestTimedMessage += OnRequestTimedMessage;
        }

        // [XPLAT] WinForms btnSerialOK_Click -> Close(). The view-model exposes no dialog result, so the
        // window simply closes (the port configuration was already applied live as the operator changed it).
        private void OnRequestClose()
        {
            Close();
        }

        // [XPLAT] WinForms "Unable to connect to Port" MessageBox -> the auto-closing AgIO timed-message toast
        // shown over this dialog (non-modal, so the dialog stays open for another attempt), mirroring the
        // original behaviour. FormTimedMessage exposes the (milliseconds, title, message) constructor.
        private void OnRequestTimedMessage(int timeInMsec, string title, string message)
        {
            FormTimedMessage toast = new FormTimedMessage(timeInMsec, title, message);
            toast.Show(this);
        }

        /// <summary>
        /// [XPLAT] Stops the view-model's 500 ms display-refresh timer (the WinForms <c>timer1</c>) and
        /// detaches its intent events as the dialog closes, so the timer never ticks against a closing window
        /// and the view-model never keeps the closed window alive. Stopping the timer deliberately does
        /// <b>not</b> close any of the six serial ports: they are owned by the shared <c>SerialCommService</c>
        /// and must persist beyond this dialog (AAP key insight).
        /// </summary>
        /// <param name="e">The window-closing event payload.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_vm != null)
            {
                _vm.Stop();
                _vm.RequestClose -= OnRequestClose;
                _vm.RequestTimedMessage -= OnRequestTimedMessage;
            }

            base.OnClosing(e);
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
