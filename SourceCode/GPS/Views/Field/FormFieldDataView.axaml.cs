// [XPLAT] migrated from net48/WinForms (Forms/Field/FormFieldData.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "System Data" field-work statistics readout — a port of the
    /// WinForms <c>FormFieldData</c> (FormFieldData.cs + FormFieldData.Designer.cs). It is a narrow,
    /// modeless panel that displays live field-work statistics (worked / applied area, remaining area
    /// and time, overlap %, work rate and trip area / distance), refreshed once per second.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings). The single declared handler
    /// <see cref="btnTripReset_Click"/> is wired from the .axaml.
    /// <para>
    /// The original per-second <c>timer1_Tick</c> read every value from the live field-data model on
    /// the FormGPS god-object (<c>mf.fd.*</c>, <c>mf.isMetric</c>, <c>mf.bnd.bndList</c>) and toggled
    /// the boundary-gated "Remain" group. That model is not projected at this checkpoint, so the live
    /// refresh and "Remain" gating are host-owned: the host attaches its own once-per-second update to
    /// the x:Name'd value labels and shows/hides the "Remain" group — see
    /// MIGRATION_DOCS/TRANSITION_MAP.md. The static captions remain as the verbatim Designer text so
    /// the panel reads correctly before/without translation.
    /// </para>
    /// <para>
    /// The trip-reset is surfaced as the thin adapter event <see cref="TripResetRequested"/> rather
    /// than mutating the (absent) <c>mf.fd</c> directly: the host subscribes and performs
    /// <c>mf.fd.workedAreaTotalUser = 0; mf.fd.distanceUser = 0;</c>, faithfully reproducing the
    /// original <c>btnTripReset_Click</c> without fabricating a call to a service that does not yet
    /// exist.
    /// </para>
    /// </remarks>
    public partial class FormFieldDataView : Window
    {
        /// <summary>
        /// Raised when the operator presses "Trip Reset". The host resets the trip totals on the
        /// field-data model (<c>mf.fd.workedAreaTotalUser = 0; mf.fd.distanceUser = 0;</c>).
        /// </summary>
        public event EventHandler TripResetRequested;

        public FormFieldDataView()
        {
            InitializeComponent();
        }

        // [XPLAT] btnTripReset_Click: raise the reset request for the host to apply to mf.fd.
        private void btnTripReset_Click(object sender, RoutedEventArgs e)
        {
            TripResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
