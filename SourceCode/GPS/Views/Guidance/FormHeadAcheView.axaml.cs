// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Code-behind for FormHeadAcheView — a 1:1 behavioural-parity port of the WinForms
// "Headache" headland builder (Forms/Guidance/FormHeadAche.cs + FormHeadAche.Designer.cs). The operator
// taps two points on a field boundary fence, builds clip lines (AB or Curve), tunes their length/offset,
// then assembles the headland loop. The left half is a live OpenGL field viewport (tap-to-pick); the
// right half is the 6-column command grid declared in the partner FormHeadAcheView.axaml.
//
// RENDERING — "Pattern B" (raw immediate-mode GL + a CUSTOM perspective camera, even more raw than
// FormABDraw): the former oglSelf_Paint draws the boundary fence lines, the built head-paths and the two
// touch points ALL via direct GL.Begin/GL.Vertex3 — it does NOT use the Core Visuals/DrawLib helpers.
// Per AAP §0.2.2/§0.7.1 every geometry/GL routine is BEHAVIOR-FROZEN and ported VERBATIM from the source.
//
// [XPLAT] RISK: this view relies on legacy immediate-mode / fixed-function OpenGL (GL.Begin/GL.Vertex3,
// the GL matrix stack, GL.Translate). The hosting AgOpenGPS.Controls.AvaloniaGeoViewport MUST therefore
// supply a DESKTOP-GL compatibility context (NOT GLES/ANGLE) or the host gates rendering to a cleared
// surface. This is the dominant feasibility risk per AAP §0.6.2 — tracked in MIGRATION_DOCS/PARITY_REPORT.md.
//
// God-object removal: the WinForms shell reached the running program through a "FormGPS mf" back-reference.
// That coupling is replaced by constructor injection of the concrete domain objects (CHeadLine/CBoundary/
// CABCurve/CTool), the field-geometry accessors (Func<>) and the persistence/side-effect operations
// (Action) — NO interfaces, NO FormGPS. The injection style mirrors the sibling GL-hosting views
// (FormBndToolView / FormBuildBoundaryFromTracksView).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;                   // Size / Point / Vector value types (used via var; kept per spec)
using Avalonia.Controls;
using Avalonia.Input;             // [XPLAT] PointerPressedEventArgs (was System.Windows.Forms.MouseEventArgs)
using Avalonia.Interactivity;     // [XPLAT] RoutedEventArgs (was System.EventArgs on WinForms handlers)
using Avalonia.Media.Imaging;     // [XPLAT] Bitmap — for the toggle glyph swap (was Properties.Resources images)
using Avalonia.Platform;          // [XPLAT] AssetLoader — packaged avares:// glyph loading
using Avalonia.Threading;         // [XPLAT] DispatcherTimer (was System.Windows.Forms.Timer)
using OpenTK;                     // KEEP — Matrix4 for the perspective projection
using OpenTK.Graphics.OpenGL;     // KEEP — immediate-mode GL preserved (Pattern B)
using AgOpenGPS.Core.Models;      // GeoLineSegment, GeoCoord (intersection scan)
using AgOpenGPS.Core.Translations;// gStr.gsHeadlandForm / gsBuild / gsReset
using AgLibrary.Logging;          // Log.EventWriter (preserved in btnBndLoop error paths)

// [XPLAT] namespace AgOpenGPS.WinForms -> AgOpenGPS.Views (matches the partner .axaml x:Class). The
// domain types (vec3, glm, CHeadLine, CHeadPath, CBoundary, CBoundaryList, CABCurve, CTool, TrackMode)
// live in namespace AgOpenGPS, so this view "using AgOpenGPS;" them. FormNumeric / FormDialogView /
// DialogSeverity already live in AgOpenGPS.Views (this namespace), so they need no using.
using AgOpenGPS;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] The "Headache" headland-builder window. A pure editor dialog: callers
/// <c>await ShowDialog(owner)</c>; there is no dialog return value — every mutation is committed to the
/// injected domain objects and persisted through the injected file Actions (and in <see cref="OnClosing"/>).
/// </summary>
public partial class FormHeadAcheView : Window
{
    // ===================================================================================================
    //  Injected collaborators (replace the WinForms "FormGPS mf" back-reference). Bare field names
    //  (hdl/bnd/curve/tool) are used deliberately so the ported bodies read exactly as the source did
    //  (e.g. hdl.tracksArr, bnd.bndList, tool.width, curve.desList).
    // ===================================================================================================

    // [XPLAT] was mf.hdl — head lines model (tracksArr / idx / desList). FileSave/Load are NOT on CHeadLine,
    // so persistence is supplied via the Action delegates below.
    private readonly CHeadLine hdl;

    // [XPLAT] was mf.bnd — boundary model (bndList[*].fenceLine / hdLine; isHeadlandOn / isSectionControlledByHeadland).
    private readonly CBoundary bnd;

    // [XPLAT] was mf.curve — only curve.desList is touched here (btnCancelTouch clears it).
    private readonly CABCurve curve;

    // [XPLAT] was mf.tool — tool.width / tool.overlap drive the tool-width readout and the multiplier combo.
    private readonly CTool tool;

    // [XPLAT] was "oglSelf" (OpenTK.GLControl). The cross-platform adapter is INJECTED (composition root
    // builds it from the field bounding box); this view sets its RenderAction and hosts its .View Control.
    private readonly AgOpenGPS.Controls.AvaloniaGeoViewport _viewport;

    // [XPLAT] field-geometry accessors (was mf.maxFieldDistance / mf.fieldCenterX / mf.fieldCenterY).
    private readonly Func<double> getMaxFieldDistance;
    private readonly Func<double> getFieldCenterX;
    private readonly Func<double> getFieldCenterY;

    // [XPLAT] unit helpers (was mf.unitsFtM / mf.m2FtOrM / mf.ftOrMtoM).
    private readonly Func<string> getUnitsFtM;
    private readonly Func<double> getM2FtOrM;
    private readonly Func<double> getFtOrMtoM;

    // [XPLAT] operations (were mf.CalculateMinMax() / mf.FileLoadHeadLines() / mf.FileSaveHeadLines() /
    // mf.FileSaveHeadland() and "mf.vehicle.isHydLiftOn = value").
    private readonly Action calculateMinMax;
    private readonly Action fileLoadHeadLines;
    private readonly Action fileSaveHeadLines;
    private readonly Action fileSaveHeadland;
    private readonly Action<bool> setHydLiftOn;

