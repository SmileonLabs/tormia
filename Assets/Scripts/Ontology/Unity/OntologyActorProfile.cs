using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Actor Profile")]
    public sealed class OntologyActorProfile : ScriptableObject
    {
        public string actorType = "Player";
        public string rigType = "Humanoid";
        public string[] capabilities = Array.Empty<string>();

        public bool HasCapability(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability) || capabilities == null)
            {
                return false;
            }

            foreach (var value in capabilities)
            {
                if (string.Equals(value, capability, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
