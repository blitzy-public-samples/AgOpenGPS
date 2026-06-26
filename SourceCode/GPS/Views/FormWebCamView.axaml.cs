// [XPLAT] migrated from net48/WinForms (Forms/FormWebCam.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] WebCam preview window — feature-gated parity shell (feature F-045).
    ///
    /// 1:1 structural-parity reimplementation of the WinForms <c>Forms/FormWebCam</c>
    /// (FormWebCam.cs + FormWebCam.Designer.cs). The original form enumerated DirectShow video-input
    /// devices through the Accord library (<c>FilterInfoCollection</c> / <c>VideoCaptureDevice</c>)
    /// and started/stopped a live preview hosted in a WinForms <c>VideoSourcePlayer</c> control.
    ///
    /// <para>
    /// The Accord <c>Accord.Video.DirectShow</c> / <c>Accord.Imaging</c> capture backend is REMOVED in
    /// this migration (AAP §0.5.1 / §0.6.3): it is Windows-only and abandoned, and webcam capture is
    /// the lowest-priority optional convenience whose documented default is already
    /// <c>isWebCamOn = false</c> (<c>docs/settings.md:L446</c>). No cross-platform capture backend is
    /// provided at this release, so — following the AAP's graceful no-op degradation rule (§0.7.2),
    /// under which the absence of an optional capability must never break the program — this view
    /// opens and closes cleanly while presenting the capture surface as a static placeholder,
    /// revealing the "not available" message, and disabling the capture controls. This is the intended
    /// cross-platform state by design, not deferred work: there is no removed dependency to wire later.
    /// </para>
    ///
    /// <para>
    /// If a future Windows-only capture path is ever reintroduced it MUST be guarded behind
    /// <c>IPlatformServices</c> / <c>RuntimeInformation.IsOSPlatform(OSPlatform.Windows)</c>; that
    /// guard is intentionally NOT added here.
    /// </para>
    ///
    /// Code-behind-driven view (no view-model, no XAML bindings), matching the sibling
    /// <c>FormDialogView</c> / <c>FormInputDialogView</c>. It depends only on Avalonia and the named
    /// controls declared in <c>FormWebCamView.axaml</c>.
    /// </summary>
    public partial class FormWebCamView : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (and used by
        /// callers showing the dialog via <c>ShowDialog</c> over an owner window — parity with the
        /// WinForms <c>FormStartPosition.CenterParent</c> modal). Loads the XAML, then applies the
        /// F-045 feature-gate so the window is never shown in a half-enabled state.
        /// </summary>
        public FormWebCamView()
        {
            InitializeComponent();

            GateFeature();
        }

        /// <summary>
        /// [XPLAT] Feature-gate for webcam capture (F-045). Because the Windows-only Accord DirectShow
        /// backend was removed and no cross-platform capture backend is provided at this release, the
        /// webcam is treated as unavailable on every platform: the unavailable message is revealed and
        /// all capture controls are disabled. The display surface (<c>videoSurface</c>) is left blank —
        /// its static gray placeholder panel — since there is no live frame to render.
        ///
        /// This preserves the established no-op graceful-degradation pattern: nothing is enumerated,
        /// nothing is started, and nothing throws, so the dialog opens and closes cleanly and core
        /// guidance is unaffected.
        /// </summary>
        private void GateFeature()
        {
            // [XPLAT] Clear "unavailable" message. No suitable localized key exists in
            // AgOpenGPS.Core.Translations.gStr (gsWebCam is only the window title "WebCam"), so per the
            // AAP a plain string is used. The wording matches the design-time text in
            // FormWebCamView.axaml so the design-time and runtime appearance stay identical.
            lblUnavailable.Text = "WebCam is not available on this platform.";
            lblUnavailable.IsVisible = true;

            // [XPLAT] Disable the capture controls — the device picker and the start/stop commands.
            // The two buttons already start disabled in the XAML; setting them here as well (plus the
            // combo box) keeps the feature-gate explicit and self-contained in the code-behind.
            deviceComboBox.IsEnabled = false;
            startButton.IsEnabled = false;
            stopButton.IsEnabled = false;

            // [XPLAT] videoSurface is intentionally left untouched: a blank gray placeholder panel.
        }
    }
}
