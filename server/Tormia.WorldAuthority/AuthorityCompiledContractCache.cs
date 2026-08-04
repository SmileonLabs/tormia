using System.Diagnostics.Metrics;
using System.Diagnostics;

internal readonly record struct AuthorityCompiledContractCacheKey(
    Guid WorldId,
    long WorldRevision,
    string ContentManifestDigest);

internal sealed class AuthorityCompiledContractRevisionChangedException(
    AuthorityCompiledContractCacheKey expected,
    AuthorityCompiledContractCacheKey actual)
    : InvalidOperationException(
        $"Authority compiled-contract key changed from revision " +
        $"{expected.WorldRevision} to {actual.WorldRevision} during build.")
{
    internal AuthorityCompiledContractCacheKey Expected { get; } = expected;
    internal AuthorityCompiledContractCacheKey Actual { get; } = actual;
}

internal sealed class AuthorityCompiledContractBackpressureException()
    : InvalidOperationException(
        "Authority compiled-contract build backlog is full; retry later.");

internal sealed class AuthorityCompiledContractBuildTimeoutException(
    TimeSpan timeout)
    : TimeoutException(
        $"Authority compiled-contract build exceeded {timeout.TotalSeconds:F1} seconds.");

internal sealed class AuthorityCompiledContractHostStoppingException(
    CancellationToken stoppingToken)
    : OperationCanceledException(
        "Authority compiled-contract build stopped because the host is shutting down.",
        stoppingToken);

/// <summary>
/// Process-local, exact-key, bounded single-flight cache. It stores immutable
/// compiled base contracts only. There is deliberately no older-revision or
/// package-compatible fallback.
/// </summary>
internal sealed class AuthorityCompiledContractCache : IDisposable
{
    private sealed class Entry
    {
        internal required Lazy<Task<AuthorityCompiledEvaluationContract>> Build;
        internal long AccessSequence;
        internal long EstimatedBytes;
    }

    private readonly object gate = new();
    private readonly Dictionary<AuthorityCompiledContractCacheKey, Entry> entries = new();
    private readonly Dictionary<AuthorityCompiledContractCacheKey,
        Lazy<Task<AuthorityCompiledEvaluationContract>>> inFlightByKey = new();
    private readonly int capacity;
    private readonly long maxEstimatedBytes;
    private readonly SemaphoreSlim buildConcurrency;
    private readonly int maxPendingBuildKeys;
    private readonly CancellationToken hostStoppingToken;
    private readonly TimeSpan buildTimeout;
    private long estimatedBytes;
    private long sequence;
    private readonly AuthorityCompiledContractCacheMetrics metrics = new();

