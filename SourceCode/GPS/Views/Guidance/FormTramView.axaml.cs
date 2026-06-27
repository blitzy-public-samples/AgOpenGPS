// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;                       // PixelPoint
using Avalonia.Controls;              // Window, WindowClosingEventArgs
using Avalonia.Controls.Primitives;   // RangeBaseValueChangedEventArgs
using Avalonia.Interactivity;         // RoutedEventArgs
using Avalonia.Media.Imaging;         // Bitmap
using Avalonia.Platform;              // AssetLoader
using AgOpenGPS.Core.Translations;    // gStr
using AgOpenGPS.Properties;           // Settings

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for <c>FormTramView</c> — a 1:1 behavioural and visual parity reimplementation
/// of the WinForms <c>FormTram</c> (<c>Forms/Guidance/FormTram.cs</c> + <c>FormTram.Designer.cs</c>):
/// the "simple" tram-line configuration dialog. The operator sets the number of passes between trams
/// (<c>nudPasses</c>), the tram-line opacity / alpha (<c>tbarTramAlpha</c>), the generate mode —
/// All(0) / Lines(1) / Outer(2), cycled on <c>btnMode</c> whose glyph is swapped at runtime — and an
/// A/B (or curve) point swap (<c>btnSwapAB</c>); it also shows the live seed / tram / track widths.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behaviour frozen.</b> Tram geometry is a behaviour-frozen contract (AAP §0.2.2): the
/// <see cref="OnSwapAbClick"/> point maths and <see cref="MoveBuildTramLine"/> rebuild are ported
/// verbatim from the source, and every numeric format is pinned to
/// <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5) so a comma-decimal locale cannot drift the
/// displayed widths.
/// </para>
/// <para>
/// <b>Imperative, not MVVM.</b> The paired markup (<c>FormTramView.axaml</c>) declares only
/// <c>x:Name</c>'d controls — no <c>DataContext</c>, no <c>x:DataType</c>, no <c>{Binding}</c>, and no
/// <c>Click=</c> attributes (including the source's <c>lblAplha</c> typo, preserved). This code-behind
/// addresses those controls directly and wires every handler by name, exactly as the WinForms designer
/// + handlers did.
/// </para>
/// <para>
/// <b>[XPLAT] Dependency injection (AAP §0.3.2).</b> The WinForms form reached the main
/// <c>FormGPS</c> god-object through a <c>private readonly FormGPS mf;</c> back-reference. That
/// back-reference is removed: every collaborator is injected through the production constructor
/// (concrete domain objects plus <see cref="Action"/> callbacks), and each substitution is tagged with
/// an inline <c>// [XPLAT]</c> comment. The injected track type is <see cref="AgOpenGPS.CTrack"/> (the
/// real type of the former <c>mf.trk</c>; <c>CTrackMethods</c> is a static extension class with none of
/// the instance members used here), and the vehicle is the concrete <see cref="AgOpenGPS.CVehicle"/>.
/// </para>
/// <para>
/// <b>Non-modal.</b> The owner shows this dialog non-modally (<c>Show(owner)</c>, consistent with the
/// other guidance palettes). Commit vs. discard is decided internally by <see cref="isSaving"/> and the
/// <see cref="OnClosing"/> body, not by a dialog result, so closing always uses the parameterless
/// <see cref="Window.Close()"/>.
/// </para>
/// <para>
/// <b>[XPLAT] No Windows-only APIs.</b> No <c>System.Windows.Forms</c>, no <c>OpenTK.GLControl</c>, no
/// <c>Microsoft.Win32</c>, and no P/Invoke. Avalonia 11.3.x.
/// </para>
/// </remarks>
public partial class FormTramView : Window
{
    // ── Injected collaborators (replace the WinForms FormGPS god-object back-reference) ──────────────
    private readonly AgOpenGPS.CTram tram;                 // [XPLAT] was mf.tram
    private readonly AgOpenGPS.CTool tool;                 // [XPLAT] was mf.tool
    private readonly AgOpenGPS.CBoundary bnd;              // [XPLAT] was mf.bnd
    private readonly AgOpenGPS.CABCurve curve;             // [XPLAT] was mf.curve
    private readonly AgOpenGPS.CABLine ABLine;             // [XPLAT] was mf.ABLine
    private readonly AgOpenGPS.CTrack trk;                 // [XPLAT] was mf.trk (real type CTrack, not CTrackMethods)
    private readonly AgOpenGPS.CVehicle vehicle;           // [XPLAT] was mf.vehicle (exposes VehicleConfig.TrackWidth)
    private readonly double m2FtOrM;                       // [XPLAT] was mf.m2FtOrM
    private readonly string unitsFtM;                      // [XPLAT] was mf.unitsFtM
    private readonly Action closeTopMosts;                 // [XPLAT] was mf.CloseTopMosts()
    private readonly Action saveTram;                      // [XPLAT] was mf.FileSaveTram()
    private readonly Action panelUpdateRightAndBottom;     // [XPLAT] was mf.PanelUpdateRightAndBottom()
    private readonly Action fixTramModeButton;             // [XPLAT] was mf.FixTramModeButton()
    private readonly Action saveTracks;                    // [XPLAT] was mf.FileSaveTracks()

