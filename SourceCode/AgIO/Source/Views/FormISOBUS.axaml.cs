// [XPLAT] migrated from net48/WinForms FormISOBUS.cs + FormISOBUS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormISOBUS.axaml</c> — the AgIO "ISOBUS" configuration dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormISOBUS : Form</c> (<c>Forms/FormISOBUS.cs</c> + <c>Forms/FormISOBUS.Designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> All behaviour lives in the bound <see cref="FormISOBUSViewModel"/>
    /// (set as the <see cref="StyledElement.DataContext"/>); the paired <c>FormISOBUS.axaml</c> drives
    /// every affordance through compiled command bindings (<c>x:DataType="vm:FormISOBUSViewModel"</c>),
    /// so this code-behind carries no event handlers. It only constructs/binds the view-model and
    /// routes the view-model's <see cref="FormISOBUSViewModel.RequestClose"/> intent to the window
    /// close, following the same convention as the sibling AgIO views (<c>FormSource</c>,
    /// <c>FormRadioChannel</c>): a parameterless constructor for the XAML loader plus a view-model
    /// overload that assigns the <see cref="StyledElement.DataContext"/>.
    /// </para>
    /// <para>
    /// Nullable reference types are disabled project-wide, so this file uses no nullable annotations.
    /// </para>
    /// </remarks>
    public partial class FormISOBUS : Window
    {
        // [XPLAT] Held so the view-model's close-request can be unsubscribed in OnClosed.
        private FormISOBUSViewModel _viewModel;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable. Application code constructs the
        /// dialog through <see cref="FormISOBUS(FormISOBUSViewModel)"/>.
        /// </summary>
        public FormISOBUS()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the
        /// view-model's close-request to the window close.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the ISOBUS configuration state.</param>
        public FormISOBUS(FormISOBUSViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += OnRequestClose;
            }
        }

        // [XPLAT] The view-model signals completion via RequestClose (a parameterless Action, mirroring
        // the WinForms Close()); close the dialog so the host's ShowDialog resolves.
        private void OnRequestClose() => Close();

        /// <summary>
        /// [XPLAT] Unsubscribe the view-model event when the dialog closes so the view-model never keeps
        /// the closed window alive.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel = null;
            }

            base.OnClosed(e);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
