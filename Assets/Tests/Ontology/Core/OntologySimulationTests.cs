using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologySimulationTests
    {
        [Test]
        public void UnguardedSetFactRuleStillReachesStableState()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Player", "Actor");

            var definition = new OntologyRuleDefinition { id = "KeepWarm" };
            definition.conditions.Add(OntologyCondition.HasConcept("?actor", "Actor"));
            definition.effects.Add(OntologyEffect.SetFact("?actor", "status", "Warm"));

            var engine = new OntologyRuleEngine();
            engine.AddRule(OntologyRuleCompiler.Compile(definition));
            var result = new OntologySimulation(maxIterations: 4).RunUntilStable(world, engine);

            Assert.That(result.ReachedStableState, Is.True);
            Assert.That(result.Iterations, Is.EqualTo(2));
            Assert.That(world.HasFact("Player", "status", "Warm"), Is.True);
        }

        [Test]
        public void SaveRoundTripPreservesFactsAndHistory()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Tree", "Plant");
            world.AddFact("Player", "inspects", "Tree");
            var session = new OntologySession();
            session.RecordAction(new OntologyAction("Player", "inspect", "Tree"));

            var saveData = OntologySaveDataConverter.Capture(world, session);
            var restoredWorld = OntologySaveDataConverter.RestoreWorld(saveData);
            var restoredSession = OntologySaveDataConverter.RestoreSession(saveData);

            Assert.That(restoredWorld.HasConcept("Tree", "Plant"), Is.True);
            Assert.That(restoredWorld.HasFact("Player", "inspects", "Tree"), Is.True);
            Assert.That(restoredSession.ActionHistory, Has.Count.EqualTo(1));
            Assert.That(restoredSession.ActionHistory[0], Is.EqualTo(new OntologyAction("Player", "inspect", "Tree")));
        }

        [Test]
        public void EquipmentQuestTargetsPartThatGrantsRequiredCapability()
        {
            var world = new OntologyWorldState();
            world.AddFact("QuestBoard", "offers", "ColdProtectionPreparation");
            world.AddFact("Part_Outerwear_Base", OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection);

            var quests = new OntologyQuestGenerator().Generate(world, "Player");

            Assert.That(quests, Has.Count.EqualTo(1));
            Assert.That(quests[0].Id, Is.EqualTo(new OntologyId("ColdProtectionPreparation")));
            Assert.That(quests[0].Goals, Has.Count.EqualTo(1));
            Assert.That(quests[0].Goals[0].RecommendedAction, Is.EqualTo(new OntologyAction("Player", "equip_part", "Part_Outerwear_Base")));
        }

        [Test]
        public void DefaultRulesAndQuestsPassValidation()
        {
            Assert.That(OntologyRuleValidator.Validate(OntologyDefaultRules.CreateDefaultDefinitions()), Is.Empty);
            Assert.That(OntologyQuestValidator.Validate(new OntologyQuestGenerator().Definitions), Is.Empty);
        }
    }
}
