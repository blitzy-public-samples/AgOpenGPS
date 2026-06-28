// [XPLAT] migrated from net48/WinForms FormPGN.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Read-only "PGN Guide" reference dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormPGN : Form</c>
    /// (<c>Forms/FormPGN.cs</c> + <c>FormPGN.designer.cs</c>). This is the code-behind half of
    /// <c>FormPGN.axaml</c>; together they form a single view whose <see cref="StyledElement.DataContext"/>
    /// is a <see cref="FormPGNViewModel"/> exposing the immutable PGN identifier/description reference
    /// table and the frozen PGN wire-protocol summary (header <c>0x80 0x81 0x7F</c>, additive CRC,
    /// loopback ports 15555/17777 — see <c>docs/pgn-protocol.md</c>).
    /// </summary>
    /// <remarks>
    /// The WinForms original carried no behaviour beyond an OK button (<c>btnSerialOK</c>) whose
    /// <c>btnSerialOK_Click</c> handler called <c>Close()</c>; this view reproduces that exactly via
    /// <see cref="OnOkClick"/>. The dialog is a passive reference window — it owns no mutable state and
    /// no result — and is opened non-modally by callers as <c>new FormPGN().Show(ownerWindow)</c>,
    /// mirroring the WinForms <c>new FormPGN().Show(this)</c> used by the AgIO UDP monitors. Because the
    /// parameterless constructor is the only entry point, it both loads the XAML and assigns the
    /// display-only view-model so the bound reference data renders without any caller having to supply it.
    /// </remarks>
    public partial class FormPGN : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormPGN"/> class, loading the XAML and binding
        /// the read-only <see cref="FormPGNViewModel"/> that supplies the PGN reference data. This is the
        /// sole constructor: callers open the guide with <c>new FormPGN().Show(ownerWindow)</c>.
        /// </summary>
        public FormPGN()
        {
            InitializeComponent();
            DataContext = new FormPGNViewModel();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        /// <summary>
        /// Handles the OK button's <c>Click</c> (wired in <c>FormPGN.axaml</c>) by closing the dialog,
        /// reproducing the WinForms <c>btnSerialOK_Click</c> handler. The guide carries no result.
        /// </summary>
        /// <param name="sender">The OK button raising the event.</param>
        /// <param name="e">The routed event data.</param>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
