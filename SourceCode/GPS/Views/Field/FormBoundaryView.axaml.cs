// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] "Start / Delete A Boundary" dialog — a 1:1 behavioral-parity port of the WinForms
/// <c>Forms/Field/FormBoundary</c> (FormBoundary.cs + .Designer.cs). The operator reviews the field's
/// boundary set (outer fence + inner exclusions) with each one's area and drive-thru flag, deletes the
/// selected boundary, opens the field in Google Earth, builds a boundary from recorded tracks, or adds a
/// new boundary — which branches to a "choose source" sub-screen and then to a "load from KML" sub-screen.
///
/// <para>
/// The WinForms form held a <c>private readonly FormGPS mf</c> back-reference and reached through it to
/// <c>mf.bnd</c> (the <see cref="CBoundary"/> boundary model), <c>mf.fd</c> (field-data GUI helper),
/// <c>mf.tool.width</c>, <c>mf.isMetric</c>, <c>mf.currentFieldDirectory</c>, <c>mf.AppModel</c> (the
/// <see cref="ApplicationModel"/> used for WGS84→local-plane KML conversion), <c>mf.trk</c> (the
/// <see cref="CTrack"/> track manager), and the host methods <c>FileSaveBoundary()</c>,
/// <c>FileLoadTracks()</c>, <c>FileMakeKMLFromCurrentPosition(...)</c>, <c>CloseTopMosts()</c> and the
/// AB-draw button refresh. Per the migration's Dependency-Inversion strategy (AAP §0.3.2) that WinForms
/// host coupling is replaced by constructor-injecting exactly those collaborators; there is no <c>mf</c>.
/// </para>
///
/// <para>
/// The original caller (FormGPS <c>boundariesToolStripMenuItem_Click</c>) opened this form modally and,
/// on a positive result, opened the <c>FormBoundaryPlayer</c> (record-by-driving) <em>non-modally</em> so
/// the operator can drive while recording — therefore this dialog NEVER opens the player itself: it closes
/// with a <see langword="true"/> dialog result (Drive/External chosen) so the caller opens the non-modal
/// player, mirroring the WinForms <c>DialogResult.OK</c> branch. Every other exit closes with
/// <see langword="false"/>. The WinForms Bing-Maps branch (which opened the GMap-based <c>FormMap</c>) is
/// re-expressed here as a guarded cross-platform Bing-Maps web launch, since GMap is replaced/feature-gated
/// (AAP §0.6.3) and no map view is in scope.
/// </para>
///
/// <para>
/// The paired <c>FormBoundaryView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls and wires no handlers. This
/// code-behind attaches the migrated WinForms handlers by name and assigns the translated <c>gStr.*</c>
/// strings at runtime. The WinForms <c>FlowLayoutPanel</c> of dynamic boundary buttons becomes an
/// <c>ItemsControl</c> (WrapPanel) inside a native <c>ScrollViewer</c>. There is NO OpenGL in this dialog.
/// </para>
/// </summary>
public partial class FormBoundaryView : Window
{
    #region Injected collaborators (replace the WinForms `FormGPS mf`)
    /// <summary>[XPLAT] Boundary model — was <c>mf.bnd</c>. Holds <c>bndList</c>, <c>isOkToAddPoints</c>, <c>BuildTurnLines()</c>.</summary>
    private readonly CBoundary _bnd;

    /// <summary>[XPLAT] Field-data GUI helper — was <c>mf.fd</c>; provides <c>UpdateFieldBoundaryGUIAreas()</c> (the <see cref="IBoundaryFieldData"/> seam shared with FormBoundaryPlayerView).</summary>
    private readonly IBoundaryFieldData _fieldData;

    /// <summary>[XPLAT] Application model — was <c>mf.AppModel</c>; supplies <c>CurrentLatLon</c> and <c>LocalPlane.ConvertWgs84ToGeoCoord(...)</c> for KML import.</summary>
    private readonly ApplicationModel _appModel;

