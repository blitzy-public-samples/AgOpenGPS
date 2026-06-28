// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AgOpenGPS.Core.Translations;
using Keypad;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Behaviour half (code-behind) of the on-screen numeric keypad entry dialog
    /// <see cref="FormNumeric"/>. This is a faithful 1:1 reimplementation of the deleted WinForms
    /// <c>Forms/Inputs/FormNumeric.cs</c> (+ <c>FormNumeric.Designer.cs</c>): a fixed-size, borderless
    /// modal that shows the current value in a large read-only field, the allowed min/max beneath it,
    /// an on-screen <see cref="NumKeypad"/> for entry, and a pair of up/down nudge buttons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dialog is intentionally imperative (no view-model, no data binding): the partner markup
    /// <c>FormNumeric.axaml</c> declares only the named controls, and this code-behind reproduces the
    /// original behaviour by addressing them directly — exactly as the WinForms designer + handlers did.
    /// It is shown modally via <c>await ShowDialog&lt;bool&gt;(owner)</c>; the result is
    /// <see langword="true"/> when the user accepts a valid value (the <c>K</c>/OK key) and
    /// <see langword="false"/> when the user cancels (the <c>X</c> key). On accept the parsed value is
    /// exposed through <see cref="ReturnValue"/>.
    /// </para>
    /// <para>
    /// <b>[XPLAT] Mandated culture correction (AAP §0.6.5 — highest data-integrity rule).</b> The
    /// original <c>FormNumeric.cs</c> was internally inconsistent: a comment claimed culture-invariant
    /// parsing yet the OK handler (source line 132) and both nudge handlers actually used
    /// <see cref="CultureInfo.CurrentCulture"/>, and the decimal key used the current culture's
    /// <c>NumberDecimalSeparator</c>. On a locale whose decimal separator is a comma that corrupts
    /// numeric entry. Every parse/format here is therefore pinned to
    /// <see cref="CultureInfo.InvariantCulture"/>, and the decimal separator is the invariant
    /// <c>"."</c> — which is also the literal character the <see cref="NumKeypad"/> emits. No other
    /// behaviour is changed: <c>double.Parse</c> (not <c>TryParse</c>) is preserved, including the
    /// original <c>"-."</c>/<c>"-"</c> edge behaviour.
    /// </para>
    /// </remarks>
    public partial class FormNumeric : Window
    {
        /// <summary>Inclusive upper bound the entered value is validated against (WinForms <c>max</c>).</summary>
        private readonly double max;

        /// <summary>Inclusive lower bound the entered value is validated against (WinForms <c>min</c>).</summary>
        private readonly double min;

        /// <summary>
        /// True until the user first interacts with the keypad/field, mirroring the WinForms
        /// <c>isFirstKey</c> flag: the seeded current value is cleared on the first keypress so the
        /// user types a fresh number rather than appending to the seed.
        /// </summary>
        private bool isFirstKey;

        // [XPLAT] InvariantCulture decimal separator (matches the literal '.' NumKeypad emits). Replaces
        // the source's Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator usage.
        private const string sep = ".";

        /// <summary>
        /// Gets the value accepted by the user. Valid only when the dialog closed with
        /// <see langword="true"/> (the OK key produced an in-range value). Mirrors the WinForms
        /// <c>ReturnValue</c> property.
        /// </summary>
        public double ReturnValue { get; private set; }

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader. Without it the
        /// Avalonia XAML compiler raises AVLN3001 ("XAML resource ... won't be reachable via runtime
        /// loader, as no public constructor was found"), which the Release zero-warning gate forbids
        /// (see MIGRATION_DOCS/CHANGELOG.md, F1-001). The parity constructor below chains to it with
        /// <c>: this()</c> so <c>InitializeComponent()</c> runs exactly once.
        /// </summary>
        public FormNumeric()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialises the dialog with the allowed range and the value to seed the entry field with.
        /// Parameter order matches the WinForms constructor exactly (<c>_min, _max, currentValue</c>).
        /// Chains to the parameterless constructor (<c>: this()</c>) for <c>InitializeComponent()</c>.
        /// </summary>
        /// <param name="_min">Inclusive minimum accepted value.</param>
        /// <param name="_max">Inclusive maximum accepted value.</param>
        /// <param name="currentValue">The value to pre-fill the entry field with.</param>
        public FormNumeric(double _min, double _max, double currentValue)
            : this()
        {
            max = _max;
            min = _min;

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was currentValue.ToString().
            tboxNumber.Text = currentValue.ToString(CultureInfo.InvariantCulture);

            isFirstKey = true;

            // [XPLAT] KeypadKeyPressedEventArgs replaces WinForms KeyPressEventArgs; e.KeyChar preserved.
            keypad1.ButtonPressed += Keypad1_ButtonPressed;

            // [XPLAT] RepeatButton.Click (auto-repeating while held, Delay/Interval set in XAML) replaces
            // the WinForms RepeatButton.MouseDown auto-repeat for the up/down nudges.
            btnDistanceUp.Click += BtnDistanceUp_Click;
            btnDistanceDn.Click += BtnDistanceDn_Click;

            // [XPLAT] tboxNumber is read-only, so a plain Click/Tapped can be swallowed by the control.
            // A tunneling PointerPressed handler (handledEventsToo) reliably reproduces tboxNumber_Click,
            // which only cleared isFirstKey so the seeded value is preserved when the field is tapped.
            tboxNumber.AddHandler(InputElement.PointerPressedEvent, TboxNumber_PointerPressed,
                                  RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>FormNumeric_Load</c> handler. The <c>Window.Opened</c> lifecycle
        /// event is the Avalonia equivalent of the WinForms <c>Load</c> event: by now the named controls
        /// are realised, so the min/max read-outs are filled, the caret is moved to the end of the seeded
        /// value, and focus is given to the keypad.
        /// </summary>
        /// <param name="e">The event data passed to the base implementation.</param>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was max/min.ToString().
            lblMax.Text = max.ToString(CultureInfo.InvariantCulture);
            lblMin.Text = min.ToString(CultureInfo.InvariantCulture);

            // [XPLAT] caret-to-end (WinForms SelectionStart = Length; SelectionLength = 0).
            tboxNumber.CaretIndex = (tboxNumber.Text ?? string.Empty).Length;
            keypad1.Focus();
        }

        /// <summary>
        /// [XPLAT] Cross-platform reimplementation of the WinForms <c>RegisterKeypad1_ButtonPressed</c>
        /// handler. Interprets the single command/digit token emitted by the on-screen
        /// <see cref="NumKeypad"/> (carried in <see cref="KeypadKeyPressedEventArgs.KeyChar"/>) using the
        /// exact same dispatch as the original: digits append; <c>'B'</c> backspaces; <c>'.'</c> inserts a
        /// decimal point (with leading-zero / <c>-0.</c> normalisation); <c>'-'</c> toggles the sign;
        /// <c>'X'</c> cancels; <c>'C'</c> clears; <c>'K'</c> validates and accepts.
        /// </summary>
        /// <param name="sender">The keypad raising the event (unused).</param>
        /// <param name="e">Event data carrying the pressed character in <see cref="KeypadKeyPressedEventArgs.KeyChar"/>.</param>
        private void Keypad1_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            // First interaction clears the seeded value so the user types a fresh number.
            if (isFirstKey)
            {
                tboxNumber.Text = "";
                isFirstKey = false;
            }

            // Clear the error sentinel (and restore the label colours) as the user enters new values.
            if (tboxNumber.Text == gStr.gsError)
            {
                tboxNumber.Text = "";
                // [XPLAT] reset to theme default foreground (WinForms ForeColor = SystemColors.ControlText).
                lblMin.ClearValue(TextBlock.ForegroundProperty);
                lblMax.ClearValue(TextBlock.ForegroundProperty);
            }

            // [XPLAT] null-safe snapshot — Avalonia TextBox.Text can be null; treat null as empty so the
            // string operations below behave identically to the WinForms (never-null) Text property.
            string text = tboxNumber.Text ?? string.Empty;

            // If it's a number just add it.
            if (char.IsNumber(e.KeyChar))
            {
                tboxNumber.Text = text + e.KeyChar;
            }

            // Backspace key, remove 1 char.
            else if (e.KeyChar == 'B')
            {
                if (text.Length > 0)
                {
                    tboxNumber.Text = text.Remove(text.Length - 1);
                }
            }

            // Decimal point.
            else if (e.KeyChar == '.')
            {
                // Does it already have a decimal?
                if (!text.Contains(sep))
                {
                    text += sep;

                    // If decimal is first char, prefix with a zero.
                    if (text.IndexOf(sep, StringComparison.Ordinal) == 0)
                    {
                        text = "0" + text;
                    }

                    // Neg sign then added a decimal, insert a 0.
                    if (text.IndexOf("-", StringComparison.Ordinal) == 0 &&
                        text.IndexOf(sep, StringComparison.Ordinal) == 1)
                    {
                        text = "-0" + sep;
                    }

                    tboxNumber.Text = text;
                }
            }

            // Negative sign.
            else if (e.KeyChar == '-')
            {
                // If already has a negative don't add again.
                if (!text.Contains("-"))
                {
                    // Prefix the negative sign.
                    tboxNumber.Text = "-" + text;
                }
                else if (text.StartsWith("-", StringComparison.Ordinal))
                {
                    // If already has one, take it away (+/- toggle).
                    tboxNumber.Text = text.Substring(1);
                }
            }

            // Exit or cancel.
            else if (e.KeyChar == 'X')
            {
                Close(false);    // [XPLAT] WinForms DialogResult.Cancel -> ShowDialog<bool> returns false.
                return;
            }

            // Clear whole display.
            else if (e.KeyChar == 'C')
            {
                tboxNumber.Text = "";
            }

            // OK button.
            else if (e.KeyChar == 'K')
            {
                // Not ok if empty - just return (no close), exactly as the source.
                if (text == "") return;

                // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was CurrentCulture
                // (source line 132). double.Parse (not TryParse) is preserved for parity.
                double tryNumber = double.Parse(text, CultureInfo.InvariantCulture);

                // Test if above or below min/max.
                if (tryNumber < min)
                {
                    tboxNumber.Text = gStr.gsError;
                    lblMin.Foreground = Brushes.Red;    // [XPLAT] WinForms lblMin.ForeColor = Color.Red.
                }
                else if (tryNumber > max)
                {
                    tboxNumber.Text = gStr.gsError;
                    lblMax.Foreground = Brushes.Red;    // [XPLAT] WinForms lblMax.ForeColor = Color.Red.
                }
                else
                {
                    // All good, return the value and accept.
                    ReturnValue = tryNumber;
                    Close(true);    // [XPLAT] WinForms DialogResult.OK -> ShowDialog<bool> returns true.
                    return;
                }
            }

            // Show the cursor at the end of the (possibly updated) text.
            tboxNumber.CaretIndex = (tboxNumber.Text ?? string.Empty).Length;
            tboxNumber.Focus();
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>BtnDistanceUp_MouseDown</c> nudge handler, now driven by the
        /// auto-repeating <c>RepeatButton.Click</c>. Increments the current value by one, clamps it to
        /// <see cref="max"/>, and writes it back. A blank / lone-minus / error value is treated as zero
        /// first, matching the original.
        /// </summary>
        /// <param name="sender">The up nudge button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnDistanceUp_Click(object sender, RoutedEventArgs e)
        {
            string t = tboxNumber.Text ?? string.Empty;
            if (t == "" || t == "-" || t == gStr.gsError)
            {
                tboxNumber.Text = "0";
                t = "0";
            }

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was CurrentCulture.
            double tryNumber = double.Parse(t, CultureInfo.InvariantCulture);
            tryNumber++;
            if (tryNumber > max) tryNumber = max;

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was tryNumber.ToString().
            tboxNumber.Text = tryNumber.ToString(CultureInfo.InvariantCulture);

            isFirstKey = false;
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>BtnDistanceDn_MouseDown</c> nudge handler, now driven by the
        /// auto-repeating <c>RepeatButton.Click</c>. Decrements the current value by one, clamps it to
        /// <see cref="min"/>, and writes it back. A blank / lone-minus / error value is treated as zero
        /// first, matching the original.
        /// </summary>
        /// <param name="sender">The down nudge button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnDistanceDn_Click(object sender, RoutedEventArgs e)
        {
            string t = tboxNumber.Text ?? string.Empty;
            if (t == "" || t == "-" || t == gStr.gsError)
            {
                tboxNumber.Text = "0";
                t = "0";
            }

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was CurrentCulture.
            double tryNumber = double.Parse(t, CultureInfo.InvariantCulture);
            tryNumber--;
            if (tryNumber < min) tryNumber = min;

            // [XPLAT] InvariantCulture (parity + cross-platform numeric I/O) — was tryNumber.ToString().
            tboxNumber.Text = tryNumber.ToString(CultureInfo.InvariantCulture);

            isFirstKey = false;
        }

        /// <summary>
        /// [XPLAT] Port of the WinForms <c>tboxNumber_Click</c> handler. Tapping the read-only entry field
        /// only clears <see cref="isFirstKey"/> so the seeded value is kept (not wiped) when the user taps
        /// before typing. Wired as a tunneling pointer-pressed handler because the read-only TextBox can
        /// otherwise swallow the event.
        /// </summary>
        /// <param name="sender">The entry field raising the event (unused).</param>
        /// <param name="e">The pointer event data (unused).</param>
        private void TboxNumber_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (isFirstKey)
            {
                isFirstKey = false;
            }
        }
    }
}
