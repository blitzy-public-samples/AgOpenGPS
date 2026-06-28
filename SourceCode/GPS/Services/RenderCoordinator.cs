// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// RenderCoordinator is the cross-platform extraction of the AgOpenGPS render-orchestration logic. It
// was lifted (draw behavior FROZEN — 1:1 visual parity) out of the former WinForms god-object FormGPS
// partial class:
//   * SourceCode/GPS/Forms/OpenGL.Designer.cs — the perspective/2D-ortho main paint (oglMain), the
//     off-screen back-buffer section/look-ahead pixel scan (oglBack), the zoom overview overlap
//     calculation (oglZoom), the view-frustum extraction, the field bounding-box min/max, and the
//     ~24 Draw* HUD overlays.
//
// Hosting model (AAP §0.6.2 OpenGL hosting, §0.3.2 Adapter):
//   * The three legacy WinForms GLControls (oglMain/oglZoom/oglBack) are GONE. Their context lifecycle
//     (MakeCurrent + SwapBuffers + BeginPaint/EndPaint) is owned by the Avalonia host adapter
//     `AvaloniaGeoViewport : GeoViewportBase` (in the sibling Controls/ folder). This service performs
//     ONLY the draw routines. Render(GeoViewportBase) is invoked by the host between BeginPaint() and
//     EndPaint(); RenderBackBufferAndScan()/RenderZoomOverviewAndCalcOverlap() are invoked by the host
//     (driven by PositionService) with the appropriate off-screen framebuffer already bound and current.
//   * Per the AAP, the dependency direction is Controls -> Services and Views -> [Controls, Services].
//     This service therefore references ONLY the Core `GeoViewportBase` abstraction, never the concrete
//     `AvaloniaGeoViewport` (no `using AgOpenGPS.Controls;`). The composition root injects the host.
//
// Decoupling notes (AAP §0.6.1 — state separation):
//   * Every former direct FormGPS member access becomes a constructor-injected collaborator, an injected
//     late-bound delegate (for collaborators that are NOT depends_on_files of this service — ABLine,
//     curve, ct, recPath, mc, ahrs, sounds, flag/CFlag, fd, the Brands bitmaps), or a public render/UI
//     state property the Avalonia view-model refreshes each frame. There is no `mf`/FormGPS back-reference.
//   * Domain/coverage state lives in the shared CSection[] and the AgOpenGPS.Core ApplicationModel (the
//     SAME instances SectionService/PositionService use); render/UI state (camera mirror, day/night,
//     panel toggles, colors, pixel sizes) lives here as properties. The oglBack scan reads
//     appModel.avgSpeed (NOT a local copy) so the coverage it writes is byte-identical to SectionService.
//   * Where the WinForms original opened a Form, clicked a Button, or toggled a Label, this service
//     performs only the DOMAIN/RENDER work and raises an event (OnFlagPicked / OnGuidanceLineExpired /
//     OnHardwareMessageExpired) the Avalonia view subscribes to.
//
// [XPLAT] FEASIBILITY RISK (DOMINANT OPEN ITEM — track in MIGRATION_DOCS/PARITY_REPORT.md):
// ============================================================================================
//   This code uses PERVASIVE IMMEDIATE-MODE / FIXED-FUNCTION OpenGL: GL.MatrixMode, GL.LoadIdentity,
//   GL.LoadMatrix, GL.Ortho, GL.PushMatrix/GL.PopMatrix, GL.Begin(PrimitiveType.*)/GL.Vertex2/
//   GL.Vertex3/GL.End, GL.Color3/GL.Color4, GL.Translate/GL.Rotate, Matrix4.CreatePerspectiveFieldOfView,
//   and GL.ReadPixels. The kept AgOpenGPS.Core GLW/DrawLib relies on the same fixed-function pipeline.
//
//   Avalonia's `OpenGlControlBase` frequently supplies an OpenGL ES / ANGLE context (ANGLE->Direct3D on
//   Windows, EGL elsewhere) in which immediate mode and the fixed-function matrix stack DO NOT EXIST,
//   and in which `glReadPixels` from the default framebuffer must be re-verified. Therefore:
//     * This file DELIBERATELY PRESERVES the immediate-mode calls verbatim for 1:1 parity. It MUST NOT
//       be rewritten to a VBO/VAO/shader pipeline as part of this migration — that is a separate,
//       larger renderer port tracked as the dominant open risk in PARITY_REPORT.md.
//     * The composition root / `AvaloniaGeoViewport` is responsible for requesting a DESKTOP-GL
//       (compatibility) context. This service ASSUMES one is current.
//     * The three GL.ReadPixels call sites (RenderBackBufferAndScan green coverage scan,
//       RenderZoomOverviewAndCalcOverlap overlap area, MakeFlagMark flag pick, and the
//       UseLightIconBySampling luma sample) MUST be verified on the Avalonia surface.
// ============================================================================================
//
// Behavior contract (AAP §0.2.2, §0.6.4 — FROZEN, parity-tested):
//   * The oglBack green-channel scan is DOMAIN-AFFECTING: it sets section[j].sectionOnRequest/
//     sectionOffRequest/isSectionRequiredOn/isSectionOn/isMappingOn and the section/mapping timers, then
//     calls SectionService.BuildMachineByte() — turning coverage into the PGN 0xFE/0xEF/0xE5 bytes. The
//     green tags (patch 0, tram 245, boundary 240, headland 250), the 500x300 buffer, PackAlignment=1,
//     the rpHeight clamp [8,290], and the goto control flow are preserved EXACTLY.
//   * CalcFrustum extracts the 6 clipping planes into frustum[24]; CalculateMinMax clamps the field
//     distance to [100, 5000]. Both are preserved verbatim (frozen culling/zoom math).
using System;
using System.Collections.Generic;
using AgOpenGPS.Classes;        // ScreenTextures, VehicleTextures (cross-platform texture holders)
using AgOpenGPS.Core;           // Camera, WorldGrid, ApplicationModel, btnStates
using AgOpenGPS.Core.Drawing;   // GeoViewportBase, Colors
using AgOpenGPS.Core.DrawLib;   // GLW, Texture2D, Font
using AgOpenGPS.Core.Models;    // XyCoord, XyDelta, GeoCoord, GeoBoundingBox, ColorRgba, Speed, Distance
using OpenTK;                   // Matrix4
using OpenTK.Graphics.OpenGL;   // GL + the OpenGL enums (PrimitiveType, MatrixMode, EnableCap, ...)
// NOTE: the domain collaborator types (CSection, CTool, CTram, CBoundary, CYouTurn, CTrack, CPatches,
// CVehicle, CISOBUS, CNMEA), the static `glm` helper, the vec2/vec3 structs and the TrackMode enum all
// live in the parent `AgOpenGPS` namespace; because this file is declared in the nested
// `AgOpenGPS.Services` namespace, the C# enclosing-namespace lookup resolves them without an explicit
// `using AgOpenGPS;` (a directive Roslyn's IDE0005 would otherwise flag as redundant — and which would
// fail the Release `TreatWarningsAsErrors` build). This mirrors the sibling SectionService.cs / PgnDispatcher.cs.

namespace AgOpenGPS.Services
{
    /// <summary>
    /// [XPLAT] Cross-platform render-orchestration service, extracted (draw behavior FROZEN) from the
    /// WinForms FormGPS partial <c>OpenGL.Designer.cs</c>. Holds all RENDER state and performs the
    /// main-viewport paint, the off-screen <c>oglBack</c> section-coverage pixel scan (which feeds
    /// <see cref="SectionService.BuildMachineByte"/>), the zoom-overview overlap calculation, the view
    /// frustum extraction and the field bounding-box math, plus the ~24 HUD overlays. All former
    /// <c>mf</c>/FormGPS members are constructor-injected (concrete collaborators), injected as late-bound
    /// delegates (non-collaborator dependencies), or exposed as per-frame render/UI state properties.
    /// It references ONLY the Core <see cref="GeoViewportBase"/> abstraction — never the concrete
    /// Avalonia host control — keeping the Services&lt;-Controls edge one-directional.
    /// </summary>
    public class RenderCoordinator
    {
        // ---- Injected collaborators (former FormGPS `mf.*` access — AAP §0.6.1 state separation) ----

        /// <summary>Perspective camera (look-at, zoom, set-distance) — was mf.camera.</summary>
        private readonly Camera camera;

        /// <summary>Field surface + world grid renderer — was mf.worldGrid.</summary>
        private readonly WorldGrid worldGrid;

        /// <summary>Shared section array; the oglBack scan writes coverage into it (also used by SectionService).</summary>
        private readonly CSection[] section;

        /// <summary>Implement/tool geometry, per-side speeds and the back-buffer read-pixel window — was mf.tool.</summary>
        private readonly CTool tool;

        /// <summary>Tramline geometry + control byte (drawn green-245 to the back buffer) — was mf.tram.</summary>
        private readonly CTram tram;

        /// <summary>Boundary/headland geometry and the headland proximity state — was mf.bnd.</summary>
        private readonly CBoundary bnd;

        /// <summary>U-turn (you-turn) runtime state for the on-screen U-turn button/markers — was mf.yt.</summary>
        private readonly CYouTurn yt;

        /// <summary>Active guidance track collection (current index + per-track mode/offset) — was mf.trk.</summary>
        private readonly CTrack trk;

        /// <summary>Applied-coverage triangle-strip patches (drawn in the main + back + zoom views) — was mf.triStrip.</summary>
        private readonly List<CPatches> triStrip;

        /// <summary>Vehicle geometry/look-ahead/dead-zone/cross-track state and the vehicle draw — was mf.vehicle.</summary>
        private readonly CVehicle vehicle;

        /// <summary>ISOBUS task-controller section override source for the back-buffer scan — was mf.isobus.</summary>
        private readonly CISOBUS isobus;

        /// <summary>NMEA fix state (fix quality, differential age) read by the RTK/age overlays — was mf.pn.</summary>
        private readonly CNMEA pn;

        /// <summary>Bitmap font renderer (HUD text) — was mf.font.</summary>
        private readonly Font font;

        /// <summary>Screen/HUD texture holder (cross-platform) — was mf.ScreenTextures.</summary>
        private readonly ScreenTextures screenTextures;

        /// <summary>Vehicle/implement texture holder (cross-platform) — was mf.VehicleTextures.</summary>
        private readonly VehicleTextures vehicleTextures;

        /// <summary>
        /// Shared AgOpenGPS.Core runtime state. Source of the canonical fused average speed (avgSpeed),
        /// the autosteer-engaged flag (isBtnAutoSteerOn), the fix-cadence watchdog counter
        /// (sentenceCounter), the field-job gate (isJobStarted), the per-fix scan flags (isReverse,
        /// isHeadlandDistanceOn), the applied-patch tally (patchCounter), the section/coverage day colour
        /// (SectionColorDay), the live pivot-to-turn-line distance (distancePivotToTurnLine), the
        /// commanded steer angle (guidanceLineSteerAngle) and the canonical machine PGN 0xEF frame
        /// (machinePgnEF) whose uturn/hydLift bytes are written by CYouTurn/CHead and the on-screen
        /// U-turn button here — the SAME instances SectionService/PositionService/CHead/CYouTurn use.
        /// </summary>
        private readonly ApplicationModel appModel;

        /// <summary>
        /// Owns the section state machine and the machine-byte (PGN) assembler. After the oglBack pixel
        /// scan updates <c>section[j].isSectionOn</c>, this service calls
        /// <see cref="SectionService.BuildMachineByte"/> — the render-&gt;section seam that turns coverage
        /// into the PGN 0xFE/0xEF/0xE5 bytes before PositionService transmits them.
        /// </summary>
        private readonly SectionService sectionService;

        // ---- Injected late-bound delegates (collaborators NOT in this service's depends_on_files) ------
        // Same delegate-injection pattern SectionService/PgnDispatcher use. The composition root wires
        // these to the live CContour (ct), CRecordedPath (recPath), CModuleComm (mc), CAHRS (ahrs),
        // CSound (sounds), CABLine (ABLine), CABCurve (curve), CFieldData (fd), CFlag list and Brands
        // bitmap helpers — none of which are collaborators of the render coordinator.

        /// <summary>Live reader for <c>ct.isContourBtnOn</c> (contour guidance active).</summary>
        private readonly Func<bool> getIsContourBtnOn;

        /// <summary>Live reader for <c>recPath.isDrivingRecordedPath</c>.</summary>
        private readonly Func<bool> getIsDrivingRecordedPath;

        /// <summary>Live reader for <c>mc.steerSwitchHigh</c> (steer switch engaged) — DrawSteerCircle colour.</summary>
        private readonly Func<bool> getSteerSwitchHigh;

        /// <summary>Live reader for <c>mc.isOutOfBounds</c> — drives the amber screen border.</summary>
        private readonly Func<bool> getIsOutOfBounds;

        /// <summary>Live reader for <c>sounds.isRTKAlarming</c> — gates the flashing RTK/turn-too-close border.</summary>
        private readonly Func<bool> getIsRtkAlarming;

        /// <summary>Live reader for <c>ABLine.isHeadingSameWay</c> — DrawTrackInfo direction glyph.</summary>
        private readonly Func<bool> getABLineIsHeadingSameWay;

        /// <summary>Live reader for <c>curve.isHeadingSameWay</c> — DrawTrackInfo direction glyph.</summary>
        private readonly Func<bool> getCurveIsHeadingSameWay;

        /// <summary>Live reader for <c>ABLine.lineWidth</c> — boundary/turn/headland line widths.</summary>
        private readonly Func<float> getAbLineWidth;

        /// <summary>Live reader for <c>ahrs.imuRoll</c> — DrawSteerCircle roll rotation/text.</summary>
        private readonly Func<double> getImuRoll;

        /// <summary>Live reader for <c>mc.actualSteerAngleDegrees</c> — DrawSteerBarText error bar.</summary>
        private readonly Func<double> getActualSteerAngleDegrees;

        /// <summary>
        /// Live reader for the section heading cosine (<c>cos(-toolPivotPos.heading)</c>), computed each fix
        /// by the position pipeline (PositionService — not a collaborator of this service). Used to place
        /// the follow-up triangle-strip patch endpoints in the main paint.
        /// </summary>
        private readonly Func<double> getCosSectionHeading;

        /// <summary>
        /// Live reader for the section heading sine (<c>sin(-toolPivotPos.heading)</c>), computed each fix
        /// by the position pipeline. Paired with <see cref="getCosSectionHeading"/> for the follow-up patches.
        /// </summary>
        private readonly Func<double> getSinSectionHeading;

        /// <summary>Live reader for <c>ABLine.howManyPathsAway</c> — DrawTrackInfo pass label.</summary>
        private readonly Func<int> getABLineHowManyPathsAway;

        /// <summary>Live reader for <c>curve.howManyPathsAway</c> — DrawTrackInfo pass label.</summary>
        private readonly Func<int> getCurveHowManyPathsAway;

        /// <summary>Live reader for <c>ABLine.goalPointAB</c> — the pure-pursuit goal-point marker.</summary>
        private readonly Func<vec2> getGoalPointAB;

        /// <summary>Live reader for <c>curve.goalPointCu</c> — the pure-pursuit goal-point marker.</summary>
        private readonly Func<vec2> getGoalPointCu;

        /// <summary>Live reader for <c>fd.workedAreaTotal</c> — zoom-overview overlap area calc.</summary>
        private readonly Func<double> getWorkedAreaTotal;

        /// <summary>
        /// Sink for the zoom-overview overlap result: argument 1 is <c>fd.actualAreaCovered</c>,
        /// argument 2 is <c>fd.overlapPercent</c>. Replaces the original direct field writes on CFieldData.
        /// </summary>
        private readonly Action<double, double> setOverlapResult;

        /// <summary>
        /// Factory for a new applied-coverage patch strip. Replaces the original <c>new CPatches(this)</c>;
        /// the composition root supplies a patch constructed with the shared ApplicationModel/CTool/
        /// CSection[]/CFieldData and the patch-save list (the CPatches ctor no longer takes FormGPS).
        /// </summary>
        private readonly Func<CPatches> createPatch;

        /// <summary>
        /// Draws the entire guidance-line region (was: <c>ct.DrawContourLine</c> / <c>ABLine.DrawABLines</c> /
        /// <c>curve.DrawCurve</c> / <c>curve.DrawCurveNew</c> / <c>ABLine.DrawABLineNew</c> /
        /// <c>recPath.DrawRecordedLine</c> / <c>recPath.DrawDubins</c>). Wired by the composition root over
        /// ct/trk/ABLine/curve/recPath — none of which are collaborators of this service.
        /// </summary>
        private readonly Action drawGuidanceLines;

        /// <summary>
        /// Draws the entire flags overlay region (was: <c>DrawFlags()</c> iterating <c>flagPts</c> of CFlag,
        /// the selected-flag box, and the dashed direct line from the pivot to the selected flag). Wired by
        /// the composition root over the CFlag list — not a collaborator of this service.
        /// </summary>
        private readonly Action drawFlagsOverlay;

        /// <summary>
        /// Runs the work/steer-switch check (was: <c>if (ahrs.isAutoSteerAuto || mc.isRemoteWorkSystemOn)
        /// mc.CheckWorkAndSteerSwitch();</c>). The composition root evaluates the gate over ahrs/mc and
        /// invokes the check; this service simply triggers it at the frozen point in the back-buffer scan.
        /// </summary>
        private readonly Action checkWorkAndSteerSwitch;

        /// <summary>
        /// Loads the brand vehicle textures (was the body of <c>SetVehicleTextures()</c>: the
        /// <c>VehicleTextures.*.SetBitmap(TractorBitmaps/HarvesterBitmaps/ArticulatedBitmaps...)</c> calls
        /// that depend on the Windows-coupled Brands bitmap helpers). Isolated behind a delegate so this
        /// service does not reference Brands.
        /// </summary>
        private readonly Action loadVehicleTextures;

        /// <summary>
        /// Applies the saved camera zoom (was <c>SetZoom()</c>, a FormGPS partial that reads the zoom
        /// from settings into the camera). Invoked once from <see cref="OnViewportInit"/>.
        /// </summary>
        private readonly Action setZoom;

        // ---- Events (replace the WinForms Form/Label interactions; the Avalonia view subscribes) -------

        /// <summary>
        /// [XPLAT] Raised by <see cref="MakeFlagMark"/> when the user clicks on an existing flag
        /// (was: opening/focusing the WinForms <c>FormFlags</c> dialog). The view opens the flag editor.
        /// <see cref="FlagNumberPicked"/> holds the one-based picked flag number.
        /// </summary>
        public event Action OnFlagPicked;

        /// <summary>
        /// [XPLAT] Raised when the transient guidance-line message timer reaches zero (was
        /// <c>lblGuidanceLine.Visible = false</c>). The view hides the guidance-line label.
        /// </summary>
        public event Action OnGuidanceLineExpired;

        /// <summary>
        /// [XPLAT] Raised when the transient hardware-message timer reaches zero (was
        /// <c>lblHardwareMessage.Visible = false</c>). The view hides the hardware-message label.
        /// </summary>
        public event Action OnHardwareMessageExpired;

        // ---- Private RENDER state (ported verbatim from OpenGL.Designer.cs L17-44) ----------------------
        // These are RENDER state (not domain state): the view frustum, the back-buffer read buffer, the
        // perspective parameters, the "no-GPS" dead-camera spin, the section-zone change detector and the
        // running cross-track averages used by the light/steer bars. The declared-but-never-used source
        // fields (byte[] rateRed/rateGrn/rateBlu, StringBuilder sb) are intentionally OMITTED: they are
        // dead in the entire source and would trip the Release TreatWarningsAsErrors / dead-code analysers.