    /// <summary>[XPLAT] Persists the boundary set to disk — was <c>mf.FileSaveBoundary()</c>.</summary>
    private readonly Action _fileSaveBoundary;

    /// <summary>[XPLAT] Writes the current GPS position as a KML for Google Earth — was <c>mf.FileMakeKMLFromCurrentPosition(mf.AppModel.CurrentLatLon)</c>.</summary>
    private readonly Action<Wgs84> _fileMakeKMLFromCurrentPosition;

    /// <summary>[XPLAT] Track manager — was <c>mf.trk</c>; passed through to the build-from-tracks dialog.</summary>
    private readonly CTrack _trk;

    /// <summary>[XPLAT] Loads the field tracks into <c>trk.gArr</c> — was <c>mf.FileLoadTracks()</c>; passed through to the build-from-tracks dialog.</summary>
    private readonly Action _fileLoadTracks;

    /// <summary>[XPLAT] Implement-tool working width (m) — was <c>mf.tool.width</c>; the "tool too small" guard reads it.</summary>
    private readonly double _toolWidth;

    /// <summary>[XPLAT] Metric vs imperial area display — was <c>mf.isMetric</c>.</summary>
    private readonly bool _isMetric;

    /// <summary>[XPLAT] Current field folder name — was <c>mf.currentFieldDirectory</c>; combined with <c>RegistrySettings.fieldsDirectory</c> for file paths.</summary>
    private readonly string _currentFieldDirectory;

    /// <summary>[XPLAT] Dismisses any other top-most floating dialogs — was <c>mf.CloseTopMosts()</c>; invoked once on load (parity with FormBoundary_Load).</summary>
    private readonly Action _closeTopMosts;

    /// <summary>[XPLAT] Refreshes the main view's AB-draw affordance — was <c>mf.btnABDraw.Visible = true</c>.</summary>
    private readonly Action _showAbDraw;

    /// <summary>[XPLAT] Owner window for the modal message dialogs and the child build-from-tracks dialog (subsumes the WinForms dialog-owner role).</summary>
    private readonly Window _owner;
    #endregion

    #region State (names + intent mirror FormBoundary.cs)
    // [XPLAT] WGS84 lon/lat scratch for the KML parse — verbatim instance fields from FormBoundary.cs
    // (declared as fields because double.TryParse writes them via `out`).
    private double latK, lonK;

    /// <summary>[XPLAT] Index of the selected boundary row, or -1 when none — was <c>fenceSelected</c>.</summary>
    private int fenceSelected = -1;

    /// <summary>[XPLAT] FormClosing guard — only an explicit code path (return / Drive / Bing / build-from-tracks) may close the window; the frame-close (X) is cancelled. Mirrors <c>FormBoundary_FormClosing</c>.</summary>
    private bool isClosing;

    /// <summary>[XPLAT] One-shot <see cref="OnLoaded"/> guard so the load-time initialisation runs exactly once.</summary>
    private bool _loaded;
    #endregion

