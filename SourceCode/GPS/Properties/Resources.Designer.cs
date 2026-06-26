// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
// Hand-written cross-platform resource accessor replacing the generated ResourceManager class.
//
// This file was previously a Visual Studio auto-generated strongly-typed resource class
// (StronglyTypedResourceBuilder) backed by a runtime resource-manager reading the compiled
// Resources.resx. It exposed images as GDI+ bitmaps and sounds as unmanaged memory streams —
// both of which depend on Windows-only imaging/audio APIs on net8.0. It is now a hand-maintained,
// cross-platform accessor: images load as Avalonia bitmaps from packaged avares:// resources,
// sounds as streams from packaged Content (next to the executable), and the GPLv3 license text
// from the packaged License.txt. The namespace, class name, and every member name are preserved
// so the existing Properties.Resources.<Member> call sites keep compiling unchanged.

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgOpenGPS.Properties
{
    /// <summary>
    ///   Strongly-typed, cross-platform accessor for the GPS image, sound, and text resources.
    ///   [XPLAT] Replaces the net48 auto-generated resource-manager-backed class.
    ///   Every member name (363 images, 11 sounds, and <c>License</c>) is preserved; only the
    ///   return types and backing loaders change:
    ///   <list type="bullet">
    ///     <item>Images return <see cref="Avalonia.Media.Imaging.Bitmap"/> loaded (and cached)
    ///       from <c>avares://AgOpenGPS/btnImages/...</c> resources packaged by AgOpenGPS.csproj.</item>
    ///     <item>Sounds return <see cref="System.IO.Stream"/> opened from the <c>Resources/</c>
    ///       folder copied beside the executable (packaged as Content).</item>
    ///     <item><c>License</c> returns the GPLv3 text read from the packaged <c>License.txt</c>.</item>
    ///   </list>
    /// </summary>
    internal static class Resources
    {
        // ===================================================================================
        //  Loaders / caches
        // ===================================================================================

        /// <summary>
        ///   Cache of decoded bitmaps keyed by their avares asset path. Mirrors the resource-set
        ///   caching the old resource manager performed, so per-frame texture fetches do not
        ///   re-open and re-decode the same PNG. Access is serialized by <see cref="_imgLock"/>
        ///   because textures may be requested from both the UI thread and the OpenGL render thread.
        /// </summary>
        private static readonly Dictionary<string, Bitmap> _imgCache = new Dictionary<string, Bitmap>();
        private static readonly object _imgLock = new object();

        /// <summary>
        ///   Loads (or returns the cached) Avalonia <see cref="Bitmap"/> for a packaged button/glyph
        ///   image. <paramref name="assetPath"/> is the assembly-relative resource path, e.g.
        ///   <c>btnImages/ABDraw.png</c>; it is resolved via the <c>avares://AgOpenGPS/</c> scheme
        ///   (the assembly name is <c>AgOpenGPS</c> and the PNGs are packaged as AvaloniaResource).
        /// </summary>
        private static Bitmap Img(string assetPath)
        {
            lock (_imgLock)
            {
                if (!_imgCache.TryGetValue(assetPath, out var bmp))
                {
                    bmp = new Bitmap(AssetLoader.Open(new Uri($"avares://AgOpenGPS/{assetPath}")));
                    _imgCache[assetPath] = bmp;
                }
                return bmp;
            }
        }

        /// <summary>
        ///   Opens a packaged audio cue (.wav) as a readable <see cref="Stream"/>. The wav files are
        ///   packaged as Content (CopyToOutputDirectory) so they sit under a <c>Resources</c> folder
        ///   beside the executable. A fresh stream is returned on each access so each consumer owns
        ///   and disposes its own handle (parity with the per-access streams the old accessor returned).
        /// </summary>
        private static Stream Wav(string fileName) =>
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Resources", fileName));

        /// <summary>Cached GPLv3 license text (read once from the packaged <c>License.txt</c>).</summary>
        private static string _license;

        // ===================================================================================
        //  Image resources (363) — return Avalonia.Media.Imaging.Bitmap via avares://AgOpenGPS/
        // ===================================================================================

        internal static Bitmap ABDraw => Img("btnImages/ABDraw.png");
        internal static Bitmap ABLatLonHeading => Img("btnImages/ABLatLonHeading.png");
        internal static Bitmap ABLatLonLatLon => Img("btnImages/ABLatLonLatLon.png");
        internal static Bitmap ABLineCycle => Img("btnImages/ABLineCycle.png");
        internal static Bitmap ABLineCycleBk => Img("btnImages/ABLineCycleBk.png");
        internal static Bitmap ABLinesHideShow => Img("btnImages/ABLinesHideShow.png");
        internal static Bitmap ABPivot => Img("btnImages/ABPivot.png");
        internal static Bitmap ABPivotCircle => Img("btnImages/ABPivotCircle.png");
        internal static Bitmap ABSmooth => Img("btnImages/ABSmooth.png");
        internal static Bitmap ABSnapNudgeMenu => Img("btnImages/ABSnapNudgeMenu.png");
        internal static Bitmap ABSnapNudgeMenuRef => Img("btnImages/ABSnapNudgeMenuRef.png");
        internal static Bitmap ABSwapPoints => Img("btnImages/ABSwapPoints.png");
        // [XPLAT] C# identifier sanitizes the resx key 'ABTrackA+'; the on-disk asset keeps the literal '+'.
        internal static Bitmap ABTrackA_ => Img("btnImages/ABTrackA+.png");
        internal static Bitmap ABTrackAB => Img("btnImages/ABTrackAB.png");
        internal static Bitmap ABTrackCurve => Img("btnImages/ABTrackCurve.png");
        internal static Bitmap ABTracks => Img("btnImages/ABTracks.png");
        internal static Bitmap AddNew => Img("btnImages/AddNew.png");
        internal static Bitmap AgIO => Img("btnImages/AgIO.png");
        internal static Bitmap AgShare => Img("btnImages/AgShare.png");
        internal static Bitmap AntennaArticulated => Img("btnImages/AntennaArticulated.png");
        internal static Bitmap AntennaHarvester => Img("btnImages/AntennaHarvester.png");
        internal static Bitmap AntennaLeftOffset => Img("btnImages/AntennaLeftOffset.png");
        internal static Bitmap AntennaNoOffset => Img("btnImages/AntennaNoOffset.png");
        internal static Bitmap AntennaRightOffset => Img("btnImages/AntennaRightOffset.png");
        internal static Bitmap AntennaTractor => Img("btnImages/AntennaTractor.png");
        internal static Bitmap APlusMinusA => Img("btnImages/APlusMinusA.png");
        internal static Bitmap APlusMinusB => Img("btnImages/APlusMinusB.png");
        internal static Bitmap APlusPlusA => Img("btnImages/APlusPlusA.png");
        internal static Bitmap APlusPlusB => Img("btnImages/APlusPlusB.png");
        internal static Bitmap ArrowLeft => Img("btnImages/ArrowLeft.png");
        internal static Bitmap ArrowRight => Img("btnImages/ArrowRight.png");
        internal static Bitmap AutoManualIsAuto => Img("btnImages/AutoManualIsAuto.png");
        internal static Bitmap AutoSteerConf => Img("btnImages/AutoSteerConf.png");
        internal static Bitmap AutoSteerOff => Img("btnImages/AutoSteerOff.png");
        internal static Bitmap AutoSteerOffSnapToPivot => Img("btnImages/AutoSteerOffSnapToPivot.png");
        internal static Bitmap AutoSteerOn => Img("btnImages/AutoSteerOn.png");
        internal static Bitmap AutoSteerOnSnapToPivot => Img("btnImages/AutoSteerOnSnapToPivot.png");
        internal static Bitmap AutoSteerSnapToPivot => Img("btnImages/AutoSteerSnapToPivot.png");
        internal static Bitmap AutoStop => Img("btnImages/AutoStop.png");
        internal static Bitmap AutoTrack => Img("btnImages/AutoTrack.png");
        internal static Bitmap AutoTrackOff => Img("btnImages/AutoTrackOff.png");
        internal static Bitmap AutoUploadOff => Img("btnImages/AutoUploadOff.png");
        internal static Bitmap AutoUploadOn => Img("btnImages/AutoUploadOn.png");
        // [XPLAT] C# identifier sanitizes the resx key 'back-button'; the on-disk asset keeps the literal '-'.
        internal static Bitmap back_button => Img("btnImages/back-button.png");
        internal static Bitmap BackSpace => Img("btnImages/BackSpace.png");
        internal static Bitmap bing1 => Img("btnImages/bing.png");
        internal static Bitmap Boundary => Img("btnImages/Boundary.png");
        internal static Bitmap BoundaryCurveLine => Img("btnImages/BoundaryCurveLine.png");
        internal static Bitmap BoundaryFromTracks => Img("btnImages/BoundaryFromTracks.png");
        internal static Bitmap BoundaryLeft => Img("btnImages/BoundaryLeft.png");
        internal static Bitmap BoundaryLoadFromGE => Img("btnImages/BoundaryLoadFromGE.png");
        internal static Bitmap BoundaryLoadMultiFromGE => Img("btnImages/BoundaryLoadMultiFromGE.png");
        internal static Bitmap BoundaryMakeLine => Img("btnImages/BoundaryMakeLine.png");
        internal static Bitmap BoundaryOuter => Img("btnImages/BoundaryOuter.png");
        internal static Bitmap boundaryPause => Img("btnImages/boundaryPause.png");
        internal static Bitmap boundaryPlay => Img("btnImages/boundaryPlay.png");
        internal static Bitmap BoundaryRecord => Img("btnImages/BoundaryRecord.png");
        internal static Bitmap BoundaryRecordPivot => Img("btnImages/BoundaryRecordPivot.png");
        internal static Bitmap BoundaryRecordTool => Img("btnImages/BoundaryRecordTool.png");
        internal static Bitmap BoundaryReduce => Img("btnImages/BoundaryReduce.png");
        internal static Bitmap BoundaryRight => Img("btnImages/BoundaryRight.png");
        internal static Bitmap BoundarySectionControlOnOff => Img("btnImages/BoundarySectionControlOnOff.png");
        internal static Bitmap boundaryStop => Img("btnImages/boundaryStop.png");
        internal static Bitmap BrightnessDn => Img("btnImages/BrightnessDn.png");
        internal static Bitmap BrightnessUp => Img("btnImages/BrightnessUp.png");
        internal static Bitmap Camera2D64 => Img("btnImages/Camera2D64.png");
        internal static Bitmap Camera3D64 => Img("btnImages/Camera3D64.png");
        internal static Bitmap CameraNorth2D => Img("btnImages/CameraNorth2D.png");
        internal static Bitmap Cancel64 => Img("btnImages/Cancel64.png");
        internal static Bitmap ChargeIndicator => Img("btnImages/ChargeIndicator.png");
        internal static Bitmap ChargingNo => Img("btnImages/ChargingNo.png");
        internal static Bitmap Chart => Img("btnImages/Chart.png");
        internal static Bitmap ColorBackGnd => Img("btnImages/ColorBackGnd.png");
        internal static Bitmap ColorLocked => Img("btnImages/ColorLocked.png");
        internal static Bitmap ColorUnlocked => Img("btnImages/ColorUnlocked.png");
        internal static Bitmap ColourPick => Img("btnImages/ColourPick.png");
        internal static Bitmap Con_Display => Img("btnImages/Config/Con_Display.png");
        internal static Bitmap Con_FeatureMenu => Img("btnImages/Config/Con_FeatureMenu.png");
        internal static Bitmap Con_ImplementMenu => Img("btnImages/Config/Con_ImplementMenu.png");
        internal static Bitmap Con_ModulesMenu => Img("btnImages/Config/Con_ModulesMenu.png");
        internal static Bitmap Con_RightMenuEdit => Img("btnImages/Config/Con_RightMenuEdit.png");
        internal static Bitmap Con_SourcesGPSDual => Img("btnImages/Config/Con_SourcesGPSDual.png");
        internal static Bitmap Con_SourcesGPSSingle => Img("btnImages/Config/Con_SourcesGPSSingle.png");
        internal static Bitmap Con_SourcesHead => Img("btnImages/Config/Con_SourcesHead.png");
        internal static Bitmap Con_SourcesMenu => Img("btnImages/Config/Con_SourcesMenu.png");
        internal static Bitmap Con_SourcesRTKAlarm => Img("btnImages/Config/Con_SourcesRTKAlarm.png");
        internal static Bitmap Con_TramMenu => Img("btnImages/Config/Con_TramMenu.png");
        internal static Bitmap Con_UTurnMenu => Img("btnImages/Config/Con_UTurnMenu.png");
        internal static Bitmap con_VehicleFunctionSpeedLimit => Img("btnImages/Config/con_VehicleFunctionSpeedLimit.png");
        internal static Bitmap Con_VehicleMenu => Img("btnImages/Config/Con_VehicleMenu.png");
        internal static Bitmap ConD_AutoDayNight => Img("btnImages/Config/ConD_AutoDayNight.png");
        internal static Bitmap ConD_DirectionMarker => Img("btnImages/Config/ConD_DirectionMarker.png");
        internal static Bitmap ConD_ExtraGuides => Img("btnImages/Config/ConD_ExtraGuides.png");
        internal static Bitmap ConD_FloorTexture => Img("btnImages/Config/ConD_FloorTexture.png");
        internal static Bitmap ConD_FullScreenBegin => Img("btnImages/Config/ConD_FullScreenBegin.png");
        internal static Bitmap ConD_Grid => Img("btnImages/Config/ConD_Grid.png");
        internal static Bitmap ConD_Imperial => Img("btnImages/Config/ConD_Imperial.png");
        internal static Bitmap ConD_KeyBoard => Img("btnImages/Config/ConD_KeyBoard.png");
        internal static Bitmap ConD_LightBar => Img("btnImages/Config/ConD_LightBar.png");
        internal static Bitmap ConD_LineSmooth => Img("btnImages/Config/ConD_LineSmooth.png");
        internal static Bitmap ConD_LogElevation => Img("btnImages/Config/ConD_LogElevation.png");
        internal static Bitmap ConD_Metric => Img("btnImages/Config/ConD_Metric.png");
        internal static Bitmap ConD_Poligons => Img("btnImages/Config/ConD_Poligons.png");
        internal static Bitmap ConD_RollHelper => Img("btnImages/Config/ConD_RollHelper.png");
        internal static Bitmap ConD_SectionHighlights => Img("btnImages/Config/ConD_SectionHighlights.png");
        internal static Bitmap ConD_Speedometer => Img("btnImages/Config/ConD_Speedometer.png");
        internal static Bitmap ConD_SteerBarBar => Img("btnImages/Config/ConD_SteerBarBar.png");
        internal static Bitmap ConDa_InvertRoll => Img("btnImages/Config/ConDa_InvertRoll.png");
        internal static Bitmap ConDa_RemoveOffset => Img("btnImages/Config/ConDa_RemoveOffset.png");
        internal static Bitmap ConDa_ResetIMU => Img("btnImages/Config/ConDa_ResetIMU.png");
        internal static Bitmap ConDa_RollSetZero => Img("btnImages/Config/ConDa_RollSetZero.png");
        internal static Bitmap ConF_HydLiftSound => Img("btnImages/Config/ConF_HydLiftSound.png");
        internal static Bitmap ConF_SoundSections => Img("btnImages/Config/ConF_SoundSections.png");
        internal static Bitmap ConF_SteerSound => Img("btnImages/Config/ConF_SteerSound.png");
        internal static Bitmap ConF_TurnSound => Img("btnImages/Config/ConF_TurnSound.png");
        internal static Bitmap ConMa_LiftLowerTime => Img("btnImages/Config/ConMa_LiftLowerTime.png");
        internal static Bitmap ConMa_LiftRaiseTime => Img("btnImages/Config/ConMa_LiftRaiseTime.png");
        internal static Bitmap ConS_ImplementAntenna => Img("btnImages/Config/ConS_ImplementAntenna.png");
        internal static Bitmap ConS_ImplementConfig => Img("btnImages/Config/ConS_ImplementConfig.png");
        internal static Bitmap ConS_ImplementHitch => Img("btnImages/Config/ConS_ImplementHitch.png");
        internal static Bitmap ConS_ImplementOffset => Img("btnImages/Config/ConS_ImplementOffset.png");
        internal static Bitmap ConS_ImplementPivot => Img("btnImages/Config/ConS_ImplementPivot.png");
        internal static Bitmap ConS_ImplementSection => Img("btnImages/Config/ConS_ImplementSection.png");
        internal static Bitmap ConS_ImplementSettings => Img("btnImages/Config/ConS_ImplementSettings.png");
        internal static Bitmap ConS_ImplementSwitch => Img("btnImages/Config/ConS_ImplementSwitch.png");
        internal static Bitmap ConS_ModulesMachine => Img("btnImages/Config/ConS_ModulesMachine.png");
        internal static Bitmap ConS_ModulesSteer => Img("btnImages/Config/ConS_ModulesSteer.png");
        internal static Bitmap ConS_Pins => Img("btnImages/Config/ConS_Pins.png");
        internal static Bitmap ConS_SourceFix => Img("btnImages/Config/ConS_SourceFix.png");
        internal static Bitmap ConS_SourcesHeading => Img("btnImages/Config/ConS_SourcesHeading.png");
        internal static Bitmap ConS_SourcesRoll => Img("btnImages/Config/ConS_SourcesRoll.png");
        internal static Bitmap ConS_VehicleConfig => Img("btnImages/Config/ConS_VehicleConfig.png");
        internal static Bitmap ConSt_Danfoss => Img("btnImages/Config/ConST_Danfoss.png");
        internal static Bitmap ConSt_InvertDirection => Img("btnImages/Config/ConSt_InvertDirection.png");
        internal static Bitmap ConSt_InvertRelay => Img("btnImages/Config/ConSt_InvertRelay.png");
        internal static Bitmap ConSt_InvertWAS => Img("btnImages/Config/ConSt_InvertWAS.png");
        internal static Bitmap ConSt_Mandatory1 => Img("btnImages/Config/ConSt_Mandatory.png");
        internal static Bitmap ConSt_TurnSensor => Img("btnImages/Config/ConSt_TurnSensor.png");
        internal static Bitmap ConSt_TurnSensorCurrent => Img("btnImages/Config/ConSt_TurnSensorCurrent.png");
        internal static Bitmap ConSt_TurnSensorPressure => Img("btnImages/Config/ConSt_TurnSensorPressure.png");
        internal static Bitmap ConT_Asymmetric => Img("btnImages/Config/ConT_Asymmetric.png");
        internal static Bitmap ConT_Symmetric => Img("btnImages/Config/ConT_Symmetric.png");
        internal static Bitmap ConT_TramOverride => Img("btnImages/Config/ConT_TramOverride.png");
        internal static Bitmap ConT_TramOverrideDisplay => Img("btnImages/Config/ConT_TramOverrideDisplay.png");
        internal static Bitmap ConT_TramSpacing => Img("btnImages/Config/ConT_TramSpacing.png");
        internal static Bitmap ContourOff => Img("btnImages/ContourOff.png");
        internal static Bitmap ContourOn => Img("btnImages/ContourOn.png");
        internal static Bitmap ConU_UturnDistance => Img("btnImages/Config/ConU_UturnDistance.png");
        internal static Bitmap ConU_UturnLength => Img("btnImages/Config/ConU_UturnLength.png");
        internal static Bitmap ConU_UturnRadius => Img("btnImages/Config/ConU_UturnRadius.png");
        internal static Bitmap ConU_UTurnSmooth => Img("btnImages/Config/ConU_UturnSmooth.png");
        internal static Bitmap ConV_CmPixel => Img("btnImages/Config/ConV_CmPixel.png");
        internal static Bitmap ConV_GuidanceLookAhead => Img("btnImages/Config/ConV_GuidanceLookAhead.png");
        internal static Bitmap ConV_LineWith => Img("btnImages/Config/ConV_LineWith.png");
        internal static Bitmap ConV_MaxAutoSteer => Img("btnImages/Config/ConV_MaxAutoSteer.png");
        internal static Bitmap ConV_MinAutoSteer => Img("btnImages/Config/ConV_MinAutoSteer.png");
        internal static Bitmap ConV_RevSteer => Img("btnImages/Config/ConV_RevSteer.png");
        internal static Bitmap ConV_SnapDistance => Img("btnImages/Config/ConV_SnapDistance.png");
        internal static Bitmap CrossTrackBackground => Img("btnImages/CrossTrackBackground.png");
        internal static Bitmap DeselectAll => Img("btnImages/DeselectAll.png");
        internal static Bitmap Discourse => Img("btnImages/Discourse.png");
        internal static Bitmap DnArrow64 => Img("btnImages/DnArrow64.png");
        internal static Bitmap DownloadAll => Img("btnImages/DownloadAll.png");
        internal static Bitmap DownloadAndUse => Img("btnImages/DownloadAndUse.png");
        internal static Bitmap Error => Img("btnImages/Error.png");
        internal static Bitmap FieldStats => Img("btnImages/FieldStats.png");
        internal static Bitmap FieldTools => Img("btnImages/FieldTools.png");
        internal static Bitmap FileClose => Img("btnImages/FileClose.png");
        internal static Bitmap FileCopy => Img("btnImages/FileCopy.png");
        internal static Bitmap FileDontSave => Img("btnImages/FileDontSave.png");
        internal static Bitmap FileEditName => Img("btnImages/FileEditName.png");
        internal static Bitmap FileExisting => Img("btnImages/FileExisting.png");
        internal static Bitmap FileExplorerWindows => Img("btnImages/FileExplorerWindows.png");
        internal static Bitmap fileMenu => Img("btnImages/fileMenu.png");
        internal static Bitmap FileNew => Img("btnImages/FileNew.png");
        internal static Bitmap FileOpen => Img("btnImages/FileOpen.png");
        internal static Bitmap FilePrevious => Img("btnImages/FilePrevious.png");
        internal static Bitmap FileSave => Img("btnImages/FileSave.png");
        internal static Bitmap FileSaveAs => Img("btnImages/FileSaveAs.png");
        internal static Bitmap FlagDelete => Img("btnImages/FlagDelete.png");
        internal static Bitmap FlagGrn => Img("btnImages/FlagGrn.png");
        internal static Bitmap FlagRed => Img("btnImages/FlagRed.png");
        internal static Bitmap FlagYel => Img("btnImages/FlagYel.png");
        internal static Bitmap ForceOverwrite => Img("btnImages/ForceOverwrite.png");
        internal static Bitmap GitHub => Img("btnImages/GitHub.png");
        internal static Bitmap GoogleEarth => Img("btnImages/GoogleEarth.png");
        internal static Bitmap GPSQuality => Img("btnImages/GPSQuality.png");
        internal static Bitmap GridRotate => Img("btnImages/GridRotate.png");
        internal static Bitmap HardwareMessage => Img("btnImages/HardwareMessage.png");
        internal static Bitmap Headache => Img("btnImages/Headache.png");
        internal static Bitmap HeadlandBuild => Img("btnImages/HeadlandBuild.png");
        internal static Bitmap HeadlandDeletePoints => Img("btnImages/HeadlandDeletePoints.png");
        internal static Bitmap HeadlandDistance => Img("btnImages/Config/HeadlandDistance.png");
        internal static Bitmap HeadlandMenu => Img("btnImages/HeadlandMenu.png");
        internal static Bitmap HeadlandOff => Img("btnImages/HeadlandOff.png");
        internal static Bitmap HeadlandOn => Img("btnImages/HeadlandOn.png");
        internal static Bitmap HeadlandReset => Img("btnImages/HeadlandReset.png");
        internal static Bitmap HeadlandSectionOff => Img("btnImages/HeadlandSectionOn.png");
        internal static Bitmap HeadlandSectionOn => Img("btnImages/HeadlandSectionOff.png");
        internal static Bitmap HeadlandSlice => Img("btnImages/HeadlandSlice.png");
        internal static Bitmap HydraulicLiftOff => Img("btnImages/HydraulicLiftOff.png");
        internal static Bitmap HydraulicLiftOn => Img("btnImages/HydraulicLiftOn.png");
        internal static Bitmap IncrementMinus => Img("btnImages/IncrementMinus.png");
        internal static Bitmap IncrementPlus => Img("btnImages/IncrementPlus.png");
        internal static Bitmap Info => Img("btnImages/Info.png");
        internal static Bitmap IsobusSectionControlIdle => Img("btnImages/IsobusSectionControlIdle.png");
        internal static Bitmap IsobusSectionControlOff => Img("btnImages/IsobusSectionControlOff.png");
        internal static Bitmap IsobusSectionControlOn => Img("btnImages/IsobusSectionControlOn.png");
        internal static Bitmap ISOXML => Img("btnImages/ISOXML.png");
        internal static Bitmap JobActive => Img("btnImages/JobActive.png");
        internal static Bitmap JobNameCalendar => Img("btnImages/JobNameCalendar.png");
        internal static Bitmap JobNameTime => Img("btnImages/JobNameTime.png");
        internal static Bitmap LatLon => Img("btnImages/LatLon.png");
        internal static Bitmap LetterABlue => Img("btnImages/LetterABlue.png");
        internal static Bitmap LetterBBlue => Img("btnImages/LetterBBlue.png");
        internal static Bitmap ManualOff => Img("btnImages/ManualOff.png");
        internal static Bitmap ManualOn => Img("btnImages/ManualOn.png");
        internal static Bitmap MappingOff => Img("btnImages/MappingOff.png");
        internal static Bitmap MappingOn => Img("btnImages/MappingOn.png");
        internal static Bitmap MenuHideShow => Img("btnImages/MenuHideShow.png");
        internal static Bitmap ModePurePursuit => Img("btnImages/ModePurePursuit.png");
        internal static Bitmap ModeStanley => Img("btnImages/ModeStanley.png");
        internal static Bitmap NavigationSettings => Img("btnImages/NavigationSettings.png");
        internal static Bitmap Next => Img("btnImages/Next.png");
        internal static Bitmap OK64 => Img("btnImages/OK64.png");
        internal static Bitmap Pan => Img("btnImages/Pan.png");
        internal static Bitmap PanBackground => Img("btnImages/PanBackground.png");
        internal static Bitmap pathResumeClose => Img("btnImages/pathResumeClose.png");
        internal static Bitmap pathResumeLast => Img("btnImages/pathResumeLast.png");
        internal static Bitmap pathResumeStart => Img("btnImages/pathResumeStart.png");
        internal static Bitmap Play => Img("btnImages/Play.png");
        internal static Bitmap PointAdd => Img("btnImages/PointAdd.png");
        internal static Bitmap PointDelete => Img("btnImages/PointDelete.png");
        internal static Bitmap Previous => Img("btnImages/Previous.png");
        internal static Bitmap QRDiscourse => Img("btnImages/QR/QRDiscourse.png");
        internal static Bitmap QRGitHub => Img("btnImages/QR/QRGitHub.png");
        internal static Bitmap QRYouTube => Img("btnImages/QR/QRYouTube.png");
        internal static Bitmap RadiusWheelBase => Img("btnImages/RadiusWheelBase.png");
        internal static Bitmap RadiusWheelBaseArticulated => Img("btnImages/RadiusWheelBaseArticulated.png");
        internal static Bitmap RadiusWheelBaseHarvester => Img("btnImages/RadiusWheelBaseHarvester.png");
        internal static Bitmap RecPath => Img("btnImages/RecPath.png");
        internal static Bitmap Reset_Default => Img("btnImages/Reset_Default.png");
        internal static Bitmap ResetColors => Img("btnImages/ResetColors.png");
        internal static Bitmap ResetTool => Img("btnImages/ResetTool.png");
        internal static Bitmap SaveToCloud => Img("btnImages/SaveToCloud.png");
        internal static Bitmap Screen2PNG => Img("btnImages/Screen2PNG.png");
        internal static Bitmap ScreenShot => Img("btnImages/ScreenShot.png");
        internal static Bitmap SectionLookAheadDelay => Img("btnImages/Config/SectionLookAheadDelay.gif");
        internal static Bitmap SectionLookAheadOff => Img("btnImages/Config/SectionLookAheadOff.gif");
        internal static Bitmap SectionMapping => Img("btnImages/SectionMapping.png");
        internal static Bitmap SectionMasterOff => Img("btnImages/SectionMasterOff.png");
        internal static Bitmap SectionMasterOn => Img("btnImages/SectionMasterOn.png");
        internal static Bitmap SectionOffBelow => Img("btnImages/SectionOffBelow.png");
        internal static Bitmap SectionOffBoundary => Img("btnImages/SectionOffBoundary.png");
        internal static Bitmap SectionOnBoundary => Img("btnImages/SectionOnBoundary.png");
        internal static Bitmap SectionOnLookAhead => Img("btnImages/Config/SectionOnLookAhead.gif");
        internal static Bitmap SelectAll => Img("btnImages/SelectAll.png");
        internal static Bitmap Settings48 => Img("btnImages/Settings48.png");
        internal static Bitmap Sf_GainTab => Img("btnImages/Steer/Sf_GainTab.png");
        internal static Bitmap Sf_PP => Img("btnImages/Steer/Sf_PP.png");
        internal static Bitmap Sf_Stanley => Img("btnImages/Steer/Sf_Stanley.png");
        internal static Bitmap Sf_SteerTab => Img("btnImages/Steer/Sf_SteerTab.png");
        internal static Bitmap SnapLeft => Img("btnImages/SnapLeft.png");
        internal static Bitmap SnapLeftHalf => Img("btnImages/SnapLeftHalf.png");
        internal static Bitmap SnapRight => Img("btnImages/SnapRight.png");
        internal static Bitmap SnapRightHalf => Img("btnImages/SnapRightHalf.png");
        internal static Bitmap SnapToPivot => Img("btnImages/SnapToPivot.png");
        internal static Bitmap Sort => Img("btnImages/Sort.png");
        internal static Bitmap SpecialFunctions => Img("btnImages/SpecialFunctions.png");
        internal static Bitmap ST_SmartWAS => Img("btnImages/Steer/ST_SmartWAS.png");
        internal static Bitmap SteerDriveOff => Img("btnImages/SteerDriveOff.png");
        internal static Bitmap SteerDriveOn => Img("btnImages/SteerDriveOn.png");
        internal static Bitmap SteerLeft => Img("btnImages/SteerLeft.png");
        internal static Bitmap SteerRight => Img("btnImages/SteerRight.png");
        internal static Bitmap SteerZero => Img("btnImages/SteerZero.png");
        internal static Bitmap Stop => Img("btnImages/Stop.png");
        internal static Bitmap SvennArrow => Img("btnImages/SvennArrow.png");
        internal static Bitmap SwitchActiveClosed => Img("btnImages/SwitchActiveClosed.png");
        internal static Bitmap SwitchActiveOpen => Img("btnImages/SwitchActiveOpen.png");
        internal static Bitmap SwitchOff => Img("btnImages/SwitchOff.png");
        internal static Bitmap SwitchOn => Img("btnImages/SwitchOn.png");
        internal static Bitmap TiltDown => Img("btnImages/TiltDown.png");
        internal static Bitmap TiltUp => Img("btnImages/TiltUp.png");
        internal static Bitmap ToolAcceptChange => Img("btnImages/ToolAcceptChange.png");
        internal static Bitmap ToolChkFront => Img("btnImages/ToolChkFront.png");
        internal static Bitmap ToolChkRear => Img("btnImages/ToolChkRear.png");
        internal static Bitmap ToolChkTBT => Img("btnImages/ToolChkTBT.png");
        internal static Bitmap ToolChkTrailing => Img("btnImages/ToolChkTrailing.png");
        internal static Bitmap ToolGap => Img("btnImages/Config/ToolGap.png");
        internal static Bitmap ToolHitchPageFront => Img("btnImages/ToolHitchPageFront.png");
        internal static Bitmap ToolHitchPageFrontHarvester => Img("btnImages/ToolHitchPageFrontHarvester.png");
        internal static Bitmap ToolHitchPageRear => Img("btnImages/ToolHitchPageRear.png");
        internal static Bitmap ToolHitchPageTBT => Img("btnImages/ToolHitchPageTBT.png");
        internal static Bitmap ToolHitchPageTrailing => Img("btnImages/ToolHitchPageTrailing.png");
        internal static Bitmap ToolHitchPivotOffsetNeg => Img("btnImages/Config/ToolHitchPivotOffsetNeg.png");
        internal static Bitmap ToolHitchPivotOffsetPos => Img("btnImages/Config/ToolHitchPivotOffsetPos.png");
        internal static Bitmap ToolOffsetNegativeLeft => Img("btnImages/Config/ToolOffsetNegativeLeft.png");
        internal static Bitmap ToolOffsetPositiveRight => Img("btnImages/Config/ToolOffsetPositiveRight.png");
        internal static Bitmap ToolOverlap => Img("btnImages/Config/ToolOverlap.png");
        internal static Bitmap TrackCurve => Img("btnImages/TrackCurve.png");
        internal static Bitmap TrackLine => Img("btnImages/TrackLine.png");
        internal static Bitmap TrackOn => Img("btnImages/TrackOn.png");
        internal static Bitmap TrackPivot => Img("btnImages/TrackPivot.png");
        internal static Bitmap TracksInvisible => Img("btnImages/TracksInvisible.png");
        internal static Bitmap TrackVisible => Img("btnImages/TrackVisible.png");
        internal static Bitmap TramAll => Img("btnImages/TramAll.png");
        internal static Bitmap TramLines => Img("btnImages/TramLines.png");
        internal static Bitmap TramMulti => Img("btnImages/TramMulti.png");
        internal static Bitmap TramOff => Img("btnImages/TramOff.png");
        internal static Bitmap TramOuter => Img("btnImages/TramOuter.png");
        internal static Bitmap Trash => Img("btnImages/Trash.png");
        internal static Bitmap TrashApplied => Img("btnImages/TrashApplied.png");
        internal static Bitmap TrashContourRef => Img("btnImages/TrashContourRef.png");
        internal static Bitmap UpArrow64 => Img("btnImages/UpArrow64.png");
        internal static Bitmap UploadOff => Img("btnImages/UploadOff.png");
        internal static Bitmap UploadOn => Img("btnImages/UploadOn.png");
        internal static Bitmap VehicleOpacity => Img("btnImages/Images/VehicleOpacity.png");
        internal static Bitmap vehiclePageArticulated => Img("btnImages/vehiclePageArticulated.png");
        internal static Bitmap vehiclePageHarvester => Img("btnImages/vehiclePageHarvester.png");
        internal static Bitmap vehiclePageTractor => Img("btnImages/vehiclePageTractor.png");
        internal static Bitmap Warning => Img("btnImages/Warning.png");
        internal static Bitmap Webcam => Img("btnImages/Webcam.png");
        internal static Bitmap WindowClose => Img("btnImages/WindowClose.png");
        internal static Bitmap WindowDayMode => Img("btnImages/WindowDayMode.png");
        internal static Bitmap WindowMaximize => Img("btnImages/WindowMaximize.png");
        internal static Bitmap WindowMinimize => Img("btnImages/WindowMinimize.png");
        internal static Bitmap WindowNightMode => Img("btnImages/WindowNightMode.png");
        internal static Bitmap WizardWand => Img("btnImages/WizardWand.png");
        internal static Bitmap WizSteerDot => Img("btnImages/WizSteerDot.png");
        internal static Bitmap WizWasZeroReset => Img("btnImages/WizWasZeroReset.png");
        internal static Bitmap YouSkipOff => Img("btnImages/YouSkipOff.png");
        internal static Bitmap YouSkipOn => Img("btnImages/YouSkipOn.png");
        internal static Bitmap YouSkipWorkedTracks => Img("btnImages/YouSkipWorkedTracks.png");
        internal static Bitmap YouTube => Img("btnImages/YouTube.png");
        internal static Bitmap Youturn80 => Img("btnImages/YouTurn80.png");
        internal static Bitmap YouTurnH => Img("btnImages/YouTurnH.png");
        internal static Bitmap YouTurnNo => Img("btnImages/YouTurnNo.png");
        internal static Bitmap YouTurnReverse => Img("btnImages/YouTurnReverse.png");
        internal static Bitmap YouTurnU => Img("btnImages/YouTurnU.png");
        internal static Bitmap z_Compass => Img("btnImages/Images/z_Compass.png");
        internal static Bitmap z_Floor => Img("btnImages/Images/z_Floor.png");
        internal static Bitmap z_Font => Img("btnImages/Images/z_Font.png");
        internal static Bitmap z_FrontWheels => Img("btnImages/Images/z_FrontWheels.png");
        internal static Bitmap z_HeadlandDark => Img("btnImages/Images/z_HeadlandDark.png");
        internal static Bitmap z_HeadlandLight => Img("btnImages/Images/z_HeadlandLight.png");
        internal static Bitmap z_LateralManual => Img("btnImages/Images/z_LateralManual.png");
        internal static Bitmap z_Lift => Img("btnImages/Images/z_Lift.png");
        internal static Bitmap z_NoGPS => Img("btnImages/Images/z_NoGPS.png");
        internal static Bitmap z_QuestionMark => Img("btnImages/Images/z_QuestionMark.png");
        internal static Bitmap z_Speedo => Img("btnImages/Images/z_Speedo.png");
        internal static Bitmap z_SpeedoNeedle => Img("btnImages/Images/z_SpeedoNeedle.png");
        internal static Bitmap z_SteerDot => Img("btnImages/Images/z_SteerDot.png");
        internal static Bitmap z_SteerPointer => Img("btnImages/Images/z_SteerPointer.png");
        internal static Bitmap z_Tire => Img("btnImages/Images/z_Tire.png");
        internal static Bitmap z_Tool => Img("btnImages/Images/z_Tool.png");
        internal static Bitmap z_TramOnOff => Img("btnImages/Images/z_TramOnOff.png");
        internal static Bitmap z_Turn => Img("btnImages/Images/z_Turn.png");
        internal static Bitmap z_TurnCancel => Img("btnImages/Images/z_TurnCancel.png");
        internal static Bitmap z_TurnManual => Img("btnImages/Images/z_TurnManual.png");
        internal static Bitmap ZoomIn48 => Img("btnImages/ZoomIn48.png");
        internal static Bitmap ZoomOGL => Img("btnImages/ZoomOGL.png");
        internal static Bitmap ZoomOut => Img("btnImages/ZoomOut.png");
        internal static Bitmap ZoomOut48 => Img("btnImages/ZoomOut48.png");

        // ===================================================================================
        //  Sound resources (11) — return System.IO.Stream from packaged Content (Resources/*.wav)
        // ===================================================================================

        internal static Stream Alarm10 => Wav("Alarm10.wav");
        internal static Stream Headland => Wav("Headland.wav");
        internal static Stream HydDown => Wav("HydDown.wav");
        internal static Stream HydUp => Wav("HydUp.wav");
        internal static Stream rtk_back => Wav("rtk_back.wav");
        internal static Stream rtk_lost => Wav("rtk_lost.wav");
        internal static Stream SectionOff => Wav("SectionOff.wav");
        internal static Stream SectionOn => Wav("SectionOn.wav");
        internal static Stream SteerOff => Wav("SteerOff.wav");
        internal static Stream SteerOn => Wav("SteerOn.wav");
        internal static Stream TF012 => Wav("TF012.WAV");   // [XPLAT] on-disk extension is UPPERCASE

        // ===================================================================================
        //  License (1) — GPLv3 text from packaged License.txt (Content, beside the executable)
        // ===================================================================================

        internal static string License =>
            _license ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "License.txt"));
    }
}
