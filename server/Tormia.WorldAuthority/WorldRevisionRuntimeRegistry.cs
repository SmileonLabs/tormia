using System.Collections.Concurrent;
using StackExchange.Redis;

internal interface IWorldRevisionRuntimeRegistry
{
    Task<long?> GetOrLoad(Guid worldId,
        Func<CancellationToken, Task<long?>> durableLoader,
        CancellationToken cancellationToken);
    Task MarkPending(Guid worldId, long currentRevision, long nextRevision,
        CancellationToken cancellationToken);
    Task ObserveCommitted(Guid worldId, long revision,
        CancellationToken cancellationToken);
}

/// <summary>Process-local equivalent of the Redis revision-fence Lua CAS.</summary>
internal interface IWorldRevisionAtomicBoundary
{
    bool TryExecuteIfCurrent(Guid worldId, long revision, Func<bool> operation);
    bool TryExecuteAtCurrent(Guid worldId, Func<long, bool> operation);
}

internal sealed class RedisWorldRevisionRuntimeRegistry(
    IConnectionMultiplexer redis, TimeProvider? clock = null) :
    IWorldRevisionRuntimeRegistry
{
    private static readonly TimeSpan AbandonedPendingGrace = TimeSpan.FromSeconds(30);
    private const string MarkPendingScript = """
        redis.call('SET', KEYS[1], ARGV[1])
        redis.call('HSET', KEYS[2], 'target', ARGV[2], 'since', ARGV[3])
        return 1
        """;
    private const string ObserveScript = """
        local current = redis.call('GET', KEYS[1])
        if not current or tonumber(ARGV[1]) > tonumber(current) then
            redis.call('SET', KEYS[1], ARGV[1])
        end
        local target = redis.call('HGET', KEYS[2], 'target')
        if target and tonumber(ARGV[1]) >= tonumber(target) then
            redis.call('DEL', KEYS[2])
        end
        return 1
        """;
    private const string SeedScript = """
        redis.call('SET', KEYS[1], ARGV[1], 'NX')
        return redis.call('GET', KEYS[1])
        """;
    private const string ReconcileRollbackScript = """
        local target = redis.call('HGET', KEYS[2], 'target')
        if target and tonumber(target) == tonumber(ARGV[1]) then
            redis.call('SET', KEYS[1], ARGV[2])
            redis.call('DEL', KEYS[2])
            return 1
        end
        return 0
        """;
    private readonly IDatabase database = redis.GetDatabase();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly PendingRevisionReconciliationCoordinator reconciliations =
        new(clock ?? TimeProvider.System);

    public async Task<long?> GetOrLoad(Guid worldId,
        Func<CancellationToken, Task<long?>> durableLoader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = await database.HashGetAllAsync(PendingKey(worldId));
        if (TryPending(pending, out var target, out var since))
            return await reconciliations.Run(
                worldId,
                () => ReconcilePending(
                    worldId, target, since, durableLoader),
                cancellationToken);
        var current = await database.StringGetAsync(CommittedKey(worldId));
        if (TryRevision(current, out var revision)) return revision;
        var loaded = await durableLoader(cancellationToken);
        if (!loaded.HasValue) return null;
        var seeded = await database.ScriptEvaluateAsync(
            SeedScript,
            new RedisKey[] { CommittedKey(worldId) },
            new RedisValue[] { loaded.Value });
        return TryRevision(seeded, out revision) ? revision : null;
    }

    private async Task<long?> ReconcilePending(
        Guid worldId,
        long target,
        long since,
        Func<CancellationToken, Task<long?>> durableLoader)
    {
        var durable = await durableLoader(CancellationToken.None);
        if (!durable.HasValue) return null;
        if (durable.Value >= target)
        {
            await ObserveCommitted(
                worldId, durable.Value, CancellationToken.None);
            return durable.Value;
        }
        if (time.GetUtcNow().ToUnixTimeMilliseconds() - since <
            AbandonedPendingGrace.TotalMilliseconds)
            return null;
        await database.ScriptEvaluateAsync(
            ReconcileRollbackScript,
            new RedisKey[] { CommittedKey(worldId), PendingKey(worldId) },
            new RedisValue[] { target, durable.Value });
        reconciliations.Invalidate(worldId);
        return durable.Value;
    }

    public async Task MarkPending(Guid worldId, long currentRevision,
        long nextRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (worldId == Guid.Empty || currentRevision < 0 ||
            nextRevision <= currentRevision)
            throw new ArgumentOutOfRangeException(nameof(nextRevision));
        await database.ScriptEvaluateAsync(
            MarkPendingScript,
            new RedisKey[] { CommittedKey(worldId), PendingKey(worldId) },
            new RedisValue[] { currentRevision, nextRevision,
                time.GetUtcNow().ToUnixTimeMilliseconds() });
        reconciliations.Invalidate(worldId);
    }

    public async Task ObserveCommitted(Guid worldId, long revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (worldId == Guid.Empty || revision < 0) return;
        await database.ScriptEvaluateAsync(
            ObserveScript,
            new RedisKey[] { CommittedKey(worldId), PendingKey(worldId) },
            new RedisValue[] { revision });
        reconciliations.Invalidate(worldId);
    }

    private static RedisKey CommittedKey(Guid worldId) =>
        $"tormia:world:{worldId:N}:revision";
    internal static RedisKey PendingKey(Guid worldId) =>
        $"tormia:world:{worldId:N}:revision:pending";
    private static bool TryRevision(RedisValue value, out long revision)
    {
        revision = 0;
        return value.HasValue && long.TryParse(value.ToString(), out revision);
    }
    private static bool TryRevision(RedisResult value, out long revision) =>
        long.TryParse(value.ToString(), out revision);
    private static bool TryPending(HashEntry[] entries, out long target, out long since)
    {
        target = 0; since = 0;
        foreach (var entry in entries)
        {
            if (entry.Name == "target") long.TryParse(entry.Value, out target);
            else if (entry.Name == "since") long.TryParse(entry.Value, out since);
        }
        return target > 0 && since > 0;
    }
}

