// [XPLAT] migrated from net48/WinForms FormConvertProfiles — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using AgOpenGPS.Views;

namespace AgOpenGPS.Views.Profiles;

/// <summary>
/// [XPLAT] Code-behind for <c>FormConvertProfilesView.axaml</c> — a 1:1 behavioural-parity
/// reimplementation of the WinForms <c>Forms/Profiles/FormConvertProfiles</c>
/// (FormConvertProfiles.cs + FormConvertProfiles.Designer.cs). The "Convert Old Profiles" dialog
/// lists every legacy single-file profile, lets the operator extract its Vehicle and/or Tool half
/// (with editable target names), and runs the behaviour-frozen <see cref="CSettingsMigration"/>
/// round-trip (AAP §0.2.2). Only the framework underneath changes — the behaviour is unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The dialog is intentionally code-behind driven (the paired XAML declares <c>x:CompileBindings="False"</c>
/// and has no <c>x:DataType</c> / no view-model): every control is addressed by its <c>x:Name</c> and the
/// handlers wired in XAML reproduce the WinForms Designer's <c>this.ctl.Event += ...</c> wiring. Migration
/// (behaviour-frozen) logic lives entirely in <see cref="CSettingsMigration"/>; this view is UI only and
/// must not let that logic drift.
/// </para>
/// <para>
/// Cross-platform conversions (each tagged <c>// [XPLAT]</c> below): the WinForms <c>Load</c> event becomes
/// the Avalonia <see cref="Window.OnOpened(EventArgs)"/> override; the on-screen keyboard summoned from a
/// name box via <c>TextBox.ShowKeyboard</c> becomes a modal <see cref="FormKeyboard"/>
/// (<c>await ShowDialog&lt;string?&gt;</c>), gated on <c>Settings.Default.setDisplay_isKeyboardOn</c> in place
/// of the removed <c>FormGPS.isKeyboardOn</c>; the WinForms message boxes become
/// <see cref="FormDialogView"/> (<c>ShowAsync</c>/<c>ShowQuestionAsync</c>); the list rows are reflection-bound
/// <see cref="FileItem"/> records (LightGreen when already converted); and the only injected collaborator is
/// <c>reloadSettings</c> (replacing the removed <c>FormGPS.LoadSettings()</c> back-reference — there is no
/// <c>FormGPS</c>/<c>mf</c>). <c>buttonConvert</c> never closes the window — it stays open for the next
/// conversion, exactly as the WinForms original did; only <c>buttonClose</c> closes it.
/// </para>
/// </remarks>
public partial class FormConvertProfilesView : Window
{
    /// <summary>
    /// [XPLAT] Reloads the running application's settings after a one-time Environment migration. Replaces
    /// the WinForms <c>_formGPS.LoadSettings()</c> call — the dialog no longer holds a <c>FormGPS</c>
    /// back-reference; the host (composition root) wires this to <c>FormGPS.LoadSettings</c>.
    /// </summary>
    private readonly Action reloadSettings;

    /// <summary>Vehicle-import toggle (mirrors the WinForms <c>vehicleEnabled</c> field; default on).</summary>
    private bool vehicleEnabled = true;

    /// <summary>Tool-import toggle (mirrors the WinForms <c>toolEnabled</c> field; default on).</summary>
    private bool toolEnabled = true;

    /// <summary>
    /// [XPLAT] One <see cref="ListBox"/> row. The XAML <c>ItemTemplate</c> reflection-binds
    /// <c>{Binding Name}</c> for the row text and <c>Classes.converted="{Binding IsConverted}"</c> to paint
    /// already-converted files LightGreen (WinForms <c>ListViewItem.BackColor = Color.LightGreen</c>), so
    /// these public property names are part of the view contract. Replaces the WinForms <c>ListViewItem</c>
    /// (whose <c>Text</c>/<c>Name</c> carried the file stem and whose <c>BackColor</c> carried the flag).
    /// </summary>
    private sealed class FileItem
    {
        /// <summary>File stem (without extension) — the row's display text and identity key.</summary>
        public string Name { get; init; }

        /// <summary>True if this old file has already been converted (paints the row LightGreen).</summary>
        public bool IsConverted { get; init; }
    }

