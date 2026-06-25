// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgOpenGPS.Updater.Forms;

namespace AgOpenGPS.Updater
{
    /// <summary>
    /// [XPLAT] Avalonia application class for the cross-platform AgOpenGPS Updater.
    /// Replaces the WinForms <c>Application.EnableVisualStyles()</c> / <c>Application.Run(Form)</c>
    /// bootstrap that previously lived in <see cref="Program"/>. The startup window is selected from
    /// the same command-line flags the old <c>Program.Main</c> parsed (<c>--firmware</c> chooses the
    /// firmware placeholder; otherwise the main updater flow), preserving launch behavior exactly.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the compiled XAML for this application (styles + the semantic Accent/Error/Success
        /// brushes consumed by the updater dialogs).
        /// </summary>
        public override void Initialize()
        {
            // [XPLAT] Pattern B: the Avalonia source generator emits InitializeComponent and the
            // typed x:Name fields for every view; the application itself just loads its XAML here.
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Selects and assigns the main window once the framework is initialized. Mirrors the
        /// original <c>Program.Main</c> logic: parse <c>--current-version</c>, <c>--install-path</c>
        /// and <c>--firmware</c>, then show either <see cref="FormFirmwareUpdate"/> or
        /// <see cref="FormUpdate"/>.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                string currentVersion = null;
                string installPath = null;
                bool showFirmwareUpdate = false;

                // [XPLAT] desktop.Args carries the process command line (WinForms read
                // Environment.GetCommandLineArgs()); parse the same three flags as the old entry point.
                string[] args = desktop.Args ?? Array.Empty<string>();
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
                        showFirmwareUpdate = true;
                    }
                }

                // Show the appropriate window (parity with the WinForms Application.Run selection).
                if (showFirmwareUpdate)
                {
                    desktop.MainWindow = new FormFirmwareUpdate();
                }
                else
                {
                    desktop.MainWindow = new FormUpdate(currentVersion, installPath);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
