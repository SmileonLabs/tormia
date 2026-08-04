using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

/// <summary>
/// Stores the latest controller input for a server-owned player avatar. Inputs
/// are ephemeral transport data, deliberately separate from commands, events,
/// world Facts, and rule bindings. A later authoritative motion step consumes
/// these samples and emits the resulting durable gameplay state when needed.
/// </summary>
internal sealed record WorldPlayerIntent(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    long Sequence,
    float MoveX,
    float MoveZ,
    float MoveSpeed,
    long ReceivedAtUnixMilliseconds,
    Guid RuntimeSessionId = default,
    bool HasDestination = false,
    float DestinationX = 0f,
    float DestinationZ = 0f,
    float DestinationStopDistance = 0f,
    string Transport = "http",
    Guid AuthenticatedTransportSessionId = default,
    ulong TransportGeneration = 0,
    long WorldRevision = 0,
    long WriterEpoch = 0);

internal sealed record PlayerMotionTransportWriterState(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    Guid RuntimeSessionId,
    string Mode,
    long WriterEpoch,
    long WorldRevision,
    Guid AuthenticatedTransportSessionId = default,
    ulong TransportGeneration = 0,
    Guid PreviousUdpTransportSessionId = default,
    ulong PreviousUdpTransportGeneration = 0);

internal sealed record PlayerMotionTransportTransitionResult(
    bool Accepted,
    string? RejectionCode,
    PlayerMotionTransportWriterState? Writer)
{
    internal static PlayerMotionTransportTransitionResult Reject(string code) =>
        new(false, code, null);
    internal static PlayerMotionTransportTransitionResult Reject(
        string code, PlayerMotionTransportWriterState? writer) =>
        new(false, code, writer);
    internal static PlayerMotionTransportTransitionResult Success(
        PlayerMotionTransportWriterState writer) => new(true, null, writer);
}

internal interface IWorldPlayerIntentRegistry
{
    string BackendName { get; }
    TimeSpan LeaseDuration { get; }
    Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken);
    Task<PlayerMotionTransportWriterState?> GetWriter(
        Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task<PlayerMotionTransportTransitionResult> InitializeHttpWriter(
        PlayerMotionTransportWriterState writer,
        CancellationToken cancellationToken);
    Task<PlayerMotionTransportTransitionResult> PromoteUdpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken);
    Task<PlayerMotionTransportTransitionResult> FallbackToHttpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken);
    Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken);
    Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken);
}

