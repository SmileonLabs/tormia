using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Hierarchy-authored navigation for the account entry flow.
    /// Panels are assigned in the Inspector; no panel is created or positioned
    /// at runtime. Button events may call the public methods directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountFlowNavigator : MonoBehaviour
    {
        [SerializeField] private OntologyAccountLoginPanel accountLoginPanel;
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private OntologyAccountCreationPanel accountCreationPanel;
        [SerializeField] private OntologyAccountCharacterCreationPanel characterCreationPanel;
        [SerializeField] private OntologyAccountWorldCreationPanel worldCreationPanel;
        [SerializeField] private OntologyAccountCharacterSelectionPanel characterSelectionPanel;
        [SerializeField] private OntologyAccountAppearanceReviewPanel appearanceReviewPanel;
        [SerializeField] private OntologyAccountWorldSelectionPanel worldSelectionPanel;
        [SerializeField] private OntologyAccountProfileReviewPanel profileReviewPanel;
        [SerializeField] private OntologyWorldEntryLoadingPanel worldEntryLoadingPanel;
        [SerializeField] private OntologyWorldRuntimeActivationGate runtimeActivationGate;

        private void Awake()
        {
            ResolvePanels();
            if (runtimeActivationGate == null)
                runtimeActivationGate = GetComponent<OntologyWorldRuntimeActivationGate>();
            if (runtimeActivationGate == null)
                runtimeActivationGate = gameObject.AddComponent<OntologyWorldRuntimeActivationGate>();
        }
        private void Start(){if(entryFlow!=null&&entryFlow.HasStoredSession)entryFlow.RestoreAccountSession(success=>{if(success)ShowAfterAuthentication();else ShowLogin();});else ShowLogin();}

        public void ShowLogin(){ResolvePanels();CloseAll();Activate(accountLoginPanel)?.Open();}
        public void ShowAfterAuthentication(){ResolvePanels();if(entryFlow!=null&&entryFlow.Characters.Count==0)ShowCharacterCreation();else ShowCharacterSelection();}

        public void ShowCharacterSelection()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            characterCreationPanel?.Close();
            worldCreationPanel?.Close();
            Activate(characterSelectionPanel)?.Open();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
        }

        public void ShowCharacterCreation()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            worldCreationPanel?.Close();
            profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
            Activate(characterCreationPanel)?.Open();
        }

        public void BackFromCharacterCreation()
        {
            ResolvePanels();
            if (entryFlow != null && entryFlow.Characters.Count > 0)
            {
                ShowCharacterSelection();
                return;
            }

            entryFlow?.LogoutAccount();
            ShowLogin();
        }

        public void ShowAppearanceReview()
        {
            // The selected character's equipped appearance is now presented in
            // AccountCharacterSelectionPanel. Keep this route as a compatibility
            // alias so older button bindings proceed to the next real step.
            ShowWorldSelection();
        }

        public void ShowWorldSelection()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            characterCreationPanel?.Close();
            worldCreationPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            Activate(worldSelectionPanel)?.Open();
            profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
        }

        public void ShowProfileReview()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            characterCreationPanel?.Close();
            worldCreationPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            Activate(profileReviewPanel)?.Open();
            worldEntryLoadingPanel?.Close();
        }

        public void BeginWorldEntry()
        {
            ResolvePanels();
            CloseAll();
            Activate(worldEntryLoadingPanel)?.BeginEntry();
        }

        public void ShowWorldEntryLoading()
        {
            ResolvePanels();
            CloseAll();
            Activate(worldEntryLoadingPanel)?.Open();
        }

        public void CloseAll()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            characterCreationPanel?.Close();
            worldCreationPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
        }

        private void ResolvePanels()
        {
            if (accountLoginPanel == null) accountLoginPanel = FindAnyObjectByType<OntologyAccountLoginPanel>(FindObjectsInactive.Include);
            if (entryFlow == null) entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(FindObjectsInactive.Include);
            if (accountCreationPanel == null) accountCreationPanel = FindAnyObjectByType<OntologyAccountCreationPanel>(FindObjectsInactive.Include);
            if (characterCreationPanel == null) characterCreationPanel = FindAnyObjectByType<OntologyAccountCharacterCreationPanel>(FindObjectsInactive.Include);
            if (worldCreationPanel == null) worldCreationPanel = FindAnyObjectByType<OntologyAccountWorldCreationPanel>(FindObjectsInactive.Include);
            if (characterSelectionPanel == null) characterSelectionPanel = FindAnyObjectByType<OntologyAccountCharacterSelectionPanel>(FindObjectsInactive.Include);
            if (appearanceReviewPanel == null) appearanceReviewPanel = FindAnyObjectByType<OntologyAccountAppearanceReviewPanel>(FindObjectsInactive.Include);
            if (worldSelectionPanel == null) worldSelectionPanel = FindAnyObjectByType<OntologyAccountWorldSelectionPanel>(FindObjectsInactive.Include);
            if (profileReviewPanel == null) profileReviewPanel = FindAnyObjectByType<OntologyAccountProfileReviewPanel>(FindObjectsInactive.Include);
            if (worldEntryLoadingPanel == null) worldEntryLoadingPanel = FindAnyObjectByType<OntologyWorldEntryLoadingPanel>(FindObjectsInactive.Include);
        }

        public void ShowWorldCreation()
        {
            ResolvePanels();
            characterCreationPanel?.Close(); characterSelectionPanel?.Close();
            accountLoginPanel?.Close();
            accountCreationPanel?.Close();
            appearanceReviewPanel?.Close(); worldSelectionPanel?.Close(); profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
            Activate(worldCreationPanel)?.Open();
        }

        public void ShowAccountCreation()
        {
            ResolvePanels();
            accountLoginPanel?.Close();
            characterCreationPanel?.Close();
            worldCreationPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
            worldEntryLoadingPanel?.Close();
            Activate(accountCreationPanel)?.Open();
        }

        private static T Activate<T>(T panel) where T : Component
        {
            if (panel != null && !panel.gameObject.activeSelf)
                panel.gameObject.SetActive(true);
            return panel;
        }
    }
}
