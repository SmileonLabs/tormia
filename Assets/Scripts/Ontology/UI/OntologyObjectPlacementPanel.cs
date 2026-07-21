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
        [SerializeField] private KeyCode toggleKey = KeyCode.B;
        [SerializeField] private bool startsVisible;

        private RectTransform panelRoot;
        private RectTransform categoryRoot;
        private Button categoryButtonTemplate;
        private RawImage previewImage;
        private TMP_Text nameText;
        private TMP_Text detailText;
        private TMP_Text pageText;
        private TMP_Text statusText;
        private OntologyPlaceablePreviewRenderer previewRenderer;
        private readonly List<Button> categoryButtons = new();
        private List<string> categories = new();
        private string selectedCategory;
        private int selectedIndex;
        private bool visible;
        private string previewedDefinitionId;

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
            if (Application.isPlaying || (categoryRoot != null && categoryRoot.childCount == 0)) RebuildCategories();
            if (Application.isPlaying) SetVisible(startsVisible);
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
            categoryRoot = panelRoot.Find("Categories/CategoryList") as RectTransform;
            categoryButtonTemplate = panelRoot.Find("Categories/CategoryList/CategoryButtonTemplate")?.GetComponent<Button>();
            var previewFrame = panelRoot.Find("PreviewFrame");
            previewImage = previewFrame == null ? null : previewFrame.Find("Preview")?.GetComponent<RawImage>();
            nameText = previewFrame == null ? null : previewFrame.Find("Name")?.GetComponent<TMP_Text>();
            detailText = previewFrame == null ? null : previewFrame.Find("Detail")?.GetComponent<TMP_Text>();
            pageText = previewFrame == null ? null : previewFrame.Find("Page")?.GetComponent<TMP_Text>();
            previewRenderer = previewFrame == null ? null : previewFrame.GetComponent<OntologyPlaceablePreviewRenderer>();
            if (previewRenderer != null) previewRenderer.Configure(previewImage);
            statusText = panelRoot.Find("Status")?.GetComponent<TMP_Text>();
            if (Application.isPlaying)
            {
                previewFrame?.Find("Previous")?.GetComponent<Button>()?.onClick.AddListener(() => Browse(-1));
                previewFrame?.Find("Next")?.GetComponent<Button>()?.onClick.AddListener(() => Browse(1));
                panelRoot.Find("Select")?.GetComponent<Button>()?.onClick.AddListener(SelectCurrent);
                panelRoot.Find("Close")?.GetComponent<Button>()?.onClick.AddListener(() => SetVisible(false));
            }
        }

        private void ApplyLocalizedStaticLabels()
        {
            if (panelRoot == null) return;
            OntologyLanguagePackService.EnsureKoreanFontFallback(panelRoot);
            SetText(
                panelRoot.Find("Categories/Title"),
                "placement.ui.categories",
                "CATEGORIES");
            SetText(
                panelRoot.Find("Select"),
                "placement.ui.select",
                "SELECT");
            SetText(
                panelRoot.Find("Close"),
                "placement.ui.close",
                "CLOSE");
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
            categories = catalog == null ? new List<string>() : catalog.Definitions.Where(d => d != null && d.IsValid).Select(d => string.IsNullOrWhiteSpace(d.category) ? "Other" : d.category).Distinct().OrderBy(v => v).ToList();
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

        [ContextMenu("Refresh Category Buttons From Catalog")]
        private void RefreshCategoryButtonsFromCatalog()
        {
            RebuildCategories();
        }

        private List<OntologyPlaceableDefinition> CurrentEntries() => catalog == null ? new List<OntologyPlaceableDefinition>() : catalog.Definitions.Where(d => d != null && d.IsValid && (string.IsNullOrWhiteSpace(d.category) ? "Other" : d.category) == selectedCategory).ToList();

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
                        "placement.empty.title",
                        Theme.emptyCatalogTitle);
                if (detailText != null)
                    detailText.text = OntologyLanguagePackService.Text(
                        "placement.empty.description",
                        Theme.emptyCatalogDescription);
                return;
            }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, entries.Count - 1); var current = entries[selectedIndex];
            if (nameText != null) nameText.text = current.LocalizedDisplayName;
            if (detailText != null)
                detailText.text =
                    OntologyLanguagePackService.PlaceableDescription(current);
            if (pageText != null) pageText.text = (selectedIndex + 1) + " / " + entries.Count;
            // State text changes during placement are frequent. Recreate the 3D model only
            // when the player actually browses to a different catalog entry.
            if (Application.isPlaying && previewRenderer != null && previewedDefinitionId != current.definitionId)
            {
                previewRenderer.Show(current);
                previewedDefinitionId = current.definitionId;
            }
            for (var i = 0; i < categoryButtons.Count; i++) categoryButtons[i].GetComponent<Image>().color = categories[i] == selectedCategory ? Theme.categorySelectedColor : Theme.categoryNormalColor;
            RefreshStatus();
        }

        private void SetVisible(bool value) { visible = value; if (panelRoot != null) panelRoot.gameObject.SetActive(value); }
        private void RefreshStatus() { if (statusText != null && placementController != null) statusText.text = placementController.Status; }

        private OntologyObjectPlacementUiTheme Theme => theme;
    }
}
