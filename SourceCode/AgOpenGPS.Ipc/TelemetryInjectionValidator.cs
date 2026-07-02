namespace AgOpenGPS.Ipc
{
    /// <summary>
    /// Validates a locally-injected <see cref="PgnEnvelope"/> before the AgIO
    /// <c>CommandService.InjectTelemetry</c> RPC fans it out to every telemetry subscriber
    /// (AAP 0.6.3 — ModSim acts as a client that injects simulated inbound frames).
    ///
    /// <para>
    /// The accept/reject decision is extracted into the shared <c>AgOpenGPS.Ipc</c> contract
    /// assembly (mirroring <see cref="CommandFrameBuilder"/>) so it can be unit-tested directly
    /// from the net48 test project, which cannot reference the AgIO WinExe host. The actual
    /// fan-out (<c>TelemetryServiceImpl.Broadcast</c>) remains in AgIO; only the validation
    /// contract lives here.
    /// </para>
    /// </summary>
    // IPC-REFACTOR: new shared helper introduced by the UDP->gRPC IPC migration; extracts the InjectTelemetry
    // accept/reject predicate out of the AgIO WinExe so the injection contract is unit-testable from the net48
    // test project (AAP 0.6.3 ModSim injection path).
    public static class TelemetryInjectionValidator
    {
        /// <summary>
        /// Determines whether <paramref name="request"/> is a valid injection that may be
        /// broadcast to subscribers. A malformed or default-version injection must never be
        /// fanned out: an unrecognized <c>schema_version</c> guards against a client built
        /// against an incompatible contract, and an unset payload (<c>oneof</c> not populated)
        /// carries nothing to deliver.
        /// </summary>
        /// <param name="request">The envelope a client asked AgIO to inject and broadcast.</param>
        /// <param name="errorMessage">
        /// On rejection, a human-readable reason (also returned in the <c>CommandAck</c>);
        /// otherwise an empty string.
        /// </param>
        /// <returns><c>true</c> when the envelope is valid and may be broadcast; otherwise <c>false</c>.</returns>
        public static bool TryValidate(PgnEnvelope request, out string errorMessage)
        {
            if (request == null)
            {
                errorMessage = "InjectTelemetry rejected: request was null.";
                return false;
            }

            if (request.SchemaVersion != IpcConstants.SchemaVersion)
            {
                errorMessage = "InjectTelemetry rejected: unsupported schema version " +
                    request.SchemaVersion + " (expected " + IpcConstants.SchemaVersion + ").";
                return false;
            }

            if (request.PayloadCase == PgnEnvelope.PayloadOneofCase.None)
            {
                errorMessage = "InjectTelemetry rejected: envelope carries no payload.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
