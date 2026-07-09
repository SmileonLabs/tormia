using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Terrain Tile Definition")]
    public sealed class OntologyTerrainTileDefinition : ScriptableObject
    {
        [SerializeField] private string tileTypeId;
        [SerializeField] private Color color = Color.gray;
        [SerializeField] private Material previewMaterial;
        [SerializeField] private GameObject tilePrefab;
        [SerializeField] private string[] concepts = { "TerrainTile" };
        [SerializeField] private OntologyFactEntry[] facts = Array.Empty<OntologyFactEntry>();

        public string TileTypeId => string.IsNullOrWhiteSpace(tileTypeId) ? name : tileTypeId;
        public Color Color => color;
        public Material PreviewMaterial => previewMaterial;
        public GameObject TilePrefab => tilePrefab;
        public string[] Concepts => concepts ?? Array.Empty<string>();
        public OntologyFactEntry[] Facts => facts ?? Array.Empty<OntologyFactEntry>();

        public void Configure(string id, Color tileColor, OntologyFactEntry[] tileFacts, Material material = null, GameObject prefab = null)
        {
            tileTypeId = id;
            color = tileColor;
            previewMaterial = material;
            tilePrefab = prefab;
            concepts = new[] { "TerrainTile" };
            facts = tileFacts ?? Array.Empty<OntologyFactEntry>();
        }
    }
}
