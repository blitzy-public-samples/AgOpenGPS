// [XPLAT] migrated from net48/WinForms (Forms/FormAgShareSettings.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AgOpenGPS.Properties;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] AgShare server / API-key settings dialog.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormAgShareSettings</c>
    /// (FormAgShareSettings.cs + .Designer.cs). The form edits the AgShare server URL and API key,
    /// toggles upload / auto-upload / auto-load state, pastes the key from the clipboard, opens the
    /// registration page, and saves.
    ///
    /// Everything that reads/writes <c>Settings.Default</c> (the AgShare* fields are present and
    /// cross-platform) and everything that is pure UI is ported here verbatim:
    /// <list type="bullet">
    ///   <item>load the server/key text and the three toggle glyphs from settings;</item>
    ///   <item>the three toggle buttons flip their setting, persist, and re-glyph (parity with
    ///         UpdateAgShareToggleButton / UpdateAgShareUploadButton / UpdateAgShareAutoLoadButton);</item>
    ///   <item>Develop unlocks the server field; Register opens the sign-up URL;</item>
    ///   <item>Paste pulls the clipboard into the API-key box (Avalonia <c>IClipboard</c>, replacing
    ///         the WinForms <c>Clipboard</c> + 500&#160;ms enable-poll);</item>
    ///   <item>editing either field enables Save; Save persists the server/key; Cancel closes.</item>
    /// </list>
    ///
    /// One behaviour is deferred rather than fabricated: the live "Test Connection" call went through
    /// the injected <c>AgShareClient</c> (<c>AgShareClient.CheckApiAsync</c> + the
    /// <c>AgShareError</c> hierarchy) and propagated to it on Save (<c>_agShareClient.UpdateSettings</c>).
    /// That client is supplied by the FormGPS/AgShare integration, which is not yet projected into the
    /// Avalonia shell, so the network test and the client-propagation are left for that wiring. Saving
    /// still persists to <c>Settings.Default</c> (which the client reads), so no settings behaviour is
    /// lost. The on-screen-keyboard tap (<c>mf</c>-owned) is likewise deferred.
    /// </summary>
    public partial class FormAgShareSettingsView : Window
    {
        private const string RegisterUrl = "https://agshare.agopengps.com/register";

        private DispatcherTimer _clipboardTimer;

        public FormAgShareSettingsView()
        {
            InitializeComponent();

            btnToggleUpload.Click += OnToggleUploadClick;
            btnAutoUpload.Click += OnAutoUploadClick;
            btnAutoLoad.Click += OnAutoLoadClick;
            btnDevelop.Click += OnDevelopClick;
            linkRegister.Click += OnRegisterClick;
            btnPaste.Click += OnPasteClick;
            buttonCancel.Click += OnCancelClick;
            buttonSave.Click += OnSaveClick;

            textBoxServer.TextChanged += OnAnySettingTextChanged;
            textBoxApiKey.TextChanged += OnAnySettingTextChanged;
        }

        // [XPLAT] Parity with FormAgShareSettings_Load: seed fields + glyphs from settings and begin
        // monitoring the clipboard so Paste enables only when text is available.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            textBoxServer.Text = Settings.Default.AgShareServer;
            textBoxApiKey.Text = Settings.Default.AgShareApiKey;

            UpdateAgShareToggleButton();
            UpdateAgShareUploadButton();
            UpdateAgShareAutoLoadButton();

            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _clipboardTimer.Tick += OnClipboardTimerTick;
            _clipboardTimer.Start();
            _ = UpdatePasteEnabledAsync();
        }

        // [XPLAT] Parity with OnFormClosed: stop the clipboard poll.
        protected override void OnClosed(EventArgs e)
        {
            if (_clipboardTimer != null)
            {
                _clipboardTimer.Stop();
                _clipboardTimer.Tick -= OnClipboardTimerTick;
                _clipboardTimer = null;
            }

            base.OnClosed(e);
        }

        // [XPLAT] Parity with ClipboardCheckTimer_Tick: re-evaluate Paste enablement each interval.
        // A named handler (rather than a lambda) keeps the EventArgs parameter and the discarded Task
        // distinct.
        private void OnClipboardTimerTick(object sender, EventArgs e)
        {
            _ = UpdatePasteEnabledAsync();
        }

        // [XPLAT] Parity with btnToggleUpload_Click: flip AgShareEnabled, re-glyph, persist.
        private void OnToggleUploadClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.AgShareEnabled = !Settings.Default.AgShareEnabled;
            UpdateAgShareToggleButton();
            Settings.Default.Save();
        }

        // [XPLAT] Parity with btnAutoUpload_Click: flip AgShareUploadActive, re-glyph, enable Save.
        private void OnAutoUploadClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.AgShareUploadActive = !Settings.Default.AgShareUploadActive;
            UpdateAgShareUploadButton();
            buttonSave.IsEnabled = true;
        }

        // [XPLAT] Parity with btnAutoLoad_Click: flip AgShareAutoLoad, re-glyph, persist, enable Save.
        private void OnAutoLoadClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.AgShareAutoLoad = !Settings.Default.AgShareAutoLoad;
            UpdateAgShareAutoLoadButton();
            Settings.Default.Save();
            buttonSave.IsEnabled = true;
        }

        // [XPLAT] Parity with btnDevelop_Click: unlock the server field.
        private void OnDevelopClick(object sender, RoutedEventArgs e)
        {
            textBoxServer.IsEnabled = true;
        }

        // [XPLAT] Parity with linkRegister_LinkClicked: open the sign-up page in the default browser.
        private void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RegisterUrl,
                UseShellExecute = true
            });
        }

        // [XPLAT] Parity with textBoxAnySetting_TextChanged: any edit enables Save.
        private void OnAnySettingTextChanged(object sender, TextChangedEventArgs e)
        {
            buttonSave.IsEnabled = true;
        }

        // [XPLAT] Parity with btnPaste_Click: move clipboard text into the API-key box, then clear it.
        // Uses the Avalonia IClipboard (async) in place of the WinForms synchronous Clipboard.
        private async void OnPasteClick(object sender, RoutedEventArgs e)
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

        // [XPLAT] Parity with buttonCancel_Click (WinForms DialogResult.Cancel): dismiss.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        // [XPLAT] Parity with buttonSave_Click: persist the server/key to settings and confirm. The
        // _agShareClient.UpdateSettings propagation is deferred to the AgShare client wiring.
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.AgShareServer = textBoxServer.Text;
            Settings.Default.AgShareApiKey = textBoxApiKey.Text;
            Settings.Default.Save();

            labelStatus.Text = "\u2714 Saved";
            labelStatus.Foreground = Brushes.Blue;
        }

        // [XPLAT] Parity with UpdateAgShareToggleButton: glyph + dependent-button enablement.
        private void UpdateAgShareToggleButton()
        {
            bool enabled = Settings.Default.AgShareEnabled;

            imgToggleUpload.Source = enabled
                ? LoadBitmap("avares://AgOpenGPS/btnImages/UploadOn.png")
                : LoadBitmap("avares://AgOpenGPS/btnImages/UploadOff.png");

            if (!enabled)
            {
                buttonSave.IsEnabled = true;
            }

            btnAutoUpload.IsEnabled = enabled;
            btnAutoLoad.IsEnabled = enabled;
        }

        // [XPLAT] Parity with UpdateAgShareUploadButton.
        private void UpdateAgShareUploadButton()
        {
            imgAutoUpload.Source = Settings.Default.AgShareUploadActive
                ? LoadBitmap("avares://AgOpenGPS/btnImages/AutoUploadOn.png")
                : LoadBitmap("avares://AgOpenGPS/btnImages/AutoUploadOff.png");
        }

        // [XPLAT] Parity with UpdateAgShareAutoLoadButton.
        private void UpdateAgShareAutoLoadButton()
        {
            imgAutoLoad.Source = Settings.Default.AgShareAutoLoad
                ? LoadBitmap("avares://AgOpenGPS/btnImages/DownloadAndUse.png")
                : LoadBitmap("avares://AgOpenGPS/btnImages/DownloadAll.png");
        }

        // [XPLAT] Enables Paste only when the clipboard holds text (parity with the WinForms
        // ClipboardCheckTimer_Tick / Clipboard.ContainsText poll).
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

        private Avalonia.Input.Platform.IClipboard GetClipboard()
        {
            return TopLevel.GetTopLevel(this)?.Clipboard;
        }

        private static Bitmap LoadBitmap(string uri)
        {
            return new Bitmap(AssetLoader.Open(new Uri(uri)));
        }
    }
}
