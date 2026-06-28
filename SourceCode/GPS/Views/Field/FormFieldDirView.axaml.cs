// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Create New Field (by name)" dialog — a 1:1 behavioural-parity
    /// reimplementation of the WinForms <c>FormFieldDir</c> (Forms/Field/FormFieldDir.cs +
    /// FormFieldDir.Designer.cs). The operator types a new field name (sanitised live against
    /// <c>glm.fileRegex</c>), optionally appends the current date and/or time, then saves — which
    /// creates the field directory under <c>RegistrySettings.fieldsDirectory</c> and writes its initial
    /// files. Per AAP §0.3.3 this is a faithful parity port of the current Windows Forms behaviour,
    /// never a redesign.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative dialog (NO <c>DataContext</c> / <c>x:DataType</c> / MVVM bindings); controls are
    /// addressed by their <c>x:Name</c>. The four Button <c>Click</c> handlers
    /// (<see cref="OnAddDateClick"/> / <see cref="OnAddTimeClick"/> / <see cref="OnCancelClick"/> /
    /// <see cref="OnSaveClick"/>) are declared in the paired <c>FormFieldDirView.axaml</c>; the
    /// <c>tboxFieldName</c> events (<see cref="OnFieldNameTextChanged"/> and the keyboard tap
    /// <see cref="OnFieldNameTapped"/>) declare nothing in XAML and are wired here in the constructor —
    /// exactly mirroring the WinForms Designer wiring.
    /// </para>
    /// <para>
    /// Dependency-Inversion (AAP §0.3.2 / §0.3.4): the WinForms form held a <c>FormGPS mf</c>
    /// god-object back-reference and, on save, called <c>mf.JobNew()</c>,
    /// <c>mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false)</c>, the seven
    /// <c>mf.FileCreate*</c>/<c>mf.FileSaveFlags</c> writers, and read/wrote
    /// <c>mf.currentFieldDirectory</c> / <c>mf.isKeyboardOn</c>. Holding a <c>FormGPS</c> reference is
    /// forbidden post-migration, so those operations are injected as plain delegates through the full
    /// constructor. The dialog still OWNS the parts that are framework-local — name validation, the
    /// <see cref="DirectoryInfo"/> path build, the directory-exists check, <c>Directory.Create</c>, the
    /// error dialogs and the dialog result — while the injected delegates perform the domain work the
    /// FormGPS shell used to. This matches the established injection style of the sibling
    /// <c>FormPanView</c> / <c>FormSaveOrNotView</c> dialogs.
    /// </para>
    /// <para>
    /// Framework conversions (each tagged <c>// [XPLAT]</c> below): the WinForms
    /// <c>FormDialog.Show(...)</c> message box becomes <c>await FormDialogView.ShowAsync(..., this)</c>
    /// (which makes <see cref="OnSaveClick"/> <see langword="async"/>); the deleted
    /// <c>TextBox.ShowKeyboard</c> Controls extension becomes the migrated
    /// <see cref="FormKeyboard"/> modal; WinForms <c>TextBox.SelectionStart</c> becomes Avalonia
    /// <see cref="TextBox.CaretIndex"/>; the <c>FormFieldDir_Load</c> <c>ScreenHelper.IsOnScreen</c>
    /// reposition is dropped in favour of <c>WindowStartupLocation="CenterOwner"</c> (set in the
    /// <c>.axaml</c>); the WinForms <c>DialogResult.OK</c> / <c>Close()</c> becomes
    /// <c>Close(true)</c> surfaced to the caller's <c>await dlg.ShowDialog&lt;bool&gt;(owner)</c>
    /// (<see langword="true"/> == a field was created). All date/time formatting uses
    /// <see cref="CultureInfo.InvariantCulture"/> so the produced names are byte-identical across OS
    /// locales (AAP §0.6.5).
    /// </para>
    /// </remarks>
    public partial class FormFieldDirView : Window
    {
        // [XPLAT] Re-entrancy guard: assigning TextBox.Text inside its TextChanged handler re-raises the
        // event. The WinForms original tolerated the recursion; here a guard makes the single rewrite
        // explicit and avoids redundant work (same pattern as the sibling FormRecordNameView).
        private bool _suppressTextChanged;

        // === Injected collaborators (replace the WinForms `mf` FormGPS god-object) ===
        // Null only on the parameterless (Avalonia XAML loader / previewer) construction path; every
        // invocation below is therefore null-guarded so the loader-constructed dialog never throws.

        // [XPLAT] mf.isKeyboardOn — when true, tapping the name field opens the on-screen keyboard.
        private readonly bool _isKeyboardOn;

        // [XPLAT] set of mf.currentFieldDirectory (the active field directory). The WinForms code wrote
        // it to the trimmed name before creating the field and cleared it to "" if creation threw.
        private readonly Action<string> _setCurrentFieldDirectory;

        // [XPLAT] mf.JobNew() — starts a new (empty) job. Called before the directory-exists check,
        // exactly as in the original.
        private readonly Action _createNewJob;

        // [XPLAT] mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false) — anchors the field's local
        // plane at the current GPS position. Injected as a single delegate so this view needs no
        // reference to the CNMEA / AppModel domain types.
        private readonly Action _defineLocalPlaneAtCurrentPosition;

        // [XPLAT] The seven sequential FormGPS field-file writers grouped into one delegate, because the
        // original called them as an uninterrupted block immediately after Directory.Create() with no
        // intervening view logic:
        //   mf.FileCreateField(); mf.FileCreateSections(); mf.FileCreateRecPath(); mf.FileCreateContour();
        //   mf.FileCreateElevation(); mf.FileSaveFlags(); mf.FileCreateBoundary();
        // The host's delegate must run them in that exact order to preserve the on-disk field layout.
        private readonly Action _writeNewFieldFiles;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, which the Release <c>TreatWarningsAsErrors</c> build treats as an error). It
        /// is also the single construction chokepoint: the full constructor chains to it with
        /// <c>: this()</c>. It loads the XAML, applies the localised title / caption (parity with the
        /// WinForms constructor, which set <c>labelEnterFieldName.Text = gStr.gsEnterFieldName</c> and
        /// <c>this.Text = gStr.gsCreateNewField</c>) and wires the <c>tboxFieldName</c> events the
        /// WinForms Designer attached (<c>TextChanged</c> + the keyboard tap). When constructed this way
        /// the injected collaborators are <see langword="null"/>, so the field-creation path is inert —
        /// the dialog is intended to be created with the full constructor by the application host.
        /// </summary>
        public FormFieldDirView()
        {
            InitializeComponent();

            // [XPLAT] WinForms ctor: labelEnterFieldName.Text = gStr.gsEnterFieldName; this.Text = gStr.gsCreateNewField.
            labelEnterFieldName.Text = gStr.gsEnterFieldName;
            Title = gStr.gsCreateNewField;

            // [XPLAT] Designer-wired tboxFieldName events reproduced programmatically (the .axaml
            // intentionally declares none for the textbox).
            tboxFieldName.TextChanged += OnFieldNameTextChanged;
            tboxFieldName.Tapped += OnFieldNameTapped;
        }

        /// <summary>
        /// [XPLAT] Full constructor used by the application host. Supplies the collaborators that the
        /// WinForms form reached through its <c>FormGPS mf</c> back-reference, so this dialog can create
        /// the field without depending on the FormGPS god-object.
        /// </summary>
        /// <param name="isKeyboardOn">
        /// Parity with <c>mf.isKeyboardOn</c>: when <see langword="true"/>, tapping the name field opens
        /// the on-screen <see cref="FormKeyboard"/>.
        /// </param>
        /// <param name="setCurrentFieldDirectory">
        /// Parity with assigning <c>mf.currentFieldDirectory</c>: invoked with the trimmed field name
        /// before creation and with <see cref="string.Empty"/> if creation throws.
        /// </param>
        /// <param name="createNewJob">Parity with <c>mf.JobNew()</c>: starts a new empty job.</param>
        /// <param name="defineLocalPlaneAtCurrentPosition">
        /// Parity with <c>mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false)</c>: anchors the new
        /// field's local plane at the current position.
        /// </param>
        /// <param name="writeNewFieldFiles">
        /// Parity with the sequential <c>mf.FileCreateField()</c> … <c>mf.FileCreateBoundary()</c> block:
        /// writes the new field's initial files. Must run those writers in their original order.
        /// </param>
        public FormFieldDirView(
            bool isKeyboardOn,
            Action<string> setCurrentFieldDirectory,
            Action createNewJob,
            Action defineLocalPlaneAtCurrentPosition,
            Action writeNewFieldFiles)
            : this()
        {
            _isKeyboardOn = isKeyboardOn;
            _setCurrentFieldDirectory = setCurrentFieldDirectory;
            _createNewJob = createNewJob;
            _defineLocalPlaneAtCurrentPosition = defineLocalPlaneAtCurrentPosition;
            _writeNewFieldFiles = writeNewFieldFiles;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormFieldDir_Load</c> → Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
        /// the Save button starts disabled and is enabled by <see cref="OnFieldNameTextChanged"/> once a
        /// non-empty trimmed name is present. The original's <c>ScreenHelper.IsOnScreen</c> reposition is
        /// intentionally NOT ported — <c>WindowStartupLocation="CenterOwner"</c> in the <c>.axaml</c>
        /// replaces it.
        /// </summary>
        /// <param name="e">The event data forwarded to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            btnSave.IsEnabled = false;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>tboxFieldName_TextChanged</c>: strip filesystem-illegal characters as the
        /// operator types using <c>glm.fileRegex</c>, preserve the caret (WinForms <c>SelectionStart</c>
        /// → Avalonia <see cref="TextBox.CaretIndex"/>), then enable <c>btnSave</c> only when the trimmed
        /// name is non-empty.
        /// </summary>
        /// <param name="sender">The <see cref="TextBox"/> raising the event.</param>
        /// <param name="e">The text-changed event data (unused).</param>
        private void OnFieldNameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged)
            {
                return;
            }

            var textBox = (TextBox)sender;
            int caret = textBox.CaretIndex;
            string current = textBox.Text ?? string.Empty;
            string sanitized = Regex.Replace(current, AgOpenGPS.glm.fileRegex, "");

            // Only rewrite (and only re-raise) when sanitisation actually changed the text.
            if (!string.Equals(sanitized, current, StringComparison.Ordinal))
            {
                _suppressTextChanged = true;
                textBox.Text = sanitized;
                textBox.CaretIndex = Math.Min(caret, sanitized.Length);
                _suppressTextChanged = false;
            }

            btnSave.IsEnabled = !string.IsNullOrEmpty((textBox.Text ?? string.Empty).Trim());
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnAddDate_Click</c>: append " yyyy-MM-dd" using
        /// <see cref="CultureInfo.InvariantCulture"/> (cross-platform stable).
        /// </summary>
        /// <param name="sender">The add-date button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnAddDateClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnAddTime_Click</c>: append " HH-mm" using
        /// <see cref="CultureInfo.InvariantCulture"/>.
        /// </summary>
        /// <param name="sender">The add-time button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnAddTimeClick(object sender, RoutedEventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnSerialCancel_Click</c>: close the dialog without creating a field. The
        /// <see langword="false"/> result is surfaced to the caller's
        /// <c>await dlg.ShowDialog&lt;bool&gt;(owner)</c>.
        /// </summary>
        /// <param name="sender">The cancel button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnSave_Click</c>, ported in full. Validates the trimmed name, records it
        /// as the current field directory, builds the target directory under
        /// <c>RegistrySettings.fieldsDirectory</c>, then — inside a try/catch identical to the original —
        /// starts a new job, and either reports a name clash (leaving the dialog open) or anchors the
        /// local plane, creates the directory and writes the initial field files. On any exception the
        /// error is logged and shown and the current field directory is cleared. The dialog then closes
        /// with a positive result, exactly mirroring the WinForms <c>DialogResult.OK; Close();</c> that
        /// ran after the try/catch (the name-clash branch returns early and keeps the dialog open).
        /// </summary>
        /// <remarks>
        /// <c>async void</c> is the correct shape for an Avalonia <c>Click</c> handler that awaits a
        /// modal dialog (AAP guidance). The WinForms <c>mf.menustripLanguage.Enabled = false</c> line is
        /// dropped — there is no menu strip in the Avalonia shell.
        /// </remarks>
        /// <param name="sender">The save button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // [XPLAT] WinForms: if the name is empty, do nothing (the dialog stays open).
            string fieldName = (tboxFieldName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            // [XPLAT] mf.currentFieldDirectory = tboxFieldName.Text.Trim();
            _setCurrentFieldDirectory?.Invoke(fieldName);

            // [XPLAT] Path.Combine is cross-platform safe and is what the original already used.
            var dirNewField = new DirectoryInfo(Path.Combine(RegistrySettings.fieldsDirectory, fieldName));

            try
            {
                // [XPLAT] mf.JobNew();
                _createNewJob?.Invoke();

                if (dirNewField.Exists)
                {
                    // [XPLAT] FormDialog.Show(gsDirectoryExists, gsChooseADifferentName, Error)
                    //         → await FormDialogView.ShowAsync(..., this). The early return keeps the
                    //         dialog open so the operator can choose a different name (WinForms parity).
                    await FormDialogView.ShowAsync(
                        gStr.gsDirectoryExists,
                        gStr.gsChooseADifferentName,
                        DialogSeverity.Error,
                        this);
                    return;
                }
                else
                {
                    // [XPLAT] mf.pn.DefineLocalPlane(mf.AppModel.CurrentLatLon, false);
                    _defineLocalPlaneAtCurrentPosition?.Invoke();

                    dirNewField.Create();

                    // [XPLAT] mf.FileCreateField(); … mf.FileCreateBoundary(); (run in original order).
                    _writeNewFieldFiles?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // [XPLAT] Log.EventWriter("Creating new field " + ex); then the error dialog; then clear
                // the current field directory — verbatim from the WinForms catch block.
                Log.EventWriter("Creating new field " + ex);

                await FormDialogView.ShowAsync(
                    gStr.gsError,
                    ex.ToString(),
                    DialogSeverity.Error,
                    this);

                _setCurrentFieldDirectory?.Invoke("");
            }

            // [XPLAT] WinForms DialogResult.OK; Close(); — runs after the try/catch in both the success
            // and exception cases (only the name-clash branch returned early above).
            Close(true);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>tboxFieldName_Click</c>: when the on-screen keyboard is enabled
        /// (<c>mf.isKeyboardOn</c>), tapping the name field opens the migrated <see cref="FormKeyboard"/>
        /// pre-filled with the current text; an accepted value is written back. Replaces the deleted
        /// <c>TextBox.ShowKeyboard(this)</c> Controls extension. Focus is then moved to the cancel button
        /// (WinForms parity), which also prevents the tap from immediately reopening the keyboard.
        /// </summary>
        /// <param name="sender">The name <see cref="TextBox"/> (unused).</param>
        /// <param name="e">The tap event data (unused).</param>
        private async void OnFieldNameTapped(object sender, TappedEventArgs e)
        {
            if (!_isKeyboardOn)
            {
                return;
            }

            var keyboard = new FormKeyboard(tboxFieldName.Text ?? string.Empty);
            string result = await keyboard.ShowDialog<string>(this);

            // [XPLAT] FormKeyboard returns null on cancel and the entered string on OK.
            if (result != null)
            {
                tboxFieldName.Text = result;
            }

            btnSerialCancel.Focus();
        }
    }
}
