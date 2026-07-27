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
    /// <summary>Builds the approved profile-final-review concept as editable uGUI hierarchy objects.</summary>
    public static class OntologyTovProfileFinalReviewAuthoring
    {
        private const string RootName = "AccountProfileReviewPanel";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(142, 121, 91, 255);
        private static readonly Color32 CardLine = new(215, 189, 148, 255);

        [MenuItem("Tormia/UI/Apply Editable TOV Profile Final Review Design")]
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

            var title = RequireOrCreateText(root, "ProfileFinalReviewTitle", fontSource);
            PlaceTop(title.rectTransform, new Vector2(0f, -42f), new Vector2(820f, 72f));
            StyleText(title, 44f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = "PROFILE FINAL REVIEW";
            var helper = RequireOrCreateText(root, "ProfileFinalReviewHelper", fontSource);
            PlaceTop(helper.rectTransform, new Vector2(0f, -116f), new Vector2(820f, 42f));
            StyleText(helper, 21f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            helper.text = "Everything is ready for your adventure.";

            var identityCard = EnsureImage(root, "CharacterIdentityCard");
            PlaceTop(identityCard.rectTransform, new Vector2(-455f, -190f), new Vector2(600f, 610f));
            StyleImage(identityCard, assets.surface, Cream, false);

            var portrait = EnsureImage(identityCard.rectTransform, "CharacterPortraitMask");
            PlaceTop(portrait.rectTransform, new Vector2(0f, -28f), new Vector2(212f, 212f));
            portrait.sprite = assets.circle != null ? assets.circle : assets.canvas;
            portrait.color = new Color32(240, 220, 186, 255);
            portrait.type = Image.Type.Simple;
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            var mask = portrait.GetComponent<Mask>() ?? Undo.AddComponent<Mask>(portrait.gameObject);
            mask.showMaskGraphic = true;
            var fallbackRect = EnsureRect(portrait.rectTransform, "FallbackCharacterPortrait");
            StretchInset(fallbackRect, 3f);
            var fallback = fallbackRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(fallbackRect.gameObject);
            fallback.texture = assets.preview;
            fallback.uvRect = new Rect(0f, 0f, 1f / 3f, 1f);
            fallback.color = Color.white;
            fallback.raycastTarget = false;
            var liveRect = EnsureRect(portrait.rectTransform, "LiveCharacterPortrait");
            StretchInset(liveRect, 3f);
            var live = liveRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(liveRect.gameObject);
            live.texture = null;
            live.color = Color.white;
            live.raycastTarget = false;
            live.gameObject.SetActive(false);
            fallback.transform.SetSiblingIndex(0);
            live.transform.SetSiblingIndex(1);

            var characterHeading = AddLeftText(identityCard.rectTransform, "CharacterHeading", "CHARACTER",
                48f, -255f, 504f, 32f, 20f, Muted, FontStyles.Bold, fontSource);
            var characterName = AddLeftText(identityCard.rectTransform, "CharacterNameValue", "Haneul",
                48f, -291f, 504f, 52f, 35f, Ink, FontStyles.Bold, fontSource);
            AddDivider(identityCard.rectTransform, "CharacterDivider", -350f, 504f);
            var templateHeading = AddLeftText(identityCard.rectTransform, "TemplateHeading", "TEMPLATE",
                48f, -366f, 504f, 32f, 20f, Muted, FontStyles.Bold, fontSource);
            var templateValue = AddLeftText(identityCard.rectTransform, "TemplateValue", "Default Adventurer",
                48f, -402f, 504f, 48f, 30f, Ink, FontStyles.Bold, fontSource);
            AddDivider(identityCard.rectTransform, "TemplateDivider", -458f, 504f);
            var appearanceHeading = AddLeftText(identityCard.rectTransform, "AppearanceHeading", "APPEARANCE",
                48f, -472f, 504f, 30f, 20f, Muted, FontStyles.Bold, fontSource);

            var appearanceSlots = new OntologyAppearanceReviewPartSlot[4];
            for (var index = 0; index < appearanceSlots.Length; index++)
                appearanceSlots[index] = StyleAppearanceSlot(identityCard.rectTransform, index, fontSource, assets);

            var hiddenAppearance = RequireOrCreateText(root, "AppearanceValue", fontSource);
            hiddenAppearance.gameObject.SetActive(false);

            var summaryCard = EnsureImage(root, "ProfileSummaryCard");
            PlaceTop(summaryCard.rectTransform, new Vector2(365f, -190f), new Vector2(850f, 610f));
            StyleImage(summaryCard, assets.surface, Cream, false);

            CreateBadge(summaryCard.rectTransform, "WorldBadge", "W", new Vector2(-332f, -48f), fontSource, assets);
            var worldHeading = AddLeftText(summaryCard.rectTransform, "SelectedWorldHeading", "SELECTED WORLD",
                170f, -61f, 610f, 34f, 24f, Muted, FontStyles.Bold, fontSource);
            var worldValue = AddLeftText(summaryCard.rectTransform, "SelectedWorldValue", string.Empty,
                170f, -99f, 610f, 58f, 39f, Ink, FontStyles.Bold, fontSource);
            AddDivider(summaryCard.rectTransform, "WorldDivider", -174f, 750f);

            CreateBadge(summaryCard.rectTransform, "PermissionBadge", "KEY", new Vector2(-332f, -202f), fontSource, assets);
            var permissionHeading = AddLeftText(summaryCard.rectTransform, "PermissionHeading", "WORLD PERMISSION",
                170f, -190f, 610f, 34f, 24f, Muted, FontStyles.Bold, fontSource);
            var permissionValue = AddLeftText(summaryCard.rectTransform, "PermissionValue", "OWNER  ·  WORLD EDITING ENABLED",
                170f, -228f, 610f, 54f, 30f, Ink, FontStyles.Bold, fontSource);
            AddDivider(summaryCard.rectTransform, "PermissionDivider", -294f, 750f);

            CreateBadge(summaryCard.rectTransform, "RelationsBadge", "ID", new Vector2(-332f, -332f), fontSource, assets);
            var relationsHeading = AddLeftText(summaryCard.rectTransform, "ProfileRelationsHeading", "PROFILE RELATIONS",
                170f, -320f, 610f, 34f, 24f, Muted, FontStyles.Bold, fontSource);
            var relationsValue = AddLeftText(summaryCard.rectTransform, "ProfileRelationsValue", string.Empty,
                170f, -358f, 610f, 92f, 21f, Ink, FontStyles.Bold, fontSource);
            relationsValue.enableWordWrapping = true;
            AddDivider(summaryCard.rectTransform, "RelationsDivider", -458f, 750f);

            CreateBadge(summaryCard.rectTransform, "StatusBadge", "OK", new Vector2(-332f, -493f), fontSource, assets);
            var statusHeading = AddLeftText(summaryCard.rectTransform, "StatusHeading", "STATUS",
                170f, -483f, 610f, 34f, 24f, Muted, FontStyles.Bold, fontSource);
            var statusValue = AddLeftText(summaryCard.rectTransform, "StatusValue", "READY TO ENTER",
                170f, -525f, 610f, 52f, 34f, Ink, FontStyles.Bold, fontSource);

            var oldSummaryRect = root.Find("ProfileOverflowSummary") as RectTransform
                ?? root.Find("Text (TMP)") as RectTransform;
            if (oldSummaryRect == null) oldSummaryRect = EnsureRect(root, "ProfileOverflowSummary");
            oldSummaryRect.name = "ProfileOverflowSummary";
            var summary = oldSummaryRect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(oldSummaryRect.gameObject);
            summary.gameObject.SetActive(false);

            var back = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(-660f, -900f),
                new Vector2(270f, 72f), false, fontSource, assets);
            var enter = StyleButton(root, "EnterWorldButton", "EnterWorldButtonSurface", new Vector2(625f, -900f),
                new Vector2(370f, 72f), true, fontSource, assets);

            RemoveRootPresentation(rootObject);
            if (rootObject.GetComponent<OntologyUiEditorPreview>() == null)
                Undo.AddComponent<OntologyUiEditorPreview>(rootObject);
            var panel = rootObject.GetComponent<OntologyAccountProfileReviewPanel>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<OntologyCharacterPartAdapter>();
            var serializedPanel = new SerializedObject(panel);
            SetReference(serializedPanel, "panelGroup", rootObject.GetComponent<CanvasGroup>());
            SetReference(serializedPanel, "titleLabel", title);
            SetReference(serializedPanel, "helperLabel", helper);
            SetReference(serializedPanel, "characterHeadingLabel", characterHeading);
            SetReference(serializedPanel, "characterNameLabel", characterName);
            SetReference(serializedPanel, "templateHeadingLabel", templateHeading);
            SetReference(serializedPanel, "templateLabel", templateValue);
            SetReference(serializedPanel, "appearanceHeadingLabel", appearanceHeading);
            SetReference(serializedPanel, "appearanceLabel", hiddenAppearance);
            SetReference(serializedPanel, "partDatabase", adapter != null ? adapter.PartDatabase : null);
            SetReference(serializedPanel, "selectedWorldHeadingLabel", worldHeading);
            SetReference(serializedPanel, "selectedWorldLabel", worldValue);
            SetReference(serializedPanel, "permissionHeadingLabel", permissionHeading);
            SetReference(serializedPanel, "permissionLabel", permissionValue);
            SetReference(serializedPanel, "profileRelationsHeadingLabel", relationsHeading);
            SetReference(serializedPanel, "profileRelationsLabel", relationsValue);
            SetReference(serializedPanel, "statusHeadingLabel", statusHeading);
            SetReference(serializedPanel, "statusLabel", statusValue);
            SetReference(serializedPanel, "summaryLabel", summary);
            SetReference(serializedPanel, "enterWorldButton", enter);
            SetReference(serializedPanel, "backButton", back);
            var slotsProperty = serializedPanel.FindProperty("appearanceSlots");
            slotsProperty.arraySize = appearanceSlots.Length;
            for (var index = 0; index < appearanceSlots.Length; index++)
                slotsProperty.GetArrayElementAtIndex(index).objectReferenceValue = appearanceSlots[index];
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            BindEditorPartSamples(appearanceSlots, adapter != null ? adapter.PartDatabase : null);
            ConfigurePortraitPreview(rootObject, live, fallback, adapter);

            var group = rootObject.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            OntologyUiEditorPreviewSelector.ShowProfileFinalReview();
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV profile final review UI to " + rootObject.scene.path + ".");
        }

        private static OntologyAppearanceReviewPartSlot StyleAppearanceSlot(
            RectTransform parent, int index, TMP_Text fontSource, Assets assets)
        {
            var root = EnsureRect(parent, "AppearancePartSlot" + (index + 1));
            PlaceTop(root, new Vector2(-207f + index * 138f, -506f), new Vector2(120f, 100f));
            var circle = EnsureImage(root, "PartCircle");
            PlaceTop(circle.rectTransform, Vector2.zero, new Vector2(82f, 82f));
            circle.sprite = assets.circle != null ? assets.circle : assets.canvas;
            circle.type = Image.Type.Simple;
            circle.color = new Color32(241, 225, 197, 255);
            circle.preserveAspect = true;
            circle.raycastTarget = false;
            var mask = circle.GetComponent<Mask>() ?? Undo.AddComponent<Mask>(circle.gameObject);
            mask.enabled = true;
            mask.showMaskGraphic = true;
            var icon = EnsureImage(circle.rectTransform, "PartIcon");
            StretchInset(icon.rectTransform, -10f);
            icon.preserveAspect = true;
            icon.maskable = true;
            icon.raycastTarget = false;
            var label = RequireOrCreateText(root, "PartLabel", fontSource);
            PlaceTop(label.rectTransform, new Vector2(0f, -78f), new Vector2(118f, 24f));
            StyleText(label, 15f, TextAlignmentOptions.Center, Ink, FontStyles.Normal);
            label.text = new[] { "Hair", "Top", "Bottom", "Shoes" }[index];
            var slot = root.GetComponent<OntologyAppearanceReviewPartSlot>()
                ?? Undo.AddComponent<OntologyAppearanceReviewPartSlot>(root.gameObject);
            var serialized = new SerializedObject(slot);
            SetReference(serialized, "icon", icon);
            SetReference(serialized, "label", label);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        private static void ConfigurePortraitPreview(
            GameObject panelObject, RawImage live, RawImage fallback, OntologyCharacterPartAdapter adapter)
        {
            var studioObject = FindSceneObject("ProfileFinalReviewPreviewStudio");
            if (studioObject == null)
            {
                studioObject = new GameObject("ProfileFinalReviewPreviewStudio");
                Undo.RegisterCreatedObjectUndo(studioObject, "Create profile final review preview studio");
            }
            studioObject.transform.position = new Vector3(0f, -2080f, 0f);
            studioObject.transform.rotation = Quaternion.identity;
            studioObject.transform.localScale = Vector3.one;
            var slotRoot = EnsureTransform(studioObject.transform, "SelectedCharacterPortraitSlot");
            slotRoot.localPosition = Vector3.zero;
            slotRoot.localRotation = Quaternion.identity;
            slotRoot.localScale = Vector3.one;
            var anchor = EnsureTransform(slotRoot, "ModelAnchor");
            var cameraTransform = EnsureTransform(slotRoot, "PreviewCamera");
            cameraTransform.localPosition = new Vector3(0f, 1.5f, 3f);
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
            serialized.FindProperty("framingExtentScale").vector2Value = new Vector2(0.31f, 0.31f);
            serialized.FindProperty("framingFocusY").floatValue = 0.79f;
            var slots = serialized.FindProperty("previewSlots");
            slots.arraySize = 1;
            var slot = slots.GetArrayElementAtIndex(0);
            slot.FindPropertyRelative("modelAnchor").objectReferenceValue = anchor;
            slot.FindPropertyRelative("previewCamera").objectReferenceValue = camera;
            slot.FindPropertyRelative("liveImage").objectReferenceValue = live;
            slot.FindPropertyRelative("fallbackImage").objectReferenceValue = fallback;
            slot.FindPropertyRelative("previewLayer").intValue = 26;
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

        private static void CreateBadge(RectTransform parent, string name, string glyph, Vector2 position,
            TMP_Text fontSource, Assets assets)
        {
            var badge = EnsureImage(parent, name);
            PlaceTop(badge.rectTransform, position, new Vector2(116f, 116f));
            badge.sprite = assets.circle != null ? assets.circle : assets.canvas;
            badge.type = Image.Type.Simple;
            badge.color = new Color32(245, 230, 201, 255);
            badge.preserveAspect = true;
            badge.raycastTarget = false;
            var text = RequireOrCreateText(badge.rectTransform, "Glyph", fontSource);
            StretchInset(text.rectTransform, 12f);
            StyleText(text, glyph.Length > 1 ? 27f : 51f, TextAlignmentOptions.Center, new Color32(194, 133, 16, 255), FontStyles.Bold);
            text.text = glyph;
        }

        private static TMP_Text AddLeftText(RectTransform parent, string name, string value, float x, float y,
            float width, float height, float fontSize, Color color, FontStyles style, TMP_Text fontSource)
        {
            var text = RequireOrCreateText(parent, name, fontSource);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = new Vector2(x, y);
            text.rectTransform.sizeDelta = new Vector2(width, height);
            text.rectTransform.localScale = Vector3.one;
            StyleText(text, fontSize, TextAlignmentOptions.Left, color, style);
            text.text = value;
            return text;
        }

        private static void AddDivider(RectTransform parent, string name, float y, float width)
        {
            var divider = EnsureImage(parent, name);
            divider.rectTransform.anchorMin = divider.rectTransform.anchorMax = divider.rectTransform.pivot = new Vector2(0.5f, 1f);
            divider.rectTransform.anchoredPosition = new Vector2(0f, y);
            divider.rectTransform.sizeDelta = new Vector2(width, 3f);
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
            label.text = primary ? "ENTER WORLD" : "BACK";
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
                throw new InvalidOperationException("Profile-final-review art assets are not imported.");
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
            if (fontSource != null)
            {
                text.font = fontSource.font;
                text.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
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
