// [XPLAT] migrated from net48/WinForms FormEventViewer.cs + FormEventViewer.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] AgIO "Event Log Viewer" dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormEventViewer : Form</c> (<c>Forms/FormEventViewer.cs</c>
    /// + <c>Forms/FormEventViewer.Designer.cs</c>). This is the code-behind half of
    /// <c>FormEventViewer.axaml</c>; together they form a single view whose
    /// <see cref="StyledElement.DataContext"/> is a <see cref="FormEventViewerViewModel"/> that
    /// reads the AgIO event-log file and appends the in-memory <c>Log.sbEvents</c> session buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This code-behind is intentionally a thin constructor/close bridge — all behaviour was moved
    /// into <see cref="FormEventViewerViewModel"/>:
    /// </para>
    /// <list type="bullet">
    ///   <item>The WinForms <c>FormEventViewer_Load</c> file read + " **** Current Session Below ***** "
    ///   session append now populates the bound <see cref="FormEventViewerViewModel.LogText"/>.</item>
    ///   <item>The WinForms <c>btnRefresh_Click</c> (clear + re-read) maps to
    ///   <see cref="FormEventViewerViewModel.RefreshCommand"/>, bound in <c>FormEventViewer.axaml</c>.</item>
    ///   <item>The WinForms <c>btnExit_Click → Close()</c> maps to
    ///   <see cref="FormEventViewerViewModel.CloseCommand"/>, which raises
    ///   <see cref="FormEventViewerViewModel.RequestClose"/>; this window subscribes to that event and
    ///   calls <see cref="Window.Close()"/> — the one window-level concern the view-model cannot own.</item>
    /// </list>
    /// <para>
    /// The host (the AgIO <c>MainWindow</c>) supplies the fully-formed event-log file path and opens the
    /// dialog as <c>await new FormEventViewer(logPath).ShowDialog(ownerWindow)</c>, mirroring the WinForms
    /// <c>FormEventViewer(string _filename)</c> + modal display. Because the view-model is created here and
    /// is never handed to an external owner, the <c>RequestClose</c> handler forms a self-contained
    /// window/view-model pair that is collected together once the host releases the dialog — so no explicit
    /// unsubscribe is required.
    /// </para>
    /// </remarks>
    public partial class FormEventViewer : Window
    {
        // [XPLAT] Replaces the WinForms read-only RichTextBox + StreamReader plumbing: the view-model
        // owns the log text, the Refresh re-read, and the close request. Held so the view stays bound
        // to a single instance for the dialog's lifetime.
        private readonly FormEventViewerViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error). It mirrors the convention
        /// used by the sibling AgIO Avalonia views (<c>FormPGN</c>, <c>FormYes</c>,
        /// <c>FormRadioChannel</c>). Application code constructs the dialog through
        /// <see cref="FormEventViewer(string)"/>, which supplies the log-file path and view-model.
        /// </summary>
        public FormEventViewer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the Event Log Viewer dialog for the supplied log-file path and binds it to a freshly
        /// built <see cref="FormEventViewerViewModel"/> — the cross-platform replacement for the WinForms
        /// <c>FormEventViewer(string _filename)</c> constructor.
        /// </summary>
        /// <param name="filename">
        /// The full path of the AgIO event-log file to display. The value is supplied fully formed by the
        /// caller (the host <c>MainWindow</c>), so no path composition is performed here; the view-model
        /// tolerates a <c>null</c> or empty value by showing only the in-memory session events.
        /// </param>
        public FormEventViewer(string filename)
        {
            InitializeComponent();

            // [XPLAT] Construct the view-model with the caller-supplied log path and bind it. The
            // view-model eagerly loads the log file + current-session events on construction so the
            // bound text control shows content as soon as the dialog opens.
            _vm = new FormEventViewerViewModel(filename);
            DataContext = _vm;

            // [XPLAT] btnExit_Click → Close(): CloseCommand raises RequestClose; the window performs the
            // actual dismissal. The view-model is owned by this window and not exposed elsewhere, so this
            // subscription does not outlive the dialog.
            _vm.RequestClose += () => Close();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
