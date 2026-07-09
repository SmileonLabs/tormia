using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Quest Database")]
    public sealed class OntologyQuestDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyQuestDefinition> definitions = new();

        public IReadOnlyList<OntologyQuestDefinition> Definitions => definitions;
    }
}
