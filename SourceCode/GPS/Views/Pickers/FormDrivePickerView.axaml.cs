// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] AgOpenGPS (GPS program) — Drive / Field picker dialog (Avalonia code-behind).
//
// 1:1 BEHAVIOURAL PARITY of the WinForms Forms/Pickers/FormDrivePicker.cs (drive / field selection);
// backing view for FormDrivePickerView.axaml. Per AAP §0.3.2 the FormGPS "mf" god-object coupling is
// REMOVED via constructor injection — no "mf", no new interface, just plain injected values:
//   * WinForms : FormDrivePicker(Form callingForm, string _fileList, string _distanceList) read
//                mf.isMetric for the distance-column units and the handlers wrote the chosen path to
//                mf.filePickerFileAndDirectory (Use Selected) / "" (Cancel) via DialogResult.
//   * Avalonia : FormDrivePickerView(string fileList, string distanceList, bool isMetric) — isMetric
//                is injected (no mf); the chosen Field.txt path is RETURNED through Close(string) /
//                ShowDialog<string> and Cancel returns null, the cross-platform replacement for
//                DialogResult.Yes / .Cancel + mf.filePickerFileAndDirectory.
// The recompiled cross-platform RegistrySettings.fieldsDirectory static is referenced directly for
// parity (it now resolves to the cross-platform config/fields root). No System.Windows.Forms, no
// OpenTK.GLControl, no System.Drawing; the only path building uses System.IO.Path.Combine.
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the drive / field picker — a faithful port of the WinForms
    /// <c>FormDrivePicker</c> (FormDrivePicker.cs + FormDrivePicker.Designer.cs). It lists the candidate
    /// fields with their distance from the current position and lets the operator pick one with
    /// "Use Selected" or back out with Cancel.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings): the controls are addressed by
    /// <c>x:Name</c> and the two button handlers are subscribed programmatically in the constructor
    /// (the paired <c>FormDrivePickerView.axaml</c> deliberately declares no <c>Click</c> attributes),
    /// exactly like the sibling code-behind dialogs (<c>FormFilePickerView</c> /
    /// <c>FormRecordPickerView</c>). The list rows are the private <see cref="DriveItem"/> record the
    /// XAML <c>ItemTemplate</c> binds to by reflection (<c>{Binding Name}</c> / <c>{Binding Distance}</c>).
    /// <para>
    /// The WinForms original received the field-name and distance CSV lists from the FormGPS caller
    /// (which computes the distances from the live position) and read <c>mf.isMetric</c>; all three are
    /// now constructor parameters, so the dialog is self-contained and never reaches through a
    /// god-object. <see cref="OnUseSelectedClick"/> composes the chosen <c>Field.txt</c> path under
    /// <c>RegistrySettings.fieldsDirectory</c> exactly as the original and returns it via
    /// <c>Close(string)</c>; <see cref="OnCancelClick"/> returns <see langword="null"/>. The host that
    /// previously consumed <c>mf.filePickerFileAndDirectory</c> reads the
    /// <c>ShowDialog&lt;string&gt;</c> result instead — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormDrivePickerView : Window
    {
        /// <summary>
        /// [XPLAT] One field row: the field name and its pre-formatted distance column. Bound by the
        /// XAML <c>ItemTemplate</c> through reflection (<c>{Binding Name}</c> / <c>{Binding Distance}</c>),
        /// so the two members are public even though the type itself is private to this dialog.
        /// </summary>
        private sealed class DriveItem
        {
            public string Name { get; set; }
            public string Distance { get; set; }
        }

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader and the
        /// design-time previewer. Loads the markup and subscribes the button handlers; application code
        /// constructs the dialog through <see cref="FormDrivePickerView(string, string, bool)"/>.
        /// </summary>
        public FormDrivePickerView()
        {
            InitializeComponent();

            // [XPLAT] The WinForms designer wired the button clicks programmatically
            // (this.btnOpenExistingLv.Click += ...; this.btnDeleteAB.Click += ...); reproduced here
            // because the paired .axaml deliberately declares no Click attributes.
            btnOpenExistingLv.Click += OnUseSelectedClick;
            btnDeleteAB.Click += OnCancelClick;
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms
        /// <c>FormDrivePicker(Form callingForm, string _fileList, string _distanceList)</c> constructor,
        /// with the <c>mf</c> back-reference replaced by the injected <paramref name="isMetric"/> flag.
        /// Sets the captions and populates the field list from the caller-supplied CSV strings.
        /// </summary>
        /// <param name="fileList">Comma-separated field names (the original <c>_fileList</c>).</param>
        /// <param name="distanceList">Comma-separated, pre-formatted distances (the original
        /// <c>_distanceList</c>), aligned by index with <paramref name="fileList"/>.</param>
        /// <param name="isMetric">Unit preference (the original <c>mf.isMetric</c>): selects the
        /// distance-column header units (km vs mi).</param>
        public FormDrivePickerView(string fileList, string distanceList, bool isMetric)
            : this()
        {
            // [XPLAT] WinForms: this.Text = gStr.gsFieldPicker; — the dialog is borderless (no title
            // bar), so the caption is applied to Window.Title for taskbar / screen-reader parity.
            Title = gStr.gsFieldPicker;

            // [XPLAT] WinForms ColumnHeader chName.Text was the literal "Field Name".
            chName.Text = "Field Name";

            // [XPLAT] Verbatim from FormDrivePicker.cs line 25: the distance header switches on the unit
            // preference (was mf.isMetric, now the injected flag).
            chDistance.Text = isMetric ? "Distance (km)" : "Distance (mi)";

            // [XPLAT] WinForms set btnOpenExistingLv.Text = gStr.gsUseSelected; (the redundant
            // FormFilePicker_Load handler re-set the same text — folded in here, so no Load handler is
            // needed). The Avalonia button's caption lives in the named btnOpenText TextBlock.
            btnOpenText.Text = gStr.gsUseSelected;

            // [XPLAT] Verbatim from FormDrivePicker.cs lines 27-38: split both CSV strings and add one
            // row per field name, attaching the distance value only when the index is in range
            // (i < distances.Length) — the original guard is preserved exactly.
            string[] files = (fileList ?? "").Split(',');
            string[] distances = (distanceList ?? "").Split(',');

            var items = new List<DriveItem>();
            for (int i = 0; i < files.Length; i++)
            {
                items.Add(new DriveItem
                {
                    Name = files[i],
                    Distance = i < distances.Length ? distances[i] : ""
                });
            }

            lvLines.ItemsSource = items;
        }

        /// <summary>
        /// [XPLAT] "Use Selected" button — WinForms <c>btnOpenExistingLv_Click</c>, whose button also
        /// carried <c>DialogResult.Yes</c> so the form closed even with no selection. Composes the
        /// selected field's <c>Field.txt</c> path under <c>RegistrySettings.fieldsDirectory</c> and
        /// returns it through <c>Close(string)</c>; with no usable selection it closes returning
        /// <see langword="null"/> (the cross-platform equivalent of the empty-string sentinel the
        /// original wrote to <c>mf.filePickerFileAndDirectory</c>).
        /// </summary>
        private void OnUseSelectedClick(object sender, RoutedEventArgs e)
        {
            if (lvLines.SelectedItem is DriveItem item && !string.IsNullOrWhiteSpace(item.Name))
            {
                // [XPLAT] Path.Combine (never a hard-coded '\') reproduces
                // Path.Combine(RegistrySettings.fieldsDirectory, <selected name>, "Field.txt").
                Close(Path.Combine(RegistrySettings.fieldsDirectory, item.Name, "Field.txt"));
            }
            else
            {
                Close(null);
            }
        }

        /// <summary>
        /// [XPLAT] Cancel button — WinForms <c>btnDeleteAB_Click</c> + <c>DialogResult.Cancel</c>, which
        /// set <c>mf.filePickerFileAndDirectory = ""</c> and closed the form. Returns
        /// <see langword="null"/> ("no selection"), equivalent to the original empty-string sentinel.
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
