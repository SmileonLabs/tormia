using System.Collections.Concurrent;
using System.Diagnostics;
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
    long UpdatedAtUnixMilliseconds,
    double VelocityX = 0d,
    double VelocityY = 0d,
    double VelocityZ = 0d,
    double GroundReferenceY = 0d,
    bool Grounded = true,
    long ServerTick = 0,
    Guid? LastProcessedRuntimeActionOccurrenceId = null,
    Guid? GroundSupportEntityId = null,
    Guid RuntimeSessionId = default);

internal interface IWorldPlayerMotionRuntimeRegistry
{
    string BackendName { get; }
    Task<WorldPlayerMotionState?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorldPlayerMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> avatarEntityIds,
        CancellationToken cancellationToken);
    Task<bool> Set(WorldPlayerMotionState state, CancellationToken cancellationToken);
    Task Activate(WorldPlayerMotionState state, CancellationToken cancellationToken);
    Task<RuntimeSessionDeactivationResult> Deactivate(
        Guid worldId, Guid avatarEntityId, Guid runtimeSessionId,
        CancellationToken cancellationToken);
}

internal enum RuntimeSessionDeactivationResult
{
    SessionMismatch = 0,
    Deactivated = 1,
    AlreadyInactive = 2
}

internal sealed class RedisWorldPlayerMotionRuntimeRegistry(IConnectionMultiplexer redis) : IWorldPlayerMotionRuntimeRegistry
{
    private const string SetCurrentSessionScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then return 0 end
        local decoded = cjson.decode(current)
        if decoded.runtimeSessionId ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
        return 1
        """;
    private const string DeactivateCurrentSessionScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
            local decoded = cjson.decode(current)
            if decoded.runtimeSessionId ~= ARGV[1] then return 0 end
            redis.call('DEL', KEYS[1])
            redis.call('SET', KEYS[2], ARGV[1], 'PX', ARGV[2])
            return 1
        end
        local inactive = redis.call('GET', KEYS[2])
        if inactive == ARGV[1] then return 2 end
        return 0
        """;
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

    public async Task<bool> Set(WorldPlayerMotionState state, CancellationToken cancellationToken)
    {
        var result = await database.ScriptEvaluateAsync(
            SetCurrentSessionScript,
            new RedisKey[] { Key(state.WorldId, state.AvatarEntityId) },
            new RedisValue[] {
                state.RuntimeSessionId.ToString("D"),
                JsonSerializer.Serialize(state, JsonOptions),
                (long)StateTtl.TotalMilliseconds });
        return (int)result == 1;
    }

    public async Task Activate(
        WorldPlayerMotionState state,
        CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(state.WorldId, state.AvatarEntityId),
            JsonSerializer.Serialize(state, JsonOptions),
            StateTtl);
    }

    public async Task<RuntimeSessionDeactivationResult> Deactivate(
        Guid worldId, Guid avatarEntityId, Guid runtimeSessionId,
        CancellationToken cancellationToken)
    {
        var result = await database.ScriptEvaluateAsync(
            DeactivateCurrentSessionScript,
            new RedisKey[]
            {
                Key(worldId, avatarEntityId),
                InactiveKey(worldId, avatarEntityId)
            },
            new RedisValue[]
            {
                runtimeSessionId.ToString("D"),
                (long)StateTtl.TotalMilliseconds
            });
        return (RuntimeSessionDeactivationResult)(int)result;
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":motion";
    private static string InactiveKey(Guid worldId, Guid avatarEntityId) =>
        Key(worldId, avatarEntityId) + ":inactive-session";
}

