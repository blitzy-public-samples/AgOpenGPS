// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create Field From Existing" dialog — a 1:1 behavioural-parity port
    /// of the WinForms <c>FormFieldExisting</c> (Forms/Field/FormFieldExisting.cs +
    /// FormFieldExisting.Designer.cs). The operator picks an existing field from a three-column list
    /// (name / distance from the current position / boundary area) to use as a TEMPLATE, types a new
    /// field name (sanitised live against <c>glm.fileRegex</c>), optionally appends the vehicle name /
    /// date / time, chooses which artefacts to copy (Flags / applied mapping / Headland / guidance
    /// Lines) and saves — cloning the chosen template into a brand-new field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); every control is addressed by
    /// its <c>x:Name</c> and every handler is wired programmatically in the constructor (the paired
    /// <c>FormFieldExistingView.axaml</c> declares none). The list rows bind (reflection) to the
    /// <see cref="FieldRow"/> record the XAML <c>ItemTemplate</c> references (Name / Distance / Area).
    /// </para>
    /// <para>
    /// The WinForms form reached the <c>FormGPS</c> god-object through an <c>mf</c> back-reference for
    /// the live position, the unit preference, the job state, the on-screen-keyboard flag and the
    /// field-IO lifecycle. Per AAP §0.3.2 that coupling is inverted: the host injects the live
    /// collaborators through the settable properties below (the same dependency-inversion seam used by
    /// the sibling <c>FormCopyTracksView</c> / <c>FormShiftPosView</c>) before calling
    /// <c>ShowDialog&lt;bool&gt;</c>. There is intentionally NO <c>FormGPS</c> field here.
    /// </para>
    /// <para>
    /// Cross-platform conversions (each tagged <c>// [XPLAT]</c>): WinForms <c>ListView</c>/<c>SubItems</c>
    /// → an Avalonia <see cref="ListBox"/> bound to an <see cref="ObservableCollection{T}"/> of
    /// <see cref="FieldRow"/>; <c>FormDialog.Show</c> → <c>await FormDialogView.ShowAsync</c>; the
    /// <c>ShowKeyboard</c> extension → the migrated <see cref="FormKeyboard"/>; <c>MouseDown</c> → Click;
    /// <c>SelectionStart</c> → <see cref="TextBox.CaretIndex"/>; <c>Load</c> → <see cref="Control.OnLoaded"/>;
    /// <c>.Enabled</c> → <c>.IsEnabled</c>; <c>DialogResult</c> → <c>Close(bool)</c>. All numeric
    /// parse/format uses <see cref="CultureInfo.InvariantCulture"/> for cross-OS determinism (§0.6.5).
    /// The empty WinForms <c>timer1</c> tick is dropped, and the <c>ScreenHelper.IsOnScreen</c>
    /// reposition is omitted (the window centres on its owner).
    /// </para>
    /// </remarks>
    public partial class FormFieldExistingView : Window
    {
        /// <summary>
        /// [XPLAT] One display row over the three list columns. The WinForms list stored each field as a
        /// (name, distance, area) triple across a <c>ListViewItem</c>'s <c>SubItems</c>; here those three
        /// slots are the <see cref="Name"/> / <see cref="Distance"/> / <see cref="Area"/> properties the
        /// XAML <c>ItemTemplate</c> binds. The slot a given value occupies changes with the sort order
        /// (see <see cref="RebuildRows"/>), exactly as the WinForms <c>SubItems</c> were reordered.
        /// </summary>
        private sealed class FieldRow
        {
            /// <summary>The value shown in the first (flexible) column for the current sort order.</summary>
            public string Name { get; set; }

            /// <summary>The value shown in the second column for the current sort order.</summary>
            public string Distance { get; set; }

            /// <summary>The value shown in the third column for the current sort order.</summary>
            public string Area { get; set; }
        }

        // [XPLAT] Sentinel placed in a column when the field is damaged (parity with the WinForms
        // "Error" SubItem text); used both to render the damaged row and to gate selection.
        private const string ErrorText = "Error";

        // [XPLAT] The bound row collection (replaces lvLines.Items). Cleared and refilled on load and on
        // each sort so the bound ListBox refreshes automatically.
        private readonly ObservableCollection<FieldRow> _rows = new ObservableCollection<FieldRow>();

        // [XPLAT] The canonical (name, distance, area) triples in load order (replaces the WinForms flat
        // fileList). The display rows in _rows are projected from these per the current sort order, so
        // sorting never loses or re-reads the underlying data.
        private readonly List<string[]> _fieldData = new List<string[]>();

        // [XPLAT] The three-way column-cycling state (WinForms 'order': 0 → 1 → 2 → 0).
        private int _order;

        // [XPLAT] Guards the one-shot field-list load against a repeated Loaded event (Avalonia raises
        // Loaded each time the control is (re)attached to the visual tree).
        private bool _loaded;

        /// <summary>
        /// [XPLAT] The shared application model (replaces <c>mf.AppModel</c>). Its
        /// <see cref="ApplicationModel.CurrentLatLon"/> is the reference position the per-field distance
        /// column is measured from. Set by the host before <c>ShowDialog&lt;bool&gt;</c>.
        /// </summary>
        public ApplicationModel AppModel { get; set; }

        /// <summary>
        /// [XPLAT] The metric/imperial unit preference (replaces <c>mf.isMetric</c>); selects the
        /// hectare vs acre conversion factor for the boundary-area column.
        /// </summary>
        public bool IsMetric { get; set; }

        /// <summary>
        /// [XPLAT] Whether a field/job is currently open (replaces <c>mf.isJobStarted</c>); when
        /// <see langword="true"/> the open field is saved before the clone is created.
        /// </summary>
        public bool IsJobStarted { get; set; }

        /// <summary>
        /// [XPLAT] Whether the on-screen keyboard is enabled (replaces <c>mf.isKeyboardOn</c>); when
        /// <see langword="true"/> tapping the name box opens <see cref="FormKeyboard"/>.
        /// </summary>
        public bool IsKeyboardOn { get; set; }

        /// <summary>
        /// [XPLAT] Closes any top-most helper windows before the picker loads (replaces
        /// <c>mf.CloseTopMosts()</c>). Optional — invoked only when supplied.
        /// </summary>
        public Action CloseTopMosts { get; set; }

        /// <summary>
        /// [XPLAT] Saves everything for the currently open field before it is closed (replaces
        /// <c>mf.FileSaveEverythingBeforeClosingField()</c>). Awaited only when <see cref="IsJobStarted"/>.
        /// </summary>
        public Func<Task> FileSaveEverythingBeforeClosingField { get; set; }

        /// <summary>
        /// [XPLAT] Opens the freshly cloned field by its <c>Field.txt</c> path (replaces
        /// <c>mf.FileOpenField(path)</c>). Awaited at the end of a successful save.
        /// </summary>
        public Func<string, Task> FileOpenField { get; set; }

        /// <summary>
        /// [XPLAT] Closes/abandons the current job when a template read fails mid-clone (replaces
        /// <c>mf.JobClose()</c>). Optional — invoked only when supplied.
        /// </summary>
        public Action JobClose { get; set; }

        /// <summary>
        /// Initializes the dialog: loads the XAML, assigns the translated captions and wires every
        /// control event by name (the markup declares none). The field list itself is populated in
        /// <see cref="OnLoaded"/>, mirroring the WinForms <c>FormFieldExisting_Load</c> timing.
        /// </summary>
        public FormFieldExistingView()
        {
            InitializeComponent();

            // [XPLAT] WinForms ctor captions (label1 / btnSort) + the placeholder template label, plus the
            // window Title (the WinForms designer text was "Field Save As"; the runtime caption is
            // gStr.gsCreateNewField, matching the FormDialog titles the load loop uses).
            label1.Text = gStr.gsEditFieldName;
            SetSortCaption(gStr.gsSort);
            lblTemplateChosen.Text = "---";
            // [XPLAT] WinForms designer Text "Field Save As" → the runtime caption gStr.gsCreateNewField
            // (the same title the FormDialog error paths use), since Avalonia exposes Window.Title.
            Title = gStr.gsCreateNewField;

            // [XPLAT] Bind the ListBox to the observable row collection once; refills update it live.
            lvLines.ItemsSource = _rows;

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            tboxFieldName.Tapped += OnFieldNameTapped;
            lvLines.SelectionChanged += OnSelectionChanged;
            btnSort.Click += OnSortClick;
            btnAddVehicleName.Click += OnAddVehicleNameClick;
            btnAddDate.Click += OnAddDateClick;
            btnAddTime.Click += OnAddTimeClick;
            btnBackSpace.Click += OnBackSpaceClick;
            btnSerialCancel.Click += OnCancelClick;
            btnSave.Click += OnSaveClick;
        }

        // [XPLAT] WinForms set btnSort.Text = gStr.gsSort directly. The Avalonia btnSort hosts a glyph
        // Image plus a caption TextBlock inside a panel, so the localized caption is applied to that
        // nested TextBlock. Defensive type checks keep this a no-op if the button content is ever a
        // plain string or restructured, so the control tree is never assumed.
        private void SetSortCaption(string text)
        {
            if (btnSort.Content is string)
            {
                btnSort.Content = text;
                return;
            }

            if (btnSort.Content is Panel panel)
            {
                foreach (Control child in panel.Children)
                {
                    if (child is TextBlock caption)
                    {
                        caption.Text = text;
                        return;
                    }
                }
            }
        }

        // [XPLAT] Project the canonical (name, distance, area) triples onto the display rows for the
        // current sort order. WinForms reordered the ListViewItem SubItems; here the same reordering is
        // applied as the row is built. The flexible first column always shows slot 0 for the order.
        private void RebuildRows()
        {
            _rows.Clear();

            foreach (string[] triple in _fieldData)
            {
                FieldRow row;
                if (_order == 1)
                {
                    // {distance, name, area}
                    row = new FieldRow { Name = triple[1], Distance = triple[0], Area = triple[2] };
                }
                else if (_order == 2)
                {
                    // {area, name, distance}
                    row = new FieldRow { Name = triple[2], Distance = triple[0], Area = triple[1] };
                }
                else
                {
                    // {name, distance, area}
                    row = new FieldRow { Name = triple[0], Distance = triple[1], Area = triple[2] };
                }

                _rows.Add(row);
            }
        }

        // [XPLAT] Swap the three column-header captions to match the current sort order, exactly as the
        // WinForms btnSort_Click reassigned chName/chDistance/chArea.Text. (The XAML uses fixed column
        // widths — *,140,140 — so only the captions are swapped, per the markup's wiring contract.)
        private void UpdateColumnHeaders()
        {
            if (_order == 1)
            {
                chName.Text = gStr.gsDistance;
                chDistance.Text = gStr.gsField;
                chArea.Text = gStr.gsArea;
            }
            else if (_order == 2)
            {
                chName.Text = gStr.gsArea;
                chDistance.Text = gStr.gsField;
                chArea.Text = gStr.gsDistance;
            }
            else
            {
                chName.Text = gStr.gsField;
                chDistance.Text = gStr.gsDistance;
                chArea.Text = gStr.gsArea;
            }
        }

        /// <summary>
        /// [XPLAT] Ported from <c>FormFieldExisting_Load</c>: disables Save, closes any top-most helper
        /// windows, then enumerates every field directory and builds the (name, distance, area) list —
        /// reading the start position from each <c>Field.txt</c> (distance from the current position) and
        /// the boundary area from each <c>Boundary.txt</c>. Damaged fields surface a
        /// <see cref="FormDialogView"/> error and are flagged "Error"/"No Bndry". Runs once.
        /// </summary>
        /// <param name="e">The routed event data passed to the base implementation.</param>
        protected override async void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            // [XPLAT] Avalonia raises Loaded on every (re)attach; the WinForms Load fired once. Guard so
            // the field list is built a single time.
            if (_loaded)
            {
                return;
            }
            _loaded = true;

            btnSave.IsEnabled = false;
            CloseTopMosts?.Invoke();

            _order = 0;

            string[] dirs = Directory.GetDirectories(RegistrySettings.fieldsDirectory);

            if (dirs == null || dirs.Length < 1)
            {
                await FormDialogView.ShowAsync(gStr.gsCreateNewField, gStr.gsFileError, DialogSeverity.Error, this);
                Log.EventWriter("File Error Load Existing Field");

                Close();
                return;
            }

            // [XPLAT] The reference position the distance column is measured from (was
            // mf.AppModel.CurrentLatLon). Defensive default keeps a missing model from throwing.
            Wgs84 currentLatLon = AppModel != null ? AppModel.CurrentLatLon : default;

            foreach (string dir in dirs)
            {
                string fieldDirectory = Path.GetFileName(dir);
                string filename = Path.Combine(dir, "Field.txt");

                // Only list a directory that actually contains a Field.txt (WinForms "else continue").
                if (!File.Exists(filename))
                {
                    continue;
                }

                string distanceText = ErrorText;

                using (GeoStreamReader reader = new GeoStreamReader(filename))
                {
                    try
                    {
                        // Skip the first 8 lines of the file.
                        for (int i = 0; i < 8; i++)
                        {
                            reader.ReadLine();
                        }

                        // Try reading a WGS84 start position.
                        if (!reader.EndOfStream)
                        {
                            Wgs84 startLatLon = reader.ReadWgs84();
                            double distance = startLatLon.DistanceInKiloMeters(currentLatLon);

                            // [XPLAT] InvariantCulture for cross-OS-stable display (§0.6.5).
                            distanceText = Math.Round(distance, 2)
                                .ToString("N2", CultureInfo.InvariantCulture).PadLeft(10);
                        }
                        else
                        {
                            // Incomplete file: flag it and warn.
                            await FormDialogView.ShowAsync(gStr.gsFileError,
                                fieldDirectory + " is Damaged, Please Delete This Field",
                                DialogSeverity.Error, this);

                            distanceText = ErrorText;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Invalid file content: flag it, warn and log for diagnostics.
                        await FormDialogView.ShowAsync(gStr.gsFileError,
                            fieldDirectory + " is Damaged, Please Delete, Field.txt is Broken",
                            DialogSeverity.Error, this);

                        Log.EventWriter(fieldDirectory
                            + " is Damaged, Please Delete, Field.txt is Broken\n" + ex.ToString());

                        distanceText = ErrorText;
                    }
                }

                string areaText = await BuildAreaText(dir, fieldDirectory);

                // [XPLAT] Canonical (name, distance, area) triple (replaces the WinForms flat fileList).
                _fieldData.Add(new[] { fieldDirectory, distanceText, areaText });
            }

            if (_fieldData.Count < 1)
            {
                await FormDialogView.ShowAsync(gStr.gsNoFieldsFound, gStr.gsCreateNewField, DialogSeverity.Error, this);
                Log.EventWriter("Create New Field, No Fields Found");

                Close();
                return;
            }

            RebuildRows();

            if (_rows.Count > 0)
            {
                UpdateColumnHeaders();
            }
            else
            {
                await FormDialogView.ShowAsync(gStr.gsNoFieldsFound, gStr.gsCreateNewField, DialogSeverity.Error, this);
                Log.EventWriter("Field Existing, No Fields to List");

                Close();
                return;
            }
        }

        /// <summary>
        /// [XPLAT] Computes the boundary-area column text for one field (ported from the
        /// <c>Boundary.txt</c> block of <c>FormFieldExisting_Load</c>): a shoelace area over the boundary
        /// points, converted to hectares (metric) or acres (imperial). A missing <c>Boundary.txt</c> is a
        /// damaged field ("Error" + dialog); a present-but-unusable boundary yields "No Bndry". All
        /// parsing uses <see cref="CultureInfo.InvariantCulture"/> (§0.6.5).
        /// </summary>
        /// <param name="dir">The absolute field directory path.</param>
        /// <param name="fieldDirectory">The field's display name (directory leaf).</param>
        /// <returns>The formatted area string, "No Bndry", or "Error".</returns>
        private async Task<string> BuildAreaText(string dir, string fieldDirectory)
        {
            string filename = Path.Combine(dir, "Boundary.txt");

            if (!File.Exists(filename))
            {
                await FormDialogView.ShowAsync(gStr.gsFileError,
                    fieldDirectory + " is Damaged, Missing Boundary.Txt \r\n Delete Field or Fix",
                    DialogSeverity.Error, this);

                Log.EventWriter(fieldDirectory + " is Damaged, Missing Boundary.Txt");

                return ErrorText;
            }

            double area = 0;
            List<vec3> pointList = new List<vec3>();

            using (StreamReader reader = new StreamReader(filename))
            {
                try
                {
                    // Read past the header line (Boundary).
                    reader.ReadLine();

                    if (!reader.EndOfStream)
                    {
                        // True/False OR a point count carried by older boundary files.
                        string line = reader.ReadLine();

                        // Older boundary files: the line above was a flag, so the count is next.
                        if (line == "True" || line == "False")
                        {
                            line = reader.ReadLine();
                        }

                        // Latest boundary files: a second flag may precede the point count.
                        if (line == "True" || line == "False")
                        {
                            line = reader.ReadLine();
                        }

                        int numPoints = int.Parse(line, CultureInfo.InvariantCulture);

                        if (numPoints > 0)
                        {
                            for (int i = 0; i < numPoints; i++)
                            {
                                line = reader.ReadLine();
                                string[] words = line.Split(',');
                                vec3 vecPt = new vec3(
                                    double.Parse(words[0], CultureInfo.InvariantCulture),
                                    double.Parse(words[1], CultureInfo.InvariantCulture),
                                    double.Parse(words[2], CultureInfo.InvariantCulture));

                                pointList.Add(vecPt);
                            }

                            int ptCount = pointList.Count;
                            if (ptCount > 5)
                            {
                                area = 0;          // Accumulates area in the loop.
                                int j = ptCount - 1;   // The last vertex precedes the first.

                                for (int i = 0; i < ptCount; j = i++)
                                {
                                    area += (pointList[j].easting + pointList[i].easting)
                                        * (pointList[j].northing - pointList[i].northing);
                                }

                                if (IsMetric)
                                {
                                    area = Math.Abs(area / 2) * 0.0001;
                                }
                                else
                                {
                                    area = Math.Abs(area / 2) * 0.00024711;
                                }
                            }
                        }
                    }
                }
                catch (Exception ef)
                {
                    area = 0;
                    Log.EventWriter(fieldDirectory + " Boundary.Txt error " + ef.ToString());
                }
            }

            if (area == 0)
            {
                Log.EventWriter("Boundary is Broken, no Area");
                return "No Bndry";
            }

            // [XPLAT] InvariantCulture for cross-OS-stable display (§0.6.5).
            return Math.Round(area, 1).ToString("N1", CultureInfo.InvariantCulture).PadLeft(10);
        }

        /// <summary>
        /// [XPLAT] Ported from <c>tboxFieldName_TextChanged</c>: live-sanitise the entered name against
        /// <c>glm.fileRegex</c> (preserving the caret via <see cref="TextBox.CaretIndex"/>) and enable
        /// Save only for a non-empty trimmed name. Re-setting the text to the sanitised value re-raises
        /// this handler once, but the sanitisation is idempotent so it settles immediately.
        /// </summary>
        /// <param name="sender">The name text box (unused; addressed by field).</param>
        /// <param name="e">The text-changed event data (unused).</param>
        private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
        {
            string current = tboxFieldName.Text ?? string.Empty;
            int caret = tboxFieldName.CaretIndex;

            string sanitized = Regex.Replace(current, glm.fileRegex, "");
            if (sanitized != current)
            {
                tboxFieldName.Text = sanitized;

                // [XPLAT] WinForms restored SelectionStart; Avalonia clamps CaretIndex into range.
                tboxFieldName.CaretIndex = Math.Min(caret, sanitized.Length);
            }

            btnSave.IsEnabled = !string.IsNullOrEmpty((tboxFieldName.Text ?? string.Empty).Trim());
        }

        /// <summary>
        /// [XPLAT] Ported from <c>tboxFieldName_Click</c>: when the on-screen keyboard is enabled, a tap
        /// on the name box opens <see cref="FormKeyboard"/>, writes the accepted value back and moves
        /// focus off the box (parity with the WinForms <c>btnSerialCancel.Focus()</c>).
        /// </summary>
        /// <param name="sender">The name text box (unused; addressed by field).</param>
        /// <param name="e">The tap gesture event data (unused).</param>
        private async void OnFieldNameTapped(object sender, TappedEventArgs e)
        {
            if (!IsKeyboardOn)
            {
                return;
            }

            string existing = tboxFieldName.Text ?? string.Empty;
            FormKeyboard keyboard = new FormKeyboard(existing);
            string result = await keyboard.ShowDialog<string>(this);

            // [XPLAT] FormKeyboard returns null on Cancel; a non-null result is the accepted text.
            if (result != null)
            {
                tboxFieldName.Text = result;
            }

            btnSerialCancel.Focus();
        }

        /// <summary>
        /// [XPLAT] Ported from <c>lvLines_SelectedIndexChanged</c>: a damaged ("Error") row warns;
        /// otherwise the chosen field becomes the template, its name seeds the name box, and Save is
        /// enabled.
        /// </summary>
        /// <param name="sender">The field list (unused; addressed by field).</param>
        /// <param name="e">The selection-changed event data (unused).</param>
        private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(lvLines.SelectedItem is FieldRow row))
            {
                return;
            }

            if (row.Name == ErrorText || row.Distance == ErrorText || row.Area == ErrorText)
            {
                // Custom dialog for the damaged-field warning (parity with the WinForms text).
                await FormDialogView.ShowAsync(gStr.gsFileError,
                    "This Field is Damaged, Please Delete \r\n ALREADY TOLD YOU THAT :)",
                    DialogSeverity.Error, this);
                return;
            }

            // [XPLAT] The field name is column 0 for sort order 0, else column 1 (WinForms SubItems[0]
            // vs SubItems[1]); in the FieldRow those slots are Name and Distance respectively.
            string templateName = _order == 0 ? row.Name : row.Distance;

            lblTemplateChosen.Text = templateName;
            tboxFieldName.Text = templateName.Trim();
            btnSave.IsEnabled = true;
        }

        /// <summary>
        /// [XPLAT] Ported from <c>btnSort_Click</c>: cycle the column order 0 → 1 → 2 → 0, rebuild the
        /// rows and swap the header captions.
        /// </summary>
        /// <param name="sender">The Sort button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnSortClick(object sender, RoutedEventArgs e)
        {
            _order += 1;
            if (_order == 3)
            {
                _order = 0;
            }

            RebuildRows();

            if (_rows.Count > 0)
            {
                UpdateColumnHeaders();
            }
        }

        // [XPLAT] btnAddVehicleName_Click: append the active vehicle profile name. String concatenation
        // treats a null TextBox.Text as empty, matching the WinForms (never-null) behaviour exactly.
        private void OnAddVehicleNameClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + RegistrySettings.vehicleProfileName;
        }

        // [XPLAT] btnAddDate_Click: append the current date (InvariantCulture, §0.6.5).
        private void OnAddDateClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnAddTime_Click: append the current time (InvariantCulture, §0.6.5).
        private void OnAddTimeClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnBackSpace (WinForms RepeatButton MouseDown → Click): remove the last character.
        private void OnBackSpaceClick(object sender, RoutedEventArgs e)
        {
            string current = tboxFieldName.Text ?? string.Empty;
            if (current.Length > 0)
            {
                tboxFieldName.Text = current.Remove(current.Length - 1, 1);
            }
        }

        // [XPLAT] btnSerialCancel_Click: WinForms Close() with DialogResult.Cancel → Close(false).
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] Ported from <c>btnSave_Click</c>: validate the chosen template and the target name,
        /// save any open field, create the new field directory, write its <c>Field.txt</c> header from
        /// the template's offsets / convergence / start-fix, conditionally copy the selected artefacts
        /// (with the WinForms stub fallbacks), open the cloned field, and close with success.
        /// </summary>
        /// <param name="sender">The Save button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Fill something in.
            string newName = (tboxFieldName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(newName))
            {
                return;
            }

            string fileStr = Path.Combine(RegistrySettings.fieldsDirectory, lblTemplateChosen.Text, "Field.txt");

            if (!File.Exists(fileStr))
            {
                await FormDialogView.ShowAsync(gStr.gsFieldFileIsCorrupt, gStr.gsChooseADifferentField, DialogSeverity.Error, this);
                return;
            }

            if (IsJobStarted && FileSaveEverythingBeforeClosingField != null)
            {
                await FileSaveEverythingBeforeClosingField();
            }

            // Get the directory and make sure it does not already exist, then create it.
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, newName);

            if (Directory.Exists(directoryName))
            {
                await FormDialogView.ShowAsync(gStr.gsDirectoryExists, gStr.gsChooseADifferentName, DialogSeverity.Error, this);
                return;
            }

            if (!string.IsNullOrEmpty(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            string offsets;
            string convergence;
            string startFix;

            // Read the template field file: offsets, convergence and the start fix at fixed line
            // positions (the only values needed from the template).
            using (StreamReader reader = new StreamReader(fileStr))
            {
                try
                {
                    reader.ReadLine();
                    reader.ReadLine();
                    reader.ReadLine();
                    reader.ReadLine();

                    offsets = reader.ReadLine();

                    reader.ReadLine();
                    convergence = reader.ReadLine();

                    reader.ReadLine();
                    startFix = reader.ReadLine();
                }
                catch (Exception ex)
                {
                    Log.EventWriter("While Opening Field" + ex);

                    await FormDialogView.ShowAsync(gStr.gsFieldFileIsCorrupt, gStr.gsChooseADifferentField, DialogSeverity.Error, this);
                    JobClose?.Invoke();
                    return;
                }
            }

            const string myFileName = "Field.txt";

            using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, myFileName)))
            {
                // Write the date (InvariantCulture, §0.6.5).
                writer.WriteLine(DateTime.Now.ToString("yyyy-MMMM-dd hh:mm:ss tt", CultureInfo.InvariantCulture));

                writer.WriteLine("$FieldDir");
                writer.WriteLine("FromExisting");

                // Easting/northing offsets.
                writer.WriteLine("$Offsets");
                writer.WriteLine(offsets);

                writer.WriteLine("$Convergence");
                writer.WriteLine(convergence);

                writer.WriteLine("StartFix");
                writer.WriteLine(startFix);
            }

            // Create the artefact copies from the template directory.
            string templateDirectoryName = Path.Combine(RegistrySettings.fieldsDirectory, lblTemplateChosen.Text);

            if (chkApplied.IsChecked == true)
            {
                CopyIfExists(Path.Combine(templateDirectoryName, "Contour.txt"), Path.Combine(directoryName, "Contour.txt"));
                CopyIfExists(Path.Combine(templateDirectoryName, "Sections.txt"), Path.Combine(directoryName, "Sections.txt"));
            }
            else
            {
                // Blank Sections.txt and a stub Contour.txt.
                using (new StreamWriter(Path.Combine(directoryName, "Sections.txt")))
                {
                    // intentionally blank
                }

                using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, "Contour.txt")))
                {
                    writer.WriteLine("$Contour");
                }
            }

            CopyIfExists(Path.Combine(templateDirectoryName, "BackPic.txt"), Path.Combine(directoryName, "BackPic.txt"));
            CopyIfExists(Path.Combine(templateDirectoryName, "BackPic.png"), Path.Combine(directoryName, "BackPic.png"));
            CopyIfExists(Path.Combine(templateDirectoryName, "Boundary.txt"), Path.Combine(directoryName, "Boundary.txt"));
            CopyIfExists(Path.Combine(templateDirectoryName, "Elevation.txt"), Path.Combine(directoryName, "Elevation.txt"));

            string headlinesSource = Path.Combine(templateDirectoryName, "Headlines.txt");
            string headlinesDest = Path.Combine(directoryName, "Headlines.txt");
            if (File.Exists(headlinesSource))
            {
                File.Copy(headlinesSource, headlinesDest);
            }
            else
            {
                using (StreamWriter writer = new StreamWriter(headlinesDest))
                {
                    writer.WriteLine("$Headlines");
                }
            }

            if (chkFlags.IsChecked == true)
            {
                CopyIfExists(Path.Combine(templateDirectoryName, "Flags.txt"), Path.Combine(directoryName, "Flags.txt"));
            }
            else
            {
                using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, "Flags.txt")))
                {
                    writer.WriteLine("$Flags");
                    writer.WriteLine("0");
                }
            }

            if (chkGuidanceLines.IsChecked == true)
            {
                CopyIfExists(Path.Combine(templateDirectoryName, "ABLines.txt"), Path.Combine(directoryName, "ABLines.txt"));
                CopyIfExists(Path.Combine(templateDirectoryName, "RecPath.txt"), Path.Combine(directoryName, "RecPath.txt"));
                CopyIfExists(Path.Combine(templateDirectoryName, "CurveLines.txt"), Path.Combine(directoryName, "CurveLines.txt"));
                CopyIfExists(Path.Combine(templateDirectoryName, "Tram.txt"), Path.Combine(directoryName, "Tram.txt"));
                CopyIfExists(Path.Combine(templateDirectoryName, "TrackLines.txt"), Path.Combine(directoryName, "TrackLines.txt"));
            }
            else
            {
                using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, "RecPath.txt")))
                {
                    writer.WriteLine("$RecPath");
                    writer.WriteLine("0");
                }
            }

            if (chkHeadland.IsChecked == true)
            {
                CopyIfExists(Path.Combine(templateDirectoryName, "Headland.txt"), Path.Combine(directoryName, "Headland.txt"));
            }

            // Now open the newly cloned field.
            if (FileOpenField != null)
            {
                await FileOpenField(Path.Combine(directoryName, myFileName));
            }

            // [XPLAT] WinForms DialogResult.OK + Close() → Close(true): the caller's ShowDialog<bool>
            // returns true to signal a successful clone.
            Close(true);
        }

        // [XPLAT] Copy a template artefact into the new field only when it exists (WinForms guarded every
        // File.Copy with File.Exists). Centralised to keep the clone block faithful and free of repetition.
        private static void CopyIfExists(string fileToCopy, string destination)
        {
            if (File.Exists(fileToCopy))
            {
                File.Copy(fileToCopy, destination);
            }
        }
    }
}
