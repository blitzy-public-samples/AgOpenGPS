// [XPLAT] migrated from net48/WinForms (Forms/Pickers/FormFilePicker.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the field picker — a faithful port of the WinForms
    /// <c>FormFilePicker</c> (FormFilePicker.cs + FormFilePicker.Designer.cs). It shows every field
    /// with its distance and boundary-area columns, lets the operator sort by cycling the columns,
    /// pick one, or delete one.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The list rows bind (reflection) to the <see cref="FileItem"/> record the XAML template
    /// references (Col0 / Col1 / Col2).
    /// <para>
    /// The self-contained presentation behaviour is reproduced exactly: the three-way
    /// <see cref="_order"/> column-cycling (<see cref="OnSortClick"/> →
    /// <see cref="UpdateListView"/> + <see cref="UpdateColumnHeaders"/>), the selection result, and
    /// the delete-with-confirmation (<see cref="OnDeleteFieldClick"/> →
    /// <c>FormDialogView.ShowQuestionAsync</c> + <c>Directory.Delete</c>). The distance column
    /// requires the live GPS position and the area units require the unit preference, both of which
    /// live on the FormGPS god-object in the original; the host therefore supplies the rows through
    /// <see cref="LoadFields"/> (the same dependency-inversion the AAP §0.3.2 mandates), and the
    /// chosen <c>Field.txt</c> path is returned through <see cref="SelectedFileAndDirectory"/>. No
    /// fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormFilePickerView : Window
    {
        /// <summary>[XPLAT] Caller-supplied field data: name, distance text, boundary-area text.</summary>
        public sealed class FileRow
        {
            public string Name { get; set; }
            public string Distance { get; set; }
            public string Area { get; set; }
        }

        /// <summary>[XPLAT] One display row over the three template columns, plus a back-reference.</summary>
        public sealed class FileItem
        {
            public string Col0 { get; set; }
            public string Col1 { get; set; }
            public string Col2 { get; set; }
            public FileRow Row { get; set; }
        }

        private readonly List<FileRow> _rows = new List<FileRow>();
        private int _order;

        /// <summary>
        /// The chosen <c>Field.txt</c> path (mirrors the original <c>filePickerFileAndDirectory</c>);
        /// empty when nothing is selected or the operator clears it.
        /// </summary>
        public string SelectedFileAndDirectory { get; private set; } = string.Empty;

        public FormFilePickerView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            btnByDistance.Click += OnSortClick;
            btnOpenExistingLv.Click += OnUseSelectedClick;
            btnDeleteAB.Click += OnClearClick;
            btnDeleteField.Click += OnDeleteFieldClick;
        }

        /// <summary>
        /// [XPLAT] Populate the picker from caller-supplied rows (the host computes distance/area from
        /// the live position and unit preference). Resets the sort order and rebuilds the list.
        /// </summary>
        public void LoadFields(IReadOnlyList<FileRow> rows)
        {
            _rows.Clear();
            if (rows != null)
            {
                _rows.AddRange(rows);
            }

            _order = 0;
            UpdateListView();
        }

        // [XPLAT] UpdateListView: project each row onto the three columns for the current sort order.
        private void UpdateListView()
        {
            var items = new List<FileItem>(_rows.Count);
            foreach (FileRow row in _rows)
            {
                items.Add(BuildItem(row, _order));
            }

            lvLines.ItemsSource = items;
            UpdateColumnHeaders();
        }

        // [XPLAT] GetFieldNames: rearrange (Name, Distance, Area) per the cycling order.
        private static FileItem BuildItem(FileRow row, int order)
        {
            if (order == 1)
            {
                return new FileItem { Col0 = row.Distance, Col1 = row.Name, Col2 = row.Area, Row = row };
            }

            if (order == 2)
            {
                return new FileItem { Col0 = row.Area, Col1 = row.Name, Col2 = row.Distance, Row = row };
            }

            return new FileItem { Col0 = row.Name, Col1 = row.Distance, Col2 = row.Area, Row = row };
        }

        // [XPLAT] UpdateColumnHeaders: relabel the three headers per the cycling order.
        private void UpdateColumnHeaders()
        {
            if (_order == 1)
            {
                chName.Text = "Distance";
                chDistance.Text = "Field";
                chArea.Text = "Area";
            }
            else if (_order == 2)
            {
                chName.Text = "Area";
                chDistance.Text = "Field";
                chArea.Text = "Distance";
            }
            else
            {
                chName.Text = "Field";
                chDistance.Text = "Distance";
                chArea.Text = "Area";
            }
        }

        // [XPLAT] btnByDistance_Click: cycle the sort order 0 → 1 → 2 → 0 and rebuild.
        private void OnSortClick(object sender, RoutedEventArgs e)
        {
            _order = (_order + 1) % 3;
            UpdateListView();
        }

        // [XPLAT] btnOpenExistingLv_Click: compose the Field.txt path for the selection and close.
        private void OnUseSelectedClick(object sender, RoutedEventArgs e)
        {
            if (lvLines.SelectedItem is FileItem item && item.Row != null)
            {
                string fieldName = item.Row.Name;
                if (string.IsNullOrWhiteSpace(fieldName) || fieldName == "---")
                {
                    return;
                }

                SelectedFileAndDirectory = Path.Combine(RegistrySettings.fieldsDirectory, fieldName, "Field.txt");
                Close(true);
            }
        }

        // [XPLAT] btnDeleteAB_Click: clear the pending selection.
        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            SelectedFileAndDirectory = string.Empty;
        }

        // [XPLAT] btnDeleteField_Click: confirm, then delete the field directory and refresh the list.
        private async void OnDeleteFieldClick(object sender, RoutedEventArgs e)
        {
            if (!(lvLines.SelectedItem is FileItem item) || item.Row == null)
            {
                return;
            }

            string fieldName = item.Row.Name;
            if (string.IsNullOrWhiteSpace(fieldName) || fieldName == "---")
            {
                return;
            }

            string dir2Delete = Path.Combine(RegistrySettings.fieldsDirectory, fieldName);

            // [XPLAT] Wrap the path for readability — parity with the original 45-char insertion.
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

            bool confirmed = await FormDialogView.ShowQuestionAsync(this, "Delete For Sure?", multiLineDir);
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

            _rows.Remove(item.Row);
            UpdateListView();
        }
    }
}
