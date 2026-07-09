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
                default:
                    return ApplyFactCondition(world, condition, inputBindings);
            }
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
                foreach (var fact in world.Facts)
                {
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
                var subject = Resolve(condition.subject, binding);
                var predicate = Resolve(condition.predicate, binding);
                var obj = Resolve(condition.obj, binding);
                if (!world.HasFact(subject, predicate, obj))
                {
                    output.Add(binding);
                }
            }

            return output;
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
            return OntologyCondition.Fact(condition.subject, "has_concept", condition.obj);
        }

        private static bool IsVariable(string value)
        {
            return !string.IsNullOrEmpty(value) && value[0] == '?';
        }
    }
}
