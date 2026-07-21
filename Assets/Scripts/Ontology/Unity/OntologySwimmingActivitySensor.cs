using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Observes whether a swimming actor currently supplies movement input. It publishes a
    /// transient ontology fact; rules decide which animation intent expresses that activity.
    /// </summary>
    public sealed class OntologySwimmingActivitySensor : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; the OntologyObject id is authoritative.")]
        private string actorId;
        [SerializeField] private string movementModePredicate = OntologyPredicates.MobilityMode;
        [SerializeField] private string swimmingMode = OntologyObjects.Swimming;
        [SerializeField] private string activityPredicate = "swimming_activity";
        [SerializeField] private string movingActivity = OntologyObjects.Moving;
        [SerializeField] private string idleActivity = OntologyObjects.Idle;
        [Tooltip("Actual horizontal speed below this value is observed as idle swimming.")]
        [SerializeField, Min(0f)] private float horizontalSpeedThreshold = 0.05f;
        [SerializeField, Min(0f)] private float idleConfirmationTime = 0.15f;

        private string injectedActivity;
        private Vector3 previousPosition;
        private float timeBelowMovementThreshold;
        private OntologyWorldBootstrap subscribedBootstrap;
        private string ActorId => actorObject != null &&
                                  !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private void Awake()
        {
            ResolveBootstrap();
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
            previousPosition = transform.position;
        }

        private void OnEnable()
        {
            ResolveBootstrap();
        }

        private void Update()
        {
            ResolveBootstrap();
            var world = bootstrap == null ? null : bootstrap.World;
            if (world == null) return;

            var nextActivity = string.Empty;
            if (world.HasFact(ActorId, movementModePredicate, swimmingMode))
            {
                var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
                var displacement = transform.position - previousPosition;
                displacement.y = 0f;
                var horizontalSpeed = displacement.magnitude / deltaTime;
                if (horizontalSpeed >= horizontalSpeedThreshold)
                {
                    timeBelowMovementThreshold = 0f;
                    nextActivity = movingActivity;
                }
                else
                {
                    timeBelowMovementThreshold += Time.deltaTime;
                    if (timeBelowMovementThreshold >= idleConfirmationTime)
                    {
                        nextActivity = idleActivity;
                    }
                }
            }
            else
            {
                timeBelowMovementThreshold = 0f;
            }

            previousPosition = transform.position;

            if (string.Equals(nextActivity, injectedActivity, System.StringComparison.Ordinal)) return;

            var changed = OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                world,
                ActorId,
                activityPredicate,
                nextActivity,
                ref injectedActivity);
            if (changed) bootstrap.RequestSimulation();
        }

        private void OnDisable()
        {
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt -= HandleWorldRebuilt;
                subscribedBootstrap = null;
            }
            var world = bootstrap == null ? null : bootstrap.World;
            if (world != null)
            {
                if (OntologyRuntimeObservationFacts.RemovePublishedSingleValue(
                        world,
                        ActorId,
                        activityPredicate,
                        ref injectedActivity))
                {
                    bootstrap.RequestSimulation();
                }
            }
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
                subscribedBootstrap.WorldRebuilt -= HandleWorldRebuilt;
            }

            subscribedBootstrap = bootstrap;
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt += HandleWorldRebuilt;
            }
        }

        private void HandleWorldRebuilt()
        {
            injectedActivity = string.Empty;
            timeBelowMovementThreshold = 0f;
            previousPosition = transform.position;
        }
    }
}
