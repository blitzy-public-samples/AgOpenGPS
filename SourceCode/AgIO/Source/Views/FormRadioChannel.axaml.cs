// [XPLAT] migrated from net48/WinForms Forms/FormRadioChannel.cs + FormRadioChannel.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] "Radio Channel" editor dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormRadioChannel : Form</c>. This is the code-behind half
    /// of <c>FormRadioChannel.axaml</c>; together they form a single view bound to a
    /// <see cref="FormRadioChannelViewModel"/> that exposes the radio channel's id/name/frequency
    /// and latitude/longitude. It follows the same convention as the sibling Avalonia views in this
    /// folder (<c>FormPGN</c>, <c>FormYes</c>, <c>FormTimedMessage</c>): a parameterless constructor
    /// for the XAML loader plus a view-model overload that assigns
    /// <see cref="StyledElement.DataContext"/>. The XAML uses compiled bindings/commands, so no
    /// event-handler code is required here.
    /// </summary>
    public partial class FormRadioChannel : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormRadioChannel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/>.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the radio-channel fields.</param>
        public FormRadioChannel(FormRadioChannelViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
