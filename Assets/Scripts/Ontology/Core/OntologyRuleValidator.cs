using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyRuleValidator
    {
        public static List<string> Validate(IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            var warnings = new List<string>();
            var ids = new HashSet<string>();
            if (definitions == null)
            {
                warnings.Add("Rule definitions are missing.");
                return warnings;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    warnings.Add($"Rule[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.id))
                {
                    warnings.Add($"Rule[{i}] has an empty id.");
                }
                else if (!ids.Add(definition.id))
                {
                    warnings.Add($"Rule '{definition.id}' is duplicated.");
                }

                if (definition.conditions == null || definition.conditions.Count == 0)
                {
                    warnings.Add($"Rule '{definition.id}' has no conditions.");
                }
                else
                {
                    for (var conditionIndex = 0; conditionIndex < definition.conditions.Count; conditionIndex++)
                    {
                        ValidateCondition(definition.id, conditionIndex, definition.conditions[conditionIndex], warnings);
                    }
                }

                if (definition.effects == null || definition.effects.Count == 0)
                {
                    warnings.Add($"Rule '{definition.id}' has no effects.");
                }
                else
                {
                    for (var effectIndex = 0; effectIndex < definition.effects.Count; effectIndex++)
                    {
                        ValidateEffect(definition.id, effectIndex, definition.effects[effectIndex], warnings);
                    }
                }
            }

            return warnings;
        }

        private static void ValidateCondition(string ruleId, int index, OntologyCondition condition, List<string> warnings)
        {
            if (condition == null)
            {
                warnings.Add($"Rule '{ruleId}' condition[{index}] is null.");
                return;
            }

            if (!System.Enum.IsDefined(typeof(OntologyConditionKind), condition.kind))
            {
                warnings.Add($"Rule '{ruleId}' condition[{index}] has an unsupported kind.");
                return;
            }

            if (string.IsNullOrWhiteSpace(condition.subject))
            {
                warnings.Add($"Rule '{ruleId}' condition[{index}] has an empty subject.");
            }

            if ((condition.kind == OntologyConditionKind.Fact || condition.kind == OntologyConditionKind.NotFact)
                && string.IsNullOrWhiteSpace(condition.predicate))
            {
                warnings.Add($"Rule '{ruleId}' condition[{index}] has an empty predicate.");
            }

            if (string.IsNullOrWhiteSpace(condition.obj))
            {
                warnings.Add($"Rule '{ruleId}' condition[{index}] has an empty object/concept.");
            }
        }

        private static void ValidateEffect(string ruleId, int index, OntologyEffect effect, List<string> warnings)
        {
            if (effect == null)
            {
                warnings.Add($"Rule '{ruleId}' effect[{index}] is null.");
                return;
            }

            if (!System.Enum.IsDefined(typeof(OntologyEffectKind), effect.kind))
            {
                warnings.Add($"Rule '{ruleId}' effect[{index}] has an unsupported kind.");
            }

            if (string.IsNullOrWhiteSpace(effect.subject))
            {
                warnings.Add($"Rule '{ruleId}' effect[{index}] has an empty subject.");
            }

            if (string.IsNullOrWhiteSpace(effect.predicate))
            {
                warnings.Add($"Rule '{ruleId}' effect[{index}] has an empty predicate.");
            }

            if (string.IsNullOrWhiteSpace(effect.obj))
            {
                warnings.Add($"Rule '{ruleId}' effect[{index}] has an empty object.");
            }
        }
    }
}
