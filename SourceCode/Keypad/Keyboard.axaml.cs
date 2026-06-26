// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

/*
 *
 *
 * Special thanks to Ray Bear for pounding out the original program
 * for keys.
 *
 */

namespace Keypad
{
    /// <summary>
    /// On-screen QWERTY/AZERTY/QWERTZ keyboard. Avalonia code-behind for <c>Keyboard.axaml</c>, a verbatim
    /// parity port of the former WinForms <c>Keyboard : GenericKeypad</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] The per-culture glyph maps (French AZERTY plus accents, German QWERTZ plus umlauts, and the
    /// default English QWERTY) for both shift states are ported verbatim from the original WinForms
    /// changeCase() so every emitted character stays byte-identical after migration. Conversions applied:
    /// WinForms Button.Text becomes Avalonia Button.Content; chk_shift.Checked becomes ToggleButton.IsChecked;
    /// Control.Visible becomes Control.IsVisible; and the WinForms double-ampersand mnemonic escape on the
    /// ampersand key becomes a single ampersand (Avalonia does not treat plain string content as an access
    /// key), which preserves the emitted ampersand character. The named keys (a1-e12, btnApos, chk_shift, and
    /// the command keys) are resolved by the Avalonia-generated InitializeComponent before they are used.
    /// </remarks>
    public partial class Keyboard : GenericKeypad
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Keyboard"/> class, loads its compiled XAML (which
        /// also wires up the named keys), and applies the per-culture layout once the control is loaded.
        /// </summary>
        public Keyboard()
        {
            InitializeComponent();
            this.Loaded += Keyboard_Load;
        }

        // [XPLAT] Original WinForms `this.Load += Keyboard_Load`; the identical logic now runs from
        // Avalonia's Loaded event so the named keys are populated before the rows are hidden/sized.
        private void Keyboard_Load(object sender, RoutedEventArgs e)
        {
            changeCase();

            switch (CultureInfo.CurrentCulture.Name)
            {
                case "fr": // ------------------------------------------French

                    break;

                case "de": // -------------------------------------------Deutsch

                    b1.IsVisible = false;
                    b2.IsVisible = false;
                    b3.IsVisible = false;
                    b4.IsVisible = false;
                    b5.IsVisible = false;
                    b6.IsVisible = false;
                    b7.IsVisible = false;
                    b8.IsVisible = false;
                    b9.IsVisible = false;
                    b10.IsVisible = false;
                    b11.IsVisible = false;
                    b12.IsVisible = false;

                    this.Height = 396;
                    break;

                default:
                    b1.IsVisible = false;
                    b2.IsVisible = false;
                    b3.IsVisible = false;
                    b4.IsVisible = false;
                    b5.IsVisible = false;
                    b6.IsVisible = false;
                    b7.IsVisible = false;
                    b8.IsVisible = false;
                    b9.IsVisible = false;
                    b10.IsVisible = false;
                    b11.IsVisible = false;
                    b12.IsVisible = false;

                    c12.IsVisible = false;

                    d12.IsVisible = false;

                    this.Height = 396;
                    break;
            }
        }

        private void Chk_shift_CheckedChanged(object sender, RoutedEventArgs e)
        {
            changeCase();
        }

        private void SendChar(Button btn)
        {
            RaiseButtonPressed(((string)btn.Content)[0]);
        }

        private void BtnClick(object sender, RoutedEventArgs e)
        {
            SendChar((Button)sender);
        }

