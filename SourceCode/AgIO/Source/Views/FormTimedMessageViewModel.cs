// [XPLAT] migrated from net48/WinForms FormTimedMessage — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// Display-only view-model backing <c>FormTimedMessage.axaml</c>, the transient,
    /// self-closing notification window that replaces the WinForms <c>FormTimedMessage</c>
    /// (formerly displayed by <c>TimedMessageBox(msec, title, message)</c>). In the migrated
    /// MVVM stack it is the view presented by a concrete
    /// <c>IErrorPresenter.PresentTimedMessage(...)</c> implementation.
    /// </summary>
    /// <remarks>
    /// This view-model intentionally owns no timer, command, or result state. The auto-dismiss
    /// duration (the former WinForms <c>timer1.Interval</c>) and the window sizing are concerns
    /// of the hosting <c>Window</c>'s code-behind, not of the view-model. The view-model only
    /// supplies the immutable display data that the WinForms original bound to <c>lblTitle</c>
    /// and <c>lblMessage2</c>: the title and the message body.
    /// </remarks>
    public class FormTimedMessageViewModel : ViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormTimedMessageViewModel"/> class.
        /// </summary>
        /// <param name="titleStr">
        /// The title shown at the top of the notification window (the former <c>lblTitle.Text</c>).
        /// </param>
        /// <param name="messageStr">
        /// The message body shown beneath the title (the former <c>lblMessage2.Text</c>).
        /// </param>
        public FormTimedMessageViewModel(string titleStr, string messageStr)
        {
            TitleText = titleStr;
            MessageText = messageStr;
        }

        /// <summary>
        /// Gets the title text displayed at the top of the notification window.
        /// </summary>
        public string TitleText { get; }

        /// <summary>
        /// Gets the message body text displayed beneath the title.
        /// </summary>
        public string MessageText { get; }
    }
}
