// [XPLAT] migrated from net48/WinForms NumKeypad.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Keypad
{
    /// <summary>
    /// On-screen numeric keypad. Reimplemented as an Avalonia <see cref="UserControl"/> deriving from
    /// <see cref="GenericKeypad"/>, replacing the former WinForms <c>NumKeypad : GenericKeypad</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Every button click forwards a single character to <see cref="GenericKeypad.RaiseButtonPressed"/>,
    /// preserving the exact token contract of the WinForms original so the GPS/AgIO numeric-entry consumers
    /// behave identically: digits <c>'0'</c>-<c>'9'</c>, <c>'C'</c> (clear), <c>'B'</c> (backspace/CE),
    /// <c>'X'</c> (cancel), <c>'K'</c> (OK/accept), <c>'-'</c> (plus/minus), and <c>'.'</c> (decimal point).
    /// </remarks>
    public partial class NumKeypad : GenericKeypad
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NumKeypad"/> class and loads its compiled XAML.
        /// </summary>
        public NumKeypad()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [XPLAT] Digit keys 0-9 — emit the literal digit characters, identical to the WinForms handlers.
        private void Btn1_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('1');
        private void Btn2_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('2');
        private void Btn3_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('3');
        private void Btn4_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('4');
        private void Btn5_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('5');
        private void Btn6_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('6');
        private void Btn7_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('7');
        private void Btn8_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('8');
        private void Btn9_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('9');
        private void Btn0_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('0');

        // [XPLAT] Command keys — emit the same control tokens the WinForms consumers already interpret.
        private void BtnPlusMinus_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('-');
        private void BtnDecimal_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('.');
        private void BtnClear_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('C');
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('X');
        private void BtnOK_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('K');
        private void BtnBackSpace_Click(object sender, RoutedEventArgs e) => RaiseButtonPressed('B');
    }
}
