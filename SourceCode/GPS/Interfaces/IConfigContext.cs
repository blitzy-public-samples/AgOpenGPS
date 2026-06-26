// [XPLAT] migrated from net48/WinForms FormConfig (mf god-object decoupling) — see TRANSITION_MAP.md
// Shared GPS interfaces that replace the former direct FormGPS (mf) back-references reached by the
// WinForms FormConfig partial classes (ConfigMenu/ConfigVehicle/ConfigTool/ConfigData/ConfigModule).
// These contracts surface ONLY the members that the configuration dialog actually consumed from mf;
// no new domain behavior is introduced. They are implemented by the application controller / extracted
// services that stand in for FormGPS, exactly as the Agent Action Plan's MVVM/Presenter scaffold and
// IPlatformServices abstraction prescribe. Keeping them in one shared file lets every re-platformed
// settings/config view bind to the same cohesive context without taking a hard dependency on FormGPS.
using AgOpenGPS.Core;          // btnStates
using AgOpenGPS.Core.Models;   // VehicleConfig, VehicleType (portable Core domain model)
using Avalonia.Controls;       // Window (owner for hosted child dialogs)

namespace AgOpenGPS.Interfaces
{
    /// <summary>
    /// [XPLAT] Cohesive configuration context that replaces the broad <c>mf</c> (FormGPS) surface the
    /// WinForms <c>FormConfig</c> reached into. Every member here maps 1:1 to a member or method the
    /// dialog previously accessed through <c>mf</c> — unit-conversion factors, on/off display flags,
    /// module-comm switch flags, sound flags, the section-master button orchestration, the machine
    /// configuration PGN send, and the host-driven population hooks (vehicle textures / summary panel)
    /// that the established sibling views defer to the extracted-services layer.
    /// </summary>
    public interface IConfigContext
    {
        // ---- Units / measurement system (mf.isMetric and the unit-conversion factors) ----
        bool IsMetric { get; set; }
        double M2InchOrCm { get; }
        double InchOrCm2m { get; }
        double Cm2CmOrIn { get; }
        double M2FtOrM { get; }
        double FtOrMtoM { get; }
        string UnitsInCm { get; }
        string UnitsFtM { get; }

        // ---- Display on/off flags (mf.is*On) ----
        bool IsTextureOn { get; set; }
        bool IsGridOn { get; set; }
        bool IsSpeedoOn { get; set; }
        bool IsSideGuideLines { get; set; }
        bool IsDrawPolygons { get; set; }
        bool IsKeyboardOn { get; set; }
        bool IsBrightnessOn { get; set; }
        bool IsSvennArrowOn { get; set; }
        bool IsLogElevation { get; set; }
        bool IsDirectionMarkers { get; set; }
        bool IsSectionLinesOn { get; set; }
        bool IsLineSmooth { get; set; }
        bool IsHeadlandDistanceOn { get; set; }
        bool IsPureDisplayOn { get; set; }
        bool IsLightbarOn { get; set; }

        // ---- Misc FormGPS state surfaced to the dialog ----
        bool IsJobStarted { get; }
        bool IsFirstHeadingSet { get; set; }
        bool IsRtkAlarmOn { get; set; }
        bool IsRtkKillAutosteer { get; set; }
        bool IsAutoStartAgIO { get; set; }
        string HeadingFromSource { get; set; }
        double DualReverseDetectionDistance { get; set; }
        double HeadingTrueDualOffset { get; set; }
        int MaxSections { get; }
        btnStates AutoBtnState { get; set; }
        btnStates ManualBtnState { get; set; }

        // ---- Module-comm work/steer switch flags (mf.mc.*) ----
        bool IsWorkSwitchEnabled { get; set; }
        bool IsSteerWorkSwitchEnabled { get; set; }
        bool IsWorkSwitchManualSections { get; set; }
        bool IsSteerWorkSwitchManualSections { get; set; }
        bool IsWorkSwitchActiveLow { get; set; }
        bool IsRemoteWorkSystemOn { get; set; }

        // ---- Sound flags (mf.sounds.*) ----
        bool IsTurnSoundOn { get; set; }
        bool IsSteerSoundOn { get; set; }
        bool IsSectionsSoundOn { get; set; }
        bool IsHydLiftSoundOn { get; set; }

        // ---- Operations (mf methods) ----
        void SaveFormGPSWindowSettings();
        void LoadSettings();

        // Section master button orchestration (mf.btnSectionMasterAuto/Manual + state machine)
        void SectionMasterAutoPerformClick();
        void SectionMasterManualPerformClick();
        void ResetSectionMasterButtons();
        void AllSectionsAndButtonsToState(btnStates state);
        void AllZonesAndButtonsToState(btnStates state);
        void LineUpIndividualSectionBtns();
        void LineUpAllZoneButtons();
        void SectionSetPosition();
        void SectionCalcWidths();
        void SectionCalcMulti();

        // Machine / relay PGN dispatch (mf.p_238 + mf.SendPgnToLoop / mf.SendRelaySettingsToMachineModule)
        void SendRelaySettingsToMachineModule();
        void SendMachineConfigPgn(byte setting0, byte raiseTime, byte lowerTime, byte user1, byte user2, byte user3, byte user4);

