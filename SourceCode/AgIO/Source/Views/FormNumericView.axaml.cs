// [XPLAT] migrated from net48/WinForms Forms/FormNumeric.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Keypad;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Touch numeric-keypad entry dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormNumeric : Form</c>. It hosts the shared
    /// <see cref="NumKeypad"/> control, a read-only display bound to a <see cref="FormNumericViewModel"/>,
    /// min/max guard labels, and up/down repeat buttons.
    /// </summary>
    /// <remarks>
    /// Behaviour ported 1:1 from the WinForms original:
    /// <list type="bullet">
    /// <item>The up/down buttons call <see cref="FormNumericViewModel.Increment"/>/<see cref="FormNumericViewModel.Decrement"/>
    /// (the former <c>BtnDistanceUp_MouseDown</c>/<c>BtnDistanceDn_MouseDown</c>), stepping by one and clamping to range.</item>
    /// <item>Keypad characters drive <see cref="FormNumericViewModel.AppendKey(char)"/> (the former
    /// <c>RegisterKeypad1_ButtonPressed</c> editing rules).</item>
    /// <item>OK (<c>'K'</c>) validates against the inclusive min/max range: on success it confirms the
    /// dialog (<c>Close(true)</c>, value via <see cref="ResultValue"/>); on a range violation it shows
    /// "Error" and reddens the violated bound's label, exactly as the WinForms handler did.</item>
    /// <item>Cancel (<c>'X'</c>) cancels the dialog (<c>Close(false)</c>).</item>
    /// </list>
    /// All parsing/formatting use <see cref="CultureInfo.InvariantCulture"/> (AAP §0.6.5 culture safety).
    /// </remarks>
    public partial class FormNumericView : Window
    {
        private const char OkKey = 'K';
        private const char CancelKey = 'X';
        private const string ErrorText = "Error";

        private FormNumericViewModel _viewModel;
        private TextBlock _lblMin;
        private TextBlock _lblMax;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormNumericView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to <paramref name="viewModel"/> and subscribes to the numeric keypad.
        /// </summary>
        /// <param name="viewModel">The view-model holding the running entry text and min/max bounds.</param>
        public FormNumericView(FormNumericViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        /// <summary>
        /// Gets the confirmed numeric value after the dialog closes with <c>true</c> (the WinForms
        /// <c>ReturnValue</c>).
        /// </summary>
        public double ResultValue { get; private set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _lblMin = this.FindControl<TextBlock>("lblMin");
            _lblMax = this.FindControl<TextBlock>("lblMax");

            var keypad = this.FindControl<NumKeypad>("keypad1");
            if (keypad != null)
            {
                keypad.ButtonPressed += Keypad_ButtonPressed;
            }
        }

        // [XPLAT] WinForms BtnDistanceUp_MouseDown -> step up by one, clamped to Max.
        private void OnIncrementClick(object sender, RoutedEventArgs e)
        {
            _viewModel?.Increment();
        }

        // [XPLAT] WinForms BtnDistanceDn_MouseDown -> step down by one, clamped to Min.
        private void OnDecrementClick(object sender, RoutedEventArgs e)
        {
            _viewModel?.Decrement();
        }

        private void Keypad_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (e.KeyChar == OkKey)
            {
                ConfirmOk();
            }
            else if (e.KeyChar == CancelKey)
            {
                Close(false);
            }
            else
            {
                // Reset any previous error highlight as the user edits.
                ClearErrorHighlight();
                _viewModel.AppendKey(e.KeyChar);
            }
        }

        // [XPLAT] WinForms 'K' (OK) handler: empty -> ignore; range-violation -> "Error" + redden the
        // violated bound; valid -> capture ResultValue and Close(true).
        private void ConfirmOk()
        {
            string text = _viewModel.EntryText;

            //not ok if empty - just return
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tryNumber))
            {
                if (tryNumber < _viewModel.Min)
                {
                    _viewModel.EntryText = ErrorText;
                    if (_lblMin != null) _lblMin.Foreground = Brushes.Red;
                }
                else if (tryNumber > _viewModel.Max)
                {
                    _viewModel.EntryText = ErrorText;
                    if (_lblMax != null) _lblMax.Foreground = Brushes.Red;
                }
                else
                {
                    //all good, return the value
                    ResultValue = tryNumber;
                    Close(true);
                }
            }
            else
            {
                _viewModel.EntryText = ErrorText;
            }
        }

        private void ClearErrorHighlight()
        {
            if (_lblMin != null) _lblMin.Foreground = Brushes.Black;
            if (_lblMax != null) _lblMax.Foreground = Brushes.Black;
        }
    }
}
