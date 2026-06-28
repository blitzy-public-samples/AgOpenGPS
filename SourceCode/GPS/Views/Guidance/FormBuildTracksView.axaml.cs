// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;                       // [XPLAT] PixelPoint, Vector
using Avalonia.Controls;              // [XPLAT] Window, Button, TextBox, ScrollViewer, ItemsControl, WindowClosingEventArgs
using Avalonia.Input;                 // [XPLAT] PointerPressedEventArgs, PointerEventArgs, InputElement
using Avalonia.Interactivity;         // [XPLAT] RoutedEventArgs, RoutingStrategies
using Avalonia.Media;                 // [XPLAT] IBrush, IImage, Brushes
using Avalonia.Media.Imaging;         // [XPLAT] Bitmap
using Avalonia.Platform;              // [XPLAT] AssetLoader
using Avalonia.Platform.Storage;      // [XPLAT] StorageProvider, FilePickerOpenOptions, FilePickerFileType, IStorageFolder
using Avalonia.Threading;             // [XPLAT] DispatcherTimer, Dispatcher, DispatcherPriority
using AgLibrary.Logging;              // [XPLAT] Log.EventWriter (portable)
using AgOpenGPS.Core;                 // [XPLAT] ApplicationModel
using AgOpenGPS.Core.Models;          // [XPLAT] Wgs84, GeoCoord
using AgOpenGPS.Core.Translations;    // [XPLAT] gStr
using AgOpenGPS.Properties;           // [XPLAT] Settings (window location persistence)

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for <c>FormBuildTracksView</c> — a 1:1 behavioural and visual parity
/// reimplementation of the WinForms <c>FormBuildTracks</c> (<c>Forms/Guidance/FormBuildTracks.cs</c>
/// 1646 lines + <c>FormBuildTracks.Designer.cs</c>). This is the full <b>Track Manager</b>: a track
/// list (reorder / rename / duplicate / delete / show-hide / swap A↔B) plus a multi-mode track
/// <b>builder</b> reached through a chooser — Curve, AB line, A+, Lat/Lon+Heading, Lat/Lon Lat/Lon,
/// Pivot and KML import. Eleven overlapping sub-panels share the same top-left origin (3,3) and this
/// code-behind shows exactly one at a time, resizing the window per active panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behaviour frozen (AAP §0.2.2 / §0.7.1).</b> Every track-building computation — <c>btnBCurve</c>,
/// the <c>btnEnter_*</c> builders, <c>CalcHeadingAB</c> / <c>CalcHeadingAPlus</c>,
/// <c>FindCircleCenter</c>, <c>btnSwapAB</c>, <c>SmoothAB</c> and the KML build branches — is ported
/// verbatim from the source. All numeric I/O uses <see cref="CultureInfo.InvariantCulture"/> exactly
/// as the source did, the dominant cross-platform data-integrity rule (AAP §0.6.5).
/// </para>
/// <para>
/// <b>No FormGPS god-object.</b> The original reached the whole application through an <c>mf</c>
/// back-reference; here the concrete domain objects (<see cref="trk"/>, <see cref="curve"/>,
/// <see cref="ABLine"/>, <see cref="tool"/>, <see cref="appModel"/>) and a set of <see cref="Func{T}"/>
/// / <see cref="Action"/> callbacks are constructor-injected. Every substitution is tagged
/// <c>// [XPLAT]</c>.
/// </para>
/// <para>
/// <b>Show semantics.</b> The caller shows this non-modally (<c>Show(owner)</c>); the dialog mutates
/// the injected domain objects in place and closes itself. The <see cref="isClosing"/> guard in
/// <see cref="OnClosing"/> reproduces the WinForms <c>e.Cancel</c> behaviour so the window can only be
/// dismissed through the Use / Cancel code paths.
/// </para>
/// </remarks>
public partial class FormBuildTracksView : Window
{
    // ───────────────────────── injected collaborators (replace mf.*) ─────────────────────────
    private readonly CTrack trk;                              // [XPLAT] was mf.trk
    private readonly CABCurve curve;                          // [XPLAT] was mf.curve
    private readonly CABLine ABLine;                          // [XPLAT] was mf.ABLine
    private readonly CTool tool;                              // [XPLAT] was mf.tool
    private readonly ApplicationModel appModel;               // [XPLAT] was mf.AppModel
    private readonly Func<vec3> getPivotAxlePos;              // [XPLAT] was mf.pivotAxlePos (live)
    private readonly Func<string> getCurrentFieldDirectory;  // [XPLAT] was mf.currentFieldDirectory
    private readonly Func<bool> isKeyboardOn;                 // [XPLAT] was mf.isKeyboardOn
    private readonly Func<bool> isBtnAutoSteerOn;             // [XPLAT] was mf.isBtnAutoSteerOn
    private readonly Func<bool> isYouTurnBtnOn;               // [XPLAT] was mf.yt.isYouTurnBtnOn
    private readonly Action performAutoSteerClick;           // [XPLAT] was mf.btnAutoSteer.PerformClick()
    private readonly Action performAutoYouTurnClick;         // [XPLAT] was mf.btnAutoYouTurn.PerformClick()
    private readonly Action resetYouTurn;                    // [XPLAT] was mf.yt.ResetYouTurn()
    private readonly Action disableYouTurnButtons;           // [XPLAT] was mf.DisableYouTurnButtons()
    private readonly Action<int> setTwoSecondCounter;        // [XPLAT] was mf.twoSecondCounter = ...
    private readonly Action panelUpdateRightAndBottom;       // [XPLAT] was mf.PanelUpdateRightAndBottom()
    private readonly Action saveTracks;                      // [XPLAT] was mf.FileSaveTracks()
    private readonly Action activateMainView;                // [XPLAT] was mf.Activate()
    private readonly Action<int, string, string> timedMessageBox; // [XPLAT] was mf.TimedMessageBox(ms,title,msg)
    private readonly string fieldsDirectory;                 // [XPLAT] was RegistrySettings.fieldsDirectory

    // ───────────────────────── source fields (kept EXACTLY as FormBuildTracks.cs) ─────────────────────────
    private double aveLineHeading;
    private int originalLine = 0;
    private bool isClosing;
    private int selectedItem = -1;
    public List<CTrk> gTemp = new List<CTrk>();

    private bool isRefRightSide = true; //left side 0 middle 1 right 2
    private TrackMode mode = TrackMode.None;
    private vec2 ptAa = new vec2();
    private vec2 ptBb = new vec2();

    private bool isOn = true;

    //used throughout to acces the master Track list
    private int idx;

    // Touch drag scrolling (source fields)
    private bool isDragging = false;
    private double dragStartY = 0;                  // [XPLAT] e.Y was int; Avalonia pointer Y is double
    private double dragStartScrollValue = 0;        // [XPLAT] flp.VerticalScroll.Value -> flpScroll.Offset.Y
    private const int DragThreshold = 5;            // pixels to distinguish click from drag
    private bool didDrag = false;
    private DateTime lastDragEndTime = DateTime.MinValue;

    // ───────────────────────── nud backing values ─────────────────────────
    // [XPLAT] The WinForms NudlessNumericUpDown stored a decimal Value; each nud is now a Button (named
    // nudLatitudeA, ... in the XAML) whose Content shows the formatted value while the authoritative
    // value lives in these backing fields (read exactly where the source read (double)nudX.Value). The
    // underscore prefix avoids colliding with the generated control fields of the same logical name.
    private double _nudLatitudeA, _nudLongitudeA, _nudLatitudeB, _nudLongitudeB;
    private double _nudLatitudePlus, _nudLongitudePlus, _nudHeadingLatLonPlus;
    private double _nudHeading;
    private double _nudLatitudePivot, _nudLongitudePivot;

    // ───────────────────────── view infrastructure ─────────────────────────
    /// <summary>[XPLAT] Backing collection for the <c>flp</c> ItemsControl (was the FlowLayoutPanel rows).</summary>
    private readonly ObservableCollection<TrackRow> rows = new ObservableCollection<TrackRow>();

    /// <summary>[XPLAT] Was WinForms <c>timer1</c> (Interval 500 ms). Armed/stopped exactly where the source toggled <c>timer1.Enabled</c>.</summary>
    private readonly DispatcherTimer timer1;

    /// <summary>[XPLAT] avares glyph cache for the runtime <c>Image.Source</c> swaps.</summary>
    private static readonly Dictionary<string, Bitmap> glyphCache = new Dictionary<string, Bitmap>();

