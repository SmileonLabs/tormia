using System;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyActionEffectDefinition
    {
        public string actionVerb;
        public string subjectPattern = "?actor";
        public string predicate;
        public string objectPattern = "?target";
        public bool requiresTool;
    }
}
