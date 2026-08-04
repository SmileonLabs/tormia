using System.Threading.Channels;
using Tormia.Ontology.Realtime.Protocol;

internal sealed record PendingAuthorityUdpSnapshotTarget(
    AuthorityUdpPeerTarget Target);

internal sealed record PendingAuthorityUdpSnapshotBatch(
    IReadOnlyList<PendingAuthorityUdpSnapshotTarget> Targets,
    IReadOnlyList<AuthorityMotionSnapshotPayload> Pages,
    long AuthorityFrameTick,
    int EstimatedBytes);

/// <summary>
/// Bounded handoff into the LiteNetLib listener. TryWrite deliberately drops
/// the newest packet when the listener is behind; motion snapshots are
/// replaceable and must never stall the Authority fixed tick.
/// </summary>
internal sealed class AuthorityUdpOutboundSnapshotChannel
{
    private readonly Channel<PendingAuthorityUdpSnapshotBatch> channel;
    private readonly int maximumQueuedBytes;
    private long queuedBytes;

    internal AuthorityUdpOutboundSnapshotChannel(
        int capacity,
        int maximumQueuedBytes)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumQueuedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumQueuedBytes));
        this.maximumQueuedBytes = maximumQueuedBytes;
        channel = Channel.CreateBounded<PendingAuthorityUdpSnapshotBatch>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
    }

    internal bool TryWrite(PendingAuthorityUdpSnapshotBatch value)
    {
        if (value.EstimatedBytes <= 0 ||
            value.EstimatedBytes > maximumQueuedBytes)
            return false;
        while (true)
        {
            var prior = Volatile.Read(ref queuedBytes);
            if (prior > maximumQueuedBytes - value.EstimatedBytes)
                return false;
            if (Interlocked.CompareExchange(
                    ref queuedBytes,
                    prior + value.EstimatedBytes,
                    prior) == prior)
                break;
        }
        if (channel.Writer.TryWrite(value)) return true;
        Interlocked.Add(ref queuedBytes, -value.EstimatedBytes);
        return false;
    }

    internal ChannelReader<PendingAuthorityUdpSnapshotBatch> Reader => channel.Reader;

    internal void Release(PendingAuthorityUdpSnapshotBatch value) =>
        Interlocked.Add(ref queuedBytes, -value.EstimatedBytes);

    internal void Complete() => channel.Writer.TryComplete();
}

internal static class AuthorityUdpMotionSnapshotBatcher
{
    // LiteNetLib starts peers at a 1024-byte packet MTU before discovery.
    // Keep the protocol datagram below that packet budget (including the
    // LiteNetLib header) so early snapshots do not depend on MTU discovery.
    internal const int MaximumSnapshotDatagramLength = 1000;
    private static readonly RealtimePacketMetadata MeasurementMetadata = new(
        Guid.Parse("01010101-0101-0101-0101-010101010101"),
        1,
        1,
        1);

    internal static IReadOnlyList<AuthorityMotionSnapshotPayload> Create(
        WorldZoneMotionFrame? frame,
        out int rejectedItems)
    {
        rejectedItems = 0;
        if (frame is null ||
            frame.FrameOccurrenceId == Guid.Empty ||
            frame.WorldId == Guid.Empty ||
            !SemanticId.IsValid(frame.ZoneKey) ||
            frame.ServerTick <= 0 ||
            frame.ObservedAtUnixMilliseconds <= 0 ||
            frame.Items.Count == 0)
        {
            return Array.Empty<AuthorityMotionSnapshotPayload>();
        }

        var items = frame.Items
            .OrderBy(value => value.AvatarEntityId)
            .Select(TryMap)
            .ToArray();
        var pageItems = new List<AuthorityMotionSnapshotItem[]>();
        var current = new List<AuthorityMotionSnapshotItem>(
            RealtimeWireContract.MaximumSnapshotItemsParseBound);
        foreach (var item in items)
        {
            if (item is null)
            {
                rejectedItems++;
                continue;
            }

            var candidate = current.Append(item).ToArray();
            if (Fits(frame, candidate))
            {
                current.Add(item);
                continue;
            }

            if (current.Count > 0)
            {
                pageItems.Add(current.ToArray());
                current.Clear();
            }
            if (Fits(frame, new[] { item }))
            {
                current.Add(item);
            }
            else
            {
                rejectedItems++;
            }
        }

        if (current.Count > 0)
            pageItems.Add(current.ToArray());
        var totalItems = pageItems.Sum(page => page.Length);
        if (totalItems == 0 ||
            totalItems > RealtimeWireContract.MaximumSnapshotTotalItems ||
            pageItems.Count > RealtimeWireContract.MaximumSnapshotPageCount)
        {
            rejectedItems += totalItems;
            return Array.Empty<AuthorityMotionSnapshotPayload>();
        }
        var pages = pageItems
            .Select((page, index) => CreatePayload(
                frame,
                page,
                checked((ushort)index),
                checked((ushort)pageItems.Count),
                checked((ushort)totalItems)))
            .ToArray();
        return RealtimeWireCodec.TryValidateCompleteAuthorityMotionSnapshotPages(
            pages,
            out _)
            ? pages
            : Array.Empty<AuthorityMotionSnapshotPayload>();
    }

