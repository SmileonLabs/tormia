using Xunit;

public sealed class WorldZoneRuntimePublicationFenceTests
{
    [Fact]
    public void OlderRevisionCannotReplaceNewerRuntimeSnapshot()
    {
        var worldId = Guid.NewGuid();
        var current = Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 200L);
        var stale = Snapshot(worldId, revision: 11L, completedAt: 300L, observedAt: 300L);

        Assert.False(WorldZoneRuntimePublicationPolicy.CanReplace(current, stale));
    }

    [Fact]
    public void SameRevisionRequiresMonotonicCompletionThenObservationTime()
    {
        var worldId = Guid.NewGuid();
        var current = Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 210L);

        Assert.False(WorldZoneRuntimePublicationPolicy.CanReplace(
            current,
            Snapshot(worldId, revision: 12L, completedAt: 199L, observedAt: 300L)));
        Assert.False(WorldZoneRuntimePublicationPolicy.CanReplace(
            current,
            Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 210L)));
        Assert.True(WorldZoneRuntimePublicationPolicy.CanReplace(
            current,
            Snapshot(worldId, revision: 12L, completedAt: 201L, observedAt: 201L)));
        Assert.True(WorldZoneRuntimePublicationPolicy.CanReplace(
            current,
            Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 211L)));
    }

    [Fact]
    public async Task InMemoryRegistryAtomicallyRejectsStalePublication()
    {
        var worldId = Guid.NewGuid();
        var runtime = new InMemoryWorldZoneRuntimeRegistry();
        var newer = Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 200L)
            with { InferredFacts = new[] { new HeadlessInferredFact("actor", "state", "new") } };
        var stale = Snapshot(worldId, revision: 11L, completedAt: 300L, observedAt: 300L)
            with { InferredFacts = new[] { new HeadlessInferredFact("actor", "state", "old") } };

        Assert.True(await runtime.TryPublish(newer, CancellationToken.None));
        Assert.False(await runtime.TryPublish(stale, CancellationToken.None));

        var stored = await runtime.Get(worldId, "main", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(12L, stored.EvaluatedWorldRevision);
        Assert.Equal("new", Assert.Single(stored.InferredFacts).Object);
    }

    [Fact]
    public async Task DeferredSnapshotFromOlderReadCannotOverwriteLaterEvaluation()
    {
        var worldId = Guid.NewGuid();
        var runtime = new InMemoryWorldZoneRuntimeRegistry();
        var older = Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 200L);
        var newer = Snapshot(worldId, revision: 12L, completedAt: 250L, observedAt: 250L)
            with { ScheduleStatus = "evaluated" };
        var staleDeferred = older with
        {
            ScheduleStatus = "execution_lease_not_acquired",
            ObservedAtUnixMilliseconds = 300L
        };

        Assert.True(await runtime.TryPublish(older, CancellationToken.None));
        Assert.True(await runtime.TryPublish(newer, CancellationToken.None));
        Assert.False(await runtime.TryPublish(staleDeferred, CancellationToken.None));

        var stored = await runtime.Get(worldId, "main", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(250L, stored.EvaluationCompletedAtUnixMilliseconds);
        Assert.Equal("evaluated", stored.ScheduleStatus);
    }

    [Fact]
    public async Task ConcurrentLowerRevisionCannotWinAfterHigherRevisionPublishes()
    {
        var worldId = Guid.NewGuid();
        var runtime = new InMemoryWorldZoneRuntimeRegistry();
        var baseline = Snapshot(worldId, revision: 10L, completedAt: 100L, observedAt: 100L);
        Assert.True(await runtime.TryPublish(baseline, CancellationToken.None));

        var candidates = Enumerable.Range(0, 32)
            .Select(index => index == 0
                ? Snapshot(worldId, revision: 12L, completedAt: 200L, observedAt: 200L)
                : Snapshot(worldId, revision: 11L, completedAt: 300L + index, observedAt: 300L + index))
            .Select(snapshot => Task.Run(
                () => runtime.TryPublish(snapshot, CancellationToken.None)))
            .ToArray();

        await Task.WhenAll(candidates);
        var stored = await runtime.Get(worldId, "main", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(12L, stored.EvaluatedWorldRevision);
    }

    private static WorldZoneRuntimeSnapshot Snapshot(
        Guid worldId,
        long revision,
        long completedAt,
        long observedAt)
    {
        var zone = new WorldZoneScheduleDefinition(
            worldId, "main", "active", -10d, -10d, 10d, 10d);
        return WorldZoneRuntimeSnapshotPolicy.CreateDeferredSnapshot(
                null,
                zone,
                "active",
                new ZoneSessionCounts(1, 1, observedAt),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10),
                "evaluated",
                observedAt)
            with
            {
                EngineStatus = "inference_only",
                EvaluatedWorldRevision = revision,
                EvaluationCompletedAtUnixMilliseconds = completedAt,
                ObservedAtUnixMilliseconds = observedAt
            };
    }
}
