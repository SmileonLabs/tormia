using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Transport-neutral Authority motion feed. The current implementation
    /// forwards SignalR motion frames and delegates recovery reads to HTTP.
    /// Future UDP may publish the same Authority frame through this feed without
    /// changing input, reconciliation, or remote-avatar presentation code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityMotionSnapshotFeed : MonoBehaviour,
        IAuthorityMotionSnapshotFeed
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityRealtimeClient realtimeClient;

        private OntologyWorldAuthorityRealtimeClient subscribedRealtimeClient;
        private bool realtimeSubscriptionActive;
        private const int MaximumRememberedOccurrences = 256;
        private readonly Queue<Guid> occurrenceOrder = new();
        private readonly HashSet<Guid> occurrences = new();
        private readonly Dictionary<Guid, ActorTimeline> actorTimelines = new();
        private readonly Dictionary<Guid, OntologyAuthorityPlayerMotionState>
            acceptedStates = new();
        private string timelineWorldId = string.Empty;
        private string timelineZoneKey = string.Empty;
        private long reliableBarrierEpoch;
        private long observedRealtimeConnectionGeneration;
        private bool realtimeRecoveryInFlight;
        private bool reliableLaneReady = true;

        public bool IsRealtimeConnected =>
            subscribedRealtimeClient != null &&
            subscribedRealtimeClient.IsConnected;

        public event Action<OntologyAuthorityZoneRuntimeNotification>
            ZoneRuntimeChanged;

        public event Action<OntologyAuthorityZoneMotionFrame>
            ZoneMotionFrameReceived;

        public void Configure(
            OntologyWorldAuthorityClient authority,
            OntologyWorldAuthorityRealtimeClient realtime)
        {
            if (authority != null)
            {
                authorityClient = authority;
            }
            if (realtime != null)
            {
                realtimeClient = realtime;
            }
            SetRealtimeSubscription(realtimeClient);
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            SetRealtimeSubscription(realtimeClient);
        }

        private void Update()
        {
            if (subscribedRealtimeClient == null ||
                !subscribedRealtimeClient.IsConnected ||
                subscribedRealtimeClient.ConnectionGeneration ==
                    observedRealtimeConnectionGeneration ||
                realtimeRecoveryInFlight) return;
            observedRealtimeConnectionGeneration =
                subscribedRealtimeClient.ConnectionGeneration;
            reliableLaneReady = false;
            ResetTimelineScope();
            StartCoroutine(RecoverRealtimeLaneRoutine());
        }

        private void OnDisable()
        {
            SetRealtimeSubscription(null);
            ResetTimelineScope();
        }

        public IEnumerator LoadPlayerMotionRoutine(
            Guid avatarEntityId,
            Action<OntologyAuthorityPlayerMotionState> completed)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                completed?.Invoke(null);
                yield break;
            }

            var capture = CaptureReliableRecoveryScope();

            yield return authorityClient.LoadPlayerMotionRoutine(
                avatarEntityId,
                value =>
                {
                    RegisterReliableRecovery(value == null
                        ? null : new[] { value }, capture, false);
                    completed?.Invoke(value);
                });
        }

        public bool TryGetLatestPlayerMotion(
            Guid avatarEntityId,
            float maximumAgeSeconds,
            out OntologyAuthorityPlayerMotionState state)
        {
            ResolveDependencies();
            if (avatarEntityId != Guid.Empty && maximumAgeSeconds >= 0f &&
                acceptedStates.TryGetValue(avatarEntityId, out state) &&
                IsCurrentScopeState(state))
            {
                var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() -
                          state.updatedAtUnixMilliseconds;
                if (age >= 0 && age <=
                    Math.Max(0d, maximumAgeSeconds) * 1000d)
                    return true;
            }
            if (authorityClient == null)
            {
                state = null;
                return false;
            }

            var found = authorityClient.TryGetLatestPlayerMotion(
                avatarEntityId, maximumAgeSeconds, out state);
            if (found) RegisterReliableRecovery(new[] { state },
                CaptureReliableRecoveryScope(), false);
            return found;
        }

        public IEnumerator LoadZoneAvatarMotionsRoutine(
            string zoneKey,
            Action<IReadOnlyList<OntologyAuthorityPlayerMotionState>> completed)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                completed?.Invoke(null);
                yield break;
            }

            var capture = CaptureReliableRecoveryScope(zoneKey);

            yield return authorityClient.LoadZoneAvatarMotionsRoutine(
                zoneKey,
                value =>
                {
                    var registered = RegisterReliableRecovery(
                        value, capture, true);
                    if (registered)
                        reliableLaneReady = true;
                    completed?.Invoke(registered ? value : null);
                });
        }

        private RecoveryScope CaptureReliableRecoveryScope(
            string requestedZone = null)
        {
            if (authorityClient == null ||
                !Guid.TryParse(authorityClient.CurrentWorldId, out var worldId) ||
                worldId == Guid.Empty) return default;
            var zone = string.IsNullOrWhiteSpace(requestedZone)
                ? authorityClient.CurrentProjectionZoneKey
                : requestedZone.Trim();
            return string.IsNullOrWhiteSpace(zone)
                ? default
                : new RecoveryScope(worldId.ToString("D"), zone,
                    reliableBarrierEpoch);
        }

        private bool RegisterReliableRecovery(
            IReadOnlyList<OntologyAuthorityPlayerMotionState> states,
            RecoveryScope capture,
            bool completeZoneSnapshot)
        {
            if (authorityClient == null || states == null || !capture.IsValid ||
                !Guid.TryParse(authorityClient.CurrentWorldId, out var worldId) ||
                worldId == Guid.Empty ||
                string.IsNullOrWhiteSpace(
                    authorityClient.CurrentProjectionZoneKey)) return false;
            var world = worldId.ToString("D");
            var zone = authorityClient.CurrentProjectionZoneKey;
            if (capture.Epoch != reliableBarrierEpoch ||
                !string.Equals(capture.WorldId, world,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(capture.ZoneKey, zone,
                    StringComparison.Ordinal)) return false;
            if (!string.Equals(world, timelineWorldId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(zone, timelineZoneKey,
                    StringComparison.Ordinal))
            {
                ResetTimelineScope();
                timelineWorldId = world;
                timelineZoneKey = zone;
            }
            var recovered = new List<RecoveredActor>(states.Count);
            var barrierChanged = false;
            var allRecoveredActorsAccepted = true;
            foreach (var state in states)
            {
                if (state == null ||
                    !string.Equals(state.worldId, world,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.zoneKey, zone,
                        StringComparison.Ordinal) ||
                    !Guid.TryParse(state.avatarEntityId, out var actorId) ||
                    !Guid.TryParse(state.runtimeSessionId, out var sessionId) ||
                    actorId == Guid.Empty || sessionId == Guid.Empty ||
                    state.serverTick <= 0 ||
                    state.updatedAtUnixMilliseconds <= 0)
                {
                    if (completeZoneSnapshot) return false;
                    continue;
                }
                recovered.Add(new RecoveredActor(actorId, sessionId, state));
            }
            if (completeZoneSnapshot)
            {
                var present = new HashSet<Guid>();
                foreach (var value in recovered) present.Add(value.ActorId);
                foreach (var actorId in new List<Guid>(actorTimelines.Keys))
                    if (!present.Contains(actorId))
                    {
                        barrierChanged |= actorTimelines.Remove(actorId);
                        acceptedStates.Remove(actorId);
                    }
            }
            foreach (var recoveredActor in recovered)
            {
                var actorId = recoveredActor.ActorId;
                var sessionId = recoveredActor.SessionId;
                var state = recoveredActor.State;
                var acceptedActor = false;
                if (!actorTimelines.TryGetValue(actorId, out var timeline))
                {
                    actorTimelines.Add(actorId,
                        new ActorTimeline(sessionId, state.serverTick,
                            state.updatedAtUnixMilliseconds));
                    barrierChanged = true;
                    acceptedActor = true;
                }
                else if (timeline.RuntimeSessionId == sessionId &&
                         state.serverTick > timeline.ServerTick)
                {
                    timeline.ServerTick = state.serverTick;
                    timeline.UpdatedAtUnixMilliseconds = Math.Max(
                        timeline.UpdatedAtUnixMilliseconds,
                        state.updatedAtUnixMilliseconds);
                    barrierChanged = true;
                    acceptedActor = true;
                }
                else if (timeline.RuntimeSessionId != sessionId &&
                         state.updatedAtUnixMilliseconds >
                            timeline.UpdatedAtUnixMilliseconds)
                {
                    timeline.RuntimeSessionId = sessionId;
                    timeline.ServerTick = state.serverTick;
                    timeline.UpdatedAtUnixMilliseconds =
                        state.updatedAtUnixMilliseconds;
                    barrierChanged = true;
                    acceptedActor = true;
                }
                else if (timeline.RuntimeSessionId == sessionId &&
                         state.serverTick == timeline.ServerTick &&
                         state.updatedAtUnixMilliseconds >=
                            timeline.UpdatedAtUnixMilliseconds)
                {
                    acceptedActor = true;
                }
                if (acceptedActor) acceptedStates[actorId] = state;
                else allRecoveredActorsAccepted = false;
            }
            if (completeZoneSnapshot && !allRecoveredActorsAccepted)
                return false;
            if (barrierChanged) reliableBarrierEpoch++;
            return true;
        }

        private IEnumerator RecoverRealtimeLaneRoutine()
        {
            realtimeRecoveryInFlight = true;
            var capture = CaptureReliableRecoveryScope();
            IReadOnlyList<OntologyAuthorityPlayerMotionState> states = null;
            if (authorityClient != null && capture.IsValid)
                yield return authorityClient.LoadZoneAvatarMotionsRoutine(
                    capture.ZoneKey, value => states = value);
            reliableLaneReady = states != null &&
                RegisterReliableRecovery(states, capture, true);
            realtimeRecoveryInFlight = false;
        }

        private void HandleZoneMotionFrameReceived(
            OntologyAuthorityZoneMotionFrame frame)
        {
            if (subscribedRealtimeClient != null &&
                subscribedRealtimeClient.ConnectionGeneration !=
                    observedRealtimeConnectionGeneration)
            {
                observedRealtimeConnectionGeneration =
                    subscribedRealtimeClient.ConnectionGeneration;
                reliableLaneReady = false;
                ResetTimelineScope();
                if (!realtimeRecoveryInFlight)
                    StartCoroutine(RecoverRealtimeLaneRoutine());
                return;
            }
            if (!reliableLaneReady) return;
            PublishMergedFrame(frame, true);
        }

        /// <summary>
        /// Accepts a fully authenticated and completely assembled UDP frame.
        /// Reliable frames remain the only barrier that may establish or
        /// replace an actor runtime session; UDP can only advance a session
        /// already observed through SignalR/recovery.
        /// </summary>
        public bool PublishAuthenticatedUdpFrame(
            OntologyAuthorityZoneMotionFrame frame) =>
            PublishMergedFrame(frame, false);

        private bool PublishMergedFrame(
            OntologyAuthorityZoneMotionFrame frame,
            bool reliableSessionBarrier)
        {
            if (frame == null ||
                !Guid.TryParse(frame.worldId, out var frameWorldId) ||
                frameWorldId == Guid.Empty ||
                string.IsNullOrWhiteSpace(frame.zoneKey) ||
                !Guid.TryParse(frame.frameOccurrenceId, out var occurrenceId) ||
                occurrenceId == Guid.Empty || frame.items == null ||
                (!reliableSessionBarrier &&
                 occurrences.Contains(occurrenceId)))
                return false;
            if (!string.Equals(frame.worldId, timelineWorldId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(frame.zoneKey, timelineZoneKey,
                    StringComparison.Ordinal))
            {
                if (!reliableSessionBarrier) return false;
                ResetTimelineScope();
                timelineWorldId = frame.worldId ?? string.Empty;
                timelineZoneKey = frame.zoneKey ?? string.Empty;
            }
            if (reliableSessionBarrier) reliableBarrierEpoch++;

            var accepted = new List<OntologyAuthorityPlayerMotionState>(
                frame.items.Length);
            foreach (var state in frame.items)
            {
                if (state == null ||
                    !string.Equals(state.worldId, frame.worldId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.zoneKey, frame.zoneKey,
                        StringComparison.Ordinal) ||
                    !Guid.TryParse(state.avatarEntityId, out var actorId) ||
                    !Guid.TryParse(state.runtimeSessionId, out var sessionId) ||
                    actorId == Guid.Empty || sessionId == Guid.Empty ||
                    state.serverTick <= 0 ||
                    state.updatedAtUnixMilliseconds <= 0)
                    continue;

                if (!actorTimelines.TryGetValue(actorId, out var timeline))
                {
                    if (!reliableSessionBarrier) continue;
                    timeline = new ActorTimeline(sessionId, state.serverTick,
                        state.updatedAtUnixMilliseconds);
                    actorTimelines.Add(actorId, timeline);
                    acceptedStates[actorId] = state;
                    accepted.Add(state);
                    continue;
                }

                if (timeline.RuntimeSessionId != sessionId)
                {
                    if (!reliableSessionBarrier ||
                        state.updatedAtUnixMilliseconds <=
                            timeline.UpdatedAtUnixMilliseconds) continue;
                    timeline.RuntimeSessionId = sessionId;
                    timeline.ServerTick = state.serverTick;
                    timeline.UpdatedAtUnixMilliseconds =
                        state.updatedAtUnixMilliseconds;
                    acceptedStates[actorId] = state;
                    accepted.Add(state);
                    continue;
                }
                if (state.serverTick <= timeline.ServerTick) continue;
                timeline.ServerTick = state.serverTick;
                timeline.UpdatedAtUnixMilliseconds = Math.Max(
                    timeline.UpdatedAtUnixMilliseconds,
                    state.updatedAtUnixMilliseconds);
                acceptedStates[actorId] = state;
                accepted.Add(state);
            }

            RememberOccurrence(occurrenceId);
            if (accepted.Count == 0) return false;
            if (accepted.Count != frame.items.Length)
            {
                frame = new OntologyAuthorityZoneMotionFrame
                {
                    worldId = frame.worldId,
                    zoneKey = frame.zoneKey,
                    frameOccurrenceId = frame.frameOccurrenceId,
                    serverTick = frame.serverTick,
                    observedAtUnixMilliseconds =
                        frame.observedAtUnixMilliseconds,
                    items = accepted.ToArray()
                };
            }
            ZoneMotionFrameReceived?.Invoke(frame);
            return true;
        }

        private void RememberOccurrence(Guid value)
        {
            if (!occurrences.Add(value)) return;
            occurrenceOrder.Enqueue(value);
            while (occurrenceOrder.Count > MaximumRememberedOccurrences)
                occurrences.Remove(occurrenceOrder.Dequeue());
        }

        private void ResetTimelineScope()
        {
            occurrenceOrder.Clear();
            occurrences.Clear();
            actorTimelines.Clear();
            acceptedStates.Clear();
            timelineWorldId = string.Empty;
            timelineZoneKey = string.Empty;
            reliableBarrierEpoch++;
            reliableLaneReady = subscribedRealtimeClient == null;
            realtimeRecoveryInFlight = false;
        }

        private bool IsCurrentScopeState(
            OntologyAuthorityPlayerMotionState state) =>
            state != null && authorityClient != null &&
            string.Equals(state.worldId, authorityClient.CurrentWorldId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(state.zoneKey,
                authorityClient.CurrentProjectionZoneKey,
                StringComparison.Ordinal);

        private void HandleZoneRuntimeChanged(
            OntologyAuthorityZoneRuntimeNotification notification)
        {
            ZoneRuntimeChanged?.Invoke(notification);
        }

        private void ResolveDependencies()
        {
            authorityClient ??=
                GetComponent<OntologyWorldAuthorityClient>();
            realtimeClient ??=
                GetComponent<OntologyWorldAuthorityRealtimeClient>();
        }

        private void SetRealtimeSubscription(
            OntologyWorldAuthorityRealtimeClient value)
        {
            if (!ReferenceEquals(subscribedRealtimeClient, value))
            {
                if (subscribedRealtimeClient != null &&
                    realtimeSubscriptionActive)
                {
                    subscribedRealtimeClient.ZoneRuntimeChanged -=
                        HandleZoneRuntimeChanged;
                    subscribedRealtimeClient.ZoneMotionFrameReceived -=
                        HandleZoneMotionFrameReceived;
                }
                subscribedRealtimeClient = value;
                realtimeSubscriptionActive = false;
            }

            if (subscribedRealtimeClient != null &&
                isActiveAndEnabled &&
                !realtimeSubscriptionActive)
            {
                subscribedRealtimeClient.ZoneRuntimeChanged +=
                    HandleZoneRuntimeChanged;
                subscribedRealtimeClient.ZoneMotionFrameReceived +=
                    HandleZoneMotionFrameReceived;
                realtimeSubscriptionActive = true;
            }
        }

        private sealed class ActorTimeline
        {
            internal ActorTimeline(Guid runtimeSessionId, long serverTick,
                long updatedAtUnixMilliseconds)
            {
                RuntimeSessionId = runtimeSessionId;
                ServerTick = serverTick;
                UpdatedAtUnixMilliseconds = updatedAtUnixMilliseconds;
            }
            internal Guid RuntimeSessionId { get; set; }
            internal long ServerTick { get; set; }
            internal long UpdatedAtUnixMilliseconds { get; set; }
        }

        private readonly struct RecoveryScope
        {
            internal RecoveryScope(string worldId, string zoneKey, long epoch)
            {
                WorldId = worldId;
                ZoneKey = zoneKey;
                Epoch = epoch;
            }
            internal string WorldId { get; }
            internal string ZoneKey { get; }
            internal long Epoch { get; }
            internal bool IsValid => !string.IsNullOrWhiteSpace(WorldId) &&
                                     !string.IsNullOrWhiteSpace(ZoneKey);
        }

        private readonly struct RecoveredActor
        {
            internal RecoveredActor(Guid actorId, Guid sessionId,
                OntologyAuthorityPlayerMotionState state)
            {
                ActorId = actorId;
                SessionId = sessionId;
                State = state;
            }
            internal Guid ActorId { get; }
            internal Guid SessionId { get; }
            internal OntologyAuthorityPlayerMotionState State { get; }
        }
    }
}
