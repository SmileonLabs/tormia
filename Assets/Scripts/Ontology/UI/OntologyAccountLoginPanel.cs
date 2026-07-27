using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    [DisallowMultipleComponent]
    public sealed class OntologyAccountLoginPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Text statusLabel,emailLabel,passwordLabel;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button createAccountButton;
        private bool busy;

        private void Awake(){Resolve();Bind();SetVisible(false);}
        private void OnEnable(){OntologyLanguagePackService.LanguageChanged+=Refresh;Refresh();}
        private void OnDisable()=>OntologyLanguagePackService.LanguageChanged-=Refresh;
        public void Open(){Resolve();SetVisible(true);Refresh();emailInput?.ActivateInputField();}
        public void Close()=>SetVisible(false);
        private void Bind(){loginButton?.onClick.RemoveAllListeners();loginButton?.onClick.AddListener(Login);createAccountButton?.onClick.RemoveAllListeners();createAccountButton?.onClick.AddListener(()=>navigator?.ShowAccountCreation());}
        private void Login(){if(busy||entryFlow==null)return;busy=true;Refresh();entryFlow.LoginAccount(emailInput?.text,passwordInput?.text,success=>{busy=false;Refresh();if(success)navigator?.ShowAfterAuthentication();});}
        private void Refresh()
        {
            if (statusLabel != null)
                statusLabel.text = "<size=36>" + OntologyLanguagePackService.Text("ui.account.login.title", "SIGN IN") +
                                   "</size>\n<size=18>" + OntologyLanguagePackService.Text("ui.account.login.prompt", "Enter your email and password.") + "</size>";

            if (emailLabel != null) emailLabel.text = OntologyLanguagePackService.Text("ui.account.field.email", "EMAIL");
            if (passwordLabel != null) passwordLabel.text = OntologyLanguagePackService.Text("ui.account.field.password", "PASSWORD");
            if (emailInput?.placeholder is TMP_Text emailPlaceholder)
                emailPlaceholder.text = OntologyLanguagePackService.Text("ui.account.field.email_placeholder", "name@example.com");
            if (passwordInput?.placeholder is TMP_Text passwordPlaceholder)
                passwordPlaceholder.text = OntologyLanguagePackService.Text("ui.account.field.password_placeholder", "Enter your password");

            if (loginButton != null)
            {
                loginButton.interactable = !busy;
                SetLabel(loginButton, OntologyLanguagePackService.Text("ui.account.login.submit", "SIGN IN"));
            }

            if (createAccountButton != null)
                SetLabel(createAccountButton, OntologyLanguagePackService.Text("ui.account.create.open", "CREATE ACCOUNT"));
        }
        private void Resolve(){entryFlow??=FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();navigator??=FindAnyObjectByType<OntologyAccountFlowNavigator>();panelGroup??=GetComponent<CanvasGroup>();emailInput??=transform.Find("EmailInput")?.GetComponent<TMP_InputField>();passwordInput??=transform.Find("PasswordInput")?.GetComponent<TMP_InputField>();statusLabel??=transform.Find("Text (TMP)")?.GetComponent<TMP_Text>();emailLabel??=transform.Find("EmailLabel")?.GetComponent<TMP_Text>();passwordLabel??=transform.Find("PasswordLabel")?.GetComponent<TMP_Text>();loginButton??=transform.Find("LoginButton")?.GetComponent<Button>();createAccountButton??=transform.Find("CreateAccountButton")?.GetComponent<Button>();}
        private void SetVisible(bool value){if(panelGroup==null)return;panelGroup.alpha=value?1:0;panelGroup.interactable=value;panelGroup.blocksRaycasts=value;}
        private static void SetLabel(Button button,string value){var label=button?.GetComponentInChildren<TMP_Text>(true);if(label!=null)label.text=value;}
    }
}
