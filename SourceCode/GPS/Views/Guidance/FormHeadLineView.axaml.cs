// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
// [XPLAT] RISK: immediate-mode OpenGL (GL.Begin/Vertex3/Color3/PointSize/LineWidth) requires a
//         desktop-GL compatibility context from AvaloniaGeoViewport; it will NOT run under an
//         OpenGL ES / ANGLE context (where the host feature-gates the immediate-mode pipeline and
//         renders a cleared surface instead). This dominant feasibility risk is tracked in
//         MIGRATION_DOCS/PARITY_REPORT.md (AAP §0.6.2). The render/geometry below is behaviour-frozen
//         and lifted VERBATIM from the WinForms FormHeadLine; only the host (WinForms GLControl ->
//         AvaloniaGeoViewport), the dialogs (FormNumeric / FormDialogView) and the mf-god-object ->
//         constructor-injected collaborators differ.
//
// [XPLAT] 1:1 parity reimplementation of SourceCode/GPS/Forms/Guidance/FormHeadLine.cs (+ .Designer.cs)
//         — the headland-line slice / clip editor. Code-behind for FormHeadLineView.axaml.

using System;
using System.Collections.Generic;
using System.Globalization;
using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.Visuals;
using AgOpenGPS.Helpers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Headland-line slice / clip editor (Avalonia <see cref="Window"/>) — the cross-platform
/// re-platform of the WinForms <c>FormHeadLine</c>. The operator taps two points on a field boundary
/// to define a slice arc, sets an offset distance, picks a curve / line build mode, and builds or
/// clips the headland line (<c>hdLine</c>). All geometry is behaviour-frozen and owned by the injected
/// domain objects (<see cref="CHeadLine"/> / <see cref="CBoundary"/> / <see cref="CABCurve"/>); this
/// view only presents and configures them and hosts the OpenGL surface.
/// </summary>
public partial class FormHeadLineView : Window
{
    // ===================================================================================================
    //  Injected collaborators — replace the WinForms "mf = callingForm as FormGPS;" god-object back-ref.
    //  [XPLAT] Only the collaborators the ported code actually needs are injected (AAP §0.7.1 limits new
    //  abstractions to IPlatformServices + the GL host adapter — NO new interfaces are introduced here).
    //  NOTE: the spec-suggested ctor lists a "CGLM glm" parameter, but NO CGLM type exists in this
    //  codebase — "glm" is the static class AgOpenGPS.glm, used statically below (glm.twoPI / glm.PIBy2 /
    //  glm.Distance / glm.DistanceSquared). It is therefore intentionally NOT a constructor parameter.
    // ===================================================================================================

    private readonly CHeadLine hdl;                 // [XPLAT] was mf.hdl
    private readonly CBoundary bnd;                  // [XPLAT] was mf.bnd
    private readonly CABCurve curve;                 // [XPLAT] was mf.curve (instance retained for ctor
                                                     //         uniformity with the sibling guidance views;
                                                     //         FormHeadLine's CABCurve calls are static).
    private readonly CTool tool;                     // [XPLAT] was mf.tool
    private readonly AvaloniaGeoViewport _viewport;  // [XPLAT] hosts the WinForms oglSelf GLControl surface

    private readonly Func<vec3> getPivotAxlePos;     // [XPLAT] was mf.pivotAxlePos
    private readonly Func<double> getMaxFieldDistance; // [XPLAT] was mf.maxFieldDistance
    private readonly Func<double> getFieldCenterX;   // [XPLAT] was mf.fieldCenterX
    private readonly Func<double> getFieldCenterY;   // [XPLAT] was mf.fieldCenterY
    private readonly Func<double> getM2FtOrM;         // [XPLAT] was mf.m2FtOrM
    private readonly Func<double> getFtOrMtoM;        // [XPLAT] was mf.ftOrMtoM
    private readonly Func<string> getUnitsFtM;        // [XPLAT] was mf.unitsFtM
    private readonly Action calculateMinMax;          // [XPLAT] was mf.CalculateMinMax()
    private readonly Action fileSaveHeadland;         // [XPLAT] was mf.FileSaveHeadland()
    private readonly Action<bool> setHydLiftOn;       // [XPLAT] was mf.vehicle.isHydLiftOn = ...

    // ===================================================================================================
    //  Fields — preserved EXACTLY from the WinForms FormHeadLine (names + initial values).
    // ===================================================================================================

    private bool isA = true;
    private int start = 99999, end = 99999;
    private int bndSelect = 0;
    private TrackMode mode = TrackMode.None;
    public List<vec3> sliceArr = new List<vec3>();
    public List<vec3> backupList = new List<vec3>();

    private bool zoomToggle;

    private double zoom = 1, sX = 0, sY = 0;

    public vec3 pint = new vec3(0.0, 1.0, 0.0);

    // [XPLAT] WinForms "Point fixPt;" (System.Drawing.Point) -> two doubles (no System.Drawing.Point
    //         dependency). fixPt.X/fixPt.Y reads become fixPtX/fixPtY.
    private double fixPtX, fixPtY;

    // Returns 1 if the lines intersect — kept from the WinForms source (public, behaviour-frozen).
    public double iE = 0, iN = 0;

    // [XPLAT] Backing value for the WinForms NudlessNumericUpDown nudSetDistance.Value (decimal). The
    //         Avalonia nudSetDistance is a Button that opens FormNumeric; this double holds its value
    //         and RefreshDistanceText() mirrors it onto the button caption (InvariantCulture "N1").
    private double nudSetDistanceValue;

    // [XPLAT] WinForms System.Windows.Forms.Timer (Interval = 500, Enabled at design time) ->
    //         Avalonia DispatcherTimer started on open, stopped on close.
    private readonly DispatcherTimer _timer;

