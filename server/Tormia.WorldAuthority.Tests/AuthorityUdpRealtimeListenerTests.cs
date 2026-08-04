using System.Net;
using System.Security.Cryptography;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

public sealed class AuthorityUdpRealtimeListenerTests
{
    private static readonly Guid WorldId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AvatarId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RuntimeSessionId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TransportSessionId =
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public void BootstrapCodec_AcceptsOnlyExactFixedBinaryFrame()
    {
        var frame = new AuthorityUdpBootstrapFrame(
            TransportSessionId,
            7,
            Enumerable.Repeat((byte)1, 32).ToArray(),
            Enumerable.Repeat((byte)2, 16).ToArray(),
            Enumerable.Repeat((byte)3, 32).ToArray());
        var encoded = AuthorityUdpBootstrapCodec.Encode(frame);

        Assert.Equal(AuthorityUdpBootstrapContract.FrameLength, encoded.Length);
        Assert.True(AuthorityUdpBootstrapCodec.TryParse(encoded, out var decoded));
        Assert.Equal(frame.AuthenticatedTransportSessionId,
            decoded.AuthenticatedTransportSessionId);
        Assert.Equal(frame.TransportGeneration, decoded.TransportGeneration);
        Assert.Equal(frame.Ticket, decoded.Ticket);
        Assert.False(AuthorityUdpBootstrapCodec.TryParse(encoded[..^1], out _));
        Assert.False(AuthorityUdpBootstrapCodec.TryParse(
            encoded.Concat(new byte[] { 0 }).ToArray(), out _));

        encoded[6] = 1;
        Assert.False(AuthorityUdpBootstrapCodec.TryParse(encoded, out _));
        CryptographicOperations.ZeroMemory(decoded.Ticket);
        CryptographicOperations.ZeroMemory(decoded.ClientNonce);
        CryptographicOperations.ZeroMemory(decoded.Proof);
    }

    [Fact]
    public void ReplayWindow_AcceptsBoundedReorderingAndRejectsReplayOrOldSequence()
    {
        var window = new AuthorityUdpReplayWindow();

        Assert.True(window.TryAccept(100));
        Assert.True(window.TryAccept(102));
        Assert.True(window.TryAccept(101));
        Assert.False(window.TryAccept(101));
        Assert.False(window.TryAccept(38));
        Assert.True(window.TryAccept(103));
    }

