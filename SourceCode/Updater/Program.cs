// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;

namespace AgOpenGPS.Updater
{
    /// <summary>
    /// Main entry point for the AgOpenGPS Updater application.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// [XPLAT] Avalonia application entry point. Replaces the WinForms bootstrap
        /// (<c>Application.EnableVisualStyles()</c> / <c>Application.SetCompatibleTextRenderingDefault(false)</c>
        /// / <c>Application.Run(new FormUpdate(...))</c>). Command-line parsing and window selection now
        /// live in <see cref="App.OnFrameworkInitializationCompleted"/>; the raw args are forwarded to the
        /// classic-desktop lifetime so <c>desktop.Args</c> exposes them there.
        ///
        /// Single-instance: the net48 updater had NO single-instance Mutex in its entry point (it simply
        /// called Application.Run), so — for 1:1 parity — none is added here. (The <c>Global\AgOpenGPS_Updater_Active</c>
        /// Mutex inside <c>UpdateService</c> is a separate signalling mutex used to tell AgOpenGPS not to
        /// shut down mid-update; it is unrelated to single-instance and is left unchanged.)
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        /// <summary>
        /// Configures the Avalonia application builder (used by both the runtime entry point above and
        /// the Avalonia design-time/previewer tooling).
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
