// [XPLAT] migrated from net48/WinForms FormAdvancedSettings.cs + FormAdvancedSettings.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormAdvancedSettings.axaml</c> — the AgIO "Advanced Settings" dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormAdvancedSettings : Form</c> (<c>Forms/FormAdvancedSettings.cs</c> +
    /// <c>Forms/FormAdvancedSettings.Designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> All behaviour — the auto-run-GPS-out, show-on-warning, and
    /// start-minimized toggles and the accept/cancel flow — lives in the bound
    /// <see cref="FormAdvancedSettingsViewModel"/> (set as the <see cref="StyledElement.DataContext"/>);
    /// the paired <c>FormAdvancedSettings.axaml</c> drives every affordance through compiled bindings
    /// (<c>x:DataType="vm:FormAdvancedSettingsViewModel"</c>), so this code-behind carries no event
    /// handlers. It only constructs/binds the view-model and routes the view-model's
    /// <see cref="FormAdvancedSettingsViewModel.RequestClose"/> intent to the window close, following
    /// the same convention as the sibling AgIO views (<c>FormSource</c>, <c>FormRadioChannel</c>).
    /// </para>
    /// <para>
    /// <see cref="FormAdvancedSettingsViewModel.RequestClose"/> carries the accept (<c>true</c>) /
    /// cancel (<c>false</c>) outcome, which is surfaced to the opener via <c>ShowDialog&lt;bool&gt;</c>
    /// (parity with the WinForms <c>DialogResult.OK</c>/<c>Cancel</c>). Nullable reference types are
    /// disabled project-wide, so this file uses no nullable annotations.
    /// </para>
    /// </remarks>
    public partial class FormAdvancedSettings : Window
    {
        // [XPLAT] Held so the view-model's close-request can be unsubscribed in OnClosed.
        private FormAdvancedSettingsViewModel _viewModel;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable. Application code constructs the
        /// dialog through <see cref="FormAdvancedSettings(FormAdvancedSettingsViewModel)"/>.
        /// </summary>
        public FormAdvancedSettings()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the
        /// view-model's close-request to the window close.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the advanced-settings toggles.</param>
        public FormAdvancedSettings(FormAdvancedSettingsViewModel viewModel)
            : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestClose += OnRequestClose;
            }
        }

        // [XPLAT] RequestClose carries the accept(true)/cancel(false) result; surface it to the opener
        // via ShowDialog<bool>, mirroring the WinForms DialogResult.OK/Cancel.
        private void OnRequestClose(bool result) => Close(result);

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
