// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormConfig.{cs,Designer.cs}) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the main configuration dialog. Avalonia reimplementation of the WinForms
    /// <c>FormConfig</c> (Forms/Settings/FormConfig.cs). The view-model
    /// (<see cref="AgOpenGPS.Core.ViewModels.ConfigViewModel"/>) is supplied by the
    /// <c>AvaloniaConfigMenuPanelPresenter</c> (<c>new ConfigView { DataContext = configViewModel }</c>),
    /// so this class provides only the parameterless constructor and the thin Avalonia adapter
    /// behaviour. <c>ConfigViewModel</c> exposes no command, so the OK button is a code-behind
    /// <see cref="OnOkClick"/> handler that closes the window — the same Click convention used by the
    /// sibling <c>ConfigMenuView</c>; the presenter's <c>CloseConfigDialog()</c> also calls
    /// <see cref="Window.Close()"/>, giving a single consistent close path.
    /// </summary>
    /// <remarks>
    /// The hosted <c>configVehicleControl</c> / <c>configSummaryControl</c> x:Names are present in the
    /// markup as the hard contract for the deeper population (the WinForms
    /// <c>configSummaryControl.UpdateSummary(mf)</c> / <c>configVehicleControl.Initialize(...)</c>),
    /// which is driven from the extracted services that replace the former <c>FormGPS</c> (<c>mf</c>)
    /// god-object and is therefore tracked separately in MIGRATION_DOCS/TRANSITION_MAP.md rather than
    /// fabricated here against state that does not yet exist.
    /// </remarks>
    public partial class ConfigView : Window
    {
        public ConfigView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Closes the configuration dialog (btnOK). Mirrors the WinForms OK button that closed the
        /// form; <c>IsDefault</c>/<c>IsCancel</c> in the markup additionally map Enter/Escape to this path.
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
