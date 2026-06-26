// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the Exit / Cancel / Shutdown countdown dialog — a faithful 1:1
    /// behavioural port of the WinForms <c>FormSaveOrNot</c> (Forms/Field/FormSaveOrNot.cs +
    /// FormSaveOrNot.Designer.cs). The dialog presents three mutually-related outcomes and shows a
    /// live one-second countdown that auto-confirms the active choice:
    /// <list type="bullet">
    ///   <item><c>btnOk</c> — exit AgOpenGPS / return to the desktop (WinForms <c>DialogResult.OK</c>).</item>
    ///   <item><c>btnReturn</c> — cancel the exit and return to AgOpenGPS (WinForms <c>DialogResult.Ignore</c>).</item>
    ///   <item><c>btnShutDown</c> — power off the computer (WinForms <c>DialogResult.Yes</c>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an IMPERATIVE dialog — there is intentionally no <c>DataContext</c>, no
    /// <c>x:DataType</c> and no MVVM data binding. Every label and visibility state is assigned in
    /// code-behind through the <c>x:Name</c>'d controls declared in <c>FormSaveOrNotView.axaml</c>,
    /// exactly mirroring the WinForms constructor + <c>FormSaveOrNot_Load</c>. The three click
    /// handlers keep the ORIGINAL WinForms method names (<c>btnOk_Click</c> / <c>btnReturn_Click</c>
    /// / <c>btnShutDown_Click</c>) and are wired from the <c>.axaml</c> markup.
    /// </para>
    /// <para>
    /// The outcome is surfaced to the caller via <see cref="Window.Close(object)"/> with a
    /// <see cref="SaveOrNotResult"/> argument, observable through
    /// <c>await dlg.ShowDialog&lt;SaveOrNotResult&gt;(owner)</c> — the cross-platform replacement for
    /// the WinForms <c>DialogResult</c> (OK ⇒ <see cref="SaveOrNotResult.ExitToWindows"/>,
    /// Ignore ⇒ <see cref="SaveOrNotResult.Cancel"/>, Yes ⇒ <see cref="SaveOrNotResult.Shutdown"/>).
    /// </para>
    /// <para>
    /// The non-visual WinForms <c>System.Windows.Forms.Timer</c> (<c>timer1</c>, 1000 ms) is
    /// reproduced with an <see cref="DispatcherTimer"/>. The <c>FormGPS</c> ("mf") god-object that
    /// the original constructor consumed is replaced by explicit constructor injection: the host
    /// supplies the current job state and the AgShare snapshot callback, so this view holds no
    /// reference to <c>FormGPS</c>.
    /// </para>
    /// </remarks>
    public partial class FormSaveOrNotView : Window
    {
        // [XPLAT] Countdown seeds copied verbatim from the WinForms fields
        // (FormSaveOrNot.cs: int countExit = 4; int countShutdown = 5;).
        private int _countExit = 4;
        private int _countShutdown = 5;

        // [XPLAT] Replaces System.Windows.Forms.Timer timer1 (Designer Interval = 1000 ms). Held so
        // it can be deterministically stopped in OnClosed, preventing ticks after the window closes.
        private DispatcherTimer _timer;

        // [XPLAT] Guards the one-time countdown setup in OnLoaded (defensive: a top-level dialog
        // raises Loaded once, but this keeps the handler idempotent and avoids a second timer).
        private bool _countdownStarted;

        /// <summary>
        /// Parameterless constructor required by Avalonia's compiled-XAML runtime loader so the
        /// <c>avares://AgOpenGPS/Views/Field/FormSaveOrNotView.axaml</c> resource stays reachable
        /// (otherwise the build emits warning <c>AVLN3001</c>, which the Release configuration treats
        /// as an error). Production code constructs the dialog via
        /// <see cref="FormSaveOrNotView(bool, Action)"/>.
        /// </summary>
        public FormSaveOrNotView()
        {
            // [XPLAT] InitializeComponent is emitted by the Avalonia XAML source generator from
            // FormSaveOrNotView.axaml; it also creates the typed x:Name'd control fields.
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the dialog, mirroring the WinForms <c>FormSaveOrNot(FormGPS gps)</c>
        /// constructor: it applies the localized captions from <see cref="gStr"/> and, when a job is
        /// active and AgShare uploads are enabled, triggers the AgShare snapshot side-effect that
        /// creates the temporary data file for the upload.
        /// </summary>
        /// <param name="isJobStarted">
        /// Whether a field job is currently started — the cross-platform replacement for the original
        /// <c>mf.isJobStarted</c>.
        /// </param>
        /// <param name="agShareSnapshot">
        /// Callback that performs the AgShare snapshot — the cross-platform replacement for the
        /// original <c>mf.AgShareSnapshot()</c>. Invoked only when <paramref name="isJobStarted"/> is
        /// <see langword="true"/> and <c>Settings.Default.AgShareEnabled</c> is <see langword="true"/>.
        /// </param>
        public FormSaveOrNotView(bool isJobStarted, Action agShareSnapshot)
            : this()
        {
            // [XPLAT] Translations: identical assignments to FormSaveOrNot.cs. The Designer
            // placeholder text in the .axaml is overridden here at runtime.
            labelExit.Text = gStr.gsExit;
            labelShutdown.Text = gStr.gsShutdown;
            labelCancel.Text = gStr.gsCancel;
            labelExitToWindows.Text = gStr.gsExitToWindows + ":";
            labelShutdownIn.Text = gStr.gsShutdownIn + ":";

            // [XPLAT] Trigger a snapshot to create a temp data file for the AgShare upload — ported
            // from `if (mf.isJobStarted && Settings.Default.AgShareEnabled) mf.AgShareSnapshot();`.
            // The FormGPS dependency is replaced by the injected callback (null-safe).
            if (isJobStarted && Settings.Default.AgShareEnabled)
            {
                agShareSnapshot?.Invoke();
            }
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormSaveOrNot_Load</c>: selects which counter pair is visible from the
        /// persisted <c>setWindow_isShutdownComputer</c> setting, seeds the counter captions and
        /// starts the one-second countdown.
        /// </summary>
        /// <param name="e">The routed-event payload.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            // Idempotent: only seed/visibility/start once even if Loaded were to be raised again.
            if (_countdownStarted)
            {
                return;
            }
            _countdownStarted = true;

            bool isShutdown = Settings.Default.setWindow_isShutdownComputer;

            // [XPLAT] The Exit pair and the Shutdown pair are mutually exclusive — exactly one is
            // shown, keyed on the persisted setting (Visible -> IsVisible).
            labelExitToWindows.IsVisible = !isShutdown;
            lblExitCtr.IsVisible = !isShutdown;
            labelShutdownIn.IsVisible = isShutdown;
            lblShutCtr.IsVisible = isShutdown;

            // [XPLAT] InvariantCulture keeps the rendered digits stable across OS locales, per the
            // migration's culture-safety guidance.
            lblExitCtr.Text = _countExit.ToString(CultureInfo.InvariantCulture);
            lblShutCtr.Text = _countShutdown.ToString(CultureInfo.InvariantCulture);

            // [XPLAT] DispatcherTimer replaces the WinForms Timer; 1 s interval matches timer1.Interval.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnCountdownTick;
            _timer.Start();
        }

        /// <summary>
        /// [XPLAT] Ports <c>timer1_Tick</c>: decrements the active counter once per second and, when
        /// it drops below zero, auto-confirms the active choice by closing the dialog.
        /// </summary>
        /// <param name="sender">The timer raising the tick; unused.</param>
        /// <param name="e">The event payload; unused.</param>
        private void OnCountdownTick(object sender, EventArgs e)
        {
            if (Settings.Default.setWindow_isShutdownComputer)
            {
                _countShutdown--;
                lblShutCtr.Text = _countShutdown.ToString(CultureInfo.InvariantCulture);
                if (_countShutdown < 0)
                {
                    Close(SaveOrNotResult.Shutdown);
                }
            }
            else
            {
                _countExit--;
                lblExitCtr.Text = _countExit.ToString(CultureInfo.InvariantCulture);
                if (_countExit < 0)
                {
                    Close(SaveOrNotResult.ExitToWindows);
                }
            }
        }

        /// <summary>
        /// [XPLAT] Ports <c>btnOk_Click</c> — exit AgOpenGPS / return to the desktop. Clears the
        /// shutdown flag and closes the dialog with <see cref="SaveOrNotResult.ExitToWindows"/>
        /// (WinForms <c>DialogResult.OK</c>). Wired via <c>Click="btnOk_Click"</c> in the markup.
        /// </summary>
        /// <param name="sender">The "Exit" button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.setWindow_isShutdownComputer = false;
            Close(SaveOrNotResult.ExitToWindows);
        }

        /// <summary>
        /// [XPLAT] Ports <c>btnReturn_Click</c> — abandon the exit and return to AgOpenGPS. Closes the
        /// dialog with <see cref="SaveOrNotResult.Cancel"/> (WinForms <c>DialogResult.Ignore</c>).
        /// Wired via <c>Click="btnReturn_Click"</c> in the markup.
        /// </summary>
        /// <param name="sender">The "Cancel" button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void btnReturn_Click(object sender, RoutedEventArgs e)
        {
            Close(SaveOrNotResult.Cancel);
        }

        /// <summary>
        /// [XPLAT] Ports <c>btnShutDown_Click</c> — power off the computer. Sets the shutdown flag and
        /// closes the dialog with <see cref="SaveOrNotResult.Shutdown"/> (WinForms
        /// <c>DialogResult.Yes</c>). Wired via <c>Click="btnShutDown_Click"</c> in the markup.
        /// </summary>
        /// <param name="sender">The "Shutdown" button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void btnShutDown_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.setWindow_isShutdownComputer = true;
            Close(SaveOrNotResult.Shutdown);
        }

        /// <summary>
        /// [XPLAT] Deterministically releases the <see cref="DispatcherTimer"/> so it cannot fire
        /// after the window has closed (the WinForms <c>Timer</c> was owned by the form's component
        /// container and disposed with the form).
        /// </summary>
        /// <param name="e">The event payload.</param>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnCountdownTick;
                _timer = null;
            }
        }
    }

    /// <summary>
    /// [XPLAT] The three mutually-exclusive outcomes of <see cref="FormSaveOrNotView"/>, replacing the
    /// WinForms <c>DialogResult.OK</c> (Exit) / <c>DialogResult.Ignore</c> (Cancel) /
    /// <c>DialogResult.Yes</c> (Shutdown) triad with a self-describing enum surfaced through
    /// <c>ShowDialog&lt;SaveOrNotResult&gt;</c>.
    /// </summary>
    public enum SaveOrNotResult
    {
        /// <summary>Exit AgOpenGPS and return to the desktop (WinForms <c>DialogResult.OK</c>).</summary>
        ExitToWindows,

        /// <summary>Cancel the exit and return to AgOpenGPS (WinForms <c>DialogResult.Ignore</c>).</summary>
        Cancel,

        /// <summary>Power off the computer (WinForms <c>DialogResult.Yes</c>).</summary>
        Shutdown
    }
}
