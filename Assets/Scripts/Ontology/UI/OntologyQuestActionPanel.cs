using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only player panel for the quests inferred from the current
    /// ontology world and the currently available action candidates.
    ///
    /// All visual controls and layout are authored in the scene hierarchy.
    /// This component only clones authored row templates to match data count,
    /// binds labels/callbacks, and never creates gameplay rules or facts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyQuestActionPanel : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private OntologyWorldBootstrap bootstrap;

        [Header("Hierarchy-authored UI")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text questEmptyLabel;
        [SerializeField] private TMP_Text actionEmptyLabel;
        [SerializeField] private RectTransform questRowsContainer;
        [SerializeField] private RectTransform actionRowsContainer;
        [SerializeField] private Button questRowTemplate;
        [SerializeField] private Button actionRowTemplate;
        [SerializeField] private bool startsVisible;

        private readonly List<GameObject> spawnedQuestRows = new();
        private readonly List<GameObject> spawnedActionRows = new();

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            OntologyLanguagePackService.LanguageChanged -= Refresh;
        }

        public void Open()
        {
            SetVisible(true);
            Refresh();
        }

        public void Close() => SetVisible(false);

        public void Refresh()
        {
            ResolveDependencies();
            ApplyLocalizedStaticLabels();
            ClearRows(spawnedQuestRows);
            ClearRows(spawnedActionRows);

            if (bootstrap == null)
            {
                SetEmptyLabels(true, true);
                return;
            }

            var quests = bootstrap.GetGeneratedQuests();
            var actions = bootstrap.GetActionCandidates();
            BindQuestRows(quests);
            BindActionRows(actions);
        }

        private void BindButtons()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveAllListeners();
                openButton.onClick.AddListener(Open);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
        }

        private void BindQuestRows(IReadOnlyList<OntologyQuest> quests)
        {
            var hasQuests = quests != null && quests.Count > 0;
            SetEmptyLabels(!hasQuests, false);
            if (!hasQuests || questRowsContainer == null || questRowTemplate == null)
                return;

            for (var index = 0; index < quests.Count; index++)
            {
                var quest = quests[index];
                if (quest == null) continue;

                var row = Instantiate(questRowTemplate, questRowsContainer);
                row.name = "QuestRow_" + index;
                row.gameObject.SetActive(true);
                row.onClick.RemoveAllListeners();
                row.onClick.AddListener(() => FocusQuest(quest));

                var label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    var state = quest.IsCompleted
                        ? OntologyLanguagePackService.Text("ui.quest.completed", "Completed")
                        : OntologyLanguagePackService.Text("ui.quest.active", "Active");
                    label.text = "[" + state + "] " + quest.Title;
                }

                spawnedQuestRows.Add(row.gameObject);
            }
        }

        private void BindActionRows(IReadOnlyList<OntologyActionCandidate> actions)
        {
            var hasActions = actions != null && actions.Count > 0;
            SetEmptyLabels(false, !hasActions);
            if (!hasActions || actionRowsContainer == null || actionRowTemplate == null)
                return;

            for (var index = 0; index < actions.Count; index++)
            {
                var candidate = actions[index];
                if (candidate == null) continue;

                var row = Instantiate(actionRowTemplate, actionRowsContainer);
                row.name = "ActionRow_" + index;
                row.gameObject.SetActive(true);
                row.onClick.RemoveAllListeners();
                row.onClick.AddListener(() => ExecuteCandidate(candidate));

                var label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = candidate.IsQuestGoal
                        ? OntologyLanguagePackService.Text("ui.quest.goal_prefix", "Quest: ") + OntologyLanguagePackService.ActionLabel(candidate)
                        : OntologyLanguagePackService.ActionLabel(candidate);
                }

                spawnedActionRows.Add(row.gameObject);
            }
        }

        private void FocusQuest(OntologyQuest quest)
        {
            if (quest == null || actionEmptyLabel == null)
                return;

            actionEmptyLabel.gameObject.SetActive(true);
            actionEmptyLabel.text = string.IsNullOrWhiteSpace(quest.Reason)
                ? quest.Title
                : quest.Title + "\n" + quest.Reason;
        }

        private void ExecuteCandidate(OntologyActionCandidate candidate)
        {
            if (bootstrap == null || candidate == null)
                return;

            bootstrap.ExecuteAction(candidate.Action);
            Refresh();
        }

        private void ApplyLocalizedStaticLabels()
        {
            if (titleLabel != null)
                titleLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.title", "Quests & Actions");
            if (questEmptyLabel != null)
                questEmptyLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.empty_quests", "No active quests");
            if (actionEmptyLabel != null)
                actionEmptyLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.empty_actions", "No available actions");
        }

        private void SetEmptyLabels(bool questsEmpty, bool actionsEmpty)
        {
            if (questEmptyLabel != null)
                questEmptyLabel.gameObject.SetActive(questsEmpty);
            if (actionEmptyLabel != null)
                actionEmptyLabel.gameObject.SetActive(actionsEmpty);
        }

        private void ClearRows(List<GameObject> rows)
        {
            for (var index = rows.Count - 1; index >= 0; index--)
            {
                var row = rows[index];
                if (row != null) Destroy(row);
            }
            rows.Clear();
        }

        private void ResolveDependencies()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }
    }
}
