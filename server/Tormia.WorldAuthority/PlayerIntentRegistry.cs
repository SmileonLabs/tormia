using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Stores the latest controller input for a server-owned player avatar. Inputs
/// are ephemeral transport data, deliberately separate from commands, events,
/// world Facts, and rule bindings. A later authoritative motion step consumes
/// these samples and emits the resulting durable gameplay state when needed.
/// </summary>
internal sealed record WorldPlayerIntent(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    long Sequence,
    float MoveX,
    float MoveZ,
    float MoveSpeed,
    long ReceivedAtUnixMilliseconds);

internal interface IWorldPlayerIntentRegistry
{
    string BackendName { get; }
    TimeSpan LeaseDuration { get; }
    Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken);
    Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
}

internal sealed class RedisWorldPlayerIntentRegistry(IConnectionMultiplexer redis) : IWorldPlayerIntentRegistry
{
    private const string SubmitLatestScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
            local decoded = cjson.decode(current)
            if decoded.sequence and tonumber(decoded.sequence) >= tonumber(ARGV[1]) then
                return 0
            end
        end
        redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
        return 1
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(3);

    public async Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(intent, JsonOptions);
        var result = await database.ScriptEvaluateAsync(
            SubmitLatestScript,
            new RedisKey[] { Key(intent.WorldId, intent.AvatarEntityId) },
            new RedisValue[] { intent.Sequence, json, (long)LeaseDuration.TotalMilliseconds });
        return (int)result == 1;
    }

    public async Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, avatarEntityId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<WorldPlayerIntent>(value!, JsonOptions);
    }

    public async Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        await database.KeyDeleteAsync(Key(worldId, avatarEntityId));
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":intent";
}

internal sealed class InMemoryWorldPlayerIntentRegistry : IWorldPlayerIntentRegistry
{
    private readonly ConcurrentDictionary<string, StoredIntent> intents = new();
    public string BackendName => "in_memory_development";
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(3);

    public Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken)
    {
        var key = Key(intent.WorldId, intent.AvatarEntityId);
        var now = DateTimeOffset.UtcNow;
        var candidate = new StoredIntent(intent, now + LeaseDuration);
        var result = intents.AddOrUpdate(
            key,
            _ => candidate,
            (_, current) => current.ExpiresAt <= now || intent.Sequence > current.Intent.Sequence
                ? candidate
                : current);
        return Task.FromResult(ReferenceEquals(result, candidate));
    }

    public Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        if (!intents.TryGetValue(key, out var stored) || stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            intents.TryRemove(key, out _);
            return Task.FromResult<WorldPlayerIntent?>(null);
        }
        return Task.FromResult<WorldPlayerIntent?>(stored.Intent);
    }

    public Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        intents.TryRemove(Key(worldId, avatarEntityId), out _);
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid avatarEntityId) => worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
    private sealed record StoredIntent(WorldPlayerIntent Intent, DateTimeOffset ExpiresAt);
}
