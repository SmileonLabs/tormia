using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using LiteNetLib;
using Tormia.Ontology.Realtime.Protocol;

internal sealed record AuthorityUdpListenerOptions(
    bool Enabled,
    int Port,
    int PendingHandshakeCapacity,
    int DatagramProcessingCapacity,
    int AuthenticatedIntentCapacity,
    int OutboundSnapshotCapacity,
    int OutboundSnapshotByteCapacity,
    int MaximumSnapshotFrameItems,
    int MaximumSnapshotBackplaneEnvelopeBytes,
    int MaximumOutstandingHandshakeRedemptions,
    int MaximumOutstandingGenerationLookups,
    TimeSpan HandshakeTimeout,
    TimeSpan GenerationFenceTimeout,
    AuthorityUdpHandshakeAdmissionOptions Admission)
{
    internal static AuthorityUdpListenerOptions Disabled { get; } = new(
        false,
        5273,
        128,
        2048,
        2048,
        64,
        4 * 1024 * 1024,
        512,
        256 * 1024,
        16,
        64,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromMilliseconds(250),
        AuthorityUdpHandshakeAdmissionOptions.Default);
}

internal sealed record AuthenticatedUdpMotionIntent(
    UdpTransportTicketBinding Binding,
    RealtimePacketHeader Header,
    MotionIntentPayload Payload,
    long ReceivedAtUnixMilliseconds,
    UdpTransportValidationFence ValidationFence);

/// <summary>
/// Evidence that transport identity was current immediately before enqueue.
/// This is not gameplay authorization. A later Authority consumer must
/// revalidate the fence before publishing canonical intent.
/// </summary>
internal sealed record UdpTransportValidationFence(
    Guid AuthenticatedTransportSessionId,
    ulong TransportGeneration,
    long ValidatedAtUnixMilliseconds);

internal interface IUdpAuthenticatedMotionIntentSink
{
    bool TryWrite(AuthenticatedUdpMotionIntent value);
}

/// <summary>
/// Stage-six isolation boundary. Stage eight may consume Reader and publish
/// canonical intent to Authority. Merely reaching this channel changes no
/// gameplay state and writes no Fact.
/// </summary>
internal sealed class UdpAuthenticatedMotionIntentChannel :
    IUdpAuthenticatedMotionIntentSink
{
    private readonly Channel<AuthenticatedUdpMotionIntent> channel;

    internal UdpAuthenticatedMotionIntentChannel(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        channel = Channel.CreateBounded<AuthenticatedUdpMotionIntent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
    }

    public bool TryWrite(AuthenticatedUdpMotionIntent value) =>
        channel.Writer.TryWrite(value);

    internal ChannelReader<AuthenticatedUdpMotionIntent> Reader => channel.Reader;
}

internal sealed class AuthorityUdpListenerDiagnostics : IDisposable
{
    internal const string MeterName = "Tormia.WorldAuthority.UdpTransport";
    internal const string HandshakeRejectedName =
        "tormia.realtime.udp.handshake.rejected";
    internal const string DatagramDroppedName =
        "tormia.realtime.udp.datagram.dropped";
    internal const string MotionIntentAcceptedName =
        "tormia.realtime.udp.motion_intent.accepted";
    internal const string MotionIntentPublishedName =
        "tormia.realtime.udp.motion_intent.published";
    internal const string MotionIntentRejectedName =
        "tormia.realtime.udp.motion_intent.rejected";
    internal const string MotionSnapshotPublishedName =
        "tormia.realtime.udp.motion_snapshot.published";
    internal const string MotionSnapshotDroppedName =
        "tormia.realtime.udp.motion_snapshot.dropped";
    internal const string MotionSnapshotItemsName =
        "tormia.realtime.udp.motion_snapshot.items";
    internal const string GenerationFenceBatchName =
        "tormia.realtime.udp.generation_fence.batch";
    internal const string GenerationFenceBatchSizeName =
        "tormia.realtime.udp.generation_fence.batch_size";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> handshakeRejected;
    private readonly Counter<long> datagramDropped;
    private readonly Counter<long> motionIntentAccepted;
    private readonly Counter<long> motionIntentPublished;
    private readonly Counter<long> motionIntentRejected;
    private readonly Counter<long> motionSnapshotPublished;
    private readonly Counter<long> motionSnapshotDropped;
    private readonly Histogram<long> motionSnapshotItems;
    private readonly Counter<long> generationFenceBatch;
    private readonly Histogram<long> generationFenceBatchSize;

