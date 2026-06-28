// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] "Record Driven Boundary" toolbar dialog — a 1:1 behavioral-parity re-host of the WinForms
/// <c>Forms/Field/FormBoundaryPlayer</c> (FormBoundaryPlayer.cs + FormBoundaryPlayer.Designer.cs).
///
/// The operator drives the vehicle while this floating tool records the pivot/tool track into the
/// in-progress boundary point list, live-displaying the enclosed area (shoelace formula) and the point
/// count. On <c>btnStop</c> the recorded points are turned into a real <see cref="CBoundaryList"/>
/// (fence-area + fence-line fix-up), appended to the field's boundary set, persisted, and the turn
/// lines are rebuilt — exactly mirroring the WinForms handler order.
///
/// <para>
/// The original form held a <c>private readonly FormGPS mf</c> back-reference and reached through it to
/// <c>mf.bnd</c>, <c>mf.tool.width</c>, <c>mf.fd</c>, <c>mf.isMetric</c>, <c>mf.AddBoundaryPoint()</c>,
/// <c>mf.FileSaveBoundary()</c>, <c>mf.CalculateMinMax()</c>, <c>mf.btnABDraw</c>/redraw and
/// <c>Properties.Settings.Default.setBnd_isDrawPivot</c>. Per the migration's Dependency-Inversion
/// strategy (AAP §0.3.2) that WinForms host coupling is replaced by constructor-injecting the same
/// collaborators: the portable <see cref="CBoundary"/> model, the tool width, a small
/// <see cref="IBoundaryFieldData"/> seam (the <c>fd</c> role), and three host callbacks
/// (<c>FileSaveBoundary</c>, <c>AddBoundaryPoint</c>, <c>ShowAbDraw</c>). The draw-at-pivot preference
/// is read from / written to the cross-platform <see cref="AgOpenGPS.Properties.Settings"/> facade
/// directly, fully qualified so the sibling <c>AgOpenGPS.Views.Settings</c> namespace does not shadow it.
/// </para>
///
/// <para>
/// The paired <c>FormBoundaryPlayerView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls — keeping their original
/// WinForms names — and wires no handlers. This code-behind attaches the migrated WinForms handlers by
/// name, so the markup and the behavior stay decoupled exactly as the original Designer/code-behind
/// split did. The three dynamically-swapped <see cref="Image"/> sources (<c>imgPausePlay</c>,
/// <c>imgLeftRight</c>, <c>imgAntennaTool</c>) carry no markup <c>Source</c>; they are assigned here from
/// the cached <c>avares://</c> bitmaps that replace the WinForms <c>Properties.Resources.*</c> images.
/// </para>
/// </summary>
public partial class FormBoundaryPlayerView : Window
{
    // ---- Injected collaborators (replace the WinForms `FormGPS mf` back-reference, AAP §0.3.2) -------

    /// <summary>[XPLAT] Portable boundary model — replaces every <c>mf.bnd.*</c> access.</summary>
    private readonly CBoundary _bnd;

    /// <summary>[XPLAT] Implement width in metres — was <c>mf.tool.width</c>.</summary>
    private readonly double _toolWidth;

    /// <summary>[XPLAT] Field-data seam (the <c>mf.fd</c> role) — boundary GUI areas + field min/max.</summary>
    private readonly IBoundaryFieldData _fieldData;

    /// <summary>[XPLAT] Persists the field's boundary set — was <c>mf.FileSaveBoundary()</c>.</summary>
    private readonly Action _fileSaveBoundary;

    /// <summary>[XPLAT] Captures one boundary point at the current draw position — was <c>mf.AddBoundaryPoint()</c>.</summary>
    private readonly Action _addBoundaryPoint;

    /// <summary>[XPLAT] Refreshes the main map / reveals the AB-draw affordance — was <c>mf.btnABDraw.Visible = true</c> + redraw.</summary>
    private readonly Action _showAbDraw;

    /// <summary>[XPLAT] Metric (true) vs imperial (false) unit mode — was <c>mf.isMetric</c>.</summary>
    private readonly bool _isMetric;

