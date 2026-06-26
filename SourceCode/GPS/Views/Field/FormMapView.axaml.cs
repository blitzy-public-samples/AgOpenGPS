// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the background-imagery dialog — a feature-gated parity shell of the
    /// WinForms <c>Forms/Field/FormMap</c> (FormMap.cs + FormMap.Designer.cs, 422 lines; feature F-021).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original dialog pulled online satellite imagery in behind the field (and let the operator
    /// trace a boundary over it) entirely through the Windows-only / abandoned <c>GMap.NET.WindowsForms</c>
    /// imagery control plus the Windows-only <c>System.Drawing</c> imaging stack
    /// (<c>GMapControl</c>/<c>GMapPolygon</c>/<c>GMapOverlay</c>/<c>PointLatLng</c>/<c>Pen</c>/<c>Brushes</c>/
    /// <c>Graphics</c>). None of those are on the cross-platform path, so per AAP §0.5.1 / §0.6.3 they are
    /// REMOVED — this code-behind references no <c>GMap.NET.*</c>, no <c>System.Drawing(.Drawing2D/.Imaging)</c>,
    /// no Bing provider, no <c>System.Windows.Forms</c>, no <c>OpenTK</c> / GL, and no <c>FormGPS</c>
    /// god-object.
    /// </para>
    /// <para>
    /// Following the established no-op graceful-degradation pattern (the sibling
    /// <see cref="FormWebCamView"/> for F-045, and the existing brightness fallback), the imagery feature
    /// is gated OFF for first release: the dialog still OPENS and shows its full control shell, but the
    /// large map surface is the static <c>mapPlaceholder</c> panel carrying the
    /// "Background imagery unavailable on this platform" message (declared in <c>FormMapView.axaml</c>),
    /// every imagery-dependent control is disabled, and only <c>btnExit</c> stays enabled so the dialog
    /// can always be dismissed. It must NEVER throw and NEVER affect startup or core guidance; every
    /// map-dependent handler is a guarded no-op. This is the intended cross-platform state by design — not
    /// deferred work — because the removed dependency has no cross-platform successor to wire later.
    /// </para>
    /// <para>
    /// Imperative, code-behind-driven view (NO <c>DataContext</c> / <c>x:DataType</c> / MVVM bindings),
    /// matching the sibling <see cref="FormPanView"/> / <c>FormFieldDirView</c>. The companion
    /// <c>FormMapView.axaml</c> declares only <c>x:Name</c> (no <c>Click=</c> attributes), so every handler
    /// is subscribed here by name. The control names are a HARD CONTRACT with the markup and keep their
    /// original WinForms spellings so this port addresses them exactly as <c>FormMap.cs</c> did.
    /// </para>
    /// </remarks>
    public partial class FormMapView : Window
    {
        /// <summary>
        /// [XPLAT] Closing guard, verbatim in spirit from the WinForms <c>FormMap.isClosing</c> field. The
        /// original form set <c>ControlBox = false</c> (hiding the native close button) and cancelled any
        /// close that did not originate from <c>btnExit</c>; this reproduces that "only the explicit OK
        /// button closes the dialog" behaviour (see <see cref="OnClosing"/>). Written by
        /// <see cref="OnExitClick"/>, read by <see cref="OnClosing"/>.
        /// </summary>
        private bool isClosing;

        /// <summary>
        /// [XPLAT] Parameterless constructor — required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, which the Release build treats as an error) and used by callers showing the
        /// dialog over an owner via <c>ShowDialog</c> (parity with the WinForms
        /// <c>FormStartPosition.CenterParent</c> modal). After loading the XAML it localises the title and
        /// labels exactly as the WinForms constructor did, then wires the control events. It deliberately
        /// does NOT create any imagery control (the removed <c>GMapControl</c>/<c>GMapPolygon</c>/
        /// <c>GMapOverlay</c>/Bing provider lines) — that creation is gone, not relocated.
        /// </summary>
        public FormMapView()
        {
            InitializeComponent();

            // [XPLAT] Localise the title + section labels from the frozen translation resources, exactly
            // as FormMap's constructor did (this.Text/labelNewBoundary/labelBoundary/lblPoints/
            // labelBackground). gStr returns plain strings, so this cannot throw or touch any OS API.
            Title = gStr.gsMapForBackground;
            labelNewBoundary.Text = gStr.gsNewFromDefault + " " + gStr.gsBoundary;
            labelBoundary.Text = gStr.gsBoundary;
            lblPoints.Text = gStr.gsPoints + ":";
            labelBackground.Text = gStr.gsBackground;

            // [XPLAT] The XAML declares only x:Name (no Click= attributes); subscribe every control here by
            // name, mirroring the FormPanView convention. btnExit is the sole functional control. Every
            // imagery-dependent control is wired to the shared guarded no-op so its original handler intent
            // is documented and the wiring survives even if a control were ever re-enabled — but they are
            // disabled in OnLoaded, so these never fire in the gated state.
            btnExit.Click += OnExitClick;
            btnZoomIn.Click += OnFeatureGatedNoOp;
            btnZoomOut.Click += OnFeatureGatedNoOp;
            btnAddFence.Click += OnFeatureGatedNoOp;
            btnDeletePoint.Click += OnFeatureGatedNoOp;
            btnDeleteAll.Click += OnFeatureGatedNoOp;
            btnGo.Click += OnFeatureGatedNoOp;
            cboxDrawMap.Click += OnFeatureGatedNoOp;
            cboxEnableLineDraw.Click += OnFeatureGatedNoOp;
        }

        /// <summary>
        /// [XPLAT] Replaces the WinForms <c>FormMap_Load</c> + <c>timer1_Tick</c> lifecycle. The original
        /// load handler restored the GMap window size/zoom/position and toggled the Bing-map state; all of
        /// that depended on the removed imagery control, so here it collapses to making the gated state
        /// explicit: disable every imagery-dependent control and clear the live readouts (there is no
        /// per-second timer and no field/boundary access in the gated dialog). <c>btnExit</c> stays enabled
        /// and the static <c>mapPlaceholder</c> message (declared in the XAML) communicates the gated
        /// state. The whole body is defensive: it can never throw, so the dialog always opens cleanly.
        /// </summary>
        /// <param name="e">The routed-event data for the <c>Loaded</c> event.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            try
            {
                // [XPLAT] feature-gated: background imagery unavailable cross-platform. Disable every
                // imagery-dependent control so the gated state is visually unambiguous (the markup already
                // sets IsEnabled="False"; this makes the gate explicit and self-contained in code).
                btnZoomIn.IsEnabled = false;
                btnZoomOut.IsEnabled = false;
                btnAddFence.IsEnabled = false;
                btnDeletePoint.IsEnabled = false;
                btnDeleteAll.IsEnabled = false;
                btnGo.IsEnabled = false;
                cboxDrawMap.IsEnabled = false;
                cboxEnableLineDraw.IsEnabled = false;

                // [XPLAT] Clear the live readouts. In the WinForms original these were driven once per
                // second by timer1_Tick from the traced-point count and the boundary list; with no imagery
                // and no field access in the gated dialog there is nothing to count, so they show empty —
                // the same display the original produced for zero points / no work in progress.
                lblPoints.Text = string.Empty;
                lblBnds.Text = string.Empty;

                // [XPLAT] btnExit remains enabled (set in the XAML) so the dialog can always be closed.
            }
            catch (Exception ex)
            {
                // [XPLAT] Graceful degradation: log and swallow. Gating the controls must never break the
                // dialog open path or affect core guidance.
                Log.EventWriter("FormMapView gated load: " + ex.Message);
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>FormMap.btnExit_Click</c>: arm the closing guard and close. This is the
        /// only path that sets <see cref="isClosing"/>, so it is the only way the dialog closes (see
        /// <see cref="OnClosing"/>).
        /// </summary>
        /// <param name="sender">The event source (unused).</param>
        /// <param name="e">The routed-event data (unused).</param>
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            isClosing = true;
            Close();
        }

        /// <summary>
        /// [XPLAT] Shared guarded no-op for every imagery-dependent control
        /// (<c>btnZoomIn</c>/<c>btnZoomOut</c>/<c>btnAddFence</c>/<c>btnDeletePoint</c>/<c>btnDeleteAll</c>/
        /// <c>btnGo</c>/<c>cboxDrawMap</c>/<c>cboxEnableLineDraw</c>). The corresponding WinForms handlers
        /// mutated the removed <c>gMapControl</c> / GMap polygon / overlay and reached the <c>FormGPS</c>
        /// god-object for boundary operations; none of that exists on the cross-platform path, so the
        /// feature-gated behaviour is to do nothing. The controls are disabled in <see cref="OnLoaded"/>,
        /// so this never executes in the gated state — it exists to keep the wiring coherent and never
        /// throws.
        /// </summary>
        /// <param name="sender">The event source (unused).</param>
        /// <param name="e">The routed-event data (unused).</param>
        private void OnFeatureGatedNoOp(object sender, RoutedEventArgs e)
        {
            // [XPLAT] feature-gated: background imagery unavailable cross-platform — intentionally no-op.
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormMap_FormClosing</c> guard: only <c>btnExit</c> (which
        /// sets <see cref="isClosing"/>) may close the dialog; any other close request — including the
        /// native window close button, which the WinForms form suppressed via <c>ControlBox = false</c> —
        /// is cancelled. The original handler also persisted the GMap window size/zoom to settings; that
        /// persistence is DROPPED because it depended on removed GMap state and on the Windows-only
        /// <c>System.Drawing.Size</c> backing of <c>setWindow_BingMapSize</c> (both off the cross-platform
        /// path). This override touches no OS API and never throws.
        /// </summary>
        /// <param name="e">The window-closing event data.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (!isClosing)
            {
                e.Cancel = true;
            }
        }
    }
}
