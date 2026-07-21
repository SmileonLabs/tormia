using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Hierarchy-authored appearance confirmation step. This only presents the
    /// selected account character's saved appearance; it does not create world
    /// facts or author visual-part data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountAppearanceReviewPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (entryFlow != null) entryFlow.StateChanged += Refresh;
            Refresh();
        }

        private void Start() => BindButtons();

        private void OnDisable()
        {
            if (entryFlow != null) entryFlow.StateChanged -= Refresh;
        }

        public void Open()
        {
            SetVisible(true);
            Refresh();
        }

        public void Close() => SetVisible(false);

        public void Refresh()
        {
            var character = entryFlow?.CurrentCharacter;
            if (summaryLabel != null)
            {
                var parts = character?.equippedPartIds;
                var appearance = parts == null || parts.Length == 0
                    ? "Default appearance"
                    : string.Join(", ", parts);
                summaryLabel.text = "APPEARANCE REVIEW\n\n" +
                                    "Character: " + (character?.displayName ?? "-") + "\n" +
                                    "Template: " + (character?.templateId ?? "-") + "\n" +
                                    "Equipped parts: " + appearance;
            }

            if (continueButton != null)
                continueButton.interactable = character != null;
        }

        private void BindButtons()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(() =>
                {
                    if (entryFlow?.CurrentCharacter != null) navigator?.ShowWorldSelection();
                });
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() => navigator?.ShowCharacterSelection());
            }
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (navigator == null) navigator = FindAnyObjectByType<OntologyAccountFlowNavigator>();

            var labels = GetComponentsInChildren<TMP_Text>(true);
            if (summaryLabel == null && labels.Length > 0) summaryLabel = labels[0];

            var buttons = GetComponentsInChildren<Button>(true);
            Button firstChildButton = null;
            Button secondChildButton = null;
            foreach (var button in buttons)
            {
                if (button == null || button.gameObject == gameObject) continue;
                if (firstChildButton == null) firstChildButton = button;
                else if (secondChildButton == null)
                {
                    secondChildButton = button;
                    break;
                }
            }

            if (continueButton == null) continueButton = firstChildButton;
            if (backButton == null) backButton = secondChildButton;
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
