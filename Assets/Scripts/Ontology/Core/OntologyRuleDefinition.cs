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
    }

    public enum OntologyConditionKind
    {
        Fact,
        NotFact,
        HasConcept,
        NotConcept,
        NotEqual
    }

    [Serializable]
    public sealed class OntologyEffect
    {
        public OntologyEffectKind kind;
        public string subject;
        public string predicate;
        public string obj;

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

        public static OntologyEffect SetFact(string subject, string predicate, string obj)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.SetFact,
                subject = subject,
                predicate = predicate,
                obj = obj
            };
        }

        public static OntologyEffect AdjustNumberFact(string subject, string predicate, string delta)
        {
            return new OntologyEffect
            {
                kind = OntologyEffectKind.AdjustNumberFact,
                subject = subject,
                predicate = predicate,
                obj = delta
            };
        }
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
