using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Read-only account-profile review binder. It makes the selected
    /// character's account-owned relations visible before world entry without
    /// turning them into shared-world Facts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountProfileReviewPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text characterNameLabel;
        [SerializeField] private TMP_Text templateLabel;
        [SerializeField] private TMP_Text appearanceLabel;
        [SerializeField] private TMP_Text profileRelationsLabel;
        [SerializeField] private TMP_Text selectedWorldLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private Button enterWorldButton;
        [SerializeField] private Button backButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;
        [SerializeField] private UnityEvent onBackRequested;

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

        // Keep behavioural listeners after the third-party button component initializes.
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
            if (entryFlow == null) return;
            var character = entryFlow.CurrentCharacter;
            if (characterNameLabel != null) characterNameLabel.text = character?.displayName ?? string.Empty;
            if (templateLabel != null) templateLabel.text = character?.templateId ?? string.Empty;
            if (appearanceLabel != null)
            {
                appearanceLabel.text = character?.equippedPartIds == null || character.equippedPartIds.Length == 0
                    ? "Default appearance"
                    : string.Join(", ", character.equippedPartIds);
            }
            if (profileRelationsLabel != null)
            {
                if (character?.profileRelations == null || character.profileRelations.Length == 0)
                {
                    profileRelationsLabel.text = "No saved profile relations";
                }
                else
                {
                    var lines = new string[character.profileRelations.Length];
                    for (var index = 0; index < character.profileRelations.Length; index++)
                    {
                        var relation = character.profileRelations[index];
                        lines[index] = relation.subjectId + " → " + relation.predicateId + " → " + relation.objectId;
                    }
                    profileRelationsLabel.text = string.Join("\n", lines);
                }
            }
            if (selectedWorldLabel != null)
            {
                var world = entryFlow.SelectedWorldId;
                selectedWorldLabel.text = string.IsNullOrWhiteSpace(world) ? string.Empty : world;
            }
            if (statusLabel != null) statusLabel.text = entryFlow.LastStatus ?? string.Empty;
            if (summaryLabel != null)
            {
                var relations = character?.profileRelations;
                var relationSummary = relations == null || relations.Length == 0
                    ? "No saved account profile relations"
                    : relations.Length + " saved account profile relation(s)";
                var appearance = character?.equippedPartIds == null || character.equippedPartIds.Length == 0
                    ? "Default appearance"
                    : string.Join(", ", character.equippedPartIds);
                summaryLabel.text = "PROFILE REVIEW\n\n" +
                                    "Character: " + (character?.displayName ?? "-") + "\n" +
                                    "Template: " + (character?.templateId ?? "-") + "\n" +
                                    "Appearance: " + appearance + "\n" +
                                    "World: " + (entryFlow.SelectedWorldId ?? "-") + "\n\n" +
                                    relationSummary;
            }
            if (enterWorldButton != null)
                enterWorldButton.interactable = character != null && !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId);
        }

        private void BindButtons()
        {
            if (enterWorldButton != null)
            {
                enterWorldButton.onClick.RemoveAllListeners();
                enterWorldButton.onClick.AddListener(() =>
                {
                    entryFlow?.EnterSelectedCharacterInCurrentWorld();
                    navigator?.CloseAll();
                });
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() =>
                {
                    if (navigator != null) navigator.ShowWorldSelection();
                    else onBackRequested?.Invoke();
                });
            }
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
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
