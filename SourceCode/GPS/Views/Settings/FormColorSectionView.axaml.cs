// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormColorSection.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the section-colour preset assignment dialog.
//
// Parity notes vs the WinForms original (FormColorSection):
//   * cb01..cb16 are CheckBoxes that behave as a RADIO group (only one selected at a time). The
//     shared cb_Click handler unchecks all then checks the sender and arms "use" mode, exactly like
//     the WinForms cb01_Click.
//   * btnC01..btnC16 are the preset palette. The shared btnC_Click reproduces the WinForms logic:
//       - In "use" mode, the clicked preset's colour is applied to the currently selected section
//         swatch (UpdateColor), then the selection clears. This is fully self-contained.
//       - In "change" mode, the WinForms form opened FormColorPicker and, on OK, stored the chosen
//         colour into the preset and persisted it. The colour picker dialog and the Settings write
//         are host responsibilities (the picker is a separate Avalonia view and Settings persistence
//         is being de-Windowsed), so this raises PresetColorEditRequested with the preset index and
//         current colour; the host opens the picker and calls SetPresetColor with the result. This
//         is the dependency-inversion seam mandated by AAP §0.3.2.
//   * cboxIsMulti_Click enables/disables the palette + swatch controls via SetGui, identical to the
//     WinForms SetGui(bool).
//   * chkUse_CheckedChanged (wired via IsCheckedChanged) flips imgChkUse between ColorLocked.png and
//     ColorUnlocked.png and retitles groupBoxSelectPreset, with the same isUse/isChange state the
//     WinForms handler set.
//   * btnOK_Click gathered the 16 swatch colours and the multi-colour flag and wrote them to
//     ToolSettings + mf.tool. Persisting to Settings and pushing into the live tool model is the
//     host's job, so OK raises ColorsAccepted with the colours + flag and closes with a positive
//     result.
//   * The initial swatch / preset colours and the multi-colour flag came from ToolSettings on load;
//     the host seeds them through SetSectionColors / SetPresetColors / SetMultiColor.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormColorSectionView : Window
    {
        /// <summary>Event payload carrying the accepted section colours and multi-colour flag.</summary>
        public sealed class SectionColorsEventArgs : EventArgs
        {
            public SectionColorsEventArgs(IReadOnlyList<Color> sectionColors, bool isMultiColor)
            {
                SectionColors = sectionColors;
                IsMultiColor = isMultiColor;
            }

            public IReadOnlyList<Color> SectionColors { get; }
            public bool IsMultiColor { get; }
        }

        /// <summary>Event payload for a preset colour edit request (opens the host colour picker).</summary>
        public sealed class PresetColorEditEventArgs : EventArgs
        {
            public PresetColorEditEventArgs(int presetIndex, Color currentColor)
            {
                PresetIndex = presetIndex;
                CurrentColor = currentColor;
            }

            /// <summary>1-based preset index (1..16), matching the WinForms button numbering.</summary>
            public int PresetIndex { get; }
            public Color CurrentColor { get; }
        }

        private const int SectionCount = 16;

        // Lock/unlock glyphs swapped into imgChkUse (parity with Resources.ColorLocked/ColorUnlocked).
        private static readonly Uri LockedUri = new Uri("avares://AgOpenGPS/btnImages/ColorLocked.png");
        private static readonly Uri UnlockedUri = new Uri("avares://AgOpenGPS/btnImages/ColorUnlocked.png");

        private readonly CheckBox[] _swatches;
        private readonly Button[] _presets;

        // isUse / isChange state machine (defaults match FormColorSection.cs).
        private bool _isUse = true;
        private bool _isChange;

        public FormColorSectionView()
        {
            InitializeComponent();

            _swatches = new[]
            {
                cb01, cb02, cb03, cb04, cb05, cb06, cb07, cb08,
                cb09, cb10, cb11, cb12, cb13, cb14, cb15, cb16
            };
            _presets = new[]
            {
                btnC01, btnC02, btnC03, btnC04, btnC05, btnC06, btnC07, btnC08,
                btnC09, btnC10, btnC11, btnC12, btnC13, btnC14, btnC15, btnC16
            };
        }

        /// <summary>Raised on OK with the chosen section colours and multi-colour flag.</summary>
        public event EventHandler<SectionColorsEventArgs> ColorsAccepted;

        /// <summary>Raised when a preset is clicked in edit mode; the host opens the colour picker.</summary>
        public event EventHandler<PresetColorEditEventArgs> PresetColorEditRequested;

        /// <summary>Host entry point: seed the 16 section swatch colours (from ToolSettings on open).</summary>
        public void SetSectionColors(IReadOnlyList<Color> colors)
        {
            if (colors == null) return;
            for (int i = 0; i < SectionCount && i < colors.Count; i++)
            {
                _swatches[i].Background = new SolidColorBrush(colors[i]);
            }
        }

        /// <summary>Host entry point: seed the 16 preset palette colours.</summary>
        public void SetPresetColors(IReadOnlyList<Color> colors)
        {
            if (colors == null) return;
            for (int i = 0; i < SectionCount && i < colors.Count; i++)
            {
                _presets[i].Background = new SolidColorBrush(colors[i]);
            }
        }

        /// <summary>Host entry point: set one preset's colour after the colour picker returns.</summary>
        public void SetPresetColor(int presetIndex, Color color)
        {
            if (presetIndex < 1 || presetIndex > SectionCount) return;
            _presets[presetIndex - 1].Background = new SolidColorBrush(color);
        }

        /// <summary>Host entry point: seed the multi-colour flag (drives the enable/disable GUI state).</summary>
        public void SetMultiColor(bool isMultiColor)
        {
            cboxIsMulti.IsChecked = isMultiColor;
            SetGui(isMultiColor);
        }

        // cb_Click: radio behaviour — clear all, then select the sender; arm "use" mode.
        private void cb_Click(object sender, RoutedEventArgs e)
        {
            UncheckAllSwatches();
            if (sender is CheckBox cbox)
            {
                cbox.IsChecked = true;
            }
            _isUse = true;
        }

        // btnC_Click: apply a preset (use mode) or request a preset edit (change mode).
        private void btnC_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button)) return;

            if (_isUse)
            {
                ApplyColorToSelected(GetColor(button.Background));
                _isUse = false;
            }
            else if (_isChange)
            {
                int presetIndex = ParsePresetIndex(button.Name);
                PresetColorEditRequested?.Invoke(this, new PresetColorEditEventArgs(presetIndex, GetColor(button.Background)));
                _isChange = false;
            }

            chkUse.IsChecked = false;
        }

        // cboxIsMulti_Click: enable/disable palette + swatches (WinForms SetGui).
        private void cboxIsMulti_Click(object sender, RoutedEventArgs e)
        {
            SetGui(cboxIsMulti.IsChecked == true);
        }

        // chkUse_CheckedChanged: flip the lock glyph, retitle the preset header, and set use/change state.
        private void chkUse_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (chkUse.IsChecked == true)
            {
                groupBoxSelectPreset.Text = "Select Square Below And Pick New Color";
                SetLockGlyph(UnlockedUri);
                _isChange = true;
                _isUse = false;
            }
            else
            {
                _isChange = false;
                _isUse = false;
                groupBoxSelectPreset.Text = "Select Preset Color";
                SetLockGlyph(LockedUri);
            }
        }

        // btnOK_Click: hand the 16 swatch colours + multi flag to the host and close.
        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            var colors = new Color[SectionCount];
            for (int i = 0; i < SectionCount; i++)
            {
                colors[i] = GetColor(_swatches[i].Background);
            }
            ColorsAccepted?.Invoke(this, new SectionColorsEventArgs(colors, cboxIsMulti.IsChecked == true));
            Close(true);
        }

        // Enable/disable every preset, swatch and the edit-lock toggle (WinForms SetGui(bool)).
        private void SetGui(bool enabled)
        {
            foreach (Button preset in _presets) preset.IsEnabled = enabled;
            foreach (CheckBox swatch in _swatches) swatch.IsEnabled = enabled;
            chkUse.IsEnabled = enabled;
        }

        // Apply a colour to whichever swatch is currently selected, then clear the selection
        // (WinForms UpdateColor).
        private void ApplyColorToSelected(Color color)
        {
            foreach (CheckBox swatch in _swatches)
            {
                if (swatch.IsChecked == true)
                {
                    swatch.Background = new SolidColorBrush(color);
                    break;
                }
            }
            UncheckAllSwatches();
            _isUse = false;
        }

        private void UncheckAllSwatches()
        {
            foreach (CheckBox swatch in _swatches)
            {
                swatch.IsChecked = false;
            }
        }

        private void SetLockGlyph(Uri uri)
        {
            // Swap the lock/unlock glyph from the GPS assembly's embedded avares assets.
            imgChkUse.Source = new Bitmap(AssetLoader.Open(uri));
        }

        private static Color GetColor(IBrush brush)
        {
            return brush is ISolidColorBrush solid ? solid.Color : Colors.Black;
        }

        // Parse the 1-based preset index from the control name ("btnC01".."btnC16").
        private static int ParsePresetIndex(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 5) return 0;
            return int.TryParse(name.Substring(4), out int index) ? index : 0;
        }
    }
}
