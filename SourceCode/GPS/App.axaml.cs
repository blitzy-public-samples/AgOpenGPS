// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// App.axaml.cs — the Avalonia <see cref="Application"/> code-behind AND the GPS program's
// COMPOSITION ROOT. It is the cross-platform replacement for the WinForms FormGPS constructor's
// object-graph assembly.
//
// The single most important migration change lives here: FormGPS used to build its domain hub with
// the panel/error presenters NULL-wired —
//
//     AppCore = new ApplicationCore(new DirectoryInfo(RegistrySettings.baseDirectory), null, null);
//
// This file wires the REAL presenters (AvaloniaPanelPresenter / AvaloniaErrorPresenter) and activates
// the previously-scaffolded MVVM layer (AAP G2, §0.3.2 MVVM). It then reconstructs — outside any
// WinForms shell — the exact runtime collaborator graph FormGPS used to own: the ~22 C-prefixed domain
// classes, the shared support objects (Camera, WorldGrid, Font, textures), and the five behaviour-frozen
// services extracted from the former FormGPS partial classes (PgnDispatcher, SectionService,
// PositionService, FieldIoService, RenderCoordinator). The god-object "mf" back-reference is gone:
// every collaborator is constructor-injected.
//
// Behaviour parity contract: the wired object graph reproduces FormGPS's runtime collaborators exactly
// (same ApplicationCore, same field/vehicle/tool directory root resolved through IPlatformServices, same
// settings load order). Only the presenters change from null to real implementations, and the WinForms
// host is replaced by Avalonia. No end-user behaviour is added or removed.
//
// Targets net8.0 AND net8.0-windows: this file references no Windows-only type directly — the only
// Windows specifics (WMI brightness, Registry migration-read) live in Platform/WindowsPlatformServices.cs
// behind #if WINDOWS, reached exclusively through the IPlatformServices abstraction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

using OpenTK.Graphics.OpenGL;

using SkiaSharp;

using AgOpenGPS.Classes;
using AgOpenGPS.Core;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Platform;
using AgOpenGPS.Core.ViewModels;

using AgOpenGPS.Services;
using AgOpenGPS.Views;

namespace AgOpenGPS
{
    /// <summary>
    /// The Avalonia application object and the GPS program's composition root. <see cref="Initialize"/>
    /// loads the XAML theme dictionaries declared in <c>App.axaml</c>; <see cref="OnFrameworkInitializationCompleted"/>
    /// assembles the full domain/service/view-model object graph and shows the main window — the cross-platform
    /// replacement for the former WinForms <c>FormGPS</c> constructor.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// [XPLAT] Rooted reference to the UDP/PGN dispatcher whose asynchronous loopback receive loop must
        /// outlive composition. It is also reachable transitively through the main window, but a dedicated
        /// field documents the ownership and gives the shutdown hook a handle. PgnDispatcher exposes no
        /// disposal API; the loopback socket is reclaimed by the OS at process exit.
        /// </summary>
        private PgnDispatcher _pgnDispatcher;

        /// <summary>
        /// Guards <see cref="EnsurePlatformServicesRegistered"/> so the platform factories are registered
        /// exactly once regardless of entry path (module initializer, <c>Program.Main</c>, or a test host).
        /// </summary>
        private static bool _platformServicesRegistered;

        /// <summary>
        /// Loads the compiled XAML for <c>App.axaml</c> (the FluentTheme plus the Day/Night
        /// <c>ThemeDictionaries</c>). Invoked by the Avalonia runtime before
        /// <see cref="OnFrameworkInitializationCompleted"/>.
        /// </summary>
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// [XPLAT] Assembly module initializer. Registers the per-OS <see cref="IPlatformServices"/> factories
        /// the instant this assembly is loaded — which is BEFORE <c>Program.Main</c> calls
        /// <c>RegistrySettings.Load()</c>. That ordering is essential: <c>RegistrySettings</c> resolves its
        /// configuration root through <see cref="PlatformServicesFactory.Create"/>, which throws if no factory
        /// has been registered. Running registration here (rather than only inside
        /// <see cref="OnFrameworkInitializationCompleted"/>) closes that startup gap without modifying
        /// <c>Program.cs</c>.
        /// </summary>
        [ModuleInitializer]
        internal static void ModuleInit() => EnsurePlatformServicesRegistered();

