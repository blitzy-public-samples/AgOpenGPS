// [XPLAT] migrated from net48/WinForms FormSerialMonitor.cs + FormSerialMonitor.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormSerialMonitor.axaml</c> — the AgIO "Serial Monitor" dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormSerialMonitor : Form</c> (<c>SourceCode/AgIO/Source/Forms/FormSerialMonitor.cs</c> +
    /// <c>FormSerialMonitor.designer.cs</c>). The dialog is a standalone serial-port sniffer: pick a
    /// port and baud, Open/Close, watch the incoming bytes in a large read-only display, and optionally
    /// Log (mirror the AgIO NMEA buffer), Save and Clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> Every behaviour — serial open/close (with the frozen
    /// <c>(portName, baudRate, Parity.None, 8, StopBits.One)</c> framing and the documented mega2560
    /// boot delay), the receive append, the log-mirror toggle, port enumeration, and the save-to-file
    /// action — lives in the bound <see cref="FormSerialMonitorViewModel"/>. The paired
    /// <c>FormSerialMonitor.axaml</c> drives every affordance through compiled command bindings
    /// (<c>x:DataType="vm:FormSerialMonitorViewModel"</c>), so this code-behind only constructs and
    /// binds the view-model, bridges its two intent events to the window-level actions a command cannot
    /// perform, and guarantees the serial port is released when the window closes.
    /// </para>
    /// <para>
    /// <b>God-object removal (the heart of the migration for this dialog).</b> The WinForms original
    /// took a <c>Form callingForm</c> parameter, cast it to <c>FormLoop mf</c>, and reached back through
    /// it for the log-mirror state (<c>mf.isLogMonitorOn</c> / <c>mf.logMonitorSentence</c>) and the
    /// <c>mf.TimedMessageBox(...)</c> popup. That back-reference is gone: this window is constructed from
    /// an injected <see cref="CommCoordinatorService"/> (the migrated AgIO comms aggregate), which it
    /// forwards to the view-model — the view-model reads the cross-platform port-name enumeration and the
    /// log-mirror state through that service instead of off a window.
    /// </para>
    /// <para>
    /// <b>Intent-event bridges (a command can neither close a window nor pop a toast — those are
    /// window-level actions).</b> The code-behind subscribes the two view-model intent events:
    /// <list type="bullet">
    ///   <item><see cref="FormSerialMonitorViewModel.RequestTimedMessage"/> (duration, title, message) →
    ///   shows the auto-closing AgIO <see cref="FormTimedMessage"/> toast over this dialog, replacing the
    ///   WinForms <c>MessageBox.Show("Unable to connect to Port")</c> / <c>mf.TimedMessageBox(...)</c>
    ///   popups while the monitor stays open.</item>
    ///   <item><see cref="FormSerialMonitorViewModel.RequestClose"/> → <c>Close()</c>, reproducing the
    ///   WinForms <c>btnSerialCancel_Click</c> → <c>Close()</c> (the OK button binds to the view-model's
    ///   <c>AcceptCommand</c>, which turns off log mirroring and then raises this event).</item>
    /// </list>
    /// Both are wired as lambdas with no explicit unsubscribe: this window <i>owns</i> the view-model it
    /// creates, so the two share one lifetime and are collected together — there is no external publisher
    /// that could keep the closed window alive (the same rationale used by the sibling <c>FormSource</c>).
    /// </para>
    /// <para>
    /// <b>Guaranteed port release on close (the critical correctness point).</b> The WinForms
    /// <c>FormSerialMonitor_FormClosing</c> handler only reset <c>mf.isLogMonitorOn = false</c>;
    /// disposal of the dialog's own <see cref="System.IO.Ports.SerialPort"/> rode on the form's
    /// <c>Dispose</c>. Here <see cref="OnClosing"/> calls the idempotent
    /// <see cref="FormSerialMonitorViewModel.Cleanup"/>, which stops the log timer, detaches the
    /// <c>DataReceived</c> handler, closes the port if open, disposes it, and clears the log-mirror flag.
    /// An un-closed serial port is far more disruptive on Linux/macOS (the device node stays locked) than
    /// on Windows, so this teardown runs on every close path — the OK button, the window frame's close
    /// button, and programmatic <c>Close()</c> alike.
    /// </para>
    /// <para>
    /// <b>Conventions.</b> Nullable reference types are disabled project-wide, so this file carries no
    /// nullable annotations (the <c>_vm</c> null check in <see cref="OnClosing"/> is an ordinary runtime
    /// guard, not a <c>?</c> annotation). It uses no WinForms/WPF/<c>System.Drawing</c>/
    /// <c>System.Windows.Threading</c> types. The parameterless constructor for the Avalonia XAML loader /
    /// design-time previewer plus a dependency-supplying overload, with an explicit
    /// <see cref="AvaloniaXamlLoader"/> call, mirrors the sibling AgIO views (<c>FormSource</c>,
    /// <c>FormCommSetGPS</c>, <c>FormUDPMonitor</c>, <c>FormTimedMessage</c>). The monitor is shown
    /// non-modally — <c>new FormSerialMonitor(viewModel.Comm).Show(owner)</c> — matching the original
    /// dialog's modality.
    /// </para>
    /// </remarks>
    public partial class FormSerialMonitor : Window
    {
        /// <summary>
        /// The view-model backing this dialog. Built in
        /// <see cref="FormSerialMonitor(CommCoordinatorService)"/> from the injected comms aggregate and
        /// retained for the lifetime of the window (it is both this field and the window's
        /// <see cref="Avalonia.StyledElement.DataContext"/>). The parameterless loader/previewer
        /// constructor leaves it at its default <c>null</c> — that path never opens a port and the
        /// <see cref="OnClosing"/> guard tolerates the <c>null</c>.
        /// </summary>
        private readonly FormSerialMonitorViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer (the URI/loader path instantiates the type with a public parameterless constructor
        /// and, in the Release configuration, an unreachable compiled-XAML resource is treated as an
        /// error). It only loads the XAML; application code always constructs the dialog through
        /// <see cref="FormSerialMonitor(CommCoordinatorService)"/>. Matches the convention of the sibling
        /// AgIO views (<c>FormSource</c>, <c>FormCommSetGPS</c>, <c>FormUDPMonitor</c>,
        /// <c>FormTimedMessage</c>).
        /// </summary>
        public FormSerialMonitor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the Serial Monitor, builds its <see cref="FormSerialMonitorViewModel"/> from the
        /// supplied comms aggregate, binds it, and wires the view-model's two intent events to the
        /// window-level actions a bound command cannot perform — the cross-platform replacement for the
        /// WinForms <c>FormSerialMonitor(Form callingForm)</c> constructor, with the <c>Form callingForm</c>
        /// god-object parameter dropped in favour of the injected service.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate. Forwarded to the view-model, which uses it for cross-platform
        /// serial port-name enumeration (<see cref="CommCoordinatorService.Serial"/>) and for the
        /// log-mirror state (<see cref="CommCoordinatorService.Nmea"/>). The view-model rejects a
        /// <c>null</c> value with an <see cref="System.ArgumentNullException"/>.
        /// </param>
        public FormSerialMonitor(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the view-model from the injected comms aggregate, then bind it. The VM owns
            // all serial / log / file logic; this window only hosts it and routes its intent events.
            _vm = new FormSerialMonitorViewModel(comm);
            DataContext = _vm;

            // [XPLAT] WinForms MessageBox.Show / mf.TimedMessageBox(...) -> the auto-closing AgIO
            // timed-message toast shown over this dialog (non-modal, so the monitor stays open). The
            // view-model raises (milliseconds, title, message); FormTimedMessage exposes that exact ctor.
            _vm.RequestTimedMessage += (milliseconds, title, message) =>
                new FormTimedMessage(milliseconds, title, message).Show(this);

            // [XPLAT] WinForms btnSerialCancel_Click -> Close(). The OK button binds to the VM's
            // AcceptCommand, which turns off log mirroring and then raises RequestClose; the window
            // performs the actual close. No unsubscribe is needed — this window owns the VM, so they
            // share one lifetime and are collected together.
            _vm.RequestClose += () => Close();
        }

        /// <summary>
        /// Loads the compiled XAML for this window. The explicit <see cref="AvaloniaXamlLoader"/> call
        /// matches the convention used by the sibling AgIO views (<c>FormSource</c>,
        /// <c>FormCommSetGPS</c>, <c>FormTimedMessage</c>).
        /// </summary>
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// [XPLAT] Releases the monitor's serial port when the window closes — the resource-cleanup the
        /// WinForms <c>FormSerialMonitor_FormClosing</c> handler (plus the form's <c>Dispose</c>) used to
        /// perform. Calls the idempotent <see cref="FormSerialMonitorViewModel.Cleanup"/> (stop the log
        /// timer, detach <c>DataReceived</c>, close and dispose the port, clear the AgIO log-mirror flag)
        /// before deferring to the base implementation, so the device node is never left locked — the
        /// critical correctness point on Linux/macOS. The <c>_vm</c> guard tolerates the parameterless
        /// loader/previewer path, on which no view-model was built.
        /// </summary>
        /// <param name="e">The window-closing event data, forwarded unchanged to the base implementation.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_vm != null)
            {
                _vm.Cleanup();
            }

            base.OnClosing(e);
        }
    }
}
