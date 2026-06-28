// [XPLAT] migrated from net48/WinForms FormLoadVehicleTool — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using AgOpenGPS.Views;

namespace AgOpenGPS.Views.Profiles;

/// <summary>
/// [XPLAT] Avalonia code-behind for the "Vehicle / Tool" profile manager — a 1:1 behavioral parity
/// re-implementation of the WinForms <c>FormLoadVehicleTool</c>
/// (<c>SourceCode/GPS/Forms/Profiles/FormLoadVehicleTool.cs</c>).
///
/// <para>
/// <b>Decoupling.</b> The original form reached back into the <c>FormGPS</c> god-object (<c>mf</c>) to
/// re-create the vehicle/tool, reload settings, push textures, and send PGN settings. To run on the
/// cross-platform stack without dragging in <c>FormGPS</c> (which is deferred / source-gated during the
/// migration), every <c>FormGPS</c> call site is replaced by a constructor-injected plain delegate. The
/// composition root (the menu/config caller) supplies trivial one-line delegates wired to the real
/// services, so the exact call <i>order</i> of the original apply-sequence is preserved verbatim in this
/// view rather than scattered across the host.
/// </para>
///
/// <para>
/// <b>Behavior frozen.</b> All file I/O runs over the behavior-frozen settings XML schema
/// (<see cref="XmlSettingsHandler"/>, <see cref="VehicleSettings"/>/<see cref="ToolSettings"/>
/// <c>Default</c>, <see cref="CSettingsMigration.IsSettingsType"/>) exactly as the WinForms original did
/// (AAP §0.2.2). Numeric previews are formatted with <see cref="CultureInfo.InvariantCulture"/> and all
/// paths are built with <see cref="Path.Combine"/> so field/profile text never drifts on a comma-decimal
/// locale or a case-sensitive filesystem (AAP §0.6.5). This class contains no <c>System.Windows.Forms</c>,
/// <c>OpenTK</c>, <c>Microsoft.Win32</c>, or <c>System.Media</c> usage.
/// </para>
/// </summary>
public partial class FormLoadVehicleToolView : Window
{
    // ===== Selection state (parity with FormLoadVehicleTool private fields) ========================

    /// <summary>[XPLAT] Name of the vehicle profile currently highlighted in the list (null = none).</summary>
    private string _selectedVehicle;

    /// <summary>[XPLAT] Name of the tool profile currently highlighted in the list (null = none).</summary>
    private string _selectedTool;

    // WinForms used Color.Orange for the active/current profile and Color.LightGreen for a non-current
    // selection. Brushes.Orange / Brushes.LightGreen are the exact Avalonia equivalents.
    private static readonly IBrush ColorCurrent = Brushes.Orange;
    private static readonly IBrush ColorSelected = Brushes.LightGreen;

    // ===== Injected collaborators (replace every former _formGPS.* call site) ======================

    private readonly Func<bool> isJobStarted;
    private readonly IErrorPresenter errorPresenter;
    private readonly Action reloadSettings;        // _formGPS.LoadSettings()
    private readonly Action recreateVehicle;       // _formGPS.vehicle = new CVehicle(_formGPS)
    private readonly Action recreateTool;          // _formGPS.tool = new CTool(_formGPS)
    private readonly Action setVehicleTextures;    // _formGPS.SetVehicleTextures()
    private readonly Action sendSettings;          // _formGPS.SendSettings()
    private readonly Action sendRelaySettings;     // _formGPS.SendRelaySettingsToMachineModule()

    // ===== List backing collections ================================================================
    // The WinForms original called listView.Items.Clear()/.Add(); ObservableCollection.Clear()/Add()
    // mirrors that one-for-one and keeps the bound ListBox in sync.
    private readonly ObservableCollection<ProfileRow> _vehicleRows = new ObservableCollection<ProfileRow>();
    private readonly ObservableCollection<ProfileRow> _toolRows = new ObservableCollection<ProfileRow>();

