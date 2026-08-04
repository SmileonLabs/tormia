using Xunit;

public sealed class WorldRevisionRuntimeRegistryTests
{
    [Fact]
    public async Task PendingReconciliationIsSingleFlightPerWorld()
    {
        var coordinator = new PendingRevisionReconciliationCoordinator(
            TimeProvider.System);
        var worldId = Guid.NewGuid();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<long?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<long?> Reconcile()
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return await release.Task;
        }

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => coordinator.Run(
                worldId, Reconcile, CancellationToken.None))
            .ToArray();
        await started.Task;
        release.SetResult(7);
        var results = await Task.WhenAll(tasks);

        Assert.All(results, value => Assert.Equal(7, value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PendingReconciliationWaitersAreBoundedAndOverflowFailsClosed()
    {
        var coordinator = new PendingRevisionReconciliationCoordinator(
            TimeProvider.System,
            maximumConcurrentWorlds: 1,
            maximumWaitersPerWorld: 2);
        var worldId = Guid.NewGuid();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<long?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<long?> Reconcile()
        {
            started.TrySetResult();
            return await release.Task;
        }

        var first = coordinator.Run(
            worldId, Reconcile, CancellationToken.None);
        await started.Task;
        var second = coordinator.Run(
            worldId, Reconcile, CancellationToken.None);
        await Task.Yield();
        var overflow = await coordinator.Run(
            worldId, Reconcile, CancellationToken.None);

        Assert.Null(overflow);
        release.SetResult(9);
        Assert.Equal(9, await first);
        Assert.Equal(9, await second);
    }

    [Fact]
    public async Task FailedReconciliationUsesShortFailClosedBackoff()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var coordinator = new PendingRevisionReconciliationCoordinator(
            clock,
            failureBackoff: TimeSpan.FromSeconds(1));
        var worldId = Guid.NewGuid();
        var calls = 0;

        Assert.Null(await coordinator.Run(
            worldId,
            () => { calls++; return Task.FromResult<long?>(null); },
            CancellationToken.None));
        Assert.Null(await coordinator.Run(
            worldId,
            () => { calls++; return Task.FromResult<long?>(3); },
            CancellationToken.None));
        Assert.Equal(1, calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(3, await coordinator.Run(
            worldId,
            () => { calls++; return Task.FromResult<long?>(3); },
            CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidateRemovesBackoffResultAndFencesInFlightCaching()
    {
        var coordinator = new PendingRevisionReconciliationCoordinator(
            TimeProvider.System);
        var worldId = Guid.NewGuid();

        Assert.Null(await coordinator.Run(
            worldId,
            () => Task.FromResult<long?>(null),
            CancellationToken.None));
        Assert.Equal(1, coordinator.RecentResultCount);

        coordinator.Invalidate(worldId);

        Assert.Equal(0, coordinator.RecentResultCount);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<long?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = coordinator.Run(
            worldId,
            async () =>
            {
                started.SetResult();
                return await release.Task;
            },
            CancellationToken.None);
        await started.Task;
        coordinator.Invalidate(worldId);
        release.SetResult(null);
        Assert.Null(await inFlight);
        Assert.Equal(0, coordinator.RecentResultCount);
    }

    [Fact]
    public async Task BackoffResultsRemainCapacityBoundedAcrossManyWorlds()
    {
        var coordinator = new PendingRevisionReconciliationCoordinator(
            TimeProvider.System,
            maximumRecentResults: 4,
            failureBackoff: TimeSpan.FromMinutes(1));

        for (var index = 0; index < 20; index++)
            Assert.Null(await coordinator.Run(
                Guid.NewGuid(),
                () => Task.FromResult<long?>(null),
                CancellationToken.None));

        Assert.InRange(coordinator.RecentResultCount, 0, 4);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        internal void Advance(TimeSpan value) => current += value;
    }
}
