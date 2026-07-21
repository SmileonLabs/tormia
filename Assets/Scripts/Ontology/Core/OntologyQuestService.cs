using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyQuestService
    {
        private readonly IReadOnlyList<OntologyQuestDefinition> definitions;

        public OntologyQuestService(IReadOnlyList<OntologyQuestDefinition> definitions)
        {
            this.definitions = definitions;
        }

        public List<OntologyQuest> Generate(OntologyWorldState world, OntologyId actorId)
        {
            if (definitions == null || definitions.Count == 0)
            {
                return new List<OntologyQuest>();
            }

            var generator = new OntologyQuestGenerator(definitions);
            return generator.Generate(world, actorId);
        }

        public int ApplyCompletionFacts(OntologyWorldState world, OntologyId actorId)
        {
            var quests = Generate(world, actorId);
            return new OntologyQuestCompletionSystem().ApplyCompletionFacts(world, actorId, quests);
        }
    }
}
