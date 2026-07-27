using NUnit.Framework;
using Tormia.Ontology.Core;
using System.Collections.Generic;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldStateTests
    {
        [Test]
        public void ConceptFactsAndEntityConceptsStayInSync()
        {
            var world = new OntologyWorldState();

            Assert.That(world.AddFact("Tree", OntologyPredicates.HasConcept, "Plant"), Is.True);
            Assert.That(world.HasConcept("Tree", "Plant"), Is.True);

            Assert.That(world.RemoveFact("Tree", OntologyPredicates.HasConcept, "Plant"), Is.True);
            Assert.That(world.HasConcept("Tree", "Plant"), Is.False);
        }

        [Test]
        public void SetFactReplacesOtherValuesAndTreatsSameValueAsNoOp()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "status", "Cold");
            world.AddFact("Player", "status", "Wet");

            Assert.That(world.SetFact("Player", "status", "Warm", out var added), Is.True);
            Assert.That(added, Is.True);
            Assert.That(world.HasFact("Player", "status", "Cold"), Is.False);
            Assert.That(world.HasFact("Player", "status", "Wet"), Is.False);
            Assert.That(world.HasFact("Player", "status", "Warm"), Is.True);

            Assert.That(world.SetFact("Player", "status", "Warm", out added), Is.False);
            Assert.That(added, Is.False);
        }

        [Test]
        public void RuntimeObservationRetractsOnlyFactsPublishedByThatSensor()
        {
            var world = new OntologyWorldState();
            var observed = new HashSet<string> { "WaterA", "WaterB" };
            var published = new HashSet<string>();
            var removalBuffer = new List<string>();

            // WaterA is a durable/authored relationship. A runtime sensor must
            // not claim ownership of it just because it observes the same fact.
            world.AddFact("Raft", OntologyPredicates.Occupies, "WaterA");

            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSet(
                    world,
                    "Raft",
                    OntologyPredicates.Occupies,
                    observed,
                    published,
                    removalBuffer),
                Is.True);
            Assert.That(published, Is.EquivalentTo(new[] { "WaterB" }));

            observed.Clear();
            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSet(
                    world,
                    "Raft",
                    OntologyPredicates.Occupies,
                    observed,
                    published,
                    removalBuffer),
                Is.True);
            Assert.That(
                world.HasFact("Raft", OntologyPredicates.Occupies, "WaterA"),
                Is.True,
                "An authored fact must survive a runtime observation refresh.");
            Assert.That(
                world.HasFact("Raft", OntologyPredicates.Occupies, "WaterB"),
                Is.False);
        }

        [Test]
        public void RuntimeObservationSingleValueDoesNotRetractAnExistingFact()
        {
            var world = new OntologyWorldState();
            var published = string.Empty;
            world.AddFact("Player", OntologyPredicates.ImmersionDepth, "Deep");

            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                    world,
                    "Player",
                    OntologyPredicates.ImmersionDepth,
                    "Deep",
                    ref published),
                Is.False);
            Assert.That(published, Is.Empty);

            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                    world,
                    "Player",
                    OntologyPredicates.ImmersionDepth,
                    string.Empty,
                    ref published),
                Is.False);
            Assert.That(
                world.HasFact("Player", OntologyPredicates.ImmersionDepth, "Deep"),
                Is.True);
        }

        [Test]
        public void RemovingOneOriginPreservesTheSameFactFromAnotherOrigin()
        {
            var world = new OntologyWorldState();
            world.AddFact("Avatar", "has_skill", "Talk");
            Assert.That(
                world.AddFactContribution(
                    "Avatar",
                    "has_skill",
                    "Talk",
                    OntologyFactOrigin.AccountProfile),
                Is.True);

            Assert.That(
                world.RemoveFactContribution(
                    "Avatar",
                    "has_skill",
                    "Talk",
                    OntologyFactOrigin.AccountProfile),
                Is.True);
            Assert.That(world.HasFact("Avatar", "has_skill", "Talk"), Is.True);
            Assert.That(
                world.IsPersistentFact(
                    new OntologyFact("Avatar", "has_skill", "Talk")),
                Is.True);
        }

        [Test]
        public void RuntimeObservationRetractionPreservesDurableContributionAddedLater()
        {
            var world = new OntologyWorldState();
            var published = string.Empty;

            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                    world,
                    "Player",
                    "standing_on",
                    "Tile_1_1",
                    ref published),
                Is.True);
            world.AddFact("Player", "standing_on", "Tile_1_1");

            Assert.That(
                OntologyRuntimeObservationFacts.RemovePublishedSingleValue(
                    world,
                    "Player",
                    "standing_on",
                    ref published),
                Is.True);
            Assert.That(world.HasFact("Player", "standing_on", "Tile_1_1"), Is.True);
            Assert.That(
                world.IsPersistentFact(
                    new OntologyFact("Player", "standing_on", "Tile_1_1")),
                Is.True);
        }

        [Test]
        public void RuntimeObservationReassertsItsValueAfterWorldRebuild()
        {
            var firstWorld = new OntologyWorldState();
            var published = string.Empty;
            OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                firstWorld,
                "Simulation",
                "current_tick",
                "Tick_1",
                ref published);

            var rebuiltWorld = new OntologyWorldState();
            Assert.That(
                OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                    rebuiltWorld,
                    "Simulation",
                    "current_tick",
                    "Tick_1",
                    ref published),
                Is.True);
            Assert.That(
                rebuiltWorld.HasFact("Simulation", "current_tick", "Tick_1"),
                Is.True);
            Assert.That(
                rebuiltWorld.IsPersistentFact(
                    new OntologyFact("Simulation", "current_tick", "Tick_1")),
                Is.False);
        }
    }
}
