// [XPLAT] migrated from net48/WinForms (Forms/Profiles/FormLoadProfile.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgLibrary.Logging;

namespace AgOpenGPS.Views.Profiles
{
    /// <summary>
    /// [XPLAT] Avalonia code-behind for the "Load Environment" dialog, migrated 1:1 from the WinForms
    /// <c>FormLoadProfile</c>. The dialog lists existing environment profiles and lets the user delete a
    /// profile or load one.
    ///
    /// The XAML declares no event handlers, so the list, the selection-driven button state, and the
    /// Load/Delete/Cancel buttons are wired programmatically in the constructor (the same imperative
    /// pattern used by the other non-MVVM dialogs). Self-contained here: the profile list is enumerated
    /// from <c>RegistrySettings.environmentDirectory</c>, and Delete removes the <c>.xml</c> file directly
    /// after a confirmation (refusing to delete the in-use environment). The actual load
    /// (<c>Properties.Settings.Default.Load()</c> + <c>FormGPS.LoadSettings()</c>) mutates global state and reloads the
    /// running app, so it is host-owned: Load returns the chosen <see cref="SelectedProfileName"/> via
    /// <c>Close(true)</c> for the host to apply.
    /// </summary>
    public partial class FormLoadProfileView : Window
    {
        /// <summary>[XPLAT] The profile the user chose to load (set when Load is pressed).</summary>
        public string SelectedProfileName { get; private set; } = string.Empty;

        public FormLoadProfileView()
        {
            InitializeComponent();

            // The XAML declares no handlers; wire the imperative behaviour here.
            listViewProfiles.SelectionChanged += ListViewProfiles_SelectionChanged;
            buttonLoad.Click += ButtonLoad_Click;
            buttonProfileDelete.Click += ButtonProfileDelete_Click;
            buttonCancel.Click += ButtonCancel_Click;
        }

        // [XPLAT] FormLoadProfile_Load: populate the list and disable the selection-dependent buttons.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            RefreshProfiles();
            buttonLoad.IsEnabled = false;
            buttonProfileDelete.IsEnabled = false;
        }

        private void RefreshProfiles()
        {
            listViewProfiles.ItemsSource = new ObservableCollection<string>(EnumerateProfiles());
            listViewProfiles.SelectedItem = null;
        }

        // [XPLAT] LoadProfiles(): enumerate *.xml under the environment directory.
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

        // [XPLAT] listViewProfiles_SelectedIndexChanged: enable Load/Delete only when a row is selected.
        private void ListViewProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = listViewProfiles.SelectedItem is string;
            buttonLoad.IsEnabled = hasSelection;
            buttonProfileDelete.IsEnabled = hasSelection;
        }

        // [XPLAT] buttonProfileDelete_Click: confirm, then delete the profile .xml — but never the
        // environment currently in use. (The WinForms job-open guard, FormGPS.isJobStarted, is host-owned.)
        private async void ButtonProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(listViewProfiles.SelectedItem is string profileName) || string.IsNullOrEmpty(profileName))
                return;

            if (string.Equals(RegistrySettings.environmentFileName, profileName, StringComparison.Ordinal))
            {
                await FormDialogView.ShowAsync(this, "Environment currently in use", "Select a different environment.");
                return;
            }

            bool ok = await FormDialogView.ShowQuestionAsync(this, "Delete", $"Delete {profileName}.xml ?");
            if (!ok) return;

            try
            {
                string dir = RegistrySettings.environmentDirectory;
                if (!string.IsNullOrEmpty(dir))
                    File.Delete(Path.Combine(dir, profileName + ".xml"));
                Log.EventWriter($"Environment profile deleted: {profileName}.xml");
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Error deleting environment profile {profileName}.xml: {ex.Message}");
            }

            RefreshProfiles();
            buttonLoad.IsEnabled = false;
            buttonProfileDelete.IsEnabled = false;
        }

        // [XPLAT] buttonOK_Click: capture the selection and return success; the host performs
        // Properties.Settings.Default.Load() + FormGPS.LoadSettings(). (The WinForms job-open guard is host-owned.)
        private void ButtonLoad_Click(object sender, RoutedEventArgs e)
        {
            if (!(listViewProfiles.SelectedItem is string profileName) || string.IsNullOrEmpty(profileName))
                return;

            SelectedProfileName = profileName;
            Close(true);
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
