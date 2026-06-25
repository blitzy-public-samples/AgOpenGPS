// [XPLAT] migrated from net48/WinForms (Forms/FormFirmwareUpdate.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Placeholder window for future firmware-update functionality.
    /// Will integrate with TeensyLoaderCLI for updating Teensy firmware.
    /// Avalonia reimplementation of the WinForms <c>FormFirmwareUpdate</c>; it remains a
    /// non-functional placeholder (AAP parity rule) — no firmware logic is added.
    /// </summary>
    public partial class FormFirmwareUpdate : Window
    {
        public FormFirmwareUpdate()
        {
            // [XPLAT] Pattern B: InitializeComponent and the typed x:Name fields
            // (LblTitle / LblInfo / BtnClose) are emitted by the Avalonia source generator
            // from FormFirmwareUpdate.axaml.
            InitializeComponent();

            // [XPLAT] The multi-line placeholder body is assigned here (the WinForms original set
            // lblInfo.Text in its designer). This is a verbatim port of the source text; "\u2022"
            // is the bullet (•) glyph used by the original. LblInfo is intentionally left empty in
            // the XAML — its content is this coordination-contract assignment.
            LblInfo.Text = "Firmware Update Feature\n\nThis feature is under development.\n\nFuture capabilities will include:\n\n\u2022 Select firmware .hex file\n\u2022 Select communication port (from AgIO settings)\n\u2022 Start TeensyLoaderCLI with parameters\n\u2022 Upload firmware to Teensy device\n\u2022 Progress indicator\n\nTeensyLoader integration:\nteensy_loader_cli --mcu=TEENSY40 --port=COM3 firmware.hex";
        }

        /// <summary>
        /// [XPLAT] Closes the window. Replaces the WinForms <c>btnClose</c> whose
        /// <c>DialogResult = Cancel</c> dismissed the dialog (no firmware action is taken).
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

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
