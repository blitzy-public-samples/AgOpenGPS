// [XPLAT] migrated from net48/WinForms FormSource.cs + FormSource.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Collections.Generic;
using Avalonia.Controls;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormSource.axaml</c> — the NTRIP caster "Source Data" mountpoint
    /// picker, reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormSource : Form</c> (<c>Forms/FormSource.cs</c> + <c>Forms/FormSource.Designer.cs</c>).
    /// Opened by <c>FormNtrip</c> so the operator can pick a caster mountpoint from the parsed
    /// source-table, ranked by the great-circle distance from the current position to each base.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Thin glue only.</b> All behaviour — source-table parsing, the haversine distance ranking,
    /// the distance/name sort toggle, the read-only selected-mount readout, and the "visit website"
    /// action — lives in the bound <see cref="FormSourceViewModel"/> (set as the
    /// <see cref="StyledElement.DataContext"/>). The paired <c>FormSource.axaml</c> drives every
    /// affordance through compiled command bindings, so this code-behind carries no event handlers:
    /// it only constructs the view-model, binds it, and routes the view-model's close request to the
    /// window's dialog-close so the result reaches the caller.
    /// </para>
    /// <para>
    /// <b>God-object removal (the heart of the migration for this dialog).</b> The WinForms original
    /// took a <c>Form callingForm</c> parameter and mutated the caller directly, writing the chosen
    /// mountpoint into <c>FormNtrip.tboxMount.Text</c>. That back-reference is gone: the
    /// <c>Form callingForm</c> parameter is dropped and the dialog instead <i>returns</i> the chosen
    /// mountpoint string. <see cref="FormSourceViewModel.RequestClose"/> carries the selected
    /// mountpoint (or <see cref="string.Empty"/> on cancel / no selection); the subscription below
    /// forwards it to <see cref="Window.Close(object)"/>, surfacing it to the opener through
    /// <c>ShowDialog&lt;string&gt;</c>. A typical caller is:
    /// <c>string mount = await new FormSource(list, lat, lon, site).ShowDialog&lt;string&gt;(owner);</c>,
    /// after which <c>FormNtrip</c> applies the returned value to its own <c>Mount</c> property.
    /// </para>
    /// <para>
    /// <b>Nullable reference types are disabled project-wide</b>, so this file uses no nullable
    /// annotations and the dialog result type is <c>string</c> (never <c>string?</c>) — matching the
    /// <c>ShowDialog&lt;string&gt;</c> contract. The convention here (a parameterless constructor for
    /// the XAML loader plus a data-supplying overload, with an explicit
    /// <see cref="AvaloniaXamlLoader"/> call) mirrors the sibling AgIO views (<c>FormYes</c>,
    /// <c>FormPGN</c>, <c>FormRadioChannel</c>, <c>FormTimedMessage</c>).
    /// </para>
    /// </remarks>
    public partial class FormSource : Window
    {
        /// <summary>
        /// The view-model backing this dialog. Built in <see cref="FormSource(List{string}, double, double, string)"/>
        /// from the caster source-table and current position, retained for the lifetime of the dialog
        /// (it is both this field and the window's <see cref="StyledElement.DataContext"/>). The
        /// parameterless loader constructor leaves it at its default — that path never touches it.
        /// </summary>
        private readonly FormSourceViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits
        /// warning <c>AVLN3001</c>, which the Release configuration treats as an error). Application
        /// code constructs the dialog through
        /// <see cref="FormSource(List{string}, double, double, string)"/>.
        /// </summary>
        public FormSource()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the mountpoint picker, builds its <see cref="FormSourceViewModel"/> from the
        /// supplied caster source-table and current position, and wires the view-model's close
        /// request to the dialog result — the cross-platform replacement for the WinForms
        /// <c>FormSource(Form callingForm, List&lt;string&gt; _dataList, double _lat, double _lon, string syte)</c>
        /// constructor, with the <c>Form callingForm</c> god-object parameter dropped.
        /// </summary>
        /// <param name="dataList">
        /// The caster source-table records, one comma-delimited <c>STR</c> record per element
        /// (mountpoint, latitude, longitude, format, network, ...). A <c>null</c> list yields an empty
        /// picker.
        /// </param>
        /// <param name="lat">The operator's current latitude, in decimal degrees.</param>
        /// <param name="lon">The operator's current longitude, in decimal degrees.</param>
        /// <param name="site">
        /// The caster's site/home URL opened by the "visit website" action. May be empty.
        /// </param>
        public FormSource(List<string> dataList, double lat, double lon, string site)
        {
            InitializeComponent();

            // [XPLAT] Build the view-model from raw source-table data + current position, then bind it.
            // The VM owns all parsing/sorting/distance math; this window only hosts and routes it.
            _vm = new FormSourceViewModel(dataList, lat, lon, site);
            DataContext = _vm;

            // [XPLAT] God-object removal: rather than writing the chosen mountpoint back into the
            // caller (the WinForms nt.tboxMount.Text mutation), the dialog returns it. RequestClose
            // carries the selected mountpoint (or string.Empty on cancel / no selection); Close(mount)
            // surfaces it to the opener via ShowDialog<string>. The VM and this window share the same
            // lifetime (the window owns the VM and closes when the request fires), so the lambda needs
            // no explicit unsubscribe.
            _vm.RequestClose += mount => Close(mount);
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
