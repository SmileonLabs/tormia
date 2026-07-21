using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Binds the hierarchy-authored uGUI world editor to ontology data.
    /// The visual structure lives in the scene so designers can edit it directly.
    /// </summary>
    public sealed class OntologyRuntimeWorldFactEditorPanel : MonoBehaviour
    {
        private enum EditorMode
        {
            Triples,
            RuleBlocks,
            PhysicalBehavior,
            Results
        }

        private static List<string> RelationChoices =>
            OntologyLanguagePackService.Registry.Terms
                .Where(value =>
                    value != null &&
                    value.Kind == OntologyTermKind.Relation &&
                    !value.Deprecated &&
                    !OntologyPredicates.IsRuntimeDerived(value.CanonicalId))
                .Select(value => value.CanonicalId)
                .OrderBy(value => value)
                .ToList();

        [SerializeField] private OntologyRuntimeWorldEditorController controller;
        [Header("Hierarchy Bindings (assign in Inspector)")]
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private TextMeshProUGUI objectName;
        [SerializeField] private TextMeshProUGUI objectSubtitle;
        [SerializeField] private TMP_Dropdown languageDropdown;
        [SerializeField] private RectTransform tripleContent;
        [SerializeField] private RectTransform physicalContent;
        [SerializeField] private RectTransform resultContent;
        [SerializeField] private GameObject tripleScrollView;
        [SerializeField] private GameObject physicalScrollView;
        [SerializeField] private GameObject resultScrollView;
        [SerializeField] private GameObject tripleRowTemplate;
        [SerializeField] private GameObject ruleRowTemplate;
        [FormerlySerializedAs("rulePresetPickerTemplate")]
        [SerializeField] private GameObject quickSetupPanelTemplate;
        [SerializeField] private GameObject physicalBaseProfileTemplate;
        [SerializeField] private GameObject physicalEffectPickerTemplate;
        [SerializeField] private GameObject physicalEffectRowTemplate;
        [SerializeField] private GameObject resultRowTemplate;
        [SerializeField] private Button addTripleButton;
        [SerializeField] private Button saveWorldButton;
        [SerializeField] private Button loadWorldButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button tripleTabButton;
        [SerializeField] private Button ruleTabButton;
        [SerializeField] private Button physicalTabButton;
        [SerializeField] private Button resultTabButton;
        [SerializeField] private TextMeshProUGUI footerHint;
        [Header("Physical Meaning Detail")]
        [SerializeField] private GameObject physicalDetailPopup;
        [SerializeField] private TextMeshProUGUI physicalDetailTitle;
        [SerializeField] private TextMeshProUGUI physicalDetailBody;
        [SerializeField] private TMP_Dropdown physicalDetailProfileDropdown;
        [SerializeField] private Button physicalDetailApplyButton;
        [SerializeField] private Button physicalDetailCloseButton;
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologySaveController saveController;
        [SerializeField] private OntologyWorldAuthorityBridge authorityBridge;

        private bool subscribed;
        private bool bound;
        private EditorMode mode;
        private OntologyPlaceableInstance pendingRuleOwner;
        private string pendingRuleId;
        private string pendingRuleVariable;
        private string pendingConceptConfirmationKey;
        private string footerStatus;
        private bool changingLanguage;
        private readonly List<string> physicalDetailProfileIds = new();
        private bool CanAuthorCurrentWorld =>
            authorityBridge == null ||
            !authorityBridge.HasSelectedAuthorityWorld ||
            authorityBridge.CanEditAuthorityWorld;

        public void Configure(OntologyRuntimeWorldEditorController value)
        {
            Unsubscribe();
            controller = value;
            Bind();
            Subscribe();
            Refresh();
        }

        private void Awake()
        {
            if (controller == null)
                controller = FindAnyObjectByType<OntologyRuntimeWorldEditorController>();
            if (bootstrap == null)
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (saveController == null)
                saveController = FindAnyObjectByType<OntologySaveController>();
            if (authorityBridge == null)
                authorityBridge = FindAnyObjectByType<OntologyWorldAuthorityBridge>();

            Bind();
            if (Application.isPlaying && tripleRowTemplate != null)
                tripleRowTemplate.SetActive(false);
            if (Application.isPlaying && ruleRowTemplate != null)
                ruleRowTemplate.SetActive(false);
            if (Application.isPlaying && quickSetupPanelTemplate != null)
                quickSetupPanelTemplate.SetActive(false);
            if (Application.isPlaying && physicalBaseProfileTemplate != null)
                physicalBaseProfileTemplate.SetActive(false);
            if (Application.isPlaying && physicalEffectPickerTemplate != null)
                physicalEffectPickerTemplate.SetActive(false);
            if (Application.isPlaying && physicalEffectRowTemplate != null)
                physicalEffectRowTemplate.SetActive(false);
            if (Application.isPlaying && resultRowTemplate != null)
                resultRowTemplate.SetActive(false);
        }

        private void Start()
        {
            Bind();
            Subscribe();
            Refresh();
        }

        private void OnEnable()
        {
            Bind();
            Subscribe();
            Refresh();
        }

        private void OnDisable() => Unsubscribe();

        private void Bind()
        {
            // The visual hierarchy is authored in the scene. Runtime code intentionally
            // uses the serialized references below, so designers may freely rename or
            // rearrange UI objects after reconnecting the affected field in Inspector.
            bound = hudRoot != null &&
                    tripleContent != null &&
                    tripleRowTemplate != null &&
                    ruleRowTemplate != null &&
                    resultRowTemplate != null;
            if (!bound) return;

            if (addTripleButton != null)
            {
                addTripleButton.onClick.RemoveAllListeners();
                addTripleButton.onClick.AddListener(AddBlankRow);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            if (saveWorldButton != null)
            {
                saveWorldButton.onClick.RemoveAllListeners();
                saveWorldButton.onClick.AddListener(SaveWorld);
            }

            if (loadWorldButton != null)
            {
                loadWorldButton.onClick.RemoveAllListeners();
                loadWorldButton.onClick.AddListener(LoadWorld);
            }

            BindTab(tripleTabButton, EditorMode.Triples);
            BindTab(ruleTabButton, EditorMode.RuleBlocks);
            BindTab(physicalTabButton, EditorMode.PhysicalBehavior);
            BindTab(resultTabButton, EditorMode.Results);
            BindLanguageDropdown();
            ApplyLocalizedStaticLabels();
            if (physicalDetailCloseButton != null)
            {
                physicalDetailCloseButton.onClick.RemoveAllListeners();
                physicalDetailCloseButton.onClick.AddListener(ClosePhysicalDetail);
            }

        }

        /// <summary>
        /// One-time editor-only migration for scenes authored before inspector bindings
        /// were introduced. It is never called by the game at runtime.
        /// </summary>
        [ContextMenu("Migrate Legacy Hierarchy Bindings")]
        public void MigrateLegacyHierarchyBindings()
        {
#if UNITY_EDITOR
            var root = transform.Find("WorldEditHUD");
            if (root == null)
            {
                Debug.LogWarning(
                    "[OntologyRuntimeWorldFactEditorPanel] WorldEditHUD was not found. " +
                    "Assign the hierarchy bindings manually in Inspector.",
                    this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Migrate World Edit UI bindings");
            hudRoot = root.gameObject;
            objectName = root.Find("Header/ObjectName")?.GetComponent<TextMeshProUGUI>();
            objectSubtitle = root.Find("Header/ObjectSubtitle")?.GetComponent<TextMeshProUGUI>();
            languageDropdown = root.Find("Header/LanguageDropdown")?.GetComponent<TMP_Dropdown>();
            tripleContent = root.Find("TripleScrollView/Viewport/Content") as RectTransform;
            physicalContent = root.Find("PhysicalScrollView/Viewport/Content") as RectTransform;
            resultContent = root.Find("ResultScrollView/Viewport/Content") as RectTransform;
            tripleScrollView = root.Find("TripleScrollView")?.gameObject;
            physicalScrollView = root.Find("PhysicalScrollView")?.gameObject;
            resultScrollView = root.Find("ResultScrollView")?.gameObject;
            tripleRowTemplate = tripleContent?.Find("TripleRowTemplate")?.gameObject;
            ruleRowTemplate = tripleContent?.Find("RuleRowTemplate")?.gameObject;
            resultRowTemplate = tripleContent?.Find("ResultRowTemplate")?.gameObject;
            addTripleButton = root.Find("Footer/ActionRow/AddTripleButton")?.GetComponent<Button>();
            saveWorldButton = root.Find("Footer/ActionRow/SaveWorldButton")?.GetComponent<Button>();
            loadWorldButton = root.Find("Footer/ActionRow/LoadWorldButton")?.GetComponent<Button>();
            closeButton = root.Find("Header/CloseButton")?.GetComponent<Button>();
            tripleTabButton = root.Find("ModeTabs/TripleTabButton")?.GetComponent<Button>();
            ruleTabButton = root.Find("ModeTabs/RuleTabButton")?.GetComponent<Button>();
            physicalTabButton = root.Find("ModeTabs/PhysicalTabButton")?.GetComponent<Button>();
            resultTabButton = root.Find("ModeTabs/ResultTabButton")?.GetComponent<Button>();
            footerHint = root.Find("Footer/Hint")?.GetComponent<TextMeshProUGUI>();
            var detail = root.Find("PhysicalDetailPopup");
            physicalDetailPopup = detail?.gameObject;
            physicalDetailTitle = detail?.Find("Title")?.GetComponent<TextMeshProUGUI>();
            physicalDetailBody = detail?.Find("Body")?.GetComponent<TextMeshProUGUI>();
            physicalDetailProfileDropdown = detail?.Find("ProfileDropdown")?.GetComponent<TMP_Dropdown>();
            physicalDetailApplyButton = detail?.Find("ApplyButton")?.GetComponent<Button>();
            physicalDetailCloseButton = detail?.Find("CloseButton")?.GetComponent<Button>();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void BindTab(Button button, EditorMode targetMode)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                ClosePhysicalDetail();
                mode = targetMode;
                footerStatus = null;
                Refresh();
            });
        }

        private void Subscribe()
        {
            if (subscribed || controller == null) return;
            controller.StateChanged += Refresh;
            OntologyLanguagePackService.LanguageChanged += HandleLanguageChanged;
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap != null) bootstrap.WorldChanged += HandleWorldChanged;
            if (authorityBridge == null)
                authorityBridge = FindAnyObjectByType<OntologyWorldAuthorityBridge>();
            if (authorityBridge != null)
                authorityBridge.StatusChanged += HandleAuthorityStatusChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (controller != null)
                controller.StateChanged -= Refresh;
            OntologyLanguagePackService.LanguageChanged -= HandleLanguageChanged;
            if (bootstrap != null) bootstrap.WorldChanged -= HandleWorldChanged;
            if (authorityBridge != null)
                authorityBridge.StatusChanged -= HandleAuthorityStatusChanged;
            subscribed = false;
        }

        private void HandleLanguageChanged()
        {
            OntologyLanguagePackService.EnsureKoreanFontFallback(transform);
            BindLanguageDropdown();
            ApplyLocalizedStaticLabels();
            Refresh();
        }

        private void BindLanguageDropdown()
        {
            if (languageDropdown == null) return;
            changingLanguage = true;
            languageDropdown.onValueChanged.RemoveAllListeners();
            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(new List<string> { "한국어", "English" });
            languageDropdown.SetValueWithoutNotify(
                OntologyLanguagePackService.CurrentLanguage ==
                OntologyDisplayLanguage.Korean ? 0 : 1);
            languageDropdown.RefreshShownValue();
            languageDropdown.onValueChanged.AddListener(index =>
            {
                if (changingLanguage) return;
                OntologyLanguagePackService.SetLanguage(
                    index == 0
                        ? OntologyDisplayLanguage.Korean
                        : OntologyDisplayLanguage.English);
            });
            changingLanguage = false;
        }

        private void ApplyLocalizedStaticLabels()
        {
            OntologyLanguagePackService.EnsureKoreanFontFallback(transform);
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private void HandleWorldChanged()
        {
            // Runtime sensing can update the world every frame. Rebuilding editable
            // rows here would destroy an expanded TMP_Dropdown before it is usable.
            // Only the read-only results view needs live world refreshes.
            if (mode == EditorMode.Results &&
                controller != null &&
                controller.IsOntologyOpen)
                Refresh();
        }

        private void HandleAuthorityStatusChanged()
        {
            if (authorityBridge == null ||
                string.IsNullOrWhiteSpace(authorityBridge.LastPublishStatus))
            {
                return;
            }

            footerStatus = authorityBridge.LastPublishStatus;
            if (controller != null && controller.IsOntologyOpen)
            {
                Refresh();
            }
            else
            {
                RefreshFooter();
            }
        }

        private void Close()
        {
            ClosePhysicalDetail();
            ClearPendingRule();
            pendingConceptConfirmationKey = null;
            controller?.CloseOntologyEditor();
        }

        private void SaveWorld()
        {
            if (saveController == null)
                saveController = FindAnyObjectByType<OntologySaveController>();
            if (saveController == null)
            {
                footerStatus = L(
                    "ui.status.save_failed",
                    "Save failed: the save controller was not found.");
                RefreshFooter();
                return;
            }

            var report = saveController.SaveSnapshot();
            footerStatus = report.Contains("Saved snapshot")
                ? L("ui.status.saved", "World saved successfully.")
                : FirstReportLine(report);
            RefreshFooter();
        }

        private void LoadWorld()
        {
            if (saveController == null)
                saveController = FindAnyObjectByType<OntologySaveController>();
            if (saveController == null)
            {
                footerStatus = L(
                    "ui.status.load_failed",
                    "Load failed: the save controller was not found.");
                RefreshFooter();
                return;
            }

            ClearPendingRule();
            var report = saveController.LoadSnapshot();
            footerStatus = report.Contains("Loaded snapshot")
                ? L("ui.status.loaded", "World loaded successfully.")
                : FirstReportLine(report);
            RefreshFooter();
        }

        private static string FirstReportLine(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return L(
                    "ui.status.no_operation_result",
                    "The operation did not return a result.");
            var lines = report.Split('\n');
            return lines.Length > 1 ? lines[1].Trim() : lines[0].Trim();
        }

        private void Refresh()
        {
            Bind();
            if (!bound || hudRoot == null) return;

            hudRoot.SetActive(controller != null && controller.IsOntologyOpen);
            if (!Application.isPlaying) return;

            RefreshModeContainers();

            tripleRowTemplate.SetActive(false);
            ruleRowTemplate.SetActive(false);
            resultRowTemplate.SetActive(false);
            ClearRuntimeRows(tripleContent);
            ClearRuntimeRows(physicalContent);
            ClearRuntimeRows(resultContent);
            RefreshFooter();

            var selected = controller != null ? controller.Selected : null;
            if (pendingRuleOwner != null && pendingRuleOwner != selected)
                ClearPendingRule();
            if (selected == null)
            {
                SetHeader(
                    L("ui.subtitle.no_selection", "No object selected"),
                    L("ui.subtitle.no_selection", "Select a placed object"));
                return;
            }

            SetHeader(
                controller.SelectedDisplayName,
                L("ui.subtitle.selected_object", "Selected object"));
            var ontology = selected.GetComponent<OntologyObject>();
            if (ontology == null) return;

            if (mode == EditorMode.RuleBlocks)
            {
                ShowRuleBlocks();
                ApplyCurrentAuthoringPermission();
                return;
            }

            if (mode == EditorMode.PhysicalBehavior)
            {
                ShowPhysicalBehavior();
                ApplyCurrentAuthoringPermission();
                return;
            }

            if (mode == EditorMode.Results)
            {
                ShowResults(ontology);
                return;
            }

            foreach (var concept in ontology.Concepts.Where(value => !string.IsNullOrWhiteSpace(value)))
                AddRow("has_concept", concept, true, false);

            foreach (var fact in ontology.Facts.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.predicate) &&
                         !string.IsNullOrWhiteSpace(value.obj)))
                AddRow(fact.predicate, fact.obj, false, false);
            ApplyCurrentAuthoringPermission();
        }

        private void RefreshFooter()
        {
            if (addTripleButton == null) return;
            var canAuthor = CanAuthorCurrentWorld;
            addTripleButton.gameObject.SetActive(
                mode == EditorMode.Triples ||
                mode == EditorMode.RuleBlocks);
            addTripleButton.interactable = canAuthor;
            if (saveWorldButton != null) saveWorldButton.interactable = canAuthor;
            if (physicalDetailApplyButton != null) physicalDetailApplyButton.interactable = canAuthor;
            if (footerHint != null)
            {
                footerHint.text = !canAuthor
                    ? L("ui.authority.read_only", "Viewer role: world editing is read only.")
                    : !string.IsNullOrWhiteSpace(footerStatus)
                    ? footerStatus
                    : mode == EditorMode.RuleBlocks
                    ? L(
                        "ui.footer.rule_blocks",
                        "A rule block enables a database rule for this object.")
                    : mode == EditorMode.PhysicalBehavior
                        ? L(
                            "ui.footer.physics",
                            "Choose a physical profile to apply to this object.")
                    : mode == EditorMode.Results
                        ? L(
                            "ui.footer.results",
                            "Current information and the rules that created it.")
                        : L(
                            "ui.footer.triples",
                            "The subject is fixed. Edit the relation and value.");
            }

            ApplyCurrentAuthoringPermission();
        }

        private void ApplyCurrentAuthoringPermission()
        {
            var canAuthor = CanAuthorCurrentWorld;
            ApplyRuntimeAuthoringPermission(tripleContent, canAuthor);
            ApplyRuntimeAuthoringPermission(physicalContent, canAuthor);
        }

        private static void ApplyRuntimeAuthoringPermission(
            Transform content,
            bool canAuthor)
        {
            if (content == null) return;
            foreach (var dropdown in content.GetComponentsInChildren<TMP_Dropdown>(true))
                dropdown.interactable = canAuthor;
            foreach (var input in content.GetComponentsInChildren<TMP_InputField>(true))
                input.interactable = canAuthor;
            foreach (var button in content.GetComponentsInChildren<Button>(true))
            {
                var controlName = button.gameObject.name;
                if (controlName.Contains("Apply") || controlName.Contains("Save") ||
                    controlName.Contains("Delete") || controlName.Contains("Remove"))
                    button.interactable = canAuthor;
            }
        }

        private void SetHeader(string title, string subtitle)
        {
            if (objectName != null) objectName.text = title;
            if (objectSubtitle != null) objectSubtitle.text = subtitle;
        }

        private void RefreshModeContainers()
        {
            var editorOpen = controller != null && controller.IsOntologyOpen;
            if (tripleScrollView != null)
                tripleScrollView.SetActive(editorOpen &&
                    (mode == EditorMode.Triples || mode == EditorMode.RuleBlocks));
            if (physicalScrollView != null)
                physicalScrollView.SetActive(editorOpen &&
                    mode == EditorMode.PhysicalBehavior);
            if (resultScrollView != null)
                resultScrollView.SetActive(editorOpen &&
                    mode == EditorMode.Results);
        }

        private void ClearRuntimeRows(RectTransform content)
        {
            if (content == null) return;
            for (var index = content.childCount - 1; index >= 0; index--)
            {
                var child = content.GetChild(index);
                if (IsAuthoredTemplate(child.gameObject))
                    continue;
                Destroy(child.gameObject);
            }
        }

        private bool IsAuthoredTemplate(GameObject candidate)
        {
            return candidate == tripleRowTemplate ||
                   candidate == ruleRowTemplate ||
                   candidate == quickSetupPanelTemplate ||
                   candidate == physicalBaseProfileTemplate ||
                   candidate == physicalEffectPickerTemplate ||
                   candidate == physicalEffectRowTemplate ||
                   candidate == resultRowTemplate;
        }

        private void AddBlankRow()
        {
            if (controller?.Selected == null) return;
            if (mode == EditorMode.RuleBlocks)
            {
                if (pendingRuleOwner == controller.Selected) return;
                var definition = controller.AvailableRuleDefinitions
                    .FirstOrDefault(value => value != null && !string.IsNullOrWhiteSpace(value.id));
                if (definition == null) return;
                pendingRuleOwner = controller.Selected;
                pendingRuleId = definition.id;
                var variables = controller.GetRuleVariables(pendingRuleId);
                pendingRuleVariable = variables.Contains("?target")
                    ? "?target"
                    : variables.FirstOrDefault();
                Refresh();
            }
            else if (mode == EditorMode.Triples)
                AddRow("has_concept", string.Empty, false, true);
        }

        private void ShowRuleBlocks()
        {
            AddRulePresetPickerRow();
            foreach (var binding in controller.SelectedRuleBlocks.Where(value => value != null))
                AddRuleRow(binding.ruleId, binding.bindingVariable, false);

            if (pendingRuleOwner == controller.Selected)
                AddRuleRow(pendingRuleId, pendingRuleVariable, true);
            else if (controller.SelectedRuleBlocks.Count == 0)
                AddResultRow(L(
                    "result.no_rule_block",
                    "No rule block is assigned. Add one to enable a database rule for this object."));
        }

        private void AddRulePresetPickerRow()
        {
            var template = quickSetupPanelTemplate != null
                ? quickSetupPanelTemplate
                : ruleRowTemplate;
            if (template == null || controller?.Selected == null) return;
            var presets = controller.AvailableRuleBlockPresets
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.presetId) &&
                                !string.IsNullOrWhiteSpace(value.primaryRuleId))
                .ToList();
            if (presets.Count == 0) return;

            var row = Instantiate(template, tripleContent);
            row.name = "RuleBlockPresetPicker";
            row.SetActive(true);
            PreparePhysicalRow(
                row,
                L("ui.rule_block.quick_action", "Quick action"),
                out var dropdown,
                out var apply,
                out var details);
            if (dropdown != null)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(presets.Select(RulePresetName).ToList());
                dropdown.SetValueWithoutNotify(0);
                dropdown.RefreshShownValue();
            }
            if (apply != null)
            {
                apply.interactable = true;
                apply.onClick.RemoveAllListeners();
                apply.onClick.AddListener(() =>
                {
                    var index = dropdown == null ? 0 : dropdown.value;
                    var preset = presets[Mathf.Clamp(index, 0, presets.Count - 1)];
                    var issue = controller.GetSelectedRuleBlockPresetValidationMessage(
                        preset.presetId);
                    if (!string.IsNullOrWhiteSpace(issue))
                    {
                        if (RequiresAttachmentSetup(preset))
                            OpenAttachmentSetup(preset);
                        else if (RequiresTemporarySkillSetup(preset))
                            OpenTemporarySkillSetup(preset);
                        else
                        {
                            footerStatus = issue;
                            RefreshFooter();
                        }
                        return;
                    }
                    controller.ApplySelectedRuleBlockPreset(preset.presetId);
                });
            }
            if (details != null)
            {
                details.interactable = true;
                details.onClick.RemoveAllListeners();
                details.onClick.AddListener(() =>
                {
                    var index = dropdown == null ? 0 : dropdown.value;
                    OpenRulePresetDetail(presets[Mathf.Clamp(index, 0, presets.Count - 1)]);
                });
            }
        }

        private static string RulePresetName(OntologyRuleBlockPreset preset)
        {
            return preset == null ? string.Empty : L(
                preset.displayNameKey,
                preset.primaryRuleId);
        }

        private static bool RequiresAttachmentSetup(OntologyRuleBlockPreset preset)
        {
            return preset != null && preset.requiredExistingPredicates.Any(value =>
                value == OntologyPredicates.AttachmentProfile ||
                value == OntologyPredicates.HasSlot ||
                value == OntologyPredicates.PickupBehavior);
        }

        private static bool RequiresTemporarySkillSetup(OntologyRuleBlockPreset preset)
        {
            return preset != null &&
                   preset.requiredExistingPredicates.Contains(
                       OntologyPredicates.GrantsSkill) &&
                   preset.requiredExistingPredicates.Contains(
                       OntologyPredicates.SkillGrantRequiresRule);
        }

        private void OpenTemporarySkillSetup(OntologyRuleBlockPreset preset)
        {
            if (physicalDetailPopup == null || preset == null || controller == null)
                return;

            var requiredRules = controller.SelectedRuleBlocks
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.ruleId))
                .Select(value => value.ruleId)
                .Distinct()
                .ToList();
            if (requiredRules.Count == 0)
            {
                footerStatus = L(
                    "rule_preset.temporary_skill_setup.no_rule",
                    "Choose a rule block that should keep this equipment skill active first.");
                RefreshFooter();
                return;
            }

            var skills = controller.GetAvailableTemporaryGrantSkills().ToList();
            if (skills.Count == 0)
            {
                footerStatus = L(
                    "rule_preset.temporary_skill_setup.no_skill",
                    "No usable skill is defined by the current ontology catalog.");
                RefreshFooter();
                return;
            }

            var choices = new List<KeyValuePair<string, string>>();
            foreach (var ruleId in requiredRules)
            {
                foreach (var skillId in skills)
                    choices.Add(new KeyValuePair<string, string>(skillId, ruleId));
            }

            if (physicalDetailTitle != null)
                physicalDetailTitle.text = L(
                    "rule_preset.temporary_skill_setup.title",
                    "Equipment skill setup");
            if (physicalDetailBody != null)
                physicalDetailBody.text = L(
                    "rule_preset.temporary_skill_setup.body",
                    "Choose the skill this equipment grants and the rule block that keeps it active. Both facts will be authored explicitly before the temporary-skill rule is added.");
            if (physicalDetailProfileDropdown != null)
            {
                physicalDetailProfileDropdown.gameObject.SetActive(true);
                physicalDetailProfileDropdown.ClearOptions();
                var optionLabels = new List<string>
                {
                    L(
                        "rule_preset.temporary_skill_setup.choose",
                        "Choose a skill and rule condition")
                };
                optionLabels.AddRange(choices.Select(value =>
                    OntologyLanguagePackService.Term(value.Key) + " · " +
                    OntologyLanguagePackService.RuleName(value.Value)));
                physicalDetailProfileDropdown.AddOptions(optionLabels);
                physicalDetailProfileDropdown.SetValueWithoutNotify(0);
                physicalDetailProfileDropdown.RefreshShownValue();
            }
            if (physicalDetailApplyButton != null)
            {
                physicalDetailApplyButton.onClick.RemoveAllListeners();
                physicalDetailApplyButton.onClick.AddListener(() =>
                {
                    var selectionIndex = physicalDetailProfileDropdown == null
                        ? 0
                        : physicalDetailProfileDropdown.value;
                    if (selectionIndex <= 0 || selectionIndex > choices.Count)
                    {
                        footerStatus = L(
                            "rule_preset.temporary_skill_setup.no_choice",
                            "Choose the skill and rule condition to apply.");
                        RefreshFooter();
                        return;
                    }

                    var choice = choices[selectionIndex - 1];
                    if (!controller.ConfigureSelectedTemporarySkillGrant(
                            choice.Key,
                            choice.Value))
                    {
                        footerStatus = L(
                            "rule_preset.validation.unavailable",
                            "The rule setup data could not be found.");
                        RefreshFooter();
                        return;
                    }
                    ClosePhysicalDetail();
                });
            }
            physicalDetailPopup.SetActive(true);
        }

        private void OpenAttachmentSetup(OntologyRuleBlockPreset preset)
        {
            if (physicalDetailPopup == null || preset == null || controller == null)
                return;

            var expectedKind = preset.requiredExistingConcepts.Contains(
                OntologyConcepts.Carryable)
                ? OntologyAttachmentKind.Carryable
                : preset.requiredExistingConcepts.Contains(OntologyConcepts.Mountable)
                    ? OntologyAttachmentKind.Mountable
                    : OntologyAttachmentKind.Wearable;
            var profiles = controller.AvailableAttachmentProfiles
                .Where(value => value != null &&
                                !string.IsNullOrWhiteSpace(value.profileId) &&
                                value.kind == expectedKind)
                .ToList();
            if (profiles.Count == 0)
            {
                footerStatus = L(
                    "rule_preset.validation.unavailable",
                    "규칙 설정 데이터를 찾을 수 없습니다.");
                RefreshFooter();
                return;
            }

            if (physicalDetailTitle != null)
                physicalDetailTitle.text = L(
                    "rule_preset.attachment_setup.title",
                    "장착 방식 설정");
            if (physicalDetailBody != null)
                physicalDetailBody.text = L(
                    "rule_preset.attachment_setup.body",
                    "장착 위치를 고르면 필요한 개념과 트리플을 자동으로 준비한 뒤 규칙을 적용합니다.");
            if (physicalDetailProfileDropdown != null)
            {
                physicalDetailProfileDropdown.gameObject.SetActive(true);
                physicalDetailProfileDropdown.ClearOptions();
                physicalDetailProfileDropdown.AddOptions(profiles
                    .Select(value => OntologyLanguagePackService.AttachmentProfileName(
                        value.profileId))
                    .ToList());
                physicalDetailProfileDropdown.SetValueWithoutNotify(0);
                physicalDetailProfileDropdown.RefreshShownValue();
            }
            if (physicalDetailApplyButton != null)
            {
                physicalDetailApplyButton.onClick.RemoveAllListeners();
                physicalDetailApplyButton.onClick.AddListener(() =>
                {
                    var index = physicalDetailProfileDropdown == null
                        ? 0
                        : Mathf.Clamp(
                            physicalDetailProfileDropdown.value,
                            0,
                            profiles.Count - 1);
                    if (!controller.ConfigureSelectedAttachmentBehavior(
                            profiles[index].profileId))
                    {
                        footerStatus = L(
                            "rule_preset.validation.unavailable",
                            "규칙 설정 데이터를 찾을 수 없습니다.");
                        RefreshFooter();
                        return;
                    }

                    var issue = controller.GetSelectedRuleBlockPresetValidationMessage(
                        preset.presetId);
                    if (!string.IsNullOrWhiteSpace(issue))
                    {
                        footerStatus = issue;
                        RefreshFooter();
                        return;
                    }
                    if (controller.ApplySelectedRuleBlockPreset(preset.presetId))
                        ClosePhysicalDetail();
                });
            }
            physicalDetailPopup.SetActive(true);
        }

        private void OpenRulePresetDetail(OntologyRuleBlockPreset preset)
        {
            if (physicalDetailPopup == null || preset == null) return;
            if (RequiresAttachmentSetup(preset))
            {
                OpenAttachmentSetup(preset);
                return;
            }
            if (RequiresTemporarySkillSetup(preset))
            {
                OpenTemporarySkillSetup(preset);
                return;
            }
            if (physicalDetailTitle != null)
                physicalDetailTitle.text = L(
                    "ui.title.rule_block_details",
                    "Rule block details");
            if (physicalDetailBody != null)
                physicalDetailBody.text = BuildRulePresetDetail(preset);
            if (physicalDetailProfileDropdown != null)
                physicalDetailProfileDropdown.gameObject.SetActive(false);
            if (physicalDetailApplyButton != null)
            {
                physicalDetailApplyButton.onClick.RemoveAllListeners();
                physicalDetailApplyButton.onClick.AddListener(() =>
                {
                    var issue = controller.GetSelectedRuleBlockPresetValidationMessage(
                        preset.presetId);
                    if (!string.IsNullOrWhiteSpace(issue))
                    {
                        footerStatus = issue;
                        RefreshFooter();
                        return;
                    }
                    if (controller.ApplySelectedRuleBlockPreset(preset.presetId))
                        ClosePhysicalDetail();
                });
            }
            physicalDetailPopup.SetActive(true);
        }

        private static string BuildRulePresetDetail(OntologyRuleBlockPreset preset)
        {
            var profile = string.IsNullOrWhiteSpace(preset.physicalProfileId)
                ? L("common.none", "none")
                : OntologyLanguagePackService.PhysicalProfileName(preset.physicalProfileId);
            var concepts = preset.requiredConcepts.Count == 0
                ? L("common.none", "none")
                : string.Join(", ", preset.requiredConcepts.Select(
                    OntologyLanguagePackService.Term));
            var facts = preset.requiredFacts.Count == 0
                ? L("common.none", "none")
                : string.Join("\n", preset.requiredFacts.Select(value =>
                    OntologyLanguagePackService.Term(value.predicate) +
                    " → " + OntologyLanguagePackService.Term(value.obj)));
            var prerequisites = string.Join(", ",
                preset.requiredExistingConcepts
                    .Select(OntologyLanguagePackService.Term)
                    .Concat(preset.requiredExistingPredicates
                        .Select(OntologyLanguagePackService.Term)));
            if (string.IsNullOrWhiteSpace(prerequisites))
                prerequisites = L("common.none", "none");
            return string.Format(
                L(
                    "rule_preset.detail.format",
                    "{0}\n\nApplied to this placed object only.\n\nRule block: {1}\nBinding: {2}\nPhysical behavior: {3}\nConcepts: {4}\nFacts:\n{5}\nRequired existing data: {6}\n\nTemplates provide defaults only. Editing here never changes the source template."),
                L(preset.descriptionKey, preset.primaryRuleId),
                OntologyLanguagePackService.RuleName(preset.primaryRuleId),
                preset.bindingVariable,
                profile,
                concepts,
                facts,
                prerequisites);
        }

        private void ShowPhysicalBehavior()
        {
            var content = physicalContent != null ? physicalContent : tripleContent;
            if (content == null || ruleRowTemplate == null || controller?.Selected == null) return;
            var profiles = controller.AvailablePhysicalProfiles
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.profileId))
                .OrderBy(value =>
                    value.mobilityMode == OntologyPhysicalMobilityMode.Anchored ? 3 :
                    value.supportsBuoyancy ? 1 : 0)
                .ThenBy(value => value.mass)
                .ToList();
            if (profiles.Count == 0)
            {
                AddResultRow(content, L(
                    "result.no_physical_profile",
                    "No physical behavior is registered."));
                return;
            }

            var current = controller.GetSelectedPhysicalProfile();
            AddBasePhysicalProfileRow(content, profiles, current);

            var selectedEffects = controller.GetSelectedPhysicalEffects()
                .Where(value => value != null)
                .OrderBy(value => PhysicalEffectName(value))
                .ToList();
            foreach (var effect in selectedEffects)
                AddSelectedPhysicalEffectRow(content, effect);

            if (current != null)
            {
                var selectedIds = selectedEffects
                    .Select(value => value.effectId)
                    .ToHashSet();
                var availableEffects = controller
                    .GetCompatiblePhysicalEffects(current.profileId)
                    .Where(value =>
                        value != null &&
                        !selectedIds.Contains(value.effectId))
                    .OrderBy(value => PhysicalEffectName(value))
                    .ToList();
                if (availableEffects.Count > 0)
                    AddPhysicalEffectPickerRow(content, availableEffects);
                else
                    AddResultRow(content, L(
                        "result.no_additional_physical_effect",
                        "No more compatible effects can be added."));
            }

            AddResultRow(content,
                current == null
                    ? L(
                        "result.no_physical_behavior",
                        "No physical behavior is selected.")
                    : string.Format(
                        L(
                            "result.expected_behavior",
                            "Expected result: {0}"),
                        PhysicalExpectedResult(current)));
        }

        private void AddBasePhysicalProfileRow(
            RectTransform content,
            IReadOnlyList<OntologyPhysicalProfile> profiles,
            OntologyPhysicalProfile current)
        {
            var template = physicalBaseProfileTemplate != null
                ? physicalBaseProfileTemplate
                : ruleRowTemplate;
            var row = Instantiate(template, content);
            row.name = "PhysicalBaseProfile";
            row.SetActive(true);
            PreparePhysicalRow(
                row,
                L("ui.physical.base_behavior", "Base physical behavior"),
                out var dropdown,
                out var apply,
                out var details);

            var profileIds = profiles.Select(value => value.profileId).ToList();
            if (dropdown != null)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(profiles.Select(PhysicalChoiceName).ToList());
                var selectedIndex = current == null
                    ? 0
                    : profileIds.IndexOf(current.profileId);
                dropdown.SetValueWithoutNotify(
                    selectedIndex >= 0 ? selectedIndex : 0);
                dropdown.RefreshShownValue();
                dropdown.interactable = true;
            }
            if (apply != null)
            {
                apply.interactable = true;
                apply.onClick.RemoveAllListeners();
                apply.onClick.AddListener(() =>
                {
                    var index = dropdown == null ? 0 : dropdown.value;
                    controller.SetSelectedPhysicalBehavior(
                        profileIds[Mathf.Clamp(index, 0, profileIds.Count - 1)]);
                });
            }
            if (details != null)
            {
                details.interactable = true;
                details.onClick.RemoveAllListeners();
                details.onClick.AddListener(() =>
                {
                    var index = dropdown == null ? 0 : dropdown.value;
                    OpenPhysicalDetail(
                        profiles[Mathf.Clamp(index, 0, profiles.Count - 1)],
                        profiles);
                });
            }
        }

        private void AddPhysicalEffectPickerRow(
            RectTransform content,
            IReadOnlyList<OntologyPhysicalEffectProfile> effects)
        {
            var template = physicalEffectPickerTemplate != null
                ? physicalEffectPickerTemplate
                : ruleRowTemplate;
            var row = Instantiate(template, content);
            row.name = "PhysicalEffectPicker";
            row.SetActive(true);
            PreparePhysicalRow(
                row,
                L("ui.physical.additional_effect", "Additional effect"),
                out var dropdown,
                out var apply,
                out var details);

            var effectIds = effects.Select(value => value.effectId).ToList();
            if (dropdown != null)
            {
                dropdown.gameObject.SetActive(true);
                dropdown.ClearOptions();
                dropdown.AddOptions(effects.Select(PhysicalEffectName).ToList());
                dropdown.SetValueWithoutNotify(0);
                dropdown.RefreshShownValue();
                dropdown.interactable = true;
            }
            if (apply != null)
            {
                apply.interactable = true;
                apply.onClick.RemoveAllListeners();
                apply.onClick.AddListener(() =>
                {
                    var index = dropdown == null ? 0 : dropdown.value;
                    controller.AddSelectedPhysicalEffect(
                        effectIds[Mathf.Clamp(index, 0, effectIds.Count - 1)]);
                });
            }
        }

        private void AddSelectedPhysicalEffectRow(
            RectTransform content,
            OntologyPhysicalEffectProfile effect)
        {
            var template = physicalEffectRowTemplate != null
                ? physicalEffectRowTemplate
                : ruleRowTemplate;
            var row = Instantiate(template, content);
            row.name = "PhysicalEffect_" + effect.effectId;
            row.SetActive(true);
            PreparePhysicalRow(
                row,
                PhysicalEffectName(effect),
                out var dropdown,
                out var apply,
                out var remove);
            if (apply != null)
            {
                apply.interactable = false;
            }
            if (remove != null)
            {
                remove.interactable = true;
                remove.onClick.RemoveAllListeners();
                remove.onClick.AddListener(() =>
                    controller.RemoveSelectedPhysicalEffect(effect.effectId));
            }
        }

        private static void PreparePhysicalRow(
            GameObject row,
            string label,
            out TMP_Dropdown dropdown,
            out Button apply,
            out Button secondary)
        {
            var objectPill = row.transform.Find("ObjectPill");
            var objectText = objectPill?.Find("ObjectText")
                ?.GetComponent<TextMeshProUGUI>();
            dropdown = row.transform.Find("RuleDropdown")
                ?.GetComponent<TMP_Dropdown>();
            apply = FindActionButton(row.transform, "ApplyButton");
            secondary = FindActionButton(row.transform, "DeleteButton");

            if (objectText != null)
            {
                objectText.text = label;
            }
        }

        private void OpenPhysicalDetail(
            OntologyPhysicalProfile profile,
            IReadOnlyList<OntologyPhysicalProfile> profiles)
        {
            if (physicalDetailPopup == null || profile == null) return;
            if (physicalDetailProfileDropdown != null)
                physicalDetailProfileDropdown.gameObject.SetActive(true);
            physicalDetailProfileIds.Clear();
            physicalDetailProfileIds.AddRange(
                profiles.Select(value => value.profileId));

            if (physicalDetailTitle != null)
                physicalDetailTitle.text = L(
                    "ui.title.physical_details",
                    "Physical behavior details");
            if (physicalDetailProfileDropdown != null)
            {
                physicalDetailProfileDropdown.onValueChanged.RemoveAllListeners();
                physicalDetailProfileDropdown.ClearOptions();
                physicalDetailProfileDropdown.AddOptions(
                    profiles.Select(PhysicalChoiceName).ToList());
                var index = physicalDetailProfileIds.IndexOf(profile.profileId);
                physicalDetailProfileDropdown.SetValueWithoutNotify(
                    index >= 0 ? index : 0);
                physicalDetailProfileDropdown.RefreshShownValue();
                physicalDetailProfileDropdown.onValueChanged.AddListener(
                    RefreshPhysicalDetailSelection);
            }
            if (physicalDetailApplyButton != null)
            {
                physicalDetailApplyButton.onClick.RemoveAllListeners();
                physicalDetailApplyButton.onClick.AddListener(() =>
                {
                    if (physicalDetailProfileDropdown == null ||
                        physicalDetailProfileIds.Count == 0)
                        return;
                    var selectedIndex = Mathf.Clamp(
                        physicalDetailProfileDropdown.value,
                        0,
                        physicalDetailProfileIds.Count - 1);
                    if (controller.SetSelectedPhysicalBehavior(
                            physicalDetailProfileIds[selectedIndex]))
                    {
                        ClosePhysicalDetail();
                    }
                });
            }
            physicalDetailPopup.SetActive(true);
            RefreshPhysicalDetailSelection(
                physicalDetailProfileDropdown == null
                    ? 0
                    : physicalDetailProfileDropdown.value);
        }

        private void RefreshPhysicalDetailSelection(int index)
        {
            if (physicalDetailBody == null ||
                controller == null ||
                physicalDetailProfileIds.Count == 0)
                return;
            var profileId = physicalDetailProfileIds[
                Mathf.Clamp(index, 0, physicalDetailProfileIds.Count - 1)];
            var profile = controller.AvailablePhysicalProfiles.FirstOrDefault(
                value => value != null && value.profileId == profileId);
            physicalDetailBody.text = BuildPhysicalDetail(profile);
        }

        private void ClosePhysicalDetail()
        {
            if (physicalDetailPopup != null)
                physicalDetailPopup.SetActive(false);
            physicalDetailProfileIds.Clear();
        }

        private static string PhysicalChoiceName(OntologyPhysicalProfile profile)
        {
            if (profile == null) return string.Empty;
            return OntologyLanguagePackService.Text(
                "physical_choice." + profile.profileId,
                profile.mobilityMode == OntologyPhysicalMobilityMode.Anchored
                    ? "Keep this object fixed in place"
                    : profile.supportsBuoyancy
                        ? profile.mass >= 2f
                            ? "Float strongly on the water"
                            : "Float lightly on the water"
                        : "Sink when it enters the water");
        }

        private static string PhysicalExpectedResult(OntologyPhysicalProfile profile)
        {
            if (profile == null) return string.Empty;
            return OntologyLanguagePackService.Text(
                "physical_result." + profile.profileId,
                PhysicalChoiceName(profile));
        }

        private static string PhysicalEffectName(
            OntologyPhysicalEffectProfile effect)
        {
            if (effect == null) return string.Empty;
            return OntologyLanguagePackService.Text(
                "physical_effect." + effect.effectId,
                effect.effectId);
        }

        private static string BuildPhysicalDetail(OntologyPhysicalProfile profile)
        {
            if (profile == null) return string.Empty;
            var concept = profile.supportsBuoyancy
                ? OntologyConcepts.FloatableObject
                : L("common.none", "none");
            var rule = profile.supportsBuoyancy &&
                       !string.IsNullOrWhiteSpace(profile.buoyancyRuleId)
                ? profile.buoyancyRuleId
                : L("common.none", "none");
            return string.Format(
                L(
                    "physical_detail.format",
                    "Expected result\n{0}\n\nAutomatically configured ontology\nConcept: {1}\nPhysical profile: {2}\nRule block: {3}\n\nPhysical tuning\nMass: {4:0.##}\nBuoyancy strength: {5:0.##}\nWater damping: {6:0.##}\nSubmerged ratio: {7:0%}\nSurface offset: {8:0.##}"),
                PhysicalExpectedResult(profile),
                OntologyLanguagePackService.Term(concept),
                OntologyLanguagePackService.PhysicalProfileName(profile.profileId),
                OntologyLanguagePackService.RuleName(rule),
                profile.mass,
                profile.buoyancyStrength,
                profile.submergedDamping,
                profile.submergedFraction,
                profile.surfaceOffset);
        }

        private void ClearPendingRule()
        {
            pendingRuleOwner = null;
            pendingRuleId = null;
            pendingRuleVariable = null;
        }

        private void AddRuleRow(string originalRuleId, string originalVariable, bool isNew)
        {
            if (ruleRowTemplate == null || controller?.Selected == null) return;
            var definitions = controller.AvailableRuleDefinitions
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.id))
                .ToList();
            if (definitions.Count == 0)
            {
                AddResultRow(L(
                    "result.no_rules",
                    "No rules are available in the Rule Database."));
                return;
            }

            var row = Instantiate(ruleRowTemplate, tripleContent);
            row.name = isNew ? "RuleRow_New" : "RuleRow";
            row.SetActive(true);

            var objectText = row.transform.Find("ObjectPill/ObjectText")?.GetComponent<TextMeshProUGUI>();
            var rule = row.transform.Find("RuleDropdown")?.GetComponent<TMP_Dropdown>();
            var variable = row.transform.Find("VariableDropdown")?.GetComponent<TMP_Dropdown>();
            var apply = FindActionButton(row.transform, "ApplyButton");
            var delete = FindActionButton(row.transform, "DeleteButton");
            if (objectText != null) objectText.text = controller.SelectedDisplayName;

            var ruleIds = definitions.Select(value => value.id).ToList();
            if (rule != null)
            {
                rule.ClearOptions();
                rule.AddOptions(ruleIds
                    .Select(OntologyLanguagePackService.RuleName)
                    .ToList());
                var index = ruleIds.IndexOf(originalRuleId);
                rule.value = index >= 0 ? index : 0;
                rule.RefreshShownValue();
            }

            void RefreshVariables(int ruleIndex)
            {
                if (variable == null) return;
                var selectedRuleId = ruleIds[Mathf.Clamp(ruleIndex, 0, ruleIds.Count - 1)];
                var variables = controller.GetRuleVariables(selectedRuleId);
                variable.ClearOptions();
                variable.AddOptions(variables.Select(DisplayVariable).ToList());
                var variableIndex = variables.IndexOf(originalVariable);
                if (variableIndex < 0)
                    variableIndex = variables.IndexOf("?target");
                variable.value = variableIndex >= 0 ? variableIndex : 0;
                variable.RefreshShownValue();
                variable.interactable = variables.Count > 1;
            }

            if (rule != null)
            {
                rule.onValueChanged.RemoveAllListeners();
                rule.onValueChanged.AddListener(ruleIndex =>
                {
                    RefreshVariables(ruleIndex);
                    if (!isNew) return;
                    pendingRuleId = ruleIds[Mathf.Clamp(ruleIndex, 0, ruleIds.Count - 1)];
                    var variables = controller.GetRuleVariables(pendingRuleId);
                    pendingRuleVariable = variables.Count > 0
                        ? variables[Mathf.Clamp(variable != null ? variable.value : 0, 0, variables.Count - 1)]
                        : null;
                });
                rule.interactable = isNew;
                RefreshVariables(rule.value);
            }

            if (variable != null && isNew)
            {
                variable.onValueChanged.RemoveAllListeners();
                variable.onValueChanged.AddListener(variableIndex =>
                {
                    var selectedRuleId = ruleIds[Mathf.Clamp(rule != null ? rule.value : 0, 0, ruleIds.Count - 1)];
                    var variables = controller.GetRuleVariables(selectedRuleId);
                    if (variables.Count == 0) return;
                    pendingRuleId = selectedRuleId;
                    pendingRuleVariable = variables[Mathf.Clamp(variableIndex, 0, variables.Count - 1)];
                });
            }

            if (apply != null)
            {
                apply.onClick.RemoveAllListeners();
                apply.onClick.AddListener(() =>
                {
                    var ruleIndex = rule != null ? rule.value : 0;
                    var nextRuleId = ruleIds[Mathf.Clamp(ruleIndex, 0, ruleIds.Count - 1)];
                    var variables = controller.GetRuleVariables(nextRuleId);
                    if (variables.Count == 0) return;
                    var variableIndex = variable != null ? variable.value : 0;
                    var nextVariable = variables[Mathf.Clamp(variableIndex, 0, variables.Count - 1)];
                    if (!isNew)
                        controller.RemoveSelectedRuleBlock(originalRuleId, originalVariable);
                    else
                        ClearPendingRule();
                    if (!controller.AddSelectedRuleBlock(nextRuleId, nextVariable) && isNew)
                    {
                        pendingRuleOwner = controller.Selected;
                        pendingRuleId = nextRuleId;
                        pendingRuleVariable = nextVariable;
                        Refresh();
                    }
                });
            }

            if (delete != null)
            {
                delete.onClick.RemoveAllListeners();
                delete.onClick.AddListener(() =>
                {
                    if (isNew)
                    {
                        ClearPendingRule();
                        Refresh();
                    }
                    else controller.RemoveSelectedRuleBlock(originalRuleId, originalVariable);
                });
            }
        }

        private void ShowResults(OntologyObject ontology)
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap == null || bootstrap.World == null)
            {
                AddResultRow(L(
                    "result.world_unavailable",
                    "The ontology world has not been initialized."));
                return;
            }

            var result = bootstrap.LastResult;
            AddResultRow(result == null
                ? L("result.not_run", "Rules have not been evaluated yet.")
                : (result.ReachedStableState
                    ? L("result.stable", "All rules have been applied.")
                    : L(
                        "result.iteration_limit",
                        "Rule evaluation stopped at the iteration limit.")) +
                  "\n" +
                  string.Format(
                      L(
                          "result.summary",
                          "Iterations: {0} · New information: {1} · Changed information: {2}"),
                      result.Iterations,
                      result.TotalAddedFacts,
                      result.TotalChangedFacts));

            var entityId = ontology.EntityId;
            foreach (var fact in bootstrap.World.Facts
                         .Where(value => value.Subject.Value == entityId)
                         .OrderBy(value => value.Predicate.Value)
                         .ThenBy(value => value.Object.Value))
                AddResultRow(
                    L("result.current_information", "Current information") +
                    " · " +
                    FactOriginLabel(fact, ontology) +
                    "\n" +
                    OntologyLanguagePackService.FormatFact(
                        fact,
                        controller.GetDisplayNameForEntity));

            if (result == null) return;
            foreach (var step in result.Steps)
            foreach (var ontologyEvent in step.Events.Where(value =>
                         value != null &&
                         (value.EventType.EndsWith("@" + entityId) || EventTouches(value, entityId))))
                AddResultRow(
                    string.Format(
                        L("result.applied_rule", "Applied rule · step {0}"),
                        step.Iteration) +
                    "\n" +
                    FormatEvent(ontologyEvent));
        }

        private static bool EventTouches(OntologyEvent ontologyEvent, string entityId)
        {
            return ontologyEvent.AddedFacts.Any(value => value.Subject.Value == entityId) ||
                   ontologyEvent.RemovedFacts.Any(value => value.Subject.Value == entityId) ||
                   ontologyEvent.SetFacts.Any(value => value.Subject.Value == entityId) ||
                   ontologyEvent.AdjustedNumberFacts.Any(value => value.Subject.Value == entityId);
        }

        private string FactOriginLabel(OntologyFact fact, OntologyObject ontology)
        {
            if (ontology != null &&
                (fact.Predicate.Value == OntologyPredicates.HasConcept ||
                 ontology.Facts.Any(value => value != null &&
                     value.predicate == fact.Predicate.Value &&
                     value.obj == fact.Object.Value)))
            {
                return L("result.origin.authored", "Authored");
            }

            if (OntologyDerivedFactPolicy.IsDerived(
                    fact,
                    bootstrap == null ? null : bootstrap.AllRuleDefinitions))
            {
                return L("result.origin.inferred", "Inferred");
            }

            return L("result.origin.observed", "Observed");
        }

        private string FormatEvent(OntologyEvent ontologyEvent)
        {
            var lines = new List<string>();
            lines.AddRange(ontologyEvent.AddedFacts.Select(value =>
                L("result.added", "Added") +
                ": " +
                OntologyLanguagePackService.FormatFact(
                    value,
                    controller.GetDisplayNameForEntity)));
            lines.AddRange(ontologyEvent.RemovedFacts.Select(value =>
                L("result.removed", "Removed") +
                ": " +
                OntologyLanguagePackService.FormatFact(
                    value,
                    controller.GetDisplayNameForEntity)));
            lines.AddRange(ontologyEvent.SetFacts
                .Concat(ontologyEvent.AdjustedNumberFacts)
                .Select(value =>
                    L("result.changed", "Changed") +
                    ": " +
                    OntologyLanguagePackService.FormatFact(
                        value,
                        controller.GetDisplayNameForEntity)));
            if (lines.Count > 0)
                return string.Join("\n", lines);
            var eventRuleId = ontologyEvent.EventType?.Split('@')[0];
            return OntologyLanguagePackService.RuleDescription(
                eventRuleId,
                string.IsNullOrWhiteSpace(ontologyEvent.Reason)
                    ? OntologyLanguagePackService.RuleName(eventRuleId)
                    : ontologyEvent.Reason);
        }

        private void AddResultRow(string text)
        {
            AddResultRow(resultContent != null ? resultContent : tripleContent, text);
        }

        private void AddResultRow(RectTransform content, string text)
        {
            if (resultRowTemplate == null || content == null) return;
            var row = Instantiate(resultRowTemplate, content);
            row.name = "ResultRow";
            row.SetActive(true);
            var label = row.transform.Find("ResultText")?.GetComponent<TextMeshProUGUI>();
            if (label == null) return;
            label.text = text;
        }

        private static string DisplayVariable(string variable)
        {
            if (string.IsNullOrWhiteSpace(variable))
                return "-";
            var canonical = variable.TrimStart('?');
            return OntologyLanguagePackService.Text(
                "variable." + canonical.ToLowerInvariant(),
                canonical);
        }

        private void AddRow(
            string originalRelation,
            string originalObject,
            bool originalWasConcept,
            bool isNew)
        {
            if (tripleContent == null || tripleRowTemplate == null || controller?.Selected == null)
                return;

            var row = Instantiate(tripleRowTemplate, tripleContent);
            row.name = isNew ? "TripleRow_New" : "TripleRow";
            row.SetActive(true);

            var subject = row.transform.Find("SubjectPill/SubjectText")?.GetComponent<TextMeshProUGUI>();
            var relation = row.transform.Find("RelationField")?.GetComponent<TMP_Dropdown>();
            var obj = row.transform.Find("ObjectField")?.GetComponent<TMP_InputField>();
            var keyboardButton = row.transform
                .Find("ObjectField/KeyboardButton")
                ?.GetComponent<Button>();
            var objectDropdown = row.transform
                .Find("ObjectField/ObjectChoiceDropdown")
                ?.GetComponent<TMP_Dropdown>();
            var objectDropdownHitArea = row.transform
                .Find("ObjectField/ObjectChoiceDropdown/HitArea")
                ?.GetComponent<Button>();
            var save = FindActionButton(row.transform, "SaveButton");
            var delete = FindActionButton(row.transform, "DeleteButton");

            if (subject != null) subject.text = controller.SelectedDisplayName;
            if (relation != null)
            {
                relation.ClearOptions();
                relation.AddOptions(RelationChoices
                    .Select(OntologyLanguagePackService.Term)
                    .ToList());
                var relationIndex = RelationChoices.FindIndex(value =>
                    string.Equals(
                        value,
                        originalRelation,
                        System.StringComparison.OrdinalIgnoreCase));
                relation.value = relationIndex >= 0 ? relationIndex : 0;
                relation.RefreshShownValue();
                relation.onValueChanged.RemoveAllListeners();
                relation.onValueChanged.AddListener(index =>
                    ConfigureObjectChoiceDropdown(
                        objectDropdown,
                        obj,
                        RelationChoices[
                            Mathf.Clamp(index, 0, RelationChoices.Count - 1)]));
            }
            if (obj != null)
            {
                // Keep the canonical identifier in the world data, but present rule
                // identifiers through the language pack when this relation points to a rule.
                obj.text = DisplayObjectValue(originalObject, originalRelation);
                obj.readOnly = true;
                obj.onEndEdit.RemoveAllListeners();
                obj.onEndEdit.AddListener(_ =>
                {
                    obj.readOnly = true;
                    SetObjectFieldEditing(keyboardButton, false);
                });
            }

            if (keyboardButton != null)
            {
                keyboardButton.onClick.RemoveAllListeners();
                keyboardButton.onClick.AddListener(() =>
                {
                    if (obj == null) return;
                        SetObjectFieldEditing(keyboardButton, true);
                    obj.readOnly = false;
                    obj.Select();
                    obj.ActivateInputField();
                    obj.caretPosition = obj.text.Length;
                });
                SetObjectFieldEditing(keyboardButton, false);
            }

            ConfigureObjectChoiceDropdown(
                objectDropdown,
                obj,
                relation != null
                    ? RelationChoices[
                        Mathf.Clamp(relation.value, 0, RelationChoices.Count - 1)]
                    : originalRelation);

            if (objectDropdownHitArea != null)
            {
                objectDropdownHitArea.onClick.RemoveAllListeners();
                objectDropdownHitArea.onClick.AddListener(() =>
                {
                    if (objectDropdown != null &&
                        objectDropdown.interactable &&
                        !objectDropdown.IsExpanded)
                        objectDropdown.Show();
                });

                var pointerTrigger = objectDropdownHitArea.GetComponent<EventTrigger>();
                if (pointerTrigger == null)
                    pointerTrigger = objectDropdownHitArea.gameObject.AddComponent<EventTrigger>();
                pointerTrigger.triggers.Clear();
                var pointerDown = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerDown
                };
                pointerDown.callback.AddListener(_ =>
                {
                    if (objectDropdown != null &&
                        objectDropdown.interactable &&
                        !objectDropdown.IsExpanded)
                        objectDropdown.Show();
                });
                pointerTrigger.triggers.Add(pointerDown);
            }

            if (save != null)
            {
                save.onClick.RemoveAllListeners();
                save.onClick.AddListener(() =>
                    SaveRow(
                        originalRelation,
                        originalObject,
                        originalWasConcept,
                        isNew,
                        relation != null ? relation.value : 0,
                        obj != null ? obj.text : string.Empty));
            }

            if (delete != null)
            {
                delete.onClick.RemoveAllListeners();
                delete.onClick.AddListener(() =>
                {
                    if (isNew) Destroy(row);
                    else DeleteRow(originalRelation, originalObject, originalWasConcept);
                });
            }
        }

        private static void SetObjectFieldEditing(
            Button keyboardButton,
            bool editing)
        {
            if (keyboardButton != null)
                keyboardButton.gameObject.SetActive(!editing);
        }

        private static Button FindActionButton(Transform row, string buttonName)
        {
            if (row == null || string.IsNullOrEmpty(buttonName))
                return null;

            var nested = row.Find("ActionButtons/" + buttonName);
            if (nested != null)
                return nested.GetComponent<Button>();

            return row.Find(buttonName)?.GetComponent<Button>();
        }

        private void ConfigureObjectChoiceDropdown(
            TMP_Dropdown dropdown,
            TMP_InputField objectField,
            string relation)
        {
            if (dropdown == null) return;
            var candidates = controller == null
                ? new List<string>()
                : controller.GetObjectCandidatesForRelation(relation).ToList();
            var options = new List<string>
            {
                L("ui.dropdown.choose", "Choose...")
            };
            options.AddRange(candidates.Select(value => DisplayObjectValue(value, relation)));
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            // Keep the authored dropdown hit area usable even when this relation has
            // no catalogued candidates yet. The menu will show the placeholder only,
            // while the adjacent keyboard button remains the path for a new value.
            dropdown.interactable = true;
            dropdown.onValueChanged.AddListener(index =>
            {
                if (index <= 0 || index >= options.Count || objectField == null)
                    return;
                objectField.SetTextWithoutNotify(
                    DisplayObjectValue(candidates[index - 1], relation));
                objectField.readOnly = true;
                dropdown.SetValueWithoutNotify(0);
                dropdown.RefreshShownValue();
            });
        }

        private void SaveRow(
            string oldRelation,
            string oldObject,
            bool oldWasConcept,
            bool isNew,
            int relationIndex,
            string obj)
        {
            var relation = RelationChoices[Mathf.Clamp(relationIndex, 0, RelationChoices.Count - 1)];
            var resolution = OntologyLanguagePackService.ResolveObjectInput(
                obj,
                relation,
                controller?.GetObjectCandidatesForRelation(relation));
            if (!resolution.Success)
            {
                footerStatus = resolution.Message;
                RefreshFooter();
                return;
            }

            obj = resolution.CanonicalValue;
            if (string.IsNullOrWhiteSpace(relation) ||
                string.IsNullOrWhiteSpace(obj) ||
                controller == null)
                return;

            if (relation == "has_concept")
            {
                // The previous Concept is still meaningful validation context while
                // replacing a row. For example, Plant -> Rock should warn even though
                // Plant will be removed after the user confirms the replacement.
                var validation = controller.ValidateSelectedConcept(obj);
                if (validation.HasWarning)
                {
                    var selectedId = controller.Selected != null
                        ? controller.Selected.GetInstanceID().ToString()
                        : "none";
                    var confirmationKey = selectedId + "|" + oldObject + "|" + obj;
                    if (validation.BlocksSave)
                    {
                        pendingConceptConfirmationKey = null;
                        footerStatus = string.Format(
                            L("validation.blocked", "BLOCKED: {0}"),
                            validation.Message);
                        RefreshFooter();
                        return;
                    }

                    if (pendingConceptConfirmationKey != confirmationKey)
                    {
                        pendingConceptConfirmationKey = confirmationKey;
                        footerStatus = string.Format(
                            L(
                                "validation.warning",
                                "WARNING: {0} Press OK again to confirm."),
                            validation.Message);
                        RefreshFooter();
                        return;
                    }
                }
            }

            pendingConceptConfirmationKey = null;
            footerStatus = null;
            if (!isNew)
            {
                if (oldWasConcept) controller.RemoveSelectedConcept(oldObject);
                else controller.RemoveSelectedFact(oldRelation, oldObject);
            }

            if (relation == "has_concept") controller.AddSelectedConcept(obj);
            else controller.AddSelectedFact(relation, obj);
        }

        private void DeleteRow(string relation, string obj, bool isConcept)
        {
            if (isConcept) controller?.RemoveSelectedConcept(obj);
            else controller?.RemoveSelectedFact(relation, obj);
        }

        private string DisplayObjectValue(string canonicalValue, string relation = null)
        {
            if (string.IsNullOrWhiteSpace(canonicalValue))
                return string.Empty;

            // Rule IDs are stable storage values, not player-facing labels.  Both
            // relations reference a rule definition, so resolve their display name
            // through the language pack without changing the saved canonical ID.
            if (relation == OntologyPredicates.SkillGrantRequiresRule ||
                relation == OntologyPredicates.HasRuleBlock)
            {
                return OntologyLanguagePackService.RuleName(canonicalValue);
            }

            var entityName = controller?.GetDisplayNameForEntity(canonicalValue);
            return string.IsNullOrWhiteSpace(entityName)
                ? OntologyLanguagePackService.Term(canonicalValue)
                : entityName;
        }
    }
}
