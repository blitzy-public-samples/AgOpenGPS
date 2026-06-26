// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the single-line name-input prompt — a faithful 1:1 behavioural-parity
    /// reimplementation of the WinForms <c>Forms/FormInputDialog</c> (FormInputDialog.cs +
    /// FormInputDialog.Designer.cs). It asks the operator for a name (field / track / boundary / …)
    /// through a title caption, a prompt caption and a single text box, sanitising the entry to a legal
    /// file name as the user types and offering an optional on-screen keyboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an imperative dialog (no view-model, no data binding): the partner markup
    /// <c>FormInputDialogView.axaml</c> declares only the named controls (<c>labelTitle</c>,
    /// <c>labelPrompt</c>, <c>textBoxInput</c>, <c>buttonOK</c>, <c>buttonCancel</c>) and this code-behind
    /// reproduces the original behaviour by addressing them directly — exactly as the WinForms designer +
    /// handlers did. The OK / Cancel buttons carried <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c>
    /// in WinForms; here their <c>Click</c> handlers (wired in the XAML via <c>Click="OnOkClick"</c> /
    /// <c>Click="OnCancelClick"</c>, so they are intentionally NOT also subscribed in code) close the
    /// window returning the entered string (OK) or <see langword="null"/> (Cancel), and the static
    /// <see cref="ShowInputAsync"/> factory applies the original trim / empty-to-null semantics.
    /// </para>
    /// <para>
    /// <b>FormGPS decoupling (AAP §0.6.1).</b> The WinForms dialog reached the <c>FormGPS</c> god-object
    /// for two things: <c>formGPS.isKeyboardOn</c> (whether the touch keyboard is enabled) and the
    /// <c>ShowKeyboard</c> control extension. Both couplings are removed: the caller now supplies a plain
    /// <c>bool keyboardOn</c> flag (sourced from <c>Properties.Settings.Default.setDisplay_isKeyboardOn</c>
    /// at the call site) and the keyboard is the migrated Avalonia <see cref="FormKeyboard"/> window shown
    /// directly from this dialog.
    /// </para>
    /// <para>
    /// Framework conversions (each tagged <c>// [XPLAT]</c> below): the WinForms
    /// <c>TextBox.SelectionStart</c> caret model is replaced by Avalonia's
    /// <see cref="TextBox.CaretIndex"/>; the WinForms <c>TextBox.Click</c> hook becomes a tunneling
    /// <see cref="InputElement.PointerPressedEvent"/> handler (a read-only-safe, reliably-reproduced
    /// click — the same technique the sibling <c>FormNumeric</c> uses); the modal
    /// <c>ShowDialog() == DialogResult.OK</c> pattern becomes the asynchronous
    /// <c>await ShowDialog&lt;string&gt;(owner)</c> model; and the <c>OnPaintBackground</c> GDI border is
    /// purely declarative in the XAML (no GDI here).
    /// </para>
    /// </remarks>
    public partial class FormInputDialogView : Window
    {
        /// <summary>
        /// [XPLAT] Replaces the WinForms <c>FormGPS.isKeyboardOn</c> coupling. When <see langword="true"/>
        /// a tap on <c>textBoxInput</c> opens the on-screen <see cref="FormKeyboard"/>; when
        /// <see langword="false"/> the pointer handler is a no-op and the field is edited with a physical
        /// keyboard only.
        /// </summary>
        private readonly bool keyboardOn;

        /// <summary>
        /// Re-entrancy guard for the on-screen keyboard. The pointer-pressed handler is asynchronous and
        /// modal; this flag ensures a second tap that arrives while the keyboard is already open (or while
        /// it is closing and focus returns to the text box) does not stack a second keyboard dialog.
        /// </summary>
        private bool isShowingKeyboard;

        /// <summary>
        /// [XPLAT] Parity-shaped constructor mirroring the WinForms
        /// <c>FormInputDialog(string title, string prompt, FormGPS formGPS)</c> — with the
        /// <c>FormGPS</c> dependency replaced by the plain <paramref name="keyboardOn"/> flag. It is
        /// <see langword="private"/> on purpose: construction is funnelled through
        /// <see cref="ShowInputAsync"/>, exactly as the WinForms type only exposed the static
        /// <c>ShowInput</c> factories.
        /// </summary>
        /// <param name="title">The bold heading shown in <c>labelTitle</c>.</param>
        /// <param name="prompt">The descriptive prompt shown in <c>labelPrompt</c>.</param>
        /// <param name="keyboardOn">
        /// Whether the on-screen keyboard is offered when the input field is tapped (the cross-platform
        /// replacement for <c>FormGPS.isKeyboardOn</c>).
        /// </param>
        private FormInputDialogView(string title, string prompt, bool keyboardOn)
        {
            InitializeComponent();

            // [XPLAT] Store the keyboard flag (was read from FormGPS.isKeyboardOn) for the pointer handler.
            this.keyboardOn = keyboardOn;

            // [XPLAT] Verbatim from FormInputDialog.cs: labelTitle.Text = title; labelPrompt.Text = prompt.
            // Avalonia's TextBlock.Text accepts null without throwing, so the assignments carry across as-is.
            labelTitle.Text = title;
            labelPrompt.Text = prompt;

            // [XPLAT] FormInputDialog.cs wired textBoxInput.TextChanged in the constructor; reproduced here.
            textBoxInput.TextChanged += OnInputTextChanged;

            // [XPLAT] FormInputDialog.cs wired textBoxInput.Click in the constructor (-> ShowKeyboard when
            // FormGPS.isKeyboardOn). A read-only text box can swallow a plain Click, and Click does not
            // exist as a first-class TextBox event in Avalonia, so a tunneling PointerPressed handler
            // (handledEventsToo) reliably reproduces the tap — the same pattern the sibling FormNumeric
            // uses. The handler itself is gated on keyboardOn, so it is a no-op when the keyboard is off.
            textBoxInput.AddHandler(
                InputElement.PointerPressedEvent,
                OnInputPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>textBoxInput.TextChanged</c>: strip file-name-illegal characters as the
        /// operator types and restore the caret. Uses the exact same <c>glm.fileRegex</c> pattern as the
        /// original (a behaviour-frozen <see cref="string"/> constant in <c>Classes/CGLM.cs</c>), so the
        /// sanitisation result is identical. The original captured <c>SelectionStart</c> before the
        /// replacement and restored it afterwards; the Avalonia equivalent captures and restores
        /// <see cref="TextBox.CaretIndex"/>. A self-comparison guard avoids the re-entrancy that assigning
        /// <see cref="TextBox.Text"/> inside its own <c>TextChanged</c> would otherwise cause, and the
        /// restored caret is clamped into the (possibly shortened) sanitised text so it always stays valid.
        /// </summary>
        /// <param name="sender">The input text box raising the event (unused).</param>
        /// <param name="e">The text-changed event data (unused).</param>
        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            // [XPLAT] WinForms `int pos = textBoxInput.SelectionStart;` -> capture the caret before replacing.
            int pos = textBoxInput.CaretIndex;

            // [XPLAT] Avalonia TextBox.Text is string?; a null-safe snapshot keeps Regex.Replace and the
            // caret math below safe. The non-null case is identical to the WinForms behaviour.
            string current = textBoxInput.Text ?? string.Empty;

            // [XPLAT] Verbatim from FormInputDialog.cs:
            //   textBoxInput.Text = Regex.Replace(textBoxInput.Text, glm.fileRegex, "");
            string sanitized = Regex.Replace(current, glm.fileRegex, "");

            // Only write back (and reposition the caret) when the sanitiser actually changed the text. This
            // prevents the assignment from re-raising TextChanged in an unbounded loop while preserving the
            // original observable behaviour — when nothing illegal was typed, the text and caret are untouched.
            if (!string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                textBoxInput.Text = sanitized;

                // [XPLAT] WinForms `textBoxInput.SelectionStart = pos;` -> restore the caret, clamped into
                // the sanitised text so a removed trailing character can never push the caret out of range.
                textBoxInput.CaretIndex = Math.Min(pos, sanitized.Length);
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>textBoxInput.Click</c> handler (gated on <c>FormGPS.isKeyboardOn</c>):
        /// when the on-screen keyboard is enabled, open the migrated <see cref="FormKeyboard"/> seeded with
        /// the current text and, on confirmation, copy the edited value back into the field — the
        /// cross-platform equivalent of the WinForms <c>textBoxInput.ShowKeyboard(this)</c> extension. The
        /// handler is a no-op when <see cref="keyboardOn"/> is <see langword="false"/> or while a keyboard
        /// is already open (the <see cref="isShowingKeyboard"/> guard). It is <c>async void</c> because it
        /// is an event handler awaiting a modal dialog — the established pattern for the sibling dialogs in
        /// this view layer.
        /// </summary>
        /// <param name="sender">The input text box raising the event (unused).</param>
        /// <param name="e">The pointer-pressed event data (unused).</param>
        private async void OnInputPointerPressed(object sender, PointerPressedEventArgs e)
        {
            // [XPLAT] WinForms `if (_formGPS?.isKeyboardOn == true)` -> the keyboardOn flag; the
            // isShowingKeyboard guard additionally prevents stacking a second modal keyboard on a re-tap.
            if (!keyboardOn || isShowingKeyboard)
            {
                return;
            }

            isShowingKeyboard = true;

            // [XPLAT] WinForms ShowKeyboard flashed the field (BackColor = Red) while the keypad was open and
            // restored it afterwards. Reproduce that editing feedback by capturing the current brush, flashing
            // red, and always restoring it in the finally — capturing/restoring the existing brush (rather
            // than forcing a fixed colour back) keeps the dialog's parity Lavender/Fluent styling intact.
            IBrush previousBackground = textBoxInput.Background;
            textBoxInput.Background = Brushes.Red;
            try
            {
                // [XPLAT] WinForms `textBoxInput.ShowKeyboard(this)` -> show the migrated on-screen keyboard
                // seeded with the current text. FormKeyboard returns the entered string on OK (its
                // ShowDialog<string> result / ReturnString) and null on Cancel.
                var keyboard = new FormKeyboard(textBoxInput.Text ?? string.Empty);
                string result = await keyboard.ShowDialog<string>(this);

                // Commit only on a non-null (accepted) result; a null result is a cancel and leaves the field
                // untouched. The assignment re-triggers OnInputTextChanged, so the committed value is itself
                // sanitised through glm.fileRegex — exactly as typed input is.
                if (result != null)
                {
                    textBoxInput.Text = result;
                }
            }
            finally
            {
                textBoxInput.Background = previousBackground;
                isShowingKeyboard = false;
            }
        }

        /// <summary>
        /// [XPLAT] OK button handler — the cross-platform replacement for the WinForms
        /// <c>buttonOK.DialogResult = DialogResult.OK</c>. Closes the dialog returning the raw entered text
        /// to the awaiting <c>ShowDialog&lt;string&gt;</c> caller; the trim / empty-to-null decision is made
        /// once, centrally, in <see cref="ShowInputAsync"/> (matching the WinForms <c>ShowInput</c>
        /// semantics). Wired from the XAML via <c>Click="OnOkClick"</c>, so it must not also be subscribed
        /// in code (which would fire twice).
        /// </summary>
        /// <param name="sender">The OK button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(textBoxInput.Text);
        }

        /// <summary>
        /// [XPLAT] Cancel button handler — the cross-platform replacement for the WinForms
        /// <c>buttonCancel.DialogResult = DialogResult.Cancel</c>. Closes the dialog returning
        /// <see langword="null"/>, which <see cref="ShowInputAsync"/> reports to its caller as "no value".
        /// Wired from the XAML via <c>Click="OnCancelClick"</c>, so it must not also be subscribed in code.
        /// </summary>
        /// <param name="sender">The Cancel button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(null);
        }

        /// <summary>
        /// [XPLAT] Asynchronous parity replacement for the WinForms
        /// <c>FormInputDialog.ShowInput(title, prompt, formGPS[, defaultValue])</c> static factories. Shows
        /// the prompt modally and returns the operator's trimmed entry, or <see langword="null"/> when the
        /// dialog is cancelled or the (trimmed) entry is empty — the verbatim WinForms semantics:
        /// <c>OK + non-empty trimmed -&gt; string; otherwise null</c>. The <c>FormGPS</c> parameter is
        /// replaced by a plain <paramref name="keyboardOn"/> flag (caller supplies
        /// <c>Properties.Settings.Default.setDisplay_isKeyboardOn</c>) plus an explicit modal
        /// <paramref name="owner"/>; the two WinForms overloads (with / without a default value) collapse
        /// into the optional <paramref name="defaultValue"/> parameter.
        /// </summary>
        /// <param name="title">The bold heading shown at the top of the dialog.</param>
        /// <param name="prompt">The descriptive prompt shown beneath the title.</param>
        /// <param name="keyboardOn">
        /// Whether tapping the input field offers the on-screen keyboard (was <c>FormGPS.isKeyboardOn</c>).
        /// </param>
        /// <param name="owner">
        /// The window the modal dialog is shown over; when <see langword="null"/> the process-wide
        /// <see cref="FormDialogView.Owner"/> fallback is used.
        /// </param>
        /// <param name="defaultValue">
        /// Optional text to pre-populate the input with (parity with the WinForms <c>defaultValue</c>
        /// overload). When <see langword="null"/> the field starts empty.
        /// </param>
        /// <returns>
        /// The trimmed, non-empty entry the operator accepted with OK, or <see langword="null"/> when the
        /// dialog was cancelled or the entry was empty after trimming.
        /// </returns>
        public static async Task<string> ShowInputAsync(
            string title,
            string prompt,
            bool keyboardOn,
            Window owner,
            string defaultValue = null)
        {
            var form = new FormInputDialogView(title, prompt, keyboardOn);

            // [XPLAT] Parity with the WinForms `form.textBoxInput.Text = defaultValue ?? ""` overload — only
            // the defaultValue-carrying ShowInput overload pre-filled the field; the plain overload left it
            // empty. Guarding on non-null preserves that distinction (the assignment also runs the sanitiser).
            if (defaultValue != null)
            {
                form.textBoxInput.Text = defaultValue;
            }

            // [XPLAT] WinForms `form.ShowDialog() == DialogResult.OK` -> async ShowDialog<string>. OnOkClick
            // closes with the entered text; OnCancelClick closes with null. Fall back to the static owner
            // when the caller did not pass one (Avalonia ShowDialog requires an owner window).
            string result = await form.ShowDialog<string>(owner ?? FormDialogView.Owner);

            // [XPLAT] Verbatim from FormInputDialog.ShowInput: on OK, trim the text and return it only when
            // non-empty; otherwise (cancel, or empty/whitespace entry) return null.
            if (result != null)
            {
                string name = result.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
