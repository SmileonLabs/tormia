using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

/// <summary>
/// Deterministic transport fault evidence across the complete server path:
/// authenticated datagram -> bounded consumer -> canonical admission -> intent
/// registry. No socket, wall-clock delay, or gameplay mutation is involved.
/// </summary>
[CollectionDefinition("Authority UDP fault metrics",
    DisableParallelization = true)]
public sealed class AuthorityUdpFaultMetricsCollection { }

[Collection("Authority UDP fault metrics")]
public sealed class AuthorityUdpMotionIntentConsumerFaultTests
{
    [Fact]
    public void Diagnostics_PreserveWriterAndConnectedPresenceFailures()
    {
        using var metrics = new MetricCapture();
        metrics.Start();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();

        diagnostics.RecordMotionIntentRejected("writer_mismatch");
        diagnostics.RecordHandshakeRejected("connected_presence_rejected");

        Assert.Equal(1, metrics.Count(
            AuthorityUdpListenerDiagnostics.MotionIntentRejectedName,
            "reason", "writer_mismatch"));
        Assert.Equal(1, metrics.Count(
            AuthorityUdpListenerDiagnostics.HandshakeRejectedName,
            "reason", "connected_presence"));
    }

    [Fact]
    public async Task GenerationFence_InvalidBatchResultFailsClosedWithBoundedMetric()
    {
        using var metrics = new MetricCapture();
        metrics.Start();
        using var diagnostics = new AuthorityUdpListenerDiagnostics();
        var fence = new AuthorityUdpTransportGenerationFence(
            new InvalidBatchTicketStore(),
            AuthorityUdpListenerOptions.Disabled with
            {
                GenerationFenceTimeout = TimeSpan.FromSeconds(1)
            },
            diagnostics);
        var bindings = Enumerable.Range(0, 3)
            .Select(_ => new UdpTransportTicketBinding(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "main_zone", Guid.NewGuid(), 1, Guid.NewGuid(),
                RealtimeWireContract.ProtocolVersion))
            .ToArray();

        var result = await fence.AreCurrentBatch(
            bindings, CancellationToken.None);

        Assert.Equal(new[] { false, false, false }, result);
        Assert.Equal(1, metrics.Count(
            AuthorityUdpListenerDiagnostics.GenerationFenceBatchName,
            "outcome", "invalid_result"));
        Assert.Equal(3, metrics.Count(
            AuthorityUdpListenerDiagnostics.GenerationFenceBatchSizeName,
            "outcome", "invalid_result"));
    }

