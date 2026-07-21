using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Stable server identity for one scene presentation. The display name and the
    /// ontology entity id may change; this GUID is the durable world-entity key.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthorityEntityIdentity : MonoBehaviour
    {
        [SerializeField] private string entityGuid;

        public string EntityGuid => entityGuid;

        public bool TryGetGuid(out Guid value) => Guid.TryParse(entityGuid, out value);

        public Guid EnsureGuid()
        {
            if (!Guid.TryParse(entityGuid, out var value))
            {
                value = Guid.NewGuid();
                entityGuid = value.ToString("D");
            }

            return value;
        }

        public void SetGuid(Guid value)
        {
            if (value != Guid.Empty)
            {
                entityGuid = value.ToString("D");
            }
        }

        public Guid RegenerateGuid()
        {
            var value = Guid.NewGuid();
            entityGuid = value.ToString("D");
            return value;
        }
    }
}
