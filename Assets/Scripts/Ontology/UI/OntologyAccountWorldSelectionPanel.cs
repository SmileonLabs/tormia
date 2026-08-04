using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents saved worlds through fixed, hierarchy-authored card slots.
    /// The account flow supplies the data; this UI does not author or alter
    /// world ontology data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountWorldSelectionPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private OntologyAccountWorldCard[] worldCards;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text helperLabel;
        [SerializeField] private GameObject emptyWorldHero;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;
        [SerializeField] private UnityEvent onWorldConfirmed;
        [SerializeField] private UnityEvent onBackRequested;
        private OntologyWorldAuthorityAccountEntryFlow subscribedEntryFlow;
        private bool refreshing;

        public OntologyWorldAuthorityAccountEntryFlow EntryFlow => entryFlow;

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            ResolveDependencies();
            BindEntryFlowEvents();
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        // See the character selection panel: bind after third-party button setup.
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
            BindEntryFlowEvents();
            SetVisible(true);
            Refresh();
        }

        public void Close() => SetVisible(false);

        public void Refresh()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                ResolveDependencies();
                BindEntryFlowEvents();
                LocalizeStaticLabels();
                var worlds = entryFlow?.Worlds ?? Array.Empty<OntologyAuthorityAccountWorld>();
                if (emptyWorldHero != null)
                    emptyWorldHero.SetActive(worlds.Count == 0);
                for (var index = 0; index < worldCards.Length; index++)
                {
                    var card = worldCards[index];
                    if (card == null) continue;
                    var world = index < worlds.Count ? worlds[index] : null;
                    card.Bind(world,
                        world != null && string.Equals(
                            world.worldId,
                            entryFlow?.SelectedWorldId,
                            StringComparison.Ordinal),
                        SelectWorld);
                }
                if (statusLabel != null) statusLabel.text = entryFlow?.LastStatus ?? string.Empty;
                if (continueButton != null)
                    continueButton.interactable =
                        entryFlow?.CurrentCharacter != null &&
                        !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId);
            }
            finally
            {
                refreshing = false;
            }
        }

        private void LocalizeStaticLabels()
        {
            if (titleLabel != null) titleLabel.text = L("ui.account.world_select.title", "SELECT A WORLD");
            if (helperLabel != null) helperLabel.text = L("ui.account.world_select.prompt", "Choose a world to enter.");
            SetButtonLabel(continueButton, L("ui.account.world_select.continue", "CONTINUE"));
            SetButtonLabel(backButton, L("ui.account.back", "BACK"));
        }

        private static void SetButtonLabel(Button button, string value)
        {
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label != null) label.text = value;
        }

        private void SelectWorld(string worldId)
        {
            if (entryFlow != null && entryFlow.SelectWorld(worldId)) Refresh();
        }

        private void BindButtons()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(() =>
                {
                    if (entryFlow?.CurrentCharacter != null && !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId))
                    {
                        if (navigator != null) navigator.ShowProfileReview();
                        else onWorldConfirmed?.Invoke();
                    }
                });
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() =>
                {
                    if (navigator != null) navigator.ShowCharacterSelection();
                    else onBackRequested?.Invoke();
                });
            }
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null)
                entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                    FindObjectsInactive.Include);
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (emptyWorldHero == null)
                emptyWorldHero = transform.Find("EmptyWorldHero")?.gameObject;
            if (worldCards == null || worldCards.Length == 0 || AnyCardMissing())
                worldCards = GetComponentsInChildren<OntologyAccountWorldCard>(true);
        }

        private void BindEntryFlowEvents()
        {
            if (subscribedEntryFlow == entryFlow) return;
            if (subscribedEntryFlow != null)
                subscribedEntryFlow.StateChanged -= Refresh;
            subscribedEntryFlow = entryFlow;
            if (subscribedEntryFlow != null)
                subscribedEntryFlow.StateChanged += Refresh;
        }

        private bool AnyCardMissing()
        {
            foreach (var card in worldCards)
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

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }

}