    // Source fields preserved EXACTLY (FormTram.cs lines 14-15).
    private bool isSaving;
    private static bool isCurve;   // [XPLAT] preserve `static` exactly as source (persists across instances)

    // [XPLAT] The passes value currently displayed on nudPasses (replaces NudlessNumericUpDown.Value,
    // which was a click-only display field). Seeded in Opened from Settings; stepped by the up/down
    // buttons and the on-screen keypad through SetPassesFromUser. Defaults to 1 (the source Minimum).
    private int passesValue = 1;

    // [XPLAT] Re-entrancy guard for the initial alpha seed. Avalonia's Slider raises ValueChanged on a
    // PROGRAMMATIC Value set (unlike the WinForms TrackBar, whose Scroll fired only on user input), so
    // the Opened seed is fenced to prevent the handler mutating tram.alpha during initialisation —
    // preserving the source behaviour where the Load assignment did not run the Scroll handler.
    private bool isLoading;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / design-time
    /// previewer (its absence raises AVLN3001, which the Release <c>TreatWarningsAsErrors</c> build
    /// treats as an error). Production code constructs the dialog through the dependency-injection
    /// constructor below.
    /// </summary>
    public FormTramView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Production constructor. Replaces the WinForms <c>FormTram(Form callingForm, bool Curve)</c>
    /// ctor and all <c>mf.*</c> usage by injecting the collaborators directly. Mirrors the source ctor
    /// (FormTram.cs lines 17-36): after <c>InitializeComponent</c> it localises the title and captions
    /// and seeds the live seed / tram width read-outs.
    /// </summary>
    /// <param name="tram">The tram-line model (was <c>mf.tram</c>).</param>
    /// <param name="tool">The tool model used for the seed-width read-out and half-width (was <c>mf.tool</c>).</param>
    /// <param name="bnd">The boundary model; an empty boundary forces Lines mode (was <c>mf.bnd</c>).</param>
    /// <param name="curve">The AB-curve model whose <c>BuildTram</c> rebuilds curve trams (was <c>mf.curve</c>).</param>
    /// <param name="ABLine">The AB-line model whose <c>BuildTram</c> rebuilds straight trams (was <c>mf.ABLine</c>).</param>
    /// <param name="trk">The active track collection used by the A/B swap maths (was <c>mf.trk</c>).</param>
    /// <param name="vehicle">The vehicle model exposing <c>VehicleConfig.TrackWidth</c> (was <c>mf.vehicle</c>).</param>
    /// <param name="m2FtOrM">Metres → feet/metres display factor (was <c>mf.m2FtOrM</c>).</param>
    /// <param name="unitsFtM">Units suffix appended to the width read-outs (was <c>mf.unitsFtM</c>).</param>
    /// <param name="closeTopMosts">Closes any top-most palettes before this dialog opens (was <c>mf.CloseTopMosts()</c>).</param>
    /// <param name="saveTram">Persists the tram file on close (was <c>mf.FileSaveTram()</c>).</param>
    /// <param name="panelUpdateRightAndBottom">Refreshes the main right/bottom panels on close (was <c>mf.PanelUpdateRightAndBottom()</c>).</param>
    /// <param name="fixTramModeButton">Re-syncs the main tram-mode button on close (was <c>mf.FixTramModeButton()</c>).</param>
    /// <param name="saveTracks">Persists the track list after an A/B swap (was <c>mf.FileSaveTracks()</c>).</param>
    /// <param name="Curve">True when the active reference is a curve rather than an AB line.</param>
    public FormTramView(
        AgOpenGPS.CTram tram,                       // [XPLAT] was mf.tram
        AgOpenGPS.CTool tool,                       // [XPLAT] was mf.tool
        AgOpenGPS.CBoundary bnd,                    // [XPLAT] was mf.bnd
        AgOpenGPS.CABCurve curve,                   // [XPLAT] was mf.curve
        AgOpenGPS.CABLine ABLine,                   // [XPLAT] was mf.ABLine
        AgOpenGPS.CTrack trk,                       // [XPLAT] was mf.trk
        AgOpenGPS.CVehicle vehicle,                 // [XPLAT] was mf.vehicle
        double m2FtOrM,                             // [XPLAT] was mf.m2FtOrM
        string unitsFtM,                            // [XPLAT] was mf.unitsFtM
        Action closeTopMosts,                       // [XPLAT] was mf.CloseTopMosts()
        Action saveTram,                            // [XPLAT] was mf.FileSaveTram()
        Action panelUpdateRightAndBottom,           // [XPLAT] was mf.PanelUpdateRightAndBottom()
        Action fixTramModeButton,                   // [XPLAT] was mf.FixTramModeButton()
        Action saveTracks,                          // [XPLAT] was mf.FileSaveTracks()
        bool Curve)
        : this()
    {
        this.tram = tram;
        this.tool = tool;
        this.bnd = bnd;
        this.curve = curve;
        this.ABLine = ABLine;
        this.trk = trk;
        this.vehicle = vehicle;
        this.m2FtOrM = m2FtOrM;
        this.unitsFtM = unitsFtM;
        this.closeTopMosts = closeTopMosts;
        this.saveTram = saveTram;
        this.panelUpdateRightAndBottom = panelUpdateRightAndBottom;
        this.fixTramModeButton = fixTramModeButton;
        this.saveTracks = saveTracks;

        // [XPLAT] The .axaml declares no Click= attributes; reproduce every WinForms designer-wired
        // handler by x:Name. The WinForms Load -> Avalonia Opened (the controls are realised by then).
        this.Opened += FormTramView_Opened;
        btnExit.Click += btnExit_Click;
        btnCancel.Click += btnCancel_Click;
        btnUpTrams.Click += btnUpTrams_Click;
        btnDnTrams.Click += btnDnTrams_Click;
        nudPasses.Click += nudPasses_Click;
        btnMode.Click += btnMode_Click;
        btnSwapAB.Click += btnSwapAB_Click;
        tbarTramAlpha.ValueChanged += tbarTramAlpha_ValueChanged;

        // Source ctor (FormTram.cs lines 23-31): localise the title + captions and seed the widths.
        this.Title = gStr.gsSimpleTramLines;
        labelPasses.Text = gStr.gsPasses;
        labelMode.Text = gStr.gsMode;
        labelAlpha.Text = gStr.gsAlpha;
        labelSeed.Text = gStr.gsWorkWidth;
        labelSprayWidth.Text = gStr.gsTramWidth;
        labelTrack.Text = gStr.gsTrack;

        // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was .ToString("N2").
        lblTramWidth.Text = (tram.tramWidth * m2FtOrM).ToString("N2", CultureInfo.InvariantCulture) + unitsFtM;
        lblSeedWidth.Text = (tool.width * m2FtOrM).ToString("N2", CultureInfo.InvariantCulture) + unitsFtM;

        // [XPLAT] Source line 33 `nudPasses.Controls[0].Enabled = false;` (disabling the inner spinner)
        // has no Avalonia equivalent — nudPasses is now a click-only display Button — so it is omitted.

        isCurve = Curve;
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormTram_Load</c> (source lines 38-104), wired to <see cref="Window.Opened"/>.
    /// Seeds the alpha slider and the passes read-out, fills the live track-width read-out, computes the
    /// tool half-width, determines the initial generate mode, sets the mode glyph, closes any top-most
    /// palettes, builds the tram preview when none exists, and clamps the window on-screen.
    /// </summary>
    private void FormTramView_Opened(object sender, EventArgs e)
    {
        // [XPLAT] Seed the alpha slider (10..100). Fenced by isLoading so the programmatic set does not
        // run the ValueChanged body (the WinForms Load assignment did not raise the Scroll handler).
        isLoading = true;
        tbarTramAlpha.Value = (int)(tram.alpha * 100);
        isLoading = false;
        lblAplha.Text = ((int)tbarTramAlpha.Value).ToString(CultureInfo.InvariantCulture) + "%";

        if (Settings.Default.setTram_passes < 1)
        {
            Settings.Default.setTram_passes = 1;
            Settings.Default.Save();
        }

        // [XPLAT] Seed the passes display WITHOUT a rebuild — the WinForms code wired ValueChanged AFTER
        // assigning nudPasses.Value, so the initial seed never ran nudPasses_ValueChanged. tram.passes is
        // therefore left untouched here (set only on user interaction via SetPassesFromUser).
        passesValue = Settings.Default.setTram_passes;
        nudPasses.Content = passesValue.ToString(CultureInfo.InvariantCulture);

        // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was .ToString("N2").
        lblTrack.Text = (vehicle.VehicleConfig.TrackWidth * m2FtOrM).ToString("N2", CultureInfo.InvariantCulture) + unitsFtM;

        tool.halfWidth = (tool.width - tool.overlap) / 2.0;

        // Determine the initial generate mode (source lines 56-66) — ported verbatim.
        //if off, turn it on because they obviously want a tram.
        tram.generateMode = 0;

        if (tram.tramList.Count > 0 && tram.tramBndOuterArr.Count > 0)
            tram.generateMode = 0;
        else if (tram.tramBndOuterArr.Count == 0)
            tram.generateMode = 1;
        else if (tram.tramList.Count == 0)
            tram.generateMode = 2;
        else tram.generateMode = 0;

        if (bnd.bndList.Count == 0) tram.generateMode = 1;

        // Set the mode glyph for the resolved generate mode (source switch, lines 68-84).
        UpdateModeImage();

        if (bnd.bndList.Count == 0) btnMode.IsEnabled = false;

        closeTopMosts();   // [XPLAT] was mf.CloseTopMosts()

        if (tram.tramList.Count > 0 || tram.tramBndOuterArr.Count > 0)
        {
            //don't rebuild as trams exist
        }
        else
        {
            MoveBuildTramLine(0);
        }

        // [XPLAT] Source lines 99-103: ScreenHelper.IsOnScreen(Bounds) -> { Top = 0; Left = 0; }. Done
        // via Avalonia Screens; if the window lands on no connected screen, move it to the origin.
        // Best-effort — does nothing when screen information is unavailable (e.g. a headless host).
        var screens = Screens;
        if (screens != null)
        {
            bool onScreen = false;
            foreach (var sc in screens.All)
            {
                if (sc.Bounds.Contains(Position))
                {
                    onScreen = true;
                    break;
                }
            }

            if (!onScreen) Position = new PixelPoint(0, 0);
        }
    }

    /// <summary>
    /// [XPLAT] Sets the <c>imgMode</c> glyph for the current <c>tram.generateMode</c>, reproducing the
    /// WinForms <c>switch</c> that assigned <c>btnMode.BackgroundImage</c> (source lines 68-84 and
    /// 253-269): 0 → TramAll, 1 → TramLines, 2 → TramOuter. The glyph is loaded from the same
    /// <c>btnImages</c> avares assets the .axaml seeds <c>imgMode</c> with. Reused by
    /// <see cref="FormTramView_Opened"/> and <see cref="btnMode_Click"/>.
    /// </summary>
    private void UpdateModeImage()
    {
        string asset;
        switch (tram.generateMode)
        {
            case 0:
                asset = "TramAll.png";
                break;

            case 1:
                asset = "TramLines.png";
                break;

            case 2:
                asset = "TramOuter.png";
                break;

            default:
                return;
        }

        imgMode.Source = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + asset)));
    }

    /// <summary>
    /// [XPLAT] Port of <c>MoveBuildTramLine</c> (source lines 134-150) — ported verbatim. Sets the
    /// display mode and rebuilds the tram preview from either the curve or the AB line, depending on the
    /// active reference. The commented <c>NudgeRefCurve</c>/<c>NudgeRefABLine</c> calls are preserved as
    /// comments exactly as in the source.
    /// </summary>
    /// <param name="Dist">The nudge distance — preserved from the source signature for parity (only the
    /// commented nudge paths consume it).</param>
    private void MoveBuildTramLine(double Dist)
    {
        tram.displayMode = 1;

        if (isCurve)
        {
            //if (Dist != 0)
            //    trk.NudgeRefCurve(Dist);
            curve.BuildTram();
        }
        else
        {
            //if (Dist != 0)
            //    trk.NudgeRefABLine(Dist);
            ABLine.BuildTram();
        }
    }

    /// <summary>
    /// [XPLAT] Body of the WinForms <c>nudPasses_ValueChanged</c> (source lines 158-164), with the
    /// 1..999 clamp that the WinForms <c>NudlessNumericUpDown</c> enforced via its Minimum/Maximum now
    /// applied explicitly (nudPasses is a plain display Button). Persists the value and rebuilds. Every
    /// passes mutation — the up/down buttons and the on-screen keypad — routes through here.
    /// </summary>
    /// <param name="v">The requested passes value before clamping.</param>
    private void SetPassesFromUser(int v)
    {
        passesValue = Math.Max(1, Math.Min(999, v));
        nudPasses.Content = passesValue.ToString(CultureInfo.InvariantCulture);
        tram.passes = passesValue;
        Settings.Default.setTram_passes = tram.passes;
        Settings.Default.Save();
        MoveBuildTramLine(0);
    }

    // [XPLAT] btnUpTrams_Click (source lines 170-173): nudPasses.UpButton() -> step up within 1..999.
    private void btnUpTrams_Click(object sender, RoutedEventArgs e)
    {
        SetPassesFromUser(passesValue + 1);
    }

    // [XPLAT] btnDnTrams_Click (source lines 175-178): nudPasses.DownButton() -> step down within 1..999.
    private void btnDnTrams_Click(object sender, RoutedEventArgs e)
    {
        SetPassesFromUser(passesValue - 1);
    }

    /// <summary>
    /// [XPLAT] Port of <c>nudPasses_Click</c> (source lines 166-169). The WinForms
    /// <c>NudlessNumericUpDown.ShowKeypad(this)</c> in-place keypad is replaced by the cross-platform
    /// <see cref="FormNumeric"/> modal dialog (range 1..999). On accept the entered value routes through
    /// <see cref="SetPassesFromUser"/>.
    /// </summary>
    private async void nudPasses_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] NudlessNumericUpDown.ShowKeypad(this) -> FormNumeric(1, 999, current).
        var f = new FormNumeric(1, 999, passesValue);
        if (await f.ShowDialog<bool>(this)) SetPassesFromUser((int)f.ReturnValue);
    }

    /// <summary>
    /// [XPLAT] Port of <c>tbarTramAlpha_Scroll</c> (source lines 274-278), wired to the Slider's
    /// <c>ValueChanged</c>. Updates <c>tram.alpha</c> (0..1 fraction) and the <c>lblAplha</c> percentage
    /// read-out. Guarded by <see cref="isLoading"/> so the initial Opened seed does not mutate
    /// <c>tram.alpha</c>.
    /// </summary>
    private void tbarTramAlpha_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (isLoading) return;

        int v = (int)tbarTramAlpha.Value;
        tram.alpha = v * 0.01;
        lblAplha.Text = v.ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnMode_Click</c> (source lines 248-272). Cycles the generate mode
    /// All(0) → Lines(1) → Outer(2) → All, swaps the <c>imgMode</c> glyph, and rebuilds the tram preview.
    /// </summary>
    private void btnMode_Click(object sender, RoutedEventArgs e)
    {
        tram.generateMode++;
        if (tram.generateMode > 2) tram.generateMode = 0;

        UpdateModeImage();

        MoveBuildTramLine(0);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnSwapAB_Click</c> (source lines 180-240) — tram geometry is behaviour-frozen
    /// (AAP §0.2.2), so the A/B (or curve) point-reversal maths are reproduced VERBATIM on the injected
    /// <see cref="trk"/> / <see cref="ABLine"/>. After the swap the tracks are saved, the working tram
    /// arrays are cleared, and the preview is rebuilt.
    /// </summary>
    private void btnSwapAB_Click(object sender, RoutedEventArgs e)
    {
        if (trk.gArr[trk.idx].mode == TrackMode.AB)
        {
            vec2 bob = trk.gArr[trk.idx].ptA;
            trk.gArr[trk.idx].ptA = trk.gArr[trk.idx].ptB;
            trk.gArr[trk.idx].ptB = new vec2(bob);

            trk.gArr[trk.idx].heading += Math.PI;
            if (trk.gArr[trk.idx].heading < 0) trk.gArr[trk.idx].heading += glm.twoPI;
            if (trk.gArr[trk.idx].heading > glm.twoPI) trk.gArr[trk.idx].heading -= glm.twoPI;

            double abHeading = trk.gArr[trk.idx].heading;
            trk.gArr[trk.idx].endPtA.easting = trk.gArr[trk.idx].ptA.easting - (Math.Sin(abHeading) * ABLine.abLength);
            trk.gArr[trk.idx].endPtA.northing = trk.gArr[trk.idx].ptA.northing - (Math.Cos(abHeading) * ABLine.abLength);

            trk.gArr[trk.idx].endPtB.easting = trk.gArr[trk.idx].ptB.easting + (Math.Sin(abHeading) * ABLine.abLength);
            trk.gArr[trk.idx].endPtB.northing = trk.gArr[trk.idx].ptB.northing + (Math.Cos(abHeading) * ABLine.abLength);

        }
        else
        {
            int cnt = trk.gArr[trk.idx].curvePts.Count;
            if (cnt > 0)
            {
                trk.gArr[trk.idx].curvePts.Reverse();

                vec3[] arr = new vec3[cnt];
                cnt--;
                trk.gArr[trk.idx].curvePts.CopyTo(arr);
                trk.gArr[trk.idx].curvePts.Clear();

                trk.gArr[trk.idx].heading += Math.PI;
                if (trk.gArr[trk.idx].heading < 0) trk.gArr[trk.idx].heading += glm.twoPI;
                if (trk.gArr[trk.idx].heading > glm.twoPI) trk.gArr[trk.idx].heading -= glm.twoPI;

                for (int i = 1; i < cnt; i++)
                {
                    vec3 pt3 = arr[i];
                    pt3.heading += Math.PI;
                    if (pt3.heading > glm.twoPI) pt3.heading -= glm.twoPI;
                    if (pt3.heading < 0) pt3.heading += glm.twoPI;
                    trk.gArr[trk.idx].curvePts.Add(pt3);
                }

                vec2 temp = new vec2(trk.gArr[trk.idx].ptA);

                (trk.gArr[trk.idx].ptA) = new vec2(trk.gArr[trk.idx].ptB);
                (trk.gArr[trk.idx].ptB) = new vec2(temp);
            }
        }

        saveTracks();   // [XPLAT] was mf.FileSaveTracks()

        tram.tramArr?.Clear();
        tram.tramList?.Clear();
        tram.tramBndOuterArr?.Clear();
        tram.tramBndInnerArr?.Clear();

        MoveBuildTramLine(0);
    }

    // [XPLAT] btnExit_Click (source lines 152-156): mark saving, then close (OnClosing persists the tram).
    private void btnExit_Click(object sender, RoutedEventArgs e)
    {
        isSaving = true;
        Close();
    }

    // [XPLAT] btnCancel_Click (source lines 242-246): close; OnClosing discards the working tram.
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormTram_FormClosing</c> (source lines 106-132), via the Avalonia
    /// <c>OnClosing(WindowClosingEventArgs)</c> override. When the operator cancelled (not saving) the
    /// working tram arrays are discarded and the display mode is reset; in all cases the surviving tram
    /// polylines are simplified, the tram + main panels are refreshed via the injected callbacks, and the
    /// alpha is persisted.
    /// </summary>
    /// <param name="e">The closing event data.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (isSaving)
        {
            //keep the working tram — the host saves it below.
        }
        else
        {
            tram.tramArr?.Clear();
            tram.tramList?.Clear();
            tram.tramBndOuterArr?.Clear();
            tram.tramBndInnerArr?.Clear();

            tram.displayMode = 0;
        }

        for (int i = 0; i < tram.tramList.Count; i++)
        {
            tram.tramList[i].ReducePointsByAngle(0.01, 100);
        }

        saveTram();                       // [XPLAT] was mf.FileSaveTram()
        panelUpdateRightAndBottom();      // [XPLAT] was mf.PanelUpdateRightAndBottom()
        fixTramModeButton();              // [XPLAT] was mf.FixTramModeButton()

        Settings.Default.setTram_alpha = tram.alpha;
        Settings.Default.Save();

        base.OnClosing(e);
    }
}