    /// <summary>
    /// [XPLAT] Primary constructor. Parity with the WinForms <c>FormConvertProfiles(FormGPS formGPS)</c>
    /// constructor, except the heavy <c>FormGPS</c> back-reference is replaced by a single
    /// <paramref name="reloadSettings"/> callback. The WinForms <c>Load</c>-handler work runs from
    /// <see cref="OnOpened(EventArgs)"/>.
    /// </summary>
    /// <param name="reloadSettings">
    /// Callback that reloads the running application's settings (replaces <c>FormGPS.LoadSettings()</c>),
    /// invoked once after a successful one-time Environment migration.
    /// </param>
    public FormConvertProfilesView(Action reloadSettings)
    {
        this.reloadSettings = reloadSettings;
        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia XAML previewer/designer only. It chains to the
    /// primary constructor with a no-op callback so design-time instantiation never touches host state. It
    /// is never used at runtime — the application always constructs the dialog with a real callback.
    /// </summary>
    public FormConvertProfilesView()
        : this(() => { })
    {
    }

    /// <summary>
    /// [XPLAT] WinForms <c>FormConvertProfiles_Load</c> → Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
    /// applies the translated title/labels, builds the convertible-file list, clears the details panel, and
    /// paints the toggle buttons.
    /// </summary>
    /// <param name="e">The event data passed to the base implementation.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] WinForms `this.Text` → Avalonia Window.Title; the labels/button caption are assigned from
        // the translation table exactly as in FormConvertProfiles_Load. The hard-coded English suffixes
        // appended to gsConvertStep1/gsConvertStep2 are preserved verbatim (parity with the WinForms source).
        this.Title = gStr.gsConvertOldProfiles;
        labelTitle.Text = gStr.gsConvertOldProfilesTitle;
        labelExplanation.Text = gStr.gsConvertProfilesExplanation;
        labelStep1.Text = gStr.gsConvertStep1 + ": " + "Select an old profile file";
        labelStep2.Text = gStr.gsConvertStep2 + ": " + "Select to convert (Vehicle and/or Tool)";
        labelVehicleName.Text = gStr.gsVehicleProfileName + ":";
        labelToolName.Text = gStr.gsToolProfileName + ":";
        // buttonClose caption (the embedded close glyph lives in the XAML; only the caption is localized).
        textClose.Text = gStr.gsClose;

        RefreshFileList();
        ClearDetails();
        UpdateToggleButtons();
    }

    /// <summary>
    /// [XPLAT] Shared <c>Tapped</c> handler for both name boxes (WinForms <c>TextBox_Click</c>). When the
    /// on-screen keyboard is enabled, summons the modal <see cref="FormKeyboard"/> seeded with the box's
    /// current text and writes the accepted result back, then returns focus to Convert. Replaces the WinForms
    /// <c>((TextBox)sender).ShowKeyboard(this)</c> + <c>FormGPS.isKeyboardOn</c> gate with the cross-platform
    /// modal dialog gated on <c>Settings.Default.setDisplay_isKeyboardOn</c>.
    /// </summary>
    /// <param name="sender">The name box that was tapped.</param>
    /// <param name="e">The tap event data (unused).</param>
    private async void TextBox_Tapped(object sender, TappedEventArgs e)
    {
        // [XPLAT] Qualify as Properties.Settings.Default — a bare `Settings` here would bind to the sibling
        // namespace AgOpenGPS.Views.Settings (this file lives under AgOpenGPS.Views.Profiles), not the
        // AgOpenGPS.Properties.Settings type. ShowDialog<string> (not string?) matches the project's
        // nullable-disabled convention used by every other migrated dialog; Cancel returns null, so the
        // `result != null` check is the cross-platform equivalent of the WinForms OK gate.
        if (Properties.Settings.Default.setDisplay_isKeyboardOn)
        {
            var tb = (TextBox)sender;
            var kb = new FormKeyboard(tb.Text ?? "");
            string result = await kb.ShowDialog<string>(this);
            if (result != null) tb.Text = result;
            buttonConvert.Focus();
        }
    }

    /// <summary>
    /// [XPLAT] Parameterless overload of <see cref="RefreshFileList(bool)"/> that clears the details panel,
    /// matching the WinForms <c>RefreshFileList()</c>.
    /// </summary>
    private void RefreshFileList()
    {
        RefreshFileList(clearDetails: true);
    }

