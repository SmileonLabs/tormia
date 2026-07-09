using System.Text;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public static class OntologyCoreSample
    {
        public static string RunFirePlantFearScenario()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Tree", "Plant");
            world.AddConcept("Tree", "Dry");
            world.AddConcept("FireSword", "Fire");
            world.AddConcept("Goblin", "Creature");
            world.AddFact("Goblin", "fears", "Fire");
            world.AddFact("Goblin", "near", "Village");
            world.AddFact("Player", "attacks_with", "FireSword");
            world.AddFact("Player", "attacks", "Tree");

            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine);

            var simulation = new OntologySimulation(maxIterations: 8);
            var result = simulation.RunUntilStable(world, engine);

            var builder = new StringBuilder();
            builder.AppendLine(world.DumpFacts());
            builder.AppendLine(result.DumpEvents());

            return builder.ToString().TrimEnd();
        }

        public static string RunMultiNpcPlaceScenario()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Tree", "Plant");
            world.AddConcept("Tree", "Dry");
            world.AddConcept("FireSword", "Fire");
            world.AddConcept("Goblin", "Creature");
            world.AddConcept("Goblin", "NPC");
            world.AddConcept("Scout", "Creature");
            world.AddConcept("Scout", "NPC");
            world.AddConcept("QuestBoard", "QuestBoard");
            world.AddFact("Goblin", "fears", "Fire");
            world.AddFact("Goblin", "near", "Village");
            world.AddFact("Scout", "fears", "Fire");
            world.AddFact("Scout", "near", "VillageEast");
            world.AddFact("Player", "attacks_with", "FireSword");
            world.AddFact("Player", "attacks", "Tree");

            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine);
            var simulation = new OntologySimulation(maxIterations: 8);
            var result = simulation.RunUntilStable(world, engine);

            var saveData = OntologySaveDataConverter.Capture(world, new OntologySession());
            var restored = OntologySaveDataConverter.RestoreWorld(saveData);

            var builder = new StringBuilder();
            builder.AppendLine("[Multi NPC/Place Scenario]");
            builder.AppendLine("GoblinDisturbsVillage=" + world.HasFact("Village", "state", "Disturbed"));
            builder.AppendLine("ScoutDisturbsVillageEast=" + world.HasFact("VillageEast", "state", "Disturbed"));
            builder.AppendLine("RestoredVillageEast=" + restored.HasFact("VillageEast", "state", "Disturbed"));
            builder.AppendLine(result.DumpEvents());
            return builder.ToString().TrimEnd();
        }

        public static string RunCraftStoneAxeScenario()
        {
            var world = new OntologyWorldState();
            world.AddFact("Player", "combined", "Stone");
            world.AddFact("Player", "combined", "Stick");

            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine);
            var simulation = new OntologySimulation(maxIterations: 4);
            var result = simulation.RunUntilStable(world, engine);

            var session = new OntologySession();
            session.RecordEvents(result);

            var builder = new StringBuilder();
            builder.AppendLine("[Craft Stone Axe Scenario]");
            builder.AppendLine("HasStoneAxe=" + world.HasFact("Player", "has_item", "StoneAxe"));
            builder.AppendLine("ConsumedStone=" + !world.HasFact("Player", "combined", "Stone"));
            builder.AppendLine("ConsumedStick=" + !world.HasFact("Player", "combined", "Stick"));
            builder.AppendLine("EventHistoryHasCraft=" + session.EventHistory.Exists(evt => evt.EventType == "CraftStoneAxeRule"));
            builder.AppendLine(session.DumpHistory());
            return builder.ToString().TrimEnd();
        }

        public static string RunTerrainTileTransferScenario()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Tile_01", "TerrainTile");
            world.AddFact("Tile_01", "has_element", "Water");
            world.AddFact("Tile_01", "temperature", "Freezing");
            world.AddFact("Tile_01", "surface", "Swamp");
            world.AddFact("Player", "standing_on", "Tile_01");

            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine);
            var simulation = new OntologySimulation(maxIterations: 4);
            var result = simulation.RunUntilStable(world, engine);

            var session = new OntologySession();
            session.RecordEvents(result);

            var builder = new StringBuilder();
            builder.AppendLine("[Terrain Tile Transfer Scenario]");
            builder.AppendLine("PlayerWet=" + world.HasFact("Player", "status", "Wet"));
            builder.AppendLine("PlayerColdExposure=" + world.HasFact("Player", "exposed_to", "ColdEnvironment"));
            builder.AppendLine("PlayerSlowed=" + world.HasFact("Player", "movement_state", "Slowed"));
            builder.AppendLine("EventHistoryHasWet=" + session.EventHistory.Exists(evt => evt.EventType == "TileWaterStatusWetRule"));
            builder.AppendLine("EventHistoryHasCold=" + session.EventHistory.Exists(evt => evt.EventType == "TileFreezingColdExposureRule"));
            builder.AppendLine("EventHistoryHasSlow=" + session.EventHistory.Exists(evt => evt.EventType == "TileSwampMovementSlowRule"));
            builder.AppendLine(session.DumpHistory());
            return builder.ToString().TrimEnd();
        }

        public static string RunColdExposureTickScenario()
        {
            var world = new OntologyWorldState();
            world.AddConcept("Tile_Cold_01", "TerrainTile");
            world.AddFact("Tile_Cold_01", "temperature", "Freezing");
            world.AddFact("Player", "standing_on", "Tile_Cold_01");
            world.AddFact("Player", "core_vitality", "10");
            world.AddFact("Simulation", "current_tick", "Tick_1");

            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine);
            var simulation = new OntologySimulation(maxIterations: 4);

            var firstTickResult = simulation.RunUntilStable(world, engine);
            var repeatedSameTickResult = simulation.RunUntilStable(world, engine);

            world.RemoveFacts("Simulation", "current_tick");
            world.AddFact("Simulation", "current_tick", "Tick_2");
            var secondTickResult = simulation.RunUntilStable(world, engine);

            world.AddFact("Player", "status", "Warm");
            world.RemoveFacts("Simulation", "current_tick");
            world.AddFact("Simulation", "current_tick", "Tick_3");
            var warmTickResult = simulation.RunUntilStable(world, engine);

            var session = new OntologySession();
            session.RecordEvents(firstTickResult);
            session.RecordEvents(repeatedSameTickResult);
            session.RecordEvents(secondTickResult);
            session.RecordEvents(warmTickResult);

            var builder = new StringBuilder();
            builder.AppendLine("[Cold Exposure Tick Scenario]");
            builder.AppendLine("AfterTwoColdTicksVitality8=" + world.HasFact("Player", "core_vitality", "8"));
            builder.AppendLine("SameTickBlocked=" + world.HasFact("Player", "cold_damage_tick", "Tick_1"));
            builder.AppendLine("SecondTickApplied=" + world.HasFact("Player", "cold_damage_tick", "Tick_2"));
            builder.AppendLine("WarmTickBlocked=" + !world.HasFact("Player", "cold_damage_tick", "Tick_3"));
            builder.AppendLine("ColdExposureInferred=" + world.HasFact("Player", "exposed_to", "ColdEnvironment"));
            builder.AppendLine(session.DumpHistory());
            return builder.ToString().TrimEnd();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogSampleInEditor()
        {
#if UNITY_EDITOR
            Debug.Log("[OntologyCoreSample]\n" + RunFirePlantFearScenario() + "\n\n" + RunMultiNpcPlaceScenario() + "\n\n" + RunCraftStoneAxeScenario() + "\n\n" + RunTerrainTileTransferScenario() + "\n\n" + RunColdExposureTickScenario());
#endif
        }
    }
}
