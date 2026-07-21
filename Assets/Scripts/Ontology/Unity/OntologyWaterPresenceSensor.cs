using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Converts trigger overlap with water volumes into the transient ontology fact:
    /// Actor occupies WaterRegion. Rules decide what that observation means.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    public sealed class OntologyWaterPresenceSensor : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private string occupancyPredicate = OntologyPredicates.Occupies;
        [SerializeField] private string immersionDepthPredicate = OntologyPredicates.ImmersionDepth;
        [SerializeField] private string shallowDepthValue = OntologyObjects.Shallow;
        [SerializeField] private string deepDepthValue = OntologyObjects.Deep;
        [SerializeField] private string unknownDepthValue = OntologyObjects.Unknown;
        [Header("Observation Performance")]
        [SerializeField, Min(0.02f)] private float observationInterval = 0.1f;
        [SerializeField, Min(0f)] private float movementRefreshDistance = 0.05f;

        private OntologyObject actorObject;
        private CharacterController characterController;
        private readonly HashSet<OntologyWaterRegionVolume> overlappingVolumes = new();
        private readonly HashSet<string> injectedRegionIds = new();
        private readonly HashSet<string> currentRegionIds = new();
        private readonly List<string> regionRemovalBuffer = new();
        private readonly Collider[] overlapBuffer = new Collider[16];
        private string injectedImmersionDepth;
        private Vector3 lastObservationPosition;
        private float nextObservationTime;
        private OntologyWorldBootstrap subscribedBootstrap;

        public bool HasActiveWaterOverlap
        {
            get
            {
                overlappingVolumes.RemoveWhere(volume => volume == null || !volume.isActiveAndEnabled);
                foreach (var volume in overlappingVolumes)
                {
                    if (volume.TryClassifyDepthAt(transform.position, transform, out var depth) &&
                        depth != OntologyWaterDepth.DryLand)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void Awake()
        {
            actorObject = GetComponent<OntologyObject>();
            characterController = GetComponent<CharacterController>();
            ResolveBootstrap();
            lastObservationPosition = transform.position;
        }

        private void OnEnable()
        {
            ResolveBootstrap();
        }

        private void FixedUpdate()
        {
            if (!ShouldRefreshObservation())
            {
                return;
            }

            RefreshPhysicalOverlaps();
            SynchronizeOccupancyFacts();
        }

        private void OnDisable()
        {
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt -= HandleWorldChanged;
                subscribedBootstrap = null;
            }
            RemoveInjectedFacts();
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterVolume(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterVolume(other);
        }

        private void OnTriggerExit(Collider other)
        {
            var volume = other.GetComponentInParent<OntologyWaterRegionVolume>();
            if (volume != null)
            {
                ExitVolume(volume);
            }
        }

        public void EnterVolume(OntologyWaterRegionVolume volume)
        {
            if (volume != null)
            {
                overlappingVolumes.Add(volume);
                SynchronizeOccupancyFacts();
            }
        }

        public void ExitVolume(OntologyWaterRegionVolume volume)
        {
            if (volume != null)
            {
                overlappingVolumes.Remove(volume);
                SynchronizeOccupancyFacts();
            }
        }

        public bool TryGetActiveSurfaceHeight(out float surfaceHeight)
        {
            surfaceHeight = float.MinValue;
            foreach (var volume in overlappingVolumes)
            {
                if (volume != null &&
                    volume.TryClassifyDepthAt(transform.position, transform, out var depth) &&
                    depth == OntologyWaterDepth.Deep &&
                    volume.TryGetSurfaceHeight(out var candidateHeight))
                {
                    surfaceHeight = Mathf.Max(surfaceHeight, candidateHeight);
                }
            }

            if (surfaceHeight == float.MinValue)
            {
                surfaceHeight = 0f;
                return false;
            }

            return true;
        }

        private void RegisterVolume(Collider other)
        {
            var volume = other.GetComponentInParent<OntologyWaterRegionVolume>();
            if (volume != null)
            {
                EnterVolume(volume);
            }
        }

        private void RefreshPhysicalOverlaps()
        {
            if (characterController == null || !characterController.enabled)
            {
                return;
            }

            var bounds = characterController.bounds;
            var colliderCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                Quaternion.identity,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            overlappingVolumes.Clear();
            for (var index = 0; index < colliderCount; index++)
            {
                var collider = overlapBuffer[index];
                var volume = collider.GetComponentInParent<OntologyWaterRegionVolume>();
                if (volume != null)
                {
                    overlappingVolumes.Add(volume);
                }
            }
        }

        private void SynchronizeOccupancyFacts()
        {
            if (actorObject == null || string.IsNullOrWhiteSpace(occupancyPredicate))
            {
                return;
            }

            ResolveBootstrap();

            var world = bootstrap == null ? null : bootstrap.World;
            if (world == null)
            {
                return;
            }

            currentRegionIds.Clear();
            var currentImmersionDepth = string.Empty;
            overlappingVolumes.RemoveWhere(volume => volume == null || !volume.isActiveAndEnabled);
            foreach (var volume in overlappingVolumes)
            {
                if (!volume.TryClassifyDepthAt(transform.position, transform, out var depth) ||
                    depth == OntologyWaterDepth.DryLand ||
                    !volume.TryGetRegionId(out var regionId))
                {
                    continue;
                }

                currentRegionIds.Add(regionId);
                if (depth == OntologyWaterDepth.Deep)
                {
                    currentImmersionDepth = deepDepthValue;
                }
                else if (depth == OntologyWaterDepth.Shallow &&
                         string.IsNullOrWhiteSpace(currentImmersionDepth))
                {
                    currentImmersionDepth = shallowDepthValue;
                }
                else if (depth == OntologyWaterDepth.Unknown &&
                         string.IsNullOrWhiteSpace(currentImmersionDepth))
                {
                    currentImmersionDepth = unknownDepthValue;
                }
            }

            var factsChanged = OntologyRuntimeObservationFacts.SynchronizeSet(
                world,
                actorObject.EntityId,
                occupancyPredicate,
                currentRegionIds,
                injectedRegionIds,
                regionRemovalBuffer);
            factsChanged |= OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                world,
                actorObject.EntityId,
                immersionDepthPredicate,
                currentImmersionDepth,
                ref injectedImmersionDepth);

            if (factsChanged)
            {
                bootstrap.RequestSimulation();
            }
        }

        private bool ShouldRefreshObservation()
        {
            var currentPosition = transform.position;
            var minimumDistance = Mathf.Max(0f, movementRefreshDistance);
            var moved = (currentPosition - lastObservationPosition).sqrMagnitude >=
                        minimumDistance * minimumDistance;
            if (!moved && Time.time < nextObservationTime)
            {
                return false;
            }

            lastObservationPosition = currentPosition;
            nextObservationTime = Time.time + Mathf.Max(0.02f, observationInterval);
            return true;
        }

        private void ResolveBootstrap()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (subscribedBootstrap == bootstrap)
            {
                return;
            }

            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt -= HandleWorldChanged;
            }

            subscribedBootstrap = bootstrap;
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt += HandleWorldChanged;
            }
        }

        private void HandleWorldChanged()
        {
            // Runtime observations are not durable. Recheck immediately when the
            // world instance is replaced by reset or restore.
            injectedRegionIds.Clear();
            injectedImmersionDepth = string.Empty;
            nextObservationTime = 0f;
        }

        private void RemoveInjectedFacts()
        {
            var world = bootstrap == null ? null : bootstrap.World;
            var factsChanged = false;
            if (world != null && actorObject != null)
            {
                factsChanged |= OntologyRuntimeObservationFacts.RemovePublishedSet(
                    world,
                    actorObject.EntityId,
                    occupancyPredicate,
                    injectedRegionIds);
            }
            overlappingVolumes.Clear();

            if (world != null && actorObject != null)
            {
                factsChanged |= OntologyRuntimeObservationFacts.RemovePublishedSingleValue(
                    world,
                    actorObject.EntityId,
                    immersionDepthPredicate,
                    ref injectedImmersionDepth);
            }

            if (factsChanged && bootstrap != null)
            {
                bootstrap.RequestSimulation();
            }
        }

    }
}
