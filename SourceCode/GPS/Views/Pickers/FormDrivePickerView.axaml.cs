// [XPLAT] migrated from net48/WinForms (Forms/Pickers/FormDrivePicker.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the drive-track field picker — a faithful port of the WinForms
    /// <c>FormDrivePicker</c> (FormDrivePicker.cs + FormDrivePicker.Designer.cs). It lists candidate
    /// fields with their distance from the current position and lets the operator pick one.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The list rows bind (reflection) to the <see cref="DriveItem"/> record the XAML template
    /// references.
    /// <para>
    /// The original received the field-name and distance lists as constructor arguments from the
    /// FormGPS caller (which computes distances from the live position). That contract is preserved
    /// via <see cref="LoadFields"/>, so the dialog is self-contained: it owns the list, the selection
    /// and the result, and never reaches through a god-object. <see cref="OnUseSelectedClick"/>
    /// composes the chosen <c>Field.txt</c> path under <c>RegistrySettings.fieldsDirectory</c> exactly
    /// as the original and exposes it via <see cref="SelectedFileAndDirectory"/> before closing;
    /// <see cref="OnClearClick"/> clears the pending selection. The host assigns the result to its
    /// <c>filePickerFileAndDirectory</c> — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormDrivePickerView : Window
    {
        /// <summary>[XPLAT] One field row: the field name and its distance column.</summary>
        public sealed class DriveItem
        {
            public string Name { get; set; }
            public string Distance { get; set; }
        }

        /// <summary>
        /// The chosen <c>Field.txt</c> path (mirrors the original <c>filePickerFileAndDirectory</c>);
        /// empty when nothing is selected or the operator clears it. The host reads this after close.
        /// </summary>
        public string SelectedFileAndDirectory { get; private set; } = string.Empty;

        public FormDrivePickerView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            btnOpenExistingLv.Click += OnUseSelectedClick;
            btnDeleteAB.Click += OnClearClick;
        }

        /// <summary>
        /// [XPLAT] Populate the list from the caller-supplied CSVs (the original ctor's
        /// <c>_fileList</c> / <c>_distanceList</c>) and set the distance column header units.
        /// </summary>
        public void LoadFields(string fileListCsv, string distanceListCsv, bool isMetric)
        {
            chDistance.Text = isMetric ? "Distance (km)" : "Distance (mi)";

            string[] fileList = (fileListCsv ?? string.Empty).Split(',');
            string[] distanceList = (distanceListCsv ?? string.Empty).Split(',');

            var rows = new List<DriveItem>();
            for (int i = 0; i < fileList.Length; i++)
            {
                rows.Add(new DriveItem
                {
                    Name = fileList[i],
                    Distance = i < distanceList.Length ? distanceList[i] : string.Empty
                });
            }

            lvLines.ItemsSource = rows;
        }

        // [XPLAT] btnOpenExistingLv_Click: compose the Field.txt path for the selection and close.
        private void OnUseSelectedClick(object sender, RoutedEventArgs e)
        {
            if (lvLines.SelectedItem is DriveItem item && !string.IsNullOrWhiteSpace(item.Name))
            {
                SelectedFileAndDirectory = Path.Combine(RegistrySettings.fieldsDirectory, item.Name, "Field.txt");
                Close(true);
            }
        }

        // [XPLAT] btnDeleteAB_Click: clear the pending selection (the original cleared the path).
        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            SelectedFileAndDirectory = string.Empty;
        }
    }
}
