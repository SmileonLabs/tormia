using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
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
                bindings = ApplyCondition(world, condition, bindings);
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
            List<Dictionary<string, OntologyId>> inputBindings)
        {
            switch (condition.kind)
            {
                case OntologyConditionKind.NotFact:
                    return ApplyNotFactCondition(world, condition, inputBindings);
                case OntologyConditionKind.HasConcept:
                    return ApplyFactCondition(world, AsConceptFact(condition), inputBindings);
                case OntologyConditionKind.NotConcept:
                    return ApplyNotFactCondition(world, AsConceptFact(condition), inputBindings);
                case OntologyConditionKind.NotEqual:
                    return ApplyNotEqualCondition(condition, inputBindings);
                case OntologyConditionKind.EquipmentSlotAvailable:
                    return ApplyEquipmentSlotAvailableCondition(
                        world,
                        condition,
                        inputBindings);
                default:
                    return ApplyFactCondition(world, condition, inputBindings);
            }
        }

        private static List<Dictionary<string, OntologyId>>
            ApplyEquipmentSlotAvailableCondition(
                OntologyWorldState world,
                OntologyCondition condition,
                List<Dictionary<string, OntologyId>> inputBindings)
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
                foreach (var fact in world.GetFactsForPredicate(
                             OntologyPredicates.HasSlot))
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
                foreach (var ownerFact in world.GetFactsForPredicate(
                             OntologyPredicates.EquippedBy))
                {
                    if (!ownerFact.Object.Equals(actor) ||
                        ownerFact.Subject.Equals(target))
                    {
                        continue;
                    }

                    foreach (var slotFact in world.GetFactsForPredicate(
                                 OntologyPredicates.HasSlot))
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
            List<Dictionary<string, OntologyId>> inputBindings)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                foreach (var fact in GetCandidateFacts(world, condition, binding))
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
            List<Dictionary<string, OntologyId>> inputBindings)
        {
            var output = new List<Dictionary<string, OntologyId>>();
            foreach (var binding in inputBindings)
            {
                var found = false;
                foreach (var fact in GetCandidateFacts(world, condition, binding))
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
            Dictionary<string, OntologyId> binding)
        {
            if (world == null || condition == null)
            {
                return EmptyFacts;
            }

            if (TryResolveKnownValue(condition.predicate, binding, out var predicate))
            {
                return world.GetFactsForPredicate(predicate);
            }

            return world.Facts;
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