    /// <summary>
    /// [XPLAT] WinForms <c>RefreshFileList(bool clearDetails)</c>: enumerates the convertible (old-format)
    /// files via <see cref="CSettingsMigration.GetConvertibleFiles"/>, builds a <see cref="FileItem"/> per
    /// file (flagging the already-converted ones so the template paints them LightGreen), updates the status
    /// line, and either clears the details panel or restores the previous selection by name.
    /// </summary>
    /// <param name="clearDetails">
    /// When <see langword="true"/> the details panel is reset; when <see langword="false"/> the previously
    /// selected file (if it still exists) is re-selected — used after a conversion so the dialog stays put.
    /// </param>
    private void RefreshFileList(bool clearDetails)
    {
        // [XPLAT] WinForms remembered the selection via listViewFiles.SelectedItems[0].Text; here the
        // selected FileItem's Name is the identity key.
        string selectedName = (listViewFiles.SelectedItem as FileItem)?.Name;

        string[] files = CSettingsMigration.GetConvertibleFiles();
        int convertedCount = 0;

        // [XPLAT] WinForms cleared listViewFiles.Items then added a ListViewItem per file; here the rows are
        // FileItem records assigned through ItemsSource (the established Avalonia list pattern).
        List<FileItem> items = new List<FileItem>();
        foreach (string name in files.OrderBy(n => n))
        {
            bool isConverted = CSettingsMigration.IsConverted(name);
            if (isConverted)
            {
                convertedCount++;
            }
            items.Add(new FileItem { Name = name, IsConverted = isConverted });
        }

        listViewFiles.ItemsSource = items;

        int count = items.Count;
        if (count == 0)
        {
            labelStatus.Text = gStr.gsNoOldFormatFiles;
        }
        else
        {
            int unconverted = count - convertedCount;
            labelStatus.Text = count + " " + gStr.gsOldFiles + ": " +
                convertedCount + " " + gStr.gsConverted + ", " + unconverted + " " + gStr.gsToConvert;
        }

        if (clearDetails)
        {
            ClearDetails();
        }
        else if (selectedName != null)
        {
            // [XPLAT] WinForms re-selected the matching ListViewItem by Name; here we set SelectedItem to the
            // newly-built FileItem with the remembered name (which raises SelectionChanged → repopulates the
            // details panel), exactly mirroring the WinForms selection-restored side effect.
            FileItem restore = items.FirstOrDefault(item => item.Name == selectedName);
            if (restore != null)
            {
                listViewFiles.SelectedItem = restore;
            }
        }
    }

    /// <summary>
    /// [XPLAT] WinForms <c>ClearDetails</c>: blanks both name boxes, resets the toggles to on, and disables
    /// the details panel and the Convert button.
    /// </summary>
    private void ClearDetails()
    {
        textBoxVehicleName.Text = "";
        textBoxToolName.Text = "";
        vehicleEnabled = true;
        toolEnabled = true;
        panelDetails.IsEnabled = false;
        buttonConvert.IsEnabled = false;
        UpdateToggleButtons();
    }

