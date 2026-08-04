using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Latest accepted ephemeral runtime action for one player avatar. The action
/// has already passed the immutable Action + assigned Rule Block evaluation.
/// A simulation adapter may consume it only when its canonical action ID also
/// matches the avatar's authored semantic action relation.
/// </summary>
internal sealed record WorldPlayerRuntimeActionIntent(
    Guid WorldId,
    Guid AvatarEntityId,
    Guid OccurrenceId,
    string ActionId,
    long AcceptedAtUnixMilliseconds,
    Guid RuntimeSessionId = default);

internal interface IWorldPlayerRuntimeActionIntentRegistry
{
    string BackendName { get; }
    Task Submit(
        WorldPlayerRuntimeActionIntent intent,
        CancellationToken cancellationToken);
    Task<WorldPlayerRuntimeActionIntent?> Get(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken);
    Task Clear(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken);
    Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken);
}

internal sealed class RedisWorldPlayerRuntimeActionIntentRegistry(
    IConnectionMultiplexer redis)
    : IWorldPlayerRuntimeActionIntentRegistry
{
    private static readonly TimeSpan IntentTtl = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";

    public async Task Submit(
        WorldPlayerRuntimeActionIntent intent,
        CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(intent.WorldId, intent.AvatarEntityId),
            JsonSerializer.Serialize(intent, JsonOptions),
            IntentTtl);
    }

    public async Task<WorldPlayerRuntimeActionIntent?> Get(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(
            Key(worldId, avatarEntityId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<WorldPlayerRuntimeActionIntent>(
                value!,
                JsonOptions);
    }

    public async Task Clear(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        await database.KeyDeleteAsync(Key(worldId, avatarEntityId));
    }

    public async Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken)
    {
        const string script = """
            local current = redis.call('GET', KEYS[1])
            if not current then return 0 end
            local decoded = cjson.decode(current)
            if decoded.runtimeSessionId ~= ARGV[1] then return 0 end
            return redis.call('DEL', KEYS[1])
            """;
        await database.ScriptEvaluateAsync(script,
            new RedisKey[] { Key(worldId, avatarEntityId) },
            new RedisValue[] { runtimeSessionId.ToString("D") });
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") +
        ":avatar:" + avatarEntityId.ToString("N") +
        ":runtime-action";
}

internal sealed class InMemoryWorldPlayerRuntimeActionIntentRegistry
    : IWorldPlayerRuntimeActionIntentRegistry
{
    private readonly ConcurrentDictionary<
        string,
        WorldPlayerRuntimeActionIntent> intents = new();

    public string BackendName => "in_memory_development";

    public Task Submit(
        WorldPlayerRuntimeActionIntent intent,
        CancellationToken cancellationToken)
    {
        intents[Key(intent.WorldId, intent.AvatarEntityId)] = intent;
        return Task.CompletedTask;
    }

    public Task<WorldPlayerRuntimeActionIntent?> Get(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        intents.TryGetValue(
            Key(worldId, avatarEntityId),
            out var intent);
        return Task.FromResult<WorldPlayerRuntimeActionIntent?>(intent);
    }

    public Task Clear(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        intents.TryRemove(Key(worldId, avatarEntityId), out _);
        return Task.CompletedTask;
    }

    public Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        if (intents.TryGetValue(key, out var current) &&
            current.RuntimeSessionId == runtimeSessionId)
            intents.TryRemove(new KeyValuePair<string,
                WorldPlayerRuntimeActionIntent>(key, current));
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
}
