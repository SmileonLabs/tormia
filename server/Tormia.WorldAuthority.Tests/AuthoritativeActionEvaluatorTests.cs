using Tormia.Ontology.Core;
using Xunit;

public sealed class AuthoritativeActionEvaluatorTests
{
    [Fact]
    public void GroundedRuntimeConstraintRejectsMissingOrNegativeObservation()
    {
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = OntologyActions.JumpAvatar,
            evaluationOnly = true,
            runtimeConstraints = new OntologyActionRuntimeConstraints
            {
                requiresGroundedObservation = true
            }
        };

        Assert.Equal(
            "grounded_observation_required",
            AuthorityRuntimeObservationPolicy.Validate(
                definition,
                null));
        Assert.Equal(
            "grounded_observation_required",
            AuthorityRuntimeObservationPolicy.Validate(
                definition,
                false));
        Assert.Null(
            AuthorityRuntimeObservationPolicy.Validate(
                definition,
                true));
    }

    [Fact]
    public void EquippedSwordAttackProducesRelationAndBoundedDamageMutation()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var definition = SwordAttackDefinition();
        var facts = new[]
        {
            EntityFact(sword, "equipped_by", actor),
            CanonicalFact(sword, "has_concept", "Sword"),
            CanonicalFact(sword, "grants_capability", "MeleeAttack"),
            NumberFact(sword, "attack_damage", 10),
            CanonicalFact(target, "has_concept", "Damageable"),
            CanonicalFact(target, "combat_disposition", "Hostile"),
            CanonicalFact(target, "is_alive", "True"),
            NumberFact(target, "current_health", 30)
        };

        var result = AuthoritativeActionEvaluator.Evaluate(
            definition, actor, target, sword, facts);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Mutations.Count);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Assert &&
            mutation.PredicateId == "attacks_with" &&
            mutation.Object?.EntityId == sword);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.AdjustNumber &&
            mutation.SubjectEntityId == target &&
            mutation.PredicateId == "current_health" &&
            mutation.Delta == -10 &&
            mutation.Minimum == 0);
    }

    [Fact]
    public void AttackWithoutEquippedToolIsRejectedWithoutMutations()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = new[]
        {
            CanonicalFact(sword, "has_concept", "Sword"),
            CanonicalFact(sword, "grants_capability", "MeleeAttack"),
            NumberFact(sword, "attack_damage", 10),
            CanonicalFact(target, "has_concept", "Damageable"),
            CanonicalFact(target, "combat_disposition", "Hostile"),
            CanonicalFact(target, "is_alive", "True"),
            NumberFact(target, "current_health", 30)
        };

        var result = AuthoritativeActionEvaluator.Evaluate(
            SwordAttackDefinition(), actor, target, sword, facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void RetractableInferenceEffectCannotBePublishedAsDurableActionMutation()
    {
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = "attack"
        };
        definition.effects.Add(OntologyEffect.AddFact("?actor", "state", "Attacking"));

        var valid = AuthoritativeActionEvaluator.IsSupportedDefinition(
            definition, out var rejectionCode);

        Assert.False(valid);
        Assert.Equal("invalid_action_effect", rejectionCode);
    }

    [Fact]
    public void CanonicalPresentationIntentIsAcceptedWithoutBecomingMutation()
    {
        var definition = SwordAttackDefinition();
        definition.presentation.actorAnimationIntent = "AttackLight";

        var valid = AuthoritativeActionEvaluator.IsSupportedDefinition(
            definition, out var rejectionCode);

        Assert.True(valid);
        Assert.Equal(string.Empty, rejectionCode);
    }

    [Fact]
    public void InvalidPresentationIntentIsRejected()
    {
        var definition = SwordAttackDefinition();
        definition.presentation.actorAnimationIntent = "Attack Light";

        var valid = AuthoritativeActionEvaluator.IsSupportedDefinition(
            definition, out var rejectionCode);

        Assert.False(valid);
        Assert.Equal("invalid_action_presentation_intent", rejectionCode);
    }

    [Fact]
    public void AttackAtZeroHealth_AppliesGuardedDeathAndLootTransitions()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var definition = SwordAttackDefinition();
        definition.effects.Add(GuardedSet(
            "?target", "is_alive", "False"));
        definition.effects.Add(GuardedSet(
            "?target", "loot_status", "Available"));

        var result = AuthoritativeActionEvaluator.Evaluate(
            definition,
            actor,
            target,
            sword,
            CombatFacts(actor, target, sword, health: 10));

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.PredicateId == "is_alive");
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.PredicateId == "loot_status");
    }

    [Fact]
    public void AttackAboveZeroHealth_SkipsGuardedDeathAndLootTransitions()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var definition = SwordAttackDefinition();
        definition.effects.Add(GuardedSet(
            "?target", "is_alive", "False"));

        var result = AuthoritativeActionEvaluator.Evaluate(
            definition,
            actor,
            target,
            sword,
            CombatFacts(actor, target, sword, health: 30));

        Assert.True(result.Accepted);
        Assert.DoesNotContain(result.Mutations, mutation =>
            mutation.PredicateId == "is_alive");
    }

    [Fact]
    public void DefeatLootPostRuleRequiresItsOwnTargetBinding()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var invocation = new OntologyActionRuleInvocationDefinition
        {
            ruleId = "LootBecomesAvailableOnDefeat",
            bindingVariable = "?target",
            bindingEntityPattern = "?target",
            intentSubjectPattern = "?target",
            intentPredicate = "defeat_resolved_intent",
            intentObjectPattern = "?target",
            required = false
        };
        var transport = new OntologyActionEffectDefinition
        {
            actionVerb = "attack",
            ruleInvocation = invocation
        };
        var rule = new OntologyRuleDefinition
        {
            id = "LootBecomesAvailableOnDefeat",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?target",
                    "defeat_resolved_intent",
                    "?target"),
                OntologyCondition.Fact(
                    "?target",
                    "has_rule_block",
                    "LootBecomesAvailableOnDefeat"),
                OntologyCondition.Fact(
                    "?target",
                    "is_alive",
                    "False"),
                OntologyCondition.Fact(
                    "?target",
                    "loot_item",
                    "?lootItem")
            ],
            effects =
            [
                OntologyEffect.SetFact(
                    "?target",
                    "loot_status",
                    "Available")
            ]
        };
        var complete = new[]
        {
            CanonicalFact(
                target,
                "has_rule_block",
                "LootBecomesAvailableOnDefeat"),
            CanonicalFact(target, "is_alive", "False"),
            CanonicalFact(
                target,
                "loot_item",
                "OntologyDataFragment")
        };

        var accepted = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport,
            rule,
            actor,
            target,
            null,
            complete);
        Assert.True(accepted.Accepted);
        Assert.Contains(
            accepted.Mutations,
            value => value.PredicateId == "loot_status");

        var removed = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport,
            rule,
            actor,
            target,
            null,
            complete.Where(value =>
                    value.PredicateId != "has_rule_block")
                .ToArray());
        Assert.False(removed.Accepted);
        Assert.Equal(
            "action_conditions_not_met",
            removed.RejectionCode);
        Assert.Empty(removed.Mutations);
    }

    [Fact]
    public void WeaponDamageFactControlsDamageWithoutActionDefinitionChange()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Where(fact => fact.PredicateId != "attack_damage")
            .Append(NumberFact(sword, "attack_damage", 7))
            .ToArray();

        var result = AuthoritativeActionEvaluator.Evaluate(
            SwordAttackDefinition(), actor, target, sword, facts);

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.AdjustNumber &&
            mutation.PredicateId == "current_health" &&
            mutation.Delta == -7);
    }

    [Fact]
    public void RemovingWeaponDamageFactRemovesAttackBehavior()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Where(fact => fact.PredicateId != "attack_damage")
            .ToArray();

        var result = AuthoritativeActionEvaluator.Evaluate(
            SwordAttackDefinition(), actor, target, sword, facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_numeric_source_missing", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void NonHostileDamageableTargetCannotBeAttacked()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Where(fact => fact.PredicateId != "combat_disposition")
            .ToArray();

        var result = AuthoritativeActionEvaluator.Evaluate(
            SwordAttackDefinition(), actor, target, sword, facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void EquipWeapon_CoexistsWithEquipmentInAnotherSlot()
    {
        var actor = Guid.NewGuid();
        var tube = Guid.NewGuid();
        var nextSword = Guid.NewGuid();
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            EntityFact(tube, "equipped_by", actor),
            CanonicalFact(tube, "has_slot", "Waist"),
            CanonicalFact(nextSword, "has_concept", "Item"),
            CanonicalFact(nextSword, "has_concept", "Weapon"),
            CanonicalFact(nextSword, "can_equip", "True"),
            CanonicalFact(nextSword, "pickup_behavior", "SelectThenCarry"),
            CanonicalFact(nextSword, "has_slot", "RightHand"),
            CanonicalFact(
                nextSword,
                "has_rule_block",
                "EquipItemOnInteractionIntent")
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            EquipWeaponDefinition(),
            EquipmentRuleDefinition(),
            actor,
            nextSword,
            null,
            facts);

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.SubjectEntityId == nextSword &&
            mutation.PredicateId == "equipped_by" &&
            mutation.Object?.EntityId == actor &&
            mutation.ResultLifetime ==
                OntologyRuleResultLifetime.RuleBound);
        Assert.DoesNotContain(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Retract &&
            mutation.SubjectEntityId == tube);
    }

    [Fact]
    public void EquipWeapon_IgnoresUnitySerializedEmptyOptionalEffectObjects()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var rule = EquipmentRuleDefinition();
        rule.effects[0].when = new OntologyNumericFactGuard();
        rule.effects[0].valueFrom = new OntologyNumericFactSource();
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(sword, "has_concept", "Item"),
            CanonicalFact(sword, "has_concept", "Weapon"),
            CanonicalFact(sword, "can_equip", "True"),
            CanonicalFact(sword, "pickup_behavior", "SelectThenCarry"),
            CanonicalFact(sword, "has_slot", "RightHand"),
            CanonicalFact(
                sword,
                "has_rule_block",
                "EquipItemOnInteractionIntent")
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            EquipWeaponDefinition(),
            rule,
            actor,
            sword,
            null,
            facts);

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.SubjectEntityId == sword &&
            mutation.PredicateId == "equipped_by" &&
            mutation.Object?.EntityId == actor);
    }

    [Fact]
    public void EquipWeapon_WhenSameSlotIsOccupiedIsRejected()
    {
        var actor = Guid.NewGuid();
        var previousSword = Guid.NewGuid();
        var nextSword = Guid.NewGuid();
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            EntityFact(previousSword, "equipped_by", actor),
            CanonicalFact(previousSword, "has_slot", "RightHand"),
            CanonicalFact(nextSword, "has_concept", "Item"),
            CanonicalFact(nextSword, "has_concept", "Weapon"),
            CanonicalFact(nextSword, "can_equip", "True"),
            CanonicalFact(nextSword, "pickup_behavior", "SelectThenCarry"),
            CanonicalFact(nextSword, "has_slot", "RightHand"),
            CanonicalFact(
                nextSword,
                "has_rule_block",
                "EquipItemOnInteractionIntent")
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            EquipWeaponDefinition(),
            EquipmentRuleDefinition(),
            actor,
            nextSword,
            null,
            facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void EquipWeapon_OccupiedByAnotherActorIsRejected()
    {
        var actor = Guid.NewGuid();
        var otherActor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(sword, "has_concept", "Item"),
            CanonicalFact(sword, "has_concept", "Weapon"),
            CanonicalFact(sword, "can_equip", "True"),
            CanonicalFact(sword, "pickup_behavior", "SelectThenCarry"),
            CanonicalFact(sword, "has_slot", "RightHand"),
            CanonicalFact(
                sword,
                "has_rule_block",
                "EquipItemOnInteractionIntent"),
            EntityFact(sword, "equipped_by", otherActor)
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            EquipWeaponDefinition(),
            EquipmentRuleDefinition(),
            actor,
            sword,
            null,
            facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void EquipWeapon_WithoutRuleBlockIsRejected()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            EquipWeaponDefinition(),
            EquipmentRuleDefinition(),
            actor,
            sword,
            null,
            new[]
            {
                CanonicalFact(actor, "has_concept", "Actor"),
                CanonicalFact(sword, "has_concept", "Item"),
                CanonicalFact(sword, "has_concept", "Weapon"),
                CanonicalFact(sword, "can_equip", "True"),
                CanonicalFact(sword, "pickup_behavior", "SelectThenCarry"),
                CanonicalFact(sword, "has_slot", "RightHand")
            });

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void EquipWeaponTransportCarriesIntentWithoutCreatingResult()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();

        var result = AuthoritativeActionEvaluator.Evaluate(
            EquipWeaponDefinition(),
            actor,
            sword,
            null,
            Array.Empty<AuthorityFactSnapshot>());

        Assert.True(result.Accepted);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void RuleInvocationCannotHideADirectEquipmentEffect()
    {
        var definition = EquipWeaponDefinition();
        definition.effects.Add(new OntologyEffect
        {
            kind = OntologyEffectKind.SetFact,
            subject = "?target",
            predicate = "equipped_by",
            obj = "?actor"
        });

        var supported = AuthoritativeActionEvaluator.IsSupportedDefinition(
            definition,
            out var rejectionCode);

        Assert.False(supported);
        Assert.Equal(
            "action_definition_mixes_rule_invocation_and_effects",
            rejectionCode);
    }

    [Fact]
    public void EquipWearable_WithRuleBlockCreatesItemOwnedRelation()
    {
        var actor = Guid.NewGuid();
        var tube = Guid.NewGuid();
        var result = AuthoritativeActionEvaluator.Evaluate(
            EquipWearableDefinition(),
            actor,
            tube,
            null,
            new[]
            {
                CanonicalFact(tube, "has_concept", "Wearable"),
                CanonicalFact(tube, "pickup_behavior", "SelectThenEquip"),
                CanonicalFact(tube, "has_slot", "Waist"),
                CanonicalFact(
                    tube,
                    "has_rule_block",
                    "AutoEquipNearbyWearable")
            });

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.SubjectEntityId == tube &&
            mutation.PredicateId == "equipped_by" &&
            mutation.Object?.EntityId == actor);
    }

    [Fact]
    public void EquipWearable_WithoutRuleBlockIsRejected()
    {
        var actor = Guid.NewGuid();
        var tube = Guid.NewGuid();
        var result = AuthoritativeActionEvaluator.Evaluate(
            EquipWearableDefinition(),
            actor,
            tube,
            null,
            new[]
            {
                CanonicalFact(tube, "has_concept", "Wearable"),
                CanonicalFact(tube, "pickup_behavior", "SelectThenEquip"),
                CanonicalFact(tube, "has_slot", "Waist")
            });

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void UnequipRuleBlock_RemovesForwardAndInverseRelations()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var transport = new OntologyActionEffectDefinition
        {
            actionVerb = "unequip_equipment",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "UnequipItemOnInteractionIntent",
                bindingVariable = "?target",
                bindingEntityPattern = "?target",
                intentSubjectPattern = "?actor",
                intentPredicate = "unequip_intent",
                intentObjectPattern = "?target"
            }
        };
        var rule = UnequipRuleDefinition();
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(sword, "has_concept", "Item"),
            CanonicalFact(
                sword,
                "has_rule_block",
                "UnequipItemOnInteractionIntent"),
            EntityFact(sword, "equipped_by", actor),
            EntityFact(actor, "equipped_item", sword)
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport,
            rule,
            actor,
            sword,
            null,
            facts);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Mutations.Count);
        Assert.All(
            result.Mutations,
            mutation => Assert.Equal(
                AuthorityMutationKind.Retract,
                mutation.Kind));
    }

    [Fact]
    public void RemovingUnequipRuleBlockRemovesUnequipBehavior()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var transport = new OntologyActionEffectDefinition
        {
            actionVerb = "unequip_equipment",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "UnequipItemOnInteractionIntent",
                bindingVariable = "?target",
                bindingEntityPattern = "?target",
                intentSubjectPattern = "?actor",
                intentPredicate = "unequip_intent",
                intentObjectPattern = "?target"
            }
        };
        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport,
            UnequipRuleDefinition(),
            actor,
            sword,
            null,
            [
                CanonicalFact(actor, "has_concept", "Actor"),
                CanonicalFact(sword, "has_concept", "Item"),
                EntityFact(sword, "equipped_by", actor),
                EntityFact(actor, "equipped_item", sword)
            ]);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void RuntimeDistanceConstraint_RequiresAvailableNearbyPositions()
    {
        var definition = EquipWeaponDefinition();
        definition.runtimeConstraints.maxActorTargetDistance = 3d;

        Assert.Null(
            AuthoritativeActionRuntimeConstraintEvaluator.Validate(
                definition,
                new AuthoritySpatialPosition(0d, 0d, 0d),
                new AuthoritySpatialPosition(2d, 0d, 0d)));
        Assert.Equal(
            "action_target_out_of_range",
            AuthoritativeActionRuntimeConstraintEvaluator.Validate(
                definition,
                new AuthoritySpatialPosition(0d, 0d, 0d),
                new AuthoritySpatialPosition(4d, 0d, 0d)));
        Assert.Equal(
            "action_actor_runtime_position_unavailable",
            AuthoritativeActionRuntimeConstraintEvaluator.Validate(
                definition,
                null,
                new AuthoritySpatialPosition(1d, 0d, 0d)));
    }

    [Fact]
    public void PrimaryAttackRuleBindsToToolAndOwnsDamage()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var transport = PrimaryAttackTransportDefinition();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Append(CanonicalFact(actor, "has_concept", "Actor"))
            .Append(CanonicalFact(
                sword,
                "has_rule_block",
                "MeleeAttackOnPrimaryIntent"))
            .Append(CanonicalFact(sword, "has_concept", "Weapon"))
            .ToArray();

        Assert.True(
            AuthoritativeActionEvaluator.TryResolveRuleBindingEntity(
                transport,
                actor,
                target,
                sword,
                out var bindingEntity));
        Assert.Equal(sword, bindingEntity);

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            transport,
            PrimaryAttackRuleDefinition(),
            actor,
            target,
            sword,
            facts);

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.AdjustNumber &&
            mutation.SubjectEntityId == target &&
            mutation.PredicateId == "current_health" &&
            mutation.Delta == -10 &&
            mutation.ResultLifetime ==
                OntologyRuleResultLifetime.DurableState);
    }

    [Fact]
    public void PresentationOnlySwingRuleAcceptsWithoutDurableMutation()
    {
        var actor = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = "swing_weapon",
            requiresTool = true,
            ruleInvocation =
                new OntologyActionRuleInvocationDefinition
                {
                    ruleId = "SwingWeaponOnPrimaryIntent",
                    bindingVariable = "?tool",
                    bindingEntityPattern = "?tool",
                    intentSubjectPattern = "?actor",
                    intentPredicate = "primary_swing_intent",
                    intentObjectPattern = "?actor"
                }
        };
        var rule = new OntologyRuleDefinition
        {
            id = "SwingWeaponOnPrimaryIntent",
            catalogVersion = 1,
            runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent = "AttackLight"
                },
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "primary_swing_intent",
                    "?actor"),
                OntologyCondition.HasConcept("?actor", "Actor"),
                OntologyCondition.HasConcept("?tool", "Weapon"),
                OntologyCondition.Fact(
                    "?tool",
                    "has_rule_block",
                    "SwingWeaponOnPrimaryIntent"),
                OntologyCondition.Fact(
                    "?tool",
                    "equipped_by",
                    "?actor"),
                OntologyCondition.Fact(
                    "?tool",
                    "grants_capability",
                    "MeleeAttack")
            ],
            effects = []
        };
        var facts = new[]
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(sword, "has_concept", "Weapon"),
            CanonicalFact(
                sword,
                "has_rule_block",
                "SwingWeaponOnPrimaryIntent"),
            EntityFact(sword, "equipped_by", actor),
            CanonicalFact(
                sword,
                "grants_capability",
                "MeleeAttack")
        };

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            definition,
            rule,
            actor,
            actor,
            sword,
            facts);

        Assert.True(result.Accepted);
        Assert.Empty(result.Mutations);
        Assert.Empty(OntologyRuleValidator.Validate([rule]));
    }

    [Fact]
    public void RemovingPrimaryAttackRuleBlockRemovesAttackBehavior()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Append(CanonicalFact(actor, "has_concept", "Actor"))
            .Append(CanonicalFact(sword, "has_concept", "Weapon"))
            .ToArray();

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PrimaryAttackTransportDefinition(),
            PrimaryAttackRuleDefinition(),
            actor,
            target,
            sword,
            facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void PlayerRespawnRuleRestoresMaximumHealthAndLife()
    {
        var actor = Guid.NewGuid();
        var facts = PlayerRespawnFacts(
            actor,
            includeRuleBlock: true);

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PlayerRespawnTransportDefinition(),
            PlayerRespawnRuleDefinition(),
            actor,
            actor,
            null,
            facts);

        Assert.True(result.Accepted);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.AdjustNumber &&
            mutation.SubjectEntityId == actor &&
            mutation.PredicateId == "current_health" &&
            mutation.Delta == 100 &&
            mutation.ResultLifetime ==
                OntologyRuleResultLifetime.DurableState);
        Assert.Contains(result.Mutations, mutation =>
            mutation.Kind == AuthorityMutationKind.Set &&
            mutation.SubjectEntityId == actor &&
            mutation.PredicateId == "is_alive" &&
            mutation.Object?.Kind == "boolean" &&
            mutation.Object.ValueJson == "true" &&
            mutation.ResultLifetime ==
                OntologyRuleResultLifetime.DurableState);
    }

    [Fact]
    public void RemovingPlayerRespawnRuleBlockRemovesRespawnBehavior()
    {
        var actor = Guid.NewGuid();

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PlayerRespawnTransportDefinition(),
            PlayerRespawnRuleDefinition(),
            actor,
            actor,
            null,
            PlayerRespawnFacts(
                actor,
                includeRuleBlock: false));

        Assert.False(result.Accepted);
        Assert.Equal(
            "action_conditions_not_met",
            result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void PlayerLocomotionRuleAcceptsCompleteEphemeralContract()
    {
        var actor = Guid.NewGuid();

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PlayerLocomotionTransportDefinition(),
            PlayerLocomotionRuleDefinition(),
            actor,
            actor,
            null,
            PlayerLocomotionFacts(
                actor,
                includeRuleBlock: true));

        Assert.True(result.Accepted);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void RemovingPlayerLocomotionRuleBlockRemovesMovementBehavior()
    {
        var actor = Guid.NewGuid();

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PlayerLocomotionTransportDefinition(),
            PlayerLocomotionRuleDefinition(),
            actor,
            actor,
            null,
            PlayerLocomotionFacts(
                actor,
                includeRuleBlock: false));

        Assert.False(result.Accepted);
        Assert.Equal(
            "action_conditions_not_met",
            result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Theory]
    [InlineData("Neutral", "True")]
    [InlineData("Hostile", "False")]
    public void PrimaryAttackRejectsFriendlyOrDefeatedTarget(
        string disposition,
        string alive)
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var facts = CombatFacts(actor, target, sword, health: 30)
            .Where(fact =>
                fact.PredicateId != "combat_disposition" &&
                fact.PredicateId != "is_alive")
            .Append(CanonicalFact(actor, "has_concept", "Actor"))
            .Append(CanonicalFact(sword, "has_concept", "Weapon"))
            .Append(CanonicalFact(
                sword,
                "has_rule_block",
                "MeleeAttackOnPrimaryIntent"))
            .Append(CanonicalFact(
                target,
                "combat_disposition",
                disposition))
            .Append(CanonicalFact(target, "is_alive", alive))
            .ToArray();

        var result = AuthoritativeActionEvaluator.EvaluateInvokedRule(
            PrimaryAttackTransportDefinition(),
            PrimaryAttackRuleDefinition(),
            actor,
            target,
            sword,
            facts);

        Assert.False(result.Accepted);
        Assert.Equal("action_conditions_not_met", result.RejectionCode);
        Assert.Empty(result.Mutations);
    }

    [Fact]
    public void AttackRangeAndCooldownResolveFromToolFacts()
    {
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var sword = Guid.NewGuid();
        var definition = PrimaryAttackTransportDefinition();
        var facts = new[]
        {
            NumberFact(sword, "attack_range", 3),
            NumberFact(sword, "attack_cooldown", 1)
        };

        var nearby = AuthoritativeActionRuntimeConstraintEvaluator.Validate(
            definition,
            new AuthoritySpatialPosition(0d, 0d, 0d),
            new AuthoritySpatialPosition(2d, 0d, 0d),
            actor,
            target,
            sword,
            facts,
            out var cooldown);
        var distant = AuthoritativeActionRuntimeConstraintEvaluator.Validate(
            definition,
            new AuthoritySpatialPosition(0d, 0d, 0d),
            new AuthoritySpatialPosition(4d, 0d, 0d),
            actor,
            target,
            sword,
            facts,
            out _);

        Assert.Null(nearby);
        Assert.Equal(TimeSpan.FromSeconds(1), cooldown);
        Assert.Equal("action_target_out_of_range", distant);
    }

    [Fact]
    public async Task AttackCooldownRuntimeRejectsImmediateDuplicate()
    {
        var registry =
            new InMemoryWorldActionCooldownRuntimeRegistry();
        var world = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var tool = Guid.NewGuid();

        Assert.True(await registry.TryAcquire(
            world,
            actor,
            tool,
            "attack",
            TimeSpan.FromSeconds(10),
            CancellationToken.None));
        Assert.False(await registry.TryAcquire(
            world,
            actor,
            tool,
            "attack",
            TimeSpan.FromSeconds(10),
            CancellationToken.None));
    }

    private static OntologyActionEffectDefinition SwordAttackDefinition()
    {
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = "attack",
            predicate = "attacks_with",
            objectPattern = "?tool",
            requiresTool = true
        };
        definition.conditions.Add(OntologyCondition.Fact("?tool", "equipped_by", "?actor"));
        definition.conditions.Add(OntologyCondition.HasConcept("?tool", "Sword"));
        definition.conditions.Add(OntologyCondition.Fact("?tool", "grants_capability", "MeleeAttack"));
        definition.conditions.Add(OntologyCondition.HasConcept("?target", "Damageable"));
        definition.conditions.Add(OntologyCondition.Fact(
            "?target", "combat_disposition", "Hostile"));
        definition.conditions.Add(OntologyCondition.Fact("?target", "is_alive", "True"));
        var damage = OntologyEffect.AdjustNumberFact(
            "?target", "current_health", "0", "0");
        damage.valueFrom = new OntologyNumericFactSource
        {
            subject = "?tool",
            predicate = "attack_damage",
            multiplier = -1
        };
        definition.effects.Add(damage);
        return definition;
    }

    private static OntologyActionEffectDefinition
        PlayerRespawnTransportDefinition() =>
        new()
        {
            actionVerb = "respawn_avatar",
            objectPattern = "?actor",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "RespawnPlayerOnDeath",
                bindingVariable = "?actor",
                bindingEntityPattern = "?actor",
                intentSubjectPattern = "?actor",
                intentPredicate = "respawn_intent",
                intentObjectPattern = "?actor"
            }
        };

    private static OntologyRuleDefinition
        PlayerRespawnRuleDefinition()
    {
        var rule = new OntologyRuleDefinition
        {
            id = "RespawnPlayerOnDeath",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "respawn_intent",
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "Actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "PlayerControlled"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "Damageable"),
                OntologyCondition.Fact(
                    "?actor",
                    "has_rule_block",
                    "RespawnPlayerOnDeath"),
                OntologyCondition.Fact(
                    "?actor",
                    "is_alive",
                    "False"),
                OntologyCondition.Fact(
                    "?actor",
                    "current_health",
                    "0"),
                OntologyCondition.Fact(
                    "?actor",
                    "maximum_health",
                    "?maximumHealth")
            ]
        };
        var health = OntologyEffect.AdjustNumberFact(
            "?actor",
            "current_health",
            "0",
            "0",
            null,
            OntologyRuleResultLifetime.DurableState);
        health.valueFrom = new OntologyNumericFactSource
        {
            subject = "?actor",
            predicate = "maximum_health"
        };
        var revive = OntologyEffect.SetFact(
            "?actor",
            "is_alive",
            "True",
            resultLifetime:
                OntologyRuleResultLifetime.DurableState);
        revive.when = new OntologyNumericFactGuard
        {
            subject = "?actor",
            predicate = "current_health",
            comparison = OntologyNumericComparison.GreaterThan,
            value = "0"
        };
        rule.effects = [health, revive];
        return rule;
    }

    private static AuthorityFactSnapshot[] PlayerRespawnFacts(
        Guid actor,
        bool includeRuleBlock)
    {
        var facts = new List<AuthorityFactSnapshot>
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(
                actor,
                "has_concept",
                "PlayerControlled"),
            CanonicalFact(
                actor,
                "has_concept",
                "Damageable"),
            CanonicalFact(actor, "is_alive", "False"),
            NumberFact(actor, "current_health", 0),
            NumberFact(actor, "maximum_health", 100)
        };
        if (includeRuleBlock)
        {
            facts.Add(CanonicalFact(
                actor,
                "has_rule_block",
                "RespawnPlayerOnDeath"));
        }
        return facts.ToArray();
    }

    private static OntologyActionEffectDefinition
        PlayerLocomotionTransportDefinition() =>
        new()
        {
            actionVerb = "move_avatar",
            objectPattern = "?actor",
            ruleInvocation =
                new OntologyActionRuleInvocationDefinition
                {
                    ruleId = "MovePlayerFromIntent",
                    bindingVariable = "?actor",
                    bindingEntityPattern = "?actor",
                    intentSubjectPattern = "?actor",
                    intentPredicate = "locomotion_intent",
                    intentObjectPattern = "?actor"
                }
        };

    private static OntologyRuleDefinition
        PlayerLocomotionRuleDefinition() =>
        new()
        {
            id = "MovePlayerFromIntent",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "locomotion_intent",
                    "?actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "Actor"),
                OntologyCondition.HasConcept(
                    "?actor",
                    "PlayerControlled"),
                OntologyCondition.Fact(
                    "?actor",
                    "has_rule_block",
                    "MovePlayerFromIntent"),
                OntologyCondition.Fact(
                    "?actor",
                    "grants_capability",
                    "Locomotion"),
                OntologyCondition.Fact(
                    "?actor",
                    "physical_profile",
                    "AuthorityKinematic"),
                OntologyCondition.Fact(
                    "?actor",
                    "is_alive",
                    "True"),
                OntologyCondition.Fact(
                    "?actor",
                    "movement_speed",
                    "?speed")
            ],
            effects = [],
            runtimePresentation =
                new OntologyActionPresentationDefinition
                {
                    actorAnimationIntent = "Locomotion"
                }
        };

    private static AuthorityFactSnapshot[] PlayerLocomotionFacts(
        Guid actor,
        bool includeRuleBlock)
    {
        var facts = new List<AuthorityFactSnapshot>
        {
            CanonicalFact(actor, "has_concept", "Actor"),
            CanonicalFact(
                actor,
                "has_concept",
                "PlayerControlled"),
            CanonicalFact(
                actor,
                "grants_capability",
                "Locomotion"),
            CanonicalFact(
                actor,
                "physical_profile",
                "AuthorityKinematic"),
            CanonicalFact(actor, "is_alive", "True"),
            NumberFact(actor, "movement_speed", 5)
        };
        if (includeRuleBlock)
        {
            facts.Add(CanonicalFact(
                actor,
                "has_rule_block",
                "MovePlayerFromIntent"));
        }
        return facts.ToArray();
    }

    private static OntologyActionEffectDefinition EquipWeaponDefinition()
    {
        return new OntologyActionEffectDefinition
        {
            actionVerb = "equip_weapon",
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "EquipItemOnInteractionIntent",
                bindingVariable = "?target",
                intentSubjectPattern = "?actor",
                intentPredicate = "interaction_intent",
                intentObjectPattern = "?target"
            }
        };
    }

    private static OntologyActionEffectDefinition
        PrimaryAttackTransportDefinition()
    {
        return new OntologyActionEffectDefinition
        {
            actionVerb = "attack",
            requiresTool = true,
            presentation = new OntologyActionPresentationDefinition
            {
                actorAnimationIntent = "AttackLight"
            },
            ruleInvocation = new OntologyActionRuleInvocationDefinition
            {
                ruleId = "MeleeAttackOnPrimaryIntent",
                bindingVariable = "?tool",
                bindingEntityPattern = "?tool",
                intentSubjectPattern = "?actor",
                intentPredicate = "primary_attack_intent",
                intentObjectPattern = "?target"
            },
            runtimeConstraints = new OntologyActionRuntimeConstraints
            {
                maxActorTargetDistanceFrom =
                    new OntologyNumericFactSource
                    {
                        subject = "?tool",
                        predicate = "attack_range"
                    },
                cooldownSecondsFrom = new OntologyNumericFactSource
                {
                    subject = "?tool",
                    predicate = "attack_cooldown"
                }
            }
        };
    }

    private static OntologyRuleDefinition PrimaryAttackRuleDefinition()
    {
        var rule = new OntologyRuleDefinition
        {
            id = "MeleeAttackOnPrimaryIntent",
            catalogVersion = 1
        };
        rule.conditions.Add(OntologyCondition.Fact(
            "?actor", "primary_attack_intent", "?target"));
        rule.conditions.Add(OntologyCondition.HasConcept("?actor", "Actor"));
        rule.conditions.Add(OntologyCondition.HasConcept("?tool", "Weapon"));
        rule.conditions.Add(OntologyCondition.Fact(
            "?tool",
            "has_rule_block",
            "MeleeAttackOnPrimaryIntent"));
        rule.conditions.Add(OntologyCondition.Fact(
            "?tool", "equipped_by", "?actor"));
        rule.conditions.Add(OntologyCondition.Fact(
            "?tool", "grants_capability", "MeleeAttack"));
        rule.conditions.Add(OntologyCondition.HasConcept(
            "?target", "Damageable"));
        rule.conditions.Add(OntologyCondition.Fact(
            "?target", "combat_disposition", "Hostile"));
        rule.conditions.Add(OntologyCondition.Fact(
            "?target", "is_alive", "True"));
        var damage = OntologyEffect.AdjustNumberFact(
            "?target",
            "current_health",
            "0",
            "0",
            null,
            OntologyRuleResultLifetime.DurableState);
        damage.valueFrom = new OntologyNumericFactSource
        {
            subject = "?tool",
            predicate = "attack_damage",
            multiplier = -1
        };
        rule.effects.Add(damage);
        return rule;
    }

    private static OntologyRuleDefinition EquipmentRuleDefinition()
    {
        var definition = new OntologyRuleDefinition
        {
            id = "EquipItemOnInteractionIntent",
            catalogVersion = 1
        };
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?actor",
                "interaction_intent",
                "?target"));
        definition.conditions.Add(
            OntologyCondition.HasConcept("?actor", "Actor"));
        definition.conditions.Add(
            OntologyCondition.HasConcept("?target", "Item"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "has_rule_block",
                "EquipItemOnInteractionIntent"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "can_equip",
                "True"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "pickup_behavior",
                "SelectThenCarry"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "has_slot",
                "?slot"));
        definition.conditions.Add(
            OntologyCondition.EquipmentSlotAvailable(
                "?actor",
                "?target"));
        definition.conditions.Add(
            OntologyCondition.NotFact(
                "?target",
                "equipped_by",
                "?holder"));
        definition.effects.Add(
            OntologyEffect.SetFact(
                "?target",
                "equipped_by",
                "?actor"));
        return definition;
    }

    private static OntologyRuleDefinition UnequipRuleDefinition()
    {
        return new OntologyRuleDefinition
        {
            id = "UnequipItemOnInteractionIntent",
            catalogVersion = 1,
            conditions =
            [
                OntologyCondition.Fact(
                    "?actor",
                    "unequip_intent",
                    "?target"),
                OntologyCondition.HasConcept("?actor", "Actor"),
                OntologyCondition.HasConcept("?target", "Item"),
                OntologyCondition.Fact(
                    "?target",
                    "has_rule_block",
                    "UnequipItemOnInteractionIntent"),
                OntologyCondition.Fact(
                    "?target",
                    "equipped_by",
                    "?actor")
            ],
            effects =
            [
                OntologyEffect.RemoveFact(
                    "?target",
                    "equipped_by",
                    "?actor"),
                OntologyEffect.RemoveFact(
                    "?actor",
                    "equipped_item",
                    "?target")
            ]
        };
    }

    private static OntologyActionEffectDefinition EquipWearableDefinition()
    {
        var definition = new OntologyActionEffectDefinition
        {
            actionVerb = "equip_wearable"
        };
        definition.conditions.Add(
            OntologyCondition.HasConcept("?target", "Wearable"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "pickup_behavior",
                "SelectThenEquip"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "has_rule_block",
                "AutoEquipNearbyWearable"));
        definition.conditions.Add(
            OntologyCondition.Fact(
                "?target",
                "has_slot",
                "?slot"));
        definition.conditions.Add(
            OntologyCondition.EquipmentSlotAvailable(
                "?actor",
                "?target"));
        definition.conditions.Add(
            OntologyCondition.NotFact(
                "?target",
                "equipped_by",
                "?holder"));
        definition.effects.Add(
            OntologyEffect.SetFact(
                "?target",
                "equipped_by",
                "?actor"));
        return definition;
    }

    private static OntologyEffect GuardedSet(
        string subject,
        string predicate,
        string value) =>
        new()
        {
            kind = OntologyEffectKind.SetFact,
            subject = subject,
            predicate = predicate,
            obj = value,
            when = new OntologyNumericFactGuard
            {
                subject = subject,
                predicate = "current_health",
                comparison = OntologyNumericComparison.LessThanOrEqual,
                value = "0"
            }
        };

    private static AuthorityFactSnapshot[] CombatFacts(
        Guid actor,
        Guid target,
        Guid sword,
        long health) =>
        new[]
        {
            EntityFact(sword, "equipped_by", actor),
            CanonicalFact(sword, "has_concept", "Sword"),
            CanonicalFact(sword, "grants_capability", "MeleeAttack"),
            NumberFact(sword, "attack_damage", 10),
            CanonicalFact(target, "has_concept", "Damageable"),
            CanonicalFact(target, "combat_disposition", "Hostile"),
            CanonicalFact(target, "is_alive", "True"),
            NumberFact(target, "current_health", health)
        };

    private static AuthorityFactSnapshot EntityFact(Guid subject, string predicate, Guid value) =>
        new(subject, predicate, "entity", value.ToString());

    private static AuthorityFactSnapshot CanonicalFact(Guid subject, string predicate, string value) =>
        new(subject, predicate, "canonical", value);

    private static AuthorityFactSnapshot NumberFact(Guid subject, string predicate, long value) =>
        new(subject, predicate, "number", value.ToString());
}
