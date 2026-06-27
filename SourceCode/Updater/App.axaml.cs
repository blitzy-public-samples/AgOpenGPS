// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgOpenGPS.Updater.Forms;

namespace AgOpenGPS.Updater
{
    /// <summary>
    /// [XPLAT] Avalonia <see cref="Application"/> for the cross-platform AgOpenGPS Updater. This
    /// code-behind is paired with <c>App.axaml</c> (<c>x:Class="AgOpenGPS.Updater.App"</c>), which
    /// supplies the base Fluent theme and the semantic color palette the updater dialogs consume.
    /// </summary>
    /// <remarks>
    /// It replaces the WinForms bootstrap that used to live in <c>Program.Main</c>
    /// (<c>Application.EnableVisualStyles()</c> / <c>Application.SetCompatibleTextRenderingDefault(false)</c>
    /// followed by <c>Application.Run(new FormUpdate(...))</c>). The command-line parsing and the
    /// firmware-vs-update window selection that lived in that <c>Main</c> are reproduced here in
    /// <see cref="OnFrameworkInitializationCompleted"/>; <see cref="Program"/> now simply forwards the
    /// process arguments to the classic-desktop lifetime so they surface as <c>desktop.Args</c>.
    ///
    /// The updater is intentionally self-contained: it does NOT reference <c>AgOpenGPS.Core</c> or
    /// <c>IPlatformServices</c>, and it deliberately performs no single-instance/mutex, settings, or
    /// culture setup here. It is a short-lived child process launched by AgOpenGPS, and the
    /// update-activity signalling mutex lives in <c>Services/UpdateService.cs</c> (AAP §0.3.2, §0.4.2).
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the compiled XAML declared in <c>App.axaml</c> (the Fluent theme plus the shared
        /// Accent/Error/Success/panel brushes), making those application-level resources available to
        /// every updater window.
        /// </summary>
        public override void Initialize()
        {
            // [XPLAT] MUST load App.axaml so the theme and palette resource dictionary are applied;
            // the Avalonia source generator emits the InitializeComponent/x:Name members for the views.
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Selects and assigns the startup window once the framework is initialized, mirroring the
        /// original <c>Program.Main</c>: show <see cref="FormFirmwareUpdate"/> when <c>--firmware</c> is
        /// present, otherwise the main <see cref="FormUpdate"/> seeded with the command-line
        /// <c>--current-version</c> and <c>--install-path</c> values.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            // Only a classic desktop lifetime exposes a single top-level MainWindow to assign; this
            // guard also keeps the design-time / previewer host (a different lifetime) working.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = BuildStartupWindow(desktop.Args ?? Array.Empty<string>());
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Parses the startup arguments and constructs the matching updater window, preserving the
        /// original WinForms routing exactly: <c>--firmware</c> selects <see cref="FormFirmwareUpdate"/>;
        /// otherwise a <see cref="FormUpdate"/> is built with the parsed current-version and install-path
        /// so the install path and version flow through to the update flow unchanged.
        /// </summary>
        /// <param name="args">
        /// The process command-line arguments as exposed by the classic-desktop lifetime
        /// (<c>desktop.Args</c>). Unlike <see cref="Environment.GetCommandLineArgs"/> — whose element 0 is
        /// the executable path, which is why the source-branch loop started at index 1 — this array
        /// contains only the real arguments, so <see cref="ParseStartupArgs"/> scans it from index 0.
        /// </param>
        /// <returns>The Avalonia <see cref="Window"/> to display as the application's main window.</returns>
        private static Window BuildStartupWindow(string[] args)
        {
            (string currentVersion, string installPath, bool showFirmware) = ParseStartupArgs(args);

            // Parity with the WinForms selection:
            // showFirmwareUpdate ? new FormFirmwareUpdate() : new FormUpdate(currentVersion, installPath).
            return showFirmware
                ? new FormFirmwareUpdate()
                : new FormUpdate(currentVersion, installPath);
        }

        /// <summary>
        /// Pure (side-effect-free) reproduction of the original <c>Program.Main</c> argument loop. It is
        /// kept separate from <see cref="BuildStartupWindow"/> so the parity-critical parsing can be
        /// exercised in isolation, without constructing Avalonia windows.
        /// </summary>
        /// <param name="args">The raw startup arguments (no executable-path element; scanned from 0).</param>
        /// <returns>
        /// The parsed <c>currentVersion</c> and <c>installPath</c> (each <see langword="null"/> when the
        /// corresponding flag is absent) together with whether <c>--firmware</c> was requested.
        /// </returns>
        private static (string currentVersion, string installPath, bool showFirmware) ParseStartupArgs(string[] args)
        {
            string currentVersion = null;
            string installPath = null;
            bool showFirmware = false;

            if (args != null)
            {
                // Identical flag set, comparison mode (OrdinalIgnoreCase) and "i + 1 < args.Length"
                // value-token bounds checks as the source-branch Program.Main loop.
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    if (arg.Equals("--current-version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        currentVersion = args[++i];
                    }
                    else if (arg.Equals("--install-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        installPath = args[++i];
                    }
                    else if (arg.Equals("--firmware", StringComparison.OrdinalIgnoreCase))
                    {
                        showFirmware = true;
                    }
                }
            }

            return (currentVersion, installPath, showFirmware);
        }
    }
}