        /// <summary>
        /// Registers the Windows/Linux/macOS <see cref="IPlatformServices"/> factories with
        /// <see cref="PlatformServicesFactory"/> exactly once. The Windows slot is compiled only for the
        /// <c>net8.0-windows</c> target (behind <c>#if WINDOWS</c>) because <c>WindowsPlatformServices</c> uses
        /// WMI and the Registry; on the portable <c>net8.0</c> target the Windows factory is left unset.
        /// <see cref="PlatformServicesFactory.Register"/> ignores <c>null</c> factory arguments, so calling this
        /// again (e.g. from <c>Program.Main</c>) is harmless.
        /// </summary>
        private static void EnsurePlatformServicesRegistered()
        {
            if (_platformServicesRegistered)
            {
                return;
            }

            _platformServicesRegistered = true;

            PlatformServicesFactory.Register(
#if WINDOWS
                windowsFactory: () => new AgOpenGPS.Platform.WindowsPlatformServices(),
#else
                windowsFactory: null,
#endif
                linuxFactory: () => new AgOpenGPS.Platform.LinuxPlatformServices(),
                macFactory: () => new AgOpenGPS.Platform.MacPlatformServices());
        }

        /// <summary>
        /// The composition root. Runs once the Avalonia framework is ready: it acquires the platform services,
        /// resolves the configuration root, wires the real presenters, constructs the entire domain + service
        /// object graph (formerly assembled inside <c>FormGPS</c>), creates and shows the main window bound to
        /// the Core view-model, wires day/night theming, starts the UDP loopback server, and registers a
        /// shutdown hook.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Design-mode guard: the XAML previewer must not run platform/IO/socket code. Bail out before
                // any heavy wiring so the designer can render the shell chrome from the parameterless views.
                if (Design.IsDesignMode)
                {
                    base.OnFrameworkInitializationCompleted();
                    return;
                }

                // ----------------------------------------------------------------------------------------
                // 1. Platform services. Registration normally already ran in the module initializer; calling
                //    EnsurePlatformServicesRegistered() again is idempotent and protects non-Program entry
                //    paths. Create() selects the implementation for the current OS via RuntimeInformation.
                // ----------------------------------------------------------------------------------------
                EnsurePlatformServicesRegistered();
                IPlatformServices platform = PlatformServicesFactory.Create();

                // ----------------------------------------------------------------------------------------
                // 2. Base directory. RegistrySettings.baseDirectory is sourced from IPlatformServices.AppDataRoot
                //    by the Properties/RegistrySettings agent, so reusing it keeps every consumer
                //    (VehicleSettings/ToolSettings/field IO) on one resolved root — exactly as FormGPS did.
                // ----------------------------------------------------------------------------------------
                DirectoryInfo baseDir = new DirectoryInfo(
                    string.IsNullOrEmpty(RegistrySettings.baseDirectory) ? platform.AppDataRoot : RegistrySettings.baseDirectory);

                // ----------------------------------------------------------------------------------------
                // 3. Presenters (Avalonia-side concrete implementations). These replace FormGPS's null wiring.
                //    Their dialog Owner is assigned once the main window exists (step 11).
                // ----------------------------------------------------------------------------------------
                AvaloniaPanelPresenter panelPresenter = new AvaloniaPanelPresenter();
                AvaloniaErrorPresenter errorPresenter = new AvaloniaErrorPresenter();

                // ----------------------------------------------------------------------------------------
                // 4. ApplicationCore — the wired replacement for `new ApplicationCore(dir, null, null)`.
                //    It builds AppModel, FieldStreamer, the ApplicationViewModel and the ApplicationPresenter,
                //    and calls AppViewModel.SetPresenter(...).
                // ----------------------------------------------------------------------------------------
                ApplicationCore appCore = new ApplicationCore(baseDir, panelPresenter, errorPresenter);
                ApplicationModel appModel = appCore.AppModel;

