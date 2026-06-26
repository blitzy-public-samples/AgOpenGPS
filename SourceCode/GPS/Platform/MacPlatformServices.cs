// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AgOpenGPS.Platform
{
    /// <summary>
    /// macOS implementation of <see cref="IPlatformServices"/> (net8.0). Resolves the config root
    /// under <c>~/Library/Application Support/AgOpenGPS</c>, enumerates <c>/dev/cu.*</c> serial
    /// devices, and guards single-instance with a lockfile plus an advisory <c>flock(2)</c> lock.
    /// Monitor brightness is feature-gated because macOS exposes no portable brightness API, so
    /// <see cref="GetBrightness"/> returns -1 and <see cref="SetBrightness"/> is a no-op,
    /// preserving the existing graceful-degradation contract.
    /// </summary>
    public sealed class MacPlatformServices : IPlatformServices
    {
        private const string DevRoot = "/dev";

        // [XPLAT] macOS-supported advisory lock (AAP §0.6.3 "Lockfile + advisory lock").
        // FileStream.Lock (fcntl byte-range lock) is unsupported on macOS and throws there, so the
        // BSD advisory whole-file lock flock(2) is used instead, applied to the lock-file handle's
        // file descriptor. LOCK_EX (exclusive) | LOCK_NB (non-blocking) acquire; LOCK_UN release.
        private const int LOCK_EX = 2;
        private const int LOCK_NB = 4;
        private const int LOCK_UN = 8;

        [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        private static extern int Flock(int handle, int operation);

        /// <inheritdoc/>
        public string AppDataRoot
        {
            get
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "AgOpenGPS");
            }
        }

        /// <inheritdoc/>
        public int GetBrightness()
        {
            // No portable macOS brightness API; feature-gated to the -1 "unavailable" contract.
            return -1;
        }

        /// <inheritdoc/>
        public void SetBrightness(int brightness)
        {
            // No portable macOS brightness API; no-op (graceful degradation).
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetSerialPortNames()
        {
            var ports = new List<string>();
            if (Directory.Exists(DevRoot))
            {
                ports.AddRange(Directory.GetFiles(DevRoot, "cu.*"));
            }

            return ports;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// [XPLAT] macOS single-instance guard = exclusive lock-file (<see cref="FileShare.None"/>) PLUS an
        /// explicit advisory <c>flock(2)</c> lock, per AAP §0.6.3 ("Lockfile + advisory lock"). The
        /// <see cref="FileShare.None"/> handle is the cross-process guard (the .NET runtime backs it with
        /// <c>flock()</c> on macOS, so a second process opening the same path fails with
        /// <see cref="IOException"/>); the explicit <c>flock(LOCK_EX|LOCK_NB)</c> is the macOS-supported
        /// advisory lock the finding requires (<see cref="FileStream.Lock(long,long)"/> is unsupported on
        /// macOS). This guard FAILS CLOSED — if the lock location cannot be prepared, the lock-file is
        /// already held, or the advisory lock cannot be acquired, it returns <see langword="false"/> and
        /// never permits a second instance (which could drive conflicting hardware/field operations).
        /// </remarks>
        public bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock)
        {
            string lockPath;
            try
            {
                Directory.CreateDirectory(AppDataRoot);
                lockPath = Path.Combine(AppDataRoot, FileNameFor(identifier));
            }
            catch (Exception)
            {
                // [XPLAT] FAIL CLOSED: cannot prepare the lock location, so sole-instance cannot be proven.
                instanceLock = new NoOpDisposable();
                return false;
            }

            FileStream stream = null;
            try
            {
                // FileShare.None => the runtime takes an exclusive flock; a second process opening the same
                // path throws IOException. This is the real cross-process single-instance guard on macOS.
                stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                // [XPLAT] Explicit macOS advisory lock via flock(2) on the lock-file descriptor. A non-zero
                // return means the advisory lock could not be acquired -> FAIL CLOSED (lost the race).
                int fd = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                if (Flock(fd, LOCK_EX | LOCK_NB) != 0)
                {
                    stream.Dispose();
                    instanceLock = new NoOpDisposable();
                    return false;
                }

                instanceLock = new SingleInstanceLock(stream, lockPath);
                return true;
            }
            catch (IOException)
            {
                // [XPLAT] Another instance already holds the lock-file -> not the sole instance. FAIL CLOSED.
                stream?.Dispose();
                instanceLock = new NoOpDisposable();
                return false;
            }
        }

        private static string FileNameFor(string identifier)
        {
            var sb = new StringBuilder(identifier.Length);
            foreach (char c in identifier)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return sb.ToString() + ".lock";
        }

        /// <summary>
        /// [XPLAT] Holds the open, exclusively-shared lock-file <see cref="FileStream"/> for the lifetime
        /// of the single instance. Disposing releases the advisory <c>flock</c> (explicitly via
        /// <c>LOCK_UN</c>, then implicitly when the descriptor closes), closes the stream — which drops the
        /// <see cref="FileShare.None"/> guard — and best-effort deletes the lock-file so a subsequent
        /// restart can re-acquire immediately. Idempotent: safe to dispose more than once.
        /// </summary>
        private sealed class SingleInstanceLock : IDisposable
        {
            private FileStream _stream;
            private readonly string _lockPath;
            private bool _disposed;

            public SingleInstanceLock(FileStream stream, string lockPath)
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

                if (_stream != null)
                {
                    // [XPLAT] Release the advisory flock before closing; closing the descriptor also
                    // releases it, but the explicit LOCK_UN documents intent and is harmless if redundant.
                    try
                    {
                        int fd = _stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                        Flock(fd, LOCK_UN);
                    }
                    catch { /* advisory unlock; ignore — closing the stream releases the lock anyway */ }

                    try { _stream.Dispose(); } catch { /* releasing the FileShare.None guard; ignore */ }
                    _stream = null;
                }

                try { File.Delete(_lockPath); } catch { /* best-effort cleanup; ignore */ }
            }
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
