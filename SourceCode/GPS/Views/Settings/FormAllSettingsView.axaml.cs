// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormAllSettings.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the read-only "All Settings" diagnostic viewer.
//
// Parity notes vs the WinForms original (FormAllSettings):
//   * The WinForms form populated three tabs (Vehicle / Tool / Environment, each split into three
//     DataGridView columns) plus a System tab by reading VehicleSettings / ToolSettings / Settings
//     defaults-vs-current and the live FormGPS "mf" diagnostics. Reading those settings models and
//     the live guidance state is the host's responsibility (Settings is being de-Windowsed and the
//     diagnostics live on FormGPS), so this view exposes ten ObservableCollection<SettingRow>
//     (bound as the ItemsSource of the ten ItemsControls) and three profile-name labels; the host
//     fills the rows. This is the dependency-inversion seam mandated by AAP §0.3.2. The WinForms
//     timer1_Tick that refreshed the System tab once per second is replaced by the host mutating the
//     System collection on its own cadence — no empty timer is introduced.
//   * SettingRow exposes exactly Setting / Profile / Default / IsHeader / IsChanged: these property
//     names are the reflection-binding contract used by the markup item templates.
//   * The four action buttons declare only x:Name (no Click in markup), so their handlers are wired
//     imperatively here, matching the WinForms convention. btnClose closes; btnExportCSV writes the
//     populated rows to <baseDirectory>/AllSettings.csv (self-contained, using the rows already held
//     here); btnScreenShot / btnCreatePNG reproduced the WinForms composite-bitmap capture
//     (DrawToBitmap + Clipboard / PNG save) which is inherently per-platform rendering — that
//     capture is raised to the host via ScreenshotRequested / CreatePngRequested, and CreatePNG then
//     closes as the WinForms handler did.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgLibrary.Logging;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormAllSettingsView : Window
    {
        /// <summary>
        /// One row of the settings viewer. The property NAMES are the reflection-binding contract
        /// referenced by the markup item templates and MUST NOT change.
        /// </summary>
        public sealed class SettingRow
        {
            public string Setting { get; set; } = string.Empty;
            public string Profile { get; set; } = string.Empty;
            public string Default { get; set; } = string.Empty;
            public bool IsHeader { get; set; }
            public bool IsChanged { get; set; }
        }

        public FormAllSettingsView()
        {
            InitializeComponent();

            // Bind each grid's ItemsSource to its backing collection; the host populates them.
            dgvVehicleL.ItemsSource = VehicleLeft;
            dgvVehicleM.ItemsSource = VehicleMiddle;
            dgvVehicleR.ItemsSource = VehicleRight;
            dgvToolL.ItemsSource = ToolLeft;
            dgvToolM.ItemsSource = ToolMiddle;
            dgvToolR.ItemsSource = ToolRight;
            dgvEnvironmentL.ItemsSource = EnvironmentLeft;
            dgvEnvironmentM.ItemsSource = EnvironmentMiddle;
            dgvEnvironmentR.ItemsSource = EnvironmentRight;
            dgvSystem.ItemsSource = System;

            // The four action buttons declare no Click in markup; wire them here (WinForms convention).
            btnExportCSV.Click += btnExportCSV_Click;
            btnScreenShot.Click += btnScreenShot_Click;
            btnCreatePNG.Click += btnCreatePNG_Click;
            btnClose.Click += btnClose_Click;
        }

        // Backing collections the host fills (defaults-vs-current rows, grouped exactly as the
        // WinForms grids were).
        public ObservableCollection<SettingRow> VehicleLeft { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> VehicleMiddle { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> VehicleRight { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> ToolLeft { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> ToolMiddle { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> ToolRight { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> EnvironmentLeft { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> EnvironmentMiddle { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> EnvironmentRight { get; } = new ObservableCollection<SettingRow>();
        public ObservableCollection<SettingRow> System { get; } = new ObservableCollection<SettingRow>();

        /// <summary>Raised by the Screenshot button; the host captures the tabs and copies to clipboard.</summary>
        public event EventHandler ScreenshotRequested;

        /// <summary>Raised by the Create-PNG button; the host saves a composite PNG of the tabs.</summary>
        public event EventHandler CreatePngRequested;

        /// <summary>Host entry point: set the three profile-name labels shown in the header.</summary>
        public void SetProfileNames(string vehicle, string tool, string environment)
        {
            lblVehicleName.Text = "Vehicle: " + (vehicle ?? string.Empty);
            lblToolName.Text = "Tool: " + (tool ?? string.Empty);
            lblEnvironmentName.Text = "Environment: " + (environment ?? string.Empty);
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // Export every populated row to <baseDirectory>/AllSettings.csv. Fully self-contained: it
        // serializes the rows already held here, so it needs no FormGPS / Settings access.
        private async void btnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(RegistrySettings.baseDirectory ?? string.Empty, "AllSettings.csv");
            try
            {
                using (var sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    WriteSection(sw, "VEHICLE", VehicleLeft, VehicleMiddle, VehicleRight);
                    WriteSection(sw, "TOOL", ToolLeft, ToolMiddle, ToolRight);
                    WriteSection(sw, "ENVIRONMENT", EnvironmentLeft, EnvironmentMiddle, EnvironmentRight);
                    WriteSection(sw, "SYSTEM", System);
                }

                await AgOpenGPS.Views.FormDialogView.ShowAsync(
                    this, "Exported", "Saved to " + path, AgOpenGPS.Views.DialogSeverity.Info);
            }
            catch (Exception ex)
            {
                Log.EventWriter("View All Settings CSV export failed: " + ex.Message);
                await AgOpenGPS.Views.FormDialogView.ShowAsync(
                    this, "Export Error", "Could not write " + path, AgOpenGPS.Views.DialogSeverity.Error);
            }
        }

        // Composite-bitmap capture to the clipboard is per-platform rendering; the host performs it.
        private void btnScreenShot_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotRequested?.Invoke(this, EventArgs.Empty);
        }

        // Composite-PNG save is per-platform rendering; the host performs it, then the dialog closes
        // (parity with the WinForms handler, which saved then closed).
        private void btnCreatePNG_Click(object sender, RoutedEventArgs e)
        {
            CreatePngRequested?.Invoke(this, EventArgs.Empty);
            Close(true);
        }

        private static void WriteSection(StreamWriter sw, string title, params ObservableCollection<SettingRow>[] columns)
        {
            sw.WriteLine(CsvEscape(title));
            foreach (ObservableCollection<SettingRow> column in columns)
            {
                foreach (SettingRow row in column)
                {
                    if (row.IsHeader)
                    {
                        sw.WriteLine(CsvEscape(row.Setting));
                    }
                    else
                    {
                        sw.WriteLine(string.Join(",", CsvEscape(row.Setting), CsvEscape(row.Profile), CsvEscape(row.Default)));
                    }
                }
            }
            sw.WriteLine();
        }

        // Quote-and-escape a CSV field exactly as the WinForms CsvEscape did.
        private static string CsvEscape(string s)
        {
            s ??= string.Empty;
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
