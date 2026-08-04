using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tormia.Ontology.Core;
using Xunit;

public sealed class AuthorityCompiledContractCacheTests
{
    [Fact]
    public async Task MetricsBalanceResidentBytesAndOverflowPendingKeys()
    {
        var measurements = new ConcurrentBag<(string Name, long Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (string.Equals(instrument.Meter.Name,
                        AuthorityCompiledContractCacheMetrics.MeterName,
                        StringComparison.Ordinal))
                    active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.Start();

        var residentRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1,
            maxEstimatedBytes: 1_024,
            maxConcurrentBuilds: 2,
            maxPendingBuildKeys: 1);
        var world = Guid.NewGuid();
        var resident = cache.GetOrBuild(
            Key(world, 1, "resident-in-flight"),
            async () =>
            {
                await residentRelease.Task;
                return AuthorityCompiledEvaluationContract.Compile([]);
            }, CancellationToken.None);
        var oversized = AuthorityCompiledEvaluationContract.Compile(
            Enumerable.Range(0, 32)
                .Select(index => new AuthorityFactSnapshot(
                    Guid.NewGuid(), "large_predicate", "canonical",
                    new string('x', 128)))
                .ToArray());
        await cache.GetOrBuild(
            Key(world, 2, "oversized-overflow"),
            () => Task.FromResult(oversized), CancellationToken.None);
        residentRelease.SetResult();
        await resident;

        Assert.Equal(0, Sum(AuthorityCompiledContractCacheMetrics
            .OverflowPendingBuildKeysName));
        Assert.Equal(1, Sum(AuthorityCompiledContractCacheMetrics
            .ResidentEntriesName));
        Assert.Equal(cache.ResidentEstimatedBytes,
            Sum(AuthorityCompiledContractCacheMetrics.EstimatedBytesName));

        long Sum(string name) => measurements
            .Where(sample => string.Equals(sample.Name, name,
                StringComparison.Ordinal))
            .Sum(sample => sample.Value);
    }