        private void changeCase()
        {

            switch (CultureInfo.CurrentCulture.Name)
            {
                case "fr": // ------------------------------------------French
                    if (chk_shift.IsChecked == true)
                    {
                        a1.Content = "!";
                        a2.Content = "@";
                        a3.Content = "#";
                        a4.Content = "$";
                        a5.Content = "%";
                        a6.Content = "^";
                        a7.Content = "&";
                        a8.Content = "*";
                        a9.Content = "(";
                        a10.Content = ")";
                        a11.Content = "+";
                        e12.Content = "€";

                        b1.Content = "À";
                        b2.Content = "Â";
                        b3.Content = "Æ";
                        b4.Content = "Ç";
                        b5.Content = "É";
                        b6.Content = "È";
                        b7.Content = "Ê";
                        b8.Content = "Ë";
                        b9.Content = "Ï";
                        b10.Content = "Î";
                        b11.Content = "Ô";
                        b12.Content = "Œ";

                        c1.Content = "A";
                        c2.Content = "Z";
                        c3.Content = "E";
                        c4.Content = "R";
                        c5.Content = "T";
                        c6.Content = "Y";
                        c7.Content = "U";
                        c8.Content = "I";
                        c9.Content = "O";
                        c10.Content = "P";
                        c11.Content = "Ü";
                        c12.Content = "Û";


                        d1.Content = "Q";
                        d2.Content = "S";
                        d3.Content = "D";
                        d4.Content = "F";
                        d5.Content = "G";
                        d6.Content = "H";
                        d7.Content = "J";
                        d8.Content = "K";
                        d9.Content = "L";
                        d10.Content = "M";
                        d11.Content = "Ù";
                        d12.Content = "-";

                        e1.Content = ">";
                        e2.Content = "W";
                        e3.Content = "X";
                        e4.Content = "C";
                        e5.Content = "V";
                        e6.Content = "B";
                        e7.Content = "N";
                        e8.Content = "?";
                        e9.Content = ".";
                        e10.Content = "/";
                        e11.Content = "§";
                        e12.Content = "€";

                        btnApos.Content = "’";
                    }
                    else
                    {
                        a1.Content = "1";
                        a2.Content = "2";
                        a3.Content = "3";
                        a4.Content = "4";
                        a5.Content = "5";
                        a6.Content = "6";
                        a7.Content = "7";
                        a8.Content = "8";
                        a9.Content = "9";
                        a10.Content = "0";
                        a11.Content = "=";
                        e12.Content = "€";

                        b1.Content = "à";
                        b2.Content = "â";
                        b3.Content = "æ";
                        b4.Content = "ç";
                        b5.Content = "é";
                        b6.Content = "è";
                        b7.Content = "ê";
                        b8.Content = "ë";
                        b9.Content = "ï";
                        b10.Content = "î";
                        b11.Content = "ô";
                        b12.Content = "œ";


                        c1.Content = "a";
                        c2.Content = "z";
                        c3.Content = "e";
                        c4.Content = "r";
                        c5.Content = "t";
                        c6.Content = "y";
                        c7.Content = "u";
                        c8.Content = "i";
                        c9.Content = "o";
                        c10.Content = "p";
                        c11.Content = "ü";
                        c12.Content = "û";


                        d1.Content = "q";
                        d2.Content = "s";
                        d3.Content = "d";
                        d4.Content = "f";
                        d5.Content = "g";
                        d6.Content = "h";
                        d7.Content = "j";
                        d8.Content = "k";
                        d9.Content = "l";
                        d10.Content = "m";
                        d11.Content = "ù";
                        d12.Content = "_";

                        e1.Content = "<";
                        e2.Content = "w";
                        e3.Content = "x";
                        e4.Content = "c";
                        e5.Content = "v";
                        e6.Content = "b";
                        e7.Content = "n";
                        e8.Content = ",";
                        e9.Content = ";";
                        e10.Content = ":";
                        e11.Content = ".";
                        e12.Content = "|";

                        btnApos.Content = "’";
                    }

                    break;

                case "de": // -------------------------------------------Deutsch
                    if (chk_shift.IsChecked == true)
                    {
                        a1.Content = "!";
                        a2.Content = "@";
                        a3.Content = "#";
                        a4.Content = "$";
                        a5.Content = "%";
                        a6.Content = "^";
                        a7.Content = "&";
                        a8.Content = "*";
                        a9.Content = "(";
                        a10.Content = ")";
                        a11.Content = "\u00DF";

                        c1.Content = "?";
                        c2.Content = "Q";
                        c3.Content = "W";
                        c4.Content = "E";
                        c5.Content = "R";
                        c6.Content = "T";
                        c7.Content = "Z";
                        c8.Content = "U";
                        c9.Content = "I";
                        c10.Content = "O";
                        c11.Content = "P";
                        c12.Content = "\u00DC";

                        d1.Content = "-";
                        d2.Content = "A";
                        d3.Content = "S";
                        d4.Content = "D";
                        d5.Content = "F";
                        d6.Content = "G";
                        d7.Content = "H";
                        d8.Content = "J";
                        d9.Content = "K";
                        d10.Content = "L";
                        d11.Content = "\u00D6";
                        d12.Content = "\u00C4";

                        e1.Content = "~";
                        e2.Content = "+";
                        e3.Content = "Y";
                        e4.Content = "X";
                        e5.Content = "C";
                        e6.Content = "V";
                        e7.Content = "B";
                        e8.Content = "N";
                        e9.Content = "M";
                        e10.Content = "<";
                        e11.Content = ">";
                        e12.Content = ":";

                        btnApos.Content = "\u0060";
                    }
                    else
                    {
                        c1.Content = "/";
                        c2.Content = "q";
                        c3.Content = "w";
                        c4.Content = "e";
                        c5.Content = "r";
                        c6.Content = "t";
                        c7.Content = "z";
                        c8.Content = "u";
                        c9.Content = "i";
                        c10.Content = "o";
                        c11.Content = "p";
                        c12.Content = "\u00FC";


                        d1.Content = "_";
                        d2.Content = "a";
                        d3.Content = "s";
                        d4.Content = "d";
                        d5.Content = "f";
                        d6.Content = "g";
                        d7.Content = "h";
                        d8.Content = "j";
                        d9.Content = "k";
                        d10.Content = "l";
                        d11.Content = "\u00F6";
                        d12.Content = "\u00E4";

                        e1.Content = "|";
                        e2.Content = "=";
                        e3.Content = "y";
                        e4.Content = "x";
                        e5.Content = "c";
                        e6.Content = "v";
                        e7.Content = "b";
                        e8.Content = "n";
                        e9.Content = "m";
                        e10.Content = ",";
                        e11.Content = ".";
                        e12.Content = ";";

                        a1.Content = "1";
                        a2.Content = "2";
                        a3.Content = "3";
                        a4.Content = "4";
                        a5.Content = "5";
                        a6.Content = "6";
                        a7.Content = "7";
                        a8.Content = "8";
                        a9.Content = "9";
                        a10.Content = "0";
                        a11.Content = "\u00DF";

                        btnApos.Content = "\u00B4";
                    }

                    break;

                default:

                    if (chk_shift.IsChecked == true)
                    {
                        a1.Content = "!";
                        a2.Content = "@";
                        a3.Content = "#";
                        a4.Content = "$";
                        a5.Content = "%";
                        a6.Content = "^";
                        a7.Content = "&";
                        a8.Content = "*";
                        a9.Content = "(";
                        a10.Content = ")";
                        a11.Content = "+";

                        c1.Content = "?";
                        c2.Content = "Q";
                        c3.Content = "W";
                        c4.Content = "E";
                        c5.Content = "R";
                        c6.Content = "T";
                        c7.Content = "Y";
                        c8.Content = "U";
                        c9.Content = "I";
                        c10.Content = "O";
                        c11.Content = "P";
                        c12.Content = ".";

                        d1.Content = "|";
                        d2.Content = "A";
                        d3.Content = "S";
                        d4.Content = "D";
                        d5.Content = "F";
                        d6.Content = "G";
                        d7.Content = "H";
                        d8.Content = "J";
                        d9.Content = "K";
                        d10.Content = "L";
                        d11.Content = "-";

                        e1.Content = "{";
                        e2.Content = "}";
                        e3.Content = "Z";
                        e4.Content = "X";
                        e5.Content = "C";
                        e6.Content = "V";
                        e7.Content = "B";
                        e8.Content = "N";
                        e9.Content = "M";
                        e10.Content = "<";
                        e11.Content = ">";
                        e12.Content = ":";

                        btnApos.Content = "\u0060";
                    }
                    else
                    {
                        a1.Content = "1";
                        a2.Content = "2";
                        a3.Content = "3";
                        a4.Content = "4";
                        a5.Content = "5";
                        a6.Content = "6";
                        a7.Content = "7";
                        a8.Content = "8";
                        a9.Content = "9";
                        a10.Content = "0";
                        a11.Content = "=";

                        c1.Content = "/";
                        c2.Content = "q";
                        c3.Content = "w";
                        c4.Content = "e";
                        c5.Content = "r";
                        c6.Content = "t";
                        c7.Content = "y";
                        c8.Content = "u";
                        c9.Content = "i";
                        c10.Content = "o";
                        c11.Content = "p";

                        d1.Content = "~";
                        d2.Content = "a";
                        d3.Content = "s";
                        d4.Content = "d";
                        d5.Content = "f";
                        d6.Content = "g";
                        d7.Content = "h";
                        d8.Content = "j";
                        d9.Content = "k";
                        d10.Content = "l";
                        d11.Content = "_";

                        e1.Content = "[";
                        e2.Content = "]";
                        e3.Content = "z";
                        e4.Content = "x";
                        e5.Content = "c";
                        e6.Content = "v";
                        e7.Content = "b";
                        e8.Content = "n";
                        e9.Content = "m";
                        e10.Content = ",";
                        e11.Content = ".";
                        e12.Content = ";";

                        btnApos.Content = "\u00B4";
                    }

                    break;
            }
        }

        private void Btn_space_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed(' ');

        private void Btn_backspace_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0008');

        private void Btn_clear_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0005');

        private void Btn_cancel_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0027');

        private void Btn_OK_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('\u0004');
    }
}
