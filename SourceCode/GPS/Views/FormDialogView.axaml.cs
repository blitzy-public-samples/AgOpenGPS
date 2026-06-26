// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using AgLibrary.Logging;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Severity of a <see cref="FormDialogView"/> message, selecting the coloured frame/body
    /// palette and the icon glyph.
    /// </summary>
    /// <remarks>
    /// Behavioural parity with the WinForms <c>AgOpenGPS.Forms.DialogSeverity</c> (Forms/FormDialog.cs):
    /// the three members and their declaration order are preserved verbatim so the cast values
    /// (<c>Error == 0</c>, <c>Warning == 1</c>, <c>Info == 2</c>) match the original. The only change is
    /// the namespace — the enum now lives in <c>AgOpenGPS.Views</c> rather than <c>AgOpenGPS.Forms</c>,
    /// so a migrated caller references <c>AgOpenGPS.Views.DialogSeverity</c>.
    /// </remarks>
    public enum DialogSeverity
    {
        /// <summary>An error condition: red frame, light-red body, error glyph (and event-log entry).</summary>
        Error,

        /// <summary>A warning condition: amber frame, khaki body, warning glyph (and event-log entry).</summary>
        Warning,

        /// <summary>An informational notice: blue frame, light-blue body, info glyph.</summary>
        Info
    }

    /// <summary>
    /// [XPLAT] Code-behind for <c>FormDialogView.axaml</c> — a 1:1 behavioural-parity reimplementation of
    /// the WinForms <c>Forms/FormDialog</c> (FormDialog.cs + FormDialog.Designer.cs): a borderless,
    /// top-most, centred 700x400 severity message / question dialog with a 20px coloured frame, an
    /// optional Error/Warning/Info icon, a bold title, a message, and OK / Cancel image buttons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The WinForms form exposed a private <c>FormDialog(title, message, showCancel, severity)</c>
    /// constructor plus two static factory entry points — <c>Show</c> (message-only, OK) and
    /// <c>ShowQuestion</c> (OK/Cancel) — and recoloured a GDI-painted 20px frame + body per
    /// <see cref="DialogSeverity"/>. This view reproduces every one of those behaviours: the 20px frame is
    /// the XAML <c>RootBorder.BorderThickness</c> (no <c>OnPaintBackground</c> / GDI <c>Pen</c>), the
    /// severity recolour sets <c>RootBorder.BorderBrush</c> / <c>RootBorder.Background</c> + the icon, and
    /// the OK / Cancel results flow through <see cref="Window.Close(object)"/> (Avalonia has no
    /// <c>DialogResult</c>), surfaced as a <see cref="bool"/> by <c>ShowDialog&lt;bool&gt;</c>
    /// (<see langword="true"/> == WinForms <c>DialogResult.OK</c>).
    /// </para>
    /// <para>
    /// The dialog is intentionally code-behind driven (no <c>x:DataType</c> / no bindings): the
    /// constructors and <see cref="SetSeverity"/> assign the <c>x:Name</c>'d control state directly,
    /// exactly as the WinForms constructor did. Construction is encapsulated behind the static factories
    /// (the parameterised constructor is private, mirroring the WinForms form); the parameterless
    /// constructor exists only because Avalonia's compiled-XAML loader requires it.
    /// </para>
    /// </remarks>
    public partial class FormDialogView : Window
    {
        // === Exact WinForms severity palette (parity) ===
        // [XPLAT] Verbatim ARGB values from FormDialog.cs (Color.FromArgb(r, g, b) — opaque, alpha 255).
        // This is a FIXED parity palette: the WinForms dialog was never day/night themed, so the severity
        // colours are reproduced literally here rather than routed through the App.axaml day/night theme
        // brushes (a theme-driven light foreground would be unreadable on these fixed light backgrounds,
        // breaking both parity and legibility).
        private static readonly Color ErrorBorderColor = Color.FromArgb(255, 192, 0, 0);
        private static readonly Color ErrorBackgroundColor = Color.FromArgb(255, 255, 192, 192);
        private static readonly Color WarningBorderColor = Color.FromArgb(255, 192, 145, 0);
        private static readonly Color WarningBackgroundColor = Color.FromArgb(255, 227, 217, 152);
        private static readonly Color InfoBorderColor = Color.FromArgb(255, 0, 0, 192);
        private static readonly Color InfoBackgroundColor = Color.FromArgb(255, 192, 192, 255);

        /// <summary>
        /// [XPLAT] Process-wide fallback owner window for the static factories. Avalonia's
        /// <see cref="Window.ShowDialog(Window)"/> requires a non-null owner (the WinForms
        /// <c>ShowDialog()</c> resolved one implicitly), so this is set once — by <c>App.axaml.cs</c> /
        /// <c>MainView</c> after the main window is created — and used whenever a caller does not pass an
        /// explicit <c>owner</c>. Callers that already supply an owner (e.g. the migrated owner-first
        /// overloads) ignore this value.
        /// </summary>
        /// <remarks>
        /// [XPLAT] The <c>new</c> modifier is intentional and required: <see cref="WindowBase"/> already
        /// declares an <em>instance</em> <c>Owner</c> (the window that owns a given dialog instance), so a
        /// same-named member triggers CS0108 (a fatal warning under the Release
        /// <c>TreatWarningsAsErrors</c> build). The AAP mandates this exact member name; this app-wide
        /// <em>static</em> default owner is a distinct concept from the inherited per-instance owner, and
        /// <c>new</c> documents the deliberate shadow. The inherited instance owner is unaffected at
        /// runtime — <see cref="Window.ShowDialog(Window)"/> still sets it on the dialog via the base
        /// type, independent of this name shadow.
        /// </remarks>
        public static new Window Owner { get; set; }

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, which the Release build — <c>TreatWarningsAsErrors</c> — treats as an error).
        /// It is also the single construction chokepoint: every instance flows through here (the private
        /// parity constructor chains to it with <c>: this()</c>), so the OK / Cancel click-to-close
        /// handlers are wired here once. The XAML additionally marks OK <c>IsDefault</c> and Cancel
        /// <c>IsCancel</c>, routing Enter / Escape to the same handlers for keyboard operability.
        /// </summary>
        public FormDialogView()
        {
            InitializeComponent();

            // [XPLAT] Avalonia Window has no DialogResult; the buttons close the window with the result
            // value, which ShowDialog<bool> returns to the awaiting caller.
            buttonOK.Click += OnOkClick;
            buttonCancel.Click += OnCancelClick;
        }

        /// <summary>
        /// [XPLAT] Parity with the private WinForms
        /// <c>FormDialog(string title, string message, bool showCancel, DialogSeverity? severity)</c>
        /// constructor: applies the optional severity, then assigns the title / message text and the
        /// Cancel button visibility. Private, exactly as in WinForms — instances are created only by the
        /// static factories below.
        /// </summary>
        /// <param name="title">The bold dialog title.</param>
        /// <param name="message">The dialog body message.</param>
        /// <param name="showCancel">
        /// <see langword="true"/> shows the Cancel button (the question variant); <see langword="false"/>
        /// hides it (the message-only variant), mirroring the WinForms <c>showCancel</c> flag.
        /// </param>
        /// <param name="severity">The optional severity; when <see langword="null"/> the default
        /// CornflowerBlue frame + Lavender body declared in XAML is kept and no icon is shown.</param>
        private FormDialogView(string title, string message, bool showCancel, DialogSeverity? severity)
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

        // [XPLAT] buttonOK -> OK result (true), mirroring the WinForms DialogResult.OK.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] buttonCancel -> Cancel result (false), mirroring the WinForms DialogResult.Cancel.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] Parity with <c>FormDialog.SetSeverity</c>: recolours the frame
        /// (<c>RootBorder.BorderBrush</c>) and the interior (<c>RootBorder.Background</c>) to the exact
        /// WinForms ARGB values and shows the matching Error / Warning / Info glyph. The WinForms version
        /// assigned <c>Resources.Error/Warning/Info</c> (embedded bitmaps); the equivalents here are the
        /// GPS assembly's <c>btnImages/</c> assets loaded via <c>avares://</c> URIs — the same asset
        /// scheme the rest of the GPS Avalonia views consume and the same files the <c>.axaml</c>
        /// references for its OK / Cancel glyphs.
        /// </summary>
        /// <param name="severity">The severity whose palette and icon to apply.</param>
        /// <exception cref="InvalidEnumArgumentException">
        /// Thrown for an out-of-range <paramref name="severity"/>, matching the WinForms guard exactly.
        /// </exception>
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

        // === Static factory API ===
        // [XPLAT] The canonical signatures place the owner LAST and optional, falling back to the static
        // <see cref="Owner"/> (per the AAP), so most call sites need not pass an owner. The owner-first
        // overloads further below preserve the signature the already-migrated GPS views call today.

        /// <summary>
        /// [XPLAT] Parity with the static <c>FormDialog.Show(title, message, severity)</c>: logs
        /// Error / Warning dialogs to the event viewer (identical to the WinForms behaviour, before
        /// display) and shows the message-only (OK) variant modally over <paramref name="owner"/> (or the
        /// static <see cref="Owner"/> fallback).
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The dialog message.</param>
        /// <param name="severity">The optional severity (colours / icon / logging).</param>
        /// <param name="owner">The modal owner window; when <see langword="null"/> the static
        /// <see cref="Owner"/> is used.</param>
        public static async Task ShowAsync(string title, string message, DialogSeverity? severity = null, Window owner = null)
        {
            // [XPLAT] Verbatim from FormDialog.Show: error/warning dialogs are logged before being shown.
            if (severity == DialogSeverity.Error || severity == DialogSeverity.Warning)
            {
                Log.EventWriter($"Dialog: {title} | {message}");
            }

            var form = new FormDialogView(title, message, showCancel: false, severity);
            await form.ShowDialog(owner ?? Owner);
        }

        /// <summary>
        /// [XPLAT] Parity with the static <c>FormDialog.ShowQuestion(title, message, severity)</c>: shows
        /// the OK / Cancel variant modally over <paramref name="owner"/> (or the static <see cref="Owner"/>
        /// fallback) and returns <see langword="true"/> when OK was chosen, mirroring the WinForms
        /// <c>DialogResult</c> return.
        /// </summary>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The dialog message.</param>
        /// <param name="severity">The optional severity (colours / icon).</param>
        /// <param name="owner">The modal owner window; when <see langword="null"/> the static
        /// <see cref="Owner"/> is used.</param>
        /// <returns><see langword="true"/> for OK, <see langword="false"/> for Cancel / closed.</returns>
        public static async Task<bool> ShowQuestionAsync(string title, string message, DialogSeverity? severity = null, Window owner = null)
        {
            var form = new FormDialogView(title, message, showCancel: true, severity);
            return await form.ShowDialog<bool>(owner ?? Owner);
        }

        /// <summary>
        /// [XPLAT] Synchronous, blocking variant of <see cref="ShowQuestionAsync"/> that returns the
        /// OK / Cancel result without the caller awaiting. It exists for the FROZEN synchronous Core
        /// presenter signatures that cannot be made async — e.g.
        /// <c>AvaloniaSelectFieldPanelPresenter.ShowConfirmDeleteMessageBox</c>, which must return a plain
        /// <see cref="bool"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This reproduces the WinForms <c>ShowDialog()</c> blocking semantics with the standard
        /// sync-over-async-modal technique: a nested dispatcher message loop. We start the async modal,
        /// then pump the UI thread with <see cref="DispatcherFrame"/> + <see cref="Dispatcher.PushFrame"/>
        /// (the Avalonia equivalent of WPF's <c>Dispatcher.PushFrame</c>) until the dialog closes. This
        /// keeps the UI responsive and avoids the deadlock that a plain <c>.Result</c> / <c>.Wait()</c>
        /// would cause on the single-threaded UI dispatcher. It MUST be called on the UI thread —
        /// <see cref="Dispatcher.VerifyAccess"/> enforces that contract.
        /// </para>
        /// </remarks>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The dialog message.</param>
        /// <param name="severity">The optional severity (colours / icon).</param>
        /// <param name="owner">The modal owner window; when <see langword="null"/> the static
        /// <see cref="Owner"/> is used.</param>
        /// <returns><see langword="true"/> for OK, <see langword="false"/> for Cancel / closed.</returns>
        public static bool ShowQuestionBlocking(string title, string message, DialogSeverity? severity = null, Window owner = null)
        {
            // [XPLAT] A nested dispatcher loop only works on the UI thread; fail fast otherwise.
            Dispatcher.UIThread.VerifyAccess();

            var form = new FormDialogView(title, message, showCancel: true, severity);
            Task<bool> showTask = form.ShowDialog<bool>(owner ?? Owner);

            // Pump the UI thread until the modal closes. (Guard against an already-completed task so we
            // never push a frame that would never exit.)
            if (!showTask.IsCompleted)
            {
                var frame = new DispatcherFrame();

                // When the dialog closes, request the nested loop to exit. Marshal the flag flip back onto
                // the UI thread (the continuation runs on a thread-pool thread via TaskScheduler.Default).
                showTask.ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => frame.Continue = false),
                    TaskScheduler.Default);

                Dispatcher.UIThread.PushFrame(frame);
            }

            // The task is guaranteed complete here; GetResult returns the value (or rethrows a fault).
            return showTask.GetAwaiter().GetResult();
        }

        // === Owner-first compatibility overloads ===
        // [XPLAT] The already-migrated GPS views call the factories with the owner FIRST
        // (e.g. ShowAsync(this, "Title", "Message", DialogSeverity.Error)). These thin overloads preserve
        // that call shape and simply delegate to the canonical owner-last methods above, so all logging
        // and modality logic lives in one place. They are unambiguous with the canonical overloads because
        // the first positional parameter is a Window here versus a string there.

        /// <summary>
        /// [XPLAT] Owner-first compatibility overload of <see cref="ShowAsync(string, string, DialogSeverity?, Window)"/>.
        /// </summary>
        /// <param name="owner">The modal owner window.</param>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The dialog message.</param>
        /// <param name="severity">The optional severity (colours / icon / logging).</param>
        public static Task ShowAsync(Window owner, string title, string message, DialogSeverity? severity = null)
        {
            return ShowAsync(title, message, severity, owner);
        }

        /// <summary>
        /// [XPLAT] Owner-first compatibility overload of <see cref="ShowQuestionAsync(string, string, DialogSeverity?, Window)"/>.
        /// </summary>
        /// <param name="owner">The modal owner window.</param>
        /// <param name="title">The dialog title.</param>
        /// <param name="message">The dialog message.</param>
        /// <param name="severity">The optional severity (colours / icon).</param>
        /// <returns><see langword="true"/> for OK, <see langword="false"/> for Cancel / closed.</returns>
        public static Task<bool> ShowQuestionAsync(Window owner, string title, string message, DialogSeverity? severity = null)
        {
            return ShowQuestionAsync(title, message, severity, owner);
        }
    }
}
