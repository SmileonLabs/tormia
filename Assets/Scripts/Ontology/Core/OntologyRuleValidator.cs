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
                    var boundVariables = new HashSet<string>();
                    for (var conditionIndex = 0; conditionIndex < definition.conditions.Count; conditionIndex++)
                    {
                        var condition = definition.conditions[conditionIndex];
                        ValidateCondition(definition.id, conditionIndex, condition, warnings);
                        ValidateVariableBindings(definition.id, conditionIndex, condition, boundVariables, warnings);
                    }

                    ValidateEffectVariableBindings(definition.id, definition.effects, boundVariables, warnings);
                }

                var hasRuntimePresentation =
                    definition.runtimePresentation != null &&
                    !string.IsNullOrWhiteSpace(
                        definition.runtimePresentation.actorAnimationIntent);
                if ((definition.effects == null ||
                     definition.effects.Count == 0) &&
                    !hasRuntimePresentation)
                {
                    warnings.Add($"Rule '{definition.id}' has no effects.");
                }
                else if (definition.effects != null)
                {
                    for (var effectIndex = 0; effectIndex < definition.effects.Count; effectIndex++)
                    {
                        ValidateEffect(definition.id, effectIndex, definition.effects[effectIndex], warnings);
                    }
                }
                if (hasRuntimePresentation &&
                    !IsCanonicalRuntimeIntent(
                        definition.runtimePresentation.actorAnimationIntent))
                {
                    warnings.Add(
                        $"Rule '{definition.id}' has an invalid runtime presentation intent.");
                }
            }

            return warnings;
        }

        private static bool IsCanonicalRuntimeIntent(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;
            foreach (var character in value)
            {
                if (!(char.IsLetterOrDigit(character) ||
                      character == '_' ||
                      character == '-' ||
                      character == '.'))
                    return false;
            }
            return true;
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

        private static void ValidateVariableBindings(string ruleId, int index, OntologyCondition condition, HashSet<string> boundVariables, List<string> warnings)
        {
            if (condition == null) return;
            var variables = GetVariables(condition.subject, condition.predicate, condition.obj);
            var introducesBindings = condition.kind == OntologyConditionKind.Fact || condition.kind == OntologyConditionKind.HasConcept;
            if (!introducesBindings)
            {
                var allowsUnboundWildcard =
                    condition.kind == OntologyConditionKind.NotFact ||
                    condition.kind == OntologyConditionKind.NotConcept;
                foreach (var variable in variables)
                {
                    if (!boundVariables.Contains(variable) && !allowsUnboundWildcard)
                    {
                        warnings.Add($"Rule '{ruleId}' condition[{index}] uses '{variable}' before it is observed by a relation.");
                    }
                }
                return;
            }

            foreach (var variable in variables) boundVariables.Add(variable);
        }

        private static void ValidateEffectVariableBindings(string ruleId, List<OntologyEffect> effects, HashSet<string> boundVariables, List<string> warnings)
        {
            if (effects == null) return;
            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                if (effect == null) continue;
                foreach (var variable in GetVariables(effect.subject, effect.predicate, effect.obj))
                {
                    if (!boundVariables.Contains(variable))
                    {
                        warnings.Add($"Rule '{ruleId}' inferred relation[{index}] uses unbound variable '{variable}'.");
                    }
                }
            }
        }

        private static IEnumerable<string> GetVariables(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && value[0] == '?') yield return value;
            }
        }
    }
}
