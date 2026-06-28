// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] MIGRATION SUMMARY (this file is the code-behind for the kiosk main shell):
//   • WinForms FormGPS window  ->  Avalonia MainView (Avalonia.Controls.Window, kiosk: Maximized + SystemDecorations=None).
//   • FormGPS god-object / `mf` back-reference  ->  the extracted Services/ classes (PositionService, PgnDispatcher,
//     SectionService, FieldIoService, RenderCoordinator) are CONSTRUCTOR-INJECTED and the ApplicationViewModel arrives
//     via DataContext (set by App.axaml.cs, the composition root). This file owns ONLY view/UI concerns: hosting the
//     OpenGL viewport, panel-drag behaviour, window hotkeys, status-readout updates and forwarding user actions.
//   • OpenTK.GLControl (oglMain/oglZoom/oglBack)  ->  AgOpenGPS.Controls.AvaloniaGeoViewport over OpenGlControlBase.
//   • WinForms KeyPreview + ProcessCmdKey hotkeys  ->  a window-level tunnelling KeyDown handler.
//   • DraggableControlExtension (DELETED)  ->  pointer-event drag implemented directly here (PointerPressed/Moved/Released).
//   • Win32 P/Invoke (SetForegroundWindow/ShowWindow)  ->  REMOVED; cross-platform Window.Activate() is used instead.
//   • Registry/%AppData% settings  ->  unchanged XML schema reached through Properties.Settings.Default (de-Windowsed elsewhere).
//
// Real-time-loop discipline (AAP §0.7.1): this view NEVER throttles or blocks the receive->fuse->steer->section path.
// The async UDP receive feeds PositionService independently; the view only *reacts* (request a viewport redraw, refresh
// labels). All view callbacks the services invoke are forwarded to thread-safe viewport requests or marshalled onto the
// UI thread, so no per-fix latency is ever added and the <=70 ms udpWatchLimit throttle stays entirely in the services.
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;   // RangeBase (steer-angle slider), ToggleButton
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;          // Bitmap — runtime button-face swaps (parity with WinForms btn.Image = ...)
using Avalonia.Platform;               // AssetLoader — loads avares:// btnImages
using Avalonia.Threading;
using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core;                 // Camera, btnStates
using AgOpenGPS.Core.Models;          // GeoBoundingBox
using AgOpenGPS.Core.ViewModels;      // ApplicationViewModel
using AgOpenGPS.Services;             // the five behaviour-frozen extracted services
// [XPLAT] Settings are reached as `Properties.Settings.Default` (the unchanged, schema-frozen XML store),
// matching the established sibling-view convention; an alias is avoided because the `AgOpenGPS.Views.Settings`
// dialog namespace would shadow it.

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] The kiosk-style main shell — a 1:1 behavioural-parity reimplementation of the WinForms
/// <c>FormGPS</c> window on Avalonia (AAP §0.3.3). It hosts the central OpenGL field viewport, the side/bottom
/// command panels, the section/zone buttons, the hamburger menu and the live status readouts declared in the
/// partner <c>MainView.axaml</c>.
/// </summary>
/// <remarks>
/// The former <c>FormGPS</c> partial-class logic (the scan loop, UDP/PGN transport, section control, field I/O
/// and the OpenGL draw routines) now lives in the sibling <see cref="AgOpenGPS.Services"/> classes that are
/// injected through the DI constructor. This code-behind therefore contains no domain mathematics and no
/// god-object back-reference; it wires the services' UI-facing callbacks to Avalonia controls, dispatches
/// keyboard hotkeys, drives the draggable panel and keeps the status strip current.
/// </remarks>
public partial class MainView : Window
{
    // ============================================================================================
    //  Injected, behaviour-frozen services (the dismantled FormGPS god-object). Stored read-only;
    //  the composition root (App.axaml.cs) owns their construction and their domain-side wiring.
    // ============================================================================================
    private readonly PositionService _position;
    private readonly PgnDispatcher _pgn;
    private readonly SectionService _sections;
    private readonly FieldIoService _fieldIo;
    private readonly RenderCoordinator _render;

    // ============================================================================================
    //  View-owned state (render/UI state stays in the view per AAP §0.6.1, never in the services).
    // ============================================================================================

    /// <summary>The Avalonia OpenGL host adapter created for the main field viewport.</summary>
    private AvaloniaGeoViewport _viewport;

    /// <summary>Cross-platform timed-message presenter (FormTimedMessage parity) owned by this window.</summary>
    private AvaloniaErrorPresenter _errorPresenter;

    /// <summary>UI-thread timer that refreshes the status strip. Off the real-time hot path entirely.</summary>
    private DispatcherTimer _statusTimer;

    /// <summary>
    /// Live hotkey table (19 chars). Seeded from <c>setKey_hotkeys</c> and replaced live by
    /// <see cref="SetHotkeys(char[])"/> when the operator edits them in <see cref="FormKeysView"/>.
    /// </summary>
    private char[] _hotkeys = Properties.Settings.Default.setKey_hotkeys.ToCharArray();

    /// <summary>Pointer-drag state for <c>panelDrag</c> (replaces the deleted DraggableControlExtension).</summary>
    private bool _isPanelDragging;

    /// <summary>Pointer offset, in <c>rootGrid</c> coordinates, between the press point and the panel's top-left.</summary>
    private Point _panelDragOffset;

    /// <summary>Backing value for <see cref="PositionService.getIsSimActive"/>; toggled by the sim menu wiring.</summary>
    private bool _isSimulatorActive;

    /// <summary>Guards <c>Opened</c> so the one-time startup gate (terms + logging) runs exactly once.</summary>
    private bool _openedHandled;

    /// <summary>Guards <c>Closing</c> so settings are persisted exactly once.</summary>
    private bool _closeHandled;

    /// <summary>
    /// [XPLAT] Optional render camera, supplied by the composition root that owns the <see cref="Camera"/>
    /// instance shared with <see cref="RenderCoordinator"/>. When set, the view persists its pitch/zoom to
    /// <c>Settings</c> on close exactly as the WinForms <c>FormGPS</c> did (the camera is no longer owned by
    /// the view after the god-object split, so it is provided rather than constructed here).
    /// </summary>
    public Camera Camera { get; set; }

    /// <summary>
    /// Gets or sets whether the built-in simulator is active. Backs the <see cref="PositionService.getIsSimActive"/>
    /// callback (was <c>timerSim.Enabled</c>); set by the simulator menu wiring of the shell.
    /// </summary>
    public bool IsSimulatorActive
    {
        get => _isSimulatorActive;
        set
        {
            _isSimulatorActive = value;
            // [XPLAT] The simulator control bar (panelSim) is shown only while the simulator is active,
            // mirroring the WinForms shell where the sim slider bar was hidden in live-GPS mode. Null-guarded
            // because the property is assigned through an object initializer (App.axaml.cs) — InitializeComponent
            // has already created the named control by then, but the guard keeps the setter safe in all orders.
            if (panelSim != null) panelSim.IsVisible = value;
        }
    }

    /// <summary>
    /// [XPLAT] The operator-action command map (see <see cref="ShellCommands"/>). The composition root
    /// (App.axaml.cs) builds this bundle with closures over the domain objects that used to be reached through
    /// the WinForms <c>FormGPS</c> god-object, then assigns it here. <see cref="WireButtonHandlers"/> attaches
    /// each shell button's <c>Click</c> to the matching delegate, reproducing the original <c>btnXxx_Click</c>
    /// behaviour without giving the view any domain dependency. Every invocation is null-guarded, so a button
    /// whose command is not yet populated is simply inert rather than faulting.
    /// </summary>
    public ShellCommands Commands { get; set; }

    /// <summary>
    /// [XPLAT] The live OpenGL viewport adapter (replacing the WinForms <c>oglMain</c>/<c>oglZoom</c>/<c>oglBack</c>
    /// controls). Several migrated Field/Guidance dialogs (head-land/head-line/tram editors) take the active
    /// viewport so they can draw their working overlays into the same GL surface the operator sees. The dialog-open
    /// closures in App.axaml.cs capture this getter lazily (<c>() =&gt; mainView.Viewport</c>) so it resolves at
    /// button-click time — after <see cref="WireUpView"/> has constructed <see cref="_viewport"/>. Returns
    /// <see langword="null"/> before the GL host is built and after teardown, which the consumers null-guard.
    /// </summary>
    public AvaloniaGeoViewport Viewport => _viewport;

    /// <summary>
    /// Guards the steer-angle slider handler so the programmatic re-centre performed by
    /// <c>btnResetSteerAngle</c> (and other code-driven slider writes) does not re-enter the simulator domain.
    /// </summary>
    private bool _suppressSteerScroll;

