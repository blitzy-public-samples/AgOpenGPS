// [XPLAT] migrated from net48/WinForms FormAdvancedSettings.cs + FormAdvancedSettings.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.ViewModels;
using AgIO.Properties;

namespace AgIO.Views
{
    /// <summary>
    /// MVVM view-model backing <c>FormAdvancedSettings.axaml</c>, the AgIO Advanced Settings
    /// dialog. It replaces the WinForms <c>FormAdvancedSettings</c> — a self-contained toggle
    /// panel (no <c>FormLoop mf</c> back-reference) that reads and writes three
    /// <see cref="Settings"/> boolean display flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] The WinForms original lived in the anomalous <c>AgIO.Forms</c> namespace; this
    /// view-model is normalized to the <c>AgIO.Views</c> convention used by every re-platformed
    /// AgIO dialog. The three WinForms <c>CheckBox</c> controls (<c>cboxStartMinimized</c>,
    /// <c>cboxAutoRunGPS_Out</c>, <c>cboxShowOnWarning</c>) become the bindable
    /// <see cref="StartMinimized"/>, <see cref="AutoRunGpsOut"/>, and <see cref="ShowOnWarning"/>
    /// properties, and the single <c>btnClose</c> OK button maps to <see cref="SaveCommand"/>.
    /// </para>
    /// <para>
    /// <b>Behavior parity.</b> The original wrote each flag to <see cref="Settings.Default"/> live
    /// on every <c>CheckedChanged</c> and persisted them with <c>Settings.Default.Save()</c> in
    /// <c>btnClose_Click</c>. This view-model reproduces that 1:1: each bound property setter writes
    /// its flag to <see cref="Settings.Default"/> immediately (mirroring the per-checkbox
    /// <c>CheckedChanged</c> handlers), and <see cref="SaveCommand"/> simply persists with
    /// <see cref="Settings.Save"/> and closes — the direct map of <c>btnClose_Click</c>. There is no
    /// cancel path: exactly like the WinForms original, the dialog exposes only the OK/Save button.
    /// Dialog dismissal is delegated to the hosting Avalonia <c>Window</c> through the
    /// <see cref="RequestClose"/> event (replacing the WinForms <c>Close()</c>).
    /// </para>
    /// </remarks>
    public class FormAdvancedSettingsViewModel : ViewModel
    {
        // [XPLAT] Backing fields seeded from Settings.Default in the constructor. Each property
        // setter writes its flag straight back to Settings.Default on change (parity with the
        // WinForms CheckedChanged handlers); SaveCommand then persists with Settings.Default.Save().
        private bool _autoRunGpsOut;
        private bool _startMinimized;
        private bool _showOnWarning;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormAdvancedSettingsViewModel"/> class,
        /// loading the current display flags from <see cref="Settings.Default"/> into the bound
        /// properties and wiring the Save/Cancel commands. Mirrors the WinForms constructor, which
        /// seeded its three check boxes from the same settings keys.
        /// </summary>
        public FormAdvancedSettingsViewModel()
        {
            // [XPLAT] Parity: seed the editable state from the persisted settings, exactly as the
            // WinForms constructor set cbox*.Checked from these same Settings.Default keys.
            _autoRunGpsOut = Settings.Default.setDisplay_isAutoRunGPS_Out;
            _startMinimized = Settings.Default.setDisplay_StartMinimized;
            _showOnWarning = Settings.Default.setDisplay_ShowOnWarning;

            // [XPLAT] btnClose (OK) -> SaveCommand (persist + close). Parity with the WinForms
            // original, which exposed a single OK/close button and had no cancel.
            SaveCommand = new RelayCommand(OnSave);
        }

        /// <summary>
        /// Gets or sets a value indicating whether AgIO automatically starts GPS-Out. Two-way
        /// bound to the former <c>cboxAutoRunGPS_Out</c> check box and persisted to
        /// <c>Settings.Default.setDisplay_isAutoRunGPS_Out</c> on save. The equality guard
        /// suppresses redundant change notifications.
        /// </summary>
        public bool AutoRunGpsOut
        {
            get => _autoRunGpsOut;
            set
            {
                if (_autoRunGpsOut != value)
                {
                    _autoRunGpsOut = value;
                    // [XPLAT] Parity with WinForms cboxAutoRunGPS_Out_CheckedChanged: write the flag
                    // to Settings.Default live on every toggle (persisted later by SaveCommand).
                    Settings.Default.setDisplay_isAutoRunGPS_Out = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether AgIO starts minimized. Two-way bound to the
        /// former <c>cboxStartMinimized</c> check box and persisted to
        /// <c>Settings.Default.setDisplay_StartMinimized</c> on save. The equality guard
        /// suppresses redundant change notifications.
        /// </summary>
        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (_startMinimized != value)
                {
                    _startMinimized = value;
                    // [XPLAT] Parity with WinForms cboxStartMinimized_CheckedChanged: write the flag
                    // to Settings.Default live on every toggle (persisted later by SaveCommand).
                    Settings.Default.setDisplay_StartMinimized = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether AgIO pops up to the foreground on a warning.
        /// Two-way bound to the former <c>cboxShowOnWarning</c> check box and persisted to
        /// <c>Settings.Default.setDisplay_ShowOnWarning</c> on save. The equality guard
        /// suppresses redundant change notifications.
        /// </summary>
        public bool ShowOnWarning
        {
            get => _showOnWarning;
            set
            {
                if (_showOnWarning != value)
                {
                    _showOnWarning = value;
                    // [XPLAT] Parity with WinForms cboxShowOnWarning_CheckedChanged: write the flag
                    // to Settings.Default live on every toggle (persisted later by SaveCommand).
                    Settings.Default.setDisplay_ShowOnWarning = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that persists the (already-applied) display flags with
        /// <see cref="Settings.Save"/> and raises <see cref="RequestClose"/> with <c>true</c>.
        /// Parity with the WinForms <c>btnClose_Click</c> handler
        /// (<c>Settings.Default.Save(); Close();</c>); the individual flags were written to
        /// <see cref="Settings.Default"/> live by the property setters.
        /// </summary>
        public RelayCommand SaveCommand { get; }

        /// <summary>
        /// Raised when the dialog should close after a save. The argument is the dialog result —
        /// always <c>true</c>, since <see cref="SaveCommand"/> is the only close path (the WinForms
        /// original exposed a single OK/close button and had no cancel). The hosting Avalonia
        /// <c>Window</c> subscribes to this event and closes itself with the supplied result,
        /// replacing the WinForms <c>Close()</c>. Initialized to a no-op delegate so it is always
        /// safe to raise without a null check (nullable reference types are disabled project-wide).
        /// </summary>
        public event Action<bool> RequestClose = delegate { };

        /// <summary>
        /// Persists the display flags (already applied to <see cref="Settings.Default"/> by the
        /// property setters) and requests the dialog close with a positive result. Reproduces the
        /// WinForms <c>btnClose_Click</c> save-and-close behavior.
        /// </summary>
        private void OnSave()
        {
            // [XPLAT] Parity with WinForms btnClose_Click (Settings.Default.Save(); Close();). The
            // three flags were already written to Settings.Default live by the property setters
            // (mirroring the CheckedChanged handlers), so Save only persists and then closes.
            Settings.Default.Save();
            RequestClose(true);
        }
    }
}
