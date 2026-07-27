#if UNITY_EDITOR
using TMPro;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tormia.Ontology.Editor
{
    public static class OntologyCreatorConversationPanelAuthoring
    {
        private const string UiScenePath = "Assets/Scenes/TormiaUI.unity";
        private static readonly Color Cream = new(1f, 0.956f, 0.84f, 1f);
        private static readonly Color Navy = new(0.055f, 0.18f, 0.29f, 1f);
        private static readonly Color Blue = new(0.12f, 0.30f, 0.58f, 1f);
        private static readonly Color Gold = new(0.78f, 0.52f, 0.13f, 1f);

        [MenuItem("Tormia/Creator Workspace/Build World Architect Conversation Panel %#w")]
        public static void Build()
        {
            var previousActiveScene = SceneManager.GetActiveScene();
            var uiScene = SceneManager.GetSceneByPath(UiScenePath);
            if (!uiScene.IsValid() || !uiScene.isLoaded)
            {
                uiScene = EditorSceneManager.OpenScene(
                    UiScenePath,
                    OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(uiScene);

            var canvas = GameObject.Find("OntologyGameCanvas");
            if (canvas == null)
            {
                throw new MissingReferenceException(
                    "Open TormiaUI and ensure OntologyGameCanvas exists.");
            }

            var existing = canvas.transform.Find("CreatorConversationPanel");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var panel = Rect(
                canvas.transform,
                "CreatorConversationPanel",
                Vector2.zero,
                Vector2.zero,
                Vector2.one,
                Vector2.zero);
            var group = Undo.AddComponent<CanvasGroup>(panel.gameObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            Undo.AddComponent<OntologyCreatorConversationPanel>(panel.gameObject);

            var dimmer = Rect(
                panel,
                "Dimmer",
                Vector2.zero,
                Vector2.zero,
                Vector2.one,
                Vector2.zero);
            var dimmerImage = Undo.AddComponent<Image>(dimmer.gameObject);
            dimmerImage.color = new Color(0.02f, 0.04f, 0.06f, 0.62f);

            var card = Rect(
                panel,
                "Card",
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(800f, 570f));
            var cardImage = Undo.AddComponent<Image>(card.gameObject);
            cardImage.color = Cream;
            var outline = Undo.AddComponent<Outline>(card.gameObject);
            outline.effectColor = Gold;
            outline.effectDistance = new Vector2(4f, -4f);

            var header = Rect(
                card,
                "Header",
                new Vector2(0f, 242f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(740f, 68f));
            var headerImage = Undo.AddComponent<Image>(header.gameObject);
            headerImage.color = Navy;
            Text(header, "Title", "WORLD ARCHITECT", 28, Color.white, TextAlignmentOptions.Center);
            Button(
                header,
                "CloseButton",
                new Vector2(326f, 0f),
                new Vector2(44f, 44f),
                "×",
                new Color(0.82f, 0.23f, 0.18f, 1f));

            TextAt(
                card,
                "Summary",
                new Vector2(0f, 174f),
                new Vector2(700f, 58f),
                "Tell me what kind of game you want to make.",
                20,
                Navy,
                TextAlignmentOptions.Center);

            Input(
                card,
                "PromptInput",
                new Vector2(0f, 66f),
                new Vector2(700f, 126f));

            TextAt(
                card,
                "Route",
                new Vector2(0f, -38f),
                new Vector2(700f, 62f),
                "The production route will appear here.",
                16,
                Navy,
                TextAlignmentOptions.Center);

            TextAt(
                card,
                "Status",
                new Vector2(0f, -112f),
                new Vector2(700f, 52f),
                string.Empty,
                16,
                new Color(0.45f, 0.25f, 0.08f, 1f),
                TextAlignmentOptions.Center);

            Button(
                card,
                "StartButton",
                new Vector2(0f, -202f),
                new Vector2(310f, 58f),
                "START DRAFT",
                Blue);

            EditorUtility.SetDirty(panel.gameObject);
            EditorSceneManager.MarkSceneDirty(uiScene);
            EditorSceneManager.SaveScene(uiScene);
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
            Selection.activeGameObject = panel.gameObject;
        }

        private static RectTransform Rect(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Text(
            Transform parent,
            string name,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment)
        {
            var rect = Rect(
                parent,
                name,
                Vector2.zero,
                Vector2.zero,
                Vector2.one,
                Vector2.zero);
            return ConfigureText(rect.gameObject, value, size, color, alignment);
        }

        private static TMP_Text TextAt(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 sizeDelta,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment)
        {
            var rect = Rect(
                parent,
                name,
                position,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                sizeDelta);
            return ConfigureText(rect.gameObject, value, size, color, alignment);
        }

        private static TMP_Text ConfigureText(
            GameObject target,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment)
        {
            var label = Undo.AddComponent<TextMeshProUGUI>(target);
            label.text = value;
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        private static Button Button(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            string label,
            Color color)
        {
            var rect = Rect(
                parent,
                name,
                position,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                size);
            var image = Undo.AddComponent<Image>(rect.gameObject);
            image.color = color;
            var button = Undo.AddComponent<Button>(rect.gameObject);
            button.targetGraphic = image;
            Text(
                rect,
                "Label",
                label,
                21,
                Color.white,
                TextAlignmentOptions.Center);
            return button;
        }

        private static TMP_InputField Input(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size)
        {
            var rect = Rect(
                parent,
                name,
                position,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                size);
            var image = Undo.AddComponent<Image>(rect.gameObject);
            image.color = Color.white;
            var field = Undo.AddComponent<TMP_InputField>(rect.gameObject);
            field.targetGraphic = image;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.characterValidation = TMP_InputField.CharacterValidation.None;

            var textArea = Rect(
                rect,
                "Text Area",
                Vector2.zero,
                Vector2.zero,
                Vector2.one,
                new Vector2(-30f, -24f));
            var text = Text(
                textArea,
                "Text",
                string.Empty,
                19,
                Navy,
                TextAlignmentOptions.TopLeft);
            var placeholder = Text(
                textArea,
                "Placeholder",
                "Describe the game you want to make...",
                19,
                new Color(0.35f, 0.38f, 0.40f, 0.6f),
                TextAlignmentOptions.TopLeft);
            field.textViewport = textArea;
            field.textComponent = text;
            field.placeholder = placeholder;
            return field;
        }
    }
}
#endif
