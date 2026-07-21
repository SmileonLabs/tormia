using UnityEngine;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    /// <summary>Optional semantic label for a collider that may receive placed objects.</summary>
    public sealed class OntologyPlacementSurface : MonoBehaviour
    {
        [SerializeField] private OntologyPlacementSurfaceKind surfaceKind = OntologyPlacementSurfaceKind.Ground;
        public OntologyPlacementSurfaceKind SurfaceKind => surfaceKind;
    }

    /// <summary>Records which catalog entry created a scene instance.</summary>
    public sealed class OntologyPlaceableInstance : MonoBehaviour
    {
        private static readonly HashSet<OntologyPlaceableInstance> Active = new();
        [SerializeField] private string definitionId;
        public string DefinitionId => definitionId;
        public static IEnumerable<OntologyPlaceableInstance> ActiveInstances => Active;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        public void Configure(string value) => definitionId = value;
    }
}
