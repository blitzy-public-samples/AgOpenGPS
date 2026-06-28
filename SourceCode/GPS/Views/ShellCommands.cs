// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] ShellCommands is the "command map" for the kiosk main shell (MainView). In WinForms, every
// FormGPS button had a `btnXxx_Click` handler living inside the FormGPS partial classes
// (Controls.Designer.cs), reaching the domain objects (ct/trk/yt/ABLine/curve/tram/bnd/vehicle/sim/
// recPath/sounds/flagPts/pn/isobus/fd/appModel) directly through the `mf` god-object back-reference.
//
// After the god-object split those domain objects live ONLY in the composition root (App.axaml.cs),
// while MainView is a thin view that owns just the five behaviour-frozen services + the shared Camera.
// To wire the operator buttons WITHOUT re-introducing a wide domain dependency on the view (and without
// adding a new architectural layer beyond IPlatformServices + the GL adapter — AAP R7), the composition
// root populates this plain delegate bundle with closures that capture the domain objects, and MainView's
// code-behind Click handlers invoke the matching delegate. The bundle IS the testable "command map" the
// final-checkpoint finding (MV-1) asks for: <see cref="GetUnpopulatedCommands"/> enumerates any operator
// action that was left unwired, so coverage of the original FormGPS action set is verifiable.
//
// Discipline (parity, AAP §0.6.1): every delegate reproduces the *domain* behaviour of its WinForms
// `btnXxx_Click` verbatim (including sounds and timed messages, which the closures raise through the
// composition root's CSound / the view's timed-message sink). The *view* feedback that depended on the
// resulting domain state — button image swaps and status labels — is returned to the caller (Func<bool>/
// Func<int>/Func<string>) so MainView, which owns the controls, performs the visual update. No domain
// mathematics live in the view, and the view never reaches into the domain objects directly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] The operator-action command map for <see cref="MainView"/>. Each public delegate corresponds to
/// exactly one WinForms <c>FormGPS</c> button/menu action; the composition root assigns each a closure over
/// the domain objects, and the view invokes them from its code-behind Click handlers. Toggle commands return
/// their resulting visual state (a <see cref="bool"/> on/off or an <see cref="int"/> mode) so the view can
/// swap the button face exactly as the WinForms handler set <c>btnXxx.Image</c>; pure actions return
/// <see langword="void"/>; status readers expose label text the view paints.
/// </summary>
/// <remarks>
/// Nullability: a delegate left <see langword="null"/> means "this action is not wired". MainView guards every
/// invocation with the null-conditional operator so a partially-populated map can never throw; the composition
/// root populates the full set, and <see cref="GetUnpopulatedCommands"/> lets a test assert complete coverage.
/// </remarks>
public sealed class ShellCommands
{
    // ============================================================================================
    //  Guidance / steering toggles (panelRight). Each returns its resulting on/off (or mode) so the
    //  view swaps the button face exactly as the WinForms `btnXxx.Image = ...` assignment did.
    // ============================================================================================

    /// <summary>btnAutoSteer — engages/disengages autosteer (sounds + safe-speed guard handled in the closure). Returns the new engaged state.</summary>
    public Func<bool> ToggleAutoSteer;

    /// <summary>btnAutoYouTurn — toggles the automatic U-turn (boundary-required guard in the closure). Returns the new on state.</summary>
    public Func<bool> ToggleYouTurn;

    /// <summary>btnContour — toggles contour guidance (turns autosteer off when stopping). Returns the new on state.</summary>
    public Func<bool> ToggleContour;

    /// <summary>btnContourLock — toggles locking the contour to the current line. Returns the new locked state for the button face.</summary>
    public Func<bool> ContourLock;

    /// <summary>btnAutoTrack — toggles auto-track. Returns the new on state.</summary>
    public Func<bool> ToggleAutoTrack;

    /// <summary>cboxAutoSnapToPivot — toggles auto-snap-to-pivot. Returns the new on state.</summary>
    public Func<bool> ToggleAutoSnapToPivot;

    /// <summary>btnCycleLines — advances to the next visible guidance track; returns its name to flash (or <see langword="null"/>).</summary>
    public Func<string> CycleLines;

    /// <summary>btnCycleLinesBk — steps to the previous visible guidance track (or locks the contour line); returns its name to flash (or <see langword="null"/>).</summary>
    public Func<string> CycleLinesBack;

    // ============================================================================================
    //  Top action row (panelBottom).
    // ============================================================================================

    /// <summary>btnYouSkipEnable — cycles the U-turn skip mode (Normal→Alternative→IgnoreWorked). Returns the new <c>SkipMode</c> ordinal.</summary>
    public Func<int> YouSkipEnable;