internal sealed class InMemoryWorldPlayerMotionRuntimeRegistry : IWorldPlayerMotionRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, WorldPlayerMotionState> states = new();
    private readonly ConcurrentDictionary<string, Guid> inactiveSessions = new();
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

    public Task<bool> Set(WorldPlayerMotionState state, CancellationToken cancellationToken)
    {
        var key = Key(state.WorldId, state.AvatarEntityId);
        if (!states.TryGetValue(key, out var current))
        {
            return state.RuntimeSessionId == Guid.Empty
                ? Task.FromResult(states.TryAdd(key, state))
                : Task.FromResult(false);
        }
        if (current.RuntimeSessionId != state.RuntimeSessionId)
        {
            return Task.FromResult(false);
        }
        var updated = states.TryUpdate(key, state, current);
        return Task.FromResult(updated);
    }

    public Task Activate(
        WorldPlayerMotionState state,
        CancellationToken cancellationToken)
    {
        states[Key(state.WorldId, state.AvatarEntityId)] = state;
        return Task.CompletedTask;
    }

    public Task<RuntimeSessionDeactivationResult> Deactivate(
        Guid worldId, Guid avatarEntityId, Guid runtimeSessionId,
        CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        while (states.TryGetValue(key, out var current))
        {
            if (current.RuntimeSessionId != runtimeSessionId)
                return Task.FromResult(
                    RuntimeSessionDeactivationResult.SessionMismatch);
            if (!states.TryRemove(new KeyValuePair<string,
                    WorldPlayerMotionState>(key, current)))
                continue;
            inactiveSessions[key] = runtimeSessionId;
            return Task.FromResult(
                RuntimeSessionDeactivationResult.Deactivated);
        }
        return Task.FromResult(
            inactiveSessions.TryGetValue(key, out var inactive) &&
            inactive == runtimeSessionId
                ? RuntimeSessionDeactivationResult.AlreadyInactive
                : RuntimeSessionDeactivationResult.SessionMismatch);
    }

    private static string Key(Guid worldId, Guid avatarEntityId) => worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
}

