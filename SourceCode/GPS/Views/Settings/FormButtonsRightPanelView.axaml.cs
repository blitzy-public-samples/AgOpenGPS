// [XPLAT] migrated from net48/WinForms (Forms/Settings/FormButtonsRightPanel.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the right-menu button-order picker.
//
// Parity notes vs the WinForms original (FormButtonsRightPanel):
//   * The eight source buttons each append a preview glyph to the right-hand preview column
//     (flpRight), disable themselves, and record their command index in the working order list —
//     identical to the WinForms handlers (btnAutoSteer_Click … btnContour_Click) which mutated
//     mf.buttonOrder. That working list is owned here (the live FormGPS button order is rebuilt by
//     the host); the index→glyph mapping below matches the WinForms source button glyphs exactly.
//   * In WinForms the preview glyphs were fixed PictureBoxes moved between flpRight and clear; an
//     Avalonia control can have only one parent, so (as the file spec permits) each glyph is
//     CONSTRUCTED here from the GPS assembly's avares assets and added in click order; Reset/Default
//     clear and rebuild flpRight.Children, reproducing flpRight.Controls.Add/Clear.
//   * btnAll_Click (Default) adds all eight glyphs and sets the order to 0..7; btnReset_Click clears
//     everything and re-enables the sources; these are fully self-contained.
//   * btnOk_Click / btnTest_Click persisted the order to Settings and called
//     mf.PanelBuildRightMenu()/PanelUpdateRightAndBottom(). Persisting the order and rebuilding the
//     live menu are host responsibilities (Settings is being de-Windowsed and the menu lives on
//     FormGPS), so both raise OrderApplied with the working order; OK also closes. The count<2 guard
//     and its error dialog/log are reproduced here using the sibling FormDialogView and
//     AgLibrary.Logging.Log. btnCancel_Click raises Cancelled (the host restores the prior order) and
//     closes. This is the dependency-inversion seam mandated by AAP §0.3.2.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgLibrary.Logging;

namespace AgOpenGPS.Views.Settings
{
    public partial class FormButtonsRightPanelView : Window
    {
        /// <summary>Event payload carrying the working right-menu button order (command indices).</summary>
        public sealed class ButtonOrderEventArgs : EventArgs
        {
            public ButtonOrderEventArgs(IReadOnlyList<int> order)
            {
                Order = order;
            }

            public IReadOnlyList<int> Order { get; }
        }

        // Command-index -> preview glyph asset, matching the WinForms source-button glyphs:
        //   0 AutoSteer, 1 YouTurn, 2 SectionMasterAuto, 3 SectionMasterManual,
        //   4 Track, 5 CycleLinesBk (skip prev), 6 CycleLines (skip next), 7 Contour.
        private static readonly string[] GlyphAssets =
        {
            "AutoSteerOff.png",      // 0
            "YouTurnNo.png",         // 1
            "SectionMasterOff.png",  // 2
            "ManualOff.png",         // 3
            "AutoTrack.png",         // 4
            "ABLineCycleBk.png",     // 5
            "ABLineCycle.png",       // 6
            "ContourOn.png"          // 7
        };

        private readonly List<int> _buttonOrder = new List<int>();
        private Button[] _sourceButtons;

        public FormButtonsRightPanelView()
        {
            InitializeComponent();

            // The eight source buttons, indexed by command id (parity with the WinForms enable flags).
            _sourceButtons = new[]
            {
                btnAutoSteer,           // 0
                btnAutoYouTurn,         // 1
                btnSectionMasterAuto,   // 2
                btnSectionMasterManual, // 3
                btnTrack,               // 4
                btnCycleLinesBk,        // 5
                btnCycleLines,          // 6
                btnContour              // 7
            };
        }

        /// <summary>The working button order chosen by the operator.</summary>
        public IReadOnlyList<int> ButtonOrder => _buttonOrder;

        /// <summary>Raised by Test (live apply) and OK; the host persists the order and rebuilds the menu.</summary>
        public event EventHandler<ButtonOrderEventArgs> OrderApplied;

        /// <summary>Raised by Cancel; the host restores the previously-saved order.</summary>
        public event EventHandler Cancelled;

        // --- Source buttons: append the preview glyph, disable the source, record the index. ---
        private void btnAutoSteer_Click(object sender, RoutedEventArgs e) => AddCommand(0);
        private void btnAutoYouTurn_Click(object sender, RoutedEventArgs e) => AddCommand(1);
        private void btnSectionMasterAuto_Click(object sender, RoutedEventArgs e) => AddCommand(2);
        private void btnSectionMasterManual_Click(object sender, RoutedEventArgs e) => AddCommand(3);
        private void btnTrack_Click(object sender, RoutedEventArgs e) => AddCommand(4);
        private void btnCycleLinesBk_Click(object sender, RoutedEventArgs e) => AddCommand(5);
        private void btnCycleLines_Click(object sender, RoutedEventArgs e) => AddCommand(6);
        private void btnContour_Click(object sender, RoutedEventArgs e) => AddCommand(7);

        private void AddCommand(int index)
        {
            flpRight.Children.Add(BuildGlyph(GlyphAssets[index]));
            _sourceButtons[index].IsEnabled = false;
            _buttonOrder.Add(index);
        }

        // Default: add all eight glyphs in the WinForms visual order and set the order to 0..7.
        private void btnAll_Click(object sender, RoutedEventArgs e)
        {
            flpRight.Children.Clear();
            _buttonOrder.Clear();

            // Visual add order from the WinForms btnAll_Click.
            int[] visualOrder = { 0, 1, 2, 3, 5, 6, 4, 7 };
            foreach (int index in visualOrder)
            {
                flpRight.Children.Add(BuildGlyph(GlyphAssets[index]));
            }

            // The WinForms form persisted "0,1,2,3,4,5,6,7" (sequential), so mirror that exactly.
            for (int i = 0; i < GlyphAssets.Length; i++)
            {
                _buttonOrder.Add(i);
            }

            foreach (Button source in _sourceButtons) source.IsEnabled = false;
        }

        // Reset: clear the preview, clear the order, re-enable every source button.
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (Button source in _sourceButtons) source.IsEnabled = true;
            flpRight.Children.Clear();
            _buttonOrder.Clear();
        }

        // Cancel: signal the host to restore the prior order, then close.
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close(false);
        }

        // OK: require at least two buttons (WinForms guard), then apply and close.
        private async void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_buttonOrder.Count < 2)
            {
                await AgOpenGPS.Views.FormDialogView.ShowAsync(
                    this, "Button Error", "Not Enough Buttons Added", AgOpenGPS.Views.DialogSeverity.Error);
                Log.EventWriter("Button Picker, Not Enough Buttons");
                return;
            }

            OrderApplied?.Invoke(this, new ButtonOrderEventArgs(_buttonOrder.ToArray()));
            Close(true);
        }

        // Test: apply the order live without closing (WinForms btnTest_Click).
        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            OrderApplied?.Invoke(this, new ButtonOrderEventArgs(_buttonOrder.ToArray()));
        }

        // Construct a preview glyph image from the GPS assembly's avares assets. A fresh instance is
        // built per add so the single-parent rule is never violated.
        private static Image BuildGlyph(string asset)
        {
            return new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + asset))),
                Width = 80,
                Height = 80,
                Margin = new Avalonia.Thickness(3),
                Stretch = Stretch.Uniform
            };
        }
    }
}
