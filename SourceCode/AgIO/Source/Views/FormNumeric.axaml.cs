// [XPLAT] migrated from net48/WinForms FormNumeric.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Keypad;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Touch numeric-keypad entry dialog, reimplemented as an Avalonia <see cref="Window"/>
    /// replacing the deleted WinForms <c>FormNumeric : Form</c> (<c>Forms/FormNumeric.cs</c> +
    /// <c>FormNumeric.Designer.cs</c>). This is the behaviour half (code-behind) of
    /// <c>FormNumeric.axaml</c>; together they form a single MVVM view bound to a
    /// <see cref="FormNumericViewModel"/> that owns the running entry text, the inclusive min/max
    /// bounds, the keypad editing rules, the up/down nudges, and the parse/validate step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hard contract (consumed by <c>AgIO.Controls.NumericUpDownExtensions</c>).</b> The dialog is
    /// opened as <c>await new FormNumeric(min, max, currentValue).ShowDialog&lt;double?&gt;(owner)</c>.
    /// On a valid entry it closes returning the value (<see cref="Window.Close(object)"/> boxes the
    /// <see langword="double"/> into the <c>double?</c> the caller awaits); on cancel — or when the
    /// window is dismissed via the title bar — it returns a <c>double?</c> with no value
    /// (<c>Close(null)</c> / the framework's <c>default(double?)</c>). This mirrors the WinForms
    /// <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c> contract, which only set the result on success.
    /// </para>
    /// <para>
    /// Behaviour is ported 1:1 from the WinForms original. The partner markup declares no
    /// <c>Click</c>/event attributes — every interactive control is addressed by <c>x:Name</c> and
    /// wired here, reproducing the designer's hookups:
    /// <list type="bullet">
    /// <item><c>keypad1.ButtonPressed</c> (the <see cref="GenericKeypad"/> event carrying
    /// <see cref="KeypadKeyPressedEventArgs.KeyChar"/>) reproduces <c>RegisterKeypad1_ButtonPressed</c>:
    /// <c>'K'</c> (OK) validates and accepts; <c>'X'</c> (Cancel) closes with no value; every other token
    /// (digits, <c>'.'</c>, <c>'-'</c>, <c>'C'</c>, <c>'B'</c>) is forwarded to
    /// <see cref="FormNumericViewModel.AppendKey(char)"/>.</item>
    /// <item><c>btnOK</c> (the default button, so Enter activates it) and <c>btnCancel</c> reproduce the
    /// same accept / cancel outcomes as explicit, touch-friendly buttons.</item>
    /// <item><c>btnDistanceUp</c> / <c>btnDistanceDn</c> reproduce the WinForms
    /// <c>BtnDistanceUp_MouseDown</c> / <c>BtnDistanceDn_MouseDown</c> single-step nudges via
    /// <see cref="FormNumericViewModel.Increment"/> / <see cref="FormNumericViewModel.Decrement"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// All numeric parsing/formatting is owned by the view-model (which pins
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> per AAP §0.6.5), so this
    /// code-behind performs no numeric parsing of its own.
    /// </para>
    /// </remarks>
    public partial class FormNumeric : Window
    {
        // [XPLAT] Keypad command tokens, identical to the WinForms NumKeypad/RegisterKeypad1_ButtonPressed
        // contract: 'K' = OK/accept, 'X' = Cancel. Every other token is an edit handled by the view-model.
        private const char OkKey = 'K';
        private const char CancelKey = 'X';

        // [XPLAT] Out-of-range sentinel shown in the entry field, matching the WinForms 'K' handler
        // ("Error"). It is the exact sentinel FormNumericViewModel recognises: the next AppendKey clears
        // it, and Increment/Decrement treat it (like "" and "-") as a zero starting point.
        private const string ErrorText = "Error";

        // Backing view-model holding the running entry text, the min/max bounds, the editing rules and
        // the parse/validate step. Non-null for application-constructed dialogs (the three-argument
        // constructor); null only for the parameterless loader/previewer path, hence the guards below.
        private FormNumericViewModel _vm;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits warning
        /// <c>AVLN3001</c>, which the Release configuration treats as an error). Application code constructs
        /// the dialog through <see cref="FormNumeric(double, double, double)"/>.
        /// </summary>
        public FormNumeric()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog seeded with the allowed range and the current value, binding it to a freshly
        /// built <see cref="FormNumericViewModel"/>. Parameter order matches the WinForms constructor and
        /// the <c>NumericUpDownExtensions</c> call site exactly.
        /// </summary>
        /// <param name="min">The inclusive minimum acceptable value (shown by the min guard label).</param>
        /// <param name="max">The inclusive maximum acceptable value (shown by the max guard label).</param>
        /// <param name="currentValue">The value pre-filled into the entry field; the first keypress replaces it.</param>
        public FormNumeric(double min, double max, double currentValue)
        {
            InitializeComponent();
            _vm = new FormNumericViewModel(min, max, currentValue);
            DataContext = _vm;
        }

        /// <summary>
        /// Loads the compiled XAML and wires every interactive control by <c>x:Name</c>. Defined explicitly
        /// (rather than relying on a generated method) to match the convention used by the sibling AgIO
        /// Avalonia views (<c>FormYes</c>, <c>FormPGN</c>, <c>FormSource</c>, <c>FormRadioChannel</c>,
        /// <c>FormTimedMessage</c>). The markup declares no handlers, so the keypad and the four command
        /// buttons are subscribed here.
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // [XPLAT] KeypadKeyPressedEventArgs replaces the WinForms KeyPressEventArgs; e.KeyChar preserved.
            var keypad = this.FindControl<NumKeypad>("keypad1");
            if (keypad != null)
            {
                keypad.ButtonPressed += Keypad_ButtonPressed;
            }

            // [XPLAT] btnOK is the default button (Enter activates it) -> validate + accept.
            var ok = this.FindControl<Button>("btnOK");
            if (ok != null)
            {
                ok.Click += BtnOK_Click;
            }

            // [XPLAT] btnCancel -> WinForms DialogResult.Cancel: close with no value.
            var cancel = this.FindControl<Button>("btnCancel");
            if (cancel != null)
            {
                cancel.Click += BtnCancel_Click;
            }

            // [XPLAT] btnDistanceUp / btnDistanceDn -> WinForms BtnDistanceUp_MouseDown / BtnDistanceDn_MouseDown.
            var up = this.FindControl<Button>("btnDistanceUp");
            if (up != null)
            {
                up.Click += BtnDistanceUp_Click;
            }

            var down = this.FindControl<Button>("btnDistanceDn");
            if (down != null)
            {
                down.Click += BtnDistanceDn_Click;
            }
        }

        /// <summary>
        /// [XPLAT] Cross-platform reimplementation of the WinForms <c>RegisterKeypad1_ButtonPressed</c>
        /// handler. Interprets the single token emitted by the on-screen <see cref="NumKeypad"/>: <c>'K'</c>
        /// validates and accepts, <c>'X'</c> cancels, and every other token (digits, <c>'.'</c>, <c>'-'</c>,
        /// <c>'C'</c>, <c>'B'</c>) is forwarded to the view-model's editing rules.
        /// </summary>
        /// <param name="sender">The keypad raising the event (unused).</param>
        /// <param name="e">Event data carrying the pressed character in <see cref="KeypadKeyPressedEventArgs.KeyChar"/>.</param>
        private void Keypad_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            if (_vm == null)
            {
                return;
            }

            if (e.KeyChar == OkKey)
            {
                Accept();
            }
            else if (e.KeyChar == CancelKey)
            {
                Close(null);    // [XPLAT] WinForms DialogResult.Cancel -> ShowDialog<double?> yields null.
            }
            else
            {
                _vm.AppendKey(e.KeyChar);
            }
        }

        // [XPLAT] btnOK click (and Enter, since btnOK is the default button) -> validate + accept.
        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        // [XPLAT] btnCancel click -> WinForms DialogResult.Cancel: close with no value.
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }

        // [XPLAT] btnDistanceUp click -> WinForms BtnDistanceUp_MouseDown: step up by one, clamped to Max.
        private void BtnDistanceUp_Click(object sender, RoutedEventArgs e)
        {
            _vm?.Increment();
        }

        // [XPLAT] btnDistanceDn click -> WinForms BtnDistanceDn_MouseDown: step down by one, clamped to Min.
        private void BtnDistanceDn_Click(object sender, RoutedEventArgs e)
        {
            _vm?.Decrement();
        }

        /// <summary>
        /// Shared accept path used by both the keypad <c>'K'</c> token and the OK button. Reproduces the
        /// WinForms <c>'K'</c> handler: an empty entry is ignored (the dialog stays open with no error);
        /// otherwise the view-model parses and range-checks the entry, the dialog closes returning the
        /// value on success, and on an out-of-range / unparseable entry the dialog stays open showing the
        /// "Error" sentinel (which the next keypad press clears). The WinForms validation only ever set the
        /// dialog result on success, which the <c>Close(value)</c>-only success path preserves.
        /// </summary>
        private void Accept()
        {
            if (_vm == null)
            {
                return;
            }

            // WinForms parity: pressing OK on an empty entry does nothing (stays open, no error shown).
            if (string.IsNullOrEmpty(_vm.EntryText))
            {
                return;
            }

            // Authoritative parse + inclusive range validation live in the view-model (InvariantCulture).
            if (_vm.TryGetResult(out double value))
            {
                // Valid: close returning the value, boxed into the double? the caller awaits.
                Close(value);
                return;
            }

            // Out-of-range / unparseable: keep the dialog open and surface the "Error" sentinel, which the
            // next keypad edit clears — mirroring the WinForms 'K' handler that showed "Error" and only set
            // the dialog result on success.
            _vm.EntryText = ErrorText;
        }
    }
}
