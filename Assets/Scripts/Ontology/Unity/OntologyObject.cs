using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyObject : MonoBehaviour
    {
        [SerializeField] private string entityId;
        [SerializeField] private string[] concepts = Array.Empty<string>();
        [SerializeField] private OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();

        public string EntityId => entityId == null ? string.Empty : entityId.Trim();
        public bool HasStableEntityId => !string.IsNullOrWhiteSpace(EntityId);
        public IReadOnlyList<string> Concepts => concepts;
        public IReadOnlyList<OntologyFactEntry> Facts => facts;

        public bool HasAuthoredConcept(string concept)
        {
            if (string.IsNullOrWhiteSpace(concept) || concepts == null)
            {
                return false;
            }

            foreach (var value in concepts)
            {
                if (value == concept)
                {
                    return true;
                }
            }

            return false;
        }

        public void ConfigureOntologyData(string id, string[] ontologyConcepts, OntologyFactEntry[] ontologyFacts)
        {
            entityId = id;
            concepts = ontologyConcepts ?? Array.Empty<string>();
            facts = ontologyFacts ?? Array.Empty<OntologyFactEntry>();
        }

        public void ReplaceFactsAndConcepts(IEnumerable<string> nextConcepts, IEnumerable<OntologyFactEntry> nextFacts)
        {
            concepts = nextConcepts == null ? Array.Empty<string>() : new List<string>(nextConcepts).ToArray();
            facts = nextFacts == null ? Array.Empty<OntologyFactEntry>() : new List<OntologyFactEntry>(nextFacts).ToArray();
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null)
            {
                return;
            }

            var id = EntityId;
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError(
                    "OntologyObject '" + gameObject.name +
                    "' has no stable entity id and was not projected into the ontology world.",
                    this);
                return;
            }
            world.GetOrCreateEntity(id);

            foreach (var concept in concepts)
            {
                world.AddConcept(
                    id,
                    OntologyLanguagePackService.CanonicalTerm(concept));
            }

            foreach (var fact in facts)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                var predicate =
                    OntologyLanguagePackService.CanonicalTerm(fact.predicate);
                world.AddFact(
                    id,
                    predicate,
                    OntologyLanguagePackService.CanonicalObjectForRelation(
                        predicate,
                        fact.obj));
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
