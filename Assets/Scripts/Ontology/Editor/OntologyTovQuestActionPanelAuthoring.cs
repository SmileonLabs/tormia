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
    /// Authors the approved TOV quest/action mockup as editable uGUI hierarchy
    /// objects. Runtime code only binds data to the authored templates.
    /// </summary>
    public static class OntologyTovQuestActionPanelAuthoring
    {
        private const string RootName = "QuestActionPanel";
        private const string ToggleName = "QuestActionToggleButton";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 BlueSoft = new(232, 241, 250, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 CreamDark = new(244, 232, 208, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(116, 103, 83, 255);
        private static readonly Color32 Green = new(104, 170, 48, 255);
        private static readonly Color32 Gray = new(132, 132, 132, 255);

        [MenuItem("Tormia/UI/Apply Editable TOV Quests & Actions Design")]
        public static void Apply()
        {
            var rootObject = FindSceneObject(RootName);
            if (rootObject == null) throw new InvalidOperationException(RootName + " was not found.");
            var toggleObject = FindSceneObject(ToggleName);
            if (toggleObject == null) throw new InvalidOperationException(ToggleName + " was not found.");

            var panel = rootObject.GetComponent<OntologyQuestActionPanel>();
            var group = rootObject.GetComponent<CanvasGroup>();
            if (panel == null || group == null)
                throw new InvalidOperationException("QuestActionPanel binder or CanvasGroup is missing.");

            var sourceText = rootObject.GetComponentInChildren<TMP_Text>(true);
            var font = sourceText != null ? sourceText.font : null;
            var fontMaterial = sourceText != null ? sourceText.fontSharedMaterial : null;
            var assets = LoadAssets();
            var root = rootObject.GetComponent<RectTransform>();

            Undo.RecordObject(root, "Apply editable TOV quests and actions design");
            Center(root, new Vector2(1740f, 1000f));
            ClearChildren(root);
            StyleRoot(rootObject, assets);

            var goldFrame = CreateImage(root, "BrandFrameGold", assets.canvas, Gold, false);
            Stretch(goldFrame.rectTransform, 8f);
            var surface = CreateImage(root, "PanelSurface", assets.surface, Cream, false);
            Stretch(surface.rectTransform, 17f);

            var logo = CreateImage(root, "TovLogo", assets.logo, Color.white, false);
            PlaceTopLeft(logo.rectTransform, new Vector2(42f, -27f), new Vector2(235f, 136f));
            logo.preserveAspect = true;

            var title = CreateText(root, "TitleLabel", "QUESTS & ACTIONS",
                42f, TextAlignmentOptions.Center, Ink, FontStyles.Bold, font, fontMaterial);
            PlaceTop(title.rectTransform, new Vector2(0f, -28f), new Vector2(820f, 55f));
            var subtitle = CreateText(root, "SubtitleLabel", "Choose a quest and act in your world.",
                18f, TextAlignmentOptions.Center, Muted, FontStyles.Normal, font, fontMaterial);
            PlaceTop(subtitle.rectTransform, new Vector2(0f, -83f), new Vector2(820f, 32f));

            var close = CreateButton(root, "CloseButton", assets.canvas, new Color32(210, 76, 51, 255));
            PlaceTopRight(close.GetComponent<RectTransform>(), new Vector2(-45f, -34f), new Vector2(88f, 88f));
            var closeLabel = CreateText(close.transform as RectTransform, "Label", "×",
                58f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, font, fontMaterial);
            Stretch(closeLabel.rectTransform, 5f);

            var divider = CreateImage(root, "BodyDivider", assets.canvas, new Color32(219, 194, 153, 255), false);
            PlaceTop(divider.rectTransform, new Vector2(-32f, -164f), new Vector2(3f, 710f));

            var questHeader = CreateText(root, "QuestHeader", "ACTIVE QUESTS",
                30f, TextAlignmentOptions.Center, Ink, FontStyles.Bold, font, fontMaterial);
            PlaceTop(questHeader.rectTransform, new Vector2(-492f, -160f), new Vector2(690f, 48f));
            AddHeaderAccents(root, "QuestHeaderAccent", -492f, -182f, 690f, assets);

            var questScroll = CreateScroll(root, "QuestScroll", new Vector2(-492f, -220f),
                new Vector2(690f, 635f), assets);
            var questEmpty = CreateText(questScroll.content, "QuestEmptyLabel", "No active quests",
                24f, TextAlignmentOptions.Center, Muted, FontStyles.Normal, font, fontMaterial);
            SetLayoutHeight(questEmpty.gameObject, 90f);
            questEmpty.gameObject.SetActive(false);
            var questTemplate = CreateQuestRow(questScroll.content, "QuestRowTemplate", assets, font, fontMaterial);
            questTemplate.gameObject.SetActive(false);
            var questPreview = CreatePreviewStack(questScroll.content, "EditorPreviewQuestRows", 3, 154f, 14f);
            CreateQuestSample(questTemplate, questPreview, "QuestPreview_VillageWelcome",
                "Village Welcome", "Talk to the Village Elder.", "ACTIVE", 0.72f, true);
            CreateQuestSample(questTemplate, questPreview, "QuestPreview_HelpFarmer",
                "Help the Farmer", "Water the crops in the field.", "ACTIVE", 0.4f, false);
            CreateQuestSample(questTemplate, questPreview, "QuestPreview_LostTool",
                "Find the Lost Tool", "Find the farmer's lost tool.", "COMPLETED", 1f, false);

            var actionHeader = CreateText(root, "ActionHeader", "AVAILABLE ACTIONS",
                30f, TextAlignmentOptions.Center, Ink, FontStyles.Bold, font, fontMaterial);
            PlaceTop(actionHeader.rectTransform, new Vector2(430f, -160f), new Vector2(780f, 48f));
            AddHeaderAccents(root, "ActionHeaderAccent", 430f, -182f, 780f, assets);

            var detailCard = CreateImage(root, "SelectedQuestCard", assets.surface, BlueSoft, false);
            PlaceTop(detailCard.rectTransform, new Vector2(430f, -220f), new Vector2(780f, 278f));
            var detailIcon = CreateImage(detailCard.rectTransform, "QuestIconFrame", assets.canvas, CreamDark, false);
            PlaceTopLeft(detailIcon.rectTransform, new Vector2(28f, -28f), new Vector2(116f, 116f));
            var detailGlyph = CreateText(detailIcon.rectTransform, "Glyph", "!",
                58f, TextAlignmentOptions.Center, Gold, FontStyles.Bold, font, fontMaterial);
            Stretch(detailGlyph.rectTransform, 12f);

            var selectedTitle = CreateText(detailCard.rectTransform, "SelectedQuestTitle", "Village Welcome",
                34f, TextAlignmentOptions.Left, Ink, FontStyles.Bold, font, fontMaterial);
            PlaceTopLeft(selectedTitle.rectTransform, new Vector2(170f, -30f), new Vector2(470f, 48f));
            var selectedStatusBadge = CreateImage(
                detailCard.rectTransform, "SelectedQuestStatusBadge", assets.greenButton, Color.white, false);
            PlaceTopRight(selectedStatusBadge.rectTransform, new Vector2(-28f, -32f), new Vector2(110f, 38f));
            var selectedStatus = CreateText(selectedStatusBadge.rectTransform, "SelectedQuestStatus", "ACTIVE",
                18f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, font, fontMaterial);
            Stretch(selectedStatus.rectTransform, 3f);

            var selectedReason = CreateText(detailCard.rectTransform, "SelectedQuestReason",
                "Talk to the Village Elder and learn about the valley.",
                22f, TextAlignmentOptions.TopLeft, Ink, FontStyles.Normal, font, fontMaterial);
            PlaceTopLeft(selectedReason.rectTransform, new Vector2(170f, -88f), new Vector2(560f, 76f));
            selectedReason.enableWordWrapping = true;
            var progressCaption = CreateText(detailCard.rectTransform, "ProgressCaption", "PROGRESS",
                17f, TextAlignmentOptions.Left, Muted, FontStyles.Bold, font, fontMaterial);
            PlaceTopLeft(progressCaption.rectTransform, new Vector2(38f, -185f), new Vector2(150f, 28f));
            var detailProgress = CreateProgress(detailCard.rectTransform, "ProgressTrack",
                new Vector2(70f, -198f), new Vector2(500f, 24f), 0.72f, assets);
            var progressValue = CreateText(detailCard.rectTransform, "ProgressValue", "72%",
                18f, TextAlignmentOptions.Right, Ink, FontStyles.Bold, font, fontMaterial);
            PlaceTopRight(progressValue.rectTransform, new Vector2(-28f, -184f), new Vector2(70f, 30f));

            var actionScroll = CreateScroll(root, "ActionScroll", new Vector2(430f, -520f),
                new Vector2(780f, 335f), assets);
            var actionEmpty = CreateText(actionScroll.content, "ActionEmptyLabel", "No available actions",
                24f, TextAlignmentOptions.Center, Muted, FontStyles.Normal, font, fontMaterial);
            SetLayoutHeight(actionEmpty.gameObject, 88f);
            actionEmpty.gameObject.SetActive(false);
            var actionTemplate = CreateActionRow(actionScroll.content, "ActionRowTemplate", assets, font, fontMaterial);
            actionTemplate.gameObject.SetActive(false);
            var actionPreview = CreatePreviewStack(actionScroll.content, "EditorPreviewActionRows", 3, 84f, 12f);
            CreateActionSample(actionTemplate, actionPreview, "ActionPreview_Talk", "TALK", "…", true);
            CreateActionSample(actionTemplate, actionPreview, "ActionPreview_Help", "HELP", "✓", true);
            CreateActionSample(actionTemplate, actionPreview, "ActionPreview_Give", "GIVE ITEM", "▣", false);

            var footerSurface = CreateImage(root, "FooterSurface", assets.surface, CreamDark, false);
            PlaceBottom(footerSurface.rectTransform, new Vector2(0f, 20f), new Vector2(1660f, 68f));
            var footer = CreateText(footerSurface.rectTransform, "Footer",
                "Select a quest or choose an available action.",
                22f, TextAlignmentOptions.Center, Ink, FontStyles.Normal, font, fontMaterial);
            Stretch(footer.rectTransform, 8f);

            StyleToggle(toggleObject, assets, font, fontMaterial);

            var preview = rootObject.GetComponent<OntologyUiEditorPreview>();
            if (preview == null) preview = Undo.AddComponent<OntologyUiEditorPreview>(rootObject);
            var previewSerialized = new SerializedObject(preview);
            SetReference(previewSerialized, "canvasGroup", group);
            previewSerialized.FindProperty("previewInEditor").boolValue = true;
            previewSerialized.ApplyModifiedPropertiesWithoutUndo();

            var serialized = new SerializedObject(panel);
            SetReference(serialized, "panelGroup", group);
            SetReference(serialized, "openButton", toggleObject.GetComponent<Button>());
            SetReference(serialized, "closeButton", close);
            SetReference(serialized, "titleLabel", title);
            SetReference(serialized, "subtitleLabel", subtitle);
            SetReference(serialized, "questHeaderLabel", questHeader);
            SetReference(serialized, "actionHeaderLabel", actionHeader);
            SetReference(serialized, "questEmptyLabel", questEmpty);
            SetReference(serialized, "actionEmptyLabel", actionEmpty);
            SetReference(serialized, "selectedQuestTitleLabel", selectedTitle);
            SetReference(serialized, "selectedQuestReasonLabel", selectedReason);
            SetReference(serialized, "selectedQuestStatusLabel", selectedStatus);
            SetReference(serialized, "selectedQuestProgressFill", detailProgress.fill);
            SetReference(serialized, "selectedQuestProgressLabel", progressValue);
            SetReference(serialized, "footerLabel", footer);
            SetReference(serialized, "questRowsContainer", questScroll.content);
            SetReference(serialized, "actionRowsContainer", actionScroll.content);
            SetReference(serialized, "questRowTemplate", questTemplate);
            SetReference(serialized, "actionRowTemplate", actionTemplate);
            var previewObjects = serialized.FindProperty("editorPreviewObjects");
            previewObjects.arraySize = 2;
            previewObjects.GetArrayElementAtIndex(0).objectReferenceValue = questPreview.gameObject;
            previewObjects.GetArrayElementAtIndex(1).objectReferenceValue = actionPreview.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(rootObject);
            EditorUtility.SetDirty(toggleObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            OntologyUiEditorPreviewSelector.ShowQuestActionPanel();
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV quests and actions UI to " + rootObject.scene.path + ".");
        }

        private static void StyleRoot(GameObject root, Assets assets)
        {
            var image = root.GetComponent<Image>() ?? Undo.AddComponent<Image>(root);
            image.sprite = assets.canvas;
            image.type = Image.Type.Sliced;
            image.color = Blue;
            image.raycastTarget = true;
            var shadow = root.GetComponent<Shadow>() ?? Undo.AddComponent<Shadow>(root);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
            shadow.effectDistance = new Vector2(0f, -10f);
        }

        private static void StyleToggle(GameObject toggleObject, Assets assets, TMP_FontAsset font, Material material)
        {
            var rect = toggleObject.GetComponent<RectTransform>();
            PlaceTopRight(rect, new Vector2(-42f, -42f), new Vector2(106f, 106f));
            ClearChildren(rect);
            var image = toggleObject.GetComponent<Image>() ?? Undo.AddComponent<Image>(toggleObject);
            image.sprite = assets.questToggle != null ? assets.questToggle : assets.blueButton;
            image.type = assets.questToggle != null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;
            image.preserveAspect = assets.questToggle != null;
            var button = toggleObject.GetComponent<Button>() ?? Undo.AddComponent<Button>(toggleObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            if (assets.questToggle != null) return;
            var ring = CreateImage(rect, "QuestIconRing", assets.canvas, Gold, false);
            Stretch(ring.rectTransform, 14f);
            var inner = CreateImage(ring.rectTransform, "QuestIconInner", assets.canvas, Cream, false);
            Stretch(inner.rectTransform, 5f);
            var glyph = CreateText(inner.rectTransform, "Glyph", "!",
                50f, TextAlignmentOptions.Center, Blue, FontStyles.Bold, font, material);
            Stretch(glyph.rectTransform, 8f);
        }

        private static Button CreateQuestRow(RectTransform parent, string name, Assets assets,
            TMP_FontAsset font, Material material)
        {
            var button = CreateButton(parent, name, assets.surface, Color.white);
            var rect = button.GetComponent<RectTransform>();
            SetLayoutHeight(button.gameObject, 154f);
            var label = CreateText(rect, "Label", "Quest title",
                27f, TextAlignmentOptions.Left, Ink, FontStyles.Bold, font, material);
            PlaceTopLeft(label.rectTransform, new Vector2(130f, -22f), new Vector2(350f, 40f));
            var description = CreateText(rect, "DescriptionLabel", "Quest objective",
                19f, TextAlignmentOptions.Left, Ink, FontStyles.Normal, font, material);
            PlaceTopLeft(description.rectTransform, new Vector2(130f, -65f), new Vector2(450f, 32f));
            var iconFrame = CreateImage(rect, "IconFrame", assets.canvas, CreamDark, false);
            PlaceTopLeft(iconFrame.rectTransform, new Vector2(20f, -24f), new Vector2(90f, 90f));
            var glyph = CreateText(iconFrame.rectTransform, "Glyph", "!",
                44f, TextAlignmentOptions.Center, Gold, FontStyles.Bold, font, material);
            Stretch(glyph.rectTransform, 10f);
            CreateProgress(rect, "ProgressTrack", new Vector2(-18f, -119f), new Vector2(225f, 18f), 0.45f, assets);
            var statusBadge = CreateImage(rect, "StatusBadge", assets.greenButton, Color.white, false);
            PlaceTopRight(statusBadge.rectTransform, new Vector2(-20f, -101f), new Vector2(118f, 38f));
            var status = CreateText(statusBadge.rectTransform, "StatusLabel", "ACTIVE",
                16f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, font, material);
            Stretch(status.rectTransform, 4f);
            return button;
        }

        private static Button CreateActionRow(RectTransform parent, string name, Assets assets,
            TMP_FontAsset font, Material material)
        {
            var button = CreateButton(parent, name, assets.canvas, Blue);
            SetLayoutHeight(button.gameObject, 84f);
            var rect = button.GetComponent<RectTransform>();
            var label = CreateText(rect, "Label", "ACTION",
                29f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold, font, material);
            PlaceTop(label.rectTransform, new Vector2(35f, -12f), new Vector2(570f, 58f));
            var iconFrame = CreateImage(rect, "IconFrame", assets.canvas, Cream, false);
            PlaceTopLeft(iconFrame.rectTransform, new Vector2(22f, -11f), new Vector2(62f, 62f));
            var glyph = CreateText(iconFrame.rectTransform, "Glyph", "›",
                38f, TextAlignmentOptions.Center, Blue, FontStyles.Bold, font, material);
            Stretch(glyph.rectTransform, 6f);
            return button;
        }

        private static void CreateQuestSample(Button template, RectTransform parent, string name,
            string title, string description, string status, float progress, bool selected)
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(clone, "Create quest preview row");
            clone.name = name;
            clone.SetActive(true);
            FindText(clone.transform, "Label").text = title;
            FindText(clone.transform, "DescriptionLabel").text = description;
            FindText(clone.transform, "StatusBadge/StatusLabel").text = status;
            FindImage(clone.transform, "ProgressTrack/ProgressFill").fillAmount = progress;
            var image = clone.GetComponent<Image>();
            image.color = selected ? new Color32(234, 244, 255, 255) : Color.white;
        }

        private static void CreateActionSample(Button template, RectTransform parent, string name,
            string label, string glyph, bool enabled)
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(clone, "Create action preview row");
            clone.name = name;
            clone.SetActive(true);
            clone.GetComponent<Button>().interactable = enabled;
            FindText(clone.transform, "Label").text = label;
            FindText(clone.transform, "IconFrame/Glyph").text = glyph;
            if (!enabled)
            {
                var image = clone.GetComponent<Image>();
                image.sprite = LoadAssets().grayButton;
                image.color = Color.white;
            }
        }

        private static RectTransform CreatePreviewStack(RectTransform parent, string name,
            int rowCount, float rowHeight, float spacing)
        {
            var rect = CreateRect(parent, name);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            SetLayoutHeight(rect.gameObject, rowCount * rowHeight + (rowCount - 1) * spacing + 8f);
            return rect;
        }

        private static ScrollParts CreateScroll(RectTransform parent, string name, Vector2 position,
            Vector2 size, Assets assets)
        {
            var root = CreateImage(parent, name, assets.surface, new Color32(250, 243, 229, 255), true);
            PlaceTop(root.rectTransform, position, size);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var viewport = CreateImage(root.rectTransform, "Viewport", assets.canvas, Color.white, false);
            Stretch(viewport.rectTransform, 12f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var content = CreateRect(viewport.rectTransform, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.rectTransform;
            scroll.content = content;
            return new ScrollParts { root = root.rectTransform, content = content };
        }

        private static ProgressParts CreateProgress(RectTransform parent, string name,
            Vector2 position, Vector2 size, float value, Assets assets)
        {
            var track = CreateImage(parent, name, assets.canvas, CreamDark, false);
            PlaceTop(track.rectTransform, position, size);
            var fill = CreateImage(track.rectTransform, "ProgressFill", null, Green, false);
            Stretch(fill.rectTransform, 2f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = value;
            return new ProgressParts { track = track, fill = fill };
        }

        private static void AddHeaderAccents(RectTransform parent, string name, float x, float y,
            float width, Assets assets)
        {
            var left = CreateImage(parent, name + "Left", assets.canvas, Gold, false);
            PlaceTop(left.rectTransform, new Vector2(x - width * 0.34f, y), new Vector2(width * 0.22f, 3f));
            var right = CreateImage(parent, name + "Right", assets.canvas, Gold, false);
            PlaceTop(right.rectTransform, new Vector2(x + width * 0.34f, y), new Vector2(width * 0.22f, 3f));
        }

        private static Button CreateButton(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            return button;
        }

        private static Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color, bool raycast)
        {
            var rect = CreateRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, string value,
            float size, TextAlignmentOptions alignment, Color color, FontStyles style,
            TMP_FontAsset font, Material material)
        {
            var rect = CreateRect(parent, name);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = material;
            }
            text.text = value;
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static RectTransform CreateRect(RectTransform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(child, "Create " + name);
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void SetLayoutHeight(GameObject target, float height)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (var index = parent.childCount - 1; index >= 0; index--)
                Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
        }

        private static void Center(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
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

        private static void PlaceBottom(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-inset * 2f, -inset * 2f);
            rect.localScale = Vector3.one;
        }

        private static TMP_Text FindText(Transform root, string path) =>
            root.Find(path)?.GetComponent<TMP_Text>();

        private static Image FindImage(Transform root, string path) =>
            root.Find(path)?.GetComponent<Image>();

        private static void SetReference(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            var property = target.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException("Serialized property not found: " + propertyName);
            property.objectReferenceValue = value;
        }

        private static GameObject FindSceneObject(string name) => Resources.FindObjectsOfTypeAll<Transform>()
            .FirstOrDefault(candidate =>
                candidate != null && candidate.gameObject.scene.IsValid() && candidate.name == name)?.gameObject;

        private static Assets LoadAssets()
        {
            var basePath = "Assets/maanetorn/Farm Game UI - Simple 2D UI/Sprites/256x256/";
            var smallPath = "Assets/maanetorn/Farm Game UI - Simple 2D UI/Sprites/128x128/";
            var assets = new Assets
            {
                logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png"),
                canvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png"),
                surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png"),
                titlePlate = AssetDatabase.LoadAssetAtPath<Sprite>(basePath + "panel_title_with_stroke_2.png"),
                blueButton = AssetDatabase.LoadAssetAtPath<Sprite>(basePath + "button_blue_default_2.png"),
                greenButton = AssetDatabase.LoadAssetAtPath<Sprite>(basePath + "button_green_default_2.png"),
                grayButton = AssetDatabase.LoadAssetAtPath<Sprite>(basePath + "button_gray_default_2.png"),
                redButton = AssetDatabase.LoadAssetAtPath<Sprite>(basePath + "button_red_default_2.png"),
                questToggle = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/UI/Textures/TOV/quest_actions_toggle_v2.png"),
                sliderBackground = AssetDatabase.LoadAssetAtPath<Sprite>(smallPath + "slider_background_1.png"),
                sliderFill = AssetDatabase.LoadAssetAtPath<Sprite>(smallPath + "slider_fill_1.png")
            };
            if (assets.logo == null || assets.canvas == null || assets.surface == null || assets.blueButton == null)
                throw new InvalidOperationException("TOV quest/action art assets are not imported.");
            return assets;
        }

        private sealed class Assets
        {
            public Sprite logo;
            public Sprite canvas;
            public Sprite surface;
            public Sprite titlePlate;
            public Sprite blueButton;
            public Sprite greenButton;
            public Sprite grayButton;
            public Sprite redButton;
            public Sprite questToggle;
            public Sprite sliderBackground;
            public Sprite sliderFill;
        }

        private sealed class ScrollParts
        {
            public RectTransform root;
            public RectTransform content;
        }

        private sealed class ProgressParts
        {
            public Image track;
            public Image fill;
        }
    }
}
#endif
