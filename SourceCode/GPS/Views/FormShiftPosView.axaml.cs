// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Globalization;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] GPS antenna position-shift (drift-compensation) editor.
///
/// 1:1 behavioral parity reimplementation of the WinForms <c>Forms/FormShiftPos</c>
/// (FormShiftPos.cs + FormShiftPos.Designer.cs). The operator nudges a North/South and an East/West
/// offset, expressed in <b>centimetres</b>, that is added to the GPS Lat/Lon (via
/// <see cref="GeoDelta"/> drift compensation) and persisted in the field's FIELD.KML when the field
/// is closed. The dialog can zero the offset, can toggle whether the offset is kept after the field
/// closes, and applies its edits on OK.
///
/// <para>
/// The original form held a <c>private readonly FormGPS mf</c> back-reference and read/wrote
/// <c>mf.AppModel.SharedFieldProperties.DriftCompensation</c> and <c>mf.isKeepOffsetsOn</c>. Per the
/// migration's Dependency-Inversion strategy (AAP §0.3.2) that WinForms host coupling is replaced by
/// constructor-injecting the same portable <see cref="ApplicationModel"/> the composition root builds;
/// the drift offset lives on <see cref="ApplicationModel.SharedFieldProperties"/> and the keep-offsets
/// flag was relocated onto <see cref="ApplicationModel.isKeepOffsetsOn"/> (the canonical home the field
/// life-cycle logic reads when deciding whether to reset the offset on field close).
/// </para>
///
/// <para>
/// The paired <c>FormShiftPosView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls — keeping their
/// original WinForms names — and wires no handlers. This code-behind attaches the migrated WinForms
/// handlers by name, so the markup and the behavior stay decoupled exactly as the original
/// Designer/code-behind split did.
/// </para>
///
/// <para>
/// The on-screen <c>NumKeypad</c> that the WinForms numeric fields opened on tap
/// (<c>NudlessNumericUpDown.ShowKeypad</c>) was a <c>mf</c>/WinForms-coupled convenience with no
/// Avalonia equivalent on the migrated control; it is intentionally not reproduced here. Functional
/// parity is preserved because the numeric fields remain directly editable and the offset is written
/// back to the model on OK (and on Zero), and the dialog exposes no Cancel/close-box escape that could
/// bypass that write.
/// </para>
/// </summary>
public partial class FormShiftPosView : Window
{
    // [XPLAT] Replaces the WinForms `private readonly FormGPS mf` back-reference (AAP §0.3.2). The
    // same portable ApplicationModel the composition root builds is injected, giving this view direct,
    // WinForms-free access to SharedFieldProperties.DriftCompensation and the keep-offsets flag.
    private readonly ApplicationModel _appModel;

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia runtime XAML loader and the design-time
    /// previewer only — present to match the sibling-view convention (and to satisfy the runtime
    /// loader, which requires a public parameterless constructor). The running application always
    /// constructs this dialog through the <see cref="FormShiftPosView(ApplicationModel)"/> overload;
    /// this inert overload merely renders the static design-time markup and wires no behavior, so a
    /// preview instance never touches the (absent) model.
    /// </summary>
    public FormShiftPosView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the drift-compensation editor bound to the supplied <paramref name="appModel"/>.
    /// </summary>
    /// <param name="appModel">
    /// The shared application model carrying <see cref="ApplicationModel.SharedFieldProperties"/>
    /// (the live <see cref="GeoDelta"/> drift offset) and <see cref="ApplicationModel.isKeepOffsetsOn"/>.
    /// </param>
    public FormShiftPosView(ApplicationModel appModel)
        : this()
    {
        _appModel = appModel;

        // [XPLAT] Force invariant number formatting and parsing on both numeric fields. The values are
        // plain centimetres and must display and round-trip identically regardless of the operating
        // system locale; a comma decimal/grouping separator would otherwise corrupt the round-trip on
        // non-US locales (the culture hazard called out in AAP §0.6.5).
        nudNorth.NumberFormat = CultureInfo.InvariantCulture.NumberFormat;
        nudEast.NumberFormat = CultureInfo.InvariantCulture.NumberFormat;

        // [XPLAT] The .axaml declares only x:Name (no handlers); attach the migrated WinForms handlers
        // here by name. The four direction buttons are Avalonia RepeatButtons, so a held press raises
        // Click repeatedly — reproducing the hold-to-repeat nudging of the WinForms RepeatButton +
        // NumericUpDown.UpButton()/DownButton() pairing.
        btnNorth.Click += OnNorthClick;
        btnSouth.Click += OnSouthClick;
        btnWest.Click += OnWestClick;
        btnEast.Click += OnEastClick;
        btnZero.Click += OnZeroClick;
        bntOK.Click += OnOkClick;

        // [XPLAT] IsCheckedChanged (not the obsolete Checked/Unchecked events) fires on either
        // transition, reproducing the WinForms chkOffsetsOn_Click caption refresh on every toggle.
        chkOffsetsOn.IsCheckedChanged += OnOffsetsToggled;

        LoadFromModel();
    }

