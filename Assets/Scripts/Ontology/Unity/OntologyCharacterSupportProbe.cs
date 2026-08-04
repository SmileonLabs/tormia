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
    /// Observes Unity collision support beneath the CharacterController capsule.
    /// It never computes a replacement step or movement coordinate. Unity's
    /// CharacterController remains the sole collision and step resolver; this
    /// probe only classifies its nearby support as ephemeral ontology evidence.
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
            ResolveController();
            if (controller == null || !controller.enabled)
            {
                return OntologyCharacterSupportState.None;
            }

            GetCapsule(transform.position, out var bottom, out _, out var radius);
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
                Mathf.Clamp(controller.slopeLimit, 0f, 89f) * Mathf.Deg2Rad);
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSelf(hit.collider) ||
                    !OntologyCollisionRoleAdapter.IsWalkableSupport(hit.collider) ||
                    Vector3.Dot(hit.normal, Vector3.up) < minimumUpwardNormal ||
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

        /// <summary>
        /// Resolves the one-time world-entry alignment offset from Unity cast
        /// evidence. Runtime locomotion never calls this method and never writes
        /// a step coordinate from it.
        /// </summary>
        public bool TryResolveGroundingOffset(
            float maximumDown,
            float maximumUp,
            float clearance,
            out float offset)
        {
            offset = 0f;
            ResolveController();
            if (controller == null || !controller.enabled)
            {
                return false;
            }

            GetCapsule(transform.position, out var bottom, out _, out var radius);
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
                Mathf.Clamp(controller.slopeLimit, 0f, 89f) * Mathf.Deg2Rad);
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (IsSelf(hit.collider) ||
                    !OntologyCollisionRoleAdapter.IsWalkableSupport(hit.collider) ||
                    Vector3.Dot(hit.normal, Vector3.up) < minimumUpwardNormal ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                var contactClearance = Mathf.Max(
                    Mathf.Max(0f, clearance),
                    controller.skinWidth);
                var upwardNormal = Mathf.Max(
                    0.01f,
                    Vector3.Dot(hit.normal, Vector3.up));
                var castCenterY = origin.y - hit.distance;
                offset =
                    castCenterY +
                    (radius + contactClearance - castRadius) / upwardNormal -
                    bottom.y;
            }
            return bestDistance < float.PositiveInfinity &&
                   offset >= -Mathf.Max(0f, maximumDown) &&
                   offset <= Mathf.Max(0f, maximumUp);
        }

        private void ResolveController()
        {
            controller ??= GetComponent<CharacterController>();
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
                transform.rotation * Vector3.Scale(controller.center, scale);
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