    public AuthorityUdpListenerDiagnostics()
    {
        handshakeRejected = meter.CreateCounter<long>(HandshakeRejectedName);
        datagramDropped = meter.CreateCounter<long>(DatagramDroppedName);
        motionIntentAccepted = meter.CreateCounter<long>(MotionIntentAcceptedName);
        motionIntentPublished = meter.CreateCounter<long>(MotionIntentPublishedName);
        motionIntentRejected = meter.CreateCounter<long>(MotionIntentRejectedName);
        motionSnapshotPublished = meter.CreateCounter<long>(MotionSnapshotPublishedName);
        motionSnapshotDropped = meter.CreateCounter<long>(MotionSnapshotDroppedName);
        motionSnapshotItems = meter.CreateHistogram<long>(MotionSnapshotItemsName);
        generationFenceBatch = meter.CreateCounter<long>(
            GenerationFenceBatchName);
        generationFenceBatchSize = meter.CreateHistogram<long>(
            GenerationFenceBatchSizeName);
    }

    internal void RecordHandshakeRejected(string reason) =>
        handshakeRejected.Add(1, new KeyValuePair<string, object?>(
            "reason",
            NormalizeHandshakeReason(reason)));

    internal void RecordDatagramDropped(string reason) =>
        datagramDropped.Add(1, new KeyValuePair<string, object?>(
            "reason",
            NormalizeDatagramReason(reason)));

    internal void RecordMotionIntentAccepted() => motionIntentAccepted.Add(1);

    internal void RecordMotionIntentPublished() => motionIntentPublished.Add(1);

    internal void RecordMotionIntentRejected(string? reason) =>
        motionIntentRejected.Add(1, new KeyValuePair<string, object?>(
            "reason",
            NormalizeIntentRejectionReason(reason)));

    internal void RecordMotionSnapshotPublished(int itemCount)
    {
        motionSnapshotPublished.Add(1);
        motionSnapshotItems.Record(itemCount);
    }

    internal void RecordMotionSnapshotDropped(string reason) =>
        motionSnapshotDropped.Add(1, new KeyValuePair<string, object?>(
            "reason",
            NormalizeSnapshotDropReason(reason)));

    internal void RecordGenerationFenceBatch(int bindingCount, string outcome)
    {
        var normalized = NormalizeGenerationFenceOutcome(outcome);
        generationFenceBatch.Add(1, new KeyValuePair<string, object?>(
            "outcome", normalized));
        generationFenceBatchSize.Record(
            Math.Max(0, bindingCount),
            new KeyValuePair<string, object?>("outcome", normalized));
    }

    public void Dispose() => meter.Dispose();

    private static string NormalizeHandshakeReason(string value) => value switch
    {
        "malformed" => "malformed",
        "admission" => "admission",
        "queue_full" => "queue_full",
        "redeem_rejected" => "redeem_rejected",
        "connected_presence_rejected" => "connected_presence",
        "cancelled" => "cancelled",
        "failure" => "failure",
        _ => "unknown"
    };

    private static string NormalizeDatagramReason(string value) => value switch
    {
        "oversized" => "oversized",
        "malformed" => "malformed",
        "unknown_session" => "unknown_session",
        "old_generation" => "old_generation",
        "stale_transport_generation" => "old_generation",
        "authentication" => "authentication",
        "replay" => "replay",
        "unsupported_kind" => "unsupported_kind",
        "binding_mismatch" => "binding_mismatch",
        "queue_full" => "queue_full",
        "invalid_delivery" => "invalid_delivery",
        "failure" => "failure",
        _ => "unknown"
    };

    private static string NormalizeIntentRejectionReason(string? value) => value switch
    {
        "old_generation" => "old_generation",
        "stale_transport_generation" => "old_generation",
        "writer_mismatch" or "transport_writer_mismatch" =>
            "writer_mismatch",
        "invalid_intent" or "invalid_player_intent" => "invalid_intent",
        "avatar_not_owned" => "avatar_not_owned",
        "zone_not_found" => "zone_not_found",
        "player_runtime_session_mismatch" => "runtime_session",
        "locomotion_contract_missing" => "contract_missing",
        "locomotion_contract_mismatch" => "contract_mismatch",
        "locomotion_rule_must_be_ephemeral" => "durable_rule",
        "stale_player_intent" => "stale_intent",
        "coalesced" => "coalesced",
        "failure" => "failure",
        _ => "rule_rejected"
    };

    private static string NormalizeSnapshotDropReason(string value) => value switch
    {
        "frame_queue_full" => "frame_queue_full",
        "datagram_queue_full" => "datagram_queue_full",
        "backplane_queue_full" => "backplane_queue_full",
        "backplane_publish_failure" => "backplane_publish_failure",
        "backplane_invalid" => "backplane_invalid",
        "backplane_start_failure" => "backplane_start_failure",
        "frame_clock_or_backplane_failure" =>
            "frame_clock_or_backplane_failure",
        "invalid_frame" => "invalid_frame",
        "invalid_item" => "invalid_item",
        "encode_failure" => "encode_failure",
        "old_generation" => "old_generation",
        "disconnected" => "disconnected",
        "cancelled" => "cancelled",
        _ => "failure"
    };