    /// <summary>btnHeadlandOnOff — toggles headland mode (forces hydraulic lift off when disabled). Returns the new headland on state.</summary>
    public Func<bool> ToggleHeadland;

    /// <summary>cboxIsSectionControlled — toggles headland section control; argument is the new checked state.</summary>
    public Action<bool> ToggleHeadlandSectionControl;

    /// <summary>btnHydLift — toggles the hydraulic lift (only effective while headland is on). Returns the new lift on state.</summary>
    public Func<bool> ToggleHydLift;

    /// <summary>btnTramDisplayMode — cycles the tramline display mode. Returns the new display mode (0..3).</summary>
    public Func<int> CycleTramDisplay;

    /// <summary>btnResetToolHeading — snaps the tool/tank heading to the current fix heading.</summary>
    public Action ResetToolHeading;

    /// <summary>btnFlag — drops a flag at the current position, de-duplicates and saves the flag list.</summary>
    public Action AddFlag;

    /// <summary>btnAdjLeft — nudges the active track left by the configured snap distance.</summary>
    public Action NudgeLeft;

    /// <summary>btnAdjRight — nudges the active track right by the configured snap distance.</summary>
    public Action NudgeRight;

    /// <summary>btnSnapToPivot — snaps the active track to the vehicle pivot.</summary>
    public Action SnapToPivot;

    /// <summary>btnTrack — track-flyout activator: turns contour off and selects a track when one exists (the flyout visibility itself is owned by the view).</summary>
    public Action TrackButton;

    /// <summary>btnTracksOff — deselects the active track (<c>trk.idx = -1</c>).</summary>
    public Action TracksOff;

    // ============================================================================================
    //  Left guidance column (panelLeft) and two-program control.
    // ============================================================================================

    /// <summary>btnStartAgIO — manually launches/focuses the AgIO hub (reuses <c>Program.StartAgIO</c>).</summary>
    public Action StartAgIO;

    // ============================================================================================
    //  Top-right control box (panelControlBox).
    // ============================================================================================

    /// <summary>btnGPSData — opens (or closes if already open) the live GPS-data window (<c>FormGPSDataView</c>).</summary>
    public Action ShowGpsData;

    /// <summary>btnFieldStats — opens (or closes if already open) the field-statistics window. Only meaningful while a job is started.</summary>
    public Action ShowFieldStats;

    // ============================================================================================
    //  Navigation overlay (panelNavigation): brightness. Camera tilt/2D/3D/N2D and day/night are handled
    //  directly by the view (it owns the shared Camera and the day/night view-model), so they are not here.
    // ============================================================================================

    /// <summary>btnBrightnessUp — increases monitor brightness by one step (per <see cref="Core.Platform.IPlatformServices"/>). Returns the new brightness label text.</summary>
    public Func<string> BrightnessUp;

    /// <summary>btnBrightnessDn — decreases monitor brightness by one step. Returns the new brightness label text.</summary>
    public Func<string> BrightnessDown;

    // ============================================================================================
    //  Simulator bar (panelSim).
    // ============================================================================================

    /// <summary>btnSimSpeedUp — accelerates the simulator one step (was the WinForms MouseDown handler).</summary>
    public Action SimSpeedUp;

    /// <summary>btnSpeedDn — decelerates the simulator one step (was the WinForms MouseDown handler).</summary>
    public Action SimSpeedDown;

    /// <summary>btnSimSetSpeedToZero — sets the simulator speed to zero.</summary>
    public Action SimSetSpeedToZero;

    /// <summary>btnSimReverseDirection — flips the simulator heading 180° (turns autosteer off).</summary>
    public Action SimReverseDirection;

    /// <summary>btnResetSim — resets the simulator to the configured start lat/lon.</summary>
    public Action SimReset;

    /// <summary>btnResetSteerAngle — zeroes the simulator steer-angle (the view also re-centres the slider).</summary>
    public Action SimResetSteerAngle;

    /// <summary>hsbarSteerAngle — applies the simulator steer-angle slider value to the simulator domain.</summary>
    public Action<double> SimSteerAngleScroll;

    // ============================================================================================
    //  Recorded-path panel (panelDrag).
    // ============================================================================================

    /// <summary>btnPathGoStop — starts/stops driving the recorded path. Returns the new driving state (controls the play/stop face and sibling enabled states).</summary>
    public Func<bool> PathGoStop;

    /// <summary>btnPathRecordStop — starts/stops recording a path (the save prompt is a dialog handled in the closure). Returns the new recording state.</summary>
    public Func<bool> PathRecordStop;

