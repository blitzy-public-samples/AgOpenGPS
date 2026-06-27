// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;                 // PixelPoint
using Avalonia.Controls;        // Window, Button, TextBlock, WindowClosingEventArgs
using Avalonia.Input;           // PointerPressedEventArgs
using Avalonia.Interactivity;   // RoutedEventArgs
using Avalonia.Threading;       // DispatcherTimer
using AgOpenGPS.Properties;     // Settings, ToolSettings

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for <c>FormNudgeView</c> — a 1:1 behavioural and visual parity reimplementation
/// of the WinForms <c>FormNudge</c> (<c>Forms/Guidance/FormNudge.cs</c> +
/// <c>FormNudge.designer.cs</c>). It is a small, borderless, fixed-size, NON-MODAL floating "nudge
/// track" tool palette that shifts the active guidance track left/right by the snap distance (or half
/// the tool width), snaps it to the vehicle pivot, or zeroes the accumulated nudge. The current offset
/// is shown live and refreshed on a 3-second timer. The palette floats over the main window and
/// persists its own location.
/// </summary>
/// <remarks>
/// <para>
/// <b>Imperative, not MVVM.</b> The paired markup (<c>FormNudgeView.axaml</c>) declares only
/// <c>x:Name</c>'d controls — no <c>DataContext</c>, no <c>x:DataType</c>, no <c>{Binding}</c>, and no
/// <c>Click=</c> attributes. This code-behind addresses those controls directly and wires every handler
/// by name, exactly as the WinForms designer + handlers did, so the behaviour is reproduced verbatim.
/// </para>
/// <para>
/// <b>[XPLAT] Dependency injection (AAP §0.3.2).</b> The WinForms form reached the main
/// <c>FormGPS</c> god-object through a <c>private readonly FormGPS mf;</c> back-reference for the
/// track-methods object, the tool, the metric flag, the unit-conversion factors, the units string, the
/// save-tracks call, and the re-activate-main-window call. That back-reference is removed: every one of
/// those collaborators is injected through the constructor (concrete domain objects plus
/// <see cref="Action"/> callbacks), and each substitution is tagged with an inline <c>// [XPLAT]</c>
/// comment. The injected track type is <see cref="AgOpenGPS.CTrack"/> (the real type of the former
/// <c>mf.trk</c>; the migration note's <c>CTrackMethods</c> is a static extension class with none of the
/// members used here — see <c>Classes/CTrack.cs</c>/<c>Classes/CTool.cs</c>), matching the sibling
/// <c>FormFieldISOXMLView</c> which likewise injects <see cref="AgOpenGPS.CTrack"/>.
/// </para>
/// <para>
/// <b>Non-modal.</b> The owner shows this palette non-modally (<c>Show(owner)</c>, not
/// <c>ShowDialog</c>): it runs a live 3-second timer and re-activates the main window after each action.
/// The class supports being shown either way; it does not force modality.
/// </para>
/// <para>
/// <b>[XPLAT] No Windows-only APIs.</b> No <c>System.Windows.Forms</c>, no <c>OpenTK.GLControl</c>, no
/// <c>Microsoft.Win32</c>, and no P/Invoke / <c>WndProc</c> / <c>CreateParams</c>. The borderless
/// caption-drag is reimplemented with <see cref="Window.BeginMoveDrag"/>, and all numeric formatting is
/// pinned to <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5).
/// </para>
/// </remarks>
public partial class FormNudgeView : Window
{
    // ── Injected collaborators (replace the WinForms FormGPS god-object back-reference) ──────────────
    private readonly AgOpenGPS.CTrack trk;       // [XPLAT] was mf.trk
    private readonly AgOpenGPS.CTool tool;       // [XPLAT] was mf.tool
    private readonly bool isMetric;              // [XPLAT] was mf.isMetric
    private readonly double m2InchOrCm;          // [XPLAT] was mf.m2InchOrCm
    private readonly double inchOrCm2m;          // [XPLAT] was mf.inchOrCm2m
    private readonly double cm2CmOrIn;           // [XPLAT] was mf.cm2CmOrIn
    private readonly string unitsInCm;           // [XPLAT] was mf.unitsInCm
    private readonly Action saveTracks;          // [XPLAT] was mf.FileSaveTracks()
    private readonly Action activateMainView;    // [XPLAT] was mf.Activate()

