using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

[Collection("Authority action metrics")]
public sealed class AuthorityPostRuleSnapshotMetricsTests
{
    [Fact]
    public void CommandScopedSnapshotPublishesOnePrepareLoadAndNoPostRuleReload()
    {
        var samples = new ConcurrentBag<SnapshotLoadSample>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, activeListener) =>
            {
                if (string.Equals(
                        instrument.Meter.Name,
                        AuthorityActionEvaluationMetrics.MeterName,
                        StringComparison.Ordinal))
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((
            instrument, measurement, tags, _) =>
        {
            if (!string.Equals(
                    instrument.Name,
                    AuthorityActionEvaluationMetrics.SnapshotCountName,
                    StringComparison.Ordinal))
            {
                return;
            }

            samples.Add(new SnapshotLoadSample(
                Phase(tags),
                measurement));
        });
        listener.Start();

        using (var metrics = new AuthorityActionEvaluationMetrics())
        {
            using var observation = metrics.Begin(
                "performance_evidence", "post_rule_snapshot_contract");
            observation.RecordSnapshotLoad(
                "prepare",
                TimeSpan.FromMilliseconds(5),
                factRows: 100_000,
                bindingRows: 4);

            // Post-rules consume the command-scoped snapshot and its committed
            // mutation overlay. No post_rule DB snapshot load is recorded.
        }

        Assert.Contains(samples, sample =>
            sample.Phase == "prepare" && sample.Count == 1L);
        Assert.Contains(samples, sample =>
            sample.Phase == "post_rule" && sample.Count == 0L);
    }

    private static string Phase(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(
                    tag.Key,
                    "snapshot.phase",
                    StringComparison.Ordinal))
            {
                return tag.Value?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private sealed record SnapshotLoadSample(string Phase, long Count);
}
