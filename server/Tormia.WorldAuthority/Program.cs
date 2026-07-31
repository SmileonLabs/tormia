using System.Data;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;
using Tormia.Ontology.Core;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException("ConnectionStrings:Platform is required.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<WorldAuthorityRepository>();
builder.Services.AddSingleton<PasswordAuthRepository>();
builder.Services.AddSingleton<ContentCatalogRepository>();
builder.Services.AddSingleton<HeadlessZoneOntologyEvaluator>();
builder.Services.AddHealthChecks();

// PostgreSQL remains the durable source of truth. Redis only fans an already
// committed revision notification out to the authority instances holding sockets.
var realtimeRedis = builder.Configuration.GetConnectionString("RealtimeRedis");
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(realtimeRedis))
{
    signalR.AddStackExchangeRedis(realtimeRedis);
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(realtimeRedis));
    builder.Services.AddSingleton<IWorldZoneSessionRegistry, RedisWorldZoneSessionRegistry>();
    builder.Services.AddSingleton<IWorldZoneRuntimeRegistry, RedisWorldZoneRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldZoneExecutionLeaseRegistry, RedisWorldZoneExecutionLeaseRegistry>();
    builder.Services.AddSingleton<IWorldPlayerIntentRegistry, RedisWorldPlayerIntentRegistry>();
    builder.Services.AddSingleton<
        IWorldPlayerRuntimeActionIntentRegistry,
        RedisWorldPlayerRuntimeActionIntentRegistry>();
    builder.Services.AddSingleton<
        IWorldPlayerPoseObservationRegistry,
        RedisWorldPlayerPoseObservationRegistry>();
    builder.Services.AddSingleton<IWorldPlayerMotionRuntimeRegistry, RedisWorldPlayerMotionRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldAutonomousActorRuntimeRegistry, RedisWorldAutonomousActorRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldActionCooldownRuntimeRegistry, RedisWorldActionCooldownRuntimeRegistry>();
}
else
{
    // Local-only fallback. Production uses Redis so every authority instance sees
    // the same Zone membership and can make the same scheduling decision.
    builder.Services.AddSingleton<IWorldZoneSessionRegistry, InMemoryWorldZoneSessionRegistry>();
    builder.Services.AddSingleton<IWorldZoneRuntimeRegistry, InMemoryWorldZoneRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldZoneExecutionLeaseRegistry, InMemoryWorldZoneExecutionLeaseRegistry>();
    builder.Services.AddSingleton<IWorldPlayerIntentRegistry, InMemoryWorldPlayerIntentRegistry>();
    builder.Services.AddSingleton<
        IWorldPlayerRuntimeActionIntentRegistry,
        InMemoryWorldPlayerRuntimeActionIntentRegistry>();
    builder.Services.AddSingleton<
        IWorldPlayerPoseObservationRegistry,
        InMemoryWorldPlayerPoseObservationRegistry>();
    builder.Services.AddSingleton<IWorldPlayerMotionRuntimeRegistry, InMemoryWorldPlayerMotionRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldAutonomousActorRuntimeRegistry, InMemoryWorldAutonomousActorRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldActionCooldownRuntimeRegistry, InMemoryWorldActionCooldownRuntimeRegistry>();
}
builder.Services.AddHostedService<WorldZoneSimulationScheduler>();
// Runtime notifications are transport hints only. They never author Facts or
// transforms and Unity still reads the current snapshot from the authority API.
builder.Services.AddSingleton<IWorldZoneRuntimeNotificationPublisher, SignalRWorldZoneRuntimeNotificationPublisher>();
builder.Services.AddHostedService<WorldPlayerMotionSimulationScheduler>();
builder.Services.AddHostedService<WorldAutonomousActorSimulationScheduler>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var repository = context.RequestServices.GetRequiredService<PasswordAuthRepository>();
        var userId = await repository.Authenticate(authorization[7..].Trim(), context.RequestAborted);
        if (userId.HasValue) context.Items["Tormia.ActorUserId"] = userId.Value;
    }
    await next();
});

