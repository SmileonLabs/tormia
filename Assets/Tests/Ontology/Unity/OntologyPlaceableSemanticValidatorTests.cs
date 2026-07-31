using System.IO;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyPlaceableSemanticValidatorTests
    {
        [Test]
        public void CompleteInflatableDefinitionHasNoSemanticWarnings()
        {
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            var physical = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            var attachment = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                template.concepts = new[]
                {
                    OntologyConcepts.CharacterPart,
                    OntologyConcepts.FloatableObject,
                    OntologyConcepts.Wearable,
                    OntologyConcepts.FlotationDevice
                };
                template.facts = new[]
                {
                    Fact(OntologyPredicates.HasSlot, "Waist"),
                    Fact(OntologyPredicates.GrantsSkill, "Swimming"),
                    Fact(OntologyPredicates.PickupBehavior, OntologyObjects.SelectThenEquip),
                    Fact(OntologyPredicates.AttachmentProfile, "WaistInflatableRing"),
                    Fact(OntologyPredicates.PhysicalProfile, "LightBuoyant")
                };
                physical.profileId = "LightBuoyant";
                physical.supportsBuoyancy = true;
                physical.buoyancyRuleId = "BuoyantWhenInWater";
                attachment.profileId = "WaistInflatableRing";
                attachment.kind = OntologyAttachmentKind.Wearable;
                attachment.slotId = "Waist";
                attachment.relationPredicate = OntologyPredicates.EquippedBy;
                attachment.relationDirection =
                    OntologyAttachmentRelationDirection.ItemToActor;
                attachment.actorAnchorBone = HumanBodyBones.Hips;

                var definition = new OntologyPlaceableDefinition
                {
                    definitionId = "Inflatable_Rings",
                    ontologyTemplate = template,
                    physicalProfile = physical,
                    attachmentProfile = attachment
                };
                definition.defaultRuleBlocks.Add(new OntologyRuleBlockBinding
                {
                    ruleId = "BuoyantWhenInWater",
                    bindingVariable = "?object"
                });

                Assert.That(OntologyPlaceableSemanticValidator.Validate(definition), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(physical);
                Object.DestroyImmediate(attachment);
            }
        }

        [Test]
        public void CarryableProfileIsValidatedFromOntologyData()
        {
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            var attachment = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                template.concepts = new[] { OntologyConcepts.Carryable };
                template.facts = new[]
                {
                    Fact(OntologyPredicates.AttachmentProfile, "BackCarry"),
                    Fact(OntologyPredicates.HasSlot, "Back"),
                    Fact(
                        OntologyPredicates.PickupBehavior,
                        OntologyObjects.SelectThenCarry)
                };
                attachment.profileId = "BackCarry";
                attachment.kind = OntologyAttachmentKind.Carryable;
                attachment.slotId = "Back";
                attachment.relationPredicate = OntologyPredicates.CarriedBy;
                attachment.relationDirection =
                    OntologyAttachmentRelationDirection.ItemToActor;

                var definition = new OntologyPlaceableDefinition
                {
                    definitionId = "AnyCarryableObject",
                    ontologyTemplate = template,
                    attachmentProfile = attachment
                };

                Assert.That(
                    OntologyPlaceableSemanticValidator.Validate(definition),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(attachment);
            }
        }

        [Test]
        public void BuoyantTreeDoesNotRequireWearableData()
        {
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            var physical = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            try
            {
                template.concepts = new[] { "Plant", OntologyConcepts.FloatableObject };
                template.facts = new[]
                {
                    Fact(OntologyPredicates.PhysicalProfile, "WoodMedium")
                };
                physical.profileId = "WoodMedium";
                physical.supportsBuoyancy = true;
                physical.buoyancyRuleId = "BuoyantWhenInWater";

                var definition = new OntologyPlaceableDefinition
                {
                    definitionId = "Tree",
                    ontologyTemplate = template,
                    physicalProfile = physical
                };
                definition.defaultRuleBlocks.Add(new OntologyRuleBlockBinding
                {
                    ruleId = "BuoyantWhenInWater",
                    bindingVariable = "?object"
                });

                Assert.That(OntologyPlaceableSemanticValidator.Validate(definition), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(physical);
            }
        }

        [Test]
        public void WearableProfileExplainsMissingOntologyPrerequisites()
        {
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            var attachment = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                attachment.profileId = "WaistItem";
                attachment.kind = OntologyAttachmentKind.Wearable;
                attachment.slotId = "Waist";
                attachment.relationPredicate = OntologyPredicates.EquippedBy;

                var definition = new OntologyPlaceableDefinition
                {
                    definitionId = "UnspecifiedItem",
                    ontologyTemplate = template,
                    attachmentProfile = attachment
                };

                var warnings = OntologyPlaceableSemanticValidator.Validate(definition);
                Assert.That(warnings, Has.Some.Contains("Wearable Concept"));
                Assert.That(warnings, Has.Some.Contains("has_slot"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(attachment);
            }
        }

        [Test]
        public void WeaponProfileRequiresAuthoredGripPoint()
        {
            var template =
                ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            var attachment =
                ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            var prefab = new GameObject("ProjectOwnedWeapon");
            try
            {
                template.concepts = new[]
                {
                    OntologyConcepts.Weapon,
                    OntologyConcepts.Carryable
                };
                template.facts = new[]
                {
                    Fact(
                        OntologyPredicates.AttachmentProfile,
                        "RightHandCarry"),
                    Fact(OntologyPredicates.HasSlot, "RightHand"),
                    Fact(
                        OntologyPredicates.PickupBehavior,
                        OntologyObjects.SelectThenCarry)
                };
                attachment.profileId = "RightHandCarry";
                attachment.kind = OntologyAttachmentKind.Carryable;
                attachment.slotId = "RightHand";
                attachment.relationPredicate =
                    OntologyPredicates.EquippedItem;
                attachment.relationDirection =
                    OntologyAttachmentRelationDirection.ActorToItem;
                attachment.requireItemGripPoint = true;
                var definition = new OntologyPlaceableDefinition
                {
                    definitionId = "WeaponWithoutGrip",
                    prefab = prefab,
                    ontologyTemplate = template,
                    attachmentProfile = attachment
                };

                Assert.That(
                    OntologyPlaceableSemanticValidator.Validate(definition),
                    Has.Some.Contains("OntologyAttachmentGripPoint"));

                var gripPoint = new GameObject("Grip")
                    .AddComponent<OntologyAttachmentGripPoint>();
                gripPoint.transform.SetParent(prefab.transform, false);
                Assert.That(
                    OntologyPlaceableSemanticValidator.Validate(definition),
                    Has.Some.Contains("calibrated"));
                gripPoint.MarkCalibrated();
                Assert.That(
                    OntologyPlaceableSemanticValidator.Validate(definition),
                    Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(attachment);
            }
        }

        [Test]
        public void EntrySemanticRepairUsesRevisionRetryTransport()
        {
            const string bridgePath =
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityBridge.cs";
            var source = File.ReadAllText(bridgePath);
            var methodStart = source.IndexOf(
                "private IEnumerator PublishDefaultMeaningPackage",
                System.StringComparison.Ordinal);
            var methodEnd = source.IndexOf(
                "private static OntologyRuleBlockBinding[]",
                methodStart,
                System.StringComparison.Ordinal);
            var methodSource = source.Substring(
                methodStart,
                methodEnd - methodStart);

            StringAssert.Contains(
                "SendCommandWithRevisionRetryRoutine",
                methodSource);
            StringAssert.DoesNotContain(
                "SendCommandRoutine(",
                methodSource);
        }

        private static OntologyFactEntry Fact(string predicate, string obj)
        {
            return new OntologyFactEntry { predicate = predicate, obj = obj };
        }
    }
}
