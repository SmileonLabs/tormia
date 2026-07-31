using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Transports a canonical respawn intent for the local Authority avatar.
    /// The assigned Rule Block owns health/life changes; this component only
    /// requests that action and presents an accepted checkpoint restore.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthorityRespawnController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private OntologyAvatarCheckpointController checkpointController;
        [SerializeField, Min(0f)] private float presentationDelaySeconds = 1.25f;
        [SerializeField, TextArea] private string lastStatus;

        private float deathObservedAt = -1f;
        private bool attemptedForCurrentDeath;
        private bool requestPending;

        public string LastStatus => lastStatus;
        public bool RequestPending => requestPending;

        private void Awake() => ResolveDependencies();

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
            EvaluateProjection(
                authorityClient == null
                    ? null
                    : authorityClient.CurrentProjection);
        }

        private void OnDisable()
        {
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived -=
                    HandleProjectionReceived;
            }
        }

        private void Update()
        {
            ResolveDependencies();
            EvaluateProjection(
                authorityClient == null
                    ? null
                    : authorityClient.CurrentProjection);
        }

        [ContextMenu("Retry Authority Respawn")]
        public void RetryRespawn()
        {
            attemptedForCurrentDeath = false;
            deathObservedAt = Time.unscaledTime -
                              Mathf.Max(0f, presentationDelaySeconds);
            EvaluateProjection(
                authorityClient == null
                    ? null
                    : authorityClient.CurrentProjection);
        }

        /// <summary>
        /// Restores a dead avatar through its authored respawn action before
        /// the normal world-entry locomotion handshake begins. This transport
        /// step does not synthesize health or life locally; the assigned Rule
        /// Block remains the sole owner of those durable results.
        /// </summary>
        public IEnumerator PrepareAliveForWorldEntryRoutine(
            Action<bool> completed)
        {
            ResolveDependencies();
            if (requestPending ||
                authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                avatarIdentity == null ||
                !avatarIdentity.TryGetGuid(out var avatarId) ||
                !TryResolveEntryRespawnRequirement(
                    authorityClient.CurrentProjection,
                    avatarId,
                    out var requiresRespawn))
            {
                lastStatus =
                    "The Authority player life state could not be resolved " +
                    "for world entry.";
                completed?.Invoke(false);
                yield break;
            }

            if (!requiresRespawn)
            {
                deathObservedAt = -1f;
                attemptedForCurrentDeath = false;
                lastStatus =
                    "The Authority player life state is ready for world entry.";
                completed?.Invoke(true);
                yield break;
            }

            if (!TryResolveRespawnActionId(
                    authorityClient.CurrentProjection,
                    avatarId,
                    out var actionId) ||
                !authorityClient.TryResolveEnabledAction(
                    actionId,
                    out var definition))
            {
                lastStatus =
                    "The dead avatar does not have an enabled Authority " +
                    "respawn contract.";
                completed?.Invoke(false);
                yield break;
            }

            requestPending = true;
            OntologyAuthorityCommandResult result = null;
            yield return SendRespawnCommandRoutine(
                avatarId,
                definition,
                value => result = value);
            if (result == null || !result.accepted)
            {
                lastStatus =
                    "Authority entry respawn rejected: " +
                    (result?.rejectionCode ?? "authority_no_response");
                requestPending = false;
                completed?.Invoke(false);
                yield break;
            }

            var loaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => loaded = value);
            var aliveVerified =
                loaded &&
                TryResolveAliveState(
                    authorityClient.CurrentProjection,
                    avatarId,
                    out var isAlive) &&
                isAlive;
            requestPending = false;
            if (!aliveVerified)
            {
                lastStatus =
                    "Authority accepted entry respawn, but the revived " +
                    "projection could not be verified.";
                completed?.Invoke(false);
                yield break;
            }

            deathObservedAt = -1f;
            attemptedForCurrentDeath = false;
            lastStatus =
                "Authority restored the player life state for world entry.";
            completed?.Invoke(true);
        }

        private void HandleProjectionReceived(
            OntologyAuthorityWorldProjection projection)
        {
            EvaluateProjection(projection);
        }

        private void EvaluateProjection(
            OntologyAuthorityWorldProjection projection)
        {
            if (requestPending ||
                authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                sessionCoordinator == null ||
                !sessionCoordinator.IsInWorld ||
                avatarIdentity == null ||
                !avatarIdentity.TryGetGuid(out var avatarId) ||
                !TryResolveAliveState(
                    projection,
                    avatarId,
                    out var isAlive))
            {
                return;
            }

            if (isAlive)
            {
                deathObservedAt = -1f;
                attemptedForCurrentDeath = false;
                return;
            }

            if (deathObservedAt < 0f)
            {
                deathObservedAt = Time.unscaledTime;
            }

            if (attemptedForCurrentDeath ||
                Time.unscaledTime <
                deathObservedAt +
                Mathf.Max(0f, presentationDelaySeconds) ||
                checkpointController == null ||
                !checkpointController.HasConfirmedRespawnAnchor ||
                !TryResolveRespawnActionId(
                    projection,
                    avatarId,
                    out var actionId) ||
                !authorityClient.TryResolveEnabledAction(
                    actionId,
                    out var definition))
            {
                return;
            }

            attemptedForCurrentDeath = true;
            StartCoroutine(
                ExecuteRespawnRoutine(
                    avatarId,
                    definition));
        }

        private IEnumerator ExecuteRespawnRoutine(
            Guid avatarId,
            OntologyAuthorityActionDefinitionProjection definition)
        {
            requestPending = true;
            OntologyAuthorityCommandResult result = null;
            yield return SendRespawnCommandRoutine(
                avatarId,
                definition,
                value => result = value);
            if (result == null || !result.accepted)
            {
                lastStatus =
                    "Authority respawn rejected: " +
                    (result?.rejectionCode ?? "authority_no_response");
                requestPending = false;
                yield break;
            }

            var loaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => loaded = value);
            if (!loaded ||
                !TryResolveAliveState(
                    authorityClient.CurrentProjection,
                    avatarId,
                    out var isAlive) ||
                !isAlive)
            {
                lastStatus =
                    "Authority accepted respawn, but the revived projection " +
                    "could not be verified.";
                requestPending = false;
                yield break;
            }

            var poseRestored = false;
            yield return checkpointController
                .RestoreConfirmedRespawnPoseRoutine(
                    value => poseRestored = value);
            lastStatus = poseRestored
                ? "Authority respawn completed at the confirmed checkpoint."
                : "Authority revived the avatar, but checkpoint presentation " +
                  "could not be restored.";
            requestPending = false;
        }

        private IEnumerator SendRespawnCommandRoutine(
            Guid avatarId,
            OntologyAuthorityActionDefinitionProjection definition,
            Action<OntologyAuthorityCommandResult> completed)
        {
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandRoutine(
                CreateRespawnCommand(
                    avatarId,
                    definition),
                value => result = value);
            if (result != null &&
                !result.transportFailure &&
                string.Equals(
                    result.rejectionCode,
                    "stale_revision",
                    StringComparison.Ordinal))
            {
                var reloaded = false;
                yield return authorityClient.LoadWorldRoutine(
                    value => reloaded = value);
                if (reloaded)
                {
                    yield return authorityClient.SendCommandRoutine(
                        CreateRespawnCommand(
                            avatarId,
                            definition),
                        value => result = value);
                }
            }

            completed?.Invoke(result);
        }

        private static OntologyWorldCommand CreateRespawnCommand(
            Guid avatarId,
            OntologyAuthorityActionDefinitionProjection definition) =>
            OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.ExecuteAction,
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    avatarId,
                    avatarId,
                    null,
                    definition.packageId,
                    definition.packageVersion,
                    definition.actionId,
                    definition.definitionVersion));

        public static bool TryResolveAliveState(
            OntologyAuthorityWorldProjection projection,
            Guid avatarId,
            out bool isAlive)
        {
            isAlive = false;
            if (projection?.facts == null ||
                avatarId == Guid.Empty)
            {
                return false;
            }

            var canonicalId = avatarId.ToString("D");
            var found = false;
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        canonicalId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.IsAlive,
                        StringComparison.Ordinal) ||
                    !bool.TryParse(
                        fact.objectValueJson,
                        out var candidate))
                {
                    continue;
                }

                if (found && candidate != isAlive)
                {
                    return false;
                }
                found = true;
                isAlive = candidate;
            }

            return found;
        }

        public static bool TryResolveEntryRespawnRequirement(
            OntologyAuthorityWorldProjection projection,
            Guid avatarId,
            out bool requiresRespawn)
        {
            requiresRespawn = false;
            if (!TryResolveAliveState(
                    projection,
                    avatarId,
                    out var isAlive))
            {
                return false;
            }

            requiresRespawn = !isAlive;
            return true;
        }

        public static bool TryResolveRespawnActionId(
            OntologyAuthorityWorldProjection projection,
            Guid avatarId,
            out string actionId)
        {
            actionId = string.Empty;
            if (projection?.facts == null ||
                avatarId == Guid.Empty)
            {
                return false;
            }

            var canonicalId = avatarId.ToString("D");
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(
                        fact.subjectEntityId,
                        canonicalId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        fact.predicateId,
                        OntologyPredicates.RespawnAction,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(
                        fact.objectCanonicalId))
                {
                    continue;
                }

                var candidate = fact.objectCanonicalId.Trim();
                if (!string.IsNullOrEmpty(actionId) &&
                    !string.Equals(
                        actionId,
                        candidate,
                        StringComparison.Ordinal))
                {
                    actionId = string.Empty;
                    return false;
                }
                actionId = candidate;
            }

            return !string.IsNullOrEmpty(actionId);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient =
                    FindAnyObjectByType<OntologyWorldAuthorityClient>(
                        FindObjectsInactive.Include);
            }
            if (sessionCoordinator == null)
            {
                sessionCoordinator =
                    FindAnyObjectByType<OntologyGameSessionCoordinator>(
                        FindObjectsInactive.Include);
            }
            if (checkpointController == null)
            {
                checkpointController =
                    FindAnyObjectByType<OntologyAvatarCheckpointController>(
                        FindObjectsInactive.Include);
            }
            var localInput =
                FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                    FindObjectsInactive.Include);
            var resolvedIdentity = localInput == null
                ? null
                : localInput.GetComponent<
                    OntologyAuthorityEntityIdentity>();
            if (resolvedIdentity != null)
            {
                avatarIdentity = resolvedIdentity;
            }
            Subscribe();
        }

        private void Subscribe()
        {
            if (authorityClient == null)
            {
                return;
            }

            authorityClient.ProjectionReceived -=
                HandleProjectionReceived;
            authorityClient.ProjectionReceived +=
                HandleProjectionReceived;
        }
    }
}
