// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// FormABDrawView.axaml.cs — Avalonia code-behind for the "Draw AB" boundary-tracing
// editor. This is a 1:1 behavioural-parity reimplementation of the WinForms FormABDraw
// (SourceCode/GPS/Forms/Guidance/FormABDraw.cs + FormABDraw.Designer.cs). All geometry
// and GL draw code is behaviour-frozen and ported VERBATIM (AAP §0.2.2 / §0.7.1).
//
// Rendering model: PATTERN B — raw immediate-mode OpenGL with a form-local custom camera.
// The form drives its OWN GL camera (custom zoom/sX/sY and pixel→world math), NOT
// AvaloniaGeoViewport.GetGeoCoord. The OpenGL surface and GL context are supplied by the
// hosted AvaloniaGeoViewport; this view installs its per-frame render via the host's
// RenderAction callback (the host brackets it between BeginPaint()/EndPaint()).
//
// God-object removal (AAP §0.3.2): the WinForms "mf = callingForm as FormGPS" back-reference
// is replaced by constructor-injected concrete domain objects (CTrack/CABCurve/CABLine/
// CBoundary) plus Func/Action delegates for the few remaining FormGPS reach-backs. No
// FormGPS, no new interfaces.

using System;                              // [XPLAT] Func, Action, Math, DateTime, EventArgs, ArgumentNullException, TimeSpan
using System.Collections.Generic;          // List, Dictionary
using System.Globalization;                // [XPLAT] CultureInfo.InvariantCulture (AAP §0.6.5)
using System.Threading.Tasks;              // [XPLAT] async keyboard handler
using Avalonia.Controls;                   // [XPLAT] Window, Button, TextBox, TextBlock, Image, Border, ContentControl, WindowClosingEventArgs
using Avalonia.Controls.Primitives;        // [XPLAT] ToggleButton (cboxIsVisible / cboxIsZoom)
using Avalonia.Input;                      // [XPLAT] PointerPressedEventArgs (was MouseEventArgs)
using Avalonia.Interactivity;              // [XPLAT] RoutedEventArgs (was EventArgs Click args)
using Avalonia.Media.Imaging;              // [XPLAT] Bitmap (runtime Image.Source swaps)
using Avalonia.Platform;                   // [XPLAT] AssetLoader (avares glyph loading)
using Avalonia.Threading;                  // [XPLAT] DispatcherTimer (was WinForms Timer)
using Avalonia.VisualTree;                 // [XPLAT] GetVisualRoot()/RenderScaling (DIP→pixel)
using AgOpenGPS.Controls;                  // AvaloniaGeoViewport (GL host adapter)
using AgOpenGPS.Core.Models;               // vec3/vec2 live in AgOpenGPS; GeoCoord/GeoBoundingBox/XyDelta here
using AgOpenGPS.Core.Translations;         // gStr
using AgOpenGPS.Core.Visuals;              // FenceLineVisual, VehicleDotVisual
using AgOpenGPS.Helpers;                   // GeoRefactorHelper
using AgOpenGPS.Visuals;                   // SectionsVisual
// [XPLAT] AgOpenGPS.Properties.Settings is referenced fully qualified (see OnOpened/OnClosing) so the
// sibling AgOpenGPS.Views.Settings namespace cannot shadow it; no 'using AgOpenGPS.Properties;' needed.
using OpenTK;                              // Matrix4 (perspective projection)
using OpenTK.Graphics.OpenGL;              // GL, MatrixMode, ClearBufferMask, EnableCap, PrimitiveType, CullFaceMode

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] "Draw AB" boundary-tracing editor — Avalonia parity port of WinForms <c>FormABDraw</c>.
/// Lets the operator trace AB lines and curves along field boundaries, manage the track list
/// (rename / show-hide / delete / extend), and commit or roll back the edits on close.
/// </summary>
public partial class FormABDrawView : Window
{
    // ───────────────────────────────────────────────────────────────────────────────────────
    //  Injected domain collaborators (replace the WinForms "mf" FormGPS back-reference).
    //  [XPLAT] AAP §0.3.2 — Dependency Inversion: concrete Core/Classes types + Func/Action seams.
    // ───────────────────────────────────────────────────────────────────────────────────────

    private readonly CTrack _trk;                              // [XPLAT] was mf.trk (gArr/idx) — concrete CTrack, not CTrackMethods
    private readonly CABCurve _curve;                          // [XPLAT] was mf.curve (desList / AddFirstLastPoints / isCurveValid)
    private readonly CABLine _ABLine;                          // [XPLAT] was mf.ABLine (abLength / isABValid)
    private readonly CBoundary _bnd;                           // [XPLAT] was mf.bnd (bndList / fenceLine / fenceLineEar)
    private readonly List<CPatches> _triStrip;                 // [XPLAT] was mf.triStrip (SectionsVisual.DrawSections)
    private readonly Func<vec3> _getPivotAxlePos;              // [XPLAT] was mf.pivotAxlePos (live read at paint time)
    private readonly Func<double> _getMaxFieldDistance;        // [XPLAT] was mf.maxFieldDistance
    private readonly Func<double> _getFieldCenterX;            // [XPLAT] was mf.fieldCenterX
    private readonly Func<double> _getFieldCenterY;            // [XPLAT] was mf.fieldCenterY
    private readonly Action _calculateMinMax;                  // [XPLAT] was mf.CalculateMinMax()
    private readonly Action _saveTracks;                       // [XPLAT] was mf.FileSaveTracks()
    private readonly Func<bool> _isKeyboardOn;                 // [XPLAT] was mf.isKeyboardOn
    private readonly Func<bool> _isBtnAutoSteerOn;             // [XPLAT] was mf.isBtnAutoSteerOn
    private readonly Action _turnAutoSteerOff;                 // [XPLAT] was mf.btnAutoSteer.PerformClick() (only invoked when on)
    private readonly Func<bool> _isYouTurnBtnOn;               // [XPLAT] was mf.yt.isYouTurnBtnOn
    private readonly Action _turnYouTurnOff;                   // [XPLAT] was mf.btnAutoYouTurn.PerformClick() (only invoked when on)
    private readonly Action<int> _setTwoSecondCounter;         // [XPLAT] was mf.twoSecondCounter = value
    private readonly Action<int, string, string> _timedMessageBox; // [XPLAT] was mf.TimedMessageBox(ms, title, message)

