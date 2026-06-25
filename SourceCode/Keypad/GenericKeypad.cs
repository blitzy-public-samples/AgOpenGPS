// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;

namespace Keypad
{
    /// <summary>
    /// Base on-screen keypad control shared by <c>NumKeypad</c> and <c>Keyboard</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Re-expressed from the former WinForms <c>System.Windows.Forms.UserControl</c> base as a
    /// code-only Avalonia <see cref="UserControl"/>. It intentionally has no <c>.axaml</c> of its own and
    /// declares no visual children — the original WinForms designer only set scaling/name/size, which the
    /// derived controls now own. <c>NumKeypad</c> and <c>Keyboard</c> supply their own XAML and
    /// use this type as their XAML root element (the standard Avalonia UserControl-inheritance pattern).
    /// The public input-broadcast contract (<see cref="ButtonPressed"/> + <see cref="RaiseButtonPressed"/>)
    /// is preserved so the GPS/AgIO numeric and keyboard consumers keep behaving identically after migration.
    /// </remarks>
    public class GenericKeypad : UserControl
    {
        /// <summary>
        /// Raised whenever a keypad button is pressed, carrying the emitted character in
        /// <see cref="KeypadKeyPressedEventArgs.KeyChar"/>. Replaces the former WinForms
        /// <c>KeyPressEventHandler</c>/<c>KeyPressEventArgs</c> pair, which are unavailable on net8.0 Avalonia.
        /// </summary>
        public event EventHandler<KeypadKeyPressedEventArgs> ButtonPressed;

        /// <summary>
        /// Broadcasts a single character to subscribers of <see cref="ButtonPressed"/>.
        /// </summary>
        /// <param name="whatToSend">
        /// The character to emit. Derived keypads send digit and command tokens
        /// (for example digits, <c>'B'</c>, <c>'.'</c>, <c>'-'</c>, <c>'X'</c>, <c>'C'</c>, <c>'K'</c>);
        /// the keyboard additionally sends letters and control characters.
        /// </param>
        /// <remarks>
        /// The name and signature are kept identical to the WinForms original so <c>NumKeypad</c> and
        /// <c>Keyboard</c> call it unchanged. The null-conditional invoke mirrors the original explicit
        /// null check on the backing delegate.
        /// </remarks>
        public void RaiseButtonPressed(char whatToSend)
        {
            ButtonPressed?.Invoke(this, new KeypadKeyPressedEventArgs(whatToSend));
        }
    }

    /// <summary>
    /// Event data for <see cref="GenericKeypad.ButtonPressed"/>, exposing the pressed character.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Cross-platform replacement for <c>System.Windows.Forms.KeyPressEventArgs</c>. The property is
    /// deliberately named <see cref="KeyChar"/> so the reimplemented Avalonia consumers in GPS and AgIO
    /// (<c>FormNumeric</c>/<c>FormKeyboard</c>) keep reading <c>e.KeyChar</c> without any logic change.
    /// </remarks>
    public class KeypadKeyPressedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="KeypadKeyPressedEventArgs"/> class.
        /// </summary>
        /// <param name="keyChar">The character emitted by the pressed keypad button.</param>
        public KeypadKeyPressedEventArgs(char keyChar)
        {
            KeyChar = keyChar;
        }

        /// <summary>
        /// Gets the character emitted by the pressed keypad button.
        /// </summary>
        public char KeyChar { get; }
    }
}
