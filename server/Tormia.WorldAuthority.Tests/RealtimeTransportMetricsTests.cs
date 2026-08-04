using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

[Collection("Realtime transport metrics")]
public sealed class RealtimeTransportMetricsTests
{
    [Fact]
    public void RecordsMotionTickLoadAndOverrunWithoutScopeIdentifiers()
    {
        var samples = Listen(out var listener);
        using (listener)
        using (var metrics = new RealtimeTransportMetrics())
        {
            metrics.RecordPlayerMotionTick(
                TimeSpan.FromMilliseconds(52.5d),
                activeAvatarCount: 7,
                frameItemCount: 4,
                overrun: true);
        }

        AssertSample(
            samples,
            RealtimeTransportMetrics.PlayerMotionTickDurationName,
            52.5d,
            ("workload", "player_motion"));
        AssertSample(
            samples,
            RealtimeTransportMetrics.PlayerMotionTickOverrunsName,
            1d,
            ("workload", "player_motion"));
        AssertSample(
            samples,
            RealtimeTransportMetrics.PlayerMotionActiveAvatarsName,
            7d);
        AssertSample(
            samples,
            RealtimeTransportMetrics.PlayerMotionFrameItemsName,
            4d);
        Assert.DoesNotContain(
            samples.SelectMany(sample => sample.Tags),
            tag =>
                string.Equals(tag.Key, "world.id", StringComparison.Ordinal) ||
                string.Equals(tag.Key, "zone.key", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordsSignalRPublishOutcomeItemsDurationAndFailure()
    {
        var samples = Listen(out var listener);
        using (listener)
        using (var metrics = new RealtimeTransportMetrics())
        {
            metrics.RecordMotionFramePublish(
                3,
                TimeSpan.FromMilliseconds(4d),
                "success");
            metrics.RecordMotionFramePublish(
                2,
                TimeSpan.FromMilliseconds(5d),
                "failure");
            metrics.RecordMotionFramePublish(
                1,
                TimeSpan.FromMilliseconds(6d),
                "cancelled");
        }

        AssertSample(
            samples,
            RealtimeTransportMetrics.MotionFramePublishCallsName,
            1d,
            ("transport", "signalr"),
            ("outcome", "success"));
        AssertSample(
            samples,
            RealtimeTransportMetrics.MotionFramePublishItemsName,
            2d,
            ("outcome", "failure"));
        AssertSample(
            samples,
            RealtimeTransportMetrics.MotionFramePublishDurationName,
            6d,
            ("outcome", "cancelled"));
        AssertSample(
            samples,
            RealtimeTransportMetrics.MotionFramePublishFailuresName,
            1d,
            ("outcome", "failure"));
        Assert.DoesNotContain(
            samples,
            sample =>
                string.Equals(
                    sample.Name,
                    RealtimeTransportMetrics.MotionFramePublishFailuresName,
                    StringComparison.Ordinal) &&
                sample.Tags.Any(tag =>
                    string.Equals(
                        tag.Value?.ToString(),
                        "cancelled",
                        StringComparison.Ordinal)));
    }

    private static ConcurrentBag<MetricSample> Listen(
        out MeterListener listener)
    {
        var samples = new ConcurrentBag<MetricSample>();
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, activeListener) =>
            {
                if (string.Equals(
                        instrument.Meter.Name,
                        RealtimeTransportMetrics.MeterName,
                        StringComparison.Ordinal))
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((
            instrument, measurement, tags, _) =>
            samples.Add(new MetricSample(
                instrument.Name,
                measurement,
                CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>((
            instrument, measurement, tags, _) =>
            samples.Add(new MetricSample(
                instrument.Name,
                measurement,
                CopyTags(tags))));
        listener.Start();
        return samples;
    }

    private static KeyValuePair<string, object?>[] CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new KeyValuePair<string, object?>[tags.Length];
        tags.CopyTo(copy);
        return copy;
    }

    private static void AssertSample(
        IEnumerable<MetricSample> samples,
        string name,
        double expected,
        params (string Key, string Value)[] expectedTags)
    {
        Assert.Contains(samples, sample =>
            string.Equals(sample.Name, name, StringComparison.Ordinal) &&
            Math.Abs(sample.Value - expected) < 0.001d &&
            expectedTags.All(expectedTag => sample.Tags.Any(tag =>
                string.Equals(
                    tag.Key,
                    expectedTag.Key,
                    StringComparison.Ordinal) &&
                string.Equals(
                    tag.Value?.ToString(),
                    expectedTag.Value,
                    StringComparison.Ordinal))));
    }

    private sealed record MetricSample(
        string Name,
        double Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
