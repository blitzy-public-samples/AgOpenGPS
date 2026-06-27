// [XPLAT] migrated from net48/WinForms FormSerialPass.cs + FormSerialPass.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormSerialPass.axaml</c> (the AgIO Serial Pass-Through dialog),
    /// reimplementing the WinForms <c>FormSerialPass : Form</c> as an Avalonia <see cref="Window"/>
    /// bound to <see cref="FormSerialPassViewModel"/>. It follows the same convention as the sibling
    /// Avalonia views in this folder: a parameterless constructor for the XAML loader / previewer plus
    /// a view-model overload that assigns <see cref="Avalonia.StyledElement.DataContext"/>. All
    /// interaction is expressed through the view-model's bound commands, so no event-handler members
    /// are required here.
    /// </summary>
    public partial class FormSerialPass : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormSerialPass()
        {
            InitializeComponent();
        }

        /// <summary>Creates the dialog bound to the supplied <paramref name="viewModel"/>.</summary>
        public FormSerialPass(FormSerialPassViewModel viewModel)
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
