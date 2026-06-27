// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgIO.Services;
using AgIO.Views;
using AgLibrary.Logging;
using AgOpenGPS.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AgIO
{
    /// <summary>
    /// Avalonia application entry point and composition root for AgIO, the cross-platform
    /// communications-hub program (serial / NTRIP / UDP loopback; the second of the two programs,
    /// auto-started by AgOpenGPS over the UDP loopback fabric). Pairs with <c>App.axaml</c>
    /// (<c>x:Class="AgIO.App"</c>) and replaces the WinForms <c>Application.Run(new FormLoop())</c>
    /// bootstrap with the Avalonia classic-desktop lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] There was no WinForms <c>App</c> equivalent in the source; the original
    /// <c>Program.cs</c> constructed and ran <c>FormLoop</c> directly. Per AAP §0.3.3 this is a 1:1
    /// behavioural/visual-parity reimplementation, never a redesign. AgIO is <b>light-themed</b> (no
    /// day/night toggle): the base Fluent theme, the pinned <c>Light</c> variant, and the module/NTRIP
    /// status palette are declared in <c>App.axaml</c>, so — unlike the sibling GPS <c>App</c> — this
    /// code-behind contains no theme-variant switching logic.
    /// </para>
    /// <para>
    /// Responsibilities of this composition root (what it does and, deliberately, what it does not):
    /// </para>
    /// <list type="bullet">
    /// <item><description>It creates the AgIO main window (<see cref="MainWindow"/>) and assigns it to the
    /// desktop lifetime — the Avalonia equivalent of <c>Application.Run(new FormLoop())</c>.</description></item>
    /// <item><description>It publishes the cross-platform <see cref="IPlatformServices"/> instance into the
    /// shared <see cref="RegistrySettings.PlatformServices"/> slot <i>before</i> the window is created, so
    /// the comm services that <see cref="MainWindow"/> constructs can enumerate serial port names on every
    /// OS. AgIO references <c>AgOpenGPS.Core</c> only for this abstraction — it uses neither
    /// <c>ApplicationCore</c> nor any GPS view-model.</description></item>
    /// <item><description>It does <b>not</b> construct the comm services itself and sets <b>no</b>
    /// <c>DataContext</c>: <see cref="MainWindow"/> is a self-contained code-behind shell whose own
    /// constructor is the comms-hub composition root (it builds and wires the UDP / NMEA / serial / NTRIP
    /// services and arms the one-second scan loop), exactly mirroring how the WinForms <c>FormLoop</c> owned
    /// those collaborators and its loop timer.</description></item>
    /// <item><description>It adds <b>no</b> shutdown hook: socket/serial teardown is owned by
    /// <c>MainWindow.OnClosing</c> (mirrors <c>FormLoop_FormClosing</c>) and the single-instance lock is
    /// owned and released by <c>Program</c> (its <c>finally</c> releases the named Mutex). Duplicating that
    /// teardown here would be redundant and could not reach <see cref="MainWindow"/>'s private
    /// services.</description></item>
    /// </list>
    /// <para>
    /// [XPLAT] Provenance note: the file brief envisaged reusing a <c>Program.PlatformServices</c> property,
    /// but the migrated AgIO <c>Program.cs</c> guards single-instance with a named
    /// <see cref="System.Threading.Mutex"/> and exposes no such property; the shared platform slot the rest
    /// of AgIO actually reads is <see cref="RegistrySettings.PlatformServices"/>, so this root wires that
    /// slot instead. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the XAML defined in <c>App.axaml</c> (Fluent theme pinned to the Light variant plus the
        /// shared module / NTRIP status palette). Mirrors the sibling GPS <c>App.Initialize</c>.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Creates and assigns the AgIO main window once the framework is initialised, replacing the
        /// WinForms <c>Application.Run(new FormLoop())</c>. Before the window is built it ensures the
        /// cross-platform <see cref="IPlatformServices"/> instance is available to the comm services that
        /// <see cref="MainWindow"/>'s constructor wires up.
        /// </summary>
        /// <remarks>
        /// Order is significant: the design-mode guard short-circuits the XAML previewer (so it never spins
        /// up platform services or the comms hub); the desktop-lifetime guard confirms the classic desktop
        /// host configured by <c>Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime</c>;
        /// <see cref="EnsurePlatformServices"/> runs <i>before</i> <c>new MainWindow()</c> because the
        /// <see cref="MainWindow"/> constructor reads <see cref="RegistrySettings.PlatformServices"/>; and
        /// <see cref="Application.OnFrameworkInitializationCompleted"/> is always called last, mirroring the
        /// sibling GPS <c>App</c>.
        /// </remarks>
        public override void OnFrameworkInitializationCompleted()
        {
            // [XPLAT] Design-mode guard first: the Avalonia XAML previewer must never start platform
            // services or the comms hub (sockets / single-instance). Avalonia.Controls.Design.IsDesignMode.
            if (Design.IsDesignMode)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // [XPLAT] Wire the cross-platform platform-services abstraction into the shared bootstrap
                // slot BEFORE the window is created: MainWindow's constructor (the comms-hub composition
                // root) reads RegistrySettings.PlatformServices to give SerialCommService cross-platform
                // serial port-name enumeration.
                EnsurePlatformServices();

                // [XPLAT] The Avalonia equivalent of Application.Run(new FormLoop()). MainWindow is a
                // self-contained code-behind shell (no DataContext): its own constructor builds and wires
                // the UDP / NMEA / serial / NTRIP services and arms the one-second scan loop, and its
                // OnClosing performs the FormLoop_FormClosing teardown.
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// [XPLAT] Selects and publishes the cross-platform <see cref="IPlatformServices"/> implementation for
        /// the running operating system, storing it in the shared <see cref="RegistrySettings.PlatformServices"/>
        /// slot that <see cref="MainWindow"/> reads. Implements the AAP §0.3.2 Strategy + Factory pattern via
        /// <see cref="PlatformServicesFactory"/>.
        /// </summary>
        /// <remarks>
        /// AgIO owns its per-OS implementations in the <see cref="AgIO.Services"/> namespace (it does not
        /// reference the GPS project): <c>WindowsPlatformServices</c> is compiled only under the
        /// <c>net8.0-windows</c> target (the SDK-defined <c>WINDOWS</c> symbol), so its registration is fenced
        /// to match, while the portable <c>LinuxPlatformServices</c> and <c>MacPlatformServices</c> compile on
        /// every target. The method is idempotent (it no-ops when the slot is already populated, so it never
        /// re-creates or double-registers) and exception-safe: a missing or failed implementation must never
        /// block startup — <c>SerialCommService</c> null-guards a null provider, so AgIO still launches and the
        /// loopback / NTRIP comm fabric runs unchanged (graceful degradation, AAP §0.6.3 / §0.6.5).
        /// </remarks>
        private static void EnsurePlatformServices()
        {
            // [XPLAT] Defensive: never re-create or double-register if a platform instance is already wired.
            if (RegistrySettings.PlatformServices != null)
            {
                return;
            }

            try
            {
                // [XPLAT] Register the per-OS factories AgIO owns, then let PlatformServicesFactory select the
                // one matching the running OS. The WINDOWS-fenced call additionally registers the Windows
                // implementation, which exists only on the net8.0-windows target.
#if WINDOWS
                PlatformServicesFactory.Register(
                    windowsFactory: () => new WindowsPlatformServices(),
                    linuxFactory: () => new LinuxPlatformServices(),
                    macFactory: () => new MacPlatformServices());
#else
                PlatformServicesFactory.Register(
                    linuxFactory: () => new LinuxPlatformServices(),
                    macFactory: () => new MacPlatformServices());
#endif

                RegistrySettings.PlatformServices = PlatformServicesFactory.Create();
            }
            catch (Exception ex)
            {
                // [XPLAT] Graceful degradation: continue without platform services rather than fail startup.
                // Logged via the existing AgLibrary logger (string concatenation only — no culture-sensitive
                // numeric formatting, per AAP §0.6.5).
                Log.EventWriter("AgIO -> platform services unavailable, continuing without them: " + ex.Message);
            }
        }
    }
}