    // [XPLAT] was System.Windows.Forms.Timer (Interval 500, Enabled). Avalonia DispatcherTimer started in
    // the injected ctor, stopped in OnClosing (Avalonia timers are not disposed with the window).
    private DispatcherTimer _timer;

    // [XPLAT] one-shot GL state init guard (the source did this once in oglSelf_Load; the Avalonia host
    // exposes no init hook, so the GL-thread RenderAction performs it on its first invocation).
    private bool _glInited;

    // ===================================================================================================
    //  Parity state — preserved EXACTLY from FormHeadAche.cs (L22-33, L703-705).
    // ===================================================================================================

    // [XPLAT] was "Point fixPt" (System.Drawing.Point client pixels) -> two doubles (no Avalonia Point field).
    private double fixPtX, fixPtY;

    private bool isA = true;
    private int start = 99999, end = 99999;
    private int bndSelect = 0;

    private bool zoomToggle;
    private double zoom = 1, sX = 0, sY = 0;

    public vec3 pint = new vec3(0.0, 1.0, 0.0);

    private bool isLinesVisible = true;

    // PUBLIC for parity (FormHeadAche.cs L703 / L705).
    public double iE = 0, iN = 0;
    public List<int> crossings = new List<int>(1);

    // [XPLAT] nudSetDistance was a WinForms NudlessNumericUpDown (Minimum 0, Maximum 200, DecimalPlaces 1,
    // ReadOnly). It is reproduced as a click-to-keypad Button whose backing value lives here and is rendered
    // into the button Content via UpdateNudDisplay(). Min/Max are read verbatim from the designer block.
    private const double nudSetDistanceMinimum = 0;
    private const double nudSetDistanceMaximum = 200;
    private const int nudSetDistanceDecimals = 1;
    private double _nudSetDistanceValue;

    // [XPLAT] one InvariantCulture handle for every numeric<->string conversion (AAP §0.6.5).
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time previewer
    /// only. It renders the static markup, wires no behaviour and leaves the injected collaborators null, so
    /// the <see cref="OnOpened"/>/<see cref="OnClosing"/> guards treat a preview/loader instance as a no-op.
    /// The running application always uses the injected overload.
    /// </summary>
    public FormHeadAcheView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the headland builder bound to the supplied collaborators.
    /// </summary>
    /// <param name="hdl">Head lines model (was <c>mf.hdl</c>).</param>
    /// <param name="bnd">Boundary model (was <c>mf.bnd</c>).</param>
    /// <param name="curve">Curve model — only <c>desList</c> is used here (was <c>mf.curve</c>).</param>
    /// <param name="tool">Tool model providing <c>width</c>/<c>overlap</c> (was <c>mf.tool</c>).</param>
    /// <param name="viewport">The cross-platform GL viewport adapter that hosts the field surface.</param>
    /// <param name="getMaxFieldDistance">Was <c>mf.maxFieldDistance</c>.</param>
    /// <param name="getFieldCenterX">Was <c>mf.fieldCenterX</c>.</param>
    /// <param name="getFieldCenterY">Was <c>mf.fieldCenterY</c>.</param>
    /// <param name="getUnitsFtM">Was <c>mf.unitsFtM</c>.</param>
    /// <param name="getM2FtOrM">Was <c>mf.m2FtOrM</c>.</param>
    /// <param name="getFtOrMtoM">Was <c>mf.ftOrMtoM</c>.</param>
    /// <param name="calculateMinMax">Was <c>mf.CalculateMinMax()</c>.</param>
    /// <param name="fileLoadHeadLines">Was <c>mf.FileLoadHeadLines()</c>.</param>
    /// <param name="fileSaveHeadLines">Was <c>mf.FileSaveHeadLines()</c>.</param>
    /// <param name="fileSaveHeadland">Was <c>mf.FileSaveHeadland()</c>.</param>
    /// <param name="setHydLiftOn">Was <c>mf.vehicle.isHydLiftOn = value</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any reference collaborator is null.</exception>
    public FormHeadAcheView(
        CHeadLine hdl,
        CBoundary bnd,
        CABCurve curve,
        CTool tool,
        // [XPLAT] The agent spec listed a "CGLM glm" parameter, but glm is a STATIC class (no CGLM type
        // exists), so the static glm.twoPI / glm.PIBy2 / glm.Distance are used directly and no instance is
        // injected — exactly as the agent spec permitted ("(or static)").
        AgOpenGPS.Controls.AvaloniaGeoViewport viewport,
        Func<double> getMaxFieldDistance,
        Func<double> getFieldCenterX,
        Func<double> getFieldCenterY,
        Func<string> getUnitsFtM,
        Func<double> getM2FtOrM,
        Func<double> getFtOrMtoM,
        Action calculateMinMax,
        Action fileLoadHeadLines,
        Action fileSaveHeadLines,
        Action fileSaveHeadland,
        Action<bool> setHydLiftOn)
        : this()
    {
        // [XPLAT] DI seam replaces "mf = callingForm as FormGPS;" — fail fast on a missing wiring rather
        // than NRE deep inside a handler.
        this.hdl = hdl ?? throw new ArgumentNullException(nameof(hdl));
        this.bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        this.curve = curve ?? throw new ArgumentNullException(nameof(curve));
        this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        this.getMaxFieldDistance = getMaxFieldDistance ?? throw new ArgumentNullException(nameof(getMaxFieldDistance));
        this.getFieldCenterX = getFieldCenterX ?? throw new ArgumentNullException(nameof(getFieldCenterX));
        this.getFieldCenterY = getFieldCenterY ?? throw new ArgumentNullException(nameof(getFieldCenterY));
        this.getUnitsFtM = getUnitsFtM ?? throw new ArgumentNullException(nameof(getUnitsFtM));
        this.getM2FtOrM = getM2FtOrM ?? throw new ArgumentNullException(nameof(getM2FtOrM));
        this.getFtOrMtoM = getFtOrMtoM ?? throw new ArgumentNullException(nameof(getFtOrMtoM));
        this.calculateMinMax = calculateMinMax ?? throw new ArgumentNullException(nameof(calculateMinMax));
        this.fileLoadHeadLines = fileLoadHeadLines ?? throw new ArgumentNullException(nameof(fileLoadHeadLines));
        this.fileSaveHeadLines = fileSaveHeadLines ?? throw new ArgumentNullException(nameof(fileSaveHeadLines));
        this.fileSaveHeadland = fileSaveHeadland ?? throw new ArgumentNullException(nameof(fileSaveHeadland));
        this.setHydLiftOn = setHydLiftOn ?? throw new ArgumentNullException(nameof(setHydLiftOn));

        // [XPLAT] The former oglSelf_Paint body is supplied to the host as its render callback. The host
        // (AvaloniaGeoViewport.HandleOpenGlRender) invokes RenderAction between BeginPaint()/EndPaint();
        // this view therefore NEVER calls BeginPaint/EndPaint itself.
        _viewport.RenderAction = RenderViewport;

        // [XPLAT] Host the viewport's exposed Control in the XAML 'ViewportHost' Border (was the oglSelf surface).
        ViewportHost.Child = _viewport.View;

        // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown was
        // wired in the designer; no Move/Up/Wheel).
        _viewport.View.PointerPressed += oglSelf_MouseDown;

        // [XPLAT] WinForms FormHeadAche_ResizeEnd kept the GL surface square; subscribe once here so the host
        // Border is re-clamped square on every resize (the projection itself is rebuilt per-frame).
        this.SizeChanged += FormHeadAche_ResizeEnd;

        // [XPLAT] WinForms Timer (Interval 500, Enabled at design time) -> DispatcherTimer started here and
        // stopped in OnClosing.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += timer1_Tick;
        _timer.Start();

        // Wire the controls imperatively (the .axaml declares no Click=/event attributes — the unanimous
        // convention of the sibling guidance views). rbtnCurve / rbtnLine carry NO handler: their IsChecked
        // is read inside oglSelf_MouseDown to choose AB vs Curve clip lines.
        btnBLength.Click += btnBLength_Click;
        btnBShrink.Click += btnBShrink_Click;
        btnALength.Click += btnALength_Click;
        btnAShrink.Click += btnAShrink_Click;
        btnCancelTouch.Click += btnCancelTouch_Click;
        cboxIsSectionControlled.Click += cboxIsSectionControlled_Click;
        nudSetDistance.Click += nudSetDistance_Click;
        cboxToolWidths.SelectionChanged += cboxToolWidths_SelectedIndexChanged;
        btnBndLoop.Click += btnBndLoop_Click;
        btnDeleteHeadland.Click += btnDeleteHeadland_Click;
        cboxIsZoom.IsCheckedChanged += cboxIsZoom_CheckedChanged;
        btnCycleBackward.Click += btnCycleBackward_Click;
        btnCycleForward.Click += btnCycleForward_Click;
        btnHeadlandOff.Click += btnHeadlandOff_Click;
        btnDeleteCurve.Click += btnDeleteCurve_Click;
        btnExit.Click += btnExit_Click;

        // [XPLAT] FormHeadAche ctor ended with "mf.CalculateMinMax();" — preserved as the last ctor step.
        calculateMinMax();
    }

