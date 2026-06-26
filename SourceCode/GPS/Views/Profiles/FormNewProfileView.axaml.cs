// [XPLAT] migrated from net48/WinForms FormNewProfile — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using AgOpenGPS.Views;

namespace AgOpenGPS.Views.Profiles;

/// <summary>
/// [XPLAT] Avalonia code-behind for the "Create New Profile" dialog — a 1:1 behavioural-parity
/// reimplementation of the WinForms <c>Forms/Profiles/FormNewProfile</c> (FormNewProfile.cs +
/// FormNewProfile.Designer.cs). The dialog prompts for a new environment-profile name and lets the
/// operator choose what to copy the new profile FROM: a blank <c>&lt;Empty Profile&gt;</c> reset, the
/// current profile, or any existing <c>*.xml</c> profile in the environment directory.
/// </summary>
/// <remarks>
/// <para>
/// The deep profile creation operates over the behaviour-frozen settings XML schema
/// (<c>Settings.Default</c> + <c>XmlSettingsHandler.SaveXMLFile</c>, AAP §0.2.2): only the framework
/// underneath the dialog changes, never the persisted result. Every branch of the original
/// <c>buttonOK_Click</c> is reproduced verbatim (empty reset / from-current / from-existing), including
/// the literal warning/overwrite prompts.
/// </para>
/// <para>
/// This is an imperative, code-behind dialog (no view-model, no data binding except the list's
/// reflection-bound <see cref="ProfileItem"/> rows). The WinForms <c>FormGPS mf</c> god-object
/// back-reference is replaced by three constructor-injected collaborators (AAP §0.7.1): a job-state
/// probe, the Core <see cref="IErrorPresenter"/> (replacing the inline <c>FormTimedMessage</c> /
/// <c>FormGPS.TimedMessageBox</c> calls), and a settings-reload callback (replacing
/// <c>FormGPS.LoadSettings()</c>). The on-screen keyboard is the sibling
/// <see cref="AgOpenGPS.Views.FormKeyboard"/>, gated on <c>Settings.Default.setDisplay_isKeyboardOn</c>.
/// </para>
/// <para>
/// Close semantics mirror the WinForms designer exactly: <c>buttonCreate</c> carried
/// <c>DialogResult.OK</c> and the handler never reset it, so the modal always closed after the handler
/// ran (even on the original early <c>return</c>s). That is reproduced by routing every
/// <see cref="ButtonCreate_Click"/> path to a single trailing <c>Close(true)</c>;
/// <see cref="ButtonCancel_Click"/> closes with <c>false</c> (WinForms <c>DialogResult.Cancel</c>). The
/// dialog is shown via <c>await new FormNewProfileView(...).ShowDialog&lt;bool&gt;(owner)</c>.
/// </para>
/// </remarks>
public partial class FormNewProfileView : Window
{
    /// <summary>
    /// [XPLAT] Sentinel "copy from" row identity for "start from a blank/empty environment profile".
    /// Parity with WinForms <c>$"&lt;{gStr.gsEmptyProfile}&gt;"</c>.
    /// </summary>
    private static readonly string EmptyProfile = $"<{gStr.gsEmptyProfile}>";

    /// <summary>
    /// [XPLAT] Filename-sanitiser used by <see cref="SanitizeFileName"/> — strips the characters that are
    /// invalid in a file name. Preserved verbatim from the WinForms source and intentionally DISTINCT from
    /// <c>glm.fileRegex</c> (which performs the live keystroke filtering in
    /// <see cref="TextBoxName_TextChanged"/>). Both regexes are required.
    /// </summary>
    private static readonly Regex InvalidFileRegex = new Regex(string.Format("[{0}]", Regex.Escape(@"<>:""/\|?*")));

    // [XPLAT] Constructor-injected collaborators replacing the WinForms `private readonly FormGPS mf;`
    // back-reference (AAP §0.7.1 — no FormGPS dependency).
    private readonly Func<bool> _isJobStarted;
    private readonly IErrorPresenter _errorPresenter;
    private readonly Action _reloadSettings;

    /// <summary>
    /// [XPLAT] One "copy from" list row. The XAML <c>ItemTemplate</c> reflection-binds
    /// <c>{Binding Display}</c> and <c>{Binding CurrentMarker}</c> (the view sets
    /// <c>x:CompileBindings="False"</c> and has no <c>x:DataType</c>), so these property names are part of
    /// the view contract. <see cref="Name"/> is the identity key (the <see cref="EmptyProfile"/> sentinel
    /// or a profile filename-stem) used by the create logic and the current-profile preselect.
    /// </summary>
    private sealed class ProfileItem
    {
        /// <summary>Identity key: the <see cref="EmptyProfile"/> sentinel or a profile filename-stem.</summary>
        public string Name { get; init; }

