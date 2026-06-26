// [XPLAT] migrated from net48/WinForms (Forms/Settings/ConfigMenu.{cs,Designer.cs}) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the configuration menu dialog. Avalonia reimplementation of the WinForms
    /// <c>ConfigMenu</c> (Forms/Settings/ConfigMenu.cs). The view-model
    /// (<see cref="AgOpenGPS.Core.ViewModels.ConfigMenuViewModel"/>) is supplied by the
    /// <c>AvaloniaConfigMenuPanelPresenter</c>, so this class only needs a parameterless constructor
    /// (the presenter does <c>new ConfigMenuView { DataContext = configMenuViewModel }</c>) plus the
    /// thin Avalonia adapter behaviour: the close button closes the window, and the current
    /// vehicle/tool context labels are populated from <see cref="AgOpenGPS.RegistrySettings"/> exactly
    /// as the WinForms <c>UpdateSummary()</c> did (ConfigMenu.Designer.cs L62-L63).
    /// </summary>
    public partial class ConfigMenuView : Window
    {
        public ConfigMenuView()
        {
            InitializeComponent();

            // [XPLAT] UpdateSummary() parity (ConfigMenu.Designer.cs L62-L63): the two context labels show
            // the active split-profile names. RegistrySettings is the same static, cross-platform settings
            // store used by the WinForms original (the "Vehicle: " / "Tool: " prefixes are verbatim).
            labelCurrentVehicle.Text = "Vehicle: " + AgOpenGPS.RegistrySettings.vehicleProfileName;
            labelCurrentTool.Text = "Tool: " + AgOpenGPS.RegistrySettings.toolProfileName;
        }

        /// <summary>
        /// [XPLAT] Closes the configuration menu (btnClose). Equivalent to the WinForms
        /// <c>btnClose_Click</c> that closed the form; the presenter's <c>CloseConfigDialog()</c> also
        /// calls <see cref="Window.Close()"/>, so closing here is the single, consistent exit path.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
