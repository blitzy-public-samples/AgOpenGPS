// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Code-behind for the AgShare server / API-key settings dialog. This is a 1:1 behavioural-parity
// reimplementation of the WinForms Forms/FormAgShareSettings (FormAgShareSettings.cs +
// FormAgShareSettings.Designer.cs) — the behaviour is unchanged, only the framework underneath it
// (AAP §0.3.3, §0.7.1). Every WinForms handler is reproduced: load, async test-connection
// (+ ConvertError), save, the three image toggle buttons and their Update* glyph methods, clipboard
// paste, the register hyperlink, the on-screen-keyboard server tap, the hidden developer toggle, and
// the 500 ms clipboard poll timer + its disposal.
//
// Framework conversions (each tagged // [XPLAT] at its call site):
//   * System.Windows.Forms.Form               -> Avalonia.Controls.Window
//   * System.Drawing.Color (status foreground) -> Avalonia.Media.Brushes (Gray/Green/Red/Blue)
//   * System.Windows.Forms.Clipboard (sync)    -> TopLevel.Clipboard (Avalonia IClipboard, async)
//   * System.Windows.Forms.Timer (500 ms)      -> Avalonia.Threading.DispatcherTimer
//   * LinkLabel (linkRegister)                 -> hyperlink-style Button (markup) + Process.Start here
//   * Button.Image swaps (Properties.Resources)-> Image.Source via AssetLoader + avares:// glyphs
//   * the mf/Owner-as-FormGPS keyboard coupling -> a setting check + the migrated modal
//                                                 Views/Inputs/FormKeyboard (no FormGPS reference)
//   * DialogResult.OK / DialogResult.Cancel    -> Close(true) / Close(false)
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] AgShare settings dialog: edits the AgShare server URL and API key, runs a live connection
/// test through the injected <see cref="AgShareClient"/>, toggles the upload / auto-upload / auto-load
/// state, pastes the key from the clipboard, opens the registration page, and persists to
/// <see cref="Settings"/>. A 1:1 behavioural-parity port of the WinForms
/// <c>Forms/FormAgShareSettings</c>; see the file header for the framework-conversion notes.
/// </summary>
public partial class FormAgShareSettingsView : Window
{
    /// <summary>
    /// [XPLAT] The sign-up page opened by the register hyperlink (parity with the WinForms
    /// <c>linkRegister_LinkClicked</c> URL, which already used <c>UseShellExecute = true</c>).
    /// </summary>
    private const string RegisterUrl = "https://agshare.agopengps.com/register";

    /// <summary>
    /// [XPLAT] The AgShare API client this dialog configures. Constructor-injected — exactly as the
    /// WinForms <c>FormAgShareSettings(AgShareClient)</c> received it — so this view never reaches back
    /// to a <c>FormGPS</c> god-object (AAP §0.6.1).
    /// </summary>
    private readonly AgShareClient _agShareClient;

    /// <summary>
    /// [XPLAT] Replaces the WinForms <c>System.Windows.Forms.Timer clipboardCheckTimer</c>: a 500 ms
    /// poll that enables the Paste button only while the clipboard holds text. Created in
    /// <see cref="OnOpened"/> and torn down in <see cref="OnClosed"/>.
    /// </summary>
    private DispatcherTimer _clipboardCheckTimer;

    /// <summary>
    /// Initializes the dialog with the AgShare client it configures. Parity with the WinForms
    /// <c>FormAgShareSettings(AgShareClient agShareClient)</c> constructor — the client is stored, the
    /// component tree is loaded, and the control event handlers are wired (mirroring the handlers the
    /// WinForms designer attached in <c>InitializeComponent</c>).
    /// </summary>
    /// <param name="agShareClient">The AgShare client whose server URL / API key this dialog edits.</param>
    public FormAgShareSettingsView(AgShareClient agShareClient)
    {
        _agShareClient = agShareClient;
        InitializeComponent();

        // [XPLAT] Designer-wired Click handlers (parity with FormAgShareSettings.Designer.cs, where each
        // of these was attached inside InitializeComponent). buttonCancel had no WinForms handler — it
        // carried DialogResult.Cancel — so it is wired here to Close(false) for the same dismissal.
        buttonTestConnection.Click += buttonTestConnection_Click;
        btnPaste.Click += btnPaste_Click;
        btnToggleUpload.Click += btnToggleUpload_Click;
        btnAutoUpload.Click += btnAutoUpload_Click;
        btnAutoLoad.Click += btnAutoLoad_Click;
        btnDevelop.Click += btnDevelop_Click;
        linkRegister.Click += linkRegister_LinkClicked;
        buttonCancel.Click += buttonCancel_Click;
        buttonSave.Click += buttonSave_Click;

        // [XPLAT] WinForms `textBoxServer.Click += textBoxServer_Click`. Avalonia TextBox has no Click;
        // the equivalent pointer gesture is the Tapped routed event, attached with AddHandler exactly as
        // the sibling migrated views do (e.g. FormFieldKMLView).
        textBoxServer.AddHandler(Gestures.TappedEvent, textBoxServer_Click);
    }

