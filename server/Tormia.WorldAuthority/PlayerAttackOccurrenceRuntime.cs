using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

internal sealed record WorldPlayerAttackOccurrence(
    Guid OccurrenceId,
    Guid WorldId,
    Guid ActorUserId,
    Guid ActorEntityId,
    Guid TargetEntityId,
    Guid ToolEntityId,
    string PackageId,
    string PackageVersion,
    string ActionId,
    int DefinitionVersion,
    long ContactOpensAtUnixMilliseconds,
    long ContactClosesAtUnixMilliseconds,
    bool ContactResolved = false);

internal interface IWorldPlayerAttackOccurrenceRegistry
{
    string BackendName { get; }
    Task Set(WorldPlayerAttackOccurrence occurrence, CancellationToken cancellationToken);
    Task<WorldPlayerAttackOccurrence?> Get(Guid worldId, Guid occurrenceId, CancellationToken cancellationToken);
    Task<bool> TryResolve(Guid worldId, Guid occurrenceId, CancellationToken cancellationToken);
    Task Remove(Guid worldId, Guid occurrenceId, CancellationToken cancellationToken);
}

internal sealed class RedisWorldPlayerAttackOccurrenceRegistry(
    IConnectionMultiplexer redis) : IWorldPlayerAttackOccurrenceRegistry
{
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();
    public string BackendName => "redis";

    public async Task Set(
        WorldPlayerAttackOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(occurrence.WorldId, occurrence.OccurrenceId),
            JsonSerializer.Serialize(occurrence, JsonOptions),
            Retention);
    }

    public async Task<WorldPlayerAttackOccurrence?> Get(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, occurrenceId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<WorldPlayerAttackOccurrence>(
                value!, JsonOptions);
    }

    public async Task<bool> TryResolve(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        const string script = "local v=redis.call('GET',KEYS[1]); if not v then return 0 end; local o=cjson.decode(v); if o.contactResolved then return 0 end; o.contactResolved=true; redis.call('SET',KEYS[1],cjson.encode(o),'PX',ARGV[1]); return 1";
        var result = await database.ScriptEvaluateAsync(
            script,
            [Key(worldId, occurrenceId)],
            [(long)Retention.TotalMilliseconds]);
        return (long)result == 1;
    }

    public async Task Remove(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken) =>
        await database.KeyDeleteAsync(Key(worldId, occurrenceId));

    private static string Key(Guid worldId, Guid occurrenceId) =>
        "tormia:world:" + worldId.ToString("N") +
        ":player-attack:" + occurrenceId.ToString("N");
}

internal sealed class InMemoryWorldPlayerAttackOccurrenceRegistry :
    IWorldPlayerAttackOccurrenceRegistry
{
    private readonly ConcurrentDictionary<string, WorldPlayerAttackOccurrence>
        occurrences = new();
    public string BackendName => "in_memory_development";

    public Task Set(
        WorldPlayerAttackOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        occurrences[Key(occurrence.WorldId, occurrence.OccurrenceId)] = occurrence;
        return Task.CompletedTask;
    }

    public Task<WorldPlayerAttackOccurrence?> Get(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        occurrences.TryGetValue(Key(worldId, occurrenceId), out var occurrence);
        return Task.FromResult<WorldPlayerAttackOccurrence?>(occurrence);
    }

    public Task<bool> TryResolve(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        var key = Key(worldId, occurrenceId);
        while (occurrences.TryGetValue(key, out var current))
        {
            if (current.ContactResolved) return Task.FromResult(false);
            if (occurrences.TryUpdate(
                    key,
                    current with { ContactResolved = true },
                    current))
                return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task Remove(
        Guid worldId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        occurrences.TryRemove(Key(worldId, occurrenceId), out _);
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid occurrenceId) =>
        worldId.ToString("N") + ":" + occurrenceId.ToString("N");
}

internal sealed record BeginPlayerAttackOccurrenceResult(
    bool Accepted,
    string? RejectionCode,
    Guid? OccurrenceId,
    long? ContactOpensAtUnixMilliseconds,
    long? ContactClosesAtUnixMilliseconds)
{
    public static BeginPlayerAttackOccurrenceResult Rejected(string code) =>
        new(false, code, null, null, null);
}

internal sealed record ResolvePlayerAttackOccurrenceResult(
    bool Accepted,
    string? RejectionCode,
    long? Revision,
    string? EventId,
    bool DamageResult,
    bool TargetDefeated)
{
    public static ResolvePlayerAttackOccurrenceResult Rejected(string code) =>
        new(false, code, null, null, false, false);
}

internal sealed record PlayerAttackContactPayload(Guid OccurrenceId);
