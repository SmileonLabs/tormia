using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyRuleDefinition
    {
        public string id;
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
        AddFact,
        RemoveFact,
        SetFact,
        AdjustNumberFact
    }
}
