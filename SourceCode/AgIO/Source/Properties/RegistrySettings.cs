// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Platform;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
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
    /// <c>HKCU\SOFTWARE\AgOpenGPS</c> and derived the data directory from <c>MyDocuments</c>. The Windows
    /// Registry is unavailable on Linux/macOS, so per AAP §0.6.3/§0.6.5 the backing is replaced by a small XML
    /// file (<c>AgIO.RegistryConfig.xml</c>) under the cross-platform application-data / configuration root
    /// supplied by <see cref="IPlatformServices.AppDataRoot"/> (Windows <c>%AppData%\AgOpenGPS</c>, Linux
    /// <c>~/.config/AgOpenGPS</c>, macOS <c>~/Library/Application Support/AgOpenGPS</c>). The platform-services
    /// instance is injected via <see cref="PlatformServices"/> by the Avalonia bootstrap; until it is wired up,
    /// <see cref="AppDataRoot"/> resolves the same per-OS location itself so the bootstrap store always loads.
    /// The key names, defaults, null/empty auto-creation, and the public API
    /// (<see cref="Load"/>/<see cref="Save"/>/<see cref="Reset"/> and every static field) are preserved exactly,
    /// so the round-trip semantics are identical. On Windows a one-time migration-read seeds the new store from
    /// the legacy registry key. The split settings XML schema and the <c>Properties.Settings.Default.Load()</c>
    /// profile round-trip (owned by <c>Settings.cs</c>) are untouched.
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

        /// <summary>
        /// [XPLAT] Cross-platform platform-services abstraction (config root, brightness, serial enumeration,
        /// single-instance) defined in <c>AgOpenGPS.Core</c>. The Avalonia bootstrap assigns this when an
        /// instance is available; while it is <see langword="null"/> the <see cref="AppDataRoot"/> fallback
        /// resolves the documented per-OS application-data location, so the factory is never recreated here.
        /// </summary>
        public static IPlatformServices PlatformServices { get; set; }

        /// <summary>
        /// [XPLAT] Optional hook through which the Avalonia UI layer surfaces a fatal settings error in a dialog
        /// (replacing the former WinForms <c>MessageBox</c>) without coupling this bootstrap to any UI type
        /// beyond the Core interface. May be <see langword="null"/> at very early startup; the fatal path logs
        /// and exits regardless.
        /// </summary>
        public static IErrorPresenter ErrorPresenter { get; set; }

        // [XPLAT] Root element name for the cross-platform XML key/value store that replaces the
        // HKCU\SOFTWARE\AgOpenGPS registry subkey.
        private const string rootElementName = "AgIO";

        // [XPLAT] File name of the AgIO bootstrap store. It lives directly under AppDataRoot (NOT under the
        // working/base directory) because WorkingDirectory is itself one of the stored values and determines
        // baseDirectory — storing the config there would be circular. The name is AgIO-specific so it never
        // collides with AgOpenGPS's own store under the shared application-data root.
        private const string storeFileName = "AgIO.RegistryConfig.xml";

        /// <summary>
        /// [XPLAT] Per-user application-data / configuration root. Prefers the injected
        /// <see cref="IPlatformServices.AppDataRoot"/>; otherwise falls back to the same documented per-OS
        /// location so the store resolves even before the platform layer is wired up.
        /// </summary>
        private static string AppDataRoot => PlatformServices?.AppDataRoot ?? DefaultAppDataRoot();

        // [XPLAT] Self-contained mirror of the IPlatformServices documented mapping used when no instance has
        // been injected: Windows %AppData%\AgOpenGPS, Linux ~/.config/AgOpenGPS, macOS
        // ~/Library/Application Support/AgOpenGPS.
        private static string DefaultAppDataRoot()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "AgOpenGPS");
            }

            // SpecialFolder.ApplicationData => %AppData% on Windows and ~/.config on Linux.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgOpenGPS");
        }

        /// <summary>
        /// [XPLAT] Absolute path of the cross-platform bootstrap store, the file-based replacement for the
        /// <c>HKCU\SOFTWARE\AgOpenGPS</c> registry key. Resolution only (no side effects); the containing
        /// directory is created by <see cref="SaveStore"/> before a write.
        /// </summary>
        private static string ConfigRootFile => Path.Combine(AppDataRoot, storeFileName);

        // [XPLAT] Loads the store document, or returns an empty root when the file is missing/unreadable —
        // the file-based analogue of Registry.CurrentUser.CreateSubKey (which never returns null).
        private static XDocument LoadStore()
        {
            string storePath = ConfigRootFile;
            if (File.Exists(storePath))
            {
                try
                {
                    // [XPLAT] SEC-6 (CWE-611 XXE): harden the load against DTD / external-entity processing.
                    // Although this is local app-data, parsing with DtdProcessing.Prohibit and a null XmlResolver
                    // ensures a tampered or corrupted store file cannot trigger external-entity expansion —
                    // consistent with the KML/ISOXML parser hardening already applied elsewhere in the suite.
                    XmlReaderSettings xmlSettings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                    };
                    using (XmlReader reader = XmlReader.Create(storePath, xmlSettings))
                    {
                        return XDocument.Load(reader);
                    }
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Settings -> Store unreadable, recreating: " + ex.Message);
                }
            }

            return new XDocument(new XElement(rootElementName));
        }

        // [XPLAT] Persists the store document, creating the application-data root on demand — the analogue of
        // writing back the registry subkey.
        private static void SaveStore(XDocument doc)
        {
            string root = AppDataRoot;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            doc.Save(ConfigRootFile);
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
#if WINDOWS
                // [XPLAT] One-time Windows migration: seed the cross-platform store from the legacy
                // HKCU\SOFTWARE\AgOpenGPS registry key, but only when the new store does not yet exist.
                // Compiled exclusively under the net8.0-windows target (the WINDOWS symbol); the plain
                // net8.0 build never references Microsoft.Win32.
                if (OperatingSystem.IsWindows() && !File.Exists(ConfigRootFile))
                {
                    MigrateFromRegistry();
                }
#endif

                // [XPLAT] open (or create) the cross-platform store, mirroring CreateSubKey semantics.
                XDocument doc = LoadStore();
                bool dirty = false;

                if (string.IsNullOrEmpty(ReadValue(doc, RegKeys.workingDirectory)))
                {
                    WriteValue(doc, RegKeys.workingDirectory, defaultString);
                    dirty = true;
                    Log.EventWriter("Settings -> WorkingDirectory was null");
                }
                workingDirectory = ReadValue(doc, RegKeys.workingDirectory);

                //Profile File Name from store
                if (ReadValue(doc, RegKeys.profileName) == null)
                {
                    WriteValue(doc, RegKeys.profileName, "");
                    dirty = true;
                    Log.EventWriter("Settings -> Profile Name was null and Created");
                }
                profileName = ReadValue(doc, RegKeys.profileName);

                //Culture from store
                if (string.IsNullOrEmpty(ReadValue(doc, RegKeys.language)))
                {
                    WriteValue(doc, RegKeys.language, "en");
                    dirty = true;
                    Log.EventWriter("Settings -> Culture was null and Created");
                }
                culture = ReadValue(doc, RegKeys.language);

                if (dirty)
                {
                    SaveStore(doc);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Settings -> Catch, Serious Problem Creating config store: " + ex.ToString());
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
                XDocument doc = LoadStore();
                WriteValue(doc, name, value);
                SaveStore(doc);
                Log.EventWriter("Settings -> Key " + name + " Saved to config store with value: " + value);

                if (name == RegKeys.profileName)
                    profileName = value;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Settings -> Catch, Serious Problem Saving keys: " + ex.ToString());
            }
        }

        public static void Reset()
        {
            try
            {
                string storePath = ConfigRootFile;
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }

#if WINDOWS
                // [XPLAT] best-effort: also clear the legacy registry store on Windows so a reset is complete.
                if (OperatingSystem.IsWindows())
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\AgOpenGPS", throwOnMissingSubKey: false);
                }
