// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormSimCoords.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Enter Coordinates For Simulator" dialog.
//
// Parity notes vs the WinForms original (FormSimCoords):
//   * The two declared handlers reproduce the WinForms DialogResult.OK / DialogResult.Cancel
//     buttons (the WinForms control was misspelled "bntOK"; renamed btnOK here). Cancel closes the
//     dialog; OK commits the entered latitude/longitude and closes.
//   * The WinForms OK handler read the FormGPS "mf" god-object directly: it checked isJobStarted and
//     timerSim, showed a TimedMessageBox, and called mf.pn.DefineLocalPlane(new Wgs84(lat, lon)).
//     That logic is part of the guidance/simulator pipeline being decoupled into injectable services,
//     so this view does not reach into FormGPS.  Instead it exposes the entered coordinates and
//     raises CoordinatesAccepted; the application host performs the job/sim validation and defines
//     the local plane.  This is the dependency-inversion seam mandated by AAP §0.3.2 ("host supplies
//     data"), used consistently across the migrated GPS dialogs.
//   * nudLatitude / nudLongitude reproduce the WinForms NudlessNumericUpDown: read-only numeric
//     displays whose click pops a numeric keypad.  The markup declares no Click for them (only
//     x:Name), so the clicks are wired here and raise CoordinateEditRequested; the host opens its
//     keypad (FormNumeric) and calls SetCoordinates with the result.  Values are formatted with
//     InvariantCulture so the display is locale-independent across Windows / Linux / macOS.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormSimCoordsView : Window
    {
        /// <summary>Identifies which coordinate field requested an edit (drives the host keypad).</summary>
        public enum SimCoordinateField
        {
            Latitude,
            Longitude
        }

        /// <summary>Event payload for a coordinate edit request raised from a numeric display click.</summary>
        public sealed class SimCoordinateEditEventArgs : EventArgs
        {
            public SimCoordinateEditEventArgs(SimCoordinateField field, double currentValue)
            {
                Field = field;
                CurrentValue = currentValue;
            }

            public SimCoordinateField Field { get; }
            public double CurrentValue { get; }
        }

        /// <summary>Event payload carrying the accepted simulator coordinates.</summary>
        public sealed class SimCoordinatesEventArgs : EventArgs
        {
            public SimCoordinatesEventArgs(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }

            public double Latitude { get; }
            public double Longitude { get; }
        }

        public FormSimCoordsView()
        {
            InitializeComponent();

            // The numeric displays declare no Click in markup; wire them here to request a keypad
            // edit from the host (parity with NudlessNumericUpDown.ShowKeypad).
            nudLatitude.Click += NudLatitude_Click;
            nudLongitude.Click += NudLongitude_Click;

            UpdateDisplays();
        }

        /// <summary>Currently entered latitude (degrees, +90..-90).</summary>
        public double Latitude { get; private set; }

        /// <summary>Currently entered longitude (degrees, +180..-180).</summary>
        public double Longitude { get; private set; }

        /// <summary>
        /// Host entry point: seed / update the displayed coordinates (e.g. from Settings
        /// setGPS_SimLatitude / setGPS_SimLongitude, or from a keypad result).  Values are clamped to
        /// the WinForms control ranges before display.
        /// </summary>
        public void SetCoordinates(double latitude, double longitude)
        {
            Latitude = Math.Max(-90.0, Math.Min(90.0, latitude));
            Longitude = Math.Max(-180.0, Math.Min(180.0, longitude));
            UpdateDisplays();
        }

        /// <summary>Raised when a numeric display is clicked; the host opens its numeric keypad.</summary>
        public event EventHandler<SimCoordinateEditEventArgs> CoordinateEditRequested;

        /// <summary>Raised when OK is pressed; the host validates job/sim state and defines the local plane.</summary>
        public event EventHandler<SimCoordinatesEventArgs> CoordinatesAccepted;

        private void UpdateDisplays()
        {
            nudLatitude.Content = Latitude.ToString("0.0000000", CultureInfo.InvariantCulture);
            nudLongitude.Content = Longitude.ToString("0.0000000", CultureInfo.InvariantCulture);
        }

        private void NudLatitude_Click(object sender, RoutedEventArgs e)
        {
            CoordinateEditRequested?.Invoke(this, new SimCoordinateEditEventArgs(SimCoordinateField.Latitude, Latitude));
        }

        private void NudLongitude_Click(object sender, RoutedEventArgs e)
        {
            CoordinateEditRequested?.Invoke(this, new SimCoordinateEditEventArgs(SimCoordinateField.Longitude, Longitude));
        }

        // OK: hand the entered coordinates to the host (which validates job/sim state and defines the
        // local plane, as the WinForms mf.pn.DefineLocalPlane call did) and close. The WinForms form
        // always closed on OK regardless of the validation branch, so closing here preserves that.
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            CoordinatesAccepted?.Invoke(this, new SimCoordinatesEventArgs(Latitude, Longitude));
            Close(true);
        }

        // Cancel: close without committing (WinForms DialogResult.Cancel).
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
