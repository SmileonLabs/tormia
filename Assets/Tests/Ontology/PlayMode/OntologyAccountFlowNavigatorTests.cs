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
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var appearance = CreatePanel<OntologyAccountAppearanceReviewPanel>("Appearance");
            var world = CreatePanel<OntologyAccountWorldSelectionPanel>("World");
            var profile = CreatePanel<OntologyAccountProfileReviewPanel>("Profile");
            var loading = CreatePanel<OntologyWorldEntryLoadingPanel>("Loading");
            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();

            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);
            SetPrivateField(navigator, "appearanceReviewPanel", appearance.Panel);
            SetPrivateField(navigator, "worldSelectionPanel", world.Panel);
            SetPrivateField(navigator, "profileReviewPanel", profile.Panel);
            SetPrivateField(navigator, "worldEntryLoadingPanel", loading.Panel);

            navigator.ShowCharacterSelection();
            Assert.That(character.Panel.gameObject.activeSelf, Is.True,
                "Navigation must reactivate an authored panel that was hidden in the hierarchy.");
            AssertOnlyVisible(character.Group, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);
            navigator.ShowAppearanceReview();
            AssertOnlyVisible(world.Group, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);
            navigator.ShowWorldSelection();
            AssertOnlyVisible(world.Group, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);
            navigator.ShowProfileReview();
            AssertOnlyVisible(profile.Group, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);
            navigator.ShowWorldEntryLoading();
            AssertOnlyVisible(loading.Group, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);
            navigator.CloseAll();
            AssertOnlyVisible(null, character.Group, appearance.Group, world.Group, profile.Group, loading.Group);

            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(appearance.Panel.gameObject);
            Object.Destroy(world.Panel.gameObject);
            Object.Destroy(profile.Panel.gameObject);
            Object.Destroy(loading.Panel.gameObject);
            Object.Destroy(navigatorObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AppearanceRouteSkipsRemovedPanelAndOpensWorldSelection()
        {
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var appearance = CreatePanel<OntologyAccountAppearanceReviewPanel>("Appearance");
            var world = CreatePanel<OntologyAccountWorldSelectionPanel>("World");
            var profile = CreatePanel<OntologyAccountProfileReviewPanel>("Profile");
            var loading = CreatePanel<OntologyWorldEntryLoadingPanel>("Loading");
            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();

            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);
            SetPrivateField(navigator, "appearanceReviewPanel", appearance.Panel);
            SetPrivateField(navigator, "worldSelectionPanel", world.Panel);
            SetPrivateField(navigator, "profileReviewPanel", profile.Panel);
            SetPrivateField(navigator, "worldEntryLoadingPanel", loading.Panel);

            navigator.ShowAppearanceReview();

            AssertVisible(appearance.Group, false);
            AssertVisible(character.Group, false);
            AssertVisible(world.Group, true);
            AssertVisible(profile.Group, false);
            AssertVisible(loading.Group, false);

            Object.Destroy(appearance.Panel.gameObject);
            yield return null;

            character.Group.alpha = 1f;
            world.Group.alpha = 1f;
            profile.Group.alpha = 1f;
            loading.Group.alpha = 1f;

            Assert.DoesNotThrow(navigator.ShowAppearanceReview);
            AssertVisible(character.Group, false);
            AssertVisible(world.Group, true);
            AssertVisible(profile.Group, false);
            AssertVisible(loading.Group, false);

            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(world.Panel.gameObject);
            Object.Destroy(profile.Panel.gameObject);
            Object.Destroy(loading.Panel.gameObject);
            Object.Destroy(navigatorObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CharacterCreationBackReturnsExistingAccountToCharacterSelection()
        {
            var login = CreatePanel<OntologyAccountLoginPanel>("Login");
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var stateObject = new GameObject("AccountState");
            var client = stateObject.AddComponent<OntologyWorldAuthorityClient>();
            var flow = stateObject.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
            SetPrivateField(client, "currentAccount", new OntologyAuthorityAccountDashboard
            {
                characters = new[]
                {
                    new OntologyAuthorityPlayerCharacter
                    {
                        characterId = "character-1",
                        displayName = "Existing Character"
                    }
                }
            });

            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();
            SetPrivateField(navigator, "entryFlow", flow);
            SetPrivateField(navigator, "accountLoginPanel", login.Panel);
            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);

            navigator.BackFromCharacterCreation();

            AssertVisible(login.Group, false);
            AssertVisible(character.Group, true);

            Object.Destroy(login.Panel.gameObject);
            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(stateObject);
            Object.Destroy(navigatorObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CharacterCreationBackSignsOutEmptyAccountAndReturnsToLogin()
        {
            var login = CreatePanel<OntologyAccountLoginPanel>("Login");
            var character = CreatePanel<OntologyAccountCharacterSelectionPanel>("Character");
            var stateObject = new GameObject("AccountState");
            var client = stateObject.AddComponent<OntologyWorldAuthorityClient>();
            var flow = stateObject.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
            SetPrivateField(client, "currentAccount", new OntologyAuthorityAccountDashboard
            {
                characters = System.Array.Empty<OntologyAuthorityPlayerCharacter>()
            });

            var navigatorObject = new GameObject("Navigator");
            navigatorObject.SetActive(false);
            var navigator = navigatorObject.AddComponent<OntologyAccountFlowNavigator>();
            SetPrivateField(navigator, "entryFlow", flow);
            SetPrivateField(navigator, "accountLoginPanel", login.Panel);
            SetPrivateField(navigator, "characterSelectionPanel", character.Panel);

            navigator.BackFromCharacterCreation();

            AssertVisible(login.Group, true);
            AssertVisible(character.Group, false);
            yield return null;
            Assert.That(client.CurrentAccount, Is.Null);

            Object.Destroy(login.Panel.gameObject);
            Object.Destroy(character.Panel.gameObject);
            Object.Destroy(stateObject);
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
