// [XPLAT] migrated from net48/WinForms (Forms/FormFirmwareUpdate.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Placeholder window for future firmware-update functionality.
    /// Will integrate with TeensyLoaderCLI for updating Teensy firmware.
    /// Avalonia reimplementation of the WinForms <c>FormFirmwareUpdate</c>.
    /// </summary>
    public partial class FormFirmwareUpdate : Window
    {
        public FormFirmwareUpdate()
        {
            // [XPLAT] Pattern B: InitializeComponent and the typed x:Name fields are emitted by the
            // Avalonia source generator from FormFirmwareUpdate.axaml.
            InitializeComponent();
        }

        // TODO note from the original placeholder is preserved verbatim in the XAML lblInfo body.
        // (See FormFirmwareUpdate.axaml — Select firmware .hex / read COM port / validate Teensy /
        //  run teensy_loader_cli / show progress / handle errors.)

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// [XPLAT] Esc closes the window, replacing the WinForms <c>CancelButton = btnClose</c> behavior.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
