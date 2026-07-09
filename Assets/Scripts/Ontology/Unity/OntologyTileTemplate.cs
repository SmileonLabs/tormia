using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(fileName = "NewTileTemplate", menuName = "Tormia/Ontology/Tile Template")]
    public sealed class OntologyTileTemplate : ScriptableObject
    {
        public string templateId;
        public Color previewColor = Color.white;
        public Material previewMaterial;
        public GameObject tilePrefab;
        public List<OntologyFactTemplateEntry> baseFacts = new();

        public string EffectiveTemplateId => string.IsNullOrWhiteSpace(templateId) ? name : templateId;
    }
}