    /// <summary>btnResumePath — cycles the path resume style (Start→Last→Close). Returns the new resume state (0..2) for the button face.</summary>
    public Func<int> ResumePath;

    /// <summary>btnSwapABRecordedPath — reverses the recorded path point order/headings.</summary>
    public Action SwapABRecordedPath;

    // ============================================================================================
    //  Dialog-navigation commands (the flyout/menu buttons that open a dialog). These are declared here
    //  so MainView attaches a handler to every button; the composition root assigns them where the
    //  migrated dialog views are wired into the shell navigation graph (see TRANSITION_MAP.md).
    // ============================================================================================

    /// <summary>btnAutoSteerConfig — opens the steer-configuration dialog (FormSteerView).</summary>
    public Action OpenSteerConfig;

    /// <summary>btnPlusAB — opens the quick-AB / add-track dialog.</summary>
    public Action OpenQuickAB;

    /// <summary>btnBuildTracks — opens the build-tracks dialog.</summary>
    public Action OpenBuildTracks;

    /// <summary>btnABDraw — opens the AB-draw dialog (<c>FormABDrawView</c>).</summary>
    public Action OpenABDraw;

    /// <summary>btnNudge — opens the track-nudge dialog.</summary>
    public Action OpenNudge;

    /// <summary>btnRefNudge — opens the reference-nudge dialog.</summary>
    public Action OpenRefNudge;

    /// <summary>btnChangeMappingColor — opens the section-mapping colour picker.</summary>
    public Action OpenMappingColor;

    /// <summary>btnGrid — opens the grid/rotate dialog (<c>FormGridView</c>).</summary>
    public Action OpenGrid;

    /// <summary>btnPickPath — opens the recorded-path picker dialog.</summary>
    public Action OpenPickPath;

    /// <summary>Steer-wizard hotkey/menu — opens the WAS-calibration steer wizard.</summary>
    public Action OpenSteerWizard;

    /// <summary>cboxpRowWidth — sets the U-turn row-skip width from the combo selection (1-based).</summary>
    public Action<int> SetRowSkipWidth;

    /// <summary>Hamburger "Simulator On" — toggles the built-in simulator on/off.</summary>
    public Action ToggleSimulator;

    /// <summary>Hamburger "Enter Sim Coords" — opens the simulator start-coordinate dialog.</summary>
    public Action EnterSimCoords;

    /// <summary>Hamburger "Language" — opens the language-selection dialog.</summary>
    public Action OpenLanguage;

    /// <summary>Hamburger "Reset All" / "Reset To Default" — resets settings to defaults.</summary>
    public Action ResetAll;

    /// <summary>Hamburger "AgShare API" — opens the AgShare configuration dialog.</summary>
    public Action OpenAgShareApi;

    /// <summary>Hamburger "Check for Updates" — runs the update check.</summary>
    public Action CheckForUpdates;

    /// <summary>Hamburger "Help" — opens product help.</summary>
    public Action ShowHelp;

    // --------------------------------------------------------------------------------------------
    //  Field-tools menu (was the WinForms toolStripBtnFieldTools dropdown + the Tools-menu boundary
    //  tool). MainView's btnFieldTools flyout attaches each item here; the composition root opens the
    //  migrated Field/Guidance editor dialog with the gating the original handlers applied.
    // --------------------------------------------------------------------------------------------

    /// <summary>Field Tools ▸ Boundaries — opens the boundary editor (<c>FormBoundaryView</c>; was boundariesToolStripMenuItem).</summary>
    public Action OpenBoundary;

    /// <summary>Field Tools ▸ Headland — opens the headland-line editor (<c>FormHeadLineView</c>; was headlandToolStripMenuItem → GetHeadland).</summary>
    public Action OpenHeadland;

    /// <summary>Field Tools ▸ Headland (Build) — opens the headland-build editor (<c>FormHeadAcheView</c>; was headlandBuildToolStripMenuItem).</summary>
    public Action OpenHeadlandBuild;

    /// <summary>Field Tools ▸ Tram Lines — opens the tramline editor (<c>FormTramLineView</c>; was tramLinesMenuField).</summary>
    public Action OpenTramLines;

    /// <summary>Field Tools ▸ Boundary Tool — opens the boundary geometry tool (<c>FormBndToolView</c>; was boundaryToolToolStripMenu).</summary>
    public Action OpenBoundaryTool;

    // ============================================================================================
    //  Status readers — the view paints these onto labels / button faces; they never mutate domain state.
    // ============================================================================================

