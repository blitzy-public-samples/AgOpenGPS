// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgOpenGPS.Core.Platform;
using Avalonia;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace AgIO
{
    internal static class Program
    {
        // [XPLAT] Single-instance identity preserved byte-for-byte from the WinForms build (was
        // `new Mutex(true, "{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}", out mutexCreated)`) so a mixed
        // fleet — and the AgOpenGPS<->AgIO two-program contract — keeps the identical guard name. The
        // guard is now acquired through IPlatformServices.TryAcquireSingleInstance (a named Mutex on
        // Windows; an exclusive lock-file on Linux/macOS) instead of a Windows-only named Mutex.
        private const string SingleInstanceName = "{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}";

        // [XPLAT] Cross-platform single-instance guard handle returned by
        // IPlatformServices.TryAcquireSingleInstance. Held in a static field for the whole process so it
        // is not released/garbage-collected before exit, and disposed by ReleaseSingleInstance() on
        // shutdown and before a Restart() relaunch. Replaces the former `private static Mutex _mutex;`.
        private static IDisposable _instanceLock;

        // [XPLAT] The single IPlatformServices instance created for this process. Exposed so the Avalonia
        // App composition root builds its services against the SAME instance rather than calling
        // PlatformServicesFactory.Create() a second time. The identical instance is also published to
        // RegistrySettings.PlatformServices in Main (the slot the rest of AgIO actually reads).
        internal static IPlatformServices PlatformServices { get; private set; }

        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString(3); // Major.Minor.Patch

        /// <summary>
        /// [XPLAT] Avalonia configuration used by both the runtime entry point and the XAML previewer.
        /// Replaces the WinForms <c>Application.EnableVisualStyles()</c> /
        /// <c>Application.SetCompatibleTextRenderingDefault(false)</c> setup. The Inter font is registered
        /// here so <c>App.axaml</c> needs no font include.
        /// </summary>
        /// <remarks>
        /// [XPLAT] G3 DESKTOP-GL EXEMPTION (review finding AgIO/Program.cs CRITICAL — "consistency").
        /// AgIO is the loopback comms hub: its views (Form*View) are plain Avalonia controls and AgIO hosts
        /// NO OpenGL surface — there is no <c>OpenGlControlBase</c>, no <c>AvaloniaGeoViewport</c>, no DrawLib/GLW
        /// usage and no <c>GL.ReadPixels</c> anywhere in the AgIO assembly. The desktop-GL request hook
        /// (<c>AvaloniaGeoViewport.RequestDesktopGlProfile</c>) lives in the GPS project's
        /// <c>AgOpenGPS.Controls</c> namespace, and AgIO deliberately does NOT reference the GPS project (the
        /// two-program model communicates only over UDP loopback — AAP §0.7.1 / R4), so that type is not — and
        /// must not become — visible here. Because AgIO never creates a GL context, forcing a desktop-GL profile
        /// would be inapplicable (it would only constrain a renderer that does not exist). The hook is therefore
        /// intentionally omitted; should a GL-hosting surface ever be added to AgIO, the desktop-GL profile must
        /// be requested at that point (replicating the GPS mitigation). See MIGRATION_DOCS/PARITY_REPORT.md.
        /// </remarks>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the WinForms bootstrap (<c>Application.EnableVisualStyles()</c> /
        /// <c>Application.Run(new FormLoop())</c>) with the Avalonia classic-desktop lifetime, and the
        /// Windows-only named <see cref="Mutex"/> single-instance guard with the cross-platform
        /// <see cref="IPlatformServices.TryAcquireSingleInstance"/> mechanism (a named Mutex on Windows;
        /// an exclusive lock-file on Linux/macOS). The original single-instance GUID is preserved verbatim,
        /// so the "only one AgIO" semantics and the AgOpenGPS&lt;-&gt;AgIO two-program contract are unchanged.
        /// The per-user UI culture is applied from the persisted profile before any UI is shown.
        /// </remarks>
        [STAThread]
        private static void Main(string[] args)
        {
            // [XPLAT] Strategy + Factory (AAP §0.3.2): AgIO owns its per-OS IPlatformServices
            // implementations in the AgIO.Services namespace — it does NOT reference the GPS project, so
            // the two-program model is preserved. Register the per-OS factories, then let
            // PlatformServicesFactory select the one matching the running OS. WindowsPlatformServices uses
            // Microsoft.Win32.Registry and so exists only on the net8.0-windows target; its registration is
            // fenced to the SDK-defined WINDOWS symbol. Register is null-tolerant, so on the plain net8.0
            // build the Windows slot is simply left unregistered.
            PlatformServicesFactory.Register(
#if WINDOWS
                windowsFactory: () => new AgIO.Services.WindowsPlatformServices(),
#endif
                linuxFactory: () => new AgIO.Services.LinuxPlatformServices(),
                macFactory: () => new AgIO.Services.MacPlatformServices());

            IPlatformServices platform = PlatformServicesFactory.Create();

            // [XPLAT] Publish the single shared instance. PlatformServices is the App-facing contract;
            // RegistrySettings.PlatformServices is the slot RegistrySettings.Load() reads (to resolve the
            // cross-platform config root via IPlatformServices.AppDataRoot) and the slot App's idempotent
            // EnsurePlatformServices() checks — populating it here means App never calls Create() again.
            PlatformServices = platform;
            RegistrySettings.PlatformServices = platform;

            // [XPLAT] Cross-platform single-instance guard (was `new Mutex(true, GUID, out created)`). The
            // preserved GUID is the identifier; when another AgIO instance already holds the guard this
            // launch exits quietly, matching the WinForms behaviour where the second instance never created
            // a FormLoop. Console.Error is used (not the file logger) because logging is initialised by
            // RegistrySettings.Load(), which has not run yet.
            if (!platform.TryAcquireSingleInstance(SingleInstanceName, out IDisposable instanceLock))
            {
                Console.Error.WriteLine("AgIO is already running.");
                return;
            }

            _instanceLock = instanceLock;

            try
            {
                // load the profile name and set profile directory (now read from the cross-platform config
                // root via IPlatformServices.AppDataRoot, which was wired up above).
                RegistrySettings.Load();

                Log.EventWriter("Program Started: " + DateTime.Now.ToString("f", CultureInfo.InvariantCulture));
                Log.EventWriter("AgIO Version: " + Version);

                // [XPLAT] Preserve the per-user UI culture from settings (was set on the WinForms thread).
                CultureInfo culture = new CultureInfo(RegistrySettings.culture);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;

                // [XPLAT] Data-integrity contract (AAP §0.6.5 — highest data-integrity risk): the two lines
                // above set ONLY the per-user UI culture; they deliberately do NOT set
                // CultureInfo.DefaultThreadCurrentCulture, which would change numeric formatting on every
                // thread and could both corrupt data and break the localized UI. All numeric PGN/protocol/
                // file I/O — the extracted comm services in Services/ (PGN-derived text, NMEA parsing, NTRIP
                // GGA) and the settings XML in Properties/ — MUST use CultureInfo.InvariantCulture explicitly
                // so a Linux/macOS locale with a comma decimal separator cannot corrupt it. Those
                // double.Parse/ToString audits live in Services/ and Properties/; Program.cs only applies the
                // UI culture and documents the split.

                // [XPLAT] Avalonia equivalent of Application.Run(new FormLoop()). The AgIO main window, its
                // view-model, and the extracted comm services are wired in
                // App.OnFrameworkInitializationCompleted — not here.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                // [XPLAT] Release the single-instance guard on shutdown so the next launch can acquire it
                // (and so the Linux/macOS lock-file is cleaned up). Idempotent: a no-op after Restart().
                ReleaseSingleInstance();
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms <c>Application.Restart()</c>. The original
        /// released and disposed the single-instance Mutex <em>before</em> calling
        /// <c>Application.Restart()</c> so the relaunched instance would not fail the guard; that
        /// release-before-relaunch ordering is preserved here. Avalonia has no <c>Application.Restart()</c>,
        /// so the current executable is relaunched as a new process and the running classic-desktop lifetime
        /// is then shut down. The public signature is unchanged because AgIO dialogs (FormEthernet, FormNtrip,
        /// FormSerialPass, FormUDP) invoke <see cref="Restart"/> from their restart flows.
        /// </summary>
        public static void Restart()
        {
            // [XPLAT] release the single-instance guard before relaunch (was Mutex.ReleaseMutex + Dispose,
            // then Application.Restart). Releasing first lets the relaunched instance re-acquire the guard.
            ReleaseSingleInstance();

            string exePath = Environment.ProcessPath; // .NET 6+; cross-platform path to the current executable
            if (!string.IsNullOrEmpty(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
            }

            // [XPLAT] request the Avalonia desktop lifetime to shut down the current instance (replaces the
            // WinForms-only Application.Restart() teardown); fall back to a hard exit if there is no desktop
            // lifetime (for example under the XAML previewer or a unit-test host).
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }

        // [XPLAT] Release + dispose the cross-platform single-instance guard exactly once. Idempotent, so it
        // is safe to call from both Main's finally and Restart() (disposing the guard releases the named
        // Mutex on Windows / the exclusive lock-file on Linux/macOS).
        private static void ReleaseSingleInstance()
        {
            _instanceLock?.Dispose();
            _instanceLock = null;
        }
    }
}
