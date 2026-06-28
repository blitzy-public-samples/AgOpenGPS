// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Provenance summary for this code-behind:
//   * WinForms System.Windows.Forms.Form  -> Avalonia.Controls.Window. The 1:1 source is
//     Forms/Guidance/FormGrid.cs + FormGrid.Designer.cs (the "2 Points - Align Grid" tool — note the
//     source's handlers are named FormABDraw_* because FormGrid was cloned from FormABDraw). Behaviour
//     is frozen; only the host stack changes.
//   * The central FormGPS ("mf") god-object is removed (AAP §0.3.2 "Extract Class / inject
//     collaborators"). Everything the original reached through "mf." becomes a constructor-injected
//     concrete domain object or a Func/Action callback — see the constructor and the per-field notes.
//   * GL hosting: the WinForms oglSelf (OpenTK.GLControl) surface and its
//     "GeoViewport _viewport = new GeoViewport(mf.FieldBoundingBox, oglSelf)" become an
//     AgOpenGPS.Controls.AvaloniaGeoViewport built from the injected bounding box and inserted into the
//     XAML "ViewportHost" Border. The former oglSelf_Paint body is supplied to the host as its
//     RenderAction; the geometry maths and every Core visual draw call are preserved verbatim
//     (AAP §0.2.2 behaviour-frozen contracts).
//
// [XPLAT] RENDER-HOOK CONTRACT (verified against the sibling AvaloniaGeoViewport + FormBndToolView):
//   AvaloniaGeoViewport.HandleOpenGlRender() itself calls BeginPaint() -> RenderAction?.Invoke() ->
//   EndPaint() each frame. Therefore RenderViewport() (the ported oglSelf_Paint body) issues ONLY the
//   Core visual draw calls and MUST NOT call BeginPaint()/EndPaint() or touch the GL context — the host
//   owns the wrap. This refines the file spec's "wrap the draw in BeginPaint()/EndPaint()" wording:
//   the adapter does the wrapping; this view subscribes its draw body via RenderAction.
//
// [XPLAT] DOMINANT FEASIBILITY RISK (AAP §0.6.2 / §0.6.5): the kept Core DrawLib / visual helpers
//   (SectionsVisual / FenceLineVisual / VehicleDotVisual / TouchPointsLineVisual -> GLW) use legacy
//   immediate-mode OpenGL, so correct rendering depends on AvaloniaGeoViewport supplying a desktop-GL
//   compatibility context (a GLES/ANGLE context gates the immediate-mode path). This view consumes the
//   adapter only — there is no GL code here — so the risk is owned entirely by AvaloniaGeoViewport.cs.
using System;
using System.Collections.Generic;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Visuals;
using AgOpenGPS.Helpers;
using AgOpenGPS.Visuals;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for the "2 Points - Align Grid" dialog — a faithful 1:1 behavioural port of the
/// WinForms <c>FormGrid</c> (<c>Forms/Guidance/FormGrid.cs</c> + <c>FormGrid.Designer.cs</c>). The
/// operator taps two points in the square field viewport to define a direction (point A then point B);
/// the world-grid is rotated to that AB heading. "Align to track" instead snaps the grid to the active
/// guidance track's AB heading. The four buttons down the right edge clear the tapped points, align to
/// track, cancel (reset the grid rotation to 0), or accept and close.
/// </summary>
/// <remarks>
/// <para>
/// This is an IMPERATIVE dialog — there is intentionally no <c>DataContext</c>, no <c>x:DataType</c> and
/// no MVVM data binding. The <c>x:Name</c>'d controls declared in <c>FormGridView.axaml</c>
/// (<c>ViewportHost</c>, <c>tlp1</c>, <c>btnCancelTouch</c>, <c>btnAlignToTrack</c>, <c>btnCancel</c>,
/// <c>btnExit</c>) are the entire integration surface; the markup wires no handlers, so the constructor
/// attaches them programmatically. The handlers keep the original WinForms method names for traceability.
/// </para>
/// <para>
/// The caller shows the dialog non-modally (<c>.Show(owner)</c>): there is no dialog result. The dialog
/// writes <c>worldGrid.gridRotation</c> directly on the injected <see cref="Core.WorldGrid"/> and closes.
/// </para>
/// </remarks>
public partial class FormGridView : Window
{
    // ===================================================================================================
    //  Injected collaborators (replace the WinForms FormGPS "mf" back-reference). See the AAP file spec
    //  mapping table; types verified against the source classes.
    // ===================================================================================================

