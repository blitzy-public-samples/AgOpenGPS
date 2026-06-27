// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// SectionService is the cross-platform extraction of the AgOpenGPS section/zone state machine,
// the machine-byte (PGN) builder, and the remote-switch handlers. It was lifted (behavior FROZEN)
// out of the former WinForms god-object FormGPS partial class:
//   * SourceCode/GPS/Forms/Sections.Designer.cs — the section/zone tri-state state machine, the
//     section geometry (back-buffer pixel coordinates), the BuildMachineByte() PGN assembler, and
//     the DoRemoteSwitches()/HandleButtonHardware()/HandleSwitchHardware() remote-switch handlers.
//
// Decoupling notes (AAP §0.6.1 — state separation):
//   * Every former direct FormGPS member access becomes a constructor-injected collaborator. There
//     is no `mf`/FormGPS back-reference and no WinForms/Drawing/Media type is referenced.
//   * The section-master tri-states (manualBtnState/autoBtnState), the field-job gate (isJobStarted)
//     and the fused average speed (avgSpeed) are NOT held locally — they live in the shared
//     AgOpenGPS.Core ApplicationModel, which is the SAME state the CModuleComm work/steer-switch
//     bridge and the Avalonia view-models read and write. Holding local copies would diverge from
//     CModuleComm and the UI; reading/writing appModel keeps the section-control activation
//     behavior-identical to the net48 original.
//   * Where the WinForms original called a Button's PerformClick() or mutated a Button's image,
//     this service performs only the DOMAIN state change and raises an event (OnSectionStateChanged /
//     OnZoneStateChanged / OnMasterStateChanged / OnManualStateChanged) that the Avalonia view
//     subscribes to for the visual update. The state machine is domain (here); the visuals are view.
//
// Behavior contract (AAP §0.2.2, §0.7.1 — FROZEN, parity-tested by PgnFrameGoldenTests +
// GuidanceEquivalenceTests):
//   * BuildMachineByte() produces byte-identical PGN 0xFE (254) / 0xEF (239) / 0xE5 (229) section
//     bytes for BOTH "sections-not-zones" (1–16 unique sections) and "zones" (up to 64 same-width
//     sections) modes, and always sets p_239.speed and p_239.tram.
//   * The section geometry rounding uses MidpointRounding.AwayFromZero (frozen).
//   * DoRemoteSwitches() acts only when isJobStarted is true (frozen safety gate) and selects the
//     button-vs-switch hardware path on (mc.ss[mc.swMain] & (1<<2)) == 0.
//
// Collaboration (AAP key insight): SectionService owns the section state machine + machine-byte
// assembly; the RenderCoordinator owns the render-coupled oglBack pixel scan that produces coverage
// into the shared CSection[] (section[j].isSectionOn). The two communicate ONLY through the shared
// CSection[] and the shared PgnDispatcher PGN instances — no `mf`, no new interface.
using System;
using AgOpenGPS.Core;        // btnStates, ApplicationModel (canonical shared section-master/job/speed state)
using AgOpenGPS.Properties;  // ToolSettings (settings singletons)
// NOTE: the domain collaborator types (CSection, CTool, CTram, CModuleComm, CSound, CTrack, CTrk) and the
// TrackMode enum live in the parent `AgOpenGPS` namespace; because this file is declared in the nested
// `AgOpenGPS.Services` namespace, the C# enclosing-namespace lookup resolves them without an explicit
// `using AgOpenGPS;` (a directive Roslyn's IDE0005 would otherwise flag as redundant — and which would
// fail the Release `TreatWarningsAsErrors` build). This mirrors the sibling PgnDispatcher.cs.

namespace AgOpenGPS.Services
{
    /// <summary>
    /// [XPLAT] Cross-platform section/zone state machine, machine-byte (PGN) assembler, and
    /// remote-switch handler, extracted (behavior FROZEN) from the WinForms FormGPS partial
    /// <c>Sections.Designer.cs</c>. All former <c>mf</c> members are constructor-injected; no WinForms,
    /// System.Drawing, or System.Media type is referenced. Button visuals are delegated to the view via
    /// events; the tri-state section/zone machine and the PGN byte assembly are owned here.
    /// </summary>
    public class SectionService
    {
        // [XPLAT] Maximum number of sections, relocated from FormGPS (`public const int MAXSECTIONS = 64;`).
        // The injected CSection[] is allocated with this length by the composition root (was FormGPS init).
        private const int MAXSECTIONS = 64;

        // ---- Injected collaborators (former FormGPS `mf.*` access — AAP §0.6.1 state separation) ----

        /// <summary>Shared section array (also written by RenderCoordinator's oglBack coverage scan).</summary>
        private readonly CSection[] section;

        /// <summary>Implement/tool geometry + per-side speeds (was mf.tool).</summary>
        private readonly CTool tool;

        /// <summary>Tramline control byte source for PGN 0xEF (was mf.tram).</summary>
        private readonly CTram tram;

        /// <summary>Module-comm switch/byte state for remote switches (was mf.mc).</summary>
        private readonly CModuleComm mc;

        /// <summary>Section on/off audio cues — cross-platform CSound (was mf.sounds).</summary>
        private readonly CSound sounds;

        /// <summary>Track collection, for marking the current pass worked (was mf.trk).</summary>
        private readonly CTrack trk;

        /// <summary>Owns the shared p_254/p_239/p_229 PGN instances populated by BuildMachineByte (was mf).</summary>
        private readonly PgnDispatcher pgn;

        /// <summary>
        /// Shared AgOpenGPS.Core runtime state. Source of the canonical section-master tri-states
        /// (manualBtnState/autoBtnState), the field-job gate (isJobStarted) and the fused average speed
        /// (avgSpeed) — the SAME instance the CModuleComm bridge and the Avalonia view-models use.
        /// </summary>
        private readonly ApplicationModel appModel;

