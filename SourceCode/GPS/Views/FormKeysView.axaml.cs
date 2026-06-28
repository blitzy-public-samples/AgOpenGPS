// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Keyboard-shortcut (hotkey) editor — a 1:1 behavioural-parity reimplementation of the
    /// WinForms <c>Forms/Form_Keys</c> (Form_Keys.cs + Form_Keys.Designer.cs; note the underscore in
    /// the source file name).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor presents nineteen shortcut buttons — eleven action shortcuts (Auto Steer, Cycle Lines,
    /// Field Menu, New Flag, Manual/Auto Section, Snap to Pivot, Move Line Left/Right, Vehicle Settings,
    /// Steer Wizard) followed by eight Section/Zone slots — in the exact character order of the persisted
    /// nineteen-character setting. The operator clicks a button to arm it (its content becomes the marker
    /// <c>"..."</c>), then types a single alphanumeric key into <c>tboxKey</c>; the key is validated,
    /// upper-cased and written onto the armed button. <c>Reset</c> restores the factory defaults and
    /// <c>OK</c> refuses to close while any slot is still unset.
    /// </para>
    /// <para>
    /// <b>State / persistence.</b> The single source of truth is
    /// <c>Properties.Settings.Default.setKey_hotkeys</c> — a nineteen-character string the WinForms form
    /// loaded into the buttons and rewrote on close. That setting is already cross-platform (the recompiled
    /// <c>AgOpenGPS.Properties.Settings</c>), so the editor loads from / saves to it directly, with no
    /// Windows-specific backing.
    /// </para>
    /// <para>
    /// <b>FormGPS decoupling (AAP §0.3.2).</b> The WinForms code reached the <c>FormGPS</c> god-object
    /// through a <c>private readonly FormGPS mf</c> back-reference solely to propagate the freshly chosen
    /// hotkeys into the running application (<c>mf.hotkeys = …ToCharArray()</c>). That coupling is removed:
    /// the owner (MainView) injects a <see cref="System.Action{T}"/> callback through the constructor, and
    /// the editor invokes it on every persist so the runtime hotkey map is refreshed exactly as before —
    /// without any reference to <c>FormGPS</c>.
    /// </para>
    /// <para>
    /// <b>Framework conversions</b> (each tagged <c>// [XPLAT]</c> below): WinForms <c>Button.Text</c> →
    /// Avalonia <c>Button.Content</c>; the modal blocking <c>FormDialog.Show(…)</c> → the migrated
    /// <see cref="FormDialogView.ShowAsync(string, string, DialogSeverity?, Window)"/>; the
    /// <c>tboxKey.TextChanged</c> detach/attach guard → the <see cref="_suppressKeyTextChanged"/> re-entrancy
    /// flag (the established sibling pattern); <c>string.ToUpper()</c> → <c>ToUpperInvariant()</c> for
    /// culture-independent parity (§0.6.5); and the WinForms <c>FormClosing</c> persistence path → the
    /// <see cref="OnClosing(WindowClosingEventArgs)"/> override. The dialog is intentionally code-behind
    /// driven (no view-model, no bindings): the partner markup <c>FormKeysView.axaml</c> declares only the
    /// named controls and deliberately wires no <c>Click</c>/<c>TextChanged</c> attributes, so every handler
    /// is subscribed here by name.
    /// </para>
    /// </remarks>
    public partial class FormKeysView : Window
    {
        /// <summary>
        /// [XPLAT] Factory-default hotkey string — copied verbatim from the WinForms
        /// <c>Form_Keys.btnReset_Click</c> and identical to the <c>setKey_hotkeys</c> default. The nineteen
        /// characters map 1:1, in order, onto <see cref="_shortcutButtons"/>.
        /// </summary>
        private const string DefaultHotkeys = "ACFGMNPTYVW12345678";

        /// <summary>
        /// [XPLAT] The "awaiting key" marker the WinForms code wrote into an armed button
        /// (<c>btn.Text = "..."</c>). A button whose content equals this is unset.
        /// </summary>
        private const string Armed = "...";

        /// <summary>
        /// [XPLAT] Runtime hotkey-map refresh callback — the cross-platform replacement for the WinForms
        /// <c>mf.hotkeys = …ToCharArray()</c> assignments (AAP §0.3.2). Supplied by the owner (MainView) and
        /// invoked with the new nineteen-character array on every persist (Reset, OK and close). May be
        /// <see langword="null"/> (e.g. the XAML-loader/previewer path), in which case persistence still
        /// occurs but no in-memory propagation is attempted.
        /// </summary>
        private readonly Action<char[]> _applyHotkeys;

        /// <summary>
        /// The nineteen shortcut buttons in the exact character order of
        /// <c>Properties.Settings.Default.setKey_hotkeys</c> (indices 0..18) — the same order the WinForms
        /// <c>LoadButtonText</c>, <c>FormClosing</c> concatenation, <c>tboxKey_TextChanged</c> assignment
        /// chain and <c>btnOK_Click</c> validation all used. Populated once from the XAML-generated fields.
        /// </summary>
        private readonly Button[] _shortcutButtons;

        /// <summary>
        /// Re-entrancy guard for <see cref="OnKeyTextChanged"/>. Assigning <c>tboxKey.Text</c> inside the
        /// handler re-raises <c>TextChanged</c>; this flag suppresses the recursive pass, mirroring the
        /// WinForms detach/attach of the <c>tboxKey.TextChanged</c> subscription.
        /// </summary>
        private bool _suppressKeyTextChanged;

        /// <summary>
        /// Persist-once guard. <see cref="OnOkClick"/> persists then calls <c>Close(true)</c>, and
        /// <see cref="OnClosing(WindowClosingEventArgs)"/> persists on any other close (window button /
        /// Escape) — both routed through <see cref="PersistHotkeys"/>, which writes the settings exactly
        /// once so the final saved string is identical regardless of how the dialog is dismissed.
        /// </summary>
        private bool _persisted;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia compiled-XAML loader and the
        /// design-time previewer (the established sibling convention — see <c>FormDialogView</c> /
        /// <c>FormYesView</c>). It delegates to <see cref="FormKeysView(Action{char[]})"/> with no
        /// refresh callback, so the editor still loads and saves the persisted setting; together the two
        /// constructors provide the optional-callback construction the migration specifies.
        /// </summary>
        public FormKeysView() : this(null)
        {
        }

        /// <summary>
        /// [XPLAT] Dependency-injection constructor replacing the WinForms <c>FormGPS mf</c> back-reference
        /// (AAP §0.3.2). The owner (MainView) passes a callback that refreshes its runtime hotkey map; the
        /// editor invokes it on every persist, reproducing the WinForms <c>mf.hotkeys = …</c> propagation
        /// without coupling to <c>FormGPS</c>.
        /// </summary>
        /// <param name="applyHotkeys">
        /// Callback applied with the new nineteen-character hotkey array whenever the hotkeys are persisted
        /// (Reset / OK / close). May be <see langword="null"/> when no in-memory propagation is required.
        /// </param>
        public FormKeysView(Action<char[]> applyHotkeys)
        {
            _applyHotkeys = applyHotkeys;

            InitializeComponent();

            // [XPLAT] Ordered to match the character order of Properties.Settings.Default.setKey_hotkeys —
            // i.e. the exact order Form_Keys.LoadButtonText / FormClosing / tboxKey_TextChanged /
            // btnOK_Click used (indices 0..18). The names resolve to the x:Name'd controls declared in
            // FormKeysView.axaml.
            _shortcutButtons = new[]
            {
                btnAutosteer, btnCycleLines, btnFieldMenu, btnNewFlag, btnManualSection,
                btnAutoSection, btnSnapToPivot, btnMoveLineLeft, btnMoveLineRight,
                btnVehicleSettings, btnSteerWizard,
                btnSection1, btnSection2, btnSection3, btnSection4,
                btnSection5, btnSection6, btnSection7, btnSection8
            };

            // [XPLAT] FormKeysView.axaml deliberately declares no Click=/TextChanged= attributes, so every
            // handler the WinForms designer wired is subscribed here by name: each hotkey button raised
            // btnEditShortcut_Click; btnReset -> btnReset_Click; btnOK -> btnOK_Click; tboxKey ->
            // tboxKey_TextChanged.
            foreach (Button button in _shortcutButtons)
            {
                button.Click += OnEditShortcutClick;
            }

            btnReset.Click += OnResetClick;
            btnOK.Click += OnOkClick;
            tboxKey.TextChanged += OnKeyTextChanged;

            // [XPLAT] Parity with Form_Keys_Load -> LoadButtonText: seed the buttons from the persisted
            // setting on construction. (The WinForms ScreenHelper.IsOnScreen bounds check is a Windows-only
            // concern handled by the .axaml WindowStartupLocation="CenterOwner", so it is not reproduced.)
            LoadButtonText();
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.btnEditShortcut_Click</c> -> <c>EditShortcut(btn)</c>: arm the
        /// clicked button (content becomes <see cref="Armed"/>), clear and focus the capture box so the next
        /// alphanumeric key lands on this slot.
        /// </summary>
        /// <param name="sender">The hotkey button that was clicked.</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnEditShortcutClick(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;

            lblAutosteer.Text = "Press New Shortcut Key";
            button.Content = Armed;

            // [XPLAT] WinForms detached tboxKey.TextChanged before clearing the box and re-attached after;
            // the suppress flag reproduces that so clearing the box does not run the capture logic.
            _suppressKeyTextChanged = true;
            tboxKey.Text = string.Empty;
            _suppressKeyTextChanged = false;

            tboxKey.Focus();
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.tboxKey_TextChanged</c>: capture a single alphanumeric key,
        /// upper-case it and write it onto the first armed button; reject anything else with the error
        /// dialog. Reproduces the original sequence exactly — the over-long guard, the
        /// <c>Regex.Replace(text, "[^0-9a-zA-Z]", "")</c> write-back into the capture box (so an illegal
        /// character is cleared rather than left to merge with the next key), the assignment to the first
        /// slot still showing <see cref="Armed"/>, and the modal "Alphanumeric Only" error otherwise.
        /// </summary>
        /// <param name="sender">The capture text box (unused).</param>
        /// <param name="e">The text-changed event data (unused).</param>
        private async void OnKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            // [XPLAT] Re-entrancy guard (was the WinForms TextChanged detach/attach): ignore the changes we
            // make to tboxKey.Text below.
            if (_suppressKeyTextChanged)
            {
                return;
            }

            _suppressKeyTextChanged = true;
            try
            {
                // [XPLAT] Avalonia TextBox.Text is nullable; a null-safe snapshot keeps the length checks
                // and Regex.Replace below safe (the non-null case is identical to WinForms).
                string raw = tboxKey.Text ?? string.Empty;

                // [XPLAT] Verbatim from WinForms: more than one character means a stray/extra keystroke —
                // clear and bail (the guard suppresses the resulting TextChanged).
                if (raw.Length > 1)
                {
                    tboxKey.Text = string.Empty;
                    return;
                }

                // [XPLAT] Verbatim from WinForms `tboxKey.Text = Regex.Replace(tboxKey.Text, "[^0-9a-zA-Z]", "")`:
                // strip any non-alphanumeric character AND write the result back, so an illegal key leaves the
                // capture box empty (not holding the rejected character, which would otherwise merge with the
                // next keystroke and be swallowed by the over-long guard above).
                string cleaned = Regex.Replace(raw, "[^0-9a-zA-Z]", "");
                tboxKey.Text = cleaned;

                if (cleaned.Length > 0)
                {
                    lblAutosteer.Text = "Press Button to Edit Shortcut";

                    // [XPLAT] WinForms tboxKey.Text.ToUpper() -> ToUpperInvariant() for culture-independent
                    // parity (§0.6.5): a Turkish/locale-specific casing must not alter the stored shortcut.
                    string key = cleaned.ToUpperInvariant();

                    // [XPLAT] Parity with the WinForms else-if chain: assign to the FIRST button still armed
                    // ("..."), in the same button order, then stop.
                    foreach (Button button in _shortcutButtons)
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
                    // [XPLAT] Parity with FormDialog.Show("Alphanumeric Only", …, DialogSeverity.Error) — now
                    // the migrated owner-last FormDialogView.ShowAsync. Awaited (async void) to reproduce the
                    // WinForms modal-blocking behaviour; the suppress flag is released in the finally only
                    // after the dialog closes, and the modal dialog disables this window meanwhile so no
                    // capture can race it.
                    await FormDialogView.ShowAsync("Alphanumeric Only", "A to Z and 0 to 9", DialogSeverity.Error, this);
                }
            }
            finally
            {
                _suppressKeyTextChanged = false;
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.btnReset_Click</c>: restore the factory defaults, persist them,
        /// propagate them to the running application via <see cref="_applyHotkeys"/> (was <c>mf.hotkeys</c>),
        /// and reload the buttons from the now-default setting.
        /// </summary>
        /// <param name="sender">The Reset button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.setKey_hotkeys = DefaultHotkeys;
            Properties.Settings.Default.Save();

            // [XPLAT] WinForms `mf.hotkeys = Properties.Settings.Default.setKey_hotkeys.ToCharArray();`.
            _applyHotkeys?.Invoke(DefaultHotkeys.ToCharArray());

            LoadButtonText();
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.btnOK_Click</c>: refuse to close while any slot is still armed
        /// (showing <see cref="Armed"/>), surfacing the WinForms "HoyKey Incomplete" error; otherwise persist
        /// the hotkeys and close with a positive result.
        /// </summary>
        /// <param name="sender">The OK button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void OnOkClick(object sender, RoutedEventArgs e)
        {
            // [XPLAT] Parity with the WinForms validation: if ANY button still shows "...", show the error
            // dialog and do NOT close. The title string ("HoyKey Incomplete") is copied verbatim, original
            // typo included, to keep 1:1 parity.
            foreach (Button button in _shortcutButtons)
            {
                if (GetContent(button) == Armed)
                {
                    await FormDialogView.ShowAsync("HoyKey Incomplete", "Finish Setting All, or Reset to Default", DialogSeverity.Error, this);
                    return;
                }
            }

            // [XPLAT] Valid: build + persist + Save + applyHotkeys (via PersistHotkeys), then close with a
            // positive result. Close(true) triggers OnClosing, whose persist is a no-op here (already done).
            PersistHotkeys();
            Close(true);
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.LoadButtonText</c>: seed each of the nineteen buttons from the
        /// corresponding character of <c>Properties.Settings.Default.setKey_hotkeys</c>, falling back to
        /// <see cref="DefaultHotkeys"/> when the stored string is missing or shorter than nineteen
        /// characters (the WinForms code indexed a guaranteed-19-char runtime array; this guard is the
        /// cross-platform-robust equivalent).
        /// </summary>
        private void LoadButtonText()
        {
            string hotkeys = Properties.Settings.Default.setKey_hotkeys;
            if (string.IsNullOrEmpty(hotkeys) || hotkeys.Length < _shortcutButtons.Length)
            {
                hotkeys = DefaultHotkeys;
            }

            for (int i = 0; i < _shortcutButtons.Length; i++)
            {
                _shortcutButtons[i].Content = hotkeys[i].ToString();
            }
        }

        /// <summary>
        /// [XPLAT] Parity with <c>Form_Keys.Form_Keys_FormClosing</c>: persist the current button contents
        /// on close. Routed through <see cref="PersistHotkeys"/> (persist-once), so a close via the window
        /// button or Escape saves the same final string — and applies the same runtime refresh — that
        /// <see cref="OnOkClick"/> would.
        /// </summary>
        /// <param name="e">The window-closing event data.</param>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            PersistHotkeys();
            base.OnClosing(e);
        }

        /// <summary>
        /// [XPLAT] Builds the nineteen-character hotkey string by concatenating the button contents in order
        /// (parity with the WinForms <c>FormClosing</c> concatenation), persists it to
        /// <c>Properties.Settings.Default.setKey_hotkeys</c>, calls <c>Save()</c>, and propagates it to the
        /// running application through <see cref="_applyHotkeys"/>. The <see cref="_persisted"/> guard makes
        /// this idempotent so the OK path (which calls this then closes) and the close path do not double-save.
        /// </summary>
        private void PersistHotkeys()
        {
            if (_persisted)
            {
                return;
            }
            _persisted = true;

            StringBuilder builder = new StringBuilder(_shortcutButtons.Length);
            foreach (Button button in _shortcutButtons)
            {
                builder.Append(GetContent(button));
            }
            string newHotkeys = builder.ToString();

            Properties.Settings.Default.setKey_hotkeys = newHotkeys;
            Properties.Settings.Default.Save();

            // [XPLAT] WinForms `mf.hotkeys = Properties.Settings.Default.setKey_hotkeys.ToCharArray();`.
            _applyHotkeys?.Invoke(newHotkeys.ToCharArray());
        }

        /// <summary>
        /// Reads a button's content as a string. The hotkey buttons only ever carry string content
        /// (a single character or the <see cref="Armed"/> marker), so a null/non-string content maps to
        /// <see cref="string.Empty"/> — which never equals <see cref="Armed"/> and contributes nothing to
        /// the persisted concatenation.
        /// </summary>
        /// <param name="button">The button whose content to read.</param>
        /// <returns>The button content as a string, or <see cref="string.Empty"/>.</returns>
        private static string GetContent(Button button)
        {
            return button.Content as string ?? string.Empty;
        }
    }
}
