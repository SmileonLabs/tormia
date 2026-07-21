using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Owns only the WorldEditHUD visibility. The triple editor renders its contents.</summary>
    public sealed class OntologyRuntimeWorldEditorPanel : MonoBehaviour
    {
        [SerializeField] private OntologyRuntimeWorldEditorController editorController;
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private GameObject placementHud;
        private bool subscribed;

        public void Configure(OntologyRuntimeWorldEditorController controller)
        {
            Unsubscribe();
            editorController = controller;
            Bind();
            Subscribe();
            Refresh();
        }

        private void Awake()
        {
            if (editorController == null) editorController = FindAnyObjectByType<OntologyRuntimeWorldEditorController>();
            Bind();
        }

        private void OnEnable() { Subscribe(); Refresh(); }
        private void OnDisable() { Unsubscribe(); }

        private void Bind()
        {
            // References are authored in Inspector. Do not discover controls by name at
            // runtime: a designer can safely rename the hierarchy after reconnecting it.
        }

        /// <summary>
        /// One-time editor migration for scenes authored before the inspector bindings.
        /// This method is not part of the runtime UI path.
        /// </summary>
        [ContextMenu("Migrate Legacy World Edit Panel Bindings")]
        public void MigrateLegacyHierarchyBindings()
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Migrate World Edit panel bindings");
            hudRoot = transform.Find("WorldEditHUD")?.gameObject;
            placementHud = transform.Find("ObjectPlacementHUD")?.gameObject;
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void Subscribe()
        {
            if (subscribed || editorController == null) return;
            editorController.StateChanged += Refresh;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || editorController == null) return;
            editorController.StateChanged -= Refresh;
            subscribed = false;
        }

        private void Refresh()
        {
            if (hudRoot != null && editorController != null) hudRoot.SetActive(editorController.IsOntologyOpen);
            if (placementHud != null && editorController != null && editorController.IsEditing) placementHud.SetActive(false);
        }
    }
}
