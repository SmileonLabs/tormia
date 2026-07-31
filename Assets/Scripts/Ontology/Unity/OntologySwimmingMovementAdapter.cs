using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Adapts the inferred swimming state to CharacterController motion.
    /// Ontology rules decide whether an actor is swimming; this component only expresses that
    /// result through buoyancy and a reduced horizontal speed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(OntologyWaterPresenceSensor))]
    public sealed class OntologySwimmingMovementAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; the OntologyObject id is authoritative.")]
        private string actorId;
        [SerializeField] private string movementModePredicate = OntologyPredicates.MobilityMode;
        [SerializeField] private string swimmingMode = OntologyObjects.Swimming;
        [Header("Pose Waterline Policy")]
        [SerializeField] private OntologyAnimationAdapter animationAdapter;
        [SerializeField] private OntologySwimmingPoseProfile swimmingPoseProfile;
        [SerializeField] private Animator targetAnimator;

        [Header("Swimming Motion")]
        [SerializeField, Range(0.1f, 1f)] private float horizontalSpeedMultiplier = 0.65f;
        [SerializeField] private float surfaceFollowStrength = 7f;
        [SerializeField] private float maximumVerticalSpeed = 5f;
        [Tooltip("Root height relative to the water surface. A negative value submerges the character while swimming.")]
        [SerializeField] private float rootHeightAboveSurface = -0.75f;

        private CharacterController characterController;
        private OntologyWaterPresenceSensor waterPresenceSensor;
        private string ActorId => actorObject != null &&
                                  !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;
        public bool IsSwimmingPresentationActive =>
            IsSwimming() &&
            waterPresenceSensor != null &&
            waterPresenceSensor.TryGetActiveSurfaceHeight(out _);

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
            waterPresenceSensor = GetComponent<OntologyWaterPresenceSensor>();
            if (animationAdapter == null) animationAdapter = GetComponent<OntologyAnimationAdapter>();
            if (targetAnimator == null) targetAnimator = FindHumanoidAnimator();
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }
        }

        /// <summary>
        /// Resolves swimming displacement when the ontology currently infers
        /// Swimming. The exclusive motion coordinator performs the actual
        /// CharacterController.Move call.
        /// </summary>
        public bool TryResolveDisplacement(
            Vector3 horizontalDirection,
            float baseSpeed,
            float deltaTime,
            out Vector3 displacement)
        {
            displacement = Vector3.zero;
            if (!IsSwimming() || characterController == null || waterPresenceSensor == null)
            {
                return false;
            }

            if (!waterPresenceSensor.TryGetActiveSurfaceHeight(out var surfaceHeight))
            {
                return false;
            }

            var desiredRootHeight = GetDesiredRootHeight(surfaceHeight);
            var verticalSpeed = Mathf.Clamp(
                (desiredRootHeight - transform.position.y) * Mathf.Max(0f, surfaceFollowStrength),
                -Mathf.Max(0f, maximumVerticalSpeed),
                Mathf.Max(0f, maximumVerticalSpeed));

            var horizontalVelocity = Vector3.ClampMagnitude(horizontalDirection, 1f)
                * Mathf.Max(0f, baseSpeed)
                * Mathf.Clamp01(horizontalSpeedMultiplier);
            displacement =
                (horizontalVelocity + Vector3.up * verticalSpeed) *
                Mathf.Max(0f, deltaTime);
            return true;
        }

        private bool IsSwimming()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            return bootstrap != null
                && bootstrap.World != null
                && bootstrap.World.HasFact(ActorId, movementModePredicate, swimmingMode);
        }

        private float GetDesiredRootHeight(float surfaceHeight)
        {
            if (animationAdapter != null && swimmingPoseProfile != null &&
                swimmingPoseProfile.TryGetDefinition(animationAdapter.SelectedIntent, out var definition))
            {
                var waterlineBone = targetAnimator == null ? null : targetAnimator.GetBoneTransform(definition.waterlineBone);
                if (waterlineBone != null)
                {
                    var desiredBoneHeight = surfaceHeight + definition.surfaceOffset;
                    return transform.position.y + (desiredBoneHeight - waterlineBone.position.y);
                }

                return surfaceHeight + definition.fallbackRootHeight;
            }

            return surfaceHeight + rootHeightAboveSurface;
        }

        private Animator FindHumanoidAnimator()
        {
            foreach (var candidate in GetComponentsInChildren<Animator>())
            {
                if (candidate != null && candidate.avatar != null && candidate.isHuman)
                {
                    return candidate;
                }
            }

            return GetComponent<Animator>();
        }
    }
}