                // ----------------------------------------------------------------------------------------
                // 5. Shared support objects (formerly FormGPS fields). The camera seeds from the persisted
                //    pitch/zoom; the world grid loads its floor texture pixels; the font binds to the screen
                //    font texture.
                // ----------------------------------------------------------------------------------------
                Camera camera = new Camera(
                    Properties.Settings.Default.setDisplay_camPitch,
                    Properties.Settings.Default.setDisplay_camZoom);

                byte[] floorPixels = LoadRgbaPixels(Properties.Resources.z_Floor, out int floorWidth, out int floorHeight);
                WorldGrid worldGrid = new WorldGrid(floorPixels, floorWidth, floorHeight);

                VehicleTextures vehicleTextures = new VehicleTextures();
                ScreenTextures screenTextures = new ScreenTextures();
                Font font = new Font(camera, screenTextures.Font);

                // ----------------------------------------------------------------------------------------
                // 6. Shared mutable collections. Parity-critical: a SINGLE patchSaveList instance is shared by
                //    every CPatches (the original CPatches stored mf.patchSaveList by reference), and triStrip
                //    is seeded with one initial patch exactly as FormGPS did. flagPts is one shared list used by
                //    both FieldIoService (persistence) and the flag render overlay below.
                // ----------------------------------------------------------------------------------------
                List<List<vec3>> patchSaveList = new List<List<vec3>>();
                List<CFlag> flagPts = new List<CFlag>();

                // ----------------------------------------------------------------------------------------
                // 7. Deferred locals for the circular dependencies. Declared null first, then assigned once
                //    their collaborators exist; the closures below capture the variables (by reference) and are
                //    only ever invoked long after composition completes.
                // ----------------------------------------------------------------------------------------
                PgnDispatcher pgn = null;
                SectionService sections = null;
                PositionService position = null;
                RenderCoordinator render = null;

                // ----------------------------------------------------------------------------------------
                // 8. Commands wiring the remote/switch inputs to the services. RelayCommand defers to the
                //    deferred service locals (null-conditional until those are assigned).
                // ----------------------------------------------------------------------------------------
                RelayCommand autoSteerToggleCommand = new RelayCommand(() => position?.OnRequestAutoSteerToggle?.Invoke());
                RelayCommand sectionMasterManualCommand = new RelayCommand(() => sections?.PerformSectionMasterManual());
                RelayCommand sectionMasterAutoCommand = new RelayCommand(() => sections?.PerformSectionMasterAuto());

                // ----------------------------------------------------------------------------------------
                // 9. Domain C-classes, in dependency (topological) order. These are behaviour-frozen; only
                //    their construction moves here from the FormGPS constructor.
                // ----------------------------------------------------------------------------------------
                CAHRS ahrs = new CAHRS();
                CSound sounds = new CSound();
                CModuleComm mc = new CModuleComm(appModel, ahrs, autoSteerToggleCommand, sectionMasterManualCommand, sectionMasterAutoCommand);
                CSim sim = new CSim(appModel, ahrs, mc);
                CNMEA pn = new CNMEA(appModel, sim, worldGrid);
                CVehicle vehicle = new CVehicle(appModel, vehicleTextures, screenTextures, camera, mc, sim);

                // Section array (MAXSECTIONS = 64; 1–16 unique / up to 64 same-width per AAP §0.2.2).
                CSection[] section = new CSection[64];
                for (int i = 0; i < section.Length; i++)
                {
                    section[i] = new CSection();
                }

