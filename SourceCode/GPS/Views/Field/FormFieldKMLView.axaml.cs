// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldKML.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create Field From KML" dialog — a faithful port of the WinForms
    /// <c>FormFieldKML</c> (FormFieldKML.cs + FormFieldKML.Designer.cs). The operator types a new
    /// field name (sanitised live against <c>glm.fileRegex</c>), optionally appends the current date
    /// and/or time, picks a Google-Earth KML file whose outer ring becomes the field boundary, then
    /// saves. It is the close sibling of <see cref="FormFieldDirView"/> plus a "Load KML" step.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings). Behaviour split:
    /// <list type="bullet">
    ///   <item><see cref="OnAddDateClick"/> / <see cref="OnAddTimeClick"/> append the date / time in
    ///         <see cref="CultureInfo.InvariantCulture"/> — identical to the original.</item>
    ///   <item>The textbox <c>TextChanged</c> reproduces the <c>glm.fileRegex</c> sanitisation and
    ///         enables <c>btnLoadKML</c> only for a non-empty trimmed name.</item>
    ///   <item><see cref="OnLoadKmlClick"/> replaces the WinForms <c>OpenFileDialog</c> with the
    ///         cross-platform Avalonia <c>StorageProvider</c> file picker, defaults the field name
    ///         from the chosen file name (when blank), publishes the chosen path via
    ///         <see cref="SelectedKmlPath"/> and enables <c>btnSave</c>. Parsing the KML
    ///         <c>&lt;coordinates&gt;</c> ring into a boundary (<c>FindLatLon</c> /
    ///         <c>LoadKMLBoundary</c>) requires the FormGPS local-plane / boundary model and is
    ///         performed by the host once it receives the path — see MIGRATION_DOCS/TRANSITION_MAP.md.</item>
    ///   <item><see cref="OnCancelClick"/> closes negative; <see cref="OnSaveClick"/> validates,
    ///         publishes <see cref="FieldName"/> + <see cref="SelectedKmlPath"/> and closes positive.</item>
    /// </list>
    /// </remarks>
    public partial class FormFieldKMLView : Window
    {
        private bool _suppressTextChanged;

        /// <summary>The validated, trimmed field name; <c>null</c> when cancelled.</summary>
        public string FieldName { get; private set; }

        /// <summary>Absolute path to the KML file the operator selected; <c>null</c> until chosen.</summary>
        public string SelectedKmlPath { get; private set; }

        public FormFieldKMLView()
        {
            InitializeComponent();
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
        }

        // [XPLAT] btnAddDate_Click: append " yyyy-MM-dd" (InvariantCulture).
        private void OnAddDateClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnAddTime_Click: append " HH-mm" (InvariantCulture).
        private void OnAddTimeClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnLoadKML_Click: cross-platform replacement for the WinForms OpenFileDialog.
        private async void OnLoadKmlClick(object sender, RoutedEventArgs e)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "KML files (*.KML)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KML files") { Patterns = new[] { "*.kml", "*.KML" } }
                }
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            SelectedKmlPath = files[0].Path.LocalPath;

            // Mirror the original: when the name is blank, seed it from the KML file name.
            if (string.IsNullOrEmpty(tboxFieldName.Text))
            {
                tboxFieldName.Text = Path.GetFileNameWithoutExtension(SelectedKmlPath);
            }

            // The boundary parse is host-owned; selecting a file is the user-facing prerequisite to save.
            btnSave.IsEnabled = true;
        }

        // [XPLAT] btnSerialCancel_Click: close without creating a field.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnSave_Click: validate name + selected KML, publish them, close positive.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string name = (tboxFieldName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(SelectedKmlPath))
            {
                return;
            }

            FieldName = name;
            Close(true);
        }

        // [XPLAT] tboxFieldName_TextChanged: glm.fileRegex sanitisation (caret preserved) + gate btnLoadKML.
        private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
            {
                return;
            }

            var textBox = (TextBox)sender;
            int caret = textBox.CaretIndex;
            string current = textBox.Text ?? string.Empty;
            string sanitized = Regex.Replace(current, AgOpenGPS.glm.fileRegex, "");

            if (!string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                _suppressTextChanged = true;
                textBox.Text = sanitized;
                textBox.CaretIndex = Math.Min(caret, sanitized.Length);
                _suppressTextChanged = false;
            }

            btnLoadKML.IsEnabled = !string.IsNullOrEmpty((textBox.Text ?? string.Empty).Trim());
        }
    }
}