    /// <summary>
    /// [XPLAT] WinForms <c>FormAgShareSettings_Load</c> -> Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
    /// translate every caption, seed the server/key fields, paint the three toggle glyphs, and start the
    /// clipboard poll. The <c>TextChanged</c> handlers are subscribed only after the fields are seeded so
    /// the initial seed does not pre-enable Save (the exact order of the WinForms Load handler).
    /// </summary>
    /// <param name="e">The event data forwarded to the base implementation.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Translations (parity with the WinForms Load handler caption block).
        Title = gStr.gsAgShareSettings;
        labelApiKey.Text = gStr.gsAgShareApiKey + ":";
        label1.Text = gStr.gsAgShareServer + ":";
        buttonTestConnection.Content = gStr.gsAgShareTestConnection;
        btnPaste.Content = gStr.gsAgSharePaste;
        label2.Text = gStr.gsAgShareRegisterHere;
        SetCancelCaption(gStr.gsCancel);
        labelStatus.Text = gStr.gsAgShareEnterDetails;
        labelButtonsHint.Text = gStr.gsAgShareButtonsHint;

        // Seed the editable fields from settings BEFORE wiring TextChanged (see method summary).
        textBoxServer.Text = Properties.Settings.Default.AgShareServer;
        textBoxApiKey.Text = Properties.Settings.Default.AgShareApiKey;

        UpdateAgShareToggleButton();
        UpdateAgShareUploadButton();
        UpdateAgShareAutoLoadButton();

        // [XPLAT] WinForms `btnPaste.Enabled = Clipboard.ContainsText()` + a 500 ms Timer. The Avalonia
        // clipboard read is async, so the initial enablement is a fire-and-forget poll and the timer tick
        // re-runs the same poll.
        _clipboardCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clipboardCheckTimer.Tick += ClipboardCheckTimer_Tick;
        _clipboardCheckTimer.Start();
        _ = UpdatePasteEnabledAsync();

        // Event handlers to enable Save on text change (subscribed last, matching the WinForms Load order).
        textBoxApiKey.TextChanged += textBoxAnySetting_TextChanged;
        textBoxServer.TextChanged += textBoxAnySetting_TextChanged;

