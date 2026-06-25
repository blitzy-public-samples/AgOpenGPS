// [XPLAT] migrated from net48/WinForms (Windows Registry backing) — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgLibrary.Settings;
using System;
using System.IO;
using System.Xml.Linq;

namespace AgIO
{
    public static class RegKeys
    {
        public const string profileName = "ProfileName";
        public const string workingDirectory = "WorkingDirectory";
        public const string language = "Language";
    }

    /// <summary>
    /// Cross-platform replacement for the former Windows-Registry-backed AgIO bootstrap store.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The WinForms build persisted three bootstrap values — <see cref="RegKeys.workingDirectory"/>,
    /// <see cref="RegKeys.profileName"/>, and <see cref="RegKeys.language"/> — under the Windows Registry key
    /// <c>HKCU\SOFTWARE\AgOpenGPS</c>, shared by AgOpenGPS and AgIO. The Windows Registry is unavailable on
    /// Linux/macOS, so per AAP §0.6.3/§0.3.4 the backing is replaced by a small XML file under the
    /// cross-platform application-data root (<see cref="Environment.SpecialFolder.ApplicationData"/> →
    /// <c>%AppData%\AgOpenGPS</c> on Windows, <c>~/.config/AgOpenGPS</c> on Linux,
    /// <c>~/Library/Application Support/AgOpenGPS</c> on macOS). The key names, defaults, null/empty
    /// auto-creation, and the public API (<see cref="Load"/>/<see cref="Save"/>/<see cref="Reset"/> and every
    /// static field) are preserved exactly, so the round-trip semantics are identical and the shared store
    /// remains common to both programs once AgOpenGPS adopts the same path in its own checkpoint. The split
    /// settings XML schema and the <c>Properties.Settings.Default.Load()</c> profile round-trip are untouched.
    /// </remarks>
    public static class RegistrySettings
    {
        public const string defaultString = "Default";

        public static string culture = "en";
        public static string workingDirectory = "Default";
        public static string baseDirectory;
        public static string profileDirectory;
        public static string logsDirectory;
        public static string profileName = "";
        public static LoadResult profileLoadResult = LoadResult.MissingFile;
        public static bool profileLoadedFromBackup = false;

        // [XPLAT] Root element name for the cross-platform XML key/value store that replaces the
        // HKCU\SOFTWARE\AgOpenGPS registry subkey.
        private const string rootElementName = "AgOpenGPS";

        /// <summary>
        /// [XPLAT] Resolves the cross-platform path of the shared bootstrap store
        /// (<c>&lt;ApplicationData&gt;/AgOpenGPS/registry.xml</c>), the file-based replacement for the
        /// <c>HKCU\SOFTWARE\AgOpenGPS</c> registry key. The containing directory is created on demand.
        /// </summary>
        private static string GetStorePath()
        {
            string appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgOpenGPS");

            if (!Directory.Exists(appDataRoot))
            {
                Directory.CreateDirectory(appDataRoot);
            }

            return Path.Combine(appDataRoot, "registry.xml");
        }

        // [XPLAT] Loads the store document, or creates an empty root if the file is missing/unreadable —
        // the file-based analogue of Registry.CurrentUser.CreateSubKey (which never returns null).
        private static XDocument LoadStore(string storePath)
        {
            if (File.Exists(storePath))
            {
                try
                {
                    return XDocument.Load(storePath);
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Config -> Store unreadable, recreating: " + ex.Message);
                }
            }

            return new XDocument(new XElement(rootElementName));
        }

        // [XPLAT] Reads a key's value, or null when absent — the analogue of RegistryKey.GetValue(name).
        private static string ReadValue(XDocument doc, string name)
        {
            return doc.Root?.Element(name)?.Value;
        }

        // [XPLAT] Writes (creating or replacing) a key's value — the analogue of RegistryKey.SetValue(name, value).
        private static void WriteValue(XDocument doc, string name, string value)
        {
            XElement element = doc.Root.Element(name);
            if (element == null)
            {
                doc.Root.Add(new XElement(name, value ?? string.Empty));
            }
            else
            {
                element.Value = value ?? string.Empty;
            }
        }

