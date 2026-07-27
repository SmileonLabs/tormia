using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyCharacterCustomizationPanel : MonoBehaviour
    {
        [SerializeField] private OntologyCharacterPartAdapter partAdapter;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private OntologyCharacterCategoryIconSet categoryIconSet;
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private KeyCode toggleKey = KeyCode.C;
        [SerializeField] private bool startsVisible = false;
        [SerializeField] private RectTransform categoryContainer;
        [SerializeField] private RectTransform partGridContainer;
        [SerializeField] private Button categoryButtonTemplate;
        [SerializeField] private Button partCardTemplate;
        [SerializeField] private Image selectedIcon;
        [SerializeField] private TMP_Text selectedTitle;
        [SerializeField] private TMP_Text selectedDescription;
        [SerializeField] private TMP_Text factPreview;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text toggleHintText;
        [SerializeField] private Button openButton;
        [SerializeField] private Button equipButton;
        [SerializeField] private Button unequipButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool equipOnPartSelection;
        [SerializeField] private bool allowRuntimeToggle = true;
        [SerializeField] private bool persistAccountAppearanceOnClose;
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow accountEntryFlow;

        private string selectedCategory = string.Empty;
        private string selectedPartId = string.Empty;
        private bool isVisible;
        private Action closedFromAccountCreation;
        private bool embeddedInAccountCharacterCreation;

        public bool IsVisible => isVisible;
        public bool RuntimeToggleEnabled => allowRuntimeToggle;
        public bool PersistsAccountAppearanceOnClose => persistAccountAppearanceOnClose;
        public bool EquipOnPartSelection => equipOnPartSelection;
        public bool HasCategoryIcons => categoryIconSet != null;
        public OntologyCharacterPartAdapter PartAdapter => partAdapter;
        public GameObject RuntimeToggleObject => openButton != null
            ? openButton.gameObject
            : toggleHintText != null ? toggleHintText.gameObject : null;

        /// <summary>Opens the existing authored part picker as a step in account character creation.</summary>
        public void OpenForAccountCharacterCreation(Action completed)
        {
            embeddedInAccountCharacterCreation = false;
            closedFromAccountCreation = completed;
            EnsureReferences();
            partAdapter?.EnsureAppearanceInitialized();
            SetVisible(true);
            Rebuild();
        }

        /// <summary>
        /// Opens this hierarchy-authored picker as the appearance layer of the
        /// account character-creation screen. The account panel owns navigation;
        /// this component continues to own only part selection and presentation.
        /// </summary>
        public void OpenEmbeddedForAccountCharacterCreation()
        {
            embeddedInAccountCharacterCreation = true;
            closedFromAccountCreation = null;
            EnsureReferences();
            partAdapter?.EnsureAppearanceInitialized();
            SetVisible(true);
            if (closeButton != null) closeButton.gameObject.SetActive(false);
            Rebuild();
        }

        public void CloseEmbeddedForAccountCharacterCreation()
        {
            if (!embeddedInAccountCharacterCreation) return;
            embeddedInAccountCharacterCreation = false;
            if (closeButton != null) closeButton.gameObject.SetActive(true);
            SetVisible(false);
        }

        private void Awake()
        {
            EnsureReferences();
            BindHierarchyReferences();
            var editorPreview = transform.Find("EditorPreviewContent");
            if (editorPreview != null) editorPreview.gameObject.SetActive(false);
            ApplyLocalizedStaticLabels();
            HookButtons();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable()
        {
            OntologyLanguagePackService.LanguageChanged -= HandleLanguageChanged;
        }

        private void HandleLanguageChanged()
        {
            BindHierarchyReferences();
            ApplyLocalizedStaticLabels();
            Rebuild();
        }

        private void Start()
        {
            Rebuild();
        }

        private void Update()
        {
            if (allowRuntimeToggle && !embeddedInAccountCharacterCreation && WasTogglePressed())
            {
                SetVisible(!isVisible);
            }
        }

        private bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (toggleKey)
                {
                    case KeyCode.C:
                        return Keyboard.current.cKey.wasPressedThisFrame;
                    case KeyCode.I:
                        return Keyboard.current.iKey.wasPressedThisFrame;
                    case KeyCode.Tab:
                        return Keyboard.current.tabKey.wasPressedThisFrame;
                    case KeyCode.Escape:
                        return Keyboard.current.escapeKey.wasPressedThisFrame;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(toggleKey);
#else
            return false;
#endif
        }

        public void Rebuild()
        {
            EnsureReferences();
            if (partDatabase == null || partGridContainer == null || categoryContainer == null)
            {
                SetStatus(L(
                    "character.status.missing_data",
                    "Character customization data is missing."));
                return;
            }

            var categories = BuildCategories();
            if (string.IsNullOrWhiteSpace(selectedCategory) && categories.Count > 0)
            {
                selectedCategory = categories[0];
            }

            ClearContainer(categoryContainer);
            if (categoryButtonTemplate != null)
            {
                categoryButtonTemplate.gameObject.SetActive(false);
            }
            if (partCardTemplate != null)
            {
                partCardTemplate.gameObject.SetActive(false);
            }

            foreach (var category in categories)
            {
                CreateCategoryButton(category);
            }

            RefreshPartGrid();
            RefreshSelectedDetails();
            GetComponent<OntologyCharacterCreationPreviewPresenter>()?.SynchronizeNow();
        }

        private void RefreshPartGrid()
        {
            ClearContainer(partGridContainer);
            if (partDatabase == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null
                    || !definition.visibleInCustomization
                    || string.IsNullOrWhiteSpace(definition.partId)
                    || definition.slot != selectedCategory)
                {
                    continue;
                }

                CreatePartCard(definition);
            }
        }

        private void RefreshSelectedDetails()
        {
            var definition = FindDefinition(selectedPartId);
            var emptySelectionIcon = selectedIcon == null
                ? null
                : selectedIcon.transform.parent.Find("EmptySelectionIcon");
            if (emptySelectionIcon != null)
                emptySelectionIcon.gameObject.SetActive(definition == null);
            if (definition == null)
            {
                if (selectedTitle != null) selectedTitle.text = OntologyCharacterCustomizationUiConfig.SelectPartTitle;
                if (selectedDescription != null) selectedDescription.text = OntologyCharacterCustomizationUiConfig.SelectPartDescription;
                if (factPreview != null) factPreview.text = string.Empty;
                if (selectedIcon != null)
                {
                    selectedIcon.sprite = null;
                    selectedIcon.enabled = false;
                }
                RefreshActionButtons(null);
                return;
            }

            var displayName = GetDisplayName(definition);
            if (selectedTitle != null) selectedTitle.text = displayName;
            if (selectedDescription != null)
            {
                selectedDescription.text = BuildPlayerSummary(definition);
            }

            if (selectedIcon != null)
            {
                selectedIcon.sprite = definition.icon;
                selectedIcon.enabled = definition.icon != null;
            }

            if (factPreview != null)
            {
                factPreview.text = BuildFactPreview(definition);
            }

            RefreshActionButtons(definition);
        }

        private void RefreshActionButtons(OntologyCharacterPartDefinition definition)
        {
            if (partAdapter == null || definition == null)
            {
                if (equipButton != null) equipButton.interactable = false;
                if (unequipButton != null) unequipButton.interactable = false;
                return;
            }

            if (equipButton != null)
            {
                equipButton.interactable = partAdapter.CanEquipPart(definition.partId, out _);
            }

            if (unequipButton != null)
            {
                unequipButton.interactable = partAdapter.CanUnequipPart(definition.partId, out _);
            }
        }

        private void EquipSelected()
        {
            var definition = FindDefinition(selectedPartId);
            if (partAdapter == null || definition == null)
            {
                SetStatus(L(
                    "character.status.select_valid_part",
                    "Select a valid part first."));
                return;
            }

            if (!partAdapter.EquipPart(definition.partId))
            {
                partAdapter.CanEquipPart(definition.partId, out var reason);
                SetStatus(string.Format(
                    L("character.status.equip_failed", "Equip failed: {0} ({1})"),
                    GetDisplayName(definition),
                    FormatReason(reason)));
                RefreshSelectedDetails();
                return;
            }

            SetStatus(string.Format(
                L("character.status.equipped", "Equipped: {0}"),
                GetDisplayName(definition)));
            Rebuild();
        }

        private void UnequipSelected()
        {
            var definition = FindDefinition(selectedPartId);
            if (partAdapter == null || definition == null)
            {
                SetStatus(L(
                    "character.status.select_valid_part",
                    "Select a valid part first."));
                return;
            }

            if (!partAdapter.UnequipPart(definition.partId))
            {
                partAdapter.CanUnequipPart(definition.partId, out var reason);
                SetStatus(string.Format(
                    L("character.status.unequip_failed", "Unequip failed: {0} ({1})"),
                    GetDisplayName(definition),
                    FormatReason(reason)));
                RefreshSelectedDetails();
                return;
            }

            SetStatus(string.Format(
                L("character.status.unequipped", "Unequipped: {0}"),
                GetDisplayName(definition)));
            Rebuild();
        }

        private void CreateCategoryButton(string category)
        {
            if (categoryButtonTemplate == null)
            {
                Debug.LogError("[OntologyCharacterCustomizationPanel] CategoryButtonTemplate is not assigned in the hierarchy.", this);
                return;
            }

            var button = Instantiate(categoryButtonTemplate, categoryContainer);
            button.name = "Category_" + category;
            button.gameObject.SetActive(true);
            SetButtonLabel(
                button,
                OntologyCharacterCustomizationUiConfig.SlotLabel(category));
            var icon = button.transform.Find(OntologyCharacterCustomizationUiConfig.IconName)
                ?.GetComponent<Image>();
            var hasIcon = false;
            if (icon != null)
            {
                icon.sprite = categoryIconSet == null ? null : categoryIconSet.GetIcon(category);
                hasIcon = icon.sprite != null;
                icon.enabled = hasIcon;
                icon.preserveAspect = true;
                icon.color = category == selectedCategory
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.88f);
                icon.rectTransform.localScale = category == selectedCategory
                    ? Vector3.one
                    : Vector3.one * 0.92f;
            }
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                // The category menu is icon-only. Keep the localized label as a safe
                // fallback when a category icon is missing or intentionally removed.
                label.gameObject.SetActive(!hasIcon);
            }
            var buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.sprite = null;
                buttonImage.color = Color.clear;
                buttonImage.raycastTarget = true;
                button.targetGraphic = buttonImage;
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                selectedCategory = category;
                selectedPartId = string.Empty;
                Rebuild();
            });
        }

        private void CreatePartCard(OntologyCharacterPartDefinition definition)
        {
            var isEquipped = partAdapter != null && partAdapter.IsPartEquipped(definition.partId);
            var isSelected = definition.partId == selectedPartId;
            var hasConflict = HasConflictFact(definition);
            var hasCapability = HasCapabilityFact(definition);
            if (partCardTemplate == null)
            {
                Debug.LogError("[OntologyCharacterCustomizationPanel] PartCardTemplate is not assigned in the hierarchy.", this);
                return;
            }

            var button = Instantiate(partCardTemplate, partGridContainer);
            button.name = "Part_" + definition.partId;
            button.gameObject.SetActive(true);
            var rect = (RectTransform)button.transform;

            // The card itself remains transparent so only the circular thumbnail
            // reads as the control. State is communicated by the circular ring.
            var cardImage = button.GetComponent<Image>();
            if (cardImage != null)
            {
                cardImage.sprite = null;
                cardImage.color = Color.clear;
                cardImage.raycastTarget = true;
                button.targetGraphic = cardImage;
            }
            var selectionOutline = rect.Find("SelectionOutline")?.GetComponent<Image>();
            if (selectionOutline != null)
            {
                selectionOutline.gameObject.SetActive(isSelected || isEquipped);
                selectionOutline.color = isEquipped
                    ? new Color(0.64f, 0.9f, 0.49f, 1f)
                    : new Color(0.95f, 0.57f, 0.17f, 1f);
            }
            var iconTransform = rect.Find("ThumbnailMask/" + OntologyCharacterCustomizationUiConfig.IconName)
                ?? rect.Find(OntologyCharacterCustomizationUiConfig.IconName);
            var icon = iconTransform?.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = definition.icon;
                icon.enabled = definition.icon != null;
            }

            var fallback = rect.Find(OntologyCharacterCustomizationUiConfig.NoIconName)?.GetComponent<TextMeshProUGUI>();
            if (fallback != null)
            {
                fallback.text = OntologyCharacterCustomizationUiConfig.NoIconLabel;
                // Part cards are intentionally icon-only. A missing icon leaves the
                // circular thumbnail empty instead of introducing runtime-only text.
                fallback.gameObject.SetActive(false);
            }

            var label = rect.Find(OntologyCharacterCustomizationUiConfig.LabelName)?.GetComponent<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = GetDisplayName(definition);
                label.gameObject.SetActive(false);
            }

            var state = rect.Find(OntologyCharacterCustomizationUiConfig.StateName)?.GetComponent<TextMeshProUGUI>();
            if (state != null)
            {
                state.text = isEquipped
                    ? OntologyCharacterCustomizationUiConfig.EquippedLabel
                    : OntologyCharacterCustomizationUiConfig.SlotLabel(definition.slot);
                state.color = isEquipped ? OntologyCharacterCustomizationUiConfig.EquippedTextColor : OntologyCharacterCustomizationUiConfig.MutedTextColor;
                state.gameObject.SetActive(false);
            }

            var badge = rect.Find(OntologyCharacterCustomizationUiConfig.BadgeName)?.GetComponent<TextMeshProUGUI>();
            if (badge != null)
            {
                badge.text = GetBadgeText(isEquipped, hasCapability, hasConflict);
                badge.gameObject.SetActive(false);
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                selectedPartId = definition.partId;
                if (!equipOnPartSelection || partAdapter == null)
                {
                    RefreshPartGrid();
                    RefreshSelectedDetails();
                    return;
                }

                if (partAdapter.IsPartEquipped(definition.partId))
                {
                    if (partAdapter.UnequipPart(definition.partId))
                    {
                        SetStatus(string.Format(
                            L("character.status.unequipped", "Unequipped: {0}"),
                            GetDisplayName(definition)));
                        Rebuild();
                        return;
                    }

                    partAdapter.CanUnequipPart(definition.partId, out var unequipReason);
                    SetStatus(string.Format(
                        L("character.status.unequip_failed", "Unequip failed: {0} ({1})"),
                        GetDisplayName(definition),
                        FormatReason(unequipReason)));
                    RefreshSelectedDetails();
                    return;
                }

                if (partAdapter.EquipPart(definition.partId))
                {
                    SetStatus(string.Format(
                        L("character.status.equipped", "Equipped: {0}"),
                        GetDisplayName(definition)));
                    Rebuild();
                    return;
                }

                partAdapter.CanEquipPart(definition.partId, out var equipReason);
                SetStatus(string.Format(
                    L("character.status.equip_failed", "Equip failed: {0} ({1})"),
                    GetDisplayName(definition),
                    FormatReason(equipReason)));
                RefreshSelectedDetails();
            });
        }

        private List<string> BuildCategories()
        {
            var categories = new List<string>();
            if (partDatabase == null)
            {
                return categories;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null
                    || !definition.visibleInCustomization
                    || string.IsNullOrWhiteSpace(definition.slot)
                    || categories.Contains(definition.slot))
                {
                    continue;
                }

                categories.Add(definition.slot);
            }

            categories.Sort(CompareCategories);
            return categories;
        }

        private static string GetBadgeText(bool isEquipped, bool hasCapability, bool hasConflict)
        {
            if (isEquipped)
            {
                return OntologyCharacterCustomizationUiConfig.OnBadge;
            }

            if (hasConflict)
            {
                return OntologyCharacterCustomizationUiConfig.ConflictBadge;
            }

            return hasCapability ? OntologyCharacterCustomizationUiConfig.CapabilityBadge : string.Empty;
        }

        private static bool HasCapabilityFact(OntologyCharacterPartDefinition definition)
        {
            if (definition.facts == null)
            {
                return false;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == OntologyPredicates.GrantsCapability)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasConflictFact(OntologyCharacterPartDefinition definition)
        {
            if (definition.facts == null)
            {
                return false;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == OntologyPredicates.ConflictsWithSlot)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareCategories(string left, string right)
        {
            var leftIndex = GetCategoryOrderIndex(left);
            var rightIndex = GetCategoryOrderIndex(right);
            if (leftIndex != rightIndex)
            {
                return leftIndex.CompareTo(rightIndex);
            }

            return string.Compare(left, right, System.StringComparison.Ordinal);
        }

        private static int GetCategoryOrderIndex(string category)
        {
            for (var i = 0; i < OntologyCharacterCustomizationUiConfig.CategoryOrder.Length; i++)
            {
                if (OntologyCharacterCustomizationUiConfig.CategoryOrder[i] == category)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private string BuildFactPreview(OntologyCharacterPartDefinition definition)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(OntologyCharacterCustomizationUiConfig.FactsHeader).Append('\n');
            builder.Append(L("entity.Player", "Player"))
                .Append(' ')
                .Append(OntologyLanguagePackService.Term(
                    OntologyPredicates.EquippedPart))
                .Append(' ')
                .Append(GetDisplayName(definition));
            if (definition.facts != null)
            {
                foreach (var fact in definition.facts)
                {
                    if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                    {
                        continue;
                    }

                    builder.Append('\n')
                        .Append(GetDisplayName(definition))
                        .Append(' ')
                        .Append(OntologyLanguagePackService.Term(fact.predicate))
                        .Append(' ')
                        .Append(OntologyLanguagePackService.Term(fact.obj));
                }
            }

            return builder.ToString();
        }

        private string BuildPlayerSummary(OntologyCharacterPartDefinition definition)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(L("character.detail.category", "Category"))
                .Append(": ")
                .Append(OntologyCharacterCustomizationUiConfig.SlotLabel(
                    definition.slot));
            builder.Append("\n")
                .Append(L("character.detail.status", "Status"))
                .Append(": ")
                .Append(GetEquipPreview(definition));

            var effects = GetEffectSummary(definition);
            if (!string.IsNullOrWhiteSpace(effects))
            {
                builder.Append("\n").Append(effects);
            }

            return builder.ToString();
        }

        private string GetEquipPreview(OntologyCharacterPartDefinition definition)
        {
            if (partAdapter == null)
            {
                return L(
                    "character.preview.no_adapter",
                    "No runtime adapter.");
            }

            if (partAdapter.IsPartEquipped(definition.partId))
            {
                return L(
                    "character.preview.already_equipped",
                    "Already equipped.");
            }

            var replacement = FindEquippedInSlot(definition.slot);
            if (replacement != null)
            {
                return string.Format(
                    L("character.preview.will_replace", "Will replace {0}."),
                    GetDisplayName(replacement));
            }

            return L(
                "character.preview.available",
                "Available to equip.");
        }

        private string GetEffectSummary(OntologyCharacterPartDefinition definition)
        {
            if (definition.facts == null || definition.facts.Length == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            var conflicts = new List<string>();
            var capabilities = new List<string>();
            foreach (var fact in definition.facts)
            {
                if (fact == null)
                {
                    continue;
                }

                if (fact.predicate == OntologyPredicates.ConflictsWithSlot)
                {
                    var equipped = FindEquippedInSlot(fact.obj);
                    conflicts.Add(equipped != null
                        ? GetDisplayName(equipped)
                        : OntologyLanguagePackService.Term(fact.obj));
                }
                else if (fact.predicate == OntologyPredicates.GrantsCapability)
                {
                    capabilities.Add(OntologyLanguagePackService.Term(fact.obj));
                }
            }

            if (conflicts.Count > 0)
            {
                builder.Append(L(
                        "character.detail.effects_will_unequip",
                        "Effects: will unequip "))
                    .Append(string.Join(", ", conflicts));
            }

            if (capabilities.Count > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\n");
                }
                builder.Append(L("character.detail.grants", "Grants"))
                    .Append(": ")
                    .Append(string.Join(", ", capabilities));
            }

            return builder.ToString();
        }

        private OntologyCharacterPartDefinition FindEquippedInSlot(string slot)
        {
            if (partAdapter == null || partDatabase == null)
            {
                return null;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && definition.slot == slot && partAdapter.IsPartEquipped(definition.partId))
                {
                    return definition;
                }
            }

            return null;
        }

        private OntologyCharacterPartDefinition FindDefinition(string partId)
        {
            if (partDatabase == null || string.IsNullOrWhiteSpace(partId))
            {
                return null;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && definition.partId == partId)
                {
                    return definition;
                }
            }

            return null;
        }

        private static string GetDisplayName(OntologyCharacterPartDefinition definition)
        {
            return OntologyLanguagePackService.CharacterPartName(
                definition.partId,
                string.IsNullOrWhiteSpace(definition.displayName)
                    ? definition.partId
                    : definition.displayName);
        }

        private static string FormatReason(string reason)
        {
            switch (reason)
            {
                case OntologyCharacterPartAdapter.FailureDefinitionMissing:
                    return L("character.failure.definition_missing", "part definition is missing");
                case OntologyCharacterPartAdapter.FailureRendererMissing:
                    return L("character.failure.renderer_missing", "target renderer is missing");
                case OntologyCharacterPartAdapter.FailureWorldMissing:
                    return L("character.failure.world_missing", "ontology world is not ready");
                case OntologyCharacterPartAdapter.FailureAlreadyEquipped:
                    return L("character.failure.already_equipped", "already equipped");
                case OntologyCharacterPartAdapter.FailureAlreadyUnequipped:
                    return L("character.failure.already_unequipped", "already unequipped");
                case OntologyCharacterPartAdapter.FailureRequiredPart:
                    return L("character.failure.required_part", "this required part cannot be removed");
                default:
                    return string.IsNullOrWhiteSpace(reason)
                        ? L("character.failure.unknown", "unknown reason")
                        : reason;
            }
        }

        private void EnsureReferences()
        {
            if (partAdapter == null)
            {
                partAdapter = OntologyCharacterPartAdapter.FindAvailable();
            }

            if (partDatabase == null && partAdapter != null)
            {
                partDatabase = partAdapter.PartDatabase;
            }
        }

        private void BindHierarchyReferences()
        {
            var rect = GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            panelCanvasGroup ??= GetComponent<CanvasGroup>();

            categoryContainer ??= rect.Find(OntologyCharacterCustomizationUiConfig.CategoryAreaName + "/" + OntologyCharacterCustomizationUiConfig.CategoryScrollViewName + "/" + OntologyCharacterCustomizationUiConfig.CategoryViewportName + "/" + OntologyCharacterCustomizationUiConfig.CategoryContentName) as RectTransform;
            categoryContainer ??= rect.Find(OntologyCharacterCustomizationUiConfig.CategoryAreaName + "/" + OntologyCharacterCustomizationUiConfig.CategoryContentName) as RectTransform;
            partGridContainer ??= rect.Find(OntologyCharacterCustomizationUiConfig.PartGridAreaName + "/" + OntologyCharacterCustomizationUiConfig.ScrollViewName + "/" + OntologyCharacterCustomizationUiConfig.ViewportName + "/" + OntologyCharacterCustomizationUiConfig.PartGridContentName) as RectTransform;
            categoryButtonTemplate ??= rect.Find(OntologyCharacterCustomizationUiConfig.TemplatesName + "/" + OntologyCharacterCustomizationUiConfig.CategoryButtonTemplateName)?.GetComponent<Button>();
            partCardTemplate ??= rect.Find(OntologyCharacterCustomizationUiConfig.TemplatesName + "/" + OntologyCharacterCustomizationUiConfig.PartCardTemplateName)?.GetComponent<Button>();
            closeButton ??= rect.Find(OntologyCharacterCustomizationUiConfig.HeaderName + "/" + OntologyCharacterCustomizationUiConfig.CloseButtonName)?.GetComponent<Button>();
            if (allowRuntimeToggle)
            {
                openButton ??= transform.parent != null ? transform.parent.Find(OntologyCharacterCustomizationUiConfig.ToggleHintName)?.GetComponent<Button>() : null;
                toggleHintText ??= transform.parent != null ? transform.parent.Find(OntologyCharacterCustomizationUiConfig.ToggleHintName)?.GetComponent<TextMeshProUGUI>() : null;
            }
            if (toggleHintText == null && openButton != null)
            {
                toggleHintText = openButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            var detail = rect.Find(OntologyCharacterCustomizationUiConfig.DetailAreaName) as RectTransform;
            if (detail == null)
            {
                return;
            }

            selectedIcon = detail.Find(OntologyCharacterCustomizationUiConfig.SelectedIconName)?.GetComponent<Image>();

            selectedTitle = detail.Find(OntologyCharacterCustomizationUiConfig.SelectedTitleName)?.GetComponent<TextMeshProUGUI>()
                ;

            selectedDescription = detail.Find(OntologyCharacterCustomizationUiConfig.SelectedDescriptionName)?.GetComponent<TextMeshProUGUI>()
                ;

            factPreview = detail.Find(OntologyCharacterCustomizationUiConfig.FactPreviewName)?.GetComponent<TextMeshProUGUI>()
                ;

            equipButton = detail.Find(OntologyCharacterCustomizationUiConfig.EquipButtonName)?.GetComponent<Button>();
            unequipButton = detail.Find(OntologyCharacterCustomizationUiConfig.UnequipButtonName)?.GetComponent<Button>();

            statusText = rect.Find(OntologyCharacterCustomizationUiConfig.StatusName)?.GetComponent<TextMeshProUGUI>()
                ;

            ApplyLocalizedStaticLabels();
        }

        private void ApplyLocalizedStaticLabels()
        {
            OntologyLanguagePackService.EnsureKoreanFontFallback(transform);
            var title = transform.Find(
                    OntologyCharacterCustomizationUiConfig.HeaderName + "/" +
                    OntologyCharacterCustomizationUiConfig.TitleTextName)
                ?.GetComponent<TMP_Text>();
            if (title != null)
                title.text = OntologyCharacterCustomizationUiConfig.Title;
            SetHierarchyText("CategoryArea/CategorySectionLabel",
                L("character.ui.section.style", "STYLE"));
            SetHierarchyText("PartGridArea/PartSectionLabel",
                L("character.ui.section.parts", "PARTS"));
            SetHierarchyText("DetailArea/DetailSectionLabel",
                L("character.ui.section.selected", "SELECTED LOOK"));
            SetHierarchyText("PreviewLabel",
                L("character.ui.live_preview", "LIVE CHARACTER PREVIEW"));
            if (toggleHintText != null)
                toggleHintText.text = OntologyCharacterCustomizationUiConfig.ToggleHint;
            SetButtonLabel(equipButton, OntologyCharacterCustomizationUiConfig.EquipLabel);
            SetButtonLabel(
                unequipButton,
                OntologyCharacterCustomizationUiConfig.UnequipLabel);
            if (selectedTitle != null && string.IsNullOrWhiteSpace(selectedPartId))
                selectedTitle.text = OntologyCharacterCustomizationUiConfig.SelectPartTitle;
            if (selectedDescription != null && string.IsNullOrWhiteSpace(selectedPartId))
                selectedDescription.text =
                    OntologyCharacterCustomizationUiConfig.SelectPartDescription;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private void SetHierarchyText(string path, string value)
        {
            var text = transform.Find(path)?.GetComponent<TMP_Text>();
            if (text != null) text.text = value;
        }

        private void HookButtons()
        {
            if (equipButton != null)
            {
                equipButton.onClick.RemoveListener(EquipSelected);
                equipButton.onClick.AddListener(EquipSelected);
            }

            if (unequipButton != null)
            {
                unequipButton.onClick.RemoveListener(UnequipSelected);
                unequipButton.onClick.AddListener(UnequipSelected);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(ClosePanel);
                closeButton.onClick.AddListener(ClosePanel);
            }

            if (openButton != null)
            {
                openButton.onClick.RemoveListener(OpenPanel);
                if (allowRuntimeToggle)
                    openButton.onClick.AddListener(OpenPanel);
            }
        }

        public void OpenPanel()
        {
            SetVisible(true);
            Rebuild();
        }

        public void ClosePanel()
        {
            SetVisible(false);
            if (persistAccountAppearanceOnClose)
            {
                EnsureReferences();
                if (accountEntryFlow == null)
                    accountEntryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
                accountEntryFlow?.SaveCurrentAccountAppearance();
            }
            var completed = closedFromAccountCreation;
            closedFromAccountCreation = null;
            completed?.Invoke();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = GetComponent<CanvasGroup>();
            }

            if (panelCanvasGroup == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            panelCanvasGroup.alpha = visible ? 1f : 0f;
            panelCanvasGroup.interactable = visible;
            panelCanvasGroup.blocksRaycasts = visible;
            if (allowRuntimeToggle && toggleHintText != null)
            {
                toggleHintText.gameObject.SetActive(!visible);
            }
            if (allowRuntimeToggle && openButton != null)
            {
                openButton.gameObject.SetActive(!visible);
            }
        }

        private void ClearContainer(RectTransform container)
        {
            for (var i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private static void SetButtonColor(Button button, Color color)
        {
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
            }
        }

    }
}
