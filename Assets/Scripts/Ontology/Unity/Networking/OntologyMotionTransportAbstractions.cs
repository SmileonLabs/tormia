using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public interface IAuthorityRealtimeClock
    {
        float MonotonicSeconds { get; }
        long UtcMilliseconds { get; }
    }

    public interface IAuthorityUdpClientDriver
    {
        bool IsReady { get; }
        event Action Connected;
        event Action<string> Disconnected;
        event Action<string> NetworkFailed;
        event Action<byte[]> DatagramReceived;
        bool Start(string host, int port, byte[] connectionBootstrap);
        bool TrySend(byte[] datagram);
        void Poll();
        void Close();
    }

    public interface IAuthorityMotionControlPlane
    {
        IEnumerator IssueTicket(
            Guid avatarEntityId,
            string runtimeSessionId,
            ushort protocolVersion,
            Action<OntologyAuthorityUdpTransportTicket> completed);

        IEnumerator TransitionWriter(
            Guid avatarEntityId,
            OntologyAuthorityUdpTransportTicket ticket,
            bool promoteUdp,
            Action<OntologyAuthorityMotionTransportTransition> completed);

        IEnumerator LoadCompleteZoneRecovery(
            string zoneKey,
            Action<IReadOnlyList<OntologyAuthorityPlayerMotionState>> completed);
    }

    public sealed class UnityAuthorityRealtimeClock : IAuthorityRealtimeClock
    {
        public float MonotonicSeconds => Time.unscaledTime;
        public long UtcMilliseconds =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public sealed class OntologyAuthorityMotionControlPlaneAdapter :
        IAuthorityMotionControlPlane
    {
        private readonly OntologyWorldAuthorityClient client;
        private readonly IAuthorityMotionSnapshotFeed feed;

        public OntologyAuthorityMotionControlPlaneAdapter(
            OntologyWorldAuthorityClient client,
            IAuthorityMotionSnapshotFeed feed)
        {
            this.client = client;
            this.feed = feed;
        }

        public IEnumerator IssueTicket(Guid avatarEntityId,
            string runtimeSessionId, ushort protocolVersion,
            Action<OntologyAuthorityUdpTransportTicket> completed) =>
            client.IssueUdpTransportTicketRoutine(avatarEntityId,
                runtimeSessionId, protocolVersion, completed);

        public IEnumerator TransitionWriter(Guid avatarEntityId,
            OntologyAuthorityUdpTransportTicket ticket, bool promoteUdp,
            Action<OntologyAuthorityMotionTransportTransition> completed) =>
            client.TransitionMotionWriterRoutine(avatarEntityId, ticket,
                promoteUdp, completed);

        public IEnumerator LoadCompleteZoneRecovery(string zoneKey,
            Action<IReadOnlyList<OntologyAuthorityPlayerMotionState>> completed) =>
            feed.LoadZoneAvatarMotionsRoutine(zoneKey, completed);
    }

    [Serializable]
    public sealed class OntologyRealtimeTransportCounters
    {
        public long udpSent;
        public long udpDropped;
        public long ackTimeouts;
        public long fallbackAttempts;
        public long fallbackSuccesses;
        public long replayRejected;
        public long assemblyRejected;
        public long assemblyExpired;

        public void Reset()
        {
            udpSent = udpDropped = ackTimeouts = fallbackAttempts = 0;
            fallbackSuccesses = replayRejected = assemblyRejected = 0;
            assemblyExpired = 0;
        }
    }

    /// <summary>
    /// Sends one canonical, ephemeral locomotion intent. Implementations carry
    /// transport only; Authority remains the evaluator of the referenced action
    /// and assigned Rule Block.
    /// </summary>
    public interface IPlayerMotionIntentTransport
    {
        IEnumerator SendPlayerIntentRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long sequence,
            Vector2 move,
            float requestedSpeed,
            bool hasDestination,
            Vector2 destination,
            float destinationStopDistance,
            OntologyAuthorityActionDefinitionProjection action,
            Action<OntologyAuthorityRuntimeIntentResult> completed);
    }

    /// <summary>
    /// Sends a collision-resolved pose as an ordered ephemeral observation. The
    /// observation must not overwrite Authority motion state.
    /// </summary>
    public interface IResolvedPoseObservationTransport
    {
        IEnumerator SendResolvedPlayerPoseRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long acceptedIntentSequence,
            long poseSequence,
            Vector3 collisionResolvedPosition,
            string motionStatus,
            Action<OntologyAuthorityRuntimeIntentResult> completed);
    }

    /// <summary>
    /// Provides Authority-owned motion snapshots to local reconciliation and
    /// remote presentation. HTTP recovery and realtime frames use the same feed
    /// so consumers never depend on a concrete socket or request implementation.
    /// </summary>
    public interface IAuthorityMotionSnapshotFeed
    {
        bool IsRealtimeConnected { get; }

        event Action<OntologyAuthorityZoneRuntimeNotification>
            ZoneRuntimeChanged;

        event Action<OntologyAuthorityZoneMotionFrame> ZoneMotionFrameReceived;

        bool TryGetLatestPlayerMotion(
            Guid avatarEntityId,
            float maximumAgeSeconds,
            out OntologyAuthorityPlayerMotionState state);

        IEnumerator LoadPlayerMotionRoutine(
            Guid avatarEntityId,
            Action<OntologyAuthorityPlayerMotionState> completed);

        IEnumerator LoadZoneAvatarMotionsRoutine(
            string zoneKey,
            Action<IReadOnlyList<OntologyAuthorityPlayerMotionState>> completed);
    }
}
