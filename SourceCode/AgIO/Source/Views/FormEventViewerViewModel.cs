// [XPLAT] migrated from net48/WinForms FormEventViewer.cs + FormEventViewer.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using System.Text;
using AgOpenGPS.Core.ViewModels;
using AgLibrary.Logging;

namespace AgIO.Views
{
    /// <summary>
    /// MVVM view-model backing <c>FormEventViewer.axaml</c>, the AgIO Event Log Viewer
    /// dialog. It replaces the WinForms <c>FormEventViewer</c>, which read a log file via
    /// a <see cref="StreamReader"/> into a read-only <c>RichTextBox</c>
    /// (<c>rtbLogViewer</c>) and then appended the in-memory <see cref="Log.sbEvents"/>
    /// session buffer.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The only substantive change from the WinForms original is the data control:
    /// the read-only <c>RichTextBox rtbLogViewer</c> becomes the bound <see cref="LogText"/>
    /// string, rendered by a selectable / read-only text control in the view. The
    /// cross-platform <see cref="AgLibrary.Logging.Log"/> static API is reused unchanged.
    /// The WinForms <c>btnRefresh</c> and <c>btnExit</c> buttons map to
    /// <see cref="RefreshCommand"/> and <see cref="CloseCommand"/>; dialog dismissal is
    /// delegated to the hosting Avalonia <c>Window</c> through the <see cref="RequestClose"/>
    /// event (the former <c>btnExit_Click → Close()</c>), so the view-model owns no
    /// windowing concern.
    /// </remarks>
    public class FormEventViewerViewModel : ViewModel
    {
        // [XPLAT] Parity literals carried verbatim from FormEventViewer.cs so the rendered
        // log text is byte-identical to the WinForms original (four leading asterisks, five
        // trailing asterisks, and the two spaces after "->" are intentional).
        private const string SessionSeparator = " **** Current Session Below ***** \r\n\r\n";
        private const string FileReadErrorPrefix = "Catch ->  error loading logfile";

        // [XPLAT] The WinForms `string filename` field; the full log-file path is supplied
        // fully formed by the caller and assigned once here, so no path composition (and
        // therefore no hard-coded separator) is ever performed in this view-model.
        private readonly string _filename;

        private string _logText;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormEventViewerViewModel"/> class,
        /// mirroring the WinForms constructor <c>FormEventViewer(string _filename)</c>.
        /// </summary>
        /// <param name="filename">
        /// The full path of the log file to display. The value is supplied fully formed by
        /// the caller, so no path composition is performed here. A <c>null</c> or empty value
        /// is tolerated: the viewer then shows only the in-memory session events.
        /// </param>
        public FormEventViewerViewModel(string filename)
        {
            _filename = filename;

            // [XPLAT] btnRefresh -> RefreshCommand; btnExit -> CloseCommand (raises RequestClose).
            RefreshCommand = new RelayCommand(Refresh);
            CloseCommand = new RelayCommand(OnClose);

            // [XPLAT] The WinForms shell populated the viewer in FormEventViewer_Load; the
            // view-model populates it eagerly on construction so the bound control shows the
            // log content as soon as the dialog opens.
            Refresh();
        }

        /// <summary>
        /// Gets the full text shown in the viewer — the contents of the log file (when
        /// present) followed by the in-memory session events. Replaces the WinForms
        /// read-only <c>RichTextBox rtbLogViewer</c>. The private setter raises a change
        /// notification so the bound text control updates whenever <see cref="Refresh"/> runs.
        /// </summary>
        public string LogText
        {
            get => _logText;
            private set
            {
                _logText = value;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Gets the command that re-reads the log file and re-appends the current session
        /// events, refreshing <see cref="LogText"/>. Parity with the WinForms
        /// <c>btnRefresh_Click</c> handler.
        /// </summary>
        public RelayCommand RefreshCommand { get; }

        /// <summary>
        /// Gets the command that requests the dialog be dismissed by raising
        /// <see cref="RequestClose"/>. Parity with the WinForms <c>btnExit_Click → Close()</c>.
        /// </summary>
        public RelayCommand CloseCommand { get; }

        /// <summary>
        /// Raised when the operator activates <see cref="CloseCommand"/>. The hosting Avalonia
        /// <c>Window</c> subscribes to this event and closes itself, replacing the WinForms
        /// <c>Close()</c> call. Initialized to a no-op delegate so it is always safe to raise
        /// without a null check.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Reads the log file (when present and readable) and appends the in-memory session
        /// events, then publishes the combined text to <see cref="LogText"/>. Reproduces the
        /// behavior shared by the WinForms <c>FormEventViewer_Load</c> and
        /// <c>btnRefresh_Click</c> handlers.
        /// </summary>
        /// <remarks>
        /// [XPLAT] File access is wrapped in try/catch for graceful degradation: a read
        /// failure appends an inline error line (matching the original) instead of throwing,
        /// and the current-session events are still appended afterward. A missing or empty
        /// file name simply skips the file read — leaving only the session events — which is
        /// consistent with the application's defensive logging posture.
        /// </remarks>
        private void Refresh()
        {
            StringBuilder sb = new StringBuilder();

            try
            {
                // [XPLAT] Guard before opening: a missing log file (common on first run) is
                // not an error — it just means there is nothing on disk yet to display.
                if (!string.IsNullOrEmpty(_filename) && File.Exists(_filename))
                {
                    // [XPLAT] Preserve the original StreamReader read approach.
                    using (StreamReader reader = new StreamReader(_filename))
                    {
                        sb.Append(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception ex)
            {
                // [XPLAT] Graceful-degradation parity: the WinForms handler appended this same
                // error text to the viewer rather than surfacing the exception to the operator.
                sb.Append(FileReadErrorPrefix);
                sb.Append(ex.ToString());
            }

            // [XPLAT] Parity: the original always appended the session separator and the
            // in-memory Log.sbEvents buffer after the file contents (even on read failure).
            sb.Append(SessionSeparator);
            sb.Append(Log.sbEvents.ToString());

            LogText = sb.ToString();
        }

        /// <summary>
        /// Raises <see cref="RequestClose"/> so the hosting window dismisses the dialog.
        /// Parity with the WinForms <c>btnExit_Click</c> handler.
        /// </summary>
        private void OnClose()
        {
            RequestClose();
        }
    }
}
