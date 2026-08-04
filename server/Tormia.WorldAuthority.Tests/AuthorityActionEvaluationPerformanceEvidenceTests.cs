using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tormia.Ontology.Core;
using Xunit;
using Xunit.Abstractions;

[CollectionDefinition("Authority action metrics", DisableParallelization = true)]
public sealed class AuthorityActionMetricsCollection;

[Collection("Authority action metrics")]
public sealed class AuthorityActionEvaluationPerformanceEvidenceTests(
    ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void HundredThousandFactPostRuleChainReusesOneCompiledSnapshot(
        int postRuleCount)
    {
        const int factCount = 100_000;
        var actor = DeterministicGuid(0);
        var facts = Enumerable.Range(0, factCount)
            .Select(index => new AuthorityFactSnapshot(
                DeterministicGuid(index % 1_000),
                $"post_chain_measurement_{index / 1_000:D4}",
                "canonical",
                $"Value{index % 97:D2}"))
            .Append(new AuthorityFactSnapshot(
                actor, "post_chain_stage", "number", "0"))
            .Concat(Enumerable.Range(0, postRuleCount).Select(index =>
                new AuthorityFactSnapshot(
                    actor,
                    "has_rule_block",
                    "canonical",
                    $"MeasuredPostRule{index}",
                    IsRuleBindingProjection: true)))
            .ToArray();

        var compileStartedAt = Stopwatch.GetTimestamp();
        var snapshot = AuthorityEvaluationSnapshot.Create(facts);
        var compileElapsed = Stopwatch.GetElapsedTime(compileStartedAt);
        var chainStartedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < postRuleCount; index++)
        {
            var ruleId = $"MeasuredPostRule{index}";
            var transport = CreateMeasuredPostTransport(ruleId, index);
            var rule = CreateMeasuredPostRule(ruleId, index);
            var evaluation =
                AuthoritativeActionEvaluator.EvaluateInvokedRule(
                    transport, rule, actor, actor, null, snapshot);
            Assert.True(evaluation.Accepted, evaluation.RejectionCode);
            foreach (var mutation in evaluation.Mutations)
            {
                Assert.True(snapshot.TryApplyCommittedMutation(
                    mutation,
                    DeterministicGuid(20_000 + index),
                    out var overlayRejection), overlayRejection);
            }
        }
        var chainElapsed = Stopwatch.GetElapsedTime(chainStartedAt);

        Assert.Equal(
            [(postRuleCount).ToString(
                System.Globalization.CultureInfo.InvariantCulture)],
            snapshot.GetValues(actor, "post_chain_stage"));
        output.WriteLine(
            "facts={0} post_rules={1} snapshot_loads=1 post_rule_reloads=0 compile_ms={2:F3} chain_ms={3:F3}",
            facts.Length,
            postRuleCount,
            compileElapsed.TotalMilliseconds,
            chainElapsed.TotalMilliseconds);
    }

    [Fact]
    public void RepresentativeActionBatchPublishesDeterministicEvidence()
    {
        var entityCount = ReadScale("TORMIA_PERF_ENTITY_COUNT", 1_000);
        var factCount = ReadScale("TORMIA_PERF_FACT_COUNT", 100_000);
        var ruleCount = ReadScale("TORMIA_PERF_RULE_COUNT", 100);
        var actionCount = ReadScale("TORMIA_PERF_ACTION_COUNT", 20);
        var maxParallelism = ReadScale(
            "TORMIA_PERF_MAX_PARALLELISM", actionCount);

        entityCount = Math.Max(entityCount, actionCount);
        factCount = Math.Max(factCount, entityCount * 2);
        ruleCount = Math.Max(ruleCount, actionCount);
        maxParallelism = Math.Clamp(maxParallelism, 1, actionCount);

        var rules = Enumerable.Range(0, ruleCount)
            .Select(CreateRule)
            .ToArray();
        var ruleValidationStartedAt = Stopwatch.GetTimestamp();
        var validationMessages = OntologyRuleValidator.Validate(rules);
        var ruleValidationElapsed =
            Stopwatch.GetElapsedTime(ruleValidationStartedAt);
        Assert.Empty(validationMessages);

        var snapshotBuildStartedAt = Stopwatch.GetTimestamp();
        var facts = CreateFacts(entityCount, factCount, actionCount);
        var snapshotBuildElapsed =
            Stopwatch.GetElapsedTime(snapshotBuildStartedAt);
        Assert.Equal(factCount, facts.Length);

        var emittedEvaluationMilliseconds = new ConcurrentBag<double>();
        var emittedFactRows = new ConcurrentBag<long>();
        var snapshotCompileMilliseconds = new ConcurrentBag<double>();
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
        listener.SetMeasurementEventCallback<double>((
            instrument, measurement, _, _) =>
        {
            if (string.Equals(
                    instrument.Name,
                    AuthorityActionEvaluationMetrics.EvaluationDurationName,
                    StringComparison.Ordinal))
            {
                emittedEvaluationMilliseconds.Add(measurement);
            }
        });
        listener.SetMeasurementEventCallback<long>((
            instrument, measurement, tags, _) =>
        {
            if (!string.Equals(
                    instrument.Name,
                    AuthorityActionEvaluationMetrics.SnapshotFactRowsName,
                    StringComparison.Ordinal))
            {
                return;
            }

            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "snapshot.phase",
                        StringComparison.Ordinal) &&
                    string.Equals(tag.Value?.ToString(), "prepare",
                        StringComparison.Ordinal))
                {
                    emittedFactRows.Add(measurement);
                    break;
                }
            }
        });
        listener.Start();

        var accepted = 0;
        var batchStartedAt = Stopwatch.GetTimestamp();
        using (var metrics = new AuthorityActionEvaluationMetrics())
        {
            Parallel.For(
                0,
                actionCount,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallelism
                },
                actionIndex =>
                {
                    var actor = DeterministicGuid(actionIndex);
                    var rule = rules[actionIndex];
                    var transport = CreateTransport(rule.id);
                    using var observation = metrics.Begin(
                        "performance_evidence", transport.actionVerb);
                    // This pure harness has no PostgreSQL connection. A zero
                    // duration deliberately distinguishes row-volume evidence
                    // from the production DB duration instrument.
                    observation.RecordSnapshotLoad(
                        "prepare", TimeSpan.Zero, facts.Length, actionCount);
                    observation.RecordBoundRuleValidation(
                        ruleValidationElapsed / ruleCount);
                    var snapshotCompileStartedAt = Stopwatch.GetTimestamp();
                    var evaluationSnapshot =
                        AuthorityEvaluationSnapshot.Create(facts);
                    snapshotCompileMilliseconds.Add(
                        Stopwatch.GetElapsedTime(snapshotCompileStartedAt)
                            .TotalMilliseconds);
                    var evaluationStartedAt = Stopwatch.GetTimestamp();
                    var result =
                        AuthoritativeActionEvaluator.EvaluateInvokedRule(
                            transport,
                            rule,
                            actor,
                            actor,
                            null,
                            evaluationSnapshot);
                    observation.RecordEvaluation(
                        Stopwatch.GetElapsedTime(evaluationStartedAt));
                    if (result.Accepted)
                        Interlocked.Increment(ref accepted);
                });
        }
        var batchElapsed = Stopwatch.GetElapsedTime(batchStartedAt);

        Assert.Equal(actionCount, accepted);
        Assert.Equal(actionCount, emittedEvaluationMilliseconds.Count);
        Assert.Equal(actionCount, emittedFactRows.Count);
        Assert.All(emittedFactRows,
            rows => Assert.Equal(factCount, rows));

        var durations = emittedEvaluationMilliseconds
            .OrderBy(value => value)
            .ToArray();
        var snapshotDurations = snapshotCompileMilliseconds
            .OrderBy(value => value)
            .ToArray();
        output.WriteLine(
            "entities={0} facts={1} rules={2} actions={3} max_parallelism={4}",
            entityCount,
            factCount,
            ruleCount,
            actionCount,
            maxParallelism);
        output.WriteLine(
            "snapshot_build_ms={0:F3} rule_validation_total_ms={1:F3} batch_total_ms={2:F3}",
            snapshotBuildElapsed.TotalMilliseconds,
            ruleValidationElapsed.TotalMilliseconds,
            batchElapsed.TotalMilliseconds);
        output.WriteLine(
            "request_snapshot_compile_sum_ms={0:F3} p50_ms={1:F3} p95_ms={2:F3} p99_ms={3:F3}",
            snapshotDurations.Sum(),
            Percentile(snapshotDurations, 0.50),
            Percentile(snapshotDurations, 0.95),
            Percentile(snapshotDurations, 0.99));
        output.WriteLine(
            "evaluation_sum_ms={0:F3} p50_ms={1:F3} p95_ms={2:F3} p99_ms={3:F3}",
            durations.Sum(),
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            Percentile(durations, 0.99));
    }

    private static OntologyRuleDefinition CreateRule(int index)
    {
        var id = RuleId(index);
        return new OntologyRuleDefinition
        {
            id = id,
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", "performance_intent", "?actor"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", id),
                OntologyCondition.HasConcept("?actor", "Actor")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "performance_result", "Evaluated")
            ]
        };
    }

    private static OntologyActionEffectDefinition CreateTransport(
        string ruleId) =>
        new()
        {
            actionVerb = "performance_action",
            objectPattern = "?actor",
            evaluationOnly = true,
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = ruleId,
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "performance_intent",
                intentObjectPattern = "?actor"
            }
        };

    private static OntologyActionEffectDefinition CreateMeasuredPostTransport(
        string ruleId,
        int index) =>
        new()
        {
            actionVerb = "measured_post_chain",
            objectPattern = "?actor",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = ruleId,
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = $"measured_post_intent_{index}",
                intentObjectPattern = "?actor"
            }
        };

    private static OntologyRuleDefinition CreateMeasuredPostRule(
        string ruleId,
        int index) =>
        new()
        {
            id = ruleId,
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", $"measured_post_intent_{index}", "?actor"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", ruleId),
                OntologyCondition.Fact(
                    "?actor", "post_chain_stage", index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor",
                    "post_chain_stage",
                    (index + 1).ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
            ]
        };

    private static AuthorityFactSnapshot[] CreateFacts(
        int entityCount,
        int factCount,
        int actionCount)
    {
        var facts = new AuthorityFactSnapshot[factCount];
        for (var index = 0; index < factCount; index++)
        {
            var entityIndex = index % entityCount;
            var predicateIndex = index / entityCount;
            var entity = DeterministicGuid(entityIndex);
            if (predicateIndex == 0 && entityIndex < actionCount)
            {
                facts[index] = new AuthorityFactSnapshot(
                    entity,
                    "has_rule_block",
                    "canonical",
                    RuleId(entityIndex));
            }
            else if (predicateIndex == 1 && entityIndex < actionCount)
            {
                facts[index] = new AuthorityFactSnapshot(
                    entity,
                    "has_concept",
                    "canonical",
                    "Actor");
            }
            else
            {
                facts[index] = new AuthorityFactSnapshot(
                    entity,
                    $"measurement_fact_{predicateIndex:D4}",
                    "number",
                    (index % 97).ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return facts;
    }

    private static Guid DeterministicGuid(int value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, value + 1);
        BitConverter.TryWriteBytes(bytes[8..], value + 10_001);
        return new Guid(bytes);
    }

    private static string RuleId(int index) =>
        $"PerformanceRule{index:D3}";

    private static int ReadScale(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : fallback;
    }

    private static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        Assert.NotEmpty(sortedValues);
        var rank = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(rank, 0, sortedValues.Count - 1)];
    }
}