        /// <summary>Column 1 display text (the profile name shown in the list).</summary>
        public string Display { get; init; }

        /// <summary>Column 2 marker: <c>"({gStr.gsCurrent})"</c> for the active profile, otherwise empty.</summary>
        public string CurrentMarker { get; set; } = "";
    }

    /// <summary>
    /// [XPLAT] Primary constructor. Stores the injected collaborators and loads the XAML. The
    /// <c>FormNewProfile_Load</c> work runs from <see cref="OnOpened"/> (the Avalonia equivalent of the
    /// WinForms <c>Load</c> event).
    /// </summary>
    /// <param name="isJobStarted">Probe for <c>FormGPS.isJobStarted</c> — true while a field/job is open.</param>
    /// <param name="errorPresenter">Core presenter for the transient timed messages (replaces
    /// <c>FormTimedMessage</c> / <c>FormGPS.TimedMessageBox</c>).</param>
    /// <param name="reloadSettings">Callback that reloads the running application's settings (replaces
    /// <c>FormGPS.LoadSettings()</c>).</param>
    public FormNewProfileView(Func<bool> isJobStarted, IErrorPresenter errorPresenter, Action reloadSettings)
    {
        _isJobStarted = isJobStarted;
        _errorPresenter = errorPresenter;
        _reloadSettings = reloadSettings;

        InitializeComponent();
    }

    /// <summary>
    /// [XPLAT] Parameterless constructor for the Avalonia XAML previewer/designer only. It chains to the
    /// primary constructor with no-op collaborators so design-time instantiation never touches host state.
    /// It is never used at runtime — the application always constructs the dialog with real collaborators.
    /// </summary>
    public FormNewProfileView()
        : this(() => false, null, () => { })
    {
    }

    /// <summary>
    /// [XPLAT] WinForms <c>FormNewProfile_Load</c> → Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
    /// applies the translated captions, builds the "copy from" list (empty sentinel + existing profiles),
    /// and preselects the current profile (marking it <c>"(current)"</c>) or, when none matches, the first
    /// row.
    /// </summary>
    /// <param name="e">The event data passed to the base implementation.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] WinForms `this.Text` → Avalonia Window.Title; the caption labels and button captions are
        // assigned from the translation table exactly as in FormNewProfile_Load.
        Title = gStr.gsCreateNewProfile;
        labelName.Text = gStr.gsName + ":";
        labelCopyFrom.Text = gStr.gsCopyFrom + ":";
        textCreate.Text = gStr.gsCreate;
        textCancel.Text = gStr.gsCancel;

        // [XPLAT] WinForms built ListViewItems (empty sentinel first, then LoadProfiles()); here the same
        // rows are built as ProfileItem objects bound by the list's ItemTemplate.
        List<ProfileItem> items = new List<ProfileItem>
        {
            new ProfileItem { Name = EmptyProfile, Display = EmptyProfile }
        };
        items.AddRange(LoadProfiles().Select(profile => new ProfileItem { Name = profile, Display = profile }));

        // [XPLAT] Mirror the WinForms `Items[environmentFileName]` keyed lookup + fallback. The current
        // marker is set BEFORE assigning ItemsSource so the reflection-bound column renders it immediately
        // (a plain CLR property is read once at bind time); selection is applied after assignment.
        ProfileItem currentProfile = items.FirstOrDefault(item => item.Name == RegistrySettings.environmentFileName);
        if (currentProfile != null)
        {
            currentProfile.CurrentMarker = $"({gStr.gsCurrent})";
        }

        listViewProfiles.ItemsSource = items;

