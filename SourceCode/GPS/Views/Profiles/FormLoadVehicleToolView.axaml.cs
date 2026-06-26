// [XPLAT] migrated from net48/WinForms (Forms/Profiles/FormLoadVehicleTool.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AgLibrary.Logging;

namespace AgOpenGPS.Views.Profiles
{
    /// <summary>
    /// [XPLAT] Avalonia code-behind for the "Vehicle / Tool" loader, migrated 1:1 from the WinForms
    /// <c>FormLoadVehicleTool</c>. The dialog shows two side-by-side lists (saved vehicle profiles and
    /// saved tool profiles), a per-list set of management buttons (Delete / New / Rename / Reset), a
    /// live "Current:" header, a "Selected:" sub-header, a read-only preview of the selected profile's
    /// key parameters, and Convert-Old / Cancel / Load actions.
    ///
    /// <para><b>Discipline (AAP §0.3.2 dependency-inversion):</b> three things require services this
    /// dialog must not own — (1) the <em>preview</em> values come from parsing a profile's
    /// <c>VehicleSettings</c>/<c>ToolSettings</c> XML; (2) <em>Load</em> mutates global
    /// <c>VehicleSettings.Default</c>/<c>ToolSettings.Default</c>, persists <c>RegistrySettings</c>, and
    /// reloads the running <c>FormGPS</c>; (3) <em>New/Rename/Reset/Convert-Old</em> use
    /// <c>XmlSettingsHandler</c>/<c>CSettingsMigration</c> plus the on-screen keyboard. Those are all
    /// host-owned. The dialog surfaces them through the <see cref="ActionRequested"/>,
    /// <see cref="VehicleSelected"/> and <see cref="ToolSelected"/> seams and accepts preview data back
    /// via <see cref="SetVehiclePreview"/>/<see cref="SetToolPreview"/>; the host re-pushes lists with
    /// <see cref="RefreshLists"/> after a mutation and closes the dialog on a successful Load.</para>
    ///
    /// <para><b>Self-contained here:</b> enumerating the profile files (a plain <c>*.xml</c> directory
    /// scan, faithful to the original <c>GetFiles</c> helper), the Orange-current / LightGreen-selected
    /// row colouring, the "Current:"/"Selected:" header text, the per-button enable/disable rules, and
    /// the Delete confirm + <c>File.Delete</c> (paths come only from <c>RegistrySettings</c>).</para>
    /// </summary>
    public partial class FormLoadVehicleToolView : Window
    {
        // WinForms parity row colours: current profile = Orange, selected (non-current) = LightGreen,
        // everything else transparent so the list's own panel colour shows through.
        private static readonly IBrush ColorCurrent = Brushes.Orange;
        private static readonly IBrush ColorSelected = Brushes.LightGreen;
        private static readonly IBrush ColorRow = Brushes.Transparent;

        private readonly ObservableCollection<ProfileRow> _vehicles = new ObservableCollection<ProfileRow>();
        private readonly ObservableCollection<ProfileRow> _tools = new ObservableCollection<ProfileRow>();

        // Currently highlighted names (null == nothing selected), mirroring _selectedVehicle/_selectedTool.
        private string _selectedVehicle;
        private string _selectedTool;

        // Captured XAML default ("cleared") preview-label texts, restored when a preview is unavailable.
        private string _defVehType, _defVehWheelbase, _defVehAntPivot, _defVehAntOffset, _defVehTrackWidth, _defVehHitch;
        private string _defToolWidth, _defToolOverlap, _defToolOffset, _defToolSections, _defToolAttach, _defToolHitch;

        /// <summary>
        /// [XPLAT] One list row. The XAML <c>ItemTemplate</c> binds <c>{Binding Name}</c>,
        /// <c>{Binding Background}</c> and <c>{Binding FontWeight}</c> by reflection
        /// (<c>x:CompileBindings="False"</c>), so these member names are part of the view contract.
        /// <see cref="Background"/> raises change notification because selection re-colours rows in place.
        /// </summary>
        public sealed class ProfileRow : INotifyPropertyChanged
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>True when this row is the currently active profile (painted Orange + Bold).</summary>
            public bool IsCurrent { get; set; }

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

            // The WinForms list font is uniformly bold, so every row is Bold (current included).
            public FontWeight FontWeight { get; set; } = FontWeight.Bold;

            public event PropertyChangedEventHandler PropertyChanged;
        }

        /// <summary>[XPLAT] The six preview lines for a vehicle profile (already formatted by the host).</summary>
        public sealed class VehiclePreview
        {
            public string Type { get; set; } = string.Empty;
            public string Wheelbase { get; set; } = string.Empty;
            public string AntennaPivot { get; set; } = string.Empty;
            public string AntennaOffset { get; set; } = string.Empty;
            public string TrackWidth { get; set; } = string.Empty;
            public string Hitch { get; set; } = string.Empty;
        }

