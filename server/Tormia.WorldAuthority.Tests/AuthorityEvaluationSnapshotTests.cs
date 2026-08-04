using System.Collections.Concurrent;
using Tormia.Ontology.Core;
using Xunit;

public sealed class AuthorityEvaluationSnapshotTests
{
    [Fact]
    public void CommandDeltaDoesNotMutateCompiledBaseOrSiblingRequest()
    {
        var actor = Guid.NewGuid();
        var compiled = AuthorityCompiledEvaluationContract.Compile(
        [
            new AuthorityFactSnapshot(actor, "state", "canonical", "Base"),
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "OverlayRule",
                IsRuleBindingProjection: true)
        ]);
        var changed = compiled.CreateCommandSnapshot();
        var sibling = compiled.CreateCommandSnapshot();

        Assert.True(changed.TryApplyCommittedMutation(
            AuthorityMutation.Set(
                actor, "state", AuthorityObject.Canonical("Changed"),
                OntologyRuleResultLifetime.DurableState),
            Guid.NewGuid(), out var rejection), rejection);

        Assert.Equal(["Changed"], changed.GetValues(actor, "state"));
        Assert.Equal(["Base"], sibling.GetValues(actor, "state"));
        Assert.Equal(["Base"], compiled.GetValues(actor, "state"));
        Assert.Equal(["OverlayRule"], changed.GetValues(actor, "has_rule_block"));
        Assert.Equal(["OverlayRule"], sibling.GetValues(actor, "has_rule_block"));
    }

    [Fact]
    public void CommittedBooleanSetIsNormalizedAndVisibleToNextPostRule()
    {
        var actor = Guid.NewGuid();
        var binding = Guid.NewGuid();
        var snapshot = AuthorityEvaluationSnapshot.Create(
        [
            new AuthorityFactSnapshot(
                actor, "is_alive", "boolean", bool.TrueString),
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "DeathPostRule",
                IsRuleBindingProjection: true)
        ]);
        var mutation = AuthorityMutation.Set(
            actor,
            "is_alive",
            AuthorityObject.Boolean(false),
            OntologyRuleResultLifetime.DurableState);

        Assert.True(snapshot.TryApplyCommittedMutation(
            mutation, binding, out var overlayRejection));
        Assert.Empty(overlayRejection);
        Assert.Equal([bool.FalseString], snapshot.GetValues(actor, "is_alive"));

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            CreatePostTransport("DeathPostRule", "death_post_intent"),
            CreatePostRule(
                "DeathPostRule",
                "death_post_intent",
                [OntologyCondition.Fact("?actor", "is_alive", bool.FalseString)],
                OntologyEffect.SetFact("?actor", "death_seen", "True")),
            actor, actor, null, snapshot);

        Assert.True(result.Accepted);
    }

    [Fact]
    public void OrderedCommittedOverlayPreservesInverseAdjustAndRawProvenance()
    {
        var actor = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var next = Guid.NewGuid();
        var firstBinding = Guid.NewGuid();
        var secondBinding = Guid.NewGuid();
        var snapshot = AuthorityEvaluationSnapshot.Create(
        [
            new AuthorityFactSnapshot(actor, "owner", "entity", previous.ToString()),
            new AuthorityFactSnapshot(previous, "owned_by", "entity", actor.ToString()),
            new AuthorityFactSnapshot(actor, "score", "number", "10")
        ]);

        var sequence = new[]
        {
            AuthorityMutation.Retract(
                previous, "owned_by", AuthorityObject.Entity(actor)),
            AuthorityMutation.Set(
                actor, "owner", AuthorityObject.Entity(next)),
            AuthorityMutation.Set(
                next, "owned_by", AuthorityObject.Entity(actor)),
            AuthorityMutation.AdjustNumber(
                actor, "score", -3, 0, 10,
                OntologyRuleResultLifetime.DurableState)
        };
        foreach (var mutation in sequence)
        {
            Assert.True(snapshot.TryApplyCommittedMutation(
                mutation, firstBinding, out var rejection), rejection);
        }

        var probe = new OntologyActionEffectDefinition
        {
            actionVerb = "overlay_probe",
            objectPattern = "?actor",
            conditions =
            [
                OntologyCondition.Fact("?actor", "owner", next.ToString()),
                OntologyCondition.Fact(next.ToString(), "owned_by", "?actor"),
                OntologyCondition.NotFact(
                    previous.ToString(), "owned_by", "?actor"),
                OntologyCondition.Fact("?actor", "score", "7")
            ],
            effects =
            [
                OntologyEffect.SetFact("?actor", "overlay_probe", "Accepted")
            ]
        };
        Assert.True(AuthoritativeActionEvaluator.Evaluate(
            probe, actor, actor, null, snapshot).Accepted);

        // PostgreSQL permits the same semantic value to be owned by a distinct
        // Rule binding. The matcher still sees one truth, while numeric effect
        // evaluation must retain two raw rows and fail single-value lookup.
        Assert.True(snapshot.TryApplyCommittedMutation(
            AuthorityMutation.Set(
                actor, "score", AuthorityObject.Number(7),
                OntologyRuleResultLifetime.RuleBound),
            secondBinding,
            out var duplicateRejection), duplicateRejection);
        Assert.False(snapshot.TryGetSingleNumber(actor, "score", out _));

        Assert.True(snapshot.TryApplyCommittedMutation(
            AuthorityMutation.Retract(
                actor, "score", AuthorityObject.Number(7)),
            secondBinding,
            out var retractRejection), retractRejection);
        Assert.Empty(snapshot.GetValues(actor, "score"));
    }

    [Fact]
    public void FactRetractionCannotRemoveProjectedRuleBinding()
    {
        var actor = Guid.NewGuid();
        var snapshot = AuthorityEvaluationSnapshot.Create(
        [
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "ProjectedRule",
                IsRuleBindingProjection: true)
        ]);

        Assert.True(snapshot.TryApplyCommittedMutation(
            AuthorityMutation.Retract(
                actor, "has_rule_block", AuthorityObject.Canonical("ProjectedRule")),
            null,
            out var rejection), rejection);
        Assert.Equal(
            ["ProjectedRule"],
            snapshot.GetValues(actor, "has_rule_block"));
    }

    [Fact]
    public void NewRequestSnapshotAfterRuleRemovalFailsClosed()
    {
        var actor = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var transport = CreateRemovalTransport();
        var rule = CreateRemovalRule();
        var enabledFacts = new[]
        {
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "SnapshotRemovalRule"),
            new AuthorityFactSnapshot(
                actor, "has_concept", "canonical", "Actor")
        };
        var enabledSnapshot = AuthorityEvaluationSnapshot.Create(enabledFacts);
        var removedSnapshot = AuthorityEvaluationSnapshot.Create(
            enabledFacts.Where(fact => fact.PredicateId != "has_rule_block")
                .ToArray());

        var enabled = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport, rule, actor, actor, null, enabledSnapshot);
        var removed = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport, rule, actor, actor, null, removedSnapshot);

        Assert.True(enabled.Accepted);
        Assert.False(removed.Accepted);
        Assert.Equal("action_conditions_not_met", removed.RejectionCode);
        Assert.NotEqual(
            enabledSnapshot.RequestScopeId,
            removedSnapshot.RequestScopeId);
        Assert.Equal(2, enabledSnapshot.SourceFactCount);
        Assert.Equal(1, removedSnapshot.SourceFactCount);
    }

    [Fact]
    public void ConcurrentInvokedRuleReadsDoNotLeakEphemeralIntentOrMutations()
    {
        var actor = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var facts = Enumerable.Range(0, 1_000)
            .Select(index => new AuthorityFactSnapshot(
                Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
                $"snapshot_fact_{index:D4}",
                "canonical",
                "Present"))
            .Append(new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "SnapshotReadRule"))
            .Append(new AuthorityFactSnapshot(
                actor, "has_concept", "canonical", "Actor"))
            .ToArray();
        var snapshot = AuthorityEvaluationSnapshot.Create(facts);
        var transport = new OntologyActionEffectDefinition
        {
            actionVerb = "snapshot_read",
            objectPattern = "?actor",
            evaluationOnly = true,
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "SnapshotReadRule",
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "snapshot_read_intent",
                intentObjectPattern = "?actor"
            }
        };
        var rule = new OntologyRuleDefinition
        {
            id = "SnapshotReadRule",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", "snapshot_read_intent", "?actor"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", "SnapshotReadRule"),
                OntologyCondition.HasConcept("?actor", "Actor")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "snapshot_read_result", "Accepted")
            ]
        };
        var results = new ConcurrentBag<AuthoritativeActionEvaluation>();

        Parallel.For(0, 20, _ =>
            results.Add(AuthoritativeActionEvaluator.EvaluateInvokedRule(
                transport, rule, actor, actor, null, snapshot)));

        Assert.Equal(20, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Accepted);
            Assert.Single(result.Mutations);
        });

        var noIntentProbe = new OntologyActionEffectDefinition
        {
            actionVerb = "snapshot_clean_probe",
            objectPattern = "?actor",
            conditions =
            [
                OntologyCondition.NotFact(
                    "?actor", "snapshot_read_intent", "?actor")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "snapshot_clean", "True")
            ]
        };
        var clean = AuthoritativeActionEvaluator.Evaluate(
            noIntentProbe, actor, actor, null, snapshot);
        Assert.True(clean.Accepted);
        Assert.Single(clean.Mutations);
    }

    [Fact]
    public void RequestLocalIntentBindsRuleVariableIntoEquivalentResult()
    {
        var actor = Guid.Parse("00000000-0000-0000-0000-000000000301");
        var target = Guid.Parse("00000000-0000-0000-0000-000000000302");
        var snapshot = AuthorityEvaluationSnapshot.Create(
        [
            new AuthorityFactSnapshot(
                actor, "has_rule_block", "canonical", "OverlayBindingRule"),
            new AuthorityFactSnapshot(
                actor, "has_concept", "canonical", "Actor")
        ]);
        var transport = new OntologyActionEffectDefinition
        {
            actionVerb = "overlay_binding",
            objectPattern = "?target",
            evaluationOnly = true,
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "OverlayBindingRule",
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "overlay_binding_intent",
                intentObjectPattern = "?target"
            }
        };
        var rule = new OntologyRuleDefinition
        {
            id = "OverlayBindingRule",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", "overlay_binding_intent", "?recipient"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", "OverlayBindingRule"),
                OntologyCondition.HasConcept("?actor", "Actor")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "overlay_binding_result", "?recipient")
            ]
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport, rule, actor, target, null, snapshot);

        Assert.True(result.Accepted);
        var mutation = Assert.Single(result.Mutations);
        Assert.Equal(actor, mutation.SubjectEntityId);
        Assert.Equal("overlay_binding_result", mutation.PredicateId);
        Assert.Equal(target, mutation.Object?.EntityId);

        var noIntentProbe = new OntologyActionEffectDefinition
        {
            actionVerb = "overlay_binding_clean_probe",
            objectPattern = "?actor",
            conditions =
            [
                OntologyCondition.NotFact(
                    "?actor", "overlay_binding_intent", "?target")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "overlay_binding_clean", "True")
            ]
        };
        Assert.True(AuthoritativeActionEvaluator.Evaluate(
            noIntentProbe, actor, target, null, snapshot).Accepted);
    }

    private static OntologyActionEffectDefinition CreateRemovalTransport() =>
        new()
        {
            actionVerb = "snapshot_remove",
            objectPattern = "?actor",
            evaluationOnly = true,
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "SnapshotRemovalRule",
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "snapshot_remove_intent",
                intentObjectPattern = "?actor"
            }
        };

    private static OntologyActionEffectDefinition CreatePostTransport(
        string ruleId,
        string intentPredicate) =>
        new()
        {
            actionVerb = "post_chain",
            objectPattern = "?actor",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = ruleId,
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = intentPredicate,
                intentObjectPattern = "?actor"
            }
        };

    private static OntologyRuleDefinition CreatePostRule(
        string ruleId,
        string intentPredicate,
        IReadOnlyList<OntologyCondition> additionalConditions,
        OntologyEffect effect) =>
        new()
        {
            id = ruleId,
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", intentPredicate, "?actor"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", ruleId),
                .. additionalConditions
            ],
            effects = [effect]
        };

    private static OntologyRuleDefinition CreateRemovalRule() =>
        new()
        {
            id = "SnapshotRemovalRule",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor", "snapshot_remove_intent", "?actor"),
                OntologyCondition.Fact(
                    "?actor", "has_rule_block", "SnapshotRemovalRule"),
                OntologyCondition.HasConcept("?actor", "Actor")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?actor", "snapshot_remove_result", "Accepted")
            ]
        };
}
