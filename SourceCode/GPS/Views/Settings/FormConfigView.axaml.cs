// [XPLAT] migrated from net48/WinForms FormConfig (folds ConfigMenu/Vehicle/Tool/Data/Module/Help partials) — see TRANSITION_MAP.md
// =============================================================================================
// [XPLAT] AgOpenGPS (GPS program) — Configuration dialog code-behind (Avalonia view).
//
// This single code-behind is a 1:1 PARITY reimplementation that FOLDS the behaviour of the EIGHT
// WinForms `partial class FormConfig` files into one Avalonia Window:
//   * FormConfig.cs              -> Controller region (Load/Close gate/FixMinMaxSpinners/unit toggles)
//   * ConfigMenu.Designer.cs     -> Menu/Nav region (sidebar routing + tab Enter/Leave dispatcher)
//   * ConfigVehicle.Designer.cs  -> Vehicle region (Display/Antenna/Config/Dimensions/Guidance)
//   * ConfigTool.Designer.cs     -> Tool region (Config/Hitch/Offset/Pivot/Sections/Switches/Settings)
//   * ConfigData.Designer.cs     -> Data region (Heading/Roll/Features)
//   * ConfigModule.Designer.cs   -> Module region (Machine/Relay/UTurn/Tram)
//   * ConfigHelp.Designer.cs     -> (no handlers; only field declarations — nothing to fold)
//
// PARITY CONTRACT: the settings XML schema is BEHAVIOR-FROZEN (AAP §0.2.2). Every value written to
// Settings/VehicleSettings/ToolSettings here matches the WinForms write EXACTLY (same key, same type,
// same conversion). All numeric ToString/Parse/Convert use InvariantCulture (AAP §0.6.5) so a comma-
// decimal locale on Linux/macOS cannot corrupt the round-trip.
//
// DECOUPLING: the former `mf` (FormGPS) god-object is replaced by constructor-injected collaborators
// (IConfigContext + the domain models). Host-driven population that the established sibling views defer
// to the extracted-services layer (vehicle textures, the summary/vehicle hosted controls, the section-
// master button orchestration, the p_238 machine PGN, FormGPS.MAXSECTIONS, the right-menu-order child
// dialog) is surfaced through IConfigContext facade methods rather than fabricated against state that
// does not yet exist. All local label/Settings/recompute logic is reproduced verbatim.
//
// The companion FormConfigView.axaml declares every control x:Name addressed below (a HARD CONTRACT)
// and wires ZERO events; every handler is wired here in WireEvents().
// =============================================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using AgLibrary.Logging;               // Log.EventWriter
using AgOpenGPS;                       // glm (math constants, e.g. glm.m2ft)
using AgOpenGPS.Core;                  // btnStates
using AgOpenGPS.Core.Models;           // VehicleType, VehicleConfig
using AgOpenGPS.Core.Translations;     // gStr
using AgOpenGPS.Interfaces;            // IConfigContext, ITramState, IAbLineState, IGuidanceState, IAhrsState
using AgOpenGPS.Properties;            // Settings, VehicleSettings, ToolSettings, RegistrySettings
using AgOpenGPS.Views;                 // FormNumeric, FormDialogView, DialogSeverity

namespace AgOpenGPS.Views.Settings
{
    // [XPLAT] This view's own namespace segment "Settings" (AgOpenGPS.Views.Settings) shadows the
    // AgOpenGPS.Properties.Settings class: the bare name `Settings` would bind to the namespace (found via
    // the enclosing AgOpenGPS.Views) before the `using AgOpenGPS.Properties;` import, so `Settings.Default`
    // fails to resolve. A namespace-scoped using-alias rebinds `Settings` to the settings class — it is
    // consulted at this innermost namespace ahead of the enclosing-namespace walk — fixing all ~100
    // `Settings.Default.*` call sites without fully qualifying each one. (VehicleSettings/ToolSettings/
    // RegistrySettings do not collide and resolve normally through the AgOpenGPS.Properties import.)
    using Settings = AgOpenGPS.Properties.Settings;

    public partial class FormConfigView : Window
    {
        // =====================================================================================
        // [XPLAT] NudState — proxy for the WinForms NumericUpDown/NudlessNumericUpDown spinners.
        // Each of the 58 `nud*` controls is a Button in Avalonia (value entry happens through the
        // FormNumeric keypad dialog). NudState carries the metric Min/Max bounds (adjusted in
        // FixMinMaxSpinners for imperial), the current decimal Value, and the DecimalPlaces used to
        // format the button caption — exactly as the WinForms NumericUpDown formatted its text.
        // =====================================================================================
        private sealed class NudState
        {
            private readonly Button _btn;
            public decimal Min;
            public decimal Max;
            public int Decimals;
            private decimal _value;

            public decimal Value
            {
                get => _value;
                set { _value = value; Refresh(); }
            }

            public NudState(Button btn, decimal min, decimal max, int decimals)
            {
                _btn = btn;
                Min = min;
                Max = max;
                Decimals = decimals;
                _value = min;
                Refresh();
            }

            // Mirror NumericUpDown.Text: fixed-point with DecimalPlaces, InvariantCulture (AAP §0.6.5).
            public void Refresh()
            {
                _btn.Content = _value.ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }

        // ===== injected collaborators (replace the former `mf` FormGPS back-reference) ===========
        private readonly IConfigContext ctx;
        // [XPLAT] vehicle/tool are surfaced via IVehicleState/IToolState rather than the concrete
        // CVehicle/CTool: those classes still hold a FormGPS `mf` back-reference and are source-gated
        // out of the cross-platform GPS build (AgOpenGPS.csproj Checkpoint-6 gating; AAP §0.6.1), exactly
        // like the equally-gated CTram/CYouTurn/CAHRS consumed here through ITramState/IGuidanceState/IAhrsState.
        private readonly IVehicleState vehicle;
        private readonly IToolState tool;
        private readonly ITramState tram;
        private readonly IAbLineState abLine;
        private readonly IGuidanceState gyd;
        private readonly IAhrsState ahrs;
        private readonly Window owner;

        // ===== nud registry (Button -> NudState) ================================================
        private readonly Dictionary<Button, NudState> _nud = new Dictionary<Button, NudState>();
        private NudState N(Button b) => _nud[b];

        // ===== sidebar highlight brushes ========================================================
        // [XPLAT] WinForms SystemColors.GradientActiveCaption (185,209,234) / GradientInactiveCaption
        // (215,228,242) mapped to literal SolidColorBrushes so the active/inactive nav highlight matches.
        private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(185, 209, 234));
        private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.FromRgb(215, 228, 242));

        // ===== closing gate: only btnOK and the unit-toggle handlers set this true ==============
        private bool isClosing = false;

        // ===== suppress ComboBox SelectionChanged side effects while loading programmatically ====
        private bool _suppressCombo = false;

        // ===== section state (ports the ConfigTool.Designer.cs instance fields) =================
        private readonly decimal[] sectionWidthArr = new decimal[16];
        private readonly decimal[] sectionPositionArr = new decimal[17];
        private double defaultSectionWidth;
        private int numberOfSections;

        // =====================================================================================
        // Constructor — port of FormConfig.cs ctor (24-101). The ~60 `nud*.Controls[0].Enabled=false`
        // caret-disable lines are intentionally omitted: value entry is via the FormNumeric dialog.
        // =====================================================================================
        public FormConfigView(IConfigContext ctx, IVehicleState vehicle, IToolState tool, ITramState tram,
            IAbLineState abLine, IGuidanceState gyd, IAhrsState ahrs, Window owner)
        {
            this.ctx = ctx;
            this.vehicle = vehicle;
            this.tool = tool;
            this.tram = tram;
            this.abLine = abLine;
            this.gyd = gyd;
            this.ahrs = ahrs;
            this.owner = owner;

            InitializeComponent();

            // [XPLAT] nud caret-disable not needed — value entry via FormNumeric dialog.
            BuildNudRegistry();
            WireEvents();
            HideSubMenu();
        }

        // =====================================================================================
        #region Controller — // [XPLAT] from FormConfig.cs
        // =====================================================================================

