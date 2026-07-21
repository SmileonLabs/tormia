using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Remembers which database rules are controlled by world rule blocks. A controlled
    /// rule is inactive when no object owns a matching block.
    /// </summary>
    public sealed class OntologyRuleBlockRegistry : MonoBehaviour
    {
        [SerializeField] private List<string> controlledRuleIds = new();

        public IReadOnlyList<string> ControlledRuleIds => controlledRuleIds;

        public bool IsControlled(string ruleId) =>
            !string.IsNullOrWhiteSpace(ruleId) && controlledRuleIds.Contains(ruleId);

        public bool SetControlled(string ruleId, bool controlled)
        {
            ruleId = ruleId?.Trim();
            if (string.IsNullOrWhiteSpace(ruleId)) return false;
            if (controlled)
            {
                if (controlledRuleIds.Contains(ruleId)) return false;
                controlledRuleIds.Add(ruleId);
                return true;
            }

            return controlledRuleIds.Remove(ruleId);
        }

        public void Replace(IEnumerable<string> ruleIds)
        {
            controlledRuleIds = ruleIds == null
                ? new List<string>()
                : ruleIds
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct()
                    .ToList();
        }
    }
}
