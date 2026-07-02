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
            // KNOWN DEVIATION (AAP 0.1.3 / 0.6.4 — Authority-Hierarchy Case 3, surfaced not silently worked
            // around): the loopback UDS/named-pipe HTTP/2 transport requires SocketsHttpHandler.ConnectCallback,
            // a .NET Core 2.1+ / .NET 6+ API that does NOT exist on the netstandard2.0 surface the net48 desktop
            // clients consume. Retargeting the client hosts to .NET 6+ is the gating prerequisite for this
            // transport. Until then, surface the limitation explicitly (rather than binding a non-loopback or
            // otherwise incorrect endpoint): the client's connect retry/backoff routes this to the existing
            // error path. The endpoint contract itself is preserved via IpcConstants.AgIoSocketPath so behaviour
            // is identical the moment the host is retargeted.
            throw new PlatformNotSupportedException(
                "gRPC over the loopback " + IpcConstants.AgIoSocketPath + " endpoint requires .NET 6+ " +
                "(SocketsHttpHandler.ConnectCallback). The net48 client host must be retargeted to .NET 6+ " +
                "(AAP 0.1.3 known deviation) before the IPC transport can connect.");
#endif
        }
    }
}
