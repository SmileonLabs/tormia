using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Rule Database")]
    public sealed class OntologyRuleDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyRuleDefinition> definitions = new();

        public IReadOnlyList<OntologyRuleDefinition> Definitions => definitions;
    }
}
