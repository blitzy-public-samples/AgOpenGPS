// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
#if WINDOWS
using AgOpenGPS.Core.Platform;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Threading;

namespace AgOpenGPS.Platform
{
    /// <summary>
    /// Windows implementation of <see cref="IPlatformServices"/>. Compiled only under the
    /// <c>net8.0-windows</c> target (guarded by <c>#if WINDOWS</c>) because it uses WMI
    /// (<see cref="System.Management"/>) and the Windows Registry. Houses the monitor-brightness
    /// WMI body relocated from the former <c>Classes/CBrightness.cs</c> and the one-time Registry
    /// migration-read consumed by the settings layer.
    /// </summary>
    public sealed class WindowsPlatformServices : IPlatformServices
    {
        private const string LegacyRegistrySubKey = @"SOFTWARE\AgOpenGPS";

        /// <inheritdoc/>
        public string AppDataRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AgOpenGPS");

        /// <inheritdoc/>
        public int GetBrightness()
        {
            try // fails (returns -1) on a device with no controllable brightness (e.g. a desktop)
            {
                var mclass = new ManagementClass("WmiMonitorBrightness")
                {
                    Scope = new ManagementScope(@"\\.\root\wmi")
                };

                foreach (ManagementObject instance in mclass.GetInstances())
                {
                    return (byte)instance.GetPropertyValue("CurrentBrightness");
                }

                return 0;
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
                var mclass = new ManagementClass("WmiMonitorBrightnessMethods")
                {
                    Scope = new ManagementScope(@"\\.\root\wmi")
                };

                var args = new object[] { 1, brightness };
                foreach (ManagementObject instance in mclass.GetInstances())
                {
                    instance.InvokeMethod("WmiSetBrightness", args);
                }
            }
            catch (Exception)
            {
                // Brightness control unavailable: no-op (parity with CBrightness).
            }
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetSerialPortNames()
        {
            return SerialPort.GetPortNames();
        }

        /// <inheritdoc/>
        public bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock)
        {
            var mutex = new Mutex(true, identifier, out bool createdNew);
            if (createdNew)
            {
                instanceLock = new MutexInstanceLock(mutex);
                return true;
            }

            mutex.Dispose();
            instanceLock = new NoOpDisposable();
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/> when the legacy <c>HKCU\SOFTWARE\AgOpenGPS</c> key
        /// exists (used to decide whether a one-time settings migration is needed on first run).
        /// Windows-only helper; not part of <see cref="IPlatformServices"/>.
        /// </summary>
        public bool LegacyRegistryExists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRegistrySubKey))
            {
                return key != null;
            }
        }

        /// <summary>
        /// Reads a single value from the legacy <c>HKCU\SOFTWARE\AgOpenGPS</c> key, or
        /// <see langword="null"/> when the key or value is absent. Used by the settings layer to
        /// migrate legacy Registry-backed settings to the cross-platform config root on first run.
        /// Known value names: WorkingDirectory, VehicleProfileName, ToolProfileName,
        /// VehicleFileName, EnvironmentFileName, Language. Windows-only helper; not part of
        /// <see cref="IPlatformServices"/>.
        /// </summary>
        public string ReadLegacyRegistryValue(string valueName)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyRegistrySubKey))
            {
                return key?.GetValue(valueName)?.ToString();
            }
        }

        private sealed class MutexInstanceLock : IDisposable
        {
            private Mutex _mutex;

            public MutexInstanceLock(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                if (_mutex == null)
                {
                    return;
                }

                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by the disposing thread; safe to ignore.
                }

                _mutex.Dispose();
                _mutex = null;
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
#endif
