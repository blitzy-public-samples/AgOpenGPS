// [XPLAT] migrated from net48/WinForms FormNumeric — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// View-model backing <c>FormNumeric.axaml</c>, the touch numeric-keypad entry dialog
    /// that replaces the WinForms <c>FormNumeric</c> (an embedded <c>Keypad.NumKeypad</c>,
    /// a read-only display textbox, and min/max guard labels).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view-model owns the running typed string (<see cref="EntryText"/>) and the
    /// editing rules applied as keypad buttons are pressed (<see cref="AppendKey"/>),
    /// exactly mirroring the original <c>RegisterKeypad1_ButtonPressed</c> handler:
    /// digits append, a single decimal point is allowed (with a leading zero inserted when
    /// appropriate), an optional leading negative sign toggles, backspace removes the last
    /// character, and clear empties the field. The first keypress replaces the pre-filled
    /// current value (type-to-replace), matching the WinForms <c>isFirstKey</c> behaviour.
    /// </para>
    /// <para>
    /// All numeric parsing and formatting use <see cref="CultureInfo.InvariantCulture"/> so
    /// that a locale whose decimal separator is a comma can never corrupt the value
    /// (see AAP §0.6.5). The decimal separator entered by the keypad is therefore always
    /// the invariant <c>'.'</c>.
    /// </para>
    /// <para>
    /// This view-model intentionally owns no OK/Cancel result state. The dialog outcome is
    /// produced by the hosting <c>Window</c> in the code-behind, which calls
    /// <see cref="TryGetResult"/> to validate and read the value before closing. On a
    /// <c>false</c> result the code-behind keeps the dialog open (and may flash/show a
    /// message), mirroring the original validation.
    /// </para>
    /// </remarks>
    public class FormNumericViewModel : ViewModel
    {
        // [XPLAT] Placeholder the original keypad wrote on an out-of-range value; the up/down repeat
        // buttons treat it (like "" and "-") as a zero starting point before stepping.
        private const string ErrorText = "Error";

        private readonly double _min;
        private readonly double _max;

        // When true, the next AppendKey clears the pre-filled current value before applying
        // the key (the WinForms "isFirstKey" type-to-replace behaviour).
        private bool _isFirstKey;

        private string _entryText;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormNumericViewModel"/> class.
        /// </summary>
        /// <param name="min">The inclusive minimum acceptable value.</param>
        /// <param name="max">The inclusive maximum acceptable value.</param>
        /// <param name="currentValue">
        /// The value pre-filled into the entry field when the dialog opens. It is formatted
        /// with <see cref="CultureInfo.InvariantCulture"/>.
        /// </param>
        public FormNumericViewModel(double min, double max, double currentValue)
        {
            _min = min;
            _max = max;

            // Pre-fill the display with the current value; the first keypress replaces it.
            _entryText = currentValue.ToString(CultureInfo.InvariantCulture);
            _isFirstKey = true;
        }

        /// <summary>
        /// Gets the inclusive minimum acceptable value (shown by the dialog's min guard label).
        /// </summary>
        public double Min => _min;

        /// <summary>
        /// Gets the inclusive maximum acceptable value (shown by the dialog's max guard label).
        /// </summary>
        public double Max => _max;

        /// <summary>
        /// Gets or sets the running typed string shown in the entry textbox. Updated by
        /// <see cref="AppendKey"/> as keypad buttons are pressed and bound to the read-only
        /// display in the view.
        /// </summary>
        public string EntryText
        {
            get { return _entryText; }
            set
            {
                if (value != _entryText)
                {
                    _entryText = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Applies a single keypad character to <see cref="EntryText"/>, reproducing the
        /// WinForms <c>RegisterKeypad1_ButtonPressed</c> editing rules.
        /// </summary>
        /// <param name="c">
        /// The keypad character. Recognised values are: any digit (appended),
        /// <c>'.'</c> (a single decimal point, with a leading zero inserted as needed),
        /// <c>'-'</c> (toggles a leading negative sign), <c>'B'</c> or <c>'\b'</c>
        /// (backspace — removes the last character), and <c>'C'</c> (clears the field).
        /// The OK (<c>'K'</c>) and Cancel (<c>'X'</c>) keys are handled by the code-behind
        /// and are ignored here.
        /// </param>
        public void AppendKey(char c)
        {
            string text = EntryText;

            // First keypress replaces the pre-filled value (type-to-replace).
            if (_isFirstKey)
            {
                text = "";
                _isFirstKey = false;
            }

            // [XPLAT] Clear the out-of-range "Error" placeholder as soon as the user enters new values,
            // matching the WinForms RegisterKeypad1_ButtonPressed guard so editing starts from a clean string.
            if (text == ErrorText)
            {
                text = "";
            }

            if (char.IsNumber(c))
            {
                // A digit: append it.
                text += c;
            }
            else if (c == 'B' || c == '\b')
            {
                // Backspace: remove the last character, if any.
                if (text.Length > 0)
                {
                    text = text[..^1];
                }
            }
            else if (c == '.')
            {
                // Decimal point: only one is allowed. '.' is the invariant decimal separator.
                if (!text.Contains('.'))
                {
                    text += ".";

                    // If the decimal is the first character, prefix it with a zero ("." -> "0.").
                    if (text.StartsWith('.'))
                    {
                        text = "0" + text;
                    }

                    // If a negative sign was entered first ("-" -> "-."), insert a zero ("-0.").
                    if (text.StartsWith('-') && text.IndexOf('.') == 1)
                    {
                        text = "-0.";
                    }
                }
            }
            else if (c == '-')
            {
                // Negative sign toggle: prefix if absent, otherwise strip the leading sign.
                if (!text.Contains('-'))
                {
                    text = "-" + text;
                }
                else if (text.StartsWith('-'))
                {
                    text = text[1..];
                }
            }
            else if (c == 'C')
            {
                // Clear the whole entry.
                text = "";
            }

            // 'K' (OK) and 'X' (Cancel) intentionally fall through unchanged: the dialog
            // result is owned by the code-behind, not by the view-model.
            EntryText = text;
        }

        /// <summary>
        /// Steps the value up by one, reproducing the WinForms <c>BtnDistanceUp_MouseDown</c> repeat-button
        /// behaviour: an empty / "-" / "Error" entry is treated as 0, the value is incremented, clamped to
        /// <see cref="Max"/>, and the type-to-replace state is cleared. Parsing/formatting use
        /// <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5 culture safety).
        /// </summary>
        public void Increment()
        {
            if (EntryText == "" || EntryText == "-" || EntryText == ErrorText) EntryText = "0";

            if (!double.TryParse(EntryText, NumberStyles.Float, CultureInfo.InvariantCulture, out double tryNumber))
                tryNumber = 0;

            tryNumber++;
            if (tryNumber > _max) tryNumber = _max;
            EntryText = tryNumber.ToString(CultureInfo.InvariantCulture);
            _isFirstKey = false;
        }

        /// <summary>
        /// Steps the value down by one, reproducing the WinForms <c>BtnDistanceDn_MouseDown</c> repeat-button
        /// behaviour: an empty / "-" / "Error" entry is treated as 0, the value is decremented, clamped to
        /// <see cref="Min"/>, and the type-to-replace state is cleared. Parsing/formatting use
        /// <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5 culture safety).
        /// </summary>
        public void Decrement()
        {
            if (EntryText == "" || EntryText == "-" || EntryText == ErrorText) EntryText = "0";

            if (!double.TryParse(EntryText, NumberStyles.Float, CultureInfo.InvariantCulture, out double tryNumber))
                tryNumber = 0;

            tryNumber--;
            if (tryNumber < _min) tryNumber = _min;
            EntryText = tryNumber.ToString(CultureInfo.InvariantCulture);
            _isFirstKey = false;
        }

        /// <summary>
        /// Attempts to parse <see cref="EntryText"/> as a double and validate it against the
        /// configured <see cref="Min"/>/<see cref="Max"/> range.
        /// </summary>
        /// <param name="value">
        /// When this method returns, contains the parsed value if parsing succeeded;
        /// otherwise zero. The value is meaningful only when the method returns <c>true</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if <see cref="EntryText"/> parsed to a number within the inclusive
        /// <see cref="Min"/>/<see cref="Max"/> range; <c>false</c> if it could not be parsed
        /// or fell outside the range.
        /// </returns>
        public bool TryGetResult(out double value)
        {
            if (double.TryParse(EntryText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                // Range-check against the inclusive bounds, matching the WinForms validation.
                return value >= _min && value <= _max;
            }

            return false;
        }
    }
}
