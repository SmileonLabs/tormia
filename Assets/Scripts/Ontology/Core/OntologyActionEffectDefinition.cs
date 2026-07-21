using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyActionEffectDefinition
    {
        public string actionVerb;
        // Legacy single-effect fields are retained so existing action assets remain compatible.
        public string subjectPattern = "?actor";
        public string predicate;
        public string objectPattern = "?target";
        public bool requiresTool;
        public List<OntologyCondition> conditions = new();
        public List<OntologyEffect> effects = new();
    }
}
