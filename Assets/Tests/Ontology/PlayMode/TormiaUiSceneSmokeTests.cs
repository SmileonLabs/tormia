using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tormia.Ontology.Tests
{
    public sealed class TormiaUiSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator CharacterAppearanceStripUsesCircularMasksAndHiddenHorizontalScroll()
        {
            var operation = SceneManager.LoadSceneAsync(
                "Assets/Scenes/TormiaUI.unity",
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
            yield return null;

            var panel = Object.FindAnyObjectByType<OntologyAccountCharacterSelectionPanel>(
                FindObjectsInactive.Include);
            Assert.That(panel, Is.Not.Null);
            var appearancePanel = panel.transform.Find(
                "SelectedCharacterDetailPanel/CurrentAppearancePanel");
            Assert.That(appearancePanel, Is.Not.Null);
            var scroll = appearancePanel.GetComponent<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.False);
            Assert.That(scroll.horizontalScrollbar, Is.Null);
            Assert.That(scroll.verticalScrollbar, Is.Null);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.content.childCount, Is.EqualTo(13));
            Assert.That(scroll.content.rect.width, Is.GreaterThan(scroll.viewport.rect.width),
                "The authored content must overflow the viewport so horizontal scrolling is available.");

            for (var index = 0; index < scroll.content.childCount; index++)
            {
                var iconFrame = scroll.content.GetChild(index).Find("IconFrame");
                Assert.That(iconFrame, Is.Not.Null);
                Assert.That(iconFrame.GetComponent<Mask>(), Is.Not.Null);
                var icon = (RectTransform)iconFrame.Find("PartIcon");
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(icon.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(icon.offsetMin.x, Is.LessThan(0f));
                Assert.That(icon.offsetMax.x, Is.GreaterThan(0f));
            }
        }

        [UnityTest]
        public IEnumerator CompositionCreatesOntologyWorldAndUsesSingleUiScene()
        {
            var operation = SceneManager.LoadSceneAsync(
                "Assets/Scenes/TormiaBootstrap.unity",
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
            yield return new WaitUntil(() =>
                SceneManager.GetSceneByName("TormiaWorld").isLoaded &&
                SceneManager.GetSceneByName("TormiaUI").isLoaded);
            yield return null;

            var bootstrap = Object.FindFirstObjectByType<OntologyWorldBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);

            bootstrap.ResetWorld(logReport: false);
            Assert.That(bootstrap.World, Is.Not.Null);
            Assert.That(bootstrap.Session, Is.Not.Null);
            Assert.That(bootstrap.World.Facts.Count(), Is.GreaterThan(0));
            Assert.That(bootstrap.GetGeneratedQuests(), Is.Not.Null);
            Assert.That(bootstrap.GetActionCandidates(), Is.Not.Null);

            var gameCanvas = FindSceneObject("OntologyGameCanvas");
            Assert.That(gameCanvas, Is.Not.Null);
            Assert.That(gameCanvas.scene.name, Is.EqualTo("TormiaUI"));
            Assert.That(FindSceneObject("FarmUI_DemoCanvas"), Is.Null,
                "The legacy demo canvas must not return beside the authored TOV UI.");

            var characterSelectionPanel =
                Object.FindAnyObjectByType<OntologyAccountCharacterSelectionPanel>(FindObjectsInactive.Include);
            Assert.That(characterSelectionPanel, Is.Not.Null);
            characterSelectionPanel.Refresh();
            Assert.That(characterSelectionPanel.EntryFlow, Is.Not.Null,
                "The additive UI scene must resolve the account entry flow from the bootstrap scene.");
            Assert.That(characterSelectionPanel.CharacterCards.Count, Is.EqualTo(3),
                "All three authored character slots must keep their runtime data-binding components.");
            foreach (var characterCard in characterSelectionPanel.CharacterCards)
            {
                Assert.That(characterCard.transform.Find("AppearanceSummary"), Is.Null,
                    "Character cards must not show an equipped-part count summary.");
            }
            Assert.That(
                characterSelectionPanel.transform.Find("AccountChip/AccountAvatarFrame"),
                Is.Null,
                "The account chip must not retain a static sample-thumbnail hierarchy.");
            Assert.That(
                characterSelectionPanel.transform
                    .Find("CharacterListPanel/CreateNewCharacterButton/PlusBadge/PlusLabel")
                    .GetComponent<TMP_Text>().text,
                Is.EqualTo("+"),
                "Localization must target the create-button label, not overwrite its plus icon.");
              for (var slotIndex = 1; slotIndex <= 5; slotIndex++)
              {
                  var iconFrame = characterSelectionPanel.transform.Find(
                      "SelectedCharacterDetailPanel/CurrentAppearancePanel/" +
                      "AppearanceViewport/AppearanceContent/AppearanceSlot" +
                      slotIndex + "/IconFrame");
                Assert.That(iconFrame, Is.Not.Null);
                Assert.That(iconFrame.GetComponent<Mask>(), Is.Not.Null,
                    "Equipped-part thumbnails must remain circularly masked.");
                Assert.That(
                    ((RectTransform)iconFrame.Find("PartIcon")).sizeDelta.x,
                    Is.GreaterThan(0f),
                      "Part sprites must overscan the mask so source-image padding is cropped.");
              }
              var appearanceScroll = characterSelectionPanel.transform
                  .Find("SelectedCharacterDetailPanel/CurrentAppearancePanel")
                  .GetComponent<ScrollRect>();
              Assert.That(appearanceScroll, Is.Not.Null);
              Assert.That(appearanceScroll.horizontal, Is.True,
                  "The equipped-part strip must retain horizontal scrolling.");
              Assert.That(appearanceScroll.vertical, Is.False,
                  "The equipped-part strip must scroll horizontally only.");
              Assert.That(appearanceScroll.horizontalScrollbar, Is.Null);
              Assert.That(appearanceScroll.verticalScrollbar, Is.Null);
              Assert.That(appearanceScroll.content.childCount, Is.EqualTo(13),
                  "All supported appearance categories must remain hierarchy-authored and horizontally scrollable.");
            var expectedCharacterCount = Mathf.Min(
                characterSelectionPanel.EntryFlow.Characters.Count,
                characterSelectionPanel.CharacterCards.Count);
            var visibleCharacterCards = characterSelectionPanel.CharacterCards
                .Where(card => card != null && card.gameObject.activeSelf)
                .ToArray();
            Assert.That(visibleCharacterCards.Length, Is.EqualTo(expectedCharacterCount),
                "Authored sample character cards must be replaced by the real account character list.");
            for (var index = 0; index < visibleCharacterCards.Length; index++)
            {
                Assert.That(
                    visibleCharacterCards[index].CharacterId,
                    Is.EqualTo(characterSelectionPanel.EntryFlow.Characters[index].characterId),
                    "Each visible card must be bound to the matching Authority character id.");
            }

            var authorityClient =
                Object.FindAnyObjectByType<OntologyWorldAuthorityClient>(FindObjectsInactive.Include);
            Assert.That(authorityClient, Is.Not.Null);
            var clientFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            var accountField = typeof(OntologyWorldAuthorityClient)
                .GetField("currentAccount", clientFlags);
            var selectedCharacterField = typeof(OntologyWorldAuthorityClient)
                .GetField("currentCharacterId", clientFlags);
            Assert.That(accountField, Is.Not.Null);
            Assert.That(selectedCharacterField, Is.Not.Null);
            var originalAccount = accountField.GetValue(authorityClient);
            var originalSelection = selectedCharacterField.GetValue(authorityClient);
            var actualCharacterId = System.Guid.NewGuid().ToString();
            try
            {
                accountField.SetValue(authorityClient, new OntologyAuthorityAccountDashboard
                {
                    account = new OntologyAuthorityAccount
                    {
                        userId = System.Guid.NewGuid().ToString(),
                        displayName = "Actual Account"
                    },
                    characters = new[]
                    {
                        new OntologyAuthorityPlayerCharacter
                        {
                            characterId = actualCharacterId,
                            displayName = "Actual Character",
                            templateId = "player",
                            equippedPartIds = new[] { "Part_Hair_001", "Part_Shirt_001" }
                        }
                    },
                    worlds = System.Array.Empty<OntologyAuthorityAccountWorld>()
                });
                selectedCharacterField.SetValue(authorityClient, actualCharacterId);
                characterSelectionPanel.Refresh();

                var boundCards = characterSelectionPanel.CharacterCards
                    .Where(card => card != null && card.gameObject.activeSelf)
                    .ToArray();
                Assert.That(boundCards.Length, Is.EqualTo(1));
                Assert.That(boundCards[0].CharacterId, Is.EqualTo(actualCharacterId));
                Assert.That(
                    boundCards[0].transform
                        .Find("SelectedIndicator/SelectedBadge/SelectedLabel")
                        .gameObject.activeSelf,
                    Is.False,
                    "Localized selection text must not wrap vertically inside the compact marker.");
                Assert.That(
                    characterSelectionPanel.transform
                        .Find("SelectedCharacterDetailPanel/SelectedCharacterName")
                        .GetComponent<TMP_Text>().text,
                    Is.EqualTo("Actual Character"));
                Assert.That(
                    characterSelectionPanel.transform
                        .Find("AccountChip/AccountLabel")
                        .GetComponent<TMP_Text>().text,
                    Is.EqualTo("Actual Account"),
                    "The account chip must show only the display name without an Account/계정 prefix.");
            }
            finally
            {
                accountField.SetValue(authorityClient, originalAccount);
                selectedCharacterField.SetValue(authorityClient, originalSelection);
                characterSelectionPanel.Refresh();
            }

            var questPanel = Object.FindAnyObjectByType<OntologyQuestActionPanel>(FindObjectsInactive.Include);
            Assert.That(questPanel, Is.Not.Null);
            var questPreview = questPanel.transform.Find(
                "QuestScroll/Viewport/Content/EditorPreviewQuestRows");
            var actionPreview = questPanel.transform.Find(
                "ActionScroll/Viewport/Content/EditorPreviewActionRows");
            Assert.That(questPreview, Is.Not.Null);
            Assert.That(actionPreview, Is.Not.Null);
            Assert.That(questPreview.gameObject.activeInHierarchy, Is.False,
                "Authored quest preview rows may remain editable but must not be visible at runtime.");
            Assert.That(actionPreview.gameObject.activeInHierarchy, Is.False,
                "Authored action preview rows may remain editable but must not be visible at runtime.");

            var questPanelGroup = questPanel.GetComponent<CanvasGroup>();
            Assert.That(questPanel.gameObject.activeInHierarchy, Is.False,
                "The quest panel must remain hidden before authenticated world entry.");
            questPanel.Open();
            Assert.That(questPanelGroup.alpha, Is.EqualTo(1f));
            Assert.That(questPreview.gameObject.activeSelf, Is.False,
                "Opening the quest panel must not reveal authored quest preview rows.");
            Assert.That(actionPreview.gameObject.activeSelf, Is.False,
                "Opening the quest panel must not reveal authored action preview rows.");
            questPanel.Close();
            Assert.That(questPanelGroup.alpha, Is.EqualTo(0f));

            var accountAppearanceObject = FindSceneObject("AccountCharacterAppearancePanel");
            var runtimeAppearanceObject = FindSceneObject("OntologyCharacterCustomizationPanel");
            Assert.That(accountAppearanceObject, Is.Not.Null);
            Assert.That(runtimeAppearanceObject, Is.Not.Null);

            var accountAppearance =
                accountAppearanceObject.GetComponent<OntologyCharacterCustomizationPanel>();
            var runtimeAppearance =
                runtimeAppearanceObject.GetComponent<OntologyCharacterCustomizationPanel>();
            Assert.That(accountAppearance, Is.Not.Null);
            Assert.That(runtimeAppearance, Is.Not.Null);

            var characterCreation = FindSceneObject("AccountCharacterCreationPanel");
            Assert.That(characterCreation, Is.Not.Null);
            var characterNameInput = characterCreation.transform.Find(
                "CharacterIdentityCard/CharacterNameInput");
            Assert.That(characterNameInput, Is.Not.Null,
                "The editable character-name field must remain visible in the scene hierarchy.");
            var characterNameField = characterNameInput.GetComponent<TMP_InputField>();
            Assert.That(characterNameField, Is.Not.Null);
            Assert.That(characterNameField.textViewport, Is.Not.Null);
            Assert.That(characterNameField.textComponent, Is.Not.Null);
            Assert.That(characterNameField.placeholder, Is.Not.Null);
            Assert.That(characterNameField.characterLimit, Is.EqualTo(0));
            Assert.That(characterNameField.characterValidation,
                Is.EqualTo(TMP_InputField.CharacterValidation.None));
            Assert.That(characterNameField.textComponent.color.grayscale, Is.LessThan(0.5f),
                "Entered character names must use the dark TOV text color.");
            characterNameField.SetTextWithoutNotify("한글이름");
            Assert.That(characterNameField.text, Is.EqualTo("한글이름"));

            Assert.That(accountAppearance.RuntimeToggleEnabled, Is.False,
                "The account appearance step must not consume the in-world C-key toggle.");
            Assert.That(runtimeAppearance.RuntimeToggleEnabled, Is.True,
                "The in-world appearance panel must retain its runtime toggle.");
            Assert.That(runtimeAppearance.PersistsAccountAppearanceOnClose, Is.True,
                "Closing the in-world appearance panel must persist account appearance.");
            Assert.That(runtimeAppearance.EquipOnPartSelection, Is.True,
                "Selecting an in-world part must equip it immediately.");
            Assert.That(runtimeAppearance.HasCategoryIcons, Is.True,
                "The in-world appearance panel must retain authored category icons.");

            var accountPreviewPresenter =
                accountAppearanceObject.GetComponent<OntologyCharacterCreationPreviewPresenter>();
            Assert.That(accountPreviewPresenter, Is.Not.Null);
            var accountAppearanceGroup = accountAppearanceObject.GetComponent<CanvasGroup>();
            Assert.That(accountAppearanceGroup, Is.Not.Null);
            accountAppearanceGroup.alpha = 1f;
            yield return new WaitForEndOfFrame();

            var accountPreviewClone = accountPreviewPresenter.PreviewCloneRoot;
            Assert.That(accountPreviewClone, Is.Not.Null,
                "Character creation must render a preview even while the world player is inactive.");
            Assert.That(accountPreviewClone.gameObject.activeInHierarchy, Is.True,
                "The account appearance preview clone must be visible while its panel is open.");
            Assert.That(
                accountPreviewClone.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                Is.True,
                "The account appearance preview must contain at least one visible renderer.");

            var characterPartAdapter =
                Object.FindAnyObjectByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include);
            Assert.That(characterPartAdapter, Is.Not.Null);
            Assert.That(accountPreviewPresenter.PartAdapter, Is.SameAs(characterPartAdapter),
                "The account preview and customization panel must project the same avatar adapter.");
            Assert.That(characterPartAdapter.gameObject.activeInHierarchy, Is.False,
                "The gameplay avatar must remain gated before authenticated world entry.");

            var lateAccountPanelObject = new GameObject(
                "LateAccountCharacterAppearancePanel",
                typeof(RectTransform),
                typeof(CanvasGroup));
            var lateAccountPanel =
                lateAccountPanelObject.AddComponent<OntologyCharacterCustomizationPanel>();
            Assert.That(lateAccountPanel.PartAdapter, Is.SameAs(characterPartAdapter),
                "Account creation must bind the inactive scene avatar adapter after scene composition.");
            Object.Destroy(lateAccountPanelObject);

            accountAppearance.OpenEmbeddedForAccountCharacterCreation();
            Assert.That(accountAppearance.PartAdapter, Is.SameAs(characterPartAdapter));
            characterPartAdapter.EnsureAppearanceInitialized();

            yield return null;
            Button equipablePartCard = null;
            string clickedPartId = null;
            foreach (var button in accountAppearanceObject.GetComponentsInChildren<Button>(true))
            {
                if (button == null || !button.gameObject.activeInHierarchy ||
                    !button.name.StartsWith("Part_Part_"))
                {
                    continue;
                }

                var candidatePartId = button.name.Substring("Part_".Length);
                if (!characterPartAdapter.CanEquipPart(candidatePartId, out _))
                {
                    continue;
                }

                equipablePartCard = button;
                clickedPartId = candidatePartId;
                break;
            }

            Assert.That(equipablePartCard, Is.Not.Null,
                "Character creation must build at least one selectable part card.");
            equipablePartCard.onClick.Invoke();
            Assert.That(characterPartAdapter.IsPartEquipped(clickedPartId), Is.True,
                "Clicking a character-creation part card must immediately equip it.");

            Assert.That(characterPartAdapter.EquipPart("Part_Pants_010"), Is.True,
                "The authored pants part used by the preview projection test must remain equipable.");
            accountPreviewClone = accountPreviewPresenter.SynchronizeNow();
            Assert.That(accountPreviewClone, Is.Not.Null);
            var sourcePants = characterPartAdapter.VisualRoot.Find("Pants")
                ?.GetComponent<SkinnedMeshRenderer>();
            var previewPants = accountPreviewClone.Find("Pants")
                ?.GetComponent<SkinnedMeshRenderer>();
            Assert.That(sourcePants, Is.Not.Null);
            Assert.That(previewPants, Is.Not.Null);
            Assert.That(
                previewPants.sharedMesh,
                Is.EqualTo(sourcePants.sharedMesh),
                "The account preview clone must follow the selected part mesh, not only its material.");
            Assert.That(previewPants.localBounds, Is.EqualTo(sourcePants.localBounds));

            accountAppearanceGroup.alpha = 0f;
            yield return new WaitForEndOfFrame();
            Assert.That(accountPreviewClone.gameObject.activeInHierarchy, Is.False,
                "The preview clone must stop rendering when the account appearance panel closes.");

            Assert.That(
                runtimeAppearanceObject.GetComponent<OntologyCharacterCreationPreviewPresenter>(),
                Is.Not.Null);
            Assert.That(
                runtimeAppearanceObject.transform.Find(
                    "CharacterPreviewFrame/LiveCharacterPreview"),
                Is.Not.Null);
            var runtimeEditorPreview =
                runtimeAppearanceObject.transform.Find("EditorPreviewContent");
            Assert.That(runtimeEditorPreview, Is.Not.Null);
            Assert.That(runtimeEditorPreview.gameObject.activeInHierarchy, Is.False,
                "Editor-only dummy content must not be visible before authenticated world entry.");

            var statusHud = Object.FindAnyObjectByType<OntologyRuntimeStatusHUD>(FindObjectsInactive.Include);
            Assert.That(statusHud, Is.Not.Null);
            Assert.That(statusHud.gameObject.activeInHierarchy, Is.False,
                "Runtime HUD must stay hidden before authenticated world entry.");
            Assert.That(statusHud.transform.parent.name, Is.EqualTo("OntologyGameCanvas"));
            Assert.That(statusHud.transform.Find("HeaderBar/Title"), Is.Not.Null);
            Assert.That(statusHud.transform.Find("StatusRows/MovementRow/IconPlate/Icon"), Is.Not.Null);
            Assert.That(statusHud.transform.Find("StatusRows/InteractionRow/Value"), Is.Not.Null);
            Assert.That(statusHud.transform.Find("StatusRows/PermissionRow/Value"), Is.Not.Null);
            Assert.That(statusHud.transform.Find("StatusRows/SaveRow/Value"), Is.Not.Null);
            statusHud.SetCollapsed(true);
            Assert.That(statusHud.IsCollapsed, Is.True,
                "The status HUD must report its collapsed state.");
            Assert.That(statusHud.transform.Find("StatusRows").gameObject.activeSelf, Is.False,
                "The collapsed status HUD must keep its row container hidden.");
            Assert.That(
                (statusHud.transform.parent.Find("StatusCardShadow") as RectTransform)
                    ?.sizeDelta.y,
                Is.EqualTo(76f).Within(0.1f));
            statusHud.SetCollapsed(false);
            Assert.That(statusHud.transform.Find("StatusRows").gameObject.activeSelf, Is.True,
                "Expanding the status HUD must reveal its row container.");

            var placementPanel = Object.FindAnyObjectByType<OntologyObjectPlacementPanel>(
                FindObjectsInactive.Include);
            Assert.That(placementPanel, Is.Not.Null);
            Assert.That(placementPanel.RuntimeToggleObject, Is.Not.Null);
            Assert.That(placementPanel.RuntimeToggleObject.name,
                Is.EqualTo("WorldPlacementToggleButton"));
            Assert.That(
                placementPanel.RuntimeToggleObject.GetComponent<Image>()?.sprite,
                Is.Not.Null);

            var actorToast = Object.FindAnyObjectByType<OntologyActorToast>(FindObjectsInactive.Include);
            Assert.That(actorToast, Is.Not.Null);
            var actorToastCanvas = actorToast.transform.Find("ActorToastCanvas") as RectTransform;
            Assert.That(actorToastCanvas, Is.Not.Null);
            Assert.That(actorToastCanvas.sizeDelta.x, Is.LessThanOrEqualTo(320f),
                "The actor toast must remain a compact head-level notification.");
            Assert.That(actorToastCanvas.sizeDelta.y, Is.LessThanOrEqualTo(80f),
                "The actor toast must not cover the world view.");
            var actorToastCard = actorToast.transform.Find("ActorToastCanvas/ToastCard");
            Assert.That(actorToastCard, Is.Not.Null);
            var obsoleteAccent = actorToastCard.Find("Accent");
            Assert.That(obsoleteAccent == null || !obsoleteAccent.gameObject.activeSelf, Is.True,
                "The old leading accent image must not intrude into the icon area.");
            Assert.That(actorToast.transform.Find(
                "ActorToastCanvas/ToastCard/IconPlate/SeverityIcon"), Is.Not.Null);
            Assert.That(actorToast.transform.Find(
                "ActorToastCanvas/ToastCard/TitleLabel"), Is.Not.Null);
            Assert.That(actorToast.transform.Find(
                "ActorToastCanvas/ToastCard/DetailLabel"), Is.Not.Null);
        }

        private static GameObject FindSceneObject(string objectName)
        {
            foreach (var value in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (value != null && value.name == objectName) return value.gameObject;
            }
            return null;
        }
    }
}