    // ============================================================================================
    //  Constructors
    // ============================================================================================

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML loader / designer previewer. It only loads the
    /// compiled XAML; the runtime wiring is performed by the dependency-injection constructor used by the
    /// composition root, so the previewer renders the shell chrome without needing the live services.
    /// </summary>
    public MainView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Dependency-injection constructor used by <c>App.axaml.cs</c>. Receives the behaviour-frozen
    /// services extracted from the former <c>FormGPS</c> partials and wires their UI-facing callbacks to this
    /// window. The <see cref="ApplicationViewModel"/> is supplied separately as the <c>DataContext</c> by the
    /// composition root (which also sets the presenters), so it is read here through <see cref="Window.DataContext"/>
    /// rather than injected — and <see cref="Core.ApplicationCore"/> is never constructed here.
    /// </summary>
    /// <param name="position">The real-time GPS-fix / scan-loop service (was <c>Position.designer.cs</c>).</param>
    /// <param name="pgn">The UDP/PGN encode-decode dispatcher (was <c>UDPComm.Designer.cs</c> + <c>PGN.Designer.cs</c>).</param>
    /// <param name="sections">The section/zone control service (was <c>Sections.Designer.cs</c>).</param>
    /// <param name="fieldIo">The field load/save/export service (was <c>SaveOpen.Designer.cs</c>).</param>
    /// <param name="render">The OpenGL render-orchestration service (was <c>OpenGL.Designer.cs</c>).</param>
    public MainView(
        PositionService position,
        PgnDispatcher pgn,
        SectionService sections,
        FieldIoService fieldIo,
        RenderCoordinator render)
    {
        _position = position ?? throw new ArgumentNullException(nameof(position));
        _pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
        _fieldIo = fieldIo ?? throw new ArgumentNullException(nameof(fieldIo));
        _render = render ?? throw new ArgumentNullException(nameof(render));

        InitializeComponent();
        WireUpView();
    }

    // ============================================================================================
    //  One-time wiring (DI path only). Ordered so every collaborator a callback captures exists first.
    // ============================================================================================

    /// <summary>
    /// Performs the runtime wiring once the XAML is loaded and the services are available: hosts the OpenGL
    /// viewport, connects the service callbacks, enables panel dragging, registers the window-level hotkey
    /// handler, subscribes the lifecycle events and prepares the status-strip refresh timer.
    /// </summary>
    private void WireUpView()
    {
        // Timed-message popups are owned by this window (FormTimedMessage parity); created first because the
        // service callbacks below capture it.
        _errorPresenter = new AvaloniaErrorPresenter { Owner = this };

        HostViewport();
        WireServiceCallbacks();
        WireButtonHandlers();
        EnablePanelDrag();

        // [XPLAT] KeyPreview parity: handle KeyDown at the window before children (Tunnel), then also on the
        // bubble pass so a hotkey still fires when a non-text child consumed nothing. The handler is registered
        // with handledEventsToo:false so it cooperates with normal focus navigation.
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        // Status-strip refresh — purely reactive UI work off the real-time path (AAP §0.7.1).
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += OnStatusTimerTick;

        Opened += OnViewOpened;
        Closing += OnViewClosing;
    }

    // ============================================================================================
    //  OpenGL viewport hosting
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Builds the <see cref="AvaloniaGeoViewport"/> over <c>OpenGlControlBase</c> (replacing the three
    /// WinForms <c>OpenTK.GLControl</c> surfaces oglMain/oglZoom/oglBack) and drops its companion control into
    /// the named host declared in <c>MainView.axaml</c>. The viewport is constructed with an explicitly empty
    /// bounding box — the "no field loaded yet" startup state, mirroring how the WinForms world/camera extents
    /// began before a field was opened; <see cref="RenderCoordinator.CalculateMinMax"/> recomputes the real
    /// field bounds once a field loads. The injected <see cref="RenderCoordinator"/> is handed to the viewport
    /// so its frozen draw/scan routines run on the Avalonia GL surface.
    /// </summary>
    private void HostViewport()
    {
        _viewport = new AvaloniaGeoViewport(GeoBoundingBox.CreateEmpty(), _render);

        // The single GL host renders the main field; the offscreen section/look-ahead scan (oglBack) and the
        // worked-area overlap estimate (oglZoom) are driven on the same surface through the viewport's
        // RequestBackBufferScan()/RequestZoomOverlapCalc() requests, so the legacy zoom/back hosts collapse onto
        // one control. The dedicated host containers from the XAML are left empty (kept for layout parity).
        if (glHostContainer != null)
        {
            glHostContainer.Content = _viewport.View;
        }
    }

    // ============================================================================================
    //  Service callback wiring. The services raise these to ask the view to do UI work; the view never
    //  reaches back into the services on the hot path. Every UI touch is either a thread-safe viewport
    //  request (self-marshalling) or marshalled onto the UI thread, so no per-fix latency is introduced.
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Connects the UI-facing callbacks of every injected service to this window. Mutating-delegate
    /// hooks (<c>Action</c>/<c>Func</c> fields) are assigned; multicast <c>event</c> hooks are subscribed.
    /// </summary>
    private void WireServiceCallbacks()
    {
        // ---- PositionService: scan-loop view callbacks (was the FormGPS partial member sites) ----
        // Render/scan requests are forwarded straight to the viewport, which is documented safe to call from
        // any thread (it raises a volatile flag and asks Avalonia for a prompt frame), so timing is preserved.
        _position.RequestMainRender = () => _viewport?.RequestRender();
        _position.RequestBackBufferScan = () => _viewport?.RequestBackBufferScan();
        _position.OnRequestSetZoom = () => _viewport?.RequestRender();           // re-applies saved camera zoom on the next frame
        _position.OnTimedMessage = ShowTimedMessage;                            // AvaloniaErrorPresenter self-marshals
        _position.OnSpeedColorState = SetSpeedValidColor;                       // touches a control -> marshalled
        _position.OnRequestAutoSteerToggle = RequestAutoSteerToggle;           // raises a button -> marshalled
        _position.getIsSimActive = () => _isSimulatorActive;

        // ---- PgnDispatcher: transport-driven view callbacks ----
        _pgn.OnGpsFixReady = () => _viewport?.RequestRender();
        _pgn.OnRemoteSwitchChanged = () => _viewport?.RequestRender();
        _pgn.OnHardwareMessage += OnHardwareMessage;                            // -> lblHardwareMessage
        _pgn.OnError += OnTransportError;                                       // -> timed message
        _pgn.OnCycleLines += () => UiPost(() => InvokeButton(btnCycleLines));   // remote "cycle lines" reflects the button
        _pgn.OnCycleLinesBk += () => UiPost(() => InvokeButton(btnCycleLinesBk));

        // ---- SectionService: section/zone/master state changes recolour the buttons AND redraw the field ----
        // The section/zone buttons carry the tri-state colour (the WinForms SetColors equivalent): the service
        // raises these events with the changed index + new state, and the view paints the matching button and
        // requests a field redraw (the authoritative coverage visual). Marshalled because they touch controls.
        _sections.OnSectionStateChanged += (sectionIndexZeroBased, state) =>
            UiPost(() =>
            {
                SetSectionZoneButtonColor(ManualSectionButton(sectionIndexZeroBased + 1), state);
                _viewport?.RequestRender();
            });
        _sections.OnZoneStateChanged += (zoneIndexOneBased, state) =>
            UiPost(() =>
            {
                SetSectionZoneButtonColor(ZoneButton(zoneIndexOneBased), state);
                _viewport?.RequestRender();
            });
        _sections.OnMasterStateChanged += state =>
            UiPost(() =>
            {
                // Section-master AUTO button face: any non-Off state shows the engaged icon (parity with the
                // WinForms btnSectionMasterAuto image swap).
                SetImage(imgSectionMasterAuto, state != btnStates.Off ? "SectionMasterOn.png" : "SectionMasterOff.png");
                _viewport?.RequestRender();
            });
        _sections.OnManualStateChanged += state =>
            UiPost(() =>
            {
                // Section-master MANUAL button face.
                SetImage(imgSectionMasterManual, state != btnStates.Off ? "ManualOn.png" : "ManualOff.png");
                _viewport?.RequestRender();
            });

        // ---- FieldIoService: field lifecycle reflects in the status strip and the field redraw ----
        _fieldIo.OnFieldOpened += OnFieldOpened;
        _fieldIo.OnTramModeChanged += () => _viewport?.RequestRender();
        _fieldIo.OnRecPathLoaded += _ => _viewport?.RequestRender();

        // ---- RenderCoordinator: display-expiry + flag-pick callbacks drive the status labels / redraw ----
        _render.OnGuidanceLineExpired += OnGuidanceLineExpired;                 // -> clear lblGuidanceLine
        _render.OnHardwareMessageExpired += OnHardwareMessageExpired;           // -> clear lblHardwareMessage
        _render.OnFlagPicked += () => _viewport?.RequestRender();
    }

    // ============================================================================================
    //  panelDrag drag-to-move (reimplements the DELETED DraggableControlExtension with pointer events).
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Enables drag-to-move on <c>panelDrag</c> using Avalonia pointer events, replacing the deleted
    /// WinForms <c>DraggableControlExtension</c> (<c>panelDrag.Draggable(true)</c>). The panel keeps its
    /// WinForms feel: it is repositioned within <c>rootGrid</c> and clamped so it can never be dragged off
    /// screen.
    /// </summary>
    private void EnablePanelDrag()
    {
        if (panelDrag == null)
        {
            return;
        }

        panelDrag.PointerPressed += OnPanelDragPointerPressed;
        panelDrag.PointerMoved += OnPanelDragPointerMoved;
        panelDrag.PointerReleased += OnPanelDragPointerReleased;
    }

    /// <summary>Begins a drag: captures the pointer and records the grab offset within <c>rootGrid</c>.</summary>
    private void OnPanelDragPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // Only the primary (left) button starts a move, matching the WinForms drag.
        if (!e.GetCurrentPoint(panelDrag).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point pointerInGrid = e.GetPosition(rootGrid);
        _panelDragOffset = new Point(
            pointerInGrid.X - panelDrag.Margin.Left,
            pointerInGrid.Y - panelDrag.Margin.Top);

        _isPanelDragging = true;
        e.Pointer.Capture(panelDrag);
        e.Handled = true;
    }

    /// <summary>Repositions the panel under the pointer, clamped to the visible <c>rootGrid</c> bounds.</summary>
    private void OnPanelDragPointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isPanelDragging)
        {
            return;
        }

