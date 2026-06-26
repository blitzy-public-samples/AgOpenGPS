// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormTram.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the tram-line dialog — a 1:1 visual-parity reimplementation of the
    /// WinForms <c>FormTram</c> (FormTram.cs + FormTram.Designer.cs). The operator sets the number of
    /// passes between trams, the tram-line opacity (alpha), and the generate mode
    /// (All / Lines / Outer), then exits (saving) or cancels.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The self-contained operator surface is reproduced faithfully:
    /// <list type="bullet">
    ///   <item>Passes — the keypad-style <c>nudPasses</c> button shows the value (seeded from
    ///         <c>Settings.setTram_passes</c>); <see cref="OnUpPassesClick"/> /
    ///         <see cref="OnDownPassesClick"/> step it within 1..999 and persist it.</item>
    ///   <item>Alpha — <c>tbarTramAlpha</c> (10..100) is seeded from <c>Settings.setTram_alpha</c>;
    ///         <see cref="OnAlphaChanged"/> updates the <c>lblAplha</c> percentage readout, and the
    ///         value is persisted on close.</item>
    ///   <item>Mode — <see cref="OnModeClick"/> cycles All(0) → Lines(1) → Outer(2) → All and swaps
    ///         the <c>imgMode</c> glyph for an exact visual match to the original.</item>
    ///   <item><see cref="OnExitClick"/> / <see cref="OnCancelClick"/> close with the save / discard
    ///         outcome.</item>
    /// </list>
    /// The tram-line geometry rebuild (<c>MoveBuildTramLine</c> → <c>mf.curve/ABLine.BuildTram</c>),
    /// the A/B swap maths (<c>mf.trk.gArr[...]</c>), and the read-only width readouts
    /// (<c>lblSeedWidth</c> / <c>lblTramWidth</c> / <c>lblTrack</c>) depend on the FormGPS god-object
    /// and are host-owned at this checkpoint. The dialog forwards intent through
    /// <see cref="PassesChanged"/>, <see cref="AlphaChanged"/>, <see cref="ModeChanged"/> and
    /// <see cref="SwapAbRequested"/>; the keypad edit on <c>nudPasses</c> is likewise host-owned. No
    /// fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormTramView : Window
    {
        // [XPLAT] Local operator state mirroring the original NumericUpDown / TrackBar / mode index.
        private int _passes = 1;
        private int _mode;

        /// <summary>The current passes-between-trams value (1..999).</summary>
        public int Passes => _passes;

        /// <summary>The current generate mode: 0 = All, 1 = Lines, 2 = Outer.</summary>
        public int GenerateMode => _mode;

        /// <summary>[XPLAT] Raised when the passes value changes (host: <c>mf.tram.passes</c> + rebuild).</summary>
        public event EventHandler<int> PassesChanged;

        /// <summary>[XPLAT] Raised with the alpha as a 0..1 fraction (host: <c>mf.tram.alpha</c>).</summary>
        public event EventHandler<double> AlphaChanged;

        /// <summary>[XPLAT] Raised with the new mode index (host: <c>mf.tram.generateMode</c> + rebuild).</summary>
        public event EventHandler<int> ModeChanged;

        /// <summary>[XPLAT] Raised to swap the active track's A/B points (host: <c>mf.trk</c> maths).</summary>
        public event EventHandler SwapAbRequested;

        public FormTramView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            btnExit.Click += OnExitClick;
            btnCancel.Click += OnCancelClick;
            btnUpTrams.Click += OnUpPassesClick;
            btnDnTrams.Click += OnDownPassesClick;
            btnMode.Click += OnModeClick;
            btnSwapAB.Click += OnSwapAbClick;
            tbarTramAlpha.ValueChanged += OnAlphaChanged;
        }

        // [XPLAT] FormTram_Load: seed alpha (10..100), the passes value, and the mode glyph.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (Properties.Settings.Default.setTram_passes < 1)
            {
                Properties.Settings.Default.setTram_passes = 1;
                Properties.Settings.Default.Save();
            }

            _passes = Properties.Settings.Default.setTram_passes;
            nudPasses.Content = _passes.ToString(CultureInfo.InvariantCulture);

            double alphaPercent = Properties.Settings.Default.setTram_alpha * 100.0;
            if (alphaPercent < tbarTramAlpha.Minimum) alphaPercent = tbarTramAlpha.Minimum;
            if (alphaPercent > tbarTramAlpha.Maximum) alphaPercent = tbarTramAlpha.Maximum;
            tbarTramAlpha.Value = alphaPercent;
            lblAplha.Text = ((int)tbarTramAlpha.Value).ToString(CultureInfo.InvariantCulture) + "%";

            _mode = 0;
            SetModeGlyph(_mode);
        }

        // [XPLAT] FormTram_FormClosing: persist the alpha exactly as the original did on close.
        protected override void OnClosed(EventArgs e)
        {
            Properties.Settings.Default.setTram_alpha = tbarTramAlpha.Value * 0.01;
            Properties.Settings.Default.Save();
            base.OnClosed(e);
        }

        // [XPLAT] btnExit_Click: isSaving = true; close positive (host saves the tram + tracks).
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] btnCancel_Click: close negative (host discards the working tram).
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnUpTrams_Click: nudPasses.UpButton() — step up within 1..999 and persist.
        private void OnUpPassesClick(object sender, RoutedEventArgs e)
        {
            if (_passes < 999) _passes++;
            ApplyPasses();
        }

        // [XPLAT] btnDnTrams_Click: nudPasses.DownButton() — step down within 1..999 and persist.
        private void OnDownPassesClick(object sender, RoutedEventArgs e)
        {
            if (_passes > 1) _passes--;
            ApplyPasses();
        }

        // [XPLAT] nudPasses_ValueChanged: persist setTram_passes and notify the host to rebuild.
        private void ApplyPasses()
        {
            nudPasses.Content = _passes.ToString(CultureInfo.InvariantCulture);
            Properties.Settings.Default.setTram_passes = _passes;
            Properties.Settings.Default.Save();
            PassesChanged?.Invoke(this, _passes);
        }

        // [XPLAT] btnMode_Click: cycle All(0) → Lines(1) → Outer(2) → All and swap the glyph.
        private void OnModeClick(object sender, RoutedEventArgs e)
        {
            _mode++;
            if (_mode > 2) _mode = 0;
            SetModeGlyph(_mode);
            ModeChanged?.Invoke(this, _mode);
        }

        // [XPLAT] btnSwapAB_Click: forward the swap so the host applies the mf.trk point maths.
        private void OnSwapAbClick(object sender, RoutedEventArgs e)
        {
            SwapAbRequested?.Invoke(this, EventArgs.Empty);
        }

        // [XPLAT] tbarTramAlpha_Scroll: update the percentage readout and notify the host.
        private void OnAlphaChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblAplha.Text = ((int)tbarTramAlpha.Value).ToString(CultureInfo.InvariantCulture) + "%";
            AlphaChanged?.Invoke(this, tbarTramAlpha.Value * 0.01);
        }

        // [XPLAT] Swap the mode glyph between the three tram resources (parity with btnMode switch).
        private void SetModeGlyph(int mode)
        {
            string asset = mode == 1 ? "TramLines.png" : mode == 2 ? "TramOuter.png" : "TramAll.png";
            try
            {
                imgMode.Source = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + asset)));
            }
            catch
            {
                // [XPLAT] Asset resolution is best-effort; a missing glyph never breaks the dialog.
            }
        }
    }
}