    private static bool Fits(
        WorldZoneMotionFrame frame,
        AuthorityMotionSnapshotItem[] items)
    {
        if (items.Length > RealtimeWireContract.MaximumSnapshotItemsParseBound)
            return false;
        if (!RealtimeWireCodec.TryCreateAuthorityMotionSnapshotForAuthentication(
                MeasurementMetadata,
                CreatePayload(
                    frame,
                    items,
                    0,
                    1,
                    checked((ushort)items.Length)),
                out var datagram,
                out _))
        {
            return false;
        }
        return datagram.Length <= MaximumSnapshotDatagramLength;
    }

    private static AuthorityMotionSnapshotPayload CreatePayload(
        WorldZoneMotionFrame frame,
        AuthorityMotionSnapshotItem[] items,
        ushort pageIndex,
        ushort pageCount,
        ushort totalItemCount) => new()
    {
        WorldId = frame.WorldId,
        ZoneKey = frame.ZoneKey,
        FrameOccurrenceId = frame.FrameOccurrenceId,
        PageIndex = pageIndex,
        PageCount = pageCount,
        TotalItemCount = totalItemCount,
        Reserved = 0,
        ObservedAtUnixMilliseconds = frame.ObservedAtUnixMilliseconds,
        Items = items
    };

    private static AuthorityMotionSnapshotItem? TryMap(
        WorldPlayerMotionState state)
    {
        if (state.AvatarEntityId == Guid.Empty ||
            state.RuntimeSessionId == Guid.Empty ||
            state.ServerTick <= 0 ||
            state.LastProcessedIntentSequence < 0 ||
            !IsFiniteFloat(state.PositionX) ||
            !IsFiniteFloat(state.PositionY) ||
            !IsFiniteFloat(state.PositionZ) ||
            !IsFiniteFloat(state.VelocityX) ||
            !IsFiniteFloat(state.VelocityY) ||
            !IsFiniteFloat(state.VelocityZ) ||
            string.IsNullOrWhiteSpace(state.MotionStatus))
        {
            return null;
        }

        return new AuthorityMotionSnapshotItem
        {
            ActorEntityId = state.AvatarEntityId,
            ActorRuntimeSessionId = state.RuntimeSessionId,
            ActorServerTick = state.ServerTick,
            PositionX = (float)state.PositionX,
            PositionY = (float)state.PositionY,
            PositionZ = (float)state.PositionZ,
            // The fixed-tick motion state does not author facing yet. Identity
            // is an explicit neutral transport value; clients may present
            // velocity-facing but transport never invents gameplay rotation.
            RotationW = 1f,
            VelocityX = (float)state.VelocityX,
            VelocityY = (float)state.VelocityY,
            VelocityZ = (float)state.VelocityZ,
            MotionStatus = state.MotionStatus,
            LastProcessedInputSequence =
                (ulong)state.LastProcessedIntentSequence,
            Flags = state.Grounded
                ? AuthorityMotionStateFlags.Grounded
                : AuthorityMotionStateFlags.None
        };
    }

    private static bool IsFiniteFloat(double value) =>
        double.IsFinite(value) &&
        value >= -float.MaxValue &&
        value <= float.MaxValue;
}

/// <summary>
/// Converts already-authoritative fixed-tick Zone frames into authenticated
/// UDP presentation snapshots. It owns no movement, rule, Fact, or lifecycle
/// transition and is disabled with the UDP listener feature flag.
/// </summary>
internal sealed class AuthorityUdpMotionSnapshotPublisher : BackgroundService
{
    private readonly AuthorityUdpListenerOptions options;
    private readonly AuthorityUdpPeerSessionRegistry sessions;
    private readonly AuthorityUdpOutboundSnapshotChannel outbound;
    private readonly AuthorityUdpListenerDiagnostics diagnostics;
    private readonly IAuthorityUdpMotionFrameBackplane backplane;
    private readonly LatestWorldZoneMotionFrameBuffer frames;

    internal AuthorityUdpMotionSnapshotPublisher(
        AuthorityUdpListenerOptions options,
        AuthorityUdpPeerSessionRegistry sessions,
        AuthorityUdpOutboundSnapshotChannel outbound,
        AuthorityUdpListenerDiagnostics diagnostics)
        : this(
            options,
            sessions,
            outbound,
            diagnostics,
            new InMemoryAuthorityUdpMotionFrameBackplane(options))
    {
    }

    public AuthorityUdpMotionSnapshotPublisher(
        AuthorityUdpListenerOptions options,
        AuthorityUdpPeerSessionRegistry sessions,
        AuthorityUdpOutboundSnapshotChannel outbound,
        AuthorityUdpListenerDiagnostics diagnostics,
        IAuthorityUdpMotionFrameBackplane backplane)
    {
        this.options = options;
        this.sessions = sessions;
        this.outbound = outbound;
        this.diagnostics = diagnostics;
        this.backplane = backplane;
        frames = new(
            options.OutboundSnapshotCapacity,
            options.OutboundSnapshotByteCapacity,
            options.MaximumSnapshotFrameItems);
    }

