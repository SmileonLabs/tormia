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
        [SerializeField] private TMP_Text selectedCharacterLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private bool startsVisible;
        [SerializeField] private UnityEvent onWorldConfirmed;
        [SerializeField] private UnityEvent onBackRequested;
        private bool refreshing;

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

        // See the character selection panel: bind after third-party button setup.
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
            var worlds = entryFlow.Worlds;
            for (var index = 0; index < worldCards.Length; index++)
            {
                var card = worldCards[index];
                if (card == null) continue;
                var world = index < worlds.Count ? worlds[index] : null;
                card.Bind(world,
                    world != null && string.Equals(world.worldId, entryFlow.SelectedWorldId, StringComparison.Ordinal),
                    SelectWorld);
            }
            if (selectedCharacterLabel != null)
                selectedCharacterLabel.text = entryFlow.CurrentCharacter?.displayName ?? string.Empty;
            if (statusLabel != null) statusLabel.text = entryFlow.LastStatus ?? string.Empty;
            if (continueButton != null)
                continueButton.interactable = entryFlow.CurrentCharacter != null && !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId);
            refreshing = false;
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
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (worldCards == null || worldCards.Length == 0 || AnyCardMissing())
                worldCards = GetComponentsInChildren<OntologyAccountWorldCard>(true);
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
    }

    [DisallowMultipleComponent]
    public sealed class OntologyAccountWorldCard : MonoBehaviour
    {
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject selectedIndicator;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text roleLabel;
        [SerializeField] private TMP_Text revisionLabel;
        private string worldId;

        private void Awake() => ResolveBindings();

        public void Bind(OntologyAuthorityAccountWorld world, bool selected, Action<string> select)
        {
            ResolveBindings();
            var available = world != null;
            worldId = available ? world.worldId : null;
            gameObject.SetActive(available);
            if (!available) return;

            if (titleLabel != null) titleLabel.text = world.title ?? world.worldId;
            if (roleLabel != null) roleLabel.text = L("ui.account.role_" + (world.role ?? string.Empty).ToLowerInvariant(), world.role ?? string.Empty);
            if (revisionLabel != null) revisionLabel.text = L("ui.account.revision", "Revision {0}").Replace("{0}", world.revision.ToString());
            if (selectedIndicator != null) selectedIndicator.SetActive(selected);
            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => select?.Invoke(worldId));
            }
        }

        private void ResolveBindings()
        {
            if (selectButton == null) selectButton = GetComponent<Button>();
            var labels = GetComponentsInChildren<TMP_Text>(true);
            if (titleLabel == null && labels.Length > 0) titleLabel = labels[0];
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }
}
