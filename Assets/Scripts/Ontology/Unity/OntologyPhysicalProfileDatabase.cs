using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyCollisionLayerBinding
    {
        public OntologyCollisionRole role;
        public string layerName = string.Empty;
        public List<OntologyCollisionRole> collidesWith = new();
    }

    [CreateAssetMenu(
        fileName = "PhysicalProfileDatabase",
        menuName = "Tormia/Ontology/Physical Profile Database")]
    public sealed class OntologyPhysicalProfileDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyPhysicalProfile> profiles = new();
        [SerializeField, Tooltip(
            "Project-owned presentation mapping from semantic collision roles " +
            "to Unity Physics layers. Layer names never become ontology IDs.")]
        private List<OntologyCollisionLayerBinding> collisionLayers = new();

        public IReadOnlyList<OntologyPhysicalProfile> Profiles => profiles;
        public IReadOnlyList<OntologyCollisionLayerBinding> CollisionLayers =>
            collisionLayers;

        public OntologyPhysicalProfile Find(string profileId)
        {
            profileId = OntologyLanguagePackService.CanonicalTerm(profileId);
            return string.IsNullOrWhiteSpace(profileId)
                ? null
                : profiles.FirstOrDefault(value =>
                    value != null &&
                    value.profileId == profileId);
        }

        public void Replace(IEnumerable<OntologyPhysicalProfile> values)
        {
            profiles = values == null
                ? new List<OntologyPhysicalProfile>()
                : values.Where(value => value != null).Distinct().ToList();
        }

        public void ReplaceCollisionLayers(
            IEnumerable<OntologyCollisionLayerBinding> values)
        {
            collisionLayers = values == null
                ? new List<OntologyCollisionLayerBinding>()
                : values.Where(value => value != null).ToList();
        }

        public bool TryResolveCollisionLayer(
            OntologyCollisionRole role,
            out int layer)
        {
            layer = -1;
            var matches = collisionLayers
                .Where(value =>
                    value != null &&
                    value.role == role &&
                    !string.IsNullOrWhiteSpace(value.layerName))
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            layer = LayerMask.NameToLayer(matches[0].layerName.Trim());
            return layer >= 0;
        }

        public int BuildCollisionMask(
            params OntologyCollisionRole[] roles)
        {
            var mask = 0;
            foreach (var role in roles ?? Array.Empty<OntologyCollisionRole>())
            {
                if (TryResolveCollisionLayer(role, out var layer))
                {
                    mask |= 1 << layer;
                }
            }
            return mask;
        }

        public bool ValidateCollisionLayers(out string error)
        {
            error = string.Empty;
            foreach (OntologyCollisionRole role in
                     Enum.GetValues(typeof(OntologyCollisionRole)))
            {
                var matches = collisionLayers
                    .Where(value => value != null && value.role == role)
                    .ToArray();
                if (matches.Length != 1)
                {
                    error =
                        "Collision role '" + role +
                        "' must have exactly one Unity layer binding.";
                    return false;
                }
                if (!TryResolveCollisionLayer(role, out _))
                {
                    error =
                        "Collision role '" + role +
                        "' references a missing Unity Physics layer.";
                    return false;
                }
            }
            return true;
        }

        public bool ApplyCollisionMatrix()
        {
            if (!ValidateCollisionLayers(out _))
            {
                return false;
            }

            foreach (var left in collisionLayers)
            {
                if (!TryResolveCollisionLayer(left.role, out var leftLayer))
                {
                    return false;
                }
                foreach (var right in collisionLayers)
                {
                    if (!TryResolveCollisionLayer(
                            right.role,
                            out var rightLayer))
                    {
                        return false;
                    }

                    var collide =
                        (left.collidesWith?.Contains(right.role) ?? false) ||
                        (right.collidesWith?.Contains(left.role) ?? false);
                    Physics.IgnoreLayerCollision(
                        leftLayer,
                        rightLayer,
                        !collide);
                }
            }
            return true;
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null) return;
            foreach (var profile in profiles.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.profileId)))
            {
                world.AddConcept(profile.profileId, OntologyConcepts.PhysicalProfile);
                world.AddFact(
                    profile.profileId,
                    OntologyPredicates.MobilityMode,
                    profile.mobilityMode switch
                    {
                        OntologyPhysicalMobilityMode.Anchored =>
                            OntologyObjects.Anchored,
                        OntologyPhysicalMobilityMode.AuthorityKinematic =>
                            OntologyObjects.AuthorityKinematic,
                        _ => OntologyObjects.Dynamic
                    });
                if (profile.supportsBuoyancy)
                {
                    world.AddFact(
                        profile.profileId,
                        OntologyPredicates.SupportsBehavior,
                        OntologyObjects.Buoyancy);
                }
            }
        }
    }
}