    // ===================================================================================================
    //  Lifecycle — was FormHeadLine_Load / FormHeadLine_FormClosing. OnLoaded/OnClosing/OnClosed mirror the
    //  proven sibling GL-host convention (FormBndToolView) and are functionally identical to the spec's
    //  "OnOpened/OnClosing": run the load body once after the controls/host exist, persist on close.
    // ===================================================================================================

    /// <inheritdoc />
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // A loader/previewer instance has no model — stay completely inert.
        if (hdl == null) return;

        // [XPLAT] FormHeadLine_Load (L44-81). The source first set the literal "1: Set distance, 2: Tap Build,
        // 3: Create Clip Lines" caption, but it is immediately overwritten by gStr.gsHeadlandForm at the end of
        // Load — so only the final translated caption is applied here (per the agent spec).
        hdl.idx = -1;

        fileLoadHeadLines();
        FixLabelsCurve();

        // [XPLAT] lblToolWidth: ToString("N1") -> ToString("N1", InvariantCulture) (AAP §0.6.5).
        lblToolWidth.Text = "( " + getUnitsFtM() + " )      Tool: "
            + ((tool.width - tool.overlap) * getM2FtOrM()).ToString("N1", CI) + getUnitsFtM() + " ";

        bnd.bndList[0].hdLine?.Clear();

        // [XPLAT] cboxIsSectionControlled.Checked + .Image -> IsChecked + swapped Content image. Assigning
        // IsChecked in code does NOT raise Click in Avalonia, so the glyph is applied explicitly.
        bool sectionControlled = AgOpenGPS.Properties.ToolSettings.Default.setHeadland_isSectionControlled;
        cboxIsSectionControlled.IsChecked = sectionControlled;
        SetSectionControlledGlyph(sectionControlled);

        // [XPLAT] Size = Settings.setWindow_HeadAcheSize (System.Drawing.Size). The WinForms work-area
        // centering (Screen.FromControl / ScreenHelper.IsOnScreen) is replaced by
        // WindowStartupLocation="CenterOwner" in the .axaml.
        System.Drawing.Size savedSize = AgOpenGPS.Properties.Settings.Default.setWindow_HeadAcheSize;
        if (savedSize.Width > 0 && savedSize.Height > 0)
        {
            Width = savedSize.Width;
            Height = savedSize.Height;
        }

        // [XPLAT] FormHeadAche_ResizeEnd projection set-up runs per-frame in RenderViewport (it reads the live
        // ViewportSize); here we only clamp the GL surface square once now (subsequent resizes re-clamp via the
        // SizeChanged subscription wired in the constructor).
        ClampViewportSquare();

        //translate
        this.Title = gStr.gsHeadlandForm;
        btnBndLoopText.Text = gStr.gsBuild;
        btnDeleteHeadlandText.Text = gStr.gsReset;
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // [XPLAT] FormHeadLine_FormClosing (L83-95).
        if (hdl != null)
        {
            fileSaveHeadLines();

            hdl.idx = (hdl.tracksArr.Count > 0) ? 0 : -1;

            // [XPLAT] Settings.setWindow_HeadAcheSize = Size -> System.Drawing.Size from the current bounds.
            double w = Width, h = Height;
            if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
            {
                AgOpenGPS.Properties.Settings.Default.setWindow_HeadAcheSize =
                    new System.Drawing.Size((int)w, (int)h);
                AgOpenGPS.Properties.Settings.Default.Save();
            }
        }

