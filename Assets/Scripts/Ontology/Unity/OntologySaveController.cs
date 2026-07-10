using System.IO;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologySaveController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private string saveFileName = "ontology_save.json";

        public string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }
        }

        public string SaveSnapshot()
        {
            if (!EnsureBootstrapReady())
            {
                return "[OntologySaveController]\nNo OntologyWorldBootstrap found.";
            }

            InjectCharacterPartFacts();
            var saveData = OntologySaveDataConverter.Capture(bootstrap.World, bootstrap.Session);
            var json = JsonUtility.ToJson(saveData, prettyPrint: true);

            var directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SavePath, json);
            return "[OntologySaveController]\nSaved snapshot to:\n" + SavePath + "\nFacts: " + saveData.facts.Count;
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
            var report = bootstrap.RestoreSnapshot(saveData, logReport: false);
            SyncCharacterPartRenderersFromFacts();
            return "[OntologySaveController]\nLoaded snapshot from:\n" + SavePath + "\n\n" + report;
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
