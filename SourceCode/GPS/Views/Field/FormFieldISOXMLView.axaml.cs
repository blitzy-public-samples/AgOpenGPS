// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create New Field From ISO-XML" dialog — a 1:1 behavioural-parity
    /// port of the WinForms <c>FormFieldIsoXml</c> (Forms/Field/FormFieldISOXML.cs +
    /// FormFieldISOXML.Designer.cs). The operator picks a field (and, by extension, one of its guidance
    /// lines) from an imported ISO-XML "Taskdata.xml", names it, optionally appends the current date /
    /// time, then builds a complete AgOpenGPS field (boundaries, headland, guidance lines and the field
    /// files) from the selected ISO-XML field. Tested ISO-XML originates from Agleader, AGCO Valtra and
    /// FendtOne (per the WinForms original).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an IMPERATIVE dialog: there is intentionally no <c>DataContext</c>, no <c>x:DataType</c>
    /// and no MVVM data binding. Every control is addressed by its <c>x:Name</c> from
    /// <c>FormFieldISOXMLView.axaml</c>, exactly mirroring the WinForms constructor +
    /// <c>FormFieldIsoXml_Load</c>. The four button <c>Click</c> handlers keep the ORIGINAL WinForms
    /// method names (<see cref="btnAddDate_Click"/> / <see cref="btnAddTime_Click"/> /
    /// <see cref="btnBuildFields_Click"/> / <see cref="btnSerialCancel_Click"/>) and are wired from the
    /// markup, while the controls whose WinForms events have no 1:1 Avalonia signature
    /// (<c>tboxFieldName</c>'s <c>TextChanged</c> + tap, and <c>tree</c>'s selection) are wired here by
    /// name.
    /// </para>
    /// <para>
    /// [XPLAT] Framework conversions (each tagged <c>// [XPLAT]</c> below):
    /// <list type="bullet">
    ///   <item>WinForms <c>TreeView</c>/<c>TreeNode</c> ⇒ Avalonia <see cref="TreeView"/>/<see cref="TreeViewItem"/>
    ///         (<c>Header</c> + <c>Tag</c> + <c>Items</c>), populated programmatically.</item>
    ///   <item>The synchronous WinForms <c>OpenFileDialog</c> shown in <c>_Load</c> ⇒ the asynchronous
    ///         Avalonia <see cref="IStorageProvider.OpenFilePickerAsync"/>, run from the
    ///         <see cref="Window.Loaded"/> handler (the picker requires a shown <see cref="TopLevel"/>).</item>
    ///   <item><c>FormDialog.Show</c> ⇒ <see cref="FormDialogView.ShowAsync(string, string, DialogSeverity?, Window)"/>;
    ///         <c>mf.YesMessageBox</c> ⇒ the injected <see cref="Func{T, TResult}"/> callback (falling back to
    ///         <see cref="FormYesView"/>); WinForms <c>DialogResult</c> ⇒ <see cref="Window.Close(object)"/>
    ///         (positive <c>true</c> == built, negative <c>false</c> == cancelled).</item>
    ///   <item>The on-screen keyboard (<c>ShowKeyboard</c>) ⇒ <see cref="FormKeyboard"/>;
    ///         <c>SelectionStart</c> ⇒ <see cref="TextBox.CaretIndex"/>; <c>.Enabled</c> ⇒
    ///         <c>.IsEnabled</c>. The WinForms <c>ScreenHelper</c> reposition is intentionally not ported
    ///         (the window is shown centred over its owner).</item>
    /// </list>
    /// </para>
    /// <para>
    /// [XPLAT] The WinForms shell reached every collaborator through the <c>FormGPS</c> ("mf")
    /// god-object. That back-reference is removed: the domain models the build needs
    /// (<see cref="ApplicationModel"/>, <see cref="CBoundary"/>, <see cref="CFieldData"/>,
    /// <see cref="CTrack"/>, <see cref="CNMEA"/>) and the field-file / UI side effects (as
    /// <see cref="Action"/> / <see cref="Func{T, TResult}"/> callbacks) are constructor-injected instead.
    /// No <c>FormGPS</c>/<c>mf</c> reference is held. The ISO-XML parse and the field build are
    /// behaviour-frozen — ported verbatim over <c>AgOpenGPS.Protocols.ISOBUS</c>'s
    /// <c>IsoXmlFieldImporter</c> — and every numeric parse / format uses
    /// <see cref="CultureInfo.InvariantCulture"/> so field files stay byte-identical across
    /// Windows / Linux / macOS.
    /// </para>
    /// </remarks>
    public partial class FormFieldISOXMLView : Window
    {
        // === ISO-XML parse state (names carried verbatim from the WinForms original) ===

        // [XPLAT] The loaded "Taskdata.xml" document (PreserveWhitespace=false, as in the original).
        private XmlDocument iso;

        // [XPLAT] The absolute path of the chosen ISO-XML file (read once by iso.Load).
        private string xmlFilename;

        // [XPLAT] All <PFD> (partfield) elements in the document — one per selectable field.
        private XmlNodeList pfd;

        // [XPLAT] Index into pfd of the field the operator selected in the tree.
        private int idxFieldSelected;

        // [XPLAT] The WGS-84 origin resolved by the importer; written to Field.txt's StartFix line.
        private Wgs84 _origin;

        // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside TextChanged re-raises the event
        // (the WinForms original mutated Text in tboxFieldName_TextChanged for the same reason).
        private bool _suppressTextChanged;

        // [XPLAT] Idempotency guard for the file-open. Avalonia's Loaded can fire again on reattach;
        // the WinForms _Load opened the picker exactly once, so this ensures the same one-shot behaviour.
        private bool _hasOpenedFile;

        // === Constructor-injected collaborators (replace the former FormGPS "mf" back-reference) ===
        // All are null only for a loader/designer-constructed instance (the parameterless ctor below);
        // a production instance is built through the dependency-injected ctor and supplies every one.

        private readonly ApplicationModel _appModel;
        private readonly CBoundary _bnd;
        private readonly CFieldData _fd;
        private readonly CTrack _trk;
        private readonly CNMEA _pn;

        // [XPLAT] Domain/file side effects formerly invoked as mf.JobNew(), mf.FileCreate*(),
        // mf.FileSave*(), mf.CalculateMinMax(), mf.btnABDraw.Visible and mf.FieldMenuButtonEnableDisable(...).
        private readonly Action _jobNew;
        private readonly Action _calculateMinMax;
        private readonly Action _fileSaveBoundary;
        private readonly Action _fileSaveHeadland;
        private readonly Action _fileSaveTracks;
        private readonly Action _fileCreateSections;
        private readonly Action _fileCreateRecPath;
        private readonly Action _fileCreateContour;
        private readonly Action _fileCreateElevation;
        private readonly Action _fileSaveFlags;
        private readonly Action _showAbDraw;
        private readonly Action<bool> _fieldMenuButtonEnableDisable;

        // [XPLAT] mf.YesMessageBox(...) — an awaitable acknowledgement message box. Falls back to a
        // FormYesView when no callback is supplied.
        private readonly Func<string, Task> _yesMessageBox;

        // [XPLAT] Writes the chosen field directory back to the host (the WinForms code set
        // mf.currentFieldDirectory, which the injected FileCreate*/FileSave* side effects read).
        private readonly Action<string> _setCurrentFieldDirectory;

        // [XPLAT] mf.isKeyboardOn — gates the on-screen-keyboard tap flow on tboxFieldName.
        private readonly bool _isKeyboardOn;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader / the design-time
        /// previewer (its absence raises <c>AVLN3001</c>, which the Release <c>TreatWarningsAsErrors</c>
        /// build treats as an error). It is the single construction chokepoint — the dependency-injected
        /// constructor chains to it with <c>: this()</c> — so the events that have no 1:1 Avalonia markup
        /// signature, and the asynchronous file-open, are wired here exactly once. An instance created
        /// directly through this constructor has no injected model, so <see cref="InitializeFromFileAsync"/>
        /// applies the captions and then no-ops (it never opens a file picker without a model).
        /// </summary>
        public FormFieldISOXMLView()
        {
            // [XPLAT] InitializeComponent is emitted by the Avalonia XAML source generator from
            // FormFieldISOXMLView.axaml; it also creates the typed x:Name'd control fields.
            InitializeComponent();

            // [XPLAT] These WinForms events were wired in the Designer. Their Avalonia counterparts have
            // no matching markup signature (TreeView.AfterSelect ⇒ SelectionChanged; the TextBox carries
            // both a TextChanged sanitiser and a tap ⇒ on-screen-keyboard flow), so they are attached
            // here by name (the same division the sibling Field dialogs use).
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            tboxFieldName.AddHandler(Gestures.TappedEvent, TboxFieldName_Tapped);
            tree.SelectionChanged += OnTreeSelectionChanged;

            // [XPLAT] The WinForms _Load opened the file dialog and built the tree synchronously. The
            // Avalonia StorageProvider is async and needs a shown TopLevel, so the open + tree build run
            // from Loaded instead (exactly as the migration guidance prescribes).
            Loaded += async (_, __) => await InitializeFromFileAsync();
        }

        /// <summary>
        /// [XPLAT] Dependency-injected constructor — the cross-platform replacement for the WinForms
        /// <c>FormFieldIsoXml(FormGPS callingForm)</c>. It captures the domain models and the field-file /
        /// UI side effects the build needs, then chains to the parameterless constructor (which performs
        /// <c>InitializeComponent</c> and the event wiring). No <c>FormGPS</c> reference is retained.
        /// </summary>
        /// <param name="appModel">The application model — supplies the local plane for area estimation and is handed to the ISO-XML importer.</param>
        /// <param name="bnd">The boundary model whose <c>bndList</c> receives the imported boundaries / headland.</param>
        /// <param name="fd">The field-data model refreshed after the boundaries are built.</param>
        /// <param name="trk">The track model whose <c>gArr</c> receives the imported guidance lines.</param>
        /// <param name="pn">The NMEA/position model on which the resolved origin defines the local plane.</param>
        /// <param name="jobNew">Starts a fresh field job (was <c>mf.JobNew()</c>).</param>
        /// <param name="calculateMinMax">Recomputes the field extents (was <c>mf.CalculateMinMax()</c>).</param>
        /// <param name="fileSaveBoundary">Persists the boundary file (was <c>mf.FileSaveBoundary()</c>).</param>
        /// <param name="fileSaveHeadland">Persists the headland file (was <c>mf.FileSaveHeadland()</c>).</param>
        /// <param name="fileSaveTracks">Persists the tracks file (was <c>mf.FileSaveTracks()</c>).</param>
        /// <param name="fileCreateSections">Creates the sections file (was <c>mf.FileCreateSections()</c>).</param>
        /// <param name="fileCreateRecPath">Creates the recorded-path file (was <c>mf.FileCreateRecPath()</c>).</param>
        /// <param name="fileCreateContour">Creates the contour file (was <c>mf.FileCreateContour()</c>).</param>
        /// <param name="fileCreateElevation">Creates the elevation file (was <c>mf.FileCreateElevation()</c>).</param>
        /// <param name="fileSaveFlags">Persists the flags file (was <c>mf.FileSaveFlags()</c>).</param>
        /// <param name="showAbDraw">Reveals the AB-draw button once a boundary exists (was <c>mf.btnABDraw.Visible = true</c>).</param>
        /// <param name="fieldMenuButtonEnableDisable">Enables/disables the field menu buttons (was <c>mf.FieldMenuButtonEnableDisable(...)</c>).</param>
        /// <param name="yesMessageBox">Shows an awaitable acknowledgement message box (was <c>mf.YesMessageBox(...)</c>).</param>
        /// <param name="setCurrentFieldDirectory">Writes the chosen field directory back to the host (was <c>mf.currentFieldDirectory = ...</c>).</param>
        /// <param name="isKeyboardOn">Whether the on-screen keyboard is enabled (was <c>mf.isKeyboardOn</c>).</param>
        public FormFieldISOXMLView(
            ApplicationModel appModel,
            CBoundary bnd,
            CFieldData fd,
            CTrack trk,
            CNMEA pn,
            Action jobNew,
            Action calculateMinMax,
            Action fileSaveBoundary,
            Action fileSaveHeadland,
            Action fileSaveTracks,
            Action fileCreateSections,
            Action fileCreateRecPath,
            Action fileCreateContour,
            Action fileCreateElevation,
            Action fileSaveFlags,
            Action showAbDraw,
            Action<bool> fieldMenuButtonEnableDisable,
            Func<string, Task> yesMessageBox,
            Action<string> setCurrentFieldDirectory,
            bool isKeyboardOn)
            : this()
        {
            _appModel = appModel;
            _bnd = bnd;
            _fd = fd;
            _trk = trk;
            _pn = pn;
            _jobNew = jobNew;
            _calculateMinMax = calculateMinMax;
            _fileSaveBoundary = fileSaveBoundary;
            _fileSaveHeadland = fileSaveHeadland;
            _fileSaveTracks = fileSaveTracks;
            _fileCreateSections = fileCreateSections;
            _fileCreateRecPath = fileCreateRecPath;
            _fileCreateContour = fileCreateContour;
            _fileCreateElevation = fileCreateElevation;
            _fileSaveFlags = fileSaveFlags;
            _showAbDraw = showAbDraw;
            _fieldMenuButtonEnableDisable = fieldMenuButtonEnableDisable;
            _yesMessageBox = yesMessageBox;
            _setCurrentFieldDirectory = setCurrentFieldDirectory;
            _isKeyboardOn = isKeyboardOn;
        }

        /// <summary>
        /// [XPLAT] The asynchronous replacement for the body of the WinForms <c>FormFieldIsoXml_Load</c>.
        /// It applies the initial captions / disabled state, prompts the operator for an ISO-XML file via
        /// the Avalonia <see cref="IStorageProvider"/>, loads it, and builds the field tree. It is invoked
        /// from <see cref="Window.Loaded"/>; because that event can fire again on reattach, the actual
        /// file-open is guarded by <see cref="_hasOpenedFile"/> so the picker is shown exactly once — the
        /// one-shot behaviour of the original <c>_Load</c>.
        /// </summary>
        private async Task InitializeFromFileAsync()
        {
            // [XPLAT] Captions / initial state — verbatim from FormFieldIsoXml_Load. Set every time the
            // window loads (cheap and idempotent) so a re-fired Loaded still presents correct text.
            tboxFieldName.Text = "";
            btnBuildFields.IsEnabled = false;
            labelFieldname.Text = gStr.gsEditFieldName;
            Title = gStr.gsCreateNewFromIsoXML;
            labelField.Text = gStr.gsBasedOnField + ":";
            tree.Items.Clear();

            // [XPLAT] A loader/designer-constructed instance has no injected model; there is nothing to
            // import against, so present the captions and stop without opening a picker.
            if (_appModel == null)
            {
                return;
            }

            // [XPLAT] Open the file picker exactly once across the window's lifetime.
            if (_hasOpenedFile)
            {
                return;
            }
            _hasOpenedFile = true;

            // [XPLAT] OpenFileDialog(Filter "XML files (*.XML)|*.XML", InitialDirectory fieldsDirectory)
            // ⇒ StorageProvider.OpenFilePickerAsync. The picker needs a shown TopLevel, which is why this
            // runs from Loaded rather than the constructor.
            TopLevel top = GetTopLevel(this);
            if (top == null)
            {
                // No TopLevel means the window is not actually shown — cancel, mirroring a dismissed dialog.
                Close(false);
                return;
            }

            IStorageFolder startLocation = null;
            try
            {
                // [XPLAT] InitialDirectory = RegistrySettings.fieldsDirectory.
                if (!string.IsNullOrEmpty(RegistrySettings.fieldsDirectory))
                {
                    startLocation = await top.StorageProvider.TryGetFolderFromPathAsync(RegistrySettings.fieldsDirectory);
                }
            }
            catch (Exception ex)
            {
                // A bad/unavailable start path must never abort the picker — log and fall back to default.
                Log.EventWriter("FormFieldISOXML: could not resolve initial directory: " + ex);
            }

            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = gStr.gsCreateNewFromIsoXML,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("XML files")
                    {
                        Patterns = new[] { "*.XML", "*.xml" }
                    }
                },
                SuggestedStartLocation = startLocation
            });

            // [XPLAT] Cancel (no selection) ⇒ the WinForms code closed the form. Close(false) == cancelled.
            if (files == null || files.Count == 0)
            {
                Close(false);
                return;
            }

            // [XPLAT] Resolve a real filesystem path for XmlDocument.Load. TryGetLocalPath is the portable
            // way to obtain it; fall back to the absolute form of the URI's path if it is unavailable.
            xmlFilename = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(xmlFilename))
            {
                xmlFilename = Path.GetFullPath(files[0].Path.LocalPath);
            }

            // [XPLAT] Load the document and build the tree inside one guard. The WinForms original loaded
            // outside its try; folding the Load in changes no frozen output — a malformed file simply
            // surfaces the same error dialog instead of escaping this async-void-style Loaded handler.
            try
            {
                iso = new XmlDocument { PreserveWhitespace = false };
                iso.Load(xmlFilename);
                pfd = iso.GetElementsByTagName("PFD");

                BuildFieldTree();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Failed to create new field: " + ex);
                await FormDialogView.ShowAsync(gStr.gsError, ex.ToString(), DialogSeverity.Error, this);
                return;
            }

            // [XPLAT] btnBuildFields.Enabled = tree.Nodes.Count > 0 (a field must exist to build one).
            btnBuildFields.IsEnabled = tree.Items.Count > 0;
        }

        /// <summary>
        /// [XPLAT] Behaviour-frozen port of the ISO-XML parse loop from <c>FormFieldIsoXml_Load</c>.
        /// For every <c>&lt;PFD&gt;</c> (partfield) it adds one top-level <see cref="TreeViewItem"/>
        /// (header "&lt;name&gt; Area: &lt;ha&gt; Ha", <c>Tag</c> = field index) and one child item per
        /// guidance line discovered through the three layouts the original recognises: ISO v3
        /// <c>GGP → GPN</c>, ISO v2 <c>PLN</c>, and a standalone <c>LSG[@A='5']</c>. The field index is
        /// recorded on the <c>Tag</c> of BOTH the field item AND every child item, so selection can resolve
        /// the owning field directly without walking parents (Avalonia's <see cref="TreeViewItem.Parent"/>
        /// may be the <see cref="TreeView"/> for a logical root). To emulate the recursive WinForms
        /// <c>tree.Sort()</c>, the field items and each field's children are ordered alphabetically by
        /// header before being added.
        /// </summary>
        private void BuildFieldTree()
        {
            // Surface-to-hectare factor, named exactly as in the WinForms original.
            const double SqmToHectares = 0.0001;

            // Collect field items so they can be sorted before being added (Avalonia has no tree.Sort()).
            List<TreeViewItem> fieldItems = new List<TreeViewItem>();

            int index = 0;
            foreach (XmlNode nodePFD in pfd)
            {
                // --- Field name + area (header text) ---
                string fieldName = nodePFD.Attributes?["C"]?.Value ?? "Unnamed";

                // Area: prefer the PFD's declared area attribute "D" (square metres) when it parses and is
                // at least 1; otherwise estimate it from the outer-boundary polygon (shoelace).
                double areaSqm;
                string areaAttr = nodePFD.Attributes?["D"]?.Value;
                if (!string.IsNullOrEmpty(areaAttr)
                    && double.TryParse(areaAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out areaSqm)
                    && areaSqm >= 1)
                {
                    // declared area accepted as-is
                }
                else
                {
                    areaSqm = EstimateAreaFromPln(nodePFD, _appModel);
                }

                double areaHa = Math.Round(areaSqm * SqmToHectares, 2, MidpointRounding.AwayFromZero);
                string fieldLabel = $"{fieldName} Area: {areaHa:0.00} Ha";

                // [XPLAT] TreeNode(fieldLabel){Tag = index} ⇒ TreeViewItem{Header, Tag}.
                TreeViewItem fieldItem = new TreeViewItem
                {
                    Header = fieldLabel,
                    Tag = index
                };

                // Children (guidance lines) gathered then sorted by header to match the recursive tree.Sort().
                List<TreeViewItem> childItems = new List<TreeViewItem>();

                // --- ISO v3 guidance: GGP → GPN → (line label by GPN type) ---
                XmlNodeList ggpNodes = nodePFD.SelectNodes("GGP");
                if (ggpNodes != null)
                {
                    foreach (XmlNode nodeGgp in ggpNodes)
                    {
                        XmlNode gpn = nodeGgp.SelectSingleNode("GPN");
                        if (gpn == null)
                        {
                            continue;
                        }

                        string name = nodeGgp.Attributes?["B"]?.Value ?? "Unnamed";
                        string type = gpn.Attributes?["C"]?.Value ?? "";

                        string childLabel = null;
                        if (type == "1")
                        {
                            childLabel = "AB: " + name;
                        }
                        else if (type == "2")
                        {
                            childLabel = "A+: " + name;
                        }
                        else if (type == "3")
                        {
                            childLabel = "Curve: " + name;
                        }

                        if (childLabel != null)
                        {
                            childItems.Add(MakeChild(childLabel, index));
                        }
                    }
                }

                // --- ISO v2 (PLN) and standalone v3 (LSG[@A='5']) guidance lines ---
                // [XPLAT] Verbatim attribute usage from the WinForms original: for BOTH the PLN nodes and
                // the standalone guidance LSG nodes the name is attribute "B", the line type is attribute
                // "C", and the point count is the number of DIRECT <PNT> children (the original used
                // nodePart.SelectNodes("PNT"), i.e. direct child PNT elements). A standalone LSG is
                // recognised by Name == "LSG" with attribute "A" == "5".
                foreach (XmlNode nodePart in nodePFD.ChildNodes)
                {
                    if (nodePart.Name == "PLN")
                    {
                        string name = nodePart.Attributes?["B"]?.Value ?? "Unnamed";
                        string type = nodePart.Attributes?["C"]?.Value ?? "";
                        int pointCount = CountChildPoints(nodePart);

                        string childLabel = MapLineLabel(type, pointCount, name);
                        if (childLabel != null)
                        {
                            childItems.Add(MakeChild(childLabel, index));
                        }
                    }
                    else if (nodePart.Name == "LSG" && (nodePart.Attributes?["A"]?.Value == "5"))
                    {
                        string name = nodePart.Attributes?["B"]?.Value ?? "Unnamed";
                        string type = nodePart.Attributes?["C"]?.Value ?? "";
                        int pointCount = CountChildPoints(nodePart);

                        string childLabel = MapLineLabel(type, pointCount, name);
                        if (childLabel != null)
                        {
                            childItems.Add(MakeChild(childLabel, index));
                        }
                    }
                }

                // Order this field's children alphabetically, then attach them.
                childItems.Sort(CompareByHeader);
                foreach (TreeViewItem child in childItems)
                {
                    fieldItem.Items.Add(child);
                }

                fieldItems.Add(fieldItem);
                index++;
            }

            // Order the fields alphabetically (emulating the recursive WinForms tree.Sort()) and add them.
            fieldItems.Sort(CompareByHeader);
            foreach (TreeViewItem field in fieldItems)
            {
                tree.Items.Add(field);
            }
        }

        /// <summary>
        /// [XPLAT] Builds a leaf guidance-line <see cref="TreeViewItem"/>. The owning field index is stored
        /// on the child's <c>Tag</c> as well as the field's, so <see cref="OnTreeSelectionChanged"/> can
        /// resolve the field from any selected node without relying on <see cref="TreeViewItem.Parent"/>.
        /// </summary>
        private static TreeViewItem MakeChild(string header, int fieldIndex)
        {
            return new TreeViewItem
            {
                Header = header,
                Tag = fieldIndex
            };
        }

        /// <summary>
        /// [XPLAT] Maps an ISO guidance-line type code + point count to its tree label, exactly as the
        /// WinForms original did for PLN / standalone-LSG nodes: 1 &amp; 2 points ⇒ "AB:", 2 &amp; 1 point
        /// ⇒ "A+:", 3 &amp; &gt;2 points ⇒ "Curve:", 4 &amp; &gt;0 ⇒ "Pivot:", 5 &amp; &gt;0 ⇒ "Spiral:".
        /// Returns <c>null</c> when no rule matches (no tree item is added).
        /// </summary>
        private static string MapLineLabel(string type, int pointCount, string name)
        {
            if (type == "1" && pointCount == 2)
            {
                return "AB: " + name;
            }
            if (type == "2" && pointCount == 1)
            {
                return "A+: " + name;
            }
            if (type == "3" && pointCount > 2)
            {
                return "Curve: " + name;
            }
            if (type == "4" && pointCount > 0)
            {
                return "Pivot: " + name;
            }
            if (type == "5" && pointCount > 0)
            {
                return "Spiral: " + name;
            }
            return null;
        }

        /// <summary>
        /// [XPLAT] Counts the direct <c>&lt;PNT&gt;</c> children of a node — the cross-platform equivalent
        /// of the original's <c>node.SelectNodes("PNT").Count</c>.
        /// </summary>
        private static int CountChildPoints(XmlNode node)
        {
            int count = 0;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == "PNT")
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// [XPLAT] Ordinal-insensitive header comparison used to sort tree items alphabetically, standing
        /// in for the recursive WinForms <c>tree.Sort()</c>.
        /// </summary>
        private static int CompareByHeader(TreeViewItem a, TreeViewItem b)
        {
            string ha = a?.Header as string ?? "";
            string hb = b?.Header as string ?? "";
            return string.Compare(ha, hb, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// [XPLAT] Port of <c>tree_AfterSelect</c>. The WinForms code resolved the owning field by walking
        /// to the selected node's parent (or itself if it was a top-level field). Here every node — field
        /// AND child — carries the field index on its <c>Tag</c>, so the owning field is read directly from
        /// the selected <see cref="TreeViewItem"/> without relying on <see cref="TreeViewItem.Parent"/>
        /// (which, for a logical root, may be the <see cref="TreeView"/> itself). When a valid field is
        /// selected the field-name caption / text box are populated and the Build / Add-Date / Add-Time /
        /// name controls are enabled; otherwise they are disabled.
        /// </summary>
        private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TreeViewItem item = tree.SelectedItem as TreeViewItem;

            // [XPLAT] An empty selection (e.g. when the tree is cleared/rebuilt) disables the controls,
            // matching "enabled = idxFieldSelected >= 0" with no field chosen.
            if (item == null || !(item.Tag is int fieldIndex))
            {
                idxFieldSelected = -1;
                btnBuildFields.IsEnabled = false;
                btnAddDate.IsEnabled = false;
                btnAddTime.IsEnabled = false;
                tboxFieldName.IsEnabled = false;
                return;
            }

            idxFieldSelected = fieldIndex;

            bool enabled = (idxFieldSelected >= 0);

            if (enabled)
            {
                // Verbatim: the field designator is attribute "C" on the selected PFD.
                string fieldName = pfd[idxFieldSelected].Attributes["C"].Value;
                labelField.Text = $"{idxFieldSelected} {fieldName}";

                // Assigning Text re-runs the sanitiser (OnFieldNameTextChanged), exactly as the original
                // WinForms assignment re-ran tboxFieldName_TextChanged.
                tboxFieldName.Text = fieldName;
            }

            btnBuildFields.IsEnabled = enabled;
            btnAddDate.IsEnabled = enabled;
            btnAddTime.IsEnabled = enabled;
            tboxFieldName.IsEnabled = enabled;
        }

        /// <summary>
        /// [XPLAT] Port of <c>tboxFieldName_TextChanged</c>: strips characters disallowed in a field-folder
        /// name via <c>glm.fileRegex</c> and restores the caret. <c>SelectionStart</c> ⇒
        /// <see cref="TextBox.CaretIndex"/>. The <see cref="_suppressTextChanged"/> guard prevents the
        /// programmatic re-assignment from recursing (WinForms only re-raised <c>TextChanged</c> on an
        /// actual value change; the guard yields the identical final text and caret on Avalonia).
        /// </summary>
        private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
            {
                return;
            }

            TextBox textBoxSender = sender as TextBox;
            if (textBoxSender == null)
            {
                return;
            }

            int cursorPosition = textBoxSender.CaretIndex;
            string sanitized = Regex.Replace(textBoxSender.Text ?? "", glm.fileRegex, "");

            _suppressTextChanged = true;
            textBoxSender.Text = sanitized;
            _suppressTextChanged = false;

            // Clamp the restored caret (WinForms SelectionStart clamps automatically; Avalonia does not).
            textBoxSender.CaretIndex = Math.Min(cursorPosition, sanitized.Length);
        }

        /// <summary>
        /// [XPLAT] Port of <c>tboxFieldName_Click</c>. When the on-screen keyboard is enabled the WinForms
        /// code called the <c>ShowKeyboard</c> extension; here the cross-platform <see cref="FormKeyboard"/>
        /// dialog is shown instead. On OK its returned string replaces the text (re-running the sanitiser);
        /// Cancel returns <c>null</c> and leaves the text unchanged. Focus then moves to the Cancel button,
        /// exactly as the original did.
        /// </summary>
        private async void TboxFieldName_Tapped(object sender, TappedEventArgs e)
        {
            if (!_isKeyboardOn)
            {
                return;
            }

            string current = tboxFieldName.Text ?? "";
            FormKeyboard keyboard = new FormKeyboard(current);
            string result = await keyboard.ShowDialog<string>(this);

            if (result != null)
            {
                tboxFieldName.Text = result;
            }

            btnSerialCancel.Focus();
        }

        /// <summary>
        /// [XPLAT] Port of <c>btnAddDate_Click</c>: appends the current date as "yyyy-MM-dd"
        /// (<see cref="CultureInfo.InvariantCulture"/>) to the field name.
        /// </summary>
        private void btnAddDate_Click(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] Port of <c>btnAddTime_Click</c>: appends the current time as "HH-mm"
        /// (<see cref="CultureInfo.InvariantCulture"/>) to the field name.
        /// </summary>
        private void btnAddTime_Click(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] Port of <c>btnSerialCancel_Click</c>. WinForms <c>Close()</c> with no positive
        /// <c>DialogResult</c> ⇒ <c>Close(false)</c> (cancelled — no field was built).
        /// </summary>
        private void btnSerialCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] Port of <c>btnBuildFields_Click</c> — the behaviour-frozen importer-driven field build.
        /// It validates the target directory, runs the <c>IsoXmlFieldImporter</c> exactly as the original
        /// (origin → boundaries sorted by area descending → headland → guidance lines), writes the
        /// "XML Derived" field files, finalises the field, reveals the AB-draw button and updates the field
        /// menu, then closes with a positive result. <c>FormDialog.Show</c>/<c>mf.YesMessageBox</c> become
        /// awaited cross-platform dialogs, and <c>DialogResult = OK; Close()</c> becomes <c>Close(true)</c>.
        /// </summary>
        private async void btnBuildFields_Click(object sender, RoutedEventArgs e)
        {
            // mf.currentFieldDirectory = tboxFieldName.Text.Trim();
            string currentFieldDirectory = tboxFieldName.Text != null ? tboxFieldName.Text.Trim() : "";
            _setCurrentFieldDirectory?.Invoke(currentFieldDirectory);

            string directoryPath = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);

            if (Directory.Exists(directoryPath))
            {
                await FormDialogView.ShowAsync(gStr.gsDirectoryExists, gStr.gsChooseADifferentName, DialogSeverity.Error, this);
                _setCurrentFieldDirectory?.Invoke("");
                return;
            }

            var fieldParts = pfd[idxFieldSelected].ChildNodes;
            var importer = new IsoXmlFieldImporter(fieldParts, _appModel);

            if (!importer.TryGetOrigin(out _origin))
            {
                await ShowYesMessageAsync("Can't calculate center of field. Missing Outer Boundary or AB line.");
                return;
            }

            _jobNew?.Invoke();
            _pn.DefineLocalPlane(_origin, true);

            List<CBoundaryList> boundaries = importer.GetBoundaries();

            // Calculate area for all boundaries first to enable sorting.
            foreach (var boundary in boundaries)
            {
                boundary.CalculateFenceArea(0); // temporary index
            }

            // Sort by area descending (largest first).
            boundaries.Sort((a, b) => b.area.CompareTo(a.area));

            foreach (var boundary in boundaries)
            {
                _bnd.bndList.Add(boundary);
                int idx = _bnd.bndList.Count - 1;
                boundary.CalculateFenceArea(idx);
                boundary.FixFenceLine(idx);
            }

            List<vec3> headland = importer.GetHeadland();
            if (headland.Count > 0 && _bnd.bndList.Count > 0 && _bnd.bndList[0].hdLine.Count == 0)
            {
                _bnd.bndList[0].hdLine.AddRange(headland);
            }

            List<CTrk> guidanceLines = importer.GetGuidanceLines();
            _trk.gArr.AddRange(guidanceLines);

            SaveFieldFiles(directoryPath);
            FinalizeField();

            if (_bnd.bndList.Count > 0)
            {
                _showAbDraw?.Invoke();
            }

            _fieldMenuButtonEnableDisable?.Invoke(
                _bnd.bndList.Count > 0 && _bnd.bndList[0].hdLine.Count > 0);

            // DialogResult = DialogResult.OK; Close();  ⇒  Close(true) (a field was built).
            Close(true);
        }

        /// <summary>
        /// [XPLAT] Replacement for <c>mf.YesMessageBox(...)</c>: shows an awaitable acknowledgement message.
        /// Uses the injected callback when present; otherwise falls back to a <see cref="FormYesView"/>.
        /// </summary>
        private Task ShowYesMessageAsync(string message)
        {
            if (_yesMessageBox != null)
            {
                return _yesMessageBox(message);
            }

            FormYesView form = new FormYesView(message);
            return form.ShowDialog(this);
        }

        /// <summary>
        /// [XPLAT] Port of <c>SaveFieldFiles</c>. Writes the "XML Derived" Field.txt header (all numerics in
        /// <see cref="CultureInfo.InvariantCulture"/> so files are byte-identical across operating systems)
        /// and then triggers the section / recorded-path / contour / elevation / flags file creation through
        /// the injected callbacks (formerly <c>mf.FileCreate*()</c> / <c>mf.FileSaveFlags()</c>).
        /// </summary>
        private void SaveFieldFiles(string directoryPath)
        {
            string fieldFile = Path.Combine(directoryPath, "Field.txt");

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using (StreamWriter writer = new StreamWriter(fieldFile))
            {
                writer.WriteLine(DateTime.Now.ToString("yyyy-MMMM-dd hh:mm:ss tt", CultureInfo.InvariantCulture));
                writer.WriteLine("$FieldDir");
                writer.WriteLine("XML Derived");
                writer.WriteLine("$Offsets");
                writer.WriteLine("0,0");
                writer.WriteLine("Convergence");
                writer.WriteLine("0");
                writer.WriteLine("StartFix");
                writer.WriteLine(_origin.Latitude.ToString(CultureInfo.InvariantCulture) + "," +
                                 _origin.Longitude.ToString(CultureInfo.InvariantCulture));
            }

            _fileCreateSections?.Invoke();
            _fileCreateRecPath?.Invoke();
            _fileCreateContour?.Invoke();
            _fileCreateElevation?.Invoke();
            _fileSaveFlags?.Invoke();
        }

        /// <summary>
        /// [XPLAT] Port of <c>FinalizeField</c>: saves the boundary, rebuilds the turn lines, refreshes the
        /// field-data GUI areas, recomputes the field extents, then saves the headland and tracks. The
        /// <c>mf.*</c> calls become the injected models / callbacks.
        /// </summary>
        private void FinalizeField()
        {
            _fileSaveBoundary?.Invoke();
            _bnd.BuildTurnLines();
            _fd.UpdateFieldBoundaryGUIAreas();
            _calculateMinMax?.Invoke();
            _fileSaveHeadland?.Invoke();
            _fileSaveTracks?.Invoke();
        }

        /// <summary>
        /// [XPLAT] Port of <c>EstimateAreaFromPln</c> — the shoelace fallback used when a PFD has no usable
        /// declared area. It locates the outer-boundary polygon (<c>PLN[@A='1']</c> → <c>LSG[@A='1']</c>),
        /// projects its <c>PNT</c> WGS-84 coordinates (attribute "C" = latitude, "D" = longitude, parsed in
        /// <see cref="CultureInfo.InvariantCulture"/>) onto the local plane, and returns the absolute
        /// shoelace area in square metres. Preserved verbatim — including reading the points BEFORE the
        /// local plane is (re)defined by the build — so the estimate is identical to the WinForms original.
        /// </summary>
        private static double EstimateAreaFromPln(XmlNode nodePfd, ApplicationModel appModel)
        {
            // Find PLN with type "1" (outer boundary).
            foreach (XmlNode nodePln in nodePfd.SelectNodes("PLN"))
            {
                if (nodePln.Attributes["A"]?.Value != "1")
                {
                    continue;
                }

                XmlNode lsg = nodePln.SelectSingleNode("LSG[@A='1']");
                if (lsg == null)
                {
                    continue;
                }

                var pts = lsg.SelectNodes("PNT");
                if (pts.Count < 3)
                {
                    continue;
                }

                var vecs = new vec2[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    double lat, lon;
                    if (!double.TryParse(pts[i].Attributes["C"]?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat))
                    {
                        continue;
                    }
                    if (!double.TryParse(pts[i].Attributes["D"]?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                    {
                        continue;
                    }

                    GeoCoord geo = appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(lat, lon));
                    vecs[i] = new vec2(geo.Easting, geo.Northing);
                }

                // Shoelace formula to compute area (in m²).
                double area = 0;
                for (int i = 0, j = vecs.Length - 1; i < vecs.Length; j = i++)
                {
                    area += (vecs[j].easting + vecs[i].easting) * (vecs[j].northing - vecs[i].northing);
                }
                return Math.Abs(area / 2.0); // m²
            }

            return 0.0;
        }
    }
}
