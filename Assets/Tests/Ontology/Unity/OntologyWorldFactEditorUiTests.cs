using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldFactEditorUiTests
    {
        private const string PrefabPath =
            "Assets/Prefabs/Ontology/UI/WorldEditHUD.prefab";

        [Test]
        public void WorldFactEditorPrefab_UsesEditableTabStatesAndCommonPageStyle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            var tabNames = new[]
            {
                "TripleTabButton",
                "RuleTabButton",
                "PhysicalTabButton",
                "ResultTabButton"
            };

            foreach (var tabName in tabNames)
            {
                var tab = prefab.transform.Find("ModeTabs/" + tabName);
                Assert.That(tab, Is.Not.Null, tabName);
                Assert.That(
                    tab.GetComponent<OntologyTabVisualState>(),
                    Is.Not.Null,
                    tabName + " must expose editable selected/unselected visuals.");
                Assert.That(tab.Find("TabIcon")?.GetComponent<Image>()?.sprite,
                    Is.Not.Null,
                    tabName + " must keep its icon as a hierarchy-owned Sprite.");
            }

            Sprite sharedPageSprite = null;
            var pageNames = new[]
            {
                "TripleScrollView",
                "PhysicalScrollView",
                "ResultScrollView"
            };

            foreach (var pageName in pageNames)
            {
                var page = prefab.transform.Find(pageName);
                Assert.That(page, Is.Not.Null, pageName);
                var image = page.GetComponent<Image>();
                Assert.That(image, Is.Not.Null);
                Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(image.sprite, Is.Not.Null);
                sharedPageSprite ??= image.sprite;
                Assert.That(image.sprite, Is.SameAs(sharedPageSprite),
                    "Every tab page must use the common TOV content-card style.");

                var scroll = page.GetComponent<ScrollRect>();
                Assert.That(scroll, Is.Not.Null);
                Assert.That(scroll.vertical, Is.True);
            }
        }

        [Test]
        public void TabVisualState_SwitchesBackgroundIconAndLabelWithoutRebuildingHierarchy()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var tab = prefab.transform.Find("ModeTabs/TripleTabButton");
                var visual = tab.GetComponent<OntologyTabVisualState>();
                var image = tab.GetComponent<Image>();
                var icon = tab.Find("TabIcon").GetComponent<Image>();
                var childCount = tab.childCount;

                visual.SetSelected(false);
                var unselectedSprite = image.sprite;
                var unselectedIconColor = icon.color;
                visual.SetSelected(true);

                Assert.That(visual.IsSelected, Is.True);
                Assert.That(image.sprite, Is.Not.SameAs(unselectedSprite));
                Assert.That(icon.color, Is.Not.EqualTo(unselectedIconColor));
                Assert.That(tab.childCount, Is.EqualTo(childCount),
                    "State changes must not regenerate designer-authored children.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }
    }
}
