using System.Collections.Generic;
using System;
using System.Collections;
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
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;

        [Header("Hierarchy-authored UI")]
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text subtitleLabel;
        [SerializeField] private TMP_Text questHeaderLabel;
        [SerializeField] private TMP_Text actionHeaderLabel;
        [SerializeField] private TMP_Text questEmptyLabel;
        [SerializeField] private TMP_Text actionEmptyLabel;
        [SerializeField] private TMP_Text selectedQuestTitleLabel;
        [SerializeField] private TMP_Text selectedQuestReasonLabel;
        [SerializeField] private TMP_Text selectedQuestStatusLabel;
        [SerializeField] private Image selectedQuestProgressFill;
        [SerializeField] private TMP_Text selectedQuestProgressLabel;
        [SerializeField] private TMP_Text footerLabel;
        [SerializeField] private RectTransform questRowsContainer;
        [SerializeField] private RectTransform actionRowsContainer;
        [SerializeField] private Button questRowTemplate;
        [SerializeField] private Button actionRowTemplate;
        [SerializeField] private GameObject[] editorPreviewObjects;
        [SerializeField] private bool startsVisible;

        private readonly List<GameObject> spawnedQuestRows = new();
        private readonly List<GameObject> spawnedActionRows = new();
        public GameObject RuntimeToggleObject => openButton == null ? null : openButton.gameObject;

        private void Awake()
        {
            HideEditorPreviews();
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
            HideEditorPreviews();
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
            {
                ShowQuestDetail(null);
                return;
            }

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
                    label.text = quest.Title;
                var description = FindText(row.transform, "DescriptionLabel");
                if (description != null)
                    description.text = string.IsNullOrWhiteSpace(quest.Reason) ? quest.Title : quest.Reason;
                var status = FindText(row.transform, "StatusBadge/StatusLabel");
                if (status != null)
                    status.text = quest.IsCompleted
                        ? OntologyLanguagePackService.Text("ui.quest.completed", "Completed")
                        : OntologyLanguagePackService.Text("ui.quest.active", "Active");
                var progress = FindImage(row.transform, "ProgressTrack/ProgressFill");
                if (progress != null)
                    progress.fillAmount = GetQuestProgress(quest);

                spawnedQuestRows.Add(row.gameObject);
            }

            ShowQuestDetail(quests[0]);
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
            ShowQuestDetail(quest);
        }

        private void ExecuteCandidate(OntologyActionCandidate candidate)
        {
            if (bootstrap == null || candidate == null)
                return;
            ResolveDependencies();
            if (authorityClient != null && authorityClient.IsAuthenticated)
            {
                if (!authorityClient.IsWorldRuntimeReady)
                {
                    if (actionEmptyLabel != null)
                    {
                        actionEmptyLabel.text =
                            "The shared-world authority is not ready. No local action was applied.";
                    }
                    return;
                }

                StartCoroutine(ExecuteAuthorityCandidateRoutine(candidate));
                return;
            }
            bootstrap.ExecuteAction(candidate.Action);
            Refresh();
        }

        private IEnumerator ExecuteAuthorityCandidateRoutine(OntologyActionCandidate candidate)
        {
            var actor = FindAuthorityIdentity(candidate.Action.ActorId.Value);
            var target = FindAuthorityIdentity(candidate.Action.TargetId.Value);
            if (actor == null || target == null || !actor.TryGetGuid(out var actorId) || !target.TryGetGuid(out var targetId))
            {
                if (actionEmptyLabel != null) actionEmptyLabel.text = "This action target is not ready in the shared world.";
                yield break;
            }
            Guid? toolId = null;
            if (!candidate.Action.ToolId.IsEmpty)
            {
                var tool = FindAuthorityIdentity(candidate.Action.ToolId.Value);
                if (tool == null || !tool.TryGetGuid(out var resolvedTool))
                {
                    if (actionEmptyLabel != null) actionEmptyLabel.text = "The required tool is not ready in the shared world.";
                    yield break;
                }
                toolId = resolvedTool;
            }
            var payload = CreateAuthorityActionPayload(
                actorId,
                targetId,
                toolId,
                candidate);
            if (payload == null)
            {
                if (actionEmptyLabel != null)
                {
                    actionEmptyLabel.text =
                        "No unique enabled Authority definition exists for this action.";
                }
                yield break;
            }
            var command = OntologyWorldAuthorityClient.CreateCommand(
                "execute_action",
                payload);
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandRoutine(command, value => result = value);
            if (result == null || !result.accepted)
            {
                if (actionEmptyLabel != null) actionEmptyLabel.text = "Action was not accepted: " + (result?.rejectionCode ?? "unknown");
                yield break;
            }
            yield return authorityClient.LoadWorldRoutine();
            Refresh();
        }

        private string CreateAuthorityActionPayload(
            Guid actorId,
            Guid targetId,
            Guid? toolId,
            OntologyActionCandidate candidate)
        {
            if (candidate == null ||
                authorityClient == null ||
                !authorityClient.TryResolveEnabledAction(
                    candidate.Action.Verb.Value,
                    out var definition))
            {
                return null;
            }

            return OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                actorId,
                targetId,
                toolId,
                definition.packageId,
                definition.packageVersion,
                definition.actionId,
                definition.definitionVersion);
        }

        private static OntologyAuthorityEntityIdentity FindAuthorityIdentity(string ontologyEntityId)
        {
            foreach (var ontology in FindObjectsByType<OntologyObject>(FindObjectsInactive.Exclude))
            {
                if (ontology != null && string.Equals(ontology.EntityId, ontologyEntityId, StringComparison.Ordinal))
                    return ontology.GetComponent<OntologyAuthorityEntityIdentity>();
            }
            return null;
        }

        private void ApplyLocalizedStaticLabels()
        {
            if (titleLabel != null)
                titleLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.title", "Quests & Actions");
            if (subtitleLabel != null)
                subtitleLabel.text = OntologyLanguagePackService.Text(
                    "ui.quest_panel.subtitle",
                    "Choose a quest and act in your world.");
            if (questHeaderLabel != null)
                questHeaderLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.active_quests", "Active Quests");
            if (actionHeaderLabel != null)
                actionHeaderLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.available_actions", "Available Actions");
            if (questEmptyLabel != null)
                questEmptyLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.empty_quests", "No active quests");
            if (actionEmptyLabel != null)
                actionEmptyLabel.text = OntologyLanguagePackService.Text("ui.quest_panel.empty_actions", "No available actions");
            if (footerLabel != null)
                footerLabel.text = OntologyLanguagePackService.Text(
                    "ui.quest_panel.footer_hint",
                    "Select a quest or choose an available action.");
        }

        private void ShowQuestDetail(OntologyQuest quest)
        {
            if (selectedQuestTitleLabel != null)
                selectedQuestTitleLabel.text = quest == null
                    ? OntologyLanguagePackService.Text("ui.quest_panel.no_selection", "No quest selected")
                    : quest.Title;
            if (selectedQuestReasonLabel != null)
                selectedQuestReasonLabel.text = quest == null
                    ? OntologyLanguagePackService.Text("ui.quest_panel.select_prompt", "Select an active quest.")
                    : string.IsNullOrWhiteSpace(quest.Reason) ? quest.Title : quest.Reason;
            if (selectedQuestStatusLabel != null)
                selectedQuestStatusLabel.text = quest == null
                    ? "-"
                    : quest.IsCompleted
                        ? OntologyLanguagePackService.Text("ui.quest.completed", "Completed")
                        : OntologyLanguagePackService.Text("ui.quest.active", "Active");
            var progress = GetQuestProgress(quest);
            if (selectedQuestProgressFill != null)
                selectedQuestProgressFill.fillAmount = progress;
            if (selectedQuestProgressLabel != null)
                selectedQuestProgressLabel.text = Mathf.RoundToInt(progress * 100f) + "%";
        }

        private static float GetQuestProgress(OntologyQuest quest)
        {
            if (quest == null || quest.Goals == null || quest.Goals.Count == 0)
                return quest != null && quest.IsCompleted ? 1f : 0f;

            var completedGoalCount = 0;
            foreach (var goal in quest.Goals)
            {
                if (goal != null && goal.IsCompleted)
                    completedGoalCount++;
            }

            return Mathf.Clamp01((float)completedGoalCount / quest.Goals.Count);
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
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
        }

        private void HideEditorPreviews()
        {
            if (editorPreviewObjects == null) return;
            foreach (var preview in editorPreviewObjects)
                if (preview != null) preview.SetActive(false);
        }

        private static TMP_Text FindText(Transform root, string path) =>
            root == null ? null : root.Find(path)?.GetComponent<TMP_Text>();

        private static Image FindImage(Transform root, string path) =>
            root == null ? null : root.Find(path)?.GetComponent<Image>();

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }
    }
}
