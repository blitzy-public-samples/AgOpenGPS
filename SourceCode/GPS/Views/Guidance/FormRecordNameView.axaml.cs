// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for the "Create New Record" name-entry dialog — a 1:1 behavioral-parity
/// reimplementation of the WinForms <c>Forms/Guidance/FormRecordName</c> (FormRecordName.cs +
/// FormRecordName.Designer.cs). The operator types a record name (sanitised live against
/// <c>glm.fileRegex</c>), optionally appends the current date and/or time, then saves; the resulting
/// name is published through the public <see cref="filename"/> field and the dialog closes positive.
/// Per AAP §0.3.3 the behaviour is unchanged — only the framework underneath it is.
/// </summary>
/// <remarks>
/// <para>
/// The paired <c>FormRecordNameView.axaml</c> is intentionally imperative (no <c>x:DataType</c>, no
/// <c>DataContext</c>, no bindings): it declares only <c>x:Name</c>'d controls — keeping their
/// original WinForms names — and wires no handlers. This code-behind attaches the migrated WinForms
/// handlers by name, so the markup and the behaviour stay decoupled exactly as the original
/// Designer/code-behind split did (the documented convention of the sibling FormShiftPosView).
/// </para>
/// <para>
/// Framework conversions (each tagged <c>// [XPLAT]</c> below):
/// </para>
/// <list type="bullet">
///   <item>The WinForms <c>private readonly FormGPS mf</c> back-reference — used <em>only</em> to read
///         <c>mf.isKeyboardOn</c> — is replaced by the injected <see cref="_keyboardOn"/> flag the
///         composition root supplies (<c>Properties.Settings.Default.setDisplay_isKeyboardOn</c>),
///         eliminating the FormGPS god-object coupling (AAP §0.3.2).</item>
///   <item><c>tboxFieldName_TextChanged</c> -> <see cref="OnFieldNameTextChanged"/>: the same
///         <c>glm.fileRegex</c> sanitisation, with the caret preserved via Avalonia's
///         <see cref="TextBox.CaretIndex"/> (WinForms <c>SelectionStart</c>).</item>
///   <item><c>tboxFieldName_Click</c> -> <see cref="OnFieldNameTapped"/>: the on-screen keyboard
///         (<see cref="FormKeyboard"/>) is opened on tap, gated by the injected keyboard flag;
///         <c>TextBox.ShowKeyboard(this)</c> becomes a modal <c>ShowDialog</c>.</item>
///   <item><c>buttonSave_Click</c> -> <see cref="OnSaveClick"/> and <c>buttonRecordCancel_Click</c>
///         -> <see cref="OnCancelClick"/>: the WinForms <c>DialogResult.OK</c>/<c>Cancel</c> becomes
///         the Avalonia modal pattern (<c>Close(true)</c>/<c>Close(false)</c>), consumed by an
///         <c>await ShowDialog&lt;bool&gt;(owner)</c> caller.</item>
///   <item><c>FormRecordName_Load</c> -> <see cref="Window.OnOpened(EventArgs)"/>; the WinForms
///         <c>ScreenHelper.IsOnScreen</c> reposition is intentionally omitted because the .axaml's
///         <c>WindowStartupLocation="CenterOwner"</c> handles placement.</item>
/// </list>
/// </remarks>
public partial class FormRecordNameView : Window
{
    // [XPLAT] Replaces the WinForms `private readonly FormGPS mf` back-reference (AAP §0.3.2). The
    // original constructor captured the whole FormGPS solely to read `mf.isKeyboardOn`; that single
    // coupling is replaced by this injected flag, which the composition root / caller supplies from
    // Properties.Settings.Default.setDisplay_isKeyboardOn. No FormGPS reference remains.
    private readonly bool _keyboardOn;

    // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside the TextChanged handler re-raises
    // TextChanged, so this flag suppresses the recursive pass while we rewrite the sanitised text
    // (the proven sibling FormConvertProfilesView idiom).
    private bool _suppressTextChanged;

