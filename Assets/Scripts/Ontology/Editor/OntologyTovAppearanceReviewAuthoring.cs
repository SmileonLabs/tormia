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
    /// <summary>Builds the approved appearance-review concept as editable uGUI hierarchy objects.</summary>
    public static class OntologyTovAppearanceReviewAuthoring
    {
        private const string RootName = "AccountAppearanceReviewPanel";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(142, 121, 91, 255);
        private static readonly Color32 CardLine = new(215, 189, 148, 255);

        [MenuItem("Tormia/UI/Apply Editable TOV Appearance Review Design")]
        public static void Apply()
        {
            var rootObject = FindSceneObject(RootName);
            if (rootObject == null) throw new InvalidOperationException(RootName + " was not found.");
            var root = rootObject.GetComponent<RectTransform>();
            var assets = LoadAssets();
            var fontSource = rootObject.GetComponentInChildren<TMP_Text>(true);

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(1740f, 1020f);
            root.localScale = Vector3.one;
            PrepareRoot(root, assets);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTopLeft(logo.rectTransform, new Vector2(42f, -30f), new Vector2(250f, 145f));
            logo.sprite = assets.logo;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;

            var title = RequireOrCreateText(root, "AppearanceReviewTitle", fontSource);
            PlaceTop(title.rectTransform, new Vector2(0f, -42f), new Vector2(760f, 72f));
            StyleText(title, 44f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = "APPEARANCE REVIEW";
            var helper = RequireOrCreateText(root, "AppearanceReviewHelper", fontSource);
            PlaceTop(helper.rectTransform, new Vector2(0f, -116f), new Vector2(760f, 42f));
            StyleText(helper, 21f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            helper.text = "Review your character before continuing.";

            var previewCard = EnsureImage(root, "CharacterPreviewCard");
            PlaceTop(previewCard.rectTransform, new Vector2(-405f, -190f), new Vector2(720f, 610f));
            StyleImage(previewCard, assets.surface, new Color32(238, 220, 188, 255), false);
            var previewMask = EnsureRect(previewCard.rectTransform, "PreviewMask");
            StretchInset(previewMask, 8f);
            if (previewMask.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(previewMask.gameObject);
            var fallbackRect = EnsureRect(previewMask, "FallbackCharacterPreview");
            StretchInset(fallbackRect, 0f);
            var fallback = fallbackRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(fallbackRect.gameObject);
            fallback.texture = assets.preview;
            fallback.uvRect = new Rect(0f, 0f, 1f / 3f, 1f);
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

            var infoCard = EnsureImage(root, "AppearanceInfoCard");
            PlaceTop(infoCard.rectTransform, new Vector2(405f, -190f), new Vector2(720f, 610f));
            StyleImage(infoCard, assets.surface, Cream, false);
            var characterHeading = AddInfoText(infoCard.rectTransform, "CharacterHeading", "CHARACTER",
                new Vector2(50f, -52f), new Vector2(620f, 34f), 21f, Muted, FontStyles.Bold, fontSource);
            var characterName = AddInfoText(infoCard.rectTransform, "CharacterNameValue", "Haneul",
                new Vector2(50f, -94f), new Vector2(620f, 58f), 36f, Ink, FontStyles.Bold, fontSource);
            AddDivider(infoCard.rectTransform, "CharacterDivider", -176f);
            var templateHeading = AddInfoText(infoCard.rectTransform, "TemplateHeading", "TEMPLATE",
                new Vector2(50f, -212f), new Vector2(620f, 34f), 21f, Muted, FontStyles.Bold, fontSource);
            var templateValue = AddInfoText(infoCard.rectTransform, "TemplateValue", "Default Adventurer",
                new Vector2(50f, -254f), new Vector2(620f, 52f), 31f, new Color32(40, 91, 163, 255), FontStyles.Bold, fontSource);
            AddDivider(infoCard.rectTransform, "TemplateDivider", -332f);
            var partsHeading = AddInfoText(infoCard.rectTransform, "EquippedPartsHeading", "EQUIPPED PARTS",
                new Vector2(50f, -365f), new Vector2(620f, 34f), 21f, Muted, FontStyles.Bold, fontSource);

            var slots = new OntologyAppearanceReviewPartSlot[4];
            for (var index = 0; index < slots.Length; index++)
                slots[index] = StylePartSlot(infoCard.rectTransform, index, fontSource, assets);

            var oldSummaryRect = root.Find("AppearanceOverflowSummary") as RectTransform
                ?? root.Find("Text (TMP)") as RectTransform;
            if (oldSummaryRect == null) oldSummaryRect = EnsureRect(root, "AppearanceOverflowSummary");
            oldSummaryRect.name = "AppearanceOverflowSummary";
            var summary = oldSummaryRect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(oldSummaryRect.gameObject);
            if (fontSource != null) { summary.font = fontSource.font; summary.fontSharedMaterial = fontSource.fontSharedMaterial; }
            PlaceTop(summary.rectTransform, new Vector2(405f, -765f), new Vector2(650f, 34f));
            StyleText(summary, 17f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            summary.text = string.Empty;

            var back = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(-660f, -900f),
                new Vector2(270f, 72f), false, fontSource, assets);
            var next = StyleButton(root, "ContinueToWorldButton", "ContinueButtonSurface", new Vector2(660f, -900f),
                new Vector2(300f, 72f), true, fontSource, assets);

            RemoveRootPresentation(rootObject);
            if (rootObject.GetComponent<OntologyUiEditorPreview>() == null) Undo.AddComponent<OntologyUiEditorPreview>(rootObject);
            var panel = rootObject.GetComponent<OntologyAccountAppearanceReviewPanel>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<OntologyCharacterPartAdapter>();
            var serializedPanel = new SerializedObject(panel);
            SetReference(serializedPanel, "panelGroup", rootObject.GetComponent<CanvasGroup>());
            SetReference(serializedPanel, "titleLabel", title);
            SetReference(serializedPanel, "helperLabel", helper);
            SetReference(serializedPanel, "characterHeadingLabel", characterHeading);
            SetReference(serializedPanel, "characterNameLabel", characterName);
            SetReference(serializedPanel, "templateHeadingLabel", templateHeading);
            SetReference(serializedPanel, "templateValueLabel", templateValue);
            SetReference(serializedPanel, "equippedPartsHeadingLabel", partsHeading);
            SetReference(serializedPanel, "summaryLabel", summary);
            SetReference(serializedPanel, "partDatabase", adapter != null ? adapter.PartDatabase : null);
            SetReference(serializedPanel, "continueButton", next);
            SetReference(serializedPanel, "backButton", back);
            var slotsProperty = serializedPanel.FindProperty("partSlots");
            slotsProperty.arraySize = slots.Length;
            for (var index = 0; index < slots.Length; index++)
                slotsProperty.GetArrayElementAtIndex(index).objectReferenceValue = slots[index];
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            BindEditorPartSamples(slots, adapter != null ? adapter.PartDatabase : null);
            Configure3dPreview(rootObject, live, fallback, adapter);

            var group = rootObject.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            OntologyUiEditorPreviewSelector.ShowAppearanceReview();
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV appearance review UI to " + rootObject.scene.path + ".");
        }

        [MenuItem("Tormia/UI/Apply Editable TOV Appearance Review Design", true)]
        private static bool CanApply() => FindSceneObject(RootName) != null;

        private static OntologyAppearanceReviewPartSlot StylePartSlot(
            RectTransform parent, int index, TMP_Text fontSource, Assets assets)
        {
            var root = EnsureRect(parent, "EquippedPartSlot" + (index + 1));
            PlaceTop(root, new Vector2(-255f + index * 170f, -412f), new Vector2(150f, 176f));
            var circle = EnsureImage(root, "PartCircle");
            PlaceTop(circle.rectTransform, new Vector2(0f, 0f), new Vector2(130f, 130f));
            circle.sprite = assets.circle != null ? assets.circle : assets.canvas;
            circle.type = Image.Type.Simple;
            circle.color = new Color32(241, 225, 197, 255);
            circle.raycastTarget = false;
            circle.preserveAspect = true;
            var mask = circle.GetComponent<Mask>() ?? Undo.AddComponent<Mask>(circle.gameObject);
            mask.enabled = true;
            mask.showMaskGraphic = true;
            var icon = EnsureImage(circle.rectTransform, "PartIcon");
            StretchInset(icon.rectTransform, -12f);
            icon.preserveAspect = true;
            icon.maskable = true;
            icon.raycastTarget = false;
            var label = RequireOrCreateText(root, "PartLabel", fontSource);
            PlaceTop(label.rectTransform, new Vector2(0f, -135f), new Vector2(148f, 38f));
            StyleText(label, 18f, TextAlignmentOptions.Center, Ink, FontStyles.Normal);
            label.text = new[] { "Hair", "Top", "Bottom", "Shoes" }[index];
            var slot = root.GetComponent<OntologyAppearanceReviewPartSlot>()
                ?? Undo.AddComponent<OntologyAppearanceReviewPartSlot>(root.gameObject);
            var serialized = new SerializedObject(slot);
            SetReference(serialized, "icon", icon);
            SetReference(serialized, "label", label);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        private static void Configure3dPreview(
            GameObject panelObject, RawImage live, RawImage fallback, OntologyCharacterPartAdapter adapter)
        {
            var studioObject = FindSceneObject("AppearanceReviewPreviewStudio");
            if (studioObject == null)
            {
                studioObject = new GameObject("AppearanceReviewPreviewStudio");
                Undo.RegisterCreatedObjectUndo(studioObject, "Create appearance review preview studio");
            }
            studioObject.transform.position = new Vector3(0f, -2040f, 0f);
            studioObject.transform.rotation = Quaternion.identity;
            studioObject.transform.localScale = Vector3.one;
            var slotRoot = EnsureTransform(studioObject.transform, "SelectedCharacterPreviewSlot");
            slotRoot.localPosition = Vector3.zero;
            slotRoot.localRotation = Quaternion.identity;
            slotRoot.localScale = Vector3.one;
            var anchor = EnsureTransform(slotRoot, "ModelAnchor");
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

            var presenter = panelObject.GetComponent<OntologyAccountCharacterSelectionPreviewPresenter>()
                ?? Undo.AddComponent<OntologyAccountCharacterSelectionPreviewPresenter>(panelObject);
            var entryFlow = UnityEngine.Object.FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            var serialized = new SerializedObject(presenter);
            SetReference(serialized, "entryFlow", entryFlow);
            SetReference(serialized, "panelGroup", panelObject.GetComponent<CanvasGroup>());
            SetReference(serialized, "sourcePartAdapter", adapter);
            SetReference(serialized, "partDatabase", adapter != null ? adapter.PartDatabase : null);
            SetReference(serialized, "previewStudioRoot", studioObject.transform);
            serialized.FindProperty("useCurrentCharacterOnly").boolValue = true;
            serialized.FindProperty("fieldOfView").floatValue = 24f;
            serialized.FindProperty("framingPadding").floatValue = 1.08f;
            var slots = serialized.FindProperty("previewSlots");
            slots.arraySize = 1;
            var slot = slots.GetArrayElementAtIndex(0);
            slot.FindPropertyRelative("modelAnchor").objectReferenceValue = anchor;
            slot.FindPropertyRelative("previewCamera").objectReferenceValue = camera;
            slot.FindPropertyRelative("liveImage").objectReferenceValue = live;
            slot.FindPropertyRelative("fallbackImage").objectReferenceValue = fallback;
            slot.FindPropertyRelative("previewLayer").intValue = 27;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            presenter.RefreshPreviews();
            EditorUtility.SetDirty(presenter);
        }

        private static void BindEditorPartSamples(
            OntologyAppearanceReviewPartSlot[] slots, OntologyCharacterPartDatabase database)
        {
            var definitions = database?.Definitions?
                .Where(definition => definition != null && definition.visibleInCustomization && definition.enabledByDefault)
                .Take(slots.Length).ToArray() ?? Array.Empty<OntologyCharacterPartDefinition>();
            for (var index = 0; index < slots.Length; index++)
                slots[index].Bind(index < definitions.Length ? definitions[index] : null);
        }

        private static TMP_Text AddInfoText(RectTransform parent, string name, string value, Vector2 position,
            Vector2 size, float fontSize, Color color, FontStyles style, TMP_Text fontSource)
        {
            var text = RequireOrCreateText(parent, name, fontSource);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            text.rectTransform.localScale = Vector3.one;
            StyleText(text, fontSize, TextAlignmentOptions.Left, color, style);
            text.text = value;
            return text;
        }

        private static void AddDivider(RectTransform parent, string name, float y)
        {
            var divider = EnsureImage(parent, name);
            divider.rectTransform.anchorMin = divider.rectTransform.anchorMax = divider.rectTransform.pivot = new Vector2(0.5f, 1f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, y);
            divider.rectTransform.sizeDelta = new Vector2(620f, 3f);
            divider.color = new Color(CardLine.r / 255f, CardLine.g / 255f, CardLine.b / 255f, 0.75f);
            divider.raycastTarget = false;
        }

        private static Button StyleButton(RectTransform root, string name, string surfaceName, Vector2 position,
            Vector2 size, bool primary, TMP_Text fontSource, Assets assets)
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
            var label = rect.GetComponentInChildren<TextMeshProUGUI>(true)
                ?? RequireOrCreateText(rect, "Label", fontSource);
            StretchInset(label.rectTransform, 4f);
            StyleText(label, 25f, TextAlignmentOptions.Center, primary ? Cream : Blue, FontStyles.Bold);
            label.text = primary ? "CONTINUE" : "BACK";
            label.transform.SetAsLastSibling();
            return button;
        }

        private static void PrepareRoot(RectTransform root, Assets assets)
        {
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

        private static Assets LoadAssets()
        {
            var result = new Assets
            {
                canvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png"),
                surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png"),
                logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png"),
                circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/circle_antialias_256.asset"),
                preview = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/CharacterSelection/character_preview_triptych_v1.png")
            };
            if (result.logo == null || result.preview == null)
                throw new InvalidOperationException("Appearance-review art assets are not imported.");
            return result;
        }

        private static GameObject FindSceneObject(string name) => Resources.FindObjectsOfTypeAll<Transform>()
            .FirstOrDefault(candidate => candidate != null && candidate.gameObject.scene.IsValid() && candidate.name == name)
            ?.gameObject;

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
            var custom = target.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == "FGUIStarter.CustomButton");
            if (custom != null) Undo.DestroyObjectImmediate(custom);
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

        private static void StretchInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-2f * inset, -2f * inset);
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
            public Sprite circle;
            public Texture2D preview;
        }
    }
}
#endif
