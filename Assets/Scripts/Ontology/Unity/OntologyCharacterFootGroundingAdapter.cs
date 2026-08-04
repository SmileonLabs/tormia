using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only Humanoid foot grounding. Unity Physics supplies the
    /// contact points and Animator IK presents them; this adapter never moves
    /// the actor root, CharacterController, or durable world pose.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class OntologyCharacterFootGroundingAdapter : MonoBehaviour
    {
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Animator animator;
        private OntologyCharacterMotionCoordinator motionCoordinator;
        private OntologySwimmingMovementAdapter swimmingMovement;
        private OntologyInputSystemPlayerInput playerInput;
        private float leftWeight;
        private float rightWeight;

        public void Configure(
            OntologyCharacterMotionCoordinator coordinator)
        {
            motionCoordinator = coordinator;
            animator ??= GetComponent<Animator>();
        }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            motionCoordinator ??=
                GetComponentInParent<OntologyCharacterMotionCoordinator>();
            swimmingMovement = motionCoordinator == null
                ? null
                : motionCoordinator.GetComponent<
                    OntologySwimmingMovementAdapter>();
            playerInput = motionCoordinator == null
                ? null
                : motionCoordinator.GetComponent<
                    OntologyInputSystemPlayerInput>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            var profile = motionCoordinator == null
                ? null
                : motionCoordinator.ActivePhysicalProfile;
            var active = profile != null &&
                         profile.enableFootGrounding &&
                         motionCoordinator.HasGroundContact &&
                         (swimmingMovement == null ||
                          !swimmingMovement.IsSwimmingPresentationActive) &&
                         animator != null &&
                         animator.isHuman;
            var maximumWeight = profile == null
                ? 0f
                : Mathf.Clamp01(
                    playerInput != null && playerInput.IsMovingIntent
                        ? profile.footIkMovingWeight
                        : profile.footIkStationaryWeight);

            ApplyFoot(
                AvatarIKGoal.LeftFoot,
                active,
                profile,
                maximumWeight,
                ref leftWeight);
            ApplyFoot(
                AvatarIKGoal.RightFoot,
                active,
                profile,
                maximumWeight,
                ref rightWeight);
        }

        private void ApplyFoot(
            AvatarIKGoal goal,
            bool active,
            OntologyPhysicalProfile profile,
            float maximumWeight,
            ref float weight)
        {
            var targetWeight = 0f;
            var targetPosition = Vector3.zero;
            var targetRotation = Quaternion.identity;
            if (active && TryResolveFootPose(
                    goal,
                    profile,
                    out targetPosition,
                    out targetRotation))
            {
                targetWeight = maximumWeight;
            }

            var blendSpeed = profile == null
                ? 1f
                : Mathf.Max(0.01f, profile.footIkBlendSpeed);
            weight = Mathf.MoveTowards(
                weight,
                targetWeight,
                blendSpeed * Time.deltaTime);
            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKRotationWeight(goal, weight);
            if (targetWeight <= 0f)
            {
                return;
            }

            animator.SetIKPosition(goal, targetPosition);
            animator.SetIKRotation(goal, targetRotation);
        }

        private bool TryResolveFootPose(
            AvatarIKGoal goal,
            OntologyPhysicalProfile profile,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = animator.GetIKPosition(goal);
            rotation = animator.GetIKRotation(goal);
            var startHeight = Mathf.Max(0.01f, profile.footProbeStartHeight);
            var distance = Mathf.Max(
                startHeight + 0.01f,
                profile.footProbeDistance);
            var origin = position + Vector3.up * startHeight;
            var count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                hits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var bestDistance = float.PositiveInfinity;
            RaycastHit best = default;
            var minimumUp = Mathf.Cos(
                Mathf.Clamp(profile.footMaximumSlope, 0f, 89f) *
                Mathf.Deg2Rad);
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (hit.collider == null ||
                    hit.collider.transform == motionCoordinator.transform ||
                    hit.collider.transform.IsChildOf(
                        motionCoordinator.transform) ||
                    Vector3.Dot(hit.normal, Vector3.up) < minimumUp ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                best = hit;
            }

            if (bestDistance == float.PositiveInfinity)
            {
                return false;
            }

            position = best.point +
                       best.normal * Mathf.Max(0f, profile.footSoleOffset);
            var forward = Vector3.ProjectOnPlane(
                animator.transform.forward,
                best.normal);
            if (forward.sqrMagnitude > 0.0001f)
            {
                rotation = Quaternion.LookRotation(
                    forward.normalized,
                    best.normal);
            }
            return true;
        }
    }
}
