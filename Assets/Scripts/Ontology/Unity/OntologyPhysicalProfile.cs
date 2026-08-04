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
        Anchored,
        AuthorityKinematic
    }

    public enum OntologyCharacterImpactMode
    {
        None,
        ControllerImpulse,
        TemporaryRigidbodyReaction
    }

    /// <summary>
    /// Selects the single Unity presentation driver allowed to move an entity.
    /// This is authored Physical Meaning, not a prefab or component-name guess.
    /// </summary>
    public enum OntologyMotionDriver
    {
        Rigidbody,
        LocalCharacterController,
        AuthorityKinematic,
        Attachment
    }

    /// <summary>
    /// Classifies how colliders participate in local presentation. Gameplay
    /// meaning remains in ontology data; this role lets the Unity collision
    /// adapter distinguish support surfaces from actor bodies and triggers.
    /// </summary>
    public enum OntologyCollisionRole
    {
        DynamicProp,
        WalkableSupport,
        ActorBody,
        InteractionTrigger,
        WaterVolume
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
        [Tooltip(
            "The exclusive Unity component family allowed to move this entity.")]
        public OntologyMotionDriver motionDriver =
            OntologyMotionDriver.Rigidbody;
        [Tooltip(
            "The collision role used by support probes and movement adapters.")]
        public OntologyCollisionRole collisionRole =
            OntologyCollisionRole.DynamicProp;
        [Min(0.01f)] public float mass = 1f;
        [Min(0f)] public float linearDamping = 0.5f;
        [Min(0f)] public float angularDamping = 1f;

        [Header("Collision Presentation")]
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

        [Header("Character Controller Presentation")]
        [Tooltip(
            "Maximum collision-resolved rise presented as a walkable step when " +
            "this profile selects LocalCharacterController. This tuning is " +
            "derived from Physical Meaning and is ignored by other drivers.")]
        [Min(0f)] public float maximumStepHeight = 0.3f;
        [Tooltip(
            "Unity CharacterController slopeLimit used while this profile " +
            "selects LocalCharacterController.")]
        [Range(0f, 89f)] public float characterSlopeLimit = 45f;
        [Tooltip(
            "Unity CharacterController skinWidth used for collision contact.")]
        [Min(0.001f)] public float characterSkinWidth = 0.03f;
        [Tooltip(
            "Unity CharacterController minMoveDistance. Keep at zero unless " +
            "a platform-specific profile requires filtering tiny moves.")]
        [Min(0f)] public float characterMinimumMoveDistance;
        [Header("Character Foot Presentation")]
        public bool enableFootGrounding = true;
        [Min(0.01f)] public float footProbeStartHeight = 0.35f;
        [Min(0.01f)] public float footProbeDistance = 0.75f;
        [Min(0f)] public float footSoleOffset = 0.025f;
        [Min(0.01f)] public float footIkBlendSpeed = 10f;
        [Range(0f, 1f)] public float footIkStationaryWeight = 1f;
        [Range(0f, 1f)] public float footIkMovingWeight = 0.15f;
        [Range(0f, 89f)] public float footMaximumSlope = 55f;

        [Header("Character Impact Presentation")]
        [Tooltip(
            "Tuning used only after an Authority-approved impact result. The " +
            "impact_response_profile Triple selects whether the controller or " +
            "a temporary Rigidbody reaction presents that result.")]
        public OntologyCharacterImpactMode defaultCharacterImpactMode =
            OntologyCharacterImpactMode.None;
        [Min(0f)] public float controllerImpulseDamping = 10f;
        [Min(0f)] public float rigidbodySettleSpeed = 0.15f;
        [Min(0f)] public float rigidbodyMinimumReactionSeconds = 0.2f;

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
