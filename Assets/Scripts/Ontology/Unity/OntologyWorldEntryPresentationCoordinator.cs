using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyWorldEntryPresentationPhase
    {
        Dormant,
        Preparing,
        Prepared,
        Active,
        Failed
    }

    /// <summary>
    /// Owns the Unity-only half of account world entry.
    ///
    /// Durable checkpoint data is restored before this component runs. This
    /// component then prepares collision while the avatar remains hidden and
    /// input-gated, clears prior ephemeral observations, and primes animation
    /// before the session is allowed to become InWorld.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldEntryPresentationCoordinator :
        MonoBehaviour
    {
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private OntologyWorldEntryGroundingAdapter groundingAdapter;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private OntologyAnimationAdapter animationAdapter;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Renderer[] avatarRenderers =
            Array.Empty<Renderer>();
        [SerializeField, Min(0.1f)] private float preparationTimeoutSeconds = 3f;

        private OntologyGameSessionCoordinator subscribedCoordinator;
        private readonly Dictionary<Renderer, bool>
            rendererVisibilityBeforeGate = new();
        private bool avatarPresentationHidden;

        public OntologyWorldEntryPresentationPhase Phase { get; private set; } =
            OntologyWorldEntryPresentationPhase.Dormant;
        public bool IsPrepared =>
            Phase == OntologyWorldEntryPresentationPhase.Prepared;

        private void Awake()
        {
            ResolveDependencies();
            Subscribe();
            ApplySessionBoundary();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
            ApplySessionBoundary();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            OntologyGameSessionCoordinator coordinator,
            OntologyAuthorityEntityIdentity identity)
        {
            sessionCoordinator = coordinator;
            if (avatarIdentity != identity)
            {
                avatarIdentity = identity;
                ResetAvatarDependencies();
            }
            ResolveDependencies();
            Subscribe();
            ApplySessionBoundary();
        }

        public bool BeginPreparation()
        {
            ResolveDependencies();
            if (sessionCoordinator == null ||
                sessionCoordinator.State !=
                OntologyGameSessionState.EnteringWorld ||
                avatarIdentity == null ||
                !avatarIdentity.gameObject.activeInHierarchy ||
                characterController == null ||
                !characterController.enabled ||
                groundingAdapter == null ||
                playerInput == null ||
                animationAdapter == null)
            {
                Phase = OntologyWorldEntryPresentationPhase.Failed;
                return false;
            }

            groundingAdapter.CancelPendingSettle();
            playerInput.enabled = false;
            animationAdapter.enabled = false;
            CaptureAndHideAvatar();
            Phase = OntologyWorldEntryPresentationPhase.Preparing;
            return true;
        }

        public IEnumerator PrepareRoutine(Action<bool> completed)
        {
            if (Phase != OntologyWorldEntryPresentationPhase.Preparing)
            {
                completed?.Invoke(false);
                yield break;
            }

            // Scene composition, restored transforms, and the physics broad
            // phase may settle on different frames. Preparation stays hidden
            // and input-gated for the whole interval.
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            groundingAdapter.RequestSettle();
            var deadline =
                Time.realtimeSinceStartup +
                Mathf.Max(0.1f, preparationTimeoutSeconds);
            while (groundingAdapter.SettlePending &&
                   Time.realtimeSinceStartup < deadline)
            {
                groundingAdapter.TrySettle();
                if (groundingAdapter.SettlePending)
                {
                    yield return new WaitForFixedUpdate();
                }
            }

            var prepared =
                !groundingAdapter.SettlePending &&
                characterController != null &&
                characterController.enabled &&
                characterController.gameObject.activeInHierarchy &&
                characterController.isGrounded;
            if (!prepared)
            {
                groundingAdapter.CancelPendingSettle();
                Phase = OntologyWorldEntryPresentationPhase.Failed;
                completed?.Invoke(false);
                yield break;
            }

            playerInput.PrepareForRuntimeActivation();
            Phase = OntologyWorldEntryPresentationPhase.Prepared;
            completed?.Invoke(true);
        }

        /// <summary>
        /// Primes animation while renderers and player input are still gated.
        /// The caller transitions the session to InWorld only after this
        /// method returns true.
        /// </summary>
        public void CaptureProjectedAppearanceWhileGated()
        {
            if (Phase != OntologyWorldEntryPresentationPhase.Prepared)
            {
                return;
            }
            CaptureAndHideAvatar(forceCapture: true);
        }

        public bool CommitBeforeSessionActivation()
        {
            if (Phase != OntologyWorldEntryPresentationPhase.Prepared ||
                characterController == null ||
                !characterController.isGrounded ||
                animationAdapter == null)
            {
                Phase = OntologyWorldEntryPresentationPhase.Failed;
                return false;
            }

            animationAdapter.enabled = true;
            animationAdapter.PrimeForRuntimeActivation();
            HideAvatarWithoutChangingSnapshot();
            Phase = OntologyWorldEntryPresentationPhase.Active;
            return true;
        }

        public void AbortPreparation()
        {
            groundingAdapter?.CancelPendingSettle();
            if (playerInput != null)
            {
                playerInput.enabled = false;
            }
            if (animationAdapter != null)
            {
                animationAdapter.enabled = false;
            }
            HideAvatarWithoutChangingSnapshot();
            Phase = OntologyWorldEntryPresentationPhase.Failed;
        }

        private void HandleSessionStateChanged(
            OntologyGameSessionState previous,
            OntologyGameSessionState next)
        {
            if (next == OntologyGameSessionState.InWorld)
            {
                if (Phase == OntologyWorldEntryPresentationPhase.Active &&
                    playerInput != null)
                {
                    playerInput.enabled = true;
                }
                if (Phase == OntologyWorldEntryPresentationPhase.Active)
                {
                    RestoreAvatarVisibility();
                }
                else
                {
                    HideAvatarWithoutChangingSnapshot();
                }
                return;
            }

            groundingAdapter?.CancelPendingSettle();
            if (playerInput != null)
            {
                playerInput.enabled = false;
            }
            if (animationAdapter != null)
            {
                animationAdapter.enabled = false;
            }
            HideAvatarWithoutChangingSnapshot();
            if (next != OntologyGameSessionState.EnteringWorld)
            {
                Phase = OntologyWorldEntryPresentationPhase.Dormant;
            }
        }

        private void ApplySessionBoundary()
        {
            if (sessionCoordinator != null &&
                sessionCoordinator.State == OntologyGameSessionState.InWorld)
            {
                if (animationAdapter != null)
                {
                    animationAdapter.enabled = true;
                }
                if (playerInput != null)
                {
                    playerInput.enabled = true;
                }
                RestoreAvatarVisibility();
                Phase = OntologyWorldEntryPresentationPhase.Active;
                return;
            }

            if (playerInput != null)
            {
                playerInput.enabled = false;
            }
            if (animationAdapter != null)
            {
                animationAdapter.enabled = false;
            }
            CaptureAndHideAvatar();
        }

        private void ResolveDependencies()
        {
            if (sessionCoordinator == null)
            {
                sessionCoordinator =
                    FindAnyObjectByType<OntologyGameSessionCoordinator>(
                        FindObjectsInactive.Include);
            }
            if (avatarIdentity == null)
            {
                var localInput =
                    FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                        FindObjectsInactive.Include);
                avatarIdentity = localInput == null
                    ? null
                    : localInput.GetComponent<OntologyAuthorityEntityIdentity>();
            }
            if (avatarIdentity == null)
            {
                return;
            }

            if (groundingAdapter == null)
            {
                groundingAdapter =
                    avatarIdentity.GetComponent<
                        OntologyWorldEntryGroundingAdapter>();
            }
            if (playerInput == null)
            {
                playerInput =
                    avatarIdentity.GetComponent<
                        OntologyInputSystemPlayerInput>();
            }
            if (animationAdapter == null)
            {
                animationAdapter =
                    avatarIdentity.GetComponent<OntologyAnimationAdapter>();
            }
            if (characterController == null)
            {
                characterController =
                    avatarIdentity.GetComponent<CharacterController>();
            }
            if (avatarRenderers == null || avatarRenderers.Length == 0)
            {
                RefreshAvatarRenderers();
            }
        }

        private void ResetAvatarDependencies()
        {
            groundingAdapter = null;
            playerInput = null;
            animationAdapter = null;
            characterController = null;
            avatarRenderers = Array.Empty<Renderer>();
            rendererVisibilityBeforeGate.Clear();
            avatarPresentationHidden = false;
        }

        private void RefreshAvatarRenderers()
        {
            avatarRenderers = avatarIdentity == null
                ? Array.Empty<Renderer>()
                : avatarIdentity.GetComponentsInChildren<Renderer>(true);
        }

        private void CaptureAndHideAvatar(bool forceCapture = false)
        {
            RefreshAvatarRenderers();
            if (avatarRenderers == null)
            {
                return;
            }
            for (var index = 0; index < avatarRenderers.Length; index++)
            {
                var avatarRenderer = avatarRenderers[index];
                if (avatarRenderer == null)
                {
                    continue;
                }
                if (forceCapture ||
                    !avatarPresentationHidden ||
                    !rendererVisibilityBeforeGate.ContainsKey(avatarRenderer))
                {
                    rendererVisibilityBeforeGate[avatarRenderer] =
                        avatarRenderer.enabled;
                }
                avatarRenderer.enabled = false;
            }
            avatarPresentationHidden = true;
        }

        private void HideAvatarWithoutChangingSnapshot()
        {
            RefreshAvatarRenderers();
            if (avatarRenderers == null)
            {
                return;
            }
            for (var index = 0; index < avatarRenderers.Length; index++)
            {
                var avatarRenderer = avatarRenderers[index];
                if (avatarRenderer == null)
                {
                    continue;
                }
                if (!rendererVisibilityBeforeGate.ContainsKey(avatarRenderer))
                {
                    rendererVisibilityBeforeGate[avatarRenderer] =
                        avatarRenderer.enabled;
                }
                avatarRenderer.enabled = false;
            }
            avatarPresentationHidden = true;
        }

        private void RestoreAvatarVisibility()
        {
            RefreshAvatarRenderers();
            for (var index = 0; index < avatarRenderers.Length; index++)
            {
                var avatarRenderer = avatarRenderers[index];
                if (avatarRenderer != null &&
                    rendererVisibilityBeforeGate.TryGetValue(
                        avatarRenderer,
                        out var enabled))
                {
                    avatarRenderer.enabled = enabled;
                }
            }
            avatarPresentationHidden = false;
        }

        private void Subscribe()
        {
            if (subscribedCoordinator == sessionCoordinator)
            {
                return;
            }
            Unsubscribe();
            subscribedCoordinator = sessionCoordinator;
            if (subscribedCoordinator != null)
            {
                subscribedCoordinator.StateChanged +=
                    HandleSessionStateChanged;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedCoordinator != null)
            {
                subscribedCoordinator.StateChanged -=
                    HandleSessionStateChanged;
            }
            subscribedCoordinator = null;
        }
    }
}
