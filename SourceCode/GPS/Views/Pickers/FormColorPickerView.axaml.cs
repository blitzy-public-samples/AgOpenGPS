// [XPLAT] migrated from net48/WinForms (Forms/Pickers/FormColorPicker.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views.Pickers
{
    /// <summary>
    /// [XPLAT] Code-behind for the colour picker — a faithful port of the WinForms
    /// <c>FormColorPicker</c> (FormColorPicker.cs + FormColorPicker.Designer.cs). Per AAP §0.5.1 the
    /// abandoned <c>MechanikaDesign</c> HSL controls are replaced by Avalonia's
    /// <see cref="ColorSpectrum"/> (the 2-D saturation/value plane, <c>colorBox2D</c>) and
    /// <see cref="ColorSlider"/> (the vertical hue slider, <c>colorSlider</c>). The operator picks a
    /// colour, or selects one of sixteen presets, and the chosen colour is exposed via
    /// <see cref="UseThisColor"/>.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The dialog is fully self-contained: the sixteen presets are loaded from and saved to
    /// <c>Settings.Default.setDisplay_customColors</c> (the same CSV the original persisted), so no
    /// FormGPS god-object is required.
    /// <list type="bullet">
    ///   <item><see cref="SetInitialColor"/> seeds the picker from the caller's colour (mirrors the
    ///         original <c>inColor</c> ctor argument + <c>UpdateColor</c>).</item>
    ///   <item>The spectrum and slider are kept in sync through <see cref="HsvColor"/> (replacing the
    ///         original <c>HslColor</c> round-trip), guarded against re-entrancy.</item>
    ///   <item>The sixteen swatches share <see cref="OnPresetClick"/>, reading the slot index from
    ///         <c>Tag</c> ("00".."15") instead of the WinForms <c>Name.Substring(3,2)</c>. In "use"
    ///         mode a click adopts the swatch colour; in "save" mode it writes the current colour into
    ///         that slot and persists.</item>
    ///   <item><see cref="OnUseToggled"/> swaps the lock glyph and group caption exactly as the
    ///         original <c>chkUse_CheckedChanged</c>; <see cref="OnSaveClick"/> closes the dialog.</item>
    /// </list>
    /// No fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormColorPickerView : Window
    {
        // [XPLAT] true = clicking a swatch adopts its colour; false = clicking a swatch saves the
        // current colour into that slot (the original isUse flag, inverted by chkUse).
        private bool _isUse = true;

        // [XPLAT] Re-entrancy guard: assigning HsvColor on one control raises ColorChanged on it.
        private bool _suppressColorSync;

        private Button[] _swatches;

        /// <summary>
        /// The colour the operator selected. Mirrors the original public <c>useThisColor</c> property;
        /// the caller reads it after the dialog closes.
        /// </summary>
        public Color UseThisColor { get; private set; }

        public FormColorPickerView()
        {
            InitializeComponent();

            _swatches = new[]
            {
                btn00, btn01, btn02, btn03, btn04, btn05, btn06, btn07,
                btn08, btn09, btn10, btn11, btn12, btn13, btn14, btn15
            };

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            colorBox2D.ColorChanged += OnSpectrumColorChanged;
            colorSlider.ColorChanged += OnSliderColorChanged;
            chkUse.IsCheckedChanged += OnUseToggled;
            btnSave.Click += OnSaveClick;
            foreach (Button swatch in _swatches)
            {
                swatch.Click += OnPresetClick;
            }
        }

        /// <summary>
        /// [XPLAT] Seed the picker from the caller's colour (the original ctor's <c>_inColor</c>).
        /// Forces full opacity, matching the original <c>CheckColorFor255()</c>.
        /// </summary>
        public void SetInitialColor(Color inColor)
        {
            UpdateColor(Force255(inColor));
        }

        // [XPLAT] FormColorPicker_Load: paint the sixteen preset swatches from the persisted CSV.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadPresetSwatches();
        }

        // [XPLAT] colorBox2D_ColorChanged: adopt the spectrum colour, keep the slider hue in sync.
        private void OnSpectrumColorChanged(object sender, ColorChangedEventArgs e)
        {
            if (_suppressColorSync)
            {
                return;
            }

            Color rgb = Force255(colorBox2D.Color);
            _suppressColorSync = true;
            colorSlider.HsvColor = colorBox2D.HsvColor;
            _suppressColorSync = false;
            ApplySelectedColor(rgb);
        }

        // [XPLAT] colorSlider_ColorChanged: adopt the slider colour, keep the spectrum plane in sync.
        private void OnSliderColorChanged(object sender, ColorChangedEventArgs e)
        {
            if (_suppressColorSync)
            {
                return;
            }

            Color rgb = Force255(colorSlider.Color);
            _suppressColorSync = true;
            colorBox2D.HsvColor = colorSlider.HsvColor;
            _suppressColorSync = false;
            ApplySelectedColor(rgb);
        }

        // [XPLAT] UpdateColor: set both controls' HSV from the colour and refresh the preview swatches.
        private void UpdateColor(Color col)
        {
            _suppressColorSync = true;
            HsvColor hsv = col.ToHsv();
            colorSlider.HsvColor = hsv;
            colorBox2D.HsvColor = hsv;
            _suppressColorSync = false;
            ApplySelectedColor(col);
        }

        // [XPLAT] Shared tail of the colour-change handlers: store the result and recolour the
        // Day/Night preview buttons (the original set btnDay/btnNight.BackColor).
        private void ApplySelectedColor(Color col)
        {
            UseThisColor = col;
            btnDay.Background = new SolidColorBrush(col);
            btnNight.Background = new SolidColorBrush(col);
        }

        // [XPLAT] btn00_Click ... btn15_Click (shared): use-mode adopts the swatch; save-mode writes
        // the current colour into the Tag-indexed slot and persists the CSV.
        private void OnPresetClick(object sender, RoutedEventArgs e)
        {
            var swatch = (Button)sender;

            if (_isUse)
            {
                if (swatch.Background is ISolidColorBrush scb)
                {
                    UpdateColor(Force255(scb.Color));
                }
            }
            else
            {
                Color col = Force255(UseThisColor);
                swatch.Background = new SolidColorBrush(col);
                SaveCustomColors();
            }
        }

        // [XPLAT] chkUse_CheckedChanged: toggle use/save mode, swap the lock glyph and the caption.
        private void OnUseToggled(object sender, RoutedEventArgs e)
        {
            if (chkUse.IsChecked == true)
            {
                _isUse = false;
                groupBoxSelectPresetColor.Text = "Pick New Color and Select Square Below to Save Preset";
                chkUseImage.Source = LoadGlyph("ColorUnlocked.png");
            }
            else
            {
                _isUse = true;
                groupBoxSelectPresetColor.Text = "Select Preset Color";
                chkUseImage.Source = LoadGlyph("ColorLocked.png");
            }
        }

        // [XPLAT] btnSave_Click: close the dialog (caller reads UseThisColor).
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // [XPLAT] Load the sixteen presets from Settings.setDisplay_customColors (CSV of ARGB ints).
        private void LoadPresetSwatches()
        {
            int[] colors = ParseCustomColors(Settings.Default.setDisplay_customColors);
            for (int i = 0; i < _swatches.Length && i < colors.Length; i++)
            {
                _swatches[i].Background = new SolidColorBrush(IntToColor(colors[i]));
            }
        }

        // [XPLAT] SaveCustomColor: serialise the sixteen swatch colours back to the CSV setting.
        private void SaveCustomColors()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _swatches.Length; i++)
            {
                Color col = _swatches[i].Background is ISolidColorBrush scb
                    ? Force255(scb.Color)
                    : Colors.Black;
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(ColorToInt(col).ToString(CultureInfo.InvariantCulture));
            }

            Settings.Default.setDisplay_customColors = sb.ToString();
            Settings.Default.Save();
        }

        // [XPLAT] Parse a CSV of up to sixteen signed ARGB integers (default-safe).
        private static int[] ParseCustomColors(string csv)
        {
            var result = new int[16];
            if (string.IsNullOrEmpty(csv))
            {
                return result;
            }

            string[] parts = csv.Split(',');
            for (int i = 0; i < result.Length && i < parts.Length; i++)
            {
                int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]);
            }

            return result;
        }

        // [XPLAT] Force full opacity (the original CheckColorFor255()).
        private static Color Force255(Color c)
        {
            return Color.FromArgb(255, c.R, c.G, c.B);
        }

        // [XPLAT] 32-bit signed ARGB int -> Color (matches WinForms Color.FromArgb(int)).
        private static Color IntToColor(int v)
        {
            byte a = (byte)((v >> 24) & 0xFF);
            byte r = (byte)((v >> 16) & 0xFF);
            byte g = (byte)((v >> 8) & 0xFF);
            byte b = (byte)(v & 0xFF);
            return Color.FromArgb(a, r, g, b);
        }

        // [XPLAT] Color -> 32-bit signed ARGB int (identical bit layout to the original iCol maths).
        private static int ColorToInt(Color c)
        {
            return (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
        }

        // [XPLAT] Best-effort glyph load; a missing asset never breaks the dialog.
        private static Bitmap LoadGlyph(string file)
        {
            try
            {
                return new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + file)));
            }
            catch
            {
                return null;
            }
        }
    }
}
