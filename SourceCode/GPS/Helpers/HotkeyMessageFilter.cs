// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AgOpenGPS.Helpers
{
    /// <summary>
    /// Cross-platform, application-wide hotkey router for the AgOpenGPS main window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This type replaces the former WinForms <c>System.Windows.Forms.IMessageFilter</c>
    /// implementation, which intercepted <c>WM_KEYDOWN</c>/<c>WM_SYSKEYDOWN</c> at the message-pump
    /// level and was hard-coupled to <c>FormGPS</c>. Avalonia has no <c>IMessageFilter</c>; instead the
    /// main window forwards its tunneling (preview) <see cref="Avalonia.Input.InputElement.KeyDownEvent"/>
    /// to this router so an app-wide hotkey is observed <em>before</em> the focused control consumes it,
    /// reproducing the original global interception. A single Avalonia <c>KeyDown</c> already fires for
    /// both ordinary and system (Alt-combo) key presses, so it covers what the two window messages did.
    /// </para>
    /// <para>
    /// The router enforces exactly one policy and holds no key-to-action mappings of its own:
    /// </para>
    /// <list type="number">
    ///   <item><description>When <see cref="Enabled"/> is <see langword="false"/>, nothing is consumed
    ///   (mirrors the old <c>_hotkeyFilter.Enabled</c> gate that stayed off until the UI was ready).</description></item>
    ///   <item><description>When keyboard focus is inside a text-editing control, the key is ignored so it
    ///   reaches the editor (parity with the old WinForms <c>TextBoxBase</c> walk).</description></item>
    ///   <item><description>Otherwise the key and its modifiers are forwarded to the injected app-wide
    ///   handler, and the event is marked handled exactly when that handler reports it consumed the key
    ///   (parity with <c>FormGPS.ProcessCmdKey</c> returning <see langword="true"/>).</description></item>
    /// </list>
    /// <para>
    /// <b>Wiring for the Avalonia <c>Views/MainView</c> consumer</b> (replaces the old <c>FormGPS</c>
    /// <c>OnHandleCreated</c>/<c>OnShown</c>/<c>OnFormClosed</c> setup):
    /// </para>
    /// <code>
    /// // Construct with the migrated FormGPS.ProcessCmdKey mapping as the delegate body:
    /// _hotkeyFilter = new HotkeyMessageFilter(onAppWideKey: (key, mods) =&gt; /* map key (+ mods) to an action; return true if consumed */)
    /// {
    ///     Enabled = false, // keep off until the window is shown/ready (was FormGPS.OnShown -&gt; _uiReady)
    /// };
    ///
    /// // Observe hotkeys before the focused control by subscribing to the TUNNELING (preview) KeyDown:
    /// this.AddHandler(InputElement.KeyDownEvent, _hotkeyFilter.OnPreviewKeyDown, RoutingStrategies.Tunnel);
    ///
    /// // When the window is shown/ready:
    /// _hotkeyFilter.Enabled = true;
    ///
    /// // On window close (symmetric to the old OnFormClosed cleanup):
    /// this.RemoveHandler(InputElement.KeyDownEvent, _hotkeyFilter.OnPreviewKeyDown);
    /// _hotkeyFilter.Dispose();
    /// </code>
    /// </remarks>
    public sealed class HotkeyMessageFilter : IDisposable
    {
        // [XPLAT] Replaces the FormGPS '_mf' god-object back-reference. The router no longer knows about
        // FormGPS or any concrete view; it forwards to an injected handler instead, decoupling the
        // input-routing policy from the (now Avalonia) UI shell. The delegate is supplied by Views/MainView
        // and carries the migrated FormGPS.ProcessCmdKey key-to-action mapping.
        private readonly Func<Key, KeyModifiers, bool> _onAppWideKey;

        /// <summary>
        /// Creates the app-wide hotkey router.
        /// </summary>
        /// <param name="onAppWideKey">
        /// Handler that maps a pressed key (plus modifiers) to an application action and returns
        /// <see langword="true"/> if it consumed the key. This is the cross-platform replacement for the
        /// former <c>FormGPS.HandleAppWideKey</c>/<c>ProcessCmdKey</c> path; the Avalonia <c>Views/MainView</c>
        /// supplies it.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="onAppWideKey"/> is <see langword="null"/>.</exception>
        public HotkeyMessageFilter(Func<Key, KeyModifiers, bool> onAppWideKey)
            => _onAppWideKey = onAppWideKey ?? throw new ArgumentNullException(nameof(onAppWideKey));

        /// <summary>
        /// Gets or sets whether the router is active. When <see langword="false"/> no key is ever consumed.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Cross-platform analogue of the old <c>_hotkeyFilter.Enabled</c> flag. <c>Views/MainView</c>
        /// keeps this <see langword="false"/> until the window is shown/ready, then sets it <see langword="true"/>
        /// (mirroring <c>FormGPS.OnShown</c>), preserving the "do not fire hotkeys before the UI is ready" gate.
        /// </remarks>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Tunneling <see cref="Avalonia.Input.KeyEventArgs"/> handler. Subscribe this to the MAIN WINDOW's
        /// tunneling <c>KeyDown</c> so hotkeys are observed before focused controls consume them:
        /// <code>window.AddHandler(InputElement.KeyDownEvent, filter.OnPreviewKeyDown, RoutingStrategies.Tunnel);</code>
        /// </summary>
        /// <param name="sender">The element the handler was attached to (normally the window).</param>
        /// <param name="e">
        /// The key event. <see cref="Avalonia.Interactivity.RoutedEventArgs.Handled"/> is set to
        /// <see langword="true"/> exactly when the key is consumed as an app-wide hotkey.
        /// </param>
        public void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Process(sender, e))
                e.Handled = true; // consume the key exactly like ProcessCmdKey returning true did
        }

        /// <summary>
        /// Core routing logic, separated from <see cref="OnPreviewKeyDown"/> so it can be unit-tested
        /// without a live routed event.
        /// </summary>
        /// <param name="sender">The element the event was raised on (used to resolve the focused control).</param>
        /// <param name="e">The key event to route.</param>
        /// <returns>
        /// <see langword="true"/> when the injected app-wide handler consumed the key; otherwise
        /// <see langword="false"/> (including when the router is disabled, the event is <see langword="null"/>,
        /// or the user is typing in a text control).
        /// </returns>
        public bool Process(object sender, KeyEventArgs e)
        {
            if (!Enabled) return false;
            if (e == null) return false;

            // If the user is typing in a text input, ignore hotkeys (parity with the WinForms TextBoxBase check).
            if (IsTypingContext(sender, e)) return false;

            // Forward to the app-wide handler (the migrated ProcessCmdKey mapping, supplied by MainView).
            // Both the key and its modifiers are passed so the delegate has strictly more information than
            // the old code, which pre-stripped modifiers; the delegate replicates the original comparison.
            return _onAppWideKey(e.Key, e.KeyModifiers);
        }

        /// <summary>
        /// Determines whether keyboard focus is currently inside a text-editing control, in which case
        /// hotkeys must be suppressed so the keystroke reaches the editor.
        /// </summary>
        /// <remarks>
        /// [XPLAT] Replaces the WinForms <c>Form.ActiveForm</c>/<c>ActiveControl</c>/<c>TextBoxBase</c> walk.
        /// Avalonia <see cref="Avalonia.Controls.TextBox"/> is the editable text input (and <c>MaskedTextBox</c>
        /// derives from it); for composite editors (for example <c>AutoCompleteBox</c> or <c>NumericUpDown</c>)
        /// the focused element is an inner <see cref="Avalonia.Controls.TextBox"/>, which this upward
        /// visual-tree walk still catches — preserving the original "suppress while typing" intent.
        /// </remarks>
        /// <param name="sender">The element the event was raised on; used to find the owning top level.</param>
        /// <param name="e">The key event, whose source is used as a fallback when no top level is found.</param>
        /// <returns><see langword="true"/> when focus is in (or within) a text-editing control.</returns>
        private static bool IsTypingContext(object sender, KeyEventArgs e)
        {
            // Resolve the currently focused element via the top level's focus manager,
            // falling back to the routed event source.
            TopLevel topLevel = sender is Visual senderVisual ? TopLevel.GetTopLevel(senderVisual) : null;
            if (topLevel == null && e.Source is Visual sourceVisual)
                topLevel = TopLevel.GetTopLevel(sourceVisual);

            IInputElement focused = topLevel?.FocusManager?.GetFocusedElement() ?? e.Source as IInputElement;

            // Walk up the visual tree from the focused element looking for a text-editing control.
            Visual current = focused as Visual;
            while (current != null)
            {
                if (current is TextBox) return true; // Avalonia TextBox is the WinForms TextBoxBase equivalent
                current = current.GetVisualParent();
            }
            return false;
        }

        /// <summary>
        /// No-op dispose. The router holds no unmanaged resources; it is retained so the owning window can
        /// <see cref="Dispose"/> it on close, symmetric to the old WinForms <c>OnFormClosed</c> cleanup.
        /// The window also unsubscribes the tunneling <c>KeyDown</c> handler it added.
        /// </summary>
        public void Dispose() { }
    }
}
