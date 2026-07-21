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
        var input = await repository.GetHeadlessZoneRuntimeInput(worldId, zoneKey, cancellationToken);
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

        var engine = new OntologyRuleEngine();
        var evaluated = 0;
        var skipped = 0;
        var missing = 0;
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
            engine.AddRule(OntologyRuleCompiler.Compile(bound), bound);
            evaluated++;
        }

        if (evaluated == 0)
        {
            return new HeadlessZoneEvaluationResult(
                evaluated,
                skipped,
                missing,
                Array.Empty<HeadlessInferredFact>(),
                missing > 0 ? "missing_published_rule_catalog" : "no_inference_rule_bindings");
        }

        var inferred = new HashSet<OntologyFact>();
        var simulation = new OntologySimulation(maxIterations: 8);
        simulation.RunUntilStable(world, engine, inferred, forceFullInitialEvaluation: true);
        var facts = inferred
            .Select(fact => new HeadlessInferredFact(
                fact.Subject.ToString(), fact.Predicate.ToString(), fact.Object.ToString()))
            .OrderBy(fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(fact => fact.Predicate, StringComparer.Ordinal)
            .ThenBy(fact => fact.Object, StringComparer.Ordinal)
            .ToArray();
        return new HeadlessZoneEvaluationResult(
            evaluated,
            skipped,
            missing,
            facts,
            "inference_only");
    }

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
    IReadOnlyList<string> EntityIds,
    IReadOnlyList<HeadlessAuthoredFact> Facts,
    IReadOnlyList<HeadlessRuleBinding> Bindings);
internal sealed record HeadlessAuthoredFact(
    string SubjectEntityId,
    string PredicateId,
    string ObjectKind,
    string? ObjectEntityId,
    string? ObjectCanonicalId,
    string? ObjectValueJson);
internal sealed record HeadlessRuleBinding(
    string TargetEntityId,
    string RuleId,
    string? ParameterValuesJson,
    string? RulePayloadJson);
internal sealed record HeadlessInferredFact(string Subject, string Predicate, string Object);
internal sealed record HeadlessZoneEvaluationResult(
    int EvaluatedRuleBindingCount,
    int SkippedRuleBindingCount,
    int MissingRuleDefinitionCount,
    IReadOnlyList<HeadlessInferredFact> InferredFacts,
    string EngineStatus);
