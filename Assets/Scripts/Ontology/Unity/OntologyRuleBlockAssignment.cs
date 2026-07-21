using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologyRuleBlockBinding
    {
        public string ruleId;
        public string bindingVariable = "?target";
    }

    /// <summary>
    /// Connects data rules to a scene entity. The component contains no game-specific
    /// behaviour; it only says which rule variable represents this entity.
    /// </summary>
    public sealed class OntologyRuleBlockAssignment : MonoBehaviour
    {
        [SerializeField] private List<OntologyRuleBlockBinding> bindings = new();

        public IReadOnlyList<OntologyRuleBlockBinding> Bindings => bindings;

        public bool Add(string ruleId, string bindingVariable)
        {
            ruleId = ruleId?.Trim();
            bindingVariable = bindingVariable?.Trim();
            if (string.IsNullOrWhiteSpace(ruleId) || string.IsNullOrWhiteSpace(bindingVariable))
                return false;
            if (bindings.Any(value =>
                    value != null &&
                    value.ruleId == ruleId &&
                    value.bindingVariable == bindingVariable))
                return false;

            bindings.Add(new OntologyRuleBlockBinding
            {
                ruleId = ruleId,
                bindingVariable = bindingVariable
            });
            return true;
        }

        public bool Remove(string ruleId, string bindingVariable)
        {
            return bindings.RemoveAll(value =>
                value != null &&
                value.ruleId == ruleId &&
                value.bindingVariable == bindingVariable) > 0;
        }

        public void Replace(IEnumerable<OntologyRuleBlockBinding> values)
        {
            bindings = values == null
                ? new List<OntologyRuleBlockBinding>()
                : values
                    .Where(value =>
                        value != null &&
                        !string.IsNullOrWhiteSpace(value.ruleId) &&
                        !string.IsNullOrWhiteSpace(value.bindingVariable))
                    .Select(value => new OntologyRuleBlockBinding
                    {
                        ruleId = value.ruleId.Trim(),
                        bindingVariable = value.bindingVariable.Trim()
                    })
                    .ToList();
        }

        public void ApplyTo(OntologyWorldState world, string entityId)
        {
            if (world == null || string.IsNullOrWhiteSpace(entityId)) return;
            foreach (var binding in bindings.Where(value => value != null))
                world.AddFact(entityId, "has_rule_block", binding.ruleId);
        }
    }

}
