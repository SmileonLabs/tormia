using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only replica for other avatars in the local authority Zone.
    /// It reads the server's ephemeral motion snapshots and never sends input,
    /// writes Facts, changes rule blocks, or modifies durable entity transforms.
    /// Assign a real remote-avatar prefab when appearance replication is ready;
    /// the capsule fallback makes the networking path testable in the meantime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityRemoteAvatarPresenter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityRealtimeClient realtimeClient;
        [SerializeField, Tooltip(
            "Optional Authority motion snapshot feed. Leave empty to use the " +
            "project SignalR plus HTTP recovery feed.")]
        private MonoBehaviour motionSnapshotFeedSource;
        [SerializeField, Tooltip(
            "Session-local transport and interpolation diagnostics. This records " +
            "presentation health only and has no Authority or gameplay ownership.")]
        private OntologyRemoteMotionRuntimeMetrics runtimeMetrics;
        [SerializeField] private OntologyWorldZoneStreamer zoneStreamer;
        [SerializeField] private OntologyAuthorityEntityIdentity localAvatarIdentity;
        [SerializeField] private Transform remoteAvatarRoot;
        [SerializeField] private GameObject remoteAvatarPrefab;
        [SerializeField, Tooltip("Optional source for the controller and Avatar used by remote visual-only instances.")]
        private Animator remoteAnimatorSource;
        [SerializeField, Tooltip("Optional local presentation catalog used to render server-authored equipped_part Facts.")]
        private OntologyCharacterPartDatabase characterPartDatabase;
        [SerializeField, Tooltip("Disabled by default until remote avatar appearance is configured.")]
        private bool replicateRemoteAvatars;
        [SerializeField, Min(0.25f), Tooltip("Fallback interval while realtime notifications are unavailable.")]
        private float pollIntervalSeconds = 0.5f;
        [SerializeField, Min(0.25f), Tooltip("Safety refresh interval while the SignalR notification channel is healthy.")]
        private float realtimeRecoveryPollIntervalSeconds = 2f;
        [SerializeField, Min(0.05f), Tooltip(
            "Coalesces bursty Zone notifications into one bounded HTTP " +
            "snapshot read instead of issuing one request per server tick.")]
        private float minimumNotificationRefreshSeconds = 0.1f;
        [SerializeField, Min(0.01f), Tooltip(
            "Legacy inspector value retained for existing scenes. Remote avatars " +
            "now render the ordered Authority snapshot timeline and do not chase " +
            "a latest-position target at an independent visual speed.")]
        private float maximumVisualSpeed = 8f;
        [SerializeField, Range(0.1f, 0.15f), Tooltip(
            "Remote presentation is intentionally rendered this far behind the " +
            "latest Authority snapshot so two ordered snapshots can be blended.")]
        private float interpolationDelaySeconds = 0.125f;
        [SerializeField, Min(0.01f), Tooltip(
            "Maximum time a remote avatar may continue from an Authority-approved " +
            "velocity when the next snapshot is late. It never predicts input or a destination.")]
        private float maximumSnapshotExtrapolationSeconds = 0.15f;
        [SerializeField, Min(0.25f), Tooltip(
            "A larger Authority snapshot timeline gap starts a new visual baseline " +
            "instead of racing across stale space after a reconnect.")]
        private float maximumSnapshotGapSeconds = 0.75f;
        [SerializeField, Min(4)] private int snapshotBufferCapacity = 24;
        [SerializeField, Min(0.25f)] private float despawnGraceSeconds = 2f;
        [SerializeField] private string horizontalAnimatorParameter = "Hor";
        [SerializeField] private string verticalAnimatorParameter = "Vert";
        [SerializeField, TextArea] private string lastStatus;

        private readonly Dictionary<Guid, RemoteReplica> replicas = new();
        private readonly Dictionary<Guid, RemoteAvatarAppearance> appearances = new();
        private bool isLoading;
        private float nextPollAt;
        private float lastRealtimeMotionFrameAt = float.NegativeInfinity;
        private IAuthorityMotionSnapshotFeed motionSnapshotFeed;
        private IAuthorityMotionSnapshotFeed subscribedMotionSnapshotFeed;
        private bool motionSnapshotFeedSubscriptionActive;
        private float nextDependencyRetryAt;

        public OntologyRemoteMotionRuntimeMetrics RuntimeMetrics => runtimeMetrics;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived += HandleProjectionReceived;
                HandleProjectionReceived(authorityClient.CurrentProjection);
            }
            SetMotionSnapshotFeedSubscription(motionSnapshotFeed);
        }

        private void Update()
        {
            RefreshDependenciesIfNeeded();
            if (!CanReplicate() || isLoading || Time.unscaledTime < nextPollAt)
            {
                return;
            }

            StartCoroutine(LoadZoneSnapshotRoutine(zoneStreamer.ActiveZoneKey));
        }

        private void LateUpdate()
        {
            if (!replicateRemoteAvatars) return;

            foreach (var replica in replicas.Values)
            {
                if (replica.Root == null) continue;
                var current = replica.Root.transform.position;
                if (!TryResolveBufferedRenderSample(
                        replica.Snapshots,
                        Time.unscaledTime,
                        Mathf.Clamp(interpolationDelaySeconds, 0.1f, 0.15f),
                        Mathf.Max(0.01f, maximumSnapshotExtrapolationSeconds),
                        out var rendered))
                {
                    continue;
                }

                var isBufferUnderrun = replica.Snapshots.Count < 2;
                if (isBufferUnderrun && !replica.BufferUnderrunActive)
                {
                    runtimeMetrics?.RecordBufferUnderrunEpisode();
                }
                replica.BufferUnderrunActive = isBufferUnderrun;

                if (rendered.IsExtrapolated && !replica.ExtrapolationActive)
                {
                    runtimeMetrics?.RecordExtrapolationEpisode();
                }
                replica.ExtrapolationActive = rendered.IsExtrapolated;

                // The root is a presentation-only replica. It is assigned from an
                // ordered Authority timeline, never moved toward a latest target at
                // a locally invented speed and never given a client destination.
                var next = rendered.Position;
                var horizontalDirection = next - current;
                horizontalDirection.y = 0f;
                if (horizontalDirection.sqrMagnitude > 0.0001f)
                {
                    replica.Root.transform.rotation = Quaternion.Slerp(
                        replica.Root.transform.rotation,
                        Quaternion.LookRotation(horizontalDirection.normalized, Vector3.up),
                        Mathf.Clamp01(Time.deltaTime * 12f));
                }
                replica.Root.transform.position = next;
                ApplyMotionAnimation(
                    replica,
                    horizontalDirection.sqrMagnitude > 0.0001f,
                    rendered.MotionStatus,
                    rendered.IsExtrapolated);
            }
        }

        private void OnDisable()
        {
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived -= HandleProjectionReceived;
            }
            SetMotionSnapshotFeedSubscription(null);
            ClearReplicas();
        }

        private IEnumerator LoadZoneSnapshotRoutine(string requestedZoneKey)
        {
            isLoading = true;
            nextPollAt = Time.unscaledTime + ResolveRefreshInterval();
            var receivedSnapshot = false;
            var feed = ResolveMotionSnapshotFeed();
            if (feed == null)
            {
                SetStatus("Authority remote-motion snapshot feed is unavailable.");
                isLoading = false;
                yield break;
            }
            yield return feed.LoadZoneAvatarMotionsRoutine(requestedZoneKey, states =>
            {
                receivedSnapshot = states != null;
                if (!receivedSnapshot ||
                    !string.Equals(requestedZoneKey, zoneStreamer == null ? string.Empty : zoneStreamer.ActiveZoneKey,
                        StringComparison.Ordinal))
                {
                    return;
                }

                ApplySnapshot(states, true);
            });

            if (!receivedSnapshot)
            {
                SetStatus("Authority did not return a remote-avatar snapshot yet.");
            }
            isLoading = false;
        }

        private void ApplySnapshot(
            IReadOnlyList<OntologyAuthorityPlayerMotionState> states,
            bool completePresenceSnapshot)
        {
            var now = Time.unscaledTime;
            var receivedIds = new HashSet<Guid>();
            TryGetLocalAvatarId(out var localAvatarId);

            foreach (var state in states)
            {
                if (state == null || !Guid.TryParse(state.avatarEntityId, out var avatarId) ||
                    avatarId == Guid.Empty || avatarId == localAvatarId)
                {
                    continue;
                }

                receivedIds.Add(avatarId);
                var target = new Vector3((float)state.positionX, (float)state.positionY, (float)state.positionZ);
                if (!replicas.TryGetValue(avatarId, out var replica) || replica.Root == null)
                {
                    replica = CreateReplica(avatarId, target);
                    replicas[avatarId] = replica;
                }

                RecordSnapshot(replica, state, target, now);
                replica.LastSeenAt = now;
                ApplyAppearance(replica, avatarId);
            }

            var staleIds = new List<Guid>();
            if (completePresenceSnapshot)
            {
                foreach (var pair in replicas)
                {
                    if (!receivedIds.Contains(pair.Key) &&
                        now - pair.Value.LastSeenAt >= despawnGraceSeconds)
                    {
                        staleIds.Add(pair.Key);
                    }
                }
            }
            foreach (var avatarId in staleIds)
            {
                DestroyReplica(avatarId);
            }

            SetStatus("Remote avatars: " + replicas.Count + ".");
        }

        private RemoteReplica CreateReplica(Guid avatarId, Vector3 position)
        {
            var root = new GameObject("AuthorityRemoteAvatar_" + avatarId.ToString("N"));
            root.transform.SetParent(GetOrCreateRoot(), false);
            root.transform.position = position;

            GameObject visual;
            if (remoteAvatarPrefab != null)
            {
                visual = Instantiate(remoteAvatarPrefab, root.transform);
                visual.name = "Visual";
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "PlaceholderVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.up;
                var collider = visual.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }

            foreach (var collider in visual.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            SetLayerRecursively(root, LayerMask.NameToLayer("Ignore Raycast"));
            var animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null && remoteAnimatorSource != null)
            {
                animator = visual.AddComponent<Animator>();
            }
            if (animator != null && remoteAnimatorSource != null)
            {
                animator.runtimeAnimatorController = remoteAnimatorSource.runtimeAnimatorController;
                animator.avatar = remoteAnimatorSource.avatar;
                animator.applyRootMotion = false;
            }
            return new RemoteReplica(root, visual.transform, animator, position, Time.unscaledTime);
        }

        private void HandleProjectionReceived(OntologyAuthorityWorldProjection projection)
        {
            if (projection?.entities == null) return;
            var partsByAvatar = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var fact in projection.facts ?? Array.Empty<OntologyAuthorityFactProjection>())
            {
                if (fact == null || !string.Equals(fact.predicateId, OntologyPredicates.EquippedPart, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(fact.subjectEntityId) || string.IsNullOrWhiteSpace(fact.objectCanonicalId))
                {
                    continue;
                }

                if (!partsByAvatar.TryGetValue(fact.subjectEntityId, out var parts))
                {
                    parts = new List<string>();
                    partsByAvatar[fact.subjectEntityId] = parts;
                }
                if (!parts.Contains(fact.objectCanonicalId)) parts.Add(fact.objectCanonicalId);
            }

            foreach (var entity in projection.entities)
            {
                if (entity == null || !Guid.TryParse(entity.entityId, out var avatarId)) continue;
                partsByAvatar.TryGetValue(entity.entityId, out var equippedParts);
                appearances[avatarId] = new RemoteAvatarAppearance(
                    entity.templateId,
                    entity.displayName,
                    equippedParts ?? (IReadOnlyList<string>)Array.Empty<string>());
                if (replicas.TryGetValue(avatarId, out var replica))
                {
                    ApplyAppearance(replica, avatarId);
                }
            }

            // Register/remove player commands arrive as durable revisions.
            // Fetching the separate runtime list here keeps presence responsive.
            RequestImmediateRefresh();
        }

        private void HandleZoneRuntimeChanged(OntologyAuthorityZoneRuntimeNotification notification)
        {
            if (notification == null || authorityClient == null || zoneStreamer == null ||
                !string.Equals(notification.worldId, authorityClient.CurrentWorldId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(notification.zoneKey, zoneStreamer.ActiveZoneKey, StringComparison.Ordinal))
            {
                return;
            }

            // A runtime-change notification deliberately carries no transform.
            // When the realtime motion stream is healthy, a companion
            // zoneMotionFrame supplies ordered poses directly. Do not turn every
            // 20 Hz hint into an HTTP read; periodic HTTP remains join/reconnect
            // recovery only.
            if (motionSnapshotFeed == null ||
                !motionSnapshotFeed.IsRealtimeConnected)
            {
                RequestImmediateRefresh();
            }
        }

        private void HandleZoneMotionFrameReceived(
            OntologyAuthorityZoneMotionFrame frame)
        {
            if (frame == null || authorityClient == null || zoneStreamer == null ||
                !string.Equals(
                    frame.worldId,
                    authorityClient.CurrentWorldId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    frame.zoneKey,
                    zoneStreamer.ActiveZoneKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            lastRealtimeMotionFrameAt = Time.unscaledTime;
            // Motion frames are deltas containing only avatars changed in this
            // server tick. Presence eviction belongs to the periodic complete
            // HTTP recovery snapshot, never to an omitted delta entry.
            ApplySnapshot(
                frame.items ?? Array.Empty<OntologyAuthorityPlayerMotionState>(),
                false);
        }

        public void RequestImmediateRefresh()
        {
            if (!CanReplicate()) return;
            nextPollAt = CoalesceRefreshDeadline(
                nextPollAt,
                Time.unscaledTime,
                Mathf.Max(0.05f, minimumNotificationRefreshSeconds));
        }

        public static float CoalesceRefreshDeadline(
            float currentDeadline,
            float now,
            float minimumDelay)
        {
            var candidate = now + Mathf.Max(0.001f, minimumDelay);
            return currentDeadline <= now
                ? candidate
                : Mathf.Min(currentDeadline, candidate);
        }

        private void ApplyAppearance(RemoteReplica replica, Guid avatarId)
        {
            if (replica?.VisualRoot == null) return;
            if (!appearances.TryGetValue(avatarId, out var appearance)) return;

            replica.Root.name = string.IsNullOrWhiteSpace(appearance.DisplayName)
                ? "AuthorityRemoteAvatar_" + avatarId.ToString("N")
                : "AuthorityRemoteAvatar_" + appearance.DisplayName;
            var fingerprint = appearance.Fingerprint;
            if (string.Equals(replica.AppearanceFingerprint, fingerprint, StringComparison.Ordinal)) return;
            replica.AppearanceFingerprint = fingerprint;
            ApplyEquippedParts(replica.VisualRoot, appearance.EquippedPartIds);
        }

        private void ApplyEquippedParts(Transform visualRoot, IReadOnlyList<string> equippedPartIds)
        {
            OntologyCharacterAppearanceProjector.ApplyPresentation(
                characterPartDatabase,
                visualRoot,
                equippedPartIds);
        }

        private void ApplyMotionAnimation(
            RemoteReplica replica,
            bool movedThisFrame,
            string renderedMotionStatus,
            bool isExtrapolated)
        {
            if (replica.Animator == null) return;
            // A stale extrapolation must not leave the remote avatar walking in
            // place. During a valid buffered segment, both the evaluated server
            // status and the actual rendered displacement can drive presentation.
            var moving = !isExtrapolated &&
                         (string.Equals(
                              renderedMotionStatus,
                              "moving",
                              StringComparison.OrdinalIgnoreCase) ||
                          movedThisFrame);
            if (HasAnimatorParameter(replica.Animator, verticalAnimatorParameter))
            {
                replica.Animator.SetFloat(verticalAnimatorParameter, moving ? 1f : 0f);
            }
            if (HasAnimatorParameter(replica.Animator, horizontalAnimatorParameter))
            {
                replica.Animator.SetFloat(horizontalAnimatorParameter, 0f);
            }
        }

        private static bool HasAnimatorParameter(Animator animator, string parameterName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName)) return false;
            foreach (var parameter in animator.parameters)
            {
                if (string.Equals(parameter.name, parameterName, StringComparison.Ordinal) &&
                    parameter.type == AnimatorControllerParameterType.Float)
                {
                    return true;
                }
            }
            return false;
        }

        private Transform GetOrCreateRoot()
        {
            if (remoteAvatarRoot != null) return remoteAvatarRoot;
            var root = new GameObject("AuthorityRemoteAvatars");
            root.transform.SetParent(transform, false);
            remoteAvatarRoot = root.transform;
            return remoteAvatarRoot;
        }

        private void DestroyReplica(Guid avatarId)
        {
            if (!replicas.TryGetValue(avatarId, out var replica)) return;
            if (replica.Root != null) Destroy(replica.Root);
            replicas.Remove(avatarId);
        }

        private void ClearReplicas()
        {
            foreach (var replica in replicas.Values)
            {
                if (replica.Root != null) Destroy(replica.Root);
            }
            replicas.Clear();
        }

        private bool CanReplicate()
        {
            return replicateRemoteAvatars && authorityClient != null && authorityClient.IsWorldRuntimeReady &&
                   zoneStreamer != null && !string.IsNullOrWhiteSpace(zoneStreamer.ActiveZoneKey);
        }

        private bool TryGetLocalAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            return localAvatarIdentity != null && localAvatarIdentity.TryGetGuid(out avatarId);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (realtimeClient == null) realtimeClient = FindAnyObjectByType<OntologyWorldAuthorityRealtimeClient>();
            ResolveMotionSnapshotFeed();
            SetMotionSnapshotFeedSubscription(motionSnapshotFeed);
            if (runtimeMetrics == null)
            {
                runtimeMetrics = realtimeClient == null
                    ? GetComponent<OntologyRemoteMotionRuntimeMetrics>()
                    : realtimeClient.RuntimeMetrics;
            }
            if (zoneStreamer == null) zoneStreamer = FindAnyObjectByType<OntologyWorldZoneStreamer>();
            if (localAvatarIdentity == null)
            {
                var resolvedLocalAvatar = ResolveLocalAvatarIdentity();
                if (resolvedLocalAvatar != null)
                {
                    localAvatarIdentity = resolvedLocalAvatar;
                    if (localAvatarIdentity.TryGetGuid(out var localAvatarId))
                    {
                        // A stale or incorrectly resolved local identity may already
                        // have produced a presentation-only replica. Remove it as soon
                        // as the account/input-owned avatar becomes available.
                        DestroyReplica(localAvatarId);
                    }
                }
            }
            if (remoteAnimatorSource == null)
            {
                foreach (var candidate in FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
                {
                    if (candidate != null && candidate.avatar != null && candidate.isHuman)
                    {
                        remoteAnimatorSource = candidate;
                        break;
                    }
                }
            }
        }

        private void RefreshDependenciesIfNeeded()
        {
            if (authorityClient != null && zoneStreamer != null &&
                motionSnapshotFeed != null && localAvatarIdentity != null)
            {
                return;
            }
            if (Time.unscaledTime < nextDependencyRetryAt)
            {
                return;
            }

            nextDependencyRetryAt = Time.unscaledTime + 1f;
            ResolveDependencies();
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
            feed.Configure(authorityClient, realtimeClient);
            motionSnapshotFeedSource = feed;
            motionSnapshotFeed = feed;
            return motionSnapshotFeed;
        }

        private void SetMotionSnapshotFeedSubscription(
            IAuthorityMotionSnapshotFeed value)
        {
            if (!ReferenceEquals(subscribedMotionSnapshotFeed, value))
            {
                if (subscribedMotionSnapshotFeed != null &&
                    motionSnapshotFeedSubscriptionActive)
                {
                    subscribedMotionSnapshotFeed.ZoneRuntimeChanged -=
                        HandleZoneRuntimeChanged;
                    subscribedMotionSnapshotFeed.ZoneMotionFrameReceived -=
                        HandleZoneMotionFrameReceived;
                }
                subscribedMotionSnapshotFeed = value;
                motionSnapshotFeedSubscriptionActive = false;
            }

            if (subscribedMotionSnapshotFeed != null &&
                isActiveAndEnabled &&
                !motionSnapshotFeedSubscriptionActive)
            {
                subscribedMotionSnapshotFeed.ZoneRuntimeChanged +=
                    HandleZoneRuntimeChanged;
                subscribedMotionSnapshotFeed.ZoneMotionFrameReceived +=
                    HandleZoneMotionFrameReceived;
                motionSnapshotFeedSubscriptionActive = true;
            }
        }

        private static OntologyAuthorityEntityIdentity ResolveLocalAvatarIdentity()
        {
            var entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                FindObjectsInactive.Include);
            if (entryFlow != null && entryFlow.AvatarIdentity != null)
            {
                return entryFlow.AvatarIdentity;
            }

            var localInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                FindObjectsInactive.Include);
            return localInput == null
                ? null
                : localInput.GetComponent<OntologyAuthorityEntityIdentity>();
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            if (target == null || layer < 0) return;
            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
        }

        private float ResolveRefreshInterval()
        {
            if (motionSnapshotFeed == null ||
                !motionSnapshotFeed.IsRealtimeConnected)
            {
                return Mathf.Max(0.25f, pollIntervalSeconds);
            }

            // Healthy realtime frames own ordinary remote presentation. An HTTP
            // read remains a slow recovery path for a missed first frame, a proxy
            // that drops frames, or a reconnect; it is not a second pose stream.
            var recoveryInterval =
                Mathf.Max(0.25f, realtimeRecoveryPollIntervalSeconds);
            return Time.unscaledTime - lastRealtimeMotionFrameAt > recoveryInterval
                ? Mathf.Max(0.25f, pollIntervalSeconds)
                : recoveryInterval;
        }

        private void RecordSnapshot(
            RemoteReplica replica,
            OntologyAuthorityPlayerMotionState state,
            Vector3 position,
            float receivedAt)
        {
            if (replica == null || state == null) return;

            var incoming = new RemoteAvatarSnapshotSample(
                state.serverTick,
                state.updatedAtUnixMilliseconds,
                state.runtimeSessionId,
                position,
                new Vector3(
                    (float)state.velocityX,
                    (float)state.velocityY,
                    (float)state.velocityZ),
                state.motionStatus,
                receivedAt);
            var requiresBaseline = replica.Snapshots.Count == 0;
            var timelineReset = false;
            if (!requiresBaseline)
            {
                var latest = replica.Snapshots[replica.Snapshots.Count - 1];
                var sessionChanged = HasSessionChanged(
                    latest.RuntimeSessionId,
                    incoming.RuntimeSessionId);
                var timelineGap = HasTimelineGap(
                    latest,
                    incoming,
                    Mathf.Max(0.25f, maximumSnapshotGapSeconds));
                if (sessionChanged || timelineGap ||
                    ShouldResetSnapshotTimeline(latest, incoming))
                {
                    replica.Snapshots.Clear();
                    requiresBaseline = true;
                    timelineReset = true;
                }
                else if (!IsSnapshotNewer(latest, incoming))
                {
                    // HTTP recovery may deliver a repeated or older snapshot after
                    // a SignalR gap. It must never rewind the presentation timeline.
                    runtimeMetrics?.RecordSnapshotRejected(
                        incoming.ServerTick,
                        incoming.ServerTick < latest.ServerTick);
                    return;
                }
            }

            replica.Snapshots.Add(incoming);
            runtimeMetrics?.RecordSnapshotAccepted(
                incoming.ServerTick,
                incoming.UpdatedAtUnixMilliseconds);
            var capacity = Mathf.Max(4, snapshotBufferCapacity);
            if (replica.Snapshots.Count > capacity)
            {
                replica.Snapshots.RemoveAt(0);
            }

            if (requiresBaseline && replica.Root != null)
            {
                runtimeMetrics?.RecordTimelineBaseline(timelineReset);
                // Joining/rejoining has no predecessor to interpolate from. Set one
                // authoritative baseline and wait for the ordered stream; do not
                // animate a catch-up toward a potentially seconds-old target.
                replica.Root.transform.position = incoming.Position;
                replica.LastRenderedPosition = incoming.Position;
            }
        }

        public static bool IsSnapshotNewer(
            RemoteAvatarSnapshotSample previous,
            RemoteAvatarSnapshotSample incoming)
        {
            if (incoming.ServerTick > previous.ServerTick) return true;
            if (incoming.ServerTick < previous.ServerTick) return false;
            return incoming.UpdatedAtUnixMilliseconds >
                   previous.UpdatedAtUnixMilliseconds;
        }

        public static bool ShouldResetSnapshotTimeline(
            RemoteAvatarSnapshotSample previous,
            RemoteAvatarSnapshotSample incoming)
        {
            // A process/runtime restart can reset a tick while preserving an old
            // session record for a short lease interval. A materially newer server
            // timestamp is a new timeline, not an out-of-order packet.
            return incoming.ServerTick < previous.ServerTick &&
                   incoming.UpdatedAtUnixMilliseconds >
                   previous.UpdatedAtUnixMilliseconds + 1000L;
        }

        public static bool HasSessionChanged(string previousSessionId, string incomingSessionId)
        {
            return !string.IsNullOrWhiteSpace(previousSessionId) &&
                   !string.IsNullOrWhiteSpace(incomingSessionId) &&
                   !string.Equals(
                       previousSessionId,
                       incomingSessionId,
                       StringComparison.Ordinal);
        }

        public static bool HasTimelineGap(
            RemoteAvatarSnapshotSample previous,
            RemoteAvatarSnapshotSample incoming,
            float maximumGapSeconds)
        {
            if (previous.UpdatedAtUnixMilliseconds <= 0L ||
                incoming.UpdatedAtUnixMilliseconds <= 0L)
            {
                return false;
            }

            return incoming.UpdatedAtUnixMilliseconds -
                   previous.UpdatedAtUnixMilliseconds >
                   Mathf.Max(0.01f, maximumGapSeconds) * 1000f;
        }

        public static bool TryResolveBufferedRenderSample(
            IReadOnlyList<RemoteAvatarSnapshotSample> snapshots,
            float localNow,
            float interpolationDelay,
            float maximumExtrapolation,
            out RemoteAvatarRenderedSample rendered)
        {
            rendered = default;
            if (snapshots == null || snapshots.Count == 0) return false;

            var latest = snapshots[snapshots.Count - 1];
            if (snapshots.Count == 1 || latest.UpdatedAtUnixMilliseconds <= 0L)
            {
                rendered = new RemoteAvatarRenderedSample(
                    latest.Position,
                    latest.MotionStatus,
                    false);
                return true;
            }

            var localElapsedMilliseconds = Math.Max(
                0d,
                (localNow - latest.ReceivedAtUnscaledTime) * 1000d);
            var targetMilliseconds = latest.UpdatedAtUnixMilliseconds +
                                     localElapsedMilliseconds -
                                     Mathf.Max(0.01f, interpolationDelay) * 1000d;
            var first = snapshots[0];
            if (targetMilliseconds <= first.UpdatedAtUnixMilliseconds)
            {
                rendered = new RemoteAvatarRenderedSample(
                    first.Position,
                    first.MotionStatus,
                    false);
                return true;
            }

            for (var index = 1; index < snapshots.Count; index++)
            {
                var next = snapshots[index];
                if (next.UpdatedAtUnixMilliseconds < targetMilliseconds) continue;
                var previous = snapshots[index - 1];
                var span = Math.Max(
                    1d,
                    next.UpdatedAtUnixMilliseconds -
                    previous.UpdatedAtUnixMilliseconds);
                var interpolation = Mathf.Clamp01((float)(
                    (targetMilliseconds - previous.UpdatedAtUnixMilliseconds) /
                    span));
                rendered = new RemoteAvatarRenderedSample(
                    Vector3.Lerp(previous.Position, next.Position, interpolation),
                    interpolation < 0.5f
                        ? previous.MotionStatus
                        : next.MotionStatus,
                    false);
                return true;
            }

            var extrapolationSeconds = (float)((targetMilliseconds -
                latest.UpdatedAtUnixMilliseconds) / 1000d);
            if (extrapolationSeconds <= Mathf.Max(0f, maximumExtrapolation))
            {
                rendered = new RemoteAvatarRenderedSample(
                    latest.Position + latest.Velocity * extrapolationSeconds,
                    latest.MotionStatus,
                    true);
                return true;
            }

            // The stream is stale. Hold the final approved pose rather than
            // extrapolating toward an input destination that Unity never owns.
            rendered = new RemoteAvatarRenderedSample(
                latest.Position,
                "idle",
                true);
            return true;
        }

        private sealed class RemoteReplica
        {
            public RemoteReplica(GameObject root, Transform visualRoot, Animator animator, Vector3 position, float seenAt)
            {
                Root = root;
                VisualRoot = visualRoot;
                Animator = animator;
                LastRenderedPosition = position;
                LastSeenAt = seenAt;
            }

            public GameObject Root { get; }
            public Transform VisualRoot { get; }
            public Animator Animator { get; }
            public List<RemoteAvatarSnapshotSample> Snapshots { get; } = new();
            public Vector3 LastRenderedPosition { get; set; }
            public float LastSeenAt { get; set; }
            public string AppearanceFingerprint { get; set; }
            public bool BufferUnderrunActive { get; set; }
            public bool ExtrapolationActive { get; set; }
        }

        private sealed class RemoteAvatarAppearance
        {
            public RemoteAvatarAppearance(string templateId, string displayName, IReadOnlyList<string> equippedPartIds)
            {
                TemplateId = templateId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                EquippedPartIds = equippedPartIds ?? Array.Empty<string>();
                Fingerprint = TemplateId + "|" + string.Join("|", EquippedPartIds);
            }

            public string TemplateId { get; }
            public string DisplayName { get; }
            public IReadOnlyList<string> EquippedPartIds { get; }
            public string Fingerprint { get; }
        }
    }

    /// <summary>
    /// One Authority-owned remote movement observation. It is a presentation
    /// transport value only: the server has already evaluated the locomotion
    /// action and collision proxies before publishing it.
    /// </summary>
    public readonly struct RemoteAvatarSnapshotSample
    {
        public RemoteAvatarSnapshotSample(
            long serverTick,
            long updatedAtUnixMilliseconds,
            string runtimeSessionId,
            Vector3 position,
            Vector3 velocity,
            string motionStatus,
            float receivedAtUnscaledTime)
        {
            ServerTick = serverTick;
            UpdatedAtUnixMilliseconds = updatedAtUnixMilliseconds;
            RuntimeSessionId = runtimeSessionId ?? string.Empty;
            Position = position;
            Velocity = velocity;
            MotionStatus = motionStatus ?? string.Empty;
            ReceivedAtUnscaledTime = receivedAtUnscaledTime;
        }

        public long ServerTick { get; }
        public long UpdatedAtUnixMilliseconds { get; }
        public string RuntimeSessionId { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public string MotionStatus { get; }
        public float ReceivedAtUnscaledTime { get; }
    }

    public readonly struct RemoteAvatarRenderedSample
    {
        public RemoteAvatarRenderedSample(
            Vector3 position,
            string motionStatus,
            bool isExtrapolated)
        {
            Position = position;
            MotionStatus = motionStatus ?? string.Empty;
            IsExtrapolated = isExtrapolated;
        }

        public Vector3 Position { get; }
        public string MotionStatus { get; }
        public bool IsExtrapolated { get; }
    }
}
