// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS.Core.Platform;
using System;
using System.IO;

namespace AgOpenGPS
{
    public static class RegKeys
    {
        // New profile system (v7+) - split Vehicle/Tool/Environment profiles
        public const string vehicleProfileName = "VehicleProfileName";
        public const string toolProfileName = "ToolProfileName";
        public const string environmentFileName = "EnvironmentFileName";

        // Legacy (v6 and earlier) - single profile file
        public const string vehicleFileName = "VehicleFileName";

        public const string workingDirectory = "WorkingDirectory";
        public const string language = "Language";
    }

    public static class RegistrySettings
    {
        public const string defaultString = "Default";
        public static string culture = "en";

        // New profile system values
        public static string vehicleProfileName = "";
        public static string toolProfileName = "";
        public static string environmentFileName = "Default";

        // Legacy value (only for migration detection)
        public static string legacyVehicleFileName = "";

        public static string workingDirectory = "Default";
        public static string vehiclesDirectory;
        public static string toolsDirectory;
        public static string logsDirectory;
        public static string baseDirectory;
        public static string fieldsDirectory;
        public static string environmentDirectory;

        public static LoadResult vehicleProfileLoadResult = LoadResult.MissingFile;
        public static LoadResult toolProfileLoadResult = LoadResult.MissingFile;
        public static bool vehicleProfileLoadedFromBackup = false;
        public static bool toolProfileLoadedFromBackup = false;

        // Indicates if migration from legacy single profile is needed
        public static bool NeedsMigration { get; private set; }

        // [XPLAT] Cross-platform services backing (replaces the Windows Registry + %AppData%/MyDocuments
        // source). Supplies the application-data/config root used both to locate the settings tree and to
        // place the fixed-location config file below. The concrete per-OS implementation is selected by
        // PlatformServicesFactory; any Registry/WMI access stays confined to WindowsPlatformServices.
        private static IPlatformServices _platform;

        // [XPLAT] Optionally called once from Program.Main (after PlatformServicesFactory.Register(...) and
        // before Load()) so this class shares the exact platform instance the rest of the app uses. It is
        // safe to skip — the Platform property below falls back to the factory. Never throws here.
        public static void Initialize(IPlatformServices platform)
        {
            _platform = platform;
        }

        // [XPLAT] Lazily resolves the platform services, falling back to PlatformServicesFactory.Create()
        // when Initialize was not called. Reading AppDataRoot acquires no OS lock, so creating a second
        // instance via the factory is harmless.
        private static IPlatformServices Platform => _platform ??= PlatformServicesFactory.Create();

        // [XPLAT] Fixed cross-platform location of the small config file holding the six legacy "registry"
        // string values (WorkingDirectory, VehicleProfileName, ToolProfileName, EnvironmentFileName,
        // VehicleFileName, Language). It lives directly at AppDataRoot — never under WorkingDirectory — so
        // it is always found on the next launch regardless of where WorkingDirectory redirects
        // baseDirectory, mirroring how the old HKCU\SOFTWARE\AgOpenGPS key lived at a fixed hive location.
        // On Windows, the one-time legacy-Registry seed (WindowsPlatformServices/CSettingsMigration) writes
        // THIS very file before Load() reads it, so this class needs no Registry knowledge.
        private static string ConfigFilePath => Path.Combine(Platform.AppDataRoot, "RegistrySettings.xml");