    // ───────────────────────────────────────────────────────────────────────────────────────
    //  Host + timer (Avalonia infrastructure replacing the WinForms designer wiring).
    // ───────────────────────────────────────────────────────────────────────────────────────

    private readonly AvaloniaGeoViewport _viewport;           // [XPLAT] hosts the GL surface (was the oglSelf GLControl)
    private readonly DispatcherTimer timer1;                  // [XPLAT] was WinForms Timer (Interval 500, design-time enabled)
    private bool _glSetupDone;                                // [XPLAT] one-shot GL state init replacing oglSelf_Load

    // [XPLAT] avares glyph cache for runtime Image.Source swaps (was Properties.Resources.* bitmaps).
    private static readonly Dictionary<string, Bitmap> glyphCache = new Dictionary<string, Bitmap>();

    // ───────────────────────────────────────────────────────────────────────────────────────
    //  Editor state — ported VERBATIM from FormABDraw.cs (exact initial values preserved).
    // ───────────────────────────────────────────────────────────────────────────────────────

    private double fixPtX, fixPtY;                            // [XPLAT] was "Point fixPt" — the math uses X/Y as doubles
    private bool isA = true;
    private int start = 99999, end = 99999;
    private int bndSelect = 0, originalLine;
    private bool isCancel = false;

    private bool zoomToggle;

    private int indx = -1;

    private double zoom = 1, sX = 0, sY = 0;

    public List<CTrk> gTemp = new List<CTrk>();               // PUBLIC — parity with WinForms original

    public vec3 pint = new vec3(0.0, 1.0, 0.0);              // PUBLIC — parity with WinForms original

    private bool isDrawSections = false;

    // ═══════════════════════════════════════════ Constructors ═══════════════════════════════════════════

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia XAML loader / previewer. Runs
    /// <c>InitializeComponent</c> only; the injected fields stay null so a loader/previewer
    /// instance is completely inert (lifecycle overrides and handlers guard on a null model).
    /// </summary>
    public FormABDrawView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Production constructor — replaces WinForms <c>FormABDraw(Form callingForm)</c>. Injects the
    /// concrete domain collaborators and the FormGPS reach-back delegates, builds and hosts the OpenGL
    /// viewport, wires every control handler imperatively (the .axaml declares no event attributes), creates
    /// the refresh timer, and finally recomputes the field min/max (was the WinForms ctor's trailing
    /// <c>mf.CalculateMinMax()</c>).
    /// </summary>
    /// <param name="trk">Track manager (was <c>mf.trk</c>) — owns <c>gArr</c> and the active <c>idx</c>.</param>
    /// <param name="curve">Curve builder (was <c>mf.curve</c>) — <c>desList</c>, <c>AddFirstLastPoints</c>, <c>isCurveValid</c>.</param>
    /// <param name="ABLine">AB-line state (was <c>mf.ABLine</c>) — <c>abLength</c>, <c>isABValid</c>.</param>
    /// <param name="bnd">Boundary set (was <c>mf.bnd</c>) — <c>bndList</c> with <c>fenceLine</c>/<c>fenceLineEar</c>.</param>
    /// <param name="fieldBoundingBox">Field extent used to build the GL host (was <c>mf.FieldBoundingBox</c>).</param>
    /// <param name="triStrip">Section coverage tri-strips (was <c>mf.triStrip</c>) for <c>SectionsVisual.DrawSections</c>.</param>
    /// <param name="getPivotAxlePos">Live pivot-axle position read at paint time (was <c>mf.pivotAxlePos</c>).</param>
    /// <param name="getMaxFieldDistance">Live camera distance (was <c>mf.maxFieldDistance</c>).</param>
    /// <param name="getFieldCenterX">Live field-centre easting (was <c>mf.fieldCenterX</c>).</param>
    /// <param name="getFieldCenterY">Live field-centre northing (was <c>mf.fieldCenterY</c>).</param>
    /// <param name="calculateMinMax">Recompute field min/max (was <c>mf.CalculateMinMax()</c>).</param>
    /// <param name="saveTracks">Persist the track set (was <c>mf.FileSaveTracks()</c>).</param>
    /// <param name="isKeyboardOn">On-screen keyboard preference (was <c>mf.isKeyboardOn</c>).</param>
    /// <param name="isBtnAutoSteerOn">Autosteer-engaged state (was <c>mf.isBtnAutoSteerOn</c>).</param>
    /// <param name="turnAutoSteerOff">Disengage autosteer (was <c>mf.btnAutoSteer.PerformClick()</c> while on).</param>
    /// <param name="isYouTurnBtnOn">YouTurn-engaged state (was <c>mf.yt.isYouTurnBtnOn</c>).</param>
    /// <param name="turnYouTurnOff">Disengage YouTurn (was <c>mf.btnAutoYouTurn.PerformClick()</c> while on).</param>
    /// <param name="setTwoSecondCounter">Set the 2-second refresh counter (was <c>mf.twoSecondCounter = value</c>).</param>
    /// <param name="timedMessageBox">Transient message box (was <c>mf.TimedMessageBox(ms, title, message)</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown when any injected collaborator is null.</exception>
    public FormABDrawView(
        CTrack trk,
        CABCurve curve,
        CABLine ABLine,
        CBoundary bnd,
        GeoBoundingBox fieldBoundingBox,
        List<CPatches> triStrip,
        Func<vec3> getPivotAxlePos,
        Func<double> getMaxFieldDistance,
        Func<double> getFieldCenterX,
        Func<double> getFieldCenterY,
        Action calculateMinMax,
        Action saveTracks,
        Func<bool> isKeyboardOn,
        Func<bool> isBtnAutoSteerOn,
        Action turnAutoSteerOff,
        Func<bool> isYouTurnBtnOn,
        Action turnYouTurnOff,
        Action<int> setTwoSecondCounter,
        Action<int, string, string> timedMessageBox)
        : this()
    {
        // [XPLAT] Fail fast on missing wiring rather than NRE deep inside a handler (replaces the implicit
        // "mf = callingForm as FormGPS" contract).
        _trk = trk ?? throw new ArgumentNullException(nameof(trk));
        _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        _ABLine = ABLine ?? throw new ArgumentNullException(nameof(ABLine));
        _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        _triStrip = triStrip ?? throw new ArgumentNullException(nameof(triStrip));
        _getPivotAxlePos = getPivotAxlePos ?? throw new ArgumentNullException(nameof(getPivotAxlePos));
        _getMaxFieldDistance = getMaxFieldDistance ?? throw new ArgumentNullException(nameof(getMaxFieldDistance));
        _getFieldCenterX = getFieldCenterX ?? throw new ArgumentNullException(nameof(getFieldCenterX));
        _getFieldCenterY = getFieldCenterY ?? throw new ArgumentNullException(nameof(getFieldCenterY));
        _calculateMinMax = calculateMinMax ?? throw new ArgumentNullException(nameof(calculateMinMax));
        _saveTracks = saveTracks ?? throw new ArgumentNullException(nameof(saveTracks));
        _isKeyboardOn = isKeyboardOn ?? throw new ArgumentNullException(nameof(isKeyboardOn));
        _isBtnAutoSteerOn = isBtnAutoSteerOn ?? throw new ArgumentNullException(nameof(isBtnAutoSteerOn));
        _turnAutoSteerOff = turnAutoSteerOff ?? throw new ArgumentNullException(nameof(turnAutoSteerOff));
        _isYouTurnBtnOn = isYouTurnBtnOn ?? throw new ArgumentNullException(nameof(isYouTurnBtnOn));
        _turnYouTurnOff = turnYouTurnOff ?? throw new ArgumentNullException(nameof(turnYouTurnOff));
        _setTwoSecondCounter = setTwoSecondCounter ?? throw new ArgumentNullException(nameof(setTwoSecondCounter));
        _timedMessageBox = timedMessageBox ?? throw new ArgumentNullException(nameof(timedMessageBox));

        // [XPLAT] Build the GL host from the injected bounding box (was new GeoViewport(mf.FieldBoundingBox,
        // oglSelf)). Pattern B installs its OWN per-frame camera via RenderAction; the host brackets the
        // callback between BeginPaint()/EndPaint() and owns the GL context + buffer swap.
        _viewport = new AvaloniaGeoViewport(fieldBoundingBox);
        _viewport.RenderAction = RenderViewport;
        ViewportHost.Child = _viewport.View;

        // [XPLAT] WinForms Timer (Interval 500, Enabled at design time) -> DispatcherTimer started OnOpened.
        timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer1.Tick += timer1_Tick;

        // Wire controls imperatively (the .axaml declares no Click=/GotFocus= attributes). Done only in the
        // injected ctor so a loader/previewer instance stays inert.
        btnExit.Click += btnExit_Click;
        btnCancel.Click += btnCancel_Click;
        btnCancelTouch.Click += btnCancelTouch_Click;
        btnDrawSections.Click += btnDrawSections_Click;
        btnSelectCurve.Click += btnSelectCurve_Click;
        btnSelectCurveBk.Click += btnSelectCurveBk_Click;
        btnDeleteCurve.Click += btnDeleteCurve_Click;
        btnAddTime.Click += btnAddTime_Click;
        btnALength.Click += btnALength_Click;
        btnBLength.Click += btnBLength_Click;
        btnMakeBoundaryCurve.Click += btnMakeBoundaryCurve_Click;
        btnMakeCurve.Click += BtnMakeCurve_Click;
        btnMakeABLine.Click += BtnMakeABLine_Click;

        // [XPLAT] ToggleButton.Click is USER-only — it does NOT fire on the programmatic IsChecked writes in
        // FixLabelsCurve, mirroring the WinForms cboxIsVisible_Click (Click) semantics exactly.
        cboxIsVisible.Click += cboxIsVisible_Click;
        cboxIsZoom.Click += cboxIsZoom_Click; // [XPLAT] was cboxIsZoom_CheckedChanged (resets zoomToggle)

        // [XPLAT] WinForms tboxNameCurve Enter/Leave -> Avalonia GotFocus/LostFocus.
        tboxNameCurve.GotFocus += tboxNameCurve_Enter;
        tboxNameCurve.LostFocus += tboxNameCurve_Leave;

        // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown was wired).
        _viewport.View.PointerPressed += oglSelf_MouseDown;

        _calculateMinMax(); // [XPLAT] was mf.CalculateMinMax() at the tail of the WinForms constructor
    }

