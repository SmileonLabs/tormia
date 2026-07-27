#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Creates only local developer connection plumbing; it does not modify ontology facts or rules.</summary>
    public static class OntologyWorldAuthoritySetup
    {
        private const string SettingsFolder = "Assets/Data/Ontology/Networking";
        private const string SettingsPath = SettingsFolder + "/WorldAuthoritySettings.asset";

        [MenuItem("Tools/Ontology/Networking/Setup Local Authority Connection")]
        public static void Setup()
        {
            var bootstrap = Object.FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError(
                    "[OntologyAuthoritySetup] Open a scene containing OntologyWorldBootstrap first.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                System.IO.Directory.CreateDirectory(SettingsFolder);
                AssetDatabase.Refresh();
            }

            var settings = AssetDatabase.LoadAssetAtPath<OntologyWorldAuthoritySettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<OntologyWorldAuthoritySettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var client = bootstrap.GetComponent<OntologyWorldAuthorityClient>();
            if (client == null)
            {
                client = Undo.AddComponent<OntologyWorldAuthorityClient>(bootstrap.gameObject);
            }
            client.Configure(settings);

            var bridge = bootstrap.GetComponent<OntologyWorldAuthorityBridge>();
            if (bridge == null)
            {
                bridge = Undo.AddComponent<OntologyWorldAuthorityBridge>(bootstrap.gameObject);
            }
            bridge.Configure(client);

            var realtime = bootstrap.GetComponent<OntologyWorldAuthorityRealtimeClient>();
            if (realtime == null)
            {
                realtime = Undo.AddComponent<OntologyWorldAuthorityRealtimeClient>(bootstrap.gameObject);
            }
            realtime.Configure(client);

            EditorUtility.SetDirty(settings);
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            Debug.Log(
                "[OntologyAuthoritySetup] Local authority connection is ready. Existing world editing remains local until commands are explicitly published.",
                bootstrap);
        }

        [MenuItem("Tools/Ontology/Networking/Assign Stable Entity IDs to Placed Objects")]
        public static void AssignStableEntityIds()
        {
            var assigned = 0;
            foreach (var placeable in Object.FindObjectsByType<OntologyPlaceableInstance>(
                         FindObjectsInactive.Include))
            {
                if (placeable == null) continue;
                var identity = placeable.GetComponent<OntologyAuthorityEntityIdentity>();
                if (identity == null)
                {
                    identity = Undo.AddComponent<OntologyAuthorityEntityIdentity>(
                        placeable.gameObject);
                }

                if (!identity.TryGetGuid(out _))
                {
                    Undo.RecordObject(identity, "Assign ontology authority entity id");
                    identity.EnsureGuid();
                    EditorUtility.SetDirty(identity);
                    assigned++;
                }
            }

            if (assigned > 0)
            {
                EditorSceneManager.MarkAllScenesDirty();
            }
            Debug.Log("[OntologyAuthoritySetup] Assigned " + assigned +
                      " stable authority entity id(s) to placed objects.");
        }
    }
}
#endif
