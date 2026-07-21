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
            Assert.That(world.HasFact("Player", "status", "Wet"), Is.True);
            Assert.That(world.HasFact("Player", "intent", "SeekShelter"), Is.True);
        }
    }
}