    // [XPLAT] One-time GL state init guard (was the oglSelf_Load handler, which the WinForms GLControl
    //         raised once when its context was created). Performed lazily inside the first RenderSelf so
    //         it runs with the host's GL context current.
    private bool _glInitDone;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia compiled-XAML loader and the design-time
    /// previewer only. It renders the static markup, wires no behaviour and leaves the injected
    /// collaborators null, so the lifecycle overrides treat a loader/preview instance as a no-op. The
    /// running application always uses the injected overload.
    /// </summary>
    public FormHeadLineView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the headland-line editor bound to the supplied collaborators (the cross-platform
    /// replacement for the WinForms <c>FormHeadLine(Form callingForm)</c> constructor).
    /// </summary>
    /// <param name="hdl">The headland-line model (was <c>mf.hdl</c>).</param>
    /// <param name="bnd">The boundary model (was <c>mf.bnd</c>).</param>
    /// <param name="curve">The AB-curve helper (was <c>mf.curve</c>; CABCurve calls here are static).</param>
    /// <param name="tool">The implement/tool model (was <c>mf.tool</c>).</param>
    /// <param name="viewport">The OpenGL viewport host adapter (replaces the WinForms <c>oglSelf</c>).</param>
    /// <param name="getPivotAxlePos">Supplies the vehicle pivot-axle position (was <c>mf.pivotAxlePos</c>).</param>
    /// <param name="getMaxFieldDistance">Supplies the field camera distance (was <c>mf.maxFieldDistance</c>).</param>
    /// <param name="getFieldCenterX">Supplies the field-centre easting (was <c>mf.fieldCenterX</c>).</param>
    /// <param name="getFieldCenterY">Supplies the field-centre northing (was <c>mf.fieldCenterY</c>).</param>
    /// <param name="getM2FtOrM">Metres -> display-unit multiplier (was <c>mf.m2FtOrM</c>).</param>
    /// <param name="getFtOrMtoM">Display-unit -> metres multiplier (was <c>mf.ftOrMtoM</c>).</param>
    /// <param name="getUnitsFtM">The display-unit label, "ft" or "m" (was <c>mf.unitsFtM</c>).</param>
    /// <param name="calculateMinMax">Recomputes the field extents (was <c>mf.CalculateMinMax()</c>).</param>
    /// <param name="fileSaveHeadland">Persists the headland set (was <c>mf.FileSaveHeadland()</c>).</param>
    /// <param name="setHydLiftOn">Sets the hydraulic-lift flag (was <c>mf.vehicle.isHydLiftOn = ...</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown when any collaborator is null.</exception>
    public FormHeadLineView(
        CHeadLine hdl,
        CBoundary bnd,
        CABCurve curve,
        CTool tool,
        AvaloniaGeoViewport viewport,
        Func<vec3> getPivotAxlePos,
        Func<double> getMaxFieldDistance,
        Func<double> getFieldCenterX,
        Func<double> getFieldCenterY,
        Func<double> getM2FtOrM,
        Func<double> getFtOrMtoM,
        Func<string> getUnitsFtM,
        Action calculateMinMax,
        Action fileSaveHeadland,
        Action<bool> setHydLiftOn)
        : this()
    {
        // [XPLAT] DI seam replacing "mf = callingForm as FormGPS;" — fail fast on a missing wiring
        //         rather than NRE deep inside a handler.
        this.hdl = hdl ?? throw new ArgumentNullException(nameof(hdl));
        this.bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        this.curve = curve ?? throw new ArgumentNullException(nameof(curve));
        this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        this.getPivotAxlePos = getPivotAxlePos ?? throw new ArgumentNullException(nameof(getPivotAxlePos));
        this.getMaxFieldDistance = getMaxFieldDistance ?? throw new ArgumentNullException(nameof(getMaxFieldDistance));
        this.getFieldCenterX = getFieldCenterX ?? throw new ArgumentNullException(nameof(getFieldCenterX));
        this.getFieldCenterY = getFieldCenterY ?? throw new ArgumentNullException(nameof(getFieldCenterY));
        this.getM2FtOrM = getM2FtOrM ?? throw new ArgumentNullException(nameof(getM2FtOrM));
        this.getFtOrMtoM = getFtOrMtoM ?? throw new ArgumentNullException(nameof(getFtOrMtoM));
        this.getUnitsFtM = getUnitsFtM ?? throw new ArgumentNullException(nameof(getUnitsFtM));
        this.calculateMinMax = calculateMinMax ?? throw new ArgumentNullException(nameof(calculateMinMax));
        this.fileSaveHeadland = fileSaveHeadland ?? throw new ArgumentNullException(nameof(fileSaveHeadland));
        this.setHydLiftOn = setHydLiftOn ?? throw new ArgumentNullException(nameof(setHydLiftOn));

        // [XPLAT] The former oglSelf_Paint body is supplied to the host as its render callback. The host
        //         invokes RenderAction between BeginPaint()/EndPaint(); this Pattern-B view sets up its
        //         OWN projection + modelview inside RenderSelf (overriding the host's bounding-box camera)
        //         because the WinForms dialog used a custom perspective + zoom/pan camera.
        _viewport.RenderAction = RenderSelf;

        // [XPLAT] Host the viewport's exposed Control in the XAML 'ViewportHost' Border (was oglSelf).
        ViewportHost.Child = _viewport.View;

        // [XPLAT] WinForms Timer (Interval 500, Enabled) -> DispatcherTimer started in OnOpened.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += timer1_Tick;

        // Wire the controls imperatively (the .axaml declares no Click=/event attributes). Done only in
        // the injected ctor so a loader/previewer instance stays inert.
        nudSetDistance.Click += nudSetDistance_Click;
        cboxToolWidths.SelectionChanged += cboxToolWidths_SelectedIndexChanged;
        btnBndLoop.Click += btnBndLoop_Click;
        btnDeletePoints.Click += btnDeletePoints_Click;
        btnClipLine.Click += btnSlice_Click;          // [XPLAT] WinForms btnClipLine.Click -> btnSlice_Click
        btnUndo.Click += btnUndo_Click;
        btnALength.Click += btnALength_Click;
        btnBLength.Click += btnBLength_Click;
        btnBShrink.Click += btnBShrink_Click;
        btnAShrink.Click += btnAShrink_Click;
        cboxIsSectionControlled.Click += cboxIsSectionControlled_Click;
        checkBoxZoomIn.IsCheckedChanged += cboxIsZoom_CheckedChanged; // [XPLAT] was cboxIsZoom_CheckedChanged
        btnHeadlandOff.Click += btnHeadlandOff_Click;
        btnExit.Click += btnExit_Click;

        // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown
        //         was wired in the designer; no Move/Up/Wheel).
        _viewport.View.PointerPressed += oglSelf_MouseDown;

        // [XPLAT] was mf.CalculateMinMax() in the WinForms ctor — recompute extents before first paint.
        calculateMinMax();
    }

    // ===================================================================================================
    //  Rendering — body lifted VERBATIM from the WinForms oglSelf_Paint / oglSelf_Resize / oglSelf_Load.
    //  [XPLAT] Pattern B: this view sets up its OWN projection + camera and draws with immediate-mode GL,
    //  overriding the host's BeginPaint bounding-box camera. The host (AvaloniaGeoViewport) invokes
    //  RenderSelf between BeginPaint()/EndPaint(); SwapBuffers is the host's job (Avalonia presents the
    //  framebuffer implicitly), so this view never swaps.
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] GL render callback (was <c>oglSelf_Paint</c>, with the <c>oglSelf_Resize</c> projection
    /// and one-time <c>oglSelf_Load</c> state folded in). Immediate-mode GL is preserved verbatim.
    /// </summary>
    // [XPLAT] RISK: GL.Begin/GL.Vertex3/GL.Color3 immediate-mode drawing needs a desktop-GL compatibility
    //         context; it is feature-gated under GLES/ANGLE (see MIGRATION_DOCS/PARITY_REPORT.md, §0.6.2).
    private void RenderSelf()
    {
        // A loader/previewer instance has no model — stay completely inert.
        if (bnd == null) return;

        // [XPLAT] one-time GL state init (was oglSelf_Load). Runs lazily with the host GL context current.
        if (!_glInitDone)
        {
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _glInitDone = true;
        }

        // [XPLAT] projection (was oglSelf_Resize). The WinForms oglSelf was a SQUARE control; the Avalonia
        //         GL surface may be non-square, so use a SQUARE viewport (perspective aspect = 1.0) sized
        //         to the smaller framebuffer dimension (PIXELS, from the host's ViewportSize).
        XyDelta vpSize = _viewport.ViewportSize;
        int side = (int)Math.Min(vpSize.DeltaX, vpSize.DeltaY);
        if (side < 1) side = 1;

        GL.MatrixMode(MatrixMode.Projection);
        GL.LoadIdentity();

        //58 degrees view
        GL.Viewport(0, 0, side, side);
        Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, 1.0f, 1.0f, 20000);
        GL.LoadMatrix(ref mat);

        GL.MatrixMode(MatrixMode.Modelview);

        // ---- was oglSelf_Paint ----
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
        GL.LoadIdentity();                  // Reset The View

        //back the camera up
        GL.Translate(0, 0, -getMaxFieldDistance() * zoom);

        //translate to that spot in the world
        GL.Translate(-getFieldCenterX() + sX * getMaxFieldDistance(), -getFieldCenterY() + sY * getMaxFieldDistance(), 0);

        //draw all the boundaries
        GL.LineWidth(4);

        for (int j = 0; j < bnd.bndList.Count; j++)
        {
            if (j == bndSelect)
                GL.Color3(0.75f, 0.75f, 0.750f);
            else
                GL.Color3(0.0f, 0.25f, 0.10f);

            GL.Begin(PrimitiveType.LineStrip);
            for (int i = 0; i < bnd.bndList[j].fenceLine.Count; i++)
            {
                GL.Vertex3(bnd.bndList[j].fenceLine[i].easting, bnd.bndList[j].fenceLine[i].northing, 0);
            }
            GL.End();
        }
        VehicleDotVisual.DrawVehicleDot(getPivotAxlePos().ToGeoCoord());
        //draw the line building graphics
        if (start != 99999 || end != 99999) DrawABTouchLine();

        //draw the actual built lines
        //if (start == 99999 && end == 99999)
        {
            DrawBuiltLines();
        }

        GL.Disable(EnableCap.Blend);

        GL.Flush();
        // [XPLAT] NO oglSelf.SwapBuffers() — the AvaloniaGeoViewport host presents the framebuffer
        //         implicitly after OnOpenGlRender returns (see AvaloniaGeoViewport.EndPaint).
    }

