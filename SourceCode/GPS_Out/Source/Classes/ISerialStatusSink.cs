// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
namespace GPS_Out
{
    /// <summary>
    /// [XPLAT] Decouples <see cref="SerialSend"/> from the deleted WinForms <c>frmStart</c>:
    /// replaces the <c>mf.SetPortButtons1()</c> back-reference. The Avalonia view-model /
    /// MainWindow implements this to refresh the serial-port indicator when the port state
    /// changes (open/close/watchdog).
    /// </summary>
    // [XPLAT] internal (not public): R7 limits new public architectural surface to
    // AgOpenGPS.Core.IPlatformServices + the OpenGL host adapter. This GPS_Out-local
    // decoupling contract is an internal implementation detail of the standalone app.
    internal interface ISerialStatusSink
    {
        /// <summary>Called when the serial port state changes so the UI can refresh its port indicator.</summary>
        void OnPortStateChanged();
    }
}
