// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// FormGPSDataView — Avalonia code-behind for the "System Data" live-telemetry read-out window.
//
// 1:1 behavioural parity with the deleted WinForms FormGPSData (Forms/FormGPSData.cs +
// Forms/FormGPSData.Designer.cs), per AAP §0.3.3 — a faithful re-platform, never a redesign. The
// original was a passive monitor: a 1000 ms System.Windows.Forms.Timer (timer1, Enabled = true)
// whose Tick handler (timer1_Tick) refreshed fifteen value Labels once per second by reading live
// fields off the FormGPS "mf" god-object — mf.frameTime / mf.timeSliceOfLastFix / mf.gpsHz, the
// mf.pn.fix easting/northing, mf.ahrs.imuYawRate, and the mf.Latitude / mf.Longitude / mf.SatsTracked
// / mf.HDOP / mf.GyroInDegrees / mf.GPSHeading / mf.Altitude / mf.AltitudeFeet computed-string
// properties. The form had no buttons and no user input; its only other behaviour was fixing its
// size (120×330) on Load and clearing mf.isGPSSentencesOn on FormClosing.
//
// Decoupling ([XPLAT], AAP §0.3.2 / §0.6.1): the FormGPS "mf" back-reference is removed. The same
// live state is constructor-injected as the concrete objects the FormGPS scan-loop was decomposed
// into — CNMEA (position), CAHRS (IMU), PositionService (frame/Hz/heading stats), PgnDispatcher
// (missed-sentence watchdog) and ApplicationCore (lat/lon, fused heading, metric flag, and the
// relocated isGPSSentencesOn flag). No new interface/abstraction is introduced (AAP limits new
// surfaces to IPlatformServices + the GL host); the concrete objects are injected directly.
//
// Cross-platform culture ([XPLAT], AAP §0.6.5 — CRITICAL): every numeric ToString in the read-out
// passes CultureInfo.InvariantCulture, so a comma-decimal OS locale on Linux/macOS can never alter
// the displayed text. The original FormGPS string properties (Convert.ToString / Math.Round(...)
// + "°") implicitly used the current culture; they are reproduced here with InvariantCulture, which
// yields the identical digits while removing the locale dependency.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Services;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// Live GPS / IMU / loop-timing diagnostics window ("System Data"). Reimplements the WinForms
    /// <c>FormGPSData</c> on Avalonia with an identical sampling cadence (1 s), identical read-out
    /// expressions and identical close/size behaviour. The view owns no domain logic: a
    /// <see cref="DispatcherTimer"/> reads the injected live state once per second and writes the
    /// fifteen value <see cref="TextBlock"/>s declared in <c>FormGPSDataView.axaml</c>.
    /// </summary>
    public partial class FormGPSDataView : Window
    {
        // [XPLAT] Injected live state — the concrete objects the FormGPS scan-loop decomposed into,
        // replacing the former "mf" (FormGPS) field reads. Each is named after the original FormGPS
        // member it stands in for so the ported read-out expressions remain a faithful copy of
        // timer1_Tick and the GUI.Designer.cs string properties.
        private readonly CNMEA _pn;                 // was mf.pn   — fix easting/northing, sats, HDOP, altitude
        private readonly CAHRS _ahrs;               // was mf.ahrs — imuHeading / imuYawRate
        private readonly PositionService _position; // was mf      — frameTime / timeSliceOfLastFix / gpsHz / gpsHeading
        private readonly PgnDispatcher _pgn;        // was mf      — missedSentenceCount
        private readonly ApplicationCore _appCore;  // was mf      — CurrentLatLon / FixHeading / IsMetric / isGPSSentencesOn

        // [XPLAT] WinForms System.Windows.Forms.Timer (timer1: Interval = 1000, Enabled = true) ->
        // Avalonia DispatcherTimer (UI-thread tick). Created only by the injecting constructor; the
        // design-time constructor leaves it null because there is no live pipeline to sample.
        private readonly DispatcherTimer _timer;

        /// <summary>
        /// Parameterless constructor for the Avalonia design-time previewer / XAML loader. It loads the
        /// markup only and starts no timer, so the window can be instantiated without a live GPS
        /// pipeline. The application host uses the injecting constructor below. No null-object
        /// abstraction is introduced (AAP constraint on new architectural surfaces).
        /// </summary>
        public FormGPSDataView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the read-out bound to the live GPS state. Replaces the WinForms
        /// <c>FormGPSData(Form callingForm)</c> constructor, which captured the <c>FormGPS</c>
        /// reference; the same live values are now supplied as concrete injected collaborators.
        /// </summary>
        /// <param name="pn">Position object (was <c>mf.pn</c>): fix easting/northing, satellites, HDOP, altitude.</param>
        /// <param name="ahrs">AHRS/IMU object (was <c>mf.ahrs</c>): IMU heading and yaw rate.</param>
        /// <param name="position">Scan-loop service (was <c>mf</c>): frame time, raw-Hz time slice, GPS Hz, GPS heading.</param>
        /// <param name="pgn">PGN transport (was <c>mf</c>): missed-sentence watchdog counter.</param>
        /// <param name="appCore">Application core (was <c>mf</c>): current lat/lon, fused heading, metric flag, isGPSSentencesOn.</param>
        public FormGPSDataView(
            CNMEA pn,
            CAHRS ahrs,
            PositionService position,
            PgnDispatcher pgn,
            ApplicationCore appCore)
            : this()
        {
            _pn = pn ?? throw new ArgumentNullException(nameof(pn));
            _ahrs = ahrs ?? throw new ArgumentNullException(nameof(ahrs));
            _position = position ?? throw new ArgumentNullException(nameof(position));
            _pgn = pgn ?? throw new ArgumentNullException(nameof(pgn));
            _appCore = appCore ?? throw new ArgumentNullException(nameof(appCore));

            // [XPLAT] timer1: Interval = 1000 ms, Enabled = true — started on open, stopped on close.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += OnTimerTick;
        }

        // [XPLAT] WinForms FormGPSData_Load -> Avalonia Window.OnOpened: fix the window to the original
        // ClientSize and start the once-per-second read-out timer.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Parity with FormGPSData_Load: this.Width = 120; this.Height = 330;
            Width = 120;
            Height = 330;

            _timer?.Start();
        }

        // [XPLAT] WinForms FormGPSData_FormClosing -> Avalonia Window.OnClosed: clear the
        // GPS-sentences flag on the shared application state, then stop and detach the timer.
        protected override void OnClosed(EventArgs e)
        {
            // Parity with FormGPSData_FormClosing: mf.isGPSSentencesOn = false;
            if (_appCore != null)
            {
                _appCore.AppModel.isGPSSentencesOn = false;
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
            }

            base.OnClosed(e);
        }

        // [XPLAT] WinForms timer1_Tick — refresh every value read-out from the injected live state.
        // Every expression and format string is reproduced verbatim from the original handler; the only
        // change is that each numeric ToString passes CultureInfo.InvariantCulture (AAP §0.6.5).
        private void OnTimerTick(object sender, EventArgs e)
        {
            // The timer is created and started only by the injecting constructor, so reaching this point
            // implies the live collaborators are present. The guard documents that invariant and keeps
            // the design-time path (parameterless constructor, null state) defensively safe.
            if (_pn == null)
            {
                return;
            }

            // Loop timing.
            lblFrameTime.Text = _position.frameTime.ToString("N1", CultureInfo.InvariantCulture);
            lblTimeSlice.Text = (1 / _position.timeSliceOfLastFix).ToString("N3", CultureInfo.InvariantCulture);
            lblHz.Text = _position.gpsHz.ToString("N1", CultureInfo.InvariantCulture);

            // Local-plane fix (easting / northing).
            lblEastingField.Text = Math.Round(_pn.fix.easting, 1).ToString(CultureInfo.InvariantCulture);
            lblNorthingField.Text = Math.Round(_pn.fix.northing, 1).ToString(CultureInfo.InvariantCulture);

            // Geographic position and satellite quality.
            lblLatitude.Text = Latitude;
            lblLongitude.Text = Longitude;
            lblSatsTracked.Text = SatsTracked;
            lblHDOP.Text = HDOP;

            // Headings: IMU, GPS fix-to-fix, and the fused heading (radians -> degrees).
            lblIMUHeading.Text = GyroInDegrees;

            // [XPLAT] GPSHeading parity (was FormGPS.GPSHeading => new GeoDir(gpsHeading).HeadingString()).
            // GeoDir.HeadingString() formats AngleInDegrees as "N1" + degree-sign but WITHOUT a culture, so
            // a comma-decimal OS locale on Linux/macOS would localise the digits (e.g. "90,0°") and diverge
            // from the Windows baseline. To honour the InvariantCulture rule (AAP §0.6.5) while keeping the
            // displayed digits byte-identical to the original, the SAME AngleInDegrees -> "N1" + "\u00B0"
            // computation HeadingString() performs is reproduced here with CultureInfo.InvariantCulture.
            // GeoDir is a behaviour-frozen Core type (out of this file's scope), so it is NOT modified;
            // "N1" is HeadingString()'s exact default format and "\u00B0" its exact degree suffix.
            lblFix2FixHeading.Text =
                new GeoDir(_position.gpsHeading).AngleInDegrees.ToString("N1", CultureInfo.InvariantCulture) + "\u00B0";
            lblFuzeHeading.Text =
                (_appCore.AppModel.FixHeading.AngleInRadians * 57.2957795).ToString("N1", CultureInfo.InvariantCulture);

            lblAngularVelocity.Text = _ahrs.imuYawRate.ToString("N2", CultureInfo.InvariantCulture);

            // Dropped-sentence watchdog counter.
            lbludpWatchCounts.Text = _pgn.missedSentenceCount.ToString(CultureInfo.InvariantCulture);

            // Altitude in the operator's current unit system.
            lblAltitude.Text = _appCore.AppViewModel.IsMetric ? Altitude : AltitudeFeet;
        }

        // -------------------------------------------------------------------------------------------
        // [XPLAT] Computed read-out strings reproduced from the deleted FormGPS GUI.Designer.cs
        // properties (originally Convert.ToString(...) / Math.Round(...) + "°"). The FormGPS host that
        // owned them is gone, so they live here as private helpers, with InvariantCulture replacing the
        // implicit current culture so the produced digits are identical but locale-independent.
        // -------------------------------------------------------------------------------------------

        // was FormGPS.Latitude: Convert.ToString(Math.Round(AppModel.CurrentLatLon.Latitude, 7))
        private string Latitude =>
            Math.Round(_appCore.AppModel.CurrentLatLon.Latitude, 7).ToString(CultureInfo.InvariantCulture);

        // was FormGPS.Longitude: Convert.ToString(Math.Round(AppModel.CurrentLatLon.Longitude, 7))
        private string Longitude =>
            Math.Round(_appCore.AppModel.CurrentLatLon.Longitude, 7).ToString(CultureInfo.InvariantCulture);

        // was FormGPS.SatsTracked: Convert.ToString(pn.satellitesTracked)
        private string SatsTracked => _pn.satellitesTracked.ToString(CultureInfo.InvariantCulture);

        // was FormGPS.HDOP: Convert.ToString(pn.hdop)
        private string HDOP => _pn.hdop.ToString(CultureInfo.InvariantCulture);

        // was FormGPS.GyroInDegrees: (ahrs.imuHeading != 99999) ? Math.Round(ahrs.imuHeading, 1) + "°" : "-"
        // 99999 is the IMU "no heading" sentinel; "\u00B0" is the degree sign, preserved exactly.
        private string GyroInDegrees =>
            _ahrs.imuHeading != 99999
                ? Math.Round(_ahrs.imuHeading, 1).ToString(CultureInfo.InvariantCulture) + "\u00B0"
                : "-";

        // was FormGPS.Altitude: Convert.ToString(Math.Round(pn.altitude, 2))
        private string Altitude => Math.Round(_pn.altitude, 2).ToString(CultureInfo.InvariantCulture);

        // was FormGPS.AltitudeFeet: Convert.ToString(Math.Round(pn.altitude * 3.28084, 1))
        private string AltitudeFeet =>
            Math.Round(_pn.altitude * 3.28084, 1).ToString(CultureInfo.InvariantCulture);
    }
}
