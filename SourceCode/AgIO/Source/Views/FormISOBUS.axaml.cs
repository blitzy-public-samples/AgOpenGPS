// [XPLAT] migrated from net48/WinForms FormISOBUS.cs + FormISOBUS.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using AgOpenGPS.Core.Interfaces;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormISOBUS.axaml</c> — the AgIO "ISOBUS" configuration dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormISOBUS : Form</c> (<c>Forms/FormISOBUS.cs</c> + <c>Forms/FormISOBUS.Designer.cs</c>).
    /// The dialog lets the operator pick a CAN adapter and channel and then launch or stop the external
    /// <c>AOG-TaskController</c> process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> Every behaviour — the adapter/channel selection, the Windows-only Registry
    /// install lookup and <see cref="System.Diagnostics.Process"/> launch/stop (both feature-gated in the
    /// view-model), the process-output log, and settings persistence — lives in the bound
    /// <see cref="FormISOBUSViewModel"/> (set as the <see cref="StyledElement.DataContext"/>). The paired
    /// <c>FormISOBUS.axaml</c> drives every affordance through compiled command bindings
    /// (<c>x:DataType="vm:FormISOBUSViewModel"</c>), so this code-behind declares no event handlers: it only
    /// constructs the view-model, binds it, and routes the view-model's
    /// <see cref="FormISOBUSViewModel.RequestClose"/> intent to the window close.
    /// </para>
    /// <para>
    /// <b>God-object removal (AAP §0.3.2).</b> The WinForms original reached its UI controls and helpers
    /// directly and showed errors through <c>MessageBox.Show</c>. This dialog takes no <c>FormLoop</c>/<c>mf</c>
    /// back-reference; instead an application-level <see cref="IErrorPresenter"/> is injected here and handed to
    /// the view-model, which surfaces the original four notifications (including the "available on Windows
    /// only" notice on Linux/macOS) through it. The error presenter passed in is the app-level one supplied by
    /// the hosting window / application (e.g. <c>RegistrySettings.ErrorPresenter</c>); the dialog is opened with
    /// <c>await new FormISOBUS(errorPresenter).ShowDialog(owner)</c>.
    /// </para>
    /// <para>
    /// <b>Conventions.</b> The shape here — a parameterless constructor for the Avalonia XAML loader plus a
    /// dependency-supplying overload that builds the view-model and binds it, with an explicit
    /// <see cref="AvaloniaXamlLoader"/> call — mirrors the sibling AgIO views (<c>FormSource</c>, <c>FormYes</c>,
    /// <c>FormEventViewer</c>). Nullable reference types are disabled project-wide, so this file uses no nullable
    /// annotations, and it references no WinForms/WPF/System.Drawing types.
    /// </para>
    /// </remarks>
    public partial class FormISOBUS : Window
    {
        /// <summary>
        /// [XPLAT] The view-model backing this dialog. Built in <see cref="FormISOBUS(IErrorPresenter)"/> from the
        /// injected presenter and retained for the lifetime of the dialog (it is both this field and the window's
        /// <see cref="StyledElement.DataContext"/>). The parameterless loader constructor leaves it at its default
        /// — that path never touches it.
        /// </summary>
        private readonly FormISOBUSViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time previewer,
        /// and to keep the compiled-XAML resource reachable (otherwise the build emits warning <c>AVLN3001</c>,
        /// which the Release configuration treats as an error via <c>TreatWarningsAsErrors</c>). Application code
        /// constructs the dialog through <see cref="FormISOBUS(IErrorPresenter)"/>.
        /// </summary>
        public FormISOBUS()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Creates the ISOBUS dialog, builds its <see cref="FormISOBUSViewModel"/> from the supplied
        /// application-level error presenter, binds it, and wires the view-model's close request to the window
        /// close — the cross-platform replacement for the WinForms <c>FormISOBUS()</c> constructor, with the
        /// god-object/MessageBox coupling replaced by the injected <see cref="IErrorPresenter"/>.
        /// </summary>
        /// <param name="errorPresenter">
        /// The application-level presenter through which the dialog (via its view-model) surfaces transient
        /// informational/error messages — the migrated replacement for <c>MessageBox.Show</c>, and the channel by
        /// which the "available on Windows only" notice is shown on Linux/macOS. May be <see langword="null"/>;
        /// the view-model invokes it null-conditionally so a missing presenter never faults the dialog.
        /// </param>
        public FormISOBUS(IErrorPresenter errorPresenter)
        {
            InitializeComponent();

            // [XPLAT] Build the view-model from the injected presenter, then bind it. The VM owns all logic
            // (adapter/channel selection, the Windows-gated Registry lookup + process launch/stop, the log, and
            // settings persistence); this window only hosts and routes it.
            _vm = new FormISOBUSViewModel(errorPresenter);
            DataContext = _vm;

            // [XPLAT] The bound Ok/Cancel commands only raise the parameterless RequestClose intent (a view-model
            // cannot close a window); forward it to Window.Close so the host's ShowDialog resolves — the migrated
            // equivalent of the WinForms btnIsobusOK Hide(). The window owns the VM and they share a lifetime, so
            // the lambda needs no explicit unsubscribe (matching the sibling FormSource convention).
            _vm.RequestClose += () => Close();
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
