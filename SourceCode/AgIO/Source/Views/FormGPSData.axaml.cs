// [XPLAT] migrated from net48/WinForms FormGPSData.cs + FormGPSData.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormGPSData.axaml</c> (the AgIO "System Data" readout dialog),
    /// reimplementing the WinForms <c>FormGPSData : Form</c> as an Avalonia <see cref="Window"/> bound
    /// to <see cref="FormGPSDataViewModel"/>. It follows the same convention as the sibling Avalonia
    /// views in this folder: a parameterless constructor for the XAML loader / previewer plus a
    /// view-model overload that assigns <see cref="Avalonia.StyledElement.DataContext"/>.
    /// </summary>
    public partial class FormGPSData : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormGPSData()
        {
            InitializeComponent();

            // [XPLAT] The WinForms FormGPSData had no button (it closed via the title-bar X). The migrated
            // view adds an explicit close affordance named btnOK in FormGPSData.axaml; per that view's
            // documented contract the code-behind wires it to Close() BY NAME here (the XAML carries no
            // Click attribute, keeping the compiled-XAML build self-contained).
            btnOK.Click += (_, _) => Close();
        }

        /// <summary>Creates the dialog bound to the supplied <paramref name="viewModel"/>.</summary>
        public FormGPSData(FormGPSDataViewModel viewModel)
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
