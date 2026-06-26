// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using Avalonia;

namespace AgDiag
{
    /// <summary>
    /// Application entry point for the AgDiag diagnostics tool.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Re-platformed from the net48 Windows Forms bootstrap to a cross-platform Avalonia
    /// desktop entry point. Two Windows-only mechanisms were replaced:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     The Windows Forms run loop (<c>Application.Run</c> over the <c>FormLoop</c> form) is
    ///     replaced by the Avalonia classic-desktop lifetime (<see cref="BuildAvaloniaApp"/> +
    ///     <c>StartWithClassicDesktopLifetime</c>). The single main window (<c>FormLoop</c>, now an
    ///     Avalonia <c>Window</c>) is created by <c>App.OnFrameworkInitializationCompleted</c>, so it
    ///     is intentionally <b>not</b> constructed here.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     The named <c>Mutex</c> previously used for single-instance enforcement is replaced by a
    ///     <b>local</b> cross-platform exclusive lock-file guard
    ///     (<see cref="TryAcquireSingleInstanceLock"/>). AgDiag has no project reference to
    ///     <c>AgOpenGPS.Core</c>, so it deliberately does not use <c>IPlatformServices</c> and stays
    ///     fully standalone. The historical single-instance identity GUID
    ///     <c>{8F6F0AC4-B9A7-55fd-A8CF-72F04E6BDE8F}</c> is preserved as the lock-file name.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    internal static class Program
    {
        // [XPLAT] Process-lifetime handle for the cross-platform single-instance lock file. The
        // FileStream is held open (FileShare.None) for as long as this process runs; a second AgDiag
        // process attempting to open the same file fails, which is how single-instance is enforced.
        private static FileStream _singleInstanceLock;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">Command-line arguments forwarded to the Avalonia desktop lifetime.</param>
        /// <returns>
        /// The process exit code. <c>0</c> when a second instance exits quietly because another
        /// instance already holds the single-instance lock; otherwise the exit code produced by the
        /// Avalonia classic-desktop lifetime.
        /// </returns>
        /// <remarks>
        /// [STAThread] is retained because Avalonia on Windows requires an STA thread for clipboard
        /// and drag-and-drop interop. The method returns <see cref="int"/> (rather than the former
        /// <c>void</c>) so the lifetime's exit code propagates to the operating system.
        /// </remarks>
        [STAThread]
        public static int Main(string[] args)
        {
            // [XPLAT] Single-instance gate. Mirrors the old behavior where a second instance never
            // started its run loop — here we simply exit quietly with success code 0.
            if (!TryAcquireSingleInstanceLock())
            {
                return 0; // another instance is already running
            }

            try
            {
                // [XPLAT] Replaces the Windows Forms run loop; the main window is created by
                // App.OnFrameworkInitializationCompleted, not here.
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                // Release the lock on normal shutdown so a subsequent launch can acquire it. The OS
                // also releases the handle automatically if the process terminates abnormally.
                _singleInstanceLock?.Dispose();
            }
        }

        /// <summary>
        /// Builds and configures the Avalonia application. Also referenced by the Avalonia visual
        /// designer, so its signature must remain <c>public static AppBuilder</c> and is left unchanged.
        /// </summary>
        /// <returns>A configured <see cref="AppBuilder"/> for <see cref="App"/>.</returns>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// Attempts to acquire the process-wide single-instance lock using a cross-platform exclusive
        /// lock file.
        /// </summary>
        /// <returns>
        /// <c>true</c> if this process acquired the lock and may run; <c>false</c> if another instance
        /// already holds the lock (or the lock could not be created).
        /// </returns>
        /// <remarks>
        /// [XPLAT] A named system mutex does not provide reliable system-wide semantics on Unix, so an
        /// advisory lock file opened with <see cref="FileShare.None"/> is used instead. .NET honors
        /// <see cref="FileShare.None"/> on every platform (Windows file sharing plus Unix advisory
        /// <c>flock</c>), so a second instance's open call throws <see cref="IOException"/>. The lock
        /// path is built exclusively with <see cref="Path.GetTempPath"/> and
        /// <see cref="Path.Combine(string, string)"/> — no hard-coded separators, no Windows-specific
        /// application-data location, and no drive-letter literals — so it resolves correctly on
        /// Windows, Linux, and macOS.
        /// </remarks>
        private static bool TryAcquireSingleInstanceLock()
        {
            try
            {
                // Group the lock file under a shared cross-platform "AgOpenGPS" temp folder.
                string lockDir = Path.Combine(Path.GetTempPath(), "AgOpenGPS");
                Directory.CreateDirectory(lockDir);

                // Reuse the historical net48 single-instance GUID (braces included) as the identity.
                string lockPath = Path.Combine(lockDir, "AgDiag-{8F6F0AC4-B9A7-55fd-A8CF-72F04E6BDE8F}.lock");

                // FileShare.None makes a concurrent open from a second instance fail with IOException.
                _singleInstanceLock = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                return true;
            }
            catch (IOException)
            {
                // The lock file is already held by another running instance.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // The temp location is not writable; treat as "cannot start" rather than launching a
                // second uncontrolled instance.
                return false;
            }
        }
    }
}
