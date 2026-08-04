using System.Security.Cryptography;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

public sealed class UdpTransportTicketStoreTests
{
    [Fact]
    public void DisabledTransport_DoesNotRequireUdpInfrastructure()
    {
        UdpTransportConfigurationPolicy.Validate(
            new(false, true, false, TimeSpan.FromSeconds(15)),
            AuthorityUdpListenerOptions.Disabled,
            hasRedis: false,
            hasCredentialProtector: false,
            isDevelopment: false);
    }

    [Fact]
    public void EnabledListener_RequiresEnabledTicketTransport()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            UdpTransportConfigurationPolicy.Validate(
                new(false, true, false, TimeSpan.FromSeconds(15)),
                AuthorityUdpListenerOptions.Disabled with { Enabled = true },
                hasRedis: true,
                hasCredentialProtector: true,
                isDevelopment: false));

        Assert.Contains("UdpEnabled", exception.Message);
    }

    [Fact]
    public void EnabledProductionTransport_RequiresRedisAndCredentialProtector()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UdpTransportConfigurationPolicy.Validate(
                new(true, true, false, TimeSpan.FromSeconds(15)),
                AuthorityUdpListenerOptions.Disabled,
                hasRedis: true,
                hasCredentialProtector: false,
                isDevelopment: false));
    }

    [Fact]
    public void ExplicitSingleInstanceDevelopment_CanValidateWithoutRedis()
    {
        UdpTransportConfigurationPolicy.Validate(
            new(true, false, true, TimeSpan.FromSeconds(15)),
            AuthorityUdpListenerOptions.Disabled with { Enabled = true },
            hasRedis: false,
            hasCredentialProtector: false,
            isDevelopment: true);
    }

    [Fact]
    public void EnabledNonRootListener_RejectsPrivilegedPort()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UdpTransportConfigurationPolicy.Validate(
                new(true, true, false, TimeSpan.FromSeconds(15)),
                AuthorityUdpListenerOptions.Disabled with
                {
                    Enabled = true,
                    Port = 443
                },
                hasRedis: true,
                hasCredentialProtector: true,
                isDevelopment: false));
    }

    [Fact]
    public void EnabledProductionTransport_ValidatesWithRedisAndCredentials()
    {
        UdpTransportConfigurationPolicy.Validate(
            new(true, true, false, TimeSpan.FromSeconds(15)),
            AuthorityUdpListenerOptions.Disabled with { Enabled = true },
            hasRedis: true,
            hasCredentialProtector: true,
            isDevelopment: false);
    }

    [Fact]
    public void RedisGenerationLookupChunking_BoundsEveryLuaInvocation()
    {
        var chunks = RedisUdpTransportTicketStore
            .CreateGenerationLookupChunks(10_001, 64);

        Assert.Equal(157, chunks.Count);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Count, 1, 64));
        Assert.Equal(0, chunks[0].Offset);
        Assert.Equal(17, chunks[^1].Count);
        Assert.Equal(10_001, chunks.Sum(chunk => chunk.Count));
        for (var index = 1; index < chunks.Count; index++)
            Assert.Equal(
                chunks[index - 1].Offset + chunks[index - 1].Count,
                chunks[index].Offset);
    }

    [Fact]
    public async Task Issue_AssignsGenerationAndReturnsOpaqueShortLivedSecrets()
    {
        var now = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var store = new InMemoryUdpTransportTicketStore(
            new TestTimeProvider(now), TimeSpan.FromSeconds(12));

        var result = await store.Issue(CreateContext(), CancellationToken.None);

        var grant = Assert.IsType<UdpTransportTicketGrant>(result.Grant);
        Assert.Equal(UdpTransportTicketIssueStatus.Issued, result.Status);
        Assert.Equal((ulong)1, grant.Binding.TransportGeneration);
        Assert.Equal(now.AddSeconds(12).ToUnixTimeMilliseconds(),
            grant.ExpiresAtUnixMilliseconds);
        Assert.Equal(32, Decode(grant.Ticket).Length);
        Assert.Equal(32, Decode(grant.DatagramAuthenticationKey).Length);
        Assert.DoesNotContain('.', grant.Ticket);
        Assert.False(store.SupportsMultipleAuthorityInstances);
    }

    [Fact]
    public async Task Issue_ReissueSupersedesPriorTicketAndServerAdvancesGeneration()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var first = Grant(await store.Issue(CreateContext(), CancellationToken.None));
        var second = Grant(await store.Issue(CreateContext(), CancellationToken.None));

        Assert.Equal((ulong)1, first.Binding.TransportGeneration);
        Assert.Equal((ulong)2, second.Binding.TransportGeneration);
        Assert.Equal(UdpTransportTicketRedeemStatus.UnknownTicket,
            (await Redeem(store, first, new AcceptingValidator())).Status);
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, second, new AcceptingValidator())).Status);
        Assert.True(await store.MarkPromotedTransportSessionConnected(
            second.Binding, CancellationToken.None));
        Assert.False(await store.IsTransportGenerationCurrent(
            first.Binding, CancellationToken.None));
        Assert.True(await store.IsTransportGenerationCurrent(
            second.Binding, CancellationToken.None));
    }

    [Fact]
    public async Task GenerationAtomicBoundaryRejectsReissueAfterPromotionWins()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var first = Grant(await store.Issue(
            CreateContext(), CancellationToken.None));
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, first, new AcceptingValidator())).Status);
        Assert.True(await store.MarkPromotedTransportSessionConnected(
            first.Binding, CancellationToken.None));
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);

        var commit = Task.Factory.StartNew(() =>
            ((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecutePromotionIfCurrentCandidate(first.Binding, () =>
            {
                entered.Set();
                release.Wait();
                return true;
            }), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var reissue = Task.Run(async () => await store.Issue(
            CreateContext(), CancellationToken.None));
        await Task.Delay(25);
        Assert.False(reissue.IsCompleted);

        release.Set();
        Assert.True(await commit);
        Assert.Equal(
            UdpTransportTicketIssueStatus.ActiveUdpWriterRequiresFallback,
            (await reissue).Status);
        Assert.False(((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecutePromotionIfCurrentCandidate(first.Binding, () => true));
    }

    [Fact]
    public async Task RedeemedHandshakeIsNotDataplaneCurrentUntilPeerIsConnected()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var grant = Grant(await store.Issue(
            CreateContext(), CancellationToken.None));
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, grant, new AcceptingValidator())).Status);

        Assert.False(await store.IsTransportGenerationCurrent(
            grant.Binding, CancellationToken.None));
        Assert.False(((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecuteIfConnected(grant.Binding, () => true));

        Assert.True(await store.MarkPromotedTransportSessionConnected(
            grant.Binding, CancellationToken.None));
        Assert.True(await store.IsTransportGenerationCurrent(
            grant.Binding, CancellationToken.None));

        await store.RevokeConnectedTransportSession(
            grant.Binding, CancellationToken.None);
        Assert.False(await store.IsTransportGenerationCurrent(
            grant.Binding, CancellationToken.None));
        Assert.False(((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecuteIfConnected(grant.Binding, () => true));
    }

    [Fact]
    public async Task IssuedCandidateIsNotCurrentWhileConnectedWriterRemainsCurrent()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var first = Grant(await store.Issue(
            CreateContext(), CancellationToken.None));
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, first, new AcceptingValidator())).Status);
        Assert.True(await store.MarkPromotedTransportSessionConnected(
            first.Binding, CancellationToken.None));

        var second = Grant(await store.Issue(
            CreateContext(), CancellationToken.None));

        Assert.True(await store.IsTransportGenerationCurrent(
            first.Binding, CancellationToken.None));
        Assert.False(await store.IsTransportGenerationCurrent(
            second.Binding, CancellationToken.None));
    }

    [Fact]
    public async Task FallbackBoundaryRevokesPromotedWriterAndFencesCandidates()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var first = Grant(await store.Issue(
            CreateContext(), CancellationToken.None));
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, first, new AcceptingValidator())).Status);
        Assert.True(await store.MarkPromotedTransportSessionConnected(
            first.Binding, CancellationToken.None));

        var accepted = ((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecuteFallbackGenerationFence(first.Binding, () => true);

        Assert.True(accepted);
        Assert.False(await store.IsTransportGenerationCurrent(
            first.Binding, CancellationToken.None));
        // The writer CAS is the fallback authority. Once it is already HTTP,
        // its callback rejects and the generation boundary must not mutate.
        Assert.False(((IUdpTransportGenerationAtomicBoundary)store)
            .TryExecuteFallbackGenerationFence(first.Binding, () => false));
    }

    [Fact]
    public async Task RevokeRuntimeSession_RemovesTicketAndGenerationLifecycleState()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var context = CreateContext();
        var first = Grant(await store.Issue(context, CancellationToken.None));
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
            (await Redeem(store, first, new AcceptingValidator())).Status);
        Assert.True(await store.MarkPromotedTransportSessionConnected(
            first.Binding, CancellationToken.None));

        await store.RevokeRuntimeSession(context.WorldId, context.UserId,
            context.AvatarEntityId, context.RuntimeSessionId,
            CancellationToken.None);
        var afterRevoke = Grant(await store.Issue(context, CancellationToken.None));

        Assert.False(await store.IsTransportGenerationCurrent(
            first.Binding, CancellationToken.None));
        Assert.Equal((ulong)1, afterRevoke.Binding.TransportGeneration);
    }

    [Fact]
    public async Task Redeem_RequiresProofAndInvalidProofCannotBurnTicket()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var grant = Grant(await store.Issue(CreateContext(), CancellationToken.None));
        var invalid = Redemption(grant) with { Proof = Encode(RandomNumberGenerator.GetBytes(32)) };

        var rejected = await store.Redeem(
            invalid, new AcceptingValidator(), CancellationToken.None);
        var accepted = await store.Redeem(
            Redemption(grant), new AcceptingValidator(), CancellationToken.None);

        Assert.Equal(UdpTransportTicketRedeemStatus.ProofRejected, rejected.Status);
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed, accepted.Status);
        Assert.Equal(grant.Binding, accepted.Binding);
        Assert.Equal(Decode(grant.DatagramAuthenticationKey),
            accepted.DatagramAuthenticationKey);
        CryptographicOperations.ZeroMemory(accepted.DatagramAuthenticationKey!);
    }

    [Fact]
    public async Task Redeem_IsAtomicUnderConcurrencyAndOnlyOneCallerWins()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var grant = Grant(await store.Issue(CreateContext(), CancellationToken.None));
        var redemption = Redemption(grant);
        var validator = new AcceptingValidator();

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            store.Redeem(redemption, validator, CancellationToken.None).AsTask()));

        Assert.Single(results, x =>
            x.Status == UdpTransportTicketRedeemStatus.Redeemed);
        Assert.Equal(15, results.Count(x =>
            x.Status == UdpTransportTicketRedeemStatus.AlreadyRedeemed));
    }

    [Fact]
    public async Task Redeem_RevalidatesCurrentAuthorityRuntimeBindingBeforeConsume()
    {
        var store = new InMemoryUdpTransportTicketStore();
        var grant = Grant(await store.Issue(CreateContext(), CancellationToken.None));

        var stale = await Redeem(store, grant, new RejectingValidator());
        var laterCurrent = await Redeem(store, grant, new AcceptingValidator());

        Assert.Equal(UdpTransportTicketRedeemStatus.BindingNoLongerCurrent, stale.Status);
        Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed, laterCurrent.Status);
    }

    [Fact]
    public async Task Redeem_ExpiredTicketFailsClosedAndReplayCannotReturnKey()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryUdpTransportTicketStore(clock, TimeSpan.FromSeconds(5));
        var grant = Grant(await store.Issue(CreateContext(), CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(5));

        var expired = await Redeem(store, grant, new AcceptingValidator());
        var replay = await Redeem(store, grant, new AcceptingValidator());

        Assert.Equal(UdpTransportTicketRedeemStatus.Expired, expired.Status);
        Assert.Null(expired.DatagramAuthenticationKey);
        Assert.Equal(UdpTransportTicketRedeemStatus.AlreadyRedeemed, replay.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer eyJhbGciOi.fake.token")]
    [InlineData("not-base64url")]
    public async Task Redeem_RejectsMalformedOrBearerLikeCredentials(string ticket)
    {
        var store = new InMemoryUdpTransportTicketStore();
        var result = await store.Redeem(
            new(ticket, "bad", "bad", Guid.NewGuid(), 1),
            new AcceptingValidator(), CancellationToken.None);
        Assert.Equal(UdpTransportTicketRedeemStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public void AesGcmProtector_RoundTripsAndRejectsWrongKeyIdOrTampering()
    {
        var protector = new AesGcmUdpTicketCredentialProtector(
            "test-key-1", RandomNumberGenerator.GetBytes(32));
        var secret = RandomNumberGenerator.GetBytes(32);
        var protectedValue = protector.Protect(secret);

        Assert.True(protector.TryUnprotect("test-key-1", protectedValue, out var restored));
        Assert.Equal(secret, restored);
        Assert.False(protector.TryUnprotect("wrong", protectedValue, out _));
        var rotatedWithoutOldKey = new AesGcmUdpTicketCredentialProtector(
            "test-key-1", RandomNumberGenerator.GetBytes(32));
        Assert.False(rotatedWithoutOldKey.TryUnprotect(
            "test-key-1", protectedValue, out _));
        protectedValue[15] ^= 0x01;
        Assert.False(protector.TryUnprotect("test-key-1", protectedValue, out _));
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(restored);
    }

    [Fact]
    public void Request_DoesNotAcceptClientGenerationAndRequiresCurrentProtocol()
    {
        var valid = new IssueUdpTransportTicketRequest(
            Guid.NewGuid(), RealtimeWireContract.ProtocolVersion);
        Assert.True(valid.IsValid);
        Assert.False((valid with { RuntimeSessionId = Guid.Empty }).IsValid);
        Assert.False((valid with { ProtocolVersion = 0 }).IsValid);
        Assert.DoesNotContain(typeof(IssueUdpTransportTicketRequest).GetProperties(),
            p => p.Name.Contains("Generation", StringComparison.Ordinal));
    }

    private static UdpTransportTicketIssueContext CreateContext() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        "main_zone",
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        RealtimeWireContract.ProtocolVersion);

    private static UdpTransportTicketGrant Grant(UdpTransportTicketIssueResult result) =>
        Assert.IsType<UdpTransportTicketGrant>(result.Grant);

    private static UdpTransportTicketRedemption Redemption(UdpTransportTicketGrant grant)
    {
        var nonce = Encode(RandomNumberGenerator.GetBytes(16));
        var key = Decode(grant.DatagramAuthenticationKey);
        var proof = UdpTransportTicketProof.Create(key, grant.Ticket, nonce,
            grant.Binding.AuthenticatedTransportSessionId,
            grant.Binding.TransportGeneration);
        CryptographicOperations.ZeroMemory(key);
        return new(grant.Ticket, nonce, proof,
            grant.Binding.AuthenticatedTransportSessionId,
            grant.Binding.TransportGeneration);
    }

    private static ValueTask<UdpTransportTicketRedeemResult> Redeem(
        IUdpTransportTicketStore store, UdpTransportTicketGrant grant,
        IUdpTransportRuntimeSessionValidator validator) =>
        store.Redeem(Redemption(grant), validator, CancellationToken.None);

    private static string Encode(byte[] value) => UdpTransportEncoding.Encode(value);
    private static byte[] Decode(string value)
    {
        Assert.True(UdpTransportEncoding.TryDecode(value,
            value.Length == 22 ? 16 : 32, out var result));
        return result;
    }

    private sealed class AcceptingValidator : IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
    private sealed class RejectingValidator : IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now;
        public TestTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
