// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind (behaviour half) of <c>ConfigMenuView.axaml</c> — the cross-platform Avalonia
    /// configuration menu / launcher window. It is the 1:1 parity reimplementation of the left-side
    /// navigation that the legacy WinForms <c>FormConfig</c> embedded (the <c>ConfigMenu.Designer.cs</c>
    /// partial); the actual settings panels live in <c>ConfigView</c>, never here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matching view-model (<see cref="AgOpenGPS.Core.ViewModels.ConfigMenuViewModel"/>) is supplied
    /// by the sibling <c>AvaloniaConfigMenuPanelPresenter</c>, which constructs this window as
    /// <c>new ConfigMenuView { DataContext = configMenuViewModel }</c>. This class therefore only needs a
    /// parameterless constructor that calls <see cref="InitializeComponent"/>; it must never set its own
    /// <see cref="StyledElement.DataContext"/> or new-up a view-model.
    /// </para>
    /// <para>
    /// Its remaining responsibilities are the thin Avalonia adapter behaviour that has no view-model home:
    /// populating the current vehicle/tool context labels when the window opens (parity with the legacy
    /// <c>FormConfig.UpdateSummary()</c>, <c>ConfigMenu.Designer.cs</c> L62-L63) and closing the window
    /// from the explicit Close button. The category buttons themselves are bound in markup to the
    /// view-model's <c>ShowConfigurationDialogCommand</c>, so no per-button code-behind is required — the
    /// legacy tab-switching / sub-menu machinery is intentionally gone in the MVVM split.
    /// </para>
    /// </remarks>
    public partial class ConfigMenuView : Window
    {
        /// <summary>
        /// Initialises the configuration-menu window. The presenter assigns the
        /// <see cref="StyledElement.DataContext"/> after construction, so no view-model is created here.
        /// </summary>
        public ConfigMenuView()
        {
            InitializeComponent(); // [XPLAT] Avalonia XAML-compiled; btnClose.Click is wired declaratively in the .axaml (Click="OnCloseClick").
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>FormConfig</c> load behaviour. <see cref="Window.OnOpened"/> is
        /// the Avalonia equivalent of the WinForms <c>Form.Load</c> event: by the time it runs the named
        /// controls are realised, so the two context labels are filled with the active split-profile names.
        /// This mirrors the legacy <c>UpdateSummary()</c> label lines verbatim — the <c>"Vehicle: "</c> and
        /// <c>"Tool: "</c> prefixes are preserved exactly (not routed through <c>gStr</c>) for parity.
        /// </summary>
        /// <param name="e">The event data passed to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // [XPLAT] mirrors legacy ConfigMenu UpdateSummary() label lines (ConfigMenu.Designer.cs L62-L63).
            // RegistrySettings is the same static, cross-platform settings store the WinForms original used;
            // it resolves unqualified here because namespace AgOpenGPS encloses AgOpenGPS.Views.Config.
            labelCurrentVehicle.Text = "Vehicle: " + RegistrySettings.vehicleProfileName;
            labelCurrentTool.Text = "Tool: " + RegistrySettings.toolProfileName;
        }

        /// <summary>
        /// [XPLAT] Closes the configuration menu in response to the Close button (<c>btnClose</c>, wired in
        /// the markup via <c>Click="OnCloseClick"</c>). Equivalent to the WinForms handler that closed the
        /// form; the presenter's <c>CloseConfigMenuDialog()</c> also calls <see cref="Window.Close()"/>, so
        /// this is the explicit user-driven exit path. Choosing a category instead closes the menu through
        /// the view-model's <c>ShowConfigurationDialogCommand</c>.
        /// </summary>
        /// <param name="sender">The Close button raising the event (unused).</param>
        /// <param name="e">The routed-event data (unused).</param>
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
