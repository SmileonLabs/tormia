using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Groups authored fact contributors by stable ontology entity id while exposing
    /// one preferred live presentation object for runtime adapters.
    /// </summary>
    public sealed class OntologyEntityRegistry
    {
        private readonly Dictionary<string, OntologyObject> objectsById = new();
        private readonly Dictionary<string, List<OntologyObject>> contributorsById = new();
        private readonly HashSet<string> duplicateEntityIds = new();
        public IReadOnlyCollection<string> DuplicateEntityIds =>
            duplicateEntityIds;

        public void Rebuild(IEnumerable<OntologyObject> objects)
        {
            objectsById.Clear();
            contributorsById.Clear();
            duplicateEntityIds.Clear();
            if (objects == null)
            {
                return;
            }

            foreach (var ontologyObject in objects)
            {
                if (ontologyObject == null ||
                    string.IsNullOrWhiteSpace(ontologyObject.EntityId))
                {
                    continue;
                }

                var entityId = ontologyObject.EntityId;
                if (!contributorsById.TryGetValue(entityId, out var contributors))
                {
                    contributors = new List<OntologyObject>();
                    contributorsById.Add(entityId, contributors);
                }

                contributors.Add(ontologyObject);
                if (!objectsById.TryGetValue(entityId, out var current) ||
                    IsPreferredPresentation(ontologyObject, current))
                {
                    objectsById[entityId] = ontologyObject;
                }
            }

            foreach (var pair in contributorsById)
            {
                if (CountPlacedPresentations(pair.Value) > 1)
                {
                    duplicateEntityIds.Add(pair.Key);
                }
            }
        }

        public bool Register(OntologyObject ontologyObject)
        {
            if (ontologyObject == null ||
                string.IsNullOrWhiteSpace(ontologyObject.EntityId))
            {
                return false;
            }

            var entityId = ontologyObject.EntityId;
            if (!contributorsById.TryGetValue(entityId, out var contributors))
            {
                contributors = new List<OntologyObject>();
                contributorsById.Add(entityId, contributors);
            }

            if (contributors.Contains(ontologyObject))
            {
                return true;
            }

            if (ontologyObject.GetComponent<OntologyPlaceableInstance>() != null &&
                CountPlacedPresentations(contributors) > 0)
            {
                duplicateEntityIds.Add(entityId);
                return false;
            }

            contributors.Add(ontologyObject);
            if (!objectsById.TryGetValue(entityId, out var current) ||
                IsPreferredPresentation(ontologyObject, current))
            {
                objectsById[entityId] = ontologyObject;
            }

            return true;
        }

        public bool Contains(string entityId)
        {
            return !string.IsNullOrWhiteSpace(entityId) &&
                   contributorsById.TryGetValue(entityId, out var contributors) &&
                   contributors.Exists(candidate => candidate != null);
        }

        public bool TryGet(string entityId, out OntologyObject ontologyObject)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                ontologyObject = null;
                return false;
            }

            return objectsById.TryGetValue(entityId, out ontologyObject) &&
                   ontologyObject != null;
        }

        public IReadOnlyList<OntologyObject> GetContributors(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) ||
                !contributorsById.TryGetValue(entityId, out var contributors))
            {
                return System.Array.Empty<OntologyObject>();
            }

            return contributors;
        }

        public bool TryGetSingleWithConcept(
            string concept,
            out OntologyObject ontologyObject)
        {
            ontologyObject = null;
            foreach (var pair in contributorsById)
            {
                var hasConcept = false;
                foreach (var contributor in pair.Value)
                {
                    if (contributor != null &&
                        contributor.HasAuthoredConcept(concept))
                    {
                        hasConcept = true;
                        break;
                    }
                }

                if (!hasConcept ||
                    !objectsById.TryGetValue(pair.Key, out var candidate) ||
                    candidate == null)
                {
                    continue;
                }

                if (ontologyObject != null)
                {
                    ontologyObject = null;
                    return false;
                }

                ontologyObject = candidate;
            }

            return ontologyObject != null;
        }

        private static int CountPlacedPresentations(
            List<OntologyObject> contributors)
        {
            var count = 0;
            foreach (var contributor in contributors)
            {
                if (contributor != null &&
                    contributor.GetComponent<OntologyPlaceableInstance>() != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsPreferredPresentation(
            OntologyObject candidate,
            OntologyObject current)
        {
            if (candidate == null)
            {
                return false;
            }

            if (current == null)
            {
                return true;
            }

            var candidateIsPlaced =
                candidate.GetComponent<OntologyPlaceableInstance>() != null;
            var currentIsPlaced =
                current.GetComponent<OntologyPlaceableInstance>() != null;
            if (candidateIsPlaced != currentIsPlaced)
            {
                return candidateIsPlaced;
            }

            // Data-only ontology contributors normally have only a few components.
            // Prefer the richer GameObject as the runtime presentation without
            // coupling the registry to a particular actor or item type.
            return candidate.GetComponents<Component>().Length >
                   current.GetComponents<Component>().Length;
        }
    }
}
