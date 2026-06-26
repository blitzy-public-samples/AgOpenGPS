// [XPLAT] migrated from net48/WinForms FormUDPMonitor.cs + FormUDPMonitor.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Threading;
using AgIO.Services;
using AgLibrary.Logging;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing <c>FormUDPMonitorView.axaml</c>, the AgIO <b>UDP Monitor</b> dialog — a live,
    /// read-only mirror of the UDP loopback traffic flowing through AgIO. It replaces the WinForms
    /// <c>FormUDPMonitor</c>, which held a direct <c>FormLoop mf</c> back-reference, switched
    /// <c>mf.isUDPMonitorOn</c> on at load, ran a 333&#160;ms UI timer that drained
    /// <c>mf.logUDPSentence</c> into a read-only multiline text box, toggled the NMEA / NTRIP capture
    /// flags, saved the captured text to <c>zAgIO_UDP_log.txt</c>, and opened the PGN reference window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>God-object removal.</b> The WinForms <c>FormLoop mf</c> back-reference is replaced by a
    /// constructor-injected <see cref="CommCoordinatorService"/>. All monitor state
    /// (<c>isUDPMonitorOn</c>, <c>logUDPSentence</c>, <c>isGPSLogOn</c>, <c>isNTRIPLogOn</c>) lives on the
    /// migrated <see cref="UdpLoopbackService"/> reached through <see cref="CommCoordinatorService.Udp"/>;
    /// this view-model only reads/writes those members. It performs <b>no</b> byte-level UDP/PGN work — the
    /// frozen wire protocol (header <c>0x80 0x81 0x7F</c>, additive CRC, loopback ports 17777/15555) stays
    /// entirely inside <see cref="UdpLoopbackService"/>. This dialog is a pure traffic mirror.
    /// </para>
    /// <para>
    /// <b>Drain pattern (frozen).</b> The WinForms timer tick did
    /// <c>textBoxRcv.AppendText(mf.logUDPSentence.ToString()); mf.logUDPSentence.Clear();</c>. That exact
    /// drain is preserved on an <see cref="DispatcherTimer"/> tick (the tick runs on the UI thread, so the
    /// append needs no extra marshalling). The shared <see cref="System.Text.StringBuilder"/> is owned by
    /// the UDP service and appended from the socket-callback thread; this view-model introduces no new
    /// synchronization so the original timing/behaviour is unchanged.
    /// </para>
    /// <para>
    /// <b>Host-delegated concerns.</b> The WinForms <c>mf.TimedMessageBox(...)</c> becomes the
    /// <see cref="RequestTimedMessage"/> event (the view opens <c>FormTimedMessage</c>); the
    /// <c>lblPGNGuide</c> click that did <c>new FormPGN().Show(this)</c> becomes the
    /// <see cref="RequestOpenPgn"/> event (the view opens the Avalonia <c>FormPGN</c>); and the Cancel
    /// button / <c>FormClosing</c> teardown becomes <see cref="RequestClose"/> plus <see cref="Cleanup"/>.
    /// The view-model owns no windowing concern.
    /// </para>
    /// <para>
    /// <b>Colours.</b> The WinForms <c>BackColor</c> toggles (Color.LightGreen when on / Color.Salmon when
    /// off) are surfaced as bound <see cref="IBrush"/> properties (<see cref="LogButtonBrush"/>,
    /// <see cref="NmeaLogButtonBrush"/>, <see cref="NtripLogButtonBrush"/>). Avalonia's
    /// <c>Brushes.LightGreen</c>/<c>Brushes.Salmon</c> are byte-identical to the System.Drawing colours of
    /// the original (#90EE90 / #FA8072), so the rendered toggle colour is at parity.
    /// </para>
    /// </remarks>
    public class FormUDPMonitorViewModel : ViewModel
    {
        // [XPLAT] The WinForms designer set timer1.Interval = 333 (ms). Frozen at the source value so the
        // monitor refreshes at the identical cadence.
        private const int TimerIntervalMilliseconds = 333;

        // [XPLAT] The WinForms log file name, kept verbatim (Path.Combine composes the full path below so
        // there is never a hard-coded directory separator — AAP §0.6.5 case/separator safety).
        private const string LogFileName = "zAgIO_UDP_log.txt";

        // [XPLAT] Soft cap on the displayed buffer. The WinForms TextBox grew without bound until the
        // operator pressed Clear; on a long-running session that is a slow memory leak (and O(n^2) string
        // growth). We keep the most-recent characters so the live tail behaviour is preserved while the
        // footprint stays bounded. The cap is large enough (~1 MB of text) that normal sessions never trim.
        private const int MaxReceivedBufferLength = 1_000_000;

        // [XPLAT] Replaces the WinForms `FormLoop mf` back-reference. The comms aggregate owns the migrated
        // UdpLoopbackService that carries every monitor flag and the log buffer this dialog mirrors.
        private readonly CommCoordinatorService _comm;

        // [XPLAT] Replaces the WinForms System.Windows.Forms.Timer (timer1). Avalonia's DispatcherTimer
        // ticks on the UI thread, matching the original UI-timer semantics.
        private readonly DispatcherTimer _timer;

        // Backing fields (nullable reference types are disabled project-wide, so no '?' annotations).
        private string _receivedText = string.Empty;
        private bool _logOn;
        private bool _cleanedUp;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormUDPMonitorViewModel"/> class for the supplied
        /// comms aggregate, reproducing the WinForms <c>FormUDPMonitor</c> constructor plus its
        /// <c>FormUDp_Load</c> handler: it switches monitor capture on and starts the drain timer.
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate whose <see cref="CommCoordinatorService.Udp"/> transport carries the
        /// monitor flags and the captured-sentence buffer. Must not be <see langword="null"/> — the dialog
        /// cannot function without it (the WinForms original would have null-faulted on first use).
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="comm"/> is <see langword="null"/>.</exception>
        public FormUDPMonitorViewModel(CommCoordinatorService comm)
        {
            if (comm == null)
            {
                throw new ArgumentNullException(nameof(comm));
            }

            _comm = comm;

            // Commands (bound to the dialog's buttons / label). Concrete RelayCommand type matches the
            // sibling AgIO view-models.
            ToggleLogCommand = new RelayCommand(ToggleLog);
            ToggleNmeaLogCommand = new RelayCommand(ToggleNmeaLog);
            ToggleNtripLogCommand = new RelayCommand(ToggleNtripLog);
            SaveFileCommand = new RelayCommand(SaveLogFile);
            ClearCommand = new RelayCommand(ClearReceived);
            OpenPgnCommand = new RelayCommand(OnOpenPgn);
            CloseCommand = new RelayCommand(OnClose);

            // FormUDp_Load parity: switch monitor capture on, then start the drain timer. The backing field
            // is set directly (not via the side-effecting IsLogOn setter) so construction does no extra work.
            _logOn = true;
            _comm.Udp.isUDPMonitorOn = true;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimerIntervalMilliseconds) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        /// <summary>
        /// Gets the text shown in the monitor — the running mirror of the drained UDP sentences. Replaces
        /// the WinForms read-only multiline <c>textBoxRcv</c>; the view binds a read-only text control to it.
        /// The private setter raises a change notification so the bound control refreshes on every drain.
        /// </summary>
        public string ReceivedText
        {
            get { return _receivedText; }
            private set
            {
                _receivedText = value;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether monitor capture is active (the former <c>btnLog</c>
        /// toggle / <c>logOn</c> flag). Setting it reproduces <c>btnLog_Click</c> exactly: it switches
        /// <see cref="UdpLoopbackService.isUDPMonitorOn"/> and starts/stops the drain timer, and drives
        /// <see cref="LogButtonBrush"/> (LightGreen when on, Salmon when off). Defaults to
        /// <see langword="true"/> (the dialog opens capturing, matching the WinForms load state and the
        /// designer's green <c>btnLog</c>).
        /// </summary>
        public bool IsLogOn
        {
            get { return _logOn; }
            set
            {
                if (value == _logOn)
                {
                    return;
                }

                _logOn = value;

                // btnLog_Click parity: on -> capture on + timer running; off -> capture off + timer stopped.
                if (_logOn)
                {
                    _comm.Udp.isUDPMonitorOn = true;
                    _timer.Start();
                }
                else
                {
                    _comm.Udp.isUDPMonitorOn = false;
                    _timer.Stop();
                }

                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(LogButtonBrush));
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether NMEA-sentence logging is active. Two-way bound to
        /// <see cref="UdpLoopbackService.isGPSLogOn"/> (the former <c>btnLogNMEA</c> toggle). Drives
        /// <see cref="NmeaLogButtonBrush"/>.
        /// </summary>
        public bool IsLogNmeaOn
        {
            get { return _comm.Udp.isGPSLogOn; }
            set
            {
                if (value == _comm.Udp.isGPSLogOn)
                {
                    return;
                }

                _comm.Udp.isGPSLogOn = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(NmeaLogButtonBrush));
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether NTRIP/RTCM logging is active. Two-way bound to
        /// <see cref="UdpLoopbackService.isNTRIPLogOn"/> (the former <c>btnLogNTRIP</c> toggle). Drives
        /// <see cref="NtripLogButtonBrush"/>.
        /// </summary>
        public bool IsLogNtripOn
        {
            get { return _comm.Udp.isNTRIPLogOn; }
            set
            {
                if (value == _comm.Udp.isNTRIPLogOn)
                {
                    return;
                }

                _comm.Udp.isNTRIPLogOn = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(NtripLogButtonBrush));
            }
        }

        /// <summary>
        /// Gets the brush for the capture-toggle button: LightGreen while capturing, Salmon while stopped.
        /// Mirrors the WinForms <c>btnLog.BackColor</c> assignment (Color.LightGreen / Color.Salmon).
        /// </summary>
        public IBrush LogButtonBrush
        {
            get { return _logOn ? Brushes.LightGreen : Brushes.Salmon; }
        }

        /// <summary>
        /// Gets the brush for the NMEA-log button: LightGreen when on, Salmon when off. Mirrors the
        /// WinForms <c>btnLogNMEA.BackColor</c> assignment.
        /// </summary>
        public IBrush NmeaLogButtonBrush
        {
            get { return IsLogNmeaOn ? Brushes.LightGreen : Brushes.Salmon; }
        }

        /// <summary>
        /// Gets the brush for the NTRIP-log button: LightGreen when on, Salmon when off. Mirrors the
        /// WinForms <c>btnLogNTRIP.BackColor</c> assignment.
        /// </summary>
        public IBrush NtripLogButtonBrush
        {
            get { return IsLogNtripOn ? Brushes.LightGreen : Brushes.Salmon; }
        }

        /// <summary>Gets the command that toggles monitor capture (the former <c>btnLog</c>).</summary>
        public RelayCommand ToggleLogCommand { get; }

        /// <summary>Gets the command that toggles NMEA logging (the former <c>btnLogNMEA</c>).</summary>
        public RelayCommand ToggleNmeaLogCommand { get; }

        /// <summary>Gets the command that toggles NTRIP logging (the former <c>btnLogNTRIP</c>).</summary>
        public RelayCommand ToggleNtripLogCommand { get; }

        /// <summary>Gets the command that saves the captured text to disk (the former <c>btnFileSave</c>).</summary>
        public RelayCommand SaveFileCommand { get; }

        /// <summary>Gets the command that clears the displayed buffer (the former <c>btnClear</c>).</summary>
        public RelayCommand ClearCommand { get; }

        /// <summary>
        /// Gets the command that asks the host to open the PGN reference window (the former
        /// <c>lblPGNGuide_Click → new FormPGN().Show(this)</c>) by raising <see cref="RequestOpenPgn"/>.
        /// </summary>
        public RelayCommand OpenPgnCommand { get; }

        /// <summary>
        /// Gets the command that closes the dialog (the former <c>btnSerialCancel</c>): it tears the
        /// monitor down via <see cref="Cleanup"/> and raises <see cref="RequestClose"/>.
        /// </summary>
        public RelayCommand CloseCommand { get; }

        /// <summary>
        /// Raised to ask the view to show a transient, self-dismissing notification — the MVVM replacement
        /// for the WinForms <c>FormLoop.TimedMessageBox(milliseconds, title, message)</c>. The arguments are
        /// the display duration in milliseconds, the title, and the message body. Initialized to a no-op
        /// delegate so it is always safe to raise.
        /// </summary>
        public event Action<int, string, string> RequestTimedMessage = delegate { };

        /// <summary>
        /// Raised when the operator activates <see cref="OpenPgnCommand"/>. The hosting view opens the
        /// Avalonia <c>FormPGN</c> reference window (the former <c>new FormPGN().Show(this)</c>). Initialized
        /// to a no-op delegate so it is always safe to raise.
        /// </summary>
        public event Action RequestOpenPgn = delegate { };

        /// <summary>
        /// Raised when <see cref="CloseCommand"/> is invoked. The hosting Avalonia <c>Window</c> subscribes
        /// and closes itself, replacing the WinForms <c>Close()</c>. Initialized to a no-op delegate so it is
        /// always safe to raise.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Stops the monitor and releases its capture flag — the parity counterpart of the WinForms
        /// <c>FormUDPMonitor_FormClosing</c> handler (which set <c>mf.isUDPMonitorOn = false</c>). The host
        /// view calls this when the dialog closes (whether via the Cancel button or the window chrome). It is
        /// idempotent, so calling it from both <see cref="OnClose"/> and the view's close handler is safe.
        /// </summary>
        public void Cleanup()
        {
            if (_cleanedUp)
            {
                return;
            }

            _cleanedUp = true;

            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _comm.Udp.isUDPMonitorOn = false;
        }

        // [XPLAT] timer1_Tick parity: drain the UDP service's captured-sentence buffer into the displayed
        // text. The StringBuilder is owned by the UDP service and appended on the socket-callback thread; we
        // read-then-clear exactly as the WinForms tick did (no new locking, so the frozen timing holds).
        private void OnTimerTick(object sender, EventArgs e)
        {
            string incoming = _comm.Udp.logUDPSentence.ToString();
            _comm.Udp.logUDPSentence.Clear();

            if (incoming.Length == 0)
            {
                return;
            }

            string combined = _receivedText + incoming;

            // Soft cap (see MaxReceivedBufferLength): keep the most-recent characters so a long session
            // cannot grow the buffer without bound. The original never capped; normal traffic stays well
            // under the limit, so this is behaviour-preserving for any realistic session.
            if (combined.Length > MaxReceivedBufferLength)
            {
                combined = combined.Substring(combined.Length - MaxReceivedBufferLength);
            }

            ReceivedText = combined;
        }

        // btnLog_Click parity: flip the capture flag (the IsLogOn setter applies the timer/flag side effects).
        private void ToggleLog()
        {
            IsLogOn = !IsLogOn;
        }

        // btnLogNMEA_Click parity: flip the NMEA-log flag on the UDP service.
        private void ToggleNmeaLog()
        {
            IsLogNmeaOn = !IsLogNmeaOn;
        }

        // btnLogNTRIP_Click parity: flip the NTRIP-log flag on the UDP service.
        private void ToggleNtripLog()
        {
            IsLogNtripOn = !IsLogNtripOn;
        }

        // btnClear_Click parity: empty the displayed buffer (textBoxRcv.Text = "").
        private void ClearReceived()
        {
            ReceivedText = string.Empty;
        }

        // btnFileSave_Click parity: write the captured text to zAgIO_UDP_log.txt, then ask the view to show
        // the "File Saved" timed message. The original wrote a bare relative path (current working
        // directory); Path.Combine with the current directory preserves that location cross-platform without
        // a hard-coded separator. File access is guarded so a read-only/locked directory degrades to a
        // logged, surfaced failure instead of crashing the dialog (AAP graceful-degradation rule).
        private void SaveLogFile()
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), LogFileName);
                using (StreamWriter writer = new StreamWriter(path, false))
                {
                    writer.Write(ReceivedText);
                }

                RequestTimedMessage(2000, "File Saved", "To zAgIO_UDP_Log.Txt");
            }
            catch (Exception ex)
            {
                Log.EventWriter("Catch -> FormUDPMonitor SaveLogFile: " + ex.Message);
                RequestTimedMessage(2000, "File Save Failed", ex.Message);
            }
        }

        // lblPGNGuide_Click parity: ask the host to open the PGN reference window.
        private void OnOpenPgn()
        {
            RequestOpenPgn();
        }

        // btnSerialCancel_Click parity: stop the monitor, then ask the host to close the dialog.
        private void OnClose()
        {
            Cleanup();
            RequestClose();
        }
    }
}
