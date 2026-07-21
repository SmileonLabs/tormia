using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Editable visual/text settings for the runtime object placement HUD.</summary>
    [CreateAssetMenu(fileName = "ObjectPlacementUiTheme", menuName = "Tormia/Ontology/Object Placement UI Theme")]
    public sealed class OntologyObjectPlacementUiTheme : ScriptableObject
    {
        public Color categoryNormalColor = new(0.10f, 0.23f, 0.30f, 1f);
        public Color categorySelectedColor = new(0.18f, 0.55f, 0.65f, 1f);
        public float categoryFontSize = 16f;
        public string emptyCatalogTitle = "No objects";
        public string emptyCatalogDescription = "Add catalog entries to this category.";
    }
}
