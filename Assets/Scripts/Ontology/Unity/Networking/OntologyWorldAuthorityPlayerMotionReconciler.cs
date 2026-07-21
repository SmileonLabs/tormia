using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Applies a smooth horizontal correction from the authority's kinematic
    /// runtime state. Local input remains responsive; this component only reduces
    /// divergence. It deliberately does not own Y, gravity, water, or collision
    /// until the server has an equivalent collision representation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityPlayerMotionReconciler : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldZoneStreamer zoneStreamer;
        [SerializeField] private OntologyWorldAuthorityPlayerIntentSender intentSender;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private CharacterController characterController;
        [SerializeField, Tooltip("Disabled by default until the avatar is published and registered on the authority.")]
        private bool reconcileAuthorityMotion;
        [SerializeField, Min(0.25f)] private float pollIntervalSeconds = 0.2f;
        [SerializeField, Min(0f)] private float correctionStartDistance = 0.08f;
        [SerializeField, Min(0.01f)] private float maximumCorrectionPerSecond = 6f;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private Vector3 latestAuthorityPosition;
        [SerializeField] private string latestMotionStatus;

        private bool isLoading;
        private bool hasAuthorityPosition;
        private float nextPollAt;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            if (!CanReconcile() || isLoading || Time.unscaledTime < nextPollAt)
            {
                return;
            }
            StartCoroutine(LoadMotionRoutine());
        }

        private void LateUpdate()
        {
            if (!CanReconcile() || !hasAuthorityPosition)
            {
                return;
            }

            var offset = latestAuthorityPosition - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= correctionStartDistance * correctionStartDistance)
            {
                return;
            }

            var correction = Vector3.ClampMagnitude(
                offset,
                Mathf.Max(0.01f, maximumCorrectionPerSecond) * Time.deltaTime);
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(correction);
            }
            else
            {
                transform.position += correction;
            }
        }

        private IEnumerator LoadMotionRoutine()
        {
            isLoading = true;
            nextPollAt = Time.unscaledTime + Mathf.Max(0.25f, pollIntervalSeconds);
            if (!TryGetAvatarId(out var avatarId))
            {
                isLoading = false;
                yield break;
            }

            yield return authorityClient.LoadPlayerMotionRoutine(avatarId, state =>
            {
                if (state == null)
                {
                    SetStatus("Authority has no runtime motion state for this avatar yet.");
                    return;
                }
                if (zoneStreamer != null && !string.IsNullOrWhiteSpace(zoneStreamer.ActiveZoneKey) &&
                    !string.Equals(zoneStreamer.ActiveZoneKey, state.zoneKey, StringComparison.Ordinal))
                {
                    SetStatus("Ignored authority position from a different Zone.");
                    return;
                }

                latestAuthorityPosition = new Vector3(
                    (float)state.positionX,
                    transform.position.y,
                    (float)state.positionZ);
                latestMotionStatus = state.motionStatus ?? string.Empty;
                hasAuthorityPosition = true;
                SetStatus("Authority motion: " + latestMotionStatus + ".");
            });
            isLoading = false;
        }

        private bool CanReconcile()
        {
            return reconcileAuthorityMotion && authorityClient != null && authorityClient.IsReady &&
                   intentSender != null && intentSender.AvatarRegistered &&
                   avatarIdentity != null && avatarIdentity.TryGetGuid(out _) &&
                   zoneStreamer != null && !string.IsNullOrWhiteSpace(zoneStreamer.ActiveZoneKey);
        }

        private bool TryGetAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            return avatarIdentity != null && avatarIdentity.TryGetGuid(out avatarId);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (zoneStreamer == null) zoneStreamer = FindAnyObjectByType<OntologyWorldZoneStreamer>();
            if (intentSender == null) intentSender = FindAnyObjectByType<OntologyWorldAuthorityPlayerIntentSender>();
            if (avatarIdentity == null) avatarIdentity = GetComponent<OntologyAuthorityEntityIdentity>();
            if (characterController == null) characterController = GetComponent<CharacterController>();
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
        }
    }
}
