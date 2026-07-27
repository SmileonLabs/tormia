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
        [SerializeField, Min(5f)] private float saveIntervalSeconds = 30f;
        [SerializeField, Min(0.1f)] private float minimumMovedDistance = 2f;
        [SerializeField] private bool saveOnApplicationPause = true;

        private Vector3 lastSavedPosition;
        private float nextSaveAt;
        private bool hasSavedPosition;
        private bool saveInProgress;

        public bool SaveInProgress => saveInProgress;

        private void Awake() => ResolveDependencies();

        private void Update()
        {
            if (sessionCoordinator == null || !sessionCoordinator.IsInWorld ||
                authorityClient == null || !authorityClient.IsWorldRuntimeReady ||
                avatarIdentity == null || saveInProgress ||
                Time.unscaledTime < nextSaveAt)
            {
                return;
            }

            nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            var position = avatarIdentity.transform.position;
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

            // A missing checkpoint is a valid first entry. The durable entity
            // projection is also a recovery path when the dedicated checkpoint
            // read is unavailable because the checkpoint command updates both.
            if (checkpoint?.transform == null)
            {
                TryRestoreFromWorldProjection(avatarId);
                lastSavedPosition = avatarIdentity.transform.position;
                hasSavedPosition = true;
                nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
                completed?.Invoke(true);
                yield break;
            }

            var value = checkpoint.transform;
            avatarIdentity.transform.SetPositionAndRotation(
                new Vector3(value.positionX, value.positionY, value.positionZ),
                Quaternion.Euler(value.rotationX, value.rotationY, value.rotationZ));
            if (!string.IsNullOrWhiteSpace(checkpoint.zoneKey))
                authorityClient.SetProjectionZoneKey(checkpoint.zoneKey);
            lastSavedPosition = avatarIdentity.transform.position;
            hasSavedPosition = true;
            nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            completed?.Invoke(true);
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
                avatarIdentity == null || !avatarIdentity.TryGetGuid(out var avatarId))
            {
                completed?.Invoke(false);
                yield break;
            }

            saveInProgress = true;
            var accepted = false;
            OntologyAuthorityCommandResult commandResult = null;
            var command = CreateCheckpointCommand(avatarId);
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
                    command = CreateCheckpointCommand(avatarId);
                    yield return authorityClient.SendCommandRoutine(command, result =>
                        accepted = result != null && result.accepted);
                }
            }

            if (accepted)
            {
                lastSavedPosition = avatarIdentity.transform.position;
                hasSavedPosition = true;
            }
            nextSaveAt = Time.unscaledTime + Mathf.Max(5f, saveIntervalSeconds);
            saveInProgress = false;
            completed?.Invoke(accepted);
        }

        private OntologyWorldCommand CreateCheckpointCommand(Guid avatarId) =>
            OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.SaveAvatarCheckpoint,
                OntologyWorldAuthorityClient.CreateAvatarCheckpointPayload(
                    avatarId,
                    authorityClient.CurrentProjectionZoneKey,
                    avatarIdentity.transform));

        public static bool ShouldReloadAndRetry(
            OntologyAuthorityCommandResult result) =>
            result != null &&
            !result.transportFailure &&
            string.Equals(
                result.rejectionCode,
                "stale_revision",
                StringComparison.Ordinal);

        private void ResolveDependencies()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>(
                    FindObjectsInactive.Include);
            if (sessionCoordinator == null)
                sessionCoordinator = FindAnyObjectByType<OntologyGameSessionCoordinator>(
                    FindObjectsInactive.Include);
            if (avatarIdentity == null)
                avatarIdentity = FindAnyObjectByType<OntologyAuthorityEntityIdentity>(
                    FindObjectsInactive.Include);
        }
    }
}