        // [XPLAT] WinForms timer1 stopped implicitly on dispose -> stop the DispatcherTimer explicitly.
        _timer?.Stop();

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // [XPLAT] Release the timer and render callback so nothing keeps ticking or drawing after the dialog
        // is gone (Avalonia does not dispose DispatcherTimers with the window).
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

    // [XPLAT] WinForms FormHeadAche_ResizeEnd (L97-125): forced Width = Height*4/3, made oglSelf square
    // (Height-50) and rebuilt the perspective. The cross-platform window is freely resizable; this handler
    // preserves only the invariant the aspect-1.0 perspective depends on — a SQUARE GL surface.
    private void FormHeadAche_ResizeEnd(object sender, SizeChangedEventArgs e)
    {
        ClampViewportSquare();
    }

    /// <summary>
    /// [XPLAT] Sizes the GL host Border square within the available client area. 'side' is derived from the
    /// window <see cref="TopLevel.ClientSize"/> (NOT the Border's own bounds), so assigning the Border size
    /// cannot feed back through SizeChanged and oscillate.
    /// </summary>
    private void ClampViewportSquare()
    {
        if (_viewport == null) return;

        Size client = this.ClientSize;
        double availW = client.Width - tlp1.Width - 8.0;   // command grid is fixed at tlp1.Width; minus margins
        double availH = client.Height;
        double side = Math.Min(availW, availH) - 4.0;       // Border Margin=2 on each side
        if (double.IsNaN(side) || side < 1.0) return;

        // was oglSelf.Left = 2 / oglSelf.Top = 2 (top-left of the form).
        ViewportHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        ViewportHost.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        ViewportHost.Width = side;
        ViewportHost.Height = side;

        _viewport.RequestRender();
    }

    // [XPLAT] FixLabelsCurve() is an EMPTY method in the source (FormHeadAche.cs L127-129). Preserved as a
    // no-op for 1:1 fidelity because several handlers call it.
    private void FixLabelsCurve()
    {
    }

    // [XPLAT] timer1_Tick (L680-683) only refreshed the GL surface (oglSelf.Refresh()).
    private void timer1_Tick(object sender, EventArgs e)
    {
        _viewport?.RequestRender();
    }

    // ===================================================================================================
    //  Small UI helpers (no WinForms analog body — these adapt WinForms idioms to Avalonia).
    // ===================================================================================================

    // [XPLAT] nudSetDistance was a read-only NumericUpDown (DecimalPlaces=1); here it is a click-to-keypad
    // Button whose Content renders the backing value. InvariantCulture per AAP §0.6.5.
    private void UpdateNudDisplay()
    {
        nudSetDistance.Content = _nudSetDistanceValue.ToString("F" + nudSetDistanceDecimals.ToString(CI), CI);
    }

    // [XPLAT] cboxIsSectionControlled glyph swap (was cboxIsSectionControlled.Image = Properties.Resources.*).
    // The XAML declares the ToggleButton Content as a sized <Image>; swapping its Source keeps the 60x60 layout.
    private void SetSectionControlledGlyph(bool on)
    {
        if (cboxIsSectionControlled.Content is Image img)
        {
            img.Source = LoadBitmap(on ? "HeadlandSectionOn.png" : "HeadlandSectionOff.png");
        }
    }

    // [XPLAT] Packaged-asset bitmap loader (was Properties.Resources.<name>); resolves the same btnImages
    // glyphs the .axaml references through the avares:// scheme.
    private static Bitmap LoadBitmap(string fileName)
    {
        return new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
    }

    // ===================================================================================================
    //  Cycle / delete / exit / distance / length handlers — ported 1:1 from FormHeadAche.cs.
    // ===================================================================================================

    // [XPLAT] btnCycleForward_Click (L131-146).
    private void btnCycleForward_Click(object sender, RoutedEventArgs e)
    {
        bnd.bndList[0].hdLine?.Clear();

        if (hdl.tracksArr.Count > 0)
        {
            hdl.idx++;
            if (hdl.idx > (hdl.tracksArr.Count - 1)) hdl.idx = 0;
        }
        else
        {
            hdl.idx = -1;
        }

        FixLabelsCurve();
    }

    // [XPLAT] btnCycleBackward_Click (L148-163).
    private void btnCycleBackward_Click(object sender, RoutedEventArgs e)
    {
        bnd.bndList[0].hdLine?.Clear();

        if (hdl.tracksArr.Count > 0)
        {
            hdl.idx--;
            if (hdl.idx < 0) hdl.idx = (hdl.tracksArr.Count - 1);
        }
        else
        {
            hdl.idx = -1;
        }

        FixLabelsCurve();
    }

    // [XPLAT] btnDeleteCurve_Click (L165-185). The commented "hdLine?.Clear()" is preserved verbatim.
    private void btnDeleteCurve_Click(object sender, RoutedEventArgs e)
    {
        //mf.bnd.bndList[0].hdLine?.Clear();

        if (hdl.tracksArr.Count > 0 && hdl.idx > -1)
        {
            hdl.tracksArr.RemoveAt(hdl.idx);
            hdl.idx--;
        }

        if (hdl.tracksArr.Count > 0)
        {
            if (hdl.idx == -1)
            {
                hdl.idx++;
            }
        }
        else hdl.idx = -1;

        FixLabelsCurve();
    }

    // [XPLAT] btnExit_Click (L685-694). cboxIsSectionControlled.Checked -> IsChecked == true.
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        fileSaveHeadLines();
        //does headland control sections
        bnd.isSectionControlledByHeadland = cboxIsSectionControlled.IsChecked == true;
        AgOpenGPS.Properties.ToolSettings.Default.setHeadland_isSectionControlled = cboxIsSectionControlled.IsChecked == true;
        AgOpenGPS.Properties.ToolSettings.Default.Save();