    // Snap adjustment in METRES — preserved exactly as the source field (FormNudge.cs line 13).
    private double snapAdj = 0;

    // [XPLAT] The numeric snap value currently displayed on nudSnapDistance, in the user's units
    // (replaces the WinForms NudlessNumericUpDown.Value). Seeded in the Opened handler and updated by
    // the keypad. snapAdj (metres) and the persisted setAS_snapDistance (cm) are derived from it.
    private double currentDisplayValue;

    // [XPLAT] 3-second offset-readout refresh timer. WinForms System.Windows.Forms.Timer "timer1"
    // (Interval = 3000, designer line 232) -> Avalonia DispatcherTimer. Started in the Opened handler,
    // stopped in OnClosed (Avalonia timers are not disposed with the window).
    private DispatcherTimer timer1;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / design-time
    /// preview (its absence raises AVLN3001, which the Release <c>TreatWarningsAsErrors</c> build treats
    /// as an error). Production code constructs the palette through
    /// <see cref="FormNudgeView(AgOpenGPS.CTrack, AgOpenGPS.CTool, bool, double, double, double, string, Action, Action)"/>.
    /// </summary>
    public FormNudgeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Production constructor. Replaces the WinForms <c>FormNudge(Form callingForm)</c> ctor and
    /// all <c>mf.*</c> usage by injecting the collaborators directly. Mirrors the source ctor
    /// (FormNudge.cs lines 14-24): after <c>InitializeComponent</c> it seeds the offset label
    /// (<see cref="UpdateMoveLabel"/>) and blanks the title.
    /// </summary>
    /// <param name="trk">The active track model (was <c>mf.trk</c>).</param>
    /// <param name="tool">The tool model used for the half-tool-width nudge (was <c>mf.tool</c>).</param>
    /// <param name="isMetric">Metric vs. imperial display flag (was <c>mf.isMetric</c>).</param>
    /// <param name="m2InchOrCm">Metres → inches/centimetres display factor (was <c>mf.m2InchOrCm</c>).</param>
    /// <param name="inchOrCm2m">Inches/centimetres → metres factor (was <c>mf.inchOrCm2m</c>).</param>
    /// <param name="cm2CmOrIn">Centimetres → centimetres/inches display factor (was <c>mf.cm2CmOrIn</c>).</param>
    /// <param name="unitsInCm">Units suffix appended to the offset readout (was <c>mf.unitsInCm</c>).</param>
    /// <param name="saveTracks">Persists the entire track list on close (was <c>mf.FileSaveTracks()</c>).</param>
    /// <param name="activateMainView">Re-activates the main window after each action (was <c>mf.Activate()</c>).</param>
    public FormNudgeView(
        AgOpenGPS.CTrack trk,        // [XPLAT] was mf.trk
        AgOpenGPS.CTool tool,        // [XPLAT] was mf.tool
        bool isMetric,              // [XPLAT] was mf.isMetric
        double m2InchOrCm,          // [XPLAT] was mf.m2InchOrCm
        double inchOrCm2m,          // [XPLAT] was mf.inchOrCm2m
        double cm2CmOrIn,           // [XPLAT] was mf.cm2CmOrIn
        string unitsInCm,           // [XPLAT] was mf.unitsInCm
        Action saveTracks,          // [XPLAT] was mf.FileSaveTracks()
        Action activateMainView)    // [XPLAT] was mf.Activate()
        : this()
    {
        this.trk = trk;
        this.tool = tool;
        this.isMetric = isMetric;
        this.m2InchOrCm = m2InchOrCm;
        this.inchOrCm2m = inchOrCm2m;
        this.cm2CmOrIn = cm2CmOrIn;
        this.unitsInCm = unitsInCm;
        this.saveTracks = saveTracks;
        this.activateMainView = activateMainView;

        // [XPLAT] timer1 (designer Interval = 3000 ms). Created here; armed in Opened, stopped in OnClosed.
        timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
        timer1.Tick += Timer1_Tick;

        // [XPLAT] The XAML declares no Click= attributes; reproduce every WinForms designer-wired handler
        // by x:Name (FormNudge.designer.cs Click subscriptions).
        btnZeroMove.Click += btnZeroMove_Click;
        btnAdjRight.Click += btnAdjRight_Click;
        btnAdjLeft.Click += btnAdjLeft_Click;
        btnSnapToPivot.Click += btnSnapToPivot_Click;
        btnHalfToolRight.Click += btnHalfToolRight_Click;
        btnHalfToolLeft.Click += btnHalfToolLeft_Click;
        nudSnapDistance.Click += nudSnapDistance_Click;
        bthOK.Click += bntOk_Click;

        // [XPLAT] WinForms Load event -> Avalonia Opened (FormNudge.designer.cs line 276).
        this.Opened += FormNudgeView_Opened;

        // [XPLAT] Borderless caption-drag (replaces the Win32 WndProc WM_NCHITTEST -> HTCAPTION hack).
        this.PointerPressed += FormNudgeView_PointerPressed;

        // [XPLAT] WinForms MouseEnter -> Avalonia PointerEntered (FormNudge.designer.cs line 277).
        // Refreshes the offset readout when the pointer enters the palette (source FormEditTrack_MouseEnter).
        this.PointerEntered += FormNudgeView_PointerEntered;

        // Source ctor (FormNudge.cs lines 21-23): seed the offset label and blank the title.
        UpdateMoveLabel();
        Title = "";   // [XPLAT] WinForms this.Text = "" -> Window.Title.
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormEditTrack_Load</c> (source lines 26-49), wired to <see cref="Window.Opened"/>.
    /// Seeds the snap-distance display from <see cref="ToolSettings"/>, derives <see cref="snapAdj"/>,
    /// restores the saved window location, refreshes the offset label, clamps the window on-screen, and
    /// arms the 3-second refresh timer.
    /// </summary>
    private void FormNudgeView_Opened(object sender, EventArgs e)
    {
        if (isMetric)
        {
            // [XPLAT] metric: 0 decimal places, value = (int)setAS_snapDistance (source lines 30-31).
            currentDisplayValue = (int)ToolSettings.Default.setAS_snapDistance;
        }
        else
        {
            // [XPLAT] imperial: 1 decimal place, value = round(setAS_snapDistance * cm2CmOrIn, 1)
            // (source lines 35-36).
            currentDisplayValue = Math.Round(ToolSettings.Default.setAS_snapDistance * cm2CmOrIn, 1);
        }

        // [XPLAT] InvariantCulture display of the seeded value on the keypad button.
        nudSnapDistance.Content = FormatSnap(currentDisplayValue);

        // Source line 39: snapAdj = setAS_snapDistance * 0.01 (cm -> m).
        snapAdj = ToolSettings.Default.setAS_snapDistance * 0.01;

        // [XPLAT] Source line 41: Location = setWindow_formNudgeLocation. Settings persists a
        // System.Drawing.Point; convert to Avalonia PixelPoint (fully qualified to avoid clashing with
        // Avalonia.Point, which `using Avalonia;` brings into scope).
        System.Drawing.Point loc = Settings.Default.setWindow_formNudgeLocation;
        Position = new PixelPoint(loc.X, loc.Y);

        UpdateMoveLabel();   // source line 42

        // [XPLAT] Source lines 44-48: ScreenHelper.IsOnScreen(Bounds) -> Avalonia Screens clamp.
        ClampToScreen();

        // [XPLAT] designer timer1.Enabled = true (line 231) -> arm the refresh timer.
        timer1?.Start();
    }

    /// <summary>
    /// [XPLAT] InvariantCulture formatting of the snap-distance value for the keypad button. Metric uses
    /// 0 decimal places (WinForms <c>NudlessNumericUpDown.DecimalPlaces = 0</c>, source line 30);
    /// imperial uses 1 decimal place (<c>DecimalPlaces = 1</c>, source line 35).
    /// </summary>
    /// <param name="value">The snap value in the user's units.</param>
    /// <returns>The culture-invariant display string.</returns>
    private string FormatSnap(double value)
        => isMetric
            ? ((int)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// [XPLAT] Cross-platform replacement for the WinForms
    /// <c>if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }</c> (source lines 44-48): if the
    /// restored top-left lands on no connected screen, move the window to the origin. Best-effort — does
    /// nothing when screen information is unavailable (e.g. a headless host).
    /// </summary>
    private void ClampToScreen()
    {
        var screens = Screens;
        if (screens == null)
        {
            return;
        }

        PixelPoint pos = Position;
        bool onScreen = false;
        foreach (var sc in screens.All)
        {
            if (sc.Bounds.Contains(pos))
            {
                onScreen = true;
                break;
            }
        }

        if (!onScreen)
        {
            Position = new PixelPoint(0, 0);
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>UpdateMoveLabel</c> (source lines 63-74). Refreshes the live offset readout
    /// from <c>trk.gArr[trk.idx].nudgeDistance</c>. Every branch (== 0, &lt; 0, &gt; 0) and the appended
    /// <see cref="unitsInCm"/> are reproduced exactly; the <c>trk.idx &gt; -1</c> guard is preserved.
    /// </summary>
    private void UpdateMoveLabel()
    {
        if (trk.idx > -1)
        {
            double nd = trk.gArr[trk.idx].nudgeDistance;
            if (nd == 0)
                lblOffset.Text = ((int)(nd * m2InchOrCm * -1)).ToString(CultureInfo.InvariantCulture) + unitsInCm;
            else if (nd < 0)
                lblOffset.Text = "< " + ((int)(nd * m2InchOrCm * -1)).ToString(CultureInfo.InvariantCulture) + unitsInCm;
            else
                lblOffset.Text = ((int)(nd * m2InchOrCm)).ToString(CultureInfo.InvariantCulture) + " >" + unitsInCm;
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>timer1_Tick</c> (source lines 118-127), driving the 3-second offset refresh.
    /// Ported EXACTLY for parity: unlike <see cref="UpdateMoveLabel"/>, the timer does NOT append
    /// <see cref="unitsInCm"/> and does NOT special-case <c>nd == 0</c>, and it additionally guards
    /// <c>trk.gArr.Count &gt; 0</c>.
    /// </summary>
    private void Timer1_Tick(object sender, EventArgs e)
    {
        if (trk.idx > -1 && trk.gArr.Count > 0)
        {
            double nd = trk.gArr[trk.idx].nudgeDistance;
            if (nd < 0)
                lblOffset.Text = "< " + ((int)(nd * m2InchOrCm * -1)).ToString(CultureInfo.InvariantCulture);
            else
                lblOffset.Text = ((int)(nd * m2InchOrCm)).ToString(CultureInfo.InvariantCulture) + " >";
        }
    }

    // [XPLAT] btnZeroMove_Click (source lines 76-81): zero the accumulated nudge.
    private void btnZeroMove_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeDistanceReset();
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] btnAdjRight_Click (source lines 92-97): nudge the track right by the snap distance.
    private void btnAdjRight_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeTrack(snapAdj);
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] btnAdjLeft_Click (source lines 99-104): nudge the track left by the snap distance.
    private void btnAdjLeft_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeTrack(-snapAdj);
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] btnSnapToPivot_Click (source lines 106-111): snap the track to the vehicle pivot.
    private void btnSnapToPivot_Click(object sender, RoutedEventArgs e)
    {
        trk.SnapToPivot();
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] btnHalfToolRight_Click (source lines 129-134): nudge right by half the working tool width.
    private void btnHalfToolRight_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeTrack((tool.width - tool.overlap) * 0.5);
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] btnHalfToolLeft_Click (source lines 136-141): nudge left by half the working tool width.
    private void btnHalfToolLeft_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeTrack((tool.width - tool.overlap) * -0.5);
        UpdateMoveLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>nudSnapDistance_Click</c> (source lines 83-90). The WinForms
    /// <c>NudlessNumericUpDown.ShowKeypad(this)</c> in-place keypad is replaced by the cross-platform
    /// <see cref="FormNumeric"/> modal dialog (max 1000, per the designer <c>Maximum</c>). On accept the
    /// displayed value, <see cref="snapAdj"/> (metres) and the persisted
    /// <c>ToolSettings.setAS_snapDistance</c> (cm) are recomputed exactly as the source; the main window
    /// is re-activated afterwards on every path.
    /// </summary>
    private async void nudSnapDistance_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] NudlessNumericUpDown.ShowKeypad(this) -> FormNumeric dialog (max 1000 per designer Maximum).
        var f = new FormNumeric(0, 1000, currentDisplayValue);
        if (await f.ShowDialog<bool>(this))
        {
            currentDisplayValue = f.ReturnValue;
            nudSnapDistance.Content = FormatSnap(currentDisplayValue);   // [XPLAT] 0/1 dp by isMetric, InvariantCulture
            snapAdj = currentDisplayValue * inchOrCm2m;                  // source line 86
            ToolSettings.Default.setAS_snapDistance = snapAdj * 100;     // source line 87
            ToolSettings.Default.Save();                                 // source line 88
        }
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    // [XPLAT] bntOk_Click (source lines 113-116): just close (non-modal palette; no result needed).
    private void bntOk_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// [XPLAT] Borderless-window caption drag — pure-Avalonia replacement for the Win32
    /// <c>WndProc</c> <c>WM_NCHITTEST</c> → <c>HTCAPTION</c> hack (source lines 155-207), with NO
    /// P/Invoke. The seven glyph buttons and OK handle their own <c>PointerPressed</c> first (an Avalonia
    /// <c>Button</c> marks the event handled), so this bubble-phase handler only fires for a press on the
    /// empty client area — faithfully reproducing the original "drag only on empty client area (no child
    /// control under the cursor)" behaviour. The double-click / <c>SC_MAXIMIZE</c> blocking (source lines
    /// 168-178) is satisfied structurally by the fixed-size borderless window (<c>CanResize="False"</c>,
    /// Min == Max in the XAML), and the <c>CS_DROPSHADOW</c> drop shadow (source lines 143-153) is
    /// cosmetic and omitted.
    /// </summary>
    private void FormNudgeView_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormEditTrack_MouseEnter</c> (source lines 50-53), wired to
    /// <see cref="InputElement.PointerEntered"/> (the WinForms <c>MouseEnter</c> equivalent,
    /// FormNudge.designer.cs line 277). Refreshes the offset readout when the pointer enters the palette.
    /// </summary>
    private void FormNudgeView_PointerEntered(object sender, PointerEventArgs e)
    {
        UpdateMoveLabel();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormEditTrack_FormClosing</c> (source lines 54-61): persist the window
    /// location and settings, then save the entire track list. Uses the Avalonia
    /// <c>OnClosing(WindowClosingEventArgs)</c> override (the WinForms <c>FormClosing</c> equivalent).
    /// </summary>
    /// <param name="e">The closing event data.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // [XPLAT] Avalonia PixelPoint -> System.Drawing.Point in the settings store (source line 56).
        PixelPoint pos = Position;
        Settings.Default.setWindow_formNudgeLocation = new System.Drawing.Point(pos.X, pos.Y);
        Settings.Default.Save();

        // Source line 60: save the entire track list.
        saveTracks();   // [XPLAT] was mf.FileSaveTracks()

        base.OnClosing(e);
    }

    /// <summary>
    /// [XPLAT] Stop the 3-second refresh timer when the window closes. Avalonia <see cref="DispatcherTimer"/>
    /// instances are not disposed with the window, so the timer is stopped (and its handler detached)
    /// explicitly here.
    /// </summary>
    /// <param name="e">The closed event data.</param>
    protected override void OnClosed(EventArgs e)
    {
        if (timer1 != null)
        {
            timer1.Stop();
            timer1.Tick -= Timer1_Tick;
        }

        base.OnClosed(e);
    }
}
