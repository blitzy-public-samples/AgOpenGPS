// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Avalonia code-behind for the right-menu button-order picker. 1:1 behavioural-parity
// reimplementation of the WinForms Forms/Settings/FormButtonsRightPanel (FormButtonsRightPanel.cs +
// FormButtonsRightPanel.Designer.cs).
//
// The dialog lets the operator pick which command buttons appear on the main right menu, and in what
// order. The operator touches the eight SOURCE buttons on the left (groupBoxSelectButtons); each touch
// disables that source button, appends its matching preview glyph into the right-hand preview column
// (flpRight, inside groupBoxAOGMenu) in click order, and records the command index. Default adds all
// eight, Reset clears and re-enables, Test applies the order live, OK commits and rebuilds the live
// right menu, and Cancel reverts. Per AAP §0.3.3 this is a faithful visual/behavioural parity port of
// the current Windows Forms UI — never a redesign.
//
// DEPENDENCY INJECTION (replaces the WinForms FormGPS "mf" god-object):
//   The WinForms form held `private readonly FormGPS mf` and drove mf.buttonOrder (a List<int>),
//   mf.PanelBuildRightMenu() and mf.PanelUpdateRightAndBottom(). Per the AAP guidance/scan-loop
//   decoupling (§0.6.1) this view takes NO FormGPS reference; instead the composition root injects an
//   IRightMenuConfig (declared below — the dependency-inversion seam mandated by AAP §0.3.2), exactly
//   as the sibling Views/Settings dialogs inject their own in-file contracts (e.g. IXteTelemetry on
//   FormGraphXTEView). This keeps the dialog testable and free of the central FormGPS back-reference
//   while preserving behaviour exactly.

