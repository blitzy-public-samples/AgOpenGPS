// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for the "Easy Drive" quick-setup wizard — a 1:1 behavioural-parity
/// reimplementation of the WinForms <c>Forms/Field/FormEasyDrive</c> (FormEasyDrive.cs +
/// FormEasyDrive.Designer.cs). The operator enters a tool work width and a hitch/pivot distance, then
/// taps <em>Next</em> to configure a one-section rigid tool and start a temporary "Easy Drive" job.
/// </summary>
/// <remarks>
/// <para>
/// The original form held a <c>private readonly FormGPS mf</c> back-reference and, on <em>Next</em>,
/// mutated that god-object directly — <c>mf.tool.*</c>, <c>mf.section[0]</c>,
/// <c>mf.SectionCalcWidths()</c>, <c>mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false)</c>,
/// <c>mf.currentFieldDirectory</c>, <c>mf.JobNew()</c> and <c>mf.isEasyDriveMode</c>. Per the
/// migration's Dependency-Inversion strategy (AAP §0.3.2) the <c>FormGPS</c>/<c>mf</c> coupling is
/// removed and replaced by constructor injection. The <em>Next</em> handler performs the EXACT same
/// work, in the EXACT same order, as the WinForms <c>btnStart_Click</c>, but against injected
/// collaborators rather than a form reference. This mirrors the sibling <c>FormShiftPosView</c>, which
/// likewise injects the portable <see cref="ApplicationModel"/> instead of reaching through
/// <c>FormGPS</c>.
/// </para>
/// <para>
/// <b>Injection split (build-driven).</b> The collaborators are injected as their concrete portable
/// types where those types participate in the cross-platform build, and as delegates otherwise:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="CSection"/> array, <see cref="CNMEA"/> and <see cref="ApplicationModel"/> are injected
///     directly (they are already decoupled/portable and compile cross-platform), so the
///     <c>section[0]</c> extents and <c>pn.DefineLocalPlane(appModel.CurrentLatLon, false)</c> are
///     written inline exactly as the WinForms handler did.
///   </description></item>
///   <item><description>
///     The rigid one-section <em>tool</em> configuration is injected as an
///     <see cref="Action{T}"/> (<c>configureRigidTool</c>) because <c>CTool</c> is still coupled to the
///     WinForms host and is therefore excluded from the current cross-platform build
///     (<c>&lt;Compile Remove="Classes\CTool.cs"&gt;</c>) while the Classes agent decouples it. The host
///     owns the live <c>CTool</c> and applies the documented rigid-tool field set; the view supplies the
///     pivot distance. This keeps the view compilable on net8.0 / net8.0-windows today and free of any
///     <c>FormGPS</c>/<c>mf</c> reference (AAP §0.3.2).
///   </description></item>
///   <item><description>
///     <c>SectionCalcWidths</c>, <c>JobNew</c>, and the writes to <c>currentFieldDirectory</c> /
///     <c>isEasyDriveMode</c> were <c>FormGPS</c> methods/fields; they are injected as delegates so the
///     migrated host (its post-migration owner) wires them.
///   </description></item>
/// </list>
/// <para>
/// The paired <c>FormEasyDriveView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls — keeping their original
/// WinForms names — and wires no handlers. This code-behind attaches the migrated handlers by name and
/// assigns the localised <see cref="gStr"/> captions + unit/value text at runtime, exactly as the
/// WinForms ctor + <c>FormEasyDrive_Load</c> did. The two numeric fields (<c>nudWidth</c> /
/// <c>nudPivotDistance</c>) are read-only display <see cref="Button"/>s reproducing the WinForms
/// <c>NudlessNumericUpDown</c>: a tap opens the on-screen numeric keypad <see cref="FormNumeric"/>
/// (Views/Inputs) with that field's min/max/current, and the chosen value is written back and
/// re-displayed.
/// </para>
/// <para>
/// <b>[XPLAT] Culture correctness (AAP §0.6.5 — highest data-integrity rule).</b> Every numeric
/// display string is formatted with <see cref="CultureInfo.InvariantCulture"/> so the decimal
/// separator is locale-independent across Windows / Linux / macOS (a comma separator would otherwise
/// misrepresent the value on non-US locales). The feet→metres conversion and the half-width split are
/// pure numeric operations and are inherently culture-independent.
/// </para>
/// <para>
/// The dialog returns its outcome through <c>Close(bool)</c>: the running application shows it via
/// <c>await dlg.ShowDialog&lt;bool&gt;(owner)</c>, where <see langword="true"/> means <em>Next</em> was
/// pressed (the job was started) and <see langword="false"/> means it was cancelled.
/// </para>
/// </remarks>
public partial class FormEasyDriveView : Window
{
    // [XPLAT] Injected live domain objects (replace the WinForms mf.section / mf.pn / mf.AppModel
    // members). All three are portable, already-decoupled types in this solution: CSection/CNMEA live
    // in the GPS project (namespace AgOpenGPS) and ApplicationModel in AgOpenGPS.Core. They are stored
    // by reference so the Next handler mutates the same instances the application uses, exactly as the
    // WinForms handler mutated mf.*.
    private readonly CSection[] _section;
    private readonly CNMEA _pn;
    private readonly ApplicationModel _appModel;

