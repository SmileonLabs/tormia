using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tormia.Ontology.Core
{
    public enum OntologyFactOrigin
    {
        Durable,
        Inferred,
        RuntimeObservation,
        AccountProfile,
        ActorProfile,
        CharacterAppearance
    }

    public sealed class OntologyWorldState
    {
        private readonly Dictionary<OntologyId, OntologyEntityState> entities = new();
        private readonly HashSet<OntologyFact> facts = new();
        private readonly Dictionary<OntologyFact, HashSet<OntologyFactOrigin>> factOrigins = new();
        // Predicate index keeps ontology semantics unchanged while avoiding a full fact scan
        // for the common "subject -> predicate -> object" rule condition.
        private readonly Dictionary<OntologyId, HashSet<OntologyFact>> factsByPredicate = new();
        private readonly HashSet<OntologyId> changedPredicates = new();

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
            return AddConceptContribution(entityId, concept, OntologyFactOrigin.Durable);
        }

        public bool AddConceptContribution(
            OntologyId entityId,
            OntologyId concept,
            OntologyFactOrigin origin)
        {
            GetOrCreateEntity(entityId);
            return AddFactContribution(entityId, OntologyPredicates.HasConcept, concept, origin);
        }

        public bool HasConcept(OntologyId entityId, OntologyId concept)
        {
            return entities.TryGetValue(entityId, out var entity) && entity.HasConcept(concept);
        }

        public bool AddFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            var existed = facts.Contains(fact);
            AddFactContribution(subject, predicate, obj, OntologyFactOrigin.Durable);
            return !existed && facts.Contains(fact);
        }

        public bool AddFactContribution(
            OntologyId subject,
            OntologyId predicate,
            OntologyId obj,
            OntologyFactOrigin origin)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            if (!fact.IsValid)
            {
                return false;
            }

            var added = facts.Add(fact);
            if (!factOrigins.TryGetValue(fact, out var origins))
            {
                origins = new HashSet<OntologyFactOrigin>();
                factOrigins.Add(fact, origins);
            }
            var contributionAdded = origins.Add(origin);
            if (added)
            {
                AddToPredicateIndex(fact);
                changedPredicates.Add(fact.Predicate);
            }

            if (fact.Predicate.Equals(OntologyPredicates.HasConcept))
            {
                GetOrCreateEntity(fact.Subject).AddConcept(fact.Object);
            }

            return contributionAdded;
        }

        public bool RemoveFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            var removed = facts.Remove(fact);
            if (removed)
            {
                factOrigins.Remove(fact);
                RemoveFromPredicateIndex(fact);
                changedPredicates.Add(fact.Predicate);
            }

            if (removed && fact.Predicate.Equals(OntologyPredicates.HasConcept) && entities.TryGetValue(fact.Subject, out var entity))
            {
                entity.RemoveConcept(fact.Object);
            }

            return removed;
        }

        public bool RemoveFactContribution(
            OntologyId subject,
            OntologyId predicate,
            OntologyId obj,
            OntologyFactOrigin origin)
        {
            var fact = new OntologyFact(subject, predicate, obj);
            if (!factOrigins.TryGetValue(fact, out var origins) || !origins.Remove(origin))
            {
                return false;
            }

            if (origins.Count > 0)
            {
                return true;
            }

            return RemoveFact(subject, predicate, obj);
        }

        public int RemoveFactContributions(
            OntologyId subject,
            OntologyFactOrigin origin,
            OntologyId predicate = default)
        {
            var targets = new List<OntologyFact>();
            foreach (var pair in factOrigins)
            {
                if (pair.Key.Subject.Equals(subject) &&
                    (predicate.IsEmpty || pair.Key.Predicate.Equals(predicate)) &&
                    pair.Value.Contains(origin))
                {
                    targets.Add(pair.Key);
                }
            }

            var removed = 0;
            foreach (var fact in targets)
            {
                if (RemoveFactContribution(fact.Subject, fact.Predicate, fact.Object, origin))
                {
                    removed++;
                }
            }
            return removed;
        }

        public bool IsPersistentFact(OntologyFact fact)
        {
            return factOrigins.TryGetValue(fact, out var origins) &&
                   origins.Contains(OntologyFactOrigin.Durable);
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
                if (RemoveFact(fact.Subject, fact.Predicate, fact.Object))
                {
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

                RemoveFact(fact.Subject, fact.Predicate, fact.Object);
                return AddFact(subject, predicate, nextValue.ToString(CultureInfo.InvariantCulture));
            }

            return false;
        }

        public bool HasFact(OntologyId subject, OntologyId predicate, OntologyId obj)
        {
            return facts.Contains(new OntologyFact(subject, predicate, obj));
        }

        public OntologyWorldChangeSet ConsumeChanges()
        {
            var changes = new OntologyWorldChangeSet(changedPredicates);
            changedPredicates.Clear();
            return changes;
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
            return factsByPredicate.TryGetValue(predicate, out var indexed)
                ? new List<OntologyFact>(indexed)
                : new List<OntologyFact>();
        }

        public List<OntologyFact> FindFacts(OntologyId predicate, OntologyId obj)
        {
            var results = new List<OntologyFact>();
            foreach (var fact in GetFactsForPredicate(predicate))
            {
                if (fact.Predicate.Equals(predicate) && fact.Object.Equals(obj))
                {
                    results.Add(fact);
                }
            }

            return results;
        }

        /// <summary>
        /// Returns the candidate facts for a known predicate without copying them. Consumers
        /// must not mutate the world while enumerating this collection.
        /// </summary>
        public IEnumerable<OntologyFact> GetFactsForPredicate(OntologyId predicate)
        {
            return factsByPredicate.TryGetValue(predicate, out var indexed)
                ? indexed
                : EmptyFacts;
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

        private static readonly OntologyFact[] EmptyFacts = new OntologyFact[0];

        private void AddToPredicateIndex(OntologyFact fact)
        {
            if (!factsByPredicate.TryGetValue(fact.Predicate, out var indexed))
            {
                indexed = new HashSet<OntologyFact>();
                factsByPredicate.Add(fact.Predicate, indexed);
            }

            indexed.Add(fact);
        }

        private void RemoveFromPredicateIndex(OntologyFact fact)
        {
            if (!factsByPredicate.TryGetValue(fact.Predicate, out var indexed))
            {
                return;
            }

            indexed.Remove(fact);
            if (indexed.Count == 0)
            {
                factsByPredicate.Remove(fact.Predicate);
            }
        }

    }

    public sealed class OntologyWorldChangeSet
    {
        private readonly HashSet<OntologyId> predicates;

        public OntologyWorldChangeSet(IEnumerable<OntologyId> predicates)
        {
            this.predicates = predicates != null
                ? new HashSet<OntologyId>(predicates)
                : new HashSet<OntologyId>();
        }

        public bool IsEmpty => predicates.Count == 0;
        public IEnumerable<OntologyId> Predicates => predicates;

        public bool ContainsPredicate(OntologyId predicate)
        {
            return predicates.Contains(predicate);
        }
    }
}
