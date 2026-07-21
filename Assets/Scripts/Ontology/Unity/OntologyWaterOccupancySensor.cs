using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Generic physical observation adapter for non-actor ontology objects.
    /// It reports only Object occupies WaterRegion; rules decide whether that means
    /// floating, sinking, damage, transport, or any other behaviour.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    public sealed class OntologyWaterOccupancySensor : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private string occupancyPredicate = OntologyPredicates.Occupies;
        [SerializeField] private LayerMask overlapLayers = Physics.DefaultRaycastLayers;
        [SerializeField, Min(0f)] private float waterContactTolerance = 0.02f;
        [Header("Observation Performance")]
        [SerializeField, Min(0.02f)] private float observationInterval = 0.1f;
        [SerializeField, Min(0f)] private float movementRefreshDistance = 0.05f;

        private OntologyObject ontologyObject;
        private readonly HashSet<OntologyWaterRegionVolume> overlappingVolumes = new();
        private readonly HashSet<string> injectedRegionIds = new();
        private readonly HashSet<string> currentRegionIds = new();
        private readonly List<string> regionRemovalBuffer = new();
        private readonly Collider[] overlapBuffer = new Collider[16];
        private Vector3 lastObservationPosition;
        private float nextObservationTime;
        private OntologyWorldBootstrap subscribedBootstrap;

        /// <summary>Number of water regions currently observed by this sensor.</summary>
        public int CurrentRegionCount => currentRegionIds.Count;

        private void Awake()
        {
            ontologyObject = GetComponent<OntologyObject>();
            ResolveBootstrap();
            lastObservationPosition = transform.position;
        }

        private void OnEnable()
        {
            ResolveBootstrap();
            SubscribeToWorldChanges();
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

        public void Configure(OntologyWorldBootstrap targetBootstrap)
        {
            bootstrap = targetBootstrap;
        }

        public void EnterVolume(OntologyWaterRegionVolume volume)
        {
            if (volume == null) return;
            overlappingVolumes.Add(volume);
            SynchronizeOccupancyFacts();
        }

        public void ExitVolume(OntologyWaterRegionVolume volume)
        {
            if (volume == null) return;
            overlappingVolumes.Remove(volume);
            SynchronizeOccupancyFacts();
        }

        public bool TryGetActiveSurfaceHeight(out float surfaceHeight)
        {
            surfaceHeight = float.MinValue;
            overlappingVolumes.RemoveWhere(volume => volume == null || !volume.isActiveAndEnabled);
            foreach (var volume in overlappingVolumes)
            {
                if (volume.TryGetSurfaceHeight(out var candidate))
                {
                    surfaceHeight = Mathf.Max(surfaceHeight, candidate);
                }
            }

            if (surfaceHeight != float.MinValue)
            {
                return true;
            }

            surfaceHeight = 0f;
            return false;
        }

        public bool TryGetActiveWaterConditions(
            out float surfaceHeight,
            out Vector3 flowVelocity)
        {
            surfaceHeight = float.MinValue;
            flowVelocity = Vector3.zero;
            var matchingFlowCount = 0;

            overlappingVolumes.RemoveWhere(volume => volume == null || !volume.isActiveAndEnabled);
            foreach (var volume in overlappingVolumes)
            {
                if (!volume.TryGetSurfaceHeight(out var candidateSurface))
                {
                    continue;
                }

                volume.TryGetFlowVelocity(out var candidateFlow);
                if (candidateSurface > surfaceHeight + 0.001f)
                {
                    surfaceHeight = candidateSurface;
                    flowVelocity = candidateFlow;
                    matchingFlowCount = 1;
                }
                else if (Mathf.Abs(candidateSurface - surfaceHeight) <= 0.001f)
                {
                    flowVelocity += candidateFlow;
                    matchingFlowCount++;
                }
            }

            if (surfaceHeight == float.MinValue)
            {
                surfaceHeight = 0f;
                flowVelocity = Vector3.zero;
                return false;
            }

            if (matchingFlowCount > 1)
            {
                flowVelocity /= matchingFlowCount;
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
            if (!TryGetObservationBounds(out var bounds))
            {
                overlappingVolumes.Clear();
                return;
            }

            var colliderCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                Quaternion.identity,
                overlapLayers,
                QueryTriggerInteraction.Collide);
            overlappingVolumes.Clear();
            for (var index = 0; index < colliderCount; index++)
            {
                var collider = overlapBuffer[index];
                if (collider == null || collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                var volume = collider.GetComponentInParent<OntologyWaterRegionVolume>();
                if (volume != null)
                {
                    overlappingVolumes.Add(volume);
                }
            }
        }

        private bool TryGetObservationBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            // Wearable and carried objects normally disable their solid colliders while
            // attached so they do not push the actor. They still need to observe their
            // real water contact. Use the visible presentation bounds as a generic sensor
            // fallback instead of coupling this adapter to a particular item or slot.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private void SynchronizeOccupancyFacts()
        {
            if (ontologyObject == null)
            {
                ontologyObject = GetComponent<OntologyObject>();
            }

            if (ontologyObject == null || string.IsNullOrWhiteSpace(occupancyPredicate))
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
            var hasObservationBounds = TryGetObservationBounds(out var observationBounds);
            overlappingVolumes.RemoveWhere(volume => volume == null || !volume.isActiveAndEnabled);
            foreach (var volume in overlappingVolumes)
            {
                if (hasObservationBounds &&
                    IsActuallyInWater(volume, observationBounds) &&
                    volume.TryGetRegionId(out var regionId))
                {
                    currentRegionIds.Add(regionId);
                }
            }

            var factsChanged = OntologyRuntimeObservationFacts.SynchronizeSet(
                world,
                ontologyObject.EntityId,
                occupancyPredicate,
                currentRegionIds,
                injectedRegionIds,
                regionRemovalBuffer);

            if (factsChanged)
            {
                bootstrap.RequestSimulation();
            }
        }

        private void RemoveInjectedFacts()
        {
            var world = bootstrap == null ? null : bootstrap.World;
            var factsChanged = false;
            if (world != null && ontologyObject != null)
            {
                factsChanged = OntologyRuntimeObservationFacts.RemovePublishedSet(
                    world,
                    ontologyObject.EntityId,
                    occupancyPredicate,
                    injectedRegionIds);
            }
            overlappingVolumes.Clear();
            if (factsChanged && bootstrap != null)
            {
                bootstrap.RequestSimulation();
            }
        }

        private void ResolveBootstrap()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            SubscribeToWorldChanges();
        }

        private void SubscribeToWorldChanges()
        {
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
            // Reset/restore replaces the runtime world, so any previous occupancy
            // observation has to be republished on the next physics step rather
            // than waiting for the normal performance interval.
            injectedRegionIds.Clear();
            nextObservationTime = 0f;
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

        private bool IsActuallyInWater(
            OntologyWaterRegionVolume volume,
            Bounds bounds)
        {
            if (volume == null ||
                !volume.TryGetSurfaceHeight(out var surfaceHeight))
            {
                return false;
            }

            // A large semantic water trigger may overlap islands or extend above
            // the visible water mesh. Contact and depth are therefore observed
            // separately before an occupies relation is emitted.
            if (bounds.min.y > surfaceHeight + waterContactTolerance)
            {
                return false;
            }

            return !volume.TryClassifyDepthAt(
                       bounds.center,
                       transform,
                       out var depth) ||
                   depth != OntologyWaterDepth.DryLand;
        }
    }
}
