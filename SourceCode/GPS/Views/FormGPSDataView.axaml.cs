// [XPLAT] migrated from net48/WinForms (Forms/FormGPSData.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Live GPS data read-out window.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormGPSData</c> (FormGPSData.cs +
    /// FormGPSData.Designer.cs). The WinForms form was a passive monitor: every one of its labels
    /// (<c>lblLatitude</c>, <c>lblLongitude</c>, <c>lblNorthingField</c>, <c>lblEastingField</c>,
    /// <c>lblAltitude</c>, <c>lblSatsTracked</c>, <c>lblHDOP</c>, <c>lblFrameTime</c>,
    /// <c>lblTimeSlice</c>, <c>lblHz</c>, <c>lbludpWatchCounts</c>, <c>lblFix2FixHeading</c>,
    /// <c>lblIMUHeading</c>, <c>lblFuzeHeading</c>, <c>lblAngularVelocity</c>) was refreshed on a
    /// timer that read fields off the <c>FormGPS</c> instance (<c>mf.frameTime</c>,
    /// <c>mf.pn.fix</c>, <c>mf.gpsHz</c>, <c>mf.ahrs.imuYawRate</c>, …). The form had no buttons and
    /// no user input — its sole behaviour was that read-out refresh, plus clearing
    /// <c>mf.isGPSSentencesOn</c> on close.
    ///
    /// At this checkpoint the live read-out source — the FormGPS scan-loop / PositionService and the
    /// AHRS fusion pipeline — is not yet wired into the Avalonia shell (it is extracted in a later
    /// migration step, per the AAP's "Extract Class / Move Method" plan). The read-out values are
    /// therefore intentionally NOT populated here rather than fabricated against services that do
    /// not yet exist. This view is the thin Avalonia adapter: it owns the parameterless constructor
    /// and <c>InitializeComponent()</c> the XAML loader requires and exposes the x:Name'd labels for
    /// the position pipeline to drive once that pipeline is connected. There are no declared event
    /// handlers to implement (the WinForms form had none).
    /// </summary>
    public partial class FormGPSDataView : Window
    {
        public FormGPSDataView()
        {
            InitializeComponent();
        }
    }
}
