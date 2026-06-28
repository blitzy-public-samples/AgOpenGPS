// [XPLAT] migrated from net48/WinForms FormLoadProfile — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgLibrary.Logging;
using AgLibrary.Settings;
using AgOpenGPS;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using AgOpenGPS.Views;

namespace AgOpenGPS.Views.Profiles;

/// <summary>
/// [XPLAT] Avalonia code-behind for the "Load Environment" dialog — a 1:1 behavioural-parity
/// reimplementation of the WinForms <c>Forms/Profiles/FormLoadProfile</c>
/// (FormLoadProfile.cs + FormLoadProfile.Designer.cs). The dialog lists every environment profile
/// (the <c>*.xml</c> files under <see cref="RegistrySettings.environmentDirectory"/>) in a single
/// selectable list and lets the operator load the selected profile, delete it, or cancel.
/// </summary>
/// <remarks>
/// <para>
/// [XPLAT] The original form held a <c>FormGPS</c> back-reference and reached into it for the job-open
/// guard (<c>isJobStarted</c>), the timed-message presenter (<c>TimedMessageBox</c>) and the post-load
/// settings reload (<c>LoadSettings</c>). Per AAP §0.7.1 that god-object reference is removed: the three
/// collaborators are injected as plain delegates plus the existing Core <see cref="IErrorPresenter"/>, so
/// this view depends on no WinForms shell.
/// </para>
/// <para>
/// [XPLAT] The companion <c>FormLoadProfileView.axaml</c> declares no event handlers (it is a code-behind
/// dialog, not MVVM — no <c>x:DataType</c>, no bindings), so every control is addressed by <c>x:Name</c>
/// and the selection / Load / Delete / Cancel events are subscribed here. The dialog is shown modally with
/// <c>ShowDialog&lt;bool&gt;</c>: <c>Close(true)</c> mirrors the WinForms <c>DialogResult.OK</c> (Load) and
/// <c>Close(false)</c> the <c>DialogResult.Cancel</c> (Cancel).
/// </para>
/// <para>
/// [XPLAT] The profile load itself runs over the behaviour-frozen settings XML schema and
/// <see cref="Settings.Load()"/> (AAP §0.2.2): this is a UI re-host only — the underlying load/save logic
/// is unchanged.
/// </para>
/// </remarks>
public partial class FormLoadProfileView : Window
{
    // [XPLAT] Injected collaborators replacing the former FormGPS (mf) back-reference (AAP §0.7.1).
    private readonly Func<bool> _isJobStarted;         // was _formGPS.isJobStarted
    private readonly IErrorPresenter _errorPresenter;  // was _formGPS.TimedMessageBox(...)
    private readonly Action _reloadSettings;           // was _formGPS.LoadSettings()

    /// <summary>
    /// [XPLAT] Primary constructor. Replaces the WinForms <c>FormLoadProfile(FormGPS)</c> constructor: the
    /// three collaborators the original pulled from <c>FormGPS</c> are injected here.
    /// </summary>
    /// <param name="isJobStarted">
    /// Returns whether a field/job is currently open. Replaces <c>_formGPS.isJobStarted</c> and guards both
    /// Load and Delete exactly as the original did. A <see langword="null"/> argument is treated as
    /// "no job open".
    /// </param>
    /// <param name="errorPresenter">
    /// The shared Core timed-message presenter (the app's <c>AvaloniaErrorPresenter</c>). Replaces
    /// <c>_formGPS.TimedMessageBox(...)</c>.
    /// </param>
    /// <param name="reloadSettings">
    /// Re-applies settings into the running app after a successful load. Replaces
    /// <c>_formGPS.LoadSettings()</c>. A <see langword="null"/> argument is treated as a no-op.
    /// </param>
    public FormLoadProfileView(Func<bool> isJobStarted, IErrorPresenter errorPresenter, Action reloadSettings)
    {
        // [XPLAT] Coalesce the delegates to safe no-ops so the dialog never throws even when a collaborator
        // is omitted (e.g. the parameterless previewer path below).
        _isJobStarted = isJobStarted ?? (() => false);
        _errorPresenter = errorPresenter;
        _reloadSettings = reloadSettings ?? (() => { });

        InitializeComponent();

        // [XPLAT] The .axaml declares no handlers; subscribe imperatively, reproducing the WinForms Designer
        // event wiring (SelectedIndexChanged / buttonLoad.Click / buttonProfileDelete.Click / Cancel).
        listViewProfiles.SelectionChanged += ListViewProfiles_SelectionChanged;
        buttonProfileDelete.Click += ButtonProfileDelete_Click;
        buttonLoad.Click += ButtonLoad_Click;
        buttonCancel.Click += ButtonCancel_Click;
    }

