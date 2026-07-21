using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActorProfileFactSynchronizer : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyActorProfile profile;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Leave empty; OntologyObject.EntityId is authoritative.")]
        private string actorId;
        [SerializeField] private bool runSimulationAfterSync = true;

        private string ActorId => actorObject != null && !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private void Awake()
        {
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
        }

        private void Start()
        {
            Sync();
        }

        public void Sync()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || profile == null || string.IsNullOrWhiteSpace(ActorId)) return;
            if (bootstrap.World == null || bootstrap.Session == null) bootstrap.ResetWorld(logReport: false);
            if (bootstrap.World == null) return;

            bootstrap.World.RemoveFacts(ActorId, "actor_type");
            bootstrap.World.RemoveFacts(ActorId, "rig_type");
            bootstrap.World.RemoveFacts(ActorId, "animation_capability");
            bootstrap.World.RemoveFacts(ActorId, OntologyPredicates.HasAnimation);
            bootstrap.World.AddFact(ActorId, "actor_type", profile.actorType);
            bootstrap.World.AddFact(ActorId, "rig_type", profile.rigType);
            if (profile.defaultConcepts != null)
            {
                foreach (var concept in profile.defaultConcepts)
                {
                    if (!string.IsNullOrWhiteSpace(concept))
                        bootstrap.World.AddFact(ActorId, OntologyPredicates.HasConcept, concept);
                }
            }
            if (profile.defaultFacts != null)
            {
                foreach (var fact in profile.defaultFacts)
                {
                    if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                        continue;
                    bootstrap.World.AddFact(ActorId, fact.predicate, fact.obj);
                }
            }
            if (profile.ontologyCapabilities != null)
            {
                foreach (var capability in profile.ontologyCapabilities)
                {
                    if (!string.IsNullOrWhiteSpace(capability))
                        bootstrap.World.AddFact(ActorId, OntologyPredicates.HasCapability, capability);
                }
            }
            if (profile.capabilities != null)
            {
                foreach (var capability in profile.capabilities)
                {
                    if (!string.IsNullOrWhiteSpace(capability))
                    {
                        // Keep the historical animation_capability projection for existing clips,
                        // without treating animation grouping as gameplay permission.
                        bootstrap.World.AddFact(ActorId, "animation_capability", capability);
                    }
                }
            }
            if (profile.animationIds != null)
            {
                foreach (var animationId in profile.animationIds)
                {
                    if (!string.IsNullOrWhiteSpace(animationId))
                        bootstrap.World.AddFact(ActorId, OntologyPredicates.HasAnimation, animationId);
                }
            }
            if (runSimulationAfterSync) bootstrap.RunSimulation();
        }
    }
}
