// [XPLAT] migrated from net48/WinForms (Forms/FormInputDialog.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the single-line name-input prompt. Faithful Avalonia reimplementation of
    /// the WinForms <c>FormInputDialog</c> (Forms/FormInputDialog.cs): a title + prompt + text box used to
    /// ask the operator for a field / track / boundary name, with on-the-fly filename sanitisation and
    /// OK / Cancel buttons.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (no view-model). All wired behaviour is self-contained — it depends only on
    /// Avalonia and the cross-platform <c>AgOpenGPS.glm.fileRegex</c> constant (CGLM.cs), never on the
    /// former <c>FormGPS</c> (<c>mf</c>) god-object:
    /// <list type="bullet">
    ///   <item><see cref="OnOkClick"/> returns the trimmed name (or <see langword="null"/> when empty)
    ///   and <see cref="OnCancelClick"/> returns <see langword="null"/> through
    ///   <c>ShowDialog&lt;string&gt;</c> — the cross-platform replacement for the WinForms
    ///   <c>DialogResult.OK</c>/<c>Cancel</c> consumed by <c>FormInputDialog.ShowInput</c>.</item>
    ///   <item>The <c>textBoxInput</c> TextChanged handler reproduces the WinForms filename sanitisation
    ///   (<c>Regex.Replace(text, glm.fileRegex, "")</c>) verbatim, preserving the caret position.</item>
    /// </list>
    /// The WinForms on-screen-keyboard hook (<c>textBoxInput.Click -&gt; ShowKeyboard</c> gated on
    /// <c>FormGPS.isKeyboardOn</c>) is intentionally NOT reproduced here: it depends on the <c>FormGPS</c>
    /// god-object and the <c>ShowKeyboard</c> control extension that the migration relocates into the
    /// extracted services. That deferral is tracked in MIGRATION_DOCS/TRANSITION_MAP.md and is not
    /// fabricated here against state that does not yet exist.
    /// </remarks>
    public partial class FormInputDialogView : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader. Wires the
        /// filename-sanitisation handler. Title / prompt / default text are supplied by the overloads.
        /// </summary>
        public FormInputDialogView()
        {
            InitializeComponent();

            // [XPLAT] FormInputDialog.cs wired textBoxInput.TextChanged in the constructor; reproduced here.
            textBoxInput.TextChanged += OnInputTextChanged;
        }

        /// <summary>
        /// [XPLAT] Mirrors WinForms <c>FormInputDialog(title, prompt, formGPS)</c> (sans the god-object):
        /// assigns the title and prompt captions.
        /// </summary>
        public FormInputDialogView(string title, string prompt) : this()
        {
            labelTitle.Text = title;
            labelPrompt.Text = prompt;
        }

        /// <summary>
        /// [XPLAT] Mirrors WinForms <c>FormInputDialog.ShowInput(title, prompt, formGPS, defaultValue)</c>:
        /// pre-populates the input with <paramref name="defaultValue"/>.
        /// </summary>
        public FormInputDialogView(string title, string prompt, string defaultValue) : this(title, prompt)
        {
            textBoxInput.Text = defaultValue ?? string.Empty;
        }

        /// <summary>
        /// [XPLAT] WinForms textBoxInput.TextChanged: strip filename-illegal characters as the operator
        /// types, restoring the caret. Uses the exact same <c>glm.fileRegex</c> pattern as the original so
        /// the sanitisation behaviour is identical. A self-comparison guard avoids the re-entrancy that
        /// assigning <see cref="TextBox.Text"/> inside its own TextChanged would otherwise cause.
        /// </summary>
        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            int pos = textBoxInput.CaretIndex;
            string current = textBoxInput.Text ?? string.Empty;
            string sanitized = Regex.Replace(current, AgOpenGPS.glm.fileRegex, "");

            if (!string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                textBoxInput.Text = sanitized;
                textBoxInput.CaretIndex = Math.Min(pos, sanitized.Length);
            }
        }

        /// <summary>
        /// [XPLAT] buttonOK -> DialogResult.OK. Returns the trimmed name, or <see langword="null"/> when it
        /// is empty (WinForms <c>ShowInput</c> treated empty input as a cancel), via
        /// <c>ShowDialog&lt;string&gt;</c>.
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            string name = textBoxInput.Text?.Trim() ?? string.Empty;
            Close(string.IsNullOrEmpty(name) ? null : name);
        }

        /// <summary>
        /// [XPLAT] buttonCancel -> DialogResult.Cancel. Returns <see langword="null"/>.
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
