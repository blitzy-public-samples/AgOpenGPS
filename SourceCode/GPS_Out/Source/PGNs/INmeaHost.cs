// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using GPS_Out.PGNs;

namespace GPS_Out
{
    /// <summary>
    /// Abstraction over the former WinForms host (frmStart) for the NMEA/PGN classes.
    /// Exposes only the members the PGN parsers/builders require, decoupling them from
    /// the deleted Form1/frmStart. Implemented by the Avalonia view-model.
    /// </summary>
    /// <remarks>
    /// [XPLAT] This is the decoupling seam introduced by the net48/WinForms → net8.0
    /// cross-platform migration. The seven PGN types in this folder (PGN100, PGN54908,
    /// PGN_GGA, PGN_RMC, PGN_VTG, PGN_GSA, PGN_ZDA) hold their host back-reference as an
    /// <c>INmeaHost</c> instead of the concrete window, so the byte-frozen protocol logic
    /// no longer depends on any UI type. The surface is intentionally minimal (AAP §0.7.1):
    /// it contains exactly the members those classes consume and nothing speculative.
    /// </remarks>
    public interface INmeaHost
    {
        /// <summary>
        /// Shared helper instance providing CRC validation (<c>GoodCRC</c>) and error logging
        /// (<c>WriteErrorLog</c>) used by the inbound PGN parsers. Read-only; the host owns the instance.
        /// </summary>
        clsTools Tls { get; }

        /// <summary>
        /// Parsed corrected-position data (AOG PGN 100) — exposes connectivity, latitude/longitude,
        /// and the optional fix-to-fix heading consumed by the GGA/RMC sentence builders. Read-only.
        /// </summary>
        PGN100 AOGdata { get; }

        /// <summary>
        /// Parsed navigation/IMU data (AGIO PGN 54908) — exposes position, speed, heading, fix quality,
        /// satellite count, HDOP, altitude, and age consumed by the GGA/RMC/VTG/GSA builders. Read-only.
        /// </summary>
        PGN54908 AGIOdata { get; }

        /// <summary>
        /// Computes the NMEA-0183 XOR checksum for the supplied sentence (between the leading
        /// <c>'$'</c> and the <c>'*'</c> terminator). Behaviour is frozen — identical bytes are produced
        /// on every platform.
        /// </summary>
        /// <param name="Data">The NMEA sentence whose checksum is required.</param>
        /// <returns>The XOR checksum of the sentence payload.</returns>
        int CheckSum(string Data);

        /// <summary>
        /// Returns the current heading (in degrees) selected by the host from the available
        /// dual/fix-to-fix/IMU sources, used by the RMC and VTG sentence builders.
        /// </summary>
        /// <returns>The heading in degrees.</returns>
        double Heading();
    }
}
