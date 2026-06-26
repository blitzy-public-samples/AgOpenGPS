// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.IO;
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Platform;

namespace AgIO.Services
{
    /// <summary>
    /// [XPLAT] Plain, injectable control-coordination <b>facade / aggregate</b> for the AgIO comms hub,
    /// extracted (behaviour frozen) from the former WinForms <c>FormLoop</c> partial class
    /// (<c>SourceCode/AgIO/Source/Forms/Controls.Designer.cs</c>). It is the single aggregate the AgIO
    /// composition root constructs: it <b>owns, constructs and wires</b> the four extracted comm services
    /// (<see cref="NmeaService"/>, <see cref="UdpLoopbackService"/>, <see cref="SerialCommService"/> and
    /// <see cref="NtripService"/>) and surfaces them — plus live module-hello status, the frozen
    /// two-program auto-start, and the NTRIP/Radio mutual-exclusion guards — to the Avalonia view-model
    /// (AAP §0.1.1, §0.3.2 "Facade").
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This facade does two jobs. First, it is the <b>composition aggregate</b> that breaks the
    /// four-service construction cycle with a two-phase construct→wire pattern (the single most important
    /// structural mechanism of the AgIO Services folder — it lets the cyclic comm services exist without
    /// any new public abstraction, satisfying AAP §0.7.1). The cycle is broken exactly as the sibling
    /// services finalised it: <see cref="UdpLoopbackService"/> takes no required peers in its constructor,
    /// so it is built first; <see cref="NmeaService"/> requires the UDP transport and is built immediately
    /// after; every remaining peer link is assigned through the services' <c>internal</c> settable
    /// properties (legal here because this file lives in the same <c>AgIO</c> assembly). Second, it carries
    /// the <b>frozen two-program auto-start</b> of AgOpenGPS/GPS_Out and the NTRIP/Radio coordination
    /// guards lifted from the WinForms partial.
    /// </para>
    /// <para>
    /// Everything UI (dialog launches, button handlers, status colours, the advanced-view width toggle and
    /// the <c>FormYes</c> confirmation) is intentionally delegated to the Avalonia Views layer; this service
    /// exposes only state, events and intent methods. The only Windows-only code is the
    /// <c>SetForegroundWindow</c>/<c>ShowWindow</c> foreground-raise, which is <c>#if WINDOWS</c>-gated with
    /// a logged no-op on Linux/macOS (AAP §0.6.3 graceful degradation). <c>Process.GetProcessesByName</c>
    /// and <c>Process.Start</c> are cross-platform and kept verbatim, preserving the two-program model.
    /// </para>
    /// </remarks>
    public sealed class CommCoordinatorService : IDisposable
    {
        // [XPLAT] Cross-platform services (serial port-name enumeration, single-instance, paths). Stored so
        // it can be handed to the serial service at construction; read during construction below.
        private readonly IPlatformServices _platform;

        // [XPLAT] Optional presenter replacing the WinForms TimedMessageBox/MessageBox popups. Null-guarded
        // with ?. at every call site, so an absent presenter is simply a no-op (the Log entries remain).
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] 1-second coordinator timer reproducing the FormLoop oneSecondLoopTimer that drove the NTRIP
        // per-second routine. System.Timers.Timer is cross-platform; its Elapsed handler runs on a
        // thread-pool thread and adds no latency to the comm path (AAP §0.6.1). Fully qualified so no extra
        // `using System.Timers;` is needed (avoids a Timer ambiguity and keeps the using set minimal).
        private readonly System.Timers.Timer _ntripSecondTimer;

        // [XPLAT] Idempotency guard for Dispose() and a fast-exit for the timer Elapsed handler that may
        // race with shutdown/restart.
        private bool _disposed;

        /// <summary>
        /// [XPLAT] Module-hello "alive" threshold (in one-second scan ticks). A module is considered
        /// connected while its <c>traffic.helloFromXxx</c> counter is below this value, exactly as the
        /// WinForms <c>FormLoop</c> alarm logic decided (counter reset to 0 on each inbound hello frame,
        /// incremented once per scan tick). Frozen at the source value.
        /// </summary>
        private const int ModuleHelloTimeout = 3;

        /// <summary>[XPLAT] The NMEA parse/build service (sentence parsing + PGN 0xD6 GPS frame assembly).</summary>
        public NmeaService Nmea { get; }

        /// <summary>[XPLAT] The UDP loopback + module-broadcast transport (ports 17777 / 15555 / 9999 / 8888).</summary>
        public UdpLoopbackService Udp { get; }

        /// <summary>[XPLAT] The serial-port transport (GPS / GPS2 / RTCM / IMU / steer / machine ports).</summary>
        public SerialCommService Serial { get; }