    /// <summary>
    /// [XPLAT] Row view-model for the <c>flp</c> ItemsControl template. The XAML row template binds by
    /// reflection (x:CompileBindings="False"); these PROPERTY NAMES are the binding contract:
    /// <c>Name</c>, <c>NameBackground</c>, <c>NameForeground</c>, <c>ModeImage</c>, <c>VisibleBackground</c>.
    /// <see cref="Index"/> carries the original <c>trk.gArr</c> position (was the WinForms <c>Button.Name</c>).
    /// </summary>
    private sealed class TrackRow
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public IBrush NameBackground { get; set; }
        public IBrush NameForeground { get; set; }
        public IImage ModeImage { get; set; }
        public IBrush VisibleBackground { get; set; }
    }

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia XAML loader / design-time previewer. It only
    /// realises the named controls and creates the (idle) timer; the injected behaviour never runs on
    /// this instance, so the design-time view never dereferences the null collaborators.
    /// </summary>
    public FormBuildTracksView()
    {
        InitializeComponent();

        // [XPLAT] timer1 (designer Interval = 500 ms). Created once here; armed in the build flows and
        // stopped/disposed in OnClosed (Avalonia DispatcherTimers are not disposed with the window).
        timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer1.Tick += timer1_Tick;
    }

    /// <summary>
    /// [XPLAT] Injected constructor — replaces the WinForms <c>FormBuildTracks(Form _mf)</c>. Chains the
    /// parameterless ctor (which runs <c>InitializeComponent</c> and creates the timer), stores the
    /// collaborators, applies the localized captions, binds the track-list collection, and wires every
    /// designer-declared handler by <c>x:Name</c> (the XAML declares no <c>Click=</c> attributes).
    /// </summary>
    public FormBuildTracksView(
        CTrack trk,                                   // [XPLAT] was mf.trk
        CABCurve curve,                               // [XPLAT] was mf.curve
        CABLine ABLine,                               // [XPLAT] was mf.ABLine
        CTool tool,                                   // [XPLAT] was mf.tool
        ApplicationModel appModel,                    // [XPLAT] was mf.AppModel
        Func<vec3> getPivotAxlePos,                   // [XPLAT] was mf.pivotAxlePos
        Func<string> getCurrentFieldDirectory,        // [XPLAT] was mf.currentFieldDirectory
        Func<bool> isKeyboardOn,                      // [XPLAT] was mf.isKeyboardOn
        Func<bool> isBtnAutoSteerOn,                  // [XPLAT] was mf.isBtnAutoSteerOn
        Func<bool> isYouTurnBtnOn,                    // [XPLAT] was mf.yt.isYouTurnBtnOn
        Action performAutoSteerClick,                 // [XPLAT] was mf.btnAutoSteer.PerformClick()
        Action performAutoYouTurnClick,               // [XPLAT] was mf.btnAutoYouTurn.PerformClick()
        Action resetYouTurn,                          // [XPLAT] was mf.yt.ResetYouTurn()
        Action disableYouTurnButtons,                 // [XPLAT] was mf.DisableYouTurnButtons()
        Action<int> setTwoSecondCounter,              // [XPLAT] was mf.twoSecondCounter = ...
        Action panelUpdateRightAndBottom,             // [XPLAT] was mf.PanelUpdateRightAndBottom()
        Action saveTracks,                            // [XPLAT] was mf.FileSaveTracks()
        Action activateMainView,                      // [XPLAT] was mf.Activate()
        Action<int, string, string> timedMessageBox,  // [XPLAT] was mf.TimedMessageBox(ms,title,msg)
        string fieldsDirectory)                       // [XPLAT] was RegistrySettings.fieldsDirectory
        : this()
    {
        this.trk = trk;
        this.curve = curve;
        this.ABLine = ABLine;
        this.tool = tool;
        this.appModel = appModel;
        this.getPivotAxlePos = getPivotAxlePos;
        this.getCurrentFieldDirectory = getCurrentFieldDirectory;
        this.isKeyboardOn = isKeyboardOn;
        this.isBtnAutoSteerOn = isBtnAutoSteerOn;
        this.isYouTurnBtnOn = isYouTurnBtnOn;
        this.performAutoSteerClick = performAutoSteerClick;
        this.performAutoYouTurnClick = performAutoYouTurnClick;
        this.resetYouTurn = resetYouTurn;
        this.disableYouTurnButtons = disableYouTurnButtons;
        this.setTwoSecondCounter = setTwoSecondCounter;
        this.panelUpdateRightAndBottom = panelUpdateRightAndBottom;
        this.saveTracks = saveTracks;
        this.activateMainView = activateMainView;
        this.timedMessageBox = timedMessageBox;
        this.fieldsDirectory = fieldsDirectory;

        // [XPLAT] localized captions (source ctor L52-72). this.Text -> Title; Label.Text -> TextBlock.Text.
        Title = gStr.gsTracks;
        labelABLine.Text = gStr.gsABline;
        labelCurve.Text = gStr.gsCurve;
        labelAPlus.Text = gStr.gsAPlus;
        labelABLine.Text = gStr.gsABline;
        labelABLine2.Text = gStr.gsABline;
        labelABCurve.Text = gStr.gsCurve;
        labelCurve2.Text = gStr.gsCurve;
        labelEditName.Text = gStr.gsEnterName;
        labelEnterName.Text = gStr.gsEnterName;
        labelLatLon.Text = gStr.gsLatLon;
        labelLatLonHeading.Text = gStr.gsLatLon + " " + gStr.gsHeading;
        labelLatitude.Text = gStr.gsLatitude;
        labelLongtitude.Text = gStr.gsLongtitude;
        labelPivot.Text = gStr.gsPivot;
        labelHeading.Text = gStr.gsHeading;
        labelLatitudeA.Text = gStr.gsLatitude + " A";
        labelLongtitudeA.Text = gStr.gsLongtitude + " A";
        labelLatitudeB.Text = gStr.gsLatitude + " B";
        labelLongtitudeB.Text = gStr.gsLongtitude + "B";
        labelStatus.Text = gStr.gsStatus + ":";

        // [XPLAT] bind the track list (was flp.Controls populated in UpdateTable).
        flp.ItemsSource = rows;

        // [XPLAT] reproduce every WinForms designer Click subscription (FormBuildTracks.Designer.cs) by
        // x:Name. All ten Cancel buttons route to the shared btnCancelCurve_Click exactly as the designer
        // wired them.
        btnCancelMain.Click += btnCancelMain_Click;
        btnListUse.Click += btnListUse_Click;
        btnListDelete.Click += btnListDelete_Click;
        btnHideShow.Click += btnHideShow_Click;
        btnMoveUp.Click += btnMoveUp_Click;
        btnMoveDn.Click += btnMoveDn_Click;
        btnSwapAB.Click += btnSwapAB_Click;
        btnEditName.Click += btnEditName_Click;
        btnDuplicate.Click += btnDuplicate_Click;
        btnNewTrack.Click += btnNewTrack_Click;

        // Name panel
        btnAdd.Click += btnAdd_Click;
        btnAddTime.Click += btnAddTime_Click;
        btnCancel_Name.Click += btnCancelCurve_Click;

        // Edit-name panel
        btnSaveEditName.Click += btnSaveEditName_Click;
        btnAddTimeEdit.Click += btnAddTimeEdit_Click;
        btnCancel_EditName.Click += btnCancelCurve_Click;

        // Choose panel
        btnzABCurve.Click += btnzABCurve_Click;
        btnzAPlus.Click += btnzAPlus_Click;
        btnzABLine.Click += btnzABLine_Click;
        btnzLatLonPlusHeading.Click += btnzLatLonPlusHeading_Click;
        btnzLatLon.Click += btnzLatLon_Click;
        btnLatLonPivot.Click += btnLatLonPivot_Click;
        btnLatLonPivotCircle.Click += btnLatLonPivot2_Click;
        btnLoadABFromKML.Click += btnLoadABFromKML_Click;
        btnCancelChoose.Click += btnCancelCurve_Click;

        // Curve panel
        btnRefSideCurve.Click += btnRefSideCurve_Click;
        btnACurve.Click += btnACurve_Click;
        btnBCurve.Click += btnBCurve_Click;
        btnPausePlay.Click += btnPausePlayCurve_Click;
        btnCancel_Curve.Click += btnCancelCurve_Click;

        // AB line panel
        btnRefSideAB.Click += btnRefSideAB_Click;
        btnALine.Click += btnALine_Click;
        btnBLine.Click += btnBLine_Click;
        btnEnter_AB.Click += btnEnter_AB_Click;
        btnCancel_ABLine.Click += btnCancelCurve_Click;

        // A+ panel
        btnRefSideAPlus.Click += btnRefSideAPlus_Click;
        btnAPlus.Click += btnAPlus_Click;
        nudHeading.Click += nudHeading_Click;
        btnEnter_APlus.Click += btnEnter_APlus_Click;
        btnCancel_APlus.Click += btnCancelCurve_Click;

        // KML panel
        btnCancel_KML.Click += btnCancelCurve_Click;

        // Lat/Lon + heading panel
        nudLatitudePlus.Click += nudLatitudePlus_Click;
        nudLongitudePlus.Click += nudLongitudePlus_Click;
        nudHeadingLatLonPlus.Click += nudHeadingLatLonPlus_Click;
        btnEnter_LatLonPlus.Click += btnEnter_LatLonPlus_Click;
        btnFillLatLonPlus.Click += btnFillLatLonPlus_Click;
        btnCancel_LatLonPlus.Click += btnCancelCurve_Click;

        // Lat/Lon Lat/Lon panel
        nudLatitudeA.Click += nudLatitudeA_Click;
        nudLongitudeA.Click += nudLongitudeA_Click;
        nudLatitudeB.Click += nudLatitudeB_Click;
        nudLongitudeB.Click += nudLongitudeB_Click;
        btnEnter_LatLonLatLon.Click += btnEnter_LatLonLatLon_Click;
        btnFillLatLonLatLonA.Click += btnFillLatLonLatLonA_Click;
        btnFillLatLonLatLonB.Click += btnFillLatLonLatLonB_Click;
        btnCancelLatLonLatLon.Click += btnCancelCurve_Click;

        // Pivot panel
        nudLatitudePivot.Click += nudLatitudePivot_Click;
        nudLongitudePivot.Click += nudLongitudePivot_Click;
        btnEnter_Pivot.Click += btnEnter_Pivot_Click;
        btnFillLAtLonPivot.Click += btnFillLAtLonPivot_Click;
        btnCancel_Pivot.Click += btnCancelCurve_Click;

        // [XPLAT] Track-list row interaction (was per-row Button.Click + the FlowLayoutPanel mouse
        // handlers). Click bubbles from the row buttons to flp; touch-drag scrolling is reproduced on the
        // ScrollViewer with tunneling pointer handlers so the gesture is seen even over the row buttons.
        flp.AddHandler(Button.ClickEvent, Flp_RowClick, RoutingStrategies.Bubble);
        flpScroll.AddHandler(InputElement.PointerPressedEvent, FlpScroll_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        flpScroll.AddHandler(InputElement.PointerMovedEvent, FlpScroll_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        flpScroll.AddHandler(InputElement.PointerReleasedEvent, FlpScroll_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        flpScroll.PointerExited += Flp_PointerExited;

        // [XPLAT] textBox1/textBox2 Click -> on-screen keyboard. WinForms TextBox.Click has no Avalonia
        // analogue, so a tunneling PointerPressed handler reproduces it.
        textBox1.AddHandler(InputElement.PointerPressedEvent, textBox_Click, RoutingStrategies.Tunnel, handledEventsToo: true);
        textBox2.AddHandler(InputElement.PointerPressedEvent, textBox_Click, RoutingStrategies.Tunnel, handledEventsToo: true);

        // [XPLAT] WinForms Load event -> Avalonia Opened. Subscribed only on the injected instance so the
        // design-time view never runs the load logic against null collaborators.
        this.Opened += OnViewOpened;
    }

    // ═══════════════════════════════════ Helpers ═══════════════════════════════════

    /// <summary>[XPLAT] Replaces every <c>this.Size = new Size(w, h)</c> site. Each call mirrors a source assignment 1:1.</summary>
    private void SetFormSize(int w, int h)
    {
        Width = w;
        Height = h;
    }

    /// <summary>[XPLAT] Loads (and caches) an avares glyph for the runtime <c>Image.Source</c> / mode-icon swaps.</summary>
    private static Bitmap LoadGlyph(string fileName)
    {
        if (!glyphCache.TryGetValue(fileName, out Bitmap bmp))
        {
            bmp = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
            glyphCache[fileName] = bmp;
        }
        return bmp;
    }

    /// <summary>[XPLAT] Sets a nud Button's display text and its authoritative backing value (InvariantCulture).</summary>
    private static void SetNud(Button nud, ref double backing, double value, string fmt)
    {
        backing = value;
        nud.Content = value.ToString(fmt, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Opens the numeric keypad (was <c>NudlessNumericUpDown.ShowKeypad</c>). Returns whether the
    /// user accepted plus the entered value. A tuple is used because C# forbids <c>ref</c> parameters in
    /// async methods, so each caller stores the value via <see cref="SetNud"/> after awaiting.
    /// </summary>
    private async Task<(bool ok, double value)> ShowNumericAsync(double min, double max, double current)
    {
        var form = new FormNumeric(min, max, current);
        bool ok = await form.ShowDialog<bool>(this);
        return (ok, form.ReturnValue);
    }

    /// <summary>[XPLAT] Arms <c>timer1</c> (was <c>timer1.Enabled = true</c>).</summary>
    private void StartTimer() => timer1.Start();

    /// <summary>[XPLAT] Stops <c>timer1</c> (was <c>timer1.Enabled = false</c>).</summary>
    private void StopTimer() => timer1.Stop();

    /// <summary>[XPLAT] Hides every sub-panel; used by the load and shared-cancel flows that show exactly one panel.</summary>
    private void HideAllPanels()
    {
        panelMain.IsVisible = false;
        panelChoose.IsVisible = false;
        panelCurve.IsVisible = false;
        panelName.IsVisible = false;
        panelEditName.IsVisible = false;
        panelABLine.IsVisible = false;
        panelAPlus.IsVisible = false;
        panelKML.IsVisible = false;
        panelLatLonPlus.IsVisible = false;
        panelLatLonLatLon.IsVisible = false;
        panelPivot.IsVisible = false;
    }

    /// <summary>[XPLAT] Builds the row view-model for <c>trk.gArr[i]</c> (was the three per-row WinForms Buttons in UpdateTable).</summary>
    private TrackRow BuildRow(int i)
    {
        CTrk t = trk.gArr[i];
        string glyph = t.mode == TrackMode.AB ? "TrackLine.png"
                     : t.mode == TrackMode.waterPivot ? "TrackPivot.png"
                     : "TrackCurve.png";
        return new TrackRow
        {
            Index = i,
            Name = t.name ?? string.Empty, // [XPLAT] never surface null as text (UI defensive)
            NameBackground = (i == selectedItem) ? Brushes.LightBlue : Brushes.AliceBlue,
            NameForeground = t.isVisible ? Brushes.Black : Brushes.Gray,
            ModeImage = LoadGlyph(glyph),
            VisibleBackground = t.isVisible ? Brushes.Green : Brushes.Red
        };
    }

    /// <summary>[XPLAT] Repopulates the row collection from <c>trk.gArr</c>.</summary>
    private void RebuildRows()
    {
        rows.Clear();
        for (int i = 0; i < trk.gArr.Count; i++)
            rows.Add(BuildRow(i));
    }

    /// <summary>
    /// Port of <c>UpdateTable</c> (FormBuildTracks.cs L266-355). Rebuilds the list from <c>trk.gArr</c>,
    /// preserving the current scroll offset (the WinForms version rebuilt the FlowLayoutPanel and kept the
    /// scroll position). Selection highlight is carried by <see cref="selectedItem"/>.
    /// </summary>
    private void UpdateTable()
    {
        double savedY = flpScroll?.Offset.Y ?? 0;
        RebuildRows();
        RestoreScroll(savedY);
    }

    /// <summary>[XPLAT] Restores a vertical scroll offset after the items are re-laid-out (clamped to the new extent).</summary>
    private void RestoreScroll(double y)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (flpScroll == null) return;
            double max = Math.Max(0, flpScroll.Extent.Height - flpScroll.Viewport.Height);
            double clamped = Math.Max(0, Math.Min(y, max));
            flpScroll.Offset = new Vector(flpScroll.Offset.X, clamped);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>[XPLAT] Approximates the source <c>ScreenHelper.IsOnScreen(Bounds)</c> guard; off-screen ⇒ reposition to (0,0).</summary>
    private void ClampToScreen()
    {
        PixelPoint pos = Position;
        bool onScreen = false;
        if (Screens != null)
        {
            foreach (var sc in Screens.All)
            {
                if (sc.Bounds.Contains(pos)) { onScreen = true; break; }
            }
        }
        if (!onScreen)
            Position = new PixelPoint(0, 0);
    }

    // ═══════════════════════════════════ Lifecycle ═══════════════════════════════════

    /// <summary>
    /// Port of <c>FormBuildTracks_Load</c> (FormBuildTracks.cs L76-150). Snapshots <c>trk.gArr</c> into
    /// <see cref="gTemp"/> (for the Cancel rollback), shows the main panel at 650×480, restores the saved
    /// window position, seeds the lat/lon/heading nuds from the current fix, and builds the track list.
    /// </summary>
    private void OnViewOpened(object sender, EventArgs e)
    {
        idx = trk.gArr.Count - 1;

        gTemp.Clear();
        for (int i = 0; i < trk.gArr.Count; i++)
            gTemp.Add(new CTrk(trk.gArr[i]));

        // [XPLAT] Panels are pre-positioned at (3,3) in the XAML; the source set each panel Top/Left = 3
        // here — omitted. Show the main panel only.
        HideAllPanels();
        panelMain.IsVisible = true;

        SetFormSize(650, 480);

        originalLine = trk.idx;
        selectedItem = originalLine;

        // restore window location
        System.Drawing.Point loc = Properties.Settings.Default.setWindow_buildTracksLocation;
        Position = new PixelPoint(loc.X, loc.Y);

        // [XPLAT] the eight `nud*.Controls[0].Enabled = false` lines (suppress in-place text entry) have no
        // Avalonia analogue — the nud is a Button that opens the keypad — and are omitted.

        double lat = appModel.CurrentLatLon.Latitude;
        double lon = appModel.CurrentLatLon.Longitude;
        SetNud(nudLatitudeA, ref _nudLatitudeA, lat, "F7");
        SetNud(nudLatitudeB, ref _nudLatitudeB, lat + 0.000005, "F7");
        SetNud(nudLongitudeA, ref _nudLongitudeA, lon, "F7");
        SetNud(nudLongitudeB, ref _nudLongitudeB, lon + 0.000005, "F7");
        SetNud(nudLatitudePlus, ref _nudLatitudePlus, lat, "F7");
        SetNud(nudLongitudePlus, ref _nudLongitudePlus, lon, "F7");
        SetNud(nudHeading, ref _nudHeading, 0, "F4");
        SetNud(nudHeadingLatLonPlus, ref _nudHeadingLatLonPlus, 0, "F4");

        UpdateTable();

        ClampToScreen();
    }

    /// <summary>
    /// Port of <c>FormBuildTracks_FormClosing</c> (FormBuildTracks.cs L152-166). While <see cref="isClosing"/>
    /// is false the close is cancelled (the window is only dismissed through the Use / Cancel paths); once a
    /// path sets the flag, the window position is persisted and the host panels are refreshed.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!isClosing)
        {
            e.Cancel = true; // [XPLAT] was e.Cancel = true; return;
            base.OnClosing(e);
            return;
        }

        Properties.Settings.Default.setWindow_buildTracksLocation = new System.Drawing.Point(Position.X, Position.Y);
        Properties.Settings.Default.Save();
        setTwoSecondCounter?.Invoke(100);
        panelUpdateRightAndBottom?.Invoke();

        base.OnClosing(e);
    }

    /// <summary>[XPLAT] Stops and unhooks the dispatcher timer (WinForms disposed timer1 with the form).</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (timer1 != null)
        {
            timer1.Stop();
            timer1.Tick -= timer1_Tick;
        }
        base.OnClosed(e);
    }

    // ═══════════════════════════════════ Main Controls ═══════════════════════════════════

    /// <summary>
    /// [XPLAT] Routes a bubbled <see cref="Button.Click"/> from a track row to the original WinForms
    /// per-row handlers. The row buttons carry style classes — <c>trackName</c> (select),
    /// <c>trackVisible</c> (show/hide toggle), <c>trackMode</c> (drag-only, no click action).
    /// </summary>
    private void Flp_RowClick(object sender, RoutedEventArgs e)
    {
        Button b = e.Source as Button;
        if (b == null && e.Source is Control c)
        {
            // walk up to the nearest Button in case the visual child raised the event
            var p = c.Parent;
            while (p != null && b == null) { b = p as Button; p = p.Parent; }
        }
        if (b == null || b.DataContext is not TrackRow row) return;

        int line = row.Index;
        if (b.Classes.Contains("trackVisible")) A_Click(line);
        else if (b.Classes.Contains("trackName")) LineSelected_Click(line);
        // trackMode: drag-only — no action
    }

    /// <summary>
    /// Port of <c>A_Click</c> (FormBuildTracks.cs L357-374). Toggles the row's visibility, clears the
    /// selection, then refreshes the list (the WinForms version set the row colours directly; rebuilding
    /// produces the identical visible result because the colours are derived from the model + selection).
    /// </summary>
    private void A_Click(int line)
    {
        if (line < 0 || line >= trk.gArr.Count) return;

        trk.gArr[line].isVisible = !trk.gArr[line].isVisible;
        selectedItem = -1;
        UpdateTable();
    }

    /// <summary>
    /// Port of <c>LineSelected_Click</c> (FormBuildTracks.cs L376-419). Selects a visible row (or clears
    /// the selection if the row is hidden). The 200 ms guard suppresses the click that ends a touch-drag.
    /// </summary>
    private void LineSelected_Click(int line)
    {
        // Ignore click if we just finished dragging (within 200ms)
        if ((DateTime.Now - lastDragEndTime).TotalMilliseconds < 200)
            return;

        if (line < 0 || line >= trk.gArr.Count) return;

        if (trk.gArr[line].isVisible)
            selectedItem = line;
        else
            selectedItem = -1;

        UpdateTable();
    }

    // Touch drag scrolling — port of StartDrag/DoDrag/Flp_MouseUp/Flp_MouseLeave (L464-524).
    // [XPLAT] WinForms MouseDown/Move/Up/Leave -> Avalonia pointer events on the ScrollViewer; the
    // WinForms flp.VerticalScroll.Value (an int pixel offset) becomes flpScroll.Offset.Y.
    private void FlpScroll_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(flpScroll);
        if (!pt.Properties.IsLeftButtonPressed) return;

        isDragging = true;
        didDrag = false;
        dragStartY = pt.Position.Y;
        dragStartScrollValue = flpScroll.Offset.Y;
    }

    private void FlpScroll_PointerMoved(object sender, PointerEventArgs e)
    {
        if (!isDragging) return;

        double deltaY = dragStartY - e.GetPosition(flpScroll).Y;

        // Only start actual dragging if moved beyond threshold
        if (Math.Abs(deltaY) > DragThreshold)
        {
            didDrag = true;
            double max = Math.Max(0, flpScroll.Extent.Height - flpScroll.Viewport.Height);
            double newScrollValue = dragStartScrollValue + deltaY;

            // Clamp to valid scroll range
            if (newScrollValue < 0) newScrollValue = 0;
            if (newScrollValue > max) newScrollValue = max;

            flpScroll.Offset = new Vector(flpScroll.Offset.X, newScrollValue);
        }
    }

    private void FlpScroll_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (didDrag)
            lastDragEndTime = DateTime.Now;
        isDragging = false;
        didDrag = false;
    }

    private void Flp_PointerExited(object sender, PointerEventArgs e)
    {
        isDragging = false;
        didDrag = false;
    }

    /// <summary>Port of <c>btnMoveUp_Click</c> (FormBuildTracks.cs L421-440).</summary>
    private void btnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem == -1 || selectedItem == 0)
            return;

        trk.gArr.Reverse(selectedItem - 1, 2);
        selectedItem--;
        idx = selectedItem;

        double scrollPixels = (flpScroll?.Offset.Y ?? 0) - 45;
        if (scrollPixels < 0) scrollPixels = 0;

        RebuildRows();
        RestoreScroll(scrollPixels);
    }

    /// <summary>Port of <c>btnMoveDn_Click</c> (FormBuildTracks.cs L442-462).</summary>
    private void btnMoveDn_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem == -1 || selectedItem == (trk.gArr.Count - 1))
            return;

        trk.gArr.Reverse(selectedItem, 2);
        selectedItem++;
        idx = selectedItem;

        // RestoreScroll clamps to the current maximum (was flp.VerticalScroll.Maximum)
        double scrollPixels = (flpScroll?.Offset.Y ?? 0) + 45;

        RebuildRows();
        RestoreScroll(scrollPixels);
    }

    /// <summary>
    /// Port of <c>btnSwapAB_Click</c> (FormBuildTracks.cs L526-579) — VERBATIM geometry. Reverses the
    /// selected track: for an AB track it swaps A↔B and flips the heading by π; for a curve it reverses
    /// the point list, flips each retained point's heading, and swaps A↔B. The <c>cnt--</c> and the
    /// <c>for (i = 1; i &lt; cnt; i++)</c> bounds are preserved exactly (behavior frozen).
    /// </summary>
    private async void btnSwapAB_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem > -1)
        {
            idx = selectedItem;

            if (trk.gArr[idx].mode == TrackMode.AB)
            {
                vec2 bob = trk.gArr[idx].ptA;
                trk.gArr[idx].ptA = trk.gArr[idx].ptB;
                trk.gArr[idx].ptB = new vec2(bob);

                trk.gArr[idx].heading += Math.PI;
                if (trk.gArr[idx].heading < 0) trk.gArr[idx].heading += glm.twoPI;
                if (trk.gArr[idx].heading > glm.twoPI) trk.gArr[idx].heading -= glm.twoPI;
            }
            else
            {
                int cnt = trk.gArr[idx].curvePts.Count;
                if (cnt > 0)
                {
                    trk.gArr[idx].curvePts.Reverse();

                    vec3[] arr = new vec3[cnt];
                    cnt--;
                    trk.gArr[idx].curvePts.CopyTo(arr);
                    trk.gArr[idx].curvePts.Clear();

                    trk.gArr[idx].heading += Math.PI;
                    if (trk.gArr[idx].heading < 0) trk.gArr[idx].heading += glm.twoPI;
                    if (trk.gArr[idx].heading > glm.twoPI) trk.gArr[idx].heading -= glm.twoPI;

                    for (int i = 1; i < cnt; i++)
                    {
                        vec3 pt3 = arr[i];
                        pt3.heading += Math.PI;
                        if (pt3.heading > glm.twoPI) pt3.heading -= glm.twoPI;
                        if (pt3.heading < 0) pt3.heading += glm.twoPI;
                        trk.gArr[idx].curvePts.Add(new vec3(pt3));
                    }

                    vec2 temp = new vec2(trk.gArr[idx].ptA);

                    (trk.gArr[idx].ptA) = new vec2(trk.gArr[idx].ptB);
                    (trk.gArr[idx].ptB) = new vec2(temp);
                }
            }

            UpdateTable();
            flp.Focus();

            // [XPLAT] FormDialog.Show(...) -> awaited Avalonia FormDialogView
            await FormDialogView.ShowAsync("A B Swapped", "Curve is Reversed", DialogSeverity.Info, this);
        }
    }

    /// <summary>Port of <c>btnHideShow_Click</c> (FormBuildTracks.cs L581-591).</summary>
    private void btnHideShow_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < trk.gArr.Count; i++)
        {
            trk.gArr[i].isVisible = isOn;
        }

        isOn = !isOn;

        UpdateTable();
    }

    /// <summary>Port of <c>btnNewTrack_Click</c> (FormBuildTracks.cs L593-605). No window resize.</summary>
    private void btnNewTrack_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelMain.IsVisible = false;
        panelCurve.IsVisible = false;
        panelName.IsVisible = false;
        panelABLine.IsVisible = false;
        panelAPlus.IsVisible = false;
        panelKML.IsVisible = false;

        curve.desList?.Clear();
        panelChoose.IsVisible = true;
    }

    /// <summary>Port of <c>btnListDelete_Click</c> (FormBuildTracks.cs L607-619).</summary>
    private void btnListDelete_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem > -1)
        {
            trk.gArr.RemoveAt(selectedItem);
            selectedItem = -1;

            trk.idx = trk.gArr.Count - 1;

            UpdateTable();
            flp.Focus();
        }
    }

    /// <summary>Port of <c>btnDuplicate_Click</c> (FormBuildTracks.cs L621-639). Local <c>idx</c> shadows the field (as in source).</summary>
    private void btnDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem > -1)
        {
            int idx = selectedItem;

            panelMain.IsVisible = false;
            panelName.IsVisible = true;
            SetFormSize(270, 360);

            trk.gArr.Add(new CTrk(trk.gArr[idx]));

            idx = trk.gArr.Count - 1;

            selectedItem = -1;

            textBox1.Text = trk.gArr[idx].name + " Copy";
        }
    }

    /// <summary>Port of <c>btnEditName_Click</c> (FormBuildTracks.cs L641-654).</summary>
    private void btnEditName_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem > -1)
        {
            idx = selectedItem;

            textBox2.Text = trk.gArr[idx].name;

            panelMain.IsVisible = false;
            panelEditName.IsVisible = true;

            SetFormSize(270, 360);
        }
    }

    /// <summary>
    /// Port of <c>btnCancelMain_Click</c> (FormBuildTracks.cs L168-196). Sets the close guard, stops
    /// autosteer / you-turn if active, then rolls the track list back to the <see cref="gTemp"/> snapshot
    /// captured on open and closes.
    /// </summary>
    private void btnCancelMain_Click(object sender, RoutedEventArgs e)
    {
        //reload what was
        isClosing = true;
        curve.desList?.Clear();

        if (isBtnAutoSteerOn())
        {
            performAutoSteerClick();
            timedMessageBox(2000, gStr.gsGuidanceStopped, "Return From Editing");
        }
        if (isYouTurnBtnOn()) performAutoYouTurnClick();

        trk.gArr.Clear();

        foreach (var item in gTemp)
        {
            trk.gArr.Add(new CTrk(item));
        }

        trk.idx = originalLine;

        curve.isCurveValid = false;
        ABLine.isABValid = false;

        setTwoSecondCounter(100);

        Close();
    }

    /// <summary>
    /// Port of <c>btnListUse_Click</c> (FormBuildTracks.cs L198-263). Commits the edited list: invalidates
    /// the cached reference lines, saves the tracks, then selects the active track using the exact
    /// three-way branch (selected-visible / first-visible / none-visible) and closes.
    /// </summary>
    private void btnListUse_Click(object sender, RoutedEventArgs e)
    {
        isClosing = true;
        //reset to generate new reference
        curve.isCurveValid = false;
        ABLine.isABValid = false;
        curve.desList?.Clear();

        if (isYouTurnBtnOn()) performAutoYouTurnClick();

        saveTracks();

        if (selectedItem > -1 && trk.gArr.Count > 0 && trk.gArr[selectedItem].isVisible)
        {
            trk.idx = selectedItem;
            resetYouTurn();

            Close();
        }

        else if (trk.gArr.Count > 0)
        {
            bool isOneVis = false;
            int trac = -1;

            foreach (var item in trk.gArr)
            {
                trac++;
                if (item.isVisible)
                {
                    isOneVis = true;
                    break;
                }
            }

            //just choose a visible something
            if (isOneVis)
            {
                trk.idx = trac;
                resetYouTurn();
                Close();
            }
            else //nothing visible
            {
                idx = -1;
                disableYouTurnButtons();
                if (isBtnAutoSteerOn())
                {
                    performAutoSteerClick();
                    timedMessageBox(2000, gStr.gsGuidanceStopped, gStr.gsNoGuidanceLines);
                    Log.EventWriter("Autosteer Stop, No Tracks Available");
                }
                Close();
            }
        }
        else
        {
            idx = -1;
            disableYouTurnButtons();
            if (isYouTurnBtnOn()) performAutoYouTurnClick();

            Close();
        }
    }


    // ═══════════════════════════════════ Pick ═══════════════════════════════════

    /// <summary>Port of <c>btnzABCurve_Click</c> (FormBuildTracks.cs L661-678).</summary>
    private void btnzABCurve_Click(object sender, RoutedEventArgs e)
    {
        mode = TrackMode.Curve;
        panelChoose.IsVisible = false;
        panelCurve.IsVisible = true;

        btnACurve.IsEnabled = true;
        imgACurve.Source = LoadGlyph("LetterABlue.png");
        btnBCurve.IsEnabled = false;
        btnPausePlay.IsEnabled = false;
        imgPausePlay.Source = LoadGlyph("boundaryPause.png");
        curve.desList?.Clear();

        SetFormSize(270, 360);
        activateMainView();
    }

    /// <summary>Port of <c>btnzAPlus_Click</c> (FormBuildTracks.cs L680-690).</summary>
    private void btnzAPlus_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelAPlus.IsVisible = true;

        btnAPlus.IsEnabled = true;
        curve.desList?.Clear();
        nudHeading.IsEnabled = false;

        SetFormSize(270, 360);
        activateMainView();
    }

    /// <summary>Port of <c>btnzABLine_Click</c> (FormBuildTracks.cs L692-704).</summary>
    private void btnzABLine_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelABLine.IsVisible = true;

        btnALine.IsEnabled = true;
        btnBLine.IsEnabled = false;
        btnEnter_AB.IsEnabled = false;
        curve.desList?.Clear();

        SetFormSize(270, 360);
        activateMainView();
    }

    /// <summary>Port of <c>btnzLatLonPlusHeading_Click</c> (FormBuildTracks.cs L706-715).</summary>
    private void btnzLatLonPlusHeading_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelLatLonPlus.IsVisible = true;
        SetFormSize(370, 460);

        SetNud(nudLatitudePlus, ref _nudLatitudePlus, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudePlus, ref _nudLongitudePlus, appModel.CurrentLatLon.Longitude, "F7");
        activateMainView();
    }

    /// <summary>Port of <c>btnzLatLon_Click</c> (FormBuildTracks.cs L717-723).</summary>
    private void btnzLatLon_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelLatLonLatLon.IsVisible = true;
        SetFormSize(370, 460);
        activateMainView();
    }

    /// <summary>Port of <c>btnLatLonPivot_Click</c> (FormBuildTracks.cs L724-734).</summary>
    private void btnLatLonPivot_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelPivot.IsVisible = true;
        SetFormSize(370, 360);

        SetNud(nudLatitudePivot, ref _nudLatitudePivot, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudePivot, ref _nudLongitudePivot, appModel.CurrentLatLon.Longitude, "F7");
        activateMainView();
    }

    /// <summary>Port of <c>btnLatLonPivot2_Click</c> (FormBuildTracks.cs L736-754) — the water-pivot circle builder.</summary>
    private void btnLatLonPivot2_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelCurve.IsVisible = true;

        curve.isMakingCurve = true;
        curve.isRecordingCurve = false;

        btnRefSideCurve.IsVisible = false;
        btnPausePlay.IsEnabled = false;
        imgPausePlay.Source = LoadGlyph("PointDelete.png");
        mode = TrackMode.waterPivot;
        imgACurve.Source = LoadGlyph("PointAdd.png");
        btnACurve.IsEnabled = true;
        btnBCurve.IsEnabled = false;

        curve.desList?.Clear();

        SetFormSize(270, 360);
        activateMainView();
    }

    // ═══════════════════════════════════ Curve ═══════════════════════════════════

    /// <summary>Port of <c>btnRefSideCurve_Click</c> (FormBuildTracks.cs L759-765).</summary>
    private void btnRefSideCurve_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        imgRefSideCurve.Source = isRefRightSide
            ? LoadGlyph("BoundaryRight.png")
            : LoadGlyph("BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnACurve_Click</c> (FormBuildTracks.cs L767-792).</summary>
    private void btnACurve_Click(object sender, RoutedEventArgs e)
    {
        if (curve.isMakingCurve)
        {
            vec3 pap = getPivotAxlePos();
            curve.desList.Add(new vec3(pap.easting, pap.northing, pap.heading));
            btnBCurve.IsEnabled = curve.desList.Count > 2;
            if (mode == TrackMode.waterPivot)
            {
                btnPausePlay.IsEnabled = curve.desList.Count > 0;
                btnACurve.IsEnabled = curve.desList.Count < 3;
            }
        }
        else
        {
            vec3 pap = getPivotAxlePos();
            lblCurveExists.Text = gStr.gsDriving;
            ptAa.easting = pap.easting;
            ptAa.northing = pap.northing;

            btnBCurve.IsEnabled = true;
            btnACurve.IsEnabled = false;
            imgACurve.Source = LoadGlyph("PointAdd.png");
            btnPausePlay.IsEnabled = true;

            curve.isMakingCurve = true;
            curve.isRecordingCurve = true;
        }
        activateMainView();
    }

    /// <summary>
    /// Port of <c>btnBCurve_Click</c> (FormBuildTracks.cs L794-897) — VERBATIM builder. Closes the curve
    /// recording and creates a track: a water-pivot circle (centre via <see cref="FindCircleCenter"/>), a
    /// minimum-spaced + smoothed curve (with average heading + reference nudge), or — for ≤2 points —
    /// discards and returns to the main panel.
    /// </summary>
    private void btnBCurve_Click(object sender, RoutedEventArgs e)
    {
        aveLineHeading = 0;
        curve.isMakingCurve = false;
        curve.isRecordingCurve = false;
        panelCurve.IsVisible = false;
        panelName.IsVisible = true;

        vec3 pap = getPivotAxlePos();
        ptBb.easting = pap.easting;
        ptBb.northing = pap.northing;

        int cnt = curve.desList.Count;
        if (mode == TrackMode.waterPivot && cnt > 2)
        {
            trk.gArr.Add(new CTrk());
            //array number is 1 less since it starts at zero
            idx = trk.gArr.Count - 1;

            trk.gArr[idx].ptA = FindCircleCenter(curve.desList[0], curve.desList[1], curve.desList[2]);
            trk.gArr[idx].mode = TrackMode.waterPivot;
            ABLine.desName = "Piv";
            textBox1.Text = ABLine.desName;

            panelPivot.IsVisible = false;
            panelName.IsVisible = true;
        }
        else if (cnt > 2)
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
            curve.desList?.Clear();

            panelMain.IsVisible = true;
            panelCurve.IsVisible = false;
            panelName.IsVisible = false;
            panelChoose.IsVisible = false;

            SetFormSize(650, 480);
        }
        activateMainView();
    }

    /// <summary>Port of <c>btnPausePlayCurve_Click</c> (FormBuildTracks.cs L899-922).</summary>
    private void btnPausePlayCurve_Click(object sender, RoutedEventArgs e)
    {
        if (mode == TrackMode.waterPivot)
        {
            if (curve.desList.Count > 0) curve.desList.RemoveAt(curve.desList.Count - 1);
            btnPausePlay.IsEnabled = curve.desList.Count > 0;
            btnACurve.IsEnabled = curve.desList.Count < 3;
        }
        else if (curve.isRecordingCurve)
        {
            curve.isRecordingCurve = false;
            imgPausePlay.Source = LoadGlyph("BoundaryRecord.png");
            btnACurve.IsEnabled = true;
        }
        else
        {
            curve.isRecordingCurve = true;
            imgPausePlay.Source = LoadGlyph("boundaryPause.png");
            btnACurve.IsEnabled = false;
        }
        btnBCurve.IsEnabled = curve.desList.Count > 2;
        activateMainView();
    }


    // ═══════════════════════════════════ AB Line ═══════════════════════════════════

    /// <summary>Port of <c>btnRefSideAB_Click</c> (FormBuildTracks.cs L927-933).</summary>
    private void btnRefSideAB_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        imgRefSideAB.Source = isRefRightSide
            ? LoadGlyph("BoundaryRight.png")
            : LoadGlyph("BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnALine_Click</c> (FormBuildTracks.cs L935-955). Starts the live AB timer.</summary>
    private void btnALine_Click(object sender, RoutedEventArgs e)
    {
        vec3 pap = getPivotAxlePos();

        ABLine.isMakingABLine = true;
        btnALine.IsEnabled = false;
        btnEnter_AB.IsEnabled = false;

        ABLine.desPtA = new vec2(pap.easting, pap.northing);

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(pap.heading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(pap.heading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(pap.heading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(pap.heading) * 1000);

        btnBLine.IsEnabled = true;
        btnALine.IsEnabled = false;

        StartTimer();
        activateMainView();
    }

    /// <summary>Port of <c>btnBLine_Click</c> (FormBuildTracks.cs L957-977). Stops the timer, fixes B, colours the B button Teal.</summary>
    private void btnBLine_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();
        btnEnter_AB.IsEnabled = true;

        vec3 pap = getPivotAxlePos();
        ABLine.desPtB = new vec2(pap.easting, pap.northing);

        btnBLine.Background = Brushes.Teal; // [XPLAT] was btnBLine.BackColor = Color.Teal

        ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
           ABLine.desPtB.northing - ABLine.desPtA.northing);
        if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(ABLine.desHeading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 1000);
        activateMainView();
    }

    /// <summary>Port of <c>btnEnter_AB_Click</c> (FormBuildTracks.cs L979-1019). Builds the AB track and nudges to the reference side. No window resize (stays 270×360).</summary>
    private void btnEnter_AB_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();

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

    // ═══════════════════════════════════ A Plus ═══════════════════════════════════

    /// <summary>Port of <c>btnRefSideAPlus_Click</c> (FormBuildTracks.cs L1027-1033).</summary>
    private void btnRefSideAPlus_Click(object sender, RoutedEventArgs e)
    {
        isRefRightSide = !isRefRightSide;
        imgRefSideAPlus.Source = isRefRightSide
            ? LoadGlyph("BoundaryRight.png")
            : LoadGlyph("BoundaryLeft.png");
        activateMainView();
    }

    /// <summary>Port of <c>btnAPlus_Click</c> (FormBuildTracks.cs L1035-1057). Seeds the heading nud and starts the live timer.</summary>
    private void btnAPlus_Click(object sender, RoutedEventArgs e)
    {
        vec3 pap = getPivotAxlePos();

        ABLine.isMakingABLine = true;

        ABLine.desPtA = new vec2(pap.easting, pap.northing);

        ABLine.desPtB.easting = ABLine.desPtA.easting + (Math.Sin(pap.heading) * 1);
        ABLine.desPtB.northing = ABLine.desPtA.northing + (Math.Cos(pap.heading) * 1);

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(pap.heading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(pap.heading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(pap.heading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(pap.heading) * 1000);

        ABLine.desHeading = pap.heading;

        btnEnter_AB.IsEnabled = true;
        nudHeading.IsEnabled = true;

        SetNud(nudHeading, ref _nudHeading, glm.toDegrees(ABLine.desHeading), "F4");

        StartTimer();
        activateMainView();
    }

    /// <summary>
    /// Port of <c>nudHeading_Click</c> (FormBuildTracks.cs L1059-1080). Stops the timer and, if the keypad
    /// is accepted, recomputes the A+ line from the entered heading (×200 segment, ±1000 end extensions).
    /// </summary>
    private async void nudHeading_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();

        var (ok, val) = await ShowNumericAsync(0, 360, _nudHeading);
        if (ok)
        {
            SetNud(nudHeading, ref _nudHeading, val, "F4");

            //original A pt. 
            ABLine.desHeading = glm.toRadians(_nudHeading);

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

    /// <summary>Port of <c>btnEnter_APlus_Click</c> (FormBuildTracks.cs L1082-1119). Note the name prefix is "A+" with no trailing space (bug-for-bug).</summary>
    private void btnEnter_APlus_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();

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

    /// <summary>
    /// Port of <c>timer1_Tick</c> (FormBuildTracks.cs L1123-1136). Live-updates the provisional AB/A+ line
    /// from the current pivot position while the user is placing a line (500 ms cadence).
    /// </summary>
    private void timer1_Tick(object sender, EventArgs e)
    {
        vec3 pap = getPivotAxlePos();
        ABLine.desPtB = new vec2(pap.easting, pap.northing);

        ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
           ABLine.desPtB.northing - ABLine.desPtA.northing);
        if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;

        ABLine.desLineEndA.easting = ABLine.desPtA.easting - (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndA.northing = ABLine.desPtA.northing - (Math.Cos(ABLine.desHeading) * 1000);

        ABLine.desLineEndB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 1000);
        ABLine.desLineEndB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 1000);
    }


    // ═══════════════════════════════════ KML Curve and line ═══════════════════════════════════

    /// <summary>
    /// Port of <c>btnLoadABFromKML_Click</c> (FormBuildTracks.cs L1140-1322). Imports one or more tracks
    /// from a KML file. [XPLAT] The WinForms <c>OpenFileDialog</c> is replaced with Avalonia's
    /// <c>StorageProvider.OpenFilePickerAsync</c> (initial directory = field directory, filtered to
    /// *.kml/*.KML); everything from <c>XmlDocument.Load</c> onward — including the
    /// <c>double.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture, ...)</c> coordinate
    /// parsing, the WGS-84→local conversion, and the 2-point AB vs &gt;2-point curve build branches — is
    /// ported verbatim (behavior frozen, §0.6.5 culture invariance preserved).
    /// </summary>
    private async void btnLoadABFromKML_Click(object sender, RoutedEventArgs e)
    {
        panelChoose.IsVisible = false;
        panelKML.IsVisible = true;

        curve.desList?.Clear();

        SetFormSize(270, 360);

        string fileAndDirectory;
        {
            // [XPLAT] OpenFileDialog -> Avalonia StorageProvider file picker
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            string initialDir = Path.Combine(fieldsDirectory, getCurrentFieldDirectory());
            IStorageFolder startFolder = null;
            if (!string.IsNullOrEmpty(initialDir))
                startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(initialDir);

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "KML files (*.KML)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KML files (*.KML)") { Patterns = new[] { "*.kml", "*.KML" } }
                }
            });

            //was a file selected
            if (files == null || files.Count == 0) return;
            else fileAndDirectory = files[0].Path.LocalPath;
        }

        XmlDocument doc = new XmlDocument
        {
            PreserveWhitespace = false
        };

        try
        {
            // [XPLAT] CP9 F3 (CWE-611 XXE hardening): the net48 original loaded user-selected KML directly via
            // XmlDocument.Load, which honors DTDs and external entities. Load through a hardened XmlReader that
            // PROHIBITS DTD processing (blocking DOCTYPE-based XXE and entity-expansion / billion-laughs) and nulls
            // the XmlResolver (blocking external entity/resource resolution). KML carries no DTD/DOCTYPE — it is
            // XSD-namespaced — so a well-formed track file parses to an identical DOM; only hostile XML is rejected.
            XmlReaderSettings kmlReaderSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 256L * 1024L * 1024L // generous 256M-char bound; never trips legitimate fields
            };
            using (XmlReader kmlReader = XmlReader.Create(fileAndDirectory, kmlReaderSettings))
            {
                doc.Load(kmlReader);
            }
            string trackName = Path.GetFileName(fileAndDirectory);
            trackName = trackName.Substring(0, trackName.Length - 4);

            XmlElement root = doc.DocumentElement;
            XmlNodeList trackList = root.GetElementsByTagName("coordinates");
            XmlNodeList namelist = root.GetElementsByTagName("name");

            if (namelist.Count > 1)
            {
                trackName = namelist[1].InnerText;
            }

            //each element in the list is a track
            for (int i = 0; i < trackList.Count; i++)
            {
                string line = trackList[i].InnerText;
                line.Trim();
                //line = coordinates;
                char[] delimiterChars = { ' ', '\t', '\r', '\n' };
                string[] numberSets = line.Split(delimiterChars);

                //at least 3 points
                if (numberSets.Length > 1)
                {
                    foreach (string item in numberSets)
                    {
                        string[] fix = item.Split(',');
                        if (fix.Length != 3) continue;
                        double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lonK);
                        double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double latK);

                        GeoCoord geoCoord = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(latK, lonK));
                        curve.desList.Add(new vec3(geoCoord));
                    }
                }

                //2 points
                if (curve.desList.Count == 2)
                {
                    ABLine.desPtA.easting = curve.desList[0].easting;
                    ABLine.desPtA.northing = curve.desList[0].northing;

                    ABLine.desPtB.easting = curve.desList[1].easting;
                    ABLine.desPtB.northing = curve.desList[1].northing;

                    // heading based on AB points
                    ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
                        ABLine.desPtB.northing - ABLine.desPtA.northing);
                    if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;

                    if (namelist.Count > i)
                    {
                        trackName = namelist[i + 1].InnerText;
                        ABLine.desName = trackName;
                    }
                    else ABLine.desName = "AB " +
                        (Math.Round(glm.toDegrees(ABLine.desHeading), 5)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";

                    trk.gArr.Add(new CTrk());

                    idx = trk.gArr.Count - 1;

                    trk.gArr[idx].heading = ABLine.desHeading;
                    trk.gArr[idx].mode = TrackMode.AB;

                    trk.gArr[idx].ptA = new vec2(ABLine.desPtA);
                    trk.gArr[idx].ptB = new vec2(ABLine.desPtB);

                    //create a name
                    trk.gArr[idx].name = ABLine.desName;

                    curve.desList?.Clear();
                }
                else if (curve.desList.Count > 2)
                {
                    //make sure point distance isn't too big 
                    CABCurve.MakePointMinimumSpacing(ref curve.desList, 1.6);
                    CABCurve.CalculateHeadings(ref curve.desList);

                    trk.gArr.Add(new CTrk());

                    //array number is 1 less since it starts at zero
                    idx = trk.gArr.Count - 1;

                    trk.gArr[idx].ptA =
                        new vec2(curve.desList[0].easting, curve.desList[0].northing);
                    trk.gArr[idx].ptB =
                        new vec2(curve.desList[curve.desList.Count - 1].easting,
                        curve.desList[curve.desList.Count - 1].northing);

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
                    //SmoothAB(4);
                    CABCurve.CalculateHeadings(ref curve.desList);

                    //write out the Curve Points
                    foreach (vec3 item in curve.desList)
                    {
                        trk.gArr[idx].curvePts.Add(new vec3(item));
                    }
                    if (namelist.Count > i)
                    {
                        trackName = namelist[i + 1].InnerText;
                        curve.desName = trackName;
                    }
                    else curve.desName = "Cu " +
                             (Math.Round(glm.toDegrees(aveLineHeading), 1)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";

                    trk.gArr[idx].name = curve.desName;

                    curve.desList?.Clear();
                }
                else
                {
                    // [XPLAT] FormDialog.Show(...) -> awaited Avalonia FormDialogView
                    await FormDialogView.ShowAsync(gStr.gsErrorreadingKML, gStr.gsMissingABLinesFile, DialogSeverity.Error, this);
                }
            }
        }
        catch (Exception ed)
        {
            Log.EventWriter("Tracks from KML " + ed.ToString());
            return;
        }

        panelKML.IsVisible = false;
        panelName.IsVisible = false;
        panelMain.IsVisible = true;

        SetFormSize(650, 480);

        curve.desList?.Clear();

        UpdateTable();
        flp.Focus();
    }


    // ═══════════════════════════════════ LatLon LatLon ═══════════════════════════════════

    /// <summary>Port of <c>nudLatitudeA_Click</c> (FormBuildTracks.cs L1330-1333). Latitude range ±90, 7 d.p.</summary>
    private async void nudLatitudeA_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-90, 90, _nudLatitudeA);
        if (ok) SetNud(nudLatitudeA, ref _nudLatitudeA, val, "F7");
    }

    /// <summary>Port of <c>nudLongitudeA_Click</c> (FormBuildTracks.cs L1335-1338). Longitude range ±180, 7 d.p.</summary>
    private async void nudLongitudeA_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-180, 180, _nudLongitudeA);
        if (ok) SetNud(nudLongitudeA, ref _nudLongitudeA, val, "F7");
    }

    /// <summary>Port of <c>nudLatitudeB_Click</c> (FormBuildTracks.cs L1340-1343).</summary>
    private async void nudLatitudeB_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-90, 90, _nudLatitudeB);
        if (ok) SetNud(nudLatitudeB, ref _nudLatitudeB, val, "F7");
    }

    /// <summary>Port of <c>nudLongitudeB_Click</c> (FormBuildTracks.cs L1345-1348).</summary>
    private async void nudLongitudeB_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-180, 180, _nudLongitudeB);
        if (ok) SetNud(nudLongitudeB, ref _nudLongitudeB, val, "F7");
    }

    /// <summary>Port of <c>btnEnter_LatLonLatLon_Click</c> (FormBuildTracks.cs L1350-1373).</summary>
    private void btnEnter_LatLonLatLon_Click(object sender, RoutedEventArgs e)
    {
        CalcHeadingAB();

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

        panelLatLonLatLon.IsVisible = false;
        panelName.IsVisible = true;

        SetFormSize(270, 360);
    }

    /// <summary>Port of <c>CalcHeadingAB</c> (FormBuildTracks.cs L1376-1389) — VERBATIM.</summary>
    public void CalcHeadingAB()
    {
        GeoCoord geoCoord = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(_nudLatitudeA, _nudLongitudeA));

        ABLine.desPtA = new vec2(geoCoord);

        geoCoord = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(_nudLatitudeB, _nudLongitudeB));
        ABLine.desPtB = new vec2(geoCoord);

        // heading based on AB points
        ABLine.desHeading = Math.Atan2(ABLine.desPtB.easting - ABLine.desPtA.easting,
            ABLine.desPtB.northing - ABLine.desPtA.northing);
        if (ABLine.desHeading < 0) ABLine.desHeading += glm.twoPI;
    }

    /// <summary>Port of <c>btnFillLatLonLatLonA_Click</c> (FormBuildTracks.cs L1391-1395).</summary>
    private void btnFillLatLonLatLonA_Click(object sender, RoutedEventArgs e)
    {
        SetNud(nudLatitudeA, ref _nudLatitudeA, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudeA, ref _nudLongitudeA, appModel.CurrentLatLon.Longitude, "F7");
    }

    /// <summary>Port of <c>btnFillLatLonLatLonB_Click</c> (FormBuildTracks.cs L1397-1401).</summary>
    private void btnFillLatLonLatLonB_Click(object sender, RoutedEventArgs e)
    {
        SetNud(nudLatitudeB, ref _nudLatitudeB, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudeB, ref _nudLongitudeB, appModel.CurrentLatLon.Longitude, "F7");
    }

    // ═══════════════════════════════════ LatLon + ═══════════════════════════════════

    /// <summary>Port of <c>nudLatitudePlus_Click</c> (FormBuildTracks.cs L1409-1412).</summary>
    private async void nudLatitudePlus_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-90, 90, _nudLatitudePlus);
        if (ok) SetNud(nudLatitudePlus, ref _nudLatitudePlus, val, "F7");
    }

    /// <summary>Port of <c>nudLongitudePlus_Click</c> (FormBuildTracks.cs L1414-1417).</summary>
    private async void nudLongitudePlus_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-180, 180, _nudLongitudePlus);
        if (ok) SetNud(nudLongitudePlus, ref _nudLongitudePlus, val, "F7");
    }

    /// <summary>Port of <c>nudHeadingLatLonPlus_Click</c> (FormBuildTracks.cs L1419-1422). Heading range 0..360, 4 d.p.</summary>
    private async void nudHeadingLatLonPlus_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(0, 360, _nudHeadingLatLonPlus);
        if (ok) SetNud(nudHeadingLatLonPlus, ref _nudHeadingLatLonPlus, val, "F4");
    }

    /// <summary>Port of <c>btnEnter_LatLonPlus_Click</c> (FormBuildTracks.cs L1424-1450). Name prefix "A+ " has a trailing space here (differs from the A Plus panel — bug-for-bug).</summary>
    private void btnEnter_LatLonPlus_Click(object sender, RoutedEventArgs e)
    {
        CalcHeadingAPlus();

        ABLine.isMakingABLine = false;
        trk.gArr.Add(new CTrk());

        idx = trk.gArr.Count - 1;

        //start end of line
        ABLine.desPtB.easting = ABLine.desPtA.easting + (Math.Sin(ABLine.desHeading) * 200);
        ABLine.desPtB.northing = ABLine.desPtA.northing + (Math.Cos(ABLine.desHeading) * 200);

        trk.gArr[idx].ptA = new vec2(ABLine.desPtA);
        trk.gArr[idx].ptB = new vec2(ABLine.desPtB);

        trk.gArr[idx].mode = TrackMode.AB;

        trk.gArr[idx].heading = ABLine.desHeading;

        ABLine.desName = "A+ " +
            (Math.Round(glm.toDegrees(ABLine.desHeading), 5)).ToString(CultureInfo.InvariantCulture) + "\u00B0 ";
        textBox1.Text = ABLine.desName;

        panelLatLonPlus.IsVisible = false;
        panelName.IsVisible = true;

        SetFormSize(270, 360);
    }

    /// <summary>Port of <c>btnFillLatLonPlus_Click</c> (FormBuildTracks.cs L1452-1456).</summary>
    private void btnFillLatLonPlus_Click(object sender, RoutedEventArgs e)
    {
        SetNud(nudLatitudePlus, ref _nudLatitudePlus, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudePlus, ref _nudLongitudePlus, appModel.CurrentLatLon.Longitude, "F7");
    }

    /// <summary>Port of <c>CalcHeadingAPlus</c> (FormBuildTracks.cs L1458-1464) — VERBATIM.</summary>
    public void CalcHeadingAPlus()
    {
        GeoCoord geoCoord = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(_nudLatitudePlus, _nudLongitudePlus));

        ABLine.desHeading = glm.toRadians(_nudHeadingLatLonPlus);
        ABLine.desPtA = new vec2(geoCoord);
    }

    // ═══════════════════════════════════ Lat Lon Pivot ═══════════════════════════════════

    /// <summary>Port of <c>nudLatitudePivot_Click</c> (FormBuildTracks.cs L1472-1475).</summary>
    private async void nudLatitudePivot_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-90, 90, _nudLatitudePivot);
        if (ok) SetNud(nudLatitudePivot, ref _nudLatitudePivot, val, "F7");
    }

    /// <summary>Port of <c>nudLongitudePivot_Click</c> (FormBuildTracks.cs L1477-1480).</summary>
    private async void nudLongitudePivot_Click(object sender, RoutedEventArgs e)
    {
        var (ok, val) = await ShowNumericAsync(-180, 180, _nudLongitudePivot);
        if (ok) SetNud(nudLongitudePivot, ref _nudLongitudePivot, val, "F7");
    }

    /// <summary>Port of <c>btnEnter_Pivot_Click</c> (FormBuildTracks.cs L1482-1500).</summary>
    private void btnEnter_Pivot_Click(object sender, RoutedEventArgs e)
    {
        GeoCoord geoCoord = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(_nudLatitudePivot, _nudLongitudePivot));

        trk.gArr.Add(new CTrk());

        idx = trk.gArr.Count - 1;

        trk.gArr[idx].ptA = new vec2(geoCoord);
        trk.gArr[idx].mode = TrackMode.waterPivot;

        ABLine.desName = "Piv";
        textBox1.Text = ABLine.desName;

        panelPivot.IsVisible = false;
        panelName.IsVisible = true;

        SetFormSize(270, 360);
        activateMainView();
    }

    /// <summary>Port of <c>FindCircleCenter</c> (FormBuildTracks.cs L1501-1513) — VERBATIM. Returns the circumcentre of three points, or a zero vector if they are (near-)collinear.</summary>
    private vec2 FindCircleCenter(vec3 p1, vec3 p2, vec3 p3)
    {
        var d2 = p2.northing * p2.northing + p2.easting * p2.easting;
        var bc = (p1.northing * p1.northing + p1.easting * p1.easting - d2) / 2;
        var cd = (d2 - p3.northing * p3.northing - p3.easting * p3.easting) / 2;
        var det = (p1.northing - p2.northing) * (p2.easting - p3.easting) - (p2.northing - p3.northing) * (p1.easting - p2.easting);
        if (Math.Abs(det) > 1e-10)
            return new vec2(
          ((p1.northing - p2.northing) * cd - (p2.northing - p3.northing) * bc) / det,
          (bc * (p2.easting - p3.easting) - cd * (p1.easting - p2.easting)) / det
        );
        else return new vec2();
    }

    /// <summary>Port of <c>btnFillLAtLonPivot_Click</c> (FormBuildTracks.cs L1515-1519).</summary>
    private void btnFillLAtLonPivot_Click(object sender, RoutedEventArgs e)
    {
        SetNud(nudLatitudePivot, ref _nudLatitudePivot, appModel.CurrentLatLon.Latitude, "F7");
        SetNud(nudLongitudePivot, ref _nudLongitudePivot, appModel.CurrentLatLon.Longitude, "F7");
    }


    // ═══════════════════════════════════ Shared ═══════════════════════════════════

    /// <summary>
    /// Port of <c>btnCancelCurve_Click</c> (FormBuildTracks.cs L1523-1544). The single shared Cancel for
    /// every build sub-panel (wired to all ten Cancel buttons by the designer). Clears the in-progress
    /// making/recording flags, returns to the main panel and restores the 650×480 window size.
    /// </summary>
    private void btnCancelCurve_Click(object sender, RoutedEventArgs e)
    {
        curve.isMakingCurve = false;
        curve.isRecordingCurve = false;
        curve.desList?.Clear();
        ABLine.isMakingABLine = false;

        panelMain.IsVisible = true;
        panelEditName.IsVisible = false;
        panelName.IsVisible = false;
        panelChoose.IsVisible = false;
        panelCurve.IsVisible = false;
        panelABLine.IsVisible = false;
        panelAPlus.IsVisible = false;
        panelLatLonLatLon.IsVisible = false;
        panelLatLonPlus.IsVisible = false;
        panelKML.IsVisible = false;
        panelPivot.IsVisible = false;

        SetFormSize(650, 480);
        activateMainView();
    }

    /// <summary>
    /// Port of <c>textBox_Click</c> (FormBuildTracks.cs L1546-1550). [XPLAT] WinForms <c>TextBox.Click</c>
    /// has no Avalonia analogue, so this is invoked from a tunneling PointerPressed handler. When the
    /// on-screen keyboard is enabled, edits the field through <see cref="FormKeyboard"/>.
    /// </summary>
    private async void textBox_Click(object sender, PointerPressedEventArgs e)
    {
        if (isKeyboardOn() && sender is TextBox tb)
        {
            var kb = new FormKeyboard(tb.Text ?? string.Empty);
            string result = await kb.ShowDialog<string>(this);
            if (result != null)
                tb.Text = result;
        }
    }

    /// <summary>
    /// Port of <c>btnAdd_Click</c> (FormBuildTracks.cs L1552-1568). Commits the new track's name from
    /// <c>textBox1</c>. Bug-for-bug: when <c>textBox1</c> is empty the placeholder is written to
    /// <c>textBox2</c> (not <c>textBox1</c>), and the local <c>idx</c> shadows the field — both preserved.
    /// </summary>
    private void btnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (textBox1.Text.Length == 0) textBox2.Text = "No Name " + DateTime.Now.ToString("hh:mm:ss", CultureInfo.InvariantCulture);

        int idx = trk.gArr.Count - 1;

        trk.gArr[idx].name = textBox1.Text.Trim();

        panelMain.IsVisible = true;
        panelName.IsVisible = false;

        SetFormSize(650, 480);

        curve.desList?.Clear();
        UpdateTable();
        activateMainView();
    }

    /// <summary>Port of <c>btnAddTime_Click</c> (FormBuildTracks.cs L1570-1575). Appends " hh:mm:ss" (InvariantCulture) to the name being entered.</summary>
    private void btnAddTime_Click(object sender, RoutedEventArgs e)
    {
        textBox1.Text += DateTime.Now.ToString(" hh:mm:ss", CultureInfo.InvariantCulture);
        curve.desName = textBox1.Text;
        activateMainView();
    }

    /// <summary>Port of <c>btnAddTimeEdit_Click</c> (FormBuildTracks.cs L1577-1581).</summary>
    private void btnAddTimeEdit_Click(object sender, RoutedEventArgs e)
    {
        textBox2.Text += DateTime.Now.ToString(" hh:mm:ss", CultureInfo.InvariantCulture);
        activateMainView();
    }

    /// <summary>Port of <c>btnSaveEditName_Click</c> (FormBuildTracks.cs L1583-1599). Commits the edited name (uses the field <c>idx</c>).</summary>
    private void btnSaveEditName_Click(object sender, RoutedEventArgs e)
    {
        if (textBox2.Text.Trim() == "") textBox2.Text = "No Name " + DateTime.Now.ToString("hh:mm:ss", CultureInfo.InvariantCulture);

        panelEditName.IsVisible = false;
        panelMain.IsVisible = true;

        curve.desList?.Clear();

        trk.gArr[idx].name = textBox2.Text.Trim();

        SetFormSize(650, 480);

        UpdateTable();
        flp.Focus();
        activateMainView();
    }

    /// <summary>
    /// Port of <c>SmoothAB</c> (FormBuildTracks.cs L1601-1644) — VERBATIM center-weighted smoothing of
    /// <c>curve.desList</c>: the first and last <c>smPts/2</c> points are copied unchanged; each interior
    /// point is replaced by the mean of its <c>smPts</c>-wide neighbourhood (heading retained), then the
    /// list is rebuilt.
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
