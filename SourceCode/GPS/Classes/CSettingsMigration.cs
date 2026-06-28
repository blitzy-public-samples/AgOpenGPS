// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Properties;

namespace AgOpenGPS
{
    public static class CSettingsMigration
    {
        /// <summary>
        /// Reads the XML settings type name from the third-level element.
        /// Returns the element name (e.g. "AgOpenGPS.Properties.VehicleSettings") or null on failure.
        /// </summary>
        public static string GetXmlSettingsType(string filePath)
        {
            try
            {
                using (var reader = new XmlTextReader(filePath))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Depth == 2)
                            return reader.Name;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Checks if the XML file contains the expected settings type.
        /// </summary>
        public static bool IsSettingsType(string filePath, string expectedType)
        {
            string type = GetXmlSettingsType(filePath);
            return type != null && type.Contains(expectedType);
        }

        /// <summary>
        /// Returns true if the file is an old format (not VehicleSettings, not ToolSettings).
        /// </summary>
        public static bool IsOldFormat(string filePath)
        {
            string type = GetXmlSettingsType(filePath);
            if (type == null) return false;
            return !type.Contains("VehicleSettings") && !type.Contains("ToolSettings");
        }

        public static bool NeedsMigration(string fileName)
        {
            string path = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", fileName + ".xml");
            return File.Exists(path) && IsOldFormat(path);
        }

        public static LoadResult MigrateVehicle(string vehicleFileName, VehicleSettings vehicleSettings)
        {
            return MigrateVehicle(vehicleFileName, vehicleFileName, vehicleSettings);
        }

        public static LoadResult MigrateVehicle(string sourceFileName, string outputName, VehicleSettings vehicleSettings)
        {
            // Look in old location (baseDirectory\Vehicles) for backward compatibility
            // New profiles are in VehicleProfiles, but old combined profiles are still in Vehicles
            string oldPath = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", sourceFileName + ".xml");

            if (!File.Exists(oldPath))
                return LoadResult.MissingFile;

            // Load old settings
            SettingsLegacy oldSettings = new SettingsLegacy();
            var loadResult = XmlSettingsHandler.LoadXMLFile(oldPath, oldSettings);
            if (loadResult != LoadResult.Ok)
                return loadResult;

            // Copy vehicle settings from old to new
            CopyVehicleSettings(oldSettings, vehicleSettings);

            // Save as new format with error handling
            try
            {
                // Ensure directory exists (may have been deleted while program is running)
                if (!Directory.Exists(RegistrySettings.vehiclesDirectory))
                {
                    Directory.CreateDirectory(RegistrySettings.vehiclesDirectory);
                    Log.EventWriter("VehicleProfiles directory recreated");
                }

                vehicleSettings.Save(outputName);

                // Verify file was actually created
                string newPath = Path.Combine(RegistrySettings.vehiclesDirectory, outputName + ".xml");
                if (!File.Exists(newPath))
                {
                    Log.ErrorWriter(sourceFileName, "Vehicle", "SaveFailed: File not created after save");
                    return LoadResult.Failed;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorWriter(sourceFileName, "Vehicle", "SaveFailed: " + ex.Message, ex);
                return LoadResult.Failed;
            }

            return LoadResult.Ok;
        }

        public static LoadResult MigrateTool(string toolFileName, ToolSettings toolSettings)
        {
            return MigrateTool(toolFileName, toolFileName, toolSettings);
        }

        public static LoadResult MigrateTool(string sourceFileName, string outputName, ToolSettings toolSettings)
        {
            string oldPath = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", sourceFileName + ".xml");

            if (!File.Exists(oldPath))
                return LoadResult.MissingFile;

            // Load old settings
            SettingsLegacy oldSettings = new SettingsLegacy();
            var loadResult = XmlSettingsHandler.LoadXMLFile(oldPath, oldSettings);
            if (loadResult != LoadResult.Ok)
                return loadResult;

            // Copy tool settings from old to new
            CopyToolSettings(oldSettings, toolSettings);

            // Save as new format with error handling
            try
            {
                // Ensure directory exists (may have been deleted while program is running)
                if (!Directory.Exists(RegistrySettings.toolsDirectory))
                {
                    Directory.CreateDirectory(RegistrySettings.toolsDirectory);
                    Log.EventWriter("ToolProfiles directory recreated");
                }

                toolSettings.Save(outputName);

                // Verify file was actually created
                string newPath = Path.Combine(RegistrySettings.toolsDirectory, outputName + ".xml");
                if (!File.Exists(newPath))
                {
                    Log.ErrorWriter(sourceFileName, "Tool", "SaveFailed: File not created after save");
                    return LoadResult.Failed;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorWriter(sourceFileName, "Tool", "SaveFailed: " + ex.Message, ex);
                return LoadResult.Failed;
            }

            return LoadResult.Ok;
        }

        private static void CopyVehicleSettings(SettingsLegacy source, VehicleSettings dest)
        {
            // Vehicle dimensions
            dest.setVehicle_wheelbase = source.setVehicle_wheelbase;
            dest.setVehicle_antennaHeight = source.setVehicle_antennaHeight;
            dest.setVehicle_antennaPivot = source.setVehicle_antennaPivot;
            dest.setVehicle_antennaOffset = source.setVehicle_antennaOffset;
            dest.setVehicle_maxSteerAngle = source.setVehicle_maxSteerAngle;
            dest.setVehicle_maxAngularVelocity = source.setVehicle_maxAngularVelocity;
            dest.setVehicle_trackWidth = source.setVehicle_trackWidth;

            // AutoSteer (excluding snapDistance, isAutoSteerAutoOn, guidanceLookAheadTime, isConstantContourOn, uTurnSmoothing, uTurnCompensation - these are now in Environment)
            dest.setAS_Kp = source.setAS_Kp;
            dest.setAS_countsPerDegree = source.setAS_countsPerDegree;
            dest.setAS_minSteerPWM = source.setAS_minSteerPWM;
            dest.setAS_highSteerPWM = source.setAS_highSteerPWM;
            dest.setAS_lowSteerPWM = source.setAS_lowSteerPWM;
            dest.setAS_wasOffset = source.setAS_wasOffset;
            dest.setAS_sideHillComp = source.setAS_sideHillComp;
            dest.setAS_ackerman = source.setAS_ackerman;
            dest.setAS_ModeXTE = source.setAS_ModeXTE;
            dest.setAS_ModeTime = source.setAS_ModeTime;
            dest.setAS_functionSpeedLimit = source.setAS_functionSpeedLimit;
            dest.setAS_maxSteerSpeed = source.setAS_maxSteerSpeed;
            dest.setAS_minSteerSpeed = source.setAS_minSteerSpeed;
            dest.setAS_isSteerInReverse = source.setAS_isSteerInReverse;

            // IMU
            dest.setIMU_rollZero = source.setIMU_rollZero;
            dest.setIMU_rollFilter = source.setIMU_rollFilter;
            dest.setIMU_invertRoll = source.setIMU_invertRoll;
            dest.setIMU_isDualAsIMU = source.setIMU_isDualAsIMU;
            dest.setIMU_fusionWeight2 = source.setIMU_fusionWeight2;

            // GPS (zonder SimLatitude/SimLongitude, isRTK, isRTK_KillAutoSteer, ageAlarm, jumpFixAlarmDistance - die zitten nu in Environment)
            dest.setGPS_headingFromWhichSource = source.setGPS_headingFromWhichSource;
            dest.setGPS_forwardComp = source.setGPS_forwardComp;
            dest.setGPS_reverseComp = source.setGPS_reverseComp;
            dest.setGPS_dualHeadingOffset = source.setGPS_dualHeadingOffset;
            dest.setGPS_dualReverseDetectionDistance = source.setGPS_dualReverseDetectionDistance;
            dest.setGPS_minimumStepLimit = source.setGPS_minimumStepLimit;

            // Arduino Steer
            dest.setArdSteer_setting0 = source.setArdSteer_setting0;
            dest.setArdSteer_setting1 = source.setArdSteer_setting1;
            dest.setArdSteer_maxPulseCounts = source.setArdSteer_maxPulseCounts;
            dest.setArdMac_isDanfoss = source.setArdMac_isDanfoss;

            // Brands - parse with fallback for old names (JDeere -> JohnDeere)
            dest.setBrand_TBrand = ParseTractorBrand(source.setBrand_TBrand);
            dest.setBrand_HBrand = ParseHarvesterBrand(source.setBrand_HBrand);
            dest.setBrand_WDBrand = ParseArticulatedBrand(source.setBrand_WDBrand);

            // Vehicle type (excluding goalPointLookAheadMult, goalPointLookAheadHold, goalPointAcquireFactor, slowSpeedCutoff, minCoverage, hydraulicLiftLookAhead, toolOffDelay, isSteerWorkSwitchEnabled - these are now in Tool)
            dest.setVehicle_vehicleType = source.setVehicle_vehicleType;
            dest.setVehicle_panicStopSpeed = source.setVehicle_panicStopSpeed;
        }

        /// <summary>
        /// Parses TractorBrand string with fallback to default.
        /// </summary>
        private static TractorBrand ParseTractorBrand(string value)
        {
            if (string.IsNullOrEmpty(value))
                return TractorBrand.AGOpenGPS;

            if (Enum.TryParse(value, true, out TractorBrand result))
                return result;

            Log.EventWriter($"Unknown TractorBrand '{value}', resetting to AGOpenGPS");
            return TractorBrand.AGOpenGPS;
        }

        /// <summary>
        /// Parses HarvesterBrand string with fallback to default.
        /// </summary>
        private static HarvesterBrand ParseHarvesterBrand(string value)
        {
            if (string.IsNullOrEmpty(value))
                return HarvesterBrand.AgOpenGPS;

            if (Enum.TryParse(value, true, out HarvesterBrand result))
                return result;

            Log.EventWriter($"Unknown HarvesterBrand '{value}', resetting to AgOpenGPS");
            return HarvesterBrand.AgOpenGPS;
        }

        /// <summary>
        /// Parses ArticulatedBrand string with fallback to default.
        /// </summary>
        private static ArticulatedBrand ParseArticulatedBrand(string value)
        {
            if (string.IsNullOrEmpty(value))
                return ArticulatedBrand.AgOpenGPS;

            if (Enum.TryParse(value, true, out ArticulatedBrand result))
                return result;

            Log.EventWriter($"Unknown ArticulatedBrand '{value}', resetting to AgOpenGPS");
            return ArticulatedBrand.AgOpenGPS;
        }

        private static void CopyToolSettings(SettingsLegacy source, ToolSettings dest)
        {
            // Tool dimensions
            dest.setVehicle_toolWidth = source.setVehicle_toolWidth;
            dest.setVehicle_toolOverlap = source.setVehicle_toolOverlap;
            dest.setVehicle_toolOffset = source.setVehicle_toolOffset;
            dest.setVehicle_tankTrailingHitchLength = source.setVehicle_tankTrailingHitchLength;
            dest.setVehicle_toolTrailingHitchLength = source.setVehicle_toolTrailingHitchLength;
            dest.setVehicle_toolLookAheadOn = source.setVehicle_toolLookAheadOn;
            dest.setVehicle_toolLookAheadOff = source.setVehicle_toolLookAheadOff;
            dest.setVehicle_hitchLength = source.setVehicle_hitchLength;

            dest.setTool_isToolTrailing = source.setTool_isToolTrailing;
            dest.setTool_isToolRearFixed = source.setTool_isToolRearFixed;
            dest.setTool_isToolTBT = source.setTool_isToolTBT;
            dest.setTool_isToolFront = source.setTool_isToolFront;
            dest.setTool_isSectionsNotZones = source.setTool_isSectionsNotZones;
            dest.setTool_isSectionOffWhenOut = source.setTool_isSectionOffWhenOut;
            dest.setTool_isDisplayTramControl = source.setTool_isDisplayTramControl;
            dest.setTool_isTramOuterInverted = source.setTool_isTramOuterInverted;
            dest.setTool_isDirectionMarkers = source.setTool_isDirectionMarkers;
            dest.setTool_trailingToolToPivotLength = source.setTool_trailingToolToPivotLength;

            // Sections
            dest.setVehicle_numSections = source.setVehicle_numSections;
            dest.setTool_numSectionsMulti = source.setTool_numSectionsMulti;
            dest.setSection_isFast = source.setSection_isFast;
            dest.setTool_defaultSectionWidth = source.setTool_defaultSectionWidth;
            dest.setTool_sectionWidthMulti = source.setTool_sectionWidthMulti;
            // Old format had 10 zones values, new format uses 9 - truncate to prevent IndexOutOfRangeException
            var zoneWords = source.setTool_zones.Split(',');
            dest.setTool_zones = string.Join(",", zoneWords.Take(9));

            // Section positions
            dest.setSection_position1 = source.setSection_position1;
            dest.setSection_position2 = source.setSection_position2;
            dest.setSection_position3 = source.setSection_position3;
            dest.setSection_position4 = source.setSection_position4;
            dest.setSection_position5 = source.setSection_position5;
            dest.setSection_position6 = source.setSection_position6;
            dest.setSection_position7 = source.setSection_position7;
            dest.setSection_position8 = source.setSection_position8;
            dest.setSection_position9 = source.setSection_position9;
            dest.setSection_position10 = source.setSection_position10;
            dest.setSection_position11 = source.setSection_position11;
            dest.setSection_position12 = source.setSection_position12;
            dest.setSection_position13 = source.setSection_position13;
            dest.setSection_position14 = source.setSection_position14;
            dest.setSection_position15 = source.setSection_position15;
            dest.setSection_position16 = source.setSection_position16;
            dest.setSection_position17 = source.setSection_position17;

            // Section colors
            dest.setColor_sec01 = source.setColor_sec01;
            dest.setColor_sec02 = source.setColor_sec02;
            dest.setColor_sec03 = source.setColor_sec03;
            dest.setColor_sec04 = source.setColor_sec04;
            dest.setColor_sec05 = source.setColor_sec05;
            dest.setColor_sec06 = source.setColor_sec06;
            dest.setColor_sec07 = source.setColor_sec07;
            dest.setColor_sec08 = source.setColor_sec08;
            dest.setColor_sec09 = source.setColor_sec09;
            dest.setColor_sec10 = source.setColor_sec10;
            dest.setColor_sec11 = source.setColor_sec11;
            dest.setColor_sec12 = source.setColor_sec12;
            dest.setColor_sec13 = source.setColor_sec13;
            dest.setColor_sec14 = source.setColor_sec14;
            dest.setColor_sec15 = source.setColor_sec15;
            dest.setColor_sec16 = source.setColor_sec16;
            dest.setColor_isMultiColorSections = source.setColor_isMultiColorSections;

            // DeadZone settings (from Vehicle)
            dest.setAS_deadZoneDistance = source.setAS_deadZoneDistance;
            dest.setAS_deadZoneHeading = source.setAS_deadZoneHeading;
            dest.setAS_deadZoneDelay = source.setAS_deadZoneDelay;

            // Headland
            dest.setHeadland_isSectionControlled = source.setHeadland_isSectionControlled;

            // Arduino Machine (moved from Vehicle to Tool)
            dest.setArdMac_setting0 = source.setArdMac_setting0;
            dest.setArdMac_isHydEnabled = source.setArdMac_isHydEnabled;
            dest.setArdMac_hydRaiseTime = source.setArdMac_hydRaiseTime;
            dest.setArdMac_hydLowerTime = source.setArdMac_hydLowerTime;
            dest.setArdMac_user1 = source.setArdMac_user1;
            dest.setArdMac_user2 = source.setArdMac_user2;
            dest.setArdMac_user3 = source.setArdMac_user3;
            dest.setArdMac_user4 = source.setArdMac_user4;

            // Relay (moved from Vehicle to Tool)
            dest.setRelay_pinConfig = source.setRelay_pinConfig;

            // Guidance algorithm (moved from Vehicle to Tool)
            dest.purePursuitIntegralGainAB = source.purePursuitIntegralGainAB;
            dest.stanleyDistanceErrorGain = source.stanleyDistanceErrorGain;
            dest.stanleyHeadingErrorGain = source.stanleyHeadingErrorGain;
            dest.stanleyIntegralGainAB = source.stanleyIntegralGainAB;
            dest.setVehicle_isStanleyUsed = source.setVehicle_isStanleyUsed;

            // Tool specific Steer Parameters (moved from Vehicle to Tool)
            dest.setVehicle_goalPointLookAheadHold = source.setVehicle_goalPointLookAheadHold;
            dest.setVehicle_toolOffDelay = source.setVehicle_toolOffDelay;
            dest.setVehicle_goalPointLookAheadMult = source.setVehicle_goalPointLookAheadMult;
            dest.setVehicle_goalPointAcquireFactor = source.setVehicle_goalPointAcquireFactor;
            dest.setVehicle_slowSpeedCutoff = source.setVehicle_slowSpeedCutoff;
            dest.setVehicle_minCoverage = source.setVehicle_minCoverage;
            dest.setVehicle_hydraulicLiftLookAhead = source.setVehicle_hydraulicLiftLookAhead;
            dest.setF_isSteerWorkSwitchEnabled = source.setF_isSteerWorkSwitchEnabled;
        }

        private static void CheckAndBackupOldFile(string fileName)
        {
            string oldPath = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".XML");
            string vehiclePath = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".xml");
            string toolPath = Path.Combine(RegistrySettings.toolsDirectory, fileName + ".xml");

            // If both new files exist, backup the old one
            if (File.Exists(vehiclePath) && File.Exists(toolPath) && File.Exists(oldPath))
            {
                string backupDir = Path.Combine(RegistrySettings.vehiclesDirectory, "oldSettingsFiles");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);

                string backupPath = Path.Combine(backupDir, fileName + ".XML.backup");
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Move(oldPath, backupPath);
                Log.EventWriter($"Settings migrated for {fileName}. Old file backed up to oldSettingsFiles.");
            }
        }

