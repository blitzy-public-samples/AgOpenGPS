// [XPLAT] migrated from net48/WinForms (Forms/Profiles/FormConvertProfiles.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Views.Profiles
{
    /// <summary>
    /// [XPLAT] Avalonia code-behind for the "Convert Old Profiles" dialog, migrated 1:1 from the
    /// WinForms <c>FormConvertProfiles</c>. The dialog lists legacy (pre-split) profile files, lets the
    /// user choose whether to import the Vehicle and/or Tool half of each old file (with editable target
    /// names), and triggers the migration.
    ///
    /// <para><b>Discipline (AAP §0.3.2 dependency-inversion):</b> the <em>list</em> of convertible files
    /// and their already-converted state come from the deep <c>CSettingsMigration</c> API
    /// (<c>GetConvertibleFiles</c>/<c>IsConverted</c>), and the conversion itself
    /// (<c>CSettingsMigration.MigrateVehicle/MigrateTool/MigrateEnvironment</c> + <c>MarkAsConverted</c>
    /// followed by <c>FormGPS.LoadSettings()</c>) mutates global application state. Those services are
    /// host-owned and are <em>not</em> fabricated here. Instead the host pushes the file list in via
    /// <see cref="LoadConvertibleFiles"/> and listens to <see cref="ConvertRequested"/> for the validated
    /// conversion intent, then re-pushes the refreshed list (the original keeps the dialog open after a
    /// conversion).</para>
    ///
    /// <para><b>Self-contained here:</b> the Vehicle/Tool toggle state machine (text + colour flip and
    /// the matching name-box enable), the <c>glm.fileRegex</c> filename sanitisation with caret
    /// preservation, the details-panel enable on selection, and the Convert-button gating — all of which
    /// are pure UI behaviour reproduced exactly from the WinForms source.</para>
    /// </summary>
    public partial class FormConvertProfilesView : Window
    {
        // LightGreen / LightGray parity swatches for the toggle buttons (WinForms Color.LightGreen /
        // Color.LightGray). Theme-independent on purpose — these mirror the original fixed control colours.
        private static readonly IBrush ToggleOnBrush = Brushes.LightGreen;
        private static readonly IBrush ToggleOffBrush = Brushes.LightGray;

        // Re-entrancy guard so the sanitising TextChanged handler does not recurse when it rewrites Text.
        private bool _suppressTextChanged;

        // Vehicle/Tool import toggles — both default ON, mirroring FormConvertProfiles' field defaults.
        private bool _vehicleEnabled = true;
        private bool _toolEnabled = true;

        // The bound list of convertible files (host-injected). ObservableCollection so the ListBox
        // refreshes if the host re-pushes after a conversion.
        private readonly ObservableCollection<ConvertRow> _files = new ObservableCollection<ConvertRow>();

        /// <summary>
        /// [XPLAT] One ListBox row. The XAML <c>ItemTemplate</c> binds <c>{Binding Name}</c> and
        /// <c>Classes.converted="{Binding IsConverted}"</c> by reflection (the file has no
        /// <c>x:DataType</c>), so these property names are part of the view contract.
        /// </summary>
        public sealed class ConvertRow
        {
            /// <summary>Display name of the old profile file (without extension).</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>True if this old file has already been converted (paints the row LightGreen).</summary>
            public bool IsConverted { get; set; }
        }

        /// <summary>
        /// [XPLAT] Validated conversion intent raised by <see cref="ConvertRequested"/>. The host maps
        /// this onto the original <c>CSettingsMigration</c> calls. <see cref="VehicleName"/>/<see cref="ToolName"/>
        /// are non-empty only when the matching export flag is set.
        /// </summary>
        public sealed class ConvertRequest
        {
            public string SourceFile { get; set; } = string.Empty;
            public bool ExportVehicle { get; set; }
            public string VehicleName { get; set; } = string.Empty;
            public bool ExportTool { get; set; }
            public string ToolName { get; set; } = string.Empty;
        }

        /// <summary>
        /// [XPLAT] Raised when the user presses Convert with a valid selection. The host performs the
        /// deep migration (<c>CSettingsMigration.*</c> + <c>FormGPS.LoadSettings()</c>), then typically
        /// re-pushes the refreshed file list via <see cref="LoadConvertibleFiles"/>. The dialog stays
        /// open, exactly as the WinForms original did.
        /// </summary>
        public event EventHandler<ConvertRequest> ConvertRequested;

        public FormConvertProfilesView()
        {
            InitializeComponent();

            // The ItemsSource is wired here (not in XAML) so the host only has to fill the collection.
            listViewFiles.ItemsSource = _files;
        }

        /// <summary>
        /// [XPLAT] Host entry point: replace the convertible-file list. Mirrors the original
        /// <c>RefreshFileList</c> population (which enumerated old files via <c>CSettingsMigration</c>),
        /// but the enumeration is performed by the host because it owns that service.
        /// </summary>
        public void LoadConvertibleFiles(IEnumerable<ConvertRow> files)
        {
            _files.Clear();
            if (files != null)
            {
                foreach (ConvertRow row in files)
                {
                    if (row != null)
                    {
                        _files.Add(row);
                    }
                }
            }

            // RefreshFileList(true) clears the details panel and resets the status line / Convert button.
            ClearDetails();
            UpdateStatus();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Mirror the WinForms Load: details panel starts disabled, toggles painted ON, status set.
            ClearDetails();
            UpdateStatus();
        }

        // ----- Selection ---------------------------------------------------------------------------

        private void ListViewFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listViewFiles.SelectedItem is ConvertRow selected)
            {
                // Both halves default to the selected file's name (WinForms parity), both toggles ON.
                _suppressTextChanged = true;
                try
                {
                    textBoxVehicleName.Text = selected.Name;
                    textBoxToolName.Text = selected.Name;
                }
                finally
                {
                    _suppressTextChanged = false;
                }

                _vehicleEnabled = true;
                _toolEnabled = true;
                panelDetails.IsEnabled = true;
                UpdateToggleButtons();
                UpdateConvertButton();
            }
            else
            {
                ClearDetails();
            }
        }

        // ----- Toggle buttons ----------------------------------------------------------------------

        private void BtnToggleVehicle_Click(object sender, RoutedEventArgs e)
        {
            _vehicleEnabled = !_vehicleEnabled;
            UpdateToggleButtons();
            UpdateConvertButton();
        }

        private void BtnToggleTool_Click(object sender, RoutedEventArgs e)
        {
            _toolEnabled = !_toolEnabled;
            UpdateToggleButtons();
            UpdateConvertButton();
        }

        /// <summary>
        /// [XPLAT] Reproduces <c>UpdateToggleButtons</c>: each toggle shows "Import …" + LightGreen when
        /// on and "No Import" + LightGray when off, and enables/disables the matching name box. The
        /// captions are the same literals the WinForms <c>gStr</c> resources resolved to at runtime
        /// (<c>gsImportVehicle</c>/<c>gsImportTool</c>/<c>gsNoImport</c>).
        /// </summary>
        private void UpdateToggleButtons()
        {
            if (_vehicleEnabled)
            {
                btnToggleVehicle.Content = "Import Vehicle";
                btnToggleVehicle.Background = ToggleOnBrush;
                textBoxVehicleName.IsEnabled = true;
            }
            else
            {
                btnToggleVehicle.Content = "No Import";
                btnToggleVehicle.Background = ToggleOffBrush;
                textBoxVehicleName.IsEnabled = false;
            }

            if (_toolEnabled)
            {
                btnToggleTool.Content = "Import Tool";
                btnToggleTool.Background = ToggleOnBrush;
                textBoxToolName.IsEnabled = true;
            }
            else
            {
                btnToggleTool.Content = "No Import";
                btnToggleTool.Background = ToggleOffBrush;
                textBoxToolName.IsEnabled = false;
            }
        }

        // ----- Name editing ------------------------------------------------------------------------

        /// <summary>
        /// [XPLAT] Shared TextChanged handler for both name boxes (wired on each TextBox in XAML). Strips
        /// invalid filename characters via <c>glm.fileRegex</c> while preserving the caret, then
        /// re-evaluates the Convert button. <paramref name="sender"/> identifies which box fired.
        /// </summary>
        private void TextBoxName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged) return;

            if (sender is TextBox tb)
            {
                _suppressTextChanged = true;
                try
                {
                    int caret = tb.CaretIndex;
                    string raw = tb.Text ?? string.Empty;
                    string clean = Regex.Replace(raw, glm.fileRegex, string.Empty);
                    if (clean != raw)
                    {
                        tb.Text = clean;
                        tb.CaretIndex = Math.Min(caret, clean.Length);
                    }
                }
                finally
                {
                    _suppressTextChanged = false;
                }
            }

            UpdateConvertButton();
        }

        /// <summary>
        /// [XPLAT] Both name boxes raise Tapped to summon the on-screen keyboard in the WinForms build.
        /// The touch keyboard is a host/shell concern on the cross-platform stack (it is not owned by an
        /// individual dialog), so this is an intentional no-op kept to satisfy the XAML handler binding.
        /// </summary>
        private void TextBox_Tapped(object sender, TappedEventArgs e)
        {
            // Intentionally empty — see summary. The shell decides whether/how to show a touch keyboard.
        }

        // ----- Convert / Close ---------------------------------------------------------------------

        /// <summary>
        /// [XPLAT] Reproduces <c>UpdateConvertButton</c>: Convert is enabled only when a file is selected,
        /// every <em>enabled</em> half has a non-blank name, and at least one half is enabled.
        /// </summary>
        private void UpdateConvertButton()
        {
            bool hasSelection = listViewFiles.SelectedItem is ConvertRow;
            bool vehicleValid = !_vehicleEnabled || !string.IsNullOrWhiteSpace(textBoxVehicleName.Text);
            bool toolValid = !_toolEnabled || !string.IsNullOrWhiteSpace(textBoxToolName.Text);
            bool atLeastOne = _vehicleEnabled || _toolEnabled;

            buttonConvert.IsEnabled = hasSelection && vehicleValid && toolValid && atLeastOne;
        }

        private void ButtonConvert_Click(object sender, RoutedEventArgs e)
        {
            if (!(listViewFiles.SelectedItem is ConvertRow selected)) return;

            bool exportVehicle = _vehicleEnabled;
            string vehicleName = exportVehicle ? (textBoxVehicleName.Text ?? string.Empty).Trim() : string.Empty;
            bool exportTool = _toolEnabled;
            string toolName = exportTool ? (textBoxToolName.Text ?? string.Empty).Trim() : string.Empty;

            // Validate non-empty names for the enabled halves (WinForms parity early-return guards).
            if (exportVehicle && string.IsNullOrEmpty(vehicleName)) return;
            if (exportTool && string.IsNullOrEmpty(toolName)) return;

            // Hand the validated intent to the host, which performs the CSettingsMigration conversion
            // (including any duplicate-name overwrite confirmation against RegistrySettings.vehicles/
            // toolsDirectory and the subsequent FormGPS.LoadSettings()). The dialog stays open and the
            // host re-pushes the refreshed list via LoadConvertibleFiles, exactly as the original did.
            ConvertRequested?.Invoke(this, new ConvertRequest
            {
                SourceFile = selected.Name,
                ExportVehicle = exportVehicle,
                VehicleName = vehicleName,
                ExportTool = exportTool,
                ToolName = toolName
            });
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            // WinForms DialogResult.Cancel — no migration intent returned.
            Close(false);
        }

        // ----- Helpers -----------------------------------------------------------------------------

        /// <summary>Reproduces <c>ClearDetails</c>: blank the name boxes, reset toggles, disable panel/Convert.</summary>
        private void ClearDetails()
        {
            _suppressTextChanged = true;
            try
            {
                textBoxVehicleName.Text = string.Empty;
                textBoxToolName.Text = string.Empty;
            }
            finally
            {
                _suppressTextChanged = false;
            }

            _vehicleEnabled = true;
            _toolEnabled = true;
            panelDetails.IsEnabled = false;
            buttonConvert.IsEnabled = false;
            UpdateToggleButtons();
        }

        /// <summary>
        /// [XPLAT] Status line mirroring <c>RefreshFileList</c>: "no old files" when empty, otherwise the
        /// count of convertible files. The exact phrasing came from <c>gStr</c> resources; the
        /// behaviour (empty vs count) is reproduced here from the injected list.
        /// </summary>
        private void UpdateStatus()
        {
            labelStatus.Text = _files.Count == 0
                ? "No old-format files to convert."
                : _files.Count + " old-format file(s) to convert.";
        }
    }
}