    private void DrawBuiltLines()
    {
        GL.LineWidth(8);
        GL.Color3(0.943f, 0.9083f, 0.09150f);
        GL.Begin(PrimitiveType.LineLoop);

        for (int i = 0; i < bnd.bndList[0].hdLine.Count; i++)
        {
            GL.Vertex3(bnd.bndList[0].hdLine[i].easting, bnd.bndList[0].hdLine[i].northing, 0);
        }
        GL.End();

        if (sliceArr.Count > 0)
        {
            //GL.Enable(EnableCap.LineStipple);
            //GL.LineStipple(1, 0x7070);
            GL.PointSize(8);

            if (mode == TrackMode.AB)
            {
                GL.Color3(0.95f, 0.09f, 0.0f);
            }
            else
            {
                GL.Color3(0.13f, 0.95f, 0.020f);
            }

            GL.Begin(PrimitiveType.LineStrip);
            foreach (vec3 item in sliceArr)
            {
                GL.Vertex3(item.easting, item.northing, 0);
            }
            GL.End();

            int cnt = sliceArr.Count - 1;
            GL.PointSize(24);
            GL.Color3(1.0f, 0.6f, 0.3f);
            GL.Begin(PrimitiveType.Points);
            GL.Vertex3(sliceArr[0].easting, sliceArr[0].northing, 0);
            GL.Color3(0.5f, 0.73f, 0.99f);
            GL.Vertex3(sliceArr[cnt].easting, sliceArr[cnt].northing, 0);
            GL.End();
        }
    }

