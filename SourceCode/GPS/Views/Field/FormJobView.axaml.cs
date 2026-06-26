// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Avalonia code-behind for the field / job launcher MENU — a 1:1 behavioural-parity port of
// the WinForms Forms/Field/FormJob.cs (+ FormJob.Designer.cs + FormJob.resx, 337 lines). The dialog
// is the operator's entry point to a field session: start a New field, Resume / Open the previous
// one, Drive-In to a nearby field, import From KML / Existing / ISO-XML, Close the current field,
// activate Easy Drive, or open the AgShare download / bulk-upload sub-dialogs.
//
// Cross-platform conversions (each tagged // [XPLAT] at its site):
//   * The FormGPS "mf" god-object is removed entirely (AAP §0.3.2): every collaborator the WinForms
//     form reached through mf — the application model (current lat/lon, metric flag), the job/easy-
//     drive state, the fields directory, the save / open-field callbacks, the AgShare snapshot and
//     close-topmosts actions, the easy-drive / cancel-menu flag setters, the file-picker path
//     accessor, the AgShare client and the owner window — is injected discretely through the parity
//     constructor and stored in private readonly fields. There is NO "mf", NO new interface, NO GL.
//   * The WinForms multi-state DialogResult (the caller switched on Yes / OK / No / Retry / Abort /
//     Ignore / Cancel) becomes the public FormJobResult enum; every handler closes with
//     Close(FormJobResult.X) and the Avalonia caller (MainView) consumes it via
//     ShowDialog<FormJobResult>(...). The exact DialogResult -> FormJobResult mapping is documented
//     on the enum below so the MainView agent can port the FormGPS job-menu switch faithfully.
//   * FormFilePicker / FormDrivePicker (WinForms Pickers) -> FormFilePickerView / FormDrivePickerView
//     (Views/Pickers): the WinForms pattern wrote the chosen path to mf.filePickerFileAndDirectory and
//     signalled DialogResult.Yes; the Avalonia views RETURN the chosen Field.txt path (or null when
//     cancelled) via ShowDialog<string>, so "result != null" is the cross-platform equivalent of
//     "DialogResult.Yes". The injected file-picker accessor is still populated for parity so any host
//     code that reads filePickerFileAndDirectory keeps working.
//   * FormAgShareDownloader / FormAgShareUploader -> FormAgShareDownloaderView /
//     FormAgShareUploaderView (this folder); FormDialog.Show -> FormDialogView.ShowAsync.
//   * The "AgShare disabled" branch hid the two AgShare buttons and shrank ClientSize by 75px; here it
//     hides the buttons AND collapses the panelAgShare row so SizeToContent recomputes a window 75px
//     shorter — the cross-platform equivalent of the ClientSize edit.
//   * .Visible -> .IsVisible; .Enabled -> .IsEnabled; Form.Load -> OnLoaded; Form.FormClosing ->
//     OnClosing. Every numeric format uses CultureInfo.InvariantCulture (§0.6.5) so a comma-decimal
//     locale on Linux/macOS can never drift the distance text or break parity.
//   * NO System.Windows.Forms / System.Drawing(GDI) / GMap / OpenTK imports. The job-menu placement
//     settings remain System.Drawing.Point / System.Drawing.Size (System.Drawing.Primitives — the
//     cross-platform BCL primitive type the Settings facade declares); they are fully qualified to
//     avoid colliding with Avalonia's Point / Size.
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Views.Pickers;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] The multi-state result of the field / job launcher menu, returned to the caller through
    /// <c>ShowDialog&lt;FormJobResult&gt;</c>. It replaces the WinForms <c>FormJob</c>'s use of
    /// <see cref="object"/> <c>DialogResult</c> as a multi-way signal that the FormGPS job-menu switch
    /// branched on.
    /// </summary>
    /// <remarks>
    /// [XPLAT] WinForms <c>DialogResult</c> → <see cref="FormJobResult"/> mapping (preserved 1:1 so the
    /// MainView agent can port the FormGPS job-menu switch faithfully):
    /// <list type="table">
    /// <listheader><term>WinForms DialogResult (button)</term><description>FormJobResult — notes</description></listheader>
    /// <item><term><c>Yes</c> (btnJobNew)</term><description><see cref="NewField"/> — new field flow</description></item>
    /// <item><term><c>OK</c> (btnJobResume)</term><description><see cref="Resume"/> — field already opened via FileOpenField("Resume") before close</description></item>
    /// <item><term><c>OK</c> (btnJobClose)</term><description><see cref="CloseField"/> — field saved before close</description></item>
    /// <item><term><c>OK</c> (btnEasyDrive)</term><description><see cref="EasyDrive"/> — isEasyDriveRequested set true</description></item>
    /// <item><term><c>No</c> (btnFromKML)</term><description><see cref="FromKml"/></description></item>
    /// <item><term><c>Retry</c> (btnFromExisting)</term><description><see cref="FromExisting"/></description></item>
    /// <item><term><c>Abort</c> (btnFromISOXML)</term><description><see cref="FromIsoXml"/></description></item>
    /// <item><term><c>Ignore</c> (btnJobAgShare / btnAgShareBulkUpload)</term><description><see cref="AgShareHandled"/> — sub-dialog already shown</description></item>
    /// <item><term>(btnJobOpen)</term><description><see cref="OpenPicker"/> — field opened directly via the file picker, close after</description></item>
    /// <item><term>(btnInField)</term><description><see cref="DriveIn"/> — field opened directly via the drive picker, close after</description></item>
    /// <item><term><c>Cancel</c> (btnDeleteAB)</term><description><see cref="None"/> — isCancelJobMenu set true; the WinForms designer's DialogResult.Cancel closed the form, so the menu closes returning the neutral result</description></item>
    /// </list>
    /// </remarks>
    public enum FormJobResult
    {
        /// <summary>No actionable result — the menu was cancelled / closed (WinForms <c>DialogResult.Cancel</c> / <c>None</c>). Default value, returned when the window is dismissed without a choice.</summary>
        None,

        /// <summary>Start a new field (WinForms btnJobNew → <c>DialogResult.Yes</c>).</summary>
        NewField,

        /// <summary>Resume the previous field — already opened via <c>FileOpenField("Resume")</c> (WinForms btnJobResume → <c>DialogResult.OK</c>).</summary>
        Resume,

        /// <summary>A field was opened through the file picker (WinForms btnJobOpen path).</summary>
        OpenPicker,

        /// <summary>A nearby field was opened through the drive-in picker (WinForms btnInField path).</summary>
        DriveIn,

        /// <summary>Import a field boundary from KML (WinForms btnFromKML → <c>DialogResult.No</c>).</summary>
        FromKml,

        /// <summary>Create a field from an existing field (WinForms btnFromExisting → <c>DialogResult.Retry</c>).</summary>
        FromExisting,

        /// <summary>Import a field from ISO-XML (WinForms btnFromISOXML → <c>DialogResult.Abort</c>).</summary>
        FromIsoXml,

        /// <summary>An AgShare download / bulk-upload sub-dialog was shown and has been handled (WinForms btnJobAgShare / btnAgShareBulkUpload → <c>DialogResult.Ignore</c>).</summary>
        AgShareHandled,

        /// <summary>Easy Drive was requested — isEasyDriveRequested set true (WinForms btnEasyDrive → <c>DialogResult.OK</c>).</summary>
        EasyDrive,

        /// <summary>Close the current field — saved before close (WinForms btnJobClose → <c>DialogResult.OK</c>).</summary>
        CloseField
    }

    /// <summary>
    /// [XPLAT] Code-behind for <c>FormJobView.axaml</c> — a faithful Avalonia port of the WinForms
    /// <c>FormJob</c> (FormJob.cs + FormJob.Designer.cs): the field / job launcher menu.
    /// </summary>
    /// <remarks>
    /// Imperative, code-behind-driven dialog (NO <c>DataContext</c>, NO <c>x:DataType</c>, NO MVVM
    /// bindings): controls are addressed by their <c>x:Name</c> and every button handler is wired
    /// programmatically in the parameterless constructor, reproducing the WinForms designer's
    /// <c>this.btnX.Click += ...</c> wiring (the paired <c>.axaml</c> deliberately declares no
    /// <c>Click</c> attributes). The <c>FormGPS mf</c> coupling of the original is replaced by
    /// constructor injection (AAP §0.3.2); the multi-state outcome is returned through
    /// <see cref="FormJobResult"/> / <c>ShowDialog&lt;FormJobResult&gt;</c> rather than the WinForms
    /// multi-way <c>DialogResult</c>.
    /// </remarks>
    public partial class FormJobView : Window
    {
        // === Injected collaborators (replace the FormGPS "mf" back-reference) ===

        // [XPLAT] Application model — supplies CurrentLatLon for the drive-in distance scan
        // (was mf.AppModel.CurrentLatLon).
        private readonly ApplicationModel _appModel;

        // [XPLAT] Metric vs imperial unit preference (was mf.isMetric) — selects km vs miles for the
        // drive-in distance column.
        private readonly bool _isMetric;

        // [XPLAT] Whether a field job is currently open (was mf.isJobStarted) — gates the Close button,
        // the resume label, the AgShare snapshot and the save-before-close in every handler.
        private readonly bool _isJobStarted;

        // [XPLAT] Whether Easy Drive mode is active (was mf.isEasyDriveMode) — when set, all buttons but
        // Close are disabled in OnLoaded.
        private readonly bool _isEasyDriveMode;

        // [XPLAT] The current field directory name (was mf.currentFieldDirectory). NOT readonly: the
        // load logic clears it to "" when the field file is missing, exactly as the original assigned
        // mf.currentFieldDirectory = "".
        private string _currentFieldDirectory;

        // [XPLAT] The local fields root (was RegistrySettings.fieldsDirectory) — injected so this view
        // never reaches a static directly; the caller passes RegistrySettings.fieldsDirectory.
        private readonly string _fieldsDirectory;

        // [XPLAT] Saves the open field before closing it (was mf.FileSaveEverythingBeforeClosingField()).
        private readonly Func<Task> _fileSaveEverythingBeforeClosingField;

        // [XPLAT] Opens a field by its Field.txt path, or "Resume" (was mf.FileOpenField(...)).
        private readonly Func<string, Task> _fileOpenField;

        // [XPLAT] Triggers an AgShare snapshot of the open field (was mf.AgShareSnapshot()).
        private readonly Action _agShareSnapshot;

        // [XPLAT] Closes any top-most overlay forms (was mf.CloseTopMosts()).
        private readonly Action _closeTopMosts;

        // [XPLAT] Sets the host's isEasyDriveRequested = true side-effect (was mf.isEasyDriveRequested = true).
        private readonly Action _setEasyDriveRequested;

        // [XPLAT] Sets the host's isCancelJobMenu = true side-effect (was mf.isCancelJobMenu = true).
        private readonly Action _setCancelJobMenu;

        // [XPLAT] Reads the host's filePickerFileAndDirectory (was mf.filePickerFileAndDirectory get).
        private readonly Func<string> _getFilePickerFileAndDirectory;

        // [XPLAT] Writes the host's filePickerFileAndDirectory (was mf.filePickerFileAndDirectory set) —
        // populated for parity so any host code that reads the field keeps working.
        private readonly Action<string> _setFilePickerFileAndDirectory;

        // [XPLAT] AgShare network client (was mf.agShareClient) — used to build the downloader / uploader.
        private readonly AgShareClient _agShareClient;

        // [XPLAT] The owning window — used as the AgShare downloader's context owner (was the FormGPS the
        // WinForms downloader received as gpsContext).
        private readonly Window _owner;

        // [XPLAT] Marks an instance built through the parity constructor below. Guards OnLoaded /
        // OnClosing so the parameterless instance the Avalonia XAML loader / previewer creates (which has
        // no injected collaborators) never runs the load logic or persists placement.
        private readonly bool _initialized;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, which the Release <c>TreatWarningsAsErrors</c> build treats as an error). It
        /// is the single construction chokepoint: <see cref="InitializeComponent"/> runs here and every
        /// button handler is wired here once (reproducing the WinForms designer wiring), so the parity
        /// constructor below chains to it with <c>: this()</c>.
        /// </summary>
        public FormJobView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired Click events reproduced programmatically (the .axaml declares none).
            btnJobNew.Click += btnJobNew_Click;
            btnJobResume.Click += btnJobResume_Click;
            btnJobOpen.Click += btnJobOpen_Click;
            btnInField.Click += btnInField_Click;
            btnFromKML.Click += btnFromKML_Click;
            btnFromExisting.Click += btnFromExisting_Click;
            btnFromISOXML.Click += btnFromISOXML_Click;
            btnJobClose.Click += btnJobClose_Click;
            btnEasyDrive.Click += btnEasyDrive_Click;
            btnJobAgShare.Click += btnJobAgShare_Click;
            btnAgShareBulkUpload.Click += btnAgShareBulkUpload_Click;
            btnDeleteAB.Click += btnDeleteAB_Click;
        }

        /// <summary>
        /// [XPLAT] Behavioural-parity constructor replacing the WinForms
        /// <c>FormJob(Form callingForm)</c> (which cast the caller to <c>FormGPS</c> and stored
        /// <c>mf</c>). Every collaborator the original reached through <c>mf</c> is injected discretely;
        /// the localized button captions and title are applied exactly as the WinForms constructor did.
        /// </summary>
        /// <param name="appModel">Application model supplying <see cref="ApplicationModel.CurrentLatLon"/> (was <c>mf.AppModel</c>).</param>
        /// <param name="isMetric">Unit preference for the drive-in distance column (was <c>mf.isMetric</c>).</param>
        /// <param name="isJobStarted">Whether a field job is open (was <c>mf.isJobStarted</c>).</param>
        /// <param name="isEasyDriveMode">Whether Easy Drive mode is active (was <c>mf.isEasyDriveMode</c>).</param>
        /// <param name="currentFieldDirectory">The current field directory name (was <c>mf.currentFieldDirectory</c>).</param>
        /// <param name="fieldsDirectory">The local fields root (was <c>RegistrySettings.fieldsDirectory</c>).</param>
        /// <param name="fileSaveEverythingBeforeClosingField">Saves the open field before closing it (was <c>mf.FileSaveEverythingBeforeClosingField()</c>).</param>
        /// <param name="fileOpenField">Opens a field by path, or "Resume" (was <c>mf.FileOpenField(...)</c>).</param>
        /// <param name="agShareSnapshot">Triggers an AgShare snapshot (was <c>mf.AgShareSnapshot()</c>).</param>
        /// <param name="closeTopMosts">Closes top-most overlay forms (was <c>mf.CloseTopMosts()</c>).</param>
        /// <param name="setEasyDriveRequested">Sets the host's <c>isEasyDriveRequested = true</c> (was <c>mf.isEasyDriveRequested = true</c>).</param>
        /// <param name="setCancelJobMenu">Sets the host's <c>isCancelJobMenu = true</c> (was <c>mf.isCancelJobMenu = true</c>).</param>
        /// <param name="getFilePickerFileAndDirectory">Reads the host's <c>filePickerFileAndDirectory</c>.</param>
        /// <param name="setFilePickerFileAndDirectory">Writes the host's <c>filePickerFileAndDirectory</c>.</param>
        /// <param name="agShareClient">AgShare network client (was <c>mf.agShareClient</c>).</param>
        /// <param name="owner">The owning window, used as the AgShare downloader's context owner.</param>
        public FormJobView(
            ApplicationModel appModel,
            bool isMetric,
            bool isJobStarted,
            bool isEasyDriveMode,
            string currentFieldDirectory,
            string fieldsDirectory,
            Func<Task> fileSaveEverythingBeforeClosingField,
            Func<string, Task> fileOpenField,
            Action agShareSnapshot,
            Action closeTopMosts,
            Action setEasyDriveRequested,
            Action setCancelJobMenu,
            Func<string> getFilePickerFileAndDirectory,
            Action<string> setFilePickerFileAndDirectory,
            AgShareClient agShareClient,
            Window owner)
            : this()
        {
            _appModel = appModel;
            _isMetric = isMetric;
            _isJobStarted = isJobStarted;
            _isEasyDriveMode = isEasyDriveMode;
            _currentFieldDirectory = currentFieldDirectory;
            _fieldsDirectory = fieldsDirectory;
            _fileSaveEverythingBeforeClosingField = fileSaveEverythingBeforeClosingField;
            _fileOpenField = fileOpenField;
            _agShareSnapshot = agShareSnapshot;
            _closeTopMosts = closeTopMosts;
            _setEasyDriveRequested = setEasyDriveRequested;
            _setCancelJobMenu = setCancelJobMenu;
            _getFilePickerFileAndDirectory = getFilePickerFileAndDirectory;
            _setFilePickerFileAndDirectory = setFilePickerFileAndDirectory;
            _agShareClient = agShareClient;
            _owner = owner;

            // [XPLAT] Localized captions — verbatim from the WinForms constructor. The button captions
            // live in the named inner TextBlocks of the .axaml (btnJobOpenText, etc.).
            btnJobOpenText.Text = gStr.gsOpen;
            btnJobNewText.Text = gStr.gsNew;
            btnJobResumeText.Text = gStr.gsResume;
            btnInFieldText.Text = gStr.gsDriveIn;
            btnFromKMLText.Text = gStr.gsFromKml;
            btnFromExistingText.Text = gStr.gsFromExisting;
            btnJobCloseText.Text = gStr.gsClose;

            Title = gStr.gsStartNewField;

            lblAgShareCloudLoad.Text = "☁ " + gStr.gsAgShareAutoLoadActive;

            _initialized = true;
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormJob_Load</c> (WinForms <c>Form.Load</c> → Avalonia
        /// <see cref="Window.OnLoaded"/>): enables / disables buttons by job state, resolves the resume
        /// label, triggers the AgShare snapshot, hides the AgShare row (shrinking the window) when
        /// AgShare is disabled, and disables everything but Close in Easy Drive mode. Behaviour is
        /// reproduced exactly; only the WinForms API surface is translated.
        /// </summary>
        /// <param name="e">The routed-event payload.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            // Guard: the parameterless (XAML-loader / previewer) instance has no injected collaborators.
            if (!_initialized)
            {
                return;
            }

            // Close only makes sense when a field is open.
            btnJobClose.IsEnabled = _isJobStarted;

            // Check if directory and file exist (maybe deleted etc.).
            if (string.IsNullOrEmpty(_currentFieldDirectory))
            {
                btnJobResume.IsEnabled = false;
            }

            string directoryName = Path.Combine(_fieldsDirectory, _currentFieldDirectory);
            string fileAndDirectory = Path.Combine(directoryName, "Field.txt");

            // Trigger a snapshot to create a temp data file for the AgShare upload.
            if (_isJobStarted && Properties.Settings.Default.AgShareEnabled)
            {
                _agShareSnapshot();
            }

            if (!File.Exists(fileAndDirectory))
            {
                lblResumeField.Text = "";
                btnJobResume.IsEnabled = false;
                _currentFieldDirectory = "";

                Log.EventWriter("Field Directory is Empty or Missing");
            }
            else
            {
                lblResumeField.Text = gStr.gsResume + ": " + _currentFieldDirectory;

                if (_isJobStarted)
                {
                    btnJobResume.IsEnabled = false;
                    lblResumeField.Text = gStr.gsOpen + ": " + _currentFieldDirectory;
                }
            }

            // Hide AgShare buttons and shrink the window if AgShare is disabled.
            if (!Properties.Settings.Default.AgShareEnabled)
            {
                btnJobAgShare.IsVisible = false;
                btnAgShareBulkUpload.IsVisible = false;

                // [XPLAT] WinForms reduced ClientSize height by 75px; collapsing the AgShare row makes
                // SizeToContent recompute a window 75px shorter (the cross-platform equivalent).
                panelAgShare.IsVisible = false;
            }

            lblAgShareCloudLoad.IsVisible = Properties.Settings.Default.AgShareEnabled
                                         && Properties.Settings.Default.AgShareAutoLoad;

            _closeTopMosts();

            // Easy Drive button: disabled when a field is open, enabled otherwise.
            btnEasyDrive.IsEnabled = !_isJobStarted;

            // When Easy Drive is active, disable all buttons except Close.
            if (_isEasyDriveMode)
            {
                btnJobNew.IsEnabled = false;
                btnJobResume.IsEnabled = false;
                btnJobOpen.IsEnabled = false;
                btnInField.IsEnabled = false;
                btnFromKML.IsEnabled = false;
                btnFromExisting.IsEnabled = false;
                btnFromISOXML.IsEnabled = false;
                btnJobAgShare.IsEnabled = false;
                btnAgShareBulkUpload.IsEnabled = false;
                btnEasyDrive.IsEnabled = false;
                lblResumeField.Text = "Easy Drive Active";
            }
        }

        /// <summary>
        /// [XPLAT] btnJobNew — WinForms <c>btnJobNew_Click</c>. Fire-and-forget save of the open field
        /// (the original used <c>_ =</c>, kept verbatim), then close with <see cref="FormJobResult.NewField"/>
        /// (WinForms <c>DialogResult.Yes</c>).
        /// </summary>
        private void btnJobNew_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                // [XPLAT] Fire-and-forget exactly as the WinForms original (_ = mf.FileSave...()).
                _ = _fileSaveEverythingBeforeClosingField();
            }

            // back to FormGPS
            Close(FormJobResult.NewField);
        }

        /// <summary>
        /// [XPLAT] btnJobResume — WinForms <c>btnJobResume_Click</c>. Opens the previous field via
        /// <c>FileOpenField("Resume")</c>, then closes with <see cref="FormJobResult.Resume"/> (WinForms
        /// <c>DialogResult.OK</c>).
        /// </summary>
        private async void btnJobResume_Click(object sender, RoutedEventArgs e)
        {
            // open the Resume.txt and continue from last exit
            await _fileOpenField("Resume");

            Log.EventWriter("Job Form, Field Resume");

            // back to FormGPS
            Close(FormJobResult.Resume);
        }

        /// <summary>
        /// [XPLAT] btnJobOpen — WinForms <c>btnJobOpen_Click</c>. Saves the open field, clears the
        /// file-picker path, shows <see cref="FormFilePickerView"/> and — when a field is chosen (the
        /// view returns a non-null path, the cross-platform equivalent of <c>DialogResult.Yes</c>) —
        /// opens it and closes with <see cref="FormJobResult.OpenPicker"/>.
        /// </summary>
        private async void btnJobOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            _setFilePickerFileAndDirectory("");

            // [XPLAT] FormFilePicker (WinForms) -> FormFilePickerView (Views/Pickers): returns the chosen
            // Field.txt path or null (cancelled) instead of writing mf.filePickerFileAndDirectory + Yes.
            string picked = await new FormFilePickerView(_appModel, _isMetric).ShowDialog<string>(this);
            if (picked != null)
            {
                _setFilePickerFileAndDirectory(picked);
                await _fileOpenField(_getFilePickerFileAndDirectory());
                Close(FormJobResult.OpenPicker);
            }
        }

        /// <summary>
        /// [XPLAT] btnInField — WinForms <c>btnInField_Click</c>. Scans every field folder for a
        /// <c>Field.txt</c> within 0.5&#160;km of the current position (skip 8 header lines, read the
        /// WGS84 start position, compute the great-circle distance), builds the candidate name / distance
        /// lists, then opens the single match directly or shows <see cref="FormDrivePickerView"/> for a
        /// choice. Corrupt files and the no-match case raise <see cref="FormDialogView"/> errors. All
        /// behaviour is frozen from the original; only the WinForms API is translated.
        /// </summary>
        private async void btnInField_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            string infieldList = "";
            string distanceList = "";
            int numFields = 0;

            string[] dirs = Directory.GetDirectories(_fieldsDirectory);

            foreach (string dir in dirs)
            {
                string filename = Path.Combine(dir, "Field.txt");

                // make sure directory has a Field.txt in it
                if (File.Exists(filename))
                {
                    using (GeoStreamReader reader = new GeoStreamReader(filename))
                    {
                        try
                        {
                            // Skip 8 lines
                            for (int i = 0; i < 8; i++)
                            {
                                reader.ReadLine();
                            }

                            // start positions
                            if (!reader.EndOfStream)
                            {
                                Wgs84 startLatLon = reader.ReadWgs84();
                                double distance = startLatLon.DistanceInKiloMeters(_appModel.CurrentLatLon);

                                if (distance < 0.5)
                                {
                                    numFields++;
                                    if (!string.IsNullOrEmpty(infieldList))
                                    {
                                        infieldList += ",";
                                        distanceList += ",";
                                    }
                                    infieldList += Path.GetFileName(dir);

                                    // Convert to miles if not metric
                                    Distance distanceObj = new Distance(distance * 1000); // Distance expects meters
                                    double displayDistance = _isMetric ? distanceObj.InKilometers : distanceObj.InMiles;
                                    distanceList += displayDistance.ToString("F3", CultureInfo.InvariantCulture);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // [XPLAT] FormDialog.Show -> FormDialogView.ShowAsync; awaited so the loop
                            // resumes after the modal dialog closes, exactly as the synchronous original.
                            await FormDialogView.ShowAsync(gStr.gsFieldFileIsCorrupt, gStr.gsChooseADifferentField, DialogSeverity.Error, this);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(infieldList))
            {
                _setFilePickerFileAndDirectory("");

                if (numFields > 1)
                {
                    // [XPLAT] FormDrivePicker (WinForms) -> FormDrivePickerView (Views/Pickers): returns
                    // the chosen Field.txt path or null (cancelled).
                    string picked = await new FormDrivePickerView(infieldList, distanceList, _isMetric).ShowDialog<string>(this);
                    if (picked != null)
                    {
                        _setFilePickerFileAndDirectory(picked);
                        await _fileOpenField(_getFilePickerFileAndDirectory());
                        Close(FormJobResult.DriveIn);
                    }
                    else
                    {
                        return;
                    }
                }
                else // 1 field found
                {
                    _setFilePickerFileAndDirectory(Path.Combine(_fieldsDirectory, infieldList, "Field.txt"));
                    await _fileOpenField(_getFilePickerFileAndDirectory());
                    Close(FormJobResult.DriveIn);
                }
            }
            else // no fields found
            {
                await FormDialogView.ShowAsync(gStr.gsNoFieldsFound, gStr.gsFieldNotOpen, DialogSeverity.Error, this);
            }
        }

        /// <summary>
        /// [XPLAT] btnFromKML — WinForms <c>btnFromKML_Click</c>. Saves the open field, then closes with
        /// <see cref="FormJobResult.FromKml"/> (WinForms <c>DialogResult.No</c>).
        /// </summary>
        private async void btnFromKML_Click(object sender, RoutedEventArgs e)
        {
            // back to FormGPS
            if (_isJobStarted)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            Close(FormJobResult.FromKml);
        }

        /// <summary>
        /// [XPLAT] btnFromExisting — WinForms <c>btnFromExisting_Click</c>. Closes with
        /// <see cref="FormJobResult.FromExisting"/> (WinForms <c>DialogResult.Retry</c>).
        /// </summary>
        private void btnFromExisting_Click(object sender, RoutedEventArgs e)
        {
            // back to FormGPS
            Close(FormJobResult.FromExisting);
        }

        /// <summary>
        /// [XPLAT] btnJobClose — WinForms <c>btnJobClose_Click</c>. Saves the open field on a background
        /// task (the original used <c>Task.Run(...)</c>, kept verbatim), then closes with
        /// <see cref="FormJobResult.CloseField"/> (WinForms <c>DialogResult.OK</c>).
        /// </summary>
        private void btnJobClose_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                // [XPLAT] Task.Run kept exactly as the WinForms original.
                _ = Task.Run(() => _fileSaveEverythingBeforeClosingField());
            }

            // back to FormGPS
            Close(FormJobResult.CloseField);
        }

        /// <summary>
        /// [XPLAT] btnFromISOXML — WinForms <c>btnFromISOXML_Click</c>. Saves the open field, then closes
        /// with <see cref="FormJobResult.FromIsoXml"/> (WinForms <c>DialogResult.Abort</c>).
        /// </summary>
        private async void btnFromISOXML_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            // back to FormGPS
            Close(FormJobResult.FromIsoXml);
        }

        /// <summary>
        /// [XPLAT] btnDeleteAB — WinForms <c>btnDeleteAB_Click</c>. The WinForms handler body only set
        /// <c>mf.isCancelJobMenu = true</c>, but the designer additionally gave the button
        /// <c>DialogResult.Cancel</c> (FormJob.Designer.cs), so clicking it BOTH set the flag AND closed
        /// the form. Both effects are preserved here: set the cancel-menu flag, then close with the
        /// neutral <see cref="FormJobResult.None"/> (the equivalent of <c>DialogResult.Cancel</c>) so the
        /// host regains control — the paired <c>.axaml</c> documents this button as "closes the menu /
        /// cancels".
        /// </summary>
        private void btnDeleteAB_Click(object sender, RoutedEventArgs e)
        {
            _setCancelJobMenu();
            Close(FormJobResult.None);
        }

        /// <summary>
        /// [XPLAT] btnJobAgShare — WinForms <c>btnJobAgShare_Click</c>. Saves the open field, shows the
        /// AgShare download sub-dialog (<see cref="FormAgShareDownloaderView"/>) modally, then closes with
        /// <see cref="FormJobResult.AgShareHandled"/> (WinForms <c>DialogResult.Ignore</c>).
        /// </summary>
        private async void btnJobAgShare_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            // [XPLAT] FormAgShareDownloader(mf) -> FormAgShareDownloaderView with the four collaborators
            // the original read off the god-object injected discretely (plus the fields directory and the
            // context owner).
            FormAgShareDownloaderView downloader = new FormAgShareDownloaderView(
                _agShareClient,
                _isJobStarted,
                _fileSaveEverythingBeforeClosingField,
                _fileOpenField,
                _fieldsDirectory,
                _owner);
            await downloader.ShowDialog(this);

            Close(FormJobResult.AgShareHandled);
        }

        /// <summary>
        /// [XPLAT] btnEasyDrive — WinForms <c>btnEasyDrive_Click</c>. Sets the host's
        /// <c>isEasyDriveRequested = true</c> side-effect, then closes with
        /// <see cref="FormJobResult.EasyDrive"/> (WinForms <c>DialogResult.OK</c>).
        /// </summary>
        private void btnEasyDrive_Click(object sender, RoutedEventArgs e)
        {
            _setEasyDriveRequested();
            Close(FormJobResult.EasyDrive);
        }

        /// <summary>
        /// [XPLAT] btnAgShareBulkUpload — WinForms <c>btnAgShareBulkUpload_Click</c>. Blocks (with an
        /// error dialog) while a field is open; otherwise shows the AgShare bulk-upload sub-dialog
        /// (<see cref="FormAgShareUploaderView"/>) modally and closes with
        /// <see cref="FormJobResult.AgShareHandled"/> (WinForms <c>DialogResult.Ignore</c>).
        /// </summary>
        private async void btnAgShareBulkUpload_Click(object sender, RoutedEventArgs e)
        {
            if (_isJobStarted)
            {
                await FormDialogView.ShowAsync(gStr.gsError, gStr.gsCloseFieldFirst, DialogSeverity.Error, this);
                return;
            }

            // [XPLAT] FormAgShareUploader(mf.agShareClient) -> FormAgShareUploaderView(_agShareClient).
            FormAgShareUploaderView uploader = new FormAgShareUploaderView(_agShareClient);
            await uploader.ShowDialog(this);

            Close(FormJobResult.AgShareHandled);
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormJob_FormClosing</c> (WinForms <c>Form.FormClosing</c> → Avalonia
        /// <see cref="Window.OnClosing"/>): persists the job-menu window placement to the settings facade.
        /// Guarded by <see cref="_initialized"/> (so the design-time instance never writes) and a
        /// try/catch (settings I/O is best-effort, exactly as the original's unconditional Save).
        /// </summary>
        /// <param name="e">The window-closing payload.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (!_initialized)
            {
                return;
            }

            try
            {
                // [XPLAT] setJobMenu_location / setJobMenu_size remain System.Drawing.Point / Size
                // (cross-platform System.Drawing.Primitives) in the settings facade. Position is the
                // window's screen pixel origin; Bounds is the actual rendered size (never NaN, unlike
                // Width/Height under SizeToContent). Fully qualified to avoid Avalonia.Point / Size.
                Properties.Settings.Default.setJobMenu_location = new System.Drawing.Point(Position.X, Position.Y);
                Properties.Settings.Default.setJobMenu_size = new System.Drawing.Size((int)Bounds.Width, (int)Bounds.Height);
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormJobView placement persist failed: " + ex);
            }
        }
    }
}
