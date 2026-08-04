using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tormia.Ontology.Core;
using Xunit;
using Xunit.Abstractions;

[Collection("Authority action metrics")]
public sealed class AuthorityPostRuleSnapshotPerformanceEvidenceTests(
    ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ExplicitPerformanceEvidence")]
    public void ExplicitPostRuleCountsReuseOneHundredThousandFactSnapshot()
    {
        var factCount = ReadPositiveScale(
            "TORMIA_PERF_POST_RULE_FACT_COUNT", 100_000);
        var postRuleCounts = ReadPostRuleCounts();
        var actor = DeterministicGuid(0);
        WarmUp(actor);
        var facts = CreateFacts(factCount, actor);

        foreach (var postRuleCount in postRuleCounts)
        {
            MeasureScenario(facts, actor, postRuleCount);
        }
    }

    private static void WarmUp(Guid actor)
    {
        var snapshot = AuthorityEvaluationSnapshot.Create(
            CreateFacts(6, actor));
        var rule = CreatePostRule(0);
        Assert.Empty(OntologyRuleValidator.Validate(new[] { rule }));
        var evaluation = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            CreateTransport(rule.id),
            rule,
            actor,
            actor,
            null,
            snapshot);
        Assert.True(evaluation.Accepted, evaluation.RejectionCode);
    }

    private void MeasureScenario(
        IReadOnlyList<AuthorityFactSnapshot> facts,
        Guid actor,
        int postRuleCount)
    {
        var snapshotLoads = new ConcurrentBag<SnapshotLoadSample>();
        var reloadCounts = new ConcurrentBag<long>();
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
            if (string.Equals(
                    instrument.Name,
                    AuthorityActionEvaluationMetrics.SnapshotCountName,
                    StringComparison.Ordinal))
            {
                snapshotLoads.Add(new SnapshotLoadSample(
                    Phase(tags), measurement));
            }
            else if (string.Equals(
                         instrument.Name,
                         AuthorityActionEvaluationMetrics
                             .PostRuleSnapshotReloadCountName,
                         StringComparison.Ordinal))
            {
                reloadCounts.Add(measurement);
            }
        });
        listener.Start();

        var snapshotStartedAt = Stopwatch.GetTimestamp();
        var snapshot = AuthorityEvaluationSnapshot.Create(facts);
        var snapshotElapsed = Stopwatch.GetElapsedTime(snapshotStartedAt);
        var evaluationElapsed = TimeSpan.Zero;
        var accepted = 0;

        using (var metrics = new AuthorityActionEvaluationMetrics())
        {
            using var observation = metrics.Begin(
                "performance_evidence", $"post_rule_count_{postRuleCount}");
            observation.RecordSnapshotLoad(
                "prepare", snapshotElapsed, facts.Count, 4);

            for (var index = 0; index < postRuleCount; index++)
            {
                var rule = CreatePostRule(index);
                Assert.Empty(OntologyRuleValidator.Validate(new[] { rule }));
                var evaluationStartedAt = Stopwatch.GetTimestamp();
                var evaluation =
                    AuthoritativeActionEvaluator.EvaluateInvokedRule(
                        CreateTransport(rule.id),
                        rule,
                        actor,
                        actor,
                        null,
                        snapshot);
                var elapsed = Stopwatch.GetElapsedTime(evaluationStartedAt);
                evaluationElapsed += elapsed;
                observation.RecordEvaluation(elapsed);
                Assert.True(evaluation.Accepted, evaluation.RejectionCode);

                foreach (var mutation in evaluation.Mutations)
                {
                    Assert.True(snapshot.TryApplyCommittedMutation(
                        mutation,
                        DeterministicGuid(index + 100),
                        out var rejection), rejection);
                }
                accepted++;
            }
        }

        Assert.Equal(postRuleCount, accepted);
        Assert.Contains(snapshotLoads, sample =>
            sample.Phase == "prepare" && sample.Count == 1L);
        Assert.Contains(snapshotLoads, sample =>
            sample.Phase == "post_rule" && sample.Count == 0L);
        Assert.Contains(reloadCounts, count => count == 0L);

        output.WriteLine(
            "facts={0} post_rules={1} prepare_snapshot_loads=1 post_rule_snapshot_reloads=0 snapshot_compile_ms={2:F3} post_rule_evaluation_ms={3:F3}",
            facts.Count,
            postRuleCount,
            snapshotElapsed.TotalMilliseconds,
            evaluationElapsed.TotalMilliseconds);
    }

    private static OntologyRuleDefinition CreatePostRule(int index)
    {
        var conditions = new List<OntologyCondition>
        {
            OntologyCondition.Fact(
                "?actor", "performance_post_rule_intent", "?actor"),
            OntologyCondition.Fact(
                "?actor", "has_rule_block", RuleId(index)),
            OntologyCondition.HasConcept("?actor", "Actor")
        };
        conditions.Add(index == 0
            ? OntologyCondition.Fact(
                "?actor", "performance_post_rule_seed", "Ready")
            : OntologyCondition.Fact(
                "?actor", ResultPredicate(index - 1), "Applied"));

        return new OntologyRuleDefinition
        {
            id = RuleId(index),
            catalogVersion = 1,
            conditions = conditions,
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", ResultPredicate(index), "Applied")
            ]
        };
    }

    private static OntologyActionEffectDefinition CreateTransport(
        string ruleId) =>
        new()
        {
            actionVerb = "performance_post_rule_action",
            objectPattern = "?actor",
            evaluationOnly = true,
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = ruleId,
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "performance_post_rule_intent",
                intentObjectPattern = "?actor"
            }
        };

    private static AuthorityFactSnapshot[] CreateFacts(
        int factCount,
        Guid actor)
    {
        const int contractFactCount = 6;
        factCount = Math.Max(factCount, contractFactCount);
        var facts = new AuthorityFactSnapshot[factCount];
        facts[0] = new AuthorityFactSnapshot(
            actor, "has_concept", "canonical", "Actor");
        facts[1] = new AuthorityFactSnapshot(
            actor, "performance_post_rule_seed", "canonical", "Ready");
        for (var index = 0; index < 4; index++)
        {
            facts[index + 2] = new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", RuleId(index));
        }

        for (var index = contractFactCount; index < factCount; index++)
        {
            var entityIndex = index % 1_000;
            var predicateIndex = index / 1_000;
            facts[index] = new AuthorityFactSnapshot(
                DeterministicGuid(entityIndex + 1),
                $"post_rule_measurement_{predicateIndex:D4}",
                "number",
                (index % 97).ToString(CultureInfo.InvariantCulture));
        }

        return facts;
    }

    private static IReadOnlyList<int> ReadPostRuleCounts()
    {
        var raw = Environment.GetEnvironmentVariable(
            "TORMIA_PERF_POST_RULE_COUNTS");
        if (string.IsNullOrWhiteSpace(raw)) return new[] { 0, 1, 4 };

        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value.Trim(), out var parsed)
                ? parsed
                : -1)
            .Where(value => value is >= 0 and <= 4)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        return values.Length > 0 ? values : new[] { 0, 1, 4 };
    }

    private static int ReadPositiveScale(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : fallback;
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

    private static Guid DeterministicGuid(int value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, value + 1);
        BitConverter.TryWriteBytes(bytes[8..], value + 10_001);
        return new Guid(bytes);
    }

    private static string RuleId(int index) =>
        $"PerformancePostRule{index:D2}";

    private static string ResultPredicate(int index) =>
        $"performance_post_rule_result_{index:D2}";

    private sealed record SnapshotLoadSample(string Phase, long Count);
}
