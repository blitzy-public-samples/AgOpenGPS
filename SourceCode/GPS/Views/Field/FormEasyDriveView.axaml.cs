// [XPLAT] migrated from net48/WinForms (Forms/Field/FormEasyDrive.cs + FormEasyDrive.Designer.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for the "Easy Drive" quick-start wizard — a 1:1 behavioural-parity
/// reimplementation of the WinForms <c>FormEasyDrive</c> (FormEasyDrive.cs + FormEasyDrive.Designer.cs).
/// The operator enters a tool work width and a hitch/pivot distance, then taps Next to start a
/// temporary one-section rigid-tool "Easy Drive" job.
/// </summary>
/// <remarks>
/// <para>
/// The original form held a <c>private readonly FormGPS mf</c> back-reference and, on Next, mutated
/// the FormGPS god-object directly (<c>mf.tool.*</c>, <c>mf.section[0]</c>, <c>SectionCalcWidths()</c>,
/// <c>mf.pn.DefineLocalPlane(...)</c>, <c>mf.currentFieldDirectory</c>, <c>mf.JobNew()</c>,
/// <c>mf.isEasyDriveMode</c>). Per the migration's Dependency-Inversion strategy (AAP §0.3.2) that
/// guidance/field-lifecycle coupling is NOT reproduced in the view: this dialog only surfaces the
/// entered values and raises <see cref="Accepted"/>; the application host performs the rigid-tool
/// configuration and starts the temporary job. This mirrors the sibling FormSimCoordsView, whose OK
/// likewise hands its values to the host instead of calling into FormGPS.
/// </para>
/// <para>
/// The paired <c>FormEasyDriveView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls — keeping their
/// original WinForms names — and wires no handlers. This code-behind attaches the migrated handlers by
/// name and assigns the localised <see cref="gStr"/> captions + unit/value text at runtime, exactly as
/// the WinForms ctor + <c>FormEasyDrive_Load</c> did.
/// </para>
/// <para>
/// The WinForms numeric fields were <c>NudlessNumericUpDown</c> controls whose click opened the
/// on-screen keypad (<c>ShowKeypad</c>). FormNumeric (Views/Inputs) exposes no cross-assembly
/// configure/result API — its <c>x:Name</c> controls are internal to its own partial class — so, per
/// AAP §0.3.2, opening the keypad and writing the chosen value back is host-owned: a click raises
/// <see cref="NumericEditRequested"/> with the field's min/max/decimals, and the host opens FormNumeric
/// and calls <see cref="SetWorkWidth(double)"/> / <see cref="SetPivotDistance(double)"/>. Avalonia's
/// spinner <c>NumericUpDown</c> is deliberately NOT used (parity = keypad entry, not spin).
/// </para>
/// </remarks>
public partial class FormEasyDriveView : Window
{
    /// <summary>Identifies which numeric field requested a keypad edit.</summary>
    public enum EasyDriveField
    {
        /// <summary>The tool work width field (<c>nudWidth</c>).</summary>
        Width,

        /// <summary>The hitch / pivot distance field (<c>nudPivotDistance</c>).</summary>
        PivotDistance
    }

    /// <summary>
    /// Event payload for a numeric keypad-edit request raised from a display-field click. Carries the
    /// everything the host needs to open FormNumeric with the same limits the WinForms
    /// NudlessNumericUpDown enforced.
    /// </summary>
    public sealed class EasyDriveNumericEditEventArgs : EventArgs
    {
        /// <summary>Creates the keypad-edit request payload.</summary>
        public EasyDriveNumericEditEventArgs(
            EasyDriveField field,
            double currentValue,
            double minimum,
            double maximum,
            int decimalPlaces,
            string unit)
        {
            Field = field;
            CurrentValue = currentValue;
            Minimum = minimum;
            Maximum = maximum;
            DecimalPlaces = decimalPlaces;
            Unit = unit;
        }

        /// <summary>Which field is being edited.</summary>
        public EasyDriveField Field { get; }

        /// <summary>The value currently shown in the field (display units).</summary>
        public double CurrentValue { get; }