    // [XPLAT] was mf.triStrip — recorded coverage patches drawn by SectionsVisual.DrawSections.
    private readonly List<CPatches> _triStrip;

    // [XPLAT] was mf.bnd — the boundary model (bndList; each CBoundaryList.fenceLineEar drawn as a fence).
    private readonly CBoundary _bnd;

    // [XPLAT] was mf.pivotAxlePos (a live vec3) — read each frame for the vehicle dot, so it is injected
    // as a Func<vec3> rather than a snapshot value.
    private readonly Func<vec3> _getPivotAxlePos;

    // [XPLAT] was mf.worldGrid — the dialog's whole purpose is to set worldGrid.gridRotation.
    private readonly Core.WorldGrid _worldGrid;

    // [XPLAT] was mf.curve — its isCurveValid flag is cleared on close and desList is cleared on
    // "clear touch points".
    private readonly CABCurve _curve;

    // [XPLAT] was mf.ABLine — its isABValid flag is cleared on close.
    private readonly CABLine _ABLine;

    // [XPLAT] was mf.trk — the active track collection (idx + gArr[].ptA/ptB) read by "Align to track".
    private readonly CTrack _trk;

    // [XPLAT] was "mf.twoSecondCounter = 100" — poke the main scan loop's counter on close so the main
    // view refreshes promptly. Injected as an Action<int> setter.
    private readonly Action<int> _setTwoSecondCounter;

    // [XPLAT] was "GeoViewport _viewport = new GeoViewport(mf.FieldBoundingBox, oglSelf)". The Avalonia
    // adapter derives from the same Core GeoViewportBase, so the inherited camera/zoom/pan helpers,
    // GetGeoCoord, BeginPaint/EndPaint and Resize are used exactly as before; only the host control
    // changes. The host owns the GL context and self-manages resize.
    private readonly AgOpenGPS.Controls.AvaloniaGeoViewport _viewport;

    // [XPLAT] WinForms System.Windows.Forms.Timer (Interval 500, Enabled) -> Avalonia DispatcherTimer.
    // Its tick requests a viewport redraw (was timer1_Tick -> oglSelf.Refresh()).
    private readonly DispatcherTimer _timer;

    // [XPLAT] Re-entrancy guard for the square-layout pass: ApplySquareLayout assigns Width, which itself
    // raises a size-changed notification; the guard makes that converge in one settle cycle instead of
    // recursing.
    private bool _inSquareLayout;

