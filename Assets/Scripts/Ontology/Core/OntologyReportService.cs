using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyReportService
    {
        public string Build(
            OntologyWorldState world,
            OntologySession session,
            OntologySimulationResult result,
            IReadOnlyList<OntologyQuest> quests,
            IReadOnlyList<OntologyActionCandidate> candidates,
            string databaseStatus)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[OntologyWorldBootstrap]");
            builder.AppendLine(databaseStatus);
            builder.AppendLine(world != null ? world.DumpFacts() : "No world state.");
            builder.AppendLine(result != null ? result.DumpEvents() : "[Simulation] Not run yet.");
            builder.AppendLine(session != null ? session.DumpHistory() : "[History]\n- none");
            AppendQuests(builder, quests);
            AppendCandidates(builder, candidates);
            return builder.ToString().TrimEnd();
        }

        private static void AppendQuests(StringBuilder builder, IReadOnlyList<OntologyQuest> quests)
        {
            builder.AppendLine("[Generated Quests]");
            if (quests == null || quests.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            foreach (var quest in quests)
            {
                builder.Append("- ");
                builder.AppendLine(quest.ToString());
            }
        }

        private static void AppendCandidates(StringBuilder builder, IReadOnlyList<OntologyActionCandidate> candidates)
        {
            builder.AppendLine("[Available Actions]");
            if (candidates == null || candidates.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            foreach (var candidate in candidates)
            {
                builder.Append("- ");
                builder.AppendLine(candidate.ToString());
            }
        }
    }
}