    /// <summary>
    /// [XPLAT] Production constructor. The eight injected collaborators stand in for the former
    /// <c>FormGPS</c> back-references; see the class summary for the apply-sequence rationale.
    /// </summary>
    public FormLoadVehicleToolView(
        Func<bool> isJobStarted,
        IErrorPresenter errorPresenter,
        Action reloadSettings,
        Action recreateVehicle,
        Action recreateTool,
        Action setVehicleTextures,
        Action sendSettings,
        Action sendRelaySettings)
    {
        this.isJobStarted = isJobStarted;
        this.errorPresenter = errorPresenter;
        this.reloadSettings = reloadSettings;
        this.recreateVehicle = recreateVehicle;
        this.recreateTool = recreateTool;
        this.setVehicleTextures = setVehicleTextures;
        this.sendSettings = sendSettings;
        this.sendRelaySettings = sendRelaySettings;

        InitializeComponent();

        // Bind each ListBox to its backing collection once; refreshes mutate the collections in place.
        listViewVehicles.ItemsSource = _vehicleRows;
        listViewTools.ItemsSource = _toolRows;
    }

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia XAML previewer / designer only. It wires no-op
    /// collaborators so the view can be instantiated in the previewer without a live host. It is never
    /// used at runtime — the composition root always calls the eight-argument constructor.
    /// </summary>
    public FormLoadVehicleToolView()
        : this(() => false, null, () => { }, () => { }, () => { }, () => { }, () => { }, () => { })
    {
    }

    // ===== Lifecycle ===============================================================================

    /// <summary>
    /// [XPLAT] Reproduces the WinForms <c>_Load</c> handler: set captions, populate both lists,
    /// pre-select the currently active vehicle/tool, then refresh all header labels and the Load button.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Captions (WinForms set Text on the form/buttons in _Load).
        this.Title = gStr.gsVehicleTool;
        textLoad.Text = gStr.gsLoad;
        textCancel.Text = gStr.gsCancel;
        buttonConvertOld.Content = gStr.gsConvertOldProfiles;

        RefreshVehicleList();
        RefreshToolList();

        // Pre-select the currently active profiles (selecting a row fires SelectionChanged, which fills
        // the preview and enables Rename for that row).
        if (!string.IsNullOrEmpty(RegistrySettings.vehicleProfileName))
        {
            ProfileRow current = _vehicleRows.FirstOrDefault(r => r.Name == RegistrySettings.vehicleProfileName);
            if (current != null)
                listViewVehicles.SelectedItem = current;
        }

        if (!string.IsNullOrEmpty(RegistrySettings.toolProfileName))
        {
            ProfileRow current = _toolRows.FirstOrDefault(r => r.Name == RegistrySettings.toolProfileName);
            if (current != null)
                listViewTools.SelectedItem = current;
        }

