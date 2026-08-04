using Xunit;

public sealed class PlayerMotionIntentAdmissionTests
{
    [Fact]
    public async Task UdpBeforeExplicitPromotionIsRejectedAndHttpRemainsCanonical()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var http = fixture.Candidate(1, PlayerMotionIntentTransport.Http,
            fixture.Gateway.Contract);
        var udp = fixture.Candidate(1, PlayerMotionIntentTransport.Udp);

        var udpResult = await fixture.Service.Submit(
            udp, CancellationToken.None);
        var httpResult = await fixture.Service.Submit(
            http, CancellationToken.None);

        Assert.False(udpResult.Accepted);
        Assert.True(httpResult.Accepted);
        var stored = await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(1, stored!.Sequence);
    }

    [Fact]
    public async Task UdpUsesAuthorityResolvedContractAndPublishesCanonicalIntent()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(fixture.Gateway.Contract,
            fixture.Gateway.LastPreviewedContract);
        var stored = await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(fixture.RuntimeSessionId, stored!.RuntimeSessionId);
        Assert.Equal(fixture.UserId, stored.UserId);
    }

    [Fact]
    public async Task RemovedRuleBlockRemovesUdpLocomotionAdmission()
    {
        var fixture = Fixture.Create();
        fixture.Gateway.Contract = null;

        var result = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_contract_missing", result.RejectionCode);
        Assert.Null(await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None));
    }

    [Fact]
    public async Task HttpClaimCannotOverrideCurrentAuthorityContract()
    {
        var fixture = Fixture.Create();
        var other = fixture.Gateway.Contract! with { ActionId = "other_action" };

        var result = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Http, other),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_contract_mismatch", result.RejectionCode);
        Assert.Equal(fixture.Gateway.Contract,
            fixture.Gateway.LastPreviewedContract);
    }

    [Fact]
    public async Task StaleRuntimeSessionFailsBeforeRuleEvaluation()
    {
        var fixture = Fixture.Create();
        var stale = fixture.Candidate(1, PlayerMotionIntentTransport.Udp) with
        {
            RuntimeSessionId = Guid.NewGuid()
        };

        var result = await fixture.Service.Submit(
            stale, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("player_runtime_session_mismatch", result.RejectionCode);
        Assert.Null(fixture.Gateway.LastPreviewedContract);
    }

    [Fact]
    public async Task ProcessedSequenceCannotBeReplayedAfterIntentWasCleared()
    {
        var fixture = Fixture.Create(lastProcessedSequence: 4);

        var result = await fixture.Service.Submit(
            fixture.Candidate(4, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("stale_player_intent", result.RejectionCode);
    }

    [Fact]
    public async Task DurableLocomotionRuleEffectFailsClosed()
    {
        var fixture = Fixture.Create();
        fixture.Gateway.MutationCount = 1;

        var result = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_rule_must_be_ephemeral", result.RejectionCode);
    }

    [Fact]
    public async Task RevokedUdpGenerationFailsImmediatelyBeforeRegistryPublish()
    {
        var fixture = Fixture.Create();

        var result = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None,
            _ => ValueTask.FromResult(false));

        Assert.False(result.Accepted);
        Assert.Equal("stale_transport_generation", result.RejectionCode);
        Assert.Null(await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None));
    }

    [Fact]
    public async Task ExactRevisionContractIsCompiledOnceForIncreasingUdpInput()
    {
        var fixture = Fixture.Create();

        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);

        Assert.Equal(1, fixture.Gateway.ResolveCalls);
        Assert.Equal(1, fixture.Gateway.PreviewCalls);
        Assert.Equal(0, fixture.Gateway.RevisionCalls);
    }

    [Fact]
    public async Task WorldRevisionChangeRetractsCachedApproval()
    {
        var fixture = Fixture.Create();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        await fixture.AdvanceRevision();
        fixture.Gateway.Contract = null;

        var result = await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_contract_missing", result.RejectionCode);
        Assert.Equal(2, fixture.Gateway.ResolveCalls);
    }

    [Fact]
    public async Task AcceptedIntentRefreshesWriterRevisionWithoutChangingWriterEpoch()
    {
        var fixture = Fixture.Create();
        var before = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        await fixture.AdvanceRevision();

        var result = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);
        var after = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(before!.WriterEpoch, after!.WriterEpoch);
        Assert.Equal(2, after.WorldRevision);
    }

    [Fact]
    public async Task PendingRevisionFailsClosedBeforeDurableCommit()
    {
        var fixture = Fixture.Create();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        await fixture.Revisions.MarkPending(
            fixture.WorldId, 1, 2, CancellationToken.None);

        var result = await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_contract_revision_unavailable",
            result.RejectionCode);
        var stored = await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(1, stored!.Sequence);
    }

    [Fact]
    public async Task CrashGapReconcilesDurableRevisionAndNeverUsesRemovedRuleCache()
    {
        var fixture = Fixture.Create();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        await fixture.Revisions.MarkPending(
            fixture.WorldId, 1, 2, CancellationToken.None);
        fixture.Gateway.Revision = 2;
        fixture.Gateway.Contract = null;

        var result = await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("locomotion_contract_missing", result.RejectionCode);
        Assert.Equal(2, fixture.Gateway.ResolveCalls);
        var stored = await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(1, stored!.Sequence);
    }

    [Fact]
    public async Task ExplicitUdpPromotionFencesHigherHttpSequenceInSameRuntimeSession()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Http,
                fixture.Gateway.Contract),
            CancellationToken.None)).Accepted);
        await fixture.PromoteNextUdp();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);

        var rejected = await fixture.Service.Submit(
            fixture.Candidate(3, PlayerMotionIntentTransport.Http,
                fixture.Gateway.Contract),
            CancellationToken.None);

        Assert.False(rejected.Accepted);
        var stored = await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(2, stored!.Sequence);
        Assert.Equal("udp", stored.Transport);
    }

    [Fact]
    public async Task ActiveUdpWriterRejectsDirectUdpRekeyUntilExplicitFallback()
    {
        var fixture = Fixture.Create();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        var reissue = await fixture.Tickets.Issue(
            fixture.TicketIssueContext(), CancellationToken.None);
        var current = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);

        Assert.Equal(
            UdpTransportTicketIssueStatus.ActiveUdpWriterRequiresFallback,
            reissue.Status);
        Assert.Null(reissue.Grant);
        var unchanged = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(current, unchanged);
    }

    [Fact]
    public async Task ExplicitFallbackIsIdempotentAndReenablesOnlyNewHttpEpoch()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var binding = await fixture.PromoteNextUdp();
        var udpWriter = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);

        var fallback = await fixture.Intents.FallbackToHttpWriter(
            binding, udpWriter!.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);
        var retry = await fixture.Intents.FallbackToHttpWriter(
            binding, udpWriter.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);

        Assert.True(fallback.Accepted);
        Assert.True(retry.Accepted);
        Assert.Equal("http", fallback.Writer!.Mode);
        Assert.Equal(udpWriter.WriterEpoch + 1,
            fallback.Writer.WriterEpoch);
        Assert.Equal(fallback.Writer, retry.Writer);
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Http,
                fixture.Gateway.Contract),
            CancellationToken.None)).Accepted);
    }

    [Fact]
    public async Task DisconnectedPeerCanFallbackUsingExactCurrentUdpWriter()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var binding = await fixture.PromoteNextUdp();
        var udpWriter = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        await fixture.Tickets.RevokeConnectedTransportSession(
            binding, CancellationToken.None);

        var fallback = await fixture.Intents.FallbackToHttpWriter(
            binding, udpWriter!.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);

        Assert.True(fallback.Accepted);
        Assert.Equal("http", fallback.Writer!.Mode);
    }

    [Fact]
    public async Task QueuedUdpIntentCannotCommitAfterFallback()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var binding = await fixture.PromoteNextUdp();
        var queued = fixture.Candidate(
            1, PlayerMotionIntentTransport.Udp);
        var udpWriter = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);

        var fallback = await fixture.Intents.FallbackToHttpWriter(
            binding, udpWriter!.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);
        var rejected = await fixture.Service.Submit(
            queued, CancellationToken.None);

        Assert.True(fallback.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Null(await fixture.Intents.Get(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitFallbackIsRequiredBeforeCandidateReissue()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var active = await fixture.PromoteNextUdp();
        var udpWriter = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        var rejected = await fixture.Tickets.Issue(
            fixture.TicketIssueContext(), CancellationToken.None);

        Assert.Equal(
            UdpTransportTicketIssueStatus.ActiveUdpWriterRequiresFallback,
            rejected.Status);

        var fallback = await fixture.Intents.FallbackToHttpWriter(
            active, udpWriter!.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);

        Assert.True(fallback.Accepted);
        Assert.Equal("http", fallback.Writer!.Mode);
        var candidate = await fixture.IssueAndRedeemUdp();
        Assert.True(candidate.Binding.TransportGeneration >
            active.TransportGeneration);
    }

    [Fact]
    public async Task PromotionRejectsStaleWriterEpochWithoutChangingHttpWriter()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var grant = await fixture.IssueAndRedeemUdp();

        var rejected = await fixture.Intents.PromoteUdpWriter(
            grant.Binding, 99, 1, CancellationToken.None);
        var writer = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);

        Assert.False(rejected.Accepted);
        Assert.Equal("http", writer!.Mode);
        Assert.Equal(1, writer.WriterEpoch);
        Assert.Equal(writer, rejected.Writer);
    }

    [Fact]
    public async Task FailedPromotionCanRecoverThroughIdempotentCurrentHttpFallback()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var grant = await fixture.IssueAndRedeemUdp();
        var current = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        var rejected = await fixture.Intents.PromoteUdpWriter(
            grant.Binding, 99, current!.WorldRevision,
            CancellationToken.None);

        var recovered = await fixture.Intents.FallbackToHttpWriter(
            grant.Binding, current.WriterEpoch, current.WorldRevision,
            CancellationToken.None);

        Assert.False(rejected.Accepted);
        Assert.True(recovered.Accepted);
        Assert.Equal("http", recovered.Writer!.Mode);
        Assert.Equal(current.WriterEpoch, recovered.Writer.WriterEpoch);
        Assert.False(await fixture.Tickets.IsTransportGenerationCurrent(
            grant.Binding, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshedUdpWriterFallsBackWithStaleTicketRevision()
    {
        var fixture = Fixture.Create();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        await fixture.AdvanceRevision();
        Assert.True((await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None)).Accepted);
        var refreshed = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        Assert.Equal(2, refreshed!.WorldRevision);

        var binding = new UdpTransportTicketBinding(
            refreshed.WorldId, refreshed.UserId, refreshed.AvatarEntityId,
            refreshed.ZoneKey, refreshed.RuntimeSessionId,
            refreshed.TransportGeneration,
            refreshed.AuthenticatedTransportSessionId,
            Tormia.Ontology.Realtime.Protocol
                .RealtimeWireContract.ProtocolVersion);
        var fallback = await fixture.Intents.FallbackToHttpWriter(
            binding, refreshed.WriterEpoch,
            expectedWorldRevision: 1,
            cancellationToken: CancellationToken.None);

        Assert.True(fallback.Accepted);
        Assert.Equal("http", fallback.Writer!.Mode);
        Assert.Equal(2, fallback.Writer.WorldRevision);
        Assert.Equal(refreshed.WriterEpoch + 1,
            fallback.Writer.WriterEpoch);
    }

    [Fact]
    public async Task UdpPromotionIdempotencyRejectsCrossUserAndCrossZoneScope()
    {
        var fixture = Fixture.Create();
        var writer = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        var exact = new UdpTransportTicketBinding(
            writer!.WorldId, writer.UserId, writer.AvatarEntityId,
            writer.ZoneKey, writer.RuntimeSessionId,
            writer.TransportGeneration,
            writer.AuthenticatedTransportSessionId,
            Tormia.Ontology.Realtime.Protocol
                .RealtimeWireContract.ProtocolVersion);

        var crossUser = await fixture.Intents.PromoteUdpWriter(
            exact with { UserId = Guid.NewGuid() },
            writer.WriterEpoch - 1, writer.WorldRevision,
            CancellationToken.None);
        var crossZone = await fixture.Intents.PromoteUdpWriter(
            exact with { ZoneKey = "zone_other" },
            writer.WriterEpoch - 1, writer.WorldRevision,
            CancellationToken.None);

        Assert.False(crossUser.Accepted);
        Assert.Null(crossUser.Writer);
        Assert.False(crossZone.Accepted);
        Assert.Null(crossZone.Writer);
    }

    [Fact]
    public async Task HttpFallbackIdempotencyRejectsCrossUserAndCrossZoneScope()
    {
        var fixture = Fixture.Create(promoteUdp: false);
        var active = await fixture.PromoteNextUdp();
        var udpWriter = await fixture.Intents.GetWriter(
            fixture.WorldId, fixture.AvatarId, CancellationToken.None);
        var fallback = await fixture.Intents.FallbackToHttpWriter(
            active, udpWriter!.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);
        Assert.True(fallback.Accepted);

        var crossUser = await fixture.Intents.FallbackToHttpWriter(
            active with { UserId = Guid.NewGuid() },
            udpWriter.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);
        var crossZone = await fixture.Intents.FallbackToHttpWriter(
            active with { ZoneKey = "zone_other" },
            udpWriter.WriterEpoch, udpWriter.WorldRevision,
            CancellationToken.None);

        Assert.False(crossUser.Accepted);
        Assert.Null(crossUser.Writer);
        Assert.False(crossZone.Accepted);
        Assert.Null(crossZone.Writer);
    }

    [Fact]
    public async Task ConcurrentSameRevisionAdmissionBuildsContractOnce()
    {
        var fixture = Fixture.Create();
        fixture.Gateway.PreviewDelayMilliseconds = 30;

        await Task.WhenAll(Enumerable.Range(1, 12).Select(sequence =>
            fixture.Service.Submit(
                fixture.Candidate(sequence, PlayerMotionIntentTransport.Udp),
                CancellationToken.None)));

        Assert.Equal(1, fixture.Gateway.ResolveCalls);
        Assert.Equal(1, fixture.Gateway.PreviewCalls);
    }

    [Fact]
    public async Task TransientPreviewFailureDoesNotPoisonContractCache()
    {
        var fixture = Fixture.Create();
        fixture.Gateway.PreviewAccepted = false;
        fixture.Gateway.PreviewRejectionCode = "authority_busy";
        var first = await fixture.Service.Submit(
            fixture.Candidate(1, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);
        fixture.Gateway.PreviewAccepted = true;

        var second = await fixture.Service.Submit(
            fixture.Candidate(2, PlayerMotionIntentTransport.Udp),
            CancellationToken.None);

        Assert.False(first.Accepted);
        Assert.Equal("authority_busy", first.RejectionCode);
        Assert.True(second.Accepted);
        Assert.Equal(2, fixture.Gateway.PreviewCalls);
    }

    [Fact]
    public async Task InvalidNumericPayloadFailsBeforeAuthorityLookup()
    {
        var fixture = Fixture.Create();
        var invalid = fixture.Candidate(1, PlayerMotionIntentTransport.Udp) with
        {
            MoveX = float.NaN
        };

        var result = await fixture.Service.Submit(
            invalid, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("invalid_player_intent", result.RejectionCode);
        Assert.Equal(0, fixture.Gateway.OwnershipChecks);
    }

    private sealed class Fixture
    {
        internal Guid WorldId { get; } = Guid.NewGuid();
        internal Guid UserId { get; } = Guid.NewGuid();
        internal Guid AvatarId { get; } = Guid.NewGuid();
        internal Guid RuntimeSessionId { get; } = Guid.NewGuid();
        internal Guid AuthenticatedTransportSessionId { get; private set; }
        internal FakeGateway Gateway { get; } = new();
        internal InMemoryWorldPlayerIntentRegistry Intents { get; private set; } = null!;
        internal InMemoryWorldPlayerMotionRuntimeRegistry Motion { get; } = new();
        internal InMemoryUdpTransportTicketStore Tickets { get; } = new();
        internal InMemoryWorldRevisionRuntimeRegistry Revisions { get; } = new();
        internal PlayerMotionIntentAdmissionService Service { get; private set; } = null!;

        internal static Fixture Create(
            long lastProcessedSequence = 0,
            bool promoteUdp = true)
        {
            var value = new Fixture();
            value.Intents = new(value.Motion, value.Tickets, value.Revisions);
            value.Service = new(
                value.Gateway, value.Motion, value.Intents, value.Revisions);
            value.Motion.Activate(
                new WorldPlayerMotionState(
                    value.WorldId,
                    value.AvatarId,
                    "world_main",
                    0d, 0d, 0d,
                    lastProcessedSequence,
                    "idle",
                    1,
                    RuntimeSessionId: value.RuntimeSessionId),
                CancellationToken.None).GetAwaiter().GetResult();
            value.Revisions.ObserveCommitted(
                value.WorldId, 1, CancellationToken.None)
                .GetAwaiter().GetResult();
            value.Intents.InitializeHttpWriter(
                new PlayerMotionTransportWriterState(
                    value.WorldId, value.UserId, value.AvatarId,
                    "world_main", value.RuntimeSessionId,
                    "http", 1, 1),
                CancellationToken.None).GetAwaiter().GetResult();
            if (promoteUdp)
                value.PromoteNextUdp().GetAwaiter().GetResult();
            return value;
        }

        internal async Task AdvanceRevision()
        {
            Gateway.Revision++;
            await Revisions.ObserveCommitted(
                WorldId, Gateway.Revision, CancellationToken.None);
        }

        internal async Task<UdpTransportTicketBinding> PromoteNextUdp()
        {
            var grant = await IssueAndRedeemUdp();
            var current = await Intents.GetWriter(
                WorldId, AvatarId, CancellationToken.None);
            var promoted = await Intents.PromoteUdpWriter(
                grant.Binding,
                current!.WriterEpoch,
                current.WorldRevision,
                CancellationToken.None);
            Assert.True(promoted.Accepted);
            AuthenticatedTransportSessionId =
                grant.Binding.AuthenticatedTransportSessionId;
            return grant.Binding;
        }

        internal async Task<UdpTransportTicketGrant> IssueAndRedeemUdp()
        {
            var result = await Tickets.Issue(
                TicketIssueContext(),
                CancellationToken.None);
            var grant = result.Grant!;
            var nonce = UdpTransportEncoding.Encode(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            Assert.True(UdpTransportEncoding.TryDecode(
                grant.DatagramAuthenticationKey, 32, out var key));
            var proof = UdpTransportTicketProof.Create(
                key, grant.Ticket, nonce,
                grant.Binding.AuthenticatedTransportSessionId,
                grant.Binding.TransportGeneration);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            var redemption = await Tickets.Redeem(
                new UdpTransportTicketRedemption(
                    grant.Ticket, nonce, proof,
                    grant.Binding.AuthenticatedTransportSessionId,
                    grant.Binding.TransportGeneration),
                new AcceptingRuntimeValidator(),
                CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redemption.Status);
            Assert.True(await Tickets.MarkPromotedTransportSessionConnected(
                grant.Binding, CancellationToken.None));
            return grant;
        }

        internal UdpTransportTicketIssueContext TicketIssueContext() => new(
            WorldId, UserId, AvatarId, "world_main", RuntimeSessionId,
            Tormia.Ontology.Realtime.Protocol.RealtimeWireContract
                .ProtocolVersion);

        internal PlayerMotionIntentCandidate Candidate(
            long sequence,
            PlayerMotionIntentTransport transport,
            PlayerMotionActionContract? claimed = null) =>
            new(
                WorldId,
                UserId,
                AvatarId,
                "world_main",
                RuntimeSessionId,
                sequence,
                1f,
                0f,
                5f,
                false,
                0f,
                0f,
                0f,
                transport,
                claimed,
                transport == PlayerMotionIntentTransport.Udp
                    ? AuthenticatedTransportSessionId : Guid.Empty,
                transport == PlayerMotionIntentTransport.Udp
                    ? Intents.GetWriter(WorldId, AvatarId,
                        CancellationToken.None).GetAwaiter().GetResult()!
                        .TransportGeneration
                    : 0UL,
                Intents.GetWriter(WorldId, AvatarId,
                    CancellationToken.None).GetAwaiter().GetResult()!
                    .WriterEpoch);

        private sealed class AcceptingRuntimeValidator :
            IUdpTransportRuntimeSessionValidator
        {
            public ValueTask<bool> IsCurrent(
                UdpTransportTicketBinding binding,
                CancellationToken cancellationToken) =>
                ValueTask.FromResult(true);
        }
    }

    private sealed class FakeGateway : IPlayerMotionIntentAuthorityGateway
    {
        internal PlayerMotionActionContract? Contract { get; set; } =
            new("development_core", "1.0.0", "move_avatar", 1);
        internal PlayerMotionActionContract? LastPreviewedContract { get; private set; }
        internal int MutationCount { get; set; }
        internal int OwnershipChecks { get; private set; }
        internal int ResolveCalls { get; private set; }
        internal int PreviewCalls { get; private set; }
        internal long Revision { get; set; } = 1;
        internal int RevisionCalls { get; private set; }
        internal bool PreviewAccepted { get; set; } = true;
        internal string? PreviewRejectionCode { get; set; }
        internal int PreviewDelayMilliseconds { get; set; }

        public Task<bool> AvatarBelongsToUser(
            Guid worldId, Guid userId, Guid avatarEntityId,
            CancellationToken cancellationToken)
        {
            OwnershipChecks++;
            return Task.FromResult(true);
        }

        public Task<bool> ZoneExists(
            Guid worldId, Guid userId, string zoneKey,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<PlayerMotionActionContract?> ResolveCurrentContract(
            Guid worldId, Guid userId, Guid avatarEntityId, string zoneKey,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            return Task.FromResult(Contract);
        }

        public Task<long?> GetCurrentRevision(
            Guid worldId, CancellationToken cancellationToken)
        {
            RevisionCalls++;
            return Task.FromResult<long?>(Revision);
        }

        public async Task<ActionPreviewEvaluationResult> Preview(
            Guid worldId, Guid userId, Guid avatarEntityId,
            PlayerMotionActionContract contract,
            CancellationToken cancellationToken)
        {
            PreviewCalls++;
            LastPreviewedContract = contract;
            if (PreviewDelayMilliseconds > 0)
                await Task.Delay(PreviewDelayMilliseconds, cancellationToken);
            return new ActionPreviewEvaluationResult(
                PreviewAccepted,
                PreviewRejectionCode,
                avatarEntityId,
                avatarEntityId,
                null,
                contract.PackageId,
                contract.PackageVersion,
                contract.ActionId,
                contract.DefinitionVersion,
                null,
                Guid.NewGuid(),
                MutationCount);
        }
    }
}
