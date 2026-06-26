// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    // [XPLAT] IErrorPresenter impl; FormTimedMessage → FormTimedMessageView (non-modal, Dispatcher-marshaled).
    /// <summary>
    /// Avalonia implementation of the Core <see cref="IErrorPresenter"/> contract. It backs the transient
    /// "timed message" UX that replaces the deleted WinForms <c>FormTimedMessage</c>: a borderless,
    /// top-most popup that shows a bold title over a message and dismisses itself after a caller-supplied
    /// interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This class is the cross-platform replacement for the WinForms
    /// <c>FormGPS.TimedMessageBox(msec, title, message)</c> helper, which constructed a
    /// <c>FormTimedMessage</c> and showed it non-modally so it never blocked the caller. Here the single
    /// <see cref="PresentTimedMessage(TimeSpan, string, string)"/> entry point constructs the migrated
    /// <see cref="FormTimedMessageView"/> (the Avalonia <see cref="Window"/> in this folder, which owns
    /// its own auto-close <c>DispatcherTimer</c>) and shows it with <see cref="Window.Show()"/> /
    /// <see cref="Window.Show(Window)"/> — never <see cref="Window.ShowDialog(Window)"/> — preserving the
    /// fire-and-forget, non-blocking behaviour of the original, which displayed and auto-disposed after
    /// the interval elapsed.
    /// </para>
    /// <para>
    /// It is constructed by <c>App.axaml.cs</c> and passed as the error-presenter argument to
    /// <c>new ApplicationCore(baseDir, panelPresenter, errorPresenter)</c>, replacing the legacy
    /// <c>(dir, null, null)</c> null-wiring. Because the presenter is created before the main window
    /// exists, the parent window is supplied afterwards through the settable <see cref="Owner"/> property.
    /// </para>
    /// <para>
    /// All show work is marshalled onto the Avalonia UI thread via <see cref="Dispatcher.UIThread"/>
    /// (the Avalonia equivalent of the WinForms <c>InvokeRequired</c>/<c>BeginInvoke</c> pattern), so the
    /// presenter is safe to call from the non-UI callers that surface errors — the extracted services and
    /// the real-time scan loop. Presenting a transient notification is best-effort: every step is wrapped
    /// defensively so that a failure to show a message can never crash the application — any failure is
    /// logged through <see cref="Log.EventWriter(string)"/> and otherwise swallowed.
    /// </para>
    /// </remarks>
    public class AvaloniaErrorPresenter : IErrorPresenter
    {
        /// <summary>
        /// Gets or sets the window that owns (parents) the transient message popups. It is assigned by
        /// <c>App.axaml.cs</c> once <c>MainView</c> exists; the presenter itself is created earlier, so it
        /// starts out <see langword="null"/>. When set, the popup is shown owned — but still non-modally —
        /// via <see cref="Window.Show(Window)"/>; when <see langword="null"/> it is shown unparented via
        /// <see cref="Window.Show()"/>, matching the WinForms original, which displayed the toast unowned
        /// near the top-left of the screen.
        /// </summary>
        public Window Owner { get; set; }

        /// <summary>
        /// Presents a self-dismissing message popup with the supplied title and body for the given
        /// duration. This is the Core <see cref="IErrorPresenter"/> entry point; it is non-blocking
        /// (fire-and-forget) and safe to call from any thread.
        /// </summary>
        /// <param name="timeSpan">
        /// How long the popup stays visible before it auto-closes. Converted to whole milliseconds for the
        /// <see cref="FormTimedMessageView"/> timer; the <c>(int)</c> cast is culture-independent, so no
        /// <see cref="System.Globalization.CultureInfo"/> handling is required.
        /// </param>
        /// <param name="titleString">The bold title shown at the top of the popup.</param>
        /// <param name="messageString">The message body shown beneath the title.</param>
        public void PresentTimedMessage(TimeSpan timeSpan, string titleString, string messageString)
        {
            try
            {
                // [XPLAT] Compute the interval once, outside the UI-thread closure, to keep the marshalled
                // work allocation-light. The (int) cast is culture-independent (no InvariantCulture
                // concern). FormTimedMessageView itself clamps a non-positive interval to a safe minimum.
                int milliseconds = (int)timeSpan.TotalMilliseconds;

                // [XPLAT] Run inline when already on the UI thread (the common case for UI-originated
                // messages, avoiding a closure allocation); otherwise post to the dispatcher. This is the
                // Avalonia equivalent of the WinForms InvokeRequired/BeginInvoke marshalling.
                if (Dispatcher.UIThread.CheckAccess())
                {
                    ShowTimedMessage(milliseconds, titleString, messageString);
                }
                else
                {
                    Dispatcher.UIThread.Post(() => ShowTimedMessage(milliseconds, titleString, messageString));
                }
            }
            catch (Exception ex)
            {
                // Dispatching a transient notification must never bubble an exception back to the caller
                // (for example, the real-time scan loop). Log best-effort and continue.
                SafeLog("PresentTimedMessage failed to dispatch", ex);
            }
        }

        /// <summary>
        /// Constructs and shows the <see cref="FormTimedMessageView"/> popup. Always invoked on the
        /// Avalonia UI thread (see <see cref="PresentTimedMessage(TimeSpan, string, string)"/>). The view
        /// is shown <em>non-modally</em> so the call returns immediately, mirroring the WinForms
        /// <c>FormTimedMessage</c>, which displayed and auto-disposed without blocking.
        /// </summary>
        /// <param name="milliseconds">Auto-close interval, in milliseconds.</param>
        /// <param name="titleString">The popup title.</param>
        /// <param name="messageString">The popup message body.</param>
        private void ShowTimedMessage(int milliseconds, string titleString, string messageString)
        {
            FormTimedMessageView view;
            try
            {
                view = new FormTimedMessageView(milliseconds, titleString, messageString);
            }
            catch (Exception ex)
            {
                // A failure to even build the popup must not crash the app; drop the message.
                SafeLog("could not construct FormTimedMessageView", ex);
                return;
            }

            // [XPLAT] Non-modal show only — never ShowDialog. Prefer the owned overload so the toast
            // tracks the main window; fall back to an unparented Show() when no owner is set (parity with
            // the original, which showed the popup unowned).
            Window owner = Owner;
            if (owner != null)
            {
                try
                {
                    view.Show(owner);
                    return;
                }
                catch (Exception ex)
                {
                    // An owner that is not yet visible (or is closing) can make the owned overload throw;
                    // fall through to an unparented show so the message is still delivered.
                    SafeLog("owned Show(Owner) failed; retrying unparented", ex);
                }
            }

            try
            {
                view.Show();
            }
            catch (Exception ex)
            {
                SafeLog("Show() failed; message not displayed", ex);
            }
        }

        /// <summary>
        /// Writes a best-effort diagnostic line for a failure encountered while presenting a transient
        /// message. The logging call is itself wrapped so that an unavailable or failing logger can never
        /// turn a swallowed UI failure into a crash.
        /// </summary>
        /// <param name="context">Short description of the operation that failed.</param>
        /// <param name="ex">The exception that was caught.</param>
        private static void SafeLog(string context, Exception ex)
        {
            try
            {
                Log.EventWriter("AvaloniaErrorPresenter: " + context + " -> " + ex.Message);
            }
            catch
            {
                // Intentionally ignored: this is the last line of a best-effort error path and must not
                // throw — there is nowhere left to report a logging failure.
            }
        }
    }
}
