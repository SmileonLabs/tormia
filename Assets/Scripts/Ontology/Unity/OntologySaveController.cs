using System.IO;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySaveController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyRuntimeObjectPlacementController placementController;
        [SerializeField] private string saveFileName = "ontology_save.json";

        public string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }
            if (placementController == null)
            {
                placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            }
        }

        public string SaveSnapshot()
        {
            if (!EnsureBootstrapReady())
            {
                return "[OntologySaveController]\nNo OntologyWorldBootstrap found.";
            }

            InjectCharacterPartFacts();
            var saveData = OntologySaveDataConverter.Capture(
                bootstrap.World,
                bootstrap.Session,
                bootstrap.AllRuleDefinitions);
            if (placementController == null)
                placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            if (placementController != null)
                saveData.placedObjects = placementController.CapturePlacedObjects();
            if (bootstrap.RuleBlockRegistry != null)
                saveData.controlledRuleIds.AddRange(bootstrap.RuleBlockRegistry.ControlledRuleIds);
            var json = JsonUtility.ToJson(saveData, prettyPrint: true);

            var directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SavePath, json);
            return "[OntologySaveController]\nSaved snapshot to:\n" + SavePath +
                   "\nFacts: " + saveData.facts.Count +
                   "\nPlaced objects: " + saveData.placedObjects.Count;
        }

        public string LoadSnapshot()
        {
            if (!EnsureBootstrapReady())
            {
                return "[OntologySaveController]\nNo OntologyWorldBootstrap found.";
            }

            if (!File.Exists(SavePath))
            {
                return "[OntologySaveController]\nSave file not found:\n" + SavePath;
            }

            var json = File.ReadAllText(SavePath);
            var saveData = JsonUtility.FromJson<OntologySaveData>(json);
            OntologyLanguagePackService.MigrateSaveData(saveData);
            if (placementController == null)
                placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            var migratedPlacementRecords = placementController == null
                ? 0
                : placementController.MigrateLegacyPlacementRecords(saveData);
            var restoredObjects = 0;
            if (saveData != null && saveData.version >= 2)
            {
                if (placementController == null)
                    placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
                if (placementController != null)
                    restoredObjects = placementController.RestorePlacedObjects(saveData.placedObjects);
            }
            if (saveData != null && saveData.version >= 3 && bootstrap.RuleBlockRegistry != null)
            {
                // Rule-control policy belongs to the authored world. Loading an older player
                // snapshot may add controlled rules, but must not remove scene-required ones.
                bootstrap.RuleBlockRegistry.Replace(
                    bootstrap.RuleBlockRegistry.ControlledRuleIds.Concat(
                        saveData.controlledRuleIds ?? Enumerable.Empty<string>()));
            }
            var report = bootstrap.RestoreSnapshot(saveData, logReport: false);
            SyncCharacterPartRenderersFromFacts();
            if (migratedPlacementRecords > 0)
            {
                SaveSnapshot();
            }
            return "[OntologySaveController]\nLoaded snapshot from:\n" + SavePath +
                   "\nRestored placed objects: " + restoredObjects +
                   "\nMigrated legacy placement records: " + migratedPlacementRecords +
                   "\n\n" + report;
        }

        private bool EnsureBootstrapReady()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null)
            {
                return false;
            }

            if (bootstrap.World == null || bootstrap.Session == null)
            {
                bootstrap.ResetWorld(logReport: false);
            }

            return true;
        }

        private static void InjectCharacterPartFacts()
        {
            foreach (var adapter in FindObjectsByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include))
            {
                adapter.InjectActivePartFacts();
            }
        }

        private static void SyncCharacterPartRenderersFromFacts()
        {
            foreach (var adapter in FindObjectsByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include))
            {
                adapter.SyncRenderersFromWorldFacts();
            }
        }
    }
}
