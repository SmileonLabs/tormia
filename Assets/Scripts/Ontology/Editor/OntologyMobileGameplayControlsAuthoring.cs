using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Editor
{
    internal static class OntologyMobileGameplayControlsAuthoring
    {
        private const string ScenePath = "Assets/Scenes/TormiaUI.unity";
        private const string CanvasPath = "TormiaUIRoot/OntologyGameCanvas";
        private const string PrefabPath =
            "Assets/Data/Ontology/UI/MobileGameplayControls.prefab";

        [MenuItem("TOV/UI/Author Mobile Gameplay Controls")]
        public static void Author()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            var canvas = GameObject.Find(CanvasPath);
            if (canvas == null || canvas.scene != scene)
                throw new MissingReferenceException(CanvasPath);

            var existing = canvas.transform.Find("MobileGameplayControls");
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var root = CreateRect("MobileGameplayControls", canvas.transform);
            Stretch(root);
            var controls = root.gameObject.AddComponent<
                OntologyMobileGameplayControls>();

            var safeArea = CreateRect("SafeArea", root);
            Stretch(safeArea);
            safeArea.gameObject.SetActive(false);

            CreateStick(safeArea);
            CreateButton(
                safeArea,
                "JumpButton",
                "JUMP",
                new Vector2(-150f, 145f),
                "<Gamepad>/buttonSouth",
                new Color(0.18f, 0.49f, 0.82f, 0.82f));
            CreateButton(
                safeArea,
                "EquipButton",
                "F",
                new Vector2(-285f, 95f),
                "<Gamepad>/rightShoulder",
                new Color(0.48f, 0.72f, 0.19f, 0.82f));

            var serialized = new SerializedObject(controls);
            serialized.FindProperty("controlsRoot").objectReferenceValue = safeArea;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var folder = System.IO.Path.GetDirectoryName(PrefabPath);
            if (!AssetDatabase.IsValidFolder(folder))
                throw new UnityException("Missing prefab folder: " + folder);
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                root.gameObject,
                PrefabPath,
                InteractionMode.UserAction);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root.gameObject;
        }

        private static void CreateStick(RectTransform parent)
        {
            var background = CreateRect("MoveStick", parent);
            background.anchorMin = background.anchorMax = new Vector2(0f, 0f);
            background.pivot = new Vector2(0.5f, 0.5f);
            background.anchoredPosition = new Vector2(155f, 155f);
            background.sizeDelta = new Vector2(190f, 190f);
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.04f, 0.13f, 0.22f, 0.38f);
            backgroundImage.raycastTarget = false;

            var handle = CreateRect("Handle", background);
            handle.anchorMin = handle.anchorMax = handle.pivot =
                new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(92f, 92f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.96f, 0.72f, 0.18f, 0.9f);
            var stick = handle.gameObject.AddComponent<OnScreenStick>();
            stick.controlPath = "<Gamepad>/leftStick";
            stick.movementRange = 64f;
        }

        private static void CreateButton(
            RectTransform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            string controlPath,
            Color color)
        {
            var button = CreateRect(name, parent);
            button.anchorMin = button.anchorMax = new Vector2(1f, 0f);
            button.pivot = new Vector2(0.5f, 0.5f);
            button.anchoredPosition = anchoredPosition;
            button.sizeDelta = new Vector2(112f, 112f);
            var image = button.gameObject.AddComponent<Image>();
            image.color = color;
            var onScreen = button.gameObject.AddComponent<OnScreenButton>();
            onScreen.controlPath = controlPath;

            var textRect = CreateRect("Label", button);
            Stretch(textRect);
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 25f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent)
        {
            var gameObject = new GameObject(
                name,
                typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
