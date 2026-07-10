using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActorProfileFactSynchronizer : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyActorProfile profile;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private bool runSimulationAfterSync = true;

        private void Start()
        {
            Sync();
        }

        public void Sync()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || profile == null || string.IsNullOrWhiteSpace(actorId)) return;
            if (bootstrap.World == null || bootstrap.Session == null) bootstrap.ResetWorld(logReport: false);
            if (bootstrap.World == null) return;

            bootstrap.World.RemoveFacts(actorId, "actor_type");
            bootstrap.World.RemoveFacts(actorId, "rig_type");
            bootstrap.World.RemoveFacts(actorId, "animation_capability");
            bootstrap.World.AddFact(actorId, "actor_type", profile.actorType);
            bootstrap.World.AddFact(actorId, "rig_type", profile.rigType);
            if (profile.capabilities != null)
            {
                foreach (var capability in profile.capabilities)
                {
                    if (!string.IsNullOrWhiteSpace(capability))
                        bootstrap.World.AddFact(actorId, "animation_capability", capability);
                }
            }
            if (runSimulationAfterSync) bootstrap.RunSimulation();
        }
    }
}
