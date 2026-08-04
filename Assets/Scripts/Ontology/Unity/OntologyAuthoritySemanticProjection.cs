using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Read-only Authority provenance attached to one Unity presentation. Gameplay
    /// adapters continue to consume OntologyObject/RuleBlockAssignment; authoring UI
    /// uses this projection to explain ownership without inventing local ownership.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthoritySemanticProjection : MonoBehaviour
    {
        [SerializeField] private List<OntologyAuthorityFactProjection> facts = new();
        [SerializeField] private long revision;

        public IReadOnlyList<OntologyAuthorityFactProjection> Facts => facts;
        public long Revision => revision;

        public void Replace(
            IEnumerable<OntologyAuthorityFactProjection> values,
            long authorityRevision)
        {
            facts = values == null
                ? new List<OntologyAuthorityFactProjection>()
                : values.Where(value => value != null).ToList();
            revision = authorityRevision;
        }

        public OntologyAuthorityFactProjection Find(
            string predicate,
            string objectValue)
        {
            return facts.FirstOrDefault(value => value != null &&
                string.Equals(value.predicateId, predicate,
                    StringComparison.Ordinal) &&
                string.Equals(ResolveObject(value), objectValue,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveObject(
            OntologyAuthorityFactProjection value)
        {
            if (!string.IsNullOrWhiteSpace(value.objectCanonicalId))
                return value.objectCanonicalId;
            if (!string.IsNullOrWhiteSpace(value.objectEntityId))
                return value.objectEntityId;
            return value.objectValueJson ?? string.Empty;
        }
    }
}