        // [XPLAT] AB-line / curve "how many paths away" live readers. In the net48 original
        // MarkAsWorkedTrack() read mf.ABLine.howManyPathsAway / mf.curve.howManyPathsAway directly.
        // CABLine/CABCurve are not collaborators of this service, so the composition root supplies these
        // as late-bound accessors (same delegate-injection pattern PgnDispatcher uses for postToUi/reportError).
        private readonly Func<int> getABLineHowManyPathsAway;
        private readonly Func<int> getCurveHowManyPathsAway;

        // ---- Events (replace the WinForms Button visual updates; the view subscribes) ---------------

        /// <summary>
        /// [XPLAT] Raised when an individual section's tri-state changes (was a Button image/color update).
        /// Argument 1 is the zero-based section index (0..MAXSECTIONS-1); argument 2 is the new state.
        /// </summary>
        public event Action<int, btnStates> OnSectionStateChanged;

        /// <summary>
        /// [XPLAT] Raised when a zone's tri-state changes (was a zone Button image/color update).
        /// Argument 1 is the one-based zone button index (1..8, matching the former btnZone1..btnZone8);
        /// argument 2 is the new state applied to every section in that zone's range.
        /// </summary>
        public event Action<int, btnStates> OnZoneStateChanged;

        /// <summary>
        /// [XPLAT] Raised when the section-master AUTO tri-state changes (was btnSectionMasterAuto.Image).
        /// </summary>
        public event Action<btnStates> OnMasterStateChanged;

        /// <summary>
        /// [XPLAT] Raised when the section-master MANUAL tri-state changes (was btnSectionMasterManual.Image).
        /// </summary>
        public event Action<btnStates> OnManualStateChanged;

