// [XPLAT] migrated from net48/WinForms FormGPSData.cs + FormGPSData.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AgIO.Services;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind half of <c>FormGPSData.axaml</c> (the AgIO live <b>"System Data"</b> readout
    /// dialog), reimplementing the deleted WinForms <c>FormGPSData : Form</c>
    /// (<c>Forms/FormGPSData.cs</c> + <c>Forms/FormGPSData.Designer.cs</c>) as an Avalonia
    /// <see cref="Window"/> bound to a <see cref="FormGPSDataViewModel"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is intentionally a <b>thin construction/teardown bridge</b>; all display behaviour was
    /// moved into <see cref="FormGPSDataViewModel"/> (a 250&#160;ms <c>DispatcherTimer</c> that mirrors the
    /// parsed NMEA/GPS/IMU state and the raw sentence boxes). The WinForms form held a <c>FormLoop mf</c>
    /// back-reference and read <c>mf.*</c> on every timer tick; that god-object back-reference is removed —
    /// the constructor instead injects the <see cref="CommCoordinatorService"/> comms aggregate and hands it
    /// to the view-model, which reads the live values from <c>comm.Nmea</c> (the single source of truth).
    /// </para>
    /// <para>
    /// <b>The one behavioural subtlety preserved here</b> is the streaming-flag teardown. The WinForms
    /// <c>FormGPSData_FormClosing</c> handler set <c>mf.isGPSSentencesOn = false</c> so the NMEA parser
    /// stopped capturing raw sentences once the dialog closed. That flag now lives on the comm path's NMEA
    /// service, and <see cref="OnClosing"/> calls <see cref="FormGPSDataViewModel.Stop"/> — which stops the
    /// refresh timer and clears <c>isGPSSentencesOn</c> — for <i>every</i> close path (the OK button, the
    /// <c>Esc</c> key via the button's <c>IsCancel</c>, and the title-bar close), exactly matching the
    /// original behaviour.
    /// </para>
    /// <para>
    /// The view is opened non-modally by the host (the AgIO <c>MainWindow</c>) via
    /// <c>new FormGPSData(comm).Show(owner)</c>, matching the original modality of the WinForms dialog (it
    /// was a live, non-modal readout). The OK button (<c>btnOK</c> in <c>FormGPSData.axaml</c>) is wired to
    /// <see cref="Window.Close"/> by name here because the XAML deliberately carries no <c>Click</c>
    /// attribute (a <c>Click</c> pointing at a handler the code-behind may not declare would break the
    /// compiled-XAML build); closing the window then routes through <see cref="OnClosing"/>.
    /// </para>
    /// </remarks>
    public partial class FormGPSData : Window
    {
        // [XPLAT] The view-model that owns the refresh timer, the InvariantCulture-formatted display
        // strings, and the streaming-flag teardown (Stop()). It replaces the former WinForms "mf"
        // back-reference. Assigned only by the CommCoordinatorService constructor; the parameterless
        // constructor (used solely by the XAML loader / design-time previewer) leaves it null, which is
        // why OnClosing guards the reference before calling Stop().
        private readonly FormGPSDataViewModel _vm;

        /// <summary>
        /// Parameterless constructor used by the Avalonia XAML loader and the design-time previewer, and to
        /// keep the compiled-XAML resource reachable (otherwise the build emits warning <c>AVLN3001</c>,
        /// which the Release configuration treats as an error). It mirrors the convention used by the
        /// sibling AgIO Avalonia views (<c>FormUDPMonitor</c>, <c>FormSerialMonitor</c>,
        /// <c>FormEventViewer</c>). Application code constructs the dialog through
        /// <see cref="FormGPSData(CommCoordinatorService)"/>, which supplies the comms aggregate and the
        /// bound view-model.
        /// </summary>
        public FormGPSData()
        {
            InitializeComponent();

            // [XPLAT] Wire the OK button to Close() BY NAME. FormGPSData.axaml gives the button
            // x:Name="btnOK" and no Click attribute (its IsCancel="True" also maps Esc to this Click), so
            // the close affordance is connected here. The control is reached through a name-scope lookup
            // (FindControl) rather than a compiled-XAML backing field: these views use the manual
            // InitializeComponent() => AvaloniaXamlLoader.Load(this) convention, under which the generated
            // x:Name fields are not auto-populated, whereas the name scope always is. The null guard keeps
            // construction safe for the design-time previewer. Closing then runs OnClosing -> _vm.Stop().
            Button okButton = this.FindControl<Button>("btnOK");
            if (okButton != null)
            {
                okButton.Click += OnOkClick;
            }
        }

        /// <summary>
        /// Creates the live "System Data" dialog, builds its <see cref="FormGPSDataViewModel"/> from the
        /// supplied comms aggregate, and binds it — the cross-platform replacement for the WinForms
        /// <c>FormGPSData(Form callingForm)</c> constructor (which cast the caller to <c>FormLoop</c>).
        /// </summary>
        /// <param name="comm">
        /// The AgIO comms aggregate whose <see cref="CommCoordinatorService.Nmea"/> supplies the live parsed
        /// GPS/IMU state shown by the dialog. The view-model enables raw-sentence capture on construction and
        /// validates this argument (it throws <see cref="System.ArgumentNullException"/> when null).
        /// </param>
        public FormGPSData(CommCoordinatorService comm)
            : this()
        {
            // [XPLAT] Build the view-model from the injected comms aggregate (no FormLoop "mf") and bind it.
            // The view-model starts its own 250 ms refresh timer; this view stays purely declarative.
            _vm = new FormGPSDataViewModel(comm);
            DataContext = _vm;
        }

        /// <summary>
        /// Loads the compiled XAML for this window. Defined explicitly (rather than relying on a generated
        /// method) to match the convention used by the sibling Avalonia views in this folder.
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Handles the OK button click (and, via the button's <c>IsCancel</c>, the <c>Esc</c> key) by
        /// closing the window. The streaming-flag teardown happens in <see cref="OnClosing"/>, so every
        /// dismissal path funnels through the same cleanup.
        /// </summary>
        private void OnOkClick(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Stops the view-model before the window closes, reproducing the WinForms
        /// <c>FormGPSData_FormClosing</c> behaviour (<c>mf.isGPSSentencesOn = false</c>):
        /// <see cref="FormGPSDataViewModel.Stop"/> stops the refresh timer and turns off raw-sentence
        /// capture in the NMEA parser. Guarded because the parameterless (design-time) constructor leaves
        /// the view-model unset; <see cref="FormGPSDataViewModel.Stop"/> is itself idempotent.
        /// </summary>
        /// <param name="e">The closing event arguments, forwarded to the base implementation.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_vm != null)
            {
                _vm.Stop();
            }

            base.OnClosing(e);
        }
    }
}
