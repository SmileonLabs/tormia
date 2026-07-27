using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldAuthorityRoleTests
    {
        [TestCase("owner", true)]
        [TestCase("editor", true)]
        [TestCase("viewer", false)]
        public void SelectedWorldRoleControlsDurableAuthoring(
            string role,
            bool expectedCanEdit)
        {
            var gameObject = new GameObject("AuthorityRoleTest");
            try
            {
                var client = gameObject.AddComponent<OntologyWorldAuthorityClient>();
                var userId = Guid.NewGuid().ToString();
                var worldId = Guid.NewGuid().ToString();
                SetField(client, "currentUserId", userId);
                SetField(client, "currentWorldId", worldId);
                SetField(client, "hasEnteredCurrentWorld", true);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    worlds = new[]
                    {
                        new OntologyAuthorityAccountWorld
                        {
                            worldId = worldId,
                            role = role
                        }
                    }
                });

                Assert.That(client.CurrentWorldRole, Is.EqualTo(role));
                Assert.That(client.CanEditCurrentWorld, Is.EqualTo(expectedCanEdit));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SelectedWorldDoesNotEnableRuntimeAuthoringBeforeEntry()
        {
            var gameObject = new GameObject("AuthorityEntryGateTest");
            try
            {
                var client = gameObject.AddComponent<OntologyWorldAuthorityClient>();
                var worldId = Guid.NewGuid().ToString();
                SetField(client, "currentUserId", Guid.NewGuid().ToString());
                SetField(client, "currentWorldId", worldId);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    worlds = new[]
                    {
                        new OntologyAuthorityAccountWorld { worldId = worldId, role = "owner" }
                    }
                });

                Assert.That(client.IsReady, Is.True);
                Assert.That(client.IsWorldRuntimeReady, Is.False);
                Assert.That(client.CanAuthorSelectedWorld, Is.True);
                Assert.That(client.CanEditCurrentWorld, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ExecuteActionPayloadContainsOnlyAuthorityIntentIdentity()
        {
            var actor = Guid.NewGuid();
            var target = Guid.NewGuid();
            var json = OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                actor, target, null, "social_village", "1.0.0", "help", 1);

            Assert.That(json, Does.Contain("\"actorEntityId\""));
            Assert.That(json, Does.Contain(actor.ToString("D")));
            Assert.That(json, Does.Contain(target.ToString("D")));
            Assert.That(json, Does.Contain("\"packageId\":\"social_village\""));
            Assert.That(json, Does.Contain("\"actionId\":\"help\""));
            Assert.That(json, Does.Not.Contain("predicate"));
            Assert.That(json, Does.Not.Contain("effect"));
        }

        [Test]
        public void WorldSelectionHidesAuthoredSlotsWhenAccountHasNoWorlds()
        {
            var root = new GameObject(
                "WorldSelectionWithoutWorlds",
                typeof(RectTransform),
                typeof(CanvasGroup));
            root.SetActive(false);
            try
            {
                var panel = root.AddComponent<OntologyAccountWorldSelectionPanel>();
                var client = root.AddComponent<OntologyWorldAuthorityClient>();
                var entryFlow = root.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
                var cards = new OntologyAccountWorldCard[3];
                for (var index = 0; index < cards.Length; index++)
                {
                    var cardObject = new GameObject(
                        "WorldCardSlot" + (index + 1),
                        typeof(RectTransform),
                        typeof(Image),
                        typeof(Button));
                    cardObject.transform.SetParent(root.transform, false);
                    cards[index] = cardObject.AddComponent<OntologyAccountWorldCard>();
                }

                var continueObject = new GameObject(
                    "Continue",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));
                continueObject.transform.SetParent(root.transform, false);
                var continueButton = continueObject.GetComponent<Button>();
                continueButton.interactable = true;

                var emptyWorldHero = new GameObject("EmptyWorldHero");
                emptyWorldHero.transform.SetParent(root.transform, false);

                SetField(entryFlow, "authorityClient", client);
                SetField(panel, "entryFlow", entryFlow);
                SetField(panel, "worldCards", cards);
                SetField(panel, "emptyWorldHero", emptyWorldHero);
                SetField(panel, "continueButton", continueButton);
                panel.Refresh();

                Assert.That(emptyWorldHero.activeSelf, Is.True);
                foreach (var card in cards)
                    Assert.That(card.gameObject.activeSelf, Is.False);
                Assert.That(continueButton.interactable, Is.False);

                var characterId = Guid.NewGuid().ToString();
                var worldId = Guid.NewGuid().ToString();
                SetField(client, "currentUserId", Guid.NewGuid().ToString());
                SetField(client, "currentCharacterId", characterId);
                SetField(client, "currentWorldId", worldId);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    characters = new[]
                    {
                        new OntologyAuthorityPlayerCharacter
                        {
                            characterId = characterId,
                            displayName = "Builder"
                        }
                    },
                    worlds = new[]
                    {
                        new OntologyAuthorityAccountWorld
                        {
                            worldId = worldId,
                            title = "Created World",
                            role = "owner"
                        }
                    }
                });
                panel.Refresh();

                Assert.That(emptyWorldHero.activeSelf, Is.False);
                Assert.That(cards[0].gameObject.activeSelf, Is.True);
                Assert.That(cards[1].gameObject.activeSelf, Is.False);
                Assert.That(cards[2].gameObject.activeSelf, Is.False);
                Assert.That(continueButton.interactable, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldSelectionOpenResolvesAccountFlowLoadedAfterUiPanel()
        {
            var panelObject = new GameObject(
                "LateBoundWorldSelection",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var authorityObject = new GameObject("LateBoundWorldAuthority");
            panelObject.SetActive(false);
            authorityObject.SetActive(false);
            try
            {
                var panel = panelObject.AddComponent<OntologyAccountWorldSelectionPanel>();
                var cardObject = new GameObject(
                    "WorldCardSlot1",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));
                cardObject.transform.SetParent(panelObject.transform, false);
                var card = cardObject.AddComponent<OntologyAccountWorldCard>();
                var emptyWorldHero = new GameObject("EmptyWorldHero");
                emptyWorldHero.transform.SetParent(panelObject.transform, false);
                var continueObject = new GameObject(
                    "Continue",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Button));
                continueObject.transform.SetParent(panelObject.transform, false);

                SetField(panel, "worldCards", new[] { card });
                SetField(panel, "emptyWorldHero", emptyWorldHero);
                SetField(panel, "continueButton", continueObject.GetComponent<Button>());
                panelObject.SetActive(true);
                panel.Open();
                Assert.That(panel.EntryFlow, Is.Null);

                var client = authorityObject.AddComponent<OntologyWorldAuthorityClient>();
                var entryFlow = authorityObject.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
                var characterId = Guid.NewGuid().ToString();
                var worldId = Guid.NewGuid().ToString();
                SetField(entryFlow, "authorityClient", client);
                SetField(client, "currentUserId", Guid.NewGuid().ToString());
                SetField(client, "currentCharacterId", characterId);
                SetField(client, "currentWorldId", worldId);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    characters = new[]
                    {
                        new OntologyAuthorityPlayerCharacter
                        {
                            characterId = characterId,
                            displayName = "Builder"
                        }
                    },
                    worlds = new[]
                    {
                        new OntologyAuthorityAccountWorld
                        {
                            worldId = worldId,
                            title = "Newly Created World",
                            role = "owner"
                        }
                    }
                });
                authorityObject.SetActive(true);

                panel.Open();

                Assert.That(panel.EntryFlow, Is.SameAs(entryFlow));
                Assert.That(emptyWorldHero.activeSelf, Is.False);
                Assert.That(card.gameObject.activeSelf, Is.True);
                Assert.That(continueObject.GetComponent<Button>().interactable, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panelObject);
                UnityEngine.Object.DestroyImmediate(authorityObject);
            }
        }

        [Test]
        public void ProfileFinalReviewShowsAuthorityRoleAndRequiresWorldPermission()
        {
            var previousLanguage = OntologyLanguagePackService.CurrentLanguage;
            var authorityObject = new GameObject("ProfileReviewAuthorityTest");
            var panelObject = new GameObject("ProfileReviewPanelTest", typeof(RectTransform), typeof(CanvasGroup));
            authorityObject.SetActive(false);
            panelObject.SetActive(false);
            try
            {
                OntologyLanguagePackService.SetLanguage(OntologyDisplayLanguage.English);
                var client = authorityObject.AddComponent<OntologyWorldAuthorityClient>();
                var entryFlow = authorityObject.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
                var panel = panelObject.AddComponent<OntologyAccountProfileReviewPanel>();
                var worldLabel = CreateText(panelObject.transform, "World");
                var permissionLabel = CreateText(panelObject.transform, "Permission");
                var enterObject = new GameObject("Enter", typeof(RectTransform), typeof(Image), typeof(Button));
                enterObject.transform.SetParent(panelObject.transform, false);
                var enterButton = enterObject.GetComponent<Button>();
                var userId = Guid.NewGuid().ToString();
                var characterId = Guid.NewGuid().ToString();
                var worldId = Guid.NewGuid().ToString();
                var world = new OntologyAuthorityAccountWorld
                {
                    worldId = worldId,
                    title = "Green Valley",
                    role = "owner"
                };

                SetField(client, "currentUserId", userId);
                SetField(client, "currentCharacterId", characterId);
                SetField(client, "currentWorldId", worldId);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    characters = new[]
                    {
                        new OntologyAuthorityPlayerCharacter
                        {
                            characterId = characterId,
                            displayName = "Haneul",
                            templateId = "player"
                        }
                    },
                    worlds = new[] { world }
                });
                SetField(entryFlow, "authorityClient", client);
                SetField(panel, "entryFlow", entryFlow);
                SetField(panel, "selectedWorldLabel", worldLabel);
                SetField(panel, "permissionLabel", permissionLabel);
                SetField(panel, "enterWorldButton", enterButton);

                panel.Refresh();
                Assert.That(worldLabel.text, Is.EqualTo("Green Valley"));
                Assert.That(permissionLabel.text, Does.Contain("Owner"));
                Assert.That(permissionLabel.text, Does.Contain("WORLD EDITING ENABLED"));
                Assert.That(enterButton.interactable, Is.True);

                world.role = "viewer";
                panel.Refresh();
                Assert.That(permissionLabel.text, Does.Contain("Viewer"));
                Assert.That(permissionLabel.text, Does.Contain("Read only"));
                Assert.That(enterButton.interactable, Is.True, "Viewer may enter but cannot author durable world changes.");

                world.role = string.Empty;
                panel.Refresh();
                Assert.That(permissionLabel.text, Is.EqualTo("NO WORLD PERMISSION"));
                Assert.That(enterButton.interactable, Is.False);
            }
            finally
            {
                OntologyLanguagePackService.SetLanguage(previousLanguage);
                UnityEngine.Object.DestroyImmediate(panelObject);
                UnityEngine.Object.DestroyImmediate(authorityObject);
            }
        }

        private static TMP_Text CreateText(Transform parent, string name)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            value.transform.SetParent(parent, false);
            return value.GetComponent<TextMeshProUGUI>();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
