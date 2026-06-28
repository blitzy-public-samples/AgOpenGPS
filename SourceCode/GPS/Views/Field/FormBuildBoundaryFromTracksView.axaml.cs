// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Linq;
using AgLibrary.Logging;
using AgOpenGPS.Classes;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Helpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] "Build Boundary From Tracks" GL-hosting dialog — a 1:1 behavioral-parity re-host of the
/// WinForms <c>Forms/Field/FormBuildBoundaryFromTracks</c> (FormBuildBoundaryFromTracks.cs +
/// .Designer.cs). The operator (de)selects the field's saved guidance tracks, optionally extends /
/// shrinks the active track's ends and auto-finds intersections, then builds a single field boundary
/// from the trimmed-and-intersected track network and saves it back to the field.
///
/// <para>
/// The original form held a <c>private readonly FormGPS mf</c> back-reference and reached through it to
/// <c>mf.trk</c> (the <see cref="CTrack"/> track manager whose <c>gArr</c> list is swapped during the
/// preview load), <c>mf.FileLoadTracks()</c>, <c>mf.bnd</c> (the <see cref="CBoundary"/> the built
/// boundary is written into) and <c>mf.currentFieldDirectory</c> (the save target). Per the migration's
/// Dependency-Inversion strategy (AAP §0.3.2) that WinForms host coupling is replaced by
/// constructor-injecting exactly those collaborators; there is no <c>mf</c> and no WinForms
/// <c>parentForm</c>.
/// </para>
///
/// <para>
/// GL-HOSTING: the WinForms <c>glControlPreview</c> (an <c>OpenTK.GLControl</c> drawn with immediate-mode
/// <c>GL.Begin</c>/<c>GL.Vertex2</c>/<c>GL.Ortho</c>) is replaced by the cross-platform
/// <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport"/> hosted inside the <c>oglHost</c> container. The
/// view owns NO GL context: it builds the viewport's bounding box from the computed track bounds (the
/// stand-in for <c>GL.Ortho</c>) and supplies its draw sequence to the host through
/// <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport.RenderAction"/>; the host wraps that callback in
/// <c>BeginPaint()</c>/<c>EndPaint()</c> (context-make-current + clear + projection + swap). Every raw
/// immediate-mode draw helper is re-expressed against the kept Core <c>GLW</c> DrawLib in geo
/// coordinates, preserving the exact source colours.
/// </para>
///
/// <para>
/// The paired <c>FormBuildBoundaryFromTracksView.axaml</c> is intentionally imperative (no
/// <c>x:DataType</c>, no <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls and
/// wires no handlers. This code-behind attaches the migrated WinForms handlers by name. The WinForms
/// <c>FlowLayoutPanel</c> of per-track checkboxes becomes an <c>ItemsControl</c> inside a native
/// <c>ScrollViewer</c>, so the original manual drag-scroll plumbing is intentionally not ported.
/// </para>
/// </summary>
public partial class FormBuildBoundaryFromTracksView : Window
{
    #region Constants
    // [XPLAT] Verbatim from FormBuildBoundaryFromTracks.cs.
    private const double VIEW_MARGIN_FACTOR = 0.1;
    private const double DEFAULT_VIEW_SIZE = 100;
    private const float SELECTED_TRACK_WIDTH = 5.0f;
    private const float NORMAL_TRACK_WIDTH = 1.0f;
    private const float BOUNDARY_LINE_WIDTH = 3.0f;
    private const float INTERSECTION_POINT_SIZE = 4.0f;
    private const int CIRCLE_SEGMENTS = 16;
    #endregion

    #region Draw colours
    // [XPLAT] The WinForms immediate-mode GL.Color3(System.Drawing.Color.*) values re-expressed as Core
    // DrawLib ColorRgba. Note the explicit (byte) casts: a bare int literal would bind to the float
    // ColorRgba ctor (0..1 range) and throw for values > 1, so each component is cast to the byte ctor.
    // RGB matches the System.Drawing named colours exactly: Yellow(255,255,0), Gray(128,128,128),
    // Green(0,128,0), Red(255,0,0).
    private static readonly ColorRgba ColorSelectedTrack = new ColorRgba((byte)255, (byte)255, (byte)0);
    private static readonly ColorRgba ColorUnselectedTrack = new ColorRgba((byte)128, (byte)128, (byte)128);
    private static readonly ColorRgba ColorTrimmed = new ColorRgba((byte)0, (byte)128, (byte)0);
    private static readonly ColorRgba ColorIntersection = new ColorRgba((byte)255, (byte)0, (byte)0);
    private static readonly ColorRgba ColorFinalBoundary = new ColorRgba((byte)255, (byte)0, (byte)0);
    private static readonly ColorRgba ColorRawSegment = new ColorRgba((byte)128, (byte)128, (byte)128);
    #endregion