        /// <summary>[XPLAT] The NTRIP / radio / serial-pass correction-source service.</summary>
        public NtripService Ntrip { get; }

        /// <summary>
        /// [XPLAT] Raised when coordinator-level status changes (for example after the Radio guard shuts
        /// down an active NTRIP connection). Replaces the WinForms direct status-label/button-text mutation;
        /// the view-model subscribes and re-reads the exposed status properties to refresh its display.
        /// </summary>
        public event EventHandler StatusChanged;

        /// <summary>
        /// [XPLAT] True while the steer module's hello frame is current (its <c>helloFromAutoSteer</c> counter
        /// is below <see cref="ModuleHelloTimeout"/>). Computed on read from the UDP transport's traffic
        /// counters exactly as the WinForms alarm logic did; the view-model binds this to its status colour.
        /// </summary>
        public bool IsSteerConnected => Udp != null && Udp.traffic.helloFromAutoSteer < ModuleHelloTimeout;

        /// <summary>
        /// [XPLAT] True while the machine module's hello frame is current (<c>helloFromMachine</c> below the
        /// timeout). See <see cref="IsSteerConnected"/>.
        /// </summary>
        public bool IsMachineConnected => Udp != null && Udp.traffic.helloFromMachine < ModuleHelloTimeout;

        /// <summary>
        /// [XPLAT] True while the IMU module's hello frame is current (<c>helloFromIMU</c> below the timeout).
        /// See <see cref="IsSteerConnected"/>.
        /// </summary>
        public bool IsImuConnected => Udp != null && Udp.traffic.helloFromIMU < ModuleHelloTimeout;

        /// <summary>
        /// [XPLAT] True while GPS sentences are flowing (the UDP transport's per-cycle GPS-out byte counter
        /// is non-zero), mirroring the WinForms GPS status indicator.
        /// </summary>
        public bool IsGpsConnected => Udp != null && Udp.traffic.cntrGPSOut != 0;

        /// <summary>
        /// [XPLAT] True while an NTRIP correction source is connected, mirroring the WinForms NTRIP status
        /// indicator (<c>isNTRIP_Connected</c>).
        /// </summary>
        public bool IsNtripOnline => Ntrip != null && Ntrip.isNTRIP_Connected;

        /// <summary>
        /// [XPLAT] Builds the comms-hub aggregate. Constructs the four comm services in a cycle-breaking
        /// order and wires every peer reference, then arms (but does not start) the one-second NTRIP routine
        /// timer. Call <see cref="StartComms"/> from the composition root to begin transport.
        /// </summary>
        /// <param name="platform">Cross-platform services (serial port-name enumeration, single-instance,
        /// application-data root). Passed to the serial service for port-name discovery.</param>
        /// <param name="errorPresenter">Optional presenter that surfaces transient messages in place of the
        /// WinForms <c>TimedMessageBox</c>; may be null (every use is null-conditional).</param>
        public CommCoordinatorService(IPlatformServices platform, IErrorPresenter errorPresenter = null)
        {
            _platform = platform;
            _errorPresenter = errorPresenter;

            // ---- Phase 1: construct (cycle-breaking order) ----------------------------------------
            // [XPLAT] UdpLoopbackService takes no required peers, so it is built first. NmeaService
            // requires the UDP transport (it validates it non-null), so it is built immediately after.
            // This reproduces the collaborator graph the WinForms FormLoop partials shared implicitly
            // through the form instance, and matches the finalised sibling constructor signatures.
            Udp = new UdpLoopbackService();
            Nmea = new NmeaService(Udp);
            Serial = new SerialCommService(_platform, _errorPresenter);
            Ntrip = new NtripService(_errorPresenter);

            // ---- Phase 2: wire cyclic peers (internal settable properties; same AgIO assembly) -----
            // [XPLAT] Nmea's UDP peer is constructor-injected (no settable property), so it is NOT wired
            // here; every other peer link is assigned after construction.
            Udp.Nmea = Nmea;
            Udp.Serial = Serial;

            Serial.Nmea = Nmea;
            Serial.Udp = Udp;

            Ntrip.Nmea = Nmea;
            Ntrip.Udp = Udp;
            Ntrip.Serial = Serial;

            // ---- One-second NTRIP routine timer (FormLoop oneSecondLoopTimer -> System.Timers.Timer) --
            // [XPLAT] AutoReset repeats on the 1 s cadence like the original WinForms timer; the source
            // FormLoop drove DoNTRIPSecondRoutine() every second (connect/reconnect/watchdog). The timer is
            // armed here but only started by StartComms() so a coordinator that is never started imposes no
            // background work.
            _ntripSecondTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _ntripSecondTimer.Elapsed += OnNtripSecondElapsed;
        }