        /// <summary>[XPLAT] The six preview lines for a tool profile (already formatted by the host).</summary>
        public sealed class ToolPreview
        {
            public string Width { get; set; } = string.Empty;
            public string Overlap { get; set; } = string.Empty;
            public string Offset { get; set; } = string.Empty;
            public string Sections { get; set; } = string.Empty;
            public string Attach { get; set; } = string.Empty;
            public string Hitch { get; set; } = string.Empty;
        }

        /// <summary>[XPLAT] Host-owned profile management actions surfaced by this dialog.</summary>
        public enum ProfileAction
        {
            NewVehicle, RenameVehicle, ResetVehicle,
            NewTool, RenameTool, ResetTool,
            ConvertOld, Load
        }

        /// <summary>[XPLAT] Payload for <see cref="ActionRequested"/>: the action plus the current selection.</summary>
        public sealed class ProfileActionEventArgs : EventArgs
        {
            public ProfileActionEventArgs(ProfileAction action, string selectedVehicle, string selectedTool)
            {
                Action = action;
                SelectedVehicle = selectedVehicle;
                SelectedTool = selectedTool;
            }

            public ProfileAction Action { get; }
            public string SelectedVehicle { get; }
            public string SelectedTool { get; }
        }

        /// <summary>
        /// [XPLAT] Raised for every host-owned action (New/Rename/Reset/Convert-Old/Load). The host
        /// performs the deep work (settings load/save, <c>FormGPS.LoadSettings()</c>, migration, keyboard
        /// prompts) and, for non-Load actions, calls <see cref="RefreshLists"/> afterwards. On a
        /// successful <see cref="ProfileAction.Load"/> the host closes this window.
        /// </summary>
        public event EventHandler<ProfileActionEventArgs> ActionRequested;

        /// <summary>[XPLAT] Raised when the vehicle selection changes so the host can supply a preview.</summary>
        public event EventHandler<string> VehicleSelected;

        /// <summary>[XPLAT] Raised when the tool selection changes so the host can supply a preview.</summary>
        public event EventHandler<string> ToolSelected;

        public FormLoadVehicleToolView()
        {
            InitializeComponent();

            listViewVehicles.ItemsSource = _vehicles;
            listViewTools.ItemsSource = _tools;

            // Capture the XAML "cleared" preview text so ClearVehicle/ToolPreview can restore it verbatim.
            _defVehType = lblVehType.Text;
            _defVehWheelbase = lblVehWheelbase.Text;
            _defVehAntPivot = lblVehAntPivot.Text;
            _defVehAntOffset = lblVehAntOffset.Text;
            _defVehTrackWidth = lblVehTrackWidth.Text;
            _defVehHitch = lblVehHitch.Text;
            _defToolWidth = lblToolWidth.Text;
            _defToolOverlap = lblToolOverlap.Text;
            _defToolOffset = lblToolOffset.Text;
            _defToolSections = lblToolSections.Text;
            _defToolAttach = lblToolAttach.Text;
            _defToolHitch = lblToolHitch.Text;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            RefreshLists();

            // Buttons that depend on a selection start disabled (mirrors the WinForms initial state).
            buttonDeleteVehicle.IsEnabled = false;
            buttonRenameVehicle.IsEnabled = false;
            buttonDeleteTool.IsEnabled = false;
            buttonRenameTool.IsEnabled = false;
            buttonLoad.IsEnabled = false;

            UpdateCurrentLabels();
            UpdateSelectedLabels();
        }

        // ===== List population =====================================================================

        /// <summary>
        /// [XPLAT] Re-enumerate both lists and pre-select the active profiles. Public so the host can
        /// call it after a New/Rename/Reset/Convert mutation (it owns those operations).
        /// </summary>
        public void RefreshLists()
        {
            RefreshVehicleList();
            RefreshToolList();
        }

        private void RefreshVehicleList()
        {
            _vehicles.Clear();
            string current = RegistrySettings.vehicleProfileName;
            foreach (string name in EnumerateProfiles(RegistrySettings.vehiclesDirectory))
            {
                bool isCurrent = string.Equals(name, current, StringComparison.Ordinal);
                _vehicles.Add(new ProfileRow
                {
                    Name = name,
                    IsCurrent = isCurrent,
                    Background = isCurrent ? ColorCurrent : ColorRow,
                    FontWeight = FontWeight.Bold
                });
            }

            // Pre-select the active vehicle (SelectionChanged fills _selectedVehicle + colouring).
            ProfileRow currentRow = _vehicles.FirstOrDefault(r => r.IsCurrent);
            listViewVehicles.SelectedItem = currentRow;
        }

