using System.Collections.Concurrent;
using StackExchange.Redis;

internal interface IWorldActionCooldownRuntimeRegistry
{
    string BackendName { get; }

    Task<bool> TryAcquire(
        Guid worldId,
        Guid actorEntityId,
        Guid? toolEntityId,
        string actionId,
        TimeSpan cooldown,
        CancellationToken cancellationToken);
}

internal sealed class RedisWorldActionCooldownRuntimeRegistry(
    IConnectionMultiplexer redis) : IWorldActionCooldownRuntimeRegistry
{
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";

    public async Task<bool> TryAcquire(
        Guid worldId,
        Guid actorEntityId,
        Guid? toolEntityId,
        string actionId,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        if (cooldown <= TimeSpan.Zero) return true;
        var key =
            "tov:action-cooldown:" + worldId.ToString("N") + ":" +
            actorEntityId.ToString("N") + ":" +
            (toolEntityId?.ToString("N") ?? "none") + ":" + actionId;
        return await database.StringSetAsync(
                key,
                "1",
                cooldown,
                When.NotExists)
            .WaitAsync(cancellationToken);
    }
}

internal sealed class InMemoryWorldActionCooldownRuntimeRegistry :
    IWorldActionCooldownRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, long> expiresAtTicks = new();

    public string BackendName => "in_memory";

    public Task<bool> TryAcquire(
        Guid worldId,
        Guid actorEntityId,
        Guid? toolEntityId,
        string actionId,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cooldown <= TimeSpan.Zero) return Task.FromResult(true);
        var key =
            worldId.ToString("N") + ":" + actorEntityId.ToString("N") + ":" +
            (toolEntityId?.ToString("N") ?? "none") + ":" + actionId;
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var expires = now + cooldown.Ticks;
        while (true)
        {
            if (!expiresAtTicks.TryGetValue(key, out var current))
            {
                if (expiresAtTicks.TryAdd(key, expires))
                    return Task.FromResult(true);
                continue;
            }
            if (current > now) return Task.FromResult(false);
            if (expiresAtTicks.TryUpdate(key, expires, current))
                return Task.FromResult(true);
        }
    }
}
