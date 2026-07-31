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
    public sealed class OntologyRuleBlockMigration
    {
        [Min(1), Tooltip(
            "Semantic contract version that introduced this replacement. " +
            "Older entities receive the replacement once; later user removal " +
            "is never silently undone.")]
        public int targetContractVersion = 2;
        public string fromRuleId;
        public string fromBindingVariable = "?target";
        public OntologyRuleBlockBinding replacement = new();
    }

    [Serializable]
    public sealed class OntologyRuleBlockRetirement
    {
        [Min(1), Tooltip(
            "Semantic contract version that retires this binding. The removal " +
            "runs only while crossing this version, so a later user-authored " +
            "assignment is preserved.")]
        public int contractVersion = 1;
        public string ruleId;
        public string bindingVariable = "?target";
    }

    [Serializable]
    public sealed class OntologyRuleBlockIntroduction
    {
        [Min(1), Tooltip(
            "Semantic contract version that first introduces this reusable " +
            "behavior. It is added only while crossing this version, so a later " +
            "user removal is preserved.")]
        public int contractVersion = 1;
        public OntologyRuleBlockBinding binding = new();
    }

    [Serializable]
    public sealed class OntologyFactIntroduction
    {
        [Min(1), Tooltip(
            "Semantic contract version that first introduces this authored " +
            "Fact. It is added only while crossing this version. An existing " +
            "value for the same predicate is preserved.")]
        public int contractVersion = 1;
        public OntologyFactEntry fact = new();
    }

    [Serializable]
    public sealed class OntologyFactRetirement
    {
        [Min(1), Tooltip(
            "Semantic contract version that retires this exact authored Fact. " +
            "The removal runs only while crossing this version, so a later " +
            "user-authored value is preserved.")]
        public int contractVersion = 1;
        public OntologyFactEntry fact = new();
        [Tooltip(
            "When enabled, the exact Fact is retired only if another active " +
            "value for the same predicate exists. Use this to remove a legacy " +
            "default without deleting the only valid state.")]
        public bool onlyWhenPredicateHasDifferentValue;
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
        [Min(1), Tooltip(
            "Version of the catalog-owned semantic contract. Runtime migration " +
            "advances only through the data-authored replacements below.")]
        public int semanticContractVersion = 1;
        [Tooltip(
            "Data-owned Rule Block replacements used when an older placed " +
            "entity advances to this semantic contract version.")]
        public List<OntologyRuleBlockMigration> ruleBlockMigrations = new();
        [Tooltip(
            "Data-owned one-time authored Fact additions for a new capability. " +
            "An existing predicate value is preserved and never overwritten.")]
        public List<OntologyFactIntroduction> introducedFacts = new();
        [Tooltip(
            "Data-owned one-time Rule Block additions for a new capability. " +
            "Unlike defaults, they are never continuously restored.")]
        public List<OntologyRuleBlockIntroduction> introducedRuleBlocks = new();
        [Tooltip(
            "Data-owned one-time exact Fact removals for obsolete or conflicting " +
            "legacy defaults. Each removal runs only while crossing its authored " +
            "contract version.")]
        public List<OntologyFactRetirement> retiredFacts = new();
        [Tooltip(
            "Data-owned one-time Rule Block removals for obsolete behavior. " +
            "Each removal runs only while crossing its authored contract version.")]
        public List<OntologyRuleBlockRetirement> retiredRuleBlocks = new();
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
            "Legacy migration reference only. Runtime definitions must explicitly select a physical profile.")]
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
            return definition?.physicalProfile;
        }
    }
}
