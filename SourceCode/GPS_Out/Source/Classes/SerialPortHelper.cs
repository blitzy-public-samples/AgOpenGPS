// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.IO.Ports;

namespace GPS_Out
{
    /// <summary>
    /// [XPLAT] GPS_Out-local serial port-name enumeration. Wraps the cross-platform
    /// <see cref="SerialPort.GetPortNames()"/> (COMx on Windows; /dev/ttyUSB*, /dev/ttyACM*
    /// on Linux; /dev/cu.* on macOS). Intentionally NOT dependent on
    /// AgOpenGPS.Core.IPlatformServices — GPS_Out is a standalone program.
    /// Feeds SerialSend.SerialPortExists and the Views/MainWindow port combobox.
    /// </summary>
    // [XPLAT] internal (not public): R7 limits new public architectural surface to
    // AgOpenGPS.Core.IPlatformServices + the OpenGL host adapter. GPS_Out is a standalone
    // program, so its serial port-name enumeration stays an internal implementation detail.
    internal static class SerialPortHelper
    {
        /// <summary>Returns the available serial port names for the current OS.</summary>
        public static string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }
    }
}
