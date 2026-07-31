using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyWorldService
    {
        // Only facts added by a rule are stored here. Scene, sensor, and action facts remain source facts.
        private readonly HashSet<OntologyFact> inferredFacts = new();

        public OntologyWorldState World { get; private set; }
        public OntologySession Session { get; private set; }
        public OntologySimulationResult LastResult { get; private set; }

        public void Reset(OntologyWorldState world)
        {
            World = world ?? new OntologyWorldState();
            Session = new OntologySession();
            LastResult = null;
            inferredFacts.Clear();
        }

        public void Restore(
            OntologySaveData saveData,
            IReadOnlyList<OntologyRuleDefinition> derivedFactDefinitions = null)
        {
            World = OntologySaveDataConverter.RestoreWorld(
                saveData,
                derivedFactDefinitions);
            Session = OntologySaveDataConverter.RestoreSession(saveData);
            LastResult = null;
            inferredFacts.Clear();
        }

        public OntologySimulationResult Simulate(
            IReadOnlyList<OntologyRuleDefinition> ruleDefinitions,
            OntologyQuestService questService,
            OntologyId actorId,
            int maxIterations,
            IReadOnlyList<OntologyRuleDefinition> derivedFactDefinitions = null,
            bool recordDerivedEventHistory = false)
        {
            EnsureReady();

            // Remove only inferred-origin contributions, then rebuild derived
            // relations from current source facts. A matching durable Authority
            // projection must survive this cleanup even when another active rule
            // can infer the same triple.
            OntologyDerivedFactPolicy.RemoveFrom(
                World,
                derivedFactDefinitions ?? ruleDefinitions);

            // Rebuild derived relations from the current source facts. This makes transient
            // observations (for example, Player occupies WaterRegion) retract their former
            // consequences as soon as the observation is removed.
            ClearInferredFacts();

            var engine = new OntologyRuleEngine();
            if (ruleDefinitions != null)
            {
                foreach (var definition in ruleDefinitions)
                {
                    if (definition != null)
                    {
                        engine.AddRule(OntologyRuleCompiler.Compile(definition), definition);
                    }
                }
            }
            var simulation = new OntologySimulation(maxIterations);

            // The world intentionally rebuilds inferred facts on every simulation so retractions
            // remain correct. Recording those rebuild operations as durable history would make the
            // same rule look like a new player-facing event on every tick. Keep the current trace
            // in LastResult; session history is reserved for authored actions/observations unless
            // an explicit diagnostic caller asks to retain the derived trace.
            LastResult = simulation.RunUntilStable(World, engine, inferredFacts, forceFullInitialEvaluation: true);
            if (recordDerivedEventHistory)
            {
                Session.RecordEvents(LastResult);
            }

            if (questService != null && questService.ApplyCompletionFacts(World, actorId) > 0)
            {
                var followUp = simulation.RunUntilStable(World, engine, inferredFacts);
                LastResult.Append(followUp);
                if (recordDerivedEventHistory)
                {
                    Session.RecordEvents(followUp);
                }
            }

            return LastResult;
        }

        private void EnsureReady()
        {
            World ??= new OntologyWorldState();
            Session ??= new OntologySession();
        }

        private void ClearInferredFacts()
        {
            foreach (var fact in inferredFacts)
            {
                World.RemoveFactContribution(
                    fact.Subject,
                    fact.Predicate,
                    fact.Object,
                    OntologyFactOrigin.Inferred);
            }

            inferredFacts.Clear();
        }
    }
}
