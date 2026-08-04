using System.Diagnostics;
using System.Text.Json;
using Tormia.Ontology.Core;

/// <summary>
/// Server-side, inference-only evaluation of a Zone's durable authored graph.
/// It deliberately accepts AddFact effects only: persistent mutations must travel
/// through an explicit authority command/action path, not appear as a background
/// scheduler side effect. Results are ephemeral runtime facts and are never
/// written into world_facts.
/// </summary>
internal sealed class HeadlessZoneOntologyEvaluator(WorldAuthorityRepository repository)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<HeadlessZoneEvaluationResult> Evaluate(
        Guid worldId,
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var totalStartedAt = Stopwatch.GetTimestamp();
        var stageStartedAt = Stopwatch.GetTimestamp();
        var input = await repository.GetHeadlessZoneRuntimeInput(worldId, zoneKey, cancellationToken);
        var databaseLoadDurationMilliseconds = ElapsedMilliseconds(stageStartedAt);
        if (input.WorldRevision < 0)
        {
            return new HeadlessZoneEvaluationResult(
                0,
                0,
                0,
                Array.Empty<HeadlessInferredFact>(),
                "world_revision_unavailable",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0d,
                databaseLoadDurationMilliseconds,
                0d,
                0d,
                0d,
                0d,
                ElapsedMilliseconds(totalStartedAt),
                input.WorldRevision,
                null,
                false);
        }

        stageStartedAt = Stopwatch.GetTimestamp();
        var world = new OntologyWorldState();
        foreach (var entityId in input.EntityIds)
        {
            world.GetOrCreateEntity(entityId);
        }

        foreach (var fact in input.Facts)
        {
            var objectId = ResolveObject(fact);
            if (!string.IsNullOrWhiteSpace(objectId))
            {
                world.AddFact(fact.SubjectEntityId, fact.PredicateId, objectId);
            }
        }
        var worldBuildDurationMilliseconds = ElapsedMilliseconds(stageStartedAt);

        stageStartedAt = Stopwatch.GetTimestamp();
        var engine = new OntologyRuleEngine();
        var evaluated = 0;
        var skipped = 0;
        var missing = 0;
        var compilationAttempts = 0;
        var compiled = 0;
        foreach (var binding in input.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.RulePayloadJson))
            {
                missing++;
                continue;
            }

            OntologyRuleDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<OntologyRuleDefinition>(
                    binding.RulePayloadJson, JsonOptions);
            }
            catch (JsonException)
            {
                skipped++;
                continue;
            }

            if (definition == null || definition.effects == null ||
                definition.effects.Any(effect => effect == null ||
                    effect.kind != OntologyEffectKind.AddFact) ||
                OntologyRuleValidator.Validate(new[] { definition }).Count > 0)
            {
                skipped++;
                continue;
            }

            var bound = BindDefinition(
                definition,
                ParseBindingVariable(binding.ParameterValuesJson),
                binding.TargetEntityId);
            compilationAttempts++;
            engine.AddRule(OntologyRuleCompiler.Compile(bound), bound);
            compiled++;
            evaluated++;
        }
        var rulePreparationDurationMilliseconds = ElapsedMilliseconds(stageStartedAt);

        if (evaluated == 0)
        {
            var emptyResult = new HeadlessZoneEvaluationResult(
                evaluated,
                skipped,
                missing,
                Array.Empty<HeadlessInferredFact>(),
                missing > 0 ? "missing_published_rule_catalog" : "no_inference_rule_bindings",
                input.EntityIds.Count,
                input.Facts.Count,
                input.Bindings.Count,
                compilationAttempts,
                compiled,
                0,
                0,
                0,
                0d,
                databaseLoadDurationMilliseconds,
                worldBuildDurationMilliseconds,
                rulePreparationDurationMilliseconds,
                0d,
                0d,
                ElapsedMilliseconds(totalStartedAt),
                input.WorldRevision,
                null,
                false);
            return await ConfirmCurrentRevision(
                worldId,
                input.WorldRevision,
                emptyResult,
                cancellationToken);
        }

        var inferred = new HashSet<OntologyFact>();
        var simulation = new OntologySimulation(maxIterations: 8);
        stageStartedAt = Stopwatch.GetTimestamp();
        var simulationResult = simulation.RunUntilStable(
            world,
            engine,
            inferred,
            forceFullInitialEvaluation: true);
        var simulationDurationMilliseconds = ElapsedMilliseconds(stageStartedAt);

        stageStartedAt = Stopwatch.GetTimestamp();
        var facts = inferred
            .Select(fact => new HeadlessInferredFact(
                fact.Subject.ToString(), fact.Predicate.ToString(), fact.Object.ToString()))
            .OrderBy(fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(fact => fact.Predicate, StringComparer.Ordinal)
            .ThenBy(fact => fact.Object, StringComparer.Ordinal)
            .ToArray();
        var resultMaterializationDurationMilliseconds =
            ElapsedMilliseconds(stageStartedAt);
        var result = new HeadlessZoneEvaluationResult(
            evaluated,
            skipped,
            missing,
            facts,
            "inference_only",
            input.EntityIds.Count,
            input.Facts.Count,
            input.Bindings.Count,
            compilationAttempts,
            compiled,
            simulationResult.Iterations,
            simulationResult.TotalEvaluatedRules,
            simulationResult.TotalSkippedRules,
            simulationResult.TotalEvaluationElapsedMilliseconds,
            databaseLoadDurationMilliseconds,
            worldBuildDurationMilliseconds,
            rulePreparationDurationMilliseconds,
            simulationDurationMilliseconds,
            resultMaterializationDurationMilliseconds,
            ElapsedMilliseconds(totalStartedAt),
            input.WorldRevision,
            null,
            false);
        return await ConfirmCurrentRevision(
            worldId,
            input.WorldRevision,
            result,
            cancellationToken);
    }

    private async Task<HeadlessZoneEvaluationResult> ConfirmCurrentRevision(
        Guid worldId,
        long inputWorldRevision,
        HeadlessZoneEvaluationResult result,
        CancellationToken cancellationToken)
    {
        var observedWorldRevision = await repository.GetCurrentRevision(
            worldId,
            cancellationToken);
        if (HeadlessZoneRevisionPolicy.IsPublishable(
                inputWorldRevision,
                observedWorldRevision))
        {
            return result with
            {
                ObservedWorldRevision = observedWorldRevision,
                IsPublishable = true
            };
        }

        return result with
        {
            InferredFacts = Array.Empty<HeadlessInferredFact>(),
            EngineStatus = observedWorldRevision.HasValue
                ? "stale_world_revision"
                : "world_revision_unavailable",
            ObservedWorldRevision = observedWorldRevision,
            IsPublishable = false
        };
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    private static string ResolveObject(HeadlessAuthoredFact fact)
    {
        return fact.ObjectKind switch
        {
            "entity" => fact.ObjectEntityId ?? string.Empty,
            "canonical" => fact.ObjectCanonicalId ?? string.Empty,
            _ => NormalizeJsonValue(fact.ObjectValueJson)
        };
    }

    private static string NormalizeJsonValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString() ?? string.Empty
                : document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ParseBindingVariable(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "?target";
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("bindingVariable", out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : "?target";
        }
        catch (JsonException)
        {
            return "?target";
        }
    }

    private static OntologyRuleDefinition BindDefinition(
        OntologyRuleDefinition source,
        string variable,
        string entityId)
    {
        var clone = new OntologyRuleDefinition
        {
            id = source.id + "@" + entityId,
            description = source.description
        };
        foreach (var condition in source.conditions.Where(value => value != null))
        {
            clone.conditions.Add(new OntologyCondition
            {
                kind = condition.kind,
                subject = Replace(condition.subject, variable, entityId),
                predicate = Replace(condition.predicate, variable, entityId),
                obj = Replace(condition.obj, variable, entityId)
            });
        }
        foreach (var effect in source.effects.Where(value => value != null))
        {
            clone.effects.Add(new OntologyEffect
            {
                kind = effect.kind,
                subject = Replace(effect.subject, variable, entityId),
                predicate = Replace(effect.predicate, variable, entityId),
                obj = Replace(effect.obj, variable, entityId)
            });
        }
        return clone;
    }

    private static string Replace(string value, string variable, string entityId) =>
        value == variable ? entityId : value;
}

