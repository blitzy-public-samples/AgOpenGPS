// [XPLAT] migrated from net48/WinForms Forms/FormPGN.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Read-only "PGN Guide" reference dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormPGN : Form</c>. It is bound to a
    /// <see cref="FormPGNViewModel"/> that exposes the immutable PGN identifier/description reference
    /// table and the frozen PGN wire-protocol summary.
    /// </summary>
    /// <remarks>
    /// The WinForms original carried no behaviour beyond an OK button that called <c>Close()</c>; this
    /// view reproduces that exactly. The dialog owns no result and no mutable state — it is a passive
    /// reference window — so the OK button simply closes it.
    /// </remarks>
    public partial class FormPGNView : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormPGNView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/>.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the read-only PGN reference data.</param>
        public FormPGNView(FormPGNViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // [XPLAT] WinForms btnSerialOK_Click -> Close(); the guide carries no result.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
