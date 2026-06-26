// [XPLAT] migrated from net48/WinForms (Forms/Guidance/FormNudge.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the track-nudge dialog — a 1:1 visual-parity reimplementation of the
    /// WinForms <c>FormNudge</c> (FormNudge.cs + FormNudge.Designer.cs). The operator shifts the
    /// active guidance track left / right by the snap distance (or half the tool width), snaps it to
    /// the pivot, or zeroes the accumulated move.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// x:Name and every handler is wired programmatically in the constructor (the .axaml declares
    /// none). The self-contained behaviour is reproduced exactly:
    /// <list type="bullet">
    ///   <item>The snap-adjustment in metres (<see cref="_snapAdj"/>) is computed from
    ///         <c>ToolSettings.Default.setAS_snapDistance</c> precisely as the original
    ///         (<c>setAS_snapDistance * 0.01</c>), and the keypad-style <c>nudSnapDistance</c> button
    ///         shows the current centimetre value.</item>
    ///   <item><see cref="OnOkClick"/> (bntOk) closes the dialog.</item>
    /// </list>
    /// The actual track mutations operate on the FormGPS god-object (<c>mf.trk.NudgeTrack</c>,
    /// <c>SnapToPivot</c>, <c>NudgeDistanceReset</c>, the half-tool width from <c>mf.tool</c>) and the
    /// live offset readout reads <c>mf.trk.gArr[idx].nudgeDistance</c>. Those are host-owned at this
    /// checkpoint, so the dialog exposes typed event seams (<see cref="NudgeRequested"/>,
    /// <see cref="HalfToolNudgeRequested"/>, <see cref="SnapToPivotRequested"/>,
    /// <see cref="NudgeResetRequested"/>) that forward operator intent — and the keypad edit of the
    /// snap distance and the offset/units formatting remain host-owned. No fabricated calls to
    /// not-yet-projected services are made here — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormNudgeView : Window
    {
        // [XPLAT] Snap adjustment in METRES — identical derivation to the original FormNudge.
        private double _snapAdj;

        /// <summary>The snap adjustment in metres, derived from <c>ToolSettings.setAS_snapDistance</c>.</summary>
        public double SnapAdjustment => _snapAdj;

        /// <summary>
        /// [XPLAT] Raised for a left (negative) / right (positive) track nudge. The signed argument is
        /// the move in metres, replacing the original <c>mf.trk.NudgeTrack(±snapAdj)</c>.
        /// </summary>
        public event EventHandler<double> NudgeRequested;

        /// <summary>
        /// [XPLAT] Raised for a half-tool-width nudge. The argument is the direction sign (-1 left,
        /// +1 right); the host computes <c>(tool.width - tool.overlap) * 0.5 * sign</c> because the
        /// tool geometry lives on the FormGPS god-object.
        /// </summary>
        public event EventHandler<double> HalfToolNudgeRequested;

        /// <summary>[XPLAT] Raised to snap the track to the pivot (<c>mf.trk.SnapToPivot</c>).</summary>
        public event EventHandler SnapToPivotRequested;

        /// <summary>[XPLAT] Raised to zero the accumulated move (<c>mf.trk.NudgeDistanceReset</c>).</summary>
        public event EventHandler NudgeResetRequested;

        public FormNudgeView()
        {
            InitializeComponent();

            // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none).
            bthOK.Click += OnOkClick;
            btnAdjLeft.Click += OnAdjustLeftClick;
            btnAdjRight.Click += OnAdjustRightClick;
            btnHalfToolLeft.Click += OnHalfToolLeftClick;
            btnHalfToolRight.Click += OnHalfToolRightClick;
            btnSnapToPivot.Click += OnSnapToPivotClick;
            btnZeroMove.Click += OnZeroMoveClick;
        }

        // [XPLAT] FormEditTrack_Load: derive the snap adjustment and show the centimetre value.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            double snapDistanceCm = ToolSettings.Default.setAS_snapDistance;
            _snapAdj = snapDistanceCm * 0.01;
            nudSnapDistance.Content = ((int)snapDistanceCm).ToString(CultureInfo.InvariantCulture);
        }

        // [XPLAT] bntOk_Click: close the dialog.
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // [XPLAT] btnAdjLeft_Click: mf.trk.NudgeTrack(-snapAdj).
        private void OnAdjustLeftClick(object sender, RoutedEventArgs e)
        {
            NudgeRequested?.Invoke(this, -_snapAdj);
        }

        // [XPLAT] btnAdjRight_Click: mf.trk.NudgeTrack(snapAdj).
        private void OnAdjustRightClick(object sender, RoutedEventArgs e)
        {
            NudgeRequested?.Invoke(this, _snapAdj);
        }

        // [XPLAT] btnHalfToolLeft_Click: half tool width to the left.
        private void OnHalfToolLeftClick(object sender, RoutedEventArgs e)
        {
            HalfToolNudgeRequested?.Invoke(this, -1.0);
        }

        // [XPLAT] btnHalfToolRight_Click: half tool width to the right.
        private void OnHalfToolRightClick(object sender, RoutedEventArgs e)
        {
            HalfToolNudgeRequested?.Invoke(this, 1.0);
        }

        // [XPLAT] btnSnapToPivot_Click: mf.trk.SnapToPivot().
        private void OnSnapToPivotClick(object sender, RoutedEventArgs e)
        {
            SnapToPivotRequested?.Invoke(this, EventArgs.Empty);
        }

        // [XPLAT] btnZeroMove_Click: mf.trk.NudgeDistanceReset().
        private void OnZeroMoveClick(object sender, RoutedEventArgs e)
        {
            NudgeResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
