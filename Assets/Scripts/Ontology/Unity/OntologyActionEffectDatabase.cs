using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Action Effect Database")]
    public sealed class OntologyActionEffectDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyActionEffectDefinition> definitions = new();

        public IReadOnlyList<OntologyActionEffectDefinition> Definitions => definitions;
    }
}
