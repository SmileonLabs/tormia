using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Settles a restored local avatar onto Unity collision before the in-world
    /// presentation gate enables input. This is ephemeral presentation state;
    /// it does not author a Fact or replace the Authority checkpoint.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class OntologyWorldEntryGroundingAdapter : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float maximumSnapDownDistance = 2f;
        [SerializeField, Min(0f)] private float maximumSnapUpDistance = 0.25f;
        [SerializeField, Min(0.001f)] private float surfaceClearance = 0.01f;

        private CharacterController characterController;
        private OntologyCharacterMotionCoordinator motionCoordinator;
        private bool settlePending;

        public bool SettlePending => settlePending;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            motionCoordinator =
                GetComponent<OntologyCharacterMotionCoordinator>() ??
                gameObject.AddComponent<
                    OntologyCharacterMotionCoordinator>();
        }

        private void FixedUpdate()
        {
            // Runtime roots, CharacterController and additive scene collision
            // can become ready in different frames. A pending checkpoint is a
            // state transition, not a one-shot visual correction, so keep
            // evaluating it until the caller observes completion or cancels it.
            if (settlePending)
            {
                TrySettle();
            }
        }

        /// <summary>
        /// Records that a restored checkpoint must be aligned with local Unity
        /// collision. The entry coordinator keeps the avatar root active while
        /// renderers and input remain gated, then waits for this request before
        /// it opens the runtime session.
        /// </summary>
        public bool RequestSettle()
        {
            settlePending = true;
            return TrySettle();
        }

        public void CancelPendingSettle()
        {
            settlePending = false;
        }

        public bool TrySettle()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
            var controllerActive =
                characterController != null &&
                characterController.enabled &&
                characterController.gameObject.activeInHierarchy;
            if (!CanSettle(
                    controllerActive,
                    IsSwimmingPresentationActive(),
                    transform.parent != null &&
                    transform.parent.GetComponentInParent<
                        OntologyAttachmentAdapter>() != null))
            {
                return false;
            }

            if (motionCoordinator == null)
            {
                motionCoordinator =
                    GetComponent<OntologyCharacterMotionCoordinator>();
            }
            if (motionCoordinator == null ||
                !motionCoordinator.TrySettleToSupport(
                    maximumSnapDownDistance,
                    maximumSnapUpDistance,
                    surfaceClearance))
            {
                return false;
            }

            GetComponent<OntologyInputSystemPlayerInput>()
                ?.ResetVerticalMotionAfterGrounding();
            settlePending = false;
            return true;
        }

        public static bool CanSettle(
            bool controllerEnabled,
            bool swimmingPresentationActive,
            bool mountedPresentationActive)
        {
            return controllerEnabled &&
                   !swimmingPresentationActive &&
                   !mountedPresentationActive;
        }

        private bool IsSwimmingPresentationActive()
        {
            var swimming = GetComponent<OntologySwimmingMovementAdapter>();
            return swimming != null && swimming.IsSwimmingPresentationActive;
        }
    }
}
