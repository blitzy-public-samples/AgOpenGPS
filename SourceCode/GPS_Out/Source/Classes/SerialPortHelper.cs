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
    public static class SerialPortHelper
    {
        /// <summary>Returns the available serial port names for the current OS.</summary>
        public static string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }
    }
}
