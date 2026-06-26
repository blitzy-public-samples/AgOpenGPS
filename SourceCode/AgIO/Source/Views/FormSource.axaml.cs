// [XPLAT] migrated from net48/WinForms Forms/FormSource.cs + FormSource.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] NTRIP "Source Data" mount-point picker dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormSource : Form</c>. This is the
    /// code-behind half of <c>FormSource.axaml</c>; together they form a single view bound to a
    /// <see cref="FormSourceViewModel"/> that exposes the parsed source-table entries and the
    /// selected mount point. It follows the same convention as the sibling Avalonia views in this
    /// folder (<c>FormPGN</c>, <c>FormYes</c>, <c>FormTimedMessage</c>): a parameterless constructor
    /// for the XAML loader plus a view-model overload that assigns
    /// <see cref="StyledElement.DataContext"/>. The XAML uses compiled bindings/commands, so no
    /// event-handler code is required here.
    /// </summary>
    public partial class FormSource : Window
    {
        /// <summary>Parameterless constructor (required for the XAML loader / designer).</summary>
        public FormSource()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/>.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the NTRIP source-table data.</param>
        public FormSource(FormSourceViewModel viewModel)
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