        private void RefreshToolList()
        {
            _tools.Clear();
            string current = RegistrySettings.toolProfileName;
            foreach (string name in EnumerateProfiles(RegistrySettings.toolsDirectory))
            {
                bool isCurrent = string.Equals(name, current, StringComparison.Ordinal);
                _tools.Add(new ProfileRow
                {
                    Name = name,
                    IsCurrent = isCurrent,
                    Background = isCurrent ? ColorCurrent : ColorRow,
                    FontWeight = FontWeight.Bold
                });
            }

            ProfileRow currentRow = _tools.FirstOrDefault(r => r.IsCurrent);
            listViewTools.SelectedItem = currentRow;
        }

        /// <summary>
        /// [XPLAT] Faithful port of the original <c>GetFiles</c> helper: every <c>*.xml</c> file name
        /// (without extension) in <paramref name="directory"/>, ordinal-sorted. Missing directories
        /// yield an empty list rather than throwing.
        /// </summary>
        private static IEnumerable<string> EnumerateProfiles(string directory)
        {
            var names = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory, "*.xml"))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (Exception ex)
            {
                // Enumeration is best-effort; a bad path must not crash the dialog.
                Log.EventWriter("FormLoadVehicleToolView enumerate error: " + ex.Message);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        // ===== Selection ===========================================================================

        private void ListViewVehicles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Reset every row to its base colour (current = Orange, others = transparent), clearing any
            // previous LightGreen highlight — matches the WinForms reset loop.
            foreach (ProfileRow row in _vehicles)
            {
                row.Background = row.IsCurrent ? ColorCurrent : ColorRow;
            }

            if (listViewVehicles.SelectedItem is ProfileRow selected)
            {
                _selectedVehicle = selected.Name;
                buttonDeleteVehicle.IsEnabled = !string.Equals(_selectedVehicle, RegistrySettings.vehicleProfileName, StringComparison.Ordinal);
                buttonRenameVehicle.IsEnabled = true;

                // Highlight a non-current selection LightGreen; the current row keeps its Orange paint.
                if (!selected.IsCurrent)
                {
                    selected.Background = ColorSelected;
                }

                // Ask the host for this profile's preview values (deep VehicleSettings XML parse).
                VehicleSelected?.Invoke(this, _selectedVehicle);
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

        private void ListViewTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (ProfileRow row in _tools)
            {
                row.Background = row.IsCurrent ? ColorCurrent : ColorRow;
            }

            if (listViewTools.SelectedItem is ProfileRow selected)
            {
                _selectedTool = selected.Name;
                buttonDeleteTool.IsEnabled = !string.Equals(_selectedTool, RegistrySettings.toolProfileName, StringComparison.Ordinal);
                buttonRenameTool.IsEnabled = true;

                if (!selected.IsCurrent)
                {
                    selected.Background = ColorSelected;
                }

                ToolSelected?.Invoke(this, _selectedTool);
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

        // ===== Header / preview labels =============================================================

        /// <summary>[XPLAT] "Current: &lt;name&gt;" headers, driven only by <c>RegistrySettings</c>.</summary>
        private void UpdateCurrentLabels()
        {
            lblCurrentVehicle.Text = string.IsNullOrEmpty(RegistrySettings.vehicleProfileName)
                ? "Current: None"
                : "Current: " + RegistrySettings.vehicleProfileName;
            lblCurrentTool.Text = string.IsNullOrEmpty(RegistrySettings.toolProfileName)
                ? "Current: None"
                : "Current: " + RegistrySettings.toolProfileName;
        }

        /// <summary>[XPLAT] "Selected: &lt;name&gt;" sub-headers, driven by the current selection.</summary>
        private void UpdateSelectedLabels()
        {
            lblSelectedVehicle.Text = _selectedVehicle != null ? "Selected: " + _selectedVehicle : "Selected: -";
            lblSelectedTool.Text = _selectedTool != null ? "Selected: " + _selectedTool : "Selected: -";
        }

        /// <summary>[XPLAT] Host callback: fill the vehicle preview labels (null clears them).</summary>
        public void SetVehiclePreview(VehiclePreview preview)
        {
            if (preview == null)
            {
                ClearVehiclePreview();
                return;
            }

            lblVehType.Text = preview.Type;
            lblVehWheelbase.Text = preview.Wheelbase;
            lblVehAntPivot.Text = preview.AntennaPivot;
            lblVehAntOffset.Text = preview.AntennaOffset;
            lblVehTrackWidth.Text = preview.TrackWidth;
            lblVehHitch.Text = preview.Hitch;
        }

        /// <summary>[XPLAT] Host callback: fill the tool preview labels (null clears them).</summary>
        public void SetToolPreview(ToolPreview preview)
        {
            if (preview == null)
            {
                ClearToolPreview();
                return;
            }

            lblToolWidth.Text = preview.Width;
            lblToolOverlap.Text = preview.Overlap;
            lblToolOffset.Text = preview.Offset;
            lblToolSections.Text = preview.Sections;
            lblToolAttach.Text = preview.Attach;
            lblToolHitch.Text = preview.Hitch;
        }

        private void ClearVehiclePreview()
        {
            lblVehType.Text = _defVehType;
            lblVehWheelbase.Text = _defVehWheelbase;
            lblVehAntPivot.Text = _defVehAntPivot;
            lblVehAntOffset.Text = _defVehAntOffset;
            lblVehTrackWidth.Text = _defVehTrackWidth;
            lblVehHitch.Text = _defVehHitch;
        }

        private void ClearToolPreview()
        {
            lblToolWidth.Text = _defToolWidth;
            lblToolOverlap.Text = _defToolOverlap;
            lblToolOffset.Text = _defToolOffset;
            lblToolSections.Text = _defToolSections;
            lblToolAttach.Text = _defToolAttach;
            lblToolHitch.Text = _defToolHitch;
        }

        // ===== Load button gating ==================================================================

        /// <summary>
        /// [XPLAT] Reproduces <c>UpdateLoadButton</c>: Load is enabled only when the selected vehicle or
        /// tool differs from the currently active one.
        /// </summary>
        private void UpdateLoadButton()
        {
            bool vehicleChanged = _selectedVehicle != null && !string.Equals(_selectedVehicle, RegistrySettings.vehicleProfileName, StringComparison.Ordinal);
            bool toolChanged = _selectedTool != null && !string.Equals(_selectedTool, RegistrySettings.toolProfileName, StringComparison.Ordinal);
            buttonLoad.IsEnabled = vehicleChanged || toolChanged;
        }

        // ===== Delete (self-contained) =============================================================

        private async void ButtonDeleteVehicle_Click(object sender, RoutedEventArgs e)
        {
            await DeleteProfileAsync(_selectedVehicle, RegistrySettings.vehicleProfileName, RegistrySettings.vehiclesDirectory, isVehicle: true);
        }

        private async void ButtonDeleteTool_Click(object sender, RoutedEventArgs e)
        {
            await DeleteProfileAsync(_selectedTool, RegistrySettings.toolProfileName, RegistrySettings.toolsDirectory, isVehicle: false);
        }

        /// <summary>
        /// [XPLAT] Confirm-then-delete a profile file. Self-contained: it refuses to delete the active
        /// profile, asks for confirmation via <see cref="FormDialogView"/>, deletes the <c>*.xml</c>
        /// under the supplied <c>RegistrySettings</c> directory, then re-enumerates the list.
        /// </summary>
        private async System.Threading.Tasks.Task DeleteProfileAsync(string name, string current, string directory, bool isVehicle)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Guard: the currently active profile cannot be deleted (the button is also disabled for it).
            if (string.Equals(name, current, StringComparison.Ordinal)) return;

            bool confirmed = await AgOpenGPS.Views.FormDialogView.ShowQuestionAsync(
                this, "Delete", "Delete profile '" + name + "'?");
            if (!confirmed) return;

            try
            {
                string path = Path.Combine(directory ?? string.Empty, name + ".xml");
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.EventWriter("Profile deleted: " + path);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("FormLoadVehicleToolView delete error: " + ex.Message);
                await AgOpenGPS.Views.FormDialogView.ShowAsync(
                    this, "Error", "Could not delete '" + name + "'." + Environment.NewLine + ex.Message,
                    AgOpenGPS.Views.DialogSeverity.Error);
            }

            // Re-enumerate (the deleted row disappears; selection resets to the active profile).
            if (isVehicle) RefreshVehicleList(); else RefreshToolList();
            UpdateSelectedLabels();
            UpdateLoadButton();
        }

        // ===== Host-owned actions (event seams) ====================================================

        private void ButtonNewVehicle_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.NewVehicle);

        private void ButtonRenameVehicle_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.RenameVehicle);

        private void ButtonResetVehicle_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.ResetVehicle);

        private void ButtonNewTool_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.NewTool);

        private void ButtonRenameTool_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.RenameTool);

        private void ButtonResetTool_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.ResetTool);

        private void ButtonConvertOld_Click(object sender, RoutedEventArgs e) =>
            RaiseAction(ProfileAction.ConvertOld);

        private void ButtonLoad_Click(object sender, RoutedEventArgs e) =>
            // The host performs the deep load (settings save/load + FormGPS reload, with its own
            // job-started guard and error dialogs) and closes this window on success.
            RaiseAction(ProfileAction.Load);

        private void RaiseAction(ProfileAction action) =>
            ActionRequested?.Invoke(this, new ProfileActionEventArgs(action, _selectedVehicle, _selectedTool));

        // ===== Cancel ==============================================================================

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            // WinForms DialogResult.Cancel.
            Close(false);
        }
    }
}
