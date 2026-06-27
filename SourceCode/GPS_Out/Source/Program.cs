// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;

namespace GPS_Out
{
    internal static class Program
    {
        // Avalonia configuration, don't remove; also used by the visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the WinForms <c>Application.EnableVisualStyles()</c> /
        /// <c>Application.Run(new frmStart())</c> bootstrap with the Avalonia classic-desktop lifetime.
        /// The original GPS_Out had no single-instance Mutex (unlike AgOpenGPS/AgIO), so none is added
        /// here — multiple-instance behaviour is preserved exactly.
        /// </remarks>
        [STAThread]
        public static void Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}
