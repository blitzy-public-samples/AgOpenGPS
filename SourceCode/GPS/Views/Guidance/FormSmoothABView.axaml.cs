// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormSmoothAB.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] The outcome of the Smooth-AB-Curve dialog, mapping the three WinForms close paths to a
    /// strongly-typed result consumed via <c>ShowDialog&lt;FormSmoothABView.SmoothAbResult&gt;</c>.
    /// </summary>
    public enum SmoothAbResult
    {
        /// <summary>Dialog dismissed (Cancel) — the host clears the working smooth list.</summary>
        Cancel = 0,

        /// <summary>"For now" (bntOK) — the host keeps the smoothing in memory for this session.</summary>
        ForNow = 1,

        /// <summary>"To file" (btnSave) — the host persists the smoothed track to disk.</summary>
        ToFile = 2
    }

    /// <summary>
    /// [XPLAT] Code-behind for the Smooth-AB-Curve dialog — a faithful port of the WinForms
    /// <c>FormSmoothAB</c> (FormSmoothAB.cs + FormSmoothAB.Designer.cs). The operator increases /
    /// decreases the smoothing window with the North / South repeat-buttons and watches the live
    /// preview, then commits "for now", saves "to file", or cancels.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The dialog owns the <c>smoothCount</c> counter and the <c>lblSmooth</c> readout exactly
    /// as the original (start 20, clamped 2..100). Mirroring the WinForms control swap, the original
    /// <c>btnNorth_MouseDown</c> / <c>btnSouth_MouseDown</c> become <see cref="RepeatButton"/>
    /// <c>Click</c> handlers (auto-repeat while held).
    /// <para>
    /// The smoothing maths and persistence live on the FormGPS god-object in the original
    /// (<c>mf.curve.SmoothAB</c>, <c>SaveSmoothList</c>, <c>smooList.Clear</c>,
    /// <c>mf.FileSaveTracks</c>, <c>mf.oglMain.Refresh</c>). Those are host-owned at this checkpoint —
    /// the dialog raises <see cref="SmoothLevelChanged"/> on every counter change so the host can
    /// recompute the live preview, and returns a <see cref="SmoothAbResult"/> so the host can apply
    /// the chosen persistence. No fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormSmoothABView : Window
    {
        // [XPLAT] Local smoothing counter — identical seed/clamp to the original FormSmoothAB.
        private int _smoothCount = 20;

        /// <summary>
        /// The smoothing window the host should apply: <c>smoothCount * 2</c>, exactly matching the
        /// original <c>mf.curve.SmoothAB(smoothCount * 2)</c> argument.
        /// </summary>
        public int SmoothLevel => _smoothCount * 2;

        /// <summary>
        /// [XPLAT] Raised whenever the operator changes the smoothing window (North / South). The host
        /// recomputes the live curve preview from <see cref="SmoothLevel"/>, replacing the original
        /// inline <c>mf.curve.SmoothAB(...)</c> + <c>mf.oglMain.Refresh()</c> calls.
        /// </summary>
        public event EventHandler SmoothLevelChanged;

        public FormSmoothABView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            btnNorth.Click += OnNorthClick;
            btnSouth.Click += OnSouthClick;
            bntOK.Click += OnForNowClick;
            btnSave.Click += OnSaveToFileClick;
            btnCancel.Click += OnCancelClick;
        }

        // [XPLAT] FormSmoothAB_Load: reset the counter and show the "**" placeholder.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _smoothCount = 20;
            lblSmooth.Text = "**";
        }

        // [XPLAT] btnNorth_MouseDown: increase smoothing (clamped at 100), refresh preview.
        private void OnNorthClick(object sender, RoutedEventArgs e)
        {
            if (_smoothCount++ > 100)
            {
                _smoothCount = 100;
            }

            lblSmooth.Text = _smoothCount.ToString(CultureInfo.InvariantCulture);
            SmoothLevelChanged?.Invoke(this, EventArgs.Empty);
        }

        // [XPLAT] btnSouth_MouseDown: decrease smoothing (clamped at 2), refresh preview.
        private void OnSouthClick(object sender, RoutedEventArgs e)
        {
            _smoothCount--;
            if (_smoothCount < 2)
            {
                _smoothCount = 2;
            }

            lblSmooth.Text = _smoothCount.ToString(CultureInfo.InvariantCulture);
            SmoothLevelChanged?.Invoke(this, EventArgs.Empty);
        }

        // [XPLAT] bntOK_Click ("For now"): keep the smoothing in memory; host saves the smooth list.
        private void OnForNowClick(object sender, RoutedEventArgs e)
        {
            Close(SmoothAbResult.ForNow);
        }

        // [XPLAT] btnSave_Click ("To file"): host saves the smooth list AND persists tracks to disk.
        private void OnSaveToFileClick(object sender, RoutedEventArgs e)
        {
            Close(SmoothAbResult.ToFile);
        }

        // [XPLAT] btnCancel_Click: discard — host clears the working smooth list.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(SmoothAbResult.Cancel);
        }
    }
}
