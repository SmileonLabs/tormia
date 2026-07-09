using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(menuName = "Tormia/Ontology/Action Candidate Database")]
    public sealed class OntologyActionCandidateDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyActionCandidateDefinition> definitions = new();

        public IReadOnlyList<OntologyActionCandidateDefinition> Definitions => definitions;
    }
}
