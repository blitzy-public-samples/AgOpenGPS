// [XPLAT] migrated from net48/WinForms FormAdvancedSettings.cs + FormAdvancedSettings.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormAdvancedSettings.axaml</c> — the AgIO "Advanced Settings"
    /// dialog, reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormAdvancedSettings : Form</c> (<c>Forms/FormAdvancedSettings.cs</c> +
    /// <c>Forms/FormAdvancedSettings.Designer.cs</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only — the simplest code-behind in the folder.</b> This dialog owns no comm
    /// services and no <c>FormLoop</c>/<c>mf</c> back-reference: every affordance (the three display
    /// toggles — Start Minimized, Auto Start GPS-Out, PopUp AgIO on Warn — and the Save/Cancel
    /// commands) is driven from the bound <see cref="FormAdvancedSettingsViewModel"/> through compiled
    /// bindings (<c>x:DataType="vm:FormAdvancedSettingsViewModel"</c> in the paired XAML), so this
    /// code-behind declares no event handlers. It simply constructs the view-model, assigns it as the
    /// window's <c>DataContext</c>, and bridges the view-model's
    /// <see cref="FormAdvancedSettingsViewModel.RequestClose"/> intent to the window close — the same
    /// convention used by the sibling AgIO views (<c>FormYes</c>, <c>FormPGN</c>,
    /// <c>FormRadioChannel</c>, <c>FormSource</c>).
    /// </para>
    /// <para>
    /// <see cref="FormAdvancedSettingsViewModel.RequestClose"/> carries the dialog outcome: <c>true</c>
    /// on the accept path (the WinForms <c>btnClose</c>/OK "save and close", routed through the
    /// view-model's <c>SaveCommand</c>) and <c>false</c> on the cancel path (the migration-added
    /// <c>CancelCommand</c>). That outcome is surfaced to the opener via <c>ShowDialog&lt;bool&gt;</c>,
    /// preserving the WinForms <c>DialogResult.OK</c>/<c>DialogResult.Cancel</c> contract — the host
    /// opens the dialog as <c>bool result = await new FormAdvancedSettings().ShowDialog&lt;bool&gt;(owner);</c>.
    /// Because <c>ShowDialog&lt;bool&gt;</c> yields <c>default(bool)</c> (<c>false</c>) when the window is
    /// dismissed via its title bar, the close/cancel path is naturally <c>false</c> and needs no extra
    /// handling. The window owns the view-model it creates, so the close-request subscription forms a
    /// self-contained pair that is collected together once the dialog is dismissed; no explicit
    /// unsubscribe is required. Nullable reference types are disabled project-wide, so this file uses no
    /// nullable annotations.
    /// </para>
    /// </remarks>
    public partial class FormAdvancedSettings : Window
    {
        // [XPLAT] The dialog owns its view-model — there is no caller-supplied VM and no FormLoop mf
        // back-reference. It is created here, exposed as the DataContext for the compiled bindings, and
        // its RequestClose event is bridged to the window Close below.
        private readonly FormAdvancedSettingsViewModel _vm;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormAdvancedSettings"/> dialog: loads the
        /// compiled XAML, constructs and binds a fresh <see cref="FormAdvancedSettingsViewModel"/>, and
        /// routes the view-model's <see cref="FormAdvancedSettingsViewModel.RequestClose"/> event to
        /// <see cref="Window.Close(object)"/> so the accept (<c>true</c>) / cancel (<c>false</c>) result
        /// flows back through <c>ShowDialog&lt;bool&gt;</c>. This is the sole entry point; the host opens
        /// the dialog with <c>new FormAdvancedSettings().ShowDialog&lt;bool&gt;(owner)</c>.
        /// </summary>
        public FormAdvancedSettings()
        {
            InitializeComponent();

            // [XPLAT] Construct + bind the self-contained view-model. Parity with the WinForms ctor,
            // which seeded its three check boxes straight from Settings.Default; the view-model now does
            // that seeding and defers the write-back to its SaveCommand.
            _vm = new FormAdvancedSettingsViewModel();
            DataContext = _vm;

            // [XPLAT] SaveCommand -> RequestClose(true); CancelCommand -> RequestClose(false). Close(result)
            // boxes the bool so ShowDialog<bool> resolves to the dialog result, replacing the WinForms
            // Close()/DialogResult.
            _vm.RequestClose += result => Close(result);
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
