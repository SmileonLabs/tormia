using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Connects a trigger volume to the semantic WaterRegion represented by an OntologyObject.
    /// The component does not decide movement or animation; it only identifies the region.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class OntologyWaterRegionVolume : MonoBehaviour
    {
        [SerializeField] private OntologyObject regionObject;
        [Header("Water Surface")]
        [Tooltip("Offset from the trigger volume's top face to the visible water surface.")]
        [SerializeField] private float surfaceHeightOffset;
        [Header("Water Motion Presentation")]
        [Tooltip("Local horizontal flow direction. Zero means still water.")]
        [SerializeField] private Vector3 localFlowDirection;
        [SerializeField, Min(0f)] private float flowSpeed;
        [Header("Depth Observation")]
        [Tooltip("Ground at or above this distance below the water surface is treated as dry land.")]
        [SerializeField, Min(0f)] private float dryLandTolerance = 0.05f;
        [Tooltip("Maximum water depth that is still observed as shallow water.")]
        [SerializeField, Min(0.01f)] private float shallowWaterMaximumDepth = 1.2f;
        [Tooltip(
            "When the water volume contains no ground collider below a position, " +
            "observe it as Deep water. Disable this only for worlds where missing " +
            "ground data must remain an explicit Unknown observation.")]
        [SerializeField] private bool missingGroundMeansDeep = true;
        [SerializeField, Min(0.1f)] private float groundProbeStartHeight = 5f;
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 40f;
        [SerializeField] private LayerMask groundProbeLayers = Physics.DefaultRaycastLayers;

        private readonly RaycastHit[] groundProbeHits = new RaycastHit[32];

        public OntologyObject RegionObject => regionObject;

        private void Reset()
        {
            ResolveParentRegion();
        }

        private void OnValidate()
        {
            ResolveParentRegion();
            dryLandTolerance = Mathf.Max(0f, dryLandTolerance);
            shallowWaterMaximumDepth = Mathf.Max(0.01f, shallowWaterMaximumDepth);
            groundProbeStartHeight = Mathf.Max(0.1f, groundProbeStartHeight);
            groundProbeDistance = Mathf.Max(0.1f, groundProbeDistance);
            flowSpeed = Mathf.Max(0f, flowSpeed);
        }

        public bool TryGetRegionId(out string regionId)
        {
            if (regionObject == null)
            {
                ResolveParentRegion();
            }

            regionId = regionObject == null ? string.Empty : regionObject.EntityId;
            return !string.IsNullOrWhiteSpace(regionId);
        }

        /// <summary>
        /// Returns the physical height used by swimming movement. The trigger remains a semantic
        /// volume; this is only the presentation adapter's waterline reference.
        /// </summary>
        public bool TryGetSurfaceHeight(out float surfaceHeight)
        {
            var volumeCollider = GetComponent<Collider>();
            if (volumeCollider == null || !volumeCollider.enabled || !isActiveAndEnabled)
            {
                surfaceHeight = 0f;
                return false;
            }

            surfaceHeight = volumeCollider.bounds.max.y + surfaceHeightOffset;
            return true;
        }

        public bool TryGetFlowVelocity(out Vector3 flowVelocity)
        {
            if (!isActiveAndEnabled)
            {
                flowVelocity = Vector3.zero;
                return false;
            }

            var worldDirection = transform.TransformDirection(localFlowDirection);
            worldDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            flowVelocity = worldDirection.sqrMagnitude <= Mathf.Epsilon
                ? Vector3.zero
                : worldDirection.normalized * Mathf.Max(0f, flowSpeed);
            return true;
        }

        /// <summary>
        /// Observes the physical depth below the visible water surface. The result is an
        /// observation only; ontology rules decide whether it means wading or swimming.
        /// </summary>
        public bool TryClassifyDepthAt(Vector3 worldPosition, Transform ignoredRoot, out OntologyWaterDepth depth)
        {
            depth = OntologyWaterDepth.DryLand;
            if (!TryGetSurfaceHeight(out var surfaceHeight))
            {
                return false;
            }

            var origin = new Vector3(worldPosition.x, surfaceHeight + groundProbeStartHeight, worldPosition.z);
            var hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                groundProbeHits,
                groundProbeStartHeight + groundProbeDistance,
                groundProbeLayers,
                QueryTriggerInteraction.Ignore);

            var nearestDistance = float.PositiveInfinity;
            var foundGround = false;
            var nearestGroundPoint = Vector3.zero;
            for (var index = 0; index < hitCount; index++)
            {
                var hit = groundProbeHits[index];
                var collider = hit.collider;
                if (collider == null || (ignoredRoot != null && collider.transform.IsChildOf(ignoredRoot)))
                {
                    continue;
                }

                if (collider.GetComponentInParent<OntologyWaterRegionVolume>() != null)
                {
                    continue;
                }

                if (hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearestGroundPoint = hit.point;
                foundGround = true;
            }

            if (foundGround)
            {
                var depthBelowSurface = surfaceHeight - nearestGroundPoint.y;
                if (depthBelowSurface <= dryLandTolerance)
                {
                    depth = OntologyWaterDepth.DryLand;
                }
                else if (depthBelowSurface <= shallowWaterMaximumDepth)
                {
                    depth = OntologyWaterDepth.Shallow;
                }
                else
                {
                    depth = OntologyWaterDepth.Deep;
                }

                return true;
            }

            // In the default world policy, water without a ground collider is water-only
            // space and therefore a direct Deep observation. The configurable fallback
            // keeps Unknown available for worlds that intentionally model incomplete
            // ground data as uncertainty.
            depth = missingGroundMeansDeep
                ? OntologyWaterDepth.Deep
                : OntologyWaterDepth.Unknown;
            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            NotifyPresenceSensor(other, entering: true);
        }

        private void OnTriggerStay(Collider other)
        {
            NotifyPresenceSensor(other, entering: true);
        }

        private void OnTriggerExit(Collider other)
        {
            NotifyPresenceSensor(other, entering: false);
        }

        private void NotifyPresenceSensor(Collider other, bool entering)
        {
            var actorSensor = other.GetComponentInParent<OntologyWaterPresenceSensor>();
            if (actorSensor != null)
            {
                if (entering)
                {
                    actorSensor.EnterVolume(this);
                }
                else
                {
                    actorSensor.ExitVolume(this);
                }
            }

            var objectSensor = other.GetComponentInParent<OntologyWaterOccupancySensor>();
            if (objectSensor == null)
            {
                return;
            }

            if (entering)
            {
                objectSensor.EnterVolume(this);
            }
            else
            {
                objectSensor.ExitVolume(this);
            }
        }

        private void ResolveParentRegion()
        {
            if (regionObject == null)
            {
                regionObject = GetComponentInParent<OntologyObject>();
            }
        }
    }

    public enum OntologyWaterDepth
    {
        DryLand = 0,
        Shallow = 1,
        Deep = 2,
        Unknown = 3
    }
}