    // [XPLAT] Kept EXACTLY from the source (public fields): the two tapped points that define the AB
    // direction. GeoCoord is a value type (AgOpenGPS.Core.Models), so GeoCoord? is Nullable<GeoCoord>.
    public GeoCoord? _coordA;
    public GeoCoord? _coordB;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML runtime loader and the
    /// design-time previewer so the <c>avares://AgOpenGPS/Views/Guidance/FormGridView.axaml</c> resource
    /// stays reachable (otherwise the build emits <c>AVLN3001</c>, which the Release configuration treats
    /// as an error). It renders the static markup, wires no behaviour and leaves the injected fields
    /// null, so the lifecycle guards treat a loader/preview instance as a no-op. Production code uses the
    /// injected overload.
    /// </summary>
    public FormGridView()
    {
        // [XPLAT] Emitted by the Avalonia XAML source generator from FormGridView.axaml; also creates the
        // typed x:Name'd control fields (ViewportHost, tlp1, btnCancelTouch, btnAlignToTrack, btnCancel,
        // btnExit) this code-behind addresses.
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog bound to the supplied collaborators, mirroring the WinForms
    /// <c>FormGrid(Form callingForm)</c> constructor: it stores the injected objects, runs
    /// <paramref name="calculateMinMax"/> (was <c>mf.CalculateMinMax()</c>), builds the GL viewport from
    /// the field bounding box, hosts it in the <c>ViewportHost</c> Border and wires the controls (the
    /// markup declares no handlers).
    /// </summary>
    /// <param name="fieldBoundingBox">Field extents used to build the viewport (was <c>mf.FieldBoundingBox</c>).</param>
    /// <param name="calculateMinMax">Recomputes the field min/max before display (was <c>mf.CalculateMinMax()</c>).</param>
    /// <param name="triStrip">Recorded coverage patches drawn by the section visual (was <c>mf.triStrip</c>).</param>
    /// <param name="bnd">The boundary model (was <c>mf.bnd</c>).</param>
    /// <param name="getPivotAxlePos">Supplies the live pivot-axle position for the vehicle dot (was <c>mf.pivotAxlePos</c>).</param>
    /// <param name="worldGrid">The world grid whose rotation this dialog sets (was <c>mf.worldGrid</c>).</param>
    /// <param name="curve">The active curve (was <c>mf.curve</c>).</param>
    /// <param name="ABLine">The active AB line (was <c>mf.ABLine</c>).</param>
    /// <param name="trk">The track collection used by "Align to track" (was <c>mf.trk</c>).</param>
    /// <param name="setTwoSecondCounter">Sets the main scan-loop counter on close (was <c>mf.twoSecondCounter = 100</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown when any reference collaborator is null.</exception>
    public FormGridView(
        GeoBoundingBox fieldBoundingBox,
        Action calculateMinMax,
        List<CPatches> triStrip,
        CBoundary bnd,
        Func<vec3> getPivotAxlePos,
        Core.WorldGrid worldGrid,
        CABCurve curve,
        CABLine ABLine,
        CTrack trk,
        Action<int> setTwoSecondCounter)
        : this()
    {
        // [XPLAT] The DI seam replaces "mf = callingForm as FormGPS;" — fail fast on a missing wiring
        // rather than NRE deep inside a handler. GeoBoundingBox is a struct, so it is not null-checked.
        if (calculateMinMax == null) throw new ArgumentNullException(nameof(calculateMinMax));
        _triStrip = triStrip ?? throw new ArgumentNullException(nameof(triStrip));
        _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        _getPivotAxlePos = getPivotAxlePos ?? throw new ArgumentNullException(nameof(getPivotAxlePos));
        _worldGrid = worldGrid ?? throw new ArgumentNullException(nameof(worldGrid));
        _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        _ABLine = ABLine ?? throw new ArgumentNullException(nameof(ABLine));
        _trk = trk ?? throw new ArgumentNullException(nameof(trk));
        _setTwoSecondCounter = setTwoSecondCounter ?? throw new ArgumentNullException(nameof(setTwoSecondCounter));

        // [XPLAT] was mf.CalculateMinMax() in the WinForms constructor.
        calculateMinMax();

        // [XPLAT] Build the viewport from the injected bounding box (was new GeoViewport(mf.FieldBoundingBox,
        // oglSelf)). The former oglSelf_Paint body is supplied as the host's RenderAction; the host invokes
        // it between its own BeginPaint()/EndPaint() (see the RENDER-HOOK CONTRACT note above).
        _viewport = new AgOpenGPS.Controls.AvaloniaGeoViewport(fieldBoundingBox);
        _viewport.RenderAction = RenderViewport;

        // [XPLAT] Host the viewport's exposed Control in the XAML 'ViewportHost' Border (was the oglSelf
        // surface). The Black Border background shows through until the GL surface paints.
        ViewportHost.Child = _viewport.View;

        // [XPLAT] WinForms Timer (Interval 500, Enabled) -> DispatcherTimer started on open.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += timer1_Tick;

        // [XPLAT] Wire the controls imperatively (the .axaml declares no Click=/event attributes). Done in
        // the injected ctor only, so a loader/previewer instance stays inert.
        btnCancelTouch.Click += btnCancelTouch_Click;
        btnAlignToTrack.Click += btnAlignToTrack_Click;
        btnCancel.Click += btnCancel_Click;
        btnExit.Click += btnExit_Click;

        // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown was
        // wired in the designer; no Move/Up/Wheel).
        _viewport.View.PointerPressed += oglSelf_MouseDown;

        // [XPLAT] Keep the viewport square and re-anchor on resize (was FormABDraw_ResizeEnd, originally
        // hooked to the WinForms ResizeEnd / oglSelf.Resize events).
        ViewportHost.SizeChanged += ViewportHost_SizeChanged;
    }

    // ===================================================================================================
    //  Lifecycle (was FormABDraw_Load / FormABDraw_FormClosing, plus disposal).
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Ports <c>FormABDraw_Load</c>: restores the persisted window size and location, runs the
    /// square-layout pass, recovers the window if it would open off-screen, and starts the redraw timer.
    /// Avalonia raises <c>Opened</c> once the window is shown — the cross-platform analogue of the WinForms
    /// <c>Load</c> event.
    /// </summary>
    /// <param name="e">The event payload.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // A loader/previewer instance has no viewport — stay completely inert (and never touch Settings).
        if (_viewport == null)
        {
            return;
        }

        // [XPLAT] WinForms: Size = Properties.Settings.Default.setWindow_gridSize (System.Drawing.Size).
        System.Drawing.Size savedSize = AgOpenGPS.Properties.Settings.Default.setWindow_gridSize;
        if (savedSize.Width > 0 && savedSize.Height > 0)
        {
            Width = savedSize.Width;
            Height = savedSize.Height;
        }

        // [XPLAT] WinForms: Location = Properties.Settings.Default.setWindow_gridLocation
        // (System.Drawing.Point -> Avalonia PixelPoint). WindowStartupLocation is "Manual" in the .axaml.
        System.Drawing.Point savedLocation = AgOpenGPS.Properties.Settings.Default.setWindow_gridLocation;
        Position = new PixelPoint(savedLocation.X, savedLocation.Y);

        // [XPLAT] was FormABDraw_ResizeEnd(this, e) — apply the square viewport + aspect now.
        ApplySquareLayout();

        // [XPLAT] Off-screen recovery. WinForms: if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }
        // ScreenHelper now resolves the monitor topology from the window's own Avalonia Screens collection.
        if (!ScreenHelper.IsOnScreen(this))
        {
            Position = new PixelPoint(0, 0);
        }

        // [XPLAT] WinForms timer1.Enabled = true -> start the DispatcherTimer.
        _timer.Start();
    }

