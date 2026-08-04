using System;
using System.Collections.Generic;
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
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text helperLabel;
        [SerializeField] private TMP_Text characterHeadingLabel;
        [SerializeField] private TMP_Text characterNameLabel;
        [SerializeField] private TMP_Text templateHeadingLabel;
        [SerializeField] private TMP_Text templateValueLabel;
        [SerializeField] private TMP_Text equippedPartsHeadingLabel;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private OntologyAppearanceReviewPartSlot[] partSlots = Array.Empty<OntologyAppearanceReviewPartSlot>();
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
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
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

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
            var character = entryFlow?.CurrentCharacter;
            LocalizeStaticLabels();
            if (characterNameLabel != null) characterNameLabel.text = character?.displayName ?? "-";
            if (templateValueLabel != null)
                templateValueLabel.text = character == null
                    ? "-"
                    : OntologyLanguagePackService.CharacterTemplateName(
                        character.templateId);
            BindPartSlots(character);
            if (summaryLabel != null)
            {
                var parts = character?.equippedPartIds;
                var visibleCount = CountVisibleParts(parts);
                summaryLabel.text = visibleCount > partSlots.Length
                    ? L("ui.account.appearance_review.more", "+{0} more equipped part(s)")
                        .Replace("{0}", (visibleCount - partSlots.Length).ToString())
                    : string.Empty;
            }

            if (continueButton != null)
                continueButton.interactable = character != null;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);

        private void LocalizeStaticLabels()
        {
            if (titleLabel != null) titleLabel.text = L("ui.account.appearance_review", "APPEARANCE REVIEW");
            if (helperLabel != null) helperLabel.text = L("ui.account.appearance_review.prompt", "Review your character before continuing.");
            if (characterHeadingLabel != null) characterHeadingLabel.text = L("ui.account.appearance_review.character", "CHARACTER");
            if (templateHeadingLabel != null) templateHeadingLabel.text = L("ui.account.appearance_review.template", "TEMPLATE");
            if (equippedPartsHeadingLabel != null) equippedPartsHeadingLabel.text = L("ui.account.appearance_review.parts", "EQUIPPED PARTS");
            SetButtonLabel(backButton, L("ui.account.back", "BACK"));
            SetButtonLabel(continueButton, L("ui.account.appearance_review.continue", "CONTINUE"));
        }

        private void BindPartSlots(OntologyAuthorityPlayerCharacter character)
        {
            var definitions = ResolveVisibleParts(character?.equippedPartIds);
            for (var index = 0; index < partSlots.Length; index++)
            {
                var slot = partSlots[index];
                if (slot == null) continue;
                slot.Bind(index < definitions.Count ? definitions[index] : null);
            }
        }

        private List<OntologyCharacterPartDefinition> ResolveVisibleParts(IReadOnlyList<string> equippedIds)
        {
            var result = new List<OntologyCharacterPartDefinition>();
            if (partDatabase?.Definitions == null) return result;
            var requested = equippedIds == null || equippedIds.Count == 0
                ? null
                : new HashSet<string>(equippedIds, StringComparer.Ordinal);
            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || !definition.visibleInCustomization) continue;
                if (requested == null ? definition.enabledByDefault : requested.Contains(definition.partId))
                    result.Add(definition);
            }
            return result;
        }

        private int CountVisibleParts(IReadOnlyList<string> equippedIds) => ResolveVisibleParts(equippedIds).Count;

        private static void SetButtonLabel(Button button, string value)
        {
            var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (label != null) label.text = value;
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
            if (partDatabase == null)
                partDatabase = FindAnyObjectByType<OntologyCharacterPartAdapter>()?.PartDatabase;

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

    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class OntologyAppearanceReviewPartSlot : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text label;
        [SerializeField, Min(0f)] private float thumbnailOverscan = 10f;

        private void OnEnable() => ResolveBindings(true);

#if UNITY_EDITOR
        private void OnValidate() => ResolveBindings(false);
#endif

        public void Bind(OntologyCharacterPartDefinition definition)
        {
            ResolveBindings(true);
            var available = definition != null;
            gameObject.SetActive(available);
            if (!available) return;
            if (icon != null)
            {
                icon.sprite = definition.icon;
                icon.enabled = definition.icon != null;
                icon.preserveAspect = true;
            }
            if (label != null)
                label.text = OntologyLanguagePackService.CharacterPartName(
                    definition.partId,
                    string.IsNullOrWhiteSpace(definition.displayName)
                        ? definition.slot ?? definition.partId
                        : definition.displayName);
        }

        private void ResolveBindings(bool createMask)
        {
            if (icon == null)
            {
                icon = transform.Find("IconFrame/PartIcon")?.GetComponent<Image>()
                    ?? transform.Find("PartCircle/PartIcon")?.GetComponent<Image>()
                    ?? transform.Find("ThumbnailMask/PartIcon")?.GetComponent<Image>();
            }
            if (icon != null && createMask)
            {
                // Appearance thumbnails come from mixed source images: some
                // contain opaque square preview backgrounds while others are
                // transparent. Always stencil the image through its authored
                // circular parent so a square source can never leak outside.
                var maskRoot = icon.transform.parent;
                if (maskRoot != null)
                {
                    var maskGraphic = maskRoot.GetComponent<Image>();
                    if (maskGraphic != null)
                    {
                        var mask = maskRoot.GetComponent<Mask>();
                        if (mask == null)
                            mask = maskRoot.gameObject.AddComponent<Mask>();
                        if (mask != null)
                        {
                            mask.enabled = true;
                            mask.showMaskGraphic = true;
                        }
                    }
                    var iconRect = icon.rectTransform;
                    iconRect.anchorMin = Vector2.zero;
                    iconRect.anchorMax = Vector2.one;
                    iconRect.pivot = new Vector2(0.5f, 0.5f);
                    iconRect.offsetMin = new Vector2(-thumbnailOverscan, -thumbnailOverscan);
                    iconRect.offsetMax = new Vector2(thumbnailOverscan, thumbnailOverscan);
                }
                icon.maskable = true;
            }
            if (label == null)
                label = transform.Find("PartLabel")?.GetComponent<TMP_Text>();
        }
    }
}