    [Fact]
    public async Task PacketLoss_DoesNotInventMissingIntentAndPublishesLatest()
    {
        await using var fixture = await Fixture.Create();

        await fixture.Deliver(packetSequence: 1, inputSequence: 1);
        await fixture.Deliver(packetSequence: 3, inputSequence: 3);
        Assert.Equal(2, await fixture.Consumer.ProcessAvailable(
            CancellationToken.None));

        var stored = await fixture.StoredIntent();
        Assert.NotNull(stored);
        Assert.Equal(3, stored.Sequence);
        Assert.Equal(1, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.MotionIntentPublishedName));
    }

    [Fact]
    public async Task ReorderedDatagrams_CoalesceToHighestInputSequence()
    {
        await using var fixture = await Fixture.Create();

        await fixture.Deliver(100, 100);
        await fixture.Deliver(102, 102);
        await fixture.Deliver(101, 101);
        Assert.Equal(3, await fixture.Consumer.ProcessAvailable(
            CancellationToken.None));

        var stored = await fixture.StoredIntent();
        Assert.NotNull(stored);
        Assert.Equal(102, stored.Sequence);
        Assert.Equal(2, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.MotionIntentRejectedName,
            "reason", "coalesced"));
    }

    [Fact]
    public async Task ReplayedDatagram_IsDroppedBeforeCanonicalAdmission()
    {
        await using var fixture = await Fixture.Create();

        await fixture.Deliver(7, 7);
        await fixture.Deliver(7, 7);
        Assert.Equal(1, await fixture.Consumer.ProcessAvailable(
            CancellationToken.None));

        Assert.Equal(7, (await fixture.StoredIntent())!.Sequence);
        Assert.Equal(1, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.DatagramDroppedName,
            "reason", "replay"));
        Assert.Equal(1, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.MotionIntentPublishedName));
    }

    [Fact]
    public async Task TooOldDatagram_IsDroppedWithoutRewindingRegistry()
    {
        await using var fixture = await Fixture.Create();

        await fixture.Deliver(100, 100);
        await fixture.Deliver(36, 36);
        Assert.Equal(1, await fixture.Consumer.ProcessAvailable(
            CancellationToken.None));

        Assert.Equal(100, (await fixture.StoredIntent())!.Sequence);
        Assert.Equal(1, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.DatagramDroppedName,
            "reason", "replay"));
    }

    [Fact]
    public async Task QueuedIntentAfterFallback_IsFencedBeforeRegistryCommit()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Deliver(1, 1);

        var writer = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        var fallback = await fixture.Intents.FallbackToHttpWriter(
            fixture.Binding,
            writer!.WriterEpoch,
            writer.WorldRevision,
            CancellationToken.None);
        Assert.True(fallback.Accepted);

        Assert.Equal(1, await fixture.Consumer.ProcessAvailable(
            CancellationToken.None));
        Assert.Null(await fixture.StoredIntent());
        Assert.Equal("http", (await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId,
            CancellationToken.None))!.Mode);
        Assert.Equal(1, fixture.Metrics.Count(
            AuthorityUdpListenerDiagnostics.MotionIntentRejectedName,
            "reason", "old_generation"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Guid WorldId { get; } = Guid.NewGuid();
        internal Guid UserId { get; } = Guid.NewGuid();
        internal Guid AvatarId { get; } = Guid.NewGuid();
        internal Guid RuntimeSessionId { get; } = Guid.NewGuid();
        internal UdpTransportTicketBinding Binding { get; private set; } = null!;
        internal InMemoryWorldPlayerIntentRegistry Intents { get; private set; } = null!;
        internal AuthorityUdpMotionIntentConsumer Consumer { get; private set; } = null!;
        internal MetricCapture Metrics { get; } = new();

        private readonly InMemoryUdpTransportTicketStore tickets = new();
        private readonly InMemoryWorldPlayerMotionRuntimeRegistry motion = new();
        private readonly InMemoryWorldRevisionRuntimeRegistry revisions = new();
        private readonly AuthorityUdpPeerSessionRegistry sessions = new();
        private readonly AuthorityUdpListenerDiagnostics diagnostics = new();
        private readonly UdpAuthenticatedMotionIntentChannel channel = new(32);
        private AuthorityUdpDatagramProcessor processor = null!;
        private byte[] authenticationKey = Array.Empty<byte>();

        internal static async Task<Fixture> Create()
        {
            var value = new Fixture();
            await value.Initialize();
            return value;
        }

        private async Task Initialize()
        {
            Metrics.Start();
            await motion.Activate(new WorldPlayerMotionState(
                WorldId, AvatarId, "main_zone",
                0d, 0d, 0d, 0, "idle", 1,
                RuntimeSessionId: RuntimeSessionId), CancellationToken.None);
            await revisions.ObserveCommitted(WorldId, 1,
                CancellationToken.None);
            Intents = new(motion, tickets, revisions);
            var initialized = await Intents.InitializeHttpWriter(
                new PlayerMotionTransportWriterState(
                    WorldId, UserId, AvatarId, "main_zone",
                    RuntimeSessionId, "http", 1, 1),
                CancellationToken.None);
            Assert.True(initialized.Accepted);

            var issued = await tickets.Issue(
                new UdpTransportTicketIssueContext(
                    WorldId, UserId, AvatarId, "main_zone",
                    RuntimeSessionId, RealtimeWireContract.ProtocolVersion),
                CancellationToken.None);
            var grant = Assert.IsType<UdpTransportTicketGrant>(issued.Grant);
            authenticationKey = Decode(grant.DatagramAuthenticationKey);
            var nonce = UdpTransportEncoding.Encode(
                RandomNumberGenerator.GetBytes(16));
            var proof = UdpTransportTicketProof.Create(
                authenticationKey, grant.Ticket, nonce,
                grant.Binding.AuthenticatedTransportSessionId,
                grant.Binding.TransportGeneration);
            var redeemed = await tickets.Redeem(
                new UdpTransportTicketRedemption(
                    grant.Ticket, nonce, proof,
                    grant.Binding.AuthenticatedTransportSessionId,
                    grant.Binding.TransportGeneration),
                new AcceptingRuntimeValidator(), CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redeemed.Status);
            CryptographicOperations.ZeroMemory(
                redeemed.DatagramAuthenticationKey!);
            Assert.True(await tickets.MarkPromotedTransportSessionConnected(
                grant.Binding, CancellationToken.None));
            var promoted = await Intents.PromoteUdpWriter(
                grant.Binding, initialized.Writer!.WriterEpoch,
                initialized.Writer.WorldRevision, CancellationToken.None);
            Assert.True(promoted.Accepted);
            Binding = grant.Binding;
            Assert.True(sessions.TryAdd(17, Binding, authenticationKey));

            var options = AuthorityUdpListenerOptions.Disabled with
            {
                GenerationFenceTimeout = TimeSpan.FromSeconds(1)
            };
            var fence = new AuthorityUdpTransportGenerationFence(
                tickets, options);
            var gateway = new AcceptingGateway();
            var admission = new PlayerMotionIntentAdmissionService(
                gateway, motion, Intents, revisions);
            processor = new AuthorityUdpDatagramProcessor(
                sessions, fence, channel, diagnostics);
            Consumer = new AuthorityUdpMotionIntentConsumer(
                channel, fence, admission, Intents, diagnostics,
                NullLogger<AuthorityUdpMotionIntentConsumer>.Instance);
        }

        internal async Task Deliver(
            ulong packetSequence,
            ulong inputSequence)
        {
            var packet = CreatePacket(packetSequence, inputSequence);
            await processor.Process(17, packet, CancellationToken.None);
        }

        internal Task<WorldPlayerIntent?> StoredIntent() => Intents.Get(
            WorldId, AvatarId, CancellationToken.None);

        private byte[] CreatePacket(
            ulong packetSequence,
            ulong inputSequence)
        {
            var payload = new MotionIntentPayload
            {
                WorldId = WorldId,
                ZoneKey = "main_zone",
                ActorEntityId = AvatarId,
                InputSequence = inputSequence,
                ClientTimestampMilliseconds = 1,
                MoveZ = 1f,
                RequestedSpeed = 4f,
                FacingZ = 1f,
                Flags = MotionIntentFlags.None
            };
            Assert.True(
                RealtimeWireCodec.TryCreateMotionIntentForAuthentication(
                    new RealtimePacketMetadata(
                        Binding.AuthenticatedTransportSessionId,
                        Binding.TransportGeneration,
                        packetSequence,
                        0),
                    payload,
                    out var datagram,
                    out var error),
                error.ToString());
            Assert.True(RealtimeWireCodec.TryGetAuthenticatedRegion(
                datagram, out var region, out error), error.ToString());
            var digest = HMACSHA256.HashData(authenticationKey, region);
            Assert.True(RealtimeWireCodec.TryWriteAuthenticationTag(
                datagram,
                digest.AsSpan(
                    0,
                    RealtimeWireContract.RequiredAuthenticationTagLength),
                out error), error.ToString());
            CryptographicOperations.ZeroMemory(digest);
            return datagram;
        }

        public ValueTask DisposeAsync()
        {
            sessions.Dispose();
            diagnostics.Dispose();
            Metrics.Dispose();
            if (authenticationKey.Length > 0)
                CryptographicOperations.ZeroMemory(authenticationKey);
            return ValueTask.CompletedTask;
        }

        private static byte[] Decode(string value)
        {
            Assert.True(UdpTransportEncoding.TryDecode(value, 32, out var key));
            return key;
        }
    }

    private sealed class AcceptingRuntimeValidator :
        IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class InvalidBatchTicketStore : IUdpTransportTicketStore
    {
        public string BackendName => "invalid_batch_test";
        public bool SupportsMultipleAuthorityInstances => true;
        public ValueTask<UdpTransportTicketIssueResult> Issue(
            UdpTransportTicketIssueContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<UdpTransportTicketRedeemResult> Redeem(
            UdpTransportTicketRedemption redemption,
            IUdpTransportRuntimeSessionValidator runtimeValidator,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> MarkPromotedTransportSessionConnected(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
        public ValueTask<bool> IsTransportGenerationCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<bool>> AreTransportGenerationsCurrent(
            IReadOnlyList<UdpTransportTicketBinding> bindings,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<bool>>(new[] { true });
        public ValueTask RevokeConnectedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokePromotedTransportSession(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RevokeRuntimeSession(
            Guid worldId, Guid userId, Guid avatarEntityId,
            Guid runtimeSessionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class AcceptingGateway : IPlayerMotionIntentAuthorityGateway
    {
        private static readonly PlayerMotionActionContract Contract =
            new("development_core", "1.0.0", "move_avatar", 1);

        public Task<bool> AvatarBelongsToUser(
            Guid worldId, Guid userId, Guid avatarEntityId,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ZoneExists(
            Guid worldId, Guid userId, string zoneKey,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PlayerMotionActionContract?> ResolveCurrentContract(
            Guid worldId, Guid userId, Guid avatarEntityId, string zoneKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlayerMotionActionContract?>(Contract);
        public Task<long?> GetCurrentRevision(
            Guid worldId, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(1);
        public Task<ActionPreviewEvaluationResult> Preview(
            Guid worldId, Guid userId, Guid avatarEntityId,
            PlayerMotionActionContract contract,
            CancellationToken cancellationToken) => Task.FromResult(
            new ActionPreviewEvaluationResult(
                true, null, avatarEntityId, avatarEntityId, null,
                contract.PackageId, contract.PackageVersion,
                contract.ActionId, contract.DefinitionVersion,
                null, Guid.NewGuid(), 0));
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener listener = new();
        internal ConcurrentBag<Sample> Samples { get; } = new();

        internal void Start()
        {
            listener.InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name ==
                    AuthorityUdpListenerDiagnostics.MeterName)
                    active.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((
                instrument, measurement, tags, _) =>
            {
                var copy = new KeyValuePair<string, object?>[tags.Length];
                tags.CopyTo(copy);
                Samples.Add(new(instrument.Name, measurement, copy));
            });
            listener.Start();
        }

        internal int Count(
            string name,
            string? tagName = null,
            string? tagValue = null) => checked((int)Samples
            .Where(sample => sample.Name == name &&
                (tagName is null || sample.Tags.Any(tag =>
                    tag.Key == tagName &&
                    string.Equals(tag.Value?.ToString(), tagValue,
                        StringComparison.Ordinal))))
            .Sum(sample => sample.Value));

        public void Dispose() => listener.Dispose();
        internal sealed record Sample(
            string Name,
            long Value,
            IReadOnlyList<KeyValuePair<string, object?>> Tags);
    }
}
