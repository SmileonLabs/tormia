using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyTerrainTileGridBuilder : MonoBehaviour
    {
        [SerializeField] private int width = 5;
        [SerializeField] private int height = 5;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private string tileIdPrefix = "Tile";
        [SerializeField] private OntologyTerrainTileDefinition defaultDefinition;
        [SerializeField] private OntologyTerrainTileOverride[] overrides = Array.Empty<OntologyTerrainTileOverride>();

        public int Width => Mathf.Max(1, width);
        public int Height => Mathf.Max(1, height);

        [ContextMenu("Generate Ontology Tile Grid")]
        public void GenerateGrid()
        {
            RebuildGrid();
        }

        [ContextMenu("Rebuild Ontology Tile Grid")]
        public void RebuildGrid()
        {
            ClearGrid();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    CreateTile(x, y, GetDefinitionFor(x, y));
                }
            }
        }

        [ContextMenu("Update Existing Ontology Tile Grid")]
        public void UpdateExistingGrid()
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    ApplyTileDefinition(x, y, GetDefinitionFor(x, y));
                }
            }
        }

        [ContextMenu("Clear Ontology Tile Grid")]
        public void ClearGrid()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
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

        public GameObject ApplyTileDefinition(int x, int y, OntologyTerrainTileDefinition definition)
        {
            if (!IsInBounds(x, y))
            {
                return null;
            }

            var tile = FindTile(x, y);
            if (tile == null)
            {
                tile = CreateTile(x, y, definition);
            }
            else
            {
                ConfigureTile(tile, x, y, definition);
            }

            return tile;
        }

        public GameObject ApplyTileDefinitionAndStoreOverride(int x, int y, OntologyTerrainTileDefinition definition)
        {
            SetOverride(x, y, definition);
            return ApplyTileDefinition(x, y, definition != null ? definition : defaultDefinition);
        }

        public void FillRectangle(int minX, int minY, int maxX, int maxY, OntologyTerrainTileDefinition definition)
        {
            var startX = Mathf.Clamp(Mathf.Min(minX, maxX), 0, Width - 1);
            var endX = Mathf.Clamp(Mathf.Max(minX, maxX), 0, Width - 1);
            var startY = Mathf.Clamp(Mathf.Min(minY, maxY), 0, Height - 1);
            var endY = Mathf.Clamp(Mathf.Max(minY, maxY), 0, Height - 1);

            for (var y = startY; y <= endY; y++)
            {
                for (var x = startX; x <= endX; x++)
                {
                    ApplyTileDefinitionAndStoreOverride(x, y, definition);
                }
            }
        }

        private GameObject CreateTile(int x, int y, OntologyTerrainTileDefinition definition)
        {
            var tile = definition != null && definition.TilePrefab != null
                ? Instantiate(definition.TilePrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = GetTileId(x, y);
            tile.transform.SetParent(transform, false);
            tile.transform.localPosition = new Vector3(x * tileSize, 0f, y * tileSize);
            if (definition == null || definition.TilePrefab == null)
            {
                tile.transform.localScale = new Vector3(tileSize, 0.08f, tileSize);
            }

            ConfigureTile(tile, x, y, definition);
            return tile;
        }

        private void ConfigureTile(GameObject tile, int x, int y, OntologyTerrainTileDefinition definition)
        {
            tile.name = GetTileId(x, y);
            var ontologyObject = tile.GetComponent<OntologyObject>();
            if (ontologyObject == null)
            {
                ontologyObject = tile.AddComponent<OntologyObject>();
            }

            var facts = BuildFacts(x, y, definition);
            var concepts = definition != null ? definition.Concepts : new[] { "TerrainTile" };
            ontologyObject.ConfigureOntologyData(tile.name, concepts, facts);

            var renderer = tile.GetComponent<Renderer>();
            if (renderer != null && definition != null)
            {
                renderer.sharedMaterial = definition.PreviewMaterial != null
                    ? definition.PreviewMaterial
                    : CreatePreviewMaterial(definition.Color);
            }
        }

        private void SetOverride(int x, int y, OntologyTerrainTileDefinition definition)
        {
            if (!IsInBounds(x, y))
            {
                return;
            }

            for (var i = 0; i < overrides.Length; i++)
            {
                var tileOverride = overrides[i];
                if (tileOverride != null && tileOverride.x == x && tileOverride.y == y)
                {
                    tileOverride.definition = definition;
                    return;
                }
            }

            var next = new OntologyTerrainTileOverride[overrides.Length + 1];
            Array.Copy(overrides, next, overrides.Length);
            next[next.Length - 1] = new OntologyTerrainTileOverride
            {
                x = x,
                y = y,
                definition = definition
            };
            overrides = next;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        private OntologyFactEntry[] BuildFacts(int x, int y, OntologyTerrainTileDefinition definition)
        {
            var sourceFacts = definition != null ? definition.Facts : Array.Empty<OntologyFactEntry>();
            var facts = new OntologyFactEntry[sourceFacts.Length + 3];
            facts[0] = new OntologyFactEntry { predicate = "grid_x", obj = x.ToString() };
            facts[1] = new OntologyFactEntry { predicate = "grid_y", obj = y.ToString() };
            facts[2] = new OntologyFactEntry { predicate = "tile_type", obj = definition != null ? definition.TileTypeId : "Undefined" };
            for (var i = 0; i < sourceFacts.Length; i++)
            {
                facts[i + 3] = new OntologyFactEntry
                {
                    predicate = sourceFacts[i] != null ? sourceFacts[i].predicate : string.Empty,
                    obj = sourceFacts[i] != null ? sourceFacts[i].obj : string.Empty
                };
            }

            return facts;
        }

        private OntologyTerrainTileDefinition GetDefinitionFor(int x, int y)
        {
            foreach (var tileOverride in overrides)
            {
                if (tileOverride != null && tileOverride.x == x && tileOverride.y == y && tileOverride.definition != null)
                {
                    return tileOverride.definition;
                }
            }

            return defaultDefinition;
        }

        private GameObject FindTile(int x, int y)
        {
            var tileName = GetTileId(x, y);
            var tileTransform = transform.Find(tileName);
            return tileTransform != null ? tileTransform.gameObject : null;
        }

        private string GetTileId(int x, int y)
        {
            return $"{tileIdPrefix}_{x}_{y}";
        }

        private static Material CreatePreviewMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            material.color = color;
            return material;
        }
    }

    [Serializable]
    public sealed class OntologyTerrainTileOverride
    {
        public int x;
        public int y;
        public OntologyTerrainTileDefinition definition;
    }
}
