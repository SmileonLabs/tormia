using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyObject : MonoBehaviour
    {
        [SerializeField] private string entityId;
        [SerializeField] private string[] concepts = Array.Empty<string>();
        [SerializeField] private OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();

        public string EntityId => string.IsNullOrWhiteSpace(entityId) ? gameObject.name : entityId;

        public void ConfigureOntologyData(string id, string[] ontologyConcepts, OntologyFactEntry[] ontologyFacts)
        {
            entityId = id;
            concepts = ontologyConcepts ?? Array.Empty<string>();
            facts = ontologyFacts ?? Array.Empty<OntologyFactEntry>();
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null)
            {
                return;
            }

            var id = EntityId;
            world.GetOrCreateEntity(id);

            foreach (var concept in concepts)
            {
                world.AddConcept(id, concept);
            }

            foreach (var fact in facts)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                world.AddFact(id, fact.predicate, fact.obj);
            }
        }
    }

    [Serializable]
    public sealed class OntologyFactEntry
    {
        public string predicate;
        public string obj;
    }
}
