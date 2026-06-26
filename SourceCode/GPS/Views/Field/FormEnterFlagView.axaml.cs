// [XPLAT] migrated from net48/WinForms (Forms/Field/FormEnterFlag.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Add Flag" dialog.
//
// Parity notes vs the WinForms original (FormEnterFlag):
//   * The dialog adds a flag at the current GPS position in one of three colours (red / green /
//     yellow) and offers CSV import / export of the field's flags. The WinForms ctor set the caption
//     from gStr.gsFormFlag, the "Point" label from gStr.gsPoint, disabled the two NumericUpDown spin
//     boxes (read-only) and seeded them from mf.AppModel.CurrentLatLon — all reproduced here.
//   * nudLatitude / nudLongitude reproduce the WinForms NudlessNumericUpDown: read-only numeric
//     DISPLAY buttons whose tap pops the on-screen numeric keypad (FormNumeric). The markup declares
//     no Click for them (only x:Name), so the clicks are wired here and raise CoordinateEditRequested;
//     the host opens its keypad (FormNumeric, Views/Inputs) and calls SetCoordinates with the result.
//     Values are formatted with InvariantCulture so the display is locale-independent across
//     Windows / Linux / macOS (a cross-cutting parity requirement — AAP §0.6.5).
//   * btnRed / btnGreen / btnYellow were all routed through ONE WinForms handler that branched on the
//     button Name to a colour byte (red=0, green=1, yellow=2). That exact mapping is preserved here in
//     a single shared handler; instead of reaching into the FormGPS god-object (add CFlag, deduplicate,
//     FileSaveFlags, open FormFlags) — which is being decoupled into injectable services per AAP
//     §0.3.2 — the view raises FlagAddRequested carrying the colour and the current coordinates. The
//     host applies the flag, persists it, opens FormFlagsView, and decides whether to close this dialog
//     (reproducing the WinForms conditional Close: if a FormFlags window is already open it is focused
//     and this dialog stays; otherwise FormFlags opens and this dialog closes). This is the
//     dependency-inversion seam used consistently across the migrated GPS dialogs (e.g. FormSimCoordsView).
//   * btnImportFlags / btnExportFlags reproduce the WinForms CSV import / export. The WinForms code used
//     OpenFileDialog / SaveFileDialog and read/wrote the flag list directly; the file picker and the
//     flag-list I/O are host-owned (via Avalonia's StorageProvider) — so the view raises
//     ImportFlagsRequested / ExportFlagsRequested and does not close on import/export (matching WinForms,
//     which kept the dialog open).
//   * btnCancel closes the dialog (WinForms DialogResult.Cancel / btnCancel_Click -> Close()).
//
// This view holds NO reference to FormGPS; all host-coupled work flows through the events below.