    /// <summary>
    /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence raises
    /// AVLN3001, an error under the Release <c>TreatWarningsAsErrors</c> build) and used by the design-time
    /// previewer. It forwards to the primary constructor with no-op collaborators.
    /// </summary>
    public FormLoadProfileView()
        : this(null, null, null)
    {
    }

    /// <summary>
    /// [XPLAT] Parity with <c>FormLoadProfile_Load</c>: assigns the literal window title and label, the
    /// translated button captions, and fills the profile list. Avalonia's <see cref="Window.OnOpened"/> is
    /// the equivalent of the WinForms <c>Form.Load</c> event.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] Literal strings (not gStr), exactly as FormLoadProfile_Load set them.
        Title = "Load Environment";
        labelLoadProfile.Text = "Load Environment:";

        // [XPLAT] Translated captions live on the inner TextBlocks of the image buttons (the .axaml
        // contract), mirroring the WinForms button.Text assignments.
        textProfileDelete.Text = gStr.gsDelete;
        textLoad.Text = gStr.gsLoad;
        textCancel.Text = gStr.gsCancel;

        PopulateProfiles();
    }

    /// <summary>
    /// [XPLAT] Fills the list with the environment profile names and clears the selection — the Avalonia
    /// equivalent of the WinForms <c>listViewProfiles.Items.Clear()/AddRange(...)/SelectedItems.Clear()</c>
    /// sequence used by both <c>FormLoadProfile_Load</c> and <c>buttonProfileDelete_Click</c>. Clearing the
    /// selection disables Load/Delete via <see cref="ListViewProfiles_SelectionChanged"/>, exactly as the
    /// WinForms <c>SelectedIndexChanged</c> handler did.
    /// </summary>
    private void PopulateProfiles()
    {
        listViewProfiles.ItemsSource = LoadProfiles().ToList();
        listViewProfiles.SelectedItem = null;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>FormLoadProfile.LoadProfiles()</c>: enumerates the <c>*.xml</c> files under
    /// <see cref="RegistrySettings.environmentDirectory"/> and returns their base names. The directory,
    /// search pattern, and <see cref="Path.GetFileNameWithoutExtension(string)"/> projection are preserved
    /// verbatim (no sorting — the original returned them in directory order).
    /// </summary>
    /// <returns>The environment profile names, or an empty sequence when the directory is absent.</returns>
    private static IEnumerable<string> LoadProfiles()
    {
        if (!Directory.Exists(RegistrySettings.environmentDirectory))
            return Enumerable.Empty<string>();

        DirectoryInfo directory = new DirectoryInfo(RegistrySettings.environmentDirectory);
        FileInfo[] files = directory.GetFiles("*.xml");
        return files.Select(file => Path.GetFileNameWithoutExtension(file.Name));
    }

    /// <summary>
    /// [XPLAT] Parity with <c>listViewProfiles_SelectedIndexChanged</c>: Load and Delete are enabled only
    /// while a profile is selected.
    /// </summary>
    private void ListViewProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool profileSelected = listViewProfiles.SelectedItem != null;
        buttonLoad.IsEnabled = profileSelected;
        buttonProfileDelete.IsEnabled = profileSelected;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>buttonProfileDelete_Click</c>: deletes the selected profile's <c>*.xml</c>
    /// after a confirmation, but refuses to delete the environment currently in use. This handler never
    /// closes the dialog (the WinForms Delete button had no <c>DialogResult</c>); it refreshes the list
    /// afterwards. <c>async void</c> is the correct shape for a UI event handler.
    /// </summary>
    private async void ButtonProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_isJobStarted())
            return;

        if (listViewProfiles.SelectedItem == null)
            return;

        string profileName = (string)listViewProfiles.SelectedItem;
        if (RegistrySettings.environmentFileName != profileName)
        {
            bool ok = await FormDialogView.ShowQuestionAsync(
                gStr.gsSaveAndReturn,
                $"Delete {profileName}.xml ?",
                owner: this);

            if (ok)
            {
                // [XPLAT] Path.Combine for cross-platform path construction (AAP §0.6.5).
                File.Delete(Path.Combine(RegistrySettings.environmentDirectory, profileName + ".xml"));
            }
        }
        else
        {
            // [XPLAT] Literal English strings preserved verbatim from the WinForms source.
            await FormDialogView.ShowAsync(
                "Environment currently in use",
                "Select different environment",
                DialogSeverity.Error,
                owner: this);
        }

        PopulateProfiles();
    }

    /// <summary>
    /// [XPLAT] Parity with <c>buttonOK_Click</c> (the Load button). When no job is open and a profile is
    /// selected, the user confirms and the profile is loaded; when a job is open, a timed message is shown
    /// instead. The WinForms Load button carried <c>DialogResult.OK</c> and the handler never reset it, so
    /// the modal always closed once the handler finished — reproduced here by falling through to a single
    /// <c>Close(true)</c> on every path (the WinForms early <c>return</c>s become nested <c>if</c>s).
    /// </summary>
    private async void ButtonLoad_Click(object sender, RoutedEventArgs e)
    {
        if (!_isJobStarted())
        {
            // [XPLAT] The button is enabled only when a row is selected; this guard preserves the original
            // SelectedItems.Count check.
            if (listViewProfiles.SelectedItem != null)
            {
                string profileName = (string)listViewProfiles.SelectedItem;
                bool ok = await FormDialogView.ShowQuestionAsync(
                    gStr.gsSaveAndReturn,
                    $"Load {profileName}.xml ?",
                    owner: this);

                if (ok)
                {
                    await LoadProfile(profileName);
                }
            }
        }
        else
        {
            // [XPLAT] was _formGPS.TimedMessageBox(2000, gStr.gsFieldIsOpen, gStr.gsCloseFieldFirst).
            _errorPresenter?.PresentTimedMessage(
                TimeSpan.FromMilliseconds(2000),
                gStr.gsFieldIsOpen,
                gStr.gsCloseFieldFirst);
        }

        // [XPLAT] Mirrors the WinForms DialogResult.OK auto-close: runs on every path above.
        Close(true);
    }

    /// <summary>
    /// [XPLAT] Parity with <c>FormLoadProfile.LoadProfile(string)</c>: persists the chosen environment name,
    /// reloads the (behaviour-frozen) settings XML, and reports the outcome. On a load failure the error is
    /// logged and surfaced and the method returns without reloading the app. The method is <c>async Task</c>
    /// because the failure dialog is awaited; the underlying
    /// <see cref="RegistrySettings.Save(string, string)"/> + <see cref="Settings.Load()"/> contract is
    /// unchanged (AAP §0.2.2).
    /// </summary>
    /// <param name="profileName">The environment profile (file base name) to load.</param>
    private async Task LoadProfile(string profileName)
    {
        // [XPLAT] RegKeys.environmentFileName is a const string; Save takes (string name, string value).
        RegistrySettings.Save(RegKeys.environmentFileName, profileName);

        // [XPLAT] Fully-qualify the settings type: within the AgOpenGPS.Views.Profiles namespace the bare
        // name "Settings" binds to the sibling namespace AgOpenGPS.Views.Settings (reachable via the
        // enclosing AgOpenGPS.Views), which would shadow the AgOpenGPS.Properties.Settings TYPE. The
        // global:: qualifier resolves unambiguously to the settings type (same disambiguation the WinForms
        // designer used for global::AgOpenGPS.Properties.* members). The behaviour-frozen Load() contract
        // (AAP §0.2.2) is unchanged.
        LoadResult result = global::AgOpenGPS.Properties.Settings.Default.Load();
        if (result != LoadResult.Ok)
        {
            Log.EventWriter($"Error loading environment profile {profileName}.xml ({result})");

            await FormDialogView.ShowAsync(
                gStr.gsError,
                $"Error loading environment profile {profileName}.xml\n\nResult: {result}",
                DialogSeverity.Error,
                owner: this);
            return;
        }

        Log.EventWriter($"Environment profile loaded: {profileName}.xml");

        // [XPLAT] was _formGPS.LoadSettings().
        _reloadSettings();

        // [XPLAT] was _formGPS.TimedMessageBox(2500, ...).
        _errorPresenter?.PresentTimedMessage(
            TimeSpan.FromMilliseconds(2500),
            $"Profile '{profileName}' loaded",
            "Environment settings loaded!");
    }

    /// <summary>
    /// [XPLAT] Parity with the WinForms Cancel button (<c>DialogResult.Cancel</c>): closes the dialog with a
    /// negative result.
    /// </summary>
    private void ButtonCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