        // Port of FormConfig_Load (103-261). Runs once when the window opens.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Load();
        }

        private void Load()
        {
            // since we reset, save current state
            ctx.SaveFormGPSWindowSettings();

            // metric or imperial on spinner min/maxes
            if (!ctx.IsMetric) FixMinMaxSpinners();

            lblVehicleToolWidth.Text = Convert.ToString((int)(tool.width * 100 * ctx.Cm2CmOrIn), CultureInfo.InvariantCulture);
            SectionFeetInchesTotalWidthLabelUpdate(ctx.IsMetric, tool.width);

            tab1.SelectedItem = tabSummary;

            // ---- Label translations (verbatim gStr keys) ----
            // configload-save
            labelUnitsBottom.Text = gStr.gsUnits + ":";
            labelToolWidthBottom.Text = gStr.gsWidth + ":";
            // tractorconfig
            SetGroupHeader(labelBoxAttachmentStyle, gStr.gsAttachmentStyle);
            labelTractorUnits.Text = gStr.gsUnits + ":";
            labelHitchLength.Text = gStr.gsHitchLength;
            labelWheelBase2.Text = gStr.gsWheelbase;
            labelTrack.Text = gStr.gsTrack;
            // antennadistanceconfig
            labelPivotDistance.Text = gStr.gsPivotDistance;
            labelAntHeight.Text = gStr.gsAntennaHeight;
            SetGroupHeader(labelAntOffset, gStr.gsAntennaOffset);
            labelLeft.Text = gStr.gsLeft;
            labelCenter.Text = gStr.gsCenter;
            labelRight.Text = gStr.gsRight;
            labelDualPositionOnRight.Text = gStr.gsDualpositionAntennaRight;
            // toolconfig
            SetGroupHeader(labelToolOffset, gStr.gsToolOffset);
            SetGroupHeader(labelOverlapGap, $"{gStr.gsOverlap} / {gStr.gsGap}");
            labelToolLeft.Text = gStr.gsToolLeft;
            labelToolRight.Text = gStr.gsToolRight;
            labelOverlap2.Text = gStr.gsOverlap;
            labelGap.Text = gStr.gsGap;
            // sections
            labelZone1.Text = gStr.gsZone + 1;
            labelZone2.Text = gStr.gsZone + 2;
            labelZone3.Text = gStr.gsZone + 3;
            labelZone4.Text = gStr.gsZone + 4;
            labelZone5.Text = gStr.gsZone + 5;
            labelZone6.Text = gStr.gsZone + 6;
            labelZone7.Text = gStr.gsZone + 7;
            labelZone8.Text = gStr.gsZone + 8;
            labelZonesBox.Text = gStr.gsZones;
            labelSectionWidth.Text = gStr.gsWidth;
            labelNumOfSections.Text = gStr.gsSections;
            labelChoose.Text = gStr.gsChoose;
            labelBoundary.Text = gStr.gsBoundary;
            labelCoverage.Text = $"% {gStr.gsCoverage}";
            // sectionswitches
            SetGroupHeader(labelGroupWorkSwitch, gStr.gsWorkSwitch);
            SetGroupHeader(labelGroupSteerSwitch, gStr.gsSteerSwitch);
            chkSelectWorkSwitch.Content = gStr.gsActive;
            chkSelectSteerSwitch.Content = gStr.gsActive;
            chkSetManualSections.Content = $"{gStr.gsSections}: {gStr.gsManual}";
            chkSetManualSectionsSteer.Content = $"{gStr.gsSections}: {gStr.gsManual}";
            chkSetAutoSections.Content = $"{gStr.gsSections}: {gStr.gsAuto}";
            chkSetAutoSectionsSteer.Content = $"{gStr.gsSections}: {gStr.gsAuto}";
            // sectiontiming
            labelLookAheadTiming.Text = gStr.gsLookAheadTiming;
            labelOnTime.Text = gStr.gsOn + "(secs)";
            labelOffTime.Text = gStr.gsOff + "(secs)";
            labelDelayTime.Text = gStr.gsTurnOffDelay + "(secs)";
            // antenna-imu configuration
            SetGroupHeader(labelGboxDual, gStr.gsDualAntennaSetting);
            SetGroupHeader(labelGboxSingle, gStr.gsSingleAntennaSetting);
            labelHeadingOffset.Text = $"{gStr.gsHeadingOffset} ({gStr.gsDegree})";
            labelReverseDistance.Text = gStr.gsReverseDistance;
            labelGpsStep.Text = gStr.gsGpsStep;
            labelFixAlarm.Text = gStr.gsFixAlarm;
            labelFixAlarmStop.Text = gStr.gsFixAlarmStop;
            labelFix2Fix.Text = gStr.gsFix2Fix;
            labelIMUFusion.Text = gStr.gsImuFusion;
            cboxIsReverseOn.Content = gStr.gsReverseSteer;
            // rollconfig
            labelRemoveOffset.Text = gStr.gsRemoveOffset;
            labelZeroRoll.Text = gStr.gsZeroRoll;
            labelInvertRoll.Text = gStr.gsInvertRoll;
            labelLess.Text = gStr.gsLess;
            labelMore.Text = gStr.gsMore;
            labelRollFilter.Text = gStr.gsRollFilter;
            // uturnconfig
            labelUturnExtend.Text = gStr.gsUturnExtension;
            labelUturnSmooth.Text = gStr.gsUturnSmooth;
            labelSendandSave.Text = gStr.gsSendAndSave;
            // hydraulicliftconfig
            SetGroupHeader(labelGroupHyd, gStr.gsHydraulicLiftConfig);
            labelEnable.Text = gStr.gsEnable;
            labelRaiseTime.Text = gStr.gsRaiseTime;
            labelLowTime.Text = gStr.gsLowerTime;
            labelPlantPop.Text = gStr.gsPlantPop;
            labelHydLiftSec.Text = gStr.gsHydraulicLiftLookAhead;
            labelUser1.Text = gStr.gsUserNo + " " + 1;
            labelUser2.Text = gStr.gsUserNo + " " + 2;
            labelUser3.Text = gStr.gsUserNo + " " + 3;
            labelUser4.Text = gStr.gsUserNo + " " + 4;
            labelHydLiftInvert.Text = gStr.gsInvertHydraulicRelays;
            labelSendSaveHydraulicLift.Text = gStr.gsSendAndSave;
            // tramsconfig
            labelTramWidth.Text = gStr.gsTramWidth;
            labelDisplay.Text = gStr.gsDisplay + "?";
            labelOverride.Text = gStr.gsOverride;
            // softbuttonsactivatorconfig
            labelFieldMenu.Text = gStr.gsFieldMenu;
            labelToolsMenu.Text = gStr.gsToolsMenu;
            labelScreenButtons.Text = gStr.gsScreenButtons;
            labelBottomMenu.Text = gStr.gsBottomMenu;
            labelRightMenu.Text = gStr.gsRightMenu;
            labelTramlineOnOff.Text = gStr.gsTramLines;
            labelHeadlandOnOff.Text = gStr.gsHeadland;
            labelBoundOnOff.Text = gStr.gsBoundary;
            labelRecPathOnOff.Text = gStr.gsRecordedPathMenu;
            labelABSmoothOnOff.Text = gStr.gsSmoothABCurve;
            labelContourOnOff.Text = gStr.gsContourOn;
            labelCamOnOff.Text = gStr.gsWebCam;
            labelOffsetFixOnOff.Text = gStr.gsOffsetFix;
            labelUturnOnOff.Text = gStr.gsUturn;
            labelLateralOnOff.Text = gStr.gsLateral;
            labelNudgeCtrlOnOff.Text = gStr.gsNudge;
            labelPowerLossOnOff.Text = gStr.gsPowerLoss;
            labelStartAgIO.Text = gStr.gsAutoStartAgIO;
            labelOffAgIO.Text = gStr.gsAutoOffAgIO;
            labelHardwareMessage.Text = gStr.gsHardwareMessages;
            labelSounds.Text = gStr.gsSound;
            labelAutosteerSoundOnOff.Text = gStr.gsAutoSteer;
            labelUturnSoundOnOff.Text = gStr.gsUturn;
            labelHydLiftSoundOnOff.Text = gStr.gsHydraulicLift;
            labelSectionSoundOnOff.Text = gStr.gsSections;
            // displaybuttonsconfig
            labelPolyOnOff.Text = gStr.gsPolygons;
            labelBrightnessOnOff.Text = gStr.gsBrightness;
            labelFieldTextureOnOff.Text = gStr.gsFieldTexture;
            labelLineSmoothOnOff.Text = gStr.gsLineSmooth;
            labelSpeedoOnOff.Text = gStr.gsSpeedo;
            labelSvenArrowOnOff.Text = gStr.gsSvennArrow;
            labelGridOnOff.Text = gStr.gsGrid;
            labelDirectionMarkOnOff.Text = gStr.gsDirectionMarkers;
            labelKeyboardOnOff.Text = gStr.gsKeyboard;
            labelFullscreenOnOff.Text = gStr.gsStartFullscreen;
            labelGuideLinesOnOff.Text = gStr.gsExtraGuideLines;
            labelSectionLinesOnOff.Text = gStr.gsSectionLines;
            labelElevationOnOff.Text = gStr.gsElevationlog;
            labelElevationOnOff.Text = gStr.gsElevationlog;
            SetGroupHeader(unitsGroupBox, gStr.gsUnits);
            cboxIsAutoSwitchDualFixOn.Content = gStr.gsAutoSwitchDualFix;
            labelAutoSwitchDualFixSpeed.Text = gStr.gsAutoSwitchDualFixSpeed;

            UpdateSummary();

            // [XPLAT] WinForms ScreenHelper.IsOnScreen(Bounds) -> Avalonia best-effort: recover a window
            // that would otherwise open off the top/left edge.
            if (Position.X < 0 || Position.Y < 0)
            {
                Position = new PixelPoint(0, 0);
            }
        }

        // Port of FormConfig_FormClosing (263-278). Only btnOK / unit toggles flip isClosing.
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!isClosing)
            {
                e.Cancel = true;
                return;
            }

            // reload all the settings
            ctx.LoadSettings();

            // save current vehicle / tool / settings
            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
            Settings.Default.Save();

            base.OnClosing(e);
        }

        // Port of FixMinMaxSpinners (280-356) — imperial bounds. Reproduced VERBATIM, including the
        // source's redundant second division of nudOffset/nudOverlap (349-352): 1:1 parity is the bar.
        private void FixMinMaxSpinners()
        {
            N(nudTankHitch).Max = Math.Round(N(nudTankHitch).Max / 2.54M);
            N(nudTankHitch).Min = Math.Round(N(nudTankHitch).Min / 2.54M);

            N(nudDrawbarLength).Max = Math.Round(N(nudDrawbarLength).Max / 2.54M);
            N(nudDrawbarLength).Min = Math.Round(N(nudDrawbarLength).Min / 2.54M);

            N(nudTrailingHitchLength).Max = Math.Round(N(nudTrailingHitchLength).Max / 2.54M);
            N(nudTrailingHitchLength).Min = Math.Round(N(nudTrailingHitchLength).Min / 2.54M);

            N(nudTractorHitchLength).Max = Math.Round(N(nudTractorHitchLength).Max / 2.54M);
            N(nudTractorHitchLength).Min = Math.Round(N(nudTractorHitchLength).Min / 2.54M);

            N(nudVehicleTrack).Max = Math.Round(N(nudVehicleTrack).Max / 2.54M);
            N(nudVehicleTrack).Min = Math.Round(N(nudVehicleTrack).Min / 2.54M);

            N(nudWheelbase).Max = Math.Round(N(nudWheelbase).Max / 2.54M);
            N(nudWheelbase).Min = Math.Round(N(nudWheelbase).Min / 2.54M);

            N(nudOverlap).Max = Math.Round(N(nudOverlap).Max / 2.54M);
            N(nudOverlap).Min = Math.Round(N(nudOverlap).Min / 2.54M);

            N(nudOffset).Max = Math.Round(N(nudOffset).Max / 2.54M);
            N(nudOffset).Min = Math.Round(N(nudOffset).Min / 2.54M);

            N(nudDefaultSectionWidth).Max = Math.Round(N(nudDefaultSectionWidth).Max / 2.54M);
            N(nudDefaultSectionWidth).Min = Math.Round(N(nudDefaultSectionWidth).Min / 3.0M);

            N(nudSection01).Max = Math.Round(N(nudSection01).Max / 2.54M);
            N(nudSection01).Min = Math.Round(N(nudSection01).Min / 2.54M);
            N(nudSection02).Max = Math.Round(N(nudSection02).Max / 2.54M);
            N(nudSection02).Min = Math.Round(N(nudSection02).Min / 2.54M);
            N(nudSection03).Max = Math.Round(N(nudSection03).Max / 2.54M);
            N(nudSection03).Min = Math.Round(N(nudSection03).Min / 2.54M);
            N(nudSection04).Max = Math.Round(N(nudSection04).Max / 2.54M);
            N(nudSection04).Min = Math.Round(N(nudSection04).Min / 2.54M);
            N(nudSection05).Max = Math.Round(N(nudSection05).Max / 2.54M);
            N(nudSection05).Min = Math.Round(N(nudSection05).Min / 2.54M);
            N(nudSection06).Max = Math.Round(N(nudSection06).Max / 2.54M);
            N(nudSection06).Min = Math.Round(N(nudSection06).Min / 2.54M);
            N(nudSection07).Max = Math.Round(N(nudSection07).Max / 2.54M);
            N(nudSection07).Min = Math.Round(N(nudSection07).Min / 2.54M);
            N(nudSection08).Max = Math.Round(N(nudSection08).Max / 2.54M);
            N(nudSection08).Min = Math.Round(N(nudSection08).Min / 2.54M);
            N(nudSection09).Max = Math.Round(N(nudSection09).Max / 2.54M);
            N(nudSection09).Min = Math.Round(N(nudSection09).Min / 2.54M);
            N(nudSection10).Max = Math.Round(N(nudSection10).Max / 2.54M);
            N(nudSection10).Min = Math.Round(N(nudSection10).Min / 2.54M);
            N(nudSection11).Max = Math.Round(N(nudSection11).Max / 2.54M);
            N(nudSection11).Min = Math.Round(N(nudSection11).Min / 2.54M);
            N(nudSection12).Max = Math.Round(N(nudSection12).Max / 2.54M);
            N(nudSection12).Min = Math.Round(N(nudSection12).Min / 2.54M);
            N(nudSection13).Max = Math.Round(N(nudSection13).Max / 2.54M);
            N(nudSection13).Min = Math.Round(N(nudSection13).Min / 2.54M);
            N(nudSection14).Max = Math.Round(N(nudSection14).Max / 2.54M);
            N(nudSection14).Min = Math.Round(N(nudSection14).Min / 2.54M);
            N(nudSection15).Max = Math.Round(N(nudSection15).Max / 2.54M);
            N(nudSection15).Min = Math.Round(N(nudSection15).Min / 2.54M);
            N(nudSection16).Max = Math.Round(N(nudSection16).Max / 2.54M);
            N(nudSection16).Min = Math.Round(N(nudSection16).Min / 2.54M);

            N(nudTramWidth).Min = Math.Round(N(nudTramWidth).Min / 2.54M);
            N(nudTramWidth).Max = Math.Round(N(nudTramWidth).Max / 2.54M);

            // Meters to feet
            N(nudTurnDistanceFromBoundary).Min = Math.Round(N(nudTurnDistanceFromBoundary).Min * 3.28M);
            N(nudTurnDistanceFromBoundary).Max = Math.Round(N(nudTurnDistanceFromBoundary).Max * 3.28M);

            N(nudOffset).Max = Math.Round(N(nudOffset).Max / 2.54M);
            N(nudOffset).Min = Math.Round(N(nudOffset).Min / 2.54M);
            N(nudOverlap).Max = Math.Round(N(nudOverlap).Max / 2.54M);
            N(nudOverlap).Min = Math.Round(N(nudOverlap).Min / 2.54M);

            N(nudTrailingToolToPivotLength).Max = Math.Round(N(nudTrailingToolToPivotLength).Max / 2.54M);
            N(nudTrailingToolToPivotLength).Min = Math.Round(N(nudTrailingToolToPivotLength).Min / 2.54M);
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            isClosing = true;
            Close();
        }

        private void tabSummary_Enter()
        {
            SectionFeetInchesTotalWidthLabelUpdate(ctx.IsMetric, tool.width);
            UpdateSummary();
        }

        private void tabSummary_Leave()
        {
        }

        private void rbtnDisplayImperial_Click(object sender, RoutedEventArgs e)
        {
            Log.EventWriter("Units To Imperial");

            ctx.IsMetric = false;
            Settings.Default.setMenu_isMetric = ctx.IsMetric;
            isClosing = true;
            Close();
        }

        private void rbtnDisplayMetric_Click(object sender, RoutedEventArgs e)
        {
            Log.EventWriter("Units to Metric");

            ctx.IsMetric = true;
            Settings.Default.setMenu_isMetric = ctx.IsMetric;
            isClosing = true;
            Close();
        }

        private async void nudNumGuideLines_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudNumGuideLines))
            {
                abLine.NumGuideLines = (int)N(nudNumGuideLines).Value;
            }
        }

        // Controller-region event wiring (the unit toggles, OK button, and the guide-lines nud).
        private void WireControllerEvents()
        {
            btnOK.Click += btnOK_Click;
            rbtnDisplayImperial.Click += rbtnDisplayImperial_Click;
            rbtnDisplayMetric.Click += rbtnDisplayMetric_Click;
            nudNumGuideLines.Click += nudNumGuideLines_Click;
            tab1.SelectionChanged += Tab1_SelectionChanged;
        }

        #endregion

        // =====================================================================================
        #region Shared helpers — // [XPLAT] Avalonia adaptation glue (no WinForms equivalent)
        // =====================================================================================

        // Build the metric base bounds for all 58 nud spinners (exact designer Min/Max/DecimalPlaces
        // from FormConfig.Designer.cs; WinForms NumericUpDown defaults are Min=0/Max=100/Dec=0).
        // FixMinMaxSpinners later rescales the imperial subset on top of these.
        private void BuildNudRegistry()
        {
            // Vehicle dimensions / antenna
            _nud[nudWheelbase] = new NudState(nudWheelbase, 50M, 1999M, 0);
            _nud[nudVehicleTrack] = new NudState(nudVehicleTrack, 20M, 2000M, 0);
            _nud[nudTractorHitchLength] = new NudState(nudTractorHitchLength, 0M, 4000M, 0);
            _nud[nudAntennaHeight] = new NudState(nudAntennaHeight, 0M, 1000M, 0);
            _nud[nudAntennaPivot] = new NudState(nudAntennaPivot, -999M, 999M, 0);
            _nud[nudAntennaOffset] = new NudState(nudAntennaOffset, 0M, 500M, 0);

            // Tool hitch / offset / overlap
            _nud[nudTankHitch] = new NudState(nudTankHitch, 10M, 3000M, 0);
            _nud[nudDrawbarLength] = new NudState(nudDrawbarLength, 0M, 3000M, 0);
            _nud[nudTrailingHitchLength] = new NudState(nudTrailingHitchLength, 10M, 3000M, 0);
            _nud[nudOffset] = new NudState(nudOffset, 0M, 2500M, 0);
            _nud[nudOverlap] = new NudState(nudOverlap, 0M, 1000M, 0);
            _nud[nudTrailingToolToPivotLength] = new NudState(nudTrailingToolToPivotLength, 0M, 2000M, 0);

            // Sections
            _nud[nudNumberOfSections] = new NudState(nudNumberOfSections, 1M, 100M, 0); // Max set to ctx.MaxSections on Enter
            _nud[nudDefaultSectionWidth] = new NudState(nudDefaultSectionWidth, 10M, 1000M, 0);
            _nud[nudMinCoverage] = new NudState(nudMinCoverage, 0M, 100M, 0);
            _nud[nudCutoffSpeed] = new NudState(nudCutoffSpeed, 0M, 30M, 1); // bounds reset per units on Enter
            _nud[nudSection01] = new NudState(nudSection01, 0M, 5000M, 0);
            _nud[nudSection02] = new NudState(nudSection02, 0M, 5000M, 0);
            _nud[nudSection03] = new NudState(nudSection03, 0M, 5000M, 0);
            _nud[nudSection04] = new NudState(nudSection04, 0M, 5000M, 0);
            _nud[nudSection05] = new NudState(nudSection05, 0M, 5000M, 0);
            _nud[nudSection06] = new NudState(nudSection06, 0M, 5000M, 0);
            _nud[nudSection07] = new NudState(nudSection07, 0M, 5000M, 0);
            _nud[nudSection08] = new NudState(nudSection08, 0M, 5000M, 0);
            _nud[nudSection09] = new NudState(nudSection09, 0M, 5000M, 0);
            _nud[nudSection10] = new NudState(nudSection10, 0M, 5000M, 0);
            _nud[nudSection11] = new NudState(nudSection11, 0M, 5000M, 0);
            _nud[nudSection12] = new NudState(nudSection12, 0M, 5000M, 0);
            _nud[nudSection13] = new NudState(nudSection13, 0M, 5000M, 0);
            _nud[nudSection14] = new NudState(nudSection14, 0M, 5000M, 0);
            _nud[nudSection15] = new NudState(nudSection15, 0M, 5000M, 0);
            _nud[nudSection16] = new NudState(nudSection16, 0M, 5000M, 0);

            // Zones
            _nud[nudZone1To] = new NudState(nudZone1To, 0M, 5000M, 0);
            _nud[nudZone2To] = new NudState(nudZone2To, 0M, 5000M, 0);
            _nud[nudZone3To] = new NudState(nudZone3To, 0M, 5000M, 0);
            _nud[nudZone4To] = new NudState(nudZone4To, 0M, 5000M, 0);
            _nud[nudZone5To] = new NudState(nudZone5To, 0M, 5000M, 0);
            _nud[nudZone6To] = new NudState(nudZone6To, 0M, 5000M, 0);
            _nud[nudZone7To] = new NudState(nudZone7To, 0M, 5000M, 0);
            _nud[nudZone8To] = new NudState(nudZone8To, 0M, 5000M, 0);

            // Section timing
            _nud[nudLookAhead] = new NudState(nudLookAhead, 0.2M, 22M, 1);
            _nud[nudLookAheadOff] = new NudState(nudLookAheadOff, 0M, 20M, 1);
            _nud[nudTurnOffDelay] = new NudState(nudTurnOffDelay, 0M, 10M, 1);

            // Heading / data
            _nud[nudFixJumpDistance] = new NudState(nudFixJumpDistance, 0M, 1000M, 0);
            _nud[nudDualHeadingOffset] = new NudState(nudDualHeadingOffset, -100M, 100M, 1);
            _nud[nudDualReverseDistance] = new NudState(nudDualReverseDistance, 0.1M, 0.9M, 2);
            _nud[nudAutoSwitchDualFixSpeed] = new NudState(nudAutoSwitchDualFixSpeed, 1M, 10M, 1);

            // Machine
            _nud[nudRaiseTime] = new NudState(nudRaiseTime, 1M, 255M, 0);
            _nud[nudLowerTime] = new NudState(nudLowerTime, 1M, 255M, 0);
            _nud[nudHydLiftLookAhead] = new NudState(nudHydLiftLookAhead, 1M, 20M, 1);
            _nud[nudUser1] = new NudState(nudUser1, 0M, 255M, 0);
            _nud[nudUser2] = new NudState(nudUser2, 0M, 255M, 0);
            _nud[nudUser3] = new NudState(nudUser3, 0M, 255M, 0);
            _nud[nudUser4] = new NudState(nudUser4, 0M, 255M, 0);

            // UTurn / tram / guidelines
            _nud[nudTurnDistanceFromBoundary] = new NudState(nudTurnDistanceFromBoundary, 0M, 100M, 2);
            _nud[nudYouTurnRadius] = new NudState(nudYouTurnRadius, 2M, 100M, 2);
            _nud[nudTramWidth] = new NudState(nudTramWidth, 1M, 10000M, 0);
            _nud[nudNumGuideLines] = new NudState(nudNumGuideLines, 1M, 5000M, 0);
        }

        // Central event wiring (the .axaml wires nothing). Each region contributes its own handlers.
        private void WireEvents()
        {
            WireControllerEvents();
            WireMenuEvents();
            WireVehicleEvents();
            WireToolEvents();
            WireDataEvents();
            WireModuleEvents();
        }

        // [XPLAT] Open the FormNumeric keypad for a nud Button; on accept, store the rounded value
        // (NumericUpDown rounded Value to DecimalPlaces) and refresh the caption. Returns true on OK.
        private async Task<bool> ShowKeypad(Button nudButton)
        {
            NudState s = N(nudButton);
            var f = new FormNumeric((double)s.Min, (double)s.Max, (double)s.Value);
            bool ok = await f.ShowDialog<bool>(this);
            if (ok)
            {
                s.Value = Math.Round((decimal)f.ReturnValue, s.Decimals);
            }
            return ok;
        }

        // [XPLAT] WinForms GroupBox.Text caption -> the unnamed `groupHeader` TextBlock child of the
        // Avalonia Border (Classes="groupbox"). Preserves runtime gStr localization of group captions.
        private static void SetGroupHeader(Border box, string text)
        {
            if (box?.Child is Panel panel)
            {
                foreach (Control child in panel.Children)
                {
                    if (child is TextBlock tb && tb.Classes.Contains("groupHeader"))
                    {
                        tb.Text = text;
                        return;
                    }
                }
            }
        }

        #endregion

        // =====================================================================================
        #region Menu / Navigation — // [XPLAT] from ConfigMenu.Designer.cs
        // =====================================================================================

        private void HideSubMenu()
        {
            panelVehicleSubMenu.IsVisible = false;
            panelToolSubMenu.IsVisible = false;
            panelDataSourcesSubMenu.IsVisible = false;
            panelArduinoSubMenu.IsVisible = false;
        }

        private void ShowSubMenu(Panel subMenu, Button btn)
        {
            ClearVehicleSubBackgrounds();
            ClearToolSubBackgrounds();
            ClearMachineSubBackgrounds();
            ClearDataSubBackgrounds();
            ClearNoSubBackgrounds();

            if (!subMenu.IsVisible)
            {
                HideSubMenu();
                subMenu.IsVisible = true;
                if (ReferenceEquals(subMenu, panelVehicleSubMenu))
                {
                    tab1.SelectedItem = tabVConfig;
                }
                else if (ReferenceEquals(subMenu, panelToolSubMenu))
                {
                    tab1.SelectedItem = tabTConfig;
                }
                else if (ReferenceEquals(subMenu, panelDataSourcesSubMenu))
                {
                    tab1.SelectedItem = tabDHeading;
                }
                else if (ReferenceEquals(subMenu, panelArduinoSubMenu))
                {
                    tab1.SelectedItem = tabAMachine;
                }
                else if (ReferenceEquals(btn, btnUTurn)) tab1.SelectedItem = tabUTurn;
                else if (ReferenceEquals(btn, btnFeatureHides)) tab1.SelectedItem = tabBtns;
                else if (ReferenceEquals(btn, btnDisplay)) tab1.SelectedItem = tabDisplay;
            }
            else
            {
                tab1.SelectedItem = tabSummary;
                subMenu.IsVisible = false;
            }
        }

        private void UpdateSummary()
        {
            // [XPLAT] Parity with the WinForms ConfigMenu.Designer.cs UpdateSummary(), which called
            // configSummaryControl.UpdateSummary(mf). The hosted ConfigSummaryControl is fully migrated
            // and reads vehicle/tool values from VehicleSettings/ToolSettings directly; the two pieces of
            // FormGPS state it formerly pulled off `mf` are supplied from this view's decoupled context:
            // the metric/imperial flag (ctx.IsMetric) and the section count (tool.numOfSections).
            configSummaryControl.UpdateSummary(ctx.IsMetric, tool.numOfSections);
            labelCurrentVehicle.Text = "Vehicle: " + RegistrySettings.vehicleProfileName;
            labelCurrentTool.Text = "Tool: " + RegistrySettings.toolProfileName;
        }

        #region No Sub menu Buttons

        private void ClearNoSubBackgrounds()
        {
            btnTram.Background = InactiveBrush;
            btnUTurn.Background = InactiveBrush;
            btnDisplay.Background = InactiveBrush;
            btnFeatureHides.Background = InactiveBrush;
        }

        private void btnTram_Click(object sender, RoutedEventArgs e)
        {
            HideSubMenu();
            ClearNoSubBackgrounds();
            if (ReferenceEquals(tab1.SelectedItem, tabTram))
            {
                tab1.SelectedItem = tabSummary;
            }
            else
            {
                tab1.SelectedItem = tabTram;
                btnTram.Background = ActiveBrush;
            }
        }

        private void btnUTurn_Click(object sender, RoutedEventArgs e)
        {
            HideSubMenu();
            ClearNoSubBackgrounds();
            if (ReferenceEquals(tab1.SelectedItem, tabUTurn))
            {
                tab1.SelectedItem = tabSummary;
            }
            else
            {
                tab1.SelectedItem = tabUTurn;
                btnUTurn.Background = ActiveBrush;
            }
        }

        private void btnFeatureHides_Click(object sender, RoutedEventArgs e)
        {
            HideSubMenu();
            ClearNoSubBackgrounds();
            if (ReferenceEquals(tab1.SelectedItem, tabBtns))
            {
                tab1.SelectedItem = tabSummary;
            }
            else
            {
                tab1.SelectedItem = tabBtns;
                btnFeatureHides.Background = ActiveBrush;
            }
        }

        private void btnDisplay_Click(object sender, RoutedEventArgs e)
        {
            HideSubMenu();
            ClearNoSubBackgrounds();
            if (ReferenceEquals(tab1.SelectedItem, tabDisplay))
            {
                tab1.SelectedItem = tabSummary;
            }
            else
            {
                tab1.SelectedItem = tabDisplay;
                btnDisplay.Background = ActiveBrush;
            }
        }

        #endregion

        #region Vehicle Sub Menu Btns

        private void btnVehicle_Click(object sender, RoutedEventArgs e)
        {
            ShowSubMenu(panelVehicleSubMenu, btnVehicle);
            btnSubVehicleType.Background = ActiveBrush;
            UpdateSummary();
        }

        private void ClearVehicleSubBackgrounds()
        {
            btnSubVehicleType.Background = InactiveBrush;
            btnSubAntenna.Background = InactiveBrush;
            btnSubDimensions.Background = InactiveBrush;
            //btnSubGuidance.Background = InactiveBrush;
        }

        private void btnSubVehicleType_Click(object sender, RoutedEventArgs e)
        {
            ClearVehicleSubBackgrounds();
            tab1.SelectedItem = tabVConfig;
            btnSubVehicleType.Background = ActiveBrush;
        }

        private void btnSubDimensions_Click(object sender, RoutedEventArgs e)
        {
            ClearVehicleSubBackgrounds();
            tab1.SelectedItem = tabVDimensions;
            btnSubDimensions.Background = ActiveBrush;
        }

        private void btnSubAntenna_Click(object sender, RoutedEventArgs e)
        {
            ClearVehicleSubBackgrounds();
            tab1.SelectedItem = tabVAntenna;
            btnSubAntenna.Background = ActiveBrush;
        }

        private void btnSubGuidance_Click(object sender, RoutedEventArgs e)
        {
            ClearVehicleSubBackgrounds();
            tab1.SelectedItem = tabVGuidance;
            //btnSubGuidance.Background = ActiveBrush;
        }

        #endregion

        #region Tool Sub Menu

        private void btnTool_Click(object sender, RoutedEventArgs e)
        {
            ShowSubMenu(panelToolSubMenu, btnTool);
            btnSubToolType.Background = ActiveBrush;
        }

        private void ClearToolSubBackgrounds()
        {
            btnSubToolType.Background = InactiveBrush;
            btnSubHitch.Background = InactiveBrush;
            btnSubSections.Background = InactiveBrush;
            btnSubSwitches.Background = InactiveBrush;
            btnSubToolSettings.Background = InactiveBrush;
            btnSubToolOffset.Background = InactiveBrush;
            btnSubPivot.Background = InactiveBrush;
        }

        private void btnSubToolType_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabTConfig;
            btnSubToolType.Background = ActiveBrush;
        }

        private void btnSubHitch_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabTHitch;
            btnSubHitch.Background = ActiveBrush;
        }

        private void btnSubToolOffset_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabToolOffset;
            btnSubToolOffset.Background = ActiveBrush;
        }

        private void btnSubPivot_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabToolPivot;
            btnSubPivot.Background = ActiveBrush;
        }

        private void btnSubSections_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabTSections;
            btnSubSections.Background = ActiveBrush;
        }

        private void btnSubSwitches_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabTSwitches;
            btnSubSwitches.Background = ActiveBrush;
        }

        private void btnSubToolSettings_Click(object sender, RoutedEventArgs e)
        {
            ClearToolSubBackgrounds();
            tab1.SelectedItem = tabTSettings;
            btnSubToolSettings.Background = ActiveBrush;
        }

        #endregion

        #region SubMenu Data Sources

        private void ClearDataSubBackgrounds()
        {
            btnSubHeading.Background = InactiveBrush;
            btnSubRoll.Background = InactiveBrush;
        }

        private void btnDataSources_Click(object sender, RoutedEventArgs e)
        {
            ShowSubMenu(panelDataSourcesSubMenu, btnDataSources);
            btnSubHeading.Background = ActiveBrush;
        }

        private void btnSubHeading_Click(object sender, RoutedEventArgs e)
        {
            ClearDataSubBackgrounds();
            tab1.SelectedItem = tabDHeading;
            btnSubHeading.Background = ActiveBrush;
        }

        private void btnSubRoll_Click(object sender, RoutedEventArgs e)
        {
            ClearDataSubBackgrounds();
            tab1.SelectedItem = tabDRoll;
            btnSubRoll.Background = ActiveBrush;
        }

        #endregion

        #region Module

        private void ClearMachineSubBackgrounds()
        {
            btnMachineModule.Background = InactiveBrush;
            btnMachineRelay.Background = InactiveBrush;
        }

        private void btnArduino_Click(object sender, RoutedEventArgs e)
        {
            ShowSubMenu(panelArduinoSubMenu, btnArduino);
            btnMachineModule.Background = ActiveBrush;
        }

        private void btnMachineModule_Click(object sender, RoutedEventArgs e)
        {
            ClearMachineSubBackgrounds();
            tab1.SelectedItem = tabAMachine;
            btnMachineModule.Background = ActiveBrush;
        }

        private void btnMachineRelay_Click(object sender, RoutedEventArgs e)
        {
            ClearMachineSubBackgrounds();
            tab1.SelectedItem = tabRelay;
            btnMachineRelay.Background = ActiveBrush;
        }

        #endregion

        // [XPLAT] WinForms fired tabX_Enter on entry (load) and tabX_Leave on exit (save). Avalonia
        // raises one TabControl.SelectionChanged; dispatch its RemovedItems -> Leave and AddedItems ->
        // Enter, by TabItem reference, reproducing the per-tab load/save lifecycle.
        private void Tab1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems != null)
            {
                foreach (object it in e.RemovedItems)
                {
                    if (it is TabItem leftTab) FireTabLeave(leftTab);
                }
            }
            if (e.AddedItems != null)
            {
                foreach (object it in e.AddedItems)
                {
                    if (it is TabItem enteredTab) FireTabEnter(enteredTab);
                }
            }
        }

        private void FireTabEnter(TabItem t)
        {
            if (ReferenceEquals(t, tabSummary)) tabSummary_Enter();
            else if (ReferenceEquals(t, tabDisplay)) tabDisplay_Enter();
            else if (ReferenceEquals(t, tabVConfig)) tabVConfig_Enter();
            else if (ReferenceEquals(t, tabVDimensions)) tabVDimensions_Enter();
            else if (ReferenceEquals(t, tabVAntenna)) tabVAntenna_Enter();
            else if (ReferenceEquals(t, tabVGuidance)) tabVGuidance_Enter();
            else if (ReferenceEquals(t, tabTConfig)) tabTConfig_Enter();
            else if (ReferenceEquals(t, tabTHitch)) tabTHitch_Enter();
            else if (ReferenceEquals(t, tabToolOffset)) tabToolOffset_Enter();
            else if (ReferenceEquals(t, tabToolPivot)) tabToolPivot_Enter();
            else if (ReferenceEquals(t, tabTSections)) tabTSections_Enter();
            else if (ReferenceEquals(t, tabTSwitches)) tabTSwitches_Enter();
            else if (ReferenceEquals(t, tabTSettings)) tabTSettings_Enter();
            else if (ReferenceEquals(t, tabDHeading)) tabDHeading_Enter();
            else if (ReferenceEquals(t, tabDRoll)) tabDRoll_Enter();
            else if (ReferenceEquals(t, tabAMachine)) tabAMachine_Enter();
            else if (ReferenceEquals(t, tabRelay)) tabRelay_Enter();
            else if (ReferenceEquals(t, tabUTurn)) tabUTurn_Enter();
            else if (ReferenceEquals(t, tabTram)) tabTram_Enter();
            else if (ReferenceEquals(t, tabBtns)) tabBtns_Enter();
        }

        private void FireTabLeave(TabItem t)
        {
            if (ReferenceEquals(t, tabSummary)) tabSummary_Leave();
            else if (ReferenceEquals(t, tabDisplay)) tabDisplay_Leave();
            else if (ReferenceEquals(t, tabVConfig)) tabVConfig_Leave();
            // tabVDimensions has no Leave handler in the source (Enter only).
            else if (ReferenceEquals(t, tabVAntenna)) tabVAntenna_Leave();
            else if (ReferenceEquals(t, tabVGuidance)) tabVGuidance_Leave();
            else if (ReferenceEquals(t, tabTConfig)) tabTConfig_Leave();
            else if (ReferenceEquals(t, tabTHitch)) tabTHitch_Leave();
            else if (ReferenceEquals(t, tabToolOffset)) tabToolOffset_Leave();
            else if (ReferenceEquals(t, tabToolPivot)) tabToolPivot_Leave();
            else if (ReferenceEquals(t, tabTSections)) tabTSections_Leave();
            else if (ReferenceEquals(t, tabTSwitches)) tabTSwitches_Leave();
            else if (ReferenceEquals(t, tabTSettings)) tabTSettings_Leave();
            else if (ReferenceEquals(t, tabDHeading)) tabDHeading_Leave();
            else if (ReferenceEquals(t, tabDRoll)) tabDRoll_Leave();
            else if (ReferenceEquals(t, tabAMachine)) tabAMachine_Leave();
            else if (ReferenceEquals(t, tabRelay)) tabRelay_Leave();
            else if (ReferenceEquals(t, tabUTurn)) tabUTurn_Leave();
            else if (ReferenceEquals(t, tabTram)) tabTram_Leave();
            else if (ReferenceEquals(t, tabBtns)) tabBtns_Leave();
        }

        private void WireMenuEvents()
        {
            btnVehicle.Click += btnVehicle_Click;
            btnTool.Click += btnTool_Click;
            btnDataSources.Click += btnDataSources_Click;
            btnArduino.Click += btnArduino_Click;

            btnTram.Click += btnTram_Click;
            btnUTurn.Click += btnUTurn_Click;
            btnFeatureHides.Click += btnFeatureHides_Click;
            btnDisplay.Click += btnDisplay_Click;

            btnSubVehicleType.Click += btnSubVehicleType_Click;
            btnSubDimensions.Click += btnSubDimensions_Click;
            btnSubAntenna.Click += btnSubAntenna_Click;
            btnSubGuidance.Click += btnSubGuidance_Click;

            btnSubToolType.Click += btnSubToolType_Click;
            btnSubHitch.Click += btnSubHitch_Click;
            btnSubToolOffset.Click += btnSubToolOffset_Click;
            btnSubPivot.Click += btnSubPivot_Click;
            btnSubSections.Click += btnSubSections_Click;
            btnSubSwitches.Click += btnSubSwitches_Click;
            btnSubToolSettings.Click += btnSubToolSettings_Click;

            btnSubHeading.Click += btnSubHeading_Click;
            btnSubRoll.Click += btnSubRoll_Click;

            btnMachineModule.Click += btnMachineModule_Click;
            btnMachineRelay.Click += btnMachineRelay_Click;
        }

        #endregion

        // =====================================================================================
        #region Vehicle — // [XPLAT] from ConfigVehicle.Designer.cs (+ Display tab from FormConfig.cs)
        // =====================================================================================

        // ---- Display tab (FormConfig.cs tabDisplay_Enter 374-395 / tabDisplay_Leave 397-400) ----
        private void tabDisplay_Enter()
        {
            chkDisplayBrightness.IsChecked = ctx.IsBrightnessOn;
            chkDisplayFloor.IsChecked = ctx.IsTextureOn;
            chkDisplayGrid.IsChecked = ctx.IsGridOn;
            chkDisplaySpeedo.IsChecked = ctx.IsSpeedoOn;
            chkDisplayStartFullScreen.IsChecked = Settings.Default.setDisplay_isStartFullScreen;
            chkSvennArrow.IsChecked = ctx.IsSvennArrowOn;
            chkDisplayExtraGuides.IsChecked = ctx.IsSideGuideLines;
            chkDisplayPolygons.IsChecked = ctx.IsDrawPolygons;
            chkDisplayKeyboard.IsChecked = ctx.IsKeyboardOn;
            chkDisplayLogElevation.IsChecked = ctx.IsLogElevation;
            chkDirectionMarkers.IsChecked = ToolSettings.Default.setTool_isDirectionMarkers;
            chkSectionLines.IsChecked = Settings.Default.setDisplay_isSectionLinesOn;
            chkLineSmooth.IsChecked = Settings.Default.setDisplay_isLineSmooth;
            chkboxHeadlandDist.IsChecked = Settings.Default.isHeadlandDistanceOn;

            if (ctx.IsMetric) rbtnDisplayMetric.IsChecked = true;
            else rbtnDisplayImperial.IsChecked = true;

            N(nudNumGuideLines).Value = abLine.NumGuideLines;
        }

        private void tabDisplay_Leave()
        {
            SaveDisplaySettings();
        }

        // Port of SaveDisplaySettings (ConfigVehicle.Designer.cs 22-71). EXACT Settings keys (schema frozen).
        private void SaveDisplaySettings()
        {
            ctx.IsTextureOn = chkDisplayFloor.IsChecked == true;
            ctx.IsGridOn = chkDisplayGrid.IsChecked == true;
            ctx.IsSpeedoOn = chkDisplaySpeedo.IsChecked == true;
            ctx.IsSideGuideLines = chkDisplayExtraGuides.IsChecked == true;

            ctx.IsDrawPolygons = chkDisplayPolygons.IsChecked == true;
            ctx.IsKeyboardOn = chkDisplayKeyboard.IsChecked == true;

            ctx.IsBrightnessOn = chkDisplayBrightness.IsChecked == true;
            ctx.IsSvennArrowOn = chkSvennArrow.IsChecked == true;
            ctx.IsLogElevation = chkDisplayLogElevation.IsChecked == true;

            ctx.IsDirectionMarkers = chkDirectionMarkers.IsChecked == true;
            ctx.IsSectionLinesOn = chkSectionLines.IsChecked == true;
            ctx.IsLineSmooth = chkLineSmooth.IsChecked == true;
            ctx.IsHeadlandDistanceOn = chkboxHeadlandDist.IsChecked == true;

            Settings.Default.setDisplay_isBrightnessOn = ctx.IsBrightnessOn;
            Settings.Default.setDisplay_isTextureOn = ctx.IsTextureOn;
            Settings.Default.setMenu_isGridOn = ctx.IsGridOn;

            Settings.Default.setDisplay_isSvennArrowOn = ctx.IsSvennArrowOn;
            Settings.Default.setMenu_isSpeedoOn = ctx.IsSpeedoOn;
            Settings.Default.setDisplay_isStartFullScreen = chkDisplayStartFullScreen.IsChecked == true;
            Settings.Default.setMenu_isSideGuideLines = ctx.IsSideGuideLines;

            Settings.Default.setMenu_isPureOn = ctx.IsPureDisplayOn;
            Settings.Default.setMenu_isLightbarOn = ctx.IsLightbarOn;
            Settings.Default.setDisplay_isKeyboardOn = ctx.IsKeyboardOn;
            Settings.Default.isHeadlandDistanceOn = ctx.IsHeadlandDistanceOn;
            Settings.Default.setDisplay_isLogElevation = ctx.IsLogElevation;

            Settings.Default.setMenu_isMetric = rbtnDisplayMetric.IsChecked == true;
            ctx.IsMetric = rbtnDisplayMetric.IsChecked == true;

            ToolSettings.Default.setTool_isDirectionMarkers = ctx.IsDirectionMarkers;

            Settings.Default.setAS_numGuideLines = abLine.NumGuideLines;
            Settings.Default.setDisplay_isSectionLinesOn = ctx.IsSectionLinesOn;
            Settings.Default.setDisplay_isLineSmooth = ctx.IsLineSmooth;
            Settings.Default.isHeadlandDistanceOn = ctx.IsHeadlandDistanceOn;

            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
            Settings.Default.Save();
        }

        // ---- Antenna tab (ConfigVehicle.Designer.cs 76-173) ----
        private void tabVAntenna_Enter()
        {
            N(nudAntennaHeight).Value = (int)(VehicleSettings.Default.setVehicle_antennaHeight * ctx.M2InchOrCm);
            N(nudAntennaPivot).Value = (int)(VehicleSettings.Default.setVehicle_antennaPivot * ctx.M2InchOrCm);

            // negative is to the right
            N(nudAntennaOffset).Value = (int)(Math.Abs(VehicleSettings.Default.setVehicle_antennaOffset) * ctx.M2InchOrCm);

            rbtnAntennaLeft.IsChecked = false;
            rbtnAntennaRight.IsChecked = false;
            rbtnAntennaCenter.IsChecked = false;
            rbtnAntennaLeft.IsChecked = VehicleSettings.Default.setVehicle_antennaOffset > 0;
            rbtnAntennaRight.IsChecked = VehicleSettings.Default.setVehicle_antennaOffset < 0;
            rbtnAntennaCenter.IsChecked = VehicleSettings.Default.setVehicle_antennaOffset == 0;

            // [XPLAT] Parity with ConfigVehicle.Designer.cs: swap the antenna diagram to match the
            // configured vehicle type. The per-type images are migrated avares:// assets exposed as
            // Avalonia Bitmaps by Properties.Resources; assign to the Image's Source (the WinForms
            // original set pboxAntenna.BackgroundImage). Same conditions/order as the original.
            if (VehicleSettings.Default.setVehicle_vehicleType == 0)
                pboxAntenna.Source = Properties.Resources.AntennaTractor;
            else if (VehicleSettings.Default.setVehicle_vehicleType == 1)
                pboxAntenna.Source = Properties.Resources.AntennaHarvester;
            else if (VehicleSettings.Default.setVehicle_vehicleType == 2)
                pboxAntenna.Source = Properties.Resources.AntennaArticulated;

            label98.Text = ctx.UnitsInCm;
            label99.Text = ctx.UnitsInCm;
            label100.Text = ctx.UnitsInCm;
        }

        private void tabVAntenna_Leave()
        {
            VehicleSettings.Default.Save();
        }

        // Shared by rbtnAntennaLeft/Right/Center (Click). Programmatic IsChecked sets do NOT fire Click
        // (Avalonia Click == user activation), matching WinForms .Click semantics — so no reentrancy.
        private void rbtnAntennaLeft_Click(object sender, RoutedEventArgs e)
        {
            if (rbtnAntennaRight.IsChecked == true)
                vehicle.VehicleConfig.AntennaOffset = (double)N(nudAntennaOffset).Value * -ctx.InchOrCm2m;
            else if (rbtnAntennaLeft.IsChecked == true)
                vehicle.VehicleConfig.AntennaOffset = (double)N(nudAntennaOffset).Value * ctx.InchOrCm2m;
            else
            {
                vehicle.VehicleConfig.AntennaOffset = 0;
                N(nudAntennaOffset).Value = 0;
            }

            VehicleSettings.Default.setVehicle_antennaOffset = vehicle.VehicleConfig.AntennaOffset;
        }

        private async void nudAntennaOffset_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudAntennaOffset))
            {
                if ((double)N(nudAntennaOffset).Value == 0)
                {
                    rbtnAntennaLeft.IsChecked = false;
                    rbtnAntennaRight.IsChecked = false;
                    rbtnAntennaCenter.IsChecked = true;
                    vehicle.VehicleConfig.AntennaOffset = 0;
                }
                else
                {
                    if (!(rbtnAntennaLeft.IsChecked == true) && !(rbtnAntennaRight.IsChecked == true))
                        rbtnAntennaRight.IsChecked = true;

                    if (rbtnAntennaRight.IsChecked == true)
                        vehicle.VehicleConfig.AntennaOffset = (double)N(nudAntennaOffset).Value * -ctx.InchOrCm2m;
                    else
                        vehicle.VehicleConfig.AntennaOffset = (double)N(nudAntennaOffset).Value * ctx.InchOrCm2m;
                }

                VehicleSettings.Default.setVehicle_antennaOffset = vehicle.VehicleConfig.AntennaOffset;
            }
        }

        private async void nudAntennaPivot_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudAntennaPivot))
            {
                VehicleSettings.Default.setVehicle_antennaPivot = (double)N(nudAntennaPivot).Value * ctx.InchOrCm2m;
                vehicle.VehicleConfig.AntennaPivot = VehicleSettings.Default.setVehicle_antennaPivot;
            }
        }

        private async void nudAntennaHeight_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudAntennaHeight))
            {
                VehicleSettings.Default.setVehicle_antennaHeight = (double)N(nudAntennaHeight).Value * ctx.InchOrCm2m;
                vehicle.VehicleConfig.AntennaHeight = VehicleSettings.Default.setVehicle_antennaHeight;
            }
        }

        // ---- Vehicle Dimensions tab (ConfigVehicle.Designer.cs 179-242; Enter only, no Leave) ----
        private void tabVDimensions_Enter()
        {
            N(nudWheelbase).Value = (int)(Math.Abs(VehicleSettings.Default.setVehicle_wheelbase) * ctx.M2InchOrCm);
            N(nudVehicleTrack).Value = (int)(Math.Abs(VehicleSettings.Default.setVehicle_trackWidth) * ctx.M2InchOrCm);
            N(nudTractorHitchLength).Value = (int)(Math.Abs(ToolSettings.Default.setVehicle_hitchLength) * ctx.M2InchOrCm);

            // [XPLAT] Parity with ConfigVehicle.Designer.cs: swap the wheelbase-radius diagram to match the
            // configured vehicle type. Per-type images are migrated avares:// assets exposed as Avalonia
            // Bitmaps by Properties.Resources; assign to the Image's Source (the WinForms original set
            // pictureBox1.Image). Same conditions/order as the original.
            if (vehicle.VehicleConfig.Type == VehicleType.Tractor)
                pictureBox1.Source = Properties.Resources.RadiusWheelBase;
            else if (vehicle.VehicleConfig.Type == VehicleType.Harvester)
                pictureBox1.Source = Properties.Resources.RadiusWheelBaseHarvester;
            else if (vehicle.VehicleConfig.Type == VehicleType.Articulated)
                pictureBox1.Source = Properties.Resources.RadiusWheelBaseArticulated;

            nudTractorHitchLength.IsVisible = (rbtnTBT.IsChecked == true) || (rbtnTrailing.IsChecked == true);
            label94.IsVisible = (rbtnTBT.IsChecked == true) || (rbtnTrailing.IsChecked == true);
            labelHitchLength.IsVisible = (rbtnTBT.IsChecked == true) || (rbtnTrailing.IsChecked == true);
            HitchLengthBlindBox.IsVisible = (rbtnFixedRear.IsChecked == true) || (rbtnFront.IsChecked == true);

            label94.Text = ctx.UnitsInCm;
            label95.Text = ctx.UnitsInCm;
            label97.Text = ctx.UnitsInCm;
        }

        private async void nudTractorHitchLength_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTractorHitchLength))
            {
                tool.hitchLength = (double)N(nudTractorHitchLength).Value * ctx.InchOrCm2m;
                if (!ToolSettings.Default.setTool_isToolFront)
                {
                    tool.hitchLength *= -1;
                }
                ToolSettings.Default.setVehicle_hitchLength = tool.hitchLength;
            }
        }

        private async void nudWheelbase_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudWheelbase))
            {
                VehicleSettings.Default.setVehicle_wheelbase = (double)N(nudWheelbase).Value * ctx.InchOrCm2m;
                vehicle.VehicleConfig.Wheelbase = VehicleSettings.Default.setVehicle_wheelbase;
                VehicleSettings.Default.Save();
            }
        }

        private async void nudVehicleTrack_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudVehicleTrack))
            {
                VehicleSettings.Default.setVehicle_trackWidth = (double)N(nudVehicleTrack).Value * ctx.InchOrCm2m;
                vehicle.VehicleConfig.TrackWidth = VehicleSettings.Default.setVehicle_trackWidth;
                tram.HalfWheelTrack = vehicle.VehicleConfig.TrackWidth * 0.5;
                VehicleSettings.Default.Save();
            }
        }

        // ---- Vehicle Guidance tab (ConfigVehicle.Designer.cs 248-254; both empty) ----
        private void tabVGuidance_Enter()
        {
        }

        private void tabVGuidance_Leave()
        {
        }

        // ---- Vehicle Config tab (ConfigVehicle.Designer.cs 259-295) ----
        private void tabVConfig_Enter()
        {
            // [XPLAT] Parity with WinForms ConfigVehicle.Designer.cs tabVConfig_Enter, which called
            // configVehicleControl.Initialize(mf.vehicle.VehicleConfig). The hosted ConfigVehicleControl
            // is fully migrated; seed it from this view's decoupled vehicle state.
            configVehicleControl.Initialize(vehicle.VehicleConfig);
        }

        private void tabVConfig_Leave()
        {
            // [XPLAT] Parity with WinForms ConfigVehicle.Designer.cs tabVConfig_Leave, which called
            // configVehicleControl.UpdateSettings() FIRST to read the edited values back out of the hosted
            // editor (into VehicleConfig / VehicleSettings) before the harvester-specific normalisation and
            // the texture refresh/save below run against those just-committed values.
            configVehicleControl.UpdateSettings();

            if (vehicle.VehicleConfig.Type == VehicleType.Harvester)
            {
                if (tool.hitchLength < 0) tool.hitchLength *= -1;

                ToolSettings.Default.setTool_isToolFront = true;
                ToolSettings.Default.setTool_isToolTBT = false;
                ToolSettings.Default.setTool_isToolTrailing = false;
                ToolSettings.Default.setTool_isToolRearFixed = false;
            }

            // [XPLAT] mf.VehicleTextures.*.SetBitmap(...) per VehicleType deferred -> ctx.RefreshVehicleTextures().
            ctx.RefreshVehicleTextures();

            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
            Settings.Default.Save();
        }

        private void WireVehicleEvents()
        {
            rbtnAntennaLeft.Click += rbtnAntennaLeft_Click;
            rbtnAntennaRight.Click += rbtnAntennaLeft_Click;
            rbtnAntennaCenter.Click += rbtnAntennaLeft_Click;
            nudAntennaOffset.Click += nudAntennaOffset_Click;
            nudAntennaPivot.Click += nudAntennaPivot_Click;
            nudAntennaHeight.Click += nudAntennaHeight_Click;

            nudTractorHitchLength.Click += nudTractorHitchLength_Click;
            nudWheelbase.Click += nudWheelbase_Click;
            nudVehicleTrack.Click += nudVehicleTrack_Click;
        }

        #endregion

        // =====================================================================================
        #region Tool — // [XPLAT] from ConfigTool.Designer.cs (Config/Hitch/Settings/Offset/Pivot/Sections/Switch)
        // =====================================================================================

        // [XPLAT] Shared field originally declared in ConfigModule.Designer.cs:202 (private string[] words),
        // consumed by BOTH the Tool zones load (setTool_zones split) and the Module relay-pin load
        // (setRelay_pinConfig split). Declared once here for the folded code-behind.
        private string[] words;

        // [XPLAT] WinForms FormDialog.Show(title,message,severity) -> async FormDialogView, fire-and-forget
        // (the dialog is informational/parity; the caller does not await it).
        private void ShowFormDialog(string title, string message, DialogSeverity severity)
        {
            _ = FormDialogView.ShowAsync(title, message, severity, this);
        }

        // [XPLAT] WinForms mf.YesMessageBox(message) -> informational FormDialogView, fire-and-forget.
        private void ShowYesMessage(string message)
        {
            _ = FormDialogView.ShowAsync("AgOpenGPS", message, DialogSeverity.Info, this);
        }

        // ---- Config sub-tab (ConfigTool.Designer.cs 29-137) ----
        private void tabTConfig_Enter()
        {
            lblInchCm2.Text = ctx.UnitsInCm;

            if (vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                pboxConfigHarvester.IsVisible = false;

                rbtnTBT.IsVisible = true;
                rbtnTrailing.IsVisible = true;
                rbtnFixedRear.IsVisible = true;
                rbtnFront.IsVisible = true;

                if (ToolSettings.Default.setTool_isToolFront)
                {
                    rbtnTBT.IsChecked = false;
                    rbtnTrailing.IsChecked = false;
                    rbtnFixedRear.IsChecked = false;
                    rbtnFront.IsChecked = true;
                }
                else if (ToolSettings.Default.setTool_isToolTBT)
                {
                    rbtnTBT.IsChecked = true;
                    rbtnTrailing.IsChecked = false;
                    rbtnFixedRear.IsChecked = false;
                    rbtnFront.IsChecked = false;
                }
                else if (ToolSettings.Default.setTool_isToolTrailing)
                {
                    rbtnTBT.IsChecked = false;
                    rbtnTrailing.IsChecked = true;
                    rbtnFixedRear.IsChecked = false;
                    rbtnFront.IsChecked = false;
                }
                else if (ToolSettings.Default.setTool_isToolRearFixed)
                {
                    rbtnTBT.IsChecked = false;
                    rbtnTrailing.IsChecked = false;
                    rbtnFixedRear.IsChecked = true;
                    rbtnFront.IsChecked = false;
                }
            }
            else
            {
                pboxConfigHarvester.IsVisible = true;

                rbtnTBT.IsVisible = false;
                rbtnTrailing.IsVisible = false;
                rbtnFixedRear.IsVisible = false;
                rbtnFront.IsVisible = false;
            }
        }

        private void tabTConfig_Leave()
        {
            if (vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                if (rbtnFront.IsChecked == true)
                {
                    ToolSettings.Default.setTool_isToolFront = true;
                    ToolSettings.Default.setTool_isToolTBT = false;
                    ToolSettings.Default.setTool_isToolTrailing = false;
                    ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnTBT.IsChecked == true)
                {
                    ToolSettings.Default.setTool_isToolFront = false;
                    ToolSettings.Default.setTool_isToolTBT = true;
                    ToolSettings.Default.setTool_isToolTrailing = true;
                    ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnTrailing.IsChecked == true)
                {
                    ToolSettings.Default.setTool_isToolFront = false;
                    ToolSettings.Default.setTool_isToolTBT = false;
                    ToolSettings.Default.setTool_isToolTrailing = true;
                    ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnFixedRear.IsChecked == true)
                {
                    ToolSettings.Default.setTool_isToolFront = false;
                    ToolSettings.Default.setTool_isToolTBT = false;
                    ToolSettings.Default.setTool_isToolTrailing = false;
                    ToolSettings.Default.setTool_isToolRearFixed = true;
                }
            }
            else
            {
                ToolSettings.Default.setTool_isToolFront = true;
                ToolSettings.Default.setTool_isToolTBT = false;
                ToolSettings.Default.setTool_isToolTrailing = false;
                ToolSettings.Default.setTool_isToolRearFixed = false;
            }

            tool.isToolRearFixed = ToolSettings.Default.setTool_isToolRearFixed;
            tool.isToolTrailing = ToolSettings.Default.setTool_isToolTrailing;
            tool.isToolTBT = ToolSettings.Default.setTool_isToolTBT;
            tool.isToolFrontFixed = ToolSettings.Default.setTool_isToolFront;

            if (ToolSettings.Default.setTool_isToolFront && tool.hitchLength < 0)
                tool.hitchLength *= -1;
            else if (!ToolSettings.Default.setTool_isToolFront && tool.hitchLength > 0)
                tool.hitchLength *= -1;
            ToolSettings.Default.setVehicle_hitchLength = tool.hitchLength;

            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
        }

        // ---- Hitch sub-tab (ConfigTool.Designer.cs 143-249) ----
        private void tabTHitch_Enter()
        {
            if (vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                // fixed - hitch only on vehicle
                if (ToolSettings.Default.setTool_isToolFront)
                {
                    nudTrailingHitchLength.IsVisible = false;
                    nudDrawbarLength.IsVisible = true;
                    nudTankHitch.IsVisible = false;
                    // [XPLAT] nudDrawbarLength.Left/picboxToolHitch.BackgroundImage layout omitted — XAML positions controls.
                }
                else if (ToolSettings.Default.setTool_isToolRearFixed)
                {
                    nudTrailingHitchLength.IsVisible = false;
                    nudDrawbarLength.IsVisible = true;
                    nudTankHitch.IsVisible = false;
                }
                else if (ToolSettings.Default.setTool_isToolTBT)
                {
                    nudTrailingHitchLength.IsVisible = true;
                    nudDrawbarLength.IsVisible = false;
                    nudTankHitch.IsVisible = true;
                }
                else if (ToolSettings.Default.setTool_isToolTrailing)
                {
                    nudTrailingHitchLength.IsVisible = true;
                    nudDrawbarLength.IsVisible = false;
                    nudTankHitch.IsVisible = false;
                }

                label112.Text = ctx.UnitsInCm;
            }
            else
            {
                nudTrailingHitchLength.IsVisible = false;
                nudDrawbarLength.IsVisible = true;
                nudTankHitch.IsVisible = false;
                label112.Text = ctx.UnitsInCm;
            }

            N(nudDrawbarLength).Value = (int)(Math.Abs(ToolSettings.Default.setVehicle_hitchLength) * ctx.M2InchOrCm);
            N(nudTrailingHitchLength).Value = (int)(Math.Abs(ToolSettings.Default.setVehicle_toolTrailingHitchLength) * ctx.M2InchOrCm);
            N(nudTankHitch).Value = (int)(Math.Abs(ToolSettings.Default.setVehicle_tankTrailingHitchLength) * ctx.M2InchOrCm);
        }

        private void tabTHitch_Leave()
        {
            VehicleSettings.Default.Save();
            ToolSettings.Default.Save();
        }

        private async void nudDrawbarLength_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudDrawbarLength))
            {
                tool.hitchLength = (double)N(nudDrawbarLength).Value * ctx.InchOrCm2m;
                if (!ToolSettings.Default.setTool_isToolFront)
                {
                    tool.hitchLength *= -1;
                }
                ToolSettings.Default.setVehicle_hitchLength = tool.hitchLength;
            }
        }

        private async void nudTankHitch_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTankHitch))
            {
                tool.tankTrailingHitchLength = (double)N(nudTankHitch).Value * -ctx.InchOrCm2m;
                ToolSettings.Default.setVehicle_tankTrailingHitchLength = tool.tankTrailingHitchLength;
            }
        }

        private async void nudTrailingHitchLength_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTrailingHitchLength))
            {
                tool.trailingHitchLength = (double)N(nudTrailingHitchLength).Value * -ctx.InchOrCm2m;
                ToolSettings.Default.setVehicle_toolTrailingHitchLength = tool.trailingHitchLength;
            }
        }

        // ---- Settings sub-tab (ConfigTool.Designer.cs 255-333) ----
        private void tabTSettings_Enter()
        {
            N(nudLookAhead).Value = (decimal)ToolSettings.Default.setVehicle_toolLookAheadOn;
            N(nudLookAheadOff).Value = (decimal)ToolSettings.Default.setVehicle_toolLookAheadOff;
            N(nudTurnOffDelay).Value = (decimal)ToolSettings.Default.setVehicle_toolOffDelay;
        }

        private void tabTSettings_Leave()
        {
            ToolSettings.Default.setVehicle_toolLookAheadOn = tool.lookAheadOnSetting;
            ToolSettings.Default.setVehicle_toolLookAheadOff = tool.lookAheadOffSetting;
            ToolSettings.Default.setVehicle_toolOffDelay = tool.turnOffDelay;

            ToolSettings.Default.Save();
        }

        private async void nudLookAhead_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudLookAhead))
            {
                if (N(nudLookAheadOff).Value > (N(nudLookAhead).Value * 0.8m))
                {
                    N(nudLookAheadOff).Value = N(nudLookAhead).Value * 0.8m;
                }

                tool.lookAheadOnSetting = (double)N(nudLookAhead).Value;
                tool.lookAheadOffSetting = (double)N(nudLookAheadOff).Value;
                tool.turnOffDelay = 0;
            }
        }

        private async void nudLookAheadOff_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudLookAheadOff))
            {
                if (N(nudLookAheadOff).Value > (N(nudLookAhead).Value * 0.8m))
                {
                    N(nudLookAheadOff).Value = N(nudLookAhead).Value * 0.8m;
                }
                tool.lookAheadOffSetting = (double)N(nudLookAheadOff).Value;

                if (N(nudLookAheadOff).Value > 0)
                {
                    tool.turnOffDelay = 0;
                    N(nudTurnOffDelay).Value = 0;
                }

                tool.lookAheadOnSetting = (double)N(nudLookAhead).Value;
                tool.lookAheadOffSetting = (double)N(nudLookAheadOff).Value;
                tool.turnOffDelay = 0;
            }
        }

        private async void nudTurnOffDelay_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTurnOffDelay))
            {
                if (N(nudTurnOffDelay).Value > 0)
                {
                    N(nudLookAheadOff).Value = 0;
                }
                tool.turnOffDelay = (double)N(nudTurnOffDelay).Value;
                tool.lookAheadOffSetting = (double)N(nudLookAheadOff).Value;
                tool.lookAheadOnSetting = (double)N(nudLookAhead).Value;
            }
        }

        // ---- Offset sub-tab (ConfigTool.Designer.cs 338-452) ----
        private void tabToolOffset_Enter()
        {
            N(nudOffset).Value = (decimal)(Math.Abs(ToolSettings.Default.setVehicle_toolOffset) * ctx.M2InchOrCm);

            rbtnToolRightPositive.IsChecked = false;
            rbtnLeftNegative.IsChecked = false;
            rbtnToolRightPositive.IsChecked = ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.IsChecked = ToolSettings.Default.setVehicle_toolOffset < 0;

            N(nudOverlap).Value = (decimal)(Math.Abs(ToolSettings.Default.setVehicle_toolOverlap) * ctx.M2InchOrCm);

            rbtnToolOverlap.IsChecked = false;
            rbtnToolGap.IsChecked = false;
            rbtnToolOverlap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap < 0;

            label175.Text = ctx.UnitsInCm;
            label176.Text = ctx.UnitsInCm;
        }

        private void tabToolOffset_Leave()
        {
            ToolSettings.Default.Save();
        }

        private async void nudOffset_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudOffset))
            {
                if (!(rbtnToolRightPositive.IsChecked == true) && !(rbtnLeftNegative.IsChecked == true))
                    rbtnToolRightPositive.IsChecked = true;

                if (rbtnToolRightPositive.IsChecked == true)
                    tool.offset = (double)N(nudOffset).Value * ctx.InchOrCm2m;
                else
                    tool.offset = (double)N(nudOffset).Value * -ctx.InchOrCm2m;

                ToolSettings.Default.setVehicle_toolOffset = tool.offset;
            }

            rbtnToolRightPositive.IsChecked = false;
            rbtnLeftNegative.IsChecked = false;
            rbtnToolRightPositive.IsChecked = ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.IsChecked = ToolSettings.Default.setVehicle_toolOffset < 0;
        }

        private void btnZeroToolOffset_Click(object sender, RoutedEventArgs e)
        {
            N(nudOffset).Value = 0;
            rbtnToolRightPositive.IsChecked = false;
            rbtnLeftNegative.IsChecked = false;

            tool.offset = 0;
            ToolSettings.Default.setVehicle_toolOffset = tool.offset;
        }

        // Shared by rbtnToolRightPositive + rbtnLeftNegative (Click).
        private void rbtnToolRightPositive_Click(object sender, RoutedEventArgs e)
        {
            if (rbtnToolRightPositive.IsChecked == true)
                tool.offset = (double)N(nudOffset).Value * ctx.InchOrCm2m;
            else
                tool.offset = (double)N(nudOffset).Value * -ctx.InchOrCm2m;
            ToolSettings.Default.setVehicle_toolOffset = tool.offset;

            rbtnToolRightPositive.IsChecked = false;
            rbtnLeftNegative.IsChecked = false;
            rbtnToolRightPositive.IsChecked = ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.IsChecked = ToolSettings.Default.setVehicle_toolOffset < 0;
        }

        private async void nudOverlap_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudOverlap))
            {
                if (!(rbtnToolOverlap.IsChecked == true) && !(rbtnToolGap.IsChecked == true))
                    rbtnToolOverlap.IsChecked = true;

                if (rbtnToolOverlap.IsChecked == true)
                    tool.overlap = (double)N(nudOverlap).Value * ctx.InchOrCm2m;
                else
                    tool.overlap = (double)N(nudOverlap).Value * -ctx.InchOrCm2m;

                ToolSettings.Default.setVehicle_toolOverlap = tool.overlap;
            }

            rbtnToolOverlap.IsChecked = false;
            rbtnToolGap.IsChecked = false;
            rbtnToolOverlap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap < 0;
        }

        private void btnZeroOverlap_Click(object sender, RoutedEventArgs e)
        {
            N(nudOverlap).Value = 0;
            rbtnToolOverlap.IsChecked = false;
            rbtnToolGap.IsChecked = false;

            tool.overlap = 0;
            ToolSettings.Default.setVehicle_toolOverlap = tool.overlap;
        }

        // Shared by rbtnToolOverlap + rbtnToolGap (Click).
        private void rbtnToolOverlap_Click(object sender, RoutedEventArgs e)
        {
            if (rbtnToolOverlap.IsChecked == true)
                tool.overlap = (double)N(nudOverlap).Value * ctx.InchOrCm2m;
            else
                tool.overlap = (double)N(nudOverlap).Value * -ctx.InchOrCm2m;
            ToolSettings.Default.setVehicle_toolOverlap = tool.overlap;
            ToolSettings.Default.Save();

            rbtnToolOverlap.IsChecked = false;
            rbtnToolGap.IsChecked = false;
            rbtnToolOverlap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.IsChecked = ToolSettings.Default.setVehicle_toolOverlap < 0;
        }

        // ---- Pivot distance sub-tab (ConfigTool.Designer.cs 458-517) ----
        private void tabToolPivot_Enter()
        {
            N(nudTrailingToolToPivotLength).Value = (decimal)(Math.Abs(ToolSettings.Default.setTool_trailingToolToPivotLength) * ctx.M2InchOrCm);

            rbtnPivotBehindPos.IsChecked = false;
            rbtnPivotAheadNeg.IsChecked = false;
            rbtnPivotBehindPos.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength < 0;

            LabelCm.Text = ctx.UnitsInCm;
        }

        private void tabToolPivot_Leave()
        {
            ToolSettings.Default.Save();
        }

        private void btnPivotOffsetZero_Click(object sender, RoutedEventArgs e)
        {
            N(nudTrailingToolToPivotLength).Value = 0;
            rbtnPivotBehindPos.IsChecked = false;
            rbtnPivotAheadNeg.IsChecked = false;

            tool.trailingToolToPivotLength = 0;
            ToolSettings.Default.setTool_trailingToolToPivotLength = tool.trailingToolToPivotLength;
        }

        // Shared by rbtnPivotBehindPos + rbtnPivotAheadNeg (Click).
        private void rbtnPivotBehindPos_Click(object sender, RoutedEventArgs e)
        {
            if (rbtnPivotBehindPos.IsChecked == true)
                tool.trailingToolToPivotLength = (double)N(nudTrailingToolToPivotLength).Value * ctx.InchOrCm2m;
            else
                tool.trailingToolToPivotLength = (double)N(nudTrailingToolToPivotLength).Value * -ctx.InchOrCm2m;
            ToolSettings.Default.setTool_trailingToolToPivotLength = tool.trailingToolToPivotLength;

            rbtnPivotBehindPos.IsChecked = false;
            rbtnPivotAheadNeg.IsChecked = false;
            rbtnPivotBehindPos.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength < 0;
        }

        private async void nudTrailingToolToPivotLength_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTrailingToolToPivotLength))
            {
                if (rbtnPivotBehindPos.IsChecked == true)
                    tool.trailingToolToPivotLength = (double)N(nudTrailingToolToPivotLength).Value * ctx.InchOrCm2m;
                else
                    tool.trailingToolToPivotLength = (double)N(nudTrailingToolToPivotLength).Value * -ctx.InchOrCm2m;

                ToolSettings.Default.setTool_trailingToolToPivotLength = tool.trailingToolToPivotLength;
            }

            rbtnPivotBehindPos.IsChecked = false;
            rbtnPivotAheadNeg.IsChecked = false;
            rbtnPivotBehindPos.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.IsChecked = ToolSettings.Default.setTool_trailingToolToPivotLength < 0;
        }

        // ---- Sections sub-tab (ConfigTool.Designer.cs 524-1246) ----
        private void tabTSections_Enter()
        {
            if (ctx.IsJobStarted)
            {
                if (ctx.AutoBtnState == btnStates.Auto)
                    ctx.SectionMasterAutoPerformClick();

                if (ctx.ManualBtnState == btnStates.On)
                    ctx.SectionMasterManualPerformClick();
            }

            if (tool.isSectionsNotZones)
            {
                // [XPLAT] reset section-master buttons (state Off + off bitmaps live on the main window).
                ctx.ResetSectionMasterButtons();

                // Update the button colors and text
                ctx.AllSectionsAndButtonsToState(ctx.AutoBtnState);

                // enable disable manual buttons
                ctx.LineUpIndividualSectionBtns();

                N(nudDefaultSectionWidth).Decimals = 0;
            }
            else
            {
                // turn section buttons all OFF
                ctx.AllZonesAndButtonsToState(btnStates.Off);

                ctx.LineUpAllZoneButtons();

                N(nudDefaultSectionWidth).Decimals = 1;
            }

            cboxIsUnique.IsChecked = !tool.isSectionsNotZones;

            cboxSectionBoundaryControl.IsChecked = ToolSettings.Default.setTool_isSectionOffWhenOut;
            // [XPLAT] cboxSectionBoundaryControl.BackgroundImage swap omitted — XAML :checked style handles it.

            if (ctx.IsMetric)
            {
                N(nudCutoffSpeed).Min = 0;
                N(nudCutoffSpeed).Max = 30;
                N(nudCutoffSpeed).Value = (decimal)ToolSettings.Default.setVehicle_slowSpeedCutoff;
                lblTurnOffBelowUnits.Text = "Km/H";
            }
            else
            {
                N(nudCutoffSpeed).Min = 0;
                N(nudCutoffSpeed).Max = (decimal)Speed.KmhToMph(30);
                N(nudCutoffSpeed).Value = (decimal)Speed.KmhToMph(ToolSettings.Default.setVehicle_slowSpeedCutoff);
                lblTurnOffBelowUnits.Text = "MPH";
            }

            if (cboxIsUnique.IsChecked == true)
            {
                // [XPLAT] cboxIsUnique.BackgroundImage (Symmetric) swap omitted — XAML :checked style handles it.
                cboxNumberOfZones.IsVisible = labelZonesBox.IsVisible = true;
            }
            else
            {
                // [XPLAT] cboxIsUnique.BackgroundImage (Asymmetric) swap omitted — XAML :checked style handles it.
                cboxNumberOfZones.IsVisible = labelZonesBox.IsVisible = false;
            }

            N(nudNumberOfSections).Max = ctx.MaxSections;

            // [XPLAT] reset section-master buttons (state Off + off bitmaps live on the main window).
            ctx.ResetSectionMasterButtons();

            N(nudMinCoverage).Value = (decimal)ToolSettings.Default.setVehicle_minCoverage;

            if (tool.isSectionsNotZones)
            {
                // Update the button colors and text
                ctx.AllSectionsAndButtonsToState(btnStates.Off);

                // enable disable manual buttons
                ctx.LineUpIndividualSectionBtns();

                numberOfSections = ToolSettings.Default.setVehicle_numSections;

                _suppressCombo = true;
                cboxNumSections.SelectedIndex = numberOfSections - 1;
                _suppressCombo = false;

                defaultSectionWidth = ToolSettings.Default.setTool_defaultSectionWidth;
                N(nudDefaultSectionWidth).Value = (int)(defaultSectionWidth * ctx.M2InchOrCm);

                panelSymmetricSections.IsVisible = false;

                nudNumberOfSections.IsVisible = false;
                cboxNumSections.IsVisible = true;

                N(nudSection01).Value = Math.Abs((ToolSettings.Default.setSection_position2 - ToolSettings.Default.setSection_position1) * (decimal)ctx.M2InchOrCm);
                N(nudSection02).Value = Math.Abs((ToolSettings.Default.setSection_position3 - ToolSettings.Default.setSection_position2) * (decimal)ctx.M2InchOrCm);
                N(nudSection03).Value = Math.Abs((ToolSettings.Default.setSection_position4 - ToolSettings.Default.setSection_position3) * (decimal)ctx.M2InchOrCm);
                N(nudSection04).Value = Math.Abs((ToolSettings.Default.setSection_position5 - ToolSettings.Default.setSection_position4) * (decimal)ctx.M2InchOrCm);
                N(nudSection05).Value = Math.Abs((ToolSettings.Default.setSection_position6 - ToolSettings.Default.setSection_position5) * (decimal)ctx.M2InchOrCm);
                N(nudSection06).Value = Math.Abs((ToolSettings.Default.setSection_position7 - ToolSettings.Default.setSection_position6) * (decimal)ctx.M2InchOrCm);
                N(nudSection07).Value = Math.Abs((ToolSettings.Default.setSection_position8 - ToolSettings.Default.setSection_position7) * (decimal)ctx.M2InchOrCm);
                N(nudSection08).Value = Math.Abs((ToolSettings.Default.setSection_position9 - ToolSettings.Default.setSection_position8) * (decimal)ctx.M2InchOrCm);
                N(nudSection09).Value = Math.Abs((ToolSettings.Default.setSection_position10 - ToolSettings.Default.setSection_position9) * (decimal)ctx.M2InchOrCm);
                N(nudSection10).Value = Math.Abs((ToolSettings.Default.setSection_position11 - ToolSettings.Default.setSection_position10) * (decimal)ctx.M2InchOrCm);
                N(nudSection11).Value = Math.Abs((ToolSettings.Default.setSection_position12 - ToolSettings.Default.setSection_position11) * (decimal)ctx.M2InchOrCm);
                N(nudSection12).Value = Math.Abs((ToolSettings.Default.setSection_position13 - ToolSettings.Default.setSection_position12) * (decimal)ctx.M2InchOrCm);
                N(nudSection13).Value = Math.Abs((ToolSettings.Default.setSection_position14 - ToolSettings.Default.setSection_position13) * (decimal)ctx.M2InchOrCm);
                N(nudSection14).Value = Math.Abs((ToolSettings.Default.setSection_position15 - ToolSettings.Default.setSection_position14) * (decimal)ctx.M2InchOrCm);
                N(nudSection15).Value = Math.Abs((ToolSettings.Default.setSection_position16 - ToolSettings.Default.setSection_position15) * (decimal)ctx.M2InchOrCm);
                N(nudSection16).Value = Math.Abs((ToolSettings.Default.setSection_position17 - ToolSettings.Default.setSection_position16) * (decimal)ctx.M2InchOrCm);

                // based on number of sections and values update the page before displaying
                UpdateSpinners();
            }
            else
            {
                // turn section buttons all OFF
                ctx.AllZonesAndButtonsToState(btnStates.Off);

                cboxNumSections.IsVisible = false;

                panelSymmetricSections.IsVisible = true;
                nudNumberOfSections.IsVisible = true;

                numberOfSections = ToolSettings.Default.setTool_numSectionsMulti;
                N(nudNumberOfSections).Value = numberOfSections;

                defaultSectionWidth = ToolSettings.Default.setTool_sectionWidthMulti;
                N(nudDefaultSectionWidth).Value = (decimal)(Math.Round((defaultSectionWidth * ctx.M2InchOrCm), 1));

                SetNudZoneMinMax();

                N(nudZone1To).Value = tool.zoneRanges[1];
                N(nudZone2To).Value = tool.zoneRanges[2];
                N(nudZone3To).Value = tool.zoneRanges[3];
                N(nudZone4To).Value = tool.zoneRanges[4];
                N(nudZone5To).Value = tool.zoneRanges[5];
                N(nudZone6To).Value = tool.zoneRanges[6];
                N(nudZone7To).Value = tool.zoneRanges[7];
                N(nudZone8To).Value = tool.zoneRanges[8];

                _suppressCombo = true;
                cboxNumberOfZones.SelectedIndex = tool.zones - 2;
                _suppressCombo = false;

                words = ToolSettings.Default.setTool_zones.Split(',');
                lblVehicleToolWidth.Text = Convert.ToString((int)(numberOfSections * defaultSectionWidth * 100 * ctx.Cm2CmOrIn), CultureInfo.InvariantCulture);

                ctx.LineUpAllZoneButtons();
                SetNudZoneVisibility();
            }

            label178.Text = ctx.UnitsInCm;
        }

        private void tabTSections_Leave()
        {
            if (tool.isSectionsNotZones)
            {
                // take the section widths and convert to meters and positions along tool.
                CalculateSectionPositions();

                ToolSettings.Default.setTool_isSectionOffWhenOut = cboxSectionBoundaryControl.IsChecked == true;
                tool.isSectionOffWhenOut = cboxSectionBoundaryControl.IsChecked == true;

                // save the values in each spinner for section position widths in settings
                ToolSettings.Default.setSection_position1 = sectionPositionArr[0];
                ToolSettings.Default.setSection_position2 = sectionPositionArr[1];
                ToolSettings.Default.setSection_position3 = sectionPositionArr[2];
                ToolSettings.Default.setSection_position4 = sectionPositionArr[3];
                ToolSettings.Default.setSection_position5 = sectionPositionArr[4];
                ToolSettings.Default.setSection_position6 = sectionPositionArr[5];
                ToolSettings.Default.setSection_position7 = sectionPositionArr[6];
                ToolSettings.Default.setSection_position8 = sectionPositionArr[7];
                ToolSettings.Default.setSection_position9 = sectionPositionArr[8];
                ToolSettings.Default.setSection_position10 = sectionPositionArr[9];
                ToolSettings.Default.setSection_position11 = sectionPositionArr[10];
                ToolSettings.Default.setSection_position12 = sectionPositionArr[11];
                ToolSettings.Default.setSection_position13 = sectionPositionArr[12];
                ToolSettings.Default.setSection_position14 = sectionPositionArr[13];
                ToolSettings.Default.setSection_position15 = sectionPositionArr[14];
                ToolSettings.Default.setSection_position16 = sectionPositionArr[15];
                ToolSettings.Default.setSection_position17 = sectionPositionArr[16];

                tool.numOfSections = numberOfSections;

                ToolSettings.Default.setVehicle_numSections = tool.numOfSections;

                // line up manual buttons based on # of sections
                ctx.LineUpIndividualSectionBtns();

                // update the sections to newly configured widths and positions in main
                ctx.SectionSetPosition();

                // update the widths of sections and tool width in main
                ctx.SectionCalcWidths();

                tram.IsTramOuterOrInner();

                ToolSettings.Default.setVehicle_toolWidth = tool.width;

                ctx.SendRelaySettingsToMachineModule();
            }
            else
            {
                tool.numOfSections = numberOfSections;
                ToolSettings.Default.setTool_numSectionsMulti = tool.numOfSections;

                tool.width = numberOfSections * defaultSectionWidth;
                ToolSettings.Default.setVehicle_toolWidth = tool.width;

                tram.IsTramOuterOrInner();

                ToolSettings.Default.Save();

                ctx.SectionCalcMulti();

                for (int i = 0; i < 9; i++)
                {
                    tool.zoneRanges[i] = 0;
                }

                tool.zoneRanges[0] = tool.zones;

                if (tool.zones == 2)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                }
                else if (tool.zones == 3)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                }
                else if (tool.zones == 4)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                    tool.zoneRanges[4] = (int)N(nudZone4To).Value;
                }
                else if (tool.zones == 5)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                    tool.zoneRanges[4] = (int)N(nudZone4To).Value;
                    tool.zoneRanges[5] = (int)N(nudZone5To).Value;
                }
                else if (tool.zones == 6)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                    tool.zoneRanges[4] = (int)N(nudZone4To).Value;
                    tool.zoneRanges[5] = (int)N(nudZone5To).Value;
                    tool.zoneRanges[6] = (int)N(nudZone6To).Value;
                }
                else if (tool.zones == 7)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                    tool.zoneRanges[4] = (int)N(nudZone4To).Value;
                    tool.zoneRanges[5] = (int)N(nudZone5To).Value;
                    tool.zoneRanges[6] = (int)N(nudZone6To).Value;
                    tool.zoneRanges[7] = (int)N(nudZone7To).Value;
                }
                else if (tool.zones == 8)
                {
                    tool.zoneRanges[1] = (int)N(nudZone1To).Value;
                    tool.zoneRanges[2] = (int)N(nudZone2To).Value;
                    tool.zoneRanges[3] = (int)N(nudZone3To).Value;
                    tool.zoneRanges[4] = (int)N(nudZone4To).Value;
                    tool.zoneRanges[5] = (int)N(nudZone5To).Value;
                    tool.zoneRanges[6] = (int)N(nudZone6To).Value;
                    tool.zoneRanges[7] = (int)N(nudZone7To).Value;
                    tool.zoneRanges[8] = (int)N(nudZone8To).Value;
                }

                string str = "";
                str = string.Join(",", tool.zoneRanges);
                ToolSettings.Default.setTool_zones = str;

                ctx.LineUpAllZoneButtons();
            }

            // no multi color zones
            if (tool.isSectionsNotZones)
                ToolSettings.Default.setColor_isMultiColorSections = tool.isMultiColoredSections = false;

            ToolSettings.Default.Save();
        }

        private async void nudZone1To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone1To)) { tool.zoneRanges[1] = (int)N(nudZone1To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone2To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone2To)) { tool.zoneRanges[2] = (int)N(nudZone2To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone3To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone3To)) { tool.zoneRanges[3] = (int)N(nudZone3To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone4To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone4To)) { tool.zoneRanges[4] = (int)N(nudZone4To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone5To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone5To)) { tool.zoneRanges[5] = (int)N(nudZone5To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone6To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone6To)) { tool.zoneRanges[6] = (int)N(nudZone6To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone7To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone7To)) { tool.zoneRanges[7] = (int)N(nudZone7To).Value; SetNudZoneVisibility(); }
        }

        private async void nudZone8To_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudZone8To)) { tool.zoneRanges[8] = (int)N(nudZone8To).Value; SetNudZoneVisibility(); }
        }

        private void cboxNumberOfZones_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCombo) return;

            if ((cboxNumberOfZones.SelectedIndex + 2) > (int)N(nudNumberOfSections).Value)
            {
                ShowYesMessage("You can't have more zones then sections");
                cboxNumberOfZones.SelectedIndex = tool.zones - 2;

                return;
            }

            tool.zones = cboxNumberOfZones.SelectedIndex + 2;

            SetNudZoneMinMax();
            FillZoneNudsWithDefaultValues();
            SetNudZoneVisibility();
        }

        private void FillZoneNudsWithDefaultValues()
        {
            N(nudZone1To).Value = 0;
            N(nudZone2To).Value = 0;
            N(nudZone3To).Value = 0;
            N(nudZone4To).Value = 0;
            N(nudZone5To).Value = 0;
            N(nudZone6To).Value = 0;
            N(nudZone7To).Value = 0;
            N(nudZone8To).Value = 0;

            if (tool.zones != 0)
            {
                int defa = numberOfSections / tool.zones;
                if (tool.zones == 2)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value = numberOfSections;
                }
                else if (tool.zones == 3)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value = numberOfSections;
                }
                else if (tool.zones == 4)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value += 3 * defa;
                    N(nudZone4To).Value = numberOfSections;
                }
                else if (tool.zones == 5)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value += 3 * defa;
                    N(nudZone4To).Value += 4 * defa;
                    N(nudZone5To).Value = numberOfSections;
                }
                else if (tool.zones == 6)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value += 3 * defa;
                    N(nudZone4To).Value += 4 * defa;
                    N(nudZone5To).Value += 5 * defa;
                    N(nudZone6To).Value = numberOfSections;
                }
                else if (tool.zones == 7)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value += 3 * defa;
                    N(nudZone4To).Value += 4 * defa;
                    N(nudZone5To).Value += 5 * defa;
                    N(nudZone6To).Value += 6 * defa;
                    N(nudZone7To).Value = numberOfSections;
                }
                else if (tool.zones == 8)
                {
                    N(nudZone1To).Value += defa;
                    N(nudZone2To).Value += 2 * defa;
                    N(nudZone3To).Value += 3 * defa;
                    N(nudZone4To).Value += 4 * defa;
                    N(nudZone5To).Value += 5 * defa;
                    N(nudZone6To).Value += 6 * defa;
                    N(nudZone7To).Value += 7 * defa;
                    N(nudZone8To).Value = numberOfSections;
                }
            }
        }

        private void SetNudZoneMinMax()
        {
            N(nudZone1To).Max = numberOfSections;
            N(nudZone2To).Max = numberOfSections;
            N(nudZone3To).Max = numberOfSections;
            N(nudZone4To).Max = numberOfSections;
            N(nudZone5To).Max = numberOfSections;
            N(nudZone6To).Max = numberOfSections;
            N(nudZone7To).Max = numberOfSections;
            N(nudZone8To).Max = numberOfSections;
        }

        private void SetNudZoneVisibility()
        {
            nudZone1To.IsVisible = false;
            nudZone2To.IsVisible = false;
            nudZone3To.IsVisible = false;
            nudZone4To.IsVisible = false;
            nudZone5To.IsVisible = false;
            nudZone6To.IsVisible = false;
            nudZone7To.IsVisible = false;
            nudZone8To.IsVisible = false;

            nudZone1To.IsEnabled = true;
            nudZone2To.IsEnabled = true;
            nudZone3To.IsEnabled = true;
            nudZone4To.IsEnabled = true;
            nudZone5To.IsEnabled = true;
            nudZone6To.IsEnabled = true;
            nudZone7To.IsEnabled = true;
            nudZone8To.IsEnabled = true;

            lblZoneStart1.IsVisible = false;
            lblZoneStart2.IsVisible = false;
            lblZoneStart3.IsVisible = false;
            lblZoneStart4.IsVisible = false;
            lblZoneStart5.IsVisible = false;
            lblZoneStart6.IsVisible = false;
            lblZoneStart7.IsVisible = false;
            lblZoneStart8.IsVisible = false;

            if (tool.zones > 1)
            {
                nudZone2To.IsVisible = true;
                nudZone1To.IsVisible = true;
                lblZoneStart1.IsVisible = true;
                lblZoneStart2.IsVisible = true;
                lblZoneStart2.Text = (N(nudZone1To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 2) nudZone2To.IsEnabled = false;
            }

            if (tool.zones > 2)
            {
                nudZone3To.IsVisible = true;
                lblZoneStart3.IsVisible = true;
                lblZoneStart3.Text = (N(nudZone2To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 3) nudZone3To.IsEnabled = false;
            }

            if (tool.zones > 3)
            {
                nudZone4To.IsVisible = true;
                lblZoneStart4.IsVisible = true;
                lblZoneStart4.Text = (N(nudZone3To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 4) nudZone4To.IsEnabled = false;
            }

            if (tool.zones > 4)
            {
                nudZone5To.IsVisible = true;
                lblZoneStart5.IsVisible = true;
                lblZoneStart5.Text = (N(nudZone4To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 5) nudZone5To.IsEnabled = false;
            }

            if (tool.zones > 5)
            {
                nudZone6To.IsVisible = true;
                lblZoneStart6.IsVisible = true;
                lblZoneStart6.Text = (N(nudZone5To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 6) nudZone6To.IsEnabled = false;
            }

            if (tool.zones > 6)
            {
                nudZone7To.IsVisible = true;
                lblZoneStart7.IsVisible = true;
                lblZoneStart7.Text = (N(nudZone6To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 7) nudZone7To.IsEnabled = false;
            }

            if (tool.zones > 7)
            {
                nudZone8To.IsVisible = true;
                lblZoneStart8.IsVisible = true;
                lblZoneStart8.Text = (N(nudZone7To).Value + 1).ToString(CultureInfo.InvariantCulture);
                if (tool.zones == 8) nudZone8To.IsEnabled = false;
            }
        }

        private void cboxSectionBoundaryControl_Click(object sender, RoutedEventArgs e)
        {
            ToolSettings.Default.setTool_isSectionOffWhenOut = !ToolSettings.Default.setTool_isSectionOffWhenOut;
            ToolSettings.Default.Save();

            cboxSectionBoundaryControl.IsChecked = ToolSettings.Default.setTool_isSectionOffWhenOut;
            // [XPLAT] BackgroundImage swap omitted — XAML :checked style handles it.
        }

        private void cboxIsUnique_Click(object sender, RoutedEventArgs e)
        {
            tool.isSectionsNotZones = !(cboxIsUnique.IsChecked == true);
            ToolSettings.Default.setTool_isSectionsNotZones = !(cboxIsUnique.IsChecked == true);
            tabTSections_Enter();
        }

        private async void nudNumberOfSections_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudNumberOfSections))
            {
                if ((int)N(nudNumberOfSections).Value < tool.zones)
                {
                    ShowYesMessage("You can't have more zones then sections");
                    N(nudNumberOfSections).Value = numberOfSections;
                    return;
                }
                numberOfSections = (int)N(nudNumberOfSections).Value;
                SetNudZoneMinMax();

                ToolSettings.Default.setTool_numSectionsMulti = numberOfSections;
                ToolSettings.Default.Save();

                lblVehicleToolWidth.Text = Convert.ToString((int)(numberOfSections * defaultSectionWidth * 100 * ctx.Cm2CmOrIn), CultureInfo.InvariantCulture);
                SectionFeetInchesTotalWidthLabelUpdate(ctx.IsMetric, tool.width);
                FillZoneNudsWithDefaultValues();
                SetNudZoneVisibility();
            }
        }

        private async void nudDefaultSectionWidth_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudDefaultSectionWidth))
            {
                defaultSectionWidth = (double)N(nudDefaultSectionWidth).Value * ctx.InchOrCm2m;

                if (tool.isSectionsNotZones)
                    ToolSettings.Default.setTool_defaultSectionWidth = defaultSectionWidth;
                else
                    ToolSettings.Default.setTool_sectionWidthMulti = defaultSectionWidth;

                ToolSettings.Default.Save();
            }
        }

        private void cboxNumSections_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCombo) return;

            if (tool.isSectionsNotZones)
            {
                numberOfSections = cboxNumSections.SelectedIndex + 1;

                decimal wide = N(nudDefaultSectionWidth).Value;

                if (ctx.IsMetric)
                {
                    if (numberOfSections * wide > 5000)
                    {
                        wide = 99;
                        ShowFormDialog("Too Wide", "Max 50 Meters", DialogSeverity.Error);
                        Log.EventWriter("Sections, Tool Set Too Wide");
                    }
                }
                else
                {
                    if (numberOfSections * wide > 1900)
                    {
                        wide = 19;
                        ShowFormDialog("Too Wide", "Max 164 Feet", DialogSeverity.Error);
                        Log.EventWriter("Sections, Tool Set Too Wide");
                    }
                }

                N(nudSection01).Value = wide;
                N(nudSection02).Value = wide;
                N(nudSection03).Value = wide;
                N(nudSection04).Value = wide;
                N(nudSection05).Value = wide;
                N(nudSection06).Value = wide;
                N(nudSection07).Value = wide;
                N(nudSection08).Value = wide;
                N(nudSection09).Value = wide;
                N(nudSection10).Value = wide;
                N(nudSection11).Value = wide;
                N(nudSection12).Value = wide;
                N(nudSection13).Value = wide;
                N(nudSection14).Value = wide;
                N(nudSection15).Value = wide;
                N(nudSection16).Value = wide;

                UpdateSpinners();

                // take the section widths and convert to meters and positions along tool.
                CalculateSectionPositions();
                // line up manual buttons based on # of sections
                ctx.LineUpIndividualSectionBtns();

                // update the sections to newly configured widths and positions in main
                ctx.SectionSetPosition();

                // update the widths of sections and tool width in main
                ctx.SectionCalcWidths();
            }
        }

        // Shared by nudSection01..nudSection16 (Click).
        private async void NudSection1_Click(object sender, RoutedEventArgs e)
        {
            await ShowKeypad((Button)sender);
            UpdateSpinners();
        }

        private async void nudCutoffSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudCutoffSpeed))
            {
                // Convert from MPH to km/h if imperial units are selected
                double speedKmh = ctx.IsMetric ? (double)N(nudCutoffSpeed).Value : Speed.MphToKmh((double)N(nudCutoffSpeed).Value);
                vehicle.slowSpeedCutoff = speedKmh;
                ToolSettings.Default.setVehicle_slowSpeedCutoff = speedKmh;
            }
        }

        private async void nudMinCoverage_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudMinCoverage))
            {
                tool.minCoverage = (int)N(nudMinCoverage).Value;
                ToolSettings.Default.setVehicle_minCoverage = tool.minCoverage;
            }
        }

        public void UpdateSpinners()
        {
            int i = numberOfSections;

            decimal toolWidth = 0;

            // [XPLAT] WinForms iterated tab1.TabPages[9].Controls filtering "nudSec*"; here we walk the
            // 16 named section nud Buttons in order (nudSection01..nudSection16).
            var secs = new[]
            {
                nudSection01, nudSection02, nudSection03, nudSection04, nudSection05, nudSection06,
                nudSection07, nudSection08, nudSection09, nudSection10, nudSection11, nudSection12,
                nudSection13, nudSection14, nudSection15, nudSection16
            };

            for (int idx = 0; idx < secs.Length; idx++)
            {
                int nudNum = idx + 1;
                var item2 = secs[idx];
                if (nudNum <= i)
                {
                    item2.IsEnabled = true;
                    item2.IsVisible = true;
                    toolWidth += N(item2).Value;

                    if (ctx.IsMetric)
                    {
                        if (toolWidth > 5000)
                        {
                            ShowFormDialog("Too Wide", "Set to 99, Max 50 Meters", DialogSeverity.Error);
                            Log.EventWriter("Sections, Tool Set Too Wide");
                            toolWidth = 0;
                            foreach (var s in secs) N(s).Value = 99;
                        }
                    }
                    else
                    {
                        if (toolWidth > 1900)
                        {
                            ShowFormDialog("Too Wide", "Set to 99, Max 164 Feet", DialogSeverity.Error);
                            Log.EventWriter("Sections, Tool Set Too Wide");
                            toolWidth = 0;
                            foreach (var s in secs) N(s).Value = 99;
                        }
                    }
                }
                else
                {
                    item2.IsEnabled = false;
                    item2.IsVisible = false;
                }
            }

            lblVehicleToolWidth.Text = Convert.ToString((int)toolWidth, CultureInfo.InvariantCulture);

            SectionFeetInchesTotalWidthLabelUpdate(ctx.IsMetric, tool.width);
        }

        // update tool width label at bottom of window
        private void SectionFeetInchesTotalWidthLabelUpdate(bool isMetric, double toolWidthInMeters)
        {
            lblInchesCm.Text = isMetric ? gStr.gsCentimeters : gStr.gsInches;
            lblFeetMeters.Text = isMetric ? gStr.gsMeters : "Feet";

            lblSecTotalWidth.Text = Distance.MediumDistanceString(isMetric, toolWidthInMeters);
            // [XPLAT] Parity with WinForms ConfigTool.Designer.cs, which propagated the formatted total
            // width into the summary tab via configSummaryControl.SetSummaryWidth(lblSecTotalWidth.Text).
            configSummaryControl.SetSummaryWidth(lblSecTotalWidth.Text);
        }

        // Convert section width to positions along toolbar
        private void CalculateSectionPositions()
        {
            int i = numberOfSections;

            // convert to meters spinner value
            sectionWidthArr[0] = N(nudSection01).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[1] = N(nudSection02).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[2] = N(nudSection03).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[3] = N(nudSection04).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[4] = N(nudSection05).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[5] = N(nudSection06).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[6] = N(nudSection07).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[7] = N(nudSection08).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[8] = N(nudSection09).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[9] = N(nudSection10).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[10] = N(nudSection11).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[11] = N(nudSection12).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[12] = N(nudSection13).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[13] = N(nudSection14).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[14] = N(nudSection15).Value * (decimal)ctx.InchOrCm2m;
            sectionWidthArr[15] = N(nudSection16).Value * (decimal)ctx.InchOrCm2m;

            // add up the set widths
            decimal setWidth = 0;
            for (int j = 0; j < i; j++)
            {
                setWidth += sectionWidthArr[j];
            }

            // leftmost position.
            setWidth *= -0.5M;

            sectionPositionArr[0] = setWidth;

            for (int j = 1; j < 17; j++)
            {
                if (j <= i) sectionPositionArr[j] = sectionPositionArr[j - 1] + sectionWidthArr[j - 1];
                else sectionPositionArr[j] = 0;
            }
        }

        // ---- Switch sub-tab (ConfigTool.Designer.cs 1392-1492) ----
        private void tabTSwitches_Enter()
        {
            // set accordingly
            chkSelectSteerSwitch.IsChecked = ctx.IsSteerWorkSwitchEnabled;
            chkSelectWorkSwitch.IsChecked = ctx.IsWorkSwitchEnabled;

            if (Settings.Default.setF_isWorkSwitchManualSections)
            {
                chkSetManualSections.IsChecked = true;
                chkSetAutoSections.IsChecked = false;
            }
            else
            {
                chkSetManualSections.IsChecked = false;
                chkSetAutoSections.IsChecked = true;
            }

            if (Settings.Default.setF_isSteerWorkSwitchManualSections)
            {
                chkSetManualSectionsSteer.IsChecked = true;
                chkSetAutoSectionsSteer.IsChecked = false;
            }
            else
            {
                chkSetManualSectionsSteer.IsChecked = false;
                chkSetAutoSectionsSteer.IsChecked = true;
            }

            chkSetManualSections.IsEnabled = chkSetAutoSections.IsEnabled = chkWorkSwActiveLow.IsEnabled = chkSelectWorkSwitch.IsChecked == true;
            chkSetManualSectionsSteer.IsEnabled = chkSetAutoSectionsSteer.IsEnabled = chkSelectSteerSwitch.IsChecked == true;

            chkWorkSwActiveLow.IsChecked = Settings.Default.setF_isWorkSwitchActiveLow;
            // [XPLAT] chkWorkSwActiveLow.Image (SwitchActiveClosed/Open) swap omitted — XAML :checked style handles it.
        }

        private void tabTSwitches_Leave()
        {
            // active low on work switch
            ctx.IsWorkSwitchActiveLow = Settings.Default.setF_isWorkSwitchActiveLow = chkWorkSwActiveLow.IsChecked == true;

            // is work switch enabled
            ctx.IsWorkSwitchEnabled = Settings.Default.setF_isWorkSwitchEnabled = chkSelectWorkSwitch.IsChecked == true;

            // Are auto or manual sections controlled.
            ctx.IsWorkSwitchManualSections = Settings.Default.setF_isWorkSwitchManualSections = chkSetManualSections.IsChecked == true;

            // Are auto or manual sections controlled for steer
            ctx.IsSteerWorkSwitchEnabled = ToolSettings.Default.setF_isSteerWorkSwitchEnabled = chkSelectSteerSwitch.IsChecked == true;

            // does steer switch control manual or auto sections
            ctx.IsSteerWorkSwitchManualSections = Settings.Default.setF_isSteerWorkSwitchManualSections = chkSetManualSectionsSteer.IsChecked == true;

            if (!ctx.IsSteerWorkSwitchEnabled && !ctx.IsWorkSwitchEnabled)
                ctx.IsRemoteWorkSystemOn = Settings.Default.setF_isRemoteWorkSystemOn = false;
            else
                ctx.IsRemoteWorkSystemOn = Settings.Default.setF_isRemoteWorkSystemOn = true;
            // save
            Settings.Default.Save();
        }

        private void chkSelectWorkSwitch_Click(object sender, RoutedEventArgs e)
        {
            chkSetManualSections.IsEnabled = chkSetAutoSections.IsEnabled = chkWorkSwActiveLow.IsEnabled = chkSelectWorkSwitch.IsChecked == true;
        }

        private void chkSelectSteerSwitch_Click(object sender, RoutedEventArgs e)
        {
            chkSetManualSectionsSteer.IsEnabled = chkSetAutoSectionsSteer.IsEnabled = chkSelectSteerSwitch.IsChecked == true;
        }

        private void chkSetManualSections_Click(object sender, RoutedEventArgs e)
        {
            chkSetAutoSections.IsChecked = false;
            chkSetManualSections.IsChecked = true;
        }

        private void chkSetAutoSections_Click(object sender, RoutedEventArgs e)
        {
            chkSetManualSections.IsChecked = false;
            chkSetAutoSections.IsChecked = true;
        }

        private void chkSetAutoSectionsSteer_Click(object sender, RoutedEventArgs e)
        {
            chkSetManualSectionsSteer.IsChecked = false;
            chkSetAutoSectionsSteer.IsChecked = true;
        }

        private void chkSetManualSectionsSteer_Click(object sender, RoutedEventArgs e)
        {
            chkSetAutoSectionsSteer.IsChecked = false;
            chkSetManualSectionsSteer.IsChecked = true;
        }

        private void WireToolEvents()
        {
            // [XPLAT] Populate the two section/zone ComboBoxes (WinForms FormConfig.Designer.cs Items.AddRange):
            // cboxNumSections "1".."16" (index+1 == count); cboxNumberOfZones "2".."8" (index+2 == zones).
            cboxNumSections.ItemsSource = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16" };
            cboxNumberOfZones.ItemsSource = new[] { "2", "3", "4", "5", "6", "7", "8" };
            cboxNumSections.SelectionChanged += cboxNumSections_SelectedIndexChanged;
            cboxNumberOfZones.SelectionChanged += cboxNumberOfZones_SelectedIndexChanged;

            // Hitch
            nudDrawbarLength.Click += nudDrawbarLength_Click;
            nudTankHitch.Click += nudTankHitch_Click;
            nudTrailingHitchLength.Click += nudTrailingHitchLength_Click;

            // Settings
            nudLookAhead.Click += nudLookAhead_Click;
            nudLookAheadOff.Click += nudLookAheadOff_Click;
            nudTurnOffDelay.Click += nudTurnOffDelay_Click;

            // Offset
            nudOffset.Click += nudOffset_Click;
            btnZeroToolOffset.Click += btnZeroToolOffset_Click;
            rbtnToolRightPositive.Click += rbtnToolRightPositive_Click;
            rbtnLeftNegative.Click += rbtnToolRightPositive_Click;
            nudOverlap.Click += nudOverlap_Click;
            btnZeroOverlap.Click += btnZeroOverlap_Click;
            rbtnToolOverlap.Click += rbtnToolOverlap_Click;
            rbtnToolGap.Click += rbtnToolOverlap_Click;

            // Pivot
            nudTrailingToolToPivotLength.Click += nudTrailingToolToPivotLength_Click;
            btnPivotOffsetZero.Click += btnPivotOffsetZero_Click;
            rbtnPivotBehindPos.Click += rbtnPivotBehindPos_Click;
            rbtnPivotAheadNeg.Click += rbtnPivotBehindPos_Click;

            // Sections
            nudCutoffSpeed.Click += nudCutoffSpeed_Click;
            nudDefaultSectionWidth.Click += nudDefaultSectionWidth_Click;
            nudMinCoverage.Click += nudMinCoverage_Click;
            nudNumberOfSections.Click += nudNumberOfSections_Click;
            cboxSectionBoundaryControl.Click += cboxSectionBoundaryControl_Click;
            cboxIsUnique.Click += cboxIsUnique_Click;

            nudSection01.Click += NudSection1_Click;
            nudSection02.Click += NudSection1_Click;
            nudSection03.Click += NudSection1_Click;
            nudSection04.Click += NudSection1_Click;
            nudSection05.Click += NudSection1_Click;
            nudSection06.Click += NudSection1_Click;
            nudSection07.Click += NudSection1_Click;
            nudSection08.Click += NudSection1_Click;
            nudSection09.Click += NudSection1_Click;
            nudSection10.Click += NudSection1_Click;
            nudSection11.Click += NudSection1_Click;
            nudSection12.Click += NudSection1_Click;
            nudSection13.Click += NudSection1_Click;
            nudSection14.Click += NudSection1_Click;
            nudSection15.Click += NudSection1_Click;
            nudSection16.Click += NudSection1_Click;

            nudZone1To.Click += nudZone1To_Click;
            nudZone2To.Click += nudZone2To_Click;
            nudZone3To.Click += nudZone3To_Click;
            nudZone4To.Click += nudZone4To_Click;
            nudZone5To.Click += nudZone5To_Click;
            nudZone6To.Click += nudZone6To_Click;
            nudZone7To.Click += nudZone7To_Click;
            nudZone8To.Click += nudZone8To_Click;

            // Switch (chkWorkSwActiveLow needs no code handler — XAML :checked style handles its image)
            chkSelectWorkSwitch.Click += chkSelectWorkSwitch_Click;
            chkSelectSteerSwitch.Click += chkSelectSteerSwitch_Click;
            chkSetManualSections.Click += chkSetManualSections_Click;
            chkSetAutoSections.Click += chkSetAutoSections_Click;
            chkSetAutoSectionsSteer.Click += chkSetAutoSectionsSteer_Click;
            chkSetManualSectionsSteer.Click += chkSetManualSectionsSteer_Click;
        }

        #endregion

        // =====================================================================================
        #region Data — Heading  // [XPLAT] from ConfigData.Designer.cs (region Heading)
        // =====================================================================================

        // Port of tabDHeading_Enter (ConfigData.Designer.cs 14-79). Heading source, step-distance,
        // dual-antenna offsets, IMU/GPS fusion, RTK alarms, reverse, and auto-switch dual-fix.
        private void tabDHeading_Enter()
        {
            // heading source radios (rbtnHeadingFix "Fix" / rbtnHeadingHDT "Dual")
            if (VehicleSettings.Default.setGPS_headingFromWhichSource == "Fix") rbtnHeadingFix.IsChecked = true;
            else if (VehicleSettings.Default.setGPS_headingFromWhichSource == "Dual") rbtnHeadingHDT.IsChecked = true;

            if (rbtnHeadingHDT.IsChecked == true)
            {
                if (Settings.Default.setAutoSwitchDualFixOn)
                {
                    rbtnHeadingFix.IsEnabled = false;
                    labelGboxSingle.IsEnabled = true;
                    labelGboxDual.IsEnabled = true;
                }
                else
                {
                    labelGboxSingle.IsEnabled = false;
                    labelGboxDual.IsEnabled = true;
                }
            }
            else
            {
                labelGboxSingle.IsEnabled = true;
                labelGboxDual.IsEnabled = false;
            }

            cboxMinGPSStep.IsChecked = (Settings.Default.setF_minHeadingStepDistance == 1.0);
            UpdateStepDistanceUI();

            N(nudDualHeadingOffset).Value = (decimal)VehicleSettings.Default.setGPS_dualHeadingOffset;
            N(nudDualReverseDistance).Value = (decimal)VehicleSettings.Default.setGPS_dualReverseDetectionDistance;

            // [XPLAT] Avalonia Slider.Value is double; the WinForms scrollbar was integer-only, so the
            // (int) casts below mirror the original integer semantics (snap-to-int is configured in XAML).
            hsbarFusion.Value = (int)(VehicleSettings.Default.setIMU_fusionWeight2 * 500);
            lblFusion.Text = ((int)hsbarFusion.Value).ToString(CultureInfo.InvariantCulture);
            lblFusionIMU.Text = (100 - (int)hsbarFusion.Value).ToString(CultureInfo.InvariantCulture);

            cboxIsRTK.IsChecked = Settings.Default.setGPS_isRTK;
            cboxIsRTK_KillAutoSteer.IsChecked = Settings.Default.setGPS_isRTK_KillAutoSteer;

            N(nudFixJumpDistance).Value = Settings.Default.setGPS_jumpFixAlarmDistance;

            cboxIsReverseOn.IsChecked = Settings.Default.setIMU_isReverseOn;
            cboxIsAutoSwitchDualFixOn.IsChecked = Settings.Default.setAutoSwitchDualFixOn;
            UpdateAutoSwitchDualFixSpeedUI();

            if (ahrs.ImuHeading != 99999)
            {
                hsbarFusion.IsEnabled = true;
            }
            else
            {
                hsbarFusion.IsEnabled = false;
            }

            if (cboxIsAutoSwitchDualFixOn.IsChecked == true)
            {
                hsbarFusion.IsEnabled = true;
            }
        }

        // Port of cboxMinGPSStep_CheckedChanged (80-84).
        private void cboxMinGPSStep_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // draw labels + update settings
            UpdateStepDistanceUI();
        }

        // Port of UpdateStepDistanceUI (86-106). cboxMinGPSStep.Text -> CheckBox.Content (real label,
        // not an image); the cm/in strings stay verbatim for parity.
        private void UpdateStepDistanceUI()
        {
            if (cboxMinGPSStep.IsChecked == true)
            {
                Settings.Default.setF_minHeadingStepDistance = 1.0;
                VehicleSettings.Default.setGPS_minimumStepLimit = 0.1;

                cboxMinGPSStep.Content = ctx.IsMetric ? "10 cm" : "3.93 in";
                lblHeadingDistance.Text = ctx.IsMetric ? "100 cm" : "39.3 in";
            }
            else
            {
                Settings.Default.setF_minHeadingStepDistance = 0.5;
                VehicleSettings.Default.setGPS_minimumStepLimit = 0.05;

                cboxMinGPSStep.Content = ctx.IsMetric ? "5 cm" : "1.96 in";
                lblHeadingDistance.Text = ctx.IsMetric ? "50 cm" : "19.68 in";
            }

            ctx.IsFirstHeadingSet = false;
        }

        // Port of tabDHeading_Leave (108-121). WinForms chained assigns (settings = mf.x = cbox.Checked)
        // are split into a local + two writes; only Settings.Default is saved here (VehicleSettings is
        // saved on close / by other tabs), matching the source exactly.
        private void tabDHeading_Leave()
        {
            VehicleSettings.Default.setIMU_fusionWeight2 = (int)hsbarFusion.Value * 0.002;
            ahrs.FusionWeight = (int)hsbarFusion.Value * 0.002;

            bool rtk = cboxIsRTK.IsChecked == true;
            Settings.Default.setGPS_isRTK = rtk;
            ctx.IsRtkAlarmOn = rtk;

            bool rev = cboxIsReverseOn.IsChecked == true;
            Settings.Default.setIMU_isReverseOn = rev;
            ahrs.IsReverseOn = rev;

            bool autoDual = cboxIsAutoSwitchDualFixOn.IsChecked == true;
            Settings.Default.setAutoSwitchDualFixOn = autoDual;
            ahrs.AutoSwitchDualFixOn = autoDual;

            bool kill = cboxIsRTK_KillAutoSteer.IsChecked == true;
            Settings.Default.setGPS_isRTK_KillAutoSteer = kill;
            ctx.IsRtkKillAutosteer = kill;

            UpdateAutoSwitchDualFixSpeedUI();
            Settings.Default.Save();
        }

        // Port of rbtnHeadingFix_CheckedChanged (122-138). SHARED by rbtnHeadingFix + rbtnHeadingHDT.
        // [XPLAT] WinForms read the checked radio's .Text via headingGroupBox.Controls.OfType<RadioButton>();
        // Avalonia determines the source string from which radio is checked ("Dual" for HDT, else "Fix").
        private void rbtnHeadingFix_CheckedChanged(object sender, RoutedEventArgs e)
        {
            string checkedText = (rbtnHeadingHDT.IsChecked == true) ? "Dual" : "Fix";
            VehicleSettings.Default.setGPS_headingFromWhichSource = checkedText;
            ctx.HeadingFromSource = checkedText;

            if (rbtnHeadingHDT.IsChecked == true)
            {
                SetAutoSwitchDualFixPanelOptions();
            }
            else
            {
                rbtnHeadingFix.IsEnabled = true;
                labelGboxSingle.IsEnabled = true;
                labelGboxDual.IsEnabled = false;
            }
        }

        // Port of nudFixJumpDistance_Click (140-147).
        private async void nudFixJumpDistance_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudFixJumpDistance))
            {
                Settings.Default.setGPS_jumpFixAlarmDistance = (int)N(nudFixJumpDistance).Value;
            }
        }

        // Port of nudDualHeadingOffset_Click (149-156). mf.pn.headingTrueDualOffset -> ctx.HeadingTrueDualOffset.
        private async void nudDualHeadingOffset_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudDualHeadingOffset))
            {
                VehicleSettings.Default.setGPS_dualHeadingOffset = (double)N(nudDualHeadingOffset).Value;
                ctx.HeadingTrueDualOffset = VehicleSettings.Default.setGPS_dualHeadingOffset;
            }
        }

        // Port of nudDualReverseDistance_Click (158-165). mf.dualReverseDetectionDistance -> ctx.DualReverseDetectionDistance.
        private async void nudDualReverseDistance_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudDualReverseDistance))
            {
                VehicleSettings.Default.setGPS_dualReverseDetectionDistance = (double)N(nudDualReverseDistance).Value;
                ctx.DualReverseDetectionDistance = VehicleSettings.Default.setGPS_dualReverseDetectionDistance;
            }
        }

        // Port of hsbarFusion_ValueChanged (175-181).
        private void hsbarFusion_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            int v = (int)hsbarFusion.Value;
            lblFusion.Text = v.ToString(CultureInfo.InvariantCulture) + "%";
            lblFusionIMU.Text = (100 - v).ToString(CultureInfo.InvariantCulture) + "%";

            ahrs.FusionWeight = v * 0.002;
        }

        // Port of cboxIsAutoSwitchDualFixOn_CheckedChanged (183-186).
        private void cboxIsAutoSwitchDualFixOn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            SetAutoSwitchDualFixPanelOptions();
        }

        // Port of nudAutoSwitchDualFixSpeed_Click (188-202). Value is stored internally as km/h.
        private async void nudAutoSwitchDualFixSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudAutoSwitchDualFixSpeed))
            {
                // Always convert back to km/h
                double input = (double)N(nudAutoSwitchDualFixSpeed).Value;
                double kmh = ctx.IsMetric ? input : Speed.MphToKmh(input);

                Settings.Default.setAutoSwitchDualFixSpeed = kmh;
                ahrs.AutoSwitchDualFixSpeed = kmh;

                // UI resync
                UpdateAutoSwitchDualFixSpeedUI();
            }
        }

        // Port of UpdateAutoSwitchDualFixSpeedUI (204-242). Internally stored as km/h; displays in the
        // current unit system. [XPLAT] WinForms also set nud.Increment = 0.1M — NudState has no Increment
        // analog (value entry is via the FormNumeric keypad), so Increment is intentionally omitted.
        private void UpdateAutoSwitchDualFixSpeedUI()
        {
            // Always stored internally as km/h
            double speedKmh = Settings.Default.setAutoSwitchDualFixSpeed;
            double minKmh = 1.0;
            double maxKmh = 10.0;

            // Convert both value and limits if needed
            double displayValue, displayMin, displayMax;
            string unitText;

            if (ctx.IsMetric)
            {
                displayValue = speedKmh;
                displayMin = minKmh;
                displayMax = maxKmh;
                unitText = "(km/h)";
            }
            else
            {
                displayValue = Speed.KmhToMph(speedKmh);
                displayMin = Speed.KmhToMph(minKmh);
                displayMax = Speed.KmhToMph(maxKmh);
                unitText = "(mph)";
            }

            // Clamp within the converted range to prevent out-of-range
            displayValue = Math.Max(displayMin, Math.Min(displayValue, displayMax));

            // Apply limits before setting Value
            N(nudAutoSwitchDualFixSpeed).Decimals = 1;
            N(nudAutoSwitchDualFixSpeed).Min = (decimal)displayMin;
            N(nudAutoSwitchDualFixSpeed).Max = (decimal)displayMax;
            N(nudAutoSwitchDualFixSpeed).Value = (decimal)displayValue;

            // Update label
            labelAutoSwitchDualFixSpeed.Text = $"{gStr.gsAutoSwitchDualFixSpeed} {unitText}";
        }

        // Port of SetAutoSwitchDualFixPanelOptions (245-259).
        private void SetAutoSwitchDualFixPanelOptions()
        {
            if (cboxIsAutoSwitchDualFixOn.IsChecked == true)
            {
                rbtnHeadingFix.IsEnabled = false;
                labelGboxSingle.IsEnabled = true;
                labelGboxDual.IsEnabled = true;
            }
            else
            {
                rbtnHeadingFix.IsEnabled = true;
                labelGboxSingle.IsEnabled = false;
                labelGboxDual.IsEnabled = true;
            }
        }

        #endregion

        // =====================================================================================
        #region Data — Roll  // [XPLAT] from ConfigData.Designer.cs (region Roll)
        // =====================================================================================

        // Port of tabDRoll_Enter (289-295).
        private void tabDRoll_Enter()
        {
            // Roll
            lblRollZeroOffset.Text = ((double)VehicleSettings.Default.setIMU_rollZero).ToString("N2", CultureInfo.InvariantCulture);
            hsbarRollFilter.Value = (int)(VehicleSettings.Default.setIMU_rollFilter * 100);
            cboxDataInvertRoll.IsChecked = VehicleSettings.Default.setIMU_invertRoll;
        }

        // Port of tabDRoll_Leave (297-307).
        private void tabDRoll_Leave()
        {
            VehicleSettings.Default.setIMU_rollFilter = (int)hsbarRollFilter.Value * 0.01;
            VehicleSettings.Default.setIMU_rollZero = ahrs.RollZero;
            VehicleSettings.Default.setIMU_invertRoll = cboxDataInvertRoll.IsChecked == true;

            ahrs.RollFilter = VehicleSettings.Default.setIMU_rollFilter;
            ahrs.IsRollInvert = VehicleSettings.Default.setIMU_invertRoll;

            VehicleSettings.Default.Save();
        }

        // Port of hsbarRollFilter_ValueChanged (309-312).
        private void hsbarRollFilter_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            lblRollFilterPercent.Text = ((int)hsbarRollFilter.Value).ToString(CultureInfo.InvariantCulture);
        }

        // Port of btnRollOffsetDown_Click (314-325). Guarded by the imuRoll 88888 "no data" sentinel.
        private void btnRollOffsetDown_Click(object sender, RoutedEventArgs e)
        {
            if (ahrs.ImuRoll != 88888)
            {
                ahrs.RollZero -= 0.1;
                lblRollZeroOffset.Text = ahrs.RollZero.ToString("N2", CultureInfo.InvariantCulture);
            }
            else
            {
                lblRollZeroOffset.Text = "***";
            }
        }

        // Port of btnRollOffsetUp_Click (327-338).
        private void btnRollOffsetUp_Click(object sender, RoutedEventArgs e)
        {
            if (ahrs.ImuRoll != 88888)
            {
                ahrs.RollZero += 0.1;
                lblRollZeroOffset.Text = ahrs.RollZero.ToString("N2", CultureInfo.InvariantCulture);
            }
            else
            {
                lblRollZeroOffset.Text = "***";
            }
        }

        // Port of btnZeroRoll_Click (339-352).
        private void btnZeroRoll_Click(object sender, RoutedEventArgs e)
        {
            if (ahrs.ImuRoll != 88888)
            {
                ahrs.ImuRoll += ahrs.RollZero;
                ahrs.RollZero = ahrs.ImuRoll;
                lblRollZeroOffset.Text = ahrs.RollZero.ToString("N2", CultureInfo.InvariantCulture);
                Log.EventWriter("Roll Zeroed with " + ahrs.RollZero.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                lblRollZeroOffset.Text = "***";
            }
        }

        // Port of btnRemoveZeroOffset_Click (354-359).
        private void btnRemoveZeroOffset_Click(object sender, RoutedEventArgs e)
        {
            ahrs.RollZero = 0;
            lblRollZeroOffset.Text = "0.00";
            Log.EventWriter("Roll Zero Offset Removed");
        }

        // Port of btnResetIMU_Click (361-365).
        private void btnResetIMU_Click(object sender, RoutedEventArgs e)
        {
            ahrs.ImuHeading = 99999;
            ahrs.ImuRoll = 88888;
        }

        #endregion

        // =====================================================================================
        #region Data — Features On/Off  // [XPLAT] from ConfigData.Designer.cs (region Features On Off)
        // =====================================================================================

        // Port of tabBtns_Enter (371-398). setFeatures is a CFeatureSettings reference object on Settings.
        private void tabBtns_Enter()
        {
            cboxFeatureTram.IsChecked = Settings.Default.setFeatures.isTramOn;
            cboxFeatureHeadland.IsChecked = Settings.Default.setFeatures.isHeadlandOn;
            cboxFeatureBoundary.IsChecked = Settings.Default.setFeatures.isBoundaryOn;

            // the nudge controls at bottom menu
            cboxFeatureNudge.IsChecked = Settings.Default.setFeatures.isABLineOn;
            cboxFeatureRecPath.IsChecked = Settings.Default.setFeatures.isRecPathOn;
            cboxFeatureABSmooth.IsChecked = Settings.Default.setFeatures.isABSmoothOn;
            cboxFeatureHideContour.IsChecked = Settings.Default.setFeatures.isHideContourOn;
            cboxFeatureWebcam.IsChecked = Settings.Default.setFeatures.isWebCamOn;
            cboxFeatureOffsetFix.IsChecked = Settings.Default.setFeatures.isOffsetFixOn;

            cboxFeatureUTurn.IsChecked = Settings.Default.setFeatures.isUTurnOn;
            cboxFeatureLateral.IsChecked = Settings.Default.setFeatures.isLateralOn;

            cboxTurnSound.IsChecked = Settings.Default.setSound_isUturnOn;
            cboxSteerSound.IsChecked = Settings.Default.setSound_isAutoSteerOn;
            cboxHydLiftSound.IsChecked = Settings.Default.setSound_isHydLiftOn;
            cboxSectionsSound.IsChecked = Settings.Default.setSound_isSectionsOn;

            cboxAutoStartAgIO.IsChecked = Settings.Default.setDisplay_isAutoStartAgIO;
            cboxAutoOffAgIO.IsChecked = Settings.Default.setDisplay_isAutoOffAgIO;
            cboxShutdownWhenNoPower.IsChecked = Settings.Default.setDisplay_isShutdownWhenNoPower;
            cboxHardwareMessages.IsChecked = Settings.Default.setDisplay_isHardwareMessages;
        }

        // Port of tabBtns_Leave (400-438). mf.sounds.* -> ctx.Is*SoundOn; mf.isAutoStartAgIO -> ctx.IsAutoStartAgIO.
        private void tabBtns_Leave()
        {
            Settings.Default.setFeatures.isTramOn = cboxFeatureTram.IsChecked == true;
            Settings.Default.setFeatures.isHeadlandOn = cboxFeatureHeadland.IsChecked == true;

            Settings.Default.setFeatures.isABLineOn = cboxFeatureNudge.IsChecked == true;

            Settings.Default.setFeatures.isBoundaryOn = cboxFeatureBoundary.IsChecked == true;
            Settings.Default.setFeatures.isRecPathOn = cboxFeatureRecPath.IsChecked == true;
            Settings.Default.setFeatures.isABSmoothOn = cboxFeatureABSmooth.IsChecked == true;
            Settings.Default.setFeatures.isHideContourOn = cboxFeatureHideContour.IsChecked == true;
            Settings.Default.setFeatures.isWebCamOn = cboxFeatureWebcam.IsChecked == true;
            Settings.Default.setFeatures.isOffsetFixOn = cboxFeatureOffsetFix.IsChecked == true;

            Settings.Default.setFeatures.isLateralOn = cboxFeatureLateral.IsChecked == true;
            Settings.Default.setFeatures.isUTurnOn = cboxFeatureUTurn.IsChecked == true;

            Settings.Default.setSound_isUturnOn = cboxTurnSound.IsChecked == true;
            ctx.IsTurnSoundOn = cboxTurnSound.IsChecked == true;
            Settings.Default.setSound_isAutoSteerOn = cboxSteerSound.IsChecked == true;
            ctx.IsSteerSoundOn = cboxSteerSound.IsChecked == true;
            Settings.Default.setSound_isSectionsOn = cboxSectionsSound.IsChecked == true;
            ctx.IsSectionsSoundOn = cboxSectionsSound.IsChecked == true;
            Settings.Default.setSound_isHydLiftOn = cboxHydLiftSound.IsChecked == true;
            ctx.IsHydLiftSoundOn = cboxHydLiftSound.IsChecked == true;

            Settings.Default.setDisplay_isAutoStartAgIO = cboxAutoStartAgIO.IsChecked == true;
            ctx.IsAutoStartAgIO = cboxAutoStartAgIO.IsChecked == true;

            Settings.Default.setDisplay_isAutoOffAgIO = cboxAutoOffAgIO.IsChecked == true;

            Settings.Default.setDisplay_isShutdownWhenNoPower = cboxShutdownWhenNoPower.IsChecked == true;

            Settings.Default.setDisplay_isHardwareMessages = cboxHardwareMessages.IsChecked == true;
            UpdateAutoSwitchDualFixSpeedUI();

            Settings.Default.Save();
        }

        // Port of btnRightMenuOrder_Click (440-446). [XPLAT] the WinForms `new FormButtonsRightPanel(mf)`
        // child dialog is launched through the decoupled IConfigContext hook (no FormGPS reference).
        private void btnRightMenuOrder_Click(object sender, RoutedEventArgs e)
        {
            ctx.ShowRightMenuOrderDialog(this);
        }

        #endregion

        // =====================================================================================
        #region Data — event wiring  // [XPLAT] from FormConfig.Designer.cs (Data tab handlers)
        // =====================================================================================

        private void WireDataEvents()
        {
            // WinForms CheckedChanged -> Avalonia IsCheckedChanged (fires on programmatic set, as Load relies on)
            cboxMinGPSStep.IsCheckedChanged += cboxMinGPSStep_CheckedChanged;
            cboxIsAutoSwitchDualFixOn.IsCheckedChanged += cboxIsAutoSwitchDualFixOn_CheckedChanged;
            rbtnHeadingHDT.IsCheckedChanged += rbtnHeadingFix_CheckedChanged;
            rbtnHeadingFix.IsCheckedChanged += rbtnHeadingFix_CheckedChanged;

            // WinForms ScrollBar.ValueChanged -> Avalonia Slider.ValueChanged
            hsbarFusion.ValueChanged += hsbarFusion_ValueChanged;
            hsbarRollFilter.ValueChanged += hsbarRollFilter_ValueChanged;

            // Heading nud keypad buttons
            nudFixJumpDistance.Click += nudFixJumpDistance_Click;
            nudDualHeadingOffset.Click += nudDualHeadingOffset_Click;
            nudDualReverseDistance.Click += nudDualReverseDistance_Click;
            nudAutoSwitchDualFixSpeed.Click += nudAutoSwitchDualFixSpeed_Click;

            // Roll buttons (btnRollOffsetDown/Up are RepeatButton; Click is inherited from Button)
            btnRollOffsetDown.Click += btnRollOffsetDown_Click;
            btnRollOffsetUp.Click += btnRollOffsetUp_Click;
            btnZeroRoll.Click += btnZeroRoll_Click;
            btnRemoveZeroOffset.Click += btnRemoveZeroOffset_Click;
            btnResetIMU.Click += btnResetIMU_Click;

            // Features
            btnRightMenuOrder.Click += btnRightMenuOrder_Click;
        }

        #endregion

        // =====================================================================================
        #region Module — Machine  // [XPLAT] from ConfigModule.Designer.cs (region Module MAchine)
        // [XPLAT] tabASteer_Enter/Leave were empty in the source and have no corresponding Avalonia
        // TabItem (autosteer config lives outside this dialog), so they are intentionally omitted.
        // =====================================================================================

        // Port of Enable_AlertM_Click (ConfigModule.Designer.cs 24-27). Wired to cboxMachInvertRelays.Click.
        private void Enable_AlertM_Click(object sender, RoutedEventArgs e)
        {
            pboxSendMachine.IsVisible = true;
        }

        // Port of tabAMachine_Enter (31-67). setArdMac_setting0 is a packed byte: bit0 = invert relays,
        // bit1 = hyd on. cboxIsHydOn.IsCheckedChanged fires cboxIsHydOn_Click (which reveals pboxSendMachine)
        // exactly as the WinForms CheckedChanged did on the programmatic set.
        private void tabAMachine_Enter()
        {
            pboxSendMachine.IsVisible = false;

            int sett = ToolSettings.Default.setArdMac_setting0;

            cboxMachInvertRelays.IsChecked = ((sett & 1) == 1);

            cboxIsHydOn.IsChecked = ((sett & 2) == 2);

            if (cboxIsHydOn.IsChecked == true)
            {
                // [XPLAT] image swap (Resources.SwitchOn) handled by the XAML :checked style.
                nudHydLiftLookAhead.IsEnabled = true;
                nudLowerTime.IsEnabled = true;
                nudRaiseTime.IsEnabled = true;
            }
            else
            {
                // [XPLAT] image swap (Resources.SwitchOff) handled by the XAML style.
                nudHydLiftLookAhead.IsEnabled = false;
                nudLowerTime.IsEnabled = false;
                nudRaiseTime.IsEnabled = false;
            }

            N(nudRaiseTime).Value = (decimal)ToolSettings.Default.setArdMac_hydRaiseTime;
            N(nudLowerTime).Value = (decimal)ToolSettings.Default.setArdMac_hydLowerTime;

            N(nudUser1).Value = ToolSettings.Default.setArdMac_user1;
            N(nudUser2).Value = ToolSettings.Default.setArdMac_user2;
            N(nudUser3).Value = ToolSettings.Default.setArdMac_user3;
            N(nudUser4).Value = ToolSettings.Default.setArdMac_user4;

            btnSendMachinePGN.Focus();

            N(nudHydLiftLookAhead).Value = (decimal)ToolSettings.Default.setVehicle_hydraulicLiftLookAhead;
        }

        // Port of tabAMachine_Leave (68-71).
        private void tabAMachine_Leave()
        {
            pboxSendMachine.IsVisible = false;
        }

        // Port of nudHydLiftSecs_Click (73-79). Wired to nudHydLiftLookAhead.Click (name differs in source).
        private async void nudHydLiftSecs_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudHydLiftLookAhead))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudRaiseTime_Click (81-87).
        private async void nudRaiseTime_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudRaiseTime))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudLowerTime_Click (89-95).
        private async void nudLowerTime_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudLowerTime))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudUser1_Click (97-103).
        private async void nudUser1_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudUser1))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudUser2_Click (105-111).
        private async void nudUser2_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudUser2))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudUser3_Click (113-119).
        private async void nudUser3_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudUser3))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of nudUser4_Click (121-127).
        private async void nudUser4_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudUser4))
            {
                pboxSendMachine.IsVisible = true;
            }
        }

        // Port of cboxIsHydOn_Click (128-145). Wired to cboxIsHydOn.IsCheckedChanged.
        private void cboxIsHydOn_Click(object sender, RoutedEventArgs e)
        {
            if (cboxIsHydOn.IsChecked == true)
            {
                // [XPLAT] image swap (Resources.SwitchOn) handled by the XAML :checked style.
                nudHydLiftLookAhead.IsEnabled = true;
                nudLowerTime.IsEnabled = true;
                nudRaiseTime.IsEnabled = true;
            }
            else
            {
                // [XPLAT] image swap (Resources.SwitchOff) handled by the XAML style.
                nudHydLiftLookAhead.IsEnabled = false;
                nudLowerTime.IsEnabled = false;
                nudRaiseTime.IsEnabled = false;
            }
            pboxSendMachine.IsVisible = true;
        }

        // Port of SaveSettingsMachine (147-185). The mf.p_238.pgn[...] writes + mf.SendPgnToLoop(...) are
        // routed through the decoupled IConfigContext.SendMachineConfigPgn(...) facade.
        private void SaveSettingsMachine()
        {
            int set = 1;
            int reset = 2046;
            int sett = 0;

            if (cboxMachInvertRelays.IsChecked == true) sett |= set;
            else sett &= reset;

            set <<= 1;
            reset <<= 1;
            reset += 1;
            if (cboxIsHydOn.IsChecked == true) sett |= set;
            else sett &= reset;

            ToolSettings.Default.setArdMac_setting0 = (byte)sett;
            ToolSettings.Default.setArdMac_hydRaiseTime = (byte)N(nudRaiseTime).Value;
            ToolSettings.Default.setArdMac_hydLowerTime = (byte)N(nudLowerTime).Value;

            ToolSettings.Default.setArdMac_user1 = (byte)N(nudUser1).Value;
            ToolSettings.Default.setArdMac_user2 = (byte)N(nudUser2).Value;
            ToolSettings.Default.setArdMac_user3 = (byte)N(nudUser3).Value;
            ToolSettings.Default.setArdMac_user4 = (byte)N(nudUser4).Value;

            ToolSettings.Default.setVehicle_hydraulicLiftLookAhead = (double)N(nudHydLiftLookAhead).Value;
            vehicle.hydLiftLookAheadTime = ToolSettings.Default.setVehicle_hydraulicLiftLookAhead;

            ctx.SendMachineConfigPgn(
                (byte)sett,
                (byte)N(nudRaiseTime).Value,
                (byte)N(nudLowerTime).Value,
                (byte)N(nudUser1).Value,
                (byte)N(nudUser2).Value,
                (byte)N(nudUser3).Value,
                (byte)N(nudUser4).Value);

            pboxSendMachine.IsVisible = false;
        }

        // Port of btnSendMachinePGN_Click (187-196).
        private void btnSendMachinePGN_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsMachine();

            ToolSettings.Default.Save();

            ShowFormDialog(gStr.gsMachinePort, gStr.gsSentToMachineModule, DialogSeverity.Info);

            pboxSendMachine.IsVisible = false;
        }

        #endregion

        // =====================================================================================
        #region Module — Relay Config  // [XPLAT] from ConfigModule.Designer.cs (region Relay Config)
        // =====================================================================================

        // Port of tabRelay_Enter (204-267). WinForms cleared+AddRange the same list into each ComboBox;
        // Avalonia uses ItemsSource. The 24 programmatic SelectedIndex sets are wrapped in _suppressCombo
        // so the shared cboxPin0_Click (SelectionChanged) does NOT falsely reveal pboxSendRelay on load.
        private void tabRelay_Enter()
        {
            pboxSendRelay.IsVisible = false;

            string[] wordsList = { "-","Section 1","Section 2","Section 3","Section 4","Section 5","Section 6","Section 7",
                    "Section 8","Section 9","Section 10","Section 11","Section 12","Section 13","Section 14","Section 15",
                    "Section 16","Hyd Up","Hyd Down","Tram Right","Tram Left", "Geo Stop" };

            // 19 tram right and 20 tram left
            _suppressCombo = true;

            cboxPin0.ItemsSource = wordsList;
            cboxPin1.ItemsSource = wordsList;
            cboxPin2.ItemsSource = wordsList;
            cboxPin3.ItemsSource = wordsList;
            cboxPin4.ItemsSource = wordsList;
            cboxPin5.ItemsSource = wordsList;
            cboxPin6.ItemsSource = wordsList;
            cboxPin7.ItemsSource = wordsList;
            cboxPin8.ItemsSource = wordsList;
            cboxPin9.ItemsSource = wordsList;
            cboxPin10.ItemsSource = wordsList;
            cboxPin11.ItemsSource = wordsList;
            cboxPin12.ItemsSource = wordsList;
            cboxPin13.ItemsSource = wordsList;
            cboxPin14.ItemsSource = wordsList;
            cboxPin15.ItemsSource = wordsList;
            cboxPin16.ItemsSource = wordsList;
            cboxPin17.ItemsSource = wordsList;
            cboxPin18.ItemsSource = wordsList;
            cboxPin19.ItemsSource = wordsList;
            cboxPin20.ItemsSource = wordsList;
            cboxPin21.ItemsSource = wordsList;
            cboxPin22.ItemsSource = wordsList;
            cboxPin23.ItemsSource = wordsList;

            words = ToolSettings.Default.setRelay_pinConfig.Split(',');

            cboxPin0.SelectedIndex = int.Parse(words[0], CultureInfo.InvariantCulture);
            cboxPin1.SelectedIndex = int.Parse(words[1], CultureInfo.InvariantCulture);
            cboxPin2.SelectedIndex = int.Parse(words[2], CultureInfo.InvariantCulture);
            cboxPin3.SelectedIndex = int.Parse(words[3], CultureInfo.InvariantCulture);
            cboxPin4.SelectedIndex = int.Parse(words[4], CultureInfo.InvariantCulture);
            cboxPin5.SelectedIndex = int.Parse(words[5], CultureInfo.InvariantCulture);
            cboxPin6.SelectedIndex = int.Parse(words[6], CultureInfo.InvariantCulture);
            cboxPin7.SelectedIndex = int.Parse(words[7], CultureInfo.InvariantCulture);
            cboxPin8.SelectedIndex = int.Parse(words[8], CultureInfo.InvariantCulture);
            cboxPin9.SelectedIndex = int.Parse(words[9], CultureInfo.InvariantCulture);
            cboxPin10.SelectedIndex = int.Parse(words[10], CultureInfo.InvariantCulture);
            cboxPin11.SelectedIndex = int.Parse(words[11], CultureInfo.InvariantCulture);
            cboxPin12.SelectedIndex = int.Parse(words[12], CultureInfo.InvariantCulture);
            cboxPin13.SelectedIndex = int.Parse(words[13], CultureInfo.InvariantCulture);
            cboxPin14.SelectedIndex = int.Parse(words[14], CultureInfo.InvariantCulture);
            cboxPin15.SelectedIndex = int.Parse(words[15], CultureInfo.InvariantCulture);
            cboxPin16.SelectedIndex = int.Parse(words[16], CultureInfo.InvariantCulture);
            cboxPin17.SelectedIndex = int.Parse(words[17], CultureInfo.InvariantCulture);
            cboxPin18.SelectedIndex = int.Parse(words[18], CultureInfo.InvariantCulture);
            cboxPin19.SelectedIndex = int.Parse(words[19], CultureInfo.InvariantCulture);
            cboxPin20.SelectedIndex = int.Parse(words[20], CultureInfo.InvariantCulture);
            cboxPin21.SelectedIndex = int.Parse(words[21], CultureInfo.InvariantCulture);
            cboxPin22.SelectedIndex = int.Parse(words[22], CultureInfo.InvariantCulture);
            cboxPin23.SelectedIndex = int.Parse(words[23], CultureInfo.InvariantCulture);

            _suppressCombo = false;
        }

        // Port of tabRelay_Leave (269-272).
        private void tabRelay_Leave()
        {
            pboxSendRelay.IsVisible = false;
        }

        // Port of btnSendRelayConfigPGN_Click (274-282).
        private void btnSendRelayConfigPGN_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsRelay();
            ctx.SendRelaySettingsToMachineModule();

            ShowFormDialog(gStr.gsMachinePort, gStr.gsSentToMachineModule, DialogSeverity.Info);

            pboxSendRelay.IsVisible = false;
        }

        // Port of SaveSettingsRelay (284-319). [XPLAT] StringBuilder replaced by string.Join over
        // InvariantCulture-formatted indices (schema-frozen comma-separated pin config string).
        private void SaveSettingsRelay()
        {
            string[] pinIdx = new string[]
            {
                cboxPin0.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin1.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin2.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin3.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin4.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin5.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin6.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin7.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin8.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin9.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin10.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin11.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin12.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin13.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin14.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin15.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin16.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin17.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin18.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin19.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin20.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin21.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin22.SelectedIndex.ToString(CultureInfo.InvariantCulture),
                cboxPin23.SelectedIndex.ToString(CultureInfo.InvariantCulture)
            };

            ToolSettings.Default.setRelay_pinConfig = string.Join(",", pinIdx);

            // save settings
            ToolSettings.Default.Save();
            pboxSendRelay.IsVisible = false;
        }

        // Port of btnRelaySetDefaultConfig_Click (321-349). _suppressCombo guards the programmatic sets;
        // pboxSendRelay is explicitly revealed (the default config differs from the saved one).
        private void btnRelaySetDefaultConfig_Click(object sender, RoutedEventArgs e)
        {
            pboxSendRelay.IsVisible = true;

            _suppressCombo = true;
            cboxPin0.SelectedIndex = 1;
            cboxPin1.SelectedIndex = 2;
            cboxPin2.SelectedIndex = 3;
            cboxPin3.SelectedIndex = 0;
            cboxPin4.SelectedIndex = 0;
            cboxPin5.SelectedIndex = 0;
            cboxPin6.SelectedIndex = 0;
            cboxPin7.SelectedIndex = 0;
            cboxPin8.SelectedIndex = 0;
            cboxPin9.SelectedIndex = 0;
            cboxPin10.SelectedIndex = 0;
            cboxPin11.SelectedIndex = 0;
            cboxPin12.SelectedIndex = 0;
            cboxPin13.SelectedIndex = 0;
            cboxPin14.SelectedIndex = 0;
            cboxPin15.SelectedIndex = 0;
            cboxPin16.SelectedIndex = 0;
            cboxPin17.SelectedIndex = 0;
            cboxPin18.SelectedIndex = 0;
            cboxPin19.SelectedIndex = 0;
            cboxPin20.SelectedIndex = 0;
            cboxPin21.SelectedIndex = 0;
            cboxPin22.SelectedIndex = 0;
            cboxPin23.SelectedIndex = 0;
            _suppressCombo = false;
        }

        // Port of btnRelayResetConfigToNone_Click (351-379).
        private void btnRelayResetConfigToNone_Click(object sender, RoutedEventArgs e)
        {
            pboxSendRelay.IsVisible = true;

            _suppressCombo = true;
            cboxPin0.SelectedIndex = 0;
            cboxPin1.SelectedIndex = 0;
            cboxPin2.SelectedIndex = 0;
            cboxPin3.SelectedIndex = 0;
            cboxPin4.SelectedIndex = 0;
            cboxPin5.SelectedIndex = 0;
            cboxPin6.SelectedIndex = 0;
            cboxPin7.SelectedIndex = 0;
            cboxPin8.SelectedIndex = 0;
            cboxPin9.SelectedIndex = 0;
            cboxPin10.SelectedIndex = 0;
            cboxPin11.SelectedIndex = 0;
            cboxPin12.SelectedIndex = 0;
            cboxPin13.SelectedIndex = 0;
            cboxPin14.SelectedIndex = 0;
            cboxPin15.SelectedIndex = 0;
            cboxPin16.SelectedIndex = 0;
            cboxPin17.SelectedIndex = 0;
            cboxPin18.SelectedIndex = 0;
            cboxPin19.SelectedIndex = 0;
            cboxPin20.SelectedIndex = 0;
            cboxPin21.SelectedIndex = 0;
            cboxPin22.SelectedIndex = 0;
            cboxPin23.SelectedIndex = 0;
            _suppressCombo = false;
        }

        // Port of cboxPin0_Click (381-385). SHARED by all 24 pin combos. [XPLAT] the WinForms ComboBox
        // .Click is mapped to Avalonia SelectionChanged; _suppressCombo prevents false reveals on load.
        private void cboxPin0_Click(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressCombo) return;
            pboxSendRelay.IsVisible = true;
        }

        #endregion

        // =====================================================================================
        #region Module — UTurn  // [XPLAT] from ConfigModule.Designer.cs (regions Uturn Enter-Leave / Uturn controls)
        // =====================================================================================

        // Port of tabUTurn_Enter (391-406).
        private void tabUTurn_Enter()
        {
            UpdateUturnText();

            lblSmoothing.Text = gyd.UTurnSmoothing.ToString(CultureInfo.InvariantCulture);

            double bob = Settings.Default.set_youTurnDistanceFromBoundary * ctx.M2FtOrM;
            if (bob < 0.2) bob = 0.2;
            N(nudTurnDistanceFromBoundary).Value = (decimal)(Math.Round(bob, 2));

            bob = Settings.Default.set_youTurnRadius * ctx.M2FtOrM;
            if (bob < 2) bob = 2;
            N(nudYouTurnRadius).Value = (decimal)(Math.Round(bob, 2));

            lblFtMUTurn.Text = lblFtMTurnRadius.Text = ctx.UnitsFtM;
        }

        // Port of tabUTurn_Leave (408-420). mf.bnd.BuildTurnLines() and mf.yt.ResetCreatedYouTurn()
        // are routed through ctx.BuildTurnLines() and gyd.ResetCreatedYouTurn().
        private void tabUTurn_Leave()
        {
            Settings.Default.setAS_uTurnSmoothing = gyd.UTurnSmoothing;
            Settings.Default.set_youTurnExtensionLength = gyd.YouTurnStartOffset;

            Settings.Default.set_youTurnRadius = gyd.YouTurnRadius;
            Settings.Default.set_youTurnDistanceFromBoundary = gyd.UturnDistanceFromBoundary;

            Settings.Default.Save();

            ctx.BuildTurnLines();
            gyd.ResetCreatedYouTurn();
        }

        // Port of UpdateUturnText (426-436).
        private void UpdateUturnText()
        {
            if (ctx.IsMetric)
            {
                lblDistance.Text = Math.Abs(gyd.YouTurnStartOffset).ToString(CultureInfo.InvariantCulture) + " m";
            }
            else
            {
                lblDistance.Text = Math.Abs((int)(gyd.YouTurnStartOffset * glm.m2ft)).ToString(CultureInfo.InvariantCulture) + " ft";
            }
        }

        // Port of nudYouTurnRadius_Click (438-444).
        private async void nudYouTurnRadius_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudYouTurnRadius))
            {
                gyd.YouTurnRadius = (double)N(nudYouTurnRadius).Value * ctx.FtOrMtoM;
            }
        }

        // Port of nudTurnDistanceFromBoundary_Click (446-452).
        private async void nudTurnDistanceFromBoundary_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTurnDistanceFromBoundary))
            {
                gyd.UturnDistanceFromBoundary = (double)N(nudTurnDistanceFromBoundary).Value * ctx.FtOrMtoM;
            }
        }

        // Port of btnDistanceDn_Click (454-458). [XPLAT] the post-decrement-in-comparison is expanded
        // so the property read/compare/write order matches the WinForms field semantics exactly.
        private void btnDistanceDn_Click(object sender, RoutedEventArgs e)
        {
            int cur = gyd.YouTurnStartOffset;
            gyd.YouTurnStartOffset = cur - 1;
            if (cur < 4) gyd.YouTurnStartOffset = 3;
            UpdateUturnText();
        }

        // Port of btnDistanceUp_Click (460-464).
        private void btnDistanceUp_Click(object sender, RoutedEventArgs e)
        {
            int cur = gyd.YouTurnStartOffset;
            gyd.YouTurnStartOffset = cur + 1;
            if (cur > 49) gyd.YouTurnStartOffset = 50;
            UpdateUturnText();
        }

        // Port of btnTurnSmoothingDown_Click (465-470).
        private void btnTurnSmoothingDown_Click(object sender, RoutedEventArgs e)
        {
            gyd.UTurnSmoothing -= 2;
            if (gyd.UTurnSmoothing < 8) gyd.UTurnSmoothing = 8;
            lblSmoothing.Text = gyd.UTurnSmoothing.ToString(CultureInfo.InvariantCulture);
        }

        // Port of btnTurnSmoothingUp_Click (472-477).
        private void btnTurnSmoothingUp_Click(object sender, RoutedEventArgs e)
        {
            gyd.UTurnSmoothing += 2;
            if (gyd.UTurnSmoothing > 50) gyd.UTurnSmoothing = 50;
            lblSmoothing.Text = gyd.UTurnSmoothing.ToString(CultureInfo.InvariantCulture);
        }

        #endregion

        // =====================================================================================
        #region Module — Tram  // [XPLAT] from ConfigModule.Designer.cs (region Tram)
        // =====================================================================================

        // Port of tabTram_Enter (482-489).
        private void tabTram_Enter()
        {
            lblTramWidthUnits.Text = ctx.UnitsInCm;

            N(nudTramWidth).Value = (int)(Math.Abs(Settings.Default.setTram_tramWidth) * ctx.M2InchOrCm);
            chkBoxOverrideTramControlPos.IsChecked = ToolSettings.Default.setTool_isTramOuterInverted;
            cboxDisplayTramControl.IsChecked = ToolSettings.Default.setTool_isDisplayTramControl;
        }

        // Port of tabTram_Leave (491-502). mf.tool.isDisplayTramControl -> tool.isDisplayTramControl;
        // mf.tram.IsTramOuterOrInner() -> tram.IsTramOuterOrInner().
        private void tabTram_Leave()
        {
            ToolSettings.Default.setTool_isTramOuterInverted = chkBoxOverrideTramControlPos.IsChecked == true;

            ToolSettings.Default.setTool_isDisplayTramControl = cboxDisplayTramControl.IsChecked == true;
            tool.isDisplayTramControl = cboxDisplayTramControl.IsChecked == true;

            tram.IsTramOuterOrInner();

            ToolSettings.Default.Save();
        }

        // Port of nudTramWidth_Click (503-510). mf.tram.tramWidth -> tram.TramWidth.
        private async void nudTramWidth_Click(object sender, RoutedEventArgs e)
        {
            if (await ShowKeypad(nudTramWidth))
            {
                tram.TramWidth = (double)N(nudTramWidth).Value * ctx.InchOrCm2m;
                Settings.Default.setTram_tramWidth = tram.TramWidth;
            }
        }

        #endregion

        // =====================================================================================
        #region Module — event wiring  // [XPLAT] from FormConfig.Designer.cs (Module tab handlers)
        // =====================================================================================

        private void WireModuleEvents()
        {
            // Machine
            cboxIsHydOn.IsCheckedChanged += cboxIsHydOn_Click;     // WinForms CheckedChanged -> IsCheckedChanged
            cboxMachInvertRelays.Click += Enable_AlertM_Click;     // WinForms Click
            nudHydLiftLookAhead.Click += nudHydLiftSecs_Click;     // source handler name differs from control
            nudRaiseTime.Click += nudRaiseTime_Click;
            nudLowerTime.Click += nudLowerTime_Click;
            nudUser1.Click += nudUser1_Click;
            nudUser2.Click += nudUser2_Click;
            nudUser3.Click += nudUser3_Click;
            nudUser4.Click += nudUser4_Click;
            btnSendMachinePGN.Click += btnSendMachinePGN_Click;

            // Relay — all 24 pin combos share cboxPin0_Click (SelectionChanged)
            cboxPin0.SelectionChanged += cboxPin0_Click;
            cboxPin1.SelectionChanged += cboxPin0_Click;
            cboxPin2.SelectionChanged += cboxPin0_Click;
            cboxPin3.SelectionChanged += cboxPin0_Click;
            cboxPin4.SelectionChanged += cboxPin0_Click;
            cboxPin5.SelectionChanged += cboxPin0_Click;
            cboxPin6.SelectionChanged += cboxPin0_Click;
            cboxPin7.SelectionChanged += cboxPin0_Click;
            cboxPin8.SelectionChanged += cboxPin0_Click;
            cboxPin9.SelectionChanged += cboxPin0_Click;
            cboxPin10.SelectionChanged += cboxPin0_Click;
            cboxPin11.SelectionChanged += cboxPin0_Click;
            cboxPin12.SelectionChanged += cboxPin0_Click;
            cboxPin13.SelectionChanged += cboxPin0_Click;
            cboxPin14.SelectionChanged += cboxPin0_Click;
            cboxPin15.SelectionChanged += cboxPin0_Click;
            cboxPin16.SelectionChanged += cboxPin0_Click;
            cboxPin17.SelectionChanged += cboxPin0_Click;
            cboxPin18.SelectionChanged += cboxPin0_Click;
            cboxPin19.SelectionChanged += cboxPin0_Click;
            cboxPin20.SelectionChanged += cboxPin0_Click;
            cboxPin21.SelectionChanged += cboxPin0_Click;
            cboxPin22.SelectionChanged += cboxPin0_Click;
            cboxPin23.SelectionChanged += cboxPin0_Click;
            btnSendRelayConfigPGN.Click += btnSendRelayConfigPGN_Click;
            btnRelaySetDefaultConfig.Click += btnRelaySetDefaultConfig_Click;
            btnRelayResetConfigToNone.Click += btnRelayResetConfigToNone_Click;

            // UTurn (btnDistance*/btnTurnSmoothing* are RepeatButton; Click is inherited from Button)
            nudYouTurnRadius.Click += nudYouTurnRadius_Click;
            nudTurnDistanceFromBoundary.Click += nudTurnDistanceFromBoundary_Click;
            btnDistanceDn.Click += btnDistanceDn_Click;
            btnDistanceUp.Click += btnDistanceUp_Click;
            btnTurnSmoothingDown.Click += btnTurnSmoothingDown_Click;
            btnTurnSmoothingUp.Click += btnTurnSmoothingUp_Click;

            // Tram
            nudTramWidth.Click += nudTramWidth_Click;
        }

        #endregion
    }
}