    // ═══════════════════════════════════════════ Lifecycle ═══════════════════════════════════════════

    /// <summary>
    /// [XPLAT] Port of <c>FormABDraw_Load</c> (FormABDraw.cs L53-109). Sets the translated title, clones the
    /// live track set into the editable <c>gTemp</c>, seeds the selection index, primes the labels and the
    /// "draw sections" glyph, restores the persisted window size, then starts the refresh timer. The
    /// WinForms manual screen-centring + <c>IsOnScreen</c> safeguard is replaced by
    /// <c>WindowStartupLocation=CenterOwner</c> in the .axaml; the WinForms <c>ResizeEnd</c> projection set-up
    /// is replaced by per-frame projection inside <see cref="RenderViewport"/>.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // A loader/previewer instance has no model — stay completely inert.
        if (_trk == null) return;

        // translate
        Title = $"{gStr.gsABDraw} - {gStr.gsABDrawHint}";

        originalLine = _trk.idx; // [XPLAT] was mf.trk.idx

        // [XPLAT] Clone the live track set into the editable working copy. The WinForms source redundantly
        // cloned twice (L62-65 then L73-78) around an intermediate FixLabelsCurve/image set; a single clone
        // followed by the index computation and one FixLabelsCurve yields the identical final state.
        gTemp.Clear();
        foreach (var item in _trk.gArr)
        {
            gTemp.Add(new CTrk(item));
        }

        if (gTemp.Count != 0)
        {
            if (_trk.idx > -1 && _trk.idx <= gTemp.Count)
            {
                indx = _trk.idx;
            }
            else
                indx = 0;
        }

        // [XPLAT] was btnDrawSections.Image = Properties.Resources.MappingOn/MappingOff
        SetButtonImage(btnDrawSections, isDrawSections ? "MappingOn.png" : "MappingOff.png");

        FixLabelsCurve();

        cboxIsZoom.IsChecked = false; // [XPLAT] was cboxIsZoom.Checked = false
        zoomToggle = false;

        // [XPLAT] Restore the persisted window size (Settings stores a System.Drawing.Size). Guard against a
        // non-positive stored value so a corrupt setting cannot collapse the window.
        // [XPLAT] Fully qualified: the sibling AgOpenGPS.Views.Settings namespace (the Settings/ dialog
        // views) would otherwise shadow the AgOpenGPS.Properties.Settings class from inside AgOpenGPS.Views.
        System.Drawing.Size storedSize = AgOpenGPS.Properties.Settings.Default.setWindow_abDrawSize;
        if (storedSize.Width > 0 && storedSize.Height > 0)
        {
            Width = storedSize.Width;
            Height = storedSize.Height;
        }