    internal AuthorityCompiledContractCache(
        int capacity = 32,
        long maxEstimatedBytes = 256L * 1024 * 1024,
        int maxConcurrentBuilds = 2,
        int maxPendingBuildKeys = 8,
        TimeSpan? buildTimeout = null,
        CancellationToken hostStoppingToken = default)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxEstimatedBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEstimatedBytes));
        if (maxConcurrentBuilds < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentBuilds));
        if (maxPendingBuildKeys < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPendingBuildKeys));
        this.capacity = capacity;
        this.maxEstimatedBytes = maxEstimatedBytes;
        buildConcurrency = new SemaphoreSlim(
            maxConcurrentBuilds, maxConcurrentBuilds);
        this.maxPendingBuildKeys = maxPendingBuildKeys;
        this.buildTimeout = buildTimeout ?? TimeSpan.FromSeconds(30);
        if (this.buildTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(buildTimeout));
        this.hostStoppingToken = hostStoppingToken;
    }

    internal int Count
    {
        get { lock (gate) return entries.Count; }
    }

    internal int PendingBuildCount
    {
        get { lock (gate) return inFlightByKey.Count; }
    }

    internal long ResidentEstimatedBytes
    {
        get { lock (gate) return estimatedBytes; }
    }

    internal async Task<AuthorityCompiledEvaluationContract> GetOrBuild(
        AuthorityCompiledContractCacheKey key,
        Func<Task<AuthorityCompiledEvaluationContract>> factory,
        CancellationToken cancellationToken)
        => await GetOrBuild(
            key, _ => factory(), cancellationToken).ConfigureAwait(false);

    internal async Task<AuthorityCompiledEvaluationContract> GetOrBuild(
        AuthorityCompiledContractCacheKey key,
        Func<CancellationToken, Task<AuthorityCompiledEvaluationContract>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Entry entry;
        Lazy<Task<AuthorityCompiledEvaluationContract>>? overflowBuild = null;
        lock (gate)
        {
            if (entries.TryGetValue(key, out entry!))
            {
                entry.AccessSequence = ++sequence;
                metrics.RecordHit();
            }
            else
            {
                if (entries.Keys.Any(existing => existing.WorldId == key.WorldId))
                    metrics.RecordStaleKeyMiss();
                metrics.RecordMiss();
                // Never evict an in-flight build: doing so permits a duplicate
                // build for the same exact key. If all bounded slots are busy,
                // perform this request uncached instead.
                if (entries.Count >= capacity &&
                    entries.Values.All(value =>
                        !value.Build.IsValueCreated ||
                        !value.Build.Value.IsCompleted))
                {
                    if (!inFlightByKey.TryGetValue(key, out overflowBuild))
                    {
                        if (inFlightByKey.Count >= maxPendingBuildKeys)
                        {
                            metrics.RecordBackpressureRejection();
                            throw new AuthorityCompiledContractBackpressureException();
                        }
                        Lazy<Task<AuthorityCompiledEvaluationContract>>? created = null;
                        created = new Lazy<Task<AuthorityCompiledEvaluationContract>>(
                            () => BuildOverflowAndRemove(
                                key, created!, factory),
                            LazyThreadSafetyMode.ExecutionAndPublication);
                        overflowBuild = created;
                        inFlightByKey.Add(key, overflowBuild);
                        metrics.RecordBypass();
                        metrics.RecordPendingAdded();
                    }
                    else
                    {
                        metrics.RecordHit();
                    }
                    entry = null!;
                }
                else
                {
                Entry? created = null;
                var build = new Lazy<Task<AuthorityCompiledEvaluationContract>>(
                    () => BuildAndAccount(key, created!, factory),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                created = new Entry
                {
                    AccessSequence = ++sequence,
                    Build = build
                };
                entry = created;
                entries.Add(key, entry);
                metrics.RecordResidentAdded();
                EvictOverflow(key);
                }
            }
        }

        if (overflowBuild is not null)
        {
            // Overflow exact keys retain single-flight identity. The bounded
            // pending-key map prevents an unbounded distinct-key backlog.
            var build = overflowBuild.Value;
            _ = build.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return await build.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // A waiter observes cancellation independently. Build completion and
        // accounting belong to BuildAndAccount, not to any particular waiter.
        return await entry.Build.Value.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AuthorityCompiledEvaluationContract> BuildAndAccount(
        AuthorityCompiledContractCacheKey key,
        Entry entry,
        Func<CancellationToken, Task<AuthorityCompiledEvaluationContract>> factory)
    {
        using var owner = CreateBuildOwnerCancellation();
        var acquired = false;
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await buildConcurrency.WaitAsync(owner.Token).ConfigureAwait(false);
            acquired = true;
            metrics.RecordBuild();
            var result = await factory(owner.Token).ConfigureAwait(false);
            MarkCompleted(key, entry, result.EstimatedBytes);
            return result;
        }
        catch (OperationCanceledException) when (hostStoppingToken.IsCancellationRequested)
        {
            metrics.RecordBuildFailure();
            RemoveResident(key, entry);
            throw new AuthorityCompiledContractHostStoppingException(hostStoppingToken);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            metrics.RecordBuildFailure();
            RemoveResident(key, entry);
            throw new AuthorityCompiledContractBuildTimeoutException(buildTimeout);
        }
        catch
        {
            metrics.RecordBuildFailure();
            RemoveResident(key, entry);
            throw;
        }
        finally
        {
            metrics.RecordBuildDuration(
                Stopwatch.GetElapsedTime(startedAt));
            if (acquired) buildConcurrency.Release();
        }
    }

    private async Task<AuthorityCompiledEvaluationContract>
        BuildOverflowAndRemove(
            AuthorityCompiledContractCacheKey key,
            Lazy<Task<AuthorityCompiledEvaluationContract>> owner,
            Func<CancellationToken, Task<AuthorityCompiledEvaluationContract>> factory)
    {
        using var buildOwner = CreateBuildOwnerCancellation();
        var acquired = false;
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await buildConcurrency.WaitAsync(buildOwner.Token).ConfigureAwait(false);
            acquired = true;
            metrics.RecordBuild();
            return await factory(buildOwner.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hostStoppingToken.IsCancellationRequested)
        {
            metrics.RecordBuildFailure();
            throw new AuthorityCompiledContractHostStoppingException(hostStoppingToken);
        }
        catch (OperationCanceledException) when (buildOwner.IsCancellationRequested)
        {
            metrics.RecordBuildFailure();
            throw new AuthorityCompiledContractBuildTimeoutException(buildTimeout);
        }
        catch
        {
            metrics.RecordBuildFailure();
            throw;
        }
        finally
        {
            metrics.RecordBuildDuration(
                Stopwatch.GetElapsedTime(startedAt));
            if (acquired) buildConcurrency.Release();
            lock (gate)
            {
                if (inFlightByKey.TryGetValue(key, out var current) &&
                    ReferenceEquals(current, owner))
                {
                    inFlightByKey.Remove(key);
                    metrics.RecordPendingRemoved();
                }
            }
        }
    }

    private CancellationTokenSource CreateBuildOwnerCancellation()
    {
        var owner = CancellationTokenSource.CreateLinkedTokenSource(
            hostStoppingToken);
        owner.CancelAfter(buildTimeout);
        return owner;
    }

    private void RemoveResident(
        AuthorityCompiledContractCacheKey key,
        Entry entry)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(key, out var current) ||
                !ReferenceEquals(current, entry)) return;
            entries.Remove(key);
            if (entry.EstimatedBytes > 0)
                estimatedBytes -= entry.EstimatedBytes;
            metrics.RecordResidentRemoved(entry.EstimatedBytes);
        }
    }

    private void EvictOverflow(AuthorityCompiledContractCacheKey protectedKey)
    {
        while (entries.Count > capacity)
        {
            var candidate = entries
                .Where(pair => pair.Key != protectedKey)
                .Where(pair => pair.Value.Build.IsValueCreated &&
                               pair.Value.Build.Value.IsCompletedSuccessfully)
                .OrderBy(pair => pair.Value.AccessSequence)
                .FirstOrDefault();
            if (candidate.Value is null) break;
            entries.Remove(candidate.Key);
            estimatedBytes -= candidate.Value.EstimatedBytes;
            metrics.RecordResidentRemoved(candidate.Value.EstimatedBytes);
            metrics.RecordEviction();
        }
    }

    private void MarkCompleted(
        AuthorityCompiledContractCacheKey key,
        Entry entry,
        long entryEstimatedBytes)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(key, out var current) ||
                !ReferenceEquals(current, entry) || entry.EstimatedBytes != 0)
                return;
            entry.EstimatedBytes = Math.Max(1, entryEstimatedBytes);
            if (entry.EstimatedBytes > maxEstimatedBytes)
            {
                // Return the successfully compiled value to current waiters,
                // but never retain an entry that cannot fit by itself. A later
                // request rebuilds it instead of silently exceeding the bound.
                entries.Remove(key);
                metrics.RecordResidentRemoved(0);
                metrics.RecordOversizedBypass();
                return;
            }
            estimatedBytes = SaturatingAdd(
                estimatedBytes, entry.EstimatedBytes);
            metrics.RecordEstimatedBytesAdded(entry.EstimatedBytes);
            while (estimatedBytes > maxEstimatedBytes)
            {
                var candidate = entries
                    .Where(pair => pair.Value.Build.IsValueCreated &&
                                   pair.Value.Build.Value.IsCompletedSuccessfully)
                    .OrderBy(pair => pair.Value.AccessSequence)
                    .FirstOrDefault();
                if (candidate.Value is null) break;
                entries.Remove(candidate.Key);
                estimatedBytes -= candidate.Value.EstimatedBytes;
                metrics.RecordResidentRemoved(candidate.Value.EstimatedBytes);
                metrics.RecordEviction();
            }
            EvictOverflow(key);
        }
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    public void Dispose()
    {
        // Semaphore disposal is intentionally deferred to GC. A singleton can
        // be disposed while owner builds are unwinding after ApplicationStopping;
        // disposing it here would make their balanced Release throw.
        metrics.Dispose();
    }
}

