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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using AgLibrary.Logging;

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
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.ViewModels;

using AgOpenGPS.Services;
using AgOpenGPS.Views;
// [XPLAT] The dialog-navigation closures (below) construct migrated editors that live in the
// Views sub-namespaces: FormSteerView / FormSteerWizView / FormSimCoordsView and the Steer*
// adapters sit in AgOpenGPS.Views.Settings; the colour/record pickers in AgOpenGPS.Views.Pickers.
// The Guidance/Field dialogs are declared in the flat AgOpenGPS.Views namespace (already imported).
using AgOpenGPS.Views.Settings;
using AgOpenGPS.Views.Pickers;

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

                // [XPLAT] Reuse the SINGLE process-level IPlatformServices instance created by Program.Main
                // rather than calling PlatformServicesFactory.Create() a second time. Program owns the
                // single-instance guard ON that very instance (Program._instanceLock), and the per-OS
                // implementations carry process-level state (config root, lock-file/Mutex handle). Creating a
                // second instance here would risk split process state for locks/config roots and violate the
                // intended single platform-service-instance semantics (review finding App.axaml.cs MAJOR L155).
                // The null-coalescing fallback to Create() keeps non-Program entry paths (XAML previewer / test
                // host) working; in those paths EnsurePlatformServicesRegistered() above guarantees a factory.
                IPlatformServices platform = Program.PlatformServices ?? PlatformServicesFactory.Create();

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

                // ----------------------------------------------------------------------------------------
                // [XPLAT] Field-close lifecycle hooks (AAP G2/G5, review App.axaml.cs CRITICAL "FieldIoService
                //   close lifecycle hooks are null"). FieldIoService deliberately owns only the field-SAVE batch;
                //   the cross-cutting close orchestration is surfaced as these two optional hooks the composition
                //   root attaches (see FieldIoService remarks). They reproduce — behaviour-frozen — the WinForms
                //   close lifecycle that lived in Controls.Designer.cs FileSaveEverythingBeforeClosingField (the
                //   pre-save stops) and FormGPS.cs JobClose() (the post-save domain reset). The WinForms build ran
                //   JobClose() inside this.Invoke(...) on the UI thread, so both hooks marshal onto the Avalonia UI
                //   thread via Dispatcher.UIThread.Invoke (synchronous; runs inline when already on the UI thread,
                //   else marshals-and-blocks — matching Control.Invoke and avoiding races with the GL render loop).
                //
                //   Two WinForms close branches are intentionally inert in the migration and therefore omitted here:
                //   the Easy-Drive branch (isEasyDriveMode is feature-gated to a constant false — see FormSteerView)
                //   and the AgShare close-time AUTO-upload (gated on AgShareEnabled && AgShareUploadActive, both
                //   default false; the field-work snapshot auto-capture was not migrated — the manual
                //   FormAgShareUploaderView is the migrated AgShare entry point). The view-side UI reset (current-
                //   field readout) is delegated to MainView via the deferred reference assigned in step 11.
                // ----------------------------------------------------------------------------------------
                MainView mainViewRef = null;

                // Pre-save: close in-progress geometry and turn sections/mapping off BEFORE the field is serialized,
                // so the saved boundary/contour/coverage/sections are complete and not mid-stroke (parity with the
                // net48 pre-save block in FileSaveEverythingBeforeClosingField).
                void BeforeCloseField()
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        // close any open contour line so the saved contour strip is finalized
                        if (ct.isContourOn) ct.StopContourLine();

                        // turn the section masters off (was btnSectionMasterAuto/Manual.PerformClick() — the
                        // migrated PerformSectionMaster* are the same toggles; the second check sees the state the
                        // first already updated, exactly as the WinForms guards did)
                        if (appModel.autoBtnState == btnStates.Auto) sections.PerformSectionMasterAuto();
                        if (appModel.manualBtnState == btnStates.On) sections.PerformSectionMasterManual();

                        // cancel any pending per-section on/off requests
                        for (int j = 0; j < tool.numOfSections; j++)
                        {
                            section[j].sectionOnOffCycle = false;
                            section[j].sectionOffRequest = false;
                        }

                        // turn mapping off (close open coverage patches) before the save serializes them
                        for (int j = 0; j < triStrip.Count; j++)
                        {
                            if (triStrip[j].isDrawing) triStrip[j].TurnMappingOff();
                        }
                    });
                }

                // Post-save: the JobClose() domain reset — clears all field-scoped state so the next field opens
                // clean. Behaviour-frozen against FormGPS.cs JobClose(); the WinForms button-image/visibility/panel
                // mutations are view concerns (the section/master events already update the view; the current-field
                // readout is reset via MainView.OnFieldClosedResetUi()).
                void AfterCloseField()
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        // recorded path
                        recPath.resumeState = 0;
                        recPath.currentPositonIndex = 0;
                        recPath.recList.Clear();
                        recPath.StopDrivingRecordedPath();
                        recPath.shortestDubinsList?.Clear();
                        recPath.shuttleDubinsList?.Clear();

                        // grid-scale string buffer (the single instance shared with PositionService)
                        position.sbGrid.Clear();

                        // reset field drift offsets unless the operator chose to keep them (FormGPS: isKeepOffsetsOn,
                        // relocated onto ApplicationModel — the canonical home documented for this very reset)
                        if (!appModel.isKeepOffsetsOn)
                        {
                            appModel.SharedFieldProperties.DriftCompensation = new GeoDelta(0.0, 0.0);
                        }

                        // headland + boundaries
                        bnd.isHeadlandOn = false;
                        bnd.bndList.Clear();

                        // hydraulic lift off — domain source of truth; the machine PGN hydLift byte is rebuilt from
                        // this flag on the next machine-byte build (was also p_239.pgn[hydLift] = 0)
                        vehicle.isHydLiftOn = false;

                        // close the field model (clears ActiveField)
                        appModel.Fields.CloseField();

                        // force section masters off and drive all sections/zones to Off
                        appModel.autoBtnState = btnStates.Off;
                        appModel.manualBtnState = btnStates.Off;
                        if (tool.isSectionsNotZones) sections.AllSectionsToState(btnStates.Off);
                        else sections.AllZonesToState(btnStates.Off);

                        // applied-coverage patches: clear every strip then seed one fresh patch (parity with
                        // triStrip.Clear() + triStrip.Add(new CPatches(...)); the same shared patchSaveList is reused)
                        for (int j = 0; j < triStrip.Count; j++)
                        {
                            triStrip[j].patchList?.Clear();
                            triStrip[j].triangleList?.Clear();
                            triStrip[j].isDrawing = false;
                            triStrip[j].numTriangles = 0;
                        }
                        triStrip.Clear();
                        triStrip.Add(new CPatches(appModel, tool, section, fd, patchSaveList));

                        // flags
                        flagPts.Clear();

                        // tramlines
                        tram.tramList?.Clear();
                        tram.displayMode = 0;
                        tram.generateMode = 0;
                        tram.tramBndInnerArr?.Clear();
                        tram.tramBndOuterArr?.Clear();

                        // curve
                        curve.ResetCurveLine();

                        // tracks
                        trk.gArr?.Clear();
                        trk.idx = -1;

                        // contour
                        ct.ResetContour();
                        ct.isContourBtnOn = false;
                        ct.isContourOn = false;

                        // autosteer + youturn domain flags off
                        appModel.isBtnAutoSteerOn = false;
                        yt.isYouTurnBtnOn = false;
                        yt.ResetYouTurn();

                        // worked-area counters
                        fd.workedAreaTotal = 0;
                        fd.UpdateFieldBoundaryGUIAreas();

                        // background imagery
                        worldGrid.BingMap = null;

                        // ISOBUS field-name reset (Task Controller)
                        isobus.SendFieldName(string.Empty);

                        // view-side reset (current-field readout) — was Text = "AgOpenGPS" / panel disables in JobClose
                        mainViewRef?.OnFieldClosedResetUi();
                    });
                }

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
                    onBeforeCloseField: BeforeCloseField,
                    onAfterCloseField: AfterCloseField);

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

                // [XPLAT] Publish the main view to the field-close hooks (declared before FieldIoService so they
                // could be passed to its constructor). The hooks are only invoked at runtime during a field close,
                // by which point this assignment has run; AfterCloseField calls mainViewRef.OnFieldClosedResetUi().
                mainViewRef = mainView;

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
                // 12b. [XPLAT] Operator-action command map (AAP G2 / R1, finding MV-1..MV-3, GPSD-1).
                //      FormGPS wired ~70 buttons/hotkeys to `btnXxx_Click` handlers in Controls.Designer.cs /
                //      GUI.Designer.cs. Each handler is reproduced here as a closure over the behaviour-frozen
                //      domain objects (Extract Method, AAP §0.6.1). The *domain* mutation — including sounds,
                //      timed messages, and cross-button effects (e.g. turning contour off also disengages
                //      autosteer) — lives in these closures verbatim; the resulting visual state is returned so
                //      MainView (which owns the controls) swaps the button face exactly as the WinForms handler
                //      set `btnXxx.Image`. Safety-critical autosteer (R3) is funnelled through ToggleAutoSteer.
                // ----------------------------------------------------------------------------------------

                // Timed-message sink — the migrated equivalent of FormGPS.TimedMessageBox(ms, title, msg).
                void ShowTimed(int milliseconds, string title, string message) =>
                    errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(milliseconds), title, message);

                // --- Cross-calling domain actions as local functions (so contour/cycle/path can disengage
                //     autosteer and youturn exactly as the WinForms `btnXxx.PerformClick()` calls did). ---

                // btnAutoSteer_Click (Controls.Designer.cs) verbatim domain. The over-speed guard is enforced
                // only when not simulating (was `if (!timerSim.Enabled)`); all three sound plays stay gated on
                // sounds.isSteerSoundOn exactly as the original. Returns the resulting engaged state.
                bool ToggleAutoSteerDomain()
                {
                    render.ResetLongAvgPivDistance();   // [XPLAT] was: longAvgPivDistance = 0;

                    if (!(position.getIsSimActive?.Invoke() ?? false))
                    {
                        if (appModel.avgSpeed > vehicle.maxSteerSpeed)
                        {
                            if (appModel.isBtnAutoSteerOn)
                            {
                                appModel.isBtnAutoSteerOn = false;
                                if (sounds.isSteerSoundOn) sounds.sndAutoSteerOff.Play();
                            }

                            Log.EventWriter("Steer Off, Above Max Safe Speed for Autosteer");

                            if (appViewModel.IsMetric)
                                ShowTimed(3000, "AutoSteer Disabled", "Above Maximum Safe Steering Speed: " + vehicle.maxSteerSpeed.ToString("N0") + " Kmh");
                            else
                                ShowTimed(3000, "AutoSteer Disabled", "Above Maximum Safe Steering Speed: " + Speed.KmhToMph(vehicle.maxSteerSpeed).ToString("N1") + " MPH");

                            return appModel.isBtnAutoSteerOn;
                        }
                    }

                    if (appModel.isBtnAutoSteerOn)
                    {
                        appModel.isBtnAutoSteerOn = false;
                        if (sounds.isSteerSoundOn) sounds.sndAutoSteerOff.Play();
                    }
                    else
                    {
                        if (ct.isContourBtnOn || trk.idx > -1)
                        {
                            appModel.isBtnAutoSteerOn = true;
                            if (sounds.isSteerSoundOn) sounds.sndAutoSteerOn.Play();
                            if (yt.isYouTurnBtnOn) yt.ResetYouTurn();
                        }
                        else
                        {
                            ShowTimed(2000, gStr.gsNoGuidanceLines, gStr.gsTurnOnContourOrMakeABLine);
                        }
                    }

                    return appModel.isBtnAutoSteerOn;
                }

                // btnAutoYouTurn_Click verbatim domain. Returns the resulting on state.
                bool ToggleYouTurnDomain()
                {
                    yt.isTurnCreationTooClose = false;

                    if (bnd.bndList.Count == 0)
                    {
                        ShowTimed(2000, gStr.gsNoBoundary, gStr.gsCreateABoundaryFirst);
                        return yt.isYouTurnBtnOn;   // false
                    }

                    yt.turnTooCloseTrigger = false;

                    if (!yt.isYouTurnBtnOn)
                    {
                        yt.ResetCreatedYouTurn();
                        if (trk.idx == -1) return yt.isYouTurnBtnOn;   // false
                        yt.isYouTurnBtnOn = true;
                        yt.isTurnCreationTooClose = false;
                        yt.isTurnCreationNotCrossingError = false;
                        yt.ResetYouTurn();
                    }
                    else
                    {
                        yt.isYouTurnBtnOn = false;
                        yt.RestorePreTriggerState();
                        yt.ResetYouTurn();
                        yt.ResetCreatedYouTurn();
                    }

                    return yt.isYouTurnBtnOn;
                }

                // btnContour_Click verbatim domain. Returns the resulting contour on state.
                bool ToggleContourDomain()
                {
                    if (trk.idx != -1) trk.idx = -1;
                    trk.isAutoTrack = false;

                    ct.isContourBtnOn = !ct.isContourBtnOn;
                    if (ct.isContourBtnOn)
                    {
                        // (WinForms DisableYouTurnButtons() — the youturn/track button enables are recomputed
                        //  by MainView.RefreshGuidanceFaces from the resulting domain state.)
                        position.guidanceLookAheadTime = 0.5;
                        ct.isLocked = false;
                    }
                    else
                    {
                        ABLine.isABValid = false;
                        curve.isCurveValid = false;
                        ct.isLocked = false;
                        position.guidanceLookAheadTime = Properties.Settings.Default.setAS_guidanceLookAheadTime;

                        if (appModel.isBtnAutoSteerOn)
                        {
                            ToggleAutoSteerDomain();   // was: btnAutoSteer.PerformClick();
                            ShowTimed(2000, gStr.gsGuidanceStopped, gStr.gsContourOn);
                        }
                    }

                    return ct.isContourBtnOn;
                }

                // btnCycleLines_Click verbatim domain. Advances to the next *visible* track, disengaging
                // autosteer and youturn as the original did. Returns the newly-active track name (for the flash),
                // or null when nothing changed. The visible-track search is bounded by the track count so a
                // pathological all-hidden list can never hang (identical result for any valid track set).
                string CycleLinesDomain()
                {
                    trk.isAutoTrack = false;
                    string flash = null;

                    if (trk.gArr.Count > 1)
                    {
                        for (int guard = 0; guard < trk.gArr.Count; guard++)
                        {
                            trk.idx++;
                            if (trk.idx == trk.gArr.Count) trk.idx = 0;
                            if (trk.gArr[trk.idx].isVisible) { flash = trk.gArr[trk.idx].name; break; }
                        }

                        if (appModel.isBtnAutoSteerOn)
                        {
                            ToggleAutoSteerDomain();
                            ShowTimed(2000, gStr.gsGuidanceStopped, "Track Changed");
                        }
                        if (yt.isYouTurnBtnOn) ToggleYouTurnDomain();
                    }

                    ABLine.isABValid = false;
                    curve.isCurveValid = false;
                    return flash;
                }

                // btnCycleLinesBk_Click verbatim domain. When contour is on it locks to line instead (original
                // behaviour) and changes no track. Note the WinForms asymmetry: the backward cycle does NOT
                // toggle youturn off, unlike the forward cycle — preserved here.
                string CycleLinesBackDomain()
                {
                    if (ct.isContourBtnOn) { ct.SetLockToLine(); return null; }

                    trk.isAutoTrack = false;
                    string flash = null;

                    if (trk.gArr.Count > 1)
                    {
                        for (int guard = 0; guard < trk.gArr.Count; guard++)
                        {
                            trk.idx--;
                            if (trk.idx == -1) trk.idx = trk.gArr.Count - 1;
                            if (trk.gArr[trk.idx].isVisible) { flash = trk.gArr[trk.idx].name; break; }
                        }

                        if (appModel.isBtnAutoSteerOn)
                        {
                            ToggleAutoSteerDomain();
                            ShowTimed(2000, gStr.gsGuidanceStopped, "Track Changed");
                        }
                    }

                    ABLine.isABValid = false;
                    curve.isCurveValid = false;
                    return flash;
                }

                // btnPathGoStop_Click verbatim domain. Turns off all guidance first (contour/youturn/autosteer
                // and the active track), then starts or stops recorded-path driving. Returns the resulting
                // driving state. The button image/enable swaps are MainView.RefreshPathFaces concerns.
                bool PathGoStopDomain()
                {
                    if (ct.isContourBtnOn) ToggleContourDomain();
                    if (yt.isYouTurnBtnOn) ToggleYouTurnDomain();
                    if (appModel.isBtnAutoSteerOn)
                    {
                        ToggleAutoSteerDomain();
                        ShowTimed(2000, gStr.gsGuidanceStopped, "Paths Enabled");
                        Log.EventWriter("Autosteer On While Enable Paths");
                    }
                    if (trk.idx > -1) trk.idx = -1;

                    if (recPath.isDrivingRecordedPath)
                    {
                        recPath.StopDrivingRecordedPath();
                        return false;
                    }

                    if (!recPath.StartDrivingRecordedPath())
                    {
                        recPath.StopDrivingRecordedPath();
                        ShowTimed(1500, gStr.gsProblemMakingPath, gStr.gsCouldntGenerateValidPath);
                        return false;
                    }

                    return true;
                }

                // GPS-data and field-statistics windows are non-modal; track the single open instance so a
                // second press closes it (reachability for finding GPSD-1 / MV-2).
                FormGPSDataView gpsDataWindow = null;
                FormFieldDataView fieldStatsWindow = null;

                // Flag colour index — FormGPS seeded btnFlag with the default (red) colour slot.
                int flagColorIndex = 0;

                // ----------------------------------------------------------------------------------------
                // [XPLAT] Dialog-navigation support (findings MVC-1/MVC-3/APP-3 and the Field/Guidance group
                // BNDY/BBT/BND/ABD/GRID/HA/HL/TL). The WinForms FormGPS opened every editor through the `mf`
                // back-reference; the Avalonia shell has none, so each migrated dialog is constructed here with
                // its collaborators injected and shown parented on the MainView window. The original open-site
                // gating (job-started / boundary / guidance-line) and the post-close PanelUpdateRightAndBottom +
                // SetZoom refresh are reproduced verbatim. mainView.RefreshAfterDialog() is the cross-platform
                // stand-in for that refresh sequence.
                // ----------------------------------------------------------------------------------------

                // Unit-conversion snapshots — were FormGPS GUI.Designer.cs L472-501, recomputed from the live
                // metric flag at open time (so a mid-session units change is honoured, exactly as the WinForms
                // PanelUpdateRightAndBottom did). Unit strings keep their original LEADING space.
                double M2FtOrM() => render.IsMetric ? 1.0 : glm.m2ft;
                double FtOrMtoM() => render.IsMetric ? 1.0 : glm.ft2m;
                string UnitsFtM() => render.IsMetric ? " m" : " ft";
                string UnitsInCm() => render.IsMetric ? " cm" : " in";
                double Cm2CmOrIn() => render.IsMetric ? 1.0 : 0.394;
                // m2InchOrCm tracks the live render value (metric 100 / imperial 39.3701); inchOrCm2m is its
                // reciprocal (metric 0.01 / imperial 0.0254) — the original always kept them paired this way.
                double M2InchOrCmVal() => render.M2InchOrCm;
                double InchOrCm2mVal() => render.M2InchOrCm != 0 ? 1.0 / render.M2InchOrCm : 0.01;

                // Shows a migrated editor parented on the shell, refreshing the shell faces/labels/viewport when
                // it closes (the WinForms open-sites called PanelUpdateRightAndBottom()/SetZoom() after close).
                void ShowEditorDialog(Window dlg)
                {
                    if (dlg == null) return;
                    dlg.Closed += (_, __) => mainView.RefreshAfterDialog();
                    dlg.ShowDialog(mainView);
                }

                // Shared dialog DI delegates (the same closures the WinForms shell passed as `mf.` callbacks).
                Func<vec3> getPivotAxlePos = () => render.PivotAxlePos;
                Func<double> getMaxFieldDistance = () => render.maxFieldDistance;
                Func<double> getFieldCenterX = () => render.fieldCenterX;
                Func<double> getFieldCenterY = () => render.fieldCenterY;
                Action calculateMinMax = () => render.CalculateMinMax();
                Func<bool> isKeyboardOn = () => Properties.Settings.Default.setDisplay_isKeyboardOn;
                Func<bool> isBtnAutoSteerOnFn = () => appModel.isBtnAutoSteerOn;
                Func<bool> isYouTurnBtnOnFn = () => yt.isYouTurnBtnOn;
                Action performAutoSteerClick = () => ToggleAutoSteerDomain();
                Action performAutoYouTurnClick = () => ToggleYouTurnDomain();
                Action<int> setTwoSecondCounter = _ => { };   // no per-frame two-second host in the migrated shell
                Action panelUpdateRightAndBottom = () => mainView.RefreshAfterDialog();
                Action saveTracks = () => fieldIo.FileSaveTracks();
                Action activateMainView = () => mainView.Activate();
                Action<int, string, string> timedMessageBox = (ms, title, msg) => ShowTimed(ms, title, msg);
                // The IBoundaryFieldData seam the boundary dialogs use (fd area recompute + render extents).
                IBoundaryFieldData boundaryFieldData = new BoundaryFieldDataAdapter(fd, render);

                ShellCommands shellCommands = new ShellCommands
                {
                    // --- Guidance / steering toggles (return resulting visual state) ---
                    ToggleAutoSteer = ToggleAutoSteerDomain,
                    ToggleYouTurn = ToggleYouTurnDomain,
                    ToggleContour = ToggleContourDomain,
                    ContourLock = () =>
                    {
                        if (ct.isContourBtnOn) ct.SetLockToLine();
                        return ct.isLocked;
                    },
                    ToggleAutoTrack = () =>
                    {
                        trk.isAutoTrack = !trk.isAutoTrack;
                        return trk.isAutoTrack;
                    },
                    ToggleAutoSnapToPivot = () =>
                    {
                        trk.isAutoSnapToPivot = !trk.isAutoSnapToPivot;
                        return trk.isAutoSnapToPivot;
                    },
                    CycleLines = CycleLinesDomain,
                    CycleLinesBack = CycleLinesBackDomain,

                    // --- Bottom panel ---
                    // btnYouSkipEnable_Click: cycle skip mode Normal -> Alternative -> IgnoreWorkedTracks -> Normal.
                    YouSkipEnable = () =>
                    {
                        yt.rowSkipsWidth = Properties.Settings.Default.set_youSkipWidth;
                        switch (yt.skipMode)
                        {
                            case SkipMode.Normal:
                                yt.skipMode = SkipMode.Alternative;
                                if (yt.rowSkipsWidth < 2) yt.rowSkipsWidth = 2;
                                yt.Set_Alternate_skips();
                                break;
                            case SkipMode.Alternative:
                                yt.skipMode = SkipMode.IgnoreWorkedTracks;
                                if (yt.rowSkipsWidth < 2) yt.rowSkipsWidth = 2;
                                break;
                            case SkipMode.IgnoreWorkedTracks:
                                yt.skipMode = SkipMode.Normal;
                                break;
                        }
                        yt.ResetCreatedYouTurn();
                        return (int)yt.skipMode;
                    },
                    // btnTramDisplayMode_Click: cycle tram display mode.
                    CycleTramDisplay = () =>
                    {
                        tram.isLeftManualOn = false;
                        tram.isRightManualOn = false;
                        if (tram.tramList.Count > 0 && tram.tramBndOuterArr.Count == 0)
                        {
                            tram.displayMode = tram.displayMode != 0 ? 0 : 2;
                        }
                        else
                        {
                            tram.displayMode++;
                            if (tram.displayMode > 3) tram.displayMode = 0;
                        }
                        return tram.displayMode;
                    },
                    // btnHeadlandOnOff_Click.
                    ToggleHeadland = () =>
                    {
                        bnd.isHeadlandOn = !bnd.isHeadlandOn;
                        if (vehicle.isHydLiftOn && !bnd.isHeadlandOn) vehicle.isHydLiftOn = false;
                        if (!bnd.isHeadlandOn) pgn.p_239.pgn[pgn.p_239.hydLift] = 0;
                        return bnd.isHeadlandOn;
                    },
                    // cboxIsSectionControlled_Click — persist headland section-control flag.
                    ToggleHeadlandSectionControl = on =>
                    {
                        bnd.isSectionControlledByHeadland = on;
                        Properties.ToolSettings.Default.setHeadland_isSectionControlled = on;
                        Properties.ToolSettings.Default.Save();
                    },
                    // btnHydLift_Click.
                    ToggleHydLift = () =>
                    {
                        if (bnd.isHeadlandOn)
                        {
                            vehicle.isHydLiftOn = !vehicle.isHydLiftOn;
                            if (!vehicle.isHydLiftOn) pgn.p_239.pgn[pgn.p_239.hydLift] = 0;
                        }
                        else
                        {
                            pgn.p_239.pgn[pgn.p_239.hydLift] = 0;
                            vehicle.isHydLiftOn = false;
                        }
                        return vehicle.isHydLiftOn;
                    },

                    // --- Track / nudge / flags ---
                    ResetToolHeading = () => position.ResetToolHeading(),
                    AddFlag = () =>
                    {
                        int nextFlag = flagPts.Count + 1;
                        CFlag flag = new CFlag(
                            appModel.CurrentLatLon.Latitude,
                            appModel.CurrentLatLon.Longitude,
                            pn.fix.easting,
                            pn.fix.northing,
                            appModel.FixHeading.AngleInRadians,
                            flagColorIndex,
                            nextFlag,
                            nextFlag.ToString());
                        flagPts.Add(flag);

                        // De-duplicate in place so the rooted list reference RenderCoordinator holds stays valid.
                        var deduped = AgOpenGPS.IO.FlagsFiles.DeduplicateFlags(flagPts);
                        flagPts.Clear();
                        flagPts.AddRange(deduped);

                        fieldIo.FileSaveFlags();
                    },
                    NudgeLeft = () => trk.NudgeTrack(-Properties.ToolSettings.Default.setAS_snapDistance * 0.01),
                    NudgeRight = () => trk.NudgeTrack(Properties.ToolSettings.Default.setAS_snapDistance * 0.01),
                    SnapToPivot = () => trk.SnapToPivot(),
                    // btnTrack_Click domain — turn contour off if on, then select a visible track if none active.
                    TrackButton = () =>
                    {
                        if (ct.isContourBtnOn) ToggleContourDomain();
                        if (trk.gArr.Count > 0 && trk.idx == -1)
                        {
                            trk.idx = trk.gArr.FindIndex(t => t.isVisible);
                            if (trk.idx == -1) trk.idx = 0;
                        }
                    },
                    TracksOff = () => trk.idx = -1,

                    // --- Two-program hub ---
                    StartAgIO = () => Program.StartAgIO(),

                    // --- Status windows (non-modal; second press closes) ---
                    ShowGpsData = () =>
                    {
                        if (gpsDataWindow != null)
                        {
                            try { gpsDataWindow.Close(); } catch { /* already closing */ }
                            gpsDataWindow = null;
                            return;
                        }
                        gpsDataWindow = new FormGPSDataView(pn, ahrs, position, pgn, appCore);
                        gpsDataWindow.Closed += (_, __) => gpsDataWindow = null;
                        gpsDataWindow.Show(mainView);
                    },
                    ShowFieldStats = () =>
                    {
                        if (!appModel.isJobStarted) return;
                        if (fieldStatsWindow != null)
                        {
                            try { fieldStatsWindow.Close(); } catch { /* already closing */ }
                            fieldStatsWindow = null;
                            return;
                        }
                        fieldStatsWindow = new FormFieldDataView(fd, bnd, appViewModel.IsMetric);
                        fieldStatsWindow.Closed += (_, __) => fieldStatsWindow = null;
                        fieldStatsWindow.Show(mainView);
                    },

                    // --- Display brightness (routed through IPlatformServices; -1 == not controllable). ---
                    BrightnessUp = () =>
                    {
                        int b = platform.GetBrightness();
                        if (b < 0) return null;
                        b = Math.Min(100, b + 10);
                        platform.SetBrightness(b);
                        Properties.Settings.Default.setDisplay_brightness = b;
                        Properties.Settings.Default.Save();
                        return b.ToString() + "%";
                    },
                    BrightnessDown = () =>
                    {
                        int b = platform.GetBrightness();
                        if (b < 0) return null;
                        b = Math.Max(10, b - 10);
                        platform.SetBrightness(b);
                        Properties.Settings.Default.setDisplay_brightness = b;
                        Properties.Settings.Default.Save();
                        return b.ToString() + "%";
                    },

                    // --- Simulator controls (verbatim from the WinForms btnSim* handlers). ---
                    SimSpeedUp = () =>
                    {
                        if (sim.stepDistance < 0) { sim.stepDistance = 0; return; }
                        if (sim.stepDistance < 0.2) sim.stepDistance += 0.02; else sim.stepDistance *= 1.15;
                        if (sim.stepDistance > 7.5) sim.stepDistance = 7.5;
                    },
                    SimSpeedDown = () =>
                    {
                        if (sim.stepDistance < 0.2 && sim.stepDistance > -0.51) sim.stepDistance -= 0.02; else sim.stepDistance *= 0.8;
                        if (sim.stepDistance < -0.5) sim.stepDistance = -0.5;
                    },
                    SimSetSpeedToZero = () => sim.stepDistance = 0,
                    SimReverseDirection = () =>
                    {
                        sim.headingTrue += Math.PI;
                        ABLine.isABValid = false;
                        curve.isCurveValid = false;
                        if (appModel.isBtnAutoSteerOn)
                        {
                            ToggleAutoSteerDomain();
                            ShowTimed(2000, gStr.gsGuidanceStopped, "Sim Reverse Touched");
                            Log.EventWriter("Steer Off, Sim Reverse Activated");
                        }
                    },
                    SimReset = () => sim.CurrentLatLon = new Wgs84(
                        Properties.Settings.Default.setGPS_SimLatitude,
                        Properties.Settings.Default.setGPS_SimLongitude),
                    SimResetSteerAngle = () => sim.steerAngleScrollBar = 0,
                    // [XPLAT] WinForms slider was 0..800 (centre 400) and mapped (val-400)*0.1 -> +-40 deg. The
                    // Avalonia slider is -100..100 (centre 0); *0.4 preserves the identical +-40 deg authority.
                    SimSteerAngleScroll = value => sim.steerAngleScrollBar = value * 0.4,

                    // --- Recorded path ---
                    PathGoStop = PathGoStopDomain,
                    // btnPathRecordStop_Click: stop+save (default name) or start a fresh recording. The WinForms
                    // FormRecordName save-as prompt is wired in the dialog-navigation phase; here the path is
                    // persisted to the default RecPath.Txt so record/stop is functional end-to-end.
                    PathRecordStop = () =>
                    {
                        if (recPath.isRecordOn)
                        {
                            recPath.isRecordOn = false;
                            fieldIo.FileSaveRecPath();
                        }
                        else if (appModel.isJobStarted)
                        {
                            recPath.recList.Clear();
                            recPath.isRecordOn = true;
                        }
                        return recPath.isRecordOn;
                    },
                    // btnResumePath_Click: cycle resume style 0 -> 1 -> 2 -> 0.
                    ResumePath = () =>
                    {
                        if (recPath.resumeState == 0) { recPath.resumeState++; ShowTimed(1500, "Resume Style", "Last Stopped Position"); }
                        else if (recPath.resumeState == 1) { recPath.resumeState++; ShowTimed(1500, "Resume Style", "Closest Point"); }
                        else { recPath.resumeState = 0; ShowTimed(1500, "Resume Style", "Start At Beginning"); }
                        return recPath.resumeState;
                    },
                    // btnSwapABRecordedPath_Click: reverse the path, rotating each point's heading by PI.
                    SwapABRecordedPath = () =>
                    {
                        int cnt = recPath.recList.Count;
                        var reversed = new List<CRecPathPt>();
                        for (int i = cnt - 1; i > -1; i--)
                        {
                            recPath.recList[i].heading += glm.PIBy2 + glm.PIBy2;
                            if (recPath.recList[i].heading < -glm.twoPI) recPath.recList[i].heading += glm.twoPI;
                            reversed.Add(recPath.recList[i]);
                        }
                        recPath.recList.Clear();
                        for (int i = 0; i < cnt; i++) recPath.recList.Add(reversed[i]);
                    },

                    // cboxpRowWidth_SelectedIndexChanged — apply a new skip width.
                    SetRowSkipWidth = value =>
                    {
                        yt.rowSkipsWidth = value;
                        yt.Set_Alternate_skips();
                        if (!yt.isYouTurnTriggered) yt.ResetCreatedYouTurn();
                        Properties.Settings.Default.set_youSkipWidth = yt.rowSkipsWidth;
                        Properties.Settings.Default.Save();
                    },

                    // =====================================================================================
                    // [XPLAT] Dialog navigation (finding MVC-1/MVC-2/MVC-3, APP-3, BND-1/BNDY-1/BBT-1, ABD-1,
                    // GRID-1/HA-1/HL-1/TL-1). Each closure reconstructs a former FormGPS open-site: it applies
                    // the SAME enable/guard gating the WinForms button had, constructs the migrated editor with
                    // the shared DI delegates, and shows it modally via ShowEditorDialog (which repaints the
                    // shell on close — the parity stand-in for PanelUpdateRightAndBottom()/SetZoom()).
                    // =====================================================================================

                    // btnAutoSteerConfig -> FormSteer. The adapters bind the dialog's ISteerSettings* seams onto
                    // the live domain objects (CVehicle / PgnDispatcher / CModuleComm / CSmartWAS).
                    OpenSteerConfig = () => ShowEditorDialog(new FormSteerView(
                        new SteerSettingsVehicleAdapter(vehicle),
                        new SteerSettingsConfigServiceAdapter(pgn, ABLine, vehicle),
                        new SteerSettingsTelemetryAdapter(mc, appModel, render, position),
                        new SteerSettingsSmartWASAdapter(smartWAS),
                        mainView)),

                    // Steer/WAS calibration wizard (finding MVC-1/MVC-2, F-020). Fully migrated; the ISteerWiz*
                    // adapters bind the wizard to the live PGN/vehicle/AHRS state. NOTE: sideHillCompFactor is
                    // backed by the persisted setting because no live CGuidance peer is wired (latent gap is
                    // documented in PARITY_REPORT.md), so calibration round-trips through settings, not a NRE.
                    OpenSteerWizard = () => ShowEditorDialog(new FormSteerWizView(
                        new SteerWizVehicleAdapter(vehicle, tram),
                        new SteerWizConfigServiceAdapter(pgn),
                        new SteerWizTelemetryAdapter(mc, appModel, render, pgn, ahrs),
                        new SteerWizAhrsAdapter(ahrs),
                        new SteerWizGuidanceAdapter(),
                        mainView)),

                    // btnABLine "+" / quick AB add (FormQuickAB). isEasyDriveMode is false in the migrated shell.
                    OpenQuickAB = () => ShowEditorDialog(new FormQuickABView(
                        curve, ABLine, trk, tool, getPivotAxlePos, isKeyboardOn, () => false,
                        isBtnAutoSteerOnFn, isYouTurnBtnOnFn, performAutoSteerClick, performAutoYouTurnClick,
                        setTwoSecondCounter, panelUpdateRightAndBottom, saveTracks, activateMainView)),

                    // btnBuildTracks -> FormBuildTracks. resetYouTurn -> yt.ResetYouTurn(); disableYouTurnButtons
                    // is reproduced by RefreshAfterDialog (re-evaluates the YouTurn button face from live state).
                    OpenBuildTracks = () => ShowEditorDialog(new FormBuildTracksView(
                        trk, curve, ABLine, tool, appModel, getPivotAxlePos, () => fieldIo.currentFieldDirectory,
                        isKeyboardOn, isBtnAutoSteerOnFn, isYouTurnBtnOnFn, performAutoSteerClick,
                        performAutoYouTurnClick, () => yt.ResetYouTurn(), () => mainView.RefreshAfterDialog(),
                        setTwoSecondCounter, panelUpdateRightAndBottom, saveTracks, activateMainView,
                        timedMessageBox, RegistrySettings.fieldsDirectory)),

                    // btnABDraw -> FormABDraw (finding ABD-1). turnAutoSteerOff/turnYouTurnOff toggle OFF only
                    // when currently on (performAuto*Click is a toggle, so it is gated to an off-action here).
                    OpenABDraw = () => ShowEditorDialog(new FormABDrawView(
                        trk, curve, ABLine, bnd, render.FieldBoundingBox, triStrip, getPivotAxlePos,
                        getMaxFieldDistance, getFieldCenterX, getFieldCenterY, calculateMinMax, saveTracks,
                        isKeyboardOn, isBtnAutoSteerOnFn, () => { if (appModel.isBtnAutoSteerOn) performAutoSteerClick(); },
                        isYouTurnBtnOnFn, () => { if (yt.isYouTurnBtnOn) performAutoYouTurnClick(); },
                        setTwoSecondCounter, timedMessageBox)),

                    // Grid editor (finding GRID-1) -> FormGrid.
                    OpenGrid = () => ShowEditorDialog(new FormGridView(
                        render.FieldBoundingBox, calculateMinMax, triStrip, bnd, getPivotAxlePos, worldGrid,
                        curve, ABLine, trk, setTwoSecondCounter)),

                    // btnNudge -> FormNudge (track nudge). Unit conversions resolve metric/imperial at open time.
                    OpenNudge = () => ShowEditorDialog(new FormNudgeView(
                        trk, tool, render.IsMetric, M2InchOrCmVal(), InchOrCm2mVal(), Cm2CmOrIn(), UnitsInCm(),
                        saveTracks, activateMainView)),

                    // btnRefNudge -> FormRefNudge (reference-line nudge); onClosed repaints the shell.
                    OpenRefNudge = () => ShowEditorDialog(new FormRefNudgeView(
                        trk, tool, ABLine, curve, render.IsMetric, M2InchOrCmVal(), InchOrCm2mVal(), Cm2CmOrIn(),
                        UnitsInCm(), saveTracks, activateMainView, () => mainView.RefreshAfterDialog())),

                    // Field-tools menu: Boundaries (finding BNDY-1). WinForms gated on a started job; FormBoundary
                    // can in turn open FormBuildBoundaryFromTracks (finding BBT-1) — reachable once this is.
                    OpenBoundary = () =>
                    {
                        if (!appModel.isJobStarted) { ShowTimed(2000, gStr.gsFieldNotOpen, gStr.gsStartNewField); return; }
                        ShowEditorDialog(new FormBoundaryView(
                            bnd, boundaryFieldData, appModel, () => fieldIo.FileSaveBoundary(),
                            w => fieldIo.FileMakeKMLFromCurrentPosition(w), trk, () => fieldIo.FileLoadTracks(),
                            tool.width, render.IsMetric, fieldIo.currentFieldDirectory,
                            () => { }, () => mainView.RefreshAfterDialog(), mainView));
                    },

                    // Field-tools menu: Headland editor (finding HL-1) -> FormHeadLine. Requires a boundary first.
                    OpenHeadland = () =>
                    {
                        if (bnd.bndList.Count == 0) { ShowTimed(2000, gStr.gsNoBoundary, gStr.gsCreateABoundaryFirst); return; }
                        ShowEditorDialog(new FormHeadLineView(
                            hdl, bnd, curve, tool, mainView.Viewport, getPivotAxlePos, getMaxFieldDistance,
                            getFieldCenterX, getFieldCenterY, M2FtOrM, FtOrMtoM, UnitsFtM, calculateMinMax,
                            () => fieldIo.FileSaveHeadland(), on => { vehicle.isHydLiftOn = on; }));
                    },

                    // Field-tools menu: Headland-build (finding HA-1) -> FormHeadAche. Requires a boundary first.
                    OpenHeadlandBuild = () =>
                    {
                        if (bnd.bndList.Count == 0) { ShowTimed(2000, gStr.gsNoBoundary, gStr.gsCreateABoundaryFirst); return; }
                        ShowEditorDialog(new FormHeadAcheView(
                            hdl, bnd, curve, tool, mainView.Viewport, getMaxFieldDistance, getFieldCenterX,
                            getFieldCenterY, UnitsFtM, M2FtOrM, FtOrMtoM, calculateMinMax,
                            () => fieldIo.FileLoadHeadLines(), () => fieldIo.FileSaveHeadLines(),
                            () => fieldIo.FileSaveHeadland(), on => { vehicle.isHydLiftOn = on; }));
                    },

                    // Field-tools menu: TramLines (finding TL-1) -> FormTramLine. WinForms required an active AB
                    // track and turned contour off first; showYesMessage is the advisory (was mf.YesMessageBox).
                    OpenTramLines = () =>
                    {
                        if (trk.idx == -1) { ShowTimed(2000, gStr.gsNoABLineActive, gStr.gsPleaseEnterABLine); return; }
                        if (ct.isContourBtnOn) ct.StopContourLine();
                        ShowEditorDialog(new FormTramLineView(
                            tram, trk, tool, bnd, ABLine, vehicle, mainView.Viewport, () => render.FieldBoundingBox,
                            getMaxFieldDistance, M2FtOrM, UnitsFtM, calculateMinMax, () => fieldIo.FileSaveTram(),
                            panelUpdateRightAndBottom, () => mainView.RefreshAfterDialog(),
                            msg => { ShowTimed(3000, gStr.gsTramLines, msg); return Task.CompletedTask; }));
                    },

                    // Field-tools menu: Boundary-tool (finding BND-1) -> FormBndTool. WinForms opened it only with
                    // a started job; the dialog builds its own viewport from the field bounding box.
                    OpenBoundaryTool = () =>
                    {
                        if (!appModel.isJobStarted) { ShowTimed(2000, gStr.gsFieldNotOpen, gStr.gsStartNewField); return; }
                        ShowEditorDialog(new FormBndToolView(
                            bnd, render.FieldBoundingBox, triStrip, boundaryFieldData, () => fieldIo.FileSaveBoundary(),
                            () => fieldIo.FileSaveHeadland(), msg => ShowTimed(2500, string.Empty, msg)));
                    },

                    // btnChangeMappingColor -> FormColorPicker. Seed with the persisted day section colour
                    // (System.Drawing.Color), persist + repaint on OK. The picker returns through ShowDialog<Color?>,
                    // so the show is an async-void inner function (fire-and-forget is correct for a void command).
                    OpenMappingColor = () =>
                    {
                        System.Drawing.Color cur = Properties.Settings.Default.setDisplay_colorSectionsDay;
                        var picker = new FormColorPickerView(
                            Avalonia.Media.Color.FromRgb(cur.R, cur.G, cur.B), Array.Empty<int>());
                        async void ShowPicker()
                        {
                            Avalonia.Media.Color? chosen = await picker.ShowDialog<Avalonia.Media.Color?>(mainView);
                            if (chosen.HasValue)
                            {
                                Properties.Settings.Default.setDisplay_colorSectionsDay =
                                    System.Drawing.Color.FromArgb(chosen.Value.R, chosen.Value.G, chosen.Value.B);
                                Properties.Settings.Default.Save();
                                mainView.RefreshAfterDialog();
                            }
                        }
                        ShowPicker();
                    },

                    // btnPickRecordedPath -> FormRecordPicker. hidePanelDrag has no migrated panel (no-op);
                    // saveRecPath persists the recorded path.
                    OpenPickPath = () => ShowEditorDialog(new FormRecordPickerView(
                        fieldIo.currentFieldDirectory, recPath, () => { }, () => fieldIo.FileSaveRecPath())),

                    // AgShare API settings -> FormAgShareSettings (AgShare disabled by default; this only edits keys).
                    OpenAgShareApi = () => ShowEditorDialog(new FormAgShareSettingsView(agShareClient)),

                    // btnSimulatorOnOff: cannot toggle the simulator while a field job is open. Persists the
                    // setting and reflects it on the shell's simulator panel.
                    ToggleSimulator = () =>
                    {
                        if (appModel.isJobStarted) { ShowTimed(2000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst); return; }
                        bool on = !Properties.Settings.Default.setMenu_isSimulatorOn;
                        Properties.Settings.Default.setMenu_isSimulatorOn = on;
                        Properties.Settings.Default.Save();
                        mainView.IsSimulatorActive = on;
                    },

                    // Enter simulator coordinates -> FormSimCoords (uses the live NMEA/job/sim state + error presenter).
                    EnterSimCoords = () => ShowEditorDialog(new FormSimCoordsView(
                        pn, appModel.isJobStarted, mainView.IsSimulatorActive, errorPresenter)),

                    // Language selection. No migrated language dialog exists (creating one is out of AAP scope —
                    // new architecture). The delegate stays non-null (fail-fast contract) and faithfully informs
                    // the operator that the UI language is set in configuration and applies after a restart.
                    OpenLanguage = () => ShowTimed(4000, gStr.gsLanguage,
                        System.Globalization.CultureInfo.CurrentUICulture.NativeName + "\r\n\r\n" +
                        gStr.gsProgramWillExitPleaseRestart),

                    // resetAllToolStripMenuItem: blocked while a job is open; otherwise confirm, reset the settings
                    // store and close so the reset takes effect on the next launch (WinForms parity: Reset()+Close()).
                    ResetAll = () =>
                    {
                        if (appModel.isJobStarted) { ShowTimed(2000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst); return; }
                        if (FormDialogView.ShowQuestionBlocking(gStr.gsResetAll, gStr.gsReallyResetEverything,
                            DialogSeverity.Warning, mainView))
                        {
                            RegistrySettings.Reset();
                            mainView.Close();
                        }
                    },

                    // AOG updater menu: blocked while a job is open; otherwise launch the sibling updater process
                    // (OS-specific name, located beside the GPS executable). Gracefully degrades when not published.
                    CheckForUpdates = () =>
                    {
                        if (appModel.isJobStarted) { ShowTimed(3000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst); return; }
                        string updaterName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                            System.Runtime.InteropServices.OSPlatform.Windows) ? "AgOpenGPS.Updater.exe" : "AgOpenGPS.Updater";
                        string updaterPath = Path.Combine(AppContext.BaseDirectory, updaterName);
                        if (File.Exists(updaterPath))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = updaterPath,
                                    WorkingDirectory = AppContext.BaseDirectory,
                                    UseShellExecute = false,
                                });
                            }
                            catch (Exception ex)
                            {
                                Log.EventWriter("[XPLAT] CheckForUpdates: failed to launch updater: " + ex.Message);
                                ShowTimed(3000, "AgOpenGPS Updater", "Could not start the updater.");
                            }
                        }
                        else
                        {
                            ShowTimed(3000, "AgOpenGPS Updater", "Updater is not available on this platform.");
                        }
                    },

                    // Help -> FormHelp (non-modal, parented on the shell; no post-close shell repaint needed).
                    ShowHelp = () => new FormHelpView().Show(mainView),

                    // --- Status readers (the view paints labels / picks button faces from these). ---
                    TrackCountText = () => trk.idx > -1 ? $"{trk.idx + 1}/{trk.gArr.Count}" : string.Empty,
                    FlagCountText = () => flagPts.Count.ToString(),
                    IsHydLiftOn = () => vehicle.isHydLiftOn,
                    TrackVisibleCount = () => trk.gArr.Count(t => t.isVisible),
                    HasBoundary = () => bnd.bndList.Count > 0,
                    IsContourOn = () => ct.isContourBtnOn,
                    IsContourLocked = () => ct.isLocked,
                    IsAutoTrackOn = () => trk.isAutoTrack,
                    IsAutoSteerOn = () => appModel.isBtnAutoSteerOn,
                    IsYouTurnOn = () => yt.isYouTurnBtnOn,
                    HasActiveTrack = () => trk.idx > -1,
                    IsAutoSnapToPivotOn = () => trk.isAutoSnapToPivot,
                    IsHeadlandOn = () => bnd.isHeadlandOn,
                    IsDrivingRecordedPath = () => recPath.isDrivingRecordedPath,
                    IsRecordingPath = () => recPath.isRecordOn,
                    YouSkipMode = () => (int)yt.skipMode,
                    TramDisplayMode = () => tram.displayMode,
                    ResumeState = () => recPath.resumeState,
                };

                mainView.Commands = shellCommands;

                // [XPLAT] Coverage gate (findings MV-1, MVC-3, APP-3). After the dialog-navigation phase EVERY
                // operator action — immediate commands, status readers, AND every dialog-navigation delegate —
                // must be populated, so that every migrated dialog is reachable from the shell. A non-empty
                // result now signals a real wiring regression (not expected deferral) and is logged as an error.
                IReadOnlyList<string> unwiredCommands = shellCommands.GetUnpopulatedCommands();
                if (unwiredCommands.Count > 0)
                {
                    Log.EventWriter($"[XPLAT] ERROR: ShellCommands wiring incomplete — {unwiredCommands.Count}/{ShellCommands.CommandCount} unpopulated: {string.Join(", ", unwiredCommands)}");
                }
                else
                {
                    Log.EventWriter($"[XPLAT] ShellCommands fully wired: {ShellCommands.CommandCount}/{ShellCommands.CommandCount} operator actions populated.");
                }

                // ----------------------------------------------------------------------------------------
                // 13. Start the loopback UDP server (frozen transport: receive on 127.0.0.1:15555, peer
                //     127.255.255.255:17777, <=70 ms throttle). FormGPS started this at load; nothing else does.
                // ----------------------------------------------------------------------------------------
                pgn.StartLoopbackServer();

                // ----------------------------------------------------------------------------------------
                // 13b. Two-program model — auto-start the AgIO hub (AAP R4 / F-003). FormGPS started AgIO in its
                //      Load handler, gated on a vehicle profile being selected AND setDisplay_isAutoStartAgIO.
                //      That exact gate is preserved here; the launch primitive itself (process probe + locate beside
                //      GPS + Process.Start) lives in Program.StartAgIO so the manual "Start AgIO" button reuses it.
                //      Done after StartLoopbackServer so GPS's 127.0.0.1:15555 receiver is already listening when
                //      AgIO comes up and begins forwarding PGN traffic over the loopback fabric.
                // ----------------------------------------------------------------------------------------
                if (!string.IsNullOrEmpty(RegistrySettings.vehicleProfileName) &&
                    Properties.Settings.Default.setDisplay_isAutoStartAgIO)
                {
                    Program.StartAgIO();
                }

                // ----------------------------------------------------------------------------------------
                // 14. Shutdown teardown. The single-instance lock is owned and released exactly once by
                //     Program.Main's finally block (preserving AgIO Restart() semantics), so App must not touch
                //     it. PgnDispatcher exposes no Dispose/Stop — its loopback socket is reclaimed by the OS at
                //     process exit — so the only action is to drop the rooted reference. MainView.OnViewClosing
                //     already persists camera pitch/zoom and saves Settings.
                //     [XPLAT] Two-program auto-off (AAP R4 / F-003): when setDisplay_isAutoOffAgIO is enabled,
                //     stop the AgIO hub GPS started, mirroring FormGPS's FormClosing CloseMainWindow() call
                //     (Program.StopAgIO adds the cross-platform bounded-wait + Kill fallback). Gated exactly as the
                //     WinForms build, and run before dropping the dispatcher reference.
                // ----------------------------------------------------------------------------------------
                desktop.ShutdownRequested += (_, __) =>
                {
                    if (Properties.Settings.Default.setDisplay_isAutoOffAgIO)
                    {
                        Program.StopAgIO();
                    }
                    _pgnDispatcher = null;
                };

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
