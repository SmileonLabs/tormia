using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation data for aligning an ontology animation intent to a water surface.
    /// It contains no gameplay decision; rules choose the intent and this profile expresses it.
    /// </summary>
    [CreateAssetMenu(menuName = "Tormia/Ontology/Swimming Pose Profile")]
    public sealed class OntologySwimmingPoseProfile : ScriptableObject
    {
        [SerializeField] private OntologySwimmingPoseDefinition[] definitions = Array.Empty<OntologySwimmingPoseDefinition>();

        public bool TryGetDefinition(string intent, out OntologySwimmingPoseDefinition definition)
        {
            if (definitions != null)
            {
                foreach (var candidate in definitions)
                {
                    if (candidate != null && string.Equals(candidate.animationIntent, intent, StringComparison.Ordinal))
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }

            definition = null;
            return false;
        }
    }

    [Serializable]
    public sealed class OntologySwimmingPoseDefinition
    {
        public string animationIntent;
        public HumanBodyBones waterlineBone = HumanBodyBones.UpperChest;
        [Tooltip("Positive raises the semantic waterline point above the visible surface; negative submerges it.")]
        public float surfaceOffset;
        [Tooltip("Used only when this humanoid does not contain the configured bone.")]
        public float fallbackRootHeight = -0.75f;
    }
}
