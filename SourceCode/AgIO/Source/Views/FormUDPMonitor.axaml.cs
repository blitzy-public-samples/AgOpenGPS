// [XPLAT] migrated from net48/WinForms FormUDPMonitor.cs + FormUDPMonitor.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormUDPMonitor.axaml</c>, the AgIO <b>UDP Monitor</b> dialog —
    /// a live, read-only mirror of the UDP loopback traffic flowing through AgIO. It is a 1:1 parity
    /// reimplementation of the deleted WinForms <c>FormUDPMonitor : Form</c>
    /// (<c>Forms/FormUDPMonitor.cs</c> + <c>FormUDPMonitor.designer.cs</c>), rebuilt as an Avalonia
    /// <see cref="Window"/> bound to <see cref="FormUDPMonitorViewModel"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The WinForms dialog held a direct <c>FormLoop mf</c> back-reference and
    /// poked <c>mf.isUDPMonitorOn</c>, <c>mf.logUDPSentence</c>, <c>mf.isGPSLogOn</c> and
    /// <c>mf.isNTRIPLogOn</c> directly. That coupling is gone: this window injects a
    /// <see cref="CommCoordinatorService"/> (the migrated comms aggregate that owns those flags and the
    /// capture buffer) into the view-model, then binds to it. Every button and the "Guide -&gt;" link is
    /// driven by a bound command on <see cref="FormUDPMonitorViewModel"/>, so this code-behind carries no
    /// per-control event handlers — only the three window-level concerns the view-model cannot own.
    /// </para>
    /// <para>
    /// <b>Host-delegated concerns (the bridge).</b> Three view-model events are translated into window
    /// operations here, each reproducing a WinForms behaviour exactly:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="FormUDPMonitorViewModel.RequestTimedMessage"/> → opens a transient
    ///   <see cref="FormTimedMessage"/> owned by this window — the parity replacement for the WinForms
    ///   <c>mf.TimedMessageBox(2000, "File Saved", "To zAgIO_UDP_Log.Txt")</c> shown after a save.</item>
    ///   <item><see cref="FormUDPMonitorViewModel.RequestOpenPgn"/> → opens the Avalonia
    ///   <see cref="FormPGN"/> reference window, mirroring the WinForms <c>lblPGNGuide_Click</c> handler
    ///   that did <c>new FormPGN().Show(this)</c>.</item>
    ///   <item><see cref="FormUDPMonitorViewModel.RequestClose"/> → closes this window, replacing the
    ///   WinForms <c>btnSerialCancel_Click</c> <c>Close()</c>. The view-model's <c>CloseCommand</c> first
    ///   tears the monitor down via <see cref="FormUDPMonitorViewModel.Cleanup"/>, then raises this event;
    ///   the window cannot close itself from the view-model, so that one concern lives here. This mirrors
    ///   the sibling <c>FormEventViewer</c> code-behind.</item>
    /// </list>
    /// <para>
    /// <b>Teardown on every close path.</b> The WinForms <c>FormUDPMonitor_FormClosing</c> handler set
    /// <c>mf.isUDPMonitorOn = false</c> so the UDP service stopped buffering display sentences regardless of
    /// how the dialog was dismissed (Cancel button or the window chrome). <see cref="OnClosing"/> reproduces
    /// that by calling the view-model's idempotent <see cref="FormUDPMonitorViewModel.Cleanup"/> on the
    /// close, so the capture flag is always cleared — whether the operator used the Close button (which has
    /// already run <c>Cleanup</c> through <c>CloseCommand</c>; the second call is a safe no-op) or the
    /// title-bar close.
    /// </para>
    /// <para>
    /// The dialog is shown non-modally with this window as owner — <c>new FormUDPMonitor(comm).Show(owner)</c>
    /// — matching the original WinForms monitor, which was a non-modal, non-top-most child window. Nullable
    /// reference types are disabled project-wide, so no <c>?</c> annotations appear on reference types; the
    /// <see cref="OnClosing"/> teardown uses an explicit <c>null</c> check to stay safe on the
    /// parameterless (XAML-loader / previewer) construction path.
    /// </para>
    /// </remarks>
    public partial class FormUDPMonitor : Window
    {
        // [XPLAT] Replaces the WinForms `FormLoop mf` back-reference. The view-model is constructed from the
        // injected CommCoordinatorService and held for the dialog's lifetime so OnClosing can tear it down.
        // It is only assigned on the comm-injection construction path; the parameterless XAML-loader /
        // previewer path leaves it null (handled by the null check in OnClosing).
        private readonly FormUDPMonitorViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia runtime XAML loader and the
        /// design-time previewer (the URI-based loader instantiates the type through it, and its presence
        /// keeps the compiled-XAML resource reachable — otherwise the build emits warning <c>AVLN3001</c>,
        /// which the Release configuration treats as an error). It only loads the XAML; real application use
        /// always goes through <see cref="FormUDPMonitor(CommCoordinatorService)"/>, which additionally
        /// builds and binds the view-model and wires the host bridges. Matches the convention of the sibling
        /// AgIO views (<c>FormPGN</c>, <c>FormEventViewer</c>, <c>FormTimedMessage</c>, <c>FormYes</c>).
        /// </summary>
        public FormUDPMonitor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes a new <see cref="FormUDPMonitor"/> for the supplied comms aggregate: it loads the
        /// XAML, builds the backing <see cref="FormUDPMonitorViewModel"/> (which switches monitor capture on
        /// and starts the 333&#160;ms drain timer, reproducing the WinForms <c>FormUDp_Load</c>), binds it as
        /// the <see cref="StyledElement.DataContext"/>, and wires the three host-delegated bridges
        /// (timed-message, PGN window, and close).
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate whose UDP transport carries the monitor flags and the captured-sentence
        /// buffer this dialog mirrors. Passed straight to the view-model, which rejects a
        /// <see langword="null"/> value with an <see cref="System.ArgumentNullException"/>.
        /// </param>
        public FormUDPMonitor(CommCoordinatorService comm)
        {
            InitializeComponent();

            // [XPLAT] Build the view-model from the injected comms aggregate (replacing the WinForms
            // `FormLoop mf` back-reference) and bind it. Construction switches isUDPMonitorOn on and starts
            // the drain timer — the parity counterpart of the WinForms FormUDp_Load handler.
            _vm = new FormUDPMonitorViewModel(comm);
            DataContext = _vm;

            // [XPLAT] mf.TimedMessageBox(msec, title, message) parity: show a transient, self-dismissing
            // FormTimedMessage owned by this window (e.g. the "File Saved" toast after btnFileSave).
            _vm.RequestTimedMessage += (ms, title, msg) => new FormTimedMessage(ms, title, msg).Show(this);

            // [XPLAT] lblPGNGuide_Click parity: new FormPGN().Show(this) — open the PGN reference window
            // non-modally with this dialog as owner.
            _vm.RequestOpenPgn += () => new FormPGN().Show(this);

            // [XPLAT] btnSerialCancel_Click parity: CloseCommand runs Cleanup() then raises RequestClose; the
            // window performs the actual dismissal. The view-model is owned by this window and never handed
            // to an external owner, so this subscription cannot outlive the dialog (no explicit unsubscribe
            // is required). Mirrors the sibling FormEventViewer code-behind.
            _vm.RequestClose += () => Close();
        }

        /// <summary>
        /// Loads the compiled XAML for this window. Defined explicitly (rather than relying on a generated
        /// method) to match the convention used by the sibling Avalonia views in this folder.
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// [XPLAT] Window close handler — the parity counterpart of the WinForms
        /// <c>FormUDPMonitor_FormClosing</c> handler, which set <c>mf.isUDPMonitorOn = false</c>. Calls the
        /// view-model's idempotent <see cref="FormUDPMonitorViewModel.Cleanup"/> so the monitor capture flag
        /// is cleared (and the drain timer stopped) on <i>every</i> close path — the Close button (which has
        /// already run <c>Cleanup</c> via <c>CloseCommand</c>; the repeat call is a no-op) and the window
        /// chrome alike — then defers to the base implementation. The <see langword="null"/> check guards the
        /// parameterless XAML-loader / previewer construction path, where the view-model was never created
        /// (explicit check rather than the null-conditional operator, as nullable reference types are
        /// disabled project-wide).
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
