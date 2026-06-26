// [XPLAT] migrated from net48/WinForms (Application.Run(new FormGPS()) + WinForms single-instance) — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using Avalonia;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace AgOpenGPS
{
    internal static class Program
    {
        // [XPLAT] Single-instance guard. Replaces the WinForms Mutex+Application.Run pattern. A .NET named
        // Mutex is supported on Windows, Linux, and macOS, so the original "only one AgOpenGPS" semantics and
        // the exact guard GUID are preserved without any Windows-only API.
        private static Mutex _mutex;

        // [XPLAT] Single-instance identity preserved byte-for-byte from the WinForms build so a mixed fleet
        // (and the AgOpenGPS<->AgIO two-program contract) keeps the same guard name.
        private const string SingleInstanceName = "{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}";

        // [XPLAT] Product version string. The WinForms build read System.Windows.Forms.Application.ProductVersion,
        // which returns the entry assembly's AssemblyInformationalVersionAttribute (falling back to the assembly
        // version). That attribute is read directly here so the value — and the SemVer/IsPreRelease/IsDevelopVersion
        // derivations below — stay identical without referencing System.Windows.Forms.
        private static readonly string ProductVersion =
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "1.0.0.0";

        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString(3); // Major.Minor.Patch
        public static readonly string SemVer = ProductVersion.Split('+').First();
        public static readonly bool IsPreRelease = ProductVersion.Contains('-');
        public static readonly bool IsDevelopVersion = ProductVersion == "1.0.0.0";

        /// <summary>
        /// [XPLAT] Avalonia configuration used by both the runtime entry point and the XAML previewer.
        /// Replaces the WinForms <c>Application.EnableVisualStyles()</c>/<c>SetCompatibleTextRenderingDefault</c>
        /// setup. The Inter font is registered here so <c>App.axaml</c> needs no font include.
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces <c>Application.Run(new FormGPS())</c> with the Avalonia classic-desktop lifetime.
        /// The single-instance guard uses a .NET named <see cref="Mutex"/>; when a second instance fails to
        /// create the guard it exits quietly (matching the WinForms build, where the second instance never
        /// created a FormGPS), mirroring the sibling AgIO program. Culture is applied from the persisted
        /// profile before any UI or file I/O so numeric formatting stays consistent across OS locales
        /// (AAP §0.6.5).
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            _mutex = new Mutex(true, SingleInstanceName, out bool mutexCreated);

            if (!mutexCreated)
            {
                // [XPLAT] Another AgOpenGPS instance already owns the guard — exit quietly. The WinForms build
                // showed a "AgOpenGPS is Already Running" warning via the now-removed WinForms FormDialog; the
                // user-facing notice belongs to the Avalonia UI shell (later checkpoint) and the core
                // single-instance behaviour is preserved here exactly as in the sibling AgIO program.
                Log.EventWriter("AgOpenGPS already running - second instance exiting");
                return;
            }

            try
            {
                // Load the profile name and set profile directory.
                RegistrySettings.Load();

                Log.EventWriter("Program Started: " + DateTime.Now.ToString("f", CultureInfo.InvariantCulture));
                Log.EventWriter("AgOpenGPS Version: " + Version);

                // [XPLAT] Apply the persisted culture to the current thread AND the default thread cultures so
                // numeric parse/format is consistent on every OS locale (AAP §0.6.5); the WinForms build set
                // only the current thread, which is insufficient once background threads do file/protocol I/O.
                var culture = new CultureInfo(RegistrySettings.culture);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                ReleaseMutex();
            }
        }

        // [XPLAT] Release + dispose the single-instance Mutex exactly once.
        private static void ReleaseMutex()
        {
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned on this thread */ }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
