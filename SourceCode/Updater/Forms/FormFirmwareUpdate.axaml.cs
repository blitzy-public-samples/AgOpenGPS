// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// Placeholder form for future firmware update functionality.
    /// Will integrate with TeensyLoaderCLI for updating Teensy firmware.
    /// </summary>
    public partial class FormFirmwareUpdate : Window
    {
        public FormFirmwareUpdate()
        {
            // [XPLAT] Pattern B: InitializeComponent and the typed x:Name fields
            // (LblTitle / LblInfo / BtnClose) are emitted by the Avalonia source generator
            // from FormFirmwareUpdate.axaml.
            InitializeComponent();

            // [XPLAT] The multi-line placeholder body is assigned here, mirroring how the WinForms
            // original set lblInfo.Text in its Designer. This is a verbatim port of the source text;
            // "•" (U+2022) is the bullet glyph from the original, and the example command line is
            // reproduced exactly. LblInfo is intentionally left empty in the XAML — its content is
            // this coordination-contract assignment. (Original used "\r\n"; "\n" renders identically
            // and is cross-platform safe.)
            LblInfo.Text =
                "Firmware Update Feature\n\n" +
                "This feature is under development.\n\n" +
                "Future capabilities will include:\n\n" +
                "• Select firmware .hex file\n" +
                "• Select communication port (from AgIO settings)\n" +
                "• Start TeensyLoaderCLI with parameters\n" +
                "• Upload firmware to Teensy device\n" +
                "• Progress indicator\n\n" +
                "TeensyLoader integration:\n" +
                "teensy_loader_cli --mcu=TEENSY40 --port=COM3 firmware.hex";
        }

        // TODO: Implement firmware update functionality
        //
        // Planned features:
        // - Select firmware .hex file
        // - Read COM port from AgIO settings
        // - Validate Teensy model (TEENSY40 for AgOpenGPS)
        // - Execute teensy_loader_cli with appropriate parameters
        // - Show upload progress
        // - Handle errors and retries
        //
        // Example command line:
        // teensy_loader_cli --mcu=TEENSY40 --port=COM3 firmware.hex
        //
        // Resources:
        // - https://www.pjrc.com/teensy/loader_cli.html
        // - https://github.com/PaulStoffregen/teensy_loader_cli

        /// <summary>
        /// [XPLAT] Closes the window. Replaces the WinForms <c>btnClose</c> whose
        /// <c>DialogResult = Cancel</c> dismissed the dialog (no firmware action is taken).
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>[XPLAT] Esc closes the window (WinForms <c>CancelButton = btnClose</c> parity).</summary>
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
