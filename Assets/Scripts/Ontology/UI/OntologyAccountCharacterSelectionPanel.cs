using System;
using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text helperLabel;
        [SerializeField] private TMP_Text accountLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text detailNameLabel;
        [SerializeField] private TMP_Text detailTemplateLabel;
        [SerializeField] private TMP_Text currentAppearanceLabel;
        [SerializeField] private TMP_Text entryHintLabel;
        [SerializeField] private OntologyAppearanceReviewPartSlot[] appearanceSlots;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button createNewButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;
        [SerializeField] private UnityEvent onCharacterConfirmed;
        [SerializeField] private UnityEvent onBackRequested;

        private bool refreshing;
        private OntologyWorldAuthorityAccountEntryFlow subscribedEntryFlow;

        public bool IsVisible => panelGroup != null && panelGroup.alpha > 0.5f && panelGroup.interactable;
        public OntologyWorldAuthorityAccountEntryFlow EntryFlow => entryFlow;
        public IReadOnlyList<OntologyAccountCharacterCard> CharacterCards =>
            characterCards ?? Array.Empty<OntologyAccountCharacterCard>();

        private void Awake()
        {
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            BindButtons();
            SetVisible(startsVisible);
            Refresh();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        // Farm UI buttons finish their own setup during Awake/OnEnable. Bind at
        // Start as well so this panel owns only the behaviour callback, not UI layout.
        private void Start() => BindButtons();

        private void OnDisable()
        {
            if (subscribedEntryFlow != null)
            {
                subscribedEntryFlow.StateChanged -= Refresh;
                subscribedEntryFlow = null;
            }
            OntologyLanguagePackService.LanguageChanged -= Refresh;
        }

        public void Open()
        {
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            SetVisible(true);
            Refresh();
        }

        public void Close() => SetVisible(false);

        public void Refresh()
        {
            if (refreshing) return;
            ResolveDependencies();
            UpdateEntryFlowSubscription();
            refreshing = true;
            LocalizeStaticLabels();

            var characters = entryFlow?.Characters ?? Array.Empty<OntologyAuthorityPlayerCharacter>();
            for (var index = 0; index < characterCards.Length; index++)
            {
                var card = characterCards[index];
                if (card == null) continue;

                var character = index < characters.Count ? characters[index] : null;
                card.Bind(
                    character,
                    character != null && string.Equals(character.characterId, entryFlow?.SelectedCharacterId, StringComparison.Ordinal),
                    SelectCharacter);
            }

            if (accountLabel != null)
                accountLabel.text = L("ui.account.character_select.account", "{0}")
                    .Replace("{0}", entryFlow?.CurrentAccount?.account?.displayName ?? "-");
            if (statusLabel != null)
                statusLabel.text = entryFlow?.LastStatus ?? string.Empty;
            var selectedCharacter = entryFlow?.CurrentCharacter;
            if (detailNameLabel != null)
                detailNameLabel.text = selectedCharacter?.displayName ?? "-";
            if (detailTemplateLabel != null)
                detailTemplateLabel.text = selectedCharacter == null
                    ? "-"
                    : OntologyLanguagePackService.CharacterTemplateName(
                        selectedCharacter.templateId);
            BindAppearanceSlots(selectedCharacter?.equippedPartIds);
            if (continueButton != null)
                continueButton.interactable = selectedCharacter != null;

            refreshing = false;
        }

        private void LocalizeStaticLabels()
        {
            if (titleLabel != null) titleLabel.text = L("ui.account.character_select.title", "SELECT A CHARACTER");
            if (helperLabel != null) helperLabel.text = L("ui.account.character_select.prompt", "Choose a character for your adventure.");
            if (currentAppearanceLabel != null)
                currentAppearanceLabel.text = L("ui.account.character_select.current_appearance", "CURRENT APPEARANCE");
            if (entryHintLabel != null)
                entryHintLabel.text = L("ui.account.character_select.entry_hint", "Enter the world with the selected appearance and profile.");
            SetButtonLabel(continueButton, L("ui.account.character_select.continue_selected", "CONTINUE WITH THIS CHARACTER"));
            SetButtonLabel(backButton, L("ui.account.character_select.logout", "LOG OUT"));
            SetButtonLabel(createNewButton, L("ui.account.character_select.create_new", "CREATE NEW CHARACTER"));
        }

        private void BindAppearanceSlots(IReadOnlyList<string> equippedPartIds)
        {
            if (appearanceSlots == null || appearanceSlots.Length == 0) return;
            var activeIds = new HashSet<string>(equippedPartIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var definitions = partDatabase?.Definitions?
                .Where(definition => definition != null && definition.visibleInCustomization
                    && (activeIds.Count == 0 ? definition.enabledByDefault : activeIds.Contains(definition.partId)))
                .Take(appearanceSlots.Length)
                .ToArray() ?? Array.Empty<OntologyCharacterPartDefinition>();
            for (var index = 0; index < appearanceSlots.Length; index++)
                appearanceSlots[index]?.Bind(index < definitions.Length ? definitions[index] : null);
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null) return;
            var preferredPath = button.name == "CreateNewCharacterButton"
                ? "CreateNewLabel"
                : "Text (TMP)";
            var label = button.transform.Find(preferredPath)?.GetComponent<TMP_Text>()
                ?? button.GetComponentsInChildren<TMP_Text>(true)
                    .FirstOrDefault(candidate => candidate.name != "PlusLabel");
            if (label != null) label.text = value;
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
                    if (navigator != null) navigator.ShowWorldSelection();
                    else onCharacterConfirmed?.Invoke();
                });
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() =>
                {
                    entryFlow?.LogoutAccount();
                    if (navigator != null) navigator.ShowLogin();
                    else onBackRequested?.Invoke();
                });
            }

            if (createNewButton != null)
            {
                createNewButton.onClick.RemoveAllListeners();
                createNewButton.onClick.AddListener(() => navigator?.ShowCharacterCreation());
            }
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null)
                entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(FindObjectsInactive.Include);
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (navigator == null)
                navigator = FindAnyObjectByType<OntologyAccountFlowNavigator>(FindObjectsInactive.Include);
            if (partDatabase == null)
                partDatabase = OntologyCharacterPartAdapter.FindAvailable()?.PartDatabase;
            ResolveCharacterCards();
            ResolveAppearanceSlots();
        }

        private void UpdateEntryFlowSubscription()
        {
            if (!isActiveAndEnabled || ReferenceEquals(subscribedEntryFlow, entryFlow)) return;
            if (subscribedEntryFlow != null) subscribedEntryFlow.StateChanged -= Refresh;
            subscribedEntryFlow = entryFlow;
            if (subscribedEntryFlow != null) subscribedEntryFlow.StateChanged += Refresh;
        }

        private void ResolveCharacterCards()
        {
            var listRoot = transform.Find("CharacterListPanel");
            if (listRoot == null)
            {
                characterCards ??= Array.Empty<OntologyAccountCharacterCard>();
                return;
            }
            var resolved = new List<OntologyAccountCharacterCard>();
            for (var index = 0; index < listRoot.childCount; index++)
            {
                var child = listRoot.GetChild(index);
                if (!child.name.StartsWith("CharacterCardSlot", StringComparison.Ordinal)) continue;
                var card = child.GetComponent<OntologyAccountCharacterCard>();
                if (card == null && Application.isPlaying)
                    card = child.gameObject.AddComponent<OntologyAccountCharacterCard>();
                if (card != null) resolved.Add(card);
            }

            characterCards = resolved
                .OrderBy(card => card.name, StringComparer.Ordinal)
                .ToArray();
        }

        private void ResolveAppearanceSlots()
        {
            var appearancePanel = transform.Find("SelectedCharacterDetailPanel/CurrentAppearancePanel");
            if (appearancePanel == null)
            {
                appearanceSlots ??= Array.Empty<OntologyAppearanceReviewPartSlot>();
                return;
            }

            // Artists author the slots under a horizontal ScrollRect content
            // container. Keep the legacy direct-child fallback so older scenes
            // still bind correctly until they are re-authored.
            var slotsRoot = appearancePanel.Find("AppearanceViewport/AppearanceContent")
                ?? appearancePanel;
            var resolved = new List<OntologyAppearanceReviewPartSlot>();
            for (var index = 0; index < slotsRoot.childCount; index++)
            {
                var child = slotsRoot.GetChild(index);
                if (!child.name.StartsWith("AppearanceSlot", StringComparison.Ordinal)) continue;
                var slot = child.GetComponent<OntologyAppearanceReviewPartSlot>();
                if (slot == null && Application.isPlaying)
                    slot = child.gameObject.AddComponent<OntologyAppearanceReviewPartSlot>();
                if (slot != null) resolved.Add(slot);
            }

            appearanceSlots = resolved
                .OrderBy(slot => slot.name, StringComparer.Ordinal)
                .ToArray();
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
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
        [SerializeField] private TMP_Text profileSummaryLabel;

        private string characterId;

        public string CharacterId => characterId;

        private void Awake() => ResolveBindings();

        public void Bind(OntologyAuthorityPlayerCharacter character, bool selected, Action<string> select)
        {
            ResolveBindings();
            var available = character != null;
            characterId = available ? character.characterId : null;
            gameObject.SetActive(available);
            if (!available) return;

            if (nameLabel != null) nameLabel.text = character.displayName ?? character.characterId;
            if (templateLabel != null)
                templateLabel.text =
                    OntologyLanguagePackService.CharacterTemplateName(
                        character.templateId);
            if (profileSummaryLabel != null)
            {
                var relationCount = character.profileRelations == null ? 0 : character.profileRelations.Length;
                profileSummaryLabel.text = relationCount == 0
                    ? L("ui.account.no_profile_relations", "No saved profile relations")
                    : L("ui.account.profile_relation_count", "{0} saved profile relation(s)")
                        .Replace("{0}", relationCount.ToString());
            }
            if (selectedIndicator != null)
            {
                selectedIndicator.SetActive(selected);
                var selectedLabels = selectedIndicator.GetComponentsInChildren<TMP_Text>(true);
                foreach (var selectedLabel in selectedLabels)
                    selectedLabel.gameObject.SetActive(false);
            }
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => select?.Invoke(characterId));
            }
        }

        private void ResolveBindings()
        {
            if (selectButton == null) selectButton = GetComponent<Button>();
            if (selectedIndicator == null)
                selectedIndicator = transform.Find("SelectedIndicator")?.gameObject;
            if (nameLabel == null)
                nameLabel = transform.Find("CharacterName")?.GetComponent<TMP_Text>();
            if (templateLabel == null)
                templateLabel = transform.Find("TemplateChip/TemplateLabel")?.GetComponent<TMP_Text>();
            if (profileSummaryLabel == null)
                profileSummaryLabel = transform.Find("ProfileSummary")?.GetComponent<TMP_Text>();
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }
}