        // [XPLAT] WinForms started the design-time-enabled Timer implicitly; start it once the window opens.
        timer1.Start();
        _viewport.RequestRender();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormABDraw_FormClosing</c> (FormABDraw.cs L111-205) — the commit/rollback. While
    /// <see cref="isCancel"/> is false the edited <c>gTemp</c> is written back to the live track set and the
    /// active line is reselected by visibility; the trailing block (invalidate guidance, bump the 2-second
    /// counter, persist the window size) always runs regardless of <see cref="isCancel"/>. Ported bug-for-bug,
    /// including the quirky unconditional <c>break</c> in the visibility scan.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A loader/previewer instance has no model — stay completely inert.
        if (_trk == null)
        {
            base.OnClosing(e);
            return;
        }

        if (!isCancel)
        {
            if (gTemp.Count == 0)
            {
                _trk.idx = -1;
                _trk.gArr.Clear();
                _saveTracks();
                if (_isBtnAutoSteerOn())
                {
                    _turnAutoSteerOff(); // [XPLAT] was mf.btnAutoSteer.PerformClick()
                    _timedMessageBox(2000, gStr.gsGuidanceStopped, "Return From Editing");
                }
                if (_isYouTurnBtnOn()) _turnYouTurnOff(); // [XPLAT] was mf.btnAutoYouTurn.PerformClick()
            }
            else
            {
                //load tracks from temp
                _trk.gArr.Clear();
                foreach (var item in gTemp)
                {
                    _trk.gArr.Add(new CTrk(item));
                }

                _saveTracks();

                if (gTemp[indx].isVisible)
                {
                    _trk.idx = indx;
                    if (_trk.idx != originalLine)
                    {
                        if (_isBtnAutoSteerOn()) _turnAutoSteerOff();
                        _timedMessageBox(2000, gStr.gsGuidanceStopped, "Return From Editing");
                        if (_isYouTurnBtnOn()) _turnYouTurnOff();
                    }

                }
                else
                {
                    bool isOneVis = false;

                    // [XPLAT] Bug-for-bug: the WinForms unconditional break only ever inspects gTemp[0].
                    foreach (var item in gTemp)
                    {
                        if (item.isVisible) isOneVis = true;
                        break;
                    }

                    if (isOneVis)
                    {
                        if (gTemp.Count > 1)
                        {
                            while (true)
                            {
                                indx++;
                                if (indx == gTemp.Count) indx = 0;

                                if (gTemp[indx].isVisible)
                                {
                                    _trk.idx = indx;
                                    break;
                                }
                            }
                        }

                        if (_trk.idx != originalLine)
                        {
                            if (_isBtnAutoSteerOn())
                            {
                                _turnAutoSteerOff();
                                _timedMessageBox(2000, gStr.gsGuidanceStopped, "Return From Editing");
                            }
                            if (_isYouTurnBtnOn()) _turnYouTurnOff();
                        }
                    }
                    else
                    {
                        _trk.idx = -1;

                        _timedMessageBox(2000, gStr.gsEditABLine, gStr.gsNoABLineActive);
                        if (_isBtnAutoSteerOn()) _turnAutoSteerOff();
                        if (_isYouTurnBtnOn()) _turnYouTurnOff();
                    }
                }
            }
        }

        _curve.isCurveValid = false;
        _ABLine.isABValid = false;

        _setTwoSecondCounter(100); // [XPLAT] was mf.twoSecondCounter = 100

        // [XPLAT] was Properties.Settings.Default.setWindow_abDrawSize = Size. Fully qualified so the
        // sibling AgOpenGPS.Views.Settings namespace cannot shadow the AgOpenGPS.Properties.Settings class.
        AgOpenGPS.Properties.Settings.Default.setWindow_abDrawSize = new System.Drawing.Size((int)Width, (int)Height);
        AgOpenGPS.Properties.Settings.Default.Save();

