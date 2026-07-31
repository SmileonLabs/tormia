using System.Linq;
using NUnit.Framework;
using TMPro;
using Tormia.Ontology.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyJuaTypographyTests
    {
        [Test]
        public void JuaResolvesEllipsisThroughProjectFallback()
        {
            var jua = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                OntologyJuaTypographyMigration.FontAssetPath);
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                OntologyJuaTypographyMigration.FallbackFontAssetPath);

            Assert.That(jua, Is.Not.Null);
            Assert.That(fallback, Is.Not.Null);
            Assert.That(
                jua.fallbackFontAssetTable,
                Does.Contain(fallback),
                "Jua must keep a fallback for glyphs absent from its source " +
                "font, including TMP's ellipsis character.");

            foreach (var style in new[]
                     {
                         FontStyles.Normal,
                         FontStyles.Bold,
                         FontStyles.Italic,
                         FontStyles.Bold | FontStyles.Italic
                     })
            foreach (var weight in new[]
                     {
                         FontWeight.Thin,
                         FontWeight.Regular,
                         FontWeight.Bold,
                         FontWeight.Black
                     })
            {
                bool alternative;
                Assert.That(
                    TMP_FontAssetUtilities.GetCharacterFromFontAsset(
                        '\u2026',
                        jua,
                        true,
                        style,
                        weight,
                        out alternative),
                    Is.Not.Null,
                    "Ellipsis must resolve for " + style + "/" + weight +
                    ".");
            }
        }

        [Test]
        public void ProjectOwnedUiAssetsUseJua()
        {
            var jua = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                OntologyJuaTypographyMigration.FontAssetPath);
            Assert.That(jua, Is.Not.Null);
            Assert.That(TMP_Settings.defaultFontAsset, Is.SameAs(jua));

            foreach (var guid in AssetDatabase.FindAssets(
                         "t:Prefab",
                         OntologyJuaTypographyMigration.PrefabSearchRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Assert.That(
                        root.GetComponentsInChildren<TMP_Text>(true)
                            .All(value => value.font == jua),
                        Is.True,
                        path + " contains a non-Jua TMP text.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var hadLoadedScene = setup.Any(value => value.isLoaded);
            try
            {
                foreach (var path in new[]
                         {
                             "Assets/Scenes/TormiaBootstrap.unity",
                             "Assets/Scenes/TormiaUI.unity",
                             "Assets/Scenes/TormiaWorld.unity"
                         })
                {
                    var scene = EditorSceneManager.OpenScene(
                        path,
                        OpenSceneMode.Single);
                    Assert.That(
                        scene.GetRootGameObjects()
                            .SelectMany(root =>
                                root.GetComponentsInChildren<TMP_Text>(true))
                            .All(value => value.font == jua),
                        Is.True,
                        path + " contains a non-Jua TMP text.");
                }
            }
            finally
            {
                if (hadLoadedScene)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
                else
                {
                    EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Single);
                }
            }
        }

        [Test]
        public void InteractiveTextGetsTwoPointIncreaseExactlyOnce()
        {
            var jua = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                OntologyJuaTypographyMigration.FontAssetPath);
            var previous = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/" +
                "LiberationSans SDF.asset");
            Assert.That(jua, Is.Not.Null);
            Assert.That(previous, Is.Not.Null);

            var root = new GameObject("TypographyTest", typeof(RectTransform));
            var button = new GameObject(
                "Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            button.transform.SetParent(root.transform, false);
            var label = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(button.transform, false);
            label.font = previous;
            label.fontSize = 14f;

            Assert.That(
                OntologyJuaTypographyMigration.ApplyToHierarchy(root, jua),
                Is.EqualTo(1));
            Assert.That(label.font, Is.SameAs(jua));
            Assert.That(label.fontSize, Is.EqualTo(16f));
            Assert.That(
                OntologyJuaTypographyMigration.ApplyToHierarchy(root, jua),
                Is.EqualTo(0));
            Assert.That(label.fontSize, Is.EqualTo(16f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void NonInteractiveTextKeepsItsExistingPointSize()
        {
            var jua = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                OntologyJuaTypographyMigration.FontAssetPath);
            var previous = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/" +
                "LiberationSans SDF.asset");
            var root = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            var label = root.GetComponent<TextMeshProUGUI>();
            label.font = previous;
            label.fontSize = 18f;

            OntologyJuaTypographyMigration.ApplyToHierarchy(root, jua);

            Assert.That(label.font, Is.SameAs(jua));
            Assert.That(label.fontSize, Is.EqualTo(18f));
            Object.DestroyImmediate(root);
        }
    }
}
