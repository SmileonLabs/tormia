using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Hierarchy-owned account/character/world entry binder. It never creates
    /// visual controls; assign the authored fields in the prefab or scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountEntryPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Dropdown characterDropdown;
        [SerializeField] private TMP_Dropdown worldDropdown;
        [SerializeField] private TMP_Text accountLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text permissionLabel;
        [SerializeField] private TMP_Text appearanceLabel;
        [SerializeField] private TMP_Text playerOntologyLabel;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button enterButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private OntologyAccountCharacterSelectionPanel characterSelectionPanel;
        [SerializeField] private bool handOffToCharacterSelectionWhenReady;
        [SerializeField] private bool startsVisible;
        private bool refreshing;
        private bool handedOff;

        public bool IsVisible => panelGroup != null && panelGroup.alpha > 0.5f && panelGroup.interactable;

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            SetVisible(startsVisible);
            if (startsVisible && entryFlow != null)
                entryFlow.ConnectDevelopmentAccount();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (entryFlow != null) entryFlow.StateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (entryFlow != null) entryFlow.StateChanged -= Refresh;
        }

        public void Open()
        {
            handedOff = false;
            SetVisible(true);
            Refresh();
        }

        public void Close() => SetVisible(false);

        public void Refresh()
        {
            if (entryFlow == null || refreshing) return;
            refreshing = true;
            BindDropdown(characterDropdown, BuildCharacterOptions(), entryFlow.SelectedCharacterId,
                id => entryFlow.SelectCharacter(id));
            BindDropdown(worldDropdown, BuildWorldOptions(), entryFlow.SelectedWorldId,
                id => entryFlow.SelectWorld(id));
            if (accountLabel != null)
                accountLabel.text = entryFlow.CurrentAccount?.account?.displayName ?? "";
            if (statusLabel != null)
            {
                var status = entryFlow.LastStatus ?? "";
                var character = entryFlow.CurrentCharacter;
                var parts = character?.equippedPartIds;
                var appearance = parts == null || parts.Length == 0
                    ? "Appearance: default profile"
                    : "Appearance: " + string.Join(", ", parts);
                statusLabel.text = string.IsNullOrWhiteSpace(status)
                    ? appearance + "\n" + BuildPlayerOntologySummary()
                    : status + "\n" + appearance + "\n" + BuildPlayerOntologySummary();
            }
            if (permissionLabel != null)
                permissionLabel.text = string.IsNullOrWhiteSpace(entryFlow.SelectedWorldRole)
                    ? ""
                    : entryFlow.SelectedWorldRole;
            if (appearanceLabel != null)
            {
                var character = entryFlow.CurrentAccount == null
                    ? null
                    : entryFlow.CurrentAccount.characters == null
                        ? null
                        : FindCharacter(entryFlow.CurrentAccount.characters, entryFlow.SelectedCharacterId);
                var parts = character?.equippedPartIds;
                appearanceLabel.text = parts == null || parts.Length == 0
                    ? "Appearance: default profile"
                    : "Appearance: " + string.Join(", ", parts);
            }
            if (playerOntologyLabel != null)
                playerOntologyLabel.text = BuildPlayerOntologySummary();
            if (enterButton != null)
                enterButton.interactable = entryFlow.CanEditSelectedWorld ||
                                           !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId);
            refreshing = false;

            if (handOffToCharacterSelectionWhenReady && !handedOff &&
                characterSelectionPanel != null && entryFlow.Characters.Count > 0)
            {
                handedOff = true;
                characterSelectionPanel.Open();
                Close();
            }
        }

        private List<TMP_Dropdown.OptionData> BuildCharacterOptions()
        {
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var character in entryFlow.Characters)
            {
                if (character == null) continue;
                options.Add(new TMP_Dropdown.OptionData(
                    string.IsNullOrWhiteSpace(character.displayName)
                        ? character.characterId
                        : character.displayName));
            }
            return options;
        }

        private List<TMP_Dropdown.OptionData> BuildWorldOptions()
        {
            var options = new List<TMP_Dropdown.OptionData>();
            foreach (var world in entryFlow.Worlds)
            {
                if (world == null) continue;
                options.Add(new TMP_Dropdown.OptionData(
                    string.IsNullOrWhiteSpace(world.title) ? world.worldId : world.title));
            }
            return options;
        }

        private void BindDropdown(
            TMP_Dropdown dropdown,
            List<TMP_Dropdown.OptionData> options,
            string selectedId,
            Func<string, bool> select)
        {
            if (dropdown == null) return;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.onValueChanged.AddListener(index =>
            {
                if (refreshing) return;
                if (dropdown == characterDropdown)
                {
                    if (index >= 0 && index < entryFlow.Characters.Count && entryFlow.Characters[index] != null)
                        select(entryFlow.Characters[index].characterId);
                }
                else if (index >= 0 && index < entryFlow.Worlds.Count && entryFlow.Worlds[index] != null)
                {
                    select(entryFlow.Worlds[index].worldId);
                }
            });

            var selectedIndex = 0;
            for (var i = 0; i < options.Count; i++)
            {
                string id;
                if (dropdown == characterDropdown)
                    id = i < entryFlow.Characters.Count ? entryFlow.Characters[i]?.characterId : null;
                else
                    id = i < entryFlow.Worlds.Count ? entryFlow.Worlds[i]?.worldId : null;
                if (string.Equals(id, selectedId, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }
            dropdown.SetValueWithoutNotify(options.Count == 0 ? 0 : selectedIndex);
            dropdown.RefreshShownValue();
        }

        private void BindButtons()
        {
            if (connectButton != null)
            {
                connectButton.onClick.RemoveAllListeners();
                connectButton.onClick.AddListener(() => entryFlow?.ConnectDevelopmentAccount());
            }
            if (enterButton != null)
            {
                enterButton.onClick.RemoveAllListeners();
                enterButton.onClick.AddListener(() => entryFlow?.EnterSelectedCharacterInCurrentWorld());
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
        }

        private static OntologyAuthorityPlayerCharacter FindCharacter(
            OntologyAuthorityPlayerCharacter[] characters,
            string id)
        {
            if (characters == null || string.IsNullOrWhiteSpace(id)) return null;
            foreach (var character in characters)
            {
                if (character != null && string.Equals(character.characterId, id, StringComparison.Ordinal))
                    return character;
            }
            return null;
        }

        private string BuildPlayerOntologySummary()
        {
            var playerController = FindAnyObjectByType<OntologyPlayerController>();
            var ontologyObject = playerController == null
                ? null
                : playerController.GetComponent<OntologyObject>();
            var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            var factCount = bootstrap?.World?.Facts == null
                ? 0
                : 0;
            if (bootstrap?.World != null && ontologyObject != null)
            {
                foreach (var fact in bootstrap.World.Facts)
                {
                    if (string.Equals(fact.Subject.Value, ontologyObject.EntityId, StringComparison.Ordinal)) factCount++;
                }
            }
            return ontologyObject == null
                ? "Player ontology: not available"
                : "Player ontology: " + ontologyObject.EntityId + " (" + factCount + " facts)";
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
