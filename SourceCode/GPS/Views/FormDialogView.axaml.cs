// [XPLAT] migrated from net48/WinForms (Forms/FormDialog.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using AgLibrary.Logging;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Severity message / question dialog.
    ///
    /// 1:1 behavioural parity reimplementation of the WinForms <c>Forms/FormDialog</c>
    /// (FormDialog.cs + FormDialog.Designer.cs). The WinForms form exposed a private
    /// <c>FormDialog(title, message, showCancel, severity)</c> constructor plus two static
    /// factory entry points (<c>Show</c> for a message and <c>ShowQuestion</c> for a yes/no),
    /// recoloured a painted 20px frame + body per <see cref="DialogSeverity"/>, and showed an
    /// Error/Warning/Info icon. This view reproduces every one of those behaviours against the
    /// x:Name'd controls declared in FormDialogView.axaml.
    ///
    /// The dialog is intentionally code-behind driven (no x:DataType / no bindings): the
    /// constructors and <see cref="SetSeverity"/> assign control state directly, exactly as the
    /// WinForms constructor did.
    /// </summary>
    public partial class FormDialogView : Window
    {
        // [XPLAT] Exact WinForms ARGB values from FormDialog.cs (Color.FromArgb(...)). These are a
        // FIXED parity palette — the WinForms dialog was never day/night themed, so the severity
        // colours are reproduced verbatim rather than routed through the App.axaml theme brushes.
        private static readonly Color ErrorBorderColor = Color.FromArgb(255, 192, 0, 0);
        private static readonly Color ErrorBackgroundColor = Color.FromArgb(255, 255, 192, 192);
        private static readonly Color WarningBorderColor = Color.FromArgb(255, 192, 145, 0);
        private static readonly Color WarningBackgroundColor = Color.FromArgb(255, 227, 217, 152);
        private static readonly Color InfoBorderColor = Color.FromArgb(255, 0, 0, 192);
        private static readonly Color InfoBackgroundColor = Color.FromArgb(255, 192, 192, 255);

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader. Mirrors the
        /// WinForms default state: Cancel hidden (message-only variant) and the default
        /// CornflowerBlue frame + Lavender body declared in XAML. The OK/Cancel buttons declare
        /// IsDefault / IsCancel in XAML (Enter / Escape) but no Click handler, so the click-to-close
        /// behaviour is wired here — this keeps Enter, Escape, and pointer clicks on one path.
        /// </summary>
        public FormDialogView()
        {
            InitializeComponent();

            buttonOK.Click += OnOkClick;
            buttonCancel.Click += OnCancelClick;
        }

        /// <summary>
        /// [XPLAT] Parity with the private WinForms
        /// <c>FormDialog(string title, string message, bool showCancel, DialogSeverity? severity)</c>
        /// constructor: applies the optional severity, then assigns the title/message text and the
        /// Cancel button visibility.
        /// </summary>
        public FormDialogView(string title, string message, bool showCancel = false, DialogSeverity? severity = null)
            : this()
        {
            if (severity.HasValue)
            {
                SetSeverity(severity.Value);
            }

            labelTitle.Text = title;
            labelMessage.Text = message;
            buttonCancel.IsVisible = showCancel;
        }

        // [XPLAT] buttonOK -> OK result. Avalonia Window.Close(result) supplies the value to the
        // awaiting ShowDialog<bool>; true mirrors the WinForms DialogResult.OK.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] buttonCancel -> Cancel result (false), mirroring WinForms DialogResult.Cancel.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] Parity with FormDialog.SetSeverity: recolours the frame (RootBorder.BorderBrush)
        /// and the interior (RootBorder.Background) to the exact WinForms ARGB values and shows the
        /// matching Error/Warning/Info glyph. The WinForms version assigned Resources.Error/Warning/Info
        /// (embedded bitmaps); the equivalents here are the GPS assembly's btnImages/ assets loaded via
        /// avares:// URIs — the same asset scheme every other GPS Avalonia view consumes.
        /// </summary>
        private void SetSeverity(DialogSeverity severity)
        {
            Color borderColor;
            Color backgroundColor;
            string iconAsset;

            switch (severity)
            {
                case DialogSeverity.Error:
                    borderColor = ErrorBorderColor;
                    backgroundColor = ErrorBackgroundColor;
                    iconAsset = "avares://AgOpenGPS/btnImages/Error.png";
                    break;
                case DialogSeverity.Warning:
                    borderColor = WarningBorderColor;
                    backgroundColor = WarningBackgroundColor;
                    iconAsset = "avares://AgOpenGPS/btnImages/Warning.png";
                    break;
                case DialogSeverity.Info:
                    borderColor = InfoBorderColor;
                    backgroundColor = InfoBackgroundColor;
                    iconAsset = "avares://AgOpenGPS/btnImages/Info.png";
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(severity), (int)severity, typeof(DialogSeverity));
            }

            RootBorder.BorderBrush = new SolidColorBrush(borderColor);
            RootBorder.Background = new SolidColorBrush(backgroundColor);

            pictureBoxIcon.Source = new Bitmap(AssetLoader.Open(new Uri(iconAsset)));
            pictureBoxIcon.IsVisible = true;
        }

        /// <summary>
        /// [XPLAT] Parity with the static <c>FormDialog.Show(title, message, severity)</c>: logs
        /// Error/Warning dialogs to the event viewer (identical to the WinForms behaviour) and shows
        /// the message-only variant modally over <paramref name="owner"/>. Avalonia requires an owning
        /// window for a modal dialog, which the WinForms <c>ShowDialog()</c> resolved implicitly.
        /// </summary>
        public static Task ShowAsync(Window owner, string title, string message, DialogSeverity? severity = null)
        {
            // [XPLAT] Parity: FormDialog.Show logs error/warning dialogs before display.
            if (severity == DialogSeverity.Error || severity == DialogSeverity.Warning)
            {
                Log.EventWriter($"Dialog: {title} | {message}");
            }

            var dialog = new FormDialogView(title, message, showCancel: false, severity);
            return dialog.ShowDialog(owner);
        }

        /// <summary>
        /// [XPLAT] Parity with the static <c>FormDialog.ShowQuestion(...)</c>: shows the OK/Cancel
        /// variant modally and returns <c>true</c> when OK was chosen, mirroring the WinForms
        /// <c>DialogResult</c> return.
        /// </summary>
        public static async Task<bool> ShowQuestionAsync(Window owner, string title, string message, DialogSeverity? severity = null)
        {
            var dialog = new FormDialogView(title, message, showCancel: true, severity);
            return await dialog.ShowDialog<bool>(owner);
        }
    }

    /// <summary>
    /// [XPLAT] Parity with the WinForms <c>AgOpenGPS.Forms.DialogSeverity</c> enum (FormDialog.cs).
    /// </summary>
    public enum DialogSeverity
    {
        Error,
        Warning,
        Info
    }
}
