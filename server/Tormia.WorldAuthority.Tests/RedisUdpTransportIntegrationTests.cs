using System.Security.Cryptography;
using StackExchange.Redis;
using Tormia.Ontology.Realtime.Protocol;
using Xunit;

/// <summary>
/// Opt-in Redis Lua parity evidence. Tests use unique GUID scopes and delete
/// only their own exact keys; they never FLUSHDB or enumerate unrelated data.
/// </summary>
public sealed class RedisUdpTransportIntegrationTests
{
    [RedisIntegrationFact]
    public async Task TicketLifecycle_IsVisibleAcrossInstancesAndBatchFenceIsExact()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var stores = redis.CreateTicketStores(maximumGenerationBatchSize: 2);
        try
        {
            await scope.ActivateMotion(redis.First);
            var grant = Grant(await stores.First.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(grant);

            var redeemed = await stores.Second.Redeem(
                Redemption(grant), new AcceptingRuntimeValidator(),
                CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redeemed.Status);
            CryptographicOperations.ZeroMemory(
                redeemed.DatagramAuthenticationKey!);
            Assert.True(await stores.Second
                .MarkPromotedTransportSessionConnected(
                    grant.Binding, CancellationToken.None));

            Assert.Equal(2, stores.First.MaximumGenerationBatchSize);
            var differentZone = grant.Binding with
            {
                ZoneKey = "different_zone"
            };
            var differentRuntime = grant.Binding with
            {
                RuntimeSessionId = Guid.NewGuid()
            };
            var current = await stores.First.AreTransportGenerationsCurrent(
                new[]
                {
                    grant.Binding,
                    differentZone,
                    grant.Binding,
                    differentRuntime,
                    grant.Binding
                },
                CancellationToken.None);
            Assert.Equal(
                new[] { true, false, true, false, true }, current);
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    [RedisIntegrationFact]
    public async Task ExpiredTicket_IsConsumedOnceAndReplayIsRejectedAcrossInstances()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var clock = new TestTimeProvider(
            new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));
        var key = RandomNumberGenerator.GetBytes(32);
        var protectorA = new AesGcmUdpTicketCredentialProtector(
            "redis-integration", key);
        var protectorB = new AesGcmUdpTicketCredentialProtector(
            "redis-integration", key);
        CryptographicOperations.ZeroMemory(key);
        var first = new RedisUdpTransportTicketStore(
            redis.First, protectorA, clock, TimeSpan.FromSeconds(5));
        var second = new RedisUdpTransportTicketStore(
            redis.Second, protectorB, clock, TimeSpan.FromSeconds(5));
        try
        {
            await scope.ActivateMotion(redis.First);
            var grant = Grant(await first.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(grant);
            clock.Advance(TimeSpan.FromSeconds(5));

            var expired = await first.Redeem(
                Redemption(grant), new AcceptingRuntimeValidator(),
                CancellationToken.None);
            var replay = await second.Redeem(
                Redemption(grant), new AcceptingRuntimeValidator(),
                CancellationToken.None);

            Assert.Equal(UdpTransportTicketRedeemStatus.Expired,
                expired.Status);
            Assert.Equal(UdpTransportTicketRedeemStatus.AlreadyRedeemed,
                replay.Status);
            Assert.Null(expired.DatagramAuthenticationKey);
            Assert.Null(replay.DatagramAuthenticationKey);
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    [RedisIntegrationFact]
    public async Task ConcurrentDuplicateRedeem_HasExactlyOneCanonicalWinner()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var stores = redis.CreateTicketStores();
        try
        {
            await scope.ActivateMotion(redis.First);
            var grant = Grant(await stores.First.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(grant);
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<UdpTransportTicketRedeemResult> Redeem(
                RedisUdpTransportTicketStore store)
            {
                await start.Task;
                return await store.Redeem(
                    Redemption(grant), new AcceptingRuntimeValidator(),
                    CancellationToken.None);
            }

            var first = Redeem(stores.First);
            var second = Redeem(stores.Second);
            start.SetResult();
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, result =>
                result.Status == UdpTransportTicketRedeemStatus.Redeemed);
            Assert.Single(results, result =>
                result.Status ==
                    UdpTransportTicketRedeemStatus.AlreadyRedeemed);
            foreach (var result in results)
            {
                if (result.DatagramAuthenticationKey is not null)
                    CryptographicOperations.ZeroMemory(
                        result.DatagramAuthenticationKey);
            }
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    [RedisIntegrationFact]
    public async Task WriterPromotionSubmitAndFallback_AreAtomicAcrossInstances()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var stores = redis.CreateTicketStores();
        var firstRegistry = new RedisWorldPlayerIntentRegistry(redis.First);
        var secondRegistry = new RedisWorldPlayerIntentRegistry(redis.Second);
        try
        {
            await scope.ActivateMotion(redis.First);
            var revisions = new RedisWorldRevisionRuntimeRegistry(redis.First);
            await revisions.ObserveCommitted(
                scope.WorldId, 1, CancellationToken.None);
            var initialized = await firstRegistry.InitializeHttpWriter(
                scope.HttpWriter,
                CancellationToken.None);
            Assert.True(initialized.Accepted);

            var grant = Grant(await stores.First.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(grant);
            var redeemed = await stores.Second.Redeem(
                Redemption(grant), new AcceptingRuntimeValidator(),
                CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redeemed.Status);
            CryptographicOperations.ZeroMemory(
                redeemed.DatagramAuthenticationKey!);
            Assert.True(await stores.First.MarkPromotedTransportSessionConnected(
                grant.Binding, CancellationToken.None));

            var promoted = await secondRegistry.PromoteUdpWriter(
                grant.Binding,
                initialized.Writer!.WriterEpoch,
                initialized.Writer.WorldRevision,
                CancellationToken.None);
            Assert.True(promoted.Accepted);
            var udpWriter = promoted.Writer!;
            Assert.True(await firstRegistry.Submit(
                scope.Intent(grant.Binding, udpWriter, sequence: 9),
                CancellationToken.None));
            Assert.Equal(9, (await secondRegistry.Get(
                scope.WorldId, scope.AvatarId,
                CancellationToken.None))!.Sequence);

            var fallback = await firstRegistry.FallbackToHttpWriter(
                grant.Binding, udpWriter.WriterEpoch,
                udpWriter.WorldRevision, CancellationToken.None);
            var retry = await secondRegistry.FallbackToHttpWriter(
                grant.Binding, udpWriter.WriterEpoch,
                udpWriter.WorldRevision, CancellationToken.None);
            Assert.True(fallback.Accepted);
            Assert.True(retry.Accepted);
            Assert.Equal(fallback.Writer, retry.Writer);
            Assert.False(await secondRegistry.Submit(
                scope.Intent(grant.Binding, udpWriter, sequence: 10),
                CancellationToken.None));
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    [RedisIntegrationFact]
    public async Task ConcurrentReissueAndPromotion_FencesTheOldGeneration()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var stores = redis.CreateTicketStores();
        var firstRegistry = new RedisWorldPlayerIntentRegistry(redis.First);
        var secondRegistry = new RedisWorldPlayerIntentRegistry(redis.Second);
        try
        {
            var initialized = await scope.PrepareHttpWriter(
                redis.First, firstRegistry);
            var firstGrant = Grant(await stores.First.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(firstGrant);
            var redeemed = await stores.Second.Redeem(
                Redemption(firstGrant), new AcceptingRuntimeValidator(),
                CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redeemed.Status);
            CryptographicOperations.ZeroMemory(
                redeemed.DatagramAuthenticationKey!);
            Assert.True(await stores.First
                .MarkPromotedTransportSessionConnected(
                    firstGrant.Binding, CancellationToken.None));

            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<PlayerMotionTransportTransitionResult> Promote()
            {
                await start.Task;
                return await secondRegistry.PromoteUdpWriter(
                    firstGrant.Binding,
                    initialized.Writer!.WriterEpoch,
                    initialized.Writer.WorldRevision,
                    CancellationToken.None);
            }
            async Task<UdpTransportTicketIssueResult> Reissue()
            {
                await start.Task;
                return await stores.First.Issue(
                    scope.IssueContext, CancellationToken.None);
            }

            var promotionTask = Promote();
            var reissueTask = Reissue();
            start.SetResult();
            await Task.WhenAll(promotionTask, reissueTask);
            var promotion = await promotionTask;
            var reissue = await reissueTask;
            Assert.NotEqual(promotion.Accepted,
                reissue.Status == UdpTransportTicketIssueStatus.Issued);
            if (promotion.Accepted)
            {
                Assert.Equal(
                    UdpTransportTicketIssueStatus
                        .ActiveUdpWriterRequiresFallback,
                    reissue.Status);
                var promotedWriter = promotion.Writer!;
                Assert.Equal(2, promotedWriter.WriterEpoch);
                Assert.True(await firstRegistry.Submit(
                    scope.Intent(firstGrant.Binding, promotedWriter, 1),
                    CancellationToken.None));
            }
            else
            {
                var secondGrant = Grant(reissue);
                scope.Track(secondGrant);
                Assert.Equal(
                    firstGrant.Binding.TransportGeneration + 1,
                    secondGrant.Binding.TransportGeneration);
                Assert.False(await stores.Second
                    .IsTransportGenerationCurrent(
                        firstGrant.Binding, CancellationToken.None));
                var writer = await firstRegistry.GetWriter(
                    scope.WorldId, scope.AvatarId, CancellationToken.None);
                Assert.NotNull(writer);
                Assert.Equal("http", writer.Mode);
                Assert.Equal(1, writer.WriterEpoch);
            }
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    [RedisIntegrationFact]
    public async Task ConcurrentSubmitAndFallback_HasOneFinalWriterEpochAndRejectsStaleUdp()
    {
        await using var redis = await RedisPair.Connect();
        var scope = RedisScope.Create();
        var stores = redis.CreateTicketStores();
        var firstRegistry = new RedisWorldPlayerIntentRegistry(redis.First);
        var secondRegistry = new RedisWorldPlayerIntentRegistry(redis.Second);
        try
        {
            var initialized = await scope.PrepareHttpWriter(
                redis.First, firstRegistry);
            var grant = Grant(await stores.First.Issue(
                scope.IssueContext, CancellationToken.None));
            scope.Track(grant);
            var redeemed = await stores.Second.Redeem(
                Redemption(grant), new AcceptingRuntimeValidator(),
                CancellationToken.None);
            Assert.Equal(UdpTransportTicketRedeemStatus.Redeemed,
                redeemed.Status);
            CryptographicOperations.ZeroMemory(
                redeemed.DatagramAuthenticationKey!);
            Assert.True(await stores.First
                .MarkPromotedTransportSessionConnected(
                    grant.Binding, CancellationToken.None));
            var promoted = await secondRegistry.PromoteUdpWriter(
                grant.Binding,
                initialized.Writer!.WriterEpoch,
                initialized.Writer.WorldRevision,
                CancellationToken.None);
            Assert.True(promoted.Accepted);
            var udpWriter = promoted.Writer!;

            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Submit()
            {
                await start.Task;
                return await firstRegistry.Submit(
                    scope.Intent(grant.Binding, udpWriter, 9),
                    CancellationToken.None);
            }
            async Task<PlayerMotionTransportTransitionResult> Fallback()
            {
                await start.Task;
                return await secondRegistry.FallbackToHttpWriter(
                    grant.Binding, udpWriter.WriterEpoch,
                    udpWriter.WorldRevision, CancellationToken.None);
            }

            var submitTask = Submit();
            var fallbackTask = Fallback();
            start.SetResult();
            await Task.WhenAll(submitTask, fallbackTask);

            var fallback = await fallbackTask;
            Assert.True(fallback.Accepted);
            Assert.NotNull(fallback.Writer);
            Assert.Equal("http", fallback.Writer.Mode);
            Assert.Equal(udpWriter.WriterEpoch + 1,
                fallback.Writer.WriterEpoch);
            Assert.Null(await firstRegistry.Get(
                scope.WorldId, scope.AvatarId, CancellationToken.None));
            Assert.False(await firstRegistry.Submit(
                scope.Intent(grant.Binding, udpWriter, 10),
                CancellationToken.None));
            var finalWriter = await firstRegistry.GetWriter(
                scope.WorldId, scope.AvatarId, CancellationToken.None);
            Assert.Equal(fallback.Writer.WriterEpoch,
                finalWriter!.WriterEpoch);
        }
        finally
        {
            await scope.Cleanup(redis.First.GetDatabase());
        }
    }

    private static UdpTransportTicketGrant Grant(
        UdpTransportTicketIssueResult result) =>
        Assert.IsType<UdpTransportTicketGrant>(result.Grant);

    private static UdpTransportTicketRedemption Redemption(
        UdpTransportTicketGrant grant)
    {
        var nonce = UdpTransportEncoding.Encode(
            RandomNumberGenerator.GetBytes(16));
        Assert.True(UdpTransportEncoding.TryDecode(
            grant.DatagramAuthenticationKey, 32, out var key));
        try
        {
            return new(
                grant.Ticket,
                nonce,
                UdpTransportTicketProof.Create(
                    key, grant.Ticket, nonce,
                    grant.Binding.AuthenticatedTransportSessionId,
                    grant.Binding.TransportGeneration),
                grant.Binding.AuthenticatedTransportSessionId,
                grant.Binding.TransportGeneration);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private sealed class RedisPair : IAsyncDisposable
    {
        private RedisPair(
            ConnectionMultiplexer first,
            ConnectionMultiplexer second)
        {
            First = first;
            Second = second;
        }

        internal ConnectionMultiplexer First { get; }
        internal ConnectionMultiplexer Second { get; }

        internal static async Task<RedisPair> Connect()
        {
            var connection = Environment.GetEnvironmentVariable(
                RedisIntegrationFactAttribute.EnvironmentVariable)!;
            var options = ConfigurationOptions.Parse(connection);
            if (options.DefaultDatabase is null or <= 0)
            {
                throw new InvalidOperationException(
                    "Redis integration tests require an explicit isolated " +
                    "non-default database (defaultDatabase=1 or greater).");
            }
            var first = await ConnectionMultiplexer.ConnectAsync(options);
            try
            {
                var second = await ConnectionMultiplexer.ConnectAsync(options);
                return new(first, second);
            }
            catch
            {
                first.Dispose();
                throw;
            }
        }

        internal (
            RedisUdpTransportTicketStore First,
            RedisUdpTransportTicketStore Second) CreateTicketStores(
                int maximumGenerationBatchSize = 64)
        {
            var key = RandomNumberGenerator.GetBytes(32);
            try
            {
                return (
                    new RedisUdpTransportTicketStore(
                        First,
                        new AesGcmUdpTicketCredentialProtector(
                            "redis-integration", key),
                        maximumGenerationBatchSize:
                            maximumGenerationBatchSize),
                    new RedisUdpTransportTicketStore(
                        Second,
                        new AesGcmUdpTicketCredentialProtector(
                            "redis-integration", key),
                        maximumGenerationBatchSize:
                            maximumGenerationBatchSize));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await First.CloseAsync();
            await Second.CloseAsync();
            First.Dispose();
            Second.Dispose();
        }
    }

    private sealed class RedisScope
    {
        private readonly List<string> trackedKeys = new();

        internal Guid WorldId { get; } = Guid.NewGuid();
        internal Guid UserId { get; } = Guid.NewGuid();
        internal Guid AvatarId { get; } = Guid.NewGuid();
        internal Guid RuntimeSessionId { get; } = Guid.NewGuid();
        internal string ZoneKey { get; } = "redis_integration_zone";

        internal static RedisScope Create() => new();

        internal UdpTransportTicketIssueContext IssueContext => new(
            WorldId, UserId, AvatarId, ZoneKey, RuntimeSessionId,
            RealtimeWireContract.ProtocolVersion);

        internal PlayerMotionTransportWriterState HttpWriter => new(
            WorldId, UserId, AvatarId, ZoneKey, RuntimeSessionId,
            "http", 1, 1);

        internal WorldPlayerIntent Intent(
            UdpTransportTicketBinding binding,
            PlayerMotionTransportWriterState writer,
            long sequence) => new(
                WorldId, UserId, AvatarId, ZoneKey,
                sequence, 0f, 1f, 4f,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                RuntimeSessionId,
                Transport: "udp",
                AuthenticatedTransportSessionId:
                    binding.AuthenticatedTransportSessionId,
                TransportGeneration: binding.TransportGeneration,
                WorldRevision: writer.WorldRevision,
                WriterEpoch: writer.WriterEpoch);

        internal async Task ActivateMotion(ConnectionMultiplexer redis)
        {
            var registry = new RedisWorldPlayerMotionRuntimeRegistry(redis);
            await registry.Activate(new WorldPlayerMotionState(
                WorldId, AvatarId, ZoneKey,
                0d, 0d, 0d, 0, "idle", 1,
                RuntimeSessionId: RuntimeSessionId),
                CancellationToken.None);
        }

        internal async Task<PlayerMotionTransportTransitionResult>
            PrepareHttpWriter(
                ConnectionMultiplexer redis,
                RedisWorldPlayerIntentRegistry registry)
        {
            await ActivateMotion(redis);
            var revisions = new RedisWorldRevisionRuntimeRegistry(redis);
            await revisions.ObserveCommitted(
                WorldId, 1, CancellationToken.None);
            var initialized = await registry.InitializeHttpWriter(
                HttpWriter, CancellationToken.None);
            Assert.True(initialized.Accepted);
            return initialized;
        }

        internal void Track(UdpTransportTicketGrant grant)
        {
            Assert.True(UdpTransportEncoding.TryDecode(
                grant.Ticket, 32, out var bytes));
            var fingerprint = UdpTransportEncoding.Fingerprint(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            trackedKeys.Add("tormia:udp-ticket:value:" + fingerprint);
            trackedKeys.Add("tormia:udp-ticket:consumed:" + fingerprint);
            trackedKeys.Add("tormia:udp-transport:active:" +
                grant.Binding.AuthenticatedTransportSessionId.ToString("N"));
            trackedKeys.Add("tormia:udp-transport:connected:" +
                grant.Binding.AuthenticatedTransportSessionId.ToString("N"));
        }

        internal async Task Cleanup(IDatabase database)
        {
            var scope = $"tormia:udp-ticket:scope:{WorldId:N}:{UserId:N}:" +
                $"{AvatarId:N}:{RuntimeSessionId:N}";
            var keys = new List<RedisKey>(trackedKeys.Select(value =>
                (RedisKey)value))
            {
                scope + ":active",
                scope + ":generation",
                scope + ":promoted",
                $"tormia:world:{WorldId:N}:avatar:{AvatarId:N}:motion",
                $"tormia:world:{WorldId:N}:avatar:{AvatarId:N}:motion:inactive-session",
                $"tormia:world:{WorldId:N}:avatar:{AvatarId:N}:intent",
                $"tormia:world:{WorldId:N}:avatar:{AvatarId:N}:intent-writer",
                $"tormia:world:{WorldId:N}:revision",
                $"tormia:world:{WorldId:N}:revision:pending"
            };
            await database.KeyDeleteAsync(keys.ToArray());
        }
    }

    private sealed class AcceptingRuntimeValidator :
        IUdpTransportRuntimeSessionValidator
    {
        public ValueTask<bool> IsCurrent(
            UdpTransportTicketBinding binding,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now;
        internal TestTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}

public sealed class RedisIntegrationFactAttribute : FactAttribute
{
    internal const string EnvironmentVariable =
        "TORMIA_TEST_REDIS_CONNECTION";

    public RedisIntegrationFactAttribute()
    {
        var connection = Environment.GetEnvironmentVariable(
            EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            Skip = $"Set {EnvironmentVariable} to an isolated test Redis " +
                "connection to run this integration test.";
            return;
        }
        try
        {
            var options = ConfigurationOptions.Parse(connection);
            if (options.DefaultDatabase is null or <= 0)
            {
                Skip = $"{EnvironmentVariable} must declare an isolated " +
                    "non-default Redis database, for example " +
                    "defaultDatabase=15.";
            }
        }
        catch (Exception exception)
        {
            Skip = $"{EnvironmentVariable} is invalid: " + exception.Message;
        }
    }
}
