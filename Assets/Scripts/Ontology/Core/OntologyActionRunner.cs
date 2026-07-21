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

            var changed = false;
            var matchedDefinition = false;
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

                var initialBinding = new System.Collections.Generic.Dictionary<string, OntologyId>
                {
                    ["?actor"] = action.ActorId,
                    ["?target"] = action.TargetId
                };
                if (!action.ToolId.IsEmpty)
                {
                    initialBinding["?tool"] = action.ToolId;
                }

                foreach (var binding in OntologyConditionMatcher.Match(
                             world,
                             definition.conditions,
                             initialBinding))
                {
                    matchedDefinition = true;
                    if (definition.effects != null && definition.effects.Count > 0)
                    {
                        foreach (var effect in definition.effects)
                        {
                            changed |= ApplyEffect(world, effect, binding);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(definition.predicate))
                    {
                        changed |= world.AddFact(
                            Resolve(definition.subjectPattern, action),
                            definition.predicate,
                            Resolve(definition.objectPattern, action));
                    }
                }
            }

            if (!matchedDefinition)
            {
                changed |= world.AddFact(action.ActorId, action.Verb, action.TargetId);
            }

            if (changed)
            {
                session?.RecordAction(action);
            }

            return changed;
        }

        private static bool ApplyEffect(
            OntologyWorldState world,
            OntologyEffect effect,
            System.Collections.Generic.Dictionary<string, OntologyId> binding)
        {
            if (effect == null)
            {
                return false;
            }

            var subject = OntologyConditionMatcher.Resolve(effect.subject, binding);
            var predicate = OntologyConditionMatcher.Resolve(effect.predicate, binding);
            var obj = OntologyConditionMatcher.Resolve(effect.obj, binding);
            return effect.kind switch
            {
                OntologyEffectKind.RemoveFact => world.RemoveFact(subject, predicate, obj),
                OntologyEffectKind.SetFact => world.SetFact(subject, predicate, obj, out _),
                OntologyEffectKind.AdjustNumberFact => world.AdjustNumberFact(subject, predicate, obj),
                _ => world.AddFact(subject, predicate, obj)
            };
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
                new OntologyActionEffectDefinition { actionVerb = "unequip_part", predicate = OntologyPredicates.UnequipPart, objectPattern = "?target" },
                CreateUnequipWearableDefinition()
            };
        }

        private static OntologyActionEffectDefinition CreateUnequipWearableDefinition()
        {
            var definition = new OntologyActionEffectDefinition
            {
                actionVerb = OntologyActions.UnequipWearable
            };
            definition.conditions.Add(OntologyCondition.Fact(
                "?target",
                OntologyPredicates.EquippedBy,
                "?actor"));
            definition.conditions.Add(OntologyCondition.Fact(
                "?slot",
                OntologyPredicates.SlotOwner,
                "?actor"));
            definition.conditions.Add(OntologyCondition.Fact(
                "?slot",
                OntologyPredicates.EquippedItem,
                "?target"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?slot",
                OntologyPredicates.EquippedItem,
                "?target"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?target",
                OntologyPredicates.EquippedBy,
                "?actor"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?actor",
                OntologyPredicates.InteractionIntent,
                "?target"));
            return definition;
        }
    }
}
