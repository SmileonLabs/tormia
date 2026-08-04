using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Ephemeral Authority-owned presentation state for any entity whose authored
/// ontology contract selects autonomous kinematic behavior. The entity can be
/// backed by any mesh or prefab; identity and behavior come from its Facts,
/// assigned Rule Block, and Physical Meaning.
/// </summary>
internal sealed record WorldAutonomousActorMotionState(
    Guid WorldId,
    Guid ActorEntityId,
    string ZoneKey,
    double PositionX,
    double PositionY,
    double PositionZ,
    double ForwardX,
    double ForwardZ,
    string MotionStatus,
    Guid? TargetEntityId,
    string ActorAnimationIntent,
    long PresentationSequence,
    long UpdatedAtUnixMilliseconds,
    Guid? AttackOccurrenceId = null,
    string AttackPhase = "idle",
    long AttackContactAtUnixMilliseconds = 0,
    long AttackRecoveryEndsAtUnixMilliseconds = 0,
    bool AttackContactResolved = false);

internal interface IWorldAutonomousActorRuntimeRegistry
{
    string BackendName { get; }
    Task<WorldAutonomousActorMotionState?> Get(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<WorldAutonomousActorMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> actorEntityIds,
        CancellationToken cancellationToken);
    Task Set(
        WorldAutonomousActorMotionState state,
        CancellationToken cancellationToken);
    Task Remove(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken);
}

internal sealed class RedisWorldAutonomousActorRuntimeRegistry(
    IConnectionMultiplexer redis) : IWorldAutonomousActorRuntimeRegistry
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";

    public async Task<WorldAutonomousActorMotionState?> Get(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, actorEntityId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<WorldAutonomousActorMotionState>(
                value!,
                JsonOptions);
    }

    public async Task<IReadOnlyList<WorldAutonomousActorMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> actorEntityIds,
        CancellationToken cancellationToken)
    {
        if (actorEntityIds.Count == 0)
            return Array.Empty<WorldAutonomousActorMotionState>();
        var keys = actorEntityIds
            .Select(id => (RedisKey)Key(worldId, id))
            .ToArray();
        var values = await database.StringGetAsync(keys);
        var result =
            new List<WorldAutonomousActorMotionState>(values.Length);
        foreach (var value in values)
        {
            if (value.IsNullOrEmpty) continue;
            var state =
                JsonSerializer.Deserialize<WorldAutonomousActorMotionState>(
                    value!,
                    JsonOptions);
            if (state is not null) result.Add(state);
        }
        return result;
    }

    public async Task Set(
        WorldAutonomousActorMotionState state,
        CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(state.WorldId, state.ActorEntityId),
            JsonSerializer.Serialize(state, JsonOptions),
            StateTtl);
    }

    public async Task Remove(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken)
    {
        await database.KeyDeleteAsync(Key(worldId, actorEntityId));
    }

    private static string Key(Guid worldId, Guid actorEntityId) =>
        "tormia:world:" + worldId.ToString("N") +
        ":autonomous-actor:" + actorEntityId.ToString("N") + ":motion";
}

internal sealed class InMemoryWorldAutonomousActorRuntimeRegistry :
    IWorldAutonomousActorRuntimeRegistry
{
    private readonly ConcurrentDictionary<
        string,
        WorldAutonomousActorMotionState> states = new();

    public string BackendName => "in_memory_development";

    public Task<WorldAutonomousActorMotionState?> Get(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken)
    {
        states.TryGetValue(Key(worldId, actorEntityId), out var state);
        return Task.FromResult<WorldAutonomousActorMotionState?>(state);
    }

    public Task<IReadOnlyList<WorldAutonomousActorMotionState>> GetMany(
        Guid worldId,
        IReadOnlyList<Guid> actorEntityIds,
        CancellationToken cancellationToken)
    {
        var result =
            new List<WorldAutonomousActorMotionState>(actorEntityIds.Count);
        foreach (var actorEntityId in actorEntityIds)
        {
            if (states.TryGetValue(
                    Key(worldId, actorEntityId),
                    out var state))
            {
                result.Add(state);
            }
        }
        return Task.FromResult<
            IReadOnlyList<WorldAutonomousActorMotionState>>(result);
    }

    public Task Set(
        WorldAutonomousActorMotionState state,
        CancellationToken cancellationToken)
    {
        states[Key(state.WorldId, state.ActorEntityId)] = state;
        return Task.CompletedTask;
    }

    public Task Remove(
        Guid worldId,
        Guid actorEntityId,
        CancellationToken cancellationToken)
    {
        states.TryRemove(Key(worldId, actorEntityId), out _);
        return Task.CompletedTask;
    }

    private static string Key(Guid worldId, Guid actorEntityId) =>
        worldId.ToString("N") + ":" + actorEntityId.ToString("N");
}

