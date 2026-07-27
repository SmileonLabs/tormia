using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Keeps a hierarchy-authored UI panel visible in the Unity Editor for
    /// layout work without changing its hidden runtime start state.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class OntologyUiEditorPreview : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private bool previewInEditor = true;

        private void OnEnable() => ApplyEditorPreview();
        private void OnValidate() => ApplyEditorPreview();

        private void Update()
        {
            if (!Application.isPlaying) ApplyEditorPreview();
        }

        private void ApplyEditorPreview()
        {
            if (Application.isPlaying || !previewInEditor) return;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) return;
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = false;
        }
    }
}
