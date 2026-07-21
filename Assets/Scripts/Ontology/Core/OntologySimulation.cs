using System.Collections.Generic;

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
            return RunUntilStable(world, engine, null);
        }

        /// <summary>
        /// Runs inference until stable and optionally records relations newly created by rules.
        /// </summary>
        public OntologySimulationResult RunUntilStable(
            OntologyWorldState world,
            OntologyRuleEngine engine,
            ISet<OntologyFact> inferredFacts)
        {
            return RunUntilStable(world, engine, inferredFacts, false);
        }

        /// <summary>
        /// Runs inference until stable. A full initial evaluation is required after a caller
        /// retracts derived facts, because those retractions do not change the rule inputs.
        /// </summary>
        public OntologySimulationResult RunUntilStable(
            OntologyWorldState world,
            OntologyRuleEngine engine,
            ISet<OntologyFact> inferredFacts,
            bool forceFullInitialEvaluation)
        {
            var result = new OntologySimulationResult();
            if (world == null || engine == null)
            {
                result.ReachedStableState = true;
                return result;
            }

            var changes = forceFullInitialEvaluation ? null : world.ConsumeChanges();
            for (var iteration = 1; iteration <= MaxIterations; iteration++)
            {
                var step = engine.EvaluateStep(world, changes, inferredFacts);
                result.Iterations = iteration;
                result.TotalAddedFacts += step.AddedFactCount;
                result.TotalChangedFacts += step.ChangedFactCount;
                result.Steps.Add(new OntologySimulationStep(iteration, step.Events, step.AddedFactCount, step.ChangedFactCount));

                if (step.ChangedFactCount == 0)
                {
                    result.ReachedStableState = true;
                    break;
                }

                if (iteration < MaxIterations)
                {
                    changes = world.ConsumeChanges();
                }
            }

            return result;
        }
    }
}
