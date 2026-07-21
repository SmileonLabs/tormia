using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyAttachmentKind
    {
        Wearable,
        Mountable,
        Carryable
    }

    public enum OntologyProximityMeasurementMode
    {
        TransformCenter,
        ColliderSurface,
        InteractionAnchor
    }

    /// <summary>
    /// Presentation data for an inferred equip or mount relation. Ontology facts decide
    /// whether attachment occurs; this profile only specifies the visual anchor and offsets.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewAttachmentProfile",
        menuName = "Tormia/Ontology/Attachment Profile")]
    public sealed class OntologyAttachmentProfile : ScriptableObject
    {
        [Tooltip("Stable identifier referenced by ontology facts and save data.")]
        public string profileId;
        public OntologyAttachmentKind kind = OntologyAttachmentKind.Wearable;
        [Tooltip("Semantic slot such as Waist, Head, RightHand, Back, or Seat.")]
        public string slotId = "Waist";

        [Header("Wearable / Carryable Anchor")]
        public HumanBodyBones actorAnchorBone = HumanBodyBones.Hips;
        [Tooltip("Optional child path used when the actor is not Humanoid or needs a custom socket.")]
        public string actorAnchorPath;

        [Header("Mountable Anchor")]
        [Tooltip("Child path on a mountable object, for example Seats/Driver.")]
        public string mountPointPath;

        [Header("Proximity Observation")]
        [Tooltip("Distance at which an actor is observed as near this object.")]
        [Min(0.05f)] public float autoEquipDistance = 1.5f;
        [Tooltip("Extra distance required before the near observation is removed.")]
        [Min(0f)] public float proximityExitPadding = 0.25f;
        [Tooltip("How distance to this object is measured. ColliderSurface is recommended for large objects.")]
        public OntologyProximityMeasurementMode proximityMeasurementMode =
            OntologyProximityMeasurementMode.TransformCenter;
        [Tooltip("Optional child path used by InteractionAnchor mode.")]
        public string proximityAnchorPath;

        [Header("Local Presentation")]
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
        public bool disableWorldPhysicsWhileAttached = true;
        public bool disableWorldCollidersWhileAttached = true;
        [Tooltip("Disables the actor CharacterController while mounted.")]
        public bool disableActorControllerWhileMounted = true;

        [Header("Detach Interaction")]
        [Tooltip("Local center of the small click area used to detach this item while it is attached.")]
        public Vector3 detachInteractionLocalCenter;
        [Tooltip("Local size of the click area used to detach this item while it is attached. This is presentation-only and never participates in physics.")]
        public Vector3 detachInteractionLocalSize = Vector3.one;
        [Tooltip("Distance in front of the actor where a clicked wearable returns to the world.")]
        [Min(0.1f)] public float detachForwardDistance = 1.75f;
        [Tooltip("Vertical offset applied when returning a wearable to the world.")]
        public float detachVerticalOffset = 0.25f;

        [Header("World Release Presentation")]
        [Tooltip("Small clearance kept between the released object's physical bounds and its supporting surface.")]
        [Min(0f)] public float detachSurfaceClearance = 0.05f;
        [Tooltip("Height above the release position from which ground and water surfaces are probed.")]
        [Min(0.1f)] public float detachSurfaceProbeHeight = 20f;
        [Tooltip("Maximum distance used to find a supporting ground or water surface when releasing an attachment.")]
        [Min(0.1f)] public float detachSurfaceProbeDistance = 60f;
        [Tooltip("Extra horizontal space kept between the actor and the released object's physical bounds.")]
        [Min(0f)] public float detachActorClearance = 0.25f;
    }
}
