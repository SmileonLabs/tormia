using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    public static class TormiaProjectSetup
    {
        public const string SourceScenePath = "Assets/ithappy/Creative_Characters_FREE/Scenes/Demonstration.unity";
        public const string MainScenePath = "Assets/Scenes/TormiaMain.unity";

        [MenuItem("Tools/Tormia/Project/Ensure Main Scene")]
        public static void EnsureMainScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                throw new InvalidOperationException($"Source scene was not found: {SourceScenePath}");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null)
            {
                EnsureFolder("Assets/Scenes");
                if (!AssetDatabase.CopyAsset(SourceScenePath, MainScenePath))
                {
                    throw new InvalidOperationException($"Failed to create main scene: {MainScenePath}");
                }
            }

            var otherScenes = EditorBuildSettings.scenes
                .Where(scene => !string.Equals(scene.path, MainScenePath, StringComparison.OrdinalIgnoreCase))
                .Where(scene => !string.Equals(scene.path, SourceScenePath, StringComparison.OrdinalIgnoreCase));

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) }
                .Concat(otherScenes)
                .ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log($"[TormiaProjectSetup] Main scene is ready: {MainScenePath}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separator = path.LastIndexOf('/');
            var parent = path.Substring(0, separator);
            var child = path.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