    // [XPLAT] Rigid one-section tool configuration, injected as a delegate because CTool is excluded
    // from the current cross-platform build (it still references the WinForms FormGPS god-object; see
    // <Compile Remove="Classes\CTool.cs"> in AgOpenGPS.csproj). The host owns the live CTool and, given
    // the pivot distance (metres), must apply the EXACT field set the WinForms btnStart_Click did:
    //   tool.isSectionsNotZones = true;  tool.numOfSections = 1;
    //   tool.isToolRearFixed = true;     tool.isToolTrailing = false;
    //   tool.isToolTBT = false;          tool.isToolFrontFixed = false;
    //   tool.hitchLength = -pivotDistance;
    //   tool.trailingHitchLength = 0;    tool.tankTrailingHitchLength = 0;
    //   tool.trailingToolToPivotLength = 0;
    //   tool.offset = 0;                 tool.overlap = 0;
    private readonly Action<double> _configureRigidTool;

    // [XPLAT] Injected host operations that were FormGPS methods/field-writes. They are supplied as
    // delegates so this view stays free of any FormGPS/mf reference (AAP §0.3.2): the composition root
    // (the migrated host that owns the field life-cycle) wires them to the post-migration owners.
    private readonly Action _sectionCalcWidths;          // was mf.SectionCalcWidths()
    private readonly Action _jobNew;                     // was mf.JobNew()
    private readonly Action<string> _setCurrentFieldDirectory; // was mf.currentFieldDirectory = ...
    private readonly Action<bool> _setIsEasyDriveMode;   // was mf.isEasyDriveMode = ...

    // [XPLAT] Unit system (was mf.isMetric). Drives the metric/imperial branch of FormEasyDrive_Load.
    private bool _isMetric = true;

    // [XPLAT] One-shot guard so the FormEasyDrive_Load port (OnLoaded) configures the units exactly
    // once, matching the WinForms Load event which fired a single time per show (OnLoaded can otherwise
    // fire again on re-attach to the visual tree).
    private bool _unitsConfigured;

    // [XPLAT] Work-width field state. These mirror the WinForms nudWidth.DecimalPlaces / Minimum /
    // Maximum / Value: the Avalonia numeric field is a read-only display, so the limits + current value
    // are held here and handed to FormNumeric when the field is tapped. Seeded with the metric defaults
    // so the dialog is coherent even before ConfigureUnits runs.
    private double _workWidth = 6.0;
    private double _widthMinimum = 0.5;
    private double _widthMaximum = 100.0;
    private int _widthDecimals = 1;

