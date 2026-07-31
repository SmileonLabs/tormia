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
        [SerializeField, Min(0.01f)] private float maximumVisualSpeed = 8f;
        [SerializeField, Min(0.25f)] private float despawnGraceSeconds = 2f;
        [SerializeField] private string horizontalAnimatorParameter = "Hor";
        [SerializeField] private string verticalAnimatorParameter = "Vert";
        [SerializeField, TextArea] private string lastStatus;

        private readonly Dictionary<Guid, RemoteReplica> replicas = new();
        private readonly Dictionary<Guid, RemoteAvatarAppearance> appearances = new();
        private bool isLoading;
        private float nextPollAt;

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
            if (realtimeClient != null)
            {
                realtimeClient.ZoneRuntimeChanged += HandleZoneRuntimeChanged;
            }
        }

        private void Update()
        {
            ResolveDependencies();
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
                var next = Vector3.MoveTowards(
                    current,
                    replica.TargetPosition,
                    Mathf.Max(0.01f, maximumVisualSpeed) * Time.deltaTime);
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
                ApplyMotionAnimation(replica, horizontalDirection.sqrMagnitude > 0.0001f);
            }
        }

        private void OnDisable()
        {
            if (authorityClient != null)
            {
                authorityClient.ProjectionReceived -= HandleProjectionReceived;
            }
            if (realtimeClient != null)
            {
                realtimeClient.ZoneRuntimeChanged -= HandleZoneRuntimeChanged;
            }
            ClearReplicas();
        }

        private IEnumerator LoadZoneSnapshotRoutine(string requestedZoneKey)
        {
            isLoading = true;
            nextPollAt = Time.unscaledTime + ResolveRefreshInterval();
            var receivedSnapshot = false;
            yield return authorityClient.LoadZoneAvatarMotionsRoutine(requestedZoneKey, states =>
            {
                receivedSnapshot = states != null;
                if (!receivedSnapshot ||
                    !string.Equals(requestedZoneKey, zoneStreamer == null ? string.Empty : zoneStreamer.ActiveZoneKey,
                        StringComparison.Ordinal))
                {
                    return;
                }

                ApplySnapshot(states);
            });

            if (!receivedSnapshot)
            {
                SetStatus("Authority did not return a remote-avatar snapshot yet.");
            }
            isLoading = false;
        }

        private void ApplySnapshot(IReadOnlyList<OntologyAuthorityPlayerMotionState> states)
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

                replica.TargetPosition = target;
                replica.LastSeenAt = now;
                replica.MotionStatus = state.motionStatus ?? string.Empty;
                ApplyAppearance(replica, avatarId);
            }

            var staleIds = new List<Guid>();
            foreach (var pair in replicas)
            {
                if (!receivedIds.Contains(pair.Key) && now - pair.Value.LastSeenAt >= despawnGraceSeconds)
                {
                    staleIds.Add(pair.Key);
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

            RequestImmediateRefresh();
        }

        public void RequestImmediateRefresh()
        {
            if (!CanReplicate()) return;
            nextPollAt = 0f;
            if (!isLoading)
            {
                StartCoroutine(LoadZoneSnapshotRoutine(zoneStreamer.ActiveZoneKey));
            }
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

        private void ApplyMotionAnimation(RemoteReplica replica, bool movedThisFrame)
        {
            if (replica.Animator == null) return;
            var moving = string.Equals(replica.MotionStatus, "moving", StringComparison.OrdinalIgnoreCase) || movedThisFrame;
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
            if (zoneStreamer == null) zoneStreamer = FindAnyObjectByType<OntologyWorldZoneStreamer>();
            var resolvedLocalAvatar = ResolveLocalAvatarIdentity();
            if (resolvedLocalAvatar != null &&
                resolvedLocalAvatar != localAvatarIdentity)
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
            return authorityClient != null && authorityClient.RealtimeNotificationsActive
                ? Mathf.Max(0.25f, realtimeRecoveryPollIntervalSeconds)
                : Mathf.Max(0.25f, pollIntervalSeconds);
        }

        private sealed class RemoteReplica
        {
            public RemoteReplica(GameObject root, Transform visualRoot, Animator animator, Vector3 position, float seenAt)
            {
                Root = root;
                VisualRoot = visualRoot;
                Animator = animator;
                TargetPosition = position;
                LastSeenAt = seenAt;
            }

            public GameObject Root { get; }
            public Transform VisualRoot { get; }
            public Animator Animator { get; }
            public Vector3 TargetPosition { get; set; }
            public float LastSeenAt { get; set; }
            public string MotionStatus { get; set; }
            public string AppearanceFingerprint { get; set; }
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
}
