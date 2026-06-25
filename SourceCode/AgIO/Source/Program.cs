// [XPLAT] migrated from net48/WinForms (Application.Run(new FormLoop()) + WinForms single-instance) — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace AgIO
{
    internal static class Program
    {
        private static Mutex _mutex;

        // [XPLAT] Single-instance identity preserved byte-for-byte from the WinForms build so a mixed
        // fleet (and the AgOpenGPS<->AgIO two-program contract) keeps the same guard name.
        private const string SingleInstanceName = "{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}";

        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString(3); // Major.Minor.Patch

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
        /// [XPLAT] Replaces <c>Application.Run(new FormLoop())</c> with the Avalonia classic-desktop
        /// lifetime. The single-instance guard uses a .NET named <see cref="Mutex"/>, which the runtime
        /// supports on Windows, Linux, and macOS, preserving the original GUID and "only one AgIO" semantics
        /// without a Windows-only API. Culture is applied from the persisted profile before any UI or file
        /// I/O so numeric formatting stays consistent across OS locales (AAP §0.6.5).
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            _mutex = new Mutex(true, SingleInstanceName, out bool mutexCreated);

            if (!mutexCreated)
            {
                // [XPLAT] Another AgIO instance already owns the guard — exit quietly, matching the WinForms
                // behaviour where the second instance never created a FormLoop.
                Log.EventWriter("AgIO already running - second instance exiting");
                return;
            }

            try
            {
                //load the profile name and set profile directory
                RegistrySettings.Load();

                Log.EventWriter("Program Started: " + DateTime.Now.ToString("f", CultureInfo.InvariantCulture));
                Log.EventWriter("AgIO Version: " + Version);

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

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>Application.Restart()</c>. The original
        /// released the single-instance Mutex before restarting so the relaunched instance would not fail the
        /// guard; that ordering is preserved here. Because Avalonia has no <c>Application.Restart()</c>, the
        /// current executable is relaunched as a new process and the running classic-desktop lifetime is then
        /// shut down.
        /// </summary>
        public static void Restart()
        {
            if (_mutex == null)
            {
                return;
            }

            ReleaseMutex();

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = false,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Restart relaunch error: " + ex.Message);
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
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