    private static string NormalizeGenerationFenceOutcome(string value) =>
        value switch
        {
            "success" => "success",
            "capacity" => "capacity",
            "timeout" => "timeout",
            "invalid_result" => "invalid_result",
            "cancelled" => "cancelled",
            _ => "failure"
        };
}

internal sealed class AuthorityUdpPeerSessionRegistry : IDisposable
{
    private readonly ConcurrentDictionary<int, AuthorityUdpPeerSession> sessions = new();

    internal bool TryAdd(
        int peerId,
        UdpTransportTicketBinding binding,
        ReadOnlySpan<byte> authenticationKey)
    {
        if (peerId < 0 || authenticationKey.Length != 32) return false;
        var session = new AuthorityUdpPeerSession(binding, authenticationKey.ToArray());
        if (sessions.TryAdd(peerId, session)) return true;
        session.Dispose();
        return false;
    }

    internal bool TryGet(int peerId, out AuthorityUdpPeerSession session) =>
        sessions.TryGetValue(peerId, out session!);

    internal IReadOnlyList<AuthorityUdpPeerTarget> GetMatching(
        Guid worldId,
        string zoneKey) => sessions
        .Where(pair =>
            pair.Value.Binding.WorldId == worldId &&
            string.Equals(
                pair.Value.Binding.ZoneKey,
                zoneKey,
                StringComparison.Ordinal))
        .OrderBy(pair => pair.Key)
        .Select(pair => new AuthorityUdpPeerTarget(
            pair.Key,
            pair.Value.Binding))
        .ToArray();

    internal bool TryGetExact(
        AuthorityUdpPeerTarget target,
        out AuthorityUdpPeerSession session)
    {
        if (sessions.TryGetValue(target.PeerId, out session!) &&
            session.Binding == target.Binding)
        {
            return true;
        }
        session = null!;
        return false;
    }

    internal void Remove(int peerId)
    {
        if (sessions.TryRemove(peerId, out var session)) session.Dispose();
    }

    public void Dispose()
    {
        foreach (var key in sessions.Keys) Remove(key);
    }
}

internal readonly record struct AuthorityUdpPeerTarget(
    int PeerId,
    UdpTransportTicketBinding Binding);

internal sealed class AuthorityUdpPeerSession : IDisposable
{
    private readonly object gate = new();
    private byte[] authenticationKey;
    private ulong outgoingPacketSequence;

    internal AuthorityUdpPeerSession(
        UdpTransportTicketBinding binding,
        byte[] authenticationKey)
    {
        Binding = binding;
        this.authenticationKey = authenticationKey;
    }

    internal UdpTransportTicketBinding Binding { get; }
    internal ReadOnlySpan<byte> AuthenticationKey => authenticationKey;
    internal AuthorityUdpReplayWindow ReplayWindow { get; } = new();

    internal bool TryCreateMotionSnapshotDatagram(
        long authorityTick,
        AuthorityMotionSnapshotPayload payload,
        out byte[] datagram,
        out RealtimeWireError error)
    {
        lock (gate)
        {
            datagram = Array.Empty<byte>();
            if (authenticationKey.Length != 32 || authorityTick < 0)
            {
                error = RealtimeWireError.InvalidPayload;
                return false;
            }
            if (outgoingPacketSequence == ulong.MaxValue)
            {
                error = RealtimeWireError.InvalidSequence;
                return false;
            }
            var sequence = ++outgoingPacketSequence;
            var metadata = new RealtimePacketMetadata(
                Binding.AuthenticatedTransportSessionId,
                Binding.TransportGeneration,
                sequence,
                authorityTick);
            if (!RealtimeWireCodec.TryCreateAuthorityMotionSnapshotForAuthentication(
                    metadata,
                    payload,
                    out datagram,
                    out error) ||
                !RealtimeWireCodec.TryGetAuthenticatedRegion(
                    datagram,
                    out var authenticatedRegion,
                    out error))
            {
                return false;
            }

            Span<byte> digest = stackalloc byte[32];
            if (!HMACSHA256.TryHashData(
                    authenticationKey,
                    authenticatedRegion,
                    digest,
                    out var written) ||
                written != digest.Length ||
                !RealtimeWireCodec.TryWriteAuthenticationTag(
                    datagram,
                    digest[..RealtimeWireContract.RequiredAuthenticationTagLength],
                    out error))
            {
                CryptographicOperations.ZeroMemory(digest);
                datagram = Array.Empty<byte>();
                return false;
            }
            CryptographicOperations.ZeroMemory(digest);
            return true;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            var prior = authenticationKey;
            authenticationKey = Array.Empty<byte>();
            if (prior.Length > 0) CryptographicOperations.ZeroMemory(prior);
        }
    }
}