/// <summary>
/// Bounds pending-revision recovery independently of incoming intent volume.
/// A missing/failed result is cached briefly as fail-closed backoff; it never
/// supplies an older revision or bypasses the pending fence.
/// </summary>
internal sealed class PendingRevisionReconciliationCoordinator
{
    private readonly ConcurrentDictionary<Guid, Operation> operations = new();
    private readonly ConcurrentDictionary<Guid, CachedResult> recent = new();
    private readonly ConcurrentDictionary<Guid, long> epochs = new();
    private readonly ConcurrentQueue<(Guid WorldId, CachedResult Result)>
        recentOrder = new();
    private readonly SemaphoreSlim operationSlots;
    private readonly TimeProvider time;
    private readonly int maximumWaitersPerWorld;
    private readonly TimeSpan resultLifetime;
    private readonly TimeSpan failureBackoff;
    private readonly int maximumRecentResults;

    internal PendingRevisionReconciliationCoordinator(
        TimeProvider time,
        int maximumConcurrentWorlds = 64,
        int maximumWaitersPerWorld = 128,
        int maximumRecentResults = 4096,
        TimeSpan? resultLifetime = null,
        TimeSpan? failureBackoff = null)
    {
        if (maximumConcurrentWorlds <= 0 || maximumWaitersPerWorld <= 0 ||
            maximumRecentResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentWorlds));
        this.time = time;
        this.maximumWaitersPerWorld = maximumWaitersPerWorld;
        this.maximumRecentResults = maximumRecentResults;
        this.resultLifetime = resultLifetime ?? TimeSpan.FromMilliseconds(100);
        this.failureBackoff = failureBackoff ?? TimeSpan.FromMilliseconds(250);
        operationSlots = new(maximumConcurrentWorlds, maximumConcurrentWorlds);
    }

    internal async Task<long?> Run(
        Guid worldId,
        Func<Task<long?>> reconcile,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var epoch = epochs.GetOrAdd(worldId, 0);
        if (recent.TryGetValue(worldId, out var cached))
        {
            if (cached.Epoch == epoch && cached.ValidUntil > now)
                return cached.Revision;
            recent.TryRemove(new KeyValuePair<Guid, CachedResult>(worldId, cached));
        }

        Operation operation;
        if (!operations.TryGetValue(worldId, out operation!))
        {
            if (!operationSlots.Wait(0)) return null;
            var created = new Operation(epoch, new Lazy<Task<long?>>(
                () => Execute(worldId, epoch, reconcile),
                LazyThreadSafetyMode.ExecutionAndPublication));
            operation = operations.GetOrAdd(worldId, created);
            if (!ReferenceEquals(operation, created))
                operationSlots.Release();
        }
        if (operation.Epoch != epoch) return null;
        if (!operation.TryAddWaiter(maximumWaitersPerWorld)) return null;
        var task = operation.Task.Value;
        try { return await task.WaitAsync(cancellationToken); }
        finally
        {
            operation.RemoveWaiter();
            if (task.IsCompleted) Remove(worldId, operation);
            else
                _ = task.ContinueWith(
                    (_, state) =>
                    {
                        var value = ((PendingRevisionReconciliationCoordinator Owner,
                            Guid WorldId, Operation Operation))state!;
                        value.Owner.Remove(value.WorldId, value.Operation);
                    },
                    (this, worldId, operation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
    }

    internal void Invalidate(Guid worldId)
    {
        epochs.AddOrUpdate(worldId, 1, static (_, current) => checked(current + 1));
        recent.TryRemove(worldId, out _);
    }

    internal int RecentResultCount => recent.Count;

    private async Task<long?> Execute(
        Guid worldId, long epoch, Func<Task<long?>> reconcile)
    {
        long? result;
        try { result = await reconcile(); }
        catch { result = null; }
        if (epochs.GetOrAdd(worldId, 0) == epoch)
        {
            var cached = new CachedResult(
                result,
                time.GetUtcNow() +
                (result.HasValue ? resultLifetime : failureBackoff),
                epoch);
            recent[worldId] = cached;
            recentOrder.Enqueue((worldId, cached));
            while (recent.Count > maximumRecentResults &&
                   recentOrder.TryDequeue(out var expired))
                recent.TryRemove(
                    new KeyValuePair<Guid, CachedResult>(
                        expired.WorldId, expired.Result));
        }
        return result;
    }

    private void Remove(Guid worldId, Operation operation)
    {
        if (operations.TryRemove(
                new KeyValuePair<Guid, Operation>(worldId, operation)))
            operationSlots.Release();
    }

    private sealed class Operation(long epoch, Lazy<Task<long?>> task)
    {
        private int waiters;
        internal Lazy<Task<long?>> Task { get; } = task;
        internal long Epoch { get; } = epoch;
        internal bool TryAddWaiter(int maximum)
        {
            while (true)
            {
                var current = Volatile.Read(ref waiters);
                if (current >= maximum) return false;
                if (Interlocked.CompareExchange(
                        ref waiters, current + 1, current) == current)
                    return true;
            }
        }
        internal void RemoveWaiter() => Interlocked.Decrement(ref waiters);
    }

    private sealed record CachedResult(
        long? Revision, DateTimeOffset ValidUntil, long Epoch);
}

internal sealed class InMemoryWorldRevisionRuntimeRegistry :
    IWorldRevisionRuntimeRegistry,
    IWorldRevisionAtomicBoundary
{
    private static readonly TimeSpan AbandonedPendingGrace = TimeSpan.FromSeconds(30);
    private readonly object gate = new();
    private readonly Dictionary<Guid, RevisionState> states = new();
    private readonly ConcurrentDictionary<Guid, Lazy<Task<long?>>> loads = new();
    private readonly TimeProvider time;

    internal InMemoryWorldRevisionRuntimeRegistry(TimeProvider? time = null) =>
        this.time = time ?? TimeProvider.System;

    public async Task<long?> GetOrLoad(Guid worldId,
        Func<CancellationToken, Task<long?>> durableLoader,
        CancellationToken cancellationToken)
    {
        RevisionState? state;
        lock (gate)
        {
            states.TryGetValue(worldId, out state);
            if (state is { PendingRevision: null }) return state.CommittedRevision;
        }
        var holder = loads.GetOrAdd(worldId,
            _ => new Lazy<Task<long?>>(
                () => durableLoader(CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var durable = await holder.Value.WaitAsync(cancellationToken);
            if (!durable.HasValue) return null;
            lock (gate)
            {
                states.TryGetValue(worldId, out state);
                if (state?.PendingRevision is long pending)
                {
                    if (durable.Value >= pending)
                    {
                        states[worldId] = new(durable.Value, null, 0);
                        return durable.Value;
                    }
                    if (time.GetUtcNow().ToUnixTimeMilliseconds() -
                        state.PendingSinceUnixMilliseconds <
                        AbandonedPendingGrace.TotalMilliseconds)
                        return null;
                }
                states[worldId] = new(durable.Value, null, 0);
                return durable.Value;
            }
        }
        finally { loads.TryRemove(new KeyValuePair<Guid, Lazy<Task<long?>>>(worldId, holder)); }
    }

    public Task MarkPending(Guid worldId, long currentRevision,
        long nextRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (worldId == Guid.Empty || currentRevision < 0 ||
            nextRevision <= currentRevision)
            throw new ArgumentOutOfRangeException(nameof(nextRevision));
        lock (gate)
            states[worldId] = new(currentRevision, nextRevision,
                time.GetUtcNow().ToUnixTimeMilliseconds());
        return Task.CompletedTask;
    }

    public Task ObserveCommitted(Guid worldId, long revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            states.TryGetValue(worldId, out var current);
            states[worldId] = new(
                Math.Max(current?.CommittedRevision ?? revision, revision),
                null, 0);
        }
        return Task.CompletedTask;
    }

    public bool TryExecuteIfCurrent(Guid worldId, long revision,
        Func<bool> operation)
    {
        lock (gate)
            return states.TryGetValue(worldId, out var state) &&
                   state.PendingRevision is null &&
                   state.CommittedRevision == revision && operation();
    }

    public bool TryExecuteAtCurrent(Guid worldId, Func<long, bool> operation)
    {
        lock (gate)
            return states.TryGetValue(worldId, out var state) &&
                   state.PendingRevision is null &&
                   operation(state.CommittedRevision);
    }

    private sealed record RevisionState(long CommittedRevision,
        long? PendingRevision, long PendingSinceUnixMilliseconds);
}
