// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormRecordName.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Enter Recorded-Path Name" dialog — a faithful, fully
    /// self-contained port of the WinForms <c>FormRecordName</c> (FormRecordName.cs +
    /// FormRecordName.Designer.cs). The operator types a name (sanitised live against
    /// <c>glm.fileRegex</c>), optionally appends the current date and/or time via the two
    /// check-boxes, then saves. The resulting file name is exposed through <see cref="Filename"/>.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name. The .axaml declares no event handlers, so every handler is wired programmatically in
    /// the constructor with <c>+=</c> — mirroring the WinForms Designer wiring. Behaviour is a 1:1
    /// reproduction of the original:
    /// <list type="bullet">
    ///   <item><see cref="OnFieldNameTextChanged"/> reproduces the original
    ///         <c>tboxFieldName_TextChanged</c>: <c>glm.fileRegex</c> sanitisation with the caret
    ///         preserved (WinForms <c>SelectionStart</c> → Avalonia <c>CaretIndex</c>),
    ///         <c>buttonSave</c> enabled only for a non-empty trimmed name, and
    ///         <c>labelFilename</c> set to the trimmed name.</item>
    ///   <item><see cref="OnSaveClick"/> reproduces <c>buttonSave_Click</c> exactly: when
    ///         <c>checkBoxRecordAddDate</c> / <c>checkBoxRecordAddTime</c> are checked it appends
    ///         " yyyy-MM-dd" / " HH-mm" (<see cref="CultureInfo.InvariantCulture"/> — cross-platform
    ///         stable), publishes the value through <see cref="Filename"/> and closes positive.</item>
    ///   <item><see cref="OnCancelClick"/> reproduces <c>buttonRecordCancel_Click</c> (close
    ///         negative).</item>
    /// </list>
    /// The original <c>tboxFieldName_Click</c> opened the on-screen keyboard only when
    /// <c>mf.isKeyboardOn</c>; that keyboard flow depends on the FormGPS god-object and is host-owned
    /// — see MIGRATION_DOCS/TRANSITION_MAP.md. No fabricated calls are made here.
    /// </remarks>
    public partial class FormRecordNameView : Window
    {
        // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside TextChanged re-raises the event.
        private bool _suppressTextChanged;

        /// <summary>
        /// The validated file name the operator entered (trimmed, with the optional date/time
        /// suffixes appended on save). Mirrors the original public <c>filename</c> field. Empty until
        /// <see cref="OnSaveClick"/> runs; the dialog closes positive only when a name was entered.
        /// </summary>
        public string Filename { get; private set; } = string.Empty;

        public FormRecordNameView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            buttonSave.Click += OnSaveClick;
            buttonRecordCancel.Click += OnCancelClick;
        }

        // [XPLAT] FormRecordName_Load: buttonSave disabled, labelFilename empty, focus the textbox.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            buttonSave.IsEnabled = false;
            labelFilename.Text = string.Empty;
            tboxFieldName.Focus();
        }

        // [XPLAT] tboxFieldName_TextChanged: glm.fileRegex sanitise (caret preserved), enable Save
        // only for a non-empty trimmed name, and mirror the trimmed name into labelFilename.
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

            string trimmed = (textBox.Text ?? string.Empty).Trim();
            buttonSave.IsEnabled = !string.IsNullOrEmpty(trimmed);
            labelFilename.Text = trimmed;
        }

        // [XPLAT] buttonSave_Click: append optional date/time, publish Filename, close positive.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string result = (labelFilename.Text ?? string.Empty);

            if (checkBoxRecordAddDate.IsChecked == true)
            {
                result += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (checkBoxRecordAddTime.IsChecked == true)
            {
                result += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
            }

            Filename = result;
            Close(true);
        }

        // [XPLAT] buttonRecordCancel_Click: close without recording a name.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
