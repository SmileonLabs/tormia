using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyFactTemplateEntry
    {
        public string predicate;
        public string obj;
    }

    [Serializable]
    public sealed class TilePlacementData
    {
        public Vector2Int coordinate;
        public string templateId;
        public List<OntologyFactTemplateEntry> uniqueFacts = new();
    }

    [CreateAssetMenu(fileName = "NewMapData", menuName = "Tormia/Ontology/Map Data")]
    public sealed class OntologyMapData : ScriptableObject
    {
        public int mapWidth = 20;
        public int mapHeight = 20;
        public List<TilePlacementData> placements = new();
    }
}