app.MapGet("/health", async (WorldAuthorityRepository repository, CancellationToken cancellationToken) =>
{
    var healthy = await repository.CanConnect(cancellationToken);
    return healthy
        ? Results.Ok(new { status = "healthy" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/v1/auth/register", async (PasswordRegisterRequest request, PasswordAuthRepository repository, CancellationToken cancellationToken) =>
{
    var result = await repository.Register(request.Email ?? string.Empty, request.DisplayName ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
    return result.RejectionCode is null ? Results.Created("/v1/account", new { userId = result.UserId, accessToken = result.AccessToken }) : Results.BadRequest(new { rejectionCode = result.RejectionCode });
});

app.MapPost("/v1/auth/login", async (PasswordLoginRequest request, PasswordAuthRepository repository, CancellationToken cancellationToken) =>
{
    var result = await repository.Login(request.Email ?? string.Empty, request.Password ?? string.Empty, cancellationToken);
    return result.RejectionCode is null ? Results.Ok(new { userId = result.UserId, accessToken = result.AccessToken }) : Results.Unauthorized();
});

app.MapPost("/v1/auth/logout", async (HttpRequest request, PasswordAuthRepository repository, CancellationToken cancellationToken) =>
{
    var authorization = request.Headers.Authorization.ToString();
    if (!TryGetActorUserId(request, out _) ||
        !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
    await repository.Revoke(authorization[7..].Trim(), cancellationToken);
    return Results.NoContent();
});

app.MapGet("/health/realtime", () => Results.Ok(new
{
    status = "healthy",
    transport = "signalr",
    backplane = string.IsNullOrWhiteSpace(realtimeRedis) ? "in_memory_development" : "redis"
}));

app.MapGet("/health/sessions", (IWorldZoneSessionRegistry sessions) => Results.Ok(new
{
    status = "healthy",
    backend = sessions.BackendName,
    leaseSeconds = sessions.LeaseDuration.TotalSeconds
}));

app.MapGet("/health/scheduler", (IWorldZoneRuntimeRegistry runtime) => Results.Ok(new
{
    status = "healthy",
    backend = runtime.BackendName,
    engine = "headless_inference_only"
}));

app.MapGet("/health/intents", (IWorldPlayerIntentRegistry intents) => Results.Ok(new
{
    status = "healthy",
    backend = intents.BackendName,
    leaseSeconds = intents.LeaseDuration.TotalSeconds
}));

app.MapGet("/health/motion", (IWorldPlayerMotionRuntimeRegistry motion) => Results.Ok(new
{
    status = "healthy",
    backend = motion.BackendName,
    engine = "fixed_tick_authored_proxy_kinematic"
}));

app.MapGet("/health/autonomous-actors", (
    IWorldAutonomousActorRuntimeRegistry motion) => Results.Ok(new
{
    status = "healthy",
    backend = motion.BackendName,
    engine = "rule_block_gated_authority_kinematic"
}));

app.MapPost("/v1/content/packages/{packageId}/rules", async (
    string packageId,
    HttpRequest httpRequest,
    ContentRuleCatalogPublishRequest request,
    ContentCatalogRepository catalog,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    var result = await catalog.PublishRules(packageId, actorUserId, request, cancellationToken);
    if (result.Accepted)
    {
        return Results.Ok(result);
    }

    return result.RejectionCode switch
    {
        "content_package_forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
        "content_package_not_found" => Results.NotFound(result),
        _ => Results.BadRequest(result)
    };
});

app.MapGet("/v1/content/packages/{packageId}/rules", async (
    string packageId,
    HttpRequest httpRequest,
    ContentCatalogRepository catalog,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    var result = await catalog.GetPublishedRules(packageId, actorUserId, cancellationToken);
    return result.RejectionCode switch
    {
        null => Results.Ok(result),
        "content_package_not_found" => Results.NotFound(result),
        "content_package_forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(result)
    };
});

app.MapPost("/v1/content/packages/{packageId}/actions", async (
    string packageId,
    HttpRequest httpRequest,
    ContentActionCatalogPublishRequest request,
    ContentCatalogRepository catalog,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    var result = await catalog.PublishActionEffects(packageId, actorUserId, request, cancellationToken);
    if (result.Accepted) return Results.Ok(result);
    return result.RejectionCode switch
    {
        "content_package_forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(result)
    };
});

app.MapGet("/v1/content/packages/{packageId}/actions", async (
    string packageId,
    HttpRequest httpRequest,
    ContentCatalogRepository catalog,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    var result = await catalog.GetPublishedActionEffects(packageId, actorUserId, cancellationToken);
    return result.RejectionCode is null ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/v1/account", async (
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    var account = await repository.GetAccountDashboard(actorUserId, cancellationToken);
    return account is null ? Results.NotFound() : Results.Ok(account);
});

app.MapPost("/v1/account/characters", async (
    HttpRequest httpRequest,
    CreatePlayerCharacterRequest request,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (!request.IsValid(out var rejectionCode)) return Results.BadRequest(new { rejectionCode });
    var character = await repository.CreatePlayerCharacter(actorUserId, request, cancellationToken);
    return character is null
        ? Results.NotFound(new { rejectionCode = "account_not_found" })
        : Results.Created($"/v1/account/characters/{character.CharacterId}", character);
});

// Account-profile writes are deliberately not world commands: they have their
// own revision and idempotency key and never change a shared world revision.
app.MapPut("/v1/account/characters/{characterId:guid}", async (
    Guid characterId,
    HttpRequest httpRequest,
    UpdatePlayerCharacterProfileRequest request,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (!request.IsValid(out var rejectionCode)) return Results.BadRequest(new { rejectionCode });
    var result = await repository.UpdatePlayerCharacterProfile(characterId, actorUserId, request, cancellationToken);
    return result.RejectionCode switch
    {
        null => Results.Ok(result),
        "character_not_found" => Results.NotFound(result),
        "profile_revision_conflict" => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapPost("/v1/worlds/{worldId:guid}/entry", async (
    Guid worldId,
    HttpRequest httpRequest,
    EnterWorldRequest request,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (request.CharacterId == Guid.Empty || request.AvatarEntityId == Guid.Empty)
    {
        return Results.BadRequest(new { rejectionCode = "invalid_world_entry" });
    }

    var result = await repository.EnterWorld(worldId, actorUserId, request, cancellationToken);
    return result.RejectionCode switch
    {
        null => Results.Ok(result),
        "world_not_found" or "character_not_found" or "avatar_not_registered" => Results.NotFound(result),
        _ => Results.BadRequest(result)
    };
});

// A world-avatar profile is a durable, revisioned overlay for this avatar in
// this world only (role, team, progression). It must never rewrite the account
// character profile, and it is not an ephemeral observation.
app.MapGet("/v1/worlds/{worldId:guid}/avatars/{avatarEntityId:guid}/profile", async (
    Guid worldId,
    Guid avatarEntityId,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    var result = await repository.GetWorldAvatarProfile(worldId, actorUserId, avatarEntityId, cancellationToken);
    return result.RejectionCode switch
    {
        null => Results.Ok(result),
        "avatar_not_registered" => Results.NotFound(result),
        _ => Results.StatusCode(StatusCodes.Status403Forbidden)
    };
});

app.MapGet("/v1/worlds/{worldId:guid}/avatars/{avatarEntityId:guid}/checkpoint", async (
    Guid worldId,
    Guid avatarEntityId,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    var result = await repository.GetAvatarCheckpoint(
        worldId, actorUserId, avatarEntityId, cancellationToken);
    return result.RejectionCode switch
    {
        null => Results.Ok(result),
        "checkpoint_not_found" or "avatar_not_registered" => Results.NotFound(result),
        _ => Results.StatusCode(StatusCodes.Status403Forbidden)
    };
});

app.MapPost("/v1/worlds", async (
    HttpRequest httpRequest,
    CreateWorldRequest request,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest(new { rejectionCode = "missing_world_identity" });
    }

    var result = await repository.CreateWorld(actorUserId, request, cancellationToken);
    return result is null
        ? Results.StatusCode(StatusCodes.Status403Forbidden)
        : Results.Created($"/v1/worlds/{result.WorldId}", result);
});

app.MapGet("/v1/worlds/{worldId:guid}", async (
    Guid worldId,
    string? zoneKey,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    if (!string.IsNullOrWhiteSpace(zoneKey) && !SemanticId.IsValid(zoneKey))
    {
        return Results.BadRequest(new { rejectionCode = "invalid_zone_key" });
    }

    var world = await repository.GetWorldProjection(
        worldId,
        actorUserId,
        string.IsNullOrWhiteSpace(zoneKey) ? null : zoneKey.Trim(),
        cancellationToken);
    return world is null ? Results.NotFound() : Results.Ok(world);
});

app.MapGet("/v1/worlds/{worldId:guid}/zones", async (
    Guid worldId,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    var zones = await repository.GetWorldZones(worldId, actorUserId, cancellationToken);
    return zones is null ? Results.NotFound() : Results.Ok(zones);
});

app.MapGet("/v1/worlds/{worldId:guid}/zones/{zoneKey}/session", async (
    Guid worldId,
    string zoneKey,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    IWorldZoneSessionRegistry sessions,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    if (!SemanticId.IsValid(zoneKey) ||
        !await repository.ZoneExists(worldId, actorUserId, zoneKey, cancellationToken))
    {
        return Results.NotFound();
    }

    var zone = await repository.GetWorldZone(worldId, actorUserId, zoneKey, cancellationToken);
    if (zone is null) return Results.NotFound();
    var session = await sessions.GetSummary(worldId, zoneKey, cancellationToken);
    return Results.Ok(new ZoneSessionProjection(
        zone.ZoneKey,
        zone.SimulationMode,
        WorldZoneRuntimePolicy.ResolveRunState(zone.SimulationMode, session.ConnectionCount),
        session.ConnectionCount,
        session.UserCount,
        session.ObservedAtUnixMilliseconds));
});

app.MapGet("/v1/worlds/{worldId:guid}/zones/{zoneKey}/runtime", async (
    Guid worldId,
    string zoneKey,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    IWorldZoneRuntimeRegistry runtime,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    if (!SemanticId.IsValid(zoneKey) ||
        !await repository.ZoneExists(worldId, actorUserId, zoneKey, cancellationToken))
    {
        return Results.NotFound();
    }

    var snapshot = await runtime.Get(worldId, zoneKey, cancellationToken);
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

// HTTP runtime clients hold the same short-lived Zone presence lease as
// SignalR clients. The socket remains a notification optimization; simulation
// presence must not disappear merely because WebSocket negotiation failed.
app.MapPost(
    "/v1/worlds/{worldId:guid}/runtime/zones/{zoneKey}/sessions/{sessionId:guid}",
    async (
        Guid worldId,
        string zoneKey,
        Guid sessionId,
        HttpRequest httpRequest,
        WorldAuthorityRepository repository,
        IWorldZoneSessionRegistry sessions,
        IWorldZoneRuntimeNotificationPublisher runtimeNotifications,
        CancellationToken cancellationToken) =>
    {
        if (!TryGetActorUserId(httpRequest, out var actorUserId))
            return Results.Unauthorized();
        if (sessionId == Guid.Empty ||
            !SemanticId.IsValid(zoneKey) ||
            !await repository.ZoneExists(
                worldId,
                actorUserId,
                zoneKey,
                cancellationToken))
        {
            return Results.NotFound(
                new { rejectionCode = "runtime_zone_session_not_found" });
        }

        await sessions.Refresh(
            worldId,
            zoneKey,
            actorUserId,
            HttpRuntimeSessionConnectionId(sessionId),
            cancellationToken);
        await runtimeNotifications.PublishChanged(
            worldId,
            zoneKey,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken);
        return Results.Ok(new
        {
            accepted = true,
            leaseSeconds = Math.Max(
                1,
                (int)sessions.LeaseDuration.TotalSeconds)
        });
    });

app.MapDelete(
    "/v1/worlds/{worldId:guid}/runtime/zones/{zoneKey}/sessions/{sessionId:guid}",
    async (
        Guid worldId,
        string zoneKey,
        Guid sessionId,
        HttpRequest httpRequest,
        WorldAuthorityRepository repository,
        IWorldZoneSessionRegistry sessions,
        IWorldZoneRuntimeNotificationPublisher runtimeNotifications,
        CancellationToken cancellationToken) =>
    {
        if (!TryGetActorUserId(httpRequest, out var actorUserId))
            return Results.Unauthorized();
        if (sessionId == Guid.Empty ||
            !SemanticId.IsValid(zoneKey) ||
            !await repository.ZoneExists(
                worldId,
                actorUserId,
                zoneKey,
                cancellationToken))
        {
            return Results.NotFound(
                new { rejectionCode = "runtime_zone_session_not_found" });
        }

        await sessions.Unregister(
            worldId,
            zoneKey,
            actorUserId,
            HttpRuntimeSessionConnectionId(sessionId),
            cancellationToken);
        await runtimeNotifications.PublishChanged(
            worldId,
            zoneKey,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken);
        return Results.Ok(new { accepted = true });
    });

app.MapPost("/v1/worlds/{worldId:guid}/runtime/intents", async (
    Guid worldId,
    HttpRequest httpRequest,
    PlayerIntentRequest request,
    WorldAuthorityRepository repository,
    IWorldPlayerIntentRegistry intents,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (!request.IsValid || !SemanticId.IsValid(request.ZoneKey))
    {
        return Results.BadRequest(new { rejectionCode = "invalid_player_intent" });
    }
    if (!await repository.AvatarBelongsToUser(worldId, actorUserId, request.AvatarEntityId, cancellationToken))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
    if (!await repository.ZoneExists(worldId, actorUserId, request.ZoneKey, cancellationToken))
    {
        return Results.NotFound(new { rejectionCode = "zone_not_found" });
    }

    var locomotionPreview = await repository.PreviewAction(
        worldId,
        actorUserId,
        new ExecuteActionPayload(
            request.AvatarEntityId,
            request.AvatarEntityId,
            null,
            request.PackageId,
            request.PackageVersion,
            request.ActionId,
            request.DefinitionVersion),
        cancellationToken);
    if (!locomotionPreview.Accepted)
    {
        return Results.Conflict(new
        {
            rejectionCode =
                locomotionPreview.RejectionCode ??
                "locomotion_rule_rejected"
        });
    }
    if (locomotionPreview.MutationCount != 0)
    {
        return Results.Conflict(new
        {
            rejectionCode =
                "locomotion_rule_must_be_ephemeral"
        });
    }
    var intent = new WorldPlayerIntent(
        worldId, actorUserId, request.AvatarEntityId, request.ZoneKey,
        request.Sequence, request.MoveX, request.MoveZ, request.MoveSpeed,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    var accepted = await intents.Submit(intent, cancellationToken);
    return accepted
        ? Results.Accepted($"/v1/worlds/{worldId}/runtime/intents/{request.AvatarEntityId}", new { accepted = true })
        : Results.Conflict(new { rejectionCode = "stale_player_intent" });
});

// Unity reports its newest collision-resolved presentation pose as an
// observation only. Server motion remains authoritative: this endpoint never
// copies the client Transform into WorldPlayerMotionState, never advances the
// world revision, and never creates a Fact/event.
app.MapPost(
    "/v1/worlds/{worldId:guid}/runtime/avatars/{avatarEntityId:guid}/resolved-pose",
    async (
        Guid worldId,
        Guid avatarEntityId,
        HttpRequest httpRequest,
        ResolvedPlayerPoseRequest request,
        WorldAuthorityRepository repository,
        IWorldPlayerIntentRegistry intents,
        IWorldPlayerMotionRuntimeRegistry motion,
        IWorldPlayerPoseObservationRegistry observations,
        CancellationToken cancellationToken) =>
    {
        if (!TryGetActorUserId(httpRequest, out var actorUserId))
            return Results.Unauthorized();
        if (avatarEntityId == Guid.Empty ||
            !request.IsValid ||
            !SemanticId.IsValid(request.ZoneKey))
        {
            return Results.BadRequest(
                new { rejectionCode = "invalid_resolved_player_pose" });
        }
        if (!await repository.AvatarBelongsToUser(
                worldId,
                actorUserId,
                avatarEntityId,
                cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var zone = await repository.GetWorldZone(
            worldId,
            actorUserId,
            request.ZoneKey,
            cancellationToken);
        if (zone is null)
        {
            return Results.NotFound(
                new { rejectionCode = "zone_not_found" });
        }
        if (!WorldPlayerMotionPolicy.IsInsideZone(
                request.PositionX,
                request.PositionZ,
                zone.MinX,
                zone.MinZ,
                zone.MaxX,
                zone.MaxZ))
        {
            return Results.Conflict(
                new { rejectionCode = "resolved_pose_outside_zone" });
        }

        var latestIntent = await intents.Get(
            worldId,
            avatarEntityId,
            cancellationToken);
        if (latestIntent is null ||
            !string.Equals(
                latestIntent.ZoneKey,
                request.ZoneKey,
                StringComparison.Ordinal) ||
            request.IntentSequence >
            latestIntent.Sequence)
        {
            return Results.Conflict(
                new { rejectionCode = "resolved_pose_intent_not_active" });
        }

        var current = await motion.Get(
            worldId,
            avatarEntityId,
            cancellationToken);
        if (current is null ||
            !string.Equals(
                current.ZoneKey,
                request.ZoneKey,
                StringComparison.Ordinal))
        {
            return Results.Conflict(
                new { rejectionCode = "avatar_runtime_not_activated" });
        }

        var accepted = await observations.SubmitLatest(
            new WorldPlayerPoseObservation(
                worldId,
                avatarEntityId,
                request.ZoneKey,
                request.IntentSequence,
                request.PoseSequence,
                request.PositionX,
                request.PositionY,
                request.PositionZ,
                request.MotionStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
        if (!accepted)
        {
            return Results.Conflict(
                new { rejectionCode = "stale_resolved_player_pose" });
        }

        return Results.Accepted(
            $"/v1/worlds/{worldId}/runtime/avatars/{avatarEntityId}",
            new { accepted = true, authoritativeState = current });
    });

app.MapPost("/v1/worlds/{worldId:guid}/runtime/avatars/{avatarEntityId:guid}/activate", async (
    Guid worldId,
    Guid avatarEntityId,
    HttpRequest httpRequest,
    ActivatePlayerRuntimeRequest request,
    WorldAuthorityRepository repository,
    IWorldPlayerIntentRegistry intents,
    IWorldPlayerRuntimeActionIntentRegistry runtimeActions,
    IWorldPlayerPoseObservationRegistry observations,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (avatarEntityId == Guid.Empty || !SemanticId.IsValid(request.ZoneKey))
        return Results.BadRequest(new { rejectionCode = "invalid_player_runtime_activation" });
    if (!await repository.AvatarBelongsToUser(
            worldId, actorUserId, avatarEntityId, cancellationToken))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await repository.ZoneExists(
            worldId, actorUserId, request.ZoneKey, cancellationToken))
        return Results.NotFound(new { rejectionCode = "zone_not_found" });

    // A new play session has a new client-side sequence. Remove any short-lived
    // sample from the prior session before restoring the durable checkpoint.
    await intents.Clear(worldId, avatarEntityId, cancellationToken);
    await runtimeActions.Clear(
        worldId,
        avatarEntityId,
        cancellationToken);
    await observations.Clear(
        worldId,
        avatarEntityId,
        cancellationToken);
    var state = await repository.ResetPlayerMotionFromDurableState(
        worldId,
        avatarEntityId,
        request.ZoneKey,
        cancellationToken);
    return state is null
        ? Results.NotFound(new { rejectionCode = "avatar_runtime_foundation_not_found" })
        : Results.Ok(new { accepted = true, state });
});

app.MapGet("/v1/worlds/{worldId:guid}/runtime/avatars/{avatarEntityId:guid}", async (
    Guid worldId,
    Guid avatarEntityId,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    IWorldPlayerMotionRuntimeRegistry motion,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (!await repository.CanAccessWorld(worldId, actorUserId, cancellationToken)) return Results.NotFound();
    if (!await repository.OwnsRegisteredAvatar(
            worldId,
            actorUserId,
            avatarEntityId,
            cancellationToken))
    {
        return Results.NotFound();
    }
    var state = await motion.Get(worldId, avatarEntityId, cancellationToken);
    return state is null ? Results.NotFound() : Results.Ok(state);
});

// Runtime-only presence read for the active Zone. This intentionally returns
// only ephemeral motion state: it does not expose player ownership metadata,
// mutate the authored world projection, or create simulation Facts.
app.MapGet("/v1/worlds/{worldId:guid}/runtime/zones/{zoneKey}/avatars", async (
    Guid worldId,
    string zoneKey,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    IWorldZoneSessionRegistry sessions,
    IWorldPlayerMotionRuntimeRegistry motion,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId)) return Results.Unauthorized();
    if (!SemanticId.IsValid(zoneKey) ||
        !await repository.ZoneExists(worldId, actorUserId, zoneKey, cancellationToken))
    {
        return Results.NotFound();
    }

    // A durable avatar registration only establishes ownership. Presence stays
    // ephemeral: only users with a live socket lease in this Zone are visible.
    // This keeps a recently disconnected avatar from lingering until its motion
    // cache entry expires, without exposing the owner identity to clients.
    var activeUserIds = await sessions.GetActiveUserIds(worldId, zoneKey, cancellationToken);
    var registrations = await repository.GetRegisteredPlayerAvatarsForZone(worldId, zoneKey, cancellationToken);
    var avatarIds = registrations
        .Where(registration => activeUserIds.Contains(registration.UserId))
        .Select(registration => registration.AvatarEntityId)
        .ToArray();
    var states = await motion.GetMany(worldId, avatarIds, cancellationToken);
    return Results.Ok(new { items = states });
});

// Ephemeral presentation read for ontology-defined autonomous actors. The
// qualifying entity IDs come from the same Triple + Rule Block + Physical
// Meaning query used by the server scheduler; this endpoint never guesses from
// a monster prefab or writes a durable transform.
app.MapGet("/v1/worlds/{worldId:guid}/runtime/zones/{zoneKey}/actors", async (
    Guid worldId,
    string zoneKey,
    HttpRequest httpRequest,
    WorldAuthorityRepository repository,
    IWorldAutonomousActorRuntimeRegistry motion,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
        return Results.Unauthorized();
    if (!SemanticId.IsValid(zoneKey) ||
        !await repository.ZoneExists(
            worldId,
            actorUserId,
            zoneKey,
            cancellationToken))
    {
        return Results.NotFound();
    }

    var configurations =
        await repository.GetAutonomousActorConfigurations(
            worldId,
            zoneKey,
            cancellationToken);
    var actorIds = configurations
        .Select(value => value.ActorEntityId)
        .ToArray();
    var states = await motion.GetMany(
        worldId,
        actorIds,
        cancellationToken);
    return Results.Ok(new { items = states });
});

// Presentation-only gameplay intents are evaluated against the same immutable
// Action + assigned Rule Block contract as durable actions. They never advance
// the world revision or write an event/Fact, so an empty swing can remain
// ephemeral while Authority still owns permission and cooldown.
app.MapPost("/v1/worlds/{worldId:guid}/runtime/actions", async (
    Guid worldId,
    HttpRequest httpRequest,
    ExecuteActionPayload request,
    WorldAuthorityRepository repository,
    IWorldPlayerRuntimeActionIntentRegistry runtimeActions,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
        return Results.Unauthorized();
    var result = await repository.EvaluateRuntimeAction(
        worldId,
        actorUserId,
        request,
        cancellationToken);
    if (result.Accepted &&
        result.ActorEntityId is Guid actorEntityId &&
        !string.IsNullOrWhiteSpace(result.ActionId))
    {
        await runtimeActions.Submit(
            new WorldPlayerRuntimeActionIntent(
                worldId,
                actorEntityId,
                Guid.NewGuid(),
                result.ActionId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
    }
    return result.RejectionCode switch
    {
        "action_actor_forbidden" =>
            Results.StatusCode(StatusCodes.Status403Forbidden),
        "action_entity_not_found" =>
            Results.NotFound(result),
        _ => Results.Ok(result)
    };
});

// Shared input adapters use this side-effect-free boundary to ask whether the
// currently assigned Rule Block accepts a semantic action. The preview uses
// the exact same immutable action, authored Facts, runtime position, and bound
// Rule Definition as execution, but it never acquires cooldown, advances the
// world revision, or writes an event/Fact.
app.MapPost("/v1/worlds/{worldId:guid}/actions/preview", async (
    Guid worldId,
    HttpRequest httpRequest,
    ExecuteActionPayload request,
    WorldAuthorityRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
        return Results.Unauthorized();
    var result = await repository.PreviewAction(
        worldId,
        actorUserId,
        request,
        cancellationToken);
    return result.RejectionCode switch
    {
        "action_actor_forbidden" =>
            Results.StatusCode(StatusCodes.Status403Forbidden),
        "action_entity_not_found" =>
            Results.NotFound(result),
        _ => Results.Ok(result)
    };
});

app.MapPost("/v1/worlds/{worldId:guid}/commands", async (
    Guid worldId,
    HttpRequest httpRequest,
    WorldCommandRequest request,
    WorldAuthorityRepository repository,
    IHubContext<WorldZoneHub> realtimeHub,
    CancellationToken cancellationToken) =>
{
    if (!TryGetActorUserId(httpRequest, out var actorUserId))
    {
        return Results.Unauthorized();
    }

    if (!request.IsValidEnvelope(out var rejectionCode))
    {
        return Results.BadRequest(new CommandResult(false, rejectionCode, null, null, false));
    }

    var result = await repository.ApplyCommand(worldId, actorUserId, request, cancellationToken);
    if (result.Accepted)
    {
        // Replayed command IDs are intentionally not broadcast again. Clients
        // treat this as a revision hint then reload the authority projection.
        if (!result.IsReplay && result.Revision.HasValue)
        {
            var zoneKey = await repository.ResolveCommandZoneKey(worldId, request, cancellationToken);
            var notification = new WorldRevisionNotification(
                worldId,
                result.Revision.Value,
                result.EventId,
                request.CommandType,
                zoneKey);
            // A zone subscriber receives only its own revision hints. The unassigned
            // group is the deliberate whole-world fallback used by development tools
            // and by clients outside every authored zone.
            await realtimeHub.Clients
                .Groups(WorldZoneHub.GetNotificationGroups(worldId, zoneKey))
                .SendAsync(WorldZoneHub.RevisionEventName, notification, cancellationToken);
        }
        return Results.Ok(result);
    }

    return result.RejectionCode switch
    {
        "world_not_found" => Results.NotFound(result),
        "forbidden" => Results.StatusCode(StatusCodes.Status403Forbidden),
        "stale_revision" => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapHub<WorldZoneHub>("/hubs/world-zone");

app.Run();

static string HttpRuntimeSessionConnectionId(Guid sessionId) =>
    "http:" + sessionId.ToString("N");

static bool TryGetActorUserId(HttpRequest request, out Guid actorUserId)
{
    if (request.HttpContext.Items.TryGetValue("Tormia.ActorUserId", out var value) && value is Guid id) { actorUserId = id; return true; }
    actorUserId = Guid.Empty; return false;
}

internal sealed class WorldAuthorityRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions OntologyJson = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly NpgsqlDataSource dataSource;
    private readonly IWorldPlayerMotionRuntimeRegistry motionRuntime;
    private readonly IWorldAutonomousActorRuntimeRegistry
        autonomousMotionRuntime;
    private readonly IWorldActionCooldownRuntimeRegistry actionCooldownRuntime;

    public WorldAuthorityRepository(
        NpgsqlDataSource dataSource,
        IWorldPlayerMotionRuntimeRegistry motionRuntime,
        IWorldAutonomousActorRuntimeRegistry autonomousMotionRuntime,
        IWorldActionCooldownRuntimeRegistry actionCooldownRuntime)
    {
        this.dataSource = dataSource;
        this.motionRuntime = motionRuntime;
        this.autonomousMotionRuntime = autonomousMotionRuntime;
        this.actionCooldownRuntime = actionCooldownRuntime;
    }

    public async Task<bool> CanConnect(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1;", connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    /// <summary>
    /// Returns account-owned profiles and accessible worlds. It never exposes
    /// another user's profile and deliberately keeps character profiles separate
    /// from shared world entities/Facts.
    /// </summary>
    public async Task<AccountDashboard?> GetAccountDashboard(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string userSql = "SELECT user_id, display_name FROM app_users WHERE user_id = @userId;";
        await using var userCommand = new NpgsqlCommand(userSql, connection);
        userCommand.Parameters.AddWithValue("userId", userId);
        await using var userReader = await userCommand.ExecuteReaderAsync(cancellationToken);
        if (!await userReader.ReadAsync(cancellationToken)) return null;
        var account = new AccountSummary(userReader.GetGuid(0), userReader.GetString(1));
        await userReader.DisposeAsync();

        var characters = await GetPlayerCharacters(connection, userId, cancellationToken);
        const string worldsSql = """
            SELECT w.world_id, w.title, w.slug, m.role, w.current_revision
            FROM world_members m
            INNER JOIN worlds w ON w.world_id = m.world_id
            WHERE m.user_id = @userId
            ORDER BY w.updated_at DESC, w.world_id;
            """;
        await using var worldsCommand = new NpgsqlCommand(worldsSql, connection);
        worldsCommand.Parameters.AddWithValue("userId", userId);
        await using var worldsReader = await worldsCommand.ExecuteReaderAsync(cancellationToken);
        var worlds = new List<AccountWorldSummary>();
        while (await worldsReader.ReadAsync(cancellationToken))
        {
            worlds.Add(new AccountWorldSummary(
                worldsReader.GetGuid(0),
                worldsReader.GetString(1),
                worldsReader.GetString(2),
                worldsReader.GetString(3),
                worldsReader.GetInt64(4)));
        }
        return new AccountDashboard(account, characters, worlds);
    }

    public async Task<PlayerCharacterSummary?> CreatePlayerCharacter(
        Guid userId,
        CreatePlayerCharacterRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await UserExists(connection, null, userId, cancellationToken)) return null;

        const string sql = """
            INSERT INTO player_characters (user_id, display_name, template_id, equipped_part_ids)
            VALUES (@userId, @displayName, @templateId, @equippedPartIds::jsonb)
            RETURNING character_id, display_name, template_id, equipped_part_ids::text,
                      profile_revision, profile_relations::text;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("displayName", request.DisplayName.Trim());
        command.Parameters.AddWithValue("templateId", request.TemplateId.Trim());
        command.Parameters.AddWithValue(
            "equippedPartIds",
            JsonSerializer.Serialize((IReadOnlyList<string>)(request.EquippedPartIds ?? new List<string>()), JsonOptions));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadPlayerCharacter(reader);
    }

    private static async Task EnsureDefaultPlayerCharacter(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string displayName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO player_characters (user_id, display_name, template_id, equipped_part_ids, is_default)
            VALUES (@userId, @displayName, 'player_default', '[]'::jsonb, true)
            ON CONFLICT (user_id) WHERE is_default DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("displayName", displayName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<PlayerCharacterSummary>> GetPlayerCharacters(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT character_id, display_name, template_id, equipped_part_ids::text,
                   profile_revision, profile_relations::text
            FROM player_characters
            WHERE user_id = @userId
            ORDER BY is_default DESC, created_at, character_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var characters = new List<PlayerCharacterSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(ReadPlayerCharacter(reader));
        }
        return characters;
    }

    private static PlayerCharacterSummary ReadPlayerCharacter(NpgsqlDataReader reader)
    {
        var parts = JsonSerializer.Deserialize<List<string>>(reader.GetString(3), JsonOptions) ?? new List<string>();
        var relations = JsonSerializer.Deserialize<List<PlayerProfileRelation>>(reader.GetString(5), JsonOptions)
                        ?? new List<PlayerProfileRelation>();
        return new PlayerCharacterSummary(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), parts,
            reader.GetInt64(4), relations);
    }

    public async Task<PlayerCharacterProfileUpdateResult> UpdatePlayerCharacterProfile(
        Guid characterId,
        Guid userId,
        UpdatePlayerCharacterProfileRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string replaySql = """
            SELECT result_revision FROM player_character_profile_commands
            WHERE character_id = @characterId AND command_id = @commandId;
            """;
        await using (var replayCommand = new NpgsqlCommand(replaySql, connection, transaction))
        {
            replayCommand.Parameters.AddWithValue("characterId", characterId);
            replayCommand.Parameters.AddWithValue("commandId", request.CommandId);
            var replay = await replayCommand.ExecuteScalarAsync(cancellationToken);
            if (replay is long replayRevision)
            {
                await transaction.CommitAsync(cancellationToken);
                return PlayerCharacterProfileUpdateResult.Success(replayRevision, true);
            }
        }

        const string selectSql = """
            SELECT profile_revision FROM player_characters
            WHERE character_id = @characterId AND user_id = @userId FOR UPDATE;
            """;
        long currentRevision;
        await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
        {
            selectCommand.Parameters.AddWithValue("characterId", characterId);
            selectCommand.Parameters.AddWithValue("userId", userId);
            var value = await selectCommand.ExecuteScalarAsync(cancellationToken);
            if (value is not long revision)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PlayerCharacterProfileUpdateResult.Rejected("character_not_found");
            }
            currentRevision = revision;
        }

        if (currentRevision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PlayerCharacterProfileUpdateResult.Rejected("profile_revision_conflict", currentRevision);
        }

        var nextRevision = currentRevision + 1;
        const string updateSql = """
            UPDATE player_characters
            SET display_name = @displayName,
                template_id = @templateId,
                equipped_part_ids = @equippedPartIds::jsonb,
                profile_relations = @profileRelations::jsonb,
                profile_revision = @nextRevision,
                updated_at = now()
            WHERE character_id = @characterId AND user_id = @userId;
            """;
        await using (var updateCommand = new NpgsqlCommand(updateSql, connection, transaction))
        {
            updateCommand.Parameters.AddWithValue("displayName", request.DisplayName.Trim());
            updateCommand.Parameters.AddWithValue("templateId", request.TemplateId.Trim());
            updateCommand.Parameters.AddWithValue("equippedPartIds", JsonSerializer.Serialize(request.EquippedPartIds ?? new List<string>(), JsonOptions));
            updateCommand.Parameters.AddWithValue("profileRelations", JsonSerializer.Serialize(request.ProfileRelations ?? new List<PlayerProfileRelation>(), JsonOptions));
            updateCommand.Parameters.AddWithValue("nextRevision", nextRevision);
            updateCommand.Parameters.AddWithValue("characterId", characterId);
            updateCommand.Parameters.AddWithValue("userId", userId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        const string commandSql = """
            INSERT INTO player_character_profile_commands(character_id, command_id, result_revision)
            VALUES (@characterId, @commandId, @resultRevision);
            """;
        await using (var command = new NpgsqlCommand(commandSql, connection, transaction))
        {
            command.Parameters.AddWithValue("characterId", characterId);
            command.Parameters.AddWithValue("commandId", request.CommandId);
            command.Parameters.AddWithValue("resultRevision", nextRevision);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return PlayerCharacterProfileUpdateResult.Success(nextRevision, false);
    }

    /// <summary>
    /// Persists which account character is used by an already registered world
    /// avatar. It is account/session metadata, not a world ontology command and
    /// does not mutate the world's revision or Facts.
    /// </summary>
    public async Task<WorldEntryResult> EnterWorld(
        Guid worldId,
        Guid userId,
        EnterWorldRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await CanAccessWorld(connection, worldId, userId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorldEntryResult.Rejected("world_not_found");
        }

        const string ownsCharacterSql = "SELECT EXISTS (SELECT 1 FROM player_characters WHERE character_id = @characterId AND user_id = @userId);";
        await using var characterCommand = new NpgsqlCommand(ownsCharacterSql, connection, transaction);
        characterCommand.Parameters.AddWithValue("characterId", request.CharacterId);
        characterCommand.Parameters.AddWithValue("userId", userId);
        if (!(bool)(await characterCommand.ExecuteScalarAsync(cancellationToken))!)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorldEntryResult.Rejected("character_not_found");
        }

        const string bindSql = """
            UPDATE world_player_avatars
            SET character_id = @characterId, character_selected_at = now(), updated_at = now()
            WHERE world_id = @worldId AND user_id = @userId AND entity_id = @avatarEntityId;
            """;
        await using var bindCommand = new NpgsqlCommand(bindSql, connection, transaction);
        bindCommand.Parameters.AddWithValue("characterId", request.CharacterId);
        bindCommand.Parameters.AddWithValue("worldId", worldId);
        bindCommand.Parameters.AddWithValue("userId", userId);
        bindCommand.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
        if (await bindCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorldEntryResult.Rejected("avatar_not_registered");
        }

        await transaction.CommitAsync(cancellationToken);
        return WorldEntryResult.Succeeded(request.CharacterId, request.AvatarEntityId);
    }

    public async Task<WorldAvatarProfileReadResult> GetWorldAvatarProfile(
        Guid worldId,
        Guid userId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await AvatarBelongsToUser(worldId, userId, avatarEntityId, cancellationToken))
            return WorldAvatarProfileReadResult.Rejected("avatar_not_registered");

        const string sql = """
            SELECT profile_relations::text, updated_revision
            FROM world_avatar_profiles
            WHERE world_id = @worldId AND avatar_entity_id = @avatarEntityId;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("avatarEntityId", avatarEntityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return WorldAvatarProfileReadResult.Succeeded(Array.Empty<PlayerProfileRelation>(), 0);

        var relations = JsonSerializer.Deserialize<List<PlayerProfileRelation>>(reader.GetString(0), JsonOptions)
                        ?? new List<PlayerProfileRelation>();
        return WorldAvatarProfileReadResult.Succeeded(relations, reader.GetInt64(1));
    }

    public async Task<AvatarCheckpointReadResult> GetAvatarCheckpoint(
        Guid worldId,
        Guid userId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await AvatarBelongsToUser(worldId, userId, avatarEntityId, cancellationToken))
            return AvatarCheckpointReadResult.Rejected("avatar_not_registered");

        const string sql = """
            SELECT zone_key,
                   position_x, position_y, position_z,
                   rotation_x, rotation_y, rotation_z,
                   updated_revision
            FROM world_avatar_checkpoints
            WHERE world_id = @worldId AND avatar_entity_id = @avatarEntityId;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("avatarEntityId", avatarEntityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return AvatarCheckpointReadResult.Rejected("checkpoint_not_found");

        return AvatarCheckpointReadResult.Succeeded(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            new TransformPayload(
                reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3),
                reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6),
                1d, 1d, 1d),
            reader.GetInt64(7));
    }

    public async Task<WorldCreatedResult?> CreateWorld(
        Guid ownerUserId,
        CreateWorldRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await UserExists(connection, transaction, ownerUserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            const string insertWorld = """
                INSERT INTO worlds (owner_user_id, slug, title, visibility)
                VALUES (@ownerUserId, @slug, @title, @visibility)
                RETURNING world_id, current_revision;
                """;
            await using var worldCommand = new NpgsqlCommand(insertWorld, connection, transaction);
            worldCommand.Parameters.AddWithValue("ownerUserId", ownerUserId);
            worldCommand.Parameters.AddWithValue("slug", request.Slug.Trim());
            worldCommand.Parameters.AddWithValue("title", request.Title.Trim());
            worldCommand.Parameters.AddWithValue("visibility", NormalizeVisibility(request.Visibility));

            await using var reader = await worldCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var worldId = reader.GetGuid(0);
            var revision = reader.GetInt64(1);
            await reader.DisposeAsync();

            const string addOwner = """
                INSERT INTO world_members (world_id, user_id, role)
                VALUES (@worldId, @ownerUserId, 'owner');
                """;
            await using var memberCommand = new NpgsqlCommand(addOwner, connection, transaction);
            memberCommand.Parameters.AddWithValue("worldId", worldId);
            memberCommand.Parameters.AddWithValue("ownerUserId", ownerUserId);
            await memberCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new WorldCreatedResult(worldId, revision);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            await transaction.RollbackAsync(cancellationToken);
            // Development reconnects are intentionally idempotent. A client may
            // lose its local preference after a domain reload, but it must not
            // create a second world or fail merely because its own slug exists.
            const string existingWorld = """
                SELECT world_id, current_revision
                FROM worlds
                WHERE owner_user_id = @ownerUserId AND slug = @slug;
                """;
            await using var existingCommand = new NpgsqlCommand(existingWorld, connection);
            existingCommand.Parameters.AddWithValue("ownerUserId", ownerUserId);
            existingCommand.Parameters.AddWithValue("slug", request.Slug.Trim());
            await using var existingReader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (!await existingReader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new WorldCreatedResult(existingReader.GetGuid(0), existingReader.GetInt64(1));
        }
    }

    public async Task<WorldProjection?> GetWorldProjection(
        Guid worldId,
        Guid actorUserId,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string worldSql = """
            SELECT w.world_id, w.title, w.current_revision
            FROM worlds w
            INNER JOIN world_members m ON m.world_id = w.world_id
            WHERE w.world_id = @worldId AND m.user_id = @actorUserId;
            """;
        await using var worldCommand = new NpgsqlCommand(worldSql, connection);
        worldCommand.Parameters.AddWithValue("worldId", worldId);
        worldCommand.Parameters.AddWithValue("actorUserId", actorUserId);
        await using var worldReader = await worldCommand.ExecuteReaderAsync(cancellationToken);
        if (!await worldReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var projection = new WorldProjection(
            worldReader.GetGuid(0),
            worldReader.GetString(1),
            worldReader.GetInt64(2),
            zoneKey,
            new List<WorldEntityProjection>(),
            new List<WorldFactProjection>(),
            new List<WorldRuleBindingProjection>(),
            new List<WorldActionDefinitionProjection>());
        await worldReader.DisposeAsync();

        const string actionsSql = """
            SELECT p.package_id, p.package_version, d.definition_id, d.definition_version,
                   COALESCE(d.payload #>> '{presentation,actorAnimationIntent}', '')
            FROM world_content_packages p
            INNER JOIN content_definitions d
                ON d.package_id = p.package_id
               AND d.package_version = p.package_version
               AND d.definition_kind = 'action_effect'
               AND d.is_published
            WHERE p.world_id = @worldId AND p.enabled
            ORDER BY p.package_id, d.definition_id, d.definition_version;
            """;
        await using (var actionsCommand = new NpgsqlCommand(actionsSql, connection))
        {
            actionsCommand.Parameters.AddWithValue("worldId", worldId);
            await using var actionsReader =
                await actionsCommand.ExecuteReaderAsync(cancellationToken);
            while (await actionsReader.ReadAsync(cancellationToken))
            {
                projection.Actions.Add(new WorldActionDefinitionProjection(
                    actionsReader.GetString(0),
                    actionsReader.GetString(1),
                    actionsReader.GetString(2),
                    actionsReader.GetInt32(3),
                    actionsReader.GetString(4)));
            }
        }

        var zoneFilter = string.IsNullOrWhiteSpace(zoneKey)
            ? string.Empty
            : " AND zone_key = @zoneKey";
        var entitiesSql = """
            SELECT entity_id, template_id, template_version, display_name, zone_key,
                   position_x, position_y, position_z, rotation_x, rotation_y, rotation_z,
                   scale_x, scale_y, scale_z
            FROM world_entities
            WHERE world_id = @worldId AND deleted_revision IS NULL
            """ + zoneFilter + " ORDER BY created_revision, entity_id;";
        await using var entitiesCommand = new NpgsqlCommand(entitiesSql, connection);
        entitiesCommand.Parameters.AddWithValue("worldId", worldId);
        AddProjectionZoneParameter(entitiesCommand, zoneKey);
        await using var entitiesReader = await entitiesCommand.ExecuteReaderAsync(cancellationToken);
        while (await entitiesReader.ReadAsync(cancellationToken))
        {
            projection.Entities.Add(new WorldEntityProjection(
                entitiesReader.GetGuid(0),
                entitiesReader.GetString(1),
                entitiesReader.GetInt32(2),
                entitiesReader.GetString(3),
                entitiesReader.IsDBNull(4) ? null : entitiesReader.GetString(4),
                new TransformPayload(
                    entitiesReader.GetDouble(5), entitiesReader.GetDouble(6), entitiesReader.GetDouble(7),
                    entitiesReader.GetDouble(8), entitiesReader.GetDouble(9), entitiesReader.GetDouble(10),
                    entitiesReader.GetDouble(11), entitiesReader.GetDouble(12), entitiesReader.GetDouble(13))));
        }
        await entitiesReader.DisposeAsync();

        var factsSql = """
            SELECT fact_id, subject_entity_id, predicate_id, object_kind, object_entity_id,
                   object_canonical_id, object_value::text
            FROM world_facts f
            INNER JOIN world_entities e ON e.entity_id = f.subject_entity_id
            WHERE f.world_id = @worldId AND f.retracted_revision IS NULL
              AND e.deleted_revision IS NULL
            """ + (string.IsNullOrWhiteSpace(zoneKey) ? string.Empty : " AND e.zone_key = @zoneKey") +
            " ORDER BY f.created_revision, f.fact_id;";
        await using var factsCommand = new NpgsqlCommand(factsSql, connection);
        factsCommand.Parameters.AddWithValue("worldId", worldId);
        AddProjectionZoneParameter(factsCommand, zoneKey);
        await using var factsReader = await factsCommand.ExecuteReaderAsync(cancellationToken);
        while (await factsReader.ReadAsync(cancellationToken))
        {
            projection.Facts.Add(new WorldFactProjection(
                factsReader.GetGuid(0),
                factsReader.GetGuid(1),
                factsReader.GetString(2),
                factsReader.GetString(3),
                factsReader.IsDBNull(4) ? null : factsReader.GetGuid(4),
                factsReader.IsDBNull(5) ? null : factsReader.GetString(5),
                factsReader.IsDBNull(6) ? null : factsReader.GetString(6)));
        }
        await factsReader.DisposeAsync();

        var rulesSql = """
            SELECT binding_id, target_entity_id, rule_id, rule_version, enabled, parameter_values::text
            FROM world_rule_bindings b
            INNER JOIN world_entities e ON e.entity_id = b.target_entity_id
            WHERE b.world_id = @worldId AND b.retracted_revision IS NULL
              AND e.deleted_revision IS NULL
            """ + (string.IsNullOrWhiteSpace(zoneKey) ? string.Empty : " AND e.zone_key = @zoneKey") +
            " ORDER BY b.created_revision, b.binding_id;";
        await using var rulesCommand = new NpgsqlCommand(rulesSql, connection);
        rulesCommand.Parameters.AddWithValue("worldId", worldId);
        AddProjectionZoneParameter(rulesCommand, zoneKey);
        await using var rulesReader = await rulesCommand.ExecuteReaderAsync(cancellationToken);
        while (await rulesReader.ReadAsync(cancellationToken))
        {
            projection.RuleBindings.Add(new WorldRuleBindingProjection(
                rulesReader.GetGuid(0),
                rulesReader.GetGuid(1),
                rulesReader.GetString(2),
                rulesReader.GetInt32(3),
                rulesReader.GetBoolean(4),
                rulesReader.GetString(5)));
        }

        return projection;
    }

    public async Task<IReadOnlyList<WorldZoneProjection>?> GetWorldZones(
        Guid worldId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await CanAccessWorld(connection, worldId, actorUserId, cancellationToken))
        {
            return null;
        }

        const string sql = """
            SELECT zone_key, min_x, min_z, max_x, max_z, simulation_mode
            FROM world_zones
            WHERE world_id = @worldId
            ORDER BY zone_key;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var zones = new List<WorldZoneProjection>();
        while (await reader.ReadAsync(cancellationToken))
        {
            zones.Add(new WorldZoneProjection(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5)));
        }
        return zones;
    }

    /// <summary>
    /// Scheduler-only query. This does not expose world content to clients; it
    /// returns just the durable boundary identity and configured run mode.
    /// </summary>
    public async Task<IReadOnlyList<WorldZoneScheduleDefinition>> GetZoneScheduleDefinitions(
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT world_id, zone_key, simulation_mode, min_x, min_z, max_x, max_z
            FROM world_zones
            ORDER BY world_id, zone_key;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var zones = new List<WorldZoneScheduleDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            zones.Add(new WorldZoneScheduleDefinition(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)));
        }
        return zones;
    }

    /// <summary>
    /// Resolves player motion only through the complete authored ontology
    /// contract. Registration alone never enables movement. Removing the
    /// locomotion Rule Block, action link, capability, life state, or Physical
    /// Meaning removes the avatar from this scheduler on the next cache refresh.
    /// </summary>
    public async Task<IReadOnlyList<WorldPlayerAvatarMotionConfiguration>> GetPlayerAvatarMotionConfigurations(
        Guid worldId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT a.entity_id, a.user_id,
                   e.position_x, e.position_y, e.position_z,
                   speed_fact.object_value::text,
                   jump_action.object_canonical_id,
                   gravity_fact.object_value::text,
                   jump_takeoff_fact.object_value::text,
                   ground_stick_fact.object_value::text,
                   maximum_step_fact.object_value::text,
                   ground_clearance_fact.object_value::text
            FROM world_player_avatars a
            INNER JOIN world_entities e ON e.entity_id = a.entity_id
            INNER JOIN world_facts actor_concept
                ON actor_concept.world_id = a.world_id
               AND actor_concept.subject_entity_id = a.entity_id
               AND actor_concept.predicate_id = 'has_concept'
               AND actor_concept.object_kind = 'canonical'
               AND actor_concept.object_canonical_id = 'Actor'
               AND actor_concept.retracted_revision IS NULL
            INNER JOIN world_facts player_concept
                ON player_concept.world_id = a.world_id
               AND player_concept.subject_entity_id = a.entity_id
               AND player_concept.predicate_id = 'has_concept'
               AND player_concept.object_kind = 'canonical'
               AND player_concept.object_canonical_id = 'PlayerControlled'
               AND player_concept.retracted_revision IS NULL
            INNER JOIN world_facts locomotion_capability
                ON locomotion_capability.world_id = a.world_id
               AND locomotion_capability.subject_entity_id = a.entity_id
               AND locomotion_capability.predicate_id = 'grants_capability'
               AND locomotion_capability.object_kind = 'canonical'
               AND locomotion_capability.object_canonical_id = 'Locomotion'
               AND locomotion_capability.retracted_revision IS NULL
            INNER JOIN world_facts physical
                ON physical.world_id = a.world_id
               AND physical.subject_entity_id = a.entity_id
               AND physical.predicate_id = 'physical_profile'
               AND physical.object_kind = 'canonical'
               AND physical.object_canonical_id = 'LocalCharacterController'
               AND physical.retracted_revision IS NULL
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'locomotion_action'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) action ON true
            INNER JOIN world_rule_bindings binding
                ON binding.world_id = a.world_id
               AND binding.target_entity_id = a.entity_id
               AND binding.enabled = true
               AND binding.retracted_revision IS NULL
            INNER JOIN world_content_packages package
                ON package.world_id = a.world_id
               AND package.enabled = true
            INNER JOIN content_definitions definition
                ON definition.package_id = package.package_id
               AND definition.package_version = package.package_version
               AND definition.definition_kind = 'action_effect'
               AND definition.definition_id =
                   action.object_canonical_id
               AND definition.payload #>>
                   '{ruleInvocation,ruleId}' = binding.rule_id
               AND definition.is_published = true
            INNER JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'movement_speed'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) speed_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'jump_action'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) jump_action ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'gravity_acceleration'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) gravity_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'jump_takeoff_speed'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) jump_takeoff_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'ground_stick_velocity'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) ground_stick_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'maximum_step_height'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) maximum_step_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = a.world_id
                  AND f.subject_entity_id = a.entity_id
                  AND f.predicate_id = 'ground_clearance'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) ground_clearance_fact ON true
            WHERE a.world_id = @worldId
              AND e.zone_key = @zoneKey
              AND e.deleted_revision IS NULL
              AND EXISTS (
                  SELECT 1
                  FROM world_facts alive
                  WHERE alive.world_id = a.world_id
                    AND alive.subject_entity_id = a.entity_id
                    AND alive.predicate_id = 'is_alive'
                    AND alive.object_kind = 'boolean'
                    AND alive.object_value::text = 'true'
                    AND alive.retracted_revision IS NULL)
            ORDER BY a.entity_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var avatars = new List<WorldPlayerAvatarMotionConfiguration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var speed = reader.IsDBNull(5) ? null : TryReadJsonNumber(reader.GetString(5));
            avatars.Add(new WorldPlayerAvatarMotionConfiguration(
                worldId,
                reader.GetGuid(0),
                reader.GetGuid(1),
                zoneKey,
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                speed,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                ReadNullableNumber(reader, 7),
                ReadNullableNumber(reader, 8),
                ReadNullableNumber(reader, 9),
                ReadNullableNumber(reader, 10),
                ReadNullableNumber(reader, 11)));
        }
        return avatars
            .GroupBy(value => value.AvatarEntityId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
    }

    /// <summary>
    /// Projects only explicitly authored collision meaning into lightweight
    /// server geometry input. A prefab, mesh, collider component, or object name
    /// cannot create a proxy. Invalid dimensions are returned as nullable input
    /// and rejected by WorldCollisionProxyPolicy.
    /// </summary>
    public async Task<IReadOnlyList<WorldCollisionProxyConfiguration>>
        GetWorldCollisionProxyConfigurations(
            Guid worldId,
            string zoneKey,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT e.entity_id,
                   e.position_x, e.position_y, e.position_z,
                   role_fact.object_canonical_id,
                   shape_fact.object_canonical_id,
                   radius_fact.object_value::text,
                   height_fact.object_value::text,
                   size_x_fact.object_value::text,
                   size_y_fact.object_value::text,
                   size_z_fact.object_value::text,
                   offset_x_fact.object_value::text,
                   offset_y_fact.object_value::text,
                   offset_z_fact.object_value::text
            FROM world_entities e
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_role'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) role_fact ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_proxy_shape'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) shape_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_radius'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) radius_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_height'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) height_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_size_x'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) size_x_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_size_y'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) size_y_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_size_z'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) size_z_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_center_offset_x'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) offset_x_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_center_offset_y'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) offset_y_fact ON true
            LEFT JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'collision_center_offset_z'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) offset_z_fact ON true
            WHERE e.world_id = @worldId
              AND e.zone_key = @zoneKey
              AND e.deleted_revision IS NULL
            ORDER BY e.entity_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var configurations =
            new List<WorldCollisionProxyConfiguration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            configurations.Add(new WorldCollisionProxyConfiguration(
                worldId,
                reader.GetGuid(0),
                zoneKey,
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetString(5),
                ReadNullableNumber(reader, 6),
                ReadNullableNumber(reader, 7),
                ReadNullableNumber(reader, 8),
                ReadNullableNumber(reader, 9),
                ReadNullableNumber(reader, 10),
                ReadNullableNumber(reader, 11),
                ReadNullableNumber(reader, 12),
                ReadNullableNumber(reader, 13)));
        }
        return configurations;
    }

    /// <summary>
    /// Resolves autonomous actors exclusively from their authored semantic
    /// contract. No template, prefab, mesh, display name, or placement category
    /// participates in this query. Removing the Rule Block or Physical Meaning
    /// removes the actor from the scheduler on the next cache refresh.
    /// </summary>
    public async Task<IReadOnlyList<WorldAutonomousActorConfiguration>>
        GetAutonomousActorConfigurations(
            Guid worldId,
            string zoneKey,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT e.entity_id,
                   e.position_x, e.position_y, e.position_z,
                   speed.object_value::text,
                   detection.object_value::text,
                   leash.object_value::text,
                   attack_range.object_value::text,
                   target_action.object_canonical_id,
                   chase_action.object_canonical_id,
                   action.object_canonical_id,
                   idle_intent.object_canonical_id,
                   move_intent.object_canonical_id,
                   package.package_id,
                   package.package_version,
                   target_definition.definition_version,
                   chase_definition.definition_version,
                   definition.definition_version
            FROM world_entities e
            INNER JOIN world_facts physical
                ON physical.world_id = e.world_id
               AND physical.subject_entity_id = e.entity_id
               AND physical.predicate_id = 'physical_profile'
               AND physical.object_kind = 'canonical'
               AND physical.object_canonical_id = 'AuthorityKinematic'
               AND physical.retracted_revision IS NULL
            INNER JOIN world_facts actor_concept
                ON actor_concept.world_id = e.world_id
               AND actor_concept.subject_entity_id = e.entity_id
               AND actor_concept.predicate_id = 'has_concept'
               AND actor_concept.object_kind = 'canonical'
               AND actor_concept.object_canonical_id = 'Actor'
               AND actor_concept.retracted_revision IS NULL
             INNER JOIN world_facts autonomous_concept
                 ON autonomous_concept.world_id = e.world_id
                AND autonomous_concept.subject_entity_id = e.entity_id
                AND autonomous_concept.predicate_id = 'has_concept'
                AND autonomous_concept.object_kind = 'canonical'
                AND autonomous_concept.object_canonical_id = 'AutonomousAgent'
                AND autonomous_concept.retracted_revision IS NULL
             INNER JOIN world_facts alive
                 ON alive.world_id = e.world_id
                AND alive.subject_entity_id = e.entity_id
                AND alive.predicate_id = 'is_alive'
                AND alive.object_kind = 'boolean'
                AND alive.object_value::text = 'true'
                AND alive.retracted_revision IS NULL
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'targeting_profile'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) targeting_profile ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'chase_profile'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) chase_profile ON true
            INNER JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'movement_speed'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) speed ON true
            INNER JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'detection_range'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) detection ON true
            INNER JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'leash_range'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) leash ON true
            INNER JOIN LATERAL (
                SELECT f.object_value
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'attack_range'
                  AND f.object_kind = 'number'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) attack_range ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'target_action'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) target_action ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'chase_action'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) chase_action ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'attack_action'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) action ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'idle_animation_intent'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) idle_intent ON true
            INNER JOIN LATERAL (
                SELECT f.object_canonical_id
                FROM world_facts f
                WHERE f.world_id = e.world_id
                  AND f.subject_entity_id = e.entity_id
                  AND f.predicate_id = 'move_animation_intent'
                  AND f.object_kind = 'canonical'
                  AND f.retracted_revision IS NULL
                ORDER BY f.created_revision DESC, f.fact_id DESC
                LIMIT 1
            ) move_intent ON true
            INNER JOIN world_content_packages package
                ON package.world_id = e.world_id
               AND package.enabled = true
            INNER JOIN content_definitions definition
                ON definition.package_id = package.package_id
               AND definition.package_version = package.package_version
               AND definition.definition_kind = 'action_effect'
               AND definition.definition_id =
                   action.object_canonical_id
               AND definition.is_published = true
            INNER JOIN content_definitions target_definition
                ON target_definition.package_id = package.package_id
               AND target_definition.package_version =
                   package.package_version
               AND target_definition.definition_kind = 'action_effect'
               AND target_definition.definition_id =
                   target_action.object_canonical_id
               AND target_definition.is_published = true
            INNER JOIN content_definitions chase_definition
                ON chase_definition.package_id = package.package_id
               AND chase_definition.package_version =
                   package.package_version
               AND chase_definition.definition_kind = 'action_effect'
               AND chase_definition.definition_id =
                   chase_action.object_canonical_id
               AND chase_definition.is_published = true
            INNER JOIN world_rule_bindings attack_binding
                ON attack_binding.world_id = e.world_id
               AND attack_binding.target_entity_id = e.entity_id
               AND attack_binding.rule_id = definition.payload #>>
                   '{ruleInvocation,ruleId}'
               AND attack_binding.enabled = true
               AND attack_binding.retracted_revision IS NULL
            INNER JOIN world_rule_bindings target_binding
                ON target_binding.world_id = e.world_id
               AND target_binding.target_entity_id = e.entity_id
               AND target_binding.rule_id =
                   target_definition.payload #>>
                       '{ruleInvocation,ruleId}'
               AND target_binding.enabled = true
               AND target_binding.retracted_revision IS NULL
            INNER JOIN world_rule_bindings chase_binding
                ON chase_binding.world_id = e.world_id
               AND chase_binding.target_entity_id = e.entity_id
               AND chase_binding.rule_id =
                   chase_definition.payload #>>
                       '{ruleInvocation,ruleId}'
               AND chase_binding.enabled = true
               AND chase_binding.retracted_revision IS NULL
            WHERE e.world_id = @worldId
              AND e.zone_key = @zoneKey
              AND e.deleted_revision IS NULL
            ORDER BY e.entity_id, package.package_id,
                     target_definition.definition_version,
                     chase_definition.definition_version,
                     definition.definition_version;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var candidates = new List<WorldAutonomousActorConfiguration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var speed = TryReadJsonNumber(reader.GetString(4));
            var detection = TryReadJsonNumber(reader.GetString(5));
            var leash = TryReadJsonNumber(reader.GetString(6));
            var range = TryReadJsonNumber(reader.GetString(7));
            if (speed is not > 0d ||
                detection is not > 0d ||
                leash is not > 0d ||
                range is not > 0d)
            {
                continue;
            }
            candidates.Add(new WorldAutonomousActorConfiguration(
                worldId,
                reader.GetGuid(0),
                zoneKey,
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                speed.Value,
                detection.Value,
                leash.Value,
                range.Value,
                reader.GetString(8),
                reader.GetInt32(15),
                reader.GetString(9),
                reader.GetInt32(16),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetInt32(17)));
        }

        // An action ID enabled from more than one package is ambiguous. The
        // scheduler refuses to guess, matching the Unity action resolver.
        return candidates
            .GroupBy(value => value.ActorEntityId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
    }

    public async Task<IReadOnlyList<WorldAutonomousTargetConfiguration>>
        GetAutonomousTargetConfigurations(
            Guid worldId,
            string zoneKey,
            CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
             SELECT entity.entity_id,
                    avatar.user_id,
                    entity.zone_key,
                    entity.position_x,
                    entity.position_y,
                    entity.position_z,
                    avatar.user_id IS NOT NULL OR EXISTS (
                        SELECT 1
                        FROM world_facts runtime_owner
                        WHERE runtime_owner.world_id = entity.world_id
                          AND runtime_owner.subject_entity_id = entity.entity_id
                          AND runtime_owner.predicate_id = 'physical_profile'
                          AND runtime_owner.object_kind = 'canonical'
                          AND runtime_owner.object_canonical_id = 'AuthorityKinematic'
                          AND runtime_owner.retracted_revision IS NULL)
                        AS requires_runtime_position
            FROM world_entities entity
            LEFT JOIN world_player_avatars avatar
                ON avatar.world_id = entity.world_id
               AND avatar.entity_id = entity.entity_id
             WHERE entity.world_id = @worldId
               AND entity.zone_key = @zoneKey
               AND entity.deleted_revision IS NULL
               AND EXISTS (
                   SELECT 1
                   FROM world_facts alive
                   WHERE alive.world_id = entity.world_id
                     AND alive.subject_entity_id = entity.entity_id
                     AND alive.predicate_id = 'is_alive'
                     AND alive.object_kind = 'boolean'
                     AND alive.object_value::text = 'true'
                     AND alive.retracted_revision IS NULL)
             ORDER BY entity.entity_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<WorldAutonomousTargetConfiguration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WorldAutonomousTargetConfiguration(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                 reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                 reader.GetDouble(3),
                 reader.GetDouble(4),
                 reader.GetDouble(5),
                 reader.GetBoolean(6)));
        }
        return result;
    }

    /// <summary>
    /// Re-seeds ephemeral motion from the current durable avatar transform. This
    /// is used by session activation and after an accepted checkpoint command;
    /// it never creates a Fact or a world event.
    /// </summary>
    public async Task<WorldPlayerMotionState?> ResetPlayerMotionFromDurableState(
        Guid worldId,
        Guid avatarEntityId,
        string expectedZoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT e.zone_key, e.position_x, e.position_y, e.position_z
            FROM world_player_avatars a
            INNER JOIN world_entities e
                ON e.world_id = a.world_id AND e.entity_id = a.entity_id
            WHERE a.world_id = @worldId
              AND a.entity_id = @avatarEntityId
              AND e.deleted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("avatarEntityId", avatarEntityId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var zoneKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        if (!string.Equals(zoneKey, expectedZoneKey, StringComparison.Ordinal))
            return null;
        var current = await motionRuntime.Get(worldId, avatarEntityId, cancellationToken);
        var state = new WorldPlayerMotionState(
            worldId,
            avatarEntityId,
            zoneKey,
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            current?.LastProcessedIntentSequence ?? 0,
            "idle",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            0d,
            0d,
            0d,
            reader.GetDouble(2),
            true,
            0,
            null);
        await motionRuntime.Set(state, cancellationToken);
        return state;
    }

    /// <summary>
    /// Returns only server-side ownership registrations for a Zone. The caller
    /// filters these against ephemeral live sessions before it reads motion data;
    /// no owner identity is returned to a game client.
    /// </summary>
    public async Task<IReadOnlyList<WorldPlayerAvatarRegistration>> GetRegisteredPlayerAvatarsForZone(
        Guid worldId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT a.entity_id, a.user_id
            FROM world_player_avatars a
            INNER JOIN world_entities e ON e.entity_id = a.entity_id
            WHERE a.world_id = @worldId
              AND e.zone_key = @zoneKey
              AND e.deleted_revision IS NULL
            ORDER BY a.entity_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var registrations = new List<WorldPlayerAvatarRegistration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            registrations.Add(new WorldPlayerAvatarRegistration(reader.GetGuid(0), reader.GetGuid(1)));
        }
        return registrations;
    }

    public async Task<HeadlessZoneRuntimeInput> GetHeadlessZoneRuntimeInput(
        Guid worldId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var entities = new List<string>();
        const string entitiesSql = """
            SELECT entity_id::text
            FROM world_entities
            WHERE world_id = @worldId AND zone_key = @zoneKey AND deleted_revision IS NULL;
            """;
        await using (var command = new NpgsqlCommand(entitiesSql, connection))
        {
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue("zoneKey", zoneKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) entities.Add(reader.GetString(0));
        }

        var facts = new List<HeadlessAuthoredFact>();
        const string factsSql = """
            SELECT f.subject_entity_id::text, f.predicate_id, f.object_kind,
                   f.object_entity_id::text, f.object_canonical_id, f.object_value::text
            FROM world_facts f
            INNER JOIN world_entities e ON e.entity_id = f.subject_entity_id
            WHERE f.world_id = @worldId AND f.retracted_revision IS NULL
              AND e.deleted_revision IS NULL AND e.zone_key = @zoneKey
            ORDER BY f.created_revision, f.fact_id;
            """;
        await using (var command = new NpgsqlCommand(factsSql, connection))
        {
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue("zoneKey", zoneKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                facts.Add(new HeadlessAuthoredFact(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        var bindings = new List<HeadlessRuleBinding>();
        const string bindingsSql = """
            SELECT b.target_entity_id::text, b.rule_id, b.parameter_values::text, d.payload::text
            FROM world_rule_bindings b
            INNER JOIN world_entities e ON e.entity_id = b.target_entity_id
            LEFT JOIN content_definitions d
                   ON d.definition_kind = 'rule'
                  AND d.definition_id = b.rule_id
                  AND d.definition_version = b.rule_version
                  AND d.is_published
            WHERE b.world_id = @worldId AND b.enabled AND b.retracted_revision IS NULL
              AND e.deleted_revision IS NULL AND e.zone_key = @zoneKey
            ORDER BY b.created_revision, b.binding_id;
            """;
        await using (var command = new NpgsqlCommand(bindingsSql, connection))
        {
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue("zoneKey", zoneKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                bindings.Add(new HeadlessRuleBinding(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return new HeadlessZoneRuntimeInput(entities, facts, bindings);
    }

    public async Task<WorldZoneProjection?> GetWorldZone(
        Guid worldId,
        Guid actorUserId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await CanAccessWorld(connection, worldId, actorUserId, cancellationToken))
        {
            return null;
        }

        const string sql = """
            SELECT zone_key, min_x, min_z, max_x, max_z, simulation_mode
            FROM world_zones
            WHERE world_id = @worldId AND zone_key = @zoneKey;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new WorldZoneProjection(
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5));
    }

    public async Task<bool> ZoneExists(
        Guid worldId,
        Guid actorUserId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await CanAccessWorld(connection, worldId, actorUserId, cancellationToken))
        {
            return false;
        }

        await using var command = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM world_zones WHERE world_id = @worldId AND zone_key = @zoneKey);",
            connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<bool> CanAccessWorld(
        Guid worldId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await CanAccessWorld(connection, worldId, actorUserId, cancellationToken);
    }

    public async Task<bool> OwnsRegisteredAvatar(
        Guid worldId,
        Guid actorUserId,
        Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_player_avatars
                WHERE world_id = @worldId
                  AND user_id = @actorUserId
                  AND entity_id = @avatarEntityId);
            """;
        await using var command =
            new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "actorUserId",
            actorUserId);
        command.Parameters.AddWithValue(
            "avatarEntityId",
            avatarEntityId);
        return (bool)(await command.ExecuteScalarAsync(
            cancellationToken))!;
    }

    public async Task<bool> AvatarBelongsToUser(
        Guid worldId,
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await AvatarBelongsToUser(connection, null, worldId, userId, entityId, cancellationToken);
    }

    private static async Task<bool> AvatarBelongsToUser(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid worldId,
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_player_avatars a
                INNER JOIN world_entities e ON e.entity_id = a.entity_id
                WHERE a.world_id = @worldId
                  AND a.user_id = @userId
                  AND a.entity_id = @entityId
                  AND e.deleted_revision IS NULL);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("entityId", entityId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<string?> ResolveCommandZoneKey(
        Guid worldId,
        WorldCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CommandType == WorldCommandTypes.PlaceEntity)
        {
            return request.Payload.Deserialize<PlaceEntityPayload>(JsonOptions)?.ZoneKey;
        }

        var entityId = request.CommandType switch
        {
            WorldCommandTypes.MoveEntity => request.Payload.Deserialize<MoveEntityPayload>(JsonOptions)?.EntityId,
            WorldCommandTypes.RetireEntity => request.Payload.Deserialize<RetireEntityPayload>(JsonOptions)?.EntityId,
            WorldCommandTypes.SetAuthoredFact => request.Payload.Deserialize<SetAuthoredFactPayload>(JsonOptions)?.SubjectEntityId,
            WorldCommandTypes.AddRuleBlock => request.Payload.Deserialize<AddRuleBlockPayload>(JsonOptions)?.TargetEntityId,
            WorldCommandTypes.ApplyMeaningPackage => request.Payload.Deserialize<ApplyMeaningPackagePayload>(JsonOptions)?.TargetEntityId,
            WorldCommandTypes.RegisterPlayerAvatar => request.Payload.Deserialize<RegisterPlayerAvatarPayload>(JsonOptions)?.EntityId,
            WorldCommandTypes.SetAvatarProfileRelations => request.Payload.Deserialize<SetAvatarProfileRelationsPayload>(JsonOptions)?.AvatarEntityId,
            WorldCommandTypes.ExecuteAction => request.Payload.Deserialize<ExecuteActionPayload>(JsonOptions)?.ActorEntityId,
            _ => null
        };

        if (request.CommandType == WorldCommandTypes.DefineZone)
        {
            return request.Payload.Deserialize<DefineZonePayload>(JsonOptions)?.ZoneKey;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (entityId.HasValue && entityId.Value != Guid.Empty)
        {
            return await GetEntityZoneKey(connection, worldId, entityId.Value, cancellationToken);
        }

        return request.CommandType switch
        {
            WorldCommandTypes.RetractAuthoredFact => await GetFactZoneKey(
                connection,
                worldId,
                request.Payload.Deserialize<RetractAuthoredFactPayload>(JsonOptions)?.FactId,
                cancellationToken),
            WorldCommandTypes.RemoveRuleBlock => await GetRuleBindingZoneKey(
                connection,
                worldId,
                request.Payload.Deserialize<RemoveRuleBlockPayload>(JsonOptions)?.BindingId,
                cancellationToken),
            _ => null
        };
    }

    public async Task<CommandResult> ApplyCommand(
        Guid worldId,
        Guid actorUserId,
        WorldCommandRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireWorldLock(connection, transaction, worldId, cancellationToken);

        var duplicate = await FindCommandResult(connection, transaction, request.CommandId, cancellationToken);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return duplicate with { IsReplay = true };
        }

        var access = await GetWorldAccess(connection, transaction, worldId, actorUserId, cancellationToken);
        if (access is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CommandResult(false, "world_not_found", null, null, false);
        }

        var isGameplayAction = request.CommandType is
            WorldCommandTypes.ExecuteAction or
            WorldCommandTypes.RegisterPlayerAvatar or
            WorldCommandTypes.SaveAvatarCheckpoint;
        if (!access.CanEdit && !isGameplayAction)
        {
            await RecordRejectedCommand(connection, transaction, worldId, actorUserId, request, "forbidden", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CommandResult(false, "forbidden", null, null, false);
        }

        if (request.ExpectedRevision != access.CurrentRevision)
        {
            await RecordRejectedCommand(connection, transaction, worldId, actorUserId, request, "stale_revision", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CommandResult(false, "stale_revision", access.CurrentRevision, null, false);
        }

        var nextRevision = access.CurrentRevision + 1;
        await using (var savepoint = new NpgsqlCommand(
            "SAVEPOINT apply_authoring_command;",
            connection,
            transaction))
        {
            await savepoint.ExecuteNonQueryAsync(cancellationToken);
        }
        var apply = await ApplyAuthoringCommand(
            connection,
            transaction,
            worldId,
            actorUserId,
            request,
            nextRevision,
            cancellationToken);
        if (!apply.Accepted)
        {
            // PostgreSQL marks a transaction as aborted after a caught
            // constraint exception. Restore the command savepoint before
            // recording the durable rejection result.
            await using (var rollbackToSavepoint = new NpgsqlCommand(
                "ROLLBACK TO SAVEPOINT apply_authoring_command;",
                connection,
                transaction))
            {
                await rollbackToSavepoint.ExecuteNonQueryAsync(cancellationToken);
            }
            await RecordRejectedCommand(connection, transaction, worldId, actorUserId, request, apply.RejectionCode!, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CommandResult(false, apply.RejectionCode, access.CurrentRevision, null, false);
        }

        await using (var releaseSavepoint = new NpgsqlCommand(
            "RELEASE SAVEPOINT apply_authoring_command;",
            connection,
            transaction))
        {
            await releaseSavepoint.ExecuteNonQueryAsync(cancellationToken);
        }

        await SetWorldRevision(connection, transaction, worldId, nextRevision, cancellationToken);
        await RecordAcceptedCommand(connection, transaction, worldId, actorUserId, request, nextRevision, cancellationToken);
        var eventId = await RecordWorldEvent(
            connection,
            transaction,
            worldId,
            actorUserId,
            request,
            nextRevision,
            apply.EventPayloadJson ?? request.Payload.GetRawText(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (request.CommandType == WorldCommandTypes.SaveAvatarCheckpoint)
        {
            var checkpoint = request.Payload.Deserialize<SaveAvatarCheckpointPayload>(JsonOptions);
            if (checkpoint is not null && !string.IsNullOrWhiteSpace(checkpoint.ZoneKey))
            {
                await ResetPlayerMotionFromDurableState(
                    worldId,
                    checkpoint.AvatarEntityId,
                    checkpoint.ZoneKey,
                    cancellationToken);
            }
        }
        else if (request.CommandType == WorldCommandTypes.RetireEntity)
        {
            var retirement =
                request.Payload.Deserialize<RetireEntityPayload>(JsonOptions);
            if (retirement is not null)
            {
                await autonomousMotionRuntime.Remove(
                    worldId,
                    retirement.EntityId,
                    cancellationToken);
            }
        }

        return new CommandResult(true, null, nextRevision, eventId, false);
    }

    private async Task<CommandApplyResult> ApplyAuthoringCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid actorUserId,
        WorldCommandRequest request,
        long revision,
        CancellationToken cancellationToken)
    {
        return request.CommandType switch
        {
            WorldCommandTypes.PlaceEntity => await PlaceEntity(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.MoveEntity => await MoveEntity(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.RetireEntity => await RetireEntity(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.SetAuthoredFact => await SetAuthoredFact(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.RetractAuthoredFact => await RetractAuthoredFact(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.AddRuleBlock => await AddRuleBlock(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.RegisterPlayerAvatar => await RegisterPlayerAvatar(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.SaveAvatarCheckpoint => await SaveAvatarCheckpoint(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.MigrateLegacyEquipmentRelations => await MigrateLegacyEquipmentRelations(
                connection, transaction, worldId, revision, cancellationToken),
            WorldCommandTypes.MigrateUnassignedEntityZones => await MigrateUnassignedEntityZones(
                connection, transaction, worldId, cancellationToken),
            WorldCommandTypes.SetAvatarProfileRelations => await SetAvatarProfileRelations(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.SetContentPackage => await SetContentPackage(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.ExecuteAction => await ExecuteAction(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.RemoveRuleBlock => await RemoveRuleBlock(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.ApplyMeaningPackage => await ApplyMeaningPackage(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.DefineZone => await DefineZone(connection, transaction, worldId, request.Payload, cancellationToken),
            _ => CommandApplyResult.Rejected("unsupported_command_type")
        };
    }

    private static async Task<CommandApplyResult> DefineZone(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<DefineZonePayload>(JsonOptions);
        if (request is null || !SemanticId.IsValid(request.ZoneKey) ||
            !double.IsFinite(request.MinX) || !double.IsFinite(request.MinZ) ||
            !double.IsFinite(request.MaxX) || !double.IsFinite(request.MaxZ) ||
            request.MinX >= request.MaxX || request.MinZ >= request.MaxZ ||
            request.SimulationMode is not ("active" or "reduced" or "dormant"))
        {
            return CommandApplyResult.Rejected("invalid_define_zone_payload");
        }

        const string sql = """
            INSERT INTO world_zones
                (world_id, zone_key, min_x, min_z, max_x, max_z, simulation_mode)
            VALUES
                (@worldId, @zoneKey, @minX, @minZ, @maxX, @maxZ, @simulationMode)
            ON CONFLICT (world_id, zone_key) DO UPDATE SET
                min_x = EXCLUDED.min_x,
                min_z = EXCLUDED.min_z,
                max_x = EXCLUDED.max_x,
                max_z = EXCLUDED.max_z,
                simulation_mode = EXCLUDED.simulation_mode;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", request.ZoneKey.Trim());
        command.Parameters.AddWithValue("minX", request.MinX);
        command.Parameters.AddWithValue("minZ", request.MinZ);
        command.Parameters.AddWithValue("maxX", request.MaxX);
        command.Parameters.AddWithValue("maxZ", request.MaxZ);
        command.Parameters.AddWithValue("simulationMode", request.SimulationMode);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return CommandApplyResult.Succeeded();
    }

    private static async Task<CommandApplyResult> PlaceEntity(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<PlaceEntityPayload>(JsonOptions);
        if (request is null || !SemanticId.IsValid(request.TemplateId) || request.TemplateVersion <= 0
            || string.IsNullOrWhiteSpace(request.DisplayName) || !request.Transform.IsValid
            || request.InitialFacts is { Count: > 128 }
            || (request.InitialFacts?.Any(fact =>
                    fact is null ||
                    !SemanticId.IsValid(fact.PredicateId) ||
                    !fact.IsValidObject()) ?? false))
        {
            return CommandApplyResult.Rejected("invalid_place_entity_payload");
        }

        var entityId = request.EntityId == Guid.Empty ? Guid.NewGuid() : request.EntityId;
        if (!string.IsNullOrWhiteSpace(request.ZoneKey)
            && !await ZoneExists(connection, transaction, worldId, request.ZoneKey, cancellationToken))
        {
            return CommandApplyResult.Rejected("unknown_zone");
        }
        foreach (var fact in request.InitialFacts ?? [])
        {
            if (fact.ObjectKind == "entity" &&
                fact.ObjectEntityId != entityId &&
                !await EntityExists(
                    connection,
                    transaction,
                    worldId,
                    fact.ObjectEntityId!.Value,
                    cancellationToken))
            {
                return CommandApplyResult.Rejected("fact_entity_not_found");
            }
        }

        const string sql = """
            INSERT INTO world_entities
                (entity_id, world_id, zone_key, template_id, template_version, display_name,
                 position_x, position_y, position_z, rotation_x, rotation_y, rotation_z,
                 scale_x, scale_y, scale_z, created_revision)
            VALUES
                (@entityId, @worldId, @zoneKey, @templateId, @templateVersion, @displayName,
                 @positionX, @positionY, @positionZ, @rotationX, @rotationY, @rotationZ,
                 @scaleX, @scaleY, @scaleZ, @revision);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("entityId", entityId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", (object?)request.ZoneKey ?? DBNull.Value);
        command.Parameters.AddWithValue("templateId", request.TemplateId);
        command.Parameters.AddWithValue("templateVersion", request.TemplateVersion);
        command.Parameters.AddWithValue("displayName", request.DisplayName.Trim());
        AddTransformParameters(command, request.Transform);
        command.Parameters.AddWithValue("revision", revision);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return CommandApplyResult.Rejected("entity_id_already_exists");
        }

        foreach (var fact in request.InitialFacts ?? [])
        {
            const string factSql = """
                INSERT INTO world_facts
                    (world_id, subject_entity_id, predicate_id, object_kind,
                     object_entity_id, object_canonical_id, object_value,
                     source_type, created_revision)
                VALUES
                    (@worldId, @subjectEntityId, @predicateId, @objectKind,
                     @objectEntityId, @objectCanonicalId, @objectValue,
                     'authored', @revision);
                """;
            await using var factCommand =
                new NpgsqlCommand(factSql, connection, transaction);
            factCommand.Parameters.AddWithValue("worldId", worldId);
            factCommand.Parameters.AddWithValue("subjectEntityId", entityId);
            factCommand.Parameters.AddWithValue("predicateId", fact.PredicateId);
            factCommand.Parameters.AddWithValue("objectKind", fact.ObjectKind);
            factCommand.Parameters.AddWithValue(
                "objectEntityId",
                (object?)fact.ObjectEntityId ?? DBNull.Value);
            factCommand.Parameters.AddWithValue(
                "objectCanonicalId",
                (object?)fact.ObjectCanonicalId ?? DBNull.Value);
            factCommand.Parameters.AddWithValue(
                "objectValue",
                NpgsqlDbType.Jsonb,
                (object?)fact.ObjectValueJson ?? DBNull.Value);
            factCommand.Parameters.AddWithValue("revision", revision);
            await factCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            entityId,
            request.TemplateId,
            request.TemplateVersion,
            request.Transform,
            initialFactCount = request.InitialFacts?.Count ?? 0
        }, JsonOptions));
    }

    private static async Task<CommandApplyResult> SetAvatarProfileRelations(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        Guid actorUserId,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<SetAvatarProfileRelationsPayload>(JsonOptions);
        if (request is null || request.AvatarEntityId == Guid.Empty || !request.IsValid())
            return CommandApplyResult.Rejected("invalid_avatar_profile_relations");

        const string ownershipSql = """
            SELECT EXISTS (SELECT 1 FROM world_player_avatars
                WHERE world_id = @worldId AND user_id = @userId AND entity_id = @avatarEntityId);
            """;
        await using (var ownership = new NpgsqlCommand(ownershipSql, connection, transaction))
        {
            ownership.Parameters.AddWithValue("worldId", worldId);
            ownership.Parameters.AddWithValue("userId", actorUserId);
            ownership.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
            if (!(bool)(await ownership.ExecuteScalarAsync(cancellationToken))!)
                return CommandApplyResult.Rejected("avatar_not_registered");
        }

        var relations = request.ProfileRelations ?? new List<PlayerProfileRelation>();
        const string upsertSql = """
            INSERT INTO world_avatar_profiles(world_id, avatar_entity_id, profile_relations, updated_revision)
            VALUES (@worldId, @avatarEntityId, @profileRelations::jsonb, @revision)
            ON CONFLICT (world_id, avatar_entity_id) DO UPDATE SET
                profile_relations = EXCLUDED.profile_relations,
                updated_revision = EXCLUDED.updated_revision,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
        command.Parameters.AddWithValue("profileRelations", JsonSerializer.Serialize(relations, JsonOptions));
        command.Parameters.AddWithValue("revision", revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            request.AvatarEntityId,
            profileRelationCount = relations.Count
        }, JsonOptions));
    }

    private static async Task<CommandApplyResult> SetContentPackage(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        Guid actorUserId,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<SetContentPackagePayload>(JsonOptions);
        if (request is null || !SemanticId.IsValid(request.PackageId) ||
            string.IsNullOrWhiteSpace(request.PackageVersion) || request.PackageVersion.Length > 64)
        {
            return CommandApplyResult.Rejected("invalid_content_package_payload");
        }

        const string accessSql = """
            SELECT EXISTS (
                SELECT 1 FROM content_package_members m
                WHERE m.package_id = @packageId AND m.user_id = @userId),
                   EXISTS (
                SELECT 1 FROM content_definitions d
                WHERE d.package_id = @packageId AND d.package_version = @packageVersion
                  AND d.is_published);
            """;
        await using (var access = new NpgsqlCommand(accessSql, connection, transaction))
        {
            access.Parameters.AddWithValue("packageId", request.PackageId.Trim());
            access.Parameters.AddWithValue("packageVersion", request.PackageVersion.Trim());
            access.Parameters.AddWithValue("userId", actorUserId);
            await using var reader = await access.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0))
                return CommandApplyResult.Rejected("content_package_forbidden");
            if (request.Enabled && !reader.GetBoolean(1))
                return CommandApplyResult.Rejected("content_package_version_not_published");
        }

        const string upsertSql = """
            INSERT INTO world_content_packages(world_id, package_id, package_version, enabled, updated_revision)
            VALUES (@worldId, @packageId, @packageVersion, @enabled, @revision)
            ON CONFLICT (world_id, package_id) DO UPDATE SET
                package_version = EXCLUDED.package_version,
                enabled = EXCLUDED.enabled,
                updated_revision = EXCLUDED.updated_revision,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(upsertSql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("packageId", request.PackageId.Trim());
        command.Parameters.AddWithValue("packageVersion", request.PackageVersion.Trim());
        command.Parameters.AddWithValue("enabled", request.Enabled);
        command.Parameters.AddWithValue("revision", revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            request.PackageId,
            request.PackageVersion,
            request.Enabled
        }, JsonOptions));
    }

    // The immutable action definition supplies conditions and effects. The command
    // supplies only actor/target/tool identities, so Unity cannot invent damage,
    // predicates, or durable state transitions.
    private async Task<CommandApplyResult> ExecuteAction(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        Guid actorUserId,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<ExecuteActionPayload>(JsonOptions);
        if (request is null || !request.IsValid()) return CommandApplyResult.Rejected("invalid_execute_action_payload");
        var prepared = await PrepareActionEvaluation(
            connection,
            transaction,
            worldId,
            actorUserId,
            request,
            cancellationToken);
        if (!prepared.Accepted)
            return CommandApplyResult.Rejected(prepared.RejectionCode!);

        if (!await actionCooldownRuntime.TryAcquire(
                worldId,
                request.ActorEntityId,
                request.ToolEntityId,
                request.ActionId.Trim(),
                prepared.Cooldown,
                cancellationToken))
        {
            return CommandApplyResult.Rejected("action_cooldown_active");
        }

        foreach (var mutation in prepared.Evaluation!.Mutations)
        {
            var mutationResult = await ApplyAuthorityMutation(
                connection,
                transaction,
                worldId,
                mutation,
                revision,
                prepared.BoundRule?.BindingId,
                cancellationToken);
            if (mutationResult is not null)
                return CommandApplyResult.Rejected(mutationResult);
        }
        var postRules = await ApplyPostRuleInvocations(
            connection,
            transaction,
            worldId,
            request,
            prepared.Definition!,
            revision,
            cancellationToken);
        if (!postRules.Accepted)
            return CommandApplyResult.Rejected(postRules.RejectionCode!);

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            request.ActorEntityId, request.TargetEntityId, request.ToolEntityId,
            request.PackageId, request.PackageVersion, request.ActionId, request.DefinitionVersion,
            RuleBindingId = prepared.BoundRule?.BindingId,
            MutationCount =
                prepared.Evaluation.Mutations.Count +
                postRules.MutationCount,
            PostRuleBindingIds = postRules.RuleBindingIds
        }, JsonOptions));
    }

    /// <summary>
    /// Executes a scheduler-selected actor action through the same immutable
    /// Action + assigned Rule Block evaluator used by player commands. The
    /// caller cannot supply effects, predicates, ranges, or cooldowns. The
    /// durable event is attributed to the world owner for audit while the
    /// payload retains the actual autonomous actor entity.
    /// </summary>
    public async Task<AutonomousActionExecutionResult>
        ExecuteAutonomousAction(
            Guid worldId,
            ExecuteActionPayload request,
            CancellationToken cancellationToken)
    {
        if (request is null || !request.IsValid())
        {
            return AutonomousActionExecutionResult.Rejected(
                "invalid_autonomous_action_payload");
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        await AcquireWorldLock(
            connection,
            transaction,
            worldId,
            cancellationToken);

        const string worldSql = """
            SELECT owner_user_id, current_revision
            FROM worlds
            WHERE world_id = @worldId;
            """;
        Guid ownerUserId;
        long currentRevision;
        await using (var worldCommand =
                     new NpgsqlCommand(worldSql, connection, transaction))
        {
            worldCommand.Parameters.AddWithValue("worldId", worldId);
            await using var reader =
                await worldCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return AutonomousActionExecutionResult.Rejected(
                    "world_not_found");
            }
            ownerUserId = reader.GetGuid(0);
            currentRevision = reader.GetInt64(1);
        }

        var prepared = await PrepareActionEvaluation(
            connection,
            transaction,
            worldId,
            ownerUserId,
            request,
            cancellationToken,
            requirePlayerActorOwnership: false);
        if (!prepared.Accepted)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AutonomousActionExecutionResult.Rejected(
                prepared.RejectionCode!);
        }

        if (!await actionCooldownRuntime.TryAcquire(
                worldId,
                request.ActorEntityId,
                request.ToolEntityId,
                request.ActionId.Trim(),
                prepared.Cooldown,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AutonomousActionExecutionResult.Rejected(
                "action_cooldown_active");
        }

        var nextRevision = currentRevision + 1;
        foreach (var mutation in prepared.Evaluation!.Mutations)
        {
            var rejection = await ApplyAuthorityMutation(
                connection,
                transaction,
                worldId,
                mutation,
                nextRevision,
                prepared.BoundRule?.BindingId,
                cancellationToken);
            if (rejection is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AutonomousActionExecutionResult.Rejected(rejection);
            }
        }
        var postRules = await ApplyPostRuleInvocations(
            connection,
            transaction,
            worldId,
            request,
            prepared.Definition!,
            nextRevision,
            cancellationToken);
        if (!postRules.Accepted)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AutonomousActionExecutionResult.Rejected(
                postRules.RejectionCode!);
        }

        var commandId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(request, JsonOptions);
        var command = new WorldCommandRequest(
            1,
            commandId,
            currentRevision,
            WorldCommandTypes.ExecuteAction,
            payload);
        await SetWorldRevision(
            connection,
            transaction,
            worldId,
            nextRevision,
            cancellationToken);
        await RecordAcceptedCommand(
            connection,
            transaction,
            worldId,
            ownerUserId,
            command,
            nextRevision,
            cancellationToken);
        var eventPayload = JsonSerializer.Serialize(new
        {
            executionSource = "autonomous_runtime",
            request.ActorEntityId,
            request.TargetEntityId,
            request.PackageId,
            request.PackageVersion,
            request.ActionId,
            request.DefinitionVersion,
            RuleBindingId = prepared.BoundRule?.BindingId,
            MutationCount =
                prepared.Evaluation.Mutations.Count +
                postRules.MutationCount,
            PostRuleBindingIds = postRules.RuleBindingIds
        }, JsonOptions);
        var eventId = await RecordWorldEvent(
            connection,
            transaction,
            worldId,
            ownerUserId,
            command,
            nextRevision,
            eventPayload,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AutonomousActionExecutionResult.Succeeded(
            prepared.Definition!.presentation
                ?.actorAnimationIntent?.Trim(),
            prepared.BoundRule?.BindingId,
            nextRevision,
            eventId);
    }

    public async Task<RuntimeActionEvaluationResult> EvaluateRuntimeAction(
        Guid worldId,
        Guid actorUserId,
        ExecuteActionPayload request,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.IsValid())
            return RuntimeActionEvaluationResult.Rejected(
                "invalid_runtime_action_payload");
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
        var prepared = await PrepareActionEvaluation(
            connection,
            transaction,
            worldId,
            actorUserId,
            request,
            cancellationToken);
        if (!prepared.Accepted)
            return RuntimeActionEvaluationResult.Rejected(
                prepared.RejectionCode!);
        if (prepared.Evaluation!.Mutations.Count != 0)
            return RuntimeActionEvaluationResult.Rejected(
                "runtime_action_mutation_not_allowed");
        var animationIntent =
            prepared.Definition!.presentation?.actorAnimationIntent?.Trim();
        if (string.IsNullOrWhiteSpace(animationIntent))
            return RuntimeActionEvaluationResult.Rejected(
                "runtime_action_presentation_missing");
        if (!await actionCooldownRuntime.TryAcquire(
                worldId,
                request.ActorEntityId,
                request.ToolEntityId,
                request.ActionId.Trim(),
                prepared.Cooldown,
                cancellationToken))
        {
            return RuntimeActionEvaluationResult.Rejected(
                "action_cooldown_active");
        }

        await transaction.CommitAsync(cancellationToken);
        return RuntimeActionEvaluationResult.Succeeded(
            request,
            animationIntent,
            prepared.BoundRule?.BindingId);
    }

    public async Task<ActionPreviewEvaluationResult> PreviewAction(
        Guid worldId,
        Guid actorUserId,
        ExecuteActionPayload request,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.IsValid())
            return ActionPreviewEvaluationResult.Rejected(
                "invalid_action_preview_payload");
        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
        var prepared = await PrepareActionEvaluation(
            connection,
            transaction,
            worldId,
            actorUserId,
            request,
            cancellationToken);
        if (!prepared.Accepted)
            return ActionPreviewEvaluationResult.Rejected(
                prepared.RejectionCode!);

        await transaction.CommitAsync(cancellationToken);
        return ActionPreviewEvaluationResult.Succeeded(
            request,
            prepared.Definition!.presentation
                ?.actorAnimationIntent?.Trim(),
            prepared.BoundRule?.BindingId,
            prepared.Evaluation!.Mutations.Count);
    }

    /// <summary>
    /// Evaluates a scheduler-owned target or chase action through the same
    /// immutable Action + assigned Rule Block path without requiring the actor
    /// entity to be the world owner's registered player avatar. This preview
    /// never mutates durable state or acquires a cooldown.
    /// </summary>
    public async Task<ActionPreviewEvaluationResult>
        PreviewAutonomousAction(
            Guid worldId,
            ExecuteActionPayload request,
            CancellationToken cancellationToken)
    {
        if (request is null || !request.IsValid())
        {
            return ActionPreviewEvaluationResult.Rejected(
                "invalid_autonomous_action_preview_payload");
        }

        await using var connection =
            await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
        await using var ownerCommand = new NpgsqlCommand(
            "SELECT owner_user_id FROM worlds WHERE world_id = @worldId;",
            connection,
            transaction);
        ownerCommand.Parameters.AddWithValue("worldId", worldId);
        var owner = await ownerCommand.ExecuteScalarAsync(cancellationToken);
        if (owner is not Guid ownerUserId)
        {
            return ActionPreviewEvaluationResult.Rejected(
                "world_not_found");
        }

        var prepared = await PrepareActionEvaluation(
            connection,
            transaction,
            worldId,
            ownerUserId,
            request,
            cancellationToken,
            requirePlayerActorOwnership: false);
        if (!prepared.Accepted)
        {
            return ActionPreviewEvaluationResult.Rejected(
                prepared.RejectionCode!);
        }
        if (prepared.Evaluation!.Mutations.Count != 0)
        {
            return ActionPreviewEvaluationResult.Rejected(
                "autonomous_control_rule_must_be_ephemeral");
        }

        await transaction.CommitAsync(cancellationToken);
        return ActionPreviewEvaluationResult.Succeeded(
            request,
            prepared.Definition!.presentation
                ?.actorAnimationIntent?.Trim(),
            prepared.BoundRule?.BindingId,
            0);
    }

    private async Task<PreparedActionEvaluation> PrepareActionEvaluation(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid actorUserId,
        ExecuteActionPayload request,
        CancellationToken cancellationToken,
        bool requirePlayerActorOwnership = true)
    {
        if (requirePlayerActorOwnership &&
            !await AvatarBelongsToUser(
                connection,
                transaction,
                worldId,
                actorUserId,
                request.ActorEntityId,
                cancellationToken))
        {
            return PreparedActionEvaluation.Rejected(
                "action_actor_forbidden");
        }
        if ((!requirePlayerActorOwnership &&
             !await EntityExists(
                 connection,
                 transaction,
                 worldId,
                 request.ActorEntityId,
                 cancellationToken)) ||
            !await EntityExists(
                connection,
                transaction,
                worldId,
                request.TargetEntityId,
                cancellationToken) ||
            (request.ToolEntityId.HasValue &&
             !await EntityExists(
                 connection,
                 transaction,
                 worldId,
                 request.ToolEntityId.Value,
                 cancellationToken)))
        {
            return PreparedActionEvaluation.Rejected(
                "action_entity_not_found");
        }

        const string definitionSql = """
            SELECT d.payload::text
            FROM world_content_packages w
            INNER JOIN content_definitions d ON d.package_id = w.package_id
                AND d.package_version = w.package_version
                AND d.definition_kind = 'action_effect'
                AND d.definition_id = @actionId
                AND d.definition_version = @definitionVersion
                AND d.is_published
            WHERE w.world_id = @worldId AND w.package_id = @packageId
              AND w.package_version = @packageVersion AND w.enabled;
            """;
        string? definitionJson;
        await using (var command = new NpgsqlCommand(
                         definitionSql,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue(
                "packageId",
                request.PackageId.Trim());
            command.Parameters.AddWithValue(
                "packageVersion",
                request.PackageVersion.Trim());
            command.Parameters.AddWithValue(
                "actionId",
                request.ActionId.Trim());
            command.Parameters.AddWithValue(
                "definitionVersion",
                request.DefinitionVersion);
            definitionJson =
                (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (definitionJson is null)
            return PreparedActionEvaluation.Rejected(
                "action_definition_not_enabled");

        OntologyActionEffectDefinition? definition;
        try
        {
            definition =
                JsonSerializer.Deserialize<OntologyActionEffectDefinition>(
                    definitionJson,
                    OntologyJson);
        }
        catch (JsonException)
        {
            return PreparedActionEvaluation.Rejected(
                "invalid_published_action_definition");
        }
        if (definition is null ||
            !AuthoritativeActionEvaluator.IsSupportedDefinition(
                definition,
                out _) ||
            !string.Equals(
                definition.actionVerb,
                request.ActionId.Trim(),
                StringComparison.Ordinal) ||
            (definition.requiresTool && !request.ToolEntityId.HasValue) ||
            (definition.objectPattern == "?tool" &&
             !request.ToolEntityId.HasValue))
        {
            return PreparedActionEvaluation.Rejected(
                "unsupported_authoritative_action_definition");
        }
        var observationRejection =
            AuthorityRuntimeObservationPolicy.Validate(
                definition,
                request.GroundedObservation);
        if (observationRejection is not null)
        {
            return PreparedActionEvaluation.Rejected(
                observationRejection);
        }

        var facts = await LoadAuthorityFactSnapshot(
            connection,
            transaction,
            worldId,
            cancellationToken);
        var cooldown = TimeSpan.Zero;
        if (definition.runtimeConstraints is not null &&
            (definition.runtimeConstraints.maxActorTargetDistance > 0d ||
             AuthoritativeActionEvaluator.HasConfiguredNumericSource(
                 definition.runtimeConstraints
                     .maxActorTargetDistanceFrom) ||
             AuthoritativeActionEvaluator.HasConfiguredNumericSource(
                 definition.runtimeConstraints.cooldownSecondsFrom)))
        {
            var actorPosition = await LoadRuntimeActionPosition(
                connection,
                transaction,
                worldId,
                request.ActorEntityId,
                cancellationToken);
            var targetPosition = await LoadRuntimeActionPosition(
                connection,
                transaction,
                worldId,
                request.TargetEntityId,
                cancellationToken);
            if (actorPosition is not null &&
                targetPosition is not null &&
                !string.Equals(
                    actorPosition.ZoneKey,
                    targetPosition.ZoneKey,
                    StringComparison.Ordinal))
            {
                return PreparedActionEvaluation.Rejected(
                    "action_target_out_of_range");
            }

            var runtimeRejection =
                AuthoritativeActionRuntimeConstraintEvaluator.Validate(
                    definition,
                    actorPosition?.Position,
                    targetPosition?.Position,
                    request.ActorEntityId,
                    request.TargetEntityId,
                    request.ToolEntityId,
                    facts,
                    out cooldown);
            if (runtimeRejection is not null)
                return PreparedActionEvaluation.Rejected(
                    runtimeRejection);
        }

        AuthorityBoundRuleInvocation? boundRule = null;
        OntologyRuleDefinition? invokedRuleDefinition = null;
        if (AuthoritativeActionEvaluator.HasRuleInvocation(definition))
        {
            if (!AuthoritativeActionEvaluator.TryResolveRuleBindingEntity(
                    definition,
                    request.ActorEntityId,
                    request.TargetEntityId,
                    request.ToolEntityId,
                    out var ruleBindingEntityId))
            {
                return PreparedActionEvaluation.Rejected(
                    "action_rule_binding_entity_unavailable");
            }
            boundRule = await LoadBoundRuleInvocation(
                connection,
                transaction,
                worldId,
                ruleBindingEntityId,
                definition.ruleInvocation.ruleId,
                cancellationToken);
            if (boundRule is null)
                return PreparedActionEvaluation.Rejected(
                    "action_rule_block_not_assigned");
            try
            {
                invokedRuleDefinition =
                    JsonSerializer.Deserialize<OntologyRuleDefinition>(
                        boundRule.DefinitionJson,
                        OntologyJson);
            }
            catch (JsonException)
            {
                return PreparedActionEvaluation.Rejected(
                    "invalid_published_rule_definition");
            }
            if (invokedRuleDefinition is null ||
                invokedRuleDefinition.catalogVersion !=
                boundRule.RuleVersion)
            {
                return PreparedActionEvaluation.Rejected(
                    "action_rule_definition_version_mismatch");
            }
            var ruleAnimationIntent =
                invokedRuleDefinition.runtimePresentation
                    ?.actorAnimationIntent?.Trim();
            if (!string.IsNullOrWhiteSpace(ruleAnimationIntent))
            {
                definition.presentation ??=
                    new OntologyActionPresentationDefinition();
                var transportIntent =
                    definition.presentation.actorAnimationIntent?.Trim();
                if (!string.IsNullOrWhiteSpace(transportIntent) &&
                    !string.Equals(
                        transportIntent,
                        ruleAnimationIntent,
                        StringComparison.Ordinal))
                {
                    return PreparedActionEvaluation.Rejected(
                        "action_rule_presentation_conflict");
                }
                definition.presentation.actorAnimationIntent =
                    ruleAnimationIntent;
            }
        }

        var evaluation = boundRule is null
            ? AuthoritativeActionEvaluator.Evaluate(
                definition,
                request.ActorEntityId,
                request.TargetEntityId,
                request.ToolEntityId,
                facts)
            : AuthoritativeActionEvaluator.EvaluateInvokedRule(
                definition,
                invokedRuleDefinition!,
                request.ActorEntityId,
                request.TargetEntityId,
                request.ToolEntityId,
                facts);
        return evaluation.Accepted
            ? PreparedActionEvaluation.Succeeded(
                definition,
                evaluation,
                boundRule,
                cooldown)
            : PreparedActionEvaluation.Rejected(
                evaluation.RejectionCode!);
    }

    /// <summary>
    /// Evaluates lifecycle reactions declared by immutable action data after
    /// the primary mutations are visible in the current transaction. The
    /// Authority owns this generic rule chain; Unity does not detect defeat or
    /// invent loot mutations. Removing an optional Rule Block binding removes
    /// only that post-transition behavior.
    /// </summary>
    private async Task<PostRuleApplicationResult> ApplyPostRuleInvocations(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        ExecuteActionPayload request,
        OntologyActionEffectDefinition actionDefinition,
        long revision,
        CancellationToken cancellationToken)
    {
        var invocations =
            actionDefinition.postRuleInvocations ??
            new List<OntologyActionRuleInvocationDefinition>();
        if (invocations.Count == 0)
            return PostRuleApplicationResult.Succeeded(0, []);

        var mutationCount = 0;
        var bindingIds = new List<Guid>();
        foreach (var invocation in invocations)
        {
            if (!AuthoritativeActionEvaluator.TryResolveRuleBindingEntity(
                    invocation,
                    request.ActorEntityId,
                    request.TargetEntityId,
                    request.ToolEntityId,
                    out var bindingEntityId))
            {
                return PostRuleApplicationResult.Rejected(
                    "post_rule_binding_entity_unavailable");
            }

            var boundRule = await LoadBoundRuleInvocation(
                connection,
                transaction,
                worldId,
                bindingEntityId,
                invocation.ruleId,
                cancellationToken);
            if (boundRule is null)
            {
                if (invocation.required)
                    return PostRuleApplicationResult.Rejected(
                        "post_rule_block_not_assigned");
                continue;
            }

            OntologyRuleDefinition? ruleDefinition;
            try
            {
                ruleDefinition =
                    JsonSerializer.Deserialize<OntologyRuleDefinition>(
                        boundRule.DefinitionJson,
                        OntologyJson);
            }
            catch (JsonException)
            {
                return PostRuleApplicationResult.Rejected(
                    "invalid_published_post_rule_definition");
            }
            if (ruleDefinition is null ||
                ruleDefinition.catalogVersion != boundRule.RuleVersion)
            {
                return PostRuleApplicationResult.Rejected(
                    "post_rule_definition_version_mismatch");
            }

            var facts = await LoadAuthorityFactSnapshot(
                connection,
                transaction,
                worldId,
                cancellationToken);
            var transport = new OntologyActionEffectDefinition
            {
                actionVerb = actionDefinition.actionVerb,
                ruleInvocation = invocation,
                presentation = new OntologyActionPresentationDefinition(),
                runtimeConstraints = new OntologyActionRuntimeConstraints()
            };
            var evaluation =
                AuthoritativeActionEvaluator.EvaluateInvokedRule(
                    transport,
                    ruleDefinition,
                    request.ActorEntityId,
                    request.TargetEntityId,
                    request.ToolEntityId,
                    facts);
            if (!evaluation.Accepted)
            {
                if (!invocation.required &&
                    string.Equals(
                        evaluation.RejectionCode,
                        "action_conditions_not_met",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                return PostRuleApplicationResult.Rejected(
                    evaluation.RejectionCode ??
                    "post_rule_rejected");
            }

            foreach (var mutation in evaluation.Mutations)
            {
                var rejection = await ApplyAuthorityMutation(
                    connection,
                    transaction,
                    worldId,
                    mutation,
                    revision,
                    boundRule.BindingId,
                    cancellationToken);
                if (rejection is not null)
                    return PostRuleApplicationResult.Rejected(rejection);
            }
            mutationCount += evaluation.Mutations.Count;
            bindingIds.Add(boundRule.BindingId);
        }

        return PostRuleApplicationResult.Succeeded(
            mutationCount,
            bindingIds);
    }

    private async Task<AuthorityActionTargetPosition?>
        LoadRuntimeActionPosition(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid entityId,
            CancellationToken cancellationToken)
    {
        var autonomous = await autonomousMotionRuntime.Get(
            worldId,
            entityId,
            cancellationToken);
        if (autonomous is not null)
        {
            return new AuthorityActionTargetPosition(
                autonomous.ZoneKey,
                new AuthoritySpatialPosition(
                    autonomous.PositionX,
                    autonomous.PositionY,
                    autonomous.PositionZ));
        }

        var player = await motionRuntime.Get(
            worldId,
            entityId,
            cancellationToken);
        if (player is not null)
        {
            return new AuthorityActionTargetPosition(
                player.ZoneKey,
                new AuthoritySpatialPosition(
                    player.PositionX,
                    player.PositionY,
                    player.PositionZ));
        }

        return await LoadActionTargetPosition(
            connection,
            transaction,
            worldId,
            entityId,
            cancellationToken);
    }

    private static async Task<AuthorityBoundRuleInvocation?>
        LoadBoundRuleInvocation(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid targetEntityId,
            string ruleId,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.binding_id, b.rule_version, d.payload::text
            FROM world_rule_bindings b
            INNER JOIN content_definitions d
                    ON d.definition_kind = 'rule'
                   AND d.definition_id = b.rule_id
                   AND d.definition_version = b.rule_version
                   AND d.is_published
            WHERE b.world_id = @worldId
              AND b.target_entity_id = @targetEntityId
              AND b.rule_id = @ruleId
              AND b.enabled = true
              AND b.retracted_revision IS NULL
            ORDER BY b.created_revision, b.binding_id
            LIMIT 2;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var result = new AuthorityBoundRuleInvocation(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2));
        if (await reader.ReadAsync(cancellationToken))
            return null;
        return result;
    }

    private static async Task<AuthorityActionTargetPosition?>
        LoadActionTargetPosition(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid entityId,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT zone_key, position_x, position_y, position_z
            FROM world_entities
            WHERE world_id = @worldId AND entity_id = @entityId
              AND deleted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(
            sql,
            connection,
            transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("entityId", entityId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AuthorityActionTargetPosition(
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            new AuthoritySpatialPosition(
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3)));
    }

    private static async Task<IReadOnlyList<AuthorityFactSnapshot>> LoadAuthorityFactSnapshot(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT subject_entity_id, predicate_id, object_kind,
                   object_entity_id::text, object_canonical_id, object_value::text
            FROM world_facts
            WHERE world_id = @worldId AND retracted_revision IS NULL
            ORDER BY created_revision, fact_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var facts = new List<AuthorityFactSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = reader.GetString(2);
            var value = kind switch
            {
                "entity" => reader.GetString(3),
                "canonical" => reader.GetString(4),
                _ => NormalizeAuthorityJsonValue(reader.IsDBNull(5) ? null : reader.GetString(5))
            };
            facts.Add(new AuthorityFactSnapshot(
                reader.GetGuid(0), reader.GetString(1), kind, value));
        }
        await reader.DisposeAsync();

        // Rule Blocks are durable semantic bindings, not duplicated authored
        // facts. Project them into the command-scoped ontology snapshot so a
        // published action can require the same has_rule_block relation that
        // Unity and the headless evaluator use.
        const string bindingsSql = """
            SELECT target_entity_id, rule_id
            FROM world_rule_bindings
            WHERE world_id = @worldId
              AND enabled = true
              AND retracted_revision IS NULL
            ORDER BY created_revision, binding_id;
            """;
        await using var bindingsCommand =
            new NpgsqlCommand(bindingsSql, connection, transaction);
        bindingsCommand.Parameters.AddWithValue("worldId", worldId);
        await using var bindingsReader =
            await bindingsCommand.ExecuteReaderAsync(cancellationToken);
        while (await bindingsReader.ReadAsync(cancellationToken))
        {
            facts.Add(new AuthorityFactSnapshot(
                bindingsReader.GetGuid(0),
                "has_rule_block",
                "canonical",
                bindingsReader.GetString(1)));
        }
        return facts;
    }

    private static string NormalizeAuthorityJsonValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String =>
                    document.RootElement.GetString() ?? string.Empty,
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => document.RootElement.GetRawText()
            };
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static async Task<string?> ApplyAuthorityMutation(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        AuthorityMutation mutation,
        long revision,
        Guid? sourceRuleBindingId,
        CancellationToken cancellationToken)
    {
        if (!await EntityExists(
                connection, transaction, worldId, mutation.SubjectEntityId, cancellationToken))
            return "action_effect_subject_not_found";

        return mutation.Kind switch
        {
            AuthorityMutationKind.Assert => await InsertActionFact(
                connection, transaction, worldId, mutation.SubjectEntityId,
                mutation.PredicateId, mutation.Object!, revision,
                sourceRuleBindingId, mutation.ResultLifetime,
                cancellationToken),
            AuthorityMutationKind.Retract => await RetractActionFact(
                connection, transaction, worldId, mutation.SubjectEntityId,
                mutation.PredicateId, mutation.Object!, revision, cancellationToken),
            AuthorityMutationKind.Set => await SetActionFact(
                connection, transaction, worldId, mutation.SubjectEntityId,
                mutation.PredicateId, mutation.Object!, revision,
                sourceRuleBindingId, mutation.ResultLifetime,
                cancellationToken),
            AuthorityMutationKind.AdjustNumber => await AdjustActionNumberFact(
                connection, transaction, worldId, mutation, revision,
                sourceRuleBindingId, cancellationToken),
            _ => "unsupported_action_mutation"
        };
    }

    private static async Task<string?> InsertActionFact(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid subjectEntityId,
        string predicateId,
        AuthorityObject value,
        long revision,
        Guid? sourceRuleBindingId,
        OntologyRuleResultLifetime resultLifetime,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO world_facts
                (world_id, subject_entity_id, predicate_id, object_kind,
                 object_entity_id, object_canonical_id, object_value,
                 source_type, source_rule_binding_id, rule_result_lifetime,
                 created_revision)
            SELECT @worldId, @subjectEntityId, @predicateId, @objectKind,
                   @objectEntityId, @objectCanonicalId, @objectValue,
                   'action', @sourceRuleBindingId, @resultLifetime, @revision
            WHERE NOT EXISTS (
                SELECT 1 FROM world_facts
                WHERE world_id = @worldId AND subject_entity_id = @subjectEntityId
                  AND predicate_id = @predicateId AND object_kind = @objectKind
                  AND object_entity_id IS NOT DISTINCT FROM @objectEntityId
                  AND object_canonical_id IS NOT DISTINCT FROM @objectCanonicalId
                  AND object_value IS NOT DISTINCT FROM @objectValue
                  AND source_rule_binding_id IS NOT DISTINCT FROM @sourceRuleBindingId
                  AND rule_result_lifetime = @resultLifetime
                  AND retracted_revision IS NULL);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddActionFactParameters(command, worldId, subjectEntityId, predicateId, value, revision);
        command.Parameters.AddWithValue(
            "sourceRuleBindingId",
            sourceRuleBindingId.HasValue
                ? sourceRuleBindingId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "resultLifetime",
            resultLifetime == OntologyRuleResultLifetime.DurableState
                ? "durable_state"
                : "rule_bound");
        await command.ExecuteNonQueryAsync(cancellationToken);
        return null;
    }

    private static async Task<string?> RetractActionFact(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid subjectEntityId,
        string predicateId,
        AuthorityObject value,
        long revision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE world_facts
            SET retracted_revision = @revision
            WHERE world_id = @worldId AND subject_entity_id = @subjectEntityId
              AND predicate_id = @predicateId AND object_kind = @objectKind
              AND object_entity_id IS NOT DISTINCT FROM @objectEntityId
              AND object_canonical_id IS NOT DISTINCT FROM @objectCanonicalId
              AND object_value IS NOT DISTINCT FROM @objectValue
              AND retracted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddActionFactParameters(command, worldId, subjectEntityId, predicateId, value, revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return null;
    }

    private static async Task<string?> SetActionFact(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid subjectEntityId,
        string predicateId,
        AuthorityObject value,
        long revision,
        Guid? sourceRuleBindingId,
        OntologyRuleResultLifetime resultLifetime,
        CancellationToken cancellationToken)
    {
        const string retractSql = """
            UPDATE world_facts SET retracted_revision = @revision
            WHERE world_id = @worldId AND subject_entity_id = @subjectEntityId
              AND predicate_id = @predicateId AND retracted_revision IS NULL
              AND NOT (
                  object_kind = @objectKind
                  AND object_entity_id IS NOT DISTINCT FROM @objectEntityId
                  AND object_canonical_id IS NOT DISTINCT FROM @objectCanonicalId
                  AND object_value IS NOT DISTINCT FROM @objectValue);
            """;
        await using (var retract = new NpgsqlCommand(retractSql, connection, transaction))
        {
            AddActionFactParameters(retract, worldId, subjectEntityId, predicateId, value, revision);
            await retract.ExecuteNonQueryAsync(cancellationToken);
        }
        return await InsertActionFact(
            connection, transaction, worldId, subjectEntityId, predicateId,
            value, revision, sourceRuleBindingId, resultLifetime,
            cancellationToken);
    }

    private static async Task<string?> AdjustActionNumberFact(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        AuthorityMutation mutation,
        long revision,
        Guid? sourceRuleBindingId,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT fact_id, object_kind, object_canonical_id, object_value::text
            FROM world_facts
            WHERE world_id = @worldId AND subject_entity_id = @subjectEntityId
              AND predicate_id = @predicateId
              AND retracted_revision IS NULL
            FOR UPDATE;
            """;
        Guid factId;
        long current;
        await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("worldId", worldId);
            select.Parameters.AddWithValue("subjectEntityId", mutation.SubjectEntityId);
            select.Parameters.AddWithValue("predicateId", mutation.PredicateId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return "required_numeric_fact_missing";
            factId = reader.GetGuid(0);
            var kind = reader.GetString(1);
            var rawValue = kind == "canonical"
                ? (reader.IsDBNull(2) ? string.Empty : reader.GetString(2))
                : NormalizeAuthorityJsonValue(
                    reader.IsDBNull(3) ? null : reader.GetString(3));
            if (!long.TryParse(rawValue, out current) ||
                await reader.ReadAsync(cancellationToken))
                return "invalid_numeric_fact_state";
        }

        long next;
        try { next = checked(current + mutation.Delta); }
        catch (OverflowException) { return "numeric_effect_overflow"; }
        if (mutation.Minimum.HasValue) next = Math.Max(next, mutation.Minimum.Value);
        if (mutation.Maximum.HasValue) next = Math.Min(next, mutation.Maximum.Value);

        const string retractSql = """
            UPDATE world_facts SET retracted_revision = @revision
            WHERE fact_id = @factId AND retracted_revision IS NULL;
            """;
        await using (var retract = new NpgsqlCommand(retractSql, connection, transaction))
        {
            retract.Parameters.AddWithValue("factId", factId);
            retract.Parameters.AddWithValue("revision", revision);
            await retract.ExecuteNonQueryAsync(cancellationToken);
        }

        return await InsertActionFact(
            connection, transaction, worldId, mutation.SubjectEntityId,
            mutation.PredicateId, AuthorityObject.Number(next), revision,
            sourceRuleBindingId, mutation.ResultLifetime,
            cancellationToken);
    }

    private static void AddActionFactParameters(
        NpgsqlCommand command,
        Guid worldId,
        Guid subjectEntityId,
        string predicateId,
        AuthorityObject value,
        long revision)
    {
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("subjectEntityId", subjectEntityId);
        command.Parameters.AddWithValue("predicateId", predicateId);
        command.Parameters.AddWithValue("objectKind", value.Kind);
        command.Parameters.AddWithValue(
            "objectEntityId",
            value.EntityId.HasValue ? value.EntityId.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "objectCanonicalId",
            value.CanonicalId is null ? DBNull.Value : value.CanonicalId);
        var objectValue = command.Parameters.Add("objectValue", NpgsqlDbType.Jsonb);
        objectValue.Value = value.ValueJson is null ? DBNull.Value : value.ValueJson;
        command.Parameters.AddWithValue("revision", revision);
    }

    private static async Task<CommandApplyResult> MoveEntity(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<MoveEntityPayload>(JsonOptions);
        if (request is null || request.EntityId == Guid.Empty || !request.Transform.IsValid)
        {
            return CommandApplyResult.Rejected("invalid_move_entity_payload");
        }

        const string sql = """
            UPDATE world_entities
            SET position_x = @positionX, position_y = @positionY, position_z = @positionZ,
                rotation_x = @rotationX, rotation_y = @rotationY, rotation_z = @rotationZ,
                scale_x = @scaleX, scale_y = @scaleY, scale_z = @scaleZ, updated_at = now()
            WHERE entity_id = @entityId AND world_id = @worldId AND deleted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("entityId", request.EntityId);
        command.Parameters.AddWithValue("worldId", worldId);
        AddTransformParameters(command, request.Transform);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected == 1
            ? CommandApplyResult.Succeeded()
            : CommandApplyResult.Rejected("entity_not_found");
    }

    /// <summary>
    /// Soft-deletes one durable world entity and closes every active semantic
    /// contribution that refers to it in the same world revision. Historical
    /// rows remain available for event replay and audit. Account-owned player
    /// avatars use their own lifecycle and cannot be retired through the generic
    /// world-authoring command.
    /// </summary>
    private static async Task<CommandApplyResult> RetireEntity(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<RetireEntityPayload>(JsonOptions);
        if (request is null || request.EntityId == Guid.Empty)
            return CommandApplyResult.Rejected("invalid_retire_entity_payload");

        const string inspectSql = """
            SELECT EXISTS (
                       SELECT 1
                       FROM world_entities
                       WHERE world_id = @worldId
                         AND entity_id = @entityId
                         AND deleted_revision IS NULL),
                   EXISTS (
                       SELECT 1
                       FROM world_player_avatars
                       WHERE world_id = @worldId
                         AND entity_id = @entityId);
            """;
        await using (var inspect =
                     new NpgsqlCommand(inspectSql, connection, transaction))
        {
            inspect.Parameters.AddWithValue("worldId", worldId);
            inspect.Parameters.AddWithValue("entityId", request.EntityId);
            await using var reader =
                await inspect.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (!reader.GetBoolean(0))
                return CommandApplyResult.Rejected("entity_not_found");
            if (reader.GetBoolean(1))
                return CommandApplyResult.Rejected(
                    "player_avatar_retirement_forbidden");
        }

        const string retractFactsSql = """
            UPDATE world_facts
            SET retracted_revision = @revision
            WHERE world_id = @worldId
              AND retracted_revision IS NULL
              AND (subject_entity_id = @entityId
                   OR object_entity_id = @entityId);
            """;
        await using (var facts =
                     new NpgsqlCommand(
                         retractFactsSql,
                         connection,
                         transaction))
        {
            facts.Parameters.AddWithValue("worldId", worldId);
            facts.Parameters.AddWithValue("entityId", request.EntityId);
            facts.Parameters.AddWithValue("revision", revision);
            await facts.ExecuteNonQueryAsync(cancellationToken);
        }

        const string retractRulesSql = """
            UPDATE world_rule_bindings
            SET retracted_revision = @revision
            WHERE world_id = @worldId
              AND target_entity_id = @entityId
              AND retracted_revision IS NULL;
            """;
        await using (var rules =
                     new NpgsqlCommand(
                         retractRulesSql,
                         connection,
                         transaction))
        {
            rules.Parameters.AddWithValue("worldId", worldId);
            rules.Parameters.AddWithValue("entityId", request.EntityId);
            rules.Parameters.AddWithValue("revision", revision);
            await rules.ExecuteNonQueryAsync(cancellationToken);
        }

        const string closeMeaningPackagesSql = """
            UPDATE world_meaning_package_applications
            SET removed_revision = @revision
            WHERE world_id = @worldId
              AND target_entity_id = @entityId
              AND removed_revision IS NULL;
            """;
        await using (var packages =
                     new NpgsqlCommand(
                         closeMeaningPackagesSql,
                         connection,
                         transaction))
        {
            packages.Parameters.AddWithValue("worldId", worldId);
            packages.Parameters.AddWithValue("entityId", request.EntityId);
            packages.Parameters.AddWithValue("revision", revision);
            await packages.ExecuteNonQueryAsync(cancellationToken);
        }

        const string retireSql = """
            UPDATE world_entities
            SET deleted_revision = @revision,
                updated_at = now()
            WHERE world_id = @worldId
              AND entity_id = @entityId
              AND deleted_revision IS NULL;
            """;
        await using var retire =
            new NpgsqlCommand(retireSql, connection, transaction);
        retire.Parameters.AddWithValue("worldId", worldId);
        retire.Parameters.AddWithValue("entityId", request.EntityId);
        retire.Parameters.AddWithValue("revision", revision);
        var affected = await retire.ExecuteNonQueryAsync(cancellationToken);
        return affected == 1
            ? CommandApplyResult.Succeeded()
            : CommandApplyResult.Rejected("entity_not_found");
    }

    /// <summary>
    /// Establishes the durable ownership relationship between an authenticated
    /// player and one entity in a world. The relationship is authority/security
    /// metadata, not an ontology Fact or a rule.
    /// </summary>
    private static async Task<CommandApplyResult> RegisterPlayerAvatar(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        Guid actorUserId,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<RegisterPlayerAvatarPayload>(JsonOptions);
        if (request is null || request.EntityId == Guid.Empty)
        {
            return CommandApplyResult.Rejected("invalid_register_player_avatar_payload");
        }

        if (!await EntityExists(connection, transaction, worldId, request.EntityId, cancellationToken))
        {
            return CommandApplyResult.Rejected("avatar_entity_not_found");
        }

        const string sql = """
            INSERT INTO world_player_avatars
                (world_id, user_id, entity_id, registered_revision)
            VALUES
                (@worldId, @userId, @entityId, @revision)
            ON CONFLICT (world_id, user_id) DO UPDATE SET
                entity_id = EXCLUDED.entity_id,
                registered_revision = EXCLUDED.registered_revision,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("userId", actorUserId);
        command.Parameters.AddWithValue("entityId", request.EntityId);
        command.Parameters.AddWithValue("revision", revision);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return CommandApplyResult.Rejected("avatar_entity_already_assigned");
        }

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new { request.EntityId }, JsonOptions));
    }

    private static async Task<CommandApplyResult> SaveAvatarCheckpoint(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        Guid actorUserId,
        long revision,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<SaveAvatarCheckpointPayload>(JsonOptions);
        if (request is null || request.AvatarEntityId == Guid.Empty ||
            !request.Transform.IsValid ||
            (!string.IsNullOrWhiteSpace(request.ZoneKey) && !SemanticId.IsValid(request.ZoneKey)))
        {
            return CommandApplyResult.Rejected("invalid_avatar_checkpoint");
        }

        const string ownershipSql = """
            SELECT EXISTS (
                SELECT 1 FROM world_player_avatars
                WHERE world_id = @worldId AND user_id = @userId AND entity_id = @avatarEntityId);
            """;
        await using (var ownership = new NpgsqlCommand(ownershipSql, connection, transaction))
        {
            ownership.Parameters.AddWithValue("worldId", worldId);
            ownership.Parameters.AddWithValue("userId", actorUserId);
            ownership.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
            if (!(bool)(await ownership.ExecuteScalarAsync(cancellationToken))!)
                return CommandApplyResult.Rejected("avatar_not_registered");
        }

        if (!string.IsNullOrWhiteSpace(request.ZoneKey) &&
            !await ZoneExists(connection, transaction, worldId, request.ZoneKey, cancellationToken))
        {
            return CommandApplyResult.Rejected("unknown_zone");
        }

        const string checkpointSql = """
            INSERT INTO world_avatar_checkpoints
                (world_id, avatar_entity_id, zone_key,
                 position_x, position_y, position_z,
                 rotation_x, rotation_y, rotation_z, updated_revision)
            VALUES
                (@worldId, @avatarEntityId, @zoneKey,
                 @positionX, @positionY, @positionZ,
                 @rotationX, @rotationY, @rotationZ, @revision)
            ON CONFLICT (world_id, avatar_entity_id) DO UPDATE SET
                zone_key = EXCLUDED.zone_key,
                position_x = EXCLUDED.position_x,
                position_y = EXCLUDED.position_y,
                position_z = EXCLUDED.position_z,
                rotation_x = EXCLUDED.rotation_x,
                rotation_y = EXCLUDED.rotation_y,
                rotation_z = EXCLUDED.rotation_z,
                updated_revision = EXCLUDED.updated_revision,
                updated_at = now();
            """;
        await using (var checkpoint = new NpgsqlCommand(checkpointSql, connection, transaction))
        {
            checkpoint.Parameters.AddWithValue("worldId", worldId);
            checkpoint.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
            checkpoint.Parameters.AddWithValue("zoneKey", (object?)request.ZoneKey ?? DBNull.Value);
            checkpoint.Parameters.AddWithValue("positionX", request.Transform.PositionX);
            checkpoint.Parameters.AddWithValue("positionY", request.Transform.PositionY);
            checkpoint.Parameters.AddWithValue("positionZ", request.Transform.PositionZ);
            checkpoint.Parameters.AddWithValue("rotationX", request.Transform.RotationX);
            checkpoint.Parameters.AddWithValue("rotationY", request.Transform.RotationY);
            checkpoint.Parameters.AddWithValue("rotationZ", request.Transform.RotationZ);
            checkpoint.Parameters.AddWithValue("revision", revision);
            await checkpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        const string entitySql = """
            UPDATE world_entities
            SET zone_key = @zoneKey,
                position_x = @positionX, position_y = @positionY, position_z = @positionZ,
                rotation_x = @rotationX, rotation_y = @rotationY, rotation_z = @rotationZ,
                updated_at = now()
            WHERE world_id = @worldId AND entity_id = @avatarEntityId AND deleted_revision IS NULL;
            """;
        await using (var entity = new NpgsqlCommand(entitySql, connection, transaction))
        {
            entity.Parameters.AddWithValue("worldId", worldId);
            entity.Parameters.AddWithValue("avatarEntityId", request.AvatarEntityId);
            entity.Parameters.AddWithValue("zoneKey", (object?)request.ZoneKey ?? DBNull.Value);
            entity.Parameters.AddWithValue("positionX", request.Transform.PositionX);
            entity.Parameters.AddWithValue("positionY", request.Transform.PositionY);
            entity.Parameters.AddWithValue("positionZ", request.Transform.PositionZ);
            entity.Parameters.AddWithValue("rotationX", request.Transform.RotationX);
            entity.Parameters.AddWithValue("rotationY", request.Transform.RotationY);
            entity.Parameters.AddWithValue("rotationZ", request.Transform.RotationZ);
            if (await entity.ExecuteNonQueryAsync(cancellationToken) != 1)
                return CommandApplyResult.Rejected("avatar_entity_not_found");
        }

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            request.AvatarEntityId,
            request.ZoneKey,
            request.Transform
        }, JsonOptions));
    }

    /// <summary>
    /// Idempotent repair for retired or incomplete action-owned equipment
    /// relations. `equipped_by` is the current item-owned equipment truth.
    /// The command removes retired `equips` facts and an `attacks_with` fact
    /// whose tool no longer has the matching `equipped_by` relation. It never
    /// manufactures equipment state from stale action history.
    /// </summary>
    private static async Task<CommandApplyResult> MigrateLegacyEquipmentRelations(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        long revision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE world_facts AS stale
            SET retracted_revision = @revision
            WHERE stale.world_id = @worldId
              AND stale.source_type = 'action'
              AND stale.retracted_revision IS NULL
              AND (
                    stale.predicate_id = 'equips'
                    OR (
                        stale.predicate_id = 'attacks_with'
                        AND (
                            stale.object_kind <> 'entity'
                            OR stale.object_entity_id IS NULL
                            OR NOT EXISTS (
                                SELECT 1
                                FROM world_facts AS equipment
                                WHERE equipment.world_id = stale.world_id
                                  AND equipment.subject_entity_id =
                                      stale.object_entity_id
                                  AND equipment.predicate_id = 'equipped_by'
                                  AND equipment.object_kind = 'entity'
                                  AND equipment.object_entity_id =
                                      stale.subject_entity_id
                                  AND equipment.retracted_revision IS NULL
                            )
                        )
                    )
                  );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        var retractedCount = await command.ExecuteNonQueryAsync(cancellationToken);
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(
            new { retractedCount },
            JsonOptions));
    }

    /// <summary>
    /// Assigns a legacy unzoned entity only when its durable X/Z position falls
    /// inside exactly one authored zone. Ambiguous or out-of-bounds entities
    /// remain unassigned so the Authority never guesses ownership.
    /// </summary>
    private static async Task<CommandApplyResult> MigrateUnassignedEntityZones(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH candidates AS (
                SELECT entity.entity_id, min(zone.zone_key) AS zone_key
                FROM world_entities AS entity
                INNER JOIN world_zones AS zone
                        ON zone.world_id = entity.world_id
                       AND entity.position_x >= zone.min_x
                       AND entity.position_x <= zone.max_x
                       AND entity.position_z >= zone.min_z
                       AND entity.position_z <= zone.max_z
                WHERE entity.world_id = @worldId
                  AND entity.deleted_revision IS NULL
                  AND (entity.zone_key IS NULL OR btrim(entity.zone_key) = '')
                GROUP BY entity.entity_id
                HAVING count(*) = 1
            )
            UPDATE world_entities AS entity
            SET zone_key = candidates.zone_key,
                updated_at = now()
            FROM candidates
            WHERE entity.entity_id = candidates.entity_id
              AND entity.world_id = @worldId
              AND entity.deleted_revision IS NULL
              AND (entity.zone_key IS NULL OR btrim(entity.zone_key) = '');
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        var migratedCount =
            await command.ExecuteNonQueryAsync(cancellationToken);
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(
            new { migratedCount },
            JsonOptions));
    }

    private static async Task<CommandApplyResult> SetAuthoredFact(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<SetAuthoredFactPayload>(JsonOptions);
        if (request is null || request.SubjectEntityId == Guid.Empty || !SemanticId.IsValid(request.PredicateId)
            || !request.IsValidObject())
        {
            return CommandApplyResult.Rejected("invalid_set_fact_payload");
        }

        if (!await EntityExists(connection, transaction, worldId, request.SubjectEntityId, cancellationToken)
            || (request.ObjectKind == "entity" && !await EntityExists(connection, transaction, worldId, request.ObjectEntityId!.Value, cancellationToken)))
        {
            return CommandApplyResult.Rejected("fact_entity_not_found");
        }

        const string sql = """
            INSERT INTO world_facts
                (world_id, subject_entity_id, predicate_id, object_kind, object_entity_id,
                 object_canonical_id, object_value, source_type, created_revision)
            VALUES
                (@worldId, @subjectEntityId, @predicateId, @objectKind, @objectEntityId,
                 @objectCanonicalId, @objectValue, 'authored', @revision);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("subjectEntityId", request.SubjectEntityId);
        command.Parameters.AddWithValue("predicateId", request.PredicateId);
        command.Parameters.AddWithValue("objectKind", request.ObjectKind);
        command.Parameters.AddWithValue("objectEntityId", (object?)request.ObjectEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue("objectCanonicalId", (object?)request.ObjectCanonicalId ?? DBNull.Value);
        command.Parameters.AddWithValue("objectValue", NpgsqlDbType.Jsonb, (object?)request.ObjectValueJson ?? DBNull.Value);
        command.Parameters.AddWithValue("revision", revision);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return CommandApplyResult.Rejected("fact_already_exists");
        }
        return CommandApplyResult.Succeeded();
    }

    private static async Task<CommandApplyResult> RetractAuthoredFact(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<RetractAuthoredFactPayload>(JsonOptions);
        if (request is null || request.FactId == Guid.Empty)
        {
            return CommandApplyResult.Rejected("invalid_retract_fact_payload");
        }

        const string sql = """
            UPDATE world_facts
            SET retracted_revision = @revision
            WHERE fact_id = @factId AND world_id = @worldId AND source_type = 'authored'
              AND retracted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("factId", request.FactId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1
            ? CommandApplyResult.Succeeded()
            : CommandApplyResult.Rejected("fact_not_found");
    }

    private static async Task<CommandApplyResult> AddRuleBlock(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<AddRuleBlockPayload>(JsonOptions);
        if (request is null || request.TargetEntityId == Guid.Empty || !SemanticId.IsValid(request.RuleId) || request.RuleVersion <= 0)
        {
            return CommandApplyResult.Rejected("invalid_add_rule_block_payload");
        }
        if (!await PublishedRuleExists(
                connection, transaction, request.RuleId, request.RuleVersion, cancellationToken))
        {
            return CommandApplyResult.Rejected("rule_definition_not_published");
        }
        if (!await EntityExists(connection, transaction, worldId, request.TargetEntityId, cancellationToken))
        {
            return CommandApplyResult.Rejected("entity_not_found");
        }

        var parametersJson = request.ParameterValuesJson ?? "{}";
        try
        {
            using var parametersDocument = JsonDocument.Parse(parametersJson);
            if (parametersDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return CommandApplyResult.Rejected("invalid_rule_binding_parameters");
            }
            parametersJson = parametersDocument.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return CommandApplyResult.Rejected("invalid_rule_binding_parameters");
        }

        var bindingId = request.BindingId == Guid.Empty ? Guid.NewGuid() : request.BindingId;
        const string sql = """
            INSERT INTO world_rule_bindings
                (binding_id, world_id, target_entity_id, rule_id, rule_version,
                 enabled, parameter_values, created_revision)
            VALUES
                (@bindingId, @worldId, @targetEntityId, @ruleId, @ruleVersion,
                 true, @parameters, @revision);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("bindingId", bindingId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("targetEntityId", request.TargetEntityId);
        command.Parameters.AddWithValue("ruleId", request.RuleId);
        command.Parameters.AddWithValue("ruleVersion", request.RuleVersion);
        command.Parameters.AddWithValue("parameters", NpgsqlDbType.Jsonb, parametersJson);
        command.Parameters.AddWithValue("revision", revision);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return CommandApplyResult.Rejected("rule_binding_already_exists");
        }
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new { bindingId, request.RuleId, request.RuleVersion }, JsonOptions));
    }

    private static async Task<CommandApplyResult> RemoveRuleBlock(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, JsonElement payload, long revision, CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<RemoveRuleBlockPayload>(JsonOptions);
        if (request is null || request.BindingId == Guid.Empty)
        {
            return CommandApplyResult.Rejected("invalid_remove_rule_block_payload");
        }

        // Package-owned bindings remain independently removable. Shared authored
        // meaning stays active while sibling bindings remain; removing the final
        // binding closes the package and retracts its owned semantic contribution.
        var package = await LoadMeaningPackageByBindingId(
            connection, transaction, worldId, request.BindingId,
            cancellationToken);
        if (package is not null)
        {
            if (package.InsertedBindingIds.Count > 1)
            {
                await RemoveMeaningPackageRuleBinding(
                    connection, transaction, worldId, package,
                    request.BindingId, revision, cancellationToken);
                return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
                {
                    request.BindingId,
                    package.ApplicationId,
                    package.PackageId,
                    remainingRuleBlockCount =
                        package.InsertedBindingIds.Count - 1
                }, JsonOptions));
            }

            await RemoveMeaningPackageApplication(
                connection, transaction, worldId, package, revision,
                cancellationToken);
            return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
            {
                request.BindingId,
                removedMeaningPackageApplicationId = package.ApplicationId,
                package.PackageId
            }, JsonOptions));
        }

        await RetractRuleBindingResults(
            connection,
            transaction,
            worldId,
            [request.BindingId],
            revision,
            cancellationToken);
        const string sql = """
            UPDATE world_rule_bindings
            SET retracted_revision = @revision
            WHERE binding_id = @bindingId AND world_id = @worldId AND retracted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("bindingId", request.BindingId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
            return CommandApplyResult.Succeeded();

        // Repeated removals are already converged and must not race a
        // client-side rollback against the latest projection.
        const string historicalSql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_rule_bindings
                WHERE binding_id = @bindingId AND world_id = @worldId);
            """;
        await using var historical =
            new NpgsqlCommand(historicalSql, connection, transaction);
        historical.Parameters.AddWithValue("bindingId", request.BindingId);
        historical.Parameters.AddWithValue("worldId", worldId);
        return (bool)(await historical.ExecuteScalarAsync(cancellationToken))!
            ? CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
            {
                request.BindingId,
                alreadyRemoved = true
            }, JsonOptions))
            : CommandApplyResult.Rejected("rule_binding_not_found");
    }

    private static async Task RemoveMeaningPackageRuleBinding(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        ActiveMeaningPackage application,
        Guid bindingId,
        long revision,
        CancellationToken cancellationToken)
    {
        await RetractRuleBindingResults(
            connection,
            transaction,
            worldId,
            [bindingId],
            revision,
            cancellationToken);
        await RetractMeaningRuleBinding(
            connection,
            transaction,
            worldId,
            bindingId,
            revision,
            cancellationToken);

        var remainingBindingIds = application.InsertedBindingIds
            .Where(value => value != bindingId)
            .ToArray();
        const string updateApplicationSql = """
            UPDATE world_meaning_package_applications
            SET inserted_binding_ids = @bindingIds
            WHERE application_id = @applicationId
              AND world_id = @worldId
              AND removed_revision IS NULL;
            """;
        await using var updateApplication =
            new NpgsqlCommand(
                updateApplicationSql,
                connection,
                transaction);
        updateApplication.Parameters.AddWithValue(
            "bindingIds",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(remainingBindingIds, JsonOptions));
        updateApplication.Parameters.AddWithValue(
            "applicationId",
            application.ApplicationId);
        updateApplication.Parameters.AddWithValue("worldId", worldId);
        await updateApplication.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Applies or removes one complete semantic contribution. The authority owns
    /// the atomic boundary: displaced authored triples and Rule Blocks are kept in
    /// the application ledger and restored when the package is removed/replaced.
    /// No template, prefab, mesh, or object-name compatibility branch exists here.
    /// </summary>
    private static async Task<CommandApplyResult> ApplyMeaningPackage(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        JsonElement payload,
        long revision,
        CancellationToken cancellationToken)
    {
        ApplyMeaningPackagePayload? request;
        try
        {
            request =
                payload.Deserialize<ApplyMeaningPackagePayload>(JsonOptions);
        }
        catch (JsonException)
        {
            // Malformed optional GUIDs and other payload-shape errors are
            // completed Authority rejections. They must never surface as a
            // transport-looking HTTP 500 that leaves the durable client outbox
            // permanently blocking world entry.
            return CommandApplyResult.Rejected(
                "invalid_meaning_package_payload");
        }
        var rejectionCode = "invalid_meaning_package_payload";
        if (request is null || !request.IsValid(out rejectionCode))
            return CommandApplyResult.Rejected(rejectionCode);
        if (!await EntityExists(
                connection, transaction, worldId, request.TargetEntityId,
                cancellationToken))
            return CommandApplyResult.Rejected("entity_not_found");

        var active = await LoadActiveMeaningPackage(
            connection, transaction, worldId, request.TargetEntityId,
            request.SlotId, cancellationToken);
        if (request.Operation == "remove")
        {
            if (active is null ||
                (request.ApplicationId != Guid.Empty &&
                 active.ApplicationId != request.ApplicationId))
                return CommandApplyResult.Rejected("meaning_package_not_found");

            await RemoveMeaningPackageApplication(
                connection, transaction, worldId, active, revision,
                cancellationToken);
            return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
            {
                operation = "remove",
                active.ApplicationId,
                request.TargetEntityId,
                request.SlotId,
                active.PackageId
            }, JsonOptions));
        }

        // Template semantic baselines use a deterministic application identity.
        // A retry for the same immutable package is already converged and must
        // not remove/reinsert its Facts and Rule Blocks under a new revision.
        if (active is not null &&
            active.ApplicationId == request.ApplicationId &&
            string.Equals(
                active.PackageId,
                request.PackageId,
                StringComparison.Ordinal))
        {
            return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
            {
                operation = "unchanged",
                active.ApplicationId,
                request.TargetEntityId,
                request.SlotId,
                active.PackageId
            }, JsonOptions));
        }

        foreach (var binding in request.RuleBlocks ?? [])
        {
            if (!await PublishedRuleExists(
                    connection, transaction, binding.RuleId,
                    binding.RuleVersion, cancellationToken))
                return CommandApplyResult.Rejected(
                    "rule_definition_not_published");
        }

        // Replacing a slot first restores its prior baseline inside this same
        // transaction/revision, then captures that baseline for the new package.
        if (active is not null)
        {
            await RemoveMeaningPackageApplication(
                connection, transaction, worldId, active, revision,
                cancellationToken);
        }

        var displacedFacts = new List<MeaningFactSnapshot>();
        foreach (var predicate in request.ReplacePredicateIds ?? [])
        {
            displacedFacts.AddRange(await RetractAuthoredFactsByPredicate(
                connection, transaction, worldId, request.TargetEntityId,
                predicate, revision, cancellationToken));
        }

        var insertedFactIds = new List<Guid>();
        var desiredFacts = new List<InitialAuthoredFactPayload>();
        desiredFacts.AddRange((request.RequiredConceptIds ?? [])
            .Select(concept => new InitialAuthoredFactPayload(
                "has_concept", "canonical", null, concept, null)));
        desiredFacts.AddRange(request.AuthoredFacts ?? []);
        foreach (var fact in desiredFacts
                     .GroupBy(MeaningFactKey)
                     .Select(group => group.First()))
        {
            var factId = await InsertMeaningFactIfMissing(
                connection, transaction, worldId, request.TargetEntityId,
                fact, revision, cancellationToken);
            if (factId.HasValue)
            {
                insertedFactIds.Add(factId.Value);
                continue;
            }

            if (!request.AdoptExistingContributions) continue;
            var adoptedFactId = await FindAdoptableMeaningFactId(
                connection, transaction, worldId, request.TargetEntityId,
                fact, cancellationToken);
            if (adoptedFactId.HasValue)
                insertedFactIds.Add(adoptedFactId.Value);
        }

        var insertedBindingIds = new List<Guid>();
        foreach (var binding in request.RuleBlocks ?? [])
        {
            var parametersJson = binding.NormalizedParametersJson();
            if (await MeaningRuleBindingExists(
                    connection, transaction, worldId, request.TargetEntityId,
                    binding.RuleId, binding.RuleVersion, parametersJson,
                    cancellationToken))
            {
                if (request.AdoptExistingContributions)
                {
                    var adoptedBindingId =
                        await FindAdoptableMeaningRuleBindingId(
                            connection, transaction, worldId,
                            request.TargetEntityId, binding.RuleId,
                            binding.RuleVersion, parametersJson,
                            cancellationToken);
                    if (adoptedBindingId.HasValue)
                        insertedBindingIds.Add(adoptedBindingId.Value);
                }
                continue;
            }

            if (request.AdoptExistingContributions)
            {
                var legacyBindingId =
                    await FindAdoptableMeaningRuleBindingIdIgnoringVersion(
                        connection, transaction, worldId,
                        request.TargetEntityId, binding.RuleId,
                        parametersJson, cancellationToken);
                if (legacyBindingId.HasValue)
                {
                    // Catalog baselines created before package ownership may
                    // still reference an older immutable Rule Definition.
                    // Retire that binding and its inferred results inside this
                    // revision, then insert and own the requested version.
                    await RetractRuleBindingResults(
                        connection,
                        transaction,
                        worldId,
                        [legacyBindingId.Value],
                        revision,
                        cancellationToken);
                    await RetractMeaningRuleBinding(
                        connection,
                        transaction,
                        worldId,
                        legacyBindingId.Value,
                        revision,
                        cancellationToken);
                }
                else if (await MeaningRuleBindingExistsIgnoringVersion(
                             connection, transaction, worldId,
                             request.TargetEntityId, binding.RuleId,
                             parametersJson, cancellationToken))
                {
                    // An active package owns the semantic binding. Do not
                    // duplicate or steal its gameplay contribution.
                    continue;
                }
            }

            var bindingId = binding.BindingId == Guid.Empty
                ? Guid.NewGuid()
                : binding.BindingId;
            if (await MeaningRuleBindingIdExists(
                    connection, transaction, bindingId,
                    cancellationToken))
            {
                // Binding rows are immutable history. A deterministic ID may
                // already belong to the retired legacy version, so a new
                // version receives a new identity instead of overwriting it.
                bindingId = Guid.NewGuid();
            }
            const string insertBindingSql = """
                INSERT INTO world_rule_bindings
                    (binding_id, world_id, target_entity_id, rule_id,
                     rule_version, enabled, parameter_values, created_revision)
                VALUES
                    (@bindingId, @worldId, @targetEntityId, @ruleId,
                     @ruleVersion, true, @parameters, @revision);
                """;
            await using var command =
                new NpgsqlCommand(insertBindingSql, connection, transaction);
            command.Parameters.AddWithValue("bindingId", bindingId);
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue(
                "targetEntityId", request.TargetEntityId);
            command.Parameters.AddWithValue("ruleId", binding.RuleId);
            command.Parameters.AddWithValue(
                "ruleVersion", binding.RuleVersion);
            command.Parameters.AddWithValue(
                "parameters", NpgsqlDbType.Jsonb, parametersJson);
            command.Parameters.AddWithValue("revision", revision);
            await command.ExecuteNonQueryAsync(cancellationToken);
            insertedBindingIds.Add(bindingId);
        }

        const string insertApplicationSql = """
            INSERT INTO world_meaning_package_applications
                (application_id, world_id, target_entity_id, slot_id, package_id,
                 package_spec, inserted_fact_ids, inserted_binding_ids,
                 displaced_facts, displaced_bindings, applied_revision)
            VALUES
                (@applicationId, @worldId, @targetEntityId, @slotId, @packageId,
                 @packageSpec, @insertedFactIds, @insertedBindingIds,
                 @displacedFacts, '[]'::jsonb, @revision);
            """;
        await using (var application =
                     new NpgsqlCommand(
                         insertApplicationSql, connection, transaction))
        {
            application.Parameters.AddWithValue(
                "applicationId", request.ApplicationId);
            application.Parameters.AddWithValue("worldId", worldId);
            application.Parameters.AddWithValue(
                "targetEntityId", request.TargetEntityId);
            application.Parameters.AddWithValue("slotId", request.SlotId);
            application.Parameters.AddWithValue("packageId", request.PackageId);
            application.Parameters.AddWithValue(
                "packageSpec", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(request, JsonOptions));
            application.Parameters.AddWithValue(
                "insertedFactIds", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(insertedFactIds, JsonOptions));
            application.Parameters.AddWithValue(
                "insertedBindingIds", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(insertedBindingIds, JsonOptions));
            application.Parameters.AddWithValue(
                "displacedFacts", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(displacedFacts, JsonOptions));
            application.Parameters.AddWithValue("revision", revision);
            await application.ExecuteNonQueryAsync(cancellationToken);
        }

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            operation = "apply",
            request.ApplicationId,
            request.TargetEntityId,
            request.SlotId,
            request.PackageId,
            insertedFactCount = insertedFactIds.Count,
            insertedRuleBlockCount = insertedBindingIds.Count,
            displacedFactCount = displacedFacts.Count
        }, JsonOptions));
    }

    private static async Task<ActiveMeaningPackage?> LoadActiveMeaningPackage(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid targetEntityId,
        string slotId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT application_id, package_id, inserted_fact_ids::text,
                   inserted_binding_ids::text, displaced_facts::text,
                   displaced_bindings::text
            FROM world_meaning_package_applications
            WHERE world_id = @worldId AND target_entity_id = @targetEntityId
              AND slot_id = @slotId AND removed_revision IS NULL
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("slotId", slotId);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ActiveMeaningPackage(
            reader.GetGuid(0),
            reader.GetString(1),
            JsonSerializer.Deserialize<List<Guid>>(
                reader.GetString(2), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<Guid>>(
                reader.GetString(3), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<MeaningFactSnapshot>>(
                reader.GetString(4), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<MeaningRuleBindingSnapshot>>(
                reader.GetString(5), JsonOptions) ?? []);
    }

    private static async Task<ActiveMeaningPackage?>
        LoadMeaningPackageByBindingId(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid bindingId,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT application_id, package_id, inserted_fact_ids::text,
                   inserted_binding_ids::text, displaced_facts::text,
                   displaced_bindings::text
            FROM world_meaning_package_applications
            WHERE world_id = @worldId
              AND inserted_binding_ids @> @bindingIds::jsonb
              AND removed_revision IS NULL
            FOR UPDATE;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "bindingIds",
            JsonSerializer.Serialize(new[] { bindingId }, JsonOptions));
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ActiveMeaningPackage(
            reader.GetGuid(0),
            reader.GetString(1),
            JsonSerializer.Deserialize<List<Guid>>(
                reader.GetString(2), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<Guid>>(
                reader.GetString(3), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<MeaningFactSnapshot>>(
                reader.GetString(4), JsonOptions) ?? [],
            JsonSerializer.Deserialize<List<MeaningRuleBindingSnapshot>>(
                reader.GetString(5), JsonOptions) ?? []);
    }

    private static async Task RemoveMeaningPackageApplication(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        ActiveMeaningPackage application,
        long revision,
        CancellationToken cancellationToken)
    {
        if (application.InsertedFactIds.Count > 0)
        {
            const string retractFactsSql = """
                UPDATE world_facts SET retracted_revision = @revision
                WHERE world_id = @worldId
                  AND fact_id = ANY(@factIds)
                  AND retracted_revision IS NULL;
                """;
            await using var command =
                new NpgsqlCommand(retractFactsSql, connection, transaction);
            command.Parameters.AddWithValue("revision", revision);
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue(
                "factIds", application.InsertedFactIds.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (application.InsertedBindingIds.Count > 0)
        {
            await RetractRuleBindingResults(
                connection,
                transaction,
                worldId,
                application.InsertedBindingIds,
                revision,
                cancellationToken);
            const string retractBindingsSql = """
                UPDATE world_rule_bindings SET retracted_revision = @revision
                WHERE world_id = @worldId
                  AND binding_id = ANY(@bindingIds)
                  AND retracted_revision IS NULL;
                """;
            await using var command =
                new NpgsqlCommand(
                    retractBindingsSql, connection, transaction);
            command.Parameters.AddWithValue("revision", revision);
            command.Parameters.AddWithValue("worldId", worldId);
            command.Parameters.AddWithValue(
                "bindingIds", application.InsertedBindingIds.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var fact in application.DisplacedFacts)
        {
            await InsertMeaningFactIfMissing(
                connection, transaction, worldId, fact.SubjectEntityId,
                fact.ToInitialPayload(), revision, cancellationToken);
        }
        foreach (var binding in application.DisplacedBindings)
        {
            if (await MeaningRuleBindingExists(
                    connection, transaction, worldId, binding.TargetEntityId,
                    binding.RuleId, binding.RuleVersion,
                    binding.ParameterValuesJson, cancellationToken))
                continue;
            const string restoreBindingSql = """
                INSERT INTO world_rule_bindings
                    (binding_id, world_id, target_entity_id, rule_id,
                     rule_version, enabled, parameter_values, created_revision)
                VALUES
                    (@bindingId, @worldId, @targetEntityId, @ruleId,
                     @ruleVersion, true, @parameters, @revision);
                """;
            await using var restore =
                new NpgsqlCommand(
                    restoreBindingSql, connection, transaction);
            restore.Parameters.AddWithValue("bindingId", Guid.NewGuid());
            restore.Parameters.AddWithValue("worldId", worldId);
            restore.Parameters.AddWithValue(
                "targetEntityId", binding.TargetEntityId);
            restore.Parameters.AddWithValue("ruleId", binding.RuleId);
            restore.Parameters.AddWithValue(
                "ruleVersion", binding.RuleVersion);
            restore.Parameters.AddWithValue(
                "parameters", NpgsqlDbType.Jsonb,
                binding.ParameterValuesJson);
            restore.Parameters.AddWithValue("revision", revision);
            await restore.ExecuteNonQueryAsync(cancellationToken);
        }

        const string removeSql = """
            UPDATE world_meaning_package_applications
            SET removed_revision = @revision
            WHERE application_id = @applicationId
              AND world_id = @worldId AND removed_revision IS NULL;
            """;
        await using var remove =
            new NpgsqlCommand(removeSql, connection, transaction);
        remove.Parameters.AddWithValue(
            "applicationId", application.ApplicationId);
        remove.Parameters.AddWithValue("worldId", worldId);
        remove.Parameters.AddWithValue("revision", revision);
        await remove.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RetractRuleBindingResults(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        IReadOnlyCollection<Guid> bindingIds,
        long revision,
        CancellationToken cancellationToken)
    {
        if (bindingIds.Count == 0) return;
        const string sql = """
            UPDATE world_facts
            SET retracted_revision = @revision
            WHERE world_id = @worldId
              AND source_rule_binding_id = ANY(@bindingIds)
              AND rule_result_lifetime = 'rule_bound'
              AND retracted_revision IS NULL;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "bindingIds",
            bindingIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<MeaningFactSnapshot>>
        RetractAuthoredFactsByPredicate(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid targetEntityId,
            string predicateId,
            long revision,
            CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT subject_entity_id, predicate_id, object_kind,
                   object_entity_id, object_canonical_id, object_value::text
            FROM world_facts
            WHERE world_id = @worldId
              AND subject_entity_id = @targetEntityId
              AND predicate_id = @predicateId
              AND source_type = 'authored'
              AND retracted_revision IS NULL
            FOR UPDATE;
            """;
        var snapshots = new List<MeaningFactSnapshot>();
        await using (var select =
                     new NpgsqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("worldId", worldId);
            select.Parameters.AddWithValue("targetEntityId", targetEntityId);
            select.Parameters.AddWithValue("predicateId", predicateId);
            await using var reader =
                await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                snapshots.Add(new MeaningFactSnapshot(
                    reader.GetGuid(0), reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }
        if (snapshots.Count == 0) return snapshots;

        const string retractSql = """
            UPDATE world_facts SET retracted_revision = @revision
            WHERE world_id = @worldId
              AND subject_entity_id = @targetEntityId
              AND predicate_id = @predicateId
              AND source_type = 'authored'
              AND retracted_revision IS NULL;
            """;
        await using var retract =
            new NpgsqlCommand(retractSql, connection, transaction);
        retract.Parameters.AddWithValue("revision", revision);
        retract.Parameters.AddWithValue("worldId", worldId);
        retract.Parameters.AddWithValue("targetEntityId", targetEntityId);
        retract.Parameters.AddWithValue("predicateId", predicateId);
        await retract.ExecuteNonQueryAsync(cancellationToken);
        return snapshots;
    }

    private static async Task<Guid?> InsertMeaningFactIfMissing(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid subjectEntityId,
        InitialAuthoredFactPayload fact,
        long revision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO world_facts
                (world_id, subject_entity_id, predicate_id, object_kind,
                 object_entity_id, object_canonical_id, object_value,
                 source_type, created_revision)
            VALUES
                (@worldId, @subjectEntityId, @predicateId, @objectKind,
                 @objectEntityId, @objectCanonicalId, @objectValue,
                 'authored', @revision)
            ON CONFLICT DO NOTHING
            RETURNING fact_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("subjectEntityId", subjectEntityId);
        command.Parameters.AddWithValue("predicateId", fact.PredicateId);
        command.Parameters.AddWithValue("objectKind", fact.ObjectKind);
        command.Parameters.AddWithValue(
            "objectEntityId", (object?)fact.ObjectEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "objectCanonicalId",
            (object?)fact.ObjectCanonicalId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "objectValue", NpgsqlDbType.Jsonb,
            (object?)fact.ObjectValueJson ?? DBNull.Value);
        command.Parameters.AddWithValue("revision", revision);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid factId ? factId : null;
    }

    /// <summary>
    /// Finds an exact authored Fact that predates meaning-package ownership.
    /// Only unclaimed rows can be adopted, so one package cannot retract a
    /// contribution owned by another active package.
    /// </summary>
    private static async Task<Guid?> FindAdoptableMeaningFactId(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid subjectEntityId,
        InitialAuthoredFactPayload fact,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fact.fact_id
            FROM world_facts fact
            WHERE fact.world_id = @worldId
              AND fact.subject_entity_id = @subjectEntityId
              AND fact.predicate_id = @predicateId
              AND fact.object_kind = @objectKind
              AND fact.object_entity_id IS NOT DISTINCT FROM @objectEntityId
              AND fact.object_canonical_id IS NOT DISTINCT FROM @objectCanonicalId
              AND fact.object_value IS NOT DISTINCT FROM @objectValue
              AND fact.source_type = 'authored'
              AND fact.retracted_revision IS NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM world_meaning_package_applications package
                  WHERE package.world_id = @worldId
                    AND package.removed_revision IS NULL
                    AND package.inserted_fact_ids
                        @> jsonb_build_array(fact.fact_id))
            ORDER BY fact.created_revision, fact.fact_id
            LIMIT 1
            FOR UPDATE OF fact;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "subjectEntityId", subjectEntityId);
        command.Parameters.AddWithValue("predicateId", fact.PredicateId);
        command.Parameters.AddWithValue("objectKind", fact.ObjectKind);
        command.Parameters.AddWithValue(
            "objectEntityId", (object?)fact.ObjectEntityId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "objectCanonicalId",
            (object?)fact.ObjectCanonicalId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "objectValue", NpgsqlDbType.Jsonb,
            (object?)fact.ObjectValueJson ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid factId ? factId : null;
    }

    private static async Task<bool> MeaningRuleBindingExists(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid targetEntityId,
        string ruleId,
        int ruleVersion,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM world_rule_bindings
                WHERE world_id = @worldId
                  AND target_entity_id = @targetEntityId
                  AND rule_id = @ruleId
                  AND rule_version = @ruleVersion
                  AND parameter_values = @parameters::jsonb
                  AND enabled AND retracted_revision IS NULL);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue("ruleVersion", ruleVersion);
        command.Parameters.AddWithValue("parameters", parametersJson);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Finds an exact active Rule Block binding that has not already been
    /// claimed by another meaning package.
    /// </summary>
    private static async Task<Guid?> FindAdoptableMeaningRuleBindingId(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid targetEntityId,
        string ruleId,
        int ruleVersion,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT binding.binding_id
            FROM world_rule_bindings binding
            WHERE binding.world_id = @worldId
              AND binding.target_entity_id = @targetEntityId
              AND binding.rule_id = @ruleId
              AND binding.rule_version = @ruleVersion
              AND binding.parameter_values = @parameters::jsonb
              AND binding.enabled
              AND binding.retracted_revision IS NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM world_meaning_package_applications package
                  WHERE package.world_id = @worldId
                    AND package.removed_revision IS NULL
                    AND package.inserted_binding_ids
                        @> jsonb_build_array(binding.binding_id))
            ORDER BY binding.created_revision, binding.binding_id
            LIMIT 1
            FOR UPDATE OF binding;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue("ruleVersion", ruleVersion);
        command.Parameters.AddWithValue(
            "parameters", NpgsqlDbType.Jsonb, parametersJson);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid bindingId ? bindingId : null;
    }

    private static async Task<Guid?>
        FindAdoptableMeaningRuleBindingIdIgnoringVersion(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid worldId,
            Guid targetEntityId,
            string ruleId,
            string parametersJson,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT binding.binding_id
            FROM world_rule_bindings binding
            WHERE binding.world_id = @worldId
              AND binding.target_entity_id = @targetEntityId
              AND binding.rule_id = @ruleId
              AND binding.parameter_values = @parameters::jsonb
              AND binding.enabled
              AND binding.retracted_revision IS NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM world_meaning_package_applications package
                  WHERE package.world_id = @worldId
                    AND package.removed_revision IS NULL
                    AND package.inserted_binding_ids
                        @> jsonb_build_array(binding.binding_id))
            ORDER BY binding.created_revision, binding.binding_id
            LIMIT 1
            FOR UPDATE OF binding;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue(
            "parameters", NpgsqlDbType.Jsonb, parametersJson);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid bindingId ? bindingId : null;
    }

    private static async Task<bool> MeaningRuleBindingExistsIgnoringVersion(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid targetEntityId,
        string ruleId,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_rule_bindings
                WHERE world_id = @worldId
                  AND target_entity_id = @targetEntityId
                  AND rule_id = @ruleId
                  AND parameter_values = @parameters::jsonb
                  AND enabled
                  AND retracted_revision IS NULL);
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue(
            "targetEntityId", targetEntityId);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue(
            "parameters", NpgsqlDbType.Jsonb, parametersJson);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> MeaningRuleBindingIdExists(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_rule_bindings
                WHERE binding_id = @bindingId);
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("bindingId", bindingId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task RetractMeaningRuleBinding(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid worldId,
        Guid bindingId,
        long revision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE world_rule_bindings
            SET retracted_revision = @revision
            WHERE world_id = @worldId
              AND binding_id = @bindingId
              AND retracted_revision IS NULL;
            """;
        await using var command =
            new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("bindingId", bindingId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string MeaningFactKey(InitialAuthoredFactPayload fact) =>
        string.Join("\u001f", fact.PredicateId, fact.ObjectKind,
            fact.ObjectEntityId?.ToString("D") ?? string.Empty,
            fact.ObjectCanonicalId ?? string.Empty,
            fact.ObjectValueJson ?? string.Empty);

    private static async Task AcquireWorldLock(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@worldId::text, 0));", connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> CanAccessWorld(
        NpgsqlConnection connection,
        Guid worldId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM world_members
                WHERE world_id = @worldId AND user_id = @actorUserId
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("actorUserId", actorUserId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddProjectionZoneParameter(NpgsqlCommand command, string? zoneKey)
    {
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            command.Parameters.AddWithValue("zoneKey", zoneKey);
        }
    }

    private static async Task<string?> GetEntityZoneKey(
        NpgsqlConnection connection,
        Guid worldId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT zone_key
            FROM world_entities
            WHERE world_id = @worldId AND entity_id = @entityId AND deleted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("entityId", entityId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (string)result;
    }

    private static async Task<string?> GetFactZoneKey(
        NpgsqlConnection connection,
        Guid worldId,
        Guid? factId,
        CancellationToken cancellationToken)
    {
        if (!factId.HasValue || factId.Value == Guid.Empty)
        {
            return null;
        }

        const string sql = """
            SELECT e.zone_key
            FROM world_facts f
            INNER JOIN world_entities e ON e.entity_id = f.subject_entity_id
            WHERE f.world_id = @worldId AND f.fact_id = @factId;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("factId", factId.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (string)result;
    }

    private static async Task<string?> GetRuleBindingZoneKey(
        NpgsqlConnection connection,
        Guid worldId,
        Guid? bindingId,
        CancellationToken cancellationToken)
    {
        if (!bindingId.HasValue || bindingId.Value == Guid.Empty)
        {
            return null;
        }

        const string sql = """
            SELECT e.zone_key
            FROM world_rule_bindings b
            INNER JOIN world_entities e ON e.entity_id = b.target_entity_id
            WHERE b.world_id = @worldId AND b.binding_id = @bindingId;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("bindingId", bindingId.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (string)result;
    }

    private static async Task<WorldAccess?> GetWorldAccess(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, Guid actorUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT w.current_revision, m.role
            FROM worlds w
            LEFT JOIN world_members m ON m.world_id = w.world_id AND m.user_id = @actorUserId
            WHERE w.world_id = @worldId;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("actorUserId", actorUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var revision = reader.GetInt64(0);
        var canEdit = !reader.IsDBNull(1) && (reader.GetString(1) is "owner" or "editor");
        return new WorldAccess(revision, canEdit);
    }

    private static async Task<CommandResult?> FindCommandResult(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid commandId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT status, rejection_code, resolved_revision
            FROM world_commands
            WHERE command_id = @commandId;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("commandId", commandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var accepted = reader.GetString(0) == "accepted";
        return new CommandResult(
            accepted,
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            null,
            false);
    }

    private static async Task RecordAcceptedCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, Guid actorUserId, WorldCommandRequest request, long revision, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO world_commands
                (command_id, world_id, actor_user_id, expected_revision, command_type,
                 payload, status, resolved_revision)
            VALUES
                (@commandId, @worldId, @actorUserId, @expectedRevision, @commandType,
                 @payload, 'accepted', @revision);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddCommandParameters(command, worldId, actorUserId, request);
        command.Parameters.AddWithValue("revision", revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordRejectedCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, Guid actorUserId, WorldCommandRequest request, string rejectionCode, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO world_commands
                (command_id, world_id, actor_user_id, expected_revision, command_type,
                 payload, status, rejection_code)
            VALUES
                (@commandId, @worldId, @actorUserId, @expectedRevision, @commandType,
                 @payload, 'rejected', @rejectionCode);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddCommandParameters(command, worldId, actorUserId, request);
        command.Parameters.AddWithValue("rejectionCode", rejectionCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommandParameters(NpgsqlCommand command, Guid worldId, Guid actorUserId, WorldCommandRequest request)
    {
        command.Parameters.AddWithValue("commandId", request.CommandId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("actorUserId", actorUserId);
        command.Parameters.AddWithValue("expectedRevision", request.ExpectedRevision);
        command.Parameters.AddWithValue("commandType", request.CommandType);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, request.Payload.GetRawText());
    }

    private static async Task SetWorldRevision(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, long revision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE worlds SET current_revision = @revision, updated_at = now() WHERE world_id = @worldId;", connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> RecordWorldEvent(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, Guid actorUserId, WorldCommandRequest request, long revision, string payloadJson, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO world_events
                (world_id, sequence_number, command_id, actor_user_id, event_type, payload)
            VALUES
                (@worldId, @revision, @commandId, @actorUserId, @eventType, @payload)
            RETURNING event_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("commandId", request.CommandId);
        command.Parameters.AddWithValue("actorUserId", actorUserId);
        command.Parameters.AddWithValue("eventType", request.CommandType);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payloadJson);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> UserExists(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM app_users WHERE user_id = @userId);", connection, transaction);
        command.Parameters.AddWithValue("userId", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> PublishedRuleExists(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ruleId,
        int ruleVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM content_definitions
                WHERE definition_kind = 'rule' AND definition_id = @ruleId
                  AND definition_version = @ruleVersion AND is_published);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("ruleId", ruleId);
        command.Parameters.AddWithValue("ruleVersion", ruleVersion);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> EntityExists(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, Guid entityId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM world_entities WHERE world_id = @worldId AND entity_id = @entityId AND deleted_revision IS NULL);", connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("entityId", entityId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> ZoneExists(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid worldId, string zoneKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM world_zones WHERE world_id = @worldId AND zone_key = @zoneKey);", connection, transaction);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddTransformParameters(NpgsqlCommand command, TransformPayload transform)
    {
        command.Parameters.AddWithValue("positionX", transform.PositionX);
        command.Parameters.AddWithValue("positionY", transform.PositionY);
        command.Parameters.AddWithValue("positionZ", transform.PositionZ);
        command.Parameters.AddWithValue("rotationX", transform.RotationX);
        command.Parameters.AddWithValue("rotationY", transform.RotationY);
        command.Parameters.AddWithValue("rotationZ", transform.RotationZ);
        command.Parameters.AddWithValue("scaleX", transform.ScaleX);
        command.Parameters.AddWithValue("scaleY", transform.ScaleY);
        command.Parameters.AddWithValue("scaleZ", transform.ScaleZ);
    }

    private static string NormalizeVisibility(string? visibility)
    {
        return visibility is "shared" or "public" ? visibility : "private";
    }

    private static double? TryReadJsonNumber(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Number &&
                   document.RootElement.TryGetDouble(out var number) &&
                   double.IsFinite(number) && number > 0d
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ReadNullableNumber(
        NpgsqlDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        try
        {
            using var document =
                JsonDocument.Parse(reader.GetString(ordinal));
            return document.RootElement.ValueKind ==
                   JsonValueKind.Number &&
                   document.RootElement.TryGetDouble(out var number) &&
                   double.IsFinite(number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class WorldCommandTypes
{
    public const string PlaceEntity = "place_entity";
    public const string MoveEntity = "move_entity";
    public const string RetireEntity = "retire_entity";
    public const string SetAuthoredFact = "set_authored_fact";
    public const string RetractAuthoredFact = "retract_authored_fact";
    public const string AddRuleBlock = "add_rule_block";
    public const string RemoveRuleBlock = "remove_rule_block";
    public const string ApplyMeaningPackage = "apply_meaning_package";
    public const string DefineZone = "define_zone";
    public const string RegisterPlayerAvatar = "register_player_avatar";
    public const string SaveAvatarCheckpoint = "save_avatar_checkpoint";
    public const string MigrateLegacyEquipmentRelations =
        "migrate_legacy_equipment_relations";
    public const string MigrateUnassignedEntityZones =
        "migrate_unassigned_entity_zones";
    public const string SetAvatarProfileRelations = "set_avatar_profile_relations";
    public const string SetContentPackage = "set_content_package";
    public const string ExecuteAction = "execute_action";
}

internal static class SemanticId
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsAsciiLetter(character) &&
                !IsAsciiDigit(character) &&
                character != '_' &&
                character != '-')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiDigit(char value) =>
        value is >= '0' and <= '9';
}

internal sealed record PasswordRegisterRequest(string? Email, string? DisplayName, string? Password);
internal sealed record PasswordLoginRequest(string? Email, string? Password);
internal sealed record CreatePlayerCharacterRequest(string DisplayName, string TemplateId, List<string>? EquippedPartIds)
{
    public bool IsValid(out string rejectionCode)
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > 80)
        {
            rejectionCode = "invalid_character_name";
            return false;
        }
        if (!SemanticId.IsValid(TemplateId) || (EquippedPartIds?.Any(id => !SemanticId.IsValid(id)) ?? false))
        {
            rejectionCode = "invalid_character_profile";
            return false;
        }
        rejectionCode = string.Empty;
        return true;
    }
}

internal sealed record UpdatePlayerCharacterProfileRequest(
    Guid CommandId,
    long ExpectedRevision,
    string DisplayName,
    string TemplateId,
    List<string>? EquippedPartIds,
    List<PlayerProfileRelation>? ProfileRelations)
{
    public bool IsValid(out string rejectionCode)
    {
        if (CommandId == Guid.Empty || ExpectedRevision < 1 ||
            string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > 80 ||
            !SemanticId.IsValid(TemplateId) ||
            (EquippedPartIds?.Any(id => !SemanticId.IsValid(id)) ?? false))
        {
            rejectionCode = "invalid_character_profile_update";
            return false;
        }
        var relations = ProfileRelations ?? new List<PlayerProfileRelation>();
        if (relations.Any(relation => !relation.IsValid()))
        {
            rejectionCode = "invalid_profile_relation";
            return false;
        }
        if (relations.Select(relation => (relation.SubjectId, relation.PredicateId, relation.ObjectId)).Distinct().Count() != relations.Count)
        {
            rejectionCode = "duplicate_profile_relation";
            return false;
        }
        rejectionCode = string.Empty;
        return true;
    }
}

internal sealed record PlayerProfileRelation(string SubjectId, string PredicateId, string ObjectId)
{
    public bool IsValid() =>
        SemanticId.IsValid(SubjectId) &&
        SemanticId.IsValid(PredicateId) &&
        SemanticId.IsValid(ObjectId);
}

internal sealed record PlayerCharacterProfileUpdateResult(bool Accepted, string? RejectionCode, long? Revision, bool IsReplay)
{
    public static PlayerCharacterProfileUpdateResult Success(long revision, bool isReplay) =>
        new(true, null, revision, isReplay);
    public static PlayerCharacterProfileUpdateResult Rejected(string rejectionCode, long? revision = null) =>
        new(false, rejectionCode, revision, false);
}
internal sealed record EnterWorldRequest(Guid CharacterId, Guid AvatarEntityId);
internal sealed record AccountSummary(Guid UserId, string DisplayName);
internal sealed record PlayerCharacterSummary(
    Guid CharacterId,
    string DisplayName,
    string TemplateId,
    IReadOnlyList<string> EquippedPartIds,
    long ProfileRevision,
    IReadOnlyList<PlayerProfileRelation> ProfileRelations);
internal sealed record AccountWorldSummary(Guid WorldId, string Title, string Slug, string Role, long Revision);
internal sealed record AccountDashboard(AccountSummary Account, IReadOnlyList<PlayerCharacterSummary> Characters, IReadOnlyList<AccountWorldSummary> Worlds);
internal sealed record WorldEntryResult(bool Accepted, string? RejectionCode, Guid? CharacterId, Guid? AvatarEntityId)
{
    public static WorldEntryResult Succeeded(Guid characterId, Guid avatarEntityId) => new(true, null, characterId, avatarEntityId);
    public static WorldEntryResult Rejected(string rejectionCode) => new(false, rejectionCode, null, null);
}
internal sealed record WorldAvatarProfileReadResult(bool Accepted, string? RejectionCode, IReadOnlyList<PlayerProfileRelation>? ProfileRelations, long Revision)
{
    public static WorldAvatarProfileReadResult Succeeded(IReadOnlyList<PlayerProfileRelation> relations, long revision) =>
        new(true, null, relations, revision);
    public static WorldAvatarProfileReadResult Rejected(string rejectionCode) =>
        new(false, rejectionCode, null, 0);
}
internal sealed record AvatarCheckpointReadResult(
    bool Accepted,
    string? RejectionCode,
    string? ZoneKey,
    TransformPayload? Transform,
    long Revision)
{
    public static AvatarCheckpointReadResult Succeeded(
        string? zoneKey,
        TransformPayload transform,
        long revision) =>
        new(true, null, zoneKey, transform, revision);
    public static AvatarCheckpointReadResult Rejected(string rejectionCode) =>
        new(false, rejectionCode, null, null, 0);
}
internal sealed record CreateWorldRequest(string Slug, string Title, string? Visibility);
internal sealed record ContentRuleCatalogPublishRequest(
    string? PackageVersion,
    List<ContentRuleDefinitionPublishRequest>? Rules);
internal sealed record ContentRuleDefinitionPublishRequest(
    string? RuleId,
    int DefinitionVersion,
    string? PayloadJson);
internal sealed record ContentRuleCatalogPublishResult(
    bool Accepted,
    string? RejectionCode,
    int PublishedCount,
    int UnchangedCount,
    IReadOnlyList<string>? ValidationMessages);
internal sealed record ContentRuleCatalogReadResult(
    string? RejectionCode,
    IReadOnlyList<PublishedRuleDefinitionSummary>? Rules);
internal sealed record PublishedRuleDefinitionSummary(
    string RuleId,
    int DefinitionVersion,
    string PackageVersion,
    string Checksum);
internal sealed record ContentActionCatalogPublishRequest(
    string? PackageVersion,
    List<ContentActionDefinitionPublishRequest>? Actions);
internal sealed record ContentActionDefinitionPublishRequest(
    string? ActionId,
    int DefinitionVersion,
    string? PayloadJson);
internal sealed record ContentActionCatalogPublishResult(
    bool Accepted,
    string? RejectionCode,
    int PublishedCount,
    int UnchangedCount,
    IReadOnlyList<string>? ValidationMessages);
internal sealed record ContentActionCatalogReadResult(
    string? RejectionCode,
    IReadOnlyList<PublishedActionDefinitionSummary>? Actions);
internal sealed record PublishedActionDefinitionSummary(
    string ActionId,
    int DefinitionVersion,
    string PackageVersion,
    string Checksum);
internal sealed record WorldCreatedResult(Guid WorldId, long Revision);
internal sealed record WorldAccess(long CurrentRevision, bool CanEdit);
internal sealed record CommandResult(bool Accepted, string? RejectionCode, long? Revision, Guid? EventId, bool IsReplay);
internal sealed record AutonomousActionExecutionResult(
    bool Accepted,
    string? RejectionCode,
    string? ActorAnimationIntent,
    Guid? RuleBindingId,
    long? Revision,
    Guid? EventId)
{
    public static AutonomousActionExecutionResult Rejected(string code) =>
        new(false, code, null, null, null, null);

    public static AutonomousActionExecutionResult Succeeded(
        string? actorAnimationIntent,
        Guid? ruleBindingId,
        long revision,
        Guid eventId) =>
        new(
            true,
            null,
            actorAnimationIntent,
            ruleBindingId,
            revision,
            eventId);
}
internal sealed record CommandApplyResult(bool Accepted, string? RejectionCode, string? EventPayloadJson)
{
    public static CommandApplyResult Succeeded(string? eventPayloadJson = null) => new(true, null, eventPayloadJson);
    public static CommandApplyResult Rejected(string rejectionCode) => new(false, rejectionCode, null);
}
internal sealed record AuthorityActionTargetPosition(
    string ZoneKey,
    AuthoritySpatialPosition Position);
internal sealed record AuthorityBoundRuleInvocation(
    Guid BindingId,
    int RuleVersion,
    string DefinitionJson);

internal sealed record WorldCommandRequest(
    int ContractVersion,
    Guid CommandId,
    long ExpectedRevision,
    string CommandType,
    JsonElement Payload)
{
    public bool IsValidEnvelope(out string rejectionCode)
    {
        if (ContractVersion != 1)
        {
            rejectionCode = "unsupported_contract_version";
            return false;
        }
        if (CommandId == Guid.Empty || ExpectedRevision < 0 || string.IsNullOrWhiteSpace(CommandType) || Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            rejectionCode = "invalid_command_envelope";
            return false;
        }
        rejectionCode = string.Empty;
        return true;
    }
}

internal sealed record TransformPayload(
    double PositionX, double PositionY, double PositionZ,
    double RotationX, double RotationY, double RotationZ,
    double ScaleX, double ScaleY, double ScaleZ)
{
    public bool IsValid => double.IsFinite(PositionX) && double.IsFinite(PositionY) && double.IsFinite(PositionZ)
                           && double.IsFinite(RotationX) && double.IsFinite(RotationY) && double.IsFinite(RotationZ)
                           && double.IsFinite(ScaleX) && double.IsFinite(ScaleY) && double.IsFinite(ScaleZ)
                           && ScaleX > 0 && ScaleY > 0 && ScaleZ > 0;
}

internal sealed record PlaceEntityPayload(
    Guid EntityId,
    string TemplateId,
    int TemplateVersion,
    string DisplayName,
    string? ZoneKey,
    TransformPayload Transform,
    List<InitialAuthoredFactPayload>? InitialFacts);
internal sealed record MoveEntityPayload(Guid EntityId, TransformPayload Transform);
internal sealed record RetireEntityPayload(Guid EntityId);
internal sealed record RetractAuthoredFactPayload(Guid FactId);
internal sealed record AddRuleBlockPayload(Guid BindingId, Guid TargetEntityId, string RuleId, int RuleVersion, string? ParameterValuesJson);
internal sealed record RemoveRuleBlockPayload(Guid BindingId);
internal sealed record ApplyMeaningPackagePayload(
    string Operation,
    Guid ApplicationId,
    Guid TargetEntityId,
    string SlotId,
    string PackageId,
    bool AdoptExistingContributions,
    List<string>? ReplacePredicateIds,
    List<string>? RequiredConceptIds,
    List<InitialAuthoredFactPayload>? AuthoredFacts,
    List<MeaningRuleBlockPayload>? RuleBlocks)
{
    public bool IsValid(out string rejectionCode)
    {
        if (Operation is not ("apply" or "remove") ||
            TargetEntityId == Guid.Empty ||
            !SemanticId.IsValid(SlotId) ||
            (Operation == "apply" &&
             (ApplicationId == Guid.Empty || !SemanticId.IsValid(PackageId))))
        {
            rejectionCode = "invalid_meaning_package_payload";
            return false;
        }
        if ((ReplacePredicateIds?.Count ?? 0) > 32 ||
            (RequiredConceptIds?.Count ?? 0) > 64 ||
            (AuthoredFacts?.Count ?? 0) > 128 ||
            (RuleBlocks?.Count ?? 0) > 32 ||
            (ReplacePredicateIds?.Any(value => !SemanticId.IsValid(value)) ?? false) ||
            (RequiredConceptIds?.Any(value => !SemanticId.IsValid(value)) ?? false) ||
            (AuthoredFacts?.Any(value =>
                value is null ||
                !SemanticId.IsValid(value.PredicateId) ||
                !value.IsValidObject()) ?? false) ||
            (RuleBlocks?.Any(value => value is null || !value.IsValid()) ?? false))
        {
            rejectionCode = "invalid_meaning_package_spec";
            return false;
        }
        rejectionCode = string.Empty;
        return true;
    }
}
internal sealed record MeaningRuleBlockPayload(
    Guid BindingId,
    string RuleId,
    int RuleVersion,
    string? ParameterValuesJson)
{
    public bool IsValid()
    {
        if (!SemanticId.IsValid(RuleId) || RuleVersion <= 0)
            return false;
        try
        {
            using var document =
                JsonDocument.Parse(ParameterValuesJson ?? "{}");
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string NormalizedParametersJson()
    {
        using var document =
            JsonDocument.Parse(ParameterValuesJson ?? "{}");
        return document.RootElement.GetRawText();
    }
}
internal sealed record MeaningFactSnapshot(
    Guid SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    Guid? ObjectEntityId,
    string? ObjectCanonicalId,
    string? ObjectValueJson)
{
    public InitialAuthoredFactPayload ToInitialPayload() =>
        new(PredicateId, ObjectKind, ObjectEntityId,
            ObjectCanonicalId, ObjectValueJson);
}
internal sealed record MeaningRuleBindingSnapshot(
    Guid TargetEntityId,
    string RuleId,
    int RuleVersion,
    string ParameterValuesJson);
internal sealed record ActiveMeaningPackage(
    Guid ApplicationId,
    string PackageId,
    List<Guid> InsertedFactIds,
    List<Guid> InsertedBindingIds,
    List<MeaningFactSnapshot> DisplacedFacts,
    List<MeaningRuleBindingSnapshot> DisplacedBindings);
internal sealed record RegisterPlayerAvatarPayload(Guid EntityId);
internal sealed record SaveAvatarCheckpointPayload(
    Guid AvatarEntityId,
    string? ZoneKey,
    TransformPayload Transform);
internal sealed record SetAvatarProfileRelationsPayload(Guid AvatarEntityId, List<PlayerProfileRelation>? ProfileRelations)
{
    public bool IsValid()
    {
        var relations = ProfileRelations ?? new List<PlayerProfileRelation>();
        return !relations.Any(relation => !relation.IsValid()) &&
               relations.Select(relation => (relation.SubjectId, relation.PredicateId, relation.ObjectId)).Distinct().Count() == relations.Count;
    }
}
internal sealed record SetContentPackagePayload(string PackageId, string PackageVersion, bool Enabled);
internal sealed record ExecuteActionPayload(
    Guid ActorEntityId,
    Guid TargetEntityId,
    Guid? ToolEntityId,
    string PackageId,
    string PackageVersion,
    string ActionId,
    int DefinitionVersion,
    bool? GroundedObservation = null)
{
    public bool IsValid() => ActorEntityId != Guid.Empty && TargetEntityId != Guid.Empty &&
                             SemanticId.IsValid(PackageId) && PackageVersion?.Trim().Length is > 0 and <= 64 &&
                             SemanticId.IsValid(ActionId) && DefinitionVersion > 0;
}
internal sealed record PreparedActionEvaluation(
    bool Accepted,
    string? RejectionCode,
    OntologyActionEffectDefinition? Definition,
    AuthoritativeActionEvaluation? Evaluation,
    AuthorityBoundRuleInvocation? BoundRule,
    TimeSpan Cooldown)
{
    public static PreparedActionEvaluation Rejected(string code) =>
        new(false, code, null, null, null, TimeSpan.Zero);

    public static PreparedActionEvaluation Succeeded(
        OntologyActionEffectDefinition definition,
        AuthoritativeActionEvaluation evaluation,
        AuthorityBoundRuleInvocation? boundRule,
        TimeSpan cooldown) =>
        new(true, null, definition, evaluation, boundRule, cooldown);
}
internal sealed record RuntimeActionEvaluationResult(
    bool Accepted,
    string? RejectionCode,
    Guid? ActorEntityId,
    Guid? ToolEntityId,
    string? PackageId,
    string? PackageVersion,
    string? ActionId,
    int DefinitionVersion,
    string? ActorAnimationIntent,
    Guid? RuleBindingId)
{
    public static RuntimeActionEvaluationResult Rejected(string code) =>
        new(
            false,
            code,
            null,
            null,
            null,
            null,
            null,
            0,
            null,
            null);

    public static RuntimeActionEvaluationResult Succeeded(
        ExecuteActionPayload request,
        string actorAnimationIntent,
        Guid? ruleBindingId) =>
        new(
            true,
            null,
            request.ActorEntityId,
            request.ToolEntityId,
            request.PackageId,
            request.PackageVersion,
            request.ActionId,
            request.DefinitionVersion,
            actorAnimationIntent,
            ruleBindingId);
}

internal sealed record ActionPreviewEvaluationResult(
    bool Accepted,
    string? RejectionCode,
    Guid? ActorEntityId,
    Guid? TargetEntityId,
    Guid? ToolEntityId,
    string? PackageId,
    string? PackageVersion,
    string? ActionId,
    int DefinitionVersion,
    string? ActorAnimationIntent,
    Guid? RuleBindingId,
    int MutationCount)
{
    public static ActionPreviewEvaluationResult Rejected(string code) =>
        new(
            false,
            code,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            null,
            null,
            0);

    public static ActionPreviewEvaluationResult Succeeded(
        ExecuteActionPayload request,
        string? actorAnimationIntent,
        Guid? ruleBindingId,
        int mutationCount) =>
        new(
            true,
            null,
            request.ActorEntityId,
            request.TargetEntityId,
            request.ToolEntityId,
            request.PackageId,
            request.PackageVersion,
            request.ActionId,
            request.DefinitionVersion,
            actorAnimationIntent,
            ruleBindingId,
            mutationCount);
}

internal sealed record PostRuleApplicationResult(
    bool Accepted,
    string? RejectionCode,
    int MutationCount,
    IReadOnlyList<Guid> RuleBindingIds)
{
    public static PostRuleApplicationResult Rejected(string code) =>
        new(false, code, 0, Array.Empty<Guid>());

    public static PostRuleApplicationResult Succeeded(
        int mutationCount,
        IReadOnlyList<Guid> bindingIds) =>
        new(true, null, mutationCount, bindingIds);
}

internal sealed record PlayerIntentRequest(
    Guid AvatarEntityId,
    string ZoneKey,
    long Sequence,
    float MoveX,
    float MoveZ,
    float MoveSpeed,
    string PackageId,
    string PackageVersion,
    string ActionId,
    int DefinitionVersion)
{
    public bool IsValid => AvatarEntityId != Guid.Empty
                           && Sequence > 0
                           && float.IsFinite(MoveX)
                           && float.IsFinite(MoveZ)
                           && float.IsFinite(MoveSpeed)
                           && MathF.Abs(MoveX) <= 1f
                           && MathF.Abs(MoveZ) <= 1f
                           && MoveSpeed is >= 0f and <= 100f
                           && SemanticId.IsValid(PackageId)
                           && PackageVersion?.Trim().Length is > 0 and <= 64
                           && SemanticId.IsValid(ActionId)
                           && DefinitionVersion > 0;
}
internal sealed record ResolvedPlayerPoseRequest(
    string ZoneKey,
    long IntentSequence,
    long PoseSequence,
    double PositionX,
    double PositionY,
    double PositionZ,
    string MotionStatus)
{
    public bool IsValid =>
        IntentSequence > 0 &&
        PoseSequence > 0 &&
        double.IsFinite(PositionX) &&
        double.IsFinite(PositionY) &&
        double.IsFinite(PositionZ) &&
        MotionStatus is "idle" or "moving" or "swimming" or "airborne";
}
internal sealed record ActivatePlayerRuntimeRequest(string ZoneKey);
internal sealed record DefineZonePayload(
    string ZoneKey,
    double MinX,
    double MinZ,
    double MaxX,
    double MaxZ,
    string SimulationMode);
internal sealed record SetAuthoredFactPayload(
    Guid SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    Guid? ObjectEntityId,
    string? ObjectCanonicalId,
    string? ObjectValueJson)
{
    public bool IsValidObject()
    {
        return ObjectKind switch
        {
            "entity" => ObjectEntityId.HasValue && ObjectCanonicalId is null && ObjectValueJson is null,
            "canonical" => !string.IsNullOrWhiteSpace(ObjectCanonicalId) && SemanticId.IsValid(ObjectCanonicalId) && ObjectEntityId is null && ObjectValueJson is null,
            "number" or "boolean" or "text" or "json" => ObjectEntityId is null && ObjectCanonicalId is null && IsJson(ObjectValueJson),
            _ => false
        };
    }

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed record InitialAuthoredFactPayload(
    string PredicateId,
    string ObjectKind,
    Guid? ObjectEntityId,
    string? ObjectCanonicalId,
    string? ObjectValueJson)
{
    public bool IsValidObject()
    {
        return ObjectKind switch
        {
            "entity" => ObjectEntityId.HasValue &&
                        ObjectCanonicalId is null &&
                        ObjectValueJson is null,
            "canonical" => !string.IsNullOrWhiteSpace(ObjectCanonicalId) &&
                           SemanticId.IsValid(ObjectCanonicalId) &&
                           ObjectEntityId is null &&
                           ObjectValueJson is null,
            "number" or "boolean" or "text" or "json" =>
                ObjectEntityId is null &&
                ObjectCanonicalId is null &&
                IsJson(ObjectValueJson),
            _ => false
        };
    }

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed record WorldProjection(
    Guid WorldId,
    string Title,
    long Revision,
    string? ScopeZoneKey,
    List<WorldEntityProjection> Entities,
    List<WorldFactProjection> Facts,
    List<WorldRuleBindingProjection> RuleBindings,
    List<WorldActionDefinitionProjection> Actions);
internal sealed record WorldEntityProjection(Guid EntityId, string TemplateId, int TemplateVersion, string DisplayName, string? ZoneKey, TransformPayload Transform);
internal sealed record WorldFactProjection(Guid FactId, Guid SubjectEntityId, string PredicateId, string ObjectKind, Guid? ObjectEntityId, string? ObjectCanonicalId, string? ObjectValueJson);
internal sealed record WorldRuleBindingProjection(Guid BindingId, Guid TargetEntityId, string RuleId, int RuleVersion, bool Enabled, string ParameterValuesJson);
internal sealed record WorldActionDefinitionProjection(
    string PackageId,
    string PackageVersion,
    string ActionId,
    int DefinitionVersion,
    string ActorAnimationIntent);
internal sealed record WorldZoneProjection(
    string ZoneKey,
    double MinX,
    double MinZ,
    double MaxX,
    double MaxZ,
    string SimulationMode);
internal sealed record WorldZoneScheduleDefinition(
    Guid WorldId,
    string ZoneKey,
    string SimulationMode,
    double MinX,
    double MinZ,
    double MaxX,
    double MaxZ);
internal sealed record WorldPlayerAvatarMotionConfiguration(
    Guid WorldId,
    Guid AvatarEntityId,
    Guid UserId,
    string ZoneKey,
    double SpawnPositionX,
    double SpawnPositionY,
    double SpawnPositionZ,
    double? MovementSpeed,
    string? JumpActionId,
    double? GravityAcceleration,
    double? JumpTakeoffSpeed,
    double? GroundStickVelocity,
    double? MaximumStepHeight,
    double? GroundClearance);
internal sealed record WorldPlayerAvatarRegistration(Guid AvatarEntityId, Guid UserId);
internal sealed record WorldAutonomousActorConfiguration(
    Guid WorldId,
    Guid ActorEntityId,
    string ZoneKey,
    double SpawnPositionX,
    double SpawnPositionY,
    double SpawnPositionZ,
    double MovementSpeed,
    double DetectionRange,
    double LeashRange,
    double AttackRange,
    string TargetActionId,
    int TargetActionDefinitionVersion,
    string ChaseActionId,
    int ChaseActionDefinitionVersion,
    string AttackActionId,
    string IdleAnimationIntent,
    string MoveAnimationIntent,
    string PackageId,
    string PackageVersion,
    int ActionDefinitionVersion);
internal sealed record WorldAutonomousTargetConfiguration(
    Guid EntityId,
    Guid? UserId,
    string ZoneKey,
    double SpawnPositionX,
    double SpawnPositionY,
    double SpawnPositionZ,
    bool RequiresRuntimePosition);

internal sealed record WorldRevisionNotification(
    Guid WorldId,
    long Revision,
    Guid? EventId,
    string CommandType,
    string? ZoneKey);

internal sealed record WorldZoneRuntimeNotification(
    Guid WorldId,
    string ZoneKey,
    long ObservedAtUnixMilliseconds);

internal sealed record ZoneSessionCounts(
    int ConnectionCount,
    int UserCount,
    long ObservedAtUnixMilliseconds);

internal sealed record ZoneSessionProjection(
    string ZoneKey,
    string SimulationMode,
    string RunState,
    int ConnectionCount,
    int UserCount,
    long ObservedAtUnixMilliseconds);

internal sealed record WorldZoneRuntimeSnapshot(
    Guid WorldId,
    string ZoneKey,
    string ConfiguredSimulationMode,
    string RunState,
    int ConnectionCount,
    int UserCount,
    bool ExecutionLeaseHeld,
    string? SchedulerOwner,
    int PlannedTickIntervalMilliseconds,
    string EngineStatus,
    int EvaluatedRuleBindingCount,
    int SkippedRuleBindingCount,
    int MissingRuleDefinitionCount,
    IReadOnlyList<HeadlessInferredFact> InferredFacts,
    long ObservedAtUnixMilliseconds);

internal static class WorldZoneRuntimePolicy
{
    public static string ResolveRunState(string configuredMode, int connectionCount)
    {
        if (configuredMode == "dormant") return "dormant";
        if (configuredMode == "reduced") return "reduced";
        return connectionCount > 0 ? "active" : "idle";
    }
}

internal interface IWorldZoneSessionRegistry
{
    string BackendName { get; }
    TimeSpan LeaseDuration { get; }
    Task Register(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken);
    Task Refresh(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken);
    Task Unregister(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken);
    Task<ZoneSessionCounts> GetSummary(Guid worldId, string? zoneKey, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetActiveUserIds(Guid worldId, string? zoneKey, CancellationToken cancellationToken);
}

internal interface IWorldZoneRuntimeRegistry
{
    string BackendName { get; }
    Task Set(WorldZoneRuntimeSnapshot snapshot, CancellationToken cancellationToken);
    Task<WorldZoneRuntimeSnapshot?> Get(Guid worldId, string zoneKey, CancellationToken cancellationToken);
}

internal interface IWorldZoneExecutionLeaseRegistry
{
    Task<bool> TryAcquire(Guid worldId, string zoneKey, string workload, string ownerId, TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// A cross-replica runtime view. It is intentionally ephemeral and never replaces
/// the PostgreSQL command/event history or the ontology projection.
/// </summary>
internal sealed class RedisWorldZoneRuntimeRegistry(IConnectionMultiplexer redis) : IWorldZoneRuntimeRegistry
{
    private static readonly TimeSpan RuntimeTtl = TimeSpan.FromSeconds(15);
    private readonly IDatabase database = redis.GetDatabase();
    public string BackendName => "redis";

    public async Task Set(WorldZoneRuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        await database.StringSetAsync(
            Key(snapshot.WorldId, snapshot.ZoneKey),
            JsonSerializer.Serialize(snapshot),
            RuntimeTtl);
    }

    public async Task<WorldZoneRuntimeSnapshot?> Get(Guid worldId, string zoneKey, CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, zoneKey));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<WorldZoneRuntimeSnapshot>(value!);
    }

    private static string Key(Guid worldId, string zoneKey) =>
        "tormia:world:" + worldId.ToString("N") + ":zone:" + zoneKey + ":runtime";
}

internal sealed class InMemoryWorldZoneRuntimeRegistry : IWorldZoneRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, WorldZoneRuntimeSnapshot> snapshots = new();
    public string BackendName => "in_memory_development";

    public Task Set(WorldZoneRuntimeSnapshot snapshot, CancellationToken cancellationToken)
    {
        snapshots[Key(snapshot.WorldId, snapshot.ZoneKey)] = snapshot;
        return Task.CompletedTask;
    }

    public Task<WorldZoneRuntimeSnapshot?> Get(Guid worldId, string zoneKey, CancellationToken cancellationToken)
    {
        snapshots.TryGetValue(Key(worldId, zoneKey), out var value);
        return Task.FromResult<WorldZoneRuntimeSnapshot?>(value);
    }

    private static string Key(Guid worldId, string zoneKey) => worldId.ToString("N") + ":" + zoneKey;
}

internal sealed class RedisWorldZoneExecutionLeaseRegistry(IConnectionMultiplexer redis) : IWorldZoneExecutionLeaseRegistry
{
    private const string ClaimScript = """
        local current = redis.call('GET', KEYS[1])
        if current == ARGV[1] then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 1
        end
        if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then
            return 1
        end
        return 0
        """;
    private readonly IDatabase database = redis.GetDatabase();

    public async Task<bool> TryAcquire(Guid worldId, string zoneKey, string workload, string ownerId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var result = await database.ScriptEvaluateAsync(
            ClaimScript,
            new RedisKey[] { Key(worldId, zoneKey, workload) },
            new RedisValue[] { ownerId, (long)duration.TotalMilliseconds });
        return (int)result == 1;
    }

    private static string Key(Guid worldId, string zoneKey, string workload) =>
        "tormia:world:" + worldId.ToString("N") + ":zone:" + zoneKey + ":" + workload + ":execution-lease";
}

internal sealed class InMemoryWorldZoneExecutionLeaseRegistry : IWorldZoneExecutionLeaseRegistry
{
    private readonly ConcurrentDictionary<string, InMemoryLease> leases = new();

    public Task<bool> TryAcquire(Guid worldId, string zoneKey, string workload, string ownerId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var key = worldId.ToString("N") + ":" + zoneKey + ":" + workload;
        var now = DateTimeOffset.UtcNow;
        var next = leases.AddOrUpdate(
            key,
            _ => new InMemoryLease(ownerId, now + duration),
            (_, current) => current.ExpiresAt <= now || current.OwnerId == ownerId
                ? new InMemoryLease(ownerId, now + duration)
                : current);
        return Task.FromResult(next.OwnerId == ownerId);
    }

    private sealed record InMemoryLease(string OwnerId, DateTimeOffset ExpiresAt);
}

/// <summary>
/// Converts durable Zone configuration plus shared session presence into a
/// cross-replica schedule. This is real orchestration (including a single-owner
/// Redis execution lease). The owned tick runs only the pure, inference-only
/// ontology core: it never executes Unity adapters and never writes inferred
/// results back into authored world facts.
/// </summary>
internal sealed class WorldZoneSimulationScheduler(
    WorldAuthorityRepository repository,
    IWorldZoneSessionRegistry sessions,
    IWorldZoneRuntimeRegistry runtime,
    IWorldZoneExecutionLeaseRegistry executionLeases,
    HeadlessZoneOntologyEvaluator evaluator,
    ILogger<WorldZoneSimulationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromSeconds(4);
    private readonly string ownerId = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var zones = await repository.GetZoneScheduleDefinitions(stoppingToken);
                foreach (var zone in zones)
                {
                    var session = await sessions.GetSummary(zone.WorldId, zone.ZoneKey, stoppingToken);
                    var state = WorldZoneRuntimePolicy.ResolveRunState(
                        zone.SimulationMode, session.ConnectionCount);
                    var interval = state switch
                    {
                        "active" => 1000,
                        "reduced" => 5000,
                        _ => 0
                    };
                    var leaseHeld = interval > 0 && await executionLeases.TryAcquire(
                        zone.WorldId, zone.ZoneKey, "inference", ownerId, ExecutionLeaseDuration, stoppingToken);
                    var evaluation = leaseHeld
                        ? await evaluator.Evaluate(zone.WorldId, zone.ZoneKey, stoppingToken)
                        : new HeadlessZoneEvaluationResult(
                            0,
                            0,
                            0,
                            Array.Empty<HeadlessInferredFact>(),
                            "not_scheduled");
                    var snapshot = new WorldZoneRuntimeSnapshot(
                        zone.WorldId,
                        zone.ZoneKey,
                        zone.SimulationMode,
                        state,
                        session.ConnectionCount,
                        session.UserCount,
                        leaseHeld,
                        leaseHeld ? ownerId : null,
                        interval,
                        evaluation.EngineStatus,
                        evaluation.EvaluatedRuleBindingCount,
                        evaluation.SkippedRuleBindingCount,
                        evaluation.MissingRuleDefinitionCount,
                        evaluation.InferredFacts,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    await runtime.Set(snapshot, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "World Zone scheduler iteration failed.");
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
}

/// <summary>
/// Shared presence lease store. It has no ontology facts and no authoring powers:
/// it only tells every server replica how many authorized sockets are currently
/// interested in a Zone, which is the input for later simulation scheduling.
/// </summary>
internal sealed class RedisWorldZoneSessionRegistry(IConnectionMultiplexer redis) : IWorldZoneSessionRegistry
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(90);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";
    public TimeSpan LeaseDuration => Lease;

    public async Task Register(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken) =>
        await Upsert(worldId, zoneKey, userId, connectionId);

    public async Task Refresh(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken) =>
        await Upsert(worldId, zoneKey, userId, connectionId);

    public async Task Unregister(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        await database.SortedSetRemoveAsync(Key(worldId, zoneKey), Member(userId, connectionId));
    }

    public async Task<ZoneSessionCounts> GetSummary(Guid worldId, string? zoneKey, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var members = await GetActiveMembers(worldId, zoneKey, now);
        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var text = member.ToString();
            var separator = text.IndexOf('|');
            users.Add(separator < 0 ? text : text[..separator]);
        }
        return new ZoneSessionCounts(members.Length, users.Count, now);
    }

    public async Task<IReadOnlySet<Guid>> GetActiveUserIds(
        Guid worldId,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        var members = await GetActiveMembers(worldId, zoneKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var users = new HashSet<Guid>();
        foreach (var member in members)
        {
            var text = member.ToString();
            var separator = text.IndexOf('|');
            if (Guid.TryParse(separator < 0 ? text : text[..separator], out var userId)) users.Add(userId);
        }
        return users;
    }

    private async Task Upsert(Guid worldId, string? zoneKey, Guid userId, string connectionId)
    {
        var key = Key(worldId, zoneKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await database.SortedSetAddAsync(key, Member(userId, connectionId), now);
        await database.KeyExpireAsync(key, Lease);
    }

    private async Task<RedisValue[]> GetActiveMembers(Guid worldId, string? zoneKey, long now)
    {
        var key = Key(worldId, zoneKey);
        var expiry = now - (long)Lease.TotalMilliseconds;
        await database.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, expiry);
        return await database.SortedSetRangeByRankAsync(key);
    }

    private static string Key(Guid worldId, string? zoneKey) =>
        "tormia:world:" + worldId.ToString("N") + ":zone:" +
        (string.IsNullOrWhiteSpace(zoneKey) ? "unassigned" : zoneKey.Trim()) + ":sessions";

    private static string Member(Guid userId, string connectionId) => userId.ToString("N") + "|" + connectionId;
}

internal sealed class InMemoryWorldZoneSessionRegistry : IWorldZoneSessionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Guid>> sessions = new();
    public string BackendName => "in_memory_development";
    public TimeSpan LeaseDuration => TimeSpan.Zero;

    public Task Register(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        sessions.GetOrAdd(Key(worldId, zoneKey), _ => new ConcurrentDictionary<string, Guid>())[connectionId] = userId;
        return Task.CompletedTask;
    }

    public Task Refresh(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken) =>
        Register(worldId, zoneKey, userId, connectionId, cancellationToken);

    public Task Unregister(Guid worldId, string? zoneKey, Guid userId, string connectionId, CancellationToken cancellationToken)
    {
        if (sessions.TryGetValue(Key(worldId, zoneKey), out var entries)) entries.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    public Task<ZoneSessionCounts> GetSummary(Guid worldId, string? zoneKey, CancellationToken cancellationToken)
    {
        var entries = sessions.TryGetValue(Key(worldId, zoneKey), out var value)
            ? value : new ConcurrentDictionary<string, Guid>();
        return Task.FromResult(new ZoneSessionCounts(
            entries.Count,
            entries.Values.Distinct().Count(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public Task<IReadOnlySet<Guid>> GetActiveUserIds(Guid worldId, string? zoneKey, CancellationToken cancellationToken)
    {
        var entries = sessions.TryGetValue(Key(worldId, zoneKey), out var value)
            ? value : new ConcurrentDictionary<string, Guid>();
        return Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>(entries.Values));
    }

    private static string Key(Guid worldId, string? zoneKey) => worldId.ToString("N") + ":" + (zoneKey ?? string.Empty);
}

internal sealed record ZoneSessionIdentity(Guid WorldId, string? ZoneKey, Guid UserId);

/// <summary>
/// A client joins one world-zone session. The hub never accepts authoring commands;
/// REST remains the ordered authority command boundary. Hub messages are only hints
/// that tell a client to refresh the projection it is authorized to read.
/// </summary>
internal sealed class WorldZoneHub(
    WorldAuthorityRepository repository,
    IWorldZoneSessionRegistry sessions,
    IWorldZoneRuntimeNotificationPublisher runtimeNotifications) : Hub
{
    public const string RevisionEventName = "worldRevision";
    public const string RuntimeChangedEventName = "zoneRuntimeChanged";

    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var worldText = request?.Query["worldId"].ToString();
        var zoneKey = request?.Query["zoneKey"].ToString();
        var actor = request?.HttpContext.Items.TryGetValue("Tormia.ActorUserId", out var actorValue) == true
            ? actorValue
            : null;

        if (!Guid.TryParse(worldText, out var worldId)
            || actor is not Guid actorUserId
            || (!string.IsNullOrWhiteSpace(zoneKey) && !SemanticId.IsValid(zoneKey))
            || !await repository.CanAccessWorld(worldId, actorUserId, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetGroupName(worldId, zoneKey),
            Context.ConnectionAborted);
        var identity = new ZoneSessionIdentity(worldId, zoneKey, actorUserId);
        Context.Items[typeof(ZoneSessionIdentity)] = identity;
        await sessions.Register(worldId, zoneKey, actorUserId, Context.ConnectionId, Context.ConnectionAborted);
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            await runtimeNotifications.PublishChanged(
                worldId, zoneKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Context.ConnectionAborted);
        }
        await base.OnConnectedAsync();
    }

    public async Task Heartbeat()
    {
        if (Context.Items.TryGetValue(typeof(ZoneSessionIdentity), out var value) &&
            value is ZoneSessionIdentity identity)
        {
            await sessions.Refresh(
                identity.WorldId,
                identity.ZoneKey,
                identity.UserId,
                Context.ConnectionId,
                Context.ConnectionAborted);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(typeof(ZoneSessionIdentity), out var value) &&
            value is ZoneSessionIdentity identity)
        {
            await sessions.Unregister(
                identity.WorldId,
                identity.ZoneKey,
                identity.UserId,
                Context.ConnectionId,
                Context.ConnectionAborted);
            if (!string.IsNullOrWhiteSpace(identity.ZoneKey))
            {
                await runtimeNotifications.PublishChanged(
                    identity.WorldId,
                    identity.ZoneKey,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    CancellationToken.None);
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static string GetGroupName(Guid worldId, string? zoneKey)
    {
        return string.IsNullOrWhiteSpace(zoneKey)
            ? $"world:{worldId:N}:unassigned"
            : $"world:{worldId:N}:zone:{zoneKey}";
    }

    public static IReadOnlyList<string> GetNotificationGroups(Guid worldId, string? zoneKey)
    {
        var worldGroup = GetGroupName(worldId, null);
        if (string.IsNullOrWhiteSpace(zoneKey))
        {
            return new[] { worldGroup };
        }

        return new[] { worldGroup, GetGroupName(worldId, zoneKey) };
    }
}
