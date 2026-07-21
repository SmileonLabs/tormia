using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Publishes a small, best-effort Zone runtime hint after the authoritative
/// motion worker has changed a visible avatar state. The hint intentionally
/// contains no position or ontology data: connected clients read the latest
/// ephemeral snapshot through the authenticated HTTP endpoint.
/// </summary>
internal interface IWorldZoneRuntimeNotificationPublisher
{
    Task PublishChanged(Guid worldId, string zoneKey, long observedAtUnixMilliseconds, CancellationToken cancellationToken);
}

internal sealed class SignalRWorldZoneRuntimeNotificationPublisher(
    IHubContext<WorldZoneHub> hub,
    ILogger<SignalRWorldZoneRuntimeNotificationPublisher> logger) : IWorldZoneRuntimeNotificationPublisher
{
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
}
