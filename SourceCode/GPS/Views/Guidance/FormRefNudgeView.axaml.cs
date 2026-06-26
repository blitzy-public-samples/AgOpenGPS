// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormRefNudge.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] The outcome of the reference-track-nudge dialog, mapping the two WinForms close paths
    /// to a strongly-typed result consumed via
    /// <c>ShowDialog&lt;FormRefNudgeView.RefNudgeResult&gt;</c>.
    /// </summary>
    public enum RefNudgeResult
    {
        /// <summary>Exit (btnExit) — the host persists the nudged tracks to disk.</summary>
        Exit = 0,

        /// <summary>Cancel (btnCancelMain) — the host restores the pre-edit track backup.</summary>
        Cancel = 1
    }

    /// <summary>
    /// [XPLAT] Code-behind for the reference-track-nudge dialog — a 1:1 visual-parity reimplementation
    /// of the WinForms <c>FormRefNudge</c> (FormRefNudge.cs + FormRefNudge.Designer.cs). The operator
    /// shifts the reference track left / right by the snap distance (or half the tool width), then
    /// exits (saving) or cancels (restoring).
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). Self-contained behaviour is reproduced exactly:
    /// <list type="bullet">
    ///   <item>The snap adjustment in metres (<see cref="_snapAdj"/>) is derived from
    ///         <c>ToolSettings.Default.setAS_snapDistanceRef</c> as the original
    ///         (<c>setAS_snapDistanceRef * 0.01</c>); <c>nudSnapDistance</c> shows the centimetre
    ///         value; <c>lblOffset</c> shows the running <see cref="_distanceMoved"/>.</item>
    ///   <item><see cref="OnExitClick"/> / <see cref="OnCancelClick"/> close with the matching
    ///         <see cref="RefNudgeResult"/>.</item>
    /// </list>
    /// The reference-track mutations (<c>mf.trk.NudgeRefTrack</c>), the pre-edit backup / restore
    /// (the original <c>gTemp</c> copy of <c>mf.trk.gArr</c> plus <c>isABValid</c>/<c>isCurveValid</c>
    /// resets), the track save, and the half-tool width from <c>mf.tool</c> are host-owned at this
    /// checkpoint. The dialog forwards intent through <see cref="NudgeRefRequested"/> and
    /// <see cref="HalfToolNudgeRefRequested"/> and reports its outcome through the result. No
    /// fabricated calls to not-yet-projected services are made here — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormRefNudgeView : Window
    {
        // [XPLAT] Snap adjustment in METRES and the running distance moved — as the original.
        private double _snapAdj;
        private double _distanceMoved;

        /// <summary>The snap adjustment in metres, from <c>ToolSettings.setAS_snapDistanceRef</c>.</summary>
        public double SnapAdjustment => _snapAdj;

        /// <summary>
        /// [XPLAT] Raised for a left (negative) / right (positive) reference-track nudge; the signed
        /// argument is the move in metres, replacing <c>mf.trk.NudgeRefTrack(±snapAdj)</c>.
        /// </summary>
        public event EventHandler<double> NudgeRefRequested;

        /// <summary>
        /// [XPLAT] Raised for a half-tool-width reference nudge. The argument is the direction sign
        /// (-1 left, +1 right); the host computes the metre distance from its tool geometry and is
        /// also responsible for advancing the offset readout for this case.
        /// </summary>
        public event EventHandler<double> HalfToolNudgeRefRequested;

        public FormRefNudgeView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            btnExit.Click += OnExitClick;
            btnCancelMain.Click += OnCancelClick;
            btnAdjLeft.Click += OnAdjustLeftClick;
            btnAdjRight.Click += OnAdjustRightClick;
            btnHalfToolLeft.Click += OnHalfToolLeftClick;
            btnHalfToolRight.Click += OnHalfToolRightClick;
        }

        // [XPLAT] FormEditTrack_Load: derive the snap adjustment, seed the readouts.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            double snapDistanceCm = ToolSettings.Default.setAS_snapDistanceRef;
            _snapAdj = snapDistanceCm * 0.01;
            _distanceMoved = 0;
            nudSnapDistance.Content = ((int)snapDistanceCm).ToString(CultureInfo.InvariantCulture);
            lblOffset.Text = "0";
        }

        // [XPLAT] btnExit_Click: host saves the tracks; close with Exit.
        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close(RefNudgeResult.Exit);
        }

        // [XPLAT] btnCancelMain_Click: host restores the pre-edit backup; close with Cancel.
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(RefNudgeResult.Cancel);
        }

        // [XPLAT] btnAdjLeft_Click: NudgeRefTrack(-snapAdj); accumulate and show the offset.
        private void OnAdjustLeftClick(object sender, RoutedEventArgs e)
        {
            NudgeRefRequested?.Invoke(this, -_snapAdj);
            _distanceMoved += -_snapAdj;
            UpdateOffsetLabel();
        }

        // [XPLAT] btnAdjRight_Click: NudgeRefTrack(snapAdj); accumulate and show the offset.
        private void OnAdjustRightClick(object sender, RoutedEventArgs e)
        {
            NudgeRefRequested?.Invoke(this, _snapAdj);
            _distanceMoved += _snapAdj;
            UpdateOffsetLabel();
        }

        // [XPLAT] btnHalfToolLeft_Click: half tool width to the left (host computes the distance).
        private void OnHalfToolLeftClick(object sender, RoutedEventArgs e)
        {
            HalfToolNudgeRefRequested?.Invoke(this, -1.0);
        }

        // [XPLAT] btnHalfToolRight_Click: half tool width to the right (host computes the distance).
        private void OnHalfToolRightClick(object sender, RoutedEventArgs e)
        {
            HalfToolNudgeRefRequested?.Invoke(this, 1.0);
        }

        // [XPLAT] DistanceMovedLabel: show the accumulated snap-nudge distance in metres. The original
        // multiplied by mf.m2InchOrCm and appended mf.unitsInCm; that unit conversion/formatting is
        // host-owned because the unit state lives on the FormGPS god-object.
        private void UpdateOffsetLabel()
        {
            lblOffset.Text = ((int)_distanceMoved).ToString(CultureInfo.InvariantCulture);
        }
    }
}
