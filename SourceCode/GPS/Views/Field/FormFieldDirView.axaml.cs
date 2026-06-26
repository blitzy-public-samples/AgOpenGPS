// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldDir.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create New Field (by name)" dialog — a faithful port of the
    /// WinForms <c>FormFieldDir</c> (FormFieldDir.cs + FormFieldDir.Designer.cs). The operator types
    /// a new field name (sanitised live against <c>glm.fileRegex</c>), optionally appends the current
    /// date and/or time, then saves.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name. The button Click handlers follow the established <c>OnXxxClick</c> convention and are
    /// wired from the .axaml. Behaviour split, mirroring the accepted sibling dialogs:
    /// <list type="bullet">
    ///   <item><see cref="OnAddDateClick"/> / <see cref="OnAddTimeClick"/> append the date / time
    ///         using <see cref="CultureInfo.InvariantCulture"/> — byte-identical to the original
    ///         <c>btnAddDate_Click</c> / <c>btnAddTime_Click</c>.</item>
    ///   <item>The textbox <c>TextChanged</c> reproduces the original <c>glm.fileRegex</c>
    ///         sanitisation (caret preserved) and enables <c>btnSave</c> only for a non-empty trimmed
    ///         name; <c>btnSave</c> starts disabled per the Designer.</item>
    ///   <item><see cref="OnCancelClick"/> closes with a negative result.</item>
    ///   <item><see cref="OnSaveClick"/> validates the name, exposes it via <see cref="FieldName"/>
    ///         and closes with a positive result. The original then created the field directory and
    ///         wrote the initial field files via the FormGPS god-object
    ///         (<c>mf.JobNew()</c>, <c>mf.FileCreateField()</c>, …); that file/IO orchestration is
    ///         owned by the host once it receives <see cref="FieldName"/> — see
    ///         MIGRATION_DOCS/TRANSITION_MAP.md. The on-screen-keyboard tap flow
    ///         (<c>mf.isKeyboardOn</c>) is likewise host-owned.</item>
    /// </list>
    /// </remarks>
    public partial class FormFieldDirView : Window
    {
        // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside TextChanged re-raises the event.
        private bool _suppressTextChanged;

        /// <summary>
        /// The validated, trimmed field name the operator entered. Set by <see cref="OnSaveClick"/>
        /// before the dialog closes with a positive result; <c>null</c> if the dialog was cancelled.
        /// The host creates the field directory / initial files from this value.
        /// </summary>
        public string FieldName { get; private set; }

        public FormFieldDirView()
        {
            InitializeComponent();

            // [XPLAT] tboxFieldName_TextChanged was wired in the WinForms Designer; wire it here.
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
        }

        // [XPLAT] btnAddDate_Click: append " yyyy-MM-dd" (InvariantCulture — cross-platform stable).
        private void OnAddDateClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnAddTime_Click: append " HH-mm" (InvariantCulture).
        private void OnAddTimeClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        // [XPLAT] btnSerialCancel_Click: close without creating a field.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] btnSave_Click: validate (non-empty trimmed name), publish the name, close positive.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string name = (tboxFieldName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            FieldName = name;
            Close(true);
        }

        // [XPLAT] tboxFieldName_TextChanged: strip filesystem-illegal characters with glm.fileRegex,
        // preserve the caret, then enable btnSave only when the trimmed name is non-empty.
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

            btnSave.IsEnabled = !string.IsNullOrEmpty((textBox.Text ?? string.Empty).Trim());
        }
    }
}