    private void DrawABTouchLine()
    {
        GL.PointSize(24);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(0, 0, 0);
        if (start != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[start].easting, bnd.bndList[bndSelect].fenceLine[start].northing, 0);
        if (end != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[end].easting, bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();

        GL.PointSize(18);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(1.0f, 0.75f, 0.350f);
        if (start != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[start].easting, bnd.bndList[bndSelect].fenceLine[start].northing, 0);

        GL.Color3(0.5f, 0.75f, 1.0f);
        if (end != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[end].easting, bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();
    }

    // ===================================================================================================
    //  Pointer input — was oglSelf_MouseDown(MouseEventArgs). [XPLAT] async void so the distance-zero
    //  guard can await the cross-platform FormDialogView (was the modal FormDialog.Show). The pixel->world
    //  camera math is ported VERBATIM (Pattern B — NOT _viewport.GetGeoCoord).
    // ===================================================================================================

    private async void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
    {
        if (nudSetDistanceValue == 0 && rbtnCurve.IsChecked == true)
        {
            // [XPLAT] FormDialog.Show(...DialogSeverity.Error) -> await FormDialogView.ShowAsync(...).
            await FormDialogView.ShowAsync("Distance Error", "Distance Set to 0, Nothing to Move", DialogSeverity.Error, this);
            Log.EventWriter("Headland, Distance=0, Can't Move");
            return;
        }
        sliceArr?.Clear();

        // [XPLAT] was oglSelf.PointToClient(Cursor.Position) (client PIXELS). e.GetPosition returns logical
        //         (DIP) coordinates; ptt and wid are kept in the SAME (DIP) units so the screen->world ratio
        //         is identical to the WinForms pixel math (the units cancel in fixPt / scale).
        var view = _viewport.View;
        var ptt = e.GetPosition(view);

        double wid = Math.Min(view.Bounds.Width, view.Bounds.Height);
        double halfWid = wid / 2;
        double scale = wid * 0.903;

        if (checkBoxZoomIn.IsChecked == true && !zoomToggle)
        {
            sX = ((halfWid - ptt.X) / wid) * 1.1;
            sY = ((halfWid - ptt.Y) / -wid) * 1.1;
            zoom = 0.1;
            zoomToggle = true;
            return;
        }

        //Convert to Origin in the center of window, 800 pixels
        fixPtX = ptt.X - halfWid;
        fixPtY = (wid - ptt.Y - halfWid);
        vec3 plotPt = new vec3
        {
            //convert screen coordinates to field coordinates
            easting = fixPtX * getMaxFieldDistance() / scale * zoom,
            northing = fixPtY * getMaxFieldDistance() / scale * zoom,
            heading = 0
        };

        plotPt.easting += getFieldCenterX() + getMaxFieldDistance() * -sX;
        plotPt.northing += getFieldCenterY() + getMaxFieldDistance() * -sY;

        pint.easting = plotPt.easting;
        pint.northing = plotPt.northing;

        zoomToggle = false;
        zoom = 1;
        sX = 0;
        sY = 0;

        if (isA)
        {
            double minDistA = double.MaxValue;
            start = 99999; end = 99999;

            for (int j = 0; j < bnd.bndList.Count; j++)
            {
                for (int i = 0; i < bnd.bndList[j].fenceLine.Count; i++)
                {
                    double dist = ((pint.easting - bnd.bndList[j].fenceLine[i].easting) * (pint.easting - bnd.bndList[j].fenceLine[i].easting))
                                    + ((pint.northing - bnd.bndList[j].fenceLine[i].northing) * (pint.northing - bnd.bndList[j].fenceLine[i].northing));
                    if (dist < minDistA)
                    {
                        minDistA = dist;
                        bndSelect = j;
                        start = i;
                    }
                }
            }

            isA = false;
        }
        else
        {
            double minDistA = double.MaxValue;
            int j = bndSelect;

            for (int i = 0; i < bnd.bndList[j].fenceLine.Count; i++)
            {
                double dist = ((pint.easting - bnd.bndList[j].fenceLine[i].easting) * (pint.easting - bnd.bndList[j].fenceLine[i].easting))
                                + ((pint.northing - bnd.bndList[j].fenceLine[i].northing) * (pint.northing - bnd.bndList[j].fenceLine[i].northing));
                if (dist < minDistA)
                {
                    minDistA = dist;
                    end = i;
                }
            }

            isA = true;

            //build the lines
            if (rbtnCurve.IsChecked == true)
            {
                bool isLoop = false;
                int limit = end;

                if ((Math.Abs(start - end)) > (bnd.bndList[bndSelect].fenceLine.Count * 0.5))
                {
                    if (start < end)
                    {
                        (start, end) = (end, start);
                    }

                    isLoop = true;
                    if (start < end)
                    {
                        limit = end;
                        end = 0;
                    }
                    else
                    {
                        limit = end;
                        end = bnd.bndList[bndSelect].fenceLine.Count;
                    }
                }
                else
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }
                }

                sliceArr?.Clear();
                vec3 pt3 = new vec3();

                if (start < end)
                {
                    for (int i = start; i <= end; i++)
                    {
                        //calculate the point inside the boundary
                        pt3 = bnd.bndList[bndSelect].fenceLine[i];
                        sliceArr.Add(pt3);

                        if (isLoop && i == bnd.bndList[bndSelect].fenceLine.Count - 1)
                        {
                            i = -1;
                            isLoop = false;
                            end = limit;
                        }
                    }
                }
                else
                {
                    for (int i = start; i >= end; i--)
                    {
                        //calculate the point inside the boundary
                        pt3 = bnd.bndList[bndSelect].fenceLine[i];
                        sliceArr.Add(pt3);

                        if (isLoop && i == 0)
                        {
                            i = bnd.bndList[bndSelect].fenceLine.Count - 1;
                            isLoop = false;
                            end = limit;
                        }
                    }
                }

                int ptCnt = sliceArr.Count - 1;

                if (ptCnt > 0)
                {
                    //who knows which way it actually goes
                    CABCurve.CalculateHeadings(ref sliceArr);

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pt = new vec3(sliceArr[ptCnt]);
                        pt.easting += (Math.Sin(pt.heading) * i);
                        pt.northing += (Math.Cos(pt.heading) * i);
                        sliceArr.Add(pt);
                    }

                    vec3 stat = new vec3(sliceArr[0]);

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pt = new vec3(stat);
                        pt.easting -= (Math.Sin(pt.heading) * i);
                        pt.northing -= (Math.Cos(pt.heading) * i);
                        sliceArr.Insert(0, pt);
                    }

                    mode = TrackMode.Curve;
                }
                else
                {
                    start = 99999; end = 99999;
                    return;
                }

                //update the arrays
                start = 99999; end = 99999;

                btnExit.Focus();
            }
            else if (rbtnLine.IsChecked == true)
            {
                if ((Math.Abs(start - end)) > (bnd.bndList[bndSelect].fenceLine.Count * 0.5))
                {
                    if (start < end)
                    {
                        (start, end) = (end, start);
                    }
                }
                else
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }
                }

                vec3 ptA = new vec3(bnd.bndList[bndSelect].fenceLine[start]);
                vec3 ptB = new vec3(bnd.bndList[bndSelect].fenceLine[end]);

                //calculate the AB Heading
                double abHead = Math.Atan2(
                    bnd.bndList[bndSelect].fenceLine[end].easting - bnd.bndList[bndSelect].fenceLine[start].easting,
                    bnd.bndList[bndSelect].fenceLine[end].northing - bnd.bndList[bndSelect].fenceLine[start].northing);
                if (abHead < 0) abHead += glm.twoPI;

                sliceArr?.Clear();

                ptA.heading = abHead;
                ptB.heading = abHead;

                for (int i = 0; i <= (int)(glm.Distance(ptA, ptB)); i++)
                {
                    vec3 ptC = new vec3(ptA)
                    {
                        easting = (Math.Sin(abHead) * i) + ptA.easting,
                        northing = (Math.Cos(abHead) * i) + ptA.northing,
                        heading = abHead
                    };
                    sliceArr.Add(ptC);
                }

                int ptCnt = sliceArr.Count - 1;

                for (int i = 1; i < 30; i++)
                {
                    vec3 pt = new vec3(sliceArr[ptCnt]);
                    pt.easting += (Math.Sin(pt.heading) * i);
                    pt.northing += (Math.Cos(pt.heading) * i);
                    sliceArr.Add(pt);
                }

                vec3 stat = new vec3(sliceArr[0]);

                for (int i = 1; i < 30; i++)
                {
                    vec3 pt = new vec3(stat);
                    pt.easting -= (Math.Sin(pt.heading) * i);
                    pt.northing -= (Math.Cos(pt.heading) * i);
                    sliceArr.Insert(0, pt);
                }

                mode = TrackMode.AB;

                start = 99999; end = 99999;
            }

            //Move the line
            if (nudSetDistanceValue != 0)
                SetLineDistance();

            btnClipLine.IsEnabled = true;
        }
    }

    private void SetLineDistance()
    {
        hdl.desList?.Clear();

        if (sliceArr.Count < 1) return;

        double distAway = nudSetDistanceValue * getFtOrMtoM();

        double distSqAway = (distAway * distAway) - 0.01;
        vec3 point;

        int refCount = sliceArr.Count;
        for (int i = 0; i < refCount; i++)
        {
            point = new vec3(
            sliceArr[i].easting - (Math.Sin(glm.PIBy2 + sliceArr[i].heading) * distAway),
            sliceArr[i].northing - (Math.Cos(glm.PIBy2 + sliceArr[i].heading) * distAway),
            sliceArr[i].heading);
            bool Add = true;

            for (int t = 0; t < refCount; t++)
            {
                double dist = ((point.easting - sliceArr[t].easting) * (point.easting - sliceArr[t].easting))
                    + ((point.northing - sliceArr[t].northing) * (point.northing - sliceArr[t].northing));
                if (dist < distSqAway)
                {
                    Add = false;
                    break;
                }
            }

            if (Add)
            {
                if (hdl.desList.Count > 0)
                {
                    double dist = ((point.easting - hdl.desList[hdl.desList.Count - 1].easting) * (point.easting - hdl.desList[hdl.desList.Count - 1].easting))
                        + ((point.northing - hdl.desList[hdl.desList.Count - 1].northing) * (point.northing - hdl.desList[hdl.desList.Count - 1].northing));
                    if (dist > 1)
                        hdl.desList.Add(point);
                }
                else hdl.desList.Add(point);
            }
        }

        sliceArr.Clear();

        for (int i = 0; i < hdl.desList.Count; i++)
        {
            sliceArr.Add(new vec3(hdl.desList[i]));
        }

        hdl.desList?.Clear();
    }

    // ===================================================================================================
    //  Control handlers — ported 1:1 from the WinForms FormHeadLine (behaviour frozen). Geometry / list
    //  math is VERBATIM; only mf.* -> injected collaborators, nudSetDistance.Value -> nudSetDistanceValue,
    //  the WinForms dialogs -> FormNumeric / FormDialogView, and the event signatures change.
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] was nudSetDistance_Click: the WinForms NudlessNumericUpDown.ShowKeypad(this) is replaced by
    /// the cross-platform FormNumeric value editor (Min 0, Max 200 from the designer). async void per the
    /// Avalonia modal-dialog idiom.
    /// </summary>
    private async void nudSetDistance_Click(object sender, RoutedEventArgs e)
    {
        var f = new FormNumeric(0, 200, nudSetDistanceValue);
        if (await f.ShowDialog<bool>(this))
        {
            nudSetDistanceValue = f.ReturnValue;
            RefreshDistanceText();
        }
        btnExit.Focus();
    }

    // [XPLAT] was cboxToolWidths_SelectedIndexChanged (SelectedIndexChanged -> SelectionChanged). The
    //         decimal cast onto nudSetDistance.Value is dropped (nudSetDistanceValue is a double).
    private void cboxToolWidths_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        nudSetDistanceValue = (Math.Round((tool.width - tool.overlap) * cboxToolWidths.SelectedIndex, 1)) * getM2FtOrM();
        RefreshDistanceText();
    }

    private void btnBndLoop_Click(object sender, RoutedEventArgs e)
    {
        int ptCount = bnd.bndList[0].fenceLine.Count;

        if (nudSetDistanceValue == 0)
        {
            hdl.desList.Clear();

            bnd.bndList[0].hdLine?.Clear();

            for (int i = 0; i < ptCount; i++)
            {
                bnd.bndList[0].hdLine.Add(new vec3(bnd.bndList[0].fenceLine[i]));
            }
        }
        else
        {
            hdl.desList?.Clear();

            //outside point
            vec3 pt3 = new vec3();

            double moveDist = nudSetDistanceValue * getFtOrMtoM();
            double distSq = (moveDist) * (moveDist) * 0.999;

            //make the boundary tram outer array
            for (int i = 0; i < ptCount; i++)
            {
                //calculate the point inside the boundary
                pt3.easting = bnd.bndList[0].fenceLine[i].easting -
                    (Math.Sin(glm.PIBy2 + bnd.bndList[0].fenceLine[i].heading) * (moveDist));

                pt3.northing = bnd.bndList[0].fenceLine[i].northing -
                    (Math.Cos(glm.PIBy2 + bnd.bndList[0].fenceLine[i].heading) * (moveDist));

                pt3.heading = bnd.bndList[0].fenceLine[i].heading;

                bool Add = true;

                for (int j = 0; j < ptCount; j++)
                {
                    double check = glm.DistanceSquared(pt3.northing, pt3.easting,
                                        bnd.bndList[0].fenceLine[j].northing, bnd.bndList[0].fenceLine[j].easting);
                    if (check < distSq)
                    {
                        Add = false;
                        break;
                    }
                }

                if (Add)
                {
                    if (hdl.desList.Count > 0)
                    {
                        double dist = ((pt3.easting - hdl.desList[hdl.desList.Count - 1].easting) * (pt3.easting - hdl.desList[hdl.desList.Count - 1].easting))
                            + ((pt3.northing - hdl.desList[hdl.desList.Count - 1].northing) * (pt3.northing - hdl.desList[hdl.desList.Count - 1].northing));
                        if (dist > 1)
                            hdl.desList.Add(pt3);
                    }
                    else hdl.desList.Add(pt3);
                }
            }

            if (hdl.desList.Count == 0)
            {
                return;
            }

            pt3 = new vec3(hdl.desList[0]);
            hdl.desList.Add(pt3);

            int cnt = hdl.desList.Count;
            if (cnt > 3)
            {
                pt3 = new vec3(hdl.desList[0]);
                hdl.desList.Add(pt3);

                //make sure point distance isn't too big 
                CABCurve.MakePointMinimumSpacing(ref hdl.desList, 1.2);
                CABCurve.CalculateHeadings(ref hdl.desList);

                bnd.bndList[0].hdLine.Clear();

                //write out the Points
                foreach (vec3 item in hdl.desList)
                {
                    bnd.bndList[0].hdLine.Add(item);
                }
            }
        }

        fileSaveHeadland();
    }

    // [XPLAT] was btnSlice_Click (wired to btnClipLine.Click). async void so the "Crossings not Found"
    //         guard can await FormDialogView (was FormDialog.Show).
    private async void btnSlice_Click(object sender, RoutedEventArgs e)
    {
        int startBnd = 0, endBnd = 0, startLine = 0, endLine = 0;
        int isStart = 0;

        if (sliceArr.Count == 0) return;

        //save a backup
        backupList?.Clear();
        foreach (var item in bnd.bndList[0].hdLine)
        {
            backupList.Add(item);
        }

        for (int i = 0; i < sliceArr.Count - 2; i++)
        {
            for (int k = 0; k < bnd.bndList[0].hdLine.Count - 2; k++)
            {
                GeoLineSegment sliceSegment = GeoRefactorHelper.GetLineSegment(sliceArr, i);
                GeoLineSegment headLineSegment = bnd.bndList[0].GetHeadLineSegment(k);
                GeoCoord? intersectionPoint = sliceSegment.IntersectionPoint(headLineSegment);

                if (intersectionPoint.HasValue)
                {
                    if (isStart == 0)
                    {
                        startBnd = k + 1;
                        startLine = i + 1;
                    }
                    else
                    {
                        endBnd = k + 1;
                        endLine = i;
                    }
                    isStart++;
                }
            }
        }

        if (isStart < 2)
        {
            await FormDialogView.ShowAsync("Error", "Crossings not Found", DialogSeverity.Error, this);
            Log.EventWriter("Headland, Crossings Not Found");

            return;
        }

        //overlaps start finish
        if ((Math.Abs(startBnd - endBnd)) > (bnd.bndList[bndSelect].fenceLine.Count * 0.5))
        {
            if (startBnd < endBnd)
            {
                (startBnd, endBnd) = (endBnd, startBnd);
            }

            hdl.desList?.Clear();

            //first bnd segment
            for (int i = endBnd; i < startBnd; i++)
            {
                hdl.desList.Add(bnd.bndList[0].hdLine[i]);
            }

            for (int i = startLine; i < endLine; i++)
            {
                hdl.desList.Add(sliceArr[i]);
            }

            //build headline from desList
            bnd.bndList[0].hdLine.Clear();

            foreach (var item in hdl.desList)
            {
                bnd.bndList[0].hdLine.Add(item);
            }
        }
        // completely in between start finish
        else
        {
            if (startBnd > endBnd)
            {
                (startBnd, endBnd) = (endBnd, startBnd);
            }

            hdl.desList?.Clear();

            //first bnd segment
            for (int i = 0; i < startBnd; i++)
            {
                hdl.desList.Add(bnd.bndList[0].hdLine[i]);
            }

            //line segment
            for (int i = startLine; i < endLine; i++)
            {
                hdl.desList.Add(sliceArr[i]);
            }

            //final bnd segment
            for (int i = endBnd; i < bnd.bndList[0].hdLine.Count; i++)
            {
                hdl.desList.Add(bnd.bndList[0].hdLine[i]);
            }

            //build headline from desList
            bnd.bndList[0].hdLine.Clear();

            foreach (var item in hdl.desList)
            {
                bnd.bndList[0].hdLine.Add(item);
            }
        }

        hdl.desList?.Clear();
        sliceArr?.Clear();
    }

    // [XPLAT] was btnDeletePoints_Click ("Reset").
    private void btnDeletePoints_Click(object sender, RoutedEventArgs e)
    {
        start = 99999; end = 99999;
        isA = true;
        hdl.desList?.Clear();
        sliceArr?.Clear();
        backupList?.Clear();
        bnd.bndList[0].hdLine?.Clear();

        int ptCount = bnd.bndList[0].fenceLine.Count;

        for (int i = 0; i < ptCount; i++)
        {
            bnd.bndList[0].hdLine.Add(new vec3(bnd.bndList[0].fenceLine[i]));
        }
    }

    private void btnUndo_Click(object sender, RoutedEventArgs e)
    {
        bnd.bndList[0].hdLine?.Clear();
        foreach (var item in backupList)
        {
            bnd.bndList[0].hdLine.Add(item);
        }
        backupList?.Clear();
    }

    private void btnALength_Click(object sender, RoutedEventArgs e)
    {
        if (sliceArr.Count > 0)
        {
            //and the beginning
            vec3 start = new vec3(sliceArr[0]);

            for (int i = 1; i < 10; i++)
            {
                vec3 pt = new vec3(start);
                pt.easting -= (Math.Sin(pt.heading) * i);
                pt.northing -= (Math.Cos(pt.heading) * i);
                sliceArr.Insert(0, pt);
            }
        }
    }

    private void btnBLength_Click(object sender, RoutedEventArgs e)
    {
        if (sliceArr.Count > 0)
        {
            int ptCnt = sliceArr.Count - 1;

            for (int i = 1; i < 10; i++)
            {
                vec3 pt = new vec3(sliceArr[ptCnt]);
                pt.easting += (Math.Sin(pt.heading) * i);
                pt.northing += (Math.Cos(pt.heading) * i);
                sliceArr.Add(pt);
            }
        }
    }

    private void btnBShrink_Click(object sender, RoutedEventArgs e)
    {
        if (sliceArr.Count > 8)
            sliceArr.RemoveRange(sliceArr.Count - 5, 5);
    }

    private void btnAShrink_Click(object sender, RoutedEventArgs e)
    {
        if (sliceArr.Count > 8)
            sliceArr.RemoveRange(0, 5);
    }

    // [XPLAT] was cboxIsSectionControlled_Click — swap the On/Off glyph.
    private void cboxIsSectionControlled_Click(object sender, RoutedEventArgs e)
    {
        SetSectionControlledImage();
    }

    // [XPLAT] was cboxIsZoom_CheckedChanged — wired to checkBoxZoomIn.IsCheckedChanged.
    private void cboxIsZoom_CheckedChanged(object sender, RoutedEventArgs e)
    {
        zoomToggle = false;
    }

    private void btnHeadlandOff_Click(object sender, RoutedEventArgs e)
    {
        bnd.bndList[0].hdLine?.Clear();
        fileSaveHeadland();
        bnd.isHeadlandOn = false;
        setHydLiftOn(false);
        Close();
    }

    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        vec3[] hdArr;

        if (bnd.bndList[0].hdLine.Count > 0)
        {
            hdArr = new vec3[bnd.bndList[0].hdLine.Count];
            bnd.bndList[0].hdLine.CopyTo(hdArr);
            bnd.bndList[0].hdLine?.Clear();

            //does headland control sections
            bnd.isSectionControlledByHeadland = cboxIsSectionControlled.IsChecked == true;
            AgOpenGPS.Properties.ToolSettings.Default.setHeadland_isSectionControlled = cboxIsSectionControlled.IsChecked == true;
            AgOpenGPS.Properties.ToolSettings.Default.Save();

            //middle points
            for (int i = 1; i < hdArr.Length; i++)
            {
                hdArr[i - 1].heading = Math.Atan2(hdArr[i - 1].easting - hdArr[i].easting, hdArr[i - 1].northing - hdArr[i].northing);
                if (hdArr[i].heading < 0) hdArr[i].heading += glm.twoPI;
            }

            double delta = 0;
            for (int i = 0; i < hdArr.Length; i++)
            {
                if (i == 0)
                {
                    bnd.bndList[0].hdLine.Add(new vec3(hdArr[i].easting, hdArr[i].northing, hdArr[i].heading));
                    continue;
                }
                delta += (hdArr[i - 1].heading - hdArr[i].heading);

                if (Math.Abs(delta) > 0.005)
                {
                    vec3 pt = new vec3(hdArr[i].easting, hdArr[i].northing, hdArr[i].heading);

                    bnd.bndList[0].hdLine.Add(pt);
                    delta = 0;
                }
            }
            vec3 ptEnd = new vec3(hdArr[hdArr.Length - 1].easting, hdArr[hdArr.Length - 1].northing, hdArr[hdArr.Length - 1].heading);

            bnd.bndList[0].hdLine.Add(ptEnd);
        }

        fileSaveHeadland();
        Close();
    }

    // ===================================================================================================
    //  Lifecycle — was FormHeadLine_Load / FormHeadLine_FormClosing.
    // ===================================================================================================

    /// <summary>[XPLAT] was FormHeadLine_Load (+ the work-area centering folded into the .axaml's
    /// WindowStartupLocation="CenterOwner"). Runs once when the window opens.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // A loader/previewer instance has no model — stay completely inert.
        if (bnd == null) return;

        hdl.idx = -1;

        // [XPLAT] InvariantCulture "N1" (AAP §0.6.5) — guidance/tool values feed protocol/file paths.
        lblToolWidth.Text = "( " + getUnitsFtM() + " )           Tool: " + ((tool.width - tool.overlap) * getM2FtOrM()).ToString("N1", CultureInfo.InvariantCulture) + " " + getUnitsFtM();

        start = 99999; end = 99999;
        isA = true;
        hdl.desList?.Clear();
        sliceArr?.Clear();
        backupList?.Clear();

        btnClipLine.IsEnabled = false;

        if (bnd.bndList[0].hdLine.Count == 0)
        {
            bnd.bndList[0].hdLine?.Clear();

            if (bnd.bndList[0].fenceLine.Count > 0)
            {
                for (int i = 0; i < bnd.bndList[0].fenceLine.Count; i++)
                {
                    bnd.bndList[0].hdLine.Add(new vec3(bnd.bndList[0].fenceLine[i]));
                }
            }
        }
        else
        {
            //make sure point distance isn't too big 
            CABCurve.MakePointMinimumSpacing(ref bnd.bndList[0].hdLine, 1.2);
            CABCurve.CalculateHeadings(ref bnd.bndList[0].hdLine);
        }

        cboxIsSectionControlled.IsChecked = AgOpenGPS.Properties.ToolSettings.Default.setHeadland_isSectionControlled;
        SetSectionControlledImage();

        checkBoxZoomIn.IsChecked = false;

        // [XPLAT] Apply the persisted window size (was Size = Settings.setWindow_HeadlineSize). The WinForms
        //         work-area centering is replaced by WindowStartupLocation="CenterOwner" in the .axaml.
        System.Drawing.Size savedSize = AgOpenGPS.Properties.Settings.Default.setWindow_HeadlineSize;
        if (savedSize.Width > 0 && savedSize.Height > 0)
        {
            Width = savedSize.Width;
            Height = savedSize.Height;
        }

        //translate
        Title = gStr.gsHeadlandForm;
        btnBndLoopText.Text = gStr.gsBuildAround;
        btnDeletePointsText.Text = gStr.gsReset;
        btnClipLineText.Text = gStr.gsClipLine;
        checkBoxZoomInText.Text = gStr.gsZoomIn;

        RefreshDistanceText();

        // [XPLAT] WinForms timer1.Enabled = true -> start the DispatcherTimer.
        _timer.Start();
    }

    /// <summary>[XPLAT] was FormHeadLine_FormClosing — set hdl.idx and persist the window size (schema
    /// frozen, AAP §0.2.1: stored via the same setWindow_HeadlineSize key as System.Drawing.Size).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (bnd != null)
        {
            if (sliceArr.Count > 0)
            {
                hdl.idx = 0;
            }
            else hdl.idx = -1;

            double w = Width, h = Height;
            if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
            {
                AgOpenGPS.Properties.Settings.Default.setWindow_HeadlineSize = new System.Drawing.Size((int)w, (int)h);
                AgOpenGPS.Properties.Settings.Default.Save();
            }
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // [XPLAT] Stop/dispose the DispatcherTimer and release the render callback so nothing keeps
        //         ticking or drawing after the dialog is gone.
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

    // [XPLAT] was timer1_Tick. oglSelf.Refresh() -> _viewport.RequestRender(). Enable/disable logic VERBATIM.
    private void timer1_Tick(object sender, EventArgs e)
    {
        _viewport.RequestRender();
        if (sliceArr.Count == 0)
        {
            btnClipLine.IsEnabled = false;
            btnALength.IsEnabled = false;
            btnBLength.IsEnabled = false;
            btnAShrink.IsEnabled = false;
            btnBShrink.IsEnabled = false;
        }
        else
        {
            btnClipLine.IsEnabled = true;
            btnBLength.IsEnabled = true;
            btnALength.IsEnabled = true;
            btnAShrink.IsEnabled = true;
            btnBShrink.IsEnabled = true;
        }

        if (backupList.Count == 0) btnUndo.IsEnabled = false; else btnUndo.IsEnabled = true;
        if (nudSetDistanceValue == 0) btnBndLoop.IsEnabled = false; else btnBndLoop.IsEnabled = true;
    }

    // ===================================================================================================
    //  Helpers (no WinForms equivalent — bridge the Avalonia control model to the frozen behaviour).
    // ===================================================================================================

    /// <summary>
    /// [XPLAT] Mirrors <see cref="nudSetDistanceValue"/> onto the nudSetDistance button caption (was the
    /// NudlessNumericUpDown.Value display). InvariantCulture "N1" — DecimalPlaces = 1 (AAP §0.6.5).
    /// </summary>
    private void RefreshDistanceText()
    {
        nudSetDistance.Content = nudSetDistanceValue.ToString("N1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Swaps the cboxIsSectionControlled glyph HeadlandSectionOn &lt;-&gt; HeadlandSectionOff to
    /// convey state (was <c>cboxIsSectionControlled.Image = Resources.HeadlandSectionOn/Off</c>). The XAML
    /// hosts the glyph as the toggle's Content &lt;Image&gt;; only its Source is swapped.
    /// </summary>
    private void SetSectionControlledImage()
    {
        string asset = cboxIsSectionControlled.IsChecked == true
            ? "avares://AgOpenGPS/btnImages/HeadlandSectionOn.png"
            : "avares://AgOpenGPS/btnImages/HeadlandSectionOff.png";

        if (cboxIsSectionControlled.Content is Image img)
        {
            img.Source = new Bitmap(AssetLoader.Open(new Uri(asset)));
        }
    }
}
