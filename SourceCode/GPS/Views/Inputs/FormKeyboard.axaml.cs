// [XPLAT] migrated from net48/WinForms (Forms/Inputs/FormKeyboard.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Keypad;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the on-screen QWERTY keyboard text-entry dialog, hosting the migrated
    /// <see cref="Keyboard"/> UserControl. This is a faithful 1:1 behavioural port of the WinForms
    /// <c>FormKeyboard : Form</c> (Forms/Inputs/FormKeyboard.cs): the dialog is IMPERATIVE (no MVVM),
    /// driving the named <c>keyboardString</c> text box directly through the on-screen key broadcast.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Keyboard"/> control raises <see cref="GenericKeypad.ButtonPressed"/> carrying a
    /// single <see cref="KeypadKeyPressedEventArgs.KeyChar"/>. The control tokens are ported verbatim
    /// from the WinForms original: backspace <c>'\u0008'</c>, clear <c>'\u0005'</c>, cancel
    /// <c>'\u0027'</c>, OK <c>'\u0004'</c>; every other character is inserted at the caret.
    /// </para>
    /// <para>
    /// The WinForms <c>TextBox.SelectionStart</c> caret API becomes Avalonia's
    /// <see cref="TextBox.CaretIndex"/> (the established idiom across the migrated GPS views). The
    /// outcome is surfaced through <see cref="Window.Close(object)"/> — <c>Close(true)</c> on OK
    /// (value via <see cref="ReturnString"/>) and <c>Close(false)</c> on Cancel — the cross-platform
    /// replacement for the WinForms <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c>.
    /// </para>
    /// </remarks>
    public partial class FormKeyboard : Window
    {
        // [XPLAT] On-screen Keyboard control tokens (Keypad.Keyboard.RaiseButtonPressed), ported verbatim
        // from the WinForms RegisterKeyboard1_ButtonPressed so emitted characters stay byte-identical.
        private const char BackspaceKey = '\u0008';
        private const char CancelKey = '\u0027';
        private const char ClearKey = '\u0005';
        private const char OkKey = '\u0004';

        /// <summary>
        /// Gets the confirmed entry text after the dialog closes with <c>true</c> (the WinForms
        /// <c>ReturnString</c> property, preserved for caller parity).
        /// </summary>
        public string ReturnString { get; private set; }

        /// <summary>
        /// Parameterless constructor required by Avalonia's compiled-XAML runtime loader (keeps the
        /// avares resource reachable / avoids AVLN3001). Also wires the on-screen keyboard and the
        /// caret-navigation buttons. Production code constructs via <see cref="FormKeyboard(string)"/>.
        /// </summary>
        public FormKeyboard()
        {
            InitializeComponent();

            // [XPLAT] WinForms wired keyboard1.ButtonPressed += RegisterKeyboard1_ButtonPressed and the
            // btnCharLeft/btnCharRight Click handlers in the designer; the named fields are created by
            // the Avalonia source generator from FormKeyboard.axaml, so we attach the same handlers here.
            keyboard1.ButtonPressed += RegisterKeyboard1_ButtonPressed;
            btnCharLeft.Click += BtnCharLeft_Click;
            btnCharRight.Click += BtnCharRight_Click;
        }

        /// <summary>
        /// [XPLAT] Mirrors the WinForms <c>FormKeyboard(string currentString)</c> constructor: seeds the
        /// editable text box with the current value and retitles the dialog "Enter a Value".
        /// </summary>
        /// <param name="currentString">The initial text to edit.</param>
        public FormKeyboard(string currentString)
            : this()
        {
            Title = "Enter a Value";
            keyboardString.Text = currentString ?? string.Empty;
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormKeyboard_Load</c>: places the caret at the end of the seeded text, gives
        /// the on-screen keyboard focus, and grows the window for the taller French (fr) accent layout.
        /// </summary>
        /// <param name="e">The routed-event payload.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            keyboardString.CaretIndex = keyboardString.Text?.Length ?? 0;
            keyboard1.Focus();

            // [XPLAT] The French AZERTY layout carries an extra accent row, so the original grew the
            // window to 575 px for "fr" and used 500 px otherwise (the .axaml default).
            Height = CultureInfo.CurrentCulture.Name == "fr" ? 575 : 500;
        }

        /// <summary>
        /// [XPLAT] Ports <c>RegisterKeyboard1_ButtonPressed</c>: applies the pressed character to the
        /// text box using caret-relative editing, or completes the dialog on the OK / Cancel tokens.
        /// </summary>
        /// <param name="sender">The on-screen keyboard; unused.</param>
        /// <param name="e">The pressed-key payload carrying <see cref="KeypadKeyPressedEventArgs.KeyChar"/>.</param>
        private void RegisterKeyboard1_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            string text = keyboardString.Text ?? string.Empty;

            // A prior validation error left the literal "Error" in the box; clear it before editing.
            if (text == gStr.gsError)
            {
                text = string.Empty;
                keyboardString.Text = text;
            }

            if (e.KeyChar == BackspaceKey)
            {
                if (text.Length > 0)
                {
                    int selectionIndex = keyboardString.CaretIndex;
                    if (selectionIndex > 0)
                    {
                        // Remove the character to the LEFT of the caret and step the caret back one.
                        keyboardString.Text = text.Remove(selectionIndex - 1, 1);
                        SetCaret(selectionIndex - 1);
                        keyboardString.Focus();
                    }
                    else
                    {
                        // Caret already at the start: nothing to delete (parity quirk of the original,
                        // which nudged the caret to position 1 without removing a character).
                        SetCaret(1);
                    }
                }
            }
            else if (e.KeyChar == CancelKey)
            {
                // WinForms: DialogResult.Cancel + Close().
                Close(false);
                return;
            }
            else if (e.KeyChar == ClearKey)
            {
                keyboardString.Text = string.Empty;
            }
            else if (e.KeyChar == OkKey)
            {
                // WinForms: ReturnString = text; DialogResult.OK + Close().
                ReturnString = keyboardString.Text ?? string.Empty;
                Close(true);
                return;
            }
            else
            {
                // Insert the typed character AT the caret and advance the caret past it.
                string insertText = e.KeyChar.ToString();
                int selectionIndex = keyboardString.CaretIndex;
                if (selectionIndex < 0) selectionIndex = 0;
                if (selectionIndex > text.Length) selectionIndex = text.Length;
                keyboardString.Text = text.Insert(selectionIndex, insertText);
                SetCaret(selectionIndex + insertText.Length);
            }

            keyboardString.Focus();
        }

        /// <summary>[XPLAT] Ports <c>btnCharLeft_Click</c>: move the caret one character left (clamped to 0).</summary>
        /// <param name="sender">The left-arrow button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void BtnCharLeft_Click(object sender, RoutedEventArgs e)
        {
            int spot = keyboardString.CaretIndex - 1;
            if (spot < 0) spot = 0;
            SetCaret(spot);
            keyboardString.Focus();
        }

        /// <summary>[XPLAT] Ports <c>btnCharRight_Click</c>: move the caret one character right (clamped to length).</summary>
        /// <param name="sender">The right-arrow button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void BtnCharRight_Click(object sender, RoutedEventArgs e)
        {
            int length = keyboardString.Text?.Length ?? 0;
            int spot = keyboardString.CaretIndex + 1;
            if (spot > length) spot = length;
            SetCaret(spot);
            keyboardString.Focus();
        }

        // [XPLAT] Sets the caret to a clamped index. Avalonia's TextBox.CaretIndex replaces the WinForms
        // SelectionStart/SelectionLength=0 pair (input arrives only via the on-screen keys, so there is
        // never a live text selection to collapse).
        private void SetCaret(int index)
        {
            int length = keyboardString.Text?.Length ?? 0;
            if (index < 0) index = 0;
            if (index > length) index = length;
            keyboardString.CaretIndex = index;
        }
    }
}
