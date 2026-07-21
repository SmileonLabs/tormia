using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Reads Unity Terrain data into semantic zones. It never modifies the
    /// Terrain itself and preserves designer-authored manual facts per zone.
    /// </summary>
    public static class OntologyTerrainAnalyzer
    {
        public static OntologyTerrainAnalysisSummary Analyze(OntologyTerrainWorldData worldData)
        {
            var summary = new OntologyTerrainAnalysisSummary();
            if (worldData == null)
            {
                summary.warnings.Add("Terrain World Data is missing.");
                return summary;
            }

            var manualFactsByZone = PreserveManualFacts(worldData.zones);
            worldData.zones.Clear();
            var terrains = ResolveTerrains(worldData);
            if (terrains.Count == 0)
            {
                summary.warnings.Add("No Terrain source is assigned.");
                return summary;
            }

            var resolution = new Vector2Int(Mathf.Max(1, worldData.zoneResolution.x), Mathf.Max(1, worldData.zoneResolution.y));
            foreach (var terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null)
                {
                    summary.warnings.Add("A Terrain source is missing TerrainData.");
                    continue;
                }

                AnalyzeTerrain(worldData, terrain, resolution, manualFactsByZone, summary);
            }

            worldData.analysisVersion = "TerrainOntologyAnalyzer/1";
            worldData.analysedUtc = DateTime.UtcNow.ToString("O");
            return summary;
        }

        private static void AnalyzeTerrain(
            OntologyTerrainWorldData worldData,
            Terrain terrain,
            Vector2Int resolution,
            Dictionary<string, List<OntologyFactTemplateEntry>> manualFactsByZone,
            OntologyTerrainAnalysisSummary summary)
        {
            var data = terrain.terrainData;
            var position = terrain.GetPosition();
            var size = data.size;
            var alphamaps = data.alphamapLayers > 0
                ? data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)
                : null;

            for (var z = 0; z < resolution.y; z++)
            {
                for (var x = 0; x < resolution.x; x++)
                {
                    var minX = x / (float)resolution.x;
                    var maxX = (x + 1) / (float)resolution.x;
                    var minZ = z / (float)resolution.y;
                    var maxZ = (z + 1) / (float)resolution.y;
                    var zone = new OntologyTerrainZoneData
                    {
                        terrainName = terrain.name,
                        coordinate = new Vector2Int(x, z),
                        zoneId = $"Zone_{terrain.name}_{x}_{z}",
                        worldBounds = new Bounds(
                            position + new Vector3((minX + maxX) * size.x * 0.5f, size.y * 0.5f, (minZ + maxZ) * size.z * 0.5f),
                            new Vector3((maxX - minX) * size.x, size.y, (maxZ - minZ) * size.z)),
                        averageHeight = AverageHeight(data, minX, minZ, maxX, maxZ),
                        averageSlope = AverageSlope(data, minX, minZ, maxX, maxZ)
                    };

                    AddFact(zone.automaticFacts, "has_concept", "TerrainZone");
                    AddFact(zone.automaticFacts, "terrain_source", terrain.name);
                    AddFact(zone.automaticFacts, "height_band", HeightBand(zone.averageHeight, size.y));
                    AddFact(zone.automaticFacts, "slope_band", SlopeBand(zone.averageSlope));
                    AddSurfaceFacts(worldData, data, alphamaps, zone, minX, minZ, maxX, maxZ);

                    if (manualFactsByZone.TryGetValue(zone.zoneId, out var manualFacts))
                    {
                        zone.manualFacts.AddRange(manualFacts);
                    }
                    worldData.zones.Add(zone);
                    summary.zoneCount++;
                }
            }
        }

        private static void AddSurfaceFacts(
            OntologyTerrainWorldData worldData,
            TerrainData terrainData,
            float[,,] alphamaps,
            OntologyTerrainZoneData zone,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            if (alphamaps == null || terrainData.terrainLayers == null) return;
            var coverage = LayerCoverage(alphamaps, minX, minZ, maxX, maxZ);
            var dominantIndex = -1;
            var dominantCoverage = 0f;
            for (var index = 0; index < coverage.Length; index++)
            {
                if (coverage[index] > dominantCoverage)
                {
                    dominantCoverage = coverage[index];
                    dominantIndex = index;
                }
            }

            if (dominantIndex >= 0 && dominantIndex < terrainData.terrainLayers.Length && terrainData.terrainLayers[dominantIndex] != null)
            {
                AddFact(zone.automaticFacts, "terrain_layer", terrainData.terrainLayers[dominantIndex].name);
            }

            foreach (var mapping in worldData.terrainLayerMappings)
            {
                if (mapping?.terrainLayer == null || mapping.facts == null) continue;
                var layerIndex = Array.IndexOf(terrainData.terrainLayers, mapping.terrainLayer);
                if (layerIndex < 0 || layerIndex >= coverage.Length || coverage[layerIndex] < mapping.minimumCoverage) continue;
                AddFacts(zone.automaticFacts, mapping.facts);
            }
        }

        private static float[] LayerCoverage(float[,,] alphamaps, float minX, float minZ, float maxX, float maxZ)
        {
            var height = alphamaps.GetLength(0);
            var width = alphamaps.GetLength(1);
            var layers = alphamaps.GetLength(2);
            var totals = new float[layers];
            var startX = Mathf.Clamp(Mathf.FloorToInt(minX * width), 0, width - 1);
            var endX = Mathf.Clamp(Mathf.CeilToInt(maxX * width), startX + 1, width);
            var startZ = Mathf.Clamp(Mathf.FloorToInt(minZ * height), 0, height - 1);
            var endZ = Mathf.Clamp(Mathf.CeilToInt(maxZ * height), startZ + 1, height);
            var sampleCount = 0;
            for (var z = startZ; z < endZ; z++)
            {
                for (var x = startX; x < endX; x++)
                {
                    for (var layer = 0; layer < layers; layer++) totals[layer] += alphamaps[z, x, layer];
                    sampleCount++;
                }
            }
            if (sampleCount > 0)
            {
                for (var layer = 0; layer < totals.Length; layer++) totals[layer] /= sampleCount;
            }
            return totals;
        }

        private static float AverageHeight(TerrainData data, float minX, float minZ, float maxX, float maxZ)
        {
            return Sample(data, minX, minZ, maxX, maxZ, data.GetInterpolatedHeight);
        }

        private static float AverageSlope(TerrainData data, float minX, float minZ, float maxX, float maxZ)
        {
            return Sample(data, minX, minZ, maxX, maxZ, data.GetSteepness);
        }

        private static float Sample(TerrainData data, float minX, float minZ, float maxX, float maxZ, Func<float, float, float> sample)
        {
            var total = 0f;
            const int sampleSize = 3;
            for (var z = 0; z < sampleSize; z++)
            {
                for (var x = 0; x < sampleSize; x++)
                {
                    var u = Mathf.Lerp(minX, maxX, (x + 0.5f) / sampleSize);
                    var v = Mathf.Lerp(minZ, maxZ, (z + 0.5f) / sampleSize);
                    total += sample(u, v);
                }
            }
            return total / (sampleSize * sampleSize);
        }

        private static string HeightBand(float height, float terrainHeight)
        {
            var ratio = terrainHeight <= 0f ? 0f : height / terrainHeight;
            return ratio < 0.33f ? "Low" : ratio < 0.66f ? "Medium" : "High";
        }

        private static string SlopeBand(float slope)
        {
            return slope < 10f ? "Flat" : slope < 25f ? "Gentle" : slope < 45f ? "Steep" : "Cliff";
        }

        private static Dictionary<string, List<OntologyFactTemplateEntry>> PreserveManualFacts(List<OntologyTerrainZoneData> zones)
        {
            var result = new Dictionary<string, List<OntologyFactTemplateEntry>>();
            if (zones == null) return result;
            foreach (var zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.zoneId) || zone.manualFacts == null) continue;
                var copies = new List<OntologyFactTemplateEntry>();
                foreach (var fact in zone.manualFacts)
                {
                    if (fact != null && !string.IsNullOrWhiteSpace(fact.predicate) && !string.IsNullOrWhiteSpace(fact.obj))
                    {
                        copies.Add(new OntologyFactTemplateEntry { predicate = fact.predicate, obj = fact.obj });
                    }
                }
                result[zone.zoneId] = copies;
            }
            return result;
        }

        private static List<Terrain> ResolveTerrains(OntologyTerrainWorldData worldData)
        {
            var result = new List<Terrain>();
            if (worldData.terrains != null)
            {
                foreach (var terrain in worldData.terrains)
                {
                    if (terrain != null && !result.Contains(terrain)) result.Add(terrain);
                }
            }
            if (worldData.terrain != null && !result.Contains(worldData.terrain)) result.Add(worldData.terrain);
            if (result.Count == 0) result.AddRange(Terrain.activeTerrains);
            return result;
        }

        private static void AddFacts(List<OntologyFactTemplateEntry> target, List<OntologyFactTemplateEntry> source)
        {
            foreach (var fact in source)
            {
                if (fact != null) AddFact(target, fact.predicate, fact.obj);
            }
        }

        private static void AddFact(List<OntologyFactTemplateEntry> target, string predicate, string obj)
        {
            if (string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj)) return;
            foreach (var existing in target)
            {
                if (existing != null && existing.predicate == predicate && existing.obj == obj) return;
            }
            target.Add(new OntologyFactTemplateEntry { predicate = predicate, obj = obj });
        }
    }

    public sealed class OntologyTerrainAnalysisSummary
    {
        public int zoneCount;
        public List<string> warnings = new();
    }
}
