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
        public string[] actorTypes = Array.Empty<string>();
        public string[] rigTypes = Array.Empty<string>();
        public string[] requiredCapabilities = Array.Empty<string>();
        public OntologyAnimationLayer layer = OntologyAnimationLayer.FullBody;
        public bool interruptible = true;
        public string[] properties = Array.Empty<string>();
        public string[] intents = Array.Empty<string>();
        public int priority;
        public bool canBlend;
    }

    public enum OntologyAnimationLayer
    {
        FullBody = 0,
        UpperBody = 1,
        Additive = 2
    }
}