using System;
using System.Globalization;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    public partial class FormEnterFlagView : Window
    {
        /// <summary>
        /// Flag colour, with the byte value matching the WinForms <c>CFlag</c> colour argument exactly
        /// (red = 0, green = 1, yellow = 2). Backed by <see cref="byte"/> so it can be passed straight
        /// through to the flag model without a conversion table.
        /// </summary>
        public enum FlagColor : byte
        {
            Red = 0,
            Green = 1,
            Yellow = 2
        }

        /// <summary>Identifies which coordinate field requested an edit (drives the host keypad).</summary>
        public enum CoordinateField
        {
            Latitude,
            Longitude
        }

        /// <summary>Event payload for a coordinate edit request raised from a numeric display tap.</summary>
        public sealed class CoordinateEditEventArgs : EventArgs
        {
            public CoordinateEditEventArgs(CoordinateField field, double currentValue)
            {
                Field = field;
                CurrentValue = currentValue;
            }

            /// <summary>The field (latitude or longitude) whose value the operator tapped to edit.</summary>
            public CoordinateField Field { get; }

            /// <summary>The current value of that field, to seed the keypad.</summary>
            public double CurrentValue { get; }
        }

        /// <summary>
        /// Event payload describing a request to add a flag: the chosen colour plus the coordinates the
        /// flag should be placed at (the dialog's current latitude / longitude). The host builds and
        /// persists the flag, exactly as the WinForms <c>btnRed_Click</c> handler did.
        /// </summary>
        public sealed class FlagAddEventArgs : EventArgs
        {
            public FlagAddEventArgs(FlagColor color, double latitude, double longitude)
            {
                Color = color;
                Latitude = latitude;
                Longitude = longitude;
            }

            /// <summary>The chosen flag colour.</summary>
            public FlagColor Color { get; }

            /// <summary>The flag colour as the raw byte the WinForms <c>CFlag</c> constructor expects.</summary>
            public byte ColorByte => (byte)Color;

            /// <summary>The latitude (degrees) at which to place the flag.</summary>
            public double Latitude { get; }

            /// <summary>The longitude (degrees) at which to place the flag.</summary>
            public double Longitude { get; }
        }

        public FormEnterFlagView()
        {
            InitializeComponent();

            // Reproduce the WinForms ctor: localise the caption and the "Point" label. The .axaml carries
            // only the design-time caption; the localised strings are assigned here at runtime.
            Title = gStr.gsFormFlag;
            labelPoint.Text = gStr.gsPoint;

            // The numeric displays declare no Click in markup; wire them here to request a keypad edit
            // from the host (parity with NudlessNumericUpDown.ShowKeypad).
            nudLatitude.Click += NudLatitude_Click;
            nudLongitude.Click += NudLongitude_Click;

            // All three colour buttons share one handler that maps the button to a colour byte, exactly
            // as the single WinForms btnRed_Click handler did.
            btnRed.Click += FlagButton_Click;
            btnGreen.Click += FlagButton_Click;
            btnYellow.Click += FlagButton_Click;

            // CSV import / export and cancel.
            btnImportFlags.Click += BtnImportFlags_Click;
            btnExportFlags.Click += BtnExportFlags_Click;
            btnCancel.Click += BtnCancel_Click;

            UpdateDisplays();
        }

        /// <summary>Currently displayed latitude (degrees, +90..-90).</summary>
        public double Latitude { get; private set; }

        /// <summary>Currently displayed longitude (degrees, +180..-180).</summary>
        public double Longitude { get; private set; }

        /// <summary>
        /// Host entry point: seed / update the displayed coordinates — e.g. from the current GPS fix
        /// (the WinForms ctor read mf.AppModel.CurrentLatLon) or from a keypad result. Values are clamped
        /// to the WinForms NumericUpDown ranges (latitude +-90, longitude +-180) before display.
        /// </summary>
        public void SetCoordinates(double latitude, double longitude)
        {
            Latitude = Math.Max(-90.0, Math.Min(90.0, latitude));
            Longitude = Math.Max(-180.0, Math.Min(180.0, longitude));
            UpdateDisplays();
        }

        /// <summary>Raised when a numeric display is tapped; the host opens its numeric keypad (FormNumeric).</summary>
        public event EventHandler<CoordinateEditEventArgs> CoordinateEditRequested;

        /// <summary>
        /// Raised when a colour flag button is pressed; the host adds and persists the flag and opens
        /// FormFlagsView (reproducing the WinForms add-flag / open-FormFlags / close sequence).
        /// </summary>
        public event EventHandler<FlagAddEventArgs> FlagAddRequested;

        /// <summary>Raised when Import Flags is pressed; the host opens a file picker and imports the CSV/txt.</summary>
        public event EventHandler ImportFlagsRequested;

        /// <summary>Raised when Export Flags is pressed; the host opens a save picker and exports the CSV.</summary>
        public event EventHandler ExportFlagsRequested;

        // Format with InvariantCulture and the WinForms DecimalPlaces = 7 precision so the displayed text
        // is identical on every OS regardless of the active locale's decimal separator.
        private void UpdateDisplays()
        {
            nudLatitude.Content = Latitude.ToString("0.0000000", CultureInfo.InvariantCulture);
            nudLongitude.Content = Longitude.ToString("0.0000000", CultureInfo.InvariantCulture);
        }

        private void NudLatitude_Click(object sender, RoutedEventArgs e)
        {
            CoordinateEditRequested?.Invoke(this, new CoordinateEditEventArgs(CoordinateField.Latitude, Latitude));
        }

        private void NudLongitude_Click(object sender, RoutedEventArgs e)
        {
            CoordinateEditRequested?.Invoke(this, new CoordinateEditEventArgs(CoordinateField.Longitude, Longitude));
        }

        // Shared colour-flag handler — preserves the WinForms colour mapping (red=0, green=1, yellow=2)
        // via reference comparison to the named buttons (robust against name typos). The host receives
        // the colour and the current coordinates and performs the add / persist / open-FormFlags work.
        private void FlagButton_Click(object sender, RoutedEventArgs e)
        {
            FlagColor color;
            if (ReferenceEquals(sender, btnGreen))
            {
                color = FlagColor.Green;
            }
            else if (ReferenceEquals(sender, btnYellow))
            {
                color = FlagColor.Yellow;
            }
            else
            {
                // btnRed (and the safe default) — WinForms initialised flagColor = 0 (red).
                color = FlagColor.Red;
            }

            FlagAddRequested?.Invoke(this, new FlagAddEventArgs(color, Latitude, Longitude));
        }

        private void BtnImportFlags_Click(object sender, RoutedEventArgs e)
        {
            ImportFlagsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnExportFlags_Click(object sender, RoutedEventArgs e)
        {
            ExportFlagsRequested?.Invoke(this, EventArgs.Empty);
        }

        // Cancel: close the dialog (WinForms DialogResult.Cancel / btnCancel_Click -> Close()).
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