/// <summary>
/// Pure spatial policy used by the Authority scheduler. It consumes only
/// already-resolved ontology configuration and ephemeral positions.
/// </summary>
internal static class WorldAutonomousActorPolicy
{
    public static double DistanceSquared(
        double leftX,
        double leftZ,
        double rightX,
        double rightZ)
    {
        var x = leftX - rightX;
        var z = leftZ - rightZ;
        return x * x + z * z;
    }

    public static (double X, double Z) AdvanceTowards(
        double fromX,
        double fromZ,
        double toX,
        double toZ,
        double maximumDistance)
    {
        var x = toX - fromX;
        var z = toZ - fromZ;
        var length = Math.Sqrt(x * x + z * z);
        if (length <= 0.000001d || maximumDistance <= 0d)
            return (fromX, fromZ);
        var accepted = Math.Min(length, maximumDistance);
        return (
            fromX + x / length * accepted,
            fromZ + z / length * accepted);
    }
}

/// <summary>
/// Server-owned autonomous actor loop. A reusable Rule Block is the behavior
/// switch, authored numeric Facts are its tuning, and AuthorityKinematic is
/// the Physical Meaning. Removing any required part makes the repository stop
/// returning that entity to this scheduler.
/// </summary>
internal sealed partial class WorldAutonomousActorSimulationScheduler(
    WorldAuthorityRepository repository,
    IWorldPlayerMotionRuntimeRegistry playerMotion,
    IWorldPlayerPoseObservationRegistry playerPoseObservations,
    IWorldAutonomousActorRuntimeRegistry actorMotion,
    IWorldZoneSessionRegistry sessions,
    IWorldZoneExecutionLeaseRegistry executionLeases,
    IWorldZoneRuntimeNotificationPublisher runtimeNotifications,
    IWorldRevisionNotificationPublisher revisionNotifications,
    ILogger<WorldAutonomousActorSimulationScheduler> logger) :
    BackgroundService
{
    private static readonly TimeSpan LoopInterval =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LeaseDuration =
        TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ZoneRefreshInterval =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfigurationRefreshInterval =
        TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleRefreshInterval =
        TimeSpan.FromSeconds(1);

    private readonly string ownerId =
        Environment.MachineName + ":autonomous-actors:" +
        Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, ActorCacheEntry> actorCache =
        new();
    private IReadOnlyList<WorldZoneScheduleDefinition> zones =
        Array.Empty<WorldZoneScheduleDefinition>();
    private DateTimeOffset nextZoneRefreshAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextZoneRefreshAt)
                {
                    zones = await repository.GetZoneScheduleDefinitions(
                        stoppingToken);
                    nextZoneRefreshAt = now + ZoneRefreshInterval;
                }

                foreach (var zone in zones)
                {
                    if (zone.SimulationMode == "dormant") continue;
                    if (!await executionLeases.TryAcquire(
                            zone.WorldId,
                            zone.ZoneKey,
                            "autonomous-actors",
                            ownerId,
                            LeaseDuration,
                            stoppingToken))
                    {
                        continue;
                    }

                    var activeUserIds = await sessions.GetActiveUserIds(
                        zone.WorldId,
                        zone.ZoneKey,
                        stoppingToken);
                    if (activeUserIds.Count == 0) continue;

                    var configurations = await GetConfigurations(
                        zone,
                        now,
                        stoppingToken);
                    if (configurations.Count == 0) continue;

                    var targets =
                        await repository.GetAutonomousTargetConfigurations(
                            zone.WorldId,
                            zone.ZoneKey,
                            stoppingToken);
                    targets = targets
                        .Where(value =>
                            !value.UserId.HasValue ||
                            activeUserIds.Contains(value.UserId.Value))
                        .ToArray();
                    var playerTargetIds = targets
                        .Where(value => value.UserId.HasValue)
                        .Select(value => value.EntityId)
                        .ToArray();
                    var playerTargetStates = (await playerMotion.GetMany(
                            zone.WorldId,
                            playerTargetIds,
                            stoppingToken))
                        .ToDictionary(
                            value => value.AvatarEntityId,
                            value => value);
                    var autonomousTargetStates =
                        (await actorMotion.GetMany(
                            zone.WorldId,
                            targets.Select(value => value.EntityId)
                                .ToArray(),
                            stoppingToken))
                        .ToDictionary(
                            value => value.ActorEntityId,
                            value => value);
                    var activeEntityIds = targets
                        .Select(value => value.EntityId)
                        .ToHashSet();

                    var changed = false;
                    foreach (var configuration in configurations)
                    {
                        // The target projection is queried every simulation
                        // tick and is the current lifecycle authority. A
                        // cached actor configuration must never resurrect an
                        // actor that has already lost is_alive eligibility.
                        if (!activeEntityIds.Contains(
                                configuration.ActorEntityId))
                        {
                            await actorMotion.Remove(
                                zone.WorldId,
                                configuration.ActorEntityId,
                                stoppingToken);
                            changed = true;
                            continue;
                        }
                        changed |= await AdvanceActor(
                            configuration,
                            targets,
                            playerTargetStates,
                            autonomousTargetStates,
                            zone,
                            now,
                            stoppingToken);
                    }

                    if (changed)
                    {
                        await runtimeNotifications.PublishChanged(
                            zone.WorldId,
                            zone.ZoneKey,
                            now.ToUnixTimeMilliseconds(),
                            stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Autonomous actor scheduler iteration failed.");
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

    private async Task<IReadOnlyList<WorldAutonomousActorConfiguration>>
        GetConfigurations(
            WorldZoneScheduleDefinition zone,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var key = zone.WorldId.ToString("N") + ":" + zone.ZoneKey;
        if (actorCache.TryGetValue(key, out var cached) &&
            cached.ExpiresAt > now)
        {
            return cached.Configurations;
        }

        var configurations =
            await repository.GetAutonomousActorConfigurations(
                zone.WorldId,
                zone.ZoneKey,
                cancellationToken);
        if (cached is not null)
        {
            var removedActorIds =
                WorldAutonomousActorLifecyclePolicy.FindRemovedActorIds(
                    cached.Configurations.Select(value =>
                        value.ActorEntityId),
                    configurations.Select(value =>
                        value.ActorEntityId));
            foreach (var removedActorId in removedActorIds)
            {
                await actorMotion.Remove(
                    zone.WorldId,
                    removedActorId,
                    cancellationToken);
            }
            if (removedActorIds.Count > 0)
            {
                // Runtime notifications are invalidation hints only. Publishing
                // after eviction makes every client fetch a snapshot that no
                // longer contains the dead/deconfigured actor, even when the
                // refreshed configuration list is empty.
                await runtimeNotifications.PublishChanged(
                    zone.WorldId,
                    zone.ZoneKey,
                    now.ToUnixTimeMilliseconds(),
                    cancellationToken);
            }
        }
        actorCache[key] = new ActorCacheEntry(
            configurations,
            now + ConfigurationRefreshInterval);
        return configurations;
    }

    private async Task<bool> AdvanceActor(
        WorldAutonomousActorConfiguration configuration,
        IReadOnlyList<WorldAutonomousTargetConfiguration> targets,
        IReadOnlyDictionary<Guid, WorldPlayerMotionState>
            playerTargetStates,
        IReadOnlyDictionary<Guid, WorldAutonomousActorMotionState>
            autonomousTargetStates,
        WorldZoneScheduleDefinition zone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await actorMotion.Get(
            configuration.WorldId,
            configuration.ActorEntityId,
            cancellationToken);
        var state = existing ?? new WorldAutonomousActorMotionState(
            configuration.WorldId,
            configuration.ActorEntityId,
            configuration.ZoneKey,
            configuration.SpawnPositionX,
            configuration.SpawnPositionY,
            configuration.SpawnPositionZ,
            0d,
            1d,
            "idle",
            null,
            configuration.IdleAnimationIntent,
            1,
            now.ToUnixTimeMilliseconds());
        var previousAt =
            DateTimeOffset.FromUnixTimeMilliseconds(
                state.UpdatedAtUnixMilliseconds);
        var seconds = Math.Clamp(
            (now - previousAt).TotalSeconds,
            0d,
            LoopInterval.TotalSeconds * 1.5d);

        WorldAutonomousTargetConfiguration? selected = null;
        WorldAutonomousTargetPosition? selectedMotion = null;
        var nearestDistance = double.PositiveInfinity;
        foreach (var target in targets)
        {
            if (!WorldAutonomousTargetPositionPolicy.TryResolve(
                    target,
                    playerTargetStates,
                    autonomousTargetStates,
                    out var motion) ||
                !string.Equals(
                    motion.ZoneKey,
                    configuration.ZoneKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var fromSpawn =
                WorldAutonomousActorPolicy.DistanceSquared(
                    configuration.SpawnPositionX,
                    configuration.SpawnPositionZ,
                    motion.PositionX,
                    motion.PositionZ);
            if (fromSpawn >
                configuration.LeashRange * configuration.LeashRange)
            {
                continue;
            }

            var distance =
                WorldAutonomousActorPolicy.DistanceSquared(
                    state.PositionX,
                    state.PositionZ,
                    motion.PositionX,
                    motion.PositionZ);
            if (distance >
                    configuration.DetectionRange *
                    configuration.DetectionRange ||
                distance >= nearestDistance)
            {
                continue;
            }

            var targetPreview =
                await repository.PreviewAutonomousAction(
                    configuration.WorldId,
                    new ExecuteActionPayload(
                        configuration.ActorEntityId,
                        target.EntityId,
                        null,
                        configuration.PackageId,
                        configuration.PackageVersion,
                        configuration.TargetActionId,
                        configuration.TargetActionDefinitionVersion),
                    cancellationToken);
            if (!targetPreview.Accepted)
            {
                continue;
            }

            selected = target;
            selectedMotion = motion;
            nearestDistance = distance;
        }

        var next = state;
        if (state.AttackOccurrenceId.HasValue)
        {
            next = await AdvanceAttackOccurrence(
                configuration,
                state,
                targets,
                playerTargetStates,
                autonomousTargetStates,
                now,
                cancellationToken);
        }
        else if (selected is null || selectedMotion is null)
        {
            next = MoveOrIdle(
                configuration,
                state,
                configuration.SpawnPositionX,
                configuration.SpawnPositionZ,
                seconds,
                zone,
                null,
                now);
        }
        else if (!WorldAutonomousMeleeContactPolicy.HasContact(
                     state.PositionX,
                     state.PositionY,
                     state.PositionZ,
                     configuration.CollisionRadius,
                     configuration.CollisionHeight,
                     selectedMotion.PositionX,
                     selectedMotion.PositionY,
                     selectedMotion.PositionZ,
                     selected.CollisionRadius,
                     selected.CollisionHeight,
                     configuration.AttackContactReach))
        {
            var chasePreview =
                await repository.PreviewAutonomousAction(
                    configuration.WorldId,
                    new ExecuteActionPayload(
                        configuration.ActorEntityId,
                        selected.EntityId,
                        null,
                        configuration.PackageId,
                        configuration.PackageVersion,
                        configuration.ChaseActionId,
                        configuration.ChaseActionDefinitionVersion),
                    cancellationToken);
            next = MoveOrIdle(
                configuration,
                state,
                chasePreview.Accepted
                    ? selectedMotion.PositionX
                    : state.PositionX,
                chasePreview.Accepted
                    ? selectedMotion.PositionZ
                    : state.PositionZ,
                seconds,
                zone,
                selected.EntityId,
                now);
        }
        else
        {
            var request = new ExecuteActionPayload(
                configuration.ActorEntityId,
                selected.EntityId,
                null,
                configuration.PackageId,
                configuration.PackageVersion,
                configuration.AttackActionId,
                configuration.ActionDefinitionVersion);
            var preview = await repository.PreviewAutonomousAction(
                configuration.WorldId,
                request,
                cancellationToken);
            var intent = preview.Accepted &&
                         !string.IsNullOrWhiteSpace(
                             preview.ActorAnimationIntent)
                ? preview.ActorAnimationIntent!
                : configuration.IdleAnimationIntent;
            var presentationSequence = preview.Accepted
                ? state.PresentationSequence + 1
                : state.PresentationSequence;
            next = state with
            {
                MotionStatus = preview.Accepted ? "attacking" : "idle",
                TargetEntityId = selected.EntityId,
                ActorAnimationIntent = intent,
                PresentationSequence = presentationSequence,
                AttackOccurrenceId = preview.Accepted
                    ? Guid.NewGuid()
                    : null,
                AttackPhase = preview.Accepted ? "windup" : "idle",
                AttackContactAtUnixMilliseconds = preview.Accepted
                    ? now.AddSeconds(configuration.AttackWindupSeconds)
                        .ToUnixTimeMilliseconds()
                    : 0,
                AttackRecoveryEndsAtUnixMilliseconds = preview.Accepted
                    ? now.AddSeconds(
                            configuration.AttackWindupSeconds +
                            configuration.AttackRecoverySeconds)
                        .ToUnixTimeMilliseconds()
                    : 0,
                AttackContactResolved = false,
                UpdatedAtUnixMilliseconds =
                    now.ToUnixTimeMilliseconds()
            };
        }

        var materiallyChanged =
            existing is null ||
            !SamePresentationState(state, next);
        var needsRefresh =
            now - previousAt >= IdleRefreshInterval;
        if (materiallyChanged || needsRefresh)
        {
            await actorMotion.Set(next, cancellationToken);
        }
        return materiallyChanged;
    }

    private async Task<WorldAutonomousActorMotionState>
        AdvanceAttackOccurrence(
            WorldAutonomousActorConfiguration configuration,
            WorldAutonomousActorMotionState state,
            IReadOnlyList<WorldAutonomousTargetConfiguration> targets,
            IReadOnlyDictionary<Guid, WorldPlayerMotionState>
                playerTargetStates,
            IReadOnlyDictionary<Guid, WorldAutonomousActorMotionState>
                autonomousTargetStates,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        var nowMilliseconds = now.ToUnixTimeMilliseconds();
        if (WorldAutonomousAttackOccurrencePolicy.ShouldResolveContact(
                state,
                nowMilliseconds))
        {
            var target = state.TargetEntityId.HasValue
                ? targets.FirstOrDefault(value =>
                    value.EntityId == state.TargetEntityId.Value)
                : null;
            var hasCurrentContact =
                target is not null &&
                WorldAutonomousTargetPositionPolicy.TryResolve(
                    target,
                    playerTargetStates,
                    autonomousTargetStates,
                    out var targetPosition) &&
                WorldAutonomousMeleeContactPolicy.HasContact(
                    state.PositionX,
                    state.PositionY,
                    state.PositionZ,
                    configuration.CollisionRadius,
                    configuration.CollisionHeight,
                    targetPosition.PositionX,
                    targetPosition.PositionY,
                    targetPosition.PositionZ,
                    target.CollisionRadius,
                    target.CollisionHeight,
                    configuration.AttackContactReach);
            if (hasCurrentContact && target!.UserId.HasValue)
            {
                var observation = await playerPoseObservations.Get(
                    configuration.WorldId,
                    target.EntityId,
                    cancellationToken);
                hasCurrentContact =
                    WorldAutonomousPlayerContactObservationPolicy
                        .HasFreshContact(
                            observation,
                            configuration.WorldId,
                            target.EntityId,
                            configuration.ZoneKey,
                            state.PositionX,
                            state.PositionY,
                            state.PositionZ,
                            configuration.CollisionRadius,
                            configuration.CollisionHeight,
                            target.CollisionRadius,
                            target.CollisionHeight,
                            configuration.AttackContactReach,
                            nowMilliseconds);
            }
            if (!hasCurrentContact)
            {
                return CancelAttackOccurrence(configuration, state, nowMilliseconds);
            }

            var preview = await repository.PreviewAutonomousAction(
                configuration.WorldId,
                new ExecuteActionPayload(
                    configuration.ActorEntityId,
                    state.TargetEntityId!.Value,
                    null,
                    configuration.PackageId,
                    configuration.PackageVersion,
                    configuration.AttackActionId,
                    configuration.ActionDefinitionVersion),
                cancellationToken);
            if (!preview.Accepted)
            {
                return CancelAttackOccurrence(configuration, state, nowMilliseconds);
            }

            var action = await repository.ExecuteAutonomousAction(
                    configuration.WorldId,
                    new ExecuteActionPayload(
                        configuration.ActorEntityId,
                        state.TargetEntityId.Value,
                        null,
                        configuration.PackageId,
                        configuration.PackageVersion,
                        configuration.AttackActionId,
                        configuration.ActionDefinitionVersion),
                    cancellationToken);
            if (action.Accepted && action.Revision.HasValue)
            {
                await revisionNotifications.PublishCommitted(
                    configuration.WorldId,
                    configuration.ZoneKey,
                    action.Revision.Value,
                    action.EventId,
                    "autonomous_attack_contact",
                    state.TargetEntityId,
                    action.DamageResult,
                    cancellationToken);
                if (action.TargetDefeated && state.TargetEntityId.HasValue)
                {
                    await actorMotion.Remove(
                        configuration.WorldId,
                        state.TargetEntityId.Value,
                        cancellationToken);
                }
            }
            return state with
            {
                MotionStatus = "attacking",
                AttackPhase = "recovery",
                AttackContactResolved = true,
                UpdatedAtUnixMilliseconds = nowMilliseconds
            };
        }

        if (WorldAutonomousAttackOccurrencePolicy.ShouldCompleteRecovery(
                state,
                nowMilliseconds))
        {
            return state with
            {
                MotionStatus = "idle",
                TargetEntityId = null,
                ActorAnimationIntent = configuration.IdleAnimationIntent,
                AttackOccurrenceId = null,
                AttackPhase = "idle",
                AttackContactAtUnixMilliseconds = 0,
                AttackRecoveryEndsAtUnixMilliseconds = 0,
                AttackContactResolved = false,
                UpdatedAtUnixMilliseconds = nowMilliseconds
            };
        }

        return state;
    }

    private static WorldAutonomousActorMotionState CancelAttackOccurrence(
        WorldAutonomousActorConfiguration configuration,
        WorldAutonomousActorMotionState state,
        long nowMilliseconds) => state with
    {
        MotionStatus = "idle",
        TargetEntityId = null,
        ActorAnimationIntent = configuration.IdleAnimationIntent,
        AttackOccurrenceId = null,
        AttackPhase = "idle",
        AttackContactAtUnixMilliseconds = 0,
        AttackRecoveryEndsAtUnixMilliseconds = 0,
        AttackContactResolved = false,
        UpdatedAtUnixMilliseconds = nowMilliseconds
    };

}

/// <summary>
/// Defines the Authority-owned temporal boundaries of one autonomous attack.
/// The Rule Block still owns eligibility and damage; this policy only decides
/// when the already evaluated occurrence reaches contact and recovery.
/// </summary>
internal static class WorldAutonomousAttackOccurrencePolicy
{
    public static bool ShouldResolveContact(
        WorldAutonomousActorMotionState state,
        long nowUnixMilliseconds) =>
        state.AttackOccurrenceId.HasValue &&
        !state.AttackContactResolved &&
        nowUnixMilliseconds >= state.AttackContactAtUnixMilliseconds;

    public static bool ShouldCompleteRecovery(
        WorldAutonomousActorMotionState state,
        long nowUnixMilliseconds) =>
        state.AttackOccurrenceId.HasValue &&
        state.AttackContactResolved &&
        nowUnixMilliseconds >= state.AttackRecoveryEndsAtUnixMilliseconds;
}

/// <summary>
/// Resolves melee contact from authored collision capsules. Attack range
/// remains an action constraint; it is not a substitute for physical contact.
/// Invalid or missing geometry fails closed in the repository projection.
/// </summary>
internal static class WorldAutonomousMeleeContactPolicy
{
    public static bool HasContact(
        double actorX,
        double actorY,
        double actorZ,
        double actorRadius,
        double actorHeight,
        double targetX,
        double targetY,
        double targetZ,
        double targetRadius,
        double targetHeight,
        double contactReach)
    {
        if (!double.IsFinite(actorX) || !double.IsFinite(actorY) ||
            !double.IsFinite(actorZ) || !double.IsFinite(targetX) ||
            !double.IsFinite(targetY) || !double.IsFinite(targetZ) ||
            !double.IsFinite(actorRadius) || actorRadius <= 0d ||
            !double.IsFinite(actorHeight) || actorHeight <= 0d ||
            !double.IsFinite(targetRadius) || targetRadius <= 0d ||
            !double.IsFinite(targetHeight) || targetHeight <= 0d ||
            !double.IsFinite(contactReach) || contactReach < 0d)
        {
            return false;
        }

        var verticalOverlap =
            actorY <= targetY + targetHeight &&
            targetY <= actorY + actorHeight;
        if (!verticalOverlap) return false;

        var allowed = actorRadius + targetRadius + contactReach;
        return WorldAutonomousActorPolicy.DistanceSquared(
                   actorX,
                   actorZ,
                   targetX,
                   targetZ) <= allowed * allowed;
    }
}

/// <summary>
/// A player target must corroborate Authority contact with a recent
/// collision-resolved presentation observation. The observation never moves
/// Authority state; it can only fail a damage occurrence closed when the
/// client presentation has already escaped contact or stopped reporting.
/// </summary>
internal static class WorldAutonomousPlayerContactObservationPolicy
{
    private const long MaximumObservationAgeMilliseconds = 1500;

    public static bool HasFreshContact(
        WorldPlayerPoseObservation? observation,
        Guid worldId,
        Guid avatarEntityId,
        string zoneKey,
        double actorX,
        double actorY,
        double actorZ,
        double actorRadius,
        double actorHeight,
        double targetRadius,
        double targetHeight,
        double contactReach,
        long nowUnixMilliseconds)
    {
        if (observation is null ||
            observation.WorldId != worldId ||
            observation.AvatarEntityId != avatarEntityId ||
            !string.Equals(
                observation.ZoneKey,
                zoneKey,
                StringComparison.Ordinal) ||
            observation.IntentSequence <= 0 ||
            observation.PoseSequence <= 0 ||
            observation.ObservedAtUnixMilliseconds >
                nowUnixMilliseconds + 250 ||
            nowUnixMilliseconds - observation.ObservedAtUnixMilliseconds >
                MaximumObservationAgeMilliseconds)
        {
            return false;
        }

        return WorldAutonomousMeleeContactPolicy.HasContact(
            actorX,
            actorY,
            actorZ,
            actorRadius,
            actorHeight,
            observation.PositionX,
            observation.PositionY,
            observation.PositionZ,
            targetRadius,
            targetHeight,
            contactReach);
    }
}

/// <summary>
/// Computes the ephemeral runtime records that must be evicted when the
/// authoritative ontology projection no longer exposes an actor as eligible.
/// Death, Rule-Block removal, or Physical-Meaning removal all use this same
/// data-driven transition.
/// </summary>
internal static class WorldAutonomousActorLifecyclePolicy
{
    public static IReadOnlyList<Guid> FindRemovedActorIds(
        IEnumerable<Guid> previousActorIds,
        IEnumerable<Guid> currentActorIds)
    {
        var current = currentActorIds.ToHashSet();
        return previousActorIds
            .Where(actorId => !current.Contains(actorId))
            .Distinct()
            .ToArray();
    }
}

/// <summary>
/// Resolves the position owner selected by ontology data. Entities whose
/// Physical Meaning owns an ephemeral runtime position must never fall back to
/// a durable spawn/checkpoint during live targeting. Durable transforms remain
/// valid only for entities without a runtime position owner.
/// </summary>
internal static class WorldAutonomousTargetPositionPolicy
{
    public static bool TryResolve(
        WorldAutonomousTargetConfiguration target,
        IReadOnlyDictionary<Guid, WorldPlayerMotionState>
            playerStates,
        IReadOnlyDictionary<Guid, WorldAutonomousActorMotionState>
            autonomousStates,
        out WorldAutonomousTargetPosition position)
    {
        if (playerStates.TryGetValue(target.EntityId, out var player))
        {
            position = new WorldAutonomousTargetPosition(
                player.ZoneKey,
                player.PositionX,
                player.PositionY,
                player.PositionZ);
            return true;
        }
        if (autonomousStates.TryGetValue(
                target.EntityId,
                out var autonomous))
        {
            position = new WorldAutonomousTargetPosition(
                autonomous.ZoneKey,
                autonomous.PositionX,
                autonomous.PositionY,
                autonomous.PositionZ);
            return true;
        }
        if (target.RequiresRuntimePosition)
        {
            position = default!;
            return false;
        }
        position = new WorldAutonomousTargetPosition(
            target.ZoneKey,
            target.SpawnPositionX,
            target.SpawnPositionY,
            target.SpawnPositionZ);
        return !string.IsNullOrWhiteSpace(target.ZoneKey);
    }

}

internal sealed partial class WorldAutonomousActorSimulationScheduler
{
    private static WorldAutonomousActorMotionState MoveOrIdle(
        WorldAutonomousActorConfiguration configuration,
        WorldAutonomousActorMotionState state,
        double targetX,
        double targetZ,
        double seconds,
        WorldZoneScheduleDefinition zone,
        Guid? targetEntityId,
        DateTimeOffset now)
    {
        var destination = WorldAutonomousActorPolicy.AdvanceTowards(
            state.PositionX,
            state.PositionZ,
            targetX,
            targetZ,
            configuration.MovementSpeed * seconds);
        var x = Math.Clamp(destination.X, zone.MinX, zone.MaxX);
        var z = Math.Clamp(destination.Z, zone.MinZ, zone.MaxZ);
        var moved =
            WorldAutonomousActorPolicy.DistanceSquared(
                state.PositionX,
                state.PositionZ,
                x,
                z) > 0.000001d;
        var forwardX = moved ? x - state.PositionX : state.ForwardX;
        var forwardZ = moved ? z - state.PositionZ : state.ForwardZ;
        var length = Math.Sqrt(
            forwardX * forwardX + forwardZ * forwardZ);
        if (length > 0.000001d)
        {
            forwardX /= length;
            forwardZ /= length;
        }
        var status = moved ? "moving" : "idle";
        var intent = moved
            ? configuration.MoveAnimationIntent
            : configuration.IdleAnimationIntent;
        var sequence =
            !string.Equals(
                state.ActorAnimationIntent,
                intent,
                StringComparison.Ordinal)
                ? state.PresentationSequence + 1
                : state.PresentationSequence;
        return state with
        {
            PositionX = x,
            PositionY = configuration.SpawnPositionY,
            PositionZ = z,
            ForwardX = forwardX,
            ForwardZ = forwardZ,
            MotionStatus = status,
            TargetEntityId = targetEntityId,
            ActorAnimationIntent = intent,
            PresentationSequence = sequence,
            UpdatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds()
        };
    }

    private static bool SamePresentationState(
        WorldAutonomousActorMotionState left,
        WorldAutonomousActorMotionState right)
    {
        return Math.Abs(left.PositionX - right.PositionX) <= 0.0001d &&
               Math.Abs(left.PositionY - right.PositionY) <= 0.0001d &&
               Math.Abs(left.PositionZ - right.PositionZ) <= 0.0001d &&
               string.Equals(
                   left.MotionStatus,
                   right.MotionStatus,
                   StringComparison.Ordinal) &&
               left.TargetEntityId == right.TargetEntityId &&
               left.PresentationSequence == right.PresentationSequence &&
               left.AttackOccurrenceId == right.AttackOccurrenceId &&
               string.Equals(
                   left.AttackPhase,
                   right.AttackPhase,
                   StringComparison.Ordinal) &&
               left.AttackContactAtUnixMilliseconds ==
                   right.AttackContactAtUnixMilliseconds &&
               left.AttackRecoveryEndsAtUnixMilliseconds ==
                   right.AttackRecoveryEndsAtUnixMilliseconds &&
               left.AttackContactResolved == right.AttackContactResolved;
    }

    private sealed record ActorCacheEntry(
        IReadOnlyList<WorldAutonomousActorConfiguration> Configurations,
        DateTimeOffset ExpiresAt);
}

internal sealed record WorldAutonomousTargetPosition(
    string ZoneKey,
    double PositionX,
    double PositionY,
    double PositionZ);
