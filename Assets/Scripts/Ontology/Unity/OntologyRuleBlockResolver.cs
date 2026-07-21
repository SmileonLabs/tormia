using System.Collections.Generic;
using System.Linq;

namespace Tormia.Ontology.Core
{
    public static class OntologyRuleBlockResolver
    {
        public static List<OntologyRuleDefinition> Resolve(
            IReadOnlyList<OntologyRuleDefinition> definitions,
            OntologyRuleBlockRegistry registry,
            IEnumerable<OntologyRuleBlockAssignment> assignments)
        {
            var resolved = new List<OntologyRuleDefinition>();
            if (definitions == null) return resolved;

            var assignmentList = assignments?
                .Where(value => value != null)
                .ToList() ?? new List<OntologyRuleBlockAssignment>();

            foreach (var definition in definitions.Where(value => value != null))
            {
                if (registry == null || !registry.IsControlled(definition.id))
                {
                    resolved.Add(definition);
                    continue;
                }

                foreach (var assignment in assignmentList)
                {
                    var ontology = assignment.GetComponent<OntologyObject>();
                    var entityId = ontology != null ? ontology.EntityId : assignment.gameObject.name;
                    foreach (var binding in assignment.Bindings.Where(value =>
                                 value != null && value.ruleId == definition.id))
                    {
                        resolved.Add(Bind(definition, binding.bindingVariable, entityId));
                    }
                }
            }

            return resolved;
        }

        public static OntologyRuleDefinition Bind(
            OntologyRuleDefinition source,
            string variable,
            string entityId)
        {
            var clone = new OntologyRuleDefinition
            {
                id = source.id + "@" + entityId,
                description = source.description
            };

            foreach (var condition in source.conditions.Where(value => value != null))
            {
                clone.conditions.Add(new OntologyCondition
                {
                    kind = condition.kind,
                    subject = Replace(condition.subject, variable, entityId),
                    predicate = Replace(condition.predicate, variable, entityId),
                    obj = Replace(condition.obj, variable, entityId)
                });
            }

            foreach (var effect in source.effects.Where(value => value != null))
            {
                clone.effects.Add(new OntologyEffect
                {
                    kind = effect.kind,
                    subject = Replace(effect.subject, variable, entityId),
                    predicate = Replace(effect.predicate, variable, entityId),
                    obj = Replace(effect.obj, variable, entityId)
                });
            }

            return clone;
        }

        public static List<string> GetVariables(OntologyRuleDefinition definition)
        {
            var variables = new HashSet<string>();
            if (definition == null) return variables.ToList();
            foreach (var condition in definition.conditions.Where(value => value != null))
            {
                AddVariable(variables, condition.subject);
                AddVariable(variables, condition.predicate);
                AddVariable(variables, condition.obj);
            }
            foreach (var effect in definition.effects.Where(value => value != null))
            {
                AddVariable(variables, effect.subject);
                AddVariable(variables, effect.predicate);
                AddVariable(variables, effect.obj);
            }
            return variables.OrderBy(value => value).ToList();
        }

        private static string Replace(string value, string variable, string entityId) =>
            value == variable ? entityId : value;

        private static void AddVariable(ISet<string> variables, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value[0] == '?')
                variables.Add(value);
        }
    }
}