/// <summary>
/// Bounded, fail-closed generation fence. One lookup may be in flight per
/// transport session, so a stalled Redis operation cannot create one task per
/// datagram. No positive cache is allowed until cross-instance invalidation is
/// available: a newly issued generation must fence the old peer immediately.
/// </summary>
internal sealed class AuthorityUdpTransportGenerationFence(
    IUdpTransportTicketStore ticketStore,
    AuthorityUdpListenerOptions options,
    AuthorityUdpListenerDiagnostics? diagnostics = null)
{
    private readonly ConcurrentDictionary<Guid, Entry> entries = new();
    private readonly SemaphoreSlim globalLookups = new(
        options.MaximumOutstandingGenerationLookups,
        options.MaximumOutstandingGenerationLookups);

    internal async ValueTask<bool> IsCurrent(
        UdpTransportTicketBinding binding,
        CancellationToken cancellationToken)
    {
        var entry = entries.GetOrAdd(
            binding.AuthenticatedTransportSessionId,
            _ => new Entry(binding.TransportGeneration));
        Task<bool> lookup;
        TaskCompletionSource<bool>? launch = null;
        lock (entry.Gate)
        {
            if (entry.Generation != binding.TransportGeneration)
                return false;
            if (entry.InFlight is null)
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                entry.InFlight = completion.Task;
                launch = completion;
            }
            lookup = entry.InFlight;
        }
        if (launch is not null)
            _ = RunLookup(entry, binding, launch);

        try
        {
            return await lookup.WaitAsync(
                options.GenerationFenceTimeout,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    internal void Remove(Guid transportSessionId) =>
        entries.TryRemove(transportSessionId, out _);

    internal async ValueTask<IReadOnlyList<bool>> AreCurrentBatch(
        IReadOnlyList<UdpTransportTicketBinding> bindings,
        CancellationToken cancellationToken)
    {
        if (bindings.Count == 0) return Array.Empty<bool>();
        var acquired = false;
        var releaseImmediately = true;
        Task<IReadOnlyList<bool>>? operation = null;
        try
        {
            acquired = await globalLookups.WaitAsync(
                options.GenerationFenceTimeout,
                cancellationToken);
            if (!acquired)
            {
                diagnostics?.RecordGenerationFenceBatch(
                    bindings.Count, "capacity");
                return Enumerable.Repeat(false, bindings.Count).ToArray();
            }
            operation = ticketStore.AreTransportGenerationsCurrent(
                bindings,
                cancellationToken).AsTask();
            var result = await operation.WaitAsync(
                options.GenerationFenceTimeout,
                cancellationToken);
            if (result.Count == bindings.Count)
            {
                diagnostics?.RecordGenerationFenceBatch(
                    bindings.Count, "success");
                return result;
            }
            diagnostics?.RecordGenerationFenceBatch(
                bindings.Count, "invalid_result");
            return Enumerable.Repeat(false, bindings.Count).ToArray();
        }
        catch (TimeoutException)
        {
            diagnostics?.RecordGenerationFenceBatch(
                bindings.Count, "timeout");
            if (acquired && operation is { IsCompleted: false })
            {
                releaseImmediately = false;
                _ = ObserveAndReleaseBatchLookup(operation);
            }
            return Enumerable.Repeat(false, bindings.Count).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics?.RecordGenerationFenceBatch(
                bindings.Count, "cancelled");
            if (acquired && operation is { IsCompleted: false })
            {
                releaseImmediately = false;
                _ = ObserveAndReleaseBatchLookup(operation);
            }
            throw;
        }
        catch
        {
            diagnostics?.RecordGenerationFenceBatch(
                bindings.Count, "failure");
            return Enumerable.Repeat(false, bindings.Count).ToArray();
        }
        finally
        {
            if (acquired && releaseImmediately) globalLookups.Release();
        }
    }

    private async Task ObserveAndReleaseBatchLookup(
        Task<IReadOnlyList<bool>> operation)
    {
        try { await operation; }
        catch { }
        finally { globalLookups.Release(); }
    }

    private async Task RunLookup(
        Entry entry,
        UdpTransportTicketBinding binding,
        TaskCompletionSource<bool> completion)
    {
        var result = false;
        var acquired = false;
        try
        {
            acquired = globalLookups.Wait(0);
            if (!acquired) return;
            result = await ticketStore.IsTransportGenerationCurrent(
                binding,
                CancellationToken.None);
        }
        catch
        {
            result = false;
        }
        finally
        {
            if (acquired) globalLookups.Release();
            lock (entry.Gate)
            {
                if (ReferenceEquals(entry.InFlight, completion.Task))
                    entry.InFlight = null;
            }
            completion.TrySetResult(result);
        }
    }

    private sealed class Entry(ulong generation)
    {
        internal object Gate { get; } = new();
        internal ulong Generation { get; } = generation;
        internal Task<bool>? InFlight { get; set; }
    }
}

internal sealed class AuthorityUdpDatagramProcessor(
    AuthorityUdpPeerSessionRegistry sessions,
    AuthorityUdpTransportGenerationFence generationFence,
    IUdpAuthenticatedMotionIntentSink sink,
    AuthorityUdpListenerDiagnostics diagnostics,
    TimeProvider? clock = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    internal async ValueTask Process(
        int peerId,
        byte[] datagram,
        CancellationToken cancellationToken)
    {
        try
        {
            if (datagram.Length > RealtimeWireContract.MaximumDatagramLength)
            {
                diagnostics.RecordDatagramDropped("oversized");
                return;
            }
            if (!TryReadHeader(datagram, out var header))
            {
                diagnostics.RecordDatagramDropped("malformed");
                return;
            }
            if (!sessions.TryGet(peerId, out var session) ||
                header.AuthenticatedTransportSessionId !=
                    session.Binding.AuthenticatedTransportSessionId)
            {
                diagnostics.RecordDatagramDropped("unknown_session");
                return;
            }
            if (header.TransportGeneration != session.Binding.TransportGeneration)
            {
                diagnostics.RecordDatagramDropped("old_generation");
                return;
            }
            if (!await generationFence.IsCurrent(
                    session.Binding,
                    cancellationToken))
            {
                diagnostics.RecordDatagramDropped("old_generation");
                return;
            }

            if (!VerifyAuthentication(session, datagram))
            {
                diagnostics.RecordDatagramDropped("authentication");
                return;
            }
            if (!session.ReplayWindow.TryAccept(header.PacketSequence))
            {
                diagnostics.RecordDatagramDropped("replay");
                return;
            }
            if (header.MessageKind != RealtimeMessageKind.MotionIntent)
            {
                diagnostics.RecordDatagramDropped("unsupported_kind");
                return;
            }
            if (!RealtimeWireCodec.TryDecodeMotionIntentStructure(
                    datagram,
                    out var decodedHeader,
                    out var payload,
                    out _))
            {
                diagnostics.RecordDatagramDropped("malformed");
                return;
            }
            if (payload.WorldId != session.Binding.WorldId ||
                payload.ActorEntityId != session.Binding.AvatarEntityId ||
                !string.Equals(
                    payload.ZoneKey,
                    session.Binding.ZoneKey,
                    StringComparison.Ordinal))
            {
                diagnostics.RecordDatagramDropped("binding_mismatch");
                return;
            }
            if (!await generationFence.IsCurrent(
                    session.Binding,
                    cancellationToken))
            {
                diagnostics.RecordDatagramDropped("old_generation");
                return;
            }
            var validatedAt = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            if (!sink.TryWrite(new(
                    session.Binding,
                    decodedHeader,
                    payload,
                    validatedAt,
                    new(
                        session.Binding.AuthenticatedTransportSessionId,
                        session.Binding.TransportGeneration,
                        validatedAt))))
            {
                diagnostics.RecordDatagramDropped("queue_full");
                return;
            }
            diagnostics.RecordMotionIntentAccepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown is not a malformed client datagram.
        }
        catch
        {
            diagnostics.RecordDatagramDropped("failure");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(datagram);
        }
    }

    private static bool TryReadHeader(
        ReadOnlySpan<byte> datagram,
        out RealtimePacketHeader header)
    {
        if (!RealtimeWireCodec.TryReadStructuralEnvelope(
                datagram,
                out var envelope,
                out _))
        {
            header = default;
            return false;
        }
        header = envelope.Header;
        return true;
    }

    private static bool VerifyAuthentication(
        AuthorityUdpPeerSession session,
        ReadOnlySpan<byte> datagram)
    {
        return RealtimeWireCodec.TryReadStructuralEnvelope(
                   datagram,
                   out var envelope,
                   out _) &&
               AuthorityUdpDatagramAuthentication.Verify(
                   session.AuthenticationKey,
                   envelope.AuthenticatedRegion,
                   envelope.AuthenticationTag);
    }
}

internal sealed class AuthorityUdpRealtimeListener : BackgroundService
{
    private readonly AuthorityUdpListenerOptions options;
    private readonly IUdpTransportTicketStore tickets;
    private readonly IUdpTransportRuntimeSessionValidator runtimeValidator;
    private readonly AuthorityUdpPeerSessionRegistry sessions;
    private readonly AuthorityUdpTransportGenerationFence generationFence;
    private readonly AuthorityUdpDatagramProcessor processor;
    private readonly AuthorityUdpOutboundSnapshotChannel outboundSnapshots;
    private readonly AuthorityUdpListenerDiagnostics diagnostics;
    private readonly ILogger<AuthorityUdpRealtimeListener> logger;
    private readonly TimeProvider clock;
    private readonly AuthorityUdpHandshakeAdmissionGate admission;
    private readonly Channel<PendingHandshake> pendingHandshakes;
    private readonly Channel<PendingDatagram> pendingDatagrams;
    private readonly Channel<UdpTransportTicketBinding> disconnectedBindings;
    private readonly SemaphoreSlim outstandingHandshakeRedemptions;

    public AuthorityUdpRealtimeListener(
        AuthorityUdpListenerOptions options,
        IUdpTransportTicketStore tickets,
        IUdpTransportRuntimeSessionValidator runtimeValidator,
        AuthorityUdpPeerSessionRegistry sessions,
        AuthorityUdpTransportGenerationFence generationFence,
        AuthorityUdpDatagramProcessor processor,
        AuthorityUdpOutboundSnapshotChannel outboundSnapshots,
        AuthorityUdpListenerDiagnostics diagnostics,
        ILogger<AuthorityUdpRealtimeListener> logger,
        TimeProvider? clock = null)
    {
        this.options = options;
        this.tickets = tickets;
        this.runtimeValidator = runtimeValidator;
        this.sessions = sessions;
        this.generationFence = generationFence;
        this.processor = processor;
        this.outboundSnapshots = outboundSnapshots;
        this.diagnostics = diagnostics;
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        ValidateOptions(options);
        admission = new(options.Admission);
        pendingHandshakes = Channel.CreateBounded<PendingHandshake>(
            new BoundedChannelOptions(options.PendingHandshakeCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        pendingDatagrams = Channel.CreateBounded<PendingDatagram>(
            new BoundedChannelOptions(options.DatagramProcessingCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
        disconnectedBindings = Channel.CreateUnbounded<UdpTransportTicketBinding>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        outstandingHandshakeRedemptions = new(
            options.MaximumOutstandingHandshakeRedemptions,
            options.MaximumOutstandingHandshakeRedemptions);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Authority UDP listener is disabled.");
            return;
        }

        var listener = new EventBasedNetListener();
        var manager = new NetManager(listener)
        {
            AutoRecycle = false,
            UnsyncedEvents = false,
            UnconnectedMessagesEnabled = false,
            BroadcastReceiveEnabled = false
        };
        listener.ConnectionRequestEvent += HandleConnectionRequest;
        listener.NetworkReceiveEvent += HandleNetworkReceive;
        listener.PeerDisconnectedEvent += HandlePeerDisconnected;

        if (!manager.Start(options.Port))
            throw new InvalidOperationException(
                $"Authority UDP listener could not bind port {options.Port}.");
        logger.LogInformation(
            "Authority UDP listener started on port {Port}; authenticated intents remain isolated.",
            options.Port);

        var handshakeWorker = ProcessHandshakes(stoppingToken);
        var datagramWorker = ProcessDatagrams(stoppingToken);
        var outboundWorker = ProcessOutboundSnapshots(manager, stoppingToken);
        var disconnectWorker = ProcessDisconnectRevocations();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                manager.PollEvents();
                await Task.Delay(2, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            pendingHandshakes.Writer.TryComplete();
            pendingDatagrams.Writer.TryComplete();
            manager.Stop();
            disconnectedBindings.Writer.TryComplete();
            outboundSnapshots.Complete();
            await Task.WhenAll(
                handshakeWorker, datagramWorker, outboundWorker,
                disconnectWorker);
            sessions.Dispose();
        }

        void HandleConnectionRequest(ConnectionRequest request)
        {
            AuthorityUdpBootstrapFrame frame;
            byte[] rawFrame;
            try
            {
                if (request.Data.AvailableBytes !=
                    AuthorityUdpBootstrapContract.FrameLength)
                {
                    diagnostics.RecordHandshakeRejected("malformed");
                    TryReject(request);
                    return;
                }
                rawFrame = request.Data.GetRemainingBytes();
                try
                {
                    if (!AuthorityUdpBootstrapCodec.TryParse(rawFrame, out frame))
                    {
                        diagnostics.RecordHandshakeRejected("malformed");
                        TryReject(request);
                        return;
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(rawFrame);
                }
            }
            catch
            {
                diagnostics.RecordHandshakeRejected("malformed");
                TryReject(request);
                return;
            }

            if (!admission.TryEnter(
                    NormalizeAddress(request.RemoteEndPoint.Address),
                    clock.GetUtcNow().ToUnixTimeMilliseconds(),
                    out var lease))
            {
                ZeroFrame(frame);
                diagnostics.RecordHandshakeRejected("admission");
                TryReject(request);
                return;
            }
            if (!pendingHandshakes.Writer.TryWrite(new(request, frame, lease!)))
            {
                ZeroFrame(frame);
                lease!.Dispose();
                diagnostics.RecordHandshakeRejected("queue_full");
                TryReject(request);
            }
        }

        void HandleNetworkReceive(
            NetPeer peer,
            NetPacketReader reader,
            byte channelNumber,
            DeliveryMethod deliveryMethod)
        {
            try
            {
                if (channelNumber != 0 ||
                    deliveryMethod != DeliveryMethod.Unreliable)
                {
                    diagnostics.RecordDatagramDropped("invalid_delivery");
                    return;
                }
                if (reader.AvailableBytes <= 0 ||
                    reader.AvailableBytes > RealtimeWireContract.MaximumDatagramLength)
                {
                    diagnostics.RecordDatagramDropped(
                        reader.AvailableBytes > RealtimeWireContract.MaximumDatagramLength
                            ? "oversized"
                            : "malformed");
                    return;
                }
                var bytes = reader.GetRemainingBytes();
                if (!pendingDatagrams.Writer.TryWrite(new(peer.Id, bytes)))
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    diagnostics.RecordDatagramDropped("queue_full");
                }
            }
            finally
            {
                reader.Recycle();
            }
        }

        void HandlePeerDisconnected(NetPeer peer, DisconnectInfo _) =>
            RemovePeer(peer.Id, revokeSharedPresence: true);
    }

    private async Task ProcessHandshakes(CancellationToken cancellationToken)
    {
        await foreach (var pending in pendingHandshakes.Reader.ReadAllAsync())
        {
            var redemptionLeaseHeld = false;
            using (pending.AdmissionLease)
            {
                try
                {
                    if (!outstandingHandshakeRedemptions.Wait(0))
                    {
                        diagnostics.RecordHandshakeRejected("admission");
                        TryReject(pending.Request);
                        continue;
                    }
                    redemptionLeaseHeld = true;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                    timeout.CancelAfter(options.HandshakeTimeout);
                    var redeemTask = tickets.Redeem(
                            pending.Frame.ToRedemption(),
                            runtimeValidator,
                            timeout.Token)
                        .AsTask();
                    UdpTransportTicketRedeemResult result;
                    try
                    {
                        result = await redeemTask.WaitAsync(
                            options.HandshakeTimeout,
                            cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        timeout.Cancel();
                        redemptionLeaseHeld = false;
                        _ = CleanupLateRedemption(
                            redeemTask,
                            outstandingHandshakeRedemptions);
                        diagnostics.RecordHandshakeRejected("failure");
                        TryReject(pending.Request);
                        continue;
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        timeout.Cancel();
                        redemptionLeaseHeld = false;
                        _ = CleanupLateRedemption(
                            redeemTask,
                            outstandingHandshakeRedemptions);
                        throw;
                    }
                    var key = result.DatagramAuthenticationKey;
                    if (result.Status != UdpTransportTicketRedeemStatus.Redeemed ||
                        result.Binding is null ||
                        key is not { Length: 32 })
                    {
                        if (key is { Length: > 0 })
                            CryptographicOperations.ZeroMemory(key);
                        diagnostics.RecordHandshakeRejected("redeem_rejected");
                        TryReject(pending.Request);
                        continue;
                    }

                    try
                    {
                        var peer = pending.Request.Accept();
                        if (!sessions.TryAdd(peer.Id, result.Binding, key))
                        {
                            peer.Disconnect();
                            disconnectedBindings.Writer.TryWrite(result.Binding);
                            diagnostics.RecordHandshakeRejected("failure");
                        }
                        else if (!await tickets.MarkPromotedTransportSessionConnected(
                                     result.Binding,
                                     cancellationToken))
                        {
                            RemovePeer(peer.Id, revokeSharedPresence: true);
                            peer.Disconnect();
                            diagnostics.RecordHandshakeRejected(
                                "connected_presence_rejected");
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(key);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    diagnostics.RecordHandshakeRejected("cancelled");
                    TryReject(pending.Request);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Authority UDP handshake failed closed.");
                    diagnostics.RecordHandshakeRejected("failure");
                    TryReject(pending.Request);
                }
                finally
                {
                    if (redemptionLeaseHeld)
                        outstandingHandshakeRedemptions.Release();
                    ZeroFrame(pending.Frame);
                }
            }
        }
    }

    private async Task ProcessDatagrams(CancellationToken cancellationToken)
    {
        await foreach (var pending in pendingDatagrams.Reader.ReadAllAsync())
        {
            await processor.Process(
                pending.PeerId,
                pending.Datagram,
                cancellationToken);
        }
    }

    private async Task ProcessOutboundSnapshots(
        NetManager manager,
        CancellationToken cancellationToken)
    {
        await foreach (var pending in outboundSnapshots.Reader.ReadAllAsync())
        {
            try
            {
                var bindings = pending.Targets
                    .Select(candidate => candidate.Target.Binding)
                    .ToArray();
                // One MGET-backed fence per authoritative Zone frame. The
                // listener still re-checks the local registry afterwards so
                // a disconnect or generation replacement during the lookup
                // cannot receive a packet.
                var current = await generationFence.AreCurrentBatch(
                    bindings,
                    cancellationToken);
                for (var index = 0; index < pending.Targets.Count; index++)
                {
                    var candidate = pending.Targets[index];
                    if (index >= current.Count || !current[index] ||
                        !sessions.TryGetExact(candidate.Target, out var session))
                    {
                        diagnostics.RecordMotionSnapshotDropped("old_generation");
                        continue;
                    }
                    var peer = manager.GetPeerById(
                        candidate.Target.PeerId) as NetPeer;
                    if (peer is null ||
                        peer.ConnectionState != ConnectionState.Connected)
                    {
                        diagnostics.RecordMotionSnapshotDropped("disconnected");
                        continue;
                    }
                    foreach (var page in pending.Pages)
                    {
                        if (!session.TryCreateMotionSnapshotDatagram(
                                pending.AuthorityFrameTick,
                                page,
                                out var datagram,
                                out _))
                        {
                            diagnostics.RecordMotionSnapshotDropped("encode_failure");
                            continue;
                        }
                        peer.Send(datagram, 0, DeliveryMethod.Unreliable);
                        diagnostics.RecordMotionSnapshotPublished(
                            page.Items.Length);
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                diagnostics.RecordMotionSnapshotDropped("cancelled");
                break;
            }
            catch (Exception exception)
            {
                diagnostics.RecordMotionSnapshotDropped("failure");
                logger.LogWarning(
                    exception,
                    "Authority UDP motion snapshot send failed closed.");
            }
            finally
            {
                outboundSnapshots.Release(pending);
            }
        }
    }

    private static void TryReject(ConnectionRequest request)
    {
        try { request.RejectForce(); }
        catch { }
    }

    private void RemovePeer(int peerId, bool revokeSharedPresence)
    {
        if (sessions.TryGet(peerId, out var session))
        {
            generationFence.Remove(
                session.Binding.AuthenticatedTransportSessionId);
            if (revokeSharedPresence)
                disconnectedBindings.Writer.TryWrite(session.Binding);
        }
        sessions.Remove(peerId);
    }

    private async Task ProcessDisconnectRevocations()
    {
        await foreach (var binding in disconnectedBindings.Reader
                           .ReadAllAsync())
        {
            try
            {
                await tickets.RevokeConnectedTransportSession(
                    binding, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Failed to revoke disconnected UDP peer presence.");
            }
        }
    }

    private async Task CleanupLateRedemption(
        Task<UdpTransportTicketRedeemResult> task,
        SemaphoreSlim operationGate)
    {
        try
        {
            var late = await task;
            if (late.DatagramAuthenticationKey is { Length: > 0 } key)
                CryptographicOperations.ZeroMemory(key);
            if (late.Status == UdpTransportTicketRedeemStatus.Redeemed &&
                late.Binding is not null)
            {
                await tickets.RevokePromotedTransportSession(
                    late.Binding,
                    CancellationToken.None);
            }
        }
        catch
        {
            // Observe the late exception. The handshake was already rejected.
        }
        finally
        {
            operationGate.Release();
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? address.MapToIPv6()
            : address;

    internal static void ValidateOptions(AuthorityUdpListenerOptions value)
    {
        if (value.Port is < 1 or > 65535 ||
            value.PendingHandshakeCapacity <= 0 ||
            value.DatagramProcessingCapacity <= 0 ||
            value.AuthenticatedIntentCapacity <= 0 ||
            value.OutboundSnapshotCapacity is <= 0 or > 4096 ||
            value.OutboundSnapshotByteCapacity is <= 0 or > 64 * 1024 * 1024 ||
            value.MaximumSnapshotFrameItems <= 0 ||
            value.MaximumSnapshotFrameItems >
                RealtimeWireContract.MaximumSnapshotTotalItems ||
            value.MaximumSnapshotBackplaneEnvelopeBytes < 1024 ||
            value.MaximumSnapshotBackplaneEnvelopeBytes > 512 * 1024 ||
            value.MaximumSnapshotBackplaneEnvelopeBytes >
                value.OutboundSnapshotByteCapacity ||
            value.MaximumOutstandingHandshakeRedemptions <= 0 ||
            value.MaximumOutstandingGenerationLookups <= 0 ||
            value.HandshakeTimeout <= TimeSpan.Zero ||
            value.GenerationFenceTimeout <= TimeSpan.Zero ||
            value.Admission.MaximumPending <= 0 ||
            value.Admission.MaximumPendingPerAddress <= 0 ||
            value.Admission.MaximumAttemptsPerAddressPerWindow <= 0 ||
            value.Admission.AttemptWindow <= TimeSpan.Zero ||
            value.Admission.MaximumTrackedAddresses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void ZeroFrame(AuthorityUdpBootstrapFrame frame)
    {
        if (frame.Ticket is not null) CryptographicOperations.ZeroMemory(frame.Ticket);
        if (frame.ClientNonce is not null) CryptographicOperations.ZeroMemory(frame.ClientNonce);
        if (frame.Proof is not null) CryptographicOperations.ZeroMemory(frame.Proof);
    }

    private sealed record PendingHandshake(
        ConnectionRequest Request,
        AuthorityUdpBootstrapFrame Frame,
        IDisposable AdmissionLease);

    private sealed record PendingDatagram(int PeerId, byte[] Datagram);
}
