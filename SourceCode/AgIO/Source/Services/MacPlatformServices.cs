// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AgOpenGPS.Core.Platform;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] macOS concrete implementation of <see cref="IPlatformServices"/> for AgIO.
    /// </summary>
    /// <remarks>
    /// Created as part of migrating AgIO from net48/WinForms (Windows-only) to .NET 8 + Avalonia
    /// running on macOS (RIDs <c>osx-x64</c> and <c>osx-arm64</c>). It is modelled on the sibling
    /// <c>WindowsPlatformServices</c>/<c>LinuxPlatformServices</c> implementations but applies macOS
    /// conventions and contains <b>no</b> Windows-only APIs, so the file compiles unchanged under both
    /// <c>net8.0</c> and <c>net8.0-windows</c> (it is registered unconditionally by the platform-service
    /// factory). macOS differs from Linux in only two places: the application-data root lives under
    /// <c>~/Library/Application Support</c> (not <c>~/.config</c>), and serial ports are discovered from
    /// the <c>/dev/cu.*</c> call-out device nodes. Monitor brightness is intentionally feature-gated to a
    /// no-op (AAP §0.6.3), preserving the <c>-1</c> "not available" contract of the former WMI controller,
    /// and single-instance is guarded with an exclusive lockfile (macOS has no named <c>Mutex</c>) plus an
    /// explicit advisory <c>flock(2)</c> lock that fails closed (QA Issue 4 — parity with GPS).
    /// Every member degrades gracefully and never throws on environmental failure (AAP §0.6.5).
    /// </remarks>
    public sealed class MacPlatformServices : IPlatformServices
    {
        /// <summary>
        /// Default lockfile stem used when <see cref="TryAcquireSingleInstance"/> is handed an
        /// identifier that sanitises to an empty string. Keeps the guard usable even for degenerate input.
        /// </summary>
        private const string DefaultLockName = "AgIO_instance";

        // [XPLAT] macOS-supported advisory whole-file lock flock(2) (QA Issue 4 — parity with the GPS
        // MacPlatformServices single-instance guard; see SourceCode/GPS/Platform/MacPlatformServices.cs and
        // MIGRATION_DOCS/TRANSITION_MAP.md). FileStream.Lock (an fcntl byte-range lock) is unsupported on
        // macOS and throws there, so the BSD advisory whole-file lock flock(2) is used instead, applied to
        // the lock-file handle's file descriptor. LOCK_EX (exclusive) | LOCK_NB (non-blocking) acquire;
        // LOCK_UN release. This gives AgIO the same explicit, stronger cross-process semantics as GPS rather
        // than relying on FileShare.None alone.
        private const int LOCK_EX = 2;
        private const int LOCK_NB = 4;
        private const int LOCK_UN = 8;

        [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        private static extern int Flock(int handle, int operation);

        /// <summary>
        /// Absolute path to the per-user AgOpenGPS application-data / configuration root on macOS,
        /// <c>~/Library/Application Support/AgOpenGPS</c>. The directory is created on demand.
        /// </summary>
        public string AppDataRoot
        {
            get
            {
                // [XPLAT] macOS convention is ~/Library/Application Support/AgOpenGPS. We build the path
                // explicitly from the user profile because Environment.SpecialFolder.ApplicationData maps
                // to ~/.config on Unix (the Linux convention), which is wrong for macOS.
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    "AgOpenGPS");

                Directory.CreateDirectory(root); // idempotent: a no-op when the directory already exists.
                return root;
            }
        }

        /// <summary>
        /// Returns the current monitor brightness percentage, or <c>-1</c> when brightness control is
        /// unavailable. On macOS this is feature-gated and always returns <c>-1</c>.
        /// </summary>
        /// <returns>Always <c>-1</c> (brightness control is feature-gated on macOS).</returns>
        public int GetBrightness() => -1; // [XPLAT] macOS brightness feature-gated; -1 == not available.

        /// <summary>
        /// Sets the monitor brightness percentage. On macOS this is a feature-gated no-op.
        /// </summary>
        /// <param name="brightness">Ignored on macOS (brightness control is feature-gated).</param>
        public void SetBrightness(int brightness)
        {
            // [XPLAT] no-op: monitor brightness control is feature-gated on macOS (AAP §0.6.3). No IOKit
            // or `brightness` shell-out is attempted, mirroring the graceful no-op of the former WMI path.
        }

        /// <summary>
        /// Enumerates the available serial-port device paths on macOS, i.e. the <c>/dev/cu.*</c>
        /// call-out (non-blocking) device nodes such as <c>/dev/cu.usbserial-XXXX</c>. The full device
        /// paths are returned in a deterministic ordinal order. Never throws.
        /// </summary>
        /// <returns>The discovered <c>/dev/cu.*</c> device paths; an empty sequence when none exist.</returns>
        public IEnumerable<string> GetSerialPortNames()
        {
            var ports = new List<string>();

            try
            {
                if (Directory.Exists("/dev"))
                {
                    // [XPLAT] cu.* are the call-out device nodes — the correct client-side serial devices
                    // on macOS (tty.* are the dial-in counterparts and are deliberately not enumerated).
                    ports.AddRange(Directory.GetFiles("/dev", "cu.*"));
                }
            }
            catch
            {
                // [XPLAT] never throw from enumeration — degrade to whatever was collected so far.
            }

            ports.Sort(StringComparer.Ordinal);
            return ports;
        }

        /// <summary>
        /// Attempts to acquire the single-instance guard for the program identified by
        /// <paramref name="identifier"/> using an exclusive lockfile held under <see cref="AppDataRoot"/>.
        /// macOS has no named <c>Mutex</c>, so the open-with-no-sharing semantics of the lockfile provide
        /// the guard. Disposing the returned <paramref name="instanceLock"/> releases (and deletes) it,
        /// which keeps <c>Program.Restart()</c> working.
        /// </summary>
        /// <param name="identifier">A per-program unique key (the program's instance GUID).</param>
        /// <param name="instanceLock">
        /// Receives the live guard to dispose on shutdown/restart when this call returns
        /// <see langword="true"/>; receives a no-op disposable when it returns <see langword="false"/>.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when this process is the sole instance; <see langword="false"/> when
        /// another instance already holds the guard.
        /// </returns>
        public bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock)
        {
            // [XPLAT] was: named Mutex on Windows; on macOS use an exclusive lockfile (FileShare.None).
            string lockPath = Path.Combine(AppDataRoot, SanitizeFileName(identifier) + ".lock");

            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                // [XPLAT] Advisory whole-file lock as an explicit, stronger cross-process signal on top of
                // the FileShare.None handle. FileStream.Lock (fcntl byte-range) is unsupported on macOS
                // (CA1416) and would throw at runtime there, so it is used only on Windows/Linux; on macOS
                // we instead take an explicit flock(2) advisory lock (QA Issue 4 — parity with GPS), which
                // FAILS CLOSED: if the advisory lock cannot be acquired we never permit a second instance
                // (a second AgIO could drive conflicting serial/UDP hardware operations).
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                {
                    try
                    {
                        stream.Lock(0, 0);
                    }
                    catch
                    {
                        // advisory lock failed — FileShare.None already guarantees exclusivity.
                    }
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // [XPLAT] Explicit macOS advisory lock via flock(2) on the lock-file descriptor. A
                    // non-zero return means the advisory lock could not be acquired -> FAIL CLOSED.
                    int fd = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                    if (Flock(fd, LOCK_EX | LOCK_NB) != 0)
                    {
                        stream.Dispose();
                        instanceLock = new NoOpDisposable();
                        return false;
                    }
                }

                instanceLock = new FileInstanceLock(stream, lockPath);
                return true;
            }
            catch (IOException)
            {
                // [XPLAT] another instance already holds the lockfile (sharing violation) — degrade to a
                // held guard so the caller exits quietly, matching the WinForms second-instance behaviour.
                instanceLock = new NoOpDisposable();
                return false;
            }
        }

        /// <summary>
        /// Reduces an arbitrary identifier (typically a GUID such as <c>{8F6F0AC4-...}</c>) to a clean,
        /// filesystem-safe lockfile stem by stripping braces and any characters that are not legal in a
        /// file name. Falls back to <see cref="DefaultLockName"/> for null/empty or fully-stripped input.
        /// </summary>
        /// <param name="identifier">The raw single-instance identifier.</param>
        /// <returns>A filesystem-safe file-name stem (without extension).</returns>
        private static string SanitizeFileName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return DefaultLockName;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var kept = new List<char>(identifier.Length);

            foreach (char c in identifier)
            {
                if (c == '{' || c == '}')
                {
                    continue;
                }

                if (Array.IndexOf(invalid, c) >= 0)
                {
                    continue;
                }

                kept.Add(c);
            }

            return kept.Count > 0 ? new string(kept.ToArray()) : DefaultLockName;
        }

        /// <summary>
        /// The live single-instance guard returned when the lockfile is successfully acquired. Holds the
        /// owning <see cref="FileStream"/> open for the process lifetime; <see cref="Dispose"/> releases
        /// the advisory lock, closes the stream and best-effort deletes the lockfile. Disposal is
        /// idempotent, so releasing the guard before a restart (and again on process exit) is safe.
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

                // [XPLAT] Mirror the acquisition guard: FileStream.Unlock is unsupported on macOS
                // (CA1416), so the byte-range advisory lock is released only on Windows/Linux; on macOS the
                // flock(2) advisory lock is released explicitly with LOCK_UN (QA Issue 4 — parity with GPS).
                // Closing the descriptor also releases the lock, but the explicit unlock documents intent
                // and is harmless if redundant.
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                {
                    try
                    {
                        _stream.Unlock(0, 0);
                    }
                    catch
                    {
                        // the advisory lock may never have been applied — ignore.
                    }
                }
                else if (OperatingSystem.IsMacOS())
                {
                    try
                    {
                        int fd = _stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                        Flock(fd, LOCK_UN);
                    }
                    catch
                    {
                        // advisory unlock; ignore — closing the stream releases the lock anyway.
                    }
                }

                try
                {
                    _stream.Dispose();
                }
                catch
                {
                    // best-effort: closing the handle releases the FileShare.None guard regardless.
                }

                try
                {
                    File.Delete(_lockPath);
                }
                catch
                {
                    // best-effort cleanup; a stale lockfile is harmless and is reused on the next launch.
                }
            }
        }

        /// <summary>
        /// The guard returned when single-instance acquisition fails (another instance holds it).
        /// Disposing it does nothing because this process never owned the guard.
        /// </summary>
        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
                // [XPLAT] nothing to release — this instance never owned the single-instance guard.
            }
        }
    }
}
