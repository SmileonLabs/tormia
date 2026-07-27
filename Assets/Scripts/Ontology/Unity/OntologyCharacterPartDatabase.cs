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
        public bool useBaseRendererMesh;
        public Sprite icon;
        public Material material;
        public bool enabledByDefault;
        [Tooltip("Required appearance parts stay equipped when their selected thumbnail is clicked again.")]
        public bool required;
        public bool visibleInCustomization = true;
        public string[] linkedPartIds = Array.Empty<string>();
        public OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();
    }
}
