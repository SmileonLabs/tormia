namespace Tormia.Ontology.Core
{
    public sealed class OntologyActionRunner
    {
        private readonly System.Collections.Generic.List<OntologyActionEffectDefinition> definitions;

        public OntologyActionRunner()
        {
            definitions = CreateDefaultDefinitions();
        }

        public OntologyActionRunner(System.Collections.Generic.IReadOnlyList<OntologyActionEffectDefinition> definitions)
        {
            this.definitions = definitions != null && definitions.Count > 0
                ? new System.Collections.Generic.List<OntologyActionEffectDefinition>(definitions)
                : CreateDefaultDefinitions();
        }

        public bool ApplyAction(OntologyWorldState world, OntologyAction action, OntologySession session = null)
        {
            if (world == null || !action.IsValid)
            {
                return false;
            }

            world.GetOrCreateEntity(action.ActorId);
            world.GetOrCreateEntity(action.TargetId);
            if (!action.ToolId.IsEmpty)
            {
                world.GetOrCreateEntity(action.ToolId);
            }

            var added = false;
            foreach (var definition in definitions)
            {
                if (definition == null || definition.actionVerb != action.Verb.Value)
                {
                    continue;
                }

                if (definition.requiresTool && action.ToolId.IsEmpty)
                {
                    continue;
                }

                added |= world.AddFact(
                    Resolve(definition.subjectPattern, action),
                    definition.predicate,
                    Resolve(definition.objectPattern, action));
            }

            if (!added)
            {
                added |= world.AddFact(action.ActorId, action.Verb, action.TargetId);
            }

            if (added)
            {
                session?.RecordAction(action);
            }

            return added;
        }

        private static OntologyId Resolve(string pattern, OntologyAction action)
        {
            switch (pattern)
            {
                case "?actor":
                    return action.ActorId;
                case "?target":
                    return action.TargetId;
                case "?tool":
                    return action.ToolId;
                default:
                    return pattern;
            }
        }

        public static System.Collections.Generic.List<OntologyActionEffectDefinition> CreateDefaultDefinitions()
        {
            return new System.Collections.Generic.List<OntologyActionEffectDefinition>
            {
                new OntologyActionEffectDefinition { actionVerb = "attack", predicate = "attacks", objectPattern = "?target" },
                new OntologyActionEffectDefinition { actionVerb = "attack", predicate = "attacks_with", objectPattern = "?tool", requiresTool = true },
                new OntologyActionEffectDefinition { actionVerb = "talk", predicate = "talks_to", objectPattern = "?target" },
                new OntologyActionEffectDefinition { actionVerb = "inspect", predicate = "inspects", objectPattern = "?target" },
                new OntologyActionEffectDefinition { actionVerb = "help", predicate = "helps", objectPattern = "?target" },
                new OntologyActionEffectDefinition { actionVerb = "equip_part", predicate = OntologyPredicates.EquippedPart, objectPattern = "?target" },
                new OntologyActionEffectDefinition { actionVerb = "unequip_part", predicate = OntologyPredicates.UnequipPart, objectPattern = "?target" }
            };
        }
    }
}
