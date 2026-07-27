using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldPlacementUiTests
    {
        private const string PrefabPath = "Assets/Data/Ontology/UI/ObjectPlacementHUD.prefab";

        [Test]
        public void WorldPlacementPrefab_UsesEditableUnifiedModesAndScrollableData()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Assert.That(prefab.transform.Find("ModeTabs/ObjectTab"), Is.Not.Null);
            Assert.That(prefab.transform.Find("ModeTabs/NpcTab"), Is.Not.Null);
            Assert.That(prefab.transform.Find("ModeTabs/MonsterTab"), Is.Not.Null);
            Assert.That(
                prefab.transform.Find("ModeTabs/ObjectTab/Icon/ObjectSprite")
                    ?.GetComponent<Image>()?.sprite,
                Is.Not.Null);
            Assert.That(
                prefab.transform.Find("ModeTabs/NpcTab/Icon/NpcSprite")
                    ?.GetComponent<Image>()?.sprite,
                Is.Not.Null);
            Assert.That(
                prefab.transform.Find("ModeTabs/MonsterTab/Icon/MonsterSprite")
                    ?.GetComponent<Image>()?.sprite,
                Is.Not.Null);
            Assert.That(prefab.transform.Find("PreviewFrame/Previous/ArrowUpper"), Is.Not.Null);
            Assert.That(prefab.transform.Find("PreviewFrame/Previous/ArrowLower"), Is.Not.Null);
            Assert.That(prefab.transform.Find("PreviewFrame/Next/ArrowUpper"), Is.Not.Null);
            Assert.That(prefab.transform.Find("PreviewFrame/Next/ArrowLower"), Is.Not.Null);

            var objectSelected = prefab.transform.Find("ModeTabs/ObjectTab/SelectedState");
            var npcSelected = prefab.transform.Find("ModeTabs/NpcTab/SelectedState");
            Assert.That(objectSelected, Is.Not.Null);
            Assert.That(objectSelected.gameObject.activeSelf, Is.True);
            Assert.That(npcSelected, Is.Not.Null);
            Assert.That(npcSelected.gameObject.activeSelf, Is.False);

            var categoryScroll = prefab.transform
                .Find("Categories/CategoryScroll")?.GetComponent<ScrollRect>();
            var ontologyScroll = prefab.transform
                .Find("OntologyPanel/OntologyScroll")?.GetComponent<ScrollRect>();
            Assert.That(categoryScroll, Is.Not.Null);
            Assert.That(categoryScroll.vertical, Is.True);
            Assert.That(categoryScroll.verticalScrollbar, Is.Null);
            Assert.That(ontologyScroll, Is.Not.Null);
            Assert.That(ontologyScroll.vertical, Is.True);
            Assert.That(ontologyScroll.verticalScrollbar, Is.Null);

            Assert.That(prefab.transform.Find("CloseIconButton"), Is.Not.Null);
            Assert.That(prefab.transform.Find("Close"), Is.Null);
            Assert.That(prefab.transform.Find("Status"), Is.Null);
            var footer = prefab.transform.Find("FooterHints")?.GetComponent<TMP_Text>();
            Assert.That(footer, Is.Not.Null);
            Assert.That(footer.text, Does.Not.Contain("B"));
        }

        [Test]
        public void PlaceableDefinition_DefaultsToObjectWithoutNameBasedInference()
        {
            var definition = new Tormia.Ontology.Core.OntologyPlaceableDefinition();
            Assert.That(
                definition.placementKind,
                Is.EqualTo(Tormia.Ontology.Core.OntologyPlaceableKind.Object));
        }
    }
}