    /// <summary>
    /// [XPLAT] Ports <c>FormABDraw_FormClosing</c>: invalidates the curve and AB line so the main view
    /// recomputes them, pokes the scan-loop counter, and persists the window geometry. Runs before the
    /// base close so the settings are saved exactly once.
    /// </summary>
    /// <param name="e">The window-closing payload.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A loader/previewer instance has no collaborators — skip the parity body.
        if (_viewport != null)
        {
            // [XPLAT] mf.curve.isCurveValid = false; mf.ABLine.isABValid = false; mf.twoSecondCounter = 100;
            _curve.isCurveValid = false;
            _ABLine.isABValid = false;
            _setTwoSecondCounter(100);

            // [XPLAT] Persist size + location (was Settings.setWindow_gridSize / setWindow_gridLocation).
            // Avalonia Width/Height (double) -> System.Drawing.Size(int); Position (PixelPoint) -> Point.
            double w = Width, h = Height;
            if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
            {
                AgOpenGPS.Properties.Settings.Default.setWindow_gridSize =
                    new System.Drawing.Size((int)w, (int)h);
            }
            AgOpenGPS.Properties.Settings.Default.setWindow_gridLocation =
                new System.Drawing.Point(Position.X, Position.Y);
            AgOpenGPS.Properties.Settings.Default.Save();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// [XPLAT] Stops and detaches the redraw timer and releases the render callback so nothing keeps
    /// ticking or drawing after the dialog is gone. There is no WinForms equivalent (the form's
    /// components were disposed by the designer), but the DispatcherTimer and the host render callback
    /// must be torn down explicitly to avoid a leak.
    /// </summary>
    /// <param name="e">The event payload.</param>
    protected override void OnClosed(EventArgs e)
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= timer1_Tick;
        }

