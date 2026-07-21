using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Chooses the client's read/subscription scope from durable authority zone
    /// boundaries. It does not create facts, simulate physics, or decide gameplay;
    /// those remain server/world responsibilities. With no authored zones it keeps
    /// the backwards-compatible whole-world projection.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldZoneStreamer : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private Transform actorTransform;
        [SerializeField, Min(1f)] private float directoryRefreshSeconds = 15f;
        [SerializeField, Min(0f)] private float nearestZoneActivationDistance = 8f;

        private OntologyAuthorityWorldZoneProjection[] zones = Array.Empty<OntologyAuthorityWorldZoneProjection>();
        private bool isLoadingDirectory;
        private float nextDirectoryRefreshAt;

        public string ActiveZoneKey => authorityClient == null
            ? string.Empty
            : authorityClient.CurrentProjectionZoneKey;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Update()
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsReady)
            {
                return;
            }

            if (!isLoadingDirectory && Time.unscaledTime >= nextDirectoryRefreshAt)
            {
                StartCoroutine(LoadZoneDirectoryRoutine());
            }

            UpdateProjectionScope();
        }

        [ContextMenu("Refresh Authority Zone Directory")]
        public void RefreshZoneDirectory()
        {
            if (!isLoadingDirectory && authorityClient != null && authorityClient.IsReady)
            {
                StartCoroutine(LoadZoneDirectoryRoutine());
            }
        }

        private IEnumerator LoadZoneDirectoryRoutine()
        {
            isLoadingDirectory = true;
            yield return authorityClient.LoadWorldZonesRoutine(value =>
            {
                zones = value ?? Array.Empty<OntologyAuthorityWorldZoneProjection>();
            });
            isLoadingDirectory = false;
            nextDirectoryRefreshAt = Time.unscaledTime + Mathf.Max(1f, directoryRefreshSeconds);
            UpdateProjectionScope();
        }

        private void UpdateProjectionScope()
        {
            if (authorityClient == null || actorTransform == null || zones == null || zones.Length == 0)
            {
                return;
            }

            var position = actorTransform.position;
            var current = FindByKey(authorityClient.CurrentProjectionZoneKey);
            // Do not oscillate at an overlapping boundary. The current valid Zone
            // remains selected until the actor actually leaves it.
            if (current != null && Contains(current, position))
            {
                return;
            }

            var next = FindContainingZone(position) ?? FindNearestZone(position);
            var nextKey = next == null ? string.Empty : next.zoneKey;
            if (!string.Equals(authorityClient.CurrentProjectionZoneKey, nextKey,
                    StringComparison.Ordinal))
            {
                authorityClient.SetProjectionZoneKey(nextKey);
            }
        }

        private OntologyAuthorityWorldZoneProjection FindContainingZone(Vector3 position)
        {
            OntologyAuthorityWorldZoneProjection candidate = null;
            foreach (var zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.zoneKey) || !Contains(zone, position))
                {
                    continue;
                }

                if (candidate == null || string.CompareOrdinal(zone.zoneKey, candidate.zoneKey) < 0)
                {
                    candidate = zone;
                }
            }
            return candidate;
        }

        private OntologyAuthorityWorldZoneProjection FindNearestZone(Vector3 position)
        {
            var maxDistanceSquared = nearestZoneActivationDistance * nearestZoneActivationDistance;
            var nearestDistanceSquared = maxDistanceSquared;
            OntologyAuthorityWorldZoneProjection nearest = null;
            foreach (var zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.zoneKey)) continue;
                var closestX = Mathf.Clamp(position.x, zone.minX, zone.maxX);
                var closestZ = Mathf.Clamp(position.z, zone.minZ, zone.maxZ);
                var distanceSquared = (new Vector2(position.x - closestX, position.z - closestZ)).sqrMagnitude;
                if (distanceSquared > nearestDistanceSquared) continue;

                if (nearest == null || distanceSquared < nearestDistanceSquared ||
                    string.CompareOrdinal(zone.zoneKey, nearest.zoneKey) < 0)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearest = zone;
                }
            }
            return nearest;
        }

        private OntologyAuthorityWorldZoneProjection FindByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var zone in zones)
            {
                if (zone != null && string.Equals(zone.zoneKey, key, StringComparison.Ordinal))
                {
                    return zone;
                }
            }
            return null;
        }

        private static bool Contains(OntologyAuthorityWorldZoneProjection zone, Vector3 position)
        {
            return position.x >= zone.minX && position.x <= zone.maxX &&
                   position.z >= zone.minZ && position.z <= zone.maxZ;
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }

            if (actorTransform == null)
            {
                actorTransform = FindAnyObjectByType<OntologyPlayerPositionTracker>()?.transform;
            }
        }
    }
}
