// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Code-behind for FormQuickABView — a 1:1 behavioural-parity reimplementation of the WinForms
// FormQuickAB (SourceCode/GPS/Forms/Guidance/FormQuickAB.cs + FormQuickAB.Designer.cs). This is the
// multi-panel "Quick AB / Tracks" builder: a Choose panel that branches to an AB-line, an A+, or a
// Curve builder, followed by a shared Name panel that appends the freshly-built CTrk to the track
// list and persists it.
//
// The track-building mathematics is behaviour-frozen (AAP §0.2.2) and is ported VERBATIM from the
// source — every geometry expression, rounding, and InvariantCulture formatting is preserved
// exactly. The only structural change is the removal of the FormGPS "mf" god-object back-reference:
// every former mf.* access is replaced by a constructor-injected concrete domain object or a
// Func/Action callback (see the mapping table in the file header docs). Each such substitution is
// tagged // [XPLAT].

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AgOpenGPS;                       // vec2, vec3, glm, CABCurve, CABLine, CTrack, CTrk, CTool, TrackMode
using AgOpenGPS.Core.Translations;     // gStr
using AgOpenGPS.Properties;            // Settings

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Quick-AB / Tracks builder dialog. Shown non-modally by the host (<c>.Show(owner)</c>);
/// it mutates the injected domain objects directly and <see cref="Window.Close()"/>s — it returns no
/// dialog result. The host wires the injected callbacks to the live <c>FormGPS</c> state.
/// </summary>
public partial class FormQuickABView : Window
{
    // ---------------------------------------------------------------------------------------------
    // [XPLAT] Injected domain objects (formerly mf.curve / mf.ABLine / mf.trk / mf.tool).
    // ---------------------------------------------------------------------------------------------
    private readonly CABCurve curve;        // [XPLAT] mf.curve
    private readonly CABLine ABLine;        // [XPLAT] mf.ABLine
    private readonly CTrack trk;            // [XPLAT] mf.trk — concrete type is CTrack (gArr/idx/Nudge*), not CTrackMethods
    private readonly CTool tool;            // [XPLAT] mf.tool

    // ---------------------------------------------------------------------------------------------
    // [XPLAT] Injected callbacks replacing the remaining mf.* surface.
    // ---------------------------------------------------------------------------------------------
    private readonly Func<vec3> getPivotAxlePos;        // [XPLAT] mf.pivotAxlePos (live)
    private readonly Func<bool> isKeyboardOn;           // [XPLAT] mf.isKeyboardOn
    private readonly Func<bool> isEasyDriveMode;        // [XPLAT] mf.isEasyDriveMode
    private readonly Func<bool> isBtnAutoSteerOn;       // [XPLAT] mf.isBtnAutoSteerOn
    private readonly Func<bool> isYouTurnBtnOn;         // [XPLAT] mf.yt.isYouTurnBtnOn
    private readonly Action performAutoSteerClick;      // [XPLAT] mf.btnAutoSteer.PerformClick()
    private readonly Action performAutoYouTurnClick;    // [XPLAT] mf.btnAutoYouTurn.PerformClick()
    private readonly Action<int> setTwoSecondCounter;   // [XPLAT] mf.twoSecondCounter = ...
    private readonly Action panelUpdateRightAndBottom;  // [XPLAT] mf.PanelUpdateRightAndBottom()
    private readonly Action saveTracks;                 // [XPLAT] mf.FileSaveTracks()
    private readonly Action activateMainView;           // [XPLAT] mf.Activate()

    // ---------------------------------------------------------------------------------------------
    // Source fields — preserved EXACTLY from FormQuickAB.
    // ---------------------------------------------------------------------------------------------
    private double aveLineHeading;
    private vec2 ptAa = new vec2();
    private vec2 ptBb = new vec2();
    private bool isRefRightSide = true;
    private int idx;

    // [XPLAT] mirrors the WinForms nudHeading.Value (a NumericUpDown) in degrees; the Avalonia
    // nudHeading is a Button whose Content shows the formatted value.
    private double nudHeadingValue;