    internal bool TryPublish(WorldZoneMotionFrame frame)
    {
        if (!options.Enabled) return false;
        if (frame.WorldId == Guid.Empty ||
            !SemanticId.IsValid(frame.ZoneKey) ||
            frame.ServerTick <= 0 ||
            frame.ObservedAtUnixMilliseconds <= 0)
        {
            diagnostics.RecordMotionSnapshotDropped("invalid_frame");
            return false;
        }
        // An empty fixed-tick result is normal and intentionally emits no UDP
        // presence semantics.
        if (frame.Items.Count == 0) return true;
        if (frames.TryWrite(frame)) return true;
        diagnostics.RecordMotionSnapshotDropped("frame_queue_full");
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        try
        {
            if (!await StartBackplaneWithRetry(stoppingToken)) return;
            var publish = PublishToBackplane(stoppingToken);
            var consume = ConsumeBackplane(stoppingToken);
            await Task.WhenAll(publish, consume);
        }
        finally
        {
            try { await backplane.Stop(CancellationToken.None); }
            catch { }
        }
    }

    private async Task<bool> StartBackplaneWithRetry(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await backplane.Start(cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                diagnostics.RecordMotionSnapshotDropped(
                    "backplane_start_failure");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }
        }
        return false;
    }

    private async Task PublishToBackplane(CancellationToken cancellationToken)
    {
        await foreach (var frame in frames.ReadAll(cancellationToken))
        {
            try
            {
                // SignalR and UDP transport the exact same source occurrence.
                // Redis is fanout only and never rewrites canonical identity.
                if (!await backplane.Publish(frame, cancellationToken))
                    diagnostics.RecordMotionSnapshotDropped(
                        "backplane_publish_failure");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                diagnostics.RecordMotionSnapshotDropped(
                    "backplane_publish_failure");
            }
        }
    }

    private async Task ConsumeBackplane(CancellationToken cancellationToken)
    {
        await foreach (var frame in backplane.ReadAll(cancellationToken))
        {
            try { ProcessFrame(frame); }
            catch
            {
                // One malformed or oversized replaceable frame must never
                // terminate the hosted publisher or the Authority process.
                diagnostics.RecordMotionSnapshotDropped("backplane_invalid");
            }
        }
    }

    private void ProcessFrame(WorldZoneMotionFrame frame)
    {
        var pages = AuthorityUdpMotionSnapshotBatcher.Create(
            frame,
            out var rejectedItems);
        for (var index = 0; index < rejectedItems; index++)
            diagnostics.RecordMotionSnapshotDropped("invalid_item");
        if (pages.Count == 0) return;

        var targets = sessions.GetMatching(frame.WorldId, frame.ZoneKey);
        var pendingTargets = new List<PendingAuthorityUdpSnapshotTarget>(
            targets.Count);
        foreach (var target in targets)
        {
            if (!sessions.TryGetExact(target, out _))
            {
                diagnostics.RecordMotionSnapshotDropped("disconnected");
                continue;
            }
            pendingTargets.Add(new(target));
        }
        if (pendingTargets.Count == 0) return;

        int estimatedBytes;
        try
        {
            estimatedBytes = checked(
                pages.Sum(page => page.Items.Length * 160 + 256) +
                pendingTargets.Count * 96);
        }
        catch (OverflowException)
        {
            diagnostics.RecordMotionSnapshotDropped("datagram_queue_full");
            return;
        }
        if (!outbound.TryWrite(new(
                pendingTargets,
                pages,
                frame.ServerTick,
                estimatedBytes)))
        {
            diagnostics.RecordMotionSnapshotDropped("datagram_queue_full");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        frames.Complete();
        return base.StopAsync(cancellationToken);
    }
}

internal sealed class CompositeWorldZoneRuntimeNotificationPublisher(
    SignalRWorldZoneRuntimeNotificationPublisher signalR,
    AuthorityUdpMotionSnapshotPublisher udp) :
    IWorldZoneRuntimeNotificationPublisher
{
    public Task PublishChanged(
        Guid worldId,
        string zoneKey,
        long observedAtUnixMilliseconds,
        CancellationToken cancellationToken) =>
        signalR.PublishChanged(
            worldId,
            zoneKey,
            observedAtUnixMilliseconds,
            cancellationToken);

    public Task PublishMotionFrame(
        Guid worldId,
        string zoneKey,
        IReadOnlyList<WorldPlayerMotionState> states,
        long authorityFrameTick,
        long observedAtUnixMilliseconds,
        CancellationToken cancellationToken)
    {
        var frame = WorldZoneMotionFramePolicy.Create(
            worldId,
            zoneKey,
            states,
            Guid.NewGuid(),
            authorityFrameTick,
            observedAtUnixMilliseconds);
        if (frame is not null) udp.TryPublish(frame);
        return frame is null
            ? Task.CompletedTask
            : signalR.PublishMotionFrame(frame, cancellationToken);
    }
}
