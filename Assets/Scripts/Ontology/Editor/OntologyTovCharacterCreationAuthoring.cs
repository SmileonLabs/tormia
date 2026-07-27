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
    /// Applies the approved TOV character-creation composition to the existing
    /// hierarchy-owned picker. Runtime code continues to own behavior only.
    /// </summary>
    public static class OntologyTovCharacterCreationAuthoring
    {
        private const string FlatFolder = "Assets/Art/UI/CharacterCreationFlat/";
        private const string LogoPath = "Assets/Art/UI/AccountLogin/tov_logo.png";
        private const string CirclePath = "Assets/Art/UI/CharacterCreationFlat/circle_antialias_256.asset";
        private const string CategoryIconFolder = "Assets/Art/UI/CharacterCategoryIcons/";

        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Navy = new(22, 53, 72, 255);
        private static readonly Color32 MutedNavy = new(80, 101, 112, 190);

        [MenuItem("Tormia/UI/Apply Editable TOV Character Creation Design")]
        public static void Apply()
        {
            OntologyCharacterCreationStudioAuthoring.Author();

            var appearance = GameObject.Find("AccountCharacterAppearancePanel");
            var creation = GameObject.Find("AccountCharacterCreationPanel");
            if (appearance == null || creation == null)
                throw new InvalidOperationException("Character creation roots are missing from the open scene.");

            Undo.RegisterFullObjectHierarchyUndo(appearance, "Apply editable TOV character creation design");
            Undo.RegisterFullObjectHierarchyUndo(creation, "Apply editable TOV character creation overlay");

            var flatCanvas = Sprite("flat_canvas.png");
            var flatSurface = Sprite("flat_surface.png");
            var flatInput = Sprite("flat_input.png");
            var logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            var circle = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
            if (logoSprite == null || circle == null)
                throw new InvalidOperationException("TOV character creation artwork is missing.");

            StyleAppearanceRoot(appearance, flatCanvas, flatSurface, logoSprite);
            StyleCategoryRail(appearance, flatCanvas);
            StylePartGrid(appearance, circle);
            StylePreview(appearance, circle);
            StyleCreationOverlay(creation, flatCanvas, flatInput);
            BuildEditorPreview(appearance, circle);

            if (appearance.GetComponent<OntologyUiEditorPreview>() == null)
                Undo.AddComponent<OntologyUiEditorPreview>(appearance);
            if (creation.GetComponent<OntologyUiEditorPreview>() == null)
                Undo.AddComponent<OntologyUiEditorPreview>(creation);

            OntologyUiEditorPreviewSelector.ShowCharacterCreation();

            creation.transform.SetAsLastSibling();
            EditorUtility.SetDirty(appearance);
            EditorUtility.SetDirty(creation);
            EditorSceneManager.MarkSceneDirty(appearance.scene);
            EditorSceneManager.SaveScene(appearance.scene);
            Debug.Log("Applied editable TOV character creation UI to " + appearance.scene.path + ".");
        }

        private static void StyleAppearanceRoot(GameObject rootObject, Sprite flatCanvas, Sprite flatSurface, Sprite logoSprite)
        {
            var root = rootObject.GetComponent<RectTransform>();
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1840f, 1000f));
            StyleImage(rootObject, flatCanvas, Blue, true);
            var shadow = rootObject.GetComponent<Shadow>() ?? Undo.AddComponent<Shadow>(rootObject);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.34f);
            shadow.effectDistance = new Vector2(0f, -8f);
            shadow.useGraphicAlpha = true;

            var gold = EnsureImage(root, "BrandFrameGold");
            StretchInset(gold.rectTransform, 7f);
            StyleImage(gold.gameObject, flatCanvas, Gold, false);
            gold.transform.SetSiblingIndex(0);

            var surface = EnsureImage(root, "CardSurface");
            StretchInset(surface.rectTransform, 13f);
            StyleImage(surface.gameObject, flatSurface, Cream, true);
            surface.transform.SetSiblingIndex(1);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTopLeft(logo.rectTransform, new Vector2(28f, -14f), new Vector2(190f, 108f));
            logo.sprite = logoSprite;
            logo.type = Image.Type.Simple;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;
            logo.transform.SetSiblingIndex(2);

            var header = root.Find("Header");
            if (header != null) header.gameObject.SetActive(false);
            var detail = root.Find("DetailArea");
            if (detail != null) detail.gameObject.SetActive(false);
            var previewLabel = root.Find("PreviewLabel");
            if (previewLabel != null) previewLabel.gameObject.SetActive(false);
        }

        private static void StyleCategoryRail(GameObject rootObject, Sprite flatCanvas)
        {
            var root = rootObject.GetComponent<RectTransform>();
            var area = Require<RectTransform>(root, "CategoryArea");
            PlaceTopLeft(area, new Vector2(32f, -150f), new Vector2(112f, 720f));
            StyleImage(area.gameObject, flatCanvas, Blue, false);
            var label = area.Find("CategorySectionLabel");
            if (label != null) label.gameObject.SetActive(false);

            var scroll = Require<RectTransform>(area, "CategoryScrollView");
            StretchInset(scroll, 7f);
            RemoveGraphic(scroll.gameObject);
            var scrollRect = scroll.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.verticalScrollbar = null;
                scrollRect.scrollSensitivity = 32f;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }
            DestroyChild(scroll, "VerticalScrollbar");

            var content = Require<RectTransform>(scroll, "CategoryViewport/CategoryContent");
            content.sizeDelta = new Vector2(92f, content.sizeDelta.y);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(0, 0, 4, 4);
                layout.spacing = 2f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            var template = Require<RectTransform>(root, "Templates/CategoryButton_Template");
            template.sizeDelta = new Vector2(92f, 82f);
            var icon = Require<RectTransform>(template, "Icon");
            Place(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f));
            var buttonImage = template.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.sprite = null;
                buttonImage.color = Color.clear;
                buttonImage.raycastTarget = true;
            }
        }

        private static void StylePartGrid(GameObject rootObject, Sprite circle)
        {
            var root = rootObject.GetComponent<RectTransform>();
            var area = Require<RectTransform>(root, "PartGridArea");
            PlaceTopLeft(area, new Vector2(158f, -150f), new Vector2(520f, 720f));
            StyleImage(area.gameObject, null, Color.clear, false);

            var label = Require<TextMeshProUGUI>(area, "PartSectionLabel");
            label.gameObject.SetActive(true);
            label.text = L("character.ui.section.parts", "CHOOSE PARTS");
            PlaceTopLeft(label.rectTransform, new Vector2(18f, -8f), new Vector2(480f, 38f));
            StyleText(label, 23f, TextAlignmentOptions.Left, Blue, FontStyles.Bold);

            var scroll = Require<RectTransform>(area, "ScrollView");
            Place(scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -24f), new Vector2(-12f, -56f));
            RemoveGraphic(scroll.gameObject);
            var scrollRect = scroll.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.verticalScrollbar = null;
                scrollRect.scrollSensitivity = 36f;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }
            DestroyChild(scroll, "VerticalScrollbar");

            var content = Require<RectTransform>(scroll, "Viewport/PartGridContent");
            content.sizeDelta = new Vector2(470f, content.sizeDelta.y);
            var grid = content.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.cellSize = new Vector2(108f, 108f);
                grid.spacing = new Vector2(8f, 8f);
                grid.padding = new RectOffset(3, 3, 3, 3);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 4;
                grid.childAlignment = TextAnchor.UpperLeft;
            }

            var template = Require<RectTransform>(root, "Templates/PartCard_Template");
            template.sizeDelta = new Vector2(108f, 108f);
            var mask = Require<RectTransform>(template, "ThumbnailMask");
            Place(mask, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 92f));
            var maskImage = mask.GetComponent<Image>();
            if (maskImage != null) maskImage.sprite = circle;
            var thumbnailIcon = Require<RectTransform>(mask, "Icon");
            StretchInset(thumbnailIcon, 0f);
            var thumbnailImage = thumbnailIcon.GetComponent<Image>();
            if (thumbnailImage != null)
            {
                // Fill the mask so square source thumbnails are clipped into a
                // true circle instead of remaining a smaller square inside it.
                thumbnailImage.preserveAspect = false;
                thumbnailImage.raycastTarget = false;
            }
            var outline = Require<RectTransform>(template, "SelectionOutline");
            Place(outline, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(102f, 102f));
            var outlineImage = outline.GetComponent<Image>();
            if (outlineImage != null) outlineImage.sprite = circle;
            foreach (var childName in new[] { "Text", "Label", "State", "NoIcon", "Badge" })
            {
                var child = template.Find(childName);
                if (child != null) child.gameObject.SetActive(false);
            }
        }

        private static void StylePreview(GameObject rootObject, Sprite circle)
        {
            var root = rootObject.GetComponent<RectTransform>();
            var frame = Require<RectTransform>(root, "CharacterPreviewFrame");
            PlaceTopLeft(frame, new Vector2(700f, -150f), new Vector2(1100f, 650f));
            StyleImage(frame.gameObject, null, Color.clear, false);
            var oldShadow = frame.GetComponent<Shadow>();
            if (oldShadow != null) oldShadow.enabled = false;

            var stage = EnsureImage(frame, "CharacterStage");
            Place(stage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(520f, 132f));
            stage.sprite = circle;
            stage.type = Image.Type.Simple;
            stage.color = new Color32(232, 215, 185, 180);
            stage.raycastTarget = false;
            stage.transform.SetSiblingIndex(0);

            var live = Require<RectTransform>(frame, "LiveCharacterPreview");
            StretchInset(live, 8f);
            live.SetSiblingIndex(1);

            var presenter = rootObject.GetComponent<OntologyCharacterCreationPreviewPresenter>();
            if (presenter != null)
            {
                var presenterObject = new SerializedObject(presenter);
                presenterObject.FindProperty("fieldOfView").floatValue = 25f;
                presenterObject.FindProperty("framingDistance").floatValue = 3.25f;
                presenterObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var status = Require<TextMeshProUGUI>(root, "Status");
            Place(status.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(174f, 104f), new Vector2(490f, 28f));
            StyleText(status, 14f, TextAlignmentOptions.Left, MutedNavy, FontStyles.Normal);
        }

        private static void StyleCreationOverlay(GameObject rootObject, Sprite flatCanvas, Sprite flatInput)
        {
            var root = rootObject.GetComponent<RectTransform>();
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1840f, 1000f));
            StyleImage(rootObject, null, Color.clear, false);

            var heading = Require<RectTransform>(root, "CharacterCreationHeading");
            Place(heading, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(-40f, 116f));
            StyleImage(heading.gameObject, null, Color.clear, false);
            var headingText = heading.GetComponentInChildren<TextMeshProUGUI>(true);
            Place(headingText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(-520f, -10f));
            StyleText(headingText, 31f, TextAlignmentOptions.Center, Navy, FontStyles.Normal);
            headingText.richText = true;
            headingText.text = "<size=31><b>" + L("ui.account.character_create.title", "CREATE YOUR CHARACTER") +
                               "</b></size>\n<size=17>" + L("ui.account.character_create.prompt", "Choose parts to complete your character.") + "</size>";
            var step = heading.Find("StepLabel");
            if (step != null) step.gameObject.SetActive(false);

            var identity = Require<RectTransform>(root, "CharacterIdentityCard");
            Place(identity, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(790f, 114f), new Vector2(990f, 106f));
            StyleImage(identity.gameObject, null, Color.clear, false);
            var nameLabel = Require<TextMeshProUGUI>(identity, "CharacterNameLabel");
            nameLabel.text = L("ui.account.character_create.name", "CHARACTER NAME");
            PlaceTopLeft(nameLabel.rectTransform, new Vector2(24f, -4f), new Vector2(920f, 28f));
            StyleText(nameLabel, 17f, TextAlignmentOptions.Left, Navy, FontStyles.Bold);
            var input = Require<RectTransform>(identity, "CharacterNameInput");
            Place(input, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(920f, 54f));
            var nameInput = StyleInput(input, flatInput);
            if (nameInput.placeholder is TMP_Text placeholder)
                placeholder.text = L("ui.account.character_create.name_placeholder", "Enter a memorable name");

            var guidance = root.Find("AppearanceGuidance");
            if (guidance != null) guidance.gameObject.SetActive(false);

            var footer = Require<RectTransform>(root, "CharacterCreationFooter");
            Place(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(-40f, 72f));
            StyleImage(footer.gameObject, null, Color.clear, false);
            var back = StyleButton(footer, "BackButton", "BackButtonSurface", new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(240f, 58f), false, flatCanvas);
            var create = StyleButton(footer, "ContinueToWorldButton", "CreateButtonSurface", new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(260f, 58f), true, flatCanvas);
            SetButtonLabel(back, L("ui.account.back", "BACK"));
            SetButtonLabel(create, L("ui.account.character_create.create", "CREATE"));

            var panel = rootObject.GetComponent<OntologyAccountCharacterCreationPanel>();
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("backButton").objectReferenceValue = back;
            serialized.FindProperty("createButton").objectReferenceValue = create;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildEditorPreview(GameObject rootObject, Sprite circle)
        {
            var root = rootObject.GetComponent<RectTransform>();
            var preview = Require<RectTransform>(root, "EditorPreviewContent");
            for (var i = preview.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(preview.GetChild(i).gameObject);
            StretchInset(preview, 0f);
            preview.gameObject.SetActive(true);

            var categoryTemplate = Require<RectTransform>(root, "Templates/CategoryButton_Template");
            var categoryIds = OntologyCharacterCustomizationUiConfig.CategoryOrder;
            var categoryCount = Mathf.Min(8, categoryIds.Length);
            for (var i = 0; i < categoryCount; i++)
            {
                var item = UnityEngine.Object.Instantiate(categoryTemplate.gameObject, preview).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(item.gameObject, "Create preview category");
                item.name = "PreviewCategory_" + categoryIds[i];
                item.gameObject.SetActive(true);
                Place(item, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-833f, 286f - i * 82f), new Vector2(92f, 82f));
                var icon = item.Find("Icon")?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CategoryIconFolder + "Category_" + categoryIds[i] + ".png");
                    icon.enabled = icon.sprite != null;
                    icon.preserveAspect = true;
                }
            }

            var partTemplate = Require<RectTransform>(root, "Templates/PartCard_Template");
            var panel = rootObject.GetComponent<OntologyCharacterCustomizationPanel>();
            var panelObject = new SerializedObject(panel);
            var database = panelObject.FindProperty("partDatabase").objectReferenceValue as OntologyCharacterPartDatabase;
            var definitions = database == null
                ? Array.Empty<OntologyCharacterPartDefinition>()
                : database.Definitions.Where(definition => definition != null && definition.visibleInCustomization && definition.icon != null).Take(16).ToArray();
            for (var i = 0; i < definitions.Length; i++)
            {
                var item = UnityEngine.Object.Instantiate(partTemplate.gameObject, preview).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(item.gameObject, "Create preview part");
                item.name = "PreviewPart_" + definitions[i].partId;
                item.gameObject.SetActive(true);
                var column = i % 4;
                var row = i / 4;
                Place(item, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-700f + column * 116f, 274f - row * 116f), new Vector2(108f, 108f));
                var icon = (item.Find("ThumbnailMask/Icon") ?? item.Find("Icon"))?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = definitions[i].icon;
                    icon.enabled = true;
                    icon.preserveAspect = true;
                }
                var outline = item.Find("SelectionOutline");
                if (outline != null)
                {
                    outline.gameObject.SetActive(i == 0);
                    var outlineImage = outline.GetComponent<Image>();
                    if (outlineImage != null)
                    {
                        outlineImage.sprite = circle;
                        outlineImage.color = Gold;
                    }
                }
            }
        }

        private static TMP_InputField StyleInput(RectTransform input, Sprite sprite)
        {
            StyleImage(input.gameObject, sprite, Color.white, true);
            var field = input.GetComponent<TMP_InputField>() ??
                        Undo.AddComponent<TMP_InputField>(input.gameObject);
            field.targetGraphic = input.GetComponent<Image>();
            field.contentType = TMP_InputField.ContentType.Standard;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterValidation = TMP_InputField.CharacterValidation.None;
            field.characterLimit = 0;
            field.readOnly = false;

            var viewport = EnsureRect(input, "Text Area");
            StretchInset(viewport, 12f);
            if (viewport.GetComponent<RectMask2D>() == null)
                Undo.AddComponent<RectMask2D>(viewport.gameObject);

            var text = field.textComponent as TextMeshProUGUI;
            text ??= input.Find("Text")?.GetComponent<TextMeshProUGUI>();
            text ??= viewport.Find("Text")?.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                var textRect = EnsureRect(viewport, "Text");
                text = Undo.AddComponent<TextMeshProUGUI>(textRect.gameObject);
            }
            text.rectTransform.SetParent(viewport, false);
            text.name = "Text";
            StretchInset(text.rectTransform, 0f);
            StyleText(text, 22f, TextAlignmentOptions.Left, Navy, FontStyles.Normal);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            var placeholder = field.placeholder as TextMeshProUGUI;
            placeholder ??= viewport.Find("Placeholder")?.GetComponent<TextMeshProUGUI>();
            if (placeholder == null)
            {
                var placeholderRect = EnsureRect(viewport, "Placeholder");
                placeholder = Undo.AddComponent<TextMeshProUGUI>(placeholderRect.gameObject);
            }
            placeholder.rectTransform.SetParent(viewport, false);
            placeholder.name = "Placeholder";
            StretchInset(placeholder.rectTransform, 0f);
            placeholder.font = text.font;
            placeholder.text = L(
                "ui.account.character_create.name_placeholder",
                "Enter a memorable name");
            StyleText(
                placeholder,
                21f,
                TextAlignmentOptions.Left,
                new Color32(80, 101, 112, 125),
                FontStyles.Normal);
            placeholder.enableWordWrapping = false;
            placeholder.transform.SetAsFirstSibling();
            text.transform.SetAsLastSibling();

            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.customCaretColor = true;
            field.caretColor = Navy;
            field.selectionColor = new Color32(91, 151, 220, 120);
            return field;
        }

        private static Button StyleButton(RectTransform parent, string name, string surfaceName, Vector2 anchor, Vector2 position, Vector2 size, bool primary, Sprite sprite)
        {
            var rect = Require<RectTransform>(parent, name);
            Place(rect, anchor, anchor, anchor, position, size);
            var custom = rect.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "FGUIStarter.CustomButton");
            if (custom != null) Undo.DestroyObjectImmediate(custom);
            var button = rect.GetComponent<Button>() ?? Undo.AddComponent<Button>(rect.gameObject);
            var outline = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            outline.sprite = sprite;
            outline.type = Image.Type.Sliced;
            outline.color = primary ? Gold : Blue;
            outline.raycastTarget = true;
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;

            var surface = EnsureImage(rect, surfaceName);
            StretchInset(surface.rectTransform, 3f);
            StyleImage(surface.gameObject, sprite, primary ? Blue : Cream, false);
            surface.transform.SetSiblingIndex(0);

            var label = rect.GetComponentInChildren<TextMeshProUGUI>(true);
            StretchInset(label.rectTransform, 4f);
            label.rectTransform.sizeDelta = new Vector2(-24f, -8f);
            StyleText(label, 26f, TextAlignmentOptions.Center, primary ? Cream : Blue, FontStyles.Bold);
            label.transform.SetAsLastSibling();
            return button;
        }

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

        private static Image EnsureImage(RectTransform parent, string name)
        {
            var rect = EnsureRect(parent, name);
            return rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
        }

        private static T Require<T>(RectTransform parent, string path) where T : Component
        {
            var child = parent.Find(path);
            var component = child != null ? child.GetComponent<T>() : null;
            if (component == null) throw new InvalidOperationException(path + " is missing " + typeof(T).Name + ".");
            return component;
        }

        private static void StyleText(TextMeshProUGUI text, float size, TextAlignmentOptions alignment, Color color, FontStyles style)
        {
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = style;
            text.raycastTarget = false;
        }

        private static void StyleImage(GameObject target, Sprite sprite, Color color, bool raycast)
        {
            var image = target.GetComponent<Image>() ?? Undo.AddComponent<Image>(target);
            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
        }

        private static void RemoveGraphic(GameObject target)
        {
            var mask = target.GetComponent<Mask>();
            if (mask != null) Undo.DestroyObjectImmediate(mask);
            var image = target.GetComponent<Image>();
            if (image != null) Undo.DestroyObjectImmediate(image);
            var renderer = target.GetComponent<CanvasRenderer>();
            if (renderer != null) Undo.DestroyObjectImmediate(renderer);
        }

        private static void DestroyChild(RectTransform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
        }

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        }

        private static void StretchInset(RectTransform rect, float inset)
        {
            Place(rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-inset * 2f, -inset * 2f));
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(FlatFolder + name);

        private static string L(string key, string fallback) => OntologyLanguagePackService.Text(key, fallback);

        private static void SetButtonLabel(Button button, string value)
        {
            var label = button == null ? null : button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = value;
        }
    }
}
#endif
