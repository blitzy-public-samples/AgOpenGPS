// [XPLAT] migrated from net48/WinForms FormLoop — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgIO.Properties;
using AgIO.Services;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Avalonia MVVM view-model backing the AgIO main window (<c>MainWindow.axaml</c>). It
    /// reimplements the <b>display + coordination</b> role of the deleted WinForms <c>FormLoop</c>
    /// (<c>Forms/FormLoop.cs</c> + <c>Forms/FormLoop.Designer.cs</c>): it surfaces live module status
    /// (GPS / Steer / Machine / IMU), NTRIP byte activity, lat/lon readouts, traffic counters and the
    /// advanced-view diagnostics, and issues the non-dialog commands (launch AgOpenGPS / GPS_Out, NTRIP
    /// start-stop, advanced-view toggle, exit).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] All byte-level communication logic is FROZEN and lives in <c>../Services/</c>. This
    /// view-model NEVER touches the receive→fuse→steer→section path: it only <b>reads</b> already-computed
    /// service state for display and <b>issues intent</b> through the <see cref="CommCoordinatorService"/>
    /// facade. The one-second <see cref="DispatcherTimer"/> mirrors the cadence of the WinForms
    /// <c>FormLoop.DoHelloAlarmLogic</c>/<c>DoTraffic</c> one-/two-second loop, but performs display reads
    /// only, so it adds no latency to the comm path (AAP §0.6.1 real-time-loop rule).
    /// </para>
    /// <para>
    /// Status colours reproduce the WinForms <c>FormLoop</c> palette (LimeGreen talking / Red silent;
    /// CornflowerBlue NTRIP active / DarkOrange idle / Transparent off) via the shared named brushes the
    /// Source-root agent declared in <c>App.axaml</c> (<c>AogModuleOkBrush</c>, <c>AogModuleAlarmBrush</c>,
    /// <c>AogNtripActiveBrush</c>, <c>AogNtripIdleBrush</c> — the documented STABLE key contract). All
    /// numeric→string display uses <see cref="CultureInfo.InvariantCulture"/> so a comma-decimal locale can
    /// never alter a displayed protocol/GPS value (AAP §0.6.5 — the #1 data-integrity rule).
    /// </para>
    /// <para>
    /// Dialog launches (FormUDP/FormNtrip/FormRadio/…) are intentionally NOT handled here: they require a
    /// <c>Window</c> owner and live in <c>MainWindow.axaml.cs</c>, which reaches the services through
    /// <see cref="Comm"/> and the mutual-exclusion guards (<see cref="CommCoordinatorService.CanOpenNtripSettings"/>
    /// / <see cref="CommCoordinatorService.CanOpenRadioSettings"/>) and surfaces messages through
    /// <see cref="ErrorPresenter"/>.
    /// </para>
    /// </remarks>
    public class MainWindowViewModel : ViewModel
    {
        // ---- Collaborators (lifecycle owned by the composition root; deliberately NOT disposed here) ----
        private readonly CommCoordinatorService _comm;
        private readonly IErrorPresenter _errorPresenter;

        // ---- One-second display-refresh timer (created lazily in Start so construction needs no Dispatcher) --
        private DispatcherTimer _uiTimer;
        private bool _eventsHooked;

        // [XPLAT] Cached service-event payloads. The service StatusChanged events may fire on socket/serial
        // threads, so the handlers only cache the (atomic) string references; the UI-thread timer publishes
        // them to the bound properties. Each is initialised non-null so a binding never shows "null".
        private string _ntripWatchCached = "";
        private string _ntripBytesCached = "";
        private string _ntripCountdownCached = "";
        private string _ntripMountCached = "";
        private string _ntripStationCached = "";
        private string _udpIpCached = "";

        // [XPLAT] NTRIP byte-flow detection for the activity brush: the WinForms original blinked the byte
        // label while corrections were arriving. We detect "actively flowing" as a change in the service's
        // cumulative tripBytes counter between two one-second ticks (no comm logic duplicated — pure read).
        private uint _lastTripBytes;
        private bool _ntripBytesFlowing;

        // ---- Backing fields for bound display text (seeded with the WinForms FormLoop initial values) ----
        private string _currentLatText = "";
        private string _currentLonText = "";
        private string _fromGpsText = "---";
        private string _toGpsText = "---";
        private string _watchText = "";
        private string _ntripBytesText = "";
        private string _ipText = "Off";
        private string _serialPortsText = "None";
        private string _rtcmMessagesText = "";
        private string _steerAngleText = "";
        private string _wasCountsText = "";
        private string _switchStatusText = "";
        private string _pingText = "";
        private string _stationIdText = "";
        private string _mountText = "";

        // ---- Advanced-view (wide flyout) toggle — mirrors FormLoop.isViewAdvanced / btnSlide ----
        private bool _isAdvancedView;

        /// <summary>
        /// [XPLAT] Builds the view-model over the AgIO comms-hub facade. This constructor signature is a
        /// hard contract consumed by the Avalonia composition root and the window code-behind.
        /// </summary>
        /// <param name="comm">The comms-hub coordinator facade owning the UDP/serial/NTRIP/NMEA services.
        /// Must not be <see langword="null"/>.</param>
        /// <param name="errorPresenter">Optional presenter that surfaces transient messages in place of the
        /// WinForms <c>TimedMessageBox</c>; may be <see langword="null"/> (every use is null-conditional).</param>
        public MainWindowViewModel(CommCoordinatorService comm, IErrorPresenter errorPresenter)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;
            _errorPresenter = errorPresenter;

            // [XPLAT] Non-dialog commands only (dialog launches need a Window owner -> code-behind).
            StartAogCommand = new RelayCommand(() => _comm.StartAOG());
            StartGpsOutCommand = new RelayCommand(() => _comm.StartGPS_Out());
            ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
            StartStopNtripCommand = new RelayCommand(ToggleNtrip);
            ToggleAdvancedViewCommand = new RelayCommand(() => IsAdvancedView = !IsAdvancedView);

            // Seed the NTRIP byte-flow baseline so the first tick does not register a spurious "flowing".
            _lastTripBytes = _comm.Ntrip.tripBytes;

            HookCommEvents();
        }

        /// <summary>
        /// [XPLAT] The comms-hub facade. Exposed so the window code-behind (which owns the
        /// <c>Window</c> needed for <c>ShowDialog</c>) can reach the peer services and the
        /// NTRIP/Radio mutual-exclusion guards when opening configuration dialogs.
        /// </summary>
        public CommCoordinatorService Comm => _comm;

        /// <summary>
        /// [XPLAT] The optional transient-message presenter, exposed so the code-behind can surface
        /// dialog-guard messages through the same channel this view-model uses.
        /// </summary>
        public IErrorPresenter ErrorPresenter => _errorPresenter;

        /// <summary>
        /// [XPLAT] Raised by <see cref="ExitCommand"/> so the window code-behind can close the window.
        /// The view-model never calls <c>Environment.Exit</c> directly (MVVM separation).
        /// </summary>
        public event EventHandler ExitRequested;

        // ===========================================================================================
        //  Refresh-timer lifecycle (replaces the WinForms FormLoop timers)
        // ===========================================================================================

        /// <summary>
        /// [XPLAT] Starts the one-second display refresh. Lazily creates the <see cref="DispatcherTimer"/>
        /// (so constructing the view-model imposes no Dispatcher dependency), paints once immediately, then
        /// arms the timer. Safe to call again after <see cref="StopTimer"/>.
        /// </summary>
        public void Start()
        {
            if (_uiTimer == null)
            {
                _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _uiTimer.Tick += OnUiTimerTick;
            }

            // Re-hook events if a prior StopTimer detached them (defensive; window lifecycle is normally terminal).
            HookCommEvents();

            RefreshDisplay();
            _uiTimer.Start();
        }

        /// <summary>
        /// [XPLAT] Stops the refresh timer and detaches the service-event handlers so the long-lived comm
        /// services do not retain a closed window's view-model. Idempotent; called from
        /// <c>MainWindow.axaml.cs</c> on window close.
        /// </summary>
        public void StopTimer()
        {
            if (_uiTimer != null)
            {
                _uiTimer.Stop();
                _uiTimer.Tick -= OnUiTimerTick;
                _uiTimer = null;
            }

            UnhookCommEvents();
        }

        // [XPLAT] DispatcherTimer.Tick fires on the UI thread, so the refresh is binding-safe.
        private void OnUiTimerTick(object sender, EventArgs e)
        {
            RefreshDisplay();
        }

        // [XPLAT] Reads already-computed service state and publishes it to the bound display + status-brush
        // properties. This reproduces what FormLoop.DoTraffic / DoHelloAlarmLogic SHOWED (the values), not
        // their comm bookkeeping (counter increment / reset stays in the services). Runs on the UI thread.
        private void RefreshDisplay()
        {
            NmeaService nmea = _comm.Nmea;
            CTraffic traffic = _comm.Udp.traffic;

            // Position readouts — mirrors FormLoop lblCurrentLat / lblCurentLon ("N7", invariant).
            CurrentLatText = nmea.latitude.ToString("N7", CultureInfo.InvariantCulture);
            CurrentLonText = nmea.longitude.ToString("N7", CultureInfo.InvariantCulture);

            // GPS traffic counters — mirrors FormLoop lblFromGPS / lblToGPS ("---" when no data, invariant).
            FromGpsText = traffic.cntrGPSIn == 0
                ? "---"
                : traffic.cntrGPSIn.ToString(CultureInfo.InvariantCulture);
            ToGpsText = traffic.cntrGPSOut == 0
                ? "---"
                : traffic.cntrGPSOut.ToString(CultureInfo.InvariantCulture);

            // NTRIP byte count / watchdog / RTCM-countdown — prefer the service's formatted status string,
            // falling back to the raw cumulative byte counter (invariant) when no status has arrived yet.
            NtripBytesText = string.IsNullOrEmpty(_ntripBytesCached)
                ? _comm.Ntrip.tripBytes.ToString(CultureInfo.InvariantCulture)
                : _ntripBytesCached;
            WatchText = _ntripWatchCached;
            RtcmMessagesText = _ntripCountdownCached;

            // Network IP list — "Off" when the module UDP network is not connected (FormLoop lblIP).
            IpText = _comm.Udp.isUDPNetworkConnected
                ? (string.IsNullOrEmpty(_udpIpCached) ? "" : _udpIpCached)
                : "Off";

            // Serial-port list — routed through the cross-platform port-name provider (never null).
            string joinedPorts = string.Join("\r\n", _comm.Serial.GetAvailablePortNames());
            SerialPortsText = string.IsNullOrEmpty(joinedPorts) ? "None" : joinedPorts;

            // Advanced-view diagnostics — computed (with identical formatting) inside the UDP/NTRIP services.
            SteerAngleText = _comm.Udp.SteerAngle;
            WasCountsText = _comm.Udp.WasCounts;
            SwitchStatusText = _comm.Udp.SwitchStatus;
            PingText = _comm.Udp.Ping;
            MountText = _ntripMountCached;
            StationIdText = _ntripStationCached;

            // NTRIP byte-flow detection (delta since last tick) for the activity brush.
            uint tripBytes = _comm.Ntrip.tripBytes;
            _ntripBytesFlowing = tripBytes != _lastTripBytes;
            _lastTripBytes = tripBytes;

            // Status brushes are computed from live comm state — re-notify so the bindings re-read them.
            NotifyPropertyChanged(nameof(SteerStatusBrush));
            NotifyPropertyChanged(nameof(MachineStatusBrush));
            NotifyPropertyChanged(nameof(ImuStatusBrush));
            NotifyPropertyChanged(nameof(GpsStatusBrush));
            NotifyPropertyChanged(nameof(NtripActivityBrush));
        }

        // ===========================================================================================
        //  Service-event handlers (cache only; the UI-thread timer publishes to bound properties)
        // ===========================================================================================

        private void HookCommEvents()
        {
            if (_eventsHooked)
            {
                return;
            }

            _comm.StatusChanged += OnCommStatusChanged;
            _comm.Udp.StatusChanged += OnUdpStatusChanged;
            _comm.Ntrip.StatusChanged += OnNtripStatusChanged;
            _comm.Ntrip.MessageRequested += OnNtripMessageRequested;
            _eventsHooked = true;
        }

        private void UnhookCommEvents()
        {
            if (!_eventsHooked)
            {
                return;
            }

            _comm.StatusChanged -= OnCommStatusChanged;
            _comm.Udp.StatusChanged -= OnUdpStatusChanged;
            _comm.Ntrip.StatusChanged -= OnNtripStatusChanged;
            _comm.Ntrip.MessageRequested -= OnNtripMessageRequested;
            _eventsHooked = false;
        }

        // [XPLAT] Coordinator-level status change (e.g. the Radio guard shut NTRIP down). Re-read the live
        // status on the UI thread so the brushes/labels refresh promptly rather than waiting for the tick.
        private void OnCommStatusChanged(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(RefreshDisplay);
        }

        // [XPLAT] UDP transport status: cache the local-IP list delivered on a successful network load.
        private void OnUdpStatusChanged(object sender, UdpStatusEventArgs e)
        {
            if (e != null && e.Connected && !string.IsNullOrEmpty(e.Message))
            {
                _udpIpCached = e.Message;
            }
        }

        // [XPLAT] NTRIP status: cache the piecemeal label values (each is null when not updated this raise,
        // mirroring the WinForms per-label writes — keep the previous value in that case).
        private void OnNtripStatusChanged(object sender, NtripStatusEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.Watch != null) _ntripWatchCached = e.Watch;
            if (e.Bytes != null) _ntripBytesCached = e.Bytes;
            if (e.Countdown != null) _ntripCountdownCached = e.Countdown;
            if (e.Mount != null) _ntripMountCached = e.Mount;
            if (e.Ip != null) _ntripStationCached = e.Ip;
        }

        // [XPLAT] NTRIP transient message -> route to the optional presenter on the UI thread (replaces the
        // WinForms TimedMessageBox). No-op when no presenter was supplied.
        private void OnNtripMessageRequested(object sender, NtripMessageEventArgs e)
        {
            IErrorPresenter presenter = _errorPresenter;
            if (presenter == null || e == null)
            {
                return;
            }

            int timeoutMs = e.TimeoutMs;
            string title = e.Title;
            string message = e.Message;
            Dispatcher.UIThread.Post(() =>
                presenter.PresentTimedMessage(TimeSpan.FromMilliseconds(timeoutMs), title, message));
        }

        // ===========================================================================================
        //  Display text properties (one-way: updated only by RefreshDisplay)
        // ===========================================================================================

        /// <summary>[XPLAT] Current latitude, formatted "N7" invariant (FormLoop lblCurrentLat).</summary>
        public string CurrentLatText
        {
            get => _currentLatText;
            private set { if (_currentLatText != value) { _currentLatText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Current longitude, formatted "N7" invariant (FormLoop lblCurentLon).</summary>
        public string CurrentLonText
        {
            get => _currentLonText;
            private set { if (_currentLonText != value) { _currentLonText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Inbound GPS frame counter, invariant ("---" when idle; FormLoop lblFromGPS).</summary>
        public string FromGpsText
        {
            get => _fromGpsText;
            private set { if (_fromGpsText != value) { _fromGpsText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Outbound GPS frame counter, invariant ("---" when idle; FormLoop lblToGPS).</summary>
        public string ToGpsText
        {
            get => _toGpsText;
            private set { if (_toGpsText != value) { _toGpsText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] NTRIP watchdog/status line (FormLoop lblWatch).</summary>
        public string WatchText
        {
            get => _watchText;
            private set { if (_watchText != value) { _watchText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] NTRIP byte count/activity text (FormLoop lblNTRIPBytes).</summary>
        public string NtripBytesText
        {
            get => _ntripBytesText;
            private set { if (_ntripBytesText != value) { _ntripBytesText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Local IP list, or "Off" when the module UDP network is off (FormLoop lblIP).</summary>
        public string IpText
        {
            get => _ipText;
            private set { if (_ipText != value) { _ipText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Discovered serial ports, or "None" (FormLoop lblSerialPorts).</summary>
        public string SerialPortsText
        {
            get => _serialPortsText;
            private set { if (_serialPortsText != value) { _serialPortsText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] RTCM message / NTRIP reconnect-countdown text (FormLoop lblMessages/lblMessagesFound).</summary>
        public string RtcmMessagesText
        {
            get => _rtcmMessagesText;
            private set { if (_rtcmMessagesText != value) { _rtcmMessagesText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: autosteer angle readout (FormLoop lblSteerAngle).</summary>
        public string SteerAngleText
        {
            get => _steerAngleText;
            private set { if (_steerAngleText != value) { _steerAngleText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: WAS counts readout (FormLoop lblWASCounts).</summary>
        public string WasCountsText
        {
            get => _wasCountsText;
            private set { if (_wasCountsText != value) { _wasCountsText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: switch-status readout (FormLoop lblSwitchStatus).</summary>
        public string SwitchStatusText
        {
            get => _switchStatusText;
            private set { if (_switchStatusText != value) { _switchStatusText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: module ping readout (FormLoop lblPing).</summary>
        public string PingText
        {
            get => _pingText;
            private set { if (_pingText != value) { _pingText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: NTRIP caster station/IP readout (FormLoop lblStationID).</summary>
        public string StationIdText
        {
            get => _stationIdText;
            private set { if (_stationIdText != value) { _stationIdText = value; NotifyPropertyChanged(); } }
        }

        /// <summary>[XPLAT] Advanced view: NTRIP mount-point readout (FormLoop lblMount).</summary>
        public string MountText
        {
            get => _mountText;
            private set { if (_mountText != value) { _mountText = value; NotifyPropertyChanged(); } }
        }

        // ===========================================================================================
        //  Module-enable toggles (parity with cboxIsSteerModule / cboxIsMachineModule / cboxIsIMUModule)
        // ===========================================================================================

        /// <summary>
        /// [XPLAT] Whether the Steer module is enabled. The setter persists to settings and saves, mirroring
        /// the WinForms <c>FormLoop</c> module-checkbox handling (<c>setMod_isSteerConnected</c>).
        /// </summary>
        public bool IsSteerModuleEnabled
        {
            get => Settings.Default.setMod_isSteerConnected;
            set
            {
                if (Settings.Default.setMod_isSteerConnected != value)
                {
                    Settings.Default.setMod_isSteerConnected = value;
                    Settings.Default.Save();
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Whether the Machine module is enabled (persists <c>setMod_isMachineConnected</c>).
        /// </summary>
        public bool IsMachineModuleEnabled
        {
            get => Settings.Default.setMod_isMachineConnected;
            set
            {
                if (Settings.Default.setMod_isMachineConnected != value)
                {
                    Settings.Default.setMod_isMachineConnected = value;
                    Settings.Default.Save();
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Whether the IMU module is enabled (persists <c>setMod_isIMUConnected</c>).
        /// </summary>
        public bool IsImuModuleEnabled
        {
            get => Settings.Default.setMod_isIMUConnected;
            set
            {
                if (Settings.Default.setMod_isIMUConnected != value)
                {
                    Settings.Default.setMod_isIMUConnected = value;
                    Settings.Default.Save();
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// [XPLAT] Advanced-view flag driving the wide-flyout layout (mirrors FormLoop <c>isViewAdvanced</c>
        /// / the <c>btnSlide</c> narrow-vs-wide width toggle). Flipped by <see cref="ToggleAdvancedViewCommand"/>.
        /// </summary>
        public bool IsAdvancedView
        {
            get => _isAdvancedView;
            set { if (_isAdvancedView != value) { _isAdvancedView = value; NotifyPropertyChanged(); } }
        }

        // ===========================================================================================
        //  Status brushes (colour parity -> bind Background in XAML). Computed from live comm state and
        //  re-notified on each timer tick. Keys match the App.axaml STABLE key contract.
        // ===========================================================================================

        /// <summary>[XPLAT] Steer module indicator brush (LimeGreen talking / Red silent).</summary>
        public IBrush SteerStatusBrush =>
            Brush(_comm.IsSteerConnected ? "AogModuleOkBrush" : "AogModuleAlarmBrush");

        /// <summary>[XPLAT] Machine module indicator brush (LimeGreen talking / Red silent).</summary>
        public IBrush MachineStatusBrush =>
            Brush(_comm.IsMachineConnected ? "AogModuleOkBrush" : "AogModuleAlarmBrush");

        /// <summary>[XPLAT] GPS indicator brush (LimeGreen flowing / Red silent).</summary>
        public IBrush GpsStatusBrush =>
            Brush(_comm.IsGpsConnected ? "AogModuleOkBrush" : "AogModuleAlarmBrush");

        /// <summary>[XPLAT] IMU module indicator brush (LimeGreen talking / Red silent).</summary>
        public IBrush ImuStatusBrush =>
            Brush(_comm.IsImuConnected ? "AogModuleOkBrush" : "AogModuleAlarmBrush");

        /// <summary>
        /// [XPLAT] NTRIP byte-activity brush: Transparent when NTRIP is off, CornflowerBlue while bytes are
        /// actively flowing, DarkOrange when on but idle (FormLoop lblNTRIPBytes blink).
        /// </summary>
        public IBrush NtripActivityBrush
        {
            get
            {
                if (!_comm.Ntrip.isNTRIP_RequiredOn)
                {
                    return Brushes.Transparent;
                }

                return Brush(_ntripBytesFlowing ? "AogNtripActiveBrush" : "AogNtripIdleBrush");
            }
        }

        // [XPLAT] Resolve a named brush from the Avalonia application resources (App.axaml), with a safe
        // Transparent fallback so a missing key (or a null Application during unit tests) never throws.
        private static IBrush Brush(string key) =>
            (Application.Current != null && Application.Current.TryFindResource(key, out var r) && r is IBrush b)
                ? b
                : Brushes.Transparent;

        // ===========================================================================================
        //  Commands (non-dialog actions only; dialog launches live in MainWindow.axaml.cs)
        // ===========================================================================================

        /// <summary>[XPLAT] Launch (or foreground-raise) AgOpenGPS — the frozen two-program auto-start.</summary>
        public RelayCommand StartAogCommand { get; }

        /// <summary>[XPLAT] Launch (or foreground-raise) the GPS_Out helper.</summary>
        public RelayCommand StartGpsOutCommand { get; }

        /// <summary>[XPLAT] Request window close via <see cref="ExitRequested"/> (code-behind closes).</summary>
        public RelayCommand ExitCommand { get; }

        /// <summary>[XPLAT] Toggle the NTRIP correction source on/off (mirrors btnStartStopNtrip).</summary>
        public RelayCommand StartStopNtripCommand { get; }

        /// <summary>[XPLAT] Flip the advanced-view flyout (mirrors btnSlide).</summary>
        public RelayCommand ToggleAdvancedViewCommand { get; }

        // [XPLAT] NTRIP start/stop, expressed entirely through the frozen NtripService API. Starting persists
        // the on flag and (re)configures so ConfigureNTRIP resolves the caster and the coordinator's
        // one-second routine opens the connection; stopping persists the off flag and tears the link down.
        private void ToggleNtrip()
        {
            NtripService ntrip = _comm.Ntrip;

            if (ntrip.isNTRIP_RequiredOn)
            {
                Settings.Default.setNTRIP_isOn = false;
                Settings.Default.Save();
                ntrip.ShutDownNTRIP();
                ntrip.isNTRIP_RequiredOn = false;
            }
            else
            {
                Settings.Default.setNTRIP_isOn = true;
                Settings.Default.Save();
                ntrip.ConfigureNTRIP();
            }

            NotifyPropertyChanged(nameof(NtripActivityBrush));
        }
    }
}
