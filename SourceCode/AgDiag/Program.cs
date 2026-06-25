// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using Avalonia;

namespace AgDiag
{
    internal static class Program
    {
        // [XPLAT] Cross-platform single-instance guard. The named Mutex used by the WinForms build
        // ("{8F6F0AC4-B9A7-55fd-A8CF-72F04E6BDE8F}") does not have reliable system-wide semantics on
        // Unix, so an advisory lockfile held open with FileShare.None for the lifetime of the process
        // is used instead. The GUID is preserved to keep the original single-instance identity.
        private const string SingleInstanceId = "8F6F0AC4-B9A7-55fd-A8CF-72F04E6BDE8F";
        private static FileStream _singleInstanceLock;

        // Avalonia configuration, don't remove; also used by the visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the WinForms <c>Application.Run(new FormLoop())</c> bootstrap with the
        /// Avalonia classic-desktop lifetime, while preserving the original single-instance behavior.
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            if (!TryAcquireSingleInstanceLock())
            {
                // Another instance already owns the lock; exit quietly, matching the WinForms behavior
                // where Application.Run was skipped when the mutex could not be acquired.
                return;
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                ReleaseSingleInstanceLock();
            }
        }

        /// <summary>
        /// Attempts to acquire the process-wide single-instance lock.
        /// </summary>
        /// <returns><c>true</c> if this process may run; <c>false</c> if another instance holds the lock.</returns>
        private static bool TryAcquireSingleInstanceLock()
        {
            try
            {
                string lockPath = Path.Combine(Path.GetTempPath(), $"AgDiag-{SingleInstanceId}.lock");

                // FileShare.None ensures a second instance fails to open the same file with an IOException.
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
                // A permission problem on the temp directory should not prevent the diagnostic tool
                // from starting; degrade gracefully by allowing the launch.
                return true;
            }
        }

        /// <summary>
        /// Releases the single-instance lock on shutdown (best effort).
        /// </summary>
        private static void ReleaseSingleInstanceLock()
        {
            try
            {
                _singleInstanceLock?.Dispose();
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS releases the handle on process exit regardless.
            }
            finally
            {
                _singleInstanceLock = null;
            }
        }
    }
}
