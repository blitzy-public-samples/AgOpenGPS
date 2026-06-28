// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.Platform;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgOpenGPS.Platform
{
    /// <summary>
    /// Linux implementation of <see cref="IPlatformServices"/> (net8.0). Resolves the config root
    /// via XDG (<c>$XDG_CONFIG_HOME</c> or <c>~/.config</c>), reads/writes monitor brightness
    /// best-effort through <c>/sys/class/backlight</c>, enumerates <c>/dev/ttyUSB*</c> and
    /// <c>/dev/ttyACM*</c> serial devices, and guards single-instance with a lockfile plus an
    /// advisory file lock. Every capability degrades gracefully (brightness returns -1 / no-op)
    /// so a missing capability never breaks startup or guidance.
    /// </summary>
    public sealed class LinuxPlatformServices : IPlatformServices
    {
        private const string BacklightRoot = "/sys/class/backlight";
        private const string DevRoot = "/dev";

        /// <inheritdoc/>
        public string AppDataRoot
        {
            get
            {
                string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(configHome))
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    configHome = Path.Combine(home, ".config");
                }

                return Path.Combine(configHome, "AgOpenGPS");
            }
        }

        /// <inheritdoc/>
        public int GetBrightness()
        {
            try
            {
                if (!Directory.Exists(BacklightRoot))
                {
                    return -1;
                }

                foreach (string device in Directory.GetDirectories(BacklightRoot))
                {
                    string currentPath = Path.Combine(device, "brightness");
                    string maxPath = Path.Combine(device, "max_brightness");

                    if (File.Exists(currentPath) && File.Exists(maxPath) &&
                        int.TryParse(File.ReadAllText(currentPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int current) &&
                        int.TryParse(File.ReadAllText(maxPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) &&
                        max > 0)
                    {
                        return (int)Math.Round(current * 100.0 / max);
                    }
                }

                return -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <inheritdoc/>
        public void SetBrightness(int brightness)
        {
            try
            {
                if (!Directory.Exists(BacklightRoot))
                {
                    return;
                }

                foreach (string device in Directory.GetDirectories(BacklightRoot))
                {
                    string currentPath = Path.Combine(device, "brightness");
                    string maxPath = Path.Combine(device, "max_brightness");

                    if (File.Exists(currentPath) && File.Exists(maxPath) &&
                        int.TryParse(File.ReadAllText(maxPath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) &&
                        max > 0)
                    {
                        int clamped = Math.Max(0, Math.Min(100, brightness));
                        int raw = (int)Math.Round(clamped / 100.0 * max);
                        File.WriteAllText(currentPath, raw.ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Writing sysfs backlight usually needs elevated privileges: no-op on failure.
            }
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetSerialPortNames()
        {
            var ports = new List<string>();
            if (Directory.Exists(DevRoot))
            {
                ports.AddRange(Directory.GetFiles(DevRoot, "ttyUSB*"));
                ports.AddRange(Directory.GetFiles(DevRoot, "ttyACM*"));
            }

            return ports;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// [XPLAT] Linux single-instance guard = exclusive lock-file (<see cref="FileShare.None"/>) PLUS
        /// a best-effort advisory record lock (<see cref="FileStream.Lock(long,long)"/>, supported on
        /// Linux), per AAP §0.6.3 ("Lockfile + advisory lock"). Unix has no named-Mutex equivalent, so the
        /// <see cref="FileShare.None"/> handle is the real cross-process guard: the .NET runtime backs it
        /// with an exclusive <c>flock()</c>, so a second process opening the same path fails with
        /// <see cref="IOException"/>. This guard FAILS CLOSED — if the lock location cannot be prepared or
        /// the lock cannot be taken, it returns <see langword="false"/> (never permits a second instance),
        /// because allowing two AgOpenGPS instances risks conflicting hardware/field operations.
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
                // [XPLAT] FAIL CLOSED: cannot prepare the lock location, so we cannot prove sole-instance.
                // Refuse to permit a second instance rather than fail open (single-instance is a safety guard).
                instanceLock = new NoOpDisposable();
                return false;
            }

            try
            {
                // FileShare.None => the runtime takes an exclusive flock; a second process opening the same
                // path throws IOException. This is the real cross-process single-instance guard.
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                // [XPLAT] Advisory record lock (fcntl) — the explicit advisory lock the AAP mandates.
                // Supported on Linux (FileStream.Lock is unsupported only on macOS), so it is guarded by
                // an OperatingSystem.IsLinux() check that also satisfies the platform-compatibility
                // analyzer (CA1416). Best-effort: FileShare.None already guarantees exclusivity.
                if (OperatingSystem.IsLinux())
                {
                    try { stream.Lock(0, 0); } catch { /* advisory only; FileShare.None already guards */ }
                }

                instanceLock = new SingleInstanceLock(stream, lockPath);
                return true;
            }
            catch (IOException)
            {
                // [XPLAT] Another instance already holds the lock-file -> this is not the sole instance.
                // FAIL CLOSED: return false so the caller exits quietly (matches WinForms second-instance behaviour).
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
        /// of the single instance. Disposing releases the advisory record lock (where held), closes the
        /// stream — which drops the <see cref="FileShare.None"/> guard — and best-effort deletes the
        /// lock-file so the directory stays tidy and a subsequent restart can re-acquire immediately.
        /// Idempotent: safe to dispose more than once.
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
                    // [XPLAT] Release the advisory record lock taken in TryAcquireSingleInstance. Guarded by
                    // OperatingSystem.IsLinux() to mirror the acquire side and satisfy CA1416 (FileStream.Unlock
                    // is unsupported on macOS). Closing the stream then drops the FileShare.None guard.
                    if (OperatingSystem.IsLinux())
                    {
                        try { _stream.Unlock(0, 0); } catch { /* advisory unlock; ignore if not held */ }
                    }
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