    /// <summary>[XPLAT] Owner window for the modal child dialogs (question / message), so they centre on the app like the WinForms active form did.</summary>
    private readonly Window _owner;

    // ---- Cached toolbar artwork (replace WinForms Properties.Resources.*; swapped at run time) -------

    private readonly Bitmap _imgBoundaryRecord;
    private readonly Bitmap _imgBoundaryPause;
    private readonly Bitmap _imgBoundaryRight;
    private readonly Bitmap _imgBoundaryLeft;
    private readonly Bitmap _imgBoundaryRecordPivot;
    private readonly Bitmap _imgBoundaryRecordTool;

    // ---- Live state (names + intent mirror FormBoundaryPlayer.cs) ------------------------------------

    /// <summary>
    /// [XPLAT] Parity with <c>FormBoundaryPlayer.isClosing</c>: every close path is cancelled
    /// (<see cref="OnClosing"/>) unless an explicit exit (<c>btnStop</c>) set this flag first.
    /// </summary>
    private bool isClosing;

    /// <summary>
    /// [XPLAT] Area / point-count refresh timer (Avalonia <see cref="DispatcherTimer"/> replaces the
    /// WinForms designer <c>System.Windows.Forms.Timer</c> whose <c>Interval = 1000</c> and
    /// <c>Enabled = true</c> auto-started it). Created and started in <see cref="OnLoaded"/>, stopped and
    /// released in <see cref="OnClosed"/>.
    /// </summary>
    private DispatcherTimer timer1;

    /// <summary>Guards the one-time <see cref="OnLoaded"/> initialisation (Avalonia may raise Loaded more than once).</summary>
    private bool _loaded;

    /// <summary>Offset keypad ceiling (4999 cm metric / 1968 in imperial) — was <c>nudOffset.Maximum</c>.</summary>
    private double _nudOffsetMax;