internal sealed class RedisWorldPlayerIntentRegistry(IConnectionMultiplexer redis) : IWorldPlayerIntentRegistry
{
    private const string SubmitLatestScript = """
        local motion = redis.call('GET', KEYS[2])
        if not motion then return 0 end
        local active = cjson.decode(motion)
        if active.runtimeSessionId ~= ARGV[1] then return 0 end
        if redis.call('EXISTS', KEYS[6]) == 1 then return 0 end
        local authorityRevision = redis.call('GET', KEYS[5])
        if not authorityRevision or
           tonumber(authorityRevision) ~= tonumber(ARGV[9]) then return 0 end
        if active.lastProcessedIntentSequence and
           tonumber(active.lastProcessedIntentSequence) >= tonumber(ARGV[2]) then
            return 0
        end
        local writer = redis.call('GET', KEYS[3])
        local writerState = nil
        if writer then writerState = cjson.decode(writer) end
        if not writerState or
           writerState.runtimeSessionId ~= ARGV[1] or
           writerState.worldId ~= ARGV[10] or
           writerState.userId ~= ARGV[11] or
           writerState.avatarEntityId ~= ARGV[12] or
           writerState.zoneKey ~= ARGV[13] or
           tonumber(writerState.writerEpoch) ~= tonumber(ARGV[8]) then return 0 end
        if ARGV[5] == 'udp' then
            if writerState.mode ~= 'udp' or
               writerState.authenticatedTransportSessionId ~= ARGV[6] or
               tonumber(writerState.transportGeneration) ~= tonumber(ARGV[7]) then return 0 end
            local currentGeneration = redis.call('GET', KEYS[4])
            if not currentGeneration or
               tonumber(currentGeneration) ~= tonumber(ARGV[7]) then return 0 end
            if redis.call('HGET', KEYS[7], 'world') ~= ARGV[10] or
               redis.call('HGET', KEYS[7], 'user') ~= ARGV[11] or
               redis.call('HGET', KEYS[7], 'avatar') ~= ARGV[12] or
               redis.call('HGET', KEYS[7], 'zone') ~= ARGV[13] or
               redis.call('HGET', KEYS[7], 'runtime') ~= ARGV[1] or
               redis.call('HGET', KEYS[7], 'transportSession') ~= ARGV[6] or
               tonumber(redis.call('HGET', KEYS[7], 'generation')) ~= tonumber(ARGV[7]) then return 0 end
        elseif ARGV[5] == 'http' then
            if writerState.mode ~= 'http' then return 0 end
        else return 0 end
        local current = redis.call('GET', KEYS[1])
        if current then
            local decoded = cjson.decode(current)
            if decoded.runtimeSessionId == ARGV[1] and decoded.sequence and tonumber(decoded.sequence) >= tonumber(ARGV[2]) then
                return 0
            end
        end
        writerState.worldRevision = tonumber(ARGV[9])
        redis.call('SET', KEYS[3], cjson.encode(writerState))
        redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[4])
        return 1
        """;
    private const string InitializeHttpWriterScript = """
        local motion = redis.call('GET', KEYS[2])
        if not motion or redis.call('EXISTS', KEYS[4]) == 1 then return 0 end
        local active = cjson.decode(motion)
        if active.runtimeSessionId ~= ARGV[1] or active.zoneKey ~= ARGV[2] then return 0 end
        local revision = redis.call('GET', KEYS[3])
        if not revision or tonumber(revision) ~= tonumber(ARGV[3]) then return 0 end
        local current = redis.call('GET', KEYS[1])
        if current then
            local writer = cjson.decode(current)
            if writer.runtimeSessionId == ARGV[1] and writer.mode == 'http' and
               tonumber(writer.writerEpoch) == 1 and
               tonumber(writer.worldRevision) == tonumber(ARGV[3]) then return 2 end
        end
        redis.call('SET', KEYS[1], ARGV[4])
        return 1
        """;
    private const string PromoteUdpWriterScript = """
        local writerText = redis.call('GET', KEYS[1])
        if not writerText then return -1 end
        local writer = cjson.decode(writerText)
        if writer.mode == 'udp' and writer.runtimeSessionId == ARGV[1] and
           writer.worldId == ARGV[8] and writer.userId == ARGV[9] and
           writer.avatarEntityId == ARGV[10] and writer.zoneKey == ARGV[2] and
           writer.authenticatedTransportSessionId == ARGV[4] and
           tonumber(writer.transportGeneration) == tonumber(ARGV[5]) and
           tonumber(writer.writerEpoch) == tonumber(ARGV[6]) + 1 then return 2 end
        local motion = redis.call('GET', KEYS[3])
        if not motion or redis.call('EXISTS', KEYS[5]) == 1 then return -2 end
        local active = cjson.decode(motion)
        if active.runtimeSessionId ~= ARGV[1] or active.zoneKey ~= ARGV[2] then return -2 end
        local revision = redis.call('GET', KEYS[4])
        if not revision or tonumber(revision) ~= tonumber(ARGV[7]) then return -3 end
        local generation = redis.call('GET', KEYS[6])
        if not generation or tonumber(generation) ~= tonumber(ARGV[5]) then return -4 end
        if redis.call('HGET', KEYS[7], 'world') ~= ARGV[8] or
           redis.call('HGET', KEYS[7], 'user') ~= ARGV[9] or
           redis.call('HGET', KEYS[7], 'avatar') ~= ARGV[10] or
           redis.call('HGET', KEYS[7], 'zone') ~= ARGV[2] or
           redis.call('HGET', KEYS[7], 'runtime') ~= ARGV[1] or
           redis.call('HGET', KEYS[7], 'transportSession') ~= ARGV[4] or
           tonumber(redis.call('HGET', KEYS[7], 'generation')) ~= tonumber(ARGV[5]) then return -4 end
        if writer.runtimeSessionId ~= ARGV[1] or writer.worldId ~= ARGV[8] or
           writer.userId ~= ARGV[9] or writer.avatarEntityId ~= ARGV[10] or
           writer.zoneKey ~= ARGV[2] or tonumber(writer.writerEpoch) ~= tonumber(ARGV[6]) or
           writer.mode ~= 'http' then return -5 end
        redis.call('DEL', KEYS[2])
        redis.call('SET', KEYS[1], ARGV[3])
        redis.call('PEXPIRE', KEYS[7], ARGV[11])
        redis.call('DEL', KEYS[9])
        redis.call('SREM', KEYS[8], KEYS[9])
        return 1
        """;
    private const string FallbackHttpWriterScript = """
        local writerText = redis.call('GET', KEYS[1])
        if not writerText then return -1 end
        local writer = cjson.decode(writerText)
        if writer.mode == 'http' and writer.runtimeSessionId == ARGV[1] and
           writer.worldId == ARGV[8] and writer.userId == ARGV[9] and
           writer.avatarEntityId == ARGV[10] and writer.zoneKey == ARGV[2] and
           tonumber(writer.writerEpoch) == tonumber(ARGV[6]) + 1 and
           writer.previousUdpTransportSessionId == ARGV[4] and
           tonumber(writer.previousUdpTransportGeneration) == tonumber(ARGV[5]) then return 2 end
        local motion = redis.call('GET', KEYS[3])
        if not motion or redis.call('EXISTS', KEYS[5]) == 1 then return -2 end
        local active = cjson.decode(motion)
        if active.runtimeSessionId ~= ARGV[1] or active.zoneKey ~= ARGV[2] then return -2 end
        local revision = redis.call('GET', KEYS[4])
        if not revision then return -3 end
        local exactScope = writer.runtimeSessionId == ARGV[1] and
           writer.worldId == ARGV[8] and writer.userId == ARGV[9] and
           writer.avatarEntityId == ARGV[10] and writer.zoneKey == ARGV[2]
        local recoverHttp = exactScope and writer.mode == 'http' and
           tonumber(writer.writerEpoch) == tonumber(ARGV[6])
        local transitionUdp = exactScope and writer.mode == 'udp' and
           writer.authenticatedTransportSessionId == ARGV[4] and
           tonumber(writer.transportGeneration) == tonumber(ARGV[5]) and
           tonumber(writer.writerEpoch) == tonumber(ARGV[6])
        if not recoverHttp and not transitionUdp then return -5 end
        local activeTicket = redis.call('GET', KEYS[8])
        if activeTicket then redis.call('DEL', activeTicket) end
        redis.call('DEL', KEYS[8])
        redis.call('INCR', KEYS[6])
        redis.call('DEL', KEYS[2])
        local transportKeys = redis.call('SMEMBERS', KEYS[7])
        for _, key in ipairs(transportKeys) do redis.call('DEL', key) end
        redis.call('DEL', KEYS[7])
        if recoverHttp then
            writer.worldRevision = tonumber(revision)
            redis.call('SET', KEYS[1], cjson.encode(writer))
            return 3
        end
        local nextWriter = cjson.decode(ARGV[3])
        nextWriter.worldRevision = tonumber(revision)
        redis.call('SET', KEYS[1], cjson.encode(nextWriter))
        return 1
        """;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase database = redis.GetDatabase();