internal sealed class AuthorityCompiledContractCacheMetrics : IDisposable
{
    internal const string MeterName = "Tormia.WorldAuthority.CompiledContractCache";
    internal const string HitsName = "tormia.authority.contract_cache.hits";
    internal const string MissesName = "tormia.authority.contract_cache.misses";
    internal const string BuildsName = "tormia.authority.contract_cache.builds";
    internal const string EvictionsName = "tormia.authority.contract_cache.evictions";
    internal const string StaleKeyMissesName = "tormia.authority.contract_cache.stale_key_misses";
    internal const string BypassesName = "tormia.authority.contract_cache.bypasses";
    internal const string BuildFailuresName = "tormia.authority.contract_cache.build_failures";
    internal const string BuildDurationName = "tormia.authority.contract_cache.build_duration";
    internal const string ResidentEntriesName = "tormia.authority.contract_cache.resident_entries";
    internal const string EstimatedBytesName = "tormia.authority.contract_cache.estimated_bytes";
    internal const string OverflowPendingBuildKeysName = "tormia.authority.contract_cache.overflow_pending_build_keys";
    internal const string BackpressureRejectionsName = "tormia.authority.contract_cache.backpressure_rejections";
    internal const string OversizedBypassesName = "tormia.authority.contract_cache.oversized_bypasses";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> hits;
    private readonly Counter<long> misses;
    private readonly Counter<long> builds;
    private readonly Counter<long> evictions;
    private readonly Counter<long> staleKeyMisses;
    private readonly Counter<long> bypasses;
    private readonly Counter<long> buildFailures;
    private readonly Histogram<double> buildDuration;
    private readonly UpDownCounter<long> residentEntries;
    private readonly UpDownCounter<long> estimatedBytes;
    private readonly UpDownCounter<long> overflowPendingBuildKeys;
    private readonly Counter<long> backpressureRejections;
    private readonly Counter<long> oversizedBypasses;

