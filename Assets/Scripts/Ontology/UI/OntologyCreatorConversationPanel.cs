using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Tormia.Ontology.Unity.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Hierarchy-authored first creator vertical slice. It captures a natural
    /// language request as an ephemeral draft and previews the catalog route.
    /// It never writes world facts or sends authority commands.
    /// </summary>
    public sealed class OntologyCreatorConversationPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private TMP_Text routeLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_InputField promptInput;
        [SerializeField] private Button startButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private OntologyCreatorWorkspaceController workspace;

        private readonly HashSet<OntologyCreatorNpcAppearance> boundAssistants = new();
        private OntologyCreatorServiceDefinition selectedService;
        private float nextBindTime;

        public OntologyCreatorWorkDraft CurrentDraft { get; private set; }
        public event Action<OntologyCreatorWorkDraft> DraftStarted;

        private void Awake()
        {
            ResolveHierarchy();
            BindButtons();
            Close();
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += RefreshText;
            BindAssistants();
        }

        private void OnDisable()
        {
            OntologyLanguagePackService.LanguageChanged -= RefreshText;
            foreach (var assistant in boundAssistants)
            {
                if (assistant != null)
                {
                    assistant.Clicked -= HandleAssistantClicked;
                }
            }
            boundAssistants.Clear();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextBindTime)
            {
                return;
            }

            nextBindTime = Time.unscaledTime + 1f;
            BindAssistants();
        }

        public void OpenForService(string serviceId)
        {
            ResolveDependencies();
            selectedService = workspace == null ? null : workspace.FindService(serviceId);
            if (selectedService == null)
            {
                SetStatus(L(
                    "ui.creator.service_unavailable",
                    "This creator service is not available."));
                return;
            }

            SetVisible(true);
            RefreshText();
            promptInput?.ActivateInputField();
        }

        public void Close()
        {
            SetVisible(false);
            selectedService = null;
        }

        public void StartDraft()
        {
            if (selectedService == null || !selectedService.acceptsInitialPrompt)
            {
                SetStatus(L(
                    "ui.creator.stage_not_ready",
                    "This specialist will become available after the world plan is drafted."));
                return;
            }

            var services = workspace?.ServiceCatalog?.Services;
            if (!OntologyCreatorWorkDraftFactory.TryCreate(
                    promptInput == null ? string.Empty : promptInput.text,
                    services,
                    out var draft,
                    out var error))
            {
                SetStatus(ResolveDraftError(error));
                return;
            }

            CurrentDraft = draft;
            SetStatus(L(
                "ui.creator.draft_started",
                "Draft started. Review the production route before any world data is changed."));
            RefreshRoute();
            DraftStarted?.Invoke(draft);
        }

        private void HandleAssistantClicked(OntologyCreatorNpcAppearance assistant)
        {
            if (assistant != null)
            {
                OpenForService(assistant.ServiceId);
            }
        }

        private void ResolveHierarchy()
        {
            panelGroup ??= GetComponent<CanvasGroup>();
            titleLabel ??= transform.Find("Card/Header/Title")?.GetComponent<TMP_Text>();
            summaryLabel ??= transform.Find("Card/Summary")?.GetComponent<TMP_Text>();
            routeLabel ??= transform.Find("Card/Route")?.GetComponent<TMP_Text>();
            statusLabel ??= transform.Find("Card/Status")?.GetComponent<TMP_Text>();
            promptInput ??= transform.Find("Card/PromptInput")?.GetComponent<TMP_InputField>();
            startButton ??= transform.Find("Card/StartButton")?.GetComponent<Button>();
            closeButton ??= transform.Find("Card/Header/CloseButton")?.GetComponent<Button>();
        }

        private void ResolveDependencies()
        {
            if (workspace == null)
            {
                workspace = FindAnyObjectByType<OntologyCreatorWorkspaceController>();
            }
        }

        private void BindButtons()
        {
            startButton?.onClick.RemoveListener(StartDraft);
            startButton?.onClick.AddListener(StartDraft);
            closeButton?.onClick.RemoveListener(Close);
            closeButton?.onClick.AddListener(Close);
        }

        private void BindAssistants()
        {
            ResolveDependencies();
            var root = workspace == null ? null : workspace.PresentationRoot;
            if (root == null)
            {
                return;
            }

            foreach (var assistant in
                     root.GetComponentsInChildren<OntologyCreatorNpcAppearance>(true))
            {
                if (assistant == null || !boundAssistants.Add(assistant))
                {
                    continue;
                }

                assistant.Clicked += HandleAssistantClicked;
            }
        }

        private void RefreshText()
        {
            if (selectedService == null)
            {
                return;
            }

            if (titleLabel != null)
            {
                titleLabel.text = L(
                    selectedService.displayNameKey,
                    selectedService.fallbackDisplayName);
            }
            if (summaryLabel != null)
            {
                summaryLabel.text = L(
                    selectedService.summaryKey,
                    selectedService.fallbackSummary);
            }
            if (promptInput?.placeholder is TMP_Text placeholder)
            {
                placeholder.text = L(
                    "ui.creator.prompt_placeholder",
                    "Describe the game you want to make...");
            }
            if (startButton != null)
            {
                startButton.interactable = selectedService.acceptsInitialPrompt;
                var label = startButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = L("ui.creator.start_draft", "START DRAFT");
                }
            }
            RefreshRoute();
        }

        private void RefreshRoute()
        {
            if (routeLabel == null)
            {
                return;
            }

            var route = CurrentDraft?.ServiceRoute ??
                        workspace?.ServiceCatalog?.Services?
                            .Where(value => value != null && value.IsValid)
                            .OrderBy(value => value.stageOrder)
                            .Select(value => value.serviceId)
                            .ToArray() ??
                        Array.Empty<string>();
            routeLabel.text = string.Join("  →  ", route);
        }

        private void SetStatus(string value)
        {
            if (statusLabel != null)
            {
                statusLabel.text = value ?? string.Empty;
            }
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null)
            {
                return;
            }

            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private static string ResolveDraftError(
            OntologyCreatorDraftError error) =>
            error switch
            {
                OntologyCreatorDraftError.PromptTooShort => L(
                    "ui.creator.prompt_too_short",
                    "Describe the game you want to make in at least 10 characters."),
                OntologyCreatorDraftError.DuplicateServiceId => L(
                    "ui.creator.duplicate_service",
                    "The creator service catalog contains a duplicate service ID."),
                OntologyCreatorDraftError.MissingPromptEntryService => L(
                    "ui.creator.missing_prompt_service",
                    "The creator service catalog has no prompt-entry service."),
                _ => L(
                    "ui.creator.service_unavailable",
                    "This creator service is not available.")
            };
    }
}
