using System.Runtime.InteropServices;

namespace AgOpenGPS.Ipc
{
    /// <summary>Shared IPC transport constants for the AgIO &lt;-&gt; AgOpenGPS gRPC contract.</summary>
    public static class IpcConstants
    {
        /// <summary>Proto schema_version stamped on every PgnEnvelope. Mirrors docs/proto/SCHEMA_VERSIONING.md.</summary>
        public const uint SchemaVersion = 1;

        /// <summary>
        /// Loopback-only gRPC endpoint. Linux/macOS: Unix domain socket; Windows: named pipe.
        /// Never externally bound (parity with the legacy UDP loopback-only posture).
        /// </summary>
        public static string AgIoSocketPath =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"\\.\pipe\agopengps_ipc"
                : "/tmp/agopengps_ipc.sock";
    }
}
