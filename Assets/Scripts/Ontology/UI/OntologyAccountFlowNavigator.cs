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
        [SerializeField] private OntologyAccountEntryPanel accountEntryPanel;
        [SerializeField] private OntologyAccountCharacterSelectionPanel characterSelectionPanel;
        [SerializeField] private OntologyAccountAppearanceReviewPanel appearanceReviewPanel;
        [SerializeField] private OntologyAccountWorldSelectionPanel worldSelectionPanel;
        [SerializeField] private OntologyAccountProfileReviewPanel profileReviewPanel;

        private void Awake() => ResolvePanels();

        public void ShowAccountEntry()
        {
            ResolvePanels();
            accountEntryPanel?.Open();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
        }

        public void ShowCharacterSelection()
        {
            ResolvePanels();
            accountEntryPanel?.Close();
            characterSelectionPanel?.Open();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
        }

        public void ShowAppearanceReview()
        {
            ResolvePanels();
            accountEntryPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Open();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
        }

        public void ShowWorldSelection()
        {
            ResolvePanels();
            accountEntryPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Open();
            profileReviewPanel?.Close();
        }

        public void ShowProfileReview()
        {
            ResolvePanels();
            accountEntryPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Open();
        }

        public void CloseAll()
        {
            ResolvePanels();
            accountEntryPanel?.Close();
            characterSelectionPanel?.Close();
            appearanceReviewPanel?.Close();
            worldSelectionPanel?.Close();
            profileReviewPanel?.Close();
        }

        private void ResolvePanels()
        {
            if (accountEntryPanel == null) accountEntryPanel = FindAnyObjectByType<OntologyAccountEntryPanel>();
            if (characterSelectionPanel == null) characterSelectionPanel = FindAnyObjectByType<OntologyAccountCharacterSelectionPanel>();
            if (appearanceReviewPanel == null) appearanceReviewPanel = FindAnyObjectByType<OntologyAccountAppearanceReviewPanel>();
            if (worldSelectionPanel == null) worldSelectionPanel = FindAnyObjectByType<OntologyAccountWorldSelectionPanel>();
            if (profileReviewPanel == null) profileReviewPanel = FindAnyObjectByType<OntologyAccountProfileReviewPanel>();
        }
    }
}
