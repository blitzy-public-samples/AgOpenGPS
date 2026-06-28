// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.Visuals;
using AgOpenGPS.Helpers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DrawColors = AgOpenGPS.Core.Drawing.Colors;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create Boundary From Mapping" boundary tool — a 1:1 behavioural-parity
    /// port of the WinForms <c>FormBndTool</c> (FormBndTool.cs + FormBndTool.Designer.cs), the largest and
    /// only GL-hosting Field dialog. The operator reduces the recorded section/coverage points to a spacing,
    /// runs a "PacMan" nearest-neighbour walk that traces the outer hull, smooths it, then commits it as a
    /// field boundary; the dialog can also slice an existing boundary between two picked fence-line points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The WinForms shell reached the running program through a <c>FormGPS mf</c> back-reference. That
    /// god-object coupling is replaced by constructor injection: the boundary model (<see cref="CBoundary"/>),
    /// the recorded coverage patches (<c>triStrip</c>), the field-data seam (<see cref="IBoundaryFieldData"/>,
    /// the <c>mf.fd</c>/<c>mf</c> role), the persistence delegates (<c>FileSaveBoundary</c>/
    /// <c>FileSaveHeadland</c>) and a message-box delegate (<c>mf.YesMessageBox</c>) are all supplied by the
    /// composition root. Cross-platform settings come straight from <see cref="AgOpenGPS.Properties.Settings"/>.
    /// </para>
    /// <para>
    /// GL hosting: the WinForms <c>oglSelf</c> (OpenTK.GLControl) surface and its
    /// <c>GeoViewport _viewport = new GeoViewport(mf.FieldBoundingBox, oglSelf)</c> become an
    /// <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport"/> built from the injected bounding box and inserted
    /// into the XAML <c>oglHost</c> Border. The geometry math (KNN/Spacing/PacMan/Smooth/BuildBnd/Reset/
    /// DeleteBoundary/EndStep and the step-mode state machine) and every <c>GLW</c> DrawLib draw call are
    /// preserved verbatim per AAP §0.2.2. The host owns the GL context and the BeginPaint/EndPaint wrap; the
    /// former <c>oglSelf_Paint</c> body is supplied to the host as its <see cref="AgOpenGPS.Controls.AvaloniaGeoViewport.RenderAction"/>.
    /// </para>
    /// </remarks>
    public partial class FormBndToolView : Window
    {
        // ===================================================================================================
        //  Injected collaborators (replace the WinForms FormGPS mf back-reference).
        // ===================================================================================================

        // [XPLAT] was mf.bnd — the boundary model (bndList, each CBoundaryList.fenceLine; BuildTurnLines()).
        private readonly CBoundary _bnd;

        // [XPLAT] was mf.triStrip — recorded coverage patches the reset seeds the section point cloud from.
        private readonly List<CPatches> _triStrip;

        // [XPLAT] was mf.fd / mf — UpdateFieldBoundaryGUIAreas() + CalculateMinMax() after the boundary set changes.
        private readonly IBoundaryFieldData _fieldData;

        // [XPLAT] was mf.FileSaveBoundary() — persist the boundary set.
        private readonly Action _fileSaveBoundary;

        // [XPLAT] was mf.FileSaveHeadland() — persist the headland set (used when the boundary set is cleared).
        private readonly Action _fileSaveHeadland;

        // [XPLAT] was mf.YesMessageBox(string) — surface a one-line advisory (e.g. "not enough points").
        private readonly Action<string> _showMessage;

        // [XPLAT] was "GeoViewport _viewport = new GeoViewport(mf.FieldBoundingBox, oglSelf)". The Avalonia
        // adapter derives from the same Core GeoViewportBase, so the inherited camera/zoom/pan helpers and
        // GetGeoCoord are used exactly as before; only the host control changes.
        private readonly AgOpenGPS.Controls.AvaloniaGeoViewport _viewport;

        // ===================================================================================================
        //  Renderer colours — preserved EXACTLY (AAP file spec).
        // ===================================================================================================
        private static readonly ColorRgba boundaryColor = new ColorRgba(0.725f, 0.95f, 0.950f);

        private static readonly ColorRgba newBoundaryStripColor = new ColorRgba(0.90f, 0.25f, 0.10f);
        private static readonly ColorRgba newBoundaryPointsColor = new ColorRgba(0.90f, 0.25f, 0.910f);
        private static readonly ColorRgba newBoundaryLoopColor = new ColorRgba(0.82f, 0.835f, 0.5f);

        private static readonly ColorRgba stepSectionColor = new ColorRgba(0.64f, 0.64f, 0.6f);

        // ===================================================================================================
        //  Algorithm state — ported verbatim from FormBndTool.cs.
        // ===================================================================================================
        private vec3 ptA = new vec3();
        private vec3 ptB = new vec3();
        public vec3 pint = new vec3(0.0, 1.0, 0.0);

        private bool isA = true;
        private bool isC = false;
        private int start = 99999, end = 99999;
        private int bndSelect = 0, smPtsChoose = 1, smPts = 4;

        public List<vec3> secList = new List<vec3>();
        public List<vec3> bndList = new List<vec3>();
        public List<vec3> smooList = new List<vec3>();

        private double minDistSq = 1, minDistDisp = 1;

        private bool isStep = false;

        private double mdA = double.MaxValue;
        private double mdB = double.MaxValue;
        private double mdC = double.MaxValue;
        private double mdD = double.MaxValue;
        private double mdE = double.MaxValue;
        private double mdF = double.MaxValue;
        private double mdG = double.MaxValue;
        private int rA, rB, rC, rD, rE, rF, rG;

        private int firstPoint, currentPoint;

        //find 3 closest points
        private vec3[] arr;

        //baseline to calc the most right vector - starts at 270 deg.
        private double prevHeading = Math.PI + glm.PIBy2;

        // [XPLAT] WinForms System.Windows.Forms.Timer -> Avalonia DispatcherTimer. The WinForms code used
        // timer1.Interval as a state flag (the "== 50" guards mean "a step walk is in progress"); a
        // DispatcherTimer exposes Interval only as a TimeSpan, so the millisecond value is mirrored here and
        // both are kept in lock-step through SetTimerInterval().
        private readonly DispatcherTimer timer1;
        private int timerIntervalMs = 500;

        /// <summary>
        /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time
        /// previewer only. It renders the static markup, wires no behaviour and leaves the injected model
        /// null, so the <see cref="OnLoaded"/>/<see cref="OnClosing"/> guards treat a preview instance as a
        /// no-op. The running application always uses the injected overload.
        /// </summary>
        public FormBndToolView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the boundary tool bound to the supplied collaborators.
        /// </summary>
        /// <param name="bnd">The boundary model (was <c>mf.bnd</c>).</param>
        /// <param name="fieldBoundingBox">The field extents used to build the viewport (was <c>mf.FieldBoundingBox</c>).</param>
        /// <param name="triStrip">Recorded coverage patches the reset reads (was <c>mf.triStrip</c>).</param>
        /// <param name="fieldData">Field-data seam exposing <c>UpdateFieldBoundaryGUIAreas</c> + <c>CalculateMinMax</c> (the <c>mf.fd</c>/<c>mf</c> role).</param>
        /// <param name="fileSaveBoundary">Persists the boundary set (was <c>mf.FileSaveBoundary()</c>).</param>
        /// <param name="fileSaveHeadland">Persists the headland set (was <c>mf.FileSaveHeadland()</c>).</param>
        /// <param name="showMessage">Surfaces a one-line advisory (was <c>mf.YesMessageBox(string)</c>).</param>
        /// <exception cref="ArgumentNullException">Thrown when any reference collaborator is null.</exception>
        public FormBndToolView(
            CBoundary bnd,
            GeoBoundingBox fieldBoundingBox,
            List<CPatches> triStrip,
            IBoundaryFieldData fieldData,
            Action fileSaveBoundary,
            Action fileSaveHeadland,
            Action<string> showMessage)
            : this()
        {
            // [XPLAT] The DI seam replaces "mf = callingForm as FormGPS;" — fail fast on a missing wiring
            // rather than NRE deep inside a handler.
            _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
            _triStrip = triStrip ?? throw new ArgumentNullException(nameof(triStrip));
            _fieldData = fieldData ?? throw new ArgumentNullException(nameof(fieldData));
            _fileSaveBoundary = fileSaveBoundary ?? throw new ArgumentNullException(nameof(fileSaveBoundary));
            _fileSaveHeadland = fileSaveHeadland ?? throw new ArgumentNullException(nameof(fileSaveHeadland));
            _showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));

            // [XPLAT] Build the viewport from the injected bounding box (was new GeoViewport(mf.FieldBoundingBox,
            // oglSelf)). The AvaloniaGeoViewport self-manages its GL context and resize, so there is no
            // CreateViewport()/oglSelf_Resize() equivalent here.
            _viewport = new AgOpenGPS.Controls.AvaloniaGeoViewport(fieldBoundingBox);

            // [XPLAT] The former oglSelf_Paint body is supplied to the host as its render callback. The host
            // invokes RenderAction between BeginPaint()/EndPaint(); the view never touches the GL context.
            _viewport.RenderAction = RenderSelf;

            // [XPLAT] Host the viewport's exposed Control in the XAML 'oglHost' Border (was the oglSelf surface).
            oglHost.Child = _viewport.View;

            // [XPLAT] WinForms Timer (Interval 500, Enabled at design time) -> DispatcherTimer started on load.
            timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timerIntervalMs) };
            timer1.Tick += timer1_Tick;

            // Wire the controls imperatively (the .axaml declares no Click=/event attributes). Done in the
            // injected ctor only, so a loader/previewer instance stays inert.
            cboxPointDistance.SelectionChanged += cboxPointDistance_SelectedIndexChanged;
            cboxSmooth.SelectionChanged += cboxSmooth_SelectedIndexChanged;

            btnResetReduce.Click += btnResetReduce_Click;
            btnAddPoints.Click += btnAddPoints_Click;
            btnStartStop.Click += btnStartStop_Click;
            btnAddBoundary.Click += btnAddBoundary_Click;
            btnCancelTouch.Click += btnCancelTouch_Click;
            btnSlice.Click += btnSlice_Click;
            btnCenterOGL.Click += btnCenterOGL_Click;
            btnMoveDn.Click += btnMoveDn_Click;
            btnMoveUp.Click += btnMoveUp_Click;
            btnMoveLeft.Click += btnMoveLeft_Click;
            btnMoveRight.Click += btnMoveRight_Click;
            btnZoomIn.Click += btnZoomIn_Click;
            btnZoomOut.Click += btnZoomOut_Click;
            btnExit.Click += btnExit_Click;

            // [XPLAT] WinForms oglSelf.MouseDown -> PointerPressed on the hosted GL control (only MouseDown
            // was wired in the designer; no Move/Up/Wheel).
            _viewport.View.PointerPressed += oglSelf_MouseDown;
        }

        // ===================================================================================================
        //  Lifecycle (was FormBndTool_Load / FormBndTool_FormClosing).
        // ===================================================================================================

        /// <inheritdoc />
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            // A loader/previewer instance has no model — stay completely inert.
            if (_bnd == null) return;

            panel1.IsVisible = false;

            // [XPLAT] WinForms detach/attach pattern preserved (SelectedIndexChanged -> SelectionChanged) so
            // restoring the persisted index does not fire the save handler.
            cboxPointDistance.SelectionChanged -= cboxPointDistance_SelectedIndexChanged;
            cboxPointDistance.SelectedIndex = AgOpenGPS.Properties.Settings.Default.bndToolSpacing;
            cboxPointDistance.SelectionChanged += cboxPointDistance_SelectedIndexChanged;

            cboxSmooth.SelectionChanged -= cboxSmooth_SelectedIndexChanged;
            cboxSmooth.SelectedIndex = AgOpenGPS.Properties.Settings.Default.bndToolSmooth;
            cboxSmooth.SelectionChanged += cboxSmooth_SelectedIndexChanged;

            cboxIsZoom.IsChecked = false;

            // [XPLAT] Apply the persisted window size (was Size = Settings.setWindow_MapBndSize). The WinForms
            // manual work-area centering is replaced by WindowStartupLocation="CenterOwner" in the .axaml.
            System.Drawing.Size savedSize = AgOpenGPS.Properties.Settings.Default.setWindow_MapBndSize;
            if (savedSize.Width > 0 && savedSize.Height > 0)
            {
                Width = savedSize.Width;
                Height = savedSize.Height;
            }

            //translate
            labelCreate.Text = gStr.gsCreate;
            labelSmooth.Text = gStr.gsSmooth;
            labelPleaseWait.Text = gStr.gsPleaseWait + "...";
            labelReset.Text = "Reset";
            labelSpacing.Text = $"{gStr.gsSpacing} ({gStr.gsCm})";
            labelPoints.Text = gStr.gsPoints;
            labelPointsToProcess.Text = gStr.gsPointsToProcess;

            //load sections if bnd exists, load bnd if exists
            if (_bnd.bndList.Count == 0)
            {
                Reset();
            }

            // [XPLAT] WinForms timer1.Enabled = true -> start the DispatcherTimer.
            timer1.Start();
        }

        /// <inheritdoc />
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // [XPLAT] Persist the window size (was FormBndTool_FormClosing: Settings.setWindow_MapBndSize = Size).
            if (_bnd != null)
            {
                double w = Width, h = Height;
                if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
                {
                    AgOpenGPS.Properties.Settings.Default.setWindow_MapBndSize =
                        new System.Drawing.Size((int)w, (int)h);
                    AgOpenGPS.Properties.Settings.Default.Save();
                }
            }

            base.OnClosing(e);
        }

        /// <inheritdoc />
        protected override void OnClosed(EventArgs e)
        {
            // [XPLAT] Stop/dispose the DispatcherTimer and release the render callback so nothing keeps
            // ticking or drawing after the dialog is gone.
            if (timer1 != null)
            {
                timer1.Stop();
                timer1.Tick -= timer1_Tick;
            }

            if (_viewport != null)
            {
                _viewport.RenderAction = null;
            }

            base.OnClosed(e);
        }

        // [XPLAT] Keeps the DispatcherTimer.Interval and the mirrored millisecond value in lock-step so the
        // WinForms "timer1.Interval == 50" state checks remain valid.
        private void SetTimerInterval(int ms)
        {
            timerIntervalMs = ms;
            timer1.Interval = TimeSpan.FromMilliseconds(ms);
        }

        // ===================================================================================================
        //  Nearest-neighbour hull walk (KNN) — ported verbatim.
        // ===================================================================================================
        private void KNN()
        {
            timer1.Stop();
            rA = rB = rC = rD = rE = rF = rG = 0;

            for (int j = 0; j < secList.Count; j++)
            {
                if (j == currentPoint) continue;

                if (arr[j].heading == 1)
                    continue;

                double dist = glm.DistanceSquared(secList[currentPoint], secList[j]);

                if (dist < mdA)
                {
                    mdG = mdF; mdF = mdE; mdE = mdD; mdD = mdC; mdC = mdB; mdB = mdA; mdA = dist; rG = rF; rF = rE; rE = rD; rD = rC; rC = rB; rB = rA; rA = j;
                }
                else if (dist < mdB)
                {
                    rG = rF; rF = rE; rE = rD; rD = rC; rC = rB; rB = j; mdG = mdF; mdF = mdE; mdE = mdD; mdD = mdC; mdC = mdB; mdB = dist;
                }
                else if (dist < mdC)
                {
                    mdG = mdF; mdF = mdE; mdE = mdD; mdD = mdC; mdC = dist; rG = rF; rF = rE; rE = rD; rD = rC; rC = j;
                }
                else if (dist < mdD)
                {
                    mdG = mdF; mdF = mdE; mdE = mdD; mdD = dist; rG = rF; rF = rE; rE = rD; rD = j;
                }
                else if (dist < mdE)
                {
                    mdG = mdF; mdF = mdE; mdE = dist; rG = rF; rF = rE; rE = j;
                }
                else if (dist < mdF)
                {
                    mdG = mdF; mdF = dist; rG = rF; rF = j;
                }
                else if (dist < mdG)
                {
                    mdG = dist; rG = j;
                }
            }

            double aMax = 5;
            double aMin = 1.14;

            double aA = Math.Atan2(secList[rA].easting - secList[currentPoint].easting,
                secList[rA].northing - secList[currentPoint].northing);
            double pA = aA;

            aA -= prevHeading;
            if (aA < 0) aA += glm.twoPI; if (aA < 0) aA += glm.twoPI;
            if (aA > aMax || aA < aMin) aA = 0;

            double aB = Math.Atan2(secList[rB].easting - secList[currentPoint].easting,
                secList[rB].northing - secList[currentPoint].northing);
            double pB = aB;
            aB -= prevHeading;
            if (aB < 0) aB += glm.twoPI; if (aB < 0) aB += glm.twoPI;

            if (aB > aMax || aB < aMin) aB = 0;

            double aC = Math.Atan2(secList[rC].easting - secList[currentPoint].easting,
                secList[rC].northing - secList[currentPoint].northing);
            double pC = aC;

            aC -= prevHeading;
            if (aC < 0) aC += glm.twoPI; if (aC < 0) aC += glm.twoPI;
            if (aC > aMax || aC < aMin) aC = 0;

            double aD = Math.Atan2(secList[rD].easting - secList[currentPoint].easting,
                secList[rD].northing - secList[currentPoint].northing);
            double pD = aD;

            aD -= prevHeading;
            if (aD < 0) aD += glm.twoPI; if (aD < 0) aD += glm.twoPI;
            if (aD > aMax || aD < aMin) aD = 0;

            double aE = Math.Atan2(secList[rE].easting - secList[currentPoint].easting,
                secList[rE].northing - secList[currentPoint].northing);
            double pE = aE;

            aE -= prevHeading;
            if (aE < 0) aE += glm.twoPI; if (aE < 0) aE += glm.twoPI;
            if (aE > aMax || aE < aMin) aE = 0;

            double aF = Math.Atan2(secList[rF].easting - secList[currentPoint].easting,
                secList[rF].northing - secList[currentPoint].northing);
            double pF = aF;

            aF -= prevHeading;
            if (aF < 0) aF += glm.twoPI; if (aF < 0) aF += glm.twoPI;
            if (aF > aMax || aF < aMin) aF = 0;

            double aG = Math.Atan2(secList[rG].easting - secList[currentPoint].easting,
                secList[rG].northing - secList[currentPoint].northing);
            double pG = aG;

            aG -= prevHeading;
            if (aG < 0) aG += glm.twoPI; if (aG < 0) aG += glm.twoPI;
            if (aG > aMax || aG < aMin) aG = 0;

            double maxAngle = Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(Math.Max(aA, aB), aC), aD), aE), aF), aG);

            //remove from list
            arr[currentPoint].heading = 1;

            if (maxAngle == aA) { currentPoint = rA; prevHeading = pA + Math.PI; }
            else if (maxAngle == aB) { currentPoint = rB; prevHeading = pB + Math.PI; }
            else if (maxAngle == aC) { currentPoint = rC; prevHeading = pC + Math.PI; }
            else if (maxAngle == aD) { currentPoint = rD; prevHeading = pD + Math.PI; }
            else if (maxAngle == aE) { currentPoint = rE; prevHeading = pE + Math.PI; }
            else if (maxAngle == aF) { currentPoint = rF; prevHeading = pF + Math.PI; }
            else if (maxAngle == aG) { currentPoint = rG; prevHeading = pG + Math.PI; }

            if (prevHeading >= glm.twoPI) prevHeading -= glm.twoPI;
            if (prevHeading < 0) prevHeading += glm.twoPI;

            mdA = double.MaxValue;
            mdB = double.MaxValue;
            mdC = double.MaxValue;
            mdD = double.MaxValue;
            mdE = double.MaxValue;
            mdF = double.MaxValue;
            mdG = double.MaxValue;

            if (bndList.Count > 7)
            {
                //unhide first point
                arr[firstPoint].heading = 0;

                //are we back to start?
                if (rA == firstPoint || rB == firstPoint || rC == firstPoint ||
                        rD == firstPoint || rE == firstPoint || rF == firstPoint || rG == firstPoint)
                {
                    EndStep();
                    SetTimerInterval(500);
                    timer1.Start();
                    cboxSmooth.IsEnabled = true;

                    int bndCount = bndList.Count;

                    for (int i = 0; i < bndCount; i++)
                    {
                        int j = i + 1;

                        if (j == bndCount) j = 0;
                        double distance = glm.Distance(bndList[i], bndList[j]);
                        if (distance > 1.1)
                        {
                            vec3 pointB = new vec3((bndList[i].easting + bndList[j].easting) / 2.0,
                                (bndList[i].northing + bndList[j].northing) / 2.0, bndList[i].heading);

                            bndList.Insert(j, pointB);
                            bndCount = bndList.Count;
                            i--;
                        }
                    }
                    return;
                }
            }

            bndList.Add(new vec3(secList[currentPoint]));
            timer1.Start();
        }

        private void btnAddPoints_Click(object sender, RoutedEventArgs e)
        {
            if (timerIntervalMs == 50) return;

            double abHead = Math.Atan2(
                ptB.easting - ptA.easting,
                ptB.northing - ptA.northing);
            //if (abHead < 0) abHead += glm.twoPI;
            //ptA.heading = abHead;
            secList.Add(ptA);
            secList.Add(ptB);

            int dist = (int)(glm.Distance(ptA, ptB));

            if (dist > 2)
            {
                for (int i = 1; i < dist; i++)
                {
                    vec3 pt = new vec3(ptA);
                    pt.easting += (Math.Sin(abHead) * i);
                    pt.northing += (Math.Cos(abHead) * i);
                    secList.Add(pt);
                }
            }

            btnAddPoints.IsEnabled = false;

            //update the arrays
            start = 99999; end = 99999;
            btnExit.Focus();
            isC = false;
            isA = true;

            btnAddPoints.IsEnabled = false;
        }

        private void btnResetReduce_Click(object sender, RoutedEventArgs e)
        {
            Reset();
        }

        private void Reset()
        {
            btnSlice.IsVisible = false;

            //start all over
            start = end = 99999;
            _viewport.ResetZoomPan();

            btnStartStop.IsEnabled = true;

            EndStep();
            SetTimerInterval(500);
            prevHeading = Math.PI + glm.PIBy2;

            secList?.Clear();
            bndList?.Clear();
            smooList?.Clear();

            DeleteBoundary();

            //for every new chunk of patch
            for (int j = 0; j < _triStrip.Count; j++)
            {
                //every time the section turns off and on is a new patch
                int patchCount = _triStrip[j].patchList.Count;

                if (patchCount > 0)
                {
                    //for every new chunk of patch
                    foreach (var triList in _triStrip[j].patchList)
                    {
                        for (int i = 1; i < triList.Count; i++)
                        {
                            vec3 bob = new vec3(triList[i].easting, triList[i].northing, 0);

                            secList.Add(bob);
                        }
                    }
                }
            }

            rA = rB = rC = rD = rE = rF = rG = firstPoint = currentPoint = 0;
            bndList?.Clear();

            // [XPLAT] WinForms btnStartStop.BackColor = Color.LightGreen -> Avalonia Background brush.
            btnStartStop.Background = Brushes.LightGreen;

            cboxPointDistance.SelectionChanged -= cboxPointDistance_SelectedIndexChanged;
            cboxPointDistance.SelectedIndex = AgOpenGPS.Properties.Settings.Default.bndToolSpacing;
            cboxPointDistance.SelectionChanged += cboxPointDistance_SelectedIndexChanged;

            cboxSmooth.SelectionChanged -= cboxSmooth_SelectedIndexChanged;
            cboxSmooth.SelectedIndex = AgOpenGPS.Properties.Settings.Default.bndToolSmooth;
            cboxSmooth.SelectionChanged += cboxSmooth_SelectedIndexChanged;
        }

        // [XPLAT] WinForms Spacing() ran a synchronous reduction and used panel1.Refresh() to repaint the
        // "Please Wait" overlay every 200 points. Avalonia paints on the next frame after the UI thread
        // yields, so the method is async and replaces panel1.Refresh() with await Task.Yield() — the overlay
        // and its live counts update exactly as before, without Application.DoEvents and without blocking.
        // The numeric reduction is otherwise verbatim.
        private async Task SpacingAsync()
        {
            if (cboxPointDistance.SelectedIndex == 10) return;
            SetTimerInterval(500);
            EndStep();

            minDistDisp = (double)(cboxPointDistance.SelectedIndex + 1);
            minDistSq = minDistDisp * minDistDisp;

            rA = rB = rC = rD = rE = rF = rG = firstPoint = currentPoint = 0;

            vec3[] arr = new vec3[secList.Count];
            secList.CopyTo(arr);

            int cntr = 0;

            lblPointToProcess.Text = secList.Count.ToString(CultureInfo.InvariantCulture);

            panel1.IsVisible = true;
            // [XPLAT] yield once so the overlay actually paints before the reduction begins.
            await Task.Yield();

            for (int i = 0; i < secList.Count; i++)
            {
                //already checked
                if (arr[i].heading > 0)
                    continue;

                for (int j = 0; j < secList.Count; j++)
                {
                    if (j == i) continue;
                    //if (arr[j].heading != 0) continue;

                    if (arr[j].heading == 0)
                    {
                        double dist = glm.DistanceSquared(secList[i], secList[j]);
                        if (dist < minDistSq)
                        {
                            //means delete this point
                            arr[j].heading = 1;
                        }
                    }
                }

                //points all around it are removed or > minDist
                arr[i].heading = 2;

                cntr++;

                if (cntr > 200)
                {
                    lblI.Text = i.ToString(CultureInfo.InvariantCulture);
                    await Task.Yield(); // [XPLAT] was panel1.Refresh()
                    cntr = 0;
                }
            }

            panel1.IsVisible = false;

            secList?.Clear();
            foreach (var item in arr)
            {
                //0 will mean visible
                if (item.heading == 2) secList.Add(new vec3(item.easting, item.northing, 0));
            }

            //Find most South point
            double minny = double.MaxValue;

            for (int j = 0; j < secList.Count; j++)
            {
                if (minny > secList[j].northing)
                {
                    firstPoint = j;
                    minny = secList[j].northing;
                }
            }

            btnStartStop.Background = Brushes.LightGreen;
            btnStartStop.IsEnabled = true;
        }

        private void PacMan()
        {
            btnStartStop.IsEnabled = false;
            if (secList.Count < 20)
            {
                _showMessage("Not enough points to make a boundary");
                return;
            }

            arr = new vec3[secList.Count];
            prevHeading = Math.PI + glm.PIBy2;

            //find most southerly - lowest Y point
            double minny = double.MaxValue;
            for (int j = 0; j < secList.Count; j++)
            {
                if (minny > secList[j].northing)
                {
                    firstPoint = j;
                    minny = secList[j].northing;
                }
            }

            //keep firstPoint
            currentPoint = firstPoint;

            //first point of bnd
            bndList?.Clear();
            bndList.Add(new vec3(secList[currentPoint]));

            secList.CopyTo(arr);

            isStep = !isStep;


            if (isStep)
            {
                SetTimerInterval(50);
            }
            else
            {
                SetTimerInterval(500);
                EndStep();
            }
            // [XPLAT] WinForms btnStartStop.BackColor = Color.WhiteSmoke -> Avalonia Background brush.
            btnStartStop.Background = Brushes.WhiteSmoke;

            //btnStartStop.Enabled = false;
        }

        private void Smooth()
        {
            if (cboxSmooth.SelectedIndex == 6) return;

            smPtsChoose = cboxSmooth.SelectedIndex;

            if (smPtsChoose == 0)
            {
                smPts = 0;
                // [XPLAT] WinForms set cboxSmooth.Text = smPts.ToString() to echo the value; the Avalonia
                // ComboBox already displays the selected item (whose value equals smPts), so no Text write is
                // needed (and ComboBox exposes no settable Text).
            }
            else
            {
                smPts = 2;
                for (int i = 1; i <= smPtsChoose; i++)
                    smPts *= 2;
                // [XPLAT] see note above — selected item already shows smPts.
            }
            SmoothList();
        }

        private void BuildBnd()
        {
            if (smooList.Count == 0) return;

            if (smooList.Count > 5)
            {
                secList?.Clear();

                //just in case
                DeleteBoundary();

                CBoundaryList New = new CBoundaryList();

                for (int i = 0; i < smooList.Count; i++)
                {
                    New.fenceLine.Add(new vec3(smooList[i]));
                }

                New.CalculateFenceArea(0);
                New.FixFenceLine(0);
                _bnd.bndList.Add(New);
                smooList.Clear();
                bndList?.Clear();

                //turn lines made from boundaries
                _fieldData.CalculateMinMax();
                _bnd.BuildTurnLines();

                _fieldData.UpdateFieldBoundaryGUIAreas();
                _fileSaveBoundary();
            }

            btnStartStop.IsEnabled = false;

            btnSlice.IsVisible = true;
            btnCancelTouch.IsVisible = true;
            btnZoomIn.IsVisible = true;
            btnZoomOut.IsVisible = true;
        }

        private void SmoothList()
        {
            secList?.Clear();

            //just go back if not very long
            if (bndList.Count < 20) return;

            int cnt = bndList.Count;

            //the temp array
            vec3[] arr = new vec3[cnt];

            if (smPts != 0)
            {

                //read the points before and after the setpoint
                for (int s = 0; s < smPts / 2; s++)
                {
                    arr[s].easting = bndList[s].easting;
                    arr[s].northing = bndList[s].northing;
                    arr[s].heading = bndList[s].heading;
                }

                for (int s = cnt - (smPts / 2); s < cnt; s++)
                {
                    arr[s].easting = bndList[s].easting;
                    arr[s].northing = bndList[s].northing;
                    arr[s].heading = bndList[s].heading;
                }

                //average them - center weighted average
                for (int i = smPts / 2; i < cnt - (smPts / 2); i++)
                {
                    for (int j = -smPts / 2; j < smPts / 2; j++)
                    {
                        arr[i].easting += bndList[j + i].easting;
                        arr[i].northing += bndList[j + i].northing;
                    }
                    arr[i].easting /= smPts;
                    arr[i].northing /= smPts;
                    arr[i].heading = bndList[i].heading;
                }
            }
            else
            {
                for (int i = 0; i < bndList.Count; i++)
                {
                    arr[i] = bndList[i];
                }
            }

            //make a list to draw
            smooList?.Clear();

            if (arr == null || cnt < 1) return;
            if (smooList == null) return;

            for (int i = 0; i < cnt; i++)
            {
                smooList.Add(arr[i]);
            }

            CABCurve.CalculateHeadings(ref smooList);

            List<vec3> smList = new List<vec3>();

            for (int i = 0; i < smooList.Count; i++)
            {
                smList.Add(new vec3(smooList[i]));
            }
            double delta = 0;
            smooList?.Clear();

            for (int i = 0; i < smList.Count; i++)
            {
                if (i == 0)
                {
                    smooList.Add(new vec3(smList[i]));
                    continue;
                }
                delta += (smList[i - 1].heading - smList[i].heading);
                if (Math.Abs(delta) > 0.02)
                {
                    smooList.Add(new vec3(smList[i]));
                    delta = 0;
                }
            }

            int bndCount = smooList.Count;

            for (int i = 0; i < bndCount; i++)
            {
                int j = i + 1;

                if (j == bndCount) j = 0;
                double distance = glm.Distance(smooList[i], smooList[j]);
                if (distance > 1.6)
                {
                    vec3 pointB = new vec3((smooList[i].easting + smooList[j].easting) / 2.0,
                        (smooList[i].northing + smooList[j].northing) / 2.0, smooList[i].heading);

                    smooList.Insert(j, pointB);
                    bndCount = smooList.Count;
                    i--;
                }
            }
            CABCurve.CalculateHeadings(ref smooList);
        }

        private void DeleteBoundary()
        {
            _bnd.bndList?.Clear();
            _fileSaveBoundary();
            _fieldData.UpdateFieldBoundaryGUIAreas();
            _fileSaveHeadland();
        }

        private async void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] Disable up-front so a second click during the awaited reduction cannot start a
            // concurrent build (WinForms blocked re-entry implicitly because the reduction ran synchronously
            // inside the click). SpacingAsync/Reset re-enable it; PacMan disables it again as before.
            btnStartStop.IsEnabled = false;
            try
            {
                await SpacingAsync();

                PacMan();
            }
            catch (Exception)
            {
                // [XPLAT] async-void guard: a fault in the deterministic reduce build must not tear down the
                // app. Clear the overlay and re-enable the control so the operator can retry.
                panel1.IsVisible = false;
                btnStartStop.IsEnabled = true;
            }
        }

        private void btnAddBoundary_Click(object sender, RoutedEventArgs e)
        {
            if (timerIntervalMs == 50) return;
            if (bndList.Count < 10) return;

            Smooth();

            BuildBnd();
        }

        private void cboxSmooth_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            AgOpenGPS.Properties.Settings.Default.bndToolSmooth = cboxSmooth.SelectedIndex;
            AgOpenGPS.Properties.Settings.Default.Save();
        }

        private void cboxPointDistance_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            AgOpenGPS.Properties.Settings.Default.bndToolSpacing = cboxPointDistance.SelectedIndex;
            AgOpenGPS.Properties.Settings.Default.Save();
        }

        private void btnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _viewport.ZoomOutStep();
        }

        private void btnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _viewport.ZoomInStep();
        }

        private void btnMoveDn_Click(object sender, RoutedEventArgs e)
        {
            _viewport.PanDown();
        }

        private void btnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            _viewport.PanUp();
        }

        private void btnMoveLeft_Click(object sender, RoutedEventArgs e)
        {
            _viewport.PanLeft();
        }

        private void btnMoveRight_Click(object sender, RoutedEventArgs e)
        {
            _viewport.PanRight();
        }

        private void btnSlice_Click(object sender, RoutedEventArgs e)
        {
            bool isLoop = false;
            int limit = end;
            if (end == 99999 || start == 99999) return;

            if (bndSelect >= 0 && bndSelect < _bnd.bndList.Count && _bnd.bndList[bndSelect].fenceLine.Count > 0)
            {
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

                vec3[] arr = new vec3[_bnd.bndList[bndSelect].fenceLine.Count];
                _bnd.bndList[bndSelect].fenceLine.CopyTo(arr);

                if (start++ == arr.Length) start--;
                //if (end-- == -1) end = 0;
                if (start == end) return;

                for (int i = start; i < end; i++)
                {
                    //calculate the point inside the boundary
                    arr[i].heading = 999;

                    if (isLoop && i == _bnd.bndList[bndSelect].fenceLine.Count - 1)
                    {
                        i = -1;
                        isLoop = false;
                        end = limit;
                    }
                }

                if (isC)
                    arr[start] = new vec3(pint);

                _bnd.bndList[bndSelect].fenceLine.Clear();

                for (int i = 0; i < arr.Length; i++)
                {
                    //calculate the point inside the boundary
                    if (arr[i].heading != 999)
                        _bnd.bndList[bndSelect].fenceLine.Add(new vec3(arr[i]));

                    if (isLoop && i == arr.Length - 1)
                    {
                        i = -1;
                        isLoop = false;
                        end = limit;
                    }
                }

                _bnd.bndList[bndSelect].FixFenceLine(bndSelect);

                _fieldData.CalculateMinMax();
                _bnd.BuildTurnLines();

                _fieldData.UpdateFieldBoundaryGUIAreas();
                _fileSaveBoundary();
            }

            start = 99999; end = 99999;
            isA = true;
            isC = false;
        }

        private void btnCenterOGL_Click(object sender, RoutedEventArgs e)
        {
            _viewport.ResetZoomPan();
        }

        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnCancelTouch_Click(object sender, RoutedEventArgs e)
        {
            start = 99999; end = 99999;
            btnExit.Focus();
            isC = false;
            isA = true;

            btnAddPoints.IsEnabled = false;
        }

        // [XPLAT] WinForms oglSelf_MouseDown(MouseEventArgs) -> PointerPressed on the hosted GL control.
        private void oglSelf_MouseDown(object sender, PointerPressedEventArgs e)
        {
            // [XPLAT] WinForms used oglSelf.PointToClient(Cursor.Position) (client PIXELS). e.GetPosition
            // returns logical (DIP) coordinates, while GetGeoCoord expects the same pixel space as the
            // viewport's ViewportSize (Bounds * RenderScaling in AvaloniaGeoViewport), so scale the position
            // by the visual root's RenderScaling — mirroring how the host derives its pixel size.
            var ptt = e.GetPosition(_viewport.View);
            double scaling = _viewport.View.GetVisualRoot()?.RenderScaling ?? 1.0;
            if (double.IsNaN(scaling) || double.IsInfinity(scaling) || scaling <= 0.0)
            {
                scaling = 1.0;
            }

            XyCoord xyClient = new XyCoord(ptt.X * scaling, ptt.Y * scaling);
            GeoCoord mouseDownCoord = _viewport.GetGeoCoord(xyClient);

            if (cboxIsZoom.IsChecked == true)
            {
                _viewport.PointZoom(mouseDownCoord, 0.125);
                cboxIsZoom.IsChecked = false;
                return;
            }

            pint = new vec3(mouseDownCoord);

            if (_bnd.bndList.Count != 0)
            {
                if (start != 99999 & end != 99999)
                {
                    isC = true;
                    return;
                }
            }

            if (isA)
            {
                double minDistA = double.MaxValue;
                start = 99999; end = 99999;
                if (_bnd.bndList.Count != 0)
                {
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
                }
                else
                {
                    start = 1;
                    ptA = pint;
                    btnAddPoints.IsEnabled = false;
                }

                isA = false;
            }
            else
            {
                double minDistA = double.MaxValue;
                int j = bndSelect;

                if (_bnd.bndList.Count != 0)
                {

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
                }
                else
                {
                    end = 1;
                    ptB = pint;
                    btnAddPoints.IsEnabled = true;
                }
                isA = true;
            }
        }

        // [XPLAT] Body lifted VERBATIM from WinForms oglSelf_Paint (GLW DrawLib calls kept AS-IS). The host
        // (AvaloniaGeoViewport) invokes this between BeginPaint()/EndPaint() as its RenderAction; the view
        // never calls BeginPaint/EndPaint or touches the GL context. The isStep KNN()/PointZoom() stepping
        // that the WinForms paint ran at its TOP now lives in timer1_Tick (it mutates camera/walk state on
        // the UI thread BEFORE the host's BeginPaint reads it), avoiding a one-frame lag.
        private void RenderSelf()
        {
            //draw all the boundaries
            if (_bnd.bndList.Count > 0)
            {
                // outter boundary
                GLW.SetLineWidth(2.0f);
                GLW.SetColor(boundaryColor);
                GeoCoord[] fenceLineArray = GeoRefactorHelper.ToGeoCoordArray(_bnd.bndList[0].fenceLine);
                GLW.DrawLineLoopPrimitive(fenceLineArray);

                GLW.SetPointSize(4.0f);
                GLW.DrawPointsPrimitive(fenceLineArray);

                if (_bnd.bndList.Count > 1)
                {
                    //inner boundaries
                    for (int i = 1; i < _bnd.bndList.Count; i++)
                    {
                        GLW.SetLineWidth(2.0f);
                        GLW.SetColor(boundaryColor); //Change color to something else than main boundary color, maybe yellow?
                        GeoCoord[] innerFenceLineArray = GeoRefactorHelper.ToGeoCoordArray(_bnd.bndList[i].fenceLine);
                        GLW.DrawLineLoopPrimitive(innerFenceLineArray);

                        GLW.SetPointSize(4.0f);
                        GLW.DrawPointsPrimitive(innerFenceLineArray);
                    }
                }
            }

            //new boundary being made
            if (bndList.Count > 0)
            {
                GeoCoord[] boundaryArray = GeoRefactorHelper.ToGeoCoordArray(bndList);
                GLW.SetLineWidth(2.0f);
                GLW.SetColor(newBoundaryStripColor);
                GLW.DrawLineStripPrimitive(boundaryArray);

                GLW.SetPointSize(4.0f);
                GLW.SetColor(newBoundaryPointsColor);
                GLW.DrawPointsPrimitive(boundaryArray);

                GLW.SetColor(newBoundaryLoopColor);
                GLW.DrawLineLoopPrimitive(GeoRefactorHelper.ToGeoCoordArray(smooList));
            }

            //the section grid if loaded
            if (secList.Count > 0)
            {
                GLW.SetPointSize(2.0f);
                GLW.SetColor(isStep ? stepSectionColor : DrawColors.Yellow);
                GLW.DrawPointsPrimitive(GeoRefactorHelper.ToGeoCoordArray(secList));
            }
            DrawTouchPointsLine();
        }

        private void DrawTouchPointsLine()
        {
            GeoCoord? coordA = null;
            GeoCoord? coordB = null;
            GeoCoord? coordC = null;
            if (start != 99999)
            {
                coordA = ((_bnd.bndList.Count != 0) ? _bnd.bndList[bndSelect].fenceLine[start] : ptA).ToGeoCoord();
            }
            if (end != 99999)
            {
                coordB = ((_bnd.bndList.Count != 0) ? _bnd.bndList[bndSelect].fenceLine[end] : ptB).ToGeoCoord();
            }
            if (isC)
            {
                coordC = pint.ToGeoCoord();
            }
            TouchPointsLineVisual.DrawTouchPointsLine(coordA, coordB, coordC);
        }

        // [XPLAT] WinForms timer1_Tick called oglSelf.Refresh() (which ran the isStep KNN()/PointZoom() at the
        // top of oglSelf_Paint, then repainted). Here the stepping runs in the tick — on the UI thread, before
        // requesting a redraw — so the host's next BeginPaint reads fresh walk state. The isStep guard is
        // evaluated once (as in the WinForms paint): if a step was in progress this tick, PointZoom always
        // follows KNN even when KNN ends the walk.
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (isStep)
            {
                KNN();
                _viewport.PointZoom(secList[currentPoint].ToGeoCoord(), 0.5);
            }
            _viewport.RequestRender();
        }

        private void EndStep()
        {
            isStep = false;
            _viewport.ResetZoomPan();
        }
    }
}
