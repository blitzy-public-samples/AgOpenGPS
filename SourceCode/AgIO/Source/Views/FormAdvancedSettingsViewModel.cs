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
    /// <c>btnClose_Click</c>. This view-model instead loads the current values into backing fields
    /// in the constructor and defers the write-back to <see cref="SaveCommand"/>, which writes all
    /// three flags and calls <see cref="Settings.Save"/> — preserving the exact three settings keys
    /// and the save semantics. Dialog dismissal is delegated to the hosting Avalonia <c>Window</c>
    /// through the <see cref="RequestClose"/> event (replacing the WinForms <c>Close()</c> /
    /// <c>DialogResult</c>), and an explicit <see cref="CancelCommand"/> dismisses the dialog
    /// without persisting — the only behavioral refinement introduced by the migration.
    /// </para>
    /// </remarks>
    public class FormAdvancedSettingsViewModel : ViewModel
    {
        // [XPLAT] Backing fields seeded from Settings.Default in the constructor and flushed back
        // to Settings.Default by OnSave. The Cancel path leaves Settings.Default untouched.
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

            // [XPLAT] btnClose (OK) -> SaveCommand (persist + close true); Cancel -> CancelCommand
            // (close false, no persist).
            SaveCommand = new RelayCommand(OnSave);
            CancelCommand = new RelayCommand(OnCancel);
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
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that writes the three display flags back to
        /// <see cref="Settings.Default"/>, persists them with <see cref="Settings.Save"/>, and
        /// raises <see cref="RequestClose"/> with <c>true</c>. Parity with the WinForms
        /// <c>btnClose_Click</c> handler (<c>Settings.Default.Save(); Close();</c>).
        /// </summary>
        public RelayCommand SaveCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without persisting any change by raising
        /// <see cref="RequestClose"/> with <c>false</c>. This is the migration's explicit cancel
        /// path; the WinForms original had no cancel button.
        /// </summary>
        public RelayCommand CancelCommand { get; }

        /// <summary>
        /// Raised when the dialog should close. The argument is the dialog result: <c>true</c>
        /// when the operator saved (via <see cref="SaveCommand"/>) and <c>false</c> when the
        /// operator cancelled (via <see cref="CancelCommand"/>). The hosting Avalonia
        /// <c>Window</c> subscribes to this event and closes itself with the supplied result,
        /// replacing the WinForms <c>Close()</c> / <c>DialogResult</c>. Initialized to a no-op
        /// delegate so it is always safe to raise without a null check (nullable reference types
        /// are disabled project-wide).
        /// </summary>
        public event Action<bool> RequestClose = delegate { };

        /// <summary>
        /// Writes the three display flags back to <see cref="Settings.Default"/>, persists them,
        /// and requests the dialog close with a positive result. Reproduces the WinForms
        /// <c>btnClose_Click</c> save-and-close behavior.
        /// </summary>
        private void OnSave()
        {
            // [XPLAT] Flush the edited state back to the persisted settings using the exact three
            // keys carried over from the WinForms original.
            Settings.Default.setDisplay_isAutoRunGPS_Out = AutoRunGpsOut;
            Settings.Default.setDisplay_StartMinimized = StartMinimized;
            Settings.Default.setDisplay_ShowOnWarning = ShowOnWarning;
            Settings.Default.Save();

            RequestClose(true);
        }

        /// <summary>
        /// Requests the dialog close with a negative result, leaving <see cref="Settings.Default"/>
        /// unchanged.
        /// </summary>
        private void OnCancel()
        {
            RequestClose(false);
        }
    }
}
