// [XPLAT] migrated from net48/WinForms (Forms/FormEventViewer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using System.Text;
using AgLibrary.Logging;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the event-log viewer dialog. Faithful Avalonia reimplementation of the
    /// WinForms <c>FormEventViewer</c> (Forms/FormEventViewer.cs + FormEventViewer.Designer.cs): a
    /// scrolling, read-only viewer that shows the on-disk log file followed by the current in-memory
    /// session events (<see cref="Log.sbEvents"/>), tails the file once a second, and offers a manual
    /// full reload (<c>Refresh</c>) plus an <c>Exit</c> button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an imperative dialog (parity with the WinForms code-behind): there is no view-model,
    /// no <c>DataContext</c> and no binding. The partner markup (<c>FormEventViewerView.axaml</c>) owns
    /// the three named controls — <c>rtbLogViewer</c>, <c>btnRefresh</c>, <c>btnExit</c> — and wires the
    /// button <c>Click</c> events to <see cref="OnRefreshClick"/> / <see cref="OnExitClick"/>.
    /// </para>
    /// <para>
    /// Cross-platform mapping of the WinForms primitives:
    /// the <c>System.Windows.Forms.Timer</c> becomes an Avalonia <see cref="DispatcherTimer"/>
    /// (UI-thread, 1000&#160;ms — verbatim with the original); the <c>RichTextBox</c> log surface becomes
    /// a read-only multiline <see cref="TextBox"/> whose <see cref="TextBox.Text"/> is assigned directly
    /// (replacing <c>RichTextBox.AppendText</c>); and the WinForms <c>SelectionStart = Text.Length;
    /// ScrollToCaret()</c> auto-scroll becomes <see cref="TextBox.CaretIndex"/> moved to the end of the
    /// text (see <see cref="ScrollToBottom"/>). The dialog depends only on Avalonia,
    /// <see cref="System.IO"/> and <c>AgLibrary.Logging.Log</c> — never on the former <c>FormGPS</c>
    /// (<c>mf</c>) god-object.
    /// </para>
    /// </remarks>
    public partial class FormEventViewerView : Window
    {
        // [XPLAT] verbatim from the WinForms FormEventViewer: the log file to display plus the running
        // read offsets used by the incremental tail-refresh (file byte offset + session-buffer length).
        private readonly string _filename = string.Empty;
        private long _filePosition;
        private int _sessionLength;

        // [XPLAT] Replaces the WinForms System.Windows.Forms.Timer with the cross-platform UI-thread
        // DispatcherTimer (Avalonia.Threading); fires AppendNewContent() once a second.
        private DispatcherTimer _refreshTimer;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia compiled-XAML loader / design
        /// preview (keeps the avares:// resource reachable and avoids AVLN3001, which Release treats as
        /// an error). It only calls <c>InitializeComponent()</c> — matching the sibling dialog
        /// convention — so the design-time previewer incurs no file I/O and starts no refresh timer.
        /// Production code constructs the dialog through <see cref="FormEventViewerView(string)"/>.
        /// </summary>
        public FormEventViewerView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Primary constructor mirroring the WinForms <c>FormEventViewer(string filename)</c>.
        /// Following the migration's dialog convention it chains the parameterless constructor (which
        /// runs <c>InitializeComponent()</c>), stores the log-file path, performs the initial full
        /// <see cref="LoadLog"/>, and starts the 1000&#160;ms tail-refresh <see cref="DispatcherTimer"/>
        /// — the same work the WinForms form performed across its constructor and <c>Load</c> handler.
        /// </summary>
        /// <param name="filename">
        /// Full path of the log file to display (e.g. the AgLibrary log file supplied by the caller /
        /// composition root). May be <see langword="null"/> or empty, in which case only the current
        /// in-memory session events are shown (the on-disk read is skipped).
        /// </param>
        public FormEventViewerView(string filename)
            : this()
        {
            // [XPLAT] WinForms: _filename = filename;  (null-hardened for the path-guarded reads below).
            _filename = filename ?? string.Empty;

            // [XPLAT] WinForms FormEventViewer_Load: initial full load, then start the 1 s tail timer.
            LoadLog();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>OnFormClosed</c>: stop, detach and release the refresh
        /// timer so it can never tick against a closed window.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= OnRefreshTimerTick;
                _refreshTimer = null;
            }

            base.OnClosed(e);
        }

        // [XPLAT] DispatcherTimer.Tick handler (named so it can be cleanly detached in OnClosed) —
        // parity with the WinForms "_refreshTimer.Tick += (s, ev) => AppendNewContent();".
        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            AppendNewContent();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>btnRefresh_Click</c>: perform a full reload. Wired from
        /// the markup via <c>Click="OnRefreshClick"</c>.
        /// </summary>
        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            LoadLog();
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>btnExit_Click</c>: close the dialog. Wired from the
        /// markup via <c>Click="OnExitClick"</c>.
        /// </summary>
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Full reload — used on open and on manual refresh (WinForms <c>LoadLog()</c>). Reads
        /// the entire log file (guarded for a missing/empty path), appends the "current session" banner
        /// and the live <see cref="Log.sbEvents"/> buffer, records the file/session read offsets, and
        /// scrolls to the bottom.
        /// </summary>
        private void LoadLog()
        {
            string fileContent = "";

            // [XPLAT] guard the file read for a missing/empty path: cross-platform, the log file may not
            // exist yet. WinForms always read _filename; here an empty path simply shows the session.
            if (!string.IsNullOrEmpty(_filename))
            {
                try
                {
                    // [XPLAT] WinForms: File.ReadAllText(_filename); _filePosition = FileInfo(...).Length;
                    fileContent = File.ReadAllText(_filename);
                    _filePosition = new FileInfo(_filename).Length;
                }
                catch (Exception ex)
                {
                    // [XPLAT] verbatim from WinForms: surface the read failure inline and reset the offset.
                    fileContent = "Catch -> error loading logfile: " + ex.Message;
                    _filePosition = 0;
                }
            }
            else
            {
                _filePosition = 0;
            }

            // [XPLAT] WinForms: _sessionLength = Log.sbEvents.Length;
            _sessionLength = Log.sbEvents.Length;

            // [XPLAT] WinForms: rtbLogViewer.Text = fileContent + banner + Log.sbEvents.ToString();
            // The WinForms SuspendLayout()/ResumeLayout() pair has no Avalonia equivalent and is not
            // needed — Avalonia batches layout, so the single Text assignment below is sufficient.
            rtbLogViewer.Text = fileContent
                + "\r\n **** Current Session Below *****\r\n\r\n"
                + Log.sbEvents.ToString();

            ScrollToBottom();
        }

        /// <summary>
        /// [XPLAT] Incremental tail-refresh — appends only the content written since the last read
        /// (WinForms <c>AppendNewContent()</c>, invoked by the timer). It seeks to the saved byte offset,
        /// reads the appended bytes, then appends any new in-memory session events, and auto-scrolls when
        /// anything was added.
        /// </summary>
        private void AppendNewContent()
        {
            bool hasNew = false;

            // [XPLAT] guard the file read for a missing/empty path.
            if (!string.IsNullOrEmpty(_filename))
            {
                try
                {
                    long currentSize = new FileInfo(_filename).Length;
                    if (currentSize > _filePosition)
                    {
                        // [XPLAT] WinForms: FileStream(Open, Read, ReadWrite) + Seek(_filePosition) +
                        // StreamReader(UTF8).ReadToEnd() — read only the bytes appended since last time.
                        using (var fs = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fs.Seek(_filePosition, SeekOrigin.Begin);
                            using (var sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                string newContent = sr.ReadToEnd();
                                if (!string.IsNullOrEmpty(newContent))
                                {
                                    // [XPLAT] Avalonia TextBox has no AppendText; concatenating onto
                                    // Text is the equivalent (a null Text concatenates as empty).
                                    rtbLogViewer.Text += newContent;
                                    hasNew = true;
                                }
                            }

                            _filePosition = currentSize;
                        }
                    }
                }
                catch
                {
                    // [XPLAT] verbatim from WinForms: tail-read failures are swallowed (e.g. a transient
                    // file lock from another writer); the next tick retries from the same offset.
                }
            }

            // [XPLAT] WinForms: append any session events added since the last read.
            int currentSessionLength = Log.sbEvents.Length;
            if (currentSessionLength > _sessionLength)
            {
                rtbLogViewer.Text += Log.sbEvents.ToString().Substring(_sessionLength);
                _sessionLength = currentSessionLength;
                hasNew = true;
            }

            if (hasNew)
            {
                ScrollToBottom();
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform equivalent of the WinForms <c>SelectionStart = Text.Length;
        /// ScrollToCaret()</c>: move the read-only log's caret to the end of the text so the newest
        /// content is visible. The caret is set immediately (correct once the control is realized — i.e.
        /// for the per-tick and Refresh paths) and re-applied via a background-priority dispatcher post
        /// so the initial open (where the control is not yet laid out when the constructor's first
        /// <see cref="LoadLog"/> runs) still reliably brings the bottom of the log into view.
        /// </summary>
        private void ScrollToBottom()
        {
            rtbLogViewer.CaretIndex = rtbLogViewer.Text?.Length ?? 0;

            // [XPLAT] "bring into view": re-apply the caret after layout so the TextBox's embedded
            // ScrollViewer scrolls to the end even on first paint (its text presenter is not realized
            // when the constructor's initial LoadLog() runs, so the immediate set above cannot scroll
            // yet). Background priority runs after the open/layout/render passes.
            Dispatcher.UIThread.Post(
                () => rtbLogViewer.CaretIndex = rtbLogViewer.Text?.Length ?? 0,
                DispatcherPriority.Background);
        }
    }
}
