using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Isolates the copied CasualWorld from TerrainDemoScene source data before
    /// visual styling work begins. This prevents TerrainData edits from leaking
    /// back into the original demo scene.
    /// </summary>
    public static class OntologyCasualWorldStyleSetup
    {
        private const string CasualWorldScenePath = "Assets/Scenes/CasualWorld.unity";
        private const string TerrainDataFolder = "Assets/Data/CasualWorld/TerrainData";

        [MenuItem("Tools/Ontology/Casual World/Isolate Terrain Data")]
        public static void IsolateTerrainData()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != CasualWorldScenePath)
            {
                EditorUtility.DisplayDialog("Casual World", "Open CasualWorld before isolating its Terrain data.", "OK");
                return;
            }

            EnsureFolder("Assets/Data");
            EnsureFolder("Assets/Data/CasualWorld");
            EnsureFolder(TerrainDataFolder);

            var clonedCount = 0;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;

                var targetPath = TerrainDataFolder + "/" + terrain.name + ".asset";
                var isolatedData = AssetDatabase.LoadAssetAtPath<TerrainData>(targetPath);
                if (isolatedData == null)
                {
                    var sourcePath = AssetDatabase.GetAssetPath(terrain.terrainData);
                    if (string.IsNullOrWhiteSpace(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, targetPath))
                    {
                        Debug.LogError("Could not copy TerrainData for " + terrain.name);
                        continue;
                    }

                    isolatedData = AssetDatabase.LoadAssetAtPath<TerrainData>(targetPath);
                    clonedCount++;
                }

                Undo.RecordObject(terrain, "Assign CasualWorld Terrain Data");
                terrain.terrainData = isolatedData;
                EditorUtility.SetDirty(terrain);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Casual World",
                clonedCount + " TerrainData asset(s) copied for CasualWorld. Future terrain styling will not alter TerrainDemoScene.",
                "OK");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
