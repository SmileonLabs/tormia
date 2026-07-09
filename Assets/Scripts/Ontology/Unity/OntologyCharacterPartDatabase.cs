using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Character Part Database")]
    public sealed class OntologyCharacterPartDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyCharacterPartDefinition> definitions = new();

        public IReadOnlyList<OntologyCharacterPartDefinition> Definitions => definitions;
    }

    [Serializable]
    public sealed class OntologyCharacterPartDefinition
    {
        public string partId;
        public string displayName;
        public string slot;
        public string rendererPath;
        public GameObject variantPrefab;
        public Sprite icon;
        public Material material;
        public bool enabledByDefault;
        public OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();
    }
}
