using System.IO;
using System.Linq;
using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySaveController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyRuntimeObjectPlacementController placementController;
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private string saveFileName = "ontology_save.json";
        [SerializeField] private bool useAccountWorldScopedPath = true;
        [SerializeField] private bool autoLoadLocalSnapshot = true;
        [SerializeField] private bool autoSaveLocalSnapshot = true;
        [SerializeField, Min(5f)] private float localAutosaveIntervalSeconds = 30f;

        private float nextAutosaveAt;

        public string SavePath => useAccountWorldScopedPath
            ? BuildScopedSavePath(
                Application.persistentDataPath,
                authorityClient?.CurrentUserId,
                authorityClient?.CurrentWorldId,
                saveFileName)
            : Path.Combine(Application.persistentDataPath, saveFileName);

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
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            nextAutosaveAt = Time.unscaledTime + Mathf.Max(5f, localAutosaveIntervalSeconds);
        }

        private void Start()
        {
            if (autoLoadLocalSnapshot && !AuthorityManagesPersistence() &&
                File.Exists(ResolveLoadPath()))
                LoadSnapshot();
        }

        private void Update()
        {
            if (!autoSaveLocalSnapshot || AuthorityManagesPersistence() ||
                Time.unscaledTime < nextAutosaveAt) return;
            nextAutosaveAt = Time.unscaledTime + Mathf.Max(5f, localAutosaveIntervalSeconds);
            SaveSnapshot();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && autoSaveLocalSnapshot && !AuthorityManagesPersistence())
                SaveSnapshot();
        }

        private void OnApplicationQuit()
        {
            if (autoSaveLocalSnapshot && !AuthorityManagesPersistence())
                SaveSnapshot();
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

            OntologyAtomicFileStore.WriteAllText(
                SavePath,
                json,
                keepBackup: true);
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

            var loadPath = ResolveLoadPath();
            if (!File.Exists(loadPath))
            {
                return "[OntologySaveController]\nSave file not found:\n" + SavePath;
            }

            var json = File.ReadAllText(loadPath);
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
            return "[OntologySaveController]\nLoaded snapshot from:\n" + loadPath +
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

        /// <summary>
        /// A configured Authority client owns durable world persistence even
        /// before login completes. Readiness is a connection state, not a signal
        /// that local snapshots may temporarily become the world source.
        /// </summary>
        public static bool ShouldUseLocalSnapshot(
            bool authorityClientConfigured) =>
            !authorityClientConfigured;

        private bool AuthorityManagesPersistence() =>
            !ShouldUseLocalSnapshot(authorityClient != null);

        private string ResolveLoadPath()
        {
            if (File.Exists(SavePath)) return SavePath;
            var legacy = Path.Combine(Application.persistentDataPath, saveFileName);
            return File.Exists(legacy) ? legacy : SavePath;
        }

        private static string Scope(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        public static string BuildScopedSavePath(
            string root,
            string accountId,
            string worldId,
            string fileName)
        {
            return Path.Combine(
                root,
                "Saves",
                "account-" + Scope(accountId, "local"),
                "world-" + Scope(worldId, "default"),
                string.IsNullOrWhiteSpace(fileName) ? "ontology_save.json" : fileName);
        }
    }
}
