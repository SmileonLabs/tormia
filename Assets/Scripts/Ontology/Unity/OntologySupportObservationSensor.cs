using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Observes physical support contacts as Object supported_by Support.
    /// Rules may derive resting_on or other gameplay meaning from this observation.
    /// </summary>
    [RequireComponent(typeof(OntologyObject))]
    public sealed class OntologySupportObservationSensor : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField, Range(0f, 1f)] private float minimumUpwardNormal = 0.45f;

        private OntologyObject ontologyObject;
        private readonly HashSet<string> contactedSupportIds = new();
        private readonly HashSet<string> injectedSupportIds = new();
        private readonly List<string> supportRemovalBuffer = new();
        private OntologyWorldBootstrap subscribedBootstrap;

        private void Awake()
        {
            ontologyObject = GetComponent<OntologyObject>();
            ResolveBootstrap();
        }

        private void OnEnable()
        {
            ResolveBootstrap();
        }

        private void FixedUpdate()
        {
            SynchronizeSupportFacts();
            contactedSupportIds.Clear();
        }

        private void OnDisable()
        {
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.WorldRebuilt -= HandleWorldRebuilt;
                subscribedBootstrap = null;
            }
            RemoveInjectedFacts();
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision == null || collision.collider == null)
            {
                return;
            }

            var support = collision.collider.GetComponentInParent<OntologyObject>();
            if (support == null || support == ontologyObject ||
                string.IsNullOrWhiteSpace(support.EntityId))
            {
                return;
            }

            foreach (var contact in collision.contacts)
            {
                if (Vector3.Dot(contact.normal, Vector3.up) >= minimumUpwardNormal)
                {
                    contactedSupportIds.Add(support.EntityId);
                    return;
                }
            }
        }

        public void Configure(OntologyWorldBootstrap targetBootstrap)
        {
            bootstrap = targetBootstrap;
        }

        private void SynchronizeSupportFacts()
        {
            if (ontologyObject == null)
                ontologyObject = GetComponent<OntologyObject>();
            ResolveBootstrap();
            var world = bootstrap == null ? null : bootstrap.World;
            if (ontologyObject == null || world == null)
            {
                return;
            }

            var changed = OntologyRuntimeObservationFacts.SynchronizeSet(
                world,
                ontologyObject.EntityId,
                OntologyPredicates.SupportedBy,
                contactedSupportIds,
                injectedSupportIds,
                supportRemovalBuffer);
            if (changed)
            {
                bootstrap.RequestSimulation();
            }
        }

        private void RemoveInjectedFacts()
        {
            var world = bootstrap == null ? null : bootstrap.World;
            var changed = false;
            if (world != null && ontologyObject != null)
            {
                changed = OntologyRuntimeObservationFacts.RemovePublishedSet(
                    world,
                    ontologyObject.EntityId,
                    OntologyPredicates.SupportedBy,
                    injectedSupportIds);
            }

            contactedSupportIds.Clear();
            injectedSupportIds.Clear();
            if (changed)
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
            // The facts belonged to the previous runtime world instance. Existing
            // collision contacts are observed again on the next physics tick.
            injectedSupportIds.Clear();
        }
    }
}
