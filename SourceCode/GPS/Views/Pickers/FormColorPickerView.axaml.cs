// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] WinForms FormColorPicker (Forms/Pickers/FormColorPicker.cs + .Designer.cs) ->
// Avalonia FormColorPickerView. Behavioural transformations made during the migration:
//   * The FormGPS `mf` god-object coupling is REMOVED via constructor injection — the dialog now
//     receives the initial colour and the 16-entry custom-colour preset array as plain values
//     (no `mf`, no new interface; AAP §0.3.2).
//   * The result is RETURNED through Close(Color) / ShowDialog<Color?> instead of the old public
//     `useThisColor` field combined with the WinForms DialogResult.OK.
//   * The abandoned, Windows-only MechanikaDesign ColorBox2D / ColorSliderVertical controls are
//     replaced by Avalonia's cross-platform ColorSpectrum (`colorBox2D`) and ColorSlider
//     (`colorSlider`); the WinForms HslColor round-trip is replaced by Avalonia's HsvColor (AAP §0.5.1).
//   * Every numeric ToString/Parse uses CultureInfo.InvariantCulture so the persisted preset CSV is
//     byte-identical across Windows/Linux/macOS locales (AAP §0.6.5).
// No System.Windows.Forms, no System.Drawing, no OpenTK / OpenTK.GLControl, no MechanikaDesign types.
using System;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgOpenGPS.Core.Translations;
// [XPLAT] The GPS settings store is referenced through its fully-qualified namespace path
// (Properties.Settings.Default) — the source idiom — rather than `using AgOpenGPS.Properties;`.
// Reason: this view lives in AgOpenGPS.Views.Pickers, so the enclosing AgOpenGPS.Views namespace is
// in scope, and a bare `Settings` would bind to the SIBLING namespace AgOpenGPS.Views.Settings
// (an enclosing-namespace member is resolved before any outer file-level using/alias). Writing
// `Properties.Settings` resolves `Properties` unambiguously to AgOpenGPS.Properties (there is no
// AgOpenGPS.Views.Properties) and reaches the AgOpenGPS.Properties.Settings type the migration needs.

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the colour picker dialog — a 1:1 behavioural parity port of the
    /// WinForms <c>FormColorPicker</c>. The operator either dials in a colour with the 2-D spectrum
    /// (<c>colorBox2D</c>) and the vertical hue slider (<c>colorSlider</c>), or clicks one of sixteen
    /// preset swatches. The chosen colour is exposed through <see cref="UseThisColor"/> and returned
    /// to the caller when the dialog closes.
    /// </summary>
    /// <remarks>
    /// This is an imperative, code-behind-driven dialog (no <c>DataContext</c>, no <c>x:DataType</c>,
    /// no bindings): controls are addressed by <c>x:Name</c> and every handler is subscribed here with
    /// <c>+=</c>, mirroring the original WinForms Designer. The dialog is self-contained — the sixteen
    /// presets are injected (and mutated in place) plus persisted to
    /// <c>Settings.Default.setDisplay_customColors</c>, so no <c>FormGPS</c> reference is required.
    /// </remarks>
    public partial class FormColorPickerView : Window
    {
        // [XPLAT] The injected 16-entry preset array (replaces the FormGPS `mf.customColorsList`).
        // Each int is an ARGB-packed colour, identical in layout to System.Drawing.Color.ToArgb().
        // Mutated in place when a preset is saved, exactly as the WinForms code mutated mf's array.
        private readonly int[] _customColors;

        // [XPLAT] Mirrors the source `isUse` flag. true  => clicking a swatch APPLIES its colour;
        // false => clicking a swatch SAVES the current colour into that preset slot. The source
        // initialises isUse = true and chkUse starts unchecked (locked glyph), so true is the
        // start state here too.
        private bool _isUse = true;

        // [XPLAT] Re-entrancy guard: assigning HsvColor on one editor raises its ColorChanged, which
        // would otherwise bounce back and re-sync the sibling in an infinite loop.
        private bool _suppressColorSync;

        // [XPLAT] The sixteen preset swatch buttons, indexed 0..15 by their Tag ("00".."15").
        private readonly Button[] _swatches;

        // [XPLAT] Lazily-loaded, cached lock/unlock glyphs for chkUse (avoids re-reading the asset on
        // every toggle). A missing asset degrades gracefully to a null source (no glyph) rather than
        // throwing.
        private Bitmap _lockedGlyph;
        private Bitmap _unlockedGlyph;

        /// <summary>
        /// [XPLAT] The colour the operator selected. Replaces the WinForms public
        /// <c>useThisColor</c> field (was <c>System.Drawing.Color</c>; now
        /// <see cref="Avalonia.Media.Color"/>). The caller reads it from the
        /// <c>ShowDialog&lt;Color?&gt;</c> result, or directly after the dialog closes.
        /// </summary>
        public Color UseThisColor { get; private set; }

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, both of which instantiate the view without arguments. It loads the markup, caches
        /// the swatch buttons, and wires every event handler programmatically (the <c>.axaml</c>
        /// declares none). Application code constructs the dialog through
        /// <see cref="FormColorPickerView(Color, int[])"/>.
        /// </summary>
        public FormColorPickerView()
        {
            InitializeComponent();

            // [XPLAT] Cache the sixteen preset swatches (replaces the individually-named btn00..btn15
            // fields the WinForms Designer added one by one). A safe empty preset set keeps the array
            // non-null on the previewer-only path; the parameterised ctor replaces it with the injected
            // array.
            _customColors = new int[16];
            _swatches = new[]
            {
                btn00, btn01, btn02, btn03, btn04, btn05, btn06, btn07,
                btn08, btn09, btn10, btn11, btn12, btn13, btn14, btn15
            };

            // [XPLAT] Designer-wired events reproduced programmatically. The two editors share one
            // handler (Editor_ColorChanged); all sixteen swatches share Preset_Click — exactly as the
            // WinForms Designer pointed every btnXX.Click at the single btn00_Click handler.
            colorBox2D.ColorChanged += Editor_ColorChanged;
            colorSlider.ColorChanged += Editor_ColorChanged;
            chkUse.IsCheckedChanged += ChkUse_Changed;
            btnSave.Click += BtnSave_Click;
            foreach (Button swatch in _swatches)
            {
                swatch.Click += Preset_Click;
            }
        }

        /// <summary>
        /// [XPLAT] Parity with the WinForms <c>FormColorPicker(Form callingForm, Color _inColor)</c>
        /// constructor, with the <c>FormGPS</c> argument replaced by injected values.
        /// </summary>
        /// <param name="initialColor">The colour the dialog opens on (the original <c>_inColor</c>).</param>
        /// <param name="customColors">
        /// The 16-entry ARGB-packed preset array (the original <c>mf.customColorsList</c>). It is
        /// mutated in place as presets are saved, so the caller observes the updated presets.
        /// </param>
        public FormColorPickerView(Color initialColor, int[] customColors)
            : this()
        {
            _customColors = customColors;

            // [XPLAT] Localise the chrome — verbatim from the WinForms ctor (this.Text/btnDay.Text/
            // btnNight.Text/groupBoxSelectPresetColor.Text). Button captions become Button.Content.
            Title = gStr.gsColorPicker;
            btnDay.Content = gStr.gsDay;
            btnNight.Content = gStr.gsNight;
            groupBoxSelectPresetColor.Text = gStr.gsPresetColor;

            // [XPLAT] Source ctor: useThisColor = inColor; UpdateColor(inColor). Seeds the spectrum,
            // the slider and both Day/Night preview swatches from the initial colour.
            UpdateColor(initialColor);

            // [XPLAT] Source FormColorPicker_Load: clamp every stored preset, write the clamped value
            // back into the array, paint each swatch, then persist once.
            LoadPresets();
        }

        /// <summary>
        /// [XPLAT] Shared replacement for the source <c>colorBox2D_ColorChanged</c> and
        /// <c>colorSlider_ColorChanged</c> handlers. Reads the new colour, clamps it, keeps the
        /// sibling editor in sync (the source synced the other control via HSL), and refreshes the
        /// public colour plus both preview swatches.
        /// </summary>
        private void Editor_ColorChanged(object sender, ColorChangedEventArgs e)
        {
            if (_suppressColorSync)
            {
                return;
            }

            Color rgb = CheckColorFor255(e.NewColor);

            // [XPLAT] Sync the OTHER editor only (the source never wrote the clamped value back into
            // the control that changed), guarded so the assignment does not re-enter this handler.
            _suppressColorSync = true;
            if (ReferenceEquals(sender, colorBox2D))
            {
                colorSlider.HsvColor = rgb.ToHsv();
            }
            else
            {
                colorBox2D.HsvColor = rgb.ToHsv();
            }
            _suppressColorSync = false;

            ApplySelectedColor(rgb);
        }

        /// <summary>
        /// [XPLAT] Source <c>UpdateColor</c>: clamp the colour, drive BOTH editors to its HSV, and
        /// refresh the public colour and preview swatches.
        /// </summary>
        private void UpdateColor(Color col)
        {
            col = CheckColorFor255(col);

            _suppressColorSync = true;
            HsvColor hsv = col.ToHsv();
            colorSlider.HsvColor = hsv;
            colorBox2D.HsvColor = hsv;
            _suppressColorSync = false;

            ApplySelectedColor(col);
        }

        /// <summary>
        /// [XPLAT] Shared tail of the colour-change paths: store the result and recolour the Day/Night
        /// preview buttons (the source set <c>btnDay.BackColor</c> and <c>btnNight.BackColor</c>).
        /// </summary>
        private void ApplySelectedColor(Color col)
        {
            UseThisColor = col;
            btnDay.Background = new SolidColorBrush(col);
            btnNight.Background = new SolidColorBrush(col);
        }

        /// <summary>
        /// [XPLAT] Source <c>btn00_Click</c>, shared by all sixteen swatches. In "use" mode a click
        /// adopts the swatch colour; in "save" mode it writes the current colour into the Tag-indexed
        /// preset slot, mutates the injected array, and persists.
        /// </summary>
        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;

            if (_isUse)
            {
                // [XPLAT] Apply mode: useThisColor = swatch.BackColor.CheckColorFor255(); UpdateColor(..).
                if (button.Background is ISolidColorBrush scb)
                {
                    UpdateColor(CheckColorFor255(scb.Color));
                }
            }
            else
            {
                // [XPLAT] Save mode. Index comes from Tag ("00".."15") instead of the WinForms
                // Name.Substring(3, 2); InvariantCulture keeps parsing locale-independent (§0.6.5).
                int index = int.Parse((string)button.Tag, CultureInfo.InvariantCulture);

                UseThisColor = CheckColorFor255(UseThisColor);
                int iCol = ToArgb(UseThisColor);
                _customColors[index] = iCol;
                button.Background = new SolidColorBrush(UseThisColor);

                SaveCustomColor();
            }
        }

        /// <summary>
        /// [XPLAT] Source <c>chkUse_CheckedChanged</c>. Swaps the lock glyph and the group caption and
        /// flips the use/save mode. The two captions are HARD-CODED English literals in the WinForms
        /// source (not gStr keys) and are preserved verbatim for parity. The checked PaleGreen
        /// background is supplied declaratively by the <c>.axaml</c> (<c>:checked</c> style).
        /// </summary>
        private void ChkUse_Changed(object sender, RoutedEventArgs e)
        {
            if (chkUse.IsChecked == true)
            {
                groupBoxSelectPresetColor.Text = "Pick New Color and Select Square Below to Save Preset";
                chkUseImage.Source = UnlockedGlyph;
                _isUse = false;
            }
            else
            {
                _isUse = true;
                groupBoxSelectPresetColor.Text = "Select Preset Color";
                chkUseImage.Source = LockedGlyph;
            }
        }

        /// <summary>
        /// [XPLAT] Source <c>btnSave_Click</c> (the button carried DialogResult.OK). Closes the dialog
        /// returning the chosen colour to a <c>ShowDialog&lt;Color?&gt;</c> caller.
        /// </summary>
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Close(UseThisColor);
        }

        /// <summary>
        /// [XPLAT] Source <c>FormColorPicker_Load</c> body. Clamps every stored preset (no channel
        /// stays at 255), writes the clamped value back into the injected array, paints the matching
        /// swatch, then persists the cleaned set once.
        /// </summary>
        private void LoadPresets()
        {
            for (int i = 0; i < 16; i++)
            {
                Color clamped = CheckColorFor255(FromArgb(_customColors[i]));
                _customColors[i] = ToArgb(clamped);
                _swatches[i].Background = new SolidColorBrush(clamped);
            }

            SaveCustomColor();
        }

        /// <summary>
        /// [XPLAT] Source <c>SaveCustomColor</c>. Serialises the sixteen preset ints to the CSV setting
        /// in the exact original layout — indices 0..14 each followed by a comma, then index 15 with no
        /// trailing comma — using InvariantCulture so the persisted text is byte-identical on every OS.
        /// </summary>
        private void SaveCustomColor()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 15; i++)
            {
                sb.Append(_customColors[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            }
            sb.Append(_customColors[15].ToString(CultureInfo.InvariantCulture));

            Properties.Settings.Default.setDisplay_customColors = sb.ToString();
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// [XPLAT] Local replacement for the WinForms <c>Color.CheckColorFor255()</c> extension
        /// (CExtensionMethods.cs): clamps each fully-saturated (255) RGB channel down to 254 while
        /// preserving the alpha channel, so a colour never round-trips as pure 255 in a channel.
        /// </summary>
        private static Color CheckColorFor255(Color c)
        {
            byte r = c.R == 255 ? (byte)254 : c.R;
            byte g = c.G == 255 ? (byte)254 : c.G;
            byte b = c.B == 255 ? (byte)254 : c.B;
            return Color.FromArgb(c.A, r, g, b);
        }

        /// <summary>
        /// [XPLAT] Pack a colour into a 32-bit ARGB int with the identical bit layout to
        /// <c>System.Drawing.Color.ToArgb()</c>, so the persisted preset ints match the originals.
        /// </summary>
        private static int ToArgb(Color c) => (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;

        /// <summary>
        /// [XPLAT] Unpack a 32-bit ARGB int into a colour, mirroring
        /// <c>System.Drawing.Color.FromArgb(int)</c> (0xAARRGGBB), so the injected preset ints decode
        /// identically.
        /// </summary>
        private static Color FromArgb(int i) =>
            Color.FromArgb((byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i);

        // [XPLAT] Cached lock/unlock glyphs, loaded on first use from the packaged Avalonia resources.
        private Bitmap LockedGlyph => _lockedGlyph ??= LoadGlyph("ColorLocked.png");

        private Bitmap UnlockedGlyph => _unlockedGlyph ??= LoadGlyph("ColorUnlocked.png");

        /// <summary>
        /// [XPLAT] Best-effort glyph loader from the packaged <c>btnImages</c> Avalonia resources. A
        /// missing asset never breaks the dialog — it simply yields a null image source.
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
