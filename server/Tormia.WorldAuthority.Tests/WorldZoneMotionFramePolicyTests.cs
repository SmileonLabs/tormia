using Xunit;

public sealed class WorldZoneMotionFramePolicyTests
{
    [Fact]
    public void CreateFiltersScopeDeduplicatesAndOrdersDeterministically()
    {
        var worldId = Guid.NewGuid();
        var otherWorldId = Guid.NewGuid();
        var firstAvatar = Guid.Parse(
            "00000000-0000-0000-0000-000000000001");
        var secondAvatar = Guid.Parse(
            "00000000-0000-0000-0000-000000000002");
        var occurrenceId = Guid.NewGuid();

        var frame = WorldZoneMotionFramePolicy.Create(
            worldId,
            "zone_main",
            new[]
            {
                State(worldId, secondAvatar, "zone_main", 4, 4d),
                State(worldId, firstAvatar, "zone_main", 2, 2d),
                State(worldId, firstAvatar, "zone_main", 5, 5d),
                State(worldId, Guid.NewGuid(), "zone_other", 9, 9d),
                State(otherWorldId, Guid.NewGuid(), "zone_main", 10, 10d)
            },
            occurrenceId,
            91,
            1234);

        Assert.NotNull(frame);
        Assert.Equal(worldId, frame!.WorldId);
        Assert.Equal(occurrenceId, frame.FrameOccurrenceId);
        Assert.Equal("zone_main", frame.ZoneKey);
        Assert.Equal(91, frame.ServerTick);
        Assert.Equal(1234, frame.ObservedAtUnixMilliseconds);
        Assert.Equal(2, frame.Items.Count);
        Assert.Equal(firstAvatar, frame.Items[0].AvatarEntityId);
        Assert.Equal(5, frame.Items[0].ServerTick);
        Assert.Equal(secondAvatar, frame.Items[1].AvatarEntityId);
    }

    [Fact]
    public void CreateRejectsEmptyOrInvalidScope()
    {
        var worldId = Guid.NewGuid();
        var state = State(
            worldId,
            Guid.NewGuid(),
            "zone_main",
            1,
            1d);

        Assert.Null(WorldZoneMotionFramePolicy.Create(
            Guid.Empty,
            "zone_main",
            new[] { state },
            Guid.NewGuid(),
            1,
            1));
        Assert.Null(WorldZoneMotionFramePolicy.Create(
            worldId,
            "not a semantic id",
            new[] { state },
            Guid.NewGuid(),
            1,
            1));
        Assert.Null(WorldZoneMotionFramePolicy.Create(
            worldId,
            "zone_main",
            Array.Empty<WorldPlayerMotionState>(),
            Guid.NewGuid(),
            1,
            1));
        Assert.Null(WorldZoneMotionFramePolicy.Create(
            worldId,
            "zone_main",
            new[] { state },
            Guid.NewGuid(),
            0,
            1));
        Assert.Null(WorldZoneMotionFramePolicy.Create(
            worldId,
            "zone_main",
            new[] { state },
            Guid.Empty,
            1,
            1));
    }

    [Fact]
    public void FrameTickDoesNotRegressWhenHighestActorTickLeavesOrReconnects()
    {
        var worldId = Guid.NewGuid();
        var first = State(worldId, Guid.NewGuid(), "zone_main", 100, 1d);
        var reconnected = State(worldId, Guid.NewGuid(), "zone_main", 1, 2d);

        var before = WorldZoneMotionFramePolicy.Create(
            worldId, "zone_main", new[] { first }, Guid.NewGuid(), 7, 1000);
        var after = WorldZoneMotionFramePolicy.Create(
            worldId, "zone_main", new[] { reconnected }, Guid.NewGuid(), 8, 1050);

        Assert.Equal(7, before!.ServerTick);
        Assert.Equal(8, after!.ServerTick);
        Assert.Equal(1, Assert.Single(after.Items).ServerTick);
    }

    private static WorldPlayerMotionState State(
        Guid worldId,
        Guid avatarId,
        string zoneKey,
        long serverTick,
        double positionX)
    {
        return new WorldPlayerMotionState(
            worldId,
            avatarId,
            zoneKey,
            positionX,
            2d,
            3d,
            4,
            "moving",
            1000 + serverTick,
            1d,
            0d,
            0d,
            2d,
            true,
            serverTick,
            null,
            null,
            Guid.NewGuid());
    }
}
