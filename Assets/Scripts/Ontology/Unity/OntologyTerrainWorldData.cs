using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Semantic layer for a Unity Terrain world. Visual terrain remains owned by
    /// Unity; this asset stores the ontology mapping, analysed facts, and
    /// designer-authored exceptions separately.
    /// </summary>
    [CreateAssetMenu(fileName = "NewOntologyTerrainWorld", menuName = "Tormia/Ontology/Terrain World Data")]
    public sealed class OntologyTerrainWorldData : ScriptableObject
    {
        [Header("Visual Source")]
        public Terrain terrain;
        public List<Terrain> terrains = new();
        [Min(1)] public Vector2Int zoneResolution = new(16, 16);

        [Header("Meaning Mappings")]
        public List<OntologyTerrainLayerMapping> terrainLayerMappings = new();
        public List<OntologyTerrainVegetationMapping> vegetationMappings = new();

        [Header("Analysed Semantic Zones")]
        public List<OntologyTerrainZoneData> zones = new();
        public string analysisVersion;
        public string analysedUtc;
    }

    [Serializable]
    public sealed class OntologyTerrainLayerMapping
    {
        public TerrainLayer terrainLayer;
        [Range(0f, 1f)] public float minimumCoverage = 0.2f;
        public List<OntologyFactTemplateEntry> facts = new();
    }

    [Serializable]
    public sealed class OntologyTerrainVegetationMapping
    {
        public GameObject vegetationPrefab;
        [Range(0f, 1f)] public float minimumDensity = 0.1f;
        public List<OntologyFactTemplateEntry> facts = new();
    }

    [Serializable]
    public sealed class OntologyTerrainZoneData
    {
        public string zoneId;
        public string terrainName;
        public Vector2Int coordinate;
        public Bounds worldBounds;
        public float averageHeight;
        public float averageSlope;
        public List<OntologyFactTemplateEntry> automaticFacts = new();
        public List<OntologyFactTemplateEntry> manualFacts = new();
    }
}
