// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] AgOpenGPS (GPS program) — Recorded-Path picker dialog (Avalonia code-behind).
//
// 1:1 BEHAVIOURAL PARITY of the WinForms Forms/Pickers/FormRecordPicker.cs (FormRecordPicker.cs +
// FormRecordPicker.Designer.cs, 159 lines); backing view for FormRecordPickerView.axaml. It lists the
// *.rec recorded driving paths in the current field directory and lets the operator load one
// ("Use Selected"), delete one ("Delete Record"), turn the recorded path off ("Path Off") or cancel.
//
// Per AAP §0.3.2 the FormGPS "mf" god-object coupling is REMOVED via constructor injection — no "mf",
// no new interface, just plain injected values/delegates:
//   * WinForms : FormRecordPicker(Form callingForm) read mf.currentFieldDirectory, mutated
//                mf.recPath.recList, called mf.recPath.StopDrivingRecordedPath() and mf.FileSaveRecPath(),
//                and hid mf.panelDrag; the buttons carried DialogResult.Yes / DialogResult.Cancel.
//   * Avalonia : FormRecordPickerView(string currentFieldDirectory, CRecordedPath recPath,
//                Action hidePanelDrag, Action saveRecPath) — the field directory, the recorded-path
//                object (mutated in place, exactly like the WinForms mf.recPath), and the two
//                FormGPS-side side-effects (save the recorded path + hide the drag panel) are injected.
//                FileSaveRecPath is NOT a CRecordedPath member (it was a FormGPS method), so it is
//                injected as the saveRecPath Action; StopDrivingRecordedPath() IS a CRecordedPath member
//                and is called on _recPath directly (parity — the same object the caller holds).
// Confirmation / error dialogs route through AgOpenGPS.Views.FormDialogView (ShowQuestionAsync /
// ShowAsync) instead of the WinForms FormDialog.ShowQuestion / FormDialog.Show; the Yes/Cancel outcome
// is returned via Close(bool) / ShowDialog<bool> instead of DialogResult. The corrupt-file event log the
// WinForms handler wrote is preserved by FormDialogView.ShowAsync itself (it logs Error/Warning dialogs
// to the event viewer), so no direct AgLibrary.Logging dependency is taken here.
//
// Cross-platform rules (AAP §0.6.5): no System.Windows.Forms, no System.Drawing, no OpenTK.GLControl;
// every path is built with System.IO.Path.Combine (never a hard-coded '\'); and every numeric parse of
// the .rec payload uses CultureInfo.InvariantCulture, because a comma-decimal locale (de-DE, fr-FR, …)
// would otherwise corrupt the easting/northing/heading/speed values on Linux/macOS. The .rec layout is
// the frozen contract written by IO/RecPathFiles.cs: line 1 = the "$RecPath" header, line 2 = the point
// count, then one "easting,northing,heading,speed,autoBtnState" row per point. See
// MIGRATION_DOCS/TRANSITION_MAP.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the recorded-path picker — a faithful Avalonia reimplementation of the
    /// WinForms <c>FormRecordPicker</c>. Imperative dialog (NO <c>DataContext</c> / <c>x:DataType</c> /
    /// MVVM bindings): every control is addressed by <c>x:Name</c> and the four button handlers are
    /// subscribed programmatically in the constructor (the paired <c>FormRecordPickerView.axaml</c>
    /// deliberately declares no <c>Click</c> attributes), exactly like the sibling code-behind dialogs
    /// (<c>FormDrivePickerView</c> / <c>FormFilePickerView</c>). The list rows are plain record-name
    /// strings the XAML <c>ItemTemplate</c> binds to by reflection (path-less <c>{Binding}</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The injected <see cref="CRecordedPath"/> is mutated in place — "Use Selected" parses the chosen
    /// <c>.rec</c> file into <c>recPath.recList</c> and "Path Off" stops/clears it — exactly mirroring
    /// the WinForms <c>mf.recPath</c>. The dialog result surfaced through <c>ShowDialog&lt;bool&gt;</c> is
    /// <see langword="true"/> when a recorded path was loaded and <see langword="false"/> when the
    /// operator cancelled, turned the path off, or no recorded paths existed.
    /// </para>
    /// </remarks>
    public partial class FormRecordPickerView : Window
    {
        // [XPLAT] Injected replacements for the former FormGPS "mf" back-reference (AAP §0.3.2).

        /// <summary>[XPLAT] The current field directory name (was <c>mf.currentFieldDirectory</c>).</summary>
        private readonly string _currentFieldDirectory;

        /// <summary>
        /// [XPLAT] The recorded-path object (was <c>mf.recPath</c>): its <c>recList</c> is cleared and
        /// re-filled by "Use Selected" and its <c>StopDrivingRecordedPath()</c> is invoked by "Path Off".
        /// The concrete instance is injected so it is mutated in place — parity with the WinForms host.
        /// </summary>
        private readonly CRecordedPath _recPath;

        /// <summary>[XPLAT] Hides the main shell's drag panel (was <c>mf.panelDrag.Visible = false</c>).</summary>
        private readonly Action _hidePanelDrag;

        /// <summary>
        /// [XPLAT] Persists the (now cleared) recorded path (was <c>mf.FileSaveRecPath()</c>, a FormGPS
        /// method — hence an injected callback rather than a <see cref="CRecordedPath"/> member call).
        /// </summary>
        private readonly Action _saveRecPath;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader and the
        /// design-time previewer, both of which instantiate the view without arguments (its absence
        /// raises AVLN3001, which the Release build — <c>TreatWarningsAsErrors</c> — treats as an error).
        /// Loads the markup and subscribes the four button handlers; application code constructs the
        /// dialog through <see cref="FormRecordPickerView(string, CRecordedPath, Action, Action)"/>.
        /// </summary>
        public FormRecordPickerView()
        {
            InitializeComponent();

            // [XPLAT] The WinForms designer wired the button clicks (btnDeleteField.Click,
            // buttonOpenExistingLv.Click, btnTurnOffRecPath.Click) and relied on DialogResult.Yes /
            // DialogResult.Cancel for buttonOpenExistingLv / btnDeleteAB. Reproduced here programmatically
            // because the paired .axaml deliberately declares no Click attributes.
            buttonOpenExistingLv.Click += UseSelected_Click;
            btnDeleteField.Click += DeleteRecord_Click;
            btnTurnOffRecPath.Click += TurnOff_Click;
            btnDeleteAB.Click += Cancel_Click;
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormRecordPicker(Form callingForm)</c> constructor, with
        /// the <c>mf</c> back-reference replaced by the injected field directory, recorded-path object and
        /// two callbacks. Stores the injected state and applies the localized captions (the WinForms
        /// constructor set <c>this.Text</c> and the button/label texts; folded in here).
        /// </summary>
        /// <param name="currentFieldDirectory">The current field directory name (was
        /// <c>mf.currentFieldDirectory</c>); the <c>.rec</c> enumeration is relative to it.</param>
        /// <param name="recPath">The recorded-path object mutated in place (was <c>mf.recPath</c>).</param>
        /// <param name="hidePanelDrag">Hides the main shell's drag panel (was
        /// <c>mf.panelDrag.Visible = false</c>); may be <see langword="null"/>.</param>
        /// <param name="saveRecPath">Persists the recorded path (was the FormGPS method
        /// <c>mf.FileSaveRecPath()</c>); may be <see langword="null"/>.</param>
        public FormRecordPickerView(string currentFieldDirectory, CRecordedPath recPath, Action hidePanelDrag, Action saveRecPath)
            : this()
        {
            _currentFieldDirectory = currentFieldDirectory;
            _recPath = recPath;
            _hidePanelDrag = hidePanelDrag;
            _saveRecPath = saveRecPath;

            // [XPLAT] Verbatim from FormRecordPicker.cs: the dialog is borderless (no title bar), so the
            // caption is applied to Window.Title for taskbar / screen-reader parity.
            Title = gStr.gsRecordedPathPicker;

            // [XPLAT] WinForms ColumnHeader chName.Text was the literal "Record Name" (Designer default).
            chName.Text = "Record Name";

            // [XPLAT] Localized button/label captions (FormRecordPicker.cs constructor):
            // buttonOpenExistingLv.Text = gsUseSelected (the Avalonia caption lives in the named
            // btnOpenText TextBlock), labelPathOff = gsTurnOffRecordedPath, labelDeleteRecord = gsDelete,
            // labelCancel = gsCancel.
            btnOpenText.Text = gStr.gsUseSelected;
            labelPathOff.Text = gStr.gsTurnOffRecordedPath;
            labelDeleteRecord.Text = gStr.gsDelete;
            labelCancel.Text = gStr.gsCancel;
        }

        /// <summary>
        /// [XPLAT] Replaces the WinForms <c>FormRecordPicker_Load</c> handler: enumerates the recorded
        /// paths once the window is shown. Deferred to <see cref="Window.OnOpened"/> (rather than the
        /// constructor) so the empty-list path can show a modal child dialog over an already-shown owner.
        /// Guarded on <see cref="_recPath"/> so the design-time previewer / XAML loader (which use the
        /// parameterless constructor and leave the injected state null) do not run file I/O.
        /// </summary>
        /// <param name="e">The event data.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_recPath != null)
            {
                LoadList();
            }
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>LoadList</c>: lists every <c>.rec</c> file in the current
        /// field directory by its bare name. When none exist it shows the (verbatim, hard-coded English)
        /// "No Recorded Paths" error and closes returning <see langword="false"/> — the cross-platform
        /// equivalent of the WinForms <c>FormDialog.Show(...Error)</c> + <c>Close()</c>.
        /// </summary>
        private async void LoadList()
        {
            var names = new List<string>();

            // [XPLAT] Path.Combine (never a hard-coded '\') over the recompiled cross-platform
            // RegistrySettings.fieldsDirectory static + the injected field directory.
            string fieldDir = Path.Combine(RegistrySettings.fieldsDirectory, _currentFieldDirectory);

            if (Directory.Exists(fieldDir))
            {
                foreach (string file in Directory.GetFiles(fieldDir))
                {
                    // [XPLAT] StringComparison.Ordinal exactly reproduces the WinForms case-sensitive
                    // ".rec" match (the extension is always written lowercase by IO/RecPathFiles.cs) while
                    // being culture-invariant — the parameterless EndsWith(".rec") uses CurrentCulture,
                    // a §0.6.5 hazard on non-invariant locales. "RecPath.txt" (the copy target) ends in
                    // ".txt" and is correctly excluded.
                    if (file.EndsWith(".rec", StringComparison.Ordinal))
                    {
                        // [XPLAT] Strip the path and ".rec" extension exactly as the WinForms ListView did.
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }

            lvLines.ItemsSource = names;

            if (names.Count == 0)
            {
                // [XPLAT] Verbatim WinForms strings (hard-coded English in the source). FormDialogView
                // logs the Error dialog to the event viewer, preserving the WinForms logging behaviour.
                await FormDialogView.ShowAsync("No Recorded Paths", "Create A Path First", DialogSeverity.Error, this);
                Close(false);
            }
        }

        /// <summary>
        /// [XPLAT] "Use Selected" — WinForms <c>btnOpenExistingLv_Click</c>, whose button also carried
        /// <c>DialogResult.Yes</c> (an implicit close Avalonia does not provide). Copies the selected
        /// <c>.rec</c> to <c>RecPath.txt</c> (so it auto-loads when the field is reopened), parses it into
        /// <c>recPath.recList</c>, and then closes <em>explicitly</em> with <see langword="true"/> on both
        /// the success and corrupt-file paths. With no selection it does nothing (stays open), matching
        /// the WinForms <c>if (count &gt; 0)</c> guard.
        /// </summary>
        private async void UseSelected_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] WinForms guarded on lvLines.SelectedItems.Count > 0; with SelectionMode=Single the
            // Avalonia equivalent is a non-null string SelectedItem.
            if (lvLines.SelectedItem is not string name)
            {
                return;
            }

            // [XPLAT] Path.Combine throughout (never a hard-coded '\').
            string fieldDir = Path.Combine(RegistrySettings.fieldsDirectory, _currentFieldDirectory);
            string selectedRecordPath = Path.Combine(fieldDir, name + ".rec");

            // [XPLAT] Copy the selected record over RecPath.txt so it auto-loads next time the field is
            // opened (verbatim from FormRecordPicker.cs; the file was just enumerated, so it exists).
            File.Copy(selectedRecordPath, Path.Combine(fieldDir, "RecPath.txt"), true);

            if (File.Exists(selectedRecordPath))
            {
                using (StreamReader reader = new StreamReader(selectedRecordPath))
                {
                    try
                    {
                        // [XPLAT] .rec layout (IO/RecPathFiles.cs): line 1 = "$RecPath" header (skipped),
                        // line 2 = the point count, then one CSV row per point. This matches the WinForms
                        // reader exactly (it read two lines and parsed the second as the count).
                        reader.ReadLine();
                        string countLine = reader.ReadLine();
                        int numPoints = int.Parse(countLine.Trim(), CultureInfo.InvariantCulture);

                        _recPath.recList.Clear();

                        while (!reader.EndOfStream)
                        {
                            for (int v = 0; v < numPoints; v++)
                            {
                                string line = reader.ReadLine();
                                string[] words = line.Split(',');

                                // [XPLAT] InvariantCulture on every numeric parse (AAP §0.6.5). The
                                // CRecPathPt ctor order is easting, northing, heading, speed, autoBtnState
                                // (Classes/CRecPathPt.cs). bool.Parse is already culture-invariant.
                                CRecPathPt point = new CRecPathPt(
                                    double.Parse(words[0], CultureInfo.InvariantCulture),
                                    double.Parse(words[1], CultureInfo.InvariantCulture),
                                    double.Parse(words[2], CultureInfo.InvariantCulture),
                                    double.Parse(words[3], CultureInfo.InvariantCulture),
                                    bool.Parse(words[4]));

                                _recPath.recList.Add(point);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // [XPLAT] WinForms showed FormDialog.Show(gsRecordedPathFileIsCorrupt,
                        // gsButFieldIsLoaded, Error) and logged the exception; FormDialogView.ShowAsync
                        // performs the equivalent Error-severity event-log write internally.
                        await FormDialogView.ShowAsync(gStr.gsRecordedPathFileIsCorrupt, gStr.gsButFieldIsLoaded, DialogSeverity.Error, this);
                    }
                }
            }

            // [XPLAT] Explicit close replacing the WinForms DialogResult.Yes — on BOTH the success and
            // corrupt-file paths, returning true to the awaiting ShowDialog<bool> caller.
            Close(true);
        }

        /// <summary>
        /// [XPLAT] "Delete Record" — WinForms <c>btnDeleteField_Click</c> (a plain button with no
        /// DialogResult, so it STAYS OPEN). Confirms via a question dialog, deletes the selected
        /// <c>.rec</c> file, then refreshes the list. With no selection or a declined confirmation it does
        /// nothing.
        /// </summary>
        private async void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (lvLines.SelectedItem is not string name)
            {
                return;
            }

            // [XPLAT] Path.Combine (never a hard-coded '\').
            string dir2Delete = Path.Combine(RegistrySettings.fieldsDirectory, _currentFieldDirectory, name + ".rec");

            // [XPLAT] WinForms: if (FormDialog.ShowQuestion(gsDeleteForSure, dir2Delete) == DialogResult.OK)
            // delete; else return. Avalonia returns the OK/Cancel choice as a bool.
            bool confirmed = await FormDialogView.ShowQuestionAsync(gStr.gsDeleteForSure, dir2Delete, DialogSeverity.Warning, this);
            if (!confirmed)
            {
                return;
            }

            File.Delete(dir2Delete);

            // [XPLAT] Refresh the list in place — the dialog stays open (no Close).
            LoadList();
        }

        /// <summary>
        /// [XPLAT] "Path Off" — WinForms <c>btnTurnOffRecPath_Click</c>. Stops driving the recorded path,
        /// clears it, persists the (now empty) recorded path, hides the main shell's drag panel, then
        /// closes returning <see langword="false"/>.
        /// </summary>
        private void TurnOff_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] StopDrivingRecordedPath() + recList are CRecordedPath members, called on the
            // injected instance directly (parity). FileSaveRecPath / panelDrag were FormGPS-side effects,
            // invoked through the injected callbacks (null-safe).
            _recPath.StopDrivingRecordedPath();
            _recPath.recList.Clear();
            _saveRecPath?.Invoke();
            _hidePanelDrag?.Invoke();

            Close(false);
        }

        /// <summary>
        /// [XPLAT] Cancel — WinForms <c>btnDeleteAB</c> carried <c>DialogResult.Cancel</c> (no explicit
        /// handler). Closes returning <see langword="false"/>.
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