        /// <summary>
        /// Constructs the section service with its injected collaborators. No <c>FormGPS</c> reference is
        /// taken; the composition root wires the shared instances so the section state machine, the
        /// CModuleComm work/steer-switch bridge, the RenderCoordinator coverage scan, and the Avalonia
        /// view-models all operate over the SAME <paramref name="section"/> array and <paramref name="appModel"/>.
        /// </summary>
        /// <param name="section">Shared section array (length <see cref="MAXSECTIONS"/>); coverage written by RenderCoordinator.</param>
        /// <param name="tool">Tool/implement geometry and per-side speeds (was mf.tool).</param>
        /// <param name="tram">Tramline control byte source (was mf.tram).</param>
        /// <param name="mc">Module-comm switch state for remote switches (was mf.mc).</param>
        /// <param name="sounds">Cross-platform section audio cues (was mf.sounds).</param>
        /// <param name="trk">Track collection for marking worked passes (was mf.trk).</param>
        /// <param name="pgn">Dispatcher owning the shared p_254/p_239/p_229 PGN instances (was mf).</param>
        /// <param name="appModel">Shared Core state: section-master tri-states, isJobStarted, avgSpeed.</param>
        /// <param name="getABLineHowManyPathsAway">Live reader for the active AB-line pass offset (was mf.ABLine.howManyPathsAway).</param>
        /// <param name="getCurveHowManyPathsAway">Live reader for the active curve pass offset (was mf.curve.howManyPathsAway).</param>
        public SectionService(
            CSection[] section,
            CTool tool,
            CTram tram,
            CModuleComm mc,
            CSound sounds,
            CTrack trk,
            PgnDispatcher pgn,
            ApplicationModel appModel,
            Func<int> getABLineHowManyPathsAway,
            Func<int> getCurveHowManyPathsAway)
        {
            this.section = section ?? throw new ArgumentNullException(nameof(section));
            this.tool = tool ?? throw new ArgumentNullException(nameof(tool));
            this.tram = tram ?? throw new ArgumentNullException(nameof(tram));
            this.mc = mc ?? throw new ArgumentNullException(nameof(mc));
            this.sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
            this.trk = trk ?? throw new ArgumentNullException(nameof(trk));
            this.pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
            this.appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));
            this.getABLineHowManyPathsAway = getABLineHowManyPathsAway
                ?? throw new ArgumentNullException(nameof(getABLineHowManyPathsAway));
            this.getCurveHowManyPathsAway = getCurveHowManyPathsAway
                ?? throw new ArgumentNullException(nameof(getCurveHowManyPathsAway));
        }

        /// <summary>
        /// Assembles the section/machine PGN payload bytes from the live section coverage state.
        /// Behavior FROZEN — byte-for-byte identical to the net48 Sections.Designer.cs original for BOTH
        /// the "sections-not-zones" mode (1–16 unique sections packed into p_254/p_239/p_229 bytes
        /// sc1to8/sc9to16) and the "zones" mode (up to 64 same-width sections packed across p_229
        /// bytes 5..12). The tool L/R speeds, the average speed and the tramline byte are always set.
        /// PUBLIC — invoked by RenderCoordinator after its oglBack pixel scan updates
        /// <c>section[j].isSectionOn</c>, and per the original ordering by the position pipeline.
        /// The only edits versus the original are the collaborator swaps: <c>mf.section</c>→<c>section</c>,
        /// <c>mf.tool</c>→<c>tool</c>, <c>mf.tram</c>→<c>tram</c>, the shared PGN objects
        /// <c>p_254/p_239/p_229</c>→<c>pgn.p_254/pgn.p_239/pgn.p_229</c>, and <c>mf.avgSpeed</c>→
        /// <c>appModel.avgSpeed</c>.
        /// </summary>
        public void BuildMachineByte()
        {
            if (tool.isSectionsNotZones)
            {
                pgn.p_254.pgn[pgn.p_254.sc1to8] = 0;
                pgn.p_254.pgn[pgn.p_254.sc9to16] = 0;

                int number = 0;
                for (int j = 0; j < 8; j++)
                {
                    if (section[j].isSectionOn)
                        number |= 1 << j;
                }
                pgn.p_254.pgn[pgn.p_254.sc1to8] = unchecked((byte)number);
                number = 0;

                for (int j = 8; j < 16; j++)
                {
                    if (section[j].isSectionOn)
                        number |= 1 << (j - 8);
                }
                pgn.p_254.pgn[pgn.p_254.sc9to16] = unchecked((byte)number);

                //machine pgn
                pgn.p_239.pgn[pgn.p_239.sc1to8] = pgn.p_254.pgn[pgn.p_254.sc1to8];
                pgn.p_239.pgn[pgn.p_239.sc9to16] = pgn.p_254.pgn[pgn.p_254.sc9to16];
                pgn.p_229.pgn[pgn.p_229.sc1to8] = pgn.p_254.pgn[pgn.p_254.sc1to8];
                pgn.p_229.pgn[pgn.p_229.sc9to16] = pgn.p_254.pgn[pgn.p_254.sc9to16];
                pgn.p_229.pgn[pgn.p_229.toolLSpeed] = unchecked((byte)(tool.farLeftSpeed * 10));
                pgn.p_229.pgn[pgn.p_229.toolRSpeed] = unchecked((byte)(tool.farRightSpeed * 10));
            }
            else
            {
                //zero all the bytes - set only if on
                for (int i = 5; i < 13; i++)
                {
                    pgn.p_229.pgn[i] = 0;
                }

                int number = 0;
                for (int k = 0; k < 8; k++)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        if (section[j + k * 8].isSectionOn)
                            number |= 1 << j;
                    }
                    pgn.p_229.pgn[5 + k] = unchecked((byte)number);
                    number = 0;
                }

                //tool speed to calc ramp
                pgn.p_229.pgn[pgn.p_229.toolLSpeed] = unchecked((byte)(tool.farLeftSpeed * 10));
                pgn.p_229.pgn[pgn.p_229.toolRSpeed] = unchecked((byte)(tool.farRightSpeed * 10));

                pgn.p_239.pgn[pgn.p_239.sc1to8] = pgn.p_229.pgn[pgn.p_229.sc1to8];
                pgn.p_239.pgn[pgn.p_239.sc9to16] = pgn.p_229.pgn[pgn.p_229.sc9to16];

                pgn.p_254.pgn[pgn.p_254.sc1to8] = pgn.p_229.pgn[pgn.p_229.sc1to8];
                pgn.p_254.pgn[pgn.p_254.sc9to16] = pgn.p_229.pgn[pgn.p_229.sc9to16];
            }

            pgn.p_239.pgn[pgn.p_239.speed] = unchecked((byte)(appModel.avgSpeed * 10));
            pgn.p_239.pgn[pgn.p_239.tram] = unchecked((byte)tram.controlByte);
        }

        /// <summary>
        /// Sets each section's left/right position from the persisted per-section boundary settings plus
        /// the tool offset, for the "sections-not-zones" mode. Behavior FROZEN — identical to the net48
        /// original (only <c>Properties.ToolSettings.Default</c> is referenced via the in-scope
        /// <c>AgOpenGPS.Properties</c> using; <c>mf.section</c>→<c>section</c>, <c>mf.tool</c>→<c>tool</c>).
        /// </summary>
        public void SectionSetPosition()
        {
            if (tool.isSectionsNotZones)
            {
                section[0].positionLeft = (double)ToolSettings.Default.setSection_position1 + ToolSettings.Default.setVehicle_toolOffset;
                section[0].positionRight = (double)ToolSettings.Default.setSection_position2 + ToolSettings.Default.setVehicle_toolOffset;

                section[1].positionLeft = (double)ToolSettings.Default.setSection_position2 + ToolSettings.Default.setVehicle_toolOffset;
                section[1].positionRight = (double)ToolSettings.Default.setSection_position3 + ToolSettings.Default.setVehicle_toolOffset;

                section[2].positionLeft = (double)ToolSettings.Default.setSection_position3 + ToolSettings.Default.setVehicle_toolOffset;
                section[2].positionRight = (double)ToolSettings.Default.setSection_position4 + ToolSettings.Default.setVehicle_toolOffset;

                section[3].positionLeft = (double)ToolSettings.Default.setSection_position4 + ToolSettings.Default.setVehicle_toolOffset;
                section[3].positionRight = (double)ToolSettings.Default.setSection_position5 + ToolSettings.Default.setVehicle_toolOffset;

                section[4].positionLeft = (double)ToolSettings.Default.setSection_position5 + ToolSettings.Default.setVehicle_toolOffset;
                section[4].positionRight = (double)ToolSettings.Default.setSection_position6 + ToolSettings.Default.setVehicle_toolOffset;

                section[5].positionLeft = (double)ToolSettings.Default.setSection_position6 + ToolSettings.Default.setVehicle_toolOffset;
                section[5].positionRight = (double)ToolSettings.Default.setSection_position7 + ToolSettings.Default.setVehicle_toolOffset;

                section[6].positionLeft = (double)ToolSettings.Default.setSection_position7 + ToolSettings.Default.setVehicle_toolOffset;
                section[6].positionRight = (double)ToolSettings.Default.setSection_position8 + ToolSettings.Default.setVehicle_toolOffset;

                section[7].positionLeft = (double)ToolSettings.Default.setSection_position8 + ToolSettings.Default.setVehicle_toolOffset;
                section[7].positionRight = (double)ToolSettings.Default.setSection_position9 + ToolSettings.Default.setVehicle_toolOffset;

                section[8].positionLeft = (double)ToolSettings.Default.setSection_position9 + ToolSettings.Default.setVehicle_toolOffset;
                section[8].positionRight = (double)ToolSettings.Default.setSection_position10 + ToolSettings.Default.setVehicle_toolOffset;

                section[9].positionLeft = (double)ToolSettings.Default.setSection_position10 + ToolSettings.Default.setVehicle_toolOffset;
                section[9].positionRight = (double)ToolSettings.Default.setSection_position11 + ToolSettings.Default.setVehicle_toolOffset;

                section[10].positionLeft = (double)ToolSettings.Default.setSection_position11 + ToolSettings.Default.setVehicle_toolOffset;
                section[10].positionRight = (double)ToolSettings.Default.setSection_position12 + ToolSettings.Default.setVehicle_toolOffset;

                section[11].positionLeft = (double)ToolSettings.Default.setSection_position12 + ToolSettings.Default.setVehicle_toolOffset;
                section[11].positionRight = (double)ToolSettings.Default.setSection_position13 + ToolSettings.Default.setVehicle_toolOffset;

                section[12].positionLeft = (double)ToolSettings.Default.setSection_position13 + ToolSettings.Default.setVehicle_toolOffset;
                section[12].positionRight = (double)ToolSettings.Default.setSection_position14 + ToolSettings.Default.setVehicle_toolOffset;

                section[13].positionLeft = (double)ToolSettings.Default.setSection_position14 + ToolSettings.Default.setVehicle_toolOffset;
                section[13].positionRight = (double)ToolSettings.Default.setSection_position15 + ToolSettings.Default.setVehicle_toolOffset;

                section[14].positionLeft = (double)ToolSettings.Default.setSection_position15 + ToolSettings.Default.setVehicle_toolOffset;
                section[14].positionRight = (double)ToolSettings.Default.setSection_position16 + ToolSettings.Default.setVehicle_toolOffset;

                section[15].positionLeft = (double)ToolSettings.Default.setSection_position16 + ToolSettings.Default.setVehicle_toolOffset;
                section[15].positionRight = (double)ToolSettings.Default.setSection_position17 + ToolSettings.Default.setVehicle_toolOffset;
            }
        }

        /// <summary>
        /// Calculates each section's width and back-buffer pixel coordinates, plus the overall tool width,
        /// far-left/right positions, and the tool's pixel position/width, for the "sections-not-zones"
        /// mode. Behavior FROZEN — the <c>MidpointRounding.AwayFromZero</c> rounding and the 250-pixel
        /// origin offset are preserved exactly so the RenderCoordinator oglBack <c>glReadPixels</c> scan
        /// reads identical pixel coordinates. Only collaborator swaps applied (<c>mf</c>→injected).
        /// </summary>
        public void SectionCalcWidths()
        {
            if (tool.isSectionsNotZones)
            {
                for (int j = 0; j < MAXSECTIONS; j++)
                {
                    section[j].sectionWidth = (section[j].positionRight - section[j].positionLeft);
                    section[j].rpSectionPosition = 250 + (int)(Math.Round(section[j].positionLeft * 10, 0, MidpointRounding.AwayFromZero));
                    section[j].rpSectionWidth = (int)(Math.Round(section[j].sectionWidth * 10, 0, MidpointRounding.AwayFromZero));
                }

                //calculate tool width based on extreme right and left values
                tool.width = (section[tool.numOfSections - 1].positionRight) - (section[0].positionLeft);

                //left and right tool position
                tool.farLeftPosition = section[0].positionLeft;
                tool.farRightPosition = section[tool.numOfSections - 1].positionRight;

                //find the right side pixel position
                tool.rpXPosition = 250 + (int)(Math.Round(tool.farLeftPosition * 10, 0, MidpointRounding.AwayFromZero));
                tool.rpWidth = (int)(Math.Round(tool.width * 10, 0, MidpointRounding.AwayFromZero));
            }
        }

        /// <summary>
        /// Calculates evenly-spaced (multi) section positions/widths and back-buffer pixel coordinates
        /// from the configured multi-section width and tool offset. Behavior FROZEN — the
        /// <c>MidpointRounding.AwayFromZero</c> rounding and 250-pixel origin offset are preserved exactly.
        /// Only collaborator swaps applied (<c>mf</c>→injected; <c>Properties.ToolSettings.Default</c> via
        /// the in-scope <c>AgOpenGPS.Properties</c> using).
        /// </summary>
        public void SectionCalcMulti()
        {
            double leftside = tool.width / -2.0;
            double defaultSectionWidth = ToolSettings.Default.setTool_sectionWidthMulti;
            double offset = ToolSettings.Default.setVehicle_toolOffset;
            section[0].positionLeft = leftside + offset;

            for (int i = 0; i < tool.numOfSections - 1; i++)
            {
                leftside += defaultSectionWidth;

                section[i].positionRight = leftside + offset;
                section[i + 1].positionLeft = leftside + offset;
                section[i].sectionWidth = defaultSectionWidth;
                section[i].rpSectionPosition = 250 + (int)(Math.Round(section[i].positionLeft * 10, 0, MidpointRounding.AwayFromZero));
                section[i].rpSectionWidth = (int)(Math.Round(section[i].sectionWidth * 10, 0, MidpointRounding.AwayFromZero));
            }

            leftside += defaultSectionWidth;
            section[tool.numOfSections - 1].positionRight = leftside + offset;
            section[tool.numOfSections - 1].sectionWidth = defaultSectionWidth;
            section[tool.numOfSections - 1].rpSectionPosition = 250 + (int)(Math.Round(section[tool.numOfSections - 1].positionLeft * 10, 0, MidpointRounding.AwayFromZero));
            section[tool.numOfSections - 1].rpSectionWidth = (int)(Math.Round(section[tool.numOfSections - 1].sectionWidth * 10, 0, MidpointRounding.AwayFromZero));

            //calculate tool width based on extreme right and left values
            tool.width = (section[tool.numOfSections - 1].positionRight) - (section[0].positionLeft);

            //left and right tool position
            tool.farLeftPosition = section[0].positionLeft;
            tool.farRightPosition = section[tool.numOfSections - 1].positionRight;

            //find the right side pixel position
            tool.rpXPosition = 250 + (int)(Math.Round(tool.farLeftPosition * 10, 0, MidpointRounding.AwayFromZero));
            tool.rpWidth = (int)(Math.Round(tool.width * 10, 0, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Entry point for remote (hardware) section switches, invoked from the PGN 0xEA (234) receive
        /// path (wired by the composition root to <c>PgnDispatcher.OnRemoteSwitchChanged</c>). Behavior
        /// FROZEN — the <c>isJobStarted</c> safety gate (now read from <c>appModel</c>) and the
        /// button-vs-switch selection on <c>(mc.ss[mc.swMain] &amp; (1&lt;&lt;2)) == 0</c> are preserved
        /// exactly. PUBLIC — it is the remote-switch hook.
        /// </summary>
        public void DoRemoteSwitches()
        {
            //MTZ8302 Feb 2020 and hagre 2024
            if (appModel.isJobStarted)
            {
                //check if third bit in the pgn234 received Main-Byte is set to indicate the use of buttons (0) or switches (1) in the SC hardware
                if ((mc.ss[mc.swMain] & (1 << 2)) == 0) // Button hardware by MTZ8302 Feb 2020 (3dr bit - check)
                {
                    HandleButtonHardware();
                }
                else  // Switch hardware by hagre 05 2024
                {
                    HandleSwitchHardware();
                }
            }
        }

        /// <summary>
        /// MTZ8302 (Feb 2020) momentary-button section hardware handler. Behavior FROZEN — extracted
        /// verbatim from Sections.Designer.cs with the collaborator swaps <c>mf.mc</c>→<c>mc</c>,
        /// <c>mf.section</c>→<c>section</c>, <c>mf.tool</c>→<c>tool</c>, <c>autoBtnState</c>→
        /// <c>appModel.autoBtnState</c>, and the WinForms <c>btnSectionMasterAuto.PerformClick()</c>→
        /// <see cref="PerformSectionMasterAuto"/> (the domain equivalent that also raises the master events).
        /// </summary>
        private void HandleButtonHardware()
        {
            //MainSW was used
            if (mc.ss[mc.swMain] != mc.ssP[mc.swMain])
            {
                //Main SW pressed
                if ((mc.ss[mc.swMain] & 1) == 1)
                {
                    //set butto off and then press it = ON
                    appModel.autoBtnState = btnStates.Off;
                    PerformSectionMasterAuto();
                } // if Main SW ON

                //if Main SW in Arduino is pressed OFF
                if ((mc.ss[mc.swMain] & 2) == 2)
                {
                    //set button on and then press it = OFF
                    appModel.autoBtnState = btnStates.Auto;
                    PerformSectionMasterAuto();
                } // if Main SW OFF

                mc.ssP[mc.swMain] = mc.ss[mc.swMain];
            }  //Main or shpList SW

            if (tool.isSectionsNotZones)  // NO Zones
            {
                if (mc.ss[mc.swOnGr0] != 0)
                {
                    // ON Signal from Arduino 
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mc.ss[mc.swOnGr0] & (1 << i)) == (1 << i)) && (tool.numOfSections > i))
                        {
                            if (section[i].sectionBtnState != btnStates.Auto)
                            {
                                section[i].sectionBtnState = btnStates.Auto;
                            }
                            PerformSectionClick(i);
                        }
                    }
                    mc.ssP[mc.swOnGr0] = mc.ss[mc.swOnGr0];

                } //if swONLo != 0 
                else
                {
                    if (mc.ssP[mc.swOnGr0] != 0)
                    {
                        mc.ssP[mc.swOnGr0] = 0;
                    }
                }


                if (mc.ss[mc.swOnGr1] != 0)
                {
                    // sections ON signal from Arduino  
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mc.ss[mc.swOnGr1] & (1 << i)) == (1 << i)) && (tool.numOfSections > i + 8))
                        {
                            if (section[i + 8].sectionBtnState != btnStates.Auto)
                            {
                                section[i + 8].sectionBtnState = btnStates.Auto;
                            }
                            PerformSectionClick(i + 8);
                        }
                    }
                    mc.ssP[mc.swOnGr1] = mc.ss[mc.swOnGr1];

                } //if swONHi != 0   
                else
                {
                    if (mc.ssP[mc.swOnGr1] != 0)
                    {
                        mc.ssP[mc.swOnGr1] = 0;
                    }
                }

                // Switches have changed
                if (mc.ss[mc.swOffGr0] != mc.ssP[mc.swOffGr0])
                {
                    //if Main = Auto then change section to Auto if Off signal from Arduino stopped
                    if (appModel.autoBtnState == btnStates.Auto)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            if (((mc.ssP[mc.swOffGr0] & (1 << i)) == (1 << i)) && ((mc.ss[mc.swOffGr0] & (1 << i)) != (1 << i)) && (section[i].sectionBtnState == btnStates.Off))
                            {
                                PerformSectionClick(i);
                            }
                        }
                    }
                    mc.ssP[mc.swOffGr0] = mc.ss[mc.swOffGr0];
                }

                if (mc.ss[mc.swOffGr1] != mc.ssP[mc.swOffGr1])
                {
                    //if Main = Auto then change section to Auto if Off signal from Arduino stopped
                    if (appModel.autoBtnState == btnStates.Auto)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            if (((mc.ssP[mc.swOffGr1] & (1 << i)) == (1 << i)) && ((mc.ss[mc.swOffGr1] & (1 << i)) != (1 << i)) && (section[i + 8].sectionBtnState == btnStates.Off))
                            {
                                PerformSectionClick(i + 8);
                            }
                        }
                    }
                    mc.ssP[mc.swOffGr1] = mc.ss[mc.swOffGr1];
                }

                // OFF Signal from Arduino
                if (mc.ss[mc.swOffGr0] != 0)
                {
                    //if section SW in Arduino is switched to OFF; check always, if switch is locked to off GUI should not change
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mc.ss[mc.swOffGr0] & (1 << i)) == (1 << i)) && (section[i].sectionBtnState != btnStates.Off))
                        {
                            section[i].sectionBtnState = btnStates.On;
                            PerformSectionClick(i);
                        }
                    }

                } // if swOFFLo !=0

                if (mc.ss[mc.swOffGr1] != 0)
                {
                    //if section SW in Arduino is switched to OFF; check always, if switch is locked to off GUI should not change
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mc.ss[mc.swOffGr1] & (1 << i)) == (1 << i)) && (section[i + 8].sectionBtnState != btnStates.Off))
                        {
                            section[i + 8].sectionBtnState = btnStates.On;
                            PerformSectionClick(i + 8);
                        }
                    }
                } // if swOFFHi !=0
            }
            else// zones to on
            {
                if (mc.ss[mc.swOnGr0] != 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((tool.zoneRanges[i + 1] > 0) && ((mc.ss[mc.swOnGr0] & (1 << i)) == (1 << i)))
                        {
                            if (section[tool.zoneRanges[i + 1] - 1].sectionBtnState != btnStates.Auto)
                            {
                                section[tool.zoneRanges[i + 1] - 1].sectionBtnState = btnStates.Auto;
                                PerformZoneClick(i);
                            }
                        }
                    }
                    mc.ssP[mc.swOnGr0] = mc.ss[mc.swOnGr0];
                }
                else
                {
                    if (mc.ssP[mc.swOnGr0] != 0)
                    {
                        mc.ssP[mc.swOnGr0] = 0;
                    }
                }

                // zones to auto
                if (mc.ss[mc.swOffGr0] != mc.ssP[mc.swOffGr0])
                {
                    if (appModel.autoBtnState == btnStates.Auto)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            if ((tool.zoneRanges[i + 1] > 0) && ((mc.ssP[mc.swOffGr0] & (1 << i)) == (1 << i)) && ((mc.ss[mc.swOffGr0] & (1 << i)) != (1 << i)) && (section[tool.zoneRanges[i + 1] - 1].sectionBtnState == btnStates.Off))
                            {
                                PerformZoneClick(i);
                            }
                        }
                    }
                    mc.ssP[mc.swOffGr0] = mc.ss[mc.swOffGr0];
                }

                // zones to off
                if (mc.ss[mc.swOffGr0] != 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((tool.zoneRanges[i + 1] > 0) && ((mc.ss[mc.swOffGr0] & (1 << i)) == (1 << i)) && (section[tool.zoneRanges[i + 1] - 1].sectionBtnState != btnStates.Off))
                        {
                            section[tool.zoneRanges[i + 1] - 1].sectionBtnState = btnStates.On;
                            PerformZoneClick(i);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// hagre (May 2024) latching-switch section hardware handler. Behavior FROZEN — extracted
        /// verbatim from Sections.Designer.cs with the same collaborator swaps as
        /// <see cref="HandleButtonHardware"/> (<c>mf</c>→injected, <c>autoBtnState</c>→
        /// <c>appModel.autoBtnState</c>, <c>btnSectionMasterAuto.PerformClick()</c>→
        /// <see cref="PerformSectionMasterAuto"/>).
        /// </summary>
        private void HandleSwitchHardware()
        {
            //MainSW Byte is AUTO
            if ((appModel.autoBtnState != btnStates.Auto) && ((mc.ss[mc.swMain] & 1) == 1))
            {
                //set button off and then press it = ON
                appModel.autoBtnState = btnStates.Off;
                PerformSectionMasterAuto();
            }
            //MainSW Byte is OFF
            else if ((appModel.autoBtnState != btnStates.Off) && ((mc.ss[mc.swMain] & 2) == 2))
            {
                //set button on and then press it = OFF
                appModel.autoBtnState = btnStates.Auto;
                PerformSectionMasterAuto();
            }

            if (tool.isSectionsNotZones) // NO Zones
            {
                if (mc.ss[mc.swAutoGr0] != 0)
                {
                    // AUTO Signal from Arduino Gr0
                    for (int i = 0; i < 8; i++)
                    {
                        if ((section[i].sectionBtnState != btnStates.Auto) && ((mc.ss[mc.swAutoGr0] & (1 << i)) == (1 << i)) && (tool.numOfSections > i) && (mc.ss[mc.swNumSections] > i))
                        {
                            section[i].sectionBtnState = btnStates.Off;
                            PerformSectionClick(i);
                        }
                    }
                }
                if (mc.ss[mc.swAutoGr1] != 0)
                {
                    // AUTO Signal from Arduino Gr1
                    for (int i = 0; i < 8; i++)
                    {
                        if ((section[i + 8].sectionBtnState != btnStates.Auto) && ((mc.ss[mc.swAutoGr1] & (1 << i)) == (1 << i)) && (tool.numOfSections > i + 8) && (mc.ss[mc.swNumSections] > i + 8))
                        {
                            PerformSectionClick(i + 8);
                        }
                    }
                }

                if (mc.ss[mc.swOnGr0] != 0)
                {
                    // ON Signal from Arduino Gr0
                    for (int i = 0; i < 8; i++)
                    {
                        if (((section[i].sectionBtnState != btnStates.On) && (mc.ss[mc.swOnGr0] & (1 << i)) == (1 << i)) && (tool.numOfSections > i) && (mc.ss[mc.swNumSections] > i))
                        {
                            section[i].sectionBtnState = btnStates.Auto;
                            PerformSectionClick(i);
                        }
                    }
                }
                if (mc.ss[mc.swOnGr1] != 0)
                {
                    // ON Signal from Arduino Gr1
                    for (int i = 0; i < 8; i++)
                    {
                        if ((section[i + 8].sectionBtnState != btnStates.On) && ((mc.ss[mc.swOnGr1] & (1 << i)) == (1 << i)) && (tool.numOfSections > i + 8) && (mc.ss[mc.swNumSections] > i + 8))
                        {
                            section[i + 8].sectionBtnState = btnStates.Auto;
                            PerformSectionClick(i + 8);
                        }
                    }
                }


                if (mc.ss[mc.swOffGr0] != 0)
                {
                    // OFF Signal from Arduino Gr0
                    for (int i = 0; i < 8; i++)
                    {
                        if ((section[i].sectionBtnState != btnStates.Off) && ((mc.ss[mc.swOffGr0] & (1 << i)) == (1 << i)) && (tool.numOfSections > i))  // !check mc.ss[tool.numOfSections] => to be on the save side by switching eveything off 
                        {
                            section[i].sectionBtnState = btnStates.On;
                            PerformSectionClick(i);
                        }
                    }
                }
                if (mc.ss[mc.swOffGr1] != 0)
                {
                    // OFF Signal from Arduino Gr1
                    for (int i = 0; i < 8; i++)
                    {
                        if ((section[i + 8].sectionBtnState != btnStates.Off) && ((mc.ss[mc.swOffGr1] & (1 << i)) == (1 << i)) && (tool.numOfSections > i + 8)) // !check mc.ss[tool.numOfSections] => to be on the save side by switching eveything off
                        {
                            PerformSectionClick(i + 8);
                        }
                    }
                }
            }
            else
            {
                // zones to auto
                if (mc.ss[mc.swAutoGr0] != 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((tool.zoneRanges[i + 1] > 0) && ((mc.ss[mc.swAutoGr0] & (1 << i)) == (1 << i)) && ((mc.ss[mc.swOnGr0] & (1 << i)) != (1 << i)) && ((mc.ss[mc.swOffGr0] & (1 << i)) != (1 << i)) && (section[tool.zoneRanges[i + 1] - 1].sectionBtnState != btnStates.Auto))
                        {
                            section[tool.zoneRanges[i + 1] - 1].sectionBtnState = btnStates.Off;
                            PerformZoneClick(i);
                        }
                    }

                }

                // zones to on
                if (mc.ss[mc.swOnGr0] != 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((tool.zoneRanges[i + 1] > 0) && ((mc.ss[mc.swOnGr0] & (1 << i)) == (1 << i)) && ((mc.ss[mc.swOffGr0] & (1 << i)) != (1 << i)) && (section[tool.zoneRanges[i + 1] - 1].sectionBtnState != btnStates.On))
                        {
                            section[tool.zoneRanges[i + 1] - 1].sectionBtnState = btnStates.Auto;
                            PerformZoneClick(i);

                        }
                    }
                }

                // zones to off
                if (mc.ss[mc.swOffGr0] != 0)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((tool.zoneRanges[i + 1] > 0) && ((mc.ss[mc.swOffGr0] & (1 << i)) == (1 << i)) && (section[tool.zoneRanges[i + 1] - 1].sectionBtnState != btnStates.Off))
                        {
                            section[tool.zoneRanges[i + 1] - 1].sectionBtnState = btnStates.On;
                            PerformZoneClick(i);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Domain logic of the former <c>btnSectionMasterManual_Click</c> (the section-master MANUAL
        /// toggle). Behavior FROZEN — the order, the audio-cue gating, the manual Off↔On toggle, the
        /// turn-off-Auto side effect, the worked-track mark, and the "all sections/zones to the new
        /// state" propagation are preserved exactly. The WinForms Button image updates are replaced by
        /// <see cref="OnManualStateChanged"/> / <see cref="OnMasterStateChanged"/> events the view binds.
        /// PUBLIC — the Avalonia view's manual-master command invokes this, then updates its visuals.
        /// </summary>
        public void PerformSectionMasterManual()
        {
            if (sounds.isSectionsSoundOn && (!mc.isSteerWorkSwitchEnabled || !mc.isSteerWorkSwitchManualSections))
                sounds.sndSectionOff.Play();

            //if Auto is on, turn it off
            appModel.autoBtnState = btnStates.Off;
            OnMasterStateChanged?.Invoke(appModel.autoBtnState);   // was btnSectionMasterAuto.Image = SectionMasterOff

            switch (appModel.manualBtnState)
            {
                case btnStates.Off:
                    appModel.manualBtnState = btnStates.On;
                    OnManualStateChanged?.Invoke(appModel.manualBtnState);   // was btnSectionMasterManual.Image = ManualOn

                    //add current track when it doesn't exist in the worked track list
                    MarkAsWorkedTrack();

                    break;

                case btnStates.On:
                    appModel.manualBtnState = btnStates.Off;
                    OnManualStateChanged?.Invoke(appModel.manualBtnState);   // was btnSectionMasterManual.Image = ManualOff
                    break;
            }

            //go set the butons and section states
            if (tool.isSectionsNotZones)
                AllSectionsToState(appModel.manualBtnState);
            else
                AllZonesToState(appModel.manualBtnState);
        }

        /// <summary>
        /// Domain logic of the former <c>btnSectionMasterAuto_Click</c> (the section-master AUTO toggle).
        /// Behavior FROZEN — the order, the turn-off-Manual side effect, the Auto Off↔Auto toggle, the
        /// audio-cue gating, the worked-track mark, and the "all sections/zones to the new state"
        /// propagation are preserved exactly. The WinForms Button image updates are replaced by
        /// <see cref="OnMasterStateChanged"/> / <see cref="OnManualStateChanged"/> events the view binds.
        /// PUBLIC — invoked by the Avalonia view's auto-master command AND internally by the remote-switch
        /// handlers (replacing the net48 <c>btnSectionMasterAuto.PerformClick()</c> call).
        /// </summary>
        public void PerformSectionMasterAuto()
        {
            //turn off manual if on
            appModel.manualBtnState = btnStates.Off;
            OnManualStateChanged?.Invoke(appModel.manualBtnState);   // was btnSectionMasterManual.Image = ManualOff

            switch (appModel.autoBtnState)
            {

                case btnStates.Off:

                    appModel.autoBtnState = btnStates.Auto;
                    OnMasterStateChanged?.Invoke(appModel.autoBtnState);   // was btnSectionMasterAuto.Image = SectionMasterOn
                    if (sounds.isSectionsSoundOn && (!mc.isSteerWorkSwitchEnabled || !mc.isSteerWorkSwitchManualSections))
                        sounds.sndSectionOn.Play();

                    //add current track when it doesn't exist in the worked track list
                    MarkAsWorkedTrack();

                    break;

                case btnStates.Auto:

                    appModel.autoBtnState = btnStates.Off;
                    OnMasterStateChanged?.Invoke(appModel.autoBtnState);   // was btnSectionMasterAuto.Image = SectionMasterOff
                    if (sounds.isSectionsSoundOn && (!mc.isSteerWorkSwitchEnabled || !mc.isSteerWorkSwitchManualSections))
                        sounds.sndSectionOn.Play();
                    break;
            }

            //go set the butons and section states
            if (tool.isSectionsNotZones)
                AllSectionsToState(appModel.autoBtnState);
            else
                AllZonesToState(appModel.autoBtnState);
        }

        /// <summary>
        /// Sets all 16 individual sections to <paramref name="state"/> (the state portion of the former
        /// <c>AllSectionsAndButtonsToState</c>). Each per-section visual update is delegated to the view
        /// via <see cref="OnSectionStateChanged"/>. Behavior FROZEN (loops sections 1..16 → index 0..15).
        /// </summary>
        public void AllSectionsToState(btnStates state)
        {
            for (int i = 1; i <= 16; i++)
            {
                section[i - 1].sectionBtnState = state;
                OnSectionStateChanged?.Invoke(i - 1, state);   // was IndividualSectionAndButonToState's SetColors(btn, state)
            }
        }

        /// <summary>
        /// Sets all configured zones to <paramref name="state"/> (the state portion of the former
        /// <c>AllZonesAndButtonsToState</c>). Each per-zone visual update is delegated to the view via
        /// <see cref="OnZoneStateChanged"/>. Behavior FROZEN — the zone-range bounds and the early return
        /// when no zones are configured are preserved exactly.
        /// </summary>
        public void AllZonesToState(btnStates state)
        {
            if (tool.zoneRanges[0] == 0) return;
            if (tool.zoneRanges[1] != 0) ZoneRangeToState(state, 0, tool.zoneRanges[1], 1);

            for (int i = 2; i <= 8; i++)
            {
                if (tool.zoneRanges[i] != 0)
                {
                    ZoneRangeToState(state, tool.zoneRanges[i - 1], tool.zoneRanges[i], i);
                }
            }
        }

        /// <summary>
        /// Applies <paramref name="state"/> to every section in the half-open zone range
        /// [<paramref name="sectionStartNumber"/>, <paramref name="sectionEndNumber"/>) and raises
        /// <see cref="OnZoneStateChanged"/> for the view (the state portion of the former
        /// <c>IndividualZoneAndButtonToState</c>; the SetColors visual is now the view's job).
        /// </summary>
        /// <param name="state">The tri-state to apply.</param>
        /// <param name="sectionStartNumber">Inclusive first section index of the zone.</param>
        /// <param name="sectionEndNumber">Exclusive last section index of the zone.</param>
        /// <param name="zoneButtonIndex">One-based zone button index (1..8) for the view event.</param>
        private void ZoneRangeToState(btnStates state, int sectionStartNumber, int sectionEndNumber, int zoneButtonIndex)
        {
            for (int i = sectionStartNumber; i < sectionEndNumber; i++)
            {
                section[i].sectionBtnState = state;
            }
            OnZoneStateChanged?.Invoke(zoneButtonIndex, state);   // was SetColors(btn, state)
        }

        /// <summary>
        /// Cycles a section/zone tri-state Off → Auto → On → Off. Behavior FROZEN (verbatim from the
        /// former <c>GetNextState</c>).
        /// </summary>
        private btnStates GetNextState(btnStates state)
        {
            if (state == btnStates.Off) return btnStates.Auto;
            else if (state == btnStates.Auto) return btnStates.On;
            else if (state == btnStates.On) return btnStates.Off;
            return btnStates.Off;
        }

        /// <summary>
        /// Domain equivalent of the former <c>PerformSectionClick</c> → <c>btnSectionXMan_Click</c> chain:
        /// advances section <paramref name="Btn"/>'s tri-state by one step and raises
        /// <see cref="OnSectionStateChanged"/> for the view. Behavior FROZEN — the WinForms
        /// <c>Button.PerformClick()</c> indirection and the Button color update are replaced by the direct
        /// state cycle and the event.
        /// </summary>
        /// <param name="Btn">Zero-based section index (the former button was btnSection{Btn+1}Man).</param>
        private void PerformSectionClick(int Btn)
        {
            btnStates state = GetNextState(section[Btn].sectionBtnState);
            section[Btn].sectionBtnState = state;
            OnSectionStateChanged?.Invoke(Btn, state);   // was IndividualSectionAndButonToState's SetColors(btn, state)
        }

        /// <summary>
        /// Domain equivalent of the former <c>PerformZoneClick</c> → <c>btnZoneX_Click</c> chain: advances
        /// the tri-state of the zone whose button index is <paramref name="Btn"/>+1, applying the new state
        /// across the zone's section range and raising <see cref="OnZoneStateChanged"/> for the view.
        /// Behavior FROZEN — the zone-range bounds (zone 1 starts at section 0) match the original exactly.
        /// </summary>
        /// <param name="Btn">Zero-based zone index (the former button was btnZone{Btn+1}).</param>
        private void PerformZoneClick(int Btn)
        {
            int zoneIndex = Btn + 1;
            btnStates state = GetNextState(section[tool.zoneRanges[zoneIndex] - 1].sectionBtnState);
            if (zoneIndex == 1)
            {
                ZoneRangeToState(state, 0, tool.zoneRanges[1], zoneIndex);
            }
            else
            {
                ZoneRangeToState(state, tool.zoneRanges[zoneIndex - 1], tool.zoneRanges[zoneIndex], zoneIndex);
            }
        }

        /// <summary>
        /// Adds the currently-active guidance pass to the selected track's worked-track set, when a track
        /// is selected. Behavior FROZEN — verbatim from the former <c>MarkAsWorkedTrack</c> except that the
        /// active pass offset, previously read from <c>mf.ABLine.howManyPathsAway</c> /
        /// <c>mf.curve.howManyPathsAway</c> (types not collaborators of this service), is now read through
        /// the injected live accessors.
        /// </summary>
        private void MarkAsWorkedTrack()
        {
            // return if there was a track selected
            if (trk.idx < 0) return;

            var track = trk.gArr[trk.idx];

            if (track.mode == TrackMode.AB)
            {
                track.workedTracks.Add(getABLineHowManyPathsAway());
            }
            else if (track.mode == TrackMode.Curve)
            {
                track.workedTracks.Add(getCurveHowManyPathsAway());
            }
        }
    }
}
