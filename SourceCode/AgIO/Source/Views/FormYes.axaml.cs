// [XPLAT] migrated from net48/WinForms FormYes.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Modal Yes/OK + (optional) Cancel confirmation dialog, reimplemented as an Avalonia
    /// <see cref="Window"/> replacing the deleted WinForms <c>FormYes : Form</c>
    /// (<c>Forms/FormYes.cs</c> + <c>Forms/FormYes.designer.cs</c>). This is the code-behind half of
    /// <c>FormYes.axaml</c>; together they form a single view bound to a
    /// <see cref="FormYesViewModel"/> that supplies the confirmation message
    /// (<see cref="FormYesViewModel.MessageText"/>) and whether the Cancel button is shown
    /// (<see cref="FormYesViewModel.ShowCancel"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behaviour is ported 1:1 from the WinForms original. The WinForms <c>btnSerialOK</c> carried
    /// <c>DialogResult.OK</c> and was the form's <c>AcceptButton</c>; here it is the default button
    /// (<c>IsDefault="True"</c> in <c>FormYes.axaml</c>, so Enter activates it) and its click closes
    /// the dialog returning <see langword="true"/>. When <see cref="FormYesViewModel.ShowCancel"/> is
    /// <see langword="true"/>, the <c>btnSerialCancel</c> button is shown (WinForms
    /// <c>DialogResult.Cancel</c>) and its click closes the dialog returning <see langword="false"/>.
    /// </para>
    /// <para>
    /// The dialog result is surfaced to callers through <c>ShowDialog&lt;bool&gt;</c>, mirroring the
    /// WinForms <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c> contract:
    /// <c>bool result = await new FormYes(msg, true).ShowDialog&lt;bool&gt;(ownerWindow);</c>.
    /// Because <c>ShowDialog&lt;bool&gt;</c> yields <c>default(bool)</c> (<see langword="false"/>)
    /// when the window is closed via the title bar, the cancel/close path is naturally
    /// <see langword="false"/> and needs no extra handling. Typical callers are the profile-overwrite
    /// confirmation in <c>FormProfiles</c> and the restart confirmations in <c>FormEthernet</c>,
    /// <c>FormUDP</c>, and <c>FormSerialPass</c> (the former <c>mf.YesMessageBox(...)</c>).
    /// </para>
    /// </remarks>
    public partial class FormYes : Window
    {
        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits
        /// warning <c>AVLN3001</c>, which the Release configuration treats as an error). Application
        /// code constructs the dialog through <see cref="FormYes(string, bool)"/>.
        /// </summary>
        public FormYes()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the confirmation dialog and binds it to a freshly built
        /// <see cref="FormYesViewModel"/> — the cross-platform replacement for the WinForms
        /// <c>FormYes(string messageStr, bool showCancel)</c> constructor.
        /// </summary>
        /// <param name="messageStr">The confirmation message shown to the operator.</param>
        /// <param name="showCancel">
        /// <see langword="true"/> to show the Cancel button (a Yes/No style prompt);
        /// <see langword="false"/> for an acknowledge-only (OK) prompt. Mirrors the WinForms
        /// <c>showCancel</c> flag that toggled <c>btnCancel.Visible</c>.
        /// </param>
        public FormYes(string messageStr, bool showCancel)
        {
            InitializeComponent();
            DataContext = new FormYesViewModel(messageStr, showCancel);
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md

        // [XPLAT] WinForms btnSerialOK / DialogResult.OK -> close returning true for ShowDialog<bool>.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] WinForms btnCancel / DialogResult.Cancel -> close returning false for ShowDialog<bool>.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