    // [XPLAT] Hitch / pivot-distance field state (mirrors WinForms nudPivotDistance.*).
    private double _pivotDistance = 1.0;
    private double _pivotMinimum = 0.0;
    private double _pivotMaximum = 20.0;
    private int _pivotDecimals = 2;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time
    /// previewer (present to match the sibling-view convention, e.g. <c>FormShiftPosView</c>). It
    /// realises the markup, wires the migrated handlers, and assigns the localised captions + window
    /// title — the parity equivalent of the WinForms ctor. The running application always constructs
    /// the dialog through the
    /// <see cref="FormEasyDriveView(CSection[], CNMEA, ApplicationModel, Action{double}, Action, Action, Action{string}, Action{bool}, bool)"/>
    /// overload; an instance created this way has no injected collaborators, so <em>Next</em> closes
    /// without performing the (host-owned) tool/job setup.
    /// </summary>
    public FormEasyDriveView()
    {
        InitializeComponent();

        // [XPLAT] The .axaml declares only x:Name (no handlers); attach the migrated WinForms handlers
        // here by name (the convention used across the migrated GPS dialogs).
        nudWidth.Click += NudWidth_Click;
        nudPivotDistance.Click += NudPivotDistance_Click;
        btnStart.Click += BtnStart_Click;
        btnCancel.Click += BtnCancel_Click;

        // [XPLAT] Localised captions + window title (parity with the WinForms ctor, which set
        // lblInfo / lblWidth / lblPivot text from gStr and this.Text = "Easy Drive"). The WinForms
        // "Next" caption was on the button itself; in the Avalonia markup the button hosts a glyph plus
        // a child TextBlock (lblStart) that carries the text, so the caption is assigned to lblStart.
        lblInfo.Text = gStr.gsEasyDriveInfo;
        lblWidth.Text = gStr.gsWorkWidth;
        lblPivot.Text = gStr.gsHitchLength;
        lblStart.Text = gStr.gsNext;
        Title = "Easy Drive";
    }

    /// <summary>
    /// Creates the Easy Drive wizard bound to the live domain objects and host operations it must
    /// configure when <em>Next</em> is pressed. This is the constructor the running application uses.
    /// </summary>
    /// <param name="section">
    /// The section array whose first element receives the full-width left/right extents
    /// (was <c>mf.section</c>; the handler writes <c>section[0]</c>).
    /// </param>
    /// <param name="pn">
    /// The NMEA/position service used to define the local plane at the current GPS position
    /// (was <c>mf.pn</c>).
    /// </param>
    /// <param name="appModel">
    /// The shared application model supplying <see cref="ApplicationModel.CurrentLatLon"/>
    /// (was <c>mf.AppModel</c>).
    /// </param>
    /// <param name="configureRigidTool">
    /// Configures the live tool as a rigid, single-section implement given the pivot distance in metres
    /// (was the inline <c>mf.tool.*</c> block). Injected as a delegate because <c>CTool</c> is excluded
    /// from the current cross-platform build; the host applies the documented rigid-tool field set,
    /// including <c>tool.hitchLength = -pivotDistance</c>.
    /// </param>
    /// <param name="sectionCalcWidths">
    /// Recomputes section widths after the extents are set (was <c>mf.SectionCalcWidths()</c>).
    /// </param>
    /// <param name="jobNew">
    /// Starts a new (temporary) job (was <c>mf.JobNew()</c>).
    /// </param>
    /// <param name="setCurrentFieldDirectory">
    /// Sets the current field directory name (was <c>mf.currentFieldDirectory = ...</c>); the handler
    /// passes the literal "Easy Drive".
    /// </param>
    /// <param name="setIsEasyDriveMode">
    /// Sets the Easy-Drive-mode flag on the host (was <c>mf.isEasyDriveMode = ...</c>); the handler
    /// passes <see langword="true"/>.
    /// </param>
    /// <param name="isMetric">
    /// True for metric (metres) field limits/defaults, false for imperial (feet) (was
    /// <c>mf.isMetric</c>).
    /// </param>
    public FormEasyDriveView(
        CSection[] section,
        CNMEA pn,
        ApplicationModel appModel,
        Action<double> configureRigidTool,
        Action sectionCalcWidths,
        Action jobNew,
        Action<string> setCurrentFieldDirectory,
        Action<bool> setIsEasyDriveMode,
        bool isMetric)
        : this()
    {
        _section = section;
        _pn = pn;
        _appModel = appModel;
        _configureRigidTool = configureRigidTool;
        _sectionCalcWidths = sectionCalcWidths;
        _jobNew = jobNew;
        _setCurrentFieldDirectory = setCurrentFieldDirectory;
        _setIsEasyDriveMode = setIsEasyDriveMode;
        _isMetric = isMetric;
    }

