// [XPLAT] migrated from net48/WinForms Forms/FormPGN.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

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

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        // [XPLAT] WinForms btnSerialOK_Click -> Close(); the guide carries no result.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
