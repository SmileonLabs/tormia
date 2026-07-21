using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Reports Actor near Object from transforms. It does not decide whether the observation
    /// means pickup, dialogue, combat, attachment, or any other behaviour.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    public sealed class OntologyProximityObservationSensor : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Min(0.05f)] private float enterDistance = 1.5f;
        [SerializeField, Min(0f)] private float exitPadding = 0.25f;
        [SerializeField] private OntologyProximityMeasurementMode measurementMode;
        [SerializeField] private string interactionAnchorPath;
        [SerializeField] private string proximityPredicate = OntologyPredicates.Near;
        [Header("Observation Performance")]
        [SerializeField, Min(0.02f)] private float observationInterval = 0.1f;
        [SerializeField, Min(0f)] private float movementRefreshDistance = 0.05f;

        private OntologyObject observedObject;
        private Collider[] observedColliders = new Collider[0];
        private Vector3 lastActorPosition;
        private Vector3 lastObservedPosition;
        private float nextObservationTime;
        private OntologyWorldBootstrap subscribedBootstrap;
        private bool publishedObservation;
        public bool IsNear =>
            bootstrap != null && bootstrap.World != null &&
            actorObject != null && observedObject != null &&
            bootstrap.World.HasFact(
                actorObject.EntityId,
                proximityPredicate,
                observedObject.EntityId);

        private void Awake()
        {
            observedObject = GetComponent<OntologyObject>();
            ResolveDependencies();
            CacheObservedColliders();
            lastActorPosition = actorObject != null ? actorObject.transform.position : Vector3.zero;
            lastObservedPosition = observedObject != null ? observedObject.transform.position : transform.position;
        }

        private void OnEnable()
        {
            ResolveDependencies();
            SubscribeToWorldChanges();
        }

        private void Update()
        {
            if (!ShouldRefreshObservation())
            {
                return;
            }

            SynchronizeObservation();
        }

        private void OnDisable()
        {
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt -= HandleWorldChanged;
                subscribedBootstrap = null;
            }
            RemoveObservation();
        }

        public void Configure(
            OntologyWorldBootstrap targetBootstrap,
            OntologyObject targetActor,
            float targetEnterDistance,
            float targetExitPadding,
            OntologyProximityMeasurementMode targetMeasurementMode =
                OntologyProximityMeasurementMode.TransformCenter,
            string targetInteractionAnchorPath = null)
        {
            bootstrap = targetBootstrap;
            actorObject = targetActor;
            enterDistance = Mathf.Max(0.05f, targetEnterDistance);
            exitPadding = Mathf.Max(0f, targetExitPadding);
            measurementMode = targetMeasurementMode;
            interactionAnchorPath = targetInteractionAnchorPath;
            observedObject = GetComponent<OntologyObject>();
            CacheObservedColliders();
        }

        private void SynchronizeObservation()
        {
            ResolveDependencies();
            if (bootstrap == null || bootstrap.World == null ||
                actorObject == null || observedObject == null ||
                string.IsNullOrWhiteSpace(proximityPredicate))
            {
                return;
            }

            var hasNearFact = bootstrap.World.HasFact(
                actorObject.EntityId,
                proximityPredicate,
                observedObject.EntityId);
            var threshold = hasNearFact
                ? enterDistance + exitPadding
                : enterDistance;
            var distance = MeasureDistance();
            var shouldBeNear = distance <= threshold;
            if (shouldBeNear == hasNearFact)
            {
                return;
            }

            var changed = false;
            if (shouldBeNear)
            {
                changed = bootstrap.World.AddFact(
                    actorObject.EntityId,
                    proximityPredicate,
                    observedObject.EntityId);
                publishedObservation = changed;
            }
            else if (publishedObservation)
            {
                changed = bootstrap.World.RemoveFact(
                    actorObject.EntityId,
                    proximityPredicate,
                    observedObject.EntityId);
                publishedObservation = false;
            }
            if (changed)
            {
                bootstrap.RequestSimulation();
            }
        }

        public float MeasureDistance()
        {
            if (actorObject == null || observedObject == null)
            {
                return float.PositiveInfinity;
            }

            var actorPosition = actorObject.transform.position;
            if (measurementMode == OntologyProximityMeasurementMode.InteractionAnchor)
            {
                var anchor = string.IsNullOrWhiteSpace(interactionAnchorPath)
                    ? observedObject.transform
                    : observedObject.transform.Find(interactionAnchorPath);
                return Vector3.Distance(
                    actorPosition,
                    anchor != null ? anchor.position : observedObject.transform.position);
            }

            if (measurementMode == OntologyProximityMeasurementMode.ColliderSurface)
            {
                var best = float.PositiveInfinity;
                foreach (var collider in observedColliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger)
                    {
                        continue;
                    }

                    best = Mathf.Min(
                        best,
                        Vector3.Distance(actorPosition, collider.ClosestPoint(actorPosition)));
                }

                if (!float.IsPositiveInfinity(best))
                {
                    return best;
                }
            }

            return Vector3.Distance(actorPosition, observedObject.transform.position);
        }

        private void RemoveObservation()
        {
            if (bootstrap == null || bootstrap.World == null ||
                actorObject == null || observedObject == null)
            {
                return;
            }

            if (!publishedObservation)
            {
                return;
            }

            var changed = bootstrap.World.RemoveFact(
                actorObject.EntityId,
                proximityPredicate,
                observedObject.EntityId);
            publishedObservation = false;
            if (changed)
            {
                bootstrap.RequestSimulation();
            }
        }

        private void ResolveDependencies()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            SubscribeToWorldChanges();

            if (actorObject != null)
            {
                return;
            }

            if (bootstrap != null)
            {
                bootstrap.EntityRegistry.TryGetSingleWithConcept(
                    OntologyConcepts.Actor,
                    out actorObject);
            }
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
            // A world reset removes runtime observations. Force the next Update
            // to measure again instead of waiting for the normal interval.
            publishedObservation = false;
            nextObservationTime = 0f;
        }

        private bool ShouldRefreshObservation()
        {
            ResolveDependencies();
            if (actorObject == null || observedObject == null)
            {
                return Time.time >= nextObservationTime;
            }

            var minimumDistance = Mathf.Max(0f, movementRefreshDistance);
            var thresholdSquared = minimumDistance * minimumDistance;
            var actorPosition = actorObject.transform.position;
            var observedPosition = observedObject.transform.position;
            var moved = (actorPosition - lastActorPosition).sqrMagnitude >= thresholdSquared ||
                        (observedPosition - lastObservedPosition).sqrMagnitude >= thresholdSquared;
            if (!moved && Time.time < nextObservationTime)
            {
                return false;
            }

            lastActorPosition = actorPosition;
            lastObservedPosition = observedPosition;
            nextObservationTime = Time.time + Mathf.Max(0.02f, observationInterval);
            return true;
        }

        private void CacheObservedColliders()
        {
            observedColliders = observedObject != null
                ? observedObject.GetComponentsInChildren<Collider>(true)
                : new Collider[0];
        }
    }
}