        /// <summary>The minimum the keypad must enforce (display units).</summary>
        public double Minimum { get; }

        /// <summary>The maximum the keypad must enforce (display units).</summary>
        public double Maximum { get; }

        /// <summary>The number of decimal places to display / accept.</summary>
        public int DecimalPlaces { get; }

        /// <summary>The display unit ("m" or "ft").</summary>
        public string Unit { get; }
    }

    /// <summary>
    /// Event payload carrying the accepted Easy Drive inputs. Values are in the dialog's DISPLAY units;
    /// <see cref="IsMetric"/> tells the host whether to convert feet to metres (the WinForms Next
    /// handler multiplied imperial inputs by 0.3048 before configuring the tool).
    /// </summary>
    public sealed class EasyDriveAcceptedEventArgs : EventArgs
    {
        /// <summary>Creates the accepted-inputs payload.</summary>
        public EasyDriveAcceptedEventArgs(double workWidth, double pivotDistance, bool isMetric)
        {
            WorkWidth = workWidth;
            PivotDistance = pivotDistance;
            IsMetric = isMetric;
        }

        /// <summary>The entered tool work width (display units; metres when <see cref="IsMetric"/>).</summary>
        public double WorkWidth { get; }

        /// <summary>The entered hitch / pivot distance (display units; metres when <see cref="IsMetric"/>).</summary>
        public double PivotDistance { get; }

        /// <summary>True when the values are metric (metres); false when imperial (feet).</summary>
        public bool IsMetric { get; }
    }

    // [XPLAT] Per-field limits + current value, mirroring the WinForms FormEasyDrive_Load setup. They
    // are seeded with the metric defaults so the dialog is fully usable even before the host calls
    // ConfigureUnits; ConfigureUnits then applies the unit-appropriate limits/initial values.
    private bool _isMetric = true;

    private double _workWidth = 6.0;
    private double _widthMinimum = 0.5;
    private double _widthMaximum = 100.0;
    private int _widthDecimals = 1;

    private double _pivotDistance = 1.0;
    private double _pivotMinimum = 0.0;
    private double _pivotMaximum = 20.0;
    private int _pivotDecimals = 2;

    /// <summary>
    /// Parameterless constructor used by the Avalonia runtime XAML loader and the design-time
    /// previewer. The application constructs the dialog this way, then calls
    /// <see cref="ConfigureUnits(bool)"/> with the live metric flag (parity with the WinForms ctor,
    /// which read <c>mf.isMetric</c> in FormEasyDrive_Load).
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
        // lblInfo / lblWidth / lblPivot / btnStart text from gStr and this.Text = "Easy Drive").
        lblInfo.Text = gStr.gsEasyDriveInfo;
        lblWidth.Text = gStr.gsWorkWidth;
        lblPivot.Text = gStr.gsHitchLength;
        lblStart.Text = gStr.gsNext;
        Title = "Easy Drive";

