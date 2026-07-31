using Xunit;

public sealed class WorldZoneSessionRegistryTests
{
    [Fact]
    public async Task HttpLeaseKeepsZoneActiveWhenRealtimeSocketLeaves()
    {
        var registry = new InMemoryWorldZoneSessionRegistry();
        var worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string zoneKey = "world_main";

        await registry.Register(
            worldId,
            zoneKey,
            userId,
            "signalr:connection",
            CancellationToken.None);
        await registry.Refresh(
            worldId,
            zoneKey,
            userId,
            "http:session",
            CancellationToken.None);
        await registry.Unregister(
            worldId,
            zoneKey,
            userId,
            "signalr:connection",
            CancellationToken.None);

        var summary = await registry.GetSummary(
            worldId,
            zoneKey,
            CancellationToken.None);

        Assert.Equal(1, summary.ConnectionCount);
        Assert.Equal(1, summary.UserCount);
        Assert.Equal(
            "active",
            WorldZoneRuntimePolicy.ResolveRunState(
                "active",
                summary.ConnectionCount));
    }

    [Fact]
    public async Task ReleasingLastLeaseReturnsActiveZoneToIdle()
    {
        var registry = new InMemoryWorldZoneSessionRegistry();
        var worldId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string zoneKey = "world_main";
        const string connectionId = "http:session";

        await registry.Refresh(
            worldId,
            zoneKey,
            userId,
            connectionId,
            CancellationToken.None);
        await registry.Unregister(
            worldId,
            zoneKey,
            userId,
            connectionId,
            CancellationToken.None);

        var summary = await registry.GetSummary(
            worldId,
            zoneKey,
            CancellationToken.None);

        Assert.Equal(0, summary.ConnectionCount);
        Assert.Equal(
            "idle",
            WorldZoneRuntimePolicy.ResolveRunState(
                "active",
                summary.ConnectionCount));
    }
}
