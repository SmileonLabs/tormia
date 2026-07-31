using Tormia.Ontology.Core;
using Xunit;

public sealed class AutonomousActorOntologyContractTests
{
    [Fact]
    public void ArbitraryEntityWithCompleteContractCanAttackPlayerFaction()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var definition = TransportDefinition();
        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            definition,
            RuleDefinition(),
            actor,
            target,
            null,
            Facts(actor, target, includeRuleBlock: true));

        Assert.True(result.Accepted);
        Assert.Contains(
            result.Mutations,
            mutation =>
                mutation.SubjectEntityId == target &&
                mutation.PredicateId == "current_health" &&
                mutation.Kind ==
                AuthorityMutationKind.AdjustNumber);
    }

    [Fact]
    public void RemovingRuleBlockRemovesAutonomousAttackBehavior()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            TransportDefinition(),
            RuleDefinition(),
            actor,
            target,
            null,
            Facts(actor, target, includeRuleBlock: false));

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
    }

    [Fact]
    public void TargetAndChaseExecuteTheirAssignedRuleDefinitions()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var facts = ControlFacts(actor, target);

        var targetAccepted =
            AuthoritativeActionEvaluator.EvaluateInvokedRule(
                ControlTransport(
                    "acquire_autonomous_target",
                    "AcquireNearestHostileTarget",
                    "autonomous_target_intent"),
                TargetRuleDefinition(),
                actor,
                target,
                null,
                facts);
        var chaseAccepted =
            AuthoritativeActionEvaluator.EvaluateInvokedRule(
                ControlTransport(
                    "chase_autonomous_target",
                    "ChaseTargetWithinLeash",
                    "autonomous_chase_intent"),
                ChaseRuleDefinition(),
                actor,
                target,
                null,
                facts);

        Assert.True(targetAccepted.Accepted, targetAccepted.RejectionCode);
        Assert.True(chaseAccepted.Accepted, chaseAccepted.RejectionCode);
        Assert.Empty(targetAccepted.Mutations);
        Assert.Empty(chaseAccepted.Mutations);

        var implicitNoEffectTransport = ControlTransport(
            "acquire_autonomous_target",
            "AcquireNearestHostileTarget",
            "autonomous_target_intent");
        implicitNoEffectTransport.evaluationOnly = false;
        var implicitNoEffect =
            AuthoritativeActionEvaluator.EvaluateInvokedRule(
                implicitNoEffectTransport,
                TargetRuleDefinition(),
                actor,
                target,
                null,
                facts);
        Assert.False(implicitNoEffect.Accepted);
        Assert.Equal(
            "unsupported_authoritative_rule_definition",
            implicitNoEffect.RejectionCode);

        var withoutTargetBlock = facts.Where(value =>
            value.PredicateId != "has_rule_block" ||
            value.ObjectValue !=
            "AcquireNearestHostileTarget").ToArray();
        var removed =
            AuthoritativeActionEvaluator.EvaluateInvokedRule(
                ControlTransport(
                    "acquire_autonomous_target",
                    "AcquireNearestHostileTarget",
                    "autonomous_target_intent"),
                TargetRuleDefinition(),
                actor,
                target,
                null,
                withoutTargetBlock);
        Assert.False(removed.Accepted);
        Assert.Equal("action_conditions_not_met", removed.RejectionCode);
    }

    [Fact]
    public void KinematicAdvanceIsBoundedByAuthoredSpeedStep()
    {
        var next = WorldAutonomousActorPolicy.AdvanceTowards(
            0d,
            0d,
            10d,
            0d,
            0.4d);

        Assert.Equal(0.4d, next.X, 6);
        Assert.Equal(0d, next.Z, 6);
    }

    [Fact]
    public void LiveRuntimeOwnerDoesNotFallBackToDurableSpawn()
    {
        var target = new WorldAutonomousTargetConfiguration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "world_main",
            10d,
            2d,
            20d,
            RequiresRuntimePosition: true);

        var resolved = WorldAutonomousTargetPositionPolicy.TryResolve(
            target,
            new Dictionary<Guid, WorldPlayerMotionState>(),
            new Dictionary<Guid, WorldAutonomousActorMotionState>(),
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void DurableStaticTargetCanUseAuthoredTransform()
    {
        var target = new WorldAutonomousTargetConfiguration(
            Guid.NewGuid(),
            null,
            "world_main",
            10d,
            2d,
            20d,
            RequiresRuntimePosition: false);

        var resolved = WorldAutonomousTargetPositionPolicy.TryResolve(
            target,
            new Dictionary<Guid, WorldPlayerMotionState>(),
            new Dictionary<Guid, WorldAutonomousActorMotionState>(),
            out var position);

        Assert.True(resolved);
        Assert.Equal(10d, position.PositionX);
        Assert.Equal(20d, position.PositionZ);
    }

    [Fact]
    public void ActorLosingOntologyEligibilityEvictsItsRuntimeMotion()
    {
        var livingActor = Guid.NewGuid();
        var deadActor = Guid.NewGuid();

        var removed =
            WorldAutonomousActorLifecyclePolicy.FindRemovedActorIds(
                [livingActor, deadActor, deadActor],
                [livingActor]);

        Assert.Equal([deadActor], removed);
    }

    private static IReadOnlyList<AuthorityFactSnapshot> Facts(
        Guid actor,
        Guid target,
        bool includeRuleBlock)
    {
        var facts = new List<AuthorityFactSnapshot>
        {
            Fact(actor, "has_concept", "Actor"),
            Fact(actor, "has_concept", "AutonomousAgent"),
            Fact(actor, "has_concept", "Damageable"),
            Fact(actor, "grants_capability", "NaturalMeleeAttack"),
            Fact(actor, "is_alive", "True"),
            Fact(actor, "hostile_to_faction", "PlayerFaction"),
            Fact(actor, "target_concept", "PlayerControlled"),
            Number(actor, "attack_damage", "5"),
            Number(actor, "attack_range", "2"),
            Number(actor, "attack_cooldown", "1"),
            Fact(target, "has_concept", "Actor"),
            Fact(target, "has_concept", "PlayerControlled"),
            Fact(target, "has_concept", "Damageable"),
            Fact(target, "is_alive", "True"),
            Fact(target, "belongs_to_faction", "PlayerFaction"),
            Number(target, "current_health", "100")
        };
        if (includeRuleBlock)
        {
            facts.Add(
                Fact(
                    actor,
                    "has_rule_block",
                    "AutonomousMeleeCombat"));
        }
        return facts;
    }

    private static AuthorityFactSnapshot Fact(
        Guid subject,
        string predicate,
        string value) =>
        new(subject, predicate, "canonical", value);

    private static AuthorityFactSnapshot Number(
        Guid subject,
        string predicate,
        string value) =>
        new(subject, predicate, "number", value);

    private static IReadOnlyList<AuthorityFactSnapshot> ControlFacts(
        Guid actor,
        Guid target) =>
    [
        Fact(actor, "has_concept", "Actor"),
        Fact(actor, "has_concept", "AutonomousAgent"),
        Fact(actor, "has_rule_block", "AcquireNearestHostileTarget"),
        Fact(actor, "has_rule_block", "ChaseTargetWithinLeash"),
        Fact(actor, "target_concept", "PlayerControlled"),
        Fact(
            actor,
            "targeting_profile",
            "NearestHostileWithinDetection"),
        Fact(actor, "chase_profile", "ChaseWithinLeash"),
        Fact(actor, "physical_profile", "AuthorityKinematic"),
        Fact(actor, "hostile_to_faction", "PlayerFaction"),
        Number(actor, "movement_speed", "2"),
        Number(actor, "detection_range", "12"),
        Number(actor, "leash_range", "18"),
        Fact(target, "has_concept", "Actor"),
        Fact(target, "has_concept", "PlayerControlled"),
        Fact(target, "has_concept", "Damageable"),
        Fact(target, "belongs_to_faction", "PlayerFaction"),
        Fact(target, "is_alive", "True")
    ];

    private static OntologyActionEffectDefinition ControlTransport(
        string action,
        string rule,
        string intent) =>
        new()
        {
            actionVerb = action,
            requiresTool = false,
            evaluationOnly = true,
            ruleInvocation =
                new OntologyActionRuleInvocationDefinition
                {
                    ruleId = rule,
                    bindingVariable = "?actor",
                    bindingEntityPattern = "?actor",
                    intentSubjectPattern = "?actor",
                    intentPredicate = intent,
                    intentObjectPattern = "?target"
                }
        };

    private static OntologyRuleDefinition TargetRuleDefinition() =>
        new()
        {
            id = "AcquireNearestHostileTarget",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "autonomous_target_intent",
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "AutonomousAgent"),
                OntologyCondition.Fact(
                    "?actor",
                    "has_rule_block",
                    "AcquireNearestHostileTarget"),
                OntologyCondition.Fact(
                    "?actor",
                    "target_concept",
                    "?targetConcept"),
                OntologyCondition.Fact(
                    "?actor",
                    "targeting_profile",
                    "NearestHostileWithinDetection"),
                OntologyCondition.HasConcept(
                    "?target",
                    "?targetConcept"),
                OntologyCondition.HasConcept(
                    "?target",
                    "Damageable"),
                OntologyCondition.Fact(
                    "?target",
                    "is_alive",
                    "True"),
                OntologyCondition.Fact(
                    "?target",
                    "belongs_to_faction",
                    "?targetFaction"),
                OntologyCondition.Fact(
                    "?actor",
                    "hostile_to_faction",
                    "?targetFaction"),
                OntologyCondition.NotEqual("?actor", "?target")
            ],
            effects = []
        };

    private static OntologyRuleDefinition ChaseRuleDefinition() =>
        new()
        {
            id = "ChaseTargetWithinLeash",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "autonomous_chase_intent",
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "AutonomousAgent"),
                OntologyCondition.Fact(
                    "?actor",
                    "has_rule_block",
                    "ChaseTargetWithinLeash"),
                OntologyCondition.Fact(
                    "?actor",
                    "chase_profile",
                    "ChaseWithinLeash"),
                OntologyCondition.Fact(
                    "?actor",
                    "physical_profile",
                    "AuthorityKinematic"),
                OntologyCondition.Fact(
                    "?actor",
                    "movement_speed",
                    "?speed"),
                OntologyCondition.Fact(
                    "?actor",
                    "detection_range",
                    "?detection"),
                OntologyCondition.Fact(
                    "?actor",
                    "leash_range",
                    "?leash")
            ],
            effects = []
        };

    private static OntologyActionEffectDefinition TransportDefinition()
    {
        return new OntologyActionEffectDefinition
        {
            actionVerb = "autonomous_melee_attack",
            requiresTool = false,
            ruleInvocation =
                new OntologyActionRuleInvocationDefinition
                {
                    ruleId = "AutonomousMeleeCombat",
                    bindingVariable = "?actor",
                    bindingEntityPattern = "?actor",
                    intentSubjectPattern = "?actor",
                    intentPredicate = "autonomous_attack_intent",
                    intentObjectPattern = "?target"
                },
            runtimeConstraints =
                new OntologyActionRuntimeConstraints
                {
                    maxActorTargetDistanceFrom =
                        new OntologyNumericFactSource
                        {
                            subject = "?actor",
                            predicate = "attack_range"
                        },
                    cooldownSecondsFrom =
                        new OntologyNumericFactSource
                        {
                            subject = "?actor",
                            predicate = "attack_cooldown"
                        }
                }
        };
    }

    private static OntologyRuleDefinition RuleDefinition()
    {
        var damage = OntologyEffect.AdjustNumberFact(
            "?target",
            "current_health",
            "0",
            "0");
        damage.valueFrom = new OntologyNumericFactSource
        {
            subject = "?actor",
            predicate = "attack_damage",
            multiplier = -1
        };
        return new OntologyRuleDefinition
        {
            id = "AutonomousMeleeCombat",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "autonomous_attack_intent",
                    "?target"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "AutonomousAgent"),
                OntologyCondition.Fact(
                    "?actor",
                    "has_rule_block",
                    "AutonomousMeleeCombat"),
                OntologyCondition.Fact(
                    "?actor",
                    "grants_capability",
                    "NaturalMeleeAttack"),
                OntologyCondition.Fact(
                    "?actor",
                    "is_alive",
                    "True"),
                OntologyCondition.HasConcept(
                    "?target",
                    "?targetConcept"),
                OntologyCondition.Fact(
                    "?actor",
                    "target_concept",
                    "?targetConcept"),
                OntologyCondition.HasConcept(
                    "?target",
                    "Damageable"),
                OntologyCondition.Fact(
                    "?target",
                    "is_alive",
                    "True"),
                OntologyCondition.Fact(
                    "?target",
                    "belongs_to_faction",
                    "?targetFaction"),
                OntologyCondition.Fact(
                    "?actor",
                    "hostile_to_faction",
                    "?targetFaction")
            ],
            effects = [damage],
            runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent = "MonsterAttack"
                }
        };
    }
}
