// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;

namespace AgOpenGPS.Core.Platform
{
    /// <summary>
    /// Cross-platform abstraction over every operating-system-specific capability that
    /// AgOpenGPS depends on. Defined in the portable <c>AgOpenGPS.Core</c> assembly so that
    /// application and UI code depend only on this interface (Dependency Inversion). The
    /// concrete implementations — <c>WindowsPlatformServices</c>, <c>LinuxPlatformServices</c>
    /// and <c>MacPlatformServices</c> — live in the GPS project; the Windows implementation is
    /// compiled under <c>net8.0-windows</c> so that WMI and Registry calls stay Windows-only.
    /// </summary>
    public interface IPlatformServices
    {
        /// <summary>
        /// Absolute path to the per-user AgOpenGPS application-data / configuration root.
        /// Replaces the former Windows Registry + <c>%AppData%</c> / <c>MyDocuments</c> source.
        /// Expected per-OS mapping: Windows <c>%AppData%\AgOpenGPS</c>, Linux
        /// <c>~/.config/AgOpenGPS</c>, macOS <c>~/Library/Application Support/AgOpenGPS</c>.
        /// Returned as a string; callers wrap it in a <c>DirectoryInfo</c> for
        /// <c>ApplicationCore</c>.
        /// </summary>
        string AppDataRoot { get; }

        /// <summary>
        /// Returns the current monitor brightness percentage (0-100), or <c>-1</c> when no
        /// controllable display exists (for example a desktop monitor, or an OS/display with no
        /// brightness control). Preserves the graceful degradation of the former WMI brightness
        /// controller.
        /// </summary>
        int GetBrightness();

        /// <summary>
        /// Sets the monitor brightness percentage (0-100). Performs no operation when brightness
        /// control is unavailable on the current display or operating system.
        /// </summary>
        void SetBrightness(int brightness);

        /// <summary>
        /// Enumerates the names of the available serial ports (for example <c>COM3</c> on
        /// Windows, <c>/dev/ttyUSB0</c> or <c>/dev/ttyACM0</c> on Linux, <c>/dev/cu.*</c> on
        /// macOS). Only port-name discovery is abstracted here; the serial I/O itself continues
        /// to use the cross-platform <c>System.IO.Ports</c> package unchanged.
        /// </summary>
        IEnumerable<string> GetSerialPortNames();

        /// <summary>
        /// Attempts to acquire the single-instance guard for the program identified by
        /// <paramref name="identifier"/>. Returns <see langword="true"/> with a live
        /// <paramref name="instanceLock"/> when this process is the sole instance; returns
        /// <see langword="false"/> (with a no-op disposable) when another instance already holds
        /// the guard. Disposing the returned guard releases it. Replaces the named Mutex
        /// previously used by AgOpenGPS and AgIO.
        /// </summary>
        /// <param name="identifier">A per-program unique key (the program's instance GUID).</param>
        /// <param name="instanceLock">Receives the guard to dispose on shutdown/restart.</param>
        bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock);
    }
}
