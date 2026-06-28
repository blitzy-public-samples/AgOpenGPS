// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Translations;
using Keypad;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the on-screen QWERTY keyboard text-entry dialog — the behaviour half of
    /// <c>FormKeyboard.axaml</c>. This is a 1:1 behavioural-parity reimplementation of the WinForms
    /// <c>Forms/Inputs/FormKeyboard</c> (FormKeyboard.cs + FormKeyboard.Designer.cs): the behaviour is
    /// unchanged, only the framework underneath it (AAP §0.7.1, §0.3.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an imperative dialog (no view-model, no data binding). It hosts the migrated
    /// <c>Keypad.Keyboard</c> on-screen keyboard (<c>keyboard1</c>), a single large editable
    /// <see cref="TextBox"/> (<c>keyboardString</c>), and two caret-navigation arrow buttons
    /// (<c>btnCharLeft</c>/<c>btnCharRight</c>) — all declared with those exact <c>x:Name</c>s in the
    /// paired XAML. Every handler is wired in the constructor, the same pattern used by the other
    /// non-MVVM dialogs in <c>AgOpenGPS.Views</c>.
    /// </para>
    /// <para>
    /// Framework conversions (each tagged <c>// [XPLAT]</c> below): the WinForms
    /// <c>KeyPressEventHandler</c>/<c>KeyPressEventArgs</c> pair is replaced by
    /// <see cref="GenericKeypad.ButtonPressed"/> carrying <see cref="KeypadKeyPressedEventArgs.KeyChar"/>;
    /// the WinForms <c>TextBox.SelectionStart</c> + <c>SelectionLength = 0</c> caret model is replaced by
    /// Avalonia's <see cref="TextBox.CaretIndex"/> (setting it collapses any selection, so the
    /// <c>SelectionLength = 0</c> lines are simply dropped, and Avalonia clamps the value into
    /// <c>[0, Text.Length]</c> so the Insert/Remove index math stays safe); the WinForms <c>Load</c> event
    /// becomes the Avalonia <see cref="Window.OnOpened(EventArgs)"/> override; and the WinForms
    /// <c>DialogResult</c> + <c>Close()</c> becomes the Avalonia modal pattern
    /// (<c>await ShowDialog&lt;string?&gt;(owner)</c>) — OK closes with the entered string, Cancel closes
    /// with <see langword="null"/>.
    /// </para>
    /// </remarks>
    public partial class FormKeyboard : Window
    {
        /// <summary>
        /// The value the user accepted with the on-screen OK key. Set only on the OK path and also
        /// returned as the <c>ShowDialog&lt;string?&gt;</c> result; mirrors the WinForms
        /// <c>ReturnString</c> property.
        /// </summary>
        public string ReturnString { get; private set; }

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader. Without it the
        /// Avalonia XAML compiler raises AVLN3001 ("XAML resource ... won't be reachable via runtime
        /// loader, as no public constructor was found"), which the Release zero-warning gate forbids
        /// (see MIGRATION_DOCS/CHANGELOG.md, F1-001). The parity constructor below chains to it with
        /// <c>: this()</c> so <c>InitializeComponent()</c> runs exactly once.
        /// </summary>
        public FormKeyboard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the dialog with the text to edit. Parity with the WinForms
        /// <c>FormKeyboard(string currentString)</c> constructor. Chains to the parameterless
        /// constructor (<c>: this()</c>) for <c>InitializeComponent()</c>; consumers normally supply the
        /// current string through this overload.
        /// </summary>
        /// <param name="currentString">The text to pre-fill into the editable field.</param>
        public FormKeyboard(string currentString)
            : this()
        {
            // [XPLAT] WinForms `this.Text = "Enter a Value"` -> Avalonia Window.Title. The window uses
            // SystemDecorations="None", so this title is invisible chrome, but it is carried across for
            // parity and supplies the dialog's accessible name (invisible accessibility — no visual change).
            Title = "Enter a Value";

            // [XPLAT] WinForms ctor `keyboardString.Text = currentString.ToString()`. Avalonia's
            // TextBox.Text is string?, so the (already-string) argument is assigned null-safely; the
            // non-null case is identical to the WinForms behaviour.
            keyboardString.Text = currentString ?? string.Empty;

            // [XPLAT] WinForms designer `keyboard1.ButtonPressed += new KeyPressEventHandler(...)`. The
            // cross-platform Keypad.GenericKeypad raises ButtonPressed with KeypadKeyPressedEventArgs
            // (replacing KeyPressEventArgs); the character is still read via e.KeyChar.
            keyboard1.ButtonPressed += Keyboard1_ButtonPressed;
            btnCharLeft.Click += BtnCharLeft_Click;
            btnCharRight.Click += BtnCharRight_Click;

            // [XPLAT] WinForms FormKeyboard_Load French-culture height special-case (575 for "fr", else
            // 500). Set here in the constructor — rather than in OnOpened — so the window opens at the
            // correct size without a visible resize.
            Height = (Thread.CurrentThread.CurrentCulture.Name == "fr") ? 575 : 500;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormKeyboard_Load</c> -> Avalonia <see cref="Window.OnOpened(EventArgs)"/>:
        /// place the caret at the end of the pre-filled text and move focus to the on-screen keyboard.
        /// (The French-height special-case from the original Load handler is applied in the constructor.)
        /// </summary>
        /// <param name="e">The event data passed to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // [XPLAT] WinForms `keyboardString.SelectionStart = keyboardString.Text.Length;
            // keyboardString.SelectionLength = 0;` -> set CaretIndex to the end (the SelectionLength=0 is
            // implicit because setting CaretIndex collapses any selection). Text is string? on Avalonia.
            keyboardString.CaretIndex = (keyboardString.Text ?? string.Empty).Length;
            keyboard1.Focus();
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms
        /// <c>RegisterKeyboard1_ButtonPressed(object, KeyPressEventArgs)</c>. Reproduces the exact token
        /// dispatch on <see cref="KeypadKeyPressedEventArgs.KeyChar"/>: backspace (<c>\u0008</c>), cancel
        /// (<c>\u0027</c>), clear (<c>\u0005</c>), OK (<c>\u0004</c>), or otherwise insert the character at
        /// the caret.
        /// </summary>
        /// <param name="sender">The on-screen keyboard control raising the event (unused).</param>
        /// <param name="e">The pressed-key data carrying the emitted character in <c>e.KeyChar</c>.</param>
        private void Keyboard1_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            // Clear the error placeholder as the user enters new values (parity with WinForms).
            if (keyboardString.Text == gStr.gsError)
            {
                keyboardString.Text = "";
            }

            // [XPLAT] Avalonia TextBox.Text is string?; capture a null-safe snapshot for the .Length/
            // .Remove/.Insert math below. Nothing else mutates the text within this single handler call,
            // so this snapshot matches the WinForms fresh reads exactly.
            string text = keyboardString.Text ?? string.Empty;

            // Backspace key — remove one char to the left of the caret.
            if (e.KeyChar == '\u0008')
            {
                if (text.Length > 0)
                {
                    // [XPLAT] WinForms SelectionStart -> Avalonia CaretIndex.
                    int caret = keyboardString.CaretIndex;

                    if (caret > 0)
                    {
                        keyboardString.Text = text.Remove(caret - 1, 1);
                        keyboardString.CaretIndex = caret - 1;
                        keyboardString.Focus();
                    }
                    else
                    {
                        // [XPLAT] Preserve the WinForms caret-at-0 quirk exactly: with text present but
                        // the caret at index 0, the original code does NOT delete a character and instead
                        // sets SelectionStart = 1. Reproduced as CaretIndex = 1 for interaction parity.
                        keyboardString.CaretIndex = 1;
                    }
                }
            }

            // Exit or cancel.
            else if (e.KeyChar == '\u0027')
            {
                // [XPLAT] WinForms `DialogResult = DialogResult.Cancel; Close();` -> Avalonia Close(null).
                // A null result signals cancellation to the ShowDialog<string?> caller. The early return
                // skips the trailing focus call below (the window is closing).
                Close(null);
                return;
            }

            // Clear the whole display.
            else if (e.KeyChar == '\u0005')
            {
                keyboardString.Text = "";
            }

            // OK button — accept and return the value.
            else if (e.KeyChar == '\u0004')
            {
                // [XPLAT] WinForms `ReturnString = keyboardString.Text; DialogResult = DialogResult.OK;
                // Close();` -> set ReturnString then Close(ReturnString). The returned string is the
                // ShowDialog<string?> result (non-null == accepted). The early return skips the trailing
                // focus call below (the window is closing).
                ReturnString = keyboardString.Text ?? string.Empty;
                Close(ReturnString);
                return;
            }

            // Otherwise it is a character — insert it at the caret.
            else
            {
                string insertText = e.KeyChar.ToString();
                int caret = keyboardString.CaretIndex;
                keyboardString.Text = text.Insert(caret, insertText);
                keyboardString.CaretIndex = caret + insertText.Length;
            }

            // Show the cursor (parity with the WinForms trailing `SelectionLength = 0; Focus();`; the
            // SelectionLength reset is implicit under the CaretIndex model).
            keyboardString.Focus();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnCharLeft_Click</c>: move the text caret one position left, clamped at 0.
        /// </summary>
        /// <param name="sender">The left-arrow button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnCharLeft_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] WinForms SelectionStart-- with clamp-to-0 -> CaretIndex.
            int spot = keyboardString.CaretIndex - 1;
            if (spot < 0)
            {
                spot = 0;
            }
            keyboardString.CaretIndex = spot;
            keyboardString.Focus();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnCharRight_Click</c>: move the text caret one position right, clamped at
        /// the end of the text.
        /// </summary>
        /// <param name="sender">The right-arrow button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnCharRight_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] WinForms SelectionStart++ with clamp-to-Text.Length -> CaretIndex.
            int len = (keyboardString.Text ?? string.Empty).Length;
            int spot = keyboardString.CaretIndex + 1;
            if (spot > len)
            {
                spot = len;
            }
            keyboardString.CaretIndex = spot;
            keyboardString.Focus();
        }
    }
}
