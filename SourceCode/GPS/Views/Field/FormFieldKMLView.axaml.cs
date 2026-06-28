// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create Field From KML" dialog — a 1:1 parity port of the WinForms
    /// <c>FormFieldKML</c> (Forms/Field/FormFieldKML.cs + FormFieldKML.Designer.cs). The operator types a
    /// new field name (sanitised live against <c>glm.fileRegex</c>), optionally appends the current date
    /// and/or time, picks a Google-Earth KML file whose outer <c>&lt;coordinates&gt;</c> ring becomes the
    /// field boundary, then saves — which creates the field directory + initial files and loads the KML
    /// boundary.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// <c>x:Name</c>. Unlike the lighter "host-owned" sibling stubs, this view performs the full original
    /// behaviour itself — the KML parse, the lat/lon averaging and the field-creation file I/O — because
    /// the agent specification mandates a faithful port. The former <c>FormGPS</c> (<c>mf</c>) god-object
    /// back-reference is replaced by constructor-injected collaborators:
    /// <list type="bullet">
    ///   <item><see cref="ApplicationModel"/> (local-plane conversion + origin), <c>CBoundary</c>,
    ///         <c>CFieldData</c> and <c>CNMEA</c> are the live domain objects.</item>
    ///   <item>The remaining <c>mf</c> calls (<c>JobNew</c>, the <c>FileCreate*</c> / <c>FileSave*</c>
    ///         helpers, <c>CalculateMinMax</c>, the AB-draw reveal, the language-menu toggle and the
    ///         <c>currentFieldDirectory</c> write-back) are injected delegates so this dialog holds no
    ///         reference to the owning shell.</item>
    /// </list>
    /// The KML parsing (<see cref="FindLatLon"/> / <see cref="LoadKMLBoundary"/>) is BEHAVIOR-FROZEN and
    /// preserved byte-for-byte from the original (InvariantCulture throughout; <c>lon = fix[0]</c>,
    /// <c>lat = fix[1]</c>). XPLAT swaps: WinForms <c>OpenFileDialog</c> → Avalonia
    /// <see cref="IStorageProvider"/> picker; <c>FormDialog.Show</c> → <see cref="FormDialogView.ShowAsync"/>;
    /// the on-screen keyboard → <see cref="FormKeyboard"/>; <c>DialogResult</c> → <c>Close(bool)</c>
    /// consumed by the host via <c>await ShowDialog&lt;bool&gt;(owner)</c>.
    /// </remarks>
    public partial class FormFieldKMLView : Window
    {
        // [XPLAT] Injected collaborators replacing the WinForms FormGPS (mf) god-object back-reference.
        private readonly ApplicationModel _appModel;
        private readonly CBoundary _bnd;
        private readonly CFieldData _fd;
        private readonly CNMEA _pn;
        private readonly bool _isKeyboardOn;
        private readonly Func<Task> _fileSaveEverythingBeforeClosingField;
        private readonly Action _jobNew;
        private readonly Action _fileSaveBoundary;
        private readonly Action _fileCreateSections;
        private readonly Action _fileCreateRecPath;
        private readonly Action _fileCreateContour;
        private readonly Action _fileCreateElevation;
        private readonly Action _fileSaveFlags;
        private readonly Action _calculateMinMax;
        private readonly Action _showAbDraw;
        private readonly Action<string> _setCurrentFieldDirectory;
        private readonly Action<bool> _setMenuLanguageEnabled;

        // [XPLAT] Class variables — identical to FormFieldKML.cs.
        private double latK, lonK;
        private string fileName;

        // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside TextChanged re-raises the event.
        private bool _suppressTextChanged;

        /// <summary>
        /// [XPLAT] Live equivalent of the WinForms <c>FormGPS.isJobStarted</c>, which is a computed
        /// property (<c>AppModel.Fields.ActiveField != null</c>) rather than a stored flag. It MUST be
        /// evaluated live (never captured as a constructor snapshot) because the original reads it both
        /// before <c>JobNew()</c> (to decide whether to save the currently-open field first) and again
        /// after <c>JobNew()</c> has opened the new field — a snapshot would break the create-first-field
        /// flow.
        /// </summary>
        private bool IsJobStarted => _appModel != null && _appModel.Fields.ActiveField != null;

        /// <summary>
        /// Parameterless constructor used by the Avalonia compiled-XAML loader. Mirrors the WinForms
        /// constructor + <c>FormFieldDir_Load</c>: localises the prompt, sets the title and starts with
        /// the Save button disabled, then wires the textbox <c>TextChanged</c> (sanitisation) and the tap
        /// → on-screen-keyboard flow that the WinForms designer attached.
        /// </summary>
        public FormFieldKMLView()
        {
            InitializeComponent();

            labelFieldname.Text = gStr.gsEnterFieldName;
            Title = gStr.gsCreateNewField;
            btnSave.IsEnabled = false;

            // [XPLAT] tboxFieldName_TextChanged + tboxFieldName_Click were wired in the WinForms Designer;
            // the .axaml deliberately declares no events for tboxFieldName, so wire them here by name.
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            tboxFieldName.AddHandler(Gestures.TappedEvent, OnFieldNameTapped);
        }

        /// <summary>
        /// Injection constructor used by the host. Chains the XAML constructor and stores the live domain
        /// collaborators and behaviour delegates extracted from the former <c>FormGPS</c> coupling.
        /// </summary>
        public FormFieldKMLView(
            ApplicationModel appModel,
            CBoundary bnd,
            CFieldData fd,
            CNMEA pn,
            bool isKeyboardOn,
            Func<Task> fileSaveEverythingBeforeClosingField,
            Action jobNew,
            Action fileSaveBoundary,
            Action fileCreateSections,
            Action fileCreateRecPath,
            Action fileCreateContour,
            Action fileCreateElevation,
            Action fileSaveFlags,
            Action calculateMinMax,
            Action showAbDraw,
            Action<string> setCurrentFieldDirectory,
            Action<bool> setMenuLanguageEnabled)
            : this()
        {
            _appModel = appModel;
            _bnd = bnd;
            _fd = fd;
            _pn = pn;
            _isKeyboardOn = isKeyboardOn;
            _fileSaveEverythingBeforeClosingField = fileSaveEverythingBeforeClosingField;
            _jobNew = jobNew;
            _fileSaveBoundary = fileSaveBoundary;
            _fileCreateSections = fileCreateSections;
            _fileCreateRecPath = fileCreateRecPath;
            _fileCreateContour = fileCreateContour;
            _fileCreateElevation = fileCreateElevation;
            _fileSaveFlags = fileSaveFlags;
            _calculateMinMax = calculateMinMax;
            _showAbDraw = showAbDraw;
            _setCurrentFieldDirectory = setCurrentFieldDirectory;
            _setMenuLanguageEnabled = setMenuLanguageEnabled;
        }

        // [XPLAT] tboxFieldName_TextChanged: strip filesystem-illegal characters with glm.fileRegex
        // (caret preserved) and enable btnLoadKML only for a non-empty trimmed name.
        private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
            {
                return;
            }

            TextBox textBox = (TextBox)sender;
            int caret = textBox.CaretIndex;
            string current = textBox.Text ?? string.Empty;
            string sanitized = Regex.Replace(current, glm.fileRegex, "");

            if (!string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                _suppressTextChanged = true;
                textBox.Text = sanitized;
                textBox.CaretIndex = Math.Min(caret, sanitized.Length);
                _suppressTextChanged = false;
            }

            btnLoadKML.IsEnabled = !string.IsNullOrEmpty((textBox.Text ?? string.Empty).Trim());
        }

        // [XPLAT] tboxFieldName_Click: when the injected on-screen keyboard is enabled, show FormKeyboard
        // and write the result back. WinForms `((TextBox)sender).ShowKeyboard(this)` → modal FormKeyboard.
        private async void OnFieldNameTapped(object sender, TappedEventArgs e)
        {
            if (!_isKeyboardOn)
            {
                return;
            }

            TextBox textBox = (TextBox)sender;
            string result = await new FormKeyboard(textBox.Text ?? string.Empty).ShowDialog<string>(this);
            if (result != null)
            {
                textBox.Text = result;
            }

            btnSerialCancel.Focus();
        }

        // [XPLAT] btnAddDate_Click: append " yyyy-MM-dd" (InvariantCulture — cross-platform stable).
        private void OnAddDateClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnAddTime_Click: append " HH-mm" (InvariantCulture — cross-platform stable).
        private void OnAddTimeClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnSerialCancel_Click: close without creating a field. WinForms Close() → Close(false).
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnLoadKML_Click: cross-platform replacement for the WinForms OpenFileDialog. Picks a
        // KML file (start folder = RegistrySettings.fieldsDirectory), seeds the field name from the file
        // name when blank, then runs FindLatLon + LoadKMLBoundary exactly as the original.
        private async void OnLoadKmlClick(object sender, RoutedEventArgs e)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            // [XPLAT] OpenFileDialog.InitialDirectory → StorageProvider suggested start location.
            IStorageFolder startFolder = null;
            if (!string.IsNullOrEmpty(RegistrySettings.fieldsDirectory))
            {
                startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(RegistrySettings.fieldsDirectory);
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "KML files (*.KML)",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KML files (*.KML)")
                    {
                        // [XPLAT] Both cases listed for case-sensitive (Linux/macOS) filesystems.
                        Patterns = new[] { "*.kml", "*.KML" }
                    }
                }
            });

            // [XPLAT] WinForms `if (ofd.ShowDialog() == DialogResult.Cancel) return;`
            if (files == null || files.Count == 0)
            {
                return;
            }

            string picked = files[0].Path.LocalPath;

            if (string.IsNullOrEmpty(tboxFieldName.Text))
            {
                tboxFieldName.Text = Path.GetFileNameWithoutExtension(picked);
            }
            fileName = picked;

            // get lat and lon from boundary in kml
            await FindLatLon(fileName);

            // check if we can load
            // Load the outer boundary
            await LoadKMLBoundary(fileName, false);
        }

        // [XPLAT] btnSave_Click: save the currently-open field (if any), create the new field, load the
        // KML boundary into it, then close positive. WinForms `DialogResult = OK; Close();` → Close(true).
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (IsJobStarted && _fileSaveEverythingBeforeClosingField != null)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            // reset sim and world to kml position
            await CreateNewField();

            // Load the outer boundary
            await LoadKMLBoundary(fileName, true);

            Close(true);
        }

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN KML outer-ring parse (preserved byte-for-byte from FormFieldKML.cs):
        /// scans for <c>&lt;coordinates&gt;</c> … <c>&lt;/coordinates&gt;</c>, splits the accumulated text,
        /// parses each <c>lon,lat[,alt]</c> tuple with <see cref="CultureInfo.InvariantCulture"/>
        /// (<c>lon = fix[0]</c>, <c>lat = fix[1]</c>), converts WGS84 → local plane and builds the fence.
        /// When <paramref name="fieldCreated"/> is set, the fence is committed to the boundary list and
        /// the field is saved (boundary + turn lines + GUI areas + min/max).
        /// </summary>
        private async Task LoadKMLBoundary(string filename, bool fieldCreated)
        {
            string coordinates = null;
            int startIndex;

            using (StreamReader reader = new StreamReader(filename))
            {
                try
                {
                    while (!reader.EndOfStream)
                    {
                        // start to read the file
                        string line = reader.ReadLine();

                        startIndex = line.IndexOf("<coordinates>", StringComparison.Ordinal);

                        if (startIndex != -1)
                        {
                            while (true)
                            {
                                int endIndex = line.IndexOf("</coordinates>", StringComparison.Ordinal);

                                if (endIndex == -1)
                                {
                                    // just add the line
                                    if (startIndex == -1) coordinates += " " + line.Substring(0);
                                    else coordinates += line.Substring(startIndex + 13);
                                }
                                else
                                {
                                    if (startIndex == -1) coordinates += " " + line.Substring(0, endIndex);
                                    else coordinates += line.Substring(startIndex + 13, endIndex - (startIndex + 13));
                                    break;
                                }
                                line = reader.ReadLine();
                                line = line.Trim();
                                startIndex = -1;
                            }

                            line = coordinates;
                            string[] numberSets = line.Split();

                            // at least 3 points
                            if (numberSets.Length > 2)
                            {
                                CBoundaryList New = new CBoundaryList();

                                foreach (string item in numberSets)
                                {
                                    if (item.Length < 3)
                                        continue;
                                    string[] fix = item.Split(',');
                                    double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lonK);
                                    double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latK);

                                    GeoCoord geoCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(latK, lonK));
                                    New.fenceLine.Add(new vec3(geoCoord));
                                }

                                // build the boundary, make sure is clockwise for outer counter clockwise for inner
                                New.CalculateFenceArea(_bnd.bndList.Count);
                                New.FixFenceLine(_bnd.bndList.Count);
                                if (fieldCreated)
                                {
                                    _bnd.bndList.Add(New);

                                    _showAbDraw?.Invoke();
                                }

                                coordinates = "";
                            }
                            else
                            {
                                await FormDialogView.ShowAsync(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error, this);
                                Log.EventWriter("New Field, Error Reading KML");
                            }
                            break;
                        }
                    }
                    if (fieldCreated)
                    {
                        _fileSaveBoundary?.Invoke();
                        _bnd.BuildTurnLines();
                        _fd.UpdateFieldBoundaryGUIAreas();
                        _calculateMinMax?.Invoke();
                    }

                    btnSave.IsEnabled = true;
                    btnLoadKML.IsEnabled = false;
                }
                catch (Exception ee)
                {
                    btnSave.IsEnabled = false;
                    btnLoadKML.IsEnabled = false;
                    await FormDialogView.ShowAsync(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error, this);
                    Log.EventWriter("New Field, Error Reading KML" + ee.ToString());
                    return;
                }
            }

            _bnd.isOkToAddPoints = false;
        }

        /// <summary>
        /// [XPLAT] BEHAVIOR-FROZEN (preserved byte-for-byte): parses the KML <c>&lt;coordinates&gt;</c>
        /// ring and averages every latitude/longitude to a representative origin point
        /// (<see cref="latK"/>/<see cref="lonK"/>), using <see cref="CultureInfo.InvariantCulture"/>.
        /// </summary>
        private async Task FindLatLon(string filename)
        {
            string coordinates = null;
            int startIndex;

            using (StreamReader reader = new StreamReader(filename))
            {
                try
                {
                    while (!reader.EndOfStream)
                    {
                        // start to read the file
                        string line = reader.ReadLine();

                        startIndex = line.IndexOf("<coordinates>", StringComparison.Ordinal);

                        if (startIndex != -1)
                        {
                            while (true)
                            {
                                int endIndex = line.IndexOf("</coordinates>", StringComparison.Ordinal);

                                if (endIndex == -1)
                                {
                                    // just add the line
                                    if (startIndex == -1) coordinates += " " + line.Substring(0);
                                    else coordinates += line.Substring(startIndex + 13);
                                }
                                else
                                {
                                    if (startIndex == -1) coordinates += " " + line.Substring(0, endIndex);
                                    else coordinates += line.Substring(startIndex + 13, endIndex - (startIndex + 13));
                                    break;
                                }
                                line = reader.ReadLine();
                                line = line.Trim();
                                startIndex = -1;
                            }

                            line = coordinates;
                            char[] delimiterChars = { ' ', '\t', '\r', '\n' };
                            string[] numberSets = line.Split(delimiterChars);

                            // at least 3 points
                            if (numberSets.Length > 2)
                            {
                                double counter = 0, lat = 0, lon = 0;
                                latK = lonK = 0;
                                foreach (string item in numberSets)
                                {
                                    if (item.Length < 3)
                                        continue;
                                    string[] fix = item.Split(',');
                                    double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lonK);
                                    double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latK);
                                    lat += latK;
                                    lon += lonK;
                                    counter += 1;
                                }
                                lonK = lon / counter;
                                latK = lat / counter;

                                coordinates = "";
                            }
                            else
                            {
                                await FormDialogView.ShowAsync(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error, this);
                                Log.EventWriter("New Field, Error Reading KML ");
                            }
                            break;
                        }
                    }
                }
                catch (Exception et)
                {
                    await FormDialogView.ShowAsync("Exception", "Error Finding Lat Lon", DialogSeverity.Error, this);
                    Log.EventWriter("Lat Lon Exception Reading KML " + et.ToString());
                    return;
                }
            }

            _bnd.isOkToAddPoints = false;
        }

        /// <summary>
        /// [XPLAT] Creates the new field directory and its initial files. Preserves the original
        /// behaviour: empty-name guard, <c>currentFieldDirectory</c> write-back, language-menu toggle,
        /// <c>JobNew()</c>, directory-exists guard, local-plane definition, the job-not-open guard, the
        /// "KML Derived" <c>Field.txt</c> header (InvariantCulture date + origin), then
        /// Sections/RecPath/Contour/Elevation creation and the flags save.
        /// </summary>
        private async Task CreateNewField()
        {
            // fill something in
            if (string.IsNullOrEmpty((tboxFieldName.Text ?? string.Empty).Trim()))
            {
                Close(false);
                return;
            }

            // append date time to name
            string currentFieldDirectory = (tboxFieldName.Text ?? string.Empty).Trim();
            _setCurrentFieldDirectory?.Invoke(currentFieldDirectory);

            // get the directory and make sure it exists, create if not
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);

            _setMenuLanguageEnabled?.Invoke(false);
            // if no template set just make a new file.
            try
            {
                // start a new job
                _jobNew?.Invoke();

                // create it for first save
                if ((!string.IsNullOrEmpty(directoryName)) && (Directory.Exists(directoryName)))
                {
                    await FormDialogView.ShowAsync(gStr.gsChooseADifferentName, gStr.gsDirectoryExists, DialogSeverity.Error, this);
                    return;
                }
                else
                {
                    _pn.DefineLocalPlane(new Wgs84(latK, lonK), true);

                    // make sure directory exists, or create it
                    if ((!string.IsNullOrEmpty(directoryName)) && (!Directory.Exists(directoryName)))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    // create the field file header info
                    if (!IsJobStarted)
                    {
                        await FormDialogView.ShowAsync(gStr.gsFieldNotOpen, gStr.gsCreateNewField, DialogSeverity.Error, this);
                        return;
                    }
                    string myFileName;

                    // get the directory and make sure it exists, create if not
                    directoryName = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);

                    if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    myFileName = "Field.txt";

                    using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, myFileName)))
                    {
                        // Write out the date
                        writer.WriteLine(DateTime.Now.ToString("yyyy-MMMM-dd hh:mm:ss tt", CultureInfo.InvariantCulture));

                        writer.WriteLine("$FieldDir");
                        writer.WriteLine("KML Derived");

                        // write out the easting and northing Offsets
                        writer.WriteLine("$Offsets");
                        writer.WriteLine("0,0");

                        writer.WriteLine("Convergence");
                        writer.WriteLine("0");

                        writer.WriteLine("StartFix");
                        writer.WriteLine(
                            _appModel.LocalPlane.Origin.Latitude.ToString(CultureInfo.InvariantCulture) + "," +
                            _appModel.LocalPlane.Origin.Longitude.ToString(CultureInfo.InvariantCulture));
                    }

                    _fileCreateSections?.Invoke();
                    _fileCreateRecPath?.Invoke();
                    _fileCreateContour?.Invoke();
                    _fileCreateElevation?.Invoke();
                    _fileSaveFlags?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Creating new kml field " + ex.ToString());

                await FormDialogView.ShowAsync(gStr.gsError, ex.ToString(), DialogSeverity.Error, this);
                _setCurrentFieldDirectory?.Invoke("");
            }
        }
    }
}
