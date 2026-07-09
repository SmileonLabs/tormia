namespace Tormia.Ontology.Core
{
    public sealed class OntologySimulation
    {
        public OntologySimulation(int maxIterations = 8)
        {
            MaxIterations = maxIterations < 1 ? 1 : maxIterations;
        }

        public int MaxIterations { get; }

        public OntologySimulationResult RunUntilStable(OntologyWorldState world, OntologyRuleEngine engine)
        {
            var result = new OntologySimulationResult();
            if (world == null || engine == null)
            {
                result.ReachedStableState = true;
                return result;
            }

            for (var iteration = 1; iteration <= MaxIterations; iteration++)
            {
                var step = engine.EvaluateStep(world);
                result.Iterations = iteration;
                result.TotalAddedFacts += step.AddedFactCount;
                result.TotalChangedFacts += step.ChangedFactCount;
                result.Steps.Add(new OntologySimulationStep(iteration, step.Events, step.AddedFactCount, step.ChangedFactCount));

                if (step.ChangedFactCount == 0)
                {
                    result.ReachedStableState = true;
                    break;
                }
            }

            return result;
        }
    }
}
