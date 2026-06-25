// [XPLAT] migrated from net48/WinForms (Forms/FormDialog.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Custom styled dialog box for the AgOpenGPS Updater (confirm / info / error / success).
    /// Avalonia reimplementation of the WinForms <c>FormDialog</c>. The WinForms static helpers returned
    /// <c>DialogResult</c> from a blocking <c>ShowDialog</c>; here they return a <see cref="Task"/> (or
    /// <see cref="Task{Boolean}"/> for confirm) from an awaited modal <c>ShowDialog</c>, with
    /// <c>true</c> meaning the OK/affirmative button was pressed.
    /// </summary>
    public partial class FormDialog : Window
    {
        // [XPLAT] These mirror the exact Color.FromArgb values from the WinForms FormDialog and
        // App.axaml's AccentBrush / ErrorBrush / SuccessBrush.
        private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(27, 151, 160));   // #1B97A0
        private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(220, 60, 60));     // #DC3C3C
        private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));   // #28A745

        public FormDialog()
        {
            // [XPLAT] Pattern B: InitializeComponent + typed x:Name fields generated from the XAML.
            InitializeComponent();
        }

        /// <summary>
        /// Shows a confirmation dialog. Returns <c>true</c> if the affirmative (OK) button was pressed.
        /// </summary>
        public static Task<bool> ShowConfirm(Window parent, string title, string message, string okText = "Yes", string cancelText = "No")
        {
            var dialog = new FormDialog { Title = title };
            dialog.lblMessage.Text = message;
            dialog.btnOK.Content = okText;
            dialog.btnCancel.Content = cancelText;
            dialog.lblIcon.Text = "?";
            dialog.panelIcon.Background = AccentBrush;
            // btnOK keeps its default green; btnCancel stays visible (its default red).
            dialog.AdjustWidth(message);
            return dialog.ShowResultAsync(parent);
        }

        /// <summary>Shows an information dialog (single OK button).</summary>
        public static Task ShowInfo(Window parent, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.lblMessage.Text = message;
            dialog.btnOK.Content = "OK";
            dialog.btnCancel.IsVisible = false;
            dialog.lblIcon.Text = "i";
            dialog.panelIcon.Background = AccentBrush;
            dialog.AdjustWidth(message);
            return dialog.ShowResultAsync(parent);
        }

        /// <summary>Shows an error dialog (single OK button, red accents).</summary>
        public static Task ShowError(Window parent, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.lblMessage.Text = message;
            dialog.btnOK.Content = "OK";
            dialog.btnCancel.IsVisible = false;
            dialog.lblIcon.Text = "!";
            dialog.panelIcon.Background = ErrorBrush;
            dialog.btnOK.Background = ErrorBrush;
            dialog.AdjustWidth(message);
            return dialog.ShowResultAsync(parent);
        }

        /// <summary>Shows a success dialog (single OK button, green accents).</summary>
        public static Task ShowSuccess(Window parent, string title, string message)
        {
            var dialog = new FormDialog { Title = title };
            dialog.lblMessage.Text = message;
            dialog.btnOK.Content = "OK";
            dialog.btnCancel.IsVisible = false;
            dialog.lblIcon.Text = "\u2713"; // ✓
            dialog.panelIcon.Background = SuccessBrush;
            dialog.btnOK.Background = SuccessBrush;
            dialog.AdjustWidth(message);
            return dialog.ShowResultAsync(parent);
        }

        /// <summary>
        /// Awaits the modal dialog over <paramref name="parent"/>, returning whether OK was pressed.
        /// </summary>
        private async Task<bool> ShowResultAsync(Window parent)
        {
            if (parent != null)
            {
                return await ShowDialog<bool>(parent);
            }

            // No owner available: fall back to a non-modal show (cannot await a result without an owner).
            Show();
            return false;
        }

        /// <summary>
        /// [XPLAT] Approximates the WinForms <c>Graphics.MeasureString</c> width adjustment: widen the
        /// dialog (up to 800) when the longest message line would otherwise be clipped. Kept as a
        /// heuristic since dialog sizing is a UI nicety, not a frozen contract.
        /// </summary>
        private void AdjustWidth(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            int maxLineLength = 0;
            foreach (var line in message.Split('\n'))
            {
                int len = line.TrimEnd('\r').Length;
                if (len > maxLineLength)
                {
                    maxLineLength = len;
                }
            }

            int neededWidth = (int)(maxLineLength * 8.0) + 120; // icon + padding
            if (neededWidth > 550)
            {
                Width = Math.Min(neededWidth + 40, 800);
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e) => Close(true);

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close(false);

        /// <summary>[XPLAT] Esc dismisses the dialog as a cancel (returns false).</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        }
    }
}
