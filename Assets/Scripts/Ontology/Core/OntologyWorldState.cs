using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyWorldState
    {
        private readonly Dictionary<OntologyId, OntologyEntityState> entities = new();
        private readonly HashSet<OntologyFact> facts = new();

        public IEnumerable<OntologyEntityState> Entities => entities.Values;
        public IEnumerable<OntologyFact> Facts => facts;

        public OntologyEntityState GetOrCreateEntity(OntologyId entityId)
        {
            if (!entities.TryGetValue(entityId, out var entity))
            {
                entity = new OntologyEntityState(entityId);
                entities.Add(entityId, entity);
            }

            return entity;
        }

        public bool AddConcept(OntologyId entityId, OntologyId concept)
        {
            var added = GetOrCreateEntity(entityId).AddConcept(concept);
            if (added)
            {
                AddFact(entityId, "has_concept", concept);
            }

            return added;
        }

        public bool HasConcept(OntologyId entityId, OntologyId concept)
        {
            return entities.TryGetValue(entityId, out var entity) && entity.HasConcept(concept);
        }

        public bool AddFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            if (!fact.IsValid)
            {
                return false;
            }

            var added = facts.Add(fact);
            if (fact.Predicate.Equals(OntologyPredicates.HasConcept))
            {
                GetOrCreateEntity(fact.Subject).AddConcept(fact.Object);
            }

            return added;
        }

        public bool RemoveFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            var removed = facts.Remove(fact);
            if (removed && fact.Predicate.Equals(OntologyPredicates.HasConcept) && entities.TryGetValue(fact.Subject, out var entity))
            {
                entity.RemoveConcept(fact.Object);
            }

            return removed;
        }

        public bool SetFact(OntologyId subject, OntologyId predicate, OntologyId obj, out bool added)
        {
            added = false;
            var expectedFact = new OntologyFact(subject, predicate, obj);
            if (!expectedFact.IsValid)
            {
                return false;
            }

            var hasExpectedFact = false;
            var hasDifferentFact = false;
            foreach (var fact in facts)
            {
                if (!fact.Subject.Equals(subject) || !fact.Predicate.Equals(predicate))
                {
                    continue;
                }

                if (fact.Object.Equals(obj))
                {
                    hasExpectedFact = true;
                }
                else
                {
                    hasDifferentFact = true;
                }
            }

            if (hasExpectedFact && !hasDifferentFact)
            {
                return false;
            }

            RemoveFacts(subject, predicate);
            added = AddFact(subject, predicate, obj);
            return added || hasExpectedFact || hasDifferentFact;
        }

        public int RemoveFacts(OntologyId subject, OntologyId predicate)
        {
            var removed = 0;
            var targets = new List<OntologyFact>();
            foreach (var fact in facts)
            {
                if (fact.Subject.Equals(subject) && fact.Predicate.Equals(predicate))
                {
                    targets.Add(fact);
                }
            }

            foreach (var fact in targets)
            {
                if (facts.Remove(fact))
                {
                    if (fact.Predicate.Equals(OntologyPredicates.HasConcept) && entities.TryGetValue(fact.Subject, out var entity))
                    {
                        entity.RemoveConcept(fact.Object);
                    }

                    removed++;
                }
            }

            return removed;
        }

        public bool AdjustNumberFact(OntologyId subject, OntologyId predicate, OntologyId delta)
        {
            if (!int.TryParse(delta.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var deltaValue))
            {
                return false;
            }

            foreach (var fact in facts)
            {
                if (!fact.Subject.Equals(subject) || !fact.Predicate.Equals(predicate))
                {
                    continue;
                }

                if (!int.TryParse(fact.Object.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentValue))
                {
                    return false;
                }

                var nextValue = currentValue + deltaValue;
                if (nextValue == currentValue)
                {
                    return false;
                }

                facts.Remove(fact);
                return AddFact(subject, predicate, nextValue.ToString(CultureInfo.InvariantCulture));
            }

            return false;
        }

        public bool HasFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            return facts.Contains(new OntologyFact(subject, predicate, obj));
        }

        public List<OntologyId> FindEntitiesWithConcept(OntologyId concept)
        {
            var results = new List<OntologyId>();
            foreach (var entity in entities.Values)
            {
                if (entity.HasConcept(concept))
                {
                    results.Add(entity.Id);
                }
            }

            return results;
        }

        public List<OntologyId> FindEntitiesWithConcepts(params OntologyId[] requiredConcepts)
        {
            var results = new List<OntologyId>();
            foreach (var entity in entities.Values)
            {
                var matches = true;
                foreach (var concept in requiredConcepts)
                {
                    if (!entity.HasConcept(concept))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    results.Add(entity.Id);
                }
            }

            return results;
        }

        public List<OntologyFact> FindFacts(OntologyId predicate)
        {
            var results = new List<OntologyFact>();
            foreach (var fact in facts)
            {
                if (fact.Predicate.Equals(predicate))
                {
                    results.Add(fact);
                }
            }

            return results;
        }

        public List<OntologyFact> FindFacts(OntologyId predicate, OntologyId obj)
        {
            var results = new List<OntologyFact>();
            foreach (var fact in facts)
            {
                if (fact.Predicate.Equals(predicate) && fact.Object.Equals(obj))
                {
                    results.Add(fact);
                }
            }

            return results;
        }

        public List<OntologyId> FindSubjects(OntologyId predicate, OntologyId obj)
        {
            var results = new List<OntologyId>();
            foreach (var fact in FindFacts(predicate, obj))
            {
                results.Add(fact.Subject);
            }

            return results;
        }

        public string DumpFacts()
        {
            var builder = new StringBuilder();
            foreach (var fact in facts)
            {
                builder.Append("[Fact] ");
                builder.AppendLine(fact.ToString());
            }

            return builder.ToString().TrimEnd();
        }

    }
}