        public static LoadResult MigrateEnvironment(string sourceFileName, string outputName)
        {
            string oldPath = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", sourceFileName + ".xml");

            if (!File.Exists(oldPath))
                return LoadResult.MissingFile;

            // Load old settings
            SettingsLegacy oldSettings = new SettingsLegacy();
            var loadResult = XmlSettingsHandler.LoadXMLFile(oldPath, oldSettings);
            if (loadResult != LoadResult.Ok)
                return loadResult;

            // Copy environment settings from old to new
            var envSettings = new Settings();
            Settings.MigrateFromOldToTarget(oldSettings, envSettings);

            // Save as environment file with error handling
            try
            {
                // Ensure directory exists (may have been deleted while program is running)
                if (!Directory.Exists(RegistrySettings.environmentDirectory))
                {
                    Directory.CreateDirectory(RegistrySettings.environmentDirectory);
                    Log.EventWriter("Environment directory recreated");
                }

                string envPath = Path.Combine(RegistrySettings.environmentDirectory, outputName + ".xml");
                XmlSettingsHandler.SaveXMLFile(envPath, envSettings);

                // Verify file was actually created
                if (!File.Exists(envPath))
                {
                    Log.ErrorWriter(sourceFileName, "Environment", "SaveFailed: File not created after save");
                    return LoadResult.Failed;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorWriter(sourceFileName, "Environment", "SaveFailed: " + ex.Message, ex);
                return LoadResult.Failed;
            }

            return LoadResult.Ok;
        }

        /// <summary>
        /// Gets list of old format XML files in the base\Vehicles directory that can be converted.
        /// Uses XML type detection to distinguish old from new format.
        /// </summary>
        public static string[] GetConvertibleFiles()
        {
            string oldVehiclesDir = Path.Combine(RegistrySettings.baseDirectory, "Vehicles");
            if (!Directory.Exists(oldVehiclesDir))
                return new string[0];

            var files = new System.Collections.Generic.List<string>();
            foreach (string file in Directory.GetFiles(oldVehiclesDir, "*.xml"))
            {
                // Include all old format files (converted and not)
                if (IsOldFormat(file))
                    files.Add(Path.GetFileNameWithoutExtension(file));
            }
            return files.ToArray();
        }

        /// <summary>
        /// Checks if an old format file has been marked as converted.
        /// </summary>
        public static bool IsConverted(string fileName)
        {
            string oldPath = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", fileName + ".xml");
            if (!File.Exists(oldPath)) return false;

            string content = File.ReadAllText(oldPath);
            return content.Contains("<!-- CONVERTED:");
        }

        /// <summary>
        /// Marks an old format file as converted by adding a comment.
        /// The original file is left untouched for backward compatibility.
        /// </summary>
        public static void MarkAsConverted(string fileName)
        {
            string oldPath = Path.Combine(RegistrySettings.baseDirectory, "Vehicles", fileName + ".xml");
            if (!File.Exists(oldPath)) return;

            // Don't mark if it's already a new-format file
            if (!IsOldFormat(oldPath)) return;

            // Check if already marked
            string content = File.ReadAllText(oldPath);
            if (content.Contains("<!-- CONVERTED:"))
            {
                Log.EventWriter($"File already marked as converted: {fileName}");
                return;
            }

            // Add marker comment at the end (harmless to old versions)
            try
            {
                using (var writer = File.AppendText(oldPath))
                {
                    writer.WriteLine();
                    writer.WriteLine("<!-- CONVERTED: Vehicle/Tool/Environment profiles created -->");
                }
                Log.EventWriter($"Marked as converted: {fileName}");
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Error marking file as converted: {fileName} - {ex.Message}");
            }
        }

        public static void MigrateAllVehicles()
        {
            string[] files = GetConvertibleFiles();

            foreach (string fileName in files)
            {
                // Migrate vehicle settings
                var vehicleSettings = new VehicleSettings();
                MigrateVehicle(fileName, fileName, vehicleSettings);

                // Migrate tool settings
                var toolSettings = new ToolSettings();
                MigrateTool(fileName, fileName, toolSettings);

                // Mark old file as converted
                MarkAsConverted(fileName);
            }
        }

        // [XPLAT] One-time, Windows-only migration of the legacy Windows Registry settings — the per-user
        // AgOpenGPS key, the pre-net8 backing described in docs/settings.md L26-28 — into the cross-platform,
        // file-based settings introduced by the migration. Before net8 the six top-level configuration
        // strings — WorkingDirectory, VehicleProfileName, ToolProfileName, EnvironmentFileName,
        // VehicleFileName and Language — lived in the Windows Registry; on net8 they live in
        // RegistrySettings.xml under IPlatformServices.AppDataRoot. This hook folds those old Registry
        // values forward on the first launch after upgrading, so an existing Windows user keeps their
        // working directory, selected profiles and language without reconfiguring.
        //
        // Call this once during startup BEFORE RegistrySettings.Load(): per the design note in
        // Properties/RegistrySettings.cs, that class has no Registry knowledge and relies on this seed to
        // have written its config file first.
        //
        // Cross-platform contract:
        //   * The actual Registry API is delegated entirely to WindowsPlatformServices — this file never
        //     references the Windows Registry API directly — and that type is compiled only for the
        //     net8.0-windows target, so the body lives under #if WINDOWS.
        //   * The RuntimeInformation.IsOSPlatform(OSPlatform.Windows) guard makes the method a no-op on
        //     Linux and macOS, where there is no legacy Registry to read.
        //   * It is idempotent: once the cross-platform config file exists the migration has already run
        //     (or the user is a fresh file-based install), so it never re-reads the Registry and never
        //     clobbers values the user has changed in the file-based store since.
        public static void MigrateLegacyRegistrySettings()
        {
            // No-op on every non-Windows OS: there is no Windows Registry to migrate from. This runtime
            // guard pairs with the #if WINDOWS compile-time guard below (WindowsPlatformServices, which owns
            // the Registry API, is compiled only for the net8.0-windows target).
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

#if WINDOWS
            try
            {
                // The Windows platform layer owns every Registry call; that Windows API stays confined there.
                AgOpenGPS.Platform.WindowsPlatformServices platform = new AgOpenGPS.Platform.WindowsPlatformServices();

                // Idempotency guard: the cross-platform config file is written exactly once — by this seed on
                // the first post-upgrade run, or by RegistrySettings itself on a fresh install. If it already
                // exists the migration is complete, so re-seeding from the (now stale) Registry would
                // overwrite changes the user has since made. RegistrySettings keeps this same file, at this
                // same fixed location, directly under AppDataRoot.
                string configFilePath = Path.Combine(platform.AppDataRoot, "RegistrySettings.xml");
                if (File.Exists(configFilePath))
                    return;

                // Nothing to migrate when the legacy key was never created (e.g. a brand-new Windows install
                // that never ran a net48 build).
                if (!platform.LegacyRegistryExists())
                    return;

                // Fold each legacy value forward into the file-based store via the public
                // RegistrySettings.Save read-modify-write. Absent Registry values come back null and are
                // skipped, leaving RegistrySettings.Load() to repair them to their original defaults — the
                // same outcome the old code produced when a Registry key was missing.
                SeedLegacyRegistryValue(platform, RegKeys.workingDirectory);
                SeedLegacyRegistryValue(platform, RegKeys.vehicleProfileName);
                SeedLegacyRegistryValue(platform, RegKeys.toolProfileName);
                SeedLegacyRegistryValue(platform, RegKeys.environmentFileName);
                SeedLegacyRegistryValue(platform, RegKeys.vehicleFileName);
                SeedLegacyRegistryValue(platform, RegKeys.language);

                Log.EventWriter("Registry -> One-time legacy Registry settings migrated to RegistrySettings.xml");
            }
            catch (Exception ex)
            {
                // A failed seed must never block startup: RegistrySettings.Load() simply falls back to its
                // defaults, exactly as it does for a missing Registry key. Log the failure and continue.
                Log.EventWriter("Registry -> Legacy Registry settings migration failed: " + ex.ToString());
            }
#endif
        }

#if WINDOWS
        // [XPLAT] Reads a single legacy Registry value through the Windows platform layer and, when present,
        // persists it into the cross-platform config file via the existing RegistrySettings.Save dispatch.
        // A null result means the value was absent from the Registry; it is left for RegistrySettings.Load()
        // to default, matching the original behavior where a missing Registry key read back as null. Gated by
        // #if WINDOWS so it is never an unused member on the net8.0 (Linux/macOS) build.
        private static void SeedLegacyRegistryValue(AgOpenGPS.Platform.WindowsPlatformServices platform, string valueName)
        {
            string registryValue = platform.ReadLegacyRegistryValue(valueName);
            if (registryValue != null)
                RegistrySettings.Save(valueName, registryValue);
        }
#endif
    }
}
