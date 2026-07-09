using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyMapDataSceneBuilder : MonoBehaviour
    {
        [SerializeField] private OntologyMapData mapData;
        [SerializeField] private Transform gridRoot;
        [SerializeField] private List<OntologyTileTemplate> tileTemplates = new List<OntologyTileTemplate>();
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private bool buildOnStart;
        [SerializeField] private bool resetWorldAfterBuild = true;

        private void Start()
        {
            if (buildOnStart)
            {
                BuildSceneTiles();
            }
        }

        [ContextMenu("Build Scene Tiles From Map Data")]
        public void BuildSceneTiles()
        {
            var root = GetGridRoot();
            ClearChildren(root);

            if (mapData == null)
            {
                Debug.LogWarning("[OntologyMapDataSceneBuilder] MapData is missing.", this);
                return;
            }

            if (mapData.placements == null)
            {
                Debug.LogWarning("[OntologyMapDataSceneBuilder] MapData placements are missing.", this);
                return;
            }

            for (var i = 0; i < mapData.placements.Count; i++)
            {
                var placement = mapData.placements[i];
                if (placement == null || string.IsNullOrWhiteSpace(placement.templateId))
                {
                    continue;
                }

                var template = FindTemplate(placement.templateId);
                if (template == null)
                {
                    Debug.LogWarning("[OntologyMapDataSceneBuilder] Missing tile template: " + placement.templateId, this);
                    continue;
                }

                CreateTile(root, placement, template);
            }

            if (resetWorldAfterBuild)
            {
                ResetWorldFromScene();
            }
        }

        [ContextMenu("Clear Generated Scene Tiles")]
        public void ClearSceneTiles()
        {
            ClearChildren(GetGridRoot());
        }

        [ContextMenu("Reset Ontology World From Scene")]
        public void ResetWorldFromScene()
        {
            var bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.ResetWorld(logReport: false);
            }
        }

        private Transform GetGridRoot()
        {
            if (gridRoot != null)
            {
                return gridRoot;
            }

            gridRoot = transform;
            return gridRoot;
        }

        private void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private void CreateTile(Transform root, TilePlacementData placement, OntologyTileTemplate template)
        {
            var tileId = GetTileId(placement.coordinate.x, placement.coordinate.y);
            var tile = template.tilePrefab != null
                ? Instantiate(template.tilePrefab, root)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (tile.transform.parent != root)
            {
                tile.transform.SetParent(root, false);
            }

            tile.name = tileId;
            tile.transform.localPosition = new Vector3(placement.coordinate.x * GetSafeTileSize(), 0f, placement.coordinate.y * GetSafeTileSize());
            if (template.tilePrefab == null)
            {
                tile.transform.localScale = new Vector3(GetSafeTileSize(), 0.08f, GetSafeTileSize());
            }

            ApplyVisual(tile, template);
            ApplyOntologyData(tile, tileId, placement, template);
        }

        private void ApplyVisual(GameObject tile, OntologyTileTemplate template)
        {
            var renderer = tile.GetComponent<Renderer>();
            if (renderer == null || template == null)
            {
                return;
            }

            if (template.previewMaterial != null)
            {
                renderer.sharedMaterial = template.previewMaterial;
            }
            else
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = template.previewColor;
                renderer.sharedMaterial = material;
            }
        }

        private void ApplyOntologyData(GameObject tile, string tileId, TilePlacementData placement, OntologyTileTemplate template)
        {
            var ontologyObject = tile.GetComponent<OntologyObject>();
            if (ontologyObject == null)
            {
                ontologyObject = tile.AddComponent<OntologyObject>();
            }

            var facts = BuildFactEntries(placement, template);
            ontologyObject.ConfigureOntologyData(tileId, new[] { "TerrainTile" }, facts.ToArray());
        }

        private List<OntologyFactEntry> BuildFactEntries(TilePlacementData placement, OntologyTileTemplate template)
        {
            var facts = new List<OntologyFactEntry>
            {
                new OntologyFactEntry { predicate = "grid_x", obj = placement.coordinate.x.ToString() },
                new OntologyFactEntry { predicate = "grid_y", obj = placement.coordinate.y.ToString() },
                new OntologyFactEntry { predicate = "tile_type", obj = template.EffectiveTemplateId }
            };

            AddTemplateEntries(facts, template.baseFacts);
            AddTemplateEntries(facts, placement.uniqueFacts);
            return facts;
        }

        private void AddTemplateEntries(List<OntologyFactEntry> target, List<OntologyFactTemplateEntry> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.predicate) || string.IsNullOrWhiteSpace(entry.obj))
                {
                    continue;
                }

                target.Add(new OntologyFactEntry
                {
                    predicate = entry.predicate,
                    obj = entry.obj
                });
            }
        }

        private OntologyTileTemplate FindTemplate(string templateId)
        {
            if (tileTemplates == null || string.IsNullOrWhiteSpace(templateId))
            {
                return null;
            }

            for (var i = 0; i < tileTemplates.Count; i++)
            {
                var template = tileTemplates[i];
                if (template != null && template.EffectiveTemplateId == templateId)
                {
                    return template;
                }
            }

            return null;
        }

        private string GetTileId(int x, int y)
        {
            return "Tile_" + x + "_" + y;
        }

        private float GetSafeTileSize()
        {
            return tileSize <= 0f ? 1f : tileSize;
        }
    }
}
