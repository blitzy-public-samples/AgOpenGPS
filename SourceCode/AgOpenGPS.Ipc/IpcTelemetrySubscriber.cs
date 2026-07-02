using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

namespace AgOpenGPS.Ipc
{
    /// <summary>
    /// The single, shared client-side driver for the AgIO <c>TelemetryService.StreamTelemetry</c>
    /// server-streaming subscription. Every IPC client (AgOpenGPS/GPS, AgDiag, GPS_Out, ModSim)
    /// runs its inbound telemetry loop through <see cref="RunAsync"/> so the connection retry /
    /// backoff schedule is guaranteed identical across all of them — eliminating the per-client
    /// duplication (and endpoint drift) that a copy-pasted loop invites.
    ///
    /// <para>
    /// Because AgOpenGPS auto-starts AgIO, the gRPC server socket may not yet be listening when a
    /// client first connects. The canonical, preserved schedule is exactly five attempts with
    /// per-attempt delays of 500 / 1000 / 2000 / 4000 / 8000 ms (base 500 ms, ×2 multiplier),
    /// i.e. delay(n) = 500 · 2^(n-1). The delay is applied after every failed attempt — including
    /// the fifth — so the total accumulated wait before the error is surfaced is
    /// 500 + 1000 + 2000 + 4000 + 8000 = 15500 ms, matching the RetryBackoffIpcTests contract.
    /// On exhaustion (or a non-transient failure) the error is handed to the caller's
    /// <c>onError</c> callback, which routes to each client's existing error surface.
    /// </para>
    ///
    /// <para>
    /// The stream is drained with an <see cref="IAsyncStreamReader{T}.MoveNext(CancellationToken)"/>
    /// loop rather than an <c>await foreach</c> async-stream, so this compiles under C# 7.3 for the
    /// net48 desktop clients that consume the netstandard2.0 target of this library.
    /// </para>
    /// </summary>
    public static class IpcTelemetrySubscriber
    {
        /// <summary>Maximum number of connection attempts before the error is surfaced.</summary>
        public const int MaxAttempts = 5;

        /// <summary>Base (first-attempt) backoff delay, in milliseconds. Doubles each attempt.</summary>
        public const int BaseDelayMs = 500;

        /// <summary>
        /// Connects to the AgIO gRPC host and drains the <c>StreamTelemetry</c> server-stream,
        /// retrying the connection with the canonical exponential backoff on a transient failure.
        /// </summary>
        /// <param name="onChannel">
        /// Invoked with the freshly created channel at the start of every attempt, so a client can
        /// build its command client / retain the channel for disposal. May be <c>null</c>.
        /// </param>
        /// <param name="onConnected">
        /// Invoked once per successful attempt when the stream opens (e.g. to set a connection flag
        /// or update UI). May be <c>null</c>.
        /// </param>
        /// <param name="onEnvelope">Invoked for every received <see cref="PgnEnvelope"/>. Required.</param>
        /// <param name="onError">
        /// Invoked once after all attempts are exhausted or on a non-transient failure. May be <c>null</c>.
        /// </param>
        /// <param name="token">Cancellation token used to stop the subscription (client shutdown).</param>
        /// <param name="onDisconnected">
        /// Invoked whenever the connection drops (before a backoff wait, and on a non-transient failure)
        /// so a client can clear its connection flag. May be <c>null</c>.
        /// </param>
        public static async Task RunAsync(
            Action<GrpcChannel>? onChannel,
            Action? onConnected,
            Action<PgnEnvelope> onEnvelope,
            Action<Exception>? onError,
            CancellationToken token,
            Action? onDisconnected = null)
        {
            if (onEnvelope == null)
            {
                throw new ArgumentNullException(nameof(onEnvelope));
            }

            Exception? lastError = null;

            for (int attempt = 0; attempt < MaxAttempts && !token.IsCancellationRequested; attempt++)
            {
                // Canonical per-attempt backoff: BaseDelayMs · 2^attempt => 500 / 1000 / 2000 / 4000 / 8000 ms.
                int delayMs = BaseDelayMs * (1 << attempt);
                GrpcChannel? channel = null;
                try
                {
                    channel = IpcChannelFactory.CreateChannel();
                    onChannel?.Invoke(channel);

                    TelemetryService.TelemetryServiceClient client =
                        new TelemetryService.TelemetryServiceClient(channel);

                    using (var call = client.StreamTelemetry(new Empty(), cancellationToken: token))
                    {
                        onConnected?.Invoke();

                        // MoveNext loop (C# 7.3 compatible — no async-stream/IAsyncEnumerable dependency).
                        while (await call.ResponseStream.MoveNext(token).ConfigureAwait(false))
                        {
                            onEnvelope(call.ResponseStream.Current);
                        }
                    }

                    // Server closed the stream cleanly — normal termination.
                    return;
                }
                catch (OperationCanceledException)
                {
                    return; // cooperative shutdown requested
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && token.IsCancellationRequested)
                {
                    return; // cancellation surfaced as RpcException — normal shutdown
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    // Server not yet listening (AgIO auto-start race) or a transient stream drop.
                    lastError = ex;
                    onDisconnected?.Invoke();
                    TryDispose(channel);

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        // Wait the per-attempt delay after EVERY failed attempt, including the fifth, so the
                        // total accumulated wait before surfacing is exactly 15500 ms (canonical schedule).
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // cancelled while backing off — normal shutdown
                    }
                }
                catch (Exception ex)
                {
                    // Non-transient failure: surface immediately (retrying would not help).
                    onDisconnected?.Invoke();
                    TryDispose(channel);

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    onError?.Invoke(ex);
                    return;
                }
            }

            // All five transient attempts (and their five backoff delays) are exhausted — surface the error.
            if (!token.IsCancellationRequested)
            {
                onError?.Invoke(lastError ?? new TimeoutException(
                    "gRPC IPC connection to " + IpcConstants.AgIoSocketPath + " failed after " +
                    MaxAttempts + " attempts."));
            }
        }

        /// <summary>
        /// A connect/stream failure is transient (retryable) when the server is not yet listening —
        /// AgIO is auto-started by AgOpenGPS so a startup race is expected.
        /// </summary>
        private static bool IsTransient(Exception ex)
        {
            return (ex is RpcException rpc && rpc.StatusCode == StatusCode.Unavailable)
                || ex is SocketException
                || ex is IOException;
        }

        /// <summary>Best-effort disposal of a failed attempt's channel before the next retry.</summary>
        private static void TryDispose(GrpcChannel? channel)
        {
            try
            {
                channel?.Dispose();
            }
            catch
            {
                // Disposal is best-effort; a failure here must not mask the connection error being handled.
            }
        }
    }
}
