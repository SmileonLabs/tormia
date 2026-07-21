using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Creates a safe working copy of the imported Terrain demo.  The original
    /// remains untouched so it can always be used as a technical reference.
    /// </summary>
    public static class OntologyCasualWorldSetup
    {
        private const string SourceScenePath = "Assets/TerrainDemoScene_URP/Scenes/TerrainDemoScene.unity";
        private const string TargetScenePath = "Assets/Scenes/CasualWorld.unity";

        [MenuItem("Tools/Ontology/Create Casual World")]
        public static void CreateCasualWorld()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath))
            {
                EditorUtility.DisplayDialog("Casual World", "The Terrain Demo source scene could not be found.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath))
            {
                var openExisting = EditorUtility.DisplayDialog(
                    "Casual World already exists",
                    "The existing CasualWorld scene will not be overwritten. Open it instead?",
                    "Open Existing",
                    "Cancel");
                if (openExisting) EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
            {
                EditorUtility.DisplayDialog("Casual World", "Unity could not create the CasualWorld scene copy.", "OK");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            EditorUtility.DisplayDialog(
                "Casual World created",
                "CasualWorld is now a working copy. The original TerrainDemoScene remains unchanged.",
                "OK");
        }
    }
}
