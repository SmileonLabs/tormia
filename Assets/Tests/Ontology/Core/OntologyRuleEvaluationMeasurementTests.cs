using System;
using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    /// <summary>
    /// Opt-in deterministic scale measurement for the ontology rule core. It does
    /// not set a machine-dependent time threshold; the measured counters and
    /// elapsed time are emitted as evidence for comparing later implementations.
    /// </summary>
    public sealed class OntologyRuleEvaluationMeasurementTests
    {
        private const int DefaultEntityCount = 1000;
        private const int DefaultRuleCount = 100;
        private const int DefaultFactsPerEntity = 100;

        [Test]
        [Explicit("Scale measurement; run explicitly when collecting ontology performance evidence.")]
        public void MeasureIndexedRulesAtRepresentativeScale()
        {
            var entityCount = ReadScale("TOV_ONTOLOGY_MEASURE_ENTITIES", DefaultEntityCount);
            var ruleCount = ReadScale("TOV_ONTOLOGY_MEASURE_RULES", DefaultRuleCount);
            var factsPerEntity = ReadScale(
                "TOV_ONTOLOGY_MEASURE_FACTS_PER_ENTITY",
                DefaultFactsPerEntity);

            var world = new OntologyWorldState();
            for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
            {
                var entityId = "MeasureEntity_" + entityIndex;
                world.GetOrCreateEntity(entityId);
                for (var factIndex = 0; factIndex < factsPerEntity; factIndex++)
                {
                    world.AddFact(
                        entityId,
                        "measure_noise_" + factIndex,
                        "Value_" + entityIndex + "_" + factIndex);
                }
            }

            var engine = new OntologyRuleEngine();
            for (var ruleIndex = 0; ruleIndex < ruleCount; ruleIndex++)
            {
                var entityId = "MeasureEntity_" + (ruleIndex % entityCount);
                var predicate = "measure_signal_" + ruleIndex;
                world.AddFact(entityId, predicate, "true");

                var definition = new OntologyRuleDefinition
                {
                    id = "MeasureRule_" + ruleIndex
                };
                definition.conditions.Add(OntologyCondition.Fact(
                    entityId,
                    predicate,
                    "true"));
                definition.effects.Add(OntologyEffect.AddFact(
                    entityId,
                    "measure_result",
                    "Result_" + ruleIndex));
                engine.AddRule(OntologyRuleCompiler.Compile(definition), definition);
            }

            var result = new OntologySimulation(maxIterations: 4)
                .RunUntilStable(world, engine);

            Assert.That(result.ReachedStableState, Is.True);
            Assert.That(result.TotalAddedFacts, Is.EqualTo(ruleCount));
            Assert.That(result.TotalEvaluatedRules, Is.EqualTo(ruleCount));
            Assert.That(result.TotalSkippedRules, Is.EqualTo(ruleCount));
            Assert.That(result.TotalEvaluationElapsedTicks, Is.GreaterThanOrEqualTo(0));
            TestContext.Out.WriteLine(
                "entities={0} authoredFacts={1} rules={2} iterations={3} " +
                "evaluatedRules={4} skippedRules={5} evaluationMs={6:F3}",
                entityCount,
                entityCount * factsPerEntity + ruleCount,
                ruleCount,
                result.Iterations,
                result.TotalEvaluatedRules,
                result.TotalSkippedRules,
                result.TotalEvaluationElapsedMilliseconds);
        }

        private static int ReadScale(string variable, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(variable);
            return int.TryParse(raw, out var value) && value > 0
                ? value
                : fallback;
        }
    }
}
