// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
// [XPLAT] RISK: immediate-mode OpenGL (GL.Begin/Vertex3) drawn inside AvaloniaGeoViewport.BeginPaint/EndPaint requires a desktop-GL compatibility context; will not run under GLES/ANGLE. See PARITY_REPORT.md (AAP §0.6.2).
//
// Code-behind for FormTramLineView.axaml — a 1:1 behavioural-parity reimplementation of the WinForms
// Forms/Guidance/FormTramLine (FormTramLine.cs + FormTramLine.Designer.cs): the advanced tram-line
// builder/cutter. The operator cycles the visible guidance tracks (AB / Curve), builds parallel tram
// lines (passes / start-pass / opacity), optionally lays an outer-boundary tram, and uses a three-click
// cutting tool on the live OpenGL viewport to trim built tram segments.
//
// The tram-building geometry (BuildCurveTram / BuildABTram / BuildTramBnd) and the segment-intersection
// cut math are behavior-frozen (AAP §0.2.2) and ported VERBATIM. Only three things change versus the
// WinForms original:
//   * the GL host (WinForms OpenTK.GLControl + AgOpenGPS.WinForms.GeoViewport -> the cross-platform
//     AgOpenGPS.Controls.AvaloniaGeoViewport adapter, which derives from the same Core GeoViewportBase),
//   * the dialogs (FormDialog -> FormDialogView; mf.YesMessageBox -> an injected showYesMessage delegate),
//   * the FormGPS 'mf' god-object back-reference -> constructor-injected concrete domain objects + plain
//     delegates (no new interfaces introduced — AAP §0.7.1).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.Visuals;
using AgOpenGPS.Helpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] The Advanced Tram Lines builder/cutter dialog. Hosts a live OpenGL field viewport on the left
/// and a 3×9 touch command panel on the right, and reproduces every behaviour of the WinForms
/// <c>FormTramLine</c>. The dialog is code-behind driven (no <c>DataContext</c> / no bindings, exactly like
/// the sibling Guidance views): every control is addressed imperatively by its <c>x:Name</c>, which keeps
/// the ORIGINAL WinForms control names so the ported logic references them as it did before.
/// </summary>
public partial class FormTramLineView : Window
{
    // ===================================================================================================
    //  Injected collaborators (replace the WinForms "FormGPS mf" back-reference). Names match the AAP
    //  mapping table so the ported body reads exactly as the source did with the "mf." prefix removed.
    //  No new interfaces are introduced (AAP §0.7.1) — concrete domain objects + plain delegates only.
    // ===================================================================================================

    // [XPLAT] was mf.tram — the tram model (tramArr/tramList/tramBndOuterArr/tramBndInnerArr, tramWidth,
    // halfWheelTrack, alpha, displayMode, CreateBoundaryOuterTrack/CreateBoundaryInnerTrack).
    private readonly CTram tram;

    // [XPLAT] was mf.trk — the track model exposing gArr (the guidance tracks cloned into gTemp). NOTE: the
    // runtime object mf.trk is a CTrack (gArr is declared on CTrack); the AAP wrote "CTrackMethods", which is
    // the *file* CTrackMethods.cs — its static TrackMethods class supplies the ReducePointsByAngle extension
    // used in OnClosing. There is NO type named CTrackMethods, so this is typed CTrack so trk.gArr binds.
    private readonly CTrack trk;

    // [XPLAT] was mf.tool — implement geometry (width / overlap / halfWidth).
    private readonly CTool tool;

    // [XPLAT] was mf.bnd — the boundary model (bndList[].fenceLineEar used to clip tram points + draw fences).
    private readonly CBoundary bnd;

    // [XPLAT] was mf.ABLine — supplies abLength for the AB endpoint extension in DrawBuiltLines.
    private readonly CABLine ABLine;

    // [XPLAT] was mf.vehicle — supplies VehicleConfig.TrackWidth for the title build (FixLabelsCurve).
    private readonly CVehicle vehicle;

    // [XPLAT] was "GeoViewport _viewport = new GeoViewport(mf.FieldBoundingBox, oglSelf)". The Avalonia
    // adapter derives from the SAME Core GeoViewportBase, so the inherited GetGeoCoord/camera helpers are
    // used exactly as before; only the host control changes. Supplied by the composition root (MainView).
    private readonly AvaloniaGeoViewport _viewport;

    // [XPLAT] was mf.FieldBoundingBox — used to (re)seat the viewport extents after calculateMinMax().
    private readonly Func<GeoBoundingBox> getFieldBoundingBox;

    // [XPLAT] was mf.maxFieldDistance — half-length the AB tram reference line is extended each way.
    private readonly Func<double> getMaxFieldDistance;

    // [XPLAT] was mf.m2FtOrM — metric/imperial display multiplier for the title build.
    private readonly Func<double> getM2FtOrM;

    // [XPLAT] was mf.unitsFtM — the " ft"/" m" unit suffix for the title build.
    private readonly Func<string> getUnitsFtM;

    // [XPLAT] was mf.CalculateMinMax() — recompute field extents before the viewport seats its bounding box.
    private readonly Action calculateMinMax;

    // [XPLAT] was mf.FileSaveTram() — persist the committed tram lines (OnClosing).
    private readonly Action fileSaveTram;

    // [XPLAT] was mf.PanelUpdateRightAndBottom() — refresh the main GPS status panels (OnClosing).
    private readonly Action panelUpdateRightAndBottom;

    // [XPLAT] was mf.FixTramModeButton() — refresh the main tram-mode toolbar button (OnClosing).
    private readonly Action fixTramModeButton;

    // [XPLAT] was mf.YesMessageBox(string) — cross-platform yes/OK advisory; awaited so the message is seen
    // BEFORE the no-guidance-lines exit closes the window (parity with the modal WinForms message box).
    private readonly Func<string, Task> showYesMessage;

    // ===================================================================================================
    //  Algorithm + UI state — ported VERBATIM from FormTramLine.cs (AAP Phase 1; behavior frozen).
    // ===================================================================================================
    private bool isCancel = false;

    private int indx = -1;

    private vec2 ptA = new vec2(9999999, 9999999);
    private vec2 ptB = new vec2(9999999, 9999999);
    private vec2 ptCut = new vec2(9999999, 9999999);

    private int step = 0;

    //tramTrams
    private List<vec2> tramArr = new List<vec2>();

    private List<List<vec2>> tramList = new List<List<vec2>>();

    private List<CTrk> gTemp = new List<CTrk>();

    private int passes, startPass;

