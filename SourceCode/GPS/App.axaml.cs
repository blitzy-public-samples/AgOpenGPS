// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace AgOpenGPS
{
    /// <summary>
    /// AgOpenGPS (GPS program) Avalonia application. Pairs with <c>App.axaml</c>
    /// (<c>x:Class="AgOpenGPS.App"</c>) and replaces the WinForms
    /// <c>Application.Run(new FormGPS())</c> bootstrap with the Avalonia application/theme model.
    /// </summary>
    /// <remarks>
    /// [XPLAT] There was no WinForms <c>App</c> equivalent in the source; the original
    /// <c>Program.cs</c> constructed and ran <see cref="Forms.FormGPS"/> directly. Per AAP §0.3.3 this
    /// is a 1:1 visual-parity reimplementation of the current Windows Forms look, never a redesign.
    ///
    /// Theme: the base Fluent theme and the day/night palette (the <c>Aog*Color</c>/<c>Aog*Brush</c>
    /// resources under <c>Light</c>/<c>Dark</c> theme dictionaries) are declared in <c>App.axaml</c>.
    /// Day/night is driven at runtime from the Core day/night state
    /// (<see cref="AgOpenGPS.Core.ViewModels.DayNightAndUnitsViewModel.IsDay"/>, surfaced through the
    /// deprecated <c>FormGPS.isDay</c> shim) by calling <see cref="SetDayNightMode(bool)"/>, which sets
    /// <see cref="Application.RequestedThemeVariant"/> to <see cref="ThemeVariant.Light"/> (day) or
    /// <see cref="ThemeVariant.Dark"/> (night). That single switch flips the whole palette, mirroring
    /// the WinForms <c>FormGPS.SwapDayNightMode()</c> behaviour.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>
        /// Loads the XAML defined in <c>App.axaml</c> (Fluent theme + day/night theme dictionaries).
        /// </summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Establishes the initial theme variant once the framework is initialised. Day is the product
        /// default (the WinForms build started with <c>isDay == true</c>), so the application opens on the
        /// <c>Light</c> palette; the day/night view-model subsequently toggles it via
        /// <see cref="SetDayNightMode(bool)"/>.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            // [XPLAT] day is the product default — open on the Light (day) palette declared in App.axaml.
            SetDayNightMode(true);

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// [XPLAT] Applies the day/night palette by switching the application-wide theme variant, which
        /// re-resolves every <c>{DynamicResource Aog*Brush}</c> reference to the matching
        /// <c>Light</c>/<c>Dark</c> theme dictionary. This is the runtime equivalent of the WinForms
        /// <c>FormGPS.SwapDayNightMode()</c> chrome recolour.
        /// </summary>
        /// <param name="isDay">
        /// <see langword="true"/> selects the day (<see cref="ThemeVariant.Light"/>) palette;
        /// <see langword="false"/> selects the night (<see cref="ThemeVariant.Dark"/>) palette.
        /// </param>
        public void SetDayNightMode(bool isDay)
        {
            RequestedThemeVariant = isDay ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        /// <summary>
        /// [XPLAT] Convenience entry point for the view-model / presenter layer, which observes the Core
        /// day/night state without holding a reference to the <see cref="App"/> instance. Resolves the
        /// running application and applies the requested palette; safe to call when no Avalonia
        /// application is current (no-op).
        /// </summary>
        /// <param name="isDay">
        /// <see langword="true"/> for the day palette; <see langword="false"/> for the night palette.
        /// </param>
        public static void ApplyDayNightMode(bool isDay)
        {
            if (Current is App app)
            {
                app.SetDayNightMode(isDay);
            }
        }
    }
}
