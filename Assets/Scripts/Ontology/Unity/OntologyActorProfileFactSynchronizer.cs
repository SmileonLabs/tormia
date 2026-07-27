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

            bootstrap.World.RemoveFactContributions(ActorId, OntologyFactOrigin.ActorProfile);
            bootstrap.World.AddFactContribution(ActorId, "actor_type", profile.actorType, OntologyFactOrigin.ActorProfile);
            bootstrap.World.AddFactContribution(ActorId, "rig_type", profile.rigType, OntologyFactOrigin.ActorProfile);
            if (profile.defaultConcepts != null)
            {
                foreach (var concept in profile.defaultConcepts)
                {
                    if (!string.IsNullOrWhiteSpace(concept))
                        bootstrap.World.AddConceptContribution(
                            ActorId,
                            concept,
                            OntologyFactOrigin.ActorProfile);
                }
            }
            if (profile.defaultFacts != null)
            {
                foreach (var fact in profile.defaultFacts)
                {
                    if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                        continue;
                    bootstrap.World.AddFactContribution(
                        ActorId,
                        fact.predicate,
                        fact.obj,
                        OntologyFactOrigin.ActorProfile);
                }
            }
            if (profile.ontologyCapabilities != null)
            {
                foreach (var capability in profile.ontologyCapabilities)
                {
                    if (!string.IsNullOrWhiteSpace(capability))
                        bootstrap.World.AddFactContribution(
                            ActorId,
                            OntologyPredicates.HasCapability,
                            capability,
                            OntologyFactOrigin.ActorProfile);
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
                        bootstrap.World.AddFactContribution(
                            ActorId,
                            "animation_capability",
                            capability,
                            OntologyFactOrigin.ActorProfile);
                    }
                }
            }
            if (profile.animationIds != null)
            {
                foreach (var animationId in profile.animationIds)
                {
                    if (!string.IsNullOrWhiteSpace(animationId))
                        bootstrap.World.AddFactContribution(
                            ActorId,
                            OntologyPredicates.HasAnimation,
                            animationId,
                            OntologyFactOrigin.ActorProfile);
                }
            }
            if (runSimulationAfterSync) bootstrap.RunSimulation();
        }
    }
}
