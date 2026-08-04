using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using StackExchange.Redis;
using Tormia.Ontology.Realtime.Protocol;

/// <summary>
/// Fans an already-authoritative fixed-tick frame to every Authority instance
/// that may own UDP peers. It transports no input and performs no gameplay
/// evaluation. The bounded receiver drops newest replaceable frames.
/// </summary>
internal interface IAuthorityUdpMotionFrameBackplane
{
    Task Start(CancellationToken cancellationToken);
    ValueTask<bool> Publish(
        WorldZoneMotionFrame frame,
        CancellationToken cancellationToken);
    IAsyncEnumerable<WorldZoneMotionFrame> ReadAll(
        CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
}

internal static class AuthorityUdpMotionFrameIngressPolicy
{
    internal static bool TryValidate(
        WorldZoneMotionFrame? frame,
        int maximumItems,
        out int estimatedBytes)
    {
        estimatedBytes = 0;
        if (frame is null ||
            frame.FrameOccurrenceId == Guid.Empty ||
            frame.WorldId == Guid.Empty ||
            !SemanticId.IsValid(frame.ZoneKey) ||
            frame.ServerTick <= 0 ||
            frame.ObservedAtUnixMilliseconds <= 0 ||
            frame.Items is null ||
            frame.Items.Count == 0 ||
            frame.Items.Count > maximumItems)
            return false;

        var actors = new HashSet<Guid>();
        foreach (var item in frame.Items)
        {
            if (item is null ||
                item.WorldId != frame.WorldId ||
                item.AvatarEntityId == Guid.Empty ||
                item.RuntimeSessionId == Guid.Empty ||
                !string.Equals(item.ZoneKey, frame.ZoneKey,
                    StringComparison.Ordinal) ||
                !SemanticId.IsValid(item.ZoneKey) ||
                item.LastProcessedIntentSequence < 0 ||
                item.ServerTick <= 0 ||
                item.UpdatedAtUnixMilliseconds <= 0 ||
                string.IsNullOrWhiteSpace(item.MotionStatus) ||
                Encoding.UTF8.GetByteCount(item.MotionStatus) >
                    RealtimeWireContract.MaximumMotionStatusUtf8Bytes ||
                !IsFiniteFloat(item.PositionX) ||
                !IsFiniteFloat(item.PositionY) ||
                !IsFiniteFloat(item.PositionZ) ||
                !IsFiniteFloat(item.VelocityX) ||
                !IsFiniteFloat(item.VelocityY) ||
                !IsFiniteFloat(item.VelocityZ) ||
                !IsFiniteFloat(item.GroundReferenceY) ||
                !actors.Add(item.AvatarEntityId))
                return false;
        }

        try
        {
            // Deliberately conservative upper bound for a JSON backplane
            // representation. Exact received envelope bytes may replace it.
            estimatedBytes = checked(512 + frame.Items.Count * 768);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsFiniteFloat(double value) =>
        double.IsFinite(value) &&
        value >= -float.MaxValue &&
        value <= float.MaxValue;
}

/// <summary>
/// Byte- and count-bounded occurrence FIFO. A frame may contain only the
/// actors changed in that Authority tick, so a later occurrence must never
/// overwrite an earlier queued occurrence from the same Zone. When full, the
/// newest occurrence is dropped without disturbing accepted deltas.
/// </summary>
internal sealed class LatestWorldZoneMotionFrameBuffer
{
    private readonly object gate = new();
    private readonly int maximumFrames;
    private readonly int maximumBytes;
    private readonly int maximumItems;
    private readonly int maximumRecentOccurrences;
    private readonly HashSet<Guid> queuedOccurrences = new();
    private readonly Dictionary<Guid, long> recentOccurrences = new();
    private readonly Channel<Entry> ready;
    private int retainedFrames;
    private int retainedBytes;
    private long occurrenceTouch;

    internal LatestWorldZoneMotionFrameBuffer(
        int maximumFrames,
        int maximumBytes,
        int maximumItems)
    {
        if (maximumFrames is <= 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (maximumItems <= 0) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        this.maximumFrames = maximumFrames;
        this.maximumBytes = maximumBytes;
        this.maximumItems = maximumItems;
        // Packet/header ticks are diagnostics, not a canonical Zone ordering
        // key. Keep only a bounded replay fence for exact frame occurrences;
        // actor runtime-session plus actor tick owns state ordering downstream.
        maximumRecentOccurrences = checked(maximumFrames * 4);
        ready = Channel.CreateBounded<Entry>(new BoundedChannelOptions(maximumFrames)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    internal bool TryWrite(WorldZoneMotionFrame frame, int exactBytes = 0)
    {
        if (!AuthorityUdpMotionFrameIngressPolicy.TryValidate(
                frame,
                maximumItems,
                out var estimatedBytes))
            return false;
        var bytes = exactBytes > 0 ? exactBytes : estimatedBytes;
        if (bytes <= 0 || bytes > maximumBytes) return false;
        lock (gate)
        {
            if (recentOccurrences.ContainsKey(frame.FrameOccurrenceId))
                return false;
            if (retainedFrames >= maximumFrames ||
                retainedBytes > maximumBytes - bytes)
                return false;
            var entry = new Entry(frame, bytes);
            if (ready.Writer.TryWrite(entry))
            {
                retainedFrames++;
                retainedBytes += bytes;
                queuedOccurrences.Add(frame.FrameOccurrenceId);
                RecordOccurrence(frame.FrameOccurrenceId);
                return true;
            }
            return false;
        }
    }

    internal async IAsyncEnumerable<WorldZoneMotionFrame> ReadAll(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in ready.Reader.ReadAllAsync(cancellationToken))
        {
            lock (gate)
            {
                retainedFrames--;
                retainedBytes -= entry.Bytes;
                queuedOccurrences.Remove(entry.Frame.FrameOccurrenceId);
            }
            yield return entry.Frame;
        }
    }

    internal int RetainedBytes
    {
        get { lock (gate) return retainedBytes; }
    }

    internal int RecentOccurrenceCount
    {
        get { lock (gate) return recentOccurrences.Count; }
    }

    internal void Complete() => ready.Writer.TryComplete();

    private void RecordOccurrence(Guid occurrenceId)
    {
        recentOccurrences[occurrenceId] = ++occurrenceTouch;
        while (recentOccurrences.Count > maximumRecentOccurrences)
        {
            var eviction = recentOccurrences
                .Where(pair => !queuedOccurrences.Contains(pair.Key))
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => pair.Key)
                .FirstOrDefault(Guid.Empty);
            if (eviction == Guid.Empty) break;
            recentOccurrences.Remove(eviction);
        }
    }

    private sealed record Entry(WorldZoneMotionFrame Frame, int Bytes);
}

internal sealed class InMemoryAuthorityUdpMotionFrameBackplane :
    IAuthorityUdpMotionFrameBackplane
{
    private readonly LatestWorldZoneMotionFrameBuffer frames;

    internal InMemoryAuthorityUdpMotionFrameBackplane(
        AuthorityUdpListenerOptions options)
    {
        frames = new(
            options.OutboundSnapshotCapacity,
            options.OutboundSnapshotByteCapacity,
            options.MaximumSnapshotFrameItems);
    }

    public Task Start(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<bool> Publish(
        WorldZoneMotionFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(frames.TryWrite(frame));
    }

    public IAsyncEnumerable<WorldZoneMotionFrame> ReadAll(
        CancellationToken cancellationToken) =>
        frames.ReadAll(cancellationToken);

    public Task Stop(CancellationToken cancellationToken)
    {
        frames.Complete();
        return Task.CompletedTask;
    }
}

internal sealed class RedisAuthorityUdpMotionFrameBackplane :
    IAuthorityUdpMotionFrameBackplane
{
    private const string ChannelName =
        "tormia:authority-udp-motion-snapshot:v2";
    private static readonly RedisChannel RedisChannelName =
        RedisChannel.Literal(ChannelName);
    private readonly IConnectionMultiplexer redis;
    private readonly AuthorityUdpListenerDiagnostics diagnostics;
    private readonly LatestWorldZoneMotionFrameBuffer frames;
    private readonly AuthorityUdpListenerOptions options;
    private readonly Action<RedisChannel, RedisValue> receiver;
    private int started;

    internal RedisAuthorityUdpMotionFrameBackplane(
        IConnectionMultiplexer redis,
        AuthorityUdpListenerDiagnostics diagnostics,
        AuthorityUdpListenerOptions options)
    {
        this.redis = redis;
        this.diagnostics = diagnostics;
        this.options = options;
        frames = new(
            options.OutboundSnapshotCapacity,
            options.OutboundSnapshotByteCapacity,
            options.MaximumSnapshotFrameItems);
        receiver = Receive;
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await redis.GetSubscriber().SubscribeAsync(
                RedisChannelName,
                receiver);
        }
        catch
        {
            Volatile.Write(ref started, 0);
            throw;
        }
    }

    public async ValueTask<bool> Publish(
        WorldZoneMotionFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AuthorityUdpMotionFrameIngressPolicy.TryValidate(
                frame,
                options.MaximumSnapshotFrameItems,
                out _))
            return false;
        byte[] envelope;
        try
        {
            envelope = JsonSerializer.SerializeToUtf8Bytes(frame);
        }
        catch
        {
            return false;
        }
        if (envelope.Length == 0 ||
            envelope.Length > options.MaximumSnapshotBackplaneEnvelopeBytes)
            return false;
        var receivers = await redis.GetSubscriber().PublishAsync(
            RedisChannelName,
            envelope,
            CommandFlags.FireAndForget);
        // FireAndForget does not report subscriber count. Reaching Redis is
        // sufficient; local and remote subscribers independently fail closed.
        return receivers >= 0;
    }

    public IAsyncEnumerable<WorldZoneMotionFrame> ReadAll(
        CancellationToken cancellationToken) =>
        frames.ReadAll(cancellationToken);

    public async Task Stop(CancellationToken cancellationToken)
    {
        frames.Complete();
        if (Interlocked.Exchange(ref started, 0) == 0) return;
        await redis.GetSubscriber().UnsubscribeAsync(
            RedisChannelName,
            receiver);
    }

    private void Receive(RedisChannel channel, RedisValue value)
    {
        try
        {
            var bytes = (byte[]?)value;
            if (bytes is null || bytes.Length <= 0 ||
                bytes.Length > options.MaximumSnapshotBackplaneEnvelopeBytes)
            {
                diagnostics.RecordMotionSnapshotDropped("backplane_invalid");
                return;
            }
            var frame = JsonSerializer.Deserialize<WorldZoneMotionFrame>(bytes);
            if (!AuthorityUdpMotionFrameIngressPolicy.TryValidate(
                    frame,
                    options.MaximumSnapshotFrameItems,
                    out _))
            {
                diagnostics.RecordMotionSnapshotDropped("backplane_invalid");
                return;
            }
            if (!frames.TryWrite(frame!, bytes.Length))
                diagnostics.RecordMotionSnapshotDropped("backplane_queue_full");
        }
        catch
        {
            diagnostics.RecordMotionSnapshotDropped("backplane_invalid");
        }
    }
}
