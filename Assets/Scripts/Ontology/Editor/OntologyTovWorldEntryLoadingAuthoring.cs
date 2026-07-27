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
    /// <summary>Builds the approved world-entry loading concept as editable uGUI hierarchy objects.</summary>
    public static class OntologyTovWorldEntryLoadingAuthoring
    {
        private const string RootName = "WorldEntryLoadingPanel";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(142, 121, 91, 255);

        [MenuItem("Tormia/UI/Apply Editable TOV World Entry Loading Design")]
        public static void Apply()
        {
            var canvas = FindSceneObject("OntologyGameCanvas")?.GetComponent<RectTransform>();
            if (canvas == null) throw new InvalidOperationException("OntologyGameCanvas was not found.");
            var root = EnsureLoadingRoot(canvas);
            var rootObject = root.gameObject;
            var assets = LoadAssets();
            var fontSource = canvas.GetComponentInChildren<TMP_Text>(true);

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(1740f, 1020f);
            root.localScale = Vector3.one;
            root.SetAsLastSibling();
            PrepareRoot(root, assets);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTopLeft(logo.rectTransform, new Vector2(42f, -27f), new Vector2(250f, 145f));
            logo.sprite = assets.logo;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;

            var title = RequireOrCreateText(root, "EnteringWorldTitle", fontSource);
            PlaceTop(title.rectTransform, new Vector2(0f, -42f), new Vector2(760f, 72f));
            StyleText(title, 48f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = "ENTERING WORLD";
            CreateTitleDecoration(root, assets);

            var previewFrame = EnsureImage(root, "WorldPreviewFrame");
            PlaceTop(previewFrame.rectTransform, new Vector2(0f, -152f), new Vector2(1600f, 535f));
            StyleImage(previewFrame, assets.canvas, Gold, false);
            var previewSurface = EnsureImage(previewFrame.rectTransform, "WorldPreviewSurface");
            StretchInset(previewSurface.rectTransform, 4f);
            StyleImage(previewSurface, assets.canvas, Cream, false);
            var worldImageRect = EnsureRect(previewSurface.rectTransform, "WorldPreview");
            StretchInset(worldImageRect, 5f);
            var worldImage = worldImageRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(worldImageRect.gameObject);
            worldImage.texture = assets.worldPreview;
            worldImage.uvRect = new Rect(0f, 0f, 1f / 3f, 1f);
            worldImage.color = Color.white;
            worldImage.raycastTarget = false;
            var previewMask = previewSurface.GetComponent<RectMask2D>() ?? Undo.AddComponent<RectMask2D>(previewSurface.gameObject);
            previewMask.padding = Vector4.zero;

            var loadingSurface = EnsureImage(root, "LoadingInformationSurface");
            PlaceTop(loadingSurface.rectTransform, new Vector2(0f, -624f), new Vector2(1600f, 290f));
            StyleImage(loadingSurface, assets.surface, new Color32(255, 248, 235, 249), false);

            var portrait = EnsureImage(loadingSurface.rectTransform, "CharacterPortraitMask");
            PlaceTop(portrait.rectTransform, new Vector2(-670f, -24f), new Vector2(170f, 170f));
            portrait.sprite = assets.circle != null ? assets.circle : assets.canvas;
            portrait.type = Image.Type.Simple;
            portrait.color = new Color32(240, 220, 186, 255);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            var portraitMask = portrait.GetComponent<Mask>() ?? Undo.AddComponent<Mask>(portrait.gameObject);
            portraitMask.showMaskGraphic = true;
            var fallbackRect = EnsureRect(portrait.rectTransform, "FallbackCharacterPortrait");
            StretchInset(fallbackRect, 3f);
            var fallback = fallbackRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(fallbackRect.gameObject);
            fallback.texture = assets.characterPreview;
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

            var worldName = AddLeftText(loadingSurface.rectTransform, "WorldNameValue", string.Empty,
                245f, -48f, 430f, 64f, 43f, Ink, FontStyles.Bold, fontSource);
            var characterName = AddLeftText(loadingSurface.rectTransform, "CharacterNameValue", "Haneul",
                245f, -112f, 430f, 32f, 18f, Muted, FontStyles.Bold, fontSource);
            var preparing = AddLeftText(loadingSurface.rectTransform, "PreparationLabel", "Preparing your adventure...",
                245f, -146f, 430f, 40f, 25f, Muted, FontStyles.Normal, fontSource);

            var stage = RequireOrCreateText(loadingSurface.rectTransform, "LoadingStageLabel", fontSource);
            PlaceTop(stage.rectTransform, new Vector2(270f, -36f), new Vector2(620f, 38f));
            StyleText(stage, 23f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            stage.text = "Loading world data";

            var progressTrack = EnsureImage(loadingSurface.rectTransform, "ProgressTrack");
            PlaceTop(progressTrack.rectTransform, new Vector2(290f, -89f), new Vector2(790f, 62f));
            StyleImage(progressTrack, assets.canvas, Gold, false);
            var trackInner = EnsureImage(progressTrack.rectTransform, "ProgressTrackInner");
            StretchInset(trackInner.rectTransform, 4f);
            StyleImage(trackInner, assets.canvas, new Color32(234, 220, 195, 255), false);

            // Reveal a full-width sliced bar through a changing mask. The fill
            // sprite never scales with progress, so its rounded cap and border
            // remain intact at every percentage.
            var fillBounds = EnsureRect(trackInner.rectTransform, "ProgressFillBounds");
            StretchInset(fillBounds, 0f);
            var reveal = EnsureRect(fillBounds, "ProgressReveal");
            reveal.anchorMin = new Vector2(0f, 0f);
            reveal.anchorMax = new Vector2(0f, 1f);
            reveal.pivot = new Vector2(0f, 0.5f);
            reveal.anchoredPosition = Vector2.zero;
            var fullFillWidth = Mathf.Max(1f, trackInner.rectTransform.rect.width);
            reveal.sizeDelta = new Vector2(fullFillWidth * 0.72f, 0f);
            if (reveal.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(reveal.gameObject);

            var legacyFill = trackInner.rectTransform.Find("ProgressFill") as RectTransform;
            if (legacyFill != null && legacyFill.parent != reveal) legacyFill.SetParent(reveal, false);
            var progressFill = EnsureImage(reveal, "ProgressFill");
            progressFill.rectTransform.anchorMin = new Vector2(0f, 0f);
            progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            progressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            progressFill.rectTransform.anchoredPosition = Vector2.zero;
            progressFill.rectTransform.sizeDelta = new Vector2(fullFillWidth, 0f);
            progressFill.sprite = assets.canvas;
            progressFill.type = Image.Type.Sliced;
            progressFill.fillAmount = 1f;
            progressFill.color = Blue;
            progressFill.raycastTarget = false;
            var marker = EnsureImage(trackInner.rectTransform, "ProgressMarker");
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(0.72f, 0.5f);
            marker.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            marker.rectTransform.anchoredPosition = Vector2.zero;
            marker.rectTransform.sizeDelta = new Vector2(62f, 62f);
            marker.sprite = assets.circle != null ? assets.circle : assets.canvas;
            marker.type = Image.Type.Simple;
            marker.color = Gold;
            marker.preserveAspect = true;
            marker.raycastTarget = false;
            marker.transform.SetAsLastSibling();
            var markerGlyph = RequireOrCreateText(marker.rectTransform, "MarkerGlyph", fontSource);
            StretchInset(markerGlyph.rectTransform, 9f);
            StyleText(markerGlyph, 32f, TextAlignmentOptions.Center, Cream, FontStyles.Bold);
            markerGlyph.text = "*";

            var progress = RequireOrCreateText(loadingSurface.rectTransform, "ProgressValue", fontSource);
            PlaceTop(progress.rectTransform, new Vector2(740f, -84f), new Vector2(120f, 66f));
            StyleText(progress, 38f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            progress.text = "72%";

            var tipCard = EnsureImage(root, "LoadingTipCard");
            PlaceTop(tipCard.rectTransform, new Vector2(145f, -861f), new Vector2(1120f, 94f));
            StyleImage(tipCard, assets.surface, new Color32(255, 248, 235, 255), false);
            var tipBadge = EnsureImage(tipCard.rectTransform, "TipBadge");
            PlaceTop(tipBadge.rectTransform, new Vector2(-505f, -12f), new Vector2(68f, 68f));
            tipBadge.sprite = assets.circle != null ? assets.circle : assets.canvas;
            tipBadge.type = Image.Type.Simple;
            tipBadge.color = new Color32(244, 224, 185, 255);
            tipBadge.preserveAspect = true;
            tipBadge.raycastTarget = false;
            var tipIcon = RequireOrCreateText(tipBadge.rectTransform, "TipIcon", fontSource);
            StretchInset(tipIcon.rectTransform, 8f);
            StyleText(tipIcon, 35f, TextAlignmentOptions.Center, new Color32(194, 133, 16, 255), FontStyles.Bold);
            tipIcon.text = "i";
            var tipHeading = AddLeftText(tipCard.rectTransform, "TipHeading", "TIP",
                90f, -24f, 100f, 46f, 28f, Ink, FontStyles.Bold, fontSource);
            var tipDivider = EnsureImage(tipCard.rectTransform, "TipDivider");
            PlaceTop(tipDivider.rectTransform, new Vector2(-330f, -15f), new Vector2(3f, 64f));
            tipDivider.color = new Color32(215, 189, 148, 255);
            tipDivider.raycastTarget = false;
            var tip = AddLeftText(tipCard.rectTransform, "TipValue",
                "Your character profile travels with you. World facts belong to this world.",
                220f, -25f, 800f, 50f, 21f, Muted, FontStyles.Bold, fontSource);

            var panel = rootObject.GetComponent<OntologyWorldEntryLoadingPanel>();
            var group = rootObject.GetComponent<CanvasGroup>();
            var entryFlow = UnityEngine.Object.FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            var navigator = UnityEngine.Object.FindAnyObjectByType<OntologyAccountFlowNavigator>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<OntologyCharacterPartAdapter>();
            var serializedPanel = new SerializedObject(panel);
            SetReference(serializedPanel, "entryFlow", entryFlow);
            SetReference(serializedPanel, "navigator", navigator);
            SetReference(serializedPanel, "panelGroup", group);
            SetReference(serializedPanel, "titleLabel", title);
            SetReference(serializedPanel, "worldNameLabel", worldName);
            SetReference(serializedPanel, "characterNameLabel", characterName);
            SetReference(serializedPanel, "preparationLabel", preparing);
            SetReference(serializedPanel, "stageLabel", stage);
            SetReference(serializedPanel, "progressLabel", progress);
            SetReference(serializedPanel, "tipHeadingLabel", tipHeading);
            SetReference(serializedPanel, "tipLabel", tip);
            SetReference(serializedPanel, "progressFill", progressFill);
            SetReference(serializedPanel, "progressFillBounds", fillBounds);
            SetReference(serializedPanel, "progressReveal", reveal);
            SetReference(serializedPanel, "progressMarker", marker.rectTransform);
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            if (navigator != null)
            {
                var serializedNavigator = new SerializedObject(navigator);
                SetReference(serializedNavigator, "worldEntryLoadingPanel", panel);
                serializedNavigator.ApplyModifiedPropertiesWithoutUndo();
            }

            ConfigurePortraitPreview(rootObject, live, fallback, adapter, entryFlow);

            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            OntologyUiEditorPreviewSelector.ShowWorldEntryLoading();
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV world entry loading UI to " + rootObject.scene.path + ".");
        }

        private static RectTransform EnsureLoadingRoot(RectTransform canvas)
        {
            var existing = canvas.Find(RootName) as RectTransform;
            if (existing != null)
            {
                if (existing.GetComponent<CanvasGroup>() == null) Undo.AddComponent<CanvasGroup>(existing.gameObject);
                if (existing.GetComponent<OntologyWorldEntryLoadingPanel>() == null)
                    Undo.AddComponent<OntologyWorldEntryLoadingPanel>(existing.gameObject);
                if (existing.GetComponent<OntologyUiEditorPreview>() == null)
                    Undo.AddComponent<OntologyUiEditorPreview>(existing.gameObject);
                return existing;
            }

            var rootObject = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup),
                typeof(OntologyWorldEntryLoadingPanel), typeof(OntologyUiEditorPreview));
            Undo.RegisterCreatedObjectUndo(rootObject, "Create world entry loading panel");
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(canvas, false);
            return root;
        }

        private static void ConfigurePortraitPreview(GameObject panelObject, RawImage live, RawImage fallback,
            OntologyCharacterPartAdapter adapter, OntologyWorldAuthorityAccountEntryFlow entryFlow)
        {
            var studioObject = FindSceneObject("WorldEntryLoadingPreviewStudio");
            if (studioObject == null)
            {
                studioObject = new GameObject("WorldEntryLoadingPreviewStudio");
                Undo.RegisterCreatedObjectUndo(studioObject, "Create world entry loading preview studio");
            }
            studioObject.transform.position = new Vector3(0f, -2120f, 0f);
            studioObject.transform.rotation = Quaternion.identity;
            studioObject.transform.localScale = Vector3.one;
            var slotRoot = EnsureTransform(studioObject.transform, "SelectedCharacterPortraitSlot");
            var anchor = EnsureTransform(slotRoot, "ModelAnchor");
            var cameraTransform = EnsureTransform(slotRoot, "PreviewCamera");
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

        private static void CreateTitleDecoration(RectTransform root, Assets assets)
        {
            var left = EnsureImage(root, "TitleLineLeft");
            PlaceTop(left.rectTransform, new Vector2(-285f, -118f), new Vector2(220f, 3f));
            left.color = Gold;
            left.raycastTarget = false;
            var right = EnsureImage(root, "TitleLineRight");
            PlaceTop(right.rectTransform, new Vector2(285f, -118f), new Vector2(220f, 3f));
            right.color = Gold;
            right.raycastTarget = false;
            var diamond = EnsureImage(root, "TitleDiamond");
            PlaceTop(diamond.rectTransform, new Vector2(0f, -106f), new Vector2(24f, 24f));
            diamond.sprite = assets.canvas;
            diamond.color = Gold;
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            diamond.raycastTarget = false;
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

        private static Assets LoadAssets()
        {
            var result = new Assets
            {
                canvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png"),
                surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png"),
                logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png"),
                circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/circle_antialias_256.asset"),
                worldPreview = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/WorldOnboarding/world_preview_triptych_v1.png"),
                characterPreview = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/CharacterSelection/character_preview_triptych_v1.png")
            };
            if (result.logo == null || result.worldPreview == null || result.characterPreview == null)
                throw new InvalidOperationException("World-entry loading art assets are not imported.");
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

        private static void PlaceTop(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void StretchInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-2f * inset, -2f * inset);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
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
            public Texture2D worldPreview;
            public Texture2D characterPreview;
        }
    }
}
#endif
