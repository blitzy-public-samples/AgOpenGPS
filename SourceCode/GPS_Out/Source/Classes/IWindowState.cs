// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using Avalonia;

namespace GPS_Out
{
    /// <summary>
    /// [XPLAT] Cross-platform abstraction of an Avalonia window's persisted geometry,
    /// replacing the WinForms <c>System.Windows.Forms.Form</c> coupling formerly used by
    /// <see cref="clsTools.LoadFormData"/> / <see cref="clsTools.SaveFormData"/>.
    /// Implemented by the GPS_Out Avalonia windows (e.g. MainWindow). The persisted keys
    /// remain <c>Name + ".Left"</c> / <c>Name + ".Top"</c> so saved geometry round-trips.
    /// </summary>
    public interface IWindowState
    {
        /// <summary>Stable name used as the settings-key prefix (matches the old WinForms Form.Name).</summary>
        string Name { get; }

        /// <summary>Top-left position in screen pixels. Settable so clsTools can restore saved geometry.</summary>
        PixelPoint Position { get; set; }

        /// <summary>Client size of the window (available for future persistence; current code persists position only).</summary>
        Size ClientSize { get; }
    }
}