    // [XPLAT] WinForms System.Windows.Forms.Timer (Interval 500, Enabled at design time) -> DispatcherTimer
    // started in OnOpened and stopped in OnClosing.
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia compiled-XAML loader and the design-time previewer
    /// only. It renders the static markup, wires no behaviour and leaves the injected collaborators null, so
    /// the <see cref="OnOpened"/>/<see cref="OnClosing"/> guards treat a preview instance as inert. The
    /// running application always uses the injected overload below.
    /// </summary>
    public FormTramLineView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the tram-line dialog bound to the supplied collaborators. Mirrors the WinForms
    /// <c>FormTramLine(Form callingForm)</c> constructor, which set <c>mf = callingForm as FormGPS</c>,
    /// then called <c>InitializeComponent()</c> and <c>mf.CalculateMinMax()</c>.
    /// </summary>
    /// <param name="tram">The tram model (was <c>mf.tram</c>).</param>
    /// <param name="trk">The track model exposing <c>gArr</c> (was <c>mf.trk</c>; runtime type <c>CTrack</c>).</param>
    /// <param name="tool">The implement geometry (was <c>mf.tool</c>).</param>
    /// <param name="bnd">The boundary model (was <c>mf.bnd</c>).</param>
    /// <param name="ABLine">The AB-line model supplying <c>abLength</c> (was <c>mf.ABLine</c>).</param>
    /// <param name="vehicle">The vehicle model supplying <c>VehicleConfig.TrackWidth</c> (was <c>mf.vehicle</c>).</param>
    /// <param name="viewport">The cross-platform GL viewport adapter (was the WinForms <c>GeoViewport</c>).</param>
    /// <param name="getFieldBoundingBox">Field extents accessor (was <c>mf.FieldBoundingBox</c>).</param>
    /// <param name="getMaxFieldDistance">AB extension half-length accessor (was <c>mf.maxFieldDistance</c>).</param>
    /// <param name="getM2FtOrM">Metric/imperial multiplier accessor (was <c>mf.m2FtOrM</c>).</param>
    /// <param name="getUnitsFtM">Unit-suffix accessor (was <c>mf.unitsFtM</c>).</param>
    /// <param name="calculateMinMax">Field-extents recompute (was <c>mf.CalculateMinMax()</c>).</param>
    /// <param name="fileSaveTram">Tram persistence (was <c>mf.FileSaveTram()</c>).</param>
    /// <param name="panelUpdateRightAndBottom">Main-panel refresh (was <c>mf.PanelUpdateRightAndBottom()</c>).</param>
    /// <param name="fixTramModeButton">Toolbar refresh (was <c>mf.FixTramModeButton()</c>).</param>
    /// <param name="showYesMessage">Yes/OK advisory dialog (was <c>mf.YesMessageBox(string)</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown when any collaborator is null (fail fast on missing wiring).</exception>
    public FormTramLineView(
        CTram tram,
        CTrack trk,
        CTool tool,
        CBoundary bnd,
        CABLine ABLine,
        CVehicle vehicle,
        AvaloniaGeoViewport viewport,
        Func<GeoBoundingBox> getFieldBoundingBox,
        Func<double> getMaxFieldDistance,
        Func<double> getM2FtOrM,
        Func<string> getUnitsFtM,
        Action calculateMinMax,
        Action fileSaveTram,
        Action panelUpdateRightAndBottom,
        Action fixTramModeButton,
        Func<string, Task> showYesMessage)
        : this()
    {
        // [XPLAT] The DI seam replaces "mf = callingForm as FormGPS;" — fail fast on a missing wiring rather
        // than NRE deep inside a handler. glm is a static class in this project, so it is NOT injected.
        this.tram = tram ?? throw new ArgumentNullException(nameof(tram));
        this.trk = trk ?? throw new ArgumentNullException(nameof(trk));
        this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
        this.bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        this.ABLine = ABLine ?? throw new ArgumentNullException(nameof(ABLine));
        this.vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        this.getFieldBoundingBox = getFieldBoundingBox ?? throw new ArgumentNullException(nameof(getFieldBoundingBox));
        this.getMaxFieldDistance = getMaxFieldDistance ?? throw new ArgumentNullException(nameof(getMaxFieldDistance));
        this.getM2FtOrM = getM2FtOrM ?? throw new ArgumentNullException(nameof(getM2FtOrM));
        this.getUnitsFtM = getUnitsFtM ?? throw new ArgumentNullException(nameof(getUnitsFtM));
        this.calculateMinMax = calculateMinMax ?? throw new ArgumentNullException(nameof(calculateMinMax));
        this.fileSaveTram = fileSaveTram ?? throw new ArgumentNullException(nameof(fileSaveTram));
        this.panelUpdateRightAndBottom = panelUpdateRightAndBottom ?? throw new ArgumentNullException(nameof(panelUpdateRightAndBottom));
        this.fixTramModeButton = fixTramModeButton ?? throw new ArgumentNullException(nameof(fixTramModeButton));
        this.showYesMessage = showYesMessage ?? throw new ArgumentNullException(nameof(showYesMessage));

        // [XPLAT] Source ctor: mf.CalculateMinMax(). Recompute field extents, then (re)seat the viewport on
        // the now-current FieldBoundingBox (the WinForms code read mf.FieldBoundingBox lazily in oglSelf_Load,
        // i.e. AFTER CalculateMinMax — preserved here by setting the bounding box after the recompute).
        calculateMinMax();
        _viewport.SetBoundingBox(getFieldBoundingBox());

        // [XPLAT] The former oglSelf_Paint body is supplied to the host as its render callback. The host
        // (AvaloniaGeoViewport) invokes RenderAction BETWEEN its own BeginPaint()/EndPaint() — so RenderSelf
        // must NOT call BeginPaint/EndPaint itself (doing so would double-apply the projection). This is the
        // established pattern of the sibling GL dialog FormBndToolView; the source's explicit
        // _viewport.BeginPaint()/EndPaint() wrap is now OWNED BY THE HOST.
        _viewport.RenderAction = RenderSelf;

        // [XPLAT] Host the viewport's exposed Control in the XAML 'ViewportHost' Border (was the oglSelf
        // OpenTK.GLControl). The AvaloniaGeoViewport self-manages its GL context and auto-resizes the GL
        // surface inside its render callback, so there is no CreateViewport()/oglSelf_Resize() equivalent.
        ViewportHost.Child = _viewport.View;

        // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown was
        // wired in the designer; no Move/Up/Wheel). Carries the three-click cut tool.
        _viewport.View.PointerPressed += oglSelf_MouseDown;

        // [XPLAT] WinForms Timer (Interval 500) -> DispatcherTimer started on open, stopped on close.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += timer1_Tick;

        // Wire the panel controls imperatively (the .axaml declares no Click=/event attributes). Done in the
        // injected ctor only, so a loader/previewer instance stays inert. Names + handlers mirror the source
        // designer wiring (btnSave -> btnExit_Click save/close; btnCancel -> btnCancel_Click cancel/close).
        btnDnAlpha.Click += btnDnAlpha_Click;
        btnUpAlpha.Click += btnUpAlpha_Click;
        btnSwapAB.Click += btnSwapAB_Click;
        btnDeleteAllTrams.Click += btnDeleteAllTrams_Click;
        btnResize.Click += btnResize_Click;
        btnCancelTouch.Click += btnCancelTouch_Click;
        cboxIsOuter.Click += cboxIsOuter_Click;
        btnSelectCurveBk.Click += btnSelectCurveBk_Click;
        btnSelectCurve.Click += btnSelectCurve_Click;
        btnDnStartTram.Click += btnDnStartTram_Click;
        btnUpStartTram.Click += btnUpStartTram_Click;
        btnDnTrams.Click += btnDnTrams_Click;
        btnUpTrams.Click += btnUpTrams_Click;
        btnAddLines.Click += btnAddLines_Click;
        btnCancel.Click += btnCancel_Click;
        btnSave.Click += btnExit_Click;
    }