        /// <summary>The 6 view-frustum clipping planes (4 coefficients each) extracted by <see cref="CalcFrustum"/>.</summary>
        private readonly double[] frustum = new double[24];

        /// <summary>
        /// Green-channel read buffer for the oglBack section/look-ahead coverage scan. Size FROZEN at
        /// 150001 (the original fixed allocation); the scan reads a (rpWidth x rpHeight) window into it.
        /// </summary>
        private readonly byte[] grnPixels = new byte[150001];

        /// <summary>Vertical field-of-view (radians) for the main perspective projection. FROZEN at 0.7.</summary>
        private readonly double fovy = 0.7;

        /// <summary>Far-plane multiplier applied to the camera set-distance in the main projection. FROZEN at -4.</summary>
        private readonly double camDistanceFactor = -4;

        /// <summary>One-shot guard so the first main paint forces a projection resize (matches source isInit).</summary>
        private bool isInit;

        /// <summary>The slow rotation angle (degrees) of the "No GPS" logo when the fix is stale.</summary>
        private int deadCam;

        /// <summary>Section-zone bitmask of the current frame (which sections are mapping-on) — change detector.</summary>
        private ulong number;

        /// <summary>Section-zone bitmask of the previous frame; when it differs from <see cref="number"/> the zones are re-packed.</summary>
        private ulong lastNumber;

        /// <summary>True while the implement look-ahead is inside the headland (drives the hydraulic raise/lower).</summary>
        private bool isHeadlandClose;

        /// <summary>Exponentially-smoothed pivot cross-track distance used by the light/steer bar text.</summary>
        private double avgPivDistance;

        /// <summary>Long-window smoothed absolute pivot distance (capped) used to scale the cross-track readout.</summary>
        private double longAvgPivDistance;

        /// <summary>Scratch tool-edge vectors reused by the direction-marker triangles in the main paint.</summary>
        private vec2 left, right, ptTip;

        // ---- Public RENDER / UI state (refreshed each frame by the Avalonia view-model / composition root) ----
        // None of these are domain truth; they mirror the former FormGPS booleans/values that the WinForms
        // paint read directly. The view-model pushes them before invoking Render so the draw is identical.

        /// <summary>Day/night palette selector (was mf.isDay): true uses the daytime sky-blue clear colour.</summary>
        public bool IsDay { get; set; }

        /// <summary>Whether the world grid is drawn (was mf.isGridOn).</summary>
        public bool IsGridOn { get; set; }

        /// <summary>Whether the field surface texture is drawn (was mf.isTextureOn).</summary>
        public bool IsTextureOn { get; set; }

        /// <summary>Whether triangle patches are drawn as wireframe polygons (was mf.isDrawPolygons — diagnostic).</summary>
        public bool IsDrawPolygons { get; set; }

        /// <summary>Whether per-section dividing lines are highlighted on the patches (was mf.isSectionlinesOn).</summary>
        public bool IsSectionLinesOn { get; set; }

        /// <summary>Whether the per-patch direction markers are drawn (was mf.isDirectionMarkers).</summary>
        public bool IsDirectionMarkers { get; set; }

        /// <summary>Whether GL line smoothing (anti-aliasing) is enabled on resize (was mf.isLineSmooth).</summary>
        public bool IsLineSmooth { get; set; }

        /// <summary>Field surface colour (was mf.fieldColor).</summary>
        public ColorRgba FieldColor { get; set; }

        /// <summary>World grid colour (was mf.worldGridColor).</summary>
        public ColorRgba WorldGridColor { get; set; }

        /// <summary>Pivot-axle world position (was mf.pivotAxlePos); drives the main camera look-at and the flag line.</summary>
        public vec3 PivotAxlePos { get; set; }

        /// <summary>Tool world position + heading (was mf.toolPos); drives the oglBack back-buffer transform.</summary>
        public vec3 ToolPos { get; set; }

        /// <summary>Camera heading in DEGREES (was mf.camHeading); third arg to <see cref="Camera.SetLookAt"/> and the compass rotate.</summary>
        public double CamHeading { get; set; }

        /// <summary>Fused fix heading in RADIANS (was mf.fixHeading) used by the compass text (distinct from ApplicationModel.FixHeading).</summary>
        public double FixHeading { get; set; }

        /// <summary>Signed pivot cross-track distance (cm) feeding the light/steer bar smoothing (was mf.lightbarDistance).</summary>
        public double LightbarDistance { get; set; }

        /// <summary>Current along-track step distance shown by the compass HUD (was mf.distanceCurrentStepFixDisplay).</summary>
        public double DistanceCurrentStepFixDisplay { get; set; }

        /// <summary>Metres-to-inch-or-cm factor for the track-info nudge readout (was mf.m2InchOrCm).</summary>
        public double M2InchOrCm { get; set; }

        /// <summary>GPS update rate (Hz) used to convert the section/mapping turn-on/off delays to frame counts (was mf.gpsHz).</summary>
        public double GpsHz { get; set; }

        /// <summary>World-grid spacing shown on the compass HUD (was mf.gridToolSpacing).</summary>
        public double GridToolSpacing { get; set; }

        /// <summary>Lightbar resolution in centimetres-per-dot (was mf.lightbarCmPerPixel — integer division semantics preserved).</summary>
        public int LightbarCmPerPixel { get; set; }

        /// <summary>Persistent steer-module heartbeat counter (was mf.steerModuleConnectedCounter); incremented in DrawSteerCircle.</summary>
        public int SteerModuleConnectedCounter { get; set; }

        /// <summary>Last pointer X in GL surface pixels (was mf.mouseX) — used by <see cref="MakeFlagMark"/>.</summary>
        public int MouseX { get; set; }

        /// <summary>Last pointer Y in GL surface pixels (was mf.mouseY) — used by <see cref="MakeFlagMark"/>.</summary>
        public int MouseY { get; set; }

        /// <summary>One-based flag number under the cursor after a pick (was mf.flagNumberPicked); 0 = none.</summary>
        public int FlagNumberPicked { get; set; }

        /// <summary>Frames remaining for the transient guidance-line message (was mf.guideLineCounter).</summary>
        public int GuideLineCounter { get; set; }

        /// <summary>Frames remaining for the transient hardware message (was mf.hardwareLineCounter).</summary>
        public int HardwareLineCounter { get; set; }

        /// <summary>Main GL surface width in pixels (was oglMain.Width).</summary>
        public int OglWidth { get; set; }

        /// <summary>Main GL surface height in pixels (was oglMain.Height).</summary>
        public int OglHeight { get; set; }

        /// <summary>Zoom-overview GL surface width in pixels (was oglZoom.Width).</summary>
        public int OglZoomWidth { get; set; }

        /// <summary>Zoom-overview GL surface height in pixels (was oglZoom.Height).</summary>
        public int OglZoomHeight { get; set; }

        /// <summary>"cm"/"in" north-south units suffix for the track-info readout (was mf.unitsInCmNS).</summary>
        public string UnitsInCmNS { get; set; } = string.Empty;

        /// <summary>Pre-formatted pivot-to-turn distance, metric (was mf.distPivotM) — U-turn button label.</summary>
        public string DistPivotM { get; set; } = string.Empty;

        /// <summary>Pre-formatted pivot-to-turn distance, imperial (was mf.distPivotFt) — U-turn button label.</summary>
        public string DistPivotFt { get; set; } = string.Empty;

        /// <summary>Application semantic version string (was Program.SemVer) shown by the beta/develop watermark.</summary>
        public string SemVer { get; set; } = string.Empty;

        /// <summary>Flash phase toggle (was mf.isFlashOnOff) for the RTK/turn alarm border and the tram dots.</summary>
        public bool IsFlashOnOff { get; set; }

        /// <summary>Selects the light-bar vs steer-bar cross-track readout (was mf.isLightBarNotSteerBar).</summary>
        public bool IsLightBarNotSteerBar { get; set; }

        /// <summary>Whether the graphical light bar (dot rail) is drawn (was mf.isLightbarOn).</summary>
        public bool IsLightbarOn { get; set; }

        /// <summary>Whether the speedometer gauge is drawn (was mf.isSpeedoOn).</summary>
        public bool IsSpeedoOn { get; set; }

        /// <summary>Whether reverse is being shown with IMU assistance (was mf.isReverseWithIMU) — DrawReverse path.</summary>
        public bool IsReverseWithIMU { get; set; }

        /// <summary>Whether the vehicle is mid direction-change (was mf.isChangingDirection) — DrawReverse path.</summary>
        public bool IsChangingDirection { get; set; }

        /// <summary>Metric vs imperial unit display (was mf.isMetric).</summary>
        public bool IsMetric { get; set; }

        /// <summary>Whether the pan fly-out is open (was mf.panelSimControls/isPanFormVisible) — compass pan icon gate.</summary>
        public bool IsPanFormVisible { get; set; }

        /// <summary>Whether the max-angular-velocity limiter is active (was mf.isMaxAngularVelocity) — compass "*".</summary>
        public bool IsMaxAngularVelocity { get; set; }

        /// <summary>Whether the Stanley controller is selected (was mf.isStanleyUsed) — gates the goal-point + manual U-turn glyph.</summary>
        public bool IsStanleyUsed { get; set; }

        /// <summary>Whether the U-turn feature is enabled (was mf.isUTurnOn) — manual U-turn glyph gate.</summary>
        public bool IsUTurnOn { get; set; }

        /// <summary>Whether lateral (nudge) manual mode is on (was mf.isLateralOn) — manual lateral glyph.</summary>
        public bool IsLateralOn { get; set; }

        /// <summary>Whether the hardware-message HUD line is enabled (was mf.isHardwareMessages).</summary>
        public bool IsHardwareMessages { get; set; }

        /// <summary>Whether a usable GPS position has been initialised (was mf.isGPSPositionInitialized) — main paint branch.</summary>
        public bool IsGpsPositionInitialized { get; set; }

        /// <summary>RTK-lost overlay flag (was mf.isRTK_AlarmOn). RENDER-ONLY here: the kill/debounce DECISION lives in PositionService.</summary>
        public bool IsRtkAlarmOn { get; set; }

        /// <summary>Develop-build watermark flag (was Program.isDevelopVersion).</summary>
        public bool IsDevelopVersion { get; set; }

        /// <summary>Pre-release/beta watermark flag (was Program.isPreRelease).</summary>
        public bool IsPreRelease { get; set; }

        /// <summary>Whether the user just pressed the left button over the GL surface (was mf.leftMouseDownOnOpenGL) — triggers flag pick.</summary>
        public bool LeftMouseDownOnOpenGL { get; set; }

        // ---- Public read-only field bounding-box results (consumed by the zoom overview + field-open recalc) ----

        /// <summary>The field's geographic bounding box, recomputed by <see cref="CalculateMinMax"/> (was mf.FieldBoundingBox).</summary>
        public GeoBoundingBox FieldBoundingBox { get; private set; }

        /// <summary>The centre of <see cref="FieldBoundingBox"/> (was mf.FieldCenter).</summary>
        public GeoCoord FieldCenter => FieldBoundingBox.CenterCoord;

        /// <summary>Easting of the field centre — the zoom-overview camera translate X (was mf.fieldCenterX).</summary>
        public double fieldCenterX => FieldCenter.Easting;

        /// <summary>Northing of the field centre — the zoom-overview camera translate Y (was mf.fieldCenterY).</summary>
        public double fieldCenterY => FieldCenter.Northing;

        /// <summary>Field "radius" used as the zoom-overview camera back-off distance, clamped to [100, 5000] (was mf.maxFieldDistance).</summary>
        public double maxFieldDistance;

        /// <summary>
        /// [XPLAT] Constructs the render coordinator with all former FormGPS collaborators injected
        /// explicitly (no <c>mf</c>/FormGPS back-reference). Concrete domain collaborators and the
        /// SectionService are required references; the late-bound delegates isolate the collaborators that
        /// are NOT depends_on_files of this service (ABLine/curve/ct/recPath/mc/ahrs/sounds/CFlag/fd/Brands).
        /// Render/UI state is supplied per-frame through the public properties, not here.
        /// </summary>
        /// <param name="camera">Perspective camera (look-at/zoom/set-distance).</param>
        /// <param name="worldGrid">Field surface + world grid renderer.</param>
        /// <param name="section">Shared section array the back-buffer scan writes coverage into.</param>
        /// <param name="tool">Implement/tool geometry and the back-buffer read-pixel window.</param>
        /// <param name="tram">Tramline geometry and control byte.</param>
        /// <param name="bnd">Boundary/headland geometry and proximity state.</param>
        /// <param name="yt">U-turn (you-turn) runtime state.</param>
        /// <param name="trk">Active guidance track collection.</param>
        /// <param name="triStrip">Applied-coverage triangle-strip patches.</param>
        /// <param name="vehicle">Vehicle geometry/look-ahead/dead-zone/cross-track state.</param>
        /// <param name="isobus">ISOBUS task-controller section override source.</param>
        /// <param name="pn">NMEA fix state (fix quality, differential age).</param>
        /// <param name="font">Bitmap font renderer for HUD text.</param>
        /// <param name="screenTextures">Screen/HUD texture holder.</param>
        /// <param name="vehicleTextures">Vehicle/implement texture holder.</param>
        /// <param name="appModel">Shared AgOpenGPS.Core runtime state (avg speed, autosteer flag, machine PGN frame, ...).</param>
        /// <param name="sectionService">Owns the section state machine and <c>BuildMachineByte()</c> (the render-&gt;section seam).</param>
        /// <param name="getIsContourBtnOn">Live reader for <c>ct.isContourBtnOn</c>.</param>
        /// <param name="getIsDrivingRecordedPath">Live reader for <c>recPath.isDrivingRecordedPath</c>.</param>
        /// <param name="getSteerSwitchHigh">Live reader for <c>mc.steerSwitchHigh</c>.</param>
        /// <param name="getIsOutOfBounds">Live reader for <c>mc.isOutOfBounds</c>.</param>
        /// <param name="getIsRtkAlarming">Live reader for <c>sounds.isRTKAlarming</c>.</param>
        /// <param name="getABLineIsHeadingSameWay">Live reader for <c>ABLine.isHeadingSameWay</c>.</param>
        /// <param name="getCurveIsHeadingSameWay">Live reader for <c>curve.isHeadingSameWay</c>.</param>
        /// <param name="getAbLineWidth">Live reader for <c>ABLine.lineWidth</c>.</param>
        /// <param name="getImuRoll">Live reader for <c>ahrs.imuRoll</c>.</param>
        /// <param name="getActualSteerAngleDegrees">Live reader for <c>mc.actualSteerAngleDegrees</c>.</param>
        /// <param name="getCosSectionHeading">Live reader for the section heading cosine (<c>cos(-toolPivotPos.heading)</c>).</param>
        /// <param name="getSinSectionHeading">Live reader for the section heading sine (<c>sin(-toolPivotPos.heading)</c>).</param>
        /// <param name="getABLineHowManyPathsAway">Live reader for <c>ABLine.howManyPathsAway</c>.</param>
        /// <param name="getCurveHowManyPathsAway">Live reader for <c>curve.howManyPathsAway</c>.</param>
        /// <param name="getGoalPointAB">Live reader for <c>ABLine.goalPointAB</c>.</param>
        /// <param name="getGoalPointCu">Live reader for <c>curve.goalPointCu</c>.</param>
        /// <param name="getWorkedAreaTotal">Live reader for <c>fd.workedAreaTotal</c>.</param>
        /// <param name="setOverlapResult">Sink for (<c>fd.actualAreaCovered</c>, <c>fd.overlapPercent</c>).</param>
        /// <param name="createPatch">Factory for a new <see cref="CPatches"/> (was <c>new CPatches(this)</c>).</param>
        /// <param name="drawGuidanceLines">Draws the whole guidance-line region.</param>
        /// <param name="drawFlagsOverlay">Draws the whole flags overlay region.</param>
        /// <param name="checkWorkAndSteerSwitch">Runs the gated work/steer-switch check.</param>
        /// <param name="loadVehicleTextures">Loads the brand vehicle textures (was the body of <c>SetVehicleTextures()</c>).</param>
        /// <param name="setZoom">Applies the saved camera zoom (was <c>SetZoom()</c>).</param>
        public RenderCoordinator(
            Camera camera,
            WorldGrid worldGrid,
            CSection[] section,
            CTool tool,
            CTram tram,
            CBoundary bnd,
            CYouTurn yt,
            CTrack trk,
            List<CPatches> triStrip,
            CVehicle vehicle,
            CISOBUS isobus,
            CNMEA pn,
            Font font,
            ScreenTextures screenTextures,
            VehicleTextures vehicleTextures,
            ApplicationModel appModel,
            SectionService sectionService,
            Func<bool> getIsContourBtnOn,
            Func<bool> getIsDrivingRecordedPath,
            Func<bool> getSteerSwitchHigh,
            Func<bool> getIsOutOfBounds,
            Func<bool> getIsRtkAlarming,
            Func<bool> getABLineIsHeadingSameWay,
            Func<bool> getCurveIsHeadingSameWay,
            Func<float> getAbLineWidth,
            Func<double> getImuRoll,
            Func<double> getActualSteerAngleDegrees,
            Func<double> getCosSectionHeading,
            Func<double> getSinSectionHeading,
            Func<int> getABLineHowManyPathsAway,
            Func<int> getCurveHowManyPathsAway,
            Func<vec2> getGoalPointAB,
            Func<vec2> getGoalPointCu,
            Func<double> getWorkedAreaTotal,
            Action<double, double> setOverlapResult,
            Func<CPatches> createPatch,
            Action drawGuidanceLines,
            Action drawFlagsOverlay,
            Action checkWorkAndSteerSwitch,
            Action loadVehicleTextures,
            Action setZoom)
        {
            // Concrete collaborators — all required (a null here is a composition-root wiring bug).
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
            this.worldGrid = worldGrid ?? throw new ArgumentNullException(nameof(worldGrid));
            this.section = section ?? throw new ArgumentNullException(nameof(section));
            this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
            this.tram = tram ?? throw new ArgumentNullException(nameof(tram));
            this.bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
            this.yt = yt ?? throw new ArgumentNullException(nameof(yt));
            this.trk = trk ?? throw new ArgumentNullException(nameof(trk));
            this.triStrip = triStrip ?? throw new ArgumentNullException(nameof(triStrip));
            this.vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            this.isobus = isobus ?? throw new ArgumentNullException(nameof(isobus));
            this.pn = pn ?? throw new ArgumentNullException(nameof(pn));
            this.font = font ?? throw new ArgumentNullException(nameof(font));
            this.screenTextures = screenTextures ?? throw new ArgumentNullException(nameof(screenTextures));
            this.vehicleTextures = vehicleTextures ?? throw new ArgumentNullException(nameof(vehicleTextures));
            this.appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            this.sectionService = sectionService ?? throw new ArgumentNullException(nameof(sectionService));

            // Late-bound delegates — all required so the frozen paint/scan never hits a null at the
            // former FormGPS member sites. The composition root supplies every one.
            this.getIsContourBtnOn = getIsContourBtnOn ?? throw new ArgumentNullException(nameof(getIsContourBtnOn));
            this.getIsDrivingRecordedPath = getIsDrivingRecordedPath ?? throw new ArgumentNullException(nameof(getIsDrivingRecordedPath));
            this.getSteerSwitchHigh = getSteerSwitchHigh ?? throw new ArgumentNullException(nameof(getSteerSwitchHigh));
            this.getIsOutOfBounds = getIsOutOfBounds ?? throw new ArgumentNullException(nameof(getIsOutOfBounds));
            this.getIsRtkAlarming = getIsRtkAlarming ?? throw new ArgumentNullException(nameof(getIsRtkAlarming));
            this.getABLineIsHeadingSameWay = getABLineIsHeadingSameWay ?? throw new ArgumentNullException(nameof(getABLineIsHeadingSameWay));
            this.getCurveIsHeadingSameWay = getCurveIsHeadingSameWay ?? throw new ArgumentNullException(nameof(getCurveIsHeadingSameWay));
            this.getAbLineWidth = getAbLineWidth ?? throw new ArgumentNullException(nameof(getAbLineWidth));
            this.getImuRoll = getImuRoll ?? throw new ArgumentNullException(nameof(getImuRoll));
            this.getActualSteerAngleDegrees = getActualSteerAngleDegrees ?? throw new ArgumentNullException(nameof(getActualSteerAngleDegrees));
            this.getCosSectionHeading = getCosSectionHeading ?? throw new ArgumentNullException(nameof(getCosSectionHeading));
            this.getSinSectionHeading = getSinSectionHeading ?? throw new ArgumentNullException(nameof(getSinSectionHeading));
            this.getABLineHowManyPathsAway = getABLineHowManyPathsAway ?? throw new ArgumentNullException(nameof(getABLineHowManyPathsAway));
            this.getCurveHowManyPathsAway = getCurveHowManyPathsAway ?? throw new ArgumentNullException(nameof(getCurveHowManyPathsAway));
            this.getGoalPointAB = getGoalPointAB ?? throw new ArgumentNullException(nameof(getGoalPointAB));
            this.getGoalPointCu = getGoalPointCu ?? throw new ArgumentNullException(nameof(getGoalPointCu));
            this.getWorkedAreaTotal = getWorkedAreaTotal ?? throw new ArgumentNullException(nameof(getWorkedAreaTotal));
            this.setOverlapResult = setOverlapResult ?? throw new ArgumentNullException(nameof(setOverlapResult));
            this.createPatch = createPatch ?? throw new ArgumentNullException(nameof(createPatch));
            this.drawGuidanceLines = drawGuidanceLines ?? throw new ArgumentNullException(nameof(drawGuidanceLines));
            this.drawFlagsOverlay = drawFlagsOverlay ?? throw new ArgumentNullException(nameof(drawFlagsOverlay));
            this.checkWorkAndSteerSwitch = checkWorkAndSteerSwitch ?? throw new ArgumentNullException(nameof(checkWorkAndSteerSwitch));
            this.loadVehicleTextures = loadVehicleTextures ?? throw new ArgumentNullException(nameof(loadVehicleTextures));
            this.setZoom = setZoom ?? throw new ArgumentNullException(nameof(setZoom));
        }

