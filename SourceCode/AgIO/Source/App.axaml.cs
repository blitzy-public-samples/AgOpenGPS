// [XPLAT] migrated from net48/WinForms (Program.cs Application.Run(new FormLoop())) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgIO.Views;

namespace AgIO
{
    /// <summary>
    /// Avalonia application entry point for AgIO, the cross-platform comms-hub program. Pairs with
    /// <c>App.axaml</c> (<c>x:Class="AgIO.App"</c>) and replaces the WinForms
    /// <c>Application.Run(new FormLoop())</c> bootstrap with the Avalonia classic-desktop lifetime.
    /// </summary>
    /// <remarks>
    /// [XPLAT] There was no WinForms <c>App</c> equivalent in the source; the original
    /// <c>Program.cs</c> constructed and ran <c>FormLoop</c> directly. Per AAP §0.3.3 this is a 1:1
    /// behavioural/visual-parity reimplementation, never a redesign. The base Fluent theme and the
    /// module/NTRIP status palette are declared in <c>App.axaml</c>; the comms-hub composition root
    /// (instantiating and wiring <see cref="MainWindow"/>'s UDP/serial/NTRIP/NMEA services and running
    /// the one-second scan loop) lives in <see cref="MainWindow"/>, exactly mirroring the way the
    /// WinForms <c>FormLoop</c> owned those collaborators and its loop timer.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the XAML defined in <c>App.axaml</c> (Fluent theme + status palette).
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Creates and assigns the AgIO main window once the framework is initialised, replacing the
        /// WinForms <c>Application.Run(new FormLoop())</c>. The <see cref="MainWindow"/> constructor is the
        /// comms-hub composition root: it instantiates and wires the transport services and starts the
        /// scan loop.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
