using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    public static class OntologyDefaultRules
    {
        public static void RegisterRules(OntologyRuleEngine engine)
        {
            RegisterRules(engine, null);
        }

        public static void RegisterRules(OntologyRuleEngine engine, IReadOnlyList<OntologyRuleDefinition> dataRules)
        {
            var definitions = dataRules != null && dataRules.Count > 0 ? dataRules : CreateDefaultDefinitions();
            foreach (var definition in definitions)
            {
                if (definition != null)
                {
                    engine.AddRule(OntologyRuleCompiler.Compile(definition));
                }
            }
        }

        public static List<OntologyRuleDefinition> CreateDefaultDefinitions()
        {
            var burnDefinition = new OntologyRuleDefinition
            {
                id = "BurnEvent",
                description = "A dry plant hit by a fire object starts burning."
            };
            burnDefinition.conditions.Add(OntologyCondition.Fact("?actor", "attacks", "?target"));
            burnDefinition.conditions.Add(OntologyCondition.Fact("?actor", "attacks_with", "?tool"));
            burnDefinition.conditions.Add(OntologyCondition.HasConcept("?tool", "Fire"));
            burnDefinition.conditions.Add(OntologyCondition.HasConcept("?target", "Plant"));
            burnDefinition.conditions.Add(OntologyCondition.HasConcept("?target", "Dry"));
            burnDefinition.conditions.Add(OntologyCondition.NotFact("?target", "state", "Burning"));
            burnDefinition.effects.Add(OntologyEffect.AddFact("?target", "state", "Burning"));
            burnDefinition.effects.Add(OntologyEffect.AddFact("?target", "emits", "Fire"));

            var smokeDefinition = new OntologyRuleDefinition
            {
                id = "SmokeEvent",
                description = "A burning object emits smoke, creating a visible world signal."
            };
            smokeDefinition.conditions.Add(OntologyCondition.Fact("?target", "state", "Burning"));
            smokeDefinition.conditions.Add(OntologyCondition.NotFact("?target", "emits", "Smoke"));
            smokeDefinition.effects.Add(OntologyEffect.AddFact("?target", "emits", "Smoke"));

            var fleeDefinition = new OntologyRuleDefinition
            {
                id = "FleeEvent",
                description = "A creature that fears fire flees when a nearby object emits fire."
            };
            fleeDefinition.conditions.Add(OntologyCondition.HasConcept("?creature", "Creature"));
            fleeDefinition.conditions.Add(OntologyCondition.Fact("?creature", "fears", "Fire"));
            fleeDefinition.conditions.Add(OntologyCondition.Fact("?emitter", "emits", "Fire"));
            fleeDefinition.conditions.Add(OntologyCondition.NotFact("?creature", "intent", "Flee"));
            fleeDefinition.effects.Add(OntologyEffect.AddFact("?creature", "intent", "Flee"));

            var disturbanceDefinition = new OntologyRuleDefinition
            {
                id = "DisturbanceEvent",
                description = "A fleeing creature near a village creates a local disturbance."
            };
            disturbanceDefinition.conditions.Add(OntologyCondition.Fact("?creature", "intent", "Flee"));
            disturbanceDefinition.conditions.Add(OntologyCondition.Fact("?creature", "near", "?place"));
            disturbanceDefinition.conditions.Add(OntologyCondition.NotFact("?place", "state", "Disturbed"));
            disturbanceDefinition.conditions.Add(OntologyCondition.NotFact("?place", "state", "Recovering"));
            disturbanceDefinition.effects.Add(OntologyEffect.AddFact("?place", "state", "Disturbed"));

            var questHookDefinition = new OntologyRuleDefinition
            {
                id = "QuestHookEvent",
                description = "A disturbed place and visible smoke expose an investigation quest hook."
            };
            questHookDefinition.conditions.Add(OntologyCondition.Fact("?smokeSource", "emits", "Smoke"));
            questHookDefinition.conditions.Add(OntologyCondition.Fact("?place", "state", "Disturbed"));
            questHookDefinition.conditions.Add(OntologyCondition.HasConcept("?board", "QuestBoard"));
            questHookDefinition.conditions.Add(OntologyCondition.NotFact("?board", "offers", "InvestigateSmokeAndPanic"));
            questHookDefinition.effects.Add(OntologyEffect.AddFact("?board", "offers", "InvestigateSmokeAndPanic"));

            var fleeingNpcMoodDefinition = new OntologyRuleDefinition
            {
                id = "NpcFearMoodEvent",
                description = "A fleeing NPC becomes afraid."
            };
            fleeingNpcMoodDefinition.conditions.Add(OntologyCondition.HasConcept("?npc", "NPC"));
            fleeingNpcMoodDefinition.conditions.Add(OntologyCondition.Fact("?npc", "intent", "Flee"));
            fleeingNpcMoodDefinition.conditions.Add(OntologyCondition.NotFact("?npc", "mood", "Afraid"));
            fleeingNpcMoodDefinition.effects.Add(OntologyEffect.AddFact("?npc", "mood", "Afraid"));

            var afraidNpcTalkDefinition = new OntologyRuleDefinition
            {
                id = "NpcDistrustEvent",
                description = "Talking to an afraid NPC makes them distrust the player."
            };
            afraidNpcTalkDefinition.conditions.Add(OntologyCondition.Fact("?actor", "talks_to", "?npc"));
            afraidNpcTalkDefinition.conditions.Add(OntologyCondition.HasConcept("?npc", "NPC"));
            afraidNpcTalkDefinition.conditions.Add(OntologyCondition.Fact("?npc", "mood", "Afraid"));
            afraidNpcTalkDefinition.conditions.Add(OntologyCondition.NotFact("Relation_{npc}_{actor}", "state", "Distrustful"));
            afraidNpcTalkDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "has_concept", "Relation"));
            afraidNpcTalkDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "subject", "?npc"));
            afraidNpcTalkDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "target", "?actor"));
            afraidNpcTalkDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "state", "Distrustful"));

            var villageHelpRespectDefinition = new OntologyRuleDefinition
            {
                id = "NpcRespectEvent",
                description = "Helping the village earns respect from nearby NPCs."
            };
            villageHelpRespectDefinition.conditions.Add(OntologyCondition.Fact("?actor", "helps", "?place"));
            villageHelpRespectDefinition.conditions.Add(OntologyCondition.HasConcept("?npc", "NPC"));
            villageHelpRespectDefinition.conditions.Add(OntologyCondition.Fact("?npc", "near", "?place"));
            villageHelpRespectDefinition.conditions.Add(OntologyCondition.NotFact("Relation_{npc}_{actor}", "state", "Respectful"));
            villageHelpRespectDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "has_concept", "Relation"));
            villageHelpRespectDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "subject", "?npc"));
            villageHelpRespectDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "target", "?actor"));
            villageHelpRespectDefinition.effects.Add(OntologyEffect.AddFact("Relation_{npc}_{actor}", "state", "Respectful"));

            var questConsequenceDefinition = new OntologyRuleDefinition
            {
                id = "QuestConsequenceEvent",
                description = "Completing the smoke investigation helps the disturbed village recover."
            };
            questConsequenceDefinition.conditions.Add(OntologyCondition.Fact("?actor", "completed_quest", "InvestigateSmokeAndPanic"));
            questConsequenceDefinition.conditions.Add(OntologyCondition.Fact("?place", "state", "Disturbed"));
            questConsequenceDefinition.conditions.Add(OntologyCondition.Fact("?board", "offers", "InvestigateSmokeAndPanic"));
            questConsequenceDefinition.conditions.Add(OntologyCondition.NotFact("?place", "state", "Recovering"));
            questConsequenceDefinition.effects.Add(OntologyEffect.SetFact("?place", "state", "Recovering"));
            questConsequenceDefinition.effects.Add(OntologyEffect.AddFact("?board", "records", "InvestigateSmokeAndPanicCompleted"));

            var craftStoneAxeDefinition = new OntologyRuleDefinition
            {
                id = "CraftStoneAxeRule",
                description = "Player -- CRAFTED --> StoneAxe"
            };
            craftStoneAxeDefinition.conditions.Add(OntologyCondition.Fact("Player", "combined", "Stone"));
            craftStoneAxeDefinition.conditions.Add(OntologyCondition.Fact("Player", "combined", "Stick"));
            craftStoneAxeDefinition.conditions.Add(OntologyCondition.NotFact("Player", "has_item", "StoneAxe"));
            craftStoneAxeDefinition.effects.Add(OntologyEffect.AddFact("Player", "has_item", "StoneAxe"));
            craftStoneAxeDefinition.effects.Add(OntologyEffect.RemoveFact("Player", "combined", "Stone"));
            craftStoneAxeDefinition.effects.Add(OntologyEffect.RemoveFact("Player", "combined", "Stick"));

            var waterTileWetDefinition = new OntologyRuleDefinition
            {
                id = "TileWaterStatusWetRule",
                description = "Actor standing on a water tile becomes Wet."
            };
            waterTileWetDefinition.conditions.Add(OntologyCondition.Fact("?actor", "standing_on", "?tile"));
            waterTileWetDefinition.conditions.Add(OntologyCondition.HasConcept("?tile", "TerrainTile"));
            waterTileWetDefinition.conditions.Add(OntologyCondition.Fact("?tile", "has_element", "Water"));
            waterTileWetDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "status", "Wet"));
            waterTileWetDefinition.effects.Add(OntologyEffect.AddFact("?actor", "status", "Wet"));

            var freezingTileColdExposureDefinition = new OntologyRuleDefinition
            {
                id = "TileFreezingColdExposureRule",
                description = "Actor standing on a freezing tile is exposed to ColdEnvironment."
            };
            freezingTileColdExposureDefinition.conditions.Add(OntologyCondition.Fact("?actor", "standing_on", "?tile"));
            freezingTileColdExposureDefinition.conditions.Add(OntologyCondition.HasConcept("?tile", "TerrainTile"));
            freezingTileColdExposureDefinition.conditions.Add(OntologyCondition.Fact("?tile", "temperature", "Freezing"));
            freezingTileColdExposureDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "exposed_to", "ColdEnvironment"));
            freezingTileColdExposureDefinition.effects.Add(OntologyEffect.AddFact("?actor", "exposed_to", "ColdEnvironment"));

            var swampResistanceCapabilityDefinition = new OntologyRuleDefinition
            {
                id = "SwampResistanceCapabilityRule",
                description = "SwampResistance equipment gives the actor swamp movement protection."
            };
            swampResistanceCapabilityDefinition.conditions.Add(OntologyCondition.Fact("?actor", OntologyPredicates.EquippedPart, "?part"));
            swampResistanceCapabilityDefinition.conditions.Add(OntologyCondition.Fact("?part", OntologyPredicates.Provides, OntologyObjects.SwampResistance));
            swampResistanceCapabilityDefinition.conditions.Add(OntologyCondition.NotFact("?actor", OntologyPredicates.HasCapability, OntologyObjects.SwampResistance));
            swampResistanceCapabilityDefinition.effects.Add(OntologyEffect.AddFact("?actor", OntologyPredicates.HasCapability, OntologyObjects.SwampResistance));

            var swampTileSlowDefinition = new OntologyRuleDefinition
            {
                id = "TileSwampMovementSlowRule",
                description = "Actor standing on a swamp tile becomes Slowed unless protected by SwampResistance."
            };
            swampTileSlowDefinition.conditions.Add(OntologyCondition.Fact("?actor", "standing_on", "?tile"));
            swampTileSlowDefinition.conditions.Add(OntologyCondition.HasConcept("?tile", "TerrainTile"));
            swampTileSlowDefinition.conditions.Add(OntologyCondition.Fact("?tile", "surface", "Swamp"));
            swampTileSlowDefinition.conditions.Add(OntologyCondition.NotFact("?actor", OntologyPredicates.HasCapability, OntologyObjects.SwampResistance));
            swampTileSlowDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "movement_state", "Slowed"));
            swampTileSlowDefinition.effects.Add(OntologyEffect.AddFact("?actor", "movement_state", "Slowed"));

            var coldExposureVitalityTickDefinition = new OntologyRuleDefinition
            {
                id = "ColdExposureVitalityTickRule",
                description = "Cold exposure lowers CoreVitality once per simulation tick unless the actor is Warm."
            };
            coldExposureVitalityTickDefinition.conditions.Add(OntologyCondition.Fact("Simulation", "current_tick", "?tick"));
            coldExposureVitalityTickDefinition.conditions.Add(OntologyCondition.Fact("?actor", "exposed_to", "ColdEnvironment"));
            coldExposureVitalityTickDefinition.conditions.Add(OntologyCondition.Fact("?actor", "core_vitality", "?vitality"));
            coldExposureVitalityTickDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "status", "Warm"));
            coldExposureVitalityTickDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "cold_damage_tick", "?tick"));
            coldExposureVitalityTickDefinition.effects.Add(OntologyEffect.AdjustNumberFact("?actor", "core_vitality", "-1"));
            coldExposureVitalityTickDefinition.effects.Add(OntologyEffect.AddFact("?actor", "cold_damage_tick", "?tick"));

            var coldProtectionWarmthDefinition = new OntologyRuleDefinition
            {
                id = "ColdProtectionWarmthRule",
                description = "ColdProtection equipment keeps a cold-exposed actor Warm."
            };
            coldProtectionWarmthDefinition.conditions.Add(OntologyCondition.Fact("?actor", "exposed_to", "ColdEnvironment"));
            coldProtectionWarmthDefinition.conditions.Add(OntologyCondition.Fact("?actor", OntologyPredicates.EquippedPart, "?part"));
            coldProtectionWarmthDefinition.conditions.Add(OntologyCondition.Fact("?part", OntologyPredicates.Provides, OntologyObjects.ColdProtection));
            coldProtectionWarmthDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "status", "Warm"));
            coldProtectionWarmthDefinition.effects.Add(OntologyEffect.AddFact("?actor", "status", "Warm"));

            var slowedAnimationIntentDefinition = new OntologyRuleDefinition
            {
                id = "SlowedAnimationIntentRule",
                description = "Slowed actors prefer impaired movement animation intent."
            };
            slowedAnimationIntentDefinition.conditions.Add(OntologyCondition.Fact("?actor", "movement_state", "Slowed"));
            slowedAnimationIntentDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "animation_intent", "ImpairedMovement"));
            slowedAnimationIntentDefinition.effects.Add(OntologyEffect.AddFact("?actor", "animation_intent", "ImpairedMovement"));

            var coldAnimationIntentDefinition = new OntologyRuleDefinition
            {
                id = "ColdAnimationIntentRule",
                description = "Cold exposure prefers discomfort animation intent."
            };
            coldAnimationIntentDefinition.conditions.Add(OntologyCondition.Fact("?actor", "exposed_to", "ColdEnvironment"));
            coldAnimationIntentDefinition.conditions.Add(OntologyCondition.NotFact("?actor", "animation_intent", "Discomfort"));
            coldAnimationIntentDefinition.effects.Add(OntologyEffect.AddFact("?actor", "animation_intent", "Discomfort"));

            var lowVitalityAnimationIntent3Definition = CreateVitalityAnimationIntentRule("LowVitalityAnimationIntentRule3", "3", "SevereDamageReaction");
            var lowVitalityAnimationIntent2Definition = CreateVitalityAnimationIntentRule("LowVitalityAnimationIntentRule2", "2", "SevereDamageReaction");
            var lowVitalityAnimationIntent1Definition = CreateVitalityAnimationIntentRule("LowVitalityAnimationIntentRule1", "1", "SevereDamageReaction");
            var deathAnimationIntentDefinition = CreateVitalityAnimationIntentRule("DeathAnimationIntentRule", "0", "DeathReaction");

            return new List<OntologyRuleDefinition>
            {
                burnDefinition,
                smokeDefinition,
                fleeDefinition,
                disturbanceDefinition,
                questHookDefinition,
                fleeingNpcMoodDefinition,
                afraidNpcTalkDefinition,
                villageHelpRespectDefinition,
                questConsequenceDefinition,
                craftStoneAxeDefinition,
                waterTileWetDefinition,
                freezingTileColdExposureDefinition,
                swampResistanceCapabilityDefinition,
                swampTileSlowDefinition,
                coldProtectionWarmthDefinition,
                coldExposureVitalityTickDefinition,
                slowedAnimationIntentDefinition,
                coldAnimationIntentDefinition,
                lowVitalityAnimationIntent3Definition,
                lowVitalityAnimationIntent2Definition,
                lowVitalityAnimationIntent1Definition,
                deathAnimationIntentDefinition
            };
        }

        private static OntologyRuleDefinition CreateVitalityAnimationIntentRule(string id, string vitality, string intent)
        {
            var definition = new OntologyRuleDefinition
            {
                id = id,
                description = "Core vitality " + vitality + " creates animation intent " + intent + "."
            };
            definition.conditions.Add(OntologyCondition.Fact("?actor", "core_vitality", vitality));
            definition.conditions.Add(OntologyCondition.NotFact("?actor", "animation_intent", intent));
            definition.effects.Add(OntologyEffect.AddFact("?actor", "animation_intent", intent));
            return definition;
        }
    }
}
