// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormColor.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the "Color Set" day/night palette dialog.
//
// Parity notes vs the WinForms original (FormColor):
//   * Six colour buttons (day text, day field, day frame, night text, night frame, night field)
//     each carry a named swatch Border whose Background shows the live colour.  In WinForms each
//     button opened a colour dialog and wrote the result to Properties.Settings.  Opening the
//     cross-platform colour picker and persisting to Settings is the host's responsibility (the
//     Settings persistence layer is being de-Windowsed in a separate workstream), so each button
//     raises ColorChangeRequested with its slot and current colour; the host opens the Avalonia
//     ColorPicker and calls SetSlotColor with the chosen colour, which updates the swatch here.
//     This is the dependency-inversion seam mandated by AAP §0.3.2.
//   * btnSwap reproduced mf.SwapDayNightMode() — raised here as SwapDayNightRequested for the host.
//   * btnReset restored the documented default palette.  The defaults are the swatch colours baked
//     into the markup; this view captures them at construction and restores them locally, then
//     raises ResetRequested so the host can persist the reset to Settings.
//   * hsbarSmooth replaces the WinForms HScrollBar: ValueChanged updates lblSmoothCam live as
//     "<value>%"; btnOK closes with a positive result and exposes SmoothValue, which the host maps
//     to mf.camSmoothFactor (parity with the WinForms OK handler).

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormColorView : Window
    {
        /// <summary>Identifies which day/night colour slot a change applies to.</summary>
        public enum ColorSlot
        {
            DayText,
            FieldDay,
            FrameDay,
            NightText,
            FrameNight,
            FieldNight
        }

        /// <summary>Event payload for a colour-change request raised from a colour button.</summary>
        public sealed class ColorSlotEventArgs : EventArgs
        {
            public ColorSlotEventArgs(ColorSlot slot, Color currentColor)
            {
                Slot = slot;
                CurrentColor = currentColor;
            }

            public ColorSlot Slot { get; }
            public Color CurrentColor { get; }
        }

        // Default palette captured from the markup swatches at construction (used by btnReset).
        private readonly Dictionary<ColorSlot, Color> _defaults = new Dictionary<ColorSlot, Color>();

        public FormColorView()
        {
            InitializeComponent();

            // Snapshot the design-time swatch colours so Reset can restore them without hard-coding
            // the literals twice.
            foreach (ColorSlot slot in Enum.GetValues(typeof(ColorSlot)))
            {
                _defaults[slot] = GetSlotColor(slot);
            }

            UpdateSmoothLabel();
        }

        /// <summary>Camera smoothing value the host maps to mf.camSmoothFactor on OK.</summary>
        public double SmoothValue => hsbarSmooth.Value;

        /// <summary>Raised when a colour button is clicked; the host opens the colour picker.</summary>
        public event EventHandler<ColorSlotEventArgs> ColorChangeRequested;

        /// <summary>Raised when Swap is clicked; the host toggles the day/night preview.</summary>
        public event EventHandler SwapDayNightRequested;

        /// <summary>Raised after Reset restores the default swatches; the host persists the reset.</summary>
        public event EventHandler ResetRequested;

        /// <summary>
        /// Host entry point: set a slot's live colour (e.g. from Settings on open, or from the colour
        /// picker result).  Updates the matching swatch Border.
        /// </summary>
        public void SetSlotColor(ColorSlot slot, Color color)
        {
            GetSwatch(slot).Background = new SolidColorBrush(color);
        }

        private Border GetSwatch(ColorSlot slot)
        {
            switch (slot)
            {
                case ColorSlot.DayText: return swatchDayText;
                case ColorSlot.FieldDay: return swatchFieldDay;
                case ColorSlot.FrameDay: return swatchFrameDay;
                case ColorSlot.NightText: return swatchNightText;
                case ColorSlot.FrameNight: return swatchFrameNight;
                case ColorSlot.FieldNight: return swatchFieldNight;
                default: return swatchDayText;
            }
        }

        private Color GetSlotColor(ColorSlot slot)
        {
            return GetSwatch(slot).Background is ISolidColorBrush brush ? brush.Color : Colors.Black;
        }

        private void RequestColorChange(ColorSlot slot)
        {
            ColorChangeRequested?.Invoke(this, new ColorSlotEventArgs(slot, GetSlotColor(slot)));
        }

        private void btnDayText_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.DayText);
        private void btnFieldDay_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.FieldDay);
        private void btnFrameDay_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.FrameDay);
        private void btnNightText_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.NightText);
        private void btnFrameNight_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.FrameNight);
        private void btnFieldNight_Click(object sender, RoutedEventArgs e) => RequestColorChange(ColorSlot.FieldNight);

        // Swap day/night preview (WinForms mf.SwapDayNightMode()).
        private void btnSwap_Click(object sender, RoutedEventArgs e)
        {
            SwapDayNightRequested?.Invoke(this, EventArgs.Empty);
        }

        // Reset all swatches to their captured defaults, then let the host persist the reset.
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (KeyValuePair<ColorSlot, Color> kvp in _defaults)
            {
                SetSlotColor(kvp.Key, kvp.Value);
            }
            ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        // OK: close with a positive result; the host reads SmoothValue and commits camSmoothFactor.
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // Live "<value>%" read-out for the camera-smoothing slider.
        private void hsbarSmooth_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateSmoothLabel();
        }

        private void UpdateSmoothLabel()
        {
            lblSmoothCam.Text = ((int)hsbarSmooth.Value).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}
