using System;
using System.Linq;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Resolves persistent equipment presentation from Authority-owned state.
    /// The resolver does not infer meaning from a prefab, mesh, or object name:
    /// the equipped entity must project exactly one authored animation intent.
    /// </summary>
    public static class OntologyEquipmentAnimationIntentResolver
    {
        public static bool TryResolveIdleIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            out string intent)
        {
            return TryResolveIntent(
                projection,
                actorEntityId,
                catalog,
                OntologyPredicates.IdleAnimationIntent,
                out intent);
        }

        public static bool TryResolveMoveIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            out string intent)
        {
            return TryResolveIntent(
                projection,
                actorEntityId,
                catalog,
                OntologyPredicates.MoveAnimationIntent,
                out intent);
        }

        private static bool TryResolveIntent(
            OntologyAuthorityWorldProjection projection,
            string actorEntityId,
            OntologyCombatCatalog catalog,
            string intentPredicate,
            out string intent)
        {
            intent = string.Empty;
            if (projection?.facts == null ||
                string.IsNullOrWhiteSpace(actorEntityId) ||
                string.IsNullOrWhiteSpace(intentPredicate))
            {
                return false;
            }

            string equippedEntityId = null;
            string resolvedIntent = null;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.EquippedBy,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fact.objectEntityId,
                        actorEntityId,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(fact.subjectEntityId))
                {
                    continue;
                }

                if (!TryResolveProjectedIntent(
                        projection,
                        fact.subjectEntityId,
                        intentPredicate,
                        out var candidateIntent))
                {
                    // Equipment without this authored presentation meaning
                    // can coexist without selecting a locomotion pose.
                    continue;
                }

                if (equippedEntityId != null &&
                    !string.Equals(
                        equippedEntityId,
                        fact.subjectEntityId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Two equipped weapon presentations are ambiguous even if
                    // other non-weapon equipment legitimately coexists.
                    return false;
                }

                equippedEntityId = fact.subjectEntityId;
                resolvedIntent = candidateIntent;
            }

            if (equippedEntityId == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(resolvedIntent))
            {
                return false;
            }

            intent = resolvedIntent.Trim();
            return true;
        }

        private static bool TryResolveProjectedIntent(
            OntologyAuthorityWorldProjection projection,
            string entityId,
            string predicate,
            out string intent)
        {
            intent = string.Empty;
            var matches = Array.FindAll(
                    projection.facts,
                    fact => fact != null &&
                            string.Equals(fact.subjectEntityId, entityId,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fact.predicateId, predicate,
                                StringComparison.Ordinal))
                .Select(ResolveFactObject)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1) return false;
            intent = matches[0];
            return true;
        }

        private static string ResolveFactObject(
            OntologyAuthorityFactProjection fact)
        {
            if (!string.IsNullOrWhiteSpace(fact.objectCanonicalId))
                return fact.objectCanonicalId.Trim();
            var value = fact.objectValueJson?.Trim();
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length >= 2 && value[0] == '"' &&
                   value[value.Length - 1] == '"'
                ? value.Substring(1, value.Length - 2)
                : value ?? string.Empty;
        }
    }
}