    #region Injected collaborators (replace the WinForms `FormGPS mf` + `Form parentForm`)
    /// <summary>[XPLAT] Track manager — was <c>mf.trk</c>; <see cref="LoadTracks"/> temporarily swaps its <c>gArr</c> to load the preview list.</summary>
    private readonly CTrack _trk;

    /// <summary>[XPLAT] Loads the field's tracks into <c>trk.gArr</c> — was <c>mf.FileLoadTracks()</c>.</summary>
    private readonly Action _fileLoadTracks;

    /// <summary>[XPLAT] Boundary model the built boundary is written into — was <c>mf.bnd</c>.</summary>
    private readonly CBoundary _bnd;

    /// <summary>[XPLAT] Current field directory accessor (save target) — was <c>mf.currentFieldDirectory</c>.</summary>
    private readonly Func<string> _getCurrentFieldDirectory;

    /// <summary>[XPLAT] Owner window for the modal message dialogs (subsumes the WinForms dialog-owner role; the original <c>parentForm</c> was stored but never used).</summary>
    private readonly Window _owner;
    #endregion

    #region State (names + intent mirror FormBuildBoundaryFromTracks.cs)
    private readonly List<CTrk> _trackList = new List<CTrk>();
    private readonly List<CTrk> _selectedTracks = new List<CTrk>();
    private BoundaryBuilder _builder;
    private CTrk _activeTrack;
    private double _viewLeft, _viewRight, _viewTop, _viewBottom;
    private bool _showTrimmedOnly;
    private bool _loaded;

    /// <summary>[XPLAT] The hosted cross-platform GL surface that replaces the WinForms <c>glControlPreview</c>.</summary>
    private AgOpenGPS.Controls.AvaloniaGeoViewport _viewport;
    #endregion

    #region Constructors
    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time
    /// previewer only. The running application always constructs this dialog through the injected
    /// overload; this inert overload merely renders the static markup, wires no behavior and leaves the
    /// collaborators null, so the <see cref="OnLoaded"/> guard treats a preview instance as a no-op.
    /// </summary>
    public FormBuildBoundaryFromTracksView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the build-boundary dialog bound to the supplied collaborators.
    /// </summary>
    /// <param name="trk">Track manager (was <c>mf.trk</c>); its <c>gArr</c> is swapped during the preview load.</param>
    /// <param name="fileLoadTracks">Loads the field tracks into <c>trk.gArr</c> (was <c>mf.FileLoadTracks()</c>).</param>
    /// <param name="bnd">Boundary model the built boundary is written into (was <c>mf.bnd</c>).</param>
    /// <param name="getCurrentFieldDirectory">Accessor for the current field directory / save target (was <c>mf.currentFieldDirectory</c>).</param>
    /// <param name="owner">Owner window for the modal message dialogs.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trk"/>, <paramref name="fileLoadTracks"/>, <paramref name="bnd"/> or <paramref name="getCurrentFieldDirectory"/> is null.</exception>
    public FormBuildBoundaryFromTracksView(
        CTrack trk,
        Action fileLoadTracks,
        CBoundary bnd,
        Func<string> getCurrentFieldDirectory,
        Window owner)
        : this()
    {
        // [XPLAT] The DI seam replaces the WinForms `mf`/`parentForm` constructor arguments; required
        // collaborators are guarded so a missing wiring fails fast rather than NRE-ing deep in a handler.
        _trk = trk ?? throw new ArgumentNullException(nameof(trk));
        _fileLoadTracks = fileLoadTracks ?? throw new ArgumentNullException(nameof(fileLoadTracks));
        _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
        _getCurrentFieldDirectory = getCurrentFieldDirectory ?? throw new ArgumentNullException(nameof(getCurrentFieldDirectory));
        _owner = owner;

        _showTrimmedOnly = false;

        // Parity with the WinForms constructor: load the preview tracks up front (swap-load-restore).
        LoadTracks();

        // [XPLAT] Seed the view bounds from the loaded tracks so the GL host receives the COMPUTED track
        // bounds at construction (the cross-platform replacement for the WinForms GL.Ortho call). OnLoaded
        // re-runs the full InitializeForm/BuildTrackSelectorUI/UpdateViewBounds sequence authoritatively.
        _selectedTracks.AddRange(_trackList);
        UpdateViewBounds();

        _viewport = new AgOpenGPS.Controls.AvaloniaGeoViewport(BuildBoundingBox());
        _viewport.RenderAction = RenderScene;
        oglHost.Child = _viewport.View;

        // [XPLAT] The .axaml declares only x:Name (no handlers); attach the migrated WinForms handlers
        // here by name, exactly reproducing the WinForms designer's `this.btnX.Click += ...` wiring.
        btnAutoFind.Click += btnAutofind_click;
        btnSelectPrevious.Click += btnSelectPrevious_Click;
        btnSelectNext.Click += btnSelectNext_Click;
        btnExtendBackward.Click += btnExtendBackward_Click;
        btnShrinkA.Click += btnShrinkA_Click;
        btnShrinkB.Click += btnShrinkB_Click;
        btnExtendForward.Click += btnExtendForward_Click;
        btnResetPreview.Click += btnResetPreview_Click;
        btnBuildBoundary.Click += btnBuildBoundary_Click;
        btnSave.Click += btnSave_Click;
        btnClose.Click += btnClose_Click;
    }
    #endregion