    internal AuthorityCompiledContractCacheMetrics()
    {
        hits = meter.CreateCounter<long>(HitsName);
        misses = meter.CreateCounter<long>(MissesName);
        builds = meter.CreateCounter<long>(BuildsName);
        evictions = meter.CreateCounter<long>(EvictionsName);
        staleKeyMisses = meter.CreateCounter<long>(StaleKeyMissesName);
        bypasses = meter.CreateCounter<long>(BypassesName);
        buildFailures = meter.CreateCounter<long>(BuildFailuresName);
        buildDuration = meter.CreateHistogram<double>(BuildDurationName, "ms");
        residentEntries = meter.CreateUpDownCounter<long>(ResidentEntriesName, "entries");
        estimatedBytes = meter.CreateUpDownCounter<long>(EstimatedBytesName, "bytes");
        overflowPendingBuildKeys = meter.CreateUpDownCounter<long>(
            OverflowPendingBuildKeysName, "keys");
        backpressureRejections = meter.CreateCounter<long>(BackpressureRejectionsName);
        oversizedBypasses = meter.CreateCounter<long>(OversizedBypassesName);
    }

    internal void RecordHit() => hits.Add(1);
    internal void RecordMiss() => misses.Add(1);
    internal void RecordBuild() => builds.Add(1);
    internal void RecordEviction() => evictions.Add(1);
    internal void RecordStaleKeyMiss() => staleKeyMisses.Add(1);
    internal void RecordBypass() => bypasses.Add(1);
    internal void RecordBuildFailure() => buildFailures.Add(1);
    internal void RecordBuildDuration(TimeSpan duration) =>
        buildDuration.Record(duration.TotalMilliseconds);
    internal void RecordResidentAdded() => residentEntries.Add(1);
    internal void RecordEstimatedBytesAdded(long bytes) => estimatedBytes.Add(bytes);
    internal void RecordResidentRemoved(long bytes)
    {
        residentEntries.Add(-1);
        if (bytes > 0) estimatedBytes.Add(-bytes);
    }
    internal void RecordPendingAdded() => overflowPendingBuildKeys.Add(1);
    internal void RecordPendingRemoved() => overflowPendingBuildKeys.Add(-1);
    internal void RecordBackpressureRejection() =>
        backpressureRejections.Add(1);
    internal void RecordOversizedBypass() => oversizedBypasses.Add(1);
    public void Dispose() => meter.Dispose();
}
