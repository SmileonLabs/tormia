using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyEvent
    {
        public OntologyEvent(string eventType, string reason)
        {
            EventType = eventType;
            Reason = reason;
        }

        public string EventType { get; }
        public string Reason { get; }
        public List<OntologyFact> AddedFacts { get; } = new();
        public List<OntologyFact> RemovedFacts { get; } = new();
        public List<OntologyFact> SetFacts { get; } = new();
        public List<OntologyFact> AdjustedNumberFacts { get; } = new();

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Reason) ? EventType : $"{EventType}: {Reason}";
        }
    }
}
