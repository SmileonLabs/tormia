using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyEquipmentSlotConditionTests
    {
        [Test]
        public void DifferentEquipmentSlotsMayCoexist()
        {
            var world = new OntologyWorldState();
            world.AddFact("Tube", OntologyPredicates.HasSlot, "Waist");
            world.AddFact("Tube", OntologyPredicates.EquippedBy, "Player");
            world.AddFact("Sword", OntologyPredicates.HasSlot, "RightHand");

            var matches = OntologyConditionMatcher.Match(
                world,
                new[]
                {
                    OntologyCondition.EquipmentSlotAvailable(
                        "Player",
                        "Sword")
                });

            Assert.That(matches, Has.Count.EqualTo(1));
        }

        [Test]
        public void SameEquipmentSlotCannotBeOccupiedTwice()
        {
            var world = new OntologyWorldState();
            world.AddFact("FirstSword", OntologyPredicates.HasSlot, "RightHand");
            world.AddFact(
                "FirstSword",
                OntologyPredicates.EquippedBy,
                "Player");
            world.AddFact("SecondSword", OntologyPredicates.HasSlot, "RightHand");

            var matches = OntologyConditionMatcher.Match(
                world,
                new[]
                {
                    OntologyCondition.EquipmentSlotAvailable(
                        "Player",
                        "SecondSword")
                });

            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void MissingOrAmbiguousTargetSlotCannotEquip()
        {
            var world = new OntologyWorldState();
            world.AddFact("Item", OntologyPredicates.HasSlot, "Waist");
            world.AddFact("Item", OntologyPredicates.HasSlot, "RightHand");

            var matches = OntologyConditionMatcher.Match(
                world,
                new[]
                {
                    OntologyCondition.EquipmentSlotAvailable(
                        "Player",
                        "Item")
                });

            Assert.That(matches, Is.Empty);
        }
    }
}