        Close();
    }

    // [XPLAT] nudSetDistance_Click (L696-700): ((NudlessNumericUpDown)sender).ShowKeypad(this) -> FormNumeric
    // modal (ctor (min,max,current); ShowDialog<bool>; ReturnValue). Min/Max are read verbatim from the
    // designer nudSetDistance block (Minimum 0, Maximum 200). async void so the dialog can be awaited.
    private async void nudSetDistance_Click(object sender, RoutedEventArgs e)
    {
        var f = new FormNumeric(nudSetDistanceMinimum, nudSetDistanceMaximum, _nudSetDistanceValue);
        if (await f.ShowDialog<bool>(this))
        {
            _nudSetDistanceValue = f.ReturnValue;
            UpdateNudDisplay();
        }
        btnExit.Focus();
    }

    // [XPLAT] btnDeleteHeadland_Click (L707-720). The commented boundary-copy block is intentionally NOT added.
    private void btnDeleteHeadland_Click(object sender, RoutedEventArgs e)
    {
        start = 99999; end = 99999;
        isA = true;
        hdl.desList?.Clear();
        bnd.bndList[0].hdLine?.Clear();

        //int ptCount = mf.bnd.bndList[0].fenceLine.Count;

        //for (int i = 0; i < ptCount; i++)
        //{
        //    mf.bnd.bndList[0].hdLine.Add(new vec3(mf.bnd.bndList[0].fenceLine[i]));
        //}
    }

    // [XPLAT] cboxToolWidths_SelectedIndexChanged (L856-859). nudSetDistance.Value (decimal) -> backing double;
    // SelectedIndexChanged -> SelectionChanged.
    private void cboxToolWidths_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
    {
        _nudSetDistanceValue = (Math.Round((tool.width - tool.overlap) * cboxToolWidths.SelectedIndex, 1)) * getM2FtOrM();
        UpdateNudDisplay();
    }

    // [XPLAT] btnHeadlandOff_Click (L861-868). mf.vehicle.isHydLiftOn = false -> setHydLiftOn(false).
    private void btnHeadlandOff_Click(object sender, RoutedEventArgs e)
    {
        bnd.bndList[0].hdLine?.Clear();
        fileSaveHeadland();
        bnd.isHeadlandOn = false;
        setHydLiftOn(false);
        Close();
    }

    // [XPLAT] btnBLength_Click (L870-884). Appends 10 points off the ORIGINAL last point (ptCnt captured
    // BEFORE the loop) along its heading. VERBATIM.
    private void btnBLength_Click(object sender, RoutedEventArgs e)
    {
        if (hdl.idx > -1)
        {
            int ptCnt = hdl.tracksArr[hdl.idx].trackPts.Count - 1;

            for (int i = 1; i < 10; i++)
            {
                vec3 pt = new vec3(hdl.tracksArr[hdl.idx].trackPts[ptCnt]);
                pt.easting += (Math.Sin(pt.heading) * i);
                pt.northing += (Math.Cos(pt.heading) * i);
                hdl.tracksArr[hdl.idx].trackPts.Add(pt);
            }
        }
    }

    // [XPLAT] btnBShrink_Click (L886-893).
    private void btnBShrink_Click(object sender, RoutedEventArgs e)
    {
        if (hdl.idx > -1)
        {
            if (hdl.tracksArr[hdl.idx].trackPts.Count > 8)
                hdl.tracksArr[hdl.idx].trackPts.RemoveRange(hdl.tracksArr[hdl.idx].trackPts.Count - 5, 5);
        }
    }

    // [XPLAT] btnALength_Click (L895-910). Prepends 10 points off the original first point along its heading.
    // NOTE: the local "vec3 start" deliberately shadows the int 'start' field exactly as the source does.
    private void btnALength_Click(object sender, RoutedEventArgs e)
    {
        if (hdl.idx > -1)
        {
            //and the beginning
            vec3 start = new vec3(hdl.tracksArr[hdl.idx].trackPts[0]);

            for (int i = 1; i < 10; i++)
            {
                vec3 pt = new vec3(start);
                pt.easting -= (Math.Sin(pt.heading) * i);
                pt.northing -= (Math.Cos(pt.heading) * i);
                hdl.tracksArr[hdl.idx].trackPts.Insert(0, pt);
            }
        }
    }

    // [XPLAT] btnAShrink_Click (L912-919).
    private void btnAShrink_Click(object sender, RoutedEventArgs e)
    {
        if (hdl.idx > -1)
        {
            if (hdl.tracksArr[hdl.idx].trackPts.Count > 8)
                hdl.tracksArr[hdl.idx].trackPts.RemoveRange(0, 5);
        }
    }

    // [XPLAT] btnCancelTouch_Click (L921-933).
    private void btnCancelTouch_Click(object sender, RoutedEventArgs e)
    {
        //update the arrays
        start = 99999; end = 99999;
        isA = true;
        FixLabelsCurve();
        curve.desList?.Clear();
        zoom = 1;
        sX = 0;
        sY = 0;
        zoomToggle = false;
        btnExit.Focus();
    }

    // [XPLAT] cboxIsSectionControlled_Click (L935-939). Image swap via the shared glyph helper.
    private void cboxIsSectionControlled_Click(object sender, RoutedEventArgs e)
    {
        SetSectionControlledGlyph(cboxIsSectionControlled.IsChecked == true);
    }

    // [XPLAT] cboxIsZoom_CheckedChanged (L941-944).
    private void cboxIsZoom_CheckedChanged(object sender, RoutedEventArgs e)
    {
        zoomToggle = false;
    }

    // ===================================================================================================
    //  btnBndLoop_Click — the headland-assembly (FormHeadAche.cs L722-854). BEHAVIOR-FROZEN, ported VERBATIM:
    //  sort tracks by a_point, save, the adjacent-line intersection scan (with the `goto again;`/label
    //  preserved — C# permits goto in async methods and no await lies between the goto and its label), the
    //  crossings-count guard, slicing each track segment into bndList[0].hdLine, the heading recompute +
    //  delta>0.005 simplification, then FileSaveHeadland(). Each FormDialog.Show(title,msg,Error) becomes an
    //  awaited FormDialogView.ShowAsync(...) immediately followed by `return` (preserving the synchronous
    //  control flow); the Log.EventWriter diagnostics are preserved exactly. Handler is async void to await.
    // ===================================================================================================
    private async void btnBndLoop_Click(object sender, RoutedEventArgs e)
    {
        //sort the lines
        hdl.tracksArr.Sort((p, q) => p.a_point.CompareTo(q.a_point));
        fileSaveHeadLines();

        hdl.idx = -1;

        //build the headland
        bnd.bndList[0].hdLine?.Clear();

        // [XPLAT] removed dead local "int numOfLines = hdl.tracksArr.Count;" (assigned, never read) so the
        // Release TreatWarningsAsErrors build stays warning-clean. Behaviour is unchanged — the loops read
        // hdl.tracksArr.Count directly.
        int nextLine = 0;
        crossings.Clear();

        int isStart = 0;

        for (int lineNum = 0; lineNum < hdl.tracksArr.Count; lineNum++)
        {
            nextLine = lineNum - 1;
            if (nextLine < 0) nextLine = hdl.tracksArr.Count - 1;

            if (nextLine == lineNum)
            {
                await FormDialogView.ShowAsync("Create Error", "Is there maybe only 1 line?", DialogSeverity.Error, this);
                Log.EventWriter("Headache, Only 1 Line");

                return;
            }

            for (int i = 0; i < hdl.tracksArr[lineNum].trackPts.Count - 2; i++)
            {
                GeoLineSegment headPathSegment = hdl.tracksArr[lineNum].GetHeadPathSegment(i);
                for (int k = 0; k < hdl.tracksArr[nextLine].trackPts.Count - 2; k++)
                {
                    GeoLineSegment otherSegment = hdl.tracksArr[nextLine].GetHeadPathSegment(k);
                    GeoCoord? intersectionPoint = headPathSegment.IntersectionPoint(otherSegment);
                    if (intersectionPoint.HasValue)
                    {
                        if (isStart == 0) i++;
                        crossings.Add(i);
                        isStart++;
                        if (isStart == 2) goto again;
                        nextLine = lineNum + 1;

                        if (nextLine > hdl.tracksArr.Count - 1) nextLine = 0;
                    }
                }
            }

        again:
            isStart = 0;
        }

        if (crossings.Count != hdl.tracksArr.Count * 2)
        {
            await FormDialogView.ShowAsync("Crossings Error", "Make sure all ends cross and only once", DialogSeverity.Error, this);
            Log.EventWriter("Headache, All ends cross and only once");
            bnd.bndList[0].hdLine?.Clear();
            return;
        }

        for (int i = 0; i < hdl.tracksArr.Count; i++)
        {
            int low = crossings[i * 2];
            int high = crossings[i * 2 + 1];
            for (int k = low; k < high; k++)
            {
                bnd.bndList[0].hdLine.Add(hdl.tracksArr[i].trackPts[k]);
            }
        }

        // [XPLAT] the source's commented desList re-fill block (L794-811) is intentionally omitted.

        vec3[] hdArr;

        if (bnd.bndList[0].hdLine.Count > 0)
        {
            hdArr = new vec3[bnd.bndList[0].hdLine.Count];
            bnd.bndList[0].hdLine.CopyTo(hdArr);
            bnd.bndList[0].hdLine?.Clear();
        }
        else
        {
            bnd.bndList[0].hdLine?.Clear();
            return;
        }

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

        fileSaveHeadland();
    }

    // ===================================================================================================
    //  oglSelf_MouseDown -> PointerPressed (FormHeadAche.cs L187-526). PATTERN B: the CUSTOM pixel->world
    //  camera math is preserved EXACTLY (NOT the host's GetGeoCoord). Only the input source changes
    //  (oglSelf.PointToClient(Cursor.Position) -> e.GetPosition(_viewport.View)) and the WinForms control
    //  size (oglSelf.Width) becomes the hosted GL control's laid-out width. async void so the "Start Point =
    //  End Point" error dialog can be awaited; that await is immediately followed by `return`, and the rest of
    //  the method is fully synchronous.
    // ===================================================================================================
    private async void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
    {
        // [XPLAT] was Point pt = oglSelf.PointToClient(Cursor.Position) — position relative to the GL control.
        var p = e.GetPosition(_viewport.View);

        // [XPLAT] was int wid = oglSelf.Width (square pixel surface). The hosted GL control's laid-out width is
        // in the SAME units as e.GetPosition, so the dimensionless ratios below are preserved. Defensive guard:
        // a genuine pointer-press only occurs after layout, so this never fires in normal use but prevents a
        // divide-by-zero NaN from polluting domain state if invoked before layout.
        double wid = _viewport.View.Bounds.Width;
        if (wid <= 0) return;
        double halfWid = wid / 2;
        double scale = wid * 0.903;

        if (cboxIsZoom.IsChecked == true && !zoomToggle)
        {
            sX = ((halfWid - p.X) / wid) * 1.1;
            sY = ((halfWid - p.Y) / -wid) * 1.1;
            zoom = 0.1;
            zoomToggle = true;
            return;
        }

        zoomToggle = false;

        //Convert to Origin in the center of window, 800 pixels
        fixPtX = p.X - halfWid;
        fixPtY = (wid - p.Y - halfWid);

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

        zoom = 1;
        sX = 0;
        sY = 0;

        bnd.bndList[0].hdLine?.Clear();
        hdl.idx = -1;

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

            if (start == end)
            {
                start = 99999; end = 99999;
                await FormDialogView.ShowAsync("Line Error", "Start Point = End Point ", DialogSeverity.Error, this);
                return;
            }

            //build the lines
            if (rbtnCurve.IsChecked == true)
            {
                hdl.tracksArr.Add(new CHeadPath());
                hdl.idx = hdl.tracksArr.Count - 1;

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

                hdl.tracksArr[hdl.idx].a_point = start;
                hdl.tracksArr[hdl.idx].trackPts?.Clear();

                if (start < end)
                {
                    for (int i = start; i <= end; i++)
                    {
                        //calculate the point inside the boundary
                        hdl.tracksArr[hdl.idx].trackPts.Add(new vec3(bnd.bndList[bndSelect].fenceLine[i]));

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
                        hdl.tracksArr[hdl.idx].trackPts.Add(new vec3(bnd.bndList[bndSelect].fenceLine[i]));

                        if (isLoop && i == 0)
                        {
                            i = bnd.bndList[bndSelect].fenceLine.Count - 1;
                            isLoop = false;
                            end = limit;
                        }
                    }
                }

                //who knows which way it actually goes
                CABCurve.CalculateHeadings(ref hdl.tracksArr[hdl.idx].trackPts);

                int ptCnt = hdl.tracksArr[hdl.idx].trackPts.Count - 1;

                for (int i = 1; i < 30; i++)
                {
                    vec3 pnt = new vec3(hdl.tracksArr[hdl.idx].trackPts[ptCnt]);
                    pnt.easting += (Math.Sin(pnt.heading) * i);
                    pnt.northing += (Math.Cos(pnt.heading) * i);
                    hdl.tracksArr[hdl.idx].trackPts.Add(pnt);
                }

                vec3 stat = new vec3(hdl.tracksArr[hdl.idx].trackPts[0]);

                for (int i = 1; i < 30; i++)
                {
                    vec3 pnt = new vec3(stat);
                    pnt.easting -= (Math.Sin(pnt.heading) * i);
                    pnt.northing -= (Math.Cos(pnt.heading) * i);
                    hdl.tracksArr[hdl.idx].trackPts.Insert(0, pnt);
                }

                //create a name
                hdl.tracksArr[hdl.idx].name = hdl.idx.ToString() + " Cu " + DateTime.Now.ToString("mm:ss", CultureInfo.InvariantCulture);

                hdl.tracksArr[hdl.idx].moveDistance = 0;

                hdl.tracksArr[hdl.idx].mode = (int)TrackMode.Curve;

                fileSaveHeadLines();

                //update the arrays
                start = 99999; end = 99999;

                FixLabelsCurve();
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

                if (hdl.idx < hdl.tracksArr.Count - 1)
                {
                    hdl.idx++;
                    hdl.tracksArr.Insert(hdl.idx, new CHeadPath());
                }
                else
                {
                    hdl.tracksArr.Add(new CHeadPath());
                    hdl.idx = hdl.tracksArr.Count - 1;
                }

                hdl.tracksArr[hdl.idx].a_point = start;
                hdl.tracksArr[hdl.idx].trackPts?.Clear();

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
                    hdl.tracksArr[hdl.idx].trackPts.Add(ptC);
                }

                int ptCnt = hdl.tracksArr[hdl.idx].trackPts.Count - 1;

                for (int i = 1; i < 30; i++)
                {
                    vec3 pnt = new vec3(hdl.tracksArr[hdl.idx].trackPts[ptCnt]);
                    pnt.easting += (Math.Sin(pnt.heading) * i);
                    pnt.northing += (Math.Cos(pnt.heading) * i);
                    hdl.tracksArr[hdl.idx].trackPts.Add(pnt);
                }

                vec3 stat = new vec3(hdl.tracksArr[hdl.idx].trackPts[0]);

                for (int i = 1; i < 30; i++)
                {
                    vec3 pnt = new vec3(stat);
                    pnt.easting -= (Math.Sin(pnt.heading) * i);
                    pnt.northing -= (Math.Cos(pnt.heading) * i);
                    hdl.tracksArr[hdl.idx].trackPts.Insert(0, pnt);
                }

                //create a name
                hdl.tracksArr[hdl.idx].name = hdl.idx.ToString() + " AB " + DateTime.Now.ToString("hh:mm:ss", CultureInfo.InvariantCulture);

                hdl.tracksArr[hdl.idx].moveDistance = 0;

                hdl.tracksArr[hdl.idx].mode = (int)TrackMode.AB;

                fileSaveHeadLines();

                FixLabelsCurve();
                start = 99999; end = 99999;
            }

            //mf.bnd.bndList[0].hdLine?.Clear();
            hdl.desList?.Clear();

            if (hdl.tracksArr.Count < 1 || hdl.idx == -1) return;

            double distAway = _nudSetDistanceValue * getFtOrMtoM();
            hdl.tracksArr[hdl.idx].moveDistance += distAway;

            double distSqAway = (distAway * distAway) - 0.01;
            vec3 point;

            int refCount = hdl.tracksArr[hdl.idx].trackPts.Count;
            for (int i = 0; i < refCount; i++)
            {
                point = new vec3(
                hdl.tracksArr[hdl.idx].trackPts[i].easting - (Math.Sin(glm.PIBy2 + hdl.tracksArr[hdl.idx].trackPts[i].heading) * distAway),
                hdl.tracksArr[hdl.idx].trackPts[i].northing - (Math.Cos(glm.PIBy2 + hdl.tracksArr[hdl.idx].trackPts[i].heading) * distAway),
                hdl.tracksArr[hdl.idx].trackPts[i].heading);
                bool Add = true;

                for (int t = 0; t < refCount; t++)
                {
                    double dist = ((point.easting - hdl.tracksArr[hdl.idx].trackPts[t].easting) * (point.easting - hdl.tracksArr[hdl.idx].trackPts[t].easting))
                        + ((point.northing - hdl.tracksArr[hdl.idx].trackPts[t].northing) * (point.northing - hdl.tracksArr[hdl.idx].trackPts[t].northing));
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

            hdl.tracksArr[hdl.idx].trackPts.Clear();

            for (int i = 0; i < hdl.desList.Count; i++)
            {
                hdl.tracksArr[hdl.idx].trackPts.Add(new vec3(hdl.desList[i]));
            }

            hdl.desList?.Clear();
        }

        // [XPLAT] refresh the GL surface after the pick/build (was implicit via oglSelf invalidation).
        _viewport.RequestRender();
    }

    // ===================================================================================================
    //  RenderViewport — supplied to the host as its RenderAction (FormHeadAche.cs oglSelf_Paint L528-570,
    //  plus the projection from oglSelf_Resize L572-585 and the one-time GL state from oglSelf_Load L946-953).
    //  The host (AvaloniaGeoViewport) invokes this between BeginPaint()/EndPaint(); this view performs NO
    //  BeginPaint/EndPaint/MakeCurrent/SwapBuffers. Because FormHeadAche uses a CUSTOM perspective camera (not
    //  the host's default bounding-box projection), the projection + modelview are (re)installed here EVERY
    //  frame so they win over whatever BeginPaint set. DIRECT immediate-mode GL only — NO Core Visuals helpers.
    //
    // [XPLAT] RISK: immediate-mode / fixed-function GL (GL.Begin/Vertex3, the matrix stack). Requires a
    // desktop-GL compatibility context from AvaloniaGeoViewport (NOT GLES/ANGLE) — the dominant feasibility
    // risk per AAP §0.6.2, tracked in MIGRATION_DOCS/PARITY_REPORT.md. When the host gates rendering (a
    // GLES/ANGLE context is detected) this callback is not invoked and a cleared surface is presented instead.
    // ===================================================================================================
    private void RenderViewport()
    {
        // [XPLAT] one-time GL state — was oglSelf_Load (L946-953). MakeCurrent() is omitted (the host owns
        // context currency); the remaining state is applied once on the GL thread on the first render.
        if (!_glInited)
        {
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ClearColor(0.22f, 0.22f, 0.22f, 1.0f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _glInited = true;
        }

        // [XPLAT] projection — was oglSelf_Resize (L572-585). Rebuilt each frame from the live framebuffer-pixel
        // ViewportSize so this aspect-1.0 perspective overrides the host's default bounding-box projection.
        XyDelta vp = _viewport.ViewportSize;
        int w = (int)vp.DeltaX;
        int h = (int)vp.DeltaY;
        GL.MatrixMode(MatrixMode.Projection);
        GL.LoadIdentity();

        //58 degrees view
        GL.Viewport(0, 0, w, h);
        Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, 1.0f, 1.0f, 20000);
        GL.LoadMatrix(ref mat);

        GL.MatrixMode(MatrixMode.Modelview);

        // [XPLAT] paint — was oglSelf_Paint (L528-570) minus MakeCurrent() and SwapBuffers() (the host owns
        // both). Otherwise VERBATIM.
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
        GL.LoadIdentity();                  // Reset The View

        //back the camera up
        GL.Translate(0, 0, -getMaxFieldDistance() * zoom);

        //translate to that spot in the world
        GL.Translate(-getFieldCenterX() + sX * getMaxFieldDistance(), -getFieldCenterY() + sY * getMaxFieldDistance(), 0);

        GL.LineWidth(2);

        for (int j = 0; j < bnd.bndList.Count; j++)
        {
            if (j == bndSelect)
                GL.Color3(0.8f, 0.8f, 0.8f);
            else
                GL.Color3(0.50f, 0.25f, 0.10f);

            GL.Begin(PrimitiveType.Lines);
            for (int i = 0; i < bnd.bndList[j].fenceLine.Count; i++)
            {
                GL.Vertex3(bnd.bndList[j].fenceLine[i].easting, bnd.bndList[j].fenceLine[i].northing, 0);
            }
            GL.End();
        }

        //draw the actual built lines
        //if (start == 99999 && end == 99999)
        {
            DrawBuiltLines();
        }

        DrawABTouchLine();

        GL.Disable(EnableCap.Blend);

        GL.Flush();
        // [XPLAT] no SwapBuffers — Avalonia presents the framebuffer after the render callback returns.
    }

    // [XPLAT] DrawBuiltLines (L587-656). Direct GL only. Ported VERBATIM (mf.* -> injected hdl/bnd).
    private void DrawBuiltLines()
    {
        if (isLinesVisible && hdl.tracksArr.Count > 0)
        {
            //GL.Enable(EnableCap.LineStipple);
            GL.LineStipple(1, 0x7070);
            GL.PointSize(3);

            for (int i = 0; i < hdl.tracksArr.Count; i++)
            {
                if (hdl.tracksArr[i].mode == (int)TrackMode.AB)
                {
                    GL.Color3(0.973f, 0.9f, 0.10f);
                }
                else
                {
                    GL.Color3(0.3f, 0.99f, 0.20f);
                }

                GL.Begin(PrimitiveType.Points);
                foreach (vec3 item in hdl.tracksArr[i].trackPts)
                {
                    GL.Vertex3(item.easting, item.northing, 0);
                }
                GL.End();
            }

            //GL.Disable(EnableCap.LineStipple);

            if (hdl.idx > -1)
            {
                GL.LineWidth(6);
                GL.Color3(1.0f, 0.0f, 1.0f);

                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 item in hdl.tracksArr[hdl.idx].trackPts)
                {
                    GL.Vertex3(item.easting, item.northing, 0);
                }
                GL.End();

                int cnt = hdl.tracksArr[hdl.idx].trackPts.Count - 1;
                GL.PointSize(28);
                GL.Color3(0, 0, 0);
                GL.Begin(PrimitiveType.Points);
                GL.Vertex3(hdl.tracksArr[hdl.idx].trackPts[0].easting, hdl.tracksArr[hdl.idx].trackPts[0].northing, 0);
                GL.Color3(0, 0, 0);
                GL.Vertex3(hdl.tracksArr[hdl.idx].trackPts[cnt].easting, hdl.tracksArr[hdl.idx].trackPts[cnt].northing, 0);
                GL.End();

                GL.PointSize(20);
                GL.Color3(1.0f, 0.7f, 0.35f);
                GL.Begin(PrimitiveType.Points);
                GL.Vertex3(hdl.tracksArr[hdl.idx].trackPts[0].easting, hdl.tracksArr[hdl.idx].trackPts[0].northing, 0);
                GL.Color3(0.6f, 0.75f, 0.99f);
                GL.Vertex3(hdl.tracksArr[hdl.idx].trackPts[cnt].easting, hdl.tracksArr[hdl.idx].trackPts[cnt].northing, 0);
                GL.End();
            }
        }

        GL.LineWidth(8);
        GL.Color3(0.93f, 0.899f, 0.50f);
        GL.Begin(PrimitiveType.LineStrip);

        for (int i = 0; i < bnd.bndList[0].hdLine.Count; i++)
        {
            GL.Vertex3(bnd.bndList[0].hdLine[i].easting, bnd.bndList[0].hdLine[i].northing, 0);
        }
        GL.End();
    }

    // [XPLAT] DrawABTouchLine (L658-678). Direct GL only. Ported VERBATIM (mf.* -> injected bnd).
    private void DrawABTouchLine()
    {
        GL.Color3(0.65, 0.650, 0.0);
        GL.PointSize(24);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(0, 0, 0);
        if (start != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[start].easting, bnd.bndList[bndSelect].fenceLine[start].northing, 0);
        if (end != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[end].easting, bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();

        GL.PointSize(16);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(1.0f, 0.75f, 0.350f);
        if (start != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[start].easting, bnd.bndList[bndSelect].fenceLine[start].northing, 0);

        GL.Color3(0.5f, 0.75f, 1.0f);
        if (end != 99999) GL.Vertex3(bnd.bndList[bndSelect].fenceLine[end].easting, bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();
    }
}
