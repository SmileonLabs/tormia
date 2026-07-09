using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyActionCandidateDefinition
    {
        public string actionVerb;
        public string targetPattern = "?target";
        public string toolPattern;
        public string labelFormat;
        public string reasonFormat;
        public List<OntologyCondition> conditions = new();
    }
}