    // [XPLAT] replaces WinForms System.Windows.Forms.Timer "timer1" (Interval = 500 ms).
    private DispatcherTimer timer1;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / previewer and to
    /// avoid an AVLN3001 "no public constructor" warning. The production constructor chains to this.
    /// </summary>
    public FormQuickABView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Production constructor. Injects the concrete domain objects and the host callbacks that
    /// replace the former <c>FormGPS</c> back-reference, wires the control events, and localises the
    /// static captions (source ctor lines 27-40).
    /// </summary>
    public FormQuickABView(
        CABCurve curve,
        CABLine ABLine,
        CTrack trk,
        CTool tool,
        Func<vec3> getPivotAxlePos,
        Func<bool> isKeyboardOn,
        Func<bool> isEasyDriveMode,
        Func<bool> isBtnAutoSteerOn,
        Func<bool> isYouTurnBtnOn,
        Action performAutoSteerClick,
        Action performAutoYouTurnClick,
        Action<int> setTwoSecondCounter,
        Action panelUpdateRightAndBottom,
        Action saveTracks,
        Action activateMainView)
        : this()
    {
        this.curve = curve;
        this.ABLine = ABLine;
        this.trk = trk;
        this.tool = tool;
        this.getPivotAxlePos = getPivotAxlePos;
        this.isKeyboardOn = isKeyboardOn;
        this.isEasyDriveMode = isEasyDriveMode;
        this.isBtnAutoSteerOn = isBtnAutoSteerOn;
        this.isYouTurnBtnOn = isYouTurnBtnOn;
        this.performAutoSteerClick = performAutoSteerClick;
        this.performAutoYouTurnClick = performAutoYouTurnClick;
        this.setTwoSecondCounter = setTwoSecondCounter;
        this.panelUpdateRightAndBottom = panelUpdateRightAndBottom;
        this.saveTracks = saveTracks;
        this.activateMainView = activateMainView;

        // [XPLAT] 500 ms heading/B-point refresh timer (replaces WinForms timer1). Initially stopped.
        timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer1.Tick += Timer1_Tick;

        // Source ctor lines 33-37: localise captions.
        Title = gStr.gsQuickAB;
        labelABLine.Text = gStr.gsABline;
        labelCurve.Text = gStr.gsCurve;
        labelAPlus.Text = gStr.gsAPlus;
        labelStatus.Text = gStr.gsStatus;

        // ----- Pick panel -----
        btnzABCurve.Click += btnzABCurve_Click;
        btnzAPlus.Click += btnzAPlus_Click;
        btnzABLine.Click += btnzABLine_Click;

        // ----- Curve panel -----
        btnRefSideCurve.Click += btnRefSideCurve_Click;
        btnACurve.Click += btnACurve_Click;
        btnBCurve.Click += btnBCurve_Click;
        btnPausePlay.Click += btnPausePlayCurve_Click;

        // ----- AB-line panel -----
        btnRefSideAB.Click += btnRefSideAB_Click;
        btnALine.Click += btnALine_Click;
        btnBLine.Click += btnBLine_Click;
        btnEnter_AB.Click += btnEnter_AB_Click;

        // ----- A+ panel -----
        btnRefSideAPlus.Click += btnRefSideAPlus_Click;
        btnAPlus.Click += btnAPlus_Click;
        nudHeading.Click += nudHeading_Click;
        btnEnter_APlus.Click += btnEnter_APlus_Click;

        // ----- Name panel -----
        btnAddTime.Click += btnAddTime_Click;
        btnAdd.Click += btnAdd_Click;

        // [XPLAT] all four panel cancel buttons route to the single shared cancel handler.
        btnCancelChoose.Click += btnCancelCurve_Click;
        btnCancel_Curve.Click += btnCancelCurve_Click;
        btnCancel_ABLine.Click += btnCancelCurve_Click;
        btnCancel_APlus.Click += btnCancelCurve_Click;

        // [XPLAT] TextBox tap -> on-screen keyboard (source wired textBox1.Click -> textBox_Click).
        textBox1.Tapped += textBox_Click;
    }

    // =================================================================================================
    // Lifecycle  (FormQuickAB_Load / FormQuickAB_FormClosing)
    // =================================================================================================

    /// <summary>
    /// Port of <c>FormQuickAB_Load</c> (source lines 42-68). The five panels and their initial
    /// visibility (panelChoose visible, the rest hidden) and the window size are declared in XAML, so
    /// only the window-position restore, the heading reset, and the on-screen guard remain here.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] Settings.setWindow_QuickABLocation is a System.Drawing.Point; map to PixelPoint.
        System.Drawing.Point loc = Properties.Settings.Default.setWindow_QuickABLocation;
        Position = new PixelPoint(loc.X, loc.Y);

