// [XPLAT] migrated from net48/WinForms FormUDP.cs + FormUDP.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormUDP.axaml</c> (the AgIO Ethernet/UDP scan dialog),
    /// reimplementing the WinForms <c>FormUDP : Form</c> as an Avalonia <see cref="Window"/> bound to
    /// <see cref="FormUDPViewModel"/>. It follows the same convention as the sibling Avalonia views in
    /// this folder: a parameterless constructor for the XAML loader / previewer plus a view-model
    /// overload that assigns <see cref="Avalonia.StyledElement.DataContext"/>. All interaction is
    /// expressed through the view-model's bound commands, so no event-handler members are required here.
    /// </summary>
    public partial class FormUDP : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormUDP()
        {
            InitializeComponent();
        }

        /// <summary>Creates the dialog bound to the supplied <paramref name="viewModel"/>.</summary>
        public FormUDP(FormUDPViewModel viewModel)
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