    /// <summary>lblNumCu — current track number and count, e.g. "3/7" (empty when no track is active).</summary>
    public Func<string> TrackCountText;

    /// <summary>lblFlagNumber — the latest flag number text.</summary>
    public Func<string> FlagCountText;

    /// <summary>btnHydLift face dependency — the current hydraulic-lift state (read after <see cref="ToggleHeadland"/> forces it off).</summary>
    public Func<bool> IsHydLiftOn;

    /// <summary>flp1 track-flyout: count of currently visible tracks (drives which flyout buttons show).</summary>
    public Func<int> TrackVisibleCount;

    /// <summary>flp1 track-flyout: whether a field boundary exists (gates the AB-draw flyout entry).</summary>
    public Func<bool> HasBoundary;

    // --------------------------------------------------------------------------------------------
    //  Guidance/steering/path STATE readers. The view repaints every guidance, bottom-row and
    //  recorded-path button face from these after any toggle, so cross-button effects (e.g. enabling
    //  contour turning autosteer off, or starting a path turning contour/youturn/autosteer off) always
    //  stay in sync — exactly as the WinForms handlers re-imaged the sibling buttons via PerformClick.
    // --------------------------------------------------------------------------------------------

    /// <summary>ct.isContourBtnOn — whether contour guidance is engaged (drives imgContour and hides btnTrack).</summary>
    public Func<bool> IsContourOn;

    /// <summary>ct.isLocked — whether the contour is locked to the current line (drives imgContourLock).</summary>
    public Func<bool> IsContourLocked;

    /// <summary>trk.isAutoTrack — whether auto-track is engaged (drives imgAutoTrack).</summary>
    public Func<bool> IsAutoTrackOn;

    /// <summary>appModel.isBtnAutoSteerOn — whether autosteer is engaged (drives imgAutoSteer; combined with <see cref="IsAutoSnapToPivotOn"/> for the snap-to-pivot face variant).</summary>
    public Func<bool> IsAutoSteerOn;

    /// <summary>yt.isYouTurnBtnOn — whether the automatic U-turn is armed (drives imgAutoYouTurn).</summary>
    public Func<bool> IsYouTurnOn;

    /// <summary>trk.idx &gt; -1 — whether a guidance track is currently active (gates btnAutoYouTurn enable and the autosteer guard).</summary>
    public Func<bool> HasActiveTrack;

    /// <summary>trk.isAutoSnapToPivot — selects the snap-to-pivot autosteer button face variant.</summary>
    public Func<bool> IsAutoSnapToPivotOn;

    /// <summary>bnd.isHeadlandOn — whether headland mode is on (drives imgHeadland).</summary>
    public Func<bool> IsHeadlandOn;

    /// <summary>recPath.isDrivingRecordedPath — whether a recorded path is being driven (drives imgPathGoStop and sibling enable states).</summary>
    public Func<bool> IsDrivingRecordedPath;

    /// <summary>recPath.isRecordOn — whether a path is being recorded (drives imgPathRecordStop and sibling enable states).</summary>
    public Func<bool> IsRecordingPath;

    /// <summary>yt.skipMode ordinal (0 Normal / 1 Alternative / 2 IgnoreWorkedTracks) — drives imgYouSkip.</summary>
    public Func<int> YouSkipMode;

    /// <summary>tram.displayMode (0..3) — drives imgTram.</summary>
    public Func<int> TramDisplayMode;

    /// <summary>recPath.resumeState (0..2) — drives imgResumePath.</summary>
    public Func<int> ResumeState;

    // ============================================================================================
    //  Coverage helper — enumerates every command/reader delegate that is still null. A test asserts the
    //  fully-populated map leaves none unwired, proving the command map covers all original FormGPS actions
    //  (final-checkpoint finding MV-1); the composition root logs any gap at startup.
    // ============================================================================================

    /// <summary>
    /// [XPLAT] Returns the names of every public delegate field on this map that is currently
    /// <see langword="null"/> (i.e. an operator action that has not been wired). An empty result proves
    /// the command map is complete.
    /// </summary>
    /// <returns>The unwired delegate field names, in declaration order.</returns>
    public IReadOnlyList<string> GetUnpopulatedCommands()
    {
        return typeof(ShellCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
            .Where(f => f.GetValue(this) == null)
            .Select(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// [XPLAT] The total number of operator-action / reader delegates the map declares — the size of the
    /// command map. Used by the coverage test as a regression floor against accidental removal of wiring.
    /// </summary>
    public static int CommandCount =>
        typeof(ShellCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Count(f => typeof(Delegate).IsAssignableFrom(f.FieldType));
}