    [Fact]
    public async Task ReplayWindow_ConcurrentDuplicateHasExactlyOneWinner()
    {
        var window = new AuthorityUdpReplayWindow();
        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => window.TryAccept(42))));

        Assert.Single(results, value => value);
    }

    [Fact]
    public void AdmissionGate_CapsPendingPerAddressAndLeaseRestoresCapacity()
    {
        var gate = new AuthorityUdpHandshakeAdmissionGate(new(
            MaximumPending: 2,
            MaximumPendingPerAddress: 1,
            MaximumAttemptsPerAddressPerWindow: 3,
            AttemptWindow: TimeSpan.FromSeconds(10),
            MaximumTrackedAddresses: 4));
        var firstAddress = IPAddress.Parse("127.0.0.1").MapToIPv6();
        var secondAddress = IPAddress.Parse("127.0.0.2").MapToIPv6();

        Assert.True(gate.TryEnter(firstAddress, 1000, out var first));
        Assert.False(gate.TryEnter(firstAddress, 1001, out _));
        Assert.True(gate.TryEnter(secondAddress, 1002, out var second));
        Assert.False(gate.TryEnter(IPAddress.IPv6Loopback, 1003, out _));

        first!.Dispose();
        Assert.True(gate.TryEnter(firstAddress, 1004, out var replacement));
        replacement!.Dispose();
        second!.Dispose();
    }

    [Fact]
    public void AuthenticatedIntentChannel_IsBoundedAndTryWriteFailsWhenFull()
    {
        var channel = new UdpAuthenticatedMotionIntentChannel(1);
        var value = new AuthenticatedUdpMotionIntent(
            Binding(),
            default,
            Payload(1),
            1,
            new(TransportSessionId, 1, 1));

        Assert.True(channel.TryWrite(value));
        Assert.False(channel.TryWrite(value));
        Assert.True(channel.Reader.TryRead(out _));
        Assert.True(channel.TryWrite(value));
    }

    [Fact]
    public async Task Processor_AuthenticatesThenConsumesReplayAndQueuesIntent()
    {
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        Assert.True(sessions.TryAdd(4, Binding(), key));
        var sink = new CapturingSink();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        var store = new CurrentTicketStore();
        var processor = new AuthorityUdpDatagramProcessor(
            sessions,
            new AuthorityUdpTransportGenerationFence(store, Options()),
            sink,
            diagnostics);
        var packet = CreatePacket(1, key);

        await processor.Process(4, packet.ToArray(), CancellationToken.None);
        await processor.Process(4, packet.ToArray(), CancellationToken.None);

        var accepted = Assert.Single(sink.Values);
        Assert.Equal(WorldId, accepted.Payload.WorldId);
        Assert.Equal(AvatarId, accepted.Payload.ActorEntityId);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(packet);
    }

    [Fact]
    public async Task Processor_BadMacDoesNotConsumeSequenceBeforeValidRetry()
    {
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        sessions.TryAdd(4, Binding(), key);
        var sink = new CapturingSink();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        var store = new CurrentTicketStore();
        var processor = new AuthorityUdpDatagramProcessor(
            sessions,
            new AuthorityUdpTransportGenerationFence(store, Options()),
            sink,
            diagnostics);
        var valid = CreatePacket(9, key);
        var invalid = valid.ToArray();
        invalid[^1] ^= 0x80;

        await processor.Process(4, invalid, CancellationToken.None);
        await processor.Process(4, valid.ToArray(), CancellationToken.None);

        Assert.Single(sink.Values);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(valid);
    }

    [Fact]
    public async Task Processor_OldGenerationFailsBeforePayloadDecodeOrSink()
    {
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var key = RandomNumberGenerator.GetBytes(32);
        sessions.TryAdd(4, Binding(), key);
        var sink = new CapturingSink();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        var store = new CurrentTicketStore(current: false);
        var processor = new AuthorityUdpDatagramProcessor(
            sessions,
            new AuthorityUdpTransportGenerationFence(store, Options()),
            sink,
            diagnostics);

        await processor.Process(
            4,
            CreatePacket(1, key),
            CancellationToken.None);

        Assert.Empty(sink.Values);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public async Task Processor_ReissueFencesPreviouslyAcceptedPeerWithoutPositiveCache()
    {
        using var sessions = new AuthorityUdpPeerSessionRegistry();
        var key = RandomNumberGenerator.GetBytes(32);
        sessions.TryAdd(4, Binding(), key);
        var sink = new CapturingSink();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        var store = new CurrentTicketStore();
        var processor = new AuthorityUdpDatagramProcessor(
            sessions,
            new AuthorityUdpTransportGenerationFence(store, Options()),
            sink,
            diagnostics);

        await processor.Process(4, CreatePacket(1, key), CancellationToken.None);
        store.Current = false;
        await processor.Process(4, CreatePacket(2, key), CancellationToken.None);

        Assert.Single(sink.Values);
        // The accepted packet is fenced once before MAC/decode and once again
        // immediately before enqueue. The reissued generation then fails the
        // first fence of packet two, so no stale intent reaches the sink.
        Assert.Equal(3, store.GenerationLookupCount);
        CryptographicOperations.ZeroMemory(key);
    }

    [Fact]
    public void ListenerOptions_InvalidCapacitiesFailFast()
    {
        var invalid = Options() with { PendingHandshakeCapacity = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityUdpRealtimeListener.ValidateOptions(invalid));

        var wireIncompatible = Options() with
        {
            MaximumSnapshotFrameItems =
                RealtimeWireContract.MaximumSnapshotTotalItems + 1
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthorityUdpRealtimeListener.ValidateOptions(wireIncompatible));
    }

    [Fact]
    public async Task GenerationFence_TimeoutKeepsOneLookupInFlightPerSession()
    {
        var store = new BlockingTicketStore();
        var options = Options() with
        {
            GenerationFenceTimeout = TimeSpan.FromMilliseconds(10),
            MaximumOutstandingGenerationLookups = 1
        };
        var fence = new AuthorityUdpTransportGenerationFence(store, options);

        Assert.False(await fence.IsCurrent(Binding(), CancellationToken.None));
        Assert.False(await fence.IsCurrent(Binding(), CancellationToken.None));
        var otherBinding = Binding() with
        {
            AuthenticatedTransportSessionId = Guid.NewGuid()
        };
        Assert.False(await fence.IsCurrent(otherBinding, CancellationToken.None));
        Assert.Equal(1, store.GenerationLookupCount);

        store.Complete(true);
        // Completion and the fence's finally block are intentionally scheduled
        // independently. Poll through the public boundary until the completed
        // in-flight entry is cleared; a single Task.Yield is timing-dependent
        // under the full parallel test suite.
        for (var attempt = 0;
             attempt < 32 && store.GenerationLookupCount < 2;
             attempt++)
        {
            await Task.Delay(1);
            Assert.True(await fence.IsCurrent(
                Binding(), CancellationToken.None));
        }
        Assert.Equal(2, store.GenerationLookupCount);
    }

    [Fact]
    public async Task BatchGenerationFence_TimeoutRetainsActualOperationLease()
    {
        var store = new BlockingTicketStore();
        var options = Options() with
        {
            GenerationFenceTimeout = TimeSpan.FromMilliseconds(10),
            MaximumOutstandingGenerationLookups = 1
        };
        var fence = new AuthorityUdpTransportGenerationFence(store, options);
        var bindings = new[] { Binding() };

        Assert.False(Assert.Single(await fence.AreCurrentBatch(
            bindings, CancellationToken.None)));
        Assert.False(Assert.Single(await fence.AreCurrentBatch(
            bindings, CancellationToken.None)));
        Assert.Equal(1, store.GenerationLookupCount);

        store.Complete(true);
        for (var attempt = 0;
             attempt < 32 && store.GenerationLookupCount < 2;
             attempt++)
        {
            await Task.Delay(1);
            await fence.AreCurrentBatch(bindings, CancellationToken.None);
        }
        Assert.Equal(2, store.GenerationLookupCount);
    }

    private static byte[] CreatePacket(ulong sequence, byte[] key)
    {
        Assert.True(RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
            new(TransportSessionId, 1, sequence, 0),
            Payload(sequence),
            out var datagram,
            out var error), error.ToString());
        Assert.True(RealtimeWireCodec.TryGetAuthenticatedRegion(
            datagram,
            out var authenticatedRegion,
            out error), error.ToString());
        var digest = HMACSHA256.HashData(key, authenticatedRegion);
        Assert.True(RealtimeWireCodec.TryWriteAuthenticationTag(
            datagram,
            digest.AsSpan(0, RealtimeWireContract.RequiredAuthenticationTagLength),
            out error), error.ToString());
        CryptographicOperations.ZeroMemory(digest);
        return datagram;
    }

    private static MotionIntentPayload Payload(ulong inputSequence) => new()
    {
        WorldId = WorldId,
        ZoneKey = "main_zone",
        ActorEntityId = AvatarId,
        InputSequence = inputSequence,
        ClientTimestampMilliseconds = 1,
        MoveX = 0,
        MoveZ = 1,
        RequestedSpeed = 4,
        FacingX = 0,
        FacingZ = 1,
        Flags = MotionIntentFlags.None
    };

    private static UdpTransportTicketBinding Binding() => new(
        WorldId,
        UserId,
        AvatarId,
        "main_zone",
        RuntimeSessionId,
        1,
        TransportSessionId,
        RealtimeWireContract.ProtocolVersion);

    private static AuthorityUdpListenerOptions Options() =>
        AuthorityUdpListenerOptions.Disabled with
        {
            GenerationFenceTimeout = TimeSpan.FromSeconds(1)
        };

    private sealed class CapturingSink : IUdpAuthenticatedMotionIntentSink
    {
        public List<AuthenticatedUdpMotionIntent> Values { get; } = new();
        public bool TryWrite(AuthenticatedUdpMotionIntent value)
        {
            Values.Add(value);
            return true;
        }
    }

    private sealed class CurrentTicketStore(bool current = true) :
        IUdpTransportTicketStore
    {
        public bool Current { get; set; } = current;
        private int generationLookupCount;
        public int GenerationLookupCount => Volatile.Read(ref generationLookupCount);
        public string BackendName => "test";
        public bool SupportsMultipleAuthorityInstances => true;
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(Current);
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref generationLookupCount);
            return ValueTask.FromResult(Current);
        }
        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref generationLookupCount);
            return ValueTask.FromResult<IReadOnlyList<bool>>(
                bindings.Select(_ => Current).ToArray());
        }
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId,
            Guid userId,
            Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class BlockingTicketStore : IUdpTransportTicketStore
    {
        private readonly TaskCompletionSource<bool> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int GenerationLookupCount { get; private set; }
        public string BackendName => "blocking_test";
        public bool SupportsMultipleAuthorityInstances => true;
        public void Complete(bool value) => completion.TrySetResult(value);
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken)
        {
            GenerationLookupCount++;
            return new(completion.Task);
        }
        public async ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken)
        {
            GenerationLookupCount++;
            var current = await completion.Task;
            return bindings.Select(_ => current).ToArray();
        }
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId,
            Guid userId,
            Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
