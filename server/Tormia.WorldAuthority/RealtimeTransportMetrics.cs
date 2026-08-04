using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Low-cardinality operational evidence for the ephemeral realtime transport.
/// These measurements observe Authority work only; they do not change motion,
/// author Facts, or participate in rule evaluation.
/// </summary>
internal sealed class RealtimeTransportMetrics : IDisposable
{
    internal const string MeterName =
        "Tormia.WorldAuthority.RealtimeTransport";
    internal const string PlayerMotionTickDurationName =
        "tormia.realtime.player_motion.tick.duration";
    internal const string PlayerMotionTickOverrunsName =
        "tormia.realtime.player_motion.tick.overruns";
    internal const string PlayerMotionActiveAvatarsName =
        "tormia.realtime.player_motion.active_avatars";
    internal const string PlayerMotionFrameItemsName =
        "tormia.realtime.player_motion.frame_items";
    internal const string MotionFramePublishCallsName =
        "tormia.realtime.motion_frame.publish.calls";
    internal const string MotionFramePublishItemsName =
        "tormia.realtime.motion_frame.publish.items";
    internal const string MotionFramePublishDurationName =
        "tormia.realtime.motion_frame.publish.duration";
    internal const string MotionFramePublishFailuresName =
        "tormia.realtime.motion_frame.publish.failures";

    private readonly Meter meter = new(MeterName);
    private readonly Histogram<double> playerMotionTickDuration;
    private readonly Counter<long> playerMotionTickOverruns;
    private readonly Histogram<long> playerMotionActiveAvatars;
    private readonly Histogram<long> playerMotionFrameItems;
    private readonly Counter<long> motionFramePublishCalls;
    private readonly Histogram<long> motionFramePublishItems;
    private readonly Histogram<double> motionFramePublishDuration;
    private readonly Counter<long> motionFramePublishFailures;

    public RealtimeTransportMetrics()
    {
        playerMotionTickDuration = meter.CreateHistogram<double>(
            PlayerMotionTickDurationName,
            "ms");
        playerMotionTickOverruns = meter.CreateCounter<long>(
            PlayerMotionTickOverrunsName,
            "ticks");
        playerMotionActiveAvatars = meter.CreateHistogram<long>(
            PlayerMotionActiveAvatarsName,
            "avatars");
        playerMotionFrameItems = meter.CreateHistogram<long>(
            PlayerMotionFrameItemsName,
            "items");
        motionFramePublishCalls = meter.CreateCounter<long>(
            MotionFramePublishCallsName,
            "calls");
        motionFramePublishItems = meter.CreateHistogram<long>(
            MotionFramePublishItemsName,
            "items");
        motionFramePublishDuration = meter.CreateHistogram<double>(
            MotionFramePublishDurationName,
            "ms");
        motionFramePublishFailures = meter.CreateCounter<long>(
            MotionFramePublishFailuresName,
            "failures");
    }

    internal void RecordPlayerMotionTick(
        TimeSpan duration,
        int activeAvatarCount,
        int frameItemCount,
        bool overrun)
    {
        var tags = new TagList
        {
            { "workload", "player_motion" }
        };
        playerMotionTickDuration.Record(
            Math.Max(0d, duration.TotalMilliseconds),
            tags);
        playerMotionActiveAvatars.Record(
            Math.Max(0, activeAvatarCount),
            tags);
        playerMotionFrameItems.Record(
            Math.Max(0, frameItemCount),
            tags);
        if (overrun)
        {
            playerMotionTickOverruns.Add(1, tags);
        }
    }

    internal void RecordMotionFramePublish(
        int itemCount,
        TimeSpan duration,
        string outcome)
    {
        var normalizedOutcome = outcome switch
        {
            "success" => "success",
            "failure" => "failure",
            "cancelled" => "cancelled",
            _ => "unknown"
        };
        var tags = new TagList
        {
            { "transport", "signalr" },
            { "outcome", normalizedOutcome }
        };
        motionFramePublishCalls.Add(1, tags);
        motionFramePublishItems.Record(Math.Max(0, itemCount), tags);
        motionFramePublishDuration.Record(
            Math.Max(0d, duration.TotalMilliseconds),
            tags);
        if (string.Equals(
                normalizedOutcome,
                "failure",
                StringComparison.Ordinal))
        {
            motionFramePublishFailures.Add(1, tags);
        }
    }

    public void Dispose() => meter.Dispose();
}
