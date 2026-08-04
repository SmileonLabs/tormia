using System.Diagnostics;
using System.Diagnostics.Metrics;

internal sealed class AuthorityActionEvaluationMetrics : IDisposable
{
    internal const string MeterName = "Tormia.WorldAuthority.ActionEvaluation";
    internal const string CallsName = "tormia.authority.action_evaluation.calls";
    internal const string TotalDurationName = "tormia.authority.action_evaluation.duration";
    internal const string SnapshotCountName = "tormia.authority.action_evaluation.snapshot.loads";
    internal const string SnapshotDurationName = "tormia.authority.action_evaluation.snapshot.db.duration";
    internal const string SnapshotFactRowsName = "tormia.authority.action_evaluation.snapshot.fact.rows";
    internal const string SnapshotBindingRowsName = "tormia.authority.action_evaluation.snapshot.binding.rows";
    internal const string ActionDefinitionDbDurationName = "tormia.authority.action_evaluation.action_definition.db.duration";
    internal const string ActionDefinitionDeserializeDurationName = "tormia.authority.action_evaluation.action_definition.deserialize.duration";
    internal const string BoundRuleLookupCountName = "tormia.authority.action_evaluation.bound_rule.lookups";
    internal const string BoundRuleDbDurationName = "tormia.authority.action_evaluation.bound_rule.db.duration";
    internal const string BoundRuleDeserializeDurationName = "tormia.authority.action_evaluation.bound_rule.deserialize.duration";
    internal const string BoundRuleValidationDurationName = "tormia.authority.action_evaluation.bound_rule.validation.duration";
    internal const string EvaluationDurationName = "tormia.authority.action_evaluation.rule_evaluation.duration";
    internal const string PostRuleSnapshotReloadCountName = "tormia.authority.action_evaluation.post_rule.snapshot_reloads";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> calls;
    private readonly Histogram<double> totalDuration;
    private readonly Histogram<long> snapshotCount;
    private readonly Histogram<double> snapshotDuration;
    private readonly Histogram<long> snapshotFactRows;
    private readonly Histogram<long> snapshotBindingRows;
    private readonly Histogram<double> actionDefinitionDbDuration;
    private readonly Histogram<double> actionDefinitionDeserializeDuration;
    private readonly Histogram<long> boundRuleLookupCount;
    private readonly Histogram<double> boundRuleDbDuration;
    private readonly Histogram<double> boundRuleDeserializeDuration;
    private readonly Histogram<double> boundRuleValidationDuration;
    private readonly Histogram<double> evaluationDuration;
    private readonly Histogram<long> postRuleSnapshotReloadCount;

    public AuthorityActionEvaluationMetrics()
    {
        calls = meter.CreateCounter<long>(CallsName, "calls");
        totalDuration = meter.CreateHistogram<double>(TotalDurationName, "ms");
        snapshotCount = meter.CreateHistogram<long>(SnapshotCountName, "loads");
        snapshotDuration = meter.CreateHistogram<double>(SnapshotDurationName, "ms");
        snapshotFactRows = meter.CreateHistogram<long>(SnapshotFactRowsName, "rows");
        snapshotBindingRows = meter.CreateHistogram<long>(SnapshotBindingRowsName, "rows");
        actionDefinitionDbDuration = meter.CreateHistogram<double>(ActionDefinitionDbDurationName, "ms");
        actionDefinitionDeserializeDuration = meter.CreateHistogram<double>(ActionDefinitionDeserializeDurationName, "ms");
        boundRuleLookupCount = meter.CreateHistogram<long>(BoundRuleLookupCountName, "lookups");
        boundRuleDbDuration = meter.CreateHistogram<double>(BoundRuleDbDurationName, "ms");
        boundRuleDeserializeDuration = meter.CreateHistogram<double>(BoundRuleDeserializeDurationName, "ms");
        boundRuleValidationDuration = meter.CreateHistogram<double>(BoundRuleValidationDurationName, "ms");
        evaluationDuration = meter.CreateHistogram<double>(EvaluationDurationName, "ms");
        postRuleSnapshotReloadCount = meter.CreateHistogram<long>(PostRuleSnapshotReloadCountName, "reloads");
    }

    internal AuthorityActionEvaluationObservation Begin(
        string callKind,
        string _) =>
        new(this, callKind);

