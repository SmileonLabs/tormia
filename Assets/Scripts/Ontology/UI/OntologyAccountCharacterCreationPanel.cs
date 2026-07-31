using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Binder for the scene-authored account character creation panel. It owns
    /// no visual layout and sends account-profile data through the Authority
    /// entry flow only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountCharacterCreationPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_InputField displayNameInput;
        [SerializeField] private TMP_Dropdown templateDropdown;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private Button createButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button customizeButton;
        [SerializeField] private OntologyCharacterCustomizationPanel appearancePanel;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text appearanceHintLabel;
        [SerializeField] private TMP_Text stepLabel;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private string[] templateIds = { "player" };
        [SerializeField] private bool startsVisible;

        private bool creating;

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            OntologyLanguagePackService.LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable() => OntologyLanguagePackService.LanguageChanged -= Refresh;

        public void Open()
        {
            ResolveDependencies();
            appearancePanel?.OpenEmbeddedForAccountCharacterCreation();
            if (appearancePanel != null)
                appearancePanel.transform.SetAsLastSibling();
            transform.SetAsLastSibling();
            SetVisible(true);
            Refresh();
            displayNameInput?.ActivateInputField();
        }

        public void Close()
        {
            SetVisible(false);
            appearancePanel?.CloseEmbeddedForAccountCharacterCreation();
        }

        public void Refresh()
        {
            if (templateDropdown != null)
            {
                templateDropdown.ClearOptions();
                var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
                foreach (var templateId in templateIds)
                {
                    if (!string.IsNullOrWhiteSpace(templateId))
                        options.Add(new TMP_Dropdown.OptionData(
                            OntologyLanguagePackService.CharacterTemplateName(
                                templateId)));
                }
                templateDropdown.AddOptions(options);
                templateDropdown.interactable = options.Count > 1 && !creating;
                templateDropdown.RefreshShownValue();
            }

            if (statusLabel != null)
                statusLabel.text = "<size=31><b>" +
                                   L("ui.account.character_create.title", "CREATE YOUR CHARACTER") +
                                   "</b></size>\n<size=17>" +
                                   L("ui.account.character_create.prompt", "Choose a look that feels like you.") +
                                   "</size>";
            if (stepLabel != null)
                stepLabel.text = L("ui.account.character_create.step", "FIRST ADVENTURE · CHARACTER SETUP");
            if (nameLabel != null)
                nameLabel.text = L("ui.account.character_create.name", "CHARACTER NAME");
            if (appearanceHintLabel != null)
                appearanceHintLabel.text = L("ui.account.character_create.appearance_hint", "Select a part to try it on instantly.");
            if (displayNameInput != null && displayNameInput.placeholder is TMP_Text placeholder)
                placeholder.text = L("ui.account.character_create.name_placeholder", "Enter a memorable name");
            if (createButton != null)
            {
                createButton.interactable = !creating && entryFlow?.CurrentAccount != null;
                SetButtonLabel(createButton, L("ui.account.character_create.create", "CREATE"));
            }
            if (backButton != null) SetButtonLabel(backButton, L("ui.account.back", "BACK"));
            if (customizeButton != null)
                customizeButton.gameObject.SetActive(false);
        }

        private void BindButtons()
        {
            if (createButton != null)
            {
                createButton.onClick.RemoveAllListeners();
                createButton.onClick.AddListener(CreateCharacter);
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() => navigator?.BackFromCharacterCreation());
            }
            if (customizeButton != null)
            {
                customizeButton.onClick.RemoveAllListeners();
                customizeButton.onClick.AddListener(OpenCustomization);
            }
        }

        private void CreateCharacter()
        {
            if (creating || entryFlow == null) return;
            var templateId = templateIds != null && templateIds.Length > 0
                ? templateIds[Mathf.Clamp(templateDropdown == null ? 0 : templateDropdown.value, 0, templateIds.Length - 1)]
                : string.Empty;
            creating = true;
            Refresh();
            entryFlow.CreateAccountCharacter(displayNameInput == null ? string.Empty : displayNameInput.text, templateId, created =>
            {
                creating = false;
                Refresh();
                if (created != null) navigator?.ShowCharacterSelection();
            });
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
            if (navigator == null) navigator = FindAnyObjectByType<OntologyAccountFlowNavigator>();
            if (appearancePanel == null)
            {
                var appearanceObject = GameObject.Find("AccountCharacterAppearancePanel");
                if (appearanceObject != null)
                    appearancePanel = appearanceObject.GetComponent<OntologyCharacterCustomizationPanel>();
            }
            if (displayNameInput == null)
            {
                displayNameInput = transform
                    .Find("CharacterIdentityCard/CharacterNameInput")
                    ?.GetComponent<TMP_InputField>();
                displayNameInput ??= GetComponentInChildren<TMP_InputField>(true);
            }
            if (displayNameInput != null)
            {
                displayNameInput.contentType = TMP_InputField.ContentType.Standard;
                displayNameInput.lineType = TMP_InputField.LineType.SingleLine;
                displayNameInput.characterValidation = TMP_InputField.CharacterValidation.None;
                displayNameInput.characterLimit = 0;
                displayNameInput.readOnly = false;
                displayNameInput.textViewport ??= displayNameInput.transform
                    .Find("Text Area") as RectTransform;
                displayNameInput.textComponent ??= displayNameInput.transform
                    .Find("Text Area/Text")
                    ?.GetComponent<TMP_Text>();
                displayNameInput.placeholder ??= displayNameInput.transform
                    .Find("Text Area/Placeholder")
                    ?.GetComponent<TMP_Text>();
            }
            if (statusLabel == null)
            {
                var statusTransform = transform.Find("Text (TMP)");
                if (statusTransform != null) statusLabel = statusTransform.GetComponent<TMP_Text>();
            }
            if (createButton == null)
            {
                var createTransform = transform.Find("ContinueToWorldButton");
                createTransform ??= transform.Find("CharacterCreationFooter/ContinueToWorldButton");
                if (createTransform != null) createButton = createTransform.GetComponent<Button>();
            }
            if (backButton == null)
            {
                var backTransform = transform.Find("BackButton");
                backTransform ??= transform.Find("CharacterCreationFooter/BackButton");
                if (backTransform != null) backButton = backTransform.GetComponent<Button>();
            }
            if (customizeButton == null)
            {
                var customizeTransform = transform.Find("CustomizeAppearanceButton");
                if (customizeTransform != null) customizeButton = customizeTransform.GetComponent<Button>();
            }
            nameLabel ??= transform.Find("CharacterIdentityCard/CharacterNameLabel")?.GetComponent<TMP_Text>();
            appearanceHintLabel ??= transform.Find("AppearanceGuidance/AppearanceHintLabel")?.GetComponent<TMP_Text>();
            stepLabel ??= transform.Find("CharacterCreationHeading/StepLabel")?.GetComponent<TMP_Text>();
        }

        private void OpenCustomization()
        {
            var appearancePanel = GameObject.Find("AccountCharacterAppearancePanel");
            var customization = appearancePanel == null
                ? FindAnyObjectByType<OntologyCharacterCustomizationPanel>()
                : appearancePanel.GetComponent<OntologyCharacterCustomizationPanel>();
            if (customization == null) return;
            Close();
            customization.OpenForAccountCharacterCreation(Open);
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private static string L(string key, string fallback) => OntologyLanguagePackService.Text(key, fallback);
        private static void SetButtonLabel(Button button, string value)
        {
            var label = button == null ? null : button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = value;
        }
    }
}
