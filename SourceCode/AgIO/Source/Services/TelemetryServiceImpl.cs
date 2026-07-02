using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgOpenGPS.Ipc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace AgIO
{
    /// <summary>
    /// Server-streaming gRPC service that fans inbound telemetry envelopes out to
    /// every connected subscriber. This replaces the legacy UDP broadcast to
    /// 127.255.255.255:17777, where a single datagram was observed by all loopback
    /// listeners, with one in-process channel per subscriber.
    /// </summary>
    public class TelemetryServiceImpl : TelemetryService.TelemetryServiceBase
    {
        // Thread-safe registry of per-subscriber channels. Static so the single
        // hardware-ingestion producer (Forms/UDP.designer.cs SendToLoopBackMessageAOG)
        // and every StreamTelemetry reader instance created per call by the ASP.NET
        // Core dependency-injection container share one registry.
        private static readonly ConcurrentDictionary<Guid, Channel<PgnEnvelope>> subscribers
            = new ConcurrentDictionary<Guid, Channel<PgnEnvelope>>();

        // Serializes the per-channel write loop so the SingleWriter invariant holds
        // even though the broadcast producer may run on more than one thread (the UI
        // thread and the serial DataReceived thread).
        private static readonly object broadcastLock = new object();

        /// <summary>
        /// Server-streaming RPC. Registers a per-subscriber channel and drains it to
        /// the response stream until the client disconnects.
        /// </summary>
        public override async Task StreamTelemetry(Empty request,
            IServerStreamWriter<PgnEnvelope> responseStream, ServerCallContext context)
        {
            // IPC-REFACTOR: per-subscriber fan-out channel is BOUNDED with FullMode=DropOldest — the
            // fallback the AAP (§0.3.4) documents "should a permanently wedged reader be observed". This
            // keeps the hardware-ingestion writer fully non-blocking (DropOldest makes TryWrite always
            // succeed, never back-pressuring the producer or other subscribers) AND caps memory at
            // IpcConstants.TelemetrySubscriberChannelCapacity envelopes even under an indefinitely stalled
            // subscriber. When full, the oldest (already-superseded) telemetry is discarded first, matching
            // the legacy UDP best-effort, self-superseding semantics (a new GPS fix arrives within ~70 ms).
            Channel<PgnEnvelope> channel = Channel.CreateBounded<PgnEnvelope>(
                new BoundedChannelOptions(IpcConstants.TelemetrySubscriberChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleWriter = true,
                    SingleReader = true
                });

            Guid id = Subscribe(channel);

            try
            {
                await foreach (PgnEnvelope envelope in channel.Reader.ReadAllAsync(context.CancellationToken))
                {
                    await responseStream.WriteAsync(envelope);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected. This is normal termination, nothing to log.
            }
            finally
            {
                Unsubscribe(id);
            }
        }

        /// <summary>
        /// Broadcasts one envelope to every subscriber. Non-blocking: a slow or stalled
        /// subscriber never back-pressures the hardware ingestion thread or the other
        /// subscribers, because each channel is bounded with <c>FullMode = DropOldest</c>,
        /// so <see cref="ChannelWriter{T}.TryWrite"/> always succeeds without blocking and a
        /// stalled subscriber's backlog is capped at
        /// <see cref="IpcConstants.TelemetrySubscriberChannelCapacity"/> envelopes (oldest,
        /// already-superseded telemetry dropped first). Called by the hardware bridge
        /// (Forms/UDP.designer.cs SendToLoopBackMessageAOG) and by CommandServiceImpl.InjectTelemetry.
        /// </summary>
        public static void Broadcast(PgnEnvelope envelope)
        {
            lock (broadcastLock)
            {
                foreach (Channel<PgnEnvelope> channel in subscribers.Values)
                {
                    channel.Writer.TryWrite(envelope);
                }
            }
        }

        /// <summary>
        /// Registers a subscriber channel and returns its key.
        /// </summary>
        private static Guid Subscribe(Channel<PgnEnvelope> channel)
        {
            Guid id = Guid.NewGuid();
            subscribers[id] = channel;
            return id;
        }

        /// <summary>
        /// Removes a subscriber channel and completes its writer so the reader loop ends.
        /// </summary>
        private static void Unsubscribe(Guid id)
        {
            if (subscribers.TryRemove(id, out Channel<PgnEnvelope> channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
