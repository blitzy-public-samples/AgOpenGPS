// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Color Set" day/night palette dialog. This is a 1:1 BEHAVIOURAL
// PARITY reimplementation of the WinForms Forms/Settings/FormColor.cs (224 lines) — never a redesign
// (AAP §0.3.3). It pairs with FormColorView.axaml (x:Class="AgOpenGPS.Views.Settings.FormColorView").
//
// WHAT CHANGED vs the WinForms original, and WHY (AAP §0.1.2 / §0.3.2 / §0.5 / §0.6):
//   * NO FormGPS / NO `mf`. The WinForms form mutated FormGPS fields directly
//     (mf.frameDayColor … mf.customColorsList, mf.camSmoothFactor, mf.isDay) and called
//     mf.SwapDayNightMode(). The cross-platform build removes the WinForms FormGPS god-object, so this
//     view depends on an INJECTED collaborator — <see cref="IColorSettingsState"/> — that owns the same
//     colour fields plus the day/night swap + render invalidation. This is the dependency-inversion
//     seam mandated by AAP §0.3.2 (MVVM / abstraction layer); the composition root supplies the concrete
//     implementation (which routes SwapDayNightMode through App.ApplyDayNightMode + invalidates the GL
//     viewport). The interface is declared here because no concrete state type exists in this file's
//     dependency set yet (the Services/Core layer is still being extracted by the parallel migration).
//   * Avalonia ColorPicker REPLACES MechanikaDesign FormColorPicker (AAP §0.5.1). Each colour button
//     opens a small modal window hosting Avalonia.Controls.ColorPicker (no third-party package); the
//     WinForms "new FormColorPicker(mf, colour).ShowDialog(this) == OK → useThisColor" flow becomes
//     "await PickColorAsync(seed) → picked colour".
//   * Settings persistence is unchanged in SCHEMA (AAP §0.5 — frozen XML): the colour keys are still
//     System.Drawing.Color (in-box System.Drawing.Primitives on net8), camera-smooth is still an int and
//     the preset CSV is still a string, so every Properties.Settings write below is byte-identical to the
//     WinForms source. UI colours are Avalonia.Media.Color, converted to/from System.Drawing.Color via
//     the ARGB channels (helpers below).
//   * All numeric / CSV parsing uses CultureInfo.InvariantCulture (AAP §0.6.5 — a comma-decimal locale
//     must never corrupt the persisted palette).
//
// PARITY CONTRACT preserved exactly from FormColor.cs:
//   - Each colour handler: ensure the matching day/night mode is showing → open the picker → (on OK)
//     store the chosen colour → ALWAYS persist to Settings + Save → double-swap (a net no-op that forces
//     the live re-render). The colour assignment is conditional on OK; the Save + double-swap are not.
//   - OK: if the mode was changed while editing, restore the original mode; map the smoothing value to
//     camSmoothFactor (value*0.004 + 0.15); persist the smoothing value; close.
//   - Reset: restore the documented default palette + the exact preset CSV, clamp every preset with
//     CheckColorFor255, repack ARGB into the 16-entry preset list, then double-swap.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AgOpenGPS.Core.Translations;
using AvColor = Avalonia.Media.Color;
using SdColor = System.Drawing.Color;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Dependency-inversion seam for the "Color Set" dialog (AAP §0.3.2). It exposes exactly the
    /// FormGPS surface the WinForms <c>FormColor</c> mutated, so the view stays a thin, testable adapter:
    /// the seven day/night colours, the 16-entry custom-preset list, the camera-smoothing factor, the
    /// current day/night flag, and the day/night toggle. The concrete implementation lives in the
    /// composition root / Core layer; <see cref="SwapDayNightMode"/> there also flips
    /// <c>Application.Current.RequestedThemeVariant</c> (per the App.axaml wiring) and invalidates the GL
    /// viewport, mirroring the WinForms <c>FormGPS.SwapDayNightMode()</c>.
    /// </summary>
    /// <remarks>
    /// Colours are <see cref="System.Drawing.Color"/> to keep the frozen settings schema (AAP §0.5) and
    /// the <c>CheckColorFor255</c> extension intact; the view converts to/from
    /// <see cref="Avalonia.Media.Color"/> via the ARGB channels for display and picking.
    /// </remarks>
    public interface IColorSettingsState
    {
        /// <summary><see langword="true"/> when the day palette is currently shown (WinForms <c>mf.isDay</c>).</summary>
        bool IsDay { get; }

        /// <summary>Day frame/chrome colour (WinForms <c>mf.frameDayColor</c>).</summary>
        SdColor FrameDayColor { get; set; }

        /// <summary>Night frame/chrome colour (WinForms <c>mf.frameNightColor</c>).</summary>
        SdColor FrameNightColor { get; set; }

        /// <summary>Day field/background colour (WinForms <c>mf.fieldColorDay</c>).</summary>
        SdColor FieldColorDay { get; set; }

        /// <summary>Night field/background colour (WinForms <c>mf.fieldColorNight</c>).</summary>
        SdColor FieldColorNight { get; set; }

        /// <summary>Day text colour (WinForms <c>mf.textColorDay</c>).</summary>
        SdColor TextColorDay { get; set; }

        /// <summary>Night text colour (WinForms <c>mf.textColorNight</c>).</summary>
        SdColor TextColorNight { get; set; }

        /// <summary>Section/accent colour (WinForms <c>mf.sectionColorDay</c>); reset by this dialog.</summary>
        SdColor SectionColorDay { get; set; }

        /// <summary>
        /// The 16-entry ARGB-packed custom-preset palette (WinForms <c>mf.customColorsList</c>). Returned
        /// by reference so the dialog can write individual slots in place, exactly as the WinForms code did.
        /// </summary>
        int[] CustomColorsList { get; }

        /// <summary>Camera-smoothing factor (WinForms <c>mf.camSmoothFactor</c>), committed on OK.</summary>
        double CamSmoothFactor { get; set; }

        /// <summary>
        /// Toggles the day/night preview (WinForms <c>mf.SwapDayNightMode()</c>): recolours the chrome,
        /// flips the application theme variant and invalidates the live render.
        /// </summary>
        void SwapDayNightMode();
    }

    /// <summary>
    /// [XPLAT] The "Color Set" dialog window. Imperative view (no DataContext / bindings): the code-behind
    /// addresses the x:Named controls directly, mirroring the WinForms <c>FormColor</c> which set control
    /// state in its constructor / Load handler.
    /// </summary>
    public partial class FormColorView : Window
    {
        // [XPLAT] Injected colour/day-night state (replaces the WinForms `mf` FormGPS reference). Null only
        // on the parameterless design-time/previewer path, where every handler short-circuits.
        private readonly IColorSettingsState _state;

        // [XPLAT] Parity with FormColor.cs `private bool daySet;` — the day/night mode captured when the
        // dialog opened, restored on OK so previewing the opposite palette is not made permanent.
        private bool daySet;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer (both instantiate the window without arguments). It loads the markup and applies the
        /// localized chrome; application code uses <see cref="FormColorView(IColorSettingsState)"/>.
        /// </summary>
        public FormColorView()
        {
            InitializeComponent();

            // [XPLAT] Localized text — verbatim from FormColor.cs: this.Text = gStr.gsColors;
            // labelCameraBehavior/labelReset/labelSmooth/labelDirect from their gStr keys. The .axaml
            // strings are design-time placeholders replaced here at runtime.
            Title = gStr.gsColors;
            labelCameraBehavior.Text = gStr.gsCameraBehavior;
            labelReset.Text = gStr.gsReset;
            labelSmooth.Text = gStr.gsSmooth;
            labelDirect.Text = gStr.gsDirect;

            UpdateSmoothLabel();
        }

        /// <summary>
        /// [XPLAT] Application constructor: injects the colour/day-night state and seeds the dialog. Mirrors
        /// the WinForms <c>FormColor(Form callingForm)</c> ctor plus its <c>FormDisplaySettings_Load</c>
        /// (daySet capture + smoothing value). The WinForms <c>ScreenHelper.IsOnScreen</c> reposition is
        /// dropped because <c>WindowStartupLocation="CenterOwner"</c> handles placement (per the file spec).
        /// </summary>
        /// <param name="state">The injected colour/day-night collaborator; must not be <see langword="null"/>.</param>
        public FormColorView(IColorSettingsState state)
            : this()
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));

            // [XPLAT] FormDisplaySettings_Load: daySet = mf.isDay; hsbarSmooth.Value = setDisplay_camSmooth.
            daySet = _state.IsDay;
            hsbarSmooth.Value = Properties.Settings.Default.setDisplay_camSmooth;
            UpdateSmoothLabel();

            // [XPLAT] Seed the six preview swatches from the live colours so the dialog opens showing the
            // current palette (the Avalonia swatches replace the WinForms live-render-only feedback).
            RefreshAllSwatches();
        }

        // ===================================================================================================
        // Colour-button handlers — one per day/night colour slot. Each reproduces the EXACT FormColor.cs
        // sequence: ensure the relevant mode is visible, open the picker, (on OK) store the colour, ALWAYS
        // persist + Save, then double-swap to force the live re-render. Handlers are async void because the
        // modal picker is awaited (parity with the WinForms blocking ShowDialog).
        // ===================================================================================================

        // [XPLAT] FormColor.cs btnFrameDay_Click — day frame colour.
        private async void btnFrameDay_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (!_state.IsDay) _state.SwapDayNightMode(); // ensure day mode so the day colour is visible

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.FrameDayColor));
            if (picked.HasValue) _state.FrameDayColor = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorDayFrame = _state.FrameDayColor;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode(); // double-swap = net no mode change, forces re-render

            UpdateSwatch(swatchFrameDay, _state.FrameDayColor);
        }

        // [XPLAT] FormColor.cs btnFrameNight_Click — night frame colour.
        private async void btnFrameNight_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (_state.IsDay) _state.SwapDayNightMode(); // ensure night mode

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.FrameNightColor));
            if (picked.HasValue) _state.FrameNightColor = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorNightFrame = _state.FrameNightColor;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            UpdateSwatch(swatchFrameNight, _state.FrameNightColor);
        }

        // [XPLAT] FormColor.cs btnFieldDay_Click — day field colour.
        private async void btnFieldDay_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (!_state.IsDay) _state.SwapDayNightMode(); // ensure day mode

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.FieldColorDay));
            if (picked.HasValue) _state.FieldColorDay = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorFieldDay = _state.FieldColorDay;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            UpdateSwatch(swatchFieldDay, _state.FieldColorDay);
        }

        // [XPLAT] FormColor.cs btnFieldNight_Click — night field colour.
        private async void btnFieldNight_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (_state.IsDay) _state.SwapDayNightMode(); // ensure night mode

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.FieldColorNight));
            if (picked.HasValue) _state.FieldColorNight = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorFieldNight = _state.FieldColorNight;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            UpdateSwatch(swatchFieldNight, _state.FieldColorNight);
        }

        // [XPLAT] FormColor.cs btnDayText_Click — day text colour.
        private async void btnDayText_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (!_state.IsDay) _state.SwapDayNightMode(); // ensure day mode

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.TextColorDay));
            if (picked.HasValue) _state.TextColorDay = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorTextDay = _state.TextColorDay;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            UpdateSwatch(swatchDayText, _state.TextColorDay);
        }

        // [XPLAT] FormColor.cs btnNightText_Click — night text colour.
        private async void btnNightText_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (_state.IsDay) _state.SwapDayNightMode(); // ensure night mode

            AvColor? picked = await PickColorAsync(ToAvalonia(_state.TextColorNight));
            if (picked.HasValue) _state.TextColorNight = ToDrawing(picked.Value);

            Properties.Settings.Default.setDisplay_colorTextNight = _state.TextColorNight;
            Properties.Settings.Default.Save();

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            UpdateSwatch(swatchNightText, _state.TextColorNight);
        }

        // [XPLAT] FormColor.cs btnSwap_Click — toggle the day/night preview (and refresh the swatches).
        private void btnSwap_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            _state.SwapDayNightMode();
            RefreshAllSwatches();
        }

        // [XPLAT] FormColor.cs hsbarSmooth_ValueChanged — live "<value>%" read-out.
        private void hsbarSmooth_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateSmoothLabel();
        }

        // [XPLAT] FormColor.cs bntOK_Click — restore the original mode, commit the camera-smoothing value,
        // persist and close. The smoothing→factor mapping (value*0.004 + 0.15) is verbatim from the source.
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (_state != null)
            {
                if (daySet != _state.IsDay) _state.SwapDayNightMode(); // restore the mode the dialog opened in

                int smooth = (int)hsbarSmooth.Value;
                Properties.Settings.Default.setDisplay_camSmooth = smooth;
                _state.CamSmoothFactor = ((double)smooth * 0.004) + 0.15;

                Properties.Settings.Default.Save();
            }

            Close(true);
        }

        // [XPLAT] FormColor.cs btnReset_Click — restore the documented default palette + preset CSV.
        // The default ARGB values match the App.axaml Light/Dark theme dictionaries; the CSV is reproduced
        // byte-for-byte from the WinForms source (note the 8th entry is -1546731).
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            if (_state is null) return;

            if (!_state.IsDay) _state.SwapDayNightMode();

            _state.FrameDayColor = SdColor.FromArgb(255, 210, 210, 230);   // #FFD2D2E6
            _state.FrameNightColor = SdColor.FromArgb(255, 50, 50, 65);    // #FF323241
            _state.SectionColorDay = SdColor.FromArgb(255, 27, 151, 160);  // #FF1B97A0
            _state.FieldColorDay = SdColor.FromArgb(255, 100, 100, 125);   // #FF64647D
            _state.FieldColorNight = SdColor.FromArgb(255, 60, 60, 60);    // #FF3C3C3C
            _state.TextColorNight = SdColor.FromArgb(255, 230, 230, 230);  // #FFE6E6E6
            _state.TextColorDay = SdColor.FromArgb(255, 10, 10, 20);       // #FF0A0A14

            Properties.Settings.Default.setDisplay_colorDayFrame = _state.FrameDayColor;
            Properties.Settings.Default.setDisplay_colorNightFrame = _state.FrameNightColor;
            Properties.Settings.Default.setDisplay_colorSectionsDay = _state.SectionColorDay;
            Properties.Settings.Default.setDisplay_colorFieldDay = _state.FieldColorDay;
            Properties.Settings.Default.setDisplay_colorFieldNight = _state.FieldColorNight;
            Properties.Settings.Default.setDisplay_colorTextDay = _state.TextColorDay;
            Properties.Settings.Default.setDisplay_colorTextNight = _state.TextColorNight;

            Properties.Settings.Default.setDisplay_customColors =
                "-62208,-12299010,-16190712,-1505559,-3621034,-16712458,-7330570,-1546731,-24406,-3289866,-2756674,-538377,-134768,-4457734,-1848839,-530985";

            Properties.Settings.Default.Save();

            // [XPLAT] Parse the 16 ARGB ints with InvariantCulture (AAP §0.6.5), clamp each with the migrated
            // CheckColorFor255 extension (Classes/CExtensionMethods.cs), then repack the channels and write
            // the slot in place — identical to the WinForms loop.
            string[] words = Properties.Settings.Default.setDisplay_customColors.Split(',');
            for (int i = 0; i < 16; i++)
            {
                int argb = int.Parse(words[i], CultureInfo.InvariantCulture);
                SdColor test = SdColor.FromArgb(argb).CheckColorFor255();
                _state.CustomColorsList[i] = (test.A << 24) | (test.R << 16) | (test.G << 8) | test.B;
            }

            _state.SwapDayNightMode();
            _state.SwapDayNightMode();

            RefreshAllSwatches();
        }

        // ===================================================================================================
        // Helpers
        // ===================================================================================================

        /// <summary>
        /// [XPLAT] Opens a small modal window hosting <see cref="Avalonia.Controls.ColorPicker"/> seeded with
        /// <paramref name="seed"/>. Replaces the WinForms <c>new FormColorPicker(mf, colour).ShowDialog(this)
        /// == DialogResult.OK</c> idiom. Returns the chosen colour on OK, or <see langword="null"/> on Cancel.
        /// </summary>
        private async Task<AvColor?> PickColorAsync(AvColor seed)
        {
            var picker = new ColorPicker
            {
                Color = seed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 220
            };

            var okButton = new Button
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            var cancelButton = new Button
            {
                Content = gStr.gsCancel,
                IsCancel = true,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            var dialog = new Window
            {
                Title = gStr.gsColorPicker,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            okButton.Click += (_, __) => dialog.Close(true);
            cancelButton.Click += (_, __) => dialog.Close(false);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttonRow.Children.Add(okButton);
            buttonRow.Children.Add(cancelButton);

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(picker);
            root.Children.Add(buttonRow);

            dialog.Content = root;

            bool accepted = await dialog.ShowDialog<bool>(this);
            return accepted ? picker.Color : (AvColor?)null;
        }

        /// <summary>[XPLAT] Refreshes all six preview swatches from the injected state's live colours.</summary>
        private void RefreshAllSwatches()
        {
            if (_state is null) return;

            UpdateSwatch(swatchFrameDay, _state.FrameDayColor);
            UpdateSwatch(swatchFrameNight, _state.FrameNightColor);
            UpdateSwatch(swatchFieldDay, _state.FieldColorDay);
            UpdateSwatch(swatchFieldNight, _state.FieldColorNight);
            UpdateSwatch(swatchDayText, _state.TextColorDay);
            UpdateSwatch(swatchNightText, _state.TextColorNight);
        }

        /// <summary>[XPLAT] Paints a swatch <see cref="Border"/> with the given stored colour.</summary>
        private static void UpdateSwatch(Border swatch, SdColor color)
        {
            swatch.Background = new SolidColorBrush(ToAvalonia(color));
        }

        /// <summary>[XPLAT] Live "<value>%" read-out for the camera-smoothing slider (InvariantCulture).</summary>
        private void UpdateSmoothLabel()
        {
            lblSmoothCam.Text = ((int)hsbarSmooth.Value).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>[XPLAT] System.Drawing.Color → Avalonia.Media.Color, preserving the ARGB channels.</summary>
        private static AvColor ToAvalonia(SdColor color)
        {
            return AvColor.FromArgb(color.A, color.R, color.G, color.B);
        }

        /// <summary>[XPLAT] Avalonia.Media.Color → System.Drawing.Color, preserving the ARGB channels.</summary>
        private static SdColor ToDrawing(AvColor color)
        {
            return SdColor.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