    #region Constructors
    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time previewer
    /// only. The running application always constructs this dialog through the injected overload; this inert
    /// overload merely renders the static markup, wires no behaviour and leaves the collaborators null, so the
    /// <see cref="OnLoaded"/> / <see cref="OnClosing"/> guards treat a preview instance as a no-op.
    /// </summary>
    public FormBoundaryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the boundary dialog bound to the supplied collaborators (the DI seam that replaces the WinForms
    /// <c>FormGPS</c> back-reference).
    /// </summary>
    /// <param name="bnd">Boundary model (was <c>mf.bnd</c>).</param>
    /// <param name="fieldData">Field-data GUI helper exposing <c>UpdateFieldBoundaryGUIAreas()</c> (was <c>mf.fd</c>).</param>
    /// <param name="appModel">Application model for KML WGS84→local-plane conversion and current position (was <c>mf.AppModel</c>).</param>
    /// <param name="fileSaveBoundary">Persists the boundary set (was <c>mf.FileSaveBoundary()</c>).</param>
    /// <param name="fileMakeKMLFromCurrentPosition">Writes the current position KML for Google Earth (was <c>mf.FileMakeKMLFromCurrentPosition(...)</c>).</param>
    /// <param name="trk">Track manager passed through to the build-from-tracks dialog (was <c>mf.trk</c>).</param>
    /// <param name="fileLoadTracks">Loads the field tracks, passed through to the build-from-tracks dialog (was <c>mf.FileLoadTracks()</c>).</param>
    /// <param name="toolWidth">Implement-tool working width in metres (was <c>mf.tool.width</c>).</param>
    /// <param name="isMetric">Metric vs imperial area display (was <c>mf.isMetric</c>).</param>
    /// <param name="currentFieldDirectory">Current field folder name (was <c>mf.currentFieldDirectory</c>).</param>
    /// <param name="closeTopMosts">Dismisses other top-most dialogs on load (was <c>mf.CloseTopMosts()</c>).</param>
    /// <param name="showAbDraw">Refreshes the main view's AB-draw affordance (was <c>mf.btnABDraw.Visible = true</c>).</param>
    /// <param name="owner">Owner window for the modal dialogs.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is null.</exception>
    public FormBoundaryView(
        CBoundary bnd,
        IBoundaryFieldData fieldData,
        ApplicationModel appModel,
        Action fileSaveBoundary,
        Action<Wgs84> fileMakeKMLFromCurrentPosition,
        CTrack trk,
        Action fileLoadTracks,
        double toolWidth,
        bool isMetric,
        string currentFieldDirectory,
        Action closeTopMosts,
        Action showAbDraw,
        Window owner)
        : this()
    {
        // [XPLAT] Required collaborators are guarded so a missing wiring fails fast rather than NRE-ing deep
        // in a handler. Optional refresh delegates (closeTopMosts / showAbDraw) are invoked null-safely.
        _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        _fieldData = fieldData ?? throw new ArgumentNullException(nameof(fieldData));
        _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
        _fileSaveBoundary = fileSaveBoundary ?? throw new ArgumentNullException(nameof(fileSaveBoundary));
        _fileMakeKMLFromCurrentPosition = fileMakeKMLFromCurrentPosition ?? throw new ArgumentNullException(nameof(fileMakeKMLFromCurrentPosition));
        _trk = trk ?? throw new ArgumentNullException(nameof(trk));
        _fileLoadTracks = fileLoadTracks ?? throw new ArgumentNullException(nameof(fileLoadTracks));
        _toolWidth = toolWidth;
        _isMetric = isMetric;
        _currentFieldDirectory = currentFieldDirectory ?? string.Empty;
        _closeTopMosts = closeTopMosts;
        _showAbDraw = showAbDraw;
        _owner = owner;

        // [XPLAT] Translated captions — was the WinForms ctor (this.Text / labelBounds / Thru / Area) and
        // btnDelete.Enabled = false. The .axaml carries only design-time placeholders.
        Title = gStr.gsStartDeleteABoundary;
        labelBounds.Text = gStr.gsBoundary;
        Thru.Text = gStr.gsDriveThru;
        Area.Text = gStr.gsArea;
        btnDelete.IsEnabled = false;

        // [XPLAT] Attach the migrated WinForms designer wiring by name. Note the WinForms designer routed
        // btnCancel / btnCancelChoose / btnCancelKML ALL to btnReturn_Click, and both KML load buttons to the
        // single btnLoadBoundaryFromGE_Click (which distinguishes single vs multi by sender).
        btnCancel.Click += btnReturn_Click;
        btnCancelChoose.Click += btnReturn_Click;
        btnCancelKML.Click += btnReturn_Click;
        btnDelete.Click += btnDelete_Click;
        btnOpenGoogleEarth.Click += btnOpenGoogleEarth_Click;
        btnBuildBoundaryFromTracks.Click += btnBuildBoundaryFromTracks_Click;
        btnAdd.Click += btnAdd_Click;
        btnGetKML.Click += btnGetKML_Click;
        btnDriveOrExt.Click += btnDriveOrExt_Click;
        btnBingMaps.Click += btnBingMaps_Click;
        btnLoadBoundaryFromGE.Click += btnLoadBoundaryFromGE_Click;
        btnLoadMultiBoundaryFromGE.Click += btnLoadBoundaryFromGE_Click;
    }
    #endregion

    #region Window lifecycle
    /// <summary>
    /// [XPLAT] Port of <c>FormBoundary_Load</c>: build the boundary list and dismiss other top-most dialogs.
    /// The 600x300 size and default panel visibility (panelMain shown, panelChoose / panelKML hidden) come
    /// from the .axaml; the WinForms <c>ScreenHelper.IsOnScreen</c> reposition is dropped because
    /// <c>WindowStartupLocation="CenterOwner"</c> guarantees an on-screen placement. A previewer /
    /// parameterless instance (null model) renders the static markup only.
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

        UpdateChart();
        _closeTopMosts?.Invoke();
    }

