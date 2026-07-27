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
    /// Authors the unified Object/NPC/Monster placement panel as ordinary,
    /// editable uGUI hierarchy objects. Runtime code only binds catalog data
    /// and selection state to these authored controls.
    /// </summary>
    public static class OntologyTovWorldPlacementAuthoring
    {
        private const string PrefabPath = "Assets/Data/Ontology/UI/ObjectPlacementHUD.prefab";
        private const string ArtFolder = "Assets/Art/UI/WorldPlacement/";
        private const string FlatFolder = "Assets/Art/UI/CharacterCreationFlat/";

        private static readonly Color32 Navy = new(22, 53, 72, 255);
        private static readonly Color32 Cream = new(255, 248, 235, 255);
        private static readonly Color32 CreamDark = new(244, 232, 208, 255);
        private static readonly Color32 Gold = new(220, 165, 39, 255);
        private static readonly Color32 Green = new(78, 139, 28, 255);
        private static readonly Color32 Muted = new(116, 103, 83, 255);

        [MenuItem("Tormia/UI/Apply Editable TOV World Placement Design")]
        public static void Apply()
        {
            PrepareImporters();
            AssetDatabase.Refresh();
            var assets = LoadAssets();
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Author(root, assets);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var sceneRoot = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(value =>
                    value != null &&
                    value.gameObject.scene.IsValid() &&
                    value.name == "ObjectPlacementHUD")?.gameObject;
            if (sceneRoot != null)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(sceneRoot))
                    PrefabUtility.RevertPrefabInstance(sceneRoot, InteractionMode.AutomatedAction);
                else
                    Author(sceneRoot, assets);
                sceneRoot.SetActive(true);
                EditorUtility.SetDirty(sceneRoot);
                EditorSceneManager.MarkSceneDirty(sceneRoot.scene);
                EditorSceneManager.SaveScene(sceneRoot.scene);
                Selection.activeGameObject = sceneRoot;
            }

            AuthorRuntimeToggle(assets);

            AssetDatabase.SaveAssets();
            Debug.Log("[TOV UI] Authored editable unified World Placement panel and separate state sprites.");
        }

        private static void AuthorRuntimeToggle(Assets assets)
        {
            var panel = Resources.FindObjectsOfTypeAll<OntologyObjectPlacementPanel>()
                .FirstOrDefault(value =>
                    value != null && value.gameObject.scene.IsValid());
            if (panel == null) return;

            var parent = panel.transform;
            var toggle = parent.Find("WorldPlacementToggleButton") as RectTransform;
            if (toggle == null)
            {
                var toggleObject = new GameObject(
                    "WorldPlacementToggleButton",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                Undo.RegisterCreatedObjectUndo(toggleObject, "Create world placement toggle");
                toggle = toggleObject.GetComponent<RectTransform>();
                toggle.SetParent(parent, false);
            }

            PlaceTopRight(toggle, new Vector2(-42f, -164f), new Vector2(106f, 106f));
            var image = GetOrAdd<Image>(toggle.gameObject);
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/UI/Textures/TOV/world_placement_toggle_v1.png");
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(toggle.gameObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            var serialized = new SerializedObject(panel);
            serialized.FindProperty("openButton").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            EditorSceneManager.SaveScene(panel.gameObject.scene);
        }

        private static void Author(GameObject rootObject, Assets assets)
        {
            var root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(1740f, 1000f);
            root.localScale = Vector3.one;
            ClearChildren(root);

            var rootImage = GetOrAdd<Image>(rootObject);
            rootImage.sprite = assets.canvas;
            rootImage.type = Image.Type.Sliced;
            rootImage.color = Gold;
            rootImage.raycastTarget = true;
            var shadow = GetOrAdd<Shadow>(rootObject);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
            shadow.effectDistance = new Vector2(0f, -10f);

            var surface = ImageObject(root, "PanelSurface", assets.canvas, Cream);
            Stretch(surface, 9f, 9f, 9f, 9f);

            var title = TextObject(root, "Title", "WORLD PLACEMENT", 48f, Navy, FontStyles.Bold,
                TextAlignmentOptions.Center, assets.boldFont);
            PlaceTop(title.rectTransform, new Vector2(0f, -31f), new Vector2(850f, 66f));
            var divider = ImageObject(root, "TitleDivider", null, new Color(Gold.r / 255f, Gold.g / 255f, Gold.b / 255f, 0.75f));
            PlaceTop(divider, new Vector2(0f, -111f), new Vector2(1550f, 2f));

            CreateCloseButton(root, assets);
            CreateModeTabs(root, assets);
            CreateCategories(root, assets);
            CreatePreview(root, assets);
            CreateOntologyPanel(root, assets);
            CreatePlaceButton(root, assets);

            var footerLine = ImageObject(root, "FooterDivider", null, new Color(Gold.r / 255f, Gold.g / 255f, Gold.b / 255f, 0.35f));
            PlaceBottom(footerLine, new Vector2(0f, 63f), new Vector2(1550f, 1f));
            var footer = TextObject(root, "FooterHints", "Q / E  ROTATE     •     ESC  CANCEL",
                17f, Muted, FontStyles.Bold, TextAlignmentOptions.Center, assets.regularFont);
            PlaceBottom(footer.rectTransform, new Vector2(0f, 20f), new Vector2(720f, 34f));
        }

        private static void CreateModeTabs(RectTransform root, Assets assets)
        {
            var tabs = RectObject(root, "ModeTabs");
            PlaceTop(tabs, new Vector2(0f, -132f), new Vector2(1550f, 82f));
            var layout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            CreateModeTab(tabs, "ObjectTab", "OBJECT", ModeIcon.Object, true, assets);
            CreateModeTab(tabs, "NpcTab", "NPC", ModeIcon.Npc, false, assets);
            CreateModeTab(tabs, "MonsterTab", "MONSTER", ModeIcon.Monster, false, assets);
        }

        private static void CreateModeTab(
            RectTransform parent,
            string name,
            string labelValue,
            ModeIcon iconKind,
            bool selected,
            Assets assets)
        {
            var button = ButtonObject(parent, name);
            var selectedState = ImageObject(button.transform, "SelectedState", assets.tabSelected, Color.white);
            Stretch(selectedState, 0f, 0f, 0f, 0f);
            selectedState.gameObject.SetActive(selected);
            var unselectedState = ImageObject(button.transform, "UnselectedState", assets.tabUnselected, Color.white);
            Stretch(unselectedState, 0f, 0f, 0f, 0f);
            unselectedState.gameObject.SetActive(!selected);

            var iconRoot = RectObject(button.transform, "Icon");
            iconRoot.anchorMin = iconRoot.anchorMax = new Vector2(0.5f, 0.5f);
            iconRoot.pivot = new Vector2(0.5f, 0.5f);
            iconRoot.anchoredPosition = new Vector2(-88f, 0f);
            iconRoot.sizeDelta = new Vector2(54f, 54f);
            var iconSprite = iconKind switch
            {
                ModeIcon.Object => assets.objectIcon,
                ModeIcon.Npc => assets.npcIcon,
                _ => assets.monsterIcon
            };
            var icon = ImageObject(iconRoot, iconKind + "Sprite", iconSprite, Color.white);
            Stretch(icon, 1f, 1f, 1f, 1f);
            icon.GetComponent<Image>().preserveAspect = true;

            var label = TextObject(button.transform, "Label", labelValue, 30f,
                selected ? Color.white : Navy, FontStyles.Bold, TextAlignmentOptions.Center, assets.boldFont);
            Stretch(label.rectTransform, 85f, 20f, 18f, 20f);
        }

        private static void CreateCategories(RectTransform root, Assets assets)
        {
            var panel = ImageObject(root, "Categories", assets.surface, new Color(1f, 1f, 1f, 0.38f));
            PlaceTopLeft(panel, new Vector2(45f, -238f), new Vector2(340f, 610f));
            var title = TextObject(panel, "Title", "CATEGORIES", 27f, Navy, FontStyles.Bold,
                TextAlignmentOptions.Center, assets.boldFont);
            PlaceTop(title.rectTransform, new Vector2(0f, -15f), new Vector2(280f, 42f));
            var titleLine = ImageObject(panel, "TitleDivider", null, new Color(Gold.r / 255f, Gold.g / 255f, Gold.b / 255f, 0.7f));
            PlaceTop(titleLine, new Vector2(0f, -63f), new Vector2(250f, 2f));

            var scroll = CreateScroll(panel, "CategoryScroll", new Vector2(18f, -80f), new Vector2(304f, 510f), assets);
            CreateCategoryButton(scroll.content, "Category_Nature", "Nature", true, assets);
            CreateCategoryButton(scroll.content, "Category_Buildings", "Buildings", false, assets);
            CreateCategoryButton(scroll.content, "Category_Furniture", "Furniture", false, assets);
            CreateCategoryButton(scroll.content, "Category_Decorations", "Decorations", false, assets);
            CreateCategoryButton(scroll.content, "Category_Utility", "Utility", false, assets);
            var template = CreateCategoryButton(scroll.content, "CategoryButtonTemplate", "Category", false, assets);
            template.gameObject.SetActive(false);
        }

        private static Button CreateCategoryButton(
            RectTransform parent,
            string name,
            string labelValue,
            bool selected,
            Assets assets)
        {
            var button = ButtonObject(parent, name);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 84f;
            layout.preferredHeight = 84f;
            layout.flexibleHeight = 0f;
            var selectedState = ImageObject(button.transform, "SelectedState", assets.categorySelected, Color.white);
            Stretch(selectedState, 0f, 0f, 0f, 0f);
            selectedState.gameObject.SetActive(selected);
            var unselectedState = ImageObject(button.transform, "UnselectedState", assets.categoryUnselected, Color.white);
            Stretch(unselectedState, 0f, 0f, 0f, 0f);
            unselectedState.gameObject.SetActive(!selected);
            var label = TextObject(button.transform, "Text", labelValue, 23f,
                selected ? Color.white : Navy, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, assets.boldFont);
            Stretch(label.rectTransform, 32f, 18f, 20f, 18f);
            return button;
        }

        private static void CreatePreview(RectTransform root, Assets assets)
        {
            var frame = ImageObject(root, "PreviewFrame", assets.surface, new Color(1f, 1f, 1f, 0.32f));
            PlaceTopLeft(frame, new Vector2(410f, -238f), new Vector2(800f, 610f));
            var renderer = frame.gameObject.AddComponent<OntologyPlaceablePreviewRenderer>();
            var previewRect = RectObject(frame, "Preview");
            var preview = previewRect.gameObject.AddComponent<RawImage>();
            preview.color = Color.white;
            preview.raycastTarget = false;
            Anchor(previewRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(500f, 500f));
            var serialized = new SerializedObject(renderer);
            serialized.FindProperty("target").objectReferenceValue = preview;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var previous = ButtonObject(frame, "Previous");
            Anchor(previous.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(35f, 10f), new Vector2(76f, 76f));
            StyleRoundArrow(previous, false, assets);
            var next = ButtonObject(frame, "Next");
            Anchor(next.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-35f, 10f), new Vector2(76f, 76f));
            StyleRoundArrow(next, true, assets);
            var page = TextObject(frame, "Page", "2 / 8", 25f, Navy, FontStyles.Bold,
                TextAlignmentOptions.Center, assets.boldFont);
            PlaceBottom(page.rectTransform, new Vector2(0f, 42f), new Vector2(180f, 35f));
            var rotate = TextObject(frame, "RotateHint", "Q / E  ROTATE", 17f, Muted, FontStyles.Bold,
                TextAlignmentOptions.Center, assets.regularFont);
            PlaceBottom(rotate.rectTransform, new Vector2(0f, 13f), new Vector2(250f, 28f));
        }

        private static void StyleRoundArrow(Button button, bool pointsRight, Assets assets)
        {
            var image = button.GetComponent<Image>();
            image.sprite = assets.categoryUnselected;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            for (var index = 0; index < 2; index++)
            {
                var upper = index == 0;
                var bar = ImageObject(
                    button.transform,
                    upper ? "ArrowUpper" : "ArrowLower",
                    null,
                    Navy);
                var x = pointsRight ? -2f : 2f;
                var y = upper ? 8f : -8f;
                Anchor(
                    bar,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(x, y),
                    new Vector2(28f, 7f));
                var angle = pointsRight
                    ? (upper ? -45f : 45f)
                    : (upper ? 45f : -45f);
                bar.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private static void CreateOntologyPanel(RectTransform root, Assets assets)
        {
            var panel = ImageObject(root, "OntologyPanel", assets.surface, new Color(1f, 1f, 1f, 0.38f));
            PlaceTopLeft(panel, new Vector2(1235f, -238f), new Vector2(460f, 610f));
            var name = TextObject(panel, "Name", "OLD OAK TREE", 31f, Navy, FontStyles.Bold,
                TextAlignmentOptions.Left, assets.boldFont);
            PlaceTopLeft(name.rectTransform, new Vector2(28f, -22f), new Vector2(404f, 45f));
            var detail = TextObject(panel, "Detail", "A sturdy old tree that brings shade and life to your world.",
                19f, Navy, FontStyles.Normal, TextAlignmentOptions.TopLeft, assets.regularFont);
            PlaceTopLeft(detail.rectTransform, new Vector2(28f, -76f), new Vector2(404f, 72f));
            detail.enableWordWrapping = true;

            var heading = TextObject(panel, "OntologyHeading", "ONTOLOGY DATA", 23f, Navy, FontStyles.Bold,
                TextAlignmentOptions.Center, assets.boldFont);
            PlaceTop(heading.rectTransform, new Vector2(0f, -160f), new Vector2(360f, 35f));
            var line = ImageObject(panel, "OntologyDivider", null, new Color(Gold.r / 255f, Gold.g / 255f, Gold.b / 255f, 0.7f));
            PlaceTop(line, new Vector2(0f, -202f), new Vector2(390f, 2f));

            var scroll = CreateScroll(panel, "OntologyScroll", new Vector2(22f, -220f), new Vector2(416f, 366f), assets);
            CreateOntologyRow(scroll.content, "DefinitionIdRow", "DEFINITION ID", "PolyStyle_Tree_01", assets);
            CreateOntologyRow(scroll.content, "ConceptsRow", "CONCEPTS", "Plant  ·  Resource", assets);
            CreateOntologyRow(scroll.content, "PlacementSurfaceRow", "PLACEMENT SURFACE", "Any Collider", assets);
            CreateOntologyRow(scroll.content, "RuleBlocksRow", "RULE BLOCKS", "None", assets);
        }

        private static void CreateOntologyRow(
            RectTransform parent,
            string name,
            string labelValue,
            string valueText,
            Assets assets)
        {
            var row = RectObject(parent, name);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 92f;
            layout.preferredHeight = 92f;
            layout.flexibleHeight = 0f;
            var label = TextObject(row, "Label", labelValue, 15f, Muted, FontStyles.Bold,
                TextAlignmentOptions.TopLeft, assets.boldFont);
            label.rectTransform.anchorMin = new Vector2(0f, 0.52f);
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.rectTransform.offsetMax = new Vector2(-10f, -6f);
            var value = TextObject(row, "Value", valueText, 20f, Navy, FontStyles.Normal,
                TextAlignmentOptions.TopLeft, assets.regularFont);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = new Vector2(1f, 0.57f);
            value.rectTransform.offsetMin = new Vector2(10f, 4f);
            value.rectTransform.offsetMax = new Vector2(-10f, 0f);
            value.enableWordWrapping = true;
            var divider = ImageObject(row, "Divider", null, new Color(Gold.r / 255f, Gold.g / 255f, Gold.b / 255f, 0.28f));
            divider.anchorMin = Vector2.zero;
            divider.anchorMax = new Vector2(1f, 0f);
            divider.pivot = new Vector2(0.5f, 0f);
            divider.anchoredPosition = Vector2.zero;
            divider.sizeDelta = new Vector2(-18f, 1f);
        }

        private static void CreatePlaceButton(RectTransform root, Assets assets)
        {
            var button = ButtonObject(root, "Select");
            var rect = button.GetComponent<RectTransform>();
            PlaceBottomRight(rect, new Vector2(-45f, 78f), new Vector2(330f, 64f));
            var image = button.GetComponent<Image>();
            image.sprite = assets.categorySelected;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var label = TextObject(button.transform, "Text", "PLACE", 26f, Color.white,
                FontStyles.Bold, TextAlignmentOptions.Center, assets.boldFont);
            Stretch(label.rectTransform, 4f, 4f, 4f, 4f);
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private static void CreateCloseButton(RectTransform root, Assets assets)
        {
            var button = ButtonObject(root, "CloseIconButton");
            var rect = button.GetComponent<RectTransform>();
            PlaceTopRight(rect, new Vector2(-34f, -28f), new Vector2(62f, 62f));
            var image = button.GetComponent<Image>();
            image.sprite = assets.closeButton;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            for (var index = 0; index < 2; index++)
            {
                var bar = ImageObject(rect, index == 0 ? "CrossA" : "CrossB", null, Color.white);
                Anchor(bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 5f));
                bar.localRotation = Quaternion.Euler(0f, 0f, index == 0 ? 45f : -45f);
            }
        }

        private static ScrollParts CreateScroll(
            RectTransform parent,
            string name,
            Vector2 topLeft,
            Vector2 size,
            Assets assets)
        {
            var root = ImageObject(parent, name, assets.surface, new Color(1f, 1f, 1f, 0.18f));
            PlaceTopLeft(root, topLeft, size);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            var viewport = ImageObject(root, "Viewport", null, new Color(1f, 1f, 1f, 0.001f));
            Stretch(viewport, 8f, 8f, 8f, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = RectObject(viewport, name == "CategoryScroll" ? "CategoryList" : "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = name == "CategoryScroll" ? 10f : 3f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = null;
            return new ScrollParts { root = root, content = content };
        }

        private static void PrepareImporters()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = path.IndexOf("tab_icon_", StringComparison.OrdinalIgnoreCase) >= 0
                    ? 512
                    : 256;
                if (path.IndexOf("tab_icon_", StringComparison.OrdinalIgnoreCase) < 0)
                    importer.spriteBorder = new Vector4(24f, 24f, 24f, 24f);
                importer.SaveAndReimport();
            }
        }

        private static Assets LoadAssets()
        {
            var result = new Assets
            {
                canvas = Sprite(FlatFolder + "flat_canvas.png"),
                surface = Sprite(FlatFolder + "flat_surface.png"),
                circle = Sprite(FlatFolder + "circle_antialias_256.asset"),
                tabSelected = Sprite(ArtFolder + "tab_selected.png"),
                tabUnselected = Sprite(ArtFolder + "tab_unselected.png"),
                categorySelected = Sprite(ArtFolder + "category_selected.png"),
                categoryUnselected = Sprite(ArtFolder + "category_unselected.png"),
                closeButton = Sprite(ArtFolder + "close_button.png"),
                objectIcon = Sprite(ArtFolder + "tab_icon_object.png"),
                npcIcon = Sprite(ArtFolder + "tab_icon_npc.png"),
                monsterIcon = Sprite(ArtFolder + "tab_icon_monster.png"),
                boldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/InterBold.asset"),
                regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/InterRegular.asset")
            };
            if (result.canvas == null || result.tabSelected == null || result.objectIcon == null ||
                result.npcIcon == null || result.monsterIcon == null ||
                result.boldFont == null || result.regularFont == null)
                throw new InvalidOperationException("World Placement art assets or Inter fonts are missing.");
            return result;
        }

        private static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static RectTransform RectObject(Transform parent, string name)
        {
            var value = new GameObject(name, typeof(RectTransform));
            var rect = value.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform ImageObject(Transform parent, string name, Sprite sprite, Color color)
        {
            var rect = RectObject(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static Button ButtonObject(Transform parent, string name)
        {
            var rect = RectObject(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            return button;
        }

        private static TextMeshProUGUI TextObject(
            Transform parent,
            string name,
            string value,
            float size,
            Color color,
            FontStyles style,
            TextAlignmentOptions alignment,
            TMP_FontAsset font)
        {
            var rect = RectObject(parent, name);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.GetComponent<T>() ?? target.AddComponent<T>();

        private static void ClearChildren(RectTransform parent)
        {
            for (var index = parent.childCount - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(index).gameObject);
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            rect.localScale = Vector3.one;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void PlaceTop(RectTransform rect, Vector2 position, Vector2 size) =>
            Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, size);

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size) =>
            Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);

        private static void PlaceTopRight(RectTransform rect, Vector2 position, Vector2 size) =>
            Anchor(rect, Vector2.one, Vector2.one, position, size);

        private static void PlaceBottom(RectTransform rect, Vector2 position, Vector2 size) =>
            Anchor(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), position, size);

        private static void PlaceBottomRight(RectTransform rect, Vector2 position, Vector2 size) =>
            Anchor(rect, new Vector2(1f, 0f), new Vector2(1f, 0f), position, size);

        private enum ModeIcon
        {
            Object,
            Npc,
            Monster
        }

        private sealed class Assets
        {
            public Sprite canvas;
            public Sprite surface;
            public Sprite circle;
            public Sprite tabSelected;
            public Sprite tabUnselected;
            public Sprite categorySelected;
            public Sprite categoryUnselected;
            public Sprite closeButton;
            public Sprite objectIcon;
            public Sprite npcIcon;
            public Sprite monsterIcon;
            public TMP_FontAsset boldFont;
            public TMP_FontAsset regularFont;
        }

        private sealed class ScrollParts
        {
            public RectTransform root;
            public RectTransform content;
        }
    }
}
#endif