using System;
using System.Collections.Generic;
using System.Globalization;
using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Helpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgOpenGPS.Views.Settings
{
    /// <summary>
    /// [XPLAT] Right-menu configuration contract consumed by <see cref="FormButtonsRightPanelView"/>,
    /// replacing the view's former direct reads/calls against the WinForms FormGPS "mf" reference
    /// (<c>mf.buttonOrder</c>, <c>mf.PanelBuildRightMenu()</c>, <c>mf.PanelUpdateRightAndBottom()</c>).
    /// The running application (or an adapter over the live FormGPS replacement) implements this at the
    /// composition root; defining it here keeps the view decoupled from FormGPS and unit-testable, in
    /// line with AAP §0.3.2 (Dependency Inversion) and §0.6.1 (FormGPS decoupling).
    /// </summary>
    public interface IRightMenuConfig
    {
        /// <summary>
        /// The working list of command indices (0-7) that make up the right menu, in operator-chosen
        /// order. Parity with the WinForms <c>FormGPS.buttonOrder</c> <see cref="List{T}"/>: the picker
        /// clears it on load and appends to it as the operator selects buttons.
        /// </summary>
        List<int> buttonOrder { get; }

        /// <summary>
        /// Rebuilds the live right-menu control set from the persisted button order. Parity with the
        /// WinForms <c>FormGPS.PanelBuildRightMenu()</c>; invoked by OK and Test after the order is saved.
        /// </summary>
        void PanelBuildRightMenu();

        /// <summary>
        /// Re-lays-out the right and bottom panels after the right menu is rebuilt. Parity with the
        /// WinForms <c>FormGPS.PanelUpdateRightAndBottom()</c>; invoked by OK and Test after
        /// <see cref="PanelBuildRightMenu"/>.
        /// </summary>
        void PanelUpdateRightAndBottom();
    }

    /// <summary>
    /// [XPLAT] Right-menu button-order picker window. Faithful Avalonia reimplementation of the WinForms
    /// <c>FormButtonsRightPanel</c>; see the file header for the full behaviour and decoupling notes.
    /// </summary>
    public partial class FormButtonsRightPanelView : Window
    {
        /// <summary>
        /// [XPLAT] Command-index -&gt; preview-glyph asset file name, matching the WinForms source-button
        /// images exactly (FormButtonsRightPanel.Designer.cs): 0 AutoSteer, 1 YouTurn,
        /// 2 SectionMasterAuto, 3 SectionMasterManual, 4 Track, 5 CycleLinesBk (skip prev),
        /// 6 CycleLines (skip next), 7 Contour. Served from the GPS assembly via
        /// <c>avares://AgOpenGPS/btnImages/&lt;name&gt;.png</c>, the convention every sibling GPS view uses.
        /// </summary>
        private static readonly string[] PreviewGlyphAssets =
        {
            "AutoSteerOff.png",     // 0 autoSteer
            "YouTurnNo.png",        // 1 youTurn
            "SectionMasterOff.png", // 2 autoSection
            "ManualOff.png",        // 3 manualSection
            "AutoTrack.png",        // 4 track
            "ABLineCycleBk.png",    // 5 skipPrev
            "ABLineCycle.png",      // 6 skipNext
            "ContourOn.png"         // 7 contour
        };

        // [XPLAT] Injected right-menu controller (replaces the WinForms "mf"); null only on the
        // parameterless design-time/XAML-loader path, never in production.
        private readonly IRightMenuConfig _rightMenu;

        // [XPLAT] The eight SOURCE buttons indexed by command id (0-7), so Default/Reset can toggle them
        // en masse and each selection handler can disable exactly one — parity with the WinForms
        // btn*.Enabled flags. Populated from the x:Name fields after InitializeComponent.
        private Button[] _sourceButtons;

        // [XPLAT] Verbatim WinForms fields. btnCounter mirrors FormButtonsRightPanel.btnCounter (it is
        // incremented per selection and reset to 0 by Reset). original captures the setting on load so
        // Cancel can restore it.
        private int btnCounter = 0;
        private string original;

        // [XPLAT] One-shot guard for the OnOpened "Load" work: WinForms Load fires once, but Avalonia's
        // OnOpened may re-fire (e.g. hide/show), so the load-parity body must run only the first time.
        private bool _loaded;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia compiled-XAML loader / design-time
        /// preview. Performs the WinForms constructor parity: loads the markup and applies the localized
        /// window title and group/label captions from <see cref="gStr"/> (exactly the strings the
        /// WinForms ctor assigned). Also caches the eight source buttons by command index.
        /// </summary>
        public FormButtonsRightPanelView()
        {
            InitializeComponent();

            // [XPLAT] Verbatim caption translation from the WinForms ctor. WinForms `this.Text` -> Title;
            // the two GroupBoxes -> HeaderedContentControl.Header; the two Labels -> TextBlock.Text; and
            // the two glyph+caption action buttons keep their glyph while their inner caption TextBlock
            // is set (SetButtonCaption).
            Title = gStr.gsButtonPicker;
            groupBoxSelectButtons.Header = gStr.gsSelectButtons;
            groupBoxAOGMenu.Header = gStr.gsRightMenu;
            labelButtonArrangeOne.Text = gStr.gsArrangeText;
            SetButtonCaption(buttonLabelDefault, gStr.gsDefault);
            SetButtonCaption(buttonLabelReset, gStr.gsReset);
            labelPreview.Text = gStr.gsPreview;

            // [XPLAT] Source buttons indexed by command id (parity with the WinForms enable flags and the
            // 0-7 index map). Order here matches PreviewGlyphAssets so _sourceButtons[i] is the button
            // that contributes command index i.
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

        /// <summary>
        /// [XPLAT] Production constructor. Parity with the WinForms <c>FormButtonsRightPanel(Form callingForm)</c>
        /// ctor, except the FormGPS "mf" reference is replaced by an injected <see cref="IRightMenuConfig"/>
        /// supplied by the composition root.
        /// </summary>
        /// <param name="rightMenu">The live right-menu controller (must not be null).</param>
        public FormButtonsRightPanelView(IRightMenuConfig rightMenu)
            : this()
        {
            _rightMenu = rightMenu ?? throw new ArgumentNullException(nameof(rightMenu));
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormToolPivot_Load</c> parity. Avalonia raises <c>Opened</c> once the
        /// window is shown (the cross-platform analogue of the WinForms Load event), so the one-time
        /// initialisation lives here: capture the current setting for Cancel, clear the working order and
        /// the preview column, and recover the window if its persisted position is off-screen.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_loaded)
            {
                return;
            }
            _loaded = true;

            // [XPLAT] WinForms: original = Properties.Settings.Default.setDisplay_buttonOrder;
            original = Properties.Settings.Default.setDisplay_buttonOrder;

            // [XPLAT] WinForms: mf.buttonOrder?.Clear(); flpRight.Controls.Clear();
            _rightMenu?.buttonOrder?.Clear();
            flpRight.Children.Clear();

            // [XPLAT] Off-screen recovery. WinForms: if (!ScreenHelper.IsOnScreen(Bounds)) { Top = 0; Left = 0; }
            // ScreenHelper now resolves the monitor topology from the window's own Avalonia Screens collection.
            if (!ScreenHelper.IsOnScreen(this))
            {
                Position = new PixelPoint(0, 0);
            }
        }

        // === Selection handlers — parity with btnAutoSteer_Click … btnContour_Click ===
        // Each appends the matching preview glyph in click order, disables its own source button, records
        // the command index, and bumps btnCounter — identical to the WinForms handlers.
        private void btnAutoSteer_Click(object sender, RoutedEventArgs e) => AddCommand(0);
        private void btnAutoYouTurn_Click(object sender, RoutedEventArgs e) => AddCommand(1);
        private void btnSectionMasterAuto_Click(object sender, RoutedEventArgs e) => AddCommand(2);
        private void btnSectionMasterManual_Click(object sender, RoutedEventArgs e) => AddCommand(3);
        private void btnTrack_Click(object sender, RoutedEventArgs e) => AddCommand(4);
        private void btnCycleLinesBk_Click(object sender, RoutedEventArgs e) => AddCommand(5);
        private void btnCycleLines_Click(object sender, RoutedEventArgs e) => AddCommand(6);
        private void btnContour_Click(object sender, RoutedEventArgs e) => AddCommand(7);

        // [XPLAT] Shared body of the eight WinForms selection handlers: add the preview glyph (flpRight
        // .Controls.Add), disable the source button (btn*.Enabled = false), append the command index
        // (mf.buttonOrder.Add(index)) and increment btnCounter.
        private void AddCommand(int index)
        {
            flpRight.Children.Add(BuildPreviewGlyph(PreviewGlyphAssets[index]));
            _sourceButtons[index].IsEnabled = false;
            _rightMenu.buttonOrder.Add(index);
            btnCounter++;
        }

        // [XPLAT] Parity with btnAll_Click (the "Default" button): persist the full sequential order, add
        // all eight glyphs in the exact WinForms visual order, set the working order to 0..7, and disable
        // every source button.
        private void btnAll_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.setDisplay_buttonOrder = "0,1,2,3,4,5,6,7";
            Properties.Settings.Default.Save();

            // [XPLAT] flpRight.Controls?.Clear(); then add the previews in the WinForms add order:
            // autoSteer, youTurn, autoSection, manualSection, skipPrev, skipNext, track, contour.
            flpRight.Children.Clear();
            int[] visualOrder = { 0, 1, 2, 3, 5, 6, 4, 7 };
            foreach (int index in visualOrder)
            {
                flpRight.Children.Add(BuildPreviewGlyph(PreviewGlyphAssets[index]));
            }

            // [XPLAT] mf.buttonOrder?.Clear(); for (i = 0..flpRight.Controls.Count-1) mf.buttonOrder.Add(i);
            // i.e. the working order is the sequential 0..7, matching the persisted CSV above.
            _rightMenu?.buttonOrder?.Clear();
            if (_rightMenu?.buttonOrder != null)
            {
                for (int i = 0; i < flpRight.Children.Count; i++)
                {
                    _rightMenu.buttonOrder.Add(i);
                }
            }

            // [XPLAT] Disable every source button (WinForms set all eight Enabled = false).
            foreach (Button source in _sourceButtons)
            {
                source.IsEnabled = false;
            }
        }

        // [XPLAT] Parity with btnReset_Click: re-enable all eight source buttons, reset the counter, clear
        // the preview column, and clear the working order.
        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (Button source in _sourceButtons)
            {
                source.IsEnabled = true;
            }

            btnCounter = 0;
            flpRight.Children.Clear();
            _rightMenu?.buttonOrder?.Clear();
        }

        // [XPLAT] Parity with btnCancel_Click: revert the setting to the value captured on load, persist,
        // and close.
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.setDisplay_buttonOrder = original;
            Properties.Settings.Default.Save();
            Close();
        }

        // [XPLAT] Parity with btnOk_Click: require at least two buttons, otherwise show the migrated error
        // dialog and log; else persist the CSV order, rebuild the live right menu, and close. async so the
        // FormDialogView modal can be awaited (the WinForms FormDialog.Show was blocking).
        private async void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_rightMenu.buttonOrder.Count < 2)
            {
                // [XPLAT] WinForms: FormDialog.Show("Button Error", "Not Enough Buttons Added", DialogSeverity.Error);
                // The owner-first FormDialogView overload supplies the modal owner (this window).
                await FormDialogView.ShowAsync(this, "Button Error", "Not Enough Buttons Added", DialogSeverity.Error);
                Log.EventWriter("Button Picker, Not Enough Buttons");
                return;
            }

            Properties.Settings.Default.setDisplay_buttonOrder = BuildButtonOrderCsv();
            Properties.Settings.Default.Save();

            _rightMenu.PanelBuildRightMenu();
            _rightMenu.PanelUpdateRightAndBottom();
            Close();
        }

        // [XPLAT] Parity with btnTest_Click: persist the CSV order and rebuild the live right menu, but do
        // NOT close — the operator can keep tweaking and re-testing.
        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.setDisplay_buttonOrder = BuildButtonOrderCsv();
            Properties.Settings.Default.Save();

            _rightMenu.PanelBuildRightMenu();
            _rightMenu.PanelUpdateRightAndBottom();
        }

        // [XPLAT] Reproduces the WinForms CSV-build loop exactly: comma-separated command indices with no
        // trailing comma after the last element. Uses InvariantCulture for the int->string conversion so
        // the persisted file is identical across OS locales (AAP §0.6.5 culture-parity).
        private string BuildButtonOrderCsv()
        {
            List<int> order = _rightMenu.buttonOrder;
            string csv = "";
            for (int i = 0; i < order.Count; i++)
            {
                if (i < order.Count - 1)
                {
                    csv += order[i].ToString(CultureInfo.InvariantCulture) + ",";
                }
                else
                {
                    csv += order[i].ToString(CultureInfo.InvariantCulture);
                }
            }
            return csv;
        }

        // [XPLAT] Sets the caption of a glyph+caption action button (buttonLabelDefault / buttonLabelReset)
        // without disturbing its glyph: the WinForms Button carried both Text and Image, reimplemented in
        // the markup as a StackPanel { Image, TextBlock }. Locate the inner TextBlock and set it, so the
        // localized gStr caption maps 1:1 to the WinForms Button.Text while the Image is preserved.
        private static void SetButtonCaption(Button button, string caption)
        {
            if (button?.Content is Panel panel)
            {
                foreach (Control child in panel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        textBlock.Text = caption;
                        return;
                    }
                }
            }
        }

        // [XPLAT] Builds a fresh preview tile for the right-menu preview column. In WinForms the eight
        // preview controls were fixed Buttons moved between flpRight and cleared; an Avalonia control can
        // have only a single parent, so (as the file spec permits) each tile is CONSTRUCTED per add. The
        // tile mirrors the WinForms preview button: 84x72, WhiteSmoke face, a thin flat border, holding
        // the centred glyph loaded from the GPS assembly's avares assets.
        private static Border BuildPreviewGlyph(string assetFileName)
        {
            var image = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://AgOpenGPS/btnImages/" + assetFileName))),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3)
            };

            return new Border
            {
                Width = 84,
                Height = 72,
                Margin = new Thickness(3),
                Background = Brushes.WhiteSmoke,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x66, 0xCC)),
                BorderThickness = new Thickness(1),
                Child = image
            };
        }
    }
}
