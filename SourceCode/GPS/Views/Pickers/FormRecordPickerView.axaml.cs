// [XPLAT] migrated from net48/WinForms (Forms/Pickers/FormRecordPicker.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the recorded-path picker — a faithful port of the WinForms
    /// <c>FormRecordPicker</c> (FormRecordPicker.cs + FormRecordPicker.Designer.cs). It lists the
    /// <c>.rec</c> recorded paths in the current field and lets the operator use, delete, or turn one
    /// off, or cancel.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The list items are plain record-name strings, matching the template's path-less
    /// <c>{Binding}</c>.
    /// <para>
    /// The original located the recorded paths under the current field directory
    /// (<c>mf.currentFieldDirectory</c>). That single value is injected through
    /// <see cref="FieldDirectory"/>, after which enumeration, the activate-copy (<c>.rec</c> →
    /// <c>RecPath.txt</c>), and the delete-with-confirmation are all self-contained file I/O. The
    /// chosen path is returned via <see cref="SelectedRecordPath"/>; the parse of that file into the
    /// live <c>mf.recPath.recList</c> (of <c>CRecPathPt</c>) and the "turn off" cleanup
    /// (<c>mf.recPath.StopDrivingRecordedPath</c>) remain host-owned — the dialog forwards the latter
    /// through <see cref="TurnOffRequested"/>. No fabricated calls to not-yet-projected services are
    /// made here — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormRecordPickerView : Window
    {
        /// <summary>
        /// [XPLAT] The current field directory name (the original <c>mf.currentFieldDirectory</c>).
        /// The host sets this before showing the dialog; enumeration is relative to it.
        /// </summary>
        public string FieldDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The full path of the <c>.rec</c> file the operator chose (empty if none). The host parses
        /// this into its recorded-path list after the dialog closes positive.
        /// </summary>
        public string SelectedRecordPath { get; private set; } = string.Empty;

        /// <summary>
        /// [XPLAT] Raised when the operator turns the recorded path off; the host stops driving the
        /// recorded path and clears its list (the original <c>mf.recPath</c> cleanup).
        /// </summary>
        public event EventHandler TurnOffRequested;

        public FormRecordPickerView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            buttonOpenExistingLv.Click += OnUseSelectedClick;
            btnDeleteAB.Click += OnCancelClick;
            btnDeleteField.Click += OnDeleteRecordClick;
            btnTurnOffRecPath.Click += OnTurnOffClick;
        }

        // [XPLAT] FormRecordPicker_Load: enumerate the .rec files in the current field.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadList();
        }

        // [XPLAT] LoadList: list every .rec file (by bare name). The original popped a "no paths"
        // dialog and closed when empty; that pre-open guard is host-owned at this checkpoint.
        private void LoadList()
        {
            var names = new List<string>();
            string fieldDir = Path.Combine(RegistrySettings.fieldsDirectory, FieldDirectory ?? string.Empty);

            try
            {
                foreach (string file in Directory.GetFiles(fieldDir))
                {
                    if (file.EndsWith(".rec", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch
            {
                // [XPLAT] Missing/absent field directory yields an empty list (no crash).
            }

            lvLines.ItemsSource = names;
        }

        // [XPLAT] btnOpenExistingLv_Click: activate the selected .rec (copy to RecPath.txt) and close.
        private void OnUseSelectedClick(object sender, RoutedEventArgs e)
        {
            if (!(lvLines.SelectedItem is string selectedRecord) || string.IsNullOrWhiteSpace(selectedRecord))
            {
                return;
            }

            string fieldDir = Path.Combine(RegistrySettings.fieldsDirectory, FieldDirectory ?? string.Empty);
            string selectedRecordPath = Path.Combine(fieldDir, selectedRecord + ".rec");

            try
            {
                if (File.Exists(selectedRecordPath))
                {
                    File.Copy(selectedRecordPath, Path.Combine(fieldDir, "RecPath.txt"), true);
                }
            }
            catch (Exception ex)
            {
                AgLibrary.Logging.Log.EventWriter("Recorded path activate failed: " + ex);
            }

            SelectedRecordPath = selectedRecordPath;
            Close(true);
        }

        // [XPLAT] btnDeleteAB_Click (Cancel): close without selecting.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnDeleteField_Click: confirm, delete the .rec file, then refresh the list.
        private async void OnDeleteRecordClick(object sender, RoutedEventArgs e)
        {
            if (!(lvLines.SelectedItem is string selectedRecord) || string.IsNullOrWhiteSpace(selectedRecord))
            {
                return;
            }

            string dir2Delete = Path.Combine(RegistrySettings.fieldsDirectory, FieldDirectory ?? string.Empty, selectedRecord + ".rec");

            bool confirmed = await FormDialogView.ShowQuestionAsync(this, "Delete For Sure?", dir2Delete);
            if (!confirmed)
            {
                return;
            }

            try
            {
                File.Delete(dir2Delete);
            }
            catch (Exception ex)
            {
                AgLibrary.Logging.Log.EventWriter("Recorded path delete failed: " + ex);
            }

            LoadList();
        }

        // [XPLAT] btnTurnOffRecPath_Click: forward the turn-off, then close.
        private void OnTurnOffClick(object sender, RoutedEventArgs e)
        {
            TurnOffRequested?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
