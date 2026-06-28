// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Enter Coordinates For Simulator" dialog
// (Views/Settings/FormSimCoordsView.axaml). This is a 1:1 behavioural-parity port of the WinForms
// Forms/Settings/FormSimCoords.cs (+ FormSimCoords.Designer.cs): a fixed-size modal that lets the
// operator set the simulator start latitude/longitude on a world-map backdrop and then commit them
// by (re)defining the local-plane origin.
//
// [XPLAT] DECOUPLING (AAP §0.3.2 Dependency Inversion): the WinForms form reached through the
// FormGPS "mf" god-object — mf.isJobStarted, mf.timerSim.Enabled, mf.TimedMessageBox(...) and
// mf.pn.DefineLocalPlane(...). Those collaborators are constructor-injected here instead, so this
// view holds NO FormGPS reference and compiles/runs unchanged on Windows, Linux and macOS.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Behaviour half (code-behind) of the simulator-coordinates dialog. Faithful port of the
    /// WinForms <c>FormSimCoords</c>: it shows the persisted simulator latitude/longitude, lets the
    /// operator edit each value through the on-screen <see cref="FormNumeric"/> keypad, and on OK
    /// defines the local-plane origin from the entered <see cref="Wgs84"/> coordinate (subject to the
    /// two job/simulator guards), then closes. Cancel closes without committing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative dialog (no view-model / data binding): the paired markup declares the controls by
    /// <c>x:Name</c> and this code-behind addresses them directly — the same convention used across the
    /// migrated GPS dialogs. The former <c>FormGPS</c> (<c>mf</c>) back-reference is replaced by the
    /// collaborators supplied to the dependency-injection constructor.
    /// </para>
    /// <para>
    /// <b>[XPLAT] Culture (AAP §0.6.5 — highest data-integrity rule).</b> Every latitude/longitude
    /// read, format and write is pinned to <see cref="CultureInfo.InvariantCulture"/> so a locale whose
    /// decimal separator is a comma can never corrupt the displayed or persisted coordinates.
    /// </para>
    /// </remarks>
    public partial class FormSimCoordsView : Window
    {
        // [XPLAT] nudLatitude / nudLongitude ranges — the exact WinForms designer Minimum/Maximum
        // (FormSimCoords.Designer.cs: latitude -90..90, longitude -180..180). Used both to clamp the
        // seeded value (reproducing NumericUpDown.Value clamping) and to bound the keypad.
        private const double LatitudeMinimum = -90.0;
        private const double LatitudeMaximum = 90.0;
        private const double LongitudeMinimum = -180.0;
        private const double LongitudeMaximum = 180.0;

        // [XPLAT] WinForms NudlessNumericUpDown.DecimalPlaces = 7. The displays are formatted with this
        // fixed-decimal format and InvariantCulture (see the class remarks).
        private const string CoordinateFormat = "F7";

        // [XPLAT] Injected collaborators replacing the FormGPS (mf) god-object back-reference:
        //   _pn                - owns DefineLocalPlane(Wgs84, bool) (was mf.pn).
        //   _isJobStarted      - whether a field is currently open (was mf.isJobStarted).
        //   _isSimulatorActive - whether the simulator is running (was mf.timerSim.Enabled).
        //   _errorPresenter    - the wired Core timed-message path (was mf.TimedMessageBox(...));
        //                        optional, with a direct FormTimedMessageView fallback.
        private readonly CNMEA _pn;
        private readonly bool _isJobStarted;
        private readonly bool _isSimulatorActive;
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] Backing values for the read-only numeric displays (were nudLatitude.Value /
        // nudLongitude.Value). Held as double; the displays render them formatted to 7 decimals.
        private double _latitude;
        private double _longitude;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / design-time
        /// previewer. It loads the markup only; the live dialog is created through the DI constructor.
        /// </summary>
        public FormSimCoordsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Live (dependency-injected) constructor. Reproduces the WinForms
        /// <c>FormSimCoords(Form callingForm)</c> ctor together with <c>FormSimCoords_Load</c>: it
        /// localises the title, wires the numeric-display clicks to the keypad, and seeds the displays
        /// from the persisted simulator coordinates.
        /// </summary>
        /// <param name="pn">
        /// Position object owning <see cref="CNMEA.DefineLocalPlane(Wgs84, bool)"/> (was <c>mf.pn</c>).
        /// </param>
        /// <param name="isJobStarted">Whether a field is currently open (was <c>mf.isJobStarted</c>).</param>
        /// <param name="isSimulatorActive">
        /// Whether the simulator is running (was <c>mf.timerSim.Enabled</c>).
        /// </param>
        /// <param name="errorPresenter">
        /// Optional Core timed-message presenter — the wired replacement for <c>mf.TimedMessageBox</c>.
        /// When <see langword="null"/> the dialog falls back to showing a
        /// <see cref="FormTimedMessageView"/> directly.
        /// </param>
        public FormSimCoordsView(
            CNMEA pn,
            bool isJobStarted,
            bool isSimulatorActive,
            IErrorPresenter errorPresenter = null)
            : this()
        {
            _pn = pn;
            _isJobStarted = isJobStarted;
            _isSimulatorActive = isSimulatorActive;
            _errorPresenter = errorPresenter;

            // [XPLAT] WinForms: this.Text = gStr.gsEnterCoordinatesForSimulator;
            Title = gStr.gsEnterCoordinatesForSimulator;

            // [XPLAT] WinForms wired both NudlessNumericUpDown.Click to nud_Click (-> ShowKeypad). The
            // markup declares no Click for the read-only displays, so wire them here to open FormNumeric.
            nudLatitude.Click += NudLatitude_Click;
            nudLongitude.Click += NudLongitude_Click;

            // [XPLAT] FormSimCoords_Load:
            //   nudLatitude.Value  = (decimal)Properties.Settings.Default.setGPS_SimLatitude;
            //   nudLongitude.Value = (decimal)Properties.Settings.Default.setGPS_SimLongitude;
            // The WinForms NumericUpDown.Value setter clamps to [Minimum, Maximum]; reproduced via Clamp
            // so a corrupt out-of-range persisted value is displayed (and later used) identically.
            _latitude = Math.Clamp(AgOpenGPS.Properties.Settings.Default.setGPS_SimLatitude, LatitudeMinimum, LatitudeMaximum);
            _longitude = Math.Clamp(AgOpenGPS.Properties.Settings.Default.setGPS_SimLongitude, LongitudeMinimum, LongitudeMaximum);
            UpdateLatitudeDisplay();
            UpdateLongitudeDisplay();
        }

        /// <summary>
        /// [XPLAT] <c>nud_Click</c> for the latitude display — opens the numeric keypad bounded to the
        /// latitude range. <see cref="FormNumeric"/> is the cross-platform replacement for the WinForms
        /// <c>NudlessNumericUpDown.ShowKeypad</c>; it is shown modally and returns <see langword="true"/>
        /// on accept, exposing the in-range value via <see cref="FormNumeric.ReturnValue"/>.
        /// <c>async void</c> is the established handler signature for the migrated dialogs (it awaits
        /// <c>ShowDialog</c>, so there is no CS1998).
        /// </summary>
        /// <param name="sender">The latitude display button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void NudLatitude_Click(object sender, RoutedEventArgs e)
        {
            var keypad = new FormNumeric(LatitudeMinimum, LatitudeMaximum, _latitude);
            if (await keypad.ShowDialog<bool>(this))
            {
                _latitude = keypad.ReturnValue;
                UpdateLatitudeDisplay();
            }
        }

        /// <summary>
        /// [XPLAT] <c>nud_Click</c> for the longitude display — opens the numeric keypad bounded to the
        /// longitude range. See <see cref="NudLatitude_Click"/> for the keypad contract.
        /// </summary>
        /// <param name="sender">The longitude display button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void NudLongitude_Click(object sender, RoutedEventArgs e)
        {
            var keypad = new FormNumeric(LongitudeMinimum, LongitudeMaximum, _longitude);
            if (await keypad.ShowDialog<bool>(this))
            {
                _longitude = keypad.ReturnValue;
                UpdateLongitudeDisplay();
            }
        }

        /// <summary>
        /// [XPLAT] OK handler — the WinForms <c>bntOK_Click</c>.
        /// <para>
        /// <b>Parity-critical control flow (reproduced verbatim).</b> The two guard branches call
        /// <see cref="Window.Close()"/> but DO NOT <c>return</c>, so control falls through and
        /// <see cref="CNMEA.DefineLocalPlane(Wgs84, bool)"/> ALWAYS runs and the dialog ALWAYS closes.
        /// No early returns are added — calling <c>Close()</c> on an Avalonia <see cref="Window"/>, like
        /// the WinForms <c>Form.Close()</c>, does not abort the method.
        /// </para>
        /// </summary>
        /// <param name="sender">The OK button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                // [XPLAT] mf.TimedMessageBox(2000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst);
                ShowTimedMessage(2000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst);
                Close();
            }

            if (!_isSimulatorActive)
            {
                // [XPLAT] mf.TimedMessageBox(3000, "Simulator is off", "Simulator can't work ...");
                // These two strings are literals in the WinForms source (not gStr); carried verbatim.
                ShowTimedMessage(3000, "Simulator is off", "Simulator can't work while using real Antenna");
                Close();
            }

            // [XPLAT] mf.pn.DefineLocalPlane(new Wgs84((double)nudLatitude.Value, (double)nudLongitude.Value), true);
            // Null-conditional: _pn is always supplied by the DI constructor; the guard only protects the
            // design-time previewer (parameterless ctor), which never reaches this handler.
            _pn?.DefineLocalPlane(new Wgs84(_latitude, _longitude), true);
            Close();
        }

        /// <summary>
        /// [XPLAT] Cancel handler — the WinForms <c>btnCancel_Click</c>: close without committing
        /// (WinForms <c>DialogResult.Cancel</c>).
        /// </summary>
        /// <param name="sender">The Cancel button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Replacement for the WinForms <c>mf.TimedMessageBox(msec, title, message)</c> helper.
        /// Prefers the injected Core <see cref="IErrorPresenter"/> (the wired path, which marshals to the
        /// UI thread and owns the toast to the main window); otherwise it shows the migrated
        /// <see cref="FormTimedMessageView"/> directly. The popup is shown UNOWNED so it survives this
        /// dialog's <see cref="Window.Close()"/> and lives out its own auto-close timer — matching the
        /// WinForms toast, which was owned by the persistent main form, not by this transient dialog.
        /// </summary>
        /// <param name="milliseconds">How long the popup stays visible before it auto-closes.</param>
        /// <param name="titleString">The bold title shown at the top of the popup.</param>
        /// <param name="messageString">The message body shown beneath the title.</param>
        private void ShowTimedMessage(int milliseconds, string titleString, string messageString)
        {
            if (_errorPresenter != null)
            {
                _errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(milliseconds), titleString, messageString);
            }
            else
            {
                new FormTimedMessageView(milliseconds, titleString, messageString).Show();
            }
        }

        // [XPLAT] Render the latitude display (was nudLatitude.Value with DecimalPlaces=7).
        private void UpdateLatitudeDisplay()
        {
            nudLatitude.Content = _latitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture);
        }

        // [XPLAT] Render the longitude display (was nudLongitude.Value with DecimalPlaces=7).
        private void UpdateLongitudeDisplay()
        {
            nudLongitude.Content = _longitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture);
        }
    }
}