/// <summary>
/// Integrates short-lived authoritative player motion selected by the complete
/// locomotion ontology contract. Input and accepted runtime actions are
/// transient; positions are resolved at a fixed tick against authored
/// primitive proxies and remain outside durable world Facts/events.
/// </summary>
internal sealed class WorldPlayerMotionSimulationScheduler(
    WorldAuthorityRepository repository,
    IWorldPlayerIntentRegistry intents,
    IWorldPlayerRuntimeActionIntentRegistry runtimeActions,
    IWorldPlayerMotionRuntimeRegistry motion,
    IWorldZoneSessionRegistry sessions,
    IWorldZoneExecutionLeaseRegistry executionLeases,
    IWorldZoneRuntimeNotificationPublisher runtimeNotifications,
    RealtimeTransportMetrics realtimeMetrics,
    ILogger<WorldPlayerMotionSimulationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan LoopInterval =
        TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LeaseDuration =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ZoneRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AvatarRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProxyRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly string ownerId = Environment.MachineName + ":motion:" + Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, AvatarCacheEntry> avatarCache = new();
    private readonly ConcurrentDictionary<string, ProxyCacheEntry> proxyCache = new();
    private IReadOnlyList<WorldZoneScheduleDefinition> zones = Array.Empty<WorldZoneScheduleDefinition>();
    private DateTimeOffset nextZoneRefreshAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var iterationStartedAt = Stopwatch.GetTimestamp();
            var activeAvatarCount = 0;
            var frameItemCount = 0;
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
                    var proxyConfigurations =
                        await GetCollisionProxyConfigurations(
                            zone,
                            now,
                            stoppingToken);
                    var currentMotionStates = await motion.GetMany(
                        zone.WorldId,
                        configurations
                            .Select(value => value.AvatarEntityId)
                            .ToArray(),
                        stoppingToken);
                    var collisionProxies = BuildCollisionProxies(
                        proxyConfigurations,
                        currentMotionStates);
                    var proxyConfigurationsByEntity =
                        proxyConfigurations
                            .GroupBy(value => value.EntityId)
                            .Where(group => group.Count() == 1)
                            .ToDictionary(
                                group => group.Key,
                                group => group.Single());
                    var activeUserIds = await sessions.GetActiveUserIds(
                        zone.WorldId,
                        zone.ZoneKey,
                        stoppingToken);
                    var changedStates =
                        new List<WorldPlayerMotionState>();
                    foreach (var configuration in configurations)
                    {
                        // Registered ownership is durable, but simulation presence
                        // is not. Offline avatars must stop refreshing their Redis
                        // motion TTL so a later entry can seed from the checkpoint.
                        if (!activeUserIds.Contains(configuration.UserId)) continue;
                        activeAvatarCount++;
                        proxyConfigurationsByEntity.TryGetValue(
                            configuration.AvatarEntityId,
                            out var actorProxyConfiguration);
                        var changedState = await AdvanceAvatar(
                            configuration,
                            actorProxyConfiguration,
                            collisionProxies,
                            zone,
                            now,
                            stoppingToken);
                        if (changedState is not null)
                        {
                            changedStates.Add(changedState);
                            frameItemCount++;
                        }
                    }

                    if (changedStates.Count > 0)
                    {
                        var observedAt = now.ToUnixTimeMilliseconds();
                        // This actor-derived maximum is a source diagnostic,
                        // not a Zone ordering key. SignalR and UDP share the
                        // frame occurrence; each actor state is ordered by its
                        // runtime session plus actor server tick.
                        var sourceFrameTick = changedStates.Max(
                            state => state.ServerTick);
                        await runtimeNotifications.PublishMotionFrame(
                            zone.WorldId,
                            zone.ZoneKey,
                            changedStates,
                            sourceFrameTick,
                            observedAt,
                            stoppingToken);
                        // Retain the old hint as a compatibility and HTTP
                        // recovery signal for clients without direct frames.
                        await runtimeNotifications.PublishChanged(
                            zone.WorldId,
                            zone.ZoneKey,
                            observedAt,
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

            var processingDuration = Stopwatch.GetElapsedTime(
                iterationStartedAt,
                Stopwatch.GetTimestamp());
            var remainingDelay = LoopInterval - processingDuration;
            realtimeMetrics.RecordPlayerMotionTick(
                processingDuration,
                activeAvatarCount,
                frameItemCount,
                remainingDelay <= TimeSpan.Zero);
            if (remainingDelay <= TimeSpan.Zero)
            {
                continue;
            }
            try
            {
                await Task.Delay(remainingDelay, stoppingToken);
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

    private async Task<IReadOnlyList<WorldCollisionProxyConfiguration>>
        GetCollisionProxyConfigurations(
            WorldZoneScheduleDefinition zone,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var key = zone.WorldId.ToString("N") + ":" + zone.ZoneKey;
        if (proxyCache.TryGetValue(key, out var existing) &&
            existing.ExpiresAt > now)
        {
            return existing.Configurations;
        }

        var configurations =
            await repository.GetWorldCollisionProxyConfigurations(
                zone.WorldId,
                zone.ZoneKey,
                cancellationToken);
        proxyCache[key] = new ProxyCacheEntry(
            configurations,
            now + ProxyRefreshInterval);
        return configurations;
    }

    private static IReadOnlyList<WorldCollisionProxy>
        BuildCollisionProxies(
            IReadOnlyList<WorldCollisionProxyConfiguration>
                configurations,
            IReadOnlyList<WorldPlayerMotionState> motionStates)
    {
        var runtimeByEntity = motionStates
            .GroupBy(value => value.AvatarEntityId)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single());
        var proxies = new List<WorldCollisionProxy>();
        foreach (var configuration in configurations)
        {
            if (!WorldCollisionProxyPolicy.TryCreate(
                    configuration,
                    out var proxy,
                    out _))
            {
                continue;
            }

            if (runtimeByEntity.TryGetValue(
                    configuration.EntityId,
                    out var runtime) &&
                string.Equals(
                    runtime.ZoneKey,
                    configuration.ZoneKey,
                    StringComparison.Ordinal))
            {
                proxy = WorldCollisionProxyPolicy.PlaceAtEntityPosition(
                    proxy!,
                    configuration,
                    runtime.PositionX,
                    runtime.PositionY,
                    runtime.PositionZ);
            }
            proxies.Add(proxy!);
        }
        return proxies;
    }

    private async Task<WorldPlayerMotionState?> AdvanceAvatar(
        WorldPlayerAvatarMotionConfiguration configuration,
        WorldCollisionProxyConfiguration? actorProxyConfiguration,
        IReadOnlyList<WorldCollisionProxy> collisionProxies,
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
            now.ToUnixTimeMilliseconds(),
            0d,
            0d,
            0d,
            configuration.SpawnPositionY,
            true,
            0,
            null);
        var intent = await intents.Get(configuration.WorldId, configuration.AvatarEntityId, cancellationToken);

        if (configuration.MovementSpeed is not > 0d ||
            configuration.GravityAcceleration is not < 0d ||
            configuration.GroundStickVelocity is not <= 0d ||
            configuration.MaximumStepHeight is not >= 0d ||
            configuration.GroundClearance is not >= 0d)
        {
            var disabled = fallback with
            {
                VelocityX = 0d,
                VelocityY = 0d,
                VelocityZ = 0d,
                MotionStatus = "missing_motion_semantics",
                UpdatedAtUnixMilliseconds =
                    now.ToUnixTimeMilliseconds(),
                ServerTick = fallback.ServerTick + 1
            };
            var disabledPublished =
                await motion.Set(disabled, cancellationToken);
            return disabledPublished &&
                   (existing is null ||
                    HasPresentationChange(fallback, disabled))
                ? disabled
                : null;
        }

        if (actorProxyConfiguration is null ||
            !WorldCollisionProxyPolicy.TryCreate(
                actorProxyConfiguration,
                out var actorProxyTemplate,
                out _))
        {
            var disabled = fallback with
            {
                VelocityX = 0d,
                VelocityY = 0d,
                VelocityZ = 0d,
                MotionStatus = "missing_collision_proxy",
                UpdatedAtUnixMilliseconds =
                    now.ToUnixTimeMilliseconds(),
                ServerTick = fallback.ServerTick + 1
            };
            var proxyFailurePublished =
                await motion.Set(disabled, cancellationToken);
            return proxyFailurePublished &&
                   (existing is null ||
                    HasPresentationChange(fallback, disabled))
                ? disabled
                : null;
        }

        if (intent is not null &&
            (!string.Equals(
                 intent.ZoneKey,
                 configuration.ZoneKey,
                 StringComparison.Ordinal) ||
             intent.RuntimeSessionId != fallback.RuntimeSessionId))
        {
            intent = null;
        }
        var runtimeAction = await runtimeActions.Get(
            configuration.WorldId,
            configuration.AvatarEntityId,
            cancellationToken);
        var jumpRequested =
            runtimeAction is not null &&
            runtimeAction.RuntimeSessionId == fallback.RuntimeSessionId &&
            runtimeAction.OccurrenceId !=
            fallback.LastProcessedRuntimeActionOccurrenceId &&
            !string.IsNullOrWhiteSpace(
                configuration.JumpActionId) &&
            string.Equals(
                runtimeAction.ActionId,
                configuration.JumpActionId,
                StringComparison.Ordinal) &&
            configuration.JumpTakeoffSpeed is > 0d;
        var next = WorldPlayerMotionPolicy.ResolveAuthoritativeStep(
            fallback,
            intent,
            configuration,
            actorProxyConfiguration,
            actorProxyTemplate!,
            collisionProxies,
            zone,
            jumpRequested
                ? runtimeAction!.OccurrenceId
                : null,
            LoopInterval.TotalSeconds,
            now.ToUnixTimeMilliseconds());
        var published = await motion.Set(next, cancellationToken);
        return published &&
               (existing is null ||
                HasPresentationChange(fallback, next))
            ? next
            : null;
    }

    private static bool HasPresentationChange(
        WorldPlayerMotionState previous,
        WorldPlayerMotionState next)
    {
        return Math.Abs(previous.PositionX - next.PositionX) >
                   0.000001d ||
               Math.Abs(previous.PositionY - next.PositionY) >
                   0.000001d ||
               Math.Abs(previous.PositionZ - next.PositionZ) >
                   0.000001d ||
               !string.Equals(
                   previous.MotionStatus,
                   next.MotionStatus,
                   StringComparison.Ordinal);
    }

    private sealed record AvatarCacheEntry(
        IReadOnlyList<WorldPlayerAvatarMotionConfiguration> Configurations,
        DateTimeOffset ExpiresAt);
    private sealed record ProxyCacheEntry(
        IReadOnlyList<WorldCollisionProxyConfiguration> Configurations,
        DateTimeOffset ExpiresAt);
}

internal static class WorldPlayerMotionPolicy
{
    public static bool IsInsideZone(
        double positionX,
        double positionZ,
        double minimumX,
        double minimumZ,
        double maximumX,
        double maximumZ)
    {
        return double.IsFinite(positionX) &&
               double.IsFinite(positionZ) &&
               positionX >= minimumX &&
               positionX <= maximumX &&
               positionZ >= minimumZ &&
               positionZ <= maximumZ;
    }

    /// <summary>
    /// The client reports requested locomotion speed as transient input. The
    /// authored movement_speed Fact remains the authority-owned upper bound.
    /// </summary>
    public static double ResolveAcceptedSpeed(float requestedSpeed, double authoredMaximumSpeed)
    {
        if (!float.IsFinite(requestedSpeed) || !double.IsFinite(authoredMaximumSpeed))
            return 0d;
        return Math.Min(
            Math.Max(0d, requestedSpeed),
            Math.Max(0d, authoredMaximumSpeed));
    }

    public static WorldPlayerMotionState ResolveAuthoritativeStep(
        WorldPlayerMotionState current,
        WorldPlayerIntent? intent,
        WorldPlayerAvatarMotionConfiguration configuration,
        WorldCollisionProxyConfiguration actorProxyConfiguration,
        WorldCollisionProxy actorProxyTemplate,
        IReadOnlyList<WorldCollisionProxy> collisionProxies,
        WorldZoneScheduleDefinition zone,
        Guid? acceptedRuntimeActionOccurrenceId,
        double deltaSeconds,
        long nowUnixMilliseconds)
    {
        var delta = double.IsFinite(deltaSeconds)
            ? Math.Clamp(deltaSeconds, 0d, 0.1d)
            : 0d;
        var maximumStepHeight =
            configuration.MaximumStepHeight ?? 0d;
        var groundClearance =
            configuration.GroundClearance ?? 0d;
        var groundReferenceY = current.GroundReferenceY;
        var groundSupportEntityId =
            current.GroundSupportEntityId;
        var grounded = current.Grounded;
        var velocityY = current.VelocityY;
        var positionY = current.PositionY;
        WorldCollisionProxy? supportAtStart = null;
        if (grounded &&
            WorldCollisionProxyPolicy.TryResolveHighestWalkableSupport(
                actorProxyTemplate,
                actorProxyConfiguration,
                current.PositionX,
                current.PositionZ,
                current.PositionY - maximumStepHeight,
                current.PositionY + maximumStepHeight,
                groundClearance,
                collisionProxies,
                out supportAtStart,
                out var initialSupportY))
        {
            positionY = initialSupportY;
            groundReferenceY = initialSupportY;
            groundSupportEntityId =
                supportAtStart!.EntityId;
        }
        else if (grounded)
        {
            // Durable checkpoint height is not implicit ground. Removing the
            // authored WalkableSupport therefore removes grounding.
            grounded = false;
            groundReferenceY = 0d;
            groundSupportEntityId = null;
        }

        var consumedOccurrence =
            current.LastProcessedRuntimeActionOccurrenceId;
        if (acceptedRuntimeActionOccurrenceId.HasValue)
        {
            consumedOccurrence =
                acceptedRuntimeActionOccurrenceId.Value;
            if (grounded &&
                configuration.JumpTakeoffSpeed is > 0d)
            {
                velocityY =
                    configuration.JumpTakeoffSpeed.Value;
                grounded = false;
            }
        }

        if (grounded && velocityY <= 0d)
        {
            velocityY =
                configuration.GroundStickVelocity ?? 0d;
        }
        else
        {
            velocityY +=
                (configuration.GravityAcceleration ?? 0d) *
                delta;
            positionY += velocityY * delta;
        }

        var directionX = intent?.MoveX ?? 0d;
        var directionZ = intent?.MoveZ ?? 0d;
        var destinationRemaining = double.PositiveInfinity;
        if (intent?.HasDestination == true)
        {
            directionX = intent.DestinationX - current.PositionX;
            directionZ = intent.DestinationZ - current.PositionZ;
            destinationRemaining = Math.Max(
                0d,
                Math.Sqrt(
                    directionX * directionX +
                    directionZ * directionZ) -
                intent.DestinationStopDistance);
        }
        var directionLength = Math.Sqrt(
            directionX * directionX +
            directionZ * directionZ);
        if (directionLength > 1d)
        {
            directionX /= directionLength;
            directionZ /= directionLength;
            directionLength = 1d;
        }
        var acceptedSpeed =
            intent is null || configuration.MovementSpeed is not > 0d
                ? 0d
                : ResolveAcceptedSpeed(
                    intent.MoveSpeed,
                    configuration.MovementSpeed.Value);
        if (directionLength <= 0.0001d ||
            acceptedSpeed <= 0d)
        {
            directionX = 0d;
            directionZ = 0d;
        }
        else
        {
            directionX /= directionLength;
            directionZ /= directionLength;
        }

        var positionX = current.PositionX;
        var positionZ = current.PositionZ;
        var requestedDistance = acceptedSpeed * delta;
        if (double.IsFinite(destinationRemaining))
        {
            requestedDistance = Math.Min(
                requestedDistance,
                destinationRemaining);
        }
        ResolvePlanarMovement(
            ref positionX,
            positionY,
            ref positionZ,
            directionX * requestedDistance,
            directionZ * requestedDistance,
            actorProxyConfiguration,
            actorProxyTemplate,
            collisionProxies,
            zone);

        if (grounded)
        {
            if (WorldCollisionProxyPolicy
                    .TryResolveHighestWalkableSupport(
                        actorProxyTemplate,
                        actorProxyConfiguration,
                        positionX,
                        positionZ,
                        positionY - maximumStepHeight,
                        positionY + maximumStepHeight,
                        groundClearance,
                        collisionProxies,
                        out var resolvedSupport,
                        out var resolvedSupportY))
            {
                positionY = resolvedSupportY;
                groundReferenceY = resolvedSupportY;
                groundSupportEntityId =
                    resolvedSupport!.EntityId;
            }
            else if (WorldCollisionProxyPolicy
                         .TryResolveHighestWalkableSupport(
                             actorProxyTemplate,
                             actorProxyConfiguration,
                             positionX,
                             positionZ,
                             positionY + maximumStepHeight,
                             double.MaxValue,
                             groundClearance,
                             collisionProxies,
                             out _,
                             out _))
            {
                // A WalkableSupport top above the authored step limit behaves
                // as a side wall. Retain the previous supported position.
                positionX = current.PositionX;
                positionZ = current.PositionZ;
                if (supportAtStart != null)
                {
                    positionY = groundReferenceY;
                }
            }
            else
            {
                // Walking beyond the authored support begins a real fall.
                grounded = false;
                groundReferenceY = 0d;
                groundSupportEntityId = null;
                velocityY =
                    (configuration.GravityAcceleration ?? 0d) *
                    delta;
                positionY =
                    current.PositionY + velocityY * delta;
            }
        }
        else if (velocityY <= 0d &&
                 WorldCollisionProxyPolicy
                     .TryResolveHighestWalkableSupport(
                         actorProxyTemplate,
                         actorProxyConfiguration,
                         positionX,
                         positionZ,
                         positionY,
                         current.PositionY,
                         groundClearance,
                         collisionProxies,
                         out var landingSupport,
                         out var landingY))
        {
            positionY = landingY;
            groundReferenceY = landingY;
            groundSupportEntityId =
                landingSupport!.EntityId;
            velocityY =
                configuration.GroundStickVelocity ?? 0d;
            grounded = true;
        }
        var velocityX = delta > 0d
            ? (positionX - current.PositionX) / delta
            : 0d;
        var velocityZ = delta > 0d
            ? (positionZ - current.PositionZ) / delta
            : 0d;
        var hasPlanarVelocity =
            Math.Abs(velocityX) > 0.0001d ||
            Math.Abs(velocityZ) > 0.0001d;
        var motionStatus = !grounded
            ? "airborne"
            : hasPlanarVelocity
                ? "moving"
                : "idle";

        return current with
        {
            PositionX = positionX,
            PositionY = positionY,
            PositionZ = positionZ,
            VelocityX = velocityX,
            VelocityY = velocityY,
            VelocityZ = velocityZ,
            GroundReferenceY = groundReferenceY,
            Grounded = grounded,
            GroundSupportEntityId = groundSupportEntityId,
            LastProcessedIntentSequence =
                intent is null
                    ? current.LastProcessedIntentSequence
                    : Math.Max(
                        current.LastProcessedIntentSequence,
                        intent.Sequence),
            LastProcessedRuntimeActionOccurrenceId =
                consumedOccurrence,
            MotionStatus = motionStatus,
            UpdatedAtUnixMilliseconds = nowUnixMilliseconds,
            ServerTick = current.ServerTick + 1
        };
    }

    private static void ResolvePlanarMovement(
        ref double positionX,
        double positionY,
        ref double positionZ,
        double displacementX,
        double displacementZ,
        WorldCollisionProxyConfiguration actorConfiguration,
        WorldCollisionProxy actorProxyTemplate,
        IReadOnlyList<WorldCollisionProxy> collisionProxies,
        WorldZoneScheduleDefinition zone)
    {
        var distance = Math.Sqrt(
            displacementX * displacementX +
            displacementZ * displacementZ);
        var substeps = Math.Max(
            1,
            (int)Math.Ceiling(distance / 0.2d));
        var stepX = displacementX / substeps;
        var stepZ = displacementZ / substeps;
        for (var index = 0; index < substeps; index++)
        {
            if (TryResolveCandidate(
                    positionX + stepX,
                    positionY,
                    positionZ + stepZ,
                    actorConfiguration,
                    actorProxyTemplate,
                    collisionProxies,
                    zone,
                    out var resolvedX,
                    out var resolvedZ))
            {
                positionX = resolvedX;
                positionZ = resolvedZ;
                continue;
            }

            if (TryResolveCandidate(
                    positionX + stepX,
                    positionY,
                    positionZ,
                    actorConfiguration,
                    actorProxyTemplate,
                    collisionProxies,
                    zone,
                    out resolvedX,
                    out resolvedZ))
            {
                positionX = resolvedX;
                positionZ = resolvedZ;
            }
            if (TryResolveCandidate(
                    positionX,
                    positionY,
                    positionZ + stepZ,
                    actorConfiguration,
                    actorProxyTemplate,
                    collisionProxies,
                    zone,
                    out resolvedX,
                    out resolvedZ))
            {
                positionX = resolvedX;
                positionZ = resolvedZ;
            }
        }
    }

    private static bool TryResolveCandidate(
        double desiredPositionX,
        double positionY,
        double desiredPositionZ,
        WorldCollisionProxyConfiguration actorConfiguration,
        WorldCollisionProxy actorProxyTemplate,
        IReadOnlyList<WorldCollisionProxy> collisionProxies,
        WorldZoneScheduleDefinition zone,
        out double resolvedPositionX,
        out double resolvedPositionZ)
    {
        if (!WorldCollisionProxyPolicy
                .TryClampEntityPositionInsideZone(
                    actorProxyTemplate,
                    actorConfiguration,
                    desiredPositionX,
                    desiredPositionZ,
                    zone.MinX,
                    zone.MinZ,
                    zone.MaxX,
                    zone.MaxZ,
                    out resolvedPositionX,
                    out resolvedPositionZ))
        {
            return false;
        }

        var actor = WorldCollisionProxyPolicy.PlaceAtEntityPosition(
            actorProxyTemplate,
            actorConfiguration,
            resolvedPositionX,
            positionY,
            resolvedPositionZ);
        foreach (var obstacle in collisionProxies)
        {
            if (WorldCollisionProxyPolicy.BlocksActorMotion(
                    actor,
                    obstacle))
            {
                return false;
            }
        }
        return true;
    }
}
