using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyQuestGenerator
    {
        private readonly List<OntologyQuestDefinition> definitions;

        public IReadOnlyList<OntologyQuestDefinition> Definitions => definitions;

        public OntologyQuestGenerator()
        {
            definitions = CreateDefaultDefinitions();
        }

        public OntologyQuestGenerator(IReadOnlyList<OntologyQuestDefinition> definitions)
        {
            this.definitions = definitions != null && definitions.Count > 0
                ? new List<OntologyQuestDefinition>(definitions)
                : CreateDefaultDefinitions();
        }

        public List<OntologyQuest> Generate(OntologyWorldState world, OntologyId actorId)
        {
            var quests = new List<OntologyQuest>();
            if (world == null)
            {
                return quests;
            }

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                AddQuestsForDefinition(world, actorId, definition, quests);
            }

            return quests;
        }

        private static void AddQuestsForDefinition(
            OntologyWorldState world,
            OntologyId actorId,
            OntologyQuestDefinition definition,
            List<OntologyQuest> quests)
        {
            foreach (var hook in world.FindFacts(definition.hookPredicate, definition.hookObject))
            {
                if (ContainsQuest(quests, definition.id))
                {
                    continue;
                }

                var quest = new OntologyQuest(
                    definition.id,
                    definition.title,
                    string.Format(definition.reasonFormat, hook.Subject));

                AddGoals(world, actorId, definition, quest);
                quests.Add(quest);
            }
        }

        private static bool ContainsQuest(List<OntologyQuest> quests, OntologyId questId)
        {
            foreach (var quest in quests)
            {
                if (quest.Id.Equals(questId))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddGoals(
            OntologyWorldState world,
            OntologyId actorId,
            OntologyQuestDefinition definition,
            OntologyQuest quest)
        {
            foreach (var goalDefinition in definition.goals)
            {
                if (goalDefinition.conditions != null && goalDefinition.conditions.Count > 0)
                {
                    AddGoalsFromConditions(world, actorId, goalDefinition, quest);
                    continue;
                }

                foreach (var source in world.FindSubjects(goalDefinition.sourcePredicate, goalDefinition.sourceObject))
                {
                    var action = new OntologyAction(actorId, goalDefinition.actionVerb, source);
                    quest.Goals.Add(new OntologyQuestGoal(
                        action,
                        string.Format(goalDefinition.descriptionFormat, source),
                        world.HasFact(action.ActorId, goalDefinition.completionPredicate, action.TargetId)));
                }
            }
        }

        private static void AddGoalsFromConditions(
            OntologyWorldState world,
            OntologyId actorId,
            OntologyQuestGoalDefinition goalDefinition,
            OntologyQuest quest)
        {
            foreach (var binding in OntologyConditionMatcher.Match(world, goalDefinition.conditions))
            {
                binding["?actor"] = actorId;
                var target = OntologyConditionMatcher.Resolve(goalDefinition.targetPattern, binding);
                var action = new OntologyAction(actorId, goalDefinition.actionVerb, target);
                quest.Goals.Add(new OntologyQuestGoal(
                    action,
                    Format(goalDefinition.descriptionFormat, binding),
                    world.HasFact(action.ActorId, goalDefinition.completionPredicate, action.TargetId)));
            }
        }

        private static string Format(string format, Dictionary<string, OntologyId> binding)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return string.Empty;
            }

            foreach (var pair in binding)
            {
                format = format.Replace("{" + pair.Key.TrimStart('?') + "}", pair.Value.ToString());
            }

            return format;
        }

        private static List<OntologyQuestDefinition> CreateDefaultDefinitions()
        {
            return new List<OntologyQuestDefinition>
            {
                new OntologyQuestDefinition
                {
                    id = "InvestigateSmokeAndPanic",
                    title = "Investigate Smoke and Panic",
                    reasonFormat = "{0} reports smoke and village panic",
                    hookPredicate = "offers",
                    hookObject = "InvestigateSmokeAndPanic",
                    goals =
                    {
                        new OntologyQuestGoalDefinition
                        {
                            actionVerb = "inspect",
                            completionPredicate = "inspects",
                            descriptionFormat = "Inspect {target} to identify the source of smoke",
                            targetPattern = "?target",
                            conditions =
                            {
                                OntologyCondition.Fact("?target", "emits", "Smoke")
                            }
                        },
                        new OntologyQuestGoalDefinition
                        {
                            actionVerb = "help",
                            completionPredicate = "helps",
                            descriptionFormat = "Help {target} recover from the disturbance",
                            targetPattern = "?target",
                            conditions =
                            {
                                OntologyCondition.Fact("?target", "state", "Disturbed")
                            }
                        }
                    }
                },
                new OntologyQuestDefinition
                {
                    id = "ColdProtectionPreparation",
                    title = "Prepare for the Cold",
                    reasonFormat = "{0} recommends protective clothing",
                    hookPredicate = "offers",
                    hookObject = "ColdProtectionPreparation",
                    goals =
                    {
                        new OntologyQuestGoalDefinition
                        {
                            actionVerb = "equip_part",
                            completionPredicate = OntologyPredicates.EquippedPart,
                            descriptionFormat = "Equip {target} to gain cold protection",
                            targetPattern = "?target",
                            conditions =
                            {
                                OntologyCondition.Fact("?target", OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection)
                            }
                        }
                    }
                },
                new OntologyQuestDefinition
                {
                    id = "SwampProtectionPreparation",
                    title = "Prepare for the Swamp",
                    reasonFormat = "{0} recommends swamp-resistant equipment",
                    hookPredicate = "offers",
                    hookObject = "SwampProtectionPreparation",
                    goals =
                    {
                        new OntologyQuestGoalDefinition
                        {
                            actionVerb = "equip_part",
                            completionPredicate = OntologyPredicates.EquippedPart,
                            descriptionFormat = "Equip {target} to gain swamp resistance",
                            targetPattern = "?target",
                            conditions =
                            {
                                OntologyCondition.Fact("?target", OntologyPredicates.GrantsCapability, OntologyObjects.SwampResistance)
                            }
                        }
                    }
                }
            };
        }
    }
}