    internal void Record(AuthorityActionEvaluationObservation observation)
    {
        var tags = new TagList
        {
            { "call.kind", observation.CallKind },
            { "evaluation.scope", "prepare_action_evaluation" }
        };
        calls.Add(1, tags);
        totalDuration.Record(observation.ElapsedMilliseconds, tags);
        actionDefinitionDbDuration.Record(
            observation.ActionDefinitionDbMilliseconds, tags);
        actionDefinitionDeserializeDuration.Record(
            observation.ActionDefinitionDeserializeMilliseconds, tags);
        boundRuleLookupCount.Record(observation.BoundRuleLookupCount, tags);
        boundRuleDbDuration.Record(observation.BoundRuleDbMilliseconds, tags);
        boundRuleDeserializeDuration.Record(
            observation.BoundRuleDeserializeMilliseconds, tags);
        boundRuleValidationDuration.Record(
            observation.BoundRuleValidationMilliseconds, tags);
        evaluationDuration.Record(observation.EvaluationMilliseconds, tags);
        postRuleSnapshotReloadCount.Record(
            observation.PostRuleSnapshotReloadCount, tags);

        RecordSnapshotPhase(
            "prepare",
            observation.PrepareSnapshotLoadCount,
            observation.PrepareSnapshotDbMilliseconds,
            observation.PrepareSnapshotFactRows,
            observation.PrepareSnapshotBindingRows,
            tags);
        RecordSnapshotPhase(
            "post_rule",
            observation.PostRuleSnapshotReloadCount,
            observation.PostRuleSnapshotDbMilliseconds,
            observation.PostRuleSnapshotFactRows,
            observation.PostRuleSnapshotBindingRows,
            tags);
    }

    private void RecordSnapshotPhase(
        string phase,
        long count,
        double durationMilliseconds,
        long factRows,
        long bindingRows,
        TagList commonTags)
    {
        var tags = commonTags;
        tags.Add("snapshot.phase", phase);
        snapshotCount.Record(count, tags);
        snapshotDuration.Record(durationMilliseconds, tags);
        snapshotFactRows.Record(factRows, tags);
        snapshotBindingRows.Record(bindingRows, tags);
    }

    public void Dispose() => meter.Dispose();
}

internal sealed class AuthorityActionEvaluationObservation : IDisposable
{
    private readonly AuthorityActionEvaluationMetrics owner;
    private readonly long startedAt = Stopwatch.GetTimestamp();
    private bool disposed;

    internal AuthorityActionEvaluationObservation(
        AuthorityActionEvaluationMetrics owner,
        string callKind)
    {
        this.owner = owner;
        CallKind = callKind?.Trim() switch
        {
            "execute_command" => "execute_command",
            "execute_autonomous" => "execute_autonomous",
            "evaluate_runtime" => "evaluate_runtime",
            "preview" => "preview",
            "preview_autonomous" => "preview_autonomous",
            "performance_evidence" => "performance_evidence",
            _ => "unknown"
        };
    }

    internal string CallKind { get; }
    internal double ElapsedMilliseconds =>
        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    internal double ActionDefinitionDbMilliseconds { get; private set; }
    internal double ActionDefinitionDeserializeMilliseconds { get; private set; }
    internal long BoundRuleLookupCount { get; private set; }
    internal double BoundRuleDbMilliseconds { get; private set; }
    internal double BoundRuleDeserializeMilliseconds { get; private set; }
    internal double BoundRuleValidationMilliseconds { get; private set; }
    internal double EvaluationMilliseconds { get; private set; }
    internal long PrepareSnapshotLoadCount { get; private set; }
    internal double PrepareSnapshotDbMilliseconds { get; private set; }
    internal long PrepareSnapshotFactRows { get; private set; }
    internal long PrepareSnapshotBindingRows { get; private set; }
    internal long PostRuleSnapshotReloadCount { get; private set; }
    internal double PostRuleSnapshotDbMilliseconds { get; private set; }
    internal long PostRuleSnapshotFactRows { get; private set; }
    internal long PostRuleSnapshotBindingRows { get; private set; }

    internal void RecordActionDefinitionDb(TimeSpan duration) =>
        ActionDefinitionDbMilliseconds += duration.TotalMilliseconds;

    internal void RecordActionDefinitionDeserialize(TimeSpan duration) =>
        ActionDefinitionDeserializeMilliseconds += duration.TotalMilliseconds;

    internal void RecordBoundRuleDb(TimeSpan duration)
    {
        BoundRuleLookupCount++;
        BoundRuleDbMilliseconds += duration.TotalMilliseconds;
    }

    internal void RecordBoundRuleDeserialize(TimeSpan duration) =>
        BoundRuleDeserializeMilliseconds += duration.TotalMilliseconds;

    internal void RecordBoundRuleValidation(TimeSpan duration) =>
        BoundRuleValidationMilliseconds += duration.TotalMilliseconds;

    internal void RecordEvaluation(TimeSpan duration) =>
        EvaluationMilliseconds += duration.TotalMilliseconds;

    internal void RecordSnapshotLoad(
        string phase,
        TimeSpan duration,
        int factRows,
        int bindingRows)
    {
        if (string.Equals(phase, "post_rule", StringComparison.Ordinal))
        {
            PostRuleSnapshotReloadCount++;
            PostRuleSnapshotDbMilliseconds += duration.TotalMilliseconds;
            PostRuleSnapshotFactRows += factRows;
            PostRuleSnapshotBindingRows += bindingRows;
            return;
        }

        PrepareSnapshotLoadCount++;
        PrepareSnapshotDbMilliseconds += duration.TotalMilliseconds;
        PrepareSnapshotFactRows += factRows;
        PrepareSnapshotBindingRows += bindingRows;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.Record(this);
    }
}
