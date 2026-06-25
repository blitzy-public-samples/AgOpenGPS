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
                // Cannot prepare the lock location; allow startup without single-instance enforcement.
                instanceLock = new NoOpDisposable();
                return true;
            }

            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                instanceLock = new SingleInstanceLock(stream);
                return true;
            }
            catch (IOException)
            {
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

        private sealed class SingleInstanceLock : IDisposable
        {
            private FileStream _stream;

            public SingleInstanceLock(FileStream stream)
            {
                _stream = stream;
            }

            public void Dispose()
            {
                if (_stream == null)
                {
                    return;
                }

                _stream.Dispose();
                _stream = null;
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
