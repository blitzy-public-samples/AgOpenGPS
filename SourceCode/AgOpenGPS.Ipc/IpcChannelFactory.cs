using System;
#if NET8_0_OR_GREATER
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
#endif
using Grpc.Net.Client;

namespace AgOpenGPS.Ipc
{
    /// <summary>
    /// Builds the single, loopback-only <see cref="GrpcChannel"/> that every AgOpenGPS IPC client
    /// (AgOpenGPS/GPS, AgDiag, GPS_Out, ModSim) uses to reach the AgIO gRPC host. Centralizing the
    /// channel construction here — rather than duplicating it in each client — guarantees an
    /// identical transport contract across all clients: the OS-branched endpoint is always derived
    /// from <see cref="IpcConstants.AgIoSocketPath"/> (never a hard-coded literal), so a client can
    /// never drift from the server's bind address.
    ///
    /// <para>
    /// The transport is loopback only, with no TLS and no authentication, exactly matching the
    /// legacy UDP loopback security posture: a Unix domain socket on Linux/macOS and a named pipe
    /// on Windows, carrying plaintext HTTP/2. It is never bound to an external interface.
    /// </para>
    /// </summary>
    public static class IpcChannelFactory
    {
        /// <summary>
        /// Creates a <see cref="GrpcChannel"/> connected to the AgIO gRPC host over the loopback
        /// Unix-domain-socket (Linux/macOS) or named-pipe (Windows) endpoint defined by
        /// <see cref="IpcConstants.AgIoSocketPath"/>.
        /// </summary>
        /// <returns>A ready-to-use loopback gRPC channel.</returns>
        public static GrpcChannel CreateChannel()
        {
#if NET8_0_OR_GREATER
            // .NET 6+ path: SocketsHttpHandler.ConnectCallback dials the loopback UDS / named pipe
            // directly and hands the resulting stream to gRPC's HTTP/2 stack. "http://localhost" is a
            // dummy, unused plaintext authority — the ACTUAL endpoint is the one opened in the callback.
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Windows loopback transport: named pipe. Kestrel/ListenNamedPipe and the client
                        // both derive the name from IpcConstants.AgIoSocketPath; strip the \\.\pipe\ prefix
                        // to obtain the bare pipe name the NamedPipeClientStream ctor expects.
                        string socketPath = IpcConstants.AgIoSocketPath;
                        string pipeName = socketPath.StartsWith(@"\\.\pipe\", StringComparison.Ordinal)
                            ? socketPath.Substring(@"\\.\pipe\".Length)
                            : socketPath;
                        NamedPipeClientStream pipe = new NamedPipeClientStream(
                            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                        return pipe;
                    }

                    // Linux/macOS loopback transport: Unix domain socket at IpcConstants.AgIoSocketPath.
                    Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(
                        new UnixDomainSocketEndPoint(IpcConstants.AgIoSocketPath),
                        cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };

            return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
#else
            // IPC-REFACTOR / AAP 0.1.3 & 0.6.4 (Authority-Hierarchy Case 3) — the RESIDUAL #1 DEVIATION.
            //
            // This branch is compiled into the netstandard2.0 facet of AgOpenGPS.Ipc, which is consumed by ALL of
            // the .NET Framework (net48) IPC clients — AgOpenGPS/GPS, AgDiag, GPS_Out and ModSim. Per the AAP 0.4.1
            // file-by-file plan the ONLY project that retargets to .NET 6+ is AgIO (the gRPC SERVER host, which
            // needs ASP.NET Core / Kestrel gRPC hosting); that single retarget is the accepted #1 deviation. Every
            // CLIENT stays on net48 (AAP 0.5.2: clients add only Grpc.Net.Client + the AgOpenGPS.Ipc ProjectReference).
            //
            // The loopback UDS/named-pipe plaintext-HTTP/2 transport built in the #if branch above requires
            // SocketsHttpHandler.ConnectCallback, which does NOT exist on the netstandard2.0 surface these net48
            // clients consume. There is consequently no functional loopback transport on net48: this is the
            // DOCUMENTED, SURFACED host-retarget prerequisite — a client must itself run on .NET 6+ for this endpoint
            // to connect — deliberately NOT worked around by silently retargeting the clients (Authority-Hierarchy
            // Case 3: surface deviations, do not silently implement). GPS additionally cannot be retargeted at all in
            // this repository: it depends on System.Management (Source\Classes\CBrightness.cs) and
            // System.Windows.Forms.DataVisualization.Charting (the FormGraph*/FormCorrection chart forms), both in
            // OUT-OF-SCOPE files with no available .NET 6+ replacement.
            //
            // Rather than throwing here — which would pre-empt the client's connect retry/backoff entirely (a throw
            // is classified non-transient and would surface before a single StreamTelemetry attempt) — we return a
            // REAL channel so the shared 5-attempt / 500-1000-2000-4000-8000 ms retry schedule in
            // IpcTelemetrySubscriber actually runs and any failure is surfaced through each client's EXISTING error
            // path (GPS: FormDialog + Log.EventWriter; ModSim: MessageBox; AgDiag/GPS_Out: their logging) — identical
            // UX to an unreachable server, with no gratuitous crash. On the .NET 6+ target the prompt designates (and
            // that AgIO already targets), this endpoint connects. The dummy "http://localhost" authority mirrors the
            // .NET 6+ path (the authoritative endpoint contract remains IpcConstants.AgIoSocketPath).
            return GrpcChannel.ForAddress("http://localhost");
#endif
        }
    }
}
