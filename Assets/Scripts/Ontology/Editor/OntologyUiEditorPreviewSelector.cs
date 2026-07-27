#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tormia.Ontology.Core.Editor
{
    /// <summary>
    /// Switches the edit-mode onboarding preview without rebuilding or moving
    /// any authored UI. This keeps overlapping onboarding panels out of the way.
    /// </summary>
    public static class OntologyUiEditorPreviewSelector
    {
        [MenuItem("Tormia/UI/Preview/Login Panel")]
        public static void ShowLogin() => Show("AccountLoginPanel");

        [MenuItem("Tormia/UI/Preview/Account Creation Panel")]
        public static void ShowAccountCreation() => Show("AccountCreationPanel");

        [MenuItem("Tormia/UI/Preview/Character Creation Panel")]
        public static void ShowCharacterCreation() => Show(
            "AccountCharacterAppearancePanel",
            "AccountCharacterCreationPanel");

        [MenuItem("Tormia/UI/Preview/Character Selection Panel")]
        public static void ShowCharacterSelection() => Show("AccountCharacterSelectionPanel");

        [MenuItem("Tormia/UI/Preview/Appearance Review Panel")]
        public static void ShowAppearanceReview() => ShowCharacterSelection();

        [MenuItem("Tormia/UI/Preview/Profile Final Review Panel")]
        public static void ShowProfileFinalReview() => Show("AccountProfileReviewPanel");

        [MenuItem("Tormia/UI/Preview/World Entry Loading Panel")]
        public static void ShowWorldEntryLoading() => Show("WorldEntryLoadingPanel");

        [MenuItem("Tormia/UI/Preview/World Selection Panel")]
        public static void ShowWorldSelection() => Show("AccountWorldSelectionPanel");

        [MenuItem("Tormia/UI/Preview/World Creation Panel")]
        public static void ShowWorldCreation() => Show("AccountWorldCreationPanel");

        [MenuItem("Tormia/UI/Preview/Quests & Actions Panel")]
        public static void ShowQuestActionPanel() => Show("QuestActionPanel");

        [MenuItem("Tormia/UI/Preview/Runtime Character Customization Panel")]
        public static void ShowRuntimeCharacterCustomization() =>
            Show("OntologyCharacterCustomizationPanel");

        private static void Show(params string[] targetNames)
        {
            foreach (var preview in Resources.FindObjectsOfTypeAll<OntologyUiEditorPreview>())
            {
                if (preview == null || !preview.gameObject.scene.IsValid()) continue;
                var enabled = targetNames.Contains(preview.gameObject.name);
                var serialized = new SerializedObject(preview);
                serialized.FindProperty("previewInEditor").boolValue = enabled;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var group = preview.GetComponent<CanvasGroup>();
                if (group == null) continue;
                group.alpha = enabled ? 1f : 0f;
                group.interactable = enabled;
                group.blocksRaycasts = false;
                EditorUtility.SetDirty(group);
            }

            // Several older onboarding panels predate OntologyUiEditorPreview
            // and keep alpha=1 in the authored scene. Hide every non-target
            // panel under the onboarding canvas so the selected screen is
            // actually editable without unrelated cards stacked over it.
            var canvas = Resources.FindObjectsOfTypeAll<Canvas>()
                .FirstOrDefault(candidate => candidate != null && candidate.gameObject.scene.IsValid() && candidate.gameObject.name == "OntologyGameCanvas")
                ?.transform;
            if (canvas != null)
            {
                foreach (var group in Resources.FindObjectsOfTypeAll<CanvasGroup>())
                {
                    if (group == null || !group.gameObject.scene.IsValid()) continue;
                    if (!group.transform.IsChildOf(canvas)) continue;
                    if (!group.gameObject.name.EndsWith("Panel")) continue;
                    var enabled = targetNames.Contains(group.gameObject.name);
                    group.alpha = enabled ? 1f : 0f;
                    group.interactable = enabled;
                    group.blocksRaycasts = false;
                    if (enabled)
                        SceneVisibilityManager.instance.Show(group.gameObject, true);
                    else
                        SceneVisibilityManager.instance.Hide(group.gameObject, true);
                    EditorUtility.SetDirty(group);
                }
            }

            foreach (var presenter in Resources.FindObjectsOfTypeAll<OntologyAccountCharacterSelectionPreviewPresenter>())
                if (presenter != null && presenter.gameObject.scene.IsValid()) presenter.RefreshPreviews();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            Canvas.ForceUpdateCanvases();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
#endif
