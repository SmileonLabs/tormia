#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    /// <summary>
    /// Applies the approved TOV character-selection concept as ordinary,
    /// hierarchy-authored uGUI objects. Runtime code only binds account data.
    /// </summary>
    public static class OntologyTovCharacterSelectionAuthoring
    {
        private const string RootName = "AccountCharacterSelectionPanel";
        private const string PreviewTexturePath = "Assets/Art/UI/CharacterSelection/character_preview_triptych_v1.png";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(80, 101, 112, 210);

        [MenuItem("Tormia/UI/Apply Editable TOV Character Selection Design")]
        public static void Apply()
        {
            var rootObject = FindSceneObject(RootName);
            if (rootObject == null) throw new InvalidOperationException(RootName + " was not found.");
            var root = rootObject.GetComponent<RectTransform>();
            var assets = LoadAssets();
            PrepareRoot(root, assets);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTopLeft(logo.rectTransform, new Vector2(42f, -30f), new Vector2(250f, 145f));
            logo.sprite = assets.logo;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;

            var title = RequireOrCreateText(root, "Text (TMP)", null);
            PlaceTop(title.rectTransform, new Vector2(0f, -45f), new Vector2(720f, 72f));
            StyleText(title, 46f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = "SELECT A CHARACTER";

            var helper = RequireOrCreateText(root, "CharacterSelectionHelper", title);
            PlaceTop(helper.rectTransform, new Vector2(0f, -120f), new Vector2(760f, 42f));
            StyleText(helper, 22f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            helper.text = "Choose a character for your adventure.";

            var accountChip = EnsureImage(root, "AccountChip");
            PlaceTopRight(accountChip.rectTransform, new Vector2(-42f, -58f), new Vector2(360f, 62f));
            StyleImage(accountChip, assets.surface, new Color32(248, 241, 225, 255), false);
            var obsoleteAccountAvatar = accountChip.rectTransform.Find("AccountAvatarFrame");
            if (obsoleteAccountAvatar != null) Undo.DestroyObjectImmediate(obsoleteAccountAvatar.gameObject);
            var accountLabel = RequireOrCreateText(accountChip.rectTransform, "AccountLabel", title);
            accountLabel.rectTransform.anchorMin = Vector2.zero;
            accountLabel.rectTransform.anchorMax = Vector2.one;
            accountLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            accountLabel.rectTransform.anchoredPosition = Vector2.zero;
            accountLabel.rectTransform.sizeDelta = new Vector2(-24f, -12f);
            StyleText(accountLabel, 19f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            accountLabel.text = "-";

            var listPanel = EnsureImage(root, "CharacterListPanel");
            PlaceTop(listPanel.rectTransform, new Vector2(-545f, -190f), new Vector2(570f, 650f));
            StyleImage(listPanel, assets.surface, new Color32(252, 246, 233, 255), false);

            var listHeading = RequireOrCreateText(listPanel.rectTransform, "CharacterListHeading", title);
            PlaceTop(listHeading.rectTransform, new Vector2(0f, -18f), new Vector2(510f, 42f));
            StyleText(listHeading, 22f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            listHeading.text = "MY CHARACTERS";

            var cards = new OntologyAccountCharacterCard[3];
            var names = new[] { "Haneul", "Bori", "Maru" };
            var templates = new[] { "Default Adventurer", "Energetic Explorer", "Default Adventurer" };
            for (var index = 0; index < cards.Length; index++)
            {
                var cardName = "CharacterCardSlot" + (index + 1);
                var cardRoot = root.Find(cardName) as RectTransform
                    ?? listPanel.rectTransform.Find(cardName) as RectTransform
                    ?? EnsureRect(root, cardName);
                cardRoot.SetParent(listPanel.rectTransform, false);
                PlaceTop(cardRoot, new Vector2(0f, -72f - index * 148f), new Vector2(520f, 132f));
                cards[index] = StyleCard(cardRoot, index, names[index], templates[index], title, assets);
            }

            var createNew = StyleCreateNewButton(listPanel.rectTransform, title, assets);
            var detail = StyleSelectedCharacterDetail(root, title, assets, out var detailName, out var detailTemplate,
                out var currentAppearance, out var appearanceSlots, out var detailLive, out var detailFallback);

            var back = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(-690f, -910f), new Vector2(250f, 70f), false, title, assets);
            back.GetComponentInChildren<TMP_Text>(true).text = "LOG OUT";
            var next = StyleButton(root, "ContinueButton", "ContinueButtonSurface", new Vector2(590f, -910f), new Vector2(440f, 76f), true, title, assets);
            next.GetComponentInChildren<TMP_Text>(true).text = "CONTINUE WITH THIS CHARACTER";
            var entryHint = RequireOrCreateText(root, "CharacterEntryHint", title);
            PlaceTop(entryHint.rectTransform, new Vector2(0f, -918f), new Vector2(650f, 42f));
            StyleText(entryHint, 17f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            entryHint.text = "Enter the world with the selected appearance and profile.";
            var status = RequireOrCreateText(root, "CharacterSelectionStatus", title);
            PlaceTop(status.rectTransform, new Vector2(0f, -858f), new Vector2(1100f, 38f));
            StyleText(status, 17f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            status.text = string.Empty;

            RemoveRootPresentation(rootObject);
            if (rootObject.GetComponent<OntologyUiEditorPreview>() == null) Undo.AddComponent<OntologyUiEditorPreview>(rootObject);

            var panel = rootObject.GetComponent<OntologyAccountCharacterSelectionPanel>();
            var serializedPanel = new SerializedObject(panel);
            SetReference(serializedPanel, "panelGroup", rootObject.GetComponent<CanvasGroup>());
            SetReference(serializedPanel, "titleLabel", title);
            SetReference(serializedPanel, "helperLabel", helper);
            SetReference(serializedPanel, "accountLabel", accountLabel);
            SetReference(serializedPanel, "statusLabel", status);
            SetReference(serializedPanel, "detailNameLabel", detailName);
            SetReference(serializedPanel, "detailTemplateLabel", detailTemplate);
            SetReference(serializedPanel, "currentAppearanceLabel", currentAppearance);
            SetReference(serializedPanel, "entryHintLabel", entryHint);
            SetReference(serializedPanel, "partDatabase", UnityEngine.Object.FindAnyObjectByType<OntologyCharacterPartAdapter>()?.PartDatabase);
            SetReference(serializedPanel, "continueButton", next);
            SetReference(serializedPanel, "backButton", back);
            SetReference(serializedPanel, "createNewButton", createNew);
            var appearanceProperty = serializedPanel.FindProperty("appearanceSlots");
            appearanceProperty.arraySize = appearanceSlots.Length;
            for (var index = 0; index < appearanceSlots.Length; index++)
                appearanceProperty.GetArrayElementAtIndex(index).objectReferenceValue = appearanceSlots[index];
            var cardsProperty = serializedPanel.FindProperty("characterCards");
            cardsProperty.arraySize = cards.Length;
            for (var index = 0; index < cards.Length; index++) cardsProperty.GetArrayElementAtIndex(index).objectReferenceValue = cards[index];
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            Configure3dPreviews(rootObject, cards, detailLive, detailFallback);

            if (rootObject.GetComponent<CanvasGroup>() is { } group)
            {
                group.alpha = 1f;
                group.interactable = false;
                group.blocksRaycasts = false;
                EditorUtility.SetDirty(group);
            }
            rootObject.GetComponent<OntologyAccountCharacterSelectionPreviewPresenter>()?.RefreshPreviews();
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            OntologyUiEditorPreviewSelector.ShowCharacterSelection();
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV character selection UI to " + rootObject.scene.path + ".");
        }

        private static OntologyAccountCharacterCard StyleCard(RectTransform root, int index, string nameValue, string templateValue, TMP_Text fontSource, Assets assets)
        {
            ClearChildren(root);
            RemoveCustomButton(root.gameObject);
            var button = root.GetComponent<Button>() ?? Undo.AddComponent<Button>(root.gameObject);
            var outline = root.GetComponent<Image>() ?? Undo.AddComponent<Image>(root.gameObject);
            StyleImage(outline, assets.canvas, new Color32(218, 199, 168, 255), true);
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;

            var selected = EnsureImage(root, "SelectedIndicator");
            PlaceMiddleRight(selected.rectTransform, new Vector2(-10f, 0f), new Vector2(48f, 48f));
            StyleImage(selected, assets.canvas, Gold, false);
            var badge = EnsureImage(selected.rectTransform, "SelectedBadge");
            StretchInset(badge.rectTransform, 4f);
            StyleImage(badge, assets.canvas, Blue, false);
            var selectedLabel = RequireOrCreateText(badge.rectTransform, "SelectedLabel", fontSource);
            StretchInset(selectedLabel.rectTransform, 4f);
            StyleText(selectedLabel, 27f, TextAlignmentOptions.Center, Blue, FontStyles.Bold);
            selectedLabel.text = string.Empty;
            selectedLabel.gameObject.SetActive(false);
            selected.gameObject.SetActive(index == 0);

            var surface = EnsureImage(root, "CardSurface");
            StretchInset(surface.rectTransform, 5f);
            StyleImage(surface, assets.surface, Cream, false);

            var previewFrame = EnsureRect(root, "CharacterPreviewFrame");
            PlaceMiddleLeft(previewFrame, new Vector2(12f, 0f), new Vector2(122f, 112f));
            var frameImage = previewFrame.GetComponent<Image>() ?? Undo.AddComponent<Image>(previewFrame.gameObject);
            StyleImage(frameImage, assets.canvas, new Color32(229, 215, 190, 255), false);
            var previewMask = EnsureRect(previewFrame, "PreviewMask");
            StretchInset(previewMask, 4f);
            if (previewMask.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(previewMask.gameObject);
            var fallbackRect = EnsureRect(previewMask, "FallbackCharacterPreview");
            CenterRect(fallbackRect, new Vector2(68f, 104f));
            var fallback = fallbackRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(fallbackRect.gameObject);
            fallback.texture = assets.preview;
            fallback.uvRect = new Rect(index / 3f, 0f, 1f / 3f, 1f);
            fallback.color = Color.white;
            fallback.raycastTarget = false;
            var liveRect = EnsureRect(previewMask, "LiveCharacterPreview");
            StretchInset(liveRect, 0f);
            var live = liveRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(liveRect.gameObject);
            live.texture = null;
            live.uvRect = new Rect(0f, 0f, 1f, 1f);
            live.color = Color.white;
            live.raycastTarget = false;
            live.gameObject.SetActive(false);
            fallback.transform.SetSiblingIndex(0);
            live.transform.SetSiblingIndex(1);

            var nameRect = EnsureRect(root, "CharacterName");
            var name = nameRect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(nameRect.gameObject);
            if (fontSource != null) { name.font = fontSource.font; name.fontSharedMaterial = fontSource.fontSharedMaterial; }
            PlaceMiddleLeft(name.rectTransform, new Vector2(156f, 22f), new Vector2(290f, 42f));
            StyleText(name, 29f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Bold);
            name.text = nameValue;

            var templateChip = EnsureImage(root, "TemplateChip");
            PlaceMiddleLeft(templateChip.rectTransform, new Vector2(156f, -26f), new Vector2(220f, 36f));
            StyleImage(templateChip, assets.canvas, index == 1 ? new Color32(142, 86, 170, 255) : new Color32(65, 122, 225, 255), false);
            var template = RequireOrCreateText(templateChip.rectTransform, "TemplateLabel", fontSource);
            StretchInset(template.rectTransform, 4f);
            StyleText(template, 16f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            template.text = templateValue;

            var obsoleteAppearance = root.Find("AppearanceSummary");
            if (obsoleteAppearance != null)
                Undo.DestroyObjectImmediate(obsoleteAppearance.gameObject);

            var profile = RequireOrCreateText(root, "ProfileSummary", fontSource);
            PlaceMiddleLeft(profile.rectTransform, new Vector2(156f, -55f), new Vector2(260f, 24f));
            StyleText(profile, 14f, TextAlignmentOptions.MidlineLeft, Muted, FontStyles.Normal);
            profile.text = string.Empty;
            profile.gameObject.SetActive(false);

            previewFrame.transform.SetAsLastSibling();
            name.transform.SetAsLastSibling();
            templateChip.transform.SetAsLastSibling();
            profile.transform.SetAsLastSibling();
            surface.transform.SetSiblingIndex(1);
            selected.transform.SetAsLastSibling();

            var card = root.GetComponent<OntologyAccountCharacterCard>() ?? Undo.AddComponent<OntologyAccountCharacterCard>(root.gameObject);
            var serialized = new SerializedObject(card);
            SetReference(serialized, "selectButton", button);
            SetReference(serialized, "selectedIndicator", selected.gameObject);
            SetReference(serialized, "nameLabel", name);
            SetReference(serialized, "templateLabel", template);
            SetReference(serialized, "profileSummaryLabel", profile);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return card;
        }

        private static Button StyleCreateNewButton(RectTransform parent, TMP_Text fontSource, Assets assets)
        {
            var rect = EnsureRect(parent, "CreateNewCharacterButton");
            PlaceTop(rect, new Vector2(0f, -520f), new Vector2(520f, 108f));
            RemoveCustomButton(rect.gameObject);
            var button = rect.GetComponent<Button>() ?? Undo.AddComponent<Button>(rect.gameObject);
            var outline = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            StyleImage(outline, assets.canvas, new Color32(205, 174, 121, 255), true);
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;

            var surface = EnsureImage(rect, "CreateNewSurface");
            StretchInset(surface.rectTransform, 4f);
            StyleImage(surface, assets.surface, new Color32(255, 250, 240, 255), false);

            var plusBadge = EnsureImage(rect, "PlusBadge");
            PlaceMiddleLeft(plusBadge.rectTransform, new Vector2(28f, 0f), new Vector2(68f, 68f));
            StyleImage(plusBadge, assets.canvas, new Color32(247, 238, 220, 255), false);
            var plus = RequireOrCreateText(plusBadge.rectTransform, "PlusLabel", fontSource);
            StretchInset(plus.rectTransform, 4f);
            StyleText(plus, 42f, TextAlignmentOptions.Center, new Color32(165, 120, 55, 255), FontStyles.Bold);
            plus.text = "+";

            var label = RequireOrCreateText(rect, "CreateNewLabel", fontSource);
            PlaceMiddleLeft(label.rectTransform, new Vector2(126f, 0f), new Vector2(350f, 58f));
            StyleText(label, 25f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Bold);
            label.text = "CREATE NEW CHARACTER";
            surface.transform.SetSiblingIndex(0);
            plusBadge.transform.SetAsLastSibling();
            label.transform.SetAsLastSibling();
            return button;
        }

        private static RectTransform StyleSelectedCharacterDetail(
            RectTransform root,
            TMP_Text fontSource,
            Assets assets,
            out TMP_Text detailName,
            out TMP_Text detailTemplate,
            out TMP_Text currentAppearance,
            out OntologyAppearanceReviewPartSlot[] appearanceSlots,
            out RawImage live,
            out RawImage fallback)
        {
            var detail = EnsureRect(root, "SelectedCharacterDetailPanel");
            PlaceTop(detail, new Vector2(320f, -190f), new Vector2(1030f, 650f));
            ClearChildren(detail);
            var outline = detail.GetComponent<Image>() ?? Undo.AddComponent<Image>(detail.gameObject);
            StyleImage(outline, assets.canvas, new Color32(218, 199, 168, 255), false);
            var surface = EnsureImage(detail, "DetailSurface");
            StretchInset(surface.rectTransform, 5f);
            StyleImage(surface, assets.surface, new Color32(255, 249, 238, 255), false);

            detailName = RequireOrCreateText(detail, "SelectedCharacterName", fontSource);
            PlaceTopLeft(detailName.rectTransform, new Vector2(34f, -22f), new Vector2(300f, 58f));
            StyleText(detailName, 39f, TextAlignmentOptions.MidlineLeft, Ink, FontStyles.Bold);
            detailName.text = "Haneul";

            var templateChip = EnsureImage(detail, "SelectedTemplateChip");
            PlaceTopLeft(templateChip.rectTransform, new Vector2(34f, -82f), new Vector2(240f, 40f));
            StyleImage(templateChip, assets.canvas, new Color32(65, 122, 225, 255), false);
            detailTemplate = RequireOrCreateText(templateChip.rectTransform, "SelectedTemplateLabel", fontSource);
            StretchInset(detailTemplate.rectTransform, 4f);
            StyleText(detailTemplate, 17f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            detailTemplate.text = "Default Adventurer";

            var previewFrame = EnsureRect(detail, "SelectedCharacterPreviewFrame");
            PlaceTop(previewFrame, new Vector2(0f, -18f), new Vector2(970f, 424f));
            var previewSurface = previewFrame.GetComponent<Image>() ?? Undo.AddComponent<Image>(previewFrame.gameObject);
            StyleImage(previewSurface, assets.surface, new Color32(252, 245, 232, 255), false);
            var previewMask = EnsureRect(previewFrame, "PreviewMask");
            StretchInset(previewMask, 3f);
            if (previewMask.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(previewMask.gameObject);
            var fallbackRect = EnsureRect(previewMask, "FallbackCharacterPreview");
            CenterRect(fallbackRect, new Vector2(260f, 400f));
            fallback = fallbackRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(fallbackRect.gameObject);
            fallback.texture = assets.preview;
            fallback.uvRect = new Rect(0f, 0f, 1f / 3f, 1f);
            fallback.color = Color.white;
            fallback.raycastTarget = false;
            var liveRect = EnsureRect(previewMask, "LiveCharacterPreview");
            StretchInset(liveRect, 0f);
            live = liveRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(liveRect.gameObject);
            live.texture = null;
            live.uvRect = new Rect(0f, 0f, 1f, 1f);
            live.color = Color.white;
            live.raycastTarget = false;
            live.gameObject.SetActive(false);

            var appearancePanel = EnsureImage(detail, "CurrentAppearancePanel");
            PlaceTop(appearancePanel.rectTransform, new Vector2(0f, -452f), new Vector2(970f, 178f));
            StyleImage(appearancePanel, assets.surface, new Color32(255, 252, 246, 255), false);
            currentAppearance = RequireOrCreateText(appearancePanel.rectTransform, "CurrentAppearanceHeading", fontSource);
            PlaceTop(currentAppearance.rectTransform, new Vector2(0f, -8f), new Vector2(520f, 38f));
            StyleText(currentAppearance, 24f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            currentAppearance.text = "CURRENT APPEARANCE";

            var viewport = EnsureRect(appearancePanel.rectTransform, "AppearanceViewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(28f, 10f);
            viewport.offsetMax = new Vector2(-28f, -46f);
            if (viewport.GetComponent<RectMask2D>() == null)
                Undo.AddComponent<RectMask2D>(viewport.gameObject);

            var content = EnsureRect(viewport, "AppearanceContent");
            content.anchorMin = new Vector2(0f, 0.5f);
            content.anchorMax = new Vector2(0f, 0.5f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            var layout = content.GetComponent<HorizontalLayoutGroup>()
                ?? Undo.AddComponent<HorizontalLayoutGroup>(content.gameObject);
            layout.padding = new RectOffset();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>()
                ?? Undo.AddComponent<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = appearancePanel.GetComponent<ScrollRect>()
                ?? Undo.AddComponent<ScrollRect>(appearancePanel.gameObject);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = null;

            appearanceSlots = new OntologyAppearanceReviewPartSlot[13];
            var placeholderLabels = new[]
            {
                "Body", "Face", "Hair", "Top", "Bottom", "Outfit", "Shoes",
                "Headwear", "Eyewear", "Accessory", "Back", "Hands", "Effect"
            };
            for (var index = 0; index < appearanceSlots.Length; index++)
            {
                var slotRoot = EnsureRect(content, "AppearanceSlot" + (index + 1));
                slotRoot.anchorMin = slotRoot.anchorMax = new Vector2(0f, 0.5f);
                slotRoot.pivot = new Vector2(0.5f, 0.5f);
                slotRoot.sizeDelta = new Vector2(140f, 116f);
                var slotLayout = slotRoot.GetComponent<LayoutElement>()
                    ?? Undo.AddComponent<LayoutElement>(slotRoot.gameObject);
                slotLayout.preferredWidth = 140f;
                slotLayout.preferredHeight = 116f;
                slotLayout.flexibleWidth = 0f;
                slotLayout.flexibleHeight = 0f;
                var slotSurface = slotRoot.GetComponent<Image>() ?? Undo.AddComponent<Image>(slotRoot.gameObject);
                // Keep the layout slot editable but visually transparent. The
                // circular IconFrame is the thumbnail surface; an opaque slot
                // image makes the horizontal list look like square cards.
                slotSurface.sprite = null;
                slotSurface.color = Color.clear;
                slotSurface.raycastTarget = false;
                var iconFrame = EnsureImage(slotRoot, "IconFrame");
                PlaceTop(iconFrame.rectTransform, new Vector2(0f, -5f), new Vector2(78f, 78f));
                StyleImage(iconFrame, assets.circleMask, new Color32(250, 242, 226, 255), false);
                var iconMask = iconFrame.GetComponent<Mask>() ?? Undo.AddComponent<Mask>(iconFrame.gameObject);
                iconMask.enabled = true;
                iconMask.showMaskGraphic = true;
                var icon = EnsureImage(iconFrame.rectTransform, "PartIcon");
                // Overscan the square source sprite behind the circular mask so
                // its corners and imported image padding never remain visible.
                StretchInset(icon.rectTransform, -12f);
                icon.sprite = assets.appearanceIcons.Length == 0
                    ? null
                    : assets.appearanceIcons[index % assets.appearanceIcons.Length];
                icon.preserveAspect = true;
                icon.maskable = true;
                icon.color = Color.white;
                icon.raycastTarget = false;
                var label = RequireOrCreateText(slotRoot, "PartLabel", fontSource);
                PlaceTop(label.rectTransform, new Vector2(0f, -85f), new Vector2(132f, 26f));
                StyleText(label, 14f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
                label.text = placeholderLabels[index];
                var slot = slotRoot.GetComponent<OntologyAppearanceReviewPartSlot>()
                    ?? Undo.AddComponent<OntologyAppearanceReviewPartSlot>(slotRoot.gameObject);
                var serializedSlot = new SerializedObject(slot);
                SetReference(serializedSlot, "icon", icon);
                SetReference(serializedSlot, "label", label);
                serializedSlot.ApplyModifiedPropertiesWithoutUndo();
                appearanceSlots[index] = slot;
            }

            surface.transform.SetSiblingIndex(0);
            previewFrame.transform.SetSiblingIndex(1);
            detailName.transform.SetAsLastSibling();
            templateChip.transform.SetAsLastSibling();
            appearancePanel.transform.SetAsLastSibling();
            return detail;
        }

        private static void Configure3dPreviews(GameObject panelObject, OntologyAccountCharacterCard[] cards, RawImage detailLive, RawImage detailFallback)
        {
            var studioObject = FindSceneObject("CharacterSelectionPreviewStudio");
            if (studioObject == null)
            {
                studioObject = new GameObject("CharacterSelectionPreviewStudio");
                Undo.RegisterCreatedObjectUndo(studioObject, "Create character selection preview studio");
            }
            studioObject.transform.position = new Vector3(0f, -2000f, 0f);
            studioObject.transform.rotation = Quaternion.identity;
            studioObject.transform.localScale = Vector3.one;

            var presenter = panelObject.GetComponent<OntologyAccountCharacterSelectionPreviewPresenter>()
                ?? Undo.AddComponent<OntologyAccountCharacterSelectionPreviewPresenter>(panelObject);
            var adapter = OntologyCharacterPartAdapter.FindAvailable();
            var entryFlow = UnityEngine.Object.FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                FindObjectsInactive.Include);
            var serialized = new SerializedObject(presenter);
            SetReference(serialized, "entryFlow", entryFlow);
            SetReference(serialized, "panelGroup", panelObject.GetComponent<CanvasGroup>());
            SetReference(serialized, "sourcePartAdapter", adapter);
            SetReference(serialized, "partDatabase", adapter != null ? adapter.PartDatabase : null);
            SetReference(serialized, "previewStudioRoot", studioObject.transform);

            var slots = serialized.FindProperty("previewSlots");
            slots.arraySize = cards.Length + 1;
            for (var index = 0; index < cards.Length + 1; index++)
            {
                var slotRoot = EnsureTransform(studioObject.transform, "PreviewSlot" + (index + 1));
                slotRoot.localPosition = new Vector3(index * 20f, 0f, 0f);
                slotRoot.localRotation = Quaternion.identity;
                slotRoot.localScale = Vector3.one;
                var anchor = EnsureTransform(slotRoot, "ModelAnchor");
                anchor.localPosition = Vector3.zero;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
                var cameraTransform = EnsureTransform(slotRoot, "PreviewCamera");
                cameraTransform.localPosition = new Vector3(0f, 1f, 4f);
                cameraTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var camera = cameraTransform.GetComponent<Camera>();
                if (camera == null) camera = cameraTransform.gameObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;
                camera.allowMSAA = true;
                var listener = cameraTransform.GetComponent<AudioListener>();
                if (listener != null) Undo.DestroyObjectImmediate(listener);

                var previewMask = index < cards.Length
                    ? cards[index].transform.Find("CharacterPreviewFrame/PreviewMask")
                    : null;
                var live = index < cards.Length
                    ? previewMask?.Find("LiveCharacterPreview")?.GetComponent<RawImage>()
                    : detailLive;
                var fallback = index < cards.Length
                    ? previewMask?.Find("FallbackCharacterPreview")?.GetComponent<RawImage>()
                    : detailFallback;
                var slot = slots.GetArrayElementAtIndex(index);
                slot.FindPropertyRelative("modelAnchor").objectReferenceValue = anchor;
                slot.FindPropertyRelative("previewCamera").objectReferenceValue = camera;
                slot.FindPropertyRelative("liveImage").objectReferenceValue = live;
                slot.FindPropertyRelative("fallbackImage").objectReferenceValue = fallback;
                slot.FindPropertyRelative("useSelectedCharacter").boolValue = index == cards.Length;
                slot.FindPropertyRelative("previewLayer").intValue = 28 + index;
            }
            serialized.FindProperty("textureResolution").intValue = 768;
            serialized.FindProperty("fieldOfView").floatValue = 24f;
            serialized.FindProperty("framingPadding").floatValue = 1.12f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            presenter.RefreshPreviews();
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(studioObject);
        }

        private static void PrepareRoot(RectTransform root, Assets assets)
        {
            Undo.RecordObject(root, "Apply editable TOV character selection design");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(1740f, 1020f);
            root.localScale = Vector3.one;
            var blue = EnsureImage(root, "BrandFrameBlue");
            var gold = EnsureImage(root, "BrandFrameGold");
            var surface = EnsureImage(root, "PanelSurface");
            StretchInset(blue.rectTransform, 0f);
            StretchInset(gold.rectTransform, 7f);
            StretchInset(surface.rectTransform, 13f);
            StyleImage(blue, assets.canvas, Blue, false);
            StyleImage(gold, assets.canvas, Gold, false);
            StyleImage(surface, assets.surface, Cream, false);
            blue.transform.SetSiblingIndex(0);
            gold.transform.SetSiblingIndex(1);
            surface.transform.SetSiblingIndex(2);
            var shadow = blue.GetComponent<Shadow>() ?? Undo.AddComponent<Shadow>(blue.gameObject);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -8f);
        }

        private static Button StyleButton(RectTransform root, string name, string surfaceName, Vector2 position, Vector2 size, bool primary, TMP_Text fontSource, Assets assets)
        {
            var rect = Require<RectTransform>(root, name);
            PlaceTop(rect, position, size);
            RemoveCustomButton(rect.gameObject);
            var button = rect.GetComponent<Button>() ?? Undo.AddComponent<Button>(rect.gameObject);
            var outline = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            StyleImage(outline, assets.canvas, primary ? Gold : Blue, true);
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;
            var inner = EnsureImage(rect, surfaceName);
            StretchInset(inner.rectTransform, 3f);
            StyleImage(inner, assets.canvas, primary ? Blue : Cream, false);
            inner.transform.SetSiblingIndex(0);
            var label = rect.GetComponentInChildren<TextMeshProUGUI>(true) ?? RequireOrCreateText(rect, "Label", fontSource);
            StretchInset(label.rectTransform, 4f);
            label.rectTransform.sizeDelta = new Vector2(-24f, -8f);
            StyleText(label, 25f, TextAlignmentOptions.Center, primary ? Cream : Blue, FontStyles.Bold);
            label.transform.SetAsLastSibling();
            return button;
        }

        private static Assets LoadAssets()
        {
            var result = new Assets
            {
                canvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png"),
                surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png"),
                logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png"),
                preview = AssetDatabase.LoadAssetAtPath<Texture2D>(PreviewTexturePath),
                circleMask = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Art/UI/CharacterCreationFlat/circle_antialias_256.asset"),
                appearanceIcons = new[]
                {
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCategoryIcons/Category_Hair.png"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCategoryIcons/Category_UpperBody.png"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCategoryIcons/Category_LowerBody.png"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCategoryIcons/Category_Footwear.png"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCategoryIcons/Category_Accessory.png")
                }
            };
            if (result.logo == null || result.preview == null) throw new InvalidOperationException("Character-selection art assets are not imported.");
            return result;
        }

        private static GameObject FindSceneObject(string name) => Resources.FindObjectsOfTypeAll<Transform>()
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.scene.IsValid() && candidate.name == name)?.gameObject;

        private static RectTransform EnsureRect(RectTransform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Transform EnsureTransform(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Image EnsureImage(RectTransform parent, string name)
        {
            var rect = EnsureRect(parent, name);
            return rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
        }

        private static TextMeshProUGUI RequireOrCreateText(RectTransform parent, string name, TMP_Text fontSource)
        {
            var rect = EnsureRect(parent, name);
            var text = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
            if (fontSource != null) { text.font = fontSource.font; text.fontSharedMaterial = fontSource.fontSharedMaterial; }
            return text;
        }

        private static T Require<T>(RectTransform parent, string name) where T : Component
        {
            var child = parent.Find(name);
            var component = child != null ? child.GetComponent<T>() : null;
            if (component == null) throw new InvalidOperationException(name + " is missing " + typeof(T).Name + ".");
            return component;
        }

        private static void RemoveCustomButton(GameObject target)
        {
            var custom = target.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "FGUIStarter.CustomButton");
            if (custom != null) Undo.DestroyObjectImmediate(custom);
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (var index = parent.childCount - 1; index >= 0; index--)
                Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
        }

        private static void RemoveRootPresentation(GameObject root)
        {
            RemoveCustomButton(root);
            var image = root.GetComponent<Image>();
            if (image != null) Undo.DestroyObjectImmediate(image);
            var renderer = root.GetComponent<CanvasRenderer>();
            if (renderer != null) Undo.DestroyObjectImmediate(renderer);
        }

        private static void PlaceTop(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceTopRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceMiddleLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceMiddleRight(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceBottom(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void StretchInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-2f * inset, -2f * inset);
            rect.localScale = Vector3.one;
        }

        private static void CenterRect(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void StyleImage(Image image, Sprite sprite, Color color, bool raycast)
        {
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
        }

        private static void StyleText(TMP_Text text, float size, TextAlignmentOptions alignment, Color color, FontStyles style)
        {
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = style;
            text.raycastTarget = false;
        }

        private static void SetReference(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            var property = target.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException("Serialized property not found: " + propertyName);
            property.objectReferenceValue = value;
        }

        private sealed class Assets
        {
            public Sprite canvas;
            public Sprite surface;
            public Sprite logo;
            public Texture2D preview;
            public Sprite circleMask;
            public Sprite[] appearanceIcons;
        }
    }
}
#endif
