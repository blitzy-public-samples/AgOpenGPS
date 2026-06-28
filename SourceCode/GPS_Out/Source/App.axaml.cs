// [XPLAT] migrated from net48/WinForms (Program.cs + frmStart DayColour wiring) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using GPS_Out.Views;

namespace GPS_Out
{
    /// <summary>
    /// Avalonia application entry point for the GPS_Out NMEA bridge, replacing the WinForms
    /// <c>Application.Run(new frmStart())</c> bootstrap.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The day/night palette lives in <c>App.axaml</c>; on startup the persisted
    /// <c>Settings.DayColour</c> string ("R, G, B") is parsed and used to overwrite the
    /// <c>DayColor</c>/<c>DayBrush</c> resources so the saved colour is honoured exactly as the
    /// WinForms build applied <c>this.BackColor = Settings.DayColour</c>.
    /// </remarks>
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // [XPLAT] honour the persisted DayColour before the main window is shown.
            ApplyDayColour();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// [XPLAT] Parses the persisted <c>Settings.DayColour</c> value ("R, G, B") and overwrites the
        /// <c>DayColor</c>/<c>DayBrush</c> application resources. On any parse failure the XAML-declared
        /// default colour is retained (graceful degradation — never breaks startup).
        /// </summary>
        private void ApplyDayColour()
        {
            try
            {
                string raw = Properties.Settings.Default.DayColour;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                string[] parts = raw.Split(',');
                if (parts.Length < 3)
                {
                    return;
                }

                byte r = byte.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                byte g = byte.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                byte b = byte.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);

                Color dayColor = Color.FromRgb(r, g, b);
                Resources["DayColor"] = dayColor;
                Resources["DayBrush"] = new SolidColorBrush(dayColor);
            }
            catch (Exception ex)
            {
                // [XPLAT] graceful degradation: retain the XAML default DayColour if the persisted value is malformed.
                System.Diagnostics.Debug.WriteLine("GPS_Out: failed to parse Settings.DayColour - using default. " + ex.Message);
            }
        }
    }
}
