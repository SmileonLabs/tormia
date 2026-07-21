using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyDebugPanel : OntologyUIPanelBase
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private TMP_Text outputText;
        [SerializeField] private Button runButton;
        [SerializeField] private Button attackButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button filterButton;
        [SerializeField] private OntologySaveController saveController;
        [SerializeField] private RectTransform actionButtonContainer;
        [SerializeField] private Button actionButtonTemplate;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text toastText;
        [SerializeField] private TMP_Text graphSummaryText;

        private readonly List<GameObject> actionButtonObjects = new();
        private ActionFilter actionFilter;

        private enum ActionFilter
        {
            All,
            Quest,
            Parts,
            World
        }

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            EnsureUiReferences();
            ApplyTheme();

            if (runButton != null)
            {
                runButton.onClick.AddListener(RunSimulation);
            }

            if (attackButton != null)
            {
                attackButton.onClick.AddListener(AttackTreeWithFireSword);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(ResetWorld);
            }

            if (saveButton != null)
            {
                saveButton.onClick.AddListener(SaveSnapshot);
            }

            if (loadButton != null)
            {
                loadButton.onClick.AddListener(LoadSnapshot);
            }

            if (filterButton != null)
            {
                filterButton.onClick.AddListener(CycleActionFilter);
            }
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
            OntologyLanguagePackService.EnsureKoreanFontFallback(transform);
            ApplyTheme();
            RefreshGraphSummary();
            RefreshActionButtons();
        }

        private void Start()
        {
            ResetWorld();
        }

        public void RunSimulation()
        {
            InjectCharacterPartFacts();
            SetOutput(EnsureBootstrap() != null
                ? bootstrap.RunSimulation()
                : Labels.noBootstrap);
            SetToast(Labels.toastRan);
            RefreshGraphSummary();
            RefreshActionButtons();
        }

        public void AttackTreeWithFireSword()
        {
            InjectCharacterPartFacts();
            SetOutput(EnsureBootstrap() != null
                ? bootstrap.AttackTargetWithTool()
                : Labels.noBootstrap);
            RefreshGraphSummary();
            RefreshActionButtons();
        }

        public void ResetWorld()
        {
            SetOutput(EnsureBootstrap() != null
                ? bootstrap.ResetWorld(logReport: false)
                : Labels.noBootstrap);
            ApplyDefaultCharacterParts();
            InjectCharacterPartFacts();
            SetToast(Labels.toastReset);
            RefreshGraphSummary();
            ResetActorToastBaselines();
            RefreshActionButtons();
        }

        public void SaveSnapshot()
        {
            SetOutput(EnsureSaveController() != null
                ? saveController.SaveSnapshot()
                : Labels.noSaveController);
            SetToast(Labels.toastSaved);
            RefreshActionButtons();
        }

        public void LoadSnapshot()
        {
            SetOutput(EnsureSaveController() != null
                ? saveController.LoadSnapshot()
                : Labels.noSaveController);
            SetToast(Labels.toastLoaded);
            RefreshGraphSummary();
            ResetActorToastBaselines();
            RefreshActionButtons();
        }

        private void ExecuteCandidate(OntologyActionCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            InjectCharacterPartFacts();
            SetOutput(EnsureBootstrap() != null
                ? bootstrap.ExecuteAction(candidate.Action)
                : Labels.noBootstrap);
            SyncCharacterPartsFromWorld();
            InjectCharacterPartFacts();
            RefreshGraphSummary();
            RefreshActionButtons();
        }

        public void CycleActionFilter()
        {
            actionFilter = actionFilter == ActionFilter.World ? ActionFilter.All : actionFilter + 1;
            RefreshActionButtons();
        }

        private void ApplyDefaultCharacterParts()
        {
            foreach (var adapter in FindObjectsByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include))
            {
                adapter.ApplyDefaultPreset();
            }
        }

        private void InjectCharacterPartFacts()
        {
            foreach (var adapter in FindObjectsByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include))
            {
                adapter.InjectActivePartFacts();
            }
        }

        private void SyncCharacterPartsFromWorld()
        {
            foreach (var adapter in FindObjectsByType<OntologyCharacterPartAdapter>(FindObjectsInactive.Include))
            {
                adapter.SyncFromWorldFacts();
            }
        }

        private void ResetActorToastBaselines()
        {
            foreach (var emitter in FindObjectsByType<OntologyActorFactToastEmitter>(FindObjectsInactive.Include))
            {
                emitter.ResetBaseline();
            }
        }

        private OntologyWorldBootstrap EnsureBootstrap()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            return bootstrap;
        }

        private OntologySaveController EnsureSaveController()
        {
            if (saveController == null)
            {
                saveController = FindAnyObjectByType<OntologySaveController>();
            }

            return saveController;
        }

        private void EnsureUiReferences()
        {
            if (actionButtonTemplate == null)
            {
                actionButtonTemplate = transform.Find("ActionButtonContainer/ButtonsScrollView/Viewport/Buttons/ActionButtonTemplate")?.GetComponent<Button>();
            }
            if (titleText == null)
            {
                titleText = transform.Find("Title")?.GetComponent<TMP_Text>();
            }

            if (toastText == null)
            {
                toastText = transform.Find("ToastText")?.GetComponent<TMP_Text>();
            }

            if (graphSummaryText == null)
            {
                graphSummaryText = transform.Find("GraphSummaryText")?.GetComponent<TMP_Text>();
            }

            if (filterButton == null)
            {
                filterButton = transform.Find("ActionFilterButton")?.GetComponent<Button>();
            }
        }

        private void ApplyTheme()
        {
            var background = GetComponent<Image>();
            if (background != null)
            {
                background.color = Theme.panelBackground;
            }

            StyleText(titleText, Labels.debugTitle, Theme.titleFontSize, Theme.titleText, TextAlignmentOptions.Center);
            StyleText(outputText, outputText != null ? outputText.text : string.Empty, Theme.outputFontSize, Theme.outputText, TextAlignmentOptions.TopLeft);
            StyleText(toastText, toastText != null ? toastText.text : string.Empty, Theme.statusFontSize, Theme.statusText, TextAlignmentOptions.MidlineLeft);
            StyleText(graphSummaryText, graphSummaryText != null ? graphSummaryText.text : string.Empty, Theme.statusFontSize, Theme.statusText, TextAlignmentOptions.MidlineLeft);

            StyleButton(runButton, Labels.runSimulation, Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleButton(attackButton, Labels.attackTree, Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleButton(resetButton, Labels.resetWorld, Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleButton(saveButton, Labels.saveSnapshot, Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleButton(loadButton, Labels.loadSnapshot, Theme.fixedButton, Theme.buttonText, Theme.buttonFontSize);
            StyleFilterButton(0);
        }

        private void SetOutput(string text)
        {
            if (outputText != null)
            {
                outputText.text = text;
            }
        }

        private void SetToast(string value)
        {
            if (toastText != null)
            {
                toastText.text = value;
            }
        }

        private void RefreshActionButtons()
        {
            ClearActionButtons();
            if (actionButtonContainer == null || EnsureBootstrap() == null)
            {
                return;
            }

            var candidates = bootstrap.GetActionCandidates();
            var visibleCount = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!PassesFilter(candidates[i]))
                {
                    continue;
                }

                CreateActionButton(candidates[i], visibleCount);
                visibleCount++;
            }

            SetToast(string.Format(Labels.filterStatusFormat, GetFilterLabel(), visibleCount));
            StyleFilterButton(visibleCount);
        }

        private bool PassesFilter(OntologyActionCandidate candidate)
        {
            if (candidate == null || actionFilter == ActionFilter.All)
            {
                return true;
            }

            if (actionFilter == ActionFilter.Quest)
            {
                return candidate.IsQuestGoal;
            }

            var verb = candidate.Action.Verb.ToString();
            var isPartAction = verb == "equip_part" || verb == "unequip_part";
            return actionFilter == ActionFilter.Parts ? isPartAction : !isPartAction;
        }

        private string GetFilterLabel()
        {
            switch (actionFilter)
            {
                case ActionFilter.Quest:
                    return Labels.questActionsFilter;
                case ActionFilter.Parts:
                    return Labels.partActionsFilter;
                case ActionFilter.World:
                    return Labels.worldActionsFilter;
                default:
                    return Labels.allActionsFilter;
            }
        }

        private void StyleFilterButton(int visibleCount)
        {
            if (filterButton != null)
            {
                StyleButton(filterButton, string.Format(Labels.filterStatusFormat, GetFilterLabel(), visibleCount), Theme.actionButton, Theme.buttonText, Theme.buttonFontSize);
            }
        }

        private void RefreshGraphSummary()
        {
            if (graphSummaryText == null || EnsureBootstrap() == null || bootstrap.World == null)
            {
                return;
            }

            var subjects = new HashSet<string>();
            var predicates = new HashSet<string>();
            var factCount = 0;
            var questActions = 0;
            foreach (var fact in bootstrap.World.Facts)
            {
                factCount++;
                subjects.Add(fact.Subject.ToString());
                predicates.Add(fact.Predicate.ToString());
            }

            foreach (var candidate in bootstrap.GetActionCandidates())
            {
                if (candidate.IsQuestGoal)
                {
                    questActions++;
                }
            }

            graphSummaryText.text = string.Format(Labels.graphSummaryFormat, factCount, subjects.Count, predicates.Count)
                + " | " + string.Format(Labels.questSummaryFormat, questActions);
        }

        private void ClearActionButtons()
        {
            for (var i = actionButtonObjects.Count - 1; i >= 0; i--)
            {
                if (actionButtonObjects[i] != null)
                {
                    actionButtonObjects[i].SetActive(false);
                    DestroyImmediate(actionButtonObjects[i]);
                }
            }

            actionButtonObjects.Clear();

            if (actionButtonContainer == null)
            {
                return;
            }

            for (var i = actionButtonContainer.childCount - 1; i >= 0; i--)
            {
                var child = actionButtonContainer.GetChild(i);
                if (child != null && child.name.StartsWith("ActionButton_", System.StringComparison.Ordinal))
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void CreateActionButton(OntologyActionCandidate candidate, int index)
        {
            if (actionButtonTemplate == null || actionButtonContainer == null)
            {
                Debug.LogError("[OntologyDebugPanel] ActionButtonTemplate or container is missing from the hierarchy.", this);
                return;
            }

            var buttonObject = Instantiate(actionButtonTemplate, actionButtonContainer);
            buttonObject.gameObject.SetActive(true);
            buttonObject.name = "ActionButton_" + index;
            var image = buttonObject.GetComponent<Image>();
            if (image != null) image.color = Theme.actionButton;
            var label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return;
            var actionLabel =
                OntologyLanguagePackService.ActionLabel(candidate);
            label.text = candidate.IsQuestGoal
                ? Labels.questActionPrefix + actionLabel
                : actionLabel;
            label.color = candidate.IsQuestGoal ? Theme.questActionText : Theme.buttonText;

            buttonObject.onClick.RemoveAllListeners();
            buttonObject.onClick.AddListener(() => ExecuteCandidate(candidate));
            actionButtonObjects.Add(buttonObject.gameObject);
        }
    }
}
