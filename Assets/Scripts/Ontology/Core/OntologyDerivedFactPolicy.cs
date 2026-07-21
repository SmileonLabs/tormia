using System.Collections.Generic;
using System.Linq;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Identifies facts produced by retractable AddFact rule effects. These facts are
    /// recomputed from source facts and active rule blocks, so they must never become
    /// persistent authored state during save/restore.
    /// </summary>
    public static class OntologyDerivedFactPolicy
    {
        public static bool IsDerived(
            OntologyFact fact,
            IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            if (definitions == null) return false;
            return definitions
                .Where(value => value?.effects != null)
                .SelectMany(value => value.effects)
                .Any(effect =>
                    effect != null &&
                    effect.kind.IsRetractableInference() &&
                    Matches(effect.subject, fact.Subject.Value) &&
                    Matches(effect.predicate, fact.Predicate.Value) &&
                    Matches(effect.obj, fact.Object.Value));
        }

        public static int RemoveFrom(
            OntologyWorldState world,
            IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            if (world == null || definitions == null) return 0;
            var derived = world.Facts
                .Where(value => IsDerived(value, definitions))
                .ToArray();
            var removed = 0;
            foreach (var fact in derived)
            {
                if (world.RemoveFact(
                        fact.Subject,
                        fact.Predicate,
                        fact.Object))
                {
                    removed++;
                }
            }

            return removed;
        }

        private static bool Matches(string pattern, string value)
        {
            return !string.IsNullOrWhiteSpace(pattern) &&
                   (pattern[0] == '?' || pattern == value);
        }
    }
}
