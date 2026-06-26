// [XPLAT] migrated from net48/WinForms (Forms/FormShiftPos.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] GPS antenna position-shift (drift compensation) dialog.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormShiftPos</c> (FormShiftPos.cs +
    /// .Designer.cs). The WinForms form let the operator nudge a north/east offset (centimetres) via
    /// four arrow buttons stepping two numeric fields, zero them, toggle a "keep offsets" checkbox,
    /// and apply on OK.
    ///
    /// The self-contained UI mechanics are ported here: the four arrow buttons step
    /// <c>nudNorth</c> / <c>nudEast</c> by their increment (parity with the WinForms
    /// <c>UpButton()</c>/<c>DownButton()</c>), <c>btnZero</c> resets both fields, the checkbox
    /// updates its own On/Off caption, and OK closes the dialog.
    ///
    /// Two behaviours read/write the live field model and are therefore deferred to that wiring rather
    /// than fabricated here: <c>SetFixDelta</c> writes
    /// <c>mf.AppModel.SharedFieldProperties.DriftCompensation</c> and the load path reads it back
    /// (plus <c>mf.isKeepOffsetsOn</c>); and the numeric-field tap opened the on-screen keypad via
    /// <c>mf</c>. The OK button's <c>mf.isKeepOffsetsOn</c> persistence belongs to that same field-model
    /// wiring. The dialog's own close is wired so it is always dismissible.
    ///
    /// The XAML declares only x:Name (no handlers), so all events are subscribed in code.
    /// </summary>
    public partial class FormShiftPosView : Window
    {
        public FormShiftPosView()
        {
            InitializeComponent();

            btnNorth.Click += OnNorthClick;
            btnSouth.Click += OnSouthClick;
            btnWest.Click += OnWestClick;
            btnEast.Click += OnEastClick;
            btnZero.Click += OnZeroClick;
            bntOK.Click += OnOkClick;
            chkOffsetsOn.Checked += OnOffsetsToggled;
            chkOffsetsOn.Unchecked += OnOffsetsToggled;

            // [XPLAT] Initialise the On/Off caption to match the checkbox's initial state (parity with
            // the WinForms load path which set the text from chkOffsetsOn.Checked).
            UpdateOffsetsCaption();
        }

        // [XPLAT] btnNorth -> step nudNorth up (parity with nudNorth.UpButton()).
        private void OnNorthClick(object sender, RoutedEventArgs e)
        {
            nudNorth.Value = (nudNorth.Value ?? 0) + nudNorth.Increment;
        }

        // [XPLAT] btnSouth -> step nudNorth down (parity with nudNorth.DownButton()).
        private void OnSouthClick(object sender, RoutedEventArgs e)
        {
            nudNorth.Value = (nudNorth.Value ?? 0) - nudNorth.Increment;
        }

        // [XPLAT] btnWest -> step nudEast down (parity with nudEast.DownButton()).
        private void OnWestClick(object sender, RoutedEventArgs e)
        {
            nudEast.Value = (nudEast.Value ?? 0) - nudEast.Increment;
        }

        // [XPLAT] btnEast -> step nudEast up (parity with nudEast.UpButton()).
        private void OnEastClick(object sender, RoutedEventArgs e)
        {
            nudEast.Value = (nudEast.Value ?? 0) + nudEast.Increment;
        }

        // [XPLAT] btnZero -> zero both fields (parity with FormShiftPos.btnZero_Click; the SetFixDelta
        // write to the field model is deferred to the field-model wiring as documented above).
        private void OnZeroClick(object sender, RoutedEventArgs e)
        {
            nudEast.Value = 0;
            nudNorth.Value = 0;
        }

        // [XPLAT] OK -> close (parity with FormShiftPos.bntOK_Click's Close(); the mf.isKeepOffsetsOn
        // persistence and SetFixDelta apply are deferred to the field-model wiring).
        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // [XPLAT] chkOffsetsOn -> refresh the On/Off caption (parity with chkOffsetsOn_Click).
        private void OnOffsetsToggled(object sender, RoutedEventArgs e)
        {
            UpdateOffsetsCaption();
        }

        private void UpdateOffsetsCaption()
        {
            chkOffsetsOn.Content = chkOffsetsOn.IsChecked == true ? "On" : "Off";
        }
    }
}
