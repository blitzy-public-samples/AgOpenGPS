// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the AgOpenGPS "Section Colors" preset-assignment dialog.
//
// 1:1 PARITY reimplementation of the WinForms original
// (Forms/Settings/FormColorSection.cs + FormColorSection.Designer.cs). Per AAP §0.3.3 the bar is
// visual + behavioral FIDELITY to the currently-rendered Windows Forms UI, never a redesign:
//   * sixteen numbered section swatches (cb01..cb16) acting as a single radio group;
//   * a sixteen-square custom preset palette (btnC01..btnC16) that operates in two modes —
//     APPLY the preset colour to the selected section (isUse) or REDEFINE the preset via a colour
//     picker (isChange);
//   * an "Edit Color" lock toggle (chkUse) that flips the palette between apply/redefine and swaps
//     a locked/unlocked glyph;
//   * a "Multi-Color Sections" toggle (cboxIsMulti) that enables/disables the whole palette;
//   * an OK button (bntOK) that commits the sixteen section colours.
//
// De-Windowsing (AAP §0.1.2 / §0.5):
//   * The WinForms `mf` (FormGPS) god-object back-reference is REMOVED. The tool/section colour
//     state is dependency-injected through the local <see cref="ISectionColorState"/> contract
//     (the original `mf.tool.secColors` / `mf.tool.isMultiColoredSections`). CTool itself is not
//     compiled into the cross-platform GPS assembly yet, hence the minimal local interface.
//   * The MechanikaDesign WinForms `FormColorPicker` is GONE; the colour pick now uses the built-in
//     cross-platform <see cref="Avalonia.Controls.ColorView"/> hosted in a small modal window
//     (the helper pattern called for by the file spec).
//   * Persistence is unchanged in shape: the split settings objects Properties.Settings (the custom
//     preset CSV) and Properties.ToolSettings (the per-section colours + multi flag) are read and
//     written exactly as the WinForms code did, preserving the settings XML schema.
//
// There is intentionally NO System.Windows.Forms, System.Drawing(GDI), MechanikaDesign,
// DataVisualization or OpenTK dependency here. System.Drawing.Color is referenced fully-qualified
// only as a data type (in-box, cross-platform via System.Drawing.Primitives on net8.0) because the
// ToolSettings colour fields and the CheckColorFor255 extension are typed against it.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Minimal contract for the injected tool/section-colour state, replacing the WinForms
    /// <c>FormGPS.tool</c> (<c>mf.tool</c>) back-reference. The composition root passes the live tool
    /// object (or a thin adapter over it) that exposes the sixteen committed section colours and the
    /// multi-colour flag. Mirrors exactly the two members the WinForms dialog touched:
    /// <c>mf.tool.secColors[0..15]</c> and <c>mf.tool.isMultiColoredSections</c>.
    /// </summary>
    public interface ISectionColorState
    {
        /// <summary>
        /// The sixteen committed section colours (index 0 == section 1 … index 15 == section 16).
        /// Typed as <see cref="System.Drawing.Color"/> so the values assign directly to and from the
        /// <c>ToolSettings.setColor_secNN</c> fields without conversion, preserving byte parity.
        /// </summary>
        System.Drawing.Color[] secColors { get; }

        /// <summary>Whether sections are drawn in individually-assigned colours (the OK commit toggles this).</summary>
        bool isMultiColoredSections { get; set; }
    }

    /// <summary>
    /// [XPLAT] Avalonia view (code-behind) for the section-colour preset dialog. Behaviour is frozen
    /// against the WinForms original; only the host framework and the OS-coupled picker change.
    /// </summary>
    public partial class FormColorSectionView : Window
    {
        /// <summary>Number of sections / preset squares the dialog manages (fixed at sixteen, as in WinForms).</summary>
        private const int SectionCount = 16;

        // --- State fields (mirror the WinForms FormColorSection fields) -------------------------------

        // WinForms initialises isUse = true: a fresh swatch click APPLIES presets until the lock is
        // opened. Preserving this exact start state is required for behavioural parity.
        private bool isUse = true;

        // True only while the lock (chkUse) is open: clicking a preset square REDEFINES it via the picker.
        private bool isChange;

        // Gate for the close guard: the window refuses to close unless OK set this true (WinForms _FormClosing).
        private bool isClosing;

        // The sixteen custom preset colours as ARGB-packed ints — byte-identical in layout to
        // System.Drawing.Color.ToArgb() and to the persisted Settings CSV. Kept as int[] (not Color[])
        // so the CSV round-trip is byte-faithful with the WinForms original.
        private readonly int[] customSectionColorsList = new int[SectionCount];

        // Injected tool/section-colour state (replaces mf.tool). Null only on the XAML-previewer path.
        private readonly ISectionColorState _tool;

        // Cached references to the sixteen section swatches and sixteen preset buttons, populated from
        // the x:Name'd controls after InitializeComponent so the shared handlers can iterate them.
        private readonly CheckBox[] _cb;
        private readonly Button[] _btnC;

        // Lazily-loaded, cached lock/unlock glyphs for chkUse (a missing asset degrades to null, never throws).
        private Bitmap _lockedGlyph;
        private Bitmap _unlockedGlyph;

        // --- Construction -----------------------------------------------------------------------------

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / design-time
        /// previewer. Loads the markup, caches the swatch/preset control arrays, applies the composed
        /// caption, parses the persisted custom-preset CSV and runs the WinForms Load logic.
        /// Application code uses <see cref="FormColorSectionView(ISectionColorState)"/>.
        /// </summary>
        public FormColorSectionView()
        {
            InitializeComponent();

            // Title is not visible (SystemDecorations=None) but is set for taskbar/parity with the
            // WinForms `this.Text = gStr.gsSectionColorsSet`.
            Title = gStr.gsSectionColorsSet;

            // labelSectionColor is intentionally empty in the markup; the WinForms ctor composed it as
            // "<Sections>: <Colors>".
            labelSectionColor.Text = $"{gStr.gsSections}: {gStr.gsColors}";

            // Cache the sixteen section swatches and sixteen preset buttons (replaces the per-control
            // wiring the WinForms Designer added one field at a time).
            _cb = new[]
            {
                cb01, cb02, cb03, cb04, cb05, cb06, cb07, cb08,
                cb09, cb10, cb11, cb12, cb13, cb14, cb15, cb16
            };
            _btnC = new[]
            {
                btnC01, btnC02, btnC03, btnC04, btnC05, btnC06, btnC07, btnC08,
                btnC09, btnC10, btnC11, btnC12, btnC13, btnC14, btnC15, btnC16
            };

            // Parse the persisted custom preset palette: a comma-separated list of sixteen ARGB ints.
            // InvariantCulture on every parse (cross-platform: a comma decimal locale must not corrupt
            // these integers). Defensive bounds/format handling keeps a malformed setting from throwing
            // in the constructor while remaining byte-identical for the normal sixteen-value setting.
            string raw = Properties.Settings.Default.setDisplay_customSectionColors ?? string.Empty;
            string[] words = raw.Split(',');
            for (int i = 0; i < SectionCount; i++)
            {
                if (i < words.Length &&
                    int.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argb))
                {
                    customSectionColorsList[i] = argb;
                }
            }

            // WinForms FormColorSection_Load equivalent.
            LoadColors();
        }

        /// <summary>
        /// [XPLAT] Application constructor. Parity with the WinForms
        /// <c>FormColorSection(Form callingForm)</c> ctor, with the <c>FormGPS</c> argument replaced by
        /// the injected <see cref="ISectionColorState"/>.
        /// </summary>
        /// <param name="toolColors">The live tool/section-colour state to commit into on OK.</param>
        public FormColorSectionView(ISectionColorState toolColors)
            : this()
        {
            _tool = toolColors;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormColorSection_Load</c>: seed every swatch from the committed section
        /// colours, seed every preset square from the parsed custom palette, restore the multi-colour
        /// toggle, and lock the palette down when multi-colour is off.
        /// </summary>
        private void LoadColors()
        {
            // The sixteen committed section colours live in discrete ToolSettings fields; gather them in
            // order so the swatches can be seeded in a single loop.
            System.Drawing.Color[] secs =
            {
                Properties.ToolSettings.Default.setColor_sec01, Properties.ToolSettings.Default.setColor_sec02,
                Properties.ToolSettings.Default.setColor_sec03, Properties.ToolSettings.Default.setColor_sec04,
                Properties.ToolSettings.Default.setColor_sec05, Properties.ToolSettings.Default.setColor_sec06,
                Properties.ToolSettings.Default.setColor_sec07, Properties.ToolSettings.Default.setColor_sec08,
                Properties.ToolSettings.Default.setColor_sec09, Properties.ToolSettings.Default.setColor_sec10,
                Properties.ToolSettings.Default.setColor_sec11, Properties.ToolSettings.Default.setColor_sec12,
                Properties.ToolSettings.Default.setColor_sec13, Properties.ToolSettings.Default.setColor_sec14,
                Properties.ToolSettings.Default.setColor_sec15, Properties.ToolSettings.Default.setColor_sec16
            };
            for (int i = 0; i < SectionCount; i++)
            {
                _cb[i].Background = new SolidColorBrush(ToAv(secs[i].CheckColorFor255()));
            }

            cboxIsMulti.IsChecked = Properties.ToolSettings.Default.setColor_isMultiColorSections;

            for (int i = 0; i < SectionCount; i++)
            {
                System.Drawing.Color preset = System.Drawing.Color.FromArgb(customSectionColorsList[i]).CheckColorFor255();
                _btnC[i].Background = new SolidColorBrush(ToAv(preset));
            }

            if (cboxIsMulti.IsChecked != true)
            {
                SetGui(false);
            }
        }

        // --- Enable / disable + toggles ---------------------------------------------------------------

        /// <summary>
        /// [XPLAT] WinForms <c>SetGui(bool)</c>: enable/disable all sixteen preset buttons, all sixteen
        /// section swatches and the lock toggle together.
        /// </summary>
        private void SetGui(bool set)
        {
            for (int i = 0; i < SectionCount; i++)
            {
                _btnC[i].IsEnabled = set;
                _cb[i].IsEnabled = set;
            }
            chkUse.IsEnabled = set;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>cboxIsMulti_Click</c>: the palette is usable only when multi-colour is on.
        /// </summary>
        private void cboxIsMulti_Click(object sender, RoutedEventArgs e)
        {
            SetGui(cboxIsMulti.IsChecked == true);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>chkUse_CheckedChanged</c>: open the lock to redefine presets (palette
        /// header prompts for a square, unlocked glyph, isChange=true), or close it to apply presets
        /// (locked glyph, header reverts). Header literals match the WinForms strings verbatim.
        /// </summary>
        private void chkUse_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (chkUse.IsChecked == true)
            {
                groupBoxSelectPreset.Text = "Select Square Below And Pick New Color";
                SetLockGlyph(unlocked: true);
                isChange = true;
                isUse = false;
            }
            else
            {
                isChange = false;
                isUse = false;
                groupBoxSelectPreset.Text = "Select Preset Color";
                SetLockGlyph(unlocked: false);
            }
        }

        // --- Section swatch radio behaviour -----------------------------------------------------------

        /// <summary>
        /// [XPLAT] WinForms shared <c>cb01_Click</c>: the sixteen swatches behave as a radio group —
        /// clear every check, then check the clicked one, and arm "apply preset to this section" mode.
        /// </summary>
        private void cb_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < SectionCount; i++)
            {
                _cb[i].IsChecked = false;
            }

            if (sender is CheckBox cbox)
            {
                cbox.IsChecked = true;
            }

            isUse = true;
            isChange = false;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>UpdateColor(Color)</c>: clamp the colour (no pure 255 channel), paint it
        /// onto whichever swatch is currently checked, then reset the radio group and disarm apply mode.
        /// </summary>
        private void UpdateColor(Color col)
        {
            // Clamp through the shared System.Drawing-typed extension to stay byte-identical with WinForms.
            Color clamped = ToAv(ToSd(col).CheckColorFor255());

            for (int i = 0; i < SectionCount; i++)
            {
                if (_cb[i].IsChecked == true)
                {
                    _cb[i].Background = new SolidColorBrush(clamped);
                    break;
                }
            }

            for (int i = 0; i < SectionCount; i++)
            {
                _cb[i].IsChecked = false;
            }

            isUse = false;
        }

        // --- Preset palette (dual mode) ---------------------------------------------------------------

        /// <summary>
        /// [XPLAT] WinForms shared <c>btnC01_Click</c>. Dual mode, reproduced exactly:
        /// when <c>isUse</c>, apply the preset's colour to the selected section swatch; when
        /// <c>isChange</c>, open the colour picker seeded with the preset's current colour, store the
        /// chosen ARGB into the custom palette, repaint the square and persist the palette CSV.
        /// The lock is always re-locked at the end (parity with the trailing <c>chkUse.Checked=false</c>).
        /// </summary>
        private async void btnC_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button butt)
            {
                return;
            }

            if (isUse)
            {
                UpdateColor(GetColor(butt.Background));
                isUse = false;
            }
            else if (isChange)
            {
                // Parse the 1-based preset index from the control name ("btnC01" -> "01" -> 1), exactly
                // as WinForms did: Name.Substring(4, 2) under InvariantCulture. Guarded so a malformed
                // name can never throw or index out of range.
                string name = butt.Name ?? string.Empty;
                int buttNumber = 0;
                if (name.Length >= 6)
                {
                    int.TryParse(name.Substring(4, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out buttNumber);
                }

                Color? chosen = await PickColorAsync(GetColor(butt.Background));
                if (chosen.HasValue && buttNumber >= 1 && buttNumber <= SectionCount)
                {
                    Color useThisColor = chosen.Value;
                    customSectionColorsList[buttNumber - 1] = ToArgb(useThisColor);
                    butt.Background = new SolidColorBrush(useThisColor);
                }

                SaveCustomColor();
                isChange = false;
            }

            // Always re-lock after handling a preset click (WinForms set chkUse.Checked = false here,
            // which re-triggers chkUse_CheckedChanged to restore the locked glyph + header).
            chkUse.IsChecked = false;
        }

        /// <summary>
        /// [XPLAT] Cross-platform replacement for the WinForms MechanikaDesign <c>FormColorPicker</c>.
        /// Hosts the built-in <see cref="ColorView"/> in a small modal window seeded with
        /// <paramref name="seed"/>; returns the chosen colour, or <c>null</c> if cancelled.
        /// </summary>
        private async Task<Color?> PickColorAsync(Color seed)
        {
            var view = new ColorView
            {
                Color = seed,
                IsAlphaEnabled = false,
                IsAlphaVisible = false,
                Margin = new Thickness(8)
            };

            var ok = new Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 90,
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancel = new Button
            {
                Content = gStr.gsCancel,
                IsCancel = true,
                MinWidth = 90,
                Margin = new Thickness(4)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8)
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);

            var root = new DockPanel { LastChildFill = true };
            root.Children.Add(buttons);
            root.Children.Add(view);

            var dlg = new Window
            {
                Title = gStr.gsColorPicker,
                Content = root,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            // Pack the result through Close so ShowDialog<Color?> yields the chosen colour (or null).
            ok.Click += (_, __) => dlg.Close((Color?)view.Color);
            cancel.Click += (_, __) => dlg.Close((Color?)null);

            return await dlg.ShowDialog<Color?>(this);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>SaveCustomColor</c>: write the sixteen preset ARGB ints to the settings
        /// CSV — the first fifteen each followed by a comma, the last with none — then persist.
        /// InvariantCulture on every conversion so the CSV is identical across operating systems.
        /// </summary>
        private void SaveCustomColor()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SectionCount - 1; i++)
            {
                sb.Append(customSectionColorsList[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            }
            sb.Append(customSectionColorsList[SectionCount - 1].ToString(CultureInfo.InvariantCulture));

            Properties.Settings.Default.setDisplay_customSectionColors = sb.ToString();
            Properties.Settings.Default.Save();
        }

        // --- Commit (OK) ------------------------------------------------------------------------------

        /// <summary>
        /// [XPLAT] WinForms <c>bntOK_Click</c>: commit each swatch colour into its ToolSettings field
        /// (clamped) and mirror it into the injected tool's <c>secColors</c>, commit the multi-colour
        /// flag, persist ToolSettings, then close. The sixteen assignments are reproduced explicitly to
        /// guarantee byte-faithful mapping cb01->sec01 … cb16->sec16.
        /// </summary>
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            Properties.ToolSettings.Default.setColor_sec01 = ToSd(GetColor(cb01.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec02 = ToSd(GetColor(cb02.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec03 = ToSd(GetColor(cb03.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec04 = ToSd(GetColor(cb04.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec05 = ToSd(GetColor(cb05.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec06 = ToSd(GetColor(cb06.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec07 = ToSd(GetColor(cb07.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec08 = ToSd(GetColor(cb08.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec09 = ToSd(GetColor(cb09.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec10 = ToSd(GetColor(cb10.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec11 = ToSd(GetColor(cb11.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec12 = ToSd(GetColor(cb12.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec13 = ToSd(GetColor(cb13.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec14 = ToSd(GetColor(cb14.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec15 = ToSd(GetColor(cb15.Background)).CheckColorFor255();
            Properties.ToolSettings.Default.setColor_sec16 = ToSd(GetColor(cb16.Background)).CheckColorFor255();

            // Mirror the committed colours into the injected tool model (WinForms wrote both in one
            // chained assignment: mf.tool.secColors[i] = ToolSettings.setColor_secNN = ...).
            if (_tool?.secColors != null && _tool.secColors.Length >= SectionCount)
            {
                _tool.secColors[0] = Properties.ToolSettings.Default.setColor_sec01;
                _tool.secColors[1] = Properties.ToolSettings.Default.setColor_sec02;
                _tool.secColors[2] = Properties.ToolSettings.Default.setColor_sec03;
                _tool.secColors[3] = Properties.ToolSettings.Default.setColor_sec04;
                _tool.secColors[4] = Properties.ToolSettings.Default.setColor_sec05;
                _tool.secColors[5] = Properties.ToolSettings.Default.setColor_sec06;
                _tool.secColors[6] = Properties.ToolSettings.Default.setColor_sec07;
                _tool.secColors[7] = Properties.ToolSettings.Default.setColor_sec08;
                _tool.secColors[8] = Properties.ToolSettings.Default.setColor_sec09;
                _tool.secColors[9] = Properties.ToolSettings.Default.setColor_sec10;
                _tool.secColors[10] = Properties.ToolSettings.Default.setColor_sec11;
                _tool.secColors[11] = Properties.ToolSettings.Default.setColor_sec12;
                _tool.secColors[12] = Properties.ToolSettings.Default.setColor_sec13;
                _tool.secColors[13] = Properties.ToolSettings.Default.setColor_sec14;
                _tool.secColors[14] = Properties.ToolSettings.Default.setColor_sec15;
                _tool.secColors[15] = Properties.ToolSettings.Default.setColor_sec16;
            }

            bool multi = cboxIsMulti.IsChecked == true;
            Properties.ToolSettings.Default.setColor_isMultiColorSections = multi;
            if (_tool != null)
            {
                _tool.isMultiColoredSections = multi;
            }

            Properties.ToolSettings.Default.Save();

            isClosing = true;
            Close(true);
        }

        // --- Close guard ------------------------------------------------------------------------------

        /// <summary>
        /// [XPLAT] WinForms <c>FormSectionColor_FormClosing</c>: the dialog cannot be dismissed except
        /// through OK — any other close attempt is cancelled.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!isClosing)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        // --- Colour helpers ---------------------------------------------------------------------------

        /// <summary>Converts a <see cref="System.Drawing.Color"/> to an <see cref="Avalonia.Media.Color"/> (channels preserved).</summary>
        private static Color ToAv(System.Drawing.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);

        /// <summary>Converts an <see cref="Avalonia.Media.Color"/> to a <see cref="System.Drawing.Color"/> (channels preserved).</summary>
        private static System.Drawing.Color ToSd(Color c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

        /// <summary>
        /// Packs an <see cref="Avalonia.Media.Color"/> into an ARGB int (0xAARRGGBB), byte-identical to
        /// <c>System.Drawing.Color.ToArgb()</c>, so persisted preset ints match the WinForms originals.
        /// </summary>
        private static int ToArgb(Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;

        /// <summary>Reads the solid colour painted on a control's background, defaulting to black.</summary>
        private static Color GetColor(IBrush brush) => brush is ISolidColorBrush s ? s.Color : Colors.Black;

        /// <summary>
        /// Swaps the chkUse glyph between the locked and unlocked icons (the WinForms code set
        /// <c>chkUse.Image</c>; here the named inner <c>imgChkUse</c> image is updated). A missing asset
        /// leaves the previous glyph in place rather than throwing.
        /// </summary>
        private void SetLockGlyph(bool unlocked)
        {
            Bitmap glyph = unlocked
                ? (_unlockedGlyph ??= LoadGlyph("ColorUnlocked.png"))
                : (_lockedGlyph ??= LoadGlyph("ColorLocked.png"));

            if (glyph != null)
            {
                imgChkUse.Source = glyph;
            }
        }

        /// <summary>
        /// Loads a button glyph from the application's embedded <c>avares://AgOpenGPS/btnImages/</c>
        /// assets. Returns <c>null</c> on any failure so a missing asset degrades gracefully.
        /// </summary>
        private static Bitmap LoadGlyph(string fileName)
        {
            try
            {
                return new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + fileName)));
            }
            catch
            {
                return null;
            }
        }
    }
}
