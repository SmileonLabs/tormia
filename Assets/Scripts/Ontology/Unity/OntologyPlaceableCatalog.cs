using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyPlaceableKind
    {
        Object,
        Npc,
        Monster
    }

    public enum OntologyPlacementSurfaceKind
    {
        AnyCollider,
        Ground,
        Water
    }

    [Serializable]
    public sealed class OntologyPlacementPolicy
    {
        [Min(0.1f)] public float maximumDistanceFromActor = 5f;
        [Range(0f, 89f)] public float maximumSlopeAngle = 35f;
        [Min(0f)] public float minimumDistanceFromPlacedObject = 0.5f;
        public OntologyPlacementSurfaceKind requiredSurface = OntologyPlacementSurfaceKind.AnyCollider;
        [Tooltip("Allows existing colliders that have not yet been labelled with OntologyPlacementSurface.")]
        public bool allowUnclassifiedSurface = true;
        public bool alignToSurfaceNormal;
        public bool randomYawOnPlacement = true;
        public Vector3 defaultLocalScale = Vector3.one;
    }

    [Serializable]
    public sealed class OntologyPlaceableDefinition
    {
        [Tooltip("Stable data id. It must not change after saves exist.")]
        public string definitionId;
        [Tooltip("Top-level placement catalog mode. This is authored data, not a prefab-name convention.")]
        public OntologyPlaceableKind placementKind = OntologyPlaceableKind.Object;
        [Tooltip("Language-pack key used only for display. It never replaces the stable definition id.")]
        public string displayNameKey;
        public string displayName;
        public string category = "Nature";
        [TextArea] public string description;
        public GameObject prefab;
        [Header("Preview Authoring")]
        [Tooltip("Optional preview-only model. Empty uses the placement prefab.")]
        public GameObject previewPrefab;
        [Tooltip("When enabled, the values below are small adjustments applied after automatic centering and framing. They never replace automatic framing.")]
        public bool useAuthoredPreviewTransform;
        [Tooltip("Offset from the automatically centered preview position.")]
        public Vector3 previewLocalPosition;
        [Tooltip("Rotation added after the preview is automatically centered.")]
        public Vector3 previewLocalEulerAngles;
        [Tooltip("Multiplier applied to the automatically calculated preview scale. (1,1,1) keeps its automatic size.")]
        public Vector3 previewLocalScale = Vector3.one;
        public OntologyMapObjectTemplate ontologyTemplate;
        public OntologyPlacementPolicy placementPolicy = new();
        [Header("Semantic Behaviour")]
        [Tooltip("Optional physical tuning data. Rules still decide when a physical state is active.")]
        public OntologyPhysicalProfile physicalProfile;
        [Tooltip("Optional wearable or mountable presentation data.")]
        public OntologyAttachmentProfile attachmentProfile;
        [Tooltip("Rule blocks injected into each newly placed instance. These remain editable per instance.")]
        public List<OntologyRuleBlockBinding> defaultRuleBlocks = new();

        public string EffectiveDisplayName => string.IsNullOrWhiteSpace(displayName) ? definitionId : displayName;
        public string LocalizedDisplayName =>
            OntologyLanguagePackService.Text(displayNameKey, EffectiveDisplayName);
        public bool IsValid => !string.IsNullOrWhiteSpace(definitionId) && prefab != null;

    }

    [CreateAssetMenu(fileName = "PlaceableCatalog", menuName = "Tormia/Ontology/Placeable Catalog")]
    public sealed class OntologyPlaceableCatalog : ScriptableObject
    {
        [SerializeField, Tooltip(
            "Fallback physical profile for catalog entries without an explicit profile.")]
        private OntologyPhysicalProfile defaultPhysicalProfile;
        [SerializeField] private List<OntologyPlaceableDefinition> definitions = new();

        public OntologyPhysicalProfile DefaultPhysicalProfile =>
            defaultPhysicalProfile;
        public IReadOnlyList<OntologyPlaceableDefinition> Definitions => definitions;

        public OntologyPlaceableDefinition Find(string definitionId)
        {
            return definitions.Find(candidate =>
                candidate != null &&
                string.Equals(
                    candidate.definitionId,
                    definitionId,
                    StringComparison.OrdinalIgnoreCase));
        }

        public void ReplaceDefinitions(IEnumerable<OntologyPlaceableDefinition> values)
        {
            definitions = values == null ? new List<OntologyPlaceableDefinition>() : new List<OntologyPlaceableDefinition>(values);
        }

        public OntologyPhysicalProfile ResolvePhysicalProfile(
            OntologyPlaceableDefinition definition)
        {
            return definition?.physicalProfile != null
                ? definition.physicalProfile
                : defaultPhysicalProfile;
        }
    }
}