                CTool tool = new CTool(appModel, section, vehicle, camera, vehicleTextures, sim, mc);
                CBoundary bnd = new CBoundary(appModel, section, tool, mc, sounds, vehicle, errorPresenter);
                CTram tram = new CTram(bnd, tool, camera);
                CABLine ABLine = new CABLine(appModel, camera, vehicle, tool, tram, ahrs, mc);
                CABCurve curve = new CABCurve(appModel, camera, vehicle, tool, tram, ahrs, mc, errorPresenter, autoSteerToggleCommand);
                CContour ct = new CContour(appModel, vehicle, tool, pn, ahrs, ABLine);
                CYouTurn yt = new CYouTurn(appModel, vehicle, tool, mc, sounds);
                CTrack trk = new CTrack(tool, appModel);
                CRecordedPath recPath = new CRecordedPath(appModel, sim);
                CFieldData fd = new CFieldData(appModel, tool, bnd);
                CSmartWAS smartWAS = new CSmartWAS(appModel);

                // CISOBUS sends through the dispatcher; pgn is still null here, so the closure defers the call
                // until pgn is assigned (step 10). This breaks the PgnDispatcher <-> CISOBUS cycle.
                CISOBUS isobus = new CISOBUS(appModel, b => pgn.SendPgnToLoop(b));
                CHeadLine hdl = new CHeadLine();

                // triStrip seeded with one patch (parity with FormGPS: triStrip = new List<CPatches>{ new CPatches(this) }).
                List<CPatches> triStrip = new List<CPatches>
                {
                    new CPatches(appModel, tool, section, fd, patchSaveList)
                };

                // AgShare cloud client (disabled by default — AgShareApiKey empty, AgShareEnabled=false).
                AgShareClient agShareClient = new AgShareClient(
                    Properties.Settings.Default.AgShareServer,
                    Properties.Settings.Default.AgShareApiKey);

                // ----------------------------------------------------------------------------------------
                // 10. Extracted services (behaviour frozen). Construction order pgn -> sections -> position ->
                //     fieldIo -> render satisfies the inter-service references; the deferred locals are assigned
                //     as each is built so the closures above resolve at runtime.
                // ----------------------------------------------------------------------------------------
                pgn = new PgnDispatcher(
                    ahrs, mc, pn, isobus, trk, appModel,
                    a => Dispatcher.UIThread.Post(a),
                    m => errorPresenter.PresentTimedMessage(TimeSpan.FromSeconds(3), "AgIO", m));
                _pgnDispatcher = pgn;

                sections = new SectionService(
                    section, tool, tram, mc, sounds, trk, pgn, appModel,
                    getABLineHowManyPathsAway: () => ABLine.howManyPathsAway,
                    getCurveHowManyPathsAway: () => curve.howManyPathsAway);

                position = new PositionService(
                    pn, ahrs, vehicle, tool, section, trk, ABLine, curve, ct, bnd, yt, recPath,
                    triStrip, fd, mc, sounds, isobus, smartWAS, appCore, pgn, sections);

