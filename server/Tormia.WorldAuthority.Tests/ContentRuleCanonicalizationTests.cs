using System.Text.Json;
using Tormia.Ontology.Core;
using Xunit;

public sealed class ContentRuleCanonicalizationTests
{
    private static readonly JsonSerializerOptions RuleJson =
        new(JsonSerializerDefaults.Web)
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

    [Fact]
    public void EmptyRuntimePresentationPreservesLegacyImmutablePayload()
    {
        const string legacyJson =
            """
            {"id":"ExampleRule","catalogVersion":2,"description":"legacy","conditions":[],"effects":[]}
            """;
        const string unityJson =
            """
            {"id":"ExampleRule","catalogVersion":2,"description":"legacy","conditions":[],"effects":[],"runtimePresentation":{"actorAnimationIntent":""}}
            """;

        var legacy = JsonSerializer.Deserialize<OntologyRuleDefinition>(
            legacyJson,
            RuleJson)!;
        var unity = JsonSerializer.Deserialize<OntologyRuleDefinition>(
            unityJson,
            RuleJson)!;

        var canonicalLegacy =
            ContentCatalogRepository.SerializeCanonicalRulePayload(legacy);
        var canonicalUnity =
            ContentCatalogRepository.SerializeCanonicalRulePayload(unity);

        Assert.Equal(canonicalLegacy, canonicalUnity);
        Assert.DoesNotContain("runtimePresentation", canonicalUnity);
    }

    [Fact]
    public void ConfiguredRuntimePresentationRemainsImmutableRuleContent()
    {
        var definition = new OntologyRuleDefinition
        {
            id = "SwingWeaponOnPrimaryIntent",
            catalogVersion = 1,
            description = "presentation",
            conditions = [],
            effects = [],
            runtimePresentation = new OntologyActionPresentationDefinition
            {
                actorAnimationIntent = "AttackLight"
            }
        };

        var canonical =
            ContentCatalogRepository.SerializeCanonicalRulePayload(definition);

        Assert.Contains("\"runtimePresentation\"", canonical);
        Assert.Contains("\"actorAnimationIntent\":\"AttackLight\"", canonical);
    }

    [Fact]
    public void DefaultRuleBoundLifetimePreservesLegacyImmutablePayload()
    {
        const string legacyJson =
            """
            {"id":"BuoyantWhenInWater","catalogVersion":2,"description":"legacy","conditions":[],"effects":[{"type":0,"subject":"?object","predicate":"is_floating","value":"True","factValueType":0,"valueOperand":"","valueSourceSubject":"","valueSourcePredicate":"","valueSourceFactValueType":0,"removeMatchingFacts":false}]}
            """;
        const string currentJson =
            """
            {"id":"BuoyantWhenInWater","catalogVersion":2,"description":"legacy","conditions":[],"effects":[{"type":0,"subject":"?object","predicate":"is_floating","value":"True","factValueType":0,"valueOperand":"","valueSourceSubject":"","valueSourcePredicate":"","valueSourceFactValueType":0,"removeMatchingFacts":false,"resultLifetime":0}]}
            """;

        var legacy = JsonSerializer.Deserialize<OntologyRuleDefinition>(
            legacyJson,
            RuleJson)!;
        var current = JsonSerializer.Deserialize<OntologyRuleDefinition>(
            currentJson,
            RuleJson)!;

        var canonicalLegacy =
            ContentCatalogRepository.SerializeCanonicalRulePayload(legacy);
        var canonicalCurrent =
            ContentCatalogRepository.SerializeCanonicalRulePayload(current);

        Assert.Equal(canonicalLegacy, canonicalCurrent);
        Assert.DoesNotContain("resultLifetime", canonicalCurrent);
    }

