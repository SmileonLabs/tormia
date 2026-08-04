using System.Collections.Concurrent;
using Tormia.Ontology.Realtime.Protocol;

internal enum PlayerMotionIntentTransport
{
    Http = 0,
    Udp = 1
}

internal sealed record PlayerMotionActionContract(
    string PackageId,
    string PackageVersion,
    string ActionId,
    int DefinitionVersion);

internal sealed record PlayerMotionIntentCandidate(
    Guid WorldId,
    Guid UserId,
    Guid AvatarEntityId,
    string ZoneKey,
    Guid RuntimeSessionId,
    long Sequence,
    float MoveX,
    float MoveZ,
    float RequestedSpeed,
    bool HasDestination,
    float DestinationX,
    float DestinationZ,
    float DestinationStopDistance,
    PlayerMotionIntentTransport Transport,
    PlayerMotionActionContract? ClaimedContract = null,
    Guid AuthenticatedTransportSessionId = default,
    ulong TransportGeneration = 0,
    long WriterEpoch = 0)
{
    internal bool IsStructurallyValid =>
        WorldId != Guid.Empty &&
        UserId != Guid.Empty &&
        AvatarEntityId != Guid.Empty &&
        RuntimeSessionId != Guid.Empty &&
        WriterEpoch > 0 &&
        Sequence > 0 &&
        SemanticId.IsValid(ZoneKey) &&
        float.IsFinite(MoveX) && MathF.Abs(MoveX) <= 1f &&
        float.IsFinite(MoveZ) && MathF.Abs(MoveZ) <= 1f &&
        float.IsFinite(RequestedSpeed) && RequestedSpeed is >= 0f and <= 100f &&
        (!HasDestination ||
         (float.IsFinite(DestinationX) &&
          float.IsFinite(DestinationZ) &&
          float.IsFinite(DestinationStopDistance) &&
          DestinationStopDistance is >= 0f and <= 100f)) &&
        (Transport == PlayerMotionIntentTransport.Http ||
         (Transport == PlayerMotionIntentTransport.Udp &&
          AuthenticatedTransportSessionId != Guid.Empty &&
          TransportGeneration > 0));
}

internal sealed record PlayerMotionIntentAdmissionResult(
    bool Accepted,
    string? RejectionCode)
{
    internal static PlayerMotionIntentAdmissionResult Reject(string code) =>
        new(false, code);

    internal static PlayerMotionIntentAdmissionResult Success { get; } =
        new(true, null);
}

internal interface IPlayerMotionIntentAuthorityGateway
{
    Task<bool> AvatarBelongsToUser(
        Guid worldId, Guid userId, Guid avatarEntityId,
        CancellationToken cancellationToken);
    Task<bool> ZoneExists(
        Guid worldId, Guid userId, string zoneKey,
        CancellationToken cancellationToken);
    Task<PlayerMotionActionContract?> ResolveCurrentContract(
        Guid worldId, Guid userId, Guid avatarEntityId, string zoneKey,
        CancellationToken cancellationToken);
    Task<long?> GetCurrentRevision(
        Guid worldId, CancellationToken cancellationToken);
    Task<ActionPreviewEvaluationResult> Preview(
        Guid worldId, Guid userId, Guid avatarEntityId,
        PlayerMotionActionContract contract,
        CancellationToken cancellationToken);
}