    /// <summary>
    /// The validated record name the operator entered: the trimmed text, with the optional
    /// " yyyy-MM-dd" / " HH-mm" suffixes appended on save. [XPLAT] Preserves the original public field
    /// name <c>filename</c> from <c>FormRecordName.cs</c> so the result contract is unchanged. Empty
    /// until <see cref="OnSaveClick"/> runs; the dialog closes positive (<c>Close(true)</c>) only when
    /// a non-empty name was entered.
    /// </summary>
    public string filename = string.Empty;

    /// <summary>
    /// [XPLAT] Parameterless constructor required by the Avalonia runtime XAML loader and the
    /// design-time previewer: a XAML-backed <see cref="Window"/> must expose a public parameterless
    /// constructor to be reachable via the runtime loader (the AVLN3001 contract). Matches the sibling
    /// FormShiftPosView convention and simply chains to <see cref="FormRecordNameView(bool)"/> with the
    /// on-screen keyboard disabled, so even a loader-constructed instance is fully wired. The running
    /// application always constructs the dialog through the <see cref="FormRecordNameView(bool)"/>
    /// overload (the documented caller pattern).
    /// </summary>
    public FormRecordNameView()
        : this(false)
    {
    }

    /// <summary>
    /// Initializes the dialog. [XPLAT] The WinForms ctor took <c>(Form callingForm)</c> only to read
    /// <c>mf.isKeyboardOn</c>; that single coupling is replaced by the injected
    /// <paramref name="keyboardOn"/> flag (default <see langword="false"/>), so the dialog never reaches
    /// back into FormGPS. The body mirrors the original: build the controls, then translate the prompt
    /// caption with <see cref="gStr.gsEnterRecordName"/>.
    /// </summary>
    /// <param name="keyboardOn">
    /// When <see langword="true"/>, tapping the name field opens the on-screen keyboard
    /// (<see cref="FormKeyboard"/>); supplied by the caller from
    /// <c>Properties.Settings.Default.setDisplay_isKeyboardOn</c>.
    /// </param>
    public FormRecordNameView(bool keyboardOn = false)
    {
        // [XPLAT] Store the injected keyboard flag (was `mf = _callingForm as FormGPS`), set before
        // InitializeComponent to mirror the original ctor's field-assignment order.
        _keyboardOn = keyboardOn;

        InitializeComponent();

        // Parity with the FormRecordName.cs ctor: translate the prompt caption.
        labelEnterRecordName.Text = gStr.gsEnterRecordName;

        // [XPLAT] The .axaml declares only x:Name (no handlers); attach the migrated WinForms handlers
        // here by name. WinForms wired tboxFieldName.TextChanged + tboxFieldName.Click +
        // buttonSave.Click + buttonRecordCancel.Click in the Designer; the textbox Click becomes a
        // Tapped gesture (the editable-textbox keyboard-summon convention of the sibling name dialogs).
        tboxFieldName.TextChanged += OnFieldNameTextChanged;
        tboxFieldName.Tapped += OnFieldNameTapped;
        buttonSave.Click += OnSaveClick;
        buttonRecordCancel.Click += OnCancelClick;
    }

