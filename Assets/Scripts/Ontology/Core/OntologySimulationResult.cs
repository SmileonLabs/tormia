using System.Collections.Generic;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySimulationResult
    {
        public bool ReachedStableState { get; internal set; }
        public int Iterations { get; internal set; }
        public int TotalAddedFacts { get; internal set; }
        public int TotalChangedFacts { get; internal set; }
        public List<OntologySimulationStep> Steps { get; } = new();

        public string DumpEvents()
        {
            var builder = new StringBuilder();
            foreach (var step in Steps)
            {
                builder.Append("[Iteration ");
                builder.Append(step.Iteration);
                builder.Append("] addedFacts=");
                builder.Append(step.AddedFactCount);
                builder.Append(" changedFacts=");
                builder.AppendLine(step.ChangedFactCount.ToString());

                foreach (var ontologyEvent in step.Events)
                {
                    builder.Append("[Event] ");
                    builder.AppendLine(ontologyEvent.ToString());
                }
            }

            builder.Append("[Stable] ");
            builder.Append(ReachedStableState);
            builder.Append(" after ");
            builder.Append(Iterations);
            builder.Append(" iteration(s)");
            return builder.ToString().TrimEnd();
        }
    }

    public sealed class OntologySimulationStep
    {
        public OntologySimulationStep(int iteration, List<OntologyEvent> events, int addedFactCount, int changedFactCount)
        {
            Iteration = iteration;
            Events = events;
            AddedFactCount = addedFactCount;
            ChangedFactCount = changedFactCount;
        }

        public int Iteration { get; }
        public List<OntologyEvent> Events { get; }
        public int AddedFactCount { get; }
        public int ChangedFactCount { get; }
    }
}