    [Fact]
    public void DurableLifetimeRemainsImmutableRuleContent()
    {
        var definition = new OntologyRuleDefinition
        {
            id = "DamageTarget",
            catalogVersion = 1,
            description = "durable",
            conditions = [],
            effects =
            [
                OntologyEffect.AdjustNumberFact(
                    "?target",
                    "current_health",
                    "-1",
                    resultLifetime:
                        OntologyRuleResultLifetime.DurableState)
            ]
        };

        var canonical =
            ContentCatalogRepository.SerializeCanonicalRulePayload(definition);

        Assert.Contains("\"resultLifetime\":1", canonical);
    }

    [Fact]
    public void DurableResultMigrationIsDerivedFromRuleContent()
    {
        var definition = new OntologyRuleDefinition
        {
            id = "ExampleStateTransition",
            catalogVersion = 2,
            description = "lifetime",
            conditions = [],
            effects =
            [
                OntologyEffect.SetFact(
                    "?target",
                    "is_alive",
                    "False",
                    resultLifetime:
                        OntologyRuleResultLifetime.DurableState),
                OntologyEffect.AdjustNumberFact(
                    "?target",
                    "current_health",
                    "-1",
                    resultLifetime:
                        OntologyRuleResultLifetime.DurableState),
                OntologyEffect.SetFact(
                    "?tool",
                    "equipped_by",
                    "?actor")
            ]
        };

        var predicates =
            ContentCatalogRepository.ResolveDurableResultPredicates(
                definition);

        Assert.Equal(
            ["current_health", "is_alive"],
            predicates);
        Assert.DoesNotContain("equipped_by", predicates);
    }

    [Fact]
    public void DefaultGroundedObservationPreservesLegacyActionPayload()
    {
        const string legacyJson =
            """
            {"actionVerb":"move_avatar","subjectPattern":"?actor","predicate":"","objectPattern":"?actor","requiresTool":false,"evaluationOnly":true,"conditions":[],"effects":[],"ruleInvocation":null,"postRuleInvocations":[],"presentation":{"actorAnimationIntent":""},"runtimeConstraints":{"maxActorTargetDistance":0,"maxActorTargetDistanceFrom":{"subject":"","predicate":"","multiplier":1},"cooldownSecondsFrom":{"subject":"","predicate":"","multiplier":1}}}
            """;
        const string currentJson =
            """
            {"actionVerb":"move_avatar","subjectPattern":"?actor","predicate":"","objectPattern":"?actor","requiresTool":false,"evaluationOnly":true,"conditions":[],"effects":[],"ruleInvocation":null,"postRuleInvocations":[],"presentation":{"actorAnimationIntent":""},"runtimeConstraints":{"maxActorTargetDistance":0,"requiresGroundedObservation":false,"maxActorTargetDistanceFrom":{"subject":"","predicate":"","multiplier":1},"cooldownSecondsFrom":{"subject":"","predicate":"","multiplier":1}}}
            """;

        var legacy =
            JsonSerializer.Deserialize<OntologyActionEffectDefinition>(
                legacyJson,
                RuleJson)!;
        var current =
            JsonSerializer.Deserialize<OntologyActionEffectDefinition>(
                currentJson,
                RuleJson)!;

        var canonicalLegacy =
            ContentCatalogRepository.SerializeCanonicalActionPayload(
                legacy);
        var canonicalCurrent =
            ContentCatalogRepository.SerializeCanonicalActionPayload(
                current);

        Assert.Equal(canonicalLegacy, canonicalCurrent);
        Assert.DoesNotContain(
            "requiresGroundedObservation",
            canonicalCurrent);
    }

    [Fact]
    public void RequiredGroundedObservationRemainsImmutableActionContent()
    {
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = "jump_avatar",
            evaluationOnly = true,
            runtimeConstraints = new OntologyActionRuntimeConstraints
            {
                requiresGroundedObservation = true
            }
        };

        var canonical =
            ContentCatalogRepository.SerializeCanonicalActionPayload(
                definition);

        Assert.Contains(
            "\"requiresGroundedObservation\":true",
            canonical);
    }
}
