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
    /// Builds the approved TOV world-selection and world-creation layouts as
    /// ordinary hierarchy-authored uGUI objects. Runtime scripts only bind
    /// data and callbacks; every visual element remains editable in Inspector.
    /// </summary>
    public static class OntologyTovWorldPanelsAuthoring
    {
        private const string TexturePath = "Assets/Art/UI/WorldOnboarding/world_preview_triptych_v1.png";
        private const string EmptyWorldHeroPath =
            "Assets/Art/UI/WorldOnboarding/empty_world_hero_v1.png";
        private static readonly Color32 Blue = new(8, 55, 125, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 Ink = new(22, 53, 72, 255);
        private static readonly Color32 Muted = new(80, 101, 112, 210);

        [MenuItem("Tormia/UI/Apply Editable TOV World Panels")]
        public static void ApplyAll()
        {
            ApplyWorldCreation(false);
            ApplyWorldSelection(false);
            OntologyUiEditorPreviewSelector.ShowWorldSelection();
            SaveActiveScene();
            Debug.Log("Applied editable TOV world creation and selection UI.");
        }

        [MenuItem("Tormia/UI/Apply Editable TOV World Creation Design")]
        public static void ApplyWorldCreationMenu() => ApplyWorldCreation(true);

        [MenuItem("Tormia/UI/Apply Editable TOV World Selection Design")]
        public static void ApplyWorldSelectionMenu() => ApplyWorldSelection(true);

        private static void ApplyWorldCreation(bool save)
        {
            var rootObject = FindSceneObject("AccountWorldCreationPanel");
            if (rootObject == null) throw new InvalidOperationException("AccountWorldCreationPanel was not found.");
            var root = rootObject.GetComponent<RectTransform>();
            var assets = LoadAssets();
            PrepareRoot(root, new Vector2(860f, 1040f), assets);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTop(logo.rectTransform, new Vector2(0f, -12f), new Vector2(440f, 220f));
            logo.sprite = assets.logo;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;

            var heading = RequireOrCreateText(root, "Text (TMP)", null);
            PlaceTop(heading.rectTransform, new Vector2(0f, -210f), new Vector2(720f, 112f));
            StyleText(heading, 38f, TextAlignmentOptions.Center, Ink, FontStyles.Normal);
            heading.richText = true;
            heading.text = "<size=38>CREATE WORLD</size>\n<size=19>Give your new world a name.</size>";

            var previewFrame = EnsureRect(root, "WorldPreviewFrame");
            PlaceTop(previewFrame, new Vector2(0f, -330f), new Vector2(680f, 310f));
            EnsureMaskedPreview(previewFrame, assets, new Rect(0f, 0f, 1f / 3f, 1f));

            var nameLabel = RequireOrCreateText(root, "WorldNameLabel", heading);
            PlaceTop(nameLabel.rectTransform, new Vector2(0f, -665f), new Vector2(650f, 34f));
            StyleText(nameLabel, 21f, TextAlignmentOptions.Left, Ink, FontStyles.Bold);
            nameLabel.text = "WORLD NAME";

            var inputRect = root.Find("WorldNameInput") as RectTransform ?? root.Find("CharacterNameInput") as RectTransform;
            if (inputRect == null) throw new InvalidOperationException("The existing world-name input was not found.");
            inputRect.name = "WorldNameInput";
            PlaceTop(inputRect, new Vector2(0f, -705f), new Vector2(650f, 68f));
            var input = StyleInput(inputRect, heading, assets, "Name your new world");

            var create = StyleButton(root, "ContinueToWorldButton", "CreateButtonSurface", new Vector2(0f, -810f), new Vector2(650f, 76f), true, assets, heading);
            var back = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(0f, -905f), new Vector2(650f, 68f), false, assets, heading);

            RemoveRootPresentation(rootObject);
            EnsurePreview(rootObject);
            var panel = rootObject.GetComponent<OntologyAccountWorldCreationPanel>();
            var serialized = new SerializedObject(panel);
            SetReference(serialized, "panelGroup", rootObject.GetComponent<CanvasGroup>());
            SetReference(serialized, "worldNameInput", input);
            SetReference(serialized, "worldNameLabel", nameLabel);
            SetReference(serialized, "createButton", create);
            SetReference(serialized, "backButton", back);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(rootObject);
            if (save) { OntologyUiEditorPreviewSelector.ShowWorldCreation(); SaveActiveScene(); }
        }

        private static void ApplyWorldSelection(bool save)
        {
            var rootObject = FindSceneObject("AccountWorldSelectionPanel");
            if (rootObject == null) throw new InvalidOperationException("AccountWorldSelectionPanel was not found.");
            var root = rootObject.GetComponent<RectTransform>();
            var assets = LoadAssets();
            PrepareRoot(root, new Vector2(1740f, 1020f), assets);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTopLeft(logo.rectTransform, new Vector2(42f, -30f), new Vector2(250f, 145f));
            logo.sprite = assets.logo;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;

            var title = RequireOrCreateText(root, "Text (TMP)", null);
            PlaceTop(title.rectTransform, new Vector2(0f, -45f), new Vector2(700f, 72f));
            StyleText(title, 46f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = "SELECT A WORLD";

            var helper = RequireOrCreateText(root, "WorldSelectionHelper", title);
            PlaceTop(helper.rectTransform, new Vector2(0f, -120f), new Vector2(700f, 42f));
            StyleText(helper, 22f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            helper.text = "Choose a world to enter.";

            var obsoleteCharacterChip = root.Find("SelectedCharacterChip");
            if (obsoleteCharacterChip != null)
                Undo.DestroyObjectImmediate(obsoleteCharacterChip.gameObject);

            var emptyWorldHero = EnsureImage(root, "EmptyWorldHero");
            PlaceTop(emptyWorldHero.rectTransform, new Vector2(0f, -150f), new Vector2(1040f, 540f));
            emptyWorldHero.sprite = assets.emptyWorldHero;
            emptyWorldHero.type = Image.Type.Simple;
            emptyWorldHero.preserveAspect = true;
            emptyWorldHero.color = Color.white;
            emptyWorldHero.raycastTarget = false;
            emptyWorldHero.gameObject.SetActive(true);

            var cards = new OntologyAccountWorldCard[3];
            for (var i = 0; i < cards.Length; i++)
            {
                var cardRect = Require<RectTransform>(root, "WorldCardSlot" + (i + 1));
                PlaceTop(cardRect, new Vector2(-540f + i * 540f, -205f), new Vector2(490f, 510f));
                cards[i] = StyleWorldCard(cardRect, i, assets, title);
            }

            var createWorld = StyleButton(root, "CreateWorldButton", "CreateWorldButtonSurface", new Vector2(0f, -750f), new Vector2(330f, 66f), false, assets, title);
            createWorld.GetComponentInChildren<TMP_Text>(true).text = "CREATE WORLD";
            var back = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(-660f, -900f), new Vector2(270f, 72f), false, assets, title);
            var next = StyleButton(root, "ContinueButton", "ContinueButtonSurface", new Vector2(660f, -900f), new Vector2(270f, 72f), true, assets, title);
            next.interactable = false;

            var status = RequireOrCreateText(root, "WorldSelectionStatus", title);
            PlaceTop(status.rectTransform, new Vector2(0f, -875f), new Vector2(820f, 48f));
            StyleText(status, 18f, TextAlignmentOptions.Center, Muted, FontStyles.Normal);
            status.text = string.Empty;

            RemoveRootPresentation(rootObject);
            EnsurePreview(rootObject);
            var panel = rootObject.GetComponent<OntologyAccountWorldSelectionPanel>();
            var serialized = new SerializedObject(panel);
            SetReference(serialized, "panelGroup", rootObject.GetComponent<CanvasGroup>());
            SetReference(serialized, "titleLabel", title);
            SetReference(serialized, "helperLabel", helper);
            SetReference(serialized, "emptyWorldHero", emptyWorldHero.gameObject);
            SetReference(serialized, "statusLabel", status);
            SetReference(serialized, "continueButton", next);
            SetReference(serialized, "backButton", back);
            var cardProperty = serialized.FindProperty("worldCards");
            cardProperty.arraySize = cards.Length;
            for (var i = 0; i < cards.Length; i++) cardProperty.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(rootObject);
            if (save) { OntologyUiEditorPreviewSelector.ShowWorldSelection(); SaveActiveScene(); }
        }

        private static OntologyAccountWorldCard StyleWorldCard(
            RectTransform root,
            int index,
            Assets assets,
            TMP_Text fontSource)
        {
            RemoveCustomButton(root.gameObject);
            var button = root.GetComponent<Button>() ?? Undo.AddComponent<Button>(root.gameObject);
            var outline = root.GetComponent<Image>() ?? Undo.AddComponent<Image>(root.gameObject);
            StyleImage(outline, assets.canvas, Gold, true);
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;

            var surface = EnsureImage(root, "CardSurface");
            StretchInset(surface.rectTransform, 5f);
            StyleImage(surface, assets.surface, Cream, false);
            surface.transform.SetSiblingIndex(0);

            var selected = EnsureImage(root, "SelectedIndicator");
            StretchInset(selected.rectTransform, 0f);
            StyleImage(selected, assets.canvas, Blue, false);
            selected.transform.SetSiblingIndex(1);
            var selectedInner = EnsureImage(selected.rectTransform, "SelectedInnerSurface");
            StretchInset(selectedInner.rectTransform, 7f);
            StyleImage(selectedInner, assets.surface, Cream, false);
            var badge = EnsureImage(selected.rectTransform, "SelectedBadge");
            PlaceBottom(badge.rectTransform, new Vector2(0f, -18f), new Vector2(240f, 48f));
            StyleImage(badge, assets.canvas, Blue, false);
            var selectedBadge = RequireOrCreateText(badge.rectTransform, "SelectedLabel", fontSource);
            StretchInset(selectedBadge.rectTransform, 4f);
            StyleText(selectedBadge, 20f, TextAlignmentOptions.Center, Cream, FontStyles.Bold);
            selectedBadge.text = "SELECTED";
            badge.transform.SetAsLastSibling();
            selected.gameObject.SetActive(false);

            var previewFrame = EnsureRect(root, "WorldThumbnailFrame");
            PlaceTop(previewFrame, new Vector2(0f, -16f), new Vector2(448f, 252f));
            EnsureMaskedPreview(previewFrame, assets, new Rect(index / 3f, 0f, 1f / 3f, 1f));

            var title = RequireOrCreateText(root, "WorldTitle", fontSource);
            PlaceTop(title.rectTransform, new Vector2(0f, -288f), new Vector2(420f, 48f));
            StyleText(title, 30f, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            title.text = string.Empty;

            var roleChip = EnsureImage(root, "RoleChip");
            PlaceTop(roleChip.rectTransform, new Vector2(0f, -352f), new Vector2(180f, 45f));
            StyleImage(roleChip, assets.canvas, index == 0 ? new Color32(65, 122, 225, 255) : index == 1 ? new Color32(105, 155, 75, 255) : new Color32(142, 86, 170, 255), false);
            var role = RequireOrCreateText(roleChip.rectTransform, "RoleLabel", fontSource);
            StretchInset(role.rectTransform, 4f);
            StyleText(role, 20f, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            role.text = string.Empty;

            var revision = RequireOrCreateText(root, "RevisionLabel", fontSource);
            PlaceTop(revision.rectTransform, new Vector2(0f, -420f), new Vector2(380f, 40f));
            StyleText(revision, 20f, TextAlignmentOptions.Center, Ink, FontStyles.Normal);
            revision.text = string.Empty;

            title.transform.SetAsLastSibling();
            roleChip.transform.SetAsLastSibling();
            revision.transform.SetAsLastSibling();
            previewFrame.transform.SetAsLastSibling();
            badge.transform.SetAsLastSibling();

            var card = root.GetComponent<OntologyAccountWorldCard>() ?? Undo.AddComponent<OntologyAccountWorldCard>(root.gameObject);
            var serialized = new SerializedObject(card);
            SetReference(serialized, "selectButton", button);
            SetReference(serialized, "selectedIndicator", selected.gameObject);
            SetReference(serialized, "titleLabel", title);
            SetReference(serialized, "roleLabel", role);
            SetReference(serialized, "revisionLabel", revision);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);
            return card;
        }

        private static void PrepareRoot(RectTransform root, Vector2 size, Assets assets)
        {
            Undo.RecordObject(root, "Apply editable TOV world UI");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = size;
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

        private static TMP_InputField StyleInput(RectTransform rect, TMP_Text fontSource, Assets assets, string placeholderValue)
        {
            var input = rect.GetComponent<TMP_InputField>() ?? Undo.AddComponent<TMP_InputField>(rect.gameObject);
            var background = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            StyleImage(background, assets.input != null ? assets.input : assets.canvas, Color.white, true);
            input.targetGraphic = background;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            var text = input.textComponent as TextMeshProUGUI ?? rect.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            if (text == null) text = RequireOrCreateText(rect, "Text", fontSource);
            StretchInset(text.rectTransform, 5f);
            text.rectTransform.sizeDelta = new Vector2(-48f, -10f);
            StyleText(text, 23f, TextAlignmentOptions.Left, Ink, FontStyles.Normal);
            input.textComponent = text;
            var placeholder = rect.Find("Placeholder")?.GetComponent<TextMeshProUGUI>() ?? RequireOrCreateText(rect, "Placeholder", fontSource);
            StretchInset(placeholder.rectTransform, 5f);
            placeholder.rectTransform.sizeDelta = new Vector2(-48f, -10f);
            StyleText(placeholder, 23f, TextAlignmentOptions.Left, new Color32(80, 101, 112, 125), FontStyles.Normal);
            placeholder.text = placeholderValue;
            input.placeholder = placeholder;
            placeholder.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
            return input;
        }

        private static Button StyleButton(RectTransform root, string name, string surfaceName, Vector2 position, Vector2 size, bool primary, Assets assets, TMP_Text fontSource)
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

        private static void EnsureMaskedPreview(RectTransform frame, Assets assets, Rect uv)
        {
            var frameImage = frame.GetComponent<Image>() ?? Undo.AddComponent<Image>(frame.gameObject);
            StyleImage(frameImage, assets.canvas, Gold, false);
            var maskImage = EnsureImage(frame, "PreviewMask");
            StretchInset(maskImage.rectTransform, 6f);
            StyleImage(maskImage, assets.canvas, Color.white, false);
            var oldMask = maskImage.GetComponent<Mask>();
            if (oldMask != null) Undo.DestroyObjectImmediate(oldMask);
            maskImage.enabled = false;
            if (maskImage.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(maskImage.gameObject);
            var imageRect = EnsureRect(maskImage.rectTransform, "PreviewImage");
            StretchInset(imageRect, 0f);
            var raw = imageRect.GetComponent<RawImage>() ?? Undo.AddComponent<RawImage>(imageRect.gameObject);
            raw.texture = assets.triptych;
            raw.uvRect = uv;
            raw.color = Color.white;
            raw.raycastTarget = false;
        }

        private static Assets LoadAssets()
        {
            var result = new Assets
            {
                canvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png"),
                surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png"),
                input = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_input.png"),
                logo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png"),
                emptyWorldHero = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyWorldHeroPath),
                triptych = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath)
            };
            if (result.logo == null || result.emptyWorldHero == null || result.triptych == null)
                throw new InvalidOperationException("TOV world UI art assets are not imported.");
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
            var custom = target.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "FGUIStarter.CustomButton");
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

        private static void EnsurePreview(GameObject root)
        {
            if (root.GetComponent<OntologyUiEditorPreview>() == null) Undo.AddComponent<OntologyUiEditorPreview>(root);
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

        private static void MarkDirty(GameObject root)
        {
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static void SaveActiveScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.SaveScene(scene);
        }

        private sealed class Assets
        {
            public Sprite canvas;
            public Sprite surface;
            public Sprite input;
            public Sprite logo;
            public Sprite emptyWorldHero;
            public Texture2D triptych;
        }
    }
}
#endif