        // [XPLAT] nudHeading.Value = 0 (the source nudHeading.Controls[0].Enabled = false has no
        // Avalonia equivalent and is intentionally omitted).
        nudHeadingValue = 0;
        nudHeading.Content = "0";

        // Source 64-67: if the saved location is off every screen, snap back to the origin.
        ClampToScreen();
    }

    /// <summary>
    /// Port of <c>FormQuickAB_FormClosing</c> (source lines 70-78): persist the window location and
    /// notify the host to refresh its panels.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PixelPoint pos = Position;
        Properties.Settings.Default.setWindow_QuickABLocation = new System.Drawing.Point(pos.X, pos.Y);
        Properties.Settings.Default.Save();

        setTwoSecondCounter?.Invoke(100);            // [XPLAT] mf.twoSecondCounter = 100
        panelUpdateRightAndBottom?.Invoke();         // [XPLAT] mf.PanelUpdateRightAndBottom()

        base.OnClosing(e);
    }

    /// <summary>[XPLAT] Tear down the refresh timer when the window is destroyed.</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (timer1 != null)
        {
            timer1.Stop();
            timer1.Tick -= Timer1_Tick;
        }
        base.OnClosed(e);
    }

    // =================================================================================================
    // [XPLAT] helpers
    // =================================================================================================

    /// <summary>[XPLAT] Start the 500 ms refresh timer (source set timer1.Enabled = true).</summary>
    private void StartTimer() => timer1?.Start();

    /// <summary>[XPLAT] Stop the 500 ms refresh timer (source set timer1.Enabled = false).</summary>
    private void StopTimer() => timer1?.Stop();

    /// <summary>
    /// [XPLAT] If the restored window position lies outside every connected screen, reset it to the
    /// origin — the cross-platform equivalent of the source's off-screen guard (source 64-67).
    /// </summary>
    private void ClampToScreen()
    {
        if (Screens == null)
        {
            return;
        }

        PixelPoint pos = Position;
        foreach (var sc in Screens.All)
        {
            if (sc.Bounds.Contains(pos))
            {
                return;
            }
        }

        Position = new PixelPoint(0, 0);
    }

    /// <summary>[XPLAT] Load a button glyph from the embedded Avalonia resources (btnImages/).</summary>
    private static Bitmap LoadButtonImage(string fileName)
    {
        return new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
    }

    /// <summary>
    /// [XPLAT] Swap the glyph of a button whose Content is an <see cref="Image"/> — replaces the
    /// WinForms <c>button.Image = Properties.Resources.X</c> assignments.
    /// </summary>
    private static void SetButtonImage(Button button, string fileName)
    {
        if (button?.Content is Image image)
        {
            image.Source = LoadButtonImage(fileName);
        }
    }

    // =================================================================================================
    // Pick panel  (source lines 81-115)
    // =================================================================================================

    /// <summary>Port of <c>btnzABCurve_Click</c> (source 81-90): branch to the Curve builder.</summary>
    private void btnzABCurve_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelCurve.IsVisible = true;

        btnACurve.IsEnabled = true;
        btnBCurve.IsEnabled = false;
        btnPausePlay.IsEnabled = false;

        curve.desList?.Clear();
        activateMainView();
    }

    /// <summary>Port of <c>btnzAPlus_Click</c> (source 92-101): branch to the A+ builder.</summary>
    private void btnzAPlus_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelAPlus.IsVisible = true;

        btnAPlus.IsEnabled = true;
        curve.desList?.Clear();
        nudHeading.IsEnabled = false;

        activateMainView();
    }

    /// <summary>Port of <c>btnzABLine_Click</c> (source 103-115): branch to the AB-line builder.</summary>
    private void btnzABLine_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelABLine.IsVisible = true;

        btnEnter_AB.IsEnabled = false;
        btnALine.IsEnabled = true;
        btnBLine.IsEnabled = false;
        btnPausePlay.IsEnabled = false;

        curve.desList?.Clear();
        activateMainView();
    }

    // =================================================================================================
    // Curve panel  (source lines 119-261)
    // =================================================================================================

    /// <summary>Port of <c>btnRefSideCurve_Click</c> (source 120-126).</summary>
    private void btnRefSideCurve_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        SetButtonImage(btnRefSideCurve, isRefRightSide ? "BoundaryRight.png" : "BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnACurve_Click</c> (source 128-153): start / extend curve recording.</summary>
    private void btnACurve_Click(object sender, RoutedEventArgs e)
    {
        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos

        if (curve.isMakingCurve)
        {
            curve.desList.Add(new vec3(pivotAxlePos.easting, pivotAxlePos.northing, pivotAxlePos.heading));
            btnBCurve.IsEnabled = curve.desList.Count > 3;
        }
        else
        {
            ptAa.easting = pivotAxlePos.easting;
            ptAa.northing = pivotAxlePos.northing;

            lblCurveExists.Text = gStr.gsDriving;

            btnBCurve.IsEnabled = true;
            btnACurve.IsEnabled = false;
            SetButtonImage(btnACurve, "PointAdd.png");

            btnPausePlay.IsEnabled = true;
            btnPausePlay.IsVisible = true;

            curve.isMakingCurve = true;
            curve.isRecordingCurve = true;
        }
        activateMainView();
    }

    /// <summary>
    /// Port of <c>btnBCurve_Click</c> (source 155-240). Behaviour-frozen: the minimum-spacing
    /// resampling, average-heading computation, tail-extension, smoothing, CTrk assembly, name
    /// formatting, and reference-side nudge are ported VERBATIM.
    /// </summary>
    private void btnBCurve_Click(object sender, RoutedEventArgs e)
    {
        aveLineHeading = 0;
        curve.isMakingCurve = false;
        curve.isRecordingCurve = false;
        panelCurve.IsVisible = false;
        panelName.IsVisible = true;

        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos
        ptBb.easting = pivotAxlePos.easting;
        ptBb.northing = pivotAxlePos.northing;

        int cnt = curve.desList.Count;
        if (cnt > 3)
        {
            //make sure point distance isn't too big
            CABCurve.MakePointMinimumSpacing(ref curve.desList, 1.6);
            CABCurve.CalculateHeadings(ref curve.desList);

            trk.gArr.Add(new CTrk());
            //array number is 1 less since it starts at zero
            idx = trk.gArr.Count - 1;

            trk.gArr[idx].ptA = new vec2(ptAa);
            trk.gArr[idx].ptB = new vec2(ptBb);

            trk.gArr[idx].mode = TrackMode.Curve;

            //calculate average heading of line
            double x = 0, y = 0;
            foreach (vec3 pt in curve.desList)
            {
                x += Math.Cos(pt.heading);
                y += Math.Sin(pt.heading);
            }
            x /= curve.desList.Count;
            y /= curve.desList.Count;
            aveLineHeading = Math.Atan2(y, x);
            if (aveLineHeading < 0) aveLineHeading += glm.twoPI;

            trk.gArr[idx].heading = aveLineHeading;

            //build the tail extensions
            curve.AddFirstLastPoints(ref curve.desList);
            SmoothAB(4);
            CABCurve.CalculateHeadings(ref curve.desList);

            //write out the Curve Points
            foreach (vec3 item in curve.desList)
            {
                trk.gArr[idx].curvePts.Add(item);
            }

            curve.desName = "Cu " +
                (Math.Round(glm.toDegrees(aveLineHeading), 1)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";

            textBox1.Text = curve.desName;

            panelCurve.IsVisible = false;
            panelName.IsVisible = true;

            double dist;

            if (isRefRightSide)
            {
                dist = (tool.width - tool.overlap) * 0.5 + tool.offset;
                trk.idx = idx;
                trk.NudgeRefCurve(dist);
            }
            else
            {
                dist = (tool.width - tool.overlap) * -0.5 + tool.offset;
                trk.idx = idx;
                trk.NudgeRefCurve(dist);
            }
        }
        else
        {
            //insufficient points recorded - close the form
            curve.desList?.Clear();
            panelCurve.IsVisible = false;
            panelName.IsVisible = false;
            panelChoose.IsVisible = false;
            Close();
        }
        activateMainView();
    }

    /// <summary>Port of <c>btnPausePlayCurve_Click</c> (source 242-261): pause / resume recording.</summary>
    private void btnPausePlayCurve_Click(object sender, RoutedEventArgs e)
    {
        if (curve.isRecordingCurve)
        {
            curve.isRecordingCurve = false;
            SetButtonImage(btnPausePlay, "BoundaryRecord.png");
            btnACurve.IsEnabled = true;
        }
        else
        {
            curve.isRecordingCurve = true;
            SetButtonImage(btnPausePlay, "boundaryPause.png");
            btnACurve.IsEnabled = false;
        }

        btnBCurve.IsEnabled = curve.desList.Count > 3;
        activateMainView();
    }

    // =================================================================================================
    // AB Line panel  (source lines 265-355) + refresh timer tick (358-371)
    // =================================================================================================

    /// <summary>Port of <c>btnRefSideAB_Click</c> (source 266-272).</summary>
    private void btnRefSideAB_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        SetButtonImage(btnRefSideAB, isRefRightSide ? "BoundaryRight.png" : "BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnALine_Click</c> (source 274-296): mark point A and start the live B refresh.</summary>
    private void btnALine_Click(object sender, RoutedEventArgs e)
    {
        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos

        ABLine.isMakingABLine = true;
        btnALine.IsEnabled = false;
        btnEnter_AB.IsEnabled = false;

        ABLine.desPtA = new vec2(pivotAxlePos.easting, pivotAxlePos.northing);

        ABLine.desPtB.easting = ABLine.desPtA.easting - (Math.Sin(pivotAxlePos.heading) * 1);
        ABLine.desPtB.northing = ABLine.desPtA.northing - (Math.Cos(pivotAxlePos.heading) * 1);

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(pivotAxlePos.heading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(pivotAxlePos.heading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(pivotAxlePos.heading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(pivotAxlePos.heading) * 1000);

        StartTimer();   // [XPLAT] timer1.Enabled = true

        btnBLine.IsEnabled = true;
        btnALine.IsEnabled = false;
        activateMainView();
    }

    /// <summary>Port of <c>btnBLine_Click</c> (source 298-315): mark point B and compute the AB heading.</summary>
    private void btnBLine_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();   // [XPLAT] timer1.Enabled = false
        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos
        ABLine.desPtB = new vec2(pivotAxlePos.easting, pivotAxlePos.northing);
        btnBLine.Background = Avalonia.Media.Brushes.Teal;   // [XPLAT] btnBLine.BackColor = Color.Teal
        btnEnter_AB.IsEnabled = true;

        ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
           ABLine.desPtB.northing - ABLine.desPtA.northing);
        if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(ABLine.desHeading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 1000);
        activateMainView();
    }

    /// <summary>Port of <c>btnEnter_AB_Click</c> (source 317-355): commit the AB track.</summary>
    private void btnEnter_AB_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();   // [XPLAT] timer1.Enabled = false
        ABLine.isMakingABLine = false;
        trk.gArr.Add(new CTrk());

        idx = trk.gArr.Count - 1;

        trk.gArr[idx].ptA = new vec2(ABLine.desPtA);
        trk.gArr[idx].ptB = new vec2(ABLine.desPtB);

        trk.gArr[idx].mode = TrackMode.AB;

        trk.gArr[idx].heading = ABLine.desHeading;

        ABLine.desName = "AB " +
            (Math.Round(glm.toDegrees(ABLine.desHeading), 5)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";

        textBox1.Text = ABLine.desName;
        trk.gArr[idx].name = ABLine.desName;

        double dist;
        if (isRefRightSide)
        {
            dist = (tool.width - tool.overlap) * 0.5 + tool.offset;
            trk.idx = idx;
            trk.NudgeRefABLine(dist);
        }
        else
        {
            dist = (tool.width - tool.overlap) * -0.5 + tool.offset;
            trk.idx = idx;
            trk.NudgeRefABLine(dist);
        }
        panelABLine.IsVisible = false;
        panelName.IsVisible = true;
        activateMainView();
    }

    /// <summary>
    /// Port of <c>timer1_Tick</c> (source 358-371): while building an AB / A+ line, continuously
    /// recompute point B, the heading, and the line extents from the live pivot position.
    /// </summary>
    private void Timer1_Tick(object sender, EventArgs e)
    {
        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos
        ABLine.desPtB = new vec2(pivotAxlePos.easting, pivotAxlePos.northing);

        ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
           ABLine.desPtB.northing - ABLine.desPtA.northing);
        if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(ABLine.desHeading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 1000);
    }

    // =================================================================================================
    // A Plus panel  (source lines 373-468)
    // =================================================================================================

    /// <summary>Port of <c>btnRefSideAPlus_Click</c> (source 375-381).</summary>
    private void btnRefSideAPlus_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        SetButtonImage(btnRefSideAPlus, isRefRightSide ? "BoundaryRight.png" : "BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnAPlus_Click</c> (source 383-406): seed an A+ line from the current heading.</summary>
    private void btnAPlus_Click(object sender, RoutedEventArgs e)
    {
        vec3 pivotAxlePos = getPivotAxlePos();   // [XPLAT] mf.pivotAxlePos

        ABLine.isMakingABLine = true;

        ABLine.desPtA = new vec2(pivotAxlePos.easting, pivotAxlePos.northing);

        ABLine.desPtB.easting = ABLine.desPtA.easting + (Math.Sin(pivotAxlePos.heading) * 1);
        ABLine.desPtB.northing = ABLine.desPtA.northing + (Math.Cos(pivotAxlePos.heading) * 1);

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(pivotAxlePos.heading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(pivotAxlePos.heading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(pivotAxlePos.heading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(pivotAxlePos.heading) * 1000);

        ABLine.desHeading = pivotAxlePos.heading;

        btnEnter_AB.IsEnabled = true;
        nudHeading.IsEnabled = true;

        // [XPLAT] nudHeading.Value = (decimal)glm.toDegrees(...) -> mirror in nudHeadingValue + Content text.
        nudHeadingValue = glm.toDegrees(ABLine.desHeading);
        nudHeading.Content = nudHeadingValue.ToString(CultureInfo.InvariantCulture);
        StartTimer();   // [XPLAT] timer1.Enabled = true
        activateMainView();
    }

    /// <summary>
    /// Port of <c>nudHeading_Click</c> (source 408-428). [XPLAT] the WinForms
    /// <c>NudlessNumericUpDown.ShowKeypad(this)</c> is replaced by the modal <see cref="FormNumeric"/>
    /// keypad (0..360); a true (OK) result re-applies the typed heading.
    /// </summary>
    private async void nudHeading_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();   // [XPLAT] timer1.Enabled = false

        // [XPLAT] ShowKeypad -> FormNumeric (returns true on OK; ReturnValue holds the entered value).
        FormNumeric f = new FormNumeric(0, 360, nudHeadingValue);
        if (await f.ShowDialog<bool>(this))
        {
            nudHeadingValue = f.ReturnValue;
            nudHeading.Content = nudHeadingValue.ToString(CultureInfo.InvariantCulture);

            //original A pt.
            ABLine.desHeading = glm.toRadians(nudHeadingValue);

            //start end of line
            ABLine.desPtB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 200);
            ABLine.desPtB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 200);

            ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(ABLine.desHeading) * 1000);
            ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(ABLine.desHeading) * 1000);

            ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 1000);
            ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 1000);
        }
        activateMainView();
    }

    /// <summary>Port of <c>btnEnter_APlus_Click</c> (source 430-468): commit the A+ track.</summary>
    private void btnEnter_APlus_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();   // [XPLAT] timer1.Enabled = false

        ABLine.isMakingABLine = false;
        trk.gArr.Add(new CTrk());

        idx = trk.gArr.Count - 1;

        trk.gArr[idx].ptA = new vec2(ABLine.desPtA);
        trk.gArr[idx].ptB = new vec2(ABLine.desPtB);

        trk.gArr[idx].mode = TrackMode.AB;

        trk.gArr[idx].heading = ABLine.desHeading;

        ABLine.desName = "A+" +
            (Math.Round(glm.toDegrees(ABLine.desHeading), 5)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";
        textBox1.Text = ABLine.desName;

        double dist;
        if (isRefRightSide)
        {
            dist = (tool.width - tool.overlap) * 0.5 + tool.offset;
            trk.idx = idx;
            trk.NudgeRefABLine(dist);
        }
        else
        {
            dist = (tool.width - tool.overlap) * -0.5 + tool.offset;
            trk.idx = idx;
            trk.NudgeRefABLine(dist);
        }

        panelAPlus.IsVisible = false;
        panelName.IsVisible = true;
        activateMainView();
    }

    // =================================================================================================
    // Name panel + shared handlers  (source lines 473-524)
    // =================================================================================================

    /// <summary>Port of <c>btnAddTime_Click</c> (source 473-477): append a timestamp to the name.</summary>
    private void btnAddTime_Click(object sender, RoutedEventArgs e)
    {
        textBox1.Text += DateTime.Now.ToString(" hh:mm:ss", CultureInfo.InvariantCulture);
        curve.desName = textBox1.Text;
    }

    /// <summary>
    /// Port of <c>btnCancelCurve_Click</c> (source 479-489). [XPLAT] shared cancel for all four panel
    /// cancel buttons (btnCancelChoose / btnCancel_Curve / btnCancel_ABLine / btnCancel_APlus).
    /// </summary>
    private void btnCancelCurve_Click(object sender, RoutedEventArgs e)
    {
        curve.desList?.Clear();

        ABLine.isMakingABLine = false;
        curve.isMakingCurve = false;
        curve.isRecordingCurve = false;

        Close();
        activateMainView();
    }

    /// <summary>
    /// Port of <c>textBox_Click</c> (source 491-495). [XPLAT] TextBox.ShowKeyboard -> FormKeyboard
    /// (returns the edited string on OK, null on cancel).
    /// </summary>
    private async void textBox_Click(object sender, TappedEventArgs e)
    {
        if (isKeyboardOn())
        {
            FormKeyboard kb = new FormKeyboard(textBox1.Text);
            string r = await kb.ShowDialog<string>(this);
            if (r != null) textBox1.Text = r;
        }
    }

    /// <summary>
    /// Port of <c>btnAdd_Click</c> (source 497-524): name and persist the new track, restore steering
    /// state, then close. [XPLAT] FormDialog.Show -> FormDialogView.ShowAsync; the autosteer / youturn
    /// PerformClick calls and FileSaveTracks are routed through injected callbacks.
    /// </summary>
    private async void btnAdd_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] null-safe equivalent of WinForms "textBox1.Text.Length == 0" (Avalonia Text may be null).
        if ((textBox1.Text ?? string.Empty).Length == 0) textBox1.Text = "No Name " + DateTime.Now.ToString("hh:mm:ss", CultureInfo.InvariantCulture);

        int idx = trk.gArr.Count - 1;

        trk.gArr[idx].name = textBox1.Text.Trim();

        panelName.IsVisible = false;

        curve.desList?.Clear();

        if (!isEasyDriveMode()) saveTracks();   // [XPLAT] mf.FileSaveTracks()

        if (isBtnAutoSteerOn())
        {
            performAutoSteerClick();   // [XPLAT] mf.btnAutoSteer.PerformClick()
            // [XPLAT] FormDialog.Show -> FormDialogView.ShowAsync
            await FormDialogView.ShowAsync(gStr.gsGuidanceStopped, "Return From Editing", DialogSeverity.Info, this);
        }
        if (isYouTurnBtnOn()) performAutoYouTurnClick();   // [XPLAT] mf.btnAutoYouTurn.PerformClick()

        ABLine.isMakingABLine = false;
        curve.desList?.Clear();
        trk.idx = idx;

        Close();
    }

    /// <summary>
    /// Port of <c>SmoothAB</c> (source 526-569). Behaviour-frozen centre-weighted smoothing of
    /// <c>curve.desList</c>; ported VERBATIM.
    /// </summary>
    public void SmoothAB(int smPts)
    {
        //countExit the reference list of original curve
        int cnt = curve.desList.Count;

        //the temp array
        vec3[] arr = new vec3[cnt];

        //read the points before and after the setpoint
        for (int s = 0; s < smPts / 2; s++)
        {
            arr[s].easting = curve.desList[s].easting;
            arr[s].northing = curve.desList[s].northing;
            arr[s].heading = curve.desList[s].heading;
        }

        for (int s = cnt - (smPts / 2); s < cnt; s++)
        {
            arr[s].easting = curve.desList[s].easting;
            arr[s].northing = curve.desList[s].northing;
            arr[s].heading = curve.desList[s].heading;
        }

        //average them - center weighted average
        for (int i = smPts / 2; i < cnt - (smPts / 2); i++)
        {
            for (int j = -smPts / 2; j < smPts / 2; j++)
            {
                arr[i].easting += curve.desList[j + i].easting;
                arr[i].northing += curve.desList[j + i].northing;
            }
            arr[i].easting /= smPts;
            arr[i].northing /= smPts;
            arr[i].heading = curve.desList[i].heading;
        }

        //make a list to draw
        curve.desList?.Clear();
        for (int i = 0; i < cnt; i++)
        {
            curve.desList.Add(arr[i]);
        }
        activateMainView();
    }
}
