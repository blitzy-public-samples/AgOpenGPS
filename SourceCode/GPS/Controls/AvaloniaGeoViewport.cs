// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// AvaloniaGeoViewport — the cross-platform Avalonia OpenGL rendering-host adapter (AAP req#4,
// §0.4.1 "Rendering host — reimplement", §0.6.2 DOMINANT feasibility risk). It is the direct
// Avalonia parallel of the retired WinForms host adapter SourceCode/GPS/WinForms/GeoViewport.cs:
// both derive from AgOpenGPS.Core.Drawing.GeoViewportBase and override exactly the three
// host-specific seams the Core drawing code isolates — MakeCurrent(), ViewportSize and
// EndPaint()/SwapBuffers. Because the Core DrawLib already targets the GeoViewportBase abstraction,
// the migration needs ONLY this parallel adapter; the renderer (GLW / Core Drawing + DrawLib) is
// left completely untouched. This file is the entire host-swap surface.
//
// WHY COMPOSITION (not inheritance): C# is single-inheritance and GeoViewportBase is an abstract
// class, so this adapter cannot also extend Avalonia.OpenGL.Controls.OpenGlControlBase. Instead it
// HOLDS a private nested OpenGlControlBase subclass (GlHost) — exactly as the WinForms adapter held
// a GLControl — and forwards OpenGlControlBase's protected OnOpenGlInit/OnOpenGlRender/OnOpenGlDeinit
// callbacks back into this adapter. The companion control is exposed via the View / GlControl
// properties so the Avalonia Views can place it in the visual tree.
//
// LIFECYCLE DIFFERENCE FROM WINFORMS (critical): in WinForms the GL context is current the instant
// the GLControl exists, so the original adapter called _glControl.MakeCurrent() + Initialize() in
// its constructor. In Avalonia the context is NOT current at construction — it only becomes current
// inside the OnOpenGlInit/OnOpenGlRender/OnOpenGlDeinit callbacks. Therefore the static
// GeoViewportBase.Initialize() and the OpenTK<->Avalonia GL binding move OUT of the constructor and
// INTO OnOpenGlInit, and MakeCurrent() becomes a no-op (Avalonia owns context currency).
//
// THREE LEGACY SURFACES (AAP §0.6.2; from Forms/OpenGL.Designer.cs + FormGPS.Designer.cs):
//   * oglMain — the visible main field viewport -> the visible GlHost of an AvaloniaGeoViewport.
//   * oglZoom — the small zoom/overview surface whose green-channel read estimates worked-area /
//     overlap -> an off-screen FBO sized to the coordinator's OglZoomWidth x OglZoomHeight.
//   * oglBack — the off-screen 500x300 back buffer whose green-channel pixel scan drives section
//     control -> a dedicated off-screen FBO owned here. Avalonia presents only the visible
//     OpenGlControlBase, so an FBO is the natural mapping for the never-presented back buffer.
// The actual draw + GL.ReadPixels for all three live in the behavior-frozen RenderCoordinator; this
// host only binds the correct framebuffer, makes the context current (implicitly, via the callback),
// invokes the coordinator, and restores Avalonia's framebuffer afterward.
//
// DOMINANT FEASIBILITY RISK (AAP §0.6.2 / §0.6.5 — first-class deliverable): the kept GLW DrawLib
// uses legacy immediate-mode / fixed-function OpenGL, which does NOT exist under OpenGL ES / ANGLE.
// Avalonia frequently supplies a GLES/ANGLE context. This adapter therefore AUDITS the GL context
// at first init (Version/Renderer/Vendor/GLSL are logged via AgLibrary and exposed as properties)
// so the immediate-mode-vs-GLES determination is captured for MIGRATION_DOCS/PARITY_REPORT.md.
// Forcing a desktop-GL (compatibility) profile is configured in Program.BuildAvaloniaApp() (owned by
// the Program.cs agent) via the per-OS Avalonia platform options; this host assumes a desktop-GL
// context is current and never "fixes" the renderer here.
//
// DEPENDENCY DIRECTION: Controls -> Services. This adapter references the behavior-frozen
// RenderCoordinator (its sole depends_on_files entry) and the Core GeoViewportBase abstraction only;
// RenderCoordinator never references this concrete host (it takes the GeoViewportBase abstraction),
// keeping the edge one-directional. The composition root (App.axaml.cs) constructs the coordinator
// and injects it here, and wires PositionService's back-buffer-scan request to RequestBackBufferScan().

