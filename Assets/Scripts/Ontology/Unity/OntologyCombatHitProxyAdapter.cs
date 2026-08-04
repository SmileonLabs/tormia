using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Materializes query-only combat geometry from authored collision Triples.
    /// It never grants Damageable meaning or attack permission.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyCombatHitProxyAdapter : MonoBehaviour
    {
        [SerializeField] private CapsuleCollider capsule;

        public Collider Collider => capsule;

        public bool TryConfigure(OntologyObject ontology)
        {
            if (ontology == null ||
                !TryGetSingle(ontology, OntologyPredicates.CollisionProxyShape,
                    out var shape) ||
                !string.Equals(shape, "Capsule", StringComparison.Ordinal) ||
                !TryGetPositive(ontology, OntologyPredicates.CollisionRadius,
                    out var radius) ||
                !TryGetPositive(ontology, OntologyPredicates.CollisionHeight,
                    out var height) ||
                height < radius * 2f)
            {
                Disable();
                return false;
            }

            if (capsule == null)
                capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(
                GetNumberOrDefault(ontology,
                    OntologyPredicates.CollisionCenterOffsetX, 0f),
                GetNumberOrDefault(ontology,
                    OntologyPredicates.CollisionCenterOffsetY, height * 0.5f),
                GetNumberOrDefault(ontology,
                    OntologyPredicates.CollisionCenterOffsetZ, 0f));
            capsule.enabled = true;
            return true;
        }

        public void Disable()
        {
            if (capsule != null) capsule.enabled = false;
        }

        private static bool TryGetPositive(
            OntologyObject ontology,
            string predicate,
            out float value)
        {
            value = 0f;
            return TryGetSingle(ontology, predicate, out var text) &&
                   float.TryParse(text, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out value) &&
                   float.IsFinite(value) && value > 0f;
        }

        private static float GetNumberOrDefault(
            OntologyObject ontology,
            string predicate,
            float fallback)
        {
            return TryGetSingle(ontology, predicate, out var text) &&
                   float.TryParse(text, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out var value) &&
                   float.IsFinite(value)
                ? value
                : fallback;
        }

        private static bool TryGetSingle(
            OntologyObject ontology,
            string predicate,
            out string value)
        {
            value = string.Empty;
            var matches = ontology.Facts
                .Where(fact => fact != null &&
                    string.Equals(fact.predicate, predicate,
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(fact.obj))
                .Select(fact => fact.obj.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1) return false;
            value = matches[0];
            return true;
        }
    }
}
