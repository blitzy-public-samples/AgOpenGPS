// [XPLAT] migrated from net48/WinForms (Forms/Form_Keys.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Text;
using System.Text.RegularExpressions;
using AgOpenGPS.Properties;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Keyboard-shortcut (hotkey) editor.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/Form_Keys</c> (Form_Keys.cs +
    /// .Designer.cs). The form presents nineteen shortcut buttons (eleven actions + eight
    /// section/zone slots); the operator clicks a button to arm it ("..."), then types a single
    /// alphanumeric key into <c>tboxKey</c>, which is validated and written onto the armed button.
    /// Reset restores the factory defaults and OK refuses to close while any slot is still unset.
    ///
    /// The single source of truth is the persisted setting <c>Settings.Default.setKey_hotkeys</c> — a
    /// nineteen-character string the WinForms form loaded into the buttons and rewrote on close. That
    /// setting is cross-platform and already present, so the whole editor is ported self-contained
    /// here: load from / save to <c>Settings.Default.setKey_hotkeys</c> directly. The only deferred
    /// piece is the in-memory propagation to the running app (the WinForms code also assigned
    /// <c>mf.hotkeys = setKey_hotkeys.ToCharArray()</c>); that lives with the FormGPS hotkey-consumer
    /// wiring and is not fabricated here. The persisted value — which that consumer reads — is written
    /// correctly, so no behaviour is lost at the settings layer.
    ///
    /// Invalid input reuses the migrated <see cref="FormDialogView"/> error dialog, exactly as the
    /// WinForms code called <c>FormDialog.Show(...)</c>.
    /// </summary>
    public partial class FormKeysView : Window
    {
        // [XPLAT] Factory-default hotkey string — verbatim from Form_Keys.btnReset_Click.
        private const string DefaultHotkeys = "ACFGMNPTYVW12345678";
        private const string Armed = "...";

        private Button[] _shortcutButtons;
        private bool _suppressKeyTextChanged;

        public FormKeysView()
        {
            InitializeComponent();

            // [XPLAT] Ordered to match the character order of Settings.Default.setKey_hotkeys, i.e. the
            // exact order Form_Keys.LoadButtonText / FormClosing used (indices 0..18).
            _shortcutButtons = new[]
            {
                btnAutosteer, btnCycleLines, btnFieldMenu, btnNewFlag, btnManualSection,
                btnAutoSection, btnSnapToPivot, btnMoveLineLeft, btnMoveLineRight,
                btnVehicleSettings, btnSteerWizard,
                btnSection1, btnSection2, btnSection3, btnSection4,
                btnSection5, btnSection6, btnSection7, btnSection8
            };

            foreach (var button in _shortcutButtons)
            {
                button.Click += OnEditShortcutClick;
            }

            btnReset.Click += OnResetClick;
            btnOK.Click += OnOkClick;
            tboxKey.TextChanged += OnKeyTextChanged;

            LoadButtonText();
        }

        // [XPLAT] Parity with Form_Keys.btnEditShortcut_Click -> EditShortcut(btn): arm the clicked
        // button and focus the capture box.
        private void OnEditShortcutClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;

            lblAutosteer.Text = "Press New Shortcut Key";
            button.Content = Armed;

            _suppressKeyTextChanged = true;
            tboxKey.Text = "";
            _suppressKeyTextChanged = false;

            tboxKey.Focus();
        }

        // [XPLAT] Parity with Form_Keys.tboxKey_TextChanged: accept a single alphanumeric key, assign
        // it (upper-cased) to the first armed button, and reject anything else. The re-entrancy guard
        // (_suppressKeyTextChanged) mirrors the original's detach/attach of the TextChanged handler.
        private void OnKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressKeyTextChanged)
            {
                return;
            }

            _suppressKeyTextChanged = true;
            try
            {
                string text = tboxKey.Text ?? string.Empty;

                if (text.Length > 1)
                {
                    tboxKey.Text = "";
                    return;
                }

                text = Regex.Replace(text, "[^0-9a-zA-Z]", "");
                if (text.Length > 0)
                {
                    lblAutosteer.Text = "Press Button to Edit Shortcut";
                    string key = text.ToUpperInvariant();

                    foreach (var button in _shortcutButtons)
                    {
                        if (GetContent(button) == Armed)
                        {
                            button.Content = key;
                            break;
                        }
                    }

                    tboxKey.Text = key;
                }
                else
                {
                    // [XPLAT] Parity with FormDialog.Show("Alphanumeric Only", ...). Fire-and-forget:
                    // the original modal toast does not gate further input, and the capture box stays
                    // ready for a valid key.
                    _ = FormDialogView.ShowAsync(this, "Alphanumeric Only", "A to Z and 0 to 9", DialogSeverity.Error);
                }
            }
            finally
            {
                _suppressKeyTextChanged = false;
            }
        }

        // [XPLAT] Parity with Form_Keys.btnReset_Click: restore defaults, persist, reload the buttons.
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.setKey_hotkeys = DefaultHotkeys;
            Settings.Default.Save();
            LoadButtonText();
        }

        // [XPLAT] Parity with Form_Keys.btnOK_Click: refuse to close while any slot is still armed.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            foreach (var button in _shortcutButtons)
            {
                if (GetContent(button) == Armed)
                {
                    _ = FormDialogView.ShowAsync(this, "HoyKey Incomplete", "Finish Setting All, or Reset to Default", DialogSeverity.Error);
                    return;
                }
            }

            Close();
        }

        // [XPLAT] Parity with Form_Keys.LoadButtonText: seed the buttons from the persisted string.
        private void LoadButtonText()
        {
            string hotkeys = Settings.Default.setKey_hotkeys;
            if (string.IsNullOrEmpty(hotkeys))
            {
                hotkeys = DefaultHotkeys;
            }

            for (int i = 0; i < _shortcutButtons.Length; i++)
            {
                _shortcutButtons[i].Content = i < hotkeys.Length
                    ? hotkeys[i].ToString()
                    : DefaultHotkeys[i].ToString();
            }
        }

        // [XPLAT] Parity with Form_Keys.Form_Keys_FormClosing: write the buttons back to the persisted
        // setting. The mf.hotkeys in-memory propagation is deferred to the hotkey-consumer wiring.
        protected override void OnClosed(EventArgs e)
        {
            var builder = new StringBuilder(_shortcutButtons.Length);
            foreach (var button in _shortcutButtons)
            {
                builder.Append(GetContent(button));
            }

            Settings.Default.setKey_hotkeys = builder.ToString();
            Settings.Default.Save();

            base.OnClosed(e);
        }

        private static string GetContent(Button button)
        {
            return button.Content as string ?? string.Empty;
        }
    }
}
