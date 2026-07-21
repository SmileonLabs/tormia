using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Actor Profile")]
    public sealed class OntologyActorProfile : ScriptableObject
    {
        public string actorType = "Player";
        public string rigType = "Humanoid";
        [Tooltip("Canonical concepts granted to every actor using this profile. These are authored profile data, not mesh-name rules.")]
        public string[] defaultConcepts = Array.Empty<string>();
        [Tooltip("Additional authored facts projected when the profile is synchronized. Use canonical English predicate/object IDs.")]
        public OntologyFactEntry[] defaultFacts = Array.Empty<OntologyFactEntry>();
        [Tooltip("Capabilities that are part of the actor ontology. Keep animation-only clip grouping in capabilities for compatibility.")]
        public string[] ontologyCapabilities = Array.Empty<string>();
        [Tooltip("Legacy animation capability grouping retained for existing animation setup data.")]
        public string[] capabilities = Array.Empty<string>();
        public string[] animationIds = Array.Empty<string>();

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

        public bool HasAnimation(string animationId)
        {
            if (string.IsNullOrWhiteSpace(animationId) || animationIds == null)
            {
                return false;
            }

            foreach (var value in animationIds)
            {
                if (string.Equals(value, animationId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
