using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Ephemeral server-authoritative position for a player avatar. It is derived
/// from a durable spawn transform, a durable movement_speed Fact, and the
/// newest transient intent; it never overwrites authored entity placement.
/// </summary>
internal sealed record WorldPlayerMotionState(
    Guid WorldId,
    Guid AvatarEntityId,
    string ZoneKey,
    double PositionX,
    double PositionY,
    double PositionZ,
    long LastProcessedIntentSequence,
    string MotionStatus,
    long UpdatedAtUnixMilliseconds);

internal interface IWorldPlayerMotionRuntimeRegistry
{
    string BackendName { get; }
    Task<WorldPlayerMotionState?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorldPlayerMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> avatarEntityIds,
        CancellationToken cancellationToken);
    Task Set(WorldPlayerMotionState state, CancellationToken cancellationToken);
}

internal sealed class RedisWorldPlayerMotionRuntimeRegistry(IConnectionMultiplexer redis) : IWorldPlayerMotionRuntimeRegistry
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();
    public string BackendName => "redis";

    public async Task<WorldPlayerMotionState?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, avatarEntityId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<WorldPlayerMotionState>(value!, JsonOptions);
    }

    public async Task<IReadOnlyList<WorldPlayerMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> avatarEntityIds,
        CancellationToken cancellationToken)
    {
        if (avatarEntityIds.Count == 0) return Array.Empty<WorldPlayerMotionState>();
        var keys = avatarEntityIds.Select(id => (RedisKey)Key(worldId, id)).ToArray();
        var values = await database.StringGetAsync(keys);
        var states = new List<WorldPlayerMotionState>(values.Length);
        foreach (var value in values)
        {
            if (value.IsNullOrEmpty) continue;
            var state = JsonSerializer.Deserialize<WorldPlayerMotionState>(value!, JsonOptions);
            if (state is not null) states.Add(state);
        }
        return states;
    }

    public async Task Set(WorldPlayerMotionState state, CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(state.WorldId, state.AvatarEntityId),
            JsonSerializer.Serialize(state, JsonOptions),
            StateTtl);
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":motion";
}

internal sealed class InMemoryWorldPlayerMotionRuntimeRegistry : IWorldPlayerMotionRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, WorldPlayerMotionState> states = new();
    public string BackendName => "in_memory_development";

    public Task<WorldPlayerMotionState?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        states.TryGetValue(Key(worldId, avatarEntityId), out var state);
        return Task.FromResult<WorldPlayerMotionState?>(state);
    }

    public Task<IReadOnlyList<WorldPlayerMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> avatarEntityIds,
        CancellationToken cancellationToken)
    {
        var results = new List<WorldPlayerMotionState>(avatarEntityIds.Count);
        foreach (var avatarEntityId in avatarEntityIds)
        {
            if (states.TryGetValue(Key(worldId, avatarEntityId), out var state))
            {
                results.Add(state);
            }
        }
        return Task.FromResult<IReadOnlyList<WorldPlayerMotionState>>(results);
    }

    public Task Set(WorldPlayerMotionState state, CancellationToken cancellationToken)
    {
        states[Key(state.WorldId, state.AvatarEntityId)] = state;
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid avatarEntityId) => worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
}

