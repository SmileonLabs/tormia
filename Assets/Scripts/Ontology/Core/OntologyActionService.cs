using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActionService
    {
        private readonly IReadOnlyList<OntologyActionCandidateDefinition> candidateDefinitions;
        private readonly IReadOnlyList<OntologyActionEffectDefinition> effectDefinitions;

        public OntologyActionService(
            IReadOnlyList<OntologyActionCandidateDefinition> candidateDefinitions,
            IReadOnlyList<OntologyActionEffectDefinition> effectDefinitions)
        {
            this.candidateDefinitions = candidateDefinitions;
            this.effectDefinitions = effectDefinitions;
        }

        public List<OntologyActionCandidate> GetCandidates(
            OntologyWorldState world,
            OntologyId actorId,
            IReadOnlyList<OntologyQuest> quests = null)
        {
            var generator = new OntologyActionCandidateGenerator(candidateDefinitions);
            var candidates = generator.Generate(world, actorId);

            if (quests == null || quests.Count == 0 || candidates.Count == 0)
            {
                return candidates;
            }

            var marked = new List<OntologyActionCandidate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var questTitle = FindMatchingQuestTitle(candidate.Action, quests);
                marked.Add(string.IsNullOrWhiteSpace(questTitle)
                    ? candidate
                    : candidate.WithQuestGoal(questTitle));
            }

            return marked;
        }

        public bool IsAvailable(OntologyWorldState world, OntologyId actorId, OntologyAction action)
        {
            if (!action.IsValid)
            {
                return false;
            }

            foreach (var candidate in GetCandidates(world, actorId))
            {
                if (candidate.Action.Equals(action))
                {
                    return true;
                }
            }

            return false;
        }

        public bool Apply(OntologyWorldState world, OntologySession session, OntologyAction action)
        {
            var runner = new OntologyActionRunner(effectDefinitions);
            return runner.ApplyAction(world, action, session);
        }

        private static string FindMatchingQuestTitle(OntologyAction action, IReadOnlyList<OntologyQuest> quests)
        {
            foreach (var quest in quests)
            {
                foreach (var goal in quest.Goals)
                {
                    if (goal.RecommendedAction.Equals(action))
                    {
                        return quest.Title;
                    }
                }
            }

            return string.Empty;
        }
    }
}
