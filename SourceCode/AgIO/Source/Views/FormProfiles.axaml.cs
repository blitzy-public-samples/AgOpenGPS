// [XPLAT] migrated from net48/WinForms FormProfiles.cs + FormProfiles.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormProfiles.axaml</c> — the AgIO "Manage Profiles" dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormProfiles : Form</c> (<c>Forms/FormProfiles.cs</c> + <c>Forms/FormProfiles.designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> All behaviour — profile enumeration, load/save/delete, and the
    /// apply/cancel flow — lives in the bound <see cref="FormProfilesViewModel"/> (set as the
    /// <see cref="StyledElement.DataContext"/>); the paired <c>FormProfiles.axaml</c> drives every
    /// affordance through compiled command bindings (<c>x:DataType="vm:FormProfilesViewModel"</c>), so
    /// this code-behind carries no event handlers. It only constructs/binds the view-model and routes
    /// the view-model's <see cref="FormProfilesViewModel.RequestClose"/> intent to the window close,
    /// following the same convention as the sibling AgIO views (<c>FormSource</c>,
    /// <c>FormRadioChannel</c>). The host subscribes <see cref="FormProfilesViewModel.ProfileApplied"/>
    /// to react to an applied profile.
    /// </para>
    /// <para>
    /// Nullable reference types are disabled project-wide, so this file uses no nullable annotations.
    /// </para>
    /// </remarks>
    public partial class FormProfiles : Window
    {
        // [XPLAT] Held so the view-model's close-request can be unsubscribed in OnClosed.
        private FormProfilesViewModel _viewModel;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable. Application code constructs the
        /// dialog through <see cref="FormProfiles(FormProfilesViewModel)"/>.
        /// </summary>
        public FormProfiles()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog bound to the supplied <paramref name="viewModel"/> and wires the
        /// view-model's close-request to the window close.
        /// </summary>
        /// <param name="viewModel">The view-model supplying the profile-management state.</param>
        public FormProfiles(FormProfilesViewModel viewModel)
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