#endif

                Log.EventWriter("Settings -> Resetting config store and Full Default Reset");
            }
            catch (Exception ex)
            {
                Log.EventWriter("Settings -> Catch, Serious Problem Resetting config store: " + ex.ToString());

                Log.FileSaveSystemEvents();

                // [XPLAT] The WinForms build showed a blocking MessageBox here; on a headless/cross-platform
                // process that is not viable, so the fatal message is routed through the optional Core
                // IErrorPresenter (when the UI has wired one up) and always echoed to stderr, preserving the
                // original fatal-exit outcome without any WinForms dependency.
                ErrorPresenter?.PresentTimedMessage(
                    TimeSpan.FromSeconds(5),
                    "Critical Settings Error",
                    "Can't delete the configuration store");
                Console.Error.WriteLine("Critical Settings Error: Can't delete the configuration store");
                Environment.Exit(0);
            }
        }

        private static void CreateDirectories()
        {
            try
            {
                if (workingDirectory == defaultString)
                {
                    // [XPLAT] default data root is the cross-platform application-data root (was
                    // MyDocuments\AgOpenGPS). AppDataRoot already ends in "AgOpenGPS", so it is NOT
                    // suffixed again (AAP §0.6.5).
                    baseDirectory = AppDataRoot;
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
                //get the profile directory, if not exist, create
                profileDirectory = Path.Combine(baseDirectory, "AgIO");

                //create profile directory if not exist
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

#if WINDOWS
        /// <summary>
        /// [XPLAT] Windows-only one-time read of the legacy <c>HKCU\SOFTWARE\AgOpenGPS</c> registry key,
        /// copying any <c>WorkingDirectory</c>/<c>ProfileName</c>/<c>Language</c> values into the new
        /// cross-platform store. Compiled only under the <c>net8.0-windows</c> target (the <c>WINDOWS</c>
        /// symbol); the plain <c>net8.0</c> build never references <c>Microsoft.Win32</c>.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void MigrateFromRegistry()
        {
            try
            {
                using Microsoft.Win32.RegistryKey regKey =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AgOpenGPS");
                if (regKey == null)
                {
                    return;
                }

                XDocument doc = LoadStore();
                bool dirty = false;

                object workingDirectoryValue = regKey.GetValue(RegKeys.workingDirectory);
                if (workingDirectoryValue != null)
                {
                    WriteValue(doc, RegKeys.workingDirectory, workingDirectoryValue.ToString());
                    dirty = true;
                }

                object profileNameValue = regKey.GetValue(RegKeys.profileName);
                if (profileNameValue != null)
                {
                    WriteValue(doc, RegKeys.profileName, profileNameValue.ToString());
                    dirty = true;
                }

                object languageValue = regKey.GetValue(RegKeys.language);
                if (languageValue != null)
                {
                    WriteValue(doc, RegKeys.language, languageValue.ToString());
                    dirty = true;
                }

                if (dirty)
                {
                    SaveStore(doc);
                    Log.EventWriter("Settings -> Windows one-time Registry migration completed");
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Settings -> Windows Registry migration failed: " + ex.Message);
            }
        }
#endif
    }
}