        public static void Load()
        {
            try
            {
                // [XPLAT] Read the six legacy values from the fixed cross-platform config file instead of
                // HKCU\SOFTWARE\AgOpenGPS. Absent keys load as null (XmlSettingsHandler leaves unmatched
                // fields at their initial value), exactly mirroring the old Registry.GetValue returning
                // null for a missing key. Missing values are repaired to their original defaults and the
                // file is written back once at the end — preserving the original "repair on load" behavior.
                RegistryConfig cfg = new RegistryConfig();
                XmlSettingsHandler.LoadXMLFile(ConfigFilePath, cfg);

                bool repaired = false;

                if (cfg.WorkingDirectory == null || cfg.WorkingDirectory == "")
                {
                    cfg.WorkingDirectory = defaultString;
                    Log.EventWriter("Registry -> Key workingDirectory was null");
                    repaired = true;
                }
                workingDirectory = cfg.WorkingDirectory;

                // NEW: Vehicle Profile Name (v7+)
                if (cfg.VehicleProfileName == null)
                {
                    cfg.VehicleProfileName = "";
                    Log.EventWriter("Registry -> Key vehicleProfileName was null");
                    repaired = true;
                }
                vehicleProfileName = cfg.VehicleProfileName;

                // NEW: Tool Profile Name (v7+)
                if (cfg.ToolProfileName == null)
                {
                    cfg.ToolProfileName = "";
                    Log.EventWriter("Registry -> Key toolProfileName was null");
                    repaired = true;
                }
                toolProfileName = cfg.ToolProfileName;

                // LEGACY: Vehicle File Name (v6 and earlier) - only for migration detection
                if (cfg.VehicleFileName == null)
                {
                    cfg.VehicleFileName = "";
                    Log.EventWriter("Registry -> Key vehicleFileName was null");
                    repaired = true;
                }
                legacyVehicleFileName = cfg.VehicleFileName;

                // Environment File Name Registry Key
                if (cfg.EnvironmentFileName == null)
                {
                    cfg.EnvironmentFileName = "Default";
                    Log.EventWriter("Registry -> Key environmentFileName was null");
                    repaired = true;
                }
                environmentFileName = cfg.EnvironmentFileName;

                //Language Registry Key
                if (cfg.Language == null || cfg.Language == "")
                {
                    cfg.Language = "en";
                    Log.EventWriter("Registry -> Key language was null");
                    repaired = true;
                }
                culture = cfg.Language;

                // [XPLAT] Persist repaired defaults so subsequent launches find every key (the old code
                // wrote each missing key back via RegistryKey.SetValue). Every field is non-null at this
                // point, so the write is safe; SaveXMLFile creates AppDataRoot if it does not yet exist.
                if (repaired)
                {
                    XmlSettingsHandler.SaveXMLFile(ConfigFilePath, cfg);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Registry -> Catch, Serious Problem Creating Registry keys: " + ex.ToString());
                Reset();
            }

            // Detect if migration is needed (has legacy VehicleFileName but no new profile keys)
            NeedsMigration = !string.IsNullOrEmpty(legacyVehicleFileName) &&
                            string.IsNullOrEmpty(vehicleProfileName) &&
                            string.IsNullOrEmpty(toolProfileName);

            if (NeedsMigration)
            {
                Log.EventWriter($"Legacy profile detected: {legacyVehicleFileName}, migration needed");
            }

            //make sure directories exist and are in right place if not default workingDir
            CreateDirectories();

            Log.CheckLogSize(Path.Combine(logsDirectory, "AgOpenGPS_Events_Log.txt"));

            // Load Environment settings
            Properties.Settings.Default.Load();

            // Load Vehicle settings if vehicleProfileName is set
            if (!string.IsNullOrEmpty(vehicleProfileName))
            {
                vehicleProfileLoadResult = Properties.VehicleSettings.Default.Load(vehicleProfileName);
                if (vehicleProfileLoadResult != LoadResult.Ok)
                {
                    Log.EventWriter($"Vehicle profile load failed: {vehicleProfileName} ({vehicleProfileLoadResult})");
                }
            }
            else vehicleProfileLoadResult = LoadResult.MissingFile;

            // Load Tool settings if toolProfileName is set
            if (!string.IsNullOrEmpty(toolProfileName))
            {
                toolProfileLoadResult = Properties.ToolSettings.Default.Load(toolProfileName);
                if (toolProfileLoadResult != LoadResult.Ok)
                {
                    Log.EventWriter($"Tool profile load failed: {toolProfileName} ({toolProfileLoadResult})");
                }
            }
            else toolProfileLoadResult = LoadResult.MissingFile;
        }

        public static void Save(string name, string value)
        {
            try
            {
                // [XPLAT] Read-modify-write the single named value into the cross-platform config file
                // instead of HKCU\SOFTWARE\AgOpenGPS. ReadConfig() loads the existing file and normalizes
                // any absent key to its default so the subsequent write can never encounter a null field.
                RegistryConfig cfg = ReadConfig();

                if (name == RegKeys.vehicleProfileName)
                    vehicleProfileName = value;
                else if (name == RegKeys.toolProfileName)
                    toolProfileName = value;
                else if (name == RegKeys.environmentFileName)
                    environmentFileName = value;
                else if (name == RegKeys.language)
                    culture = value;

                if (name == RegKeys.workingDirectory && value == Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                {
                    SetConfigValue(cfg, name, defaultString);
                    Log.EventWriter("Registry -> Key " + name + " Saved to registry key with value: " + defaultString);
                }
                else//storing the value
                {
                    SetConfigValue(cfg, name, value);
                    Log.EventWriter("Registry -> Key " + name + " Saved to registry key with value: " + value);
                }

                XmlSettingsHandler.SaveXMLFile(ConfigFilePath, cfg);
            }
            catch (Exception ex)
            {
                Log.EventWriter("Registry -> Catch, Unable to save " + name + ": " + ex.ToString());
            }
        }

        public static void Reset()
        {
            try
            {
                // [XPLAT] Delete the cross-platform config file and reset the in-memory values to their
                // defaults (replaces Registry.CurrentUser.DeleteSubKeyTree). The next Load() recreates the
                // file from these defaults. The one-time legacy-Registry read is delegated to the
                // Windows-only layer, so there is nothing Registry-related to clear here.
                string configFile = ConfigFilePath;
                if (File.Exists(configFile))
                {
                    File.Delete(configFile);
                }

                workingDirectory = defaultString;
                vehicleProfileName = "";
                toolProfileName = "";
                environmentFileName = "Default";
                legacyVehicleFileName = "";
                culture = "en";

                Log.EventWriter("Registry -> Resetting config file and Full Default Reset");
            }
            catch (Exception ex)//program will crash anyways!
            {
                Log.EventWriter("Registry -> Catch, Serious Problem Resetting config file: " + ex.ToString());

                Log.FileSaveSystemEvents();

                // [XPLAT] This runs pre-UI, so log the critical settings error instead of showing the
                // former WinForms FormDialog, then exit as before. Do not introduce any UI type here.
                Log.EventWriter("Registry -> Critical Settings Error: Can't delete the config file");

                Environment.Exit(0);
            }
        }

        private static void CreateDirectories()
        {
            try
            {
                if (workingDirectory == defaultString)
                {
                    // [XPLAT] Default data root comes from the platform services. AppDataRoot already
                    // includes the trailing "AgOpenGPS" leaf on every OS (Windows %AppData%\AgOpenGPS,
                    // Linux ~/.config/AgOpenGPS, macOS ~/Library/Application Support/AgOpenGPS), so it must
                    // NOT be re-appended here. (Replaces the former MyDocuments\AgOpenGPS Windows path.)
                    baseDirectory = Platform.AppDataRoot;
                }
                else //user set to other
                {
                    baseDirectory = Path.Combine(workingDirectory, "AgOpenGPS");
                }

                // Ensure baseDirectory exists
                if (!string.IsNullOrEmpty(baseDirectory) && !Directory.Exists(baseDirectory))
                {
                    Directory.CreateDirectory(baseDirectory);
                    Log.EventWriter("Base directory created: " + baseDirectory);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Working Directory: " + ex.ToString());

                if (workingDirectory != defaultString)
                {
                    workingDirectory = defaultString;
                    Save(RegKeys.workingDirectory, defaultString);
                    CreateDirectories();
                    return;
                }
                else//program will crash anyways!
                {
                    Log.FileSaveSystemEvents();
                    Environment.Exit(0);
                }
            }

            //get the vehicles directory, if not exist, create
            try
            {
                vehiclesDirectory = Path.Combine(baseDirectory, "VehicleProfiles");
                if (!string.IsNullOrEmpty(vehiclesDirectory) && !Directory.Exists(vehiclesDirectory))
                {
                    Directory.CreateDirectory(vehiclesDirectory);
                    Log.EventWriter("VehicleProfiles Dir Created");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making VehicleProfiles Directory: " + ex.ToString());
            }

            //get the tools directory, if not exist, create
            try
            {
                toolsDirectory = Path.Combine(baseDirectory, "ToolProfiles");
                if (!string.IsNullOrEmpty(toolsDirectory) && !Directory.Exists(toolsDirectory))
                {
                    Directory.CreateDirectory(toolsDirectory);
                    Log.EventWriter("ToolProfiles Dir Created");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making ToolProfiles Directory: " + ex.ToString());
            }

            //get the environment directory, if not exist, create
            try
            {
                environmentDirectory = Path.Combine(baseDirectory, "Environment");
                if (!string.IsNullOrEmpty(environmentDirectory) && !Directory.Exists(environmentDirectory))
                {
                    Directory.CreateDirectory(environmentDirectory);
                    Log.EventWriter("Environment Dir Created");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Environment Directory: " + ex.ToString());
            }

            //get the fields directory, if not exist, create
            try
            {
                fieldsDirectory = Path.Combine(baseDirectory, "Fields");
                if (!string.IsNullOrEmpty(fieldsDirectory) && !Directory.Exists(fieldsDirectory))
                {
                    Directory.CreateDirectory(fieldsDirectory);
                    Log.EventWriter("Fields Dir Created");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Fields Directory: " + ex.ToString());
            }

            //get the logs directory, if not exist, create
            try
            {
                logsDirectory = Path.Combine(baseDirectory, "Logs");
                if (!string.IsNullOrEmpty(logsDirectory) && !Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                    Log.EventWriter("Logs Dir Created");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Logs Directory: " + ex.ToString());
            }
        }

        // [XPLAT] Loads the cross-platform config file and normalizes every absent key to its original
        // default, guaranteeing all fields are non-null before a write (XmlSettingsHandler.SaveXMLFile
        // dereferences each field's value, so a null field would throw). Used by Save() for its
        // read-modify-write; Load() instead loads raw so it can detect, log and repair missing keys.
        private static RegistryConfig ReadConfig()
        {
            RegistryConfig cfg = new RegistryConfig();
            XmlSettingsHandler.LoadXMLFile(ConfigFilePath, cfg);

            if (cfg.WorkingDirectory == null) cfg.WorkingDirectory = defaultString;
            if (cfg.VehicleProfileName == null) cfg.VehicleProfileName = "";
            if (cfg.ToolProfileName == null) cfg.ToolProfileName = "";
            if (cfg.EnvironmentFileName == null) cfg.EnvironmentFileName = "Default";
            if (cfg.VehicleFileName == null) cfg.VehicleFileName = "";
            if (cfg.Language == null) cfg.Language = "en";

            return cfg;
        }

        // [XPLAT] Maps a legacy registry value name (one of the RegKeys constants) to the matching field on
        // the config holder, mirroring the original RegistryKey.SetValue(name, value) dispatch. An unknown
        // name is ignored, matching the harmless old behavior of writing a registry value nothing reads.
        private static void SetConfigValue(RegistryConfig cfg, string name, string value)
        {
            if (name == RegKeys.workingDirectory)
                cfg.WorkingDirectory = value;
            else if (name == RegKeys.vehicleProfileName)
                cfg.VehicleProfileName = value;
            else if (name == RegKeys.toolProfileName)
                cfg.ToolProfileName = value;
            else if (name == RegKeys.environmentFileName)
                cfg.EnvironmentFileName = value;
            else if (name == RegKeys.vehicleFileName)
                cfg.VehicleFileName = value;
            else if (name == RegKeys.language)
                cfg.Language = value;
        }

        // [XPLAT] Cross-platform replacement for the six HKCU\SOFTWARE\AgOpenGPS string values, persisted by
        // AgLibrary.Settings.XmlSettingsHandler to RegistrySettings.xml at AppDataRoot. The field names match
        // the RegKeys value names exactly so the same identifiers round-trip through the settings file.
        // ToString() is overridden because XmlSettingsHandler uses it as the XML root element name, and the
        // default nested-type name ("AgOpenGPS.RegistrySettings+RegistryConfig") contains '+', which is
        // illegal in an XML element name and would throw during save.
        private sealed class RegistryConfig
        {
            public string WorkingDirectory;
            public string VehicleProfileName;
            public string ToolProfileName;
            public string EnvironmentFileName;
            public string VehicleFileName;
            public string Language;

            public override string ToString()
            {
                return "RegistrySettings";
            }
        }
    }
}