        /// <summary>
        /// [XPLAT] Starts the comms hub: binds the loopback socket (port 17777, always) and — when
        /// <paramref name="loadUdpNetwork"/> is set — the module UDP network socket (port 9999), then starts
        /// the one-second NTRIP routine. Mirrors the comm-start portion of the WinForms <c>FormLoop_Load</c>.
        /// The settings-driven opening of previously-connected serial ports is intentionally left to the
        /// view-model (it owns the persisted port names/baud rates), keeping this aggregate decoupled from
        /// the settings backing.
        /// </summary>
        /// <param name="loadUdpNetwork">When true (the default) the module UDP network socket is also opened;
        /// pass the persisted "UDP on" setting here to reproduce the FormLoop gate.</param>
        public void StartComms(bool loadUdpNetwork = true)
        {
            Udp.LoadLoopback();

            if (loadUdpNetwork)
            {
                Udp.LoadUDPNetwork();
            }

            _ntripSecondTimer.Start();
        }

        /// <summary>
        /// [XPLAT] Stops the one-second NTRIP routine without tearing down the transports. Symmetric with
        /// <see cref="StartComms"/>; full teardown (sockets/ports/timers) happens in <see cref="Dispose"/>.
        /// </summary>
        public void StopComms()
        {
            _ntripSecondTimer.Stop();
        }

        // [XPLAT] One-second timer tick. Drives the NTRIP service's complete per-second routine
        // (DoNTRIPSecondRoutine — connect/reconnect plus the watchdog increment), exactly as the WinForms
        // FormLoop one-second timer did. (The lower-level IncrementNTRIPWatchDog is private to NtripService
        // and is invoked from within DoNTRIPSecondRoutine, so driving the public routine preserves the
        // frozen behaviour in full.) Guarded against shutdown races and swallowing exceptions so a transient
        // fault never tears down the timer.
        private void OnNtripSecondElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Ntrip.DoNTRIPSecondRoutine();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch -> CommCoordinator NTRIP second routine: " + ex.Message);
            }
        }

        /// <summary>
        /// [XPLAT] Auto-starts (or foreground-raises) AgOpenGPS — the frozen two-program contract lifted from
        /// the WinForms <c>FormLoop.StartAOG()</c>. <c>Application.StartupPath</c> becomes
        /// <c>AppContext.BaseDirectory</c> and the executable name is OS-aware; the foreground-raise is
        /// Windows-only and feature-gated (logged no-op elsewhere).
        /// </summary>
        public void StartAOG()
        {
            Process[] processName = Process.GetProcessesByName("AgOpenGPS");
            if (processName.Length == 0)
            {
                try
                {
                    // [XPLAT] Application.StartupPath -> AppContext.BaseDirectory; OS-aware exe name.
                    string exeName = OperatingSystem.IsWindows() ? "AgOpenGPS.exe" : "AgOpenGPS";
                    string strPath = Path.Combine(AppContext.BaseDirectory, exeName);
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = strPath,
                        WorkingDirectory = Path.GetDirectoryName(strPath)
                    };
                    Process.Start(processInfo);
                }
                catch
                {
                    _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "No File Found", "Can't Find AgOpenGPS");
                    Log.EventWriter("Can't Find AgOpenGPS");
                }
            }
            else
            {
#if WINDOWS
                // [XPLAT] foreground-raise is Windows-only (user32) — feature-gated; no portable equivalent.
                ShowWindow(processName[0].MainWindowHandle, 9);   // SW_RESTORE
                SetForegroundWindow(processName[0].MainWindowHandle);
#else
                Log.EventWriter("AgOpenGPS already running (foreground-raise not supported on this OS)");
#endif
            }
        }

        /// <summary>
        /// [XPLAT] Auto-starts (or foreground-raises) the GPS_Out helper — the frozen two-program contract
        /// lifted from the WinForms <c>FormLoop.StartGPS_Out()</c>. Identical structure to
        /// <see cref="StartAOG"/> for the <c>GPS_Out</c> process/executable.
        /// </summary>
        public void StartGPS_Out()
        {
            Process[] processName = Process.GetProcessesByName("GPS_Out");
            if (processName.Length == 0)
            {
                try
                {
                    // [XPLAT] Application.StartupPath -> AppContext.BaseDirectory; OS-aware exe name.
                    string exeName = OperatingSystem.IsWindows() ? "GPS_Out.exe" : "GPS_Out";
                    string strPath = Path.Combine(AppContext.BaseDirectory, exeName);
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = strPath,
                        WorkingDirectory = Path.GetDirectoryName(strPath)
                    };
                    Process.Start(processInfo);
                }
                catch
                {
                    _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "No File Found", "Can't Find GPS_Out");
                    Log.EventWriter("Can't Find GPS_Out");
                }
            }
            else
            {
#if WINDOWS
                // [XPLAT] foreground-raise is Windows-only (user32) — feature-gated; no portable equivalent.
                ShowWindow(processName[0].MainWindowHandle, 9);   // SW_RESTORE
                SetForegroundWindow(processName[0].MainWindowHandle);
#else
                Log.EventWriter("GPS_Out already running (foreground-raise not supported on this OS)");
#endif
            }
        }

        /// <summary>
        /// [XPLAT] NTRIP-settings mutual-exclusion guard lifted verbatim from the WinForms
        /// <c>FormLoop.SettingsNTRIP()</c> head. The view-model calls this <b>before</b> opening the NTRIP
        /// settings dialog: it returns <see langword="false"/> (after presenting the exact timed message)
        /// when a conflicting source is on, and <see langword="true"/> when NTRIP settings may be opened.
        /// </summary>
        /// <returns><see langword="true"/> when the NTRIP settings dialog may be opened.</returns>
        public bool CanOpenNtripSettings()
        {
            if (Ntrip.isRadio_RequiredOn)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "Radio NTRIP ON", "Turn it off before using NTRIP");
                return false;
            }

            if (Ntrip.isSerialPass_RequiredOn)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "Serial NTRIP ON", "Turn it off before using NTRIP");
                return false;
            }

            return true;
        }

        /// <summary>
        /// [XPLAT] Radio-settings mutual-exclusion guard lifted verbatim from the WinForms
        /// <c>FormLoop.SettingsRadio()</c> head. The view-model calls this <b>before</b> opening the radio
        /// settings dialog: it returns <see langword="false"/> (after presenting the exact timed message)
        /// when a conflicting source is on. When radio is required while an NTRIP connection is live it shuts
        /// the NTRIP connection down, clears the radio-required flag and raises <see cref="StatusChanged"/>
        /// (replacing the WinForms <c>lblWatch</c>/<c>btnStartStopNtrip</c> label updates), then allows the
        /// dialog.
        /// </summary>
        /// <returns><see langword="true"/> when the radio settings dialog may be opened.</returns>
        public bool CanOpenRadioSettings()
        {
            if (Ntrip.isSerialPass_RequiredOn)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "Serial Pass NTRIP ON", "Turn it off before using Radio NTRIP");
                return false;
            }

            if (Ntrip.isNTRIP_RequiredOn)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), "Air NTRIP ON", "Turn it off before using Radio NTRIP");
                return false;
            }

            if (Ntrip.isRadio_RequiredOn && Ntrip.isNTRIP_Connected)
            {
                Ntrip.ShutDownNTRIP();
                Ntrip.isRadio_RequiredOn = false;

                // [XPLAT] The WinForms code set lblWatch = "Stopped" and btnStartStopNtrip = "OffLine" here;
                // surfaced through StatusChanged for the view-model to refresh.
                OnStatusChanged();
            }

            return true;
        }

        // [XPLAT] Raises the coordinator status-changed notification (EventArgs.Empty — the view-model
        // re-reads the exposed status properties). Centralised so future status transitions reuse it.
        private void OnStatusChanged()
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