    /// <summary>
    /// [XPLAT] Port of <c>FormBoundary_FormClosing</c>: block the window-frame (X) close unless an explicit
    /// code path has set <see cref="isClosing"/>. The <c>_bnd != null</c> guard lets a previewer /
    /// parameterless instance close freely.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_bnd != null && !isClosing)
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
    #endregion

    #region Boundary list (UpdateChart) + row interaction
    /// <summary>
    /// [XPLAT] Port of <c>UpdateChart</c>: clear and rebuild the boundary list. The WinForms
    /// <c>FlowLayoutPanel</c> received three Buttons per boundary — name (180w) / area (180w) / drive-thru
    /// (110w), each with a 10px margin — which wrap three-per-row; the same buttons are added to the
    /// <c>flp1</c> ItemsControl (its ItemsPanel is a horizontal WrapPanel). The boundary index is carried in
    /// each Button's <c>Tag</c> (the cross-platform stand-in for the WinForms <c>Button.Name = i.ToString()</c>,
    /// which a XAML control-name could not hold because names must be valid identifiers). The selected row's
    /// name + area buttons are coloured OrangeRed; <c>btnDelete</c>'s enabled state is owned by
    /// <see cref="B_Click"/> (not recomputed here) to preserve the WinForms behaviour exactly.
    /// </summary>
    private void UpdateChart()
    {
        int inner = 1;

        flp1.Items.Clear();

        for (int i = 0; i < _bnd.bndList.Count; i++)
        {
            // name / outer-inner button
            Button a = new Button
            {
                Width = 180,
                Height = 35,
                Margin = new Thickness(10),
                Tag = i
            };
            a.Click += B_Click;

            // area button
            Button b = new Button
            {
                Width = 180,
                Height = 35,
                Margin = new Thickness(10),
                Tag = i
            };
            b.Click += B_Click;

            // drive-thru button
            Button d = new Button
            {
                Width = 110,
                Height = 35,
                Margin = new Thickness(10),
                Tag = i
            };
            d.Click += DriveThru_Click;

            if (i == 0)
            {
                _bnd.bndList[i].isDriveThru = false;
                a.Content = gStr.gsOuter;
                d.Content = "--";
                d.IsEnabled = false;
            }
            else
            {
                inner += 1;
                a.Content = string.Format(CultureInfo.InvariantCulture, gStr.gsInner + " {0}", inner);
                d.Content = _bnd.bndList[i].isDriveThru ? "Yes" : "No";
            }

            // [XPLAT] Dynamic-precision area display, verbatim from FormBoundary.cs but pinned to
            // InvariantCulture (AAP §0.6.5 locale safety): the format string itself is sliced from
            // "0.########" so the smaller the integer part, the more decimal places are shown.
            if (_isMetric)
            {
                int length = (_bnd.bndList[i].area * 0.0001).ToString("0", CultureInfo.InvariantCulture).Length;
                if (length > 10) length = 10;
                if (length < 3) length = 3;
                b.Content = (_bnd.bndList[i].area * 0.0001).ToString("0.########".Substring(0, 11 - length), CultureInfo.InvariantCulture) + " Ha";
            }
            else
            {
                int length = (_bnd.bndList[i].area * 0.000247105).ToString("0", CultureInfo.InvariantCulture).Length;
                if (length > 10) length = 10;
                if (length < 3) length = 3;
                b.Content = (_bnd.bndList[i].area * 0.000247105).ToString("0.########".Substring(0, 11 - length), CultureInfo.InvariantCulture) + " Ac";
            }

            if (i == fenceSelected)
            {
                a.Foreground = Brushes.OrangeRed;
                b.Foreground = Brushes.OrangeRed;
            }

            flp1.Items.Add(a);
            flp1.Items.Add(b);
            flp1.Items.Add(d);
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>DriveThru_Click</c>: toggle the selected boundary's drive-thru flag, rebuild the
    /// list, and rebuild the turn lines. The boundary index comes from the Button's <c>Tag</c> (was
    /// <c>Convert.ToInt32(b.Name)</c>).
    /// </summary>
    private void DriveThru_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx)
        {
            _bnd.bndList[idx].isDriveThru = !_bnd.bndList[idx].isDriveThru;
            UpdateChart();
            _bnd.BuildTurnLines();
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>B_Click</c>: toggle row selection. Re-clicking the selected row deselects it
    /// (<c>fenceSelected = -1</c>); selecting the outer ring (index 0) only enables Delete when it is the sole
    /// boundary; selecting any inner ring enables Delete. The enable rule and the (intentional) fact that
    /// deselecting does NOT disable Delete are preserved verbatim from the WinForms source.
    /// </summary>
    private void B_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx)
        {
            int oldfenceSelected = fenceSelected;
            fenceSelected = idx;

            if (fenceSelected == oldfenceSelected)
                fenceSelected = -1;
            else if (fenceSelected == 0)
                btnDelete.IsEnabled = _bnd.bndList.Count == 1;
            else if (fenceSelected > 0)
                btnDelete.IsEnabled = true;
        }
        UpdateChart();
    }
    #endregion

