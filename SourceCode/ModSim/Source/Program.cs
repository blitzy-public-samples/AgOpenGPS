// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using System;
using System.Threading;

namespace ModSim
{
    /// <summary>
    /// Application entry point for the standalone ModSim module simulator.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Re-platformed from the net48 Windows Forms bootstrap to a cross-platform Avalonia
    /// desktop entry point. The Windows Forms run loop (<c>Application.Run(new FormSim())</c>) is
    /// replaced by the Avalonia classic-desktop lifetime (<see cref="BuildAvaloniaApp"/> +
    /// <c>StartWithClassicDesktopLifetime</c>); the single main window is created by
    /// <c>App.OnFrameworkInitializationCompleted</c> rather than constructed here. ModSim remains a
    /// standalone module with no project reference to <c>AgOpenGPS.Core</c>, so it intentionally does
    /// not use <c>IPlatformServices</c> and keeps its own local single-instance guard.
    /// </remarks>
    internal static class Program
    {
        // [XPLAT] Local single-instance guard, retained verbatim from the WinForms build. Named system
        // mutexes are supported cross-platform on .NET 8 (Windows, Linux, and macOS), so this stays as
        // ModSim's standalone single-instance mechanism — no Core abstraction is introduced. The
        // historical identity GUID is preserved so the single-instance identity is unchanged. The field
        // is static and readonly so the mutex handle is held for the lifetime of the process.
        private static readonly Mutex Mutex = new Mutex(true, "{8F6F0AC4-B9A1-66fd-A8CF-72F04E6BDE82}");

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">Command-line arguments forwarded to the Avalonia desktop lifetime.</param>
        /// <remarks>
        /// [STAThread] is retained because Avalonia on Windows requires an STA thread for clipboard and
        /// drag-and-drop interop; the attribute is ignored on Linux and macOS. Single-instance behavior
        /// is preserved exactly: the application body runs only when this process acquires the mutex. A
        /// second concurrent instance fails the <see cref="WaitHandle.WaitOne(TimeSpan, bool)"/> call,
        /// falls through the gate, and exits silently with no window — identical to the original
        /// WinForms behavior.
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            if (Mutex.WaitOne(TimeSpan.Zero, true))
            {
                // [XPLAT] Replaces Application.EnableVisualStyles() / SetCompatibleTextRenderingDefault()
                // / Application.Run(new FormSim()) with the Avalonia classic-desktop lifetime.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
        }

        /// <summary>
        /// Builds and configures the Avalonia application. Declared <c>public static</c> so the Avalonia
        /// XAML previewer / visual designer can discover and invoke it.
        /// </summary>
        /// <returns>A configured <see cref="AppBuilder"/> for the ModSim <see cref="App"/>.</returns>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