        if (_viewport != null)
        {
            _viewport.RenderAction = null;
        }

        base.OnClosed(e);
    }

    // ===================================================================================================
    //  Layout (was FormABDraw_ResizeEnd / oglSelf_Resize / oglSelf_Load / CreateViewport).
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] <c>ViewportHost.SizeChanged</c> handler — the Avalonia analogue of the WinForms
    /// <c>ResizeEnd</c> / <c>oglSelf_Resize</c> events. Defers to <see cref="ApplySquareLayout"/>.
    /// </summary>
    /// <param name="sender">The viewport host; unused.</param>
    /// <param name="e">The size-changed payload; unused.</param>
    private void ViewportHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplySquareLayout();
    }

    /// <summary>
    /// [XPLAT] Ports <c>FormABDraw_ResizeEnd</c>: keeps the field viewport square and reproduces the
    /// source window aspect. WinForms used absolute control coordinates
    /// (<c>Width = (int)(Height * 1.09); oglSelf.Height = oglSelf.Width = Height - 40;</c>); here the
    /// viewport host is forced to a <c>Height - 40</c> square inside the responsive Grid and the window
    /// width is set to <c>(int)(Height * 1.09)</c>. The host self-manages its GL resize from the control's
    /// actual pixel size, so the explicit <c>Resize</c> is a parity hint for the immediate frame. The
    /// re-entrancy guard keeps the <c>Width</c> assignment from recursing.
    /// </summary>
    private void ApplySquareLayout()
    {
        if (_inSquareLayout || _viewport == null)
        {
            return;
        }

        double h = Height;
        if (double.IsNaN(h) || h <= 40.0)
        {
            return;
        }

        _inSquareLayout = true;
        try
        {
            // [XPLAT] source: oglSelf.Height = oglSelf.Width = Height - 40;
            int side = (int)(h - 40.0);
            if (side < 1)
            {
                side = 1;
            }

            if (ViewportHost.Width != side)
            {
                ViewportHost.Width = side;
            }
            if (ViewportHost.Height != side)
            {
                ViewportHost.Height = side;
            }

            // [XPLAT] source: Width = (int)(Height * 1.09); guard avoids re-triggering on the same value.
            double targetWidth = (int)(h * 1.09);
            if (Math.Abs(Width - targetWidth) > 0.5)
            {
                Width = targetWidth;
            }

            // [XPLAT] source: _viewport.Resize(oglSelf.Width, oglSelf.Height). The adapter re-derives its
            // pixel size from the host control each frame, so this is an immediate hint only.
            _viewport.Resize(side, side);
        }
        finally
        {
            _inSquareLayout = false;
        }
    }

    // ===================================================================================================
    //  Button handlers — ported 1:1 from FormGrid.cs.
    // ===================================================================================================

    /// <summary>[XPLAT] Ports <c>btnExit_Click</c>: accept and close (the dialog has already written the rotation).</summary>
    /// <param name="sender">The accept button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>[XPLAT] Ports <c>btnCancel_Click</c>: reset the grid rotation to 0 and close.</summary>
    /// <param name="sender">The cancel button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        _worldGrid.gridRotation = 0;
        Close();
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnCancelTouch_Click</c>: clear the two tapped points, clear the curve's working
    /// point list, reset the viewport zoom/pan and move focus to the accept button.
    /// </summary>
    /// <param name="sender">The clear-touch button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnCancelTouch_Click(object sender, RoutedEventArgs e)
    {
        _coordA = null;
        _coordB = null;
        _curve.desList?.Clear();

        _viewport.ResetZoomPan();

        btnExit.Focus();
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnAlignToTrack_Click</c>: if a track is active, rotate the world grid to that
    /// track's AB heading (atan2 of the easting/northing delta, wrapped into [0, 2π) then converted to
    /// degrees) and close. The maths is preserved verbatim from the source.
    /// </summary>
    /// <param name="sender">The align-to-track button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnAlignToTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_trk.idx > -1)
        {
            _worldGrid.gridRotation = Math.Atan2(
                _trk.gArr[_trk.idx].ptB.easting - _trk.gArr[_trk.idx].ptA.easting,
                _trk.gArr[_trk.idx].ptB.northing - _trk.gArr[_trk.idx].ptA.northing);
            if (_worldGrid.gridRotation < 0) _worldGrid.gridRotation += glm.twoPI;
            _worldGrid.gridRotation = glm.toDegrees(_worldGrid.gridRotation);
        }
        Close();
    }

    // ===================================================================================================
    //  Viewport interaction + render (was oglSelf_MouseDown / oglSelf_Paint / timer1_Tick).
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Ports <c>oglSelf_MouseDown</c>: maps the pointer to a field coordinate and captures the two
    /// tap points. WinForms used <c>oglSelf.PointToClient(Cursor.Position)</c> (client PIXELS);
    /// <c>e.GetPosition</c> returns logical (DIP) coordinates while <see cref="GeoViewportBase.GetGeoCoord"/>
    /// expects the same pixel space the viewport reports as its size, so the position is scaled by the
    /// visual root's render scaling — mirroring how the host derives its pixel size. The first tap sets
    /// <see cref="_coordA"/>; the second sets <see cref="_coordB"/> and rotates the grid to the AB heading.
    /// </summary>
    /// <param name="sender">The hosted GL control; unused.</param>
    /// <param name="e">The pointer-pressed payload (provides the tap position).</param>
    private void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
    {
        var pt = e.GetPosition(_viewport.View);
        double scaling = _viewport.View.GetVisualRoot()?.RenderScaling ?? 1.0;
        if (double.IsNaN(scaling) || double.IsInfinity(scaling) || scaling <= 0.0)
        {
            scaling = 1.0;
        }

        XyCoord xyClient = new XyCoord(pt.X * scaling, pt.Y * scaling);
        GeoCoord mouseDownCoord = _viewport.GetGeoCoord(xyClient);

        if (!_coordA.HasValue)
        {
            _coordA = mouseDownCoord;
        }
        else
        {
            _coordB = mouseDownCoord;
            GeoDir abDir = new GeoDir(_coordA.Value, _coordB.Value);
            _worldGrid.gridRotation = abDir.AngleInDegrees;
        }

        // [XPLAT] was oglSelf.Refresh().
        _viewport.RequestRender();
    }

    /// <summary>
    /// [XPLAT] Ports the body of <c>oglSelf_Paint</c>. Supplied to the host as
    /// <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport.RenderAction"/>; the host invokes it between its
    /// own <c>BeginPaint()</c>/<c>EndPaint()</c>, so this method issues ONLY the behaviour-frozen Core
    /// visual draw calls and never touches the GL context (see the RENDER-HOOK CONTRACT note at the top of
    /// the file). The draw sequence is identical to the WinForms source.
    /// </summary>
    private void RenderViewport()
    {
        SectionsVisual.DrawSections(_triStrip);

        for (int j = 0; j < _bnd.bndList.Count; j++)
        {
            GeoCoord[] fenceLineEar = GeoRefactorHelper.ToGeoCoordArray(_bnd.bndList[j].fenceLineEar);
            bool isSelected = j == 0;
            FenceLineVisual.DrawFenceLine(fenceLineEar, isSelected);
        }

        VehicleDotVisual.DrawVehicleDot(_getPivotAxlePos().ToGeoCoord());
        TouchPointsLineVisual.DrawTouchPoints(_coordA, _coordB);
    }

    /// <summary>
    /// [XPLAT] Ports <c>timer1_Tick</c>: requests a viewport redraw (was <c>oglSelf.Refresh()</c>). The
    /// host throttles the actual repaint to the display refresh.
    /// </summary>
    /// <param name="sender">The dispatcher timer; unused.</param>
    /// <param name="e">The event payload; unused.</param>
    private void timer1_Tick(object sender, EventArgs e)
    {
        _viewport.RequestRender();
    }
}
