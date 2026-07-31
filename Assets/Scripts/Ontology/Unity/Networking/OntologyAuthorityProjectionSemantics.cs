using System;
using System.Globalization;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Reads typed semantic markers from an Authority projection. Projection
    /// consumers share this parser so a numeric Fact cannot be mistaken for a
    /// missing canonical-string Fact and trigger a destructive migration loop.
    /// </summary>
    public static class OntologyAuthorityProjectionSemantics
    {
        public static int ResolveSemanticContractVersion(
            OntologyAuthorityWorldProjection projection,
            string entityId)
        {
            if (projection?.facts == null ||
                string.IsNullOrWhiteSpace(entityId))
            {
                return 0;
            }

            var resolved = 0;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        entityId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.SemanticContractVersion,
                        StringComparison.Ordinal) ||
                    !TryReadNonNegativeInteger(fact, out var version))
                {
                    continue;
                }

                resolved = Math.Max(resolved, version);
            }

            return resolved;
        }

        public static int ResolveSemanticContractVersion(
            OntologyAuthorityWorldProjection projection,
            Guid entityId) =>
            entityId == Guid.Empty
                ? 0
                : ResolveSemanticContractVersion(
                    projection,
                    entityId.ToString("D"));

        private static bool TryReadNonNegativeInteger(
            OntologyAuthorityFactProjection fact,
            out int value)
        {
            value = 0;
            var raw = fact.objectValueJson;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                raw = raw.Trim().Trim('"');
                if (int.TryParse(
                        raw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var typed) &&
                    typed >= 0)
                {
                    value = typed;
                    return true;
                }
            }

            return int.TryParse(
                       fact.objectCanonicalId,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   value >= 0;
        }
    }
}