        Point pointerInGrid = e.GetPosition(rootGrid);
        double left = pointerInGrid.X - _panelDragOffset.X;
        double top = pointerInGrid.Y - _panelDragOffset.Y;

        // Clamp so the whole panel stays on screen (parity with the original draggable limits).
        double maxLeft = Math.Max(0.0, rootGrid.Bounds.Width - panelDrag.Bounds.Width);
        double maxTop = Math.Max(0.0, rootGrid.Bounds.Height - panelDrag.Bounds.Height);
        left = Math.Clamp(left, 0.0, maxLeft);
        top = Math.Clamp(top, 0.0, maxTop);

        panelDrag.Margin = new Thickness(left, top, 0, 0);
        e.Handled = true;
    }

    /// <summary>Ends a drag and releases the pointer capture.</summary>
    private void OnPanelDragPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!_isPanelDragging)
        {
            return;
        }

        _isPanelDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ============================================================================================
    //  Hotkeys (KeyPreview parity). The WinForms FormGPS handled ProcessCmdKey against a 19-char hotkey
    //  table (default "ACFGMNPTYVW12345678"); this reproduces that mapping at the window level.
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Live-replaces the hotkey table. This is the <c>Action&lt;char[]&gt;</c> target handed to
    /// <see cref="FormKeysView"/> so edits the operator makes in the hotkey editor take effect immediately
    /// (parity with the WinForms <c>Form_Keys</c> pushing a fresh <c>char[]</c> back into <c>FormGPS.hotkeys</c>).
    /// A defensive copy is stored, and a too-short array is ignored so the dispatcher never indexes out of range.
    /// </summary>
    /// <param name="keys">The new 19-entry hotkey table.</param>
    public void SetHotkeys(char[] keys)
    {
        if (keys == null || keys.Length < 19)
        {
            return;
        }

        _hotkeys = (char[])keys.Clone();
    }

    /// <summary>
    /// [XPLAT] Window-level key handler (replaces WinForms <c>KeyPreview</c> + <c>ProcessCmdKey</c>). Maps the
    /// pressed key to the configured hotkey table and invokes the matching action by calling the injected
    /// services / view-model commands / named buttons. Keystrokes are ignored while a text field is focused so
    /// typing into dialogs is never hijacked.
    /// </summary>
    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || _hotkeys == null || _hotkeys.Length < 19)
        {
            return;
        }

        // Do not steal keystrokes from text entry (a sensible cross-platform refinement over the WinForms
        // global ProcessCmdKey, which predated the Avalonia dialog text fields).
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        char pressed = KeyToHotkeyChar(e.Key);
        if (pressed == '\0')
        {
            return;
        }

        // First matching index wins, exactly mirroring the WinForms if/return chain order.
        for (int i = 0; i < _hotkeys.Length; i++)
        {
            if (char.ToUpperInvariant(_hotkeys[i]) == pressed)
            {
                if (DispatchHotkey(i))
                {
                    e.Handled = true;
                }
                return;
            }
        }
    }

    /// <summary>
    /// [XPLAT] Executes the hotkey action for table index <paramref name="index"/>, preserving the exact
    /// FormGPS <c>ProcessCmdKey</c> semantics but routed through injected services / VM commands / named
    /// buttons instead of the god-object.
    /// </summary>
    /// <param name="index">Zero-based hotkey index (0..18).</param>
    /// <returns><see langword="true"/> when the key was consumed.</returns>
    private bool DispatchHotkey(int index)
    {
        switch (index)
        {
            case 0: InvokeButton(btnAutoSteer); return true;            // autosteer on/off
            case 1: InvokeButton(btnCycleLines); return true;           // cycle guidance lines
            case 2: _ = SaveAndCloseFieldAsync(); return true;          // save & close field
            case 3: InvokeButton(btnFlag); return true;                 // new flag
            case 4: _sections.PerformSectionMasterManual(); return true; // section master MANUAL
            case 5: _sections.PerformSectionMasterAuto(); return true;   // section master AUTO
            case 6: InvokeButton(btnSnapToPivot); return true;          // snap track to pivot
            case 7: InvokeButton(btnAdjLeft); return true;              // nudge track left
            case 8: InvokeButton(btnAdjRight); return true;             // nudge track right
            case 9: ShowVehicleConfig(); return true;                   // vehicle settings / config menu
            case 10: OpenSteerWizard(); return true;                    // steer wizard
            default:                                                    // section/zone 1..8 (indices 11..18)
                ToggleSectionOrZone(index - 10);
                return true;
        }
    }

    /// <summary>
    /// [XPLAT] hotkey[9]: opens the configuration / vehicle-settings menu through the bound view-model command
    /// (was <c>toolStripConfig.PerformClick()</c>). Falls back to no-op if the DataContext is not yet the
    /// <see cref="ApplicationViewModel"/>.
    /// </summary>
    private void ShowVehicleConfig()
    {
        if (DataContext is ApplicationViewModel vm && vm.ShowConfigMenuCommand.CanExecute(null))
        {
            vm.ShowConfigMenuCommand.Execute(null);
        }
    }

    /// <summary>
    /// [XPLAT] hotkey[10]: opens the migrated Avalonia steer-wizard (<c>FormSteerWizView</c>, was WinForms
    /// <c>FormSteerWiz</c>). The composition root wires <see cref="ShellCommands.OpenSteerWizard"/> to construct
    /// the view with its real collaborator adapters; the inline fallback is a defensive guard that only fires if
    /// the command was never wired, so the hotkey is never silently dropped.
    /// </summary>
    private void OpenSteerWizard()
    {
        // [XPLAT] Route to the real migrated steer-calibration workflow (wired by App composition root).
        if (Commands?.OpenSteerWizard != null)
        {
            Commands.OpenSteerWizard();
            return;
        }

        // Defensive fallback only — OpenSteerWizard is populated in production composition.
        Log.EventWriter("Steer wizard hotkey pressed but OpenSteerWizard command was not wired");
        ShowTimedMessage(2000, "Steer Wizard", "Not available yet");
    }

    /// <summary>
    /// [XPLAT] hotkeys[11..18]: toggles section or zone <paramref name="oneBased"/> (1..8). The WinForms code
    /// chose between the section and zone button using <c>tool.isSectionsNotZones</c>; that flag is private to
    /// the section domain now, so the view decides from which control set is actually shown — only the active
    /// mode's buttons are visible — which yields the same routing without reaching into the service internals.
    /// </summary>
    /// <param name="oneBased">The 1..8 section/zone number.</param>
    private void ToggleSectionOrZone(int oneBased)
    {
        Button section = SectionButton(oneBased);
        Button zone = ZoneButton(oneBased);

        if (section != null && section.IsVisible)
        {
            InvokeButton(section);
        }
        else if (zone != null && zone.IsVisible)
        {
            InvokeButton(zone);
        }
    }

    /// <summary>Resolves the manual section button (btnSection1Man..btnSection8Man) for <paramref name="n"/> (1..8).</summary>
    private Button SectionButton(int n) => n switch
    {
        1 => btnSection1Man,
        2 => btnSection2Man,
        3 => btnSection3Man,
        4 => btnSection4Man,
        5 => btnSection5Man,
        6 => btnSection6Man,
        7 => btnSection7Man,
        8 => btnSection8Man,
        _ => null,
    };

    /// <summary>Resolves the zone button (btnZone1..btnZone8) for <paramref name="n"/> (1..8).</summary>
    private Button ZoneButton(int n) => n switch
    {
        1 => btnZone1,
        2 => btnZone2,
        3 => btnZone3,
        4 => btnZone4,
        5 => btnZone5,
        6 => btnZone6,
        7 => btnZone7,
        8 => btnZone8,
        _ => null,
    };

    /// <summary>
    /// [XPLAT] Programmatic button activation — the cross-platform stand-in for WinForms <c>Button.PerformClick()</c>.
    /// Executes a bound command (so XAML-bound buttons such as the menu-flow buttons fire) and raises the
    /// <see cref="Button.ClickEvent"/> (so imperatively wired Click handlers fire). Disabled buttons are skipped,
    /// matching <c>PerformClick</c>, which is a no-op on a disabled control.
    /// </summary>
    /// <param name="button">The button to activate; ignored when <see langword="null"/> or disabled.</param>
    private static void InvokeButton(Button button)
    {
        if (button == null || !button.IsEffectivelyEnabled)
        {
            return;
        }

        if (button.Command is ICommand command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>
    /// [XPLAT] Maps an Avalonia <see cref="Key"/> to the uppercase hotkey character the table stores, covering
    /// letters and both top-row and numeric-keypad digits. Returns <c>'\0'</c> for keys with no hotkey character.
    /// </summary>
    private static char KeyToHotkeyChar(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return (char)('A' + (key - Key.A));
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return (char)('0' + (key - Key.D0));
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (char)('0' + (key - Key.NumPad0));
        }

        return '\0';
    }

    /// <summary>
    /// [XPLAT] hotkey[2] / file-menu "save &amp; close field": fire-and-forget the field save orchestrated by
    /// <see cref="FieldIoService"/>. Wrapped so an I/O fault is logged and never surfaces as an unobserved task
    /// exception.
    /// </summary>
    private async Task SaveAndCloseFieldAsync()
    {
        try
        {
            await _fieldIo.FileSaveEverythingBeforeClosingField();
        }
        catch (Exception ex)
        {
            Log.EventWriter("Save & close field (hotkey) failed: " + ex.Message);
        }
    }

    /// <summary>Posts <paramref name="action"/> onto the Avalonia UI thread, or runs it inline when already there.</summary>
    private static void UiPost(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    // ============================================================================================
    //  Lifecycle (FormGPS_Load / FormGPS_FormClosing parity).
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Window <c>Opened</c> handler — reproduces <c>FormGPS_Load</c>: it logs the program start with an
    /// <see cref="CultureInfo.InvariantCulture"/> timestamp, runs the terms-and-conditions gate
    /// (<see cref="FormTermsAndConditionsView"/> shown modally; the app exits when the operator disagrees),
    /// seeds the current-field readout, brings the window forward and starts the reactive status timer. Guarded
    /// so the one-time gate runs exactly once.
    /// </summary>
    /// <remarks>
    /// Parity note: the WinForms baseline never writes <c>setDisplay_isTermsAccepted</c> back to <see langword="true"/>
    /// (it defaults <see langword="false"/> and is only ever read), so this handler deliberately does NOT persist
    /// the flag — the acceptance setting is managed exactly as in the original product, neither extended nor reduced.
    /// </remarks>
    private async void OnViewOpened(object sender, EventArgs e)
    {
        if (_openedHandled)
        {
            return;
        }

        _openedHandled = true;

        // FormGPS_Load parity: program-start banner + version, both InvariantCulture (§0.6.5).
        Log.EventWriter("Program Started: " + DateTime.Now.ToString("f", CultureInfo.InvariantCulture));
        Log.EventWriter("AOG Version: " + (typeof(MainView).Assembly.GetName().Version?.ToString() ?? "unknown"));

        // Terms-and-conditions gate (FormGPS_Load parity). FormTermsAndConditionsView returns true on agree
        // (Close(true)) and false on disagree (Close(false)).
        if (!Properties.Settings.Default.setDisplay_isTermsAccepted)
        {
            bool agreed = await new FormTermsAndConditionsView().ShowDialog<bool>(this);
            if (!agreed)
            {
                Log.EventWriter("Terms Not Accepted");
                Log.FileSaveSystemEvents();
                Environment.Exit(0);
                return;
            }

            Log.EventWriter("Terms Accepted");
        }
        else
        {
            Log.EventWriter("Terms Already Accepted");
        }

        // Seed the current-field readout from the persisted directory (FormGPS_Load: currentFieldDirectory = setF_CurrentDir).
        string fieldDir = Properties.Settings.Default.setF_CurrentDir;
        if (!string.IsNullOrEmpty(fieldDir))
        {
            lblCurrentField.Text = fieldDir;
        }

        // [XPLAT] cross-platform window activation replaces the removed Win32 SetForegroundWindow/ShowWindow.
        Activate();

        // Lay out / colour the section & zone buttons for the current tool + job state (WinForms
        // LineUpIndividualSectionBtns / LineUpAllZoneButtons parity), and seed the status labels.
        RefreshSectionZoneButtons();
        RefreshShellLabels();

        // Seed every guidance / bottom-row / recorded-path button face from the initial domain state so the
        // shell opens with faces matching the restored settings (parity with the WinForms designer initial images).
        RefreshGuidanceFaces();
        RefreshBottomFaces();
        RefreshPathFaces();

        // Begin the purely reactive status-strip refresh (off the real-time receive->fuse->steer->section path).
        _statusTimer?.Start();
    }

    /// <summary>
    /// [XPLAT] Window <c>Closing</c> handler — reproduces the <c>FormGPS</c> shutdown: it stops the status timer,
    /// persists the camera pitch/zoom to <c>Settings</c> (when the composition root supplied the shared
    /// <see cref="Camera"/>) and calls <c>Save()</c> exactly as <c>GUI.Designer.cs</c> did, then detaches the GL
    /// host so Avalonia releases the OpenGL context deterministically. Guarded so persistence happens once.
    /// </summary>
    private void OnViewClosing(object sender, WindowClosingEventArgs e)
    {
        if (_closeHandled)
        {
            return;
        }

        _closeHandled = true;

        // Stop and detach the reactive UI timer.
        if (_statusTimer != null)
        {
            _statusTimer.Stop();
            _statusTimer.Tick -= OnStatusTimerTick;
        }

        // Persist camera pitch/zoom exactly as the WinForms shutdown did (GUI.Designer.cs set setDisplay_camPitch /
        // setDisplay_camZoom from the camera, then Settings.Default.Save()). The camera is no longer owned by the
        // view after the god-object split, so it is persisted only when the composition root provided it.
        if (Camera != null)
        {
            Properties.Settings.Default.setDisplay_camPitch = Camera.PitchInDegrees;
            Properties.Settings.Default.setDisplay_camZoom = Camera.ZoomValue;
        }

        Properties.Settings.Default.Save();

        // Detach the GL control from the visual tree so Avalonia's OnOpenGlDeinit runs and the OpenGL resources
        // are released deterministically (GeoViewportBase/AvaloniaGeoViewport expose no Dispose of their own).
        if (glHostContainer != null)
        {
            glHostContainer.Content = null;
        }

        _viewport = null;
    }

    // ============================================================================================
    //  Status readouts (driven from data the services expose). All file/protocol-adjacent numerics use
    //  InvariantCulture (§0.6.5). The DispatcherTimer.Tick fires on the UI thread, so no marshalling is needed.
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Refreshes the status strip from <see cref="PositionService"/> (mirrors the WinForms
    /// <c>lblHz</c>/<c>lblFix</c> updates in <c>GUI.Designer.cs</c>). Purely reactive — it reads already-computed
    /// values and never participates in the real-time fix path (AAP §0.7.1).
    /// </summary>
    private void OnStatusTimerTick(object sender, EventArgs e)
    {
        if (_position.isGPSPositionInitialized)
        {
            // lblHz parity: "<gpsHz> ~ <frameTime>" (GUI.Designer.cs).
            lblHz.Text = _position.gpsHz.ToString("N1", CultureInfo.InvariantCulture)
                + " ~ " + _position.frameTime.ToString("N1", CultureInfo.InvariantCulture);

            // lblFix parity: a fix-timing readout from the exposed last-fix time slice, flagged when RTK alarms.
            string fix = "Age: " + _position.timeSliceOfLastFix.ToString("N1", CultureInfo.InvariantCulture);
            if (_position.isRTK_AlarmOn)
            {
                fix += "  RTK!";
            }

            lblFix.Text = fix;
        }
        else
        {
            // "Not Connected" parity (OpenGL.Designer.cs set the disconnected placeholders).
            lblHz.Text = "Not Connected";
            lblFix.Text = "---";
        }
    }

    // ============================================================================================
    //  Service-callback handlers (the services ask the view to do UI work; never the reverse on the hot path).
    // ============================================================================================

    /// <summary>
    /// [XPLAT] <see cref="PositionService.OnTimedMessage"/> target — shows a self-dismissing message through the
    /// owned <see cref="AvaloniaErrorPresenter"/> (FormTimedMessage parity). The presenter self-marshals onto the
    /// UI thread, so no explicit dispatch is required here.
    /// </summary>
    /// <param name="milliseconds">How long the message stays visible.</param>
    /// <param name="title">The message title.</param>
    /// <param name="message">The message body.</param>
    private void ShowTimedMessage(int milliseconds, string title, string message)
    {
        _errorPresenter?.PresentTimedMessage(
            TimeSpan.FromMilliseconds(milliseconds),
            title ?? string.Empty,
            message ?? string.Empty);
    }

    /// <summary>
    /// [XPLAT] <see cref="PositionService.OnSpeedColorState"/> target — colours <c>lblSpeed</c> green when the
    /// speed reading is valid and red otherwise (parity with the WinForms <c>lblSpeed.ForeColor</c> toggling).
    /// </summary>
    /// <param name="valid"><see langword="true"/> for a valid speed (green); otherwise red.</param>
    private void SetSpeedValidColor(bool valid)
    {
        UiPost(() => lblSpeed.Foreground = valid ? Brushes.Green : Brushes.Red);
    }

    /// <summary>
    /// [XPLAT] <see cref="PositionService.OnRequestAutoSteerToggle"/> target — toggles autosteer by activating the
    /// <c>btnAutoSteer</c> button (parity with the WinForms code calling <c>btnAutoSteer.PerformClick()</c>).
    /// Marshalled because it raises a control event.
    /// </summary>
    private void RequestAutoSteerToggle()
    {
        UiPost(() => InvokeButton(btnAutoSteer));
    }

    /// <summary>
    /// [XPLAT] <see cref="PgnDispatcher.OnHardwareMessage"/> target — surfaces a hardware/diagnostic string in
    /// <c>lblHardwareMessage</c> (parity with the WinForms UDP handler). A null payload renders as empty text,
    /// never the literal "null" (UI8).
    /// </summary>
    /// <param name="message">The hardware message text.</param>
    private void OnHardwareMessage(string message)
    {
        UiPost(() =>
        {
            lblHardwareMessage.Text = message ?? string.Empty;
            lblHardwareMessage.IsVisible = !string.IsNullOrEmpty(message);
        });
    }

    /// <summary>
    /// [XPLAT] <see cref="PgnDispatcher.OnError"/> target — reports a transport fault as a brief timed message so
    /// the operator sees the problem without the program stalling on the real-time path.
    /// </summary>
    /// <param name="message">The transport error text.</param>
    private void OnTransportError(string message)
    {
        ShowTimedMessage(2000, "Comm Error", message ?? string.Empty);
    }

    /// <summary>
    /// [XPLAT] <see cref="FieldIoService.OnFieldOpened"/> target — refreshes the current-field readout from the
    /// service's <c>currentFieldDirectory</c> and requests a field redraw.
    /// </summary>
    private void OnFieldOpened()
    {
        UiPost(() =>
        {
            lblCurrentField.Text = _fieldIo.currentFieldDirectory ?? string.Empty;
            // A job is now started — the section/zone buttons become operable for the configured tool, and the
            // guidance/bottom/path faces re-seed from the freshly-loaded field state.
            RefreshSectionZoneButtons();
            RefreshGuidanceFaces();
            RefreshBottomFaces();
            RefreshPathFaces();
            RefreshShellLabels();
        });
        _viewport?.RequestRender();
    }

    /// <summary>
    /// [XPLAT] View-side counterpart of <see cref="OnFieldOpened"/>, invoked by the composition root's
    /// post-save field-close hook (App.axaml.cs <c>AfterCloseField</c>). It performs the WinForms
    /// <c>JobClose()</c> view resets that are genuinely view concerns: clearing the current-field readout
    /// (parity with <c>lblCurrentField</c> being emptied) and requesting a redraw so the now-empty field
    /// surface repaints. The window title is already the static "AgOpenGPS" (it is never set to a per-field
    /// caption in the migrated shell), and the section/master button visuals are already driven by the
    /// <see cref="SectionService"/> events — so no further view mutation is required here. Always marshals to
    /// the UI thread via <see cref="UiPost"/> because the hook may run on a background continuation thread.
    /// </summary>
    public void OnFieldClosedResetUi()
    {
        UiPost(() =>
        {
            lblCurrentField.Text = string.Empty;
            // The job is closed — hide the section/zone buttons again (parity with the WinForms job-close
            // which made the section/zone controls invisible until the next field opens) and re-seed the
            // guidance/bottom/path faces from the reset domain state.
            RefreshSectionZoneButtons();
            RefreshGuidanceFaces();
            RefreshBottomFaces();
            RefreshPathFaces();
            RefreshShellLabels();
        });
        _viewport?.RequestRender();
    }

    /// <summary>
    /// [XPLAT] <see cref="RenderCoordinator.OnGuidanceLineExpired"/> target — clears and hides the guidance-line
    /// readout when its display timeout elapses (parity with <c>OpenGL.Designer.cs</c> hiding <c>lblGuidanceLine</c>).
    /// </summary>
    private void OnGuidanceLineExpired()
    {
        UiPost(() =>
        {
            lblGuidanceLine.Text = string.Empty;
            lblGuidanceLine.IsVisible = false;
        });
    }

    /// <summary>
    /// [XPLAT] <see cref="RenderCoordinator.OnHardwareMessageExpired"/> target — clears and hides the hardware
    /// message readout when its display timeout elapses (parity with <c>OpenGL.Designer.cs</c>).
    /// </summary>
    private void OnHardwareMessageExpired()
    {
        UiPost(() =>
        {
            lblHardwareMessage.Text = string.Empty;
            lblHardwareMessage.IsVisible = false;
        });
    }

    // ============================================================================================
    //  [XPLAT] Operator-button wiring (final-checkpoint finding MV-1).
    //  Attaches a Click handler to every shell button/toggle/menu item declared in MainView.axaml,
    //  reproducing the WinForms FormGPS `btnXxx_Click` behaviour. Pure view actions (camera tilt, window
    //  min/max/close, panel toggles, day/night) are performed inline because the view owns those concerns;
    //  every domain action is delegated to the ShellCommands map populated by the composition root, so this
    //  file keeps no domain dependency. Toggle commands return their resulting state and the view swaps the
    //  button face exactly as WinForms set `btn.Image`. Status indicators that had no WinForms click handler
    //  (btnChargeStatus, btnIsobusSectionControl) are intentionally left unwired.
    // ============================================================================================

    /// <summary>
    /// Wires the <c>Click</c> (and equivalent) events of every shell control to its action. Called once from
    /// <see cref="WireUpView"/> after the services are connected. Every domain invocation is null-guarded so a
    /// not-yet-populated command is inert rather than throwing.
    /// </summary>
    private void WireButtonHandlers()
    {
        // ---- panelLeft ----
        // btnNavigationSettings keeps its XAML ShowConfigMenuCommand binding (config flow) and additionally
        // toggles the navigation overlay here so the camera/brightness controls remain reachable (the WinForms
        // button toggled panelNavigation).
        btnNavigationSettings.Click += (_, __) => { if (panelNavigation != null) panelNavigation.IsVisible = !panelNavigation.IsVisible; };
        btnAutoSteerConfig.Click += (_, __) => Commands?.OpenSteerConfig?.Invoke();
        btnStartAgIO.Click += (_, __) => Commands?.StartAgIO?.Invoke();

        // ---- panelRight (guidance/steering toggles) ----
        // Each command performs the FULL domain mutation of its WinForms btnXxx_Click — including cross-effects
        // such as enabling contour turning autosteer off, or cycling tracks turning autosteer off. The view then
        // repaints EVERY guidance face from the state readers (RefreshGuidanceFaces), so sibling buttons stay in
        // sync exactly as the WinForms handlers re-imaged them via PerformClick. The autosteer toggle is the single
        // funnel for both user clicks and domain-requested toggles (PositionService.OnRequestAutoSteerToggle ->
        // RequestAutoSteerToggle -> InvokeButton(btnAutoSteer)), so the full safe-speed/guidance guard lives in the
        // ToggleAutoSteer closure.
        btnContourLock.Click += (_, __) => { Commands?.ContourLock?.Invoke(); RefreshGuidanceFaces(); };
        btnContour.Click += (_, __) => { Commands?.ToggleContour?.Invoke(); RefreshGuidanceFaces(); };
        btnCycleLines.Click += (_, __) => { FlashGuidanceLine(Commands?.CycleLines?.Invoke()); RefreshGuidanceFaces(); RefreshShellLabels(); };
        btnCycleLinesBk.Click += (_, __) => { FlashGuidanceLine(Commands?.CycleLinesBack?.Invoke()); RefreshGuidanceFaces(); RefreshShellLabels(); };
        btnAutoTrack.Click += (_, __) => { Commands?.ToggleAutoTrack?.Invoke(); RefreshGuidanceFaces(); };
        btnSectionMasterManual.Click += (_, __) => _sections.PerformSectionMasterManual();   // face swapped by OnManualStateChanged
        btnSectionMasterAuto.Click += (_, __) => _sections.PerformSectionMasterAuto();        // face swapped by OnMasterStateChanged
        btnAutoYouTurn.Click += (_, __) => { Commands?.ToggleYouTurn?.Invoke(); RefreshGuidanceFaces(); };
        btnAutoSteer.Click += (_, __) => { Commands?.ToggleAutoSteer?.Invoke(); RefreshGuidanceFaces(); };

        // ---- Manual section buttons (1..16) and zone buttons (1..8) — route to the SectionService (MV-3).
        // Colour/visibility are refreshed reactively by the section/zone state-changed events and the
        // lifecycle RefreshSectionZoneButtons() calls, exactly as the WinForms LineUp* methods coloured them.
        for (int i = 1; i <= 16; i++)
        {
            Button b = ManualSectionButton(i);
            if (b == null) continue;
            int idx = i - 1;
            b.Click += (_, __) => _sections.PerformSectionClick(idx);
        }
        for (int i = 1; i <= 8; i++)
        {
            Button z = ZoneButton(i);
            if (z == null) continue;
            int idx = i - 1;
            z.Click += (_, __) => _sections.PerformZoneClick(idx);
        }

        // ---- panelBottom ----
        cboxpRowWidth.SelectionChanged += (_, __) => Commands?.SetRowSkipWidth?.Invoke(cboxpRowWidth.SelectedIndex + 1);
        btnYouSkipEnable.Click += (_, __) => { Commands?.YouSkipEnable?.Invoke(); RefreshBottomFaces(); };
        btnChangeMappingColor.Click += (_, __) => Commands?.OpenMappingColor?.Invoke();
        btnResetToolHeading.Click += (_, __) => Commands?.ResetToolHeading?.Invoke();
        btnTramDisplayMode.Click += (_, __) => { Commands?.CycleTramDisplay?.Invoke(); RefreshBottomFaces(); };
        btnHydLift.Click += (_, __) => { Commands?.ToggleHydLift?.Invoke(); RefreshBottomFaces(); };
        cboxIsSectionControlled.Click += (_, __) =>
        {
            bool on = cboxIsSectionControlled.IsChecked == true;
            Commands?.ToggleHeadlandSectionControl?.Invoke(on);
            SetImage(imgHeadlandSection, on ? "HeadlandSectionOn.png" : "HeadlandSectionOff.png");
        };
        // btnHeadlandOnOff toggles headland AND (when turning off) forces the hydraulic lift off in the domain;
        // RefreshBottomFaces repaints both imgHeadland and imgHydLift from the resulting state.
        btnHeadlandOnOff.Click += (_, __) => { Commands?.ToggleHeadland?.Invoke(); RefreshBottomFaces(); };
        btnFlag.Click += (_, __) => { Commands?.AddFlag?.Invoke(); RefreshShellLabels(); };
        btnAdjLeft.Click += (_, __) => Commands?.NudgeLeft?.Invoke();
        btnAdjRight.Click += (_, __) => Commands?.NudgeRight?.Invoke();
        btnSnapToPivot.Click += (_, __) => Commands?.SnapToPivot?.Invoke();
        btnTrack.Click += (_, __) =>
        {
            Commands?.TrackButton?.Invoke();
            flp1.IsVisible = !flp1.IsVisible;            // the track flyout is a view concern
            if (flp1.IsVisible) RefreshTrackFlyout();
        };

        // ---- flp1 track flyout (dialog entries are populated in the dialog-navigation phase) ----
        btnRefNudge.Click += (_, __) => Commands?.OpenRefNudge?.Invoke();
        cboxAutoSnapToPivot.Click += (_, __) => Commands?.ToggleAutoSnapToPivot?.Invoke();
        btnTracksOff.Click += (_, __) => { Commands?.TracksOff?.Invoke(); flp1.IsVisible = false; };
        btnBuildTracks.Click += (_, __) => Commands?.OpenBuildTracks?.Invoke();
        btnPlusAB.Click += (_, __) => Commands?.OpenQuickAB?.Invoke();
        btnABDraw.Click += (_, __) => Commands?.OpenABDraw?.Invoke();
        btnNudge.Click += (_, __) => Commands?.OpenNudge?.Invoke();

        // ---- panelControlBox (top-right) ----
        btnFieldStats.Click += (_, __) => Commands?.ShowFieldStats?.Invoke();
        btnGPSData.Click += (_, __) => Commands?.ShowGpsData?.Invoke();              // GPSD-1 / MV-2: GPS data reachable
        btnMinimizeMainForm.Click += (_, __) => WindowState = WindowState.Minimized;
        btnMaximizeMainForm.Click += (_, __) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnShutdown.Click += (_, __) => Close();
        // btnChargeStatus: WinForms status indicator with no click handler — intentionally not wired.

        // ---- panelSim ----
        btnResetSim.Click += (_, __) => Commands?.SimReset?.Invoke();
        btnResetSteerAngle.Click += (_, __) =>
        {
            Commands?.SimResetSteerAngle?.Invoke();
            _suppressSteerScroll = true;
            hsbarSteerAngle.Value = 0;                  // re-centre the slider without re-entering the domain
            _suppressSteerScroll = false;
        };
        btnSpeedDn.Click += (_, __) => Commands?.SimSpeedDown?.Invoke();
        btnSimSetSpeedToZero.Click += (_, __) => Commands?.SimSetSpeedToZero?.Invoke();
        btnSimSpeedUp.Click += (_, __) => Commands?.SimSpeedUp?.Invoke();
        btnSimReverseDirection.Click += (_, __) => Commands?.SimReverseDirection?.Invoke();
        hsbarSteerAngle.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_suppressSteerScroll)
            {
                Commands?.SimSteerAngleScroll?.Invoke(hsbarSteerAngle.Value);
            }
        };

        // ---- panelNavigation (camera tilt/2D/3D/N2D verbatim from FormGPS; grid + brightness + day/night) ----
        btnTiltUp.Click += (_, __) =>
        {
            if (Camera == null) return;
            Camera.PitchInDegrees -= ((Camera.PitchInDegrees * 0.012) - 1);
            if (Camera.PitchInDegrees > -58) Camera.PitchInDegrees = 0;
            _viewport?.RequestRender();
        };
        btnTiltDn.Click += (_, __) =>
        {
            if (Camera == null) return;
            if (Camera.PitchInDegrees > -59) Camera.PitchInDegrees = -60;
            Camera.PitchInDegrees += ((Camera.PitchInDegrees * 0.012) - 1);
            if (Camera.PitchInDegrees < -70) Camera.PitchInDegrees = -70;
            _viewport?.RequestRender();
        };
        btn2D.Click += (_, __) => { if (Camera == null) return; Camera.FollowDirectionHint = true; Camera.PitchInDegrees = 0; _viewport?.RequestRender(); };
        btn3D.Click += (_, __) => { if (Camera == null) return; Camera.FollowDirectionHint = true; Camera.PitchInDegrees = -65; _viewport?.RequestRender(); };
        btnN2D.Click += (_, __) => { if (Camera == null) return; Camera.FollowDirectionHint = false; Camera.PitchInDegrees = 0; _viewport?.RequestRender(); };
        btnGrid.Click += (_, __) => Commands?.OpenGrid?.Invoke();
        btnDayNightMode.Click += (_, __) => ToggleDayNight();
        btnBrightnessUp.Click += (_, __) => { if (Commands?.BrightnessUp == null) return; lblBrightness.Text = Commands.BrightnessUp() ?? lblBrightness.Text; };
        btnBrightnessDn.Click += (_, __) => { if (Commands?.BrightnessDown == null) return; lblBrightness.Text = Commands.BrightnessDown() ?? lblBrightness.Text; };

        // ---- panelDrag (recorded path) ----
        // btnPathGoStop first turns contour/youturn/autosteer off (domain prologue) then toggles path driving;
        // refresh both the path faces and the guidance faces so the disengaged guidance buttons update too.
        btnPathGoStop.Click += (_, __) => { Commands?.PathGoStop?.Invoke(); RefreshPathFaces(); RefreshGuidanceFaces(); };
        btnResumePath.Click += (_, __) => { Commands?.ResumePath?.Invoke(); RefreshPathFaces(); };
        btnPathRecordStop.Click += (_, __) => { Commands?.PathRecordStop?.Invoke(); RefreshPathFaces(); };
        btnPickPath.Click += (_, __) => Commands?.OpenPickPath?.Invoke();
        btnSwapABRecordedPath.Click += (_, __) => Commands?.SwapABRecordedPath?.Invoke();

        // ---- menuStrip1 (hamburger) ----
        loadVehicleToolToolStripMenuItem.Click += (_, __) => ShowVehicleConfig();
        menustripLanguage.Click += (_, __) => Commands?.OpenLanguage?.Invoke();
        simulatorOnToolStripMenuItem.Click += (_, __) => Commands?.ToggleSimulator?.Invoke();
        enterSimCoordsToolStripMenuItem.Click += (_, __) => Commands?.EnterSimCoords?.Invoke();
        kioskModeToolStrip.Click += (_, __) => ToggleKioskMode();
        resetALLToolStripMenuItem.Click += (_, __) => Commands?.ResetAll?.Invoke();
        resetEverythingToolStripMenuItem.Click += (_, __) => Commands?.ResetAll?.Invoke();
        AgShareApiMenuItem.Click += (_, __) => Commands?.OpenAgShareApi?.Invoke();
        checkForUpdatesToolStripMenuItem.Click += (_, __) => Commands?.CheckForUpdates?.Invoke();
        helpMenuItem.Click += (_, __) => Commands?.ShowHelp?.Invoke();

        // ---- btnFieldTools flyout (was toolStripBtnFieldTools + Tools-menu boundary tool) ----
        // Each opens a migrated Field/Guidance editor; the composition-root closure applies the original
        // job-started / boundary / guidance-line gating and refreshes the bottom panel after close.
        menuBoundaries.Click += (_, __) => Commands?.OpenBoundary?.Invoke();
        menuHeadland.Click += (_, __) => Commands?.OpenHeadland?.Invoke();
        menuHeadlandBuild.Click += (_, __) => Commands?.OpenHeadlandBuild?.Invoke();
        menuTramLines.Click += (_, __) => Commands?.OpenTramLines?.Invoke();
        menuBoundaryTool.Click += (_, __) => Commands?.OpenBoundaryTool?.Invoke();
    }

    // ============================================================================================
    //  [XPLAT] View-side helpers used by the button wiring.
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Swaps a button-face <see cref="Image"/> to the named asset under <c>avares://AgOpenGPS/btnImages/</c>,
    /// the cross-platform stand-in for the WinForms <c>btn.Image = Resources.X</c> assignment. A load failure is
    /// logged and the previous face is kept, so a missing asset never crashes the shell.
    /// </summary>
    /// <param name="target">The image element to update; ignored when <see langword="null"/>.</param>
    /// <param name="assetFileName">The PNG file name within the <c>btnImages</c> folder.</param>
    private static void SetImage(Image target, string assetFileName)
    {
        if (target == null || string.IsNullOrEmpty(assetFileName))
        {
            return;
        }

        try
        {
            var uri = new Uri("avares://AgOpenGPS/btnImages/" + assetFileName);
            target.Source = new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception ex)
        {
            Log.EventWriter("Button image swap failed for " + assetFileName + ": " + ex.Message);
        }
    }

    /// <summary>Maps a U-turn skip mode ordinal (the value returned by <see cref="ShellCommands.YouSkipEnable"/>) to its button face.</summary>
    private static string YouSkipAsset(int skipMode) => skipMode switch
    {
        1 => "YouSkipOn.png",            // Alternative
        2 => "YouSkipWorkedTracks.png",  // IgnoreWorkedTracks
        _ => "YouSkipOff.png",           // Normal
    };

    /// <summary>Maps a tram display mode (0..3) to its button face.</summary>
    private static string TramAsset(int displayMode) => displayMode switch
    {
        1 => "TramAll.png",
        2 => "TramLines.png",
        3 => "TramOuter.png",
        _ => "TramOff.png",
    };

    /// <summary>Maps a recorded-path resume state (0..2) to its button face.</summary>
    private static string ResumeAsset(int resumeState) => resumeState switch
    {
        1 => "pathResumeLast.png",
        2 => "pathResumeClose.png",
        _ => "pathResumeStart.png",
    };

    /// <summary>
    /// [XPLAT] Flashes a guidance-track name in <c>lblGuidanceLine</c> after a cycle-lines action (the
    /// <see cref="RenderCoordinator.OnGuidanceLineExpired"/> callback later clears it, matching the WinForms
    /// guideLineCounter behaviour). A null/empty name leaves the label hidden.
    /// </summary>
    private void FlashGuidanceLine(string trackName)
    {
        if (string.IsNullOrEmpty(trackName))
        {
            return;
        }

        lblGuidanceLine.Text = trackName;
        lblGuidanceLine.IsVisible = true;
    }

    /// <summary>
    /// [XPLAT] Repaints every panelRight guidance/steering button face from the current domain state exposed by
    /// the <see cref="ShellCommands"/> readers. Called after any guidance toggle so cross-button effects (contour
    /// turning autosteer off, cycling tracks turning autosteer off, a path start disengaging guidance) stay in
    /// sync — the cross-platform equivalent of the WinForms handlers re-imaging sibling buttons through
    /// PerformClick. The autosteer face selects the snap-to-pivot variant exactly as the original did.
    /// </summary>
    private void RefreshGuidanceFaces()
    {
        if (Commands == null)
        {
            return;
        }

        bool contour = Commands.IsContourOn?.Invoke() ?? false;
        SetImage(imgContour, contour ? "ContourOn.png" : "ContourOff.png");
        SetImage(imgContourLock, (Commands.IsContourLocked?.Invoke() ?? false) ? "ColorLocked.png" : "ColorUnlocked.png");
        SetImage(imgAutoTrack, (Commands.IsAutoTrackOn?.Invoke() ?? false) ? "AutoTrack.png" : "AutoTrackOff.png");
        SetImage(imgAutoYouTurn, (Commands.IsYouTurnOn?.Invoke() ?? false) ? "YouTurn80.png" : "YouTurnNo.png");

        bool steer = Commands.IsAutoSteerOn?.Invoke() ?? false;
        bool snap = Commands.IsAutoSnapToPivotOn?.Invoke() ?? false;
        SetImage(imgAutoSteer, steer
            ? (snap ? "AutoSteerOnSnapToPivot.png" : "AutoSteerOn.png")
            : (snap ? "AutoSteerOffSnapToPivot.png" : "AutoSteerOff.png"));

        // Contour replaces/hides the track button; the U-turn button is only meaningful with an active track and
        // no contour (parity with WinForms Disable/EnableYouTurnButtons and the contour-on btnTrack hide).
        bool hasTrack = Commands.HasActiveTrack?.Invoke() ?? false;
        if (btnTrack != null)
        {
            btnTrack.IsVisible = !contour;
            btnTrack.IsEnabled = !contour;
        }
        if (btnAutoYouTurn != null)
        {
            btnAutoYouTurn.IsEnabled = !contour && hasTrack;
        }
    }

    /// <summary>
    /// [XPLAT] Repaints the panelBottom button faces (U-turn skip, tramline display, hydraulic lift, headland)
    /// from the current domain state. Called after the corresponding toggles; headland-off also forces the
    /// hydraulic-lift face off because the domain clears it.
    /// </summary>
    private void RefreshBottomFaces()
    {
        if (Commands == null)
        {
            return;
        }

        SetImage(imgYouSkip, YouSkipAsset(Commands.YouSkipMode?.Invoke() ?? 0));
        SetImage(imgTram, TramAsset(Commands.TramDisplayMode?.Invoke() ?? 0));
        SetImage(imgHydLift, (Commands.IsHydLiftOn?.Invoke() ?? false) ? "HydraulicLiftOn.png" : "HydraulicLiftOff.png");
        SetImage(imgHeadland, (Commands.IsHeadlandOn?.Invoke() ?? false) ? "HeadlandOn.png" : "HeadlandOff.png");
    }

    /// <summary>
    /// [XPLAT] Repaints the panelDrag recorded-path button faces (go/stop, record/stop, resume style) from the
    /// current domain state and enforces the mutual-exclusivity enable states the WinForms handlers toggled
    /// (you cannot record while driving a path, or pick/resume while either is active).
    /// </summary>
    private void RefreshPathFaces()
    {
        if (Commands == null)
        {
            return;
        }

        bool driving = Commands.IsDrivingRecordedPath?.Invoke() ?? false;
        bool recording = Commands.IsRecordingPath?.Invoke() ?? false;

        SetImage(imgPathGoStop, driving ? "boundaryStop.png" : "boundaryPlay.png");
        SetImage(imgPathRecordStop, recording ? "boundaryStop.png" : "BoundaryRecord.png");
        SetImage(imgResumePath, ResumeAsset(Commands.ResumeState?.Invoke() ?? 0));

        if (btnPathGoStop != null) btnPathGoStop.IsEnabled = !recording;
        if (btnPathRecordStop != null) btnPathRecordStop.IsEnabled = !driving;
        if (btnPickPath != null) btnPickPath.IsEnabled = !driving && !recording;
        if (btnResumePath != null) btnResumePath.IsEnabled = !driving && !recording;
    }

    /// <summary>
    /// [XPLAT] Toggles day/night by flipping the bound <see cref="ApplicationViewModel.IsDay"/> (which the
    /// composition root subscribes to, flipping <c>Application.RequestedThemeVariant</c> and persisting the
    /// setting — the cross-platform <c>SwapDayNightMode</c> path). Also swaps the button face, recolours the
    /// section/zone buttons for the new palette and requests a field redraw.
    /// </summary>
    private void ToggleDayNight()
    {
        if (DataContext is ApplicationViewModel vm)
        {
            vm.IsDay = !vm.IsDay;
            SetImage(imgDayNight, vm.IsDay ? "WindowNightMode.png" : "WindowDayMode.png");
            RefreshSectionZoneButtons();
            _viewport?.RequestRender();
        }
    }

    /// <summary>
    /// [XPLAT] Hamburger "Kiosk Mode": toggles borderless full-screen (parity with the WinForms kiosk toggle that
    /// flipped the form border/state). The shell already starts borderless-maximised, so this restores the normal
    /// chrome and back.
    /// </summary>
    private void ToggleKioskMode()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
            SystemDecorations = SystemDecorations.Full;
        }
        else
        {
            SystemDecorations = SystemDecorations.None;
            WindowState = WindowState.FullScreen;
        }
    }

    /// <summary>Resolves the manual section button (btnSection1Man..btnSection16Man) for <paramref name="n"/> (1..16).</summary>
    private Button ManualSectionButton(int n) => n switch
    {
        1 => btnSection1Man,
        2 => btnSection2Man,
        3 => btnSection3Man,
        4 => btnSection4Man,
        5 => btnSection5Man,
        6 => btnSection6Man,
        7 => btnSection7Man,
        8 => btnSection8Man,
        9 => btnSection9Man,
        10 => btnSection10Man,
        11 => btnSection11Man,
        12 => btnSection12Man,
        13 => btnSection13Man,
        14 => btnSection14Man,
        15 => btnSection15Man,
        16 => btnSection16Man,
        _ => null,
    };

    /// <summary>
    /// [XPLAT] Paints a section/zone button in its tri-state colour, reproducing the WinForms <c>SetColors</c>
    /// palette (Off = Red/Crimson, Auto = Lime/ForestGreen, On = Yellow/DarkGoldenrod; foreground Black/White)
    /// chosen by the current day/night mode.
    /// </summary>
    private void SetSectionZoneButtonColor(Button button, btnStates state)
    {
        if (button == null)
        {
            return;
        }

        bool day = (DataContext as ApplicationViewModel)?.IsDay ?? true;
        Color background = state switch
        {
            btnStates.Auto => day ? Colors.Lime : Colors.ForestGreen,
            btnStates.On => day ? Colors.Yellow : Colors.DarkGoldenrod,
            _ => day ? Colors.Red : Colors.Crimson,
        };

        button.Background = new SolidColorBrush(background);
        button.Foreground = new SolidColorBrush(day ? Colors.Black : Colors.White);
    }

    /// <summary>
    /// [XPLAT] Lays out and colours the manual-section (1..16) and zone (1..8) buttons for the current tool
    /// configuration and job state — the cross-platform equivalent of the WinForms
    /// <c>LineUpIndividualSectionBtns</c> / <c>LineUpAllZoneButtons</c>. Section buttons are visible only when a
    /// job is started, the tool is in unique-section mode, and the section count covers the button; zone buttons
    /// only in zone mode. Visible buttons are painted with their current tri-state colour. (Findings MV-1/MV-3.)
    /// </summary>
    private void RefreshSectionZoneButtons()
    {
        if (_sections == null)
        {
            return;
        }

        bool jobStarted = _sections.IsJobStarted;
        bool sectionsMode = _sections.IsSectionsNotZones;
        int sectionCount = _sections.NumOfSections;
        int zoneCount = _sections.NumOfZones;

        for (int i = 1; i <= 16; i++)
        {
            Button b = ManualSectionButton(i);
            if (b == null) continue;

            bool visible = jobStarted && sectionsMode && sectionCount >= i;
            b.IsVisible = visible;
            if (visible)
            {
                SetSectionZoneButtonColor(b, _sections.GetSectionState(i - 1));
            }
        }

        for (int i = 1; i <= 8; i++)
        {
            Button z = ZoneButton(i);
            if (z == null) continue;

            bool visible = jobStarted && !sectionsMode && zoneCount >= i;
            z.IsVisible = visible;
            if (visible)
            {
                SetSectionZoneButtonColor(z, _sections.GetZoneState(i));
            }
        }
    }

    /// <summary>
    /// [XPLAT] Refreshes the shell status labels driven by domain readers — the current track number/count
    /// (<c>lblNumCu</c>) and the latest flag number (<c>lblFlagNumber</c>). Safe to call when the command map or
    /// a particular reader is not yet populated (the label is simply left unchanged).
    /// </summary>
    private void RefreshShellLabels()
    {
        if (Commands?.TrackCountText != null)
        {
            lblNumCu.Text = Commands.TrackCountText() ?? string.Empty;
        }

        if (Commands?.FlagCountText != null)
        {
            lblFlagNumber.Text = Commands.FlagCountText() ?? lblFlagNumber.Text;
        }
    }

    /// <summary>
    /// [XPLAT] Updates which entries of the track flyout (<c>flp1</c>) are shown: the "tracks off" entry only
    /// when at least one track is visible, and the AB-draw entry only when a field boundary exists (parity with
    /// the WinForms flyout gating). Other entries are always available.
    /// </summary>
    private void RefreshTrackFlyout()
    {
        if (btnTracksOff != null && Commands?.TrackVisibleCount != null)
        {
            btnTracksOff.IsVisible = Commands.TrackVisibleCount() > 0;
        }

        if (btnABDraw != null && Commands?.HasBoundary != null)
        {
            btnABDraw.IsVisible = Commands.HasBoundary();
        }
    }

    /// <summary>
    /// [XPLAT] Re-paints every shell affordance from the current domain state and requests a redraw. This is the
    /// cross-platform stand-in for the WinForms <c>PanelUpdateRightAndBottom()</c> + <c>PanelsAndOGLSize()</c> +
    /// <c>SetZoom()</c> sequence the FormGPS dialog open-sites ran after a Field/Guidance editor closed (e.g.
    /// <c>GetHeadland()</c>, <c>boundariesToolStripMenuItem_Click</c>). The dialog-navigation closures in the
    /// composition root (App.axaml.cs) invoke this when a migrated editor is dismissed, so a boundary/headland/
    /// tram/track edit is immediately reflected on the bottom panel faces, the section/zone buttons, the status
    /// labels, the track flyout gating, and the GL viewport — exactly as the WinForms shell refreshed itself.
    /// Safe to call at any time; each helper is independently null-guarded.
    /// </summary>
    public void RefreshAfterDialog()
    {
        RefreshGuidanceFaces();
        RefreshBottomFaces();
        RefreshPathFaces();
        RefreshSectionZoneButtons();
        RefreshShellLabels();
        RefreshTrackFlyout();
        _viewport?.RequestRender();
    }

    // [XPLAT] MIGRATION SUMMARY (recap): FormGPS window -> Avalonia MainView; the FormGPS god-object / `mf`
    // back-reference -> the five constructor-injected, behaviour-frozen Services + the ApplicationViewModel
    // (via DataContext); OpenTK.GLControl (oglMain/oglZoom/oglBack) -> AvaloniaGeoViewport over OpenGlControlBase;
    // WinForms KeyPreview + ProcessCmdKey -> the window-level KeyDown dispatcher above; the deleted
    // DraggableControlExtension -> the pointer-event panel drag above; and Win32 SetForegroundWindow/ShowWindow
    // -> the cross-platform Window.Activate(). No domain mathematics live here, and the view never adds latency
    // to the receive->fuse->steer->section path (AAP §0.7.1). See MIGRATION_DOCS/TRANSITION_MAP.md.
}
