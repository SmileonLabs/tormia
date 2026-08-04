using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Read-only request overlay for an immutable ontology world. Implementations
    /// expose semantic additions and tombstones; they must not mutate the base.
    /// </summary>
    public interface IOntologyFactOverlay
    {
        IEnumerable<OntologyFact> GetAddedFacts();
        IEnumerable<OntologyFact> GetAddedFacts(OntologyId predicate);
        bool IsRemoved(OntologyFact fact);
    }

    public static class OntologyConditionMatcher
    {
        public static List<Dictionary<string, OntologyId>> Match(
            OntologyWorldState world,
            IReadOnlyList<OntologyCondition> conditions)
        {
            return Match(world, conditions, null);
        }

        public static List<Dictionary<string, OntologyId>> Match(
            OntologyWorldState world,
            IReadOnlyList<OntologyCondition> conditions,
            Dictionary<string, OntologyId> initialBinding)
        {
            return MatchCore(world, conditions, initialBinding, null);
        }

        /// <summary>
        /// Matches against an immutable base world plus one request-local Fact.
        /// The overlay participates in every Fact-backed condition without
        /// becoming a contribution owned by <paramref name="world"/>.
        /// </summary>
        public static List<Dictionary<string, OntologyId>> Match(
            OntologyWorldState world,
            IReadOnlyList<OntologyCondition> conditions,
            Dictionary<string, OntologyId> initialBinding,
            OntologyFact overlayFact)
        {
            return MatchCore(
                world,
                conditions,
                initialBinding,
                overlayFact.IsValid ? overlayFact : (OntologyFact?)null);
        }

        public static List<Dictionary<string, OntologyId>> Match(
            OntologyWorldState world,
            IReadOnlyList<OntologyCondition> conditions,
            Dictionary<string, OntologyId> initialBinding,
            IOntologyFactOverlay overlay)
        {
            return MatchCore(world, conditions, initialBinding, null, overlay);
        }

        private static List<Dictionary<string, OntologyId>> MatchCore(
            OntologyWorldState world,
            IReadOnlyList<OntologyCondition> conditions,
            Dictionary<string, OntologyId> initialBinding,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay = null)
        {
            var bindings = new List<Dictionary<string, OntologyId>>
            {
                initialBinding != null ? new Dictionary<string, OntologyId>(initialBinding) : new Dictionary<string, OntologyId>()
            };
            if (world == null || conditions == null)
            {
                return bindings;
            }

            foreach (var condition in conditions)
            {
                bindings = ApplyCondition(
                    world, condition, bindings, overlayFact, overlay);
                if (bindings.Count == 0)
                {
                    break;
                }
            }

            return bindings;
        }

        public static bool HasMatches(OntologyWorldState world, IReadOnlyList<OntologyCondition> conditions)
        {
            return Match(world, conditions).Count > 0;
        }

        public static OntologyId Resolve(string pattern, Dictionary<string, OntologyId> binding)
        {
            if (IsVariable(pattern) && binding != null && binding.TryGetValue(pattern, out var value))
            {
                return value;
            }

            if (binding != null && !string.IsNullOrWhiteSpace(pattern))
            {
                foreach (var pair in binding)
                {
                    pattern = pattern.Replace("{" + pair.Key.TrimStart('?') + "}", pair.Value.ToString());
                }
            }

            return new OntologyId(pattern);
        }

        private static List<Dictionary<string, OntologyId>> ApplyCondition(
            OntologyWorldState world,
            OntologyCondition condition,
            List<Dictionary<string, OntologyId>> inputBindings,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay)
        {
            switch (condition.kind)
            {
                case OntologyConditionKind.NotFact:
                    return ApplyNotFactCondition(
                        world, condition, inputBindings, overlayFact, overlay);
                case OntologyConditionKind.HasConcept:
                    return ApplyFactCondition(
                        world,
                        AsConceptFact(condition),
                        inputBindings,
                        overlayFact, overlay);
                case OntologyConditionKind.NotConcept:
                    return ApplyNotFactCondition(
                        world,
                        AsConceptFact(condition),
                        inputBindings,
                        overlayFact, overlay);
                case OntologyConditionKind.NotEqual:
                    return ApplyNotEqualCondition(condition, inputBindings);
                case OntologyConditionKind.EquipmentSlotAvailable:
                    return ApplyEquipmentSlotAvailableCondition(
                        world,
                        condition,
                        inputBindings,
                        overlayFact, overlay);
                default:
                    return ApplyFactCondition(
                        world, condition, inputBindings, overlayFact, overlay);
            }
        }

        private static List<Dictionary<string, OntologyId>>
            ApplyEquipmentSlotAvailableCondition(
                OntologyWorldState world,
                OntologyCondition condition,
                List<Dictionary<string, OntologyId>> inputBindings,
                OntologyFact? overlayFact,
                IOntologyFactOverlay overlay)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                var actor = Resolve(condition.subject, binding);
                var target = Resolve(condition.obj, binding);
                if (actor.IsEmpty || target.IsEmpty)
                {
                    continue;
                }

                OntologyId targetSlot = default;
                var slotCount = 0;
                foreach (var fact in GetFactsForPredicate(
                             world,
                             OntologyPredicates.HasSlot,
                             overlayFact, overlay))
                {
                    if (!fact.Subject.Equals(target))
                    {
                        continue;
                    }

                    if (slotCount == 0)
                    {
                        targetSlot = fact.Object;
                    }
                    else if (!targetSlot.Equals(fact.Object))
                    {
                        slotCount++;
                        break;
                    }

                    slotCount = 1;
                }

                if (slotCount != 1 || targetSlot.IsEmpty)
                {
                    continue;
                }

                var occupied = false;
                foreach (var ownerFact in GetFactsForPredicate(
                             world,
                             OntologyPredicates.EquippedBy,
                             overlayFact, overlay))
                {
                    if (!ownerFact.Object.Equals(actor) ||
                        ownerFact.Subject.Equals(target))
                    {
                        continue;
                    }

                    foreach (var slotFact in GetFactsForPredicate(
                                 world,
                                 OntologyPredicates.HasSlot,
                                 overlayFact, overlay))
                    {
                        if (slotFact.Subject.Equals(ownerFact.Subject) &&
                            slotFact.Object.Equals(targetSlot))
                        {
                            occupied = true;
                            break;
                        }
                    }

                    if (occupied)
                    {
                        break;
                    }
                }

                if (!occupied)
                {
                    output.Add(binding);
                }
            }

            return output;
        }

        private static List<Dictionary<string, OntologyId>> ApplyNotEqualCondition(
            OntologyCondition condition,
            List<Dictionary<string, OntologyId>> inputBindings)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                var left = Resolve(condition.subject, binding);
                var right = Resolve(condition.obj, binding);
                if (!left.Equals(right))
                {
                    output.Add(binding);
                }
            }

            return output;
        }

        private static List<Dictionary<string, OntologyId>> ApplyFactCondition(
            OntologyWorldState world,
            OntologyCondition condition,
            List<Dictionary<string, OntologyId>> inputBindings,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                foreach (var fact in GetCandidateFacts(
                             world, condition, binding, overlayFact, overlay))
                {
                    // Avoid allocating a new binding for every unrelated fact. Most rule
                    // conditions specify a predicate, so the world index plus this check
                    // discards non-matches before any dictionary copy is made.
                    if (!CanMatch(condition.subject, fact.Subject, binding) ||
                        !CanMatch(condition.predicate, fact.Predicate, binding) ||
                        !CanMatch(condition.obj, fact.Object, binding))
                    {
                        continue;
                    }

                    var next = new Dictionary<string, OntologyId>(binding);
                    if (TryMatch(condition.subject, fact.Subject, next)
                        && TryMatch(condition.predicate, fact.Predicate, next)
                        && TryMatch(condition.obj, fact.Object, next))
                    {
                        output.Add(next);
                    }
                }
            }

            return output;
        }

        private static List<Dictionary<string, OntologyId>> ApplyNotFactCondition(
            OntologyWorldState world,
            OntologyCondition condition,
            List<Dictionary<string, OntologyId>> inputBindings,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                var found = false;
                foreach (var fact in GetCandidateFacts(
                             world, condition, binding, overlayFact, overlay))
                {
                    if (MatchesNegativePattern(condition.subject, fact.Subject, binding) &&
                        MatchesNegativePattern(condition.predicate, fact.Predicate, binding) &&
                        MatchesNegativePattern(condition.obj, fact.Object, binding))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    output.Add(binding);
                }
            }

            return output;
        }

        private static IEnumerable<OntologyFact> GetCandidateFacts(
            OntologyWorldState world,
            OntologyCondition condition,
            Dictionary<string, OntologyId> binding,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay)
        {
            if (world == null || condition == null)
            {
                return EmptyFacts;
            }

            if (TryResolveKnownValue(condition.predicate, binding, out var predicate))
            {
                return GetFactsForPredicate(world, predicate, overlayFact, overlay);
            }

            return GetFacts(world, overlayFact, overlay);
        }

        private static IEnumerable<OntologyFact> GetFactsForPredicate(
            OntologyWorldState world,
            OntologyId predicate,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay = null)
        {
            var baseFacts = world.GetFactsForPredicate(predicate);
            if (overlay != null)
                return MergeOverlay(baseFacts, overlay.GetAddedFacts(predicate), overlay);
            if (!overlayFact.HasValue ||
                !overlayFact.Value.Predicate.Equals(predicate) ||
                world.HasFact(
                    overlayFact.Value.Subject,
                    overlayFact.Value.Predicate,
                    overlayFact.Value.Object))
            {
                return baseFacts;
            }

            return AppendOverlay(baseFacts, overlayFact.Value);
        }

        private static IEnumerable<OntologyFact> GetFacts(
            OntologyWorldState world,
            OntologyFact? overlayFact,
            IOntologyFactOverlay overlay)
        {
            if (overlay != null)
                return MergeOverlay(world.Facts, overlay.GetAddedFacts(), overlay);
            if (!overlayFact.HasValue ||
                world.HasFact(
                    overlayFact.Value.Subject,
                    overlayFact.Value.Predicate,
                    overlayFact.Value.Object))
            {
                return world.Facts;
            }

            return AppendOverlay(world.Facts, overlayFact.Value);
        }

        private static IEnumerable<OntologyFact> MergeOverlay(
            IEnumerable<OntologyFact> baseFacts,
            IEnumerable<OntologyFact> additions,
            IOntologyFactOverlay overlay)
        {
            var emitted = new HashSet<OntologyFact>();
            foreach (var fact in baseFacts)
                if (!overlay.IsRemoved(fact) && emitted.Add(fact)) yield return fact;
            foreach (var fact in additions)
                if (!overlay.IsRemoved(fact) && emitted.Add(fact)) yield return fact;
        }

        private static IEnumerable<OntologyFact> AppendOverlay(
            IEnumerable<OntologyFact> baseFacts,
            OntologyFact overlayFact)
        {
            foreach (var fact in baseFacts)
            {
                yield return fact;
            }

            yield return overlayFact;
        }

        private static bool CanMatch(
            string pattern,
            OntologyId value,
            Dictionary<string, OntologyId> binding)
        {
            if (IsVariable(pattern))
            {
                return !binding.TryGetValue(pattern, out var bound) || bound.Equals(value);
            }

            return Resolve(pattern, binding).Equals(value);
        }

        private static bool TryResolveKnownValue(
            string pattern,
            Dictionary<string, OntologyId> binding,
            out OntologyId value)
        {
            value = default;
            if (IsVariable(pattern))
            {
                return binding != null && binding.TryGetValue(pattern, out value);
            }

            value = Resolve(pattern, binding);
            return !value.IsEmpty;
        }

        private static bool MatchesNegativePattern(
            string pattern,
            OntologyId value,
            Dictionary<string, OntologyId> binding)
        {
            if (IsVariable(pattern))
            {
                return !binding.TryGetValue(pattern, out var bound) || bound.Equals(value);
            }

            return Resolve(pattern, binding).Equals(value);
        }

        private static bool TryMatch(string pattern, OntologyId value, Dictionary<string, OntologyId> binding)
        {
            if (!IsVariable(pattern))
            {
                return value.Equals(new OntologyId(pattern));
            }

            if (binding.TryGetValue(pattern, out var bound))
            {
                return bound.Equals(value);
            }

            binding[pattern] = value;
            return true;
        }

        private static OntologyCondition AsConceptFact(OntologyCondition condition)
        {
            return OntologyCondition.Fact(
                condition.subject,
                OntologyPredicates.HasConcept,
                condition.obj);
        }

        private static bool IsVariable(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '?';
        }

        private static readonly OntologyFact[] EmptyFacts = new OntologyFact[0];
    }
}
