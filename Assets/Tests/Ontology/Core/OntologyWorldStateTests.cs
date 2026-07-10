using NUnit.Framework;
using Tormia.Ontology.Core;

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
    }
}