    /// <summary>The tool work width currently shown (display units: metres when metric, feet otherwise).</summary>
    public double WorkWidth => _workWidth;

    /// <summary>The hitch / pivot distance currently shown (display units: metres when metric, feet otherwise).</summary>
    public double PivotDistance => _pivotDistance;

    /// <summary>True when the dialog is in metric mode (metres); false for imperial (feet).</summary>
    public bool IsMetric => _isMetric;

    /// <summary>
    /// [XPLAT] Port of the WinForms <c>FormEasyDrive_Load</c> handler. <see cref="Window"/>'s
    /// <see cref="Control.OnLoaded"/> is the Avalonia equivalent of the WinForms <c>Load</c> event: by
    /// the time it fires the named controls are realised, so the per-field decimals / minimum / maximum
    /// / initial value and the unit captions are applied here. A one-shot guard keeps it to a single run
    /// (matching the WinForms Load semantics) so a later re-attach never clobbers a value the operator
    /// has entered on the keypad.
    /// </summary>
    /// <param name="e">The event data passed to the base implementation.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (_unitsConfigured)
        {
            return;
        }

        _unitsConfigured = true;
        ConfigureUnits();
    }

    // [XPLAT] Metric/imperial branch of FormEasyDrive_Load. Sets the unit labels and the per-field
    // decimals / minimum / maximum / initial value, then renders the values. The exact limits and
    // defaults are reproduced verbatim from the WinForms source (do NOT change them).
    private void ConfigureUnits()
    {
        if (_isMetric)
        {
            lblUnitWidth.Text = "m";
            lblUnitPivot.Text = "m";

            _widthDecimals = 1;
            _widthMinimum = 0.5;
            _widthMaximum = 100.0;
            _workWidth = 6.0;

            _pivotDecimals = 2;
            _pivotMinimum = 0.0;
            _pivotMaximum = 20.0;
            _pivotDistance = 1.0;
        }
        else
        {
            lblUnitWidth.Text = "ft";
            lblUnitPivot.Text = "ft";

            _widthDecimals = 1;
            _widthMinimum = 1.0;
            _widthMaximum = 330.0;
            _workWidth = 20.0;

            _pivotDecimals = 1;
            _pivotMinimum = 0.0;
            _pivotMaximum = 66.0;
            _pivotDistance = 3.3;
        }

        UpdateDisplays();
    }

    // [XPLAT] Render both numeric display fields. The values are formatted with
    // CultureInfo.InvariantCulture so the decimal separator is locale-independent across Windows /
    // Linux / macOS (the culture hazard called out in AAP §0.6.5). The "F<decimals>" format reproduces
    // the WinForms NumericUpDown.DecimalPlaces rendering (e.g. 6.0, 1.00, 3.3). nudWidth /
    // nudPivotDistance are read-only display Buttons, so the text is assigned to Button.Content.
    private void UpdateDisplays()
    {
        nudWidth.Content = FormatValue(_workWidth, _widthDecimals);
        nudPivotDistance.Content = FormatValue(_pivotDistance, _pivotDecimals);
    }

    // [XPLAT] InvariantCulture "F<decimals>" formatter shared by the display refresh and the keypad
    // write-back, so every rendered number uses the same locale-independent representation.
    private static string FormatValue(double value, int decimals)
    {
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // [XPLAT] nudWidth tap -> open the numeric keypad for the work width (parity with
    // NudlessNumericUpDown.ShowKeypad). FormNumeric (Views/Inputs) is the cross-platform replacement for
    // the WinForms keypad; it is shown modally and returns true on accept, exposing the chosen value via
    // ReturnValue. async void is the established handler signature for the migrated dialogs (it awaits
    // ShowDialog, so there is no CS1998).
    private async void NudWidth_Click(object sender, RoutedEventArgs e)
    {
        var keypad = new FormNumeric(_widthMinimum, _widthMaximum, _workWidth);
        if (await keypad.ShowDialog<bool>(this))
        {
            _workWidth = keypad.ReturnValue;
            nudWidth.Content = FormatValue(_workWidth, _widthDecimals);
        }
    }

    // [XPLAT] nudPivotDistance tap -> open the numeric keypad for the hitch / pivot distance.
    private async void NudPivotDistance_Click(object sender, RoutedEventArgs e)
    {
        var keypad = new FormNumeric(_pivotMinimum, _pivotMaximum, _pivotDistance);
        if (await keypad.ShowDialog<bool>(this))
        {
            _pivotDistance = keypad.ReturnValue;
            nudPivotDistance.Content = FormatValue(_pivotDistance, _pivotDecimals);
        }
    }

    // [XPLAT] btnStart (Next) -> the full body of the WinForms btnStart_Click, run against the injected
    // collaborators instead of mf.*. Configures a rigid one-section tool spanning the entered width,
    // recomputes section widths, defines the local plane at the current GPS position, names a temporary
    // "Easy Drive" field, starts a new job, sets Easy-Drive mode, and closes with a positive result. The
    // ordering matches the WinForms source exactly.
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        // [XPLAT] Design-time / no-injection safety: an instance built via the parameterless
        // constructor (previewer) has no collaborators. The running application always uses the
        // injection constructor, so this guard never triggers in production; it only prevents a
        // NullReferenceException if Next is somehow invoked without injected state.
        if (_section == null || _section.Length == 0 || _pn == null || _appModel == null)
        {
            Close(true);
            return;
        }

        // [XPLAT] Resolve the configured values. Metric inputs are used directly; imperial inputs are
        // converted feet -> metres (the exact 0.3048 factor from the WinForms source). Pure numeric
        // math, so it is culture-independent.
        double width;
        double pivotDistance;
        if (_isMetric)
        {
            width = _workWidth;
            pivotDistance = _pivotDistance;
        }
        else
        {
            width = _workWidth * 0.3048;
            pivotDistance = _pivotDistance * 0.3048;
        }

        // [XPLAT] Configure the tool in memory (rigid, 1 section). The host applies the documented
        // rigid-tool field set (including tool.hitchLength = -pivotDistance) via the injected delegate,
        // because CTool is excluded from the current cross-platform build.
        _configureRigidTool?.Invoke(pivotDistance);

        // [XPLAT] Single section spanning the full width (section[0] gets the symmetric extents), then
        // recompute the derived section widths via the injected host operation.
        double halfWidth = width / 2.0;
        _section[0].positionLeft = -halfWidth;
        _section[0].positionRight = halfWidth;
        _sectionCalcWidths?.Invoke();

        // [XPLAT] Define the local plane at the current GPS position (was
        // mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false)).
        _pn.DefineLocalPlane(_appModel.CurrentLatLon, false);

        // [XPLAT] Set up a temporary field (no directory on disk), start the job, and flag Easy-Drive
        // mode — the FormGPS field-lifecycle writes, now performed through the injected delegates.
        _setCurrentFieldDirectory?.Invoke("Easy Drive");
        _jobNew?.Invoke();
        _setIsEasyDriveMode?.Invoke(true);

        // [XPLAT] WinForms DialogResult.OK -> ShowDialog<bool> returns true.
        Close(true);
    }

    // [XPLAT] btnCancel -> close without committing (WinForms DialogResult.Cancel -> ShowDialog<bool>
    // returns false).
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
