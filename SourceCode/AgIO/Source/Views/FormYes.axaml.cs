// [XPLAT] migrated from net48/WinForms Forms/FormYes.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Modal Yes/OK + Cancel confirmation dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormYes : Form</c>. It is bound to a
    /// <see cref="FormYesViewModel"/> (message text + whether Cancel is shown).
    /// </summary>
    /// <remarks>
    /// Behaviour ported 1:1 from the WinForms original: <c>btnSerialOK</c> is the accept button
    /// (DialogResult.OK) and, when <see cref="FormYesViewModel.ShowCancel"/> is true,
    /// <c>btnSerialCancel</c> is the cancel button. The dialog result is surfaced through
    /// <c>ShowDialog&lt;bool&gt;</c>: <c>Close(true)</c> for OK/Yes and <c>Close(false)</c> for Cancel.
    /// </remarks>
    public partial class FormYes : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormYes()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/>.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the message and Cancel visibility.</param>
        public FormYes(FormYesViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [XPLAT] WinForms DialogResult.OK -> close returning true for ShowDialog<bool>.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] WinForms DialogResult.Cancel -> close returning false for ShowDialog<bool>.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
