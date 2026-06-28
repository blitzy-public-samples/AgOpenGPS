// [XPLAT] migrated from net48/WinForms FormKeyboard.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Keypad;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the AgIO on-screen touch-keyboard text-entry dialog — the behaviour
    /// half of <c>FormKeyboard.axaml</c>. This is a 1:1 behavioural-parity reimplementation of the
    /// deleted WinForms <c>Forms/FormKeyboard : Form</c> (<c>FormKeyboard.cs</c> +
    /// <c>FormKeyboard.Designer.cs</c>): the runtime behaviour is unchanged — only the framework
    /// underneath it moves from Windows Forms to Avalonia (AAP §0.3.3, §0.7.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the imperative GPS sibling, this AgIO dialog is data-bound. Its
    /// <see cref="StyledElement.DataContext"/> is a <see cref="FormKeyboardViewModel"/> that holds the
    /// running entry string (<see cref="FormKeyboardViewModel.EntryText"/>, two-way bound to the
    /// <c>keyboardString</c> TextBox) and reproduces the original
    /// <c>RegisterKeyboard1_ButtonPressed</c> text transitions in
    /// <see cref="FormKeyboardViewModel.AppendKey(char)"/>. This code-behind owns only the dialog
    /// lifecycle and the routing of on-screen-keyboard key presses into the view-model.
    /// </para>
    /// <para>
    /// HARD CONTRACT — the helper <c>AgIO.Controls.TextBoxExtensions.ShowKeyboard</c> opens this dialog
    /// as <c>string result = await dialog.ShowDialog&lt;string&gt;(owner)</c> (where
    /// <c>dialog = new FormKeyboard(currentString)</c>). The result type is <c>string</c> — NOT
    /// <c>string?</c> — because nullable reference types are disabled project-wide; on cancel/close the
    /// generic <c>ShowDialog&lt;string&gt;</c> yields <see langword="null"/> (the default for a
    /// reference type) and the caller performs a runtime null check. Accordingly:
    /// <list type="bullet">
    ///   <item><description>OK (button or the on-screen keyboard's OK command key) closes returning the
    ///   typed string — <c>Close(_vm.GetResult())</c> — the cross-platform equivalent of the WinForms
    ///   <c>ReturnString = keyboardString.Text; DialogResult = DialogResult.OK; Close();</c>.</description></item>
    ///   <item><description>Cancel (button, the on-screen keyboard's Cancel command key, or the title-bar
    ///   close) closes returning <see langword="null"/> — <c>Close(null)</c> — the equivalent of the
    ///   WinForms <c>DialogResult = DialogResult.Cancel; Close();</c>.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Framework conversions (each tagged <c>// [XPLAT]</c> below): the WinForms
    /// <c>KeyPressEventHandler</c>/<c>KeyPressEventArgs</c> pair is replaced by the cross-platform
    /// <see cref="GenericKeypad.ButtonPressed"/> event carrying
    /// <see cref="KeypadKeyPressedEventArgs.KeyChar"/> (the character is still read via <c>e.KeyChar</c>);
    /// the WinForms <c>DialogResult</c> + <c>Close()</c> dismissal becomes the Avalonia modal pattern
    /// (<c>Close(object)</c> feeding <c>ShowDialog&lt;string&gt;</c>). The XAML is loaded with the
    /// explicit <see cref="AvaloniaXamlLoader"/> call used by every sibling AgIO view (<c>FormYes</c>,
    /// <c>FormSource</c>, <c>FormPGN</c>, <c>FormRadioChannel</c>, <c>FormTimedMessage</c>).
    /// </para>
    /// </remarks>
    public partial class FormKeyboard : Window
    {
        // [XPLAT] Command tokens emitted by Keypad.Keyboard, identical to the WinForms
        // KeyPressEventArgs.KeyChar values the original RegisterKeyboard1_ButtonPressed switched on.
        // The OK and Cancel tokens drive the dialog lifecycle here (the view-model deliberately treats
        // them as no-ops); every other character is text editing handled by the view-model.
        private const char OkKey = '\u0004';     // dialog confirm — return the typed string
        private const char CancelKey = '\u0027'; // dialog cancel — return null

        // [XPLAT] The data-binding source for this dialog. Set by the parameterized constructor and read
        // back on confirmation via GetResult() (the WinForms ReturnString = keyboardString.Text).
        private FormKeyboardViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia compiled-XAML loader and the
        /// design-time previewer, and to keep the compiled-XAML resource reachable (otherwise the build
        /// emits warning <c>AVLN3001</c>, which the Release configuration — with
        /// <c>TreatWarningsAsErrors</c> — promotes to an error). Application code always constructs the
        /// dialog through <see cref="FormKeyboard(string)"/>; this mirrors the dual-constructor
        /// convention of the sibling parameterized AgIO views (<c>FormYes</c>, <c>FormSource</c>,
        /// <c>FormTimedMessage</c>, <c>FormRadioChannel</c>).
        /// </summary>
        public FormKeyboard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the dialog with the text to edit and binds it to a freshly built
        /// <see cref="FormKeyboardViewModel"/>. Parity with the WinForms
        /// <c>FormKeyboard(string currentString)</c> constructor (which set
        /// <c>keyboardString.Text = currentString</c>): this is the entry point used by
        /// <c>TextBoxExtensions.ShowKeyboard</c> via <c>new FormKeyboard(currentString)</c>.
        /// </summary>
        /// <param name="currentString">
        /// The text to pre-populate into the entry field. The view-model treats a <see langword="null"/>
        /// value as an empty entry, so the dialog always opens with a valid, editable string.
        /// </param>
        public FormKeyboard(string currentString)
        {
            InitializeComponent();
            _vm = new FormKeyboardViewModel(currentString);
            DataContext = _vm;
        }

        /// <summary>
        /// Loads the compiled XAML for this window and subscribes to the embedded on-screen keyboard's
        /// key broadcasts. Defined explicitly (rather than relying on a generated method) to match the
        /// convention used by the sibling AgIO Avalonia views.
        /// </summary>
        /// <remarks>
        /// [XPLAT] WinForms designer <c>keyboard1.ButtonPressed += new KeyPressEventHandler(...)</c>.
        /// Because this view declares its own <see cref="AvaloniaXamlLoader"/>-based loader, the
        /// <c>x:Name</c>d controls are resolved through <see cref="NameScopeExtensions.FindControl{T}"/>
        /// rather than generated fields. The null guard mirrors the sibling AgIO views and keeps the
        /// design-time previewer safe.
        /// </remarks>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // [XPLAT] The migrated Keypad.GenericKeypad raises ButtonPressed with
            // KeypadKeyPressedEventArgs (replacing the WinForms KeyPressEventArgs); the character is
            // still read via e.KeyChar in the handler below.
            Keyboard keyboard = this.FindControl<Keyboard>("keyboard1");
            if (keyboard != null)
            {
                keyboard.ButtonPressed += Keyboard1_ButtonPressed;
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms
        /// <c>RegisterKeyboard1_ButtonPressed(object, KeyPressEventArgs)</c>. Reproduces the exact token
        /// dispatch on <see cref="KeypadKeyPressedEventArgs.KeyChar"/>: the OK command token confirms the
        /// dialog (returning the typed string), the Cancel command token cancels it (returning
        /// <see langword="null"/>), and every other character — including backspace and clear — is
        /// applied to the running entry text by the view-model.
        /// </summary>
        /// <param name="sender">The on-screen keyboard control raising the event (unused).</param>
        /// <param name="e">The pressed-key data carrying the emitted character in <c>e.KeyChar</c>.</param>
        private void Keyboard1_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            // [XPLAT] OK / Cancel own the dialog lifecycle (the view-model treats both tokens as no-ops);
            // every other key — backspace, clear, and printable characters — is text editing routed to
            // FormKeyboardViewModel.AppendKey, which carries the original transitions verbatim.
            if (e.KeyChar == OkKey)
            {
                // [XPLAT] WinForms ReturnString = keyboardString.Text; DialogResult.OK; Close();
                Close(_vm.GetResult());
            }
            else if (e.KeyChar == CancelKey)
            {
                // [XPLAT] WinForms DialogResult.Cancel; Close(). Null signals cancellation to the
                // ShowDialog<string> caller.
                Close(null);
            }
            else
            {
                _vm.AppendKey(e.KeyChar);
            }
        }

        /// <summary>
        /// [XPLAT] OK button <c>Click</c> handler (wired via <c>Click="OnOkClick"</c> in
        /// <c>FormKeyboard.axaml</c>; the button is the dialog's default, so Enter activates it). Closes
        /// the dialog returning the entered text — the WinForms <c>ReturnString = keyboardString.Text;
        /// DialogResult = DialogResult.OK; Close();</c>.
        /// </summary>
        /// <param name="sender">The OK button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(_vm.GetResult());
        }

        /// <summary>
        /// [XPLAT] Cancel button <c>Click</c> handler (wired via <c>Click="OnCancelClick"</c> in
        /// <c>FormKeyboard.axaml</c>; the button is the dialog's cancel button, so Escape activates it).
        /// Closes the dialog returning <see langword="null"/> — the WinForms
        /// <c>DialogResult = DialogResult.Cancel; Close();</c>.
        /// </summary>
        /// <param name="sender">The Cancel button raising the event (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
