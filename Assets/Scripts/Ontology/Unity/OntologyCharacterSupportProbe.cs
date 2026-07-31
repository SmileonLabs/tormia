using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public struct OntologyCharacterSupportState
    {
        public bool hasSupport;
        public Collider collider;
        public Vector3 point;
        public Vector3 normal;
        public float distance;

        public static OntologyCharacterSupportState None =>
            new()
            {
                hasSupport = false,
                collider = null,
                point = Vector3.zero,
                normal = Vector3.up,
                distance = float.PositiveInfinity
            };
    }

    /// <summary>
    /// Observes the current local collision support using the CharacterController
    /// capsule. It does not author durable Facts or decide whether locomotion is
    /// allowed; it supplies ephemeral collision evidence to the motion owner.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class OntologyCharacterSupportProbe : MonoBehaviour
    {
        [SerializeField] private LayerMask probeLayers =
            Physics.DefaultRaycastLayers;
        [SerializeField, Min(0.001f)] private float probeStartOffset = 0.05f;
        [SerializeField, Min(0.001f)] private float probeDistance = 0.18f;
        [SerializeField, Range(0f, 1f)] private float radiusScale = 0.92f;

        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Collider[] overlaps = new Collider[32];
        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public void ConfigureProbeLayers(int layerMask)
        {
            probeLayers = layerMask;
        }

        public OntologyCharacterSupportState Probe()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterController>();
            }
            if (controller == null || !controller.enabled)
            {
                return OntologyCharacterSupportState.None;
            }

            GetCapsule(
                transform.position,
                out var bottom,
                out _,
                out var radius);
            var castRadius = Mathf.Max(
                0.01f,
                radius * Mathf.Clamp(radiusScale, 0.1f, 1f));
            var origin = bottom + Vector3.up * probeStartOffset;
            var count = Physics.SphereCastNonAlloc(
                origin,
                castRadius,
                Vector3.down,
                hits,
                probeStartOffset + probeDistance,
                probeLayers,
                QueryTriggerInteraction.Ignore);

            var best = OntologyCharacterSupportState.None;
            var minimumUpwardNormal = Mathf.Cos(
                Mathf.Clamp(controller.slopeLimit, 0f, 89f) *
                Mathf.Deg2Rad);
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSelf(hit.collider) ||
                    !OntologyCollisionRoleAdapter.IsWalkableSupport(
                        hit.collider) ||
                    Vector3.Dot(hit.normal, Vector3.up) <
                    minimumUpwardNormal ||
                    hit.distance >= best.distance)
                {
                    continue;
                }

                best = new OntologyCharacterSupportState
                {
                    hasSupport = true,
                    collider = hit.collider,
                    point = hit.point,
                    normal = hit.normal,
                    distance = hit.distance
                };
            }
            return best;
        }

        public bool TryResolveStep(
            Vector3 horizontalDisplacement,
            OntologyCharacterSupportState currentSupport,
            float maximumStepHeight,
            out float rise)
        {
            rise = 0f;
            if (!currentSupport.hasSupport ||
                maximumStepHeight <= 0f)
            {
                return false;
            }

            var planar = Vector3.ProjectOnPlane(
                horizontalDisplacement,
                Vector3.up);
            var distance = planar.magnitude;
            if (distance <= 0.0001f)
            {
                return false;
            }

            GetCapsule(
                transform.position,
                out var bottom,
                out var top,
                out var radius);
            var direction = planar / distance;
            var obstacleCount = Physics.CapsuleCastNonAlloc(
                bottom,
                top,
                Mathf.Max(0.01f, radius - controller.skinWidth),
                direction,
                hits,
                distance + controller.skinWidth,
                probeLayers,
                QueryTriggerInteraction.Ignore);
            RaycastHit obstacle = default;
            var foundObstacle = false;
            var minimumUpwardNormal = Mathf.Cos(
                Mathf.Clamp(controller.slopeLimit, 0f, 89f) *
                Mathf.Deg2Rad);
            for (var index = 0; index < obstacleCount; index++)
            {
                var candidate = hits[index];
                if (IsSelf(candidate.collider))
                {
                    continue;
                }

                var candidateIsWalkable =
                    OntologyCollisionRoleAdapter.IsWalkableSupport(
                        candidate.collider);
                if (!candidateIsWalkable)
                {
                    // Actor bodies and dynamic props are obstacles, never steps.
                    return false;
                }

                if (!CanTreatAsStepObstacle(
                        currentSupport.collider,
                        candidate.collider,
                        candidateIsWalkable,
                        candidate.normal,
                        minimumUpwardNormal))
                {
                    // A face belonging to the current support collider is part
                    // of the same collision surface. Low-poly triangle seams
                    // and shoreline edges must not become an authored upward
                    // step displacement.
                    continue;
                }

                if (!foundObstacle ||
                    candidate.distance < obstacle.distance)
                {
                    obstacle = candidate;
                    foundObstacle = true;
                }
            }
            if (!foundObstacle)
            {
                return false;
            }

            var downOrigin =
                bottom +
                direction * (distance + controller.skinWidth) +
                Vector3.up * (maximumStepHeight + probeStartOffset);
            var topCount = Physics.RaycastNonAlloc(
                downOrigin,
                Vector3.down,
                hits,
                maximumStepHeight + probeStartOffset + probeDistance,
                probeLayers,
                QueryTriggerInteraction.Ignore);
            RaycastHit stepTop = default;
            var foundTop = false;
            for (var index = 0; index < topCount; index++)
            {
                var candidate = hits[index];
                if (IsSelf(candidate.collider) ||
                    !OntologyCollisionRoleAdapter.IsWalkableSupport(
                        candidate.collider) ||
                    Vector3.Dot(candidate.normal, Vector3.up) <
                    minimumUpwardNormal)
                {
                    continue;
                }
                if (!foundTop || candidate.point.y > stepTop.point.y)
                {
                    stepTop = candidate;
                    foundTop = true;
                }
            }
            if (!foundTop)
            {
                return false;
            }

            rise = stepTop.point.y - currentSupport.point.y;
            if (rise <= 0.001f || rise > maximumStepHeight)
            {
                rise = 0f;
                return false;
            }

            GetCapsule(
                transform.position + planar + Vector3.up * rise,
                out var destinationBottom,
                out var destinationTop,
                out var destinationRadius);
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                destinationBottom,
                destinationTop,
                Mathf.Max(
                    0.01f,
                    destinationRadius - controller.skinWidth),
                overlaps,
                probeLayers,
                QueryTriggerInteraction.Ignore);
            for (var index = 0; index < overlapCount; index++)
            {
                var collider = overlaps[index];
                if (IsSelf(collider) ||
                    collider == stepTop.collider)
                {
                    continue;
                }
                rise = 0f;
                return false;
            }
            return true;
        }

        public static bool CanTreatAsStepObstacle(
            Collider currentSupport,
            Collider candidate,
            bool candidateIsWalkable,
            Vector3 candidateNormal,
            float minimumUpwardNormal)
        {
            return candidate != null &&
                   candidate != currentSupport &&
                   candidateIsWalkable &&
                   Vector3.Dot(candidateNormal, Vector3.up) <
                   minimumUpwardNormal;
        }

        public bool TryResolveGroundingOffset(
            float maximumDown,
            float maximumUp,
            float clearance,
            out float offset)
        {
            offset = 0f;
            GetCapsule(
                transform.position,
                out var bottom,
                out _,
                out var radius);
            var origin = bottom + Vector3.up * Mathf.Max(0f, maximumUp);
            var castRadius = Mathf.Max(
                0.01f,
                radius * Mathf.Clamp(radiusScale, 0.1f, 1f));
            var count = Physics.SphereCastNonAlloc(
                origin,
                castRadius,
                Vector3.down,
                hits,
                Mathf.Max(0.001f, maximumUp + maximumDown + clearance),
                probeLayers,
                QueryTriggerInteraction.Ignore);
            var bestDistance = float.PositiveInfinity;
            var minimumUpwardNormal = Mathf.Cos(
                Mathf.Clamp(controller.slopeLimit, 0f, 89f) *
                Mathf.Deg2Rad);
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSelf(hit.collider) ||
                    !OntologyCollisionRoleAdapter.IsWalkableSupport(
                        hit.collider) ||
                    Vector3.Dot(hit.normal, Vector3.up) <
                    minimumUpwardNormal ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                // SphereCast uses a slightly reduced observation radius so it
                // can see seams without starting overlapped. Settling must use
                // the actual controller radius along the observed surface
                // normal; adding the radius only on world Y over-raises a
                // capsule on a slope and makes the first Move correct it.
                var contactClearance = Mathf.Max(
                    Mathf.Max(0f, clearance),
                    controller.skinWidth);
                var upwardNormal = Mathf.Max(
                    0.01f,
                    Vector3.Dot(hit.normal, Vector3.up));
                var castCenterY = origin.y - hit.distance;
                offset =
                    castCenterY +
                    (radius + contactClearance - castRadius) /
                    upwardNormal -
                    bottom.y;
            }
            return bestDistance < float.PositiveInfinity &&
                   offset >= -Mathf.Max(0f, maximumDown) &&
                   offset <= Mathf.Max(0f, maximumUp);
        }

        private void GetCapsule(
            Vector3 rootPosition,
            out Vector3 bottom,
            out Vector3 top,
            out float radius)
        {
            var scale = transform.lossyScale;
            radius = controller.radius *
                     Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            radius = Mathf.Max(0.001f, radius);
            var height = Mathf.Max(
                controller.height * Mathf.Abs(scale.y),
                radius * 2f);
            var worldCenter =
                rootPosition +
                transform.rotation *
                Vector3.Scale(controller.center, scale);
            var halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            bottom = worldCenter + Vector3.down * halfSegment;
            top = worldCenter + Vector3.up * halfSegment;
        }

        private bool IsSelf(Collider collider)
        {
            return collider == null ||
                   collider.transform == transform ||
                   collider.transform.IsChildOf(transform);
        }
    }
}