    #region Command handlers
    /// <summary>
    /// [XPLAT] Port of <c>btnDelete_Click</c>: confirm via the cross-platform question dialog
    /// (<c>FormDialog.ShowQuestion(...) == DialogResult.OK</c> → <see cref="FormDialogView.ShowQuestionBlocking"/>),
    /// then clear the selected boundary's headland line, remove it, and refresh in the exact WinForms order:
    /// FileSaveBoundary → UpdateFieldBoundaryGUIAreas → BuildTurnLines → UpdateChart.
    /// </summary>
    private void btnDelete_Click(object sender, RoutedEventArgs e)
    {
        bool result = FormDialogView.ShowQuestionBlocking(
            gStr.gsCompletelyDeleteBoundary,
            gStr.gsDeleteForSure,
            owner: _owner);

        if (result)
        {
            btnDelete.IsEnabled = false;

            if (_bnd.bndList.Count > fenceSelected)
            {
                // Clear and remove selected boundary
                _bnd.bndList[fenceSelected].hdLine?.Clear();
                _bnd.bndList.RemoveAt(fenceSelected);
            }

            fenceSelected = -1;

            // Save updated boundary data and refresh UI (exact WinForms call order)
            _fileSaveBoundary();
            _fieldData.UpdateFieldBoundaryGUIAreas();
            _bnd.BuildTurnLines();
            UpdateChart();
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>ResetAllBoundary</c>: drop every boundary, persist the empty set, and refresh.
    /// </summary>
    private void ResetAllBoundary()
    {
        fenceSelected = -1;
        _bnd.bndList.Clear();
        _fileSaveBoundary();
        flp1.Items.Clear();

        UpdateChart();
        _bnd.BuildTurnLines();
        btnDelete.IsEnabled = false;
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnReturn_Click</c> (also the WinForms target of btnCancel / btnCancelChoose /
    /// btnCancelKML): cancel boundary-point capture, restore the main panel + 600x300 size, then close with a
    /// <see langword="false"/> result (the WinForms <c>DialogResult.Cancel</c> branch — caller takes no action).
    /// </summary>
    private void btnReturn_Click(object sender, RoutedEventArgs e)
    {
        _bnd.isOkToAddPoints = false;

        panelMain.IsVisible = true;
        panelChoose.IsVisible = false;
        panelKML.IsVisible = false;

        Width = 600;
        Height = 300;
        isClosing = true;
        UpdateChart();
        Close(false);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnAdd_Click</c>: reject an implausibly narrow tool (&lt; 0.2 m) with an error
    /// dialog, otherwise reveal the "choose boundary source" sub-screen and shrink the window to 245x350.
    /// The WinForms modal <c>FormDialog.Show(...)</c> becomes an awaited <see cref="FormDialogView.ShowAsync"/>.
    /// </summary>
    private async void btnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_toolWidth < 0.2)
        {
            await FormDialogView.ShowAsync("Tool Error", "Your tool is too small", DialogSeverity.Error, _owner);
            Log.EventWriter("Boundary, Tool is too narrow");
            return;
        }

        panelMain.IsVisible = false;
        panelKML.IsVisible = false;
        panelChoose.IsVisible = true;

        Width = 245;
        Height = 350;
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnGetKML_Click</c>: reveal the "load boundary from KML" sub-screen.
    /// </summary>
    private void btnGetKML_Click(object sender, RoutedEventArgs e)
    {
        panelMain.IsVisible = false;
        panelChoose.IsVisible = false;
        panelKML.IsVisible = true;
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnDriveOrExt_Click</c>: the operator chose to record the boundary by driving (or
    /// add it externally). The WinForms designer's <c>DialogResult.OK</c> closed the form, after which FormGPS
    /// opened the record-by-driving <c>FormBoundaryPlayer</c> <em>non-modally</em>. Closing with
    /// <see langword="true"/> reproduces that contract so the caller opens the non-modal player; the player is
    /// never opened from here because the operator must be able to drive while it is shown.
    /// </summary>
    private void btnDriveOrExt_Click(object sender, RoutedEventArgs e)
    {
        panelMain.IsVisible = false;
        panelChoose.IsVisible = false;
        panelKML.IsVisible = false;
        isClosing = true;
        Close(true);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnBingMaps_Click</c>. The WinForms designer's <c>DialogResult.Yes</c> closed the
    /// form, after which FormGPS opened the GMap-based <c>FormMap</c>. GMap is replaced/feature-gated and no
    /// map view is in scope (AAP §0.6.3), so this re-expresses the intent as a guarded cross-platform launch
    /// of Bing Maps centred on the current position, then closes with a <see langword="false"/> result.
    /// </summary>
    private void btnBingMaps_Click(object sender, RoutedEventArgs e)
    {
        panelMain.IsVisible = false;
        panelChoose.IsVisible = false;
        panelKML.IsVisible = false;

        // [XPLAT] Bing Maps aerial view centred on the current WGS84 position (InvariantCulture so the
        // lat/lon query never picks up a comma decimal separator on a non-US locale).
        Wgs84 pos = _appModel.CurrentLatLon;
        string url = string.Format(
            CultureInfo.InvariantCulture,
            "https://www.bing.com/maps?cp={0}~{1}&lvl=18&style=h",
            pos.Latitude,
            pos.Longitude);
        OpenInDefaultApp(url);

        isClosing = true;
        Close(false);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnBuildBoundaryFromTracks_Click</c>: when boundaries already exist, confirm their
    /// removal (<see cref="FormDialogView.ShowQuestionBlocking"/>) and clear them, then open the cross-platform
    /// <see cref="FormBuildBoundaryFromTracksView"/> modally (was <c>new FormBuildBoundaryFromTracks(mf,this).ShowDialog()</c>)
    /// and close this dialog afterwards.
    /// </summary>
    private async void btnBuildBoundaryFromTracks_Click(object sender, RoutedEventArgs e)
    {
        if (_bnd.bndList.Count > 0)
        {
            bool result = FormDialogView.ShowQuestionBlocking(
                "Boundary Exists",
                "A boundary already exists. Do you want to remove it?",
                owner: _owner);
            if (!result)
            {
                return;
            }

            _bnd.bndList.Clear();
        }

        var form = new FormBuildBoundaryFromTracksView(
            _trk,
            _fileLoadTracks,
            _bnd,
            () => _currentFieldDirectory,
            this);
        await form.ShowDialog(this);

        isClosing = true;
        Close(false);
    }

    /// <summary>
    /// [XPLAT] Port of <c>btnOpenGoogleEarth_Click</c>: write the current position as a KML and open it. The
    /// WinForms bare <c>Process.Start(path)</c> is replaced by the guarded cross-platform launcher
    /// (<see cref="OpenInDefaultApp"/>), which never throws on Linux/macOS, then closes with a
    /// <see langword="false"/> result.
    /// </summary>
    private void btnOpenGoogleEarth_Click(object sender, RoutedEventArgs e)
    {
        // save new copy of kml with selected flag and view in GoogleEarth
        _fileMakeKMLFromCurrentPosition(_appModel.CurrentLatLon);

        string kmlPath = Path.Combine(RegistrySettings.fieldsDirectory, _currentFieldDirectory, "CurrentPosition.KML");
        OpenInDefaultApp(kmlPath);

        isClosing = true;
        Close(false);
    }
    #endregion

    #region KML import (behaviour-frozen)
    /// <summary>
    /// [XPLAT] Port of <c>btnLoadBoundaryFromGE_Click</c> — wired to BOTH the single-load
    /// (<c>btnLoadBoundaryFromGE</c>) and multi-load (<c>btnLoadMultiBoundaryFromGE</c>) buttons; the
    /// <paramref name="sender"/> distinguishes them (multi first calls <see cref="ResetAllBoundary"/> and
    /// imports every <c>&lt;coordinates&gt;</c> block, single imports only the first then <c>break</c>s). The
    /// WinForms <c>OpenFileDialog</c> becomes <see cref="IStorageProvider.OpenFilePickerAsync"/>; the
    /// <c>&lt;coordinates&gt;</c> scan/parse is preserved byte-for-byte (BEHAVIOR-FROZEN, AAP §0.2.2):
    /// accumulate across lines, split on <c>{' ','\t','\r','\n'}</c>, split each tuple on <c>','</c> with
    /// <c>lon = fix[0]</c> / <c>lat = fix[1]</c> parsed under <see cref="CultureInfo.InvariantCulture"/>,
    /// convert WGS84→local-plane, build the fence, then <c>CalculateFenceArea</c> / <c>FixFenceLine</c>. The
    /// outer try/catch matches the source (a malformed file is logged and aborts the import).
    /// </summary>
    private async void btnLoadBoundaryFromGE_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] Identify single-vs-multi by control identity (was `button.Name == "btnLoadMultiBoundaryFromGE"`).
        bool isMulti = ReferenceEquals(sender, btnLoadMultiBoundaryFromGE);

        // [XPLAT] OpenFileDialog (KML filter, InitialDirectory = field folder) → StorageProvider picker.
        TopLevel topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IStorageFolder startFolder = null;
        string fieldFolder = Path.Combine(RegistrySettings.fieldsDirectory, _currentFieldDirectory);
        if (!string.IsNullOrEmpty(fieldFolder))
        {
            startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(fieldFolder);
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "KML files (*.KML)",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("KML files (*.KML)")
                {
                    // [XPLAT] Both cases listed for case-sensitive (Linux/macOS) filesystems.
                    Patterns = new[] { "*.kml", "*.KML" }
                }
            }
        });

        // [XPLAT] WinForms `if (ofd.ShowDialog() == DialogResult.Cancel) return;`
        if (files == null || files.Count == 0)
        {
            return;
        }

        string fileAndDirectory = files[0].Path.LocalPath;

        string coordinates = null;
        int startIndex;

        using (StreamReader reader = new StreamReader(fileAndDirectory))
        {
            if (isMulti) ResetAllBoundary();

            try
            {
                while (!reader.EndOfStream)
                {
                    // start to read the file
                    string line = reader.ReadLine();

                    startIndex = line.IndexOf("<coordinates>", StringComparison.Ordinal);

                    if (startIndex != -1)
                    {
                        while (true)
                        {
                            int endIndex = line.IndexOf("</coordinates>", StringComparison.Ordinal);

                            if (endIndex == -1)
                            {
                                // just add the line
                                if (startIndex == -1) coordinates += line.Substring(0);
                                else coordinates += line.Substring(startIndex + 13);
                            }
                            else
                            {
                                if (startIndex == -1) coordinates += line.Substring(0, endIndex);
                                else coordinates += line.Substring(startIndex + 13, endIndex - (startIndex + 13));
                                break;
                            }
                            line = reader.ReadLine();
                            line = line.Trim();
                            startIndex = -1;
                        }

                        line = coordinates;
                        char[] delimiterChars = { ' ', '\t', '\r', '\n' };
                        string[] numberSets = line.Split(delimiterChars);

                        // at least 3 points
                        if (numberSets.Length > 2)
                        {
                            CBoundaryList New = new CBoundaryList();

                            foreach (string item in numberSets)
                            {
                                string[] fix = item.Split(',');
                                double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lonK);
                                double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latK);

                                GeoCoord geoCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(latK, lonK));
                                New.fenceLine.Add(new vec3(geoCoord));
                            }

                            New.CalculateFenceArea(_bnd.bndList.Count);
                            New.FixFenceLine(_bnd.bndList.Count);

                            _bnd.bndList.Add(New);

                            _showAbDraw?.Invoke();

                            coordinates = "";
                        }
                        else
                        {
                            await FormDialogView.ShowAsync(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error, _owner);
                            Log.EventWriter("KML Read Error to make new field");
                        }

                        if (!isMulti)
                        {
                            break;
                        }
                    }
                }
                _fileSaveBoundary();
                _bnd.BuildTurnLines();
                _showAbDraw?.Invoke();
                UpdateChart();
            }
            catch (Exception ed)
            {
                Log.EventWriter("Load Boundary from GE " + ed.ToString());
                return;
            }
        }

        _bnd.isOkToAddPoints = false;

        panelMain.IsVisible = true;
        panelChoose.IsVisible = false;
        panelKML.IsVisible = false;

        Width = 600;
        Height = 300;

        UpdateChart();
    }
    #endregion

    #region Cross-platform helpers
    /// <summary>
    /// [XPLAT] Opens a file or URL in the host's default handler, replacing the WinForms bare
    /// <c>Process.Start(path)</c> (which on modern .NET no longer shell-executes and throws for a file path or
    /// http(s) URL). Uses the shell on Windows and the <c>open</c> / <c>xdg-open</c> launchers on macOS /
    /// Linux. Failures degrade gracefully (logged, never thrown) per AAP §0.7.2, so a missing Google-Earth or
    /// browser association can never break the dialog on a non-Windows host.
    /// </summary>
    private static void OpenInDefaultApp(string target)
    {
        try
        {
            if (string.IsNullOrEmpty(target)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo { FileName = "open", Arguments = "\"" + target + "\"", UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = "\"" + target + "\"", UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            Log.EventWriter("FormBoundaryView open target failed: " + ex.Message);
        }
    }
    #endregion
}

