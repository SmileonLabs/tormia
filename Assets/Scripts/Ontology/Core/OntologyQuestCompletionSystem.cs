using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyQuestCompletionSystem
    {
        public int ApplyCompletionFacts(OntologyWorldState world, OntologyId actorId, List<OntologyQuest> quests)
        {
            if (world == null || actorId.IsEmpty || quests == null)
            {
                return 0;
            }

            var added = 0;
            foreach (var quest in quests)
            {
                if (!quest.IsCompleted)
                {
                    continue;
                }

                if (world.AddFact(actorId, "completed_quest", quest.Id))
                {
                    added++;
                }

                var questStateId = new OntologyId($"QuestState_{actorId}_{quest.Id}");
                if (world.AddFact(questStateId, "has_concept", "QuestState"))
                {
                    added++;
                }

                world.AddFact(questStateId, "actor", actorId);
                world.AddFact(questStateId, "quest", quest.Id);
                world.RemoveFacts(questStateId, "state");
                if (world.AddFact(questStateId, "state", "Completed"))
                {
                    added++;
                }
            }

            return added;
        }
    }
}
