using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Tormia.Ontology.Editor
{
    /// <summary>
    /// Applies the project-owned Jua typography policy without touching layout,
    /// hierarchy, colors, alignment, or third-party assets. Interactive text is
    /// enlarged only while its font is migrated, making the operation idempotent.
    /// </summary>
    public static class OntologyJuaTypographyMigration
    {
        public const string SourceFontPath =
            "Assets/UI/Fonts/Jua-Regular.ttf";
        public const string FontAssetPath =
            "Assets/UI/Fonts/Jua-Regular SDF.asset";
        public const string FallbackSourceFontAssetPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/" +
            "LiberationSans SDF.asset";
        public const string FallbackFontAssetPath =
            "Assets/UI/Fonts/TOV Symbol Fallback SDF.asset";
        public const float InteractivePointIncrease = 2f;

        public static readonly string[] PrefabSearchRoots =
        {
            "Assets/Prefabs/Ontology/UI",
            "Assets/Data/Ontology/UI"
        };

        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/TormiaBootstrap.unity",
            "Assets/Scenes/TormiaUI.unity",
            "Assets/Scenes/TormiaWorld.unity"
        };

        [MenuItem("Tools/Ontology/UI/Apply Jua Typography")]
        public static void ApplyProjectTypography()
        {
            if (Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt)
                .Any(scene => scene.isDirty))
            {
                throw new InvalidOperationException(
                    "Save the currently open scene changes before applying " +
                    "the project typography migration.");
            }

            var font = EnsureFontAsset();
            ApplyTmpDefaultFont(font);

            var changedPrefabs = 0;
            var changedTexts = 0;
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:Prefab",
                         PrefabSearchRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                var changed = ApplyToHierarchy(root, font);
                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changedPrefabs++;
                    changedTexts += changed;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var changedScenes = 0;
            try
            {
                foreach (var path in ScenePaths.Where(
                             value => AssetDatabase.LoadAssetAtPath<SceneAsset>(
                                 value) != null))
                {
                    var scene = EditorSceneManager.OpenScene(
                        path,
                        OpenSceneMode.Single);
                    var changed = scene.GetRootGameObjects()
                        .Sum(root => ApplyToHierarchy(root, font));
                    if (changed <= 0)
                        continue;

                    EditorSceneManager.SaveScene(scene);
                    changedScenes++;
                    changedTexts += changed;
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[TOV Typography] Jua-Regular applied to " +
                changedTexts + " text component(s), " +
                changedPrefabs + " prefab(s), and " +
                changedScenes + " scene(s). Interactive text increased by " +
                InteractivePointIncrease + "pt exactly once.");
        }

        public static int ApplyToHierarchy(
            GameObject root,
            TMP_FontAsset font)
        {
            if (root == null || font == null)
                return 0;

            var changed = 0;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text.font == font)
                    continue;

                var interactive =
                    text.GetComponentInParent<Selectable>(true) != null;
                Undo.RecordObject(text, "Apply TOV Jua typography");
                text.font = font;
                if (interactive)
                {
                    text.fontSize += InteractivePointIncrease;
                    if (text.enableAutoSizing)
                    {
                        text.fontSizeMin += InteractivePointIncrease;
                        text.fontSizeMax += InteractivePointIncrease;
                    }
                }
                EditorUtility.SetDirty(text);
                changed++;
            }
            return changed;
        }

        private static TMP_FontAsset EnsureFontAsset()
        {
            var existing =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                EnsureMissingGlyphFallback(existing);
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Jua source font was not found at " + SourceFontPath + ".");
            }

            var font = TMP_FontAsset.CreateFontAsset(
                source,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (font == null)
                throw new InvalidOperationException(
                    "TextMesh Pro could not create the Jua font asset.");

            font.name = "Jua-Regular SDF";
            AssetDatabase.CreateAsset(font, FontAssetPath);
            if (font.material != null &&
                !AssetDatabase.Contains(font.material))
            {
                font.material.name = "Jua-Regular SDF Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
            }
            foreach (var atlas in font.atlasTextures.Where(value =>
                         value != null && !AssetDatabase.Contains(value)))
            {
                atlas.name = "Jua-Regular SDF Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);
            var created = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                FontAssetPath);
            EnsureMissingGlyphFallback(created);
            return created;
        }

        private static void EnsureMissingGlyphFallback(TMP_FontAsset font)
        {
            if (font == null)
                return;

            var fallback = EnsureProjectSymbolFallback();

            font.fallbackFontAssetTable ??= new System.Collections.Generic
                .List<TMP_FontAsset>();
            font.fallbackFontAssetTable.RemoveAll(value =>
                value == null ||
                AssetDatabase.GetAssetPath(value) ==
                FallbackSourceFontAssetPath);
            if (!font.fallbackFontAssetTable.Contains(fallback))
                font.fallbackFontAssetTable.Insert(0, fallback);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset EnsureProjectSymbolFallback()
        {
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                FallbackFontAssetPath);
            if (fallback == null)
            {
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                        FallbackSourceFontAssetPath) == null ||
                    !AssetDatabase.CopyAsset(
                        FallbackSourceFontAssetPath,
                        FallbackFontAssetPath))
                {
                    throw new InvalidOperationException(
                        "TOV typography fallback font could not be created " +
                        "from " + FallbackSourceFontAssetPath + ".");
                }

                AssetDatabase.ImportAsset(FallbackFontAssetPath);
                fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    FallbackFontAssetPath);
            }

            if (fallback == null)
                throw new InvalidOperationException(
                    "TOV typography fallback font was not found at " +
                    FallbackFontAssetPath + ".");

            fallback.name = "TOV Symbol Fallback SDF";
            var weights = fallback.fontWeightTable;
            if (weights == null || weights.Length < 10)
                throw new InvalidOperationException(
                    "TOV symbol fallback has an invalid TMP weight table.");

            // TMP resolves overflow symbols with the active style and weight.
            // Point every synthetic weight back to this project-owned symbol
            // asset so a bold Jua label can still resolve U+2026.
            for (var index = 0; index < weights.Length; index++)
            {
                weights[index].regularTypeface = fallback;
                weights[index].italicTypeface = fallback;
            }
            EditorUtility.SetDirty(fallback);
            AssetDatabase.SaveAssets();
            return fallback;
        }

        private static void ApplyTmpDefaultFont(TMP_FontAsset font)
        {
            if (font == null || TMP_Settings.defaultFontAsset == font)
                return;

            TMP_Settings.defaultFontAsset = font;
            if (TMP_Settings.instance != null)
                EditorUtility.SetDirty(TMP_Settings.instance);
        }
    }
}
