using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyQuestValidator
    {
        public static List<string> Validate(IReadOnlyList<OntologyQuestDefinition> definitions)
        {
            var warnings = new List<string>();
            var ids = new HashSet<string>();
            if (definitions == null)
            {
                warnings.Add("Quest definitions are missing.");
                return warnings;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    warnings.Add($"Quest[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.id))
                {
                    warnings.Add($"Quest[{i}] has an empty id.");
                }
                else if (!ids.Add(definition.id))
                {
                    warnings.Add($"Quest '{definition.id}' is duplicated.");
                }

                if (string.IsNullOrWhiteSpace(definition.title))
                {
                    warnings.Add($"Quest '{definition.id}' has an empty title.");
                }

                if (string.IsNullOrWhiteSpace(definition.hookPredicate))
                {
                    warnings.Add($"Quest '{definition.id}' has an empty hook predicate.");
                }

                if (string.IsNullOrWhiteSpace(definition.hookObject))
                {
                    warnings.Add($"Quest '{definition.id}' has an empty hook object.");
                }

                if (definition.goals == null || definition.goals.Count == 0)
                {
                    warnings.Add($"Quest '{definition.id}' has no goals.");
                    continue;
                }

                for (var goalIndex = 0; goalIndex < definition.goals.Count; goalIndex++)
                {
                    ValidateGoal(definition.id, goalIndex, definition.goals[goalIndex], warnings);
                }
            }

            return warnings;
        }

        private static void ValidateGoal(string questId, int index, OntologyQuestGoalDefinition goal, List<string> warnings)
        {
            if (goal == null)
            {
                warnings.Add($"Quest '{questId}' goal[{index}] is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(goal.actionVerb))
            {
                warnings.Add($"Quest '{questId}' goal[{index}] has an empty action verb.");
            }

            if (string.IsNullOrWhiteSpace(goal.completionPredicate))
            {
                warnings.Add($"Quest '{questId}' goal[{index}] has an empty completion predicate.");
            }

            if (string.IsNullOrWhiteSpace(goal.descriptionFormat))
            {
                warnings.Add($"Quest '{questId}' goal[{index}] has an empty description format.");
            }

            var hasConditionSource = goal.conditions != null && goal.conditions.Count > 0;
            var hasLegacySource = !string.IsNullOrWhiteSpace(goal.sourcePredicate) && !string.IsNullOrWhiteSpace(goal.sourceObject);
            if (!hasConditionSource && !hasLegacySource)
            {
                warnings.Add($"Quest '{questId}' goal[{index}] has no source conditions.");
            }

            if (hasConditionSource && string.IsNullOrWhiteSpace(goal.targetPattern))
            {
                warnings.Add($"Quest '{questId}' goal[{index}] has an empty target pattern.");
            }
        }
    }
}