        // Host-driven population deferred to the extracted-services layer (mf.VehicleTextures.* / configSummaryControl.UpdateSummary(mf))
        void RefreshVehicleTextures();
        void UpdateConfigSummary();

        // Boundary turn-line rebuild (mf.bnd.BuildTurnLines)
        void BuildTurnLines();

        // Right-panel button-order child dialog (mf → new FormButtonsRightPanel(mf).ShowDialog(mf))
        void ShowRightMenuOrderDialog(Window owner);
    }

    /// <summary>[XPLAT] Tramline state surfaced from <c>mf.tram</c> (CTram).</summary>
    public interface ITramState
    {
        double HalfWheelTrack { get; set; }
        double TramWidth { get; set; }
        void IsTramOuterOrInner();
    }

    /// <summary>[XPLAT] AB-line / guidance-line state surfaced from <c>mf.ABLine</c> (CABLine).</summary>
    public interface IAbLineState
    {
        int NumGuideLines { get; set; }
    }

    /// <summary>[XPLAT] You-turn guidance state surfaced from <c>mf.yt</c> (CYouTurn).</summary>
    public interface IGuidanceState
    {
        int UTurnSmoothing { get; set; }
        int YouTurnStartOffset { get; set; }
        double YouTurnRadius { get; set; }
        double UturnDistanceFromBoundary { get; set; }
        void ResetCreatedYouTurn();
    }

    /// <summary>[XPLAT] AHRS / IMU state surfaced from <c>mf.ahrs</c> (CAHRS).</summary>
    public interface IAhrsState
    {
        double RollZero { get; set; }
        double RollFilter { get; set; }
        bool IsRollInvert { get; set; }
        double ImuRoll { get; set; }
        double ImuHeading { get; set; }
        double FusionWeight { get; set; }
        bool IsReverseOn { get; set; }
        bool AutoSwitchDualFixOn { get; set; }
        double AutoSwitchDualFixSpeed { get; set; }
    }

    /// <summary>
    /// [XPLAT] Vehicle state surfaced from <c>mf.vehicle</c> (CVehicle). CVehicle still carries a
    /// <c>private readonly FormGPS mf;</c> back-reference and is therefore source-gated out of the
    /// cross-platform GPS compilation (see AgOpenGPS.csproj Checkpoint-6 source gating + AAP §0.6.1)
    /// until the guidance pipeline is extracted into services. The configuration dialog consumes the
    /// vehicle exclusively through this seam — exactly as it consumes the equally-gated CTram/CYouTurn/
    /// CAHRS via ITramState/IGuidanceState/IAhrsState — so it binds to the abstraction rather than the
    /// concrete gated type. Only the members the dialog actually touched on <c>mf.vehicle</c> are
    /// surfaced; <see cref="VehicleConfig"/> itself is the portable Core model (AgOpenGPS.Core.Models),
    /// so it is exposed directly and only its mutable sub-members (antenna/wheelbase/track/type) are set.
    /// [XPLAT] Member names deliberately mirror the original <c>CVehicle</c> public members verbatim
    /// (the schema-frozen domain surface) so the eventual adapter maps 1:1 and the dialog port is exact.
    /// </summary>
    public interface IVehicleState
    {
        VehicleConfig VehicleConfig { get; }
        double hydLiftLookAheadTime { get; set; }
        double slowSpeedCutoff { get; set; }
    }

    /// <summary>
    /// [XPLAT] Tool/implement state surfaced from <c>mf.tool</c> (CTool). Like CVehicle, CTool holds a
    /// <c>private readonly FormGPS mf;</c> back-reference and is source-gated out of the cross-platform
    /// build pending the FormGPS→services decoupling (AAP §0.6.1); the dialog therefore binds to this
    /// abstraction. Every member maps 1:1 to a public field the WinForms FormConfig read or wrote on
    /// <c>mf.tool</c> — no new behavior is introduced. The settings written through these members feed
    /// the behaviour-frozen Tool/Vehicle settings XML (schema frozen, AAP §0.2.2). <c>zoneRanges</c>
    /// is exposed read-only because the dialog only mutates its elements (it is never reassigned).
    /// [XPLAT] Member names deliberately mirror the original <c>CTool</c> public fields verbatim so the
    /// port stays exact and an adapter implementation is a trivial 1:1 mapping.
    /// </summary>
    public interface IToolState
    {
        double width { get; set; }
        double overlap { get; set; }
        double offset { get; set; }
        double hitchLength { get; set; }
        double tankTrailingHitchLength { get; set; }
        double trailingHitchLength { get; set; }
        double trailingToolToPivotLength { get; set; }
        double lookAheadOnSetting { get; set; }
        double lookAheadOffSetting { get; set; }
        double turnOffDelay { get; set; }
        int minCoverage { get; set; }
        int numOfSections { get; set; }
        int zones { get; set; }
        int[] zoneRanges { get; }
        bool isToolTrailing { get; set; }
        bool isToolTBT { get; set; }
        bool isToolRearFixed { get; set; }
        bool isToolFrontFixed { get; set; }
        bool isSectionsNotZones { get; set; }
        bool isSectionOffWhenOut { get; set; }
        bool isMultiColoredSections { get; set; }
        bool isDisplayTramControl { get; set; }
    }
}
