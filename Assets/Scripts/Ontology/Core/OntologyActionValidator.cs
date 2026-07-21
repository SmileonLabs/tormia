using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyActionValidator
    {
        public static List<string> ValidateCandidates(IReadOnlyList<OntologyActionCandidateDefinition> definitions)
        {
            var warnings = new List<string>();
            if (definitions == null)
            {
                warnings.Add("Action candidate definitions are missing.");
                return warnings;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    warnings.Add($"ActionCandidate[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.actionVerb))
                {
                    warnings.Add($"ActionCandidate[{i}] has an empty action verb.");
                }

                if (string.IsNullOrWhiteSpace(definition.targetPattern))
                {
                    warnings.Add($"ActionCandidate '{definition.actionVerb}' has an empty target pattern.");
                }

                if (string.IsNullOrWhiteSpace(definition.labelFormat))
                {
                    warnings.Add($"ActionCandidate '{definition.actionVerb}' has an empty label format.");
                }

                if (definition.conditions == null || definition.conditions.Count == 0)
                {
                    warnings.Add($"ActionCandidate '{definition.actionVerb}' has no conditions.");
                }
            }

            return warnings;
        }

        public static List<string> ValidateEffects(IReadOnlyList<OntologyActionEffectDefinition> definitions)
        {
            var warnings = new List<string>();
            if (definitions == null)
            {
                warnings.Add("Action effect definitions are missing.");
                return warnings;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    warnings.Add($"ActionEffect[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.actionVerb))
                {
                    warnings.Add($"ActionEffect[{i}] has an empty action verb.");
                }

                var hasStructuredEffects =
                    definition.effects != null && definition.effects.Count > 0;
                if (!hasStructuredEffects && string.IsNullOrWhiteSpace(definition.subjectPattern))
                {
                    warnings.Add($"ActionEffect '{definition.actionVerb}' has an empty subject pattern.");
                }

                if (!hasStructuredEffects && string.IsNullOrWhiteSpace(definition.predicate))
                {
                    warnings.Add($"ActionEffect '{definition.actionVerb}' has an empty predicate.");
                }

                if (!hasStructuredEffects && string.IsNullOrWhiteSpace(definition.objectPattern))
                {
                    warnings.Add($"ActionEffect '{definition.actionVerb}' has an empty object pattern.");
                }

                if (hasStructuredEffects)
                {
                    for (var effectIndex = 0; effectIndex < definition.effects.Count; effectIndex++)
                    {
                        var effect = definition.effects[effectIndex];
                        if (effect == null ||
                            string.IsNullOrWhiteSpace(effect.subject) ||
                            string.IsNullOrWhiteSpace(effect.predicate) ||
                            string.IsNullOrWhiteSpace(effect.obj))
                        {
                            warnings.Add(
                                $"ActionEffect '{definition.actionVerb}' effect[{effectIndex}] is incomplete.");
                        }
                    }
                }
            }

            return warnings;
        }
    }
}
