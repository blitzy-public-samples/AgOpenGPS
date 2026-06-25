// [XPLAT] migrated from net48/WinForms Forms/FormKeyboard.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypad;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] On-screen touch keyboard dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormKeyboard : Form</c>. It hosts the shared
    /// <see cref="Keyboard"/> control and an entry <c>TextBox</c> two-way bound to a
    /// <see cref="FormKeyboardViewModel"/>.
    /// </summary>
    /// <remarks>
    /// Behaviour ported 1:1: the keyboard's <see cref="GenericKeypad.ButtonPressed"/> events drive
    /// <see cref="FormKeyboardViewModel.AppendKey(char)"/> (which reproduces the original
    /// <c>RegisterKeyboard1_ButtonPressed</c> text transitions). The OK control token (<c>'\u0004'</c>)
    /// confirms the dialog (<c>Close(true)</c>, exposing the value via <see cref="Result"/>) and the
    /// Cancel control token (<c>'\u0027'</c>) cancels it (<c>Close(false)</c>) — the cross-platform
    /// equivalent of the WinForms <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c>.
    /// </remarks>
    public partial class FormKeyboardView : Window
    {
        private const char OkKey = '\u0004';
        private const char CancelKey = '\u0027';

        private FormKeyboardViewModel _viewModel;

        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormKeyboardView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to <paramref name="viewModel"/> and subscribes to the on-screen
        /// keyboard's key events.
        /// </summary>
        /// <param name="viewModel">The view-model holding the running entry text.</param>
        public FormKeyboardView(FormKeyboardViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        /// <summary>
        /// Gets the confirmed entry text after the dialog closes with <c>true</c> (the WinForms
        /// <c>ReturnString</c>).
        /// </summary>
        public string Result => _viewModel?.GetResult();

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            var keyboard = this.FindControl<Keyboard>("keyboard1");
            if (keyboard != null)
            {
                keyboard.ButtonPressed += Keyboard_ButtonPressed;
            }
        }

        private void Keyboard_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            // [XPLAT] OK / Cancel own the dialog lifecycle; every other key is text editing on the VM.
            if (e.KeyChar == OkKey)
            {
                Close(true);
            }
            else if (e.KeyChar == CancelKey)
            {
                Close(false);
            }
            else
            {
                _viewModel?.AppendKey(e.KeyChar);
            }
        }
    }
}
