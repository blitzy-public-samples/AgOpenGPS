// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormBuildTracks.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the Build-Tracks manager — a 1:1 visual-parity reimplementation of the
    /// WinForms <c>FormBuildTracks</c> (FormBuildTracks.cs + FormBuildTracks.Designer.cs). This is the
    /// largest guidance dialog: a track list (reorder / rename / duplicate / delete / show-hide) plus
    /// a multi-panel wizard for creating AB-line, A+, curve, KML, lat/lon and pivot tracks.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name. The .axaml declares no event handlers, so this code-behind is the thin Avalonia
    /// adapter required for XAML compilation, plus the self-contained main-dismissal behaviour.
    /// <para>
    /// <see cref="OnCancelMainClick"/> wires the main Cancel button to <see cref="Window.Close()"/>,
    /// reproducing the dismissal of the original <c>btnCancelMain_Click</c>. Everything else in this
    /// dialog is interleaved with the FormGPS god-object: the track list reflects and mutates
    /// <c>mf.trk.gArr</c>; the wizard's panel transitions are driven in lock-step with
    /// <c>mf.curve</c> / <c>mf.ABLine</c> recording state (e.g. the original <c>btnzABCurve_Click</c>
    /// clears <c>mf.curve.desList</c> as it reveals the curve panel); and the pre-close restore
    /// rebuilds <c>mf.trk.gArr</c> from the original backup and resets
    /// <c>isABValid</c>/<c>isCurveValid</c>. Because the panel navigation and the model state are a
    /// single unit, that behaviour is host-owned at this checkpoint (the host can hook
    /// <see cref="Window.Closing"/> for the restore). No fabricated calls to not-yet-projected
    /// services are made here — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormBuildTracksView : Window
    {
        public FormBuildTracksView()
        {
            InitializeComponent();

            // [XPLAT] Main Cancel — wire the self-contained dismissal (the original also restored the
            // track-array backup and stopped auto-steer first; that model restore is host-owned).
            btnCancelMain.Click += OnCancelMainClick;
        }

        // [XPLAT] btnCancelMain_Click: dismiss the manager.
        private void OnCancelMainClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
