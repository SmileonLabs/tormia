using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Current reliable HTTP adapter for canonical player motion intent and
    /// collision-resolved observations. It is deliberately a thin delegate:
    /// request shape, session, sequence, Rule Block evaluation, and acceptance
    /// remain owned by World Authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityHttpMotionTransport : MonoBehaviour,
        IPlayerMotionIntentTransport,
        IResolvedPoseObservationTransport
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private long authorityWriterEpoch;

        public long AuthorityWriterEpoch => authorityWriterEpoch;

        public void SetAuthorityWriterEpoch(long value)
        {
            authorityWriterEpoch = Math.Max(0L, value);
        }

        public void Configure(OntologyWorldAuthorityClient value)
        {
            if (value != null)
            {
                authorityClient = value;
            }
        }

        public IEnumerator SendPlayerIntentRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long sequence,
            Vector2 move,
            float requestedSpeed,
            bool hasDestination,
            Vector2 destination,
            float destinationStopDistance,
            OntologyAuthorityActionDefinitionProjection action,
            Action<OntologyAuthorityRuntimeIntentResult> completed)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                completed?.Invoke(null);
                yield break;
            }

            if (string.Equals(authorityClient.ActiveMotionWriterMode, "http",
                    StringComparison.Ordinal) &&
                authorityClient.ActiveMotionWriterEpoch > 0)
                authorityWriterEpoch = authorityClient.ActiveMotionWriterEpoch;
            if (authorityWriterEpoch <= 0)
            {
                completed?.Invoke(OntologyAuthorityRuntimeIntentResult.Rejected(
                    "authority_http_writer_epoch_unavailable"));
                yield break;
            }

            yield return authorityClient.SendPlayerIntentRoutine(
                avatarEntityId,
                zoneKey,
                sequence,
                move,
                requestedSpeed,
                hasDestination,
                destination,
                destinationStopDistance,
                action,
                completed,
                authorityWriterEpoch);
        }

        public IEnumerator SendResolvedPlayerPoseRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long acceptedIntentSequence,
            long poseSequence,
            Vector3 collisionResolvedPosition,
            string motionStatus,
            Action<OntologyAuthorityRuntimeIntentResult> completed)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                completed?.Invoke(null);
                yield break;
            }

            yield return authorityClient.SendResolvedPlayerPoseRoutine(
                avatarEntityId,
                zoneKey,
                acceptedIntentSequence,
                poseSequence,
                collisionResolvedPosition,
                motionStatus,
                completed);
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void ResolveDependencies()
        {
            authorityClient ??=
                GetComponent<OntologyWorldAuthorityClient>();
        }
    }
}
