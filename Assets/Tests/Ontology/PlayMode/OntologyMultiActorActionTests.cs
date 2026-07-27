using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyMultiActorActionTests
    {
        [UnityTest]
        public IEnumerator NpcUsesTheSameActorScopedActionCandidatesAsThePlayer()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            bootstrap.ResetWorld(logReport: false);
            bootstrap.World.AddConcept("Villager_01", OntologyConcepts.Actor);
            bootstrap.World.AddConcept("Villager_01", "NPC");
            bootstrap.World.AddFact("Villager_01", "has_skill", "Help");
            bootstrap.World.AddFact("Villager_01", "has_skill", "Talk");
            bootstrap.World.AddConcept("Well_01", "Creature");
            bootstrap.World.AddFact("Well_01", "state", "Disturbed");

            var candidateDefinition = new OntologyActionCandidateDefinition
            {
                actionVerb = "help",
                targetPattern = "?target",
                conditions =
                {
                    OntologyCondition.Fact("?actor", "has_skill", "Help"),
                    OntologyCondition.Fact("?target", "state", "Disturbed")
                }
            };
            var effectDefinition = new OntologyActionEffectDefinition
            {
                actionVerb = "help",
                subjectPattern = "?actor",
                predicate = "helps",
                objectPattern = "?target"
            };
            var actions = new OntologyActionService(
                new[] { candidateDefinition },
                new[] { effectDefinition });

            var candidates = actions.GetCandidates(
                bootstrap.World,
                "Villager_01");
            Assert.That(candidates.Exists(candidate =>
                candidate.Action.ActorId.Value == "Villager_01" &&
                candidate.Action.Verb.Value == "help" &&
                candidate.Action.TargetId.Value == "Well_01"), Is.True);

            var applied = actions.Apply(
                bootstrap.World,
                bootstrap.Session,
                new OntologyAction("Villager_01", "help", "Well_01"));
            Assert.That(applied, Is.True);
            Assert.That(bootstrap.World.HasFact("Villager_01", "helps", "Well_01"), Is.True);

            Object.Destroy(bootstrapObject);
            yield return null;
        }
    }
}
