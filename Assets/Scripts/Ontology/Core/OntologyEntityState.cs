using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyEntityState
    {
        private readonly HashSet<OntologyId> concepts = new();

        public OntologyEntityState(OntologyId id)
        {
            Id = id;
        }

        public OntologyId Id { get; }

        public IEnumerable<OntologyId> Concepts => concepts;

        public bool AddConcept(OntologyId concept)
        {
            return !concept.IsEmpty && concepts.Add(concept);
        }

        public bool HasConcept(OntologyId concept)
        {
            return concepts.Contains(concept);
        }
    }
}
