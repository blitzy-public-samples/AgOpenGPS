// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// GPS application entry point. The Windows Forms bootstrap (Application.EnableVisualStyles /
// SetCompatibleTextRenderingDefault / Application.Run(new FormGPS())) and the Windows-only named-Mutex
// single-instance guard are replaced by an Avalonia classic-desktop bootstrap and the cross-platform
// single-instance mechanism exposed by AgOpenGPS.Core.Platform.IPlatformServices. Every OS-specific
// capability (config root, single-instance guard, brightness, serial enumeration) is reached only through
// that interface, whose concrete per-OS implementations live under this project's Platform/ folder and are
// selected at startup by PlatformServicesFactory. See MIGRATION_DOCS/TRANSITION_MAP.md.
using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Platform;
using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgOpenGPS
{
    internal static class Program
    {
        // [XPLAT] Single-instance identity, preserved byte-for-byte from the WinForms build (it was the
        // named-Mutex name). Keeping the exact GUID means a mixed fleet — and the AgOpenGPS<->AgIO
        // two-program contract — continues to recognise the same single-instance key across the migration.
        // The guard itself is no longer a raw Mutex here: IPlatformServices.TryAcquireSingleInstance owns the
        // per-OS mechanism (named Mutex on Windows; lock-file + advisory lock on Linux/macOS).
        private const string SingleInstanceName = "{516-0AC5-B9A1-55fd-A8CE-72F04E6BDE8F}";

        // [XPLAT] The acquired single-instance guard for THIS process. Stored in a static field so it is
        // held for the entire process lifetime (it must not be garbage-collected/disposed early, which would
        // release the guard and let a second instance start). It is disposed exactly once on graceful
        // shutdown (see Main's finally), mirroring the WinForms process-lifetime Mutex and supporting the
        // sibling AgIO Restart() flow, which relies on the guard being released when the process exits.
        private static IDisposable _instanceLock;

        // [XPLAT] The single IPlatformServices instance created for this process. Exposed so the Avalonia
        // application (App.axaml.cs) builds ApplicationCore against the SAME instance rather than creating a
        // second one — important because the single-instance guard above lives on this very instance.
        // internal: App lives in the same assembly/namespace, so it can read this without widening visibility.
        internal static IPlatformServices PlatformServices { get; private set; }

        // [XPLAT] Product version string. The WinForms build read System.Windows.Forms.Application.ProductVersion,
        // which returns the entry assembly's AssemblyInformationalVersionAttribute (falling back to the file/assembly
        // version). That attribute is read directly here so the value — and the SemVer/IsPreRelease/IsDevelopVersion
        // derivations below — stay identical without referencing System.Windows.Forms. The fallback chain matches the
        // old behaviour: informational version → assembly version string → "1.0.0.0".
        private static readonly string ProductVersion =
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "1.0.0.0";

        // Major.Minor.Patch — unchanged from the WinForms build (already reflection-based). Read by other code
        // (e.g. ISOXML ManagementSoftwareVersion, the AgIO title bar), so it stays public static readonly.
        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString(3);

        // SemVer strips any "+build" metadata; IsPreRelease detects a "-prerelease" suffix; IsDevelopVersion flags the
        // unversioned dev build. Semantics are identical to the WinForms build — only the version source changed
        // (Application.ProductVersion -> ProductVersion above). Consumed by the About/Terms/Settings views and the
        // beta/develop watermark, so all remain public static readonly.
        public static readonly string SemVer = ProductVersion.Split('+').First();
        public static readonly bool IsPreRelease = ProductVersion.Contains('-');
        public static readonly bool IsDevelopVersion = ProductVersion == "1.0.0.0";

        /// <summary>
        /// [XPLAT] Builds and configures the Avalonia application. Declared <c>public static</c> so the Avalonia
        /// XAML previewer / visual designer can discover and invoke it. Replaces the WinForms
        /// <c>Application.EnableVisualStyles()</c> / <c>SetCompatibleTextRenderingDefault(false)</c> setup; the
        /// Inter font is registered here so <c>App.axaml</c> needs no font include. The main window (hosting
        /// <c>Views/MainView</c>) and the <c>ApplicationCore</c>/view-model/service wiring are created by
        /// <c>App.OnFrameworkInitializationCompleted</c>, not here.
        /// </summary>
        /// <returns>A configured <see cref="AppBuilder"/> for the GPS <see cref="App"/>.</returns>
        /// <remarks>
        /// [XPLAT] G3 / AAP §0.6.2 (DOMINANT feasibility risk): the central field viewport's Core DrawLib/GLW
        /// renderer (<c>RenderCoordinator</c>) uses legacy immediate-mode / fixed-function OpenGL plus three
        /// <c>GL.ReadPixels</c> back-buffer scans (section look-ahead, zoom-overlap, flag pick). Those exist only
        /// in a DESKTOP OpenGL (compatibility) context; under the OpenGL ES / ANGLE context Avalonia frequently
        /// selects by default they would be unavailable and <see cref="AvaloniaGeoViewport"/> would feature-gate
        /// the viewport to a blank cleared surface. The build is therefore wrapped with
        /// <see cref="AvaloniaGeoViewport.RequestDesktopGlProfile(AppBuilder)"/>, which requests WGL (Windows) /
        /// GLX (Linux/X11) native desktop GL with 3.2-compatibility then 2.1 profiles, falling back to software
        /// rendering. It is a best-effort request — the host still audits the obtained context and gates if it is
        /// nonetheless GLES — so per-OS/RID hardware confirmation of the live and back-buffer GL paths remains an
        /// open item tracked in MIGRATION_DOCS/PARITY_REPORT.md. Wiring this hook is the migration's required
        /// mitigation for the GL-context risk (review G3).
        /// </remarks>
        public static AppBuilder BuildAvaloniaApp()
            => AvaloniaGeoViewport.RequestDesktopGlProfile(
                    AppBuilder.Configure<App>()
                        .UsePlatformDetect())
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">Command-line arguments forwarded to the Avalonia desktop lifetime.</param>
        /// <remarks>
        /// [XPLAT] Replaces <c>Application.Run(new FormGPS())</c> with the Avalonia classic-desktop lifetime and the
        /// Windows-only named <c>Mutex</c> with <see cref="IPlatformServices.TryAcquireSingleInstance"/>. Startup order
        /// is significant: register the per-OS platform factories, create the one platform instance, share it with the
        /// settings layer, acquire the single-instance guard, run the one-time legacy-settings migration, load the
        /// profile, apply the persisted culture, then bootstrap Avalonia.
        /// <para>
        /// [STAThread] is retained: Avalonia on Windows requires an STA thread for clipboard and drag-and-drop interop;
        /// the attribute is ignored on Linux and macOS.
        /// </para>
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            // [XPLAT] Register the concrete per-OS IPlatformServices creators with the Core factory, then create the
            // single instance for this process (Strategy + Factory, AAP §0.3.2). The Windows creator is behind
            // #if WINDOWS because WindowsPlatformServices is compiled only for the net8.0-windows target (it uses WMI
            // + the Registry); on the net8.0 (Linux/macOS) build the Windows slot is simply not registered, which is
            // correct because that build never runs on Windows. Register is null-tolerant per slot.
            PlatformServicesFactory.Register(
#if WINDOWS
                windowsFactory: () => new AgOpenGPS.Platform.WindowsPlatformServices(),
#endif
                linuxFactory: () => new AgOpenGPS.Platform.LinuxPlatformServices(),
                macFactory: () => new AgOpenGPS.Platform.MacPlatformServices());

            IPlatformServices platform = PlatformServicesFactory.Create();
            PlatformServices = platform;

            // [XPLAT] Share this exact instance with the settings layer so RegistrySettings resolves its config root
            // (IPlatformServices.AppDataRoot) from the same platform object the rest of the app uses. This is the
            // documented startup hook (call after Register, before Load); it never throws.
            RegistrySettings.Initialize(platform);

            // [XPLAT] Cross-platform single-instance guard (replaces the named Mutex). When another instance already
            // holds the guard, exit quietly — matching the WinForms build, where the second instance never created a
            // FormGPS. The Avalonia UI is not running yet, so the user-facing notice goes to stderr (the WinForms
            // FormDialog warning is gone); a non-zero exit code lets a launcher detect the "already running" outcome.
            if (!platform.TryAcquireSingleInstance(SingleInstanceName, out IDisposable instanceLock))
            {
                Console.Error.WriteLine("AgOpenGPS is already running.");
                Environment.ExitCode = 1;
                return;
            }

            // Hold the guard for the whole process; release it on graceful shutdown (finally below).
            _instanceLock = instanceLock;

            try
            {
                // [XPLAT] One-time legacy Windows Registry -> RegistrySettings.xml migration. Must run BEFORE
                // RegistrySettings.Load() so existing Windows users' legacy HKCU\SOFTWARE\AgOpenGPS values seed the new
                // cross-platform XML settings store on first launch (RegistrySettings itself has no Registry knowledge
                // and relies on this seed). Safe to call unconditionally: it is a guaranteed no-op on non-Windows
                // (RuntimeInformation guard + a #if WINDOWS body that is not even compiled into the net8.0 assembly),
                // and idempotent on Windows (skips once RegistrySettings.xml exists). See CSettingsMigration +
                // MIGRATION_DOCS/TRANSITION_MAP.md.
                CSettingsMigration.MigrateLegacyRegistrySettings();

                // Load the profile/working-directory configuration from the cross-platform config root.
                RegistrySettings.Load();

                // [XPLAT] Preserve the per-user UI culture loaded from settings (unchanged from the WinForms build).
                // INVARIANT-CULTURE CONTRACT (AAP §0.6.5, the highest data-integrity risk): only the *UI* culture is
                // taken from settings here. All numeric file/protocol I/O — field files, settings XML, ISOXML, and
                // PGN-derived text — MUST use CultureInfo.InvariantCulture in the IO/, Properties/, Protocols/ and
                // Services/ layers, so a Linux/macOS locale with a comma decimal separator can never corrupt data.
                // Do NOT force CultureInfo.DefaultThreadCurrentCulture to InvariantCulture globally: that would break
                // the localized UI. The two concerns are deliberately split — localized UI vs. always-invariant data.
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(RegistrySettings.culture);
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(RegistrySettings.culture);

                // [XPLAT] Start the Avalonia classic-desktop lifetime (replaces Application.Run(new FormGPS())). This
                // call blocks until the application exits; App.OnFrameworkInitializationCompleted creates the main
                // window and wires ApplicationCore against PlatformServices.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                // [XPLAT] Release the single-instance guard exactly once on shutdown. On Linux/macOS this also removes
                // the lock-file; on Windows it releases the named Mutex. Disposing here (after the lifetime returns)
                // mirrors the WinForms process-lifetime Mutex and lets the AgIO Restart() flow re-acquire immediately.
                _instanceLock?.Dispose();
                _instanceLock = null;
            }
        }

        // [XPLAT] ---- Two-program model: GPS owns the AgIO hub lifecycle (AAP R4 / F-003) ----------------
        // GPS and AgIO are separate single-instance programs that communicate ONLY over UDP loopback; GPS
        // auto-starts AgIO on launch and auto-stops it on exit. The WinForms FormGPS did this inline in its
        // Load handler (Process.GetProcessesByName("AgIO") guard + Process.Start(Application.StartupPath\AgIO.exe))
        // and in its FormClosing handler (Process.GetProcessesByName("AgIO")[0].CloseMainWindow()). That logic is
        // lifted here, behaviour-frozen, as two reusable launch/terminate primitives the Avalonia composition
        // root (App.axaml.cs) and the manual "Start AgIO" button both call. No GPS->AgIO project reference is
        // introduced (that would break the two-program contract): the sibling is located on disk and managed
        // purely through System.Diagnostics.Process. See MIGRATION_DOCS/TRANSITION_MAP.md.

        // [XPLAT] OS-specific file name of the sibling AgIO executable. The WinForms build hard-coded "AgIO.exe";
        // a self-contained publish on Linux/macOS produces an extension-less "AgIO" launcher. RuntimeInformation
        // selects the right one so auto-start works on every target RID (win-x64/linux-x64/osx-x64/osx-arm64).
        private static string AgIOExecutableName =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "AgIO.exe" : "AgIO";

        // [XPLAT] Process *name* used by the running-instance probe. Process.GetProcessesByName matches the
        // executable file name WITHOUT its extension on every OS, so the single literal "AgIO" is correct on
        // Windows, Linux and macOS alike — mirroring the WinForms Process.GetProcessesByName("AgIO") guard.
        private const string AgIOProcessName = "AgIO";

        /// <summary>
        /// [XPLAT] Auto-starts the sibling AgIO hub process if it is not already running. Preserves the WinForms
        /// FormGPS load behaviour (start AgIO when a vehicle profile is selected and <c>setDisplay_isAutoStartAgIO</c>
        /// is true) — part of the two-program model (AAP R4 / F-003) in which GPS owns AgIO's lifecycle while the
        /// two communicate only over UDP loopback. The gating decision is made by the caller (the App composition
        /// root) so this stays a pure, reusable launch primitive that the manual "Start AgIO" button can also call.
        /// </summary>
        /// <remarks>
        /// The AgIO executable is located beside the running GPS executable via <see cref="AppContext.BaseDirectory"/>
        /// (reliable for both framework-dependent and self-contained publishes, unlike <c>Environment.ProcessPath</c>,
        /// which under <c>dotnet App.dll</c> points at the shared host). The launch is guarded by a
        /// <see cref="Process.GetProcessesByName(string)"/> probe so a second AgIO is never spawned, exactly as the
        /// WinForms build did. All failures are swallowed-and-logged: a missing or unstartable AgIO must never crash
        /// GPS or block guidance (graceful-degradation rule, AAP §0.7.2).
        /// </remarks>
        internal static void StartAgIO()
        {
            try
            {
                // Don't launch a second hub — mirror the WinForms GetProcessesByName("AgIO").Length == 0 guard.
                if (Process.GetProcessesByName(AgIOProcessName).Length > 0)
                {
                    return;
                }

                string exePath = Path.Combine(AppContext.BaseDirectory, AgIOExecutableName);
                if (!File.Exists(exePath))
                {
                    // Match the WinForms "Can't Find AgIO" diagnostic. The UI-facing TimedMessageBox the
                    // WinForms build also showed is intentionally omitted here (auto-start runs before/independently
                    // of any modal owner); the operator can still start AgIO manually.
                    Log.EventWriter("Can't Find AgIO, File not Found");
                    return;
                }

                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    // [XPLAT] UseShellExecute=false launches the executable directly (consistent on all RIDs and
                    // required for a self-contained launcher); the WinForms default of true is unnecessary here.
                    UseShellExecute = false,
                };
                Process.Start(processInfo);
                Log.EventWriter("AgIO Started");
            }
            catch
            {
                // Graceful degradation: never let an AgIO launch failure break GPS startup.
                Log.EventWriter("Can't Find AgIO, File not Found");
            }
        }

        /// <summary>
        /// [XPLAT] Auto-stops the sibling AgIO hub on GPS shutdown when <c>setDisplay_isAutoOffAgIO</c> is enabled
        /// (preserving FormGPS's auto-off behaviour and the two-program lifecycle, AAP R4). The WinForms build called
        /// <c>CloseMainWindow()</c> on the AgIO process; that is a Windows-only graceful close (WM_CLOSE) and a no-op
        /// on Linux/macOS, so it is followed here by a bounded wait and a <see cref="Process.Kill(bool)"/> fallback to
        /// guarantee the hub actually exits on every OS — otherwise auto-off would silently fail off-Windows. The
        /// gating decision is made by the caller; this stays a pure terminate primitive.
        /// </summary>
        internal static void StopAgIO()
        {
            try
            {
                foreach (Process agio in Process.GetProcessesByName(AgIOProcessName))
                {
                    try
                    {
                        // Graceful first (matches WinForms): WM_CLOSE on Windows, a no-op that returns false elsewhere.
                        agio.CloseMainWindow();

                        // Give AgIO a moment to release its single-instance guard and tear down cleanly. On Unix
                        // CloseMainWindow did nothing, so this wait will time out and the Kill fallback runs.
                        if (!agio.WaitForExit(1500))
                        {
                            // Cross-platform guarantee: force-terminate when graceful close did not (or could not)
                            // take effect. entireProcessTree:true also reaps any AgIO child processes.
                            agio.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Ignore per-process failures (already exited / access race) and continue with the rest.
                    }
                    finally
                    {
                        agio.Dispose();
                    }
                }
            }
            catch
            {
                // Never let teardown throw during shutdown.
            }
        }
    }
}
