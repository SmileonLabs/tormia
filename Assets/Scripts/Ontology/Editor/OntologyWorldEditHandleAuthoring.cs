using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    public static class OntologyWorldEditHandleAuthoring
    {
        private const string PrefabPath =
            "Assets/Prefabs/Ontology/UI/WorldEditContextHandle.prefab";
        private const string IconFolder =
            "Assets/Art/UI/WorldEditHandle/";

        private static readonly (string button, string icon)[] OrderedButtons =
        {
            ("MoveButton", "icon_move.png"),
            ("RotateLeftButton", "icon_rotate_left.png"),
            ("RotateRightButton", "icon_rotate_right.png"),
            ("ScaleUpButton", "icon_scale_up.png"),
            ("ScaleDownButton", "icon_scale_down.png"),
            ("DuplicateButton", "icon_duplicate.png"),
            ("OntologyButton", "icon_ontology.png"),
            ("DeleteButton", "icon_delete.png")
        };

        [MenuItem("Tormia/Ontology/Apply TOV World Edit Handle")]
        public static void Apply()
        {
            ImportIcons();
            ApplyToPrefab();
            ApplyToLoadedSceneInstances();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "[OntologyWorldEditHandleAuthoring] Applied centered icon-only " +
                "world edit controls and requested button order.");
        }

        private static void ImportIcons()
        {
            foreach (var (_, iconName) in OrderedButtons)
            {
                var path = IconFolder + iconName;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    throw new InvalidOperationException(
                        $"Texture importer was not found for '{path}'.");

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 512;
                importer.textureCompression =
                    TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        private static void ApplyToPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ApplyToHierarchy(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyToLoadedSceneInstances()
        {
            var changedScenes = new HashSet<Scene>();
            foreach (var handle in UnityEngine.Object
                         .FindObjectsByType<OntologyRuntimeWorldEditHandle>(
                             FindObjectsInactive.Include))
            {
                if (handle == null || EditorUtility.IsPersistent(handle))
                    continue;

                ApplyToHierarchy(handle.transform);
                changedScenes.Add(handle.gameObject.scene);
            }

            foreach (var scene in changedScenes.Where(scene => scene.IsValid()))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        private static void ApplyToHierarchy(Transform root)
        {
            var panel = root.Find("HandlePanel");
            if (panel == null)
                throw new InvalidOperationException(
                    $"HandlePanel was not found under '{root.name}'.");

            for (var index = 0; index < OrderedButtons.Length; index++)
            {
                var (buttonName, iconName) = OrderedButtons[index];
                var button = panel.Find(buttonName);
                if (button == null)
                    throw new InvalidOperationException(
                        $"{buttonName} was not found under HandlePanel.");

                button.SetSiblingIndex(index);
                var icon = button.Find("Icon");
                if (icon == null)
                    throw new InvalidOperationException(
                        $"Icon was not found under {buttonName}.");

                var iconImage = icon.GetComponent<Image>();
                if (iconImage == null)
                    throw new InvalidOperationException(
                        $"Image was not found on {buttonName}/Icon.");

                iconImage.sprite =
                    AssetDatabase.LoadAssetAtPath<Sprite>(IconFolder + iconName);
                iconImage.color = Color.white;
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = true;

                var rect = icon as RectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(-18f, -18f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;

                EditorUtility.SetDirty(iconImage);
                EditorUtility.SetDirty(rect);
                EditorUtility.SetDirty(button);
            }
        }
    }
}