        public static void Load()
        {
            try
            {
                // [XPLAT] open (or create) the cross-platform store, mirroring CreateSubKey semantics.
                string storePath = GetStorePath();
                XDocument doc = LoadStore(storePath);
                bool dirty = false;

                if (ReadValue(doc, RegKeys.workingDirectory) == null || ReadValue(doc, RegKeys.workingDirectory) == "")
                {
                    WriteValue(doc, RegKeys.workingDirectory, defaultString);
                    dirty = true;
                    Log.EventWriter("Config -> Key workingDirectory was null");
                }
                workingDirectory = ReadValue(doc, RegKeys.workingDirectory);

                //Profile File Name from store
                if (ReadValue(doc, RegKeys.profileName) == null)
                {
                    WriteValue(doc, RegKeys.profileName, "");
                    dirty = true;
                    Log.EventWriter("Config -> Key Profile Name was null and Created");
                }
                profileName = ReadValue(doc, RegKeys.profileName);

                //Culture from store
                if (ReadValue(doc, RegKeys.language) == null || ReadValue(doc, RegKeys.language) == "")
                {
                    WriteValue(doc, RegKeys.language, "en");
                    dirty = true;
                    Log.EventWriter("Config -> Culture was null and Created");
                }
                culture = ReadValue(doc, RegKeys.language);

                if (dirty)
                {
                    doc.Save(storePath);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Config -> Catch, Serious Problem Creating config store: " + ex.ToString());
                Reset();
            }

            //make sure directories exist and are in right place if not default workingDir
            CreateDirectories();

            Log.CheckLogSize(Path.Combine(logsDirectory, "AgIO_Events_Log.txt"));

            profileLoadResult = Properties.Settings.Default.Load();
            if (profileLoadResult != LoadResult.Ok)
            {
                Log.EventWriter($"AgIO profile load failed: '{profileName}' ({profileLoadResult})");
            }
        }

        public static void Save(string name, string value)
        {
            try
            {
                string storePath = GetStorePath();
                XDocument doc = LoadStore(storePath);
                WriteValue(doc, name, value);
                doc.Save(storePath);
                Log.EventWriter("Config -> Key " + name + " Saved to config store with value: " + value);

                if (name == RegKeys.profileName)
                    profileName = value;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Config -> Catch, Serious Problem Saving keys: " + ex.ToString());
            }
        }

        public static void Reset()
        {
            try
            {
                string storePath = GetStorePath();
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }

                Log.EventWriter("Config -> Resetting config store and Full Default Reset");
            }
            catch (Exception ex)
            {
                Log.EventWriter("Config -> Catch, Serious Problem Resetting config store: " + ex.ToString());

                Log.FileSaveSystemEvents();

                // [XPLAT] The WinForms build showed a blocking MessageBox here; on a headless/cross-platform
                // process that is not viable, so the failure is logged (above) and the process exits, matching
                // the original fatal-exit outcome.
                Environment.Exit(0);
            }
        }

        private static void CreateDirectories()
        {
            try
            {
                if (workingDirectory == defaultString)
                {
                    baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AgOpenGPS");
                }
                else //user set to other
                {
                    baseDirectory = Path.Combine(workingDirectory, "AgOpenGPS");
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

            try
            {
                logsDirectory = Path.Combine(baseDirectory, "Logs");
                //create Logs directory if not exist
                if (!string.IsNullOrEmpty(logsDirectory) && !Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                    Log.EventWriter("Logs Dir Created\r");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Logs Directory: " + ex.ToString());
            }

            try
            {
                //get the Documents directory, if not exist, create
                profileDirectory = Path.Combine(baseDirectory, "AgIO");

                //create Logs directory if not exist
                if (!string.IsNullOrEmpty(profileDirectory) && !Directory.Exists(profileDirectory))
                {
                    Directory.CreateDirectory(profileDirectory);
                    Log.EventWriter("Profile Dir Created\r");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch, Serious Problem Making Profile Directory: " + ex.ToString());
            }
        }
    }
}