    /// <summary>Current offset display value (cm metric / inch imperial) — was <c>nudOffset.Value</c>.</summary>
    private double _nudOffsetValue;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time
    /// previewer only. The running application always constructs this dialog through the injected
    /// overload; this inert overload merely renders the static markup, wires no behavior and leaves the
    /// model null, so the <see cref="OnLoaded"/>/<see cref="OnClosing"/> guards treat a preview instance
    /// as a no-op.
    /// </summary>
    public FormBoundaryPlayerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the driven-boundary recorder bound to the supplied collaborators.
    /// </summary>
    /// <param name="bnd">The boundary model (in-progress point list, draw flags, offset).</param>
    /// <param name="toolWidth">Implement width in metres (was <c>mf.tool.width</c>).</param>
    /// <param name="fieldData">Field-data seam exposing <c>UpdateFieldBoundaryGUIAreas</c> + <c>CalculateMinMax</c> (the <c>mf.fd</c>/<c>mf</c> role).</param>
    /// <param name="fileSaveBoundary">Persists the boundary set (was <c>mf.FileSaveBoundary()</c>).</param>
    /// <param name="addBoundaryPoint">Captures one boundary point (was <c>mf.AddBoundaryPoint()</c>).</param>
    /// <param name="isMetric">Metric vs imperial unit mode (was <c>mf.isMetric</c>).</param>
    /// <param name="showAbDraw">Refreshes the main view / reveals AB-draw (was <c>mf.btnABDraw.Visible = true</c> + redraw).</param>
    /// <param name="owner">Owner window for the modal child dialogs.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bnd"/>, <paramref name="fieldData"/>, <paramref name="fileSaveBoundary"/>, <paramref name="addBoundaryPoint"/> or <paramref name="showAbDraw"/> is null.</exception>
    public FormBoundaryPlayerView(
        CBoundary bnd,
        double toolWidth,
        IBoundaryFieldData fieldData,
        Action fileSaveBoundary,
        Action addBoundaryPoint,
        bool isMetric,
        Action showAbDraw,
        Window owner)
        : this()
    {
        // [XPLAT] The DI seam replaces "mf = callingForm as FormGPS;" — the required collaborators are
        // guarded so a missing wiring fails fast rather than NRE-ing deep in a handler.
        _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        _fieldData = fieldData ?? throw new ArgumentNullException(nameof(fieldData));
        _fileSaveBoundary = fileSaveBoundary ?? throw new ArgumentNullException(nameof(fileSaveBoundary));
        _addBoundaryPoint = addBoundaryPoint ?? throw new ArgumentNullException(nameof(addBoundaryPoint));
        _showAbDraw = showAbDraw ?? throw new ArgumentNullException(nameof(showAbDraw));
        _toolWidth = toolWidth;
        _isMetric = isMetric;
        _owner = owner;

        // [XPLAT] Cache the dynamically-swapped toolbar artwork once (parity with the WinForms
        // Properties.Resources.* images). Note the exact source casing — "boundaryPause" is lower-case.
        _imgBoundaryRecord = LoadBitmap("BoundaryRecord.png");
        _imgBoundaryPause = LoadBitmap("boundaryPause.png");
        _imgBoundaryRight = LoadBitmap("BoundaryRight.png");
        _imgBoundaryLeft = LoadBitmap("BoundaryLeft.png");
        _imgBoundaryRecordPivot = LoadBitmap("BoundaryRecordPivot.png");
        _imgBoundaryRecordTool = LoadBitmap("BoundaryRecordTool.png");

        // [XPLAT] Localised window title + "Area:" caption (parity with the WinForms constructor, which
        // overwrote the designer English text: this.Text = gStr.gsStopRecordPauseBoundary;
        // label1.Text = gStr.gsArea + ":";).
        Title = gStr.gsStopRecordPauseBoundary;
        label1.Text = gStr.gsArea + ":";

        // [XPLAT] The .axaml declares only x:Name (no handlers); attach the migrated WinForms handlers
        // here by name so markup and behavior stay decoupled, exactly as the Designer/code-behind split.
        nudOffset.Click += nudOffset_Click;
        btnStop.Click += btnStop_Click;
        btnPausePlay.Click += btnPausePlay_Click;
        btnAddPoint.Click += btnAddPoint_Click;
        btnDeleteLast.Click += btnDeleteLast_Click;
        btnRestart.Click += btnRestart_Click;
        btnLeftRight.Click += btnLeftRight_Click;
        btnAntennaTool.Click += btnAntennaTool_Click;

        // [XPLAT] IsCheckedChanged (not the obsolete Checked/Unchecked) fires on either transition,
        // reproducing the WinForms cboxIsRecBoundaryWhenSectionOn_Click on every toggle.
        cboxIsRecBoundaryWhenSectionOn.IsCheckedChanged += cboxIsRecBoundaryWhenSectionOn_Click;
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormBoundaryPlayer_Load</c>: seed the offset keypad range/value and units
    /// caption, set the initial toolbar images, prime the boundary model, and start the refresh timer.
    /// Runs once; a previewer/parameterless instance (no injected model) renders the static markup only.
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Previewer / parameterless-construction guard — nothing to drive without the injected model.
        if (_bnd == null)
        {
            return;
        }

        if (_loaded)
        {
            return;
        }
        _loaded = true;

        if (_isMetric)
        {
            // Metric: offset is centimetres; max 4999 cm; seed at half the implement width.
            _nudOffsetMax = 4999;
            _nudOffsetValue = _toolWidth * 0.5 * 100;
            lblMetersInches.Text = gStr.gsCentimeters;
        }
        else
        {
            // Imperial: offset is inches; max 1968 in; seed at half the implement width. The caption is
            // the whole-foot/whole-inch form the WinForms Load built (integer inches, no decimal).
            _nudOffsetMax = 1968;
            _nudOffsetValue = _toolWidth * 0.5 * 39.3701;
            double ftInches = _nudOffsetValue;
            lblMetersInches.Text = ((int)(ftInches / 12)).ToString(CultureInfo.InvariantCulture)
                + "' " + ((int)(ftInches % 12)).ToString(CultureInfo.InvariantCulture) + "\"";
        }

        // WinForms NumericUpDown had DecimalPlaces=0 and ThousandsSeparator=false, so it displayed
        // Value.ToString("F0"); reproduce that on the read-only Button display (InvariantCulture per the
        // cross-platform locale mandate, AAP §0.6.5).
        UpdateNudOffsetDisplay();

        // Initial toolbar images (parity with the WinForms Load assignments).
        imgPausePlay.Source = _imgBoundaryRecord;

        _bnd.isDrawAtPivot = AgOpenGPS.Properties.Settings.Default.setBnd_isDrawPivot;

        imgLeftRight.Source = _bnd.isDrawRightSide ? _imgBoundaryRight : _imgBoundaryLeft;
        imgAntennaTool.Source = _bnd.isDrawAtPivot ? _imgBoundaryRecordPivot : _imgBoundaryRecordTool;

        _bnd.createBndOffset = _toolWidth * 0.5;
        _bnd.isBndBeingMade = true;

        // WinForms timer1: Interval = 1000 ms, Enabled = true (auto-start).
        timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        timer1.Tick += timer1_Tick;
        timer1.Start();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormBoundaryPlayer_FormClosing</c>: veto every close attempt except the
    /// explicit exit path (<c>btnStop</c>), which sets <see cref="isClosing"/> first. The
    /// <see cref="_bnd"/> null-check lets a design-time / previewer instance close normally.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_bnd != null && !isClosing)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// [XPLAT] Stop and release the refresh timer when the window closes. WinForms disposed the designer
    /// timer with the form; the DispatcherTimer must be stopped explicitly so it stops firing and is not
    /// leaked.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        if (timer1 != null)
        {
            timer1.Stop();
            timer1.Tick -= timer1_Tick;
            timer1 = null;
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// [XPLAT] Port of <c>timer1_Tick</c>: compute the in-progress boundary area with the shoelace
    /// formula (verbatim loop), convert to the display unit (hectares metric / acres imperial), round to
    /// two decimals and refresh the area + point-count labels. All formatting uses
    /// <see cref="CultureInfo.InvariantCulture"/> for cross-platform locale-independence (AAP §0.6.5).
    /// </summary>
    private void timer1_Tick(object sender, EventArgs e)
    {
        int ptCount = _bnd.bndBeingMadePts.Count;
        double area = 0;

        if (ptCount > 0)
        {
            int j = ptCount - 1;  // The last vertex is the 'previous' one to the first

            for (int i = 0; i < ptCount; j = i++)
            {
                area += (_bnd.bndBeingMadePts[j].easting + _bnd.bndBeingMadePts[i].easting)
                      * (_bnd.bndBeingMadePts[j].northing - _bnd.bndBeingMadePts[i].northing);
            }
            area = Math.Abs(area / 2);
        }

        if (_isMetric)
        {
            lblArea.Text = Math.Round(area * 0.0001, 2).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            lblArea.Text = Math.Round(area * 0.000247105, 2).ToString(CultureInfo.InvariantCulture);
        }

        lblPoints.Text = _bnd.bndBeingMadePts.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Port of <c>nudOffset_Click</c>: the read-only offset display opens the on-screen
    /// <see cref="FormNumeric"/> keypad (replacing the WinForms <c>NudlessNumericUpDown.ShowKeypad</c>).
    /// On accept the new value is stored and <c>createBndOffset</c> is recomputed: centimetres ÷ 100 in
    /// metric, inches ÷ 39.3701 in imperial (with the feet/inches caption rebuilt to one decimal place,
    /// matching the WinForms "N1" inch format here). On cancel the value is unchanged, which reproduces
    /// the WinForms no-op recompute. InvariantCulture throughout (AAP §0.6.5).
    /// </summary>
    private async void nudOffset_Click(object sender, RoutedEventArgs e)
    {
        var keypad = new FormNumeric(0, _nudOffsetMax, _nudOffsetValue);
        bool accepted = await keypad.ShowDialog<bool>(this);

        // WinForms returned focus to the record button after the keypad regardless of the result.
        btnPausePlay.Focus();

        if (!accepted)
        {
            return;
        }

        _nudOffsetValue = keypad.ReturnValue;
        UpdateNudOffsetDisplay();

        if (_isMetric)
        {
            _bnd.createBndOffset = _nudOffsetValue * 0.01;
        }
        else
        {
            _bnd.createBndOffset = _nudOffsetValue / 39.3701;
            double ftInches = _nudOffsetValue;
            lblMetersInches.Text = ((int)(ftInches / 12)).ToString(CultureInfo.InvariantCulture)
                + "' " + (ftInches % 12).ToString("N1", CultureInfo.InvariantCulture) + "\"";
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnStop_Click</c>: confirm "Done?"; if confirmed and more than two points were
    /// recorded, build a real boundary from the points and commit it in the exact WinForms order
    /// (CalculateFenceArea → FixFenceLine → add → UpdateFieldBoundaryGUIAreas → CalculateMinMax →
    /// FileSaveBoundary → BuildTurnLines → reveal AB-draw → log), otherwise show the "no boundary"
    /// message. Either way the recording state is reset and the dialog closes via the explicit exit path.
    /// </summary>
    private async void btnStop_Click(object sender, RoutedEventArgs e)
    {
        // Ask user if they are done with the boundary (FormDialog.ShowQuestion(...) == DialogResult.OK).
        bool result = FormDialogView.ShowQuestionBlocking(gStr.gsBoundary, "Done?", null, _owner);

        if (result)
        {
            if (_bnd.bndBeingMadePts.Count > 2)
            {
                // Create new boundary from drawn points.
                CBoundaryList New = new CBoundaryList();

                for (int i = 0; i < _bnd.bndBeingMadePts.Count; i++)
                {
                    New.fenceLine.Add(_bnd.bndBeingMadePts[i]);
                }

                New.CalculateFenceArea(_bnd.bndList.Count);
                New.FixFenceLine(_bnd.bndList.Count);
                _bnd.bndList.Add(New);

                // Update field GUI and boundaries.
                _fieldData.UpdateFieldBoundaryGUIAreas();
                _fieldData.CalculateMinMax();
                _fileSaveBoundary();
                _bnd.BuildTurnLines();

                // Reveal the AB-draw affordance / refresh the main view (was mf.btnABDraw.Visible = true).
                _showAbDraw();

                Log.EventWriter("Driven Boundary Created, Area: " + lblArea.Text);
            }
            else
            {
                await FormDialogView.ShowAsync(gStr.gsNoBoundary, gStr.gsExit, DialogSeverity.Error, _owner);
            }

            // Stop adding points and reset state.
            _bnd.isOkToAddPoints = false;
            _bnd.isBndBeingMade = false;
            _bnd.bndBeingMadePts.Clear();

            isClosing = true;
            Close();
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnPausePlay_Click</c> (the record/pause toggle). When recording is paused the
    /// add/delete buttons are enabled (manual editing) and the record glyph is shown; when recording is
    /// active they are disabled and the pause glyph is shown.
    /// </summary>
    private void btnPausePlay_Click(object sender, RoutedEventArgs e)
    {
        if (_bnd.isOkToAddPoints)
        {
            _bnd.isOkToAddPoints = false;
            imgPausePlay.Source = _imgBoundaryRecord;
            btnAddPoint.IsEnabled = true;
            btnDeleteLast.IsEnabled = true;
        }
        else
        {
            _bnd.isOkToAddPoints = true;
            imgPausePlay.Source = _imgBoundaryPause;
            btnAddPoint.IsEnabled = false;
            btnDeleteLast.IsEnabled = false;
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnAddPoint_Click</c>: briefly enable point capture, add one boundary point,
    /// disable capture again, and refresh the point count (exact WinForms toggling).
    /// </summary>
    private void btnAddPoint_Click(object sender, RoutedEventArgs e)
    {
        _bnd.isOkToAddPoints = true;
        _addBoundaryPoint();
        _bnd.isOkToAddPoints = false;
        lblPoints.Text = _bnd.bndBeingMadePts.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnDeleteLast_Click</c>: remove the most recently captured point (guarding an
    /// empty list) and refresh the point count.
    /// </summary>
    private void btnDeleteLast_Click(object sender, RoutedEventArgs e)
    {
        int ptCount = _bnd.bndBeingMadePts.Count;
        if (ptCount > 0)
        {
            _bnd.bndBeingMadePts.RemoveAt(ptCount - 1);
        }
        lblPoints.Text = _bnd.bndBeingMadePts.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnRestart_Click</c>: confirm a complete delete; if confirmed, clear the
    /// in-progress points. The point count is refreshed regardless (matching the WinForms source).
    /// </summary>
    private void btnRestart_Click(object sender, RoutedEventArgs e)
    {
        bool result = FormDialogView.ShowQuestionBlocking(
            gStr.gsDeleteForSure,
            gStr.gsCompletelyDeleteBoundary,
            null,
            _owner);

        if (result)
        {
            _bnd.bndBeingMadePts?.Clear();
        }
        lblPoints.Text = _bnd.bndBeingMadePts.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnLeftRight_Click</c>: flip which side of the track the boundary offset is
    /// drawn on and swap the left/right glyph.
    /// </summary>
    private void btnLeftRight_Click(object sender, RoutedEventArgs e)
    {
        _bnd.isDrawRightSide = !_bnd.isDrawRightSide;
        imgLeftRight.Source = _bnd.isDrawRightSide ? _imgBoundaryRight : _imgBoundaryLeft;
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnAntennaTool_Click</c>: flip between drawing at the antenna/pivot and at the
    /// tool, swap the glyph, and persist the preference to the cross-platform Settings facade. Parity
    /// note: the WinForms source did NOT call Settings.Save() here, so neither do we.
    /// </summary>
    private void btnAntennaTool_Click(object sender, RoutedEventArgs e)
    {
        _bnd.isDrawAtPivot = !_bnd.isDrawAtPivot;
        imgAntennaTool.Source = _bnd.isDrawAtPivot ? _imgBoundaryRecordPivot : _imgBoundaryRecordTool;
        AgOpenGPS.Properties.Settings.Default.setBnd_isDrawPivot = _bnd.isDrawAtPivot;
    }

    /// <summary>
    /// [XPLAT] Port of <c>cboxIsRecBoundaryWhenSectionOn_Click</c>: mirror the toggle into the model so
    /// points are auto-recorded whenever a section is on.
    /// </summary>
    private void cboxIsRecBoundaryWhenSectionOn_Click(object sender, RoutedEventArgs e)
    {
        _bnd.isRecBoundaryWhenSectionOn = cboxIsRecBoundaryWhenSectionOn.IsChecked == true;
    }

    /// <summary>
    /// [XPLAT] Refresh the read-only offset display. WinForms showed <c>Value.ToString("F0")</c>
    /// (DecimalPlaces=0, ThousandsSeparator=false); InvariantCulture keeps it locale-independent.
    /// </summary>
    private void UpdateNudOffsetDisplay()
    {
        nudOffset.Content = _nudOffsetValue.ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [XPLAT] Load a toolbar bitmap from the embedded <c>avares://</c> assets, replacing the WinForms
    /// <c>Properties.Resources.*</c> image lookups with the same artwork.
    /// </summary>
    private static Bitmap LoadBitmap(string fileName)
    {
        return new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
    }
}

/// <summary>
/// [XPLAT] Minimal field-data seam carved out of the WinForms <c>FormGPS</c> god-object — the <c>mf.fd</c>
/// / <c>mf</c> role this dialog used when committing a driven boundary. The composition root supplies an
/// adapter; defining the contract locally (the convention sibling views follow for their telemetry seams)
/// keeps <see cref="FormBoundaryPlayerView"/> free of any <c>FormGPS</c> dependency.
/// </summary>
public interface IBoundaryFieldData
{
    /// <summary>Recompute and refresh the boundary-area read-outs after the boundary set changes (was <c>mf.fd.UpdateFieldBoundaryGUIAreas()</c>).</summary>
    void UpdateFieldBoundaryGUIAreas();

    /// <summary>Recompute the field's world min/max extents after the boundary set changes (was <c>mf.CalculateMinMax()</c>).</summary>
    void CalculateMinMax();
}
