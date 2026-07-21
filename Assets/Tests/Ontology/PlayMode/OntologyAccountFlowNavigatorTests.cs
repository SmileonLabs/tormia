using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAccountFlowNavigatorTests
    {
        [UnityTest]
        public IEnumerator CompleteReviewSequenceKeepsExactlyOneStepVisible()
        {
            var account = CreatePanel<OntologyAccountEntryPanel>("Account");
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var appearance = CreatePanel<OntologyAccountAppearanceReviewPanel>("Appearance");
            var world = CreatePanel<OntologyAccountWorldSelectionPanel>("World");
            var profile = CreatePanel<OntologyAccountProfileReviewPanel>("Profile");
            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();

            SetPrivateField(navigator, "accountEntryPanel", account.Panel);
            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);
            SetPrivateField(navigator, "appearanceReviewPanel", appearance.Panel);
            SetPrivateField(navigator, "worldSelectionPanel", world.Panel);
            SetPrivateField(navigator, "profileReviewPanel", profile.Panel);

            navigator.ShowAccountEntry();
            AssertOnlyVisible(account.Group, account.Group, character.Group, appearance.Group, world.Group, profile.Group);
            navigator.ShowCharacterSelection();
            AssertOnlyVisible(character.Group, account.Group, character.Group, appearance.Group, world.Group, profile.Group);
            navigator.ShowAppearanceReview();
            AssertOnlyVisible(appearance.Group, account.Group, character.Group, appearance.Group, world.Group, profile.Group);
            navigator.ShowWorldSelection();
            AssertOnlyVisible(world.Group, account.Group, character.Group, appearance.Group, world.Group, profile.Group);
            navigator.ShowProfileReview();
            AssertOnlyVisible(profile.Group, account.Group, character.Group, appearance.Group, world.Group, profile.Group);
            navigator.CloseAll();
            AssertOnlyVisible(null, account.Group, character.Group, appearance.Group, world.Group, profile.Group);

            Object.Destroy(account.Panel.gameObject);
            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(appearance.Panel.gameObject);
            Object.Destroy(world.Panel.gameObject);
            Object.Destroy(profile.Panel.gameObject);
            Object.Destroy(navigatorObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AppearanceStepOpensOnlyWhenPanelIsPresent()
        {
            var account = CreatePanel<OntologyAccountEntryPanel>("Account");
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var appearance = CreatePanel<OntologyAccountAppearanceReviewPanel>("Appearance");
            var world = CreatePanel<OntologyAccountWorldSelectionPanel>("World");
            var profile = CreatePanel<OntologyAccountProfileReviewPanel>("Profile");
            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();

            SetPrivateField(navigator, "accountEntryPanel", account.Panel);
            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);
            SetPrivateField(navigator, "appearanceReviewPanel", appearance.Panel);
            SetPrivateField(navigator, "worldSelectionPanel", world.Panel);
            SetPrivateField(navigator, "profileReviewPanel", profile.Panel);

            navigator.ShowAppearanceReview();

            AssertVisible(appearance.Group, true);
            AssertVisible(account.Group, false);
            AssertVisible(character.Group, false);
            AssertVisible(world.Group, false);
            AssertVisible(profile.Group, false);

            Object.Destroy(appearance.Panel.gameObject);
            yield return null;

            account.Group.alpha = 1f;
            character.Group.alpha = 1f;
            world.Group.alpha = 1f;
            profile.Group.alpha = 1f;

            Assert.DoesNotThrow(navigator.ShowAppearanceReview);
            AssertVisible(account.Group, false);
            AssertVisible(character.Group, false);
            AssertVisible(world.Group, false);
            AssertVisible(profile.Group, false);

            Object.Destroy(account.Panel.gameObject);
            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(world.Panel.gameObject);
            Object.Destroy(profile.Panel.gameObject);
            Object.Destroy(navigatorObject);
            yield return null;
        }

        private static (T Panel, CanvasGroup Group) CreatePanel<T>(string name)
            where T : MonoBehaviour
        {
            var value = new GameObject(name);
            value.SetActive(false);
            var group = value.AddComponent<CanvasGroup>();
            var panel = value.AddComponent<T>();
            SetPrivateField(panel, "panelGroup", group);
            return (panel, group);
        }

        private static void AssertVisible(CanvasGroup group, bool expected)
        {
            Assert.That(group.alpha, Is.EqualTo(expected ? 1f : 0f));
            Assert.That(group.interactable, Is.EqualTo(expected));
            Assert.That(group.blocksRaycasts, Is.EqualTo(expected));
        }

        private static void AssertOnlyVisible(
            CanvasGroup expected,
            params CanvasGroup[] groups)
        {
            foreach (var group in groups)
                AssertVisible(group, group == expected);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            target.GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}