internal sealed class WorldAuthorityPlayerMotionIntentGateway(
    WorldAuthorityRepository repository) : IPlayerMotionIntentAuthorityGateway
{
    public Task<bool> AvatarBelongsToUser(
        Guid worldId, Guid userId, Guid avatarEntityId,
        CancellationToken cancellationToken) =>
        repository.AvatarBelongsToUser(
            worldId, userId, avatarEntityId, cancellationToken);

    public Task<bool> ZoneExists(
        Guid worldId, Guid userId, string zoneKey,
        CancellationToken cancellationToken) =>
        repository.ZoneExists(worldId, userId, zoneKey, cancellationToken);

    public async Task<PlayerMotionActionContract?> ResolveCurrentContract(
        Guid worldId, Guid userId, Guid avatarEntityId, string zoneKey,
        CancellationToken cancellationToken)
    {
        var configurations =
            await repository.GetPlayerAvatarMotionConfigurations(
                worldId, zoneKey, cancellationToken);
        var matching = configurations
            .Where(value => value.AvatarEntityId == avatarEntityId &&
                            value.UserId == userId)
            .Take(2)
            .ToArray();
        if (matching.Length != 1) return null;
        var configuration = matching[0];
        if (configuration.MovementSpeed is not > 0d ||
            configuration.GravityAcceleration is not < 0d ||
            configuration.GroundStickVelocity is not <= 0d ||
            configuration.MaximumStepHeight is not >= 0d ||
            configuration.GroundClearance is not >= 0d ||
            !SemanticId.IsValid(configuration.PackageId) ||
            string.IsNullOrWhiteSpace(configuration.PackageVersion) ||
            !SemanticId.IsValid(configuration.LocomotionActionId) ||
            configuration.LocomotionActionDefinitionVersion <= 0)
        {
            return null;
        }
        return new(
            configuration.PackageId,
            configuration.PackageVersion,
            configuration.LocomotionActionId,
            configuration.LocomotionActionDefinitionVersion);
    }

    public Task<long?> GetCurrentRevision(
        Guid worldId, CancellationToken cancellationToken) =>
        repository.GetCurrentRevision(worldId, cancellationToken);

    public Task<ActionPreviewEvaluationResult> Preview(
        Guid worldId, Guid userId, Guid avatarEntityId,
        PlayerMotionActionContract contract,
        CancellationToken cancellationToken) =>
        repository.PreviewAction(
            worldId,
            userId,
            new ExecuteActionPayload(
                avatarEntityId,
                avatarEntityId,
                null,
                contract.PackageId,
                contract.PackageVersion,
                contract.ActionId,
                contract.DefinitionVersion),
            cancellationToken);
}

