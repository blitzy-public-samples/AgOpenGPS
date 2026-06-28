// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AgDiag
{
    /// <summary>
    /// AgDiag Avalonia application. Replaces the WinForms <c>Application.Run(new FormLoop())</c> bootstrap
    /// with the Avalonia application/lifetime model.
    /// </summary>
    /// <remarks>
    /// [XPLAT] AgDiag is a fixed dark diagnostics tool; it uses the default Fluent theme declared in
    /// App.axaml (the window paints its own #1E1E32 background), so no runtime day/night switching is
    /// required here. <see cref="OnFrameworkInitializationCompleted"/> creates the single
    /// <see cref="FormLoop"/> window for the classic desktop lifetime.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the XAML defined in App.axaml (applying the Fluent theme) into this application
        /// instance. Invoked by the Avalonia runtime during <c>AppBuilder</c> setup.
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Creates and assigns the single main window once the Avalonia framework has finished
        /// initializing. For the classic desktop lifetime this is the re-platformed
        /// <see cref="FormLoop"/> diagnostics window — the Avalonia equivalent of the former
        /// WinForms <c>Application.Run(new FormLoop())</c>.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new FormLoop();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
