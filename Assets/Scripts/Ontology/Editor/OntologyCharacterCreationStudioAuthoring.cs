#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    /// <summary>
    /// Idempotent scene authoring helper for the hierarchy-owned account
    /// character creator. Runtime code never builds or positions this UI.
    /// </summary>
    public static class OntologyCharacterCreationStudioAuthoring
    {
        private const string FarmSprites =
            "Assets/maanetorn/Farm Game UI - Simple 2D UI/Sprites/256x256/";
        private const string CategoryIconFolder = "Assets/Art/UI/CharacterCategoryIcons/";
        private const string CategoryIconSetPath =
            "Assets/Data/Ontology/UI/CharacterCategoryIconSet.asset";
        private const string FlatUiFolder = "Assets/Art/UI/CharacterCreationFlat/";
        private const string CircleSpriteAssetPath =
            "Assets/Art/UI/CharacterCreationFlat/circle_antialias_256.asset";

        [MenuItem("Tormia/Ontology/Author Character Creation Studio")]
        public static void Author()
        {
            var creation = GameObject.Find("AccountCharacterCreationPanel");
            var appearance = GameObject.Find("AccountCharacterAppearancePanel");
            if (creation == null || appearance == null)
            {
                Debug.LogError("Character creation roots are missing from the active scene.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(creation, "Author character creation overlay");
            Undo.RegisterFullObjectHierarchyUndo(appearance, "Author appearance studio");

            EnsureFlatUiSprites();
            AuthorAppearanceStudio(appearance);
            AuthorCreationOverlay(creation);
            AuthorCategoryIcons(appearance);

            appearance.transform.SetSiblingIndex(creation.transform.GetSiblingIndex());
            creation.transform.SetAsLastSibling();
            EditorUtility.SetDirty(appearance);
            EditorUtility.SetDirty(creation);
            EditorSceneManager.MarkSceneDirty(creation.scene);
            EditorSceneManager.SaveScene(creation.scene);
            Debug.Log("Authored the hierarchy-editable flat character creation studio.");
        }

        private static void AuthorAppearanceStudio(GameObject root)
        {
            var canvas = FlatSprite("flat_canvas.png");
            var surface = FlatSprite("flat_surface.png");
            var inset = FlatSprite("flat_inset.png");
            var navy = FlatSprite("flat_navy.png");
            var olive = FlatSprite("flat_olive.png");
            var taupe = FlatSprite("flat_taupe.png");

            Place((RectTransform)root.transform, Center, Center, Center, Vector2.zero,
                new Vector2(1880f, 1040f));
            StyleImage(root, canvas, Color.white, true);
            SetSoftShadow(root, true, new Vector2(0f, -3f), 0.09f);

            var serialized = new SerializedObject(
                root.GetComponent<OntologyCharacterCustomizationPanel>());
            serialized.FindProperty("equipOnPartSelection").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var header = Rect(root.transform, "Header");
            Place(header, TopLeft, TopRight, TopCenter, new Vector2(0f, -16f),
                new Vector2(-32f, 96f));
            // The account creation overlay owns the single visible header. Keep
            // this hierarchy node for the standalone picker without drawing a
            // second stacked panel behind the creation title.
            StyleImage(header.gameObject, null, Color.clear, false);
            var headerTitle = header.Find("TitleText").GetComponent<TMP_Text>();
            Place(headerTitle.rectTransform, Vector2.zero, Vector2.one, Center,
                Vector2.zero, new Vector2(-120f, -18f));
            headerTitle.fontSize = 28f;
            headerTitle.color = Navy;
            headerTitle.alignment = TextAlignmentOptions.Center;
            Place((RectTransform)header.Find("CloseButton"), TopRight, TopRight,
                TopRight, new Vector2(-20f, -20f), new Vector2(42f, 42f));

            var category = Rect(root.transform, "CategoryArea");
            Place(category, TopLeft, TopLeft, TopLeft, new Vector2(28f, -124f),
                new Vector2(190f, 716f));
            StyleImage(category.gameObject, surface, Color.white, false);
            SectionLabel(category, "CategorySectionLabel", "STYLE");
            Place((RectTransform)category.Find("CategoryScrollView"), Vector2.zero,
                Vector2.one, Center, new Vector2(0f, -20f), new Vector2(-20f, -68f));
            StyleScrollArea(category.Find("CategoryScrollView"));
            var categoryContent = category.Find(
                "CategoryScrollView/CategoryViewport/CategoryContent");
            var categoryLayout = categoryContent == null
                ? null
                : categoryContent.GetComponent<VerticalLayoutGroup>();
            if (categoryLayout != null)
                categoryLayout.padding = new RectOffset(
                    categoryLayout.padding.left, categoryLayout.padding.right,
                    139, categoryLayout.padding.bottom);

            var gridArea = Rect(root.transform, "PartGridArea");
            Place(gridArea, TopLeft, TopLeft, TopLeft, new Vector2(234f, -124f),
                new Vector2(390f, 716f));
            StyleImage(gridArea.gameObject, surface, Color.white, false);
            SectionLabel(gridArea, "PartSectionLabel", "PARTS");
            Place((RectTransform)gridArea.Find("ScrollView"), Vector2.zero, Vector2.one,
                Center, new Vector2(0f, -20f), new Vector2(-20f, -68f));
            StyleScrollArea(gridArea.Find("ScrollView"));
            var gridContent = (RectTransform)gridArea.Find("ScrollView/Viewport/PartGridContent");
            gridContent.sizeDelta = new Vector2(300f, 0f);
            var grid = gridContent.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(144f, 144f);
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(3, 3, 3, 3);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var detail = Rect(root.transform, "DetailArea");
            Place(detail, TopLeft, TopLeft, TopLeft, new Vector2(1440f, -124f),
                new Vector2(412f, 716f));
            StyleImage(detail.gameObject, surface, Color.white, false);
            SectionLabel(detail, "DetailSectionLabel", "SELECTED LOOK");
            Place((RectTransform)detail.Find("SelectedIcon"), TopCenter, TopCenter,
                TopCenter, new Vector2(0f, -72f), new Vector2(150f, 150f));
            var emptySelectionIcon = Rect(detail, "EmptySelectionIcon");
            Place(emptySelectionIcon, TopCenter, TopCenter, TopCenter,
                new Vector2(0f, -96f), new Vector2(92f, 92f));
            var emptyImage = emptySelectionIcon.GetComponent<Image>();
            if (emptyImage == null)
                emptyImage = Undo.AddComponent<Image>(emptySelectionIcon.gameObject);
            emptyImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                CategoryIconFolder + "Category_" +
                OntologyCharacterCustomizationUiConfig.SlotUpperBody + ".png");
            emptyImage.preserveAspect = true;
            emptyImage.raycastTarget = false;
            emptyImage.color = new Color(0.38f, 0.34f, 0.28f, 0.24f);
            ConfigureDetailText(detail, "SelectedTitle", -252f, 42f, 22f, Navy,
                TextAlignmentOptions.Center);
            ConfigureDetailText(detail, "SelectedDescription", -306f, 92f, 15f,
                MutedNavy, TextAlignmentOptions.TopLeft);
            ConfigureDetailText(detail, "FactPreview", -412f, 110f, 13f,
                FactGreen, TextAlignmentOptions.TopLeft);
            var equip = (RectTransform)detail.Find("EquipButton");
            Place(equip, BottomLeft, BottomLeft, BottomLeft, new Vector2(24f, 26f),
                new Vector2(172f, 54f));
            StyleButton(equip.GetComponent<Button>(), olive, Navy, 16f);
            var unequip = (RectTransform)detail.Find("UnequipButton");
            Place(unequip, BottomRight, BottomRight, BottomRight, new Vector2(-24f, 26f),
                new Vector2(172f, 54f));
            StyleButton(unequip.GetComponent<Button>(), taupe, Navy, 16f);

            var preview = Rect(root.transform, "CharacterPreviewFrame");
            Place(preview, Center, Center, Center, new Vector2(80f, 54f),
                new Vector2(780f, 582f));
            StyleImage(preview.gameObject, inset, Color.white, false);
            SetSoftShadow(preview.gameObject, true, new Vector2(0f, -2f), 0.07f);
            AuthorLivePreview(root, preview);
            var previewLabel = root.transform.Find("PreviewLabel").GetComponent<TMP_Text>();
            Place(previewLabel.rectTransform, TopCenter, TopCenter, TopCenter,
                new Vector2(80f, -126f), new Vector2(720f, 40f));
            previewLabel.text = "LIVE CHARACTER PREVIEW";
            previewLabel.fontSize = 18f;
            previewLabel.color = Navy;
            previewLabel.alignment = TextAlignmentOptions.Center;

            var status = root.transform.Find("Status").GetComponent<TMP_Text>();
            Place(status.rectTransform, BottomCenter, BottomCenter, BottomCenter,
                new Vector2(80f, 104f), new Vector2(760f, 28f));
            status.fontSize = 14f;
            status.alignment = TextAlignmentOptions.Center;
            status.color = MutedNavy;

            AuthorPartCardTemplate(root, navy);

            root.transform.Find("Templates").gameObject.SetActive(false);
            var editorPreview = root.transform.Find("EditorPreviewContent");
            editorPreview.gameObject.SetActive(true);
            PositionPreviewCategories(editorPreview, navy, taupe);
            PositionPreviewParts(editorPreview, navy, surface);
            AuthorMannequin(editorPreview, inset, surface, navy);
        }

        private static void AuthorCreationOverlay(GameObject root)
        {
            var surface = FlatSprite("flat_surface.png");
            var inputSprite = FlatSprite("flat_input.png");
            var green = FlatSprite("flat_green.png");
            var coral = FlatSprite("flat_coral.png");

            Place((RectTransform)root.transform, Center, Center, Center, Vector2.zero,
                new Vector2(1920f, 1080f));
            StyleImage(root, null, Color.clear, false);

            var heading = Rect(root.transform, "CharacterCreationHeading");
            Place(heading, TopLeft, TopRight, TopCenter, new Vector2(0f, -20f),
                new Vector2(-56f, 94f));
            StyleImage(heading.gameObject, surface, Color.white, false);
            var oldHeading = root.transform.Find("Text (TMP)");
            if (oldHeading != null) oldHeading.SetParent(heading, false);
            var headingText = heading.GetComponentInChildren<TMP_Text>(true);
            Place(headingText.rectTransform, Vector2.zero, Vector2.one, Center,
                new Vector2(0f, -4f), new Vector2(-120f, -18f));
            headingText.fontSize = 30f;
            headingText.color = Navy;
            headingText.alignment = TextAlignmentOptions.Center;
            var step = Text(heading, "StepLabel", "FIRST ADVENTURE · CHARACTER SETUP",
                12f, MutedNavy, TextAlignmentOptions.Center, headingText.font);
            Place(step.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, -6f),
                new Vector2(-120f, 18f));
            step.gameObject.SetActive(false);

            var identity = Rect(root.transform, "CharacterIdentityCard");
            Place(identity, BottomCenter, BottomCenter, BottomCenter, new Vector2(80f, 126f),
                new Vector2(780f, 104f));
            StyleImage(identity.gameObject, surface, Color.white, false);
            var input = root.transform.Find("CharacterNameInput") as RectTransform;
            if (input != null) input.SetParent(identity, false);
            input = identity.Find("CharacterNameInput") as RectTransform;
            Place(input, BottomCenter, BottomCenter, BottomCenter, new Vector2(0f, 14f),
                new Vector2(720f, 48f));
            StyleInput(input, inputSprite);
            var nameLabel = Text(identity, "CharacterNameLabel", "CHARACTER NAME", 15f,
                Navy, TextAlignmentOptions.Left, headingText.font);
            Place(nameLabel.rectTransform, TopCenter, TopCenter, TopCenter,
                new Vector2(0f, -10f), new Vector2(720f, 26f));

            var guidance = Rect(root.transform, "AppearanceGuidance");
            Place(guidance, TopLeft, TopLeft, TopLeft, new Vector2(264f, -98f),
                new Vector2(350f, 30f));
            StyleImage(guidance.gameObject, null, Color.clear, false);
            var hint = Text(guidance, "AppearanceHintLabel",
                "Select a part to try it on instantly.", 12f, MutedNavy,
                TextAlignmentOptions.Left, headingText.font);
            Place(hint.rectTransform, Vector2.zero, Vector2.one, Center, Vector2.zero,
                Vector2.zero);
            guidance.gameObject.SetActive(false);

            var footer = Rect(root.transform, "CharacterCreationFooter");
            Place(footer, BottomLeft, BottomRight, BottomCenter, new Vector2(0f, 20f),
                new Vector2(-56f, 78f));
            StyleImage(footer.gameObject, surface, Color.white, false);
            ReparentButton(root.transform, footer, "BackButton", MiddleLeft,
                new Vector2(22f, 0f), new Vector2(220f, 52f), coral);
            ReparentButton(root.transform, footer, "ContinueToWorldButton", MiddleRight,
                new Vector2(-22f, 0f), new Vector2(260f, 52f), green);
            StyleButton(footer.Find("BackButton").GetComponent<Button>(), coral, Color.white, 22f);
            StyleButton(footer.Find("ContinueToWorldButton").GetComponent<Button>(), green,
                Color.white, 22f);

            var customize = root.transform.Find("CustomizeAppearanceButton");
            if (customize != null) customize.gameObject.SetActive(false);
        }

        private static void PositionPreviewCategories(Transform parent, Sprite active, Sprite idle)
        {
            var names = new[] { "PreviewCategory_Hair", "PreviewCategory_Top", "PreviewCategory_Bottom" };
            for (var i = 0; i < names.Length; i++)
            {
                var item = parent.Find(names[i]) as RectTransform;
                if (item == null) continue;
                Place(item, Center, Center, Center, new Vector2(-817f, 242f - i * 102f),
                    new Vector2(150f, 96f));
                var buttonImage = item.GetComponent<Image>();
                if (buttonImage == null) buttonImage = Undo.AddComponent<Image>(item.gameObject);
                buttonImage.sprite = null;
                buttonImage.color = Color.clear;
                buttonImage.raycastTarget = false;
                var text = item.GetComponentInChildren<TMP_Text>(true);
                if (text != null) text.gameObject.SetActive(false);
                var icon = Rect(item, OntologyCharacterCustomizationUiConfig.IconName);
                Place(icon, Center, Center, Center, Vector2.zero, new Vector2(88f, 88f));
                var iconImage = icon.GetComponent<Image>();
                if (iconImage == null) iconImage = Undo.AddComponent<Image>(icon.gameObject);
                var categoryId = i == 0
                    ? OntologyCharacterCustomizationUiConfig.SlotHair
                    : i == 1
                        ? OntologyCharacterCustomizationUiConfig.SlotUpperBody
                        : OntologyCharacterCustomizationUiConfig.SlotLowerBody;
                iconImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    CategoryIconFolder + "Category_" + categoryId + ".png");
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.color = i == 0 ? Color.white : new Color(1f, 1f, 1f, 0.78f);
                icon.localScale = i == 0 ? Vector3.one : Vector3.one * 0.88f;
            }
        }

        private static void AuthorCategoryIcons(GameObject appearanceRoot)
        {
            var categoryIds = OntologyCharacterCustomizationUiConfig.CategoryOrder;
            foreach (var categoryId in categoryIds)
            {
                var path = CategoryIconFolder + "Category_" + categoryId + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }

            var iconSet = AssetDatabase.LoadAssetAtPath<OntologyCharacterCategoryIconSet>(
                CategoryIconSetPath);
            if (iconSet == null)
            {
                iconSet = ScriptableObject.CreateInstance<OntologyCharacterCategoryIconSet>();
                AssetDatabase.CreateAsset(iconSet, CategoryIconSetPath);
            }

            var iconSetObject = new SerializedObject(iconSet);
            var entries = iconSetObject.FindProperty("entries");
            entries.arraySize = categoryIds.Length;
            for (var i = 0; i < categoryIds.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("categoryId").stringValue = categoryIds[i];
                entry.FindPropertyRelative("icon").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(
                        CategoryIconFolder + "Category_" + categoryIds[i] + ".png");
            }
            iconSetObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(iconSet);

            var panel = appearanceRoot.GetComponent<OntologyCharacterCustomizationPanel>();
            var panelObject = new SerializedObject(panel);
            panelObject.FindProperty("categoryIconSet").objectReferenceValue = iconSet;
            panelObject.ApplyModifiedPropertiesWithoutUndo();

            var template = appearanceRoot.transform.Find(
                OntologyCharacterCustomizationUiConfig.TemplatesName + "/" +
                OntologyCharacterCustomizationUiConfig.CategoryButtonTemplateName) as RectTransform;
            if (template == null) return;
            template.sizeDelta = new Vector2(150f, 96f);
            var templateImage = template.GetComponent<Image>();
            if (templateImage == null) templateImage = Undo.AddComponent<Image>(template.gameObject);
            templateImage.sprite = null;
            templateImage.color = Color.clear;
            templateImage.raycastTarget = true;
            var templateButton = template.GetComponent<Button>();
            if (templateButton != null) templateButton.targetGraphic = templateImage;
            var iconRect = Rect(template, OntologyCharacterCustomizationUiConfig.IconName);
            Place(iconRect, Center, Center, Center, Vector2.zero, new Vector2(88f, 88f));
            var image = iconRect.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(iconRect.gameObject);
            image.sprite = iconSet.GetIcon(OntologyCharacterCustomizationUiConfig.SlotBody);
            image.preserveAspect = true;
            image.raycastTarget = false;
            var label = template.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.gameObject.SetActive(false);
        }

        private static void PositionPreviewParts(Transform parent, Sprite active, Sprite idle)
        {
            var names = new[] { "PreviewPart_Hair", "PreviewPart_Top", "PreviewPart_Bottom" };
            var labels = new[] { "Soft Hair", "Farm Shirt", "Denim Bottom" };
            var categories = new[]
            {
                OntologyCharacterCustomizationUiConfig.SlotHair,
                OntologyCharacterCustomizationUiConfig.SlotUpperBody,
                OntologyCharacterCustomizationUiConfig.SlotLowerBody
            };
            for (var i = 0; i < names.Length; i++)
            {
                var item = parent.Find(names[i]) as RectTransform;
                if (item == null) continue;
                Place(item, Center, Center, Center,
                    new Vector2(-611f + i % 2 * 182f, 242f - i / 2 * 192f),
                    new Vector2(170f, 180f));
                StyleImage(item.gameObject, i == 0 ? active : idle, Color.white, true);
                var icon = (item.Find("ThumbnailMask/" +
                            OntologyCharacterCustomizationUiConfig.IconName)
                            ?? item.Find(OntologyCharacterCustomizationUiConfig.IconName))
                    ?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                        CategoryIconFolder + "Category_" + categories[i] + ".png");
                    icon.enabled = icon.sprite != null;
                    icon.preserveAspect = true;
                    icon.color = Color.white;
                }
                var noIcon = item.Find(OntologyCharacterCustomizationUiConfig.NoIconName);
                if (noIcon != null) noIcon.gameObject.SetActive(false);
                var label = item.Find("Label")?.GetComponent<TMP_Text>();
                if (label != null)
                {
                    label.text = labels[i];
                    label.color = i == 0 ? Color.white : Navy;
                }
                var state = item.Find("State")?.GetComponent<TMP_Text>();
                if (state != null)
                {
                    state.text = i == 0 ? "Equipped" : "Try on";
                    state.color = i == 0 ? SoftGreen : MutedNavy;
                }
            }
        }

        private static void AuthorMannequin(Transform parent, Sprite inner, Sprite panel, Sprite outfit)
        {
            var mannequin = Rect(parent, "PreviewMannequin");
            Place(mannequin, Center, Center, Center, new Vector2(80f, 50f),
                new Vector2(260f, 470f));
            StyleImage(mannequin.gameObject, inner, new Color(1f, 1f, 1f, 0.92f), false);
            var head = Rect(mannequin, "Head");
            Place(head, TopCenter, TopCenter, TopCenter, new Vector2(0f, 40f),
                new Vector2(132f, 132f));
            StyleImage(head.gameObject, panel, new Color(1f, 0.83f, 0.62f, 1f), false);
            var body = Rect(mannequin, "EquippedOutfit");
            Place(body, Center, Center, Center, new Vector2(0f, -10f),
                new Vector2(198f, 240f));
            StyleImage(body.gameObject, outfit, new Color(0.72f, 0.88f, 1f, 1f), false);
            var font = parent.GetComponentInChildren<TMP_Text>(true)?.font;
            var label = Text(mannequin, "EditorPreviewLabel", "YOUR CHARACTER\nLIVE PART PREVIEW",
                17f, Brown, TextAlignmentOptions.Center, font);
            Place(label.rectTransform, BottomCenter, BottomCenter, BottomCenter,
                new Vector2(0f, -34f), new Vector2(320f, 56f));
        }

        private static void AuthorLivePreview(GameObject root, RectTransform frame)
        {
            const string renderTexturePath =
                "Assets/OntologyCharacterCreationPreview.renderTexture";
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(renderTexturePath);
            if (texture == null)
            {
                texture = new RenderTexture(720, 840, 24, RenderTextureFormat.ARGB32)
                {
                    name = "OntologyCharacterCreationPreview",
                    antiAliasing = 4,
                    useMipMap = false
                };
                AssetDatabase.CreateAsset(texture, renderTexturePath);
            }

            var imageRect = Rect(frame, "LiveCharacterPreview");
            Place(imageRect, Vector2.zero, Vector2.one, Center, Vector2.zero,
                new Vector2(-20f, -20f));
            var rawImage = imageRect.GetComponent<RawImage>();
            if (rawImage == null) rawImage = Undo.AddComponent<RawImage>(imageRect.gameObject);
            rawImage.texture = texture;
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;
            imageRect.SetAsFirstSibling();

            var cameraTransform = root.transform.Find("CharacterPreviewCamera");
            if (cameraTransform == null)
            {
                var cameraObject = new GameObject("CharacterPreviewCamera", typeof(Camera));
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create character preview camera");
                cameraTransform = cameraObject.transform;
                cameraTransform.SetParent(root.transform, false);
            }
            var camera = cameraTransform.GetComponent<Camera>();
            camera.targetTexture = texture;
            camera.enabled = false;
            camera.depth = -50f;

            var presenter = root.GetComponent<OntologyCharacterCreationPreviewPresenter>();
            if (presenter == null)
                presenter = Undo.AddComponent<OntologyCharacterCreationPreviewPresenter>(root);
            presenter.Configure(camera, rawImage);
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("fieldOfView").floatValue = 28f;
            presenterObject.FindProperty("framingDistance").floatValue = 4.6f;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ReparentButton(Transform oldParent, Transform newParent, string name,
            Vector2 anchor, Vector2 position, Vector2 size, Sprite sprite)
        {
            var button = oldParent.Find(name) as RectTransform;
            if (button != null) button.SetParent(newParent, false);
            button = newParent.Find(name) as RectTransform;
            Place(button, anchor, anchor, anchor, position, size);
            StyleImage(button.gameObject, sprite, Color.white, true);
        }

        private static void AuthorPartCardTemplate(GameObject root, Sprite navy)
        {
            var template = root.transform.Find(
                OntologyCharacterCustomizationUiConfig.TemplatesName + "/" +
                OntologyCharacterCustomizationUiConfig.PartCardTemplateName) as RectTransform;
            if (template == null) return;

            template.sizeDelta = new Vector2(144f, 144f);
            StyleButton(template.GetComponent<Button>(), navy, Color.white, 14f);
            var templateImage = template.GetComponent<Image>();
            if (templateImage != null)
            {
                templateImage.sprite = null;
                templateImage.color = Color.clear;
            }

            var icon = template.Find(OntologyCharacterCustomizationUiConfig.IconName)
                as RectTransform ?? template.Find("ThumbnailMask/" +
                    OntologyCharacterCustomizationUiConfig.IconName) as RectTransform;
            if (icon != null)
            {
                Place(icon, TopLeft, TopRight, TopCenter, new Vector2(0f, -10f),
                    new Vector2(-20f, 108f));
                var image = icon.GetComponent<Image>();
                if (image != null)
                {
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }
            }

            var noIcon = template.Find(OntologyCharacterCustomizationUiConfig.NoIconName)
                ?.GetComponent<TMP_Text>();
            if (noIcon != null)
            {
                Place(noIcon.rectTransform, TopLeft, TopRight, TopCenter,
                    new Vector2(0f, -18f), new Vector2(-24f, 92f));
                noIcon.fontSize = 13f;
                noIcon.color = new Color(0.75f, 0.82f, 0.85f, 0.75f);
                noIcon.alignment = TextAlignmentOptions.Center;
            }

            foreach (var childName in new[]
                     {
                         "Text", OntologyCharacterCustomizationUiConfig.LabelName,
                         OntologyCharacterCustomizationUiConfig.StateName,
                         OntologyCharacterCustomizationUiConfig.NoIconName,
                         OntologyCharacterCustomizationUiConfig.BadgeName
                     })
            {
                var child = template.Find(childName);
                if (child != null) child.gameObject.SetActive(false);
            }

            var circle = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpriteAssetPath);
            var mask = template.Find("ThumbnailMask")?.GetComponent<Image>();
            if (mask != null)
            {
                mask.sprite = circle;
                Place(mask.rectTransform, Center, Center, Center, Vector2.zero,
                    new Vector2(123f, 123f));
            }

            var badge = template.Find(OntologyCharacterCustomizationUiConfig.BadgeName)
                ?.GetComponent<TMP_Text>();
            if (badge != null)
            {
                Place(badge.rectTransform, TopRight, TopRight, TopRight,
                    new Vector2(-8f, -8f), new Vector2(58f, 24f));
                badge.fontSize = 11f;
                badge.color = SoftGreen;
                badge.alignment = TextAlignmentOptions.Center;
            }

            var label = template.Find(OntologyCharacterCustomizationUiConfig.LabelName)
                ?.GetComponent<TMP_Text>();
            if (label != null)
            {
                Place(label.rectTransform, BottomLeft, BottomRight, BottomCenter,
                    new Vector2(0f, 34f), new Vector2(-18f, 28f));
                label.fontSize = 14f;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;
            }

            var state = template.Find(OntologyCharacterCustomizationUiConfig.StateName)
                ?.GetComponent<TMP_Text>();
            if (state != null)
            {
                Place(state.rectTransform, BottomLeft, BottomRight, BottomCenter,
                    new Vector2(0f, 10f), new Vector2(-18f, 22f));
                state.fontSize = 11f;
                state.color = SoftGreen;
                state.alignment = TextAlignmentOptions.Center;
            }

            var outline = Rect(template, "SelectionOutline");
            Place(outline, Center, Center, Center, Vector2.zero,
                new Vector2(135f, 135f));
            StyleImage(outline.gameObject, circle, Color.white, false);
            outline.SetAsFirstSibling();
            outline.gameObject.SetActive(false);
        }

        private static void StyleScrollArea(Transform scrollTransform)
        {
            if (scrollTransform == null) return;
            // The legacy hierarchy has a Mask on both the ScrollView and the
            // Viewport. Keep only the viewport mask; a transparent outer mask
            // would clip every generated icon and part card.
            var outerMask = scrollTransform.GetComponent<Mask>();
            if (outerMask != null) Undo.DestroyObjectImmediate(outerMask);
            var outerImage = scrollTransform.GetComponent<Image>();
            if (outerImage != null) Undo.DestroyObjectImmediate(outerImage);
            var outerRenderer = scrollTransform.GetComponent<CanvasRenderer>();
            if (outerRenderer != null) Undo.DestroyObjectImmediate(outerRenderer);
            var viewport = scrollTransform.Find(OntologyCharacterCustomizationUiConfig.CategoryViewportName)
                           ?? scrollTransform.Find(OntologyCharacterCustomizationUiConfig.ViewportName);
            if (viewport != null)
            {
                var viewportImage = viewport.GetComponent<Image>();
                if (viewportImage != null)
                {
                    viewportImage.sprite = null;
                    viewportImage.color = Color.white;
                    viewportImage.raycastTarget = true;
                }
                var viewportMask = viewport.GetComponent<Mask>();
                if (viewportMask != null)
                {
                    viewportMask.enabled = true;
                    viewportMask.showMaskGraphic = false;
                }
            }

            var scroll = scrollTransform.GetComponent<ScrollRect>();
            if (scroll == null) return;
            scroll.verticalScrollbar = null;
            var legacyBar = scrollTransform.Find("VerticalScrollbar");
            if (legacyBar != null) Undo.DestroyObjectImmediate(legacyBar.gameObject);
        }

        private static void StyleInput(RectTransform input, Sprite sprite)
        {
            if (input == null) return;
            StyleImage(input.gameObject, sprite, Color.white, true);
            var field = input.GetComponent<TMP_InputField>();
            if (field == null) return;
            field.targetGraphic = input.GetComponent<Image>();
            if (field.textComponent != null)
            {
                field.textComponent.color = Navy;
                field.textComponent.fontSize = 17f;
            }
            if (field.placeholder is TMP_Text placeholder)
            {
                placeholder.color = new Color(MutedNavy.r, MutedNavy.g, MutedNavy.b, 0.58f);
                placeholder.fontSize = 16f;
            }
        }

        private static void StyleButton(Button button, Sprite sprite, Color textColor,
            float textSize)
        {
            if (button == null) return;
            StyleImage(button.gameObject, sprite, Color.white, true);
            button.targetGraphic = button.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.58f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.color = textColor;
                label.fontSize = textSize;
                label.enableAutoSizing = false;
                label.alignment = TextAlignmentOptions.Center;
                Place(label.rectTransform, Vector2.zero, Vector2.one, Center,
                    Vector2.zero, new Vector2(-24f, -8f));
                // RectTransform recalculates anchoredPosition when sizeDelta is
                // assigned after stretch anchors. Reassert zero so localization
                // or re-authoring cannot return the label to the legacy +8 Y.
                label.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        private static void SetSoftShadow(GameObject target, bool enabled, Vector2 distance,
            float alpha)
        {
            var shadow = target.GetComponent<Shadow>();
            if (shadow == null && enabled) shadow = Undo.AddComponent<Shadow>(target);
            if (shadow == null) return;
            shadow.enabled = enabled;
            shadow.effectColor = new Color(0.08f, 0.16f, 0.22f, alpha);
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private static void ConfigureDetailText(Transform parent, string name, float y,
            float height, float size, Color color, TextAlignmentOptions alignment)
        {
            var text = parent.Find(name).GetComponent<TMP_Text>();
            Place(text.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, y),
                new Vector2(-48f, height));
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
        }

        private static void SectionLabel(Transform parent, string name, string value)
        {
            var font = parent.GetComponentInParent<OntologyCharacterCustomizationPanel>()
                .GetComponentInChildren<TMP_Text>(true).font;
            var text = Text(parent, name, value, 18f, Navy, TextAlignmentOptions.Center, font);
            Place(text.rectTransform, TopLeft, TopRight, TopCenter, new Vector2(0f, -14f),
                new Vector2(-24f, 32f));
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TMP_Text Text(Transform parent, string name, string value, float size,
            Color color, TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            var rect = Rect(parent, name);
            var text = rect.GetComponent<TextMeshProUGUI>();
            if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static void StyleImage(GameObject target, Sprite sprite, Color color, bool raycast)
        {
            var image = target.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(target);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void EnsureFlatUiSprites()
        {
            Directory.CreateDirectory(FlatUiFolder);
            WriteRoundedSprite("flat_canvas.png", new Color32(250, 241, 228, 252),
                new Color32(211, 191, 166, 255), 15f, 2f);
            WriteRoundedSprite("flat_surface.png", new Color32(255, 249, 240, 252),
                new Color32(220, 201, 177, 255), 14f, 2f);
            WriteRoundedSprite("flat_inset.png", new Color32(239, 226, 205, 248),
                new Color32(205, 182, 153, 255), 14f, 2f);
            WriteRoundedSprite("flat_input.png", new Color32(255, 255, 255, 255),
                new Color32(219, 202, 181, 255), 12f, 2f);
            WriteRoundedSprite("flat_navy.png", new Color32(24, 47, 63, 255),
                new Color32(44, 69, 84, 255), 12f, 2f);
            WriteRoundedSprite("flat_green.png", new Color32(126, 181, 53, 255),
                new Color32(98, 151, 34, 255), 13f, 2f);
            WriteRoundedSprite("flat_coral.png", new Color32(241, 92, 74, 255),
                new Color32(207, 64, 51, 255), 13f, 2f);
            WriteRoundedSprite("flat_olive.png", new Color32(201, 207, 145, 255),
                new Color32(170, 179, 107, 255), 12f, 2f);
            WriteRoundedSprite("flat_taupe.png", new Color32(205, 194, 183, 255),
                new Color32(178, 164, 151, 255), 12f, 2f);
            WriteRoundedSprite("flat_outline_orange.png", new Color32(255, 255, 255, 0),
                new Color32(242, 145, 44, 255), 12f, 3f);
            WriteRoundedSprite("flat_scroll_track.png", new Color32(219, 207, 190, 125),
                new Color32(219, 207, 190, 125), 4f, 0f);
            WriteRoundedSprite("flat_scroll_handle.png", new Color32(171, 157, 137, 230),
                new Color32(171, 157, 137, 230), 4f, 0f);
            AssetDatabase.SaveAssets();
        }

        private static void WriteRoundedSprite(string fileName, Color32 fill, Color32 border,
            float radius, float borderWidth)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(fileName),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            var half = new Vector2(size * 0.5f - 1f, size * 0.5f - 1f);
            var center = new Vector2(size * 0.5f, size * 0.5f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f) - center;
                    var outerDistance = RoundedRectDistance(point, half, radius);
                    var outerCoverage = Mathf.Clamp01(0.5f - outerDistance);
                    var innerCoverage = outerCoverage;
                    if (borderWidth > 0f)
                    {
                        var innerHalf = half - Vector2.one * borderWidth;
                        var innerRadius = Mathf.Max(0f, radius - borderWidth);
                        var innerDistance = RoundedRectDistance(point, innerHalf, innerRadius);
                        innerCoverage = Mathf.Clamp01(0.5f - innerDistance);
                    }
                    var color = Color.Lerp((Color)border, (Color)fill, innerCoverage);
                    color.a *= outerCoverage;
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            var path = FlatUiFolder + fileName;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = size;
            importer.spritePixelsPerUnit = size;
            importer.spriteBorder = new Vector4(18f, 18f, 18f, 18f);
            importer.SaveAndReimport();
        }

        private static float RoundedRectDistance(Vector2 point, Vector2 halfSize,
            float radius)
        {
            var q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y))
                    - (halfSize - Vector2.one * radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static Sprite Sprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(FarmSprites + name);

        private static Sprite FlatSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(FlatUiFolder + name);

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Vector2 MiddleLeft = new(0f, 0.5f);
        private static readonly Vector2 MiddleRight = new(1f, 0.5f);
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 TopCenter = new(0.5f, 1f);
        private static readonly Vector2 TopRight = new(1f, 1f);
        private static readonly Vector2 BottomLeft = new(0f, 0f);
        private static readonly Vector2 BottomCenter = new(0.5f, 0f);
        private static readonly Vector2 BottomRight = new(1f, 0f);
        private static readonly Color Brown = new(0.25f, 0.13f, 0.06f, 1f);
        private static readonly Color MutedBrown = new(0.39f, 0.26f, 0.14f, 1f);
        private static readonly Color Navy = new(0.08f, 0.16f, 0.22f, 1f);
        private static readonly Color MutedNavy = new(0.32f, 0.38f, 0.42f, 1f);
        private static readonly Color FactGreen = new(0.25f, 0.43f, 0.31f, 1f);
        private static readonly Color SoftGreen = new(0.64f, 0.9f, 0.49f, 1f);
    }
}
#endif