/// <summary>
/// One Authority admission boundary shared by reliable HTTP and authenticated
/// UDP. Transport supplies only an ephemeral canonical intent. The currently
/// authored locomotion contract and active runtime session remain authoritative.
/// </summary>
internal sealed class PlayerMotionIntentAdmissionService(
    IPlayerMotionIntentAuthorityGateway authority,
    IWorldPlayerMotionRuntimeRegistry motion,
    IWorldPlayerIntentRegistry intents,
    IWorldRevisionRuntimeRegistry revisions)
{
    private const int MaximumCachedContracts = 4096;
    private const int MaximumPendingContractBuilds = 1024;
    private readonly ConcurrentDictionary<ContractCacheKey, ContractCacheEntry>
        contractCache = new();
    private readonly ConcurrentDictionary<ContractCacheKey,
        Lazy<Task<ContractCacheEntry>>>
        contractBuilds = new();
    private readonly ConcurrentQueue<ContractCacheKey> cacheOrder = new();
    private readonly SemaphoreSlim contractBuildGate = new(16, 16);
    private readonly SemaphoreSlim pendingContractBuildSlots = new(
        MaximumPendingContractBuilds,
        MaximumPendingContractBuilds);

    internal async Task<PlayerMotionIntentAdmissionResult> Submit(
        PlayerMotionIntentCandidate candidate,
        CancellationToken cancellationToken,
        Func<CancellationToken, ValueTask<bool>>? finalTransportFence = null)
    {
        if (!candidate.IsStructurallyValid)
            return PlayerMotionIntentAdmissionResult.Reject(
                "invalid_player_intent");
        var activeMotion = await motion.Get(
            candidate.WorldId,
            candidate.AvatarEntityId,
            cancellationToken);
        if (activeMotion is null ||
            activeMotion.RuntimeSessionId != candidate.RuntimeSessionId ||
            !string.Equals(
                activeMotion.ZoneKey,
                candidate.ZoneKey,
                StringComparison.Ordinal))
        {
            return PlayerMotionIntentAdmissionResult.Reject(
                "player_runtime_session_mismatch");
        }
        if (candidate.Sequence <= activeMotion.LastProcessedIntentSequence)
            return PlayerMotionIntentAdmissionResult.Reject(
                "stale_player_intent");

        var contractResult = await ResolveValidatedContract(
            candidate, cancellationToken);
        if (contractResult.RejectionCode is not null)
            return PlayerMotionIntentAdmissionResult.Reject(
                contractResult.RejectionCode);
        var currentContract = contractResult.Contract!;
        if (candidate.ClaimedContract is not null &&
            candidate.ClaimedContract != currentContract)
        {
            return PlayerMotionIntentAdmissionResult.Reject(
                "locomotion_contract_mismatch");
        }

        if (finalTransportFence is not null &&
            !await finalTransportFence(cancellationToken))
        {
            return PlayerMotionIntentAdmissionResult.Reject(
                "stale_transport_generation");
        }

        var accepted = await intents.Submit(
            new WorldPlayerIntent(
                candidate.WorldId,
                candidate.UserId,
                candidate.AvatarEntityId,
                candidate.ZoneKey,
                candidate.Sequence,
                candidate.MoveX,
                candidate.MoveZ,
                candidate.RequestedSpeed,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                candidate.RuntimeSessionId,
                candidate.HasDestination,
                candidate.DestinationX,
                candidate.DestinationZ,
                candidate.DestinationStopDistance,
                candidate.Transport == PlayerMotionIntentTransport.Udp
                    ? "udp" : "http",
                candidate.AuthenticatedTransportSessionId,
                candidate.TransportGeneration,
                contractResult.WorldRevision,
                candidate.WriterEpoch),
            cancellationToken);
        return accepted
            ? PlayerMotionIntentAdmissionResult.Success
            : PlayerMotionIntentAdmissionResult.Reject("stale_player_intent");
    }

    private async Task<ContractCacheEntry> ResolveValidatedContract(
        PlayerMotionIntentCandidate candidate,
        CancellationToken cancellationToken)
    {
        var revision = await revisions.GetOrLoad(
            candidate.WorldId,
            token => authority.GetCurrentRevision(candidate.WorldId, token),
            cancellationToken);
        if (!revision.HasValue)
            return new(null, "locomotion_contract_revision_unavailable");
        var key = ContractCacheKey.From(candidate, revision.Value);
        if (contractCache.TryGetValue(key, out var cached)) return cached;
        Lazy<Task<ContractCacheEntry>> holder;
        if (!contractBuilds.TryGetValue(key, out holder!))
        {
            if (!pendingContractBuildSlots.Wait(0))
                return new(null, "locomotion_contract_build_capacity");
            var created = new Lazy<Task<ContractCacheEntry>>(
                () => BuildValidatedContract(candidate, revision.Value, key),
                LazyThreadSafetyMode.ExecutionAndPublication);
            holder = contractBuilds.GetOrAdd(key, created);
            if (!ReferenceEquals(holder, created))
                pendingContractBuildSlots.Release();
        }
        var build = holder.Value;
        try
        {
            return await build.WaitAsync(cancellationToken);
        }
        finally
        {
            if (build.IsCompleted)
                RemovePendingBuild(key, holder);
            else
                _ = build.ContinueWith(
                    (_, state) =>
                    {
                        var removal = ((PlayerMotionIntentAdmissionService Owner,
                            ContractCacheKey Key,
                            Lazy<Task<ContractCacheEntry>> Holder))state!;
                        removal.Owner.RemovePendingBuild(
                            removal.Key, removal.Holder);
                    },
                    (this, key, holder),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
    }

    private void RemovePendingBuild(
        ContractCacheKey key,
        Lazy<Task<ContractCacheEntry>> holder)
    {
        if (contractBuilds.TryRemove(
                new KeyValuePair<ContractCacheKey,
                    Lazy<Task<ContractCacheEntry>>>(key, holder)))
            pendingContractBuildSlots.Release();
    }

    private async Task<ContractCacheEntry> BuildValidatedContract(
        PlayerMotionIntentCandidate candidate,
        long revision,
        ContractCacheKey key)
    {
        await contractBuildGate.WaitAsync(CancellationToken.None);
        try
        {
            if (contractCache.TryGetValue(key, out var cached)) return cached;
            var currentRevision = await revisions.GetOrLoad(
                candidate.WorldId,
                token => authority.GetCurrentRevision(candidate.WorldId, token),
                CancellationToken.None);
            if (currentRevision != revision)
                return new(null, "locomotion_contract_revision_changed");
            ContractCacheEntry built;
            if (!await authority.AvatarBelongsToUser(
                    candidate.WorldId,
                    candidate.UserId,
                    candidate.AvatarEntityId,
                    CancellationToken.None))
                built = new(null, "avatar_not_owned");
            else if (!await authority.ZoneExists(
                         candidate.WorldId,
                         candidate.UserId,
                         candidate.ZoneKey,
                         CancellationToken.None))
                built = new(null, "zone_not_found");
            else
            {
                var contract = await authority.ResolveCurrentContract(
                    candidate.WorldId,
                    candidate.UserId,
                    candidate.AvatarEntityId,
                    candidate.ZoneKey,
                    CancellationToken.None);
                if (contract is null)
                    built = new(null, "locomotion_contract_missing");
                else
                {
                    var preview = await authority.Preview(
                        candidate.WorldId,
                        candidate.UserId,
                        candidate.AvatarEntityId,
                        contract,
                        CancellationToken.None);
                    if (!preview.Accepted)
                    {
                        // Backpressure, timeouts and other preview failures are
                        // not semantic contract state and must never poison the
                        // exact-revision cache.
                        return new(null, preview.RejectionCode ??
                            "locomotion_rule_rejected");
                    }
                    built = preview.MutationCount != 0
                        ? new(null, "locomotion_rule_must_be_ephemeral")
                        : new(contract, null, revision);
                }
            }
            var finalRevision = await revisions.GetOrLoad(
                candidate.WorldId,
                token => authority.GetCurrentRevision(candidate.WorldId, token),
                CancellationToken.None);
            if (finalRevision != revision)
                return new(null, "locomotion_contract_revision_changed");
            return Cache(key, built);
        }
        finally { contractBuildGate.Release(); }
    }

    private ContractCacheEntry Cache(
        ContractCacheKey key, ContractCacheEntry value)
    {
        contractCache[key] = value;
        cacheOrder.Enqueue(key);
        while (contractCache.Count > MaximumCachedContracts &&
               cacheOrder.TryDequeue(out var expired))
            contractCache.TryRemove(expired, out _);
        return value;
    }

    private sealed record ContractCacheEntry(
        PlayerMotionActionContract? Contract,
        string? RejectionCode,
        long WorldRevision = 0);

    private readonly record struct ContractCacheKey(
        Guid WorldId,
        Guid UserId,
        Guid AvatarEntityId,
        string ZoneKey,
        Guid RuntimeSessionId,
        PlayerMotionIntentTransport Transport,
        Guid AuthenticatedTransportSessionId,
        ulong TransportGeneration,
        long WriterEpoch,
        long WorldRevision)
    {
        internal static ContractCacheKey From(
            PlayerMotionIntentCandidate value, long revision) =>
            new(value.WorldId, value.UserId, value.AvatarEntityId,
                value.ZoneKey, value.RuntimeSessionId, value.Transport,
                value.AuthenticatedTransportSessionId,
                value.TransportGeneration, value.WriterEpoch, revision);
    }
}

/// <summary>
/// Drains the authenticated bounded UDP channel into the same Authority intent
/// admission used by HTTP. It emits no snapshots and owns no gameplay rule.
/// </summary>
internal sealed class AuthorityUdpMotionIntentConsumer(
    UdpAuthenticatedMotionIntentChannel channel,
    AuthorityUdpTransportGenerationFence generationFence,
    PlayerMotionIntentAdmissionService admission,
    IWorldPlayerIntentRegistry intents,
    AuthorityUdpListenerDiagnostics diagnostics,
    ILogger<AuthorityUdpMotionIntentConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await channel.Reader.WaitToReadAsync(stoppingToken))
            await ProcessAvailable(stoppingToken);
    }

    /// <summary>
    /// Deterministic bounded drain used by the hosted loop and transport fault
    /// tests. It preserves the production coalescing and Authority admission
    /// path; it is not a second gameplay or transport writer.
    /// </summary>
    internal async Task<int> ProcessAvailable(CancellationToken cancellationToken)
    {
        var latest = new Dictionary<IntentMailboxKey,
            AuthenticatedUdpMotionIntent>();
        var drained = 0;
        while (drained < 2048 && channel.Reader.TryRead(out var queued))
        {
            drained++;
            var key = IntentMailboxKey.From(queued);
            if (!latest.TryGetValue(key, out var prior) ||
                queued.Payload.InputSequence > prior.Payload.InputSequence)
            {
                if (prior is not null)
                    diagnostics.RecordMotionIntentRejected("coalesced");
                latest[key] = queued;
            }
            else
            {
                diagnostics.RecordMotionIntentRejected("coalesced");
            }
        }
        foreach (var value in latest.Values)
            await ProcessOne(value, cancellationToken);
        return drained;
    }

    private async Task ProcessOne(
        AuthenticatedUdpMotionIntent value,
        CancellationToken stoppingToken)
    {
        try
        {
            if (value.ValidationFence.AuthenticatedTransportSessionId !=
                    value.Binding.AuthenticatedTransportSessionId ||
                value.ValidationFence.TransportGeneration !=
                    value.Binding.TransportGeneration ||
                !await generationFence.IsCurrent(
                    value.Binding, stoppingToken))
            {
                diagnostics.RecordMotionIntentRejected("old_generation");
                return;
            }
            if (value.Payload.InputSequence is 0 or > long.MaxValue)
            {
                diagnostics.RecordMotionIntentRejected("invalid_intent");
                return;
            }

            var writer = await intents.GetWriter(
                value.Binding.WorldId,
                value.Binding.AvatarEntityId,
                stoppingToken);
            if (writer is null || writer.Mode != "udp" ||
                writer.RuntimeSessionId != value.Binding.RuntimeSessionId ||
                writer.AuthenticatedTransportSessionId !=
                    value.Binding.AuthenticatedTransportSessionId ||
                writer.TransportGeneration !=
                    value.Binding.TransportGeneration)
            {
                diagnostics.RecordMotionIntentRejected("writer_mismatch");
                return;
            }

            var result = await admission.Submit(
                    new PlayerMotionIntentCandidate(
                        value.Payload.WorldId,
                        value.Binding.UserId,
                        value.Payload.ActorEntityId,
                        value.Payload.ZoneKey,
                        value.Binding.RuntimeSessionId,
                        checked((long)value.Payload.InputSequence),
                        value.Payload.MoveX,
                        value.Payload.MoveZ,
                        value.Payload.RequestedSpeed,
                        value.Payload.HasDestination,
                        value.Payload.DestinationX,
                        value.Payload.DestinationZ,
                        value.Payload.DestinationStopDistance,
                        PlayerMotionIntentTransport.Udp,
                        null,
                        value.Binding.AuthenticatedTransportSessionId,
                        value.Binding.TransportGeneration,
                        writer.WriterEpoch),
                    stoppingToken,
                    token => generationFence.IsCurrent(value.Binding, token));
            if (result.Accepted)
                diagnostics.RecordMotionIntentPublished();
            else
                diagnostics.RecordMotionIntentRejected(result.RejectionCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            diagnostics.RecordMotionIntentRejected("failure");
            logger.LogWarning(exception,
                "Authenticated UDP motion intent failed closed.");
        }
    }

    private readonly record struct IntentMailboxKey(
        Guid WorldId,
        Guid AvatarEntityId,
        Guid RuntimeSessionId,
        Guid AuthenticatedTransportSessionId,
        ulong TransportGeneration)
    {
        internal static IntentMailboxKey From(
            AuthenticatedUdpMotionIntent value) =>
            new(value.Binding.WorldId, value.Binding.AvatarEntityId,
                value.Binding.RuntimeSessionId,
                value.Binding.AuthenticatedTransportSessionId,
                value.Binding.TransportGeneration);
    }
}
