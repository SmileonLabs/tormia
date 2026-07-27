using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tormia.Ontology.Core
{
    /// <summary>Game-style catalog: category menu, rotating 3D preview, browse arrows, then Select.</summary>
    [ExecuteAlways]
    public sealed class OntologyObjectPlacementPanel : MonoBehaviour
    {
        [SerializeField] private OntologyRuntimeObjectPlacementController placementController;
        [SerializeField] private OntologyPlaceableCatalog catalog;
        [SerializeField] private GameObject panelPrefab;
        [SerializeField] private OntologyObjectPlacementUiTheme theme;
        [SerializeField] private Button openButton;
        [SerializeField] private KeyCode toggleKey = KeyCode.B;

        private RectTransform panelRoot;
        private RectTransform categoryRoot;
        private Button categoryButtonTemplate;
        private Button objectTab;
        private Button npcTab;
        private Button monsterTab;
        private Button selectButton;
        private Button previousButton;
        private Button nextButton;
        private RawImage previewImage;
        private TMP_Text nameText;
        private TMP_Text detailText;
        private TMP_Text pageText;
        private TMP_Text statusText;
        private TMP_Text definitionIdText;
        private TMP_Text conceptsText;
        private TMP_Text placementSurfaceText;
        private TMP_Text ruleBlocksText;
        private OntologyPlaceablePreviewRenderer previewRenderer;
        private readonly List<Button> categoryButtons = new();
        private List<string> categories = new();
        private string selectedCategory;
        private OntologyPlaceableKind selectedKind = OntologyPlaceableKind.Object;
        private int selectedIndex;
        private bool visible;
        private string previewedDefinitionId;
        public GameObject RuntimeToggleObject =>
            openButton == null ? null : openButton.gameObject;
        public GameObject RuntimePanelObject =>
            panelRoot == null ? null : panelRoot.gameObject;
        public bool IsVisible => visible;

        public void Configure(OntologyRuntimeObjectPlacementController controller, OntologyPlaceableCatalog nextCatalog, GameObject nextPanelPrefab = null, OntologyObjectPlacementUiTheme nextTheme = null)
        {
            placementController = controller; catalog = nextCatalog;
            if (nextPanelPrefab != null) panelPrefab = nextPanelPrefab;
            if (nextTheme != null) theme = nextTheme;
        }

        private void Awake()
        {
            if (placementController == null) placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>();
            if (catalog == null && placementController != null) catalog = placementController.Catalog;
            BuildUi();
            BindOpenButton();
            if (Application.isPlaying || (categoryRoot != null && categoryRoot.childCount == 0)) RebuildCategories();
            // World entry always starts with the catalog closed. It can then be
            // opened explicitly with B or the hierarchy-authored toggle button.
            if (Application.isPlaying) SetVisible(false);
            else if (panelRoot != null) panelRoot.gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && placementController != null) placementController.StateChanged += Refresh;
            OntologyLanguagePackService.LanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable()
        {
            if (Application.isPlaying && placementController != null) placementController.StateChanged -= Refresh;
            OntologyLanguagePackService.LanguageChanged -= HandleLanguageChanged;
        }

        private void BindOpenButton()
        {
            if (openButton == null) return;
            openButton.onClick.RemoveListener(Open);
            openButton.onClick.AddListener(Open);
        }

        public void Open() => SetVisible(true);
        public void Close() => SetVisible(false);

        private void HandleLanguageChanged()
        {
            ApplyLocalizedStaticLabels();
            RebuildCategories();
            Refresh();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            // ESC closes the catalog before placement begins. Once the catalog is
            // hidden, the placement controller keeps ownership of ESC and can
            // cancel the in-world preview normally.
            if (visible && WasClosePressed())
            {
                SetVisible(false);
                return;
            }
            if (WasTogglePressed()) SetVisible(!visible);
        }

        private bool WasClosePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        private bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return false;
            return System.Enum.TryParse<Key>(toggleKey.ToString(), out var key)
                && Keyboard.current[key].wasPressedThisFrame;
#else
            return false;
#endif
        }

        private void BuildUi()
        {
            var existing = transform.Find("ObjectPlacementHUD") as RectTransform;
            if (existing == null && panelPrefab != null)
            {
                existing = Instantiate(panelPrefab, transform).GetComponent<RectTransform>();
                if (existing != null) existing.name = "ObjectPlacementHUD";
            }
            if (existing == null)
            {
                Debug.LogError("Object Placement HUD prefab is not assigned.", this);
                return;
            }
            panelRoot = existing;
            BindExistingUi();
            ApplyLocalizedStaticLabels();
        }

        private void BindExistingUi()
        {
            categoryRoot =
                panelRoot.Find("Categories/CategoryScroll/Viewport/CategoryList") as RectTransform ??
                panelRoot.Find("Categories/CategoryList") as RectTransform;
            categoryButtonTemplate = categoryRoot?.Find("CategoryButtonTemplate")?.GetComponent<Button>();
            objectTab = panelRoot.Find("ModeTabs/ObjectTab")?.GetComponent<Button>();
            npcTab = panelRoot.Find("ModeTabs/NpcTab")?.GetComponent<Button>();
            monsterTab = panelRoot.Find("ModeTabs/MonsterTab")?.GetComponent<Button>();
            var previewFrame = panelRoot.Find("PreviewFrame");
            previewImage = previewFrame == null ? null : previewFrame.Find("Preview")?.GetComponent<RawImage>();
            nameText =
                panelRoot.Find("OntologyPanel/Name")?.GetComponent<TMP_Text>() ??
                (previewFrame == null ? null : previewFrame.Find("Name")?.GetComponent<TMP_Text>());
            detailText =
                panelRoot.Find("OntologyPanel/Detail")?.GetComponent<TMP_Text>() ??
                (previewFrame == null ? null : previewFrame.Find("Detail")?.GetComponent<TMP_Text>());
            pageText = previewFrame == null ? null : previewFrame.Find("Page")?.GetComponent<TMP_Text>();
            var ontologyContent = panelRoot.Find("OntologyPanel/OntologyScroll/Viewport/Content");
            definitionIdText = ontologyContent?.Find("DefinitionIdRow/Value")?.GetComponent<TMP_Text>();
            conceptsText = ontologyContent?.Find("ConceptsRow/Value")?.GetComponent<TMP_Text>();
            placementSurfaceText = ontologyContent?.Find("PlacementSurfaceRow/Value")?.GetComponent<TMP_Text>();
            ruleBlocksText = ontologyContent?.Find("RuleBlocksRow/Value")?.GetComponent<TMP_Text>();
            previewRenderer = previewFrame == null ? null : previewFrame.GetComponent<OntologyPlaceablePreviewRenderer>();
            if (previewRenderer != null) previewRenderer.Configure(previewImage);
            previousButton = previewFrame?.Find("Previous")?.GetComponent<Button>();
            nextButton = previewFrame?.Find("Next")?.GetComponent<Button>();
            selectButton = panelRoot.Find("Select")?.GetComponent<Button>();
            statusText = panelRoot.Find("Status")?.GetComponent<TMP_Text>();
            if (Application.isPlaying)
            {
                objectTab?.onClick.AddListener(() => SelectKind(OntologyPlaceableKind.Object));
                npcTab?.onClick.AddListener(() => SelectKind(OntologyPlaceableKind.Npc));
                monsterTab?.onClick.AddListener(() => SelectKind(OntologyPlaceableKind.Monster));
                previousButton?.onClick.AddListener(() => Browse(-1));
                nextButton?.onClick.AddListener(() => Browse(1));
                selectButton?.onClick.AddListener(SelectCurrent);
                (panelRoot.Find("CloseIconButton") ?? panelRoot.Find("Close"))
                    ?.GetComponent<Button>()?.onClick.AddListener(Close);
            }
        }

        private void ApplyLocalizedStaticLabels()
        {
            if (panelRoot == null) return;
            OntologyLanguagePackService.EnsureKoreanFontFallback(panelRoot);
            SetText(
                panelRoot.Find("Title"),
                "placement.ui.world_title",
                "WORLD PLACEMENT");
            SetText(
                panelRoot.Find("Categories/Title"),
                "placement.ui.categories",
                "CATEGORIES");
            SetText(panelRoot.Find("ModeTabs/ObjectTab"), "placement.ui.mode.object", "OBJECT");
            SetText(panelRoot.Find("ModeTabs/NpcTab"), "placement.ui.mode.npc", "NPC");
            SetText(panelRoot.Find("ModeTabs/MonsterTab"), "placement.ui.mode.monster", "MONSTER");
            SetText(panelRoot.Find("OntologyPanel/OntologyHeading"), "placement.ui.ontology_data", "ONTOLOGY DATA");
            SetText(
                panelRoot.Find("OntologyPanel/OntologyScroll/Viewport/Content/DefinitionIdRow/Label"),
                "placement.ui.definition_id",
                "DEFINITION ID");
            SetText(
                panelRoot.Find("OntologyPanel/OntologyScroll/Viewport/Content/ConceptsRow/Label"),
                "placement.ui.concepts",
                "CONCEPTS");
            SetText(
                panelRoot.Find("OntologyPanel/OntologyScroll/Viewport/Content/PlacementSurfaceRow/Label"),
                "placement.ui.placement_surface",
                "PLACEMENT SURFACE");
            SetText(
                panelRoot.Find("OntologyPanel/OntologyScroll/Viewport/Content/RuleBlocksRow/Label"),
                "placement.ui.rule_blocks",
                "RULE BLOCKS");
            SetText(
                panelRoot.Find("Select"),
                "placement.ui.select",
                "PLACE");
        }

        private static void SetText(
            Transform target,
            string key,
            string fallback)
        {
            var label = target?.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = OntologyLanguagePackService.Text(key, fallback);
        }

        private void RebuildCategories()
        {
            categories = catalog == null
                ? new List<string>()
                : catalog.Definitions
                    .Where(d => d != null && d.IsValid && d.placementKind == selectedKind)
                    .Select(d => string.IsNullOrWhiteSpace(d.category) ? "Other" : d.category)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();
            if (!categories.Contains(selectedCategory)) selectedCategory = categories.Count == 0 ? null : categories[0];
            // Reuse authored category buttons first. This keeps the user's Scene/Prefab
            // styling (sprites, colors, sizes and typography) instead of deleting it and
            // rebuilding a visually different runtime copy.
            var authoredButtons = categoryRoot == null
                ? new List<Button>()
                : categoryRoot.GetComponentsInChildren<Button>(true)
                    .Where(button => button != categoryButtonTemplate &&
                                     button.transform.parent == categoryRoot &&
                                     button.name.StartsWith("Category_"))
                    .ToList();
            foreach (var authoredButton in authoredButtons)
            {
                authoredButton.onClick.RemoveAllListeners();
                authoredButton.gameObject.SetActive(false);
            }
            categoryButtons.Clear();
            if (categoryButtonTemplate == null)
            {
                Debug.LogError("[OntologyObjectPlacementPanel] CategoryButtonTemplate is missing from ObjectPlacementHUD hierarchy.", this);
                return;
            }

            for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                var category = categories[categoryIndex];
                var button = categoryIndex < authoredButtons.Count
                    ? authoredButtons[categoryIndex]
                    : Instantiate(categoryButtonTemplate, categoryRoot);
                button.gameObject.SetActive(true);
                button.name = "Category_" + category;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = OntologyLanguagePackService.Category(category);
                var captured = category;
                button.onClick.AddListener(() => SelectCategory(captured));
                categoryButtons.Add(button);
            }
            selectedIndex = 0; Refresh();
        }

        private void SelectCategory(string category)
        {
            selectedCategory = category; selectedIndex = 0; Refresh();
        }

        private void SelectKind(OntologyPlaceableKind kind)
        {
            if (selectedKind == kind) return;
            selectedKind = kind;
            selectedCategory = null;
            selectedIndex = 0;
            previewedDefinitionId = null;
            if (previewRenderer != null) previewRenderer.Clear();
            RebuildCategories();
        }

        [ContextMenu("Refresh Category Buttons From Catalog")]
        private void RefreshCategoryButtonsFromCatalog()
        {
            RebuildCategories();
        }

        private List<OntologyPlaceableDefinition> CurrentEntries() => catalog == null
            ? new List<OntologyPlaceableDefinition>()
            : catalog.Definitions
                .Where(d => d != null && d.IsValid &&
                            d.placementKind == selectedKind &&
                            (string.IsNullOrWhiteSpace(d.category) ? "Other" : d.category) == selectedCategory)
                .ToList();

        private void Browse(int direction)
        {
            var entries = CurrentEntries(); if (entries.Count == 0) return;
            selectedIndex = (selectedIndex + direction + entries.Count) % entries.Count; Refresh();
        }

        private void SelectCurrent()
        {
            var entries = CurrentEntries(); if (entries.Count == 0 || placementController == null) return;
            if (placementController.BeginPlacement(entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)])) SetVisible(false);
        }

        private void Refresh()
        {
            var entries = CurrentEntries(); if (entries.Count == 0)
            {
                if (nameText != null)
                    nameText.text = OntologyLanguagePackService.Text(
                        "placement.empty." + selectedKind.ToString().ToLowerInvariant() + ".title",
                        "No " + selectedKind.ToString().ToLowerInvariant() + " entries");
                if (detailText != null)
                    detailText.text = OntologyLanguagePackService.Text(
                        "placement.empty." + selectedKind.ToString().ToLowerInvariant() + ".description",
                        Theme.emptyCatalogDescription);
                SetOntologySummary(null);
                if (pageText != null) pageText.text = "0 / 0";
                if (selectButton != null) selectButton.interactable = false;
                if (previousButton != null) previousButton.interactable = false;
                if (nextButton != null) nextButton.interactable = false;
                RefreshSelectionVisuals();
                return;
            }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, entries.Count - 1); var current = entries[selectedIndex];
            if (selectButton != null) selectButton.interactable = true;
            if (previousButton != null) previousButton.interactable = entries.Count > 1;
            if (nextButton != null) nextButton.interactable = entries.Count > 1;
            if (nameText != null) nameText.text = current.LocalizedDisplayName;
            if (detailText != null)
                detailText.text =
                    OntologyLanguagePackService.PlaceableDescription(current);
            if (pageText != null) pageText.text = (selectedIndex + 1) + " / " + entries.Count;
            SetOntologySummary(current);
            // State text changes during placement are frequent. Recreate the 3D model only
            // when the player actually browses to a different catalog entry.
            if (Application.isPlaying && previewRenderer != null && previewedDefinitionId != current.definitionId)
            {
                previewRenderer.Show(current);
                previewedDefinitionId = current.definitionId;
            }
            RefreshSelectionVisuals();
            RefreshStatus();
        }

        private void SetOntologySummary(OntologyPlaceableDefinition definition)
        {
            if (definitionIdText != null)
                definitionIdText.text = definition?.definitionId ?? "—";
            if (conceptsText != null)
                conceptsText.text = definition?.ontologyTemplate?.concepts == null ||
                                    definition.ontologyTemplate.concepts.Length == 0
                    ? "None"
                    : string.Join("  ·  ", definition.ontologyTemplate.concepts);
            if (placementSurfaceText != null)
                placementSurfaceText.text = definition?.placementPolicy == null
                    ? "—"
                    : SplitPascalCase(definition.placementPolicy.requiredSurface.ToString());
            if (ruleBlocksText != null)
                ruleBlocksText.text = definition?.defaultRuleBlocks == null ||
                                      definition.defaultRuleBlocks.Count == 0
                    ? "None"
                    : string.Join(
                        "\n",
                        definition.defaultRuleBlocks
                            .Where(binding => binding != null && !string.IsNullOrWhiteSpace(binding.ruleId))
                            .Select(binding => binding.ruleId));
        }

        private void RefreshSelectionVisuals()
        {
            SetSelectedVisual(objectTab, selectedKind == OntologyPlaceableKind.Object);
            SetSelectedVisual(npcTab, selectedKind == OntologyPlaceableKind.Npc);
            SetSelectedVisual(monsterTab, selectedKind == OntologyPlaceableKind.Monster);
            for (var index = 0; index < categoryButtons.Count && index < categories.Count; index++)
                SetSelectedVisual(categoryButtons[index], categories[index] == selectedCategory);
        }

        private static void SetSelectedVisual(Button button, bool selected)
        {
            if (button == null) return;
            var selectedState = button.transform.Find("SelectedState");
            var unselectedState = button.transform.Find("UnselectedState");
            if (selectedState != null) selectedState.gameObject.SetActive(selected);
            if (unselectedState != null) unselectedState.gameObject.SetActive(!selected);
            if (selectedState == null && unselectedState == null && button.targetGraphic is Image image)
                image.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.94f);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = selected ? Color.white : new Color32(22, 53, 72, 255);
        }

        private static string SplitPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            return System.Text.RegularExpressions.Regex.Replace(value, "(\\B[A-Z])", " $1");
        }

        private void SetVisible(bool value) { visible = value; if (panelRoot != null) panelRoot.gameObject.SetActive(value); }
        private void RefreshStatus() { if (statusText != null && placementController != null) statusText.text = placementController.Status; }

        private OntologyObjectPlacementUiTheme Theme => theme;
    }
}