    // ===================================================================================================
    //  Lifecycle — OnOpened/OnClosing reproduce FormTramLine_Load / FormTramLine_FormClosing 1:1.
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Ports <c>FormTramLine_Load</c>. The WinForms order was: set the title + alpha label, compute
    /// <c>tool.halfWidth</c>, build the title (FixLabelsCurve), restore the persisted window size, centre on
    /// the working area, run <c>FormTramLine_ResizeEnd</c> (the square-viewport aspect rule), fall back to the
    /// origin if off-screen, reset the start/pass labels, then load and build the lines. The 500 ms repaint
    /// timer (enabled at design time in the WinForms designer) is started here. Guarded so a design-time
    /// previewer (no injected collaborators) stays inert.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] A loader/previewer instance has no collaborators wired — do nothing.
        if (tram == null) return;

        //translate all the controls
        Title = gStr.gsAdvancedTramLines;                                                          // [XPLAT] was Text = ...
        lblAplha.Text = ((int)(tram.alpha * 100)).ToString(CultureInfo.InvariantCulture);

        tool.halfWidth = (tool.width - tool.overlap) / 2.0;

        FixLabelsCurve();

        // Window Properties — restore the persisted size. [XPLAT] setWindow_tramLineSize is the frozen
        // System.Drawing.Size settings key (schema unchanged, AAP §0.5); map it to Avalonia Width/Height.
        System.Drawing.Size savedSize = AgOpenGPS.Properties.Settings.Default.setWindow_tramLineSize;
        Width = savedSize.Width;
        Height = savedSize.Height;

        // [XPLAT] WinForms centred on Screen.FromControl(this).WorkingArea then called FormTramLine_ResizeEnd;
        // ApplyResizeAspect reproduces the Width = Height + 300 rule AND re-centres, and ClampToScreen
        // reproduces the ScreenHelper.IsOnScreen(Bounds) -> origin fallback.
        ApplyResizeAspect();
        ClampToScreen();

        // [XPLAT] WinForms Timer (Interval 500, Enabled) -> start the DispatcherTimer now.
        _timer.Start();

        ResetStartNumLabels();