    // [XPLAT] Combines the WinForms ctor's gStr localisation + Title and FormShiftPos_Load's model
    // read. Running it once at construction (the Avalonia idiom) is equivalent to the WinForms Load
    // event for this fixed, immediately-shown dialog.
    private void LoadFromModel()
    {
        // [XPLAT] Localised direction captions and window title (parity with the WinForms ctor, which
        // overwrote the designer's English captions with the translated strings).
        label27.Text = gStr.gsNorth;
        label2.Text = gStr.gsWest;
        label3.Text = gStr.gsEast;
        label4.Text = gStr.gsSouth;
        Title = $"{gStr.gsShiftGPSPosition} ({gStr.gsCm})";

        // [XPLAT] FormShiftPos_Load: the drift offset is stored as a GeoDelta in metres, while this
        // editor works in centimetres, so the displayed value is 100 × the metre delta. The exact
        // scale from the WinForms source is preserved (do NOT change it).
        GeoDelta driftCompensation = _appModel.SharedFieldProperties.DriftCompensation;
        nudNorth.Value = 100m * (decimal)driftCompensation.NorthingDelta;
        nudEast.Value = 100m * (decimal)driftCompensation.EastingDelta;

        // [XPLAT] Reflect the relocated keep-offsets flag and sync the On/Off caption (parity with the
        // WinForms load path that set chkOffsetsOn.Checked = mf.isKeepOffsetsOn and the caption text).
        chkOffsetsOn.IsChecked = _appModel.isKeepOffsetsOn;
        UpdateOffsetsCaption();
    }

    // [XPLAT] btnNorth -> step nudNorth up (parity with nudNorth.UpButton()). ClipValueToMinMax on the
    // control clamps to [-9999, 9999] exactly as the WinForms Minimum/Maximum did. Matching the source,
    // the arrow buttons only move the field; the model is written on Zero/OK, not on each nudge.
    private void OnNorthClick(object sender, RoutedEventArgs e)
    {
        nudNorth.Value = (nudNorth.Value ?? 0m) + nudNorth.Increment;
    }

    // [XPLAT] btnSouth -> step nudNorth down (parity with nudNorth.DownButton()).
    private void OnSouthClick(object sender, RoutedEventArgs e)
    {
        nudNorth.Value = (nudNorth.Value ?? 0m) - nudNorth.Increment;
    }

    // [XPLAT] btnWest -> step nudEast down; West is the negative East direction (parity with
    // nudEast.DownButton()).
    private void OnWestClick(object sender, RoutedEventArgs e)
    {
        nudEast.Value = (nudEast.Value ?? 0m) - nudEast.Increment;
    }

    // [XPLAT] btnEast -> step nudEast up; East is the positive direction (parity with
    // nudEast.UpButton()).
    private void OnEastClick(object sender, RoutedEventArgs e)
    {
        nudEast.Value = (nudEast.Value ?? 0m) + nudEast.Increment;
    }

    // [XPLAT] btnZero -> zero both fields and write the zeroed offset straight back (parity with
    // FormShiftPos.btnZero_Click: nudEast.Value = 0; nudNorth.Value = 0; SetFixDelta();).
    private void OnZeroClick(object sender, RoutedEventArgs e)
    {
        nudEast.Value = 0m;
        nudNorth.Value = 0m;
        SetFixDelta();
    }

    // [XPLAT] bntOK -> persist the keep-offsets flag, write the offset back, and close with a positive
    // dialog result (parity with FormShiftPos.bntOK_Click: mf.isKeepOffsetsOn = chkOffsetsOn.Checked;
    // SetFixDelta(); Close();). Close(true) supplies the result to a ShowDialog<bool> caller.
    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        _appModel.isKeepOffsetsOn = chkOffsetsOn.IsChecked == true;
        SetFixDelta();
        Close(true);
    }

    // [XPLAT] chkOffsetsOn -> refresh the On/Off caption (parity with chkOffsetsOn_Click). Subscribed
    // to both Checked and Unchecked so either transition updates the caption.
    private void OnOffsetsToggled(object sender, RoutedEventArgs e)
    {
        UpdateOffsetsCaption();
    }

    // [XPLAT] SetFixDelta: write the centimetre fields back into DriftCompensation as a GeoDelta in
    // metres — the EXACT inverse of the load scale (metres = centimetres / 100). The GeoDelta argument
    // order is (northingDelta, eastingDelta), matching the WinForms source. Division by 100.0 is a pure
    // numeric (culture-independent) operation, consistent with the InvariantCulture mandate (AAP §0.6.5).
    private void SetFixDelta()
    {
        _appModel.SharedFieldProperties.DriftCompensation = new GeoDelta(
            (double)(nudNorth.Value ?? 0m) / 100.0,
            (double)(nudEast.Value ?? 0m) / 100.0);
    }

    // [XPLAT] Mirror the WinForms On/Off caption on the keep-offsets toggle button (the WinForms
    // CheckBox used Appearance.Button with its Text flipped between "On" and "Off").
    private void UpdateOffsetsCaption()
    {
        chkOffsetsOn.Content = chkOffsetsOn.IsChecked == true ? "On" : "Off";
    }
}
