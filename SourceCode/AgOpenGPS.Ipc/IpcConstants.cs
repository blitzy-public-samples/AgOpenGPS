using System.Runtime.InteropServices;

namespace AgOpenGPS.Ipc
{
    /// <summary>Shared IPC transport constants for the AgIO &lt;-&gt; AgOpenGPS gRPC contract.</summary>
    public static class IpcConstants
    {
        /// <summary>Proto schema_version stamped on every PgnEnvelope. Mirrors docs/proto/SCHEMA_VERSIONING.md.</summary>
        public const uint SchemaVersion = 1;

        // IPC-REFACTOR: bound for the per-subscriber fan-out channel — introduced so the fan-out is
        // bounded (DropOldest) rather than unbounded, capping memory under an indefinitely stalled
        // subscriber (AAP §0.3.4 documented fallback). Consumed by AgIO's TelemetryServiceImpl.
        /// <summary>
        /// Capacity of each per-subscriber telemetry fan-out channel in AgIO's <c>TelemetryService</c>.
        /// The channel is <b>bounded</b> (with <c>FullMode = DropOldest</c>) so that a permanently
        /// stalled subscriber cannot grow memory without limit, while the hardware-ingestion writer
        /// stays fully non-blocking (AAP §0.3.4's documented fallback "should a permanently wedged
        /// reader be observed"). Sized well above the sustained ~14 Hz GPS-fix cadence (and the other
        /// PGN traffic) so normal transient bursts are never dropped; when a subscriber does stall,
        /// the oldest — already-superseded — telemetry is discarded first, matching the legacy UDP
        /// best-effort, self-superseding semantics.
        /// </summary>
        public const int TelemetrySubscriberChannelCapacity = 1024;

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
