// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;                 // PixelPoint
using Avalonia.Controls;        // Window, WindowClosingEventArgs
using Avalonia.Interactivity;   // RoutedEventArgs
using AgOpenGPS.Core.Translations;  // gStr
using AgOpenGPS.Properties;     // ToolSettings

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for <c>FormRefNudgeView</c> — a 1:1 behavioural and visual parity
/// reimplementation of the WinForms <c>FormRefNudge</c> (<c>Forms/Guidance/FormRefNudge.cs</c> +
/// <c>FormRefNudge.designer.cs</c>). It is a small, fixed-size, NON-MODAL "Nudge Reference Track"
/// tool palette that shifts the active <b>reference</b> track left/right by the snap distance (or by
/// half the working tool width), shows the running accumulated offset, and offers a commit (Exit →
/// save) or a rollback (Cancel → restore the track list from a snapshot taken when the palette opened).
/// </summary>
/// <remarks>
/// <para>
/// <b>Imperative, not MVVM.</b> The paired markup (<c>FormRefNudgeView.axaml</c>) declares only
/// <c>x:Name</c>'d controls — no <c>DataContext</c>, no <c>x:DataType</c>, no <c>{Binding}</c>, and no
/// <c>Click=</c> attributes. This code-behind addresses those controls directly and wires every handler
/// by name, exactly as the WinForms designer + handlers did, so the behaviour is reproduced verbatim.
/// </para>
/// <para>
/// <b>[XPLAT] Dependency injection (AAP §0.3.2).</b> The WinForms form reached the main
/// <c>FormGPS</c> god-object through a <c>private readonly FormGPS mf;</c> back-reference for the track
/// model, the tool, the AB line / curve guidance peers, the metric flag, the unit-conversion factors,
/// the units string, the save-tracks call, the re-activate-main-window call, and the "show the right
/// panel again on close" side-effect. That back-reference is removed: every collaborator is injected
/// through the constructor (concrete domain objects plus <see cref="Action"/> callbacks), and each
/// substitution is tagged with an inline <c>// [XPLAT]</c> comment. The injected track type is
/// <see cref="AgOpenGPS.CTrack"/> — the real type of the former <c>mf.trk</c> that exposes
/// <c>gArr</c> (the <see cref="AgOpenGPS.CTrk"/> list) and <c>NudgeRefTrack(double)</c>; the migration
/// note's <c>CTrackMethods</c> is a static extension class with none of the members used here (see
/// <c>Classes/CTrack.cs</c>). This matches the sibling <c>FormNudgeView</c>, which likewise injects
/// <see cref="AgOpenGPS.CTrack"/>.
/// </para>
/// <para>
/// <b>Non-modal.</b> The owner shows this palette non-modally (<c>Show(owner)</c>, not
/// <c>ShowDialog</c>): the commit (<see cref="btnExit_Click"/> → <see cref="saveTracks"/>) and the
/// rollback (<see cref="btnCancelMain_Click"/> → restore <see cref="gTemp"/>) are performed by direct
/// domain mutation, NOT via a dialog result, so no <c>ShowDialog&lt;T&gt;</c> return value is needed.
/// The only modal interaction is the snap-distance numeric keypad (<see cref="FormNumeric"/>), opened
/// from <see cref="nudSnapDistance_Click"/>.
/// </para>
/// <para>
/// <b>[XPLAT] No Windows-only APIs.</b> No <c>System.Windows.Forms</c>, no <c>OpenTK.GLControl</c>, no
/// <c>Microsoft.Win32</c>, and no P/Invoke. All numeric formatting is pinned to
/// <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5 — highest data-integrity rule) so a locale
/// whose decimal separator is a comma cannot corrupt the persisted snap distance.
/// </para>
/// </remarks>
public partial class FormRefNudgeView : Window
{
    // ── Injected collaborators (replace the WinForms FormGPS god-object back-reference) ──────────────
    private readonly AgOpenGPS.CTrack trk;        // [XPLAT] was mf.trk
    private readonly AgOpenGPS.CTool tool;        // [XPLAT] was mf.tool
    private readonly AgOpenGPS.CABLine ABLine;    // [XPLAT] was mf.ABLine
    private readonly AgOpenGPS.CABCurve curve;    // [XPLAT] was mf.curve
    private readonly bool isMetric;               // [XPLAT] was mf.isMetric
    private readonly double m2InchOrCm;           // [XPLAT] was mf.m2InchOrCm
    private readonly double inchOrCm2m;           // [XPLAT] was mf.inchOrCm2m
    private readonly double cm2CmOrIn;            // [XPLAT] was mf.cm2CmOrIn
    private readonly string unitsInCm;            // [XPLAT] was mf.unitsInCm
    private readonly Action saveTracks;           // [XPLAT] was mf.FileSaveTracks()
    private readonly Action activateMainView;     // [XPLAT] was mf.Activate() / mf.Focus()
    private readonly Action onClosed;             // [XPLAT] was mf.panelRight.Visible = true