/// <summary>
/// Runs a deliberately limited server kinematic pass. It accepts only active
/// input for avatars assigned to a configured Zone, reads speed from the
/// authored movement_speed Fact, and clamps X/Z to that Zone's durable bounds.
/// Terrain collision, gravity, water, mounts, and movement-mode rules remain
/// separate adapters until an authoritative collision representation exists.
/// </summary>
internal sealed class WorldPlayerMotionSimulationScheduler(
    WorldAuthorityRepository repository,
    IWorldPlayerIntentRegistry intents,
    IWorldPlayerMotionRuntimeRegistry motion,
    IWorldZoneExecutionLeaseRegistry executionLeases,
    IWorldZoneRuntimeNotificationPublisher runtimeNotifications,
    ILogger<WorldPlayerMotionSimulationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ZoneRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AvatarRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly string ownerId = Environment.MachineName + ":motion:" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, AvatarCacheEntry> avatarCache = new();
    private IReadOnlyList<WorldZoneScheduleDefinition> zones = Array.Empty<WorldZoneScheduleDefinition>();
    private DateTimeOffset nextZoneRefreshAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextZoneRefreshAt)
                {
                    zones = await repository.GetZoneScheduleDefinitions(stoppingToken);
                    nextZoneRefreshAt = now + ZoneRefreshInterval;
                }

                foreach (var zone in zones)
                {
                    if (zone.SimulationMode == "dormant") continue;
                    var leaseHeld = await executionLeases.TryAcquire(
                        zone.WorldId, zone.ZoneKey, "player-motion", ownerId, LeaseDuration, stoppingToken);
                    if (!leaseHeld) continue;

                    var configurations = await GetAvatarConfigurations(zone, now, stoppingToken);
                    var zoneRuntimeChanged = false;
                    foreach (var configuration in configurations)
                    {
                        zoneRuntimeChanged |= await AdvanceAvatar(configuration, zone, now, stoppingToken);
                    }

                    if (zoneRuntimeChanged)
                    {
                        await runtimeNotifications.PublishChanged(
                            zone.WorldId,
                            zone.ZoneKey,
                            now.ToUnixTimeMilliseconds(),
                            stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Player motion scheduler iteration failed.");
            }

            try
            {
                await Task.Delay(LoopInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<IReadOnlyList<WorldPlayerAvatarMotionConfiguration>> GetAvatarConfigurations(
        WorldZoneScheduleDefinition zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var key = zone.WorldId.ToString("N") + ":" + zone.ZoneKey;
        if (avatarCache.TryGetValue(key, out var existing) && existing.ExpiresAt > now)
        {
            return existing.Configurations;
        }

        var configurations = await repository.GetPlayerAvatarMotionConfigurations(
            zone.WorldId, zone.ZoneKey, cancellationToken);
        avatarCache[key] = new AvatarCacheEntry(configurations, now + AvatarRefreshInterval);
        return configurations;
    }

    private async Task<bool> AdvanceAvatar(
        WorldPlayerAvatarMotionConfiguration configuration,
        WorldZoneScheduleDefinition zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await motion.Get(configuration.WorldId, configuration.AvatarEntityId, cancellationToken);
        var fallback = existing ?? new WorldPlayerMotionState(
            configuration.WorldId,
            configuration.AvatarEntityId,
            configuration.ZoneKey,
            configuration.SpawnPositionX,
            configuration.SpawnPositionY,
            configuration.SpawnPositionZ,
            0,
            "idle",
            now.ToUnixTimeMilliseconds());
        var intent = await intents.Get(configuration.WorldId, configuration.AvatarEntityId, cancellationToken);

        if (configuration.MovementSpeed is not > 0d)
        {
            if (existing is null || fallback.MotionStatus != "missing_movement_speed")
            {
                await motion.Set(fallback with { MotionStatus = "missing_movement_speed", UpdatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds() }, cancellationToken);
                return true;
            }
            return false;
        }

        if (intent is null || !string.Equals(intent.ZoneKey, configuration.ZoneKey, StringComparison.Ordinal))
        {
            if (existing is null || fallback.MotionStatus != "idle")
            {
                await motion.Set(fallback with { MotionStatus = "idle", UpdatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds() }, cancellationToken);
                return true;
            }
            return false;
        }

        var previousAt = DateTimeOffset.FromUnixTimeMilliseconds(fallback.UpdatedAtUnixMilliseconds);
        var seconds = Math.Clamp((now - previousAt).TotalSeconds, 0d, LoopInterval.TotalSeconds * 1.5d);
        var directionLength = Math.Sqrt(intent.MoveX * intent.MoveX + intent.MoveZ * intent.MoveZ);
        var directionX = directionLength > 0.0001d ? intent.MoveX / directionLength : 0d;
        var directionZ = directionLength > 0.0001d ? intent.MoveZ / directionLength : 0d;
        var nextX = Math.Clamp(fallback.PositionX + directionX * configuration.MovementSpeed.Value * seconds, zone.MinX, zone.MaxX);
        var nextZ = Math.Clamp(fallback.PositionZ + directionZ * configuration.MovementSpeed.Value * seconds, zone.MinZ, zone.MaxZ);
        var next = fallback with
        {
            PositionX = nextX,
            PositionY = configuration.SpawnPositionY,
            PositionZ = nextZ,
            LastProcessedIntentSequence = Math.Max(fallback.LastProcessedIntentSequence, intent.Sequence),
            MotionStatus = directionLength > 0.0001d ? "moving" : "idle",
            UpdatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds()
        };
        await motion.Set(next, cancellationToken);
        return existing is null ||
               !string.Equals(fallback.MotionStatus, next.MotionStatus, StringComparison.Ordinal) ||
               Math.Abs(fallback.PositionX - next.PositionX) > 0.0001d ||
               Math.Abs(fallback.PositionY - next.PositionY) > 0.0001d ||
               Math.Abs(fallback.PositionZ - next.PositionZ) > 0.0001d;
    }

    private sealed record AvatarCacheEntry(
        IReadOnlyList<WorldPlayerAvatarMotionConfiguration> Configurations,
        DateTimeOffset ExpiresAt);
}