#if WINDOWS
        // [XPLAT] Win32 foreground-raise P/Invokes, relocated from the WinForms FormLoop.cs (L19-31) since
        // this service now owns the two-program foreground-raise. Compiled only on the net8.0-windows head
        // (the SDK auto-defines the WINDOWS symbol there); never present on the cross-platform net8.0 build,
        // so they cannot be referenced off-Windows.
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif

        /// <summary>
        /// [XPLAT] Tears down the aggregate: stops the one-second timer first (so a late Elapsed tick cannot
        /// touch a half-disposed service), then cascades <see cref="IDisposable.Dispose"/> to the owned
        /// transports so the loopback/UDP sockets (17777 / 15555 / 9999) and the serial COM/tty handles are
        /// released on shutdown and on <c>Program.Restart()</c>. <see cref="NmeaService"/> holds no
        /// unmanaged resources and is not disposable. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Stop + dispose the timer before the services so no Elapsed tick races the teardown.
            try
            {
                _ntripSecondTimer.Stop();
                _ntripSecondTimer.Elapsed -= OnNtripSecondElapsed;
                _ntripSecondTimer.Dispose();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch -> CommCoordinator Dispose timer: " + ex.Message);
            }

            // Cascade to the owned services so sockets/ports/timers are released on shutdown/restart.
            Ntrip?.Dispose();
            Serial?.Dispose();
            Udp?.Dispose();
            // Nmea has no IDisposable.

            GC.SuppressFinalize(this);
        }
    }
}