        // ================================================================================================
        //  Main viewport lifecycle (ported from oglMain_Load / oglMain_Resize). The host adapter
        //  (AvaloniaGeoViewport) calls OnViewportInit() from OnOpenGlInit and OnViewportResize() from its
        //  size handler — both with the GL context already current, so the original oglMain.MakeCurrent()
        //  calls are removed (host-owned).
        // ================================================================================================

        /// <summary>
        /// [XPLAT] Resets the smoothed pivot cross-track-distance accumulator to zero — the
        /// <c>longAvgPivDistance = 0</c> assignment that opened the WinForms <c>btnAutoSteer_Click</c>
        /// handler (Controls.Designer.cs). The accumulator is private render state, so the Avalonia
        /// autosteer toggle reaches it through <c>ShellCommands.ToggleAutoSteer</c>. Behaviour-frozen.
        /// </summary>
        public void ResetLongAvgPivDistance()
        {
            longAvgPivDistance = 0;
        }

        /// <summary>
        /// [XPLAT] One-time GL state setup for the main viewport (was <c>oglMain_Load</c>): the field-paint
        /// clear colour, alpha blend function, back-face cull mode, the saved camera zoom and the initial
        /// vehicle textures. The host invokes this from <c>OnOpenGlInit</c> with the context current; the
        /// original WinForms watchdog-timer enable and the <c>BeginInvoke</c> texture-load deferral are gone
        /// (the host guarantees the context is current here, so textures load synchronously).
        /// </summary>
        public void OnViewportInit()
        {
            GL.ClearColor(0.14f, 0.14f, 0.37f, 1.0f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.CullFace(CullFaceMode.Back);
            setZoom();

            SetVehicleTextures();
        }

        /// <summary>
        /// [XPLAT] Rebuilds the main perspective projection for a new surface size (was
        /// <c>oglMain_Resize</c>). The host passes the current surface width/height (was
        /// <c>oglMain.Width</c>/<c>oglMain.Height</c>); they are cached into <see cref="OglWidth"/>/
        /// <see cref="OglHeight"/> so the 2D-ortho overlays use the identical dimensions. The exact
        /// fovy / near (1.0) / far (camDistanceFactor * camera.camSetDistance) are preserved.
        /// </summary>
        /// <param name="width">Main GL surface width in pixels.</param>
        /// <param name="height">Main GL surface height in pixels.</param>
        public void OnViewportResize(int width, int height)
        {
            OglWidth = width;
            OglHeight = height;

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Viewport(0, 0, width, height);
            Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView((float)fovy, (float)width / (float)height,
                1.0f, (float)(camDistanceFactor * camera.camSetDistance));
            GL.LoadMatrix(ref mat);
            GL.MatrixMode(MatrixMode.Modelview);
            if (IsLineSmooth) GL.Enable(EnableCap.LineSmooth);
            else GL.Disable(EnableCap.LineSmooth);
        }

        /// <summary>
        /// [XPLAT] Loads the brand vehicle textures (was <c>SetVehicleTextures()</c>). The actual
        /// <c>VehicleTextures.*.SetBitmap(TractorBitmaps/HarvesterBitmaps/ArticulatedBitmaps.Get*(...))</c>
        /// calls depend on the Windows-coupled Brands bitmap helper and the VehicleSettings, so they are
        /// isolated behind the injected <see cref="loadVehicleTextures"/> delegate — keeping this service
        /// free of any Brands/Settings reference.
        /// </summary>
        public void SetVehicleTextures()
        {
            loadVehicleTextures();
        }

        // ================================================================================================
        //  Main visible-frame paint (ported from oglMain_Paint, source L76-769). DRAW-ONLY: the host
        //  (AvaloniaGeoViewport) owns MakeCurrent / BeginPaint / EndPaint / buffer-swap and calls this
        //  between BeginPaint() and EndPaint(). The two removed calls are the original oglMain.MakeCurrent()
        //  (top) and oglMain.SwapBuffers() (end of each branch). Everything else is preserved verbatim, EXCEPT:
        //    * the RTK alarm region is reduced to RENDER-ONLY (Phase 7) — the autosteer-kill decision and
        //      the RTK-recovered debounce moved to PositionService.EvaluateRtkRecovery();
        //    * the "No GPS" branch's lblSpeed/lblHz WinForms label writes are dropped (the view-model owns them);
        //    * the periodic file-save tail (DistanceToFieldOriginCheck / FileSaveSections / FileSaveContour /
        //      FileSaveElevation / oglZoom.Refresh) is dropped (it belongs to FieldIoService / the host scan loop).
        // ================================================================================================

        /// <summary>
        /// [XPLAT] Draws one main-viewport frame (FROZEN — 1:1 visual parity). Invoked by the host adapter
        /// between its <c>BeginPaint()</c> and <c>EndPaint()</c>; this method performs ONLY the GL draw
        /// routines and ends with <c>GL.Flush()</c> (NOT <c>SwapBuffers</c>, which the host owns). When the
        /// fix is stale (<c>sentenceCounter &gt;= 299</c>) it renders the spinning "No GPS" fallback instead.
        /// </summary>
        /// <param name="viewport">
        /// The Core viewport abstraction supplied by the host. The host has already made the GL context
        /// current and begun the paint; this draw-only method drives the fixed-function pipeline directly
        /// (the projection was established by <see cref="OnViewportResize"/>), so the parameter is accepted
        /// for the host-contracted signature and to keep the Services&lt;-Controls edge one-directional.
        /// </param>
        public void Render(GeoViewportBase viewport)
        {
            if (appModel.sentenceCounter < 299)
            {
                if (IsGpsPositionInitialized)
                {
                    if (!isInit)
                    {
                        OnViewportResize(OglWidth, OglHeight);
                    }
                    isInit = true;

                    //  Clear the color and depth buffer.
                    GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);

                    if (IsDay) GL.ClearColor(0.27f, 0.4f, 0.7f, 1.0f);
                    else GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);

                    GL.LoadIdentity();

                    #region Camera
                    //position the camera
                    camera.SetLookAt(PivotAxlePos.easting, PivotAxlePos.northing, CamHeading);

                    //the bounding box of the camera for cullling.
                    CalcFrustum();
                    GL.Disable(EnableCap.Blend);

                    #endregion

                    #region World and Grid

                    worldGrid.DrawFieldSurface(FieldColor, camera.ZoomValue, IsTextureOn);

                    if (IsGridOn) worldGrid.DrawWorldGrid(WorldGridColor);

                    if (IsDrawPolygons) GL.PolygonMode(MaterialFace.Front, PolygonMode.Line);

                    #endregion

                    #region Triangle section patches

                    GL.Enable(EnableCap.Blend);
                    //draw patches of sections

                    //direction marker width
                    double factor = 0.37;

                    GL.LineWidth(2);

                    for (int j = 0; j < triStrip.Count; j++)
                    {
                        //every time the section turns off and on is a new patch //check if in frustum or not
                        bool isDraw;

                        int patches = triStrip[j].patchList.Count;

                        if (patches > 0)
                        {
                            //initialize the steps for mipmap of triangles (skipping detail while zooming out)
                            int mipmap = 0;
                            if (camera.camSetDistance < -800) mipmap = 2;
                            if (camera.camSetDistance < -1500) mipmap = 4;
                            if (camera.camSetDistance < -2400) mipmap = 8;
                            if (camera.camSetDistance < -5000) mipmap = 16;

                            //for every new chunk of patch
                            foreach (var triList in triStrip[j].patchList)
                            {
                                //check for even
                                if (triList.Count % 2 == 0)
                                    break;

                                isDraw = false;
                                int count2 = triList.Count;
                                for (int i = 1; i < count2; i += 3)
                                {
                                    //determine if point is in frustum or not, if < 0, its outside so abort, z always is 0
                                    if (frustum[0] * triList[i].easting + frustum[1] * triList[i].northing + frustum[3] <= 0)
                                        continue;//right
                                    if (frustum[4] * triList[i].easting + frustum[5] * triList[i].northing + frustum[7] <= 0)
                                        continue;//left
                                    if (frustum[16] * triList[i].easting + frustum[17] * triList[i].northing + frustum[19] <= 0)
                                        continue;//bottom
                                    if (frustum[20] * triList[i].easting + frustum[21] * triList[i].northing + frustum[23] <= 0)
                                        continue;//top
                                    if (frustum[8] * triList[i].easting + frustum[9] * triList[i].northing + frustum[11] <= 0)
                                        continue;//far
                                    if (frustum[12] * triList[i].easting + frustum[13] * triList[i].northing + frustum[15] <= 0)
                                        continue;//near

                                    //point is in frustum so draw the entire patch. The downside of triangle strips.
                                    isDraw = true;
                                    break;
                                }

                                if (isDraw)
                                {
                                    count2 = triList.Count;
                                    GL.Begin(PrimitiveType.TriangleStrip);

                                    if (IsDay) GL.Color4((byte)triList[0].easting, (byte)triList[0].northing, (byte)triList[0].heading, (byte)152);
                                    else GL.Color4((byte)triList[0].easting, (byte)triList[0].northing, (byte)triList[0].heading, (byte)(152 * 0.5));

                                    //if large enough patch and camera zoomed out, fake mipmap the patches, skip triangles
                                    if (count2 >= (mipmap + 2))
                                    {
                                        int step = mipmap;
                                        for (int i = 1; i < count2; i += step)
                                        {
                                            GL.Vertex3(triList[i].easting, triList[i].northing, 0); i++;
                                            GL.Vertex3(triList[i].easting, triList[i].northing, 0); i++;
                                            if (count2 - i <= (mipmap + 2)) step = 0;//too small to mipmap it
                                        }
                                    }
                                    else { for (int i = 1; i < count2; i++) GL.Vertex3(triList[i].easting, triList[i].northing, 0); }
                                    GL.End();

                                    if (IsSectionLinesOn)
                                    {
                                        //highlight lines
                                        GL.Color4(0.2, 0.2, 0.2, 1.0);
                                        GL.Begin(PrimitiveType.LineStrip);

                                        //if large enough patch and camera zoomed out, fake mipmap the patches, skip triangles
                                        if (count2 >= (mipmap + 2))
                                        {
                                            int step = mipmap;
                                            for (int i = 1; i < count2; i += step + 2)
                                            {
                                                GL.Vertex3(triList[i].easting, triList[i].northing, 0);
                                                if (count2 - i <= (mipmap + 2)) step = 0;//too small to mipmap it
                                            }
                                        }
                                        else { for (int i = 1; i < count2; i += 2) GL.Vertex3(triList[i].easting, triList[i].northing, 0); }
                                        GL.End();

                                        GL.Begin(PrimitiveType.LineStrip);
                                        //if large enough patch and camera zoomed out, fake mipmap the patches, skip triangles
                                        if (count2 >= (mipmap + 2))
                                        {
                                            int step = mipmap;
                                            for (int i = 2; i < count2; i += step + 2)
                                            {
                                                GL.Vertex3(triList[i].easting, triList[i].northing, 0);
                                                if (count2 - i <= (mipmap + 2)) step = 0;//too small to mipmap it
                                            }
                                        }
                                        else { for (int i = 2; i < count2; i += 2) GL.Vertex3(triList[i].easting, triList[i].northing, 0); }
                                        GL.End();
                                    }

                                    if (IsDirectionMarkers)
                                    {
                                        if (triList.Count > 42)
                                        {
                                            double headz =
                                                Math.Atan2(triList[39].easting - triList[37].easting, triList[39].northing - triList[37].northing);

                                            left = new vec2(
                                                (triList[37].easting + factor * (triList[38].easting - triList[37].easting)),
                                                (triList[37].northing + factor * (triList[38].northing - triList[37].northing)));

                                            factor = 1 - factor;

                                            right = new vec2(
                                                (triList[37].easting + factor * (triList[38].easting - triList[37].easting)),
                                                (triList[37].northing + factor * (triList[38].northing - triList[37].northing)));

                                            double disst = glm.Distance(left, right);
                                            disst *= 1.5;

                                            ptTip = new vec2((left.easting + right.easting) / 2, (left.northing + right.northing) / 2);

                                            ptTip = new vec2(ptTip.easting + (Math.Sin(headz) * disst), ptTip.northing + (Math.Cos(headz) * disst));

                                            GL.Color4((byte)(255 - triList[0].easting), (byte)(255 - triList[0].northing), (byte)(255 - triList[0].heading), (byte)150);
                                            //GL.LineWidth(3.0f);

                                            GL.Begin(PrimitiveType.Triangles);
                                            GL.Vertex3(left.easting, left.northing, 0);
                                            GL.Vertex3(right.easting, right.northing, 0);

                                            GL.Color4(0.85, 0.85, 1, 1.0);
                                            GL.Vertex3(ptTip.easting, ptTip.northing, 0);
                                            GL.End();
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // the follow up to sections patches
                    int patchCount = 0;

                    if (appModel.patchCounter > 0)
                    {
                        if (IsDay) GL.Color4(appModel.SectionColorDay.R, appModel.SectionColorDay.G, appModel.SectionColorDay.B, (byte)152);
                        else GL.Color4(appModel.SectionColorDay.R, appModel.SectionColorDay.G, appModel.SectionColorDay.B, (byte)(76));

                        for (int j = 0; j < triStrip.Count; j++)
                        {
                            if (triStrip[j].isDrawing)
                            {
                                if (tool.isMultiColoredSections)
                                {
                                    // [XPLAT] tool.secColors migrated from System.Drawing.Color[] to AgOpenGPS.Core.Models.ColorRgba[];
                                    // ColorRgba exposes Red/Green/Blue (the explicit Color->ColorRgba operator maps R->Red, G->Green, B->Blue),
                                    // so these byte values are identical to the former .R/.G/.B and visual parity is preserved.
                                    if (IsDay) GL.Color4(tool.secColors[j].Red, tool.secColors[j].Green, tool.secColors[j].Blue, (byte)152);
                                    else GL.Color4(tool.secColors[j].Red, tool.secColors[j].Green, tool.secColors[j].Blue, (byte)(76));
                                }
                                patchCount = triStrip[j].patchList.Count - 1;

                                if (patchCount > -1)
                                {
                                    try
                                    {
                                        //draw the triangle in each triangle strip
                                        GL.Begin(PrimitiveType.TriangleStrip);

                                        //left side of triangle
                                        vec2 pt = new vec2((getCosSectionHeading() * section[triStrip[j].currentStartSectionNum].positionLeft) + ToolPos.easting,
                                                (getSinSectionHeading() * section[triStrip[j].currentStartSectionNum].positionLeft) + ToolPos.northing);

                                        GL.Vertex3(pt.easting, pt.northing, 0);

                                        //Right side of triangle
                                        pt = new vec2((getCosSectionHeading() * section[triStrip[j].currentEndSectionNum].positionRight) + ToolPos.easting,
                                           (getSinSectionHeading() * section[triStrip[j].currentEndSectionNum].positionRight) + ToolPos.northing);

                                        GL.Vertex3(pt.easting, pt.northing, 0);

                                        int last = triStrip[j].patchList[patchCount].Count;

                                        GL.Vertex3(triStrip[j].patchList[patchCount][last - 2].easting, triStrip[j].patchList[patchCount][last - 2].northing, 0);
                                        GL.Vertex3(triStrip[j].patchList[patchCount][last - 1].easting, triStrip[j].patchList[patchCount][last - 1].northing, 0);
                                        GL.End();
                                    }
                                    catch
                                    { }
                                }
                            }
                        }
                    }

                    if (tram.displayMode != 0) tram.DrawTram();

                    GL.PolygonMode(MaterialFace.Front, PolygonMode.Fill);

                    #endregion

                    #region Boundaries

                    if (bnd.bndList.Count > 0 || bnd.isBndBeingMade == true)
                    {
                        //draw Boundaries
                        bnd.DrawFenceLines();

                        GL.LineWidth(getAbLineWidth());

                        //draw the turnLines
                        if (yt.isYouTurnBtnOn && !getIsContourBtnOn())
                        {
                            GL.LineWidth(getAbLineWidth() * 3);
                            GL.Color4(0, 0, 0, 0.80f);

                            for (int i = 0; i < bnd.bndList.Count; i++)
                            {
                                bnd.bndList[i].turnLine.DrawPolygon();
                            }

                            GL.Color3(0.76f, 0.6f, 0.95f);
                            GL.LineWidth(getAbLineWidth());
                            for (int i = 0; i < bnd.bndList.Count; i++)
                            {
                                bnd.bndList[i].turnLine.DrawPolygon();
                            }
                        }

                        //Draw headland
                        if (bnd.isHeadlandOn && bnd.bndList.Count > 0)
                        {
                            GL.LineWidth(getAbLineWidth() * 3);

                            GL.Color4(0, 0, 0, 0.80f);
                            bnd.bndList[0].hdLine.DrawPolygon();

                            GL.LineWidth(getAbLineWidth());
                            GL.Color4(0.960f, 0.96232f, 0.30f, 1.0f);
                            bnd.bndList[0].hdLine.DrawPolygon();
                        }
                    }

                    #endregion

                    #region Guidance Lines

                    // [XPLAT] The whole guidance-line region (contour line, AB/curve ghost lines, line-creation
                    // previews, recorded-path + Dubins) is drawn by the injected delegate, which is wired by the
                    // composition root over ct/trk/ABLine/curve/recPath — none of which are collaborators here.
                    drawGuidanceLines();

                    #endregion

                    #region Flags

                    // [XPLAT] The whole flags overlay region (DrawFlags() over the CFlag list, plus the dashed
                    // direct line from the pivot to the selected flag) is drawn by the injected delegate; the
                    // CFlag list is not a collaborator of this service.
                    drawFlagsOverlay();

                    #endregion

                    #region Vehicle and Implement

                    //draw the vehicle/implement
                    GL.PushMatrix();
                    {
                        tool.DrawTool();
                        vehicle.DrawVehicle();
                    }
                    GL.PopMatrix();

                    if (camera.camSetDistance > -250)
                    {
                        if (trk.idx > -1 && !IsStanleyUsed)
                        {
                            ColorRgba foregroundColor =
                                Math.Abs(vehicle.modeActualXTE) > 0.15 ?
                                Colors.GoalPointColor :
                                Colors.Green;
                            vec2 goalPoint = trk.gArr[trk.idx].mode == TrackMode.AB ? getGoalPointAB() : getGoalPointCu();
                            GeoCoord goalCoord = goalPoint.ToGeoCoord();

                            // background layer
                            GLW.SetPointSize(16.0f);
                            GLW.SetColor(Colors.Black);
                            GLW.DrawPoint(goalCoord);
                            // foreground layer
                            GLW.SetPointSize(8.0f);
                            GLW.SetColor(foregroundColor);
                            GLW.DrawPoint(goalCoord);
                        }
                    }

                    #endregion

                    #region 2D Ortho

                    // 2D Ortho ---------------------------------------////////-------------------------------------------------
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.PushMatrix();
                    GL.LoadIdentity();

                    //negative and positive on width, 0 at top to bottom ortho view
                    GL.Ortho(-(double)OglWidth / 2, (double)OglWidth / 2, (double)OglHeight, 0, -1, 1);

                    //  Create the appropriate modelview matrix.
                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.PushMatrix();
                    GL.LoadIdentity();

                    //LightBar if AB Line is set and turned on or contour
                    if (IsLightBarNotSteerBar)
                    {
                        DrawLightBarText();
                    }
                    else
                    {
                        if (IsLightbarOn) DrawSteerBarText();
                    }

                    if (trk.idx > -1 && !getIsContourBtnOn()) DrawTrackInfo();

                    if (bnd.bndList.Count > 0 && yt.isYouTurnBtnOn) DrawUTurnBtn();

                    if ((appModel.isBtnAutoSteerOn || yt.isYouTurnBtnOn) && !getIsContourBtnOn()) DrawManUTurnBtn();

                    //if (isCompassOn) DrawCompass();
                    DrawCompassText();

                    if (IsSpeedoOn) DrawSpeedo();

                    DrawSteerCircle();

                    if (tool.isDisplayTramControl && tram.displayMode != 0) { DrawTramMarkers(); }

                    if (vehicle.isHydLiftOn) DrawLiftIndicator();

                    if (appModel.isReverse || IsChangingDirection)
                        DrawReverse();

                    // [XPLAT] RENDER-ONLY RTK overlay (Phase 7). The autosteer-kill decision and the
                    // 1000 ms RTK-recovered debounce that formerly lived here moved to
                    // PositionService.EvaluateRtkRecovery(); this service merely reads the shared
                    // IsRtkAlarmOn flag and draws the "RTK Fix Lost" overlay when the fix is not RTK-fixed.
                    if (IsRtkAlarmOn)
                    {
                        if (pn.fixQuality != 4)
                        {
                            DrawLostRTK();
                        }
                    }

                    if (IsDevelopVersion)
                    {
                        DrawVersion("DEVELOP VERSION");
                    }
                    else if (IsPreRelease)
                    {
                        DrawVersion("Beta Testing v" + SemVer);
                    }

                    if (bnd.isHeadlandOn && appModel.isHeadlandDistanceOn)
                    {
                        DrawHeadlandDistance();
                    }

                    if (pn.age > pn.ageAlarm) DrawAge();

                    //at least one track
                    if (GuideLineCounter > 0) DrawGuidanceLineText();

                    //if hardware messages
                    if (IsHardwareMessages) DrawHardwareMessageText();

                    //just in case
                    GL.Disable(EnableCap.LineStipple);

                    GL.PopMatrix();//  Pop the modelview.

                    ////-------------------------------------------------ORTHO END---------------------------------------

                    #endregion

                    #region Screen Border

                    //  back to the projection and pop it, then back to the model view.
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.PopMatrix();
                    GL.MatrixMode(MatrixMode.Modelview);

                    //reset point size
                    GL.PointSize(1.0f);

                    // --- SCREEN BORDER: draw last so nothing can overdraw it ---
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.PushMatrix();
                    GL.LoadIdentity();
                    GL.Ortho(-(double)OglWidth / 2, (double)OglWidth / 2, (double)OglHeight, 0, -1, 1);

                    GL.MatrixMode(MatrixMode.Modelview);
                    GL.PushMatrix();
                    GL.LoadIdentity();

                    GL.LineWidth(8);
                    GL.Color3(0, 0, 0);

                    if (getIsOutOfBounds())
                    {
                        GL.Color3(1.0, 0.66, 0.33);
                        GL.LineWidth(8);
                    }

                    #endregion

                    #region RTK Alarms

                    if ((IsRtkAlarmOn && getIsRtkAlarming()) || (yt.isYouTurnBtnOn && yt.turnTooCloseTrigger))
                    {
                        if (IsFlashOnOff)
                        {
                            GL.Color3(1.0, 0.25, 0.25);
                            GL.LineWidth(16);
                        }
                        else
                        {
                            GL.Color3(0.8, 0.250, 0.25);
                            GL.LineWidth(16);
                        }
                    }

                    GL.Begin(PrimitiveType.LineLoop);
                    GL.Vertex3(-OglWidth / 2, 0, 0);
                    GL.Vertex3(OglWidth / 2, 0, 0);
                    GL.Vertex3(OglWidth / 2, OglHeight, 0);
                    GL.Vertex3(-OglWidth / 2, OglHeight, 0);
                    GL.End();

                    GL.PopMatrix();
                    GL.MatrixMode(MatrixMode.Projection);
                    GL.PopMatrix();
                    GL.MatrixMode(MatrixMode.Modelview);

                    #endregion

                    GL.Flush();
                    // [XPLAT] oglMain.SwapBuffers() removed — the Avalonia host swaps after EndPaint().

                    if (LeftMouseDownOnOpenGL) MakeFlagMark();
                }
            }
            else
            {
                // [XPLAT] No-GPS fallback: spinning logo. oglMain.MakeCurrent() removed (host-owned).
                GL.Enable(EnableCap.Blend);
                GL.ClearColor(0.122f, 0.1258f, 0.1275f, 1.0f);

                GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
                GL.LoadIdentity();

                GL.Translate(0.0, 0.3, -10);
                //rotate the camera down to look at fix
                GL.Rotate(deadCam, 0.0, 1.0, 0.0);

                deadCam += 5;

                GL.Color4(1.25f, 1.25f, 1.275f, 0.75);
                screenTextures.NoGps.DrawCenteredAroundOrigin(new XyDelta(2.5, -2.5));

                // 2D Ortho ---------------------------------------////////-------------------------------------------------

                GL.MatrixMode(MatrixMode.Projection);
                GL.PushMatrix();
                GL.LoadIdentity();

                //negative and positive on width, 0 at top to bottom ortho view
                GL.Ortho(-(double)OglWidth / 2, (double)OglWidth / 2, (double)OglHeight, 0, -1, 1);

                //  Create the appropriate modelview matrix.
                GL.MatrixMode(MatrixMode.Modelview);
                GL.PushMatrix();
                GL.LoadIdentity();

                GL.Color3(0.98f, 0.98f, 0.70f);

                int edge = -OglWidth / 2 + 10;

                font.DrawText(edge, OglHeight - 80, "<-- AgIO ?");

                GL.Flush();//finish openGL commands
                GL.PopMatrix();//  Pop the modelview.

                ////-------------------------------------------------ORTHO END---------------------------------------

                //  back to the projection and pop it, then back to the model view.
                GL.MatrixMode(MatrixMode.Projection);
                GL.PopMatrix();
                GL.MatrixMode(MatrixMode.Modelview);

                //reset point size
                GL.PointSize(1.0f);

                GL.Flush();
                // [XPLAT] oglMain.SwapBuffers() removed — the Avalonia host swaps after EndPaint().
                // [XPLAT] lblSpeed.Text / lblHz.Text writes removed — the Avalonia view-model owns those labels.
            }

            // [XPLAT] The periodic file-save tail (fileSaveAlwaysCounter / fileSaveCounter /
            // DistanceToFieldOriginCheck / FileSaveSections / FileSaveContour / FileSaveElevation /
            // oglZoom.Refresh) that followed here in the WinForms paint is intentionally NOT ported:
            // field persistence is owned by FieldIoService and the host scan loop, not the renderer.
        }

        // ================================================================================================
        //  Off-screen back-buffer section/look-ahead scan (FROZEN — DOMAIN-AFFECTING). Ported from
        //  oglBack_Load + oglBack_Resize + oglBack_Paint (source L770-1446). PositionService triggers this
        //  (via its RequestBackBufferScan delegate) on the off-screen 500x300 framebuffer the host has bound
        //  and made current. The three lifecycle bodies are merged into one idempotent method: the Load GL
        //  state-setters and the Resize projection are re-applied each call (they are idempotent, so behavior
        //  is identical to the WinForms sequence), and the host-owned oglBack.MakeCurrent()/Width/Height/
        //  SwapBuffers calls are removed.
        //
        //  The green channel is the coverage map: patches are drawn (0,127,0); tram lines 245; boundary 240;
        //  headland 250. The single GL.ReadPixels (PixelFormat.Green) into grnPixels[150001] is then scanned
        //  to set tram.controlByte, the headland-close hydraulics, and every section's
        //  sectionOnRequest/sectionOffRequest/isSectionRequiredOn/isSectionOn/isMappingOn and timers. Finally
        //  SectionService.BuildMachineByte() turns that coverage into the PGN 0xFE/0xEF/0xE5 bytes — the
        //  render->section seam. Every green tag, the rpHeight clamp [8,290], the pixLimit formula and the
        //  two goto labels are preserved verbatim.
        // ================================================================================================

        /// <summary>
        /// [XPLAT] Renders the implement coverage to the off-screen back buffer and scans the green channel
        /// to drive section control (FROZEN — byte-identical to the WinForms <c>oglBack</c> path). Invoked by
        /// the host on the bound 500x300 framebuffer. Ends by calling
        /// <see cref="SectionService.BuildMachineByte"/> so the machine PGN bytes are assembled from the
        /// freshly-scanned coverage before PositionService transmits them.
        /// </summary>
        public void RenderBackBufferAndScan()
        {
            // ---- oglBack_Load (idempotent GL state; host owns MakeCurrent and the 500x300 FBO size) ----
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

            // ---- oglBack_Resize (off-screen perspective — constants FROZEN) ----
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Viewport(0, 0, 500, 300);
            Matrix4 backMat = Matrix4.CreatePerspectiveFieldOfView(0.06f, 1.6666666666f, 50.0f, 520.0f);
            GL.LoadMatrix(ref backMat);
            GL.MatrixMode(MatrixMode.Modelview);

            // ---- oglBack_Paint ----
            GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
            GL.LoadIdentity();                  // Reset The View

            //back the camera up
            GL.Translate(0, 0, -500);

            //rotate camera so heading matched fix heading in the world
            GL.Rotate(glm.toDegrees(ToolPos.heading), 0, 0, 1);

            GL.Translate(-ToolPos.easting - Math.Sin(ToolPos.heading) * 15,
                -ToolPos.northing - Math.Cos(ToolPos.heading) * 15,
                0);

            #region Draw to Back Buffer

            //patch color
            GL.Color3((byte)0, (byte)127, (byte)0);

            //to draw or not the triangle patch
            bool isDraw;

            double pivEplus = ToolPos.easting + 50;
            double pivEminus = ToolPos.easting - 50;
            double pivNplus = ToolPos.northing + 50;
            double pivNminus = ToolPos.northing - 50;

            //draw patches j= # of sections
            for (int j = 0; j < triStrip.Count; j++)
            {
                //every time the section turns off and on is a new patch
                int patchCount = triStrip[j].patchList.Count;

                if (patchCount > 0)
                {
                    //for every new chunk of patch
                    foreach (var triList in triStrip[j].patchList)
                    {
                        isDraw = false;
                        int count2 = triList.Count;
                        for (int i = 1; i < count2; i += 3)
                        {
                            //determine if point is in frustum or not
                            if (triList[i].easting > pivEplus)
                                continue;
                            if (triList[i].easting < pivEminus)
                                continue;
                            if (triList[i].northing > pivNplus)
                                continue;
                            if (triList[i].northing < pivNminus)
                                continue;

                            //point is in frustum so draw the entire patch
                            isDraw = true;
                            break;
                        }

                        if (isDraw)
                        {
                            //draw the triangles in each triangle strip
                            GL.Begin(PrimitiveType.TriangleStrip);
                            for (int i = 1; i < count2; i++) GL.Vertex3(triList[i].easting, triList[i].northing, 0);
                            GL.End();
                        }
                    }
                }
            }

            //draw 245 green for the tram tracks

            if (tool.isDisplayTramControl && tram.displayMode != 0 && (trk.idx > -1))
            {
                GL.Color3((byte)0, (byte)245, (byte)0);
                GL.LineWidth(4);

                if ((tram.displayMode == 1 || tram.displayMode == 2))
                {
                    for (int i = 0; i < tram.tramList.Count; i++)
                    {
                        GL.Begin(PrimitiveType.LineStrip);
                        for (int h = 0; h < tram.tramList[i].Count; h++)
                            GL.Vertex3(tram.tramList[i][h].easting, tram.tramList[i][h].northing, 0);
                        GL.End();
                    }
                }

                if (tram.displayMode == 1 || tram.displayMode == 3)
                {
                    //boundary tram list
                    GL.Begin(PrimitiveType.LineStrip);
                    for (int h = 0; h < tram.tramBndOuterArr.Count; h++)
                        GL.Vertex3(tram.tramBndOuterArr[h].easting, tram.tramBndOuterArr[h].northing, 0);
                    for (int h = 0; h < tram.tramBndInnerArr.Count; h++)
                        GL.Vertex3(tram.tramBndInnerArr[h].easting, tram.tramBndInnerArr[h].northing, 0);
                    GL.End();
                }
            }

            //draw 240 green for boundary
            if (bnd.bndList.Count > 0)
            {
                ////draw the bnd line
                if (bnd.bndList[0].fenceLine.Count > 3)
                {
                    GL.LineWidth(3);
                    GL.Color3((byte)0, (byte)240, (byte)0);
                    bnd.bndList[0].fenceLine.DrawPolygon();
                }

                //draw 250 green for the headland
                if (bnd.isHeadlandOn)
                {
                    GL.LineWidth(3);
                    GL.Color3((byte)0, (byte)250, (byte)0);
                    bnd.bndList[0].hdLine.DrawPolygon();
                }
            }

            //finish it up - we need to read the ram of video card
            GL.Flush();

            #endregion

            #region Lookahead

            //determine where the tool is wrt to headland
            if (bnd.isHeadlandOn) bnd.WhereAreToolCorners();

            //set the look ahead for hyd Lift in pixels per second
            vehicle.hydLiftLookAheadDistanceLeft = tool.farLeftSpeed * vehicle.hydLiftLookAheadTime * 10;
            vehicle.hydLiftLookAheadDistanceRight = tool.farRightSpeed * vehicle.hydLiftLookAheadTime * 10;

            if (vehicle.hydLiftLookAheadDistanceLeft > 200) vehicle.hydLiftLookAheadDistanceLeft = 200;
            if (vehicle.hydLiftLookAheadDistanceRight > 200) vehicle.hydLiftLookAheadDistanceRight = 200;

            tool.lookAheadDistanceOnPixelsLeft = tool.farLeftSpeed * tool.lookAheadOnSetting * 10;
            tool.lookAheadDistanceOnPixelsRight = tool.farRightSpeed * tool.lookAheadOnSetting * 10;

            if (tool.lookAheadDistanceOnPixelsLeft > 200) tool.lookAheadDistanceOnPixelsLeft = 200;
            if (tool.lookAheadDistanceOnPixelsRight > 200) tool.lookAheadDistanceOnPixelsRight = 200;

            tool.lookAheadDistanceOffPixelsLeft = tool.farLeftSpeed * tool.lookAheadOffSetting * 10;
            tool.lookAheadDistanceOffPixelsRight = tool.farRightSpeed * tool.lookAheadOffSetting * 10;

            if (tool.lookAheadDistanceOffPixelsLeft > 160) tool.lookAheadDistanceOffPixelsLeft = 160;
            if (tool.lookAheadDistanceOffPixelsRight > 160) tool.lookAheadDistanceOffPixelsRight = 160;

            //determine if section is in boundary and headland using the section left/right positions
            bool isLeftIn = true, isRightIn = true;

            if (bnd.bndList.Count > 0)
            {
                for (int j = 0; j < tool.numOfSections; j++)
                {
                    //only one first left point, the rest are all rights moved over to left
                    isLeftIn = j == 0 ? bnd.IsPointInsideFenceArea(section[j].leftPoint) : isRightIn;
                    isRightIn = bnd.IsPointInsideFenceArea(section[j].rightPoint);

                    if (!tool.isSectionOffWhenOut)
                    {
                        //merge the two sides into in or out
                        if (isLeftIn || isRightIn) section[j].isInBoundary = true;
                        else section[j].isInBoundary = false;
                    }
                    else
                    {
                        //merge the two sides into in or out
                        if (!isLeftIn || !isRightIn) section[j].isInBoundary = false;
                        else section[j].isInBoundary = true;
                    }
                }
            }

            //determine farthest ahead lookahead - is the height of the readpixel line
            double rpHeight = 0;
            double rpOnHeight = 0;
            double rpToolHeight = 0;

            //pick the larger side
            if (vehicle.hydLiftLookAheadDistanceLeft > vehicle.hydLiftLookAheadDistanceRight) rpToolHeight = vehicle.hydLiftLookAheadDistanceLeft;
            else rpToolHeight = vehicle.hydLiftLookAheadDistanceRight;

            if (tool.lookAheadDistanceOnPixelsLeft > tool.lookAheadDistanceOnPixelsRight) rpOnHeight = tool.lookAheadDistanceOnPixelsLeft;
            else rpOnHeight = tool.lookAheadDistanceOnPixelsRight;

            isHeadlandClose = false;

            #endregion

            #region Back buffer scan

            //clamp the height after looking way ahead, this is for switching off super section only
            rpOnHeight = Math.Abs(rpOnHeight);
            rpToolHeight = Math.Abs(rpToolHeight);

            //10 % min is required for overlap, otherwise it never would be on.
            int pixLimit = (int)((double)(section[0].rpSectionWidth * rpOnHeight) / (double)(5.0));

            if ((rpOnHeight < rpToolHeight && bnd.isHeadlandOn)) rpHeight = rpToolHeight + 2;
            else rpHeight = rpOnHeight + 2;

            if (rpHeight > 290) rpHeight = 290;
            if (rpHeight < 8) rpHeight = 8;

            //read the whole block of pixels up to max lookahead, one read only
            GL.ReadPixels(tool.rpXPosition, 0, tool.rpWidth, (int)rpHeight, OpenTK.Graphics.OpenGL.PixelFormat.Green, PixelType.UnsignedByte, grnPixels);

            //determine if headland is in read pixel buffer left middle and right.
            int start = 0, end = 0, tagged = 0, totalPixel = 0;

            //slope of the look ahead line
            double mOn = 0, mOff = 0;

            double theta = mOn = (tool.lookAheadDistanceOnPixelsRight - tool.lookAheadDistanceOnPixelsLeft) / tool.rpWidth;
            double deg = glm.toDegrees(Math.Atan(theta));

            //tram and hydraulics
            if (tram.displayMode > 0 && tool.width > vehicle.VehicleConfig.TrackWidth)
            {
                tram.controlByte = 0;
                //1 pixels in is there a tram line?
                if (tram.isOuter)
                {
                    if (grnPixels[tool.rpWidth - (int)(tram.halfWheelTrack * 10)] == 245 || tram.isRightManualOn) tram.controlByte += 1;
                    if (grnPixels[(int)(tram.halfWheelTrack * 10)] == 245 || tram.isLeftManualOn) tram.controlByte += 2;
                }
                else
                {
                    if (grnPixels[tool.rpWidth / 2 + (int)(tram.halfWheelTrack * 10)] == 245 || tram.isRightManualOn) tram.controlByte += 1;
                    if (grnPixels[tool.rpWidth / 2 - (int)(tram.halfWheelTrack * 10)] == 245 || tram.isLeftManualOn) tram.controlByte += 2;
                }
            }
            else tram.controlByte = 0;

            //determine if in or out of headland, do hydraulics if on
            if (bnd.isHeadlandOn)
            {
                //calculate the slope
                double m = (vehicle.hydLiftLookAheadDistanceRight - vehicle.hydLiftLookAheadDistanceLeft) / tool.rpWidth;
                int height = 1;

                for (int pos = 0; pos < tool.rpWidth; pos++)
                {
                    height = (int)(vehicle.hydLiftLookAheadDistanceLeft + (m * pos)) - 1;
                    for (int a = pos; a < height * tool.rpWidth; a += tool.rpWidth)
                    {
                        if (grnPixels[a] == 250)
                        {
                            isHeadlandClose = true;
                            goto GetOutTool;
                        }
                    }
                }

            GetOutTool: //goto

                //is the tool completely in the headland or not
                bnd.isToolInHeadland = bnd.isToolOuterPointsInHeadland && !isHeadlandClose;

                bnd.SetHydPosition();
            }

            #endregion

            #region Section Control

            ///////////////////////////////////////////   Section control        ssssssssssssssssssssss

            int endHeight = 1, startHeight = 1;

            if (bnd.isHeadlandOn && bnd.isSectionControlledByHeadland) bnd.WhereAreToolLookOnPoints();

            for (int j = 0; j < tool.numOfSections; j++)
            {
                //Off or too slow or going backwards
                if (section[j].sectionBtnState == btnStates.Off || appModel.avgSpeed < vehicle.slowSpeedCutoff || section[j].speedPixels < 0)
                {
                    section[j].sectionOnRequest = false;
                    section[j].sectionOffRequest = true;

                    // Manual on, force the section On
                    if (section[j].sectionBtnState == btnStates.On)
                    {
                        section[j].sectionOnRequest = true;
                        section[j].sectionOffRequest = false;
                        continue;
                    }
                    continue;
                }

                // Manual on, force the section On
                if (section[j].sectionBtnState == btnStates.On)
                {
                    section[j].sectionOnRequest = true;
                    section[j].sectionOffRequest = false;
                    continue;
                }

                //AutoSection - If any nowhere applied, send OnRequest, if its all green send an offRequest
                section[j].isSectionRequiredOn = false;

                //calculate the slopes of the lines
                mOn = (tool.lookAheadDistanceOnPixelsRight - tool.lookAheadDistanceOnPixelsLeft) / tool.rpWidth;
                mOff = (tool.lookAheadDistanceOffPixelsRight - tool.lookAheadDistanceOffPixelsLeft) / tool.rpWidth;

                start = section[j].rpSectionPosition - section[0].rpSectionPosition;
                end = section[j].rpSectionWidth - 1 + start;

                if (end >= tool.rpWidth)
                    end = tool.rpWidth - 1;

                totalPixel = 1;
                tagged = 0;

                for (int pos = start; pos <= end; pos++)
                {
                    startHeight = (int)(tool.lookAheadDistanceOffPixelsLeft + (mOff * pos)) * tool.rpWidth + pos;
                    endHeight = (int)(tool.lookAheadDistanceOnPixelsLeft + (mOn * pos)) * tool.rpWidth + pos;

                    for (int a = startHeight; a <= endHeight; a += tool.rpWidth)
                    {
                        totalPixel++;
                        if (grnPixels[a] == 0) tagged++;
                    }
                }

                //determine if meeting minimum coverage
                section[j].isSectionRequiredOn = ((tagged * 100) / totalPixel > (100 - tool.minCoverage));

                //logic if in or out of boundaries or headland
                if (bnd.bndList.Count > 0)
                {
                    //if out of boundary, turn it off
                    if (!section[j].isInBoundary)
                    {
                        section[j].isSectionRequiredOn = false;
                        section[j].sectionOffRequest = true;
                        section[j].sectionOnRequest = false;
                        section[j].sectionOffTimer = 0;
                        section[j].sectionOnTimer = 0;
                        continue;
                    }
                    else
                    {
                        //is headland coming up
                        if (bnd.isHeadlandOn && bnd.isSectionControlledByHeadland)
                        {
                            bool isHeadlandInLookOn = false;

                            //is headline in off to on area
                            mOn = (tool.lookAheadDistanceOnPixelsRight - tool.lookAheadDistanceOnPixelsLeft) / tool.rpWidth;
                            mOff = (tool.lookAheadDistanceOffPixelsRight - tool.lookAheadDistanceOffPixelsLeft) / tool.rpWidth;

                            start = section[j].rpSectionPosition - section[0].rpSectionPosition;

                            end = section[j].rpSectionWidth - 1 + start;

                            if (end >= tool.rpWidth)
                                end = tool.rpWidth - 1;

                            tagged = 0;

                            for (int pos = start; pos <= end; pos++)
                            {
                                startHeight = (int)(tool.lookAheadDistanceOffPixelsLeft + (mOff * pos)) * tool.rpWidth + pos;
                                endHeight = (int)(tool.lookAheadDistanceOnPixelsLeft + (mOn * pos)) * tool.rpWidth + pos;

                                for (int a = startHeight; a <= endHeight; a += tool.rpWidth)
                                {
                                    if (a < 0)
                                        mOn = 0;
                                    if (grnPixels[a] == 250)
                                    {
                                        isHeadlandInLookOn = true;
                                        goto GetOutHdOn;
                                    }
                                }
                            }
                        GetOutHdOn:

                            //determine if look ahead points are completely in headland
                            if (section[j].isSectionRequiredOn && section[j].isLookOnInHeadland && !isHeadlandInLookOn)
                            {
                                section[j].isSectionRequiredOn = false;
                                section[j].sectionOffRequest = true;
                                section[j].sectionOnRequest = false;
                            }

                            if (section[j].isSectionRequiredOn && !section[j].isLookOnInHeadland && isHeadlandInLookOn)
                            {
                                section[j].isSectionRequiredOn = true;
                                section[j].sectionOffRequest = false;
                                section[j].sectionOnRequest = true;
                            }
                        }
                    }
                }

                //global request to turn on section
                section[j].sectionOnRequest = section[j].isSectionRequiredOn;
                section[j].sectionOffRequest = !section[j].sectionOnRequest;

            }  // end of go thru all sections "for"

            //Set all the on and off times based from on off section requests
            for (int j = 0; j < tool.numOfSections; j++)
            {
                //SECTION timers

                if (section[j].sectionOnRequest)
                    section[j].isSectionOn = true;

                //turn off delay
                if (tool.turnOffDelay > 0)
                {
                    if (!section[j].sectionOffRequest) section[j].sectionOffTimer = (int)(GpsHz * tool.turnOffDelay);

                    if (section[j].sectionOffTimer > 0) section[j].sectionOffTimer--;

                    if (section[j].sectionOffRequest && section[j].sectionOffTimer == 0)
                    {
                        if (section[j].isSectionOn) section[j].isSectionOn = false;
                    }
                }
                else
                {
                    if (section[j].sectionOffRequest)
                        section[j].isSectionOn = false;
                }

                //Mapping timers
                if (section[j].sectionOnRequest && !section[j].isMappingOn && section[j].mappingOnTimer == 0)
                {
                    section[j].mappingOnTimer = (int)(tool.lookAheadOnSetting * (GpsHz) - 1);
                }
                else if (section[j].sectionOnRequest && section[j].isMappingOn && section[j].mappingOffTimer > 1)
                {
                    section[j].mappingOffTimer = 0;
                    section[j].mappingOnTimer = (int)(tool.lookAheadOnSetting * (GpsHz) - 1);
                }

                if (tool.lookAheadOffSetting > 0)
                {
                    if (section[j].sectionOffRequest && section[j].isMappingOn && section[j].mappingOffTimer == 0)
                    {
                        section[j].mappingOffTimer = (int)(tool.lookAheadOffSetting * (GpsHz) + 4);
                    }
                }
                else if (tool.turnOffDelay > 0)
                {
                    if (section[j].sectionOffRequest && section[j].isMappingOn && section[j].mappingOffTimer == 0)
                        section[j].mappingOffTimer = (int)(tool.turnOffDelay * GpsHz);
                }
                else
                {
                    section[j].mappingOffTimer = 0;
                }

                //MAPPING - Not the making of triangle patches - only status - on or off
                if (section[j].sectionOnRequest)
                {
                    section[j].mappingOffTimer = 0;
                    if (section[j].mappingOnTimer > 1)
                        section[j].mappingOnTimer--;
                    else
                    {
                        section[j].isMappingOn = true;
                    }
                }

                if (section[j].sectionOffRequest)
                {
                    section[j].mappingOnTimer = 0;
                    if (section[j].mappingOffTimer > 1)
                        section[j].mappingOffTimer--;
                    else
                    {
                        section[j].isMappingOn = false;
                    }
                }
            }

            //Checks the workswitch or steerSwitch if required
            // [XPLAT] the gate (ahrs.isAutoSteerAuto || mc.isRemoteWorkSystemOn) and the
            // mc.CheckWorkAndSteerSwitch() call are encapsulated in the injected delegate (ahrs/mc are not
            // collaborators of this service).
            checkWorkAndSteerSwitch();

            // Check ISOBUS for actual section states
            // Only override if TC has active clients with section control capability (numberOfSections > 0)
            // For 0-section implements, let AOG control sections normally
            if (isobus.IsAlive() && isobus.HasActiveClients)
            {
                for (int j = 0; j < tool.numOfSections; j++)
                {
                    if (isobus.IsSectionOn(j))
                        section[j].isMappingOn = true;
                    else
                        section[j].isMappingOn = false;
                }
            }

            // check if any sections have changed status
            number = 0;

            for (int j = 0; j < tool.numOfSections; j++)
            {
                if (section[j].isMappingOn)
                {
                    number |= 1ul << j;
                }
            }

            //there has been a status change of section on/off
            if (number != lastNumber)
            {
                int sectionOnOffZones = 0, patchingZones = 0;

                //everything off
                if (number == 0)
                {
                    for (int j = 0; j < triStrip.Count; j++)
                    {
                        if (triStrip[j].isDrawing)
                            triStrip[j].TurnMappingOff();
                    }
                }
                else if (!tool.isMultiColoredSections)
                {
                    //set the start and end positions from section points
                    for (int j = 0; j < tool.numOfSections; j++)
                    {
                        //skip till first mapping section
                        if (!section[j].isMappingOn) continue;

                        //do we need more patches created
                        if (triStrip.Count < sectionOnOffZones + 1)
                            triStrip.Add(createPatch());

                        //set this strip start edge to edge of this section
                        triStrip[sectionOnOffZones].newStartSectionNum = j;

                        while ((j + 1) < tool.numOfSections && section[j + 1].isMappingOn)
                        {
                            j++;
                        }

                        //set the edge of this section to be end edge of strp
                        triStrip[sectionOnOffZones].newEndSectionNum = j;
                        sectionOnOffZones++;
                    }

                    //countExit current patch strips being made
                    for (int j = 0; j < triStrip.Count; j++)
                    {
                        if (triStrip[j].isDrawing) patchingZones++;
                    }

                    //tests for creating new strips or continuing
                    bool isOk = (patchingZones == sectionOnOffZones && sectionOnOffZones < 3);

                    if (isOk)
                    {
                        for (int j = 0; j < sectionOnOffZones; j++)
                        {
                            if (triStrip[j].newStartSectionNum > triStrip[j].currentEndSectionNum
                                || triStrip[j].newEndSectionNum < triStrip[j].currentStartSectionNum)
                                isOk = false;
                        }
                    }

                    if (isOk)
                    {
                        for (int j = 0; j < sectionOnOffZones; j++)
                        {
                            if (triStrip[j].newStartSectionNum != triStrip[j].currentStartSectionNum
                                || triStrip[j].newEndSectionNum != triStrip[j].currentEndSectionNum)
                            {
                                //if (tool.isSectionsNotZones)
                                {
                                    triStrip[j].AddMappingPoint(0);
                                }

                                triStrip[j].currentStartSectionNum = triStrip[j].newStartSectionNum;
                                triStrip[j].currentEndSectionNum = triStrip[j].newEndSectionNum;
                                triStrip[j].AddMappingPoint(0);
                            }
                        }
                    }
                    else
                    {
                        //too complicated, just make new strips
                        for (int j = 0; j < triStrip.Count; j++)
                        {
                            if (triStrip[j].isDrawing)
                                triStrip[j].TurnMappingOff();
                        }

                        for (int j = 0; j < sectionOnOffZones; j++)
                        {
                            triStrip[j].currentStartSectionNum = triStrip[j].newStartSectionNum;
                            triStrip[j].currentEndSectionNum = triStrip[j].newEndSectionNum;
                            triStrip[j].TurnMappingOn(0);
                        }
                    }
                }
                else if (tool.isMultiColoredSections) //could be else only but this is more clear
                {
                    //set the start and end positions from section points
                    for (int j = 0; j < tool.numOfSections; j++)
                    {
                        //do we need more patches created
                        if (triStrip.Count < sectionOnOffZones + 1)
                            triStrip.Add(createPatch());

                        //set this strip start edge to edge of this section
                        triStrip[sectionOnOffZones].newStartSectionNum = j;

                        //set the edge of this section to be end edge of strp
                        triStrip[sectionOnOffZones].newEndSectionNum = j;
                        sectionOnOffZones++;

                        if (!section[j].isMappingOn)
                        {
                            if (triStrip[j].isDrawing)
                                triStrip[j].TurnMappingOff();
                        }
                        else
                        {
                            triStrip[j].currentStartSectionNum = triStrip[j].newStartSectionNum;
                            triStrip[j].currentEndSectionNum = triStrip[j].newEndSectionNum;
                            triStrip[j].TurnMappingOn(j);
                        }
                    }
                }

                lastNumber = number;
            }

            #endregion

            //send the byte out to section machines
            // [XPLAT] render->section seam: turn the freshly-scanned coverage into the machine PGN bytes
            // via the injected SectionService (was the FormGPS-local BuildMachineByte()).
            sectionService.BuildMachineByte();

            //this is the end of the "frame". Now we wait for next NMEA sentence with a valid fix.
        }

        // ================================================================================================
        //  Zoom-overview overlap calculation (ported from oglZoom_Load + oglZoom_Resize + oglZoom_Paint,
        //  source L1446-1721). Renders the whole field's coverage into the small zoom-overview surface and
        //  reads back the green channel to estimate worked-area / overlap. The three lifecycle bodies are
        //  merged idempotently; the host owns MakeCurrent and the surface. The large commented-out dead
        //  AB/curve/fence overview block and the DistanceToFieldOriginCheck() (a YesMessageBox dialog) are
        //  intentionally NOT ported. The width x width ReadPixels quirk is preserved verbatim.
        // ================================================================================================

        /// <summary>
        /// [XPLAT] Renders the field coverage to the zoom-overview surface and computes the worked-area /
        /// overlap-percent from the green-channel pixel histogram (FROZEN math). The results
        /// (<c>actualAreaCovered</c>, <c>overlapPercent</c>) are pushed through the injected
        /// <see cref="setOverlapResult"/> sink (was direct writes on CFieldData), and the worked-area total
        /// is read through <see cref="getWorkedAreaTotal"/>. Only runs when a field job is started.
        /// </summary>
        public void RenderZoomOverviewAndCalcOverlap()
        {
            if (appModel.isJobStarted)
            {
                // ---- oglZoom_Load (idempotent GL state; host owns MakeCurrent + the surface) ----
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Back);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.ClearColor(0, 0, 0, 1.0f);

                // ---- oglZoom_Resize (overview perspective — constants FROZEN: 58-degree view) ----
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadIdentity();
                GL.Viewport(0, 0, OglZoomWidth, OglZoomHeight);
                //58 degrees view
                Matrix4 zoomMat = Matrix4.CreatePerspectiveFieldOfView(1.0f, 1.0f, 100.0f, 5000.0f);
                GL.LoadMatrix(ref zoomMat);
                GL.MatrixMode(MatrixMode.Modelview);

                // ---- oglZoom_Paint ----
                GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
                GL.LoadIdentity();                  // Reset The View

                CalculateMinMax();
                //back the camera up
                GL.Translate(0, 0, -maxFieldDistance);
                GL.Enable(EnableCap.Blend);

                //translate to that spot in the world
                GL.Translate(-fieldCenterX, -fieldCenterY, 0);

                GL.Color4(0.5, 0.5, 0.5, 0.5);
                //draw patches
                int count2;

                for (int j = 0; j < triStrip.Count; j++)
                {
                    //every time the section turns off and on is a new patch
                    int patchCount = triStrip[j].patchList.Count;

                    if (patchCount > 0)
                    {
                        //for every new chunk of patch
                        foreach (var triList in triStrip[j].patchList)
                        {
                            //draw the triangle in each triangle strip
                            GL.Begin(PrimitiveType.TriangleStrip);
                            count2 = triList.Count;
                            int mipmap = 2;

                            //if large enough patch and camera zoomed out, fake mipmap the patches, skip triangles
                            if (count2 >= (mipmap))
                            {
                                int step = mipmap;
                                for (int i = 1; i < count2; i += step)
                                {
                                    GL.Vertex3(triList[i].easting, triList[i].northing, 0); i++;
                                    GL.Vertex3(triList[i].easting, triList[i].northing, 0); i++;

                                    //too small to mipmap it
                                    if (count2 - i <= (mipmap))
                                        break;
                                }
                            }

                            else
                            {
                                for (int i = 1; i < count2; i++) GL.Vertex3(triList[i].easting, triList[i].northing, 0);
                            }
                            GL.End();

                        }
                    }
                } //end of section patches

                GL.Flush();

                int grnHeight = OglZoomHeight;
                int grnWidth = OglZoomWidth;
                byte[] overPix = new byte[grnHeight * grnWidth + 1];

                GL.ReadPixels(0, 0, grnWidth, grnWidth, OpenTK.Graphics.OpenGL.PixelFormat.Green, PixelType.UnsignedByte, overPix);

                int once = 0;
                int twice = 0;
                int more = 0;
                int level = 0;
                double total = 0;
                double total2 = 0;

                //50, 96, 112
                for (int i = 0; i < grnHeight * grnWidth; i++)
                {

                    if (overPix[i] > 105)
                    {
                        more++;
                        level = overPix[i];
                    }
                    else if (overPix[i] > 85)
                    {
                        twice++;
                        level = overPix[i];
                    }
                    else if (overPix[i] > 50)
                    {
                        once++;
                    }
                }
                total = once + twice + more;
                total2 = total + twice + more + more;

                if (total2 > 0)
                {
                    // [XPLAT] was: fd.actualAreaCovered = (...); fd.overlapPercent = Math.Round(...);
                    double actualAreaCovered = (total / total2 * getWorkedAreaTotal());
                    double overlapPercent = Math.Round(((1 - total / total2) * 100), 2);
                    setOverlapResult(actualAreaCovered, overlapPercent);
                }
                else
                {
                    setOverlapResult(0, 0);
                }
            }
        }

        /// <summary>
        /// [XPLAT] Picks the tram-dot colour for a marker (ported verbatim from <c>TramDotColor</c>):
        /// manual mode flashes between the on/off manual colours; automatic mode uses the control-bit
        /// on/off colours.
        /// </summary>
        /// <param name="isManual">True when this tram side is under manual control.</param>
        /// <param name="isFlashOn">Current flash phase (manual mode only).</param>
        /// <param name="controlBitOn">Whether the automatic tram control bit is set for this side.</param>
        /// <returns>The colour to tint the tram dot.</returns>
        private ColorRgba TramDotColor(bool isManual, bool isFlashOn, bool controlBitOn)
        {
            return isManual
                ? (isFlashOn ? Colors.TramDotManualFlashOnColor : Colors.TramDotManualFlashOffColor)
                : (controlBitOn ? Colors.TramDotAutomaticControlBitOnColor : Colors.TramDotAutomaticControlBitOffColor);
        }

        // ================================================================================================
        //  HUD overlays (ported verbatim from OpenGL.Designer.cs). WinForms control sizes (oglMain.Width/
        //  Height) become OglWidth/OglHeight; the ScreenTextures.* / VehicleTextures.* instance accesses
        //  become the injected screenTextures/vehicleTextures; non-collaborator reads (ABLine/curve/mc/ahrs)
        //  become the injected delegates; and the machine-PGN uturn/hydLift bytes go to appModel.machinePgnEF
        //  (the canonical frame CYouTurn/CHead write — see the constructor docs).
        // ================================================================================================

        /// <summary>
        /// [XPLAT] Picks the picked flag (if any) under the last click and signals the view to open the flag
        /// editor (ported from <c>MakeFlagMark</c>). The original opened/focused the WinForms
        /// <c>FormFlags</c> dialog; here the domain work (the 16x16 RGB read-back that decodes the flag id
        /// into <see cref="FlagNumberPicked"/>) is kept and a <see cref="OnFlagPicked"/> event is raised — the
        /// CFlag list and the flag-editor window are owned by the Avalonia view, not this service.
        /// </summary>
        public void MakeFlagMark()
        {
            LeftMouseDownOnOpenGL = false;

            try
            {
                byte[] data1 = new byte[768];

                //scan the center of click and a set of square points around
                GL.ReadPixels(MouseX - 8, MouseY - 8, 16, 16, OpenTK.Graphics.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, data1);

                //made it here so no flag found
                FlagNumberPicked = 0;

                for (int ctr = 0; ctr < 768; ctr += 3)
                {
                    if (data1[ctr] == 255 | data1[ctr + 1] == 255)
                    {
                        FlagNumberPicked = data1[ctr + 2];
                        break;
                    }
                }

                if (FlagNumberPicked > 0)
                {
                    // [XPLAT] was: focus the open FormFlags or open a new one over the picked flag. The
                    // Avalonia view subscribes to OnFlagPicked and owns that window; it reads FlagNumberPicked.
                    OnFlagPicked?.Invoke();
                }
            }
            catch
            {
                FlagNumberPicked = 0;
            }
        }

        /// <summary>
        /// [XPLAT] Draws the manual U-turn and manual lateral-shift glyphs (ported from
        /// <c>DrawManUTurnBtn</c>).
        /// </summary>
        private void DrawManUTurnBtn()
        {
            int bottomSide = 90;
            int two3 = -OglWidth / 4;

            if (!IsStanleyUsed && IsUTurnOn)
            {
                GL.Color3(0.90f, 0.90f, 0.293f);
                XyCoord center = new XyCoord(two3, 120);
                XyDelta delta = new XyDelta(82, 30);
                screenTextures.TurnManual.DrawCentered(center, delta);
            }

            //lateral line move

            bottomSide += 80;
            if (IsLateralOn)
            {
                GL.Color3(0.590f, 0.90f, 0.93f);
                XyCoord center = new XyCoord(two3, 200);
                XyDelta delta = new XyDelta(100, 30);

                screenTextures.LateralManual.DrawCentered(center, delta);
            }
        }

        /// <summary>
        /// [XPLAT] Draws the automatic U-turn button, the K-turn/normal-turn style glyph, and the
        /// distance-to-turn / pass-number text (ported from <c>DrawUTurnBtn</c>). The uturn machine byte is
        /// written to <c>appModel.machinePgnEF[appModel.machinePgnEFUturn]</c> — the SAME canonical EF frame
        /// CYouTurn writes — replacing the original <c>p_239.pgn[p_239.uturn]</c> (the composition root
        /// unifies that frame with the transmitted PGN 0xEF).
        /// </summary>
        private void DrawUTurnBtn()
        {
            GL.Enable(EnableCap.Texture2D);

            if (!yt.isYouTurnTriggered)
            {
                if (appModel.distancePivotToTurnLine > 0 && !yt.isOutOfBounds && yt.youTurnPhase == 10) GL.Color3(0.3f, 0.95f, 0.3f);
                else GL.Color3(0.97f, 0.635f, 0.4f);
                appModel.machinePgnEF[appModel.machinePgnEFUturn] = 0;
            }
            else
            {
                GL.Color3(0.90f, 0.90f, 0.293f);
                appModel.machinePgnEF[appModel.machinePgnEFUturn] = 1;
            }

            Texture2D turnTexture = !yt.isYouTurnTriggered ? screenTextures.Turn : screenTextures.TurnCancel;
            //int bottom = 90;
            int two3 = OglWidth / 5;
            XyCoord turnTextureCenter = new XyCoord(two3, 120);
            turnTexture.DrawCentered(turnTextureCenter, !yt.isTurnLeft ? new XyDelta(62, 30) : new XyDelta(-62, 30));

            //draw K turn/ normal turn button
            two3 += 140;

            GL.Color3(1.0f, 1.0f, 1.0f);

            Texture2D uTurnTexture = yt.uTurnStyle == 0 ? screenTextures.UTurnU : screenTextures.UTurnH;
            XyCoord uTurnTextureCenter = new XyCoord(two3, 130);
            uTurnTexture.DrawCentered(uTurnTextureCenter, new XyDelta(32, 30));

            two3 -= 140;
            GL.Color3(0.927f, 0.9635f, 0.74f);

            if (IsMetric)
            {
                if (!yt.isYouTurnTriggered)
                {
                    font.DrawText(-40 + two3, 120, DistPivotM);
                }
                else
                {
                    font.DrawText(-40 + two3, 120, yt.onA.ToString());
                }
            }
            else
            {
                if (!yt.isYouTurnTriggered)
                {
                    font.DrawText(-40 + two3, 120, DistPivotFt);
                }
                else
                {
                    font.DrawText(-40 + two3, 120, yt.onA.ToString());
                }
            }
        }

        /// <summary>
        /// [XPLAT] Draws the autosteer engage circle (steer pointer + roll indicator + clock + stationary
        /// dot), colour-coded by steer-switch / autosteer / connection state (ported from
        /// <c>DrawSteerCircle</c>). The steer-switch and IMU-roll reads use the injected delegates;
        /// <c>steerModuleConnectedCounter</c> becomes the public <see cref="SteerModuleConnectedCounter"/>.
        /// </summary>
        private void DrawSteerCircle()
        {
            int sizer = OglWidth / 15;
            int center = OglWidth / 2 - sizer;
            int bottomSide = OglHeight - sizer / 2;
            XyDelta textureDelta = new XyDelta(sizer, sizer);

            //draw the clock
            GL.Color4(0.9752f, 0.80f, 0.3f, 0.98);
            font.DrawText(center - 210, OglHeight - 26, DateTime.Now.ToString("T"), 0.8);

            GL.PushMatrix();

            if (getSteerSwitchHigh())
            {
                GL.Color4(0.9752f, 0.0f, 0.03f, 0.98);
                trk.isAutoSnapped = false;
            }
            else if (appModel.isBtnAutoSteerOn)
            {
                GL.Color4(0.052f, 0.970f, 0.03f, 0.97);
                if (trk.isAutoSnapToPivot && !trk.isAutoSnapped)
                {
                    trk.SnapToPivot();
                    trk.isAutoSnapped = true;
                }
            }
            else
            {
                GL.Color4(0.952f, 0.750f, 0.03f, 0.97);
                trk.isAutoSnapped = false;
            }

            //we have lost connection to steer module
            if (SteerModuleConnectedCounter++ > 30)
            {
                GL.Color4(0.952f, 0.093570f, 0.93f, 0.97);
            }

            GL.Translate(center, bottomSide, 0);
            GL.Rotate(getImuRoll(), 0, 0, 1);

            screenTextures.SteerPointer.DrawCenteredAroundOrigin(textureDelta);

            if ((getImuRoll() != 88888))
            {
                string head = Math.Round(getImuRoll(), 1).ToString();
                font.DrawText((int)(((head.Length) * -9)), -45, head, 1.2);
            }

            GL.PopMatrix();

            // stationary part
            GL.PushMatrix();

            GL.Translate(center, bottomSide, 0);

            screenTextures.SteerDot.DrawCenteredAroundOrigin(textureDelta);

            GL.PopMatrix();
        }

        /// <summary>
        /// [XPLAT] Draws the left/right tram-line dots, coloured by <see cref="TramDotColor"/> from the
        /// tram control byte and manual flags (ported from <c>DrawTramMarkers</c>).
        /// </summary>
        private void DrawTramMarkers()
        {
            //int sizer = 60;
            int bottomSide = OglHeight / 5;
            XyCoord leftDotCenter = new XyCoord(-50, bottomSide);
            XyCoord rightDotCenter = new XyCoord(+50, bottomSide);
            XyDelta dotDelta = new XyDelta(24, 24);

            ColorRgba leftDotColor = TramDotColor(tram.isLeftManualOn, IsFlashOnOff, (tram.controlByte & 2) != 0);
            GLW.SetColor(leftDotColor);
            screenTextures.TramDot.DrawCentered(leftDotCenter, dotDelta);

            ColorRgba rightDotColor = TramDotColor(tram.isRightManualOn, IsFlashOnOff, (tram.controlByte & 1) != 0);
            GLW.SetColor(rightDotColor);
            screenTextures.TramDot.DrawCentered(rightDotCenter, dotDelta);
        }

        /// <summary>
        /// [XPLAT] Draws a filled left/right arrow triangle for the dot-rail light bar (ported verbatim from
        /// <c>DrawArrowTriangle</c>).
        /// </summary>
        private void DrawArrowTriangle(double cx, double cy, double size, bool isRight)
        {
            double s = size;
            GL.Begin(PrimitiveType.Triangles);
            if (isRight)
            {
                // '>' : base on left, tip on right
                GL.Vertex2(cx - s, cy - s);
                GL.Vertex2(cx - s, cy + s);
                GL.Vertex2(cx + s, cy);
            }
            else
            {
                // '<' : base on right, tip on left
                GL.Vertex2(cx + s, cy - s);
                GL.Vertex2(cx + s, cy + s);
                GL.Vertex2(cx - s, cy);
            }
            GL.End();
        }

        /// <summary>
        /// [XPLAT] Draws the graphical cross-track light bar (dot rail + lit arrows) for the given offline
        /// distance (ported from <c>DrawLightBar</c>). <c>lightbarCmPerPixel</c> becomes the public
        /// <see cref="LightbarCmPerPixel"/> (integer-division resolution preserved).
        /// </summary>
        private void DrawLightBar(double width, double height, double offlineDistance)
        {
            const int spacing = 32;
            const int dotsPerSide = 8;
            const double down = 25.0;

            const double arrowSizeFill = 10.0;
            const double arrowSizeHalo = 16.0;

            GL.LineWidth(1);

            // Compute center gap so text background has space
            double wide = width / 18.0;
            if (wide < 64) wide = 64;
            double margin = 20.0; // extra spacing in the center
            double desiredInner = wide + margin;
            double currentInner = 2 * spacing;
            double shift = Math.Max(0.0, desiredInner - currentInner);

            // Clamp offline distance
            int dotDistance = (int)offlineDistance;
            int limit = (int)(LightbarCmPerPixel * dotsPerSide);
            if (dotDistance < -limit) dotDistance = -limit;
            if (dotDistance > limit) dotDistance = limit;

            // Background rail (small dark points)
            GL.PointSize(8.0f);
            GL.Color3(0.00, 0.0, 0.0);
            GL.Begin(PrimitiveType.Points);
            for (int i = -dotsPerSide; i < -1; i++) GL.Vertex2((i * spacing) - shift, down);   // left rail
            for (int i = 2; i < dotsPerSide + 1; i++) GL.Vertex2((i * spacing) + shift, down); // right rail
            GL.End();

            GL.PushMatrix();
            GL.Translate(0, 0, 0.01);

            if (offlineDistance < 0.0)
            {
                // Right/green side (negative = right correction)
                int lit = (-dotDistance / LightbarCmPerPixel) + 1;
                if (lit < 0) lit = 0;
                lit = Math.Min(lit, dotsPerSide + 1);

                // HALO first
                GL.Color3(0.0f, 0.0f, 0.0f);
                for (int i = 2; i < lit + 1; i++)
                {
                    double cx = (i * spacing) + shift;
                    DrawArrowTriangle(cx, down, arrowSizeHalo, true);
                }

                // FILL
                GL.Color3(0.0f, 0.980f, 0.0f);
                for (int i = 1; i < lit; i++)
                {
                    double cx = (i * spacing + spacing) + shift;
                    DrawArrowTriangle(cx, down, arrowSizeFill, true);
                }
            }
            else
            {
                // Left/red side (positive = left correction)
                int lit = (dotDistance / LightbarCmPerPixel) + 1;
                if (lit < 0) lit = 0;
                lit = Math.Min(lit, dotsPerSide + 1);

                // HALO first
                GL.Color3(0.0f, 0.0f, 0.0f);
                for (int i = 2; i < lit + 1; i++)
                {
                    double cx = (i * -spacing) - shift;
                    DrawArrowTriangle(cx, down, arrowSizeHalo, false);
                }

                // FILL
                GL.Color3(0.980f, 0.30f, 0.0f);
                for (int i = 1; i < lit; i++)
                {
                    double cx = (i * -spacing - spacing) - shift;
                    DrawArrowTriangle(cx, down, arrowSizeFill, false);
                }
            }

            GL.PopMatrix();
        }

        /// <summary>
        /// [XPLAT] Draws the light-bar cross-track text + dot rail with EWMA smoothing (ported from
        /// <c>DrawLightBarText</c>). The contour/recorded-path gates use the injected delegates;
        /// <c>lightbarDistance</c> becomes <see cref="LightbarDistance"/>; the cross-track background uses
        /// the injected <c>screenTextures</c>.
        /// </summary>
        private void DrawLightBarText()
        {
            GL.Disable(EnableCap.DepthTest);

            if (getIsContourBtnOn() || trk.idx > -1 || getIsDrivingRecordedPath())
            {
                // EWMA of cross-track in mm (fast/visual smoothing)
                avgPivDistance = avgPivDistance * 0.5 + LightbarDistance * 0.5;

                // Low-pass of absolute error in mm (for the small secondary text)
                longAvgPivDistance = longAvgPivDistance * 0.98 + Math.Abs(avgPivDistance) * 0.02;
                if (longAvgPivDistance > 150) longAvgPivDistance = 150; // cap AFTER filter to keep smoothing effective

                // Convert to display units: cm (metric) or inch (imperial)
                double avgPivotDistance = avgPivDistance * (IsMetric ? 0.1 : 0.03937);

                // Keep geometry identical to SteerBarText
                double textSize = (100.0 + (OglHeight - 600.0)) * 0.0012;
                int wide = (int)(OglWidth / 18.0);
                if (wide < 64) wide = 64;

                // Draw the dot-rail LightBar using the same sizing
                if (IsLightbarOn) DrawLightBar(OglWidth, OglHeight, avgPivotDistance);

                // Clamp display range for the main label
                if (avgPivotDistance > 999) avgPivotDistance = 999;
                if (avgPivotDistance < -999) avgPivotDistance = -999;

                // Build label just like SteerBar: near-zero shows "> 0 <", otherwise show magnitude + direction
                string hede;
                if (Math.Abs(avgPivotDistance) < 0.9999)
                {
                    hede = "> 0 <";
                }
                else
                {
                    hede = (avgPivotDistance < 0)
                        ? (Math.Abs(avgPivotDistance)).ToString("N0") + " >"  // negative → right correction
                        : "< " + (Math.Abs(avgPivotDistance)).ToString("N0"); // positive → left correction
                }

                // Center based on actual string length and SteerBar font metrics
                int center = -(int)((hede.Length * 0.5) * (18 * (1.0 + textSize)));

                // Dynamic color (same logic you already use)
                double green = Math.Abs(avgPivDistance);
                double red = green;
                if (green > 400) green = 400;
                green = (0.4 - (green * 0.001)) + 0.58;
                if (red > 400) red = 400;
                red = 0.002 * red;

                GL.Color4(red, green, 0.3, 1.0);

                // Background rectangle IDENTICAL to SteerBarText
                XyCoord u0v0 = new XyCoord(-wide, 35 * (1 + textSize));
                XyCoord u1v1 = new XyCoord(wide, 3);
                screenTextures.CrossTrackBackground.Draw(u0v0, u1v1);

                // Main text (distance + arrows)
                GL.Color4(0.12f, 0.12770f, 0.120f, 1);
                font.DrawText(center, 2, hede, 1.0 + textSize);

                // Secondary small line: long-term average (only when small)
                if (longAvgPivDistance < 150)
                {
                    string small = (Math.Abs(longAvgPivDistance * (IsMetric ? 0.1 : 0.03937))).ToString("N1");
                    GL.Color3(0.950f, 0.952f, 0.3f);
                    int centerSmall = -(int)((small.Length * 0.5) * 16);
                    font.DrawText(centerSmall, (int)(30 * (1.0 + 0.2 * textSize)) + 10, small, 1.0);
                }

                // top line indicator as SteerBar when in dead-zone
                if (vehicle.isInDeadZone)
                {
                    GL.Color4(0.512f, 0.9712770f, 0.5120f, 1);
                    GL.LineWidth(4);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex2(-wide, 36 * (1 + textSize));
                    GL.Vertex2(wide, 36 * (1 + textSize));
                    GL.End();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Draws the steer-bar cross-track + steer-angle-error indicator (ported from
        /// <c>DrawSteerBarText</c>). The contour/recorded-path gates and the actual steer angle use the
        /// injected delegates; <c>isBtnAutoSteerOn</c>/<c>guidanceLineSteerAngle</c> come from
        /// <see cref="appModel"/>; <c>lightbarDistance</c> from <see cref="LightbarDistance"/>.
        /// </summary>
        private void DrawSteerBarText()
        {
            if (getIsContourBtnOn() || trk.idx > -1 || getIsDrivingRecordedPath())
            {
                GL.Disable(EnableCap.DepthTest);
                int spacing = OglWidth / 50;
                if (spacing < 28) spacing = 28;
                int offset = (int)((double)OglHeight / 40);
                int line = 12;
                int line2 = 8;

                //int down = (int)((double)oglMain.Height/38);
                int down = 58 + (int)((double)(OglHeight - 600) / 17);

                double textSize = (100 + (double)(OglHeight - 600)) * 0.0012;
                int pointy = 24;

                double alphaBar = 1.0;
                if (appModel.isBtnAutoSteerOn) alphaBar = 0.5;

                avgPivDistance = avgPivDistance * 0.8 + LightbarDistance * 0.2;

                // in millimeters
                double avgPivotDistance = avgPivDistance * (IsMetric ? 0.1 : 0.03937);
                double err = (getActualSteerAngleDegrees() - (double)(appModel.guidanceLineSteerAngle) * 0.01);

                if (appModel.isBtnAutoSteerOn)
                {
                    if (Math.Abs(err) < 0.5) err = 0;
                    offset = (int)((double)OglHeight / 60);
                    line /= 2;
                    line2 /= 2;
                }
                else
                {
                    if (Math.Abs(err) < 0.2) err = 0;
                }

                double errLine = err;
                if (errLine > 12) errLine = 12;
                if (errLine < -12) errLine = -12;
                errLine *= spacing;

                if (errLine > 0) errLine += 35;
                else errLine -= 35;

                if (err != 0)
                {
                    GL.Color4(0, 0, 0, alphaBar);
                    GL.LineWidth(line);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex2(0, down);
                    GL.Vertex2(errLine, down);
                    GL.End();
                    GL.Color4(0.950f, 0.986530f, 0.40f, alphaBar);
                    GL.LineWidth(line2);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex2(0, down);
                    GL.Vertex2(errLine, down);
                    GL.End();

                    if ((err) > 0.0)
                    {
                        spacing *= -1;
                        offset *= -1;
                        pointy *= -1;
                    }

                    GL.Color4(0, 0.99, 0, alphaBar);
                    GL.Begin(PrimitiveType.TriangleStrip);
                    GL.Vertex2((errLine), down - offset);
                    GL.Vertex2((errLine + offset + pointy), down);
                    GL.Vertex2((errLine), down + offset);
                    GL.End();

                    GL.Color4(0.79, 0.79, 0, alphaBar);

                    GL.Begin(PrimitiveType.TriangleStrip);
                    GL.Vertex2((0), down - offset);
                    GL.Vertex2((0 + offset + pointy), down);
                    GL.Vertex2((0), down + offset);
                    GL.End();

                    GL.LineWidth(3);
                    GL.Color4(0, 0, 0, alphaBar);

                    GL.Begin(PrimitiveType.LineLoop);
                    GL.Vertex2((errLine), down - offset);
                    GL.Vertex2((errLine + offset + pointy), down);
                    GL.Vertex2((errLine), down + offset);
                    GL.End();

                    GL.Begin(PrimitiveType.LineLoop);
                    GL.Vertex2((0), down - offset);
                    GL.Vertex2((0 + offset + pointy), down);
                    GL.Vertex2((0), down + offset);
                    GL.End();
                }

                int center = 0;
                string hede = "> 0 <";

                if (avgPivotDistance > 999) avgPivotDistance = 999;
                if (avgPivotDistance < -999) avgPivotDistance = -999;

                if (Math.Abs(avgPivotDistance) > 0.9999)
                {
                    if (avgPivotDistance < 0.0)
                    {
                        hede = (Math.Abs(avgPivotDistance)).ToString("N0") + " >";
                        center = -(int)(((double)(hede.Length) * 0.5) * (18 * (1.0 + textSize)) - 0);
                    }
                    else
                    {
                        hede = "< " + (Math.Abs(avgPivotDistance)).ToString("N0");
                        center = -(int)(((double)(hede.Length) * 0.5) * (18 * (1.0 + textSize)));
                    }
                }
                else
                {
                    center = (int)(-40 * (1 + textSize));
                }

                int wide = (int)((double)OglWidth / 18);
                if (wide < 64) wide = 64;

                double green = Math.Abs(avgPivDistance);
                double red = green;
                if (green > 400) green = 400;
                green *= .001;
                green = (0.4 - green) + 0.58;

                if (red > 400) red = 400;
                red = 0.002 * red;

                GL.Color4(red, green, 0.3, 1.0);

                XyCoord u0v0 = new XyCoord(-wide, 35 * (1 + textSize));
                XyCoord u1v1 = new XyCoord(wide, 3);
                screenTextures.CrossTrackBackground.Draw(u0v0, u1v1);

                GL.Color4(0.12f, 0.12770f, 0.120f, 1);

                font.DrawText(center, 2, hede, 1.0 + textSize);

                if (vehicle.isInDeadZone)
                {
                    GL.Color4(0.512f, 0.9712770f, 0.5120f, 1);
                    GL.LineWidth(4);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex2(-wide, 36 * (1 + textSize));
                    GL.Vertex2(wide, 36 * (1 + textSize));
                    GL.End();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Draws the current track's direction / pass-number / nudge-offset label (ported from
        /// <c>DrawTrackInfo</c>). The AB/curve heading-same-way and paths-away reads use the injected
        /// delegates; <c>m2InchOrCm</c>/<c>unitsInCmNS</c> become <see cref="M2InchOrCm"/>/
        /// <see cref="UnitsInCmNS"/>; the headland-distance gate comes from <see cref="appModel"/>.
        /// </summary>
        private void DrawTrackInfo()
        {
            string offs = "";

            if (trk.gArr[trk.idx].nudgeDistance != 0)
                offs = ((int)(trk.gArr[trk.idx].nudgeDistance * M2InchOrCm)).ToString() + UnitsInCmNS;

            string dire;

            if (trk.gArr[trk.idx].mode == TrackMode.AB)
            {
                if (getABLineIsHeadingSameWay()) dire = "{";
                else dire = "}";

                if (getABLineHowManyPathsAway() > -1)
                    dire = dire + (getABLineHowManyPathsAway() + 1).ToString() + "R " + offs;
                else
                    dire = dire + (-getABLineHowManyPathsAway()).ToString() + "L " + offs;
            }
            else
            {
                if (getCurveIsHeadingSameWay()) dire = "{";
                else dire = "}";

                GL.Color4(1.269, 1.25, 1.2510, 0.87);
                if (getCurveHowManyPathsAway() > -1)
                    dire = dire + (getCurveHowManyPathsAway() + 1).ToString() + "R " + offs;
                else
                    dire = dire + (-getCurveHowManyPathsAway()).ToString() + "L " + offs;
            }

            int start = -(int)(((double)(dire.Length) * 0.45) * (22 * (1.0)));
            int down = 0;
            int baseDown = (bnd.isHeadlandOn && appModel.isHeadlandDistanceOn) ? 175 : 70;
            int offset = (int)((OglHeight - 600) / 12.0);
            down = baseDown + offset;

            double textSize = (300 + (double)(OglHeight - 600)) * 0.0012 + 1;

            GL.Color4(0.9, 0.9, 0.9, 0.8);

            font.DrawText(start, down, dire, textSize);
        }

        /// <summary>
        /// [XPLAT] Draws the zoom-in / zoom-out / pan / menu-show-hide HUD icons, the grid-spacing label,
        /// the heading read-out, the GPS-step read-out and the max-angular-velocity warning glyph (ported
        /// verbatim from <c>DrawCompassText</c>). <c>oglMain.Width/Height</c> become <see cref="OglWidth"/>/
        /// <see cref="OglHeight"/>; <c>isJobStarted</c> reads from <see cref="appModel"/>; the
        /// <c>ScreenTextures.*</c> statics become the injected <see cref="screenTextures"/>; and
        /// <c>isPanFormVisible</c>/<c>gridToolSpacing</c>/<c>fixHeading</c>/<c>distanceCurrentStepFixDisplay</c>/
        /// <c>isMaxAngularVelocity</c> become the corresponding render-state properties. Immediate-mode GL
        /// preserved (see file-level FEASIBILITY RISK block).
        /// </summary>
        private void DrawCompassText()
        {
            GL.Color3(0.90f, 0.90f, 0.93f);

            int center = OglWidth / 2 - 60;

            XyCoord zoomInCoord = new XyCoord(center, 50);
            XyCoord zoomOutCoord = new XyCoord(center, 200);
            XyDelta sizeDelta = new XyDelta(32, 32);

            screenTextures.ZoomIn.Draw(zoomInCoord, zoomInCoord + sizeDelta);
            screenTextures.ZoomOut.Draw(zoomOutCoord, zoomOutCoord + sizeDelta);

            //Pan
            if (appModel.isJobStarted)
            {
                center = OglWidth / -2 + 30;
                if (!IsPanFormVisible)
                {
                    XyCoord panCoord = new XyCoord(center, 50);
                    screenTextures.Pan.Draw(panCoord, panCoord + sizeDelta);
                }

                //hide show bottom menu
                int hite = OglHeight - 30;
                XyCoord menuShowHideCoord = new XyCoord(center, hite - 32);
                screenTextures.MenuShowHide.Draw(menuShowHideCoord, menuShowHideCoord + sizeDelta);

                center += 50;
                font.DrawText(center - 56, hite - 72, "x" + GridToolSpacing.ToString(), 1);
            }

            center = OglWidth / -2 + 10;
            double deg = glm.toDegrees(FixHeading);
            if (deg > 359.9) deg = 359.9;
            string strHeading = (deg).ToString("N1");
            int lenth = 18 * strHeading.Length;

            GL.Color3(0.9852f, 0.982f, 0.983f);
            font.DrawText(OglWidth / 2 - lenth, 10, strHeading, 1);

            //GPS Step
            if (DistanceCurrentStepFixDisplay < 0.03 * 100)
                GL.Color3(0.98f, 0.82f, 0.653f);
            font.DrawText(center, 10, DistanceCurrentStepFixDisplay.ToString("N1") + "cm", 1);

            if (IsMaxAngularVelocity)
            {
                GL.Color3(0.98f, 0.4f, 0.4f);
                font.DrawText(center - 10, OglHeight - 260, "*", 2);
            }
        }

        // [XPLAT] DrawCompass renders the optional rotating compass-rose overlay. Its ONLY call site is
        // the historical, currently-disabled toggle `//if (isCompassOn) DrawCompass();` in the main paint
        // (see Render). It is ported verbatim for 1:1 structural parity with the source OpenGL.Designer.cs.
        // Because the sole invocation is commented out (the compass-on feature is inactive in the current
        // product), the unused-private-member analyzer (IDE0051) is suppressed locally rather than deleting
        // the method — deleting it would diverge from the frozen source, while a global suppression would
        // weaken the analyzer everywhere. ScreenTextures.* -> injected screenTextures; camHeading ->
        // CamHeading; oglMain.Width -> OglWidth. Immediate-mode GL preserved.
#pragma warning disable IDE0051 // Remove unused private members
        private void DrawCompass()
        {
            //Heading text
            int center = OglWidth / 2 - 55;
            font.DrawText(center - 8, 40, "^", 0.8);

            GL.PushMatrix();
            GL.Color4(0.952f, 0.870f, 0.73f, 0.8);

            GL.Translate(center, 78, 0);
            GL.Rotate(-CamHeading, 0, 0, 1);

            screenTextures.Compass.DrawCenteredAroundOrigin(new XyDelta(52.0, 52.0));

            GL.PopMatrix();
        }
#pragma warning restore IDE0051 // Remove unused private members

        /// <summary>
        /// [XPLAT] Draws the reverse / changing-direction indicator (ported verbatim from <c>DrawReverse</c>).
        /// <c>isReverseWithIMU</c>/<c>isChangingDirection</c> become <see cref="IsReverseWithIMU"/>/
        /// <see cref="IsChangingDirection"/>; <c>isReverse</c> reads from <see cref="appModel"/>;
        /// <c>ScreenTextures.Lift</c> becomes the injected <see cref="screenTextures"/>;
        /// <c>oglMain.Width/Height</c> become <see cref="OglWidth"/>/<see cref="OglHeight"/>. The
        /// dead commented localization lines (referencing the removed <c>gStr</c> string table) are
        /// omitted; the identical-color <c>if (isReverse) ... else ...</c> branch is preserved verbatim
        /// as it is live source code. Immediate-mode GL preserved.
        /// </summary>
        private void DrawReverse()
        {
            if (IsReverseWithIMU)
            {
                GL.Color3(0.952f, 0.9520f, 0.0f);

                GL.PushMatrix();
                GL.Enable(EnableCap.Texture2D);

                screenTextures.Lift.Bind();

                GL.Translate(-OglWidth / 12, OglHeight / 2 - 20, 0);
                GL.Rotate(180, 0, 0, 1);

                GL.Begin(PrimitiveType.Quads);              // Build Quad From A Triangle Strip
                {
                    GL.TexCoord2(0, 0.15); GL.Vertex2(-32, -32); // 
                    GL.TexCoord2(1, 0.15); GL.Vertex2(32, -32.0); // 
                    GL.TexCoord2(1, 1); GL.Vertex2(32, 32); // 
                    GL.TexCoord2(0, 1); GL.Vertex2(-32, 32); //
                }
                GL.End();

                GL.Disable(EnableCap.Texture2D);
                GL.PopMatrix();
            }
            else
            {
                if (appModel.isReverse) GL.Color3(0.952f, 0.0f, 0.0f);
                else GL.Color3(0.952f, 0.0f, 0.0f);

                if (IsChangingDirection) GL.Color3(0.952f, 0.990f, 0.0f);

                GL.PushMatrix();
                GL.Enable(EnableCap.Texture2D);

                screenTextures.Lift.Bind();

                GL.Translate(-OglWidth / 12, OglHeight / 2 - 20, 0);

                if (IsChangingDirection) GL.Rotate(90, 0, 0, 1);
                else GL.Rotate(180, 0, 0, 1);

                GL.Begin(PrimitiveType.Quads);              // Build Quad From A Triangle Strip
                {
                    GL.TexCoord2(0, 0.15); GL.Vertex2(-32, -32); // 
                    GL.TexCoord2(1, 0.15); GL.Vertex2(32, -32.0); // 
                    GL.TexCoord2(1, 1); GL.Vertex2(32, 32); // 
                    GL.TexCoord2(0, 1); GL.Vertex2(-32, 32); //
                }
                GL.End();

                GL.Disable(EnableCap.Texture2D);
                GL.PopMatrix();
            }
        }

        /// <summary>
        /// [XPLAT] Draws the hydraulic-lift up/down indicator (ported verbatim from <c>DrawLiftIndicator</c>).
        /// The original sampled the machine PGN-EF frame via <c>p_239.pgn[p_239.hydLift]</c>; that becomes
        /// <c>appModel.machinePgnEF[appModel.machinePgnEFHydLift]</c> (the unified machine-EF frame written
        /// by CHead.SetHydPosition — see the PGN-parity note). <c>ScreenTextures.Lift</c> -> injected
        /// <see cref="screenTextures"/>; <c>oglMain.Width/Height</c> -> <see cref="OglWidth"/>/
        /// <see cref="OglHeight"/>. Immediate-mode GL preserved.
        /// </summary>
        private void DrawLiftIndicator()
        {
            GL.PushMatrix();

            GL.Translate(OglWidth / 2 - 35, OglHeight / 2, 0);

            if (appModel.machinePgnEF[appModel.machinePgnEFHydLift] == 2)
            {
                GL.Color3(0.0f, 0.950f, 0.0f);
            }
            else
            {
                GL.Rotate(180, 0, 0, 1);
                GL.Color3(0.952f, 0.40f, 0.0f);
            }
            screenTextures.Lift.DrawCenteredAroundOrigin(new XyDelta(48, 64));

            GL.PopMatrix();
        }

        /// <summary>
        /// [XPLAT] Draws the analog speedometer dial and needle (ported verbatim from <c>DrawSpeedo</c>).
        /// <c>avgSpeed</c> reads from <see cref="appModel"/>; <c>isMetric</c> becomes <see cref="IsMetric"/>;
        /// <c>Speed.KmhToMph</c> is the kept Core conversion; <c>ScreenTextures.Speedo/SpeedoNeedle</c>
        /// become the injected <see cref="screenTextures"/>; <c>oglMain.Width</c> -> <see cref="OglWidth"/>.
        /// Immediate-mode GL preserved.
        /// </summary>
        private void DrawSpeedo()
        {
            GL.PushMatrix();

            GL.Color4(0.952f, 0.980f, 0.98f, 0.99);

            GL.Translate(OglWidth / 2 - 130, 65, 0);

            screenTextures.Speedo.DrawCenteredAroundOrigin(new XyDelta(58, 58));

            // speedoSpeed is just a number without a unit
            double speedoSpeed = Math.Abs(IsMetric ? appModel.avgSpeed : Speed.KmhToMph(appModel.avgSpeed));
            speedoSpeed = Math.Min(speedoSpeed, 20);
            double angle = (speedoSpeed - 10) * 15;

            if (speedoSpeed > -0.1) GL.Color3(0.850f, 0.950f, 0.30f);
            else GL.Color3(0.952f, 0.0f, 0.0f);

            GL.Rotate(angle, 0, 0, 1);
            screenTextures.SpeedoNeedle.DrawCenteredAroundOrigin(new XyDelta(48, 48));

            GL.PopMatrix();
        }

        /// <summary>
        /// [XPLAT] Draws the "RTK Fix Lost" warning text (ported verbatim from <c>DrawLostRTK</c>). This is
        /// the RENDER-only half of the RTK alarm: the autosteer-kill decision and the 1000 ms debounce were
        /// moved to <c>PositionService.EvaluateRtkRecovery()</c> (AAP §0.6 Phase 7). <c>oglMain.Width/Height</c>
        /// -> <see cref="OglWidth"/>/<see cref="OglHeight"/>. Immediate-mode GL preserved.
        /// </summary>
        private void DrawLostRTK()
        {
            GL.Color3(0.9752f, 0.752f, 0.40f);
            font.DrawText(-OglWidth / 3, OglHeight / 3, "RTK Fix Lost", 2);
        }

        /// <summary>
        /// [XPLAT] Draws the build/version watermark (ported verbatim from <c>DrawVersion</c>).
        /// <c>oglMain.Width/Height</c> -> <see cref="OglWidth"/>/<see cref="OglHeight"/>. The version string
        /// is supplied by the caller (composition root / view-model) rather than read from a WinForms field.
        /// Immediate-mode GL preserved.
        /// </summary>
        /// <param name="version">The version string to render (e.g. semantic version with pre-release tag).</param>
        private void DrawVersion(string version)
        {
            GL.Color3(1f, 1f, 1f);
            font.DrawText(-OglWidth / 2.1, OglHeight / 1.2, version, 0.8);
        }

        /// <summary>
        /// [XPLAT] Draws the differential-correction "Age" read-out (ported verbatim from <c>DrawAge</c>).
        /// <c>pn.age</c> is read from the injected <see cref="pn"/> (CNMEA); <c>oglMain.Width</c> ->
        /// <see cref="OglWidth"/>. Immediate-mode GL preserved.
        /// </summary>
        private void DrawAge()
        {
            GL.Color3(0.9752f, 0.52f, 0.0f);
            font.DrawText(OglWidth / 4, 60, "Age:" + pn.age.ToString("N1"), 1.5);
        }

        /// <summary>
        /// [XPLAT] Counts down the transient guidance-line label timer (ported verbatim from
        /// <c>DrawGuidanceLineText</c>). The WinForms <c>guideLineCounter</c> becomes
        /// <see cref="GuideLineCounter"/>; when it reaches zero the original hid the
        /// <c>lblGuidanceLine</c> WinForms label — that view-side hide is raised through the
        /// <see cref="OnGuidanceLineExpired"/> event so the Avalonia view-model can clear the label.
        /// </summary>
        private void DrawGuidanceLineText()
        {
            if (GuideLineCounter > 0)
            {
                GuideLineCounter--;

                if (GuideLineCounter == 0)
                {
                    OnGuidanceLineExpired?.Invoke();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Draws the headland-distance HUD badge — a rounded translucent box with the headland
        /// icon and the distance label (ported verbatim from <c>DrawHeadlandDistance</c>). The badge turns
        /// red when within 20 m of the headland and yellow/green otherwise. <c>oglMain.Height</c> ->
        /// <see cref="OglHeight"/>; <c>isMetric</c> -> <see cref="IsMetric"/>; <c>bnd.HeadlandDistance</c>
        /// is read from the injected <see cref="bnd"/>; <c>Distance.MediumBigDistanceString</c> is the kept
        /// Core formatter; <c>ScreenTextures.HeadlandLight/HeadlandDark</c> -> injected
        /// <see cref="screenTextures"/>; the icon's light/dark variant is chosen by sampling the framebuffer
        /// via <see cref="UseLightIconBySampling"/>. Immediate-mode GL + the local Ortho HUD push/pop
        /// preserved verbatim.
        /// </summary>
        private void DrawHeadlandDistance()
        {
            // --- Query GL viewport (pixels) ---
            int[] vp = new int[4];
            GL.GetInteger(GetPName.Viewport, vp);
            int viewportWidth = vp[2];
            int viewportHeight = vp[3];

            // ---- BEGIN: Local HUD overlay (pixel space) ----
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0.0, viewportWidth, viewportHeight, 0.0, -1.0, 1.0);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
            if (depthWasEnabled) GL.Disable(EnableCap.DepthTest);

            // === Scaling rules (match LightBar style) ===
            // LightBar uses: textSize = (100 + (Height - 600)) * 0.0012 and then font scale = (1.0 + textSize)
            double textSize = (100.0 + (OglHeight - 600.0)) * 0.0006;
            double labelScale = 1.0 + textSize;

            // General pixel scale against a 600 px baseline; clamp to keep sane extremes
            double scale = OglHeight / 600.0;
            if (scale < 0.6) scale = 0.6;
            if (scale > 2.0) scale = 2.0;

            // --- Positioning: centered horizontally, top offset scales with height ---
            double anchorCenterX = viewportWidth / 2;
            double anchorTopY = (int)(70 * scale);

            // --- Metrics (all scale with s) ---
            // Estimate per-char width using the text scale so centering stays accurate.
            double charWidthBase = 6.5;
            double charWidth = charWidthBase * labelScale;
            double textLineHeightBase = 25.0;
            double textLineHeight = textLineHeightBase * labelScale;

            double iconWidth = 20.0 * scale;
            double iconHeight = 20.0 * scale;
            double spacing = 15.0 * scale;

            double padH = 12.0 * scale;
            double padV = 8.0 * scale;

            // Build label in display units (metric = meters, imperial = inches)
            string label;
            if (bnd.HeadlandDistance.HasValue)
            {
                double meters = bnd.HeadlandDistance.Value;

                // Show feet (2 decimals) or meters (1 decimal)
                label = Distance.MediumBigDistanceString(IsMetric, meters, 0, 1);
            }
            else
            {
                label = "--";
            }

            // Measure content
            double textWidth = label.Length * charWidth;
            double contentWidth = iconWidth + spacing + textWidth;
            double contentHeight = Math.Max(iconHeight, textLineHeight);

            // Box rect (center horizontally)
            double boxWidth = contentWidth + 7 * padH;
            double boxHeight = contentHeight + 0.2 * padV;
            double boxX = anchorCenterX - boxWidth / 2.0;
            double boxY = anchorTopY;

            // --- Background box: rounded fill + border ---
            // Corner radius (scales with your 'scale'; if you don't have 'scale', replace '10.0 * scale' with e.g. 10.0)
            double r = Math.Min(10.0 * scale, Math.Min(boxWidth, boxHeight) * 0.5 - 1.0);
            if (r < 1.0) r = 1.0;

            // Smoothness of the rounded corners
            int seg = 12;
            double step = (Math.PI * 0.5) / seg;

            // --- Fill (warm yellow, translucent) ---
            if (bnd.HeadlandDistance.HasValue && bnd.HeadlandDistance.Value > 20.0)
                GL.Color4(1.00f, 0.95f, 0.25f, 0.55f);
            else
                GL.Color4(1.00f, 0.0f, 0.0f, 0.75f);

            // Center rectangle (between rounded corners)
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex2(boxX + r, boxY);
            GL.Vertex2(boxX + boxWidth - r, boxY);
            GL.Vertex2(boxX + boxWidth - r, boxY + boxHeight);
            GL.Vertex2(boxX + r, boxY + boxHeight);
            GL.End();

            // Left & right side rectangles
            GL.Begin(PrimitiveType.Quads);
            // Left
            GL.Vertex2(boxX, boxY + r);
            GL.Vertex2(boxX + r, boxY + r);
            GL.Vertex2(boxX + r, boxY + boxHeight - r);
            GL.Vertex2(boxX, boxY + boxHeight - r);
            // Right
            GL.Vertex2(boxX + boxWidth - r, boxY + r);
            GL.Vertex2(boxX + boxWidth, boxY + r);
            GL.Vertex2(boxX + boxWidth, boxY + boxHeight - r);
            GL.Vertex2(boxX + boxWidth - r, boxY + boxHeight - r);
            GL.End();

            // Four corner fills (triangle fans)
            double cx, cy;

            // Top-left corner (π .. 3π/2)
            cx = boxX + r; cy = boxY + r;
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex2(cx, cy);
            for (int i = 0; i <= seg; i++)
            {
                double a = Math.PI + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            GL.End();

            // Top-right corner (3π/2 .. 2π)
            cx = boxX + boxWidth - r; cy = boxY + r;
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex2(cx, cy);
            for (int i = 0; i <= seg; i++)
            {
                double a = (1.5 * Math.PI) + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            GL.End();

            // Bottom-right corner (0 .. π/2)
            cx = boxX + boxWidth - r; cy = boxY + boxHeight - r;
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex2(cx, cy);
            for (int i = 0; i <= seg; i++)
            {
                double a = 0.0 + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            GL.End();

            // Bottom-left corner (π/2 .. π)
            cx = boxX + r; cy = boxY + boxHeight - r;
            GL.Begin(PrimitiveType.TriangleFan);
            GL.Vertex2(cx, cy);
            for (int i = 0; i <= seg; i++)
            {
                double a = (0.5 * Math.PI) + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }
            GL.End();

            // --- Border (amber, more opaque) ---
            float borderThickness = (float)Math.Max(2.0, 1.0 * scale);
            GL.LineWidth(borderThickness);
            GL.Color4(0.0f, 0.0f, 0.0f, 0.5f);
            GL.Begin(PrimitiveType.LineLoop);

            // Top-left arc (π .. 3π/2)
            cx = boxX + r; cy = boxY + r;
            for (int i = 0; i <= seg; i++)
            {
                double a = Math.PI + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }

            // Top-right arc (3π/2 .. 2π)
            cx = boxX + boxWidth - r; cy = boxY + r;
            for (int i = 0; i <= seg; i++)
            {
                double a = (1.5 * Math.PI) + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }

            // Bottom-right arc (0 .. π/2)
            cx = boxX + boxWidth - r; cy = boxY + boxHeight - r;
            for (int i = 0; i <= seg; i++)
            {
                double a = 0.0 + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }

            // Bottom-left arc (π/2 .. π)
            cx = boxX + r; cy = boxY + boxHeight - r;
            for (int i = 0; i <= seg; i++)
            {
                double a = (0.5 * Math.PI) + i * step;
                GL.Vertex2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }

            GL.End();
            GL.LineWidth(1f);

            // --- Icon placement inside the box ---
            double iconX = boxX + padH;
            double iconY = boxY + (boxHeight - iconHeight);

            bool useLight = UseLightIconBySampling((int)iconX, (int)iconY, (int)iconWidth, (int)iconHeight);
            Texture2D iconTexture = useLight ? screenTextures.HeadlandLight : screenTextures.HeadlandDark;

            // Draw icon
            GL.Color3(1.0f, 1.0f, 1.0f);
            GL.PushMatrix();
            GL.Translate(iconX + iconWidth / 2.0, iconY + iconHeight / 2.0, 0);
            iconTexture.DrawCenteredAroundOrigin(new XyDelta(iconWidth, iconHeight));
            GL.PopMatrix();

            // --- Text next to icon, vertically centered in the box ---
            double textX = iconX + iconWidth + spacing;
            double textY = boxY + (boxHeight - textLineHeight) - 4;

            if (bnd.HeadlandDistance.HasValue && bnd.HeadlandDistance.Value > 20.0)
                GL.Color3(0.0f, 0.9f, 0.0f);
            else
                GL.Color3(1.0f, 1.0f, 0.0f);

            font.DrawText((int)textX, (int)textY, label, labelScale);

            // ---- END: Restore previous GL state ----
            if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();

            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
        }

        /// <summary>
        /// [XPLAT] Decides whether to use the light headland icon by sampling a small framebuffer block
        /// under the icon (ported verbatim from <c>UseLightIconBySampling</c>). Returns true for a dark
        /// background (use the light icon), false for a light background (use the dark icon).
        /// <c>oglMain.Width/Height</c> -> <see cref="OglWidth"/>/<see cref="OglHeight"/>. The
        /// <see cref="GL.ReadPixels(int,int,int,int,OpenTK.Graphics.OpenGL.PixelFormat,PixelType,byte[])"/>
        /// call must be verified on the Avalonia surface (see file-level FEASIBILITY RISK + PARITY_REPORT).
        /// </summary>
        /// <param name="x">Left edge of the icon, top-origin pixel coordinates.</param>
        /// <param name="yTop">Top edge of the icon, top-origin pixel coordinates.</param>
        /// <param name="w">Icon width in pixels.</param>
        /// <param name="h">Icon height in pixels.</param>
        private bool UseLightIconBySampling(int x, int yTop, int w, int h)
        {
            // Sample a tiny 8x8 block around the center of the icon
            const int sampleSize = 8;
            int centerX = x + w / 2;
            int centerY_TopOrigin = yTop + h / 2;

            // OpenGL's ReadPixels origin is bottom-left; convert from top-origin coordinates
            int readX = Math.Max(0, centerX - sampleSize / 2);
            int readY = Math.Max(0, OglHeight - centerY_TopOrigin - sampleSize / 2);
            int maxW = Math.Min(sampleSize, OglWidth - readX);
            int maxH = Math.Min(sampleSize, OglHeight - readY);
            if (maxW <= 0 || maxH <= 0) return false; // fallback: assume light background → use dark icon

            // Read pixels (BGRA order when using PixelFormat.Bgra)
            byte[] buffer = new byte[maxW * maxH * 4];
            GL.ReadPixels(readX, readY, maxW, maxH,
                          OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
                          PixelType.UnsignedByte, buffer);

            // Compute average luma (BT.601): Y ≈ 0.299R + 0.587G + 0.114B
            double sum = 0;
            for (int i = 0; i < buffer.Length; i += 4)
            {
                byte b = buffer[i + 0];
                byte g = buffer[i + 1];
                byte r = buffer[i + 2];
                // byte a = buffer[i + 3]; // not needed
                double y601 = 0.299 * r + 0.587 * g + 0.114 * b;
                sum += y601;
            }
            double avg = sum / (buffer.Length / 4);

            // Threshold around mid-gray; tune if needed (e.g., 140–155 depending on your visuals)
            return avg < 140.0; // dark background → use light icon
        }

        /// <summary>
        /// [XPLAT] Counts down the transient hardware-message label timer (ported verbatim from
        /// <c>DrawHardwareMessageText</c>). The WinForms <c>hardwareLineCounter</c> becomes
        /// <see cref="HardwareLineCounter"/>; when it reaches zero the original hid the
        /// <c>lblHardwareMessage</c> WinForms label — that view-side hide is raised through the
        /// <see cref="OnHardwareMessageExpired"/> event for the Avalonia view-model to handle.
        /// </summary>
        private void DrawHardwareMessageText()
        {
            if (HardwareLineCounter > 0)
            {
                HardwareLineCounter--;

                if (HardwareLineCounter == 0)
                {
                    OnHardwareMessageExpired?.Invoke();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Recomputes the six view-frustum clipping planes from the current PROJECTION and MODELVIEW
        /// matrices (ported verbatim from <c>CalcFrustum</c>). The extracted planes are stored in
        /// <see cref="frustum"/> (RIGHT 0-3, LEFT 4-7, FAR 8-11, NEAR 12-15, BOTTOM 16-19, TOP 20-23) and
        /// drive the triangle-patch culling in <see cref="Render"/>. Uses the fixed-function matrix queries
        /// <c>GL.GetFloat(GetPName.ProjectionMatrix/ModelviewMatrix, ...)</c> — preserved verbatim per the
        /// file-level FEASIBILITY RISK (requires a desktop-GL / compatibility context).
        /// </summary>
        private void CalcFrustum()
        {
            float[] proj = new float[16];                           // For Grabbing The PROJECTION Matrix
            float[] modl = new float[16];                           // For Grabbing The MODELVIEW Matrix
            float[] clip = new float[16];                           // Result Of Concatenating PROJECTION and MODELVIEW

            GL.GetFloat(GetPName.ProjectionMatrix, proj);   // Grab The Current PROJECTION Matrix
            GL.GetFloat(GetPName.ModelviewMatrix, modl);   // Grab The Current MODELVIEW Matrix  

            // Concatenate (Multiply) The Two Matricies
            clip[0] = modl[0] * proj[0] + modl[1] * proj[4] + modl[2] * proj[8] + modl[3] * proj[12];
            clip[1] = modl[0] * proj[1] + modl[1] * proj[5] + modl[2] * proj[9] + modl[3] * proj[13];
            clip[2] = modl[0] * proj[2] + modl[1] * proj[6] + modl[2] * proj[10] + modl[3] * proj[14];
            clip[3] = modl[0] * proj[3] + modl[1] * proj[7] + modl[2] * proj[11] + modl[3] * proj[15];

            clip[4] = modl[4] * proj[0] + modl[5] * proj[4] + modl[6] * proj[8] + modl[7] * proj[12];
            clip[5] = modl[4] * proj[1] + modl[5] * proj[5] + modl[6] * proj[9] + modl[7] * proj[13];
            clip[6] = modl[4] * proj[2] + modl[5] * proj[6] + modl[6] * proj[10] + modl[7] * proj[14];
            clip[7] = modl[4] * proj[3] + modl[5] * proj[7] + modl[6] * proj[11] + modl[7] * proj[15];

            clip[8] = modl[8] * proj[0] + modl[9] * proj[4] + modl[10] * proj[8] + modl[11] * proj[12];
            clip[9] = modl[8] * proj[1] + modl[9] * proj[5] + modl[10] * proj[9] + modl[11] * proj[13];
            clip[10] = modl[8] * proj[2] + modl[9] * proj[6] + modl[10] * proj[10] + modl[11] * proj[14];
            clip[11] = modl[8] * proj[3] + modl[9] * proj[7] + modl[10] * proj[11] + modl[11] * proj[15];

            clip[12] = modl[12] * proj[0] + modl[13] * proj[4] + modl[14] * proj[8] + modl[15] * proj[12];
            clip[13] = modl[12] * proj[1] + modl[13] * proj[5] + modl[14] * proj[9] + modl[15] * proj[13];
            clip[14] = modl[12] * proj[2] + modl[13] * proj[6] + modl[14] * proj[10] + modl[15] * proj[14];
            clip[15] = modl[12] * proj[3] + modl[13] * proj[7] + modl[14] * proj[11] + modl[15] * proj[15];

            // Extract the RIGHT clipping plane
            frustum[0] = clip[3] - clip[0];
            frustum[1] = clip[7] - clip[4];
            frustum[2] = clip[11] - clip[8];
            frustum[3] = clip[15] - clip[12];

            // Extract the LEFT clipping plane
            frustum[4] = clip[3] + clip[0];
            frustum[5] = clip[7] + clip[4];
            frustum[6] = clip[11] + clip[8];
            frustum[7] = clip[15] + clip[12];

            // Extract the FAR clipping plane
            frustum[8] = clip[3] - clip[2];
            frustum[9] = clip[7] - clip[6];
            frustum[10] = clip[11] - clip[10];
            frustum[11] = clip[15] - clip[14];

            // Extract the NEAR clipping plane.  This is last on purpose (see pointinfrustum() for reason)
            frustum[12] = clip[3] + clip[2];
            frustum[13] = clip[7] + clip[6];
            frustum[14] = clip[11] + clip[10];
            frustum[15] = clip[15] + clip[14];

            // Extract the BOTTOM clipping plane
            frustum[16] = clip[3] + clip[1];
            frustum[17] = clip[7] + clip[5];
            frustum[18] = clip[11] + clip[9];
            frustum[19] = clip[15] + clip[13];

            // Extract the TOP clipping plane
            frustum[20] = clip[3] - clip[1];
            frustum[21] = clip[7] - clip[5];
            frustum[22] = clip[11] - clip[9];
            frustum[23] = clip[15] - clip[13];
        }

        /// <summary>
        /// [XPLAT] Determines the field bounding box and the largest cross-field distance (ported verbatim
        /// from <c>CalculateMinMax</c>). Exposed <c>public</c> because FieldIoService's field-open
        /// <c>recalcFieldBounds</c> delegate is wired to it by the composition root. The
        /// <see cref="maxFieldDistance"/> is clamped to [100, 5000] exactly as in the source. Note:
        /// <see cref="GeoBoundingBox"/> is a value type, so the <c>FieldBoundingBox.Include(...)</c> call in
        /// the empty branch operates on a temporary (a no-op beyond setting <see cref="maxFieldDistance"/>
        /// to 1500) — this matches the source behavior 1:1.
        /// </summary>
        public void CalculateMinMax()
        {
            FieldBoundingBox = CalculateFieldBoundingBox();
            if (FieldBoundingBox.IsEmpty)
            {
                FieldBoundingBox.Include(new GeoCoord(0.0, 0.0));
                maxFieldDistance = 1500;
            }
            else
            {
                //the largest distance across field
                double eastingDistance = FieldBoundingBox.MaxEasting - FieldBoundingBox.MinEasting;
                double northingDistance = FieldBoundingBox.MaxNorthing - FieldBoundingBox.MinNorthing;

                maxFieldDistance = Math.Max(eastingDistance, northingDistance);

                if (maxFieldDistance < 100) maxFieldDistance = 100;
                if (maxFieldDistance > 5000) maxFieldDistance = 5000;
            }
        }

        /// <summary>
        /// [XPLAT] Builds the field bounding box from the outer fence line, or — when no boundary exists —
        /// from the recorded triangle patches (ported verbatim from <c>CalculateFieldBoundingBox</c>). The
        /// first entry of each patch triangle list is skipped because it encodes the patch colour disguised
        /// as a <c>vec3</c>; only every third subsequent vertex is sampled, exactly as in the source.
        /// </summary>
        private GeoBoundingBox CalculateFieldBoundingBox()
        {
            GeoBoundingBox bb = GeoBoundingBox.CreateEmpty();
            if (bnd.bndList.Count > 0)
            {
                foreach (vec3 vertex in bnd.bndList[0].fenceLine)
                {
                    bb.Include(vertex.ToGeoCoord());
                }
            }
            else
            {
                foreach (CPatches patches in triStrip)
                {
                    //for every new chunk of patch
                    foreach (List<vec3> triList in patches.patchList)
                    {
                        // Skip the first entry. It is the color disguised as vec3
                        for (int i = 1; i < triList.Count; i += 3)
                        {
                            bb.Include(triList[i].ToGeoCoord());
                        }
                    }
                }
            }
            return bb;
        }
    }
}
