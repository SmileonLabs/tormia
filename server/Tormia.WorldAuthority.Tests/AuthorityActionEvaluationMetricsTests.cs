using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Xunit;

[Collection("Authority action metrics")]
public sealed class AuthorityActionEvaluationMetricsTests
{
    [Fact]
    public void ObservationPublishesPerCallPhaseAndRowMeasurements()
    {
        var samples = new ConcurrentBag<MetricSample>();
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

        using (var metrics = new AuthorityActionEvaluationMetrics())
        {
            using var observation = metrics.Begin(
                "execute_command", "user_supplied_unbounded_action_9f284f");
            observation.RecordActionDefinitionDb(
                TimeSpan.FromMilliseconds(2));
            observation.RecordActionDefinitionDeserialize(
                TimeSpan.FromMilliseconds(3));
            observation.RecordBoundRuleDb(
                TimeSpan.FromMilliseconds(5));
            observation.RecordBoundRuleDeserialize(
                TimeSpan.FromMilliseconds(7));
            observation.RecordBoundRuleValidation(
                TimeSpan.FromMilliseconds(11));
            observation.RecordEvaluation(
                TimeSpan.FromMilliseconds(13));
            observation.RecordSnapshotLoad(
                "prepare", TimeSpan.FromMilliseconds(17), 101, 9);
            observation.RecordSnapshotLoad(
                "post_rule", TimeSpan.FromMilliseconds(19), 103, 9);
            observation.RecordSnapshotLoad(
                "post_rule", TimeSpan.FromMilliseconds(23), 107, 9);
        }

        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.CallsName,
            1,
            ("call.kind", "execute_command"),
            ("evaluation.scope", "prepare_action_evaluation"));
        Assert.DoesNotContain(samples.SelectMany(sample => sample.Tags),
            tag => string.Equals(
                tag.Key, "action.id", StringComparison.Ordinal));
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.SnapshotCountName,
            1,
            ("snapshot.phase", "prepare"));
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.SnapshotCountName,
            2,
            ("snapshot.phase", "post_rule"));
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.SnapshotFactRowsName,
            210,
            ("snapshot.phase", "post_rule"));
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.SnapshotBindingRowsName,
            18,
            ("snapshot.phase", "post_rule"));
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.BoundRuleLookupCountName,
            1);
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.EvaluationDurationName,
            13);
        AssertSample(
            samples,
            AuthorityActionEvaluationMetrics.PostRuleSnapshotReloadCountName,
            2);
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
                string.Equals(tag.Key, expectedTag.Key,
                    StringComparison.Ordinal) &&
                string.Equals(tag.Value?.ToString(), expectedTag.Value,
                    StringComparison.Ordinal))));
    }

    private sealed record MetricSample(
        string Name,
        double Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
