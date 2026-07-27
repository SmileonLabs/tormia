using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    public static class OntologyAccountCreationPanelBrandAuthoring
    {
        [MenuItem("Tormia/UI/Apply Editable TOV Account Creation Design")]
        public static void Apply()
        {
            var rootObject = GameObject.Find("AccountCreationPanel");
            if (rootObject == null) throw new InvalidOperationException("AccountCreationPanel was not found in the open scene.");

            var root = rootObject.GetComponent<RectTransform>();
            var flatCanvas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_canvas.png");
            var flatSurface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_surface.png");
            var flatInput = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/CharacterCreationFlat/flat_input.png");
            var logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AccountLogin/tov_logo.png");
            if (logoSprite == null) throw new InvalidOperationException("The imported TOV logo sprite is missing.");

            Undo.RecordObject(root, "Apply editable TOV account creation design");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(860f, 1000f);
            root.localScale = Vector3.one;

            var blue = EnsureImage(root, "BrandFrameBlue");
            var gold = EnsureImage(root, "BrandFrameGold");
            var surface = EnsureImage(root, "CardSurface");
            StretchInset(blue.rectTransform, 0f);
            StretchInset(gold.rectTransform, 7f);
            StretchInset(surface.rectTransform, 12f);
            StyleImage(blue, flatCanvas, new Color32(8, 55, 125, 255), false);
            StyleImage(gold, flatCanvas, new Color32(220, 165, 39, 255), false);
            StyleImage(surface, flatSurface != null ? flatSurface : flatCanvas, new Color32(255, 248, 235, 255), true);
            blue.transform.SetSiblingIndex(0);
            gold.transform.SetSiblingIndex(1);
            surface.transform.SetSiblingIndex(2);

            var shadow = blue.GetComponent<Shadow>() ?? Undo.AddComponent<Shadow>(blue.gameObject);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -8f);

            var logo = EnsureImage(root, "TovLogo");
            PlaceTop(logo.rectTransform, new Vector2(0f, -4f), new Vector2(610f, 300f));
            logo.sprite = logoSprite;
            logo.preserveAspect = true;
            logo.color = Color.white;
            logo.raycastTarget = false;
            logo.transform.SetSiblingIndex(3);

            var title = Require<TextMeshProUGUI>(root, "Text (TMP)");
            PlaceTop(title.rectTransform, new Vector2(0f, -270f), new Vector2(700f, 92f));
            StyleText(title, 36f, TextAlignmentOptions.Center, new Color32(22, 53, 72, 255), FontStyles.Normal);
            title.richText = true;

            var emailLabel = Require<TextMeshProUGUI>(root, "EmailLabel");
            var displayNameLabel = Require<TextMeshProUGUI>(root, "DisplayNameLabel");
            var passwordLabel = Require<TextMeshProUGUI>(root, "PasswordLabel");
            PlaceTop(emailLabel.rectTransform, new Vector2(0f, -365f), new Vector2(650f, 30f));
            PlaceTop(displayNameLabel.rectTransform, new Vector2(0f, -480f), new Vector2(650f, 30f));
            PlaceTop(passwordLabel.rectTransform, new Vector2(0f, -595f), new Vector2(650f, 30f));
            foreach (var label in new[] { emailLabel, displayNameLabel, passwordLabel })
                StyleText(label, 21f, TextAlignmentOptions.Left, new Color32(22, 53, 72, 255), FontStyles.Bold);

            var emailInput = StyleInput(root, "EmailInput", new Vector2(0f, -400f), TMP_InputField.ContentType.EmailAddress, "name@example.com", flatInput, flatCanvas);
            var displayNameInput = StyleInput(root, "DisplayNameInput", new Vector2(0f, -515f), TMP_InputField.ContentType.Standard, "Your in-game name", flatInput, flatCanvas);
            var passwordInput = StyleInput(root, "PasswordInput", new Vector2(0f, -630f), TMP_InputField.ContentType.Password, "At least 10 characters", flatInput, flatCanvas);
            var createButton = StyleButton(root, "CreateAccountButton", "CreateButtonSurface", new Vector2(0f, -725f), new Vector2(650f, 74f), true, flatCanvas);
            var backButton = StyleButton(root, "BackButton", "BackButtonSurface", new Vector2(0f, -815f), new Vector2(650f, 68f), false, flatCanvas);

            DestroyChild(root, "CustomizeAppearanceButton");
            var rootImage = rootObject.GetComponent<Image>();
            if (rootImage != null) Undo.DestroyObjectImmediate(rootImage);
            var rootRenderer = rootObject.GetComponent<CanvasRenderer>();
            if (rootRenderer != null) Undo.DestroyObjectImmediate(rootRenderer);
            if (rootObject.GetComponent<OntologyUiEditorPreview>() == null) Undo.AddComponent<OntologyUiEditorPreview>(rootObject);

            var panel = rootObject.GetComponent<OntologyAccountCreationPanel>();
            var serializedPanel = new SerializedObject(panel);
            SetReference(serializedPanel, "emailInput", emailInput);
            SetReference(serializedPanel, "displayNameInput", displayNameInput);
            SetReference(serializedPanel, "passwordInput", passwordInput);
            SetReference(serializedPanel, "statusLabel", title);
            SetReference(serializedPanel, "emailLabel", emailLabel);
            SetReference(serializedPanel, "displayNameLabel", displayNameLabel);
            SetReference(serializedPanel, "passwordLabel", passwordLabel);
            SetReference(serializedPanel, "createButton", createButton);
            SetReference(serializedPanel, "backButton", backButton);
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            rootObject.SetActive(true);
            EditorUtility.SetDirty(rootObject);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            EditorSceneManager.SaveScene(rootObject.scene);
            Debug.Log("Applied editable TOV account creation UI to " + rootObject.scene.path + ".");
        }

        private static TMP_InputField StyleInput(RectTransform root, string name, Vector2 position, TMP_InputField.ContentType contentType, string placeholderText, Sprite inputSprite, Sprite fallback)
        {
            var rect = Require<RectTransform>(root, name);
            PlaceTop(rect, position, new Vector2(650f, 64f));
            var input = rect.GetComponent<TMP_InputField>();
            var background = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            StyleImage(background, inputSprite != null ? inputSprite : fallback, Color.white, true);
            input.targetGraphic = background;
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;

            var text = rect.Find("Text") != null ? rect.Find("Text").GetComponent<TextMeshProUGUI>() : input.textComponent as TextMeshProUGUI;
            StretchText(text.rectTransform);
            StyleText(text, 23f, TextAlignmentOptions.Left, new Color32(22, 53, 72, 255), FontStyles.Normal);
            input.textComponent = text;

            var placeholderTransform = rect.Find("Placeholder");
            TextMeshProUGUI placeholder;
            if (placeholderTransform == null)
            {
                var placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                Undo.RegisterCreatedObjectUndo(placeholderObject, "Create " + name + " placeholder");
                placeholderObject.transform.SetParent(rect, false);
                placeholder = placeholderObject.GetComponent<TextMeshProUGUI>();
                placeholder.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
            }
            else placeholder = placeholderTransform.GetComponent<TextMeshProUGUI>();
            StretchText(placeholder.rectTransform);
            placeholder.font = text.font;
            placeholder.fontSharedMaterial = text.fontSharedMaterial;
            StyleText(placeholder, 23f, TextAlignmentOptions.Left, new Color32(80, 101, 112, 125), FontStyles.Normal);
            placeholder.text = placeholderText;
            input.placeholder = placeholder;
            return input;
        }

        private static Button StyleButton(RectTransform root, string name, string surfaceName, Vector2 position, Vector2 size, bool primary, Sprite sprite)
        {
            var rect = Require<RectTransform>(root, name);
            PlaceTop(rect, position, size);
            var custom = rect.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "FGUIStarter.CustomButton");
            if (custom != null) Undo.DestroyObjectImmediate(custom);
            var button = rect.GetComponent<Button>() ?? Undo.AddComponent<Button>(rect.gameObject);
            var outline = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
            StyleImage(outline, sprite, primary ? new Color32(220, 165, 39, 255) : new Color32(8, 55, 125, 255), true);
            button.targetGraphic = outline;
            button.transition = Selectable.Transition.ColorTint;

            var inner = EnsureImage(rect, surfaceName);
            StretchInset(inner.rectTransform, 3f);
            StyleImage(inner, sprite, primary ? new Color32(8, 55, 125, 255) : new Color32(255, 248, 235, 255), false);
            inner.transform.SetSiblingIndex(0);

            var label = rect.GetComponentInChildren<TextMeshProUGUI>(true);
            StretchInset(label.rectTransform, 4f);
            label.rectTransform.sizeDelta = new Vector2(-24f, -8f);
            StyleText(label, 27f, TextAlignmentOptions.Center, primary ? new Color32(255, 248, 235, 255) : new Color32(8, 55, 125, 255), FontStyles.Bold);
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

        private static T Require<T>(RectTransform parent, string name) where T : Component
        {
            var child = parent.Find(name);
            var component = child != null ? child.GetComponent<T>() : null;
            if (component == null) throw new InvalidOperationException(name + " is missing " + typeof(T).Name + ".");
            return component;
        }

        private static void PlaceTop(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static void StretchInset(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-inset * 2f, -inset * 2f);
            rect.localScale = Vector3.one;
        }

        private static void StretchText(RectTransform rect)
        {
            StretchInset(rect, 5f);
            rect.sizeDelta = new Vector2(-48f, -10f);
        }

        private static void StyleImage(Image image, Sprite sprite, Color color, bool raycastTarget)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycastTarget;
        }

        private static void StyleText(TextMeshProUGUI text, float size, TextAlignmentOptions alignment, Color color, FontStyles style)
        {
            text.alignment = alignment;
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
        }

        private static void DestroyChild(RectTransform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
        }

        private static void SetReference(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            target.FindProperty(propertyName).objectReferenceValue = value;
        }
    }
}
