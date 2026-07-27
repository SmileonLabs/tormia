using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    public static class TormiaProjectSetup
    {
        public const string BootstrapScenePath = "Assets/Scenes/TormiaBootstrap.unity";
        public const string WorldScenePath = "Assets/Scenes/TormiaWorld.unity";
        public const string UiScenePath = "Assets/Scenes/TormiaUI.unity";

        [MenuItem("Tools/Tormia/Project/Ensure Scene Composition")]
        public static void EnsureSceneComposition()
        {
            var requiredScenePaths = new[]
            {
                BootstrapScenePath,
                WorldScenePath,
                UiScenePath
            };
            var missingScenePaths = requiredScenePaths
                .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                .ToArray();
            if (missingScenePaths.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Scene composition is incomplete: {string.Join(", ", missingScenePaths)}");
            }

            EditorBuildSettings.scenes = requiredScenePaths
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log(
                "[TormiaProjectSetup] Scene composition is ready: " +
                "TormiaBootstrap + TormiaWorld + TormiaUI.");
        }
    }
}
