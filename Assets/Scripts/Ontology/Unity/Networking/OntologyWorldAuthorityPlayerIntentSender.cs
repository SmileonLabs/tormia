using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Optional bridge from the local controller to the authority's transient
    /// input channel. It observes intent only: local movement remains untouched
    /// until a later server-motion/reconciliation slice is explicitly enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityPlayerIntentSender : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField, Tooltip(
            "Optional canonical motion-intent transport. Leave empty to use the " +
            "project HTTP adapter; input never talks to a concrete transport.")]
        private MonoBehaviour intentTransportSource;
        [SerializeField, Tooltip(
            "Optional Authority motion snapshot feed for the latest approved " +
            "local state. Leave empty to create one on the Authority host.")]
        private MonoBehaviour motionSnapshotFeedSource;
        [SerializeField] private OntologyWorldZoneStreamer zoneStreamer;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField, Tooltip(
            "Sends transient input only, never world Facts. Disable the " +
            "component itself for offline preview; gameplay prediction does " +
            "not fail open when Authority is unavailable.")]
        private bool submitRuntimeIntents = true;
        [SerializeField, Min(1f)] private float submissionsPerSecond = 15f;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private bool avatarRegistered;
        [SerializeField] private bool locomotionApproved;
        [SerializeField] private long nextSequence = 1;
        [SerializeField] private long lastAcceptedSequence;
        [SerializeField] private long lastPredictionAffectingSequence;

        private bool isSubmitting;
        private bool submittingActiveIntent;
        private bool forceSubmitAfterInFlight;
        private bool sentActiveIntent;
        private float nextSubmitAt;
        private bool locomotionPreparationCompleted;
        private bool hasLocomotionContractFingerprint;
        private string locomotionContractFingerprint = string.Empty;
        private bool jumpRequestPending;
        private IPlayerMotionIntentTransport intentTransport;
        private IAuthorityMotionSnapshotFeed motionSnapshotFeed;
        private long intentTransportGeneration;
        private Vector2 lastAcceptedMove;
        private float lastAcceptedRequestedSpeed;
        private bool lastAcceptedHasDestination;
        private Vector2 lastAcceptedDestination;
        private float lastAcceptedDestinationStopDistance;

        public bool AvatarRegistered => avatarRegistered;
        public bool RequiresAuthorityLocomotionApproval =>
            submitRuntimeIntents;
        public bool CanPresentPredictedLocomotion =>
            submitRuntimeIntents &&
            authorityClient != null &&
            authorityClient.IsWorldRuntimeReady &&
            avatarRegistered &&
            locomotionApproved;
        public long LastAcceptedSequence => lastAcceptedSequence;
        public long LastPredictionAffectingSequence =>
            lastPredictionAffectingSequence;
        public string LastStatus => lastStatus ?? string.Empty;
        public event Action<long> AcceptedSequenceAdvanced;
        public bool HasActiveLocomotionIntent =>
            locomotionApproved && sentActiveIntent;
        public bool HasAcceptedLocomotionLease =>
            locomotionApproved && locomotionPreparationCompleted;

        private void SetAcceptedSequence(
            long sequence,
            bool predictionAffecting)
        {
            if (sequence <= lastAcceptedSequence)
            {
                return;
            }

            lastAcceptedSequence = sequence;
            if (!predictionAffecting ||
                sequence <= lastPredictionAffectingSequence)
            {
                return;
            }

            lastPredictionAffectingSequence = sequence;
            AcceptedSequenceAdvanced?.Invoke(sequence);
        }
        /// <summary>Called by the durable account-entry coordinator after it
        /// completes the first-time avatar placement and registration.</summary>
        public void SetAvatarRegistered(bool value)
        {
            avatarRegistered = value;
            if (!value)
            {
                intentTransportGeneration++;
                locomotionApproved = false;
                locomotionPreparationCompleted = false;
                lastAcceptedSequence = 0;
                lastPredictionAffectingSequence = 0;
                ClearLocomotionContractFingerprint();
                return;
            }

            CaptureCurrentLocomotionContract();
        }

        /// <summary>
        /// Performs the transient, zero-motion Authority handshake required
        /// before Unity presents local locomotion. World entry waits for this
        /// approval while the avatar is still hidden and input-gated, so the
        /// first visible frame cannot depend on the user's first movement
        /// sample.
        /// </summary>
        public IEnumerator PrepareLocomotionPresentationRoutine(
            string zoneKey,
            Action<bool> completed = null)
        {
            ResolveDependencies();
            var transport = ResolveIntentTransport();
            locomotionApproved = false;
            locomotionPreparationCompleted = false;

            if (!submitRuntimeIntents ||
                authorityClient == null ||
                transport == null ||
                !avatarRegistered ||
                string.IsNullOrWhiteSpace(zoneKey) ||
                !TryGetAvatarId(out var avatarId) ||
                !authorityClient.IsPlayerRuntimeActiveFor(avatarId) ||
                !TryResolveLocomotionAction(out var locomotionAction))
            {
                SetStatus(
                    "Authority locomotion preparation failed: the avatar, " +
                    "Zone, action Triple, or Rule Block is unavailable.");
                completed?.Invoke(false);
                yield break;
            }

            isSubmitting = true;
            var accepted = false;
            var sequence = nextSequence++;
            var callbackGeneration = intentTransportGeneration;
            var callbackRuntimeSessionId = authorityClient.ActiveRuntimeSessionId;
            yield return transport.SendPlayerIntentRoutine(
                avatarId,
                zoneKey,
                sequence,
                Vector2.zero,
                0f,
                false,
                Vector2.zero,
                0f,
                locomotionAction,
                result =>
                {
                    if (!IsCurrentIntentTransportCallback(
                            callbackGeneration,
                            callbackRuntimeSessionId,
                            requireActiveComponent: false))
                    {
                        SetStatus(
                            "authority_intent_callback_superseded: " +
                            "generation=" + callbackGeneration + "/" +
                            intentTransportGeneration + ", session=" +
                            (string.Equals(
                                callbackRuntimeSessionId,
                                authorityClient?.ActiveRuntimeSessionId,
                                StringComparison.OrdinalIgnoreCase)
                                ? "current"
                                : "changed") + ", avatar=" +
                            (avatarRegistered ? "registered" : "released"));
                        return;
                    }
                    accepted = result != null && result.accepted;
                    if (!accepted)
                    {
                        SetStatus(
                            "Authority locomotion preparation rejected: " +
                            (result?.rejectionCode ?? "unknown"));
                        return;
                    }

                    locomotionApproved = true;
                    SetAcceptedSequence(sequence, false);
                    sentActiveIntent = false;
                    nextSubmitAt =
                        Time.unscaledTime +
                        1f / Mathf.Max(1f, submissionsPerSecond);
                    CaptureCurrentLocomotionContract();
                    SetStatus(
                        "Authority locomotion preparation accepted (" +
                        sequence + ").");
                });

            isSubmitting = false;
            locomotionPreparationCompleted = accepted;
            if (accepted &&
                transport is OntologyWorldAuthorityUdpMotionTransport udp &&
                udp.IsUdpMotionWriterEnabled)
            {
                // Admission and promotion are transport-only optimization.
                // Failure leaves the Authority-confirmed HTTP writer active
                // and must not invalidate the locomotion gameplay contract.
                yield return udp.PrepareUdpAdmissionRoutine(avatarId);
            }
            completed?.Invoke(accepted);
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            intentTransportGeneration++;
            locomotionApproved = false;
            locomotionPreparationCompleted = false;
            isSubmitting = false;
            jumpRequestPending = false;
            lastAcceptedSequence = 0;
            lastPredictionAffectingSequence = 0;
            ClearLocomotionContractFingerprint();
        }

        /// <summary>
        /// Submits one non-durable jump intent. The input adapter supplies only
        /// the current support observation; the enabled action, assigned Rule
        /// Block, capability, physical profile, and takeoff speed remain
        /// Authority/project data.
        /// </summary>
        public bool TryRequestJump(bool groundedObservation)
        {
            ResolveDependencies();
            if (jumpRequestPending ||
                !groundedObservation ||
                authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                playerInput == null ||
                !TryGetAvatarId(out var avatarId) ||
                !TryResolveJumpAction(out var jumpAction) ||
                !TryResolveJumpTakeoffSpeed(out var takeoffSpeed))
            {
                return false;
            }

            jumpRequestPending = true;
            StartCoroutine(
                RequestJumpRoutine(
                    avatarId,
                    jumpAction,
                    takeoffSpeed,
                    groundedObservation));
            return true;
        }

        private IEnumerator RequestJumpRoutine(
            Guid avatarId,
            OntologyAuthorityActionDefinitionProjection jumpAction,
            float takeoffSpeed,
            bool groundedObservation)
        {
            OntologyAuthorityRuntimeActionResult result = null;
            yield return authorityClient.SendRuntimeActionRoutine(
                avatarId,
                avatarId,
                null,
                jumpAction,
                value => result = value,
                groundedObservation);
            jumpRequestPending = false;

            if (result == null || !result.accepted)
            {
                SetStatus(
                    "Authority jump rejected: " +
                    (result?.rejectionCode ?? "unknown"));
                yield break;
            }

            if (!playerInput.ApplyApprovedJump(takeoffSpeed))
            {
                SetStatus(
                    "Authority jump approval expired before local support " +
                    "presentation could consume it.");
                yield break;
            }

            SetStatus("Authority jump accepted.");
        }

        private void Update()
        {
            ResolveDependencies();
            if (!submitRuntimeIntents || authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady || !avatarRegistered || !TryGetAvatarId(out var avatarId) ||
                string.IsNullOrWhiteSpace(zoneStreamer == null ? string.Empty : zoneStreamer.ActiveZoneKey))
            {
                if (RequiresAuthorityLocomotionApproval &&
                    (authorityClient == null ||
                     !authorityClient.IsWorldRuntimeReady ||
                     !avatarRegistered))
                {
                    locomotionApproved = false;
                }
                return;
            }

            if (!TryResolveLocomotionAction(out var locomotionAction))
            {
                locomotionApproved = false;
                SetStatus(
                    "Authority locomotion unavailable: assign locomotion_action " +
                    "and its Rule Block.");
                return;
            }

            var move = Vector2.zero;
            var requestedSpeed = 0f;
            var hasDestination = false;
            var destination = Vector2.zero;
            var destinationStopDistance = 0f;
            var hasMove = playerInput != null &&
                          playerInput.TryGetWorldMoveIntent(
                              out move,
                              out requestedSpeed,
                              out hasDestination,
                              out destination,
                              out destinationStopDistance);
            if (!hasMove)
            {
                move = Vector2.zero;
            }

            if (isSubmitting)
            {
                if (!hasMove &&
                    (sentActiveIntent || submittingActiveIntent))
                {
                    forceSubmitAfterInFlight = true;
                }
                return;
            }

            var snapshotFeed = ResolveMotionSnapshotFeed();
            if (playerInput != null &&
                snapshotFeed != null &&
                snapshotFeed.TryGetLatestPlayerMotion(
                    avatarId,
                    1f,
                    out var latestMotion))
            {
                playerInput.TryCompleteAuthorityGroundDestination(
                    new Vector2(
                        (float)latestMotion.positionX,
                        (float)latestMotion.positionZ));
            }

            // A stop sample is sent once immediately. Continuing input is sampled
            // at the configured rate, while the server's short lease covers a
            // client crash or network loss without creating durable state.
            // A complete contract that was invalidated by a projection
            // transition revalidates itself with a zero-motion sample. Waiting
            // for the user's next key press would leave collision presentation
            // and animation in an unapproved intermediate state.
            var shouldSubmit = ShouldSubmitRuntimeIntent(
                locomotionPreparationCompleted,
                locomotionApproved,
                hasMove,
                sentActiveIntent,
                Time.unscaledTime,
                nextSubmitAt);
            if (!shouldSubmit)
            {
                return;
            }

            StartCoroutine(SubmitRoutine(
                move,
                requestedSpeed,
                hasDestination,
                destination,
                destinationStopDistance,
                hasMove,
                locomotionAction));
        }

        public static bool ShouldSubmitRuntimeIntent(
            bool initialPreparationCompleted,
            bool locomotionApproved,
            bool hasMove,
            bool sentActiveIntent,
            float now,
            float nextSubmitAt)
        {
            if (!initialPreparationCompleted)
                return false;

            // A zero-input sample is still an ephemeral locomotion lease. It
            // keeps the server's fixed-tick pose and the collision-resolved
            // observation channel current while the player is standing. A
            // client crash remains fail-closed through the registry TTL.
            var movementEdge =
                locomotionApproved && hasMove != sentActiveIntent;
            return movementEdge || now >= nextSubmitAt;
        }

        [ContextMenu("Register Current Player Avatar with Authority")]
        public void RegisterCurrentAvatar()
        {
            StartCoroutine(RegisterCurrentAvatarRoutine());
        }

        /// <summary>
        /// Registers the scene avatar before an account character is allowed to
        /// enter the selected world. Registration is durable ownership metadata;
        /// it does not create a Fact or send movement input.
        /// </summary>
        public IEnumerator RegisterCurrentAvatarRoutine(Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsWorldRuntimeReady || !TryGetAvatarId(out var avatarId))
            {
                SetStatus("Connect to authority and assign a stable avatar Entity GUID before registering.");
                completed?.Invoke(false);
                yield break;
            }

            var command = OntologyWorldAuthorityClient.CreateCommand(
                "register_player_avatar",
                OntologyWorldAuthorityClient.CreateRegisterPlayerAvatarPayload(avatarId));
            yield return authorityClient.SendCommandRoutine(command, result =>
            {
                avatarRegistered = result != null && result.accepted;
                SetStatus(avatarRegistered
                    ? "Authority accepted this player avatar."
                    : "Avatar registration rejected: " + (result?.rejectionCode ?? "unknown"));
                completed?.Invoke(avatarRegistered);
            });
        }

        private IEnumerator SubmitRoutine(
            Vector2 move,
            float requestedSpeed,
            bool hasDestination,
            Vector2 destination,
            float destinationStopDistance,
            bool active,
            OntologyAuthorityActionDefinitionProjection locomotionAction)
        {
            isSubmitting = true;
            submittingActiveIntent = active;
            if (!TryGetAvatarId(out var avatarId))
            {
                isSubmitting = false;
                submittingActiveIntent = false;
                yield break;
            }

            var zoneKey = zoneStreamer.ActiveZoneKey;
            var sequence = nextSequence++;
            var transport = ResolveIntentTransport();
            if (transport == null)
            {
                isSubmitting = false;
                submittingActiveIntent = false;
                SetStatus("Authority input transport is unavailable.");
                yield break;
            }
            var predictionAffecting =
                HasPredictionAffectingSampleChange(
                    active,
                    sentActiveIntent,
                    move,
                    requestedSpeed,
                    hasDestination,
                    destination,
                    destinationStopDistance,
                    lastAcceptedMove,
                    lastAcceptedRequestedSpeed,
                    lastAcceptedHasDestination,
                    lastAcceptedDestination,
                    lastAcceptedDestinationStopDistance);
            var callbackGeneration = intentTransportGeneration;
            var callbackRuntimeSessionId = authorityClient.ActiveRuntimeSessionId;
            yield return transport.SendPlayerIntentRoutine(
                avatarId,
                zoneKey,
                sequence,
                move,
                requestedSpeed,
                hasDestination,
                destination,
                destinationStopDistance,
                locomotionAction,
                result =>
                {
                    if (!IsCurrentIntentTransportCallback(
                            callbackGeneration,
                            callbackRuntimeSessionId))
                    {
                        return;
                    }
                    if (result == null || !result.accepted)
                    {
                        locomotionApproved = false;
                        nextSubmitAt =
                            Time.unscaledTime +
                            Mathf.Max(
                                0.25f,
                                1f / Mathf.Max(
                                    1f,
                                    submissionsPerSecond));
                        SetStatus("Authority input rejected: " + (result?.rejectionCode ?? "unknown"));
                        return;
                    }
                    locomotionApproved = true;
                    SetAcceptedSequence(
                        sequence,
                        predictionAffecting);
                    CaptureCurrentLocomotionContract();
                    sentActiveIntent = active;
                    lastAcceptedMove = move;
                    lastAcceptedRequestedSpeed = requestedSpeed;
                    lastAcceptedHasDestination = hasDestination;
                    lastAcceptedDestination = destination;
                    lastAcceptedDestinationStopDistance =
                        destinationStopDistance;
                    nextSubmitAt = Time.unscaledTime + 1f / Mathf.Max(1f, submissionsPerSecond);
                    SetStatus("Authority input accepted (" + sequence + ").");
                });
            isSubmitting = false;
            submittingActiveIntent = false;
            if (forceSubmitAfterInFlight)
            {
                forceSubmitAfterInFlight = false;
                nextSubmitAt = 0f;
            }
        }

        public static bool HasPredictionAffectingSampleChange(
            bool active,
            bool previouslyActive,
            Vector2 move,
            float requestedSpeed,
            bool hasDestination,
            Vector2 destination,
            float destinationStopDistance,
            Vector2 previousMove,
            float previousRequestedSpeed,
            bool previousHasDestination,
            Vector2 previousDestination,
            float previousDestinationStopDistance)
        {
            if (active != previouslyActive)
                return true;
            if (!active)
                return false;
            if (hasDestination || previousHasDestination)
            {
                return hasDestination != previousHasDestination ||
                       (destination - previousDestination).sqrMagnitude >
                           0.000001f ||
                       Mathf.Abs(
                           destinationStopDistance -
                           previousDestinationStopDistance) > 0.001f;
            }
            return (move - previousMove).sqrMagnitude > 0.000001f ||
                   Mathf.Abs(
                       requestedSpeed - previousRequestedSpeed) > 0.001f ||
                   hasDestination != previousHasDestination;
        }

        private bool TryGetAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            return avatarIdentity != null && avatarIdentity.TryGetGuid(out avatarId);
        }

        public bool TryResolveLocomotionAction(
            out OntologyAuthorityActionDefinitionProjection definition)
        {
            definition = null;
            if (authorityClient == null ||
                !TryGetAvatarId(out var avatarId))
            {
                return false;
            }

            var projection = authorityClient.CurrentProjection;
            if (!TryResolveLocomotionActionId(
                    projection,
                    avatarId,
                    out var actionId))
            {
                return false;
            }

            return authorityClient.TryResolveEnabledAction(
                actionId,
                out definition);
        }

        public bool TryResolveJumpAction(
            out OntologyAuthorityActionDefinitionProjection definition)
        {
            definition = null;
            if (authorityClient == null ||
                !TryGetAvatarId(out var avatarId) ||
                !TryResolveCanonicalActionId(
                    authorityClient.CurrentProjection,
                    avatarId,
                    OntologyPredicates.JumpAction,
                    out var actionId))
            {
                return false;
            }

            return authorityClient.TryResolveEnabledAction(
                actionId,
                out definition);
        }

        public static bool TryResolveLocomotionActionId(
            OntologyAuthorityWorldProjection projection,
            Guid avatarId,
            out string actionId)
        {
            actionId = null;
            var facts = projection?.facts;
            if (avatarId == Guid.Empty || facts == null) return false;
            var avatar = avatarId.ToString("D");
            foreach (var fact in facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        avatar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.LocomotionAction,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fact.objectKind,
                        "canonical",
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(
                        fact.objectCanonicalId))
                {
                    continue;
                }

                var candidate = fact.objectCanonicalId.Trim();
                if (actionId != null &&
                    !string.Equals(
                        actionId,
                        candidate,
                        StringComparison.Ordinal))
                {
                    actionId = null;
                    return false;
                }
                actionId = candidate;
            }
            return !string.IsNullOrWhiteSpace(actionId);
        }

        public static bool TryResolveCanonicalActionId(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicateId,
            out string actionId)
        {
            actionId = null;
            var facts = projection?.facts;
            if (entityId == Guid.Empty ||
                facts == null ||
                string.IsNullOrWhiteSpace(predicateId))
            {
                return false;
            }

            var entity = entityId.ToString("D");
            foreach (var fact in facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        entity,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fact.objectKind,
                        "canonical",
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(
                        fact.objectCanonicalId))
                {
                    continue;
                }

                var candidate = fact.objectCanonicalId.Trim();
                if (actionId != null &&
                    !string.Equals(
                        actionId,
                        candidate,
                        StringComparison.Ordinal))
                {
                    actionId = null;
                    return false;
                }
                actionId = candidate;
            }

            return !string.IsNullOrWhiteSpace(actionId);
        }

        public bool TryResolveLocomotionSpeed(
            bool sprint,
            out float speed)
        {
            speed = 0f;
            if (!TryGetAvatarId(out var avatarId))
                return false;
            var predicate = sprint
                ? OntologyPredicates.SprintSpeed
                : OntologyPredicates.MovementSpeed;
            return TryResolveNumberFact(
                authorityClient?.CurrentProjection,
                avatarId,
                predicate,
                out speed) &&
                speed > 0f;
        }

        public bool TryResolveGravityAcceleration(
            out float gravityAcceleration)
        {
            gravityAcceleration = 0f;
            if (!TryGetAvatarId(out var avatarId))
                return false;
            return TryResolveNumberFact(
                       authorityClient?.CurrentProjection,
                       avatarId,
                       OntologyPredicates.GravityAcceleration,
                       out gravityAcceleration) &&
                    gravityAcceleration < 0f;
        }

        public bool TryResolveJumpTakeoffSpeed(out float takeoffSpeed)
        {
            takeoffSpeed = 0f;
            if (!TryGetAvatarId(out var avatarId))
                return false;
            return TryResolveNumberFact(
                       authorityClient?.CurrentProjection,
                       avatarId,
                       OntologyPredicates.JumpTakeoffSpeed,
                       out takeoffSpeed) &&
                   takeoffSpeed > 0f;
        }

        public bool TryResolveGroundStickVelocity(
            out float groundStickVelocity)
        {
            groundStickVelocity = 0f;
            if (!TryGetAvatarId(out var avatarId))
                return false;
            return TryResolveNumberFact(
                       authorityClient?.CurrentProjection,
                       avatarId,
                       OntologyPredicates.GroundStickVelocity,
                       out groundStickVelocity) &&
                   groundStickVelocity <= 0f;
        }

        public static bool TryResolveNumberFact(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicateId,
            out float value)
        {
            value = 0f;
            if (projection?.facts == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicateId))
            {
                return false;
            }
            var entity = entityId.ToString("D");
            var matches = new List<string>();
            foreach (var fact in projection.facts)
            {
                if (fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        entity,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fact.objectKind,
                        "number",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(
                        fact.objectValueJson))
                {
                    var candidate = fact.objectValueJson.Trim();
                    if (!matches.Contains(candidate))
                        matches.Add(candidate);
                }
            }
            return matches.Count == 1 &&
                   float.TryParse(
                       matches[0],
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   float.IsFinite(value);
        }

        /// <summary>
        /// Builds the projection-owned portion of the local avatar's locomotion
        /// contract. Revisions, unrelated entities, and unrelated avatar
        /// semantics are deliberately excluded: combat, equipment, profile, or
        /// presentation changes must not suspend an already approved player
        /// locomotion sample. Only the authored facts, Rule Blocks, action
        /// identity, world, and Zone that participate in locomotion
        /// evaluation are included.
        /// </summary>
        public static bool TryCreateLocomotionContractFingerprint(
            OntologyAuthorityWorldProjection projection,
            Guid avatarId,
            out string fingerprint)
        {
            fingerprint = string.Empty;
            if (projection == null ||
                avatarId == Guid.Empty ||
                !TryResolveLocomotionActionId(
                    projection,
                    avatarId,
                    out var actionId))
            {
                return false;
            }

            var matchingActions =
                new List<OntologyAuthorityActionDefinitionProjection>();
            if (projection.actions != null)
            {
                foreach (var action in projection.actions)
                {
                    if (action != null &&
                        string.Equals(
                            action.actionId,
                            actionId,
                            StringComparison.Ordinal))
                    {
                        matchingActions.Add(action);
                    }
                }
            }
            if (matchingActions.Count != 1)
            {
                return false;
            }

            var avatar = avatarId.ToString("D");
            var facts = new List<string>();
            if (projection.facts != null)
            {
                foreach (var fact in projection.facts)
                {
                    if (fact == null ||
                        !string.Equals(
                            fact.subjectEntityId,
                            avatar,
                            StringComparison.OrdinalIgnoreCase) ||
                        !IsLocomotionContractFact(fact))
                    {
                        continue;
                    }

                    facts.Add(CreateCanonicalRecord(
                        fact.predicateId,
                        fact.objectKind,
                        fact.objectEntityId,
                        fact.objectCanonicalId,
                        fact.objectValueJson));
                }
            }
            facts.Sort(StringComparer.Ordinal);

            var bindings = new List<string>();
            if (projection.ruleBindings != null)
            {
                foreach (var binding in projection.ruleBindings)
                {
                    if (binding == null ||
                        !string.Equals(
                            binding.targetEntityId,
                            avatar,
                            StringComparison.OrdinalIgnoreCase) ||
                        !IsLocomotionContractRule(binding.ruleId))
                    {
                        continue;
                    }

                    bindings.Add(CreateCanonicalRecord(
                        binding.ruleId,
                        binding.ruleVersion.ToString(),
                        binding.enabled ? "1" : "0",
                        binding.parameterValuesJson));
                }
            }
            bindings.Sort(StringComparer.Ordinal);

            var selectedAction = matchingActions[0];
            var builder = new StringBuilder();
            AppendCanonicalPart(builder, projection.worldId);
            AppendCanonicalPart(builder, projection.scopeZoneKey);
            AppendCanonicalPart(builder, avatar);
            AppendCanonicalPart(builder, actionId);
            AppendCanonicalPart(builder, selectedAction.packageId);
            AppendCanonicalPart(builder, selectedAction.packageVersion);
            AppendCanonicalPart(
                builder,
                selectedAction.definitionVersion.ToString());
            AppendCanonicalPart(
                builder,
                selectedAction.actorAnimationIntent);
            AppendCanonicalRecords(builder, facts);
            AppendCanonicalRecords(builder, bindings);
            fingerprint = builder.ToString();
            return true;
        }

        private static bool IsLocomotionContractFact(
            OntologyAuthorityFactProjection fact)
        {
            if (fact == null)
                return false;

            switch (fact.predicateId)
            {
                case OntologyPredicates.LocomotionAction:
                case OntologyPredicates.MovementSpeed:
                case OntologyPredicates.SprintSpeed:
                case OntologyPredicates.PhysicalProfile:
                case OntologyPredicates.IsAlive:
                case OntologyPredicates.GravityAcceleration:
                    return true;
                case OntologyPredicates.GrantsCapability:
                    return HasCanonicalObject(fact, "Locomotion");
                case OntologyPredicates.HasConcept:
                    return HasCanonicalObject(
                               fact,
                               OntologyConcepts.Actor) ||
                           HasCanonicalObject(
                               fact,
                               OntologyConcepts.PlayerControlled);
                case OntologyPredicates.HasRuleBlock:
                    return HasCanonicalObject(
                        fact,
                        OntologyRuleBlocks.MovePlayerFromIntent);
                default:
                    return false;
            }
        }

        private static bool HasCanonicalObject(
            OntologyAuthorityFactProjection fact,
            string canonicalId)
        {
            return fact != null &&
                   string.Equals(
                       fact.objectKind,
                       "canonical",
                       StringComparison.Ordinal) &&
                   string.Equals(
                       fact.objectCanonicalId,
                       canonicalId,
                       StringComparison.Ordinal);
        }

        private static bool IsLocomotionContractRule(string ruleId)
        {
            return string.Equals(
                ruleId,
                OntologyRuleBlocks.MovePlayerFromIntent,
                StringComparison.Ordinal);
        }

        public static bool ShouldInvalidateLocomotionApproval(
            bool hasPreviousFingerprint,
            string previousFingerprint,
            bool hasNextFingerprint,
            string nextFingerprint)
        {
            return !hasPreviousFingerprint ||
                   !hasNextFingerprint ||
                   !string.Equals(
                       previousFingerprint,
                       nextFingerprint,
                       StringComparison.Ordinal);
        }

        private void HandleProjectionReceived(
            OntologyAuthorityWorldProjection projection)
        {
            var hadPrevious = hasLocomotionContractFingerprint;
            var previous = locomotionContractFingerprint;
            var next = string.Empty;
            var hasNext =
                TryGetAvatarId(out var avatarId) &&
                TryCreateLocomotionContractFingerprint(
                    projection,
                    avatarId,
                    out next);

            if (ShouldInvalidateLocomotionApproval(
                    hadPrevious,
                    previous,
                    hasNext,
                    next))
            {
                // A changed or incomplete avatar contract must be re-evaluated
                // by Authority. An unrelated entity revision preserves the
                // already-approved transient locomotion lease.
                locomotionApproved = false;
            }

            hasLocomotionContractFingerprint = hasNext;
            locomotionContractFingerprint =
                hasNext ? next : string.Empty;
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
                if (isActiveAndEnabled) Subscribe();
            }
            if (zoneStreamer == null)
            {
                zoneStreamer = FindAnyObjectByType<OntologyWorldZoneStreamer>();
            }
            if (playerInput == null)
            {
                playerInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>();
            }
            if (avatarIdentity == null && playerInput != null)
            {
                avatarIdentity = playerInput.GetComponent<OntologyAuthorityEntityIdentity>();
            }
        }

        private IPlayerMotionIntentTransport ResolveIntentTransport()
        {
            if (intentTransportSource != null)
            {
                var assignedTransport = intentTransportSource as
                    IPlayerMotionIntentTransport;
                if (assignedTransport == null)
                {
                    return null;
                }
                SetIntentTransport(assignedTransport);
                return intentTransport;
            }

            var adapter = GetComponent<OntologyWorldAuthorityHttpMotionTransport>();
            if (adapter == null)
            {
                adapter = gameObject.AddComponent<
                    OntologyWorldAuthorityHttpMotionTransport>();
            }
            adapter.Configure(authorityClient);
            var settings = authorityClient?.Settings;
            if (settings != null &&
                settings.enableExperimentalUdpMotionTransport)
            {
                var udp = GetComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                if (udp == null)
                    udp = gameObject.AddComponent<
                        OntologyWorldAuthorityUdpMotionTransport>();
                udp.Configure(authorityClient, adapter);
                udp.ConfigureEndpoint(
                    settings.ResolveUdpHost(Application.platform),
                    settings.udpPort, true, true);
                intentTransportSource = udp;
                SetIntentTransport(udp);
                return intentTransport;
            }
            intentTransportSource = adapter;
            SetIntentTransport(adapter);
            return intentTransport;
        }

        private IAuthorityMotionSnapshotFeed ResolveMotionSnapshotFeed()
        {
            if (motionSnapshotFeedSource != null)
            {
                var assignedFeed = motionSnapshotFeedSource as
                    IAuthorityMotionSnapshotFeed;
                if (assignedFeed == null)
                {
                    motionSnapshotFeed = null;
                    return null;
                }
                motionSnapshotFeed = assignedFeed;
                return motionSnapshotFeed;
            }

            if (authorityClient == null)
            {
                return null;
            }

            var feed = authorityClient.GetComponent<
                OntologyWorldAuthorityMotionSnapshotFeed>();
            if (feed == null)
            {
                feed = authorityClient.gameObject.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
            }
            feed.Configure(
                authorityClient,
                authorityClient.GetComponent<
                    OntologyWorldAuthorityRealtimeClient>());
            motionSnapshotFeedSource = feed;
            motionSnapshotFeed = feed;
            return motionSnapshotFeed;
        }

        private void SetIntentTransport(
            IPlayerMotionIntentTransport value)
        {
            if (ReferenceEquals(intentTransport, value))
            {
                return;
            }

            intentTransport = value;
            intentTransportGeneration++;
        }

        private bool IsCurrentIntentTransportCallback(
            long callbackGeneration,
            string callbackRuntimeSessionId,
            bool requireActiveComponent = true)
        {
            // The account-entry coordinator deliberately performs its
            // zero-motion handshake while gameplay presentation is gated.
            // A disabled presenter must not discard that Authority response;
            // ordinary live input callbacks still require an active sender.
            if ((requireActiveComponent && !isActiveAndEnabled) ||
                callbackGeneration != intentTransportGeneration ||
                authorityClient == null ||
                !avatarRegistered)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(callbackRuntimeSessionId) ||
                   string.Equals(
                       callbackRuntimeSessionId,
                       authorityClient.ActiveRuntimeSessionId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void Subscribe()
        {
            if (authorityClient == null) return;
            authorityClient.ProjectionReceived -=
                HandleProjectionReceived;
            authorityClient.ProjectionReceived +=
                HandleProjectionReceived;
            CaptureCurrentLocomotionContract();
        }

        private void Unsubscribe()
        {
            if (authorityClient == null) return;
            authorityClient.ProjectionReceived -=
                HandleProjectionReceived;
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
        }

        private void CaptureCurrentLocomotionContract()
        {
            if (authorityClient == null ||
                !TryGetAvatarId(out var avatarId) ||
                !TryCreateLocomotionContractFingerprint(
                    authorityClient.CurrentProjection,
                    avatarId,
                    out var fingerprint))
            {
                return;
            }

            hasLocomotionContractFingerprint = true;
            locomotionContractFingerprint = fingerprint;
        }

        private void ClearLocomotionContractFingerprint()
        {
            hasLocomotionContractFingerprint = false;
            locomotionContractFingerprint = string.Empty;
        }

        private static string CreateCanonicalRecord(
            params string[] values)
        {
            var builder = new StringBuilder();
            if (values != null)
            {
                foreach (var value in values)
                {
                    AppendCanonicalPart(builder, value);
                }
            }
            return builder.ToString();
        }

        private static void AppendCanonicalRecords(
            StringBuilder builder,
            IEnumerable<string> records)
        {
            if (records == null)
            {
                AppendCanonicalPart(builder, null);
                return;
            }

            foreach (var record in records)
            {
                AppendCanonicalPart(builder, record);
            }
        }

        private static void AppendCanonicalPart(
            StringBuilder builder,
            string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
        }
    }
}
