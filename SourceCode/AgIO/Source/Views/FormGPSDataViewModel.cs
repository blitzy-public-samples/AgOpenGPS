// [XPLAT] migrated from net48/WinForms FormGPSData.cs + FormGPSData.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using AgOpenGPS.Core.ViewModels;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing the AgIO live <b>GPS Data</b> dialog (<c>FormGPSData</c>): a read-only,
    /// timer-driven mirror of the current parsed NMEA/GPS/IMU state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] Reimplemented from the WinForms <c>FormGPSData</c> — a <c>Form</c> that held a
    /// <c>FormLoop mf</c> back-reference and refreshed its labels/textboxes from <c>mf.*</c> on a
    /// 250&#160;ms UI <c>Timer</c> tick, blanked the raw-sentence textboxes on load, and set
    /// <c>mf.isGPSSentencesOn = false</c> when it closed. The god-object back-reference is removed:
    /// this view-model instead takes an injected <see cref="CommCoordinatorService"/> and reads the
    /// live values from its behaviour-frozen <see cref="NmeaService"/> (<c>comm.Nmea</c>), which is the
    /// single source of truth for parsed GPS/IMU state.
    /// </para>
    /// <para>
    /// <b>Culture safety (AAP §0.6.5).</b> Every numeric value is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/>, so a locale whose decimal separator is a comma can
    /// never corrupt the displayed latitude/longitude/speed/heading/etc. This deliberately hardens the
    /// WinForms original, which formatted with the ambient culture.
    /// </para>
    /// <para>
    /// <b>Real-time safety (AAP §0.6.1).</b> The refresh timer only <i>reads</i> values that the comm
    /// path has already parsed and formats them for display; it performs no socket receive and no
    /// sentence parsing (that work stays frozen in <see cref="NmeaService"/>, driven off the receive
    /// callbacks). It therefore adds no latency to the receive&#8594;parse pipeline.
    /// </para>
    /// <para>
    /// The dialog has no buttons or commands (the WinForms original carried none); the hosting view
    /// closes itself and calls <see cref="Stop"/> on close.
    /// </para>
    /// </remarks>
    public class FormGPSDataViewModel : ViewModel
    {
        /// <summary>
        /// [XPLAT] Display-refresh cadence in milliseconds, reproduced verbatim from the WinForms
        /// <c>FormGPSData.Designer.cs</c> (<c>timer1.Interval = 250</c>).
        /// </summary>
        private const int RefreshIntervalMilliseconds = 250;

        // [XPLAT] Injected comms aggregate (AAP §0.3.2 "Facade"). The live parsed GPS/IMU state is read
        // from its NmeaService (comm.Nmea); this replaces the former WinForms FormLoop "mf" back-reference.
        private readonly CommCoordinatorService _comm;

        // [XPLAT] UI-thread display-refresh timer (Avalonia DispatcherTimer replaces the WinForms
        // System.Windows.Forms.Timer). It reads only; see the class remarks on real-time safety.
        private readonly DispatcherTimer _timer;

        // Backing fields for the bound display strings. Initialised to empty (never null) so the view
        // never renders "null", mirroring the WinForms load that blanked the raw-sentence textboxes.
        private string _latText = string.Empty;
        private string _lonText = string.Empty;
        private string _fixQuality = string.Empty;
        private string _satellites = string.Empty;
        private string _hdop = string.Empty;
        private string _speed = string.Empty;
        private string _roll = string.Empty;
        private string _imuRoll = string.Empty;
        private string _imuPitch = string.Empty;
        private string _imuYawRate = string.Empty;
        private string _imuHeading = string.Empty;
        private string _age = string.Empty;
        private string _headingTrue = string.Empty;
        private string _headingTrueDual = string.Empty;
        private string _altitude = string.Empty;
        private string _vtgText = string.Empty;
        private string _ggaText = string.Empty;
        private string _paogiText = string.Empty;
        private string _avrText = string.Empty;
        private string _hdtText = string.Empty;
        private string _hpdText = string.Empty;
        private string _pandaText = string.Empty;
        private string _ksxtText = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormGPSDataViewModel"/> class, enables raw-sentence
        /// capture in the NMEA parser, and starts the display-refresh timer.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate that owns the live <see cref="NmeaService"/> read on each refresh.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="comm"/> is null.</exception>
        public FormGPSDataViewModel(CommCoordinatorService comm)
        {
            // Fail fast on an incomplete composition rather than throwing an opaque NullReferenceException
            // on first refresh (mirrors NmeaService's own required-peer guard). Nullable refs are disabled
            // project-wide, so this explicit guard is the contract.
            _comm = comm ?? throw new ArgumentNullException(nameof(comm));

            // [XPLAT] Parity: opening the dialog turns on raw-sentence capture in the NMEA parser. The
            // WinForms FormGPSData set mf.isGPSSentencesOn = false on close, implying it was turned on while
            // the dialog was open; the flag now lives on NmeaService and Stop() turns it back off.
            _comm.Nmea.isGPSSentencesOn = true;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMilliseconds) };
            _timer.Tick += (sender, e) => Refresh();
            _timer.Start();
        }

        /// <summary>Gets the latitude in decimal degrees, formatted <c>"N7"</c> (WinForms <c>lblLatitude</c>).</summary>
        public string LatText
        {
            get { return _latText; }
            private set { SetProperty(ref _latText, value); }
        }

        /// <summary>Gets the longitude in decimal degrees, formatted <c>"N7"</c> (WinForms <c>lblLongitude</c>).</summary>
        public string LonText
        {
            get { return _lonText; }
            private set { SetProperty(ref _lonText, value); }
        }

        /// <summary>Gets the fix-quality text (e.g. <c>"RTK fix: "</c>) from the parser (WinForms <c>lblFixQuality</c>).</summary>
        public string FixQuality
        {
            get { return _fixQuality; }
            private set { SetProperty(ref _fixQuality, value); }
        }

        /// <summary>Gets the number of satellites tracked (WinForms <c>lblSatsTracked</c>).</summary>
        public string Satellites
        {
            get { return _satellites; }
            private set { SetProperty(ref _satellites, value); }
        }

        /// <summary>Gets the horizontal dilution of precision (WinForms <c>lblHDOP</c>).</summary>
        public string Hdop
        {
            get { return _hdop; }
            private set { SetProperty(ref _hdop, value); }
        }

        /// <summary>Gets the ground speed, formatted <c>"N1"</c> (WinForms <c>lblSpeed</c>).</summary>
        public string Speed
        {
            get { return _speed; }
            private set { SetProperty(ref _speed, value); }
        }

        /// <summary>Gets the roll angle, formatted <c>"N2"</c> (WinForms <c>lblRoll</c>).</summary>
        public string Roll
        {
            get { return _roll; }
            private set { SetProperty(ref _roll, value); }
        }

        /// <summary>Gets the IMU roll reading (WinForms <c>lblIMURoll</c>).</summary>
        public string ImuRoll
        {
            get { return _imuRoll; }
            private set { SetProperty(ref _imuRoll, value); }
        }

        /// <summary>Gets the IMU pitch reading (WinForms <c>lblIMUPitch</c>).</summary>
        public string ImuPitch
        {
            get { return _imuPitch; }
            private set { SetProperty(ref _imuPitch, value); }
        }

        /// <summary>Gets the IMU yaw-rate reading (WinForms <c>lblIMUYawRate</c>).</summary>
        public string ImuYawRate
        {
            get { return _imuYawRate; }
            private set { SetProperty(ref _imuYawRate, value); }
        }

        /// <summary>Gets the IMU heading reading (WinForms <c>lblIMUHeading</c>).</summary>
        public string ImuHeading
        {
            get { return _imuHeading; }
            private set { SetProperty(ref _imuHeading, value); }
        }

        /// <summary>Gets the differential-correction age, formatted <c>"N1"</c> (WinForms <c>lblAge</c>).</summary>
        public string Age
        {
            get { return _age; }
            private set { SetProperty(ref _age, value); }
        }

        /// <summary>Gets the true GPS heading, formatted <c>"N2"</c> (WinForms <c>lblGPSHeading</c>).</summary>
        public string HeadingTrue
        {
            get { return _headingTrue; }
            private set { SetProperty(ref _headingTrue, value); }
        }

        /// <summary>Gets the dual-antenna true heading, formatted <c>"N2"</c> (WinForms <c>lblDualHeading</c>).</summary>
        public string HeadingTrueDual
        {
            get { return _headingTrueDual; }
            private set { SetProperty(ref _headingTrueDual, value); }
        }

        /// <summary>Gets the altitude/elevation, formatted <c>"N1"</c> (WinForms <c>lblAltitude</c>).</summary>
        public string Altitude
        {
            get { return _altitude; }
            private set { SetProperty(ref _altitude, value); }
        }

        /// <summary>Gets the raw <c>$..VTG</c> sentence text (WinForms <c>tboxVTG</c>).</summary>
        public string VtgText
        {
            get { return _vtgText; }
            private set { SetProperty(ref _vtgText, value); }
        }

        /// <summary>Gets the raw <c>$..GGA</c> sentence text (WinForms <c>tboxGGA</c>).</summary>
        public string GgaText
        {
            get { return _ggaText; }
            private set { SetProperty(ref _ggaText, value); }
        }

        /// <summary>Gets the raw <c>$PAOGI</c> sentence text (WinForms <c>tboxPAOGI</c>).</summary>
        public string PaogiText
        {
            get { return _paogiText; }
            private set { SetProperty(ref _paogiText, value); }
        }

        /// <summary>Gets the raw <c>$..AVR</c> sentence text (WinForms <c>tboxAVR</c>).</summary>
        public string AvrText
        {
            get { return _avrText; }
            private set { SetProperty(ref _avrText, value); }
        }

        /// <summary>Gets the raw <c>$..HDT</c> sentence text (WinForms <c>tboxHDT</c>).</summary>
        public string HdtText
        {
            get { return _hdtText; }
            private set { SetProperty(ref _hdtText, value); }
        }

        /// <summary>Gets the raw <c>$GPHPD</c> sentence text (WinForms <c>tboxHPD</c>).</summary>
        public string HpdText
        {
            get { return _hpdText; }
            private set { SetProperty(ref _hpdText, value); }
        }

        /// <summary>Gets the raw <c>$PANDA</c> sentence text (WinForms <c>tboxPANDA</c>).</summary>
        public string PandaText
        {
            get { return _pandaText; }
            private set { SetProperty(ref _pandaText, value); }
        }

        /// <summary>Gets the raw <c>$KSXT</c> sentence text (WinForms <c>tboxKSXT</c>).</summary>
        public string KsxtText
        {
            get { return _ksxtText; }
            private set { SetProperty(ref _ksxtText, value); }
        }

        /// <summary>
        /// [XPLAT] Copies the current parsed values from <see cref="CommCoordinatorService.Nmea"/> into the
        /// bound display properties, formatting every numeric value with
        /// <see cref="CultureInfo.InvariantCulture"/>. Invoked on each <see cref="DispatcherTimer"/> tick;
        /// it only reads already-parsed state and never touches the comm/parse path. The number-format
        /// specifiers (<c>N7</c>/<c>N1</c>/<c>N2</c> and the plain integer conversions) are reproduced
        /// exactly from the WinForms <c>timer1_Tick</c> handler.
        /// </summary>
        private void Refresh()
        {
            NmeaService nmea = _comm.Nmea;

            LatText = nmea.latitude.ToString("N7", CultureInfo.InvariantCulture);
            LonText = nmea.longitude.ToString("N7", CultureInfo.InvariantCulture);

            // FixQuality is a parser-supplied string literal (never null); the others are numeric.
            FixQuality = nmea.FixQuality ?? string.Empty;
            Satellites = nmea.satellitesData.ToString(CultureInfo.InvariantCulture);
            Hdop = nmea.hdopData.ToString(CultureInfo.InvariantCulture);
            Speed = nmea.speedData.ToString("N1", CultureInfo.InvariantCulture);

            Roll = nmea.rollData.ToString("N2", CultureInfo.InvariantCulture);
            ImuRoll = nmea.imuRollData.ToString(CultureInfo.InvariantCulture);
            ImuPitch = nmea.imuPitchData.ToString(CultureInfo.InvariantCulture);
            ImuYawRate = nmea.imuYawRateData.ToString(CultureInfo.InvariantCulture);
            ImuHeading = nmea.imuHeadingData.ToString(CultureInfo.InvariantCulture);

            Age = nmea.ageData.ToString("N1", CultureInfo.InvariantCulture);

            HeadingTrue = nmea.headingTrueData.ToString("N2", CultureInfo.InvariantCulture);
            HeadingTrueDual = nmea.headingTrueDualData.ToString("N2", CultureInfo.InvariantCulture);

            Altitude = nmea.altitudeData.ToString("N1", CultureInfo.InvariantCulture);

            // Raw sentence text is captured verbatim by the parser (non-null; defaults to ""), passed
            // through unchanged. Coalesced defensively so the view never binds a null string.
            VtgText = nmea.vtgSentence ?? string.Empty;
            GgaText = nmea.ggaSentence ?? string.Empty;
            PaogiText = nmea.paogiSentence ?? string.Empty;
            AvrText = nmea.avrSentence ?? string.Empty;
            HdtText = nmea.hdtSentence ?? string.Empty;
            HpdText = nmea.hpdSentence ?? string.Empty;
            PandaText = nmea.pandaSentence ?? string.Empty;
            KsxtText = nmea.ksxtSentence ?? string.Empty;
        }

        /// <summary>
        /// [XPLAT] Stops the refresh timer and turns off raw-sentence capture in the NMEA parser, mirroring
        /// the WinForms <c>FormGPSData_FormClosing</c> handler (<c>mf.isGPSSentencesOn = false</c>). The
        /// hosting view calls this when the dialog closes. Safe to call more than once.
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            _comm.Nmea.isGPSSentencesOn = false;
        }

        /// <summary>
        /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises a property-changed
        /// notification for the calling property, but only when the value actually changes (ordinal
        /// comparison). The base <see cref="ViewModel"/> exposes <c>NotifyPropertyChanged</c> rather than a
        /// <c>SetProperty</c> primitive, so this thin, change-guarded wrapper provides the guard-then-notify
        /// ergonomics used across the AgIO view-models while avoiding redundant UI updates each tick.
        /// </summary>
        /// <param name="field">The backing field to update, passed by reference.</param>
        /// <param name="value">The new display string.</param>
        /// <param name="name">The bound property name; supplied automatically by the compiler.</param>
        private void SetProperty(ref string field, string value, [CallerMemberName] string name = null)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            NotifyPropertyChanged(name);
        }
    }
}
