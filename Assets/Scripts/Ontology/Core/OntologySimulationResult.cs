using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySimulationResult
    {
        public bool ReachedStableState { get; internal set; }
        public int Iterations { get; internal set; }
        public int TotalAddedFacts { get; internal set; }
        public int TotalChangedFacts { get; internal set; }
        public int TotalEvaluatedRules { get; internal set; }
        public int TotalSkippedRules { get; internal set; }
        public long TotalEvaluationElapsedTicks { get; internal set; }
        public double TotalEvaluationElapsedMilliseconds =>
            ToMilliseconds(TotalEvaluationElapsedTicks);
        public List<OntologySimulationStep> Steps { get; } = new();

        public void Append(OntologySimulationResult followUp)
        {
            if (followUp == null)
            {
                return;
            }

            var offset = Iterations;
            TotalAddedFacts += followUp.TotalAddedFacts;
            TotalChangedFacts += followUp.TotalChangedFacts;
            TotalEvaluatedRules += followUp.TotalEvaluatedRules;
            TotalSkippedRules += followUp.TotalSkippedRules;
            TotalEvaluationElapsedTicks += followUp.TotalEvaluationElapsedTicks;
            Iterations += followUp.Iterations;
            ReachedStableState = followUp.ReachedStableState;
            foreach (var step in followUp.Steps)
            {
                Steps.Add(new OntologySimulationStep(
                    offset + step.Iteration,
                    step.Events,
                    step.AddedFactCount,
                    step.ChangedFactCount,
                    step.EvaluatedRuleCount,
                    step.SkippedRuleCount,
                    step.EvaluationElapsedTicks));
            }
        }

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
                builder.Append(step.ChangedFactCount);
                builder.Append(" evaluatedRules=");
                builder.Append(step.EvaluatedRuleCount);
                builder.Append(" skippedRules=");
                builder.Append(step.SkippedRuleCount);
                builder.Append(" evaluationMs=");
                builder.AppendLine(step.EvaluationElapsedMilliseconds.ToString(
                    "F3",
                    CultureInfo.InvariantCulture));

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

        internal static double ToMilliseconds(long elapsedTicks) =>
            elapsedTicks * 1000d / Stopwatch.Frequency;
    }

    public sealed class OntologySimulationStep
    {
        public OntologySimulationStep(
            int iteration,
            List<OntologyEvent> events,
            int addedFactCount,
            int changedFactCount,
            int evaluatedRuleCount,
            int skippedRuleCount,
            long evaluationElapsedTicks)
        {
            Iteration = iteration;
            Events = events;
            AddedFactCount = addedFactCount;
            ChangedFactCount = changedFactCount;
            EvaluatedRuleCount = evaluatedRuleCount;
            SkippedRuleCount = skippedRuleCount;
            EvaluationElapsedTicks = evaluationElapsedTicks;
        }

        public int Iteration { get; }
        public List<OntologyEvent> Events { get; }
        public int AddedFactCount { get; }
        public int ChangedFactCount { get; }
        public int EvaluatedRuleCount { get; }
        public int SkippedRuleCount { get; }
        public long EvaluationElapsedTicks { get; }
        public double EvaluationElapsedMilliseconds =>
            OntologySimulationResult.ToMilliseconds(EvaluationElapsedTicks);
    }
}
