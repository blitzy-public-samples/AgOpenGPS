// [XPLAT] migrated from net48/WinForms (Forms/FormPan.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Camera pan pad — 1:1 behavioural-parity reimplementation of the WinForms
    /// <c>Forms/FormPan</c> (FormPan.cs + FormPan.Designer.cs).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FormPan is a small, borderless, non-modal eight-direction camera-pan pad shown over the main
    /// FormGPS window. Each of the eight directional buttons nudges the camera's <c>PanX</c>/<c>PanY</c>
    /// by <c>AdjustFactor (0.04) * camSetDistance</c> — the per-direction signs are reproduced verbatim
    /// from <c>FormPan.cs</c> so the panning direction matches the original product exactly — and the
    /// central cancel button resets both pans to zero and closes the window.
    /// </para>
    /// <para>
    /// Dependency injection (AAP §0.3.2): the WinForms form reached the camera through its owning
    /// <c>FormGPS</c> (<c>private readonly FormGPS mf; ... mf.camera</c>). That god-object back-reference
    /// is removed — the camera is injected directly through the constructor, and an <c>onClosed</c>
    /// callback lets the owner (MainView / its view-model) clear its <c>isPanFormVisible</c> flag, which
    /// is exact parity with the WinForms <c>FormClosing</c> handler that set
    /// <c>mf.isPanFormVisible = false</c>.
    /// </para>
    /// <para>
    /// Type note: the migration specification referred to the camera by its legacy WinForms class name
    /// <c>CCamera</c>; that class no longer exists. The camera was consolidated into the portable
    /// <see cref="Camera"/> in <c>AgOpenGPS.Core</c> (the declared dependency of this view), which
    /// exposes the same <see cref="Camera.PanX"/>, <see cref="Camera.PanY"/>, and
    /// <see cref="Camera.camSetDistance"/> members the original pan arithmetic uses.
    /// </para>
    /// <para>
    /// The XAML declares only <c>x:Name</c> (no <c>Click</c> attributes), so every button is subscribed
    /// in code by name. The four diagonals carry a "Pan" prefix in the Avalonia view
    /// (<c>btnPanUpLeft</c>/<c>btnPanUpRight</c>/<c>btnPanDownLeft</c>/<c>btnPanDownRight</c>) versus the
    /// WinForms designer names (<c>btnUpLeft</c>/<c>btnUpRight</c>/<c>btnDownLeft</c>/<c>btnDownRight</c>);
    /// the four edges and the cancel button keep their original names. The window is shown non-modally
    /// by the owner via <c>Show(owner)</c>, matching the WinForms non-modal pan overlay.
    /// </para>
    /// </remarks>
    public partial class FormPanView : Window
    {
        /// <summary>
        /// [XPLAT] Pan step factor applied to <see cref="Camera.camSetDistance"/> on each click.
        /// Verbatim from <c>FormPan.AdjustFactor</c>.
        /// </summary>
        private const double AdjustFactor = 0.04;

        /// <summary>
        /// [XPLAT] The injected camera (replaces the WinForms <c>mf.camera</c>). Holds the
        /// <see cref="Camera.PanX"/>/<see cref="Camera.PanY"/> offsets this pad adjusts and the
        /// read-only <see cref="Camera.camSetDistance"/> the pan math scales by.
        /// </summary>
        private readonly Camera _camera;

        /// <summary>
        /// [XPLAT] Optional owner callback invoked when this window closes, so the owner can clear its
        /// <c>isPanFormVisible</c> flag — parity with the WinForms <c>FormClosing</c> handler.
        /// </summary>
        private readonly Action _onClosed;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / design
        /// preview (keeps the <c>avares://</c> resource reachable and avoids AVLN3001, which the Release
        /// build treats as an error). Production code constructs the window via
        /// <see cref="FormPanView(Camera, Action)"/>.
        /// </summary>
        public FormPanView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Production constructor. Mirrors the WinForms <c>FormPan(Form callingForm)</c> ctor,
        /// but injects the camera directly instead of resolving it through <c>FormGPS</c>, and wires the
        /// nine buttons declared in the XAML (which carry no <c>Click</c> attributes).
        /// </summary>
        /// <param name="camera">
        /// The camera whose <see cref="Camera.PanX"/>/<see cref="Camera.PanY"/> this pad adjusts.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="onClosed">
        /// Optional callback invoked when the window closes; the owner uses it to clear its
        /// <c>isPanFormVisible</c> flag. May be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="camera"/> is <see langword="null"/>.
        /// </exception>
        public FormPanView(Camera camera, Action onClosed = null)
            : this()
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _onClosed = onClosed;

            // [XPLAT] The XAML declares only x:Name (no Click= attributes); subscribe every button here
            // by name. Each handler body reproduces the per-direction sign expression from FormPan.cs
            // verbatim so the panning direction matches the WinForms product exactly.
            btnPanUp.Click += btnPanUp_Click;
            btnPanDn.Click += btnPanDn_Click;
            btnPanRight.Click += btnPanRight_Click;
            btnPanLeft.Click += btnPanLeft_Click;
            btnPanUpLeft.Click += btnPanUpLeft_Click;
            btnPanUpRight.Click += btnPanUpRight_Click;
            btnPanDownLeft.Click += btnPanDownLeft_Click;
            btnPanDownRight.Click += btnPanDownRight_Click;
            btnPanCancel.Click += btnPanCancel_Click;
        }

        // [XPLAT] btnPanUp — parity with FormPan.btnPanUp_Click:
        //   mf.camera.PanY += AdjustFactor * mf.camera.camSetDistance;
        private void btnPanUp_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY += AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanDn — parity with FormPan.btnPanDn_Click:
        //   mf.camera.PanY -= AdjustFactor * mf.camera.camSetDistance;
        private void btnPanDn_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY -= AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanRight — parity with FormPan.btnPanRight_Click:
        //   mf.camera.PanX += AdjustFactor * mf.camera.camSetDistance;
        private void btnPanRight_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanX += AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanLeft — parity with FormPan.btnPanLeft_Click:
        //   mf.camera.PanX -= AdjustFactor * mf.camera.camSetDistance;
        private void btnPanLeft_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanX -= AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanUpLeft (WinForms btnUpLeft) — parity with FormPan.btnUpLeft_Click:
        //   mf.camera.PanY += AdjustFactor * mf.camera.camSetDistance;
        //   mf.camera.PanX -= AdjustFactor * mf.camera.camSetDistance;
        private void btnPanUpLeft_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY += AdjustFactor * _camera.camSetDistance;
            _camera.PanX -= AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanUpRight (WinForms btnUpRight) — parity with FormPan.btnUpRight_Click:
        //   mf.camera.PanY += AdjustFactor * mf.camera.camSetDistance;
        //   mf.camera.PanX += AdjustFactor * mf.camera.camSetDistance;
        private void btnPanUpRight_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY += AdjustFactor * _camera.camSetDistance;
            _camera.PanX += AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanDownLeft (WinForms btnDownLeft) — parity with FormPan.btnDownLeft_Click:
        //   mf.camera.PanY -= AdjustFactor * mf.camera.camSetDistance;
        //   mf.camera.PanX -= AdjustFactor * mf.camera.camSetDistance;
        private void btnPanDownLeft_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY -= AdjustFactor * _camera.camSetDistance;
            _camera.PanX -= AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanDownRight (WinForms btnDownRight) — parity with FormPan.btnDownRight_Click:
        //   mf.camera.PanY -= AdjustFactor * mf.camera.camSetDistance;
        //   mf.camera.PanX += AdjustFactor * mf.camera.camSetDistance;
        private void btnPanDownRight_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanY -= AdjustFactor * _camera.camSetDistance;
            _camera.PanX += AdjustFactor * _camera.camSetDistance;
        }

        // [XPLAT] btnPanCancel — parity with FormPan.btnPanCancel_Click: zero both pans, then close. The
        // WinForms handler also set mf.isPanFormVisible = false; that flag clear is delivered through the
        // injected onClosed callback fired from OnClosed once Close() completes (see OnClosed below),
        // exactly as the WinForms FormClosing handler cleared the same flag on every close path.
        private void btnPanCancel_Click(object sender, RoutedEventArgs e)
        {
            _camera.PanX = 0;
            _camera.PanY = 0;
            Close();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormPan_FormClosing</c> handler, which set
        /// <c>mf.isPanFormVisible = false</c>. Invokes the injected <c>onClosed</c> callback so the owner
        /// clears its visibility flag. This covers every close path — the cancel button and any external
        /// dismissal by the owner.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _onClosed?.Invoke();
        }
    }
}