        base.OnClosing(e);
    }

    /// <summary>[XPLAT] Stops and unhooks the dispatcher timer (WinForms disposed the Timer with the form).</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (timer1 != null)
        {
            timer1.Stop();
            timer1.Tick -= timer1_Tick;
        }
        base.OnClosed(e);
    }

    // ═══════════════════════════════════════════ Glyph helpers ═══════════════════════════════════════════

    /// <summary>[XPLAT] Loads (and caches) an avares glyph for runtime <c>Image.Source</c> swaps (was Properties.Resources.*).</summary>
    private static Bitmap LoadGlyph(string fileName)
    {
        if (!glyphCache.TryGetValue(fileName, out Bitmap bmp))
        {
            bmp = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
            glyphCache[fileName] = bmp;
        }
        return bmp;
    }

    /// <summary>
    /// [XPLAT] Swaps the glyph of a button/toggle whose Content is the unnamed <c>&lt;Image&gt;</c> declared in
    /// the .axaml (was <c>control.Image = Properties.Resources.X</c>). No-ops if the Content is not an Image.
    /// </summary>
    private static void SetButtonImage(ContentControl control, string fileName)
    {
        if (control?.Content is Image img)
        {
            img.Source = LoadGlyph(fileName);
        }
    }

    // ═══════════════════════════════════════════ Control handlers ═══════════════════════════════════════════

    /// <summary>[XPLAT] Port of <c>btnExit_Click</c> (L212). Commit path — close WITHOUT cancelling.</summary>
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        isCancel = false;
        Close();
    }

    /// <summary>[XPLAT] Port of <c>btnCancel_Click</c> (L218). Rollback path — close WITH the cancel guard set.</summary>
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        isCancel = true;
        Close();
    }

    /// <summary>[XPLAT] Port of <c>btnCancelTouch_Click</c> (L224). Aborts the in-progress trace and resets the camera.</summary>
    private void btnCancelTouch_Click(object sender, RoutedEventArgs e)
    {
        //update the arrays
        btnMakeABLine.IsEnabled = false;
        btnMakeCurve.IsEnabled = false;
        start = 99999; end = 99999;
        isA = true;

        FixLabelsCurve();

        _curve.desList?.Clear();

        zoom = 1;
        sX = 0;
        sY = 0;
        zoomToggle = false;

        btnExit.Focus();
    }

    /// <summary>[XPLAT] Port of <c>cboxIsZoom_CheckedChanged</c> (L207). Resets the one-shot zoom latch.</summary>
    private void cboxIsZoom_Click(object sender, RoutedEventArgs e)
    {
        zoomToggle = false;
    }

    /// <summary>
    /// [XPLAT] Port of <c>FixLabelsCurve</c> (L244-265). Refreshes the name box, the "n / total" label, and the
    /// visibility toggle (glyph + checked) for the current selection; shows the placeholders when nothing is
    /// selected. WinForms <c>.Visible</c> -> <c>.IsVisible</c>, <c>.Enabled</c> -> <c>.IsEnabled</c>,
    /// <c>.Checked</c> -> <c>.IsChecked</c>, <c>.Image</c> -> <see cref="SetButtonImage"/>.
    /// </summary>
    private void FixLabelsCurve()
    {
        if (indx > -1 && gTemp.Count > 0)
        {
            tboxNameCurve.Text = gTemp[indx].name;
            tboxNameCurve.IsEnabled = true;
            lblCurveSelected.Text = (indx + 1).ToString() + " / " + gTemp.Count.ToString();
            cboxIsVisible.IsVisible = true;
            cboxIsVisible.IsChecked = gTemp[indx].isVisible;
            if (gTemp[indx].isVisible)
                SetButtonImage(cboxIsVisible, "TrackVisible.png");
            else SetButtonImage(cboxIsVisible, "TracksInvisible.png");
        }
        else
        {
            tboxNameCurve.Text = "***";
            tboxNameCurve.IsEnabled = false;
            lblCurveSelected.Text = "*";
            cboxIsVisible.IsVisible = false;
        }
    }

    /// <summary>[XPLAT] Port of <c>btnSelectCurve_Click</c> (L267). Advances the selection (wraps to 0).</summary>
    private void btnSelectCurve_Click(object sender, RoutedEventArgs e)
    {
        if (gTemp.Count > 0)
        {
            indx++;
            if (indx > (gTemp.Count - 1)) indx = 0;
        }
        else
        {
            indx = -1;
        }

        FixLabelsCurve();
    }

    /// <summary>[XPLAT] Port of <c>btnSelectCurveBk_Click</c> (L294). Steps the selection back (wraps to last).</summary>
    private void btnSelectCurveBk_Click(object sender, RoutedEventArgs e)
    {
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
    }

    /// <summary>[XPLAT] Port of <c>cboxIsVisible_Click</c> (L309). Toggles the current track's visibility + glyph.</summary>
    private void cboxIsVisible_Click(object sender, RoutedEventArgs e)
    {
        gTemp[indx].isVisible = cboxIsVisible.IsChecked == true;
        if (gTemp[indx].isVisible)
            SetButtonImage(cboxIsVisible, "TrackVisible.png");
        else SetButtonImage(cboxIsVisible, "TracksInvisible.png");
    }

    /// <summary>[XPLAT] Port of <c>btnDeleteCurve_Click</c> (L317). Removes the selection, re-clamps the index, marks the line set dirty.</summary>
    private void btnDeleteCurve_Click(object sender, RoutedEventArgs e)
    {
        if (indx > -1)
        {
            gTemp.RemoveAt(indx);
        }

        if (gTemp.Count > 0)
        {
            if (indx > gTemp.Count - 1)
            {
                indx = gTemp.Count - 1;
            }
        }
        else
        {
            indx = -1;
        }

        originalLine = -2;

        FixLabelsCurve();
    }

    /// <summary>[XPLAT] Port of <c>btnDrawSections_Click</c> (L341). Toggles the coverage overlay + glyph.</summary>
    private void btnDrawSections_Click(object sender, RoutedEventArgs e)
    {
        isDrawSections = !isDrawSections;
        SetButtonImage(btnDrawSections, isDrawSections ? "MappingOn.png" : "MappingOff.png");
    }

    /// <summary>[XPLAT] Port of <c>tboxNameCurve_Leave</c> (L348). Commits the edited name on focus loss (Avalonia Text is nullable).</summary>
    private void tboxNameCurve_Leave(object sender, RoutedEventArgs e)
    {
        if (indx > -1)
            gTemp[indx].name = (tboxNameCurve.Text ?? string.Empty).Trim();
        btnExit.Focus();
    }

    /// <summary>
    /// [XPLAT] Port of <c>tboxNameCurve_Enter</c> (L355). When the on-screen keyboard is enabled, edits the name
    /// through the Avalonia <c>FormKeyboard</c> (was <c>TextBox.ShowKeyboard(this)</c>); a null result means the
    /// keyboard was cancelled. <c>async void</c> is required because <c>ShowDialog</c> is awaited from a focus event.
    /// </summary>
    private async void tboxNameCurve_Enter(object sender, GotFocusEventArgs e)
    {
        if (_isKeyboardOn())
        {
            var kb = new FormKeyboard(tboxNameCurve.Text ?? string.Empty);
            string result = await kb.ShowDialog<string>(this);
            if (result != null)
                tboxNameCurve.Text = result;

            if (indx > -1)
                gTemp[indx].name = (tboxNameCurve.Text ?? string.Empty).Trim();
            btnExit.Focus();
        }
    }

    /// <summary>[XPLAT] Port of <c>btnAddTime_Click</c> (L368). Appends " hh:mm:ss" (InvariantCulture) to the selected name.</summary>
    private void btnAddTime_Click(object sender, RoutedEventArgs e)
    {
        if (indx > -1)
        {
            gTemp[indx].name += DateTime.Now.ToString(" hh:mm:ss", CultureInfo.InvariantCulture);
            FixLabelsCurve();
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnMakeBoundaryCurve_Click</c> (L377-422). Converts every boundary fence into a
    /// <c>bndCurve</c> track (skipping fences with &lt; 4 points), naming the outer "Boundary Curve" and inner
    /// fences "Inner Boundary Curve N". Geometry copied verbatim.
    /// </summary>
    private void btnMakeBoundaryCurve_Click(object sender, RoutedEventArgs e)
    {            //countExit the points from the boundary
        for (int q = 0; q < _bnd.bndList.Count; q++)
        {
            // Boundary already has proper headings and spacing from FixFenceLine()
            // Just copy the points directly to create the boundary curve
            List<vec3> bndPoints = new List<vec3>();
            foreach (vec3 pt in _bnd.bndList[q].fenceLine)
            {
                bndPoints.Add(new vec3(pt));
            }

            if (bndPoints.Count < 4)
                continue;

            gTemp.Add(new CTrk());
            indx = gTemp.Count - 1;

            // Set A and B points to first and second-to-last points
            // (last point is duplicate of first for closing the loop)
            gTemp[indx].ptA = new vec2(bndPoints[0].easting, bndPoints[0].northing);
            gTemp[indx].ptB = new vec2(bndPoints[bndPoints.Count - 2].easting, bndPoints[bndPoints.Count - 2].northing);

            //create a name
            gTemp[indx].name = "Boundary Curve";
            if (q > 0) gTemp[indx].name = "Inner Boundary Curve " + q.ToString();

            gTemp[indx].heading = 0;
            gTemp[indx].mode = TrackMode.bndCurve;

            // Copy all boundary points including the closing point
            foreach (vec3 pt in bndPoints)
            {
                gTemp[indx].curvePts.Add(pt);
            }
        }

        //update the arrays
        btnMakeABLine.IsEnabled = false;
        btnMakeCurve.IsEnabled = false;
        start = 99999; end = 99999;

        FixLabelsCurve();

        btnExit.Focus();
    }

    /// <summary>
    /// [XPLAT] Port of <c>BtnMakeCurve_Click</c> (L424-529). Slices the selected fence span (handling the
    /// wrap-around "loop" case), builds the desList, minimum-spaces and re-heads it, computes the average
    /// heading, adds the tail extensions, and stores the result as a <c>Curve</c> track named "Cu {deg}°".
    /// Math + spacing constant (1.6) ported verbatim.
    /// </summary>
    private void BtnMakeCurve_Click(object sender, RoutedEventArgs e)
    {
        bool isLoop = false;
        int limit = end;

        if ((Math.Abs(start - end)) > (_bnd.bndList[bndSelect].fenceLine.Count * 0.5))
        {
            isLoop = true;
            if (start < end)
            {
                (end, start) = (start, end);
            }

            limit = end;
            end = _bnd.bndList[bndSelect].fenceLine.Count;
        }
        else //normal
        {
            if (start > end)
            {
                (end, start) = (start, end);
            }
        }

        _curve.desList?.Clear();
        vec3 pt3;

        for (int i = start; i < end; i++)
        {
            //calculate the point inside the boundary
            pt3 = new vec3(_bnd.bndList[bndSelect].fenceLine[i]);

            _curve.desList.Add(new vec3(pt3));

            if (isLoop && i == _bnd.bndList[bndSelect].fenceLine.Count - 1)
            {
                i = -1;
                isLoop = false;
                end = limit;
            }
        }

        gTemp.Add(new CTrk());
        //array number is 1 less since it starts at zero
        indx = gTemp.Count - 1;

        gTemp[indx].ptA =
            new vec2(_curve.desList[0].easting, _curve.desList[0].northing);
        gTemp[indx].ptB =
            new vec2(_curve.desList[_curve.desList.Count - 1].easting,
            _curve.desList[_curve.desList.Count - 1].northing);

        int cnt = _curve.desList.Count;
        if (cnt > 3)
        {
            //make sure point distance isn't too big 
            CABCurve.MakePointMinimumSpacing(ref _curve.desList, 1.6);
            CABCurve.CalculateHeadings(ref _curve.desList);

            //calculate average heading of line
            double x = 0, y = 0;

            foreach (vec3 pt in _curve.desList)
            {
                x += Math.Cos(pt.heading);
                y += Math.Sin(pt.heading);
            }
            x /= _curve.desList.Count;
            y /= _curve.desList.Count;
            gTemp[indx].heading = Math.Atan2(y, x);
            if (gTemp[indx].heading < 0) gTemp[indx].heading += glm.twoPI;

            //build the tail extensions
            _curve.AddFirstLastPoints(ref _curve.desList);
            //mf.curve.SmoothAB(2);
            CABCurve.CalculateHeadings(ref _curve.desList);

            //array number is 1 less since it starts at zero
            indx = gTemp.Count - 1;

            //create a name
            gTemp[indx].name = "Cu " +
                (Math.Round(glm.toDegrees(gTemp[indx].heading), 1)).ToString(CultureInfo.InvariantCulture)
                + "\u00B0";

            gTemp[indx].mode = TrackMode.Curve;

            //write out the Curve Points
            foreach (vec3 item in _curve.desList)
            {
                gTemp[indx].curvePts.Add(item);
            }

            //update the arrays
            btnMakeABLine.IsEnabled = false;
            btnMakeCurve.IsEnabled = false;
            start = 99999; end = 99999;

            FixLabelsCurve();
        }
        else
        {
        }
        btnExit.Focus();
        _curve.desList?.Clear();
    }

    /// <summary>
    /// [XPLAT] Port of <c>BtnMakeABLine_Click</c> (L531-580). Computes the AB heading between the two picked
    /// fence points (handling the wrap-around ordering) and stores the result as an <c>AB</c> track named
    /// "AB {deg}°". Math ported verbatim.
    /// </summary>
    private void BtnMakeABLine_Click(object sender, RoutedEventArgs e)
    {
        //if more then half way around, it crosses start finish
        if ((Math.Abs(start - end)) <= (_bnd.bndList[bndSelect].fenceLine.Count * 0.5))
        {
            if (start < end)
            {
                (end, start) = (start, end);
            }
        }
        else
        {
            if (start > end)
            {
                (end, start) = (start, end);
            }
        }

        //calculate the AB Heading
        double abHead = Math.Atan2(
            _bnd.bndList[bndSelect].fenceLine[end].easting - _bnd.bndList[bndSelect].fenceLine[start].easting,
            _bnd.bndList[bndSelect].fenceLine[end].northing - _bnd.bndList[bndSelect].fenceLine[start].northing);
        if (abHead < 0) abHead += glm.twoPI;

        gTemp.Add(new CTrk());

        indx = gTemp.Count - 1;

        gTemp[indx].heading = abHead;
        gTemp[indx].mode = TrackMode.AB;

        //calculate the new points for the reference line and points
        gTemp[indx].ptA.easting = _bnd.bndList[bndSelect].fenceLine[start].easting;
        gTemp[indx].ptA.northing = _bnd.bndList[bndSelect].fenceLine[start].northing;

        gTemp[indx].ptB.easting = _bnd.bndList[bndSelect].fenceLine[end].easting;
        gTemp[indx].ptB.northing = _bnd.bndList[bndSelect].fenceLine[end].northing;

        //create a name
        gTemp[indx].name = "AB " +
            (Math.Round(glm.toDegrees(gTemp[indx].heading), 1)).ToString(CultureInfo.InvariantCulture) + "\u00B0";

        //clean up gui
        btnMakeABLine.IsEnabled = false;
        btnMakeCurve.IsEnabled = false;

        start = 99999; end = 99999;

        FixLabelsCurve();
    }

    /// <summary>[XPLAT] Port of <c>btnALength_Click</c> (L845). Prepends 50 points to extend the curve's A-end. The local <c>start</c> intentionally shadows the int field (as in the source).</summary>
    private void btnALength_Click(object sender, RoutedEventArgs e)
    {
        if (indx > -1 && gTemp[indx].mode == TrackMode.Curve)
        {
            //and the beginning
            vec3 start = new vec3(gTemp[indx].curvePts[0]);

            for (int i = 1; i < 50; i++)
            {
                vec3 pt = new vec3(start);
                pt.easting -= (Math.Sin(pt.heading) * i);
                pt.northing -= (Math.Cos(pt.heading) * i);
                gTemp[indx].curvePts.Insert(0, pt);
            }
        }
    }

    /// <summary>[XPLAT] Port of <c>btnBLength_Click</c> (L862). Appends 50 points to extend the curve's B-end.</summary>
    private void btnBLength_Click(object sender, RoutedEventArgs e)
    {
        if (indx > -1 && gTemp[indx].mode == TrackMode.Curve)
        {
            int ptCnt = gTemp[indx].curvePts.Count - 1;

            for (int i = 1; i < 50; i++)
            {
                vec3 pt = new vec3(gTemp[indx].curvePts[ptCnt]);
                pt.easting += (Math.Sin(pt.heading) * i);
                pt.northing += (Math.Cos(pt.heading) * i);
                gTemp[indx].curvePts.Add(pt);
            }
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>timer1_Tick</c> (L819-843). Refreshes the viewport (was <c>oglSelf.Refresh()</c>),
    /// disables "make boundary curve" once a boundary curve already exists, and enables the A/B length
    /// extenders only while a <c>Curve</c> track is selected. Branch logic ported verbatim.
    /// </summary>
    private void timer1_Tick(object sender, EventArgs e)
    {
        _viewport.RequestRender(); // [XPLAT] was oglSelf.Refresh()

        btnMakeBoundaryCurve.IsEnabled = true;
        for (int i = 0; i < gTemp.Count; i++)
        {
            if (gTemp[i].mode == TrackMode.bndCurve)
            {
                btnMakeBoundaryCurve.IsEnabled = false;
                break;
            }
        }

        if (indx > -1 && gTemp[indx].mode != TrackMode.Curve)
        {
            btnALength.IsEnabled = false;
            btnBLength.IsEnabled = false;
        }
        else
        {
            btnALength.IsEnabled = true;
            btnBLength.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════ Pattern B — raw GL camera + input ═══════════════════════════════════

    /// <summary>
    /// [XPLAT] Port of <c>oglSelf_MouseDown</c> (L582-668) — PATTERN B custom pixel→world projection (NOT
    /// <c>GetGeoCoord</c>). WinForms <c>oglSelf.PointToClient(Cursor.Position)</c> (client pixels) becomes
    /// <c>e.GetPosition</c> (logical DIP) scaled by the visual root's RenderScaling into pixel space, matching
    /// the GL viewport pixel size. The viewport is square (side = min(width,height)) like the source oglSelf,
    /// so <c>wid</c> is that side for both axes. First click selects the nearest fence point across all
    /// boundaries (A); the second selects the nearest within the chosen boundary (B). Math ported verbatim.
    /// </summary>
    private void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
    {
        var ptd = e.GetPosition(_viewport.View);
        double scaling = _viewport.View.GetVisualRoot()?.RenderScaling ?? 1.0;
        if (double.IsNaN(scaling) || double.IsInfinity(scaling) || scaling <= 0.0)
        {
            scaling = 1.0;
        }
        double ptX = ptd.X * scaling;
        double ptY = ptd.Y * scaling;

        int wid = Math.Min((int)_viewport.ViewportSize.DeltaX, (int)_viewport.ViewportSize.DeltaY); // [XPLAT] square side (was oglSelf.Width)
        int halfWid = wid / 2;
        double scale = (double)wid * 0.903;

        if (cboxIsZoom.IsChecked == true && !zoomToggle)
        {
            sX = ((halfWid - ptX) / wid) * 1.1;
            sY = ((halfWid - ptY) / -wid) * 1.1;
            zoom = 0.1;
            zoomToggle = true;
            return;
        }

        zoomToggle = false;
        btnMakeABLine.IsEnabled = false;
        btnMakeCurve.IsEnabled = false;


        //Convert to Origin in the center of window, 800 pixels
        fixPtX = ptX - halfWid;
        fixPtY = (wid - ptY - halfWid);
        vec3 plotPt = new vec3
        {
            //convert screen coordinates to field coordinates
            easting = fixPtX * _getMaxFieldDistance() / scale * zoom,
            northing = fixPtY * _getMaxFieldDistance() / scale * zoom,
            heading = 0
        };

        plotPt.easting += _getFieldCenterX() + _getMaxFieldDistance() * -sX;
        plotPt.northing += _getFieldCenterY() + _getMaxFieldDistance() * -sY;

        pint.easting = plotPt.easting;
        pint.northing = plotPt.northing;

        zoom = 1;
        sX = 0;
        sY = 0;

        if (isA)
        {
            double minDistA = double.MaxValue;
            start = 99999; end = 99999;

            for (int j = 0; j < _bnd.bndList.Count; j++)
            {
                for (int i = 0; i < _bnd.bndList[j].fenceLine.Count; i++)
                {
                    double dist = ((pint.easting - _bnd.bndList[j].fenceLine[i].easting) * (pint.easting - _bnd.bndList[j].fenceLine[i].easting))
                                    + ((pint.northing - _bnd.bndList[j].fenceLine[i].northing) * (pint.northing - _bnd.bndList[j].fenceLine[i].northing));
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

            for (int i = 0; i < _bnd.bndList[j].fenceLine.Count; i++)
            {
                double dist = ((pint.easting - _bnd.bndList[j].fenceLine[i].easting) * (pint.easting - _bnd.bndList[j].fenceLine[i].easting))
                                + ((pint.northing - _bnd.bndList[j].fenceLine[i].northing) * (pint.northing - _bnd.bndList[j].fenceLine[i].northing));
                if (dist < minDistA)
                {
                    minDistA = dist;
                    end = i;
                }
            }

            isA = true;

            btnMakeABLine.IsEnabled = true;
            btnMakeCurve.IsEnabled = true;
        }

        _viewport.RequestRender(); // [XPLAT] reflect the new pick immediately (was the WinForms paint loop)
    }

    /// <summary>
    /// [XPLAT] Port of <c>oglSelf_Paint</c> (L670-704) — supplied to the host as its RenderAction. The host
    /// brackets this between BeginPaint()/EndPaint() (context + buffer swap), so this body issues only the
    /// projection set-up, the PATTERN B custom camera, and the immediate-mode draws (no MakeCurrent, no
    /// SwapBuffers).
    /// </summary>
    private void RenderViewport()
    {
        // [XPLAT] RISK: this view relies on LEGACY IMMEDIATE-MODE OpenGL (GL.Begin/GL.Vertex3/GL.LineStipple/
        // GL.PointSize/GL.Translate + the fixed-function matrix stack). AvaloniaGeoViewport MUST supply a
        // DESKTOP-GL compatibility context (NOT GLES/ANGLE) or these calls fail. This is the dominant migration
        // feasibility risk (AAP §0.6.2); the renderer is behaviour-frozen, so the context must accommodate it —
        // tracked in MIGRATION_DOCS/PARITY_REPORT.md.

        // A loader/previewer instance has no model — nothing to draw.
        if (_bnd == null) return;

        XyDelta size = _viewport.ViewportSize;
        int w = (int)size.DeltaX;
        int h = (int)size.DeltaY;
        if (w <= 0 || h <= 0) return;

        // [XPLAT] one-shot GL state init replacing oglSelf_Load (L923) — runs once the GL context is first current.
        if (!_glSetupDone)
        {
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            _glSetupDone = true;
        }

        // [XPLAT] Projection from oglSelf_Resize (L908-921), set per-frame because the GL context is only current
        // inside the render callback. The matrix is byte-identical to GLW.CreatePerspectiveFieldOfView(). The
        // viewport is a square (side = min(w,h)) anchored at the SCREEN top-left (GL y = h - side) to reproduce
        // the square, top-left oglSelf surface and keep the hard-coded aspect 1.0 perspective un-stretched.
        int side = Math.Min(w, h);
        GL.MatrixMode(MatrixMode.Projection);
        GL.LoadIdentity();
        GL.Viewport(0, h - side, side, side);
        Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, 1.0f, 1.0f, 20000);
        GL.LoadMatrix(ref mat);
        GL.MatrixMode(MatrixMode.Modelview);

        // [XPLAT] oglSelf_Paint body — MakeCurrent/SwapBuffers removed (the host's BeginPaint/EndPaint own those).
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
        GL.LoadIdentity();                  // Reset The View

        //back the camera up
        GL.Translate(0, 0, -_getMaxFieldDistance() * zoom);

        //translate to that spot in the world
        GL.Translate(-_getFieldCenterX() + sX * _getMaxFieldDistance(), -_getFieldCenterY() + sY * _getMaxFieldDistance(), 0);

        if (isDrawSections) SectionsVisual.DrawSections(_triStrip);

        for (int j = 0; j < _bnd.bndList.Count; j++)
        {
            GeoCoord[] fenceLineEar = GeoRefactorHelper.ToGeoCoordArray(_bnd.bndList[j].fenceLineEar);
            bool isSelected = j == bndSelect;
            FenceLineVisual.DrawFenceLine(fenceLineEar, isSelected);
        }
        VehicleDotVisual.DrawVehicleDot(_getPivotAxlePos().ToGeoCoord());

        //draw the line building graphics
        if (start != 99999 || end != 99999) DrawABTouchPoints();

        //draw the actual built lines
        if (start == 99999 && end == 99999)
        {
            DrawBuiltLines();
        }

        GL.Flush();
    }

    /// <summary>
    /// [XPLAT] Port of <c>DrawBuiltLines</c> (L706-795). Immediate-mode rendering of every built track: stippled
    /// red AB lines (extended ±abLength), green curve / orange boundary-curve line strips, and the A/B endpoint
    /// points; the selected track (<c>i == indx</c>) is drawn thicker and un-stippled. Colours, stipple patterns
    /// (0x0707 / 0x0007), widths, and point sizes ported verbatim. (The source's commented-out
    /// numABLineSelected block — dead code — is intentionally omitted.)
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

                GL.Vertex3(gTemp[i].ptA.easting - (Math.Sin(gTemp[i].heading) * _ABLine.abLength), gTemp[i].ptA.northing - (Math.Cos(gTemp[i].heading) * _ABLine.abLength), 0);
                GL.Vertex3(gTemp[i].ptB.easting + (Math.Sin(gTemp[i].heading) * _ABLine.abLength), gTemp[i].ptB.northing + (Math.Cos(gTemp[i].heading) * _ABLine.abLength), 0);

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
    /// [XPLAT] Port of <c>DrawABTouchPoints</c> (L797-817). Draws the in-progress A/B pick markers: a large
    /// black point underlay (size 24) then the coloured A (orange) / B (blue) points (size 16). Colours and
    /// sizes ported verbatim.
    /// </summary>
    private void DrawABTouchPoints()
    {
        GL.Color3(0.65, 0.650, 0.0);
        GL.PointSize(24);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(0, 0, 0);
        if (start != 99999) GL.Vertex3(_bnd.bndList[bndSelect].fenceLine[start].easting, _bnd.bndList[bndSelect].fenceLine[start].northing, 0);
        if (end != 99999) GL.Vertex3(_bnd.bndList[bndSelect].fenceLine[end].easting, _bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();

        GL.PointSize(16);
        GL.Begin(PrimitiveType.Points);

        GL.Color3(1.0f, 0.75f, 0.350f);
        if (start != 99999) GL.Vertex3(_bnd.bndList[bndSelect].fenceLine[start].easting, _bnd.bndList[bndSelect].fenceLine[start].northing, 0);

        GL.Color3(0.5f, 0.5f, 1.0f);
        if (end != 99999) GL.Vertex3(_bnd.bndList[bndSelect].fenceLine[end].easting, _bnd.bndList[bndSelect].fenceLine[end].northing, 0);
        GL.End();
    }
}
