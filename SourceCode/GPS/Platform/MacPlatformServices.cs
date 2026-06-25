// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgOpenGPS.Platform
{
    /// <summary>
    /// macOS implementation of <see cref="IPlatformServices"/> (net8.0). Resolves the config root
    /// under <c>~/Library/Application Support/AgOpenGPS</c>, enumerates <c>/dev/cu.*</c> serial
    /// devices, and guards single-instance with a lockfile plus an advisory file lock. Monitor
    /// brightness is feature-gated because macOS exposes no portable brightness API, so
    /// <see cref="GetBrightness"/> returns -1 and <see cref="SetBrightness"/> is a no-op,
    /// preserving the existing graceful-degradation contract.
    /// </summary>
    public sealed class MacPlatformServices : IPlatformServices
    {
        private const string DevRoot = "/dev";

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
