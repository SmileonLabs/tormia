using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Creates editable sample data and connects the generic runtime placement UI.</summary>
    public static class OntologyObjectPlacementSetup
    {
        private const string DataFolder = "Assets/Data/Ontology/Placeables";
        private const string CatalogPath = DataFolder + "/PolyStylePlaceables.asset";
        private const string UiDataFolder = "Assets/Data/Ontology/UI";
        private const string PanelPrefabPath = UiDataFolder + "/ObjectPlacementHUD.prefab";
        private const string ThemePath = UiDataFolder + "/ObjectPlacementUiTheme.asset";

        [MenuItem("Tools/Ontology/Setup Runtime Object Placement")]
        public static void Setup()
        {
            SetupForCurrentScene(out var result);
            EditorUtility.DisplayDialog("Object Placement Setup", result, "OK");
        }

        public static bool SetupForCurrentScene(out string result)
        {
            EnsureFolder("Assets/Data"); EnsureFolder("Assets/Data/Ontology"); EnsureFolder(DataFolder);
            var catalog = AssetDatabase.LoadAssetAtPath<OntologyPlaceableCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<OntologyPlaceableCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                SeedPolyStyleEntries(catalog);
            }

            var player = GameObject.Find("OntologyPlayer");
            var canvasObject = GameObject.Find("OntologyGameCanvas");
            if (player == null || canvasObject == null)
            {
                result = "OntologyPlayer and OntologyGameCanvas must exist in the open scene.";
                return false;
            }

            var controller = player.GetComponent<OntologyRuntimeObjectPlacementController>();
            if (controller == null) controller = Undo.AddComponent<OntologyRuntimeObjectPlacementController>(player);
            var worldEditor = player.GetComponent<OntologyRuntimeWorldEditorController>();
            if (worldEditor == null) worldEditor = Undo.AddComponent<OntologyRuntimeWorldEditorController>(player);
            var worldEditorPanel = canvasObject.GetComponent<OntologyRuntimeWorldEditorPanel>();
            if (worldEditorPanel == null) worldEditorPanel = Undo.AddComponent<OntologyRuntimeWorldEditorPanel>(canvasObject);
            var factEditorPanel = canvasObject.GetComponent<OntologyRuntimeWorldFactEditorPanel>();
            if (factEditorPanel == null) factEditorPanel = Undo.AddComponent<OntologyRuntimeWorldFactEditorPanel>(canvasObject);
            var panel = canvasObject.GetComponent<OntologyObjectPlacementPanel>();
            if (panel == null) panel = Undo.AddComponent<OntologyObjectPlacementPanel>(canvasObject);
            var panelPrefab = EnsurePanelPrefab(canvasObject.transform.Find("ObjectPlacementHUD")?.gameObject);
            var theme = EnsureTheme();
            if (panelPrefab == null)
            {
                result = "Create an ObjectPlacementHUD under OntologyGameCanvas once, then run setup again.";
                return false;
            }
            controller.Configure(catalog, player.transform, Camera.main, Object.FindAnyObjectByType<OntologyWorldBootstrap>(), null);
            worldEditor.Configure(Camera.main, controller, Object.FindAnyObjectByType<OntologyWorldBootstrap>());
            panel.Configure(controller, catalog, panelPrefab, theme);
            if (canvasObject.transform.Find("WorldEditHUD") == null)
            {
                result =
                    "WorldEditHUD prefab instance is missing. Add " +
                    "Assets/Prefabs/Ontology/UI/WorldEditHUD.prefab under " +
                    "OntologyGameCanvas, then run setup again.";
                return false;
            }
            worldEditorPanel.Configure(worldEditor);
            factEditorPanel.Configure(worldEditor);
            EditorUtility.SetDirty(catalog); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(worldEditor); EditorUtility.SetDirty(panel); EditorUtility.SetDirty(worldEditorPanel); EditorUtility.SetDirty(factEditorPanel);
            EditorSceneManager.MarkSceneDirty(player.scene);
            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            result = "Runtime Object Placement is ready. Enter Play Mode, then press B to open the object panel.";
            return true;
        }

        private static void SeedPolyStyleEntries(OntologyPlaceableCatalog catalog)
        {
            var entries = new List<OntologyPlaceableDefinition>();
            Add(entries, "PolyStyle_Stone_1", "Stone 1", "Nature", "A POLY STYLE stone.", "Stone_1.prefab", "Rock", "Resource");
            Add(entries, "PolyStyle_Stone_2", "Stone 2", "Nature", "A POLY STYLE stone.", "Stone_2.prefab", "Rock", "Resource");
            Add(entries, "PolyStyle_Stone_3", "Stone 3", "Nature", "A POLY STYLE stone.", "Stone_3.prefab", "Rock", "Resource");
            Add(entries, "PolyStyle_Grass_1", "Grass 1", "Nature", "A POLY STYLE vegetation group.", "Grass_1.prefab", "Plant");
            Add(entries, "PolyStyle_Grass_2", "Grass 2", "Nature", "A POLY STYLE vegetation group.", "Grass_2.prefab", "Plant");
            Add(entries, "PolyStyle_Environment_1", "Environment 1", "Landmark", "A POLY STYLE environment cluster.", "Environment_1.prefab", "Landmark");

            catalog.ReplaceDefinitions(entries);
            EditorUtility.SetDirty(catalog);
        }

        private static void Add(List<OntologyPlaceableDefinition> entries, string id, string displayName, string category, string description, string prefabName, params string[] concepts)
        {
            const string prefabFolder = "Assets/POLY STYLE - Vegetation Pack 2/Vegetation Pack 2_URP/Prefabs/Environments/";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabFolder + prefabName);
            if (prefab == null) return;
            var template = CreateTemplate(id, description, concepts);
            entries.Add(new OntologyPlaceableDefinition
            {
                definitionId = id,
                displayName = displayName,
                category = category,
                description = description,
                prefab = prefab,
                ontologyTemplate = template,
                placementPolicy = new OntologyPlacementPolicy()
            });
        }

        private static OntologyMapObjectTemplate CreateTemplate(string id, string description, string[] concepts)
        {
            var path = DataFolder + "/" + id + "Template.asset";
            var template = AssetDatabase.LoadAssetAtPath<OntologyMapObjectTemplate>(path);
            if (template != null) return template;
            template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            template.description = description; template.concepts = concepts;
            AssetDatabase.CreateAsset(template, path);
            return template;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }

        private static GameObject EnsurePanelPrefab(GameObject sceneHud)
        {
            EnsureFolder("Assets/Data"); EnsureFolder("Assets/Data/Ontology"); EnsureFolder(UiDataFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (prefab != null) return prefab;
            if (sceneHud == null) return null;
            return PrefabUtility.SaveAsPrefabAsset(sceneHud, PanelPrefabPath);
        }

        private static OntologyObjectPlacementUiTheme EnsureTheme()
        {
            EnsureFolder("Assets/Data"); EnsureFolder("Assets/Data/Ontology"); EnsureFolder(UiDataFolder);
            var theme = AssetDatabase.LoadAssetAtPath<OntologyObjectPlacementUiTheme>(ThemePath);
            if (theme != null) return theme;
            theme = ScriptableObject.CreateInstance<OntologyObjectPlacementUiTheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
            return theme;
        }






























    }
}
