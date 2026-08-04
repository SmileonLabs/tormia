using Xunit;

public sealed class PlayerMotionRuntimeSessionTests
{
    [Fact]
    public async Task MotionPublicationCannotCrossActivationEpoch()
    {
        var registry = new InMemoryWorldPlayerMotionRuntimeRegistry();
        var worldId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();
        var oldState = State(worldId, avatarId, oldSession, 1d);
        var activated = State(worldId, avatarId, newSession, 10d);

        await registry.Activate(oldState, CancellationToken.None);
        await registry.Activate(activated, CancellationToken.None);

        Assert.False(await registry.Set(
            oldState with { PositionX = 2d, ServerTick = 2 },
            CancellationToken.None));
        Assert.True(await registry.Set(
            activated with { PositionX = 11d, ServerTick = 1 },
            CancellationToken.None));
        var current = await registry.Get(
            worldId,
            avatarId,
            CancellationToken.None);
        Assert.Equal(newSession, current!.RuntimeSessionId);
        Assert.Equal(11d, current.PositionX);
    }

    [Fact]
    public async Task NewIntentEpochReplacesHigherOldSequence()
    {
        var registry = new InMemoryWorldPlayerIntentRegistry();
        var worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();

        Assert.True((await registry.InitializeHttpWriter(
            new PlayerMotionTransportWriterState(
                worldId, userId, avatarId, "zone", oldSession,
                "http", 1, 1), CancellationToken.None)).Accepted);

        Assert.True(await registry.Submit(
            new WorldPlayerIntent(
                worldId, userId, avatarId, "zone", 99, 1f, 0f, 1f, 1,
                oldSession, WorldRevision: 1, WriterEpoch: 1),
            CancellationToken.None));
        Assert.True((await registry.InitializeHttpWriter(
            new PlayerMotionTransportWriterState(
                worldId, userId, avatarId, "zone", newSession,
                "http", 1, 1), CancellationToken.None)).Accepted);
        Assert.True(await registry.Submit(
            new WorldPlayerIntent(
                worldId, userId, avatarId, "zone", 1, 0f, 0f, 0f, 2,
                newSession, WorldRevision: 1, WriterEpoch: 1),
            CancellationToken.None));

        var current = await registry.Get(
            worldId,
            avatarId,
            CancellationToken.None);
        Assert.Equal(newSession, current!.RuntimeSessionId);
        Assert.Equal(1, current.Sequence);
    }

    [Fact]
    public async Task RuntimeDeactivationIsSessionScopedAndIdempotent()
    {
        var registry = new InMemoryWorldPlayerMotionRuntimeRegistry();
        var worldId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var activeSession = Guid.NewGuid();
        var staleSession = Guid.NewGuid();
        await registry.Activate(
            State(worldId, avatarId, activeSession, 1d),
            CancellationToken.None);

        Assert.Equal(RuntimeSessionDeactivationResult.SessionMismatch,
            await registry.Deactivate(worldId, avatarId, staleSession,
                CancellationToken.None));
        Assert.NotNull(await registry.Get(
            worldId, avatarId, CancellationToken.None));

        Assert.Equal(RuntimeSessionDeactivationResult.Deactivated,
            await registry.Deactivate(worldId, avatarId, activeSession,
                CancellationToken.None));
        Assert.Null(await registry.Get(
            worldId, avatarId, CancellationToken.None));
        Assert.Equal(RuntimeSessionDeactivationResult.AlreadyInactive,
            await registry.Deactivate(worldId, avatarId, activeSession,
                CancellationToken.None));
        Assert.Equal(RuntimeSessionDeactivationResult.SessionMismatch,
            await registry.Deactivate(worldId, avatarId, staleSession,
                CancellationToken.None));
    }

    [Fact]
    public async Task OldSessionCleanupCannotEraseNewIntent()
    {
        var registry = new InMemoryWorldPlayerIntentRegistry();
        var worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var avatarId = Guid.NewGuid();
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();
        Assert.True((await registry.InitializeHttpWriter(
            new PlayerMotionTransportWriterState(
                worldId, userId, avatarId, "zone", newSession,
                "http", 1, 1), CancellationToken.None)).Accepted);
        await registry.Submit(new WorldPlayerIntent(
            worldId, userId, avatarId, "zone", 1, 0, 0, 0, 1,
            newSession, WorldRevision: 1, WriterEpoch: 1),
            CancellationToken.None);

        await registry.ClearIfSessionMatches(
            worldId, avatarId, oldSession, CancellationToken.None);

        var current = await registry.Get(
            worldId, avatarId, CancellationToken.None);
        Assert.Equal(newSession, current!.RuntimeSessionId);
    }

    private static WorldPlayerMotionState State(
        Guid worldId,
        Guid avatarId,
        Guid runtimeSessionId,
        double positionX) =>
        new(
            worldId,
            avatarId,
            "zone",
            positionX,
            0d,
            0d,
            0,
            "idle",
            1,
            RuntimeSessionId: runtimeSessionId);
}
