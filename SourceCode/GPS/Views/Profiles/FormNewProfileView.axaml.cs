// [XPLAT] migrated from net48/WinForms (Forms/Profiles/FormNewProfile.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views.Profiles
{
    /// <summary>
    /// [XPLAT] Avalonia code-behind for the "Create New Environment Profile" dialog, migrated 1:1 from
    /// the WinForms <c>FormNewProfile</c>. The dialog lets the user type a new environment name and pick
    /// a "copy from" source (a blank/empty reset, the current environment, or an existing profile).
    ///
    /// Self-contained here (no FormGPS dependency): the profile list is enumerated directly from
    /// <c>RegistrySettings.environmentDirectory</c>, the name field is sanitised with the shared
    /// <c>glm.fileRegex</c>, and Create is gated on a non-empty name. The deep create itself
    /// (Properties.Settings.Default reset/save/load + <c>FormGPS.LoadSettings()</c>) mutates global application
    /// state and reloads the running app, so it is host-owned: this dialog returns the validated intent
    /// (<see cref="NewProfileName"/> + <see cref="SourceProfileName"/>) via <c>Close(true)</c> and the
    /// host maps it to the original three create modes (empty / from-current / from-existing).
    /// </summary>
    public partial class FormNewProfileView : Window
    {
        /// <summary>Sentinel row representing "start from a blank/empty environment profile".</summary>
        private const string EmptyProfile = "<Empty Profile>";

        // Re-entrancy guard so the sanitising TextChanged handler does not recurse when it rewrites Text.
        private bool _suppressTextChanged;

        /// <summary>
        /// [XPLAT] One ListBox row. The XAML <c>ItemTemplate</c> binds <c>{Binding Display}</c> and
        /// <c>{Binding CurrentMarker}</c> by reflection (the file has no <c>x:DataType</c>), so these
        /// property names are part of the view contract.
        /// </summary>
        public sealed class ProfileRow
        {
            public string Name { get; set; } = string.Empty;
            public string Display { get; set; } = string.Empty;
            public string CurrentMarker { get; set; } = string.Empty;
        }

        /// <summary>[XPLAT] The validated new profile name the user entered (set when Create is pressed).</summary>
        public string NewProfileName { get; private set; } = string.Empty;

        /// <summary>
        /// [XPLAT] The "copy from" source the user chose. Equals <see cref="EmptyProfile"/> for a blank
        /// reset, the current environment name to clone the running settings, or another profile name to
        /// clone an existing file. The host maps this to the original CreateNewEmptyProfile /
        /// CreateNewProfileFromCurrent / CreateNewProfileFromExisting flows.
        /// </summary>
        public string SourceProfileName { get; private set; } = string.Empty;

        public FormNewProfileView()
        {
            InitializeComponent();
        }

        // [XPLAT] FormNewProfile_Load: build the list (empty sentinel + existing profiles) and select
        // the current environment if present, else the empty row; Create starts disabled.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PopulateProfiles();
            buttonCreate.IsEnabled = false;
        }

        private void PopulateProfiles()
        {
            var rows = new ObservableCollection<ProfileRow>
            {
                new ProfileRow { Name = EmptyProfile, Display = EmptyProfile, CurrentMarker = string.Empty }
            };

            string current = RegistrySettings.environmentFileName;
            ProfileRow currentRow = null;
            foreach (string name in EnumerateProfiles())
            {
                bool isCurrent = string.Equals(name, current, StringComparison.Ordinal);
                var row = new ProfileRow
                {
                    Name = name,
                    Display = name,
                    CurrentMarker = isCurrent ? "(current)" : string.Empty
                };
                rows.Add(row);
                if (isCurrent) currentRow = row;
            }

            listViewProfiles.ItemsSource = rows;

            // Mirror the WinForms default selection: current profile if present, otherwise the empty row.
            listViewProfiles.SelectedItem = currentRow ?? rows[0];
        }

        // [XPLAT] LoadProfiles(): enumerate *.xml under the environment directory (InvariantCulture
        // ordering for cross-platform stability).
        private static IEnumerable<string> EnumerateProfiles()
        {
            string dir = RegistrySettings.environmentDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Enumerable.Empty<string>();

            return new DirectoryInfo(dir).GetFiles("*.xml")
                .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }

        // Declared in XAML: TextChanged="TextBoxName_TextChanged".
        // [XPLAT] textBoxName_TextChanged: strip invalid filename characters while preserving the caret,
        // then enable Create only for a non-empty name.
        private void TextBoxName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChanged) return;
            _suppressTextChanged = true;
            try
            {
                int caret = textBoxName.CaretIndex;
                string raw = textBoxName.Text ?? string.Empty;
                string clean = Regex.Replace(raw, glm.fileRegex, string.Empty);
                if (!string.Equals(clean, raw, StringComparison.Ordinal))
                {
                    textBoxName.Text = clean;
                    textBoxName.CaretIndex = Math.Min(caret, clean.Length);
                }
                buttonCreate.IsEnabled = !string.IsNullOrEmpty(clean.Trim());
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }

        // Declared in XAML: Tapped="TextBoxName_Tapped".
        private void TextBoxName_Tapped(object sender, TappedEventArgs e)
        {
            // [XPLAT] The WinForms textBoxName_Click opened the on-screen keyboard when
            // FormGPS.isKeyboardOn (and blocked edits with a "close field first" message while a job was
            // open). Both the keyboard and the job-open guard depend on the FormGPS god-object and are
            // host-owned at this checkpoint; with a physical keyboard the field edits directly, so there
            // is no action to take here.
        }

        // Declared in XAML: Click="ButtonCreate_Click".
        // [XPLAT] buttonOK_Click: validate/sanitise the name, capture the chosen source, and return the
        // intent to the host (which performs the Properties.Settings.Default mutation + FormGPS.LoadSettings).
        private void ButtonCreate_Click(object sender, RoutedEventArgs e)
        {
            string name = Regex.Replace((textBoxName.Text ?? string.Empty).Trim(), glm.fileRegex, string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!(listViewProfiles.SelectedItem is ProfileRow source)) return;

            NewProfileName = name;
            SourceProfileName = source.Name;
            Close(true);
        }

        // Declared in XAML: Click="ButtonCancel_Click".
        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
