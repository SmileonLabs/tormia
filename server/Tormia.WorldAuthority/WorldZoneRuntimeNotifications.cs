using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

/// <summary>
/// Publishes a small, best-effort Zone runtime hint after the authoritative
/// motion worker has changed a visible avatar state. The hint intentionally
/// contains no position or ontology data: connected clients read the latest
/// ephemeral snapshot through the authenticated HTTP endpoint.
/// </summary>
internal interface IWorldZoneRuntimeNotificationPublisher
{
    Task PublishChanged(Guid worldId, string zoneKey, long observedAtUnixMilliseconds, CancellationToken cancellationToken);
    Task PublishMotionFrame(
        Guid worldId,
        string zoneKey,
        IReadOnlyList<WorldPlayerMotionState> states,
        long authorityFrameTick,
        long observedAtUnixMilliseconds,
        CancellationToken cancellationToken);
}

internal sealed record WorldZoneMotionFrame(
    Guid FrameOccurrenceId,
    Guid WorldId,
    string ZoneKey,
    long ServerTick,
    long ObservedAtUnixMilliseconds,
    IReadOnlyList<WorldPlayerMotionState> Items);

internal static class WorldZoneMotionFramePolicy
{
    public static WorldZoneMotionFrame? Create(
        Guid worldId,
        string zoneKey,
        IReadOnlyList<WorldPlayerMotionState>? states,
        Guid frameOccurrenceId,
        long authorityFrameTick,
        long observedAtUnixMilliseconds)
    {
        if (worldId == Guid.Empty ||
            frameOccurrenceId == Guid.Empty ||
            !SemanticId.IsValid(zoneKey) ||
            authorityFrameTick <= 0 ||
            states is null)
        {
            return null;
        }

        var items = states
            .Where(state =>
                state.WorldId == worldId &&
                string.Equals(
                    state.ZoneKey,
                    zoneKey,
                    StringComparison.Ordinal))
            .GroupBy(state => state.AvatarEntityId)
            .Select(group => group
                .OrderByDescending(state => state.ServerTick)
                .ThenByDescending(
                    state => state.UpdatedAtUnixMilliseconds)
                .First())
            .OrderBy(state => state.AvatarEntityId)
            .ToArray();
        if (items.Length == 0)
        {
            return null;
        }

        return new WorldZoneMotionFrame(
            frameOccurrenceId,
            worldId,
            zoneKey,
            authorityFrameTick,
            observedAtUnixMilliseconds,
            items);
    }
}

internal interface IWorldRevisionNotificationPublisher
{
    Task PublishCommitted(
        Guid worldId,
        string zoneKey,
        long revision,
        Guid? eventId,
        string commandType,
        Guid? targetEntityId,
        bool damageResult,
        CancellationToken cancellationToken);
}

internal sealed class SignalRWorldRevisionNotificationPublisher(
    IHubContext<WorldZoneHub> hub,
    ILogger<SignalRWorldRevisionNotificationPublisher> logger) :
    IWorldRevisionNotificationPublisher
{
    public async Task PublishCommitted(
        Guid worldId,
        string zoneKey,
        long revision,
        Guid? eventId,
        string commandType,
        Guid? targetEntityId,
        bool damageResult,
        CancellationToken cancellationToken)
    {
        if (worldId == Guid.Empty ||
            revision <= 0 ||
            !SemanticId.IsValid(zoneKey))
        {
            return;
        }

        try
        {
            var notification = new WorldRevisionNotification(
                worldId,
                revision,
                eventId,
                commandType,
                zoneKey,
                targetEntityId,
                damageResult);
            await hub.Clients
                .Groups(
                    WorldZoneHub.GetNotificationGroups(
                        worldId,
                        zoneKey))
                .SendAsync(
                    WorldZoneHub.RevisionEventName,
                    notification,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not publish committed revision {Revision} for " +
                "{WorldId}/{ZoneKey}.",
                revision,
                worldId,
                zoneKey);
        }
    }
}

internal sealed class SignalRWorldZoneRuntimeNotificationPublisher(
    IHubContext<WorldZoneHub> hub,
    RealtimeTransportMetrics realtimeMetrics,
    ILogger<SignalRWorldZoneRuntimeNotificationPublisher> logger)
{
    private const string MotionFrameEventName = "zoneMotionFrame";

    public async Task PublishChanged(
        Guid worldId,
        string zoneKey,
        long observedAtUnixMilliseconds,
        CancellationToken cancellationToken)
    {
        if (worldId == Guid.Empty || !SemanticId.IsValid(zoneKey)) return;

        try
        {
            var notification = new WorldZoneRuntimeNotification(worldId, zoneKey, observedAtUnixMilliseconds);
            await hub.Clients
                .Group(WorldZoneHub.GetGroupName(worldId, zoneKey))
                .SendAsync(WorldZoneHub.RuntimeChangedEventName, notification, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal service shutdown; the next connected client reads a snapshot.
        }
        catch (Exception exception)
        {
            // A lost socket/backplane must never stop authoritative simulation.
            logger.LogWarning(exception, "Could not publish runtime change for {WorldId}/{ZoneKey}.", worldId, zoneKey);
        }
    }

    public async Task PublishMotionFrame(
        WorldZoneMotionFrame frame,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await hub.Clients
                .Group(WorldZoneHub.GetGroupName(
                    frame.WorldId,
                    frame.ZoneKey))
                .SendAsync(
                    MotionFrameEventName,
                    frame,
                    cancellationToken);
            realtimeMetrics.RecordMotionFramePublish(
                frame.Items.Count,
                Stopwatch.GetElapsedTime(startedAt),
                "success");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal service shutdown. Clients retain HTTP recovery.
            realtimeMetrics.RecordMotionFramePublish(
                frame.Items.Count,
                Stopwatch.GetElapsedTime(startedAt),
                "cancelled");
        }
        catch (Exception exception)
        {
            // Realtime presentation transport cannot stop Authority motion.
            realtimeMetrics.RecordMotionFramePublish(
                frame.Items.Count,
                Stopwatch.GetElapsedTime(startedAt),
                "failure");
            logger.LogWarning(
                exception,
                "Could not publish motion frame for {WorldId}/{ZoneKey}.",
                frame.WorldId,
                frame.ZoneKey);
        }
    }
}