using System;
using AgLibrary.Logging;
using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Services;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS.Controls
{
    /// <summary>
    /// [XPLAT] Cross-platform Avalonia OpenGL rendering-host adapter. Derives from the Core
    /// <see cref="GeoViewportBase"/> abstraction (overriding the three host seams
    /// <see cref="MakeCurrent"/>, <see cref="ViewportSize"/> and <see cref="EndPaint"/>) and HOLDS a
    /// nested <see cref="OpenGlControlBase"/> (<c>GlHost</c>) that forwards the Avalonia GL lifecycle
    /// callbacks. It binds the kept OpenTK 3.3.3 GL entry points to Avalonia's GL context, owns the
    /// off-screen framebuffers that replace the legacy <c>oglBack</c>/<c>oglZoom</c> surfaces, and
    /// drives the behavior-frozen <see cref="RenderCoordinator"/> between <see cref="BeginPaint"/> and
    /// <see cref="EndPaint"/>. The kept <c>GLW</c> DrawLib and everything under
    /// <c>AgOpenGPS.Core/Drawing</c> + <c>AgOpenGPS.Core/DrawLib</c> are left untouched.
    /// </summary>
    public class AvaloniaGeoViewport : GeoViewportBase
    {
        // ---- The off-screen oglBack section/look-ahead scan buffer (AAP §0.6.2) ----
        // The coordinator's RenderBackBufferAndScan() self-sets GL.Viewport(0,0,500,300),
        // PixelStore(PackAlignment,1) and reads the green channel into grnPixels[150001]; the host only
        // supplies a 500x300 off-screen framebuffer. The dimensions are FROZEN to match the WinForms
        // oglBack exactly so the section-control pixel scan stays byte-identical.
        private const int BackBufferWidth = 500;
        private const int BackBufferHeight = 300;

        // ---- The companion Avalonia GL control (composition, not inheritance) ----
        private readonly GlHost _glHost;

        // ---- The behavior-frozen render-orchestration service (sole depends_on_files dependency) ----
        // Nullable: the composition root may inject it via the constructor or assign it later through the
        // RenderCoordinator property before the surface goes live. Every call below is null-guarded so the
        // viewport renders a cleared background harmlessly until the coordinator is wired.
        private RenderCoordinator _renderCoordinator;

        // ---- Avalonia's per-frame default framebuffer ----
        // Avalonia binds a NON-ZERO framebuffer as the render target and passes its id to OnOpenGlRender.
        // We cache it so the off-screen passes (oglBack/oglZoom) can restore it after binding their own FBO.
        private int _defaultFbo;

        // ---- Off-screen FBO handles (per-instance; created in OnOpenGlInit, freed in OnOpenGlDeinit) ----
        private int _backFbo;
        private int _backColorRenderbuffer;
        private int _backDepthRenderbuffer;

        // oglZoom overlap FBO — lazily (re)allocated to the coordinator's current OglZoomWidth/Height.
        private int _zoomFbo;
        private int _zoomColorRenderbuffer;
        private int _zoomDepthRenderbuffer;
        private int _zoomFboWidth;
        private int _zoomFboHeight;

        // ---- Cross-thread off-screen-pass requests (set from any thread; consumed in the render callback) ----
        // PositionService drives the section scan from its async receive->fuse->steer->section loop (a
        // threadpool thread). The GL context is only current inside OnOpenGlRender, so a request merely
        // raises a volatile flag and asks Avalonia for a prompt frame; the actual GL work runs in the next
        // OnOpenGlRender. This preserves the <=70 ms loop timing (AAP §0.1.1, §0.6.1) without adding latency.
        private volatile bool _backBufferScanPending;
        private volatile bool _zoomOverlapPending;
        private volatile bool _flagMarkPending;

        // ---- Applied surface size (drives resize detection so we rebuild the projection only on change) ----
        private int _appliedPixelWidth = -1;
        private int _appliedPixelHeight = -1;

        // ---- Last-known-good pixel size (guards ViewportSize before the surface is laid out) ----
        private int _lastPixelWidth = 1;
        private int _lastPixelHeight = 1;

        private bool _glInitialized;

        // ---- GL context audit results (DOMINANT feasibility risk — AAP §0.6.2 / §0.6.5) ----
        // Populated once at first OnOpenGlInit and surfaced so the composition root / PARITY_REPORT tooling
        // can record whether a desktop-GL (compatibility) context — required by the immediate-mode GLW
        // DrawLib — was obtained, or whether a GLES/ANGLE context was supplied instead.

        /// <summary>The GL <c>GL_VERSION</c> string captured at first init (empty until audited).</summary>
        public string GlVersion { get; private set; } = string.Empty;

        /// <summary>The GL <c>GL_RENDERER</c> string captured at first init (empty until audited).</summary>
        public string GlRenderer { get; private set; } = string.Empty;

        /// <summary>The GL <c>GL_VENDOR</c> string captured at first init (empty until audited).</summary>
        public string GlVendor { get; private set; } = string.Empty;

        /// <summary>The <c>GL_SHADING_LANGUAGE_VERSION</c> string captured at first init (empty until audited).</summary>
        public string GlShadingLanguageVersion { get; private set; } = string.Empty;

        /// <summary>True once the GL context strings have been queried in <c>OnOpenGlInit</c>.</summary>
        public bool IsContextAudited { get; private set; }

        /// <summary>
        /// True when the audited context looks like OpenGL ES / ANGLE (rather than desktop GL). Under such
        /// a context the immediate-mode / fixed-function GLW DrawLib will NOT run, which is the dominant open
        /// feasibility risk recorded in MIGRATION_DOCS/PARITY_REPORT.md.
        /// </summary>
        public bool IsLikelyOpenGlEs { get; private set; }

        /// <summary>
        /// [XPLAT] Creates the adapter without a render coordinator (assign one later via the
        /// <see cref="RenderCoordinator"/> property). Parallels the WinForms ctor but creates its OWN
        /// companion control and does NOT call <see cref="GeoViewportBase.Initialize"/> — the GL context is
        /// not current until the Avalonia callbacks fire (see <c>OnOpenGlInit</c>).
        /// </summary>
        /// <param name="boundingBox">The field bounding box passed straight to the base for zoom/pan setup.</param>
        public AvaloniaGeoViewport(GeoBoundingBox boundingBox)
            : this(boundingBox, null)
        {
        }

        /// <summary>
        /// [XPLAT] Creates the adapter and injects the behavior-frozen <see cref="RenderCoordinator"/> whose
        /// draw routines this host drives between <see cref="BeginPaint"/> and <see cref="EndPaint"/>.
        /// </summary>
        /// <param name="boundingBox">The field bounding box passed straight to the base for zoom/pan setup.</param>
        /// <param name="renderCoordinator">
        /// The render-orchestration service, or <c>null</c> to wire it later via the
        /// <see cref="RenderCoordinator"/> property. When <c>null</c> the host renders a cleared background.
        /// </param>
        public AvaloniaGeoViewport(GeoBoundingBox boundingBox, RenderCoordinator renderCoordinator)
            : base(boundingBox)
        {
            _renderCoordinator = renderCoordinator;
            // The companion control is created up front so the Views can host it immediately; the GL context
            // is bound lazily on the first OnOpenGlInit callback once the control is in the visual tree.
            _glHost = new GlHost(this);
        }

        /// <summary>
        /// The behavior-frozen render-orchestration service. May be assigned by the composition root after
        /// construction (before the surface goes live). All host callbacks null-guard this reference.
        /// </summary>
        public RenderCoordinator RenderCoordinator
        {
            get => _renderCoordinator;
            set => _renderCoordinator = value;
        }

        /// <summary>
        /// The companion Avalonia control to place in the visual tree. Exposed as the base
        /// <see cref="Control"/> type for general hosting in XAML / code-behind.
        /// </summary>
        public Control View => _glHost;

        /// <summary>
        /// The companion control typed as <see cref="OpenGlControlBase"/> for callers that need the GL
        /// surface directly (e.g. to forward pointer/wheel input or request a redraw).
        /// </summary>
        public OpenGlControlBase GlControl => _glHost;

        // ============================================================================================
        //  GeoViewportBase host-seam overrides (the three operations the Core drawing code isolates).
        // ============================================================================================

        /// <summary>
        /// [XPLAT] No-op. Avalonia owns GL-context currency: the context is made current by the framework
        /// for the duration of <c>OnOpenGlInit</c>/<c>OnOpenGlRender</c>/<c>OnOpenGlDeinit</c>, and the base
        /// only ever calls <see cref="MakeCurrent"/> from <see cref="GeoViewportBase.BeginPaint"/> /
        /// <see cref="GeoViewportBase.Resize"/>, which this host invokes solely from within those callbacks.
        /// Unlike the WinForms adapter (which called <c>GLControl.MakeCurrent()</c>) there is no public
        /// "make current" on <see cref="OpenGlControlBase"/>, and calling any OpenTK
        /// <c>GraphicsContext.MakeCurrent</c> here would fight Avalonia's context management — so this is
        /// deliberately empty.
        /// </summary>
        protected override void MakeCurrent()
        {
            // Intentionally empty — see method summary. [XPLAT]
        }

        /// <summary>
        /// [XPLAT] The GL surface size in DPI-aware FRAMEBUFFER PIXELS (not logical units), because the GL
        /// viewport, the scale math in <see cref="GeoViewportBase.BeginPaint"/> and every
        /// <c>glReadPixels</c> operate in pixels. The value is computed with the SAME formula Avalonia uses
        /// internally to size the GL framebuffer — <c>Max(1, (int)(Bounds.{Width,Height} * RenderScaling))</c>
        /// (truncation, floored at 1) — so the viewport, the FBO and the pixel reads all share one integer
        /// pixel grid. The floor-at-1 also guarantees <c>BeginPaint</c>'s division by the viewport size can
        /// never divide by zero before the surface is laid out.
        /// </summary>
        public override XyDelta ViewportSize
        {
            get
            {
                GetCurrentPixelSize(out int width, out int height);
                return new XyDelta(width, height);
            }
        }

        /// <summary>
        /// [XPLAT] Ends the frame with the base <see cref="GeoViewportBase.EndPaint"/> (which calls
        /// <c>GLW.Flush()</c>) and DELIBERATELY does NOT swap buffers. Avalonia presents the
        /// <see cref="OpenGlControlBase"/> framebuffer automatically after <c>OnOpenGlRender</c> returns,
        /// unlike the WinForms <c>GeoViewport</c> which had to call <c>_glControl.SwapBuffers()</c>. The
        /// override exists only to document this deliberate divergence from the WinForms adapter.
        /// </summary>
        public override void EndPaint()
        {
            base.EndPaint();
            // No SwapBuffers — presentation is implicit in Avalonia (the control presents the framebuffer
            // after OnOpenGlRender returns). [XPLAT]
        }

        /// <summary>
        /// Computes the current framebuffer pixel size from the companion control's bounds and the render
        /// scaling, replicating Avalonia's internal GL-surface sizing exactly. Falls back to the last known
        /// good size (then to 1x1) when the surface has not been laid out yet or reports a degenerate size,
        /// and caches the result so subsequent guarded reads stay stable.
        /// </summary>
        private void GetCurrentPixelSize(out int width, out int height)
        {
            // Visual.VisualRoot is not public; the public Avalonia.VisualTree.VisualExtensions.GetVisualRoot()
            // extension returns the same IRenderRoot (whose RenderScaling is public). [XPLAT]
            double scaling = _glHost.GetVisualRoot()?.RenderScaling ?? 1.0;
            if (double.IsNaN(scaling) || double.IsInfinity(scaling) || scaling <= 0.0)
            {
                scaling = 1.0;
            }

            // var avoids naming Avalonia.Rect explicitly (and any cross-namespace ambiguity with OpenTK).
            var bounds = _glHost.Bounds;

            // Match Avalonia's OpenGlControlBase.GetPixelSize: Max(1, (int)(extent * scaling)). The (int)
            // cast truncates; a NaN extent converts to 0, which Max(1, ...) lifts to 1.
            width = Math.Max(1, (int)(bounds.Width * scaling));
            height = Math.Max(1, (int)(bounds.Height * scaling));

            // Before layout the bounds are 0x0 -> width/height collapse to 1; prefer the last real size if
            // we have one so the first few frames do not flash a 1x1 viewport.
            if (bounds.Width <= 0.0 || bounds.Height <= 0.0)
            {
                width = _lastPixelWidth;
                height = _lastPixelHeight;
            }
            else
            {
                _lastPixelWidth = width;
                _lastPixelHeight = height;
            }
        }

        // ============================================================================================
        //  OpenTK 3.3.3 <-> Avalonia GL binding (process-wide, one-time) — the crux of keeping GLW
        //  untouched. DOMINANT feasibility risk (AAP §0.6.2): OpenTK 3.x loads GL through a
        //  GraphicsContext (NOT GLLoader as in 4.x). We construct a GraphicsContext whose entry points
        //  resolve from Avalonia's GlInterface.GetProcAddress, then LoadAll() so the static
        //  OpenTK.Graphics.OpenGL.GL.* methods GLW calls resolve to Avalonia's context functions.
        // ============================================================================================

        // Guards the one-time binding. GL entry points are process-wide static delegates in OpenTK, so
        // only the first viewport to initialize binds; later instances reuse the loaded pointers.
        private static readonly object GlBindingLock = new object();

        // The OpenTK context wrapper is kept alive for the process lifetime and NEVER disposed: it only
        // wraps Avalonia's externally-owned context (handle Zero + address-loader delegates), and tearing
        // it down could break a sibling viewport that is still live. Held in a field so the binding is not
        // garbage-collected (also keeps the field both written AND read, avoiding an unused-field warning).
        private static GraphicsContext _sharedGlContext;

        // One-time guard so a GLES/immediate-mode render failure logs once instead of every frame.
        private bool _renderErrorLogged;

        /// <summary>
        /// [XPLAT] Binds the kept OpenTK 3.3.3 GL entry points to Avalonia's GL context exactly once for the
        /// process. Returns <c>true</c> on success. On failure (the single least-certain integration point of
        /// the migration — AAP §0.6.2) it logs the dominant feasibility blocker and returns <c>false</c> so
        /// the host degrades gracefully instead of crashing startup.
        /// </summary>
        private static bool EnsureGlBindings(GlInterface gl)
        {
            if (_sharedGlContext != null)
            {
                return true;
            }

            lock (GlBindingLock)
            {
                if (_sharedGlContext != null)
                {
                    return true;
                }

                try
                {
                    // Explicitly-typed delegates avoid any constructor-overload ambiguity. GetProcAddress
                    // returns IntPtr, matching GraphicsContext.GetAddressDelegate exactly; the
                    // GetCurrentContextDelegate returns ContextHandle.Zero because Avalonia — not OpenTK —
                    // owns and tracks the current context.
                    var getAddress = new GraphicsContext.GetAddressDelegate(name => gl.GetProcAddress(name));
                    var getCurrent = new GraphicsContext.GetCurrentContextDelegate(() => ContextHandle.Zero);

                    var context = new GraphicsContext(ContextHandle.Zero, getAddress, getCurrent);
                    context.LoadAll();

                    _sharedGlContext = context;
                    Log.EventWriter("AvaloniaGeoViewport: OpenTK 3.3.3 GL entry points bound to Avalonia GL context.");
                    return true;
                }
                catch (Exception ex)
                {
                    // Hard feasibility blocker (AAP §0.6.2). Recorded loudly; the dominant open risk is
                    // tracked in MIGRATION_DOCS/PARITY_REPORT.md. Do not rethrow — never break startup.
                    Log.EventWriter("AvaloniaGeoViewport: FAILED to bind OpenTK GL to the Avalonia context "
                        + "(dominant feasibility risk — see MIGRATION_DOCS/PARITY_REPORT.md): " + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// [XPLAT] Reads a GL string safely, returning <see cref="string.Empty"/> on any failure or null result.
        /// </summary>
        private static string SafeGetGlString(StringName name)
        {
            try
            {
                return GL.GetString(name) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// [XPLAT] Queries and records the GL context strings (Version/Renderer/Vendor/GLSL) at first init and
        /// determines whether the context is OpenGL ES / ANGLE (under which the immediate-mode GLW DrawLib will
        /// not run). This is the dominant feasibility audit (AAP §0.6.2 / §0.6.5).
        /// </summary>
        private void AuditGlContext()
        {
            GlVersion = SafeGetGlString(StringName.Version);
            GlRenderer = SafeGetGlString(StringName.Renderer);
            GlVendor = SafeGetGlString(StringName.Vendor);
            GlShadingLanguageVersion = SafeGetGlString(StringName.ShadingLanguageVersion);
            IsContextAudited = true;

            // Desktop GL version strings look like "4.6.0 ..."; GLES strings contain "OpenGL ES", and ANGLE
            // advertises itself in the renderer/vendor. Either signals the immediate-mode renderer will fail.
            IsLikelyOpenGlEs =
                GlVersion.IndexOf("OpenGL ES", StringComparison.OrdinalIgnoreCase) >= 0
                || GlRenderer.IndexOf("ANGLE", StringComparison.OrdinalIgnoreCase) >= 0
                || GlVendor.IndexOf("ANGLE", StringComparison.OrdinalIgnoreCase) >= 0;

            Log.EventWriter("AvaloniaGeoViewport GL context audit -> Version='" + GlVersion
                + "', Renderer='" + GlRenderer + "', Vendor='" + GlVendor
                + "', GLSL='" + GlShadingLanguageVersion + "', LikelyGLES=" + IsLikelyOpenGlEs);

            if (IsLikelyOpenGlEs)
            {
                Log.EventWriter("AvaloniaGeoViewport: WARNING — GLES/ANGLE context detected. The immediate-mode "
                    + "GLW DrawLib requires a desktop-GL (compatibility) profile; request one in "
                    + "Program.BuildAvaloniaApp() (Program.cs agent). Dominant open risk — see PARITY_REPORT.md.");
            }
        }

        // ============================================================================================
        //  Avalonia OpenGlControlBase lifecycle handlers (forwarded from the nested GlHost). The GL
        //  context is guaranteed current for the duration of each handler.
        // ============================================================================================

        /// <summary>
        /// [XPLAT] First-init handler: binds OpenTK GL to Avalonia's context, audits the context, runs the
        /// static <see cref="GeoViewportBase.Initialize"/> global-state setup (moved here from the WinForms
        /// ctor because the context is not current until now), allocates the off-screen <c>oglBack</c>
        /// framebuffer, and invokes the coordinator's per-viewport load hook (textures + blend/cull state).
        /// </summary>
        private void HandleOpenGlInit(GlInterface gl)
        {
            if (!EnsureGlBindings(gl))
            {
                _glInitialized = false;
                return;
            }

            if (!IsContextAudited)
            {
                AuditGlContext();
            }

            try
            {
                // Static global GL state: GLW.EnableCullFace + SetCullFaceModeBack + SetClearColor(0,0,0,1).
                // Replaces the WinForms GeoViewport ctor's Initialize() call (context now current).
                GeoViewportBase.Initialize();
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: GeoViewportBase.Initialize() failed: " + ex.Message);
            }

            // Off-screen 500x300 back buffer for the section/look-ahead green-channel scan (oglBack parity).
            CreateBackFbo();

            try
            {
                // Coordinator load hook (oglMain_Load parity): GL.ClearColor(0.14,0.14,0.37,1),
                // BlendFunc(SrcAlpha, OneMinusSrcAlpha), CullFace(Back), setZoom(), SetVehicleTextures().
                _renderCoordinator?.OnViewportInit();
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: RenderCoordinator.OnViewportInit() failed: " + ex.Message);
            }

            _glInitialized = true;
        }

        /// <summary>
        /// [XPLAT] Per-frame handler. Caches Avalonia's framebuffer id, applies any pending resize through the
        /// inherited <see cref="GeoViewportBase.Resize"/> and the coordinator's projection, drives the visible
        /// main pass between <see cref="BeginPaint"/> and <see cref="EndPaint"/>, services any pending
        /// off-screen passes (each restoring Avalonia's framebuffer afterward), and requests the next frame to
        /// sustain continuous-render parity with the WinForms watchdog-driven paint.
        /// </summary>
        private void HandleOpenGlRender(GlInterface gl, int fb)
        {
            // Avalonia binds a NON-ZERO framebuffer as the visible render target; cache it so the off-screen
            // passes can restore it after binding their own FBO.
            _defaultFbo = fb;

            if (!_glInitialized)
            {
                // GL binding failed at init; nothing can be drawn. Do not spin the render loop.
                return;
            }

            GetCurrentPixelSize(out int width, out int height);
            bool sizeChanged = width != _appliedPixelWidth || height != _appliedPixelHeight;

            // Make the visible framebuffer the active draw target for the main pass.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb);

            if (sizeChanged)
            {
                // Inherited Resize: MakeCurrent (no-op) + GLW.Viewport(w,h) + base perspective. The coordinator
                // then installs its own (OpenGL.Designer.cs-derived) projection, which wins for the main paint.
                Resize(width, height);
                _renderCoordinator?.OnViewportResize(width, height);
                _appliedPixelWidth = width;
                _appliedPixelHeight = height;
            }

            BeginPaint();
            try
            {
                // The behavior-frozen draw routines live in RenderCoordinator; this host never ports draw
                // logic. Render() performs its own internal GL.Flush (NOT a swap) and the "No GPS" fallback.
                _renderCoordinator?.Render(this);
            }
            catch (Exception ex)
            {
                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.EventWriter("AvaloniaGeoViewport: RenderCoordinator.Render() threw (logged once; likely the "
                        + "immediate-mode-vs-GLES dominant risk — see PARITY_REPORT.md): " + ex.Message);
                }
            }
            EndPaint();

            // Off-screen section/look-ahead scan (oglBack) — DOMAIN-CRITICAL: drives the PGN machine/section
            // byte. The coordinator self-sets the 500x300 viewport, PixelStore and the green-channel ReadPixels.
            if (_backBufferScanPending)
            {
                _backBufferScanPending = false;
                RunBackBufferScan();
            }

            // Off-screen worked-area / overlap estimate (oglZoom) — cosmetic statistic.
            if (_zoomOverlapPending)
            {
                _zoomOverlapPending = false;
                RunZoomOverlap();
            }

            // Flag-mark pick reads from the VISIBLE framebuffer (flags are drawn into the main pass), so it
            // runs against Avalonia's framebuffer, not an off-screen FBO.
            if (_flagMarkPending)
            {
                _flagMarkPending = false;
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _defaultFbo);
                try
                {
                    _renderCoordinator?.MakeFlagMark();
                }
                catch (Exception ex)
                {
                    Log.EventWriter("AvaloniaGeoViewport: RenderCoordinator.MakeFlagMark() failed: " + ex.Message);
                }
            }

            // Continuous-render parity with the WinForms tmrWatchdog-driven paint. Avalonia throttles this to
            // the display refresh; the <=70 ms scan-loop cadence (AAP §0.1.1, §0.6.1) is owned by the
            // PositionService-driven RequestBackBufferScan(), independent of this redraw request.
            _glHost.RequestNextFrameRendering();
        }

        /// <summary>
        /// [XPLAT] Binds the off-screen <c>oglBack</c> framebuffer, runs the coordinator's section/look-ahead
        /// scan (which self-manages viewport, pixel-store and the green-channel <c>glReadPixels</c> with the
        /// same byte layout as the WinForms path), then restores Avalonia's framebuffer.
        /// </summary>
        private void RunBackBufferScan()
        {
            if (_backFbo == 0)
            {
                return; // FBO unavailable (init failed / unsupported); section scan degrades to no-op.
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _backFbo);
            try
            {
                _renderCoordinator?.RenderBackBufferAndScan();
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: RenderBackBufferAndScan() failed: " + ex.Message);
            }
            finally
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _defaultFbo);
            }
        }

        /// <summary>
        /// [XPLAT] Lazily (re)allocates an off-screen FBO sized to the coordinator's current zoom-overview
        /// surface, binds it, runs the overlap-estimate pass, then restores Avalonia's framebuffer.
        /// </summary>
        private void RunZoomOverlap()
        {
            if (_renderCoordinator == null)
            {
                return;
            }

            int zoomWidth = _renderCoordinator.OglZoomWidth;
            int zoomHeight = _renderCoordinator.OglZoomHeight;
            if (!EnsureZoomFbo(zoomWidth, zoomHeight))
            {
                return;
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _zoomFbo);
            try
            {
                _renderCoordinator.RenderZoomOverviewAndCalcOverlap();
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: RenderZoomOverviewAndCalcOverlap() failed: " + ex.Message);
            }
            finally
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _defaultFbo);
            }
        }

        // ============================================================================================
        //  Off-screen framebuffer management (color + depth renderbuffers). Idempotent and null-safe.
        // ============================================================================================

        /// <summary>
        /// [XPLAT] Creates the fixed 500x300 off-screen framebuffer that replaces the WinForms <c>oglBack</c>
        /// surface. Dimensions are FROZEN to keep the section-control green-channel scan byte-identical.
        /// </summary>
        private void CreateBackFbo()
        {
            DeleteBackFbo(); // idempotent across context re-init

            try
            {
                _backColorRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _backColorRenderbuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, BackBufferWidth, BackBufferHeight);

                _backDepthRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _backDepthRenderbuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, BackBufferWidth, BackBufferHeight);

                _backFbo = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _backFbo);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _backColorRenderbuffer);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _backDepthRenderbuffer);

                FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    Log.EventWriter("AvaloniaGeoViewport: off-screen oglBack framebuffer incomplete: " + status);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: failed to create oglBack framebuffer: " + ex.Message);
                DeleteBackFbo();
            }
            finally
            {
                // Restore the default framebuffer (Avalonia rebinds the real target on the next render anyway).
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
        }

        /// <summary>
        /// [XPLAT] Ensures the off-screen zoom-overview FBO matches the requested size, (re)allocating on a
        /// size change. Returns <c>true</c> when a complete FBO of the requested size is bound-ready.
        /// </summary>
        private bool EnsureZoomFbo(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (_zoomFbo != 0 && width == _zoomFboWidth && height == _zoomFboHeight)
            {
                return true;
            }

            DeleteZoomFbo();

            try
            {
                _zoomColorRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _zoomColorRenderbuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, height);

                _zoomDepthRenderbuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _zoomDepthRenderbuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);

                _zoomFbo = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _zoomFbo);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _zoomColorRenderbuffer);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _zoomDepthRenderbuffer);

                FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                _zoomFboWidth = width;
                _zoomFboHeight = height;

                if (status != FramebufferErrorCode.FramebufferComplete)
                {
                    Log.EventWriter("AvaloniaGeoViewport: off-screen oglZoom framebuffer incomplete: " + status);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, _defaultFbo);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter("AvaloniaGeoViewport: failed to create oglZoom framebuffer: " + ex.Message);
                DeleteZoomFbo();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _defaultFbo);
                return false;
            }
        }

        /// <summary>[XPLAT] Idempotent, null-safe disposal of the off-screen <c>oglBack</c> GL resources.</summary>
        private void DeleteBackFbo()
        {
            if (_backFbo != 0)
            {
                GL.DeleteFramebuffer(_backFbo);
                _backFbo = 0;
            }
            if (_backColorRenderbuffer != 0)
            {
                GL.DeleteRenderbuffer(_backColorRenderbuffer);
                _backColorRenderbuffer = 0;
            }
            if (_backDepthRenderbuffer != 0)
            {
                GL.DeleteRenderbuffer(_backDepthRenderbuffer);
                _backDepthRenderbuffer = 0;
            }
        }

        /// <summary>[XPLAT] Idempotent, null-safe disposal of the off-screen <c>oglZoom</c> GL resources.</summary>
        private void DeleteZoomFbo()
        {
            if (_zoomFbo != 0)
            {
                GL.DeleteFramebuffer(_zoomFbo);
                _zoomFbo = 0;
            }
            if (_zoomColorRenderbuffer != 0)
            {
                GL.DeleteRenderbuffer(_zoomColorRenderbuffer);
                _zoomColorRenderbuffer = 0;
            }
            if (_zoomDepthRenderbuffer != 0)
            {
                GL.DeleteRenderbuffer(_zoomDepthRenderbuffer);
                _zoomDepthRenderbuffer = 0;
            }
            _zoomFboWidth = 0;
            _zoomFboHeight = 0;
        }

        /// <summary>
        /// [XPLAT] Teardown handler: disposes the off-screen framebuffers. The shared OpenTK GL binding wrapper
        /// is intentionally NOT disposed (it wraps Avalonia's externally-owned context and the loaded entry
        /// points are process-wide; a sibling viewport may still be live).
        /// </summary>
        private void HandleOpenGlDeinit(GlInterface gl)
        {
            DeleteBackFbo();
            DeleteZoomFbo();
            _appliedPixelWidth = -1;
            _appliedPixelHeight = -1;
            _glInitialized = false;
        }

        // ============================================================================================
        //  Cross-thread off-screen-pass requests. The actual GL work runs inside OnOpenGlRender (where the
        //  context is current); a request raises a volatile flag and asks Avalonia for a prompt frame.
        // ============================================================================================

        /// <summary>
        /// [XPLAT] Requests the section/look-ahead back-buffer scan (oglBack) on the next rendered frame.
        /// Safe to call from any thread (e.g. PositionService's receive->fuse->steer->section loop).
        /// </summary>
        public void RequestBackBufferScan()
        {
            _backBufferScanPending = true;
            RequestRender();
        }

        /// <summary>
        /// [XPLAT] Requests the off-screen worked-area / overlap estimate (oglZoom) on the next rendered frame.
        /// Safe to call from any thread.
        /// </summary>
        public void RequestZoomOverlapCalc()
        {
            _zoomOverlapPending = true;
            RequestRender();
        }

        /// <summary>
        /// [XPLAT] Requests a flag-mark colour pick from the visible framebuffer on the next rendered frame.
        /// Safe to call from any thread.
        /// </summary>
        public void RequestFlagMark()
        {
            _flagMarkPending = true;
            RequestRender();
        }

        /// <summary>
        /// [XPLAT] Asks the companion control for a prompt redraw, marshalling onto the UI thread because
        /// <see cref="OpenGlControlBase.RequestNextFrameRendering"/> must be invoked there while callers may be
        /// on a threadpool thread.
        /// </summary>
        private void RequestRender()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _glHost.RequestNextFrameRendering();
            }
            else
            {
                Dispatcher.UIThread.Post(() => _glHost.RequestNextFrameRendering());
            }
        }

        // ============================================================================================
        //  Nested companion control. Single inheritance forces composition: AvaloniaGeoViewport derives
        //  from GeoViewportBase, so the OpenGlControlBase lives here and forwards its protected lifecycle
        //  callbacks back to the owning adapter (exactly as the WinForms adapter HELD a GLControl).
        // ============================================================================================

        /// <summary>
        /// [XPLAT] Private Avalonia OpenGL control owned by <see cref="AvaloniaGeoViewport"/>. It exists solely
        /// to surface <see cref="OpenGlControlBase"/>'s protected GL lifecycle callbacks and forward them to
        /// the owner, which holds the <see cref="GeoViewportBase"/> rendering state.
        /// </summary>
        private sealed class GlHost : OpenGlControlBase
        {
            private readonly AvaloniaGeoViewport _owner;

            /// <summary>Creates the companion control bound to its owning <paramref name="owner"/> adapter.</summary>
            public GlHost(AvaloniaGeoViewport owner)
            {
                _owner = owner;
            }

            /// <inheritdoc />
            protected override void OnOpenGlInit(GlInterface gl)
            {
                _owner.HandleOpenGlInit(gl);
            }

            /// <inheritdoc />
            protected override void OnOpenGlRender(GlInterface gl, int fb)
            {
                _owner.HandleOpenGlRender(gl, fb);
            }

            /// <inheritdoc />
            protected override void OnOpenGlDeinit(GlInterface gl)
            {
                _owner.HandleOpenGlDeinit(gl);
            }
        }
    }
}