    [Fact]
    public async Task MetricsRecordOversizedResidentBypassAndReturnToZero()
    {
        var measurements = new ConcurrentBag<(string Name, long Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (string.Equals(instrument.Meter.Name,
                        AuthorityCompiledContractCacheMetrics.MeterName,
                        StringComparison.Ordinal))
                    active.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.Start();

        using var cache = new AuthorityCompiledContractCache(
            capacity: 2, maxEstimatedBytes: 1_024);
        var oversized = AuthorityCompiledEvaluationContract.Compile(
            Enumerable.Range(0, 32)
                .Select(index => new AuthorityFactSnapshot(
                    Guid.NewGuid(), "large_predicate", "canonical",
                    new string('x', 128)))
                .ToArray());
        await cache.GetOrBuild(
            Key(Guid.NewGuid(), 1, "oversized-metric"),
            () => Task.FromResult(oversized), CancellationToken.None);

        Assert.Equal(1, Sum(AuthorityCompiledContractCacheMetrics
            .OversizedBypassesName));
        Assert.Equal(0, Sum(AuthorityCompiledContractCacheMetrics
            .ResidentEntriesName));
        Assert.Equal(0, Sum(AuthorityCompiledContractCacheMetrics
            .EstimatedBytesName));
        Assert.Equal(0, Sum(AuthorityCompiledContractCacheMetrics
            .OverflowPendingBuildKeysName));

        long Sum(string name) => measurements
            .Where(sample => string.Equals(sample.Name, name,
                StringComparison.Ordinal))
            .Sum(sample => sample.Value);
    }

    [Fact]
    public async Task OversizedContractIsReturnedButNeverMadeResident()
    {
        var facts = Enumerable.Range(0, 32)
            .Select(index => new AuthorityFactSnapshot(
                Guid.NewGuid(), "large_predicate", "canonical",
                new string('x', 128)))
            .ToArray();
        var contract = AuthorityCompiledEvaluationContract.Compile(facts);
        Assert.True(contract.EstimatedBytes > 1_024);
        using var cache = new AuthorityCompiledContractCache(
            capacity: 4, maxEstimatedBytes: 1_024);
        var key = Key(Guid.NewGuid(), 1, "oversized");
        var builds = 0;

        Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            return Task.FromResult(contract);
        }

        var first = await cache.GetOrBuild(
            key, Factory, CancellationToken.None);
        var second = await cache.GetOrBuild(
            key, Factory, CancellationToken.None);

        Assert.Same(contract, first);
        Assert.Same(contract, second);
        Assert.Equal(2, builds);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ResidentEstimatedBytes);
    }

    [Fact]
    public async Task OversizedContractDoesNotChangeExistingResidentAccounting()
    {
        var resident = AuthorityCompiledEvaluationContract.Compile([]);
        var limit = resident.EstimatedBytes + 1_024;
        using var cache = new AuthorityCompiledContractCache(
            capacity: 2, maxEstimatedBytes: limit);
        var world = Guid.NewGuid();
        await cache.GetOrBuild(
            Key(world, 1, "resident"),
            () => Task.FromResult(resident), CancellationToken.None);
        var before = cache.ResidentEstimatedBytes;
        var oversized = AuthorityCompiledEvaluationContract.Compile(
            Enumerable.Range(0, 64)
                .Select(index => new AuthorityFactSnapshot(
                    Guid.NewGuid(), "large_predicate", "canonical",
                    new string('x', 128)))
                .ToArray());

        await cache.GetOrBuild(
            Key(world, 2, "oversized"),
            () => Task.FromResult(oversized), CancellationToken.None);

        Assert.Equal(1, cache.Count);
        Assert.Equal(before, cache.ResidentEstimatedBytes);
    }

    [Fact]
    public async Task SameExactKeyUsesOneSingleFlightBuild()
    {
        using var cache = new AuthorityCompiledContractCache(4);
        var key = Key(Guid.NewGuid(), 7, "manifest-a");
        var builds = 0;
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            return BuildAfterRelease();
        }

        async Task<AuthorityCompiledEvaluationContract> BuildAfterRelease()
        {
            await release.Task;
            return AuthorityCompiledEvaluationContract.Compile([]);
        }

        var requests = Enumerable.Range(0, 20)
            .Select(_ => cache.GetOrBuild(key, Factory, CancellationToken.None))
            .ToArray();
        release.SetResult();
        var contracts = await Task.WhenAll(requests);

        Assert.Equal(1, Volatile.Read(ref builds));
        Assert.All(contracts, contract => Assert.Same(contracts[0], contract));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCancelOrUnaccountSharedBuild()
    {
        using var cache = new AuthorityCompiledContractCache(1);
        var key = Key(Guid.NewGuid(), 7, "manifest-a");
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var builds = 0;
        async Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            await release.Task;
            return AuthorityCompiledEvaluationContract.Compile([]);
        }

        using var cancelled = new CancellationTokenSource();
        var cancelledWaiter = cache.GetOrBuild(
            key, Factory, cancelled.Token);
        var survivingWaiter = cache.GetOrBuild(
            key, Factory, CancellationToken.None);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledWaiter);
        release.SetResult();
        var completed = await survivingWaiter;
        var laterHit = await cache.GetOrBuild(
            key, Factory, CancellationToken.None);

        Assert.Same(completed, laterHit);
        Assert.Equal(1, builds);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task SharedBuildTimeoutRemovesResidentAndReleasesPermit()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1,
            maxConcurrentBuilds: 1,
            buildTimeout: TimeSpan.FromMilliseconds(50));
        var key = Key(Guid.NewGuid(), 7, "timeout");

        await Assert.ThrowsAsync<AuthorityCompiledContractBuildTimeoutException>(
            () => cache.GetOrBuild(
                key,
                async buildToken =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, buildToken);
                    return AuthorityCompiledEvaluationContract.Compile([]);
                },
                CancellationToken.None));

        Assert.Equal(0, cache.Count);
        var recovered = await cache.GetOrBuild(
            key,
            _ => Task.FromResult(
                AuthorityCompiledEvaluationContract.Compile([])),
            CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task HostStoppingCancelsSharedOwnerWithoutBecomingActionTimeout()
    {
        using var stopping = new CancellationTokenSource();
        using var cache = new AuthorityCompiledContractCache(
            buildTimeout: TimeSpan.FromSeconds(10),
            hostStoppingToken: stopping.Token);
        var key = Key(Guid.NewGuid(), 7, "shutdown");
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var build = cache.GetOrBuild(
            key,
            async buildToken =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, buildToken);
                return AuthorityCompiledEvaluationContract.Compile([]);
            },
            CancellationToken.None);
        await started.Task;
        stopping.Cancel();

        var exception = await Assert.ThrowsAsync<
            AuthorityCompiledContractHostStoppingException>(() => build);
        Assert.True(exception.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.PendingBuildCount);
    }

    [Fact]
    public async Task DisposeDuringBuildDoesNotBreakBalancedPermitRelease()
    {
        var cache = new AuthorityCompiledContractCache(
            buildTimeout: TimeSpan.FromSeconds(5));
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var build = cache.GetOrBuild(
            Key(Guid.NewGuid(), 1, "dispose"),
            async _ =>
            {
                started.SetResult();
                await release.Task;
                return AuthorityCompiledEvaluationContract.Compile([]);
            },
            CancellationToken.None);
        await started.Task;

        cache.Dispose();
        release.SetResult();

        var result = await build;
        Assert.NotNull(result);
    }

    [Fact]
    public void CompileAndMemoryEstimateHonorOwnerCancellation()
    {
        var facts = Enumerable.Range(0, 100_000)
            .Select(index => new AuthorityFactSnapshot(
                Guid.NewGuid(), "value", "number", index.ToString()))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AuthorityCompiledEvaluationContract.Compile(
                facts, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() =>
            AuthorityCompiledContractMemoryEstimator.Estimate(
                facts, cancellation.Token));
    }

    [Fact]
    public async Task OverflowTimeoutRemovesPendingIdentityAndReleasesPermit()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1,
            maxConcurrentBuilds: 1,
            maxPendingBuildKeys: 1,
            buildTimeout: TimeSpan.FromMilliseconds(75));
        var world = Guid.NewGuid();
        var residentStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resident = cache.GetOrBuild(
            Key(world, 1, "resident"),
            async buildToken =>
            {
                residentStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, buildToken);
                return AuthorityCompiledEvaluationContract.Compile([]);
            },
            CancellationToken.None);
        await residentStarted.Task;
        var overflow = cache.GetOrBuild(
            Key(world, 2, "overflow"),
            async buildToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, buildToken);
                return AuthorityCompiledEvaluationContract.Compile([]);
            },
            CancellationToken.None);

        await Assert.ThrowsAsync<AuthorityCompiledContractBuildTimeoutException>(
            () => resident);
        await Assert.ThrowsAsync<AuthorityCompiledContractBuildTimeoutException>(
            () => overflow);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.PendingBuildCount);
    }

    [Fact]
    public async Task DifferentKeyBuildsNeverExceedProcessConcurrencyLimit()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1,
            maxConcurrentBuilds: 2,
            maxPendingBuildKeys: 16);
        var current = 0;
        var maximum = 0;
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AuthorityCompiledEvaluationContract> Factory()
        {
            var active = Interlocked.Increment(ref current);
            int observed;
            do
            {
                observed = Volatile.Read(ref maximum);
            }
            while (active > observed &&
                   Interlocked.CompareExchange(
                       ref maximum, active, observed) != observed);
            try
            {
                await release.Task;
                return AuthorityCompiledEvaluationContract.Compile([]);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }

        var world = Guid.NewGuid();
        var requests = Enumerable.Range(1, 12)
            .Select(revision => cache.GetOrBuild(
                Key(world, revision, $"manifest-{revision}"),
                Factory,
                CancellationToken.None))
            .ToArray();
        var deadline = Stopwatch.StartNew();
        while (Volatile.Read(ref maximum) < 2 &&
               deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10);
        release.SetResult();
        await Task.WhenAll(requests);

        Assert.Equal(2, maximum);
        Assert.Equal(0, current);
    }

    [Fact]
    public async Task SameOverflowKeyRetainsSingleFlightIdentity()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1, maxConcurrentBuilds: 2);
        var residentRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var overflowRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var world = Guid.NewGuid();
        var resident = cache.GetOrBuild(
            Key(world, 1, "resident"),
            async () =>
            {
                await residentRelease.Task;
                return AuthorityCompiledEvaluationContract.Compile([]);
            }, CancellationToken.None);
        var overflowBuilds = 0;
        async Task<AuthorityCompiledEvaluationContract> OverflowFactory()
        {
            Interlocked.Increment(ref overflowBuilds);
            await overflowRelease.Task;
            return AuthorityCompiledEvaluationContract.Compile([]);
        }
        var overflowKey = Key(world, 2, "overflow");
        var first = cache.GetOrBuild(
            overflowKey, OverflowFactory, CancellationToken.None);
        var second = cache.GetOrBuild(
            overflowKey, OverflowFactory, CancellationToken.None);
        overflowRelease.SetResult();
        var results = await Task.WhenAll(first, second);
        residentRelease.SetResult();
        await resident;

        Assert.Equal(1, overflowBuilds);
        Assert.Same(results[0], results[1]);
    }

    [Fact]
    public async Task CancelledOverflowWaiterDoesNotAffectSharedBuild()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1, maxConcurrentBuilds: 2);
        var residentRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var overflowRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var world = Guid.NewGuid();
        var resident = cache.GetOrBuild(
            Key(world, 1, "resident"),
            async () =>
            {
                await residentRelease.Task;
                return AuthorityCompiledEvaluationContract.Compile([]);
            }, CancellationToken.None);
        var builds = 0;
        async Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            await overflowRelease.Task;
            return AuthorityCompiledEvaluationContract.Compile([]);
        }
        using var cancellation = new CancellationTokenSource();
        var key = Key(world, 2, "overflow");
        var cancelled = cache.GetOrBuild(key, Factory, cancellation.Token);
        var surviving = cache.GetOrBuild(key, Factory, CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        overflowRelease.SetResult();
        await surviving;
        residentRelease.SetResult();
        await resident;

        Assert.Equal(1, builds);
    }

    [Fact]
    public async Task DistinctOverflowPendingKeysAreBounded()
    {
        using var cache = new AuthorityCompiledContractCache(
            capacity: 1,
            maxConcurrentBuilds: 1,
            maxPendingBuildKeys: 2);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AuthorityCompiledEvaluationContract> Factory() => Build();
        async Task<AuthorityCompiledEvaluationContract> Build()
        {
            await release.Task;
            return AuthorityCompiledEvaluationContract.Compile([]);
        }
        var world = Guid.NewGuid();
        var resident = cache.GetOrBuild(
            Key(world, 1, "resident"), Factory, CancellationToken.None);
        var pendingOne = cache.GetOrBuild(
            Key(world, 2, "pending-1"), Factory, CancellationToken.None);
        var pendingTwo = cache.GetOrBuild(
            Key(world, 3, "pending-2"), Factory, CancellationToken.None);

        await Assert.ThrowsAsync<AuthorityCompiledContractBackpressureException>(
            () => cache.GetOrBuild(
                Key(world, 4, "rejected"),
                Factory,
                CancellationToken.None));
        release.SetResult();
        await Task.WhenAll(resident, pendingOne, pendingTwo);
    }

    [Fact]
    public async Task RevisionAndManifestAreExactKeyMissBoundaries()
    {
        using var cache = new AuthorityCompiledContractCache(8);
        var world = Guid.NewGuid();
        var builds = 0;
        Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            return Task.FromResult(AuthorityCompiledEvaluationContract.Compile([]));
        }

        var revisionOne = await cache.GetOrBuild(
            Key(world, 1, "manifest-a"), Factory, CancellationToken.None);
        var revisionTwo = await cache.GetOrBuild(
            Key(world, 2, "manifest-a"), Factory, CancellationToken.None);
        var newManifest = await cache.GetOrBuild(
            Key(world, 2, "manifest-b"), Factory, CancellationToken.None);

        Assert.Equal(3, builds);
        Assert.NotSame(revisionOne, revisionTwo);
        Assert.NotSame(revisionTwo, newManifest);
    }

    [Fact]
    public async Task RuleRemovalAtNewRevisionFailsClosed()
    {
        using var cache = new AuthorityCompiledContractCache(4);
        var world = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var enabledFacts = new[]
        {
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "CachedRule"),
            new AuthorityFactSnapshot(
                actor, "has_concept", "canonical", "Actor")
        };
        var removedFacts = enabledFacts
            .Where(fact => fact.PredicateId != "has_rule_block")
            .ToArray();
        var enabled = await cache.GetOrBuild(
            Key(world, 10, "manifest-a"),
            () => Task.FromResult(
                AuthorityCompiledEvaluationContract.Compile(enabledFacts)),
            CancellationToken.None);
        var removed = await cache.GetOrBuild(
            Key(world, 11, "manifest-a"),
            () => Task.FromResult(
                AuthorityCompiledEvaluationContract.Compile(removedFacts)),
            CancellationToken.None);

        var accepted = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            Transport(), Rule(), actor, actor, null,
            enabled.CreateCommandSnapshot());
        var rejected = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            Transport(), Rule(), actor, actor, null,
            removed.CreateCommandSnapshot());

        Assert.True(accepted.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal("action_conditions_not_met", rejected.RejectionCode);
    }

    [Fact]
    public async Task CommandMutationOverlayNeverLeaksIntoCachedBaseOrSibling()
    {
        using var cache = new AuthorityCompiledContractCache();
        var actor = Guid.NewGuid();
        var key = Key(Guid.NewGuid(), 4, "manifest-a");
        var contract = await cache.GetOrBuild(
            key,
            () => Task.FromResult(AuthorityCompiledEvaluationContract.Compile(
            [
                new AuthorityFactSnapshot(
                    actor, "health", "number", "10")
            ])),
            CancellationToken.None);
        var first = contract.CreateCommandSnapshot();
        var sibling = contract.CreateCommandSnapshot();

        Assert.True(first.TryApplyCommittedMutation(
            AuthorityMutation.AdjustNumber(
                actor, "health", -4, 0, 10,
                OntologyRuleResultLifetime.DurableState),
            null,
            out var rejection), rejection);

        Assert.Equal(["6"], first.GetValues(actor, "health"));
        Assert.Equal(["10"], sibling.GetValues(actor, "health"));
        Assert.Equal(
            ["10"],
            contract.CreateCommandSnapshot().GetValues(actor, "health"));
    }

    [Fact]
    public async Task CapacityEvictsLeastRecentlyUsedEntry()
    {
        using var cache = new AuthorityCompiledContractCache(2);
        var world = Guid.NewGuid();
        var key1 = Key(world, 1, "a");
        var key2 = Key(world, 2, "a");
        var key3 = Key(world, 3, "a");
        var builds = new ConcurrentDictionary<AuthorityCompiledContractCacheKey, int>();

        Task<AuthorityCompiledEvaluationContract> Get(
            AuthorityCompiledContractCacheKey key) => cache.GetOrBuild(
                key,
                () =>
                {
                    builds.AddOrUpdate(key, 1, (_, current) => current + 1);
                    return Task.FromResult(
                        AuthorityCompiledEvaluationContract.Compile([]));
                },
                CancellationToken.None);

        await Get(key1);
        await Get(key2);
        await Get(key1); // key2 is now least recently used.
        await Get(key3);
        await Get(key2);

        Assert.Equal(2, cache.Count);
        Assert.Equal(1, builds[key1]);
        Assert.Equal(2, builds[key2]);
        Assert.Equal(1, builds[key3]);
    }

    [Fact]
    [Trait("Category", "ExplicitPerformanceEvidence")]
    public async Task HundredThousandFactExactRevisionHitAvoidsRecompile()
    {
        using var cache = new AuthorityCompiledContractCache();
        var key = Key(Guid.NewGuid(), 100, "manifest-100k");
        var builds = 0;
        var facts = Enumerable.Range(0, 100_000)
            .Select(index => new AuthorityFactSnapshot(
                Guid.Parse($"00000000-0000-0000-0001-{index + 1:D12}"),
                $"fact_{index % 100:D3}",
                "canonical",
                $"value_{index:D6}"))
            .Append(new AuthorityFactSnapshot(
                Guid.Empty, "health", "number", "10"))
            .ToArray();
        Task<AuthorityCompiledEvaluationContract> Factory()
        {
            Interlocked.Increment(ref builds);
            return Task.FromResult(
                AuthorityCompiledEvaluationContract.Compile(facts));
        }

        var compileWatch = Stopwatch.StartNew();
        var first = await cache.GetOrBuild(key, Factory, CancellationToken.None);
        compileWatch.Stop();
        var hitWatch = Stopwatch.StartNew();
        var hit = await cache.GetOrBuild(key, Factory, CancellationToken.None);
        var hitSnapshot = hit.CreateCommandSnapshot();
        Assert.Equal(["10"], hitSnapshot.GetValues(Guid.Empty, "health"));
        hitWatch.Stop();
        var mutationWatch = Stopwatch.StartNew();
        Assert.True(hitSnapshot.TryApplyCommittedMutation(
            AuthorityMutation.AdjustNumber(
                Guid.Empty, "health", -1, 0, 10,
                OntologyRuleResultLifetime.DurableState),
            null,
            out var mutationRejection), mutationRejection);
        mutationWatch.Stop();

        Console.WriteLine(
            $"100k cold_compile_ms={compileWatch.Elapsed.TotalMilliseconds:F3} " +
            $"cache_hit_read_ms={hitWatch.Elapsed.TotalMilliseconds:F3} " +
            $"first_committed_mutation_delta_ms={mutationWatch.Elapsed.TotalMilliseconds:F3}");

        Assert.Same(first, hit);
        Assert.Equal(1, builds);
        Assert.True(
            hitWatch.Elapsed < compileWatch.Elapsed,
            $"Expected cache hit ({hitWatch.Elapsed.TotalMilliseconds:F3} ms) " +
            $"below compile ({compileWatch.Elapsed.TotalMilliseconds:F3} ms).");
        Assert.True(
            mutationWatch.Elapsed < compileWatch.Elapsed,
            $"First mutation must remain key-local ({mutationWatch.Elapsed.TotalMilliseconds:F3} ms) " +
            $"instead of cloning the 100k base ({compileWatch.Elapsed.TotalMilliseconds:F3} ms).");
    }

    private static AuthorityCompiledContractCacheKey Key(
        Guid world, long revision, string manifest) =>
        new(world, revision, manifest);

    private static OntologyActionEffectDefinition Transport() => new()
    {
        actionVerb = "cached_action",
        objectPattern = "?actor",
        evaluationOnly = true,
        ruleInvocation = new OntologyActionRuleInvocationDefinition
        {
            ruleId = "CachedRule",
            bindingVariable = "?actor",
            bindingEntityPattern = "?actor",
            intentSubjectPattern = "?actor",
            intentPredicate = "cached_intent",
            intentObjectPattern = "?actor"
        }
    };

    private static OntologyRuleDefinition Rule() => new()
    {
        id = "CachedRule",
        catalogVersion = 1,
        conditions =
        [
            OntologyCondition.Fact("?actor", "cached_intent", "?actor"),
            OntologyCondition.Fact(
                "?actor", "has_rule_block", "CachedRule"),
            OntologyCondition.HasConcept("?actor", "Actor")
        ],
        effects =
        [
            OntologyEffect.SetFact("?actor", "cached_result", "Accepted")
        ]
    };
}
