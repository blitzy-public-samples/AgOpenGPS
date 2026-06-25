// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ModSim.Views;

namespace ModSim
{
    /// <summary>
    /// ModSim Avalonia application. Replaces the WinForms <c>Application.Run(new FormSim())</c>
    /// bootstrap with the Avalonia application/lifetime model.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The original Program.cs constructed and ran <c>FormSim</c> directly; that role moves
    /// here: <see cref="OnFrameworkInitializationCompleted"/> creates the single
    /// <see cref="MainSimView"/> window for the classic desktop lifetime. The base Fluent theme is
    /// declared in App.axaml.
    /// </remarks>
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainSimView();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
