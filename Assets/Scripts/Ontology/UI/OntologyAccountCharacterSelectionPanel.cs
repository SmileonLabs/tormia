using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Binds account characters to a fixed, authored set of hierarchy card slots.
    /// It never instantiates UI or decides layout: artists can edit every card in
    /// the scene/prefab, while this component only fills text and selection state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountCharacterSelectionPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private OntologyAccountCharacterCard[] characterCards;
        [SerializeField] private TMP_Text accountLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;
        [SerializeField] private UnityEvent onCharacterConfirmed;
        [SerializeField] private UnityEvent onBackRequested;

        private bool refreshing;

        public bool IsVisible => panelGroup != null && panelGroup.alpha > 0.5f && panelGroup.interactable;

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
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        // Farm UI buttons finish their own setup during Awake/OnEnable. Bind at
        // Start as well so this panel owns only the behaviour callback, not UI layout.
        private void Start() => BindButtons();

        private void OnDisable()
        {
            if (entryFlow != null) entryFlow.StateChanged -= Refresh;
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
            if (entryFlow == null || refreshing) return;
            refreshing = true;

            var characters = entryFlow.Characters;
            for (var index = 0; index < characterCards.Length; index++)
            {
                var card = characterCards[index];
                if (card == null) continue;

                var character = index < characters.Count ? characters[index] : null;
                card.Bind(
                    character,
                    character != null && string.Equals(character.characterId, entryFlow.SelectedCharacterId, StringComparison.Ordinal),
                    SelectCharacter);
            }

            if (accountLabel != null)
                accountLabel.text = entryFlow.CurrentAccount?.account?.displayName ?? string.Empty;
            if (statusLabel != null)
                statusLabel.text = entryFlow.LastStatus ?? string.Empty;
            if (continueButton != null)
                continueButton.interactable = entryFlow.CurrentCharacter != null;

            refreshing = false;
        }

        private void SelectCharacter(string characterId)
        {
            if (entryFlow != null && entryFlow.SelectCharacter(characterId)) Refresh();
        }

        private void BindButtons()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(() =>
                {
                    if (entryFlow?.CurrentCharacter == null) return;
                    if (navigator != null) navigator.ShowAppearanceReview();
                    else onCharacterConfirmed?.Invoke();
                });
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() =>
                {
                    if (navigator != null) navigator.ShowAccountEntry();
                    else onBackRequested?.Invoke();
                });
            }
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (characterCards == null || characterCards.Length == 0 || AnyCardMissing())
                characterCards = GetComponentsInChildren<OntologyAccountCharacterCard>(true);
        }

        private bool AnyCardMissing()
        {
            foreach (var card in characterCards)
                if (card == null) return true;
            return false;
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }
    }

    /// <summary>
    /// One authored character-card slot. The card can be styled freely in the
    /// hierarchy; fields only identify the content surfaces to populate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountCharacterCard : MonoBehaviour
    {
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject selectedIndicator;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text templateLabel;
        [SerializeField] private TMP_Text appearanceLabel;
        [SerializeField] private TMP_Text profileSummaryLabel;

        private string characterId;

        private void Awake() => ResolveBindings();

        public void Bind(OntologyAuthorityPlayerCharacter character, bool selected, Action<string> select)
        {
            ResolveBindings();
            var available = character != null;
            characterId = available ? character.characterId : null;
            gameObject.SetActive(available);
            if (!available) return;

            if (nameLabel != null) nameLabel.text = character.displayName ?? character.characterId;
            if (templateLabel != null) templateLabel.text = character.templateId ?? string.Empty;
            if (appearanceLabel != null)
            {
                appearanceLabel.text = character.equippedPartIds == null || character.equippedPartIds.Length == 0
                    ? L("ui.account.default_appearance", "Default appearance")
                    : string.Join(", ", character.equippedPartIds);
            }
            if (profileSummaryLabel != null)
            {
                var relationCount = character.profileRelations == null ? 0 : character.profileRelations.Length;
                profileSummaryLabel.text = relationCount == 0
                    ? L("ui.account.no_profile_relations", "No saved profile relations")
                    : L("ui.account.profile_relation_count", "{0} saved profile relation(s)")
                        .Replace("{0}", relationCount.ToString());
            }
            if (selectedIndicator != null) selectedIndicator.SetActive(selected);
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => select?.Invoke(characterId));
            }
        }

        private void ResolveBindings()
        {
            if (selectButton == null) selectButton = GetComponent<Button>();
            var labels = GetComponentsInChildren<TMP_Text>(true);
            if (nameLabel == null && labels.Length > 0) nameLabel = labels[0];
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }
}
