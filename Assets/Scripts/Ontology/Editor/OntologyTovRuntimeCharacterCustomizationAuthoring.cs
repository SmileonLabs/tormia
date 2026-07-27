#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    /// <summary>
    /// Reuses the approved hierarchy-authored character-creation visual system
    /// for the distinct in-world appearance editor. The runtime panel keeps its
    /// own input and persistence policy while sharing presentation assets.
    /// </summary>
    public static class OntologyTovRuntimeCharacterCustomizationAuthoring
    {
        private const string SourceName = "AccountCharacterAppearancePanel";
        private const string TargetName = "OntologyCharacterCustomizationPanel";
        private const string IconSetPath =
            "Assets/Data/Ontology/UI/CharacterCategoryIconSet.asset";

        [MenuItem("Tormia/UI/Apply TOV Runtime Character Customization Design")]
        public static void Apply()
        {
            var source = FindSceneObject(SourceName);
            var target = FindSceneObject(TargetName);
            if (source == null || target == null)
                throw new InvalidOperationException(
                    "Character appearance source or runtime target panel is missing.");

            var sourcePanel = source.GetComponent<OntologyCharacterCustomizationPanel>();
            var targetPanel = target.GetComponent<OntologyCharacterCustomizationPanel>();
            var sourceGroup = source.GetComponent<CanvasGroup>();
            var targetGroup = target.GetComponent<CanvasGroup>();
            if (sourcePanel == null || targetPanel == null || sourceGroup == null || targetGroup == null)
                throw new InvalidOperationException("Character customization panel bindings are incomplete.");

            Undo.RegisterFullObjectHierarchyUndo(target, "Apply TOV runtime character customization");
            CopyRootPresentation(source, target);
            ReplaceAuthoredChildren(source.transform, target.transform);

            var header = target.transform.Find(OntologyCharacterCustomizationUiConfig.HeaderName);
            if (header != null)
            {
                header.gameObject.SetActive(true);
                var title = header.Find(OntologyCharacterCustomizationUiConfig.TitleTextName)
                    ?.GetComponent<TMP_Text>();
                if (title != null)
                {
                    title.text = OntologyCharacterCustomizationUiConfig.Title;
                    title.color = new Color32(22, 53, 72, 255);
                    title.fontSize = 44f;
                    title.alignment = TextAlignmentOptions.Center;
                    title.gameObject.SetActive(true);
                }
                StyleCloseButton(header, title);
            }

            var editorPreview = target.transform.Find("EditorPreviewContent");
            if (editorPreview != null) editorPreview.gameObject.SetActive(true);
            var templates = target.transform.Find(OntologyCharacterCustomizationUiConfig.TemplatesName);
            if (templates != null) templates.gameObject.SetActive(false);

            var iconSet = AssetDatabase.LoadAssetAtPath<OntologyCharacterCategoryIconSet>(IconSetPath);
            if (iconSet == null)
                throw new InvalidOperationException("Character category icon set is missing.");

            var openButton = target.transform.parent
                ?.Find(OntologyCharacterCustomizationUiConfig.ToggleHintName)
                ?.GetComponent<Button>();
            var entryFlow = UnityEngine.Object.FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            BindPanel(targetPanel, targetGroup, iconSet, openButton, entryFlow);
            DisableAccountRuntimeInput(sourcePanel);
            BindPreviewPresenter(source, target, targetGroup);
            BindEditorPreview(target, targetGroup);

            targetGroup.alpha = 1f;
            targetGroup.interactable = true;
            targetGroup.blocksRaycasts = false;
            target.transform.SetAsLastSibling();
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(targetPanel);
            EditorUtility.SetDirty(sourcePanel);
            EditorSceneManager.MarkSceneDirty(target.scene);
            OntologyUiEditorPreviewSelector.ShowRuntimeCharacterCustomization();
            EditorSceneManager.SaveScene(target.scene);
            Debug.Log("Applied editable TOV runtime character customization UI.");
        }

        private static void CopyRootPresentation(GameObject source, GameObject target)
        {
            var sourceRect = source.GetComponent<RectTransform>();
            var targetRect = target.GetComponent<RectTransform>();
            targetRect.anchorMin = sourceRect.anchorMin;
            targetRect.anchorMax = sourceRect.anchorMax;
            targetRect.pivot = sourceRect.pivot;
            targetRect.anchoredPosition = sourceRect.anchoredPosition;
            targetRect.sizeDelta = sourceRect.sizeDelta;
            targetRect.localScale = sourceRect.localScale;

            var sourceImage = source.GetComponent<Image>();
            var targetImage = target.GetComponent<Image>();
            targetImage.sprite = sourceImage.sprite;
            targetImage.type = sourceImage.type;
            targetImage.color = sourceImage.color;
            targetImage.raycastTarget = true;

            var sourceShadow = source.GetComponent<Shadow>();
            var targetShadow = target.GetComponent<Shadow>();
            if (sourceShadow != null)
            {
                if (targetShadow == null) targetShadow = Undo.AddComponent<Shadow>(target);
                targetShadow.effectColor = sourceShadow.effectColor;
                targetShadow.effectDistance = sourceShadow.effectDistance;
                targetShadow.useGraphicAlpha = sourceShadow.useGraphicAlpha;
            }
        }

        private static void StyleCloseButton(Transform header, TMP_Text title)
        {
            var close = header.Find(OntologyCharacterCustomizationUiConfig.CloseButtonName)
                ?.GetComponent<Button>();
            if (close == null) return;
            close.gameObject.SetActive(true);
            var image = close.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Art/UI/CharacterCreationFlat/flat_coral.png");
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                close.targetGraphic = image;
            }

            var label = close.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            label.text = "X";
            if (title != null)
            {
                label.font = title.font;
                label.fontSharedMaterial = title.fontSharedMaterial;
            }
            label.color = new Color32(22, 53, 72, 255);
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.rectTransform.localScale = Vector3.one;
            label.gameObject.SetActive(false);
            CreateCloseGlyphLine(close.transform, "CloseGlyphA", 45f);
            CreateCloseGlyphLine(close.transform, "CloseGlyphB", -45f);
        }

        private static void CreateCloseGlyphLine(Transform parent, string name, float rotation)
        {
            var existing = parent.Find(name);
            var line = existing == null
                ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                : existing.gameObject;
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(line, "Create close button glyph");
                line.transform.SetParent(parent, false);
            }
            var rect = (RectTransform)line.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(24f, 4f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var image = line.GetComponent<Image>();
            image.sprite = null;
            image.color = new Color32(22, 53, 72, 255);
            image.raycastTarget = false;
            line.SetActive(true);
        }

        private static void ReplaceAuthoredChildren(Transform source, Transform target)
        {
            for (var index = target.childCount - 1; index >= 0; index--)
                Undo.DestroyObjectImmediate(target.GetChild(index).gameObject);

            for (var index = 0; index < source.childCount; index++)
            {
                var child = source.GetChild(index);
                if (child.name == "AccountCharacterCreationPanel") continue;
                var clone = UnityEngine.Object.Instantiate(child.gameObject, target);
                Undo.RegisterCreatedObjectUndo(clone, "Clone TOV character customization hierarchy");
                clone.name = child.name;
                clone.transform.SetSiblingIndex(target.childCount - 1);
            }
        }

        private static void BindPanel(
            OntologyCharacterCustomizationPanel panel,
            CanvasGroup group,
            OntologyCharacterCategoryIconSet iconSet,
            Button openButton,
            OntologyWorldAuthorityAccountEntryFlow entryFlow)
        {
            var root = panel.transform;
            var serialized = new SerializedObject(panel);
            Set(serialized, "categoryIconSet", iconSet);
            Set(serialized, "panelCanvasGroup", group);
            Set(serialized, "categoryContainer",
                root.Find("CategoryArea/CategoryScrollView/CategoryViewport/CategoryContent"));
            Set(serialized, "partGridContainer",
                root.Find("PartGridArea/ScrollView/Viewport/PartGridContent"));
            Set(serialized, "categoryButtonTemplate",
                root.Find("Templates/CategoryButton_Template")?.GetComponent<Button>());
            Set(serialized, "partCardTemplate",
                root.Find("Templates/PartCard_Template")?.GetComponent<Button>());
            Set(serialized, "selectedIcon",
                root.Find("DetailArea/SelectedIcon")?.GetComponent<Image>());
            Set(serialized, "selectedTitle",
                root.Find("DetailArea/SelectedTitle")?.GetComponent<TMP_Text>());
            Set(serialized, "selectedDescription",
                root.Find("DetailArea/SelectedDescription")?.GetComponent<TMP_Text>());
            Set(serialized, "factPreview",
                root.Find("DetailArea/FactPreview")?.GetComponent<TMP_Text>());
            Set(serialized, "statusText", root.Find("Status")?.GetComponent<TMP_Text>());
            Set(serialized, "toggleHintText", openButton?.GetComponentInChildren<TMP_Text>(true));
            Set(serialized, "openButton", openButton);
            Set(serialized, "equipButton",
                root.Find("DetailArea/EquipButton")?.GetComponent<Button>());
            Set(serialized, "unequipButton",
                root.Find("DetailArea/UnequipButton")?.GetComponent<Button>());
            Set(serialized, "closeButton",
                root.Find("Header/CloseButton")?.GetComponent<Button>());
            Set(serialized, "accountEntryFlow", entryFlow);
            serialized.FindProperty("startsVisible").boolValue = false;
            serialized.FindProperty("equipOnPartSelection").boolValue = true;
            serialized.FindProperty("allowRuntimeToggle").boolValue = true;
            serialized.FindProperty("persistAccountAppearanceOnClose").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DisableAccountRuntimeInput(OntologyCharacterCustomizationPanel panel)
        {
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("allowRuntimeToggle").boolValue = false;
            serialized.FindProperty("persistAccountAppearanceOnClose").boolValue = false;
            Set(serialized, "openButton", null);
            Set(serialized, "toggleHintText", null);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindPreviewPresenter(
            GameObject source,
            GameObject target,
            CanvasGroup targetGroup)
        {
            var sourcePresenter = source.GetComponent<OntologyCharacterCreationPreviewPresenter>();
            var presenter = target.GetComponent<OntologyCharacterCreationPreviewPresenter>();
            if (presenter == null) presenter = Undo.AddComponent<OntologyCharacterCreationPreviewPresenter>(target);

            var serialized = new SerializedObject(presenter);
            if (sourcePresenter != null)
            {
                var sourceSerialized = new SerializedObject(sourcePresenter);
                serialized.FindProperty("viewDirection").vector3Value =
                    sourceSerialized.FindProperty("viewDirection").vector3Value;
                serialized.FindProperty("fieldOfView").floatValue =
                    sourceSerialized.FindProperty("fieldOfView").floatValue;
                serialized.FindProperty("framingDistance").floatValue =
                    sourceSerialized.FindProperty("framingDistance").floatValue;
                serialized.FindProperty("characterScale").floatValue =
                    sourceSerialized.FindProperty("characterScale").floatValue;
                serialized.FindProperty("previewLayer").intValue =
                    sourceSerialized.FindProperty("previewLayer").intValue;
            }
            Set(serialized, "panelGroup", targetGroup);
            Set(serialized, "previewCamera",
                target.transform.Find("CharacterPreviewCamera")?.GetComponent<Camera>());
            Set(serialized, "previewImage",
                target.transform.Find("CharacterPreviewFrame/LiveCharacterPreview")
                    ?.GetComponent<RawImage>());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindEditorPreview(GameObject target, CanvasGroup group)
        {
            var preview = target.GetComponent<OntologyUiEditorPreview>();
            if (preview == null) preview = Undo.AddComponent<OntologyUiEditorPreview>(target);
            var serialized = new SerializedObject(preview);
            Set(serialized, "canvasGroup", group);
            serialized.FindProperty("previewInEditor").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException("Serialized property is missing: " + name);
            property.objectReferenceValue = value;
        }

        private static GameObject FindSceneObject(string name)
        {
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform != null && transform.gameObject.scene.IsValid() &&
                    transform.name == name)
                    return transform.gameObject;
            }
            return null;
        }
    }
}
#endif
