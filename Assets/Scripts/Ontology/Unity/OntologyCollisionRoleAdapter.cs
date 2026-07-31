using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents the collision role selected by Physical Meaning. The component
    /// does not infer gameplay meaning and never moves a Transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyCollisionRoleAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyCollisionRole role =
            OntologyCollisionRole.DynamicProp;
        [SerializeField, Tooltip(
            "Optional project-owned mapping used for scene-authored collision " +
            "surfaces. Runtime ontology objects receive this from the active " +
            "Physical Profile Database.")]
        private OntologyPhysicalProfileDatabase physicalProfileDatabase;
        [SerializeField] private bool collisionLayerApplied;

        public OntologyCollisionRole Role => role;
        public bool CollisionLayerApplied => collisionLayerApplied;

        private readonly Dictionary<GameObject, int> originalLayers = new();

        private void OnEnable()
        {
            ApplyCollisionLayer();
        }

        private void OnDisable()
        {
            RestoreCollisionLayers();
        }

        public void Configure(OntologyCollisionRole value)
        {
            role = value;
            ApplyCollisionLayer();
        }

        public void Configure(
            OntologyCollisionRole value,
            OntologyPhysicalProfileDatabase database)
        {
            role = value;
            physicalProfileDatabase = database;
            ApplyCollisionLayer();
        }

        public void ClearConfiguration()
        {
            RestoreCollisionLayers();
            physicalProfileDatabase = null;
        }

        private void ApplyCollisionLayer()
        {
            RestoreCollisionLayers();
            if (physicalProfileDatabase == null ||
                !physicalProfileDatabase.TryResolveCollisionLayer(
                    role,
                    out var layer))
            {
                collisionLayerApplied = false;
                return;
            }

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider == null)
                {
                    continue;
                }
                var colliderObject = collider.gameObject;
                if (!originalLayers.ContainsKey(colliderObject))
                {
                    originalLayers.Add(
                        colliderObject,
                        colliderObject.layer);
                }
                colliderObject.layer = layer;
            }
            collisionLayerApplied = originalLayers.Count > 0;
        }

        private void RestoreCollisionLayers()
        {
            foreach (var pair in originalLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.layer = pair.Value;
                }
            }
            originalLayers.Clear();
            collisionLayerApplied = false;
        }

        public static bool TryResolve(
            Collider collider,
            out OntologyCollisionRole value)
        {
            value = OntologyCollisionRole.DynamicProp;
            if (collider == null)
            {
                return false;
            }

            var adapter =
                collider.GetComponentInParent<OntologyCollisionRoleAdapter>();
            if (adapter == null)
            {
                return false;
            }

            value = adapter.Role;
            return true;
        }

        public static bool IsWalkableSupport(Collider collider)
        {
            if (collider == null || collider.isTrigger)
            {
                return false;
            }

            if (TryResolve(collider, out var role))
            {
                return role == OntologyCollisionRole.WalkableSupport;
            }

            return false;
        }
    }
}
