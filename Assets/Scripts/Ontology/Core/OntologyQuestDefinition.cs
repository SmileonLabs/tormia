using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyQuestDefinition
    {
        public string id;
        public string title;
        public string reasonFormat;
        public string hookPredicate;
        public string hookObject;
        public List<OntologyQuestGoalDefinition> goals = new();
    }

    [Serializable]
    public sealed class OntologyQuestGoalDefinition
    {
        public string sourcePredicate;
        public string sourceObject;
        public string actionVerb;
        public string completionPredicate;
        public string descriptionFormat;
        public string targetPattern = "?target";
        public List<OntologyCondition> conditions = new();
    }
}
