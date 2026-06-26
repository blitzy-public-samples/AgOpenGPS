// [XPLAT] migrated from net48/WinForms (Forms/FormWebCam.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Webcam preview window — feature-gated parity shell.
    ///
    /// 1:1 structural parity reimplementation of the WinForms <c>Forms/FormWebCam</c> (FormWebCam.cs
    /// + .Designer.cs). The WinForms form enumerated DirectShow video devices via Accord
    /// (<c>FilterInfoCollection</c> / <c>VideoCaptureDevice</c>) and started/stopped a live preview.
    ///
    /// Per the AAP, webcam capture (feature F-045) is feature-gated off Windows: Accord's DirectShow
    /// backend is Windows-only and abandoned, and the feature's default is <c>isWebCamOn = false</c>.
    /// Following the AAP's graceful-degradation rule (§0.6.3) — the absence of an optional capability
    /// must never break the program — this view presents the capture surface as a static placeholder,
    /// reveals the "not available on this platform" message, and disables the capture controls instead
    /// of binding a removed backend. This is the intended cross-platform state, not deferred work: the
    /// capture backend is gone by design, so there is nothing to wire later.
    /// </summary>
    public partial class FormWebCamView : Window
    {
        public FormWebCamView()
        {
            InitializeComponent();

            // [XPLAT] Graceful feature-gate (F-045): no cross-platform capture backend, so surface the
            // unavailable state and disable the controls rather than fabricating a removed dependency.
            lblUnavailable.IsVisible = true;
            startButton.IsEnabled = false;
            stopButton.IsEnabled = false;
            deviceComboBox.IsEnabled = false;
        }
    }
}