    public string BackendName => "redis";
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(3);

    public async Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(intent, JsonOptions);
        var result = await database.ScriptEvaluateAsync(
            SubmitLatestScript,
            new RedisKey[] {
                Key(intent.WorldId, intent.AvatarEntityId),
                MotionKey(intent.WorldId, intent.AvatarEntityId),
                WriterKey(intent.WorldId, intent.AvatarEntityId),
                TransportGenerationKey(intent),
                WorldRevisionKey(intent.WorldId),
                PendingWorldRevisionKey(intent.WorldId),
                ConnectedTransportKey(
                    intent.AuthenticatedTransportSessionId) },
            new RedisValue[] {
                intent.RuntimeSessionId.ToString("D"),
                intent.Sequence,
                json,
                (long)LeaseDuration.TotalMilliseconds,
                intent.Transport,
                intent.AuthenticatedTransportSessionId.ToString("D"),
                intent.TransportGeneration,
                intent.WriterEpoch,
                intent.WorldRevision,
                intent.WorldId.ToString("D"),
                intent.UserId.ToString("D"),
                intent.AvatarEntityId.ToString("D"),
                intent.ZoneKey });
        return (int)result == 1;
    }

    public async Task<PlayerMotionTransportWriterState?> GetWriter(
        Guid worldId, Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync(
            WriterKey(worldId, avatarEntityId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<PlayerMotionTransportWriterState>(
                value!, JsonOptions);
    }

    public async Task<PlayerMotionTransportTransitionResult> InitializeHttpWriter(
        PlayerMotionTransportWriterState writer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidInitialWriter(writer))
            return PlayerMotionTransportTransitionResult.Reject(
                "invalid_transport_writer_initialization");
        var normalized = writer with
        {
            Mode = "http",
            WriterEpoch = 1,
            AuthenticatedTransportSessionId = Guid.Empty,
            TransportGeneration = 0,
            PreviousUdpTransportSessionId = Guid.Empty,
            PreviousUdpTransportGeneration = 0
        };
        var result = (int)await database.ScriptEvaluateAsync(
            InitializeHttpWriterScript,
            new RedisKey[]
            {
                WriterKey(writer.WorldId, writer.AvatarEntityId),
                MotionKey(writer.WorldId, writer.AvatarEntityId),
                WorldRevisionKey(writer.WorldId),
                PendingWorldRevisionKey(writer.WorldId)
            },
            new RedisValue[]
            {
                writer.RuntimeSessionId.ToString("D"), writer.ZoneKey,
                writer.WorldRevision,
                JsonSerializer.Serialize(normalized, JsonOptions)
            });
        return result is 1 or 2
            ? PlayerMotionTransportTransitionResult.Success(normalized)
            : PlayerMotionTransportTransitionResult.Reject(
                "transport_writer_initialization_fence_failed");
    }

    public Task<PlayerMotionTransportTransitionResult> PromoteUdpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken) =>
        TransitionUdpWriter(
            binding, expectedWriterEpoch, expectedWorldRevision,
            promote: true, cancellationToken);

    public Task<PlayerMotionTransportTransitionResult> FallbackToHttpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken) =>
        TransitionUdpWriter(
            binding, expectedWriterEpoch, expectedWorldRevision,
            promote: false, cancellationToken);

    private async Task<PlayerMotionTransportTransitionResult> TransitionUdpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        bool promote,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidTransition(
                binding, expectedWriterEpoch, expectedWorldRevision))
            return PlayerMotionTransportTransitionResult.Reject(
                "invalid_transport_writer_transition");
        var next = new PlayerMotionTransportWriterState(
            binding.WorldId, binding.UserId, binding.AvatarEntityId,
            binding.ZoneKey, binding.RuntimeSessionId,
            promote ? "udp" : "http",
            checked(expectedWriterEpoch + 1),
            expectedWorldRevision,
            promote ? binding.AuthenticatedTransportSessionId : Guid.Empty,
            promote ? binding.TransportGeneration : 0,
            promote ? Guid.Empty : binding.AuthenticatedTransportSessionId,
            promote ? 0 : binding.TransportGeneration);
        var scope = TransportScopePrefix(binding);
        var keys = promote
            ? new RedisKey[]
            {
                WriterKey(binding.WorldId, binding.AvatarEntityId),
                Key(binding.WorldId, binding.AvatarEntityId),
                MotionKey(binding.WorldId, binding.AvatarEntityId),
                WorldRevisionKey(binding.WorldId),
                PendingWorldRevisionKey(binding.WorldId),
                scope + ":generation",
                ConnectedTransportKey(binding.AuthenticatedTransportSessionId),
                scope + ":promoted",
                PromotedTransportKey(binding.AuthenticatedTransportSessionId)
            }
            : new RedisKey[]
            {
                WriterKey(binding.WorldId, binding.AvatarEntityId),
                Key(binding.WorldId, binding.AvatarEntityId),
                MotionKey(binding.WorldId, binding.AvatarEntityId),
                WorldRevisionKey(binding.WorldId),
                PendingWorldRevisionKey(binding.WorldId),
                scope + ":generation",
                scope + ":promoted",
                scope + ":active"
            };
        var result = (int)await database.ScriptEvaluateAsync(
            promote ? PromoteUdpWriterScript : FallbackHttpWriterScript,
            keys,
            new RedisValue[]
            {
                binding.RuntimeSessionId.ToString("D"), binding.ZoneKey,
                JsonSerializer.Serialize(next, JsonOptions),
                binding.AuthenticatedTransportSessionId.ToString("D"),
                binding.TransportGeneration, expectedWriterEpoch,
                expectedWorldRevision, binding.WorldId.ToString("D"),
                binding.UserId.ToString("D"),
                binding.AvatarEntityId.ToString("D"),
                (long)TimeSpan.FromDays(30).TotalMilliseconds
            });
        if (result is 1 or 2 or 3)
        {
            var current = await GetWriter(
                binding.WorldId, binding.AvatarEntityId, cancellationToken);
            return current is null
                ? PlayerMotionTransportTransitionResult.Reject(
                    "transport_writer_not_initialized")
                : PlayerMotionTransportTransitionResult.Success(current);
        }
        var rejectionCode = result switch
        {
            -2 => "player_runtime_session_mismatch",
            -3 => "transport_world_revision_mismatch",
            -4 => "transport_binding_not_promoted",
            -5 => "transport_writer_mismatch",
            _ => "transport_writer_not_initialized"
        };
        var authoritativeWriter = await GetWriter(
            binding.WorldId, binding.AvatarEntityId, cancellationToken);
        if (authoritativeWriter?.WorldId != binding.WorldId ||
            authoritativeWriter.UserId != binding.UserId ||
            authoritativeWriter.AvatarEntityId != binding.AvatarEntityId ||
            authoritativeWriter.RuntimeSessionId != binding.RuntimeSessionId ||
            !string.Equals(authoritativeWriter.ZoneKey, binding.ZoneKey,
                StringComparison.Ordinal))
            authoritativeWriter = null;
        return PlayerMotionTransportTransitionResult.Reject(
            rejectionCode, authoritativeWriter);
    }

    public async Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var value = await database.StringGetAsync(Key(worldId, avatarEntityId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<WorldPlayerIntent>(value!, JsonOptions);
    }

    public async Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        await database.KeyDeleteAsync(new RedisKey[] {
            Key(worldId, avatarEntityId), WriterKey(worldId, avatarEntityId) });
    }

    public async Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken)
    {
        const string script = """
            local current = redis.call('GET', KEYS[1])
            local removed = 0
            if current then
                local decoded = cjson.decode(current)
                if decoded.runtimeSessionId == ARGV[1] then
                    redis.call('DEL', KEYS[1])
                    removed = 1
                end
            end
            local writer = redis.call('GET', KEYS[2])
            if writer then
                local writerState = cjson.decode(writer)
                if writerState.runtimeSessionId == ARGV[1] then
                    redis.call('DEL', KEYS[2])
                    removed = 1
                end
            end
            return removed
            """;
        await database.ScriptEvaluateAsync(script,
            new RedisKey[] {
                Key(worldId, avatarEntityId), WriterKey(worldId, avatarEntityId) },
            new RedisValue[] { runtimeSessionId.ToString("D") });
    }

    private static string Key(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":intent";
    private static string MotionKey(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":motion";
    private static string WriterKey(Guid worldId, Guid avatarEntityId) =>
        "tormia:world:" + worldId.ToString("N") + ":avatar:" + avatarEntityId.ToString("N") + ":intent-writer";
    private static string TransportGenerationKey(WorldPlayerIntent intent) =>
        "tormia:udp-ticket:scope:" + intent.WorldId.ToString("N") + ":" +
        intent.UserId.ToString("N") + ":" + intent.AvatarEntityId.ToString("N") +
        ":" + intent.RuntimeSessionId.ToString("N") + ":generation";
    private static string WorldRevisionKey(Guid worldId) =>
        $"tormia:world:{worldId:N}:revision";
    private static string PendingWorldRevisionKey(Guid worldId) =>
        $"tormia:world:{worldId:N}:revision:pending";
    private static string TransportScopePrefix(UdpTransportTicketBinding binding) =>
        $"tormia:udp-ticket:scope:{binding.WorldId:N}:{binding.UserId:N}:" +
        $"{binding.AvatarEntityId:N}:{binding.RuntimeSessionId:N}";
    private static string PromotedTransportKey(Guid transportSessionId) =>
        $"tormia:udp-transport:active:{transportSessionId:N}";
    private static string ConnectedTransportKey(Guid transportSessionId) =>
        $"tormia:udp-transport:connected:{transportSessionId:N}";
    private static bool IsValidInitialWriter(
        PlayerMotionTransportWriterState value) =>
        value.WorldId != Guid.Empty && value.UserId != Guid.Empty &&
        value.AvatarEntityId != Guid.Empty && value.RuntimeSessionId != Guid.Empty &&
        SemanticId.IsValid(value.ZoneKey) && value.WorldRevision >= 0;
    private static bool IsValidTransition(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision) =>
        binding.WorldId != Guid.Empty && binding.UserId != Guid.Empty &&
        binding.AvatarEntityId != Guid.Empty &&
        binding.RuntimeSessionId != Guid.Empty &&
        binding.AuthenticatedTransportSessionId != Guid.Empty &&
        binding.TransportGeneration > 0 &&
        SemanticId.IsValid(binding.ZoneKey) &&
        expectedWriterEpoch > 0 && expectedWorldRevision >= 0;
}

internal sealed class InMemoryWorldPlayerIntentRegistry(
    IWorldPlayerMotionRuntimeRegistry? motion = null,
    IUdpTransportTicketStore? tickets = null,
    IWorldRevisionRuntimeRegistry? revisions = null) : IWorldPlayerIntentRegistry
{
    private readonly ConcurrentDictionary<string, StoredIntent> intents = new();
    private readonly ConcurrentDictionary<string, PlayerMotionTransportWriterState> writers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    public string BackendName => "in_memory_development";
    public TimeSpan LeaseDuration => TimeSpan.FromSeconds(3);

    public async Task<bool> Submit(WorldPlayerIntent intent, CancellationToken cancellationToken)
    {
        var key = Key(intent.WorldId, intent.AvatarEntityId);
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (motion is not null)
            {
                var active = await motion.Get(
                    intent.WorldId, intent.AvatarEntityId, cancellationToken);
                if (active is null ||
                    active.RuntimeSessionId != intent.RuntimeSessionId ||
                    active.LastProcessedIntentSequence >= intent.Sequence)
                    return false;
            }
            if (intent.Transport is not ("http" or "udp")) return false;
            var now = DateTimeOffset.UtcNow;
            bool Commit()
            {
                if (!writers.TryGetValue(key, out var writer) ||
                    writer.WorldId != intent.WorldId ||
                    writer.UserId != intent.UserId ||
                    writer.AvatarEntityId != intent.AvatarEntityId ||
                    writer.RuntimeSessionId != intent.RuntimeSessionId ||
                    !string.Equals(writer.ZoneKey, intent.ZoneKey,
                        StringComparison.Ordinal) ||
                    writer.WriterEpoch != intent.WriterEpoch ||
                    !string.Equals(writer.Mode, intent.Transport,
                        StringComparison.Ordinal) ||
                    (intent.Transport == "udp" &&
                     (writer.AuthenticatedTransportSessionId !=
                          intent.AuthenticatedTransportSessionId ||
                      writer.TransportGeneration != intent.TransportGeneration)))
                    return false;
                if (intents.TryGetValue(key, out var current) &&
                    current.ExpiresAt > now &&
                    current.Intent.RuntimeSessionId == intent.RuntimeSessionId &&
                    current.Intent.Sequence >= intent.Sequence)
                    return false;
                writers[key] = writer with
                {
                    WorldRevision = intent.WorldRevision
                };
                intents[key] = new StoredIntent(intent, now + LeaseDuration);
                return true;
            }
            bool CommitAtRevision()
            {
                if (revisions is null) return Commit();
                return revisions is IWorldRevisionAtomicBoundary boundary &&
                       boundary.TryExecuteIfCurrent(
                           intent.WorldId, intent.WorldRevision, Commit);
            }
            if (intent.Transport == "udp")
                return tickets is IUdpTransportGenerationAtomicBoundary boundary &&
                       boundary.TryExecuteIfConnected(
                           new UdpTransportTicketBinding(
                               intent.WorldId, intent.UserId,
                               intent.AvatarEntityId, intent.ZoneKey,
                               intent.RuntimeSessionId,
                               intent.TransportGeneration,
                               intent.AuthenticatedTransportSessionId,
                               Tormia.Ontology.Realtime.Protocol
                                   .RealtimeWireContract.ProtocolVersion),
                           CommitAtRevision);
            return CommitAtRevision();
        }
        finally { gate.Release(); }
    }

    public Task<WorldPlayerIntent?> Get(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        if (!intents.TryGetValue(key, out var stored) || stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            intents.TryRemove(key, out _);
            return Task.FromResult<WorldPlayerIntent?>(null);
        }
        return Task.FromResult<WorldPlayerIntent?>(stored.Intent);
    }

    public async Task Clear(Guid worldId, Guid avatarEntityId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            intents.TryRemove(key, out _);
            writers.TryRemove(key, out _);
        }
        finally { gate.Release(); }
    }

    public Task<PlayerMotionTransportWriterState?> GetWriter(
        Guid worldId, Guid avatarEntityId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        writers.TryGetValue(Key(worldId, avatarEntityId), out var writer);
        return Task.FromResult(writer);
    }

    public async Task<PlayerMotionTransportTransitionResult> InitializeHttpWriter(
        PlayerMotionTransportWriterState writer,
        CancellationToken cancellationToken)
    {
        if (!IsValidInitialWriter(writer))
            return PlayerMotionTransportTransitionResult.Reject(
                "invalid_transport_writer_initialization");
        var key = Key(writer.WorldId, writer.AvatarEntityId);
        var avatarGate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await avatarGate.WaitAsync(cancellationToken);
        try
        {
            if (motion is not null)
            {
                var active = await motion.Get(
                    writer.WorldId, writer.AvatarEntityId, cancellationToken);
                if (active is null ||
                    active.RuntimeSessionId != writer.RuntimeSessionId ||
                    !string.Equals(active.ZoneKey, writer.ZoneKey,
                        StringComparison.Ordinal))
                    return PlayerMotionTransportTransitionResult.Reject(
                        "player_runtime_session_mismatch");
            }
            var normalized = writer with
            {
                Mode = "http", WriterEpoch = 1,
                AuthenticatedTransportSessionId = Guid.Empty,
                TransportGeneration = 0,
                PreviousUdpTransportSessionId = Guid.Empty,
                PreviousUdpTransportGeneration = 0
            };
            bool Commit()
            {
                if (writers.TryGetValue(key, out var current) &&
                    current.RuntimeSessionId == normalized.RuntimeSessionId &&
                    current.Mode == "http" && current.WriterEpoch == 1 &&
                    current.WorldRevision == normalized.WorldRevision)
                    return true;
                writers[key] = normalized;
                return true;
            }
            var committed = revisions is null
                ? Commit()
                : revisions is IWorldRevisionAtomicBoundary boundary &&
                  boundary.TryExecuteIfCurrent(
                      writer.WorldId, writer.WorldRevision, Commit);
            return committed
                ? PlayerMotionTransportTransitionResult.Success(normalized)
                : PlayerMotionTransportTransitionResult.Reject(
                    "transport_writer_initialization_fence_failed");
        }
        finally { avatarGate.Release(); }
    }

    public Task<PlayerMotionTransportTransitionResult> PromoteUdpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken) =>
        TransitionUdpWriter(binding, expectedWriterEpoch,
            expectedWorldRevision, true, cancellationToken);

    public Task<PlayerMotionTransportTransitionResult> FallbackToHttpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        CancellationToken cancellationToken) =>
        TransitionUdpWriter(binding, expectedWriterEpoch,
            expectedWorldRevision, false, cancellationToken);

    private async Task<PlayerMotionTransportTransitionResult> TransitionUdpWriter(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision,
        bool promote,
        CancellationToken cancellationToken)
    {
        if (!IsValidTransition(binding, expectedWriterEpoch,
                expectedWorldRevision))
            return PlayerMotionTransportTransitionResult.Reject(
                "invalid_transport_writer_transition");
        if (tickets is not IUdpTransportGenerationAtomicBoundary ticketBoundary)
            return PlayerMotionTransportTransitionResult.Reject(
                "transport_atomic_boundary_unavailable");
        if (motion is not null)
        {
            var active = await motion.Get(
                binding.WorldId, binding.AvatarEntityId, cancellationToken);
            if (active is null ||
                active.RuntimeSessionId != binding.RuntimeSessionId ||
                !string.Equals(active.ZoneKey, binding.ZoneKey,
                    StringComparison.Ordinal))
                return PlayerMotionTransportTransitionResult.Reject(
                    "player_runtime_session_mismatch");
        }
        var key = Key(binding.WorldId, binding.AvatarEntityId);
        var avatarGate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await avatarGate.WaitAsync(cancellationToken);
        PlayerMotionTransportWriterState? accepted = null;
        try
        {
            if (writers.TryGetValue(key, out var existing) &&
                existing.WorldId == binding.WorldId &&
                existing.UserId == binding.UserId &&
                existing.AvatarEntityId == binding.AvatarEntityId &&
                existing.RuntimeSessionId == binding.RuntimeSessionId &&
                string.Equals(existing.ZoneKey, binding.ZoneKey,
                    StringComparison.Ordinal) &&
                existing.WriterEpoch == expectedWriterEpoch + 1 &&
                ((promote && existing.Mode == "udp" &&
                  existing.AuthenticatedTransportSessionId ==
                      binding.AuthenticatedTransportSessionId &&
                  existing.TransportGeneration == binding.TransportGeneration) ||
                 (!promote && existing.Mode == "http" &&
                  existing.PreviousUdpTransportSessionId ==
                      binding.AuthenticatedTransportSessionId &&
                  existing.PreviousUdpTransportGeneration ==
                      binding.TransportGeneration)))
                return PlayerMotionTransportTransitionResult.Success(existing);
            bool Commit(long authoritativeRevision)
            {
                if (!writers.TryGetValue(key, out var current)) return false;
                if (promote && current.Mode == "udp" &&
                    current.WorldId == binding.WorldId &&
                    current.UserId == binding.UserId &&
                    current.AvatarEntityId == binding.AvatarEntityId &&
                    current.RuntimeSessionId == binding.RuntimeSessionId &&
                    string.Equals(current.ZoneKey, binding.ZoneKey,
                        StringComparison.Ordinal) &&
                    current.AuthenticatedTransportSessionId ==
                        binding.AuthenticatedTransportSessionId &&
                    current.TransportGeneration == binding.TransportGeneration &&
                    current.WriterEpoch == expectedWriterEpoch + 1)
                {
                    accepted = current;
                    return true;
                }
                if (!promote && current.Mode == "http" &&
                    current.WorldId == binding.WorldId &&
                    current.UserId == binding.UserId &&
                    current.AvatarEntityId == binding.AvatarEntityId &&
                    current.RuntimeSessionId == binding.RuntimeSessionId &&
                    string.Equals(current.ZoneKey, binding.ZoneKey,
                        StringComparison.Ordinal) &&
                    current.WriterEpoch == expectedWriterEpoch)
                {
                    accepted = current with
                    {
                        WorldRevision = authoritativeRevision
                    };
                    writers[key] = accepted;
                    intents.TryRemove(key, out _);
                    return true;
                }
                if (!promote && current.Mode == "http" &&
                    current.WorldId == binding.WorldId &&
                    current.UserId == binding.UserId &&
                    current.AvatarEntityId == binding.AvatarEntityId &&
                    current.RuntimeSessionId == binding.RuntimeSessionId &&
                    string.Equals(current.ZoneKey, binding.ZoneKey,
                        StringComparison.Ordinal) &&
                    current.PreviousUdpTransportSessionId ==
                        binding.AuthenticatedTransportSessionId &&
                    current.PreviousUdpTransportGeneration ==
                        binding.TransportGeneration &&
                    current.WriterEpoch == expectedWriterEpoch + 1)
                {
                    accepted = current;
                    return true;
                }
                if (current.WorldId != binding.WorldId ||
                    current.UserId != binding.UserId ||
                    current.AvatarEntityId != binding.AvatarEntityId ||
                    current.RuntimeSessionId != binding.RuntimeSessionId ||
                    !string.Equals(current.ZoneKey, binding.ZoneKey,
                        StringComparison.Ordinal) ||
                    current.WriterEpoch != expectedWriterEpoch ||
                    (promote
                        ? current.Mode != "http"
                        : (current.Mode != "udp" ||
                      current.AuthenticatedTransportSessionId !=
                          binding.AuthenticatedTransportSessionId ||
                      current.TransportGeneration != binding.TransportGeneration)))
                    return false;
                accepted = new(
                    binding.WorldId, binding.UserId, binding.AvatarEntityId,
                    binding.ZoneKey, binding.RuntimeSessionId,
                    promote ? "udp" : "http",
                    checked(expectedWriterEpoch + 1),
                    authoritativeRevision,
                    promote ? binding.AuthenticatedTransportSessionId : Guid.Empty,
                    promote ? binding.TransportGeneration : 0,
                    promote ? Guid.Empty : binding.AuthenticatedTransportSessionId,
                    promote ? 0 : binding.TransportGeneration);
                writers[key] = accepted;
                intents.TryRemove(key, out _);
                return true;
            }
            bool CommitAtRevision()
            {
                if (revisions is null)
                    return Commit(expectedWorldRevision);
                if (revisions is not IWorldRevisionAtomicBoundary boundary)
                    return false;
                return promote
                    ? boundary.TryExecuteIfCurrent(
                        binding.WorldId, expectedWorldRevision,
                        () => Commit(expectedWorldRevision))
                    : boundary.TryExecuteAtCurrent(
                        binding.WorldId, Commit);
            }
            var committed = promote
                ? ticketBoundary.TryExecutePromotionIfCurrentCandidate(
                    binding, CommitAtRevision)
                : ticketBoundary.TryExecuteFallbackGenerationFence(
                    binding, CommitAtRevision);
            if (!committed || accepted is null)
            {
                writers.TryGetValue(key, out var authoritativeWriter);
                return PlayerMotionTransportTransitionResult.Reject(
                    "transport_writer_transition_fence_failed",
                    authoritativeWriter?.WorldId == binding.WorldId &&
                    authoritativeWriter.UserId == binding.UserId &&
                    authoritativeWriter.AvatarEntityId ==
                        binding.AvatarEntityId &&
                    authoritativeWriter.RuntimeSessionId ==
                        binding.RuntimeSessionId &&
                    string.Equals(authoritativeWriter.ZoneKey,
                        binding.ZoneKey, StringComparison.Ordinal)
                        ? authoritativeWriter
                        : null);
            }
        }
        finally { avatarGate.Release(); }
        return PlayerMotionTransportTransitionResult.Success(accepted);
    }

    public async Task ClearIfSessionMatches(Guid worldId, Guid avatarEntityId,
        Guid runtimeSessionId, CancellationToken cancellationToken)
    {
        var key = Key(worldId, avatarEntityId);
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (intents.TryGetValue(key, out var current) &&
                current.Intent.RuntimeSessionId == runtimeSessionId)
                intents.TryRemove(key, out _);
            if (writers.TryGetValue(key, out var writer) &&
                writer.RuntimeSessionId == runtimeSessionId)
                writers.TryRemove(key, out _);
        }
        finally { gate.Release(); }
    }

    private static string Key(Guid worldId, Guid avatarEntityId) => worldId.ToString("N") + ":" + avatarEntityId.ToString("N");
    private sealed record StoredIntent(WorldPlayerIntent Intent, DateTimeOffset ExpiresAt);
    private static bool IsValidInitialWriter(
        PlayerMotionTransportWriterState value) =>
        value.WorldId != Guid.Empty && value.UserId != Guid.Empty &&
        value.AvatarEntityId != Guid.Empty && value.RuntimeSessionId != Guid.Empty &&
        SemanticId.IsValid(value.ZoneKey) && value.WorldRevision >= 0;
    private static bool IsValidTransition(
        UdpTransportTicketBinding binding,
        long expectedWriterEpoch,
        long expectedWorldRevision) =>
        binding.WorldId != Guid.Empty && binding.UserId != Guid.Empty &&
        binding.AvatarEntityId != Guid.Empty && binding.RuntimeSessionId != Guid.Empty &&
        binding.AuthenticatedTransportSessionId != Guid.Empty &&
        binding.TransportGeneration > 0 && SemanticId.IsValid(binding.ZoneKey) &&
        expectedWriterEpoch > 0 && expectedWorldRevision >= 0;
}
