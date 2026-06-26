// [XPLAT] migrated from net48/WinForms (Forms/FormEventViewer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AgLibrary.Logging;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the event-log viewer dialog. Faithful Avalonia reimplementation of the
    /// WinForms <c>FormEventViewer</c> (Forms/FormEventViewer.cs): it shows the on-disk log file followed
    /// by the current in-memory session events (<see cref="Log.sbEvents"/>), refreshes the tail once a
    /// second, and offers a manual full reload plus an exit button.
    /// </summary>
    /// <remarks>
    /// This is an imperative dialog (no view-model). All behaviour is self-contained: it depends only on
    /// Avalonia, <see cref="System.IO"/> and <c>AgLibrary.Logging.Log</c> (a referenced cross-platform
    /// project) — never on the former <c>FormGPS</c> (<c>mf</c>) god-object. The WinForms
    /// <c>System.Windows.Forms.Timer</c> is replaced by an Avalonia <see cref="DispatcherTimer"/>, and the
    /// <c>RichTextBox.ScrollToCaret()</c> scroll-to-bottom is reproduced by moving the read-only
    /// <see cref="TextBox.CaretIndex"/> to the end of the text.
    /// </remarks>
    public partial class FormEventViewerView : Window
    {
        // [XPLAT] verbatim from WinForms FormEventViewer: the log file to display and the running
        // read offsets used by the incremental tail-refresh.
        private readonly string _filename;
        private long _filePosition;
        private int _sessionLength;
        private DispatcherTimer _refreshTimer;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia tooling/source generator. It
        /// delegates to the real <see cref="FormEventViewerView(string)"/> entry point with an empty
        /// filename; <see cref="LoadLog"/> then shows only the current session events (the on-disk file
        /// read is skipped for an empty path). The owning shell uses the string overload with the active
        /// log path once it is wired.
        /// </summary>
        public FormEventViewerView() : this(string.Empty)
        {
        }

        /// <summary>
        /// [XPLAT] Primary constructor mirroring the WinForms <c>FormEventViewer(string filename)</c>.
        /// </summary>
        /// <param name="filename">Full path of the log file to display; may be empty.</param>
        public FormEventViewerView(string filename)
        {
            InitializeComponent();
            _filename = filename ?? string.Empty;
        }

        /// <summary>
        /// [XPLAT] WinForms FormEventViewer_Load parity: full load on open, then start the 1 s tail timer.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            LoadLog();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (s, ev) => AppendNewContent();
            _refreshTimer.Start();
        }

        /// <summary>
        /// [XPLAT] WinForms OnFormClosed parity: stop and release the refresh timer.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
            base.OnClosed(e);
        }

        /// <summary>
        /// [XPLAT] btnRefresh_Click -> full reload.
        /// </summary>
        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            LoadLog();
        }

        /// <summary>
        /// [XPLAT] btnExit_Click -> Close().
        /// </summary>
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Full reload — used on open and manual refresh (WinForms LoadLog()). Reads the whole
        /// file (guarding an empty path on the parameterless entry point), appends the current session
        /// banner and events, and scrolls to the bottom.
        /// </summary>
        private void LoadLog()
        {
            string fileContent = "";

            if (!string.IsNullOrEmpty(_filename))
            {
                try
                {
                    fileContent = File.ReadAllText(_filename);
                    _filePosition = new FileInfo(_filename).Length;
                }
                catch (Exception ex)
                {
                    fileContent = "Catch -> error loading logfile: " + ex.Message;
                    _filePosition = 0;
                }
            }
            else
            {
                _filePosition = 0;
            }

            _sessionLength = Log.sbEvents.Length;

            rtbLogViewer.Text = fileContent
                + "\r\n **** Current Session Below *****\r\n\r\n"
                + Log.sbEvents.ToString();
            ScrollToBottom();
        }

        /// <summary>
        /// [XPLAT] Appends only new content since the last read — called by the timer (WinForms
        /// AppendNewContent()). Tails the file from the saved offset and appends any new session events.
        /// </summary>
        private void AppendNewContent()
        {
            bool hasNew = false;

            if (!string.IsNullOrEmpty(_filename))
            {
                try
                {
                    long currentSize = new FileInfo(_filename).Length;
                    if (currentSize > _filePosition)
                    {
                        using (var fs = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fs.Seek(_filePosition, SeekOrigin.Begin);
                            using (var sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                string newContent = sr.ReadToEnd();
                                if (!string.IsNullOrEmpty(newContent))
                                {
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
                    // [XPLAT] verbatim from WinForms: tail-read failures are swallowed (e.g. transient
                    // file locks); the next tick retries.
                }
            }

            int currentSessionLength = Log.sbEvents.Length;
            if (currentSessionLength > _sessionLength)
            {
                rtbLogViewer.Text += Log.sbEvents.ToString().Substring(_sessionLength);
                _sessionLength = currentSessionLength;
                hasNew = true;
            }

            if (hasNew) ScrollToBottom();
        }

        /// <summary>
        /// [XPLAT] Avalonia equivalent of WinForms SelectionStart = Text.Length; ScrollToCaret():
        /// move the caret of the read-only log to the end so the newest content is visible.
        /// </summary>
        private void ScrollToBottom()
        {
            rtbLogViewer.CaretIndex = rtbLogViewer.Text?.Length ?? 0;
        }
    }
}