internal sealed record HeadlessZoneRuntimeInput(
    long WorldRevision,
    IReadOnlyList<string> EntityIds,
    IReadOnlyList<HeadlessAuthoredFact> Facts,
    IReadOnlyList<HeadlessRuleBinding> Bindings)
{
    public static HeadlessZoneRuntimeInput Unavailable { get; } = new(
        -1,
        Array.Empty<string>(),
        Array.Empty<HeadlessAuthoredFact>(),
        Array.Empty<HeadlessRuleBinding>());
}
internal sealed record HeadlessAuthoredFact(
    string SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    string? ObjectEntityId,
    string? ObjectCanonicalId,
    string? ObjectValueJson);
internal sealed record HeadlessRuleBinding(
    Guid BindingId,
    string TargetEntityId,
    string RuleId,
    int RuleVersion,
    long CreatedRevision,
    string? ParameterValuesJson,
    string? RuleDefinitionChecksum,
    string? RulePayloadJson);
internal sealed record HeadlessInferredFact(string Subject, string Predicate, string Object);
internal sealed record HeadlessZoneEvaluationResult(
    int EvaluatedRuleBindingCount,
    int SkippedRuleBindingCount,
    int MissingRuleDefinitionCount,
    IReadOnlyList<HeadlessInferredFact> InferredFacts,
    string EngineStatus,
    int InputEntityCount,
    int InputFactCount,
    int InputRuleBindingCount,
    int RuleCompilationAttemptCount,
    int CompiledRuleCount,
    int SimulationIterationCount,
    int CoreEvaluatedRuleCount,
    int CoreSkippedRuleCount,
    double CoreRuleEvaluationDurationMilliseconds,
    double DatabaseLoadDurationMilliseconds,
    double WorldBuildDurationMilliseconds,
    double RulePreparationDurationMilliseconds,
    double SimulationDurationMilliseconds,
    double ResultMaterializationDurationMilliseconds,
    double EvaluationDurationMilliseconds,
    long InputWorldRevision,
    long? ObservedWorldRevision,
    bool IsPublishable);

internal static class HeadlessZoneRevisionPolicy
{
    public static bool IsPublishable(
        long inputWorldRevision,
        long? observedWorldRevision) =>
        inputWorldRevision >= 0 &&
        observedWorldRevision.HasValue &&
        inputWorldRevision == observedWorldRevision.Value;
}
