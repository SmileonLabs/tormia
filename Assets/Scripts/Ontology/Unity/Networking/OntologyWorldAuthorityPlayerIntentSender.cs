using System;
using System.Collections;
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
        [SerializeField] private OntologyWorldZoneStreamer zoneStreamer;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField, Tooltip("Disabled by default: enabling this sends transient input only, never world Facts.")]
        private bool submitRuntimeIntents;
        [SerializeField, Min(1f)] private float submissionsPerSecond = 15f;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private bool avatarRegistered;
        [SerializeField] private long nextSequence = 1;

        private bool isSubmitting;
        private bool sentActiveIntent;
        private float nextSubmitAt;

        public bool AvatarRegistered => avatarRegistered;

        /// <summary>Called by the durable account-entry coordinator after it
        /// completes the first-time avatar placement and registration.</summary>
        public void SetAvatarRegistered(bool value)
        {
            avatarRegistered = value;
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            if (!submitRuntimeIntents || isSubmitting || authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady || !avatarRegistered || !TryGetAvatarId(out _) ||
                string.IsNullOrWhiteSpace(zoneStreamer == null ? string.Empty : zoneStreamer.ActiveZoneKey))
            {
                return;
            }

            var move = Vector2.zero;
            var hasMove = playerInput != null && playerInput.TryGetWorldMoveIntent(out move);
            var jump = playerInput != null && playerInput.IsJumpIntentHeld;
            if (!hasMove)
            {
                move = Vector2.zero;
            }

            // A stop sample is sent once immediately. Continuing input is sampled
            // at the configured rate, while the server's short lease covers a
            // client crash or network loss without creating durable state.
            var shouldSubmit = (hasMove || jump)
                ? Time.unscaledTime >= nextSubmitAt
                : sentActiveIntent;
            if (!shouldSubmit)
            {
                return;
            }

            StartCoroutine(SubmitRoutine(move, jump, hasMove || jump));
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

        private IEnumerator SubmitRoutine(Vector2 move, bool jump, bool active)
        {
            isSubmitting = true;
            if (!TryGetAvatarId(out var avatarId))
            {
                isSubmitting = false;
                yield break;
            }

            var zoneKey = zoneStreamer.ActiveZoneKey;
            var sequence = nextSequence++;
            yield return authorityClient.SendPlayerIntentRoutine(
                avatarId, zoneKey, sequence, move, jump, result =>
                {
                    if (result == null || !result.accepted)
                    {
                        SetStatus("Authority input rejected: " + (result?.rejectionCode ?? "unknown"));
                        return;
                    }
                    sentActiveIntent = active;
                    nextSubmitAt = Time.unscaledTime + 1f / Mathf.Max(1f, submissionsPerSecond);
                    SetStatus("Authority input accepted (" + sequence + ").");
                });
            isSubmitting = false;
        }

        private bool TryGetAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            return avatarIdentity != null && avatarIdentity.TryGetGuid(out avatarId);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
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

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
        }
    }
}
