using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Converts a pointer ray into an Authority entity candidate. This adapter
    /// owns only Unity observation/filtering: World Authority still decides
    /// whether the requested action is permitted.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthorityTargetingAdapter : MonoBehaviour
    {
        [SerializeField] private LayerMask candidateLayers = ~0;
        [SerializeField, Min(1f)] private float maximumRayDistance = 500f;
        [SerializeField, Range(4, 128)] private int maximumHitCount = 32;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Collide;
        [SerializeField, Min(0f)] private float surfaceAssistPadding = 0.15f;

        private RaycastHit[] hitBuffer;

        public bool TryResolvePointerCandidate(
            Camera worldCamera,
            Vector2 screenPosition,
            OntologyAuthorityEntityIdentity actorIdentity,
            OntologyAuthorityEntityIdentity equippedToolIdentity,
            OntologyWorldAuthorityClient authorityClient,
            out OntologyAuthorityEntityIdentity targetIdentity,
            out Vector3 hitPoint)
        {
            targetIdentity = null;
            hitPoint = default;
            if (worldCamera == null || actorIdentity == null ||
                authorityClient == null)
            {
                return false;
            }

            EnsureBuffer();
            var ray = worldCamera.ScreenPointToRay(screenPosition);
            var count = Physics.RaycastNonAlloc(
                ray,
                hitBuffer,
                Mathf.Max(1f, maximumRayDistance),
                candidateLayers,
                triggerInteraction);
            if (count <= 0)
            {
                return false;
            }

            Array.Sort(
                hitBuffer,
                0,
                count,
                RaycastHitDistanceComparer.Instance);
            var actorRoot = actorIdentity.transform;
            var toolRoot = equippedToolIdentity == null
                ? null
                : equippedToolIdentity.transform;
            var hasSurfaceHit = false;
            var surfaceHitPoint = default(Vector3);
            for (var index = 0; index < count; index++)
            {
                var collider = hitBuffer[index].collider;
                if (collider == null ||
                    IsPartOf(collider.transform, actorRoot) ||
                    IsPartOf(collider.transform, toolRoot))
                {
                    continue;
                }

                if (!collider.isTrigger && !hasSurfaceHit)
                {
                    hasSurfaceHit = true;
                    surfaceHitPoint = hitBuffer[index].point;
                }

                var candidate =
                    collider.GetComponentInParent<
                        OntologyAuthorityEntityIdentity>();
                if (candidate == null ||
                    candidate == actorIdentity ||
                    candidate == equippedToolIdentity ||
                    !candidate.TryGetGuid(out var candidateId) ||
                    !authorityClient.ContainsProjectedEntity(candidateId))
                {
                    continue;
                }

                if (collider.GetComponentInParent<
                        OntologyCombatTargetPresenter>() == null)
                {
                    continue;
                }

                targetIdentity = candidate;
                hitPoint = hitBuffer[index].point;
                return true;
            }

            return hasSurfaceHit &&
                   TryResolveSurfaceAssistedCombatCandidate(
                       worldCamera,
                       surfaceHitPoint,
                       actorRoot,
                       toolRoot,
                       authorityClient,
                       out targetIdentity,
                       out hitPoint);
        }

        public static bool IsPartOf(
            Transform candidate,
            Transform excludedRoot)
        {
            return candidate != null &&
                   excludedRoot != null &&
                   (candidate == excludedRoot ||
                     candidate.IsChildOf(excludedRoot));
        }

        public static bool IsWithinPlanarBoundsFootprint(
            Bounds bounds,
            Vector3 surfacePoint,
            float padding)
        {
            var clampedPadding = Mathf.Max(0f, padding);
            var deltaX = Mathf.Max(
                Mathf.Abs(surfacePoint.x - bounds.center.x) -
                bounds.extents.x,
                0f);
            var deltaZ = Mathf.Max(
                Mathf.Abs(surfacePoint.z - bounds.center.z) -
                bounds.extents.z,
                0f);
            return (deltaX * deltaX) + (deltaZ * deltaZ) <=
                   clampedPadding * clampedPadding;
        }

        private bool TryResolveSurfaceAssistedCombatCandidate(
            Camera worldCamera,
            Vector3 surfacePoint,
            Transform actorRoot,
            Transform toolRoot,
            OntologyWorldAuthorityClient authorityClient,
            out OntologyAuthorityEntityIdentity targetIdentity,
            out Vector3 hitPoint)
        {
            targetIdentity = null;
            hitPoint = default;
            var bestScore = float.PositiveInfinity;
            foreach (var presenter in
                     FindObjectsByType<OntologyCombatTargetPresenter>(
                         FindObjectsInactive.Include))
            {
                if (presenter == null || !presenter.isActiveAndEnabled)
                    continue;

                var identity = presenter.AuthorityIdentity;
                var collider = presenter.InteractionCollider;
                if (identity == null ||
                    collider == null ||
                    !collider.enabled ||
                    identity.transform == actorRoot ||
                    identity.transform == toolRoot ||
                    !identity.TryGetGuid(out var candidateId) ||
                    !authorityClient.ContainsProjectedEntity(candidateId) ||
                    !IsWithinPlanarBoundsFootprint(
                        collider.bounds,
                        surfacePoint,
                        surfaceAssistPadding))
                {
                    continue;
                }

                var targetPoint = collider.bounds.center;
                if (!HasUnobstructedPointerLine(
                        worldCamera.transform.position,
                        targetPoint,
                        actorRoot,
                        toolRoot,
                        identity.transform))
                {
                    continue;
                }

                var planarDelta = new Vector2(
                    surfacePoint.x - targetPoint.x,
                    surfacePoint.z - targetPoint.z);
                var score = planarDelta.sqrMagnitude;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                targetIdentity = identity;
                hitPoint = collider.ClosestPoint(surfacePoint);
            }

            return targetIdentity != null;
        }

        private bool HasUnobstructedPointerLine(
            Vector3 origin,
            Vector3 targetPoint,
            Transform actorRoot,
            Transform toolRoot,
            Transform targetRoot)
        {
            var offset = targetPoint - origin;
            var distance = offset.magnitude;
            if (distance <= Mathf.Epsilon)
                return true;

            var hits = Physics.RaycastAll(
                origin,
                offset / distance,
                distance + 0.01f,
                candidateLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, RaycastHitDistanceComparer.Instance);
            foreach (var hit in hits)
            {
                var collider = hit.collider;
                if (collider == null ||
                    IsPartOf(collider.transform, actorRoot) ||
                    IsPartOf(collider.transform, toolRoot))
                {
                    continue;
                }

                if (IsPartOf(collider.transform, targetRoot))
                    return true;
                if (!collider.isTrigger)
                    return false;
            }

            return false;
        }

        private void EnsureBuffer()
        {
            var size = Mathf.Clamp(maximumHitCount, 4, 128);
            if (hitBuffer == null || hitBuffer.Length != size)
            {
                hitBuffer = new RaycastHit[size];
            }
        }

        private sealed class RaycastHitDistanceComparer :
            System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new();

            public int Compare(RaycastHit left, RaycastHit right) =>
                left.distance.CompareTo(right.distance);
        }
    }
}
