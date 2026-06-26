// [XPLAT] migrated from net48/WinForms (Forms/Field/FormSaveOrNot.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgOpenGPS.Properties;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the Exit / Cancel / Shutdown countdown dialog — a faithful
    /// behavioural port of the WinForms <c>FormSaveOrNot</c> (FormSaveOrNot.cs +
    /// FormSaveOrNot.Designer.cs). The dialog offers three mutually-related outcomes and shows a
    /// live one-second countdown that auto-confirms the active choice:
    /// <list type="bullet">
    ///   <item><c>btnOk</c> — Exit AgOpenGPS (return to the desktop).</item>
    ///   <item><c>btnReturn</c> — Cancel and return to AgOpenGPS.</item>
    ///   <item><c>btnShutDown</c> — power off the computer.</item>
    /// </list>
    /// The outcome is surfaced to the caller two equivalent ways so the dialog is convenient from
    /// either calling style: as the typed value of <see cref="Window.ShowDialog{TResult}"/>
    /// (<c>await dlg.ShowDialog&lt;SaveOrNotResult&gt;(owner)</c>) and via the <see cref="Result"/>
    /// property, mirroring the original <c>DialogResult</c> (OK / Ignore / Yes).
    /// </summary>
    /// <remarks>
    /// Imperative dialog: NO DataContext, NO x:DataType, NO MVVM bindings — every control is
    /// addressed by its x:Name, matching the accepted sibling code-behind dialogs. The three Click
    /// handlers keep the ORIGINAL WinForms method names (<c>btnOk_Click</c> / <c>btnReturn_Click</c>
    /// / <c>btnShutDown_Click</c>) and are wired from the .axaml. The WinForms
    /// <c>System.Windows.Forms.Timer</c> (timer1, 1000 ms) is reproduced with a
    /// <see cref="DispatcherTimer"/>. The <c>setWindow_isShutdownComputer</c> reads/writes go through
    /// the cross-platform <c>Settings.Default</c> exactly as the original. The original ctor also
    /// triggered an AgShare snapshot (<c>mf.isJobStarted &amp;&amp; AgShareEnabled =&gt;
    /// mf.AgShareSnapshot()</c>); that depends on the FormGPS god-object which is not projected at
    /// this checkpoint, so the host performs the snapshot before showing this dialog — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormSaveOrNotView : Window
    {
        // [XPLAT] WinForms DialogResult.OK / Ignore / Yes mapped to a self-describing result enum.
        private int _countExit = 4;
        private int _countShutdown = 5;
        private DispatcherTimer _timer;

        /// <summary>The operator's (or countdown's) selected outcome. Defaults to Cancel.</summary>
        public SaveOrNotResult Result { get; private set; } = SaveOrNotResult.Cancel;

        public FormSaveOrNotView()
        {
            InitializeComponent();
        }

        // [XPLAT] FormSaveOrNot_Load: choose which counter pair is visible from the persisted setting,
        // seed the counter captions, then start the one-second countdown.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            bool isShutdown = Settings.Default.setWindow_isShutdownComputer;
            labelExitToWindows.IsVisible = !isShutdown;
            lblExitCtr.IsVisible = !isShutdown;
            labelShutdownIn.IsVisible = isShutdown;
            lblShutCtr.IsVisible = isShutdown;

            lblExitCtr.Text = _countExit.ToString(CultureInfo.InvariantCulture);
            lblShutCtr.Text = _countShutdown.ToString(CultureInfo.InvariantCulture);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += OnCountdownTick;
            _timer.Start();
        }

        // [XPLAT] timer1_Tick: decrement the active counter; when it drops below zero, auto-confirm.
        private void OnCountdownTick(object sender, EventArgs e)
        {
            if (Settings.Default.setWindow_isShutdownComputer)
            {
                _countShutdown--;
                lblShutCtr.Text = _countShutdown.ToString(CultureInfo.InvariantCulture);
                if (_countShutdown < 0)
                {
                    Result = SaveOrNotResult.Shutdown;
                    Close(SaveOrNotResult.Shutdown);
                }
            }
            else
            {
                _countExit--;
                lblExitCtr.Text = _countExit.ToString(CultureInfo.InvariantCulture);
                if (_countExit < 0)
                {
                    Result = SaveOrNotResult.Exit;
                    Close(SaveOrNotResult.Exit);
                }
            }
        }

        // [XPLAT] btnOk_Click — exit to the desktop (WinForms DialogResult.OK).
        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOrNotResult.Exit;
            Settings.Default.setWindow_isShutdownComputer = false;
            Close(SaveOrNotResult.Exit);
        }

        // [XPLAT] btnReturn_Click — abandon the exit and return to AgOpenGPS (WinForms DialogResult.Ignore).
        private void btnReturn_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOrNotResult.Cancel;
            Close(SaveOrNotResult.Cancel);
        }

        // [XPLAT] btnShutDown_Click — power off the computer (WinForms DialogResult.Yes).
        private void btnShutDown_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOrNotResult.Shutdown;
            Settings.Default.setWindow_isShutdownComputer = true;
            Close(SaveOrNotResult.Shutdown);
        }

        // [XPLAT] Deterministically release the DispatcherTimer so it cannot fire after close.
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
    /// <c>DialogResult.Yes</c> (Shutdown) triad with a self-describing enum.
    /// </summary>
    public enum SaveOrNotResult
    {
        /// <summary>Exit AgOpenGPS and return to the desktop.</summary>
        Exit,

        /// <summary>Cancel the exit and return to AgOpenGPS.</summary>
        Cancel,

        /// <summary>Power off the computer.</summary>
        Shutdown
    }
}
