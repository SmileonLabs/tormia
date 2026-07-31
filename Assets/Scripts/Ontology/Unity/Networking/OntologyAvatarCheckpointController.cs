using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Persists coarse player checkpoints through revisioned Authority commands.
    /// Per-frame motion remains ephemeral and is never converted into DB events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAvatarCheckpointController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private OntologyWorldEntryGroundingAdapter groundingAdapter;
        [SerializeField] private OntologyDrowningRecoveryAdapter drowningRecovery;
        [SerializeField] private OntologyWorldAuthorityPlayerMotionReconciler motionReconciler;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private OntologyWaterPresenceSensor waterPresenceSensor;
        [SerializeField] private CharacterController characterController;
        [SerializeField, Min(5f)] private float saveIntervalSeconds = 30f;
        [SerializeField, Min(0.1f)] private float minimumMovedDistance = 2f;
        [SerializeField] private bool saveOnApplicationPause = true;

        private Vector3 lastSavedPosition;
        private Quaternion lastSavedRotation;
        private float nextSaveAt;
        private bool hasSavedPosition;
        private bool saveInProgress;
        private bool recoverySaveScheduled;
        private Vector3 pendingRecoveryPosition;
        private Quaternion pendingRecoveryRotation;
        private bool restoredPoseConfirmationPending;
        private Vector3 restoredCheckpointPosition;
        private Quaternion restoredCheckpointRotation;

        public bool SaveInProgress => saveInProgress;
        public bool HasConfirmedRespawnAnchor => hasSavedPosition;
        public bool RestoredPoseConfirmationPending =>
            restoredPoseConfirmationPending;

        private void Awake() => ResolveDependencies();

        private void Update()
        {
            ResolveDependencies();
            if (sessionCoordinator == null || !sessionCoordinator.IsInWorld ||
                authorityClient == null || !authorityClient.IsWorldRuntimeReady ||
                avatarIdentity == null || saveInProgress ||
                Time.unscaledTime < nextSaveAt)
            {
                return;
            }

            nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            var position = avatarIdentity.transform.position;
            if (!CanCaptureCurrentCheckpoint())
            {
                return;
            }
            if (hasSavedPosition &&
                Vector3.Distance(position, lastSavedPosition) < minimumMovedDistance)
            {
                return;
            }

            StartCoroutine(SaveRoutine(null));
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && saveOnApplicationPause && isActiveAndEnabled && !saveInProgress)
                StartCoroutine(SaveRoutine(null));
        }

        public IEnumerator RestoreRoutine(Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || avatarIdentity == null ||
                !avatarIdentity.TryGetGuid(out var avatarId))
            {
                completed?.Invoke(false);
                yield break;
            }

            OntologyAuthorityAvatarCheckpoint checkpoint = null;
            yield return authorityClient.LoadAvatarCheckpointRoutine(
                avatarId, value => checkpoint = value);

            motionReconciler?.BeginCheckpointReseed();

            // A missing checkpoint is a valid first entry. The durable entity
            // projection is also a recovery path when the dedicated checkpoint
            // read is unavailable because the checkpoint command updates both.
            // This controller restores durable data only. Unity collision
            // preparation belongs to the world-entry presentation coordinator.
            if (checkpoint?.transform == null)
            {
                TryRestoreFromWorldProjection(avatarId);
                StageRestoredPose(
                    avatarIdentity.transform.position,
                    avatarIdentity.transform.rotation);
                completed?.Invoke(true);
                yield break;
            }

            var value = checkpoint.transform;
            avatarIdentity.transform.SetPositionAndRotation(
                new Vector3(value.positionX, value.positionY, value.positionZ),
                Quaternion.Euler(value.rotationX, value.rotationY, value.rotationZ));
            if (!string.IsNullOrWhiteSpace(checkpoint.zoneKey))
                authorityClient.SetProjectionZoneKey(checkpoint.zoneKey);
            StageRestoredPose(
                avatarIdentity.transform.position,
                avatarIdentity.transform.rotation);
            completed?.Invoke(true);
        }

        /// <summary>
        /// Confirms the final pose after Unity collision preparation has
        /// completed. Any grounding correction is persisted through one
        /// revisioned Authority checkpoint; no presentation rule lives here.
        /// </summary>
        public IEnumerator ConfirmRestoredPoseRoutine(
            Action<bool> completed = null)
        {
            ResolveDependencies();
            if (!restoredPoseConfirmationPending ||
                avatarIdentity == null ||
                authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady)
            {
                completed?.Invoke(false);
                yield break;
            }

            var finalPosition = avatarIdentity.transform.position;
            var finalRotation = avatarIdentity.transform.rotation;
            var accepted = true;
            if (Vector3.Distance(
                    restoredCheckpointPosition,
                    finalPosition) > 0.001f ||
                Quaternion.Angle(
                    restoredCheckpointRotation,
                    finalRotation) > 0.01f)
            {
                accepted = false;
                yield return SavePoseRoutine(
                    finalPosition,
                    finalRotation,
                    value => accepted = value);
            }

            if (accepted)
            {
                ConfirmRespawnAnchor(finalPosition, finalRotation);
                nextSaveAt =
                    Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            }
            motionReconciler?.CompleteCheckpointReseed(
                accepted,
                finalPosition);
            restoredPoseConfirmationPending = false;
            completed?.Invoke(accepted);
        }

        public void CancelPendingRestore()
        {
            restoredPoseConfirmationPending = false;
            motionReconciler?.CompleteCheckpointReseed(
                false,
                avatarIdentity == null
                    ? Vector3.zero
                    : avatarIdentity.transform.position);
        }

        private bool TryRestoreFromWorldProjection(Guid avatarId)
        {
            var entities = authorityClient?.CurrentProjection?.entities;
            if (entities == null)
                return false;

            var canonicalAvatarId = avatarId.ToString("D");
            for (var index = 0; index < entities.Length; index++)
            {
                var entity = entities[index];
                if (entity?.transform == null ||
                    !string.Equals(entity.entityId, canonicalAvatarId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = entity.transform;
                avatarIdentity.transform.SetPositionAndRotation(
                    new Vector3(value.positionX, value.positionY, value.positionZ),
                    Quaternion.Euler(value.rotationX, value.rotationY, value.rotationZ));
                if (!string.IsNullOrWhiteSpace(entity.zoneKey))
                    authorityClient.SetProjectionZoneKey(entity.zoneKey);
                return true;
            }

            return false;
        }

        private void StageRestoredPose(
            Vector3 position,
            Quaternion rotation)
        {
            restoredCheckpointPosition = position;
            restoredCheckpointRotation = rotation;
            restoredPoseConfirmationPending = true;
        }

        public void SaveNow(Action<bool> completed = null)
        {
            if (!isActiveAndEnabled || saveInProgress)
            {
                completed?.Invoke(false);
                return;
            }
            StartCoroutine(SaveRoutine(completed));
        }

        public IEnumerator SaveRoutine(Action<bool> completed)
        {
            ResolveDependencies();
            if (saveInProgress || authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                avatarIdentity == null || !avatarIdentity.TryGetGuid(out _) ||
                !CanCaptureCurrentCheckpoint())
            {
                completed?.Invoke(false);
                yield break;
            }

            yield return SavePoseRoutine(
                avatarIdentity.transform.position,
                avatarIdentity.transform.rotation,
                completed);
        }

        /// <summary>
        /// Queues one durable checkpoint at a recovery adapter's confirmed safe
        /// position. The accepted command also re-seeds the Authority's ephemeral
        /// player-motion state, preventing stale water motion from pulling the
        /// local presentation back toward the drowning location.
        /// </summary>
        public void SaveRecoveryPoseNow(
            Vector3 position,
            Quaternion rotation,
            Action<bool> completed = null)
        {
            pendingRecoveryPosition = position;
            pendingRecoveryRotation = rotation;
            if (recoverySaveScheduled)
            {
                return;
            }

            recoverySaveScheduled = true;
            StartCoroutine(SaveRecoveryPoseWhenReady(completed));
        }

        public bool TryGetConfirmedRespawnAnchor(
            out Vector3 position,
            out Quaternion rotation)
        {
            position = lastSavedPosition;
            rotation = lastSavedRotation;
            return hasSavedPosition;
        }

        /// <summary>
        /// Presents an Authority-approved respawn at the last confirmed durable
        /// checkpoint and re-seeds ephemeral player motion from that same pose.
        /// Health and life-state mutations are deliberately not owned here.
        /// </summary>
        public IEnumerator RestoreConfirmedRespawnPoseRoutine(
            Action<bool> completed)
        {
            ResolveDependencies();
            while (saveInProgress)
            {
                yield return null;
            }

            if (!TryGetConfirmedRespawnAnchor(
                    out var position,
                    out var rotation) ||
                avatarIdentity == null)
            {
                completed?.Invoke(false);
                yield break;
            }

            motionReconciler?.BeginCheckpointReseed();
            var controllerWasEnabled =
                characterController != null &&
                characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            avatarIdentity.transform.SetPositionAndRotation(
                position,
                rotation);

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }
            playerInput?.ResetVerticalMotionAfterGrounding();
            groundingAdapter?.RequestSettle();

            var accepted = false;
            yield return SavePoseRoutine(
                position,
                rotation,
                value => accepted = value);
            motionReconciler?.CompleteCheckpointReseed(
                accepted,
                position);
            completed?.Invoke(accepted);
        }

        public static bool CanCaptureCheckpoint(
            bool recoveryActive,
            bool hasWaterOverlap,
            bool controllerEnabled,
            bool controllerGrounded)
        {
            return !recoveryActive &&
                   !hasWaterOverlap &&
                   (!controllerEnabled || controllerGrounded);
        }

        private IEnumerator SaveRecoveryPoseWhenReady(Action<bool> completed)
        {
            while (saveInProgress)
            {
                yield return null;
            }

            var position = pendingRecoveryPosition;
            var rotation = pendingRecoveryRotation;
            yield return SavePoseRoutine(position, rotation, completed);
            recoverySaveScheduled = false;
        }

        private IEnumerator SavePoseRoutine(
            Vector3 position,
            Quaternion rotation,
            Action<bool> completed)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsWorldRuntimeReady ||
                avatarIdentity == null || !avatarIdentity.TryGetGuid(out var avatarId))
            {
                completed?.Invoke(false);
                yield break;
            }

            saveInProgress = true;
            var accepted = false;
            OntologyAuthorityCommandResult commandResult = null;
            var command = CreateCheckpointCommand(avatarId, position, rotation);
            yield return authorityClient.SendCommandRoutine(command, result =>
            {
                commandResult = result;
                accepted = result != null && result.accepted;
            });

            if (!accepted && ShouldReloadAndRetry(commandResult))
            {
                // A revision conflict needs a fresh command ID after reloading the
                // server projection. Transport replays retain their original ID.
                var refreshed = false;
                yield return authorityClient.LoadWorldRoutine(value => refreshed = value);
                if (refreshed)
                {
                    command = CreateCheckpointCommand(avatarId, position, rotation);
                    yield return authorityClient.SendCommandRoutine(command, result =>
                        accepted = result != null && result.accepted);
                }
            }

            if (accepted)
            {
                ConfirmRespawnAnchor(position, rotation);
            }
            nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            saveInProgress = false;
            completed?.Invoke(accepted);
        }

        private OntologyWorldCommand CreateCheckpointCommand(
            Guid avatarId,
            Vector3 position,
            Quaternion rotation) =>
            OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.SaveAvatarCheckpoint,
                OntologyWorldAuthorityClient.CreateAvatarCheckpointPayload(
                    avatarId,
                    authorityClient.CurrentProjectionZoneKey,
                    new OntologyAuthorityTransform
                    {
                        positionX = position.x,
                        positionY = position.y,
                        positionZ = position.z,
                        rotationX = rotation.eulerAngles.x,
                        rotationY = rotation.eulerAngles.y,
                        rotationZ = rotation.eulerAngles.z,
                        scaleX = avatarIdentity.transform.localScale.x,
                        scaleY = avatarIdentity.transform.localScale.y,
                        scaleZ = avatarIdentity.transform.localScale.z
                    }));

        public static bool ShouldReloadAndRetry(
            OntologyAuthorityCommandResult result) =>
            result != null &&
            !result.transportFailure &&
            string.Equals(
                result.rejectionCode,
                "stale_revision",
                StringComparison.Ordinal);

        private bool CanCaptureCurrentCheckpoint()
        {
            return CanCaptureCheckpoint(
                drowningRecovery != null && drowningRecovery.RecoveryActive,
                waterPresenceSensor != null && waterPresenceSensor.HasActiveWaterOverlap,
                characterController != null && characterController.enabled,
                characterController == null || characterController.isGrounded);
        }

        private void ConfirmRespawnAnchor(
            Vector3 position,
            Quaternion rotation)
        {
            lastSavedPosition = position;
            lastSavedRotation = rotation;
            hasSavedPosition = true;
            drowningRecovery?.SetConfirmedRespawnAnchor(position, rotation);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>(
                    FindObjectsInactive.Include);
            if (sessionCoordinator == null)
                sessionCoordinator = FindAnyObjectByType<OntologyGameSessionCoordinator>(
                    FindObjectsInactive.Include);
            var localInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                FindObjectsInactive.Include);
            var resolvedAvatarIdentity = localInput == null
                ? null
                : localInput.GetComponent<OntologyAuthorityEntityIdentity>();
            var avatarIdentityChanged = false;
            if (resolvedAvatarIdentity != null &&
                resolvedAvatarIdentity != avatarIdentity)
            {
                avatarIdentity = resolvedAvatarIdentity;
                avatarIdentityChanged = true;
                groundingAdapter = null;
                drowningRecovery = null;
                waterPresenceSensor = null;
                characterController = null;
                motionReconciler = null;
                playerInput = null;
            }
            if (groundingAdapter == null && avatarIdentity != null)
                groundingAdapter =
                    avatarIdentity.GetComponent<
                        OntologyWorldEntryGroundingAdapter>();
            if (drowningRecovery == null && avatarIdentity != null)
                drowningRecovery =
                    avatarIdentity.GetComponent<OntologyDrowningRecoveryAdapter>();
            if (waterPresenceSensor == null && avatarIdentity != null)
                waterPresenceSensor =
                    avatarIdentity.GetComponent<OntologyWaterPresenceSensor>();
            if (characterController == null && avatarIdentity != null)
                characterController =
                    avatarIdentity.GetComponent<CharacterController>();
            if (motionReconciler == null && avatarIdentity != null)
                motionReconciler =
                    avatarIdentity.GetComponent<
                        OntologyWorldAuthorityPlayerMotionReconciler>();
            if (playerInput == null && avatarIdentity != null)
                playerInput =
                    avatarIdentity.GetComponent<
                        OntologyInputSystemPlayerInput>();
            if (avatarIdentityChanged)
            {
                hasSavedPosition = false;
                drowningRecovery?.ClearConfirmedRespawnAnchor();
            }
        }
    }
}
