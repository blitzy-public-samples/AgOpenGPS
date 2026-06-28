// [XPLAT] migrated from net48/WinForms FormYes — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// Display-only view-model backing <c>FormYes.axaml</c>, the generic confirmation
    /// dialog that replaces the WinForms <c>FormYes</c> / <c>MessageBox.Show(...)</c>
    /// Yes/OK + Cancel prompts. Callers include the profile-overwrite confirmation in
    /// <c>FormProfiles</c> and the restart confirmations in <c>FormEthernet</c>,
    /// <c>FormUDP</c>, and <c>FormSerialPass</c> (the former <c>mf.YesMessageBox(...)</c>).
    /// </summary>
    /// <remarks>
    /// This view-model intentionally owns no command or result state. The dialog outcome
    /// (OK/Yes vs. Cancel) is returned by the hosting <c>Window</c> itself through
    /// <c>Close(true)</c> / <c>Close(false)</c> in the code-behind. The view-model only
    /// supplies the immutable display data: the confirmation message and whether the
    /// Cancel button is shown.
    /// </remarks>
    public class FormYesViewModel : ViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormYesViewModel"/> class.
        /// </summary>
        /// <param name="messageStr">The confirmation message shown to the operator.</param>
        /// <param name="showCancel">
        /// <c>true</c> to show the Cancel button (a Yes/No style prompt); <c>false</c> for
        /// an acknowledge-only (OK) prompt. Mirrors the WinForms <c>showCancel</c> flag.
        /// </param>
        public FormYesViewModel(string messageStr, bool showCancel)
        {
            MessageText = messageStr;
            ShowCancel = showCancel;
        }

        /// <summary>
        /// Gets the confirmation message displayed in the body of the dialog.
        /// </summary>
        public string MessageText { get; }

        /// <summary>
        /// Gets a value indicating whether the Cancel button is visible. When
        /// <c>false</c>, only the OK/Yes button is presented.
        /// </summary>
        public bool ShowCancel { get; }
    }
}
