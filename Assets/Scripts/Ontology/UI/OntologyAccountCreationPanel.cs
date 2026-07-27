using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Scene-authored email/password account registration panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountCreationPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField displayNameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text emailLabel;
        [SerializeField] private TMP_Text displayNameLabel;
        [SerializeField] private TMP_Text passwordLabel;
        [SerializeField] private Button createButton;
        [SerializeField] private Button backButton;
        [SerializeField] private bool startsVisible;
        private bool creating;

        private void Awake()
        {
            Resolve();
            Bind();
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
            Resolve();
            SetVisible(true);
            Refresh();
            emailInput?.ActivateInputField();
        }

        public void Close() => SetVisible(false);

        private void Bind()
        {
            if (createButton != null)
            {
                createButton.onClick.RemoveAllListeners();
                createButton.onClick.AddListener(Create);
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(() => navigator?.ShowLogin());
            }
        }

        private void Create()
        {
            if (creating || entryFlow == null) return;
            creating = true;
            Refresh();
            entryFlow.RegisterAccount(emailInput == null ? string.Empty : emailInput.text,
                displayNameInput == null ? string.Empty : displayNameInput.text,
                passwordInput == null ? string.Empty : passwordInput.text, success =>
                {
                    creating = false;
                    Refresh();
                    if (success) navigator?.ShowCharacterCreation();
                });
        }

        private void Refresh()
        {
            if (statusLabel != null)
                statusLabel.text = "<size=36>" + L("ui.account.create.title", "CREATE ACCOUNT") + "</size>\n<size=18>" +
                                   L("ui.account.create.prompt", "Enter email, display name and a password of at least 10 characters.") + "</size>";
            if (emailLabel != null) emailLabel.text = L("ui.account.field.email", "EMAIL");
            if (displayNameLabel != null) displayNameLabel.text = L("ui.account.field.display_name", "DISPLAY NAME");
            if (passwordLabel != null) passwordLabel.text = L("ui.account.field.password", "PASSWORD");
            if (emailInput?.placeholder is TMP_Text emailPlaceholder)
                emailPlaceholder.text = L("ui.account.field.email_placeholder", "name@example.com");
            if (displayNameInput?.placeholder is TMP_Text displayNamePlaceholder)
                displayNamePlaceholder.text = L("ui.account.create.display_name_placeholder", "Your in-game name");
            if (passwordInput?.placeholder is TMP_Text passwordPlaceholder)
                passwordPlaceholder.text = L("ui.account.create.password_placeholder", "At least 10 characters");
            if (createButton != null)
            {
                createButton.interactable = !creating;
                SetButtonLabel(createButton, L("ui.account.create.create", "CREATE ACCOUNT"));
            }
            if (backButton != null) SetButtonLabel(backButton, L("ui.account.back", "BACK"));
        }

        private void Resolve()
        {
            entryFlow ??= FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            navigator ??= FindAnyObjectByType<OntologyAccountFlowNavigator>();
            panelGroup ??= GetComponent<CanvasGroup>();
            emailInput ??= transform.Find("EmailInput")?.GetComponent<TMP_InputField>();
            displayNameInput ??= transform.Find("DisplayNameInput")?.GetComponent<TMP_InputField>();
            passwordInput ??= transform.Find("PasswordInput")?.GetComponent<TMP_InputField>();
            statusLabel ??= transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();
            emailLabel ??= transform.Find("EmailLabel")?.GetComponent<TMP_Text>();
            displayNameLabel ??= transform.Find("DisplayNameLabel")?.GetComponent<TMP_Text>();
            passwordLabel ??= transform.Find("PasswordLabel")?.GetComponent<TMP_Text>();
            createButton ??= transform.Find("CreateAccountButton")?.GetComponent<Button>();
            backButton ??= transform.Find("BackButton")?.GetComponent<Button>();
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
