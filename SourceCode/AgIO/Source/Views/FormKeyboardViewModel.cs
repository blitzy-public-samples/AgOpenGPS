// [XPLAT] migrated from net48/WinForms FormKeyboard — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// MVVM view-model backing <c>FormKeyboard.axaml</c>, the touch on-screen keyboard
    /// dialog used by AgIO to capture free-text entry. It replaces the WinForms
    /// <c>FormKeyboard</c>, which embedded a <c>Keypad.Keyboard</c> control, a
    /// <c>keyboardString</c> TextBox, a <c>string ReturnString</c>, and returned
    /// <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The running entry text lives in <see cref="EntryText"/> (two-way bound to
    /// the entry <c>TextBox</c>). Characters raised by the on-screen keyboard are applied
    /// through <see cref="AppendKey(char)"/>, which reproduces the original
    /// <c>RegisterKeyboard1_ButtonPressed</c> text transitions verbatim. The dialog
    /// lifecycle (the former <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c> + Close)
    /// is owned by the hosting Avalonia <c>Window</c>'s code-behind; on confirmation the
    /// window reads the value via <see cref="GetResult"/> (the WinForms
    /// <c>ReturnString = keyboardString.Text</c>).
    /// </remarks>
    public class FormKeyboardViewModel : ViewModel
    {
        // [XPLAT] Control tokens emitted by Keypad.Keyboard, identical to the WinForms
        // KeyPressEventArgs.KeyChar values the original handler switched on.
        private const char BackspaceKey = '\u0008'; // '\b' — remove the trailing character
        private const char CancelKey = '\u0027';    // dialog cancel (no text change here)
        private const char ClearKey = '\u0005';     // wipe the whole entry
        private const char OkKey = '\u0004';         // dialog confirm (no text change here)

        // [XPLAT] Placeholder the original cleared on the next keypress so a stale "Error"
        // string never survives into the returned value.
        private const string ErrorText = "Error";

        private string _entryText;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormKeyboardViewModel"/> class,
        /// seeding the entry text with the caller's current value.
        /// </summary>
        /// <param name="currentString">
        /// The text to pre-populate, mirroring the WinForms constructor that set
        /// <c>keyboardString.Text = currentString</c>. A <c>null</c> value is treated as
        /// an empty entry so the dialog always opens with a valid, editable string.
        /// </param>
        public FormKeyboardViewModel(string currentString)
        {
            EntryText = currentString ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the running entry text shown in the dialog's <c>TextBox</c> and
        /// mutated by <see cref="AppendKey(char)"/>. Setting the value raises a
        /// <c>NotifyPropertyChanged</c> notification so a two-way binding stays in sync;
        /// the equality guard suppresses redundant notifications that could otherwise
        /// disturb the bound caret position.
        /// </summary>
        public string EntryText
        {
            get => _entryText;
            set
            {
                if (_entryText != value)
                {
                    _entryText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Applies a single character emitted by the on-screen keyboard, reproducing the
        /// WinForms <c>RegisterKeyboard1_ButtonPressed</c> behavior exactly.
        /// </summary>
        /// <param name="c">
        /// The character raised by the keyboard control. Printable characters (including
        /// space) are appended; the backspace and clear control tokens edit the text; and
        /// the cancel / OK control tokens are intentionally left to the hosting window and
        /// do not modify the text.
        /// </param>
        public void AppendKey(char c)
        {
            // [XPLAT] Clear the error placeholder as soon as the user enters new values,
            // matching the original guard so backspace / clear / append all start from a
            // clean string.
            if (EntryText == ErrorText)
            {
                EntryText = string.Empty;
            }

            if (c == BackspaceKey)
            {
                // Backspace — remove the trailing character when there is one.
                if (EntryText.Length > 0)
                {
                    EntryText = EntryText.Remove(EntryText.Length - 1);
                }
            }
            else if (c == CancelKey)
            {
                // [XPLAT] Cancel was DialogResult.Cancel + Close in WinForms. The Avalonia
                // window owns the dialog lifecycle, so the cancel token leaves the entry
                // text untouched here (and must never be appended as a literal glyph).
            }
            else if (c == ClearKey)
            {
                // Clear — wipe the entire entry.
                EntryText = string.Empty;
            }
            else if (c == OkKey)
            {
                // [XPLAT] OK was ReturnString = keyboardString.Text + DialogResult.OK +
                // Close in WinForms. The window owns confirmation and reads the value via
                // GetResult(); the token leaves the entry text untouched here.
            }
            else
            {
                // Any printable character (letters, digits, punctuation, space) is appended.
                EntryText += c;
            }
        }

        /// <summary>
        /// Returns the confirmed entry text, equivalent to the WinForms
        /// <c>ReturnString = keyboardString.Text</c> captured when the OK key was pressed.
        /// </summary>
        /// <returns>The current <see cref="EntryText"/> value.</returns>
        public string GetResult()
        {
            return EntryText;
        }
    }
}