    #region Form lifecycle
    /// <summary>
    /// [XPLAT] Port of <c>FormBuildBoundaryFromTracks_Load</c>: initialise selection state, build the
    /// builder and the track-selector checkboxes, recolour the list, fit the view and request the first
    /// redraw. Runs once; a previewer/parameterless instance (no injected model) renders static markup only.
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Previewer / parameterless-construction guard — nothing to drive without the injected model.
        if (_trk == null)
        {
            return;
        }

        if (_loaded)
        {
            return;
        }
        _loaded = true;

        InitializeForm();
        InitializeBuilder();
        BuildTrackSelectorUI();
        UpdateTrackListHighlighting();
        UpdateViewBounds();
        RequestRedraw();
    }

    /// <summary>[XPLAT] Port of <c>InitializeForm</c>: active track = first, all tracks selected, save disabled.</summary>
    private void InitializeForm()
    {
        _activeTrack = _trackList.FirstOrDefault();
        _showTrimmedOnly = false;
        _selectedTracks.Clear();
        _selectedTracks.AddRange(_trackList);
        btnSave.IsEnabled = false;
    }

    /// <summary>
    /// [XPLAT] Port of <c>LoadTracks</c> — preserves the swap-load-restore exactly: temporarily point the
    /// track manager's <c>gArr</c> at a scratch list, load the field tracks into it via the injected
    /// <c>FileLoadTracks</c> delegate, copy them into the preview list, and always restore the original
    /// <c>gArr</c> in the finally block (operating on the injected <see cref="CTrack"/> instead of <c>mf.trk</c>).
    /// </summary>
    private void LoadTracks()
    {
        _trackList.Clear();
        var tempTrackList = new List<CTrk>();
        var originalTrackList = _trk.gArr;

        try
        {
            _trk.gArr = tempTrackList;
            _fileLoadTracks();

            if (tempTrackList.Count == 0)
            {
                _ = FormDialogView.ShowAsync("Track Info", "No tracks found.", DialogSeverity.Info, _owner);
                return;
            }
            _trackList.AddRange(tempTrackList);
        }
        catch (Exception ex)
        {
            _ = FormDialogView.ShowAsync("Track Load Error", ex.Message, DialogSeverity.Error, _owner);
        }
        finally
        {
            _trk.gArr = originalTrackList;
        }
    }
    #endregion

    #region Track Management
    /// <summary>[XPLAT] Port of <c>BuildTrackSelectorUI</c>: one CheckBox per track in the ItemsControl, all selected.</summary>
    private void BuildTrackSelectorUI()
    {
        flpTrackList.Items.Clear();
        _selectedTracks.Clear();

        foreach (var trk in _trackList)
        {
            var chk = CreateTrackCheckbox(trk);
            _selectedTracks.Add(trk);
            flpTrackList.Items.Add(chk);
        }
    }

    /// <summary>
    /// [XPLAT] Port of <c>CreateTrackCheckbox</c>. The IsChecked state is set BEFORE the handler is
    /// attached so the initial checked state does not fire <see cref="OnTrackSelectionChanged"/> (parity
    /// with the WinForms initializer-then-subscribe order). <c>IsCheckedChanged</c> (not the obsolete
    /// Checked/Unchecked) fires on both transitions, reproducing the WinForms <c>CheckedChanged</c>.
    /// </summary>
    private CheckBox CreateTrackCheckbox(CTrk track)
    {
        var chk = new CheckBox
        {
            Content = $"{track.name} ({track.mode})",
            IsChecked = true,
            Tag = track,
            FontSize = 16,
            MinHeight = 40,
            Padding = new Thickness(8, 8, 0, 0),
            Background = Brushes.Green,
            Foreground = Brushes.Black
        };

        chk.IsCheckedChanged += (s, e) => OnTrackSelectionChanged(s as CheckBox);
        return chk;
    }

    /// <summary>
    /// [XPLAT] Port of <c>OnTrackSelectionChanged</c>. The WinForms 200 ms drag-debounce revert block is
    /// intentionally dropped (the manual drag-scroll it guarded is replaced by the native ScrollViewer).
    /// </summary>
    private void OnTrackSelectionChanged(CheckBox checkbox)
    {
        if (checkbox?.Tag is CTrk track)
        {
            if (checkbox.IsChecked == true)
            {
                if (!_selectedTracks.Contains(track))
                    _selectedTracks.Add(track);
            }
            else
            {
                _selectedTracks.Remove(track);
                if (track == _activeTrack)
                    _activeTrack = _selectedTracks.FirstOrDefault();
            }

            _builder.SetTracks(_selectedTracks);
            _builder.BuildSegments();
            UpdateTrackListHighlighting();
            RequestRedraw();
        }
    }

    /// <summary>[XPLAT] Port of <c>UpdateTrackListHighlighting</c>: deselected = OrangeRed/LightGray; active = Gold/Black; selected = Green/Black.</summary>
    private void UpdateTrackListHighlighting()
    {
        foreach (var item in flpTrackList.Items)
        {
            if (item is CheckBox cb && cb.Tag is CTrk trk)
            {
                if (!_selectedTracks.Contains(trk))
                {
                    cb.Background = Brushes.OrangeRed;
                    cb.Foreground = Brushes.LightGray;
                }
                else if (trk == _activeTrack)
                {
                    cb.Background = Brushes.Gold;
                    cb.Foreground = Brushes.Black;
                }
                else
                {
                    cb.Background = Brushes.Green;
                    cb.Foreground = Brushes.Black;
                }
            }
        }
    }

    /// <summary>[XPLAT] Port of <c>RefreshTrackSelection</c>: rebuild <c>_selectedTracks</c> from the currently-checked boxes.</summary>
    private void RefreshTrackSelection()
    {
        _showTrimmedOnly = false;
        _selectedTracks.Clear();

        foreach (var item in flpTrackList.Items)
        {
            if (item is CheckBox cb && cb.IsChecked == true && cb.Tag is CTrk trk)
            {
                _selectedTracks.Add(trk);
            }
        }
    }
    #endregion

    #region Boundary Building (builder lifecycle)
    /// <summary>[XPLAT] Port of <c>InitializeBuilder</c>.</summary>
    private void InitializeBuilder()
    {
        _builder = new BoundaryBuilder();
        _builder.SetTracks(_selectedTracks);
    }

    /// <summary>[XPLAT] Port of <c>InitializeBuilderAndRebuild</c>: re-seed tracks, rebuild segments, find + trim intersections, redraw.</summary>
    private void InitializeBuilderAndRebuild()
    {
        _builder.SetTracks(_selectedTracks);
        _builder.BuildSegments();
        _builder.FindIntersections();
        _builder.TrimSegmentsToIntersections();

        RequestRedraw();
    }
    #endregion

    #region Track Manipulation
    /// <summary>[XPLAT] Port of <c>ShiftSelectedTrackPoint</c>: extend/shrink the active track's A or B end, refit + rebuild.</summary>
    private void ShiftSelectedTrackPoint(bool isPointA, double deltaMeters)
    {
        var trk = _activeTrack;
        if (trk == null) return;

        _showTrimmedOnly = false;

        try
        {
            if (trk.mode == TrackMode.AB)
            {
                AdjustABTrackPoints(trk, isPointA, deltaMeters);
            }
            else if (trk.mode == TrackMode.Curve && trk.curvePts.Count >= 2)
            {
                AdjustCurveTrackPoints(trk, isPointA, deltaMeters);
            }

            UpdateViewBounds();
            InitializeBuilderAndRebuild();
        }
        catch (Exception ex)
        {
            Log.EventWriter($"Track adjustment failed: {ex}");
            _ = FormDialogView.ShowAsync("Error", "Track adjustment failed: " + ex.Message, DialogSeverity.Error, _owner);
        }
    }

    /// <summary>[XPLAT] Port of <c>AdjustABTrackPoints</c>.</summary>
    private void AdjustABTrackPoints(CTrk track, bool isPointA, double deltaMeters)
    {
        vec2 direction = (track.ptB - track.ptA).Normalize();

        if (isPointA)
        {
            track.ptA += direction * deltaMeters;
        }
        else
        {
            track.ptB -= direction * deltaMeters;
        }
    }

    /// <summary>[XPLAT] Port of <c>AdjustCurveTrackPoints</c>.</summary>
    private void AdjustCurveTrackPoints(CTrk track, bool isStartPoint, double deltaMeters)
    {
        int steps = (int)Math.Abs(deltaMeters);
        bool isExtend = deltaMeters < 0;

        if (isStartPoint)
        {
            AdjustCurveStartPoint(track, steps, isExtend);
        }
        else
        {
            AdjustCurveEndPoint(track, steps, isExtend);
        }
    }

    /// <summary>[XPLAT] Port of <c>AdjustCurveStartPoint</c>.</summary>
    private void AdjustCurveStartPoint(CTrk track, int steps, bool isExtend)
    {
        for (int s = 0; s < steps && (isExtend || track.curvePts.Count > 2); s++)
        {
            var pt0 = track.curvePts[0];
            var pt1 = track.curvePts[1];
            vec2 dir = (new vec2(pt0.easting, pt0.northing) - new vec2(pt1.easting, pt1.northing)).Normalize();

            if (isExtend)
            {
                track.curvePts.Insert(0, new vec3(
                    pt0.easting + dir.easting,
                    pt0.northing + dir.northing,
                    pt0.heading));
            }
            else
            {
                track.curvePts.RemoveAt(0);
            }
        }
    }

    /// <summary>[XPLAT] Port of <c>AdjustCurveEndPoint</c>.</summary>
    private void AdjustCurveEndPoint(CTrk track, int steps, bool isExtend)
    {
        for (int s = 0; s < steps && (isExtend || track.curvePts.Count > 2); s++)
        {
            int last = track.curvePts.Count - 1;
            var ptN = track.curvePts[last];
            var ptN1 = track.curvePts[last - 1];
            vec2 dir = (new vec2(ptN.easting, ptN.northing) - new vec2(ptN1.easting, ptN1.northing)).Normalize();

            if (isExtend)
            {
                track.curvePts.Add(new vec3(
                    ptN.easting + dir.easting,
                    ptN.northing + dir.northing,
                    ptN.heading));
            }
            else
            {
                track.curvePts.RemoveAt(last);
            }
        }
    }
    #endregion

    #region View Management
    /// <summary>[XPLAT] Port of <c>UpdateViewBounds</c>, additionally pushing the bounds to the GL host (see <see cref="ApplyBoundsToViewport"/>).</summary>
    private void UpdateViewBounds()
    {
        CalculateViewBounds();
        ApplyViewMargins();
        ApplyBoundsToViewport();
    }

    /// <summary>[XPLAT] Port of <c>CalculateViewBounds</c>.</summary>
    private void CalculateViewBounds()
    {
        _viewLeft = double.PositiveInfinity;
        _viewRight = double.NegativeInfinity;
        _viewTop = double.NegativeInfinity;
        _viewBottom = double.PositiveInfinity;

        foreach (var trk in _selectedTracks)
        {
            if (trk.mode == TrackMode.AB)
            {
                UpdateMinMax(trk.ptA);
                UpdateMinMax(trk.ptB);
            }
            else if (trk.mode == TrackMode.Curve)
            {
                foreach (var pt in trk.curvePts)
                    UpdateMinMax(new vec2(pt.easting, pt.northing));
            }
        }

        if (double.IsInfinity(_viewLeft) || double.IsInfinity(_viewRight) ||
            double.IsInfinity(_viewTop) || double.IsInfinity(_viewBottom))
        {
            SetDefaultViewBounds();
        }
    }

    /// <summary>[XPLAT] Port of <c>UpdateMinMax</c>.</summary>
    private void UpdateMinMax(vec2 pt)
    {
        _viewLeft = Math.Min(_viewLeft, pt.easting);
        _viewRight = Math.Max(_viewRight, pt.easting);
        _viewBottom = Math.Min(_viewBottom, pt.northing);
        _viewTop = Math.Max(_viewTop, pt.northing);
    }

    /// <summary>[XPLAT] Port of <c>SetDefaultViewBounds</c>.</summary>
    private void SetDefaultViewBounds()
    {
        _viewLeft = -DEFAULT_VIEW_SIZE;
        _viewRight = DEFAULT_VIEW_SIZE;
        _viewTop = DEFAULT_VIEW_SIZE;
        _viewBottom = -DEFAULT_VIEW_SIZE;
    }

    /// <summary>[XPLAT] Port of <c>ApplyViewMargins</c>.</summary>
    private void ApplyViewMargins()
    {
        double margin = Math.Max(10, (_viewRight - _viewLeft) * VIEW_MARGIN_FACTOR);
        _viewLeft -= margin;
        _viewRight += margin;
        _viewTop += margin;
        _viewBottom -= margin;
    }

    /// <summary>
    /// [XPLAT] Builds the GL host's bounding box from the computed track bounds — the cross-platform
    /// replacement for the WinForms <c>GL.Ortho(_viewLeft, _viewRight, _viewBottom, _viewTop, -1, 1)</c>.
    /// Note the <see cref="GeoCoord"/> constructor is northing-first.
    /// </summary>
    private GeoBoundingBox BuildBoundingBox()
    {
        return new GeoBoundingBox(
            new GeoCoord(_viewBottom, _viewLeft),
            new GeoCoord(_viewTop, _viewRight));
    }

    /// <summary>
    /// [XPLAT] Pushes the recomputed bounds to the GL host — the stand-in for the WinForms per-paint
    /// <c>SetupViewport</c>/<c>GL.Ortho</c>. <c>ResetZoomPan</c> re-fits and recentres on the new bounds,
    /// matching the source's fixed fit-to-tracks ortho (the preview had no pan/zoom interaction).
    /// Null-guarded so the constructor's initial <see cref="UpdateViewBounds"/> (before the viewport
    /// exists) is a safe no-op.
    /// </summary>
    private void ApplyBoundsToViewport()
    {
        if (_viewport == null) return;
        _viewport.SetBoundingBox(BuildBoundingBox());
        _viewport.ResetZoomPan();
    }

    /// <summary>
    /// [XPLAT] The WinForms <c>BeginInvoke(() =&gt; glControlPreview.Invalidate())</c> debounce becomes a
    /// direct <c>InvalidateVisual</c> on the GL host control; the host also self-schedules continuous
    /// frames (RequestNextFrameRendering) so the preview reflects state changes on the next frame.
    /// </summary>
    private void RequestRedraw()
    {
        _viewport?.View?.InvalidateVisual();
    }
    #endregion

    #region OpenGL Rendering ([XPLAT] immediate-mode GL re-expressed via the Core GLW DrawLib)
    /// <summary>
    /// [XPLAT] Supplied to <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport.RenderAction"/>; the host
    /// wraps this in <c>BeginPaint()</c>/<c>EndPaint()</c> (make-current + clear + bounding-box projection
    /// + buffer swap), so this method issues ONLY <c>GLW</c> geo-coordinate draw primitives. The
    /// immediate-mode <c>GL.Clear</c>/<c>SetupViewport</c>/<c>SwapBuffers</c> from the WinForms
    /// <c>RenderScene</c> are owned by the host and intentionally not ported here. Draw order matches the
    /// source exactly.
    /// </summary>
    private void RenderScene()
    {
        if (!_showTrimmedOnly)
        {
            DrawRawSegments();
            DrawTracks();
        }

        DrawBoundary();
        DrawFinalBoundary();
    }

    /// <summary>[XPLAT] Port of <c>DrawTracks</c>: active track = Yellow @ 5px, others = Gray @ 1px.</summary>
    private void DrawTracks()
    {
        for (int i = 0; i < _selectedTracks.Count; i++)
        {
            var trk = _selectedTracks[i];
            bool isSelected = (trk == _activeTrack);
            ColorRgba color = isSelected ? ColorSelectedTrack : ColorUnselectedTrack;
            float width = isSelected ? SELECTED_TRACK_WIDTH : NORMAL_TRACK_WIDTH;

            if (trk.mode == TrackMode.AB)
            {
                DrawLine(trk.ptA, trk.ptB, color, width);
            }
            else if (trk.mode == TrackMode.Curve)
            {
                DrawPolyline(trk.curvePts.Select(p => new vec2(p.easting, p.northing)).ToList(), color, width);
            }
        }
    }

    /// <summary>[XPLAT] Port of <c>DrawBoundary</c>: trimmed segments + intersection markers.</summary>
    private void DrawBoundary()
    {
        if (_builder == null) return;

        DrawTrimmedSegments();
        DrawIntersectionPoints();
    }

    /// <summary>[XPLAT] Port of <c>DrawTrimmedSegments</c>: Green line pairs (GL.Lines) over the trimmed segment list.</summary>
    private void DrawTrimmedSegments()
    {
        GLW.SetLineWidth(2.5f);
        GLW.SetColor(ColorTrimmed);

        var verts = new List<GeoCoord>(_builder.TrimmedSegments.Count * 2);
        foreach (var seg in _builder.TrimmedSegments)
        {
            verts.Add(seg.Start.ToGeoCoord());
            verts.Add(seg.End.ToGeoCoord());
        }
        GLW.DrawLinesPrimitive(verts.ToArray());
    }

    /// <summary>[XPLAT] Port of <c>DrawIntersectionPoints</c>: a small Red ring per intersection.</summary>
    private void DrawIntersectionPoints()
    {
        foreach (var pt in _builder.IntersectionPoints)
        {
            DrawCircle(pt, ColorIntersection, INTERSECTION_POINT_SIZE);
        }
    }

    /// <summary>[XPLAT] Port of <c>DrawFinalBoundary</c>: Red closed LineLoop over the final boundary points.</summary>
    private void DrawFinalBoundary()
    {
        if (_builder?.FinalBoundary == null || _builder.FinalBoundary.Count < 2)
            return;

        GLW.SetLineWidth(BOUNDARY_LINE_WIDTH);
        GLW.SetColor(ColorFinalBoundary);
        GLW.DrawLineLoopPrimitive(GeoRefactorHelper.ToGeoCoordArray(_builder.FinalBoundary));
    }

    /// <summary>[XPLAT] Port of <c>DrawLine</c>: a single coloured segment (GL.Lines) via GLW.</summary>
    private void DrawLine(vec2 ptA, vec2 ptB, ColorRgba color, float width = 1.0f)
    {
        GLW.SetLineWidth(width);
        GLW.SetColor(color);
        GLW.DrawLinesPrimitive(new[] { ptA.ToGeoCoord(), ptB.ToGeoCoord() });
    }

    /// <summary>[XPLAT] Port of <c>DrawPolyline</c>: a coloured LineStrip via GLW.</summary>
    private void DrawPolyline(List<vec2> points, ColorRgba color, float width = 1.0f)
    {
        if (points.Count < 2) return;

        GLW.SetLineWidth(width);
        GLW.SetColor(color);
        GLW.DrawLineStripPrimitive(GeoRefactorHelper.ToGeoCoordArray(points));
    }

    /// <summary>
    /// [XPLAT] Port of <c>DrawCircle</c>: the immediate-mode 16-segment LineLoop ring re-expressed as a
    /// computed <see cref="GeoCoord"/> ring drawn via <c>GLW.DrawLineLoopPrimitive</c> — preserving the
    /// source's small circular intersection marker (GeoCoord is northing-first; x = easting + cos, y =
    /// northing + sin, matching the WinForms <c>GL.Vertex2(easting + cos, northing + sin)</c>).
    /// </summary>
    private void DrawCircle(vec2 center, ColorRgba color, float radius)
    {
        GLW.SetColor(color);

        var ring = new GeoCoord[CIRCLE_SEGMENTS];
        for (int i = 0; i < CIRCLE_SEGMENTS; i++)
        {
            double angle = i * Math.PI * 2.0 / CIRCLE_SEGMENTS;
            ring[i] = new GeoCoord(
                center.northing + Math.Sin(angle) * radius,
                center.easting + Math.Cos(angle) * radius);
        }
        GLW.DrawLineLoopPrimitive(ring);
    }

    /// <summary>[XPLAT] Port of <c>DrawRawSegments</c>: Gray line pairs (GL.Lines) over the raw segment list (skipped once trimmed-only).</summary>
    private void DrawRawSegments()
    {
        if (_builder?.Segments == null || _showTrimmedOnly) return;

        GLW.SetLineWidth(1.5f);
        GLW.SetColor(ColorRawSegment);

        var verts = new List<GeoCoord>(_builder.Segments.Count * 2);
        foreach (var seg in _builder.Segments)
        {
            verts.Add(seg.Start.ToGeoCoord());
            verts.Add(seg.End.ToGeoCoord());
        }
        GLW.DrawLinesPrimitive(verts.ToArray());
    }
    #endregion

    #region Event Handlers
    /// <summary>[XPLAT] Port of <c>btnResetPreview_Click</c>: reload tracks and rebuild the whole preview.</summary>
    private void btnResetPreview_Click(object sender, RoutedEventArgs e)
    {
        InitializeForm();
        _trackList.Clear();
        LoadTracks();
        BuildTrackSelectorUI();
        RefreshTrackSelection();
        InitializeBuilder();
        UpdateViewBounds();
        UpdateTrackListHighlighting();
        RequestRedraw();
    }

    /// <summary>[XPLAT] Port of <c>btnSelectPrevious_Click</c>: move the active track one step back (wrapping).</summary>
    private void btnSelectPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTracks.Count == 0 || _activeTrack == null) return;

        int idx = _selectedTracks.IndexOf(_activeTrack);
        idx = (idx - 1 + _selectedTracks.Count) % _selectedTracks.Count;
        _activeTrack = _selectedTracks[idx];

        RequestRedraw();
        UpdateTrackListHighlighting();
    }

    /// <summary>[XPLAT] Port of <c>btnSelectNext_Click</c>: move the active track one step forward (wrapping).</summary>
    private void btnSelectNext_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTracks.Count == 0 || _activeTrack == null) return;

        int idx = _selectedTracks.IndexOf(_activeTrack);
        idx = (idx + 1) % _selectedTracks.Count;
        _activeTrack = _selectedTracks[idx];

        RequestRedraw();
        UpdateTrackListHighlighting();
    }

    // [XPLAT] Ports of the four end-adjust handlers (verbatim deltas): Extend B/A use a negative delta
    // (grow), Shrink A/B use a positive delta (shrink), exactly as the WinForms one-liners.
    private void btnExtendForward_Click(object sender, RoutedEventArgs e) => ShiftSelectedTrackPoint(false, -10);
    private void btnExtendBackward_Click(object sender, RoutedEventArgs e) => ShiftSelectedTrackPoint(true, -10);
    private void btnShrinkA_Click(object sender, RoutedEventArgs e) => ShiftSelectedTrackPoint(true, +10);
    private void btnShrinkB_Click(object sender, RoutedEventArgs e) => ShiftSelectedTrackPoint(false, +10);

    /// <summary>[XPLAT] Port of <c>btnBuildBoundary_Click</c>: enable Save then build (BuildBoundary may re-disable on failure).</summary>
    private void btnBuildBoundary_Click(object sender, RoutedEventArgs e)
    {
        btnSave.IsEnabled = true;
        BuildBoundary();
    }

    /// <summary>[XPLAT] Port of <c>BuildBoundary</c>: rebuild, trim, finalise, push to the main app boundary, and report.</summary>
    private void BuildBoundary()
    {
        InitializeBuilderAndRebuild();

        try
        {
            var result = _builder.BuildTrimmedBoundary();
            var finalized = _builder.FinalizedBoundary;

            if (result.Count < 3 || finalized == null)
            {
                _ = FormDialogView.ShowAsync("Error", "No valid boundary could be generated", DialogSeverity.Error, _owner);
                btnSave.IsEnabled = false;
                return;
            }

            _showTrimmedOnly = true;
            UpdateMainApplicationBoundary(finalized);
            RequestRedraw();

            _ = FormDialogView.ShowAsync("Succes!", $"Boundary built with {result.Count} points", DialogSeverity.Info, _owner);
            btnSave.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.EventWriter($"Boundary build failed: {ex}");
            _ = FormDialogView.ShowAsync("Error", "Build failed" + ex.ToString(), DialogSeverity.Error, _owner);
        }
    }

    /// <summary>[XPLAT] Port of <c>UpdateMainApplicationBoundary</c>: replace the field's boundary set and rebuild turn lines (was via <c>mf.bnd</c>).</summary>
    private void UpdateMainApplicationBoundary(CBoundaryList boundary)
    {
        _bnd.bndList.Clear();
        _bnd.bndList.Add(boundary);
        _bnd.BuildTurnLines();
    }

    /// <summary>[XPLAT] Port of <c>btnSave_Click</c>: save then close.</summary>
    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveBoundary();
        Close();
    }

    /// <summary>[XPLAT] Port of <c>btnAutofind_click</c>: extend all tracks, rebuild, and prompt the operator.</summary>
    private void btnAutofind_click(object sender, RoutedEventArgs e)
    {
        _builder.ExtendAllTracks(50.0);
        InitializeBuilderAndRebuild();
        _ = FormDialogView.ShowAsync("Finding Intersections...", "All green? Press Build or manually correct!", DialogSeverity.Info, _owner);
    }

    /// <summary>
    /// [XPLAT] Port of <c>SaveBoundary</c>. Behaviour-frozen: the source calls
    /// <see cref="ValidateSaveConditions"/> for its side-effect dialogs but DISCARDS the result and saves
    /// regardless, so the result is explicitly discarded here (<c>_ =</c>) to preserve that exact flow.
    /// </summary>
    private void SaveBoundary()
    {
        try
        {
            _ = ValidateSaveConditions();
            _builder.SaveToBoundaryFile(_getCurrentFieldDirectory());
        }
        catch (Exception ex)
        {
            Log.EventWriter($"Save failed: {ex}");
            _ = FormDialogView.ShowAsync("Error", ex.Message, DialogSeverity.Error, _owner);
        }
    }

    /// <summary>[XPLAT] Port of <c>ValidateSaveConditions</c>: a field must be selected and a >=3-point finalized boundary must exist.</summary>
    private bool ValidateSaveConditions()
    {
        if (string.IsNullOrEmpty(_getCurrentFieldDirectory()))
        {
            _ = FormDialogView.ShowAsync("Save Error", "Please select a field before saving.", DialogSeverity.Error, _owner);
            return false;
        }

        if (_builder?.FinalizedBoundary == null || _builder.FinalizedBoundary.fenceLine.Count < 3)
        {
            _ = FormDialogView.ShowAsync("Save Error", "No valid boundary to save", DialogSeverity.Error, _owner);
            return false;
        }

        return true;
    }

    /// <summary>[XPLAT] Port of <c>btnClose_Click</c>: close without saving.</summary>
    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    #endregion
}

