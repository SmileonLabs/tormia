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
    /// Read-only account-profile review binder. It makes the selected
    /// character's account-owned relations visible before world entry without
    /// turning them into shared-world Facts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountProfileReviewPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text helperLabel;
        [SerializeField] private TMP_Text characterHeadingLabel;
        [SerializeField] private TMP_Text characterNameLabel;
        [SerializeField] private TMP_Text templateHeadingLabel;
        [SerializeField] private TMP_Text templateLabel;
        [SerializeField] private TMP_Text appearanceHeadingLabel;
        [SerializeField] private TMP_Text appearanceLabel;
        [SerializeField] private OntologyAppearanceReviewPartSlot[] appearanceSlots;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private TMP_Text profileRelationsHeadingLabel;
        [SerializeField] private TMP_Text profileRelationsLabel;
        [SerializeField] private TMP_Text selectedWorldHeadingLabel;
        [SerializeField] private TMP_Text selectedWorldLabel;
        [SerializeField] private TMP_Text permissionHeadingLabel;
        [SerializeField] private TMP_Text permissionLabel;
        [SerializeField] private TMP_Text statusHeadingLabel;
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
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        // Keep behavioural listeners after the third-party button component initializes.
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
            if (entryFlow == null) return;
            var character = entryFlow.CurrentCharacter;
            if (titleLabel != null) titleLabel.text = L("ui.account.profile_final_review.title", "PROFILE FINAL REVIEW");
            if (helperLabel != null) helperLabel.text = L("ui.account.profile_final_review.prompt", "Everything is ready for your adventure.");
            if (characterHeadingLabel != null) characterHeadingLabel.text = L("ui.account.profile_final_review.character", "CHARACTER");
            if (templateHeadingLabel != null) templateHeadingLabel.text = L("ui.account.profile_final_review.template", "TEMPLATE");
            if (appearanceHeadingLabel != null) appearanceHeadingLabel.text = L("ui.account.profile_final_review.appearance", "APPEARANCE");
            if (selectedWorldHeadingLabel != null) selectedWorldHeadingLabel.text = L("ui.account.profile_final_review.world", "SELECTED WORLD");
            if (permissionHeadingLabel != null) permissionHeadingLabel.text = L("ui.account.profile_final_review.permission", "WORLD PERMISSION");
            if (profileRelationsHeadingLabel != null) profileRelationsHeadingLabel.text = L("ui.account.profile_final_review.relations", "PROFILE RELATIONS");
            if (statusHeadingLabel != null) statusHeadingLabel.text = L("ui.account.profile_final_review.status", "STATUS");
            if (characterNameLabel != null) characterNameLabel.text = character?.displayName ?? string.Empty;
            if (templateLabel != null) templateLabel.text = character?.templateId ?? string.Empty;
            if (appearanceLabel != null)
            {
                appearanceLabel.text = character?.equippedPartIds == null || character.equippedPartIds.Length == 0
                    ? L("ui.account.default_appearance", "Default appearance")
                    : string.Join(", ", character.equippedPartIds);
            }
            BindAppearanceSlots(character?.equippedPartIds);
            if (profileRelationsLabel != null)
            {
                if (character?.profileRelations == null || character.profileRelations.Length == 0)
                {
                    profileRelationsLabel.text = L("ui.account.no_profile_relations", "No saved profile relations");
                }
                else
                {
                    var lines = new string[character.profileRelations.Length];
                    for (var index = 0; index < character.profileRelations.Length; index++)
                    {
                        var relation = character.profileRelations[index];
                        lines[index] = relation.subjectId + "  >  " + relation.predicateId + "  >  " + relation.objectId;
                    }
                    profileRelationsLabel.text = string.Join("\n", lines);
                }
            }
            if (selectedWorldLabel != null)
                selectedWorldLabel.text = ResolveSelectedWorldTitle();
            if (permissionLabel != null)
                permissionLabel.text = BuildPermissionLabel();
            var canEnter = character != null
                && !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldId)
                && !string.IsNullOrWhiteSpace(entryFlow.SelectedWorldRole);
            if (statusLabel != null)
            {
                statusLabel.text = string.IsNullOrWhiteSpace(entryFlow.LastStatus) && canEnter
                    ? L("ui.account.profile_final_review.ready", "READY TO ENTER")
                    : entryFlow.LastStatus ?? string.Empty;
            }
            if (summaryLabel != null)
            {
                var relations = character?.profileRelations;
                var relationSummary = relations == null || relations.Length == 0
                    ? L("ui.account.no_account_profile_relations", "No saved account profile relations")
                    : L("ui.account.profile_relation_count", "{0} saved account profile relation(s)")
                        .Replace("{0}", relations.Length.ToString());
                var appearance = character?.equippedPartIds == null || character.equippedPartIds.Length == 0
                    ? L("ui.account.default_appearance", "Default appearance")
                    : string.Join(", ", character.equippedPartIds);
                summaryLabel.text = L("ui.account.profile_review", "PROFILE REVIEW") + "\n\n" +
                                    L("ui.account.character", "Character: {0}").Replace("{0}", character?.displayName ?? "-") + "\n" +
                                    L("ui.account.template", "Template: {0}").Replace("{0}", character?.templateId ?? "-") + "\n" +
                                    L("ui.account.appearance", "Appearance: {0}").Replace("{0}", appearance) + "\n" +
                                    L("ui.account.world", "World: {0}").Replace("{0}", entryFlow.SelectedWorldId ?? "-") + "\n\n" +
                                    relationSummary;
            }
            if (enterWorldButton != null)
            {
                enterWorldButton.interactable = canEnter;
                var label = enterWorldButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = L("ui.account.profile_final_review.enter", "ENTER WORLD");
            }
            if (backButton != null)
            {
                var label = backButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = L("ui.account.back", "BACK");
            }
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

        private string ResolveSelectedWorldTitle()
        {
            var selectedWorldId = entryFlow?.SelectedWorldId;
            if (string.IsNullOrWhiteSpace(selectedWorldId)) return string.Empty;
            foreach (var world in entryFlow.Worlds)
            {
                if (world != null && string.Equals(world.worldId, selectedWorldId, StringComparison.Ordinal))
                    return string.IsNullOrWhiteSpace(world.title) ? selectedWorldId : world.title;
            }
            return selectedWorldId;
        }

        private string BuildPermissionLabel()
        {
            var role = entryFlow?.SelectedWorldRole;
            if (string.IsNullOrWhiteSpace(role))
                return L("ui.account.profile_final_review.permission_missing", "NO WORLD PERMISSION");
            var localizedRole = L("ui.account.role_" + role.ToLowerInvariant(), role);
            var access = entryFlow.CanEditSelectedWorld
                ? L("ui.account.profile_final_review.edit_allowed", "WORLD EDITING ENABLED")
                : L("ui.account.read_only", "Read only");
            return localizedRole + "  ·  " + access;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private void BindButtons()
        {
            if (enterWorldButton != null)
            {
                enterWorldButton.onClick.RemoveAllListeners();
                enterWorldButton.onClick.AddListener(() =>
                {
                    if (navigator != null) navigator.BeginWorldEntry();
                    else entryFlow?.EnterSelectedCharacterInCurrentWorld();
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
            if (partDatabase == null)
                partDatabase = FindAnyObjectByType<OntologyCharacterPartAdapter>()?.PartDatabase;
            if (appearanceSlots == null || appearanceSlots.Length == 0 || appearanceSlots.Any(slot => slot == null))
                appearanceSlots = GetComponentsInChildren<OntologyAppearanceReviewPartSlot>(true);
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
