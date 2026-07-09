using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Animation Database")]
    public sealed class OntologyAnimationDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyAnimationDefinition> definitions = new();

        public IReadOnlyList<OntologyAnimationDefinition> Definitions => definitions;
    }

    [Serializable]
    public sealed class OntologyAnimationDefinition
    {
        public string animationId;
        public AnimationClip clip;
        public string[] properties = Array.Empty<string>();
        public string[] intents = Array.Empty<string>();
        public int priority;
        public bool canBlend;
    }
}
