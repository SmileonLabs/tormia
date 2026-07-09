using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyRule
    {
        private readonly Func<OntologyWorldState, IReadOnlyList<OntologyEvent>> evaluate;

        public OntologyRule(string id, string description, Func<OntologyWorldState, IReadOnlyList<OntologyEvent>> evaluate)
        {
            Id = id;
            Description = description;
            this.evaluate = evaluate;
        }

        public string Id { get; }
        public string Description { get; }

        public IReadOnlyList<OntologyEvent> Evaluate(OntologyWorldState world)
        {
            return evaluate != null ? evaluate(world) : Array.Empty<OntologyEvent>();
        }
    }
}