        UpdateCurrentLabels();
        UpdateSelectedLabels();
        UpdateLoadButton();
    }

    // ===== Header / selection labels ===============================================================

    /// <summary>
    /// [XPLAT] "Current: &lt;name&gt;" headers, driven solely by <see cref="RegistrySettings"/>. The
    /// literal <c>" (load failed)"</c> suffix is preserved exactly from the WinForms original.
    /// </summary>
    private void UpdateCurrentLabels()
    {
        lblCurrentVehicle.Text = !string.IsNullOrEmpty(RegistrySettings.vehicleProfileName)
            ? gStr.gsCurrentVehicle + ": " + RegistrySettings.vehicleProfileName +
              (RegistrySettings.vehicleProfileLoadResult == LoadResult.Ok ? "" : " (load failed)")
            : gStr.gsCurrentVehicle + ": " + gStr.gsNone;

        lblCurrentTool.Text = !string.IsNullOrEmpty(RegistrySettings.toolProfileName)
            ? gStr.gsCurrentTool + ": " + RegistrySettings.toolProfileName +
              (RegistrySettings.toolProfileLoadResult == LoadResult.Ok ? "" : " (load failed)")
            : gStr.gsCurrentTool + ": " + gStr.gsNone;
    }

    /// <summary>[XPLAT] "Selected: &lt;name&gt;" sub-headers (or "Selected: -" when nothing is selected).</summary>
    private void UpdateSelectedLabels()
    {
        lblSelectedVehicle.Text = _selectedVehicle != null
            ? gStr.gsSelected + ": " + _selectedVehicle
            : gStr.gsSelected + ": -";

        lblSelectedTool.Text = _selectedTool != null
            ? gStr.gsSelected + ": " + _selectedTool
            : gStr.gsSelected + ": -";
    }

    // ===== List population =========================================================================

    /// <summary>
    /// [XPLAT] Re-enumerate the vehicle profiles directory and rebuild the list. The active profile is
    /// painted Orange + Bold; selection and preview are reset (matches WinForms <c>RefreshVehicleList</c>).
    /// </summary>
    private void RefreshVehicleList()
    {
        _vehicleRows.Clear();
        foreach (string name in GetFiles(RegistrySettings.vehiclesDirectory, "VehicleSettings"))
        {
            ProfileRow row = new ProfileRow { Name = name };
            if (name == RegistrySettings.vehicleProfileName)
            {
                row.Background = ColorCurrent;
                row.FontWeight = FontWeight.Bold;
            }
            _vehicleRows.Add(row);
        }

        _selectedVehicle = null;
        ClearVehiclePreview();
    }

    /// <summary>
    /// [XPLAT] Re-enumerate the tools directory and rebuild the list (matches WinForms
    /// <c>RefreshToolList</c>).
    /// </summary>
    private void RefreshToolList()
    {
        _toolRows.Clear();
        foreach (string name in GetFiles(RegistrySettings.toolsDirectory, "ToolSettings"))
        {
            ProfileRow row = new ProfileRow { Name = name };
            if (name == RegistrySettings.toolProfileName)
            {
                row.Background = ColorCurrent;
                row.FontWeight = FontWeight.Bold;
            }
            _toolRows.Add(row);
        }

        _selectedTool = null;
        ClearToolPreview();
    }

    /// <summary>
    /// [XPLAT] Enumerate <c>*.xml</c> files in <paramref name="directory"/> that are genuinely of the
    /// requested settings type (per <see cref="CSettingsMigration.IsSettingsType"/>), returning their
    /// base names sorted ascending. A missing directory yields an empty sequence (no throw).
    /// </summary>
    private IEnumerable<string> GetFiles(string directory, string expectedType)
    {
        if (!Directory.Exists(directory))
            return Enumerable.Empty<string>();

        return new DirectoryInfo(directory).GetFiles("*.xml")
            .Where(f => CSettingsMigration.IsSettingsType(f.FullName, expectedType))
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .OrderBy(n => n);
    }

    // ===== Selection handlers ======================================================================

    /// <summary>
    /// [XPLAT] Vehicle selection changed: reset non-current rows to the list background, highlight a
    /// non-current selection LightGreen, update the action buttons, and load the preview.
    /// </summary>
    private void ListViewVehicles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Reset every non-current row to the transparent default so the list background (InactiveCaption)
        // shows through — visually equivalent to the WinForms reset to listView.BackColor.
        foreach (ProfileRow li in _vehicleRows)
        {
            if (li.Name != RegistrySettings.vehicleProfileName)
                li.Background = Brushes.Transparent;
        }

        if (listViewVehicles.SelectedItem is ProfileRow sel)
        {
            _selectedVehicle = sel.Name;
            buttonDeleteVehicle.IsEnabled = _selectedVehicle != RegistrySettings.vehicleProfileName;
            buttonRenameVehicle.IsEnabled = true;

            if (_selectedVehicle != RegistrySettings.vehicleProfileName)
                sel.Background = ColorSelected;

            LoadVehiclePreview(_selectedVehicle);
        }
        else
        {
            _selectedVehicle = null;
            buttonDeleteVehicle.IsEnabled = false;
            buttonRenameVehicle.IsEnabled = false;
            ClearVehiclePreview();
        }

        UpdateSelectedLabels();
        UpdateLoadButton();
    }

    /// <summary>
    /// [XPLAT] Tool selection changed — analogous to <see cref="ListViewVehicles_SelectionChanged"/>.
    /// </summary>
    private void ListViewTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (ProfileRow li in _toolRows)
        {
            if (li.Name != RegistrySettings.toolProfileName)
                li.Background = Brushes.Transparent;
        }

        if (listViewTools.SelectedItem is ProfileRow sel)
        {
            _selectedTool = sel.Name;
            buttonDeleteTool.IsEnabled = _selectedTool != RegistrySettings.toolProfileName;
            buttonRenameTool.IsEnabled = true;

            if (_selectedTool != RegistrySettings.toolProfileName)
                sel.Background = ColorSelected;

            LoadToolPreview(_selectedTool);
        }
        else
        {
            _selectedTool = null;
            buttonDeleteTool.IsEnabled = false;
            buttonRenameTool.IsEnabled = false;
            ClearToolPreview();
        }

        UpdateSelectedLabels();
        UpdateLoadButton();
    }

    // ===== Preview population ======================================================================

    /// <summary>
    /// [XPLAT] Load a vehicle profile XML into a throw-away <see cref="VehicleSettings"/> and fill the
    /// vehicle preview labels. Note: <c>lblVehHitch</c> is intentionally NOT set here — see the quirk in
    /// <see cref="LoadToolPreview"/>.
    /// </summary>
    private void LoadVehiclePreview(string name)
    {
        VehicleSettings preview = new VehicleSettings();
        string path = Path.Combine(RegistrySettings.vehiclesDirectory, name + ".xml");
        if (File.Exists(path))
            XmlSettingsHandler.LoadXMLFile(path, preview);

        string type = preview.setVehicle_vehicleType == 0 ? gStr.gsTractor
            : preview.setVehicle_vehicleType == 1 ? gStr.gsHarvester
            : gStr.gsArticulated;

        lblVehType.Text = gStr.gsType + ": " + type;
        lblVehWheelbase.Text = gStr.gsWheelbase + ": " + preview.setVehicle_wheelbase.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblVehAntPivot.Text = gStr.gsAntennaPivot + ": " + preview.setVehicle_antennaPivot.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblVehAntOffset.Text = gStr.gsAntennaOffset + ": " + preview.setVehicle_antennaOffset.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblVehTrackWidth.Text = gStr.gsTrackWidth + ": " + preview.setVehicle_trackWidth.ToString("N2", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>[XPLAT] Reset the vehicle preview labels to their "&lt;label&gt;:" placeholders.</summary>
    private void ClearVehiclePreview()
    {
        lblVehType.Text = gStr.gsType + ":";
        lblVehWheelbase.Text = gStr.gsWheelbase + ":";
        lblVehAntPivot.Text = gStr.gsAntennaPivot + ":";
        lblVehAntOffset.Text = gStr.gsAntennaOffset + ":";
        lblVehTrackWidth.Text = gStr.gsTrackWidth + ":";
        lblVehHitch.Text = gStr.gsHitch + ":";
    }

    /// <summary>
    /// [XPLAT] Load a tool profile XML into a throw-away <see cref="ToolSettings"/> and fill the tool
    /// preview labels.
    ///
    /// <para><b>QUIRK (preserved exactly).</b> The vehicle-hitch label <c>lblVehHitch</c> is populated
    /// here from the TOOL preview's <c>setVehicle_hitchLength</c>, not from the vehicle preview. This
    /// faithfully reproduces the WinForms behavior.</para>
    /// </summary>
    private void LoadToolPreview(string name)
    {
        ToolSettings preview = new ToolSettings();
        string path = Path.Combine(RegistrySettings.toolsDirectory, name + ".xml");
        if (File.Exists(path))
            XmlSettingsHandler.LoadXMLFile(path, preview);

        string attach = preview.setTool_isToolFront ? gStr.gsFront
            : preview.setTool_isToolTBT ? gStr.gsTBT
            : preview.setTool_isToolRearFixed ? gStr.gsRearFixed
            : preview.setTool_isToolTrailing ? gStr.gsTrailing
            : "?";

        lblToolWidth.Text = gStr.gsWidth + ": " + preview.setVehicle_toolWidth.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblToolOverlap.Text = gStr.gsOverlap + ": " + preview.setVehicle_toolOverlap.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblToolOffset.Text = gStr.gsOffset + ": " + preview.setVehicle_toolOffset.ToString("N2", CultureInfo.InvariantCulture) + " m";
        lblToolSections.Text = gStr.gsSections + ": " + preview.setVehicle_numSections.ToString();
        lblToolAttach.Text = gStr.gsAttach + ": " + attach;
        lblToolHitch.Text = gStr.gsTrailingHitch + ": " + preview.setVehicle_toolTrailingHitchLength.ToString("N2", CultureInfo.InvariantCulture) + " m";

        // QUIRK: vehicle-hitch label populated from the TOOL preview — preserve EXACTLY.
        lblVehHitch.Text = gStr.gsHitch + ": " + preview.setVehicle_hitchLength.ToString("N2", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>[XPLAT] Reset the tool preview labels to their "&lt;label&gt;:" placeholders.</summary>
    private void ClearToolPreview()
    {
        lblToolWidth.Text = gStr.gsWidth + ":";
        lblToolOverlap.Text = gStr.gsOverlap + ":";
        lblToolOffset.Text = gStr.gsOffset + ":";
        lblToolSections.Text = gStr.gsSections + ":";
        lblToolAttach.Text = gStr.gsAttach + ":";
        lblToolHitch.Text = gStr.gsTrailingHitch + ":";
    }

    // ===== Load button gating ======================================================================

    /// <summary>
    /// [XPLAT] Load is enabled only when the selected vehicle or tool differs from the active profile
    /// (matches WinForms <c>UpdateLoadButton</c>).
    /// </summary>
    private void UpdateLoadButton()
    {
        bool vehicleChanged = _selectedVehicle != null && _selectedVehicle != RegistrySettings.vehicleProfileName;
        bool toolChanged = _selectedTool != null && _selectedTool != RegistrySettings.toolProfileName;
        buttonLoad.IsEnabled = vehicleChanged || toolChanged;
    }

    // ===== Load (the only button that closes the window) ===========================================

    /// <summary>
    /// [XPLAT] Apply the selected vehicle and/or tool. The window closes ONLY when a change is actually
    /// applied; every early return (job open, load failure) leaves the dialog open. The post-load
    /// apply-sequence below is the exact six-call order of the WinForms original (the former
    /// <c>_formGPS.*</c> calls, now injected delegates).
    /// </summary>
    private async void ButtonLoad_Click(object sender, RoutedEventArgs e)
    {
        if (isJobStarted())
        {
            errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst);
            return;
        }

        bool vehicleChanged = _selectedVehicle != null && _selectedVehicle != RegistrySettings.vehicleProfileName;
        bool toolChanged = _selectedTool != null && _selectedTool != RegistrySettings.toolProfileName;

        // Persist the in-memory current settings before swapping profiles.
        if (vehicleChanged) VehicleSettings.Default.Save();
        if (toolChanged) ToolSettings.Default.Save();

        if (vehicleChanged)
        {
            LoadResult result = VehicleSettings.Default.Load(_selectedVehicle);
            if (result != LoadResult.Ok)
            {
                Log.EventWriter($"Error loading vehicle {_selectedVehicle}.xml ({result})");
                await FormDialogView.ShowAsync(gStr.gsError,
                    gStr.gsErrorLoadingVehicle + " '" + _selectedVehicle + ".xml' " + Environment.NewLine +
                    gStr.gsFileResult + ": " + result + Environment.NewLine,
                    DialogSeverity.Error, owner: this);
                return;
            }
            RegistrySettings.Save(RegKeys.vehicleProfileName, _selectedVehicle);
            Log.EventWriter($"Vehicle loaded: {_selectedVehicle}");
        }

        if (toolChanged)
        {
            LoadResult result = ToolSettings.Default.Load(_selectedTool);
            if (result != LoadResult.Ok)
            {
                Log.EventWriter($"Error loading tool {_selectedTool}.xml ({result})");
                await FormDialogView.ShowAsync(gStr.gsError,
                    gStr.gsErrorLoadingTool + " '" + _selectedTool + ".xml' " + Environment.NewLine +
                    gStr.gsFileResult + ": " + result + Environment.NewLine,
                    DialogSeverity.Error, owner: this);
                return;
            }
            RegistrySettings.Save(RegKeys.toolProfileName, _selectedTool);
            Log.EventWriter($"Tool loaded: {_selectedTool}");
        }

        if (vehicleChanged || toolChanged)
        {
            // EXACT order (was _formGPS.* calls): recreate vehicle, recreate tool, reload settings,
            // push textures, send settings, send relay settings.
            recreateVehicle();
            recreateTool();
            reloadSettings();
            setVehicleTextures();
            sendSettings();
            sendRelaySettings();

            string msg = "";
            if (vehicleChanged) msg += $"Vehicle: {_selectedVehicle}";
            if (vehicleChanged && toolChanged) msg += "  |  ";
            if (toolChanged) msg += $"Tool: {_selectedTool}";

            errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2500), gStr.gsLoad, msg);
            Close();   // closes ONLY here (the WinForms form had no DialogResult)
        }
    }

    // ===== Delete ==================================================================================

    /// <summary>[XPLAT] Delete the selected vehicle profile (cannot delete the active one).</summary>
    private async void ButtonDeleteVehicle_Click(object sender, RoutedEventArgs e)
    {
        if (isJobStarted() || string.IsNullOrEmpty(_selectedVehicle)) return;

        if (_selectedVehicle == RegistrySettings.vehicleProfileName)
        {
            await FormDialogView.ShowAsync(gStr.gsVehicleInUse, gStr.gsCannotDeleteActiveVehicle, DialogSeverity.Error, owner: this);
            return;
        }

        bool ok = await FormDialogView.ShowQuestionAsync(gStr.gsDelete, gStr.gsDeleteVehicleConfirm + " '" + _selectedVehicle + "'?", owner: this);
        if (ok)
        {
            string path = Path.Combine(RegistrySettings.vehiclesDirectory, _selectedVehicle + ".xml");
            if (File.Exists(path)) File.Delete(path);
            Log.EventWriter($"Vehicle deleted: {_selectedVehicle}");
            RefreshVehicleList();
            UpdateSelectedLabels();
            UpdateLoadButton();
        }
    }

    /// <summary>[XPLAT] Delete the selected tool profile (cannot delete the active one).</summary>
    private async void ButtonDeleteTool_Click(object sender, RoutedEventArgs e)
    {
        if (isJobStarted() || string.IsNullOrEmpty(_selectedTool)) return;

        if (_selectedTool == RegistrySettings.toolProfileName)
        {
            await FormDialogView.ShowAsync(gStr.gsToolInUse, gStr.gsCannotDeleteActiveTool, DialogSeverity.Error, owner: this);
            return;
        }

        bool ok = await FormDialogView.ShowQuestionAsync(gStr.gsDelete, gStr.gsDeleteToolConfirm + " '" + _selectedTool + "'?", owner: this);
        if (ok)
        {
            string path = Path.Combine(RegistrySettings.toolsDirectory, _selectedTool + ".xml");
            if (File.Exists(path)) File.Delete(path);
            Log.EventWriter($"Tool deleted: {_selectedTool}");
            RefreshToolList();
            UpdateSelectedLabels();
            UpdateLoadButton();
        }
    }

    // ===== Rename ==================================================================================

    /// <summary>
    /// [XPLAT] Rename the selected vehicle profile. A case-only rename is allowed (the exists-check uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>). If the active profile was renamed, the registry
    /// key is updated.
    /// </summary>
    private async void ButtonRenameVehicle_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedVehicle)) return;

        string oldName = _selectedVehicle;
        string newName = await PromptForName(gStr.gsRenameVehicle, gStr.gsEnterNewVehicleName, oldName);
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;

        string oldPath = Path.Combine(RegistrySettings.vehiclesDirectory, oldName + ".xml");
        string newPath = Path.Combine(RegistrySettings.vehiclesDirectory, newName + ".xml");

        if (File.Exists(newPath) && !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsVehicleExists + " '" + newName + "'", DialogSeverity.Error, owner: this);
            return;
        }

        File.Move(oldPath, newPath);
        Log.EventWriter($"Vehicle renamed: {oldName} -> {newName}");
        if (oldName == RegistrySettings.vehicleProfileName)
            RegistrySettings.Save(RegKeys.vehicleProfileName, newName);

        RefreshVehicleList();
        _selectedVehicle = null;
        UpdateSelectedLabels();
        UpdateLoadButton();
    }

    /// <summary>
    /// [XPLAT] Rename the selected tool profile — analogous to <see cref="ButtonRenameVehicle_Click"/>,
    /// preserving the case-only-rename allowance.
    /// </summary>
    private async void ButtonRenameTool_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedTool)) return;

        string oldName = _selectedTool;
        string newName = await PromptForName(gStr.gsRenameTool, gStr.gsEnterNewToolName, oldName);
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;

        string oldPath = Path.Combine(RegistrySettings.toolsDirectory, oldName + ".xml");
        string newPath = Path.Combine(RegistrySettings.toolsDirectory, newName + ".xml");

        if (File.Exists(newPath) && !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsToolExists + " '" + newName + "'", DialogSeverity.Error, owner: this);
            return;
        }

        File.Move(oldPath, newPath);
        Log.EventWriter($"Tool renamed: {oldName} -> {newName}");
        if (oldName == RegistrySettings.toolProfileName)
            RegistrySettings.Save(RegKeys.toolProfileName, newName);

        RefreshToolList();
        _selectedTool = null;
        UpdateSelectedLabels();
        UpdateLoadButton();
    }

    // ===== New (copy from current) =================================================================

    /// <summary>[XPLAT] Create a new vehicle profile by copying the current <see cref="VehicleSettings"/>.</summary>
    private async void ButtonNewVehicle_Click(object sender, RoutedEventArgs e)
    {
        string name = await PromptForName(gStr.gsNewVehicle, gStr.gsEnterVehicleName);
        if (string.IsNullOrEmpty(name)) return;

        string path = Path.Combine(RegistrySettings.vehiclesDirectory, name + ".xml");
        if (File.Exists(path) && CSettingsMigration.IsSettingsType(path, "VehicleSettings"))
        {
            await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsVehicleExists + " '" + name + "'", DialogSeverity.Error, owner: this);
            return;
        }

        XmlSettingsHandler.SaveXMLFile(path, VehicleSettings.Default);   // copy from current
        Log.EventWriter($"New vehicle created (from current): {name}");
        RefreshVehicleList();
    }

    /// <summary>[XPLAT] Create a new tool profile by copying the current <see cref="ToolSettings"/>.</summary>
    private async void ButtonNewTool_Click(object sender, RoutedEventArgs e)
    {
        string name = await PromptForName(gStr.gsNewTool, gStr.gsEnterToolName);
        if (string.IsNullOrEmpty(name)) return;

        string path = Path.Combine(RegistrySettings.toolsDirectory, name + ".xml");
        if (File.Exists(path) && CSettingsMigration.IsSettingsType(path, "ToolSettings"))
        {
            await FormDialogView.ShowAsync(gStr.gsExists, gStr.gsToolExists + " '" + name + "'", DialogSeverity.Error, owner: this);
            return;
        }

        XmlSettingsHandler.SaveXMLFile(path, ToolSettings.Default);   // copy from current
        Log.EventWriter($"New tool created (from current): {name}");
        RefreshToolList();
    }

    // ===== Reset to default ========================================================================

    /// <summary>
    /// [XPLAT] Create and load a fresh "Default" vehicle profile. The apply-sequence here is four calls
    /// and deliberately omits <c>sendRelaySettings</c> (vehicle reset does not touch the machine relay).
    /// </summary>
    private async void ButtonResetVehicle_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await FormDialogView.ShowQuestionAsync(gStr.gsCreateDefaultVehicle,
            gStr.gsCreateDefaultVehicleConfirm + Environment.NewLine + Environment.NewLine + "This will create and load the new profile.",
            DialogSeverity.Info, owner: this);
        if (!ok) return;

        string defaultName = "Default";
        string path = Path.Combine(RegistrySettings.vehiclesDirectory, defaultName + ".xml");
        if (File.Exists(path))
        {
            bool overwrite = await FormDialogView.ShowQuestionAsync(gStr.gsOverwrite,
                gStr.gsProfileExistsOverwrite + " '" + defaultName + "'?", DialogSeverity.Warning, owner: this);
            if (!overwrite) return;
        }

        VehicleSettings fresh = new VehicleSettings();
        XmlSettingsHandler.SaveXMLFile(path, fresh);
        Log.EventWriter($"Default vehicle profile created: {defaultName}");

        LoadResult loadResult = VehicleSettings.Default.Load(defaultName);
        if (loadResult != LoadResult.Ok)
        {
            Log.EventWriter($"Error loading vehicle {defaultName}.xml ({loadResult})");
            await FormDialogView.ShowAsync(gStr.gsError,
                gStr.gsErrorLoadingVehicle + " '" + defaultName + ".xml' " + Environment.NewLine + gStr.gsFileResult + ": " + loadResult,
                DialogSeverity.Error, owner: this);
            RefreshVehicleList();
            return;
        }

        RegistrySettings.Save(RegKeys.vehicleProfileName, defaultName);
        Log.EventWriter($"Vehicle loaded: {defaultName}");

        // Apply order (NO sendRelaySettings for vehicle reset).
        recreateVehicle();
        reloadSettings();
        setVehicleTextures();
        sendSettings();

        RefreshVehicleList();
        errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2500), gStr.gsLoad, gStr.gsDefaultVehicleLoaded);
    }

    /// <summary>
    /// [XPLAT] Create and load a fresh "Default" tool profile. The apply-sequence here is four calls and
    /// deliberately omits <c>setVehicleTextures</c> (tool reset does not change vehicle textures).
    /// </summary>
    private async void ButtonResetTool_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await FormDialogView.ShowQuestionAsync(gStr.gsCreateDefaultTool,
            gStr.gsCreateDefaultToolConfirm + Environment.NewLine + Environment.NewLine + "This will create and load the new profile.",
            DialogSeverity.Info, owner: this);
        if (!ok) return;

        string defaultName = "Default";
        string path = Path.Combine(RegistrySettings.toolsDirectory, defaultName + ".xml");
        if (File.Exists(path))
        {
            bool overwrite = await FormDialogView.ShowQuestionAsync(gStr.gsOverwrite,
                gStr.gsProfileExistsOverwrite + " '" + defaultName + "'?", DialogSeverity.Warning, owner: this);
            if (!overwrite) return;
        }

        ToolSettings fresh = new ToolSettings();
        XmlSettingsHandler.SaveXMLFile(path, fresh);
        Log.EventWriter($"Default tool profile created: {defaultName}");

        LoadResult loadResult = ToolSettings.Default.Load(defaultName);
        if (loadResult != LoadResult.Ok)
        {
            Log.EventWriter($"Error loading tool {defaultName}.xml ({loadResult})");
            await FormDialogView.ShowAsync(gStr.gsError,
                gStr.gsErrorLoadingTool + " '" + defaultName + ".xml' " + Environment.NewLine + gStr.gsFileResult + ": " + loadResult,
                DialogSeverity.Error, owner: this);
            RefreshToolList();
            return;
        }

        RegistrySettings.Save(RegKeys.toolProfileName, defaultName);
        Log.EventWriter($"Tool loaded: {defaultName}");

        // Apply order (NO setVehicleTextures for tool reset).
        recreateTool();
        reloadSettings();
        sendSettings();
        sendRelaySettings();

        RefreshToolList();
        errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2500), gStr.gsLoad, gStr.gsDefaultToolLoaded);
    }

    // ===== Convert old profiles ====================================================================

    /// <summary>
    /// [XPLAT] Open the legacy-profile converter, then refresh BOTH lists (and only the lists — matching
    /// the WinForms original, which did not touch labels or the Load button afterward).
    /// </summary>
    private async void ButtonConvertOld_Click(object sender, RoutedEventArgs e)
    {
        FormConvertProfilesView form = new FormConvertProfilesView(reloadSettings);
        await form.ShowDialog(this);
        RefreshVehicleList();
        RefreshToolList();
    }

    // ===== Cancel ==================================================================================

    /// <summary>[XPLAT] Close without applying (WinForms DialogResult.Cancel).</summary>
    private void ButtonCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    // ===== Name prompt helpers =====================================================================

    /// <summary>
    /// [XPLAT] Prompt for a profile name via the on-screen input dialog, honoring the user's virtual
    /// keyboard preference (<c>Settings.Default.setDisplay_isKeyboardOn</c>). Returns null on cancel.
    /// </summary>
    private Task<string> PromptForName(string title, string prompt)
        => FormInputDialogView.ShowInputAsync(title, prompt, Properties.Settings.Default.setDisplay_isKeyboardOn, this);

    /// <summary>
    /// [XPLAT] Prompt for a profile name pre-filled with <paramref name="defaultValue"/> (used by rename).
    /// </summary>
    private Task<string> PromptForName(string title, string prompt, string defaultValue)
        => FormInputDialogView.ShowInputAsync(title, prompt, Properties.Settings.Default.setDisplay_isKeyboardOn, this, defaultValue);

    // ===== Row item type (bound by the .axaml ItemTemplate via reflection) =========================

    /// <summary>
    /// [XPLAT] One profile row in a list. The paired <c>.axaml</c> binds <see cref="Name"/>,
    /// <see cref="Background"/>, and <see cref="FontWeight"/> by reflection (x:CompileBindings="False").
    /// Rows default to <see cref="Avalonia.Media.FontWeight.Bold"/> to match the WinForms ListView font
    /// (Tahoma, Bold); <see cref="Background"/> raises change notifications so selection re-coloring
    /// updates live.
    /// </summary>
    private sealed class ProfileRow : INotifyPropertyChanged
    {
        public string Name { get; init; }

        private IBrush _background = Brushes.Transparent;
        public IBrush Background
        {
            get => _background;
            set
            {
                if (!ReferenceEquals(_background, value))
                {
                    _background = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
                }
            }
        }

        public FontWeight FontWeight { get; set; } = FontWeight.Bold;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