    /// <summary>
    /// [XPLAT] WinForms <c>listViewFiles_SelectedIndexChanged</c>: when a file is selected, default both name
    /// boxes to its name (which raises <see cref="TextBoxName_TextChanged"/> → re-evaluates Convert, exactly
    /// like the WinForms <c>TextChanged</c> side effect), reset both toggles to on, and enable the details
    /// panel; otherwise clear the details.
    /// </summary>
    /// <param name="sender">The file list (unused).</param>
    /// <param name="e">The selection-changed event data (unused).</param>
    private void ListViewFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (listViewFiles.SelectedItem is FileItem sel)
        {
            textBoxVehicleName.Text = sel.Name;
            textBoxToolName.Text = sel.Name;
            vehicleEnabled = true;
            toolEnabled = true;
            panelDetails.IsEnabled = true;
            UpdateToggleButtons();
        }
        else
        {
            ClearDetails();
        }
    }

    /// <summary>
    /// [XPLAT] WinForms <c>btnToggleVehicle_Click</c>: flip the Vehicle-import toggle, then repaint the toggle
    /// buttons and re-evaluate the Convert button.
    /// </summary>
    /// <param name="sender">The Vehicle toggle button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void BtnToggleVehicle_Click(object sender, RoutedEventArgs e)
    {
        vehicleEnabled = !vehicleEnabled;
        UpdateToggleButtons();
        UpdateConvertButton();
    }

    /// <summary>
    /// [XPLAT] WinForms <c>btnToggleTool_Click</c>: flip the Tool-import toggle, then repaint the toggle
    /// buttons and re-evaluate the Convert button.
    /// </summary>
    /// <param name="sender">The Tool toggle button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void BtnToggleTool_Click(object sender, RoutedEventArgs e)
    {
        toolEnabled = !toolEnabled;
        UpdateToggleButtons();
        UpdateConvertButton();
    }

    /// <summary>
    /// [XPLAT] WinForms <c>UpdateToggleButtons</c>: each toggle shows its "Import …" caption with a LightGreen
    /// face and enables its name box when on, or a "No Import" caption with a LightGray face and a disabled
    /// name box when off. The captions come from <c>gStr</c> (<c>gsImportVehicle</c>/<c>gsImportTool</c>/
    /// <c>gsNoImport</c>); the colours are the fixed WinForms <c>Color.LightGreen</c>/<c>Color.LightGray</c>
    /// swatches (theme-independent, for parity).
    /// </summary>
    private void UpdateToggleButtons()
    {
        if (vehicleEnabled)
        {
            btnToggleVehicle.Content = gStr.gsImportVehicle;
            btnToggleVehicle.Background = Brushes.LightGreen;
            textBoxVehicleName.IsEnabled = true;
        }
        else
        {
            btnToggleVehicle.Content = gStr.gsNoImport;
            btnToggleVehicle.Background = Brushes.LightGray;
            textBoxVehicleName.IsEnabled = false;
        }

        if (toolEnabled)
        {
            btnToggleTool.Content = gStr.gsImportTool;
            btnToggleTool.Background = Brushes.LightGreen;
            textBoxToolName.IsEnabled = true;
        }
        else
        {
            btnToggleTool.Content = gStr.gsNoImport;
            btnToggleTool.Background = Brushes.LightGray;
            textBoxToolName.IsEnabled = false;
        }
    }

    /// <summary>
    /// [XPLAT] WinForms <c>UpdateConvertButton</c>: Convert is enabled only when a file is selected, every
    /// <em>enabled</em> half has a non-blank name, and at least one half is enabled.
    /// </summary>
    private void UpdateConvertButton()
    {
        bool hasSelection = listViewFiles.SelectedItem != null;
        bool vehicleValid = !vehicleEnabled || !string.IsNullOrWhiteSpace(textBoxVehicleName.Text);
        bool toolValid = !toolEnabled || !string.IsNullOrWhiteSpace(textBoxToolName.Text);
        bool atLeastOne = vehicleEnabled || toolEnabled;

        buttonConvert.IsEnabled = hasSelection && vehicleValid && toolValid && atLeastOne;
    }

    /// <summary>
    /// [XPLAT] Shared <c>TextChanged</c> handler for both name boxes (WinForms <c>textBoxName_TextChanged</c>):
    /// strips invalid filename characters via <c>glm.fileRegex</c> while preserving the caret, then
    /// re-evaluates the Convert button. Setting <c>Text</c> to an already-clean value raises no further
    /// change notification, so the rewrite cannot recurse.
    /// </summary>
    /// <param name="sender">The name box whose text changed.</param>
    /// <param name="e">The text-changed event data (unused).</param>
    private void TextBoxName_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = (TextBox)sender;
        int pos = tb.CaretIndex;
        tb.Text = Regex.Replace(tb.Text ?? "", glm.fileRegex, "");
        tb.CaretIndex = pos;

        UpdateConvertButton();
    }

    /// <summary>
    /// [XPLAT] WinForms <c>buttonConvert_Click</c>: validates the target names, blocks duplicate output files,
    /// confirms, then runs the behaviour-frozen <see cref="CSettingsMigration"/> round-trip for the enabled
    /// halves plus a one-time Environment migration, marks the source converted on success, optionally reloads
    /// settings, reports the outcome, and refreshes the list. Every early <c>return</c> simply exits the
    /// handler — the dialog is NEVER closed here (it stays open for the next conversion); only
    /// <see cref="ButtonClose_Click"/> closes it.
    /// </summary>
    /// <param name="sender">The Convert button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private async void ButtonConvert_Click(object sender, RoutedEventArgs e)
    {
        if (listViewFiles.SelectedItem is not FileItem selItem) return;

        string sourceFile = selItem.Name;
        bool exportVehicle = vehicleEnabled;
        string vehicleName = exportVehicle ? textBoxVehicleName.Text.Trim() : null;
        bool exportTool = toolEnabled;
        string toolName = exportTool ? textBoxToolName.Text.Trim() : null;

        // Validate non-empty names for the enabled halves (WinForms parity early-return guards).
        if (exportVehicle && string.IsNullOrEmpty(vehicleName)) return;
        if (exportTool && string.IsNullOrEmpty(toolName)) return;

        // Block duplicate output names — refuse to overwrite an existing Vehicle/Tool profile.
        if (exportVehicle)
        {
            string vehiclePath = Path.Combine(RegistrySettings.vehiclesDirectory, vehicleName + ".xml");
            if (File.Exists(vehiclePath))
            {
                await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsVehicleAlreadyExists + " '" + vehicleName + "'", DialogSeverity.Error, owner: this);
                return;
            }
        }
        if (exportTool)
        {
            string toolPath = Path.Combine(RegistrySettings.toolsDirectory, toolName + ".xml");
            if (File.Exists(toolPath))
            {
                await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsToolAlreadyExists + " '" + toolName + "'", DialogSeverity.Error, owner: this);
                return;
            }
        }

        string confirmMsg = gStr.gsConvertFrom + " '" + sourceFile + "':\n\n";
        if (exportVehicle) confirmMsg += "  " + gStr.gsConvertVehicleLine + ": " + vehicleName + "\n";
        if (exportTool) confirmMsg += "  " + gStr.gsConvertToolLine + ": " + toolName + "\n";

        bool confirm = await FormDialogView.ShowQuestionAsync(gStr.gsConvertConfirm, confirmMsg, owner: this);
        if (!confirm) return;

        var errors = new List<string>();

        // Convert Vehicle (optional) — behaviour-frozen CSettingsMigration round-trip.
        if (exportVehicle)
        {
            var vSettings = new VehicleSettings();
            var vResult = CSettingsMigration.MigrateVehicle(sourceFile, vehicleName, vSettings);
            if (vResult != LoadResult.Ok) { errors.Add($"Vehicle: {vResult}"); Log.ErrorWriter(sourceFile, "Vehicle", vResult.ToString()); }
        }

        // Convert Tool (optional).
        if (exportTool)
        {
            var tSettings = new ToolSettings();
            var tResult = CSettingsMigration.MigrateTool(sourceFile, toolName, tSettings);
            if (tResult != LoadResult.Ok) { errors.Add($"Tool: {tResult}"); Log.ErrorWriter(sourceFile, "Tool", tResult.ToString()); }
        }

        // One-time environment migration (only when environment.xml does not yet exist).
        bool migratedEnvironment = false;
        string envPath = Path.Combine(RegistrySettings.environmentDirectory, "environment.xml");
        if (!File.Exists(envPath))
        {
            var eResult = CSettingsMigration.MigrateEnvironment(sourceFile, "environment");
            if (eResult == LoadResult.Ok) { migratedEnvironment = true; Log.EventWriter($"Environment migrated from '{sourceFile}'"); }
            else { errors.Add($"Environment: {eResult}"); Log.ErrorWriter(sourceFile, "Environment", eResult.ToString()); }
        }

        if (errors.Count == 0)
        {
            // Mark the old file as converted (paints it green on the next refresh).
            CSettingsMigration.MarkAsConverted(sourceFile);

            string logMsg = $"Converted '{sourceFile}' -> " +
                (exportVehicle ? $"Vehicle:'{vehicleName}', " : "") +
                (exportTool ? $"Tool:'{toolName}'" : "");
            if (migratedEnvironment) logMsg += ", Environment: 'environment.xml' (one-time)";
            Log.EventWriter(logMsg);

            // If Environment was migrated, load it immediately and notify the host to reload settings.
            if (migratedEnvironment)
            {
                // [XPLAT] Properties.Settings.Default — qualified to avoid binding to the sibling
                // AgOpenGPS.Views.Settings namespace (see TextBox_Tapped note).
                var loadResult = Properties.Settings.Default.Load();
                if (loadResult == LoadResult.Ok) { reloadSettings(); Log.EventWriter("Environment settings loaded after migration"); }
            }

            string successMsg = "'" + sourceFile + "' " + gStr.gsConvertedSuccess;
            if (migratedEnvironment) successMsg += "\n\n" + gStr.gsEnvironmentMigratedLoaded;

            await FormDialogView.ShowAsync(gStr.gsConversionComplete, successMsg, DialogSeverity.Info, owner: this);

            // Refresh to show the converted file in green; keep the dialog open for the next conversion.
            RefreshFileList(clearDetails: false);
        }
        else
        {
            Log.EventWriter($"Errors converting '{sourceFile}': {string.Join(", ", errors)}");
            await FormDialogView.ShowAsync(gStr.gsConversionErrors,
                gStr.gsConversionErrorMsg + ": " + Environment.NewLine + string.Join("\n", errors), DialogSeverity.Warning, owner: this);
        }
    }

    /// <summary>
    /// [XPLAT] WinForms <c>buttonClose</c> (<c>DialogResult.Cancel</c>): closes the dialog with a
    /// <see langword="false"/> result. This is the only path that closes the window.
    /// </summary>
    /// <param name="sender">The Close button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void ButtonClose_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
