// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] WinForms AgOpenGPS.FormFilePicker (Forms/Pickers/FormFilePicker.cs +
// FormFilePicker.Designer.cs) -> Avalonia AgOpenGPS.Views.Pickers.FormFilePickerView. This is a
// 1:1 behavioural-parity port of the most complex of the four pickers: it enumerates every field
// folder that contains a Field.txt, computes each field's distance from the current GPS position
// (via the Core GeoStreamReader + Wgs84.DistanceInKiloMeters) and its boundary area (shoelace from
// Boundary.txt), and offers a three-way column reorder, a delete-with-confirmation, a cancel, and a
// "use selected" action.
//
// Migration deltas (every behavioural change is provenance-tagged // [XPLAT] at its call site):
//   * The FormGPS "mf" god-object coupling is removed via constructor injection (AAP §0.3.2): the
//     current latitude/longitude comes from the injected ApplicationModel.CurrentLatLon and the
//     hectare-vs-acre unit choice from the injected isMetric flag. No "mf", no new interface.
//   * The dead WinForms designer timer (timer1, Interval 300, enabled in Load but with NO Tick
//     handler anywhere) is omitted.
//   * Confirm / error dialogs route through AgOpenGPS.Views.FormDialogView (ShowAsync /
//     ShowQuestionAsync) instead of the WinForms FormDialog.
//   * The result is returned through Close(string) / ShowDialog<string> — the chosen Field.txt full
//     path, or null when cancelled / no fields — replacing mf.filePickerFileAndDirectory + the
//     WinForms DialogResult auto-close.
//   * The hard-coded '\\' used to wrap the long path for the delete-confirm dialog is replaced with
//     Path.DirectorySeparatorChar, and every numeric format uses CultureInfo.InvariantCulture
//     (§0.6.5) so comma-decimal locales cannot corrupt the distance/area text or break parity.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Streamers;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormFilePickerView.axaml</c> — a faithful Avalonia port of the
    /// WinForms <c>FormFilePicker</c> (FormFilePicker.cs + FormFilePicker.Designer.cs): the field
    /// picker that lists every field folder containing a <c>Field.txt</c> with its distance and
    /// boundary-area columns, lets the operator cycle the column order (Sort), delete a field, cancel,
    /// or open the selected field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative / code-behind-driven dialog (NO <c>DataContext</c>, NO <c>x:DataType</c>, NO MVVM):
    /// controls are addressed by <c>x:Name</c> and every handler is wired programmatically in the
    /// constructor, reproducing the WinForms designer's <c>this.btnX.Click += ...</c> wiring. The list
    /// rows are reflection-bound (<c>{Binding Col0/Col1/Col2}</c> under the markup's
    /// <c>x:CompileBindings="False"</c>) to the <see cref="FileItem"/> row record this code-behind
    /// fills.
    /// </para>
    /// <para>
    /// The <c>FormGPS mf</c> coupling of the original is replaced by constructor injection of the
    /// <see cref="ApplicationModel"/> (current latitude/longitude) and the metric flag (AAP §0.3.2),
    /// and the chosen <c>Field.txt</c> path is returned through <see cref="Window.Close(object)"/> /
    /// <c>ShowDialog&lt;string&gt;</c> rather than the WinForms <c>filePickerFileAndDirectory</c> +
    /// <c>DialogResult</c>.
    /// </para>
    /// </remarks>
    public partial class FormFilePickerView : Window
    {
        /// <summary>
        /// [XPLAT] One field row. <see cref="Name"/> / <see cref="Distance"/> / <see cref="Area"/> are
        /// the raw values; <see cref="Col0"/> / <see cref="Col1"/> / <see cref="Col2"/> are the
        /// reflection-bound display slots the markup template renders, rewritten per the current sort
        /// order (the WinForms <c>GetFieldNames(order)</c> idiom). The properties are public so the
        /// reflection bindings can read them even though the type itself is a private implementation
        /// detail.
        /// </summary>
        private sealed class FileItem
        {
            public string Name { get; set; }       // field directory name
            public string Distance { get; set; }   // pre-formatted distance ("N2" padded) or "---"
            public string Area { get; set; }       // pre-formatted area ("N1" padded) or "No Bndry"
            public string Col0 { get; set; }        // ordered display slots (filled per _order)
            public string Col1 { get; set; }
            public string Col2 { get; set; }
        }

        // [XPLAT] Replaces "mf": the injected ApplicationModel supplies the live GPS position
        // (CurrentLatLon) the original read through mf.AppModel.CurrentLatLon.
        private readonly ApplicationModel _appModel;

        // [XPLAT] Replaces mf.isMetric: selects hectares (metric) vs acres (imperial) for the area.
        private readonly bool _isMetric;

        // Current column order (0 = Name|Distance|Area, 1 = Distance|Name|Area, 2 = Area|Name|Distance),
        // mirroring the WinForms "order" field.
        private int _order;

        // The field rows shown in the list. Cleared and rebuilt by LoadFieldList so reloads (after a
        // delete) never duplicate entries.
        private readonly List<FileItem> _items = new List<FileItem>();

        // [XPLAT] Marks an instance built through the parity constructor below. Guards OnOpened so the
        // parameterless instance the Avalonia XAML loader/previewer creates never triggers the
        // "no fields" dialog (its _appModel is null and its list was never loaded).
        private readonly bool _initialized;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, which the Release build — <c>TreatWarningsAsErrors</c> — treats as an
        /// error). It is the single construction chokepoint: <see cref="InitializeComponent"/> runs
        /// here and the four command-button handlers are wired here once (reproducing the WinForms
        /// designer wiring), so the parity constructor below chains to it with <c>: this()</c>.
        /// </summary>
        public FormFilePickerView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired Click events reproduced programmatically (the .axaml declares none).
            btnByDistance.Click += Sort_Click;          // WinForms btnByDistance_Click
            btnOpenExistingLv.Click += UseSelected_Click; // WinForms btnOpenExistingLv_Click
            btnDeleteAB.Click += Cancel_Click;          // WinForms btnDeleteAB_Click
            btnDeleteField.Click += DeleteField_Click;  // WinForms btnDeleteField_Click
        }

        /// <summary>
        /// [XPLAT] Parity replacement for the WinForms <c>FormFilePicker(Form callingForm)</c>
        /// constructor: instead of casting the caller to <c>FormGPS</c> and reading <c>mf</c>, the live
        /// position and unit preference are injected. Localises the UI (parity with the WinForms
        /// constructor's <c>gStr</c> assignments) and builds the field list (parity with
        /// <c>FormFilePicker_Load</c>: <c>order = 0; LoadFieldList(); UpdateListView();</c>). The dead
        /// <c>timer1</c> is intentionally not reproduced.
        /// </summary>
        /// <param name="appModel">
        /// The application model supplying <see cref="ApplicationModel.CurrentLatLon"/> for the
        /// per-field distance column (replaces <c>mf.AppModel.CurrentLatLon</c>).
        /// </param>
        /// <param name="isMetric">
        /// <see langword="true"/> to report boundary area in hectares, <see langword="false"/> in acres
        /// (replaces <c>mf.isMetric</c>).
        /// </param>
        public FormFilePickerView(ApplicationModel appModel, bool isMetric)
            : this()
        {
            _appModel = appModel;
            _isMetric = isMetric;

            // Translate the UI (parity with the WinForms constructor).
            Title = gStr.gsFieldPicker;             // WinForms this.Text
            btnSortText.Text = gStr.gsSort;         // WinForms btnByDistance.Text
            btnOpenText.Text = gStr.gsUseSelected;  // WinForms btnOpenExistingLv.Text
            labelDeleteField.Text = gStr.gsDeleteField;
            labelCancel.Text = gStr.gsCancel;

            _order = 0;
            LoadFieldList();
            UpdateListView();

            _initialized = true;
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>ShowNoFieldsMessage</c> path: the original showed the
        /// "no fields" error dialog and closed from inside <c>LoadFieldList</c>/<c>UpdateListView</c>.
        /// Showing a modal child dialog and awaiting it requires the window to already be open, so the
        /// empty-list check is deferred here to first open. The guard skips the design-time
        /// parameterless instance.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_initialized && _items.Count == 0)
            {
                // Fire-and-forget: the async helper awaits the modal dialog, then closes this window.
                _ = ShowNoFieldsMessageAsync();
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>LoadFieldList</c>: populate <see cref="_items"/> from every folder
        /// under the fields directory that actually contains a <c>Field.txt</c>. The original
        /// try/catch around <c>Directory.GetDirectories</c> (degrading to an empty set) is preserved;
        /// the no-fields dialog itself is raised from <see cref="OnOpened"/> (it must await).
        /// </summary>
        private void LoadFieldList()
        {
            // Clear first so a reload after a delete never duplicates rows.
            _items.Clear();

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(RegistrySettings.fieldsDirectory);
            }
            catch
            {
                dirs = Array.Empty<string>();
            }

            foreach (string dir in dirs)
            {
                // [XPLAT] Path.Combine instead of a hard-coded separator.
                string fieldFile = Path.Combine(dir, "Field.txt");

                // Only show folders that actually have a Field.txt (parity with the original filter).
                if (!File.Exists(fieldFile))
                {
                    continue;
                }

                FileItem item = new FileItem { Name = new DirectoryInfo(dir).Name };
                AppendFieldNameAndDistance(item, fieldFile);
                AppendBoundaryArea(item, dir);
                _items.Add(item);
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>AppendFieldNameAndDistance</c>: read the legacy <c>Field.txt</c>
        /// (skip 8 lines, then a WGS84 position) and compute the great-circle distance in kilometres
        /// from the current position, falling back to a neutral placeholder on any failure.
        /// </summary>
        private void AppendFieldNameAndDistance(FileItem item, string filename)
        {
            try
            {
                using (var reader = new GeoStreamReader(filename))
                {
                    // Legacy format: skip 8 lines, then expect a WGS84 position.
                    for (int i = 0; i < 8; i++)
                    {
                        reader.ReadLine();
                    }

                    if (!reader.EndOfStream)
                    {
                        var startLatLon = reader.ReadWgs84();
                        // [XPLAT] Distance from the current GPS position (was mf.AppModel.CurrentLatLon).
                        double km = startLatLon.DistanceInKiloMeters(_appModel.CurrentLatLon);
                        // [XPLAT] InvariantCulture so comma-decimal locales cannot corrupt the value (§0.6.5).
                        item.Distance = Math.Round(km, 2).ToString("N2", CultureInfo.InvariantCulture).PadLeft(10);
                    }
                    else
                    {
                        // Incomplete file -> neutral placeholder (parity with the original).
                        item.Distance = "---".PadLeft(10);
                    }
                }
            }
            catch (Exception ex)
            {
                // Neutral placeholder; no popup here (parity — the central loader validates again).
                AgLibrary.Logging.Log.EventWriter("Field.txt read failed (picker): " + ex);
                item.Distance = "---".PadLeft(10);
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>AppendBoundaryArea</c>: compute the field's boundary area from
        /// <c>Boundary.txt</c> when present, showing "No Bndry" for a zero/degenerate area and a
        /// neutral placeholder on failure.
        /// </summary>
        private void AppendBoundaryArea(FileItem item, string dir)
        {
            // [XPLAT] Path.Combine instead of a hard-coded separator.
            string filename = Path.Combine(dir, "Boundary.txt");

            if (!File.Exists(filename))
            {
                item.Area = "---".PadLeft(10);
                return;
            }

            try
            {
                double area = CalculateBoundaryArea(filename);
                // Show "No Bndry" if area = 0 (legacy behavior), otherwise the numeric value.
                // [XPLAT] InvariantCulture on the "N1" format (§0.6.5).
                item.Area = area == 0
                    ? "No Bndry"
                    : Math.Round(area, 1).ToString("N1", CultureInfo.InvariantCulture).PadLeft(10);
            }
            catch (Exception ex)
            {
                AgLibrary.Logging.Log.EventWriter("Boundary.txt read failed (picker): " + ex);
                item.Area = "---".PadLeft(10);
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>CalculateBoundaryArea</c>: parse the easting/northing boundary points
        /// and compute the polygon area with the shoelace formula, converting to hectares (metric) or
        /// acres (imperial). Returns 0 on insufficient points (≤ 5) or a degenerate polygon. All
        /// parsing uses <see cref="CultureInfo.InvariantCulture"/> (§0.6.5).
        /// </summary>
        private double CalculateBoundaryArea(string filename)
        {
            var pointList = new List<vec3>();
            using (var reader = new StreamReader(filename))
            {
                string line;

                // Header.
                line = reader.ReadLine();
                if (line == null) return 0;

                // Next line(s): may contain True/False flags before the point count; handle legacy
                // variations safely (up to two such flags).
                line = reader.ReadLine();
                if (line == null) return 0;

                if (line == "True" || line == "False")
                {
                    line = reader.ReadLine();
                    if (line == null) return 0;
                }
                if (line == "True" || line == "False")
                {
                    line = reader.ReadLine();
                    if (line == null) return 0;
                }

                int numPoints;
                if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out numPoints))
                {
                    return 0;
                }

                if (numPoints <= 0) return 0;

                for (int i = 0; i < numPoints; i++)
                {
                    line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) return 0;

                    var words = line.Split(',');
                    if (words.Length < 3) return 0;

                    double e, n, h;
                    if (!double.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)) return 0;
                    if (!double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return 0;
                    if (!double.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return 0;

                    pointList.Add(new vec3(e, n, h));
                }
            }

            int ptCount = pointList.Count;
            if (ptCount <= 5) return 0;

            // Shoelace algorithm.
            double acc = 0;
            int j = ptCount - 1;
            for (int i = 0; i < ptCount; j = i++)
            {
                acc += (pointList[j].easting + pointList[i].easting) *
                       (pointList[j].northing - pointList[i].northing);
            }

            double areaM2 = Math.Abs(acc / 2.0);
            // [XPLAT] _isMetric replaces mf.isMetric: hectares (×0.0001) vs acres (×0.00024711).
            return _isMetric ? (areaM2 * 0.0001) : (areaM2 * 0.00024711);
        }

        /// <summary>
        /// [XPLAT] Parity with <c>GetFieldNames(index)</c>: return the three display values in the
        /// current column order — 0 = [Name, Distance, Area], 1 = [Distance, Name, Area],
        /// 2 = [Area, Name, Distance].
        /// </summary>
        private static string[] GetFieldNames(FileItem item, int order)
        {
            if (order == 0)
            {
                return new[] { item.Name, item.Distance, item.Area };
            }
            if (order == 1)
            {
                return new[] { item.Distance, item.Name, item.Area };
            }
            return new[] { item.Area, item.Name, item.Distance };
        }

        /// <summary>
        /// [XPLAT] Parity with <c>UpdateColumnHeaders</c>: relabel the three fixed-width header slots
        /// (chName / chDistance / chArea) for the current order. The WinForms version also swapped the
        /// per-column widths (680/140/140 following the field position); the markup uses fixed
        /// 680/140/140 columns, so only the header text is reassigned here — the same gStr values, in
        /// the same per-column order, so headers stay aligned with the <see cref="GetFieldNames"/>
        /// cells.
        /// </summary>
        private void UpdateColumnHeaders()
        {
            if (_order == 0)
            {
                chName.Text = gStr.gsField;
                chDistance.Text = gStr.gsDistance;
                chArea.Text = gStr.gsArea;
            }
            else if (_order == 1)
            {
                chName.Text = gStr.gsDistance;
                chDistance.Text = gStr.gsField;
                chArea.Text = gStr.gsArea;
            }
            else
            {
                chName.Text = gStr.gsArea;
                chDistance.Text = gStr.gsField;
                chArea.Text = gStr.gsDistance;
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>UpdateListView</c>: project every row onto its three display slots for
        /// the current order, then refresh the list. The WinForms version cleared and re-added
        /// <c>ListViewItem</c>s; in Avalonia, reassigning <c>ItemsSource</c> (null, then the list)
        /// re-renders the template with the new column slots. Also refreshes the headers.
        /// </summary>
        private void UpdateListView()
        {
            foreach (FileItem item in _items)
            {
                string[] cols = GetFieldNames(item, _order);
                item.Col0 = cols[0];
                item.Col1 = cols[1];
                item.Col2 = cols[2];
            }

            // Force the ItemsControl to rebuild its containers against the rewritten Col0/Col1/Col2.
            lvLines.ItemsSource = null;
            lvLines.ItemsSource = _items;

            UpdateColumnHeaders();
        }

        /// <summary>
        /// [XPLAT] Parity with <c>ShowNoFieldsMessage</c>: show the error dialog, log it, and close the
        /// picker with a null result (cancelled / no fields). Async because the Avalonia dialog is
        /// awaited; invoked from <see cref="OnOpened"/> and after a delete that empties the list.
        /// </summary>
        private async Task ShowNoFieldsMessageAsync()
        {
            await FormDialogView.ShowAsync(gStr.gsNoFieldsFound, gStr.gsCreateNewField, DialogSeverity.Error, this);
            AgLibrary.Logging.Log.EventWriter("File Picker, No Fields");
            Close(null);
        }

        /// <summary>
        /// [XPLAT] Parity with <c>btnByDistance_Click</c>: cycle the column order 0 → 1 → 2 → 0 and
        /// rebuild the list (the picker stays open).
        /// </summary>
        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _order = (_order + 1) % 3;
            UpdateListView();
        }

        /// <summary>
        /// [XPLAT] Parity with <c>btnOpenExistingLv_Click</c> (WinForms DialogResult.Yes): return the
        /// chosen field's <c>Field.txt</c> path. The original resolved the field name from the first or
        /// second sub-item depending on the order; both always resolve to the raw field name, so
        /// <see cref="FileItem.Name"/> is used directly. With nothing selected (or an empty / "---"
        /// name) the picker simply stays open.
        /// </summary>
        private void UseSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!(lvLines.SelectedItem is FileItem item))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim() == "---")
            {
                return;
            }

            // [XPLAT] Return the chosen Field.txt path via Close(string) — replaces
            // mf.filePickerFileAndDirectory + DialogResult.Yes. Path.Combine for cross-platform paths.
            Close(Path.Combine(RegistrySettings.fieldsDirectory, item.Name, "Field.txt"));
        }

        /// <summary>
        /// [XPLAT] Parity with <c>btnDeleteAB_Click</c> (WinForms DialogResult.Cancel): close with a
        /// null result. The WinForms button cleared <c>mf.filePickerFileAndDirectory</c> and the
        /// designer's DialogResult auto-closed the form; a null result here signals "cancelled /
        /// nothing chosen".
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }

        /// <summary>
        /// [XPLAT] Parity with <c>btnDeleteField_Click</c>: confirm, recursively delete the selected
        /// field directory, then reload the list (the picker stays open). When the delete empties the
        /// list, the "no fields" message is shown and the picker closes (parity with the original
        /// reload path).
        /// </summary>
        private async void DeleteField_Click(object sender, RoutedEventArgs e)
        {
            if (!(lvLines.SelectedItem is FileItem item))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim() == "---")
            {
                return;
            }

            // [XPLAT] Path.Combine for the directory to delete.
            string dir2Delete = Path.Combine(RegistrySettings.fieldsDirectory, item.Name);

            // [XPLAT] Wrap the path for the confirm dialog: insert a newline near char 45.
            // Replaces the WinForms hard-coded '\\' with Path.DirectorySeparatorChar.
            const int maxLength = 45;
            string multiLineDir = dir2Delete;
            if (maxLength < dir2Delete.Length)
            {
                int sep = dir2Delete.LastIndexOf(Path.DirectorySeparatorChar, Math.Min(maxLength, dir2Delete.Length - 1));
                if (sep != -1)
                {
                    multiLineDir = dir2Delete.Insert(sep + 1, "\n");
                }
            }

            // [XPLAT] User-action confirmation (was FormDialog.ShowQuestion != DialogResult.OK -> return).
            bool confirmed = await FormDialogView.ShowQuestionAsync(gStr.gsDeleteForSure, multiLineDir, DialogSeverity.Warning, this);
            if (!confirmed)
            {
                return;
            }

            try
            {
                Directory.Delete(dir2Delete, true);
            }
            catch (Exception ex)
            {
                AgLibrary.Logging.Log.EventWriter("Field delete failed: " + ex);
            }

            // Reload and refresh (parity with the original; the picker stays open).
            LoadFieldList();
            UpdateListView();

            // [XPLAT] The WinForms reload showed the "no fields" message (and closed) when the last
            // field was deleted; reproduce that here.
            if (_items.Count == 0)
            {
                await ShowNoFieldsMessageAsync();
            }
        }
    }
}
