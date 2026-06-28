// [XPLAT] migrated from net48/WinForms FormNtrip.cs + FormNtrip.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AgIO.Controls;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormNtrip.axaml</c> — the AgIO "NTRIP Client Settings" dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> replacing the deleted WinForms
    /// <c>FormNtrip : Form</c> (<c>Forms/FormNtrip.cs</c> + <c>Forms/FormNtrip.Designer.cs</c>). It is
    /// bound to a <see cref="FormNtripViewModel"/> that owns every NTRIP setting read/write, the
    /// caster source-table fetch, the DNS resolution and the serial/UDP routing rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal (constructor injection).</b> The WinForms original took a direct
    /// <c>FormLoop</c> back-reference (<c>mf</c>) and reached through it for the live fix, the NTRIP
    /// packet size, the radio/serial-pass guards and the reconfigure hook. That coupling is gone: this
    /// view is constructed from the injected <see cref="CommCoordinatorService"/> comms facade only
    /// (<see cref="FormNtrip(CommCoordinatorService)"/>), which it hands to the view-model. The opener
    /// (<c>MainWindow</c>, guarded by its <c>CanOpenNtripSettings()</c>) uses
    /// <c>await new FormNtrip(comm).ShowDialog(owner)</c>.
    /// </para>
    /// <para>
    /// <b>Dialog orchestration is the code-behind's job.</b> A view-model must not open windows, pop
    /// message boxes or restart the process, so the view-model surfaces those concerns as intent events
    /// and this code-behind bridges each one to the concrete Avalonia dialog / lifecycle action — exactly
    /// mirroring the sibling AgIO views:
    /// <list type="bullet">
    ///   <item><description><see cref="FormNtripViewModel.RequestTimedMessage"/> (duration, title,
    ///   message) → the auto-closing <see cref="FormTimedMessage"/> toast shown non-modally with
    ///   <c>Show(this)</c> (the former <c>FormLoop.TimedMessageBox</c>).</description></item>
    ///   <item><description><see cref="FormNtripViewModel.RequestConfirm"/> (message) → the
    ///   acknowledge-only <see cref="FormYes"/> dialog opened with <c>showCancel:false</c> via
    ///   <c>ShowDialog&lt;bool&gt;</c> (the former single-OK <c>FormLoop.YesMessageBox</c> used for the
    ///   "Restarting" / "Can't Find" prompts).</description></item>
    ///   <item><description><see cref="FormNtripViewModel.RequestPickSource"/> (records, lat, lon, site)
    ///   → the <see cref="FormSource"/> mountpoint picker via <c>ShowDialog&lt;string&gt;</c>; the chosen
    ///   mountpoint is written back to <see cref="FormNtripViewModel.Mount"/> (the cross-platform
    ///   replacement for the WinForms <c>FormSource</c> mutating <c>FormNtrip.tboxMount.Text</c>
    ///   directly).</description></item>
    ///   <item><description><see cref="FormNtripViewModel.RequestRestart"/> → the cross-platform
    ///   <see cref="Program.Restart"/> (NOT the WinForms <c>Application.Restart</c>).</description></item>
    ///   <item><description><see cref="FormNtripViewModel.RequestClose"/> → <see cref="Window.Close()"/>
    ///   for the in-place save and Cancel paths.</description></item>
    /// </list>
    /// The view-model owns the window for the dialog's whole lifetime and nothing longer-lived references
    /// it (the comms facade is referenced <i>by</i> the view-model, never the reverse), so these
    /// subscriptions form a self-contained cycle that the GC reclaims and need no explicit teardown —
    /// the same rationale documented on the sibling <c>FormSource</c>.
    /// </para>
    /// <para>
    /// <b>On-screen touch keyboard (parity with the WinForms <c>tbox_Click</c> handlers).</b> The
    /// original attached a <c>Click</c> handler that opened the touch keyboard — gated on
    /// <c>FormLoop.isKeyboardOn</c> — to exactly the four text fields URL, Mount, User name and Password.
    /// <see cref="WireOnScreenKeyboard"/> reproduces that on the same four fields: the WinForms
    /// <c>TextBox.Click</c> becomes Avalonia <see cref="InputElement.PointerPressed"/>, the gate becomes
    /// <see cref="FormNtripViewModel.IsKeyboardOn"/>, and the keyboard is presented by the converted
    /// <see cref="AgIO.Controls.TextBoxExtensions.ShowKeyboard(TextBox, Window)"/> helper, which writes the
    /// typed text back to <see cref="TextBox.Text"/> — flowing through the markup's two-way binding into
    /// the bound view-model property. The URL field additionally re-triggers
    /// <see cref="FormNtripViewModel.FindIpCommand"/> after entry, reproducing the original
    /// <c>tboxEnterURL_Click</c> which always followed the keyboard with a <c>btnGetIP.PerformClick()</c>.
    /// The numeric fields' <c>ShowKeypad</c> wiring is intentionally not reproduced here — Avalonia's
    /// <see cref="NumericUpDown"/> supplies native numeric entry.
    /// </para>
    /// <para>
    /// <b>Live "Current GPS Fix" display (parity with the WinForms <c>timer1</c>).</b> The original ran a
    /// 500&#160;ms <c>System.Windows.Forms.Timer</c> (<c>Enabled = true</c>) whose <c>Tick</c> refreshed
    /// the current latitude/longitude read-outs. That becomes a <see cref="DispatcherTimer"/> here that
    /// calls <see cref="FormNtripViewModel.UpdateCurrentFix"/> on the same 500&#160;ms cadence; it is
    /// stopped and detached in <see cref="OnClosed"/> so it neither fires after close nor keeps the window
    /// alive.
    /// </para>
    /// <para>
    /// Follows the established AgIO view convention (<c>FormYes</c>, <c>FormSource</c>, <c>FormKeyboard</c>,
    /// <c>FormTimedMessage</c>, <c>FormRadioChannel</c>): a parameterless constructor for the XAML loader /
    /// previewer, an explicit <see cref="AvaloniaXamlLoader"/>-based <see cref="InitializeComponent"/>, and
    /// named controls resolved at runtime via <see cref="NameScopeExtensions.FindControl{T}"/>. No WinForms,
    /// WPF or <c>System.Drawing</c> types are referenced; nullable reference types are disabled
    /// project-wide, so no reference type is annotated with <c>?</c>.
    /// </para>
    /// </remarks>
    public partial class FormNtrip : Window
    {
        /// <summary>
        /// [XPLAT] The view-model built from the injected comms facade in
        /// <see cref="FormNtrip(CommCoordinatorService)"/>. Held so the keyboard / current-fix handlers can
        /// reach it and so it is both this field and the window's <see cref="StyledElement.DataContext"/>.
        /// Left <see langword="null"/> on the parameterless (designer / loader) path; every member that
        /// touches it guards against that, keeping the design-time previewer safe.
        /// </summary>
        private readonly FormNtripViewModel _vm;

        /// <summary>
        /// [XPLAT] 500&#160;ms display timer that refreshes the read-only "Current GPS Fix" latitude /
        /// longitude. Replaces the WinForms <c>timer1</c> (<c>Enabled = true</c>, <c>Interval = 500</c>); it
        /// is created and started by the parameterized constructor and stopped / detached in
        /// <see cref="OnClosed"/>. Left <see langword="null"/> on the parameterless path (the timer is never
        /// created there), so <see cref="OnClosed"/> guards it.
        /// </summary>
        private readonly DispatcherTimer _currentFixTimer;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration — with <c>TreatWarningsAsErrors</c> — promotes
        /// to an error). Application code always constructs the dialog through
        /// <see cref="FormNtrip(CommCoordinatorService)"/>; this mirrors the dual-constructor convention of
        /// the sibling AgIO views.
        /// </summary>
        public FormNtrip()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the NTRIP settings dialog from the injected comms facade: builds the backing
        /// <see cref="FormNtripViewModel"/>, assigns it as the <see cref="StyledElement.DataContext"/>,
        /// bridges the view-model's intent events to the matching Avalonia dialogs and the cross-platform
        /// restart, wires the on-screen keyboard, and starts the live current-fix display timer. The
        /// cross-platform replacement for the WinForms <c>FormNtrip(Form callingForm)</c> constructor, with
        /// the <c>FormLoop</c> god-object parameter dropped in favour of the
        /// <see cref="CommCoordinatorService"/> facade.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms facade. Supplies the behaviour-frozen <c>NtripService</c> (settings targets +
        /// reconfigure hook) and the <c>NmeaService</c> live fix consumed by the view-model. Must not be
        /// <see langword="null"/> — the view-model throws <see cref="ArgumentNullException"/> otherwise.
        /// </param>
        public FormNtrip(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the VM from the comms facade (the WinForms FormNtrip_Load seeding now lives in
            // the VM constructor) and bind it. The .axaml declares x:DataType=FormNtripViewModel, so the
            // compiled bindings resolve against exactly this DataContext.
            _vm = new FormNtripViewModel(comm);
            DataContext = _vm;

            // [XPLAT] WinForms TimedMessageBox(msec, title, message) -> the auto-closing toast, shown
            // non-modally so the dialog stays open underneath it.
            _vm.RequestTimedMessage += (milliseconds, title, message) =>
                new FormTimedMessage(milliseconds, title, message).Show(this);

            // [XPLAT] WinForms YesMessageBox(message) -> the acknowledge-only confirmation (showCancel:false)
            // used for the "Restart of AgIO is Required - Restarting" and "Can't Find: ..." prompts. The
            // boolean result is intentionally unused (single-OK semantics).
            _vm.RequestConfirm += async message =>
                await new FormYes(message, false).ShowDialog<bool>(this);

            // [XPLAT] WinForms btnGetSourceTable_Click -> FormSource mountpoint picker. The dialog now
            // RETURNS the chosen mountpoint (rather than mutating FormNtrip.tboxMount.Text), which is written
            // back to the bound Mount property. Parameters are explicitly typed so the lambda matches the
            // Action<List<string>, double, double, string> event signature exactly.
            _vm.RequestPickSource += async (List<string> sourceTable, double latitude, double longitude, string site) =>
            {
                string mount = await new FormSource(sourceTable, latitude, longitude, site).ShowDialog<string>(this);
                if (!string.IsNullOrEmpty(mount))
                {
                    _vm.Mount = mount;
                }
            };

            // [XPLAT] WinForms Program.Restart() preserved: the "Restart of AgIO is Required" flow delegates
            // to the migrated cross-platform Program restart helper (AgIO.Program.Restart), NOT the WinForms
            // Application.Restart.
            _vm.RequestRestart += () => Program.Restart();

            // [XPLAT] WinForms Close() for the in-place save (after ConfigureNTRIP) and the Cancel button.
            _vm.RequestClose += () => Close();

            // [XPLAT] WinForms tbox_Click on the four text fields -> PointerPressed (see WireOnScreenKeyboard).
            WireOnScreenKeyboard();

            // [XPLAT] WinForms timer1 (Enabled=true, Interval=500) -> DispatcherTimer driving the VM's
            // current-fix refresh on the same cadence; started here and stopped in OnClosed.
            _currentFixTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _currentFixTimer.Tick += OnCurrentFixTimerTick;
            _currentFixTimer.Start();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        /// <summary>
        /// [XPLAT] Attaches the on-screen touch keyboard to the four text fields that carried the WinForms
        /// <c>tbox_Click</c> handler — URL, Mount, User name and Password. The URL field uses a distinct
        /// handler (<see cref="OnUrlPointerPressed"/>) because the WinForms <c>tboxEnterURL_Click</c> always
        /// followed the keyboard with a <c>btnGetIP.PerformClick()</c>; the other three use
        /// <see cref="OnTextBoxPointerPressed"/>. Controls are resolved by their <c>x:Name</c> through
        /// <see cref="NameScopeExtensions.FindControl{T}"/> (this view loads its XAML with the explicit
        /// <see cref="AvaloniaXamlLoader"/> call, so the name-generator's field-assigning overload never
        /// runs); each lookup is null-guarded to keep the design-time previewer safe.
        /// </summary>
        private void WireOnScreenKeyboard()
        {
            HookKeyboard("tboxEnterURL", OnUrlPointerPressed);
            HookKeyboard("tboxMount", OnTextBoxPointerPressed);
            HookKeyboard("tboxUserName", OnTextBoxPointerPressed);
            HookKeyboard("tboxUserPassword", OnTextBoxPointerPressed);
        }

        /// <summary>
        /// [XPLAT] Resolves a text box by its <c>x:Name</c> and, when present, subscribes
        /// <paramref name="handler"/> to its <see cref="InputElement.PointerPressed"/> event (the Avalonia
        /// analogue of the WinForms <c>TextBox.Click</c>). The text boxes live for the lifetime of this
        /// window, so the subscription needs no explicit teardown.
        /// </summary>
        /// <param name="fieldName">The <c>x:Name</c> of the text box to wire.</param>
        /// <param name="handler">The pointer-pressed handler to attach.</param>
        private void HookKeyboard(string fieldName, EventHandler<PointerPressedEventArgs> handler)
        {
            TextBox textBox = this.FindControl<TextBox>(fieldName);
            if (textBox != null)
            {
                textBox.PointerPressed += handler;
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>tboxMount_Click</c> /
        /// <c>tboxUserName_Click</c> / <c>tboxUserPassword_Click</c> handlers. When the view-model's
        /// <see cref="FormNtripViewModel.IsKeyboardOn"/> flag is set (the former
        /// <c>FormLoop.isKeyboardOn</c> gate), it presents the on-screen keyboard for the tapped text box
        /// through the converted <see cref="AgIO.Controls.TextBoxExtensions.ShowKeyboard(TextBox, Window)"/>
        /// helper, which writes the typed text back to <see cref="TextBox.Text"/> and thus, via the markup's
        /// two-way binding, into the bound view-model property. <c>async void</c> is the standard signature
        /// for an awaited event handler.
        /// </summary>
        /// <param name="sender">The text box that was pressed.</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnTextBoxPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_vm != null && _vm.IsKeyboardOn && sender is TextBox textBox)
            {
                await textBox.ShowKeyboard(this);
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>tboxEnterURL_Click</c> handler. It opens
        /// the on-screen keyboard when <see cref="FormNtripViewModel.IsKeyboardOn"/> is set, then — always,
        /// exactly as the original called <c>btnGetIP.PerformClick()</c> outside its keyboard gate —
        /// re-triggers <see cref="FormNtripViewModel.FindIpCommand"/> to resolve the freshly entered URL to
        /// a caster IP. Because the keyboard helper writes the typed text back through the two-way binding
        /// before this awaits, the command reads the updated <see cref="FormNtripViewModel.CasterUrl"/>.
        /// </summary>
        /// <param name="sender">The URL text box that was pressed.</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnUrlPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (_vm != null && _vm.IsKeyboardOn)
                {
                    await textBox.ShowKeyboard(this);
                }

                if (_vm != null && _vm.FindIpCommand != null && _vm.FindIpCommand.CanExecute(null))
                {
                    _vm.FindIpCommand.Execute(null);
                }
            }
        }

        /// <summary>
        /// [XPLAT] Display-timer tick: refreshes the read-only current latitude / longitude from the live
        /// fix by invoking <see cref="FormNtripViewModel.UpdateCurrentFix"/> — the Avalonia equivalent of
        /// the WinForms <c>timer1_Tick</c>. Null-guards the view-model for the parameterless (designer)
        /// path, although that path never creates or starts the timer.
        /// </summary>
        /// <param name="sender">The timer raising the tick (unused).</param>
        /// <param name="e">The tick event data (unused).</param>
        private void OnCurrentFixTimerTick(object sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.UpdateCurrentFix();
            }
        }

        /// <summary>
        /// [XPLAT] Stops and detaches the current-fix display timer when the dialog closes so it neither
        /// fires after close nor keeps this window alive. The view-model intent-event subscriptions and the
        /// child text boxes' <see cref="InputElement.PointerPressed"/> subscriptions are intentionally left
        /// in place — they share this window's lifetime and are collected with it.
        /// </summary>
        /// <param name="e">The close-event payload passed to the base implementation.</param>
        protected override void OnClosed(EventArgs e)
        {
            if (_currentFixTimer != null)
            {
                _currentFixTimer.Stop();
                _currentFixTimer.Tick -= OnCurrentFixTimerTick;
            }

            base.OnClosed(e);
        }
    }
}