    // ── Exact source fields (FormRefNudge.cs lines 14, 17) ───────────────────────────────────────────
    // Snapshot of the track list taken when the palette opens; btnCancelMain restores it (rollback).
    public System.Collections.Generic.List<AgOpenGPS.CTrk> gTemp = new();   // [XPLAT] was public List<CTrk>
    private double snapAdj = 0, distanceMoved = 0;

    // [XPLAT] The numeric snap value currently displayed on nudSnapDistance, in the user's units
    // (replaces the WinForms NudlessNumericUpDown.Value). Seeded in the Opened handler and updated by the
    // keypad; snapAdj (metres) and the persisted setAS_snapDistanceRef (cm) are derived from it.
    private double currentDisplayValue;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / design-time
    /// preview (its absence raises AVLN3001, which the Release <c>TreatWarningsAsErrors</c> build treats
    /// as an error). Production code constructs the palette through the dependency-injection overload.
    /// </summary>
    public FormRefNudgeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Production constructor. Replaces the WinForms <c>FormRefNudge(Form callingForm)</c> ctor
    /// (source lines 18-26) and all <c>mf.*</c> usage by injecting the collaborators directly. After the
    /// chained <c>InitializeComponent</c> it wires the designer-declared handlers by name, subscribes the
    /// <see cref="Window.Opened"/> lifecycle event (the WinForms <c>Load</c> equivalent), and sets the
    /// caption from the translated <c>gStr.gsNudgeRefTrack</c> (source line 25).
    /// </summary>
    /// <param name="trk">The active track model (was <c>mf.trk</c>); supplies <c>gArr</c> and <c>NudgeRefTrack</c>.</param>
    /// <param name="tool">The tool model used for the half-tool-width nudge (was <c>mf.tool</c>).</param>
    /// <param name="ABLine">The AB-line guidance peer invalidated on rollback (was <c>mf.ABLine</c>).</param>
    /// <param name="curve">The curve guidance peer invalidated on rollback (was <c>mf.curve</c>).</param>
    /// <param name="isMetric">Metric vs. imperial display flag (was <c>mf.isMetric</c>).</param>
    /// <param name="m2InchOrCm">Metres → inches/centimetres display factor (was <c>mf.m2InchOrCm</c>).</param>
    /// <param name="inchOrCm2m">Inches/centimetres → metres factor (was <c>mf.inchOrCm2m</c>).</param>
    /// <param name="cm2CmOrIn">Centimetres → centimetres/inches display factor (was <c>mf.cm2CmOrIn</c>).</param>
    /// <param name="unitsInCm">Units suffix appended to the offset readout (was <c>mf.unitsInCm</c>).</param>
    /// <param name="saveTracks">Persists the entire track list on Exit (was <c>mf.FileSaveTracks()</c>).</param>
    /// <param name="activateMainView">Re-activates the main window after each action (was <c>mf.Activate()</c> / <c>mf.Focus()</c>).</param>
    /// <param name="onClosed">Restores the main right-hand panel on any close path (was <c>mf.panelRight.Visible = true</c>).</param>
    public FormRefNudgeView(
        AgOpenGPS.CTrack trk,        // [XPLAT] was mf.trk
        AgOpenGPS.CTool tool,        // [XPLAT] was mf.tool
        AgOpenGPS.CABLine ABLine,    // [XPLAT] was mf.ABLine
        AgOpenGPS.CABCurve curve,    // [XPLAT] was mf.curve
        bool isMetric,              // [XPLAT] was mf.isMetric
        double m2InchOrCm,          // [XPLAT] was mf.m2InchOrCm
        double inchOrCm2m,          // [XPLAT] was mf.inchOrCm2m
        double cm2CmOrIn,           // [XPLAT] was mf.cm2CmOrIn
        string unitsInCm,           // [XPLAT] was mf.unitsInCm
        Action saveTracks,          // [XPLAT] was mf.FileSaveTracks()
        Action activateMainView,    // [XPLAT] was mf.Activate() / mf.Focus()
        Action onClosed)            // [XPLAT] was mf.panelRight.Visible = true
        : this()
    {
        this.trk = trk;
        this.tool = tool;
        this.ABLine = ABLine;
        this.curve = curve;
        this.isMetric = isMetric;
        this.m2InchOrCm = m2InchOrCm;
        this.inchOrCm2m = inchOrCm2m;
        this.cm2CmOrIn = cm2CmOrIn;
        this.unitsInCm = unitsInCm;
        this.saveTracks = saveTracks;
        this.activateMainView = activateMainView;
        this.onClosed = onClosed;

        // [XPLAT] The XAML declares no Click= attributes; reproduce every WinForms designer-wired handler
        // by x:Name (FormRefNudge.designer.cs Click subscriptions).
        btnHalfToolLeft.Click += btnHalfToolLeft_Click;
        btnHalfToolRight.Click += btnHalfToolRight_Click;
        btnAdjLeft.Click += btnAdjLeft_Click;
        btnAdjRight.Click += btnAdjRight_Click;
        nudSnapDistance.Click += nudSnapDistance_Click;
        btnExit.Click += btnExit_Click;
        btnCancelMain.Click += btnCancelMain_Click;

        // [XPLAT] WinForms Load event -> Avalonia Opened (FormRefNudge.designer.cs line 240).
        this.Opened += FormRefNudgeView_Opened;

        // Source ctor line 25: this.Text = gStr.gsNudgeRefTrack -> Window.Title.
        this.Title = gStr.gsNudgeRefTrack;
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormEditTrack_Load</c> (source lines 28-58), wired to
    /// <see cref="Window.Opened"/>. Seeds the snap-distance display from <see cref="ToolSettings"/>
    /// (0 dp metric / 1 dp imperial), derives <see cref="snapAdj"/>, takes the rollback snapshot of the
    /// track list into <see cref="gTemp"/>, seeds the offset readout, and clamps the window on-screen.
    /// </summary>
    /// <param name="sender">The window raising the event (unused).</param>
    /// <param name="e">The event data (unused).</param>
    private void FormRefNudgeView_Opened(object sender, EventArgs e)
    {
        if (isMetric)
        {
            // [XPLAT] metric: 0 decimal places, value = (int)setAS_snapDistanceRef (source lines 32-33).
            currentDisplayValue = (int)ToolSettings.Default.setAS_snapDistanceRef;
        }
        else
        {
            // [XPLAT] imperial: 1 decimal place, value = round(setAS_snapDistanceRef * cm2CmOrIn, 1)
            // (source lines 37-38).
            currentDisplayValue = Math.Round(ToolSettings.Default.setAS_snapDistanceRef * cm2CmOrIn, 1);
        }

        // [XPLAT] InvariantCulture display of the seeded value on the keypad button.
        nudSnapDistance.Content = FormatSnap(currentDisplayValue);

        // Source line 41: snapAdj = setAS_snapDistanceRef * 0.01 (cm -> m).
        snapAdj = ToolSettings.Default.setAS_snapDistanceRef * 0.01;

        // Source lines 43-46: clone the current track list so btnCancelMain can roll it back.
        // CTrk's copy-constructor performs the deep-ish copy (Classes/CTrk.cs line 47).
        gTemp.Clear();
        foreach (var item in trk.gArr)
        {
            gTemp.Add(new AgOpenGPS.CTrk(item));
        }

        // Source line 48: offset readout. NOTE the explicit " " space before the units — this differs
        // from FormNudge (which omits the space); preserved EXACTLY for parity.
        lblOffset.Text = ((int)(distanceMoved * m2InchOrCm)).ToString(CultureInfo.InvariantCulture) + " " + unitsInCm;

        // Source lines 53-57: ScreenHelper.IsOnScreen(Bounds) -> Avalonia Screens clamp.
        ClampToScreen();
    }

    /// <summary>
    /// [XPLAT] InvariantCulture formatting of the snap-distance value for the keypad button. Metric uses
    /// 0 decimal places (WinForms <c>NudlessNumericUpDown.DecimalPlaces = 0</c>, source line 32);
    /// imperial uses 1 decimal place (<c>DecimalPlaces = 1</c>, source line 37).
    /// </summary>
    /// <param name="value">The snap value in the user's units.</param>
    /// <returns>The culture-invariant display string.</returns>
    private string FormatSnap(double value)
        => isMetric
            ? ((int)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// [XPLAT] Cross-platform replacement for the WinForms
    /// <c>if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }</c> (source lines 53-57): if the
    /// window's top-left lands on no connected screen, move it to the origin. Because
    /// <c>WindowStartupLocation=CenterOwner</c> places the palette on-screen, this is typically a no-op;
    /// it is ported for parity. Best-effort — does nothing when screen information is unavailable
    /// (e.g. a headless host).
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
    /// [XPLAT] Port of <c>DistanceMovedLabel</c> (source lines 106-110). Refreshes the running offset
    /// readout from the accumulated <see cref="distanceMoved"/> and re-activates the main view (the
    /// source called <c>mf.Focus()</c> here). The explicit <c>" "</c> space before the units is
    /// preserved EXACTLY (parity — differs from FormNudge).
    /// </summary>
    private void DistanceMovedLabel()
    {
        lblOffset.Text = ((int)(distanceMoved * m2InchOrCm)).ToString(CultureInfo.InvariantCulture) + " " + unitsInCm;
        activateMainView();   // [XPLAT] was mf.Focus()
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnAdjRight_Click</c> (source lines 74-80): nudge the reference track right by
    /// the snap distance, accumulate the offset, refresh the readout, and re-activate the main view.
    /// </summary>
    /// <param name="sender">The button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnAdjRight_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeRefTrack(snapAdj);
        distanceMoved += snapAdj;
        DistanceMovedLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnAdjLeft_Click</c> (source lines 82-88): nudge the reference track left by
    /// the snap distance, accumulate the (negative) offset, refresh the readout, and re-activate the
    /// main view.
    /// </summary>
    /// <param name="sender">The button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnAdjLeft_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeRefTrack(-snapAdj);
        distanceMoved += -snapAdj;
        DistanceMovedLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnHalfToolRight_Click</c> (source lines 90-96): nudge the reference track
    /// right by half the working tool width (<c>(tool.width - tool.overlap) * 0.5</c>), accumulate the
    /// offset, refresh the readout, and re-activate the main view.
    /// </summary>
    /// <param name="sender">The button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnHalfToolRight_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeRefTrack((tool.width - tool.overlap) * 0.5);
        distanceMoved += (tool.width - tool.overlap) * 0.5;
        DistanceMovedLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnHalfToolLeft_Click</c> (source lines 98-104): nudge the reference track
    /// left by half the working tool width (<c>(tool.width - tool.overlap) * -0.5</c>), accumulate the
    /// (negative) offset, refresh the readout, and re-activate the main view.
    /// </summary>
    /// <param name="sender">The button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnHalfToolLeft_Click(object sender, RoutedEventArgs e)
    {
        trk.NudgeRefTrack((tool.width - tool.overlap) * -0.5);
        distanceMoved += (tool.width - tool.overlap) * -0.5;
        DistanceMovedLabel();
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>nudSnapDistance_Click</c> (source lines 65-72). The WinForms
    /// <c>NudlessNumericUpDown.ShowKeypad(this)</c> in-place keypad is replaced by the cross-platform
    /// <see cref="FormNumeric"/> modal dialog (max 1000, per the designer <c>Maximum</c>). On accept the
    /// displayed value, <see cref="snapAdj"/> (metres) and the persisted
    /// <c>ToolSettings.setAS_snapDistanceRef</c> (cm) are recomputed exactly as the source; the main
    /// view is re-activated afterwards on every path (was <c>mf.Activate()</c>).
    /// </summary>
    /// <param name="sender">The keypad button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private async void nudSnapDistance_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] NudlessNumericUpDown.ShowKeypad(this) -> FormNumeric dialog (max 1000 per designer Maximum).
        var f = new FormNumeric(0, 1000, currentDisplayValue);
        if (await f.ShowDialog<bool>(this))
        {
            currentDisplayValue = f.ReturnValue;
            nudSnapDistance.Content = FormatSnap(currentDisplayValue);    // [XPLAT] 0/1 dp by isMetric, InvariantCulture
            snapAdj = currentDisplayValue * inchOrCm2m;                   // source line 68
            ToolSettings.Default.setAS_snapDistanceRef = snapAdj * 100;   // source line 69
            ToolSettings.Default.Save();                                  // source line 70
        }
        activateMainView();   // [XPLAT] was mf.Activate()
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnExit_Click</c> (source lines 112-120): commit the edit by persisting the
    /// entire track list (was <c>mf.FileSaveTracks()</c>), then close. The right-hand panel is restored
    /// by <see cref="OnClosing"/> on every close path.
    /// </summary>
    /// <param name="sender">The OK button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        saveTracks();   // [XPLAT] was mf.FileSaveTracks()
        Close();
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnCancelMain_Click</c> (source lines 122-136): roll the edit back. The track
    /// list is cleared and rebuilt from the <see cref="gTemp"/> snapshot (deep copy via the
    /// <see cref="AgOpenGPS.CTrk"/> copy-constructor), the AB-line and curve guidance peers are
    /// invalidated so they rebuild from the restored tracks, then the palette closes WITHOUT saving.
    /// </summary>
    /// <param name="sender">The Cancel button raising the event (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnCancelMain_Click(object sender, RoutedEventArgs e)
    {
        trk.gArr.Clear();

        foreach (var item in gTemp)
        {
            trk.gArr.Add(new AgOpenGPS.CTrk(item));
        }

        ABLine.isABValid = false;   // [XPLAT] was mf.ABLine.isABValid = false
        curve.isCurveValid = false; // [XPLAT] was mf.curve.isCurveValid = false

        Close();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormEditTrack_FormClosing</c> (source lines 60-63): restore the main window's
    /// right-hand panel on ANY close path (Exit, Cancel, or the OS window chrome). Uses the Avalonia
    /// <see cref="Window.OnClosing(WindowClosingEventArgs)"/> override (the WinForms <c>FormClosing</c>
    /// equivalent). Null-conditional invoke guards the design-time parameterless-ctor instance, whose
    /// callback is never assigned (it is wired only by the production constructor).
    /// </summary>
    /// <param name="e">The closing event data.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        onClosed?.Invoke();   // [XPLAT] was mf.panelRight.Visible = true
        base.OnClosing(e);
    }
}
