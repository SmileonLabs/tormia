using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Marks a live ontology entity as an actor. Player and NPC presentation can
    /// share this component; gameplay meaning continues to come from facts,
    /// profiles, and rules rather than from the GameObject name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyActorRuntimeContext : MonoBehaviour
    {
        [SerializeField] private OntologyObject actorObject;
        [SerializeField] private OntologyActorProfile profile;
        [SerializeField] private bool autonomous;

        public OntologyObject ActorObject => actorObject;
        public OntologyActorProfile Profile => profile;
        public bool Autonomous => autonomous;
        public string ActorId => actorObject == null ? string.Empty : actorObject.EntityId;

        private void Awake()
        {
            if (actorObject == null) actorObject = GetComponent<OntologyObject>();
        }
    }
}
