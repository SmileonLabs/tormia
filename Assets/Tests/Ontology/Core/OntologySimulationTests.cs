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
        public void SaveAndLegacyRestoreExcludeRetractableRuleResults()
        {
            var buoyancy = CreateDerivedBuoyancyRule();
            var world = new OntologyWorldState();
            world.AddFactContribution(
                "Tube",
                OntologyPredicates.Occupies,
                "Water",
                OntologyFactOrigin.RuntimeObservation);
            world.AddFact(
                "Tube",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating);

            var saveData = OntologySaveDataConverter.Capture(
                world,
                new OntologySession(),
                new[] { buoyancy });
            Assert.That(
                saveData.facts,
                Has.None.Matches<OntologyFactRecord>(value =>
                    value.subject == "Tube" &&
                    value.predicate == OntologyPredicates.Occupies &&
                    value.obj == "Water"));
            Assert.That(
                saveData.facts,
                Has.None.Matches<OntologyFactRecord>(value =>
                    value.predicate == OntologyPredicates.PhysicalState &&
                    value.obj == OntologyObjects.Floating));

            // Simulate a version written before derived facts were filtered.
            saveData.facts.Add(new OntologyFactRecord
            {
                subject = "Tube",
                predicate = OntologyPredicates.PhysicalState,
                obj = OntologyObjects.Floating
            });
            var restored = OntologySaveDataConverter.RestoreWorld(
                saveData,
                new[] { buoyancy });
            Assert.That(
                restored.HasFact(
                    "Tube",
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating),
                Is.False);
            Assert.That(
                restored.HasFact(
                    "Tube",
                    OntologyPredicates.Occupies,
                    "Water"),
                Is.False);
        }

        [Test]
        public void RemovingRuleBlockRetractsInferredFloatingFact()
        {
            var buoyancy = CreateDerivedBuoyancyRule();
            var world = new OntologyWorldState();
            world.AddFact("Tube", OntologyPredicates.Occupies, "Water");
            world.AddFactContribution(
                "Tube",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating,
                OntologyFactOrigin.Inferred);
            var service = new OntologyWorldService();
            service.Reset(world);

            // Legacy durable derived facts are filtered by RestoreWorld above.
            // During a live rebuild, only the inferred contribution is retracted;
            // a matching durable Authority projection must remain owned by Authority.
            service.Simulate(
                new OntologyRuleDefinition[0],
                null,
                "Player",
                8,
                new[] { buoyancy });

            Assert.That(
                world.HasFact(
                    "Tube",
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating),
                Is.False);
            Assert.That(
                world.HasFact(
                    "Tube",
                    OntologyPredicates.Occupies,
                    "Water"),
                Is.True);
        }

        [Test]
        public void EquipmentQuestTargetsPartThatGrantsRequiredCapability()
        {
            var world = new OntologyWorldState();
            world.AddFact("QuestBoard", "offers", "ColdProtectionPreparation");
            world.AddFact("Part_Outerwear_Base", OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection);

            var quests = new OntologyQuestGenerator(
                OntologyQuestGenerator.CreateDefaultDefinitions()).Generate(world, "Player");

            Assert.That(quests, Has.Count.EqualTo(1));
            Assert.That(quests[0].Id, Is.EqualTo(new OntologyId("ColdProtectionPreparation")));
            Assert.That(quests[0].Goals, Has.Count.EqualTo(1));
            Assert.That(quests[0].Goals[0].RecommendedAction, Is.EqualTo(new OntologyAction("Player", "equip_part", "Part_Outerwear_Base")));
        }

        [Test]
        public void DefaultRulesAndQuestsPassValidation()
        {
            Assert.That(OntologyRuleValidator.Validate(OntologyDefaultRules.CreateDefaultDefinitions()), Is.Empty);
            Assert.That(
                OntologyQuestValidator.Validate(
                    OntologyQuestGenerator.CreateDefaultDefinitions()),
                Is.Empty);
        }

        [Test]
        public void EmptyActionCatalogDoesNotRestoreHiddenDefaults()
        {
            var world = new OntologyWorldState();
            world.AddFact("Actor", "has_skill", "Talk");
            world.AddConcept("Target", "Creature");
            var service = new OntologyActionService(
                System.Array.Empty<OntologyActionCandidateDefinition>(),
                System.Array.Empty<OntologyActionEffectDefinition>());

            Assert.That(service.GetCandidates(world, "Actor"), Is.Empty);
            Assert.That(
                service.Apply(
                    world,
                    new OntologySession(),
                    new OntologyAction("Actor", "invented_action", "Target")),
                Is.False);
            Assert.That(
                world.HasFact("Actor", "invented_action", "Target"),
                Is.False);
        }

        private static OntologyRuleDefinition CreateDerivedBuoyancyRule()
        {
            var definition = new OntologyRuleDefinition
            {
                id = "BuoyantWhenInWater"
            };
            definition.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.Occupies,
                "?water"));
            definition.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));
            return definition;
        }

        [Test]
        public void RecomputeRetractsSwimmingWhenWaterOccupancyEnds()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Player", "Actor");
            world.AddConcept("BeachWater_01", "WaterRegion");
            world.AddFact("BeachWater_01", "water_depth", "Deep");

            var swimming = new OntologyRuleDefinition { id = "DeepWaterOccupancySwimming" };
            swimming.conditions.Add(OntologyCondition.Fact("?actor", "occupies", "?region"));
            swimming.conditions.Add(OntologyCondition.HasConcept("?region", "WaterRegion"));
            swimming.conditions.Add(OntologyCondition.Fact("?region", "water_depth", "Deep"));
            swimming.effects.Add(OntologyEffect.AddFact("?actor", "movement_mode", "Swimming"));

            var service = new OntologyWorldService();
            service.Reset(world);
            world.AddFact("Player", "occupies", "BeachWater_01");
            service.Simulate(new[] { swimming }, null, "Player", 8);
            Assert.That(world.HasFact("Player", "movement_mode", "Swimming"), Is.True);

            // A second simulation has no changed input fact. The inferred relation must still
            // be reconstructed after the service retracts its previous derived result.
            service.Simulate(new[] { swimming }, null, "Player", 8);
            Assert.That(world.HasFact("Player", "movement_mode", "Swimming"), Is.True);

            world.RemoveFact("Player", "occupies", "BeachWater_01");
            service.Simulate(new[] { swimming }, null, "Player", 8);
            Assert.That(world.HasFact("Player", "movement_mode", "Swimming"), Is.False);
        }

        [Test]
        public void DeepWaterWithoutSwimmingRuleBlockInfersDrowning()
        {
            var world = new OntologyWorldState();
            world.AddConcept("BeachWater_01", "WaterRegion");
            world.AddFact("Player", "occupies", "BeachWater_01");
            world.AddFact("Player", "immersion_depth", "Deep");

            var drowning = CreateDeepWaterDrowningRule();
            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { drowning }, null, "Player", 8);

            Assert.That(world.HasFact("Player", "movement_mode", "Drowning"), Is.True);

            world.RemoveFact("Player", "occupies", "BeachWater_01");
            world.RemoveFact("Player", "immersion_depth", "Deep");
            service.Simulate(new[] { drowning }, null, "Player", 8);

            Assert.That(world.HasFact("Player", "movement_mode", "Drowning"), Is.False);
        }

        [Test]
        public void SwimmingRuleBlockPreventsDrowningAndAllowsBoundSwimmingRule()
        {
            var world = new OntologyWorldState();
            world.AddConcept("BeachWater_01", "WaterRegion");
            world.AddFact("BeachWater_01", "has_rule_block", "DeepWaterOccupancySwimming");
            world.AddFact("Player", "occupies", "BeachWater_01");
            world.AddFact("Player", "immersion_depth", "Deep");

            var swimming = new OntologyRuleDefinition { id = "DeepWaterOccupancySwimming@BeachWater_01" };
            swimming.conditions.Add(OntologyCondition.Fact("?actor", "occupies", "BeachWater_01"));
            swimming.conditions.Add(OntologyCondition.Fact("?actor", "immersion_depth", "Deep"));
            swimming.conditions.Add(OntologyCondition.HasConcept("BeachWater_01", "WaterRegion"));
            swimming.effects.Add(OntologyEffect.AddFact("?actor", "movement_mode", "Swimming"));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { swimming, CreateDeepWaterDrowningRule() }, null, "Player", 8);

            Assert.That(world.HasFact("Player", "movement_mode", "Swimming"), Is.True);
            Assert.That(world.HasFact("Player", "movement_mode", "Drowning"), Is.False);
        }

        [Test]
        public void BuoyantObjectFloatsOnlyWhileItOccupiesWater()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Driftwood", OntologyConcepts.FloatableObject);
            world.AddConcept("Lake", OntologyConcepts.WaterRegion);
            world.AddConcept("WoodMedium", OntologyConcepts.PhysicalProfile);
            world.AddFact("Driftwood", OntologyPredicates.Occupies, "Lake");
            world.AddFact("Driftwood", OntologyPredicates.PhysicalProfile, "WoodMedium");
            world.AddFact(
                "WoodMedium",
                OntologyPredicates.SupportsBehavior,
                OntologyObjects.Buoyancy);

            var buoyancy = new OntologyRuleDefinition { id = "BuoyantWhenInWater" };
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.Occupies,
                "?water"));
            buoyancy.conditions.Add(OntologyCondition.HasConcept(
                "?object",
                OntologyConcepts.FloatableObject));
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PhysicalProfile,
                "?profile"));
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?profile",
                OntologyPredicates.SupportsBehavior,
                OntologyObjects.Buoyancy));
            buoyancy.conditions.Add(OntologyCondition.HasConcept(
                "?water",
                OntologyConcepts.WaterRegion));
            buoyancy.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { buoyancy }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "Driftwood",
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating),
                Is.True);

            world.RemoveFact("Driftwood", OntologyPredicates.Occupies, "Lake");
            service.Simulate(new[] { buoyancy }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "Driftwood",
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating),
                Is.False);
        }

        [Test]
        public void PhysicalProfileWithoutBuoyancyBehaviorDoesNotInferFloating()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Stone", OntologyConcepts.FloatableObject);
            world.AddConcept("Lake", OntologyConcepts.WaterRegion);
            world.AddFact("Stone", OntologyPredicates.Occupies, "Lake");
            world.AddFact(
                "Stone",
                OntologyPredicates.PhysicalProfile,
                "HeavySinking");

            var buoyancy = new OntologyRuleDefinition { id = "BuoyantWhenInWater" };
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.Occupies,
                "?water"));
            buoyancy.conditions.Add(OntologyCondition.HasConcept(
                "?object",
                OntologyConcepts.FloatableObject));
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PhysicalProfile,
                "?profile"));
            buoyancy.conditions.Add(OntologyCondition.Fact(
                "?profile",
                OntologyPredicates.SupportsBehavior,
                OntologyObjects.Buoyancy));
            buoyancy.conditions.Add(OntologyCondition.HasConcept(
                "?water",
                OntologyConcepts.WaterRegion));
            buoyancy.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { buoyancy }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "Stone",
                    OntologyPredicates.PhysicalState,
                    OntologyObjects.Floating),
                Is.False);
        }

        [Test]
        public void SupportedByRelationInfersRestingOn()
        {
            var world = new OntologyWorldState();
            world.AddFact(
                "Crate",
                OntologyPredicates.SupportedBy,
                "Platform");
            var rule = new OntologyRuleDefinition
            {
                id = "SupportedObjectRestsOnSupport"
            };
            rule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.SupportedBy,
                "?support"));
            rule.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.RestingOn,
                "?support"));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { rule }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "Crate",
                    OntologyPredicates.RestingOn,
                    "Platform"),
                Is.True);
        }

        [Test]
        public void IntendedNearbyWearableEquipsWhileInteractionIntentRemainsActive()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Player", "Actor");
            world.AddConcept("InflatableRing", OntologyConcepts.Wearable);
            world.AddFact(
                "InflatableRing",
                OntologyPredicates.PickupBehavior,
                OntologyObjects.SelectThenEquip);
            world.AddFact(
                "InflatableRing",
                OntologyPredicates.AttachmentProfile,
                "WaistInflatableRing");
            world.AddFact(
                "Player",
                OntologyPredicates.Near,
                "InflatableRing");
            world.AddFact(
                "Player",
                OntologyPredicates.InteractionIntent,
                "InflatableRing");

            var autoEquip = new OntologyRuleDefinition { id = "AutoEquipNearbyWearable" };
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.Near,
                "?object"));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.InteractionIntent,
                "?object"));
            autoEquip.conditions.Add(OntologyCondition.HasConcept("?actor", "Actor"));
            autoEquip.conditions.Add(OntologyCondition.HasConcept(
                "?object",
                OntologyConcepts.Wearable));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PickupBehavior,
                OntologyObjects.SelectThenEquip));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.AttachmentProfile,
                "?profile"));
            autoEquip.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.EquippedBy,
                "?actor"));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(new[] { autoEquip }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "InflatableRing",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.InteractionIntent,
                    "InflatableRing"),
                Is.True);

            world.RemoveFact("Player", OntologyPredicates.Near, "InflatableRing");
            service.Simulate(new[] { autoEquip }, null, "Player", 8);

            Assert.That(
                world.HasFact(
                    "InflatableRing",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.False);
        }

        [Test]
        public void EquipmentSlotKeepsOneWearableAndAllowsReplacementAfterUnequip()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Player", "Actor");
            AddWearable(world, "TubeA", "Waist");
            AddWearable(world, "TubeB", "Waist");
            world.AddFact("Player", OntologyPredicates.Near, "TubeA");
            world.AddFact(
                "Player",
                OntologyPredicates.InteractionIntent,
                "TubeA");

            var autoEquip = new OntologyRuleDefinition
            {
                id = "AutoEquipNearbyWearable"
            };
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.Near,
                "?object"));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.InteractionIntent,
                "?object"));
            autoEquip.conditions.Add(OntologyCondition.HasConcept("?actor", "Actor"));
            autoEquip.conditions.Add(OntologyCondition.HasConcept(
                "?object",
                OntologyConcepts.Wearable));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PickupBehavior,
                OntologyObjects.SelectThenEquip));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.AttachmentProfile,
                "?profile"));
            autoEquip.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.HasSlot,
                "?slot"));
            autoEquip.conditions.Add(OntologyCondition.NotFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.EquippedItem,
                "?equipped"));
            autoEquip.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.EquippedItem,
                "?object"));
            autoEquip.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.SlotOwner,
                "?actor"));
            autoEquip.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.SlotId,
                "?slot"));

            var attach = new OntologyRuleDefinition
            {
                id = "EquippedSlotItemAttaches"
            };
            attach.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.SlotOwner,
                "?actor"));
            attach.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.SlotId,
                "?slot"));
            attach.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.EquippedItem,
                "?item"));
            attach.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.HasSlot,
                "?slot"));
            attach.effects.Add(OntologyEffect.AddFact(
                "?item",
                OntologyPredicates.EquippedBy,
                "?actor"));

            var rules = new[] { autoEquip, attach };
            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(rules, null, "Player", 8);

            const string slotEntity = "EquipmentSlot_Player_Waist";
            Assert.That(
                world.HasFact(
                    slotEntity,
                    OntologyPredicates.EquippedItem,
                    "TubeA"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "TubeA",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.True);

            world.AddFact("Player", OntologyPredicates.Near, "TubeB");
            world.SetFact(
                "Player",
                OntologyPredicates.InteractionIntent,
                "TubeB",
                out _);
            service.Simulate(rules, null, "Player", 8);
            Assert.That(
                world.HasFact(
                    slotEntity,
                    OntologyPredicates.EquippedItem,
                    "TubeA"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "TubeA",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.True,
                "An occupied equipment slot must keep its attachment relation across later simulations.");
            Assert.That(
                world.HasFact(
                    "TubeB",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.False);

            world.RemoveFact(
                slotEntity,
                OntologyPredicates.EquippedItem,
                "TubeA");
            service.Simulate(rules, null, "Player", 8);
            Assert.That(
                world.HasFact(
                    slotEntity,
                    OntologyPredicates.EquippedItem,
                    "TubeB"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "TubeB",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.True);
        }

        [Test]
        public void UnequipWearableActionRemovesEquipmentStateWithoutDeletingObservation()
        {
            var world = new OntologyWorldState();
            const string slotEntity = "CustomRuntimeSlot";
            world.AddFact(
                slotEntity,
                OntologyPredicates.SlotOwner,
                "Player");
            world.AddFact(
                slotEntity,
                OntologyPredicates.SlotId,
                "Waist");
            world.AddFact(
                slotEntity,
                OntologyPredicates.EquippedItem,
                "TubeA");
            world.AddFact(
                "TubeA",
                OntologyPredicates.EquippedBy,
                "Player");
            world.AddFact(
                "Player",
                OntologyPredicates.InteractionIntent,
                "TubeA");
            world.AddFact(
                "Player",
                OntologyPredicates.Near,
                "TubeA");

            var definition = new OntologyActionEffectDefinition
            {
                actionVerb = OntologyActions.UnequipWearable
            };
            definition.conditions.Add(OntologyCondition.Fact(
                "?target",
                OntologyPredicates.EquippedBy,
                "?actor"));
            definition.conditions.Add(OntologyCondition.Fact(
                "?slot",
                OntologyPredicates.SlotOwner,
                "?actor"));
            definition.conditions.Add(OntologyCondition.Fact(
                "?slot",
                OntologyPredicates.EquippedItem,
                "?target"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?slot",
                OntologyPredicates.EquippedItem,
                "?target"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?target",
                OntologyPredicates.EquippedBy,
                "?actor"));
            definition.effects.Add(OntologyEffect.RemoveFact(
                "?actor",
                OntologyPredicates.InteractionIntent,
                "?target"));

            var runner = new OntologyActionRunner(new[] { definition });
            Assert.That(
                runner.ApplyAction(
                    world,
                    new OntologyAction(
                        "Player",
                        OntologyActions.UnequipWearable,
                        "TubeA")),
                Is.True);
            Assert.That(
                world.HasFact(
                    slotEntity,
                    OntologyPredicates.EquippedItem,
                    "TubeA"),
                Is.False);
            Assert.That(
                world.HasFact(
                    "TubeA",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.False);
            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.InteractionIntent,
                    "TubeA"),
                Is.False);
            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.Near,
                    "TubeA"),
                Is.True);
        }

        private static void AddWearable(
            OntologyWorldState world,
            string itemId,
            string slot)
        {
            world.AddConcept(itemId, OntologyConcepts.Wearable);
            world.AddFact(
                itemId,
                OntologyPredicates.PickupBehavior,
                OntologyObjects.SelectThenEquip);
            world.AddFact(
                itemId,
                OntologyPredicates.AttachmentProfile,
                "TestProfile");
            world.AddFact(itemId, OntologyPredicates.HasSlot, slot);
        }

        [Test]
        public void EquippedFloatingTubeGrantsTemporarySwimmingSkillUntilBuoyancyIsRemoved()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Player", "Actor");
            world.AddConcept("InflatableRing", OntologyConcepts.FlotationDevice);
            world.AddConcept("Lake", OntologyConcepts.WaterRegion);
            world.AddFact(
                "InflatableRing",
                OntologyPredicates.EquippedBy,
                "Player");
            world.AddFact(
                "InflatableRing",
                OntologyPredicates.GrantsSkill,
                "Swimming");
            world.AddFact(
                "InflatableRing",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating);
            world.AddFact("Player", OntologyPredicates.Occupies, "Lake");
            world.AddFact("Player", "immersion_depth", "Deep");

            var grantTemporarySkill = new OntologyRuleDefinition
            {
                id = "EquippedItemGrantsTemporarySkill"
            };
            grantTemporarySkill.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.EquippedBy,
                "?actor"));
            grantTemporarySkill.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.GrantsSkill,
                "?skill"));
            grantTemporarySkill.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));
            grantTemporarySkill.effects.Add(OntologyEffect.AddFact(
                "?actor",
                OntologyPredicates.HasTemporarySkill,
                "?skill"));

            var temporarySkillUsable = new OntologyRuleDefinition
            {
                id = "TemporarySkillBecomesUsable"
            };
            temporarySkillUsable.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.HasTemporarySkill,
                "?skill"));
            temporarySkillUsable.effects.Add(OntologyEffect.AddFact(
                "?actor",
                OntologyPredicates.CanUseSkill,
                "?skill"));

            var skillSwimming = new OntologyRuleDefinition
            {
                id = "UsableSwimmingSkillEnablesDeepWaterMovement"
            };
            skillSwimming.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.CanUseSkill,
                "Swimming"));
            skillSwimming.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.Occupies,
                "?target"));
            skillSwimming.conditions.Add(OntologyCondition.Fact(
                "?actor",
                "immersion_depth",
                "Deep"));
            skillSwimming.conditions.Add(OntologyCondition.HasConcept(
                "?target",
                OntologyConcepts.WaterRegion));
            skillSwimming.effects.Add(OntologyEffect.AddFact(
                "?actor",
                "movement_mode",
                "Swimming"));

            var cancelDrowning = new OntologyRuleDefinition
            {
                id = "UsableSwimmingSkillCancelsDrowning"
            };
            cancelDrowning.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.CanUseSkill,
                "Swimming"));
            cancelDrowning.conditions.Add(OntologyCondition.Fact(
                "?actor",
                "movement_mode",
                "Drowning"));
            cancelDrowning.effects.Add(OntologyEffect.RemoveFact(
                "?actor",
                "movement_mode",
                "Drowning"));

            var service = new OntologyWorldService();
            service.Reset(world);
            service.Simulate(
                new[]
                {
                    CreateDeepWaterDrowningRule(),
                    grantTemporarySkill,
                    temporarySkillUsable,
                    skillSwimming,
                    cancelDrowning
                },
                null,
                "Player",
                8);

            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.HasTemporarySkill,
                    "Swimming"),
                Is.True);
            Assert.That(
                world.HasFact("Player", OntologyPredicates.CanUseSkill, "Swimming"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.HasCapability,
                    OntologyObjects.WaterFloat),
                Is.False);
            Assert.That(
                world.HasFact("Player", "movement_mode", "Swimming"),
                Is.True);
            Assert.That(
                world.HasFact("Player", "movement_mode", "Drowning"),
                Is.False);

            world.RemoveFact(
                "InflatableRing",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating);
            service.Simulate(
                new[]
                {
                    CreateDeepWaterDrowningRule(),
                    grantTemporarySkill,
                    temporarySkillUsable,
                    skillSwimming,
                    cancelDrowning
                },
                null,
                "Player",
                8);

            Assert.That(
                world.HasFact(
                    "Player",
                    OntologyPredicates.HasTemporarySkill,
                    "Swimming"),
                Is.False);
            Assert.That(
                world.HasFact("Player", OntologyPredicates.CanUseSkill, "Swimming"),
                Is.False);
            Assert.That(
                world.HasFact("Player", "movement_mode", "Swimming"),
                Is.False);
            Assert.That(
                world.HasFact("Player", "movement_mode", "Drowning"),
                Is.True);
            Assert.That(
                world.HasFact(
                    "InflatableRing",
                    OntologyPredicates.EquippedBy,
                    "Player"),
                Is.True,
                "The tube remains equipped; only its buoyancy-backed skill must be retracted.");
        }

        private static OntologyRuleDefinition CreateDeepWaterDrowningRule()
        {
            var definition = new OntologyRuleDefinition { id = "DeepWaterWithoutSwimmingBlockDrowning" };
            definition.conditions.Add(OntologyCondition.Fact("?actor", "occupies", "?target"));
            definition.conditions.Add(OntologyCondition.Fact("?actor", "immersion_depth", "Deep"));
            definition.conditions.Add(OntologyCondition.HasConcept("?target", "WaterRegion"));
            definition.conditions.Add(OntologyCondition.NotFact("?target", "has_rule_block", "DeepWaterOccupancySwimming"));
            definition.conditions.Add(OntologyCondition.NotFact(
                "?actor",
                OntologyPredicates.CanUseSkill,
                "Swimming"));
            definition.effects.Add(OntologyEffect.AddFact("?actor", "movement_mode", "Drowning"));
            return definition;
        }
    }
}
