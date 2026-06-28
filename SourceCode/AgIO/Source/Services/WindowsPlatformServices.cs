// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
#if WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using Microsoft.Win32;
using AgOpenGPS.Core.Platform;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Windows-only concrete implementation of
    /// <see cref="AgOpenGPS.Core.Platform.IPlatformServices"/> for the AgIO program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AgIO and AgOpenGPS (GPS) are sibling executables that communicate only over UDP loopback;
    /// AgIO does <b>not</b> reference the GPS project. AgIO therefore owns its own platform-service
    /// implementations (it cannot reuse GPS's <c>Platform/</c> types). They live in
    /// <c>AgIO/Source/Services/</c> under the <see cref="AgIO.Services"/> namespace.
    /// </para>
    /// <para>
    /// The entire file is fenced behind <c>#if WINDOWS</c>. AgIO multi-targets
    /// <c>net8.0;net8.0-windows</c>; the SDK auto-defines the <c>WINDOWS</c> compile symbol only for
    /// the <c>net8.0-windows</c> target framework. On the portable <c>net8.0</c> build this file
    /// compiles to an empty translation unit — which is correct and required, because the
    /// Windows-only <see cref="Microsoft.Win32.Registry"/> APIs used by
    /// <see cref="ReadLegacyRegistryConfig"/> are not available there and the type is only ever
    /// referenced from code that is itself guarded by <c>#if WINDOWS</c>.
    /// </para>
    /// <para>
    /// The <see cref="System.Runtime.Versioning.SupportedOSPlatform"/> attribute is applied as a
    /// belt-and-suspenders measure so the Windows-only Registry calls do not raise the CA1416
    /// platform-compatibility analyzer error (the Release configuration sets
    /// <c>TreatWarningsAsErrors</c>).
    /// </para>
    /// <para>
    /// Provenance: the single-instance guard replaces the named <see cref="Mutex"/> formerly held by
    /// <c>AgIO/Source/Program.cs</c>; brightness is an intentional <c>-1</c>/no-op (AgIO has no
    /// brightness UI — the WMI brightness path lives in GPS, not AgIO); and the legacy-Registry
    /// migration read mirrors the former <c>HKCU\SOFTWARE\AgOpenGPS</c> bootstrap store.
    /// </para>
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class WindowsPlatformServices : IPlatformServices
    {
        /// <summary>
        /// Absolute path to the per-user AgIO application-data / configuration root,
        /// <c>%AppData%\AgOpenGPS</c> on Windows. The directory is created if it does not yet exist.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the former Windows-Registry + <c>%AppData%</c> source.
        /// <see cref="Environment.SpecialFolder.ApplicationData"/> resolves to the Roaming AppData
        /// folder (<c>%AppData%</c>) on Windows. <see cref="Path.Combine(string, string)"/> is used
        /// so no path separator is hard-coded; the literal <c>"AgOpenGPS"</c> segment keeps its exact
        /// casing for parity with the existing layout used by both programs.
        /// </remarks>
        public string AppDataRoot
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AgOpenGPS");

                // Idempotent: ensures the root exists for config / single-instance files.
                Directory.CreateDirectory(root);
                return root;
            }
        }

        /// <summary>
        /// Returns <c>-1</c> ("brightness control not available"). AgIO has no brightness UI, so this
        /// is a deliberate no-op query that preserves the graceful-degradation contract.
        /// </summary>
        /// <returns>Always <c>-1</c>.</returns>
        // [XPLAT] Deliberate no-op: AgIO never had monitor-brightness control (no brightness UI in
        // FormLoop). The WMI brightness path lives in GPS's WindowsPlatformServices, not AgIO. The
        // -1 sentinel matches CBrightness.Get()'s "no controllable display" return value.
        public int GetBrightness() => -1;

        /// <summary>
        /// No-op. AgIO does not control monitor brightness.
        /// </summary>
        /// <param name="brightness">Ignored; AgIO has no brightness UI.</param>
        // [XPLAT] Deliberate no-op (see GetBrightness): AgIO has no brightness UI; brightness control
        // is a GPS-only concern.
        public void SetBrightness(int brightness)
        {
            // Intentionally empty: AgIO does not expose or control monitor brightness.
        }

        /// <summary>
        /// Enumerates the names of the available serial ports (for example <c>COM1</c>, <c>COM3</c>
        /// on Windows).
        /// </summary>
        /// <returns>The serial port names reported by the operating system.</returns>
        /// <remarks>
        /// [XPLAT] Only port-<i>name</i> discovery is abstracted; the serial I/O itself continues to
        /// use the cross-platform <c>System.IO.Ports</c> package unchanged.
        /// </remarks>
        public IEnumerable<string> GetSerialPortNames() => SerialPort.GetPortNames();

        /// <summary>
        /// Attempts to acquire the single-instance guard for the program identified by
        /// <paramref name="identifier"/>. Returns <see langword="true"/> with a live
        /// <paramref name="instanceLock"/> when this process is the sole instance; returns
        /// <see langword="false"/> (with a no-op disposable) when another instance already holds the
        /// guard. Disposing the returned guard releases it.
        /// </summary>
        /// <param name="identifier">A per-program unique key (the program's instance GUID).</param>
        /// <param name="instanceLock">Receives the guard to dispose on shutdown / restart.</param>
        /// <returns>
        /// <see langword="true"/> when this is the first/only instance; otherwise
        /// <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// [XPLAT] was: <c>new Mutex(true, "{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}", out mutexCreated)</c>
        /// in <c>AgIO/Source/Program.cs</c>. The guard is now keyed on the passed-in
        /// <paramref name="identifier"/> (Program.cs passes that same GUID) so the AgOpenGPS&lt;-&gt;AgIO
        /// two-program contract keeps the identical guard name.
        /// <para>
        /// Thread affinity: a thread-owned <see cref="Mutex"/> must be released from the same thread
        /// that acquired it. Single-instance acquisition here and the shutdown/restart release (which
        /// disposes the returned <paramref name="instanceLock"/>) both run on the application's main
        /// thread in <c>Program</c>/<c>App</c>, so the affinity requirement holds.
        /// </para>
        /// </remarks>
        public bool TryAcquireSingleInstance(string identifier, out IDisposable instanceLock)
        {
            // initiallyOwned: true — when createdNew is true this thread owns the mutex (parity with
            // the original Program._mutex), so MutexInstanceLock.Dispose() may call ReleaseMutex().
            var mutex = new Mutex(true, identifier, out bool createdNew);
            if (createdNew)
            {
                instanceLock = new MutexInstanceLock(mutex); // owns the handle; releases on Dispose
                return true;
            }

            // Another AgIO instance already owns the guard — release our handle and report "running".
            mutex.Dispose();
            instanceLock = new NoOpDisposable();
            return false;
        }

        /// <summary>
        /// [XPLAT] One-time legacy-Registry migration read. Returns the three former AgIO settings
        /// (<c>ProfileName</c> / <c>WorkingDirectory</c> / <c>Language</c>) from
        /// <c>HKCU\SOFTWARE\AgOpenGPS</c>, or an empty dictionary when the key/values are absent.
        /// Read-only and null-safe; never throws. Windows-only.
        /// </summary>
        /// <returns>
        /// A read-only map of the legacy value names to their string values; empty when the legacy
        /// key or values do not exist (or cannot be read).
        /// </returns>
        /// <remarks>
        /// Provided here to satisfy the AgIO Services folder requirement that
        /// <c>WindowsPlatformServices</c> carry the one-time Registry migration-read helper used by
        /// <c>Properties/RegistrySettings.cs</c>. It is an implementation-detail static method on a
        /// concrete class, not a new abstraction. It is deliberately <c>public</c> (not
        /// <c>private</c>) so that, if the migrated <c>RegistrySettings.cs</c> inlines its own
        /// equivalent read instead of calling this, the Release <c>TreatWarningsAsErrors</c> build is
        /// not broken by an "unused private member" diagnostic.
        /// </remarks>
        public static IReadOnlyDictionary<string, string> ReadLegacyRegistryConfig()
        {
            var result = new Dictionary<string, string>();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AgOpenGPS"))
                {
                    if (key == null)
                    {
                        return result;
                    }

                    AddIfPresent(result, key, "ProfileName");
                    AddIfPresent(result, key, "WorkingDirectory");
                    AddIfPresent(result, key, "Language");
                }
            }
            catch
            {
                // Absent / locked legacy key -> defaults apply downstream; a migration read must
                // never throw, so any failure deliberately yields the (possibly partial) result.
            }

            return result;
        }

        /// <summary>
        /// Adds <paramref name="name"/> to <paramref name="map"/> when the registry value exists.
        /// Null-safe.
        /// </summary>
        private static void AddIfPresent(Dictionary<string, string> map, RegistryKey key, string name)
        {
            object v = key.GetValue(name);
            if (v != null)
            {
                map[name] = v.ToString();
            }
        }

        /// <summary>
        /// [XPLAT] Single-instance guard that owns a thread-acquired named <see cref="Mutex"/> and
        /// releases it on <see cref="Dispose"/>. Replaces the disposal logic previously inlined in
        /// <c>AgIO/Source/Program.cs</c> (<c>_mutex.ReleaseMutex(); _mutex.Dispose();</c>), which
        /// also supports <c>Program.Restart()</c> by releasing the guard before relaunch so the new
        /// instance can acquire it.
        /// </summary>
        private sealed class MutexInstanceLock : IDisposable
        {
            private readonly Mutex _mutex;
            private bool _disposed;

            /// <summary>
            /// Initializes the lock with the owned <see cref="Mutex"/>.
            /// </summary>
            /// <param name="mutex">A mutex owned by the current thread (createdNew was true).</param>
            public MutexInstanceLock(Mutex mutex)
            {
                _mutex = mutex;
            }

            /// <summary>
            /// Releases the owned mutex (guarded so double-dispose is safe) and disposes the handle.
            /// </summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    // Valid because the owning thread acquired the mutex (createdNew == true) and
                    // disposal runs on that same (main) thread.
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Not owned by the calling thread (already released) — safe to ignore.
                }
                catch (ObjectDisposedException)
                {
                    // Handle already disposed — safe to ignore.
                }

                _mutex.Dispose();
            }
        }

        /// <summary>
        /// [XPLAT] No-op guard returned when another instance already holds the single-instance
        /// mutex; disposing it does nothing.
        /// </summary>
        private sealed class NoOpDisposable : IDisposable
        {
            /// <summary>Does nothing.</summary>
            public void Dispose()
            {
                // Intentionally empty: there is no guard to release in the "already running" case.
            }
        }
    }
}
#endif
