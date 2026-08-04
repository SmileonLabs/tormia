using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Client-observed pose after local collision presentation. It is diagnostics
/// and reconciliation evidence only; it never owns the authoritative runtime
/// position and never writes durable world data.
/// </summary>
internal sealed record WorldPlayerPoseObservation(
    Guid WorldId,
    Guid AvatarEntityId,
    string ZoneKey,
    long IntentSequence,
    long PoseSequence,
    double PositionX,
    double PositionY,
    double PositionZ,
    string MotionStatus,
    long ObservedAtUnixMilliseconds,
    Guid RuntimeSessionId = default);

internal interface IWorldPlayerPoseObservationRegistry
{
    string BackendName { get; }
    Task<bool> SubmitLatest(
        WorldPlayerPoseObservation observation,
        CancellationToken cancellationToken);
    Task<WorldPlayerPoseObservation?> Get(
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

internal sealed class RedisWorldPlayerPoseObservationRegistry(
    IConnectionMultiplexer redis)
    : IWorldPlayerPoseObservationRegistry
{
    private const string SubmitLatestScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
            local decoded = cjson.decode(current)
            if decoded.runtimeSessionId == ARGV[1] and
               decoded.poseSequence and
               tonumber(decoded.poseSequence) >= tonumber(ARGV[2]) then
                return 0
            end
        end
        redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[4])
        return 1
        """;
    private static readonly TimeSpan ObservationTtl =
        TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";

    public async Task<bool> SubmitLatest(
        WorldPlayerPoseObservation observation,
        CancellationToken cancellationToken)
    {
        var result = await database.ScriptEvaluateAsync(
            SubmitLatestScript,
            new RedisKey[]
            {
                Key(observation.WorldId, observation.AvatarEntityId)
            },
            new RedisValue[]
            {
                observation.RuntimeSessionId.ToString("D"),
                observation.PoseSequence,
                JsonSerializer.Serialize(observation, JsonOptions),
                (long)ObservationTtl.TotalMilliseconds
            });
        return (int)result == 1;
    }

    public async Task<WorldPlayerPoseObservation?> Get(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(
            Key(worldId, avatarEntityId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<WorldPlayerPoseObservation>(
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
        ":pose-observation";
}

internal sealed class InMemoryWorldPlayerPoseObservationRegistry
    : IWorldPlayerPoseObservationRegistry
{
    private readonly ConcurrentDictionary<
        string,
        WorldPlayerPoseObservation> observations = new();

    public string BackendName => "in_memory_development";

    public Task<bool> SubmitLatest(
        WorldPlayerPoseObservation observation,
        CancellationToken cancellationToken)
    {
        var key = Key(observation.WorldId, observation.AvatarEntityId);
        while (true)
        {
            if (!observations.TryGetValue(key, out var current))
            {
                return Task.FromResult(
                    observations.TryAdd(key, observation));
            }
            if (current.RuntimeSessionId == observation.RuntimeSessionId &&
                current.PoseSequence >= observation.PoseSequence)
            {
                return Task.FromResult(false);
            }
            if (observations.TryUpdate(key, observation, current))
            {
                return Task.FromResult(true);
            }
        }
    }

    public Task<WorldPlayerPoseObservation?> Get(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        observations.TryGetValue(
            Key(worldId, avatarEntityId),
            out var observation);
        return Task.FromResult<WorldPlayerPoseObservation?>(observation);
    }

    public Task Clear(
        Guid worldId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        observations.TryRemove(Key(worldId, avatarEntityId), out _);
        return Task.CompletedTask;
    }

    public Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        if (observations.TryGetValue(key, out var current) &&
            current.RuntimeSessionId == runtimeSessionId)
            observations.TryRemove(new KeyValuePair<string,
                WorldPlayerPoseObservation>(key, current));
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
}
