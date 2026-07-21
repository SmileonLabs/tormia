using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "FeaturePackDatabase",
        menuName = "Tormia/Ontology/Feature Pack Database")]
    public sealed class OntologyFeaturePackDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyFeaturePack> packs = new();

        public IReadOnlyList<OntologyFeaturePack> Packs => packs;

        public OntologyFeaturePack Find(string packId)
        {
            if (string.IsNullOrWhiteSpace(packId)) return null;
            return packs.FirstOrDefault(value => value != null && value.packId == packId);
        }
    }
}