        // Render the seeded metric defaults so the display fields show a value immediately.
        UpdateDisplays();
    }

    /// <summary>The tool work width currently shown (display units: metres when metric, feet otherwise).</summary>
    public double WorkWidth => _workWidth;

    /// <summary>The hitch / pivot distance currently shown (display units: metres when metric, feet otherwise).</summary>
    public double PivotDistance => _pivotDistance;

    /// <summary>True when the dialog is in metric mode (metres); false for imperial (feet).</summary>
    public bool IsMetric => _isMetric;

    /// <summary>
    /// Host entry point that reproduces <c>FormEasyDrive_Load</c>: selects the unit captions and the
    /// per-field decimals / minimum / maximum / initial value for metric or imperial mode. Call this
    /// once after constructing the dialog, before showing it.
    /// </summary>
    /// <param name="isMetric">True for metric (metres), false for imperial (feet).</param>
    public void ConfigureUnits(bool isMetric)
    {
        _isMetric = isMetric;

        if (isMetric)
        {
            // [XPLAT] Metric branch of FormEasyDrive_Load.
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
            // [XPLAT] Imperial branch of FormEasyDrive_Load.
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

    /// <summary>
    /// Host write-back from the numeric keypad for the work width (parity with the WinForms
    /// NudlessNumericUpDown value assignment). The value is clamped to the configured min/max exactly
    /// as the WinForms control did, then re-displayed.
    /// </summary>
    /// <param name="value">The value chosen on the keypad (display units).</param>
    public void SetWorkWidth(double value)
    {
        _workWidth = Clamp(value, _widthMinimum, _widthMaximum);
        UpdateDisplays();
    }

    /// <summary>
    /// Host write-back from the numeric keypad for the hitch / pivot distance. Clamped to the
    /// configured min/max and re-displayed.
    /// </summary>
    /// <param name="value">The value chosen on the keypad (display units).</param>
    public void SetPivotDistance(double value)
    {
        _pivotDistance = Clamp(value, _pivotMinimum, _pivotMaximum);
        UpdateDisplays();
    }

    /// <summary>
    /// Raised when a numeric display field is clicked; the host opens FormNumeric with the supplied
    /// limits and calls <see cref="SetWorkWidth(double)"/> / <see cref="SetPivotDistance(double)"/>
    /// with the result (the keypad seam shared with the sibling FormSimCoordsView).
    /// </summary>
    public event EventHandler<EasyDriveNumericEditEventArgs> NumericEditRequested;

    /// <summary>
    /// Raised when Next is pressed; the host configures the rigid one-section tool and starts the
    /// temporary "Easy Drive" job from the supplied values (the FormGPS work the WinForms Next handler
    /// performed inline). The dialog closes with a positive result immediately after.
    /// </summary>
    public event EventHandler<EasyDriveAcceptedEventArgs> Accepted;

    // [XPLAT] Render both numeric display fields. Values are formatted with CultureInfo.InvariantCulture
    // so the decimal separator is locale-independent across Windows / Linux / macOS — the culture hazard
    // called out in AAP §0.6.5 (a comma separator would misrepresent the value on non-US locales). The
    // "F<decimals>" format reproduces the WinForms NumericUpDown.DecimalPlaces rendering (e.g. 6.0,
    // 1.00).
    private void UpdateDisplays()
    {
        nudWidth.Content = _workWidth.ToString("F" + _widthDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        nudPivotDistance.Content = _pivotDistance.ToString("F" + _pivotDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // [XPLAT] nudWidth click -> request a keypad edit for the work width (parity with
    // NudlessNumericUpDown.ShowKeypad). The host opens FormNumeric and writes the result back via
    // SetWorkWidth.
    private void NudWidth_Click(object sender, RoutedEventArgs e)
    {
        NumericEditRequested?.Invoke(
            this,
            new EasyDriveNumericEditEventArgs(
                EasyDriveField.Width,
                _workWidth,
                _widthMinimum,
                _widthMaximum,
                _widthDecimals,
                _isMetric ? "m" : "ft"));
    }

    // [XPLAT] nudPivotDistance click -> request a keypad edit for the hitch / pivot distance.
    private void NudPivotDistance_Click(object sender, RoutedEventArgs e)
    {
        NumericEditRequested?.Invoke(
            this,
            new EasyDriveNumericEditEventArgs(
                EasyDriveField.PivotDistance,
                _pivotDistance,
                _pivotMinimum,
                _pivotMaximum,
                _pivotDecimals,
                _isMetric ? "m" : "ft"));
    }

    // [XPLAT] btnStart (Next) -> hand the entered values to the host (which configures the rigid
    // one-section tool and starts the temporary job, as the WinForms Next handler did inline) and close
    // with a positive result. Close(true) supplies the result to a ShowDialog<bool> caller.
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        Accepted?.Invoke(this, new EasyDriveAcceptedEventArgs(_workWidth, _pivotDistance, _isMetric));
        Close(true);
    }

    // [XPLAT] btnCancel -> close without committing (WinForms DialogResult.Cancel).
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // [XPLAT] Clamp helper reproducing the WinForms NumericUpDown Minimum/Maximum clamp. A pure numeric
    // operation, so it is culture-independent.
    private static double Clamp(double value, double minimum, double maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }
}
