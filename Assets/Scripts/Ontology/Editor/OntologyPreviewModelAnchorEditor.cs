using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CustomEditor(typeof(OntologyPreviewModelAnchor))]
    public sealed class OntologyPreviewModelAnchorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var anchor = (OntologyPreviewModelAnchor)target;
            anchor.Catalog = (OntologyPlaceableCatalog)EditorGUILayout.ObjectField("Catalog", anchor.Catalog, typeof(OntologyPlaceableCatalog), false);
            if (anchor.Catalog == null) { EditorGUILayout.HelpBox("Assign the placeable catalog.", MessageType.Info); return; }
            var options = new string[anchor.Catalog.Definitions.Count];
            var selected = 0;
            for (var i = 0; i < options.Length; i++) { options[i] = anchor.Catalog.Definitions[i].EffectiveDisplayName; if (anchor.Catalog.Definitions[i].definitionId == anchor.DefinitionId) selected = i; }
            selected = EditorGUILayout.Popup("Catalog Object", selected, options);
            if (options.Length > 0) anchor.DefinitionId = anchor.Catalog.Definitions[selected].definitionId;
            var definition = Definition(anchor);
            if (definition != null)
            {
                EditorGUI.BeginChangeCheck();
                var previewPrefab = (GameObject)EditorGUILayout.ObjectField("Preview Prefab", definition.previewPrefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(anchor.Catalog, "Set preview prefab");
                    definition.previewPrefab = previewPrefab;
                    EditorUtility.SetDirty(anchor.Catalog);
                }
            }
            EditorGUILayout.ObjectField("Preview Model", anchor.PreviewModel, typeof(Transform), true);
            EditorGUILayout.Space();
            if (GUILayout.Button("Load Selected Model")) Load(anchor);
            using (new EditorGUI.DisabledScope(anchor.PreviewModel == null))
            {
                if (GUILayout.Button("Apply Current Adjustment to Preview")) Apply(anchor);
                if (GUILayout.Button("Clear Preview Adjustment")) ResetTransform(anchor);
            }
            EditorGUILayout.HelpBox("The model is always automatically centered first. Select PreviewModel and make only small Position, Rotation, or Scale adjustments. Apply stores those adjustments, so the same appearance is used in-game.", MessageType.None);
            if (GUI.changed) { EditorUtility.SetDirty(anchor); }
        }

        private static OntologyPlaceableDefinition Definition(OntologyPreviewModelAnchor anchor) => anchor.Catalog?.Find(anchor.DefinitionId);
        private static void Load(OntologyPreviewModelAnchor anchor)
        {
            var definition = Definition(anchor); if (definition == null) return;
            if (anchor.PreviewModel != null) DestroyImmediate(anchor.PreviewModel.gameObject);
            var prefab = definition.previewPrefab != null ? definition.previewPrefab : definition.prefab;
            if (prefab == null) return;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor.transform);
            model.name = "PreviewModel_" + definition.definitionId;
            OntologyPlaceablePreviewRenderer.CenterAndFrame(anchor.transform, model, 2.4f);
            anchor.SetAutomaticBaseline(model.transform);
            if (definition.useAuthoredPreviewTransform)
                OntologyPlaceablePreviewRenderer.ApplyAuthoredAdjustment(anchor.transform, model, definition.previewLocalPosition, definition.previewLocalEulerAngles, definition.previewLocalScale);
            anchor.PreviewModel = model.transform; EditorUtility.SetDirty(anchor);
        }
        private static void Apply(OntologyPreviewModelAnchor anchor)
        {
            var definition = Definition(anchor); if (definition == null || anchor.PreviewModel == null) return;
            Undo.RecordObject(anchor.Catalog, "Apply preview adjustment");
            definition.useAuthoredPreviewTransform = true;
            // The anchor model is loaded already centered. Store only the user's difference
            // from that baseline, so every preview remains automatically framed.
            definition.previewLocalPosition = anchor.PreviewModel.localPosition - anchor.AutomaticLocalPosition;
            definition.previewLocalEulerAngles = anchor.PreviewModel.localEulerAngles;
            definition.previewLocalScale = Divide(anchor.PreviewModel.localScale, anchor.AutomaticLocalScale);
            EditorUtility.SetDirty(anchor.Catalog); AssetDatabase.SaveAssets();
        }

        private static Vector3 Divide(Vector3 value, Vector3 divisor) => new(
            Mathf.Abs(divisor.x) < 0.0001f ? 1f : value.x / divisor.x,
            Mathf.Abs(divisor.y) < 0.0001f ? 1f : value.y / divisor.y,
            Mathf.Abs(divisor.z) < 0.0001f ? 1f : value.z / divisor.z);
        private static void ResetTransform(OntologyPreviewModelAnchor anchor)
        {
            var definition = Definition(anchor); if (definition == null) return;
            Undo.RecordObject(anchor.Catalog, "Clear preview adjustment"); definition.useAuthoredPreviewTransform = false; definition.previewLocalPosition = Vector3.zero; definition.previewLocalEulerAngles = Vector3.zero; definition.previewLocalScale = Vector3.one; EditorUtility.SetDirty(anchor.Catalog); AssetDatabase.SaveAssets();
        }
    }
}
