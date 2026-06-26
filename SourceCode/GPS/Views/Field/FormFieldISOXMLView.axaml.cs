// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldISOXML.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create New Field From ISO-XML" dialog — a faithful port of the
    /// WinForms <c>FormFieldIsoXml</c> (FormFieldISOXML.cs + FormFieldISOXML.Designer.cs). The
    /// operator picks a field (and one of its guidance lines) from an imported "Taskdata.xml", names
    /// it, optionally appends the current date / time, then builds a new AgOpenGPS field from it.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings). The four Button Click handlers
    /// keep the ORIGINAL WinForms method names (<c>btnAddDate_Click</c> / <c>btnAddTime_Click</c> /
    /// <c>btnBuildFields_Click</c> / <c>btnSerialCancel_Click</c>) and are wired from the .axaml.
    /// Behaviour split:
    /// <list type="bullet">
    ///   <item><see cref="btnAddDate_Click"/> / <see cref="btnAddTime_Click"/> append the date / time
    ///         in <see cref="CultureInfo.InvariantCulture"/> — identical to the original.</item>
    ///   <item>The textbox <c>TextChanged</c> reproduces the <c>glm.fileRegex</c> sanitisation; the
    ///         <see cref="TreeView"/> <c>SelectionChanged</c> reproduces <c>tree_AfterSelect</c> by
    ///         enabling the name / date / time inputs once a field node is chosen (the Designer starts
    ///         <c>tboxFieldName</c>, <c>btnAddDate</c>, <c>btnAddTime</c>, <c>btnBuildFields</c>
    ///         disabled).</item>
    ///   <item><see cref="btnSerialCancel_Click"/> closes negative; <see cref="btnBuildFields_Click"/>
    ///         validates the name, publishes it via <see cref="FieldName"/> and closes positive.</item>
    /// </list>
    /// Loading and parsing the "Taskdata.xml" into the field/guidance-line tree, and building the
    /// resulting field, depend on the FormGPS god-object and the ISOBUS importer which are not
    /// projected at this checkpoint; the host opens the file, populates <c>tree</c> and performs the
    /// build once it receives <see cref="FieldName"/> — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormFieldISOXMLView : Window
    {
        private bool _suppressTextChanged;

        /// <summary>The validated, trimmed field name; <c>null</c> when cancelled.</summary>
        public string FieldName { get; private set; }

        public FormFieldISOXMLView()
        {
            InitializeComponent();

            // [XPLAT] These events were wired in the WinForms Designer; wire them here.
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            tree.SelectionChanged += OnTreeSelectionChanged;
        }

        // [XPLAT] btnAddDate_Click: append " yyyy-MM-dd" (InvariantCulture).
        private void btnAddDate_Click(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnAddTime_Click: append " HH-mm" (InvariantCulture).
        private void btnAddTime_Click(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnSerialCancel_Click: close without building a field.
        private void btnSerialCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnBuildFields_Click: validate the name, publish it, close positive.
        private void btnBuildFields_Click(object sender, RoutedEventArgs e)
        {
            string name = (tboxFieldName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            FieldName = name;
            Close(true);
        }

        // [XPLAT] tree_AfterSelect: a chosen field node enables the name / date / time inputs.
        private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = tree.SelectedItem != null;
            tboxFieldName.IsEnabled = hasSelection;
            btnAddDate.IsEnabled = hasSelection;
            btnAddTime.IsEnabled = hasSelection;
            UpdateBuildEnabled();
        }

        // [XPLAT] tboxFieldName_TextChanged: glm.fileRegex sanitisation (caret preserved) + gate build.
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

            UpdateBuildEnabled();
        }

        // [XPLAT] btnBuildFields is enabled only with both a selected field node and a non-empty name.
        private void UpdateBuildEnabled()
        {
            btnBuildFields.IsEnabled =
                tree.SelectedItem != null &&
                !string.IsNullOrEmpty((tboxFieldName.Text ?? string.Empty).Trim());
        }
    }
}
