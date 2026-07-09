using ithappy.Creative_Characters_FREE.Controller;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Editor
{
    public sealed class OntologyPlaytestSetupEditor : EditorWindow
    {
        private const string BaseMeshPath = "Assets/ithappy/Creative_Characters_FREE/Meshes/Base_Mesh.fbx";
        private const string AnimatorControllerPath = "Assets/ithappy/Creative_Characters_FREE/Animations/Animation_Controllers/Character_Movement.controller";
        private const string UiFolderPath = "Assets/Data/Ontology/UI";
        private const string ThemePath = UiFolderPath + "/OntologyUITheme.asset";
        private const string LabelsPath = UiFolderPath + "/OntologyUILabels.asset";
        private const string UiPrefabFolderPath = "Assets/Prefabs/Ontology/UI";
        private const string DebugCanvasPrefabPath = UiPrefabFolderPath + "/OntologyDebugCanvas.prefab";

        private GameObject playerObject;
        private OntologyWorldBootstrap bootstrap;
        private bool createPlayerIfMissing = true;

        [MenuItem("Tools/Ontology/Playtest Setup")]
        public static void Open()
        {
            GetWindow<OntologyPlaytestSetupEditor>("Ontology Playtest");
        }

        [MenuItem("Tools/Ontology/Setup Runtime UI")]
        public static void SetupRuntimeUiMenu()
        {
            SetupRuntimeUi();
        }

        [MenuItem("Tools/Ontology/Setup Character Customization UI")]
        public static void SetupCharacterCustomizationUiMenu()
        {
            SetupCharacterCustomizationUi();
        }

        [MenuItem("Tools/Ontology/Hide Debug UI")]
        public static void HideDebugUiMenu()
        {
            SetDebugUiVisible(false);
        }

        [MenuItem("Tools/Ontology/Show Debug UI")]
        public static void ShowDebugUiMenu()
        {
            SetDebugUiVisible(true);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Ontology Playtest Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select or assign the ithappy player root, then run setup. The player will receive ontology runtime components and initial Player facts.", MessageType.Info);

            playerObject = (GameObject)EditorGUILayout.ObjectField("Player Object", playerObject, typeof(GameObject), true);
            bootstrap = (OntologyWorldBootstrap)EditorGUILayout.ObjectField("Bootstrap", bootstrap, typeof(OntologyWorldBootstrap), true);
            createPlayerIfMissing = EditorGUILayout.Toggle("Create If Missing", createPlayerIfMissing);

            if (GUILayout.Button("Use Selection As Player"))
            {
                playerObject = Selection.activeGameObject;
            }

            if (GUILayout.Button("Setup Player For Ontology Playtest"))
            {
                SetupPlayer();
            }

            if (GUILayout.Button("Setup Ontology Runtime UI"))
            {
                SetupRuntimeUi();
            }
        }

        private void SetupPlayer()
        {
            if (playerObject == null)
            {
                playerObject = GameObject.Find("OntologyPlayer");
            }

            if (playerObject == null && createPlayerIfMissing)
            {
                playerObject = CreateIthappyPlayerRoot();
            }

            if (playerObject == null)
            {
                Debug.LogError("Ontology playtest setup failed: Player Object is missing.");
                return;
            }

            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }

            Undo.RegisterFullObjectHierarchyUndo(playerObject, "Setup Ontology Player");

            var ontologyObject = GetOrAdd<OntologyObject>(playerObject);
            ontologyObject.ConfigureOntologyData(
                "Player",
                new[] { "Actor" },
                new[] { new OntologyFactEntry { predicate = "core_vitality", obj = "10" } });
            EditorUtility.SetDirty(ontologyObject);

            var characterController = GetOrAdd<CharacterController>(playerObject);
            characterController.height = 2f;
            characterController.radius = 0.45f;
            characterController.center = new Vector3(0f, 1f, 0f);
            EditorUtility.SetDirty(characterController);

            var rootAnimator = playerObject.GetComponent<Animator>();
            if (rootAnimator != null)
            {
                rootAnimator.enabled = false;
                EditorUtility.SetDirty(rootAnimator);
            }

            var characterMover = playerObject.GetComponent<CharacterMover>();
            if (characterMover != null)
            {
                characterMover.enabled = false;
                EditorUtility.SetDirty(characterMover);
            }

            var legacyInput = playerObject.GetComponent<MovePlayerInput>();
            if (legacyInput != null)
            {
                legacyInput.enabled = false;
                EditorUtility.SetDirty(legacyInput);
            }

            var visualAnimator = ConfigureVisualAnimator(playerObject);
            GetOrAdd<OntologyInputSystemPlayerInput>(playerObject);

            var tracker = GetOrAdd<OntologyPlayerPositionTracker>(playerObject);
            var trackerSerialized = new SerializedObject(tracker);
            trackerSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            trackerSerialized.FindProperty("player").objectReferenceValue = playerObject.transform;
            trackerSerialized.FindProperty("actorId").stringValue = "Player";
            trackerSerialized.FindProperty("tileIdPrefix").stringValue = "Tile";
            trackerSerialized.FindProperty("tileSize").floatValue = 1f;
            trackerSerialized.FindProperty("runSimulationOnTileChanged").boolValue = true;
            var transientFacts = trackerSerialized.FindProperty("transientFactsToCleanup");
            transientFacts.arraySize = 5;
            SetFactEntry(transientFacts.GetArrayElementAtIndex(0), "status", "Wet");
            SetFactEntry(transientFacts.GetArrayElementAtIndex(1), "movement_state", "Slowed");
            SetFactEntry(transientFacts.GetArrayElementAtIndex(2), "exposed_to", "ColdEnvironment");
            SetFactEntry(transientFacts.GetArrayElementAtIndex(3), "animation_intent", "ImpairedMovement");
            SetFactEntry(transientFacts.GetArrayElementAtIndex(4), "animation_intent", "Discomfort");
            trackerSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tracker);

            var controller = GetOrAdd<OntologyPlayerController>(playerObject);
            var controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            controllerSerialized.FindProperty("actorId").stringValue = "Player";
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);

            var animationAdapter = GetOrAdd<OntologyAnimationAdapter>(playerObject);
            var animationSerialized = new SerializedObject(animationAdapter);
            animationSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            animationSerialized.FindProperty("animationDatabase").objectReferenceValue = AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>("Assets/Data/Ontology/AnimationDatabase.asset");
            animationSerialized.FindProperty("targetAnimator").objectReferenceValue = visualAnimator;
            animationSerialized.FindProperty("actorId").stringValue = "Player";
            animationSerialized.FindProperty("playSelectedClip").boolValue = false;
            animationSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(animationAdapter);

            ConfigureActorToast(playerObject, bootstrap, GetOrCreateAsset<OntologyUITheme>(ThemePath), GetOrCreateAsset<OntologyUILabels>(LabelsPath));

            if (bootstrap != null && bootstrap.GetComponent<OntologyTickRunner>() == null)
            {
                var tickRunner = Undo.AddComponent<OntologyTickRunner>(bootstrap.gameObject);
                var tickSerialized = new SerializedObject(tickRunner);
                tickSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
                tickSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tickRunner);
            }

            if (bootstrap != null)
            {
                var saveController = GetOrAdd<OntologySaveController>(bootstrap.gameObject);
                var saveSerialized = new SerializedObject(saveController);
                saveSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
                saveSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(saveController);
            }

            Selection.activeGameObject = playerObject;
            SetupRuntimeUi();
            Debug.Log("Ontology playtest setup complete for " + playerObject.name + ".");
        }

        private static void SetupRuntimeUi()
        {
            var theme = GetOrCreateAsset<OntologyUITheme>(ThemePath);
            var labels = GetOrCreateAsset<OntologyUILabels>(LabelsPath);
            var canvas = GetOrCreateCanvas();
            ConfigureDebugPanel(canvas.transform, theme, labels);
            ConfigureCharacterPartPanel(canvas.transform, theme, labels);
            ConfigureRuntimeHud(canvas.transform, theme, labels);
            var player = GameObject.Find("OntologyPlayer");
            if (player != null)
            {
                ConfigureActorToast(player, FindFirstObjectByType<OntologyWorldBootstrap>(), theme, labels);
            }
            SetupUiThemeReferences();
            SaveUiPrefab(canvas);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log("Ontology runtime UI setup complete.");
        }

        private static void SetupCharacterCustomizationUi()
        {
            var canvas = GetOrCreateGameCanvas();
            EnsureEventSystem();
            RemoveCustomizationPanelFromDebugCanvas();
            var panel = GetOrCreateRect(canvas.transform, OntologyCharacterCustomizationUiConfig.PanelName, typeof(CanvasRenderer), typeof(Image), typeof(OntologyCharacterCustomizationPanel));
            SetAnchored(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(820f, 500f));

            var image = panel.GetComponent<Image>();
            image.color = OntologyCharacterCustomizationUiConfig.PanelColor;
            var group = panel.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = Undo.AddComponent<CanvasGroup>(panel.gameObject);
            }
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            DestroyChildIfExists(panel, "Categories");
            DestroyChildIfExists(panel, "PartGrid");
            DestroyChildIfExists(panel, "SelectedDetail");

            var header = GetOrCreateRect(panel, OntologyCharacterCustomizationUiConfig.HeaderName, typeof(CanvasRenderer), typeof(Image));
            SetAnchored(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(0f, 54f));
            header.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.HeaderColor;
            ConfigureDragHandle(header, panel);
            var title = GetOrCreateText(header, OntologyCharacterCustomizationUiConfig.TitleTextName, OntologyCharacterCustomizationUiConfig.Title, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            SetStretch(title.rectTransform, new Vector2(18f, 8f), new Vector2(-18f, -8f));
            var close = GetOrCreateButton(header, OntologyCharacterCustomizationUiConfig.CloseButtonName, OntologyCharacterCustomizationUiConfig.CloseLabel, OntologyCharacterCustomizationUiConfig.SecondaryButtonColor, Color.white, 16f);
            SetAnchored((RectTransform)close.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-14f, -10f), new Vector2(34f, 34f));

            var categoryArea = GetOrCreateRect(panel, OntologyCharacterCustomizationUiConfig.CategoryAreaName, typeof(CanvasRenderer), typeof(Image));
            SetAnchored(categoryArea, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -70f), new Vector2(150f, 370f));
            categoryArea.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.SurfaceColor;
            DestroyChildIfExists(categoryArea, OntologyCharacterCustomizationUiConfig.CategoryContentName);
            var categoryScrollView = GetOrCreateRect(categoryArea, OntologyCharacterCustomizationUiConfig.CategoryScrollViewName, typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
            SetStretch(categoryScrollView, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            categoryScrollView.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.ScrollBackgroundColor;
            categoryScrollView.GetComponent<Mask>().showMaskGraphic = false;
            var categoryViewport = GetOrCreateRect(categoryScrollView, OntologyCharacterCustomizationUiConfig.CategoryViewportName, typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            SetStretch(categoryViewport, Vector2.zero, Vector2.zero);
            categoryViewport.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.ViewportMaskColor;
            categoryViewport.GetComponent<Mask>().showMaskGraphic = false;
            var categoryContent = GetOrCreateRect(categoryViewport, OntologyCharacterCustomizationUiConfig.CategoryContentName, typeof(RectTransform));
            categoryContent.anchorMin = new Vector2(0f, 1f);
            categoryContent.anchorMax = new Vector2(1f, 1f);
            categoryContent.pivot = new Vector2(0.5f, 1f);
            categoryContent.anchoredPosition = Vector2.zero;
            categoryContent.sizeDelta = new Vector2(0f, 0f);
            var categoryLayout = categoryContent.GetComponent<VerticalLayoutGroup>() ?? Undo.AddComponent<VerticalLayoutGroup>(categoryContent.gameObject);
            categoryLayout.spacing = 8f;
            categoryLayout.childControlWidth = true;
            categoryLayout.childControlHeight = false;
            categoryLayout.childForceExpandWidth = true;
            categoryLayout.childForceExpandHeight = false;
            var categoryFitter = categoryContent.GetComponent<ContentSizeFitter>() ?? Undo.AddComponent<ContentSizeFitter>(categoryContent.gameObject);
            categoryFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            categoryFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var categoryScroll = categoryScrollView.GetComponent<ScrollRect>();
            categoryScroll.viewport = categoryViewport;
            categoryScroll.content = categoryContent;
            categoryScroll.horizontal = false;
            categoryScroll.vertical = true;

            var partGridArea = GetOrCreateRect(panel, OntologyCharacterCustomizationUiConfig.PartGridAreaName, typeof(CanvasRenderer), typeof(Image));
            SetAnchored(partGridArea, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -70f), new Vector2(380f, 370f));
            partGridArea.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.GridBackgroundColor;
            var scrollView = GetOrCreateRect(partGridArea, OntologyCharacterCustomizationUiConfig.ScrollViewName, typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
            SetStretch(scrollView, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            scrollView.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.ScrollBackgroundColor;
            scrollView.GetComponent<Mask>().showMaskGraphic = false;
            var viewport = GetOrCreateRect(scrollView, OntologyCharacterCustomizationUiConfig.ViewportName, typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            SetStretch(viewport, Vector2.zero, Vector2.zero);
            viewport.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.ViewportMaskColor;
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            var partGridContent = GetOrCreateRect(viewport, OntologyCharacterCustomizationUiConfig.PartGridContentName, typeof(RectTransform));
            partGridContent.anchorMin = new Vector2(0f, 1f);
            partGridContent.anchorMax = new Vector2(0f, 1f);
            partGridContent.pivot = new Vector2(0f, 1f);
            partGridContent.anchoredPosition = Vector2.zero;
            partGridContent.sizeDelta = new Vector2(340f, 600f);
            var grid = partGridContent.GetComponent<GridLayoutGroup>() ?? Undo.AddComponent<GridLayoutGroup>(partGridContent.gameObject);
            grid.cellSize = new Vector2(128f, 150f);
            grid.spacing = new Vector2(8f, 8f);
            var fitter = partGridContent.GetComponent<ContentSizeFitter>() ?? Undo.AddComponent<ContentSizeFitter>(partGridContent.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollView.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = partGridContent;
            scroll.horizontal = false;
            scroll.vertical = true;

            var detail = GetOrCreateRect(panel, OntologyCharacterCustomizationUiConfig.DetailAreaName, typeof(CanvasRenderer), typeof(Image));
            SetAnchored(detail, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(572f, -70f), new Vector2(230f, 370f));
            detail.GetComponent<Image>().color = OntologyCharacterCustomizationUiConfig.SurfaceColor;
            var selectedIcon = GetOrCreateRect(detail, OntologyCharacterCustomizationUiConfig.SelectedIconName, typeof(CanvasRenderer), typeof(Image));
            SetAnchored(selectedIcon, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(198f, 126f));
            selectedIcon.GetComponent<Image>().preserveAspect = true;
            selectedIcon.GetComponent<Image>().color = Color.white;
            selectedIcon.GetComponent<Image>().enabled = false;
            var selectedTitle = GetOrCreateText(detail, OntologyCharacterCustomizationUiConfig.SelectedTitleName, OntologyCharacterCustomizationUiConfig.SelectPartTitle, 18f, Color.white, TextAlignmentOptions.MidlineLeft);
            SetAnchored(selectedTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -152f), new Vector2(198f, 30f));
            var selectedDescription = GetOrCreateText(detail, OntologyCharacterCustomizationUiConfig.SelectedDescriptionName, OntologyCharacterCustomizationUiConfig.SelectPartDescription, 13f, OntologyCharacterCustomizationUiConfig.MutedTextColor, TextAlignmentOptions.TopLeft);
            selectedDescription.enableWordWrapping = true;
            SetAnchored(selectedDescription.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -190f), new Vector2(198f, 56f));
            var factPreview = GetOrCreateText(detail, OntologyCharacterCustomizationUiConfig.FactPreviewName, string.Empty, 12f, OntologyCharacterCustomizationUiConfig.FactTextColor, TextAlignmentOptions.TopLeft);
            factPreview.enableWordWrapping = true;
            SetAnchored(factPreview.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -252f), new Vector2(198f, 70f));
            var equip = GetOrCreateButton(detail, OntologyCharacterCustomizationUiConfig.EquipButtonName, OntologyCharacterCustomizationUiConfig.EquipLabel, OntologyCharacterCustomizationUiConfig.EquipButtonColor, Color.black, 13f);
            SetAnchored((RectTransform)equip.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -332f), new Vector2(94f, 32f));
            var unequip = GetOrCreateButton(detail, OntologyCharacterCustomizationUiConfig.UnequipButtonName, OntologyCharacterCustomizationUiConfig.UnequipLabel, OntologyCharacterCustomizationUiConfig.SecondaryButtonColor, Color.white, 13f);
            SetAnchored((RectTransform)unequip.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -332f), new Vector2(94f, 32f));

            var status = GetOrCreateText(panel, OntologyCharacterCustomizationUiConfig.StatusName, string.Empty, 13f, OntologyCharacterCustomizationUiConfig.MutedTextColor, TextAlignmentOptions.MidlineLeft);
            SetAnchored(status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(0f, 24f));

            DestroyChildIfExists(canvas.transform, OntologyCharacterCustomizationUiConfig.ToggleHintName);
            var hintButton = GetOrCreateButton(canvas.transform, OntologyCharacterCustomizationUiConfig.ToggleHintName, OntologyCharacterCustomizationUiConfig.ToggleHint, OntologyCharacterCustomizationUiConfig.HintBackgroundColor, Color.white, 16f);
            SetAnchored((RectTransform)hintButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(260f, 34f));
            var hint = hintButton.GetComponentInChildren<TextMeshProUGUI>(true);

            var templates = GetOrCreateRect(panel, OntologyCharacterCustomizationUiConfig.TemplatesName, typeof(RectTransform));
            templates.gameObject.SetActive(false);
            var categoryTemplate = GetOrCreateButton(templates, OntologyCharacterCustomizationUiConfig.CategoryButtonTemplateName, OntologyCharacterCustomizationUiConfig.CategoryTemplateLabel, OntologyCharacterCustomizationUiConfig.SurfaceOpaqueColor, Color.white, 13f);
            ((RectTransform)categoryTemplate.transform).sizeDelta = new Vector2(130f, 32f);
            var partTemplate = GetOrCreateButton(templates, OntologyCharacterCustomizationUiConfig.PartCardTemplateName, string.Empty, OntologyCharacterCustomizationUiConfig.SurfaceOpaqueColor, Color.white, 13f);
            var partRect = (RectTransform)partTemplate.transform;
            partRect.sizeDelta = new Vector2(128f, 150f);
            var icon = GetOrCreateRect(partRect, OntologyCharacterCustomizationUiConfig.IconName, typeof(CanvasRenderer), typeof(Image));
            SetStretch(icon, new Vector2(10f, 54f), new Vector2(-10f, -10f));
            icon.GetComponent<Image>().preserveAspect = true;
            var noIcon = GetOrCreateText(partRect, OntologyCharacterCustomizationUiConfig.NoIconName, OntologyCharacterCustomizationUiConfig.NoIconLabel, 14f, OntologyCharacterCustomizationUiConfig.MutedTextColor, TextAlignmentOptions.Center);
            SetStretch(noIcon.rectTransform, new Vector2(10f, 54f), new Vector2(-10f, -10f));
            var badge = GetOrCreateText(partRect, OntologyCharacterCustomizationUiConfig.BadgeName, OntologyCharacterCustomizationUiConfig.OnBadge, 11f, Color.white, TextAlignmentOptions.Center);
            SetStretch(badge.rectTransform, new Vector2(64f, -26f), new Vector2(-6f, -4f));
            var label = GetOrCreateText(partRect, OntologyCharacterCustomizationUiConfig.LabelName, OntologyCharacterCustomizationUiConfig.PartTemplateLabel, 14f, Color.white, TextAlignmentOptions.Center);
            SetStretch(label.rectTransform, new Vector2(6f, 22f), new Vector2(-6f, -94f));
            var state = GetOrCreateText(partRect, OntologyCharacterCustomizationUiConfig.StateName, OntologyCharacterCustomizationUiConfig.SlotTemplateLabel, 12f, OntologyCharacterCustomizationUiConfig.MutedTextColor, TextAlignmentOptions.Center);
            SetStretch(state.rectTransform, new Vector2(6f, 4f), new Vector2(-6f, -126f));

            var player = GameObject.Find("OntologyPlayer");
            var adapter = player != null ? player.GetComponent<OntologyCharacterPartAdapter>() : FindFirstObjectByType<OntologyCharacterPartAdapter>();
            var serialized = new SerializedObject(panel.GetComponent<OntologyCharacterCustomizationPanel>());
            serialized.FindProperty("partAdapter").objectReferenceValue = adapter;
            serialized.FindProperty("partDatabase").objectReferenceValue = adapter != null ? adapter.PartDatabase : AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>("Assets/Data/Ontology/CharacterPartDatabase.asset");
            serialized.FindProperty("panelCanvasGroup").objectReferenceValue = group;
            serialized.FindProperty("startsVisible").boolValue = false;
            serialized.FindProperty("categoryContainer").objectReferenceValue = categoryContent;
            serialized.FindProperty("partGridContainer").objectReferenceValue = partGridContent;
            serialized.FindProperty("categoryButtonTemplate").objectReferenceValue = categoryTemplate;
            serialized.FindProperty("partCardTemplate").objectReferenceValue = partTemplate;
            serialized.FindProperty("selectedIcon").objectReferenceValue = selectedIcon.GetComponent<Image>();
            serialized.FindProperty("selectedTitle").objectReferenceValue = selectedTitle;
            serialized.FindProperty("selectedDescription").objectReferenceValue = selectedDescription;
            serialized.FindProperty("factPreview").objectReferenceValue = factPreview;
            serialized.FindProperty("statusText").objectReferenceValue = status;
            serialized.FindProperty("toggleHintText").objectReferenceValue = hint;
            serialized.FindProperty("openButton").objectReferenceValue = hintButton;
            serialized.FindProperty("equipButton").objectReferenceValue = equip;
            serialized.FindProperty("unequipButton").objectReferenceValue = unequip;
            serialized.FindProperty("closeButton").objectReferenceValue = close;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(panel.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("Ontology character customization UI setup complete.");
        }

        private static Canvas GetOrCreateGameCanvas()
        {
            var canvasObject = GameObject.Find(OntologyCharacterCustomizationUiConfig.GameCanvasName);
            if (canvasObject == null)
            {
                canvasObject = new GameObject(OntologyCharacterCustomizationUiConfig.GameCanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Ontology Game Canvas");
            }

            var canvas = GetOrAdd<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = GetOrAdd<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GetOrAdd<GraphicRaycaster>(canvasObject);
            EditorUtility.SetDirty(canvasObject);
            return canvas;
        }

        private static void RemoveCustomizationPanelFromDebugCanvas()
        {
            var debugCanvas = GameObject.Find("OntologyDebugCanvas");
            if (debugCanvas == null)
            {
                return;
            }

            var oldPanel = debugCanvas.transform.Find(OntologyCharacterCustomizationUiConfig.PanelName);
            if (oldPanel != null)
            {
                Undo.DestroyObjectImmediate(oldPanel.gameObject);
            }
        }

        private static void SetDebugUiVisible(bool visible)
        {
            var debugCanvas = FindGameObjectIncludingInactive("OntologyDebugCanvas");
            if (debugCanvas == null)
            {
                Debug.LogWarning("OntologyDebugCanvas not found.");
                return;
            }

            Undo.RecordObject(debugCanvas, visible ? "Show Debug UI" : "Hide Debug UI");
            debugCanvas.SetActive(visible);
            EditorUtility.SetDirty(debugCanvas);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(debugCanvas.scene);
            Debug.Log((visible ? "Showed" : "Hid") + " OntologyDebugCanvas.");
        }

        private static GameObject FindGameObjectIncludingInactive(string objectName)
        {
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (transform.name == objectName)
                {
                    return transform.gameObject;
                }
            }

            return null;
        }

        private static void SetupUiThemeReferences()
        {
            var theme = GetOrCreateAsset<OntologyUITheme>(ThemePath);
            var labels = GetOrCreateAsset<OntologyUILabels>(LabelsPath);
            foreach (var panel in Object.FindObjectsByType<OntologyUIPanelBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var serialized = new SerializedObject(panel);
                serialized.FindProperty("theme").objectReferenceValue = theme;
                serialized.FindProperty("labels").objectReferenceValue = labels;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(panel);
            }

            AssetDatabase.SaveAssets();
        }

        private static Canvas GetOrCreateCanvas()
        {
            var canvasObject = GameObject.Find("OntologyDebugCanvas");
            if (canvasObject == null)
            {
                canvasObject = new GameObject("OntologyDebugCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Ontology Debug Canvas");
            }

            var canvas = GetOrAdd<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetOrAdd<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GetOrAdd<GraphicRaycaster>(canvasObject);
            EnsureEventSystem();
            EditorUtility.SetDirty(canvasObject);
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }

            ConfigureEventSystemInputModule(eventSystem.gameObject);
            EditorUtility.SetDirty(eventSystem);
        }

        private static void ConfigureEventSystemInputModule(GameObject eventSystemObject)
        {
#if ENABLE_INPUT_SYSTEM
            var inputSystemModule = eventSystemObject.GetComponent<InputSystemUIInputModule>() ?? Undo.AddComponent<InputSystemUIInputModule>(eventSystemObject);
            var standaloneModule = eventSystemObject.GetComponent<StandaloneInputModule>();
            if (standaloneModule != null)
            {
                Undo.DestroyObjectImmediate(standaloneModule);
            }
            EditorUtility.SetDirty(inputSystemModule);
#else
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                Undo.AddComponent<StandaloneInputModule>(eventSystemObject);
            }
