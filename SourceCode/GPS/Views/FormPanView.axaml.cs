// [XPLAT] migrated from net48/WinForms (Forms/FormPan.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Camera pan pad.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormPan</c> (FormPan.cs +
    /// FormPan.Designer.cs). The WinForms form was a nine-button directional pad: the eight
    /// directional buttons (<c>btnPanUp</c>, <c>btnPanDn</c>, <c>btnPanLeft</c>, <c>btnPanRight</c>,
    /// and the four diagonals) nudged <c>mf.camera.PanX</c>/<c>PanY</c> by
    /// <c>0.04 * mf.camera.camSetDistance</c>, and <c>btnPanCancel</c> reset both pans to zero,
    /// cleared <c>mf.isPanFormVisible</c>, and closed the window.
    ///
    /// The eight directional actions operate exclusively on the <c>FormGPS</c> camera
    /// (<c>mf.camera</c>), which is not yet projected into the Avalonia shell at this checkpoint, so
    /// they are deliberately left for the camera/RenderCoordinator wiring rather than fabricated
    /// against a camera that does not yet exist. The self-contained part of the Cancel behaviour —
    /// closing the window — is wired here so the dialog is dismissible; the camera-zeroing and the
    /// <c>isPanFormVisible</c> flag clear belong to that same later camera wiring.
    ///
    /// The XAML declares only x:Name (no Click handlers), so the Cancel close is subscribed in code.
    /// </summary>
    public partial class FormPanView : Window
    {
        public FormPanView()
        {
            InitializeComponent();

            // [XPLAT] btnPanCancel -> close. Parity with FormPan.btnPanCancel_Click's Close(); the
            // camera pan reset (mf.camera.PanX/PanY = 0) and mf.isPanFormVisible = false are deferred
            // to the camera wiring as noted above.
            btnPanCancel.Click += OnCancelClick;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
