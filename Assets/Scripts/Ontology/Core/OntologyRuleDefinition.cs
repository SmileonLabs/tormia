using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyRuleDefinition
    {
        public string id;
        // Immutable server catalog version. A Rule Block binds to this version so
        // publishing an updated rule cannot silently rewrite an existing world.
        public int catalogVersion = 1;
        public string description;
        public List<OntologyCondition> conditions = new();
        public List<OntologyEffect> effects = new();
        // Optional non-durable result for a presentation-only Rule Block.
        // Authority may emit it after conditions pass, but it never becomes a
        // Fact or revisioned world event.
        public OntologyActionPresentationDefinition runtimePresentation =
            new();
    }

    [Serializable]
    public sealed class OntologyCondition
    {
        public OntologyConditionKind kind;
        public string subject;
        public string predicate;
        public string obj;

        public static OntologyCondition Fact(string subject, string predicate, string obj)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.Fact,
                subject = subject,
                predicate = predicate,
                obj = obj
            };
        }

        public static OntologyCondition NotFact(string subject, string predicate, string obj)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.NotFact,
                subject = subject,
                predicate = predicate,
                obj = obj
            };
        }

        public static OntologyCondition HasConcept(string subject, string concept)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.HasConcept,
                subject = subject,
                obj = concept
            };
        }

        public static OntologyCondition NotConcept(string subject, string concept)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.NotConcept,
                subject = subject,
                obj = concept
            };
        }

        public static OntologyCondition NotEqual(string subject, string obj)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.NotEqual,
                subject = subject,
                obj = obj
            };
        }

        /// <summary>
        /// Requires the target item's authored has_slot value to be unoccupied
        /// by another item whose equipped_by relation points at the actor.
        /// This is a generic ontology invariant: it does not inspect an item,
        /// prefab, mesh, or slot name.
        /// </summary>
        public static OntologyCondition EquipmentSlotAvailable(
            string actor,
            string target)
        {
            return new OntologyCondition
            {
                kind = OntologyConditionKind.EquipmentSlotAvailable,
                subject = actor,
                obj = target
            };
        }
    }

    public enum OntologyConditionKind
    {
        Fact,
        NotFact,
        HasConcept,
        NotConcept,
        NotEqual,
        EquipmentSlotAvailable
    }

    [Serializable]
    public sealed class OntologyEffect
    {
        public OntologyEffectKind kind;
        // Determines whether an accepted Rule Block owns the resulting Fact for
        // its whole lifetime or only authored the state transition that created
        // it. Removing a Rule Block retracts RuleBound results, but must not undo
        // DurableState history such as damage, death, respawn, or collected loot.
        public OntologyRuleResultLifetime resultLifetime =
            OntologyRuleResultLifetime.RuleBound;
        public string subject;
        public string predicate;
        public string obj;
        // Optional numeric bounds used by authoritative AdjustNumberFact effects.
        // They are data-owned constraints rather than predicate-specific server code.
        public string minimum;
        public string maximum;
        // Optional data source for a numeric adjustment. For example, an attack
        // can subtract the equipped tool's attack_damage fact without encoding a
        // weapon name or a damage constant in the action definition.
        public OntologyNumericFactSource valueFrom;
        // Optional inverse relation maintained atomically with SetFact. This is
        // data-owned relation metadata, not a predicate-specific server rule.
        public string inversePredicate = string.Empty;
        // Optional post-effect guard. Effects run in order, allowing a persistent
        // transition to depend on a number adjusted by an earlier effect.
        public OntologyNumericFactGuard when;

        public static OntologyEffect AddFact(string subject, string predicate, string obj)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.AddFact,
                subject = subject,
                predicate = predicate,
                obj = obj
            };
        }

        public static OntologyEffect RemoveFact(string subject, string predicate, string obj)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.RemoveFact,
                subject = subject,
                predicate = predicate,
                obj = obj
            };
        }

        public static OntologyEffect SetFact(
            string subject,
            string predicate,
            string obj,
            string inversePredicate = "",
            OntologyRuleResultLifetime resultLifetime =
                OntologyRuleResultLifetime.RuleBound)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.SetFact,
                resultLifetime = resultLifetime,
                subject = subject,
                predicate = predicate,
                obj = obj,
                inversePredicate = inversePredicate
            };
        }

        public static OntologyEffect AdjustNumberFact(
            string subject,
            string predicate,
            string delta,
            string minimum = null,
            string maximum = null,
            OntologyRuleResultLifetime resultLifetime =
                OntologyRuleResultLifetime.RuleBound)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.AdjustNumberFact,
                resultLifetime = resultLifetime,
                subject = subject,
                predicate = predicate,
                obj = delta,
                minimum = minimum,
                maximum = maximum
            };
        }
    }

    [Serializable]
    public sealed class OntologyNumericFactSource
    {
        public string subject;
        public string predicate;
        public long multiplier = 1;
    }

    [Serializable]
    public sealed class OntologyNumericFactGuard
    {
        public string subject;
        public string predicate;
        public OntologyNumericComparison comparison;
        public string value;
    }

    public enum OntologyNumericComparison
    {
        None,
        LessThan,
        LessThanOrEqual,
        Equal,
        GreaterThanOrEqual,
        GreaterThan
    }

    public enum OntologyEffectKind
    {
        // Derived relation. The simulation retracts and recomputes it whenever observations change.
        AddFact,
        // Explicitly withdraws a relation from the current world state.
        RemoveFact,
        // Persistent state transition. It is not part of the retractable inferred-fact set.
        SetFact,
        // Persistent numeric state transition.
        AdjustNumberFact
    }

    public enum OntologyRuleResultLifetime
    {
        // The Fact is a live contribution of the assigned Rule Block and is
        // retracted when that binding or its meaning package is removed.
        RuleBound,
        // The Fact records an accepted state transition. Rule removal prevents
        // future transitions but never rewinds this already committed state.
        DurableState
    }

    public static class OntologyEffectSemantics
    {
        public static bool IsRetractableInference(this OntologyEffectKind kind)
        {
            return kind == OntologyEffectKind.AddFact;
        }

        public static bool IsPersistentStateMutation(this OntologyEffectKind kind)
        {
            return kind == OntologyEffectKind.SetFact ||
                   kind == OntologyEffectKind.AdjustNumberFact;
        }

        public static string DisplayName(this OntologyEffectKind kind)
        {
            return kind switch
            {
                OntologyEffectKind.AddFact => "Infer Fact (recomputed)",
                OntologyEffectKind.RemoveFact => "Remove Fact",
                OntologyEffectKind.SetFact => "Set Persistent State",
                OntologyEffectKind.AdjustNumberFact => "Adjust Persistent Number",
                _ => kind.ToString()
            };
        }
    }
}
