using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Publishes the local CharacterController pose as ephemeral observation
    /// evidence after Unity presentation collision resolves it. The historical
    /// class name is retained for scene-reference compatibility. The server
    /// never adopts this pose as authoritative motion state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityPlayerMotionReconciler :
        MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldZoneStreamer zoneStreamer;
        [SerializeField]
        private OntologyWorldAuthorityPlayerIntentSender intentSender;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField]
        private OntologySwimmingMovementAdapter swimmingMovement;
        [SerializeField]
        private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField, Tooltip(
            "Publishes collision-resolved local poses as ephemeral observation " +
            "samples. These samples never own Authority motion.")]
        private bool publishCollisionResolvedPose = true;
        [SerializeField, Min(1f)] private float publicationsPerSecond = 10f;
        [Header("Authority prediction reconciliation")]
        [SerializeField, Tooltip(
            "Reads the local avatar's Authority motion snapshot and queues only " +
            "a planar correction into the exclusive motion coordinator.")]
        private bool reconcileAuthorityPlanarMotion = true;
        [SerializeField, Min(1f)] private float snapshotsPerSecond = 10f;
        [SerializeField, Min(0f)] private float planarDeadZone = 0.08f;
        [SerializeField, Min(0.01f)]
        private float maximumCorrectionSpeed = 2.5f;
        [SerializeField, Min(0f)]
        private float maximumSnapshotExtrapolationSeconds = 0.15f;
        [SerializeField, Min(0.1f)]
        private float maximumSnapshotAgeSeconds = 1f;
        [SerializeField, Min(0.1f), Tooltip(
            "Bounds a single queued residual without changing the Authority " +
            "target. Repeated newer snapshots still converge smoothly.")]
        private float maximumQueuedCorrectionDistance = 4f;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private long lastPoseSequence;
        [SerializeField] private long lastObservedServerTick;

        private bool isPublishing;
        private bool isLoadingSnapshot;
        private bool checkpointReseedPending;
        private bool runtimeWasReady;
        private float nextPublishAt;
        private float nextSnapshotAt;
        private OntologyCharacterMotionCoordinator motionCoordinator;

        public bool CheckpointReseedPending => checkpointReseedPending;
        public long LastPoseSequence => lastPoseSequence;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            var ready = CanUseRuntime();
            if (!ready)
            {
                runtimeWasReady = false;
                motionCoordinator?.ClearAuthorityCorrection();
                return;
            }
            if (!runtimeWasReady)
            {
                runtimeWasReady = true;
                lastPoseSequence = 0;
                nextPublishAt = 0f;
                nextSnapshotAt = 0f;
                lastObservedServerTick = 0;
            }

            if (publishCollisionResolvedPose &&
                !isPublishing &&
                !checkpointReseedPending &&
                Time.unscaledTime >= nextPublishAt)
            {
                StartCoroutine(PublishPoseRoutine());
            }

            if (reconcileAuthorityPlanarMotion &&
                !isLoadingSnapshot &&
                !checkpointReseedPending &&
                Time.unscaledTime >= nextSnapshotAt)
            {
                StartCoroutine(LoadAuthoritySnapshotRoutine());
            }
        }

        /// <summary>
        /// Stops runtime pose publication while a recovery adapter commits a
        /// durable checkpoint. No stale runtime sample can overwrite the
        /// reseeded pose during that transaction.
        /// </summary>
        public void BeginCheckpointReseed()
        {
            checkpointReseedPending = true;
            motionCoordinator?.ClearAuthorityCorrection();
        }

        public void CompleteCheckpointReseed(
            bool accepted,
            Vector3 confirmedPosition)
        {
            checkpointReseedPending = false;
            lastPoseSequence = 0;
            nextPublishAt = 0f;
            nextSnapshotAt = 0f;
            lastObservedServerTick = 0;
            motionCoordinator?.ClearAuthorityCorrection();
            lastStatus = accepted
                ? "Collision-resolved pose publisher reseeded."
                : "Collision-resolved pose publisher checkpoint rejected.";
        }

        private IEnumerator PublishPoseRoutine()
        {
            isPublishing = true;
            nextPublishAt =
                Time.unscaledTime +
                1f / Mathf.Max(1f, publicationsPerSecond);

            if (!TryGetAvatarId(out var avatarId))
            {
                isPublishing = false;
                yield break;
            }

            var poseSequence = ++lastPoseSequence;
            OntologyAuthorityRuntimeIntentResult result = null;
            yield return authorityClient.SendResolvedPlayerPoseRoutine(
                avatarId,
                zoneStreamer.ActiveZoneKey,
                intentSender.LastAcceptedSequence,
                poseSequence,
                transform.position,
                ResolveMotionStatus(),
                value => result = value);
            lastStatus =
                result != null && result.accepted
                    ? "Collision-resolved pose accepted (" +
                      poseSequence + ")."
                    : "Collision-resolved pose rejected: " +
                      (result?.rejectionCode ?? "unknown");
            isPublishing = false;
        }

        private IEnumerator LoadAuthoritySnapshotRoutine()
        {
            isLoadingSnapshot = true;
            nextSnapshotAt =
                Time.unscaledTime +
                1f / Mathf.Max(1f, snapshotsPerSecond);
            if (!TryGetAvatarId(out var avatarId))
            {
                isLoadingSnapshot = false;
                yield break;
            }

            OntologyAuthorityPlayerMotionState state = null;
            yield return authorityClient.LoadPlayerMotionRoutine(
                avatarId,
                value => state = value);
            if (state != null &&
                state.serverTick > lastObservedServerTick &&
                string.Equals(
                    state.avatarEntityId,
                    avatarId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    state.zoneKey,
                    zoneStreamer.ActiveZoneKey,
                    StringComparison.Ordinal) &&
                TryResolvePlanarCorrection(
                    state,
                    transform.position,
                    maximumSnapshotExtrapolationSeconds,
                    maximumSnapshotAgeSeconds,
                    maximumQueuedCorrectionDistance,
                    out var correction))
            {
                lastObservedServerTick = state.serverTick;
                if (motionCoordinator != null &&
                    motionCoordinator.QueueAuthorityPlanarCorrection(
                        correction,
                        state.serverTick,
                        maximumCorrectionSpeed,
                        planarDeadZone))
                {
                    lastStatus =
                        "Authority planar snapshot queued (" +
                        state.serverTick + ").";
                }
            }
            isLoadingSnapshot = false;
        }

        public static bool TryResolvePlanarCorrection(
            OntologyAuthorityPlayerMotionState state,
            Vector3 localPosition,
            float maximumExtrapolationSeconds,
            float maximumAgeSeconds,
            float maximumQueuedDistance,
            out Vector3 correction)
        {
            correction = Vector3.zero;
            if (state == null ||
                state.serverTick <= 0 ||
                !IsFinite(localPosition) ||
                !double.IsFinite(state.positionX) ||
                !double.IsFinite(state.positionZ) ||
                !double.IsFinite(state.velocityX) ||
                !double.IsFinite(state.velocityZ) ||
                !float.IsFinite(maximumExtrapolationSeconds) ||
                !float.IsFinite(maximumAgeSeconds) ||
                !float.IsFinite(maximumQueuedDistance) ||
                maximumExtrapolationSeconds < 0f ||
                maximumAgeSeconds <= 0f ||
                maximumQueuedDistance <= 0f)
            {
                return false;
            }

            var now =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ageSeconds =
                (now - state.updatedAtUnixMilliseconds) / 1000d;
            if (!double.IsFinite(ageSeconds) ||
                ageSeconds < -0.25d ||
                ageSeconds > maximumAgeSeconds)
            {
                return false;
            }
            var extrapolation = Math.Min(
                Math.Max(0d, ageSeconds),
                maximumExtrapolationSeconds);
            var target = new Vector3(
                (float)(state.positionX +
                        state.velocityX * extrapolation),
                localPosition.y,
                (float)(state.positionZ +
                        state.velocityZ * extrapolation));
            if (!IsFinite(target))
            {
                return false;
            }

            correction = Vector3.ProjectOnPlane(
                target - localPosition,
                Vector3.up);
            correction = Vector3.ClampMagnitude(
                correction,
                maximumQueuedDistance);
            return true;
        }

        private string ResolveMotionStatus()
        {
            if (swimmingMovement != null &&
                swimmingMovement.IsSwimmingPresentationActive)
            {
                return "swimming";
            }
            if (playerInput != null && !playerInput.IsGrounded)
            {
                return "airborne";
            }
            return playerInput != null &&
                   playerInput.TryGetWorldMoveIntent(out _, out _)
                ? "moving"
                : "idle";
        }

        private bool CanUseRuntime()
        {
            return authorityClient != null &&
                   authorityClient.IsWorldRuntimeReady &&
                   intentSender != null &&
                   intentSender.AvatarRegistered &&
                   intentSender.LastAcceptedSequence > 0 &&
                   avatarIdentity != null &&
                   avatarIdentity.TryGetGuid(out _) &&
                   zoneStreamer != null &&
                   !string.IsNullOrWhiteSpace(
                       zoneStreamer.ActiveZoneKey) &&
                   OntologyMotionDriverAdapter.Allows(
                       this,
                       OntologyMotionDriver.LocalCharacterController);
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        private bool TryGetAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            return avatarIdentity != null &&
                   avatarIdentity.TryGetGuid(out avatarId);
        }

        private void ResolveDependencies()
        {
            authorityClient ??=
                FindAnyObjectByType<OntologyWorldAuthorityClient>();
            zoneStreamer ??=
                FindAnyObjectByType<OntologyWorldZoneStreamer>();
            intentSender ??=
                FindAnyObjectByType<
                    OntologyWorldAuthorityPlayerIntentSender>();
            playerInput ??=
                GetComponent<OntologyInputSystemPlayerInput>();
            swimmingMovement ??=
                GetComponent<OntologySwimmingMovementAdapter>();
            avatarIdentity ??=
                GetComponent<OntologyAuthorityEntityIdentity>();
            motionCoordinator ??=
                GetComponent<OntologyCharacterMotionCoordinator>();
        }
    }
}
