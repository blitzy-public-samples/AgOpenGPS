// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace GPS_Out.Views
{
    /// <summary>
    /// Auto-dismissing, click-to-close help / notification popup, reimplemented as an Avalonia
    /// <see cref="Window"/> in 1:1 parity with the deleted WinForms <c>frmHelp : Form</c>.
    /// Constructed by <c>clsTools.ShowHelp</c> as <c>new HelpWindow(message, title, timeInMsec)</c>.
    /// </summary>
    /// <remarks>
    /// [XPLAT] Behaviour is ported verbatim from <c>frmHelp</c>; only the host APIs change:
    /// <list type="bullet">
    /// <item>The constructor assigns the title/message and sizes the window height proportionally to
    /// the message length (<c>20 + (len / 34) * 40</c>, clamped to [150, 500]); the width is fixed at
    /// 450 — exactly as the WinForms original.</item>
    /// <item>The WinForms <c>System.Windows.Forms.Timer</c> becomes a <see cref="DispatcherTimer"/>
    /// that auto-closes the popup after <c>timeInMsec</c>; clicking the surface closes it immediately.</item>
    /// <item>On open the saved geometry is restored and the DayColour background is painted; on close
    /// (when the window is in the Normal state) the geometry is persisted. Persistence flows through the
    /// internal <see cref="IWindowState"/> contract instead of the WinForms <c>Form</c> coupling.</item>
    /// <item>The WinForms calling-form back-reference is dropped: this window owns its own
    /// <see cref="clsTools"/> (whose persisted state is static, so it is shared with the main window).</item>
    /// </list>
    /// </remarks>
    public partial class HelpWindow : Window, IWindowState
    {
        // [XPLAT] the window owns its own clsTools (now parameterless — the calling-form back-reference
        // was removed). SAFE because clsTools.HTapp / HTfiles are static, so this instance shares the
        // persisted application state with the MainWindow's instance.
        private readonly clsTools tools = new clsTools();

        // [XPLAT] one-shot auto-close timer; replaces the WinForms System.Windows.Forms.Timer "timer1".
        // It is a plain private field (not an x:Name control) and is readonly (assigned once in the ctor).
        private readonly DispatcherTimer timer1;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML runtime loader (analyzer
        /// AVLN3001): the avares-registered view must expose a public parameterless constructor to be
        /// reachable. The popup is always created in practice through the
        /// <see cref="HelpWindow(string, string, int)"/> overload (<c>clsTools.ShowHelp</c>); this
        /// overload only loads the XAML for the design-time / runtime-loader path.
        /// </summary>
        public HelpWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the popup, assigns the title/message, sizes it to the message length and starts the
        /// auto-close timer — mirroring the WinForms <c>frmHelp(frmStart, string, string, int)</c>
        /// constructor with the calling-form back-reference dropped.
        /// </summary>
        /// <param name="message">The help / notification text shown in the body.</param>
        /// <param name="title">The window title (defaults to "Help").</param>
        /// <param name="timeInMsec">Milliseconds before the popup auto-closes (defaults to 30000).</param>
        public HelpWindow(string message, string title = "Help", int timeInMsec = 30000)
        {
            InitializeComponent();

            // [XPLAT] satellite.ico via avares (was resources.GetObject("$this.Icon")). Wrapped in
            // try/catch for graceful degradation if the icon is not registered as an AvaloniaResource;
            // the window still opens and the failure is logged.
            try
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GPS_Out/satellite.ico")));
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog("HelpWindow/Icon: " + ex.Message);
            }

            Title = title;          // was this.Text = Title
            label1.Text = message;  // was label1.Text = Message

            // [XPLAT] WinForms timer1.Interval + timer1.Enabled = true (designer) -> DispatcherTimer.
            timer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeInMsec) };
            timer1.Tick += Timer1_Tick;
            timer1.Start();

            // [XPLAT] dynamic height ported verbatim from frmHelp: integer arithmetic preserved exactly.
            int len = message.Length;
            Width = 450;
            int ht = 20 + (len / 34) * 40;
            if (ht < 150) ht = 150;
            else if (ht > 500) ht = 500;
            Height = ht;

            Opened += OnOpened;
            Closing += OnClosing;
        }

        // [XPLAT] replaces panel1_Click / label1.Click (both wired to PointerPressed in the .axaml):
        // clicking anywhere on the popup surface closes it immediately.
        private void OnSurfacePressed(object sender, PointerPressedEventArgs e)
        {
            Close();
        }

        // [XPLAT] replaces the WinForms timer1_Tick auto-dismiss. The geometry save is consolidated into
        // OnClosing — Close() raises the Closing event — so the position is saved exactly once on both
        // the auto-dismiss path and the click-to-close path (avoids a disposed-window double-save).
        private void Timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();
            Close();
        }

        // [XPLAT] replaces frmHelp_Load: restore the saved geometry, then paint the DayColour background.
        private void OnOpened(object sender, EventArgs e)
        {
            try
            {
                tools.LoadFormData(this);                                           // was mf.Tls.LoadFormData(this)
                Background = ParseDayColour(Properties.Settings.Default.DayColour); // was this.BackColor = Settings.Default.DayColour
            }
            catch (Exception ex)
            {
                tools.WriteErrorLog("HelpWindow/OnOpened: " + ex.Message);          // was frmHelp/frmHelp_Load
            }
        }

        // [XPLAT] replaces frmHelp_FormClosed: persist the geometry only when the window is in the
        // Normal state (matches the original). The right-hand side is fully qualified to disambiguate
        // the WindowState enum from the inherited Window.WindowState property of the same simple name.
        private void OnClosing(object sender, WindowClosingEventArgs e)
        {
            if (WindowState == Avalonia.Controls.WindowState.Normal)
            {
                tools.SaveFormData(this);
            }
        }

        // [XPLAT] DayColour is now an "R, G, B" string (was a System.Drawing.Color setting). It is parsed
        // to an Avalonia brush with InvariantCulture so it round-trips identically across locales; the
        // parts may contain leading spaces (the default value is "210, 220, 230"), so each is trimmed.
        // System.Drawing is never used.
        private static IBrush ParseDayColour(string value)
        {
            string[] parts = value.Split(',');
            byte r = byte.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            byte g = byte.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
            byte b = byte.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        // ---- IWindowState (explicit; preserves the WinForms Form.Name = "frmHelp" settings keys) ----

        // [XPLAT] constant "frmHelp" so the persisted keys stay frmHelp.Left / frmHelp.Top; the inherited
        // StyledElement.Name would be null / the element name, which would break the saved-geometry keys.
        string IWindowState.Name => "frmHelp";

        // [XPLAT] forward to the inherited Window.Position so real window placement is preserved (a new
        // auto-property would shadow Window.Position and break positioning).
        PixelPoint IWindowState.Position
        {
            get => Position;
            set => Position = value;
        }

        // [XPLAT] forward to the inherited TopLevel.ClientSize (read-only; current code persists position only).
        Size IWindowState.ClientSize => ClientSize;
    }
}