        // Parity: the WinForms source leaves the server field enabled.
        textBoxServer.IsEnabled = true;
    }

    /// <summary>
    /// [XPLAT] WinForms <c>OnFormClosed</c> -> Avalonia <see cref="Window.OnClosed(EventArgs)"/>: stop the
    /// clipboard poll. <see cref="DispatcherTimer"/> has no <c>Dispose</c>, so stopping it and dropping the
    /// reference is the cross-platform equivalent of the WinForms <c>Stop()</c> + <c>Dispose()</c>.
    /// </summary>
    /// <param name="e">The event data forwarded to the base implementation.</param>
    protected override void OnClosed(EventArgs e)
    {
        if (_clipboardCheckTimer != null)
        {
            _clipboardCheckTimer.Stop();
            _clipboardCheckTimer.Tick -= ClipboardCheckTimer_Tick;
            _clipboardCheckTimer = null;
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// [XPLAT] Parity with <c>ClipboardCheckTimer_Tick</c>: re-evaluate Paste enablement each interval.
    /// The actual (async) clipboard read is delegated to <see cref="UpdatePasteEnabledAsync"/> and
    /// fire-and-forgotten, keeping the synchronous <see cref="DispatcherTimer"/> tick signature.
    /// </summary>
    /// <param name="sender">The timer raising the tick (unused).</param>
    /// <param name="e">The tick event data (unused).</param>
    private void ClipboardCheckTimer_Tick(object sender, EventArgs e)
    {
        _ = UpdatePasteEnabledAsync();
    }

    /// <summary>
    /// [XPLAT] Parity with the async <c>buttonTestConnection_Click</c>: probe the server/key via the
    /// static <see cref="AgShareClient.CheckApiAsync(string, string)"/> and reflect the result in the
    /// status label (Gray while connecting, Green + Save-enabled on success, Red with the converted error
    /// on failure).
    /// </summary>
    /// <param name="sender">The Test Connection button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private async void buttonTestConnection_Click(object sender, RoutedEventArgs e)
    {
        labelStatus.Text = gStr.gsAgShareConnecting;
        labelStatus.Foreground = Brushes.Gray;

        var baseUrl = textBoxServer.Text;
        var apiKey = textBoxApiKey.Text;

        var result = await AgShareClient.CheckApiAsync(baseUrl, apiKey);

        if (result.IsSuccessful)
        {
            labelStatus.Text = "\u2714 " + gStr.gsAgShareConnectionOk;
            labelStatus.Foreground = Brushes.Green;
            buttonSave.IsEnabled = true;
        }
        else
        {
            string error = ConvertError(result.Error);
            labelStatus.Text = "\u274C " + error;
            labelStatus.Foreground = Brushes.Red;
        }
    }

    /// <summary>
    /// [XPLAT] Parity with <c>ConvertError</c>: map an <see cref="AgShareError"/> to a display string.
    /// The switch is identical to the WinForms original — an unknown error subtype (including
    /// <see cref="JsonError"/>) falls through to the default and throws, preserving the fail-fast contract.
    /// </summary>
    /// <param name="error">The error returned by the connection check.</param>
    /// <returns>A human-readable message describing the error.</returns>
    private string ConvertError(AgShareError error)
    {
        switch (error)
        {
            case InvalidApiKeyError _:
                return gStr.gsAgShareInvalidApiKey;
            case StatusCodeError statusCodeError:
                return $"Status {statusCodeError.StatusCode}: {statusCodeError.Body}";
            case HttpRequestError httpRequestError:
                return $"Error: {httpRequestError.Exception.Message}";
            default:
                throw new InvalidOperationException($"Unknown {nameof(AgShareError)}: {error.GetType()}");
        }
    }

    /// <summary>
    /// [XPLAT] Parity with <c>buttonSave_Click</c>: push the server/key into the live
    /// <see cref="AgShareClient"/>, persist them to <see cref="Settings"/>, and confirm with the Blue
    /// "Saved" status. The WinForms button carried <c>DialogResult.OK</c> (and the form had
    /// <c>ControlBox = false</c>, so the buttons were the only exits), so the dialog then closes with a
    /// <see langword="true"/> result.
    /// </summary>
    /// <param name="sender">The Save button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void buttonSave_Click(object sender, RoutedEventArgs e)
    {
        _agShareClient.UpdateSettings(textBoxServer.Text, textBoxApiKey.Text);

        Properties.Settings.Default.AgShareServer = textBoxServer.Text;
        Properties.Settings.Default.AgShareApiKey = textBoxApiKey.Text;
        Properties.Settings.Default.Save();

        labelStatus.Text = "\u2714 " + gStr.gsAgShareSaved;
        labelStatus.Foreground = Brushes.Blue;

        Close(true);
    }

    /// <summary>
    /// [XPLAT] Reproduces the WinForms <c>buttonCancel</c> <c>DialogResult.Cancel</c>: dismiss the dialog
    /// with a <see langword="false"/> result (no settings written).
    /// </summary>
    /// <param name="sender">The Cancel button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void buttonCancel_Click(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    /// <summary>
    /// [XPLAT] Parity with <c>textBoxAnySetting_TextChanged</c>: any edit to the server or API-key field
    /// enables Save.
    /// </summary>
    /// <param name="sender">The edited text box (unused).</param>
    /// <param name="e">The text-changed event data (unused).</param>
    private void textBoxAnySetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        buttonSave.IsEnabled = true;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>UpdateAgShareToggleButton</c>: paint the toggle glyph
    /// (UploadOn/UploadOff) from <see cref="Settings.AgShareEnabled"/>, surface its caption, and gate the
    /// dependent auto-upload / auto-load buttons. <c>AgShareEnabled</c> defaults to <see langword="false"/>
    /// (security-frozen, AAP §0.7.2 / docs/settings.md:L419-L423) — never default-enabled. When disabled,
    /// Save is enabled so the user can still persist a server/key without activating AgShare.
    /// </summary>
    private void UpdateAgShareToggleButton()
    {
        bool enabled = Properties.Settings.Default.AgShareEnabled;

        if (enabled)
        {
            imgToggleUpload.Source = LoadBitmap("avares://AgOpenGPS/btnImages/UploadOn.png");
            // [XPLAT] WinForms btnToggleUpload.Text (caption under the glyph). The Avalonia button's
            // content is the state-conveying glyph image, so the caption is surfaced as the tooltip.
            ToolTip.SetTip(btnToggleUpload, gStr.gsAgShareActivated);
        }
        else
        {
            imgToggleUpload.Source = LoadBitmap("avares://AgOpenGPS/btnImages/UploadOff.png");
            ToolTip.SetTip(btnToggleUpload, gStr.gsAgShareDeactivated);
            buttonSave.IsEnabled = true;
        }

        btnAutoUpload.IsEnabled = enabled;
        btnAutoLoad.IsEnabled = enabled;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>btnToggleUpload_Click</c>: flip <see cref="Settings.AgShareEnabled"/>,
    /// re-paint the glyph, and persist.
    /// </summary>
    /// <param name="sender">The toggle button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnToggleUpload_Click(object sender, RoutedEventArgs e)
    {
        Properties.Settings.Default.AgShareEnabled = !Properties.Settings.Default.AgShareEnabled;
        UpdateAgShareToggleButton();
        Properties.Settings.Default.Save();
    }

    /// <summary>
    /// [XPLAT] Parity with <c>btnPaste_Click</c>: move the clipboard text into the API-key field, then
    /// clear the clipboard and disable Paste. Uses the Avalonia <see cref="IClipboard"/> (async) in place
    /// of the WinForms synchronous <c>Clipboard</c>.
    /// </summary>
    /// <param name="sender">The Paste button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private async void btnPaste_Click(object sender, RoutedEventArgs e)
    {
        var clipboard = GetClipboard();
        if (clipboard == null)
        {
            return;
        }

        string text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            textBoxApiKey.Text = text;
            await clipboard.ClearAsync();
            btnPaste.IsEnabled = false;
        }
    }

    /// <summary>
    /// [XPLAT] Parity with <c>textBoxServer_Click</c>: when the on-screen keyboard is enabled, edit the
    /// server field through the migrated modal <see cref="FormKeyboard"/> and move focus to Paste. The
    /// WinForms <c>((TextBox)sender).ShowKeyboard(this)</c> extension and the <c>Owner as FormGPS</c>
    /// coupling are dropped (AAP §0.6.1): only the <see cref="Settings.setDisplay_isKeyboardOn"/> gate
    /// remains, exactly as the sibling migrated views do.
    /// </summary>
    /// <param name="sender">The server text box (unused; the field is addressed by name).</param>
    /// <param name="e">The tapped gesture data (unused).</param>
    private async void textBoxServer_Click(object sender, TappedEventArgs e)
    {
        if (!Properties.Settings.Default.setDisplay_isKeyboardOn)
        {
            return;
        }

        string result = await new FormKeyboard(textBoxServer.Text ?? string.Empty).ShowDialog<string>(this);
        if (result != null)
        {
            textBoxServer.Text = result;
        }

        btnPaste.Focus();
    }

    /// <summary>
    /// [XPLAT] Parity with <c>linkRegister_LinkClicked</c>: open the sign-up page in the default browser.
    /// The WinForms source already used <c>UseShellExecute = true</c>, which is the cross-platform way to
    /// hand a URL to the OS shell, so it is carried across unchanged.
    /// </summary>
    /// <param name="sender">The register hyperlink button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void linkRegister_LinkClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = RegisterUrl,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// [XPLAT] Parity with <c>btnDevelop_Click</c>: the hidden developer toggle unlocks the server field.
    /// </summary>
    /// <param name="sender">The developer toggle button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnDevelop_Click(object sender, RoutedEventArgs e)
    {
        textBoxServer.IsEnabled = true;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>btnAutoLoad_Click</c>: flip <see cref="Settings.AgShareAutoLoad"/>, re-paint
    /// the glyph, persist, and enable Save.
    /// </summary>
    /// <param name="sender">The auto-load button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnAutoLoad_Click(object sender, RoutedEventArgs e)
    {
        Properties.Settings.Default.AgShareAutoLoad = !Properties.Settings.Default.AgShareAutoLoad;
        UpdateAgShareAutoLoadButton();
        Properties.Settings.Default.Save();
        buttonSave.IsEnabled = true;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>UpdateAgShareAutoLoadButton</c>: paint the auto-load glyph
    /// (DownloadAndUse/DownloadAll) and caption from <see cref="Settings.AgShareAutoLoad"/>.
    /// </summary>
    private void UpdateAgShareAutoLoadButton()
    {
        if (Properties.Settings.Default.AgShareAutoLoad)
        {
            imgAutoLoad.Source = LoadBitmap("avares://AgOpenGPS/btnImages/DownloadAndUse.png");
            ToolTip.SetTip(btnAutoLoad, gStr.gsAgShareAutoLoad);
        }
        else
        {
            imgAutoLoad.Source = LoadBitmap("avares://AgOpenGPS/btnImages/DownloadAll.png");
            ToolTip.SetTip(btnAutoLoad, gStr.gsAgShareLocalOnly);
        }
    }

    /// <summary>
    /// [XPLAT] Parity with <c>btnAutoUpload_Click</c>: flip <see cref="Settings.AgShareUploadActive"/>,
    /// re-paint the glyph, and enable Save. (Like the WinForms original, this toggle does not itself
    /// persist; the change is committed when the user clicks Save.)
    /// </summary>
    /// <param name="sender">The auto-upload button (unused).</param>
    /// <param name="e">The routed event data (unused).</param>
    private void btnAutoUpload_Click(object sender, RoutedEventArgs e)
    {
        Properties.Settings.Default.AgShareUploadActive = !Properties.Settings.Default.AgShareUploadActive;
        UpdateAgShareUploadButton();
        buttonSave.IsEnabled = true;
    }

    /// <summary>
    /// [XPLAT] Parity with <c>UpdateAgShareUploadButton</c>: paint the auto-upload glyph
    /// (AutoUploadOn/AutoUploadOff) and caption from <see cref="Settings.AgShareUploadActive"/>.
    /// </summary>
    private void UpdateAgShareUploadButton()
    {
        if (Properties.Settings.Default.AgShareUploadActive)
        {
            ToolTip.SetTip(btnAutoUpload, gStr.gsAgShareUploadOn);
            imgAutoUpload.Source = LoadBitmap("avares://AgOpenGPS/btnImages/AutoUploadOn.png");
        }
        else
        {
            ToolTip.SetTip(btnAutoUpload, gStr.gsAgShareUploadOff);
            imgAutoUpload.Source = LoadBitmap("avares://AgOpenGPS/btnImages/AutoUploadOff.png");
        }
    }

    /// <summary>
    /// [XPLAT] Enables Paste only while the clipboard holds text — the async equivalent of the WinForms
    /// <c>Clipboard.ContainsText()</c> poll. <see cref="ClipboardExtensions.TryGetTextAsync"/> is the
    /// non-obsolete text read on Avalonia 11.3 (the bare <c>IClipboard.GetTextAsync</c> is
    /// <c>[Obsolete]</c>).
    /// </summary>
    /// <returns>A task that completes once the Paste enablement has been refreshed.</returns>
    private async Task UpdatePasteEnabledAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null)
        {
            return;
        }

        string text = await clipboard.TryGetTextAsync();
        bool hasText = !string.IsNullOrEmpty(text);
        if (btnPaste.IsEnabled != hasText)
        {
            btnPaste.IsEnabled = hasText;
        }
    }

    /// <summary>
    /// [XPLAT] Resolves the clipboard for this window (<c>TopLevel.Clipboard</c>), the cross-platform
    /// replacement for the static WinForms <c>Clipboard</c>. May be <see langword="null"/> very early in
    /// the window lifecycle, so every caller null-checks.
    /// </summary>
    /// <returns>The clipboard, or <see langword="null"/> if no top level is available yet.</returns>
    private IClipboard GetClipboard()
    {
        return TopLevel.GetTopLevel(this)?.Clipboard;
    }

    /// <summary>
    /// [XPLAT] Sets the Cancel button caption while preserving its Cancel64 glyph. The button's content
    /// is an image-over-caption stack (FormAgShareSettingsView.axaml), so the caption is written onto the
    /// stack's <see cref="TextBlock"/> rather than replacing the whole content (which would drop the
    /// image). Defensive against the stack shape changing.
    /// </summary>
    /// <param name="caption">The translated Cancel caption.</param>
    private void SetCancelCaption(string caption)
    {
        var captionText = buttonCancel.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault();
        if (captionText != null)
        {
            captionText.Text = caption;
        }
    }

    /// <summary>
    /// [XPLAT] Loads a button glyph packaged as an Avalonia resource (avares://) — the cross-platform
    /// replacement for the WinForms <c>Properties.Resources</c> bitmap accessors. The glyph PNG bytes are
    /// unchanged; only the packaging/loading mechanism changes.
    /// </summary>
    /// <param name="uri">The <c>avares://</c> URI of the glyph (assembly "AgOpenGPS", folder btnImages).</param>
    /// <returns>The decoded bitmap for assignment to an <see cref="Image"/> source.</returns>
    private static Bitmap LoadBitmap(string uri)
    {
        return new Bitmap(AssetLoader.Open(new Uri(uri)));
    }
}
