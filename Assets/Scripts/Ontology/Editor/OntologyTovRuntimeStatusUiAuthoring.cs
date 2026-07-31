#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Authors the TOV runtime status card and actor toast as ordinary uGUI
    /// objects. Every visual layer remains selectable and editable in Hierarchy.
    /// </summary>
    public static class OntologyTovRuntimeStatusUiAuthoring
    {
        private const string IconFolder = "Assets/Art/UI/RuntimeStatusIcons/";
        private const string FlatFolder = "Assets/Art/UI/CharacterCreationFlat/";
        private static readonly Color Navy = new(0.075f, 0.145f, 0.18f, 1f);
        private static readonly Color Cream = new(0.98f, 0.947f, 0.86f, 1f);
        private static readonly Color Gold = new(0.78f, 0.58f, 0.25f, 1f);
        private static readonly Color Green = new(0.37f, 0.62f, 0.18f, 1f);
        private static readonly Color Muted = new(0.33f, 0.39f, 0.39f, 1f);

        [MenuItem("Tormia/UI/Apply TOV Runtime Status & Toast Design")]
        public static void ApplyToOpenScene()
        {
            PrepareIconImporters();
            AssetDatabase.Refresh();

            var hud = Object.FindAnyObjectByType<OntologyRuntimeStatusHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogError("[TOV UI] OntologyRuntimeStatusHUD was not found in the open scene.");
                return;
            }

            AuthorHud(hud);
            var toast = Object.FindAnyObjectByType<OntologyActorToast>(FindObjectsInactive.Include);
            if (toast != null) AuthorToast(toast);
            else Debug.LogWarning("[TOV UI] OntologyActorToast was not found; status HUD was still authored.");

            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Selection.activeGameObject = hud.gameObject;
            Debug.Log("[TOV UI] Runtime status HUD and actor toast authored as editable hierarchy objects.");
        }

        private static void PrepareIconImporters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
        }

        private static void AuthorHud(OntologyRuntimeStatusHUD hud)
        {
            var root = hud.GetComponent<RectTransform>();
            var gameCanvas = GameObject.Find("OntologyGameCanvas");
            if (gameCanvas != null && root.parent != gameCanvas.transform)
                root.SetParent(gameCanvas.transform, false);
            root.gameObject.SetActive(true);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(28f, -28f);
            root.sizeDelta = new Vector2(430f, 320f);
            ClearChildren(root);

            var rootImage = GetOrAdd<Image>(root.gameObject);
            rootImage.sprite = Sprite(FlatFolder + "flat_taupe.png");
            rootImage.type = Image.Type.Sliced;
            rootImage.color = Gold;
            rootImage.raycastTarget = false;

            var previousShadow = root.parent.Find("StatusCardShadow");
            if (previousShadow != null) Undo.DestroyObjectImmediate(previousShadow.gameObject);
            var shadow = ImageObject(root.parent, "StatusCardShadow", Sprite(FlatFolder + "flat_navy.png"),
                new Color(0.03f, 0.07f, 0.08f, 0.28f));
            shadow.SetSiblingIndex(root.GetSiblingIndex());
            CopyRect(root, shadow);
            shadow.anchoredPosition += new Vector2(7f, -8f);
            root.SetSiblingIndex(shadow.GetSiblingIndex() + 1);

            var surface = ImageObject(root, "CardSurface", Sprite(FlatFolder + "flat_canvas.png"), Cream);
            Stretch(surface, 4f, 4f, 4f, 4f);

            var header = ImageObject(root, "HeaderBar", Sprite(FlatFolder + "flat_navy.png"), Navy);
            Anchor(header, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -4f), new Vector2(422f, 68f));
            var crest = ImageObject(header, "HeaderCrest", Sprite(IconFolder + "status_movement.png"), Color.white);
            Anchor(crest, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(23f, 0f), new Vector2(48f, 48f));
            crest.GetComponent<Image>().preserveAspect = true;
            var title = TextObject(header, "Title", "WORLD STATUS", 29f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Stretch(title.rectTransform, 79f, 10f, 18f, 10f);

            var rows = RectObject(root, "StatusRows");
            rows.anchorMin = Vector2.zero;
            rows.anchorMax = Vector2.one;
            rows.offsetMin = new Vector2(18f, 14f);
            rows.offsetMax = new Vector2(-18f, -76f);

            var movement = Row(rows, "MovementRow", "MOVEMENT", "Explore", "status_movement.png", 0);
            var interaction = Row(rows, "InteractionRow", "INTERACTION", "None", "status_interaction.png", 1);
            var permission = Row(rows, "PermissionRow", "PERMISSION", "Builder", "status_permission.png", 2);
            var save = Row(rows, "SaveRow", "SAVE", "Saved", "status_save.png", 3);

            var serialized = new SerializedObject(hud);
            Set(serialized, "titleText", title);
            Set(serialized, "movementLabel", movement.label);
            Set(serialized, "movementValue", movement.value);
            Set(serialized, "interactionLabel", interaction.label);
            Set(serialized, "interactionValue", interaction.value);
            Set(serialized, "permissionLabel", permission.label);
            Set(serialized, "permissionValue", permission.value);
            Set(serialized, "saveLabel", save.label);
            Set(serialized, "saveValue", save.value);
            Set(serialized, "titleHitArea", header);
            Set(serialized, "collapsibleContent", rows.gameObject);
            Set(serialized, "collapseShadow", shadow);
            serialized.FindProperty("expandedHeight").floatValue = 320f;
            serialized.FindProperty("collapsedHeight").floatValue = 76f;
            Set(serialized, "textMeshProText", null);
            Set(serialized, "uiText", null);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);
        }

        private static (TextMeshProUGUI label, TextMeshProUGUI value) Row(
            RectTransform parent, string name, string labelValue, string statusValue, string iconName, int index)
        {
            const float height = 57f;
            var row = RectObject(parent, name);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -index * height);
            row.sizeDelta = new Vector2(0f, height);

            if (index > 0)
            {
                var divider = ImageObject(row, "Divider", null, new Color(Gold.r, Gold.g, Gold.b, 0.28f));
                divider.anchorMin = new Vector2(0f, 1f);
                divider.anchorMax = new Vector2(1f, 1f);
                divider.pivot = new Vector2(0.5f, 1f);
                divider.anchoredPosition = Vector2.zero;
                divider.sizeDelta = new Vector2(-8f, 1f);
            }

            var plate = ImageObject(row, "IconPlate", Sprite(FlatFolder + "flat_navy.png"), new Color(0.09f, 0.18f, 0.21f, 0.98f));
            Anchor(plate, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(43f, 43f));
            var icon = ImageObject(plate, "Icon", Sprite(IconFolder + iconName), Color.white);
            Stretch(icon, 5f, 5f, 5f, 5f);
            icon.GetComponent<Image>().preserveAspect = true;

            var label = TextObject(row, "Label", labelValue, 15f, Muted, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            label.rectTransform.offsetMin = new Vector2(59f, 0f);
            label.rectTransform.offsetMax = new Vector2(0f, 0f);
            var value = TextObject(row, "Value", statusValue, 21f, Navy, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            value.rectTransform.anchorMin = new Vector2(0.58f, 0f);
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = new Vector2(-8f, 0f);
            if (index == 3) value.color = Green;
            return (label, value);
        }

        private static void AuthorToast(OntologyActorToast toast)
        {
            var canvas = toast.GetComponentInChildren<Canvas>(true);
            var createdCanvas = false;
            if (canvas == null)
            {
                var canvasRect = RectObject(toast.transform, "ActorToastCanvas");
                canvas = canvasRect.gameObject.AddComponent<Canvas>();
                canvas.gameObject.AddComponent<CanvasGroup>();
                createdCanvas = true;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            var root = canvas.GetComponent<RectTransform>();
            if (createdCanvas)
            {
                // Keep a newly authored world-space preview near its actor and
                // at the same practical scale used by the default UI theme.
                root.anchoredPosition3D = new Vector3(0f, 0.45f, 0f);
                root.localScale = Vector3.one * 0.004f;
            }
            root.sizeDelta = new Vector2(300f, 76f);
            ClearChildren(root);
            var group = GetOrAdd<CanvasGroup>(canvas.gameObject);
            group.alpha = 1f; // Authoring preview; play mode Awake hides it until a message arrives.
            group.interactable = false;
            group.blocksRaycasts = false;

            var shadow = ImageObject(root, "ToastShadow", Sprite(FlatFolder + "flat_navy.png"),
                new Color(0.02f, 0.06f, 0.07f, 0.32f));
            Stretch(shadow, 3f, -4f, -3f, 4f);
            var card = ImageObject(root, "ToastCard", Sprite(FlatFolder + "flat_canvas.png"), Cream);
            Stretch(card, 0f, 0f, 0f, 0f);
            var outline = GetOrAdd<Outline>(card.gameObject);
            outline.effectColor = Gold;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var plate = ImageObject(card, "IconPlate", Sprite(FlatFolder + "flat_green.png"), new Color(0.91f, 0.96f, 0.78f, 1f));
            Anchor(plate, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(52f, 52f));
            var icon = ImageObject(plate, "SeverityIcon", Sprite(IconFolder + "toast_success.png"), Color.white);
            Stretch(icon, 6f, 6f, 6f, 6f);
            icon.GetComponent<Image>().preserveAspect = true;

            var title = TextObject(card, "TitleLabel", "Helped the farmer", 17f, Navy, FontStyles.Bold, TextAlignmentOptions.BottomLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(78f, -1f);
            title.rectTransform.offsetMax = new Vector2(-14f, -7f);
            var detail = TextObject(card, "DetailLabel", "Friendship +1", 13f, Green, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            detail.rectTransform.anchorMin = Vector2.zero;
            detail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            detail.rectTransform.offsetMin = new Vector2(78f, 7f);
            detail.rectTransform.offsetMax = new Vector2(-14f, 1f);

            var pointer = ImageObject(root, "Pointer", Sprite(FlatFolder + "flat_canvas.png"), Cream);
            pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, 0f);
            pointer.pivot = new Vector2(0.5f, 0.5f);
            pointer.anchoredPosition = new Vector2(0f, -4f);
            pointer.sizeDelta = new Vector2(14f, 14f);
            pointer.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var serialized = new SerializedObject(toast);
            Set(serialized, "cardBackground", card.GetComponent<Image>());
            Set(serialized, "accent", null);
            Set(serialized, "severityIcon", icon.GetComponent<Image>());
            Set(serialized, "titleLabel", title);
            Set(serialized, "detailLabel", detail);
            Set(serialized, "infoIcon", Sprite(IconFolder + "toast_info.png"));
            Set(serialized, "positiveIcon", Sprite(IconFolder + "toast_success.png"));
            Set(serialized, "warningIcon", Sprite(IconFolder + "toast_warning.png"));
            Set(serialized, "negativeIcon", Sprite(IconFolder + "toast_negative.png"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(toast);
        }

        private static RectTransform RectObject(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform ImageObject(Transform parent, string name, Sprite sprite, Color color)
        {
            var rect = RectObject(parent, name);
            var image = goAdd<Image>(rect.gameObject);
            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static TextMeshProUGUI TextObject(Transform parent, string name, string value, float size,
            Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var rect = RectObject(parent, name);
            var text = goAdd<TextMeshProUGUI>(rect.gameObject);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                Tormia.Ontology.Editor.OntologyJuaTypographyMigration
                    .FontAssetPath);
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.GetComponent<T>() ?? Undo.AddComponent<T>(target);

        private static T goAdd<T>(GameObject target) where T : Component =>
            Undo.AddComponent<T>(target);

        private static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static void Set(SerializedObject serialized, string property, Object value)
        {
            var found = serialized.FindProperty(property);
            if (found != null) found.objectReferenceValue = value;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void CopyRect(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
        }
    }
}
#endif
