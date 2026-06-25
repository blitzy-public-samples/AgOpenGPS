// [XPLAT] migrated from net48/WinForms Keyboard.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Keypad
{
    /// <summary>
    /// On-screen QWERTY/AZERTY/QWERTZ keyboard. Reimplemented as an Avalonia <see cref="UserControl"/>
    /// deriving from <see cref="GenericKeypad"/>, replacing the former WinForms <c>Keyboard : GenericKeypad</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The per-culture glyph maps (French AZERTY + accents, German QWERTZ + umlauts, default English
    /// QWERTY) for both shift states are ported verbatim from the original <c>changeCase()</c> so the emitted
    /// characters are byte-identical after migration. WinForms <c>Button.Text</c> becomes Avalonia
    /// <c>Button.Content</c>; <c>chk_shift.Checked</c> becomes <c>ToggleButton.IsChecked</c>; the original
    /// <c>Keyboard_Load</c> culture/visibility handling is reproduced in <see cref="ApplyCultureLayout"/>.
    /// Special thanks to Ray Bear for the original key layout.
    /// </remarks>
    public partial class Keyboard : GenericKeypad
    {
        // [XPLAT] Cached row references resolved by x:Name once after XAML load, so changeCase() can assign
        // glyphs without depending on compiler-generated name fields. Index i maps to button {row}{i+1}.
        private Button[] _aRow;
        private Button[] _bRow;
        private Button[] _cRow;
        private Button[] _dRow;
        private Button[] _eRow;
        private Button _btnApos;
        private ToggleButton _chkShift;

        /// <summary>
        /// Initializes a new instance of the <see cref="Keyboard"/> class, loads its compiled XAML, and
        /// applies the initial glyph map and per-culture layout (mirroring the original <c>Keyboard_Load</c>).
        /// </summary>
        public Keyboard()
        {
            InitializeComponent();

            _aRow = ResolveRow("a", 11);
            _bRow = ResolveRow("b", 12);
            _cRow = ResolveRow("c", 12);
            _dRow = ResolveRow("d", 12);
            _eRow = ResolveRow("e", 12);
            _btnApos = this.FindControl<Button>("btnApos");
            _chkShift = this.FindControl<ToggleButton>("chk_shift");

            // [XPLAT] Original Keyboard_Load: paint the keys first, then hide rows per culture.
            changeCase();
            ApplyCultureLayout();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private Button[] ResolveRow(string prefix, int count)
        {
            var row = new Button[count];
            for (int i = 0; i < count; i++)
            {
                row[i] = this.FindControl<Button>(prefix + (i + 1).ToString(CultureInfo.InvariantCulture));
            }

            return row;
        }

        /// <summary>
        /// [XPLAT] Reproduces the original <c>Keyboard_Load</c> culture switch: French shows every row;
        /// German hides the accent (b) row; the default English layout hides the accent row plus the
        /// trailing c12/d12 keys. The original additionally shrank the control to 396 px when the accent
        /// row was hidden; that value is intentionally not reproduced because it clips the bottom command
        /// row — emitted-token parity is unaffected and the host dialog governs final sizing.
        /// </summary>
        private void ApplyCultureLayout()
        {
            switch (CultureInfo.CurrentCulture.Name)
            {
                case "fr": // French — all rows visible.
                    break;

                case "de": // German — hide the accent row.
                    HideRow(_bRow);
                    break;

                default: // English and everything else — hide accent row plus c12 and d12.
                    HideRow(_bRow);
                    _cRow[11].IsVisible = false; // c12
                    _dRow[11].IsVisible = false; // d12
                    break;
            }
        }

        private static void HideRow(Button[] row)
        {
            foreach (var button in row)
            {
                if (button != null)
                {
                    button.IsVisible = false;
                }
            }
        }

        /// <summary>
        /// [XPLAT] Toggle handler for the shift key (WinForms <c>Chk_shift_CheckedChanged</c>); repaints the
        /// glyph map for the new shift state.
        /// </summary>
        private void Chk_shift_CheckedChanged(object sender, RoutedEventArgs e)
        {
            changeCase();
        }

        /// <summary>
        /// [XPLAT] Emits the first character of the pressed key's content, identical to the original
        /// <c>SendChar</c> reading <c>Button.Text[0]</c>.
        /// </summary>
        private void SendChar(Button senderb)
        {
            if (senderb?.Content is string text && text.Length > 0)
            {
                RaiseButtonPressed(text[0]);
            }
        }

        /// <summary>
        /// [XPLAT] Click handler shared by all glyph keys (rows a-e and the apostrophe key), mirroring the
        /// original <c>BtnClick</c> -&gt; <c>SendChar</c> path.
        /// </summary>
        private void BtnClick(object sender, RoutedEventArgs e)
        {
            SendChar(sender as Button);
        }

        /// <summary>
        /// [XPLAT] Assigns the key glyphs for the current culture and shift state. Ported verbatim from the
        /// original <c>changeCase()</c>: only the keys the original assigned per branch are set, so hidden
        /// trailing keys behave identically. The original WinForms <c>"&amp;&amp;"</c> mnemonic escape on the
        /// ampersand key is rendered here as a single <c>"&amp;"</c> because Avalonia does not treat plain
        /// string content as an access-key mnemonic; the emitted character (<c>'&amp;'</c>) is unchanged.
        /// </summary>
        private void changeCase()
        {
            bool shift = _chkShift?.IsChecked == true;

            switch (CultureInfo.CurrentCulture.Name)
            {
                case "fr": // ------------------------------------------French
                    if (shift)
                    {
                        SetRow(_aRow, "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "+");
                        SetRow(_bRow, "\u00C0", "\u00C2", "\u00C6", "\u00C7", "\u00C9", "\u00C8", "\u00CA", "\u00CB", "\u00CF", "\u00CE", "\u00D4", "\u0152");
                        SetRow(_cRow, "A", "Z", "E", "R", "T", "Y", "U", "I", "O", "P", "\u00DC", "\u00DB");
                        SetRow(_dRow, "Q", "S", "D", "F", "G", "H", "J", "K", "L", "M", "\u00D9", "-");
                        SetRow(_eRow, ">", "W", "X", "C", "V", "B", "N", "?", ".", "/", "\u00A7", "\u20AC");
                        SetContent(_btnApos, "\u2019");
                    }
                    else
                    {
                        SetRow(_aRow, "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "=");
                        SetRow(_bRow, "\u00E0", "\u00E2", "\u00E6", "\u00E7", "\u00E9", "\u00E8", "\u00EA", "\u00EB", "\u00EF", "\u00EE", "\u00F4", "\u0153");
                        SetRow(_cRow, "a", "z", "e", "r", "t", "y", "u", "i", "o", "p", "\u00FC", "\u00FB");
                        SetRow(_dRow, "q", "s", "d", "f", "g", "h", "j", "k", "l", "m", "\u00F9", "_");
                        SetRow(_eRow, "<", "w", "x", "c", "v", "b", "n", ",", ";", ":", ".", "|");
                        SetContent(_btnApos, "\u2019");
                    }

                    break;

                case "de": // -------------------------------------------Deutsch
                    if (shift)
                    {
                        SetRow(_aRow, "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "\u00DF");
                        SetRow(_cRow, "?", "Q", "W", "E", "R", "T", "Z", "U", "I", "O", "P", "\u00DC");
                        SetRow(_dRow, "-", "A", "S", "D", "F", "G", "H", "J", "K", "L", "\u00D6", "\u00C4");
                        SetRow(_eRow, "~", "+", "Y", "X", "C", "V", "B", "N", "M", "<", ">", ":");
                        SetContent(_btnApos, "\u0060");
                    }
                    else
                    {
                        SetRow(_aRow, "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "\u00DF");
                        SetRow(_cRow, "/", "q", "w", "e", "r", "t", "z", "u", "i", "o", "p", "\u00FC");
                        SetRow(_dRow, "_", "a", "s", "d", "f", "g", "h", "j", "k", "l", "\u00F6", "\u00E4");
                        SetRow(_eRow, "|", "=", "y", "x", "c", "v", "b", "n", "m", ",", ".", ";");
                        SetContent(_btnApos, "\u00B4");
                    }

                    break;

                default: // ------------------------------------------------English / other
                    if (shift)
                    {
                        SetRow(_aRow, "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "+");
                        SetRow(_cRow, "?", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", ".");
                        SetRow(_dRow, "|", "A", "S", "D", "F", "G", "H", "J", "K", "L", "-");
                        SetRow(_eRow, "{", "}", "Z", "X", "C", "V", "B", "N", "M", "<", ">", ":");
                        SetContent(_btnApos, "\u0060");
                    }
                    else
                    {
                        SetRow(_aRow, "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "=");
                        SetRow(_cRow, "/", "q", "w", "e", "r", "t", "y", "u", "i", "o", "p");
                        SetRow(_dRow, "~", "a", "s", "d", "f", "g", "h", "j", "k", "l", "_");
                        SetRow(_eRow, "[", "]", "z", "x", "c", "v", "b", "n", "m", ",", ".", ";");
                        SetContent(_btnApos, "\u00B4");
                    }

                    break;
            }
        }

        // [XPLAT] Assigns glyphs to the leading keys of a row; trailing keys not supplied are left untouched,
        // matching the original which simply did not assign them in those branches.
        private static void SetRow(Button[] row, params string[] glyphs)
        {
            if (row == null)
            {
                return;
            }

            for (int i = 0; i < glyphs.Length && i < row.Length; i++)
            {
                SetContent(row[i], glyphs[i]);
            }
        }

        private static void SetContent(Button button, string glyph)
        {
            if (button != null)
            {
                button.Content = glyph;
            }
        }

        // [XPLAT] Bottom command-row keys emit the same control tokens as the WinForms original.
        private void Btn_space_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed(' ');

        private void Btn_backspace_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0008');

        private void Btn_clear_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0005');

        private void Btn_cancel_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0027');

        private void Btn_OK_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0004');
    }
}
