// [XPLAT] migrated from net48/WinForms FormProfiles.cs + FormProfiles.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgIO.Properties;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// MVVM view-model backing the AgIO "Manage Profiles" dialog. It replaces the WinForms
    /// <c>FormProfiles</c> (<c>Forms/FormProfiles.cs</c> + <c>Forms/FormProfiles.designer.cs</c>,
    /// namespace <c>AgIO</c>, constructor <c>FormProfiles(Form callingForm)</c>), which enumerated the
    /// profile directory with a <see cref="DirectoryInfo"/>, confirmed actions with
    /// <c>MessageBox.Show(...)</c>, and applied a profile through
    /// <see cref="Settings"/>.<c>Default.Reset()/Save()/Load()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this manager does.</b> Each AgIO settings profile is a single XML file kept in
    /// <see cref="RegistrySettings.profileDirectory"/>; the active profile is named by
    /// <see cref="RegistrySettings.profileName"/> and <c>Settings.Default.Load()/Save()</c> read and
    /// write <c>&lt;profileName&gt;.XML</c> in that folder. This view-model therefore only enumerates,
    /// loads, saves, and deletes those files — it never reformats them. The settings XML schema and the
    /// <c>CSettingsMigration</c> round-trip are FROZEN (AAP §0.2.2), so all file content flows through
    /// the unchanged <see cref="Settings"/> API.
    /// </para>
    /// <para>
    /// <b>God-object removal.</b> The WinForms dialog took a <c>FormLoop</c> reference (<c>mf</c>) used
    /// only to read the touch-keyboard flag and to live-filter text. Both couplings are dropped: the
    /// manager operates on <see cref="Settings"/>.<c>Default</c> plus the profile directory alone, and
    /// the on-screen keyboard / live text filtering are view concerns handled by the paired Avalonia
    /// view (the converted <c>../Controls/</c> <c>ShowKeyboard</c> helper plus <c>FormKeyboard</c>).
    /// </para>
    /// <para>
    /// <b>Dialogs without owning a window (AAP §0.3.2).</b> A view-model cannot own an Avalonia
    /// <c>Window</c>, so the WinForms message boxes are surfaced as intents the code-behind fulfils:
    /// Yes/No confirmations (overwrite, delete) are requested through the awaitable
    /// <see cref="ConfirmAsync"/> callback — wired by the view to
    /// <c>await new FormYes(message, true).ShowDialog&lt;bool&gt;(owner)</c> — and informational
    /// (OK-only) notices go to the injected <see cref="IErrorPresenter"/> (the cross-platform
    /// <c>FormTimedMessage</c> sink). Applying a profile raises <see cref="ProfileApplied"/> so the host
    /// can reload any settings-derived state, and dialog dismissal is raised through
    /// <see cref="RequestClose"/>.
    /// </para>
    /// <para>
    /// <b>Cross-platform correctness (AAP §0.6.5).</b> Every file path is composed with
    /// <see cref="Path.Combine(string, string)"/> (no hard-coded <c>'\\'</c>), and profile enumeration
    /// matches the <c>.xml</c> extension case-INSENSITIVELY because <see cref="Settings"/> persists with
    /// an uppercase <c>.XML</c> suffix while Linux and macOS use case-sensitive filesystems — a
    /// case-sensitive <c>*.xml</c> filter would silently miss every profile. The same case-insensitive
    /// match keeps the <c>.last</c> backup files written by <see cref="Settings"/> out of the list.
    /// </para>
    /// </remarks>
    public class FormProfilesViewModel : ViewModel
    {
        // [XPLAT] Parity title/message literals carried verbatim from FormProfiles.cs so the operator
        // sees the same prompts as the WinForms original (the trailing "..." is intentional).
        private const string SaveAndReturnTitle = "Save And Return";
        private const string EnterFileNameMessage = "Enter a File Name To Save...";
        private const string DeleteConfirmMessage = "Delete profile?";

        // [XPLAT] Canonical on-disk extension. Settings.Save()/Load() compose "<profileName>.XML"
        // (uppercase), so profiles created by this app are always ".XML"; new files are written with
        // this casing to round-trip exactly with the frozen Settings layer.
        private const string ProfileFileExtension = ".XML";

        // [XPLAT] Case-insensitive extension token used both for enumeration and for the overwrite
        // prompt text ("Overwrite: <name>.xml" matched the WinForms message exactly).
        private const string ProfileFileExtensionMatch = ".xml";

        // [XPLAT] Ported verbatim from the WinForms FormProfiles.SanitizeFileName: strip the characters
        // that are illegal in a file name (< > : " / \ | ? *) before composing a profile path.
        private static readonly Regex InvalidFileRegex =
            new Regex("[" + Regex.Escape(@"<>:""/\|?*") + "]");

        // [XPLAT] Informational/error sink (replaces MessageBox.Show(...OK...)). Injected so the manager
        // never owns a window; guarded with ?. at every call site for graceful degradation.
        private readonly IErrorPresenter _errorPresenter;

        private string _selectedProfile = string.Empty;
        private string _newProfileName = string.Empty;
        private string _currentProfileName = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormProfilesViewModel"/> class and eagerly
        /// loads the list of existing profiles, mirroring the WinForms <c>FormCommSaver_Load</c> handler
        /// that populated the dialog's combo boxes when it opened.
        /// </summary>
        /// <param name="errorPresenter">
        /// The sink used for informational (OK-only) notifications — the cross-platform replacement for
        /// the WinForms <c>MessageBox.Show(...OK...)</c> calls. A <c>null</c> value is tolerated (every
        /// use is null-conditional), so the manager still functions without a presenter attached.
        /// </param>
        public FormProfilesViewModel(IErrorPresenter errorPresenter)
        {
            _errorPresenter = errorPresenter;

            Profiles = new ObservableCollection<string>();

            // The commands raise intents only; a view-model owns no window, so closing the dialog,
            // refreshing the host, and presenting confirmations are delegated to the view via events
            // and the ConfirmAsync callback. Save/Delete are async because they await ConfirmAsync.
            LoadCommand = new RelayCommand(OnLoad);
            SaveCommand = new RelayCommand(OnSave);
            DeleteCommand = new RelayCommand(OnDelete);
            CloseCommand = new RelayCommand(OnClose);

            RefreshProfiles();
        }

        /// <summary>
        /// Gets the display names (file names without extension) of the profiles found in
        /// <see cref="RegistrySettings.profileDirectory"/>. Replaces the WinForms combo-box item lists
        /// (<c>cboxOverWrite</c> / <c>cboxChooseExisting</c>); the bound list control selects into
        /// <see cref="SelectedProfile"/>.
        /// </summary>
        public ObservableCollection<string> Profiles { get; }

        /// <summary>
        /// Gets or sets the profile currently selected in the list. <see cref="LoadCommand"/> applies it
        /// and <see cref="DeleteCommand"/> removes it; an empty value makes both commands no-ops.
        /// </summary>
        public string SelectedProfile
        {
            get { return _selectedProfile; }
            set
            {
                if (value != _selectedProfile)
                {
                    _selectedProfile = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the name typed for a new (or "save as") profile — the former
        /// <c>tboxCreateNew</c> / <c>tboxSaveAs</c> text. It is sanitized and validated by
        /// <see cref="SaveCommand"/> before a file is written.
        /// </summary>
        public string NewProfileName
        {
            get { return _newProfileName; }
            set
            {
                if (value != _newProfileName)
                {
                    _newProfileName = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the name of the active profile (<see cref="RegistrySettings.profileName"/>), backing the
        /// WinForms <c>lblLast</c> ("Using Profile: …") and <c>lblCurrentProfile</c> labels. Refreshed
        /// whenever the active profile changes; the private setter raises a change notification.
        /// </summary>
        public string CurrentProfileName
        {
            get { return _currentProfileName; }
            private set
            {
                if (value != _currentProfileName)
                {
                    _currentProfileName = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that applies <see cref="SelectedProfile"/> as the active profile and reloads
        /// its values. Parity with the WinForms <c>cboxChooseExisting_SelectedIndexChanged</c> "Open"
        /// flow (set the profile name, then <c>Settings.Default.Load()</c>).
        /// </summary>
        public RelayCommand LoadCommand { get; }

        /// <summary>
        /// Gets the command that writes the current <see cref="Settings"/> to a profile named
        /// <see cref="NewProfileName"/>. Parity with the WinForms <c>btnSaveAs_Click</c> flow, with the
        /// <c>cboxOverWrite</c> confirmation folded in (an existing target is confirmed through
        /// <see cref="ConfirmAsync"/> before it is overwritten).
        /// </summary>
        public RelayCommand SaveCommand { get; }

        /// <summary>
        /// Gets the command that deletes <see cref="SelectedProfile"/> after a Yes/No confirmation, then
        /// refreshes the list. The dialog stays open so further management can continue.
        /// </summary>
        public RelayCommand DeleteCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without changing the active profile, by raising
        /// <see cref="RequestClose"/>. Parity with the WinForms <c>btnSerialCancel_Click</c> handler.
        /// </summary>
        public RelayCommand CloseCommand { get; }

        /// <summary>
        /// Asynchronous Yes/No confirmation callback — the MVVM replacement for the WinForms
        /// <c>MessageBox.Show(..., MessageBoxButtons.YesNo, ...)</c> prompts. The view assigns this to
        /// <c>message =&gt; new FormYes(message, true).ShowDialog&lt;bool&gt;(owner)</c>; commands proceed
        /// only when the returned task yields <c>true</c>. It is initialized to a safe default that
        /// declines (returns <c>false</c>) so a confirmation is never silently treated as accepted when
        /// the view has not wired a real prompt.
        /// </summary>
        public Func<string, Task<bool>> ConfirmAsync = message => Task.FromResult(false);

        /// <summary>
        /// Raised after a profile is applied (loaded) or saved as the active profile, so the host can
        /// reload any settings-derived state — the explicit, decoupled replacement for the WinForms
        /// <c>mf</c> back-reference. Initialized to a no-op delegate so it is always safe to raise.
        /// </summary>
        public event Action ProfileApplied = delegate { };

        /// <summary>
        /// Raised to ask the hosting Avalonia <c>Window</c> to close the dialog (the former
        /// <c>Close()</c> call). It is raised on cancel and after a profile is applied or saved.
        /// Initialized to a no-op delegate so it is always safe to raise.
        /// </summary>
        public event Action RequestClose = delegate { };

        /// <summary>
        /// Re-enumerates <see cref="RegistrySettings.profileDirectory"/> into <see cref="Profiles"/> and
        /// refreshes <see cref="CurrentProfileName"/>. Mirrors the WinForms <c>FormCommSaver_Load</c>
        /// population step and is re-run after a save or delete so the list stays current.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Enumeration uses <see cref="Directory.GetFiles(string)"/> and filters the extension
        /// case-insensitively (<see cref="StringComparison.OrdinalIgnoreCase"/>): <see cref="Settings"/>
        /// writes <c>.XML</c> (uppercase) yet Linux/macOS are case-sensitive, so a literal <c>*.xml</c>
        /// pattern would miss every profile. The same filter excludes the <c>.last</c> backup files. A
        /// missing or not-yet-created directory simply yields an empty list (graceful degradation).
        /// </remarks>
        public void RefreshProfiles()
        {
            Profiles.Clear();

            string directory = RegistrySettings.profileDirectory;
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (Path.GetExtension(file).Equals(ProfileFileExtensionMatch, StringComparison.OrdinalIgnoreCase))
                    {
                        Profiles.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }

            CurrentProfileName = RegistrySettings.profileName ?? string.Empty;
        }

        /// <summary>
        /// Applies <see cref="SelectedProfile"/> as the active profile and reloads its values, then
        /// notifies the host and requests the dialog close. Reproduces the WinForms
        /// <c>cboxChooseExisting_SelectedIndexChanged</c> handler.
        /// </summary>
        /// <remarks>
        /// [XPLAT] The WinForms call was <c>Settings.Default.Load()</c> after setting the registry
        /// profile key; the AgIO <see cref="Settings"/> exposes <c>Load()</c> (there is no
        /// <c>Reload()</c>), and the registry write is now the cross-platform
        /// <see cref="RegistrySettings.Save(string, string)"/> backed by <c>IPlatformServices</c>. An
        /// empty selection is a no-op, and any failure is reported through the presenter without
        /// applying or closing.
        /// </remarks>
        private void OnLoad()
        {
            string profile = SelectedProfile;
            if (string.IsNullOrEmpty(profile))
            {
                return;
            }

            try
            {
                // Point the active profile at the selection, then load that profile's XML so its values
                // take effect — exactly the WinForms "Open" sequence, behaviour frozen.
                RegistrySettings.Save(RegKeys.profileName, profile);
                Settings.Default.Load();
                CurrentProfileName = RegistrySettings.profileName ?? string.Empty;
            }
            catch (Exception ex)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), SaveAndReturnTitle, ex.Message);
                return;
            }

            ProfileApplied();
            RequestClose();
        }

        /// <summary>
        /// Saves the current <see cref="Settings"/> to a profile named <see cref="NewProfileName"/>,
        /// confirming an overwrite first when a profile of that name already exists, then notifies the
        /// host and requests the dialog close. Reproduces the WinForms <c>btnSaveAs_Click</c> flow with
        /// the <c>cboxOverWrite</c> confirmation merged in.
        /// </summary>
        /// <remarks>
        /// [XPLAT] <c>async void</c> is the standard pattern for a command handler that must
        /// <c>await</c> a UI interaction (here <see cref="ConfirmAsync"/>); the body is wrapped in
        /// try/catch so a file or settings error is surfaced through the presenter rather than escaping
        /// the void async boundary. The name is sanitized and trimmed exactly as the original
        /// (<c>SanitizeFileName(...).Trim()</c>); an empty result reproduces the WinForms "Enter a File
        /// Name To Save..." notice and leaves the dialog open.
        /// </remarks>
        private async void OnSave()
        {
            string name = SanitizeFileName(NewProfileName).Trim();
            if (name.Length == 0)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), SaveAndReturnTitle, EnterFileNameMessage);
                return;
            }

            try
            {
                // Confirm before clobbering an existing profile, matching the WinForms overwrite prompt
                // ("Overwrite: <name>.xml"). ConfirmAsync is captured locally before the await and
                // null-checked so a view that has not wired a prompt cannot fault here.
                string existingPath = ResolveProfileFilePath(name);
                if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                {
                    Func<string, Task<bool>> confirm = ConfirmAsync;
                    bool overwrite = confirm != null && await confirm("Overwrite: " + name + ProfileFileExtensionMatch);
                    if (!overwrite)
                    {
                        return;
                    }
                }

                // Make the new name the active profile, then write the current in-memory settings to it.
                // Settings.Save() composes "<profileName>.XML" itself (frozen schema), so no file content
                // is shaped here — the profile file IS the frozen settings XML.
                RegistrySettings.Save(RegKeys.profileName, name);
                Settings.Default.Save();
                CurrentProfileName = RegistrySettings.profileName ?? string.Empty;
                RefreshProfiles();
            }
            catch (Exception ex)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), SaveAndReturnTitle, ex.Message);
                return;
            }

            ProfileApplied();
            RequestClose();
        }

        /// <summary>
        /// Deletes <see cref="SelectedProfile"/> after a Yes/No confirmation, then clears the selection
        /// and refreshes the list while leaving the dialog open.
        /// </summary>
        /// <remarks>
        /// [XPLAT] <c>async void</c> command handler awaiting <see cref="ConfirmAsync"/> (the Yes/No
        /// <c>FormYes</c> prompt). The delete is guarded by <see cref="File.Exists(string)"/> so it is
        /// idempotent, the path is resolved cross-platform via <see cref="ResolveProfileFilePath"/>, and
        /// any I/O failure is reported through the presenter instead of throwing across the async-void
        /// boundary.
        /// </remarks>
        private async void OnDelete()
        {
            string profile = SelectedProfile;
            if (string.IsNullOrEmpty(profile))
            {
                return;
            }

            Func<string, Task<bool>> confirm = ConfirmAsync;
            bool confirmed = confirm != null && await confirm(DeleteConfirmMessage);
            if (!confirmed)
            {
                return;
            }

            try
            {
                string path = ResolveProfileFilePath(profile);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _errorPresenter?.PresentTimedMessage(TimeSpan.FromSeconds(2), SaveAndReturnTitle, ex.Message);
            }

            SelectedProfile = string.Empty;
            RefreshProfiles();
        }

        /// <summary>
        /// Requests the dialog close without changing the active profile. Parity with the WinForms
        /// <c>btnSerialCancel_Click</c> handler.
        /// </summary>
        private void OnClose()
        {
            RequestClose();
        }

        /// <summary>
        /// Resolves the on-disk path of a profile by display name. Prefers the canonical
        /// <c>&lt;name&gt;.XML</c> path written by <see cref="Settings"/>; if that exact file does not
        /// exist, it searches the directory case-insensitively so a legacy lowercase <c>.xml</c> file
        /// (carried over from a case-insensitive Windows install) can still be located on a
        /// case-sensitive filesystem. When nothing matches, the canonical path is returned so callers can
        /// test it with <see cref="File.Exists(string)"/>.
        /// </summary>
        /// <param name="name">The profile display name (without extension).</param>
        /// <returns>The resolved file path, or <see cref="string.Empty"/> when no profile directory exists.</returns>
        private static string ResolveProfileFilePath(string name)
        {
            string directory = RegistrySettings.profileDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                return string.Empty;
            }

            string preferred = Path.Combine(directory, name + ProfileFileExtension);
            if (File.Exists(preferred))
            {
                return preferred;
            }

            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase)
                        && Path.GetExtension(file).Equals(ProfileFileExtensionMatch, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }

            return preferred;
        }

        /// <summary>
        /// Removes characters that are illegal in a file name. Ported verbatim from the WinForms
        /// <c>FormProfiles.SanitizeFileName</c> so a typed profile name produces the same file name on
        /// every platform; a <c>null</c> or empty input yields <see cref="string.Empty"/>.
        /// </summary>
        /// <param name="fileName">The raw profile name typed by the operator.</param>
        /// <returns>The sanitized name with illegal characters stripped.</returns>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            return InvalidFileRegex.Replace(fileName, string.Empty);
        }
    }
}
