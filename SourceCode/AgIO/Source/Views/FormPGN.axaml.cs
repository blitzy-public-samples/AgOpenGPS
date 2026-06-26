// [XPLAT] migrated from net48/WinForms Forms/FormPGN.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Read-only "PGN Guide" reference dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormPGN : Form</c>. This is the
    /// code-behind half of <c>FormPGN.axaml</c>; together they form a single view that is bound to a
    /// <see cref="FormPGNViewModel"/> exposing the immutable PGN identifier/description reference
    /// table and the frozen PGN wire-protocol summary (header 0x80 0x81 0x7F, additive CRC,
    /// loopback ports 15555/17777 — see docs/pgn-protocol.md).
    /// </summary>
    /// <remarks>
    /// The WinForms original (<c>Forms/FormPGN.cs</c>) carried no behaviour beyond an OK button
    /// (<c>btnSerialOK</c>) whose <c>btnSerialOK_Click</c> handler called <c>Close()</c>; this view
    /// reproduces that exactly. The dialog owns no result and no mutable state — it is a passive
    /// reference window — so the OK button simply closes it. This follows the same convention as the
    /// sibling Avalonia views in this folder (<c>FormYes</c>, <c>FormTimedMessage</c>) and the
    /// old-naming twin <c>FormPGNView</c>: a parameterless constructor for the XAML loader plus a
    /// view-model overload that assigns <see cref="StyledElement.DataContext"/>.
    /// </remarks>
    public partial class FormPGN : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormPGN()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/>.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the read-only PGN reference data.</param>
        public FormPGN(FormPGNViewModel viewModel)
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
