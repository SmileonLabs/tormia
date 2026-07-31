using System;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [RequireComponent(typeof(Animator))]
    public sealed class OntologyAnimationAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyAnimationDatabase animationDatabase;
        [SerializeField] private OntologyActorProfile actorProfile;
        [SerializeField] private OntologyCombatCatalog equipmentPresentationCatalog;
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyAuthorityEntityIdentity authorityIdentity;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; the OntologyObject id is authoritative.")]
        private string actorId;
        [SerializeField] private bool useAnimatorSpeed = true;
        [SerializeField] private float normalAnimatorSpeed = 1.0f;
        [SerializeField] private float slowedAnimatorSpeed = 0.75f;
        [SerializeField] private bool playSelectedClip;
        [SerializeField] private bool loopBlendableClips = true;
        [SerializeField, Min(0f)] private float transitionBlendDuration = 0.25f;
        [SerializeField, Min(0f), Tooltip(
            "Allows an accepted Authority animation intent to wait briefly for " +
            "the projection, actor repertoire, or runtime visual Animator.")]
        private float presentationReadinessGracePeriod = 2f;
        [SerializeField] private bool useDefaultIntentWhenNoFact = true;
        [SerializeField] private string defaultIdleIntent = "Idle";
        [SerializeField] private string defaultMoveIntent = "Locomotion";
        [SerializeField] private float movementIntentThreshold = 0.01f;
        [SerializeField, Tooltip(
            "When enabled, the canonical state resolver owns base Idle, locomotion, " +
            "airborne, landing, and equipment presentation. Input continues to own movement only.")]
        private bool driveBaseLocomotionFromManifest = true;
        [SerializeField] private float fallVelocityThreshold = -2f;
        [SerializeField] private OntologyAnimatorBoolBinding[] boolBindings =
        {
            new OntologyAnimatorBoolBinding
            {
                predicate = "movement_state",
                obj = "Slowed",
                parameter = "isSlowed"
            },
            new OntologyAnimatorBoolBinding
            {
                predicate = "exposed_to",
                obj = "ColdEnvironment",
                parameter = "isFreezing"
            }
        };

        private Animator animator;
        private int[] boolParameterHashes = Array.Empty<int>();
        [SerializeField] private string selectedAnimationId;
        [SerializeField] private string selectedClipName;
        [SerializeField] private string selectedIntent;
        [SerializeField] private string lastAuthorityPresentationDiagnostic;
        [SerializeField] private bool selectedCanBlend;
        [SerializeField] private bool selectedFromOntologyIntent;

        public string SelectedAnimationId => selectedAnimationId;
        public string SelectedClipName => selectedClipName;
        public string SelectedIntent => selectedIntent;
        public bool SelectedCanBlend => selectedCanBlend;
        public OntologyActorProfile ActorProfile => actorProfile;

        private AnimationClip selectedClip;
        private OntologyInputSystemPlayerInput ontologyInput;
        private PlayableGraph playableGraph;
        private AnimationClipPlayable clipPlayable;
        private AnimatorControllerPlayable controllerPlayable;
        private AnimationMixerPlayable clipMixer;
        private AnimationLayerMixerPlayable animationMixer;
        private string playingAnimationId;
        private bool replaySelectedClipRequested;
        private int activeClipInput = -1;
        private int transitionFromInput = -1;
        private int transitionToInput = -1;
        private AnimationTransitionMode transitionMode;
        private float transitionElapsed;
        private bool lastDefaultMovementState;
        private bool hasDefaultMovementState;
        private bool transientPresentationActive;
        private float transientPresentationEndsAt;
        private bool persistentPresentationActive;
        private string persistentPresentationIntent = string.Empty;
        private bool selectedInterruptible = true;
        private bool selectedLoop;
        private float selectedTransitionDuration;
        private bool selectedHasContactWindow;
        private float selectedContactWindowStartNormalized;
        private float selectedContactWindowEndNormalized = 1f;
        private float selectedPlaybackStartNormalized;
        private float selectedPlaybackEndNormalized = 1f;
        private OntologyAnimationLayer selectedLayer;
        private AvatarMask selectedAvatarMask;
        private OntologyAnimationRootMotionMode selectedRootMotionMode;
        private OntologyWorldCommand pendingAuthorityAnimationCommand;
        private OntologyAuthorityCommandResult pendingAuthorityAnimationResult;
        private string pendingTransientIntent = string.Empty;
        private float pendingPresentationUntil;
        private bool originalApplyRootMotion;
        private bool rootMotionCaptured;
        private OntologyAnimationStateResolver stateResolver;
        private string resolvedBaseIntent = string.Empty;
        private OntologyAnimationStateKind resolvedBaseKind =
            OntologyAnimationStateKind.Idle;
        private string equipmentIdleIntent = string.Empty;
        private string equipmentMoveIntent = string.Empty;
        private string externalBaseIntent = string.Empty;
        private string ActorId => actorObject != null &&
                                  !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private enum AnimationTransitionMode
        {
            None,
            ControllerToClip,
            ClipToClip,
            ClipToController
        }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (targetAnimator == null)
            {
                targetAnimator = FindBestAnimatorTarget();
            }

            if (targetAnimator != null)
            {
                animator = targetAnimator;
            }

            ontologyInput = GetComponent<OntologyInputSystemPlayerInput>();
            stateResolver = new OntologyAnimationStateResolver(
                fallVelocityThreshold);
            ontologyInput?.SetAnimatorPresentationOwnership(
                driveBaseLocomotionFromManifest);
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
            if (authorityIdentity == null)
                authorityIdentity = GetComponentInParent<OntologyAuthorityEntityIdentity>();
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();

            RebuildParameterCache();
        }

        private void OnValidate()
        {
            normalAnimatorSpeed = Mathf.Max(0f, normalAnimatorSpeed);
            slowedAnimatorSpeed = Mathf.Max(0f, slowedAnimatorSpeed);
            transitionBlendDuration = Mathf.Max(0f, transitionBlendDuration);
            presentationReadinessGracePeriod =
                Mathf.Max(0f, presentationReadinessGracePeriod);
        }

        private Animator FindBestAnimatorTarget()
        {
            var animators = GetComponentsInChildren<Animator>();
            foreach (var candidate in animators)
            {
                if (candidate != null && candidate.avatar != null && candidate.isHuman)
                {
                    return candidate;
                }
            }

            return GetComponent<Animator>();
        }

        private void OnEnable()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null)
            {
                bootstrap.WorldChanged += RefreshFromWorld;
            }
            BindAuthorityClient();
        }

        private void Start()
        {
            // Additive scene load order can place the actor before the bootstrap
            // client. Rebind once all Start methods are eligible without creating
            // duplicate event subscriptions.
            BindAuthorityClient();
            ResolveBaseIntent();
            RefreshFromWorld();
        }

        private void Update()
        {
            RetryPendingAuthorityPresentation();
            if (transientPresentationActive)
            {
                if (ShouldReleaseTransientPresentation(
                        transientPresentationActive,
                        persistentPresentationActive,
                        selectedInterruptible,
                        IsMoving(),
                        Time.time,
                        transientPresentationEndsAt))
                {
                    transientPresentationActive = false;
                    ResolveBaseIntent();
                    RefreshFromWorld();
                }
                if (transientPresentationActive)
                {
                    return;
                }
            }

            if (driveBaseLocomotionFromManifest && ontologyInput != null)
            {
                var previous = resolvedBaseIntent;
                ResolveBaseIntent();
                if (!string.Equals(previous, resolvedBaseIntent, StringComparison.Ordinal))
                    RefreshFromWorld();
                return;
            }

            if (!string.IsNullOrWhiteSpace(externalBaseIntent))
            {
                return;
            }

            var currentMovementState = IsMoving();
            if (!hasDefaultMovementState || currentMovementState != lastDefaultMovementState)
            {
                lastDefaultMovementState = currentMovementState;
                hasDefaultMovementState = true;
                RefreshFromWorld();
            }
        }

        public static bool ShouldYieldTransientPresentationToLocomotion(
            bool transientActive,
            bool interruptible,
            bool hasMovementIntent)
        {
            return transientActive && interruptible && hasMovementIntent;
        }

        public static bool ShouldReleaseTransientPresentation(
            bool transientActive,
            bool persistentActive,
            bool interruptible,
            bool hasMovementIntent,
            float currentTime,
            float presentationEndsAt)
        {
            return transientActive &&
                   !persistentActive &&
                   (currentTime >= presentationEndsAt ||
                    ShouldYieldTransientPresentationToLocomotion(
                        transientActive,
                        interruptible,
                        hasMovementIntent));
        }

        private void RefreshFromWorld()
        {
            if (bootstrap == null || bootstrap.World == null || animator == null)
            {
                return;
            }

            RefreshEquipmentAnimationIntents(authorityClient?.CurrentProjection);
            if (transientPresentationActive)
            {
                return;
            }

            var isSlowed = bootstrap.World.HasFact(ActorId, "movement_state", "Slowed");
            if (useAnimatorSpeed)
            {
                animator.speed = isSlowed ? slowedAnimatorSpeed : normalAnimatorSpeed;
            }

            ApplyBoolBindings();
            SelectAnimationCandidate();
            ApplySelectedClipPlayback();
        }

        /// <summary>
        /// Plays an ephemeral animation selected by canonical intent without
        /// authoring a world Fact. Authority rules still decide whether the
        /// corresponding gameplay action succeeds.
        /// </summary>
        public bool PlayTransientIntent(string intent)
        {
            return TryPlayTransientIntent(intent, out _);
        }

        /// <summary>
        /// Returns the current Authority-approved transient clip time together
        /// with its manifest-authored contact window. This is an ephemeral
        /// presentation observation only; it never decides or applies damage.
        /// </summary>
        public bool TryGetActiveContactWindow(
            string expectedIntent,
            out float normalizedTime,
            out float windowStart,
            out float windowEnd)
        {
            normalizedTime = 0f;
            windowStart = 0f;
            windowEnd = 0f;
            if (!transientPresentationActive ||
                !selectedHasContactWindow ||
                selectedClip == null ||
                selectedClip.length <= 0f ||
                !playableGraph.IsValid() ||
                !clipPlayable.IsValid() ||
                string.IsNullOrWhiteSpace(expectedIntent) ||
                !string.Equals(
                    selectedIntent,
                    expectedIntent.Trim(),
                    StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                normalizedTime = Mathf.Clamp01(
                    (float)(clipPlayable.GetTime() / selectedClip.length));
                windowStart = selectedContactWindowStartNormalized;
                windowEnd = selectedContactWindowEndNormalized;
                return true;
            }
            catch (ArgumentNullException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves the authored contact contract independently of playback
        /// readiness. This lets an observer wait for the Authority-approved
        /// transient animation without introducing a hard-coded time window.
        /// </summary>
        public bool TryResolveContactWindowContract(
            string expectedIntent,
            out float clipDuration,
            out float windowStart,
            out float windowEnd)
        {
            clipDuration = 0f;
            windowStart = 0f;
            windowEnd = 0f;
            if (animationDatabase == null ||
                string.IsNullOrWhiteSpace(expectedIntent))
            {
                return false;
            }

            var canonicalIntent = expectedIntent.Trim();
            OntologyAnimationDefinition best = null;
            foreach (var definition in animationDatabase.Definitions)
            {
                if (definition == null ||
                    !definition.hasContactWindow ||
                    definition.clip == null ||
                    definition.clip.length <= 0f ||
                    !CanUse(definition) ||
                    !HasIntent(definition, canonicalIntent))
                {
                    continue;
                }

                if (best == null || definition.priority > best.priority)
                    best = definition;
            }

            if (best == null)
                return false;

            clipDuration = best.clip.length;
            windowStart = Mathf.Clamp01(
                best.contactWindowStartNormalized);
            windowEnd = Mathf.Clamp01(
                best.contactWindowEndNormalized);
            return windowEnd > windowStart;
        }

        public static bool IsWithinContactWindow(
            float normalizedTime,
            float windowStart,
            float windowEnd)
        {
            return windowStart >= 0f &&
                   windowEnd <= 1f &&
                   windowEnd > windowStart &&
                   normalizedTime >= windowStart &&
                   normalizedTime <= windowEnd;
        }

        /// <summary>
        /// Presents a canonical state intent until its owning projection clears
        /// that state. The clip's own length selects its final pose; no prefab,
        /// animation-state, or actor-name fallback participates.
        /// </summary>
        public bool SetPersistentIntent(string intent)
        {
            var canonical = string.IsNullOrWhiteSpace(intent)
                ? string.Empty
                : intent.Trim();
            if (canonical.Length == 0)
                return false;
            if (persistentPresentationActive &&
                string.Equals(
                    persistentPresentationIntent,
                    canonical,
                    StringComparison.Ordinal))
            {
                return true;
            }

            // A durable projected state supersedes any transient hit/attack
            // presentation. The selected canonical intent still has to exist in
            // the actor's validated repertoire.
            persistentPresentationActive = false;
            persistentPresentationIntent = string.Empty;
            transientPresentationActive = false;
            if (!TryPlayTransientIntent(canonical, out var rejectionCode))
            {
                lastAuthorityPresentationDiagnostic = rejectionCode;
                return false;
            }

            persistentPresentationActive = true;
            persistentPresentationIntent = canonical;
            transientPresentationEndsAt = float.PositiveInfinity;
            return true;
        }

        public void ClearPersistentIntent(string intent = null)
        {
            if (!persistentPresentationActive)
                return;
            if (!string.IsNullOrWhiteSpace(intent) &&
                !string.Equals(
                    persistentPresentationIntent,
                    intent.Trim(),
                    StringComparison.Ordinal))
            {
                return;
            }

            persistentPresentationActive = false;
            persistentPresentationIntent = string.Empty;
            transientPresentationActive = false;
            ResolveBaseIntent();
            RefreshFromWorld();
        }

        /// <summary>
        /// Supplies an ephemeral Authority-approved base presentation intent for
        /// an actor that has no local player input component. The caller still
        /// owns no gameplay decision; this only selects a catalog animation.
        /// </summary>
        public void SetExternalBaseIntent(string intent)
        {
            var canonical = string.IsNullOrWhiteSpace(intent)
                ? string.Empty
                : intent.Trim();
            if (string.Equals(
                    externalBaseIntent,
                    canonical,
                    StringComparison.Ordinal))
            {
                return;
            }

            externalBaseIntent = canonical;
            if (!transientPresentationActive)
            {
                RefreshFromWorld();
            }
        }

        public void ClearExternalBaseIntent()
        {
            SetExternalBaseIntent(string.Empty);
        }

        private bool TryPlayTransientIntent(
            string intent,
            out string rejectionCode)
        {
            rejectionCode = string.Empty;
            if (string.IsNullOrWhiteSpace(intent))
            {
                rejectionCode = "animation_intent_missing";
                return false;
            }
            if (animationDatabase == null)
            {
                rejectionCode = "animation_database_missing";
                return false;
            }
            if (animator == null || !animator.isActiveAndEnabled)
            {
                rejectionCode = "runtime_animator_not_ready";
                return false;
            }
            if (transientPresentationActive && !selectedInterruptible)
            {
                rejectionCode = "active_animation_not_interruptible";
                return false;
            }
            if (persistentPresentationActive)
            {
                rejectionCode = "persistent_animation_active";
                return false;
            }

            OntologyAnimationDefinition best = null;
            foreach (var definition in animationDatabase.Definitions)
            {
                if (!CanUse(definition) || !HasIntent(definition, intent)) continue;
                if (best == null || definition.priority > best.priority)
                    best = definition;
            }
            if (best == null || best.clip == null)
            {
                rejectionCode = "actor_animation_repertoire_missing";
                return false;
            }

            selectedAnimationId = best.animationId;
            selectedClipName = best.clip.name;
            selectedIntent = intent;
            ApplySelectedDefinitionMetadata(best, false);
            selectedFromOntologyIntent = true;
            selectedClip = best.clip;
            transientPresentationActive = true;
            // Every accepted Authority action is a new presentation occurrence.
            // Reusing the same canonical animation id must replay the clip rather
            // than treating it as an unchanged persistent state.
            replaySelectedClipRequested = true;
            transientPresentationEndsAt =
                Time.time + Mathf.Max(0.05f, best.clip.length);
            ApplySelectedClipPlayback();
            lastAuthorityPresentationDiagnostic =
                "playing:" + intent + ":" + best.animationId;
            return true;
        }

        private void OnDisable()
        {
            if (bootstrap != null)
            {
                bootstrap.WorldChanged -= RefreshFromWorld;
            }
            if (authorityClient != null)
            {
                authorityClient.CommandCompleted -= HandleAuthorityCommandCompleted;
                authorityClient.RuntimeActionCompleted -=
                    HandleAuthorityRuntimeActionCompleted;
                authorityClient.ProjectionReceived -= HandleAuthorityProjection;
            }
            ontologyInput?.SetAnimatorPresentationOwnership(false);
            ResetRuntimePresentationState();
        }

        /// <summary>
        /// Establishes a fresh presentation baseline from the already prepared
        /// local avatar. The caller must keep renderers and input gated until
        /// this method completes. No gameplay permission or durable Fact is
        /// created here.
        /// </summary>
        public void PrimeForRuntimeActivation()
        {
            ResetRuntimePresentationState();
            ontologyInput?.SetAnimatorPresentationOwnership(
                driveBaseLocomotionFromManifest);
            BindAuthorityClient();
            ResolveBaseIntent();
            RefreshFromWorld();
        }

        private void ResetRuntimePresentationState()
        {
            StopSelectedClipPlayback();
            stateResolver?.Reset();
            transientPresentationActive = false;
            transientPresentationEndsAt = 0f;
            persistentPresentationActive = false;
            persistentPresentationIntent = string.Empty;
            replaySelectedClipRequested = false;
            playingAnimationId = string.Empty;
            pendingAuthorityAnimationCommand = null;
            pendingAuthorityAnimationResult = null;
            pendingTransientIntent = string.Empty;
            pendingPresentationUntil = 0f;
            resolvedBaseIntent = string.Empty;
            resolvedBaseKind = OntologyAnimationStateKind.Idle;
            externalBaseIntent = string.Empty;
            equipmentIdleIntent = string.Empty;
            equipmentMoveIntent = string.Empty;
            selectedAnimationId = string.Empty;
            selectedClipName = string.Empty;
            selectedIntent = string.Empty;
            selectedClip = null;
            selectedFromOntologyIntent = false;
        }

        private void HandleAuthorityCommandCompleted(
            OntologyWorldCommand command,
            OntologyAuthorityCommandResult result)
        {
            if (!CanCommandRequestAuthorityPresentation(command) ||
                authorityIdentity == null ||
                !authorityIdentity.TryGetGuid(out var actorEntityId) ||
                !OntologyAuthorityActionAnimationIntentResolver
                    .IsExecuteActionForActor(
                        command,
                        actorEntityId.ToString("D")))
            {
                return;
            }

            if (TryPresentAuthorityAction(command, result))
            {
                return;
            }

            if (!ShouldQueueAuthorityPresentation(
                    result,
                    presentationReadinessGracePeriod))
            {
                return;
            }

            pendingAuthorityAnimationCommand = command;
            pendingAuthorityAnimationResult = result;
            pendingPresentationUntil =
                Time.unscaledTime + presentationReadinessGracePeriod;
        }

        public static bool CanCommandRequestAuthorityPresentation(
            OntologyWorldCommand command)
        {
            return command != null &&
                   string.Equals(
                       command.commandType,
                       OntologyWorldCommandKinds.ExecuteAction,
                       StringComparison.Ordinal);
        }

        private void BindAuthorityClient()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (authorityClient == null) return;

            authorityClient.CommandCompleted -= HandleAuthorityCommandCompleted;
            authorityClient.CommandCompleted += HandleAuthorityCommandCompleted;
            authorityClient.RuntimeActionCompleted -=
                HandleAuthorityRuntimeActionCompleted;
            authorityClient.RuntimeActionCompleted +=
                HandleAuthorityRuntimeActionCompleted;
            authorityClient.ProjectionReceived -= HandleAuthorityProjection;
            authorityClient.ProjectionReceived += HandleAuthorityProjection;
            RefreshEquipmentAnimationIntents(authorityClient.CurrentProjection);
        }

        private void HandleAuthorityRuntimeActionCompleted(
            OntologyAuthorityRuntimeActionResult result)
        {
            if (result == null ||
                !result.accepted ||
                authorityIdentity == null ||
                !authorityIdentity.TryGetGuid(out var actorEntityId) ||
                !string.Equals(
                    result.actorEntityId,
                    actorEntityId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(result.actorAnimationIntent))
            {
                return;
            }

            var intent = result.actorAnimationIntent.Trim();
            if (ShouldRouteAuthorityIntentToMotionStateResolver(
                    intent,
                    driveBaseLocomotionFromManifest,
                    animationDatabase))
            {
                // The accepted action is still the semantic source of the
                // jump occurrence. The collision-backed state resolver owns
                // JumpStart/Airborne/Fall/Landing playback so a generic
                // non-interruptible transient cannot outlive the physical
                // phase and block landing.
                ClearPendingAuthorityPresentation();
                lastAuthorityPresentationDiagnostic =
                    "motion_state_resolver_owns:" + intent;
                return;
            }

            if (TryPlayTransientIntent(intent, out var rejectionCode))
            {
                ClearPendingAuthorityPresentation();
                return;
            }

            lastAuthorityPresentationDiagnostic = rejectionCode;
            if (presentationReadinessGracePeriod <= 0f) return;
            pendingTransientIntent = intent;
            pendingPresentationUntil =
                Time.unscaledTime + presentationReadinessGracePeriod;
        }

        private void HandleAuthorityProjection(
            OntologyAuthorityWorldProjection projection)
        {
            if (RefreshEquipmentAnimationIntents(projection))
            {
                RefreshFromWorld();
            }
            RetryPendingAuthorityPresentation();
        }

        private bool TryPresentAuthorityAction(
            OntologyWorldCommand command,
            OntologyAuthorityCommandResult result)
        {
            if (authorityClient == null ||
                authorityIdentity == null ||
                !authorityIdentity.TryGetGuid(out var actorEntityId))
            {
                lastAuthorityPresentationDiagnostic =
                    "authority_context_missing";
                return false;
            }

            if (!OntologyAuthorityActionAnimationIntentResolver.TryResolveActorIntent(
                    command,
                    result,
                    authorityClient.CurrentProjection,
                    actorEntityId.ToString("D"),
                    out var intent))
            {
                lastAuthorityPresentationDiagnostic =
                    "authority_animation_intent_unresolved";
                return false;
            }

            if (ShouldRouteAuthorityIntentToMotionStateResolver(
                    intent,
                    driveBaseLocomotionFromManifest,
                    animationDatabase))
            {
                ClearPendingAuthorityPresentation();
                lastAuthorityPresentationDiagnostic =
                    "motion_state_resolver_owns:" + intent;
                return true;
            }

            if (TryPlayTransientIntent(intent, out var rejectionCode))
            {
                ClearPendingAuthorityPresentation();
                return true;
            }

            lastAuthorityPresentationDiagnostic = rejectionCode;
            pendingTransientIntent = intent;
            return false;
        }

        public static bool ShouldRouteAuthorityIntentToMotionStateResolver(
            string intent,
            bool stateResolverOwnsBaseLocomotion,
            OntologyAnimationDatabase database)
        {
            return stateResolverOwnsBaseLocomotion &&
                   database != null &&
                   database.IsIntentOwnedBy(
                       intent,
                       OntologyAnimationPresentationOwner
                           .MotionStateResolver);
        }

        private void RetryPendingAuthorityPresentation()
        {
            if (!ShouldRetryAuthorityPresentation(
                    pendingPresentationUntil,
                    Time.unscaledTime))
            {
                if (pendingPresentationUntil > 0f)
                {
                    Debug.LogWarning(
                        "[OntologyAnimation] Authority-approved presentation " +
                        "expired: " + lastAuthorityPresentationDiagnostic,
                        this);
                    ClearPendingAuthorityPresentation();
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingTransientIntent))
            {
                if (TryPlayTransientIntent(
                        pendingTransientIntent,
                        out var rejectionCode))
                {
                    ClearPendingAuthorityPresentation();
                }
                else
                {
                    lastAuthorityPresentationDiagnostic = rejectionCode;
                }
                return;
            }

            if (pendingAuthorityAnimationCommand != null &&
                pendingAuthorityAnimationResult != null)
            {
                TryPresentAuthorityAction(
                    pendingAuthorityAnimationCommand,
                    pendingAuthorityAnimationResult);
            }
        }

        public static bool ShouldQueueAuthorityPresentation(
            OntologyAuthorityCommandResult result,
            float gracePeriod)
        {
            return result != null &&
                   result.accepted &&
                   !result.isReplay &&
                   gracePeriod > 0f;
        }

        public static bool ShouldRetryAuthorityPresentation(
            float pendingUntil,
            float currentTime)
        {
            return pendingUntil > 0f && currentTime <= pendingUntil;
        }

        private void ClearPendingAuthorityPresentation()
        {
            pendingAuthorityAnimationCommand = null;
            pendingAuthorityAnimationResult = null;
            pendingTransientIntent = string.Empty;
            pendingPresentationUntil = 0f;
        }

        private bool RefreshEquipmentAnimationIntents(
            OntologyAuthorityWorldProjection projection)
        {
            var nextIdleIntent = string.Empty;
            var nextMoveIntent = string.Empty;
            if (authorityIdentity != null &&
                authorityIdentity.TryGetGuid(out var actorEntityId))
            {
                OntologyEquipmentAnimationIntentResolver.TryResolveIdleIntent(
                    projection,
                    actorEntityId.ToString("D"),
                    equipmentPresentationCatalog,
                    out nextIdleIntent);
                OntologyEquipmentAnimationIntentResolver.TryResolveMoveIntent(
                    projection,
                    actorEntityId.ToString("D"),
                    equipmentPresentationCatalog,
                    out nextMoveIntent);
            }

            if (string.Equals(equipmentIdleIntent, nextIdleIntent, StringComparison.Ordinal) &&
                string.Equals(equipmentMoveIntent, nextMoveIntent, StringComparison.Ordinal))
            {
                return false;
            }

            equipmentIdleIntent = nextIdleIntent;
            equipmentMoveIntent = nextMoveIntent;
            return true;
        }

        public void ConfigureEquipmentPresentation(
            OntologyCombatCatalog catalog)
        {
            equipmentPresentationCatalog = catalog;
            if (RefreshEquipmentAnimationIntents(authorityClient?.CurrentProjection))
            {
                RefreshFromWorld();
            }
        }

        private void SelectAnimationCandidate()
        {
            selectedAnimationId = string.Empty;
            selectedClipName = string.Empty;
            selectedIntent = string.Empty;
            selectedCanBlend = false;
            selectedFromOntologyIntent = false;
            selectedClip = null;

            if (animationDatabase == null || animationDatabase.Definitions == null)
            {
                return;
            }

            OntologyAnimationDefinition bestDefinition = null;
            string bestIntent = null;
            var foundFactIntent = false;
            var motionStateOwnsSelection =
                ShouldPrioritizeResolvedMotionState(
                    driveBaseLocomotionFromManifest,
                    ontologyInput != null,
                    resolvedBaseKind) &&
                !string.IsNullOrWhiteSpace(resolvedBaseIntent);
            if (motionStateOwnsSelection)
            {
                foreach (var definition in animationDatabase.Definitions)
                {
                    if (!CanUse(definition) ||
                        !HasIntent(definition, resolvedBaseIntent))
                    {
                        continue;
                    }

                    if (bestDefinition == null ||
                        definition.priority > bestDefinition.priority)
                    {
                        bestDefinition = definition;
                        bestIntent = resolvedBaseIntent;
                    }
                }
            }
            else
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    if (fact.Subject.ToString() != ActorId ||
                        fact.Predicate.ToString() != "animation_intent")
                    {
                        continue;
                    }

                    var intent = fact.Object.ToString();
                    foundFactIntent = true;
                    foreach (var definition in animationDatabase.Definitions)
                    {
                        if (!CanUse(definition) ||
                            !HasIntent(definition, intent))
                        {
                            continue;
                        }

                        if (bestDefinition == null ||
                            definition.priority > bestDefinition.priority)
                        {
                            bestDefinition = definition;
                            bestIntent = intent;
                        }
                    }
                }
            }

            var selectedFromRuntimeState = false;
            if (motionStateOwnsSelection)
            {
                selectedFromRuntimeState = true;
            }
            else if (!foundFactIntent && useDefaultIntentWhenNoFact)
            {
                var isMoving = IsMoving();
                var defaultIntent =
                    !string.IsNullOrWhiteSpace(externalBaseIntent)
                        ? externalBaseIntent
                        : driveBaseLocomotionFromManifest &&
                                    !string.IsNullOrWhiteSpace(resolvedBaseIntent)
                    ? resolvedBaseIntent
                    : isMoving
                        ? !string.IsNullOrWhiteSpace(equipmentMoveIntent)
                            ? equipmentMoveIntent
                            : defaultMoveIntent
                        : !string.IsNullOrWhiteSpace(equipmentIdleIntent)
                            ? equipmentIdleIntent
                            : defaultIdleIntent;
                selectedFromRuntimeState =
                    !string.IsNullOrWhiteSpace(externalBaseIntent) ||
                    driveBaseLocomotionFromManifest ||
                    (isMoving
                        ? !string.IsNullOrWhiteSpace(equipmentMoveIntent)
                        : !string.IsNullOrWhiteSpace(equipmentIdleIntent));
                foreach (var definition in animationDatabase.Definitions)
                {
                    if (!CanUse(definition) || !HasIntent(definition, defaultIntent))
                    {
                        continue;
                    }

                    if (bestDefinition == null || definition.priority > bestDefinition.priority)
                    {
                        bestDefinition = definition;
                        bestIntent = defaultIntent;
                    }
                }
            }

            if (bestDefinition == null)
            {
                return;
            }

            selectedAnimationId = bestDefinition.animationId;
            selectedClipName = bestDefinition.clip.name;
            selectedIntent = bestIntent;
            ApplySelectedDefinitionMetadata(bestDefinition, bestDefinition.loop);
            selectedFromOntologyIntent =
                foundFactIntent || selectedFromRuntimeState;
            selectedClip = bestDefinition.clip;
        }

        public static bool ShouldPrioritizeResolvedMotionState(
            bool stateResolverOwnsBaseLocomotion,
            bool hasLocalMotionObservation,
            OntologyAnimationStateKind resolvedKind)
        {
            if (!stateResolverOwnsBaseLocomotion ||
                !hasLocalMotionObservation)
            {
                return false;
            }

            return resolvedKind == OntologyAnimationStateKind.JumpStart ||
                   resolvedKind == OntologyAnimationStateKind.Airborne ||
                   resolvedKind == OntologyAnimationStateKind.Landing;
        }

        private void ResolveBaseIntent()
        {
            if (!string.IsNullOrWhiteSpace(externalBaseIntent))
            {
                resolvedBaseIntent = externalBaseIntent;
                resolvedBaseKind = OntologyAnimationStateKind.Idle;
                return;
            }

            if (stateResolver == null)
            {
                stateResolver = new OntologyAnimationStateResolver(
                    fallVelocityThreshold);
            }

            if (ontologyInput == null)
            {
                resolvedBaseIntent = IsMoving()
                    ? !string.IsNullOrWhiteSpace(equipmentMoveIntent)
                        ? equipmentMoveIntent
                        : defaultMoveIntent
                    : !string.IsNullOrWhiteSpace(equipmentIdleIntent)
                        ? equipmentIdleIntent
                        : defaultIdleIntent;
                resolvedBaseKind = IsMoving()
                    ? OntologyAnimationStateKind.Locomotion
                    : OntologyAnimationStateKind.Idle;
                return;
            }

            var resolved = stateResolver.Resolve(
                new OntologyAnimationStateSnapshot(
                    ontologyInput.IsMovingIntent,
                    ontologyInput.IsRunIntent,
                    ontologyInput.IsGrounded,
                    ontologyInput.VerticalVelocity,
                    equipmentIdleIntent,
                    equipmentMoveIntent,
                    actorProfile == null
                        ? defaultIdleIntent
                        : actorProfile.idleAnimationIntent,
                    actorProfile == null
                        ? defaultMoveIntent
                        : actorProfile.moveAnimationIntent,
                    actorProfile == null
                        ? OntologyAnimationIntentIds.FastLocomotion
                        : actorProfile.fastMoveAnimationIntent,
                    ontologyInput.JumpOccurrence,
                    selectedIntent,
                    IsCurrentBasePresentationCompleted()));
            resolvedBaseIntent = resolved.Intent;
            resolvedBaseKind = resolved.Kind;
        }

        private bool IsMoving()
        {
            if (ontologyInput != null)
            {
                return ontologyInput.IsMovingIntent;
            }

            return false;
        }

        private void ApplySelectedClipPlayback()
        {
            if ((!playSelectedClip && !transientPresentationActive) ||
                selectedClip == null ||
                !selectedFromOntologyIntent)
            {
                BeginReturnToController();
                return;
            }

            if (playingAnimationId == selectedAnimationId && playableGraph.IsValid())
            {
                if (replaySelectedClipRequested)
                {
                    replaySelectedClipRequested = false;
                    BeginClipToClipBlend();
                    return;
                }

                if (transitionMode == AnimationTransitionMode.ClipToController)
                {
                    transitionMode = AnimationTransitionMode.ControllerToClip;
                    transitionElapsed = 0f;
                }
                return;
            }

            replaySelectedClipRequested = false;
            if (playableGraph.IsValid() &&
                animationMixer.IsValid() &&
                clipMixer.IsValid() &&
                activeClipInput >= 0)
            {
                BeginClipToClipBlend();
                return;
            }

            StopSelectedClipPlayback();
            playableGraph = PlayableGraph.Create("OntologyAnimationAdapter_" + ActorId);
            var output = AnimationPlayableOutput.Create(playableGraph, "OntologyAnimation", animator);
            controllerPlayable = AnimatorControllerPlayable.Create(playableGraph, animator.runtimeAnimatorController);
            clipPlayable = AnimationClipPlayable.Create(playableGraph, selectedClip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            clipPlayable.SetDuration(GetPlaybackEndTime());
            clipPlayable.SetTime(GetPlaybackStartTime());
            clipPlayable.SetSpeed(1d);
            clipMixer = AnimationMixerPlayable.Create(
                playableGraph,
                2,
                true);
            animationMixer = AnimationLayerMixerPlayable.Create(
                playableGraph,
                2,
                false);
            playableGraph.Connect(controllerPlayable, 0, animationMixer, 0);
            playableGraph.Connect(clipPlayable, 0, clipMixer, 0);
            playableGraph.Connect(clipMixer, 0, animationMixer, 1);
            ConfigureAnimationLayer(1);
            clipMixer.SetInputWeight(0, 1f);
            clipMixer.SetInputWeight(1, 0f);
            animationMixer.SetInputWeight(
                0,
                selectedCanBlend ? 1f : 0f);
            animationMixer.SetInputWeight(
                1,
                selectedCanBlend ? 0f : 1f);
            output.SetSourcePlayable(animationMixer);
            playableGraph.Play();
            playingAnimationId = selectedAnimationId;
            activeClipInput = 0;
            transitionFromInput = 0;
            transitionToInput = 1;
            transitionMode = selectedCanBlend
                ? AnimationTransitionMode.ControllerToClip
                : AnimationTransitionMode.None;
            transitionElapsed = 0f;
        }

        private void BeginClipToClipBlend()
        {
            if (!clipMixer.IsValid())
            {
                StopSelectedClipPlayback();
                ApplySelectedClipPlayback();
                return;
            }

            var nextInput = activeClipInput == 0 ? 1 : 0;
            playableGraph.Disconnect(clipMixer, nextInput);
            var nextClip = AnimationClipPlayable.Create(playableGraph, selectedClip);
            nextClip.SetApplyFootIK(false);
            nextClip.SetApplyPlayableIK(false);
            nextClip.SetDuration(GetPlaybackEndTime());
            nextClip.SetTime(GetPlaybackStartTime());
            nextClip.SetSpeed(1d);
            playableGraph.Connect(nextClip, 0, clipMixer, nextInput);
            ConfigureAnimationLayer(1);
            animationMixer.SetInputWeight(0, 0f);
            animationMixer.SetInputWeight(1, 1f);
            clipMixer.SetInputWeight(activeClipInput, 1f);
            clipMixer.SetInputWeight(nextInput, 0f);
            clipPlayable = nextClip;
            playingAnimationId = selectedAnimationId;
            if (!selectedCanBlend)
            {
                clipMixer.SetInputWeight(activeClipInput, 0f);
                clipMixer.SetInputWeight(nextInput, 1f);
                activeClipInput = nextInput;
                transitionFromInput = -1;
                transitionToInput = -1;
                transitionMode = AnimationTransitionMode.None;
                transitionElapsed = 0f;
                return;
            }

            transitionFromInput = activeClipInput;
            transitionToInput = nextInput;
            transitionMode = AnimationTransitionMode.ClipToClip;
            transitionElapsed = 0f;
        }

        private void BeginReturnToController()
        {
            if (!playableGraph.IsValid())
            {
                return;
            }

            if (!animationMixer.IsValid() || !controllerPlayable.IsValid())
            {
                StopSelectedClipPlayback();
                return;
            }

            if (transitionMode != AnimationTransitionMode.ClipToController)
            {
                transitionFromInput = activeClipInput;
                transitionToInput = 0;
                transitionMode = AnimationTransitionMode.ClipToController;
                transitionElapsed = 0f;
            }
        }

        private void StopSelectedClipPlayback()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }

            playingAnimationId = string.Empty;
            replaySelectedClipRequested = false;
            activeClipInput = -1;
            transitionFromInput = -1;
            transitionToInput = -1;
            transitionMode = AnimationTransitionMode.None;
            transitionElapsed = 0f;
            animationMixer = default;
            clipMixer = default;
            controllerPlayable = default;
            clipPlayable = default;
            RestoreRootMotion();
        }

        private void LateUpdate()
        {
            ReconcileBasePresentationAfterMovement();
            if ((!playSelectedClip && !transientPresentationActive) ||
                !playableGraph.IsValid() ||
                !clipPlayable.IsValid())
            {
                return;
            }

            UpdateTransitionBlend();

            if (persistentPresentationActive &&
                selectedClip != null &&
                selectedClip.length > 0f)
            {
                try
                {
                    if (!clipPlayable.IsValid()) return;
                    if (clipPlayable.GetTime() >= selectedClip.length)
                    {
                        clipPlayable.SetTime(
                            Math.Max(
                                0d,
                                selectedClip.length -
                                1d / Math.Max(1d, selectedClip.frameRate)));
                        clipPlayable.SetSpeed(0d);
                    }
                }
                catch (ArgumentNullException)
                {
                    StopSelectedClipPlayback();
                }
                catch (InvalidOperationException)
                {
                    StopSelectedClipPlayback();
                }
                return;
            }

            if (selectedLoop && loopBlendableClips &&
                selectedClip != null && selectedClip.length > 0f)
            {
                // A graph can be torn down during an intent change between frames.
                // Treat an invalid native playable as a completed transition instead
                // of allowing GetTime() to raise and interrupt the player loop.
                try
                {
                    if (!clipPlayable.IsValid()) return;
                    var time = clipPlayable.GetTime();
                    if (time >= GetPlaybackEndTime())
                        clipPlayable.SetTime(GetPlaybackStartTime());
                }
                catch (ArgumentNullException)
                {
                    StopSelectedClipPlayback();
                }
                catch (InvalidOperationException)
                {
                    StopSelectedClipPlayback();
                }
            }
        }

        /// <summary>
        /// Player input and CharacterController movement are evaluated during
        /// Update. Re-read that approved presentation state in LateUpdate so
        /// script execution order cannot leave one frame of transform movement
        /// under an idle or completed transient clip. This selects presentation
        /// only; it does not grant locomotion or author a Fact.
        /// </summary>
        private void ReconcileBasePresentationAfterMovement()
        {
            if (transientPresentationActive &&
                ShouldReleaseTransientPresentation(
                    transientPresentationActive,
                    persistentPresentationActive,
                    selectedInterruptible,
                    IsMoving(),
                    Time.time,
                    transientPresentationEndsAt))
            {
                transientPresentationActive = false;
                ResolveBaseIntent();
                RefreshFromWorld();
                return;
            }

            if (transientPresentationActive ||
                !driveBaseLocomotionFromManifest ||
                ontologyInput == null)
            {
                return;
            }

            var previous = resolvedBaseIntent;
            ResolveBaseIntent();
            if (!string.Equals(
                    previous,
                    resolvedBaseIntent,
                    StringComparison.Ordinal))
            {
                RefreshFromWorld();
            }
        }

        private void UpdateTransitionBlend()
        {
            if (!animationMixer.IsValid())
            {
                return;
            }

            var duration = selectedTransitionDuration > 0f
                ? selectedTransitionDuration
                : Mathf.Max(0f, transitionBlendDuration);
            transitionElapsed = duration <= 0f ? duration : transitionElapsed + Time.deltaTime;
            var weight = duration <= 0f ? 1f : Mathf.Clamp01(transitionElapsed / duration);
            if (transitionMode == AnimationTransitionMode.ClipToController)
            {
                animationMixer.SetInputWeight(0, weight);
                animationMixer.SetInputWeight(1, 1f - weight);
                if (weight >= 1f)
                {
                    StopSelectedClipPlayback();
                }
            }
            else if (transitionMode == AnimationTransitionMode.ClipToClip)
            {
                if (!clipMixer.IsValid())
                {
                    StopSelectedClipPlayback();
                    return;
                }
                animationMixer.SetInputWeight(0, 0f);
                animationMixer.SetInputWeight(1, 1f);
                clipMixer.SetInputWeight(transitionFromInput, 1f - weight);
                clipMixer.SetInputWeight(transitionToInput, weight);
                if (weight >= 1f)
                {
                    activeClipInput = transitionToInput;
                    transitionMode = AnimationTransitionMode.None;
                }
            }
            else if (transitionMode == AnimationTransitionMode.ControllerToClip)
            {
                animationMixer.SetInputWeight(0, 1f - weight);
                animationMixer.SetInputWeight(1, weight);
                if (weight >= 1f)
                {
                    transitionMode = AnimationTransitionMode.None;
                }
            }
        }

        private static bool HasIntent(OntologyAnimationDefinition definition, string intent)
        {
            if (definition.intents == null)
            {
                return false;
            }

            foreach (var candidate in definition.intents)
            {
                if (candidate == intent)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanUse(OntologyAnimationDefinition definition)
        {
            if (definition == null || definition.clip == null)
            {
                return false;
            }
            if (actorProfile != null &&
                (!Matches(definition.actorTypes, actorProfile.actorType) ||
                 !Matches(definition.rigTypes, actorProfile.rigType)))
            {
                return false;
            }

            // Profile selection defines the actor's repertoire; ontology rules decide intent.
            if (HasRegisteredAnimationsInWorld())
            {
                if (bootstrap.World.HasFact(
                        ActorId,
                        OntologyPredicates.HasAnimation,
                        definition.animationId))
                {
                    return true;
                }

                if (definition.legacyAnimationIds != null)
                {
                    foreach (var legacyId in definition.legacyAnimationIds)
                    {
                        if (!string.IsNullOrWhiteSpace(legacyId) &&
                            bootstrap.World.HasFact(
                                ActorId,
                                OntologyPredicates.HasAnimation,
                                legacyId.Trim()))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (actorProfile != null)
            {
                if (actorProfile.HasAnimation(definition.animationId))
                {
                    return true;
                }

                if (definition.legacyAnimationIds != null)
                {
                    foreach (var legacyId in definition.legacyAnimationIds)
                    {
                        if (!string.IsNullOrWhiteSpace(legacyId) &&
                            actorProfile.HasAnimation(legacyId.Trim()))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            return true;
        }

        private static bool Matches(string[] constraints, string value)
        {
            if (constraints == null || constraints.Length == 0) return true;
            foreach (var constraint in constraints)
            {
                if (string.Equals(constraint, value, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void ApplySelectedDefinitionMetadata(
            OntologyAnimationDefinition definition,
            bool loop)
        {
            selectedCanBlend = definition != null && definition.canBlend;
            selectedInterruptible = definition == null || definition.interruptible;
            selectedLoop = definition != null && (definition.loop || loop);
            selectedTransitionDuration = definition == null
                ? transitionBlendDuration
                : Mathf.Max(0f, definition.transitionDuration);
            selectedHasContactWindow =
                definition != null && definition.hasContactWindow;
            selectedContactWindowStartNormalized = definition == null
                ? 0f
                : Mathf.Clamp01(
                    definition.contactWindowStartNormalized);
            selectedContactWindowEndNormalized = definition == null
                ? 1f
                : Mathf.Clamp01(
                    definition.contactWindowEndNormalized);
            selectedPlaybackStartNormalized = definition == null
                ? 0f
                : Mathf.Clamp01(
                    definition.playbackStartNormalized);
            selectedPlaybackEndNormalized =
                ResolvePlaybackEndNormalized(definition);
            selectedLayer = definition == null
                ? OntologyAnimationLayer.FullBody
                : definition.layer;
            selectedAvatarMask = definition?.avatarMask;
            selectedRootMotionMode = definition == null
                ? OntologyAnimationRootMotionMode.Inherit
                : definition.rootMotionMode;
            ApplyRootMotion();
        }

        private float GetPlaybackStartTime()
        {
            return selectedClip == null
                ? 0f
                : selectedClip.length *
                  Mathf.Clamp01(selectedPlaybackStartNormalized);
        }

        private float GetPlaybackEndTime()
        {
            return selectedClip == null
                ? 0f
                : selectedClip.length *
                  Mathf.Clamp(
                      selectedPlaybackEndNormalized,
                      selectedPlaybackStartNormalized,
                      1f);
        }

        private static float ResolvePlaybackEndNormalized(
            OntologyAnimationDefinition definition)
        {
            if (definition == null) return 1f;
            return definition.playbackEndNormalized <= 0f &&
                   definition.playbackStartNormalized <= 0f
                ? 1f
                : Mathf.Clamp01(definition.playbackEndNormalized);
        }

        private bool IsCurrentBasePresentationCompleted()
        {
            if (selectedClip == null ||
                selectedLoop ||
                !clipPlayable.IsValid())
            {
                return false;
            }

            try
            {
                var frameTolerance =
                    1f / Mathf.Max(1f, selectedClip.frameRate);
                return clipPlayable.GetTime() >=
                       Math.Max(
                           GetPlaybackStartTime(),
                           GetPlaybackEndTime() - frameTolerance);
            }
            catch (ArgumentNullException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void ConfigureAnimationLayer(int input)
        {
            if (!animationMixer.IsValid() || input < 1) return;
            animationMixer.SetLayerAdditive(
                (uint)input,
                selectedLayer == OntologyAnimationLayer.Additive);
            if (selectedAvatarMask != null)
                animationMixer.SetLayerMaskFromAvatarMask(
                    (uint)input,
                    selectedAvatarMask);
        }

        private void ApplyRootMotion()
        {
            if (animator == null ||
                selectedRootMotionMode == OntologyAnimationRootMotionMode.Inherit)
                return;
            if (!rootMotionCaptured)
            {
                originalApplyRootMotion = animator.applyRootMotion;
                rootMotionCaptured = true;
            }
            animator.applyRootMotion =
                selectedRootMotionMode == OntologyAnimationRootMotionMode.Enabled;
        }

        private void RestoreRootMotion()
        {
            if (!rootMotionCaptured || animator == null) return;
            animator.applyRootMotion = originalApplyRootMotion;
            rootMotionCaptured = false;
        }

        private bool HasRegisteredAnimationsInWorld()
        {
            if (bootstrap == null || bootstrap.World == null)
            {
                return false;
            }

            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() == ActorId &&
                    fact.Predicate.ToString() == OntologyPredicates.HasAnimation)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyBoolBindings()
        {
            if (boolBindings == null)
            {
                return;
            }

            if (boolParameterHashes == null || boolParameterHashes.Length != boolBindings.Length)
            {
                RebuildParameterCache();
            }

            for (var i = 0; i < boolBindings.Length; i++)
            {
                var binding = boolBindings[i];
                if (binding == null || boolParameterHashes[i] == 0)
                {
                    continue;
                }

                var value = bootstrap.World.HasFact(ActorId, binding.predicate, binding.obj);
                animator.SetBool(boolParameterHashes[i], value);
            }
        }

        private void RebuildParameterCache()
        {
            if (animator == null)
            {
                return;
            }

            if (boolBindings == null)
            {
                boolParameterHashes = Array.Empty<int>();
                return;
            }

            boolParameterHashes = new int[boolBindings.Length];
            for (var i = 0; i < boolBindings.Length; i++)
            {
                var binding = boolBindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.parameter))
                {
                    continue;
                }

                boolParameterHashes[i] = FindBoolParameterHash(binding.parameter);
            }
        }

        private int FindBoolParameterHash(string parameter)
        {
            foreach (var animatorParameter in animator.parameters)
            {
                if (animatorParameter.type != AnimatorControllerParameterType.Bool)
                {
                    continue;
                }

                if (animatorParameter.name == parameter)
                {
                    return animatorParameter.nameHash;
                }
            }

            return 0;
        }
    }

    [Serializable]
    public sealed class OntologyAnimatorBoolBinding
    {
        public string predicate;
        public string obj;
        public string parameter;
    }
}