        if (currentProfile != null)
        {
            listViewProfiles.SelectedItem = currentProfile;
        }
        else
        {
            // Fallback for the initial setup, when no profiles exist yet: select the empty sentinel row.
            listViewProfiles.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// [XPLAT] Parity with WinForms <c>LoadProfiles()</c> (identical to FormLoadProfile): enumerate the
    /// <c>*.xml</c> profiles under the environment directory, returning their filename-stems.
    /// </summary>
    /// <returns>The profile names (filename-stems), or an empty sequence when the directory is absent.</returns>
    private static IEnumerable<string> LoadProfiles()
    {
        if (!Directory.Exists(RegistrySettings.environmentDirectory))
        {
            return Enumerable.Empty<string>();
        }

        DirectoryInfo directory = new DirectoryInfo(RegistrySettings.environmentDirectory);
        FileInfo[] files = directory.GetFiles("*.xml");
        return files.Select(file => Path.GetFileNameWithoutExtension(file.Name));
    }

    /// <summary>
    /// [XPLAT] WinForms <c>textBoxName_Click</c> (wired to <c>Tapped</c> in the Avalonia view). When no job
    /// is open and the on-screen keyboard is enabled, edits the name via the sibling
    /// <see cref="FormKeyboard"/> modal (a non-null result is written back into the field). When a job is
    /// open, the injected presenter shows the "field is open" timed message and the field is disabled —
    /// exactly as the original popped <c>FormTimedMessage</c> and set <c>textBoxName.Enabled = false</c>.
    /// </summary>
    /// <param name="sender">The text box raising the tap (unused).</param>
    /// <param name="e">The tap event data (unused).</param>
    private async void TextBoxName_Tapped(object sender, TappedEventArgs e)
    {
        if (!_isJobStarted())
        {
            if (Properties.Settings.Default.setDisplay_isKeyboardOn)
            {
                // [XPLAT] WinForms `((TextBox)sender).ShowKeyboard(_formGPS)` → the FormKeyboard modal.
                // ShowDialog<string> returns the entered string (OK) or null (cancel); the nullable
                // annotation is dropped because the project compiles with nullable reference types disabled.
                FormKeyboard keyboard = new FormKeyboard(textBoxName.Text ?? string.Empty);
                string result = await keyboard.ShowDialog<string>(this);
                if (result != null)
                {
                    textBoxName.Text = result;
                }
            }
        }
        else
        {
            _errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2000), gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst);
            textBoxName.IsEnabled = false;
        }
    }

    /// <summary>
    /// [XPLAT] WinForms <c>textBoxName_TextChanged</c>: live-filter the name through <c>glm.fileRegex</c>
    /// (preserving the caret) and enable Create only for a non-empty name. The WinForms
    /// <c>SelectionStart</c> caret model maps to Avalonia's <see cref="TextBox.CaretIndex"/> (Avalonia
    /// clamps it into <c>[0, Text.Length]</c>); the idempotent regex bounds any re-entrancy from
    /// re-assigning <c>Text</c>.
    /// </summary>
    /// <param name="sender">The text box raising the change (unused).</param>
    /// <param name="e">The text-change event data (unused).</param>
    private void TextBoxName_TextChanged(object sender, TextChangedEventArgs e)
    {
        int caret = textBoxName.CaretIndex;
        textBoxName.Text = Regex.Replace(textBoxName.Text ?? string.Empty, glm.fileRegex, "");
        textBoxName.CaretIndex = caret;

        buttonCreate.IsEnabled = !string.IsNullOrEmpty(textBoxName.Text);
    }

    /// <summary>
    /// [XPLAT] WinForms <c>buttonOK_Click</c> (<c>DialogResult.OK</c>). Sanitises the name, optionally
    /// confirms an overwrite, then runs the matching create branch (empty reset / from-current /
    /// from-existing). Mirroring the designer's <c>DialogResult.OK</c> auto-close, EVERY path falls through
    /// to a single trailing <see cref="Window.Close(object)"/> with <c>true</c> (the original early
    /// <c>return</c>s are reshaped into nested guards so the dialog still always closes).
    /// </summary>
    /// <param name="sender">The Create button (unused).</param>
    /// <param name="e">The click event data (unused).</param>
    private async void ButtonCreate_Click(object sender, RoutedEventArgs e)
    {
        string newProfileName = SanitizeFileName((textBoxName.Text ?? string.Empty).Trim()).Trim();
        if (!string.IsNullOrEmpty(newProfileName))
        {
            string newProfilePath = Path.Combine(RegistrySettings.environmentDirectory, newProfileName + ".xml");

            bool proceed = true;
            if (File.Exists(newProfilePath))
            {
                proceed = await FormDialogView.ShowQuestionAsync(
                    gStr.gsSaveAndReturn,
                    $"Profile '{newProfileName}' already exists.\r\n\r\nOverwrite?",
                    owner: this);
            }

            if (proceed && listViewProfiles.SelectedItem is ProfileItem sel)
            {
                string existingProfileName = sel.Name;

                if (existingProfileName.Equals(EmptyProfile))
                {
                    bool confirmReset = await FormDialogView.ShowQuestionAsync(
                        "!! WARNING !!",
                        "This will reset all Environment settings (display, sounds, window positions). Are you Sure?",
                        DialogSeverity.Warning,
                        owner: this);

                    if (confirmReset)
                    {
                        CreateNewEmptyProfile(newProfileName);
                    }
                }
                else if (existingProfileName.Equals(RegistrySettings.environmentFileName))
                {
                    CreateNewProfileFromCurrent(newProfileName);
                }
                else
                {
                    await CreateNewProfileFromExisting(newProfileName, existingProfileName);
                }
            }
        }

        // [XPLAT] WinForms buttonCreate.DialogResult == OK closed the modal after the handler on EVERY path
        // (including the original early returns). Reproduce that single auto-close here.
        Close(true);
    }

    /// <summary>
    /// [XPLAT] Parity with WinForms <c>CreateNewEmptyProfile</c>: point the registry at the new name, reset
    /// the settings to defaults, persist the fresh XML immediately, reload the application, and report the
    /// reset via the injected presenter.
    /// </summary>
    /// <param name="profileName">The sanitised new profile name.</param>
    private void CreateNewEmptyProfile(string profileName)
    {
        string profilePath = Path.Combine(RegistrySettings.environmentDirectory, profileName + ".xml");

        RegistrySettings.Save(RegKeys.environmentFileName, profileName);

        Properties.Settings.Default.Reset();

        // Save the XML file immediately so it exists on disk.
        XmlSettingsHandler.SaveXMLFile(profilePath, Properties.Settings.Default);

        Log.EventWriter($"New environment profile created: {profileName}.xml");

        _reloadSettings();

        _errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2500), $"New profile '{profileName}' created", "Environment settings reset!");
    }

    /// <summary>
    /// [XPLAT] Parity with WinForms <c>CreateNewProfileFromCurrent</c>: point the registry at the new name
    /// and save the currently loaded settings under it.
    /// </summary>
    /// <param name="profileName">The sanitised new profile name.</param>
    private void CreateNewProfileFromCurrent(string profileName)
    {
        RegistrySettings.Save(RegKeys.environmentFileName, profileName);

        Properties.Settings.Default.Save();

        Log.EventWriter($"Environment profile saved as: {profileName}.xml");
    }

    /// <summary>
    /// [XPLAT] Parity with WinForms <c>CreateNewProfileFromExisting</c>: temporarily switch to the chosen
    /// existing profile to load its settings, then (on success) reload the application and re-save those
    /// settings under the new name. On a load failure the previous environment is restored and the injected
    /// dialog reports the error. The WinForms <c>void</c> handler becomes <c>async Task</c> so the awaited
    /// error dialog is sequenced correctly.
    /// </summary>
    /// <param name="profileName">The sanitised new profile name.</param>
    /// <param name="existingProfileName">The existing profile to copy settings from.</param>
    private async Task CreateNewProfileFromExisting(string profileName, string existingProfileName)
    {
        // Temporarily switch to existing profile to load its settings.
        string previousEnv = RegistrySettings.environmentFileName;
        RegistrySettings.Save(RegKeys.environmentFileName, existingProfileName);

        LoadResult result = Properties.Settings.Default.Load();
        if (result != LoadResult.Ok)
        {
            Log.EventWriter($"Error loading environment profile {existingProfileName}.xml ({result})");

            await FormDialogView.ShowAsync(
                gStr.gsError,
                "Error loading profile " + existingProfileName + ".xml\n\nResult: " + result,
                DialogSeverity.Error,
                owner: this);

            // Restore previous environment file name.
            RegistrySettings.Save(RegKeys.environmentFileName, previousEnv);
            return;
        }

        Log.EventWriter($"Environment profile loaded: {existingProfileName}.xml");

        _reloadSettings();

        // Save as the new profile name.
        RegistrySettings.Save(RegKeys.environmentFileName, profileName);
        Properties.Settings.Default.Save();

        _errorPresenter.PresentTimedMessage(TimeSpan.FromMilliseconds(2500), $"New profile '{profileName}' created", "Environment settings loaded!");
    }

    /// <summary>
    /// [XPLAT] Parity with WinForms <c>SanitizeFileName</c>: strip the characters invalid in a file name via
    /// <see cref="InvalidFileRegex"/>.
    /// </summary>
    /// <param name="fileName">The raw name entered by the operator.</param>
    /// <returns>The name with invalid file-name characters removed.</returns>
    private static string SanitizeFileName(string fileName)
    {
        return InvalidFileRegex.Replace(fileName, string.Empty);
    }

    /// <summary>
    /// [XPLAT] WinForms <c>buttonCancel</c> (<c>DialogResult.Cancel</c>): dismiss the dialog with a negative
    /// result.
    /// </summary>
    /// <param name="sender">The Cancel button (unused).</param>
    /// <param name="e">The click event data (unused).</param>
    private void ButtonCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