#endif
        }

        private static void DestroyChildIfExists(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static void ConfigureDragHandle(RectTransform handle, RectTransform target)
        {
            var dragHandleType = System.Type.GetType("Tormia.Ontology.Core.OntologyUIDragHandle, Assembly-CSharp");
            if (dragHandleType == null)
            {
                Debug.LogError("OntologyUIDragHandle type was not found. Refresh scripts and run setup again.");
                return;
            }

            var dragHandle = handle.GetComponent(dragHandleType) ?? Undo.AddComponent(handle.gameObject, dragHandleType);
            var serialized = new SerializedObject(dragHandle);
            serialized.FindProperty("targetRect").objectReferenceValue = target;
            serialized.FindProperty("constrainToParent").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dragHandle);
        }

        private static void ConfigureDebugPanel(Transform canvas, OntologyUITheme theme, OntologyUILabels labels)
        {
            var panel = GetOrCreateRect(canvas, "OntologyDebugPanel", typeof(Image), typeof(OntologyDebugPanel));
            SetAnchored(panel, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.up, theme.debugPanelAnchoredPosition, theme.debugPanelSize);
            panel.GetComponent<Image>().color = theme.panelBackground;

            var title = GetOrCreateText(panel, "Title", labels.debugTitle, theme.titleFontSize, theme.titleText, TextAlignmentOptions.Center);
            SetStretch(title.rectTransform, new Vector2(16f, -42f), new Vector2(-16f, -8f));

            var output = GetOrCreateText(panel, "OutputText", string.Empty, theme.outputFontSize, theme.outputText, TextAlignmentOptions.TopLeft);
            output.enableWordWrapping = true;
            SetStretch(output.rectTransform, new Vector2(16f, 18f), new Vector2(-284f, -190f));

            var toast = GetOrCreateText(panel, "ToastText", string.Empty, theme.statusFontSize, theme.statusText, TextAlignmentOptions.MidlineLeft);
            SetStretch(toast.rectTransform, new Vector2(16f, -178f), new Vector2(-284f, -150f));

            var graph = GetOrCreateText(panel, "GraphSummaryText", string.Empty, theme.statusFontSize, theme.statusText, TextAlignmentOptions.MidlineLeft);
            SetStretch(graph.rectTransform, new Vector2(16f, -148f), new Vector2(-284f, -120f));

            var run = GetOrCreateButton(panel, "RunSimulationButton", labels.runSimulation, theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)run.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(24f, -64f), theme.fixedButtonSize);
            var attack = GetOrCreateButton(panel, "AttackTreeButton", labels.attackTree, theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)attack.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(170f, -64f), theme.fixedButtonSize);
            var reset = GetOrCreateButton(panel, "ResetWorldButton", labels.resetWorld, theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)reset.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(316f, -64f), theme.fixedButtonSize);
            var save = GetOrCreateButton(panel, "SaveSnapshotButton", labels.saveSnapshot, theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)save.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(24f, -102f), theme.fixedButtonSize);
            var load = GetOrCreateButton(panel, "LoadSnapshotButton", labels.loadSnapshot, theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)load.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(170f, -102f), theme.fixedButtonSize);
            var filter = GetOrCreateButton(panel, "ActionFilterButton", labels.allActionsFilter, theme.actionButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)filter.transform, Vector2.up, Vector2.up, Vector2.up, new Vector2(316f, -102f), new Vector2(224f, 32f));

            var actionContainer = GetOrCreateRect(panel, "ActionButtonContainer", typeof(Image));
            SetStretch(actionContainer, new Vector2(492f, 18f), new Vector2(-16f, -64f));
            actionContainer.GetComponent<Image>().color = theme.rowOdd;

            var header = GetOrCreateText(actionContainer, "Header", labels.actionButtonsTitle, theme.statusFontSize, theme.titleText, TextAlignmentOptions.MidlineLeft);
            SetStretch(header.rectTransform, new Vector2(10f, -30f), new Vector2(-10f, -2f));

            var buttons = GetOrCreateScrollContent(actionContainer, "ButtonsScrollView", "Buttons", theme);
            ClearGeneratedActionButtons(buttons);
            var debug = panel.GetComponent<OntologyDebugPanel>();
            var serialized = new SerializedObject(debug);
            serialized.FindProperty("outputText").objectReferenceValue = output;
            serialized.FindProperty("runButton").objectReferenceValue = run;
            serialized.FindProperty("attackButton").objectReferenceValue = attack;
            serialized.FindProperty("resetButton").objectReferenceValue = reset;
            serialized.FindProperty("saveButton").objectReferenceValue = save;
            serialized.FindProperty("loadButton").objectReferenceValue = load;
            serialized.FindProperty("filterButton").objectReferenceValue = filter;
            serialized.FindProperty("actionButtonContainer").objectReferenceValue = buttons;
            serialized.FindProperty("titleText").objectReferenceValue = title;
            serialized.FindProperty("toastText").objectReferenceValue = toast;
            serialized.FindProperty("graphSummaryText").objectReferenceValue = graph;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(debug);
        }

        private static void ConfigureCharacterPartPanel(Transform canvas, OntologyUITheme theme, OntologyUILabels labels)
        {
            var panel = canvas.Find("OntologyCharacterPartPanel") as RectTransform;
            if (panel == null)
            {
                return;
            }

            SetAnchored(panel, Vector2.one, Vector2.one, Vector2.one, theme.panelAnchoredPosition, theme.panelSize);
            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.color = theme.panelBackground;
            }

            var detail = GetOrCreateText(panel, "DetailText", string.Empty, theme.statusFontSize, theme.statusText, TextAlignmentOptions.TopLeft);
            detail.enableWordWrapping = true;
            SetStretch(detail.rectTransform, new Vector2(16f, 86f), new Vector2(-16f, 154f));

            var preview = GetOrCreateText(panel, "PreviewText", string.Empty, theme.outputFontSize, theme.outputText, TextAlignmentOptions.TopLeft);
            preview.enableWordWrapping = true;
            SetStretch(preview.rectTransform, new Vector2(16f, 18f), new Vector2(-16f, 82f));

            var search = GetOrCreateInputField(panel, "SearchInput", labels.partSearchPlaceholder, theme);
            SetAnchored((RectTransform)search.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.up, new Vector2(16f, -82f), new Vector2(176f, 28f));

            var stateFilter = GetOrCreateButton(panel, "StateFilterButton", string.Format(labels.partStateFilterFormat, labels.partStateAll), theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)stateFilter.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.up, new Vector2(200f, -82f), new Vector2(104f, 28f));

            var slotFilter = GetOrCreateButton(panel, "SlotFilterButton", string.Format(labels.partSlotFilterFormat, labels.partSlotAll), theme.fixedButton, theme.buttonText, theme.buttonFontSize);
            SetAnchored((RectTransform)slotFilter.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.up, new Vector2(310f, -82f), new Vector2(104f, 28f));

            var rows = GetOrCreateScrollContent(panel, "RowsScrollView", "Rows", theme);
            var rowsScroll = panel.Find("RowsScrollView") as RectTransform;
            if (rowsScroll != null)
            {
                SetStretch(rowsScroll, new Vector2(16f, 158f), new Vector2(-16f, -116f));
            }
            var serialized = new SerializedObject(panel.GetComponent<OntologyCharacterPartPanel>());
            serialized.FindProperty("rowContainer").objectReferenceValue = rows;
            serialized.FindProperty("detailText").objectReferenceValue = detail;
            serialized.FindProperty("previewText").objectReferenceValue = preview;
            serialized.FindProperty("searchInput").objectReferenceValue = search;
            serialized.FindProperty("stateFilterButton").objectReferenceValue = stateFilter;
            serialized.FindProperty("slotFilterButton").objectReferenceValue = slotFilter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureRuntimeHud(Transform canvas, OntologyUITheme theme, OntologyUILabels labels)
        {
            var hud = GetOrCreateRect(canvas, "OntologyRuntimeStatusHUD", typeof(Image), typeof(OntologyRuntimeStatusHUD));
            SetAnchored(hud, Vector2.zero, Vector2.zero, Vector2.zero, theme.hudPanelAnchoredPosition, theme.hudPanelSize);
            hud.GetComponent<Image>().color = theme.hudBackground;
            var text = GetOrCreateText(hud, "Text", labels.hudTitle, theme.hudFontSize, theme.hudText, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true;
            SetStretch(text.rectTransform, new Vector2(12f, 10f), new Vector2(-12f, -10f));

            var component = hud.GetComponent<OntologyRuntimeStatusHUD>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("textMeshProText").objectReferenceValue = text;
            serialized.FindProperty("bootstrap").objectReferenceValue = FindFirstObjectByType<OntologyWorldBootstrap>();
            serialized.FindProperty("animationAdapter").objectReferenceValue = FindFirstObjectByType<OntologyAnimationAdapter>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static void ConfigureActorToast(GameObject player, OntologyWorldBootstrap targetBootstrap, OntologyUITheme theme, OntologyUILabels labels)
        {
            if (player == null)
            {
                return;
            }

            var toast = GetOrAdd<OntologyActorToast>(player);
            var toastSerialized = new SerializedObject(toast);
            toastSerialized.FindProperty("theme").objectReferenceValue = theme;
            toastSerialized.FindProperty("anchor").objectReferenceValue = FindActorToastAnchor(player);
            toastSerialized.FindProperty("targetCamera").objectReferenceValue = Camera.main;
            toastSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(toast);

            var emitter = GetOrAdd<OntologyActorFactToastEmitter>(player);
            var emitterSerialized = new SerializedObject(emitter);
            emitterSerialized.FindProperty("bootstrap").objectReferenceValue = targetBootstrap;
            emitterSerialized.FindProperty("actorToast").objectReferenceValue = toast;
            emitterSerialized.FindProperty("labels").objectReferenceValue = labels;
            emitterSerialized.FindProperty("partDatabase").objectReferenceValue = AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>("Assets/Data/Ontology/CharacterPartDatabase.asset");
            emitterSerialized.FindProperty("actorId").stringValue = "Player";
            emitterSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(emitter);
        }

        private static Transform FindActorToastAnchor(GameObject player)
        {
            foreach (var animator in player.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.isHuman)
                {
                    continue;
                }

                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head;
                }
            }

            var namedHead = player.transform.Find("Head");
            return namedHead != null ? namedHead : player.transform;
        }

        private static RectTransform GetOrCreateScrollContent(Transform parent, string scrollName, string contentName, OntologyUITheme theme)
        {
            var scroll = GetOrCreateRect(parent, scrollName, typeof(Image), typeof(Mask), typeof(ScrollRect));
            SetStretch(scroll, theme.rowsOffsetMin, theme.rowsOffsetMax);
            scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            scroll.GetComponent<Mask>().showMaskGraphic = false;

            var viewport = GetOrCreateRect(scroll, "Viewport", typeof(Image), typeof(Mask));
            SetStretch(viewport, Vector2.zero, Vector2.zero);
            viewport.GetComponent<Image>().color = Color.clear;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = viewport.Find(contentName) as RectTransform;
            var oldContent = parent.Find(contentName) as RectTransform;
            if (content == null && oldContent != null)
            {
                content = oldContent;
                content.SetParent(viewport, false);
            }
            if (content == null)
            {
                content = GetOrCreateRect(viewport, contentName);
            }

            SetStretch(content, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);

            var scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        private static void ClearGeneratedActionButtons(RectTransform content)
        {
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child != null && child.name.StartsWith("ActionButton_", System.StringComparison.Ordinal))
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name, params System.Type[] componentTypes)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                child = go.GetComponent<RectTransform>();
                child.SetParent(parent, false);
            }

            foreach (var componentType in componentTypes)
            {
                if (child.GetComponent(componentType) == null)
                {
                    Undo.AddComponent(child.gameObject, componentType);
                }
            }

            return child;
        }

        private static TextMeshProUGUI GetOrCreateText(Transform parent, string name, string value, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var rect = GetOrCreateRect(parent, name, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var text = rect.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static Button GetOrCreateButton(Transform parent, string name, string label, Color background, Color textColor, float fontSize)
        {
            var rect = GetOrCreateRect(parent, name, typeof(CanvasRenderer), typeof(Image), typeof(Button));
            rect.GetComponent<Image>().color = background;
            var text = GetOrCreateText(rect, "Text", label, fontSize, textColor, TextAlignmentOptions.Center);
            SetStretch(text.rectTransform, Vector2.zero, Vector2.zero);
            return rect.GetComponent<Button>();
        }

        private static TMP_InputField GetOrCreateInputField(Transform parent, string name, string placeholder, OntologyUITheme theme)
        {
            var rect = GetOrCreateRect(parent, name, typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            rect.GetComponent<Image>().color = theme.rowOdd;

            var text = GetOrCreateText(rect, "Text", string.Empty, theme.buttonFontSize, theme.rowText, TextAlignmentOptions.MidlineLeft);
            text.enableWordWrapping = false;
            SetStretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));

            var placeholderText = GetOrCreateText(rect, "Placeholder", placeholder, theme.buttonFontSize, theme.statusText, TextAlignmentOptions.MidlineLeft);
            placeholderText.enableWordWrapping = false;
            SetStretch(placeholderText.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));

            var input = rect.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SaveUiPrefab(Canvas canvas)
        {
            EnsurePrefabFolder();
            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, DebugCanvasPrefabPath);
        }

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Ontology"))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "Ontology");
            }

            if (!AssetDatabase.IsValidFolder(UiPrefabFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs/Ontology", "UI");
            }
        }

        private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            EnsureUiFolder();
            asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void EnsureUiFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data"))
            {
                AssetDatabase.CreateFolder("Assets", "Data");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Data/Ontology"))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Ontology");
            }

            if (!AssetDatabase.IsValidFolder(UiFolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Data/Ontology", "UI");
            }
        }

        private static GameObject CreateIthappyPlayerRoot()
        {
            var root = new GameObject("OntologyPlayer");
            Undo.RegisterCreatedObjectUndo(root, "Create Ontology Player");
            root.transform.position = Vector3.up * 0.05f;

            var meshAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BaseMeshPath);
            if (meshAsset != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(meshAsset);
                if (visual != null)
                {
                    Undo.RegisterCreatedObjectUndo(visual, "Create Ontology Player Visual");
                    visual.name = "Visual_Base_Mesh";
                    visual.transform.SetParent(root.transform, false);
                }
            }
            else
            {
                Debug.LogWarning("ithappy Base_Mesh was not found at: " + BaseMeshPath);
            }

            return root;
        }

        private static void AssignAnimatorController(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController != null)
            {
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
            if (controller == null)
            {
                Debug.LogWarning("ithappy animator controller was not found at: " + AnimatorControllerPath);
                return;
            }

            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(animator);
        }

        private static Animator ConfigureVisualAnimator(GameObject playerObject)
        {
            var visual = playerObject.transform.Find("Visual_Base_Mesh");
            if (visual != null)
            {
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;
                EditorUtility.SetDirty(visual);
            }

            Animator bestAnimator = null;
            foreach (var animator in playerObject.GetComponentsInChildren<Animator>(true))
            {
                if (animator.avatar == null || !animator.isHuman)
                {
                    continue;
                }

                bestAnimator = animator;
                break;
            }

            if (bestAnimator != null)
            {
                AssignAnimatorController(bestAnimator);
                bestAnimator.applyRootMotion = false;
                bestAnimator.enabled = true;
                EditorUtility.SetDirty(bestAnimator);
            }

            return bestAnimator;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            return Undo.AddComponent<T>(target);
        }

        private static void SetFactEntry(SerializedProperty property, string predicate, string obj)
        {
            property.FindPropertyRelative("predicate").stringValue = predicate;
            property.FindPropertyRelative("obj").stringValue = obj;
        }
    }
}
