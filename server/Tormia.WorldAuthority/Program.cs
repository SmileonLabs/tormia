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
    builder.Services.AddSingleton<IWorldPlayerMotionRuntimeRegistry, RedisWorldPlayerMotionRuntimeRegistry>();
}
else
{
    // Local-only fallback. Production uses Redis so every authority instance sees
    // the same Zone membership and can make the same scheduling decision.
    builder.Services.AddSingleton<IWorldZoneSessionRegistry, InMemoryWorldZoneSessionRegistry>();
    builder.Services.AddSingleton<IWorldZoneRuntimeRegistry, InMemoryWorldZoneRuntimeRegistry>();
    builder.Services.AddSingleton<IWorldZoneExecutionLeaseRegistry, InMemoryWorldZoneExecutionLeaseRegistry>();
    builder.Services.AddSingleton<IWorldPlayerIntentRegistry, InMemoryWorldPlayerIntentRegistry>();
    builder.Services.AddSingleton<IWorldPlayerMotionRuntimeRegistry, InMemoryWorldPlayerMotionRuntimeRegistry>();
}
builder.Services.AddHostedService<WorldZoneSimulationScheduler>();
// Runtime notifications are transport hints only. They never author Facts or
// transforms and Unity still reads the current snapshot from the authority API.
builder.Services.AddSingleton<IWorldZoneRuntimeNotificationPublisher, SignalRWorldZoneRuntimeNotificationPublisher>();
builder.Services.AddHostedService<WorldPlayerMotionSimulationScheduler>();

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
    engine = "zone_bounded_kinematic"
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

    var intent = new WorldPlayerIntent(
        worldId, actorUserId, request.AvatarEntityId, request.ZoneKey,
        request.Sequence, request.MoveX, request.MoveZ, request.Jump,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    var accepted = await intents.Submit(intent, cancellationToken);
    return accepted
        ? Results.Accepted($"/v1/worlds/{worldId}/runtime/intents/{request.AvatarEntityId}", new { accepted = true })
        : Results.Conflict(new { rejectionCode = "stale_player_intent" });
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

    public WorldAuthorityRepository(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
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
            SELECT p.package_id, p.package_version, d.definition_id, d.definition_version
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
                    actionsReader.GetInt32(3)));
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
    /// Resolves only the static pieces needed by the kinematic authority: the
    /// durable spawn transform and an authored numeric movement_speed Fact.
    /// Dynamic input, position, and observations remain in the runtime layer.
    /// </summary>
    public async Task<IReadOnlyList<WorldPlayerAvatarMotionConfiguration>> GetPlayerAvatarMotionConfigurations(
        Guid worldId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT a.entity_id, e.position_x, e.position_y, e.position_z,
                   speed_fact.object_value::text
            FROM world_player_avatars a
            INNER JOIN world_entities e ON e.entity_id = a.entity_id
            LEFT JOIN LATERAL (
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
            WHERE a.world_id = @worldId
              AND e.zone_key = @zoneKey
              AND e.deleted_revision IS NULL
            ORDER BY a.entity_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("zoneKey", zoneKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var avatars = new List<WorldPlayerAvatarMotionConfiguration>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var speed = reader.IsDBNull(4) ? null : TryReadJsonNumber(reader.GetString(4));
            avatars.Add(new WorldPlayerAvatarMotionConfiguration(
                worldId,
                reader.GetGuid(0),
                zoneKey,
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                speed));
        }
        return avatars;
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
            WorldCommandTypes.SetAuthoredFact => request.Payload.Deserialize<SetAuthoredFactPayload>(JsonOptions)?.SubjectEntityId,
            WorldCommandTypes.AddRuleBlock => request.Payload.Deserialize<AddRuleBlockPayload>(JsonOptions)?.TargetEntityId,
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

        return new CommandResult(true, null, nextRevision, eventId, false);
    }

    private static async Task<CommandApplyResult> ApplyAuthoringCommand(
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
            WorldCommandTypes.SetAuthoredFact => await SetAuthoredFact(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.RetractAuthoredFact => await RetractAuthoredFact(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.AddRuleBlock => await AddRuleBlock(connection, transaction, worldId, request.Payload, revision, cancellationToken),
            WorldCommandTypes.RegisterPlayerAvatar => await RegisterPlayerAvatar(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.SaveAvatarCheckpoint => await SaveAvatarCheckpoint(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.SetAvatarProfileRelations => await SetAvatarProfileRelations(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.SetContentPackage => await SetContentPackage(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.ExecuteAction => await ExecuteAction(connection, transaction, worldId, request.Payload, actorUserId, revision, cancellationToken),
            WorldCommandTypes.RemoveRuleBlock => await RemoveRuleBlock(connection, transaction, worldId, request.Payload, revision, cancellationToken),
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
            || string.IsNullOrWhiteSpace(request.DisplayName) || !request.Transform.IsValid)
        {
            return CommandApplyResult.Rejected("invalid_place_entity_payload");
        }

        var entityId = request.EntityId == Guid.Empty ? Guid.NewGuid() : request.EntityId;
        if (!string.IsNullOrWhiteSpace(request.ZoneKey)
            && !await ZoneExists(connection, transaction, worldId, request.ZoneKey, cancellationToken))
        {
            return CommandApplyResult.Rejected("unknown_zone");
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

        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new { entityId, request.TemplateId, request.TemplateVersion, request.Transform }, JsonOptions));
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

    // The action definition supplies the meaning. The command supplies only the
    // actor/target/tool entities, which prevents a Unity client from inventing a
    // predicate or writing arbitrary durable facts. Complex predicates/effects are
    // intentionally deferred until they can run in the headless evaluator.
    private static async Task<CommandApplyResult> ExecuteAction(
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
        if (!await AvatarBelongsToUser(connection, transaction, worldId, actorUserId, request.ActorEntityId, cancellationToken))
            return CommandApplyResult.Rejected("action_actor_forbidden");
        if (!await EntityExists(connection, transaction, worldId, request.TargetEntityId, cancellationToken) ||
            (request.ToolEntityId.HasValue && !await EntityExists(connection, transaction, worldId, request.ToolEntityId.Value, cancellationToken)))
            return CommandApplyResult.Rejected("action_entity_not_found");

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
        await using (var command = new NpgsqlCommand(definitionSql, connection, transaction))
        {
            command.Parameters.AddWithValue("worldId", worldId); command.Parameters.AddWithValue("packageId", request.PackageId.Trim());
            command.Parameters.AddWithValue("packageVersion", request.PackageVersion.Trim()); command.Parameters.AddWithValue("actionId", request.ActionId.Trim());
            command.Parameters.AddWithValue("definitionVersion", request.DefinitionVersion);
            definitionJson = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (definitionJson is null) return CommandApplyResult.Rejected("action_definition_not_enabled");

        OntologyActionEffectDefinition? definition;
        try { definition = JsonSerializer.Deserialize<OntologyActionEffectDefinition>(definitionJson, OntologyJson); }
        catch (JsonException) { return CommandApplyResult.Rejected("invalid_published_action_definition"); }
        if (definition is null || !IsSupportedAuthoritativeAction(definition) ||
            !string.Equals(definition.actionVerb, request.ActionId.Trim(), StringComparison.Ordinal) ||
            (definition.requiresTool && !request.ToolEntityId.HasValue) ||
            (definition.objectPattern == "?tool" && !request.ToolEntityId.HasValue))
            return CommandApplyResult.Rejected("unsupported_authoritative_action_definition");

        var objectEntityId = definition.objectPattern == "?tool" ? request.ToolEntityId!.Value : request.TargetEntityId;
        const string insertSql = """
            INSERT INTO world_facts (world_id, subject_entity_id, predicate_id, object_kind, object_entity_id, source_type, created_revision)
            VALUES (@worldId, @actorEntityId, @predicateId, 'entity', @objectEntityId, 'action', @revision);
            """;
        await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
        insert.Parameters.AddWithValue("worldId", worldId); insert.Parameters.AddWithValue("actorEntityId", request.ActorEntityId);
        insert.Parameters.AddWithValue("predicateId", definition.predicate.Trim()); insert.Parameters.AddWithValue("objectEntityId", objectEntityId);
        insert.Parameters.AddWithValue("revision", revision);
        try { await insert.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException exception) when (exception.SqlState == "23505") { return CommandApplyResult.Rejected("action_effect_already_exists"); }
        return CommandApplyResult.Succeeded(JsonSerializer.Serialize(new
        {
            request.ActorEntityId, request.TargetEntityId, request.ToolEntityId,
            request.PackageId, request.PackageVersion, request.ActionId, request.DefinitionVersion,
            PredicateId = definition.predicate
        }, JsonOptions));
    }

    private static bool IsSupportedAuthoritativeAction(OntologyActionEffectDefinition definition) =>
        definition.conditions is not { Count: > 0 } && definition.effects is not { Count: > 0 } &&
        SemanticId.IsValid(definition.actionVerb) && SemanticId.IsValid(definition.predicate) &&
        definition.subjectPattern == "?actor" && (definition.objectPattern is "?target" or "?tool");

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
        const string sql = """
            UPDATE world_rule_bindings
            SET retracted_revision = @revision
            WHERE binding_id = @bindingId AND world_id = @worldId AND retracted_revision IS NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("bindingId", request.BindingId);
        command.Parameters.AddWithValue("worldId", worldId);
        command.Parameters.AddWithValue("revision", revision);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1
            ? CommandApplyResult.Succeeded()
            : CommandApplyResult.Rejected("rule_binding_not_found");
    }

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
}

internal static class WorldCommandTypes
{
    public const string PlaceEntity = "place_entity";
    public const string MoveEntity = "move_entity";
    public const string SetAuthoredFact = "set_authored_fact";
    public const string RetractAuthoredFact = "retract_authored_fact";
    public const string AddRuleBlock = "add_rule_block";
    public const string RemoveRuleBlock = "remove_rule_block";
    public const string DefineZone = "define_zone";
    public const string RegisterPlayerAvatar = "register_player_avatar";
    public const string SaveAvatarCheckpoint = "save_avatar_checkpoint";
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
internal sealed record CommandApplyResult(bool Accepted, string? RejectionCode, string? EventPayloadJson)
{
    public static CommandApplyResult Succeeded(string? eventPayloadJson = null) => new(true, null, eventPayloadJson);
    public static CommandApplyResult Rejected(string rejectionCode) => new(false, rejectionCode, null);
}

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

internal sealed record PlaceEntityPayload(Guid EntityId, string TemplateId, int TemplateVersion, string DisplayName, string? ZoneKey, TransformPayload Transform);
internal sealed record MoveEntityPayload(Guid EntityId, TransformPayload Transform);
internal sealed record RetractAuthoredFactPayload(Guid FactId);
internal sealed record AddRuleBlockPayload(Guid BindingId, Guid TargetEntityId, string RuleId, int RuleVersion, string? ParameterValuesJson);
internal sealed record RemoveRuleBlockPayload(Guid BindingId);
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
    int DefinitionVersion)
{
    public bool IsValid() => ActorEntityId != Guid.Empty && TargetEntityId != Guid.Empty &&
                             SemanticId.IsValid(PackageId) && PackageVersion?.Trim().Length is > 0 and <= 64 &&
                             SemanticId.IsValid(ActionId) && DefinitionVersion > 0;
}
internal sealed record PlayerIntentRequest(
    Guid AvatarEntityId,
    string ZoneKey,
    long Sequence,
    float MoveX,
    float MoveZ,
    bool Jump)
{
    public bool IsValid => AvatarEntityId != Guid.Empty
                           && Sequence > 0
                           && float.IsFinite(MoveX)
                           && float.IsFinite(MoveZ)
                           && MathF.Abs(MoveX) <= 1f
                           && MathF.Abs(MoveZ) <= 1f;
}
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
    int DefinitionVersion);
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
    string ZoneKey,
    double SpawnPositionX,
    double SpawnPositionY,
    double SpawnPositionZ,
    double? MovementSpeed);
internal sealed record WorldPlayerAvatarRegistration(Guid AvatarEntityId, Guid UserId);

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
