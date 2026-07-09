using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyCharacterPartPanel : OntologyUIPanelBase
    {
        [SerializeField] private OntologyCharacterPartAdapter partAdapter;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private RectTransform rowContainer;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private TMP_Text previewText;
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private Button stateFilterButton;
        [SerializeField] private Button slotFilterButton;

        private readonly List<GameObject> rowObjects = new();
        private string lastAction = string.Empty;
        private string selectedPartId = string.Empty;
        private string searchQuery = string.Empty;
        private string slotFilter = string.Empty;
        private PartStateFilter stateFilter;

        private enum PartStateFilter
        {
            All,
            Equipped,
            Available
        }

        private void Awake()
        {
            EnsureReferences();
            HookControls();
        }

        private void Start()
        {
            Rebuild();
        }

        private void RefreshRows()
        {
            if (partAdapter == null || partDatabase == null || rowContainer == null)
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                if (!PassesFilter(definition))
                {
                    continue;
                }

                var row = rowContainer.Find("PartRow_" + definition.partId);
                if (row == null)
                {
                    continue;
                }

                var isEquipped = partAdapter.IsPartEquipped(definition.partId);
                var hasFact = partAdapter.HasEquippedPartFact(definition.partId);
                var isConflictAffected = IsConflictAffectedBySelectedPart(definition);
                var background = row.GetComponent<Image>();
                if (background != null)
                {
                    background.color = isConflictAffected
                        ? Theme.rowConflict
                        : isEquipped
                        ? Theme.rowEquipped
                        : row.GetSiblingIndex() % 2 == 0
                            ? Theme.rowEven
                            : Theme.rowOdd;
                }

                UpdateRowButtons(row, isEquipped);

                var label = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label == null)
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
                label.text = FormatPartLabel(displayName, isEquipped, hasFact);
                label.color = isConflictAffected ? Theme.rowConflictText : isEquipped ? Theme.rowEquippedText : Theme.rowText;
            }
        }

        public void Rebuild()
        {
            EnsureReferences();
            HookControls();
            ClearRows();

            if (partAdapter == null || partDatabase == null || rowContainer == null)
            {
                SetStatus(Labels.missingReferences);
                return;
            }

            var definitions = partDatabase.Definitions;
            var visualRow = 0;
            var lastSlot = string.Empty;
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.partId))
                {
                    continue;
                }

                if (!PassesFilter(definition))
                {
                    continue;
                }

                if (!string.Equals(lastSlot, definition.slot, System.StringComparison.Ordinal))
                {
                    CreateSlotHeader(definition.slot, visualRow);
                    visualRow++;
                    lastSlot = definition.slot;
                }

                CreateRow(definition, visualRow);
                visualRow++;
            }

            if (rowContainer != null)
            {
                rowContainer.sizeDelta = new Vector2(rowContainer.sizeDelta.x, visualRow * Theme.rowSpacing + Theme.rowHeight);
            }

            RefreshStatus();
            RefreshDetail();
        }

        public void RefreshStatus()
        {
            if (partAdapter == null || partDatabase == null)
            {
                SetStatus(Labels.noPartAdapter);
                return;
            }

            var equippedCount = 0;
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition != null && partAdapter.IsPartEquipped(definition.partId))
                {
                    equippedCount++;
                }
            }

            var status = string.Format(Labels.equippedCountFormat, equippedCount, partDatabase.Definitions.Count);
            if (!string.IsNullOrWhiteSpace(lastAction))
            {
                status += " | " + lastAction;
            }

            SetStatus(status);
        }

        public void SelectPart(string partId)
        {
            selectedPartId = partId;
            RefreshDetail();
            RefreshRows();
        }

        private void EnsureReferences()
        {
            if (partAdapter == null)
            {
                partAdapter = FindFirstObjectByType<OntologyCharacterPartAdapter>();
            }

            if (partDatabase == null && partAdapter != null)
            {
                partDatabase = partAdapter.PartDatabase;
            }

            if (rowContainer == null)
            {
                rowContainer = transform as RectTransform;
            }

            if (detailText == null)
            {
                var detail = transform.Find("DetailText");
                if (detail != null)
                {
                    detailText = detail.GetComponent<TMP_Text>();
                }
            }

            if (previewText == null)
            {
                previewText = transform.Find("PreviewText")?.GetComponent<TMP_Text>();
            }

            if (searchInput == null)
            {
                searchInput = transform.Find("SearchInput")?.GetComponent<TMP_InputField>();
            }

            if (stateFilterButton == null)
            {
                stateFilterButton = transform.Find("StateFilterButton")?.GetComponent<Button>();
            }

            if (slotFilterButton == null)
            {
                slotFilterButton = transform.Find("SlotFilterButton")?.GetComponent<Button>();
            }
        }

        private void HookControls()
        {
            if (searchInput != null)
            {
                searchInput.onValueChanged.RemoveListener(SetSearchQuery);
                searchInput.onValueChanged.AddListener(SetSearchQuery);
            }

            if (stateFilterButton != null)
            {
                stateFilterButton.onClick.RemoveListener(CycleStateFilter);
                stateFilterButton.onClick.AddListener(CycleStateFilter);
            }

            if (slotFilterButton != null)
            {
                slotFilterButton.onClick.RemoveListener(CycleSlotFilter);
                slotFilterButton.onClick.AddListener(CycleSlotFilter);
            }

            RefreshFilterLabels();
        }

        private void SetSearchQuery(string value)
        {
            searchQuery = value ?? string.Empty;
            Rebuild();
        }

        private void CycleStateFilter()
        {
            stateFilter = stateFilter == PartStateFilter.Available ? PartStateFilter.All : stateFilter + 1;
            RefreshFilterLabels();
            Rebuild();
        }

        private void CycleSlotFilter()
        {
            slotFilter = GetNextSlotFilter();
            RefreshFilterLabels();
            Rebuild();
        }

        private void RefreshFilterLabels()
        {
            StyleButton(stateFilterButton, string.Format(Labels.partStateFilterFormat, GetStateFilterLabel()), Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleButton(slotFilterButton, string.Format(Labels.partSlotFilterFormat, string.IsNullOrWhiteSpace(slotFilter) ? Labels.partSlotAll : slotFilter), Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
        }

        private string GetStateFilterLabel()
        {
            switch (stateFilter)
            {
                case PartStateFilter.Equipped:
                    return Labels.partStateEquipped;
                case PartStateFilter.Available:
                    return Labels.partStateAvailable;
                default:
                    return Labels.partStateAll;
            }
        }

        private string GetNextSlotFilter()
        {
            if (partDatabase == null || partDatabase.Definitions == null)
            {
                return string.Empty;
            }

            var returnNext = string.IsNullOrWhiteSpace(slotFilter);
            var seenSlots = new HashSet<string>();
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.slot))
                {
                    continue;
                }

                if (!seenSlots.Add(definition.slot))
                {
                    continue;
                }

                if (returnNext)
                {
                    return definition.slot;
                }

                if (definition.slot == slotFilter)
                {
                    returnNext = true;
                }
            }

            return string.Empty;
        }

        private bool PassesFilter(OntologyCharacterPartDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var displayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
                if (displayName.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) < 0
                    && definition.partId.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(slotFilter) && definition.slot != slotFilter)
            {
                return false;
            }

            var isEquipped = partAdapter != null && partAdapter.IsPartEquipped(definition.partId);
            if (stateFilter == PartStateFilter.Equipped)
            {
                return isEquipped;
            }

            if (stateFilter == PartStateFilter.Available)
            {
                return !isEquipped;
            }

            return true;
        }

        private void CreateSlotHeader(string slot, int rowIndex)
        {
            var header = new GameObject("SlotHeader_" + slot, typeof(RectTransform), typeof(Image));
            header.transform.SetParent(rowContainer, false);
            rowObjects.Add(header);

            var rect = header.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -rowIndex * Theme.rowSpacing);
            rect.sizeDelta = new Vector2(0f, Theme.rowHeight);

            var background = header.GetComponent<Image>();
            background.raycastTarget = false;
            background.color = Theme.rowEven;

            var label = CreateText(header.transform, "Label", string.IsNullOrWhiteSpace(slot) ? "Unknown Slot" : slot, Theme.rowFontSize, Theme.statusText, TextAlignmentOptions.MidlineLeft);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Theme.rowLabelOffsetMin;
            labelRect.offsetMax = new Vector2(-8f, 0f);
            label.fontStyle = FontStyles.Bold;
        }

        private void CreateRow(OntologyCharacterPartDefinition definition, int rowIndex)
        {
            var isEquipped = partAdapter != null && partAdapter.IsPartEquipped(definition.partId);
            var hasFact = partAdapter != null && partAdapter.HasEquippedPartFact(definition.partId);
            var isConflictAffected = IsConflictAffectedBySelectedPart(definition);
            var row = new GameObject("PartRow_" + definition.partId, typeof(RectTransform), typeof(Image));
            row.transform.SetParent(rowContainer, false);
            rowObjects.Add(row);

            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -rowIndex * Theme.rowSpacing);
            rect.sizeDelta = new Vector2(0f, Theme.rowHeight);

            var background = row.GetComponent<Image>();
            background.raycastTarget = false;
            background.color = isConflictAffected
                ? Theme.rowConflict
                : isEquipped
                ? Theme.rowEquipped
                : rowIndex % 2 == 0
                    ? Theme.rowEven
                    : Theme.rowOdd;

            CreateLabel(row.transform, definition, isEquipped, hasFact);
            CreatePartButton(row.transform, Labels.equip, definition.partId, true, Theme.equipButtonPosition, Theme.equipButton);
            CreatePartButton(row.transform, Labels.unequip, definition.partId, false, Theme.unequipButtonPosition, Theme.unequipButton);
            UpdateRowButtons(row.transform, isEquipped);
        }

        private void CreateLabel(Transform parent, OntologyCharacterPartDefinition definition, bool isEquipped, bool hasFact)
        {
            var isConflictAffected = IsConflictAffectedBySelectedPart(definition);
            var label = CreateText(parent, "Label", string.Empty, Theme.rowFontSize, isConflictAffected ? Theme.rowConflictText : isEquipped ? Theme.rowEquippedText : Theme.rowText, TextAlignmentOptions.MidlineLeft);

            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Theme.rowLabelOffsetMin;
            rect.offsetMax = Theme.rowLabelOffsetMax;

            var displayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
            label.text = FormatPartLabel(displayName, isEquipped, hasFact);
        }

        private string FormatPartLabel(string displayName, bool isEquipped, bool hasFact)
        {
            var state = isEquipped ? Labels.equippedState : Labels.unequippedState;
            var mesh = isEquipped ? Labels.equippedState : Labels.unequippedState;
            var fact = hasFact ? Labels.equippedState : Labels.unequippedState;
            return "[" + state + "] " + displayName
                + "  " + Labels.meshState + "=" + mesh
                + " " + Labels.factState + "=" + fact;
        }

        private void CreatePartButton(Transform parent, string labelText, string partId, bool equip, Vector2 anchoredPosition, Color color)
        {
            var button = CreateButton(parent, labelText + "Button", labelText, anchoredPosition, Theme.buttonSize, color, Theme.buttonText, Theme.buttonFontSize);
            button.gameObject.AddComponent<OntologyCharacterPartButton>();

            var partButton = button.GetComponent<OntologyCharacterPartButton>();
            partButton.Configure(this, partId, equip);
            partButton.ConfigureStyle(color, Theme.buttonHover);
            button.onClick.AddListener(() => SelectPart(partId));
        }

        private void UpdateRowButtons(Transform row, bool isEquipped)
        {
            var equipButton = row.Find(Labels.equip + "Button")?.GetComponent<OntologyCharacterPartButton>();
            var unequipButton = row.Find(Labels.unequip + "Button")?.GetComponent<OntologyCharacterPartButton>();
            if (equipButton != null)
            {
                equipButton.SetInteractable(!isEquipped, Theme.equipButton, Theme.disabledButton);
            }

            if (unequipButton != null)
            {
                unequipButton.SetInteractable(isEquipped, Theme.unequipButton, Theme.disabledButton);
            }
        }

        public void SetPartEquipped(string partId, bool equip)
        {
            if (equip)
            {
                Equip(partId);
            }
            else
            {
                Unequip(partId);
            }

            RefreshRows();
            SelectPart(partId);
        }

        private void Equip(string partId)
        {
            var displayName = GetDisplayName(partId);
            if (partAdapter == null)
            {
                lastAction = string.Format(Labels.equipFailedFormat, displayName + " (" + Labels.noPartAdapter + ")");
                RefreshStatus();
                return;
            }

            if (!partAdapter.CanEquipPart(partId, out var reason))
            {
                lastAction = string.Format(Labels.equipFailedFormat, displayName + " (" + GetFailureReasonLabel(reason) + ")");
                RefreshStatus();
                return;
            }

            if (partAdapter.EquipPart(partId))
            {
                partAdapter.InjectActivePartFacts();
                lastAction = string.Format(Labels.equippedActionFormat, displayName);
            }
            else
            {
                lastAction = string.Format(Labels.equipFailedFormat, displayName);
            }

            RefreshStatus();
        }

        private void Unequip(string partId)
        {
            var displayName = GetDisplayName(partId);
            if (partAdapter == null)
            {
                lastAction = string.Format(Labels.unequipFailedFormat, displayName + " (" + Labels.noPartAdapter + ")");
                RefreshStatus();
                return;
            }

            if (!partAdapter.CanUnequipPart(partId, out var reason))
            {
                lastAction = string.Format(Labels.unequipFailedFormat, displayName + " (" + GetFailureReasonLabel(reason) + ")");
                RefreshStatus();
                return;
            }

            if (partAdapter.UnequipPart(partId))
            {
                partAdapter.InjectActivePartFacts();
                lastAction = string.Format(Labels.unequippedActionFormat, displayName);
            }
            else
            {
                lastAction = string.Format(Labels.unequipFailedFormat, displayName);
            }

            RefreshStatus();
        }

        private string GetFailureReasonLabel(string reason)
        {
            switch (reason)
            {
                case OntologyCharacterPartAdapter.FailureDefinitionMissing:
                    return Labels.partFailureDefinitionMissing;
                case OntologyCharacterPartAdapter.FailureRendererMissing:
                    return Labels.partFailureRendererMissing;
                case OntologyCharacterPartAdapter.FailureWorldMissing:
                    return Labels.partFailureWorldMissing;
                case OntologyCharacterPartAdapter.FailureAlreadyEquipped:
                    return Labels.partFailureAlreadyEquipped;
                case OntologyCharacterPartAdapter.FailureAlreadyUnequipped:
                    return Labels.partFailureAlreadyUnequipped;
                default:
                    return string.IsNullOrWhiteSpace(reason) ? Labels.noDiff : reason;
            }
        }

        private string GetDisplayName(string partId)
        {
            var definition = FindDefinition(partId);
            if (definition == null)
            {
                return partId;
            }

            return string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
        }

        private void ClearRows()
        {
            for (var i = rowObjects.Count - 1; i >= 0; i--)
            {
                if (rowObjects[i] != null)
                {
                    DestroyImmediate(rowObjects[i]);
                }
            }

            rowObjects.Clear();

            if (rowContainer == null)
            {
                return;
            }

            for (var i = rowContainer.childCount - 1; i >= 0; i--)
            {
                var child = rowContainer.GetChild(i);
                DestroyImmediate(child.gameObject);
            }
        }

        private void SetStatus(string value)
        {
            if (statusText != null)
            {
                statusText.text = value;
            }
        }

        private void RefreshDetail()
        {
            if (detailText == null || partDatabase == null || string.IsNullOrWhiteSpace(selectedPartId))
            {
                return;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || definition.partId != selectedPartId)
                {
                    continue;
                }

                var displayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
                var isEquipped = partAdapter != null && partAdapter.IsPartEquipped(definition.partId);
                var hasFact = partAdapter != null && partAdapter.HasEquippedPartFact(definition.partId);
                detailText.text = string.Format(
                    Labels.selectedPartFormat,
                    displayName,
                    definition.slot,
                    isEquipped ? Labels.equippedState : Labels.unequippedState,
                    hasFact ? Labels.equippedState : Labels.unequippedState);
                if (previewText != null)
                {
                    previewText.text = string.Format(
                        Labels.selectedPartPreviewFormat,
                        displayName,
                        GetCapabilityPreview(definition),
                        GetConflictPreview(definition),
                        GetEquipDiffPreview(definition, isEquipped));
                }
                return;
            }
        }

        private string GetCapabilityPreview(OntologyCharacterPartDefinition definition)
        {
            var result = new System.Text.StringBuilder();
            AppendFacts(definition, OntologyPredicates.GrantsCapability, result, FormatCapability);
            return result.Length == 0 ? Labels.noCapabilities : result.ToString();
        }

        private string GetConflictPreview(OntologyCharacterPartDefinition definition)
        {
            var result = new System.Text.StringBuilder();
            AppendFacts(definition, OntologyPredicates.ConflictsWithSlot, result, FormatSlotName);
            if (!string.IsNullOrWhiteSpace(definition.slot))
            {
                foreach (var other in partDatabase.Definitions)
                {
                    if (other == null || other == definition || other.slot != definition.slot)
                    {
                        continue;
                    }

                    AppendValue(result, string.IsNullOrWhiteSpace(other.displayName) ? other.partId : other.displayName);
                }
            }

            return result.Length == 0 ? Labels.noConflicts : result.ToString();
        }

        private string GetEquipDiffPreview(OntologyCharacterPartDefinition definition, bool isEquipped)
        {
            if (isEquipped)
            {
                return Labels.noDiff;
            }

            var result = new System.Text.StringBuilder();
            var conflicts = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(definition.slot) && partAdapter != null)
            {
                foreach (var other in partDatabase.Definitions)
                {
                    if (other == null || other == definition || !partAdapter.IsPartEquipped(other.partId))
                    {
                        continue;
                    }

                    if (other.slot == definition.slot || HasFact(definition, OntologyPredicates.ConflictsWithSlot, other.slot) || HasFact(other, OntologyPredicates.ConflictsWithSlot, definition.slot))
                    {
                        AppendValue(conflicts, string.IsNullOrWhiteSpace(other.displayName) ? other.partId : other.displayName);
                    }
                }
            }

            if (conflicts.Length > 0)
            {
                AppendValue(result, string.Format(Labels.conflictWarningFormat, conflicts));
            }

            AppendValue(result, "Equip " + (string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName));
            return result.Length == 0 ? Labels.noDiff : result.ToString();
        }

        private static void AppendFacts(OntologyCharacterPartDefinition definition, string predicate, System.Text.StringBuilder result)
        {
            AppendFacts(definition, predicate, result, null);
        }

        private static void AppendFacts(OntologyCharacterPartDefinition definition, string predicate, System.Text.StringBuilder result, System.Func<string, string> formatter)
        {
            if (definition.facts == null)
            {
                return;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == predicate)
                {
                    AppendValue(result, formatter == null ? fact.obj : formatter(fact.obj));
                }
            }
        }

        private bool IsConflictAffectedBySelectedPart(OntologyCharacterPartDefinition candidate)
        {
            var selected = FindDefinition(selectedPartId);
            if (selected == null || candidate == null || selected == candidate || partAdapter == null)
            {
                return false;
            }

            if (!partAdapter.IsPartEquipped(candidate.partId))
            {
                return false;
            }

            return candidate.slot == selected.slot
                || HasFact(selected, OntologyPredicates.ConflictsWithSlot, candidate.slot)
                || HasFact(candidate, OntologyPredicates.ConflictsWithSlot, selected.slot);
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

        private string FormatCapability(string capability)
        {
            switch (capability)
            {
                case "SwampResistance":
                    return Labels.capabilitySwampResistance + " (" + Labels.capabilitySwampResistanceEffect + ")";
                case "ColdProtection":
                    return Labels.capabilityColdProtection + " (" + Labels.capabilityColdProtectionEffect + ")";
                default:
                    return SplitPascalCase(capability);
            }
        }

        private string FormatSlotName(string slot)
        {
            return SplitPascalCase(slot);
        }

        private static string SplitPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var result = new System.Text.StringBuilder(value.Length + 4);
            for (var i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
                {
                    result.Append(' ');
                }

                result.Append(value[i]);
            }

            return result.ToString();
        }

        private static bool HasFact(OntologyCharacterPartDefinition definition, string predicate, string obj)
        {
            if (definition == null || definition.facts == null)
            {
                return false;
            }

            foreach (var fact in definition.facts)
            {
                if (fact != null && fact.predicate == predicate && fact.obj == obj)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendValue(System.Text.StringBuilder result, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (result.Length > 0)
            {
                result.Append(", ");
            }

            result.Append(value);
        }
    }
}
