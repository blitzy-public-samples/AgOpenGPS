// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using AgOpenGPS.Core.Platform;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Linux implementation of <see cref="IPlatformServices"/> for the AgIO comm hub.
    /// Replaces every Windows-only primitive the former net48/WinForms build relied on: the Windows
    /// Registry / <c>%AppData%</c> configuration root becomes the XDG base directory
    /// (<c>$XDG_CONFIG_HOME</c> or <c>~/.config</c>), WMI monitor brightness becomes the graceful
    /// <c>-1</c> "not available" no-op (AgIO has no brightness UI), serial-port discovery enumerates
    /// the <c>/dev</c> device nodes directly, and the named <c>Mutex</c> single-instance guard becomes
    /// an exclusive lock-file held for the process lifetime.
    /// </summary>
    /// <remarks>
    /// Contains no Windows-only APIs (no Registry, no WMI, no <c>Mutex</c>) and no conditional
    /// compilation, so it compiles unchanged on both <c>net8.0</c> and <c>net8.0-windows</c>; AgIO
    /// selects it at runtime through its platform-services factory. All path handling uses
    /// <see cref="Path.Combine(string, string)"/> with exact-case literals and all ordering uses
    /// <see cref="StringComparer.Ordinal"/> so behaviour is identical across OS locales (AAP §0.6.5).
    /// </remarks>
    public sealed class LinuxPlatformServices : IPlatformServices
    {
        // [XPLAT] Per-product folder name reused for the config root and the lock-file directory.
        private const string ApplicationFolderName = "AgOpenGPS";

        /// <summary>
        /// Absolute path to AgOpenGPS' per-user configuration root on Linux, honoring the XDG Base
        /// Directory specification: <c>$XDG_CONFIG_HOME/AgOpenGPS</c> when the variable is set,
        /// otherwise <c>~/.config/AgOpenGPS</c>. The directory is created on access (idempotent) so
        /// callers can wrap it in a <see cref="DirectoryInfo"/> without a separate existence check.
        /// </summary>
        public string AppDataRoot
        {
            get
            {
                // [XPLAT] Linux XDG base dir: $XDG_CONFIG_HOME or ~/.config (replaces %AppData%/Registry).
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(config))
                {
                    config = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config");
                }

                string root = Path.Combine(config, ApplicationFolderName);
                Directory.CreateDirectory(root); // idempotent: ensures the root exists for callers
                return root;
            }
        }

        /// <summary>
        /// Returns <c>-1</c> to signal that no controllable display brightness is available. Linux
        /// brightness control is best-effort / no-op for AgIO (which has no brightness UI), preserving
        /// the graceful "<c>-1</c> == not available" contract of the former WMI brightness controller.
        /// </summary>
        public int GetBrightness() => -1; // [XPLAT] AgIO has no brightness UI; -1 == not available

        /// <summary>
        /// No-op on Linux for AgIO: monitor brightness is not driven from this process, and writing to
        /// <c>/sys/class/backlight</c> requires elevated privileges. Preserves the graceful degradation
        /// of the former WMI controller, which silently ignored displays without brightness control.
        /// </summary>
        public void SetBrightness(int brightness)
        {
            // [XPLAT] Intentionally no-op (see GetBrightness): brightness control is unavailable here.
        }

        /// <summary>
        /// Enumerates the Linux serial device nodes that <c>System.IO.Ports.SerialPort</c> can open —
        /// USB-serial adapters (<c>/dev/ttyUSB*</c>), CDC-ACM devices (<c>/dev/ttyACM*</c>) and callout
        /// nodes (<c>/dev/cu.*</c>, present on some setups). Full device paths are returned in a stable,
        /// culture-invariant order. Never throws: an unreadable <c>/dev</c> degrades to an empty list.
        /// </summary>
        public IEnumerable<string> GetSerialPortNames()
        {
            var ports = new List<string>();

            try
            {
                if (Directory.Exists("/dev"))
                {
                    // [XPLAT] Enumerate device nodes directly (avoids System.IO.Ports name quirks across distros).
                    ports.AddRange(Directory.GetFiles("/dev", "ttyUSB*"));
                    ports.AddRange(Directory.GetFiles("/dev", "ttyACM*"));
                    ports.AddRange(Directory.GetFiles("/dev", "cu.*"));
                }
            }
            catch
            {
                // [XPLAT] /dev unreadable (permissions, sandbox) -> degrade to an empty list; never throw.
            }

            ports.Sort(StringComparer.Ordinal); // deterministic, culture-invariant ordering (CA1310)
            return ports;
        }

        /// <summary>
        /// Acquires the cross-platform single-instance guard for <paramref name="identifier"/> using an
        /// exclusive lock-file under <see cref="AppDataRoot"/>. Unix has no named-Mutex equivalent, so an
        /// <see cref="FileStream"/> opened with <see cref="FileShare.None"/> is the guard: a second
        /// process opening the same path fails with <see cref="IOException"/>. Returns <see langword="true"/>
        /// with a live <see cref="FileInstanceLock"/> for the sole instance, or <see langword="false"/>
        /// with a no-op disposable when the guard is already held. Disposing the returned lock releases
        /// the guard so <c>Program.Restart()</c> can relaunch and immediately re-acquire it.
        /// </summary>
        /// <param name="identifier">Per-program unique key (the program's instance GUID).</param>
        /// <param name="instanceLock">Receives the guard to dispose on shutdown / restart.</param>
        public bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock)
        {
            // [XPLAT] was: named Mutex on Windows; on Linux use an exclusive lock-file (FileShare.None).
            string lockPath = Path.Combine(AppDataRoot, SanitizeFileName(identifier) + ".lock");

            try
            {
                // FileShare.None => a second process opening the same path throws IOException (the guard).
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                if (!OperatingSystem.IsMacOS())
                {
                    // [XPLAT] Advisory record lock where supported (Windows/Linux). FileStream.Lock is
                    // unsupported on macOS, so it is skipped there; FileShare.None remains the real guard.
                    try { stream.Lock(0, 0); } catch { /* advisory only; FileShare.None already guards */ }
                }

                instanceLock = new FileInstanceLock(stream, lockPath);
                return true;
            }
            catch (IOException)
            {
                // [XPLAT] Another AgIO instance already holds the lock-file -> this is not the sole instance.
                instanceLock = new NoOpDisposable();
                return false;
            }
        }

        /// <summary>
        /// Strips brace and filesystem-invalid characters from <paramref name="identifier"/> so a GUID
        /// such as <c>{8F6F0AC4-...}</c> becomes a clean, collision-free lock-file name. Uniqueness is
        /// preserved (only braces / invalid characters are removed); falls back to a constant when the
        /// sanitized result would otherwise be empty.
        /// </summary>
        private static string SanitizeFileName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return "instance";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var cleaned = new List<char>(identifier.Length);

            foreach (char c in identifier)
            {
                if (c != '{' && c != '}' && Array.IndexOf(invalid, c) < 0)
                {
                    cleaned.Add(c);
                }
            }

            return cleaned.Count > 0 ? new string(cleaned.ToArray()) : "instance";
        }

        /// <summary>
        /// [XPLAT] Holds the open, exclusively-shared lock-file <see cref="FileStream"/> for the lifetime
        /// of the single instance. Disposing releases the advisory lock (where held), closes the stream —
        /// which drops the <see cref="FileShare.None"/> guard — and best-effort deletes the lock-file so
        /// the directory stays tidy and <c>Program.Restart()</c> can re-acquire immediately. Idempotent.
        /// </summary>
        private sealed class FileInstanceLock : IDisposable
        {
            private readonly FileStream _stream;
            private readonly string _lockPath;
            private bool _disposed;

            public FileInstanceLock(FileStream stream, string lockPath)
            {
                _stream = stream;
                _lockPath = lockPath;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (!OperatingSystem.IsMacOS())
                {
                    try { _stream.Unlock(0, 0); } catch { /* advisory unlock; ignore if not held */ }
                }

                try { _stream.Dispose(); } catch { /* releasing the FileShare.None guard; ignore */ }

                try { File.Delete(_lockPath); } catch { /* best-effort cleanup; ignore */ }
            }
        }

        /// <summary>
        /// [XPLAT] Disposable returned when the single-instance guard is already held by another process;
        /// disposing it does nothing, mirroring the no-op guard semantics of the interface contract.
        /// </summary>
        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
                // [XPLAT] Nothing to release: this instance never owned the guard.
            }
        }
    }
}
