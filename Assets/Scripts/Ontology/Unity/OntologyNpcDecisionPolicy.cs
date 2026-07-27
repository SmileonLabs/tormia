using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "NpcDecisionPolicy",
        menuName = "Tormia/Ontology/NPC Decision Policy")]
    public sealed class OntologyNpcDecisionPolicy : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float decisionIntervalSeconds = 1f;
        [SerializeField] private string[] preferredActionVerbs = Array.Empty<string>();
        [SerializeField, Tooltip(
            "Development/offline only. Shared-world NPC execution remains server-authoritative.")]
        private bool allowLocalSimulation;
        [SerializeField] private bool autoRun;

        public float DecisionIntervalSeconds => decisionIntervalSeconds;
        public IReadOnlyList<string> PreferredActionVerbs => preferredActionVerbs;
        public bool AllowLocalSimulation => allowLocalSimulation;
        public bool AutoRun => autoRun;
    }
}