    /// <summary>
    /// [XPLAT] WinForms <c>FormRecordName_Load</c> -> Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
    /// start with Save disabled, the preview label empty, and focus in the name field. The WinForms
    /// <c>ScreenHelper.IsOnScreen</c> reposition is intentionally not ported — the .axaml's
    /// <c>WindowStartupLocation="CenterOwner"</c> handles placement.
    /// </summary>
    /// <param name="e">The event data passed to the base implementation.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        buttonSave.IsEnabled = false;
        labelFilename.Text = string.Empty;
        tboxFieldName.Focus();
    }

    /// <summary>
    /// [XPLAT] Reproduces <c>tboxFieldName_TextChanged</c>: strip characters disallowed in a file name
    /// via <c>glm.fileRegex</c> while preserving the caret (WinForms <c>SelectionStart</c> ->
    /// Avalonia <see cref="TextBox.CaretIndex"/>), enable <c>buttonSave</c> only for a non-empty
    /// trimmed name, and mirror the trimmed name into the <c>labelFilename</c> preview. The text is
    /// rewritten only when the sanitiser actually changed it, so an already-clean keystroke never
    /// disturbs the caret; a re-entrancy guard suppresses the recursive event the rewrite raises.
    /// </summary>
    /// <param name="sender">The <see cref="TextBox"/> raising the change (the name field).</param>
    /// <param name="e">The change event data (unused).</param>
    private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        var textBox = (TextBox)sender;

        // [XPLAT] WinForms SelectionStart -> Avalonia CaretIndex.
        int caret = textBox.CaretIndex;
        string raw = textBox.Text ?? string.Empty;
        string clean = Regex.Replace(raw, glm.fileRegex, string.Empty);

        if (!string.Equals(clean, raw, StringComparison.Ordinal))
        {
            _suppressTextChanged = true;
            try
            {
                textBox.Text = clean;
                textBox.CaretIndex = Math.Min(caret, clean.Length);
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }

        // Enable Save only on a non-empty trimmed name; mirror the trimmed name into the live preview.
        string trimmed = (textBox.Text ?? string.Empty).Trim();
        buttonSave.IsEnabled = !string.IsNullOrEmpty(trimmed);
        labelFilename.Text = trimmed;
    }

    /// <summary>
    /// [XPLAT] Reproduces <c>tboxFieldName_Click</c> with the FormGPS coupling removed: when the
    /// injected <see cref="_keyboardOn"/> flag is set, tapping the field opens the migrated on-screen
    /// keyboard (<see cref="FormKeyboard"/>) seeded with the current text. The accepted value is written
    /// back (a cancelled keyboard returns <see langword="null"/> and leaves the text unchanged), then
    /// focus moves off the field — parity with the original <c>buttonRecordCancel.Focus()</c>.
    /// </summary>
    /// <param name="sender">The name field raising the tap (unused).</param>
    /// <param name="e">The tap gesture data (unused).</param>
    private async void OnFieldNameTapped(object sender, TappedEventArgs e)
    {
        if (!_keyboardOn)
        {
            return;
        }

        // [XPLAT] replaces TextBox.ShowKeyboard(this): open the migrated on-screen keyboard modally,
        // seeded with the current text. The project compiles with nullable reference types disabled,
        // so the result uses the unannotated `string` (an annotated `string?` would emit CS8632);
        // a cancelled FormKeyboard still completes with null (FormKeyboard calls Close(null)) — exactly
        // as ShowDialog<string?> would — so the null cancel-check below is preserved.
        var keyboard = new FormKeyboard(tboxFieldName.Text ?? string.Empty);
        string result = await keyboard.ShowDialog<string>(this);
        if (result != null)
        {
            tboxFieldName.Text = result;
        }

        // Parity with the WinForms `buttonRecordCancel.Focus();` after the keyboard closes.
        buttonRecordCancel.Focus();
    }

    /// <summary>
    /// [XPLAT] Reproduces <c>buttonSave_Click</c>: optionally append the current date (" yyyy-MM-dd")
    /// and/or time (" HH-mm"), publish the composed name through <see cref="filename"/>, and close
    /// positive. The <see cref="CultureInfo.InvariantCulture"/> on both
    /// <see cref="DateTime.ToString(string, IFormatProvider)"/> calls is MANDATORY (AAP §0.6.5): the
    /// record name becomes a file path, so the operating-system locale must never alter it (the source
    /// already used InvariantCulture — preserved exactly).
    /// </summary>
    /// <param name="sender">The Save button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (checkBoxRecordAddDate.IsChecked == true)
        {
            labelFilename.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (checkBoxRecordAddTime.IsChecked == true)
        {
            labelFilename.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        filename = labelFilename.Text ?? string.Empty;
        Close(true);
    }

    /// <summary>
    /// [XPLAT] Reproduces <c>buttonRecordCancel_Click</c>: dismiss the dialog without recording a name
    /// (WinForms <c>DialogResult.Cancel</c> -> <c>Close(false)</c>).
    /// </summary>
    /// <param name="sender">The Cancel button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
