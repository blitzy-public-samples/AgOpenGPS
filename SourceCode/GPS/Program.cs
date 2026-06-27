// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// GPS application entry point. The Windows Forms bootstrap (Application.EnableVisualStyles /
// SetCompatibleTextRenderingDefault / Application.Run(new FormGPS())) and the Windows-only named-Mutex
// single-instance guard are replaced by an Avalonia classic-desktop bootstrap and the cross-platform
// single-instance mechanism exposed by AgOpenGPS.Core.Platform.IPlatformServices. Every OS-specific
// capability (config root, single-instance guard, brightness, serial enumeration) is reached only through
// that interface, whose concrete per-OS implementations live under this project's Platform/ folder and are
// selected at startup by PlatformServicesFactory. See MIGRATION_DOCS/TRANSITION_MAP.md.
using AgOpenGPS.Core.Platform;
using Avalonia;
using System;
using System.Linq;
using System.Reflection;
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
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
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
    }
}
