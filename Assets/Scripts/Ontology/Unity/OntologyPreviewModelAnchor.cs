using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Editor-visible anchor used to author a catalog item's preview transform.</summary>
    public sealed class OntologyPreviewModelAnchor : MonoBehaviour
    {
        [SerializeField] private OntologyPlaceableCatalog catalog;
        [SerializeField] private string definitionId;
        [SerializeField] private Transform previewModel;
        [SerializeField, HideInInspector] private Vector3 automaticLocalPosition;
        [SerializeField, HideInInspector] private Vector3 automaticLocalScale = Vector3.one;

        public OntologyPlaceableCatalog Catalog { get => catalog; set => catalog = value; }
        public string DefinitionId { get => definitionId; set => definitionId = value; }
        public Transform PreviewModel { get => previewModel; set => previewModel = value; }
        public Vector3 AutomaticLocalPosition => automaticLocalPosition;
        public Vector3 AutomaticLocalScale => automaticLocalScale;

        public void SetAutomaticBaseline(Transform model)
        {
            automaticLocalPosition = model != null ? model.localPosition : Vector3.zero;
            automaticLocalScale = model != null ? model.localScale : Vector3.one;
        }
    }
}
