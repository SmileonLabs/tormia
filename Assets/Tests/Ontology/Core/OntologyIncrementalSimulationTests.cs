using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyIncrementalSimulationTests
    {
        [Test]
        public void UnrelatedPredicateChangeDoesNotEvaluateIndexedRule()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "unrelated", "Value");

            var definition = new OntologyRuleDefinition { id = "RequiresWeather" };
            definition.conditions.Add(OntologyCondition.Fact("Weather", "state", "Raining"));
            definition.effects.Add(OntologyEffect.AddFact("Player", "status", "Wet"));

            var engine = new OntologyRuleEngine();
            engine.AddRule(OntologyRuleCompiler.Compile(definition), definition);
            var result = new OntologySimulation().RunUntilStable(world, engine);

            Assert.That(result.ReachedStableState, Is.True);
            Assert.That(result.TotalChangedFacts, Is.Zero);
            Assert.That(result.TotalEvaluatedRules, Is.Zero);
            Assert.That(result.TotalSkippedRules, Is.EqualTo(1));
            Assert.That(result.Steps[0].EvaluationElapsedTicks, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.HasFact("Player", "status", "Wet"), Is.False);
        }

        [Test]
        public void RelatedPredicateChangeActivatesRuleChainAcrossIterations()
        {
            var world = new OntologyWorldState();
            world.AddFact("Weather", "state", "Raining");

            var wet = new OntologyRuleDefinition { id = "BecomeWet" };
            wet.conditions.Add(OntologyCondition.Fact("Weather", "state", "Raining"));
            wet.conditions.Add(OntologyCondition.NotFact("Player", "status", "Wet"));
            wet.effects.Add(OntologyEffect.AddFact("Player", "status", "Wet"));

            var seekShelter = new OntologyRuleDefinition { id = "SeekShelter" };
            seekShelter.conditions.Add(OntologyCondition.Fact("Player", "status", "Wet"));
            seekShelter.effects.Add(OntologyEffect.AddFact("Player", "intent", "SeekShelter"));

            var engine = new OntologyRuleEngine();
            engine.AddRule(OntologyRuleCompiler.Compile(wet), wet);
            engine.AddRule(OntologyRuleCompiler.Compile(seekShelter), seekShelter);

            var result = new OntologySimulation(maxIterations: 4).RunUntilStable(world, engine);

            Assert.That(result.ReachedStableState, Is.True);
            Assert.That(result.TotalEvaluatedRules, Is.EqualTo(3));
            Assert.That(result.TotalSkippedRules, Is.EqualTo(3));
            Assert.That(result.TotalEvaluationElapsedTicks, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.HasFact("Player", "status", "Wet"), Is.True);
            Assert.That(world.HasFact("Player", "intent", "SeekShelter"), Is.True);
        }

        [Test]
        public void ForceFullInitialEvaluationEvaluatesIndexedRuleAfterChangesWereConsumed()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "capability", "CanGlow");
            world.ConsumeChanges();

            var definition = new OntologyRuleDefinition { id = "ApplyGlow" };
            definition.conditions.Add(OntologyCondition.Fact(
                "Player",
                "capability",
                "CanGlow"));
            definition.effects.Add(OntologyEffect.AddFact(
                "Player",
                "presentation",
                "Glowing"));

            var engine = new OntologyRuleEngine();
            engine.AddRule(OntologyRuleCompiler.Compile(definition), definition);

            var result = new OntologySimulation(maxIterations: 4).RunUntilStable(
                world,
                engine,
                inferredFacts: null,
                forceFullInitialEvaluation: true);

            Assert.That(result.ReachedStableState, Is.True);
            Assert.That(result.TotalEvaluatedRules, Is.EqualTo(1));
            Assert.That(result.TotalSkippedRules, Is.EqualTo(1));
            Assert.That(world.HasFact("Player", "presentation", "Glowing"), Is.True);
        }

        [Test]
        public void RemovingRuleSetRetractsItsPreviouslyInferredResult()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "capability", "CanGlow");
            var service = new OntologyWorldService();
            service.Reset(world);

            var glow = CreateCapabilityResultRule(
                "ApplyGlow",
                "CanGlow",
                "Glowing");
            service.Simulate(
                new[] { glow },
                questService: null,
                actorId: "Player",
                maxIterations: 4);
            Assert.That(world.HasFact("Player", "presentation", "Glowing"), Is.True);

            var removedResult = service.Simulate(
                System.Array.Empty<OntologyRuleDefinition>(),
                questService: null,
                actorId: "Player",
                maxIterations: 4);

            Assert.That(removedResult.ReachedStableState, Is.True);
            Assert.That(removedResult.TotalEvaluatedRules, Is.Zero);
            Assert.That(removedResult.TotalSkippedRules, Is.Zero);
            Assert.That(world.HasFact("Player", "presentation", "Glowing"), Is.False);
        }

        [Test]
        public void ReplacingRuleSetRetractsOldResultAndInfersReplacementResult()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "capability", "CanGlow");
            var service = new OntologyWorldService();
            service.Reset(world);

            var oldRule = CreateCapabilityResultRule(
                "ApplyWarmGlow",
                "CanGlow",
                "WarmGlow");
            service.Simulate(
                new[] { oldRule },
                questService: null,
                actorId: "Player",
                maxIterations: 4);

            var replacementRule = CreateCapabilityResultRule(
                "ApplyCoolGlow",
                "CanGlow",
                "CoolGlow");
            var replacementResult = service.Simulate(
                new[] { replacementRule },
                questService: null,
                actorId: "Player",
                maxIterations: 4);

            Assert.That(replacementResult.ReachedStableState, Is.True);
            Assert.That(replacementResult.TotalEvaluatedRules, Is.EqualTo(1));
            Assert.That(replacementResult.TotalSkippedRules, Is.EqualTo(1));
            Assert.That(world.HasFact("Player", "presentation", "WarmGlow"), Is.False);
            Assert.That(world.HasFact("Player", "presentation", "CoolGlow"), Is.True);
        }

        private static OntologyRuleDefinition CreateCapabilityResultRule(
            string ruleId,
            string capability,
            string result)
        {
            var definition = new OntologyRuleDefinition { id = ruleId };
            definition.conditions.Add(OntologyCondition.Fact(
                "Player",
                "capability",
                capability));
            definition.effects.Add(OntologyEffect.AddFact(
                "Player",
                "presentation",
                result));
            return definition;
        }
    }
}