                FieldIoService fieldIo = new FieldIoService(
                    pn, fd, triStrip, ct, flagPts, bnd, tram, recPath, trk, hdl, ABLine, worldGrid,
                    position.sbGrid, appCore, agShareClient,
                    pickFieldFile: startDir => PickFieldFile(startDir),
                    onJobNew: () => appCore.AppModel.Fields.OpenField(),
                    recalcFieldBounds: () => render.CalculateMinMax(),
                    isJobStarted: () => appCore.AppModel.isJobStarted,
                    reportFileProblem: (title, message, isError) =>
                        errorPresenter.PresentTimedMessage(TimeSpan.FromSeconds(isError ? 4 : 2), title, message),
                    showTimedMessage: (timeoutMs, title, message) =>
                        errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(timeoutMs), title, message),
                    onBeforeCloseField: null,
                    onAfterCloseField: null);

                render = new RenderCoordinator(
                    camera, worldGrid, section, tool, tram, bnd, yt, trk, triStrip, vehicle, isobus, pn,
                    font, screenTextures, vehicleTextures, appModel, sections,
                    getIsContourBtnOn: () => ct.isContourBtnOn,
                    getIsDrivingRecordedPath: () => recPath.isDrivingRecordedPath,
                    getSteerSwitchHigh: () => mc.steerSwitchHigh,
                    getIsOutOfBounds: () => mc.isOutOfBounds,
                    getIsRtkAlarming: () => sounds.isRTKAlarming,
                    getABLineIsHeadingSameWay: () => ABLine.isHeadingSameWay,
                    getCurveIsHeadingSameWay: () => curve.isHeadingSameWay,
                    getAbLineWidth: () => ABLine.lineWidth,
                    getImuRoll: () => ahrs.imuRoll,
                    getActualSteerAngleDegrees: () => mc.actualSteerAngleDegrees,
                    getCosSectionHeading: () => Math.Cos(-appModel.ToolPivotHeading.AngleInRadians),
                    getSinSectionHeading: () => Math.Sin(-appModel.ToolPivotHeading.AngleInRadians),
                    getABLineHowManyPathsAway: () => ABLine.howManyPathsAway,
                    getCurveHowManyPathsAway: () => curve.howManyPathsAway,
                    getGoalPointAB: () => ABLine.goalPointAB,
                    getGoalPointCu: () => curve.goalPointCu,
                    getWorkedAreaTotal: () => fd.workedAreaTotal,
                    setOverlapResult: (area, overlap) => { fd.actualAreaCovered = area; fd.overlapPercent = overlap; },
                    createPatch: () => new CPatches(appModel, tool, section, fd, patchSaveList),
                    drawGuidanceLines: DrawGuidanceLines,
                    drawFlagsOverlay: DrawFlagsOverlay,
                    checkWorkAndSteerSwitch: CheckWorkAndSteerSwitch,
                    loadVehicleTextures: LoadVehicleTextures,
                    setZoom: SetZoom);

                // ----------------------------------------------------------------------------------------
                // 11. Main window — the Avalonia kiosk shell. The behaviour-frozen services are injected; the
                //     Core view-model is supplied as the DataContext; the shared camera and simulator state are
                //     set through the public properties. The presenters receive it as their dialog owner.
                // ----------------------------------------------------------------------------------------
                MainView mainView = new MainView(position, pgn, sections, fieldIo, render)
                {
                    Camera = camera,
                    IsSimulatorActive = Properties.Settings.Default.setMenu_isSimulatorOn,
                    DataContext = appCore.AppViewModel,
                };

                panelPresenter.Owner = mainView;
                errorPresenter.Owner = mainView;

                desktop.MainWindow = mainView;

                // ----------------------------------------------------------------------------------------
                // 12. Day/night theming. The Core view-model's IsDay/IsMetric seed from Settings, and the
                //     RequestedThemeVariant follows IsDay so the App.axaml ThemeDictionaries flip. ApplyTheme
                //     is idempotent (it only re-applies when IsDay actually changes), because the view-model
                //     raises PropertyChanged via NotifyAllPropertiesChanged (no specific property name to filter).
                // ----------------------------------------------------------------------------------------
                ApplicationViewModel appViewModel = appCore.AppViewModel;
                appViewModel.IsMetric = Properties.Settings.Default.setMenu_isMetric;
                appViewModel.IsDay = Properties.Settings.Default.setDisplay_isDayMode;

                bool? lastAppliedIsDay = null;
                void ApplyTheme()
                {
                    bool isDay = appViewModel.IsDay;
                    if (lastAppliedIsDay == isDay)
                    {
                        return;
                    }

                    lastAppliedIsDay = isDay;
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        SetDayNightMode(isDay);
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() => SetDayNightMode(isDay));
                    }
                }

                ApplyTheme();
                appViewModel.PropertyChanged += (_, __) => ApplyTheme();

                // ----------------------------------------------------------------------------------------
                // 13. Start the loopback UDP server (frozen transport: receive on 127.0.0.1:15555, peer
                //     127.255.255.255:17777, <=70 ms throttle). FormGPS started this at load; nothing else does.
                // ----------------------------------------------------------------------------------------
                pgn.StartLoopbackServer();

                // ----------------------------------------------------------------------------------------
                // 14. Shutdown teardown. The single-instance lock is owned and released exactly once by
                //     Program.Main's finally block (preserving AgIO Restart() semantics), so App must not touch
                //     it. PgnDispatcher exposes no Dispose/Stop — its loopback socket is reclaimed by the OS at
                //     process exit — so the only action is to drop the rooted reference. MainView.OnViewClosing
                //     already persists camera pitch/zoom and saves Settings.
                // ----------------------------------------------------------------------------------------
                desktop.ShutdownRequested += (_, __) => { _pgnDispatcher = null; };

                // ----------------------------------------------------------------------------------------
                //  Local render delegates — ported verbatim (behaviour) from the FormGPS paint regions and
                //  OpenGL.Designer.cs. They are local functions so they close over the domain locals above and
                //  the deferred `render` (assigned in step 10, invoked only during GL paint).
                // ----------------------------------------------------------------------------------------

                // Guidance lines (was the "Guidance Lines" paint region). Uses the migrated parameterised draw
                // signatures; the camera heading / pivot are read from the render coordinator's per-frame state.
                void DrawGuidanceLines()
                {
                    if (ct.isContourBtnOn)
                    {
                        ct.DrawContourLine();
                    }
                    else if (trk.idx > -1)
                    {
                        if (trk.gArr[trk.idx].mode == TrackMode.AB)
                        {
                            ABLine.DrawABLines(font, render.CamHeading);
                        }
                        else
                        {
                            curve.DrawCurve(font, render.CamHeading);
                        }
                    }

                    if (curve.isMakingCurve)
                    {
                        curve.DrawCurveNew(render.PivotAxlePos);
                    }

                    if (ABLine.isMakingABLine)
                    {
                        ABLine.DrawABLineNew(font, render.CamHeading);
                    }

                    recPath.DrawRecordedLine();
                    recPath.DrawDubins();
                }

                // Flags overlay (was the "Flags" paint region + DrawFlags()). Draws every field flag, then the
                // dashed line from the pivot axle to the picked flag.
                void DrawFlagsOverlay()
                {
                    if (flagPts.Count > 0)
                    {
                        DrawFlagsPorted();
                    }

                    try
                    {
                        if (render.FlagNumberPicked > 0)
                        {
                            GL.LineWidth(ABLine.lineWidth);
                            GL.Enable(EnableCap.LineStipple);
                            GL.LineStipple(1, 0x0707);
                            GL.Begin(PrimitiveType.Lines);
                            GL.Color3(0.930f, 0.72f, 0.32f);
                            GL.Vertex3(render.PivotAxlePos.easting, render.PivotAxlePos.northing, 0);
                            GL.Vertex3(flagPts[render.FlagNumberPicked - 1].easting, flagPts[render.FlagNumberPicked - 1].northing, 0);
                            GL.End();
                            GL.Disable(EnableCap.LineStipple);
                        }
                    }
                    catch
                    {
                        // Parity: the original wrapped the picked-flag line in try/catch and swallowed faults
                        // (e.g. a transient index race against flag editing) so a single bad frame never crashes
                        // the render loop.
                    }
                }

                // Ported FormGPS.DrawFlags(): a coloured point + label per flag (colour 0=red/1=green/2=yellow),
                // the flag ID encoded into the blue channel for back-buffer picking, and a selection diamond
                // around the picked flag. GLW has no line-loop primitive, so the box uses raw GL like the original.
                void DrawFlagsPorted()
                {
                    try
                    {
                        int flagCnt = flagPts.Count;
                        for (int f = 0; f < flagCnt; f++)
                        {
                            CFlag flag = flagPts[f];

                            ColorRgba flagColorRgb;
                            string flagPrefix;
                            if (flag.color == 0)
                            {
                                flagColorRgb = new ColorRgba((byte)255, (byte)0, (byte)0);
                                flagPrefix = "&";
                            }
                            else if (flag.color == 1)
                            {
                                flagColorRgb = new ColorRgba((byte)0, (byte)255, (byte)0);
                                flagPrefix = "|";
                            }
                            else
                            {
                                flagColorRgb = new ColorRgba((byte)255, (byte)255, (byte)0);
                                flagPrefix = "~";
                            }

                            // Encode the flag ID into the blue channel for the back-buffer pixel pick (parity).
                            flagColorRgb.Blue = (byte)flag.ID;

                            GL.PointSize(8.0f);
                            GLW.SetColor(flagColorRgb);
                            GL.Begin(PrimitiveType.Points);
                            GL.Vertex3(flag.easting, flag.northing, 0);
                            GL.End();

                            font.DrawText3D(flag.easting, flag.northing, flagPrefix + flag.notes, render.CamHeading);
                        }

                        if (render.FlagNumberPicked != 0)
                        {
                            double offSet = camera.ZoomValue * camera.ZoomValue * 0.01;
                            CFlag picked = flagPts[render.FlagNumberPicked - 1];

                            GLW.SetLineWidth(4.0f);
                            GLW.SetColor(Colors.FlagSelectedBoxColor);

                            GL.Begin(PrimitiveType.LineLoop);
                            GL.Vertex3(picked.easting - offSet, picked.northing, 0);
                            GL.Vertex3(picked.easting, picked.northing + offSet, 0);
                            GL.Vertex3(picked.easting + offSet, picked.northing, 0);
                            GL.Vertex3(picked.easting, picked.northing - offSet, 0);
                            GL.End();
                        }
                    }
                    catch
                    {
                        // Parity: FormGPS.DrawFlags wrapped its whole body in try/catch.
                    }
                }

                // Work/steer switch poll (was the inline gate in OpenGL.Designer.cs). Only forwards to the module
                // comm when an autosteer or remote-work source is active, exactly as the original guarded it.
                void CheckWorkAndSteerSwitch()
                {
                    if (ahrs.isAutoSteerAuto || mc.isRemoteWorkSystemOn)
                    {
                        mc.CheckWorkAndSteerSwitch();
                    }
                }

                // Vehicle textures (was FormGPS.SetVehicleTextures()). Pre-touches the lazily-uploaded textures so
                // their GL handles exist before first paint. The four brand textures (Tractor/Harvester/
                // ArticulatedFront/ArticulatedRear) intentionally remain deferred empty placeholders in the
                // migration: VehicleTextures exposes no SetBitmap and the per-brand bitmaps are surfaced by the
                // config UI preview, so forcing a GL upload here would serve no renderer need.
                void LoadVehicleTextures()
                {
                    _ = vehicleTextures.FrontWheel;
                    _ = vehicleTextures.Tire;
                    _ = vehicleTextures.ToolAxle;
                    _ = vehicleTextures.Tractor;
                    _ = vehicleTextures.Harvester;
                    _ = vehicleTextures.ArticulatedFront;
                    _ = vehicleTextures.ArticulatedRear;
                }

                // Zoom (grid-spacing residual of FormGPS.SetZoom). The projection matrix itself is rebuilt by
                // RenderCoordinator.OnViewportResize on the Avalonia GL surface, so only the world-grid step that
                // depended on the camera distance and tool width is reproduced here (parity).
                void SetZoom()
                {
                    double gridStep = camera.camSetDistance / -15.0;
                    int gridToolSpacing = (int)(gridStep / tool.width + 0.5);
                    if (gridToolSpacing < 1)
                    {
                        gridToolSpacing = 1;
                    }

                    worldGrid.GridStep = gridToolSpacing * tool.width;
                }

                // [XPLAT] Field.txt picker — replaces the WinForms OpenFileDialog. Runs the Avalonia async
                // StorageProvider picker to completion on the UI thread using the DispatcherFrame pump idiom
                // established by FormDialogView, so the behaviour-frozen FieldIoService keeps its synchronous
                // Func<string,string> contract (the chosen path, or the literal "Cancel").
                string PickFieldFile(string startDir)
                {
                    TopLevel topLevel = desktop.MainWindow != null ? TopLevel.GetTopLevel(desktop.MainWindow) : null;
                    if (topLevel?.StorageProvider == null || !topLevel.StorageProvider.CanOpen)
                    {
                        return "Cancel";
                    }

                    FilePickerOpenOptions options = new FilePickerOpenOptions
                    {
                        Title = "Select Field.txt",
                        AllowMultiple = false,
                    };

                    if (!string.IsNullOrEmpty(startDir))
                    {
                        try
                        {
                            IStorageFolder startFolder = topLevel.StorageProvider
                                .TryGetFolderFromPathAsync(startDir).GetAwaiter().GetResult();
                            if (startFolder != null)
                            {
                                options.SuggestedStartLocation = startFolder;
                            }
                        }
                        catch
                        {
                            // Best-effort start-location hint; ignore an unresolvable path.
                        }
                    }

                    Task<IReadOnlyList<IStorageFile>> pickTask = topLevel.StorageProvider.OpenFilePickerAsync(options);

                    if (!pickTask.IsCompleted)
                    {
                        DispatcherFrame frame = new DispatcherFrame();
                        pickTask.ContinueWith(
                            _ => Dispatcher.UIThread.Post(() => frame.Continue = false),
                            TaskScheduler.Default);
                        Dispatcher.UIThread.PushFrame(frame);
                    }

                    IReadOnlyList<IStorageFile> files = pickTask.GetAwaiter().GetResult();
                    if (files == null || files.Count == 0)
                    {
                        return "Cancel";
                    }

                    return files[0].Path.LocalPath;
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Sets the application theme variant from the day/night state: Light for day, Dark for night, so the
        /// <c>App.axaml</c> theme dictionaries flip. Re-applying the same variant is skipped to avoid theme-
        /// dictionary churn.
        /// </summary>
        /// <param name="isDay"><see langword="true"/> for day (Light), <see langword="false"/> for night (Dark).</param>
        private void SetDayNightMode(bool isDay)
        {
            ThemeVariant target = isDay ? ThemeVariant.Light : ThemeVariant.Dark;
            if (!Equals(RequestedThemeVariant, target))
            {
                RequestedThemeVariant = target;
            }
        }

        /// <summary>
        /// Static convenience entry point that lets non-composition callers (e.g. a settings dialog) flip the
        /// global day/night theme through the running <see cref="App"/> instance.
        /// </summary>
        /// <param name="isDay"><see langword="true"/> for day (Light), <see langword="false"/> for night (Dark).</param>
        public static void ApplyDayNightMode(bool isDay)
        {
            if (Current is App app)
            {
                app.SetDayNightMode(isDay);
            }
        }

        /// <summary>
        /// [XPLAT] Normalises an Avalonia <see cref="Bitmap"/> into a tightly-packed RGBA8888 (un-premultiplied)
        /// pixel buffer for an OpenGL texture upload, mirroring <c>VehicleTextures.LoadTexture</c> so every image
        /// crossing the Avalonia/Skia → OpenTK boundary is decoded identically. Used here to feed the
        /// <see cref="WorldGrid"/> floor texture.
        /// </summary>
        /// <param name="image">The source Avalonia bitmap (an <c>avares://</c> asset).</param>
        /// <param name="width">Receives the decoded pixel width.</param>
        /// <param name="height">Receives the decoded pixel height.</param>
        /// <returns>A managed RGBA8888 byte array (4 bytes per pixel, row-major).</returns>
        private static byte[] LoadRgbaPixels(Bitmap image, out int width, out int height)
        {
            using (MemoryStream png = new MemoryStream())
            {
                image.Save(png);
                png.Position = 0;
                using (SKBitmap decoded = SKBitmap.Decode(png))
                {
                    SKImageInfo info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    using (SKBitmap rgba = new SKBitmap(info))
                    {
                        using (SKCanvas canvas = new SKCanvas(rgba))
                        {
                            canvas.Clear(SKColors.Transparent);
                            canvas.DrawBitmap(decoded, 0, 0);
                        }

                        // rgba.Bytes returns a fresh managed copy, so it stays valid after the SKBitmap is disposed.
                        width = info.Width;
                        height = info.Height;
                        return rgba.Bytes;
                    }
                }
            }
        }
    }
}
