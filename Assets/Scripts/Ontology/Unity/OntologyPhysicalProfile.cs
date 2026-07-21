using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyDynamicColliderMode
    {
        KeepExisting,
        ConvexMesh,
        BoundsBox,
        BoundsSphere
    }

    public enum OntologyPhysicalMobilityMode
    {
        Dynamic,
        Anchored
    }

    /// <summary>
    /// Numeric presentation data for ontology-driven physics. The profile never decides
    /// whether an object floats; it only tunes the Unity adapter after rules infer that state.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewPhysicalProfile",
        menuName = "Tormia/Ontology/Physical Profile")]
    public sealed class OntologyPhysicalProfile : ScriptableObject
    {
        [Tooltip("Stable identifier referenced by ontology facts and save data.")]
        public string profileId;
        [Tooltip("Semantic mobility exposed by the profile database and presented by the physical body adapter.")]
        public OntologyPhysicalMobilityMode mobilityMode =
            OntologyPhysicalMobilityMode.Dynamic;
        [Min(0.01f)] public float mass = 1f;
        [Min(0f)] public float linearDamping = 0.5f;
        [Min(0f)] public float angularDamping = 1f;

        [Header("Dynamic Collision Presentation")]
        [Tooltip("Selects a Rigidbody-compatible collision shape without depending on the object name.")]
        public OntologyDynamicColliderMode dynamicColliderMode =
            OntologyDynamicColliderMode.KeepExisting;
        [Tooltip("Local offset added to an automatically generated bounds collider.")]
        public Vector3 dynamicColliderCenterOffset;
        [Tooltip("Size multiplier for an automatically generated bounds collider.")]
        public Vector3 dynamicColliderSizeMultiplier = Vector3.one;
        [Tooltip(
            "Optional Rigidbody constraints applied while this physical profile is active.")]
        public RigidbodyConstraints constraints = RigidbodyConstraints.None;

        [Header("Buoyancy Presentation")]
        public bool supportsBuoyancy;
        [Tooltip(
            "Ontology rule block that may infer Floating for this profile. " +
            "This is an explicit semantic link, not a lookup by rule ordering.")]
        public string buoyancyRuleId;
        [Min(0f)] public float buoyancyStrength = 12f;
        [Min(0f)] public float submergedDamping = 2f;
        [Tooltip(
            "Fraction of the object's physical bounds below the waterline at rest. " +
            "Zero places the bottom at the surface; one places the top at the surface.")]
        [Range(0f, 1f)] public float submergedFraction = 0.5f;
        [Tooltip(
            "Fine waterline adjustment after bounds-based submergence is calculated.")]
        public float surfaceOffset;
        [Min(0f)] public float uprightStability = 3f;
        [Min(1)] public int buoyancySampleCount = 1;
    }
}