        // [XPLAT] LoadAndFixLines awaits the cross-platform yes/OK dialog before its no-lines Close(); run it
        // after the synchronous setup, fire-and-forget on the UI thread (parity with the WinForms code, which
        // showed a modal box then Close()d). Any exception is logged-and-swallowed so OnOpened never faults.
        _ = LoadAndFixLinesAsync();
    }

    /// <summary>
    /// [XPLAT] Ports <c>FormTramLine_FormClosing</c>. On cancel the in-progress tram model is wiped (and the
    /// display mode reset); otherwise the committed tram lists are point-reduced, persisted via the injected
    /// <c>fileSaveTram</c>, and the main GPS panels/toolbar refreshed. The window size and tram alpha are
    /// written back to the frozen settings keys. The repaint timer and pointer subscription are torn down.
    /// Guarded for the design-time previewer.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (tram == null)
        {
            base.OnClosing(e);
            return;
        }

        if (isCancel)
        {
            tram.tramArr?.Clear();
            tram.tramList?.Clear();
            tram.tramBndOuterArr?.Clear();
            tram.tramBndInnerArr?.Clear();

            tram.displayMode = 0;
        }

        for (int i = 0; i < tram.tramList.Count; i++)
        {
            tram.tramList[i].ReducePointsByAngle(0.01, 200);
        }

        fileSaveTram();                  // [XPLAT] was mf.FileSaveTram()
        panelUpdateRightAndBottom();     // [XPLAT] was mf.PanelUpdateRightAndBottom()
        fixTramModeButton();             // [XPLAT] was mf.FixTramModeButton()

        // [XPLAT] frozen settings round-trip (schema unchanged): System.Drawing.Size + double alpha.
        AgOpenGPS.Properties.Settings.Default.setWindow_tramLineSize = new System.Drawing.Size((int)Width, (int)Height);
        AgOpenGPS.Properties.Settings.Default.setTram_alpha = tram.alpha;
        AgOpenGPS.Properties.ToolSettings.Default.Save();
        AgOpenGPS.Properties.Settings.Default.Save();

        // [XPLAT] tear down the repaint timer + pointer subscription (no WinForms designer Dispose here).
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= timer1_Tick;
        }
        if (_viewport?.View != null)
        {
            _viewport.View.PointerPressed -= oglSelf_MouseDown;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// [XPLAT] Async wrapper around <see cref="LoadAndFixLines"/> for the fire-and-forget call in
    /// <see cref="OnOpened"/>: it isolates the await of the yes/OK dialog so an exception cannot escape onto
    /// the UI message loop.
    /// </summary>
    private async Task LoadAndFixLinesAsync()
    {
        try
        {
            await LoadAndFixLines();
        }
        catch (Exception)
        {
            // The dialog/await path failed; the dialog itself logs. Never fault the open sequence.
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>LoadAndFixLines</c>. Clones every VISIBLE AB/Curve track from <c>trk.gArr</c> into the
    /// working <c>gTemp</c> list (AB tracks start drawn on the default side: <c>isVisible = false</c>; curves
    /// drawn on the curve side: <c>isVisible = true</c>). With no guidance lines it shows the cross-platform
    /// advisory (was <c>mf.YesMessageBox</c>), cancels and closes. Otherwise it pre-builds each track once,
    /// dropping any that resolve to an empty tram (and flipping that track's side for next time), then selects
    /// the first track and rebuilds.
    /// </summary>
    private async Task LoadAndFixLines()
    {
        //load the lines from Trks
        gTemp?.Clear();

        foreach (var item in trk.gArr)
        {
            if ((item.mode == TrackMode.AB || item.mode == TrackMode.Curve) && item.isVisible)
            {
                //default side assuming built in AB Draw - isVisible is used for side to draw
                gTemp.Add(new CTrk(item));
                if (item.mode == TrackMode.AB)
                    gTemp[gTemp.Count - 1].isVisible = false;
                else
                    gTemp[gTemp.Count - 1].isVisible = true;
            }
        }

        if (gTemp == null || gTemp.Count == 0)
        {
            // [XPLAT] was mf.YesMessageBox(...) — awaited so the operator sees it before the window closes.
            await showYesMessage(gStr.gsNoGuidanceLines + "\r\n\r\n  Exiting");
            isCancel = true;
            Close();
            // [XPLAT] return here: the WinForms code fell through to the for-loop AND a trailing BuildTram();
            // on WinForms Close() is deferred so an empty gTemp made the loop a no-op, but the trailing
            // BuildTram() would index gTemp[0]. Returning preserves the user-visible "no lines -> close"
            // behaviour while avoiding the empty-list dereference on .NET (AAP §0.6.5 robustness).
            return;
        }
        else
        {
            indx = 0;
        }

        for (indx = 0; indx < gTemp.Count; indx++)
        {
            BuildTram();
            if (tramList[0].Count == 0)
            {
                gTemp[indx].isVisible = !gTemp[indx].isVisible;
                tramList.Clear();
                tramArr.Clear();
            }
        }

        indx = 0;
        FixLabelsCurve();
        BuildTram();
    }

    /// <summary>
    /// [XPLAT] Ports <c>ResetStartNumLabels</c>. The start pass is 1 when an outer-boundary tram is in play
    /// (toggle checked or an outer-boundary array already exists), otherwise 0; the pass count defaults to 2.
    /// </summary>
    private void ResetStartNumLabels()
    {
        if (cboxIsOuter.IsChecked == true)        // [XPLAT] WinForms cboxIsOuter.Checked -> ToggleButton.IsChecked (bool?)
        {
            startPass = 1;
        }
        else
        {
            startPass = 0;
        }

        if (tram.tramBndOuterArr.Count > 0)
        {
            startPass = 1;
        }

        passes = 2;
        lblStartPass.Text = gStr.gsStart + ":\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Ports the geometry of <c>FormTramLine_ResizeEnd</c> that still applies on Avalonia: the window
    /// keeps a square GL region on the left by forcing <c>Width = Height + 300</c> (the 300 px command panel
    /// sits in the fixed second grid column). The WinForms code additionally sized the GL control and called
    /// <c>_viewport.Resize</c>; on Avalonia the Grid lays the panel out and <see cref="AvaloniaGeoViewport"/>
    /// resizes its own GL surface inside its render callback (HandleOpenGlRender calls the inherited Resize
    /// when the framebuffer size changes), so the viewport is deliberately NOT resized from the UI thread —
    /// there is no current GL context here. The window is then re-centred, mirroring the source.
    /// </summary>
    private void ApplyResizeAspect()
    {
        Width = Height + 300;
        CenterOnCurrentScreen();
    }

    /// <summary>
    /// [XPLAT] Reproduces the working-area centring the WinForms <c>FormTramLine_ResizeEnd</c> performed via
    /// <c>Screen.FromControl(this)</c>. Uses Avalonia's <see cref="Window.Screens"/>: picks the screen the
    /// window currently sits on (else the primary), and centres within its working area, converting the
    /// logical Width/Height to physical pixels via the screen scaling. Best-effort — does nothing when screen
    /// information is unavailable (e.g. a headless host).
    /// </summary>
    private void CenterOnCurrentScreen()
    {
        var screens = Screens;
        if (screens == null) return;

        var screen = screens.Primary;
        foreach (var sc in screens.All)
        {
            if (sc.Bounds.Contains(Position))
            {
                screen = sc;
                break;
            }
        }
        if (screen == null) return;

        PixelRect area = screen.WorkingArea;
        double scaling = screen.Scaling;
        int physW = (int)(Width * scaling);
        int physH = (int)(Height * scaling);
        int left = area.X + ((area.Width - physW) / 2);
        int top = area.Y + ((area.Height - physH) / 2);
        Position = new PixelPoint(left, top);
    }

    /// <summary>
    /// [XPLAT] Ports the <c>ScreenHelper.IsOnScreen(Bounds)</c> fallback from <c>FormTramLine_Load</c>: if the
    /// window's position lands on no connected screen, move it to the origin. Idiom shared with the sibling
    /// Guidance views.
    /// </summary>
    private void ClampToScreen()
    {
        var screens = Screens;
        if (screens == null) return;

        PixelPoint pos = Position;
        foreach (var sc in screens.All)
        {
            if (sc.Bounds.Contains(pos))
            {
                return;
            }
        }

        Position = new PixelPoint(0, 0);
    }

    // ===================================================================================================
    //  Building lines — BEHAVIOR FROZEN (AAP §0.2.2). Ported VERBATIM from FormTramLine.cs; the only edits
    //  are the mf.* -> injected-collaborator renames (mf.tram->tram, mf.bnd->bnd, mf.maxFieldDistance->
    //  getMaxFieldDistance()). glm is a static class, so glm.* calls are unchanged.
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Ports <c>btnAddLines_Click</c>: commits every freshly built local tram polyline into the tram
    /// model (a fresh capacity-32 <c>tram.tramArr</c> per polyline appended to <c>tram.tramList</c>), then
    /// clears the local working lists.
    /// </summary>
    private void btnAddLines_Click(object sender, RoutedEventArgs e)
    {
        if (tramList.Count > 0)
        {
            for (int i = 0; i < tramList.Count; i++)
            {
                tram.tramArr = new List<vec2>
                {
                    Capacity = 32
                };

                tram.tramList.Add(tram.tramArr);

                for (int j = 0; j < tramList[i].Count; j++)
                {
                    vec2 tr = new vec2(tramList[i][j]);
                    tram.tramArr.Add(tr);
                }
            }
        }

        tramList?.Clear();
        tramArr?.Clear();
    }

    /// <summary>
    /// [XPLAT] Ports <c>BuildTram</c>: dispatches to the curve or AB builder for the selected track. The
    /// invalid-line branch (unreachable in normal flow — <c>gTemp</c> only holds AB/Curve tracks) raises the
    /// cross-platform error dialog (was <c>FormDialog.Show(... DialogSeverity.Error)</c>).
    /// </summary>
    private void BuildTram()
    {
        if (gTemp[indx].mode == TrackMode.Curve)
        {
            //if (Dist != 0)
            //mf.trk.NudgeRefCurve(Dist);
            BuildCurveTram();
        }
        else if (gTemp[indx].mode == TrackMode.AB)
        {
            //if (Dist != 0)
            //mf.trk.NudgeRefABLine(Dist);
            BuildABTram();
        }
        else
        {
            // [XPLAT] was FormDialog.Show("Invalid Line", "Use AB LIne or Curve Only", DialogSeverity.Error).
            // BuildTram is synchronous (invoked from the build loop and many button handlers), so the awaitable
            // FormDialogView.ShowAsync is fire-and-forgotten; the source strings are preserved verbatim.
            _ = FormDialogView.ShowAsync("Invalid Line", "Use AB LIne or Curve Only", DialogSeverity.Error, this);
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>BuildCurveTram</c> VERBATIM (behavior frozen). Offsets the selected curve's points to
    /// both sides for each pass (inner: half-tram − half-wheel-track; outer: half-tram + half-wheel-track),
    /// rejecting points closer than <c>widd</c> to the reference, thinning to ~1.2 m spacing, and clipping to
    /// the field boundary (<c>bnd.bndList[0].fenceLineEar</c>).
    /// </summary>
    private void BuildCurveTram()
    {
        tramList?.Clear();
        tramArr?.Clear();

        int refCount = gTemp[indx].curvePts.Count;

        int cntr = startPass;

        double widd;

        //draw on correct side
        double sideHeading = 0;
        if (gTemp[indx].isVisible) sideHeading = Math.PI;

        for (int i = cntr; i <= (passes + startPass) - 1; i++)
        {
            tramArr = new List<vec2>
            {
                Capacity = 128
            };

            tramList.Add(tramArr);

            widd = (tram.tramWidth * 0.5) - tram.halfWheelTrack;
            widd += (tram.tramWidth * i);

            double distSqAway = widd * widd * 0.999999;

            for (int j = 0; j < refCount; j += 1)
            {
                vec2 point = new vec2(
                (Math.Sin(glm.PIBy2 + gTemp[indx].curvePts[j].heading + sideHeading) *
                    widd) + gTemp[indx].curvePts[j].easting,
                (Math.Cos(glm.PIBy2 + gTemp[indx].curvePts[j].heading + sideHeading) *
                    widd) + gTemp[indx].curvePts[j].northing
                    );

                bool Add = true;
                for (int t = 0; t < refCount; t++)
                {
                    //distance check to be not too close to ref line
                    double dist = ((point.easting - gTemp[indx].curvePts[t].easting) * (point.easting - gTemp[indx].curvePts[t].easting))
                        + ((point.northing - gTemp[indx].curvePts[t].northing) * (point.northing - gTemp[indx].curvePts[t].northing));
                    if (dist < distSqAway)
                    {
                        Add = false;
                        break;
                    }
                }
                if (Add)
                {
                    //a new point only every 2 meters
                    double dist = tramArr.Count > 0 ? ((point.easting - tramArr[tramArr.Count - 1].easting) * (point.easting - tramArr[tramArr.Count - 1].easting))
                        + ((point.northing - tramArr[tramArr.Count - 1].northing) * (point.northing - tramArr[tramArr.Count - 1].northing)) : 3.0;
                    if (dist > 1.2)
                    {
                        //if inside the boundary, add
                        if (bnd.bndList[0].fenceLineEar.IsPointInPolygon(point))
                        {
                            tramArr.Add(point);
                        }
                    }
                }
            }
        }

        for (int i = cntr; i <= (passes + startPass) - 1; i++)
        {
            tramArr = new List<vec2>
            {
                Capacity = 128
            };

            tramList.Add(tramArr);

            widd = (tram.tramWidth * 0.5) + tram.halfWheelTrack;
            widd += (tram.tramWidth * i);
            double distSqAway = widd * widd * 0.999999;

            for (int j = 0; j < refCount; j += 1)
            {
                vec2 point = new vec2(
                Math.Sin(glm.PIBy2 + gTemp[indx].curvePts[j].heading + sideHeading) *
                    widd + gTemp[indx].curvePts[j].easting,
                Math.Cos(glm.PIBy2 + gTemp[indx].curvePts[j].heading + sideHeading) *
                    widd + gTemp[indx].curvePts[j].northing
                    );

                bool Add = true;
                for (int t = 0; t < refCount; t++)
                {
                    //distance check to be not too close to ref line
                    double dist = ((point.easting - gTemp[indx].curvePts[t].easting) * (point.easting - gTemp[indx].curvePts[t].easting))
                        + ((point.northing - gTemp[indx].curvePts[t].northing) * (point.northing - gTemp[indx].curvePts[t].northing));
                    if (dist < distSqAway)
                    {
                        Add = false;
                        break;
                    }
                }
                if (Add)
                {
                    //a new point only every 2 meters
                    double dist = tramArr.Count > 0 ? ((point.easting - tramArr[tramArr.Count - 1].easting) * (point.easting - tramArr[tramArr.Count - 1].easting))
                        + ((point.northing - tramArr[tramArr.Count - 1].northing) * (point.northing - tramArr[tramArr.Count - 1].northing)) : 3.0;
                    if (dist > 1.2)
                    {
                        //if inside the boundary, add
                        if (bnd.bndList[0].fenceLineEar.IsPointInPolygon(point))
                        {
                            tramArr.Add(point);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>BuildABTram</c> VERBATIM (behavior frozen). Extends the AB segment by
    /// <c>getMaxFieldDistance()</c> each way (was <c>mf.maxFieldDistance</c>), samples it every 2 m, then for
    /// each pass offsets the sampled reference to both sides and clips to the field boundary.
    /// </summary>
    private void BuildABTram()
    {
        // [XPLAT] was mf.maxFieldDistance (a field, read twice for endPtA/endPtB); captured once here. The
        // accessor is deterministic, so a single read is behaviourally identical to the two source reads.
        double maxFieldDistance = getMaxFieldDistance();

        List<vec2> tramRef = new List<vec2>();

        double abHeading = gTemp[indx].heading;

        double hsin = Math.Sin(abHeading);
        double hcos = Math.Cos(abHeading);

        gTemp[indx].endPtA.easting = gTemp[indx].ptA.easting - (Math.Sin(abHeading) * maxFieldDistance);
        gTemp[indx].endPtA.northing = gTemp[indx].ptA.northing - (Math.Cos(abHeading) * maxFieldDistance);

        gTemp[indx].endPtB.easting = gTemp[indx].ptB.easting + (Math.Sin(abHeading) * maxFieldDistance);
        gTemp[indx].endPtB.northing = gTemp[indx].ptB.northing + (Math.Cos(abHeading) * maxFieldDistance);

        double len = glm.Distance(gTemp[indx].endPtA, gTemp[indx].endPtB);
        //divide up the AB line into segments
        vec2 P1 = new vec2();
        for (int i = 0; i < (int)len; i += 2)
        {
            P1.easting = (hsin * i) + gTemp[indx].endPtA.easting;
            P1.northing = (hcos * i) + gTemp[indx].endPtA.northing;
            tramRef.Add(P1);
        }

        //create list of list of points of triangle strip of AB Highlight
        double headingCalc = abHeading + glm.PIBy2;

        if (headingCalc < 0) headingCalc += glm.twoPI;
        if (headingCalc > glm.twoPI) headingCalc -= glm.twoPI;

        if (gTemp[indx].isVisible) headingCalc += Math.PI;
        if (headingCalc > glm.twoPI) headingCalc -= glm.twoPI;

        hsin = Math.Sin(headingCalc);
        hcos = Math.Cos(headingCalc);

        tramList?.Clear();
        tramArr?.Clear();

        //no boundary starts on first pass
        int cntr = startPass;

        double widd;
        for (int i = cntr; i < passes + startPass; i++)
        {
            tramArr = new List<vec2>
            {
                Capacity = 128
            };

            tramList.Add(tramArr);

            widd = (tram.tramWidth * 0.5) - tram.halfWheelTrack;
            widd += (tram.tramWidth * i);

            for (int j = 0; j < tramRef.Count; j++)
            {
                P1.easting = hsin * widd + tramRef[j].easting;
                P1.northing = (hcos * widd) + tramRef[j].northing;

                if (bnd.bndList[0].fenceLineEar.IsPointInPolygon(P1))
                {
                    tramArr.Add(P1);
                }
            }
        }

        for (int i = cntr; i < passes + startPass; i++)
        {
            tramArr = new List<vec2>
            {
                Capacity = 128
            };

            tramList.Add(tramArr);

            widd = (tram.tramWidth * 0.5) + tram.halfWheelTrack;
            widd += (tram.tramWidth * i);

            for (int j = 0; j < tramRef.Count; j++)
            {
                P1.easting = (hsin * widd) + tramRef[j].easting;
                P1.northing = (hcos * widd) + tramRef[j].northing;

                if (bnd.bndList[0].fenceLineEar.IsPointInPolygon(P1))
                {
                    tramArr.Add(P1);
                }
            }
        }

        tramRef?.Clear();
        //outside tram

        if (bnd.bndList.Count == 0 || passes != 0)
        {
            //return;
        }
    }

    // ===================================================================================================
    //  OpenGL and Drawing — immediate-mode GL ported VERBATIM (behavior frozen, AAP §0.2.2).
    //  [XPLAT] RISK: GL.Begin/Vertex3/Color3/Color4/PointSize/LineWidth/LineStipple require a desktop-GL
    //  compatibility context; they will not run under GLES/ANGLE. See PARITY_REPORT.md (AAP §0.6.2).
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Ports <c>oglSelf_MouseDown</c> — the three-click cutting tool. Click 1 sets <c>ptA</c>, click 2
    /// sets <c>ptB</c> (drawing the cut line), click 3 sets the "keep side" reference <c>ptCut</c>: every tram
    /// polyline that the A→B segment intersects is trimmed of the points lying on the cut side, then the tool
    /// resets. The WinForms <c>oglSelf.PointToClient(Cursor.Position)</c> is replaced by the Avalonia pointer
    /// position relative to the GL host; the inherited <c>GetGeoCoord</c> (PATTERN A — clean adapter) maps it
    /// to field coordinates exactly as the WinForms <c>GeoViewport</c> did.
    /// </summary>
    private void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
    {
        step++;

        // [XPLAT] was: Point ptt = oglSelf.PointToClient(Cursor.Position). Avalonia gives the pointer position
        // relative to the GL host control; cast to int to match the WinForms integer client coordinates.
        var ptt = e.GetPosition(_viewport.View);
        XyCoord xyClient = new XyCoord((int)ptt.X, (int)ptt.Y);
        GeoCoord mouseDownCoord = _viewport.GetGeoCoord(xyClient);

        if (step == 1)
        {
            ptA = new vec2(mouseDownCoord);
        }
        else if (step == 2)
        {
            ptB = new vec2(mouseDownCoord);
        }
        else
        {
            ptCut = new vec2(mouseDownCoord);

            bool isLeft = (ptB.easting - ptA.easting) * (ptCut.northing - ptA.northing)
                > (ptB.northing - ptA.northing) * (ptCut.easting - ptA.easting);

            bool isIntersect = false;

            if (tramList.Count > 0)
            {
                for (int i = 0; i < tramList.Count; i++)
                {
                    ////check for line intersection
                    for (int j = 0; j < tramList[i].Count - 1; j++)
                    {
                        if (GetLineIntersection(
                            tramList[i][j].easting, tramList[i][j].northing,
                            tramList[i][j + 1].easting, tramList[i][j + 1].northing,
                            ptA.easting, ptA.northing, ptB.easting, ptB.northing))
                        {
                            isIntersect = true;
                            break;
                        }
                    }

                    if (isIntersect)
                    {
                        for (int h = 0; h < tramList[i].Count; h++)
                        {
                            if (isLeft)
                            {
                                if ((ptB.easting - ptA.easting) * (tramList[i][h].northing - ptA.northing)
                                    > (ptB.northing - ptA.northing) * (tramList[i][h].easting - ptA.easting))
                                {
                                    tramList[i].RemoveAt(h);
                                    h = -1;
                                }
                            }
                            else
                            {
                                if ((ptB.easting - ptA.easting) * (tramList[i][h].northing - ptA.northing)
                                    < (ptB.northing - ptA.northing) * (tramList[i][h].easting - ptA.easting))
                                {
                                    tramList[i].RemoveAt(h);
                                    h = -1;
                                }
                            }
                        }
                    }
                    isIntersect = false;
                }
            }

            ptB.easting = 9999999;
            ptB.northing = 9999999;
            ptA.easting = 9999999;
            ptA.northing = 9999999;
            step = 0;
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>GetLineIntersection</c> VERBATIM: standard segment-segment intersection on the
    /// parameters <c>s</c>,<c>t</c> ∈ [0,1] (p0→p1 against p2→p3).
    /// </summary>
    private bool GetLineIntersection(double p0x, double p0y, double p1x, double p1y,
    double p2x, double p2y, double p3x, double p3y)
    {
        double s1x, s1y, s2x, s2y;
        s1x = p1x - p0x;
        s1y = p1y - p0y;

        s2x = p3x - p2x;
        s2y = p3y - p2y;

        double s, t;
        s = (-s1y * (p0x - p2x) + s1x * (p0y - p2y)) / (-s2x * s1y + s1x * s2y);

        if (s >= 0 && s <= 1)
        {
            //check oher side
            t = (s2x * (p0y - p2y) - s2y * (p0x - p2x)) / (-s2x * s1y + s1x * s2y);
            if (t >= 0 && t <= 1)
            {
                // Collision detected
                return true;
            }
        }

        return false; // No collision
    }

    /// <summary>
    /// [XPLAT] Ports the BODY of <c>oglSelf_Paint</c> as the viewport's render callback (assigned to
    /// <c>_viewport.RenderAction</c>). CRITICAL: the host (<see cref="AvaloniaGeoViewport"/>
    /// HandleOpenGlRender) ALREADY calls <c>BeginPaint()</c> before invoking this and <c>EndPaint()</c> after
    /// it returns, so — unlike the WinForms original which wrapped its body in <c>_viewport.BeginPaint()</c>/
    /// <c>EndPaint()</c> — this method must NOT call them (a second wrap would double-apply the camera and
    /// corrupt the projection). Same contract as the sibling GL dialog FormBndToolView. The immediate-mode GL
    /// below is behavior-frozen and carries the GLES/ANGLE risk noted at the top of this region.
    /// </summary>
    private void RenderSelf()
    {
        for (int j = 0; j < bnd.bndList.Count; j++)
        {
            GeoCoord[] fenceLineEar = GeoRefactorHelper.ToGeoCoordArray(bnd.bndList[j].fenceLineEar);
            bool isSelected = j == 0;
            FenceLineVisual.DrawFenceLine(fenceLineEar, isSelected);
        }
        DrawBuiltLines();
        DrawTrams();
        DrawNewTrams();

        GL.PointSize(18);

        GL.Begin(PrimitiveType.Points);
        GL.Color3(1.0, 0, 0);
        GL.Vertex3(ptA.easting, ptA.northing, 0);
        GL.End();

        GL.Begin(PrimitiveType.Points);
        GL.Color3(0, 1.0, 0);
        GL.Vertex3(ptB.easting, ptB.northing, 0);
        GL.End();

        if (step == 2)
        {
            GL.LineWidth(6);

            GL.Begin(PrimitiveType.Lines);
            GL.Color3(1.0, 0, 0);
            GL.Vertex3(ptA.easting, ptA.northing, 0);
            GL.Color3(0, 1.0, 0);
            GL.Vertex3(ptB.easting, ptB.northing, 0);

            GL.End();
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>DrawTrams</c> VERBATIM — the committed tram lines (semi-transparent, alpha-tinted) and,
    /// when present, the outer/inner boundary tram loops.
    /// </summary>
    private void DrawTrams()
    {
        GL.LineWidth(6);

        GL.Color4(0.730f, 0.52f, 0.63530f, tram.alpha);

        if (tram.tramList.Count > 0)
        {
            for (int i = 0; i < tram.tramList.Count; i++)
            {
                GL.Begin(PrimitiveType.LineStrip);
                for (int h = 0; h < tram.tramList[i].Count; h++)
                    GL.Vertex3(tram.tramList[i][h].easting, tram.tramList[i][h].northing, 0);
                GL.End();
            }
        }

        if (tram.tramBndOuterArr.Count > 0)
        {
            GL.Color4(0.830f, 0.72f, 0.3530f, tram.alpha);

            GL.Begin(PrimitiveType.LineLoop);
            for (int h = 0; h < tram.tramBndOuterArr.Count; h++) GL.Vertex3(tram.tramBndOuterArr[h].easting, tram.tramBndOuterArr[h].northing, 0);
            GL.End();
            GL.Begin(PrimitiveType.LineLoop);
            for (int h = 0; h < tram.tramBndInnerArr.Count; h++) GL.Vertex3(tram.tramBndInnerArr[h].easting, tram.tramBndInnerArr[h].northing, 0);
            GL.End();
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>DrawNewTrams</c> VERBATIM — the freshly built (not-yet-committed) local tram polylines
    /// drawn as thin near-white line strips.
    /// </summary>
    private void DrawNewTrams()
    {
        GL.LineWidth(2);

        GL.Color4(0.97530f, 0.972f, 0.973530f, 1.0);

        if (tramList.Count > 0)
        {
            for (int i = 0; i < tramList.Count; i++)
            {
                GL.Begin(PrimitiveType.LineStrip);
                for (int h = 0; h < tramList[i].Count; h++)
                    GL.Vertex3(tramList[i][h].easting, tramList[i][h].northing, 0);
                GL.End();
            }
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>DrawBuiltLines</c> VERBATIM — the source guidance tracks in <c>gTemp</c>: AB lines
    /// (red, stippled; selected = solid + thicker, extended by <c>ABLine.abLength</c>) and curves (green;
    /// boundary-curves olive; endpoint points orange/blue, selected = larger). <c>mf.ABLine</c> -> injected
    /// <c>ABLine</c>.
    /// </summary>
    private void DrawBuiltLines()
    {
        GL.LineStipple(1, 0x0707);
        for (int i = 0; i < gTemp.Count; i++)
        {
            //AB Lines
            if (gTemp[i].mode == TrackMode.AB)
            {
                GL.Enable(EnableCap.LineStipple);
                GL.LineWidth(4);

                if (i == indx)
                {
                    GL.LineWidth(8);
                    GL.Disable(EnableCap.LineStipple);
                }

                GL.Color3(1.0f, 0.20f, 0.20f);

                GL.Begin(PrimitiveType.Lines);

                GL.Vertex3(gTemp[i].ptA.easting - (Math.Sin(gTemp[i].heading) * ABLine.abLength), gTemp[i].ptA.northing - (Math.Cos(gTemp[i].heading) * ABLine.abLength), 0);
                GL.Vertex3(gTemp[i].ptB.easting + (Math.Sin(gTemp[i].heading) * ABLine.abLength), gTemp[i].ptB.northing + (Math.Cos(gTemp[i].heading) * ABLine.abLength), 0);

                GL.End();

                GL.Disable(EnableCap.LineStipple);
            }

            else if (gTemp[i].mode == TrackMode.Curve || gTemp[i].mode == TrackMode.bndCurve)
            {
                GL.Enable(EnableCap.LineStipple);
                GL.LineWidth(5);

                if (gTemp[i].mode == TrackMode.bndCurve) GL.LineStipple(1, 0x0007);
                else GL.LineStipple(1, 0x0707);


                if (i == indx)
                {
                    GL.LineWidth(8);
                    GL.Disable(EnableCap.LineStipple);
                }

                GL.Color3(0.30f, 0.97f, 0.30f);
                if (gTemp[i].mode == TrackMode.bndCurve) GL.Color3(0.70f, 0.5f, 0.2f);
                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 pts in gTemp[i].curvePts)
                {
                    GL.Vertex3(pts.easting, pts.northing, 0);
                }
                GL.End();

                GL.Disable(EnableCap.LineStipple);

                if (i == indx) GL.PointSize(16);
                else GL.PointSize(8);

                GL.Color3(1.0f, 0.75f, 0.350f);
                GL.Begin(PrimitiveType.Points);

                GL.Vertex3(gTemp[i].curvePts[0].easting,
                            gTemp[i].curvePts[0].northing,
                            0);


                GL.Color3(0.5f, 0.5f, 1.0f);
                GL.Vertex3(gTemp[i].curvePts[gTemp[i].curvePts.Count - 1].easting,
                            gTemp[i].curvePts[gTemp[i].curvePts.Count - 1].northing,
                            0);
                GL.End();
            }
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>timer1_Tick</c>. The WinForms timer called <c>oglSelf.Refresh()</c> every 500 ms;
    /// <see cref="AvaloniaGeoViewport.RequestRender"/> re-invokes the GL host's render callback (it marshals
    /// to the UI thread internally and only flags a redraw — no GL context is touched here).
    /// </summary>
    private void timer1_Tick(object sender, EventArgs e)
    {
        _viewport.RequestRender();
    }

    // ===================================================================================================
    //  Buttons and misc functions — ported 1:1 from FormTramLine.cs. WinForms (object, EventArgs) handlers
    //  become Avalonia (object, RoutedEventArgs) handlers; every numeric ToString uses InvariantCulture.
    // ===================================================================================================

    /// <summary>[XPLAT] Ports <c>btnSelectCurve_Click</c>: advance to the next track (wrap to 0), reset the
    /// start/pass labels and rebuild.</summary>
    private void btnSelectCurve_Click(object sender, RoutedEventArgs e)
    {
        tramList?.Clear();
        tramArr?.Clear();

        if (gTemp.Count > 0)
        {
            indx++;
            if (indx > (gTemp.Count - 1)) indx = 0;
        }
        else
        {
            indx = -1;
        }

        ResetStartNumLabels();
        FixLabelsCurve();
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();

    }

    /// <summary>[XPLAT] Ports <c>btnSelectCurveBk_Click</c>: step to the previous track (wrap to last). Note
    /// the source does NOT call ResetStartNumLabels here — preserved exactly.</summary>
    private void btnSelectCurveBk_Click(object sender, RoutedEventArgs e)
    {
        tramList?.Clear();
        tramArr?.Clear();

        if (gTemp.Count > 0)
        {
            indx--;
            if (indx < 0) indx = gTemp.Count - 1;
        }
        else
        {
            indx = -1;
        }

        FixLabelsCurve();
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnExit_Click</c> (wired to <c>btnSave</c>): commit (not cancel) and close.</summary>
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        isCancel = false;
        Close();
    }

    /// <summary>[XPLAT] Ports <c>btnCancel_Click</c> (wired to <c>btnCancel</c>): cancel and close (OnClosing
    /// wipes the in-progress tram model).</summary>
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        isCancel = true;
        Close();
    }

    /// <summary>[XPLAT] Ports <c>btnSwapAB_Click</c>: flip the selected track's draw side and rebuild.</summary>
    private void btnSwapAB_Click(object sender, RoutedEventArgs e)
    {
        gTemp[indx].isVisible = !gTemp[indx].isVisible;
        ResetStartNumLabels();
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnDeleteAllTrams_Click</c>: clear the local AND committed tram lists +
    /// boundary trams, reset labels, uncheck the outer toggle, rebuild.</summary>
    private void btnDeleteAllTrams_Click(object sender, RoutedEventArgs e)
    {
        tramList?.Clear();
        tramArr?.Clear();
        tram.tramList?.Clear();
        tram.tramArr?.Clear();

        tram.tramBndOuterArr?.Clear();
        tram.tramBndInnerArr?.Clear();

        ResetStartNumLabels();

        cboxIsOuter.IsChecked = false;      // [XPLAT] WinForms cboxIsOuter.Checked = false
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnCancelTouch_Click</c>: reset the three-click cut points.</summary>
    private void btnCancelTouch_Click(object sender, RoutedEventArgs e)
    {
        ptB.easting = 9999999;
        ptB.northing = 9999999;
        ptA.easting = 9999999;
        ptA.northing = 9999999;
        step = 0;
    }

    /// <summary>
    /// [XPLAT] Ports <c>FixLabelsCurve</c>. Builds the window title from the track / tram / seed widths
    /// (converted to the active units) and updates the selected-track readout. The WinForms form Text becomes
    /// the Avalonia <c>Title</c>, and EVERY numeric format uses <see cref="CultureInfo.InvariantCulture"/>
    /// (AAP §0.6.5) so a comma-decimal locale cannot corrupt the title.
    /// </summary>
    private void FixLabelsCurve()
    {
        Title = gStr.gsTramLines;
        Title += "    Track: " + (vehicle.VehicleConfig.TrackWidth * getM2FtOrM()).ToString("N2", CultureInfo.InvariantCulture) + getUnitsFtM();
        Title += "    Tram: " + (tram.tramWidth * getM2FtOrM()).ToString("N2", CultureInfo.InvariantCulture) + getUnitsFtM();
        Title += "    Seed: " + (tool.width * getM2FtOrM()).ToString("N2", CultureInfo.InvariantCulture) + getUnitsFtM();

        if (indx > -1 && gTemp.Count > 0)
        {
            this.Title += "   " + gTemp[indx].name;
            lblCurveSelected.Text = (indx + 1).ToString(CultureInfo.InvariantCulture) + " / " + gTemp.Count.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            Title += "   Line ***";
            lblCurveSelected.Text = "*";
        }
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnResize_Click</c>: set the window height to the primary screen's working-area height
    /// then re-apply the square-viewport aspect rule. WinForms <c>Screen.PrimaryScreen.WorkingArea.Height</c>
    /// (physical px) -> Avalonia logical Height via the primary screen scaling.
    /// </summary>
    private void btnResize_Click(object sender, RoutedEventArgs e)
    {
        var screens = Screens;
        if (screens != null && screens.Primary != null)
        {
            PixelRect area = screens.Primary.WorkingArea;
            double scaling = screens.Primary.Scaling;
            if (scaling <= 0) scaling = 1.0;
            Height = area.Height / scaling;
        }
        ApplyResizeAspect();
    }

    /// <summary>[XPLAT] Ports <c>btnDnAlpha_Click</c>: decrease tram opacity (floor 0.2) and update the label
    /// (InvariantCulture).</summary>
    private void btnDnAlpha_Click(object sender, RoutedEventArgs e)
    {
        tram.alpha -= 0.1;
        if (tram.alpha < 0.2) tram.alpha = 0.2;
        lblAplha.Text = ((int)(tram.alpha * 100)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>[XPLAT] Ports <c>btnUpAlpha_Click</c>: increase tram opacity (cap 1.0) and update the label
    /// (InvariantCulture).</summary>
    private void btnUpAlpha_Click(object sender, RoutedEventArgs e)
    {
        tram.alpha += 0.1;
        if (tram.alpha > 1.0) tram.alpha = 1.0;
        lblAplha.Text = ((int)(tram.alpha * 100)).ToString(CultureInfo.InvariantCulture);
    }

    // ----- Outer Tram --------------------------------------------------------------------------------

    /// <summary>[XPLAT] Ports <c>cboxIsOuter_Click</c> (wired to the <c>cboxIsOuter</c> ToggleButton): rebuild
    /// the boundary tram when toggled on, reset labels, rebuild.</summary>
    private void cboxIsOuter_Click(object sender, RoutedEventArgs e)
    {
        tram.tramBndOuterArr?.Clear();
        tram.tramBndInnerArr?.Clear();
        if (cboxIsOuter.IsChecked == true)      // [XPLAT] WinForms cboxIsOuter.Checked
        {
            BuildTramBnd();
        }
        ResetStartNumLabels();
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>BuildTramBnd</c>: enable boundary-tram display and (re)create the outer/inner
    /// boundary tracks on the tram model.</summary>
    private void BuildTramBnd()
    {
        tram.displayMode = 1;
        tram.CreateBoundaryOuterTrack();
        tram.CreateBoundaryInnerTrack();
    }

    // ----- Start And Passes Controls -----------------------------------------------------------------

    /// <summary>[XPLAT] Ports <c>btnUpTrams_Click</c>: one more pass, rebuild.</summary>
    private void btnUpTrams_Click(object sender, RoutedEventArgs e)
    {
        passes++;
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnDnTrams_Click</c>: one fewer pass (floor 1), rebuild.</summary>
    private void btnDnTrams_Click(object sender, RoutedEventArgs e)
    {
        passes--;
        if (passes < 1) passes = 1;
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnUpStartTram_Click</c>: increment the start pass, rebuild.</summary>
    private void btnUpStartTram_Click(object sender, RoutedEventArgs e)
    {
        startPass++;
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();
    }

    /// <summary>[XPLAT] Ports <c>btnDnStartTram_Click</c>: decrement the start pass (floor 0), rebuild.</summary>
    private void btnDnStartTram_Click(object sender, RoutedEventArgs e)
    {
        startPass--;
        if (startPass < 0) startPass = 0;
        lblStartPass.Text = "Start\r\n" + startPass.ToString(CultureInfo.InvariantCulture);
        lblNumPasses.Text = passes.ToString(CultureInfo.InvariantCulture);
        BuildTram();
    }
}
