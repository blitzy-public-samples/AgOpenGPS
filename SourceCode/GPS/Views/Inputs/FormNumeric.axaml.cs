// [XPLAT] migrated from net48/WinForms (Forms/Inputs/FormNumeric.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Keypad;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the on-screen numeric-entry dialog, hosting the migrated
    /// <see cref="NumKeypad"/> UserControl with min/max readouts and up/down nudge buttons. This is a
    /// faithful 1:1 behavioural port of the WinForms <c>FormNumeric : Form</c>
    /// (Forms/Inputs/FormNumeric.cs): the dialog is IMPERATIVE (no MVVM), driving the named
    /// <c>tboxNumber</c> / <c>lblMin</c> / <c>lblMax</c> controls directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="NumKeypad"/> raises <see cref="GenericKeypad.ButtonPressed"/> carrying a single
    /// <see cref="KeypadKeyPressedEventArgs.KeyChar"/>. The token contract is ported verbatim: digits
    /// <c>'0'</c>-<c>'9'</c>, backspace <c>'B'</c>, decimal <c>'.'</c>, plus/minus <c>'-'</c>, clear
    /// <c>'C'</c>, cancel <c>'X'</c> and OK <c>'K'</c>.
    /// </para>
    /// <para>
    /// Display, parsing and the decimal separator use <see cref="CultureInfo.CurrentCulture"/>,
    /// exactly as the WinForms original did — this is a user-facing entry dialog, so the operator sees
    /// and types digits in their own locale (the confirmed <see cref="ReturnValue"/> is a culture-neutral
    /// <see cref="double"/>). The outcome is surfaced through <see cref="Window.Close(object)"/>:
    /// <c>Close(true)</c> on a valid OK (value via <see cref="ReturnValue"/>) and <c>Close(false)</c> on
    /// Cancel — the cross-platform replacement for the WinForms <c>DialogResult.OK</c> /
    /// <c>DialogResult.Cancel</c>.
    /// </para>
    /// </remarks>
    public partial class FormNumeric : Window
    {
        // [XPLAT] Inclusive bounds + first-key flag, copied verbatim from the WinForms fields.
        private readonly double _max;
        private readonly double _min;
        private bool _isFirstKey;

        /// <summary>
        /// Gets the confirmed numeric value after the dialog closes with <c>true</c> (the WinForms
        /// <c>ReturnValue</c> property, preserved for caller parity).
        /// </summary>
        public double ReturnValue { get; private set; }

        /// <summary>
        /// Parameterless constructor required by Avalonia's compiled-XAML runtime loader (keeps the
        /// avares resource reachable / avoids AVLN3001). Also wires the keypad and the nudge buttons.
        /// Production code constructs via <see cref="FormNumeric(double, double, double)"/>.
        /// </summary>
        public FormNumeric()
        {
            InitializeComponent();

            // [XPLAT] WinForms wired keypad1.ButtonPressed += RegisterKeypad1_ButtonPressed and the
            // btnDistanceUp/btnDistanceDn MouseDown handlers in the designer. The Avalonia source
            // generator creates the named fields from FormNumeric.axaml; the RepeatButtons reproduce
            // the legacy press-and-hold repeat (Delay/Interval set in the .axaml) via Click.
            keypad1.ButtonPressed += RegisterKeypad1_ButtonPressed;
            btnDistanceUp.Click += BtnDistanceUp_Click;
            btnDistanceDn.Click += BtnDistanceDn_Click;
        }

        /// <summary>
        /// [XPLAT] Mirrors the WinForms <c>FormNumeric(double _min, double _max, double currentValue)</c>
        /// constructor: stores the inclusive bounds, seeds the display with the current value, and arms
        /// the first-key replace behaviour.
        /// </summary>
        /// <param name="min">Inclusive minimum accepted value.</param>
        /// <param name="max">Inclusive maximum accepted value.</param>
        /// <param name="currentValue">The value shown when the dialog opens.</param>
        public FormNumeric(double min, double max, double currentValue)
            : this()
        {
            _max = max;
            _min = min;
            Title = "Enter Value";
            tboxNumber.Text = currentValue.ToString(CultureInfo.CurrentCulture);
            _isFirstKey = true;
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormNumeric_Load</c>: renders the min/max guard labels, places the caret at
        /// the end of the seeded value, and focuses the keypad.
        /// </summary>
        /// <param name="e">The routed-event payload.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            lblMax.Text = _max.ToString(CultureInfo.CurrentCulture);
            lblMin.Text = _min.ToString(CultureInfo.CurrentCulture);
            SetCaretEnd();
            keypad1.Focus();
        }

        /// <summary>
        /// [XPLAT] Ports <c>RegisterKeypad1_ButtonPressed</c>: applies the pressed token to the value,
        /// validates the inclusive range on OK, and completes the dialog on OK / Cancel.
        /// </summary>
        /// <param name="sender">The numeric keypad; unused.</param>
        /// <param name="e">The pressed-key payload carrying <see cref="KeypadKeyPressedEventArgs.KeyChar"/>.</param>
        private void RegisterKeypad1_ButtonPressed(object sender, KeypadKeyPressedEventArgs e)
        {
            string text = tboxNumber.Text ?? string.Empty;

            // First key after opening replaces the seeded current value.
            if (_isFirstKey)
            {
                text = string.Empty;
                tboxNumber.Text = text;
                _isFirstKey = false;
            }

            // A prior range error left "Error" in the box; clear it and reset the reddened bounds.
            if (text == gStr.gsError)
            {
                text = string.Empty;
                tboxNumber.Text = text;
                lblMin.Foreground = Brushes.Black;
                lblMax.Foreground = Brushes.Black;
            }

            char keyChar = e.KeyChar;
            string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            if (char.IsNumber(keyChar))
            {
                tboxNumber.Text = text + keyChar;
            }
            else if (keyChar == 'B')
            {
                if (text.Length > 0)
                {
                    tboxNumber.Text = text.Remove(text.Length - 1);
                }
            }
            else if (keyChar == '.')
            {
                if (!text.Contains(decimalSeparator))
                {
                    text += decimalSeparator;
                    // Leading "." -> "0."; leading "-." -> "-0.".
                    if (text.IndexOf(decimalSeparator, StringComparison.Ordinal) == 0)
                    {
                        text = "0" + text;
                    }
                    if (text.IndexOf("-", StringComparison.Ordinal) == 0 &&
                        text.IndexOf(decimalSeparator, StringComparison.Ordinal) == 1)
                    {
                        text = "-0" + decimalSeparator;
                    }
                    tboxNumber.Text = text;
                }
            }
            else if (keyChar == '-')
            {
                if (!text.Contains("-"))
                {
                    tboxNumber.Text = "-" + text;
                }
                else if (text.StartsWith("-", StringComparison.Ordinal))
                {
                    tboxNumber.Text = text.Substring(1);
                }
            }
            else if (keyChar == 'X')
            {
                // WinForms: DialogResult.Cancel + Close().
                Close(false);
                return;
            }
            else if (keyChar == 'C')
            {
                tboxNumber.Text = string.Empty;
            }
            else if (keyChar == 'K')
            {
                string current = tboxNumber.Text ?? string.Empty;
                if (current.Length == 0)
                {
                    return;
                }

                double tryNumber = double.Parse(current, CultureInfo.CurrentCulture);
                if (tryNumber < _min)
                {
                    tboxNumber.Text = gStr.gsError;
                    lblMin.Foreground = Brushes.Red;
                }
                else if (tryNumber > _max)
                {
                    tboxNumber.Text = gStr.gsError;
                    lblMax.Foreground = Brushes.Red;
                }
                else
                {
                    // WinForms: ReturnValue = tryNumber; DialogResult.OK + Close().
                    ReturnValue = tryNumber;
                    Close(true);
                    return;
                }
            }

            SetCaretEnd();
            tboxNumber.Focus();
        }

        /// <summary>
        /// [XPLAT] Ports <c>BtnDistanceUp_MouseDown</c>: step the value up by one, clamped to the maximum.
        /// Driven by the <c>btnDistanceUp</c> RepeatButton (press-and-hold repeats).
        /// </summary>
        /// <param name="sender">The up button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void BtnDistanceUp_Click(object sender, RoutedEventArgs e)
        {
            double tryNumber = ParseForNudge();
            tryNumber++;
            if (tryNumber > _max) tryNumber = _max;
            tboxNumber.Text = tryNumber.ToString(CultureInfo.CurrentCulture);
            _isFirstKey = false;
            SetCaretEnd();
        }

        /// <summary>
        /// [XPLAT] Ports <c>BtnDistanceDn_MouseDown</c>: step the value down by one, clamped to the minimum.
        /// Driven by the <c>btnDistanceDn</c> RepeatButton (press-and-hold repeats).
        /// </summary>
        /// <param name="sender">The down button; unused.</param>
        /// <param name="e">The routed-event payload; unused.</param>
        private void BtnDistanceDn_Click(object sender, RoutedEventArgs e)
        {
            double tryNumber = ParseForNudge();
            tryNumber--;
            if (tryNumber < _min) tryNumber = _min;
            tboxNumber.Text = tryNumber.ToString(CultureInfo.CurrentCulture);
            _isFirstKey = false;
            SetCaretEnd();
        }

        // [XPLAT] Shared nudge pre-parse: empty / "-" / "Error" are treated as "0" before incrementing,
        // exactly as the WinForms MouseDown handlers did.
        private double ParseForNudge()
        {
            string text = tboxNumber.Text ?? string.Empty;
            if (text.Length == 0 || text == "-" || text == gStr.gsError)
            {
                text = "0";
            }
            return double.Parse(text, CultureInfo.CurrentCulture);
        }

        // [XPLAT] Places the caret at the end of the value. Avalonia's TextBox.CaretIndex replaces the
        // WinForms SelectionStart/SelectionLength=0 pair (input arrives only via the on-screen keypad).
        private void SetCaretEnd()
        {
            tboxNumber.CaretIndex = tboxNumber.Text?.Length ?? 0;
        }
    }
}
